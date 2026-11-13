// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> Outcome of a MeshSweepCollision </summary>
    public enum MeshSweepStatus
    {
        /// <summary> Compute has not run (the zero value, so that an uncomputed sweep proves nothing) </summary>
        NotComputed,
        /// <summary> proven: no two primitives come closer than MinSeparation in [0, TimeLimit] </summary>
        NoCollision,
        /// <summary> some pair may touch: EarliestTimeOfImpact is a lower bound of the first contact, EarliestNearContactTime a time of a near contact </summary>
        Collision,
        /// <summary>
        /// a pair hit MaxIterations before EarliestNearContactTime: EarliestTimeOfImpact is still a lower bound of the
        /// first contact, but there may be no near contact up to EarliestNearContactTime
        /// </summary>
        IterationLimit,
        /// <summary>
        /// a mesh is null, an end position array is too short, a position is not finite or above 2^1000 in magnitude, or
        /// the options are invalid
        /// </summary>
        InvalidInput
    }

    /// <summary> The kind of a primitive pair of a sweep </summary>
    public enum SweepContactKind
    {
        /// <summary> First is a vertex, Second a triangle </summary>
        VertexTriangle,
        /// <summary> First and Second are edges </summary>
        EdgeEdge
    }

    /// <summary> The result of one primitive pair of a sweep </summary>
    public struct SweepContact
    {
        /// <summary> see SweepContactKind </summary>
        public SweepContactKind Kind;
        /// <summary> vertex or edge id (in the mesh given by FirstOfMeshA) </summary>
        public int First;
        /// <summary> triangle or edge id, in the other mesh (in the same mesh for a self sweep) </summary>
        public int Second;
        /// <summary> true if First is a primitive of mesh A (always for a self sweep) </summary>
        public bool FirstOfMeshA;
        /// <summary> the pair's query result </summary>
        public CcdResult Result;
    }

    /// <summary>
    /// Continuous collision detection of deforming meshes: every vertex moves linearly from its position in the mesh
    /// (t = 0) to its end position (t = 1). Finds the pairs of primitives (vertex-triangle, edge-edge) that may come
    /// closer than Options.MinSeparation, and the earliest time of impact, conservatively: NoCollision is a proof
    /// (ContinuousCollision). A self sweep (one mesh) skips the pairs that share a vertex; A against B tests every
    /// vertex of each against the triangles of the other and every edge pair.
    ///
    /// Broad phase: the box of each primitive's start and end positions (the primitive stays in it under linear
    /// motion), widened by half the minimum separation with outward rounding, in a closed, unpadded box tree (no
    /// overlapping pair is missed). Narrow phase: ContinuousCollision per candidate pair; each pair is computed
    /// independently. Both run in parallel over fixed jobs (blocks of query primitives, self edge-pair tasks) in batches of
    /// about two million candidate pairs; the jobs and batches depend only on the input and the outputs are merged in
    /// job order and sorted, so the result does not depend on the thread schedule, and the memory is that of a batch and
    /// of the kept pairs.
    /// </summary>
    public class MeshSweepCollision
    {
        readonly DMesh3 meshA, meshB;
        Vector3d[] endA, endB;
        readonly bool self;

        /// <summary> options of every pair query </summary>
        public CcdOptions Options = new CcdOptions();
        /// <summary>
        /// the broad phase splits the step into this many equal sub-steps (a power of two, 1 to 64; 4 by default): a pair
        /// of overlapping whole-step boxes is a candidate only if the boxes of its primitives also overlap in some
        /// sub-step. Fewer candidates for large motions; every
        /// candidate is still solved over the whole step, so a pair's result does not depend on SubSteps. A pair is left
        /// out only if it is farther apart than MinSeparation in every sub-step: no contact is lost, but a false positive or
        /// an IterationLimit pair of SubSteps = 1 can be left out (docs/design/CCD.md 10.10).
        /// </summary>
        public int SubSteps = 4;
        /// <summary> run the whole sweep (boxes, tree queries, pair queries) on all cores (the result equals the serial one) </summary>
        public bool Parallel = true;
        /// <summary> false: Contacts holds the pairs that may give the first contact; true: every candidate pair </summary>
        public bool CollectAll = false;

        /// <summary> see MeshSweepStatus </summary>
        public MeshSweepStatus Status;
        /// <summary>
        /// no contact between any two primitives on [0, EarliestTimeOfImpact); at most Options.TimeLimit if that is
        /// below 1 (only [0, TimeLimit] is searched), +infinity if no pair may touch in the whole step
        /// </summary>
        public double EarliestTimeOfImpact;
        /// <summary> the smallest NearContactTime of a pair; +infinity if none </summary>
        public double EarliestNearContactTime;
        /// <summary>
        /// CollectAll false: the pairs with Collision or IterationLimit whose TimeOfImpact is at most
        /// EarliestNearContactTime (each may be the first contact); true: every candidate pair. Sorted by
        /// (TimeOfImpact, Kind, FirstOfMeshA descending, First, Second).
        /// </summary>
        public List<SweepContact> Contacts = new List<SweepContact>();
        /// <summary> the number of pairs the broad phase passed to the narrow phase </summary>
        public int CandidatePairs;
        /// <summary> the number of candidate pairs as a long (CandidatePairs saturates at int.MaxValue) </summary>
        public long CandidatePairCount;
        /// <summary>
        /// benchmarks: the wall time of the last Compute's preparation (boxes, trees, jobs) and of the batches (the tree
        /// queries, which are the rest of the broad phase, and the pair queries)
        /// </summary>
        internal double PrepareSeconds, PairsSeconds;

        /// <summary> a self sweep of mesh; endPositions[vid] is the position of vertex vid at t = 1 </summary>
        public MeshSweepCollision(DMesh3 mesh, Vector3d[] endPositions)
        {
            meshA = meshB = mesh; endA = endB = endPositions; self = true;
        }

        /// <summary> mesh A against mesh B, with the end positions of their vertices (indexed by vertex id) </summary>
        public MeshSweepCollision(DMesh3 meshA, Vector3d[] endA, DMesh3 meshB, Vector3d[] endB)
        {
            this.meshA = meshA; this.endA = endA; this.meshB = meshB; this.endB = endB; self = false;
        }

        /// <summary>
        /// replace the end positions (eg the next time step); then call Compute again. endB null keeps B's end positions
        /// (for a self sweep it is not used)
        /// </summary>
        public void SetEndPositions(Vector3d[] endA, Vector3d[] endB = null)
        {
            this.endA = endA;
            if (self) this.endB = endA;
            else if (endB != null) this.endB = endB;
        }


        /// <summary> run the sweep; may be called again (after changing Options or the end positions) </summary>
        public MeshSweepStatus Compute()
        {
            Contacts = new List<SweepContact>();
            CandidatePairs = 0;
            CandidatePairCount = 0;
            PrepareSeconds = PairsSeconds = 0;
            EarliestTimeOfImpact = double.PositiveInfinity;
            EarliestNearContactTime = double.PositiveInfinity;
            Status = MeshSweepStatus.InvalidInput;
            if (meshA == null || meshB == null || endA == null || endB == null || Options == null || Options.IsValid() == false
                || endA.Length < meshA.MaxVertexID || endB.Length < meshB.MaxVertexID
                || BlockSize < 1 || BlockSize > (1 << 20) || SelfTasks < 1 || BatchPairs < 1
                || SubSteps < 1 || SubSteps > 64 || (SubSteps & (SubSteps - 1)) != 0)
                return Status;
            if (finite(meshA, endA) == false || (self == false && finite(meshB, endB) == false))
                return Status;

            // half the minimum separation, rounded up (0.5 ms underflows to 0 for ms = 2^-1074)
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double half = Options.MinSeparation > 0 ? ExactInterval.Up(0.5 * Options.MinSeparation) : 0;
            bool parallel = Parallel;

            // the trees (boxes of the swept triangles and edges) and the jobs: fixed blocks of query primitives, so that
            // every job's output depends only on the input; the outputs are merged in job order and sorted
            var jobs = new List<Job>();
            Prepared triB = triangles(meshB, endB, half, parallel), triA = self ? null : triangles(meshA, endA, half, parallel);
            Prepared edgB = edges(meshB, endB, half, parallel);
            int[] vertsA = ids(meshA.VertexIndices()), vertsB = self ? null : ids(meshB.VertexIndices());
            int[] edgesA = self ? null : ids(meshA.EdgeIndices());
            add_blocks(jobs, JobKind.VertexTriangle, true, vertsA.Length);
            if (self == false) {
                add_blocks(jobs, JobKind.VertexTriangle, false, vertsB.Length);
                add_blocks(jobs, JobKind.EdgeEdge, true, edgesA.Length);
            }
            List<Index2i> selfTasks = null;
            if (self && edgB.Trees != null) {
                selfTasks = edgB.Trees[0].SelfPairTasks(SelfTasks);
                for (int k = 0; k < selfTasks.Count; ++k)
                    jobs.Add(new Job { Kind = JobKind.SelfEdges, Start = k, End = k + 1 });
            }
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

            // In batches of about BatchPairs candidate pairs (the batches depend only on the input; the memory is that of
            // a group of jobs' candidates and of a batch, plus the kept pairs):
            // (1) the jobs' candidate pairs, in parallel over the jobs; (2) the pairs, in parallel over the pairs (so that
            // expensive pairs that cluster in one job still spread over the cores); (3) the results recorded per job, in
            // order.
            var outputs = new JobOutput[jobs.Count];
            var jobPairs = new List<PairRef>[jobs.Count];
            // the batch's pairs and results, grown once and reused (no new large arrays per batch)
            var flat = new PairRef[0];
            var results = new CcdResult[0];
            Action<int, int> flush = (a, b) => {
                // (2) the pairs of jobs [a, b), in parallel over the pairs
                int total = 0;
                for (int jj = a; jj < b; ++jj)
                    total = checked(total + jobPairs[jj].Count);
                if (flat.Length < total) {
                    flat = new PairRef[total];
                    results = new CcdResult[total];
                }
                int at = 0;
                for (int jj = a; jj < b; ++jj) {
                    jobPairs[jj].CopyTo(flat, at);
                    at += jobPairs[jj].Count;
                }
                PairRef[] f = flat;
                CcdResult[] res = results;
                Action<int, CcdSolver> solve = (k, solver) => { res[k] = solve_pair(f[k], solver); };
                if (parallel && total > 1)
                    gParallel.ForRange(total, () => new CcdSolver(), solve);
                else {
                    var solver = new CcdSolver();
                    for (int k = 0; k < total; ++k)
                        solve(k, solver);
                }
                // (3) the results recorded per job, in order
                at = 0;
                for (int jj = a; jj < b; ++jj) {
                    var o = new JobOutput();
                    int n = jobPairs[jj].Count;
                    for (int k = at; k < at + n; ++k)
                        record(o, f[k], res[k]);
                    at += n;
                    outputs[jj] = o;
                    jobPairs[jj] = null;
                }
            };
            int pending = 0;
            long held = 0;
            for (int g0 = 0; g0 < jobs.Count; g0 += BatchJobs) {
                // (1) the candidates of a group of jobs, in parallel over its jobs
                int ng = Math.Min(BatchJobs, jobs.Count - g0);
                Action<int, Worker> collect = (jj, w) => { jobPairs[g0 + jj] = job_candidates(jobs[g0 + jj], vertsA, vertsB, edgesA, selfTasks, triA, triB, edgB, half, w); };
                if (parallel && ng > 1)
                    gParallel.ForRange(ng, () => new Worker(), collect);
                else {
                    var w = new Worker();
                    for (int jj = 0; jj < ng; ++jj)
                        collect(jj, w);
                }
                // a batch ends after the first job (in job order) at which it holds BatchPairs pairs
                for (int jj = g0; jj < g0 + ng; ++jj) {
                    held += jobPairs[jj].Count;
                    if (held >= BatchPairs) {
                        flush(pending, jj + 1);
                        pending = jj + 1;
                        held = 0;
                    }
                }
            }
            flush(pending, jobs.Count);
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            PrepareSeconds = (t1 - t0) / (double)System.Diagnostics.Stopwatch.Frequency;
            PairsSeconds = (t2 - t1) / (double)System.Diagnostics.Stopwatch.Frequency;

            // reduce serially in job order (min of times: exact, independent of the order); a time limit below 1 bounds
            // the claim
            if (Options.TimeLimit < 1)
                EarliestTimeOfImpact = Options.TimeLimit;
            bool invalid = false;
            long candidates = 0;
            foreach (JobOutput o in outputs) {
                invalid |= o.Invalid;
                candidates += o.Candidates;
                EarliestTimeOfImpact = Math.Min(EarliestTimeOfImpact, o.MinTimeOfImpact);
                EarliestNearContactTime = Math.Min(EarliestNearContactTime, o.MinNearContactTime);
            }
            CandidatePairs = (int)Math.Min(int.MaxValue, candidates);
            CandidatePairCount = candidates;
            if (invalid) {
                EarliestTimeOfImpact = EarliestNearContactTime = double.PositiveInfinity;
                return Status = MeshSweepStatus.InvalidInput;
            }
            bool limited = false;
            foreach (JobOutput o in outputs)
                foreach (SweepContact c in o.Kept) {
                    CcdResult r = c.Result;
                    bool candidate = (r.Status == CcdStatus.Collision || r.Status == CcdStatus.IterationLimit)
                                     && r.TimeOfImpact <= EarliestNearContactTime;
                    if (r.Status == CcdStatus.IterationLimit && r.TimeOfImpact <= EarliestNearContactTime)
                        limited = true;
                    if (CollectAll || candidate)
                        Contacts.Add(c);
                }
            Contacts.Sort(compare);
            if (limited) Status = MeshSweepStatus.IterationLimit;
            else if (EarliestNearContactTime < double.PositiveInfinity) Status = MeshSweepStatus.Collision;
            else Status = MeshSweepStatus.NoCollision;
            return Status;
        }

        // the start and end positions of the mesh's vertices are finite and within the solver's range (a NaN box would
        // drop its pairs silently)
        static bool finite(DMesh3 m, Vector3d[] end)
        {
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid), q = end[vid];
                double r = CcdSolver.InputRange;
                if ((Math.Abs(p.x) <= r && Math.Abs(p.y) <= r && Math.Abs(p.z) <= r
                     && Math.Abs(q.x) <= r && Math.Abs(q.y) <= r && Math.Abs(q.z) <= r) == false)
                    return false;
            }
            return true;
        }

        static int compare(SweepContact x, SweepContact y)
        {
            int c = x.Result.TimeOfImpact.CompareTo(y.Result.TimeOfImpact);
            if (c != 0) return c;
            if (x.Kind != y.Kind) return x.Kind.CompareTo(y.Kind);
            if (x.FirstOfMeshA != y.FirstOfMeshA) return x.FirstOfMeshA ? -1 : 1;
            if (x.First != y.First) return x.First.CompareTo(y.First);
            return x.Second.CompareTo(y.Second);
        }

        // the box of the points, widened by pad (>= 0) with outward rounding
        static AxisAlignedBox3d box(double pad, Vector3d p0, Vector3d p1, Vector3d p2, Vector3d p3, Vector3d p4, Vector3d p5, int n)
        {
            double x0 = p0.x, y0 = p0.y, z0 = p0.z, x1 = x0, y1 = y0, z1 = z0;
            for (int i = 1; i < n; ++i) {
                Vector3d p = i == 1 ? p1 : i == 2 ? p2 : i == 3 ? p3 : i == 4 ? p4 : p5;
                x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); z0 = Math.Min(z0, p.z);
                x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); z1 = Math.Max(z1, p.z);
            }
            if (pad > 0) {
                x0 = ExactInterval.Down(x0 - pad); y0 = ExactInterval.Down(y0 - pad); z0 = ExactInterval.Down(z0 - pad);
                x1 = ExactInterval.Up(x1 + pad); y1 = ExactInterval.Up(y1 + pad); z1 = ExactInterval.Up(z1 + pad);
            }
            var b = new AxisAlignedBox3d();
            b.Min = new Vector3d(x0, y0, z0);
            b.Max = new Vector3d(x1, y1, z1);
            return b;
        }

        // the number of self edge-pair tasks (SelfPairTasks) and the query primitives per job: fixed, so that the jobs do
        // not depend on the machine (tests set smaller values to get many jobs on small meshes)
        internal int SelfTasks = 4096;
        internal int BlockSize = 128;

        enum JobKind { VertexTriangle, EdgeEdge, SelfEdges }

        struct Job
        {
            public JobKind Kind;
            public bool OfA;           // the query primitives are of mesh A
            public int Start, End;     // a range of the query primitives (SelfEdges: of the tasks)
        }

        // a job's output: the pairs that may touch (every candidate with CollectAll), the minima, the candidate count
        sealed class JobOutput
        {
            public readonly List<SweepContact> Kept = new List<SweepContact>();
            public long Candidates;
            public double MinTimeOfImpact = double.PositiveInfinity, MinNearContactTime = double.PositiveInfinity;
            public bool Invalid;
        }

        // per thread: query buffers
        sealed class Worker
        {
            public readonly List<int> Found = new List<int>();
            public AxisAlignedBox3d[] Sub;
            public readonly List<Index2i> Pairs = new List<Index2i>();
        }

        // the swept boxes of a mesh's triangles or edges, their tree, and the ids by box index
        sealed class Prepared
        {
            public int[] Ids;
            public TriangleBoxTree[] Trees;      // the tree of the whole-step boxes (one; null without primitives)
        }

        static int[] ids(IEnumerable<int> e) { return new List<int>(e).ToArray(); }

        void add_blocks(List<Job> jobs, JobKind kind, bool ofA, int n)
        {
            for (int s = 0; s < n; s += BlockSize)
                jobs.Add(new Job { Kind = kind, OfA = ofA, Start = s, End = Math.Min(n, s + BlockSize) });
        }

        // fill boxes[i] for i in [0, n) in fixed blocks (parallel or not: the same boxes)
        void for_blocks(int n, bool parallel, Action<int> f)
        {
            int blocks = (n + BlockSize - 1) / BlockSize;
            Action<int> block = b => { for (int i = b * BlockSize; i < Math.Min(n, (b + 1) * BlockSize); ++i) f(i); };
            if (parallel && blocks > 1)
                gParallel.ForRange(blocks, block);
            else
                for (int b = 0; b < blocks; ++b) block(b);
        }

        // The box of point v of mesh m (end positions end) over sub-step i of SubSteps: the box of its positions at
        // t_i = i / SubSteps and t_(i+1). The ends of the step are the exact positions; at an inner t_i (exact: SubSteps is
        // a power of two) the position s + t (e - s) is enclosed in outward-rounded intervals. Under linear motion the
        // point stays in the box during the sub-step (a convex combination of the two positions).
        void sub_box(DMesh3 m, Vector3d[] end, int v, int i, ref double x0, ref double y0, ref double z0, ref double x1, ref double y1, ref double z1)
        {
            Vector3d s = m.GetVertex(v), e = end[v];
            for (int j = i; j <= i + 1; ++j) {
                if (j == 0 || j == SubSteps) {
                    Vector3d q = j == 0 ? s : e;
                    x0 = Math.Min(x0, q.x); y0 = Math.Min(y0, q.y); z0 = Math.Min(z0, q.z);
                    x1 = Math.Max(x1, q.x); y1 = Math.Max(y1, q.y); z1 = Math.Max(z1, q.z);
                    continue;
                }
                ExactInterval t = ExactInterval.Of(j / (double)SubSteps);
                ExactInterval px = ExactInterval.Of(s.x) + t * (ExactInterval.Of(e.x) - ExactInterval.Of(s.x));
                ExactInterval py = ExactInterval.Of(s.y) + t * (ExactInterval.Of(e.y) - ExactInterval.Of(s.y));
                ExactInterval pz = ExactInterval.Of(s.z) + t * (ExactInterval.Of(e.z) - ExactInterval.Of(s.z));
                x0 = Math.Min(x0, px.Lo); y0 = Math.Min(y0, py.Lo); z0 = Math.Min(z0, pz.Lo);
                x1 = Math.Max(x1, px.Hi); y1 = Math.Max(y1, py.Hi); z1 = Math.Max(z1, pz.Hi);
            }
        }

        // the box of the points v0..v(n-1) over sub-step i, clipped to their box over the whole step (both hold the
        // points during the sub-step; the clip keeps the interval widening at inner t_i inside the SubSteps = 1 box, so
        // the candidates are a subset of those of SubSteps = 1), widened by pad with outward rounding
        AxisAlignedBox3d sub_box(DMesh3 m, Vector3d[] end, int i, double pad, int n, int v0, int v1, int v2)
        {
            double x0 = double.PositiveInfinity, y0 = x0, z0 = x0, x1 = double.NegativeInfinity, y1 = x1, z1 = x1;
            sub_box(m, end, v0, i, ref x0, ref y0, ref z0, ref x1, ref y1, ref z1);
            if (n > 1) sub_box(m, end, v1, i, ref x0, ref y0, ref z0, ref x1, ref y1, ref z1);
            if (n > 2) sub_box(m, end, v2, i, ref x0, ref y0, ref z0, ref x1, ref y1, ref z1);
            double fx0 = double.PositiveInfinity, fy0 = fx0, fz0 = fx0, fx1 = double.NegativeInfinity, fy1 = fx1, fz1 = fx1;
            for (int k = 0; k < n; ++k) {
                int v = k == 0 ? v0 : k == 1 ? v1 : v2;
                Vector3d ps = m.GetVertex(v), pe = end[v];
                fx0 = Math.Min(fx0, Math.Min(ps.x, pe.x)); fy0 = Math.Min(fy0, Math.Min(ps.y, pe.y)); fz0 = Math.Min(fz0, Math.Min(ps.z, pe.z));
                fx1 = Math.Max(fx1, Math.Max(ps.x, pe.x)); fy1 = Math.Max(fy1, Math.Max(ps.y, pe.y)); fz1 = Math.Max(fz1, Math.Max(ps.z, pe.z));
            }
            x0 = Math.Max(x0, fx0); y0 = Math.Max(y0, fy0); z0 = Math.Max(z0, fz0);
            x1 = Math.Min(x1, fx1); y1 = Math.Min(y1, fy1); z1 = Math.Min(z1, fz1);
            if (pad > 0) {
                x0 = ExactInterval.Down(x0 - pad); y0 = ExactInterval.Down(y0 - pad); z0 = ExactInterval.Down(z0 - pad);
                x1 = ExactInterval.Up(x1 + pad); y1 = ExactInterval.Up(y1 + pad); z1 = ExactInterval.Up(z1 + pad);
            }
            var b = new AxisAlignedBox3d();
            b.Min = new Vector3d(x0, y0, z0);
            b.Max = new Vector3d(x1, y1, z1);
            return b;
        }

        Prepared triangles(DMesh3 m, Vector3d[] end, double half, bool parallel)
        {
            var p = new Prepared { Ids = ids(m.TriangleIndices()) };
            if (p.Ids.Length == 0)
                return p;
            var boxes = new AxisAlignedBox3d[p.Ids.Length];
            for_blocks(boxes.Length, parallel, i => {
                Index3i t = m.GetTriangle(p.Ids[i]);
                boxes[i] = box(half, m.GetVertex(t.a), m.GetVertex(t.b), m.GetVertex(t.c), end[t.a], end[t.b], end[t.c], 6);
            });
            p.Trees = new[] { new TriangleBoxTree(new List<AxisAlignedBox3d>(boxes)) };
            return p;
        }

        Prepared edges(DMesh3 m, Vector3d[] end, double half, bool parallel)
        {
            var p = new Prepared { Ids = ids(m.EdgeIndices()) };
            if (p.Ids.Length == 0)
                return p;
            var boxes = new AxisAlignedBox3d[p.Ids.Length];
            for_blocks(boxes.Length, parallel, i => {
                Index2i e = m.GetEdgeV(p.Ids[i]);
                boxes[i] = box(half, m.GetVertex(e.a), m.GetVertex(e.b), end[e.a], end[e.b], Vector3d.Zero, Vector3d.Zero, 4);
            });
            p.Trees = new[] { new TriangleBoxTree(new List<AxisAlignedBox3d>(boxes)) };
            return p;
        }

        // Sub-step filter of a candidate pair: true if the sub-step boxes of the two primitives (sub_box, clipped to the
        // whole-step boxes, widened by half the minimum separation each) overlap in some sub-step. The query primitive's
        // boxes are in w.Sub (set_sub); the other primitive's points are (m, end, n, v0, v1, v2).
        bool overlaps_some_sub_step(Worker w, DMesh3 m, Vector3d[] end, double half, int n, int v0, int v1, int v2)
        {
            for (int st = 0; st < SubSteps; ++st) {
                AxisAlignedBox3d b = sub_box(m, end, st, half, n, v0, v1, v2), q = w.Sub[st];
                if (b.Min.x <= q.Max.x && q.Min.x <= b.Max.x && b.Min.y <= q.Max.y && q.Min.y <= b.Max.y
                    && b.Min.z <= q.Max.z && q.Min.z <= b.Max.z)
                    return true;
            }
            return false;
        }

        // the query primitive's sub-step boxes into w.Sub
        void set_sub(Worker w, DMesh3 m, Vector3d[] end, double half, int n, int v0, int v1, int v2)
        {
            if (w.Sub == null || w.Sub.Length < SubSteps)
                w.Sub = new AxisAlignedBox3d[SubSteps];
            for (int st = 0; st < SubSteps; ++st)
                w.Sub[st] = sub_box(m, end, st, half, n, v0, v1, v2);
        }

        // jobs per group of the candidate search, and the candidate pairs per batch (they live at the same time, with
        // their results: about 64 bytes each); a batch ends at the job that reaches BatchPairs, so it holds at most
        // BatchPairs plus one job's pairs
        const int BatchJobs = 16;
        internal int BatchPairs = 1 << 21;

        // the candidate pairs of one job
        List<PairRef> job_candidates(Job job, int[] vertsA, int[] vertsB, int[] edgesA, List<Index2i> selfTasks,
                                 Prepared triA, Prepared triB, Prepared edgB, double half, Worker w)
        {
            var list = new List<PairRef>();
            if (job.Kind == JobKind.VertexTriangle) {
                DMesh3 vm = job.OfA ? meshA : meshB, tm = job.OfA ? meshB : meshA;
                Vector3d[] ve = job.OfA ? endA : endB;
                Prepared tri = job.OfA ? triB : triA;
                int[] verts = job.OfA ? vertsA : vertsB;
                if (tri.Trees != null)
                    for (int k = job.Start; k < job.End; ++k)
                        vertex_against(verts[k], vm, ve, tm, tri, job.OfA, half, w, list);
            } else if (job.Kind == JobKind.EdgeEdge) {
                if (edgB.Trees != null)
                    for (int k = job.Start; k < job.End; ++k)
                        edge_against(edgesA[k], edgB, half, w, list);
            } else
                self_edges(selfTasks[job.Start], edgB, half, w, list);
            return list;
        }

        // a candidate pair: the kind, the first primitive (of mesh A or B) and the second (of the other mesh, or of A for
        // a self sweep)
        struct PairRef
        {
            public SweepContactKind Kind;
            public bool OfA;
            public int First, Second;
        }

        CcdResult solve_pair(PairRef p, CcdSolver solver)
        {
            DMesh3 m1 = p.OfA ? meshA : meshB, m2 = p.OfA ? meshB : meshA;
            Vector3d[] e1 = p.OfA ? endA : endB, e2 = p.OfA ? endB : endA;
            if (p.Kind == SweepContactKind.VertexTriangle) {
                Index3i t = m2.GetTriangle(p.Second);
                solver.Set(m1.GetVertex(p.First), m2.GetVertex(t.a), m2.GetVertex(t.b), m2.GetVertex(t.c),
                           e1[p.First], e2[t.a], e2[t.b], e2[t.c]);
                return solver.Solve(true, Options);
            }
            Index2i ea = m1.GetEdgeV(p.First), eb = m2.GetEdgeV(p.Second);
            solver.Set(m1.GetVertex(ea.a), m1.GetVertex(ea.b), m2.GetVertex(eb.a), m2.GetVertex(eb.b),
                       e1[ea.a], e1[ea.b], e2[eb.a], e2[eb.b]);
            return solver.Solve(false, Options);
        }

        // record a solved pair in its job's output
        void record(JobOutput o, PairRef p, CcdResult r)
        {
            ++o.Candidates;
            if (r.Status == CcdStatus.InvalidInput)
                o.Invalid = true;
            bool touch = r.Status == CcdStatus.Collision || r.Status == CcdStatus.IterationLimit;
            if (touch)
                o.MinTimeOfImpact = Math.Min(o.MinTimeOfImpact, r.TimeOfImpact);
            if (r.Status == CcdStatus.Collision)
                o.MinNearContactTime = Math.Min(o.MinNearContactTime, r.NearContactTime);
            if (touch || CollectAll)
                o.Kept.Add(new SweepContact { Kind = p.Kind, First = p.First, Second = p.Second, FirstOfMeshA = p.OfA, Result = r });
        }

        // the candidate triangles of a vertex in the other mesh (or in its own, without the incident ones), by id
        void vertex_against(int vid, DMesh3 vm, Vector3d[] ve, DMesh3 tm, Prepared tri, bool ofA, double half, Worker w, List<PairRef> pairs)
        {
            Vector3d p = vm.GetVertex(vid);
            w.Found.Clear();
            tri.Trees[0].Query(box(half, p, ve[vid], p, p, p, p, 2), w.Found);
            w.Found.Sort();
            if (SubSteps > 1)
                set_sub(w, vm, ve, half, 1, vid, -1, -1);
            Vector3d[] te = vm == tm ? ve : (ve == endA ? endB : endA);
            foreach (int k in w.Found) {
                int tid = tri.Ids[k];
                Index3i t = tm.GetTriangle(tid);
                if (self && (t.a == vid || t.b == vid || t.c == vid))
                    continue;
                if (SubSteps > 1 && overlaps_some_sub_step(w, tm, te, half, 3, t.a, t.b, t.c) == false)
                    continue;
                pairs.Add(new PairRef { Kind = SweepContactKind.VertexTriangle, OfA = ofA, First = vid, Second = tid });
            }
        }

        // the candidate edges of B of an edge of A, by id
        void edge_against(int eid, Prepared edgB, double half, Worker w, List<PairRef> pairs)
        {
            Index2i ea = meshA.GetEdgeV(eid);
            w.Found.Clear();
            edgB.Trees[0].Query(box(half, meshA.GetVertex(ea.a), meshA.GetVertex(ea.b), endA[ea.a], endA[ea.b], Vector3d.Zero, Vector3d.Zero, 4), w.Found);
            w.Found.Sort();
            if (SubSteps > 1)
                set_sub(w, meshA, endA, half, 2, ea.a, ea.b, -1);
            foreach (int k in w.Found) {
                int other = edgB.Ids[k];
                if (SubSteps > 1) {
                    Index2i eb = meshB.GetEdgeV(other);
                    if (overlaps_some_sub_step(w, meshB, endB, half, 2, eb.a, eb.b, -1) == false)
                        continue;
                }
                pairs.Add(new PairRef { Kind = SweepContactKind.EdgeEdge, OfA = true, First = eid, Second = other });
            }
        }

        // self sweep: the overlapping edge pairs of one SelfPairs task (each pair once), sorted by ids; edges that share
        // a vertex skipped
        void self_edges(Index2i task, Prepared edg, double half, Worker w, List<PairRef> pairs)
        {
            w.Pairs.Clear();
            edg.Trees[0].SelfPairs(task, w.Pairs);
            for (int i = 0; i < w.Pairs.Count; ++i) {
                int x = edg.Ids[w.Pairs[i].a], y = edg.Ids[w.Pairs[i].b];
                w.Pairs[i] = x < y ? new Index2i(x, y) : new Index2i(y, x);
            }
            w.Pairs.Sort((p, q) => p.a != q.a ? p.a.CompareTo(q.a) : p.b.CompareTo(q.b));
            foreach (Index2i k in w.Pairs) {
                Index2i ea = meshA.GetEdgeV(k.a), eb = meshA.GetEdgeV(k.b);
                if (eb.a == ea.a || eb.a == ea.b || eb.b == ea.a || eb.b == ea.b)
                    continue;
                if (SubSteps > 1) {
                    set_sub(w, meshA, endA, half, 2, ea.a, ea.b, -1);
                    if (overlaps_some_sub_step(w, meshA, endA, half, 2, eb.a, eb.b, -1) == false)
                        continue;
                }
                pairs.Add(new PairRef { Kind = SweepContactKind.EdgeEdge, OfA = true, First = k.a, Second = k.b });
            }
        }
    }
}
