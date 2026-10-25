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
    ///
    /// Implemented from the papers cited here and in ExactMeshArrangement, no code taken over (docs/SOURCES.md).
    /// </summary>
    public class ExactMeshBoolean
    {
        public enum Operations { Union = 0, Intersection = 1, Difference = 2 }

        public DMesh3 MeshA, MeshB;
        public Operations Operation;

        /// <summary> seed of the random ray directions (the result does not depend on it) </summary>
        public int Seed = 31337;

        public DMesh3 Result;
        /// <summary> why Compute() failed, otherwise null </summary>
        public string Error;
        /// <summary> pairs of triangles of different meshes that intersect </summary>
        public int IntersectingPairs;
        /// <summary> pairs of triangles of the same mesh that intersect (other than at shared vertices and edges) </summary>
        public int SelfIntersectingPairs;
        /// <summary> edges of the result where solids touch (more than two pieces); split into manifold pairs </summary>
        public int NonManifoldEdges;
        /// <summary> triangles that could not be added to the result mesh and got their own vertices (should be 0) </summary>
        public int NonManifoldTriangles;
        /// <summary> number of patches classified </summary>
        public int Patches;
        /// <summary> if false, Compute() fails for meshes that are not closed in the winding number sense </summary>
        public bool AllowOpenMeshes = true;
        /// <summary> per input mesh: winding numbers exact (closed mesh) or generalized (open mesh) </summary>
        public bool ExactWindingA, ExactWindingB;
        // for tests: use generalized winding numbers also for closed meshes
        internal bool ForceGeneralizedWinding = false;
        // for tests: no floating-point filters in the arrangement
        internal bool DisableFilters = false;
        /// <summary> time per phase, for diagnostics </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();
        /// <summary> sizes of the intermediate results, for diagnostics </summary>
        public Dictionary<string, long> Counts = new Dictionary<string, long>();


        public ExactMeshBoolean(DMesh3 a, DMesh3 b, Operations op)
        {
            MeshA = a; MeshB = b; Operation = op;
        }


        ExactMeshArrangement arr;

        public bool Compute()
        {
            Result = null;
            Error = null;
            if (MeshA == null || MeshB == null) {
                Error = "input mesh is null";
                return false;
            }
            foreach (DMesh3 m in new[] { MeshA, MeshB }) {
                foreach (int vid in m.VertexIndices()) {
                    Vector3d v = m.GetVertex(vid);
                    if (double.IsNaN(v.x + v.y + v.z) || double.IsInfinity(v.x + v.y + v.z)) {
                        Error = "input meshes have non-finite coordinates";
                        return false;
                    }
                }
            }
            ExactWindingA = IsWindingClosed(MeshA) && ForceGeneralizedWinding == false;
            ExactWindingB = IsWindingClosed(MeshB) && ForceGeneralizedWinding == false;
            if (AllowOpenMeshes == false && (ExactWindingA == false || ExactWindingB == false)) {
                Error = "input meshes must be closed (every edge used equally often in both directions)";
                return false;
            }

            arr = new ExactMeshArrangement(new List<DMesh3>() { MeshA, MeshB }) { SelfIntersections = true, DisableFilters = DisableFilters };
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
            classify(out keep, out flip);
            PhaseMilliseconds["classification"] = sw.ElapsedMilliseconds; sw.Restart();
            Result = assemble(keep, flip);
            PhaseMilliseconds["assembly"] = sw.ElapsedMilliseconds;
            return true;
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


        bool inside(double wa, double wb)
        {
            // winding numbers are integers for closed meshes, generalized ones are thresholded at 0.5
            bool a = wa > 0.5, b = wb > 0.5;
            switch (Operation) {
                case Operations.Union: return a || b;
                case Operations.Intersection: return a && b;
                default: return a && !b;
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
            for (int g0 = 0, g1 = 0; g0 < edgeKeys.Length; g0 = g1) {
                g1 = g0 + 1;
                while (g1 < edgeKeys.Length && edgeKeys[g1] == edgeKeys[g0]) g1++;
                if (arr.ConstraintEdges.Contains(edgeKeys[g0]))
                    continue;
                for (int mesh = 0; mesh < 2; ++mesh) {
                    int a = -1, b = -1, count = 0;
                    for (int g = g0; g < g1; ++g) {
                        int t = edgeTri[g];
                        if (output[t].Mesh != mesh) continue;
                        if (count == 0) a = t; else b = t;
                        count++;
                    }
                    if (count != 2 || same_stack(stack[a], stack[b]) == false)
                        continue;
                    int ra = find(a), rb = find(b);
                    if (ra != rb)
                        parent[ra] = rb;
                }
            }

            var patchWinding = new Dictionary<int, double[]>();
            var rng = new Random(Seed);
            var trees = new TriangleBoxTree[2];
            for (int i = 0; i < n; ++i) {
                if (representative[i] == false)
                    continue;
                var ot = output[i];
                int root = find(i);
                double[] w;
                if (patchWinding.TryGetValue(root, out w) == false) {
                    w = new double[2];
                    for (int m = 0; m < 2; ++m) {
                        if ((m == 0) ? ExactWindingA : ExactWindingB) {
                            if (trees[m] == null)
                                trees[m] = arr.Trees[m];
                            w[m] = front_winding(i, m, trees[m], rng, stack[i]);
                        } else {
                            w[m] = front_generalized_winding(i, m, stack[i]);
                        }
                    }
                    patchWinding[root] = w;
                    Patches++;
                }
                // back side: crossing the stack from the back to the front changes the winding number of a
                // mesh by -1 for each of its triangles oriented like the piece, +1 for opposite ones
                double[] back = { w[0], w[1] };
                if (stack[i] == null) {
                    back[ot.Mesh] += 1;
                } else {
                    var src = arr.Triangles[ot.Source];
                    foreach (int ti in stack[i]) {
                        var U = arr.Triangles[ti];
                        BigInteger dot = src.NX * U.NX + src.NY * U.NY + src.NZ * U.NZ;
                        back[U.Mesh] += (dot.Sign > 0) ? 1 : -1;
                    }
                }
                bool inFront = inside(w[0], w[1]), inBack = inside(back[0], back[1]);
                keep[i] = inFront != inBack;
                flip[i] = inFront;
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
        int front_winding(int i, int mesh, TriangleBoxTree tree, Random rng, List<int> stack)
        {
            var ot = arr.Output[i];
            var src = arr.Triangles[ot.Source];
            List<int> tris = arr.MeshTriangles[mesh];

            BigInteger[] min, max;
            mesh_bounds(mesh, out min, out max);
            int p = centroid(ot);
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

                int winding = 0;
                bool degenerate = false;
                tree.QuerySegment(pv, rv, pad, (k) => {
                    if (degenerate) return;
                    int ti = tris[k];
                    var U = arr.Triangles[ti];
                    if (U.Degenerate || ti == ot.Source || (stack != null && stack.Contains(ti))) return;
                    int sp = (U.P.A * P.X + U.P.B * P.Y + U.P.C * P.Z - U.P.D * P.W).Sign;
                    int sr = (U.P.A * Rp.X + U.P.B * Rp.Y + U.P.C * Rp.Z - U.P.D).Sign;
                    if (sr == 0) { degenerate = true; return; }
                    // sp == 0: the point is in the plane but not in the triangle (it would be in the stack)
                    if (sp == 0 || sp == sr) return;
                    int o0 = orient3d(P, Rp, arr.Exact(U.Corners[0]), arr.Exact(U.Corners[1]));
                    int o1 = orient3d(P, Rp, arr.Exact(U.Corners[1]), arr.Exact(U.Corners[2]));
                    int o2 = orient3d(P, Rp, arr.Exact(U.Corners[2]), arr.Exact(U.Corners[0]));
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

        readonly DMeshAABBTree3[] windingTrees = new DMeshAABBTree3[2];

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
            if (windingTrees[mesh] == null) {
                windingTrees[mesh] = new DMeshAABBTree3(m, true);
                windingTrees[mesh].FastWindingNumber(Vector3d.Zero);
            }
            Vector3d pv = arr.PointToVector(centroid(ot));
            double w = windingTrees[mesh].FastWindingNumber(pv);
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
            for (int i = 0; i < arr.Output.Count; ++i) {
                if (keep[i] == false)
                    continue;
                var ot = arr.Output[i];
                tris.Add(flip[i] ? new Index3i(ot.A, ot.C, ot.B) : new Index3i(ot.A, ot.B, ot.C));
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
                foreach (var pair in pair_around_edge(tris, list, u, v)) {
                    join(slot(pair.Key, u), slot(pair.Value, u));
                    join(slot(pair.Key, v), slot(pair.Value, v));
                }
                if (g1 - g0 > 2)
                    NonManifoldEdges++;
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

            DMesh3 result = new DMesh3();
            var vmap = new Dictionary<int, int>();
            for (int t = 0; t < tris.Count; ++t) {
                int[] vid = new int[3];
                for (int k = 0; k < 3; ++k) {
                    int root = find(3 * t + k);
                    if (vmap.TryGetValue(root, out vid[k]) == false) {
                        vid[k] = result.AppendVertex(coords[usedIndex[tris[t][k]]]);
                        vmap[root] = vid[k];
                    }
                }
                int tid = result.AppendTriangle(vid[0], vid[1], vid[2]);
                if (tid < 0) {
                    NonManifoldTriangles++;
                    int a2 = result.AppendVertex(result.GetVertex(vid[0]));
                    int b2 = result.AppendVertex(result.GetVertex(vid[1]));
                    int c2 = result.AppendVertex(result.GetVertex(vid[2]));
                    result.AppendTriangle(a2, b2, c2);
                }
            }
            return result;
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
            // angles of the triangles' half-planes around the axis u -> v
            Vector3d pu = arr.PointToVector(u), pv = arr.PointToVector(v);
            Vector3d axis = (pv - pu).Normalized;
            Vector3d refDir = Vector3d.Zero;
            var angle = new Dictionary<int, double>();
            foreach (int t in list) {
                Index3i f = tris[t];
                int w = f.a + f.b + f.c - u - v;
                Vector3d d = arr.PointToVector(w) - pu;
                d -= d.Dot(axis) * axis;
                if (refDir == Vector3d.Zero)
                    refDir = d.Normalized;
                Vector3d y = axis.Cross(refDir);
                angle[t] = Math.Atan2(d.Dot(y), d.Dot(refDir));
            }
            var sorted = new List<int>(list);
            sorted.Sort((a, b) => angle[a].CompareTo(angle[b]));
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

        static bool runs(Index3i t, int u, int v)
        {
            return (t.a == u && t.b == v) || (t.b == u && t.c == v) || (t.c == u && t.a == v);
        }
    }
}
