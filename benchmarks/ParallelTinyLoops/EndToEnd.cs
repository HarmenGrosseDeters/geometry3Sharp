using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using gs;

// End-to-end timings of library operations (procedural inputs): large meshes (no slowdown allowed) and the tiny-loop
// cases of #1502 (box-pair Booleans, small remeshes) alone and with the busy pool of the issue
static class EndToEnd
{
    static DMesh3 Sphere(int n) { return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh(); }
    static DMesh3 Box(Vector3d c, Vector3d e) { return new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh(); }
    static DMesh3 Bumpy(int n)
    {
        DMesh3 m = Sphere(n);
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            m.SetVertex(v, p * (1 + 0.05 * Math.Sin(7 * p.x) * Math.Cos(5 * p.y) * Math.Sin(6 * p.z)));
        }
        return m;
    }

    static double Time(Action a) { var sw = Stopwatch.StartNew(); a(); return sw.Elapsed.TotalMilliseconds; }

    static double Median(List<double> t) { var s = t.OrderBy(x => x).ToList(); return s[s.Count / 2]; }

    static string Row(string name, string detail, List<double> t)
    {
        return "| " + name + " | " + detail + " | " + string.Join(" / ", t.Select(x => x.ToString("F0", CultureInfo.InvariantCulture))) + " | " + Median(t).ToString("F0", CultureInfo.InvariantCulture) + " |";
    }

    class Case { public string Name, Detail; public int Reps; public Action Run; }

    static List<Case> Cases(bool tinyOnly, bool largeOnly)
    {
        var cases = new List<Case>();
        if (!tinyOnly) {
            // large meshes
            DMesh3 big = Bumpy(130);        // ~200k triangles
            cases.Add(new Case { Name = "Reducer", Detail = big.TriangleCount + " tris -> 20000", Reps = 5, Run = () => {
                var m = new DMesh3(big); var r = new Reducer(m); r.ReduceToTriangleCount(20000); } });
            cases.Add(new Case { Name = "Reducer (projection)", Detail = big.TriangleCount + " tris -> 20000, ProjectToFarMesh", Reps = 3, Run = () => {
                var m = new DMesh3(big); var r = new Reducer(m); r.ReduceToTriangleCount(20000); } });
            DMesh3 mid = Bumpy(60);         // ~43k triangles
            cases.Add(new Case { Name = "Remesher", Detail = mid.TriangleCount + " tris, 5 passes, target edge 0.05", Reps = 5, Run = () => {
                var m = new DMesh3(mid); var r = new Remesher(m); r.SetTargetEdgeLength(0.05); r.SmoothSpeedT = 0.5;
                for (int i = 0; i < 5; ++i) r.BasicRemeshPass(); } });
            cases.Add(new Case { Name = "RemesherPro", Detail = mid.TriangleCount + " tris, 5 passes, target edge 0.05", Reps = 5, Run = () => {
                var m = new DMesh3(mid); var r = new RemesherPro(m); r.SetTargetEdgeLength(0.05); r.SmoothSpeedT = 0.5;
                for (int i = 0; i < 5; ++i) r.BasicRemeshPass(); } });
            DMesh3 sa = Sphere(40), sb = Sphere(40);
            MeshTransforms.Translate(sb, new Vector3d(0.6, 0.3, 0.2));
            cases.Add(new Case { Name = "Boolean Union (large)", Detail = sa.TriangleCount + "+" + sb.TriangleCount + " tris", Reps = 5, Run = () => {
                ExactMeshBoolean.Union(new[] { sa, sb }); } });
            DMesh3 la = Sphere(120), lb = Sphere(120);
            MeshTransforms.Translate(lb, new Vector3d(0.6, 0.3, 0.2));
            cases.Add(new Case { Name = "Boolean Union (xl)", Detail = la.TriangleCount + "+" + lb.TriangleCount + " tris", Reps = 3, Run = () => {
                ExactMeshBoolean.Union(new[] { la, lb }); } });
        }
        if (!largeOnly) {
            // tiny cases of #1502
            var boxes = new List<DMesh3[]>();
            var rnd = new Random(7);
            for (int i = 0; i < 40; ++i) {
                var a = Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
                var b = Box(new Vector3d(rnd.Next(-2, 3) * 0.5, rnd.Next(-2, 3) * 0.5, rnd.Next(-2, 3) * 0.5), new Vector3d(0.5 + rnd.Next(0, 3) * 0.25, 0.75, 0.5));
                boxes.Add(new[] { a, b });
            }
            cases.Add(new Case { Name = "Boolean box pairs", Detail = "40 pairs x (union, intersection), 12 tris each", Reps = 5, Run = () => {
                foreach (var p in boxes) { ExactMeshBoolean.Union(p); ExactMeshBoolean.Intersection(p); } } });
            DMesh3 small = Sphere(6);
            cases.Add(new Case { Name = "Remesher small", Detail = small.TriangleCount + " tris, 12 remeshes x 3 passes", Reps = 5, Run = () => {
                for (int k = 0; k < 12; ++k) {
                    var m = new DMesh3(small); var r = new Remesher(m); r.SetTargetEdgeLength(0.2);
                    for (int i = 0; i < 3; ++i) r.BasicRemeshPass();
                } } });
            cases.Add(new Case { Name = "Reducer small", Detail = "Sphere(12), 20 x -> 100 tris", Reps = 5, Run = () => {
                for (int k = 0; k < 20; ++k) { var m = Sphere(12); var r = new Reducer(m); r.ReduceToTriangleCount(100); } } });
        }
        return cases;
    }

    public static void Run(string[] args)
    {
        bool busy = args.Contains("busy");
        bool serial = args.Contains("serial");
        bool tiny = args.Contains("tiny"), large = args.Contains("large");
        string only = args.FirstOrDefault(a => a.StartsWith("only="))?.Substring(5);
        Console.WriteLine("# e2e " + (busy ? "BUSY pool" : "alone") + (serial ? ", SerialOnThisThread" : "") + ", " + Environment.ProcessorCount + " cores; ms per run (runs / median)");
        Console.WriteLine("| case | input | runs ms | median ms |");
        Console.WriteLine("|---|---|---|---|");
        var cases = Cases(tiny, large);
        IDisposable load = null;
        // the busy pool of the issue
        if (busy) {
            var stop = new CancellationTokenSource();
            var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(() => {
                while (!stop.IsCancellationRequested)
                    Parallel.For(0, 64, (j) => { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < 10) { } });
            })).ToArray();
            load = new Stopper(() => { stop.Cancel(); Task.WaitAll(tasks); });
            Thread.Sleep(500);
        }
        try {
            foreach (var c in cases) {
                if (only != null && !c.Name.Contains(only)) continue;
                gParallel.SerialOnThisThread = serial;
                c.Run();    // warm up
                var t = new List<double>();
                double cap = busy ? 120000 : 1e9;
                for (int r = 0; r < c.Reps; ++r) {
                    t.Add(Time(c.Run));
                    if (t.Last() > cap) break;
                }
                Console.WriteLine(Row(c.Name, c.Detail, t));
            }
        } finally { gParallel.SerialOnThisThread = false; load?.Dispose(); }
    }

    sealed class Stopper : IDisposable { Action a; public Stopper(Action a) { this.a = a; } public void Dispose() { a(); } }
}
