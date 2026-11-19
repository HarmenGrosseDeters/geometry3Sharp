using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// R15 (docs/ROBUSTNESS_SWEEP.md): the Status of the most recent MeshIsoCurves.Compute().
    /// </summary>
    public class MeshIsoCurvesStatusTests
    {
        public static IEnumerable<object[]> ModeData() => MeshIsoCurvesExtremeFieldTests.ModeData();

        static DMesh3 Triangle(Similarity s) => MeshIsoCurvesExtremeFieldTests.TriangleFor(s);

        [Theory]
        [MemberData(nameof(ModeData))]
        public void Status_SingleInfinityNextToFiniteValues_IsNonFiniteInput(MeshIsoCurves.RootfindingModes mode)
        {
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, Field(p => p.y > 0.5 ? double.PositiveInfinity : p.x - 0.5, mode).Status);
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, Field(p => p.y > 0.5 ? double.NegativeInfinity : p.x - 0.5, mode).Status);
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void ZeroNextToInfinities_DoesNotProduceNaN(MeshIsoCurves.RootfindingModes mode)
        {
            double inf = double.PositiveInfinity;
            foreach (double[] f in new[] { new[] { 0.0, inf, inf }, new[] { 0.0, inf, -inf }, new[] { 0.0, -inf, inf }, new[] { 0.0, -inf, -inf } }) {
                var iso = new MeshIsoCurves(Triangle(Similarity.Identity), p => p.x < 0.5 && p.y < 0.5 ? f[0] : (p.x > 0.5 ? f[1] : f[2])) { RootMode = mode };
                iso.Compute();
                foreach (int v in iso.Graph.VertexIndices())
                    Assert.True(iso.Graph.GetVertex(v).IsFinite, $"{mode} {string.Join(",", f)}");
            }
        }

        // ---- Status ----

        static MeshIsoCurves Field(Func<Vector3d, double> f, MeshIsoCurves.RootfindingModes mode = MeshIsoCurves.RootfindingModes.SingleLerp)
        {
            var iso = new MeshIsoCurves(Triangle(Similarity.Identity), f) { RootMode = mode };
            iso.Compute();
            return iso;
        }

        [Fact]
        public void Status_BeforeCompute_IsNotComputed()
        {
            var iso = new MeshIsoCurves(Triangle(Similarity.Identity), p => p.x);
            Assert.Equal(MeshIsoCurves.ComputeStatus.NotComputed, iso.Status);
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void Status_Crossing_IsOk(MeshIsoCurves.RootfindingModes mode)
        {
            var iso = Field(p => p.x - 0.5, mode);
            Assert.Equal(MeshIsoCurves.ComputeStatus.Ok, iso.Status);
            Assert.True(iso.Graph.EdgeCount > 0);
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void Status_ValidFieldWithoutCrossing_IsNoCrossing(MeshIsoCurves.RootfindingModes mode)
        {
            foreach (double c in new[] { 1.0, -1.0, 1e308, -1e-300 }) {
                var iso = Field(p => c, mode);
                Assert.Equal(MeshIsoCurves.ComputeStatus.NoCrossing, iso.Status);
                Assert.True(iso.Graph.EdgeCount == 0);
            }
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void Status_NaN_IsNonFiniteInput(MeshIsoCurves.RootfindingModes mode)
        {
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, Field(p => double.NaN, mode).Status);
            // one NaN in a triangle that would otherwise cross
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, Field(p => p.y > 0.5 ? double.NaN : p.x - 0.5, mode).Status);
            Assert.True(Field(p => double.NaN, mode).Graph.EdgeCount == 0);
        }

        [Theory]
        [MemberData(nameof(ModeData))]
        public void Status_Infinity_IsNonFiniteInput(MeshIsoCurves.RootfindingModes mode)
        {
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, Field(p => double.PositiveInfinity, mode).Status);
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, Field(p => double.NegativeInfinity, mode).Status);
        }

        [Fact]
        public void Status_BisectionFindsTheJumpBetweenInfinities_AndReportsNonFiniteInput()
        {
            // +infinity, -infinity, +infinity: the existing behaviour is kept (Bisection uses the signs only) ...
            var iso = Field(p => p.x < 0.5 ? double.PositiveInfinity : double.NegativeInfinity, MeshIsoCurves.RootfindingModes.Bisection);
            Assert.True(iso.Graph.EdgeCount == 1, "Bisection finds the jump");
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, iso.Status);
            // ... and the interpolating modes add nothing
            foreach (var mode in new[] { MeshIsoCurves.RootfindingModes.SingleLerp, MeshIsoCurves.RootfindingModes.LerpSteps }) {
                var other = Field(p => p.x < 0.5 ? double.PositiveInfinity : double.NegativeInfinity, mode);
                Assert.True(other.Graph.EdgeCount == 0);
                Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, other.Status);
            }
        }

        [Fact]
        public void Status_TriangleSubsetOverload_ReportsOnlyTheProcessedTriangles()
        {
            var m = Triangle(Similarity.Identity);
            m.AppendVertex(new Vector3d(1, 1, 0));
            m.AppendTriangle(1, 3, 2);
            var iso = new MeshIsoCurves(m, p => p.x + p.y > 1.5 ? double.NaN : p.x - 0.5);
            iso.Compute(new[] { 0 });
            Assert.Equal(MeshIsoCurves.ComputeStatus.Ok, iso.Status);
            iso.Compute(new[] { 1 });
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, iso.Status);
            iso.Compute(new int[0]);
            Assert.Equal(MeshIsoCurves.ComputeStatus.NoCrossing, iso.Status);
        }

        [Fact]
        public void Status_IsResetByEveryCompute()
        {
            var mode = MeshIsoCurves.RootfindingModes.SingleLerp;
            double level = double.NaN;
            var iso = new MeshIsoCurves(Triangle(Similarity.Identity), p => p.x - level) { RootMode = mode };
            iso.Compute();
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, iso.Status);
            level = 0.5;
            iso.Compute();
            Assert.Equal(MeshIsoCurves.ComputeStatus.Ok, iso.Status);
            level = 5;
            iso.Compute();
            Assert.Equal(MeshIsoCurves.ComputeStatus.NoCrossing, iso.Status);
            level = double.PositiveInfinity;
            iso.Compute();
            Assert.Equal(MeshIsoCurves.ComputeStatus.NonFiniteInput, iso.Status);
            level = 0.5;
            iso.Compute();
            Assert.Equal(MeshIsoCurves.ComputeStatus.Ok, iso.Status);
            Assert.True(iso.Graph.EdgeCount == 1, "no stale graph");
        }

        [Fact]
        public void Status_AfterAFailedCompute_IsNotComputed()
        {
            bool fail = false;
            var iso = new MeshIsoCurves(Triangle(Similarity.Identity), p => fail ? throw new InvalidOperationException() : p.x - 0.5);
            iso.Compute();
            Assert.Equal(MeshIsoCurves.ComputeStatus.Ok, iso.Status);
            fail = true;
            Assert.Throws<InvalidOperationException>(() => iso.Compute());
            Assert.Equal(MeshIsoCurves.ComputeStatus.NotComputed, iso.Status);
        }
    }
}
