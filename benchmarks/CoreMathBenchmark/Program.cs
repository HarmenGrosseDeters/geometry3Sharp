// Cost of the correctly rounded CoreMath (sin, cos, tan, atan2, acos, exp, log, and the fused multiply-add) against System.Math, in ns per call.
// net10.0: CoreMath.FusedMultiplyAdd is Math.FusedMultiplyAdd (hardware); net48 (the netstandard2.0 build): the software version.
using System;
using System.Diagnostics;
using g3;
using g3.CoreMathPort;

static class Program
{
    const int N = 1 << 16;

    static void Main(string[] args)
    {
        if (args != null && args.Length > 0 && args[0] == "interval") {
            RunExactInterval();
            return;
        }
        var rnd = new Random(1);
        var a = new double[N]; var b = new double[N]; var pos = new double[N]; var wide = new double[N]; var unit = new double[N];
        for (int i = 0; i < N; ++i) {
            a[i] = (rnd.NextDouble() - 0.5) * 12.0;                  // angles of a few turns
            b[i] = (rnd.NextDouble() - 0.5) * 40.0;                  // exp arguments
            pos[i] = rnd.NextDouble() * 100 + 1e-3;                  // log arguments
            wide[i] = (rnd.NextDouble() - 0.5) * 2e3;
            unit[i] = rnd.NextDouble() * 2 - 1;                      // acos arguments
        }
        Console.WriteLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            + ", arch " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        Console.WriteLine();
        Console.WriteLine("| function | System.Math (ns/call) | CoreMath (ns/call) | ratio |");
        Console.WriteLine("|---|---|---|---|");
        Row("sin", a, x => Math.Sin(x), x => CoreMath.Sin(x));
        Row("cos", a, x => Math.Cos(x), x => CoreMath.Cos(x));
        Row("sin (|x| < 1000)", wide, x => Math.Sin(x), x => CoreMath.Sin(x));
        Row("tan", a, x => Math.Tan(x), x => CoreMath.Tan(x));
        Row("tan (|x| < 1000)", wide, x => Math.Tan(x), x => CoreMath.Tan(x));
        Row("acos", unit, x => Math.Acos(x), x => CoreMath.Acos(x));
        Row("acos, composed AcosByAtan2 (not correctly rounded)", unit, x => Math.Acos(x), x => CoreMath.AcosByAtan2(x));
        Row("atan2(x, 1.7)", a, x => Math.Atan2(x, 1.7), x => CoreMath.Atan2(x, 1.7));
        Row("exp", b, x => Math.Exp(x), x => CoreMath.Exp(x));
        Row("log", pos, x => Math.Log(x), x => CoreMath.Log(x));
        Console.WriteLine();
        Console.WriteLine("| fused multiply-add | helper (ns/call) | software version (ns/call) |");
        Console.WriteLine("|---|---|---|");
        double t1 = Time(a, x => CoreMath.FusedMultiplyAdd(x, 1.0000001, 0.1));
        double t2 = Time(a, x => CoreMath.SoftwareFusedMultiplyAdd(x, 1.0000001, 0.1));
        Console.WriteLine($"| x * 1.0000001 + 0.1 | {t1:F1} | {t2:F1} |");
    }

    static double sink;

    static double Time(double[] xs, Func<double, double> f)
    {
        double s = 0;
        for (int i = 0; i < xs.Length; ++i) s += f(xs[i]);     // warm-up (JIT)
        var times = new double[5];
        for (int r = 0; r < times.Length; ++r) {
            var sw = Stopwatch.StartNew();
            for (int rep = 0; rep < 8; ++rep)
                for (int i = 0; i < xs.Length; ++i) s += f(xs[i]);
            times[r] = sw.Elapsed.TotalMilliseconds * 1e6 / (8.0 * xs.Length);
        }
        sink += s;
        Array.Sort(times);
        return times[times.Length / 2];
    }

    static void Row(string name, double[] xs, Func<double, double> m, Func<double, double> c)
    {
        double tm = Time(xs, m), tc = Time(xs, c);
        Console.WriteLine($"| {name} | {tm:F1} | {tc:F1} | {tc / tm:F1} |");
    }

    static double MedianMs(Func<double> body, int runs)
    {
        sink += body(); // JIT warm-up
        var t = new double[runs];
        for (int r = 0; r < runs; ++r) {
            var sw = Stopwatch.StartNew();
            sink += body();
            t[r] = sw.Elapsed.TotalMilliseconds;
        }
        Array.Sort(t);
        return t[runs / 2];
    }

    static DMesh3 Grid(double half, int n, Func<double, double, double> h)
    {
        var m = new DMesh3();
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                double x = -half + 2 * half * i / (n - 1), y = -half + 2 * half * j / (n - 1);
                m.AppendVertex(new Vector3d(x, y, h(x, y)));
            }
        for (int j = 0; j < n - 1; ++j)
            for (int i = 0; i < n - 1; ++i) {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                if (((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
            }
        return m;
    }

    static void RunExactInterval()
    {
        const int M = 4096;
        const int Loops = 20000;
        var rnd = new Random(2009);
        var normal = new double[M];
        var mixed = new double[M];
        var rhs = new double[M];
        for (int i = 0; i < M; ++i) {
            double v = (rnd.NextDouble() - 0.5) * 200.0;
            if (v == 0.0) v = 1.0;
            normal[i] = v;
            rhs[i] = 1.0 + rnd.NextDouble() * 10.0;
            mixed[i] = (i & 31) == 0 ? 0.0
                : (i & 31) == 1 ? (i % 64 == 1 ? -double.Epsilon : double.Epsilon)
                : (i & 31) == 2 ? (rnd.NextDouble() - 0.5) * 1e-300
                : v;
        }
        Console.WriteLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            + ", arch " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        Console.WriteLine();
        Console.WriteLine("| benchmark | median (ms) | ns / op |");
        Console.WriteLine("|---|---:|---:|");

        double tUp = MedianMs(() => {
            double s = 0;
            for (int k = 0; k < Loops; ++k)
                for (int i = 0; i < M; ++i) s += ExactInterval.Up(normal[i]);
            return s;
        }, 7);
        Console.WriteLine($"| ExactInterval.Up (normal, 4096 x 20k) | {tUp:F1} | {tUp * 1e6 / ((double)M * Loops):F2} |");

        double tUpDown = MedianMs(() => {
            double s = 0;
            for (int k = 0; k < Loops; ++k)
                for (int i = 0; i < M; ++i) s += ExactInterval.Up(mixed[i]) + ExactInterval.Down(mixed[i]);
            return s;
        }, 7);
        Console.WriteLine($"| ExactInterval.Up + Down (mixed, 4096 x 20k) | {tUpDown:F1} | {tUpDown * 1e6 / (2.0 * M * Loops):F2} |");

        const int ArithLoops = 4000;
        double tArith = MedianMs(() => {
            double s = 0;
            for (int k = 0; k < ArithLoops; ++k)
                for (int i = 0; i < M; ++i) {
                    ExactInterval a = ExactInterval.Of(normal[i]), b = ExactInterval.Of(rhs[i]);
                    ExactInterval q = ExactInterval.Sqrt(ExactInterval.Square(a - b) + b * b) / b;
                    s += q.Lo + q.Hi;
                }
            return s;
        }, 7);
        Console.WriteLine($"| ExactInterval kernel sqrt((a-b)^2+b^2)/b (4096 x 4k) | {tArith:F1} | {tArith * 1e6 / ((double)M * ArithLoops):F2} |");

        DMesh3 plane = Grid(1, 41, (x, y) => 0);
        DMesh3 terrain = Grid(20, 41, (x, y) => 3 * Math.Sin(x / 7 + 1) * Math.Sin(y / 5) + 0.8 * Math.Sin(x * 1.3) * Math.Cos(y * 1.1));
        DMesh3 saddle = Grid(1, 41, (x, y) => 0.3 * (x * x - y * y));
        int src = (41 / 2) * 41 + 41 / 2;

        double tPlane = MedianMs(() => { var g = new ExactMeshGeodesicDistance(plane); g.Compute(new[] { src }); return g.Upper[0]; }, 5);
        Console.WriteLine($"| ExactMeshGeodesicDistance plane 41x41 | {tPlane:F1} | - |");
        double tTerrain = MedianMs(() => { var g = new ExactMeshGeodesicDistance(terrain); g.Compute(new[] { src }); return g.Upper[0]; }, 5);
        Console.WriteLine($"| ExactMeshGeodesicDistance terrain 41x41 | {tTerrain:F1} | - |");
        double tSaddle = MedianMs(() => { var g = new ExactMeshGeodesicDistance(saddle); g.Compute(new[] { src }); return g.Upper[0]; }, 3);
        Console.WriteLine($"| ExactMeshGeodesicDistance saddle 41x41 | {tSaddle:F1} | - |");
    }
}
