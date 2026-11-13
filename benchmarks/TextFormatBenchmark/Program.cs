using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using g3;

// Time per coordinate of MeshIOUtil.FormatDouble / FormatFloat (ShortestDecimal, Ryu) against the formats it replaced:
//   "R"          what net10 used (.NET Core 3.0 and later: shortest, platform code)
//   G15/16/17    what .NET Framework used (the first of 15, 16 and 17 digits that the platform parser reads back);
//                run here on net10 to show its cost, its text differs from the others for subnormals and some 16 digit values
// and of a whole OBJ write. Inputs are procedural with fixed seeds. CSV on stdout; "same" counts texts equal to "R".
//   dotnet run --project benchmarks/TextFormatBenchmark -c Release [-- <count> <rounds>]
static class P
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static ulong state = 1;
    static ulong Next()
    {
        ulong z = (state += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    static string Legacy(double v)
    {
        string s = v.ToString("G15", Inv);
        if (double.Parse(s, NumberStyles.Float, Inv) == v) return s;
        s = v.ToString("G16", Inv);
        if (double.Parse(s, NumberStyles.Float, Inv) == v) return s;
        return v.ToString("G17", Inv);
    }

    static double Median(Func<long> run, int rounds, out long check)
    {
        check = run();
        var t = new double[rounds];
        for (int r = 0; r < rounds; ++r) { var sw = Stopwatch.StartNew(); check = run(); t[r] = sw.Elapsed.TotalMilliseconds; }
        Array.Sort(t);
        return t[rounds / 2];
    }

    static void Row(string set, string method, int n, Func<long> run, int rounds)
    {
        long check;
        double ms = Median(run, rounds, out check);
        Console.WriteLine($"{set},{method},{ms * 1e6 / n:F1},{check}");
    }

    static void Main(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 1000000;
        int rounds = args.Length > 1 ? int.Parse(args[1]) : 5;
        var coords = new double[n]; var bits = new double[n]; var shortc = new double[n]; var floats = new float[n];
        for (int i = 0; i < n; ++i) {
            coords[i] = ((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0;                 // 16-17 digit coordinates
            double b; do { b = BitConverter.Int64BitsToDouble((long)Next()); } while (double.IsNaN(b) || double.IsInfinity(b));
            bits[i] = b;                                                                                 // any exponent
            shortc[i] = Math.Round(coords[i], 3);                                                        // short decimals (CAD-like)
            floats[i] = (float)coords[i];
        }
        Console.WriteLine("set,method,ns_per_value,checksum(total text length)");
        foreach (var kv in new[] { ("coords17", coords), ("anyexp", bits), ("short3", shortc) }) {
            double[] a = kv.Item2;
            Row(kv.Item1, "R(old net10)", n, () => { long c = 0; foreach (double x in a) c += x.ToString("R", Inv).Length; return c; }, rounds);
            Row(kv.Item1, "G15/16/17(old net48)", n, () => { long c = 0; foreach (double x in a) c += Legacy(x).Length; return c; }, rounds);
            Row(kv.Item1, "ShortestDecimal(new)", n, () => { long c = 0; foreach (double x in a) c += MeshIOUtil.FormatDouble(x).Length; return c; }, rounds);
            long same = 0; foreach (double x in a) if (x.ToString("R", Inv) == MeshIOUtil.FormatDouble(x)) ++same;
            Console.WriteLine($"{kv.Item1},same_as_R,{same}/{n},");
        }
        Row("float", "R(old net10)", n, () => { long c = 0; foreach (float x in floats) c += x.ToString("R", Inv).Length; return c; }, rounds);
        Row("float", "ShortestDecimal(new)", n, () => { long c = 0; foreach (float x in floats) c += MeshIOUtil.FormatFloat(x).Length; return c; }, rounds);

        // a whole OBJ write of n/3 vertices (vertex lines only: n/3 vertices, n/3 triangles on a strip)
        int nv = n / 3;
        var mesh = new DMesh3();
        for (int i = 0; i < nv; ++i) mesh.AppendVertex(new Vector3d(coords[3 * i], coords[3 * i + 1], coords[3 * i + 2]));
        for (int i = 0; i + 2 < nv; ++i) mesh.AppendTriangle(i, i + 1, i + 2);
        Row("obj", $"WriteMesh {nv} vertices", nv, () => {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            new OBJWriter().Write(sw, new System.Collections.Generic.List<WriteMesh> { new WriteMesh(mesh) }, new WriteOptions());
            return sw.GetStringBuilder().Length;
        }, rounds);
    }
}
