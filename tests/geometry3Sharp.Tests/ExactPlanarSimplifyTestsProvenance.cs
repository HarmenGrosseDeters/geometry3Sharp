using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests for <see cref="ExactPlanarSimplify"/> group-key partitioning (<see cref="ExactPlanarSimplify.SetGroupKeys"/>)
    /// and per-source preservation (<see cref="ExactPlanarSimplify.EnablePreserveSources"/>).
    /// </summary>
    public class ExactPlanarSimplifyTestsProvenance
    {
        // A closed 2x1x1 box whose top face (z = 1) is split into left (x in [0,1], group 1) and right (x in [1,2], group 2)
        // with an overlapping flush coplanar box on top to produce removable coplanar Steiner points.
        static DMesh3 TwoGroupTopBox(int groupLeft, int groupRight)
        {
            var m = new DMesh3();
            m.EnableTriangleGroups(0);
            // 2x1x1 box made from two 1x1x1 half-boxes fused cleanly: 12 vertices
            int[,] b = new int[3, 2];
            int[,] t = new int[3, 2];
            for (int ix = 0; ix < 3; ++ix) {
                for (int iy = 0; iy < 2; ++iy) {
                    b[ix, iy] = m.AppendVertex(new Vector3d(ix, iy, 0));
                    t[ix, iy] = m.AppendVertex(new Vector3d(ix, iy, 1));
                }
            }
            void quad(int a, int bb, int c, int d, int g)
            {
                int t0 = m.AppendTriangle(a, bb, c);
                int t1 = m.AppendTriangle(a, c, d);
                m.SetTriangleGroup(t0, g);
                m.SetTriangleGroup(t1, g);
            }
            // top (z = 1, +Z normal): left [0,1] = groupLeft, right [1,2] = groupRight
            quad(t[0, 0], t[1, 0], t[1, 1], t[0, 1], groupLeft);
            quad(t[1, 0], t[2, 0], t[2, 1], t[1, 1], groupRight);
            // bottom (z = 0, -Z normal)
            quad(b[0, 0], b[0, 1], b[1, 1], b[1, 0], 3);
            quad(b[1, 0], b[1, 1], b[2, 1], b[2, 0], 3);
            // front (y = 0, -Y normal)
            quad(b[0, 0], b[1, 0], t[1, 0], t[0, 0], 4);
            quad(b[1, 0], b[2, 0], t[2, 0], t[1, 0], 4);
            // back (y = 1, +Y normal)
            quad(b[1, 1], b[0, 1], t[0, 1], t[1, 1], 5);
            quad(b[2, 1], b[1, 1], t[1, 1], t[2, 1], 5);
            // left (x = 0, -X normal) and right (x = 2, +X normal)
            quad(b[0, 1], b[0, 0], t[0, 0], t[0, 1], 6);
            quad(b[2, 0], b[2, 1], t[2, 1], t[2, 0], 7);
            return m;
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1, int groupId)
        {
            var g = new TrivialBox3Generator {
                Box = new Box3d(new Vector3d(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)),
                    new Vector3d(0.5 * (x1 - x0), 0.5 * (y1 - y0), 0.5 * (z1 - z0))),
                NoSharedVertices = false
            };
            DMesh3 m = g.Generate().MakeDMesh();
            m.EnableTriangleGroups(groupId);
            foreach (int tid in m.TriangleIndices()) m.SetTriangleGroup(tid, groupId);
            return m;
        }

        static Vector3d[] AllInputCoords(ExactMeshArrangement arr)
        {
            var ids = new List<int>(arr.PointCount);
            for (int p = 0; p < arr.PointCount; ++p)
                ids.Add(p);
            return arr.PointsToVectors(ids);
        }

        [Fact]
        public void SetGroupKeys_PreservesCoplanarGroupBoundary_WhileSimplifyingInteriorPoints()
        {
            DMesh3 a = TwoGroupTopBox(10, 20);
            // Overlapping flush box with top at z = 1 (crossing x = 1 seam and interior of both groups)
            DMesh3 b = Box(0.25, 0.25, 0.2, 1.75, 0.75, 1.0, 30);

            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { SelfIntersections = true, PreserveTriangleGroups = true };
            arr.Compute();
            Vector3d[] coords = AllInputCoords(arr);

            // Gather all top-face (z = 1) pieces of mesh A (which has interior Steiner points from B's edges)
            var tris = new List<Index3i>();
            var src = new List<int>();
            var flp = new List<bool>();
            for (int i = 0; i < arr.Output.Count; ++i) {
                var ot = arr.Output[i];
                if (ot.Mesh != 0) continue;
                var it = arr.Triangles[ot.Source];
                int g = a.GetTriangleGroup(it.Tid);
                if (g != 10 && g != 20) continue;
                tris.Add(new Index3i(ot.A, ot.B, ot.C));
                src.Add(ot.Source);
                flp.Add(false);
            }
            var inputPoints = new bool[arr.PointCount];
            for (int m = 0; m < arr.VertexPoints.Length; ++m)
                foreach (int vid in arr.Meshes[m].VertexIndices())
                    inputPoints[arr.Canonical(arr.VertexPoints[m][vid])] = true;

            var keys = new int[arr.Triangles.Count];
            for (int ti = 0; ti < arr.Triangles.Count; ++ti) {
                var it = arr.Triangles[ti];
                keys[ti] = arr.Meshes[it.Mesh].GetTriangleGroup(it.Tid);
            }

            var simp = new ExactPlanarSimplify(arr, tris, src, flp);
            simp.SetGroupKeys(keys);
            simp.Run(inputPoints);
            List<int> outGroups;
            List<Index3i> res = simp.Result(out outGroups);

            Assert.Equal(res.Count, outGroups.Count);
            Assert.True(simp.Removed > 0, "Expected interior Steiner points inside the coplanar groups to be simplified away.");

            // Exact area of group 10 and group 20 must each equal 1.0 (no triangle crossed x = 1 seam).
            double area10 = 0, area20 = 0;
            for (int i = 0; i < res.Count; ++i) {
                Vector3d p0 = coords[res[i].a], p1 = coords[res[i].b], p2 = coords[res[i].c];
                double area = 0.5 * (p1 - p0).Cross(p2 - p0).Length;
                if (outGroups[i] == 10) {
                    Assert.True(p0.x <= 1.0 + 1e-12 && p1.x <= 1.0 + 1e-12 && p2.x <= 1.0 + 1e-12);
                    area10 += area;
                } else if (outGroups[i] == 20) {
                    Assert.True(p0.x >= 1.0 - 1e-12 && p1.x >= 1.0 - 1e-12 && p2.x >= 1.0 - 1e-12);
                    area20 += area;
                } else {
                    Assert.Fail("Unexpected output group " + outGroups[i]);
                }
            }
            Assert.InRange(area10, 1.0 - 1e-12, 1.0 + 1e-12);
            Assert.InRange(area20, 1.0 - 1e-12, 1.0 + 1e-12);
        }

        [Fact]
        public void EnablePreserveSources_KeepsEveryOutputVertexInsideCitedInputTriangle()
        {
            DMesh3 a = TwoGroupTopBox(10, 20);
            // Two overlapping flush boxes whose edges cross strictly inside one input triangle of A's top face
            // (for example (0.5, 0.25, 1.0) strictly inside triangle ((0,0,1), (1,0,1), (1,1,1))), creating a true
            // interior Steiner point that EnablePreserveSources removes while never crossing input-triangle edges.
            DMesh3 b = Box(0.25, 0.25, 0.2, 1.75, 0.75, 1.0, 30);
            DMesh3 c = Box(0.50, 0.10, 0.2, 0.80, 0.90, 1.0, 40);

            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b, c }) { SelfIntersections = true };
            arr.Compute();
            Vector3d[] coords = AllInputCoords(arr);

            var tris = new List<Index3i>();
            var src = new List<int>();
            var flp = new List<bool>();
            for (int i = 0; i < arr.Output.Count; ++i) {
                var ot = arr.Output[i];
                if (ot.Mesh != 0) continue;
                var it = arr.Triangles[ot.Source];
                int g = a.GetTriangleGroup(it.Tid);
                if (g != 10 && g != 20) continue;
                tris.Add(new Index3i(ot.A, ot.B, ot.C));
                src.Add(ot.Source);
                flp.Add(false);
            }
            var inputPoints = new bool[arr.PointCount];
            for (int m = 0; m < arr.VertexPoints.Length; ++m)
                foreach (int vid in arr.Meshes[m].VertexIndices())
                    inputPoints[arr.Canonical(arr.VertexPoints[m][vid])] = true;

            var simp = new ExactPlanarSimplify(arr, tris, src, flp);
            simp.EnablePreserveSources();
            simp.Run(inputPoints);
            List<int> outSource;
            List<bool> outFlipped;
            List<Index3i> res = simp.Result(out outSource, out outFlipped);

            Assert.Equal(res.Count, outSource.Count);
            Assert.Equal(res.Count, outFlipped.Count);
            Assert.True(simp.Removed > 0, "Expected interior Steiner points inside individual source triangles to be removed.");

            for (int i = 0; i < res.Count; ++i) {
                Assert.False(outFlipped[i]);
                var it = arr.Triangles[outSource[i]];
                Index3i st = a.GetTriangle(it.Tid);
                Vector3d sa = a.GetVertex(st.a), sb = a.GetVertex(st.b), sc = a.GetVertex(st.c);
                for (int k = 0; k < 3; ++k) {
                    Vector3d p = coords[res[i][k]];
                    Vector3d w = ExactMeshAttributeTransfer.LocalBarycentric(p, sa, sb, sc, clamp: false);
                    Assert.True(w.x >= -1e-11 && w.y >= -1e-11 && w.z >= -1e-11,
                        $"Output vertex {p} lies outside cited source triangle {it.Tid}: bary={w}");
                    Assert.InRange(w.x + w.y + w.z, 1.0 - 1e-11, 1.0 + 1e-11);
                }
            }
        }
    }
}
