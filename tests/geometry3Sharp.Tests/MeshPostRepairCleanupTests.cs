using System.Collections.Generic;
using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MeshPostRepairCleanupTests
    {
        static DMesh3 Sphere(double radius = 1, Vector3d center = default(Vector3d))
        {
            var gen = new Sphere3Generator_NormalizedCube() { EdgeVertices = 12, Radius = radius };
            DMesh3 mesh = gen.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }

        static int DegenerateCount(DMesh3 mesh)
        {
            double diag = mesh.CachedBounds.DiagonalLength;
            return mesh.TriangleIndices().Count(t => mesh.GetTriArea(t) < 1e-12 * diag * diag);
        }

        static int ShortEdgeCount(DMesh3 mesh, double len)
        {
            return mesh.EdgeIndices().Count(e => {
                Index2i ev = mesh.GetEdgeV(e);
                return mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)) < len;
            });
        }

        [Fact]
        public void CleanMesh_IsUnchanged()
        {
            DMesh3 mesh = Sphere();
            int nV = mesh.VertexCount, nT = mesh.TriangleCount;
            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(nV, mesh.VertexCount);
            Assert.Equal(nT, mesh.TriangleCount);
            Assert.Equal(0, cleanup.RemovedDuplicates + cleanup.FixedDegenerate + cleanup.FixedFolds
                + cleanup.DisconnectedBowties + cleanup.FlippedComponents);
        }

        [Fact]
        public void DuplicateTrianglePair_IsRemoved()
        {
            DMesh3 mesh = Sphere();
            int nT = mesh.TriangleCount;
            int a = mesh.AppendVertex(new Vector3d(5, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(6, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(5, 1, 0));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, b);

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(2, cleanup.RemovedDuplicates);
            Assert.Equal(nT, mesh.TriangleCount);
            Assert.True(mesh.IsClosed());
        }

        [Fact]
        public void CapTriangle_IsFlippedAway()
        {
            DMesh3 mesh = Sphere();
            // poke a triangle with a point almost on one of its edges -> cap triangle with ~zero area
            DMesh3.PokeTriangleInfo info;
            Assert.Equal(MeshResult.Ok, mesh.PokeTriangle(mesh.TriangleIndices().First(), new Vector3d(0.5, 0.5 - 1e-9, 1e-9), out info));
            Assert.True(DegenerateCount(mesh) > 0);

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(0, DegenerateCount(mesh));
            Assert.True(mesh.IsClosed());
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void NeedleTriangles_AreCollapsed()
        {
            DMesh3 mesh = Sphere();
            // split an edge very close to one end -> very short edge with two needle triangles
            int eid = mesh.EdgeIndices().First();
            Index2i ev = mesh.GetEdgeV(eid);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, mesh.SplitEdge(eid, out split));
            mesh.SetVertex(split.vNew, Vector3d.Lerp(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b), 1e-8));
            Assert.Equal(1, ShortEdgeCount(mesh, 1e-6));

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(0, ShortEdgeCount(mesh, 1e-6));
            Assert.True(mesh.IsClosed());
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void InsideOutComponent_IsFlipped_ButCavityIsKept()
        {
            DMesh3 mesh = Sphere(1, Vector3d.Zero);
            // separate inside-out sphere -> should be flipped
            DMesh3 inverted = Sphere(1, new Vector3d(5, 0, 0));
            inverted.ReverseOrientation();
            MeshEditor.Append(mesh, inverted);
            // inside-out sphere inside the first one = cavity -> must stay inside-out
            DMesh3 cavity = Sphere(0.5, Vector3d.Zero);
            cavity.ReverseOrientation();
            MeshEditor.Append(mesh, cavity);
            Assert.Single(MeshPostRepairCleanup.FindInvertedComponents(mesh));

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();

            Assert.Equal(1, cleanup.FlippedComponents);
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            var vols = comps.Components.Select(c => MeshMeasurements.VolumeArea(mesh, c.Indices, mesh.GetVertex).x).OrderBy(v => v).ToList();
            Assert.True(vols[0] < 0);     // cavity
            Assert.True(vols[1] > 0 && vols[2] > 0);
        }

        /// <summary>
        /// Closed outward box [min, max] whose face at min[axis] is an n x n grid (2 n^2 triangles) and whose
        /// other faces are coarse fans, so most triangles lie on that face.
        /// </summary>
        static DMesh3 BoxWithDenseFace(Vector3d min, Vector3d max, int axis, int n)
        {
            // local (u, v, w) = cyclic permutation of (x, y, z) starting at axis, the dense face is u = 0
            Func<double, double, double, Vector3d> world = (u, v, w) => {
                double[] l = { u, v, w }, x = new double[3];
                for (int k = 0; k < 3; ++k) {
                    int a = (axis + k) % 3;
                    x[a] = min[a] + l[k] * (max[a] - min[a]);
                }
                return new Vector3d(x[0], x[1], x[2]);
            };
            Func<int, Vector3d> axisDir = k => { var d = Vector3d.Zero; d[(axis + k) % 3] = 1; return d; };
            var mesh = new DMesh3();
            var g = new int[n + 1, n + 1];
            for (int a = 0; a <= n; ++a)
                for (int b = 0; b <= n; ++b)
                    g[a, b] = mesh.AppendVertex(world(0, (double)a / n, (double)b / n));
            var f = new int[2, 2];
            for (int a = 0; a < 2; ++a)
                for (int b = 0; b < 2; ++b)
                    f[a, b] = mesh.AppendVertex(world(1, a, b));
            // orient every triangle by the outward normal of its face
            Action<int, int, int, Vector3d> tri = (i0, i1, i2, outward) => {
                Vector3d nrm = (mesh.GetVertex(i1) - mesh.GetVertex(i0)).Cross(mesh.GetVertex(i2) - mesh.GetVertex(i0));
                if (nrm.Dot(outward) > 0) mesh.AppendTriangle(i0, i1, i2);
                else mesh.AppendTriangle(i0, i2, i1);
            };
            for (int a = 0; a < n; ++a)
                for (int b = 0; b < n; ++b) {
                    tri(g[a, b], g[a + 1, b], g[a + 1, b + 1], -axisDir(0));
                    tri(g[a, b], g[a + 1, b + 1], g[a, b + 1], -axisDir(0));
                }
            tri(f[0, 0], f[1, 0], f[1, 1], axisDir(0));
            tri(f[0, 0], f[1, 1], f[0, 1], axisDir(0));
            // side faces: fan from the far corner next to the first boundary vertex
            Action<Func<int, int>, int, int, Vector3d> side = (bd, farFirst, farLast, outward) => {
                for (int k = 0; k < n; ++k)
                    tri(bd(k), bd(k + 1), farFirst, outward);
                tri(bd(n), farLast, farFirst, outward);
            };
            side(k => g[0, k], f[0, 0], f[0, 1], -axisDir(1));
            side(k => g[n, k], f[1, 0], f[1, 1], axisDir(1));
            side(k => g[k, 0], f[0, 0], f[1, 0], -axisDir(2));
            side(k => g[k, n], f[0, 1], f[1, 1], axisDir(2));
            return mesh;
        }

        [Fact]
        public void InvertedComponents_TouchingBoxesAndCavity_DoNotDependOnUnitOrPosition()
        {
            // A = [0,2]x[0,1]^2 outwards; B = inverted box touching A at x = 2; C = cavity in A (inwards =
            // correct) touching A's wall y = 0. B and C are thin slabs whose contact face is densely
            // triangulated (A's coarsely), so most of their triangles, and the best-shaped ones, lie on A's
            // surface, where the winding number of A is 0 or 1 depending on rounding. Only B is inverted.
            DMesh3 a = BoxWithDenseFace(new Vector3d(0, 0, 0), new Vector3d(2, 1, 1), 0, 1);
            DMesh3 b = BoxWithDenseFace(new Vector3d(2, 0, 0), new Vector3d(2.02, 1, 1), 0, 12);
            b.ReverseOrientation();
            DMesh3 c = BoxWithDenseFace(new Vector3d(0.5, 0, 0.25), new Vector3d(1.5, 0.02, 0.75), 1, 12);
            c.ReverseOrientation();
            foreach (DMesh3 m in new[] { a, b, c })
                Assert.True(m.IsClosed());
            Assert.True(Volume(a) > 0 && Volume(b) < 0 && Volume(c) < 0);
            DMesh3 mesh = new DMesh3(a);
            MeshEditor.Append(mesh, b);
            MeshEditor.Append(mesh, c);
            // generic rotation: the contact faces are coplanar only up to rounding, as in real meshes
            MeshTransforms.Rotate(mesh, Vector3d.Zero, new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37));
            int bTris = b.TriangleCount;

            foreach (var (scale, offset) in new[] { (1.0, 0.0), (0.001, 0.0), (0.1, 0.0), (1.0, 5e6) }) {
                DMesh3 m = new DMesh3(mesh);
                MeshTransforms.Scale(m, scale);
                MeshTransforms.Translate(m, new Vector3d(offset, offset, offset));
                var inverted = MeshPostRepairCleanup.FindInvertedComponents(m);
                Assert.True(inverted.Count == 1 && inverted[0].Length == bTris,
                    $"scale {scale}, offset {offset}: {inverted.Count} inverted components");
            }
        }

        [Fact]
        public void InvertedComponents_ThinCavityTouchingWall_IsNotInverted()
        {
            // thin cavity plate (thickness 1e-4 of its size, correctly oriented inwards) touching the outer
            // wall y = 0 of [0,2]^3: sample points must not be pushed through the plate to outside the box
            foreach (double t in new[] { 1e-2, 1e-4, 2e-5 }) {
                DMesh3 mesh = new GridBox3Generator() {
                    Box = new Box3d(new Vector3d(1, 1, 1), new Vector3d(1, 1, 1)), EdgeVertices = 3 }.Generate().MakeDMesh();
                DMesh3 plate = new GridBox3Generator() {
                    Box = new Box3d(new Vector3d(1, t / 2, 1), new Vector3d(0.5, t / 2, 0.5)), EdgeVertices = 8 }.Generate().MakeDMesh();
                plate.ReverseOrientation();
                MeshEditor.Append(mesh, plate);
                Assert.True(mesh.IsClosed());
                MeshTransforms.Rotate(mesh, Vector3d.Zero, new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37));
                Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
            }
        }

        [Fact]
        public void AutoRepair_WithPostRepairCleanup_GivesCleanClosedMesh()
        {
            // sphere with a hole, a separate two-sided triangle pair and a double cone with a bowtie apex
            DMesh3 mesh = Sphere();
            mesh.RemoveTriangle(mesh.TriangleIndices().First(), false, false);
            int a = mesh.AppendVertex(new Vector3d(5, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(6, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(5, 1, 0));
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, b);
            DMesh3 cone = MeshTestUtil.DoubleConeBowtie(8, 1);
            MeshTransforms.Translate(cone, new Vector3d(-5, 0, 0));
            MeshEditor.Append(mesh, cone);
            Assert.False(mesh.IsClosed());

            var repair = new MeshAutoRepair(mesh) { PostRepairCleanup = true };
            Assert.True(repair.Apply());
            DMesh3 result = repair.Mesh;

            Assert.True(result.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(result));
            Assert.Equal(0, DegenerateCount(result));
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(result));
            Assert.True(Volume(result) > 0);
            Assert.True(result.CheckValidity(false, FailMode.ReturnOnly));
        }

        static DMesh3 Cube(Vector3d min, double size)
        {
            return new TrivialBox3Generator() { Box = new Box3d(min + 0.5 * size * Vector3d.One, 0.5 * size * Vector3d.One) }.Generate().MakeDMesh();
        }

        [Fact]
        public void ManyComponents_InsideOutOneFound()
        {
            // 600 hollow cubes (each with a cavity, oriented inwards) in a grid, plus one inside-out cube and one hollow
            // cube with an outward cavity: with more than 500 closed components nothing was checked, so NotCleanReason
            // called such a mesh clean
            DMesh3 mesh = new DMesh3();
            for (int i = 0; i < 600; ++i) {
                Vector3d p = new Vector3d(3 * (i % 30), 3 * (i / 30), 0);
                MeshEditor.Append(mesh, Cube(p, 2));
                DMesh3 cavity = Cube(p + 0.5 * Vector3d.One, 1);
                cavity.ReverseOrientation();
                MeshEditor.Append(mesh, cavity);
            }
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh, false));

            DMesh3 inverted = Cube(new Vector3d(0, -5, 0), 2);
            inverted.ReverseOrientation();
            MeshEditor.Append(mesh, inverted);
            MeshEditor.Append(mesh, Cube(new Vector3d(5, -5, 0), 2));
            MeshEditor.Append(mesh, Cube(new Vector3d(5.5, -4.5, 0.5), 1));      // a cavity facing outwards
            var found = MeshPostRepairCleanup.FindInvertedComponents(mesh);
            Assert.Equal(2, found.Count);
            Assert.Equal("inside-out components", MeshRobustRepair.NotCleanReason(mesh, false));

            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();
            Assert.Equal(2, cleanup.FlippedComponents);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh, false));
        }

        [Fact]
        public void ManyComponents_SideBySide_Fast()
        {
            // 20000 cubes in a row: the sweep finds no enclosing boxes without comparing all pairs
            DMesh3 mesh = new DMesh3();
            for (int i = 0; i < 20000; ++i)
                MeshEditor.Append(mesh, Cube(new Vector3d(2 * i, 0, 0), 1));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
            Assert.True(timer.ElapsedMilliseconds < 20000, $"{timer.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void EnclosingBoxes_SameAsAllPairs()
        {
            // random nested, overlapping, identical and equal-coordinate boxes on a coarse grid
            var rnd = new Random(3);
            for (int trial = 0; trial < 50; ++trial) {
                var boxes = new List<AxisAlignedBox3d>();
                int n = 1 + rnd.Next(60);
                for (int k = 0; k < n; ++k) {
                    if (k > 0 && rnd.Next(5) == 0) { boxes.Add(boxes[rnd.Next(k)]); continue; }
                    Func<int> c = () => rnd.Next(6);
                    int x0 = c(), y0 = c(), z0 = c();
                    boxes.Add(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x0 + 1 + rnd.Next(4), y0 + 1 + rnd.Next(4), z0 + 1 + rnd.Next(4))));
                }
                var found = MeshPostRepairCleanup.enclosing_boxes(boxes);
                for (int i = 0; i < n; ++i) {
                    var expected = Enumerable.Range(0, n).Where(j => j != i && boxes[j].Contains(boxes[i])).ToList();
                    Assert.Equal(expected, found[i]);
                }
            }
        }

        [Fact]
        public void UnusedVertexFarAway_SmallTrianglesNotDegenerate()
        {
            // the degenerate area is relative to the triangles' bounds: an unused vertex 1e4 away made the triangles of
            // a sphere of radius 0.02 "degenerate", and the cleanup collapsed it
            DMesh3 mesh = Cube(Vector3d.Zero, 1);
            DMesh3 sphere = Sphere(0.02, new Vector3d(3, 0, 0));
            MeshEditor.Append(mesh, sphere);
            mesh.AppendVertex(new Vector3d(1e4, 1e4, 1e4));
            int n = mesh.TriangleCount;
            var cleanup = new MeshPostRepairCleanup(mesh);
            cleanup.Apply();
            Assert.Equal(0, cleanup.FixedDegenerate);
            Assert.Equal(n, mesh.TriangleCount);
        }
    }
}
