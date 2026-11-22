using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The topology certificate for the contour of a smooth implicit field (ContouringSmoothCertificate) and its interval
    /// functions (IntervalMath, IntervalTpms). The certificate must never pass where its stated facts do not hold: the
    /// cell-level oracle samples those facts in every cell it does not list, the truth tests compare the contour's topology,
    /// and the refusal tests cover each reason.
    /// </summary>
    public class ContouringSmoothCertificateTests
    {
        // ---------------------------------------------------------------- fields

        /// <summary> a field with enclosures that DualContouring can contour: Value is the plain double formula </summary>
        abstract class TestField : IntervalImplicit3d, ImplicitFunction3d
        {
            public abstract IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z);
            public abstract double Value(ref Vector3d p);
            public double At(Vector3d p) { return Value(ref p); }
            protected static ExactInterval C(double v) { return ExactInterval.Of(v); }
        }

        /// <summary> g(K x) - level + cap (|x|^2 / R^2)^3: a TPMS closed by a smooth cap inside the ball of radius R </summary>
        sealed class CappedTpms : TestField
        {
            readonly IntervalTpms tpms; readonly double R, cap;
            public CappedTpms(TpmsType type, double k, double level, double radius, double cap) { tpms = new IntervalTpms(type, k, level); R = radius; this.cap = cap; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                IntervalEnclosure e = tpms.Enclose(x, y, z);
                ExactInterval r2 = ExactInterval.Square(C(R));
                ExactInterval u = (ExactInterval.Square(x) + ExactInterval.Square(y) + ExactInterval.Square(z)) / r2;
                ExactInterval u2 = ExactInterval.Square(u);
                ExactInterval d = C(6) * C(cap) * u2 / r2;
                e.F = e.F + C(cap) * (u2 * u); e.Gx = e.Gx + d * x; e.Gy = e.Gy + d * y; e.Gz = e.Gz + d * z;
                return e;
            }
            public override double Value(ref Vector3d p)
            {
                double u = p.LengthSquared / (R * R);
                return TpmsLattice.Evaluate(tpms.Type, tpms.K * p.x, tpms.K * p.y, tpms.K * p.z) - tpms.Level + cap * u * u * u;
            }
        }

        /// <summary> |p - c| - r </summary>
        sealed class Sphere : TestField
        {
            readonly Vector3d c; readonly double r;
            public Sphere(Vector3d center, double radius) { c = center; r = radius; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                ExactInterval dx = x - C(c.x), dy = y - C(c.y), dz = z - C(c.z);
                ExactInterval rho = ExactInterval.Sqrt(ExactInterval.Square(dx) + ExactInterval.Square(dy) + ExactInterval.Square(dz));
                ExactInterval whole = new ExactInterval(double.NegativeInfinity, double.PositiveInfinity);
                if (!(rho.Lo > 0)) return new IntervalEnclosure { F = rho - C(r), Gx = whole, Gy = whole, Gz = whole, Smooth = false };
                return new IntervalEnclosure { F = rho - C(r), Gx = dx / rho, Gy = dy / rho, Gz = dz / rho, Smooth = true };
            }
            public override double Value(ref Vector3d p) { return (p - c).Length - r; }
        }

        /// <summary> x^2 + y^2 + z^2 - r^2 </summary>
        sealed class PolySphere : TestField
        {
            readonly double r;
            public PolySphere(double radius) { r = radius; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                return new IntervalEnclosure { F = ExactInterval.Square(x) + ExactInterval.Square(y) + ExactInterval.Square(z) - ExactInterval.Square(C(r)),
                    Gx = C(2) * x, Gy = C(2) * y, Gz = C(2) * z, Smooth = true };
            }
            public override double Value(ref Vector3d p) { return p.LengthSquared - r * r; }
        }

        /// <summary> ((x-a)^2 + y^2 + z^2)((x+a)^2 + y^2 + z^2) - b^4: one component for b > a, two for b < a </summary>
        sealed class Cassini : TestField
        {
            readonly double a, b;
            public Cassini(double a, double b) { this.a = a; this.b = b; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                ExactInterval q = ExactInterval.Square(y) + ExactInterval.Square(z), xm = x - C(a), xp = x + C(a);
                ExactInterval A = ExactInterval.Square(xm) + q, B = ExactInterval.Square(xp) + q, s = A + B;
                return new IntervalEnclosure { F = A * B - ExactInterval.Square(ExactInterval.Square(C(b))),
                    Gx = C(2) * (xm * B + xp * A), Gy = C(2) * (y * s), Gz = C(2) * (z * s), Smooth = true };
            }
            public override double Value(ref Vector3d p)
            {
                double q = p.y * p.y + p.z * p.z, A = (p.x - a) * (p.x - a) + q, B = (p.x + a) * (p.x + a) + q;
                return A * B - b * b * b * b;
            }
        }

        /// <summary> min(a, b): one piece where its enclosure is strictly below the other's over the box, else not smooth </summary>
        sealed class Min : TestField
        {
            readonly TestField a, b;
            public Min(TestField a, TestField b) { this.a = a; this.b = b; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                IntervalEnclosure p = a.Enclose(x, y, z), q = b.Enclose(x, y, z);
                if (p.F.Hi < q.F.Lo) return p;
                if (q.F.Hi < p.F.Lo) return q;
                return new IntervalEnclosure { F = new ExactInterval(Math.Min(p.F.Lo, q.F.Lo), Math.Min(p.F.Hi, q.F.Hi)),
                    Gx = IntervalMath.Hull(p.Gx, q.Gx), Gy = IntervalMath.Hull(p.Gy, q.Gy), Gz = IntervalMath.Hull(p.Gz, q.Gz), Smooth = false };
            }
            public override double Value(ref Vector3d p) { return Math.Min(a.Value(ref p), b.Value(ref p)); }
        }

        /// <summary> the field in a moved frame: g(p) = f(S^-1 p) for a similarity S; the box is enclosed in f's frame </summary>
        sealed class Moved : TestField
        {
            readonly TestField f; readonly Similarity s;
            public Moved(TestField f, Similarity s) { this.f = f; this.s = s; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                Matrix3d R = s.Rotation;
                ExactInterval inv = C(1) / C(s.Scale);
                ExactInterval px = (x - C(s.Translation.x)) * inv, py = (y - C(s.Translation.y)) * inv, pz = (z - C(s.Translation.z)) * inv;
                // S^-1 p = R^T (p - t) / scale
                ExactInterval qx = C(R[0, 0]) * px + C(R[1, 0]) * py + C(R[2, 0]) * pz;
                ExactInterval qy = C(R[0, 1]) * px + C(R[1, 1]) * py + C(R[2, 1]) * pz;
                ExactInterval qz = C(R[0, 2]) * px + C(R[1, 2]) * py + C(R[2, 2]) * pz;
                IntervalEnclosure e = f.Enclose(qx, qy, qz);
                // grad g = R grad f / scale
                return new IntervalEnclosure { F = e.F, Smooth = e.Smooth,
                    Gx = (C(R[0, 0]) * e.Gx + C(R[0, 1]) * e.Gy + C(R[0, 2]) * e.Gz) * inv,
                    Gy = (C(R[1, 0]) * e.Gx + C(R[1, 1]) * e.Gy + C(R[1, 2]) * e.Gz) * inv,
                    Gz = (C(R[2, 0]) * e.Gx + C(R[2, 1]) * e.Gy + C(R[2, 2]) * e.Gz) * inv };
            }
            public override double Value(ref Vector3d p) { Vector3d q = s.Inverse(p); return f.Value(ref q); }
        }

        /// <summary> a field whose Value differs from its enclosures by a constant: the contour then has other node signs </summary>
        sealed class Shifted : TestField
        {
            readonly TestField f; readonly double shift;
            public Shifted(TestField f, double shift) { this.f = f; this.shift = shift; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z) { return f.Enclose(x, y, z); }
            public override double Value(ref Vector3d p) { return f.Value(ref p) + shift; }
        }

        sealed class Throwing : IntervalImplicit3d
        {
            public IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z) { throw new InvalidOperationException("field"); }
        }

        sealed class Negated : TestField
        {
            readonly TestField f;
            public Negated(TestField f) { this.f = f; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                IntervalEnclosure e = f.Enclose(x, y, z);
                return new IntervalEnclosure { F = -e.F, Gx = -e.Gx, Gy = -e.Gy, Gz = -e.Gz, Smooth = e.Smooth };
            }
            public override double Value(ref Vector3d p) { return -f.Value(ref p); }
        }

        // ---------------------------------------------------------------- helpers

        static readonly Vector3d Shift = new Vector3d(-1.113, -1.107, -1.103);

        static DualContouring Contour(TestField f, Vector3d origin, double size, int n, double iso = 0, bool insideIsLess = true)
        {
            var dc = new DualContouring { Implicit = f, InsideIsLess = insideIsLess, IsoValue = iso, CellSize = size / n, Simplify = false,
                Bounds = new AxisAlignedBox3d(origin, origin + size * Vector3d.One), RecordCertificateData = true };
            dc.Generate();
            return dc;
        }

        static ContouringSmoothCertificate Certify(TestField f, DualContouring dc, bool parallel = true)
        {
            var cert = new ContouringSmoothCertificate { Field = f, Contour = dc, ParallelCompute = parallel };
            cert.Compute();
            return cert;
        }

        static ContouringSmoothCertificate Run(TestField f, int n, out DualContouring dc, double scale = 1)
        {
            dc = Contour(f, scale * Shift, scale * 2.2, n);
            return Certify(f, dc);
        }

        static string Signature(DMesh3 mesh)
        {
            if (mesh.TriangleCount == 0) return "empty";
            var t = MeshTopologySummary.Compute(mesh);
            return "c=" + t.Components + ";g=" + string.Join("+", t.ComponentList.Select(c => c.Genus).OrderBy(g => g)) + (mesh.IsClosed() ? "" : ";open");
        }

        static string List(ContouringSmoothCertificate c)
        {
            return c.Certified + "|" + c.RunFailure + "|" + c.SurfaceCells + "|" + string.Join(",", c.PassedAtTightening) + "|"
                + string.Join(";", c.UncertifiedCells.Select(u => u.Cell.x + "," + u.Cell.y + "," + u.Cell.z + ":" + u.Reason));
        }

        static TestField Gyroid(double scale = 1) { return new CappedTpms(TpmsType.Gyroid, 2 * Math.PI / (2.2 * scale), 0, 0.9 * scale, 4); }

        // ---------------------------------------------------------------- the interval functions

        // sin x for a double x as an integer scaled by 2^S, by the Taylor series in exact integer arithmetic (error < 2^(8 - S) * terms)
        const int S = 1200;
        static BigInteger Scaled(double d) { ExactDyadic e = ExactDyadic.Of(d); return e.E + S >= 0 ? e.M << (e.E + S) : e.M >> -(e.E + S); }
        static void BigSinCos(double x, out BigInteger sin, out BigInteger cos)
        {
            // divisions truncate towards zero (a shift of a negative number would round down and never reach 0)
            BigInteger one = BigInteger.One << S, X = Scaled(x), X2 = X * X / one;
            BigInteger term = X; sin = X;
            for (int n = 1; !term.IsZero; ++n) { term = -(term * X2 / one) / (2 * n * (2 * n + 1)); sin += term; }
            term = one; cos = term;
            for (int n = 1; !term.IsZero; ++n) { term = -(term * X2 / one) / ((2 * n - 1) * (2 * n)); cos += term; }
        }

        [Fact]
        public void SinCos_AtAPoint_EnclosesTheExactValue_AndIsTight()
        {
            var rnd = new Random(7);
            BigInteger slack = BigInteger.One << (S - 900);        // far below a unit in the last place of a double, far above the series' error
            var xs = new List<double> { 0, 1e-300, -1e-300, 0.5, Math.PI / 2, -Math.PI / 2, Math.PI, 2 * Math.PI, 30, -30, 0.7853981633974483 };
            for (int i = 0; i < 200; ++i) xs.Add((rnd.NextDouble() - 0.5) * (i % 4 == 0 ? 80 : 20));
            for (int k = -8; k <= 8; ++k) { double m = k * Math.PI / 4; xs.Add(m); xs.Add(Math.BitIncrement(m)); xs.Add(Math.BitDecrement(m)); }
            foreach (double x in xs) {
                ExactInterval s, c; IntervalMath.SinCos(x, out s, out c);
                BigInteger bs, bc; BigSinCos(x, out bs, out bc);
                Assert.True(Scaled(s.Lo) <= bs + slack && bs - slack <= Scaled(s.Hi), "sin " + x.ToString("R"));
                Assert.True(Scaled(c.Lo) <= bc + slack && bc - slack <= Scaled(c.Hi), "cos " + x.ToString("R"));
                Assert.True(s.Hi - s.Lo < 2e-12 && c.Hi - c.Lo < 2e-12, "width at " + x.ToString("R"));     // twice the remainder bound at most
                Assert.True(s.Lo >= -1 && s.Hi <= 1 && c.Lo >= -1 && c.Hi <= 1);
            }
            ExactInterval a, b;
            IntervalMath.SinCos(0.0078125, out a, out b);
            Assert.True(a.Hi - a.Lo < 1e-16 && b.Hi - b.Lo < 3e-15);     // near 0: a few units in the last place
            // far from 0 (the series of the reference would be too long): against the runtime's functions, which are
            // accurate to far better than 1e-9; a wrong quadrant or reduction would be off by much more
            foreach (double x in new[] { 1e3, -1e3, 100000.25, -2.5e6, 632117.25 * 2.5, 1e9 }) {
                IntervalMath.SinCos(x, out a, out b);
                Assert.True(a.Lo - 1e-9 <= Math.Sin(x) && Math.Sin(x) <= a.Hi + 1e-9 && b.Lo - 1e-9 <= Math.Cos(x) && Math.Cos(x) <= b.Hi + 1e-9, "far " + x);
                Assert.True(a.Hi - a.Lo < 1e-6 && b.Hi - b.Lo < 1e-6, "far width " + x);
            }
        }

        [Fact]
        public void SinCos_RemainderBounds_AreTheLagrangeBounds()
        {
            // the interval of pi / 2 is a double and its successor, with pi / 2 strictly between (60 digits of pi / 2)
            BigInteger piHalf = BigInteger.Parse("157079632679489661923132169163975144209858469968755291048747") * (BigInteger.One << S) / BigInteger.Pow(10, 59);
            Assert.True(Scaled(IntervalMath.PiHalf.Lo) < piHalf - 2 && piHalf + 2 < Scaled(IntervalMath.PiHalf.Hi));
            Assert.Equal(Math.BitIncrement(IntervalMath.PiHalf.Lo), IntervalMath.PiHalf.Hi);
            // 15! and 14! as exact doubles
            BigInteger f = BigInteger.One;
            for (int i = 2; i <= 14; ++i) f *= i;
            Assert.Equal(f, new BigInteger(IntervalMath.Factorial14));
            Assert.Equal(f * 15, new BigInteger(IntervalMath.Factorial15));
            // where the remainder is largest (|r| near pi / 4) dropping it must break the enclosure: the polynomial alone is
            // off by about (pi/4)^14 / 14! = 3.9e-13 for cos, far above the rounding of the evaluation
            double x = 0.7853981633974483;
            ExactInterval s, c; IntervalMath.SinCos(x, out s, out c);
            Assert.InRange(c.Hi - c.Lo, 7e-13, 1.2e-12);          // twice the bound of cos, and nothing else of that size
            Assert.InRange(s.Hi - s.Lo, 3e-14, 6e-14);            // twice (pi/4)^15 / 15! = 2.0e-14
            Assert.True(IntervalMath.MaxReduced >= 0.7853981633974484 && IntervalMath.MaxReduced < 1);
            // the bounds hold for |r| <= MaxReduced only: a reduction with another multiple is refused, not evaluated
            IntervalMath.SinCosReduced(x, 0, out s, out c);
            Assert.True(c.Hi - c.Lo < 1.2e-12);
            IntervalMath.SinCosReduced(x, 1, out s, out c);          // r = pi/4 - pi/2: still within the range
            Assert.True(c.Hi - c.Lo < 1.2e-12 && s.Hi - s.Lo < 1.2e-12);
            IntervalMath.SinCosReduced(x, 2, out s, out c);          // |r| = 3 pi / 4 > MaxReduced
            Assert.Equal(-1, s.Lo); Assert.Equal(1, s.Hi); Assert.Equal(-1, c.Lo); Assert.Equal(1, c.Hi);
            IntervalMath.SinCosReduced(0.81, 0, out s, out c);
            Assert.Equal(-1, s.Lo); Assert.Equal(1, c.Hi);
            IntervalMath.SinCosReduced(0.79, 0, out s, out c);
            Assert.True(s.Hi - s.Lo < 1e-12);
            // the bound follows |r|: tiny near a multiple of pi / 2
            IntervalMath.SinCos(1.5707963267948966 + 1e-3, out s, out c);
            Assert.True(s.Hi - s.Lo < 1e-15 && c.Hi - c.Lo < 1e-15);
        }

        [Fact]
        public void SinCos_OutOfRange_IsTheUnitInterval()
        {
            foreach (double x in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 2e9, -2e9 }) {
                ExactInterval s, c; IntervalMath.SinCos(x, out s, out c);
                Assert.Equal(-1, s.Lo); Assert.Equal(1, s.Hi); Assert.Equal(-1, c.Lo); Assert.Equal(1, c.Hi);
            }
            ExactInterval s2, c2;
            IntervalMath.SinCos(new ExactInterval(0, 7), out s2, out c2);
            Assert.Equal(-1, s2.Lo); Assert.Equal(1, c2.Hi);
            IntervalMath.SinCos(new ExactInterval(0, double.PositiveInfinity), out s2, out c2);
            Assert.Equal(-1, s2.Lo); Assert.Equal(1, s2.Hi);
        }

        [Fact]
        public void SinCos_OverAnInterval_EnclosesEveryPoint_AndFindsTheExtrema()
        {
            var rnd = new Random(11);
            BigInteger slack = BigInteger.One << (S - 900);
            for (int i = 0; i < 120; ++i) {
                double lo = (rnd.NextDouble() - 0.5) * 40, w = Math.Pow(2, -8 * rnd.NextDouble()) * 5;
                ExactInterval s, c; IntervalMath.SinCos(new ExactInterval(lo, lo + w), out s, out c);
                for (int j = 0; j < 5; ++j) {
                    double x = j == 0 ? lo : j == 1 ? lo + w : lo + w * rnd.NextDouble();
                    if (x > lo + w) x = lo + w;
                    BigInteger bs, bc; BigSinCos(x, out bs, out bc);
                    Assert.True(Scaled(s.Lo) <= bs + slack && bs - slack <= Scaled(s.Hi), "sin over [" + lo + ", " + (lo + w) + "] at " + x);
                    Assert.True(Scaled(c.Lo) <= bc + slack && bc - slack <= Scaled(c.Hi), "cos over [" + lo + ", " + (lo + w) + "] at " + x);
                }
            }
            // each extremum, on both sides of 0 and several periods away, is inside when the interval holds it ...
            for (int n = -3; n <= 3; ++n) {
                double p = 2 * Math.PI * n;
                ExactInterval s, c;
                IntervalMath.SinCos(new ExactInterval(p + 1.5, p + 1.6), out s, out c); Assert.Equal(1, s.Hi);                 // pi/2
                IntervalMath.SinCos(new ExactInterval(p - 1.6, p - 1.5), out s, out c); Assert.Equal(-1, s.Lo);                // -pi/2
                IntervalMath.SinCos(new ExactInterval(p - 0.1, p + 0.1), out s, out c); Assert.Equal(1, c.Hi);                 // 0
                IntervalMath.SinCos(new ExactInterval(p + 3.1, p + 3.2), out s, out c); Assert.Equal(-1, c.Lo);                // pi
                // ... and is not added where the interval is monotone (the enclosure stays near the end values)
                IntervalMath.SinCos(new ExactInterval(p + 0.2, p + 0.4), out s, out c);
                Assert.True(s.Hi < 0.39 && s.Lo > 0.19 && c.Hi < 0.99 && c.Lo > 0.92);
                IntervalMath.SinCos(new ExactInterval(p + 2.0, p + 2.2), out s, out c);
                Assert.True(s.Hi < 0.91 && s.Lo > 0.80 && c.Hi < -0.41 && c.Lo > -0.59);
                IntervalMath.SinCos(new ExactInterval(p - 2.2, p - 2.0), out s, out c);
                Assert.True(s.Lo > -0.91 && s.Hi < -0.80 && c.Hi < -0.41 && c.Lo > -0.59);
            }
        }

        public static IEnumerable<object[]> TpmsTypes() => Enum.GetValues(typeof(TpmsType)).Cast<TpmsType>().Select(t => new object[] { t });

        [Theory]
        [MemberData(nameof(TpmsTypes))]
        public void IntervalTpms_EnclosesValueAndGradient(TpmsType type)
        {
            var rnd = new Random(3 + (int)type);
            foreach (bool negate in new[] { false, true }) {
                double k = 2.5, level = 0.3;
                var f = new IntervalTpms(type, k, level, negate);
                Func<Vector3d, double> value = p => (negate ? -1 : 1) * TpmsLattice.Evaluate(type, k * p.x, k * p.y, k * p.z) - level;
                for (int i = 0; i < 300; ++i) {
                    Vector3d a = new Vector3d(rnd.NextDouble() * 8 - 4, rnd.NextDouble() * 8 - 4, rnd.NextDouble() * 8 - 4);
                    double w = i < 30 ? 0 : Math.Pow(2, -1 - 7 * rnd.NextDouble());
                    IntervalEnclosure e = f.Enclose(new ExactInterval(a.x, a.x + w), new ExactInterval(a.y, a.y + w), new ExactInterval(a.z, a.z + w));
                    Assert.True(e.Smooth);
                    for (int j = 0; j < 4; ++j) {
                        Vector3d p = a + w * new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                        double v = value(p);
                        Assert.True(e.F.Lo - 1e-12 <= v && v <= e.F.Hi + 1e-12, type + " value");
                        const double d = 1e-6;
                        for (int ax = 0; ax < 3; ++ax) {
                            Vector3d q0 = p, q1 = p; q0[ax] -= d; q1[ax] += d;
                            double fd = (value(q1) - value(q0)) / (2 * d);
                            ExactInterval g = ax == 0 ? e.Gx : ax == 1 ? e.Gy : e.Gz;
                            Assert.True(g.Lo - 1e-6 <= fd && fd <= g.Hi + 1e-6, type + " gradient " + ax + ": " + fd + " not in [" + g.Lo + ", " + g.Hi + "]");
                        }
                    }
                    if (w == 0) {
                        // at a point the enclosures are tight: a wrong term or factor cannot hide in the width
                        Assert.True(e.F.Hi - e.F.Lo < 1e-10 && e.Gx.Hi - e.Gx.Lo < 1e-10 && e.Gy.Hi - e.Gy.Lo < 1e-10 && e.Gz.Hi - e.Gz.Lo < 1e-10, type + " width");
                    }
                }
            }
        }

        // ---------------------------------------------------------------- the pieces of the certificate

        [Fact]
        public void GradientTest_IsTheIntervalProductOfTwoCopies()
        {
            Func<double, double, ExactInterval> I = (a, b) => new ExactInterval(a, b);
            Assert.True(ContouringSmoothCertificate.GradientTest(I(1, 2), I(-0.1, 0.1), I(0, 0)));          // 1 - 0.01 > 0
            Assert.True(ContouringSmoothCertificate.GradientTest(I(-0.5, 1), I(2, 3), I(-1, 1)));           // -0.5 + 4 - 1 > 0
            Assert.False(ContouringSmoothCertificate.GradientTest(I(-1, 1), I(0.5, 0.6), I(0, 0)));         // -1 + 0.25: a square would pass
            Assert.False(ContouringSmoothCertificate.GradientTest(I(-1, 1), I(-1, 1), I(-1, 1)));
            Assert.False(ContouringSmoothCertificate.GradientTest(I(0, 0), I(0, 0), I(0, 0)));              // not > 0
            Assert.False(ContouringSmoothCertificate.GradientTest(I(1, 2), I(double.NegativeInfinity, double.PositiveInfinity), I(0, 0)));
            Assert.False(ContouringSmoothCertificate.GradientTest(I(double.NaN, double.NaN), I(1, 2), I(1, 2)));
            Assert.True(ContouringSmoothCertificate.GradientTest(I(-3, -2), I(0, 0), I(0, 0)));
            // a lower bound of exactly 0 does not pass: subnormal products whose widened sum is [0, ...]
            double e = double.Epsilon, a = Math.Pow(2, -537), b = Math.Sqrt(2) * a;
            Assert.Equal(e, a * a); Assert.Equal(2 * e, b * b);
            ExactInterval sum = I(b, b) * I(b, b) + I(b, b) * I(b, b) + I(a, a) * I(a, a);
            Assert.Equal(0, sum.Lo);
            Assert.False(ContouringSmoothCertificate.GradientTest(I(b, b), I(b, b), I(a, a)));
        }

        [Fact]
        public void CertifiedSign_NeedsAStrictBound()
        {
            Assert.Equal(1, ContouringSmoothCertificate.CertifiedSign(new ExactInterval(double.Epsilon, 1)));
            Assert.Equal(-1, ContouringSmoothCertificate.CertifiedSign(new ExactInterval(-1, -double.Epsilon)));
            Assert.Equal(0, ContouringSmoothCertificate.CertifiedSign(new ExactInterval(0, 1)));
            Assert.Equal(0, ContouringSmoothCertificate.CertifiedSign(new ExactInterval(-1, 0)));
            Assert.Equal(0, ContouringSmoothCertificate.CertifiedSign(new ExactInterval(-1, 1)));
            Assert.Equal(0, ContouringSmoothCertificate.CertifiedSign(new ExactInterval(0, 0)));
            Assert.Equal(0, ContouringSmoothCertificate.CertifiedSign(new ExactInterval(double.NaN, double.NaN)));
        }

        [Fact]
        public void EqualSpacing_AcceptsCubes_RefusesOtherCells()
        {
            double[] even = { 0, 0.25, 0.5, 0.75 }, twice = { 0, 0.25, 0.75 }, coarse = { 0, 0.5, 1 }, flat = { 0, 0, 0.25 }, back = { 0, 0.25, 0.125 };
            Assert.True(ContouringSmoothCertificate.EqualSpacing(even, even, even, 1e-6));
            Assert.False(ContouringSmoothCertificate.EqualSpacing(even, twice, even, 1e-6));
            Assert.False(ContouringSmoothCertificate.EqualSpacing(even, even, coarse, 1e-6));       // equal along each axis, not between the axes
            Assert.False(ContouringSmoothCertificate.EqualSpacing(flat, even, even, 1e-6));
            Assert.False(ContouringSmoothCertificate.EqualSpacing(even, back, even, 1e-6));
            double[] off = { 0, 0.25, 0.5 + 1e-9 };
            Assert.True(ContouringSmoothCertificate.EqualSpacing(even, off, even, 1e-6));
            Assert.False(ContouringSmoothCertificate.EqualSpacing(even, off, even, 1e-9));
            // rounding of origin + h * i: accepted for an ordinary grid, refused where the cell size nears the rounding unit
            Func<double, double, int, double[]> grid = (o, h, n) => Enumerable.Range(0, n + 1).Select(i => o + h * i).ToArray();
            Assert.True(ContouringSmoothCertificate.EqualSpacing(grid(-1.113, 2.2 / 32, 32), grid(632117.25, 2.2 / 32, 32), grid(-1.103, 2.2 / 32, 32), 1.0 / 1048576));
            Assert.False(ContouringSmoothCertificate.EqualSpacing(grid(1e9, 3e-7, 32), grid(1e9, 3e-7, 32), grid(1e9, 3e-7, 32), 1.0 / 1048576));
        }

        // the 48 symmetries of the cube as permutations of the corners c = x + 2 y + 4 z
        static IEnumerable<int[]> CubeSymmetries()
        {
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            foreach (int[] p in perms)
                for (int flip = 0; flip < 8; ++flip) {
                    int[] map = new int[8];
                    for (int c = 0; c < 8; ++c) {
                        int d = 0;
                        for (int ax = 0; ax < 3; ++ax) { int bit = (c >> ax & 1) ^ (flip >> ax & 1); d |= bit << p[ax]; }
                        map[c] = d;
                    }
                    yield return map;
                }
        }

        static int Canonical(int mask)
        {
            int best = int.MaxValue;
            foreach (int[] map in CubeSymmetries()) {
                int m = 0;
                for (int c = 0; c < 8; ++c) if ((mask >> c & 1) != 0) m |= 1 << map[c];
                best = Math.Min(best, Math.Min(m, ~m & 255));
            }
            return best;
        }

        [Fact]
        public void AdmittedConfiguration_IsNineOfTheFourteen()
        {
            var classes = new Dictionary<int, bool>();
            for (int mask = 0; mask < 256; ++mask) {
                int c = Canonical(mask);
                bool admitted = ContouringSmoothCertificate.AdmittedConfiguration(mask);
                if (classes.ContainsKey(c)) Assert.Equal(classes[c], admitted);         // a property of the class: symmetric, and the same for the complement
                else classes[c] = admitted;
            }
            Assert.Equal(14, classes.Count);
            Assert.Equal(9, classes.Count(kv => kv.Value));
            // the five excluded ones, by a representative (corners x + 2 y + 4 z)
            Assert.False(ContouringSmoothCertificate.AdmittedConfiguration(1 << 0 | 1 << 7));                         // a body diagonal
            Assert.False(ContouringSmoothCertificate.AdmittedConfiguration(1 << 0 | 1 << 3 | 1 << 5));                // three corners, pairwise across a face
            Assert.False(ContouringSmoothCertificate.AdmittedConfiguration(1 << 0 | 1 << 1 | 1 << 6 | 1 << 7));       // two opposite edges
            Assert.False(ContouringSmoothCertificate.AdmittedConfiguration(1 << 0 | 1 << 1 | 1 << 2 | 1 << 7));       // three on a face and the far corner
            Assert.False(ContouringSmoothCertificate.AdmittedConfiguration(1 << 0 | 1 << 3 | 1 << 5 | 1 << 6));       // alternate corners
            // the two admitted ones with a face of alternating signs
            Assert.True(ContouringSmoothCertificate.AdmittedConfiguration(1 << 0 | 1 << 3));                          // a face diagonal
            Assert.True(ContouringSmoothCertificate.AdmittedConfiguration(1 << 0 | 1 << 3 | 1 << 4));                 // an edge and a corner across a face
            Assert.True(ContouringSmoothCertificate.AdmittedConfiguration(0));
            Assert.True(ContouringSmoothCertificate.AdmittedConfiguration(255));
            Assert.True(ContouringSmoothCertificate.AdmittedConfiguration(0x0F));
        }

        // ---------------------------------------------------------------- the certificate on contours

        // In every cell the certificate does not list, the facts it claims hold at sampled points: no sign change of f, or
        // f smooth with pairwise acute gradients and one gradient component of constant sign.
        static int OracleViolations(TestField f, DualContouring dc, ContouringSmoothCertificate cert, int seed, int samples = 20)
        {
            var listed = new HashSet<Vector3i>(cert.UncertifiedCells.Select(u => u.Cell));
            var rnd = new Random(seed);
            Vector3i n = dc.CornerDimensions; Vector3d o = dc.Bounds.Min; double h = dc.CellSize;
            int bad = 0;
            for (int k = 0; k < n.z - 1; ++k) for (int j = 0; j < n.y - 1; ++j) for (int i = 0; i < n.x - 1; ++i) {
                if (listed.Contains(new Vector3i(i, j, k))) continue;
                var values = new List<double>(); var grads = new List<Vector3d>(); bool smooth = true;
                for (int q = 0; q < samples; ++q) {
                    Vector3d u = q < 8 ? new Vector3d(q & 1, q >> 1 & 1, q >> 2 & 1) : new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                    Vector3d p = new Vector3d(o.x + h * (i + u.x), o.y + h * (j + u.y), o.z + h * (k + u.z));
                    values.Add(f.At(p) - dc.IsoValue);
                    IntervalEnclosure e = f.Enclose(ExactInterval.Of(p.x), ExactInterval.Of(p.y), ExactInterval.Of(p.z));
                    if (!e.Smooth) smooth = false;
                    else grads.Add(new Vector3d(0.5 * (e.Gx.Lo + e.Gx.Hi), 0.5 * (e.Gy.Lo + e.Gy.Hi), 0.5 * (e.Gz.Lo + e.Gz.Hi)));
                }
                if (values.All(v => v > 0) || values.All(v => v < 0)) continue;          // consistent with zero-free
                bool acute = smooth && grads.All(a => grads.All(b => a.Dot(b) > 0));
                bool axis = grads.All(g => g.x > 0) || grads.All(g => g.x < 0) || grads.All(g => g.y > 0) || grads.All(g => g.y < 0) || grads.All(g => g.z > 0) || grads.All(g => g.z < 0);
                if (!acute || !axis) ++bad;
            }
            return bad;
        }

        [Fact]
        public void CappedGyroid_IsCertified_AndTheContourHasTheCertifiedTopology()
        {
            TestField f = Gyroid();
            DualContouring dc;
            ContouringSmoothCertificate cert = Run(f, 32, out dc);
            Assert.True(cert.Certified, List(cert));
            Assert.Equal(SmoothCertificateRunFailure.None, cert.RunFailure);
            Assert.True(cert.SurfaceCells > 500);
            Assert.Equal(cert.SurfaceCells, cert.PassedAtTightening.Sum());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(dc.Mesh));
            Assert.Equal(0, OracleViolations(f, dc, cert, 1, 12));
            // a second certified grid gives the same surface up to isotopy: components and genus agree
            DualContouring fine;
            ContouringSmoothCertificate certFine = Run(f, 36, out fine);
            Assert.True(certFine.Certified, List(certFine));
            Assert.Equal(Signature(fine.Mesh), Signature(dc.Mesh));
        }

        [Fact]
        public void Sphere_IsCertified_AlsoWithAnIsoValue_AndWithInsideGreater()
        {
            var f = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            DualContouring dc;
            Assert.True(Run(f, 16, out dc).Certified);
            Assert.Equal("c=1;g=0", Signature(dc.Mesh));
            // the level 0.1 of the same field (a sphere of radius 0.8)
            dc = Contour(f, Shift, 2.2, 16, 0.1);
            ContouringSmoothCertificate cert = Certify(f, dc);
            Assert.True(cert.Certified, List(cert));
            Assert.Equal(0, OracleViolations(f, dc, cert, 2));
            // inside where the value is greater: the negated field
            var g = new Negated(f);
            dc = Contour(g, Shift, 2.2, 16, 0, false);
            cert = Certify(g, dc);
            Assert.True(cert.Certified, List(cert));
            // the wrong convention for the same contour is a node sign mismatch everywhere on the surface
            var wrong = new ContouringSmoothCertificate { Field = f, Contour = dc };
            wrong.Compute();
            Assert.False(wrong.Certified);
            Assert.Contains(wrong.UncertifiedCells, u => u.Reason == SmoothCertificateReason.NodeSign);
        }

        public static IEnumerable<object[]> HardCases() => new[] {
            new object[] { "cassini-neck", 16 }, new object[] { "cassini-touching", 16 }, new object[] { "cassini-singular", 16 },
            new object[] { "two-spheres", 16 }, new object[] { "two-spheres", 32 }, new object[] { "gyroid-coarse", 12 }, new object[] { "small-sphere", 16 } };

        static TestField Hard(string name, out string truth)
        {
            switch (name) {
                case "cassini-neck": truth = "c=1;g=0"; return new Moved(new Cassini(0.45, 0.45 * 1.001), Similarity.Rotated);
                case "cassini-touching": truth = "c=2;g=0+0"; return new Moved(new Cassini(0.45, 0.45 * 0.999), Similarity.Rotated);
                case "cassini-singular": truth = null; return new Cassini(0.45, 0.45);
                case "two-spheres": truth = "c=2;g=0+0"; return new Min(new Sphere(new Vector3d(0.47, 0, 0), 0.45), new Sphere(new Vector3d(-0.47, 0, 0), 0.45));
                case "gyroid-coarse": truth = null; return new CappedTpms(TpmsType.Gyroid, 2 * Math.PI * 2 / 2.2, 0, 0.9, 4);
                default: truth = "c=1;g=0"; return new Sphere(new Vector3d(0.31, 0.27, 0.11), 0.04);          // smaller than a cell
            }
        }

        [Theory]
        [MemberData(nameof(HardCases))]
        public void HardCases_AreNotCertified_AndNoUnlistedCellViolatesTheOracle(string name, int n)
        {
            string truth;
            TestField f = Hard(name, out truth);
            DualContouring dc;
            ContouringSmoothCertificate cert = Run(f, n, out dc);
            Assert.False(cert.Certified, name);
            Assert.NotEmpty(cert.UncertifiedCells);
            Assert.Equal(0, OracleViolations(f, dc, cert, 5));
            // the list is sorted by (z, y, x), whatever the order of the blocks
            for (int i = 1; i < cert.UncertifiedCells.Count; ++i) {
                Vector3i p = cert.UncertifiedCells[i - 1].Cell, q = cert.UncertifiedCells[i].Cell;
                Assert.True(p.z < q.z || (p.z == q.z && (p.y < q.y || (p.y == q.y && p.x < q.x))), name + " order at " + i);
            }
            // the reason fits the case
            if (name == "two-spheres") Assert.Contains(cert.UncertifiedCells, u => u.Reason == SmoothCertificateReason.NotSmooth);
            else Assert.Contains(cert.UncertifiedCells, u => u.Reason == SmoothCertificateReason.GradientTest);
        }

        [Fact]
        public void AWrongContour_IsNeverCertified()
        {
            // contours whose topology is not the level set's (features below the cell size), found by comparing with the truth
            var cases = new List<KeyValuePair<TestField, string>>();
            double h = 2.2 / 16;
            // two balls 0.3 cells apart: the contour joins them
            cases.Add(new KeyValuePair<TestField, string>(new Min(new Sphere(new Vector3d(0.45 + 0.15 * h, 0.02, 0.01), 0.45), new Sphere(new Vector3d(-0.45 - 0.15 * h, 0.02, 0.01), 0.45)), "c=2;g=0+0"));
            // a ball smaller than a cell between the nodes: the contour is empty
            cases.Add(new KeyValuePair<TestField, string>(new Sphere(Shift + h * new Vector3d(7.5, 7.5, 7.5), 0.3 * h), "c=1;g=0"));
            // a Cassini surface with a neck of 0.04 cells, in several positions
            foreach (Similarity sim in new[] { Similarity.Rotated, new Similarity("r2", 1, Matrix3d.AxisAngleD(Vector3d.AxisY, 50) * Matrix3d.AxisAngleD(Vector3d.AxisZ, 20), new Vector3d(0.03, 0.01, 0.02)),
                new Similarity("r3", 1, Matrix3d.AxisAngleD(Vector3d.AxisX, 70) * Matrix3d.AxisAngleD(Vector3d.AxisZ, 40), new Vector3d(0.01, 0.04, 0.0)) })
                cases.Add(new KeyValuePair<TestField, string>(new Moved(new Cassini(0.45, 0.45 * 1.001), sim), "c=1;g=0"));
            int wrong = 0; string seen = "";
            foreach (var kv in cases) {
                DualContouring dc;
                ContouringSmoothCertificate cert = Run(kv.Key, 16, out dc);
                string sig = Signature(dc.Mesh);
                seen += sig + " ";
                if (sig != kv.Value) ++wrong;
                Assert.False(cert.Certified, sig);
            }
            Assert.True(wrong >= 2, "the cases must include wrong contours: " + seen);
        }

        [Fact]
        public void ACreaseWithASmallAngle_IsRefusedAsNotSmooth_ThoughItsGradientsPassTheTest()
        {
            // two spheres 0.02 apart: their union has a crease whose two normals differ by 1.6 degrees
            var f = new Min(new Sphere(Vector3d.Zero, 0.7), new Sphere(new Vector3d(0.02, 0, 0), 0.7));
            DualContouring dc;
            ContouringSmoothCertificate cert = Run(f, 16, out dc);
            Assert.False(cert.Certified);
            Assert.NotEmpty(cert.UncertifiedCells);
            Assert.All(cert.UncertifiedCells, u => Assert.Equal(SmoothCertificateReason.NotSmooth, u.Reason));
            Assert.Equal("c=1;g=0", Signature(dc.Mesh));
        }

        /// <summary>
        /// A test double whose enclosures say "smooth, gradient (1, 0, 0), a possible zero" for every box and give the true
        /// sign at a point: every cell then passes the gradient test, whatever its corner signs.
        /// </summary>
        sealed class Lying : TestField
        {
            readonly TestField f;
            public Lying(TestField f) { this.f = f; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z)
            {
                ExactInterval value = new ExactInterval(-1, 1);
                if (x.Lo == x.Hi && y.Lo == y.Hi && z.Lo == z.Hi) { double v = f.At(new Vector3d(x.Lo, y.Lo, z.Lo)); value = C(v); }
                return new IntervalEnclosure { F = value, Gx = C(1), Gy = C(0), Gz = C(0), Smooth = true };
            }
            public override double Value(ref Vector3d p) { return f.Value(ref p); }
        }

        [Fact]
        public void ACellWithAnExcludedConfiguration_IsRefused_EvenIfItPassesTheGradientTest()
        {
            // two small balls around two nodes on a body diagonal of the cell (5, 5, 5): its corner signs are a configuration
            // the published argument excludes. No real field can pass the gradient test there, so the enclosures are faked.
            double h = 2.2 / 16;
            Vector3d a = Shift + h * new Vector3d(5, 5, 5), b = Shift + h * new Vector3d(6, 6, 6);
            var f = new Lying(new Min(new Sphere(a, 0.3 * h), new Sphere(b, 0.3 * h)));
            DualContouring dc;
            ContouringSmoothCertificate cert = Run(f, 16, out dc);
            Assert.False(cert.Certified);
            var refused = cert.UncertifiedCells.Where(u => u.Reason == SmoothCertificateReason.Configuration).ToList();
            Assert.Single(refused);
            Assert.Equal(new Vector3i(5, 5, 5), refused[0].Cell);
            // the 14 other cells at the two nodes have one inside corner each: admitted, and with these enclosures certified
            Assert.DoesNotContain(cert.UncertifiedCells, u => u.Cell == new Vector3i(4, 4, 4) || u.Cell == new Vector3i(6, 6, 6));
        }

        [Fact]
        public void CellsThatFailTheMeshSide_AreListedOnce()
        {
            var f = new Sphere(Vector3d.Zero, 0.7);
            DualContouring dc = Contour(f, Shift, 2.2, 16);
            Assert.True(Certify(f, dc).Certified);
            dc.CertificateMeshCells.Add(new KeyValuePair<Vector3i, int>(new Vector3i(3, 4, 5), 1));
            dc.CertificateMeshCells.Add(new KeyValuePair<Vector3i, int>(new Vector3i(3, 4, 5), 1));
            ContouringSmoothCertificate cert = Certify(f, dc);
            Assert.False(cert.Certified);
            Assert.Single(cert.UncertifiedCells);
            Assert.Equal(new Vector3i(3, 4, 5), cert.UncertifiedCells[0].Cell);
            Assert.Equal(SmoothCertificateReason.MeshSide, cert.UncertifiedCells[0].Reason);
        }

        [Fact]
        public void NodeOnTheLevelSet_IsRefusedAtThatNode()
        {
            // a dyadic grid with six nodes exactly on the sphere: f = 0 there
            var f = new PolySphere(0.5625);
            DualContouring dc = Contour(f, new Vector3d(-1.125, -1.125, -1.125), 2.25, 16);
            ContouringSmoothCertificate cert = Certify(f, dc);
            Assert.False(cert.Certified);
            Assert.All(cert.UncertifiedCells, u => Assert.Equal(SmoothCertificateReason.NodeSign, u.Reason));
            Assert.Equal(6 * 8, cert.UncertifiedCells.Count);            // the eight cells around each of the six nodes
            // the same sphere on a shifted grid is certified
            DualContouring shifted;
            Assert.True(Run(f, 16, out shifted).Certified);
        }

        [Fact]
        public void AContourOfAnotherFunction_IsRefusedByItsNodeSigns()
        {
            var f = new Sphere(Vector3d.Zero, 0.7);
            var other = new Shifted(f, 0.12);          // the contour is of the sphere of radius 0.58
            DualContouring dc = Contour(other, Shift, 2.2, 16);
            ContouringSmoothCertificate cert = Certify(other, dc);
            Assert.False(cert.Certified);
            Assert.Contains(cert.UncertifiedCells, u => u.Reason == SmoothCertificateReason.NodeSign);
            // also where the certificate's field has no zero at all: every node of the grid is compared
            var far = new Shifted(new Sphere(Vector3d.Zero, 0.2), -0.5);        // enclosures: radius 0.2; contour: radius 0.7
            dc = Contour(far, Shift, 2.2, 16);
            cert = Certify(far, dc);
            Assert.False(cert.Certified);
            Assert.Contains(cert.UncertifiedCells, u => u.Reason == SmoothCertificateReason.NodeSign);
            Assert.True(dc.Mesh.TriangleCount > 0);
        }

        /// <summary> the contour sees the opposite sign at one point (a grid node); the enclosures are the field's </summary>
        sealed class FlippedAt : TestField
        {
            readonly TestField f; readonly Vector3d at;
            public FlippedAt(TestField f, Vector3d at) { this.f = f; this.at = at; }
            public override IntervalEnclosure Enclose(ExactInterval x, ExactInterval y, ExactInterval z) { return f.Enclose(x, y, z); }
            public override double Value(ref Vector3d p) { double v = f.Value(ref p); return p == at ? -v : v; }
        }

        [Theory]
        [InlineData(0.04, false)]      // the node next to the surface: all eight cells around it are crossed
        [InlineData(1.5, false)]       // one to two cells away: cells proven zero-free one by one
        [InlineData(4.3, false)]       // far away: inside a block of cells proven zero-free as a whole (not 4.0: the surface would pass through a node)
        [InlineData(-1.5, false)]
        public void OneNodeWithTheWrongSign_RefusesExactlyTheEightCellsAroundIt(double cellsFromSurface, bool rotated)
        {
            WrongNode(cellsFromSurface, rotated);
        }

        [Fact]
        public void OneNodeWithTheWrongSign_InARotatedFrame_AtManyDistances()
        {
            // in a rotated frame the enclosure of a whole cell is loose, so at some distances the cells around the node are
            // proven zero-free only by their sub-boxes; the sweep covers surface cells, such cells and zero-free boxes
            for (int i = 0; i <= 44; ++i) {
                double d = -1.2 + 0.0625 * i + 0.013;         // -1.19 .. 1.56 cells, never a multiple of the cell size
                WrongNode(d, true);
            }
        }

        static void WrongNode(double cellsFromSurface, bool rotated)
        {
            const int n = 24;
            double h = 2.2 / n;
            Vector3i node = new Vector3i(16, 11, 12);
            Vector3d at = new Vector3d(Shift.x + h * node.x, Shift.y + h * node.y, Shift.z + h * node.z);
            // a sphere whose surface passes at the given distance from the node, on the axis through its centre
            Vector3d centre = at - new Vector3d(0.4 + cellsFromSurface * h, 0, 0);
            TestField sphere = rotated ? (TestField)new Moved(new Sphere(Similarity.Rotated.Inverse(centre), 0.4), Similarity.Rotated) : new Sphere(centre, 0.4);
            DualContouring clean = Contour(sphere, Shift, 2.2, n);
            Assert.True(Certify(sphere, clean).Certified);
            var f = new FlippedAt(sphere, at);
            DualContouring dc = Contour(f, Shift, 2.2, n);
            ContouringSmoothCertificate cert = Certify(f, dc);
            Assert.False(cert.Certified);
            Assert.All(cert.UncertifiedCells, u => Assert.Equal(SmoothCertificateReason.NodeSign, u.Reason));
            var expected = new HashSet<Vector3i>();
            for (int c = 0; c < 8; ++c) expected.Add(new Vector3i(node.x - (c & 1), node.y - (c >> 1 & 1), node.z - (c >> 2 & 1)));
            Assert.Equal(expected, new HashSet<Vector3i>(cert.UncertifiedCells.Select(u => u.Cell)));
            Assert.Equal(8, cert.UncertifiedCells.Count);
        }

        [Fact]
        public void PiecesOfAMin_AreDecidedBySubBoxes_WhereTheWholeCellCannot()
        {
            // two balls two cells apart: near the gap a whole cell cannot tell which ball is nearer, its sub-boxes can
            double h = 2.2 / 16;
            var f = new Min(new Sphere(new Vector3d(0.35 + h, 0.02, 0.01), 0.35), new Sphere(new Vector3d(-0.35 - h, 0.02, 0.01), 0.35));
            DualContouring dc;
            ContouringSmoothCertificate cert = Run(f, 16, out dc);
            Assert.True(cert.Certified, List(cert));
            Assert.True(cert.PassedAtTightening[1] + cert.PassedAtTightening[2] > 0);
            Assert.Equal("c=2;g=0+0", Signature(dc.Mesh));
            Assert.Equal(0, OracleViolations(f, dc, cert, 6));
            var none = new ContouringSmoothCertificate { Field = f, Contour = dc, MaxTightening = 0 };
            none.Compute();
            Assert.Contains(none.UncertifiedCells, u => u.Reason == SmoothCertificateReason.NotSmooth);
        }

        [Fact]
        public void RunLevelRefusals()
        {
            var f = new Sphere(Vector3d.Zero, 0.7);
            // no record
            var plain = new DualContouring { Implicit = f, InsideIsLess = true, CellSize = 2.2 / 16, Simplify = false, Bounds = new AxisAlignedBox3d(Shift, Shift + 2.2 * Vector3d.One) };
            plain.Generate();
            ContouringSmoothCertificate cert = Certify(f, plain);
            Assert.False(cert.Certified);
            Assert.Equal(SmoothCertificateRunFailure.NoRecord, cert.RunFailure);
            // the level set leaves the grid
            DualContouring dc = Contour(f, new Vector3d(-0.5, -1.1, -1.1), 2.2, 16);
            cert = Certify(f, dc);
            Assert.False(cert.Certified);
            Assert.True((cert.RunFailure & SmoothCertificateRunFailure.OuterLayer) != 0);
            Assert.Contains(cert.UncertifiedCells, u => u.Reason == SmoothCertificateReason.OuterLayer);
            // ... also when it only touches the outermost layer of cells, on the low or the high side of any axis
            foreach (Vector3d o in new[] { new Vector3d(-0.75, -1.1, -1.1), new Vector3d(-1.45, -1.1, -1.1), new Vector3d(-1.1, -0.75, -1.1),
                new Vector3d(-1.1, -1.45, -1.1), new Vector3d(-1.1, -1.1, -0.75), new Vector3d(-1.1, -1.1, -1.45) }) {
                dc = Contour(f, o, 2.2, 16);
                cert = Certify(f, dc);
                Assert.True((cert.RunFailure & SmoothCertificateRunFailure.OuterLayer) != 0, o.ToString());
                Assert.All(cert.UncertifiedCells, u => Assert.Equal(SmoothCertificateReason.OuterLayer, u.Reason));
            }
            // the mesh side: a vertex moved out of its cell after the contouring
            dc = Contour(f, Shift, 2.2, 16);
            Assert.True(Certify(f, dc).Certified);
            int vid = dc.Mesh.VertexIndices().First();
            dc.Mesh.SetVertex(vid, dc.Mesh.GetVertex(vid) + new Vector3d(0.5, 0.5, 0.5));
            dc.CheckCertificateMeshSideForTests();
            cert = Certify(f, dc);
            Assert.False(cert.Certified);
            Assert.True((cert.RunFailure & SmoothCertificateRunFailure.MeshSide) != 0);
            Assert.Throws<ArgumentNullException>(() => new ContouringSmoothCertificate { Contour = dc }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContouringSmoothCertificate { Field = f, Contour = dc, MaxTightening = 5 }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContouringSmoothCertificate { Field = f, Contour = dc, MaxTightening = int.MaxValue }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContouringSmoothCertificate { Field = f, Contour = dc, MaxTightening = -1 }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContouringSmoothCertificate { Field = f, Contour = dc, CellShapeTolerance = 0.5 }.Compute());
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContouringSmoothCertificate { Field = f, Contour = dc, CellShapeTolerance = double.NaN }.Compute());
            Assert.Throws<ArgumentNullException>(() => new ContouringSmoothCertificate { Field = f }.Compute());
            // an exception of the field is the same in both paths
            var throwing = new Throwing();
            DualContouring any = Contour(f, Shift, 2.2, 16);
            Assert.Throws<InvalidOperationException>(() => new ContouringSmoothCertificate { Field = throwing, Contour = any, ParallelCompute = false }.Compute());
            Assert.Throws<InvalidOperationException>(() => new ContouringSmoothCertificate { Field = throwing, Contour = any, ParallelCompute = true }.Compute());
        }

        [Fact]
        public void AContourThatIsNotTheRecordedDenseRun_IsRefused()
        {
            var f = new Sphere(Vector3d.Zero, 0.7);
            Func<DualContouring> fresh = () => Contour(f, Shift, 2.2, 16);
            Action<DualContouring, string> refused = (dc, what) => {
                ContouringSmoothCertificate cert = Certify(f, dc);
                Assert.False(cert.Certified, what);
                Assert.Equal(SmoothCertificateRunFailure.NoRecord, cert.RunFailure);
            };
            DualContouring d = fresh();
            Assert.True(Certify(f, d).Certified);
            Assert.True(d.CertificateRun.HasValue);
            // the record switched on only after the run
            var late = new DualContouring { Implicit = f, InsideIsLess = true, CellSize = 2.2 / 16, Simplify = false, Bounds = new AxisAlignedBox3d(Shift, Shift + 2.2 * Vector3d.One) };
            late.Generate();
            late.RecordCertificateData = true;
            refused(late, "flag set after the run");
            // a field of the contour changed after the run: its node flags and grid would no longer be the run's
            d = fresh(); d.IsoValue = 0.1; refused(d, "IsoValue");
            d = fresh(); d.InsideIsLess = false; refused(d, "InsideIsLess");
            d = fresh(); d.CellSize = 2.2 / 8; refused(d, "CellSize");
            d = fresh(); d.Bounds = new AxisAlignedBox3d(Shift + 0.01 * Vector3d.One, Shift + 2.2 * Vector3d.One); refused(d, "Bounds");
            d = fresh(); d.CornerDimensions = new Vector3i(9, 9, 9); refused(d, "CornerDimensions");
            d = fresh(); d.RecordCertificateData = false; refused(d, "record switched off");
            d = fresh(); d.Mesh = null; refused(d, "no mesh");
            // the samples taken again without a new mesh
            d = fresh(); d.FindCrossings(); refused(d, "FindCrossings after the run");
            // a simplified run (it only happens without the record) after a recorded one on the same instance
            d = fresh();
            d.RecordCertificateData = false; d.Simplify = true;
            d.Generate();
            Assert.False(d.CertificateRun.HasValue);
            d.RecordCertificateData = true;
            refused(d, "simplified run after a recorded one");
            // the same instance used again in sparse mode: the dense grid's dimensions are stale, the run is not dense
            d = fresh();
            var cells = new List<Vector3i>();
            for (int k = 0; k < 16; ++k) for (int j = 0; j < 16; ++j) for (int i = 0; i < 16; ++i) cells.Add(new Vector3i(i, j, k));
            double h = 2.2 / 16;
            d.CandidateCells = cells;
            d.NodeValueF = n => f.At(new Vector3d(Shift.x + h * n.x, Shift.y + h * n.y, Shift.z + h * n.z));
            d.FindCrossings();
            d.Generate();
            Assert.True(d.Mesh.TriangleCount > 0);
            Assert.True(d.CertificateRun.HasValue && !d.CertificateRun.Value.Dense);
            refused(d, "sparse run on a reused instance");
            // a cancelled run
            var cancelled = new DualContouring { Implicit = f, InsideIsLess = true, CellSize = 2.2 / 16, Simplify = false, Bounds = new AxisAlignedBox3d(Shift, Shift + 2.2 * Vector3d.One), RecordCertificateData = true, CancelF = () => true };
            cancelled.Generate();
            Assert.False(cancelled.CertificateRun.HasValue);
        }

        [Fact]
        public void Tightening_CertifiesMoreCells_AndIsNeededForACoarseGrid()
        {
            TestField f = new Moved(new Sphere(Vector3d.Zero, 0.7), Similarity.Rotated);
            DualContouring dc = Contour(f, Shift, 2.2, 12);
            var none = new ContouringSmoothCertificate { Field = f, Contour = dc, MaxTightening = 0 };
            none.Compute();
            var two = new ContouringSmoothCertificate { Field = f, Contour = dc, MaxTightening = 2 };
            two.Compute();
            Assert.False(none.Certified);
            Assert.True(two.Certified, List(two));
            Assert.True(two.PassedAtTightening[1] + two.PassedAtTightening[2] > 0);
            Assert.True(two.SurfaceCells <= none.SurfaceCells);        // sub-boxes also prove more cells zero-free
            Assert.Equal(0, OracleViolations(f, dc, two, 9));
        }

        // ---------------------------------------------------------------- invariance, reuse, determinism

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Invariance_UnderSimilarities(string name)
        {
            Similarity sim = Similarity.All().First(s => s.Name == name);
            var baseField = new Sphere(new Vector3d(0.03, -0.02, 0.01), 0.7);
            const int n = 20;
            DualContouring dc0 = Contour(baseField, Shift, 2.2, n);
            ContouringSmoothCertificate c0 = Certify(baseField, dc0);
            Assert.True(c0.Certified, List(c0));

            // the same shape in the moved frame, on the axis-aligned grid at the image of the grid's corner
            TestField f = sim.IsExact ? (TestField)new Sphere(sim.Scale * new Vector3d(0.03, -0.02, 0.01), sim.Scale * 0.7) : new Moved(baseField, sim);
            Vector3d origin = sim.IsExact ? sim.Scale * Shift : sim.Apply(Vector3d.Zero) + sim.Scale * Shift;
            DualContouring dc = Contour(f, origin, sim.Scale * 2.2, n);
            ContouringSmoothCertificate c = Certify(f, dc);
            Assert.Equal(0, OracleViolations(f, dc, c, 4));
            Assert.Equal("c=1;g=0", Signature(dc.Mesh));
            if (sim.IsExact) {
                // a power of two: every interval operation scales exactly, so the result is the same bit for bit
                Assert.Equal(List(c0), List(c));
            } else if (sim == Similarity.RotatedUtmMillimetre) {
                // a cell of 1.1e-4 at coordinates of 5.7e6: the node spacings differ by up to one rounding unit of the
                // coordinate, 8e-6 of the cell, so the cells are not cubes within the tolerance and the run is refused for
                // that reason alone (every cell passes)
                Assert.False(c.Certified);
                Assert.Equal(SmoothCertificateRunFailure.CellShape, c.RunFailure);
                Assert.Empty(c.UncertifiedCells);
            } else {
                // another frame: other cells, the same verdict for this well resolved shape
                Assert.True(c.Certified, name + ": " + List(c));
                Assert.InRange(c.SurfaceCells, c0.SurfaceCells / 2, c0.SurfaceCells * 3);
            }
        }

        [Fact]
        public void Invariance_PowerOfTwoScales_OfATpms_AreBitIdentical()
        {
            DualContouring dc;
            string reference = List(Run(Gyroid(), 24, out dc));
            foreach (double s in new[] { 1.0 / 1024, 1024, Math.Pow(2, 40), Math.Pow(2, -40) }) {
                DualContouring scaled;
                Assert.Equal(reference, List(Run(Gyroid(s), 24, out scaled, s)));
            }
        }

        [Fact]
        public void Reuse_ComputeTwice_GivesTheSameResult()
        {
            TestField f = Gyroid();
            DualContouring dc = Contour(f, Shift, 2.2, 24);
            var cert = new ContouringSmoothCertificate { Field = f, Contour = dc };
            cert.Compute();
            string first = List(cert);
            cert.Compute();
            Assert.Equal(first, List(cert));
            // and on another contour with the same instance
            TestField g = new Sphere(Vector3d.Zero, 0.7);
            DualContouring dc2 = Contour(g, Shift, 2.2, 16);
            cert.Field = g; cert.Contour = dc2;
            cert.Compute();
            Assert.Equal(List(Certify(g, dc2)), List(cert));
            cert.Field = f; cert.Contour = dc;
            cert.Compute();
            Assert.Equal(first, List(cert));
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial_AndRepeats()
        {
            foreach (string name in new[] { "gyroid", "two-spheres", "cassini-neck" }) {
                string truth;
                TestField f = name == "gyroid" ? Gyroid() : Hard(name, out truth);
                DualContouring dc = Contour(f, Shift, 2.2, 24);
                string serial = List(Certify(f, dc, false));
                for (int run = 0; run < 3; ++run)
                    Assert.Equal(serial, List(Certify(f, dc, true)));
                Assert.Equal(serial, List(Certify(f, dc, false)));
            }
        }
    }
}
