using System;
using System.Linq;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class FoldoverFreeTests
    {
        readonly ITestOutputHelper output;
        public FoldoverFreeTests(ITestOutputHelper output) { this.output = output; }

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

        static Vector2d[] Start(DMesh3 m, bool folded = true)
        {
            var r = new TutteEmbedding(m).Compute();
            Assert.True(r.Succeeded, r.Error);
            if (folded) {
                r.UV[12] = new Vector2d(2, 0);
                r.UV[7] = new Vector2d(-1.5, 1.5);
            }
            return r.UV;
        }

        static void SameBits(Vector2d a, Vector2d b)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.x), BitConverter.DoubleToInt64Bits(b.x));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.y), BitConverter.DoubleToInt64Bits(b.y));
        }

        static void Valid(DMesh3 m, Vector2d[] start, ParameterizationResult r)
        {
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(m.TriangleCount, r.Distortion.Triangles);
            Assert.Equal(0, r.Distortion.FlippedTriangles);
            Assert.Equal(0, r.Distortion.DegenerateUVTriangles);
            Assert.True(MathUtil.IsFinite(r.Distortion.SymmetricDirichlet));
            foreach (int vid in m.VertexIndices()) {
                Assert.True(MathUtil.IsFinite(r.UV[vid].x) && MathUtil.IsFinite(r.UV[vid].y));
                if (m.IsBoundaryVertex(vid)) SameBits(start[vid], r.UV[vid]);
            }
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                Vector2d a = r.UV[t.b] - r.UV[t.a], b = r.UV[t.c] - r.UV[t.a];
                Assert.True(a.Cross(b) > 1e-12 * Math.Max(a.LengthSquared, Math.Max(b.LengthSquared, (a - b).LengthSquared)));
            }
        }

        [Fact]
        public void FoldedDisc_UntanglesWithoutMovingBoundary()
        {
            var m = Disc(); var uv = Start(m);
            var before = ParameterizationDistortion.Compute(m, uv);
            Assert.True(before.FlippedTriangles > 0);
            var r = new FoldoverFreeParameterization(m, uv).Compute();
            output.WriteLine($"round fixture: before flips={before.FlippedTriangles} degenerate={before.DegenerateUVTriangles}; " +
                $"after status={r.Status} iterations={r.Iterations} flips={r.Distortion?.FlippedTriangles} " +
                $"degenerate={r.Distortion?.DegenerateUVTriangles} energy={r.Energy:R}; {r.Error}");
            Valid(m, uv, r);
            Assert.True(r.Converged, "a flip-free final iterate is the converged solution: " + r.Error);
            Assert.True(UVOverlap.Compute(m, r.UV).IsInjective);
        }

        [Fact]
        public void ValidStart_StaysValidAndConverges()
        {
            var m = Disc(); var uv = Start(m, false);
            Assert.Equal(0, ParameterizationDistortion.Compute(m, uv).FlippedTriangles);
            var r = new FoldoverFreeParameterization(m, uv).Compute();
            Valid(m, uv, r);
            Assert.True(r.Converged, r.Error);
            Assert.True(r.Iterations > 0);
        }

        [Fact]
        public void DegenerateInteriorStart_Untangles()
        {
            var m = Disc(); var uv = Start(m, false);
            for (int i = 0; i < uv.Length; ++i) if (!m.IsBoundaryVertex(i)) uv[i] = Vector2d.Zero;
            Assert.True(ParameterizationDistortion.Compute(m, uv).FlippedTriangles > 0);
            Valid(m, uv, new FoldoverFreeParameterization(m, uv).Compute());
        }

        [Fact]
        public void Similarities_AndUnusedOutlier_DoNotChangeSolution()
        {
            var m = Disc(); var uv = Start(m);
            var baseline = new FoldoverFreeParameterization(m, uv).Compute();
            Valid(m, uv, baseline);
            foreach (Similarity s in Similarity.All()) {
                var transformed = Invariance.Transformed(m, s);
                transformed.AppendVertex(new Vector3d(1e200, -1e200, 1e200));
                var r = new FoldoverFreeParameterization(transformed, uv).Compute();
                Assert.True(r.Succeeded, s.Name + ": " + r.Error);
                for (int i = 0; i < uv.Length; ++i) {
                    if (s.IsExact) SameBits(baseline.UV[i], r.UV[i]);
                    else Assert.True((baseline.UV[i] - r.UV[i]).Length <= Math.Max(1e-7, 100 * s.Resolution(1)), s.Name);
                }
                Assert.Equal(0, r.Distortion.FlippedTriangles);
                Assert.Equal(0, r.Distortion.DegenerateUVTriangles);
            }
        }

        [Fact]
        public void ReuseABA_RepeatedRunsAndInputImmutability()
        {
            var m = Disc(); var uv = Start(m); var saved = (Vector2d[])uv.Clone();
            var positions = m.VertexIndices().Select(m.GetVertex).ToArray();
            var op = new FoldoverFreeParameterization(m, uv);
            var a = op.Compute(); Valid(m, uv, a);
            var repeat = op.Compute();
            op.MaxIterations = 0;
            var b = op.Compute(); Assert.Equal(ParameterizationStatus.FlippedTriangles, b.Status);
            Assert.Equal(0, b.Iterations); Assert.False(b.Converged);
            op.MaxIterations = 200;
            var again = op.Compute();
            var fresh = new FoldoverFreeParameterization(m, uv).Compute();
            for (int i = 0; i < uv.Length; ++i) {
                SameBits(a.UV[i], repeat.UV[i]); SameBits(a.UV[i], again.UV[i]); SameBits(a.UV[i], fresh.UV[i]);
                SameBits(saved[i], uv[i]); SameBits(uv[i], b.UV[i]);
                Assert.Equal(positions[i], m.GetVertex(i));
            }
            op.InitialUV = new Vector2d[0];
            Assert.Equal(ParameterizationStatus.InvalidConstraints, op.Compute().Status);
            op.InitialUV = uv;
            var restored = op.Compute();
            for (int i = 0; i < uv.Length; ++i) SameBits(a.UV[i], restored.UV[i]);
        }

        [Fact]
        public void InvalidConstraints_AndMesh_AreDiagnostic()
        {
            var m = Disc(); var uv = Start(m);
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                var invalid = (Vector2d[])uv.Clone(); invalid[12] = new Vector2d(bad, 0);
                Assert.Equal(ParameterizationStatus.InvalidConstraints, new FoldoverFreeParameterization(m, invalid).Compute().Status);
            }
            var collapsed = (Vector2d[])uv.Clone(); collapsed[0] = collapsed[1];
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new FoldoverFreeParameterization(m, collapsed).Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidMesh, new FoldoverFreeParameterization(new DMesh3(), uv).Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidMesh, new FoldoverFreeParameterization(null, uv).Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new FoldoverFreeParameterization(m, null).Compute().Status);
            foreach (double epsilon in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
                Assert.Equal(ParameterizationStatus.InvalidConstraints,
                    new FoldoverFreeParameterization(m, uv) { InitialEpsilon = epsilon }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints,
                new FoldoverFreeParameterization(m, uv) { MinimumEpsilon = 2 }.Compute().Status);
            m.SetVertex(12, m.GetVertex(7));
            Assert.Equal(ParameterizationStatus.InvalidMesh, new FoldoverFreeParameterization(m, uv).Compute().Status);
        }

        [Fact]
        public void FullyLockedTriangle_ValidOrProvablyInfeasible()
        {
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(Vector3d.AxisX); m.AppendVertex(Vector3d.AxisY); m.AppendTriangle(0, 1, 2);
            var uv = new[] { new Vector2d(-0.0, 0), new Vector2d(1, 0), new Vector2d(0, 1) };
            Valid(m, uv, new FoldoverFreeParameterization(m, uv).Compute());
            uv[2] = new Vector2d(0, -1);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new FoldoverFreeParameterization(m, uv).Compute().Status);
        }

        [Theory]
        [InlineData(1, 0.2, -0.1, 1.3)]
        [InlineData(1, 0.2, 0.3, -1)]
        [InlineData(0, 0, 0, 0)]
        [InlineData(1, 2, 2, 4)]
        public void AnalyticGradient_AndLiftedHessian_MatchIndependentOracle(double a, double b, double c, double d)
        {
            var j = new[] { a, b, c, d }; const double epsilon = 0.7, delta = 1e-5;
            var g = new double[4]; var h = new double[4, 4];
            double energy = FoldoverFreeParameterization.Element(j, epsilon, g, h);
            Assert.True(Math.Abs(energy - DirectEnergy(j, epsilon)) < 1e-12 * energy);
            for (int i = 0; i < 4; ++i) {
                var plus = (double[])j.Clone(); plus[i] += delta;
                var minus = (double[])j.Clone(); minus[i] -= delta;
                double finiteDifference = (DirectEnergy(plus, epsilon) - DirectEnergy(minus, epsilon)) / (2 * delta);
                Assert.True(Math.Abs(g[i] - finiteDifference) < 1e-6 * Math.Max(1, Math.Abs(g[i])));
            }
            double[] direction = { 0.2, -0.3, 0.7, 0.1 };
            double quadratic = 0;
            for (int i = 0; i < 4; ++i)
                for (int k = 0; k < 4; ++k) {
                    Assert.Equal(h[i, k], h[k, i], 10);
                    quadratic += direction[i] * h[i, k] * direction[k];
                }
            double det = a * d - b * c, detSlope = d * direction[0] - c * direction[1] - b * direction[2] + a * direction[3];
            double root = Math.Sqrt(det * det + epsilon * epsilon), chi = (det + root) / 2, chiPrime = (1 + det / root) / 2;
            Func<double, double> lifted = t => {
                double norm = 0;
                for (int i = 0; i < 4; ++i) norm += (j[i] + t * direction[i]) * (j[i] + t * direction[i]);
                double linearDet = det + t * detSlope;
                return (norm + linearDet * linearDet + 1) / (chi + chiPrime * t * detSlope);
            };
            double oracle = (lifted(1e-3) - 2 * lifted(0) + lifted(-1e-3)) / 1e-6;
            Assert.True(quadratic > 0);
            Assert.True(Math.Abs(quadratic - oracle) < 1e-4 * Math.Max(1, quadratic));
        }

        static double DirectEnergy(double[] a, double epsilon)
        {
            double det = a[0] * a[3] - a[1] * a[2];
            return (a.Sum(v => v * v) + det * det + 1) / ((det + Math.Sqrt(det * det + epsilon * epsilon)) / 2);
        }

        [Fact]
        public void AnnulusNearCollapsedBoundary_IsNotAssumedInfeasible()
        {
            var mesh = new DMesh3();
            for (int j = 0; j < 4; ++j)
                for (int i = 0; i < 8; ++i) {
                    double a = 2 * Math.PI * i / 8;
                    mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0.6 * j));
                }
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < 8; ++i) {
                    int a = 8 * j + i, b = 8 * j + (i + 1) % 8;
                    mesh.AppendTriangle(a, b, b + 8); mesh.AppendTriangle(a, b + 8, a + 8);
                }
            var lscm = new LSCMParameterization(mesh).Compute();
            Assert.Equal(ParameterizationStatus.FlippedTriangles, lscm.Status);
            var result = new FoldoverFreeParameterization(mesh, lscm.UV).Compute();
            Valid(mesh, lscm.UV, result);
            Assert.True(UVOverlap.Compute(mesh, result.UV).IsInjective);
        }

        [Fact]
        public void TorusCut_LocalSuccessDoesNotClaimGlobalInjectivity()
        {
            var cone = new ConeFlattening(UVAtlasTests.Torus()) { FeatureWeight = 1 };
            var start = cone.Compute();
            Assert.True(start.Succeeded, start.Error);
            Assert.True(UVOverlap.Compute(cone.CutMesh, start.UV).OverlappingPairs > 0);
            var r = new FoldoverFreeParameterization(cone.CutMesh, start.UV).Compute();
            Valid(cone.CutMesh, start.UV, r);
            Assert.False(UVOverlap.Compute(cone.CutMesh, r.UV).IsInjective);
        }

        [Fact]
        public void ClosedAndDisconnectedMeshes_AreNotSilentlyAccepted()
        {
            var closed = UVAtlasTests.Sphere(3);
            Assert.Equal(ParameterizationStatus.InvalidTopology,
                new FoldoverFreeParameterization(closed, new Vector2d[closed.MaxVertexID]).Compute().Status);
            var disconnected = Disc();
            int a = disconnected.AppendVertex(new Vector3d(2, 0, 0));
            int b = disconnected.AppendVertex(new Vector3d(3, 0, 0));
            int c = disconnected.AppendVertex(new Vector3d(2, 1, 0));
            disconnected.AppendTriangle(a, b, c);
            Assert.Equal(ParameterizationStatus.InvalidTopology,
                new FoldoverFreeParameterization(disconnected, new Vector2d[disconnected.MaxVertexID]).Compute().Status);
        }

        [Fact]
        public void StableRegularization_AndUnrepresentableOutput_AreExplicit()
        {
            var j = new[] { 1.0, 0, 0, -1.0 };
            double energy = FoldoverFreeParameterization.Element(j, 1e-100, null, null);
            Assert.True(MathUtil.IsFinite(energy) && energy > 0);
            Assert.True(double.IsPositiveInfinity(FoldoverFreeParameterization.Element(j, 1e-200, null, null)));
            var m = Disc(); var uv = Start(m);
            var tiny = Invariance.Transformed(m, new Similarity("tiny", 1e-200, Matrix3d.Identity, Vector3d.Zero));
            var r = new FoldoverFreeParameterization(tiny, uv).Compute();
            Assert.Equal(ParameterizationStatus.SolverFailed, r.Status);
            Assert.Contains("distortion", r.Error);
            var large = (Vector2d[])uv.Clone(); large[0] = new Vector2d(double.MaxValue, 0); large[1] = new Vector2d(-double.MaxValue, 0);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new FoldoverFreeParameterization(m, large).Compute().Status);
            var extreme = new FoldoverFreeParameterization(m, uv) { InitialEpsilon = 1e-200, MinimumEpsilon = 1e-200 }.Compute();
            Assert.Equal(ParameterizationStatus.SolverFailed, extreme.Status);
            Assert.False(extreme.Succeeded); Assert.False(extreme.Converged);
        }

        [Fact]
        public void EpsilonSchedule_UsesSameEpsilonEnergyRatioAndDeterminant()
        {
            Assert.Equal(0.9, FoldoverFreeParameterization.NextEpsilon(1, 0.5, 10, 10), 14);
            Assert.Equal(0.5, FoldoverFreeParameterization.NextEpsilon(1, 0.5, 10, 5), 14);
            double expected = 1 - 0.5 * Math.Sqrt(5) / (2 + Math.Sqrt(5));
            Assert.Equal(expected, FoldoverFreeParameterization.NextEpsilon(1, -2, 10, 5), 14);
        }
    }
}
