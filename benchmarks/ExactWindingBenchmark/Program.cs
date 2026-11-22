// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using g3;
using gs;

// Before/after benchmark comparing legacy Jacobson hierarchical WindingCache (ForceLegacyWindingCache = true),
// the exact ray-plus-boundary ExactWindingCache (Antipodal / FEWN), and FastWindingNumber on procedural meshes
// and MeshWindingNumberGrid (FullGrid and NarrowBand).
// Usage: dotnet run --project benchmarks/ExactWindingBenchmark -c Release [-- <rounds>]
static class Program
{
    static readonly FieldInfo LegacyField = typeof(DMeshAABBTree3).GetField(
        "ForceLegacyWindingCache", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo GetCacheMethod = typeof(DMeshAABBTree3).GetMethod(
        "GetExactWindingCache", BindingFlags.NonPublic | BindingFlags.Instance);

    static void SetLegacy(DMeshAABBTree3 tree, bool legacy) => LegacyField.SetValue(tree, legacy);

    static (int boundaryCount, bool useDirectSum) GetCacheInfo(DMeshAABBTree3 tree)
    {
        object cache = GetCacheMethod.Invoke(tree, null);
        Type t = cache.GetType();
        int bc = (int)t.GetProperty("BoundaryCount", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cache);
        bool ds = (bool)t.GetProperty("UseDirectSum", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cache);
        return (bc, ds);
    }

    static DMesh3 Sphere(int edgeVertices)
    {
        return new Sphere3Generator_NormalizedCube() { Radius = 1.0, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
    }

    static DMesh3 OpenSphere(int edgeVertices)
    {
        DMesh3 m = Sphere(edgeVertices);
        foreach (int tid in m.TriangleIndices().ToArray())
            if (m.GetTriCentroid(tid).z > 0.4)
                m.RemoveTriangle(tid, true, false);
        return m;
    }

    static DMesh3 FlippedCapSphere(int edgeVertices)
    {
        DMesh3 m = Sphere(edgeVertices);
        foreach (int tid in m.TriangleIndices().ToArray())
            if (m.GetTriCentroid(tid).z > 0.3)
                m.ReverseTriOrientation(tid);
        return m;
    }

    static DMesh3 UnweldedSoup(DMesh3 src)
    {
        DMesh3 soup = new DMesh3();
        Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
        foreach (int tid in src.TriangleIndices()) {
            src.GetTriVertices(tid, ref a, ref b, ref c);
            int va = soup.AppendVertex(a);
            int vb = soup.AppendVertex(b);
            int vc = soup.AppendVertex(c);
            soup.AppendTriangle(va, vb, vc);
        }
        return soup;
    }

    static DMesh3 DisconnectedSoup(int triCount, int seed)
    {
        var rnd = new Random(seed);
        DMesh3 soup = new DMesh3();
        for (int i = 0; i < triCount; ++i) {
            Vector3d o = new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            Vector3d d1 = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 0.15;
            Vector3d d2 = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 0.15;
            int va = soup.AppendVertex(o);
            int vb = soup.AppendVertex(o + d1);
            int vc = soup.AppendVertex(o + d2);
            soup.AppendTriangle(va, vb, vc);
        }
        return soup;
    }

    static Vector3d[] RandomQueries(int n, int seed)
    {
        var rnd = new Random(seed);
        var pts = new Vector3d[n];
        for (int i = 0; i < n; ++i)
            pts[i] = new Vector3d(rnd.NextDouble() * 3 - 1.5, rnd.NextDouble() * 3 - 1.5, rnd.NextDouble() * 3 - 1.5);
        return pts;
    }

    static double Median(double[] arr)
    {
        Array.Sort(arr);
        return arr[arr.Length / 2];
    }

    static void BenchPointQueries(string name, DMesh3 mesh, int queryCount, int rounds)
    {
        Vector3d[] pts = RandomQueries(queryCount, 42);
        var legacyBuild = new double[rounds];
        var legacyQuery = new double[rounds];
        var exactBuild = new double[rounds];
        var exactQuery = new double[rounds];
        var fastBuild = new double[rounds];
        var fastQuery = new double[rounds];

        double[] legacyVals = new double[queryCount];
        double[] exactVals = new double[queryCount];
        double[] fastVals = new double[queryCount];
        int boundaryCount = 0;
        bool useDirect = false;

        for (int r = -1; r < rounds; ++r) {
            // Legacy WindingCache
            var tLegacy = new DMeshAABBTree3(mesh, true);
            SetLegacy(tLegacy, true);
            var sw = Stopwatch.StartNew();
            legacyVals[0] = tLegacy.WindingNumber(pts[0]);
            sw.Stop();
            double bMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            for (int i = 0; i < queryCount; ++i)
                legacyVals[i] = tLegacy.WindingNumber(pts[i]);
            sw.Stop();
            double qMs = sw.Elapsed.TotalMilliseconds;
            if (r >= 0) { legacyBuild[r] = bMs; legacyQuery[r] = qMs; }

            // Exact ray+boundary cache
            var tExact = new DMeshAABBTree3(mesh, true);
            sw.Restart();
            exactVals[0] = tExact.WindingNumber(pts[0]);
            sw.Stop();
            bMs = sw.Elapsed.TotalMilliseconds;
            if (r == 0) (boundaryCount, useDirect) = GetCacheInfo(tExact);
            sw.Restart();
            for (int i = 0; i < queryCount; ++i)
                exactVals[i] = tExact.WindingNumber(pts[i]);
            sw.Stop();
            qMs = sw.Elapsed.TotalMilliseconds;
            if (r >= 0) { exactBuild[r] = bMs; exactQuery[r] = qMs; }

            // FastWindingNumber
            var tFast = new DMeshAABBTree3(mesh, true);
            sw.Restart();
            fastVals[0] = tFast.FastWindingNumber(pts[0]);
            sw.Stop();
            bMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            for (int i = 0; i < queryCount; ++i)
                fastVals[i] = tFast.FastWindingNumber(pts[i]);
            sw.Stop();
            qMs = sw.Elapsed.TotalMilliseconds;
            if (r >= 0) { fastBuild[r] = bMs; fastQuery[r] = qMs; }
        }

        double maxErrExact = 0.0, maxErrFast = 0.0;
        for (int i = 0; i < queryCount; ++i) {
            maxErrExact = Math.Max(maxErrExact, Math.Abs(exactVals[i] - legacyVals[i]));
            maxErrFast = Math.Max(maxErrFast, Math.Abs(fastVals[i] - legacyVals[i]));
        }

        double lb = Median(legacyBuild), lq = Median(legacyQuery);
        double eb = Median(exactBuild), eq = Median(exactQuery);
        double fb = Median(fastBuild), fq = Median(fastQuery);
        double qSpeedup = lq / Math.Max(eq, 1e-6);
        double bSpeedup = lb / Math.Max(eb, 1e-6);

        Console.WriteLine($"| {name} | {mesh.TriangleCount} | {boundaryCount}{(useDirect ? " (direct)" : "")} | {queryCount} | {lb:F1} | {eb:F1} ({bSpeedup:F1}x) | {lq:F1} | {eq:F2} ({qSpeedup:F1}x) | {fb:F1} / {fq:F2} | {maxErrExact:E2} | {maxErrFast:E2} |");
    }

    static void BenchGrid(string name, DMesh3 mesh, MeshWindingNumberGrid.ComputeModes mode, int resolution, int rounds)
    {
        double cellSize = mesh.CachedBounds.MaxDim / resolution;
        var legacyTimes = new double[rounds];
        var exactTimes = new double[rounds];
        var fastTimes = new double[rounds];
        float[] legacyRef = null;
        float[] exactOut = null;

        for (int r = -1; r < rounds; ++r) {
            var tLegacy = new DMeshAABBTree3(mesh, true);
            SetLegacy(tLegacy, true);
            var gLegacy = new MeshWindingNumberGrid(mesh, tLegacy, cellSize) {
                ComputeMode = mode,
                UseFastWindingNumber = false
            };
            var sw = Stopwatch.StartNew();
            gLegacy.Compute();
            sw.Stop();
            if (r >= 0) legacyTimes[r] = sw.Elapsed.TotalMilliseconds;
            if (r == 0) legacyRef = (float[])gLegacy.Grid.Buffer.Clone();

            var tExact = new DMeshAABBTree3(mesh, true);
            var gExact = new MeshWindingNumberGrid(mesh, tExact, cellSize) {
                ComputeMode = mode,
                UseFastWindingNumber = false
            };
            sw.Restart();
            gExact.Compute();
            sw.Stop();
            if (r >= 0) exactTimes[r] = sw.Elapsed.TotalMilliseconds;
            if (r == 0) exactOut = (float[])gExact.Grid.Buffer.Clone();

            var tFast = new DMeshAABBTree3(mesh, true);
            var gFast = new MeshWindingNumberGrid(mesh, tFast, cellSize) {
                ComputeMode = mode,
                UseFastWindingNumber = true
            };
            sw.Restart();
            gFast.Compute();
            sw.Stop();
            if (r >= 0) fastTimes[r] = sw.Elapsed.TotalMilliseconds;
        }

        double maxDiff = 0.0;
        for (int i = 0; i < legacyRef.Length; ++i)
            maxDiff = Math.Max(maxDiff, Math.Abs(exactOut[i] - legacyRef[i]));

        double lt = Median(legacyTimes), et = Median(exactTimes), ft = Median(fastTimes);
        Console.WriteLine($"| {name} | {mode} | {resolution}^3 | {lt:F1} | {et:F1} ({lt / Math.Max(et, 1e-6):F1}x) | {ft:F1} | {maxDiff:E2} |");
    }

    static void Warmup()
    {
        DMesh3 warmClosed = Sphere(12);
        DMesh3 warmOpen = OpenSphere(12);
        Vector3d[] pts = RandomQueries(400, 1);
        for (int iter = 0; iter < 10; ++iter) {
            foreach (DMesh3 m in new[] { warmClosed, warmOpen }) {
                var tl = new DMeshAABBTree3(m, true); SetLegacy(tl, true);
                var te = new DMeshAABBTree3(m, true);
                var tf = new DMeshAABBTree3(m, true);
                for (int i = 0; i < pts.Length; ++i) {
                    tl.WindingNumber(pts[i]);
                    te.WindingNumber(pts[i]);
                    tf.FastWindingNumber(pts[i]);
                }
            }
        }
        double cs = warmClosed.CachedBounds.MaxDim / 16.0;
        for (int iter = 0; iter < 4; ++iter) {
            new MeshWindingNumberGrid(warmClosed, new DMeshAABBTree3(warmClosed, true), cs) { ComputeMode = MeshWindingNumberGrid.ComputeModes.FullGrid }.Compute();
            new MeshWindingNumberGrid(warmOpen, new DMeshAABBTree3(warmOpen, true), cs) { ComputeMode = MeshWindingNumberGrid.ComputeModes.NarrowBand }.Compute();
        }
    }

    static int Main(string[] args)
    {
        int rounds = 5;
        if (args.Length > 0) {
            if (!int.TryParse(args[0], out rounds) || rounds <= 0) {
                Console.Error.WriteLine("Usage: ExactWindingBenchmark [<positive_rounds>]");
                return 1;
            }
        }
        Warmup();
        Console.WriteLine($"ExactWindingBenchmark (rounds={rounds}, processors={Environment.ProcessorCount})");
        Console.WriteLine();
        Console.WriteLine("### Point queries (`DMeshAABBTree3.WindingNumber`)");
        Console.WriteLine("| case | tris | |∂M| | queries | legacy build ms | exact build ms | legacy query ms | exact query ms | FWN build/query ms | max |exact-legacy| | max |FWN-legacy| |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");

        DMesh3 s20k = Sphere(42);          // ~20k tris
        DMesh3 s80k = Sphere(83);          // ~80k tris
        DMesh3 open20k = OpenSphere(42);
        DMesh3 flipped20k = FlippedCapSphere(42);
        DMesh3 unwelded20k = UnweldedSoup(s20k);
        DMesh3 openSoup5k = DisconnectedSoup(5000, 7);

        BenchPointQueries("closed sphere 20k", s20k, 1000, rounds);
        BenchPointQueries("closed sphere 80k", s80k, 1000, rounds);
        BenchPointQueries("open sphere 20k", open20k, 1000, rounds);
        BenchPointQueries("flipped cap 20k", flipped20k, 1000, rounds);
        BenchPointQueries("unwelded closed 20k", unwelded20k, 1000, rounds);
        BenchPointQueries("disconnected soup 5k", openSoup5k, 200, rounds);

        Console.WriteLine();
        Console.WriteLine("### Grid evaluation (`MeshWindingNumberGrid`, including cache build)");
        Console.WriteLine("| case | mode | grid | legacy ms | exact row-ray ms | FWN ms | max |exact-legacy| |");
        Console.WriteLine("|---|---|---|---|---|---|---|");

        BenchGrid("closed sphere 20k", s20k, MeshWindingNumberGrid.ComputeModes.FullGrid, 64, rounds);
        BenchGrid("open sphere 20k", open20k, MeshWindingNumberGrid.ComputeModes.FullGrid, 64, rounds);
        BenchGrid("closed sphere 20k", s20k, MeshWindingNumberGrid.ComputeModes.NarrowBand, 64, rounds);
        BenchGrid("open sphere 20k", open20k, MeshWindingNumberGrid.ComputeModes.NarrowBand, 64, rounds);
        return 0;
    }
}
