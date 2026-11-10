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
                for (int k = 1; k < 3; ++k) if (PolyhedralEnvelope.length(ed[k]) > PolyhedralEnvelope.length(ed[L])) L = k;
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
            // (2^-560, 2^-900: edges whose squares underflow)
            foreach (double scale in new[] { 1.0, 1.0 / 1024, 1e-150, 1e150, Math.Pow(2, -560), Math.Pow(2, -900) })
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
            int tested = 0;
            foreach ((Vector3d[] t, double eps) in TestCases()) {
                // in units of eps relative to v0 (any scale); the double check needs coordinates within 1e6 eps
                if (t.Max(p => p.MaxAbs) > 1e6 * eps) continue;
                Build(t, eps, out Vector3d[] pts, out int nf, out EnvelopeStatus st);
                Assert.Equal(EnvelopeStatus.Ready, st);
                tested++;
                Func<Vector3d, Vector3d> rel = x => (x - t[0]) / eps;
                var planes = new List<(Vector3d n, double d)>();
                for (int f = 0; f < nf; ++f) {
                    Vector3d q0 = rel(pts[3 * f]), q1 = rel(pts[3 * f + 1]), q2 = rel(pts[3 * f + 2]);
                    Vector3d n = (q1 - q0).Cross(q2 - q0).Normalized;
                    planes.Add((n, n.Dot(q0)));
                }
                double maxd = 0;
                var tri = new Triangle3d(rel(t[0]), rel(t[1]), rel(t[2]));
                for (int i = 0; i < nf; ++i) for (int j = i + 1; j < nf; ++j) for (int k = j + 1; k < nf; ++k) {
                    var M = new Matrix3d(planes[i].n, planes[j].n, planes[k].n, true);
                    if (Math.Abs(M.Determinant) < 1e-9) continue;
                    Vector3d x = M.Inverse() * new Vector3d(planes[i].d, planes[j].d, planes[k].d);
                    if (planes.All(p => p.n.Dot(x) - p.d <= 1e-7)) {
                        double d = Math.Sqrt(new DistPoint3Triangle3(x, tri).GetSquared());
                        Assert.True(d <= 1 + 1e-6, $"vertex at {d} eps, {t[0]} {t[1]} {t[2]}");
                        maxd = Math.Max(maxd, d);
                    }
                }
                Assert.True(maxd >= 0.85, $"largest vertex distance {maxd} eps, {t[0]} {t[1]} {t[2]}");
            }
            Assert.True(tested > 500, $"{tested} cases");
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

        // the open interval (relative to [0, 1]) of t with every facet function f0 + t (f1 - f0) < 0, or null
        static (Q lo, bool loOpen, Q hi, bool hiOpen)? IntervalOf(IEnumerable<(BigInteger f0, BigInteger f1)> fs)
        {
            Q lo = Q.Zero, hi = Q.One;
            bool loOpen = false, hiOpen = false;
            foreach ((BigInteger f0, BigInteger f1) in fs) {
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

        // the interval of polyhedron p on the segment a + t (b - a): the determinant is affine along it
        static (Q lo, bool loOpen, Q hi, bool hiOpen)? Interval(PolyhedralEnvelope env, int p, Vector3d a, Vector3d b)
        {
            var fs = new List<(BigInteger, BigInteger)>();
            for (int f = 0; f < env.FacetCount(p); ++f) {
                env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                BigInteger[] F = AlignPair(Det(q0, q1, q2, a), Det(q0, q1, q2, b));
                fs.Add((F[0], F[1]));
            }
            return IntervalOf(fs);
        }

        // is [0, 1] covered by the intervals? (they are relatively open in [0, 1]: a sweep)
        static bool Covers(List<(Q lo, bool loOpen, Q hi, bool hiOpen)> iv)
        {
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

        static bool OracleSegment(PolyhedralEnvelope env, Vector3d a, Vector3d b, bool preFilter = true)
        {
            var iv = new List<(Q lo, bool loOpen, Q hi, bool hiOpen)>();
            foreach (int p in NearPolyhedra(env, preFilter, a, b)) {
                var i = Interval(env, p, a, b);
                if (i != null) iv.Add(i.Value);
            }
            return Covers(iv);
        }

        // ---- the triangle oracle: in the parameters x = a + u (b - a) + v (c - a), each facet is an affine function
        // A u + B v + C (exact integers), the polyhedron's part of the triangle the open polygon where all are < 0. The
        // triangle is covered iff (1) clipping by area leaves nothing (MeshEnvelope's method on these polygons) and
        // (2) every trace segment of a facet line in the triangle and every edge is covered (1D sweeps): an uncovered
        // point off all trace lines would lie in an uncovered open cell, of positive area.

        static BigInteger[] Align3(Dy a, Dy b, Dy c)
        {
            var v = new[] { a, b, c };
            int e = v.Where(d => d.M.IsZero == false).Select(d => d.E).DefaultIfEmpty(0).Min();
            return v.Select(d => d.M.IsZero ? BigInteger.Zero : d.M << (d.E - e)).ToArray();
        }

        // per polyhedron its facet functions (A, B, C): f(u, v) = A u + B v + C, the sign of the determinant
        // The oracles work on the polyhedra whose box meets the box of the query only. Polyhedron p belongs to the p-th triangle of the
        // mesh and every point of it is within epsilon of that triangle (the library's proven bound, ENVELOPE.md 4.4, tested in
        // Polyhedron_VerticesWithinEpsilon): it lies in the triangle's box grown by 1.01 epsilon. A polyhedron whose box is disjoint
        // from the query's cannot meet the query, so its open part of the query is empty and it adds nothing to the covering. (The
        // oracle looped over all polyhedra for every query, quadratic in the triangle count and 217 s on the 300-triangle sphere.
        // QueryOracle_PreFilterChangesNoVerdict compares with the unfiltered oracle and checks which triangle each polyhedron is
        // the polyhedron of.)
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PolyhedralEnvelope, Vector3d[][]> PolyhedronBoxes =
            new System.Runtime.CompilerServices.ConditionalWeakTable<PolyhedralEnvelope, Vector3d[][]>();

        static Vector3d[][] BoxesOf(PolyhedralEnvelope env) => PolyhedronBoxes.GetValue(env, e => {
            var boxes = new Vector3d[e.PolyhedronCount][];
            int p = 0;
            foreach (int t in e.Mesh.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                e.Mesh.GetTriVertices(t, ref a, ref b, ref c);
                double g = 1.01 * e.Epsilon;
                var lo = new Vector3d(Math.Min(a.x, Math.Min(b.x, c.x)) - g, Math.Min(a.y, Math.Min(b.y, c.y)) - g, Math.Min(a.z, Math.Min(b.z, c.z)) - g);
                var hi = new Vector3d(Math.Max(a.x, Math.Max(b.x, c.x)) + g, Math.Max(a.y, Math.Max(b.y, c.y)) + g, Math.Max(a.z, Math.Max(b.z, c.z)) + g);
                boxes[p++] = new[] { lo, hi };
            }
            return boxes;
        });

        static IEnumerable<int> NearPolyhedra(PolyhedralEnvelope env, bool preFilter, params Vector3d[] query)
        {
            var boxes = BoxesOf(env);
            for (int p = 0; p < env.PolyhedronCount; ++p) {
                bool meets = true;
                if (preFilter)
                    for (int k = 0; k < 3 && meets; ++k)
                        meets = query.Max(q => q[k]) >= boxes[p][0][k] && query.Min(q => q[k]) <= boxes[p][1][k];
                if (meets) yield return p;
            }
        }

        static List<List<BigInteger[]>> FacetFunctions(PolyhedralEnvelope env, Vector3d a, Vector3d b, Vector3d c, bool preFilter = true)
        {
            var r = new List<List<BigInteger[]>>();
            foreach (int p in NearPolyhedra(env, preFilter, a, b, c)) {
                var fs = new List<BigInteger[]>();
                for (int f = 0; f < env.FacetCount(p); ++f) {
                    env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                    Dy fa = Det(q0, q1, q2, a), fb = Det(q0, q1, q2, b), fc = Det(q0, q1, q2, c);
                    BigInteger[] F = Align3(fb - fa, fc - fa, fa);
                    fs.Add(F);
                }
                r.Add(fs);
            }
            return r;
        }

        static void Meet(BigInteger[] l1, BigInteger[] l2, out BigInteger X, out BigInteger Y, out BigInteger W)
        {
            // lines A u + B v <= C as (A, B, C): the intersection (X / W, Y / W), W >= 0
            W = l1[0] * l2[1] - l2[0] * l1[1];
            X = l1[2] * l2[1] - l2[2] * l1[1];
            Y = l1[0] * l2[2] - l2[0] * l1[2];
            if (W.Sign < 0) { W = -W; X = -X; Y = -Y; }
        }

        // MeshEnvelope's clip: the part of a convex polygon (CCW lines A u + B v <= C) with h; null if of zero area
        static List<BigInteger[]> Clip(List<BigInteger[]> poly, BigInteger[] h)
        {
            int n = poly.Count;
            var F = new int[n];
            bool anyNeg = false, anyPos = false;
            for (int i = 0; i < n; ++i) {
                Meet(poly[(i + n - 1) % n], poly[i], out BigInteger X, out BigInteger Y, out BigInteger W);
                F[i] = (h[0] * X + h[1] * Y - h[2] * W).Sign;
                if (F[i] < 0) anyNeg = true;
                if (F[i] > 0) anyPos = true;
            }
            if (anyPos == false) return poly;
            if (anyNeg == false) return null;
            int start = 0;
            while (F[start] >= 0) start++;
            var res = new List<BigInteger[]>(n + 1);
            bool pending = false, inserted = false;
            for (int k = 0; k < n; ++k) {
                int i = (start + k) % n;
                bool kept = F[i] < 0 || F[(i + 1) % n] < 0;
                if (kept) {
                    if (pending && inserted == false) { res.Add(h); inserted = true; }
                    res.Add(poly[i]);
                }
                if (F[(i + 1) % n] > 0) pending = true;
            }
            if (pending && inserted == false) res.Add(h);
            return res;
        }

        static bool AreaCovered(List<List<BigInteger[]>> polys)
        {
            var tri = new List<BigInteger[]> { new BigInteger[] { 0, -1, 0 }, new BigInteger[] { 1, 1, 1 }, new BigInteger[] { -1, 0, 0 } };
            var pieces = new List<List<BigInteger[]>> { tri };
            foreach (var fs in polys) {
                // f < 0: A u + B v < -C
                var lines = new List<BigInteger[]>();
                bool empty = false;
                foreach (BigInteger[] F in fs) {
                    if (F[0].IsZero && F[1].IsZero) { if (F[2].Sign >= 0) empty = true; continue; }
                    lines.Add(new[] { F[0], F[1], -F[2] });
                }
                if (empty) continue;
                var next = new List<List<BigInteger[]>>();
                foreach (var piece in pieces) {
                    // a piece the polygon does not reach stays whole (no vertex strictly inside one of its lines)
                    if (lines.Any(h => Enumerable.Range(0, piece.Count).All(i => {
                            Meet(piece[(i + piece.Count - 1) % piece.Count], piece[i], out BigInteger X, out BigInteger Y, out BigInteger W);
                            return (h[0] * X + h[1] * Y - h[2] * W).Sign >= 0; }))) { next.Add(piece); continue; }
                    List<BigInteger[]> cur = piece;
                    foreach (BigInteger[] h in lines) {
                        var outside = Clip(cur, new[] { -h[0], -h[1], -h[2] });
                        if (outside != null) next.Add(outside);
                        cur = Clip(cur, h);
                        if (cur == null) break;
                    }
                }
                pieces = next;
                if (pieces.Count == 0) return true;
            }
            return false;
        }

        // the segment between two homogeneous points of the plane (X, Y, W), W > 0, covered by the open polygons?
        static bool SegmentCovered(List<List<BigInteger[]>> polys, (BigInteger X, BigInteger Y, BigInteger W) P0, (BigInteger X, BigInteger Y, BigInteger W) P1)
        {
            var iv = new List<(Q lo, bool loOpen, Q hi, bool hiOpen)>();
            foreach (var fs in polys) {
                // f along the segment, times W0 W1 > 0: g(t) = (1 - t) f(P0) W0 W1 + t f(P1) W0 W1
                var i = IntervalOf(fs.Select(F => ((F[0] * P0.X + F[1] * P0.Y + F[2] * P0.W) * P1.W, (F[0] * P1.X + F[1] * P1.Y + F[2] * P1.W) * P0.W)));
                if (i != null) iv.Add(i.Value);
            }
            return Covers(iv);
        }

        static bool OracleTriangle(PolyhedralEnvelope env, Vector3d a, Vector3d b, Vector3d c, bool preFilter = true)
        {
            Dy[] N = Cross(Sub(D(b), D(a)), Sub(D(c), D(a)));
            if (N.All(d => d.M.IsZero)) return OracleSegment(env, a, b, preFilter) && OracleSegment(env, b, c, preFilter) && OracleSegment(env, c, a, preFilter);
            var polys = FacetFunctions(env, a, b, c, preFilter);
            if (AreaCovered(polys) == false) return false;
            var corners = new[] { (BigInteger.Zero, BigInteger.Zero, BigInteger.One), (BigInteger.One, BigInteger.Zero, BigInteger.One), (BigInteger.Zero, BigInteger.One, BigInteger.One) };
            for (int k = 0; k < 3; ++k) if (SegmentCovered(polys, corners[k], corners[(k + 1) % 3]) == false) return false;
            // the trace of every facet line in the triangle: its points on the closed triangle's edges, the extreme two
            var edges = new[] { new BigInteger[] { 1, 0, 0 }, new BigInteger[] { 0, 1, 0 }, new BigInteger[] { 1, 1, 1 } };    // u = 0, v = 0, u + v = 1
            foreach (var fs in polys)
                foreach (BigInteger[] F in fs) {
                    if (F[0].IsZero && F[1].IsZero) continue;
                    var line = new[] { F[0], F[1], -F[2] };     // A u + B v = -C
                    var pts = new List<(BigInteger X, BigInteger Y, BigInteger W)>();
                    foreach (var e in edges) {
                        Meet(line, e, out BigInteger X, out BigInteger Y, out BigInteger W);
                        if (W.IsZero) continue;
                        if (X.Sign >= 0 && Y.Sign >= 0 && X + Y <= W) pts.Add((X, Y, W));
                    }
                    if (pts.Count < 2) continue;    // misses the triangle or touches a corner (covered by the edges)
                    // extremes along the line's direction (B, -A)
                    Func<(BigInteger X, BigInteger Y, BigInteger W), (BigInteger X, BigInteger Y, BigInteger W), int> cmp =
                        (p, q) => ((F[1] * p.X - F[0] * p.Y) * q.W).CompareTo((F[1] * q.X - F[0] * q.Y) * p.W);
                    var lo = pts[0]; var hi = pts[0];
                    foreach (var q in pts) { if (cmp(q, lo) < 0) lo = q; if (cmp(q, hi) > 0) hi = q; }
                    if (cmp(lo, hi) == 0) continue;
                    if (SegmentCovered(polys, lo, hi) == false) return false;
                }
            return true;
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

        [Trait("Category", "Slow")]
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

        // The oracle tests below run one query mesh per test class (they are the longest of the suite, the meshes are independent, and
        // xUnit runs the classes in parallel but the tests of one class one after another). The
        // queries of all meshes come from one Random: a test generates the queries of the meshes before its own without judging them, so
        // that each mesh sees exactly the queries it saw in the loop over all of them (QueryMeshes_AreCoveredByTheOracleTests).
        /// <summary> the pre-filter of the oracles drops polyhedra whose box misses the query: the verdicts are those of the unfiltered oracle </summary>
        [Fact]
        public void QueryOracle_PreFilterChangesNoVerdict()
        {
            (DMesh3 mesh, double eps) = QueryMeshes().First();            // Sphere(4): the unfiltered oracle takes seconds here
            var env = new PolyhedralEnvelope(mesh, eps);
            // polyhedron p is the polyhedron of the p-th triangle: its vertices are strictly inside it (checked exactly at build)
            int pi = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                foreach (int v in new[] { tv.a, tv.b, tv.c }) Assert.True(OracleInside(env, pi, mesh.GetVertex(v)), $"polyhedron {pi}");
                pi++;
            }
            Assert.Equal(env.PolyhedronCount, pi);
            int n = 0, yes = 0;
            foreach (var (a, b, c) in QueryTriangles(env, new Random(13)).Where((q, i) => i % 6 == 0)) {
                bool r = OracleTriangle(env, a, b, c, true);
                Assert.True(r == OracleTriangle(env, a, b, c, false), $"triangle {a} {b} {c}");
                if (r) yes++;
                n++;
            }
            Assert.True(yes > n / 5 && yes < n, $"{yes} of {n}");
            foreach (var (a, b) in QuerySegments(env, new Random(5)).Where((q, i) => i % 6 == 0))
                Assert.True(OracleSegment(env, a, b, true) == OracleSegment(env, a, b, false), $"segment {a} {b}");
        }

        [Fact]
        public void QueryMeshes_AreCoveredByTheOracleTests()
        {
            Assert.Equal(3, QueryMeshes().Count());      // the per-mesh classes below
        }

        public class ContainsSegmentMesh0 { [Fact] public void ContainsSegment_MatchesIntervalOracle() => SegmentOracle(0); }

        public class ContainsSegmentMesh1 { [Fact] public void ContainsSegment_MatchesIntervalOracle() => SegmentOracle(1); }

        public class ContainsSegmentMesh2 { [Fact] public void ContainsSegment_MatchesIntervalOracle() => SegmentOracle(2); }

        static void SegmentOracle(int meshIndex)
        {
            var rand = new Random(5);
            int k = 0;
            foreach ((DMesh3 mesh, double eps) in QueryMeshes()) {
                var env = new PolyhedralEnvelope(mesh, eps);
                if (k++ != meshIndex) { foreach (var skipped in QuerySegments(env, rand)) { } continue; }
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
        /// The guarantee on the queries: every sample point of every accepted segment is within epsilon of the mesh (up
        /// to the double rounding of the distance), at scales 1, 2^-10, 1e-150, 1e150 and at UTM coordinates.
        /// </summary>
        [Trait("Category", "Slow")]
        [Fact]
        public void Guarantee_AcceptedSegmentsWithinEpsilon()
        {
            var sims = new[] { (1.0, Vector3d.Zero), (1.0 / 1024, Vector3d.Zero), (1e-150, Vector3d.Zero), (1e150, Vector3d.Zero),
                               (1.0, new Vector3d(632117.25, 5712043.5, 113.75)) };
            foreach ((DMesh3 mesh0, double eps0) in QueryMeshes())
                foreach ((double sc, Vector3d off) in sims) {
                    DMesh3 mesh = new DMesh3(mesh0);
                    foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, sc * mesh.GetVertex(v) + off);
                    double eps = sc * eps0;
                    var env = new PolyhedralEnvelope(mesh, eps);
                    Assert.Equal(EnvelopeStatus.Ready, env.Status);
                    double tol = 1e-9 * eps + 16 * U * off.MaxAbs;
                    int accepted = 0;
                    foreach ((Vector3d a, Vector3d b) in QuerySegments(env, new Random(9))) {
                        if (env.ContainsSegment(a, b) == false) continue;
                        accepted++;
                        for (int k = 0; k <= 16; ++k) {
                            Vector3d x = Vector3d.Lerp(a, b, k / 16.0);
                            // the distance to the nearest triangle by brute force, relative to each triangle's first
                            // vertex and in units of eps (no squares of huge or tiny values)
                            double d = double.MaxValue;
                            foreach (int t in mesh.TriangleIndices()) {
                                Triangle3d tri = new Triangle3d();
                                mesh.GetTriVertices(t, ref tri.V0, ref tri.V1, ref tri.V2);
                                Vector3d o = tri.V0;
                                var rt = new Triangle3d((tri.V0 - o) / eps, (tri.V1 - o) / eps, (tri.V2 - o) / eps);
                                d = Math.Min(d, Math.Sqrt(new DistPoint3Triangle3((x - o) / eps, rt).GetSquared()) * eps);
                            }
                            Assert.True(d <= eps + tol, $"scale {sc}: a point of an accepted segment at {d / eps} eps");
                        }
                    }
                    Assert.True(accepted > 20);
                }
        }

        /// <summary>
        /// Segments exactly in a facet plane (the axis-parallel facets of a flat triangle in z = 0, a degenerate segment
        /// and a point triangle; their facet points share the plane coordinate exactly), decided as the oracle does, with
        /// both answers occurring.
        /// </summary>
        [Fact]
        public void ContainsSegment_CoplanarWithFacets()
        {
            var tris = new List<Vector3d[]> {
                new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) },
                new[] { new Vector3d(0, 0, 0.015625), new Vector3d(1, 0, 0.015625), new Vector3d(0, 1, 0.015625) },
                new[] { new Vector3d(1.25, 0.5, 0), new Vector3d(2, 0.5, 0), new Vector3d(1.5, 0.5, 0) },
                new[] { new Vector3d(-0.5, 0.25, 0), new Vector3d(-0.5, 0.25, 0), new Vector3d(-0.5, 0.25, 0) },
            };
            var env = new PolyhedralEnvelope(Soup(tris), 0.03);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            int coplanar = 0, yes = 0, no = 0;
            var rand = new Random(4);
            for (int p = 0; p < env.PolyhedronCount; ++p)
                for (int f = 0; f < env.FacetCount(p); ++f) {
                    env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                    // the axis of the facet's normal, if the three points share one coordinate exactly
                    int ax = -1;
                    for (int k = 0; k < 3; ++k) if (q0[k] == q1[k] && q0[k] == q2[k]) ax = k;
                    if (ax < 0) continue;
                    for (int i = 0; i < 12; ++i) {
                        Vector3d a = env.Mesh.GetTriCentroid(p) + 0.6 * new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
                        Vector3d b = a + 0.3 * new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
                        a[ax] = q0[ax]; b[ax] = q0[ax];
                        Assert.Equal(0, Det(q0, q1, q2, a).Sign);
                        Assert.Equal(0, Det(q0, q1, q2, b).Sign);
                        bool r = env.ContainsSegment(a, b);
                        Assert.Equal(OracleSegment(env, a, b), r);
                        coplanar++;
                        if (r) yes++; else no++;
                    }
                }
            Assert.True(coplanar >= 100 && yes > 5 && no > 5, $"{coplanar} coplanar, {yes} contained, {no} not");
        }

        /// <summary>
        /// (review) a degenerate triangle (a, a, b) whose edges' squares underflow: the box around the edge, not the axis
        /// cube around the slanted segment (whose corners were 8e6 eps away)
        /// </summary>
        [Fact]
        public void Degenerate_UnderflowingEdges_KeepTheGuarantee()
        {
            Vector3d b = new Vector3d(1e-163, 1e-163, 1e-163);
            var env = new PolyhedralEnvelope(Soup(new[] { new[] { Vector3d.Zero, Vector3d.Zero, b } }), 1e-170);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            Assert.False(env.Contains(new Vector3d(1e-163, 0, 0)));
            Assert.True(env.Contains(b / 2));
            Assert.True(env.ContainsSegment(Vector3d.Zero, b));
            Assert.Equal(6, env.FacetCount(0));
        }

        static IEnumerable<(Vector3d a, Vector3d b, Vector3d c)> QueryTriangles(PolyhedralEnvelope env, Random rand)
        {
            DMesh3 m = env.Mesh;
            Func<double> g = () => rand.NextDouble() * 2 - 1;
            double eps = env.Epsilon, delta = eps / Math.Sqrt(3);
            var tids = m.TriangleIndices().ToList();
            foreach (int t in tids) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c), n = m.GetTriNormal(t);
                yield return (a, b, c);                                                     // the mesh's own
                Vector3d ce = (a + b + c) / 3;
                yield return (ce + 1.6 * (a - ce), ce + 1.6 * (b - ce), ce + 1.6 * (c - ce));      // across neighbours
                double h = (t % 3 == 0 ? 0.5 : t % 3 == 1 ? 0.95 : 1.3) * delta;
                yield return (a + h * n, b + h * n, c + h * n);                             // lifted
                yield return (a + eps * new Vector3d(g(), g(), g()), b + eps * new Vector3d(g(), g(), g()), c);
            }
            for (int i = 0; i < 40; ++i) {                                                  // over several triangles
                Vector3d a = m.GetTriCentroid(tids[rand.Next(tids.Count)]);
                yield return (a, a + 0.25 * new Vector3d(g(), g(), g()), a + 0.25 * new Vector3d(g(), g(), g()));
            }
            // degenerate queries: collinear and a point
            Vector3d p0 = m.GetTriCentroid(tids[0]), p1 = m.GetTriCentroid(tids[1 % tids.Count]);
            yield return (p0, p1, 0.5 * (p0 + p1));
            yield return (p0, p0, p0);
            // in a facet plane: corners from the facet's own points (axis-parallel facets give exact coplanar triangles)
            for (int p = 0; p < env.PolyhedronCount; p += 7)
                for (int f = 0; f < env.FacetCount(p); f += 3) {
                    env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                    yield return (q0, q0 + (q1 - q0) * 0.01, q0 + (q2 - q0) * 0.01);
                }
        }

        public class ContainsTriangleMesh0 { [Fact] public void ContainsTriangle_MatchesOracle() => TriangleOracle(0); }

        // Slow: ~1200 exact BigInteger oracle verdicts on the 300-triangle sphere (7 s; 110 s before the box pre-filter of the oracle)
        [Trait("Category", "Slow")]
        public class ContainsTriangleMesh1 { [Fact] public void ContainsTriangle_MatchesOracle() => TriangleOracle(1); }

        public class ContainsTriangleMesh2 { [Fact] public void ContainsTriangle_MatchesOracle() => TriangleOracle(2); }

        static void TriangleOracle(int meshIndex)
        {
            var rand = new Random(13);
            int k = 0;
            foreach ((DMesh3 mesh, double eps) in QueryMeshes()) {
                var env = new PolyhedralEnvelope(mesh, eps);
                if (k++ != meshIndex) { foreach (var skipped in QueryTriangles(env, rand)) { } continue; }
                int yes = 0, n = 0, edgesOnly = 0;
                foreach ((Vector3d a, Vector3d b, Vector3d c) in QueryTriangles(env, rand)) {
                    bool r = env.ContainsTriangle(a, b, c);
                    Assert.True(OracleTriangle(env, a, b, c) == r, $"triangle {a} {b} {c}: {r}");
                    if (r) yes++;
                    else if (env.ContainsSegment(a, b) && env.ContainsSegment(b, c) && env.ContainsSegment(c, a)) edgesOnly++;
                    n++;
                }
                Assert.True(yes > n / 5 && yes < n, $"{yes} of {n} contained");
                foreach (int t in mesh.TriangleIndices()) {
                    Index3i tv = mesh.GetTriangle(t);
                    Assert.True(env.ContainsTriangle(mesh.GetVertex(tv.a), mesh.GetVertex(tv.b), mesh.GetVertex(tv.c)));
                }
            }
        }

        /// <summary>
        /// A hole that only C3 finds: a flat grid with the two triangles of one inner square removed, and query
        /// triangles over the hole whose vertices and edges lie in the covered ring around it.
        /// </summary>
        [Fact]
        public void ContainsTriangle_HoleInsideTheTriangle()
        {
            var tris = new List<Vector3d[]>();
            for (int i = 0; i < 6; ++i)
                for (int j = 0; j < 6; ++j) {
                    if (i == 2 && j == 2) continue;
                    Vector3d p = new Vector3d(i, j, 0);
                    tris.Add(new[] { p, p + Vector3d.AxisX, p + Vector3d.AxisX + Vector3d.AxisY });
                    tris.Add(new[] { p, p + Vector3d.AxisX + Vector3d.AxisY, p + Vector3d.AxisY });
                }
            var env = new PolyhedralEnvelope(Soup(tris), 0.1);
            Assert.Equal(EnvelopeStatus.Ready, env.Status);
            // around the hole [2, 3]^2: vertices and edges in the ring, the hole inside
            var (a, b, c) = (new Vector3d(1.5, 1.5, 0), new Vector3d(4.5, 1.5, 0), new Vector3d(1.5, 4.5, 0));
            Assert.True(env.ContainsSegment(a, b) && env.ContainsSegment(b, c) && env.ContainsSegment(c, a));
            Assert.False(env.ContainsTriangle(a, b, c));
            Assert.False(OracleTriangle(env, a, b, c));
            // lifted by 0.02 and tilted: the same
            Assert.False(env.ContainsTriangle(a + 0.02 * Vector3d.AxisZ, b, c - 0.02 * Vector3d.AxisZ));
            // beside the hole: contained
            Assert.True(env.ContainsTriangle(new Vector3d(3.5, 0.5, 0), new Vector3d(5.5, 0.5, 0), new Vector3d(3.5, 2.5, 0.01)));
            // the hole's own square, slightly smaller: not contained; the whole grid minus the hole row: contained
            Assert.False(env.ContainsTriangle(new Vector3d(2.2, 2.2, 0), new Vector3d(2.8, 2.2, 0), new Vector3d(2.2, 2.8, 0)));
            Assert.True(env.ContainsTriangle(new Vector3d(0.1, 0.1, 0), new Vector3d(5.9, 0.1, 0), new Vector3d(0.1, 1.9, 0)));
        }

        [Fact]
        public void ContainsTriangle_Guarantee_Invariance_Determinism()
        {
            DMesh3 mesh = Sphere(5);
            double eps = 0.03;
            var env = new PolyhedralEnvelope(mesh, eps);
            var tq = QueryTriangles(env, new Random(17)).ToList();
            bool[] r = tq.Select(t => env.ContainsTriangle(t.a, t.b, t.c)).ToArray();
            // the guarantee: sample points of accepted triangles within eps of the mesh
            var spatial = new DMeshAABBTree3(mesh, true);
            for (int i = 0; i < tq.Count; ++i) {
                if (r[i] == false) continue;
                for (int s = 0; s <= 6; ++s)
                    for (int t = 0; s + t <= 6; ++t) {
                        Vector3d x = tq[i].a + (s / 6.0) * (tq[i].b - tq[i].a) + (t / 6.0) * (tq[i].c - tq[i].a);
                        double d = Math.Sqrt(MeshQueries.TriDistanceSqr(mesh, spatial.FindNearestTriangle(x), x));
                        Assert.True(d <= eps * (1 + 1e-9), $"a point of an accepted triangle at {d / eps} eps");
                    }
            }
            // 2^k scales: the same answers. The other similarities: triangles well inside (corners within delta / 4 of
            // the mesh, small against delta: the triangle lies in its corner's polyhedra... checked by a sampled
            // distance bound) are contained, triangles with a corner beyond 1.01 eps are not
            double delta = eps / Math.Sqrt(3);
            var rand = new Random(23);
            var inside = new List<(Vector3d a, Vector3d b, Vector3d c)>();
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                Vector3d n = mesh.GetTriNormal(t);
                Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), c = mesh.GetVertex(tv.c);
                double s = 0.25 * delta * (2 * rand.NextDouble() - 1);
                inside.Add((a + s * n, b + s * n, c + s * n));      // the mesh triangle moved along its normal by < delta / 4
            }
            var outside = tq.Where(t => new[] { t.a, t.b, t.c }.Any(x => Math.Sqrt(MeshQueries.TriDistanceSqr(mesh, spatial.FindNearestTriangle(x), x)) > 1.01 * eps)).ToList();
            Assert.True(outside.Count > 10);
            foreach (var t in inside) Assert.True(env.ContainsTriangle(t.a, t.b, t.c));
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 m2 = new DMesh3(mesh);
                foreach (int v in m2.VertexIndices()) m2.SetVertex(v, sim.Apply(m2.GetVertex(v)));
                var e2 = new PolyhedralEnvelope(m2, sim.ApplyLength(eps));
                Assert.Equal(EnvelopeStatus.Ready, e2.Status);
                if (sim.IsExact) {
                    for (int i = 0; i < tq.Count; ++i)
                        Assert.Equal(r[i], e2.ContainsTriangle(sim.Apply(tq[i].a), sim.Apply(tq[i].b), sim.Apply(tq[i].c)));
                } else {
                    foreach (var t in inside) Assert.True(e2.ContainsTriangle(sim.Apply(t.a), sim.Apply(t.b), sim.Apply(t.c)), $"{sim}: inside");
                    foreach (var t in outside) Assert.False(e2.ContainsTriangle(sim.Apply(t.a), sim.Apply(t.b), sim.Apply(t.c)), $"{sim}: outside");
                }
            }
            // parallel equals serial, repeated
            for (int round = 0; round < 2; ++round) {
                var par = new bool[tq.Count];
                Parallel.For(0, tq.Count, i => par[i] = env.ContainsTriangle(tq[i].a, tq[i].b, tq[i].c));
                Assert.Equal(r, par);
            }
        }

        /// <summary> the triangle oracle at scales 1e-150, 1e150 and at UTM coordinates (no exceptions, same answers) </summary>
        [Fact]
        public void ContainsTriangle_MatchesOracle_ExtremeScales()
        {
            foreach ((double sc, Vector3d off) in new[] { (1e-150, Vector3d.Zero), (1e150, Vector3d.Zero), (1.0, new Vector3d(632117.25, 5712043.5, 113.75)) }) {
                DMesh3 mesh = Sphere(4);
                foreach (int v in mesh.VertexIndices()) mesh.SetVertex(v, sc * mesh.GetVertex(v) + off);
                var env = new PolyhedralEnvelope(mesh, sc * 0.05);
                Assert.Equal(EnvelopeStatus.Ready, env.Status);
                int yes = 0, n = 0;
                foreach (var t in QueryTriangles(env, new Random(13)).Take(150)) {
                    bool r = env.ContainsTriangle(t.a, t.b, t.c);
                    Assert.True(OracleTriangle(env, t.a, t.b, t.c) == r, $"scale {sc}, offset {off}: {t.a} {t.b} {t.c}");
                    if (r) yes++;
                    n++;
                }
                Assert.True(yes > 10 && yes < n, $"scale {sc}: {yes} of {n}");
            }
        }

        // the next double up / down (Math.BitIncrement is missing on net48)
        static double Up(double x) { return x == 0 ? double.Epsilon : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? 1 : -1)); }
        static double Down(double x) { return -Up(-x); }

        // the coordinate of the axis-parallel facet on side sgn of a point triangle's cube
        static double CubeFace(Vector3d c, double eps, int axis, int sgn)
        {
            var env = new PolyhedralEnvelope(Soup(new[] { new[] { c, c, c } }), eps);
            for (int f = 0; f < env.FacetCount(0); ++f) {
                env.GetFacet(0, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                if (q0[axis] == q1[axis] && q0[axis] == q2[axis] && Math.Sign(q0[axis] - c[axis]) == sgn) return q0[axis];
            }
            throw new InvalidOperationException("no face");
        }

        // a cube centre with its face on side sgn at target moved by ulps units in the last place (0: exactly on it)
        static Vector3d PlaceCube(Vector3d c, double eps, int axis, int sgn, double target, int ulps)
        {
            double want = target;
            for (int i = 0; i < Math.Abs(ulps); ++i) want = ulps > 0 ? Up(want) : Down(want);
            for (int attempt = 0; attempt < 60; ++attempt) {
                double x = c[axis];
                for (int it = 0; it < 30; ++it) { c[axis] = x; double f = CubeFace(c, eps, axis, sgn); if (f == want) return c; x += want - f; }
                for (int it = 0; it < 64; ++it) {
                    c[axis] = x;
                    double f = CubeFace(c, eps, axis, sgn);
                    if (f == want) return c;
                    x = f < want ? Up(x) : Down(x);
                }
                c.z += (attempt % 2 == 0 ? 1 : -1) * 1e-5 * (attempt + 1);
            }
            throw new InvalidOperationException("no placement");
        }

        /// <summary>
        /// (review) Four cubes of point triangles whose faces meet exactly at x = X0 and y = Y0, touching, one ulp apart
        /// or one ulp overlapping; query triangles from coordinates at the faces and one ulp beside them: triangles in
        /// facet planes, through the point where four facets meet, with vertices on facet planes, shared traces and
        /// dependent pairs. ContainsTriangle equals the oracle; the triangle around the meeting point is contained iff
        /// an overlap closes the cross
        /// </summary>
        [Trait("Category", "Slow")]
        [Fact]
        public void ContainsTriangle_FourCubesMeetingAtAPoint()
        {
            double eps = 0.1, delta = eps / Math.Sqrt(3);
            var rand = new Random(100);
            foreach (int gapX in new[] { 0, 1, -1 })
                foreach (int gapY in new[] { 0, 1, -1 }) {
                    Vector3d cA = new Vector3d(-0.5 * delta, 0.03125 * delta, 0.2), cB = Vector3d.Zero;
                    double X0 = 0;
                    for (int tr = 0; ; ++tr) {
                        X0 = CubeFace(cA, eps, 0, 1);
                        try { cB = PlaceCube(new Vector3d(X0 + delta, -0.0625 * delta, 0.201), eps, 0, -1, X0, gapX); break; }
                        catch (InvalidOperationException) { cA.x = Up(cA.x); if (tr > 50) throw; }
                    }
                    Vector3d cC = new Vector3d(0.0078125 * delta, 0.5 * delta, 0.198), cD = Vector3d.Zero;
                    double Y0 = 0;
                    for (int tr = 0; ; ++tr) {
                        Y0 = CubeFace(cC, eps, 1, -1);
                        try { cD = PlaceCube(new Vector3d(-0.015625 * delta, Y0 - delta, 0.2005), eps, 1, 1, Y0, -gapY); break; }
                        catch (InvalidOperationException) { cC.y = Up(cC.y); if (tr > 50) throw; }
                    }
                    var env = new PolyhedralEnvelope(Soup(new[] { cA, cB, cC, cD }.Select(c => new[] { c, c, c })), eps);
                    Assert.Equal(EnvelopeStatus.Ready, env.Status);
                    var xs = new List<double> { X0, X0 - 0.3 * delta, X0 + 0.3 * delta, X0 - 0.05 * delta, X0 + 0.07 * delta };
                    var ys = new List<double> { Y0, Y0 - 0.3 * delta, Y0 + 0.3 * delta, Y0 - 0.04 * delta, Y0 + 0.06 * delta };
                    var zs = new List<double> { 0.2, 0.2 + 0.1 * delta, 0.2 - 0.2 * delta };
                    for (int p = 0; p < env.PolyhedronCount; ++p)
                        for (int f = 0; f < env.FacetCount(p); ++f) {
                            env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
                            for (int k = 0; k < 3; ++k) if (q0[k] == q1[k] && q0[k] == q2[k]) (k == 0 ? xs : k == 1 ? ys : zs).Add(q0[k]);
                        }
                    Func<List<double>, List<double>> around = v => v.SelectMany(x => new[] { x, Up(x), Down(x) }).Distinct().ToList();
                    xs = around(xs); ys = around(ys); zs = around(zs);
                    Func<Vector3d> pick = () => new Vector3d(xs[rand.Next(xs.Count)], ys[rand.Next(ys.Count)], zs[rand.Next(zs.Count)]);
                    int yes = 0, no = 0;
                    for (int i = 0; i < 250; ++i) {
                        Vector3d a = pick(), b = pick(), c = pick();
                        if (rand.Next(3) == 0) { b.z = a.z; c.z = a.z; } else if (rand.Next(3) == 0) { b.x = a.x; c.x = a.x; }
                        bool r = env.ContainsTriangle(a, b, c);
                        Assert.True(OracleTriangle(env, a, b, c) == r, $"gaps {gapX} {gapY}: ({a.x:R},{a.y:R},{a.z:R}) ({b.x:R},{b.y:R},{b.z:R}) ({c.x:R},{c.y:R},{c.z:R})");
                        if (r) yes++; else no++;
                    }
                    Assert.True(yes > 10 && no > 10, $"gaps {gapX} {gapY}: {yes} contained, {no} not");
                    Vector3d ta = new Vector3d(X0 - 0.3 * delta, Y0 - 0.3 * delta, 0.2), tb = new Vector3d(X0 + 0.3 * delta, Y0 - 0.2 * delta, 0.2), tc = new Vector3d(X0, Y0 + 0.3 * delta, 0.2);
                    Assert.Equal(gapX < 0 || gapY < 0, env.ContainsTriangle(ta, tb, tc));
                    Assert.Equal(gapX < 0 || gapY < 0, OracleTriangle(env, ta, tb, tc));
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
                    // segments between two points within delta / 2 of one triangle: inside its polyhedron (convex)
                    int segChecked = 0;
                    foreach (int t in mesh.TriangleIndices()) {
                        Vector3d c = mesh.GetTriCentroid(t), n = mesh.GetTriNormal(t);
                        Index3i tv = mesh.GetTriangle(t);
                        Vector3d x = c + 0.4 * delta * n, y = 0.8 * mesh.GetVertex(tv.a) + 0.2 * c - 0.4 * delta * n;
                        Assert.True(e2.ContainsSegment(sim.Apply(x), sim.Apply(y)));
                        segChecked++;
                    }
                    Assert.True(segChecked > 50);
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
