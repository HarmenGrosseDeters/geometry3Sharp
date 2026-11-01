using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.PreventSelfIntersections: every collapse is checked exactly against the nearby triangles,
    /// so the reduction cannot push the surface through itself.
    /// </summary>
    public class DefectFreeReduceTests
    {
        // a thin wavy slab, finely tessellated: aggressive reduction pushes the two sides through each other
        static DMesh3 ThinSlab()
        {
            DMesh3 slab = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(5, 5, 0.02)), EdgeVertices = 40 }.Generate().MakeDMesh();
            foreach (int vid in slab.VertexIndices().ToArray()) {
                Vector3d v = slab.GetVertex(vid);
                slab.SetVertex(vid, new Vector3d(v.x, v.y, v.z + 0.3 * Math.Sin(1.3 * v.x) * Math.Cos(1.1 * v.y)));
            }
            return slab;
        }

        [Fact]
        public void ThinSlab_PlainReducerIntersects_CheckedOneDoesNot()
        {
            DMesh3 input = ThinSlab();
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            DMesh3 plain = new DMesh3(input);
            new Reducer(plain).ReduceToTriangleCount(120);
            Assert.True(MeshRepairMetrics.SelfIntersections(plain) > 0, "test does not exercise the check");

            DMesh3 safe = new DMesh3(input);
            new Reducer(safe) { PreventSelfIntersections = true }.ReduceToTriangleCount(120);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(safe));
            Assert.True(safe.IsClosed());
            Assert.True(safe.TriangleCount < 0.2 * input.TriangleCount, $"{safe.TriangleCount} of {input.TriangleCount} triangles");
        }

        [Fact]
        public void CleanMesh_SameReductionAsWithoutTheCheck()
        {
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            new Reducer(a).ReduceToTriangleCount(500);
            new Reducer(b) { PreventSelfIntersections = true }.ReduceToTriangleCount(500);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(b));
        }

        [Fact]
        public void InputIntersections_NoNewOnes()
        {
            // two overlapping spheres (an input self-intersection), reduced: the overlap stays, no new ones
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(1.2, 0, 0));
            MeshEditor.Append(a, b);
            int before = MeshRepairMetrics.SelfIntersections(a);
            Assert.True(before > 0);
            new Reducer(a) { PreventSelfIntersections = true }.ReduceToTriangleCount(800);
            Assert.True(a.TriangleCount < 1000);
            // collapses near the overlap only happen if they create no new intersecting pair; the remaining
            // pairs are the (coarser) overlap curve
            Assert.True(MeshRepairMetrics.SelfIntersections(a) <= before);
        }

        // A flat ring (its collapses cut the corners of the hole) with flat islands in the hole, right below such
        // corners. All triangles lie in one axis plane, so their boxes are flat.
        static DMesh3 FlatRingWithIslands()
        {
            const int n = 24;
            var mesh = new DMesh3();
            for (int i = 0; i < n; ++i) {
                double angle = MathUtil.TwoPI * i / n;
                mesh.AppendVertex(new Vector3d(Math.Cos(angle), Math.Sin(angle), 0));
                mesh.AppendVertex(new Vector3d(1.5 * Math.Cos(angle), 1.5 * Math.Sin(angle), 0));
            }
            for (int i = 0; i < n; ++i) {
                int a = 2 * i, b = a + 1, c = (a + 2) % (2 * n), d = c + 1;
                mesh.AppendTriangle(a, b, d);
                mesh.AppendTriangle(a, d, c);
            }
            for (int i = 0; i < n; i += 2) {
                double angle = MathUtil.TwoPI * i / n, da = 0.02;
                int p = mesh.AppendVertex(new Vector3d(0.97 * Math.Cos(angle - da), 0.97 * Math.Sin(angle - da), 0));
                int q = mesh.AppendVertex(new Vector3d(0.97 * Math.Cos(angle + da), 0.97 * Math.Sin(angle + da), 0));
                int r = mesh.AppendVertex(new Vector3d(0.93 * Math.Cos(angle), 0.93 * Math.Sin(angle), 0));
                mesh.AppendTriangle(p, r, q);
            }
            return mesh;
        }

        // every pair, with the exact test the reducer uses: no box or tree that could share a flaw with the code under test
        static int IntersectingPairs(DMesh3 mesh)
        {
            int[] ids = mesh.TriangleIndices().ToArray();
            int found = 0;
            for (int i = 0; i < ids.Length; ++i) {
                Index3i ia = mesh.GetTriangle(ids[i]);
                Triangle3d A = new Triangle3d(mesh.GetVertex(ia.a), mesh.GetVertex(ia.b), mesh.GetVertex(ia.c));
                if (ExactTriangleIntersection.IsDegenerate(ref A)) continue;
                for (int j = i + 1; j < ids.Length; ++j) {
                    Index3i ib = mesh.GetTriangle(ids[j]);
                    Triangle3d B = new Triangle3d(mesh.GetVertex(ib.a), mesh.GetVertex(ib.b), mesh.GetVertex(ib.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                    if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B))
                        found++;
                }
            }
            return found;
        }

        [Fact]
        public void FlatBoxes_CoplanarCollapseOntoAnotherPart_IsRefused()
        {
            DMesh3 input = FlatRingWithIslands();
            Assert.Equal(0, IntersectingPairs(input));
            DMesh3 plain = new DMesh3(input);
            new Reducer(plain).ReduceToTriangleCount(40);
            Assert.True(IntersectingPairs(plain) > 0, "test does not exercise the check");

            DMesh3 safe = new DMesh3(input);
            new Reducer(safe) { PreventSelfIntersections = true }.ReduceToTriangleCount(40);
            Assert.Equal(0, IntersectingPairs(safe));
            Assert.True(safe.TriangleCount < input.TriangleCount, "nothing collapsed");
            Assert.True(safe.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(safe));
        }

        [Fact]
        public void FlatBoxes_NoIntersection_InEveryFrame()
        {
            DMesh3 source = FlatRingWithIslands();
            DMesh3 reference = new DMesh3(source);
            new Reducer(reference) { PreventSelfIntersections = true }.ReduceToTriangleCount(40);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(source, s);
                new Reducer(mesh) { PreventSelfIntersections = true }.ReduceToTriangleCount(40);
                Assert.True(mesh.TriangleCount < source.TriangleCount, s.Name);
                // (in the rotated frames the triangles are only nearly coplanar; the check stays exact)
                Assert.True(IntersectingPairs(mesh) == 0, s.Name);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(mesh, s)));
            }
        }

        [Fact]
        public void FlatBoxes_ReducerUsedTwice_StillNoIntersection()
        {
            DMesh3 mesh = FlatRingWithIslands();
            var reducer = new Reducer(mesh) { PreventSelfIntersections = true };
            reducer.ReduceToTriangleCount(70);
            Assert.Equal(0, IntersectingPairs(mesh));
            reducer.ReduceToTriangleCount(40);
            Assert.Equal(0, IntersectingPairs(mesh));
        }

        class CollapseProbe : Reducer
        {
            public CollapseProbe(DMesh3 mesh) : base(mesh)
            {
                PreventSelfIntersections = true;
                ensure_triangle_grid();
            }
            public bool Creates(int a, int b, int keep, Vector3d newPos) => collapse_creates_intersection(a, b, keep, newPos);
        }

        // a fan around vertex 0 with the ring 1, 2, 3, 4, and a separate triangle
        static DMesh3 FanAndTriangle(double ringZ, Vector3d p, Vector3d q, Vector3d r)
        {
            var mesh = new DMesh3();
            mesh.AppendVertex(new Vector3d(0, 0, ringZ));
            mesh.AppendVertex(new Vector3d(1, 0, ringZ));
            mesh.AppendVertex(new Vector3d(0, 1, ringZ));
            mesh.AppendVertex(new Vector3d(-1, 0, ringZ));
            mesh.AppendVertex(new Vector3d(0, -1, ringZ));
            for (int i = 0; i < 4; ++i)
                mesh.AppendTriangle(0, 1 + i, 1 + (i + 1) % 4);
            mesh.AppendTriangle(mesh.AppendVertex(p), mesh.AppendVertex(q), mesh.AppendVertex(r));
            return mesh;
        }

        [Fact]
        public void CollapseCheck_FlatBoxes_CoplanarOverlap()
        {
            // the moved vertex drags the new triangles over the separate triangle, all in the plane z = 0
            Vector3d p = new Vector3d(2, 0.1, 0), q = new Vector3d(2.1, 0.1, 0), r = new Vector3d(2, 0.2, 0);
            Vector3d newPos = new Vector3d(3, 0, 0);
            Assert.True(new CollapseProbe(FanAndTriangle(0, p, q, r)).Creates(0, 1, 1, newPos));
            // control: in the plane, but beside the new triangles
            Vector3d away = new Vector3d(0, 3, 0);
            Assert.False(new CollapseProbe(FanAndTriangle(0, p + away, q + away, r + away)).Creates(0, 1, 1, newPos));
            // control: above the plane
            Vector3d up = new Vector3d(0, 0, 0.5);
            Assert.False(new CollapseProbe(FanAndTriangle(0, p + up, q + up, r + up)).Creates(0, 1, 1, newPos));
        }

        [Fact]
        public void CollapseCheck_BoxesThatOnlyTouch()
        {
            // the moved vertex lands on a corner of the separate triangle: boxes with z in [0, 1] and [-1, 0]
            Vector3d newPos = new Vector3d(0.2, 0, 0);
            DMesh3 touching = FanAndTriangle(1, newPos, new Vector3d(0.2, 1, -1), new Vector3d(1.2, 0, -1));
            Assert.True(new CollapseProbe(touching).Creates(0, 1, 1, newPos));
            // control: just above that corner
            Assert.False(new CollapseProbe(touching).Creates(0, 1, 1, newPos + new Vector3d(0, 0, 0.01)));
        }

        // ---- what the closed test costs: regions that already touch or overlap do not collapse ----

        static DMesh3 FlatPlate(int n)
        {
            var mesh = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = i + j * (n + 1), b = a + 1, c = a + n + 2, d = a + n + 1;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        [Fact]
        public void TriangleWithAnInfiniteVertex_DoesNotThrow()
        {
            // a separate triangle (inf, 0, 0), (0, 0, 0), (0, 1, 0): its box is flat in z and not finite, so the closed
            // grid query passes it to every check near z = 0 (the open box test used to skip it)
            DMesh3 mesh = FlatPlate(10);
            int a = mesh.AppendVertex(new Vector3d(double.PositiveInfinity, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(0, 0, 0)), c = mesh.AppendVertex(new Vector3d(0, 1, 0));
            mesh.AppendTriangle(a, b, c);
            // (they are not checked: the exact tests need finite input)
            new Reducer(mesh) { PreventSelfIntersections = true }.ReduceToTriangleCount(20);
            Assert.True(mesh.TriangleCount < 100, $"{mesh.TriangleCount} triangles");
        }

        [Fact]
        public void CoincidentSheets_InAnAxisPlane_Freeze()
        {
            // two copies of a plate: every new triangle overlaps the other copy, so no collapse is accepted. The
            // documented limit in docs/UPSTREAM_CHANGES.md (with the open test: 640)
            DMesh3 mesh = FlatPlate(40);
            new MeshEditor(mesh).AppendMesh(FlatPlate(40));
            Assert.Equal(6400, mesh.TriangleCount);
            new Reducer(mesh) { PreventSelfIntersections = true }.ReduceToTriangleCount(640);
            Assert.Equal(6400, mesh.TriangleCount);
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void BoxesTouchingAtAFace_KeepMoreTriangles()
        {
            // the region of the shared face stays. The documented limit in docs/UPSTREAM_CHANGES.md (with the open test: 194)
            DMesh3 a = new GridBox3Generator() { EdgeVertices = 10, Box = new Box3d(Vector3d.Zero, new Vector3d(10, 10, 10)) }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Translate(b, new Vector3d(20, 0, 0));
            new MeshEditor(a).AppendMesh(b);
            Assert.Equal(1944, a.TriangleCount);
            new Reducer(a) { PreventSelfIntersections = true }.ReduceToTriangleCount(194);
            Assert.Equal(540, a.TriangleCount);
            Assert.True(a.CheckValidity(false, FailMode.ReturnOnly));
        }
    }
}
