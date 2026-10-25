using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// One-line bugs found in the class-by-class review (docs/Analyse-2026-10.md).
    /// Each test fails on the unfixed code.
    /// </summary>
    public class ReviewFixesTests
    {
        static DMesh3 Sphere(int edgeVertices = 6) { return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh(); }

        // --- math ---

        [Fact]
        public void MathUtil_InvSqrt()
        {
            Assert.Equal(0.5, MathUtil.InvSqrt(4), 12);
        }

        [Fact]
        public void Vector3d_GenerateComplementBasis_IsOrthonormal()
        {
            foreach (Vector3d dir in new[] { new Vector3d(1, 2, 3), new Vector3d(0.1, -5, 0.3), Vector3d.AxisX, Vector3d.AxisY }) {
                Vector3d w = dir.Normalized, u = Vector3d.Zero, v = Vector3d.Zero;
                Vector3d.GenerateComplementBasis(ref u, ref v, w);
                Assert.Equal(1.0, u.Length, 12);
                Assert.Equal(1.0, v.Length, 12);
                Assert.Equal(0.0, u.Dot(v), 12);
                Assert.Equal(0.0, u.Dot(w), 12);
            }
        }

        [Fact]
        public void Index3i_Index4i_CopyConstructors()
        {
            Assert.Equal(new Index3i(1, 2, 3), new Index3i(new Index3i(1, 2, 3)));
            Assert.Equal(new Index4i(1, 2, 3, 4), new Index4i(new Index4i(1, 2, 3, 4)));
        }

        [Fact]
        public void Matrix3_FuncConstructor()
        {
            var md = new Matrix3d((r, c) => 10 * r + c);
            var mf = new Matrix3f((r, c) => 10 * r + c);
            for (int r = 0; r < 3; ++r) {
                for (int c = 0; c < 3; ++c) {
                    Assert.Equal(10 * r + c, md[r, c]);
                    Assert.Equal(10 * r + c, mf[r, c]);
                }
            }
        }

        [Fact]
        public void MatrixUtil_Transpose3x3()
        {
            double[] M = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            double[] MT = new double[9];
            MatrixUtil.Transpose3x3(M, MT);
            Assert.Equal(new double[] { 1, 4, 7, 2, 5, 8, 3, 6, 9 }, MT);
        }

        // --- core ---

        [Fact]
        public void Dijkstra_Dense_ImprovedQueueEntryIsNotOverwrittenByWorsePath()
        {
            // 1 is first reached via 3 (dist 7), improved via 2 (dist 5), then offered 6 via 4.
            // The stale distance of 1 used to let the worse path 0-4-1 replace 0-2-1.
            var edges = new Dictionary<(int, int), float> {
                { (0, 3), 1 }, { (0, 2), 2 }, { (0, 4), 3 },
                { (3, 1), 6 }, { (2, 1), 3 }, { (4, 1), 3 }
            };
            Func<int, int, float> distF = (a, b) => edges.TryGetValue((a, b), out float d) ? d : edges[(b, a)];
            Func<int, IEnumerable<int>> nbrsF = (a) => edges.Keys.Where(e => e.Item1 == a || e.Item2 == a).Select(e => e.Item1 == a ? e.Item2 : e.Item1).ToList();

            foreach (bool sparse in new[] { true, false }) {
                var dijkstra = new DijkstraGraphDistance(5, sparse, (id) => true, distF, nbrsF);
                dijkstra.AddSeed(0, 0);
                dijkstra.Compute();
                Assert.Equal(5, dijkstra.GetDistance(1), 5);
                var path = new List<int>();
                dijkstra.GetPathToSeed(1, path);
                Assert.Equal(new List<int> { 1, 2, 0 }, path);
            }
        }

        // --- approximation ---

        [Fact]
        public void BiArcFit2_SemicircleCase_SetsBothArcs()
        {
            var fit = new BiArcFit2(new Vector2d(0, 0), new Vector2d(0, 1), new Vector2d(4, 0), new Vector2d(0, 1));
            Assert.False(fit.Arc1IsSegment);
            Assert.False(fit.Arc2IsSegment);
            Assert.True(fit.Arc1.Center.EpsilonEqual(new Vector2d(1, 0), 1e-12));
            Assert.True(fit.Arc2.Center.EpsilonEqual(new Vector2d(3, 0), 1e-12));
            Assert.Equal(1.0, fit.Arc1.Radius, 12);
            Assert.Equal(1.0, fit.Arc2.Radius, 12);
        }

        // --- containment ---

        [Fact]
        public void ContOrientedBox3_ResultValid()
        {
            var rand = new Random(31337);
            var points = Enumerable.Range(0, 200).Select(i => new Vector3d(rand.NextDouble(), 2 * rand.NextDouble(), 3 * rand.NextDouble())).ToList();
            var cont = new ContOrientedBox3(points);
            Assert.True(cont.ResultValid);
            Box3d box = cont.Box;
            box.Expand(1e-9);       // extreme points lie exactly on the box faces
            foreach (Vector3d p in points)
                Assert.True(box.Contains(p));
        }

        // --- curve ---

        [Fact]
        public void BaseCurve2_GetMinTime()
        {
            var curve = new BezierCurve2(2, new[] { Vector2d.Zero, Vector2d.AxisX, Vector2d.One });
            curve.SetTimeInterval(0.25, 0.75);
            Assert.Equal(0.25, curve.GetMinTime());
            Assert.Equal(0.75, curve.GetMaxTime());
        }

        // --- distance ---

        [Fact]
        public void DistTriangle3Triangle3_ClosestPointsBelongToTheirTriangles()
        {
            // closest pair is found on an edge of triangle1, ie in the second loop of GetSquared
            var t0 = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(0, 10, 0));
            var t1 = new Triangle3d(new Vector3d(1, 1, 1), new Vector3d(2, 1, 1), new Vector3d(1, 2, 5));
            var dist = new DistTriangle3Triangle3(t0, t1);
            dist.Compute();
            Assert.Equal(1.0, dist.Get(), 12);
            Assert.Equal(0.0, dist.Triangle0Closest.z, 12);
            Assert.Equal(1.0, dist.Triangle1Closest.z, 12);

            var rand = new Random(17);
            Func<Vector3d> rv = () => new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 4;
            for (int k = 0; k < 300; ++k) {
                var a = new Triangle3d(rv(), rv(), rv());
                var b = new Triangle3d(rv(), rv(), rv());
                var d = new DistTriangle3Triangle3(a, b);
                double len = d.Get();
                Assert.True(a.PointAt(d.Triangle0BaryCoords).Distance(d.Triangle0Closest) < 1e-8);
                Assert.True(b.PointAt(d.Triangle1BaryCoords).Distance(d.Triangle1Closest) < 1e-8);
                Assert.Equal(len, d.Triangle0Closest.Distance(d.Triangle1Closest), 8);
            }
        }

        // --- implicit ---

        [Fact]
        public void LinearFalloff_Duplicate()
        {
            var falloff = new LinearFalloff() { ConstantRange = 0.25 };
            IFalloffFunction dup = falloff.Duplicate();
            Assert.IsType<LinearFalloff>(dup);
            Assert.Equal(falloff.FalloffT(0.5), dup.FalloffT(0.5));
        }

        [Fact]
        public void ImplicitPoint2d_InBounds()
        {
            var pt = new ImplicitPoint2d(10, 0, 2);
            Assert.True(pt.InBounds(10, 0));
            Assert.True(pt.InBounds(11, 1));
            Assert.False(pt.InBounds(10, 5));
            Assert.False(pt.InBounds(0, 0));
        }

        // --- mesh ---

        [Fact]
        public void EdgeSpan_CheckValidity_ReportsInvalidSpan()
        {
            DMesh3 mesh = Sphere();
            int eid = mesh.EdgeIndices().First();
            Index2i ev = mesh.GetEdgeV(eid);
            var valid = new EdgeSpan(mesh, new[] { ev.a, ev.b }, new[] { eid }, true);
            Assert.True(valid.CheckValidity(FailMode.ReturnOnly));

            int far = mesh.VertexIndices().First(v => v != ev.a && v != ev.b && mesh.FindEdge(ev.a, v) == DMesh3.InvalidID);
            var invalid = new EdgeSpan(mesh, new[] { ev.a, far }, new[] { eid }, true);
            Assert.False(invalid.CheckValidity(FailMode.ReturnOnly));
        }

        // --- mesh_generators ---

        class YCylinder : ImplicitFunction3d
        {
            public double Value(ref Vector3d p) { return Math.Sqrt(p.x * p.x + p.z * p.z) - 0.08; }
        }

        [Fact]
        public void MarchingCubesPro_MoreThan4096CellsAlongY_MatchesMarchingCubes()
        {
            // edge-hash flags were int shifts (1<<60 == 1<<28) and collided with the y bits from 4096 cells on
            var bounds = new AxisAlignedBox3d(new Vector3d(-0.12, 0, -0.12), new Vector3d(0.12, 4200 * 0.05, 0.12));
            var pro = new MarchingCubesPro() { Implicit = new YCylinder(), Bounds = bounds, CubeSize = 0.05, ParallelCompute = false };
            pro.Generate();
            var mc = new MarchingCubes() { Implicit = new YCylinder(), Bounds = bounds, CubeSize = 0.05, ParallelCompute = false };
            mc.Generate();
            Assert.True(pro.CellDimensions.y > 4096);
            Assert.Equal(mc.Mesh.VertexCount, pro.Mesh.VertexCount);
            Assert.Equal(mc.Mesh.TriangleCount, pro.Mesh.TriangleCount);
        }

        // --- mesh_ops ---

        [Fact]
        public void MeshPlaneCut_FillHoles_ReturnsTrueOnSuccess()
        {
            DMesh3 mesh = Sphere();
            var cut = new MeshPlaneCut(mesh, Vector3d.Zero, Vector3d.AxisY);
            Assert.True(cut.Cut());
            Assert.True(cut.FillHoles());
            Assert.All(cut.LoopFillTriangles, tris => Assert.NotNull(tris));
            Assert.True(mesh.IsClosed());
        }

        // --- spatial ---

        [Fact]
        public void DMeshAABBTree_TestIntersection_TreeWithSingleChildNodes()
        {
            // a bottom-up tree has single-child nodes; descending into one used to recurse forever
            DMesh3 sphere = Sphere(9);
            var sphereTree = new DMeshAABBTree3(sphere);
            sphereTree.Build(DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings);

            foreach (int tid in sphere.TriangleIndices()) {
                Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
                sphere.GetTriVertices(tid, ref v0, ref v1, ref v2);
                var inner = new DMesh3();
                inner.AppendTriangle(inner.AppendVertex(0.97 * v0), inner.AppendVertex(0.97 * v1), inner.AppendVertex(0.97 * v2));
                var innerTree = new DMeshAABBTree3(inner, true);
                Assert.False(innerTree.TestIntersection(sphereTree));
            }
        }
    }
}
