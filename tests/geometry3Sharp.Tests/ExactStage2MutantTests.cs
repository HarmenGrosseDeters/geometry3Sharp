using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests for the mutants that survived the incremental mutation run of the determinism proof stage 2 (PR #1370, run 45028348383) on
    /// the lines that PR changed: the hook of the libm perturbation, the salted hashes and the shard bits, the cull guard, the error
    /// bound of three planes, the effort-limit message and the lower-level values that only reach a bound.
    /// </summary>
    [Collection("AntipodalLibmPerturbation")]      // (the hook is a static: see ExactMeshBooleanTestsAntipodalLibmPerturbation)
    public class ExactStage2MutantTests
    {
        static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        // ---------------------------------------------------------------- libm_moved (ExactMeshBoolean.cs, the F2 hook)

        static double Moved(double v, int n, bool byLowBit)
        {
            MethodInfo m = typeof(ExactMeshBoolean).GetMethod("libm_moved", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(m);
            return (double)m.Invoke(null, new object[] { v, n, byLowBit });
        }

        static long Bits(double x) { return BitConverter.DoubleToInt64Bits(x); }

        [Fact]
        public void LibmMoved_MovesByUlpsInTheDirectionOfTheSign()
        {
            // "up" is towards +infinity: away from zero adds to the bit pattern of a positive value; for a negative value the magnitude shrinks
            Assert.Equal(Bits(1.0) + 1, Bits(Moved(1.0, 1, false)));
            Assert.Equal(Bits(1.0) - 1, Bits(Moved(1.0, -1, false)));
            Assert.Equal(Bits(1.0) + 5, Bits(Moved(1.0, 5, false)));
            Assert.Equal(Bits(-1.0) - 1, Bits(Moved(-1.0, 1, false)));
            Assert.Equal(Bits(-1.0) + 1, Bits(Moved(-1.0, -1, false)));
            Assert.True(Moved(-1.0, 1, false) > -1.0 && Moved(-1.0, -1, false) < -1.0);
            Assert.True(Moved(1.0, 1, false) > 1.0 && Moved(1.0, -1, false) < 1.0);
        }

        [Fact]
        public void LibmMoved_ByLowBit_FlipsTheDirectionOfAnOddBitPattern()
        {
            double odd = BitConverter.Int64BitsToDouble(Bits(1.5) | 1), even = BitConverter.Int64BitsToDouble(Bits(1.5) & ~1L);
            Assert.True(Moved(odd, 1, true) < odd);       // (the low bit set: the direction is reversed)
            Assert.True(Moved(even, 1, true) > even);
            Assert.True(Moved(odd, 1, false) > odd);      // (without the switch: as given)
            Assert.True(Moved(odd, -1, true) > odd);
            Assert.True(Moved(even, -1, true) < even);
        }

        [Fact]
        public void LibmMoved_Zero_BecomesASubnormalOfTheRightSign_AndExtremesStay()
        {
            Assert.Equal(3 * double.Epsilon, Moved(0.0, 3, false));
            Assert.Equal(-3 * double.Epsilon, Moved(0.0, -3, false));
            // (a value that the move would take through zero or to infinity stays)
            Assert.Equal(Bits(double.Epsilon), Bits(Moved(double.Epsilon, -1, false)));
            Assert.Equal(Bits(double.MaxValue), Bits(Moved(double.MaxValue, 1, false)));
            Assert.Equal(Bits(-double.Epsilon), Bits(Moved(-double.Epsilon, 1, false)));
            // (one ulp inside the range moves)
            Assert.Equal(Bits(double.MaxValue) - 1, Bits(Moved(double.MaxValue, -1, false)));
            Assert.Equal(Bits(double.Epsilon) + 1, Bits(Moved(double.Epsilon, 1, false)));
        }

        [Fact]
        public void LibmValue_IsTheIdentityWithoutThePerturbation_AndOnlyMovesTheMaskedCalls()
        {
            MethodInfo v = typeof(ExactMeshBoolean).GetMethod("libm_value", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(v);
            Func<double, int, double> call = (x, which) => (double)v.Invoke(null, new object[] { x, which });
            Assert.Equal(Bits(0.75), Bits(call(0.75, 1)));
            try {
                ExactMeshBoolean.LibmPerturbForTests = 1;
                ExactMeshBoolean.LibmPerturbMaskForTests = 2;
                Assert.Equal(Bits(0.75), Bits(call(0.75, 1)));            // (not in the mask)
                Assert.Equal(Bits(0.75) + 1, Bits(call(0.75, 2)));        // (in the mask)
                ExactMeshBoolean.LibmPerturbMaskForTests = 15;
                Assert.Equal(Bits(0.75) + 1, Bits(call(0.75, 8)));
                Assert.Equal(Bits(0.75) + 1, Bits(call(0.75, 1)));
            } finally {
                ExactMeshBoolean.LibmPerturbForTests = 0;
                ExactMeshBoolean.LibmPerturbMaskForTests = 15;
            }
        }
        // ---------------------------------------------------------------- the salted hash, HashedPoint (ExactMeshArrangement.cs)

        // the reference of ExactMeshArrangement.salted: a SplitMix-like mix of the hash with the salt (0: the hash itself)
        static int Salted(int h, int salt)
        {
            if (salt == 0)
                return h;
            ulong z = (uint)(h ^ salt) * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 29)) * 0xBF58476D1CE4E5B9UL;
            return (int)(z ^ (z >> 32));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0x68E31DA4)]
        [InlineData(-12345)]
        public void Salted_IsTheMixOfTheHash_AndTheIdentityForSaltZero(int salt)
        {
            MethodInfo m = typeof(ExactMeshArrangement).GetMethod("salted", Private, null, new[] { typeof(int), typeof(int) }, null);
            Assert.NotNull(m);
            foreach (int h in new[] { 0, 1, -1, 123456789, int.MinValue, int.MaxValue })
                Assert.Equal(Salted(h, salt), (int)m.Invoke(null, new object[] { h, salt }));
        }

        [Fact]
        public void IdComparer_HashesWithTheSalt_OfAnArrangement()
        {
            int Hash(int salt, ExactPoint3 p, out int id)
            {
                var mesh = new DMesh3();
                mesh.AppendVertex(new Vector3d(0, 0, 0)); mesh.AppendVertex(new Vector3d(1, 0, 0)); mesh.AppendVertex(new Vector3d(0, 1, 0));
                mesh.AppendTriangle(0, 1, 2);
                var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh }) { HashSalt = salt };
                arr.Compute();
                id = arr.AddPoint(p);
                return new ExactMeshArrangement.ExactIdComparer(arr).GetHashCode(id);
            }
            foreach (ExactPoint3 p in new[] { ExactPoint3.FromInteger(1, 2, 3), ExactPoint3.FromInteger(BigInteger.One << 300, 2, 3), ExactPoint3.Create(1, 2, 3, 7) }) {
                int raw = Hash(0, p, out _);
                Assert.Equal(Salted(raw, 0x68E31DA4), Hash(0x68E31DA4, p, out _));
                Assert.NotEqual(raw, Hash(0x68E31DA4, p, out _));
            }
        }

        [Fact]
        public void HashedPoint_HashesWithItsSalt_AndEqualsByValue()
        {
            Type t = typeof(ExactMeshArrangement).GetNestedType("HashedPoint", BindingFlags.NonPublic);
            Assert.NotNull(t);
            ExactPoint3 p = ExactPoint3.Create(5, -7, 9, 4), q = ExactPoint3.Create(5, -7, 9, 5);
            object a = Activator.CreateInstance(t, new object[] { p, 0x68E31DA4 }), b = Activator.CreateInstance(t, new object[] { p, 0x68E31DA4 }), c = Activator.CreateInstance(t, new object[] { q, 0x68E31DA4 });
            Assert.Equal(Salted(p.GetHashCode(), 0x68E31DA4), a.GetHashCode());
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a.GetHashCode(), c.GetHashCode());
            Assert.True(a.Equals(b));                  // (Equals(object))
            Assert.False(a.Equals(c));
            Assert.False(a.Equals("not a point"));
            object unsalted = Activator.CreateInstance(t, new object[] { p, 0 });
            Assert.Equal(p.GetHashCode(), unsalted.GetHashCode());
        }

        // ---------------------------------------------------------------- the cull guard (ExactMeshArrangement.cull)

        [Fact]
        public void Cull_RunsWhenBothCallbacksAreSet_OnAnIntersectionOfClosedMeshes()
        {
            DMesh3 Sphere(Vector3d c) { DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 12); MeshTransforms.Translate(m, c); return m; }
            var culled = new ExactMeshBoolean(Sphere(Vector3d.Zero), Sphere(new Vector3d(0.7, 0.3, 0.1)), Ops.Intersection);
            Assert.True(culled.Compute(), culled.Error);
            Assert.True(culled.Arrangement.CulledCount > 0, "no triangle was culled: " + culled.Arrangement.CulledCount);
            Assert.True(culled.Arrangement.CullTests > 0);
        }

        // ---------------------------------------------------------------- planes_approx (ExactMeshArrangement.cs: the bound of an intersection point)

        [Fact]
        public void PlanesApprox_ErrorBound_IsTheDocumentedFormula()
        {
            const double U = 1.1102230246251565e-16;          // (2^-53: the unit roundoff of the arrangement's U)
            FieldInfo uField = typeof(ExactMeshArrangement).GetField("U", Private | BindingFlags.Static | BindingFlags.Public);
            double u = uField != null ? (double)uField.GetValue(null) : U;
            double tiny = (double)(typeof(ExactMeshArrangement).GetField("Tiny", Private | BindingFlags.Static | BindingFlags.Public)?.GetValue(null) ?? 0.0);
            MethodInfo m = typeof(ExactMeshArrangement).GetMethod("planes_approx", Private, null,
                new[] { typeof(double[]), typeof(int), typeof(double[]), typeof(int), typeof(double[]), typeof(int), typeof(double).MakeByRefType() }, null);
            Assert.NotNull(m);
            var rand = new Random(895);
            int finite = 0;
            for (int k = 0; k < 200; ++k) {
                double scale = Math.Pow(10, rand.Next(-3, 9));
                double[] q1 = Plane(rand, scale), q2 = Plane(rand, scale), q3 = Plane(rand, scale);
                var args = new object[] { q1, 0, q2, 0, q3, 0, 0.0 };
                Vector3d x = (Vector3d)m.Invoke(null, args);
                double err = (double)args[6];
                // the same computation in the test
                double a1 = q1[0], b1 = q1[1], c1 = q1[2], d1 = q1[3], a2 = q2[0], b2 = q2[1], c2 = q2[2], d2 = q2[3], a3 = q3[0], b3 = q3[1], c3 = q3[2], d3 = q3[3];
                double c23x = b2 * c3 - c2 * b3, c23y = c2 * a3 - a2 * c3, c23z = a2 * b3 - b2 * a3;
                double c31x = b3 * c1 - c3 * b1, c31y = c3 * a1 - a3 * c1, c31z = a3 * b1 - b3 * a1;
                double c12x = b1 * c2 - c1 * b2, c12y = c1 * a2 - a1 * c2, c12z = a1 * b2 - b1 * a2;
                double p23x = Math.Abs(b2 * c3) + Math.Abs(c2 * b3), p23y = Math.Abs(c2 * a3) + Math.Abs(a2 * c3), p23z = Math.Abs(a2 * b3) + Math.Abs(b2 * a3);
                double p31x = Math.Abs(b3 * c1) + Math.Abs(c3 * b1), p31y = Math.Abs(c3 * a1) + Math.Abs(a3 * c1), p31z = Math.Abs(a3 * b1) + Math.Abs(b3 * a1);
                double p12x = Math.Abs(b1 * c2) + Math.Abs(c1 * b2), p12y = Math.Abs(c1 * a2) + Math.Abs(a1 * c2), p12z = Math.Abs(a1 * b2) + Math.Abs(b1 * a2);
                double det = a1 * c23x + b1 * c23y + c1 * c23z;
                double nx = d1 * c23x + d2 * c31x + d3 * c12x, ny = d1 * c23y + d2 * c31y + d3 * c12y, nz = d1 * c23z + d2 * c31z + d3 * c12z;
                double edet = 8 * u * (Math.Abs(a1) * p23x + Math.Abs(b1) * p23y + Math.Abs(c1) * p23z);
                double enx = 8 * u * (Math.Abs(d1) * p23x + Math.Abs(d2) * p31x + Math.Abs(d3) * p12x);
                double eny = 8 * u * (Math.Abs(d1) * p23y + Math.Abs(d2) * p31y + Math.Abs(d3) * p12y);
                double enz = 8 * u * (Math.Abs(d1) * p23z + Math.Abs(d2) * p31z + Math.Abs(d3) * p12z);
                Assert.Equal(nx / det, x.x); Assert.Equal(ny / det, x.y); Assert.Equal(nz / det, x.z);
                double ad = Math.Abs(det), expected = double.PositiveInfinity;
                if (ad > 2.02 * edet) {
                    double den = ad - 1.01 * edet;
                    double ex = (enx + Math.Abs(x.x) * edet) / den, ey = (eny + Math.Abs(x.y) * edet) / den, ez = (enz + Math.Abs(x.z) * edet) / den;
                    expected = 1.01 * Math.Max(ex, Math.Max(ey, ez)) + 2 * u * Math.Max(Math.Abs(x.x), Math.Max(Math.Abs(x.y), Math.Abs(x.z))) + tiny;
                    finite++;
                }
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(err));
            }
            Assert.True(finite > 150, finite + " finite bounds");
        }

        static double[] Plane(Random rand, double scale)
        {
            return new[] { rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1, (rand.NextDouble() * 2 - 1) * scale };
        }

        // ---------------------------------------------------------------- the message of the effort limit (ExactMeshBoolean.Compute)

        static DMesh3 NearlyCoincidentBoxes(int seed, int n)
        {
            var rnd = new Random(seed);
            DMesh3 m = new DMesh3();
            var c = new Vector3d(0.5, 0.5, 0.5);
            for (int i = 0; i < n; ++i) {
                rnd.NextDouble(); rnd.NextDouble(); rnd.NextDouble();
                DMesh3 p = new TrivialBox3Generator() { Box = new Box3d(c, new Vector3d(0.3, 0.2, 0.25)) }.Generate().MakeDMesh();
                var axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                MeshTransforms.Rotate(p, c, Quaterniond.AxisAngleD(axis, Math.Pow(10, -2 - 5 * rnd.NextDouble())));
                MeshTransforms.Translate(p, Math.Pow(10, -3 - 6 * rnd.NextDouble()) * axis);
                MeshEditor.Append(m, p);
            }
            return m;
        }

        [Fact]
        public void StrictRounding_AtTheEffortLimit_SaysSo_AndOtherwiseNot()
        {
            var limited = new ExactMeshBoolean(NearlyCoincidentBoxes(22, 9), new DMesh3(), Ops.Union) { StrictRounding = true, RoundingMaxPairTests = 100 };
            Assert.False(limited.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, limited.Status);
            Assert.Contains("; the repair stopped at its effort limit: the smallest rejected edit is the smallest of those proposed before it", limited.Error);
            Assert.DoesNotContain("the scale is not finite", limited.Error);
            Assert.DoesNotContain("outside the supported range", limited.Error);
            // (the rod of 1e300: the scale message, and no word of the effort limit)
            DMesh3 rod = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1e300, 1, 1)) }.Generate().MakeDMesh();
            var strict = new ExactMeshBoolean(rod, new DMesh3(), Ops.Union) { StrictRounding = true };
            Assert.False(strict.Compute());
            Assert.Contains("; the scale is not finite (a diagonal overflows), every edit rejected", strict.Error);
            Assert.DoesNotContain("effort limit", strict.Error);
        }

        // ---------------------------------------------------------------- the Antipodal tree: node radius and L1 size, and the bound fullBound

        static Vector3d[] GridChain(Random rand, int n, bool open)
        {
            Vector3d[] segs = ExactMeshBooleanTestsAntipodalTree.Chains(n, rand, open);
            for (int k = 0; k < segs.Length; ++k)
                segs[k] = new Vector3d(Math.Round(segs[k].x * 1048576), Math.Round(segs[k].y * 1048576), Math.Round(segs[k].z * 1048576));
            return segs;
        }

        [Fact]
        public void AntipodalTree_NodeRadiusAndL1_AreTheMaximaOverTheNodesPoints()
        {
            var rand = new Random(4731);
            int nodes = 0;
            for (int trial = 0; trial < 6; ++trial) {
                Vector3d[] g = GridChain(rand, 300 + rand.Next(500), trial % 3 == 2);
                var t = ExactMeshBoolean.AntipodalTree.Build(g);
                for (int k = 0; k < t.R.Length; ++k) {
                    int a = t.Lo[k], b = t.Hi[k];
                    var c = new Vector3d(t.Cx[k], t.Cy[k], t.Cz[k]);
                    Vector3d last = g[t.Seg[b - 1] + 1];
                    double r = 0, l1 = 0;
                    for (int q = a; q <= b; ++q) {
                        Vector3d v = q < b ? g[t.Seg[q]] : last;
                        r = Math.Max(r, (v - c).Length);
                        l1 = Math.Max(l1, Math.Abs(v.x) + Math.Abs(v.y) + Math.Abs(v.z));
                    }
                    // (the stored values are rounded up by a few ulps: the tree adds its own margin)
                    Assert.True(t.R[k] >= r && t.R[k] <= r * (1 + 1e-12) + 1e-300, $"node {k}: radius {t.R[k]:R} against {r:R}");
                    Assert.True(t.M1[k] >= l1 && t.M1[k] <= l1 * (1 + 1e-12) + 1e-300, $"node {k}: L1 {t.M1[k]:R} against {l1:R}");
                    nodes++;
                }
            }
            Assert.True(nodes > 100, nodes + " nodes");
        }

        // fullBound of the fixture of EveryCall_MovesFullBound, summed over the values that the tree gives: pinned to 1e-4 of itself (the libm
        // values inside it differ by an ulp between platforms and move it by 1e-6 at most), which fixes the arithmetic of hmin and of the errors
        [Fact]
        public void FullBound_SumOverAFixture_IsPinned()
        {
            var rand = new Random(767);
            double sum = 0; int n = 0;
            for (int trial = 0; trial < 12; ++trial) {
                Vector3d[] segs = GridChain(rand, 400 + rand.Next(1200), false);
                var tree = ExactMeshBoolean.AntipodalTree.Build(segs);
                for (int q = 0; q < 80; ++q) {
                    Vector3d p = new Vector3d(rand.NextDouble() * 6 - 3, rand.NextDouble() * 6 - 3, rand.NextDouble() * 2 - 1) * 1048576;
                    Vector3d x1 = ExactMeshBooleanTestsAntipodalTree.RandomUnit(rand);
                    double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
                    if (ExactMeshBoolean.antipodal_tree_value(p, pm, x1, 0, segs, tree, 0.25, 0, 0, out _, out _, out double fb) == 0 && !double.IsInfinity(fb)) {
                        sum += fb; n++;
                    }
                }
            }
            Assert.True(n > 300, n + " values");
            Assert.True(Math.Abs(sum - PinnedFullBoundSum) <= 1e-4 * PinnedFullBoundSum, $"sum {sum:R} ({n} values), pinned {PinnedFullBoundSum:R}");
        }

        const double PinnedFullBoundSum = 6.823513914684366E-05;

        // ---------------------------------------------------------------- Center.Local: the error of a centroid in the frame of its triangle

        [Fact]
        public void CenterLocal_Error_IsTheDocumentedFormula()
        {
            (DMesh3 a, DMesh3 b) = ExactMeshBooleanTestsLocalFilter.NearCopy(16, 1e-12);
            var s = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            Assert.True(s.Compute(), s.Error);
            var arr = s.Arrangement;
            int checkedPieces = 0, source = -1;
            ExactMeshBoolean.LocalFrame frame = null;
            for (int i = 0; i < arr.Output.Count; i += 7) {
                var piece = arr.Output[i];
                if (piece.Source != source) {
                    source = piece.Source;
                    frame = new ExactMeshBoolean.LocalFrame(s, arr.Triangles[source]);
                }
                var c = new ExactMeshBoolean.Center(s, piece, frame);
                Vector3d l = c.Local(out double le);
                Vector3d ca = frame.Corner(piece.A), cb = frame.Corner(piece.B), cc = frame.Corner(piece.C);
                double m = 0;
                for (int k = 0; k < 3; ++k)
                    m = Math.Max(m, Math.Abs(ca[k]) + Math.Abs(cb[k]) + Math.Abs(cc[k]));
                double expected = (ExactMeshBoolean.LocalFrame.PointError(ca) + ExactMeshBoolean.LocalFrame.PointError(cb) + ExactMeshBoolean.LocalFrame.PointError(cc)) / 3 * (1 + 1e-15) + 1.2e-16 * m;
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(le));
                Assert.Equal((ca + cb + cc) / 3, l);
                checkedPieces++;
            }
            Assert.True(checkedPieces > 200, checkedPieces + " pieces");
        }
    }
}
