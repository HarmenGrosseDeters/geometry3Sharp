using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1612: Math.Floor / Ceiling(Math.Log(x, 2)) as a binary exponent is off by one at exact powers of two (a platform libm value);
    /// MathUtil.FloorLog2 / CeilLog2 read the exponent bits instead.
    /// </summary>
    public class BinaryExponentTests
    {
        [Fact]
        public void FloorAndCeilLog2_AreExactForEveryPowerOfTwo_AlsoSubnormal()
        {
            for (int k = -1074; k <= 1023; ++k) {
                double p = Math.Pow(2, k);
                if (k == -1074) p = BitConverter.Int64BitsToDouble(1L);   // Math.Pow(2, -1074) is exact too, but spell the smallest subnormal by its bits
                Assert.Equal(k, MathUtil.FloorLog2(p));
                Assert.Equal(k, MathUtil.CeilLog2(p));
                // one ulp above: floor stays, ceil goes up (the largest finite double has no ulp above)
                if (k >= -1022 && k < 1023) {   // below 2^-1022 the ulp is 2^-1074, so the neighbour is another binade: covered by Subnormals_AllMantissaLengths
                    double up = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) + 1);
                    Assert.Equal(k, MathUtil.FloorLog2(up));
                    Assert.Equal(k + 1, MathUtil.CeilLog2(up));
                }
                // one ulp below (for 2^-1074 there is none)
                if (k >= -1022) {
                    double down = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p) - 1);
                    Assert.Equal(k - 1, MathUtil.FloorLog2(down));
                    Assert.Equal(k, MathUtil.CeilLog2(down));
                }
            }
        }

        [Fact]
        public void Subnormals_AllMantissaLengths()
        {
            for (int len = 1; len <= 52; ++len) {
                long m = (1L << len) - 1;   // all ones: not a power of two (except len 1)
                double x = BitConverter.Int64BitsToDouble(m);
                Assert.Equal(len - 1075, MathUtil.FloorLog2(x));
                Assert.Equal(len == 1 ? -1074 : len - 1074, MathUtil.CeilLog2(x));
            }
            Assert.Equal(1023, MathUtil.FloorLog2(double.MaxValue));
            Assert.Equal(1024, MathUtil.CeilLog2(double.MaxValue));
        }

        [Fact]
        public void LogOfPowerOfTwo_WasOffByOne_OnThisPlatformOrNot_TheHelperIsNot()
        {
            // the k that the issue lists for glibc: -59 -58 -55 -51 -47 -39 -31 -29 (floor) and 29 31 39 47 51 55 58 59 (ceiling)
            foreach (int k in new[] { -59, -58, -55, -51, -47, -39, -31, -29, 29, 31, 39, 47, 51, 55, 58, 59 }) {
                Assert.Equal(k, MathUtil.FloorLog2(Math.Pow(2, k)));
                Assert.Equal(k, MathUtil.CeilLog2(Math.Pow(2, k)));
            }
        }

        [Fact]
        public void InvalidInput_IsDefined()
        {
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                Assert.Throws<ArgumentOutOfRangeException>(() => MathUtil.FloorLog2(bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => MathUtil.CeilLog2(bad));
            }
            Assert.Equal(0, MathUtil.CeilLog2Saturating(double.NaN));
            Assert.Equal(int.MaxValue, MathUtil.CeilLog2Saturating(double.PositiveInfinity));
            Assert.Equal(int.MinValue, MathUtil.CeilLog2Saturating(0));
            Assert.Equal(int.MinValue, MathUtil.CeilLog2Saturating(-3));
            Assert.Equal(30, MathUtil.CeilLog2Saturating(Math.Pow(2, 30)));
            Assert.Equal(31, MathUtil.CeilLog2Saturating(Math.Pow(2, 30) + 1));
        }

        // ---- FoldoverFreeParameterization.PowerScale ----

        static ParameterizationResult Run(int k)
        {
            int n = 8; double s = Math.Pow(2, k);
            var mesh = new DMesh3();
            for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) mesh.AppendVertex(new Vector3d(i / (double)n, j / (double)n, 0));
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1; mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d); }
            var uv = new List<Vector2d>();
            for (int v = 0; v < mesh.VertexCount; v++) { var p = mesh.GetVertex(v); uv.Add(new Vector2d(p.x, p.y)); }
            int mid = 4 * (n + 1) + 4; var q = uv[mid];
            uv[mid] = new Vector2d(q.x + 0.3 / n, q.y + 0.45 / n);
            uv[mid + 1] = new Vector2d(uv[mid - 1].x - 0.2 / n, uv[mid + 1].y);
            for (int v = 0; v < uv.Count; v++) uv[v] = uv[v] * s;
            return new FoldoverFreeParameterization(mesh, uv).Compute();
        }

        [Theory]
        [InlineData(-29)]
        [InlineData(-31)]
        [InlineData(-39)]
        [InlineData(-59)]
        [InlineData(0)]
        [InlineData(29)]
        [InlineData(-3)]
        public void FoldoverFree_UvScaledByAPowerOfTwo_GivesTheScaledResultBitForBit(int k)
        {
            ParameterizationResult reference = Run(-28);
            Assert.True(reference.Succeeded);
            ParameterizationResult r = Run(k);
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(reference.Iterations, r.Iterations);
            Assert.Equal(reference.Energy, r.Energy);
            double f = Math.Pow(2, k + 28);   // result UVs are in the scaled frame
            Assert.Equal(reference.UV.Length, r.UV.Length);
            for (int i = 0; i < r.UV.Length; ++i) {
                Assert.Equal(reference.UV[i].x * f, r.UV[i].x);
                Assert.Equal(reference.UV[i].y * f, r.UV[i].y);
            }
        }

        [Fact]
        public void FoldoverFree_UnderSimilarities_ScalesGiveTheSameResultBitForBit()
        {
            // the UV scale of every exact similarity of the harness (2^k): same iterations and energy, UVs scaled
            ParameterizationResult reference = Run(0);
            foreach (Similarity s in Similarity.All()) {
                if (!s.IsExact) continue;
                int k = (int)Math.Round(Math.Log(s.Scale, 2));
                Assert.Equal(s.Scale, Math.Pow(2, k));
                ParameterizationResult r = Run(k);
                Assert.True(r.Succeeded, s.Name);
                Assert.Equal(reference.Iterations, r.Iterations);
                Assert.Equal(reference.Energy, r.Energy);
                for (int i = 0; i < r.UV.Length; ++i) {
                    Assert.Equal(reference.UV[i].x * s.Scale, r.UV[i].x);
                    Assert.Equal(reference.UV[i].y * s.Scale, r.UV[i].y);
                }
            }
        }

        // ---- TpmsLattice Enclosure: the dyadic level pad s = 2^ceil(log2 maxCoord) ----

        static TpmsLattice Enclosure(double scale)
        {
            // CellSize 2^k (the box is 2 cells per side), Divisions 4: the cube and every coordinate are dyadic, so the largest coordinate is
            // a power of two times a constant for every k
            return new TpmsLattice {
                Type = TpmsType.SchwarzP, Style = TpmsStyle.Sheet, VolumeFraction = 0.3, Enclosure = true, CellSize = scale, Divisions = 4,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * scale)
            };
        }

        [Fact]
        public void TpmsEnclosure_PowerOfTwoScales_AreAllAcceptedAndGiveTheScaledMesh()
        {
            // #1612 (S1): the same problem scaled by 2^k was refused with "coordinate / CellSize > 2^20" at the largest coordinates 2^29 and 2^31
            // (and accepted at 2^28, 2^30, 2^32), because Ceiling(Log(2^k, 2)) is k + 1 there. Scan every k that crosses those.
            var reference = Enclosure(1);
            reference.Compute();
            Assert.Equal(TpmsStatus.Ok, reference.Status);
            for (int k = -40; k <= 40; ++k) {
                var t = Enclosure(Math.Pow(2, k));
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok, "2^" + k + ": " + t.Status + ": " + t.Message);
                Assert.Equal(reference.Mesh.TriangleCount, t.Mesh.TriangleCount);
                Assert.Equal(reference.RealizedVolumeFraction, t.RealizedVolumeFraction);
                Assert.Equal(reference.IsoValueUsed * Math.Pow(2, k), t.IsoValueUsed);
            }
        }

        [Fact]
        public void TpmsEnclosure_UnderSimilarities()
        {
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var t = new TpmsLattice {
                    Type = TpmsType.SchwarzP, Style = TpmsStyle.Sheet, VolumeFraction = 0.3, Enclosure = true, CellSize = s.Scale, Divisions = 4,
                    Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * s.Scale),
                    Origin = s.Apply(Vector3d.Zero), Rotation = s.Rotation
                };
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok || t.Status == TpmsStatus.NotClean || t.Status == TpmsStatus.SelfIntersecting, s.Name + ": " + t.Status + ": " + t.Message);
                if (!s.IsExact) continue;   // the exact frames: scale a power of two, Origin and Rotation as in the harness
                Assert.Equal(TpmsStatus.Ok, t.Status);
                if (reference == null) { reference = t; continue; }
                Assert.Equal(reference.Mesh.TriangleCount, t.Mesh.TriangleCount);
                Assert.Equal(reference.RealizedVolumeFraction, t.RealizedVolumeFraction);
                Assert.Equal("", Invariance.Difference(reference.Mesh, Invariance.MappedBack(t.Mesh, s)));
            }
        }
    }
}
