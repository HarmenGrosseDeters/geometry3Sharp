using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A small far part must not make SafeRounding delete the rest. Two unit spheres and a unit tetrahedron 1e8 away:
    /// the result's bounding box diagonal is 1.7e8, and with tolerances relative to it (degenerate area 1e-12 diag^2,
    /// maxLength 1e-5 diag) every sphere triangle counted as degenerate and was contracted, the rest removed as tiny:
    /// Success, RoundingClean, 0 triangles. The result must keep the spheres' union and the tetrahedron.
    /// </summary>
    public class ExactBooleanFarComponentTests
    {
        static DMesh3 Sphere(Vector3d c)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static void AddTetrahedron(DMesh3 m, Vector3d p, double size)
        {
            int v0 = m.AppendVertex(p), v1 = m.AppendVertex(p + size * Vector3d.AxisX), v2 = m.AppendVertex(p + size * Vector3d.AxisY), v3 = m.AppendVertex(p + size * Vector3d.AxisZ);
            m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
        }

        // (per component, relative to a point of it: far from the origin the plain sum cancels)
        static double Volume(DMesh3 m)
        {
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            double volume = 0;
            foreach (var comp in comps.Components) {
                Vector3d o = m.GetVertex(m.GetTriangle(comp.Indices[0]).a);
                foreach (int t in comp.Indices) {
                    Index3i tv = m.GetTriangle(t);
                    volume += (m.GetVertex(tv.a) - o).Dot((m.GetVertex(tv.b) - o).Cross(m.GetVertex(tv.c) - o)) / 6;
                }
            }
            return volume;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (double far in new[] { 1e4, 1e8, 1e12 })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    foreach (bool sphere in new[] { false, true })
                        yield return new object[] { far, op, sphere };
        }

        // the far part: a unit tetrahedron, or a small sphere (radius 0.1)
        static double AddFar(DMesh3 a, double far, bool sphere)
        {
            var p = new Vector3d(far, far, far);
            if (sphere == false) {
                AddTetrahedron(a, p, 1);
                return 1.0 / 6;
            }
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 0.1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, p);
            double v = Volume(s);
            new MeshEditor(a).AppendMesh(s);
            return v;
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op)
        {
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            Assert.True(boolean.Compute(), boolean.Error);
            return boolean;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void FarPart_SpheresKept(double far, Ops op, bool sphere)
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.5, 0.3, 0.2));
            // the reference: the same operation without the far part
            ExactMeshBoolean near = Run(a, b, op);
            double expected = Volume(near.Result);
            double farVolume = AddFar(a, far, sphere);
            ExactMeshBoolean boolean = Run(a, b, op);
            DMesh3 r = boolean.Result;
            double total = expected + (op == Ops.Intersection ? 0 : farVolume);
            Assert.True(r.TriangleCount >= near.Result.TriangleCount / 2, $"far {far} {op}: {r.TriangleCount} triangles left of {near.Result.TriangleCount}");
            Assert.True(Math.Abs(Volume(r) - total) <= 1e-6 * total + 1e-6, $"far {far} {op}: volume {Volume(r)}, expected {total}");
            Assert.True(r.IsClosed() && Invariance.AllFinite(r));
            Assert.True(boolean.RoundingClean, $"far {far} {op}: not clean");
            Assert.Equal(0, boolean.Rounding.ScaleDisparity);
            // the spheres' part is the same as without the far part (same tolerances: the largest component's)
            Assert.Equal(near.Rounding.MaxDrift, boolean.Rounding.MaxDrift);
        }

        [Fact]
        public void FarPart_Similarities_Pow2BitIdentical()
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.5, 0.3, 0.2));
            AddFar(a, 1e8, false);
            ExactMeshBoolean b0 = Run(a, b, Ops.Union);
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), Ops.Union);
                Assert.True(bs.Result.TriangleCount >= b0.Result.TriangleCount / 2, $"{sim}: {bs.Result.TriangleCount} triangles");
                Assert.True(bs.Result.IsClosed() && Invariance.AllFinite(bs.Result), sim.ToString());
                if (sim.IsExact)
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", sim.ToString());
            }
        }

        [Fact]
        public void FarPart_ComputeTwice_SerialEqualsParallel()
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.5, 0.3, 0.2));
            AddFar(a, 1e8, true);
            gParallel.SerialOnThisThread = true;
            DMesh3 serial;
            try { serial = Run(a, b, Ops.Difference).Result; } finally { gParallel.SerialOnThisThread = false; }
            Assert.True(Invariance.Difference(serial, Run(a, b, Ops.Difference).Result) == "");
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Difference);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(Invariance.Difference(serial, boolean.Result) == "");
        }

        [Fact]
        public void RealVolume_NotRemovedAsThin_Counted()
        {
            // a sphere and a cube of edge 1e-3 with a needle, with a bound so wide (MaxDisplacement 0.01) that the cube
            // counts as thin (thickness edge / 3 below 0.01 diag) though it is not tiny: it holds real volume for its
            // area, so it is kept and counted, and the repair is not clean (before: removed silently)
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();
            int n = m.TriangleCount;
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(3, 0, 0), 0.5e-3 * Vector3d.One) }.Generate().MakeDMesh();
            int eid = cube.EdgeIndices().First();
            Index2i ev = cube.GetEdgeV(eid);
            Vector3d p = cube.GetVertex(ev.a), q = cube.GetVertex(ev.b);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, cube.SplitEdge(eid, out split));
            cube.SetVertex(split.vNew, p + 1e-12 * (q - p).Normalized);
            new MeshEditor(m).AppendMesh(cube);
            int total = m.TriangleCount;
            var repair = new MeshRoundingRepair(m) { MaxDisplacement = 0.01 };
            Assert.False(repair.Apply());
            Assert.Equal(1, repair.ScaleDisparity);
            Assert.Equal(0, repair.RemovedComponents);
            Assert.True(m.TriangleCount > n, "the cube was removed");
            Assert.True(m.IsClosed());
        }
    }
}
