using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PolyhedralEnvelope (docs/design/ENVELOPE.md section 4.4): the bounds of the construction checked a posteriori
    /// in exact and high-precision arithmetic, and the queries against independent exact oracles (an exact determinant
    /// for points, the covering of the parameter interval by the polyhedra's open intervals for segments).
    /// </summary>
    public class PolyhedralEnvelopeTests
    {
        const double U = 1.1102230246251565e-16;

        // ------------------------------------------------------------ exact arithmetic for the oracles

        // a dyadic rational M 2^E
        struct Dy
        {
            public BigInteger M; public int E;
            public static Dy Of(double x)
            {
                if (x == 0) return new Dy() { M = 0, E = 0 };
                long bits = BitConverter.DoubleToInt64Bits(x);
                int be = (int)((bits >> 52) & 0x7FF);
                long m = bits & 0xFFFFFFFFFFFFFL;
                int e;
                if (be == 0) e = -1074; else { m |= 1L << 52; e = be - 1075; }
                return new Dy() { M = x < 0 ? -m : m, E = e };
            }
            public static Dy operator *(Dy a, Dy b) => new Dy() { M = a.M * b.M, E = a.E + b.E };
            public static Dy operator -(Dy a) => new Dy() { M = -a.M, E = a.E };
            public static Dy operator +(Dy a, Dy b)
            {
                if (a.M.IsZero) return b;
                if (b.M.IsZero) return a;
                int e = Math.Min(a.E, b.E);
                return new Dy() { M = (a.M << (a.E - e)) + (b.M << (b.E - e)), E = e };
            }
            public static Dy operator -(Dy a, Dy b) => a + (-b);
            public int Sign => M.Sign;
            public static int Compare(Dy a, Dy b) => (a - b).Sign;
        }

        static Dy[] D(Vector3d v) => new[] { Dy.Of(v.x), Dy.Of(v.y), Dy.Of(v.z) };
        static Dy[] Sub(Dy[] a, Dy[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static Dy Dot(Dy[] a, Dy[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static Dy[] Cross(Dy[] a, Dy[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        // exact det(q1 - q0, q2 - q0, x - q0): the sign of ExactPredicates.Orient3d(q0, q1, q2, x)
        static Dy Det(Vector3d q0, Vector3d q1, Vector3d q2, Vector3d x)
        {
            Dy[] o = D(q0);
            return Dot(Cross(Sub(D(q1), o), Sub(D(q2), o)), Sub(D(x), o));
        }

        // ------------------------------------------------------------ high-precision unit vectors (fixed point 2^-P)

        const int P = 400;

        static BigInteger ISqrt(BigInteger n)
        {
            if (n.Sign <= 0) return 0;
            BigInteger x = BigInteger.One << (int)((n.ToByteArray().Length * 8) / 2 + 1);
            while (true) {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        // the integers of exact vector components at one exponent
        static BigInteger[] Ints(Dy[] v)
        {
            int e = v.Where(d => d.M.IsZero == false).Select(d => d.E).DefaultIfEmpty(0).Min();
            return v.Select(d => d.M.IsZero ? BigInteger.Zero : d.M << (d.E - e)).ToArray();
        }

        // v / |v| in fixed point (times 2^P), error a few units of 2^-P
        static BigInteger[] Normalize(BigInteger[] X)
        {
            BigInteger n2 = X[0] * X[0] + X[1] * X[1] + X[2] * X[2];
            BigInteger r = ISqrt(n2 << (2 * P));       // |X| 2^P
            return X.Select(x => (x << (2 * P)) / r).ToArray();
        }

        static BigInteger[] Fixed(Vector3d v) => D(v).Select(d => d.E + P >= 0 ? d.M << (d.E + P) : d.M >> -(d.E + P)).ToArray();

        // |m~ - m*| <= bound, m* in fixed point
        static bool Within(Vector3d m, BigInteger[] mstar, double bound)
        {
            BigInteger[] f = Fixed(m);
            BigInteger s = 0;
            for (int i = 0; i < 3; ++i) { BigInteger d = f[i] - mstar[i]; s += d * d; }
            // compare |d|^2 2^(2P) with bound^2 2^(2P), bound^2 as a dyadic
            Dy b2 = Dy.Of(bound) * Dy.Of(bound);
            BigInteger rhs = b2.E + 2 * P >= 0 ? b2.M << (b2.E + 2 * P) : b2.M >> -(b2.E + 2 * P);
            return s + 16 <= rhs;
        }

        static BigInteger[] CrossFixed(BigInteger[] a, BigInteger[] b) => new[] {
            a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        // the ideal directions of the polyhedron of (v0, v1, v2), in the order of the builder (ENVELOPE.md 4.4), with
        // the derived theta (without the builder's slack of one u)
        static List<(BigInteger[] m, double theta)> IdealDirections(Vector3d v0, Vector3d v1, Vector3d v2)
        {
            var r = new List<(BigInteger[], double)>();
            Vector3d[] v = { v0, v1, v2 };
            Dy[] a0 = D(v0), a1 = D(v1), a2 = D(v2);
            Dy[] N = Cross(Sub(a1, a0), Sub(a2, a0));
            BigInteger[] Ni = Ints(N);
            if (Ni.Any(x => x.IsZero == false)) {
                BigInteger[] n = Normalize(Ni);
                r.Add((n, 8 * U));
                r.Add((n.Select(x => -x).ToArray(), 8 * U));
                Dy[][] e = { Sub(a1, a0), Sub(a2, a1), Sub(a0, a2) };
                for (int k = 0; k < 3; ++k) {
                    BigInteger[] ek = Ints(e[k]);
                    r.Add((Normalize(CrossFixed(ek, n)), 28 * U));
                }
                for (int i = 0; i < 3; ++i) {
                    Vector3d a = v[(i + 1) % 3] - v[i], b = v[(i + 2) % 3] - v[i];
                    double sc = Math.Max(a.MaxAbs, b.MaxAbs);
                    a /= sc; b /= sc;       // (only for the decision, as the builder's power-of-two scaling)
                    if (!(a.Dot(b) >= -(a.LengthSquared + b.LengthSquared) / 4)) continue;
                    // -(a + b) = 2 v_i - v_{i+1} - v_{i+2}
                    Dy[] m = { Dy.Of(2) * D(v[i])[0] - D(v[(i + 1) % 3])[0] - D(v[(i + 2) % 3])[0],
                               Dy.Of(2) * D(v[i])[1] - D(v[(i + 1) % 3])[1] - D(v[(i + 2) % 3])[1],
                               Dy.Of(2) * D(v[i])[2] - D(v[(i + 1) % 3])[2] - D(v[(i + 2) % 3])[2] };
                    r.Add((Normalize(Ints(m)), 10 * U));
                }
            } else {
                Vector3d[] ed = { v1 - v0, v2 - v1, v0 - v2 };
                Dy[][] e = { Sub(a1, a0), Sub(a2, a1), Sub(a0, a2) };
                int L = 0;
                for (int k = 1; k < 3; ++k) if (ed[k].LengthSquared > ed[L].LengthSquared) L = k;
                BigInteger[] eL = Ints(e[L]);
                if (eL.Any(x => x.IsZero == false)) {
                    BigInteger[] d = Normalize(eL);
                    Vector3d dd; PolyhedralEnvelope.unit(ed[L], out dd);
                    Vector3d axis = Math.Abs(dd.x) <= Math.Abs(dd.y) && Math.Abs(dd.x) <= Math.Abs(dd.z) ? Vector3d.AxisX
                        : Math.Abs(dd.y) <= Math.Abs(dd.z) ? Vector3d.AxisY : Vector3d.AxisZ;
                    BigInteger[] ax = Fixed(axis);
                    BigInteger[] p1 = Normalize(CrossFixed(d, ax));
                    BigInteger[] p2 = Normalize(CrossFixed(d, p1));
                    r.Add((d, 6 * U)); r.Add((d.Select(x => -x).ToArray(), 6 * U));
                    r.Add((p1, 19 * U)); r.Add((p1.Select(x => -x).ToArray(), 19 * U));
                    r.Add((p2, 60 * U)); r.Add((p2.Select(x => -x).ToArray(), 60 * U));
                } else {
                    foreach (Vector3d ax in new[] { Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ }) {
                        r.Add((Fixed(ax), 0)); r.Add((Fixed(-ax), 0));
                    }
                }
            }
            return r;
        }

        // ------------------------------------------------------------ test triangles

        static IEnumerable<Vector3d[]> TestTriangles(int seed)
        {
            var rand = new Random(seed);
            Func<double> g = () => rand.NextDouble() * 2 - 1;
            Func<Vector3d> pt = () => new Vector3d(g(), g(), g());
            for (int i = 0; i < 40; ++i) yield return new[] { pt(), pt(), pt() };
            // right angles (a cap is required there), nearly right, obtuse, equilateral
            yield return new[] { Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY };
            yield return new[] { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(1e-17, 1, 0) };
            yield return new[] { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(-1e-17, 1, 0) };
            yield return new[] { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(-0.9, 0.1, 0) };
            yield return new[] { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(0.5, Math.Sqrt(0.75), 0) };
            // slivers: nearly collinear, a tiny edge
            for (int i = 0; i < 10; ++i) {
                Vector3d a = pt(), b = pt();
                double t = rand.NextDouble();
                Vector3d c = a + t * (b - a) + Math.Pow(10, -3 - 2 * i) * pt();
                yield return new[] { a, b, c };
                yield return new[] { a, a + Math.Pow(10, -3 - 2 * i) * pt(), b };
            }
            // degenerate: exactly collinear (dyadic), two equal vertices, a point
            yield return new[] { Vector3d.Zero, new Vector3d(1, 2, 3), new Vector3d(0.5, 1, 1.5) };
            yield return new[] { new Vector3d(0.25, -1, 3), new Vector3d(0.25, -1, 3), new Vector3d(2, 0.5, 1) };
            yield return new[] { new Vector3d(0.25, -1, 3), new Vector3d(0.25, -1, 3), new Vector3d(0.25, -1, 3) };
            yield return new[] { new Vector3d(1, 1, 1), new Vector3d(3, 3, 3), new Vector3d(2, 2, 2) };
        }

        static IEnumerable<(Vector3d[] t, double eps)> TestCases()
        {
            int seed = 0;
            foreach (double scale in new[] { 1.0, 1.0 / 1024, 1e-150, 1e150 })
                foreach (Vector3d off in new[] { Vector3d.Zero, new Vector3d(632117.25, 5712043.5, 113.75) })
                    foreach (double eps in new[] { 1e-3, 0.3, 50 }) {
                        if (off != Vector3d.Zero && scale != 1) continue;
                        foreach (Vector3d[] t in TestTriangles(seed++))
                            yield return (t.Select(p => scale * p + off).ToArray(), eps * scale);
                    }
        }

        static PolyhedralEnvelope.Builder Build(Vector3d[] t, double eps, out Vector3d[] pts, out int nf, out EnvelopeStatus st)
        {
            var b = new PolyhedralEnvelope.Builder(eps, t[0], t[1], t[2]);
            b.Log = new List<PolyhedralEnvelope.Builder.Facet>();
            pts = new Vector3d[PolyhedralEnvelope.MaxFacets * 3];
            st = b.Build(pts, 0, out nf);
            return b;
        }

        /// <summary>
        /// Every quantity of the proof (ENVELOPE.md 4.4) checked a posteriori: delta &lt;= eps / sqrt 3, rho and R are
        /// upper bounds, each direction within its derived theta of the exact ideal direction (high precision), the
        /// logged mu a true bound of the points' plane residuals (exact), Lambda over the ball, and the final offset
        /// inequality s &lt;= h_T(m) + delta - theta (R + rho) - Lambda mu exactly. The triangle strictly inside.
        /// </summary>
        [Fact]
        public void Polyhedron_BoundsHold_APosteriori()
        {
            int ready = 0, cases = 0;
            foreach ((Vector3d[] t, double eps) in TestCases()) {
                cases++;
                var b = Build(t, eps, out Vector3d[] pts, out int nf, out EnvelopeStatus st);
                if (st != EnvelopeStatus.Ready) {
                    // only for an epsilon tiny against the coordinates (far from the origin)
                    Assert.True(eps < 1e-6 * t.Max(p => p.MaxAbs), $"{st} for eps {eps}, {t[0]} {t[1]} {t[2]}");
                    continue;
                }
                ready++;
                // delta <= eps / sqrt 3, rho >= |v_k - v0|, R >= rho + 2 eps
                Assert.True(Dy.Compare(Dy.Of(3) * Dy.Of(b.delta) * Dy.Of(b.delta), Dy.Of(eps) * Dy.Of(eps)) <= 0);
                foreach (Vector3d q in t) {
                    Dy[] d = Sub(D(q), D(t[0]));
                    Assert.True(Dy.Compare(Dot(d, d), Dy.Of(b.rho) * Dy.Of(b.rho)) <= 0);
                }
                Assert.True(Dy.Compare(Dy.Of(b.rho) + Dy.Of(2) * Dy.Of(eps), Dy.Of(b.R)) <= 0);

                var ideal = IdealDirections(t[0], t[1], t[2]);
                Assert.Equal(ideal.Count, nf);
                Assert.Equal(nf, b.Log.Count);
                for (int f = 0; f < nf; ++f) {
                    var log = b.Log[f];
                    Assert.True(Within(log.M, ideal[f].m, ideal[f].theta + 1e-30), $"facet {f}: direction beyond theta {ideal[f].theta / U} u");
                    Assert.True(ideal[f].theta <= log.Theta);
                    Vector3d q0 = pts[3 * f], q1 = pts[3 * f + 1], q2 = pts[3 * f + 2];
                    Dy[] m = D(log.M);
                    Dy s = Dy.Of(log.S);
                    // mu: a true bound of |m . q_k - s|
                    Dy mu = Dy.Of(0);
                    foreach (Vector3d q in new[] { q0, q1, q2 }) {
                        Dy r = Dot(m, D(q)) - s;
                        if (r.Sign < 0) r = -r;
                        if (Dy.Compare(r, mu) > 0) mu = r;
                    }
                    Assert.True(Dy.Compare(mu, Dy.Of(log.Mu)) <= 0, $"facet {f}: mu bound");
                    // the offset inequality, exactly, with the true mu
                    Dy h = t.Select(q => Dot(m, D(q))).Aggregate((x, y) => Dy.Compare(x, y) >= 0 ? x : y);
                    Dy rhs = h + Dy.Of(b.delta) - Dy.Of(log.Theta) * (Dy.Of(b.R) + Dy.Of(b.rho)) - Dy.Of(2) * mu;
                    Assert.True(Dy.Compare(s, rhs) <= 0, $"facet {f}: offset inequality");
                    // Lambda over the ball B(v0, R): sum |lambda_k| on the projected triangle
                    Assert.True(LambdaMax(log.M, q0, q1, q2, t[0], b.R) <= 1.8, $"facet {f}: Lambda");
                    // the facet keeps at least 0.9 delta
                    Assert.True(log.Margin <= b.delta / 10);
                    // the triangle strictly inside
                    foreach (Vector3d q in t) Assert.True(Det(q0, q1, q2, q).Sign < 0);
                }
            }
            Assert.True(ready > 0.9 * cases, $"{ready} of {cases} ready");
        }

        // max over the ball B(c, R) of sum_k |lambda_k(x)|, lambda the barycentric coordinates of the projection of x
        // along m onto the plane of q0, q1, q2 (double; the value is far from the bound 2)
        static double LambdaMax(Vector3d m, Vector3d q0, Vector3d q1, Vector3d q2, Vector3d c, double R)
        {
            // (scale free: relative to q0, in units of R)
            q1 = (q1 - q0) / R; q2 = (q2 - q0) / R; c = (c - q0) / R; q0 = Vector3d.Zero; R = 1;
            Vector3d mu = m.Normalized;
            Vector3d a = (q1 - q0); a -= a.Dot(mu) * mu; a.Normalize();
            Vector3d b = mu.Cross(a);
            Func<Vector3d, Vector2d> pr = x => new Vector2d((x - q0).Dot(a), (x - q0).Dot(b));
            Vector2d p1 = pr(q1), p2 = pr(q2), pc = pr(c);
            double det = p1.x * p2.y - p1.y * p2.x;
            // lambda1 = (x p2.y - y p2.x) / det, lambda2 = (p1.x y - p1.y x) / det, lambda0 = 1 - lambda1 - lambda2
            Vector2d g1 = new Vector2d(p2.y, -p2.x) / det, g2 = new Vector2d(-p1.y, p1.x) / det, g0 = -(g1 + g2);
            double max = 0;
            const int K = 3600;
            for (int k = 0; k < K; ++k) {
                double phi = 2 * Math.PI * k / K;
                Vector2d x = pc + R * new Vector2d(Math.Cos(phi), Math.Sin(phi));
                double l1 = g1.Dot(x), l2 = g2.Dot(x), l0 = 1 - l1 - l2;
                max = Math.Max(max, Math.Abs(l0) + Math.Abs(l1) + Math.Abs(l2));
            }
            // between samples the value grows by at most R (2 pi / K) times the gradients' lengths
            return max + R * (2 * Math.PI / K) * (g0.Length + g1.Length + g2.Length);
        }

        /// <summary>
        /// The vertices of each polyhedron (triple intersections of its planes in double that satisfy all of them) are
        /// within epsilon of the triangle (the guarantee, up to the double rounding of the check), and the polyhedron is
        /// not much smaller than the paper's (some vertex beyond 0.85 epsilon for a non-degenerate triangle).
        /// </summary>
        [Fact]
        public void Polyhedron_VerticesWithinEpsilon()
        {
            foreach ((Vector3d[] t, double eps) in TestCases()) {
                // the double check needs coordinates near eps, and no tiny or huge values (Vector3d.Normalized)
                if (t.Max(p => p.MaxAbs) > 1e3 * eps || eps < 1e-10 || eps > 1e10) continue;
                Build(t, eps, out Vector3d[] pts, out int nf, out EnvelopeStatus st);
                Assert.Equal(EnvelopeStatus.Ready, st);
                var planes = new List<(Vector3d n, double d)>();
                for (int f = 0; f < nf; ++f) {
                    Vector3d n = (pts[3 * f + 1] - pts[3 * f]).Cross(pts[3 * f + 2] - pts[3 * f]).Normalized;
                    planes.Add((n, n.Dot(pts[3 * f])));
                }
                double maxd = 0, worst = double.MaxValue;
                var tri = new Triangle3d(t[0], t[1], t[2]);
                for (int i = 0; i < nf; ++i) for (int j = i + 1; j < nf; ++j) for (int k = j + 1; k < nf; ++k) {
                    var M = new Matrix3d(planes[i].n, planes[j].n, planes[k].n, true);
                    if (Math.Abs(M.Determinant) < 1e-9) continue;
                    Vector3d x = M.Inverse() * new Vector3d(planes[i].d, planes[j].d, planes[k].d);
                    worst = Math.Min(worst, planes.Max(p => p.n.Dot(x) - p.d) / eps);
                    if (planes.All(p => p.n.Dot(x) - p.d <= 1e-9 * eps)) {
                        double d = Math.Sqrt(new DistPoint3Triangle3(x, tri).GetSquared());
                        Assert.True(d <= eps * (1 + 1e-6), $"vertex at {d / eps} eps");
                        maxd = Math.Max(maxd, d);
                    }
                }
                Assert.True(maxd >= 0.85 * eps, $"largest vertex distance {maxd / eps} eps, nf {nf}, worst {worst}, eps {eps}, {t[0]} {t[1]} {t[2]}");
            }
        }

        [Fact]
        public void Status_Cases()
        {
            DMesh3 m = Soup(new[] { new[] { Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY } });
            Assert.Equal(EnvelopeStatus.InvalidEpsilon, new PolyhedralEnvelope(m, 0).Status);
            Assert.Equal(EnvelopeStatus.InvalidEpsilon, new PolyhedralEnvelope(m, double.NaN).Status);
            Assert.Equal(EnvelopeStatus.InvalidEpsilon, new PolyhedralEnvelope(m, double.PositiveInfinity).Status);
            Assert.Equal(EnvelopeStatus.Empty, new PolyhedralEnvelope(new DMesh3(), 1).Status);
            Assert.Equal(EnvelopeStatus.TooThin, new PolyhedralEnvelope(m, 1e-290).Status);
            Assert.Equal(EnvelopeStatus.TooThin, new PolyhedralEnvelope(m, 1e-15).Status);
            Assert.Equal(EnvelopeStatus.Ready, new PolyhedralEnvelope(m, 1e-11).Status);
            Assert.Equal(EnvelopeStatus.OutOfRange, new PolyhedralEnvelope(Soup(new[] { new[] {
                new Vector3d(-1e308, 0, 0), new Vector3d(1e308, 0, 0), Vector3d.AxisY } }), 1).Status);
            var env = new PolyhedralEnvelope(m, 1e-15);
            Assert.False(env.Contains(Vector3d.Zero));
            Assert.False(env.ContainsSegment(Vector3d.Zero, Vector3d.AxisX));
        }

        // ------------------------------------------------------------ queries against oracles

        static DMesh3 Soup(IEnumerable<Vector3d[]> tris)
        {
            var m = new DMesh3();
            foreach (Vector3d[] t in tris) {
                int a = m.AppendVertex(t[0]), b = m.AppendVertex(t[1]), c = m.AppendVertex(t[2]);
                m.AppendTriangle(a, b, c);
            }
            return m;
        }

        static DMesh3 Sphere(int n) => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();

        static bool OracleInside(PolyhedralEnvelope env, int p, Vector3d x)
        {
            for (int f = 0; f < env.FacetCount(p); ++f) {
                env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                if (Det(q0, q1, q2, x).Sign >= 0) return false;
            }
            return true;
        }

        static bool OracleContains(PolyhedralEnvelope env, Vector3d x)
        {
            for (int p = 0; p < env.PolyhedronCount; ++p) if (OracleInside(env, p, x)) return true;
            return false;
        }

        // a rational n / d, d > 0
        struct Q
        {
            public BigInteger N, D;
            public static int Cmp(Q a, Q b) => (a.N * b.D).CompareTo(b.N * a.D);
            public static readonly Q Zero = new Q() { N = 0, D = 1 }, One = new Q() { N = 1, D = 1 };
        }

        static BigInteger[] AlignPair(Dy a, Dy b)
        {
            int e = Math.Min(a.M.IsZero ? int.MaxValue : a.E, b.M.IsZero ? int.MaxValue : b.E);
            if (e == int.MaxValue) e = 0;
            return new[] { a.M.IsZero ? 0 : a.M << (a.E - e), b.M.IsZero ? 0 : b.M << (b.E - e) };
        }

        // the open interval (relative to [0, 1]) of t with a + t (b - a) strictly inside polyhedron p, or null
        static (Q lo, bool loOpen, Q hi, bool hiOpen)? Interval(PolyhedralEnvelope env, int p, Vector3d a, Vector3d b)
        {
            Q lo = Q.Zero, hi = Q.One;
            bool loOpen = false, hiOpen = false;
            for (int f = 0; f < env.FacetCount(p); ++f) {
                env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                // the determinant is affine along the segment: f(t) = f0 + t (f1 - f0)
                BigInteger[] F = AlignPair(Det(q0, q1, q2, a), Det(q0, q1, q2, b));
                BigInteger f0 = F[0], f1 = F[1];
                if (f0.Sign < 0 && f1.Sign < 0) continue;
                if (f0.Sign >= 0 && f1.Sign >= 0) return null;
                var tau = new Q() { N = f0, D = f0 - f1 };
                if (tau.D.Sign < 0) { tau.N = -tau.N; tau.D = -tau.D; }
                if (f0.Sign < 0) {      // t < tau
                    int c = Q.Cmp(tau, hi);
                    if (c < 0 || (c == 0 && hiOpen == false)) { hi = tau; hiOpen = true; }
                } else {                // t > tau
                    int c = Q.Cmp(tau, lo);
                    if (c > 0 || (c == 0 && loOpen == false)) { lo = tau; loOpen = true; }
                }
            }
            int k = Q.Cmp(lo, hi);
            if (k > 0 || (k == 0 && (loOpen || hiOpen))) return null;
            return (lo, loOpen, hi, hiOpen);
        }

        // is [0, 1] covered by the polyhedra's intervals? (they are relatively open in [0, 1]: a sweep)
        static bool OracleSegment(PolyhedralEnvelope env, Vector3d a, Vector3d b)
        {
            var iv = new List<(Q lo, bool loOpen, Q hi, bool hiOpen)>();
            for (int p = 0; p < env.PolyhedronCount; ++p) {
                var i = Interval(env, p, a, b);
                if (i != null) iv.Add(i.Value);
            }
            Q cur = Q.Zero;
            while (true) {
                bool found = false;
                Q best = cur; bool bestOpen = true;
                foreach (var i in iv) {
                    int cl = Q.Cmp(i.lo, cur), ch = Q.Cmp(cur, i.hi);
                    bool containsCur = (cl < 0 || (cl == 0 && i.loOpen == false)) && (ch < 0 || (ch == 0 && i.hiOpen == false));
                    if (containsCur == false) continue;
                    int c = Q.Cmp(i.hi, best);
                    if (found == false || c > 0 || (c == 0 && i.hiOpen == false)) { best = i.hi; bestOpen = i.hiOpen; found = true; }
                }
                if (found == false) return false;
                if (bestOpen == false) return true;     // closed at 1
                cur = best;
            }
        }

        static IEnumerable<(DMesh3 mesh, double eps)> QueryMeshes()
        {
            yield return (Sphere(4), 0.05);
            yield return (Sphere(6), 0.02);
            var rand = new Random(7);
            Func<double> g = () => rand.NextDouble() * 2 - 1;
            var tris = new List<Vector3d[]>();
            for (int i = 0; i < 12; ++i) {
                var c = new Vector3d(g(), g(), g()) * 0.5;
                tris.Add(new[] { c, c + 0.4 * new Vector3d(g(), g(), g()), c + 0.4 * new Vector3d(g(), g(), g()) });
            }
            // a degenerate segment and a point triangle in the soup
            tris.Add(new[] { new Vector3d(0.1, 0.2, 0.3), new Vector3d(0.5, 0.2, 0.3), new Vector3d(0.3, 0.2, 0.3) });
            tris.Add(new[] { new Vector3d(-0.5, 0.25, 0), new Vector3d(-0.5, 0.25, 0), new Vector3d(-0.5, 0.25, 0) });
            yield return (Soup(tris), 0.04);
        }

        static IEnumerable<Vector3d> QueryPoints(PolyhedralEnvelope env, Random rand)
        {
            DMesh3 m = env.Mesh;
            Func<double> g = () => rand.NextDouble() * 2 - 1;
            double eps = env.Epsilon;
            foreach (int v in m.VertexIndices()) yield return m.GetVertex(v);
            foreach (int t in m.TriangleIndices()) {
                Vector3d c = m.GetTriCentroid(t);
                yield return c;
                for (int k = 0; k < 3; ++k) yield return c + eps * new Vector3d(g(), g(), g());
            }
            // the facets' own points and points near facets (on the boundary or across it)
            for (int p = 0; p < env.PolyhedronCount; p += 3)
                for (int f = 0; f < env.FacetCount(p); ++f) {
                    env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                    yield return q0;
                    yield return q0 + 1e-14 * new Vector3d(g(), g(), g());
                }
        }

        [Fact]
        public void Contains_MatchesOracle()
        {
            var rand = new Random(3);
            foreach ((DMesh3 mesh, double eps) in QueryMeshes()) {
                var env = new PolyhedralEnvelope(mesh, eps);
                Assert.Equal(EnvelopeStatus.Ready, env.Status);
                int inside = 0, n = 0;
                foreach (Vector3d x in QueryPoints(env, rand)) {
                    bool r = env.Contains(x);
                    Assert.Equal(OracleContains(env, x), r);
                    if (r) inside++;
                    n++;
                }
                Assert.True(inside > n / 4 && inside < n, $"{inside} of {n} inside");
                foreach (int v in mesh.VertexIndices()) Assert.True(env.Contains(mesh.GetVertex(v)));
            }
        }

        static IEnumerable<(Vector3d a, Vector3d b)> QuerySegments(PolyhedralEnvelope env, Random rand)
        {
            DMesh3 m = env.Mesh;
            Func<double> g = () => rand.NextDouble() * 2 - 1;
            double eps = env.Epsilon;
            var tids = m.TriangleIndices().ToList();
            foreach (int t in tids) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                yield return (a, b);                                    // mesh edges
                yield return ((a + b) / 2, c);
                yield return (a + eps * new Vector3d(g(), g(), g()), c + eps * new Vector3d(g(), g(), g()));
            }
            for (int i = 0; i < 60; ++i) {                              // across several triangles
                int t0 = tids[rand.Next(tids.Count)], t1 = tids[rand.Next(tids.Count)];
                Vector3d a = m.GetTriCentroid(t0), b = m.GetTriCentroid(t1);
                if ((a - b).Length < 0.8) yield return (a, b);
                yield return (a, a + 0.1 * new Vector3d(g(), g(), g()));
            }
            // segments in a facet plane and from a facet point (exact boundary cases)
            for (int p = 0; p < env.PolyhedronCount; p += 5)
                for (int f = 0; f < env.FacetCount(p); f += 2) {
                    env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                    Vector3d c = env.Mesh.GetTriCentroid(tids[p]);
                    yield return (q0, c);
                    yield return (q0, q0 + (q1 - q0) * 1e-3);
                }
            // a point segment
            yield return (m.GetTriCentroid(tids[0]), m.GetTriCentroid(tids[0]));
        }

        [Fact]
        public void ContainsSegment_MatchesIntervalOracle()
        {
            var rand = new Random(5);
            foreach ((DMesh3 mesh, double eps) in QueryMeshes()) {
                var env = new PolyhedralEnvelope(mesh, eps);
                int yes = 0, n = 0;
                foreach ((Vector3d a, Vector3d b) in QuerySegments(env, rand)) {
                    bool r = env.ContainsSegment(a, b);
                    Assert.True(OracleSegment(env, a, b) == r, $"segment {a} - {b}: {r}");
                    if (r) { yes++; Assert.True(env.Contains(a) && env.Contains(b)); }
                    n++;
                }
                Assert.True(yes > n / 4 && yes < n, $"{yes} of {n} contained");
                foreach (int t in mesh.TriangleIndices()) {
                    Index3i tv = mesh.GetTriangle(t);
                    Assert.True(env.ContainsSegment(mesh.GetVertex(tv.a), mesh.GetVertex(tv.b)));
                }
            }
        }

        /// <summary>
        /// 2^k scales give the same answers and the same facet points, scaled, bit for bit; the other similarities give
        /// the same answers on queries well inside (half delta from the mesh) or well outside (beyond epsilon).
        /// </summary>
        [Fact]
        public void Invariance_AllSimilarities()
        {
            DMesh3 mesh = Sphere(5);
            double eps = 0.03;
            var env = new PolyhedralEnvelope(mesh, eps);
            var rand = new Random(11);
            var pts = QueryPoints(env, rand).ToList();
            var segs = QuerySegments(env, new Random(12)).ToList();
            bool[] rp = pts.Select(env.Contains).ToArray();
            bool[] rs = segs.Select(s => env.ContainsSegment(s.a, s.b)).ToArray();
            var spatial = new DMeshAABBTree3(mesh, true);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 m2 = new DMesh3(mesh);
                foreach (int v in m2.VertexIndices()) m2.SetVertex(v, sim.Apply(m2.GetVertex(v)));
                var e2 = new PolyhedralEnvelope(m2, sim.ApplyLength(eps));
                Assert.Equal(EnvelopeStatus.Ready, e2.Status);
                if (sim.IsExact) {
                    for (int p = 0; p < env.PolyhedronCount; ++p) {
                        Assert.Equal(env.FacetCount(p), e2.FacetCount(p));
                        for (int f = 0; f < env.FacetCount(p); ++f) {
                            env.GetFacet(p, f, out Vector3d a0, out Vector3d a1, out Vector3d a2);
                            e2.GetFacet(p, f, out Vector3d b0, out Vector3d b1, out Vector3d b2);
                            Assert.Equal(sim.Apply(a0), b0); Assert.Equal(sim.Apply(a1), b1); Assert.Equal(sim.Apply(a2), b2);
                        }
                    }
                    for (int i = 0; i < pts.Count; ++i) Assert.Equal(rp[i], e2.Contains(sim.Apply(pts[i])));
                    for (int i = 0; i < segs.Count; ++i) Assert.Equal(rs[i], e2.ContainsSegment(sim.Apply(segs[i].a), sim.Apply(segs[i].b)));
                } else {
                    double delta = eps / Math.Sqrt(3);
                    int checkedCount = 0;
                    for (int i = 0; i < pts.Count; ++i) {
                        double d = Math.Sqrt(MeshQueries.TriDistanceSqr(mesh, spatial.FindNearestTriangle(pts[i]), pts[i]));
                        if (d < 0.5 * delta) { Assert.True(e2.Contains(sim.Apply(pts[i]))); checkedCount++; }
                        else if (d > 1.01 * eps) { Assert.False(e2.Contains(sim.Apply(pts[i]))); checkedCount++; }
                    }
                    Assert.True(checkedCount > pts.Count / 3);
                }
            }
        }

        /// <summary> parallel queries equal serial ones, and repeated queries on one instance give the same answers </summary>
        [Fact]
        public void Determinism_ParallelEqualsSerial()
        {
            DMesh3 mesh = Sphere(6);
            var env = new PolyhedralEnvelope(mesh, 0.02);
            var segs = QuerySegments(env, new Random(21)).ToList();
            bool[] serial = segs.Select(s => env.ContainsSegment(s.a, s.b)).ToArray();
            for (int round = 0; round < 3; ++round) {
                var par = new bool[segs.Count];
                Parallel.For(0, segs.Count, i => par[i] = env.ContainsSegment(segs[i].a, segs[i].b));
                Assert.Equal(serial, par);
            }
            Assert.Equal(serial, segs.Select(s => env.ContainsSegment(s.a, s.b)).ToArray());
        }
    }
}
