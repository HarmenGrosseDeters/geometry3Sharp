using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Uses the EnergyDecreaseTolerance option, so it lives apart from FoldoverFreeTests (which must compile on base).
    public class FoldoverFreeToleranceTests
    {
        static DMesh3 Disc()
        {
            var m = new DMesh3();
            for (int y = 0; y < 5; ++y)
                for (int x = 0; x < 5; ++x) m.AppendVertex(new Vector3d(x * 0.25, y * 0.25, 0));
            for (int y = 0; y < 4; ++y)
                for (int x = 0; x < 4; ++x) {
                    int a = 5 * y + x;
                    m.AppendTriangle(a, a + 1, a + 6); m.AppendTriangle(a, a + 6, a + 5);
                }
            return m;
        }

        static Vector2d[] FoldedStart(DMesh3 m)
        {
            var r = new TutteEmbedding(m).Compute();
            r.UV[12] = new Vector2d(2, 0); r.UV[7] = new Vector2d(-1.5, 1.5);
            return r.UV;
        }

        [Fact]
        public void EnergyDecreaseTolerance_IsValidated()
        {
            var m = Disc(); var uv = FoldedStart(m);
            foreach (double bad in new[] { -1e-3, 1, double.NaN, double.PositiveInfinity })
                Assert.Equal(ParameterizationStatus.InvalidConstraints,
                    new FoldoverFreeParameterization(m, uv) { EnergyDecreaseTolerance = bad }.Compute().Status);
        }

        // Tolerance 0 switches the energy stop off only: the Newton-decrement exit still ends the run (before the
        // iteration limit), with the same quality, and takes longer than the default.
        [Fact]
        public void ToleranceZero_KeepsNewtonExit_AndRunsLonger()
        {
            var m = Disc(); var uv = FoldedStart(m);
            var normal = new FoldoverFreeParameterization(m, uv).Compute();
            var strict = new FoldoverFreeParameterization(m, uv) { EnergyDecreaseTolerance = 0 }.Compute();
            Assert.True(strict.Succeeded, strict.Error);
            Assert.True(strict.Converged, strict.Error);
            Assert.True(strict.Iterations < 200, "stopped by the iteration limit: " + strict.Iterations);
            Assert.True(strict.Iterations > normal.Iterations);
            Assert.True(strict.Distortion.SymmetricDirichlet <= normal.Distortion.SymmetricDirichlet * 1.001);
        }
    }
}
