// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Boolean operations (union, intersection, difference) of two triangle meshes, with exact arithmetic:
    /// the intersection curves, the retriangulation and the inside/outside decisions are computed exactly
    /// (ExactMeshArrangement), so touching, coplanar and nearly degenerate configurations give a closed
    /// result, at any scale. Only the output vertex coordinates are rounded to double.
    ///
    /// The solid of a mesh is where its winding number is positive (Zhou et al., "Mesh Arrangements for Solid
    /// Geometry", 2016), so self-intersecting meshes, nested and overlapping shells and duplicated faces are
    /// handled. The triangles of both meshes are split along all intersections, also of a mesh with itself.
    /// For each patch of pieces between intersection curves, the winding numbers of both meshes on its front
    /// side are counted exactly along a ray (signed crossings); the back side differs by the orientations of
    /// the triangles at the patch. A piece is part of the result if the operation is true on one side and
    /// false on the other, oriented towards the false side. Pieces where several triangles coincide are
    /// output once.
    ///
    /// The winding numbers are exact for meshes that are closed in the winding number sense (every edge used
    /// equally often in both directions; non-manifold edges and self-intersections are allowed). For other
    /// (open) meshes the generalized winding number is used (Jacobson et al. 2013, fast approximation), with the
    /// solid where it is above 0.5; then the result is generally not closed. An inside-out mesh (winding
    /// number -1) has an empty solid. Degenerate (zero-area) input
    /// triangles are dropped. The rounding of new vertices can, rarely, create tiny self-intersections.
    /// New vertices that lie exactly inside a flat region of the result or on a straight edge between two flat
    /// regions (eg the crossings of the edges of overlapping coplanar faces) are removed and the regions
    /// retriangulated (SimplifyFlatRegions, ExactPlanarSimplify), so that repeated booleans do not split the
    /// faces again and again at rounded points.
    ///
    /// Usage: construct with the two meshes and the operation, call Compute(), and read Result (a new mesh;
    /// the inputs are not modified). Compute() returns false and sets Error for null inputs, non-finite
    /// coordinates, or open meshes when AllowOpenMeshes is false; it throws InvalidOperationException in the
    /// (not expected) case that no non-degenerate ray is found. Result has no normals, colors, UVs or groups.
    /// Compute() can be called again after changing the meshes or the operation.
    ///
    /// Cost: exact predicates behind floating-point filters (BigInteger only where the filters cannot decide);
    /// slower than floating-point methods, the phases run in parallel.
    /// Prefer this class over <see cref="MeshBoolean"/> (floating-point, not robust, union of closed meshes
    /// only). Use <see cref="gs.MeshWindingNumberBoolean"/> instead when a remeshed, volumetric result is
    /// acceptable and very broken input (triangle soups) must give a closed mesh.
    ///
    /// Implemented from the papers cited here and in ExactMeshArrangement, no code taken over (docs/SOURCES.md).
    /// </summary>
    public class ExactMeshBoolean
    {
        /// <summary>
        /// the boolean operation; Difference is A minus B. With more than two meshes (Meshes): Union is inside
        /// any of them, Intersection inside all of them, Difference inside the first and none of the others.
        /// </summary>
        public enum Operations { Union = 0, Intersection = 1, Difference = 2 }

        /// <summary> result code of Compute() </summary>
        public enum BooleanStatus
        {
            /// <summary> Result is set (possibly empty, eg the intersection of disjoint meshes) </summary>
            Success = 0,
            /// <summary> no input mesh, a null mesh, or non-finite coordinates (see Error) </summary>
            InvalidInput = 1,
            /// <summary> a mesh is not closed in the winding number sense while AllowOpenMeshes is false </summary>
            OpenInput = 2,
            /// <summary> Compute() was not called yet </summary>
            NotComputed = 3
        }

        /// <summary> the input meshes of A op B (not modified); unused if Meshes is set </summary>
        public DMesh3 MeshA, MeshB;
        /// <summary>
        /// the input meshes of an operation on any number of meshes, in one arrangement (not modified). If null,
        /// the inputs are MeshA and MeshB. Mesh i is entry i of the winding number vector of WindingPredicate.
        /// </summary>
        public List<DMesh3> Meshes;
        /// <summary> the operation computed by Compute() (unless WindingPredicate is set) </summary>
        public Operations Operation;
        /// <summary>
        /// if set, decides instead of Operation where the result's solid is (Zhou et al. 2016): called with the
        /// winding numbers of all input meshes at a point (entry i for mesh i, in the order of Meshes, or A, B),
        /// returns true for inside. The winding numbers are exact integers for closed meshes; for open meshes the
        /// generalized winding number w is rounded to ceil(w - 1/2) (so w > 1/2 is at least 1). Called on both
        /// sides of every patch, from one thread; the array is reused and must not be modified or kept. A point
        /// far from all meshes has all winding numbers 0, so a predicate that is true there gives an unbounded
        /// solid and no closed result. Examples: AtLeast(k), (w) => (w[0] > 0) != (w[1] > 0) (symmetric difference).
        /// </summary>
        public Func<int[], bool> WindingPredicate;

        /// <summary> seed of the random ray directions (the result does not depend on it) </summary>
        public int Seed = 31337;

        /// <summary> the result of Compute(): a new mesh (closed and manifold for closed inputs), or null if Compute() failed </summary>
        public DMesh3 Result;
        /// <summary> why Compute() failed, otherwise null </summary>
        public string Error;
        /// <summary> result code of Compute(); Success iff Compute() returned true </summary>
        public BooleanStatus Status = BooleanStatus.NotComputed;
        /// <summary> pairs of triangles of different meshes that intersect </summary>
        public int IntersectingPairs;
        /// <summary> pairs of triangles of the same mesh that intersect (other than at shared vertices and edges) </summary>
        public int SelfIntersectingPairs;
        /// <summary> edges of the result where solids touch (more than two pieces); split into manifold pairs </summary>
        public int NonManifoldEdges;
        /// <summary> triangles that could not be added to the result mesh and got their own vertices (should be 0) </summary>
        public int NonManifoldTriangles;
        /// <summary> edges where the solid touches itself and that were split per pair of triangles (see assemble) </summary>
        public int SplitNonManifoldEdges;
        /// <summary> number of patches classified </summary>
        public int Patches;
        /// <summary>
        /// remove new vertices (not input vertices) that lie exactly inside a flat region of the result or on a
        /// straight edge between two flat regions, and retriangulate there (ExactPlanarSimplify). Without it,
        /// coplanar overlapping faces leave the crossings of their edges and diagonals in the result, which
        /// repeated booleans split at again (the number of triangles grows quickly).
        /// </summary>
        public bool SimplifyFlatRegions = true;
        /// <summary> vertices removed by SimplifyFlatRegions </summary>
        public int RemovedVertices;
        /// <summary> if false, Compute() fails for meshes that are not closed in the winding number sense </summary>
        public bool AllowOpenMeshes = true;
        /// <summary> per input mesh: winding numbers exact (closed mesh) or generalized (open mesh) </summary>
        public bool ExactWindingA, ExactWindingB;
        /// <summary> the same for every input mesh (in the order of Meshes, or A, B), after Compute() </summary>
        public bool[] ExactWinding;
        // for tests: use generalized winding numbers also for closed meshes
        internal bool ForceGeneralizedWinding = false;
        // for tests: no floating-point filters in the arrangement
        internal bool DisableFilters = false;
        /// <summary> time per phase, for diagnostics </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> sizes of the intermediate results, for diagnostics </summary>
        public Dictionary<string, long> Counts = new Dictionary<string, long>();


        /// <summary> set up the boolean of meshes a and b; call Compute() to run it </summary>
        public ExactMeshBoolean(DMesh3 a, DMesh3 b, Operations op)
        {
            MeshA = a; MeshB = b; Operation = op;
        }

        /// <summary>
        /// set up an operation on any number of meshes in one arrangement: union (inside any), intersection
        /// (inside all) or difference (inside the first and none of the others); call Compute() to run it
        /// </summary>
        public ExactMeshBoolean(IEnumerable<DMesh3> meshes, Operations op)
        {
            Meshes = new List<DMesh3>(meshes ?? new DMesh3[0]);
            Operation = op;
        }

        /// <summary>
        /// set up the solid of a winding number predicate over any number of meshes (see WindingPredicate), eg
        /// AtLeast(2) for the points inside at least two of the meshes; call Compute() to run it
        /// </summary>
        public ExactMeshBoolean(IEnumerable<DMesh3> meshes, Func<int[], bool> windingPredicate)
            : this(meshes, Operations.Union)
        {
            WindingPredicate = windingPredicate;
        }

        /// <summary> predicate for WindingPredicate: inside at least k of the meshes (winding number positive) </summary>
        public static Func<int[], bool> AtLeast(int k)
        {
            return (w) => {
                int count = 0;
                for (int i = 0; i < w.Length; ++i)
                    if (w[i] > 0 && ++count >= k)
                        return true;
                return k <= 0;
            };
        }

        /// <summary> union of any number of meshes in one arrangement; null if Compute() fails </summary>
        public static DMesh3 Union(IEnumerable<DMesh3> meshes)
        {
            var op = new ExactMeshBoolean(meshes, Operations.Union);
            return op.Compute() ? op.Result : null;
        }

        /// <summary> intersection of any number of meshes in one arrangement; null if Compute() fails </summary>
        public static DMesh3 Intersection(IEnumerable<DMesh3> meshes)
        {
            var op = new ExactMeshBoolean(meshes, Operations.Intersection);
            return op.Compute() ? op.Result : null;
        }


        ExactMeshArrangement arr;

        /// <summary>
        /// Compute the boolean into Result. Returns false (and sets Error and Status) if there is no input, an
        /// input is null, has non-finite coordinates, or is open while AllowOpenMeshes is false; otherwise true
        /// (Status Success). An empty result (for example the intersection of disjoint meshes) is a success with
        /// an empty Result mesh.
        /// </summary>
        public bool Compute()
        {
            Result = null;
            Error = null;
            // (the meshes or the operation may have changed since the last call)
            IntersectingPairs = SelfIntersectingPairs = NonManifoldEdges = NonManifoldTriangles = SplitNonManifoldEdges = Patches = RemovedVertices = 0;
            ExactWindingA = ExactWindingB = false;
            ExactWinding = null;
            PhaseMilliseconds = new Dictionary<string, long>();
            Counts = new Dictionary<string, long>();
            arr = null;
            bounds.Clear();
            windingTrees.Clear();
            List<DMesh3> inputs = Meshes ?? new List<DMesh3>() { MeshA, MeshB };
            if (inputs.Count == 0)
                return fail(BooleanStatus.InvalidInput, "no input meshes");
            foreach (DMesh3 m in inputs)
                if (m == null)
                    return fail(BooleanStatus.InvalidInput, "input mesh is null");
            foreach (DMesh3 m in inputs) {
                foreach (int vid in m.VertexIndices()) {
                    Vector3d v = m.GetVertex(vid);
                    if (double.IsNaN(v.x + v.y + v.z) || double.IsInfinity(v.x + v.y + v.z))
                        return fail(BooleanStatus.InvalidInput, "input meshes have non-finite coordinates");
                }
            }
            ExactWinding = new bool[inputs.Count];
            for (int m = 0; m < inputs.Count; ++m)
                ExactWinding[m] = IsWindingClosed(inputs[m]) && ForceGeneralizedWinding == false;
            ExactWindingA = ExactWinding[0];
            ExactWindingB = inputs.Count > 1 && ExactWinding[1];
            if (AllowOpenMeshes == false && Array.IndexOf(ExactWinding, false) >= 0)
                return fail(BooleanStatus.OpenInput, "input meshes must be closed (every edge used equally often in both directions)");

            arr = new ExactMeshArrangement(inputs) { SelfIntersections = true, DisableFilters = DisableFilters };
            arr.Compute();
            IntersectingPairs = arr.IntersectingPairs - arr.SelfIntersectingPairs;
            SelfIntersectingPairs = arr.SelfIntersectingPairs;
            PhaseMilliseconds = new Dictionary<string, long>(arr.PhaseMilliseconds);
            Counts["candidate_pairs"] = arr.CandidatePairs;
            Counts["intersecting_pairs"] = arr.IntersectingPairs;
            Counts["points"] = arr.PointCount;
            Counts["merged_points"] = arr.MergedPoints;
            Counts["pieces"] = arr.Output.Count;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool[] keep, flip;
            windingSides = windingExactSides = 0;
            classify(out keep, out flip);
            PhaseMilliseconds["classification"] = sw.ElapsedMilliseconds; sw.Restart();
            Counts["winding_plane_sides"] = windingSides;
            Counts["winding_plane_sides_exact"] = windingExactSides;
            Result = assemble(keep, flip);
            PhaseMilliseconds["assembly"] = sw.ElapsedMilliseconds;
            Counts["removed_vertices"] = RemovedVertices;
            Status = BooleanStatus.Success;
            return true;
        }

        bool fail(BooleanStatus status, string error)
        {
            Status = status;
            Error = error;
            return false;
        }


        /// <summary>
        /// The boundary of the solid of a mesh (where its winding number is positive): self-intersections,
        /// nested shells and duplicated faces are resolved exactly. Returns null if Compute() fails.
        /// </summary>
        public static DMesh3 Resolve(DMesh3 mesh)
        {
            var op = new ExactMeshBoolean(mesh, new DMesh3(), Operations.Union);
            return op.Compute() ? op.Result : null;
        }


        /// <summary>
        /// Every edge is used equally often in both directions (by vertex position, so unwelded copies of a
        /// vertex count as the same). Then the winding number is an integer, the same along every ray.
        /// </summary>
        public static bool IsWindingClosed(DMesh3 mesh)
        {
            // fast path: every edge of the mesh has two triangles that run through it in opposite directions
            // (then the edges are also balanced by position, merging vertices keeps the sums)
            bool byIds = true;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) { byIds = false; break; }
                Index2i ev = mesh.GetEdgeV(eid);
                if (runs(mesh.GetTriangle(et.a), ev.a, ev.b) == runs(mesh.GetTriangle(et.b), ev.a, ev.b)) { byIds = false; break; }
            }
            if (byIds)
                return true;

            var ids = new Dictionary<Vector3d, int>();
            Func<int, int> id = (vid) => {
                Vector3d v = mesh.GetVertex(vid);
                if (v.x == 0) v.x = 0;      // -0.0
                if (v.y == 0) v.y = 0;
                if (v.z == 0) v.z = 0;
                int k;
                if (ids.TryGetValue(v, out k) == false) {
                    k = ids.Count;
                    ids[v] = k;
                }
                return k;
            };
            var count = new Dictionary<long, int>(EdgeKeyComparer.Instance);
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                int[] v = { id(t.a), id(t.b), id(t.c) };
                for (int k = 0; k < 3; ++k) {
                    int x = v[k], y = v[(k + 1) % 3];
                    if (x == y) continue;
                    // +1 for x < y, -1 for the reverse direction
                    long key = (x < y) ? (((long)x << 32) | (uint)y) : (((long)y << 32) | (uint)x);
                    int d = (x < y) ? 1 : -1;
                    int c0;
                    count.TryGetValue(key, out c0);
                    count[key] = c0 + d;
                }
            }
            foreach (int c in count.Values)
                if (c != 0)
                    return false;
            return true;
        }


        // the winding numbers at a point: winding[m] for every mesh m, of which only the meshes in touched can
        // be nonzero (the others are 0)
        bool inside(int[] winding, List<int> touched)
        {
            if (WindingPredicate != null)
                return WindingPredicate(winding);
            int count = 0;
            foreach (int m in touched)
                if (winding[m] > 0)
                    count++;
            switch (Operation) {
                case Operations.Union: return count > 0;
                case Operations.Intersection: return count == winding.Length;
                default: return winding[0] > 0 && count == 1;
            }
        }


        void classify(out bool[] keep, out bool[] flip)
        {
            List<ExactMeshArrangement.OutputTriangle> output = arr.Output;
            int n = output.Count;
            keep = new bool[n];
            flip = new bool[n];

            // stacks: the input triangles that contain a piece (its source, and coplanar overlapping triangles);
            // the piece is output only from the stack's first triangle
            var stack = new List<int>[n];
            bool[] representative = new bool[n];
            for (int i = 0; i < n; ++i) {
                var ot = output[i];
                var src = arr.Triangles[ot.Source];
                representative[i] = true;
                if (src.Coplanar.Count == 0)
                    continue;
                int c = centroid(ot);
                foreach (int ui in src.Coplanar) {
                    if (inside_coplanar(src, arr.Triangles[ui], c)) {
                        if (stack[i] == null)
                            stack[i] = new List<int>() { ot.Source };
                        stack[i].Add(ui);
                        if (ui < ot.Source)
                            representative[i] = false;
                    }
                }
            }

            // patches: pieces of one mesh connected across edges that are not on an intersection curve, with
            // exactly two pieces of that mesh (then they are oriented consistently, the meshes are winding
            // closed); the winding numbers are the same on the front sides of all pieces of a patch
            long[] edgeKeys; int[] edgeTri;
            group_edges(n, (i) => new Index3i(output[i].A, output[i].B, output[i].C), out edgeKeys, out edgeTri);
            int[] parent = new int[n];
            for (int i = 0; i < n; ++i)
                parent[i] = i;
            Func<int, int> find = (x) => {
                while (parent[x] != x) {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            };
            var atEdge = new List<int>();
            for (int g0 = 0, g1 = 0; g0 < edgeKeys.Length; g0 = g1) {
                g1 = g0 + 1;
                while (g1 < edgeKeys.Length && edgeKeys[g1] == edgeKeys[g0]) g1++;
                if (arr.ConstraintEdges.Contains(edgeKeys[g0]))
                    continue;
                // the pieces at the edge by mesh (stable insertion sort, the groups are small)
                atEdge.Clear();
                for (int g = g0; g < g1; ++g) {
                    int t = edgeTri[g], k = atEdge.Count;
                    atEdge.Add(t);
                    while (k > 0 && output[atEdge[k - 1]].Mesh > output[t].Mesh) {
                        atEdge[k] = atEdge[k - 1];
                        k--;
                    }
                    atEdge[k] = t;
                }
                for (int s0 = 0, s1 = 0; s0 < atEdge.Count; s0 = s1) {
                    s1 = s0 + 1;
                    while (s1 < atEdge.Count && output[atEdge[s1]].Mesh == output[atEdge[s0]].Mesh) s1++;
                    if (s1 - s0 != 2)
                        continue;
                    int a = atEdge[s0], b = atEdge[s0 + 1];
                    if (same_stack(stack[a], stack[b]) == false)
                        continue;
                    int ra = find(a), rb = find(b);
                    if (ra != rb)
                        parent[ra] = rb;
                }
            }

            // winding numbers in front of each patch, sparse (the nonzero ones, as pairs mesh, winding number):
            // a closed mesh has winding number 0 outside its box, so only the meshes whose box contains the
            // piece are evaluated
            int nm = arr.Meshes.Count;
            var patchWinding = new Dictionary<int, int[]>();
            var rng = new Random(Seed);
            var closedBoxes = new List<AxisAlignedBox3d>();
            var openMeshes = new List<int>();
            for (int m = 0; m < nm; ++m) {
                closedBoxes.Add(ExactWinding[m] ? input_bounds(m) : AxisAlignedBox3d.Empty);
                if (ExactWinding[m] == false)
                    openMeshes.Add(m);
            }
            var closedTree = new TriangleBoxTree(closedBoxes);
            var candidates = new List<int>();
            var sparse = new List<int>();
            int[] winding = new int[nm];
            bool[] isTouched = new bool[nm];
            var touched = new List<int>();
            Action<int, int> add = (m, d) => {
                if (isTouched[m] == false) {
                    isTouched[m] = true;
                    touched.Add(m);
                }
                winding[m] += d;
            };
            for (int i = 0; i < n; ++i) {
                if (representative[i] == false)
                    continue;
                var ot = output[i];
                int root = find(i);
                int[] w;
                if (patchWinding.TryGetValue(root, out w) == false) {
                    int p = centroid(ot);
                    Vector3d pv = arr.ToInputVector(arr.Exact(p));
                    double pad = 1e-9 * (Math.Abs(pv.x) + Math.Abs(pv.y) + Math.Abs(pv.z)) + 1e-300;
                    candidates.Clear();
                    closedTree.Query(new AxisAlignedBox3d(pv - pad * Vector3d.One, pv + pad * Vector3d.One), (m) => candidates.Add(m));
                    candidates.AddRange(openMeshes);
                    candidates.Sort();
                    sparse.Clear();
                    foreach (int m in candidates) {
                        int wm;
                        if (ExactWinding[m]) {
                            wm = front_winding(i, m, arr.Trees[m], rng, stack[i], p);
                        } else {
                            // generalized winding numbers are thresholded at 1/2: ceil(w - 1/2) > 0 iff w > 1/2,
                            // and it commutes with adding the integer changes across the patch
                            wm = (int)Math.Ceiling(front_generalized_winding(i, m, stack[i]) - 0.5);
                        }
                        if (wm != 0) {
                            sparse.Add(m);
                            sparse.Add(wm);
                        }
                    }
                    w = sparse.ToArray();
                    patchWinding[root] = w;
                    Patches++;
                }
                for (int k = 0; k < w.Length; k += 2)
                    add(w[k], w[k + 1]);
                bool inFront = inside(winding, touched);
                // back side: crossing the stack from the back to the front changes the winding number of a
                // mesh by -1 for each of its triangles oriented like the piece, +1 for opposite ones
                if (stack[i] == null) {
                    add(ot.Mesh, 1);
                } else {
                    var src = arr.Triangles[ot.Source];
                    foreach (int ti in stack[i]) {
                        var U = arr.Triangles[ti];
                        BigInteger dot = src.NX * U.NX + src.NY * U.NY + src.NZ * U.NZ;
                        add(U.Mesh, (dot.Sign > 0) ? 1 : -1);
                    }
                }
                bool inBack = inside(winding, touched);
                keep[i] = inFront != inBack;
                flip[i] = inFront;
                foreach (int m in touched) {
                    winding[m] = 0;
                    isTouched[m] = false;
                }
                touched.Clear();
            }
        }

        // the undirected edges of n triangles, sorted: edgeKeys[k] is the edge, edgeTri[k] the triangle; equal
        // edges are adjacent
        static void group_edges(int n, Func<int, Index3i> tri, out long[] edgeKeys, out int[] edgeTri)
        {
            edgeKeys = new long[3 * n];
            edgeTri = new int[3 * n];
            for (int i = 0; i < n; ++i) {
                Index3i t = tri(i);
                edgeKeys[3 * i] = ExactMeshArrangement.EdgeKey(t.a, t.b);
                edgeKeys[3 * i + 1] = ExactMeshArrangement.EdgeKey(t.b, t.c);
                edgeKeys[3 * i + 2] = ExactMeshArrangement.EdgeKey(t.c, t.a);
                edgeTri[3 * i] = edgeTri[3 * i + 1] = edgeTri[3 * i + 2] = i;
            }
            Array.Sort(edgeKeys, edgeTri);
        }

        static bool same_stack(List<int> a, List<int> b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Count != b.Count)
                return false;
            var set = new HashSet<int>(a);
            foreach (int x in b)
                if (set.Contains(x) == false)
                    return false;
            return true;
        }


        int centroid(ExactMeshArrangement.OutputTriangle ot)
        {
            ExactPoint3 a = arr.Exact(ot.A), b = arr.Exact(ot.B), c = arr.Exact(ot.C);
            BigInteger wbc = b.W * c.W, wac = a.W * c.W, wab = a.W * b.W;
            return arr.AddPoint(ExactPoint3.Create(
                a.X * wbc + b.X * wac + c.X * wab,
                a.Y * wbc + b.Y * wac + c.Y * wab,
                a.Z * wbc + b.Z * wac + c.Z * wab,
                3 * a.W * wbc));
        }

        // point p (in the common plane of t and u) is in the closed triangle u
        bool inside_coplanar(ExactMeshArrangement.InputTriangle t, ExactMeshArrangement.InputTriangle u, int p)
        {
            int s = arr.Orient(t, u.Corners[0], u.Corners[1], u.Corners[2]);
            for (int k = 0; k < 3; ++k)
                if (arr.Orient(t, u.Corners[k], u.Corners[(k + 1) % 3], p) * s < 0)
                    return false;
            return true;
        }


        /// <summary>
        /// Winding number of the mesh just in front of output piece i (on the side its source triangle's normal
        /// points to): signed crossings of a segment from the piece's centroid to a far point in front, without
        /// the triangles of the piece's stack. Exact; a new random direction is used if the segment passes
        /// through an edge or vertex.
        /// </summary>
        int front_winding(int i, int mesh, TriangleBoxTree tree, Random rng, List<int> stack, int p)
        {
            var ot = arr.Output[i];
            var src = arr.Triangles[ot.Source];
            List<int> tris = arr.MeshTriangles[mesh];

            BigInteger[] min, max;
            mesh_bounds(mesh, out min, out max);
            ExactPoint3 P = arr.Exact(p);
            // outside the box of the mesh: winding number 0
            for (int k = 0; k < 3; ++k)
                if (P[k] < min[k] * P.W || P[k] > max[k] * P.W)
                    return 0;
            BigInteger ext = BigInteger.One;
            for (int k = 0; k < 3; ++k)
                ext = BigInteger.Max(ext, max[k] - min[k] + 1);

            Vector3d pv = arr.ToInputVector(P);
            // pad: the rounding of the point to double (relative 1e-16), with a wide margin
            double pad = 1e-9 * (Math.Abs(pv.x) + Math.Abs(pv.y) + Math.Abs(pv.z)) + 1e-300;
            // grid coordinates of the point as doubles, for the filtered plane sides
            double gx = ExactPoint3.ToDouble(P.X, P.W, 0), gy = ExactPoint3.ToDouble(P.Y, P.W, 0), gz = ExactPoint3.ToDouble(P.Z, P.W, 0);
            for (int attempt = 0; attempt < 200; ++attempt) {
                int axis = rng.Next(3);
                long[] dir = new long[3];
                for (int k = 0; k < 3; ++k)
                    dir[k] = rng.Next(-1000, 1001);
                dir[axis] = (rng.Next(2) == 0) ? -1000 : 1000;
                BigInteger[] R = new BigInteger[3];
                for (int k = 0; k < 3; ++k)
                    R[k] = (min[k] + max[k]) / 2 + dir[k] * ext * 4;
                ExactPoint3 Rp = ExactPoint3.FromInteger(R[0], R[1], R[2]);
                // the segment must start into the front side of the piece
                BigInteger front = src.NX * (Rp.X * P.W - P.X) + src.NY * (Rp.Y * P.W - P.Y) + src.NZ * (Rp.Z * P.W - P.Z);
                if (front.Sign <= 0)
                    continue;
                Vector3d rv = arr.ToInputVector(Rp);
                double rx = (double)Rp.X, ry = (double)Rp.Y, rz = (double)Rp.Z;
                Vector3d gp = new Vector3d(gx, gy, gz), gr = new Vector3d(rx, ry, rz);

                int winding = 0;
                bool degenerate = false;
                tree.QuerySegment(pv, rv, pad, (k) => {
                    if (degenerate) return;
                    int ti = tris[k];
                    var U = arr.Triangles[ti];
                    if (U.Degenerate || ti == ot.Source || (stack != null && stack.Contains(ti))) return;
                    // sides of the plane: in doubles where the error bound decides, else exact
                    windingSides += 2;
                    int sr = DisableFilters ? 2 : plane_side_filtered(U.PlaneApprox, rx, ry, rz);
                    if (sr == 2) {
                        sr = (U.P.A * Rp.X + U.P.B * Rp.Y + U.P.C * Rp.Z - U.P.D).Sign;
                        windingExactSides++;
                    }
                    if (sr == 0) { degenerate = true; return; }
                    int sp = DisableFilters ? 2 : plane_side_filtered(U.PlaneApprox, gx, gy, gz);
                    if (sp == 2) {
                        sp = (U.P.A * P.X + U.P.B * P.Y + U.P.C * P.Z - U.P.D * P.W).Sign;
                        windingExactSides++;
                    }
                    // sp == 0: the point is in the plane but not in the triangle (it would be in the stack)
                    if (sp == 0 || sp == sr) return;
                    ExactPoint3 c0 = arr.Exact(U.Corners[0]), c1 = arr.Exact(U.Corners[1]), c2 = arr.Exact(U.Corners[2]);
                    Vector3d d0 = grid_double(c0), d1 = grid_double(c1), d2 = grid_double(c2);
                    int o0 = orient3d(P, Rp, c0, c1, ref gp, ref gr, ref d0, ref d1);
                    int o1 = orient3d(P, Rp, c1, c2, ref gp, ref gr, ref d1, ref d2);
                    int o2 = orient3d(P, Rp, c2, c0, ref gp, ref gr, ref d2, ref d0);
                    if (o0 == 0 || o1 == 0 || o2 == 0) {
                        // the line passes through an edge or vertex: only a problem if it is the triangle's
                        if ((o0 >= 0 && o1 >= 0 && o2 >= 0) || (o0 <= 0 && o1 <= 0 && o2 <= 0))
                            degenerate = true;
                        return;
                    }
                    // leaving through the front side (towards the normal) counts +1, entering -1
                    if (o0 == o1 && o1 == o2)
                        winding += sr;
                });
                if (degenerate == false)
                    return winding;
            }
            throw new InvalidOperationException("ExactMeshBoolean: no non-degenerate ray found");
        }

        // plane sides evaluated by front_winding, and how many of them exactly (the filter could not decide)
        long windingSides, windingExactSides;

        /// <summary>
        /// sign of A x + B y + C z - D for a plane given by doubles pa[0..3] (each with relative error at most
        /// 2^-52, ExactMeshArrangement.InputTriangle.PlaneApprox) at a point given by doubles (relative error at
        /// most 2^-52 each); 2 if the error bound does not decide (also for overflow or tiny values)
        /// </summary>
        internal static int plane_side_filtered(double[] pa, double x, double y, double z)
        {
            double a = pa[0] * x, b = pa[1] * y, c = pa[2] * z, d = pa[3];
            double mag = Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(d);
            // per term: 2^-52 (coefficient) + 2^-52 (coordinate) + 2^-53 (product); the sum adds 3 * 2^-53 of
            // mag: below 8 * 2^-52 * mag, taken generously
            double bound = 1e-14 * mag;
            if (!(mag < double.MaxValue) || mag < 1e-290)
                return 2;
            double v = a + b + c - d;
            if (v > bound) return 1;
            if (v < -bound) return -1;
            return 2;
        }

        readonly Dictionary<int, DMeshAABBTree3> windingTrees = new Dictionary<int, DMeshAABBTree3>();

        /// <summary>
        /// Generalized winding number of an open mesh just in front of output piece i: the fast winding number at
        /// the piece's centroid without the triangles the centroid lies on (they are evaluated directly by the
        /// tree, so subtracting the same terms removes them), each of which adds -1/2 in front (+1/2 behind)
        /// if oriented like the piece, the opposite otherwise.
        /// </summary>
        double front_generalized_winding(int i, int mesh, List<int> stack)
        {
            var ot = arr.Output[i];
            var src = arr.Triangles[ot.Source];
            DMesh3 m = arr.Meshes[mesh];
            DMeshAABBTree3 tree;
            if (windingTrees.TryGetValue(mesh, out tree) == false) {
                windingTrees[mesh] = tree = new DMeshAABBTree3(m, true);
                tree.FastWindingNumber(Vector3d.Zero);
            }
            // (the generalized winding number is approximate: the centroid of the corners in doubles is enough)
            Vector3d pv = (arr.PointToVector(ot.A) + arr.PointToVector(ot.B) + arr.PointToVector(ot.C)) / 3;
            double w = tree.FastWindingNumber(pv);
            List<int> on = stack ?? new List<int>() { ot.Source };
            foreach (int ti in on) {
                var U = arr.Triangles[ti];
                if (U.Mesh != mesh || U.Degenerate)
                    continue;
                Index3i t = m.GetTriangle(U.Tid);
                w -= MathUtil.TriSolidAngle(m.GetVertex(t.a), m.GetVertex(t.b), m.GetVertex(t.c), ref pv) / MathUtil.FourPI;
                BigInteger dot = src.NX * U.NX + src.NY * U.NY + src.NZ * U.NZ;
                w -= (dot.Sign > 0) ? 0.5 : -0.5;
            }
            return w;
        }

        readonly Dictionary<int, BigInteger[][]> bounds = new Dictionary<int, BigInteger[][]>();

        // box of the input coordinates of a mesh (Empty for a mesh without vertices)
        AxisAlignedBox3d input_bounds(int mesh)
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            foreach (int vid in arr.Meshes[mesh].VertexIndices())
                box.Contain(arr.Meshes[mesh].GetVertex(vid));
            return box;
        }

        void mesh_bounds(int mesh, out BigInteger[] min, out BigInteger[] max)
        {
            BigInteger[][] b;
            if (bounds.TryGetValue(mesh, out b) == false) {
                b = new[] { new BigInteger[3], new BigInteger[3] };
                bool first = true;
                // (from the extreme doubles: the grid map is monotone)
                Vector3d lo = Vector3d.Zero, hi = Vector3d.Zero;
                foreach (int vid in arr.Meshes[mesh].VertexIndices()) {
                    Vector3d v = arr.Meshes[mesh].GetVertex(vid);
                    for (int k = 0; k < 3; ++k) {
                        if (first || v[k] < lo[k]) lo[k] = v[k];
                        if (first || v[k] > hi[k]) hi[k] = v[k];
                    }
                    first = false;
                }
                for (int k = 0; k < 3 && first == false; ++k) {
                    b[0][k] = arr.GridValue(lo[k], k);
                    b[1][k] = arr.GridValue(hi[k], k);
                }
                bounds[mesh] = b;
            }
            min = b[0]; max = b[1];
        }

        // sign of the 4x4 determinant of homogeneous points (consistent orientation, used for crossing tests)
        // the grid coordinates of the point as doubles (each with relative error at most 2^-52)
        static Vector3d grid_double(ExactPoint3 p)
        {
            if (p.W.IsOne)
                return new Vector3d((double)p.X, (double)p.Y, (double)p.Z);
            return new Vector3d(ExactPoint3.ToDouble(p.X, p.W, 0), ExactPoint3.ToDouble(p.Y, p.W, 0), ExactPoint3.ToDouble(p.Z, p.W, 0));
        }

        /// <summary>
        /// orient3d(a, b, c, d) with a floating-point filter: the doubles da..dd approximate the points (relative
        /// error at most 2^-52 per coordinate); exact if the error bound does not decide (or DisableFilters)
        /// </summary>
        int orient3d(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d, ref Vector3d da, ref Vector3d db, ref Vector3d dc, ref Vector3d dd)
        {
            if (DisableFilters == false) {
                int s = orient3d_filtered(ref da, ref db, ref dc, ref dd);
                if (s != 2)
                    return s;
            }
            return orient3d(a, b, c, d);
        }

        /// <summary>
        /// sign of the homogeneous 4x4 determinant of orient3d for points given approximately by doubles (relative
        /// error at most 2^-52 per coordinate, positive weights), or 2 if the error bound does not decide.
        /// det4 [a 1; b 1; c 1; d 1] = -det3 [b-a; c-a; d-a]; the rows u, v, w of the 3x3 determinant are
        /// computed with absolute errors eu, ev, ew (inputs and subtraction), which change the determinant by at
        /// most (|u|+eu)(|v|+ev)(|w|+ew) - |u||v||w| (expanding it multilinearly, Hadamard's inequality for
        /// each term); the evaluation adds at most 1e-15 times the permanent (Shewchuk's orient3d bound).
        /// </summary>
        internal static int orient3d_filtered(ref Vector3d a, ref Vector3d b, ref Vector3d c, ref Vector3d d)
        {
            double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            double wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
            double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;
            double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);
            double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy)) + Math.Abs(wy) * (Math.Abs(uzvx) + Math.Abs(uxvz))
                + Math.Abs(wz) * (Math.Abs(uxvy) + Math.Abs(uyvx));
            // per coordinate: 2^-52 of each input (relative) and 2^-53 of the difference, taken as 3 * 2^-52 of
            // the sum of the inputs' magnitudes
            const double rel = 3 * 2.220446049250313e-16;
            double ax = Math.Abs(a.x), ay = Math.Abs(a.y), az = Math.Abs(a.z);
            double eu = rel * norm(Math.Abs(b.x) + ax, Math.Abs(b.y) + ay, Math.Abs(b.z) + az);
            double ev = rel * norm(Math.Abs(c.x) + ax, Math.Abs(c.y) + ay, Math.Abs(c.z) + az);
            double ew = rel * norm(Math.Abs(d.x) + ax, Math.Abs(d.y) + ay, Math.Abs(d.z) + az);
            double nu = norm(ux, uy, uz), nv = norm(vx, vy, vz), nw = norm(wx, wy, wz);
            // (nu+eu)(nv+ev)(nw+ew) - nu nv nw, expanded (positive terms, no cancellation)
            double perturb = eu * nv * nw + nu * ev * nw + nu * nv * ew + eu * ev * nw + eu * nv * ew + nu * ev * ew + eu * ev * ew;
            double bound = 1.01 * (perturb + 1e-15 * permanent);
            if (!(bound < double.MaxValue) || !(Math.Abs(det) < double.MaxValue) || permanent < 1e-280)
                return 2;
            if (det > bound) return -1;
            if (det < -bound) return 1;
            return 2;
        }

        // Euclidean norm, rounded up (relative 1e-15)
        static double norm(double x, double y, double z)
        {
            return Math.Sqrt(x * x + y * y + z * z) * (1 + 1e-15);
        }

        static int orient3d(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
        {
            BigInteger[,] m = new BigInteger[4, 4];
            ExactPoint3[] pts = { a, b, c, d };
            for (int k = 0; k < 4; ++k) {
                m[k, 0] = pts[k].X; m[k, 1] = pts[k].Y; m[k, 2] = pts[k].Z; m[k, 3] = pts[k].W;
            }
            return ExactMeshArrangement.Det4(m).Sign;
        }


        /// <summary>
        /// The kept pieces as a mesh. Where more than two pieces meet at an edge (solids touching along the edge),
        /// each piece is paired with its neighbour around the edge on its solid side, and the edge vertices are
        /// duplicated per pair, so that the result is closed and manifold. Likewise vertices where solids touch.
        /// </summary>
        DMesh3 assemble(bool[] keep, bool[] flip)
        {
            var tris = new List<Index3i>();
            var source = new List<int>();
            var flipped = new List<bool>();
            for (int i = 0; i < arr.Output.Count; ++i) {
                if (keep[i] == false)
                    continue;
                var ot = arr.Output[i];
                tris.Add(flip[i] ? new Index3i(ot.A, ot.C, ot.B) : new Index3i(ot.A, ot.B, ot.C));
                source.Add(ot.Source);
                flipped.Add(flip[i]);
            }
            if (SimplifyFlatRegions) {
                // new points inside flat regions and on straight edges between them are not needed
                var inputPoints = new HashSet<int>();
                for (int m = 0; m < arr.VertexPoints.Length; ++m)
                    foreach (int vid in arr.Meshes[m].VertexIndices())
                        inputPoints.Add(arr.Canonical(arr.VertexPoints[m][vid]));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var simplify = new ExactPlanarSimplify(arr, tris, source, flipped);
                simplify.Run(inputPoints);
                tris = simplify.Result();
                RemovedVertices = simplify.Removed;
                PhaseMilliseconds["simplify"] = sw.ElapsedMilliseconds;
            }

            // corner slots (3 per triangle) joined across the edges where the triangles are neighbours
            int[] parent = new int[3 * tris.Count];
            for (int k = 0; k < parent.Length; ++k)
                parent[k] = k;
            Func<int, int> find = (x) => {
                while (parent[x] != x) {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            };
            Action<int, int> join = (x, y) => {
                int rx = find(x), ry = find(y);
                if (rx != ry) parent[rx] = ry;
            };
            // slot of point p in triangle t
            Func<int, int, int> slot = (t, p) => 3 * t + ((tris[t].a == p) ? 0 : (tris[t].b == p) ? 1 : 2);

            long[] edgeKeys; int[] edgeTri;
            group_edges(tris.Count, (t) => tris[t], out edgeKeys, out edgeTri);
            var list = new List<int>();
            // the pairs at the edges with more than two pieces
            var nonManifold = new List<KeyValuePair<long, List<KeyValuePair<int, int>>>>();
            for (int g0 = 0, g1 = 0; g0 < edgeKeys.Length; g0 = g1) {
                g1 = g0 + 1;
                while (g1 < edgeKeys.Length && edgeKeys[g1] == edgeKeys[g0]) g1++;
                int u = (int)(edgeKeys[g0] >> 32), v = (int)(edgeKeys[g0] & 0xFFFFFFFF);
                if (g1 - g0 == 2) {
                    join(slot(edgeTri[g0], u), slot(edgeTri[g0 + 1], u));
                    join(slot(edgeTri[g0], v), slot(edgeTri[g0 + 1], v));
                    continue;
                }
                list.Clear();
                for (int g = g0; g < g1; ++g)
                    list.Add(edgeTri[g]);
                var pairs = pair_around_edge(tris, list, u, v);
                foreach (var pair in pairs) {
                    join(slot(pair.Key, u), slot(pair.Value, u));
                    join(slot(pair.Key, v), slot(pair.Value, v));
                }
                if (g1 - g0 > 2) {
                    NonManifoldEdges++;
                    nonManifold.Add(new KeyValuePair<long, List<KeyValuePair<int, int>>>(edgeKeys[g0], pairs));
                }
            }

            // coordinates of the points used (rounded from the exact points, in parallel)
            var used = new List<int>();
            var usedIndex = new Dictionary<int, int>();
            foreach (Index3i t in tris)
                for (int k = 0; k < 3; ++k)
                    if (usedIndex.ContainsKey(t[k]) == false) {
                        usedIndex[t[k]] = used.Count;
                        used.Add(t[k]);
                    }
            Vector3d[] coords = arr.PointsToVectors(used);

            // vertices: one per class of corner slots
            DMesh3 result = new DMesh3();
            var vmap = new Dictionary<int, int>();
            int[] vids = new int[3 * tris.Count];
            for (int t = 0; t < tris.Count; ++t) {
                for (int k = 0; k < 3; ++k) {
                    int root = find(3 * t + k), vid;
                    if (vmap.TryGetValue(root, out vid) == false) {
                        vid = result.AppendVertex(coords[usedIndex[tris[t][k]]]);
                        vmap[root] = vid;
                    }
                    vids[3 * t + k] = vid;
                }
            }

            // where the solid touches itself along an edge and its surface is connected around both ends, the pairs
            // at the edge get the same two vertices (the ends cannot be duplicated), which would give an edge with
            // more than two triangles: then the edge is split for each pair at a new vertex of its own (at the
            // midpoint, the same position for all pairs, like other contacts), so that every edge has two triangles
            var splitAt = new Dictionary<int, int>();       // slot 3 t + k (edge from corner k to k + 1) -> vertex
            foreach (var nm in nonManifold) {
                int u = (int)(nm.Key >> 32), v = (int)(nm.Key & 0xFFFFFFFF);
                var byVertices = new Dictionary<long, List<KeyValuePair<int, int>>>(EdgeKeyComparer.Instance);
                foreach (var pair in nm.Value) {
                    long key = ExactMeshArrangement.EdgeKey(vids[slot(pair.Key, u)], vids[slot(pair.Key, v)]);
                    List<KeyValuePair<int, int>> group;
                    if (byVertices.TryGetValue(key, out group) == false)
                        byVertices[key] = group = new List<KeyValuePair<int, int>>();
                    group.Add(pair);
                }
                foreach (var group in byVertices.Values) {
                    if (group.Count < 2)
                        continue;
                    SplitNonManifoldEdges++;
                    foreach (var pair in group) {
                        Vector3d mid = 0.5 * (coords[usedIndex[u]] + coords[usedIndex[v]]);
                        int m = result.AppendVertex(mid);
                        foreach (int t in new[] { pair.Key, pair.Value }) {
                            int ku = slot(t, u) - 3 * t, kv = slot(t, v) - 3 * t;
                            int k = ((ku + 1) % 3 == kv) ? ku : kv;     // the edge (u, v) or (v, u) of t
                            splitAt[3 * t + k] = m;
                        }
                    }
                }
            }

            var poly = new List<int>();
            for (int t = 0; t < tris.Count; ++t) {
                poly.Clear();
                int first = -1;
                for (int k = 0; k < 3; ++k) {
                    poly.Add(vids[3 * t + k]);
                    int m;
                    if (splitAt.TryGetValue(3 * t + k, out m)) {
                        if (first < 0) first = poly.Count;
                        poly.Add(m);
                    }
                }
                if (first < 0) {
                    append_triangle(result, poly[0], poly[1], poly[2]);
                    continue;
                }
                // fan from a split vertex: it is on one edge, so no fan triangle is degenerate
                int n = poly.Count;
                for (int j = 1; j + 1 < n; ++j)
                    append_triangle(result, poly[first], poly[(first + j) % n], poly[(first + j + 1) % n]);
            }
            return result;
        }

        void append_triangle(DMesh3 result, int a, int b, int c)
        {
            if (result.AppendTriangle(a, b, c) >= 0)
                return;
            // (an edge with more than two triangles that could not be split: kept, with its own vertices)
            NonManifoldTriangles++;
            int a2 = result.AppendVertex(result.GetVertex(a));
            int b2 = result.AppendVertex(result.GetVertex(b));
            int c2 = result.AppendVertex(result.GetVertex(c));
            result.AppendTriangle(a2, b2, c2);
        }

        // pairs of triangles that are neighbours across edge (u, v): for two triangles the pair, for more each
        // triangle with the next one around the edge on its back (solid) side, which runs the other way
        List<KeyValuePair<int, int>> pair_around_edge(List<Index3i> tris, List<int> list, int u, int v)
        {
            var pairs = new List<KeyValuePair<int, int>>();
            if (list.Count < 2)
                return pairs;
            if (list.Count == 2) {
                pairs.Add(new KeyValuePair<int, int>(list[0], list[1]));
                return pairs;
            }
            // the triangles' half-planes in the order of increasing angle around the axis u -> v (exact: half-planes
            // can differ by less than the rounding of the points), from the half-plane of the first triangle
            ExactPoint3 U = arr.Exact(u), V = arr.Exact(v);
            BigInteger[] axis = scaled_difference(V, U);
            var dir = new Dictionary<int, BigInteger[]>();
            var half = new Dictionary<int, int>();
            BigInteger[] r = null, y = null;
            foreach (int t in list) {
                Index3i f = tris[t];
                BigInteger[] d = scaled_difference(arr.Exact(f.a + f.b + f.c - u - v), U);
                if (r == null) {
                    r = d;
                    y = cross(axis, r);
                }
                dir[t] = d;
                // 0: the first half-plane, 1: angle in (0, pi), 2: pi, 3: (pi, 2 pi)
                int s = dot(y, d).Sign;
                half[t] = (s > 0) ? 1 : (s < 0) ? 3 : (dot(cross(axis, d), y).Sign > 0) ? 0 : 2;
            }
            var sorted = new List<int>(list);
            sorted.Sort((a, b) => {
                int c = half[a].CompareTo(half[b]);
                if (c == 0 && (half[a] & 1) == 1)
                    c = -dot(cross(axis, dir[a]), dir[b]).Sign;      // b at a larger angle than a: positive
                return (c != 0) ? c : a.CompareTo(b);
            });
            // a triangle running u -> v has its normal towards increasing angle: its solid is at decreasing angle
            var used = new HashSet<int>();
            int n = sorted.Count;
            for (int k = 0; k < n; ++k) {
                int t = sorted[k];
                if (runs(tris[t], u, v) == false || used.Contains(t))
                    continue;
                int next = sorted[(k - 1 + n) % n];
                if (runs(tris[next], v, u) && used.Contains(next) == false) {
                    pairs.Add(new KeyValuePair<int, int>(t, next));
                    used.Add(t); used.Add(next);
                }
            }
            return pairs;
        }

        // p - q times the positive factor p.W q.W
        static BigInteger[] scaled_difference(ExactPoint3 p, ExactPoint3 q)
        {
            return new[] { p.X * q.W - q.X * p.W, p.Y * q.W - q.Y * p.W, p.Z * q.W - q.Z * p.W };
        }

        static BigInteger[] cross(BigInteger[] a, BigInteger[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        static BigInteger dot(BigInteger[] a, BigInteger[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        static bool runs(Index3i t, int u, int v)
        {
            return (t.a == u && t.b == v) || (t.b == u && t.c == v) || (t.c == u && t.a == v);
        }
    }
}
