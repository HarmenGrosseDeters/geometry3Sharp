using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The ray decisions on the exact direction (#1672: ExactRayTriangle.LineDirection, *Direction,
    /// ExactPredicates.Orient3dDirection): against an independent rational oracle (plane hit and barycentric signs,
    /// in BigInteger) on seeded inputs over the whole exponent range, the symbolic perturbation against an explicit
    /// one, LineDirection an exact positive multiple of the direction, and invariance under Similarity.All() (the
    /// 2^k scales and far 2^k beyond them bit for bit).
    /// </summary>
    public class ExactRayTriangleDirectionTests
    {
        // ---- the rational oracle (shares no code with ExactRayTriangle / ExactPredicates) ----

        // v = m 2^e exactly
        static void Decompose(double v, out BigInteger m, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int ex = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (ex == 0) e = -1074; else { frac |= 1L << 52; e = ex - 1075; }
            m = bits < 0 ? -new BigInteger(frac) : new BigInteger(frac);
        }

        // the coordinates as integers in units of the smallest exponent among them (a positive common scale)
        internal static BigInteger[][] Integers(params Vector3d[] pts)
        {
            var ms = new BigInteger[pts.Length * 3];
            var es = new int[pts.Length * 3];
            int emin = int.MaxValue;
            for (int i = 0; i < pts.Length; ++i)
                for (int j = 0; j < 3; ++j) {
                    Decompose(pts[i][j], out ms[3 * i + j], out es[3 * i + j]);
                    if (ms[3 * i + j].IsZero == false) emin = Math.Min(emin, es[3 * i + j]);
                }
            var r = new BigInteger[pts.Length][];
            for (int i = 0; i < pts.Length; ++i) {
                r[i] = new BigInteger[3];
                for (int j = 0; j < 3; ++j)
                    r[i][j] = ms[3 * i + j].IsZero ? BigInteger.Zero : ms[3 * i + j] << (es[3 * i + j] - emin);
            }
            return r;
        }

        internal static BigInteger[] Sub(BigInteger[] a, BigInteger[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        internal static BigInteger[] Cross(BigInteger[] a, BigInteger[] b) =>
            new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        internal static BigInteger Dot(BigInteger[] a, BigInteger[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static BigInteger[] Mul(BigInteger s, BigInteger[] a) => new[] { s * a[0], s * a[1], s * a[2] };
        static BigInteger[] Add(BigInteger[] a, BigInteger[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };

        /// <summary>
        /// The closed-triangle hit of the ray o + t d, t &gt;= 0, by the plane hit: N = (b - a) x (c - a); d . N = 0 is a
        /// miss (parallel, or in the plane: the closed rule); t = (a - o) . N / (d . N); the hit point X = o + t d is in
        /// the closed triangle iff the three ((v_(i+1) - v_i) x (X - v_i)) . N are &gt;= 0. Miss for a triangle of no area.
        /// </summary>
        static int Oracle(Vector3d o, Vector3d d, Vector3d a, Vector3d b, Vector3d c)
        {
            var I = Integers(o, d, a, b, c);
            BigInteger[] O = I[0], D = I[1], A = I[2], B = I[3], C = I[4];
            var N = Cross(Sub(B, A), Sub(C, A));
            BigInteger den = Dot(D, N);
            if (den.IsZero) return ExactRayTriangle.Miss;
            BigInteger num = Dot(Sub(A, O), N);
            if (num.Sign * den.Sign < 0) return ExactRayTriangle.Miss;
            // den X = den O + num D (den and the edge test's sign: multiply by sign(den))
            var X = Add(Mul(den, O), Mul(num, D));
            var V = new[] { A, B, C };
            for (int i = 0; i < 3; ++i) {
                var vi = V[i]; var vj = V[(i + 1) % 3];
                BigInteger s = Dot(Cross(Sub(vj, vi), Sub(X, Mul(den, vi))), N) * den.Sign;
                if (s.Sign < 0) return ExactRayTriangle.Miss;
            }
            return num.IsZero ? ExactRayTriangle.AtOrigin : ExactRayTriangle.Ahead;
        }

        // ---- seeded inputs: degenerate grids, rays aimed at edges and vertices, and the whole exponent range ----

        static double Signed(Random r, int e)
        {
            // a random double with 53, few or one significant bits, magnitude about 2^e (subnormal or normal)
            double m;
            switch (r.Next(3)) {
                case 0: m = 1 + r.NextDouble(); break;
                case 1: m = 1 + r.Next(16) / 16.0; break;
                default: m = 1; break;
            }
            double v = ExactPoint3.ScaleB(m, e);
            return r.Next(2) == 0 ? v : -v;
        }

        internal static IEnumerable<Vector3d[]> Cases(int seed, int count)
        {
            var r = new Random(seed);
            for (int it = 0; it < count; ++it) {
                int kind = it % 4;
                Vector3d o, d, a, b, c;
                if (kind == 0) {
                    // a small integer grid (many rays through vertices and along edges), scaled by 2^k over the range
                    Func<Vector3d> g = () => new Vector3d(r.Next(-3, 4), r.Next(-3, 4), r.Next(-3, 4));
                    o = g(); d = g(); a = g(); b = g(); c = g();
                    int k = r.Next(-1074, 1020);
                    Func<Vector3d, Vector3d> s = v => new Vector3d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k), ExactPoint3.ScaleB(v.z, k));
                    o = s(o); a = s(a); b = s(b); c = s(c);
                    d = s(d);
                } else if (kind == 1) {
                    // aimed at a vertex, an edge point or an interior point (the rounded direction passes near it)
                    int e = r.Next(-1074, 1000);
                    Func<Vector3d> p = () => new Vector3d(Signed(r, e - r.Next(4)), Signed(r, e - r.Next(4)), Signed(r, e - r.Next(4)));
                    a = p(); b = p(); c = p(); o = p();
                    double u = r.Next(3) == 0 ? 0 : r.NextDouble(), v = r.Next(3) == 0 ? 0 : r.NextDouble() * (1 - u);
                    Vector3d target = a + u * (b - a) + v * (c - a);
                    d = target - o;
                } else {
                    // independent random values with mixed exponents, subnormal ones included
                    int e = kind == 2 ? r.Next(-1074, -1000) : r.Next(-1074, 1000);
                    Func<Vector3d> p = () => new Vector3d(Signed(r, Math.Max(-1074, e - r.Next(8))), Signed(r, Math.Max(-1074, e - r.Next(8))), Signed(r, Math.Max(-1074, e - r.Next(8))));
                    a = p(); b = p(); c = p(); o = p(); d = p();
                }
                if (o.IsFinite && d.IsFinite && a.IsFinite && b.IsFinite && c.IsFinite && d != Vector3d.Zero)
                    yield return new[] { o, d, a, b, c };
            }
        }

        [Fact]
        public void RayHitClosedDirection_EqualsTheRationalOracle()
        {
            int hits = 0, boundary = 0, subnormal = 0, n = 0;
            foreach (var q in Cases(1672, 6000)) {
                Vector3d o = q[0], d = q[1], a = q[2], b = q[3], c = q[4];
                int expected = Oracle(o, d, a, b, c);
                Vector3d ld = ExactRayTriangle.LineDirection(o, d);
                int h = ExactRayTriangle.RayHitClosedDirection(o, ld, a, b, c);
                Assert.True(expected == h, $"o {o:R} d {d:R} a {a:R} b {b:R} c {c:R}: {h}, oracle {expected}");
                // the same with the unscaled direction, and the public API
                Assert.Equal(expected, ExactRayTriangle.RayHitClosedDirection(o, d, a, b, c));
                Assert.Equal(expected != ExactRayTriangle.Miss, IntersectionUtil.Intersects(o, d, a, b, c));
                n++;
                if (expected != ExactRayTriangle.Miss) {
                    hits++;
                    if (ExactPredicates.Orient3dDirection(o, d, a, b) == 0 || ExactPredicates.Orient3dDirection(o, d, b, c) == 0
                        || ExactPredicates.Orient3dDirection(o, d, c, a) == 0)
                        boundary++;
                }
                if (Math.Abs(o.x) < 2.2250738585072014e-308 && o.x != 0) subnormal++;
            }
            Assert.True(n > 5000 && hits > 500 && boundary > 100 && subnormal > 200, $"n {n}, hits {hits}, on edges {boundary}, subnormal {subnormal}");
        }

        // ---- the perturbation, explicitly (as ExactRayTriangleTests.EdgeSign_EqualsTheExplicitlyPerturbedDeterminant) ----

        static BigInteger[] Big(Vector3d v, int k, int ex, int ey, int ez)
        {
            BigInteger s = BigInteger.One << k;
            Func<double, int, BigInteger> c = (x, e) => new BigInteger(x) * s + (e >= 0 ? BigInteger.One << e : BigInteger.Zero);
            return new[] { c(v.x, ex), c(v.y, ey), c(v.z, ez) };
        }

        [Fact]
        public void EdgeSignDirection_EqualsTheExplicitlyPerturbedDeterminant()
        {
            // ε = 2^-20, scaled by 2^240: det[d + (ε, ε², ε³), a - o', b - o'], o' = o + (ε⁴, ε⁸, ε¹²), on a small grid;
            // antisymmetric in a, b; equal to EdgeSign(o, o + d, a, b) (o + d is exact here)
            var rnd = new Random(1672);
            int degenerate = 0, deep = 0;
            for (int it = 0; it < 20000; ++it) {
                Func<Vector3d> r = () => new Vector3d(rnd.Next(-2, 3), rnd.Next(-2, 3), rnd.Next(-2, 3));
                Vector3d o = r(), d = r(), a = r(), b = r();
                if (d == Vector3d.Zero) continue;
                int s = ExactRayTriangle.EdgeSignDirection(o, d, a, b);
                Assert.Equal(-s, ExactRayTriangle.EdgeSignDirection(o, d, b, a));
                Assert.Equal(ExactRayTriangle.EdgeSign(o, o + d, a, b), s);
                if (a == b) { Assert.Equal(0, s); continue; }
                if (ExactPredicates.Orient3dDirection(o, d, a, b) == 0) degenerate++;
                if ((a - o).Cross(b - o) == Vector3d.Zero) deep++;
                int k = 240;
                BigInteger[] O = Big(o, k, k - 80, k - 160, k - 240), D = Big(d, k, k - 20, k - 40, k - 60);
                BigInteger[] A = Big(a, k, -1, -1, -1), B = Big(b, k, -1, -1, -1);
                int expected = Dot(Cross(D, Sub(A, O)), Sub(B, O)).Sign;
                Assert.True(expected == s, $"o {o} d {d} a {a} b {b}: {s}, perturbed {expected}");
            }
            Assert.True(degenerate > 1000 && deep > 300, $"{degenerate}, {deep}");
        }

        [Fact]
        public void EdgeSignDirection_FarExponents_EqualsThePointFormWhereTheSecondPointIsExact()
        {
            // o + d exact (a grid scaled by 2^k): the direction form decides as the point form, also in the perturbed
            // stages, at subnormal and huge scales (the exact stages there: BigInteger)
            var rnd = new Random(31);
            for (int it = 0; it < 4000; ++it) {
                Func<Vector3d> r = () => new Vector3d(rnd.Next(-2, 3), rnd.Next(-2, 3), rnd.Next(-2, 3));
                Vector3d o = r(), d = r(), a = r(), b = r(), c = r();
                if (d == Vector3d.Zero) continue;
                int k = it % 2 == 0 ? rnd.Next(-1074, -1000) : rnd.Next(950, 1020);
                Func<Vector3d, Vector3d> s = v => new Vector3d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k), ExactPoint3.ScaleB(v.z, k));
                Assert.Equal(ExactRayTriangle.EdgeSign(o, o + d, a, b), ExactRayTriangle.EdgeSignDirection(s(o), s(d), s(a), s(b)));
                Assert.Equal(ExactRayTriangle.LineCrossing(o, o + d, a, b, c), ExactRayTriangle.LineCrossingDirection(s(o), s(d), s(a), s(b), s(c)));
                Assert.Equal(ExactRayTriangle.ClosedCrossing(o, o + d, a, b, c), ExactRayTriangle.ClosedCrossingDirection(s(o), s(d), s(a), s(b), s(c)));
            }
        }

        // ---- LineDirection ----

        [Fact]
        public void LineDirection_IsAnExactPositiveMultipleOfTheDirection()
        {
            var r = new Random(3);
            int scaled = 0, kept = 0;
            for (int it = 0; it < 6000; ++it) {
                int eo = r.Next(-1074, 1024), ed = r.Next(-1074, 1024);
                var o = new Vector3d(Signed(r, eo), Signed(r, Math.Max(-1074, eo - r.Next(60))), r.Next(4) == 0 ? 0 : Signed(r, eo));
                var d = new Vector3d(Signed(r, ed), Signed(r, Math.Max(-1074, ed - r.Next(1100))), r.Next(4) == 0 ? 0 : Signed(r, Math.Max(-1074, ed - r.Next(60))));
                if (o.IsFinite == false || d.IsFinite == false) continue;
                Vector3d ld = ExactRayTriangle.LineDirection(o, d);
                // ld = 2^j d exactly for one j: compare the integers at a common exponent
                var I = Integers(d, ld);
                BigInteger[] D = I[0], L = I[1];
                int j = -1;
                for (int c = 0; c < 3; ++c)
                    if (D[c].IsZero == false) { j = c; break; }
                Assert.True(L[j].Sign == D[j].Sign);
                for (int c = 0; c < 3; ++c)
                    Assert.True(L[c] * D[j] == D[c] * L[j], $"o {o:R} d {d:R}: {ld:R}");
                if (ld == d) kept++; else scaled++;
            }
            Assert.True(scaled > 1500 && kept > 150, $"{scaled}, {kept}");
            Assert.Equal(Vector3d.Zero, ExactRayTriangle.LineDirection(Vector3d.One, Vector3d.Zero));
            Assert.Equal(Vector3d.Zero, ExactRayTriangle.LineDirection(Vector3d.One, new Vector3d(double.NaN, 0, 1)));
            Assert.Equal(Vector3d.Zero, ExactRayTriangle.LineDirection(new Vector3d(double.PositiveInfinity, 0, 0), Vector3d.One));
            Assert.Equal(ExactRayTriangle.Miss, ExactRayTriangle.RayHitClosedDirection(Vector3d.Zero, Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ));
            Assert.Equal(ExactRayTriangle.Miss, ExactRayTriangle.RayHitPerturbedDirection(Vector3d.Zero, Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ));
        }

        // ---- the perturbed ray ----

        [Fact]
        public void RayHitPerturbedDirection_EqualsTheClosedHitOffTheEdges_AndThePointFormOnTheGrid()
        {
            int n = 0;
            foreach (var q in Cases(11, 4000)) {
                Vector3d o = q[0], d = q[1], a = q[2], b = q[3], c = q[4];
                int p = ExactRayTriangle.RayHitPerturbedDirection(o, d, a, b, c);
                bool general = ExactPredicates.Orient3dDirection(o, d, a, b) != 0 && ExactPredicates.Orient3dDirection(o, d, b, c) != 0
                    && ExactPredicates.Orient3dDirection(o, d, c, a) != 0 && ExactPredicates.Orient3d(a, b, c, o) != 0;
                if (general) {
                    Assert.Equal(Oracle(o, d, a, b, c), p);
                    n++;
                }
            }
            Assert.True(n > 2000, $"{n}");
            // on an integer grid o + d is exact: the direction form is the point form, perturbed stages included
            var rnd = new Random(5);
            for (int it = 0; it < 20000; ++it) {
                Func<Vector3d> r = () => new Vector3d(rnd.Next(-2, 3), rnd.Next(-2, 3), rnd.Next(-2, 3));
                Vector3d o = r(), d = r(), a = r(), b = r(), c = r();
                if (d == Vector3d.Zero) continue;
                Assert.Equal(ExactRayTriangle.RayHitPerturbed(o, o + d, a, b, c), ExactRayTriangle.RayHitPerturbedDirection(o, d, a, b, c));
                Assert.Equal(ExactRayTriangle.RayHitClosed(o, o + d, a, b, c), ExactRayTriangle.RayHitClosedDirection(o, d, a, b, c));
            }
        }

        // ---- invariance ----

        static Vector3d Scaled(Vector3d v, int k) => new Vector3d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k), ExactPoint3.ScaleB(v.z, k));

        // rays and triangles with coordinates in [-8, 8] (for the similarities that round)
        static IEnumerable<Vector3d[]> NormalRangeCases(int seed, int count)
        {
            var r = new Random(seed);
            Func<Vector3d> p = () => new Vector3d(r.NextDouble() * 16 - 8, r.NextDouble() * 16 - 8, r.NextDouble() * 16 - 8);
            for (int it = 0; it < count; ++it) {
                Vector3d o = p(), a = p(), b = p(), c = p();
                // half aimed into the triangle
                Vector3d d = it % 2 == 0 ? (a + b + c) / 3 + 0.3 * (p() / 8) - o : p();
                yield return new[] { o, d, a, b, c };
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RayHits_AllSimilarities_SameVerdict(string name)
        {
            // the seeded cases transformed: under 2^k (IsExact) every verdict (closed and perturbed, and the public
            // IntersectionUtil.Intersects) is the same bit for bit, and so it is at far 2^k scales where the
            // scaled values stay exact; under the others the clear hits (the hit point well inside: barycentrics
            // above 1/8) and the clear misses (well outside) stay
            Similarity sim = Similarity.ByName(name);
            int compared = 0;
            foreach (var q in sim.IsExact ? Cases(29, 2000) : NormalRangeCases(29, 2000)) {
                Vector3d o = q[0], d = q[1], a = q[2], b = q[3], c = q[4];
                int h = ExactRayTriangle.RayHitClosedDirection(o, ExactRayTriangle.LineDirection(o, d), a, b, c);
                int hp = ExactRayTriangle.RayHitPerturbedDirection(o, ExactRayTriangle.LineDirection(o, d), a, b, c);
                if (sim.IsExact) {
                    int k = (int)Math.Round(Math.Log(sim.Scale, 2));
                    foreach (int kk in new[] { k, k * 50, -k * 50 }) {
                        Vector3d so = Scaled(o, kk), sd = Scaled(d, kk), sa = Scaled(a, kk), sb = Scaled(b, kk), sc = Scaled(c, kk);
                        // (exact only while nothing is rounded to the subnormal grid or overflows)
                        if (Scaled(so, -kk) != o || Scaled(sd, -kk) != d || Scaled(sa, -kk) != a || Scaled(sb, -kk) != b || Scaled(sc, -kk) != c)
                            continue;
                        Assert.Equal(h, ExactRayTriangle.RayHitClosedDirection(so, ExactRayTriangle.LineDirection(so, sd), sa, sb, sc));
                        Assert.Equal(hp, ExactRayTriangle.RayHitPerturbedDirection(so, ExactRayTriangle.LineDirection(so, sd), sa, sb, sc));
                        Assert.Equal(h != ExactRayTriangle.Miss, IntersectionUtil.Intersects(so, sd, sa, sb, sc));
                        compared++;
                    }
                    continue;
                }
                // a well-conditioned case: the ray's clear hit or clear miss survives the rounding of the transform
                Vector3d n = (b - a).Cross(c - a);
                double dn = d.Dot(n);
                if (!(Math.Abs(dn) > 1e-3 * d.Length * n.Length))
                    continue;
                double t = (a - o).Dot(n) / dn;
                if (!(t > 1e-3 || t < -1e-3)) continue;
                Vector3d x = o + t * d;
                double w1 = (c - a).Cross(x - a).Dot(n) / n.LengthSquared * -1, w2 = (b - a).Cross(x - a).Dot(n) / n.LengthSquared;
                double w0 = 1 - w1 - w2;
                bool clearHit = t > 0 && w0 > 0.125 && w1 > 0.125 && w2 > 0.125;
                bool clearMiss = t < 0 || w0 < -0.125 || w1 < -0.125 || w2 < -0.125;
                if (clearHit == clearMiss) continue;
                Vector3d mo = sim.Apply(o), md = sim.ApplyDirection(d);
                bool moved = IntersectionUtil.Intersects(mo, md, sim.Apply(a), sim.Apply(b), sim.Apply(c));
                Assert.True(moved == clearHit, $"{name}: o {o:R} d {d:R} a {a:R} b {b:R} c {c:R}");
                Assert.Equal(clearHit, h != ExactRayTriangle.Miss);
                compared++;
            }
            Assert.True(compared > 300, $"{name}: {compared}");
        }

        [Fact]
        public void IntrRay3Triangle3_FarDirectionLengths_SameParameterScaledAndBarycentrics()
        {
            // t of the ray o + t d scales by 2^-j with the direction 2^j d (IntrRay3Triangle3.hit scales a direction
            // outside [2^-100, 2^100] by a power of two and t back); the barycentrics stay (direction 1 is the reference)
            var o = new Vector3d(0.25, 0.375, 3);
            var d = new Vector3d(0.125, -0.0625, -1);
            var tri = new Triangle3d(new Vector3d(-1, -1, 0), new Vector3d(2, -1, 0.5), new Vector3d(0, 2, -0.25));
            var r0 = new IntrRay3Triangle3(new Ray3d(o, d, true), tri);
            Assert.True(r0.Find());
            foreach (int j in new[] { -1000, -300, -101, 101, 200, 900 }) {
                var r = new IntrRay3Triangle3(new Ray3d(o, Scaled(d, j), true), tri);
                Assert.True(r.Find(), $"2^{j}");
                double t = ExactPoint3.ScaleB(r.RayParameter, j);
                Assert.True(Math.Abs(t - r0.RayParameter) <= 1e-14 * r0.RayParameter, $"2^{j}: t {r.RayParameter:R}, {t:R} vs {r0.RayParameter:R}");
                Assert.True((r.TriangleBaryCoords - r0.TriangleBaryCoords).MaxAbs <= 1e-14, $"2^{j}: {r.TriangleBaryCoords}");
                Assert.Equal(r0.RayParameter, t);
            }
        }

        [Fact]
        public void IssueRepro_SameVerdictOverTheWholeRangeOfScales()
        {
            // #1672: every 2^k copy of the subnormal repro hits, and the tree / linear-search route agrees on a mesh
            double e = double.Epsilon;
            var o = new Vector3d(e, 0, 0); var d = new Vector3d(e, 4 * e, 0);
            var a = new Vector3d(2 * e, 8 * e, -2 * e); var b = new Vector3d(5 * e, 8 * e, -2 * e); var c = new Vector3d(2 * e, 8 * e, 2 * e);
            for (int k = 0; k <= 2094; k += 3) {
                Vector3d so = Scaled(o, k), sd = Scaled(d, k);
                Assert.Equal(ExactRayTriangle.Ahead, ExactRayTriangle.RayHitClosedDirection(so, ExactRayTriangle.LineDirection(so, sd), Scaled(a, k), Scaled(b, k), Scaled(c, k)));
                Assert.Equal(ExactRayTriangle.Ahead, ExactRayTriangle.RayHitPerturbedDirection(so, ExactRayTriangle.LineDirection(so, sd), Scaled(a, k), Scaled(b, k), Scaled(c, k)));
            }
            // a normal-range copy through the tree (normalized direction: the same line up to rounding; at 2^1074 the
            // coordinates are small integers and (1, 4, 0) / sqrt(17) still crosses the interior)
            int s = 1074;
            var mesh = new DMesh3();
            mesh.AppendVertex(Scaled(a, s)); mesh.AppendVertex(Scaled(b, s)); mesh.AppendVertex(Scaled(c, s));
            mesh.AppendTriangle(0, 1, 2);
            var tree = new DMeshAABBTree3(mesh, true);
            var ray = new Ray3d(Scaled(o, s), Scaled(d, s).Normalized, true);
            Assert.Equal(0, tree.FindNearestHitTriangle(ray));
            Assert.Equal(0, MeshQueries.FindHitTriangle_LinearSearch(mesh, ray));
            Assert.Equal(1, tree.CountCrossings(ray.Origin, ray.Direction));
        }
    }
}
