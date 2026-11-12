using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Vector2d / 3d / 4d and Vector2f / 3f / 4f IsFinite tested the sum of the components (upstream 1.0.324), which overflows for finite
    /// large values: (1e308, 1e308) was "not finite", and ConvexHull2 refused such points. Now every component is tested. The callers
    /// that the old filter kept away from large coordinates are run here on them: each gives a defined result (the same as at an ordinary
    /// scale, a refusal, or a finite output), never NaN or an exception.
    /// </summary>
    public class VectorIsFiniteTests
    {
        const double Big = 1e308;

        // the assertions check components themselves, independent of the IsFinite under test
        static bool Finite(Vector3d v) => !(double.IsNaN(v.x) || double.IsInfinity(v.x) || double.IsNaN(v.y) || double.IsInfinity(v.y) || double.IsNaN(v.z) || double.IsInfinity(v.z));
        static bool Finite(Vector2d v) => !(double.IsNaN(v.x) || double.IsInfinity(v.x) || double.IsNaN(v.y) || double.IsInfinity(v.y));
        const float BigF = 3e38f;

        [Fact]
        public void LargeFiniteComponents_AreFinite()
        {
            Assert.True(new Vector2d(Big, Big).IsFinite);
            Assert.True(new Vector2d(-Big, -Big).IsFinite);
            Assert.True(new Vector3d(Big, Big, 0).IsFinite);
            Assert.True(new Vector3d(-Big, -Big, Big).IsFinite);
            Assert.True(new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue).IsFinite);
            Assert.True(new Vector4d(Big, Big, Big, Big).IsFinite);
            Assert.True(new Vector2f(BigF, BigF).IsFinite);
            Assert.True(new Vector3f(-BigF, -BigF, BigF).IsFinite);
            Assert.True(new Vector4f(BigF, BigF, -BigF, -BigF).IsFinite);
        }

        public static TheoryData<double> NonFinite() => new TheoryData<double> { double.NaN, double.PositiveInfinity, double.NegativeInfinity };

        /// <summary> NaN or an infinity in any one component, the others finite (also large): not finite </summary>
        [Theory]
        [MemberData(nameof(NonFinite))]
        public void NonFiniteComponent_IsNotFinite(double bad)
        {
            float badF = (float)bad;
            foreach (double other in new[] { 0, 1, -Big }) {
                float otherF = (float)Math.Max(-BigF, Math.Min(BigF, other));
                for (int i = 0; i < 4; ++i) {
                    double[] c = { other, other, other, other };
                    float[] f = { otherF, otherF, otherF, otherF };
                    c[i] = bad; f[i] = badF;
                    if (i < 2) {
                        Assert.False(new Vector2d(c[0], c[1]).IsFinite);
                        Assert.False(new Vector2f(f[0], f[1]).IsFinite);
                    }
                    if (i < 3) {
                        Assert.False(new Vector3d(c[0], c[1], c[2]).IsFinite);
                        Assert.False(new Vector3f(f[0], f[1], f[2]).IsFinite);
                    }
                    Assert.False(new Vector4d(c[0], c[1], c[2], c[3]).IsFinite);
                    Assert.False(new Vector4f(f[0], f[1], f[2], f[3]).IsFinite);
                }
            }
            // opposite infinities, whose sum is NaN, and ordinary values
            Assert.False(new Vector3d(double.PositiveInfinity, double.NegativeInfinity, 0).IsFinite);
            Assert.True(new Vector3d(1, -2, 3).IsFinite);
            Assert.True(Vector2d.Zero.IsFinite && Vector3f.Zero.IsFinite && Vector4d.Zero.IsFinite);
        }

        static List<Vector2d> Square(double scale, double offset) =>
            new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1), new Vector2d(0.5, 0.4), new Vector2d(0.2, 0.7) }
                .Select(p => scale * (p + new Vector2d(offset, offset))).ToList();

        /// <summary> points near 1e308 (up to 1.5e308; their coordinate sums overflow) give the hull of the same points at unit size; 1.0.324 threw
        /// ArgumentException ("not finite") </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(-1.5)]
        public void ConvexHull2_NearMaxValue_GivesTheHull(double offset)
        {
            var unit = new ConvexHull2(Square(1, offset), 1e-9, QueryNumberType.QT_DOUBLE);
            var big = new ConvexHull2(Square(Big, offset), 1e-9, QueryNumberType.QT_DOUBLE);
            Assert.Equal(2, big.Dimension);
            Assert.Equal(unit.HullIndices, big.HullIndices);
            Assert.All(big.GetHullPolygon().Vertices, v => Assert.True(Finite(v)));
        }

        static DMesh3 Sphere(double scale, Vector3d offset)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 6, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, scale * m.GetVertex(v) + offset);
            return m;
        }

        static void AssertFinite(DMesh3 m, string what)
        {
            foreach (int v in m.VertexIndices()) Assert.True(Finite(m.GetVertex(v)), what + ": vertex " + v + " is not finite");
        }

        /// <summary> a plane cut of a sphere through its centre at 2^k scales up to 2^1020 (about 1e307, so the coordinate sums and the
        /// diagonal overflow) gives the cut of the unit sphere, scaled bit for bit. 1.0.324 (and this fork before the fix) collapsed
        /// every edge there: the tolerance squared, or the diagonal of the bounds, was infinite </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(200)]
        [InlineData(600)]
        [InlineData(1020)]
        public void MeshPlaneCut_AtHugeScales_IsTheCutOfTheUnitSphere(int k)
        {
            double s = Math.Pow(2, k);
            DMesh3 unit = Sphere(1, Vector3d.Zero), big = Sphere(s, Vector3d.Zero);
            Assert.True(new MeshPlaneCut(unit, Vector3d.Zero, Vector3d.AxisZ).Cut());
            var cut = new MeshPlaneCut(big, Vector3d.Zero, Vector3d.AxisZ);
            Assert.True(cut.Cut());
            Assert.Equal(unit.TriangleCount, big.TriangleCount);
            Assert.Equal(unit.VertexCount, big.VertexCount);
            Assert.True(big.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(cut.CutLoops.Count > 0);
            AssertFinite(big, "cut");
            foreach (int v in unit.VertexIndices()) Assert.Equal(s * unit.GetVertex(v), big.GetVertex(v));
        }

        /// <summary> two triangles near 1e308 that cross (their coordinate sums overflow): the same intersection as the copy scaled by
        /// 2^-1000; 1.0.324 reported Empty </summary>
        [Fact]
        public void IntrTriangle3Triangle3_NearMaxValue_FindsTheCrossing()
        {
            Func<double, Triangle3d[]> make = (s) => new[] {
                new Triangle3d(s * new Vector3d(1.0, 1.0, 0.9), s * new Vector3d(1.4, 1.0, 0.9), s * new Vector3d(1.0, 1.4, 0.9)),
                new Triangle3d(s * new Vector3d(1.1, 1.1, 0.7), s * new Vector3d(1.1, 1.1, 1.1), s * new Vector3d(1.2, 1.05, 1.1)) };
            Triangle3d[] small = make(Math.Pow(2, 23)), big = make(Math.Pow(2, 1023));    // (up to 1.26e308)
            var a = new IntrTriangle3Triangle3(small[0], small[1]);
            var b = new IntrTriangle3Triangle3(big[0], big[1]);
            Assert.True(a.Test());
            Assert.True(b.Test());
            Assert.True(a.Find());
            Assert.True(b.Find());
            Assert.Equal(a.Type, b.Type);
            Assert.Equal(a.Quantity, b.Quantity);
            for (int i = 0; i < b.Quantity; ++i) Assert.True(Finite(b.Points[i]));
        }

        public static TheoryData<string> Callers() => new TheoryData<string> {
            "PinSharpVertices", "Reducer", "MeshCurvature", "Laplacian", "Fidelity", "ICP", "RepairOrientation", "RoundingRepair",
            "PointCloudSurface", "RemoveOccluded" };

        /// <summary> the other callers of IsFinite on a sphere near 1e308 (every coordinate sum overflows): no exception, no NaN in the
        /// output; a refusal (false) is a defined result </summary>
        [Theory]
        [MemberData(nameof(Callers))]
        public void Callers_NearMaxValue_GiveDefinedResults(string op)
        {
            var off = new Vector3d(1.2e308, 1.2e308, 1.2e308);
            DMesh3 m = Sphere(0.4e308, off);
            switch (op) {
                case "PinSharpVertices": {
                    int n = MeshConstraintUtil.PinSharpVertices(new MeshConstraints(), m);
                    Assert.InRange(n, 0, m.VertexCount);
                    break;
                }
                case "Reducer": {
                    new Reducer(m).ReduceToTriangleCount(m.TriangleCount / 2);
                    Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                    AssertFinite(m, op);
                    break;
                }
                case "MeshCurvature": {
                    var c = MeshCurvature.Compute(m);
                    Assert.All(c.Mean, h => Assert.False(double.IsNaN(h)));
                    Assert.All(c.AngleDeficit, d => Assert.False(double.IsNaN(d)));
                    break;
                }
                case "Laplacian": {
                    var s = new LaplacianMeshSmoother(m);
                    foreach (int v in m.VertexIndices().Where(v => v % 5 == 0)) s.SetConstraint(v, m.GetVertex(v), 1, false);
                    s.Initialize();
                    var result = new Vector3d[m.VertexCount];
                    if (s.SolveMultipleCG(result))
                        Assert.All(result, v => Assert.True(Finite(v)));
                    break;
                }
                case "Fidelity": {
                    var f = new MeshFidelityMetrics(m, Sphere(0.404e308, off));
                    // a refusal leaves the metrics NaN; a computed result must have finite distances
                    if (f.Compute())
                        Assert.False(double.IsNaN(f.Chamfer) || double.IsInfinity(f.Chamfer), "chamfer " + f.Chamfer);
                    break;
                }
                case "ICP": {
                    var icp = new MeshICP(Sphere(0.4e308, off + new Vector3d(0.004e308, 0, 0)), new DMeshAABBTree3(m, true));
                    icp.Solve();
                    Assert.True(Finite(icp.Translation));
                    Assert.False(double.IsNaN(icp.Rotation.w));
                    break;
                }
                case "RepairOrientation": {
                    var r = new MeshRepairOrientation(m);
                    r.OrientComponents();
                    r.SolveGlobalOrientation();
                    AssertFinite(m, op);
                    break;
                }
                case "RoundingRepair": {
                    var r = new MeshRoundingRepair(m);
                    r.Apply();
                    AssertFinite(m, op);
                    break;
                }
                case "PointCloudSurface": {
                    var p = new PointCloudSurface(m.Vertices().ToList());
                    // a refusal names its reason; a result is a mesh with finite vertices
                    if (p.Compute()) AssertFinite(p.Result, op);
                    else Assert.False(string.IsNullOrEmpty(p.Error), "a refusal without a reason");
                    break;
                }
                case "RemoveOccluded": {
                    new RemoveOccludedTriangles(m).Apply();
                    Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                    break;
                }
            }
        }
    }
}
