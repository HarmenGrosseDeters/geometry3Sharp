using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;
using Fx = geometry3Sharp.Tests.ExactBooleanGradientTests;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// BooleanVolumeGradient (mesh_ops/BooleanVolumeGradient.cs) against the exact rational oracle of the research prototype
    /// (Harness/BooleanGradient.cs, itself checked against exact central differences in ExactBooleanGradientTests): every
    /// component within its stated bound, the bound tight, exact scaling under 2^k, reuse, determinism and the contact regions.
    /// </summary>
    public class BooleanVolumeGradientTests
    {
        readonly ITestOutputHelper output;
        public BooleanVolumeGradientTests(ITestOutputHelper output) { this.output = output; }

        static BooleanVolumeGradient Run(DMesh3 a, DMesh3 b, Ops op, out ExactMeshBoolean boolean)
        {
            boolean = new ExactMeshBoolean(a, b, op) { RetainClassification = true };
            Assert.True(boolean.Compute(), boolean.Error);
            var g = new BooleanVolumeGradient(boolean);
            Assert.True(g.Compute(), g.Error);
            return g;
        }

        static BooleanVolumeGradient Run(DMesh3 a, DMesh3 b, Ops op) => Run(a, b, op, out _);

        // |value - exact| <= bound, exactly
        static bool Within(double value, double bound, BigRational exact)
        {
            BigRational d = BigRational.Of(value) - exact;
            if (d.Sign < 0) d = new BigRational(-d.N, d.D);
            return (BigRational.Of(bound) - d).Sign >= 0;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string shape in new[] { "axis", "rotated", "edge" })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    yield return new object[] { shape, op };
            foreach (string shape in new[] { "face", "flush", "flush swapped" })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    yield return new object[] { shape, op };
        }

        static void Inputs(string shape, out DMesh3 a, out DMesh3 b)
        {
            switch (shape) {
                case "axis": a = Fx.Unit(); b = Fx.ShiftedBox(); break;
                case "rotated": a = Fx.Unit(); b = Fx.RotatedBox(); break;
                case "edge": a = Fx.Unit(); b = Fx.EdgeOnTop(); break;
                case "face": a = Fx.Unit(); b = BooleanFuzzShapes.Box(1, 0.25, 0.25, 2, 0.75, 0.75); break;
                case "flush": a = Fx.Unit(); b = BooleanFuzzShapes.Box(0.5, 0.25, 0.5, 1.5, 0.75, 1); break;
                default: a = BooleanFuzzShapes.Box(0.5, 0.25, 0.5, 1.5, 0.75, 1); b = Fx.Unit(); break;
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Gradient_IsWithinItsBound_OfTheExactOracle(string shape, Ops op)
        {
            Inputs(shape, out DMesh3 a, out DMesh3 b);
            BooleanVolumeGradient g = Run(a, b, op);
            BooleanGradient exact = BooleanGradient.Compute(a, b, op, area: false);
            Assert.True(Within(g.Volume, g.VolumeBound, exact.Volume), $"volume {g.Volume} +- {g.VolumeBound}");
            Assert.True(g.VolumeBound <= 1e-14 * Math.Max(1, Math.Abs(g.Volume)), $"volume bound {g.VolumeBound}");
            double worst = 0, largest = 0;
            for (int m = 0; m < 2; ++m) {
                DMesh3 mesh = m == 0 ? a : b;
                Assert.Equal(mesh.MaxVertexID, g.Gradient[m].Length);
                foreach (int v in mesh.VertexIndices()) {
                    BigRational[] x = exact.VolumeGradientAt(m, v);
                    for (int k = 0; k < 3; ++k) {
                        Assert.True(Within(g.Gradient[m][v][k], g.GradientBound[m][v][k], x[k]),
                            $"mesh {m} vertex {v} component {k}: {g.Gradient[m][v][k]:R} +- {g.GradientBound[m][v][k]:R}, exact {BooleanGradient.D(x[k]):R}");
                        worst = Math.Max(worst, g.GradientBound[m][v][k]);
                        largest = Math.Max(largest, Math.Abs(g.Gradient[m][v][k]));
                    }
                }
            }
            output.WriteLine($"{shape} {op}: volume {g.Volume:R} +- {g.VolumeBound:E2}, largest |dV/dx| {largest:R}, largest bound {worst:E2}, contacts {g.Contacts.Count}");
            // a useful bound: a few hundred ulps of the largest entry
            Assert.True(worst <= 1e-13 * Math.Max(1, largest), $"bound {worst}");
        }

        [Theory]
        [InlineData("rotated", Ops.Union)]
        [InlineData("flush", Ops.Intersection)]      // (vertices of no kept piece, a contact region)
        public void Similarities_PowersOfTwoScaleExactly_OthersWithinTheInputRounding(string shape, Ops op)
        {
            Inputs(shape, out DMesh3 a, out DMesh3 b);
            BooleanVolumeGradient g = Run(a, b, op);
            foreach (Similarity s in Similarity.All()) {
                BooleanVolumeGradient gs = Run(Invariance.Transformed(a, s), Invariance.Transformed(b, s), op);
                if (s.IsRotated == false)
                    Assert.Equal(g.Contacts.Count, gs.Contacts.Count);    // (a rotation rounds the faces out of their common plane)
                double s2 = s.Scale * s.Scale, worst = 0;
                for (int m = 0; m < 2; ++m)
                    for (int v = 0; v < g.Gradient[m].Length; ++v) {
                        if (s.IsExact) {
                            for (int k = 0; k < 3; ++k) {
                                Assert.True(g.Gradient[m][v][k] * s2 == gs.Gradient[m][v][k], $"{s.Name} mesh {m} vertex {v}: {g.Gradient[m][v][k]:R} {gs.Gradient[m][v][k]:R}");
                                Assert.True(g.GradientBound[m][v][k] * s2 == gs.GradientBound[m][v][k], $"{s.Name} mesh {m} vertex {v} bound: {g.GradientBound[m][v][k]:R} {gs.GradientBound[m][v][k]:R}");
                            }
                        } else {
                            Vector3d back = s.Rotation.Transpose() * gs.Gradient[m][v] / s2;
                            worst = Math.Max(worst, (back - g.Gradient[m][v]).Length);
                            // (the bound of the transformed input: finite and a few ulps of the entries)
                            Assert.True(gs.GradientBound[m][v].MaxAbs <= 1e-13 * s2, $"{s.Name}: bound {gs.GradientBound[m][v]}");
                        }
                    }
                if (s.IsExact) {
                    Assert.Equal(g.Volume * s2 * s.Scale, gs.Volume);
                    Assert.Equal(g.VolumeBound * s2 * s.Scale, gs.VolumeBound);
                }
                output.WriteLine($"{s.Name}: exact={s.IsExact}, worst |R^T g'/s^2 - g| = {worst:E3}");
                // (at a contact the rotated input, rounded out of the common plane, is off the kink: its gradient is that of a
                // nearby branch, so only the other similarities and the inputs without a contact are compared)
                if (s.IsRotated == false || g.Contacts.Count == 0)
                    Assert.True(worst < 1e-6, s.Name);
            }
        }

        static string Bits(BooleanVolumeGradient g)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(BitConverter.DoubleToInt64Bits(g.Volume)).Append(',').Append(BitConverter.DoubleToInt64Bits(g.VolumeBound));
            for (int m = 0; m < g.Gradient.Length; ++m)
                for (int v = 0; v < g.Gradient[m].Length; ++v)
                    for (int k = 0; k < 3; ++k)
                        sb.Append(',').Append(BitConverter.DoubleToInt64Bits(g.Gradient[m][v][k])).Append(':').Append(BitConverter.DoubleToInt64Bits(g.GradientBound[m][v][k]));
            foreach (var c in g.Contacts) {
                sb.Append('|').Append(string.Join(",", c.Meshes)).Append(';').Append(c.Sheets).Append(';').Append(BitConverter.DoubleToInt64Bits(c.Area));
                foreach (var alt in c.Alternatives)
                    foreach (var t in alt)
                        sb.Append(';').Append(t.Mesh).Append('/').Append(t.Vertex).Append('=').Append(BitConverter.DoubleToInt64Bits(t.Value.x))
                          .Append('/').Append(BitConverter.DoubleToInt64Bits(t.Value.y)).Append('/').Append(BitConverter.DoubleToInt64Bits(t.Value.z));
            }
            return sb.ToString();
        }

        [Fact]
        public void Reuse_ComputeTwiceOnOneInstance_GivesTheSameBits()
        {
            BooleanVolumeGradient g = Run(Fx.Unit(), BooleanFuzzShapes.Box(0.5, 0.25, 0.5, 1.5, 0.75, 1), Ops.Union, out ExactMeshBoolean b);
            string first = Bits(g);
            Assert.True(g.Compute(), g.Error);
            Assert.Equal(first, Bits(g));
            // and after the boolean is computed again
            Assert.True(b.Compute());
            Assert.True(g.Compute(), g.Error);
            Assert.Equal(first, Bits(g));
        }

        [Fact]
        public void Determinism_RepeatedAndSerialRuns_GiveTheSameBits()
        {
            foreach (string shape in new[] { "rotated", "flush" }) {
                Inputs(shape, out DMesh3 a, out DMesh3 b);
                string expected = Bits(Run(a, b, Ops.Difference));
                for (int run = 0; run < 3; ++run)
                    Assert.Equal(expected, Bits(Run(a, b, Ops.Difference)));
                gParallel.SerialOnThisThread = true;
                try {
                    Assert.Equal(expected, Bits(Run(a, b, Ops.Difference)));
                } finally {
                    gParallel.SerialOnThisThread = false;
                }
            }
        }

        [Fact]
        public void RetainClassification_ChangesNoResult()
        {
            foreach (string shape in new[] { "rotated", "face", "flush" })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                    Inputs(shape, out DMesh3 a, out DMesh3 b);
                    var plain = new ExactMeshBoolean(a, b, op);
                    var kept = new ExactMeshBoolean(a, b, op) { RetainClassification = true };
                    Assert.True(plain.Compute());
                    Assert.True(kept.Compute());
                    Assert.Equal("", Invariance.Difference(plain.Result, kept.Result));
                    Assert.Null(plain.CertificateKeep);
                    Assert.Null(plain.StacksPerPiece);
                    Assert.Null(plain.CertificateFront);
                    Assert.NotNull(kept.CertificateKeep);
                    Assert.NotNull(kept.StacksPerPiece);
                    // the winding numbers in front are kept exactly for the representatives with a coplanar stack
                    for (int i = 0; i < kept.StacksPerPiece.Length; ++i)
                        Assert.Equal(kept.StacksPerPiece[i] != null && kept.StacksPerPiece[i].All(t => t >= kept.Arrangement.Output[i].Source), kept.CertificateFront[i] != null);
                    Assert.Equal(shape != "rotated", kept.CertificateFront.Any(f => f != null));
                }
            // and an n-ary boolean
            var three = new[] { Fx.Unit(), BooleanFuzzShapes.Box(1, 0.25, 0.25, 2, 0.75, 0.75), Fx.RotatedBox() };
            var p3 = new ExactMeshBoolean(three, Ops.Union);
            var k3 = new ExactMeshBoolean(three, Ops.Union) { RetainClassification = true };
            Assert.True(p3.Compute());
            Assert.True(k3.Compute());
            Assert.Equal("", Invariance.Difference(p3.Result, k3.Result));
        }

        [Fact]
        public void Compute_Fails_WithoutRetention_ForAnOpenInput_AndForAFailedBoolean()
        {
            var plain = new ExactMeshBoolean(Fx.Unit(), Fx.ShiftedBox(), Ops.Union);
            Assert.True(plain.Compute());
            var g = new BooleanVolumeGradient(plain);
            Assert.False(g.Compute());
            Assert.Contains("RetainClassification", g.Error);
            Assert.Null(g.Gradient);

            var open = new ExactMeshBoolean(Fx.Unit(), BooleanFuzzShapes.Without(Fx.ShiftedBox(), 0), Ops.Union) { AllowOpenMeshes = true, RetainClassification = true };
            Assert.True(open.Compute(), open.Error);
            g = new BooleanVolumeGradient(open);
            Assert.False(g.Compute());
            Assert.Contains("not closed", g.Error);

            var notRun = new ExactMeshBoolean(Fx.Unit(), Fx.ShiftedBox(), Ops.Union) { RetainClassification = true };
            g = new BooleanVolumeGradient(notRun);
            Assert.False(g.Compute());
            Assert.Throws<ArgumentNullException>(() => new BooleanVolumeGradient(null));
        }

        static double Directional(Vector3d[][] grad, Vector3d[][] d)
        {
            double r = 0;
            for (int m = 0; m < d.Length; ++m)
                if (d[m] != null)
                    for (int v = 0; v < d[m].Length; ++v)
                        r += grad[m][v].Dot(d[m][v]);
            return r;
        }

        static double Directional(BooleanVolumeGradient.GradientTerm[] terms, Vector3d[][] d)
            => terms.Where(t => d[t.Mesh] != null).Sum(t => t.Value.Dot(d[t.Mesh][t.Vertex]));

        [Theory]
        // face contact of area 1/4 (B moves along +x, away from A): realized and other one-sided derivative
        [InlineData("face", Ops.Union, 0.25, 0.0)]
        [InlineData("face", Ops.Intersection, 0.0, -0.25)]
        [InlineData("face", Ops.Difference, 0.0, 0.25)]
        // flush tops (B's top moves up): A u B keeps A's copy (left derivative 1/4), B u A keeps B's (right derivative 1/2)
        [InlineData("flush", Ops.Union, 0.25, 0.5)]
        [InlineData("flush swapped", Ops.Union, 0.5, 0.25)]
        public void ContactRegion_GivesTheRealizedAndTheOtherOneSidedDerivative(string shape, Ops op, double realized, double other)
        {
            Inputs(shape, out DMesh3 a, out DMesh3 b);
            BooleanVolumeGradient g = Run(a, b, op);
            Vector3d[][] d;
            if (shape == "face")
                d = new[] { new Vector3d[a.MaxVertexID], Enumerable.Repeat(Vector3d.AxisX, b.MaxVertexID).ToArray() };
            else {
                DMesh3 moving = shape == "flush" ? b : a;     // B's box is the first mesh when swapped
                Vector3d[] top = moving.VertexIndices().Select(v => moving.GetVertex(v).z == 1 ? Vector3d.AxisZ : Vector3d.Zero).ToArray();
                d = shape == "flush" ? new[] { new Vector3d[a.MaxVertexID], top } : new[] { top, new Vector3d[b.MaxVertexID] };
            }
            Assert.Single(g.Contacts);
            BooleanVolumeGradient.ContactRegion c = g.Contacts[0];
            output.WriteLine($"{shape} {op}: meshes {string.Join(",", c.Meshes)} sheets {c.Sheets} area {c.Area} alternatives {c.Alternatives.Length}, realized {Directional(g.Gradient, d)}");
            Assert.Equal(new[] { 0, 1 }, c.Meshes);
            Assert.Equal(2, c.Sheets);
            Assert.Equal(0.25, c.Area, 12);
            Assert.Equal(realized, Directional(g.Gradient, d), 12);
            Assert.Single(c.Alternatives);
            Assert.All(c.Alternatives[0], t => Assert.True(t.Bound.MaxAbs <= 1e-14, $"bound {t.Bound}"));
            Assert.Equal(other, Directional(g.Gradient, d) + Directional(c.Alternatives[0], d), 12);
        }

        [Theory]
        [InlineData("axis")]
        [InlineData("rotated")]
        [InlineData("edge")]
        public void NoContactOfPositiveArea_NoRegion(string shape)
        {
            Inputs(shape, out DMesh3 a, out DMesh3 b);
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                Assert.Empty(Run(a, b, op).Contacts);
        }

        [Fact]
        public void TwoContacts_AreTwoRegions_InAFixedOrder()
        {
            // B touches A's +x face and C touches A's -y face: two regions; their alternatives touch disjoint vertices
            DMesh3 a = Fx.Unit(), b = BooleanFuzzShapes.Box(1, 0.25, 0.25, 2, 0.75, 0.75), c = BooleanFuzzShapes.Box(0.25, -1, 0.25, 0.75, 0, 0.75);
            var boolean = new ExactMeshBoolean(new[] { a, b, c }, Ops.Union) { RetainClassification = true };
            Assert.True(boolean.Compute(), boolean.Error);
            var g = new BooleanVolumeGradient(boolean);
            Assert.True(g.Compute(), g.Error);
            Assert.Equal(2, g.Contacts.Count);
            // (in the order of their first piece: A's triangles come first, its +x face before its -y face in Box's order)
            Assert.Equal(new[] { 0, 1 }, g.Contacts[0].Meshes);
            Assert.Equal(new[] { 0, 2 }, g.Contacts[1].Meshes);
            Assert.All(g.Contacts, r => Assert.Single(r.Alternatives));
            Assert.True(Math.Abs(g.Volume - 1.5) <= g.VolumeBound);
        }

        [Fact]
        public void Enclose_ContainsTheExactQuotient_AndIsExactForGridIntegers()
        {
            foreach (var (n, d, e) in new[] { (1, 3, 0), (-2, 7, -30), (5, 11, 40), (1, 10, -1060) }) {
                ExactInterval x = BooleanVolumeGradient.enclose(n, d, e);
                BigRational q = new BigRational(n, d) * BooleanGradient.Pow2(e);
                Assert.True((BigRational.Of(x.Lo) - q).Sign < 0 && (BigRational.Of(x.Hi) - q).Sign > 0, $"{n}/{d} 2^{e}");
            }
            ExactInterval i = BooleanVolumeGradient.enclose(12345, 1, -20);
            Assert.Equal(i.Lo, i.Hi);
            Assert.Equal(12345.0 / (1 << 20), i.Lo);
            Assert.Equal(0, BooleanVolumeGradient.enclose(0, 5, 3).Hi);
        }

        [Fact]
        public void Mid_BoundCoversBothEnds()
        {
            // [1, 1 + 2^-52]: the midpoint rounds to 1 (ties to even), so the bound must reach the upper end
            var x = new ExactInterval(1, ExactInterval.Up(1));
            double m = BooleanVolumeGradient.mid(x, out double b);
            // (checked exactly: m + b in double would round up to the end)
            Assert.True((BigRational.Of(m) - BigRational.Of(b) - BigRational.Of(x.Lo)).Sign <= 0 && (BigRational.Of(m) + BigRational.Of(b) - BigRational.Of(x.Hi)).Sign >= 0, $"{m:R} +- {b:R}");
            // [Down(1), 1]: the midpoint ties to Hi, so the bound must reach Lo; [-1e-300, 1]: m - Lo rounds down to 0.5, so
            // the bound needs its step up
            foreach (var y in new[] { new ExactInterval(ExactInterval.Down(1), 1), new ExactInterval(-1e-300, 1) }) {
                double my = BooleanVolumeGradient.mid(y, out double by);
                Assert.True((BigRational.Of(my) - BigRational.Of(by) - BigRational.Of(y.Lo)).Sign <= 0 && (BigRational.Of(my) + BigRational.Of(by) - BigRational.Of(y.Hi)).Sign >= 0, $"[{y.Lo:R}, {y.Hi:R}]: {my:R} +- {by:R}");
            }
            m = BooleanVolumeGradient.mid(new ExactInterval(double.NegativeInfinity, 1), out b);
            Assert.Equal(double.PositiveInfinity, b);
            m = BooleanVolumeGradient.mid(new ExactInterval(-3, -3), out b);
            Assert.Equal(-3, m);
            Assert.True(b >= 0 && b <= double.Epsilon);
        }

        [Theory]
        [InlineData(-300)]
        [InlineData(300)]
        public void ExtremeScales_FiniteBounds_AndExactScaling(int k)
        {
            Inputs("flush", out DMesh3 a, out DMesh3 b);
            BooleanVolumeGradient g = Run(a, b, Ops.Difference);
            double s = ExactPoint3.ScaleB(1, k), s2 = ExactPoint3.ScaleB(1, 2 * k);
            BooleanVolumeGradient gs = Run(BooleanFuzzShapes.Map(a, p => p * s), BooleanFuzzShapes.Map(b, p => p * s), Ops.Difference);
            for (int m = 0; m < 2; ++m)
                for (int v = 0; v < g.Gradient[m].Length; ++v) {
                    Assert.Equal(g.Gradient[m][v] * s2, gs.Gradient[m][v]);
                    Assert.Equal(g.GradientBound[m][v] * s2, gs.GradientBound[m][v]);
                    Assert.False(double.IsInfinity(gs.GradientBound[m][v].MaxAbs));
                }
            Assert.Equal(ExactPoint3.ScaleB(g.Volume, 3 * k), gs.Volume);
        }

        [Fact]
        public void WindingPredicate_GivesTheSameGradientAsTheOperation()
        {
            foreach (string shape in new[] { "rotated", "face", "flush" }) {
                Inputs(shape, out DMesh3 a, out DMesh3 b);
                string op = Bits(Run(a, b, Ops.Union));
                var bp = new ExactMeshBoolean(new[] { a, b }, w => w[0] > 0 || w[1] > 0) { RetainClassification = true };
                Assert.True(bp.Compute(), bp.Error);
                var g = new BooleanVolumeGradient(bp);
                Assert.True(g.Compute(), g.Error);
                Assert.Equal(op, Bits(g));
            }
        }

        [Fact]
        public void ThreeFlushTops_OneRegionOfThreeSheets_WithItsOtherBranches()
        {
            // three boxes whose tops lie in z = 1 and overlap in [0.5, 0.75] x [0.25, 0.75]
            DMesh3 a = Fx.Unit(), b = BooleanFuzzShapes.Box(0.5, 0.25, 0.5, 1.5, 0.75, 1), c = BooleanFuzzShapes.Box(-0.5, 0.25, 0.25, 0.75, 0.75, 1);
            var boolean = new ExactMeshBoolean(new[] { a, b, c }, Ops.Union) { RetainClassification = true };
            Assert.True(boolean.Compute(), boolean.Error);
            var g = new BooleanVolumeGradient(boolean);
            Assert.True(g.Compute(), g.Error);
            var three = g.Contacts.Where(r => r.Sheets == 3).ToList();
            Assert.Single(three);
            Assert.Equal(new[] { 0, 1, 2 }, three[0].Meshes);
            Assert.Equal(0.125, three[0].Area, 12);
            // the union keeps one copy: moving with A (realized), B or C: two other branches
            Assert.Equal(2, three[0].Alternatives.Length);
            // (inclusion-exclusion: 1 + 1/4 + 15/32 - 1/8 - 9/32 - 1/16 + 1/16)
            Assert.True(Math.Abs(g.Volume - 1.3125) <= g.VolumeBound, $"{g.Volume:R} +- {g.VolumeBound:R}");
        }

        static bool Contains(ExactInterval x, BigRational v) => (BigRational.Of(x.Lo) - v).Sign <= 0 && (BigRational.Of(x.Hi) - v).Sign >= 0;

        [Fact]
        public void ExactZeroShortcuts_AreExactOnlyWhereTheIdentityHolds()
        {
            // x - x = 0 only for one point; [1, 2] - [1, 3] holds -2 and 1
            var d = BooleanVolumeGradient.sub(new ExactInterval(1, 2), new ExactInterval(1, 3));
            Assert.True(Contains(d, BigRational.Of(-2)) && Contains(d, BigRational.Of(1)), $"[{d.Lo}, {d.Hi}]");
            var z = BooleanVolumeGradient.sub(ExactInterval.Of(0.1), ExactInterval.Of(0.1));
            Assert.True(z.Lo == 0 && z.Hi == 0);
            Assert.True(BooleanVolumeGradient.mul(ExactInterval.Of(0), new ExactInterval(-1e300, 1e300)).Hi == 0);
            // 0 / b is 0 only for a b without 0
            Assert.True(double.IsInfinity(BooleanVolumeGradient.div(ExactInterval.Of(0), new ExactInterval(-1, 1)).Hi));
            Assert.True(BooleanVolumeGradient.div(ExactInterval.Of(0), new ExactInterval(1, 2)).Hi == 0);
        }

        [Fact]
        public void Scaled_CoversTheIntervalAlsoWhereTheScalingUnderflows()
        {
            foreach (int k in new[] { 0, -1000, -1060, -1074, 40 }) {
                var x = new ExactInterval(3, ExactInterval.Up(3));
                double m = BooleanVolumeGradient.scaled(x, k, out double b);
                BigRational lo = BigRational.Of(x.Lo) * BooleanGradient.Pow2(k), hi = BigRational.Of(x.Hi) * BooleanGradient.Pow2(k);
                BigRational M = BigRational.Of(m), B = BigRational.Of(b);
                Assert.True((M - B - lo).Sign <= 0 && (M + B - hi).Sign >= 0, $"k={k}: {m:R} +- {b:R}");
            }
        }

        [Theory]
        [InlineData(-200)]
        [InlineData(-400)]
        public void InputsOfVeryDifferentMagnitudes_StayWithinFiniteRelativeBounds_OfTheOracle(int k)
        {
            // a unit box and a shifted box, and a tiny box at [-3t, -t]^3 in the second mesh (disjoint components)
            double t = ExactPoint3.ScaleB(1, k);
            DMesh3 a = Fx.Unit(), b = BooleanFuzzShapes.Join(Fx.ShiftedBox(), BooleanFuzzShapes.Box(-3 * t, -3 * t, -3 * t, -t, -t, -t));
            BooleanVolumeGradient g = Run(a, b, Ops.Union);
            BooleanGradient exact = BooleanGradient.Compute(a, b, Ops.Union, area: false);
            Assert.True(Within(g.Volume, g.VolumeBound, exact.Volume), $"volume {g.Volume:R} +- {g.VolumeBound:R}");
            Assert.True(g.VolumeBound <= 1e-14 * Math.Abs(g.Volume), $"volume bound {g.VolumeBound:R} of {g.Volume:R}");
            for (int m = 0; m < 2; ++m) {
                DMesh3 mesh = m == 0 ? a : b;
                foreach (int v in mesh.VertexIndices()) {
                    BigRational[] x = exact.VolumeGradientAt(m, v);
                    // the entries of a vertex have its box's magnitude: t^2 for the tiny box, about 1 for the others
                    double size = mesh.GetVertex(v).x < 0 ? t * t : 1;     // (only the tiny box has negative coordinates)
                    for (int c = 0; c < 3; ++c) {
                        Assert.True(Within(g.Gradient[m][v][c], g.GradientBound[m][v][c], x[c]), $"mesh {m} vertex {v} component {c}");
                        Assert.True(g.GradientBound[m][v][c] <= 1e-13 * size, $"mesh {m} vertex {v}: bound {g.GradientBound[m][v][c]:R}, size {size:R}");
                    }
                }
            }
        }

        [Fact]
        public void GradientBeyondTheDoubleRange_ComputeFails()
        {
            // entries of about (1e300)^2: not a double
            DMesh3 a = BooleanFuzzShapes.Map(Fx.Unit(), p => p * 1e300), b = BooleanFuzzShapes.Map(Fx.ShiftedBox(), p => p * 1e300);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union) { RetainClassification = true };
            Assert.True(boolean.Compute(), boolean.Error);
            var g = new BooleanVolumeGradient(boolean);
            Assert.False(g.Compute());
            Assert.Contains("double range", g.Error);
            // scaled by 2^400: the volume, about 2^1200, overflows while the gradient, about 2^800, is a double
            double s = ExactPoint3.ScaleB(1, 400);
            var big = new ExactMeshBoolean(BooleanFuzzShapes.Map(Fx.Unit(), p => p * s), BooleanFuzzShapes.Map(Fx.ShiftedBox(), p => p * s), Ops.Union) { RetainClassification = true };
            Assert.True(big.Compute(), big.Error);
            g = new BooleanVolumeGradient(big);
            Assert.False(g.Compute());
            Assert.Contains("double range", g.Error);
            Assert.True(double.IsInfinity(g.VolumeBound));
            Assert.All(g.Gradient, row => Assert.All(row, v => Assert.False(double.IsInfinity(v.MaxAbs))));
        }

        [Fact]
        public void Subnormals_MidOfAPointIsThePoint_ScaleWidensWhereItRounds()
        {
            // 0.5 eps + 0.5 eps rounds to 0: the point branch returns the point itself
            Assert.Equal(double.Epsilon, BooleanVolumeGradient.mid(ExactInterval.Of(double.Epsilon), out double b));
            Assert.Equal(0, b);
            // [3, Up(3)] 2^-1075 is not a multiple of 2^-1074 at its lower end: the scaled interval must still hold the exact ends
            var x = new ExactInterval(3, ExactInterval.Up(3));
            foreach (int k in new[] { -1075, -1076, -1060, 10 }) {
                ExactInterval y = BooleanVolumeGradient.scale(x, k);
                Assert.True(Contains(y, BigRational.Of(x.Lo) * BooleanGradient.Pow2(k)) && Contains(y, BigRational.Of(x.Hi) * BooleanGradient.Pow2(k)), $"k={k}: [{y.Lo:R}, {y.Hi:R}]");
            }
            Assert.Equal(new ExactInterval(6, 2 * ExactInterval.Up(3)).Hi, BooleanVolumeGradient.scale(x, 1).Hi);    // (exact where it can be)
        }
    }
}
