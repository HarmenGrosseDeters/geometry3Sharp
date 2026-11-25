using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

// Benchmark of BooleanVolumeGradient (README.md): the default ExactMeshBoolean path (before / after the change: the same
// inputs, the median of repeated runs), the boolean with RetainClassification, and the gradient's own time.
//   dotnet run -c Release -- default [reps]      (also builds with -p:G3Before=true on the tree before the change)
//   dotnet run -c Release -- gradient [reps]
static class Program
{
    static DMesh3 Sphere(int n, Vector3d scale, Vector3d shift)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { EdgeVertices = n, Radius = 1 }.Generate().MakeDMesh();
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            m.SetVertex(v, new Vector3d(p.x * scale.x, p.y * scale.y, p.z * scale.z) + shift);
        }
        return m;
    }

    static IEnumerable<(string name, DMesh3 a, DMesh3 b)> Cases()
    {
        foreach (int n in new[] { 16, 32 }) {
            DMesh3 a = Sphere(n, Vector3d.One, Vector3d.Zero);
            DMesh3 b = Sphere(n, new Vector3d(0.9, 0.8, 0.85), new Vector3d(0.53, 0.21, 0.17));
            yield return ($"spheres n={n} ({a.TriangleCount}+{b.TriangleCount} tris)", a, b);
        }
        // boxes with touching and flush faces (contact regions)
        DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
        DMesh3 flush = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1, 0.5, 0.75), new Vector3d(0.5, 0.25, 0.25)) }.Generate().MakeDMesh();
        yield return ("boxes, flush faces", box, flush);
    }

    static double Median(List<double> x) { x.Sort(); return x[x.Count / 2]; }

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "default";
        int reps = args.Length > 1 ? int.Parse(args[1]) : 11;
        foreach (var (name, a, b) in Cases())
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                var plain = new List<double>();
                var kept = new List<double>();
                var grad = new List<double>();
                var bytes = new List<long>();
                int pieces = 0, contacts = 0;
                for (int r = 0; r < reps + 1; ++r) {
                    long m0 = GC.GetTotalAllocatedBytes(true);
                    var sw = Stopwatch.StartNew();
                    var bo = new ExactMeshBoolean(a, b, op);
                    if (!bo.Compute()) { Console.WriteLine("failed: " + bo.Error); return 1; }
                    double t0 = sw.Elapsed.TotalMilliseconds;
                    long m1 = GC.GetTotalAllocatedBytes(true);
                    if (r > 0) { plain.Add(t0); bytes.Add(m1 - m0); }       // (the first run warms up the JIT)
#if !G3_BEFORE
                    if (mode == "gradient") {
                        sw.Restart();
                        var bk = new ExactMeshBoolean(a, b, op) { RetainClassification = true };
                        if (!bk.Compute()) { Console.WriteLine("failed: " + bk.Error); return 1; }
                        double t1 = sw.Elapsed.TotalMilliseconds;
                        sw.Restart();
                        var g = new BooleanVolumeGradient(bk);
                        if (!g.Compute()) { Console.WriteLine("failed: " + g.Error); return 1; }
                        double t2 = sw.Elapsed.TotalMilliseconds;
                        if (r > 0) { kept.Add(t1); grad.Add(t2); }
                        pieces = bk.Counts.TryGetValue("pieces", out long p) ? (int)p : 0;
                        contacts = g.Contacts.Count;
                    }
#endif
                }
                if (mode == "gradient")
                    Console.WriteLine($"{name,-34} {op,-12} pieces {pieces,6}  boolean {Median(plain),8:F1} ms  retained {Median(kept),8:F1} ms ({100 * (Median(kept) / Median(plain) - 1),5:F1} %)  gradient {Median(grad),8:F1} ms ({100 * Median(grad) / Median(plain),5:F1} % of the boolean)  contacts {contacts}");
                else
                    Console.WriteLine($"{name,-34} {op,-12} boolean (default) median {Median(plain),8:F2} ms of {reps}, allocated min {bytes.Min()} max {bytes.Max()} bytes");
            }
        return 0;
    }
}
