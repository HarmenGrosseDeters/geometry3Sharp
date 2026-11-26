using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BoolCmp;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// RC3 of docs/research/BOOLEAN_TRIAGE_2026-11.md: MeshPostRepairCleanup.FindInvertedComponents (and so
    /// MeshRobustRepair.NotCleanReason, "inside-out components") took the orientation of a closed component from the sign of
    /// its floating-point volume relative to the centre of the whole mesh; for a sliver far from that centre the rounding of
    /// the sum exceeds the volume and the sign is noise. ExactPredicates.VolumeSign is a filtered exact sign
    /// (docs/design/EXACT_VOLUME_SIGN.md): it only corrects wrong answers.
    /// </summary>
    public class ExactVolumeSignTests
    {
        static (double[] xyz, int[] corners) Arrays(DMesh3 m, IEnumerable<int> tris)
        {
            var index = new Dictionary<int, int>();
            var xyz = new List<double>();
            var corners = new List<int>();
            foreach (int t in tris) {
                Index3i tv = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    if (index.TryGetValue(tv[k], out int id) == false) {
                        index[tv[k]] = id = xyz.Count / 3;
                        Vector3d p = m.GetVertex(tv[k]);
                        xyz.Add(p.x); xyz.Add(p.y); xyz.Add(p.z);
                    }
                    corners.Add(id);
                }
            }
            return (xyz.ToArray(), corners.ToArray());
        }

        static int Filtered(DMesh3 m, IEnumerable<int> tris) { var (x, c) = Arrays(m, tris); return ExactPredicates.VolumeSign(x, c); }
        static int Exact(DMesh3 m, IEnumerable<int> tris) { var (x, c) = Arrays(m, tris); return ExactPredicates.VolumeSignExact(x, c); }

        // a closed prism over a regular k-gon in a random plane through at, its top offset from its bottom by `ulps` units
        // in the last place of the coordinates along the plane's normal (rounded): a slab of nearly zero volume of many
        // triangles, outward by its exact sign
        static DMesh3 ThinPrism(Random rnd, int k, int ulps, Vector3d at)
        {
            Vector3d nrm = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
            Vector3d e1 = nrm.Cross(Vector3d.AxisZ).Normalized, e2 = nrm.Cross(e1);
            double len = at.Length;      // (positive; the next double above it, also on net48)
            double h = ulps * (BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(len) + 1) - len);
            var m = new DMesh3();
            for (int i = 0; i < k; ++i) {
                double a = 2 * Math.PI * i / k;
                Vector3d p = at + 0.3 * (Math.Cos(a) * e1 + Math.Sin(a) * e2);
                m.AppendVertex(p);
                m.AppendVertex(p + h * nrm);
            }
            int cb = m.AppendVertex(at), ct = m.AppendVertex(at + h * nrm);
            for (int i = 0; i < k; ++i) {
                int j = (i + 1) % k;
                m.AppendTriangle(cb, 2 * j, 2 * i);
                m.AppendTriangle(ct, 2 * i + 1, 2 * j + 1);
                m.AppendTriangle(2 * i, 2 * j, 2 * j + 1);
                m.AppendTriangle(2 * i, 2 * j + 1, 2 * i + 1);
            }
            return m;
        }

        [Fact]
        public void NonFiniteCoordinates_HaveNoSign()
        {
            Assert.Equal(0, ExactPredicates.VolumeSign(new[] { 0.0, 0, 0, 1, 0, 0, double.NaN, 1, 0 }, new[] { 0, 1, 2 }));
            Assert.Equal(0, ExactPredicates.VolumeSignExact(new[] { 0.0, 0, 0, 1, 0, 0, double.PositiveInfinity, 1, 0 }, new[] { 0, 1, 2 }));
            Assert.Equal(0, ExactPredicates.VolumeSign(new double[0], new int[0]));
        }

        [Fact]
        public void ExactSign_WithoutATriangle_IsZero()
        {
            // no corners, fewer than three corners, and a null list: no solid, no sign (even for coordinates that would give one)
            double[] xyz = { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            Assert.Equal(0, ExactPredicates.VolumeSignExact(xyz, null));
            Assert.Equal(0, ExactPredicates.VolumeSign(xyz, null));
            Assert.Equal(0, ExactPredicates.VolumeSign(xyz, new int[0]));
            Assert.Equal(0, ExactPredicates.VolumeSign(xyz, new[] { 0, 1 }));
            Assert.Equal(0, ExactPredicates.VolumeSignExact(xyz, new int[0]));
            Assert.Equal(0, ExactPredicates.VolumeSignExact(xyz, new[] { 0, 1 }));
            // a closed tetrahedron has a sign, so the zero above comes from the guard
            int[] tet = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            Assert.NotEqual(0, ExactPredicates.VolumeSignExact(xyz, tet));
            Assert.Equal(ExactPredicates.VolumeSignExact(xyz, tet), ExactPredicates.VolumeSign(xyz, tet));
        }

        /// <summary>
        /// The filter never decides wrong: on random slivers (volumes near the rounding of the sum, far from the origin and
        /// at extreme scales) and on fat solids, the filtered sign equals the integer sum's; a good share of the slivers needs
        /// the exact stage, the fat solids none
        /// </summary>
        [Fact]
        public void Filter_AgreesWithTheExactSum()
        {
            var rnd = new Random(20261117);
            int slivers = 0;
            foreach (double scale in new[] { 1.0, Math.Pow(2, -300), Math.Pow(2, 300), 1e-7 })
                foreach (double offset in new[] { 0.0, 1e3, 6e6 })
                    for (int i = 0; i < 150; ++i) {
                        var m = new DMesh3();
                        int[] tet = FindInvertedComponentsExactTests.AppendSliverTet(m, rnd, new Vector3d(offset, -offset, 0.5 * offset));
                        MeshTransforms.Scale(m, scale);
                        int exact = Exact(m, tet);
                        Assert.Equal(exact, Filtered(m, tet));
                        if (exact != 0) ++slivers;
                    }
            Assert.True(slivers > 1000);
            // many triangles: thin prisms over a 64-gon, a few ulps thick, tilted, far from the origin (the rounding of the
            // sum grows with the number of terms: the bound's summation term)
            for (int i = 0; i < 300; ++i) {
                DMesh3 m = ThinPrism(rnd, 64, rnd.Next(1, 6), new Vector3d(1e3 * rnd.NextDouble(), -2e3, 7e2));
                Assert.Equal(Exact(m, m.TriangleIndices()), Filtered(m, m.TriangleIndices()));
            }
            DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 3, EdgeVertices = 8 }.Generate().MakeDMesh();
            Assert.Equal(1, Filtered(sphere, sphere.TriangleIndices()));
            sphere.ReverseOrientation();
            Assert.Equal(-1, Filtered(sphere, sphere.TriangleIndices()));
        }

        /// <summary>
        /// the filter decides fat solids, also far from the origin and at large and small scales, without the exact stage
        /// (not where the products of the differences underflow, eg 2^-500: then the underflow term keeps it undecided and the
        /// integer sum decides, as in Filter_AgreesWithTheExactSum_OpenSoupsAndSubnormals)
        /// </summary>
        [Fact]
        public void Filter_DecidesFatSolids_WithoutTheExactStage()
        {
            foreach (double scale in new[] { 1.0, Math.Pow(2, -300), Math.Pow(2, 300) }) {
                DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
                MeshTransforms.Translate(sphere, new Vector3d(1e6, 2e6, -3e6));
                MeshTransforms.Scale(sphere, scale);
                var (xyz, corners) = Arrays(sphere, sphere.TriangleIndices());
                Assert.Equal(1, ExactPredicates.VolumeSignFilter(xyz, corners));
                Assert.Equal(1, ExactPredicates.VolumeSignExact(xyz, corners));
            }
        }

        /// <summary>
        /// open triangle soups (the sum depends on the origin o, the first corner: a filter or an exact sum that took another
        /// origin would disagree), with coordinates down to subnormal numbers (products that underflow) and mixed magnitudes:
        /// the filtered sign is the exact one
        /// </summary>
        [Fact]
        public void Filter_AgreesWithTheExactSum_OpenSoupsAndSubnormals()
        {
            var rnd = new Random(985);
            int undecided = 0;
            foreach (double scale in new[] { 1.0, Math.Pow(2, -1000), Math.Pow(2, -1060), 1e-320, Math.Pow(2, 500) })
                for (int i = 0; i < 400; ++i) {
                    int k = rnd.Next(1, 6), np = 3 * k + 1;
                    var xyz = new double[3 * np];
                    for (int j = 0; j < xyz.Length; ++j) {
                        double v = (rnd.NextDouble() - 0.5) * (rnd.Next(3) == 0 ? Math.Pow(2, -80) : 1) + (j % 3 == 0 ? 3 : 0);
                        // (some coordinates rounded to eighths: exact zeros and cancellations)
                        xyz[j] = (rnd.Next(2) == 0 ? Math.Round(8 * v) / 8 : v) * scale;
                    }
                    var corners = new int[3 * k];
                    for (int j = 0; j < corners.Length; ++j) corners[j] = rnd.Next(np);
                    int exact = ExactPredicates.VolumeSignExact(xyz, corners);
                    int filter = ExactPredicates.VolumeSignFilter(xyz, corners);
                    Assert.True(filter == 0 || filter == exact, $"scale {scale:R}, case {i}: filter {filter}, exact {exact}");
                    Assert.Equal(exact, ExactPredicates.VolumeSign(xyz, corners));
                    if (filter == 0) ++undecided;
                }
            Assert.True(undecided > 0);
        }

        /// <summary>
        /// the summation term of the bound: the terms det = z of triangles (o; (1,0,0), (0,1,0), (0,0,z)) are exact, but their
        /// recursive sum loses the small ones: +1, +2^-46, then 200 times -(2^-53 - 2^-106) (each just under half an ulp of
        /// the running sum, absorbed), then -1 gives +2^-46 = 128 * 2^-53 in floating point, while the exact sum is negative.
        /// The permanents alone (about 14 * 2^-53) or a quarter of the summation term (about 101 * 2^-53) would let the filter
        /// decide +1; the bound (about 418 * 2^-53, a factor of about 3 above the error, 200 * 2^-53) does not
        /// </summary>
        [Fact]
        public void Filter_SummationError_IsInTheBound()
        {
            double[] z = { 1, Math.Pow(2, -46), -(Math.Pow(2, -53) - Math.Pow(2, -106)), -1 };
            var xyz = new List<double> { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
            foreach (double v in z) { xyz.Add(0); xyz.Add(0); xyz.Add(v); }
            var corners = new List<int> { 0, 1, 2 };     // (a first triangle at o, det 0: o is the origin)
            corners.AddRange(new[] { 1, 2, 3, 1, 2, 4 });
            for (int i = 0; i < 200; ++i) corners.AddRange(new[] { 1, 2, 5 });
            corners.AddRange(new[] { 1, 2, 6 });
            double sum = 0;
            for (int t = 0; t < corners.Count / 3; ++t) sum += xyz[3 * corners[3 * t + 2] + 2] * (t == 0 ? 0 : 1);
            Assert.Equal(Math.Pow(2, -46), sum);            // the floating-point sum
            Assert.Equal(-1, ExactPredicates.VolumeSignExact(xyz.ToArray(), corners.ToArray()));
            Assert.Equal(0, ExactPredicates.VolumeSignFilter(xyz.ToArray(), corners.ToArray()));     // not decided by the filter
            Assert.Equal(-1, ExactPredicates.VolumeSign(xyz.ToArray(), corners.ToArray()));
        }

        /// <summary>
        /// the underflow term of the bound: 1000 terms of exactly 2^-1076 (2^-300 * 2^-300 * 2^-476), each lost to 0 by the
        /// subnormal rounding, and one representable term of -100 * 2^-1074 give a floating-point sum of -100 * 2^-1074,
        /// while the exact sum, (250 - 100) * 2^-1074, is positive. The relative terms of the bound are about 1e-14 of the
        /// sum and would let the filter decide -1; UnderflowUnit times the count does not
        /// </summary>
        [Fact]
        public void Filter_UnderflowTerm_IsInTheBound()
        {
            double t = Math.Pow(2, -300);
            var xyz = new List<double> { 0, 0, 0, t, 0, 0, 0, t, 0, 0, 0, Math.Pow(2, -476), 0, 0, -100 * Math.Pow(2, -474) };
            var corners = new List<int> { 0, 1, 2 };     // (o at the origin, det 0)
            for (int i = 0; i < 1000; ++i) corners.AddRange(new[] { 1, 2, 3 });
            corners.AddRange(new[] { 1, 2, 4 });
            Assert.Equal(0.0, t * t * Math.Pow(2, -476));                       // a term lost to underflow
            Assert.Equal(-100 * double.Epsilon, t * t * (-100 * Math.Pow(2, -474)));
            Assert.Equal(1, ExactPredicates.VolumeSignExact(xyz.ToArray(), corners.ToArray()));
            Assert.Equal(0, ExactPredicates.VolumeSignFilter(xyz.ToArray(), corners.ToArray()));
            Assert.Equal(1, ExactPredicates.VolumeSign(xyz.ToArray(), corners.ToArray()));
        }

        /// <summary> invariance (Similarity.All()): the 2^k scales give the same sign, also for the hard sliver </summary>
        [Fact]
        public void Sign_InvariantUnderSimilarities()
        {
            var (m, tet) = FindInvertedComponentsExactTests.SliverWithAWrongFloatSign();
            int s0 = Filtered(m, tet);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 t = Invariance.Transformed(m, sim);
                if (sim.IsExact) {
                    Assert.Equal(s0, Filtered(t, tet));
                    Assert.Equal(MeshPostRepairCleanup.FindInvertedComponents(m).Count, MeshPostRepairCleanup.FindInvertedComponents(t).Count);
                }
                // every frame: the filtered sign is the exact one
                Assert.Equal(Exact(t, tet), Filtered(t, tet));
            }
        }

        // --- #1191: the exact magnitude compare of the zero-volume rule (docs/design/EXACT_VOLUME_SIGN.md, last section)

        // value * 2^1074 as an integer (exact for every finite double)
        internal static BigInteger Scaled(double v)
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
        internal static BigInteger ExactS(DMesh3 mesh, List<int> comp)
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

        static DMesh3 Sheet(Vector3d at, double r, double h, bool inverted, int n = 17) => SafeRoundingInsideOutResidueTests.Bipyramid(n, r, h, at, inverted);

        // a closed tetrahedron with legs 3 (S = 27, area-free: the test passes t directly), at an offset
        internal static (double[] xyz, int[] corners) Tetra(double ox, double oy, double oz)
        {
            double[] xyz = { ox, oy, oz, ox + 3, oy, oz, ox, oy + 3, oz, ox, oy, oz + 3 };
            int[] corners = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            return (xyz, corners);
        }

        internal static double Next(double x, int k) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + k);

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

        /// <summary>
        /// open soups (the sum depends on the origin, the first vertex: a closed surface hides an error in the origin, whose
        /// terms cancel over it) of one to five triangles with coordinates at offsets and mixed magnitudes, t at |S| / 3 and
        /// its neighbours: the sign is that of the BigInteger comparison. (A single triangle has S = 0 and is asserted in
        /// Compare_DecidesNothing_ForUnusableInput; the equal case in Compare_EqualAndNeighbours. Only the direction 3 emin < e_t of the
        /// shift occurs here: for random full-mantissa coordinates e_t is about 3 emin + 115, and the other direction needs |S| below
        /// 2^-102 of the cube of the smallest coordinate, which no sheet with representable coordinates has)
        /// </summary>
        [Fact]
        public void Compare_AgreesWithTheOracle_OnOpenSoups()
        {
            var rnd = new Random(11911);
            int equal = 0, below = 0, above = 0, accepted = 0;
            for (int k = 0; k < 300; ++k) {
                int n = 1 + rnd.Next(5);
                double scale = Math.Pow(2, rnd.Next(-30, 30)), off = (rnd.NextDouble() - 0.5) * scale * Math.Pow(2, rnd.Next(0, 4));
                var xyz = new double[9 * n];
                for (int i = 0; i < xyz.Length; ++i)
                    xyz[i] = off + (rnd.NextDouble() - 0.5) * scale;
                var corners = new int[3 * n];
                for (int i = 0; i < corners.Length; ++i)
                    corners[i] = 3 * (i / 3) + i % 3;
                // (S exactly, at the scale 2^(3 * 1074), the same sum as the library's: the first vertex is the origin)
                BigInteger[] q = new BigInteger[xyz.Length];
                for (int i = 0; i < q.Length; ++i)
                    q[i] = Scaled(xyz[i]);
                BigInteger total = 0;
                for (int tri = 0; tri < n; ++tri) {
                    BigInteger[] a = new BigInteger[3], b = new BigInteger[3], c = new BigInteger[3];
                    for (int d = 0; d < 3; ++d) {
                        a[d] = q[9 * tri + d] - q[d]; b[d] = q[9 * tri + 3 + d] - q[d]; c[d] = q[9 * tri + 6 + d] - q[d];
                    }
                    total += c[0] * (a[1] * b[2] - a[2] * b[1]) + c[1] * (a[2] * b[0] - a[0] * b[2]) + c[2] * (a[0] * b[1] - a[1] * b[0]);
                }
                BigInteger absS = BigInteger.Abs(total);
                if (absS.IsZero)
                    continue;
                double t0 = (double)(absS >> (3 * 1074 - 60)) / 3 / 1152921504606846976.0;
                if (t0 < ExactPredicates.MinNormal || double.IsInfinity(t0))
                    continue;
                ++accepted;
                foreach (double t in new[] { t0, Next(t0, 1), Next(t0, -1), t0 * 1.5, t0 * 0.5 }) {
                    int expected = absS.CompareTo(3 * Scaled(t) << (2 * 1074));
                    Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, corners, t, out int got), $"case {k}");
                    Assert.Equal(expected, got);
                    if (expected == 0) ++equal; else if (expected < 0) ++below; else ++above;
                }
            }
            Assert.True(below > 200 && above > 200, $"{below} below, {above} above, {equal} equal");
            Assert.True(accepted > 150, $"{accepted} of 300 soups decided");
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
            // (one triangle: S = 0, below 3 t, decided)
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, new[] { 0, 1, 2 }, 9, out s));
            Assert.Equal(-1, s);
            Assert.False(ExactPredicates.TryCompareAbsVolume6(xyz, new[] { 0, 1 }, 9, out s));
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


        // (#1216) the exact volume sum and the comparison of two sums by a factor: what decides whether a repair changed the volume
        // of a closed component by more than a factor 2 (MeshRoundingRepair.remove_tiny_components)
        // a tetrahedron with the corner o and the edge vectors a e1, b e2, c e3: six times its volume is a b c
        static (double[] xyz, int[] corners) Tetrahedron(Vector3d o, double a, double b, double c, bool inverted)
        {
            var xyz = new[] { o.x, o.y, o.z, o.x + a, o.y, o.z, o.x, o.y + b, o.z, o.x, o.y, o.z + c };
            int[] corners = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            if (inverted) corners = new[] { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2 };
            return (xyz, corners);
        }

        /// <summary> the exact volume sum: its value on tetrahedra (also far from the origin and at mixed exponents), its sign, no value without a triangle or with a NaN </summary>
        [Fact]
        public void VolumeSum6Exact_IsTheSumOfTheDeterminants()
        {
            // (S = total 2^e: the exponent is three times that of the smallest unit of the coordinates, so total is an integer)
            Func<BigInteger, int, double> value = (t, x) => (double)t * Math.Pow(2, x);
            var (xyz, corners) = Tetrahedron(new Vector3d(0, 0, 0), 2, 3, 5, false);
            Assert.True(ExactPredicates.TryVolumeSum6Exact(xyz, corners, out BigInteger total, out int e));
            Assert.True(e % 3 == 0);
            Assert.Equal(30, value(total, e));
            (xyz, corners) = Tetrahedron(new Vector3d(0, 0, 0), 2, 3, 5, true);
            Assert.True(ExactPredicates.TryVolumeSum6Exact(xyz, corners, out total, out e));
            Assert.Equal(-30, value(total, e));
            // (far from the origin, coordinates with fractions)
            (xyz, corners) = Tetrahedron(new Vector3d(1e6, -2.5, 0.125), 0.5, 0.25, 3, false);
            Assert.True(ExactPredicates.TryVolumeSum6Exact(xyz, corners, out total, out e));
            Assert.True(e < 0 && e % 3 == 0);
            Assert.Equal(0.375, value(total, e));
            Assert.Equal(1, total.Sign);
            Assert.Equal(ExactPredicates.VolumeSignExact(xyz, corners), total.Sign);
            // (independent of the first corner for a closed surface: the triangles in another order)
            int[] rotated = corners.Skip(3).Concat(corners.Take(3)).ToArray();
            Assert.True(ExactPredicates.TryVolumeSum6Exact(xyz, rotated, out BigInteger total2, out int e2));
            Assert.Equal(total, total2);
            Assert.Equal(e, e2);
            Assert.False(ExactPredicates.TryVolumeSum6Exact(xyz, new int[0], out total, out e));
            Assert.False(ExactPredicates.TryVolumeSum6Exact(xyz, null, out total, out e));
            xyz[4] = double.NaN;
            Assert.False(ExactPredicates.TryVolumeSum6Exact(xyz, corners, out total, out e));
            xyz[4] = double.PositiveInfinity;
            Assert.False(ExactPredicates.TryVolumeSum6Exact(xyz, corners, out total, out e));
        }

        /// <summary>
        /// the factor is 2 on both sides, not 1.5 or 3 (#1464 review: a growth factor of 1.5 survived every fixture of the thin solids,
        /// whose volumes end below 1 or beyond 2): growth and shrink at 1.5, 1.99 and 2.01 times, in both directions
        /// </summary>
        [Fact]
        public void VolumeSumsDifferByMoreThan_FactorTwo_JustBelowAndJustAbove_OnBothSides()
        {
            BigInteger v = BigInteger.Pow(10, 30);
            Func<int, bool> grows = permille => ExactPredicates.VolumeSumsDifferByMoreThan(v, 0, v * permille / 1000, 0, 1);
            Func<int, bool> shrinks = permille => ExactPredicates.VolumeSumsDifferByMoreThan(v * permille / 1000, 0, v, 0, 1);
            Assert.False(grows(1500));
            Assert.False(grows(1990));
            Assert.True(grows(2010));
            Assert.False(grows(1000));
            Assert.False(shrinks(1500));
            Assert.False(shrinks(1990));
            Assert.True(shrinks(2010));
            Assert.False(shrinks(1000));
            // (down to the half, not to 2/3 or 1/3 of the volume)
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(v, 0, v / 2 + v / 100, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(v, 0, v / 2 - v / 100, 0, 1));
        }

        /// <summary> "more than a factor 2": exactly 2 is not more, one unit beyond is, in both directions, across exponents, and a changed sign always is </summary>
        [Fact]
        public void VolumeSumsDifferByMoreThan_IsStrict_AndExact()
        {
            BigInteger big = BigInteger.Pow(2, 200) + 1;
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, big, 0, 1));
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, 2 * big, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, 2 * big + 1, 0, 1));
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(2 * big, 0, big, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(2 * big + 1, 0, big, 0, 1));
            // (the same values at different exponents: 2 big = big 2^1; 8 big at -3 = big)
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, big, 1, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, big + 1, 1, 1));
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(8 * big, -3, 2 * big, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(8 * big - 1, -3, 2 * big, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(big, 3, big, 0, 1));
            // (the factor: 2^2)
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, 4 * big, 0, 2));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, 4 * big + 1, 0, 2));
            // (signs: negative sums compare by magnitude; a changed sign, or one of them zero, is a change)
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(-big, 0, -2 * big, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(-big, 0, -2 * big - 1, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, -big, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(big, 0, BigInteger.Zero, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(BigInteger.Zero, 0, big, 0, 1));
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(BigInteger.Zero, 0, BigInteger.Zero, 5, 1));
            // (the first sum at the larger exponent, small: 1 x 2^4 against 20 is a factor 1.25; shifted the wrong way it were 0)
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(BigInteger.One, 4, 20, 0, 1));
            Assert.False(ExactPredicates.VolumeSumsDifferByMoreThan(20, 0, BigInteger.One, 4, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(BigInteger.One, 4, 33, 0, 1));
            Assert.True(ExactPredicates.VolumeSumsDifferByMoreThan(33, 0, BigInteger.One, 4, 1));
        }

        /// <summary> one triangle is a sum (zero: it is taken relative to its own first corner); fewer than three corners are none </summary>
        [Fact]
        public void VolumeSum6Exact_OneTriangle_IsZero_NoTriangle_IsNotComputed()
        {
            double[] xyz = { 1, 2, 3, 4, 6, 5, 7, 1, 9 };
            Assert.True(ExactPredicates.TryVolumeSum6Exact(xyz, new[] { 0, 1, 2 }, out BigInteger total, out int exponent));
            Assert.True(total.IsZero);
            Assert.False(ExactPredicates.TryVolumeSum6Exact(xyz, new[] { 0, 1 }, out total, out exponent));
            Assert.False(ExactPredicates.TryVolumeSum6Exact(xyz, null, out total, out exponent));
        }

        // ---- #1435: the filter code of VolumeSign and TryCompareAbsVolume6, value by value (the survivors of the mutation shard)

        static (double[] xyz, int[] corners) FatSolid(bool insideOut)
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sphere, new Vector3d(1e6, 2e6, -3e6));
            if (insideOut) sphere.ReverseOrientation();
            return Arrays(sphere, sphere.TriangleIndices());
        }

        /// <summary> a vertex that no triangle uses is still checked: a coordinate that is not finite anywhere gives no sign, whichever the filter says </summary>
        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteCoordinate_OfAnUnusedVertex_GivesNoSign_AndNoComparison(double bad)
        {
            var (xyz, corners) = FatSolid(false);
            Assert.Equal(1, ExactPredicates.VolumeSign(xyz, corners));            // (the control: the filter decides this solid)
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, corners, 1, out int control));
            Assert.Equal(1, control);
            // (the bad coordinate in each of the three places of the unused vertex, and as the first and last array element)
            for (int place = 0; place < 3; ++place) {
                double[] x2 = new double[xyz.Length + 3];
                Array.Copy(xyz, x2, xyz.Length);
                x2[xyz.Length + place] = bad;
                Assert.Equal(0, ExactPredicates.VolumeSign(x2, corners));
                Assert.Equal(0, ExactPredicates.VolumeSignExact(x2, corners));
                Assert.False(ExactPredicates.TryCompareAbsVolume6(x2, corners, 1, out int s));
            }
            // (the first array element, which no triangle of this list uses: the guard must start at 0)
            double[] x3 = new double[xyz.Length + 3];
            Array.Copy(xyz, 0, x3, 3, xyz.Length);
            int[] shifted = corners.Select(c => c + 1).ToArray();
            x3[0] = bad;
            Assert.Equal(0, ExactPredicates.VolumeSign(x3, shifted));
            Assert.False(ExactPredicates.TryCompareAbsVolume6(x3, shifted, 1, out int s2));
        }

        /// <summary> the filter's sign of a solid turned inside out is -1 (the second test of SignOfBoundedSum), and VolumeSign agrees </summary>
        [Fact]
        public void Filter_DecidesAnInsideOutSolid_AsNegative()
        {
            var (xyz, corners) = FatSolid(true);
            Assert.Equal(-1, ExactPredicates.VolumeSignFilter(xyz, corners));
            Assert.Equal(-1, ExactPredicates.VolumeSign(xyz, corners));
            Assert.Equal(-1, ExactPredicates.VolumeSignExact(xyz, corners));
        }

        [Fact]
        public void SignOfBoundedSum_IsStrictOnBothSides()
        {
            Assert.Equal(1, ExactPredicates.SignOfBoundedSum(10, 5));
            Assert.Equal(-1, ExactPredicates.SignOfBoundedSum(-10, 5));
            Assert.Equal(0, ExactPredicates.SignOfBoundedSum(5, 5));
            Assert.Equal(0, ExactPredicates.SignOfBoundedSum(-5, 5));
            Assert.Equal(0, ExactPredicates.SignOfBoundedSum(0, 0));
            Assert.Equal(1, ExactPredicates.SignOfBoundedSum(Next(5, 1), 5));
            Assert.Equal(-1, ExactPredicates.SignOfBoundedSum(-Next(5, 1), 5));
            Assert.Equal(0, ExactPredicates.SignOfBoundedSum(Next(5, -1), 5));
            Assert.Equal(0, ExactPredicates.SignOfBoundedSum(-Next(5, -1), 5));
        }

#if NET
        /// <summary>
        /// what the filter decides costs no allocation, what it does not decide goes to the integers (BigInteger arrays): the filtered
        /// sign of a fat solid, and the comparison of its |S| with a far threshold on both sides, allocate nothing (the
        /// bypass of the filter would give the same sign, only slower)
        /// </summary>
        [Fact]
        public void FilterDecisions_AllocateNothing_TheExactStageDoes()
        {
            var (xyz, corners) = FatSolid(false);
            // (the sum S of this solid is about 6 times its volume of 4 / 3 pi: 25; thresholds far below and above)
            for (int warm = 0; warm < 3; ++warm) {
                ExactPredicates.VolumeSign(xyz, corners);
                ExactPredicates.TryCompareAbsVolume6(xyz, corners, 1, out _);
                ExactPredicates.TryCompareAbsVolume6(xyz, corners, 100, out _);
                ExactPredicates.VolumeSign(xyz, corners.Take(3).ToArray());
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int sign = ExactPredicates.VolumeSign(xyz, corners);
            bool decided1 = ExactPredicates.TryCompareAbsVolume6(xyz, corners, 1, out int s1);
            bool decided2 = ExactPredicates.TryCompareAbsVolume6(xyz, corners, 100, out int s2);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(1, sign);
            Assert.True(decided1 && decided2);
            Assert.Equal(1, s1);
            Assert.Equal(-1, s2);
            Assert.Equal(0, after - before);
            // (the exact stage allocates: a threshold at |S| / 3 cannot be decided by the filter)
            var (tx, tc) = Tetra(0, 0, 0);
            long b2 = GC.GetAllocatedBytesForCurrentThread();
            ExactPredicates.TryCompareAbsVolume6(tx, tc, 9, out _);
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - b2 > 0);
        }
#endif

        // ---- the bound of VolumeSumWithBound

        static (BigInteger m, int e) Dyadic(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            BigInteger m = exp == 0 ? frac : frac | (1L << 52);
            int e = exp == 0 ? -1074 : exp - 1075;
            return (bits < 0 ? -m : m, e);
        }
        static (BigInteger m, int e) Mul((BigInteger m, int e) a, (BigInteger m, int e) b) => (a.m * b.m, a.e + b.e);
        static (BigInteger m, int e) Add((BigInteger m, int e) a, (BigInteger m, int e) b)
        {
            int e = Math.Min(a.e, b.e);
            return ((a.m << (a.e - e)) + (b.m << (b.e - e)), e);
        }
        static int Compare((BigInteger m, int e) a, (BigInteger m, int e) b)
        {
            int e = Math.Min(a.e, b.e);
            return (a.m << (a.e - e)).CompareTo(b.m << (b.e - e));
        }
        // |x - exact| / exact as a double (exact positive)
        static double RelativeError(double x, (BigInteger m, int e) exact)
        {
            var d = Add(Dyadic(x), (-exact.m, exact.e));
            return (double)((BigInteger.Abs(d.m) << 200) / BigInteger.Abs(exact.m)) * Math.Pow(2, d.e - exact.e - 200);
        }

        /// <summary> g = gamma_(n+2) = k u / (1 - k u), k = n + 2, rounded once: within half a unit in the last place of the exact value </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(100)]
        [InlineData(1000000)]
        public void VolumeBoundG_IsTheRoundedGamma(int n)
        {
            BigInteger k = n + 2;
            BigInteger two53 = BigInteger.One << 53;
            double g = ExactPredicates.VolumeBoundG(n);
            // exact: k / (2^53 - k); |g - exact| <= 2^-53 exact (one rounding of the quotient; k u and 1 - k u are exact)
            double exactApprox = (double)((k << 200) / (two53 - k)) * Math.Pow(2, -200);
            Assert.True(Math.Abs(g - exactApprox) <= 1.0e-16 * exactApprox + 1e-300, n + ": " + g + " vs " + exactApprox);
        }

        /// <summary> E = (Orient3dBound perm + UnderflowUnit under + g abs) (1 + 3 g), up to the few roundings of its evaluation (2^-51 relative); g large enough to show the factor </summary>
        [Theory]
        [InlineData(0.75, 0.0, 1.25, 1.0 / (1 << 30))]
        [InlineData(1.0, 0.0, 0.0, 1.0 / (1 << 20))]
        [InlineData(0.0, 0.0, 1.0, 1.0 / (1 << 30))]
        [InlineData(1e10, 3.0, 2.5e9, 1.0 / (1 << 25))]
        public void VolumeBound_IsTheDocumentedExpression(double perm, double under, double abs, double g)
        {
            double bound = ExactPredicates.VolumeBound(perm, under, abs, g);
            var sum = Add(Add(Mul(Dyadic(ExactPredicates.Orient3dBound), Dyadic(perm)), Mul(Dyadic(ExactPredicates.UnderflowUnit), Dyadic(under))),
                          Mul(Dyadic(g), Dyadic(abs)));
            var factor = Add(Dyadic(1.0), Mul(Dyadic(3.0), Dyadic(g)));
            var exact = Mul(sum, factor);
            Assert.True(RelativeError(bound, exact) <= Math.Pow(2, -51), "relative error " + RelativeError(bound, exact));
        }

        [Fact]
        public void VolumeBoundUsable_NeedsFiniteSumAndBound_AndSmallG()
        {
            Assert.True(ExactPredicates.VolumeBoundUsable(1, 2, 0.1));
            Assert.True(ExactPredicates.VolumeBoundUsable(-1, 2, 0.1));
            Assert.True(ExactPredicates.VolumeBoundUsable(1, 2, Next(0.25, -1)));
            Assert.False(ExactPredicates.VolumeBoundUsable(1, 2, 0.25));
            Assert.False(ExactPredicates.VolumeBoundUsable(1, 2, 0.5));
            // each of the four values that are not finite, with the others fine (the sum's own tests are a second guard of the bound's)
            Assert.False(ExactPredicates.VolumeBoundUsable(double.NaN, 2, 0.1));
            Assert.False(ExactPredicates.VolumeBoundUsable(double.PositiveInfinity, 2, 0.1));
            Assert.False(ExactPredicates.VolumeBoundUsable(double.NegativeInfinity, 2, 0.1));
            Assert.False(ExactPredicates.VolumeBoundUsable(1, double.NaN, 0.1));
            Assert.False(ExactPredicates.VolumeBoundUsable(1, double.PositiveInfinity, 0.1));
        }

        /// <summary> perm (a sum of absolute products) can overflow where det does not: the sum is finite, the bound is not, and the filter must not be used </summary>
        [Fact]
        public void VolumeSumWithBound_FiniteSumWithOverflowingBound_IsNotUsable()
        {
            // the second triangle has u = (0, 1e154, 1.1e154), v = (0, 1.1e154, 1e154), w = (0.5, 0, 0): uyvz = 1e308, uzvy = 1.21e308, so
            // |uyvz| + |uzvy| overflows (perm = infinity) and det = 0.5 (uyvz - uzvy) = -1e307 does not
            double[] xyz = { 0, 0, 0,   0, 1e154, 1.1e154,   0, 1.1e154, 1e154,   0.5, 0, 0 };
            int[] corners = { 0, 0, 0, 1, 2, 3 };
            bool usable = ExactPredicates.VolumeSumWithBound(xyz, corners, out double sum, out double bound);
            Assert.False(usable);
            Assert.True(double.IsInfinity(bound));
            Assert.True(sum < -1e306 && !double.IsInfinity(sum));
            // (the filter is unusable, so the exact stage compares: |S| of about 1e307 is far above 3 t = 3)
            Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, corners, 1, out int s));
            Assert.Equal(1, s);
            Assert.Equal(0, ExactPredicates.VolumeSignFilter(xyz, corners));       // (undecided: the exact stage decides)
            Assert.Equal(-1, ExactPredicates.VolumeSign(xyz, corners));
        }

        // ---- the comparison filter of TryCompareAbsVolume6

        static bool Less((BigInteger m, int e) a, (BigInteger m, int e) b) => Compare(a, b) < 0;

        /// <summary>
        /// soundness and completeness of VolumeCompareFilter against exact arithmetic: sum = a, bound = E, 3 t at a x E (1 + j u)
        /// around the thresholds a - E and a + E, j from -24 to 24 (u = 2^-53). Decided +1 only where the exact a - E exceeds 3 t, -1 only where
        /// the exact a + E is below 3 t (the filter is never wrong); decided wherever the exact margin is more than 16 u of 3 t (not too
        /// timid: the slack is 4 u per side and the roundings about 3 u)
        /// </summary>
        [Fact]
        public void VolumeCompareFilter_IsSound_AndDecidesBeyondTheSlack()
        {
            var rnd = new Random(1435);
            double u = ExactPredicates.Epsilon;
            int decidedPlus = 0, decidedMinus = 0, undecided = 0;
            for (int trial = 0; trial < 400; ++trial) {
                double a = (0.5 + rnd.NextDouble()) * Math.Pow(2, rnd.Next(-200, 200));
                double bound = a * Math.Pow(2, -rnd.Next(2, 60)) * (0.5 + rnd.NextDouble());
                for (int j = -24; j <= 24; ++j) {
                    foreach (bool plus in new[] { true, false }) {
                        double edge = plus ? a - bound : a + bound;
                        double t3target = edge * (1 + j * u);
                        double t = t3target / 3;
                        if (!(t >= ExactPredicates.MinNormal)) continue;
                        bool decided = ExactPredicates.VolumeCompareFilter(a, bound, t, out int sign);
                        var exactT3 = Mul(Dyadic(t), Dyadic(3.0));
                        var exactMinus = Add(Dyadic(a), Dyadic(-bound));      // a - E
                        var exactPlus = Add(Dyadic(a), Dyadic(bound));        // a + E
                        if (decided) {
                            if (sign == 1) { Assert.True(Less(exactT3, exactMinus), "unsound +1 at j=" + j); decidedPlus++; }
                            else { Assert.Equal(-1, sign); Assert.True(Less(exactPlus, exactT3), "unsound -1 at j=" + j); decidedMinus++; }
                        } else { Assert.Equal(0, sign); undecided++; }
                        // completeness: beyond 16 u of 3 t the filter decides
                        var margin = Mul(exactT3, Add(Dyadic(1.0), Dyadic(16 * u)));
                        if (Less(margin, exactMinus)) Assert.True(decided && sign == 1, "not decided +1 at j=" + j);
                        var lowMargin = Mul(exactT3, Add(Dyadic(1.0), Dyadic(-16 * u)));
                        if (Less(exactPlus, lowMargin)) Assert.True(decided && sign == -1, "not decided -1 at j=" + j);
                    }
                }
            }
            Assert.True(decidedPlus > 1000 && decidedMinus > 1000 && undecided > 1000, decidedPlus + " " + decidedMinus + " " + undecided);
        }

        /// <summary> where the slack-adjusted values are equal the filter decides nothing (strict comparisons), on both sides </summary>
        [Fact]
        public void VolumeCompareFilter_AtEquality_DecidesNothing()
        {
            double u = ExactPredicates.Epsilon;
            int foundPlus = 0, foundMinus = 0;
            var rnd = new Random(7);
            for (int trial = 0; trial < 2000 && (foundPlus < 5 || foundMinus < 5); ++trial) {
                double a = (0.5 + rnd.NextDouble()) * 64, bound = a * 0.125 * (0.5 + rnd.NextDouble());
                double x = (a - bound) * (1 - 4 * u);
                double y = (a + bound) * (1 + 4 * u);
                double t0 = x / (1 + 4 * u) / 3, t1 = y / (1 - 4 * u) / 3;
                for (int k = -6; k <= 6; ++k) {
                    double t = Next(t0, k);
                    if (3 * t * (1 + 4 * u) == x) { foundPlus++; Assert.False(ExactPredicates.VolumeCompareFilter(a, bound, t, out int s)); Assert.Equal(0, s); }
                    t = Next(t1, k);
                    if (3 * t * (1 - 4 * u) == y) { foundMinus++; Assert.False(ExactPredicates.VolumeCompareFilter(a, bound, t, out int s)); Assert.Equal(0, s); }
                }
            }
            Assert.True(foundPlus >= 5 && foundMinus >= 5, foundPlus + " " + foundMinus);
        }

        [Fact]
        public void VolumeCompareFilter_ThresholdThatOverflows_DecidesNothing_AndNormalOnesDecide()
        {
            Assert.False(ExactPredicates.VolumeCompareFilter(1, 0.5, double.MaxValue, out int s));       // 3 t overflows
            Assert.False(ExactPredicates.VolumeCompareFilter(1e300, 1, 1e308, out s));
            Assert.Equal(0, s);
            Assert.True(ExactPredicates.VolumeCompareFilter(1000, 1, 10, out s));
            Assert.Equal(1, s);
            Assert.True(ExactPredicates.VolumeCompareFilter(-1000, 1, 10, out s));       // (the sign of the sum does not matter: |S|)
            Assert.Equal(1, s);
            Assert.True(ExactPredicates.VolumeCompareFilter(10, 1, 1000, out s));
            Assert.Equal(-1, s);
            Assert.True(ExactPredicates.VolumeCompareFilter(0, 0.5, 1000, out s));
            Assert.Equal(-1, s);
        }

        // S = the exact sum of the determinants of VolumeSign, in exact dyadic arithmetic of the test (not the library's integers)
        static (BigInteger m, int e) ExactSum(double[] xyz, int[] corners)
        {
            (BigInteger m, int e) total = (BigInteger.Zero, 0);
            Func<int, int, (BigInteger m, int e)> d = (i, j) => Add(Dyadic(xyz[3 * i + j]), Dyadic(-xyz[3 * corners[0] + j]));
            for (int t = 0; t < corners.Length / 3; ++t) {
                int a = corners[3 * t], b = corners[3 * t + 1], c = corners[3 * t + 2];
                var (ux, uy, uz) = (d(a, 0), d(a, 1), d(a, 2));
                var (vx, vy, vz) = (d(b, 0), d(b, 1), d(b, 2));
                var (wx, wy, wz) = (d(c, 0), d(c, 1), d(c, 2));
                Func<(BigInteger m, int e), (BigInteger m, int e)> neg = x => (-x.m, x.e);
                var det = Add(Add(Mul(wx, Add(Mul(uy, vz), neg(Mul(uz, vy)))), Mul(wy, Add(Mul(uz, vx), neg(Mul(ux, vz))))),
                              Mul(wz, Add(Mul(ux, vy), neg(Mul(uy, vx)))));
                total = Add(total, det);
            }
            return total;
        }

        /// <summary>
        /// a sliver of the size of a few units in the last place at coordinates near 1: |S| is a few units of 2^(3 emin), about 2^-156 and
        /// below t's own unit 2^-210 only where 3 emin lies above the exponent of t, so the exact comparison shifts |S| to the left (the
        /// direction that Compare_AgreesWithTheOracle_OnOpenSoups does not reach), t from 2 below to 2 above |S| / 3 (the filter cannot decide)
        /// </summary>
        [Fact]
        public void Compare_ExactStage_ShiftsTheSumLeft_ForASliverOfUlpSize()
        {
            var rnd = new Random(1435);
            double ulp = Math.Pow(2, -52);
            int checkedCases = 0;
            for (int trial = 0; trial < 60; ++trial) {
                double[] xyz = new double[12];
                for (int i = 0; i < 3; ++i) xyz[i] = 1.0;                       // o = (1, 1, 1)
                for (int v = 1; v < 4; ++v)
                    for (int c = 0; c < 3; ++c)
                        xyz[3 * v + c] = 1.0 + (rnd.Next(-3, 8)) * ulp * (c == v - 1 ? 1 : 0.0) + rnd.Next(0, 2) * ulp * (c == (v % 3) ? 1 : 0.0);
                int[] corners = (trial % 2 == 0) ? new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 } : new[] { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2 };
                var s = ExactSum(xyz, corners);
                if (s.m.IsZero) continue;
                var abs = (BigInteger.Abs(s.m), s.e);
                double t0 = Math.Abs((double)s.m * Math.Pow(2, s.e)) / 3;     // (s.m small: both factors exact)
                for (int k = -2; k <= 2; ++k) {
                    double t = Next(t0, k);
                    int expected = Compare(abs, Mul(Dyadic(3.0), Dyadic(t)));
                    Assert.True(ExactPredicates.TryCompareAbsVolume6(xyz, corners, t, out int sign), "trial " + trial + " k " + k);
                    Assert.Equal(Math.Sign(expected), sign);
                    checkedCases++;
                }
            }
            Assert.True(checkedCases > 100, checkedCases.ToString());
        }

        /// <summary>
        /// the bound of VolumeSumWithBound term by term, on soups of small integer coordinates (the determinants and products are exact in doubles):
        /// perm = sum of |wx| (|uyvz| + |uzvy|) + |wy| (|uzvx| + |uxvz|) + |wz| (|uxvy| + |uyvx|), under = sum of |w|_1 + 1, abs = sum of |det|,
        /// bound = VolumeBound of them with g = VolumeBoundG(n), and the sum itself, all in exact arithmetic of the test
        /// </summary>
        [Fact]
        public void VolumeSumWithBound_IsTheDocumentedExpression_TermByTerm()
        {
            var rnd = new Random(1435);
            int cases = 0;
            for (int trial = 0; trial < 200; ++trial) {
                int nv = 4 + rnd.Next(4), n = 1 + rnd.Next(4);
                double[] xyz = new double[3 * nv];
                for (int i = 0; i < xyz.Length; ++i) xyz[i] = rnd.Next(-9, 10);
                int[] corners = new int[3 * n];
                for (int i = 0; i < corners.Length; ++i) corners[i] = rnd.Next(nv);
                (BigInteger m, int e) sumX = (BigInteger.Zero, 0), perm = (BigInteger.Zero, 0), under = (BigInteger.Zero, 0), abs = (BigInteger.Zero, 0);
                Func<(BigInteger m, int e), (BigInteger m, int e)> mag = x => (BigInteger.Abs(x.m), x.e);
                Func<int, int, (BigInteger m, int e)> d = (i, j) => Add(Dyadic(xyz[3 * i + j]), Dyadic(-xyz[3 * corners[0] + j]));
                for (int t = 0; t < n; ++t) {
                    int a = corners[3 * t], b = corners[3 * t + 1], c = corners[3 * t + 2];
                    var (ux, uy, uz) = (d(a, 0), d(a, 1), d(a, 2));
                    var (vx, vy, vz) = (d(b, 0), d(b, 1), d(b, 2));
                    var (wx, wy, wz) = (d(c, 0), d(c, 1), d(c, 2));
                    Func<(BigInteger m, int e), (BigInteger m, int e)> neg = x => (-x.m, x.e);
                    var det = Add(Add(Mul(wx, Add(Mul(uy, vz), neg(Mul(uz, vy)))), Mul(wy, Add(Mul(uz, vx), neg(Mul(ux, vz))))),
                                  Mul(wz, Add(Mul(ux, vy), neg(Mul(uy, vx)))));
                    sumX = Add(sumX, det);
                    abs = Add(abs, mag(det));
                    perm = Add(perm, Add(Add(Mul(mag(wx), Add(mag(Mul(uy, vz)), mag(Mul(uz, vy)))), Mul(mag(wy), Add(mag(Mul(uz, vx)), mag(Mul(ux, vz))))),
                                         Mul(mag(wz), Add(mag(Mul(ux, vy)), mag(Mul(uy, vx))))));
                    under = Add(under, Add(Add(mag(wx), Add(mag(wy), mag(wz))), Dyadic(1.0)));
                }
                Assert.True(ExactPredicates.VolumeSumWithBound(xyz, corners, out double sum, out double bound));
                Assert.Equal(0, Compare(Dyadic(sum), sumX));                  // (exact: small integers)
                // g exact: k / (2^53 - k)
                BigInteger k = n + 2;
                var gDen = (BigInteger.One << 53) - k;
                // bound = (Orient3dBound perm + UnderflowUnit under + g abs) (1 + 3 g), g = k / gDen: compare with the exact value, relative 2^-50
                var inner = Add(Add(Mul(Dyadic(ExactPredicates.Orient3dBound), perm), Mul(Dyadic(ExactPredicates.UnderflowUnit), under)), (BigInteger.Zero, 0));
                // (g abs and (1 + 3 g) as exact fractions: multiply through by gDen)
                var num = Add(Mul(inner, (gDen, 0)), Mul((k, 0), abs));                    // (inner + g abs) gDen
                var exactNum = Mul(num, (gDen + 3 * k, 0));                                 // (1 + 3 g) gDen = gDen + 3 k
                if (exactNum.m.IsZero) { Assert.Equal(0.0, bound); continue; }
                if (bound < 1e-290) continue;       // (only the underflow term: subnormal, rounded far coarser than 2^-50)
                var diff = Add(Mul(Dyadic(bound), (gDen * gDen, 0)), (-exactNum.m, exactNum.e));
                double rel = (double)((BigInteger.Abs(diff.m) << 200) / BigInteger.Abs(exactNum.m)) * Math.Pow(2, diff.e - exactNum.e - 200);
                Assert.True(rel <= Math.Pow(2, -50), "n=" + n + " bound " + bound + " relative error " + rel);
                cases++;
            }
            Assert.True(cases > 100, cases.ToString());
        }
    }
}
