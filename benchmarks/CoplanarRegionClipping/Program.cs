using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

// #1630, stage 3 of docs/design/COPLANAR_REGION_CLIPPING.md: the doubling series of two coplanar fans (the union of a capped cylinder and a copy shifted along x),
// with the old rule and with ExactMeshBoolean.RegionClipping, and a case with no coplanar cluster.
//   dotnet run -c Release --project benchmarks/CoplanarRegionClipping -- [--old-all] [--rings 61,121,241,481,961] [--reps 3]
// The old rule at ring 481 takes about 27 s and at 961 much more: it runs up to ring 241 unless --old-all.
static class Program
{
    // a capped cylinder (radius r, height h) whose ring points are on the rational parametrisation of the circle: 4 K - 2 ring points, the caps are fans from the cap centres
    static DMesh3 Cylinder(int K, double r = 0.5, double h = 1.0)
    {
        var points = new List<Vector2d>();
        for (int k = -K; k <= K; ++k) { double t = (double)k / K; points.Add(new Vector2d(r * (1 - t * t) / (1 + t * t), r * 2 * t / (1 + t * t))); }
        for (int k = -K + 1; k < K; ++k) { double t = (double)k / K; points.Add(new Vector2d(-r * (1 - t * t) / (1 + t * t), -r * 2 * t / (1 + t * t))); }
        points.Sort((a, b) => Math.Atan2(a.y, a.x).CompareTo(Math.Atan2(b.y, b.x)));
        var ring = new List<Vector2d>();
        foreach (Vector2d p in points)
            if (ring.Count == 0 || ring[ring.Count - 1] != p)
                ring.Add(p);
        if (ring[0] == ring[ring.Count - 1])
            ring.RemoveAt(ring.Count - 1);
        int n = ring.Count;
        var m = new DMesh3();
        for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(ring[i].x, 0, ring[i].y));
        for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(ring[i].x, h, ring[i].y));
        int c0 = m.AppendVertex(new Vector3d(0, 0, 0)), c1 = m.AppendVertex(new Vector3d(0, h, 0));
        for (int i = 0; i < n; ++i) {
            int j = (i + 1) % n;
            m.AppendTriangle(c0, i, j); m.AppendTriangle(c1, n + j, n + i);
            m.AppendTriangle(i, n + j, j); m.AppendTriangle(i, n + i, n + j);
        }
        return m;
    }

    // the mesh shifted along x: dx = 0 is the shift of #1630, 2 ulps of the x extent (every shifted coordinate exact)
    static DMesh3 Shifted(DMesh3 a, double dx)
    {
        if (dx == 0) {
            double maxAbs = a.VertexIndices().Max(v => Math.Abs(a.GetVertex(v).x));
            dx = 2 * (BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(maxAbs) + 1) - maxAbs);
        }
        var b = new DMesh3(a);
        foreach (int vid in b.VertexIndices().ToList()) { Vector3d v = b.GetVertex(vid); v.x += dx; b.SetVertex(vid, v); }
        return b;
    }

    struct Run { public double Ms; public int Pieces, Pairs, Intersecting, Triangles; public Dictionary<string, long> Phases; }

    static Run Measure(IList<DMesh3> meshes, bool regions, int reps)
    {
        var times = new List<double>();
        Run last = default;
        for (int rep = 0; rep < reps + 1; ++rep) {       // (the first run of a process is JIT: not counted)
            var b = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union) { RegionClipping = regions };
            var sw = Stopwatch.StartNew();
            if (b.Compute() == false) throw new Exception(b.Error);
            double ms = sw.Elapsed.TotalMilliseconds;
            if (rep > 0) times.Add(ms);
            last = new Run { Pieces = b.Arrangement.Output.Count, Pairs = b.Arrangement.CandidatePairs, Intersecting = b.Arrangement.IntersectingPairs, Triangles = b.Result.TriangleCount, Phases = new Dictionary<string, long>(b.PhaseMilliseconds) };
        }
        times.Sort();
        last.Ms = times[times.Count / 2];
        return last;
    }

    // the sizes of the Thingi10K contact cases (5k to 100k triangles): no cluster at all (spheres), flat contact of large coplanar faces (grid boxes), a fan pair
    static IEnumerable<(string, IList<DMesh3>)> SizeCases()
    {
        DMesh3 Sphere(int n, Vector3d c) { var m = new Sphere3Generator_NormalizedCube() { Radius = 0.5, EdgeVertices = n }.Generate().MakeDMesh(); MeshTransforms.Translate(m, c); return m; }
        DMesh3 GridBox(int n, Vector3d c) => new GridBox3Generator() { Box = new Box3d(c, new Vector3d(0.5, 0.5, 0.5)), EdgeVertices = n }.Generate().MakeDMesh();
        foreach (int n in new[] { 30, 60, 120 })
            yield return ($"two spheres crossing (no coplanar face), {n} per edge", new[] { Sphere(n, Vector3d.Zero), Sphere(n, new Vector3d(0.3, 0.1, 0.05)) });
        foreach (int n in new[] { 30, 60, 120 })
            yield return ($"two grid boxes, face contact, {n} per edge", new[] { GridBox(n, Vector3d.Zero), GridBox(n, new Vector3d(1.0, 0.125, 0.0625)) });
        foreach (int n in new[] { 30, 60, 120 })
            yield return ($"two grid boxes overlapping, faces flush, {n} per edge", new[] { GridBox(n, Vector3d.Zero), GridBox(n, new Vector3d(0.25, 0.125, 0.0)) });
        DMesh3 c = Cylinder(60);
        yield return ("two capped cylinders, shifted by 0.25 (fans)", new[] { c, Shifted(c, 0.25) });
    }

    static void Main(string[] args)
    {
        bool oldAll = args.Contains("--old-all");
        int reps = 3;
        int[] rings = { 61, 121, 241, 481, 961 };
        for (int i = 0; i + 1 < args.Length; ++i) {
            if (args[i] == "--reps") reps = int.Parse(args[i + 1]);
            if (args[i] == "--rings") rings = args[i + 1].Split(',').Select(int.Parse).ToArray();
        }
        Console.WriteLine($"cores {Environment.ProcessorCount}, median of {reps} runs after a warm-up run, union of a capped cylinder and a shifted copy");
        foreach (double dx in new[] { 0.0, 0.25 }) {
            Console.WriteLine(dx == 0 ? "\ncase S (shift of 2 ulps)" : "\ncase R (shift of 0.25)");
            Console.WriteLine("| ring | pieces old | pieces region | ms old | ms region | growth region | candidate pairs | intersecting pairs |");
            Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
            double prev = 0;
            foreach (int ring in rings) {
                DMesh3 a = Cylinder((ring + 2) / 4), b = Shifted(a, dx);
                var meshes = new[] { a, b };
                Run neu = Measure(meshes, true, reps);
                Run old = default; bool haveOld = oldAll || ring <= 241;
                if (haveOld) old = Measure(meshes, false, Math.Min(reps, ring >= 241 ? 1 : reps));
                Console.WriteLine($"| {ring} | {(haveOld ? old.Pieces.ToString() : "-")} | {neu.Pieces} | {(haveOld ? old.Ms.ToString("F0") : "-")} | {neu.Ms:F0} | {(prev > 0 ? "x" + (neu.Ms / prev).ToString("F1") : "-")} | {neu.Pairs} | {neu.Intersecting} |");
                prev = neu.Ms;
            }
        }
        Console.WriteLine("\nthe cost of the switch on inputs of the sizes of the corpus (E' stage 4: RegionClipping as the default): median of " + reps + " runs, ms; phases of the last run: planes (planeRep and the regions), intersect (the pair loop), merge, classification, assembly");
        Console.WriteLine("| case | triangles | ms off | ms on | on / off | planes off / on | intersect off / on | merge off / on | classification off / on | assembly off / on |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var (name, meshes) in SizeCases()) {
            Run off = Measure(meshes, false, reps), on = Measure(meshes, true, reps);
            string P(Run r, string k) => r.Phases != null && r.Phases.TryGetValue(k, out long v) ? v.ToString() : "-";
            string Pair(string k) => P(off, k) + " / " + P(on, k);
            Console.WriteLine($"| {name} | {meshes.Sum(m => m.TriangleCount)} | {off.Ms:F0} | {on.Ms:F0} | x{on.Ms / Math.Max(off.Ms, 0.01):F2} | {Pair("intersections.planes")} | {Pair("intersections.intersect")} | {Pair("merge")} | {Pair("classification")} | {Pair("assembly")} |");
        }
        // no coplanar cluster: two crossing capped cylinders (no face of one in a plane of the other's): the switch costs nothing
        Console.WriteLine("\nno coplanar cluster: two perpendicular capped cylinders, 128 slices");
        DMesh3 c1 = new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 2f, Slices = 128, NoSharedVertices = false }.Generate().MakeDMesh();
        DMesh3 c2 = new DMesh3(c1);
        foreach (int v in c2.VertexIndices().ToList()) { Vector3d p = c2.GetVertex(v); c2.SetVertex(v, new Vector3d(p.y - 1, p.x, p.z + 0.1)); }
        Run o = Measure(new[] { c1, c2 }, false, reps), n = Measure(new[] { c1, c2 }, true, reps);
        Console.WriteLine($"old {o.Ms:F0} ms, region {n.Ms:F0} ms, pieces {o.Pieces} / {n.Pieces}, result triangles {o.Triangles} / {n.Triangles}");
    }
}
