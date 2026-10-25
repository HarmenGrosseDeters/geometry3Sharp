// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Numerics;

namespace g3
{
    /// <summary>
    /// Boolean operations (union, intersection, difference) of two closed triangle meshes, with exact
    /// arithmetic: the intersection curves, the retriangulation and the inside/outside decisions are computed
    /// exactly (ExactMeshArrangement), so touching, coplanar and nearly degenerate configurations give a
    /// closed result, at any scale. Only the output vertex coordinates are rounded to double.
    ///
    /// Pieces of each mesh between intersection curves (patches) are inside or outside the other mesh,
    /// decided by the parity of an exact ray test from a point inside the patch. Pieces where both meshes
    /// overlap in the same plane are kept or removed by their orientations (Zhou et al., "Mesh Arrangements
    /// for Solid Geometry", 2016).
    ///
    /// Requirements: both meshes closed, consistently oriented (outward normals) and free of
    /// self-intersections. Degenerate (zero-area) input triangles are dropped.
    /// The rounding of new vertices can, rarely, create tiny self-intersections in the result.
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
        /// <summary> pairs of triangles (one from each mesh) that intersect </summary>
        public int IntersectingPairs;
        /// <summary> triangles whose vertices had to be duplicated in the result (non-manifold edges or vertices) </summary>
        public int NonManifoldTriangles;
        /// <summary> number of patches classified by a ray test </summary>
        public int Patches;
        /// <summary> time per phase, for diagnostics </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();


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
            if (MeshA.IsClosed() == false || MeshB.IsClosed() == false) {
                Error = "input meshes must be closed";
                return false;
            }
            foreach (DMesh3 m in new[] { MeshA, MeshB })
                foreach (int vid in m.VertexIndices()) {
                    Vector3d v = m.GetVertex(vid);
                    if (double.IsNaN(v.x + v.y + v.z) || double.IsInfinity(v.x + v.y + v.z)) {
                        Error = "input meshes have non-finite coordinates";
                        return false;
                    }
                }

            arr = new ExactMeshArrangement(new List<DMesh3>() { MeshA, MeshB });
            arr.Compute();
            IntersectingPairs = arr.IntersectingPairs;
            PhaseMilliseconds = new Dictionary<string, long>(arr.PhaseMilliseconds);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool[] keep, flip;
            classify(out keep, out flip);
            PhaseMilliseconds["classification"] = sw.ElapsedMilliseconds; sw.Restart();
            Result = assemble(keep, flip);
            PhaseMilliseconds["assembly"] = sw.ElapsedMilliseconds;
            return true;
        }



        void classify(out bool[] keep, out bool[] flip)
        {
            List<ExactMeshArrangement.OutputTriangle> output = arr.Output;
            int n = output.Count;
            keep = new bool[n];
            flip = new bool[n];

            // triangles on the surface of the other mesh (coplanar overlaps): +1 same orientation, -1 opposite
            int[] on = new int[n];
            for (int i = 0; i < n; ++i) {
                var ot = output[i];
                var src = arr.Triangles[ot.Source];
                if (src.Coplanar.Count == 0)
                    continue;
                int c = centroid(ot);
                foreach (int ui in src.Coplanar) {
                    var U = arr.Triangles[ui];
                    if (inside_coplanar(src, U, c)) {
                        BigInteger dot = src.NX * U.NX + src.NY * U.NY + src.NZ * U.NZ;
                        on[i] = (dot.Sign > 0) ? 1 : -1;
                        break;
                    }
                }
            }

            // patches: triangles of one mesh connected across edges that are not on an intersection curve
            var edgeTris = new Dictionary<long, List<int>>(EdgeKeyComparer.Instance);
            for (int i = 0; i < n; ++i) {
                var ot = output[i];
                foreach (long key in new[] { ExactMeshArrangement.EdgeKey(ot.A, ot.B), ExactMeshArrangement.EdgeKey(ot.B, ot.C), ExactMeshArrangement.EdgeKey(ot.C, ot.A) }) {
                    List<int> list;
                    if (edgeTris.TryGetValue(key, out list) == false) {
                        list = new List<int>();
                        edgeTris[key] = list;
                    }
                    list.Add(i);
                }
            }
            int[] parent = new int[n];
            for (int i = 0; i < n; ++i)
                parent[i] = i;
            Func<int, int> find = null;
            find = (x) => {
                while (parent[x] != x) {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            };
            foreach (var kv in edgeTris) {
                if (arr.ConstraintEdges.Contains(kv.Key))
                    continue;
                List<int> list = kv.Value;
                for (int k = 1; k < list.Count; ++k) {
                    int a = list[0], b = list[k];
                    if (output[a].Mesh != output[b].Mesh || on[a] != 0 || on[b] != 0)
                        continue;
                    int ra = find(a), rb = find(b);
                    if (ra != rb)
                        parent[ra] = rb;
                }
            }

            var patchInside = new Dictionary<int, bool>();
            var rng = new Random(Seed);
            var trees = new TriangleBoxTree[2];
            for (int i = 0; i < n; ++i) {
                var ot = output[i];
                bool isA = ot.Mesh == 0;
                if (on[i] != 0) {
                    bool same = on[i] > 0;
                    switch (Operation) {
                        case Operations.Union:
                        case Operations.Intersection:
                            keep[i] = isA && same;
                            break;
                        case Operations.Difference:
                            keep[i] = isA && !same;
                            break;
                    }
                    continue;
                }
                int root = find(i);
                bool inside;
                if (patchInside.TryGetValue(root, out inside) == false) {
                    int other = 1 - ot.Mesh;
                    if (trees[other] == null)
                        trees[other] = build_tree(other);
                    inside = inside_mesh(centroid(ot), other, trees[other], rng);
                    patchInside[root] = inside;
                    Patches++;
                }
                switch (Operation) {
                    case Operations.Union:
                        keep[i] = !inside;
                        break;
                    case Operations.Intersection:
                        keep[i] = inside;
                        break;
                    case Operations.Difference:
                        keep[i] = isA ? !inside : inside;
                        flip[i] = !isA;
                        break;
                }
            }
        }


        int centroid(ExactMeshArrangement.OutputTriangle ot)
        {
            ExactPoint3 a = arr.Points[ot.A], b = arr.Points[ot.B], c = arr.Points[ot.C];
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


        TriangleBoxTree build_tree(int mesh)
        {
            var boxes = new List<AxisAlignedBox3d>();
            DMesh3 m = arr.Meshes[mesh];
            foreach (int ti in arr.MeshTriangles[mesh]) {
                Index3i t = m.GetTriangle(arr.Triangles[ti].Tid);
                AxisAlignedBox3d box = new AxisAlignedBox3d(m.GetVertex(t.a));
                box.Contain(m.GetVertex(t.b));
                box.Contain(m.GetVertex(t.c));
                boxes.Add(box);
            }
            return new TriangleBoxTree(boxes);
        }


        // parity of the crossings of a segment from p to a far point with the closed mesh; a new random
        // direction is used if the segment touches an edge or vertex
        bool inside_mesh(int p, int mesh, TriangleBoxTree tree, Random rng)
        {
            ExactPoint3 P = arr.Points[p];
            List<int> tris = arr.MeshTriangles[mesh];

            BigInteger[] min, max;
            mesh_bounds(mesh, out min, out max);
            BigInteger ext = BigInteger.One;
            for (int k = 0; k < 3; ++k)
                ext = BigInteger.Max(ext, max[k] - min[k] + 1);
            // the point is inside the box of the mesh, or the answer is outside
            for (int k = 0; k < 3; ++k)
                if (P[k] < min[k] * P.W || P[k] > max[k] * P.W)
                    return false;

            Vector3d pv = P.ToVector3d(arr.ScaleExp);
            for (int attempt = 0; attempt < 100; ++attempt) {
                int axis = rng.Next(3);
                long[] dir = new long[3];
                for (int k = 0; k < 3; ++k)
                    dir[k] = rng.Next(-1000, 1001);
                dir[axis] = (rng.Next(2) == 0) ? -1000 : 1000;
                BigInteger[] R = new BigInteger[3];
                for (int k = 0; k < 3; ++k)
                    R[k] = (min[k] + max[k]) / 2 + dir[k] * ext * 4;
                ExactPoint3 Rp = ExactPoint3.FromInteger(R[0], R[1], R[2]);
                Vector3d rv = Rp.ToVector3d(arr.ScaleExp);
                // pad: the rounding of the point to double (relative 1e-16), with a wide margin
                double pad = 1e-9 * (Math.Abs(pv.x) + Math.Abs(pv.y) + Math.Abs(pv.z)) + 1e-300;

                int crossings = 0;
                bool degenerate = false;
                tree.QuerySegment(pv, rv, pad, (k) => {
                    if (degenerate) return;
                    var U = arr.Triangles[tris[k]];
                    if (U.Degenerate) return;
                    int sp = (U.P.A * P.X + U.P.B * P.Y + U.P.C * P.Z - U.P.D * P.W).Sign;
                    int sr = (U.P.A * Rp.X + U.P.B * Rp.Y + U.P.C * Rp.Z - U.P.D).Sign;
                    if (sr == 0) { degenerate = true; return; }
                    if (sp == 0 || sp == sr) return;
                    int o0 = orient3d(P, Rp, arr.Points[U.Corners[0]], arr.Points[U.Corners[1]]);
                    int o1 = orient3d(P, Rp, arr.Points[U.Corners[1]], arr.Points[U.Corners[2]]);
                    int o2 = orient3d(P, Rp, arr.Points[U.Corners[2]], arr.Points[U.Corners[0]]);
                    if (o0 == 0 || o1 == 0 || o2 == 0) {
                        // the line passes through an edge or vertex: only a problem if it is the triangle's
                        if ((o0 >= 0 && o1 >= 0 && o2 >= 0) || (o0 <= 0 && o1 <= 0 && o2 <= 0))
                            degenerate = true;
                        return;
                    }
                    if (o0 == o1 && o1 == o2)
                        crossings++;
                });
                if (degenerate == false)
                    return (crossings % 2) == 1;
            }
            throw new InvalidOperationException("ExactMeshBoolean: no non-degenerate ray found");
        }

        readonly Dictionary<int, BigInteger[][]> bounds = new Dictionary<int, BigInteger[][]>();

        void mesh_bounds(int mesh, out BigInteger[] min, out BigInteger[] max)
        {
            BigInteger[][] b;
            if (bounds.TryGetValue(mesh, out b) == false) {
                b = new[] { new BigInteger[3], new BigInteger[3] };
                bool first = true;
                foreach (int vid in arr.Meshes[mesh].VertexIndices()) {
                    ExactPoint3 v = arr.Points[arr.VertexPoints[mesh][vid]];
                    for (int k = 0; k < 3; ++k) {
                        if (first || v[k] < b[0][k]) b[0][k] = v[k];
                        if (first || v[k] > b[1][k]) b[1][k] = v[k];
                    }
                    first = false;
                }
                bounds[mesh] = b;
            }
            min = b[0]; max = b[1];
        }

        // sign of the 4x4 determinant of homogeneous points (consistent orientation, used for parity only)
        static int orient3d(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
        {
            BigInteger[,] m = new BigInteger[4, 4];
            ExactPoint3[] pts = { a, b, c, d };
            for (int k = 0; k < 4; ++k) {
                m[k, 0] = pts[k].X; m[k, 1] = pts[k].Y; m[k, 2] = pts[k].Z; m[k, 3] = pts[k].W;
            }
            return ExactMeshArrangement.Det4(m).Sign;
        }


        DMesh3 assemble(bool[] keep, bool[] flip)
        {
            DMesh3 result = new DMesh3();
            var vmap = new Dictionary<int, int>();
            Func<int, int> vertex = (p) => {
                int vid;
                if (vmap.TryGetValue(p, out vid) == false) {
                    vid = result.AppendVertex(arr.PointToVector(p));
                    vmap[p] = vid;
                }
                return vid;
            };
            for (int i = 0; i < arr.Output.Count; ++i) {
                if (keep[i] == false)
                    continue;
                var ot = arr.Output[i];
                int a = vertex(ot.A), b = vertex(ot.B), c = vertex(ot.C);
                if (flip[i]) {
                    int tmp = b; b = c; c = tmp;
                }
                int tid = result.AppendTriangle(a, b, c);
                if (tid < 0) {
                    NonManifoldTriangles++;
                    int a2 = result.AppendVertex(result.GetVertex(a));
                    int b2 = result.AppendVertex(result.GetVertex(b));
                    int c2 = result.AppendVertex(result.GetVertex(c));
                    result.AppendTriangle(a2, b2, c2);
                }
            }
            return result;
        }
    }
}
