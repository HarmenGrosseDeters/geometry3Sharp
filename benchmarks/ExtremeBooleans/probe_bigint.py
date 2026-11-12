# Scratch instrumentation of math/ExactPredicates.cs for docs/perf/EXTREME_RUNTIME_BOOLEANS.md (lever B1): counts the
# BigInteger exact stages of Orient3d / Orient2d, their time, how many are exactly zero, how many have nonzero
# coordinate magnitudes within a span of 2^200 (a power-of-two scaling would bring them into the expansion range), and
# how many a "leading-term" certificate would decide (coordinates below 2^(Emax-200) flushed to 0, the exact sign of the
# flushed determinant kept if its magnitude exceeds a bound on what the flushed parts can contribute).
# Applied to a throw-away worktree, never committed to the library:
#     git worktree add --detach <wt> <commit>; python3 probe_bigint.py <wt>/math/ExactPredicates.cs
#     dotnet build <wt>/benchmarks/BooleanComparison -c Release -o <tool>
# With G3_BIGINT_PROBE=1 the process prints one line "BIGINT_PROBE {...}" on stderr at exit.
import sys
path = sys.argv[1]; s = open(path).read()
def rep(old, new):
    global s
    if s.count(old) != 1: raise SystemExit("anchor not unique or missing: " + old[:80])
    s = s.replace(old, new)
rep("""        internal static int orient2d_bigint(Vector2d a, Vector2d b, Vector2d c)
        {""", """        internal static int orient2d_bigint(Vector2d a, Vector2d b, Vector2d c)
        {
            if (BigProbe.On == false) return orient2d_bigint0(a, b, c);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int r = orient2d_bigint0(a, b, c);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            double[] v = { a.x, a.y, b.x, b.y, c.x, c.y };
            bool scal = BigProbe.Span(v) <= 200;
            int cert = 0;
            if (r != 0 && scal == false) {
                double[] f = BigProbe.Flush(v, out double delta, out double M);
                BigInteger[] w = to_integers(f); int e = BigProbe.Emin(f);
                BigInteger D = (w[2] - w[0]) * (w[5] - w[1]) - (w[3] - w[1]) * (w[4] - w[0]);
                if (D.IsZero == false && BigProbe.Log2(D, 2 * e) > System.Math.Log2(8 * delta * (2 * M + 2 * delta)) + 1)
                    cert = D.Sign == r ? 1 : -1;
            }
            BigProbe.Add(1, t1 - t0, r == 0, scal, cert);
            return r;
        }
        static int orient2d_bigint0(Vector2d a, Vector2d b, Vector2d c)
        {""")
rep("""        internal static int orient3d_bigint(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {""", """        internal static int orient3d_bigint(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if (BigProbe.On == false) return orient3d_bigint0(a, b, c, d);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int r = orient3d_bigint0(a, b, c, d);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            double[] v = { a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z };
            bool scal = BigProbe.Span(v) <= 200;
            int cert = 0;
            if (r != 0 && scal == false) {
                double[] f = BigProbe.Flush(v, out double delta, out double M);
                BigInteger[] p = to_integers(f); int e = BigProbe.Emin(f);
                BigInteger ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2];
                BigInteger vx = p[6] - p[0], vy = p[7] - p[1], vz = p[8] - p[2];
                BigInteger wx = p[9] - p[0], wy = p[10] - p[1], wz = p[11] - p[2];
                BigInteger D = wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
                double m = 2 * M + 2 * delta;
                if (D.IsZero == false && BigProbe.Log2(D, 3 * e) > System.Math.Log2(36 * delta * m * m) + 1)
                    cert = D.Sign == r ? 1 : -1;
            }
            BigProbe.Add(0, t1 - t0, r == 0, scal, cert);
            return r;
        }
        static int orient3d_bigint0(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {""")
rep("""    internal static class Expansion
    {""", """    internal static class BigProbe
    {
        public static readonly bool On = System.Environment.GetEnvironmentVariable("G3_BIGINT_PROBE") == "1";
        static long[] n = new long[2], ticks = new long[2], zero = new long[2], scal = new long[2], cert = new long[2], wrong = new long[2];
        static BigProbe()
        {
            if (On) System.AppDomain.CurrentDomain.ProcessExit += (o, e) => System.Console.Error.WriteLine(
                "BIGINT_PROBE {\\"orient3d\\": " + Line(0) + ", \\"orient2d\\": " + Line(1) + "}");
        }
        static string Line(int k) => "{\\"calls\\": " + n[k] + ", \\"ms\\": " + (ticks[k] * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
            + ", \\"zero\\": " + zero[k] + ", \\"span_le_200\\": " + scal[k] + ", \\"certified\\": " + cert[k] + ", \\"certificate_wrong\\": " + wrong[k] + "}";
        public static void Add(int k, long t, bool z, bool s, int c)
        {
            System.Threading.Interlocked.Increment(ref n[k]); System.Threading.Interlocked.Add(ref ticks[k], t);
            if (z) System.Threading.Interlocked.Increment(ref zero[k]);
            if (s) System.Threading.Interlocked.Increment(ref scal[k]);
            if (c == 1) System.Threading.Interlocked.Increment(ref cert[k]);
            if (c == -1) System.Threading.Interlocked.Increment(ref wrong[k]);
        }
        static int Exp(double x) => System.Math.ILogB(x);
        // exponent span of the nonzero magnitudes
        public static int Span(double[] v)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (double x in v) if (x != 0) { int e = Exp(x); lo = System.Math.Min(lo, e); hi = System.Math.Max(hi, e); }
            return hi == int.MinValue ? 0 : hi - lo;
        }
        public static double[] Flush(double[] v, out double delta, out double M)
        {
            double mx = 0; foreach (double x in v) mx = System.Math.Max(mx, System.Math.Abs(x));
            double t = System.Math.ScaleB(mx, -200); delta = t; M = 2 * mx;
            double[] f = new double[v.Length];
            for (int i = 0; i < v.Length; ++i) f[i] = System.Math.Abs(v[i]) < t ? 0 : v[i];
            return f;
        }
        public static int Emin(double[] v)
        {
            int e = int.MaxValue;
            foreach (double x in v) if (x != 0) {
                long bits = System.BitConverter.DoubleToInt64Bits(x); int be = (int)((bits >> 52) & 0x7FF);
                e = System.Math.Min(e, be == 0 ? -1074 : be - 1075);
            }
            return e == int.MaxValue ? 0 : e;
        }
        public static double Log2(BigInteger D, int scale) => BigInteger.Log(BigInteger.Abs(D), 2) + scale;
    }

    internal static class Expansion
    {""")
open(path, "w").write(s)
