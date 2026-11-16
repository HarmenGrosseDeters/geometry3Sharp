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
    /// overlapping pair is missed); a pair of overlapping boxes is kept if the boxes of its primitives also overlap in
    /// some sub-step (SubSteps). The tree nodes also hold the sub-step boxes, so the search drops a pair of nodes once
    /// they are apart in every sub-step (SubStepBoxTree; the same candidates as filtering the pairs of the whole-step
    /// tree, which is the path above MaxTreeDoubles). Narrow phase: ContinuousCollision per candidate pair over the whole step; each pair is
    /// computed independently. Both run in parallel over fixed jobs (blocks of query primitives, self edge-pair tasks) in
    /// batches of about two million candidate pairs (a batch ends at the job that reaches that number, so it can hold
    /// one job's pairs more); the jobs and batches depend only on the input and the outputs are merged in
    /// job order and sorted, so the result does not depend on the thread schedule, and the memory is that of a batch, of
    /// the kept pairs and of the sub-step hulls ((SubSteps + 1) * 48 bytes per vertex).
    /// </summary>
    public class MeshSweepCollision
    {
        readonly DMesh3 meshA, meshB;
        Vector3d[] endA, endB;
        readonly bool self;
        // the sub-step hulls of the meshes' points during Compute (sub_hulls), and whether the sub-step filter runs
        // (SubSteps > 1 and the hulls within MaxHullDoubles)
        double[] hullsA, hullsB;
        bool subFilter;
        // whether the trees are SubStepBoxTrees (the sub-step filter inside the traversal; within MaxTreeDoubles), else
        // TriangleBoxTrees with the filter applied to their pairs
        bool subTree;
        // the most doubles the SubStepBoxTrees may take together (leaves and nodes, SubStepBoxTree.Doubles: about
        // 1.5 * 6 (SubSteps + 1) per triangle and edge; 2^27: 1 GB); above it the filter runs on the pairs of the whole-step trees, with the same candidates (tests set
        // smaller values)
        internal long MaxTreeDoubles = 1L << 27;
        // tests: whether the last Compute used the SubStepBoxTrees
        internal bool UsedSubStepTree { get { return subTree; } }
        // the most doubles the sub-step hulls may take (2^27: 1 GB, below the 2 GB object limit of .NET Framework);
        // above it the filter is skipped, which gives the candidates of SubSteps = 1 (tests set smaller values)
        internal long MaxHullDoubles = 1L << 27;

        /// <summary> options of every pair query </summary>
        public CcdOptions Options = new CcdOptions();
        /// <summary>
        /// the broad phase splits the step into this many equal sub-steps (a power of two, 1 to 64; 4 by default): a pair
        /// of overlapping whole-step boxes is a candidate only if the boxes of its primitives also overlap in some
        /// sub-step. Fewer candidates for large motions; every candidate is still solved over the whole step, so a pair's
        /// result does not depend on SubSteps. A pair is left out only if its boxes are farther apart than MinSeparation
        /// in every sub-step: no contact is lost, but a pair that SubSteps = 1 reports as a false positive (within the
        /// error bound) or at the iteration limit can be left out (docs/design/CCD.md 10.10). Then Status can go from
        /// Collision to NoCollision, the earliest times can be later (never past a true contact), and with CollectAll =
        /// false Contacts can hold pairs that SubSteps = 1 leaves out (those up to the later EarliestNearContactTime): the
        /// subset holds for the candidates and per-pair results. SubSteps = 1 gives the behaviour before this option.
        /// Memory: (SubSteps + 1) * 48 bytes per vertex; above 1 GB the filter is skipped (the candidates of SubSteps = 1).
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
        /// <summary> benchmarks: the part of PairsSeconds spent collecting the candidates (the tree queries and the filter) </summary>
        internal double CandidateSeconds;

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
            PairsSkipped = PairsAbandoned = 0;
            PrepareSeconds = PairsSeconds = CandidateSeconds = 0;
            EarliestTimeOfImpact = double.PositiveInfinity;
            EarliestNearContactTime = double.PositiveInfinity;
            Status = MeshSweepStatus.InvalidInput;
            subFilter = subTree = false;
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
            long hullDoubles = ((long)meshA.MaxVertexID + (self ? 0 : meshB.MaxVertexID)) * 6 * (SubSteps + 1);
            subFilter = SubSteps > 1 && hullDoubles <= MaxHullDoubles;
            long treeDoubles = SubStepBoxTree.Doubles(meshB.TriangleCount, SubSteps) + (self ? 0 : SubStepBoxTree.Doubles(meshA.TriangleCount, SubSteps))
                               + SubStepBoxTree.Doubles(meshB.EdgeCount, SubSteps);
            subTree = subFilter && treeDoubles <= MaxTreeDoubles;
            Prepared triB, triA, edgB;
            // (the hulls are released on every exit, also when a build throws, eg out of memory)
            try {
                if (subFilter) {
                    hullsA = sub_hulls(meshA, endA, half, parallel);
                    hullsB = self ? hullsA : sub_hulls(meshB, endB, half, parallel);
                }
                triB = triangles(meshB, endB, hullsB, half, parallel);
                triA = self ? null : triangles(meshA, endA, hullsA, half, parallel);
                edgB = edges(meshB, endB, hullsB, half, parallel);
            } catch {
                hullsA = hullsB = null;
                throw;
            }
            int[] vertsA = ids(meshA.VertexIndices()), vertsB = self ? null : ids(meshB.VertexIndices());
            int[] edgesA = self ? null : ids(meshA.EdgeIndices());
            add_blocks(jobs, JobKind.VertexTriangle, true, vertsA.Length);
            if (self == false) {
                add_blocks(jobs, JobKind.VertexTriangle, false, vertsB.Length);
                add_blocks(jobs, JobKind.EdgeEdge, true, edgesA.Length);
            }
            List<Index2i> selfTasks = null;
            if (self && edgB.Count > 0) {
                selfTasks = subTree ? edgB.Sub.SelfPairTasks(SelfTasks) : edgB.Tree.SelfPairTasks(SelfTasks);
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
            try {
                var jobPairs = new List<PairRef>[jobs.Count];
                double bound = double.PositiveInfinity;
                var jobSkipped = new long[jobs.Count];
                long abandoned = 0;
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
                    Action<int, CcdSolver> solve = (k, solver) => { solver.AbandonAfter = PreFilter && CollectAll == false ? bound : double.PositiveInfinity; res[k] = solve_pair(f[k], solver); if (solver.Abandoned) System.Threading.Interlocked.Increment(ref abandoned); };
                    // in fixed chunks: the bound of a chunk is the smallest near contact time of the chunks before it
                    // (it depends only on the input), so every pair that is not abandoned has its own result
                    int chunk = Math.Max(1, AbandonChunk);
                    for (long c0l = 0; c0l < total; c0l += chunk) {
                        int c0 = (int)c0l;
                        int c1 = (int)Math.Min(total, c0l + chunk);
                        int cs = c0;
                        if (parallel && c1 - c0 > 1)
                            gParallel.ForRange(c1 - c0, () => new_solver(), (k, solver) => solve(cs + k, solver));
                        else {
                            var solver = new_solver();
                            for (int k = c0; k < c1; ++k)
                                solve(k, solver);
                        }
                        if (CollectAll == false)
                            for (int k = c0; k < c1; ++k)
                                if (res[k].Status == CcdStatus.Collision)
                                    bound = Math.Min(bound, res[k].NearContactTime);
                    }
                    // (3) the results recorded per job, in order
                    at = 0;
                    for (int jj = a; jj < b; ++jj) {
                        var o = new JobOutput();
                        o.Candidates = jobSkipped[jj];
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
                    long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    Action<int, Worker> collect = (jj, w) => { jobPairs[g0 + jj] = job_candidates(jobs[g0 + jj], vertsA, vertsB, edgesA, selfTasks, triA, triB, edgB, half, w);
                        if (PreFilter && CollectAll == false)
                            jobSkipped[g0 + jj] = drop_root_discarded(jobPairs[g0 + jj], w); };
                    if (parallel && ng > 1)
                        gParallel.ForRange(ng, () => new Worker(), collect);
                    else {
                        var w = new Worker();
                        for (int jj = 0; jj < ng; ++jj)
                            collect(jj, w);
                    }
                    CandidateSeconds += (System.Diagnostics.Stopwatch.GetTimestamp() - c0) / (double)System.Diagnostics.Stopwatch.Frequency;
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
                PairsAbandoned = abandoned;
                PairsSkipped = 0;
                foreach (long n in jobSkipped) PairsSkipped += n;
            } finally {
                hullsA = hullsB = null;
            }
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
            public double[] Query;               // the query primitive's sub-step boxes (sub_boxes; with subTree after its whole-step box)
            public readonly List<Index2i> Pairs = new List<Index2i>();
            public int[] Stack;                  // SubStepBoxTree.Query's stacks
            public ulong[] Masks;
            public CcdSolver Solver;             // the pre-filter's
        }

        // the swept boxes of a mesh's triangles or edges, their tree, and the ids by box index
        sealed class Prepared
        {
            public int[] Ids;
            public TriangleBoxTree Tree;         // the tree of the whole-step boxes (null without primitives or with subTree)
            public SubStepBoxTree Sub;           // with subTree: the tree of the whole-step and sub-step boxes (null without primitives)
            public int Count { get { return Ids.Length; } }
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

        // The sub-step hulls of the points of mesh m (end positions end), by vertex id: for t_j = j / SubSteps, j = 0 ..
        // SubSteps, the bounds (lo x y z, hi x y z) of the point's position at t_j, widened by pad with outward rounding.
        // At j = 0 and SubSteps they are the exact positions; at an inner t_j (exact: SubSteps is a power of two) the
        // position s + t_j (e - s) is enclosed in an outward-rounded interval. Under linear motion the point stays
        // during sub-step i in the box of its hulls at t_i and t_(i+1) (a convex combination of the two positions). The
        // padding is per point: Down(x - pad) and Up(x + pad) are monotone, so the union of padded points is the padded
        // union, as in box(). (The filter only drops candidates of the whole-step tree, so the candidates are a subset
        // of those of SubSteps = 1 whatever the hulls' widening.)
        double[] sub_hulls(DMesh3 m, Vector3d[] end, double pad, bool parallel)
        {
            int k = SubSteps, stride = 6 * (k + 1);
            var h = new double[checked((long)m.MaxVertexID * stride)];
            for_blocks(m.MaxVertexID, parallel, v => {
                if (m.IsVertex(v) == false)
                    return;
                Vector3d s = m.GetVertex(v), e = end[v];
                for (int j = 0; j <= k; ++j) {
                    int o = v * stride + 6 * j;
                    for (int c = 0; c < 3; ++c) {
                        double lo, hi;
                        if (j == 0 || j == k) {
                            lo = hi = j == 0 ? s[c] : e[c];
                        } else {
                            ExactInterval q = ExactInterval.Of(s[c]) + ExactInterval.Of(j / (double)k) * (ExactInterval.Of(e[c]) - ExactInterval.Of(s[c]));
                            lo = q.Lo;
                            hi = q.Hi;
                        }
                        if (pad > 0) {
                            lo = ExactInterval.Down(lo - pad);
                            hi = ExactInterval.Up(hi + pad);
                        }
                        h[o + c] = lo;
                        h[o + 3 + c] = hi;
                    }
                }
            });
            return h;
        }

        Prepared triangles(DMesh3 m, Vector3d[] end, double[] h, double half, bool parallel)
        {
            var p = new Prepared { Ids = ids(m.TriangleIndices()) };
            if (p.Ids.Length == 0)
                return p;
            var boxes = new AxisAlignedBox3d[p.Ids.Length];
            for_blocks(boxes.Length, parallel, i => {
                Index3i t = m.GetTriangle(p.Ids[i]);
                boxes[i] = box(half, m.GetVertex(t.a), m.GetVertex(t.b), m.GetVertex(t.c), end[t.a], end[t.b], end[t.c], 6);
            });
            if (subTree) {
                p.Sub = new SubStepBoxTree(flatten(boxes), boxes.Length, SubSteps, (i, dst, o) => {
                    Index3i t = m.GetTriangle(p.Ids[i]);
                    sub_boxes(h, 3, t.a, t.b, t.c, dst, o);
                }, parallel);
            } else
                p.Tree = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxes));
            return p;
        }

        Prepared edges(DMesh3 m, Vector3d[] end, double[] h, double half, bool parallel)
        {
            var p = new Prepared { Ids = ids(m.EdgeIndices()) };
            if (p.Ids.Length == 0)
                return p;
            var boxes = new AxisAlignedBox3d[p.Ids.Length];
            for_blocks(boxes.Length, parallel, i => {
                Index2i e = m.GetEdgeV(p.Ids[i]);
                boxes[i] = box(half, m.GetVertex(e.a), m.GetVertex(e.b), end[e.a], end[e.b], Vector3d.Zero, Vector3d.Zero, 4);
            });
            if (subTree) {
                p.Sub = new SubStepBoxTree(flatten(boxes), boxes.Length, SubSteps, (i, dst, o) => {
                    Index2i e = m.GetEdgeV(p.Ids[i]);
                    sub_boxes(h, 2, e.a, e.b, -1, dst, o);
                }, parallel);
            } else
                p.Tree = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxes));
            return p;
        }

        // the boxes as min x y z, max x y z each
        static double[] flatten(AxisAlignedBox3d[] boxes)
        {
            var w = new double[6L * boxes.Length];
            for (int i = 0; i < boxes.Length; ++i)
                put(w, checked(6 * i), boxes[i]);
            return w;
        }

        static void put(double[] dst, int o, AxisAlignedBox3d b)
        {
            dst[o] = b.Min.x; dst[o + 1] = b.Min.y; dst[o + 2] = b.Min.z;
            dst[o + 3] = b.Max.x; dst[o + 4] = b.Max.y; dst[o + 5] = b.Max.z;
        }

        // Sub-step filter of a pair of primitives, both from the hulls: true if, in some sub-step, the box of the points
        // p0..p(n-1) (hulls hp) and that of the points q0..q(m-1) (hulls hq) overlap (closed boxes); stops at the first
        // separating axis of each sub-step (self edge pairs, where the first edge changes from pair to pair)
        bool overlaps_some_sub_step(double[] hp, int n, int p0, int p1, int p2, double[] hq, int m, int q0, int q1, int q2)
        {
            int stride = 6 * (SubSteps + 1);
            for (int st = 0; st < SubSteps; ++st) {
                bool all = true;
                for (int c = 0; c < 3 && all; ++c) {
                    double alo = double.PositiveInfinity, ahi = double.NegativeInfinity;
                    double blo = double.PositiveInfinity, bhi = double.NegativeInfinity;
                    for (int i = 0; i < n; ++i) {
                        int o = (i == 0 ? p0 : i == 1 ? p1 : p2) * stride + 6 * st + c;
                        alo = Math.Min(alo, Math.Min(hp[o], hp[o + 6]));
                        ahi = Math.Max(ahi, Math.Max(hp[o + 3], hp[o + 9]));
                    }
                    for (int i = 0; i < m; ++i) {
                        int o = (i == 0 ? q0 : i == 1 ? q1 : q2) * stride + 6 * st + c;
                        blo = Math.Min(blo, Math.Min(hq[o], hq[o + 6]));
                        bhi = Math.Max(bhi, Math.Max(hq[o + 3], hq[o + 9]));
                    }
                    all = alo <= bhi && blo <= ahi;
                }
                if (all)
                    return true;
            }
            return false;
        }

        // the boxes of the points p0..p(n-1) (hulls h) over the sub-steps into dst (lo x y z, hi x y z per sub-step): the
        // box over sub-step i is that of their hulls at t_i and t_(i+1) (sub_hulls)
        void sub_boxes(double[] h, int n, int p0, int p1, int p2, double[] dst, int at = 0)
        {
            int stride = 6 * (SubSteps + 1);
            for (int st = 0; st < SubSteps; ++st)
                for (int c = 0; c < 3; ++c) {
                    double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                    for (int i = 0; i < n; ++i) {
                        int o = (i == 0 ? p0 : i == 1 ? p1 : p2) * stride + 6 * st + c;
                        lo = Math.Min(lo, Math.Min(h[o], h[o + 6]));
                        hi = Math.Max(hi, Math.Max(h[o + 3], h[o + 9]));
                    }
                    dst[at + 6 * st + c] = lo;
                    dst[at + 6 * st + 3 + c] = hi;
                }
        }

        // Sub-step filter of a candidate pair: true if, in some sub-step, the query primitive's box (q, from sub_boxes)
        // and the box of the points q0..q(m-1) (hulls h) overlap (closed boxes)
        bool overlaps_some_sub_step(double[] q, double[] h, int m, int q0, int q1, int q2)
        {
            int stride = 6 * (SubSteps + 1);
            for (int st = 0; st < SubSteps; ++st) {
                bool all = true;
                for (int c = 0; c < 3 && all; ++c) {
                    double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                    for (int i = 0; i < m; ++i) {
                        int o = (i == 0 ? q0 : i == 1 ? q1 : q2) * stride + 6 * st + c;
                        lo = Math.Min(lo, Math.Min(h[o], h[o + 6]));
                        hi = Math.Max(hi, Math.Max(h[o + 3], h[o + 9]));
                    }
                    all = q[6 * st + c] <= hi && lo <= q[6 * st + 3 + c];
                }
                if (all)
                    return true;
            }
            return false;
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
                if (tri.Count > 0)
                    for (int k = job.Start; k < job.End; ++k)
                        vertex_against(verts[k], vm, ve, tm, tri, job.OfA, half, w, list);
            } else if (job.Kind == JobKind.EdgeEdge) {
                if (edgB.Count > 0)
                    for (int k = job.Start; k < job.End; ++k)
                        edge_against(edgesA[k], edgB, half, w, list);
            } else
                self_edges(selfTasks[job.Start], edgB, w, list);
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

        /// <summary> tests: false switches the pre-filter of the narrow phase off: the pairs whose first box the solver
        /// discards are queried like any other, and no pair is abandoned (the results are the same either way) </summary>
        internal bool PreFilter = true;

        /// <summary> tests and benchmarks: the candidate pairs the pre-filter dropped before a query (the solver discards their first box) and those whose query stopped at the bound (no pair of either kind is a contact) </summary>
        internal long PairsSkipped, PairsAbandoned;

        /// <summary> the number of candidate pairs per fixed chunk of a batch after which the bound of the abandoned pairs is updated </summary>
        internal int AbandonChunk = 1 << 16;

        CcdSolver new_solver() { return new CcdSolver { RootShortcut = PreFilter }; }

        // the pair's points into the solver
        void set_pair(PairRef p, CcdSolver solver)
        {
            DMesh3 m1 = p.OfA ? meshA : meshB, m2 = p.OfA ? meshB : meshA;
            Vector3d[] e1 = p.OfA ? endA : endB, e2 = p.OfA ? endB : endA;
            if (p.Kind == SweepContactKind.VertexTriangle) {
                Index3i t = m2.GetTriangle(p.Second);
                solver.Set(m1.GetVertex(p.First), m2.GetVertex(t.a), m2.GetVertex(t.b), m2.GetVertex(t.c),
                           e1[p.First], e2[t.a], e2[t.b], e2[t.c]);
                return;
            }
            Index2i ea = m1.GetEdgeV(p.First), eb = m2.GetEdgeV(p.Second);
            solver.Set(m1.GetVertex(ea.a), m1.GetVertex(ea.b), m2.GetVertex(eb.a), m2.GetVertex(eb.b),
                       e1[ea.a], e1[ea.b], e2[eb.a], e2[eb.b]);
        }

        CcdResult solve_pair(PairRef p, CcdSolver solver)
        {
            set_pair(p, solver);
            return solver.Solve(p.Kind == SweepContactKind.VertexTriangle, Options);
        }

        // Drops from the job's pairs those that the solver discards at its first box (CollectAll false: a pair that is not
        // a contact has no output but its count); returns how many.
        int drop_root_discarded(List<PairRef> pairs, Worker w)
        {
            if (w.Solver == null)
                w.Solver = new_solver();
            CcdSolver solver = w.Solver;
            int kept = 0;
            for (int k = 0; k < pairs.Count; ++k) {
                PairRef p = pairs[k];
                set_pair(p, solver);
                if (solver.RootDiscards(p.Kind == SweepContactKind.VertexTriangle, Options))
                    continue;
                pairs[kept++] = p;
            }
            int dropped = pairs.Count - kept;
            pairs.RemoveRange(kept, dropped);
            return dropped;
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

        // the candidate triangles of a vertex in the other mesh (or in its own, without the incident ones), by id, in
        // the order of the tree query (it depends only on the input; Contacts is sorted at the end)
        void vertex_against(int vid, DMesh3 vm, Vector3d[] ve, DMesh3 tm, Prepared tri, bool ofA, double half, Worker w, List<PairRef> pairs)
        {
            Vector3d p = vm.GetVertex(vid);
            w.Found.Clear();
            AxisAlignedBox3d qb = box(half, p, ve[vid], p, p, p, p, 2);
            bool filter = subFilter && subTree == false;
            if (subTree) {
                double[] q = query_boxes(w);
                put(q, 0, qb);
                sub_boxes(ofA ? hullsA : hullsB, 1, vid, -1, -1, q, 6);
                tri.Sub.Query(q, w.Found, ref w.Stack, ref w.Masks);
            } else {
                tri.Tree.Query(qb, w.Found);
                if (filter)
                    sub_boxes(ofA ? hullsA : hullsB, 1, vid, -1, -1, query_boxes(w));
            }
            double[] ht = ofA ? hullsB : hullsA;
            foreach (int k in w.Found) {
                int tid = tri.Ids[k];
                if (self || filter) {
                    Index3i t = tm.GetTriangle(tid);
                    if (self && (t.a == vid || t.b == vid || t.c == vid))
                        continue;
                    if (filter && overlaps_some_sub_step(w.Query, ht, 3, t.a, t.b, t.c) == false)
                        continue;
                }
                pairs.Add(new PairRef { Kind = SweepContactKind.VertexTriangle, OfA = ofA, First = vid, Second = tid });
            }
        }

        // the candidate edges of B of an edge of A, by id, in the order of the tree query
        void edge_against(int eid, Prepared edgB, double half, Worker w, List<PairRef> pairs)
        {
            Index2i ea = meshA.GetEdgeV(eid);
            w.Found.Clear();
            AxisAlignedBox3d qb = box(half, meshA.GetVertex(ea.a), meshA.GetVertex(ea.b), endA[ea.a], endA[ea.b], Vector3d.Zero, Vector3d.Zero, 4);
            bool filter = subFilter && subTree == false;
            if (subTree) {
                double[] q = query_boxes(w);
                put(q, 0, qb);
                sub_boxes(hullsA, 2, ea.a, ea.b, -1, q, 6);
                edgB.Sub.Query(q, w.Found, ref w.Stack, ref w.Masks);
            } else {
                edgB.Tree.Query(qb, w.Found);
                if (filter)
                    sub_boxes(hullsA, 2, ea.a, ea.b, -1, query_boxes(w));
            }
            foreach (int k in w.Found) {
                int other = edgB.Ids[k];
                if (filter) {
                    Index2i eb = meshB.GetEdgeV(other);
                    if (overlaps_some_sub_step(w.Query, hullsB, 2, eb.a, eb.b, -1) == false)
                        continue;
                }
                pairs.Add(new PairRef { Kind = SweepContactKind.EdgeEdge, OfA = true, First = eid, Second = other });
            }
        }

        double[] query_boxes(Worker w)
        {
            if (w.Query == null || w.Query.Length < 6 * (SubSteps + 1))
                w.Query = new double[6 * (SubSteps + 1)];
            return w.Query;
        }

        // self sweep: the overlapping edge pairs of one SelfPairs task (each pair once, ids ordered within the pair), in
        // SelfPairs order; edges that share a vertex skipped
        void self_edges(Index2i task, Prepared edg, Worker w, List<PairRef> pairs)
        {
            w.Pairs.Clear();
            bool filter = subFilter && subTree == false;
            if (subTree)
                edg.Sub.SelfPairs(task, w.Pairs);
            else
                edg.Tree.SelfPairs(task, w.Pairs);
            foreach (Index2i k in w.Pairs) {
                int x = edg.Ids[k.a], y = edg.Ids[k.b];
                Index2i ea = meshA.GetEdgeV(x), eb = meshA.GetEdgeV(y);
                if (eb.a == ea.a || eb.a == ea.b || eb.b == ea.a || eb.b == ea.b)
                    continue;
                if (filter && overlaps_some_sub_step(hullsA, 2, ea.a, ea.b, -1, hullsA, 2, eb.a, eb.b, -1) == false)
                    continue;
                pairs.Add(new PairRef { Kind = SweepContactKind.EdgeEdge, OfA = true, First = Math.Min(x, y), Second = Math.Max(x, y) });
            }
        }
    }
}
