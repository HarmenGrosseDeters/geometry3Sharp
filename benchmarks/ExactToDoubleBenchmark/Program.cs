using System;
using System.Diagnostics;
using System.Numerics;
using g3;

// ExactPoint3.ToDouble (num / den * 2^scaleExp as a correctly rounded double, issue #1410), isolated, and the exact points' ToVector3d.
// Run: dotnet run -c Release (best with DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0); ns per call, the minimum of 9 repetitions.
class Program
{
    static double MinNs(Action f, int n, int reps = 9)
    {
        f();
        double best = double.MaxValue;
        for (int r = 0; r < reps; r++) { var sw = Stopwatch.StartNew(); f(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / n); }
        return best;
    }

    static BigInteger Rand(Random r, int bits)
    {
        var b = new byte[bits / 8 + 2];
        r.NextBytes(b);
        b[b.Length - 1] &= 0x7F;
        return (new BigInteger(b) >> ((b.Length * 8 - 8) - bits)) | (BigInteger.One << (bits - 1));
    }

    static void Run(string name, int numBits, int denBits, int scaleLo, int scaleHi, bool negative)
    {
        var r = new Random(5);
        int N = 100000;
        var num = new BigInteger[N]; var den = new BigInteger[N]; var sc = new int[N];
        for (int i = 0; i < N; i++) {
            num[i] = Rand(r, numBits); if (negative && (i & 1) == 1) num[i] = -num[i];
            den[i] = Rand(r, denBits); sc[i] = r.Next(scaleLo, scaleHi + 1);
        }
        double sum = 0;
        double ns = MinNs(() => { double s = 0; for (int i = 0; i < N; i++) s += ExactPoint3.ToDouble(num[i], den[i], sc[i]); sum = s; }, N);
        Console.WriteLine($"ToDouble {name}: {ns:F1} ns (checksum {sum:R})");
    }

    static void Main()
    {
        Run("num 100 bit / den 100 bit", 100, 100, -60, 0, true);
        Run("num 200 bit / den 150 bit", 200, 150, -60, 0, true);
        Run("num 60 bit / den 1 bit (W = 1)", 60, 1, -60, 0, true);
        Run("num 300 bit / den 290 bit", 300, 290, -60, 0, false);
        Run("subnormal results", 100, 100, -1100, -1000, false);
        var r = new Random(6);
        int N = 50000; var pts = new ExactPoint3[N];
        for (int i = 0; i < N; i++) pts[i] = ExactPoint3.CreateUnreduced(Rand(r, 120), Rand(r, 120), Rand(r, 120), Rand(r, 100));
        double chk = 0;
        Console.WriteLine($"ExactPoint3.ToVector3d(-30): {MinNs(() => { double s = 0; for (int i = 0; i < N; i++) { var v = pts[i].ToVector3d(-30); s += v.x + v.y + v.z; } chk = s; }, N):F1} ns (checksum {chk:R})");
    }
}
