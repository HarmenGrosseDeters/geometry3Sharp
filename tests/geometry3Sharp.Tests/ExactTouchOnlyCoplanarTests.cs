using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The coplanar shortcuts of ExactMeshArrangement.touch_only (docs/design/CHEAPER_CONSTRUCTIONS.md, 4.1) against
    /// ExactTriangleIntersection.IntersectsBeyondShared, the code they replace for coplanar neighbours: on generated
    /// planar patches with folds lying flat, collinear corners, slivers and corners on the other triangle's edges,
    /// every pair of triangles sharing corners gives the same answer.
    /// </summary>
    public class ExactTouchOnlyCoplanarTests
    {
        // a planar patch: a grid triangulation of points o + i e1 + j e2 (exact doubles), plus triangles on duplicated
        // (unwelded, so the arrangement shares them by point id) vertices that fold over grid edges and fan around
        // grid vertices, each with a random third corner on the grid: same side (overlap), opposite side, collinear
        static DMesh3 Patch(Random r, Vector3d o, Vector3d e1, Vector3d e2, int n, int extra)
        {
            var m = new DMesh3();
            Func<int, int, Vector3d> P = (i, j) => o + i * e1 + j * e2;
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(P(i, j));
            Func<int, int, int> V = (i, j) => j * (n + 1) + i;
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    if (r.Next(2) == 0) {
                        m.AppendTriangle(V(i, j), V(i + 1, j), V(i + 1, j + 1));
                        m.AppendTriangle(V(i, j), V(i + 1, j + 1), V(i, j + 1));
                    } else {
                        m.AppendTriangle(V(i, j), V(i + 1, j), V(i, j + 1));
                        m.AppendTriangle(V(i + 1, j), V(i + 1, j + 1), V(i, j + 1));
                    }
                }
            for (int k = 0; k < extra; ++k) {
                int i0 = r.Next(n + 1), j0 = r.Next(n + 1);
                int i1 = Math.Max(0, Math.Min(n, i0 + r.Next(-1, 2))), j1 = Math.Max(0, Math.Min(n, j0 + r.Next(-1, 2)));
                int i2 = r.Next(n + 1), j2 = r.Next(n + 1);
                if ((i0 == i1 && j0 == j1) || (i0 == i2 && j0 == j2) || (i1 == i2 && j1 == j2))
                    continue;
                // (collinear third corners give degenerate triangles, which the arrangement skips: kept as input)
                int a = m.AppendVertex(P(i0, j0)), b = m.AppendVertex(P(i1, j1)), c = m.AppendVertex(P(i2, j2));
                if (r.Next(2) == 0) m.AppendTriangle(a, b, c); else m.AppendTriangle(b, a, c);
            }
            return m;
        }

        static void CheckAllPairs(DMesh3 mesh, ref int coplanarEdge, ref int coplanarVertex, ref int differ)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3> { mesh }) { SelfIntersections = true };
            arr.Compute();
            var byPoint = new Dictionary<int, List<int>>();
            for (int t = 0; t < arr.Triangles.Count; ++t) {
                var T = arr.Triangles[t];
                for (int k = 0; k < 3; ++k) {
                    List<int> l;
                    if (byPoint.TryGetValue(T.Corners[k], out l) == false)
                        byPoint[T.Corners[k]] = l = new List<int>();
                    if (l.Contains(t) == false) l.Add(t);
                }
            }
            var seen = new HashSet<long>();
            foreach (List<int> l in byPoint.Values)
                for (int x = 0; x < l.Count; ++x)
                    for (int y = x + 1; y < l.Count; ++y) {
                        int ti = Math.Min(l[x], l[y]), ui = Math.Max(l[x], l[y]);
                        if (seen.Add(((long)ti << 32) | (uint)ui) == false)
                            continue;
                        var T = arr.Triangles[ti]; var U = arr.Triangles[ui];
                        if (T.Degenerate || U.Degenerate)
                            continue;
                        int shared = 0;
                        for (int k = 0; k < 3; ++k)
                            if (U.Corners[k] == T.Corners[0] || U.Corners[k] == T.Corners[1] || U.Corners[k] == T.Corners[2])
                                shared++;
                        if (shared == 3)
                            continue;
                        if (shared == 2) coplanarEdge++; else coplanarVertex++;
                        var pa = new Index3i(T.Corners[0], T.Corners[1], T.Corners[2]);
                        var pb = new Index3i(U.Corners[0], U.Corners[1], U.Corners[2]);
                        var A = new Triangle3d(T.V0, T.V1, T.V2); var B = new Triangle3d(U.V0, U.V1, U.V2);
                        bool expected = ExactTriangleIntersection.IntersectsBeyondShared(pa, ref A, pb, ref B) == false;
                        if (arr.touch_only(T, U) != expected || arr.touch_only(U, T) != expected)
                            differ++;
                    }
        }

        [Fact]
        public void CoplanarPatches_SameAsIntersectsBeyondShared()
        {
            var r = new Random(7);
            int edge = 0, vertex = 0, differ = 0;
            // axis-aligned and tilted planes, float-like and large coordinates, slivers (long thin cells)
            var planes = new[] {
                new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) },
                new[] { new Vector3d(0.375, -2.5, 7), new Vector3d(0.25, 0, 0.125), new Vector3d(0, 0.5, 0.25) },
                new[] { new Vector3d(1024.5, 333.25, -77), new Vector3d(1.0 / 64, 1.0 / 128, 1.0 / 32), new Vector3d(-1.0 / 16, 1.0 / 64, 0) },
                new[] { new Vector3d(632117.25, 5712043.5, 113.75), new Vector3d(0.5, 0.25, 0), new Vector3d(0, 0, 0.75) },
                new[] { new Vector3d(0, 0, 0), new Vector3d(1, 1.0 / 1024, 0), new Vector3d(1.0 / 1024, 1, 1.0 / 2048) },
                new[] { new Vector3d(-3, 2, 1), new Vector3d(8, 0, 0), new Vector3d(1.0 / 256, 1.0 / 128, 0) },
            };
            foreach (var p in planes)
                for (int rep = 0; rep < 4; ++rep)
                    CheckAllPairs(Patch(r, p[0], p[1], p[2], 5, 40), ref edge, ref vertex, ref differ);
            Assert.Equal(0, differ);
            Assert.True(edge > 1000 && vertex > 1000, $"shared edge {edge}, shared vertex {vertex}");
        }

        [Fact]
        public void FoldOverlappingFlat_IsNotTouchOnly()
        {
            // two triangles on the edge (0,0)-(1,0) with both far corners above it: they overlap
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(0.5, 1, 0));
            m.AppendVertex(new Vector3d(0.25, 0.5, 0));
            m.AppendTriangle(0, 1, 2);
            int a = m.AppendVertex(new Vector3d(1, 0, 0)), b = m.AppendVertex(new Vector3d(0, 0, 0));
            m.AppendTriangle(a, b, 3);
            var arr = new ExactMeshArrangement(new List<DMesh3> { m }) { SelfIntersections = true };
            arr.Compute();
            Assert.False(arr.touch_only(arr.Triangles[0], arr.Triangles[1]));
            // and with the far corner below the edge they only touch
            var m2 = new DMesh3();
            m2.AppendVertex(new Vector3d(0, 0, 0)); m2.AppendVertex(new Vector3d(1, 0, 0)); m2.AppendVertex(new Vector3d(0.5, 1, 0));
            m2.AppendVertex(new Vector3d(0.25, -0.5, 0));
            m2.AppendTriangle(0, 1, 2); m2.AppendTriangle(1, 0, 3);
            var arr2 = new ExactMeshArrangement(new List<DMesh3> { m2 }) { SelfIntersections = true };
            arr2.Compute();
            Assert.True(arr2.touch_only(arr2.Triangles[0], arr2.Triangles[1]));
        }
    }
}
