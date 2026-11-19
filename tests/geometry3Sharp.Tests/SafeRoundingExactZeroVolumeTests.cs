using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1191: the numerically-zero-volume rule (#1067, 2 |V| below ulp(largest coordinate) * area) decided exactly where the
    /// floating-point margin could not. The rule is exact for the computed area: |S| &lt; 3 t, S = 6 V exact, t = ulp(L) * A_fp.
    /// </summary>
    public class SafeRoundingExactZeroVolumeTests
    {
        readonly ITestOutputHelper output;
        public SafeRoundingExactZeroVolumeTests(ITestOutputHelper output) { this.output = output; }

        static double Ulp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;

        // value * 2^1074 as an integer (exact for every finite double)
        static BigInteger Scaled(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int e = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (e != 0) frac |= 1L << 52;
            int exp = e == 0 ? -1074 : e - 1075;
            BigInteger m = new BigInteger(bits < 0 ? -frac : frac);
            return m << (exp + 1074);
        }

        // the test's own oracle: S * 2^(3 * 1074), independent of ExactPredicates
        static BigInteger ExactS(DMesh3 mesh, List<int> comp)
        {
            Vector3d o3 = mesh.GetVertex(mesh.GetTriangle(comp[0]).a);
            BigInteger[] o = { Scaled(o3.x), Scaled(o3.y), Scaled(o3.z) };
            BigInteger[] P(int v) { Vector3d p = mesh.GetVertex(v); return new[] { Scaled(p.x) - o[0], Scaled(p.y) - o[1], Scaled(p.z) - o[2] }; }
            BigInteger total = 0;
            foreach (int t in comp) {
                Index3i tv = mesh.GetTriangle(t);
                BigInteger[] a = P(tv.a), b = P(tv.b), c = P(tv.c);
                total += c[0] * (a[1] * b[2] - a[2] * b[1]) + c[1] * (a[2] * b[0] - a[0] * b[2]) + c[2] * (a[0] * b[1] - a[1] * b[0]);
            }
            return total;
        }

        // |S| / (3 t) of the only component, with the library's A_fp and L (< 1: removed)
        static double Ratio(DMesh3 mesh, out double t)
        {
            List<int> comp = MeshRoundingRepair.connected_components(mesh).First().ToList();
            double area = 0, largest = 0;
            foreach (int id in comp) {
                Index3i tv = mesh.GetTriangle(id);
                Vector3d a = mesh.GetVertex(tv.a);
                area += 0.5 * (mesh.GetVertex(tv.b) - a).Cross(mesh.GetVertex(tv.c) - a).Length;
                largest = Math.Max(largest, Math.Max(a.MaxAbs, Math.Max(mesh.GetVertex(tv.b).MaxAbs, mesh.GetVertex(tv.c).MaxAbs)));
            }
            t = Ulp(largest) * area;
            // (3 t exactly: t * 2^1074 as an integer, times 3, at the scale 2^(3 * 1074) of S: times 2^(2 * 1074))
            BigInteger rhs = 3 * Scaled(t) << (2 * 1074);
            BigInteger lhs = BigInteger.Abs(ExactS(mesh, comp));
            return Math.Exp((double)BigInteger.Log(lhs == 0 ? 1 : lhs) - (double)BigInteger.Log(rhs));
        }

        // a closed double cone over a 17-gon of radius r with apexes at +-h: thickness 2 |V| / area = about h * c
        static DMesh3 Sheet(Vector3d at, double r, double h, bool inverted, int n = 17) => SafeRoundingInsideOutResidueTests.Bipyramid(n, r, h, at, inverted);

        // the apex height whose thickness is f ulps of the largest coordinate (formulas of the double cone, before rounding)
        static double Height(Vector3d at, double r, double f, int n = 17)
        {
            double ulp = Ulp(Math.Max(Math.Abs(at.x) + r, Math.Max(Math.Abs(at.y) + r, Math.Abs(at.z))));
            double h0 = 1e-15;
            double area = 2 * n * 0.5 * (2 * r * Math.Sin(Math.PI / n)) * Math.Sqrt(h0 * h0 + Math.Pow(r * Math.Cos(Math.PI / n), 2));
            double vol = 2.0 / 3 * (0.5 * n * r * r * Math.Sin(2 * Math.PI / n)) * h0;
            return h0 * f * ulp / (2 * vol / area);
        }

        // the solid (a sphere with needles, so that the repair edits and looks at the components) with the sheets
        static DMesh3 Input(params DMesh3[] sheets)
        {
            DMesh3 m = SafeRoundingInsideOutResidueTests.SphereWithNeedlesAndResidue(new DMesh3());
            foreach (DMesh3 s in sheets)
                new MeshEditor(m).AppendMesh(s);
            return m;
        }

        static (DMesh3 Mesh, MeshRoundingRepair Repair) Repaired(DMesh3 m)
        {
            var copy = new DMesh3(m);
            var r = new MeshRoundingRepair(copy);
            r.Apply();
            return (copy, r);
        }

        static readonly Vector3d Big = new Vector3d(5, 0, 0), Small = new Vector3d(0.3, 0, 0);

        // the arguments of numerically_zero_volume for the only component (the computed volume and norms are left out: 0)
        static bool Zero(DMesh3 m, double volume = 0, double tripleNorms = 0)
        {
            List<int> comp = MeshRoundingRepair.connected_components(m).First().ToList();
            double area = 0, largest = 0;
            foreach (int id in comp) {
                Index3i tv = m.GetTriangle(id);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                area += 0.5 * (b - a).Cross(c - a).Length;
                largest = Math.Max(largest, Math.Max(a.MaxAbs, Math.Max(b.MaxAbs, c.MaxAbs)));
            }
            return MeshRoundingRepair.numerically_zero_volume(m, comp, volume, area, largest, tripleNorms);
        }

        /// <summary>
        /// sheets in the band that the floating-point margin cannot decide (the error of the triple products is as large as the
        /// bound: unit sheet at x = 5, coordinates up to 6; and the small p0008 kind at 0.3): the exact volume decides, on both sides
        /// of the threshold and close to it. The test's own oracle (BigInteger, the library's area) agrees with the result
        /// </summary>
        [Theory]
        [InlineData(5.0, 1.0, 0.3, true)]
        [InlineData(5.0, 1.0, 0.5, true)]
        [InlineData(5.0, 1.0, 0.9, true)]
        [InlineData(5.0, 1.0, 0.99, true)]
        [InlineData(5.0, 1.0, 1.01, false)]
        [InlineData(5.0, 1.0, 1.1, false)]
        [InlineData(5.0, 1.0, 3.0, false)]
        [InlineData(0.3, 0.0005, 0.99, true)]
        [InlineData(0.3, 0.0005, 1.01, false)]
        public void UndecidedBand_IsDecidedExactly(double x, double r, double f, bool removed)
        {
            foreach (bool inverted in new[] { true, false }) {
                Vector3d at = new Vector3d(x, 0, 0);
                DMesh3 sheet = Sheet(at, r, Height(at, r, f), inverted);
                Assert.Equal(removed, Ratio(sheet, out _) < 1);
                Assert.Equal(removed, Zero(sheet));
                var (m, rep) = Repaired(Input(sheet));
                Assert.Equal(removed ? 1 : 0, rep.RemovedComponents);
                Assert.Equal(removed ? 34 : 0, rep.RemovedTriangles);
            }
        }

        /// <summary>
        /// the many-triangle flat sheet near the origin (2000 triangles, coordinates up to 0.45: the case of the old margin test, where
        /// the terms cancel to far below their size): decided exactly on both sides of the threshold, 0.9 and 1.1 ulp thick
        /// </summary>
        [Theory]
        [InlineData(0.9, true)]
        [InlineData(0.99, true)]
        [InlineData(1.01, false)]
        [InlineData(1.1, false)]
        public void ManyTrianglesNearTheOrigin_AreDecidedExactly(double f, bool removed)
        {
            Vector3d at = new Vector3d(0.25, 0, 0);
            DMesh3 sheet = Sheet(at, 0.2, Height(at, 0.2, f, 1000), true, 1000);
            Assert.Equal(2000, sheet.TriangleCount);
            Assert.Equal(removed, Ratio(sheet, out _) < 1);
            Assert.Equal(removed, Zero(sheet));
        }

        /// <summary>
        /// the keep shortcut (a component whose computed volume is far above the threshold is kept without the exact test) and its
        /// margin (8 times (n + 16) u sum |pa-o||pb-o||pc-o| is allowed for before it keeps); the exact test removes this sheet
        /// </summary>
        [Fact]
        public void KeepShortcut_DoesNotRemove_AndTheExactTestDoes()
        {
            DMesh3 sheet = Sheet(Big, 1, Height(Big, 1, 0.5), true);
            Assert.True(Zero(sheet));
            Assert.True(Ratio(sheet, out double t) < 1);
            // (a computed volume V with 2 V = 2 t keeps; 2 V = 0.75 t goes on to the exact test; 1.2 t keeps)
            Assert.False(Zero(sheet, volume: t));
            Assert.False(Zero(sheet, volume: 0.6 * t));
            Assert.True(Zero(sheet, volume: 0.375 * t));
            Assert.True(Zero(sheet, volume: -0.375 * t));
            Assert.False(Zero(sheet, volume: -t));
            // the margin: tripleNorms with 8 * (n + 16) u * norms = 0.75 t lowers 2 (V - 0.75 t) below t: the exact test runs and removes
            int n = MeshRoundingRepair.connected_components(sheet).First().Count;
            double norms = 0.75 * t / (8 * (n + 16) * 1.1102230246251565e-16);
            Assert.True(Zero(sheet, volume: t, tripleNorms: norms));
            Assert.False(Zero(sheet, volume: t, tripleNorms: norms / 4));
            Assert.False(Zero(sheet, volume: 1, tripleNorms: 1));
            Assert.False(Zero(sheet, volume: double.NaN));
        }

        /// <summary> the decision at |S| = 3 t exactly is not "below": a tetrahedron with |S| = 27 and t = ulp(2^52) * 9 = 9 stays, 9 + ulp removes </summary>
        [Fact]
        public void EqualToTheThreshold_Stays()
        {
            var (xyz, c) = Tetra(0, 0, 0);
            var m = new DMesh3();
            for (int i = 0; i < 4; ++i) m.AppendVertex(new Vector3d(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]));
            for (int i = 0; i < 4; ++i) m.AppendTriangle(c[3 * i], c[3 * i + 1], c[3 * i + 2]);
            List<int> comp = MeshRoundingRepair.connected_components(m).First().ToList();
            double L = 4503599627370496.0;     // 2^52: ulp 1
            Assert.False(MeshRoundingRepair.numerically_zero_volume(m, comp, 0, 9, L, 0));
            Assert.True(MeshRoundingRepair.numerically_zero_volume(m, comp, 0, Next(9, 1), L, 0));
            Assert.False(MeshRoundingRepair.numerically_zero_volume(m, comp, 0, Next(9, -1), L, 0));
        }

        // a closed tetrahedron with legs 3 (S = 27, area-free: the test passes t directly), at an offset
        static (double[] xyz, int[] corners) Tetra(double ox, double oy, double oz)
        {
            double[] xyz = { ox, oy, oz, ox + 3, oy, oz, ox, oy + 3, oz, ox, oy, oz + 3 };
            int[] corners = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            return (xyz, corners);
        }

        static double Next(double x, int k) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + k);

        /// <summary> |S| = 27 against 3 t: equal at t = 9, below at the next double up, above at the next down; the filter cannot decide the equal case </summary>
        [Fact]
        public void Compare_EqualAndNeighbours()
        {
            var (xyz, c) = Tetra(0, 0, 0);
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, c, 9, out int s));
            Assert.Equal(0, s);
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, c, Next(9, 1), out s));
            Assert.Equal(-1, s);
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, c, Next(9, -1), out s));
            Assert.Equal(1, s);
            // the filter decides far from the threshold, on both sides
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, c, 1, out s));
            Assert.Equal(1, s);
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, c, 100, out s));
            Assert.Equal(-1, s);
            // the orientation does not matter
            int[] flipped = { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2 };
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, flipped, 9, out s));
            Assert.Equal(0, s);
        }

        /// <summary> nothing is decided for a coordinate that is not finite, t that is not a positive normal number, a sum that overflows, or no triangles </summary>
        [Fact]
        public void Compare_DecidesNothing_ForUnusableInput()
        {
            var (xyz, c) = Tetra(0, 0, 0);
            int s;
            foreach (double t in new[] { 0, -9, double.NaN, double.PositiveInfinity, double.Epsilon, ExactPredicates.MinNormal / 2, -double.MaxValue })
                Assert.False(ExactPredicates.TryCompareAbsVolume6(xyz, c, t, out s), "t = " + t);
            // the smallest normal t is usable
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, c, ExactPredicates.MinNormal, out s));
            Assert.Equal(1, s);
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                double[] x2 = (double[])xyz.Clone();
                x2[4] = bad;
                Assert.False(ExactPredicates.TryCompareAbsVolume6(x2, c, 9, out s));
            }
            // S_fp overflows: coordinates of 1e200 (the determinants of about 1e600)
            var (huge, hc) = Tetra(0, 0, 0);
            for (int i = 0; i < huge.Length; ++i) huge[i] *= 1e200 / 3;
            Assert.False(ExactPredicates.TryCompareAbsVolume6(huge, hc, 9, out s));
            Assert.False(ExactPredicates.TryCompareAbsVolume6(xyz, new int[0], 9, out s));
            Assert.False(ExactPredicates.TryCompareAbsVolume6(xyz, null, 9, out s));
            // (and numerically_zero_volume keeps: a sheet with area 0 has t = 0)
            Assert.False(MeshRoundingRepair.numerically_zero_volume(new DMesh3(), new List<int>(), 0, 0, 1, 0));
        }

        /// <summary>
        /// randomised against the oracle: closed cones with jittered rim at offsets where the differences round, t within a few ulps
        /// of |S| / 3 (the exact path) and far from it (the filter); the sign is that of the BigInteger comparison, 200 cases
        /// </summary>
        [Fact]
        public void Compare_AgreesWithTheOracle()
        {
            var rnd = new Random(1191);
            int equal = 0, below = 0, above = 0;
            for (int k = 0; k < 200; ++k) {
                Vector3d at = new Vector3d(rnd.NextDouble() * 8 - 4, rnd.NextDouble(), rnd.NextDouble());
                double r = 0.001 + rnd.NextDouble();
                DMesh3 m = Sheet(at, r, r * Math.Pow(2, -rnd.Next(1, 60)) * (0.5 + rnd.NextDouble()), rnd.Next(2) == 0);
                for (int v = 0; v < m.MaxVertexID; ++v) {
                    Vector3d p = m.GetVertex(v);
                    m.SetVertex(v, new Vector3d(p.x * (1 + 1e-9 * rnd.NextDouble()), p.y, p.z));
                }
                List<int> comp = MeshRoundingRepair.connected_components(m).First().ToList();
                BigInteger absS = BigInteger.Abs(ExactS(m, comp));
                // (the centre of the window: t0 = |S| / 3 as a double, S at the scale 2^(3 * 1074); then neighbours in ulps, and far values)
                double t0 = (double)(absS >> (3 * 1074 - 60)) / 3 / 1152921504606846976.0;
                if (t0 < ExactPredicates.MinNormal)
                    continue;
                var xyz = new List<double>();
                var corners = new List<int>();
                var index = new Dictionary<int, int>();
                foreach (int id in comp) {
                    Index3i tv = m.GetTriangle(id);
                    for (int q = 0; q < 3; ++q) {
                        if (!index.TryGetValue(tv[q], out int ix)) { index[tv[q]] = ix = xyz.Count / 3; Vector3d p = m.GetVertex(tv[q]); xyz.Add(p.x); xyz.Add(p.y); xyz.Add(p.z); }
                        corners.Add(ix);
                    }
                }
                foreach (double t in new[] { t0, Next(t0, 1), Next(t0, -1), Next(t0, 7), Next(t0, -7), t0 * 1.001, t0 * 0.999, t0 * 2, t0 / 2 }) {
                    // (3 t exactly against |S| 2^(3 * 1074); |S| here is at the scale 2^(3 * 1074))
                    BigInteger rhs = 3 * Scaled(t) << (2 * 1074);
                    int expected = absS.CompareTo(rhs);
                    Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz.ToArray(), corners.ToArray(), t, out int got), $"case {k} t={t:R} t0={t0:R}");
                    Assert.Equal(expected, got);
                    if (expected == 0) ++equal; else if (expected < 0) ++below; else ++above;
                }
            }
            Assert.True(below > 100 && above > 100, $"{below} below, {above} above, {equal} equal");
        }

        /// <summary>
        /// invariance over Similarity.All(): at the 2^k scales (S scales by 2^3k, ulp(L) * A_fp exactly the same) the decision is the
        /// same for each sheet and the repair result is the same bit for bit; in the other frames the coordinates are rounded (the sheet
        /// is a different one) and the result is a closed, finite solid
        /// </summary>
        [Fact]
        public void Removal_Invariance()
        {
            DMesh3 input = Input(Sheet(Big, 1, Height(Big, 1, 0.5), true), Sheet(-Big, 1, Height(-Big, 1, 0.99), false),
                Sheet(new Vector3d(0, 5, 0), 1, Height(new Vector3d(0, 5, 0), 1, 1.01), false));
            var (m0, r0) = Repaired(input);
            Assert.Equal(2, r0.RemovedComponents);
            Assert.Equal(68, r0.RemovedTriangles);
            Assert.True(m0.IsClosed());
            foreach (Similarity s in Similarity.All()) {
                DMesh3 t = Invariance.Transformed(input, s);
                var (m, r) = Repaired(t);
                if (s.IsExact) {
                    Assert.True(r.RemovedComponents == 2 && r.RemovedTriangles == 68, s.Name);
                    Assert.Equal(m0.TriangleCount, m.TriangleCount);
                    for (int v = 0; v < m0.MaxVertexID; ++v)
                        if (m0.IsVertex(v))
                            Assert.Equal(m0.GetVertex(v) * s.Scale, m.GetVertex(v));
                }
                Assert.True(Invariance.AllFinite(m), s.Name);
                Assert.True(m.IsClosed(), s.Name);
            }
        }

        /// <summary> the decision itself, bit for bit over the 2^k scales, for sheets on both sides (the same answer at every power of two, also where the largest coordinate changes binade) </summary>
        [Theory]
        [InlineData(1.9999, 0.5, true)]
        [InlineData(1.9999, 1.5, false)]
        [InlineData(1.0001, 0.99, true)]
        [InlineData(1.0001, 1.01, false)]
        [InlineData(3.9, 0.99, true)]
        [InlineData(3.9, 1.01, false)]
        public void Decision_IsTheSame_AtEveryPowerOfTwo(double x, double f, bool removed)
        {
            Vector3d at = new Vector3d(x, 0, 0);
            DMesh3 sheet = Sheet(at, 0.25, Height(at, 0.25, f), true);
            for (int k = -40; k <= 40; k += 8) {
                double scale = Math.Pow(2, k);
                var m = new DMesh3(sheet);
                for (int v = 0; v < m.MaxVertexID; ++v)
                    m.SetVertex(v, m.GetVertex(v) * scale);
                Assert.Equal(removed, Zero(m));
            }
        }

        /// <summary> running the repair twice on one instance changes nothing, and equal inputs give the same result </summary>
        [Fact]
        public void Reuse_AndDeterminism()
        {
            DMesh3 input = Input(Sheet(Big, 1, Height(Big, 1, 0.5), true));
            DMesh3 m = new DMesh3(input);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(1, r.RemovedComponents);
            int tris = m.TriangleCount;
            r.Apply();
            Assert.Equal(tris, m.TriangleCount);
            Assert.Equal(tris, Repaired(input).Mesh.TriangleCount);
        }
    }
}
