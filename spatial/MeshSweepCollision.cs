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
    /// overlapping pair is missed). Narrow phase: ContinuousCollision per candidate pair, in parallel; each pair is
    /// computed independently, so the result does not depend on the thread schedule.
    /// </summary>
    public class MeshSweepCollision
    {
        readonly DMesh3 meshA, meshB;
        Vector3d[] endA, endB;
        readonly bool self;

        /// <summary> options of every pair query </summary>
        public CcdOptions Options = new CcdOptions();
        /// <summary> run the pair queries on all cores (the result equals the serial one) </summary>
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

        struct Pair
        {
            public SweepContactKind Kind;
            public int First, Second;
            public bool FirstOfMeshA;
        }

        /// <summary> run the sweep; may be called again (after changing Options or the end positions) </summary>
        public MeshSweepStatus Compute()
        {
            Contacts = new List<SweepContact>();
            CandidatePairs = 0;
            EarliestTimeOfImpact = double.PositiveInfinity;
            EarliestNearContactTime = double.PositiveInfinity;
            Status = MeshSweepStatus.InvalidInput;
            if (meshA == null || meshB == null || endA == null || endB == null || Options == null || Options.IsValid() == false
                || endA.Length < meshA.MaxVertexID || endB.Length < meshB.MaxVertexID)
                return Status;
            if (finite(meshA, endA) == false || (self == false && finite(meshB, endB) == false))
                return Status;

            // half the minimum separation, rounded up (0.5 ms underflows to 0 for ms = 2^-1074)
            double half = Options.MinSeparation > 0 ? ExactInterval.Up(0.5 * Options.MinSeparation) : 0;
            var pairs = new List<Pair>();
            // vertices of A against the triangles of B (and, for A against B, the vertices of B against the triangles of A)
            vertex_triangle(meshA, endA, meshB, endB, true, half, pairs);
            if (self == false)
                vertex_triangle(meshB, endB, meshA, endA, false, half, pairs);
            edge_edge(half, pairs);
            CandidatePairs = pairs.Count;

            var results = new CcdResult[pairs.Count];
            Action<int, CcdSolver> one = (i, solver) => {
                Pair p = pairs[i];
                DMesh3 m1 = p.FirstOfMeshA ? meshA : meshB, m2 = p.FirstOfMeshA ? meshB : meshA;
                Vector3d[] e1 = p.FirstOfMeshA ? endA : endB, e2 = p.FirstOfMeshA ? endB : endA;
                if (p.Kind == SweepContactKind.VertexTriangle) {
                    Index3i t = m2.GetTriangle(p.Second);
                    solver.Set(m1.GetVertex(p.First), m2.GetVertex(t.a), m2.GetVertex(t.b), m2.GetVertex(t.c),
                               e1[p.First], e2[t.a], e2[t.b], e2[t.c]);
                    results[i] = solver.Solve(true, Options);
                } else {
                    Index2i ea = m1.GetEdgeV(p.First), eb = m2.GetEdgeV(p.Second);
                    solver.Set(m1.GetVertex(ea.a), m1.GetVertex(ea.b), m2.GetVertex(eb.a), m2.GetVertex(eb.b),
                               e1[ea.a], e1[ea.b], e2[eb.a], e2[eb.b]);
                    results[i] = solver.Solve(false, Options);
                }
            };
            if (Parallel && pairs.Count > 1)
                gParallel.ForRange(pairs.Count, () => new CcdSolver(), one);
            else {
                var solver = new CcdSolver();
                for (int i = 0; i < pairs.Count; ++i)
                    one(i, solver);
            }

            // reduce serially (min of times: exact, independent of the order); a time limit below 1 bounds the claim
            if (Options.TimeLimit < 1)
                EarliestTimeOfImpact = Options.TimeLimit;
            bool invalid = false;
            for (int i = 0; i < results.Length; ++i) {
                CcdResult r = results[i];
                if (r.Status == CcdStatus.InvalidInput) invalid = true;
                if (r.Status == CcdStatus.Collision || r.Status == CcdStatus.IterationLimit)
                    EarliestTimeOfImpact = Math.Min(EarliestTimeOfImpact, r.TimeOfImpact);
                if (r.Status == CcdStatus.Collision)
                    EarliestNearContactTime = Math.Min(EarliestNearContactTime, r.NearContactTime);
            }
            if (invalid) {
                EarliestTimeOfImpact = EarliestNearContactTime = double.PositiveInfinity;
                return Status = MeshSweepStatus.InvalidInput;
            }
            bool limited = false;
            for (int i = 0; i < results.Length; ++i) {
                CcdResult r = results[i];
                bool candidate = (r.Status == CcdStatus.Collision || r.Status == CcdStatus.IterationLimit)
                                 && r.TimeOfImpact <= EarliestNearContactTime;
                if (r.Status == CcdStatus.IterationLimit && r.TimeOfImpact <= EarliestNearContactTime)
                    limited = true;
                if (CollectAll || candidate)
                    Contacts.Add(new SweepContact { Kind = pairs[i].Kind, First = pairs[i].First, Second = pairs[i].Second,
                                                    FirstOfMeshA = pairs[i].FirstOfMeshA, Result = r });
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

        void vertex_triangle(DMesh3 vm, Vector3d[] vend, DMesh3 tm, Vector3d[] tend, bool verticesOfA, double half, List<Pair> pairs)
        {
            var tids = new List<int>();
            var boxes = new List<AxisAlignedBox3d>();
            foreach (int tid in tm.TriangleIndices()) {
                Index3i t = tm.GetTriangle(tid);
                tids.Add(tid);
                boxes.Add(box(half, tm.GetVertex(t.a), tm.GetVertex(t.b), tm.GetVertex(t.c), tend[t.a], tend[t.b], tend[t.c], 6));
            }
            if (tids.Count == 0)
                return;
            var tree = new TriangleBoxTree(boxes);
            var found = new List<int>();
            foreach (int vid in vm.VertexIndices()) {
                Vector3d p = vm.GetVertex(vid);
                found.Clear();
                tree.Query(box(half, p, vend[vid], p, p, p, p, 2), found);
                found.Sort();
                foreach (int k in found) {
                    int tid = tids[k];
                    if (self) {
                        Index3i t = tm.GetTriangle(tid);
                        if (t.a == vid || t.b == vid || t.c == vid)
                            continue;
                    }
                    pairs.Add(new Pair { Kind = SweepContactKind.VertexTriangle, First = vid, Second = tid, FirstOfMeshA = verticesOfA });
                }
            }
        }

        void edge_edge(double half, List<Pair> pairs)
        {
            var eidsB = new List<int>();
            var boxesB = new List<AxisAlignedBox3d>();
            foreach (int eid in meshB.EdgeIndices()) {
                Index2i e = meshB.GetEdgeV(eid);
                eidsB.Add(eid);
                boxesB.Add(box(half, meshB.GetVertex(e.a), meshB.GetVertex(e.b), endB[e.a], endB[e.b], Vector3d.Zero, Vector3d.Zero, 4));
            }
            if (eidsB.Count == 0)
                return;
            var tree = new TriangleBoxTree(boxesB);
            if (self) {
                // each overlapping pair of different edges once (SelfPairs), in id order; edges sharing a vertex skipped
                var found = new List<Index2i>();
                foreach (Index2i task in tree.SelfPairTasks(1))
                    tree.SelfPairs(task, found);
                var keyed = new List<Index2i>(found.Count);
                foreach (Index2i k in found) {
                    int x = eidsB[k.a], y = eidsB[k.b];
                    keyed.Add(x < y ? new Index2i(x, y) : new Index2i(y, x));
                }
                keyed.Sort((p, q) => p.a != q.a ? p.a.CompareTo(q.a) : p.b.CompareTo(q.b));
                foreach (Index2i k in keyed) {
                    Index2i ea = meshA.GetEdgeV(k.a), eb = meshA.GetEdgeV(k.b);
                    if (eb.a == ea.a || eb.a == ea.b || eb.b == ea.a || eb.b == ea.b) continue;
                    pairs.Add(new Pair { Kind = SweepContactKind.EdgeEdge, First = k.a, Second = k.b, FirstOfMeshA = true });
                }
                return;
            }
            var hits = new List<int>();
            foreach (int eid in meshA.EdgeIndices()) {
                Index2i ea = meshA.GetEdgeV(eid);
                hits.Clear();
                tree.Query(box(half, meshA.GetVertex(ea.a), meshA.GetVertex(ea.b), endA[ea.a], endA[ea.b], Vector3d.Zero, Vector3d.Zero, 4), hits);
                hits.Sort();
                foreach (int k in hits)
                    pairs.Add(new Pair { Kind = SweepContactKind.EdgeEdge, First = eid, Second = eidsB[k], FirstOfMeshA = true });
            }
        }
    }
}
