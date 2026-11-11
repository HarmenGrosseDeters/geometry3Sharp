// Cost of the correctly rounded CoreMath (sin, cos, atan2, exp, log, and the fused multiply-add) against System.Math, in ns per call.
// net10.0: CoreMath.FusedMultiplyAdd is Math.FusedMultiplyAdd (hardware); net48 (the netstandard2.0 build): the software version.
using System;
using System.Diagnostics;
using g3.CoreMathPort;

static class Program
{
    const int N = 1 << 16;

    static void Main()
    {
        var rnd = new Random(1);
        var a = new double[N]; var b = new double[N]; var pos = new double[N]; var wide = new double[N];
        for (int i = 0; i < N; ++i) {
            a[i] = (rnd.NextDouble() - 0.5) * 12.0;                  // angles of a few turns
            b[i] = (rnd.NextDouble() - 0.5) * 40.0;                  // exp arguments
            pos[i] = rnd.NextDouble() * 100 + 1e-3;                  // log arguments
            wide[i] = (rnd.NextDouble() - 0.5) * 2e3;
        }
        Console.WriteLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            + ", arch " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        Console.WriteLine();
        Console.WriteLine("| function | System.Math (ns/call) | CoreMath (ns/call) | ratio |");
        Console.WriteLine("|---|---|---|---|");
        Row("sin", a, x => Math.Sin(x), x => CoreMath.Sin(x));
        Row("cos", a, x => Math.Cos(x), x => CoreMath.Cos(x));
        Row("sin (|x| < 1000)", wide, x => Math.Sin(x), x => CoreMath.Sin(x));
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
}
