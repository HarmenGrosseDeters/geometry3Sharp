using System;
using System.Diagnostics;
using System.Linq;
using G3Native;

// usage: driver <op 0|1|2|imprint> <edgeVertices> <runs>   prints: first-run ms, median ms of the runs, triangles, hash, not-clean reason
unsafe static class Program
{
    static int Main(string[] args)
    {
        string opArg = args.Length > 0 ? args[0] : "0";
        int n = args.Length > 1 ? int.Parse(args[1]) : 32, runs = args.Length > 2 ? int.Parse(args[2]) : 5;
        var a = Core.MakeSphere(n, 1.0, 0, 0, 0);
        var b = Core.MakeSphere(n, 0.9, 0.7, 0.3, 0.2);
        var times = new double[runs];
        ResultBox r = null;
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            int rc = opArg == "imprint" ? Core.Imprint(a, b, null, out r) : Core.Boolean(a, b, int.Parse(opArg), null, out r);
            sw.Stop();
            times[i] = sw.Elapsed.TotalMilliseconds;
            if (rc != 0) { Console.WriteLine("failed rc=" + rc + " " + Core.LastError); return 1; }
        }
        Console.WriteLine("managed op={0} n={1} first_ms={2:F1} median_ms={3:F1} tris={4} hash={5:x16} reason='{6}'",
            opArg, n, times[0], times.OrderBy(x => x).ElementAt(runs / 2), r.Mesh.TriangleCount, Core.Hash(r.Mesh), Core.Reason(r));
        return 0;
    }
}
