using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using gs;

static class P
{
    static void Main(string[] args)
    {
        int n = 0, above = 0, equal = 0, tanAbove = 0; long worstBelow = 0, worstTanAbove = 0, worstTanBelow = 0; double worstMs = 0, worstAt = 0, worstMsAt = 0;
        var hist = new System.Collections.Generic.SortedDictionary<long, int>();
        foreach (string line in File.ReadLines(args[0])) {
            string[] p = line.Split(' ');
            double a = BitConverter.Int64BitsToDouble(long.Parse(p[0]));
            long rb = long.Parse(p[1]);
            var sw = Stopwatch.StartNew();
            double s = SlopeSpec.TanLowerBound(a);
            double ms = sw.Elapsed.TotalMilliseconds;
            if (ms > worstMs) { worstMs = ms; worstMsAt = a; }
            long sb = BitConverter.DoubleToInt64Bits(s);
            long d = sb - rb;      // ulps above the reference (the largest double <= tan)
            n++;
            int c; hist.TryGetValue(d, out c); hist[d] = c + 1;
            if (d > 0) { above++; Console.WriteLine($"ABOVE: a {a:R} S {s:R} ref {BitConverter.Int64BitsToDouble(rb):R} ({d} ulps)"); }
            if (d == 0) equal++;
            if (-d > worstBelow) { worstBelow = -d; worstAt = a; }
            double t = Math.Tan(a * Math.PI / 180);
            long td = BitConverter.DoubleToInt64Bits(t) - rb;
            if (t > 0 && !double.IsInfinity(t)) { if (td > 0) tanAbove++; worstTanAbove = Math.Max(worstTanAbove, td); worstTanBelow = Math.Max(worstTanBelow, -td); }
            // the spec
            var spec = SlopeSpec.Degrees(a);
            if (spec.IsValid && BitConverter.DoubleToInt64Bits(spec.S) != sb) Console.WriteLine($"S differs from TanLowerBound at {a:R}");
            if (spec.IsValid == false && s >= 2.2250738585072014E-308) Console.WriteLine($"invalid at {a:R} with S {s:R}");
        }
        Console.WriteLine($"{n} angles: S above the reference {above}, equal {equal}, worst below {worstBelow} ulps (at {worstAt:R}); Math.Tan above {tanAbove}, worst {worstTanAbove} ulps above, {worstTanBelow} below; slowest call {worstMs:0.0} ms (at {worstMsAt:R})");
        Console.WriteLine("ulps relative to the reference: " + string.Join(", ", hist.Select(kv => $"{kv.Key}: {kv.Value}")));
        foreach (double a in new[] { 45.0, 30.0, 60.0, 5e-324, 1e-310, 2.8e-322, BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(90.0) - 1) }) {
            var sw = Stopwatch.StartNew();
            double s = SlopeSpec.TanLowerBound(a);
            Console.WriteLine($"a {a:R}: S {s:R} (Math.Tan {Math.Tan(a * Math.PI / 180):R}), valid {SlopeSpec.Degrees(a).IsValid}, {sw.Elapsed.TotalMilliseconds:0.0} ms");
        }
    }
}
