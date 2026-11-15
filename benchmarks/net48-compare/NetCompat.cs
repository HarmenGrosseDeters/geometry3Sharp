// The few runtime APIs the benchmarks use that .NET Framework 4.8 lacks, behind one name, so the same source and the same output rows
// build for net10.0 and net48 (benchmarks/net48-compare/README.md). On net10.0 every member calls the API it replaces: no behavior change.
// Members without an equivalent on net48 say so (the values are then not comparable across the runtimes; the hashes and results are).
using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

static class NetCompat
{
    // first 16 hex digits (upper case) of SHA-256 of the UTF-8 text: Convert.ToHexString(SHA256.HashData(..)).Substring(0, 16)
    public static string Sha256Hex16(string text)
    {
#if NETFRAMEWORK
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16);
#else
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 16);
#endif
    }

    // GC.GetTotalAllocatedBytes(true); net48: the AppDomain's cumulative allocation (monitoring is switched on at the first call)
    public static long AllocatedBytes()
    {
#if NETFRAMEWORK
        AppDomain.MonitoringIsEnabled = true;
        return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#else
        return GC.GetTotalAllocatedBytes(true);
#endif
    }

    // GC.GetTotalPauseDuration(); net48 has none: zero
    public static TimeSpan GcPause()
    {
#if NETFRAMEWORK
        return TimeSpan.Zero;
#else
        return GC.GetTotalPauseDuration();
#endif
    }

    // GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes; net48: the managed heap size now
    public static long HeapBytes()
    {
#if NETFRAMEWORK
        return GC.GetTotalMemory(false);
#else
        return GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes;
#endif
    }

    // File.Move(from, to, true)
    public static void MoveOverwrite(string from, string to)
    {
#if NETFRAMEWORK
        if (File.Exists(to))
            File.Delete(to);
        File.Move(from, to);
#else
        File.Move(from, to, true);
#endif
    }

    // Math.Cbrt; net48: Math.Pow(x, 1/3) with one Newton step (may differ from Math.Cbrt in the last bit: a case that depends on it,
    // eg `profile spheres:k`, has other inputs on net48 and is not a hash comparison)
    public static double Cbrt(double x)
    {
#if NETFRAMEWORK
        if (x == 0 || double.IsNaN(x) || double.IsInfinity(x))
            return x;
        double r = Math.Pow(Math.Abs(x), 1.0 / 3.0);
        r -= (r * r * r - Math.Abs(x)) / (3 * r * r);
        return x < 0 ? -r : r;
#else
        return Math.Cbrt(x);
#endif
    }
}

#if NETFRAMEWORK
// instance-style calls of .NET 5+ members that the sources use
static class NetCompatExtensions
{
    public static long GetBitLength(this BigInteger v)
    {
        // as on .NET 5+: the minimal number of bits without the sign bit (two's complement), 0 for 0 and -1
        if (v.Sign < 0)
            return v == BigInteger.MinusOne ? 0 : (-(v + 1)).GetBitLength();
        if (v.IsZero)
            return 0;
        byte[] bytes = v.ToByteArray();
        return bytes.Length * 8L - LeadingZeros(bytes[bytes.Length - 1]);
    }

    static int LeadingZeros(byte b)
    {
        int n = 0;
        for (int mask = 0x80; mask != 0 && (b & mask) == 0; mask >>= 1)
            n++;
        return n;
    }

    public static string[] Split(this string s, string separator) => s.Split(new[] { separator }, StringSplitOptions.None);
}
#endif
