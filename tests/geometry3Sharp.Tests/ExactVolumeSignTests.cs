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

    }
}
