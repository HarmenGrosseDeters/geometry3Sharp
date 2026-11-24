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

    // Points at 10^-3 .. 10^-14 from the boundary edges of an open mesh (the edge angle is ill-conditioned there).
    static Vector3d[] NearBoundaryQueries(DMesh3 mesh, int n, int seed)
    {
        var rnd = new Random(seed);
        int[] edges = mesh.BoundaryEdgeIndices().ToArray();
        var pts = new Vector3d[n];
        for (int i = 0; i < n; ++i) {
            Index2i ev = mesh.GetEdgeV(edges[rnd.Next(edges.Length)]);
            Vector3d a = mesh.GetVertex(ev.a), b = mesh.GetVertex(ev.b);
            Vector3d dir = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
            pts[i] = a + rnd.NextDouble() * (b - a) + Math.Pow(10, -3 - 11 * rnd.NextDouble()) * dir;
        }
        return pts;
    }

    static void BenchNearEdge(string name, DMesh3 mesh, Vector3d[] pts, int rounds)
    {
        var times = new double[rounds];
        double checksum = 0;
        for (int r = -1; r < rounds; ++r) {
            var tree = new DMeshAABBTree3(mesh, true);
            tree.WindingNumber(pts[0]);          // (cache build)
            var sw = Stopwatch.StartNew();
            double sum = 0;
            for (int i = 0; i < pts.Length; ++i)
                sum += tree.WindingNumber(pts[i]);
            sw.Stop();
            checksum = sum;
            if (r >= 0) times[r] = sw.Elapsed.TotalMilliseconds;
        }
        double ms = Median(times);
        Console.WriteLine($"| {name} | {mesh.TriangleCount} | {pts.Length} | {ms:F2} | {1000.0 * ms / pts.Length:F2} | {checksum:R} |");
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
        bool nearOnly = args.Length > 1 && args[1] == "near-edge";
        if (nearOnly) {
            DMesh3 c20k = Sphere(42), o20k = OpenSphere(42);
            Console.WriteLine("### Edge angle near open edges (`DMeshAABBTree3.WindingNumber`, exact cache, per-query time)");
            Console.WriteLine("| case | tris | queries | total ms | us / query | sum of the values |");
            Console.WriteLine("|---|---|---|---|---|---|");
            BenchNearEdge("closed sphere 20k, random", c20k, RandomQueries(2000, 5), rounds);
            BenchNearEdge("open sphere 20k, random (away from the edges)", o20k, RandomQueries(2000, 5), rounds);
            BenchNearEdge("open sphere 20k, 1e-3 .. 1e-14 from the boundary edges", o20k, NearBoundaryQueries(o20k, 2000, 5), rounds);
            return 0;
        }
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
        Console.WriteLine("### Edge angle near open edges (`DMeshAABBTree3.WindingNumber`, exact cache, per-query time)");
        Console.WriteLine("| case | tris | queries | total ms | us / query | sum of the values |");
        Console.WriteLine("|---|---|---|---|---|---|");
        BenchNearEdge("closed sphere 20k, random", s20k, RandomQueries(2000, 5), rounds);
        BenchNearEdge("open sphere 20k, random (away from the edges)", open20k, RandomQueries(2000, 5), rounds);
        BenchNearEdge("open sphere 20k, 1e-3 .. 1e-14 from the boundary edges", open20k, NearBoundaryQueries(open20k, 2000, 5), rounds);
        Console.WriteLine();
        Console.WriteLine("### Grid evaluation (`MeshWindingNumberGrid`, including cache build)");
        Console.WriteLine("| case | mode | grid | legacy ms | exact row-ray ms | FWN ms | max |exact-legacy| |");
        Console.WriteLine("|---|---|---|---|---|---|---|");

        BenchGrid("closed sphere 20k", s20k, MeshWindingNumberGrid.ComputeModes.FullGrid, 64, rounds);
        BenchGrid("open sphere 20k", open20k, MeshWindingNumberGrid.ComputeModes.FullGrid, 64, rounds);
        BenchGrid("closed sphere 20k", s20k, MeshWindingNumberGrid.ComputeModes.NarrowBand, 64, rounds);
        BenchGrid("open sphere 20k", open20k, MeshWindingNumberGrid.ComputeModes.NarrowBand, 64, rounds);

        Console.WriteLine();
        Console.WriteLine("### Downstream adoption (`FastWindingNumber` vs opt-in exact winding)");
        Console.WriteLine("| operation | case | FWN ms | exact ms | speedup vs FWN |");
        Console.WriteLine("|---|---|---|---|---|");

        BenchSolidify("closed sphere 20k (dense 64)", s20k, false, 64, rounds);
        BenchSolidify("closed sphere 20k (adaptive 64)", s20k, true, 64, rounds);
        BenchSolidify("unwelded closed 20k (dense 64)", unwelded20k, false, 64, rounds);
        BenchBatchInside("closed sphere 20k (20k pts)", s20k, 20000, rounds);
        BenchBatchInside("unwelded closed 20k (20k pts)", unwelded20k, 20000, rounds);
        BenchDeviation("closed sphere 20k (2.4k query vtx)", s20k, rounds);

        Console.WriteLine();
        Console.WriteLine("### Hierarchical boundary threshold queries (`BoundaryTree`, `MeshBatchQueries.IsInsideBatch`)");
        Console.WriteLine("| case | tris | |∂M| | queries | linear exact ms | hierarchical exact ms | FWN ms | speedup vs linear | parity |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");

        DMesh3 cup2k = OpenCup(2000, 6);
        DMesh3 cup4k = OpenCup(4000, 4);
        BenchHierarchicalBoundary("open cup rim 2k", cup2k, 2000, rounds);
        BenchHierarchicalBoundary("open cup rim 4k", cup4k, 2000, rounds);
        return 0;
    }

    // Open hemispherical cup with `rimEdges` exterior boundary edges along the open rim.
    static DMesh3 OpenCup(int rimEdges, int rings)
    {
        DMesh3 m = new DMesh3();
        int pole = m.AppendVertex(new Vector3d(0, 0, -1.0));
        int[,] ringV = new int[rings, rimEdges];
        for (int r = 0; r < rings; ++r) {
            double phi = -0.5 * Math.PI + (0.65 * Math.PI) * (r + 1) / rings;
            double rad = Math.Cos(phi), z = Math.Sin(phi);
            for (int k = 0; k < rimEdges; ++k) {
                double th = 2.0 * Math.PI * k / rimEdges;
                double wobble = 1.0 + 0.05 * Math.Sin(5.0 * th);
                ringV[r, k] = m.AppendVertex(new Vector3d(rad * wobble * Math.Cos(th), rad * wobble * Math.Sin(th), z));
            }
        }
        for (int k = 0; k < rimEdges; ++k) {
            int kn = (k + 1) % rimEdges;
            m.AppendTriangle(pole, ringV[0, kn], ringV[0, k]);
        }
        for (int r = 0; r + 1 < rings; ++r) {
            for (int k = 0; k < rimEdges; ++k) {
                int kn = (k + 1) % rimEdges;
                m.AppendTriangle(ringV[r, k], ringV[r, kn], ringV[r + 1, kn]);
                m.AppendTriangle(ringV[r, k], ringV[r + 1, kn], ringV[r + 1, k]);
            }
        }
        return m;
    }

    static void BenchSolidify(string label, DMesh3 mesh, bool adaptive, int resolution, int rounds)
    {
        var fwnMs = new double[rounds];
        var exactMs = new double[rounds];
        var sw = new Stopwatch();
        for (int r = -1; r < rounds; ++r) {
            var sFwn = new MeshWindingNumberSolidify(mesh) {
                Adaptive = adaptive, GridResolution = resolution, UseExactWinding = false,
                CleanupResult = false, ReduceTolerance = 0
            };
            sw.Restart();
            sFwn.Compute();
            sw.Stop();
            if (r >= 0) fwnMs[r] = sw.Elapsed.TotalMilliseconds;

            var sExact = new MeshWindingNumberSolidify(mesh) {
                Adaptive = adaptive, GridResolution = resolution, UseExactWinding = true,
                CleanupResult = false, ReduceTolerance = 0
            };
            sw.Restart();
            sExact.Compute();
            sw.Stop();
            if (r >= 0) exactMs[r] = sw.Elapsed.TotalMilliseconds;
        }
        double ft = Median(fwnMs), et = Median(exactMs);
        Console.WriteLine($"| `MeshWindingNumberSolidify` | {label} | {ft:F1} | {et:F1} | {ft / Math.Max(et, 1e-6):F1}x |");
    }

    static void BenchBatchInside(string label, DMesh3 mesh, int nQueries, int rounds)
    {
        Vector3d[] pts = RandomQueries(nQueries, 91);
        var resFwn = new InsideStatus[nQueries];
        var resExact = new InsideStatus[nQueries];
        var fwnMs = new double[rounds];
        var exactMs = new double[rounds];
        var sw = new Stopwatch();
        for (int r = -1; r < rounds; ++r) {
            var tFwn = new DMeshAABBTree3(mesh, true);
            sw.Restart();
            MeshBatchQueries.IsInsideBatch(tFwn, pts, resFwn, InsideMethod.WindingNumber);
            sw.Stop();
            if (r >= 0) fwnMs[r] = sw.Elapsed.TotalMilliseconds;

            var tExact = new DMeshAABBTree3(mesh, true);
            sw.Restart();
            MeshBatchQueries.IsInsideBatch(tExact, pts, resExact, InsideMethod.ExactWindingNumber);
            sw.Stop();
            if (r >= 0) exactMs[r] = sw.Elapsed.TotalMilliseconds;
        }
        double ft = Median(fwnMs), et = Median(exactMs);
        Console.WriteLine($"| `MeshBatchQueries.IsInsideBatch` | {label} | {ft:F1} | {et:F1} | {ft / Math.Max(et, 1e-6):F1}x |");
    }

    static void BenchDeviation(string label, DMesh3 refMesh, int rounds)
    {
        DMesh3 queryMesh = Sphere(14);
        MeshTransforms.Scale(queryMesh, 0.92);
        MeshTransforms.Translate(queryMesh, new Vector3d(0.15, -0.05, 0.08));
        var fwnMs = new double[rounds];
        var exactMs = new double[rounds];
        var sw = new Stopwatch();
        for (int r = -1; r < rounds; ++r) {
            var dFwn = new MeshDeviation(queryMesh, refMesh) { Sign = MeshDeviation.Signs.WindingNumber };
            sw.Restart();
            dFwn.Compute();
            sw.Stop();
            if (r >= 0) fwnMs[r] = sw.Elapsed.TotalMilliseconds;

            var dExact = new MeshDeviation(queryMesh, refMesh) { Sign = MeshDeviation.Signs.ExactWindingNumber };
            sw.Restart();
            dExact.Compute();
            sw.Stop();
            if (r >= 0) exactMs[r] = sw.Elapsed.TotalMilliseconds;
        }
        double ft = Median(fwnMs), et = Median(exactMs);
        Console.WriteLine($"| `MeshDeviation` | {label} | {ft:F1} | {et:F1} | {ft / Math.Max(et, 1e-6):F1}x |");
    }

    static void BenchHierarchicalBoundary(string label, DMesh3 mesh, int nQueries, int rounds)
    {
        Vector3d[] pts = RandomQueries(nQueries, 131);
        var linRes = new InsideStatus[nQueries];
        var hierRes = new InsideStatus[nQueries];
        var fwnRes = new InsideStatus[nQueries];
        var linMs = new double[rounds];
        var hierMs = new double[rounds];
        var fwnMs = new double[rounds];
        var sw = new Stopwatch();

        int boundaryCount = 0;
        for (int r = -1; r < rounds; ++r) {
            var tLin = new DMeshAABBTree3(mesh, true);
            sw.Restart();
            tLin.WindingNumber(Vector3d.Zero);
            gParallel.ForEach(Interval1i.Range(nQueries), i => {
                linRes[i] = tLin.WindingNumber(pts[i]) > 0.5 ? InsideStatus.Inside : InsideStatus.Outside;
            });
            sw.Stop();
            if (r == 0) boundaryCount = GetCacheInfo(tLin).boundaryCount;
            if (r >= 0) linMs[r] = sw.Elapsed.TotalMilliseconds;

            var tHier = new DMeshAABBTree3(mesh, true);
            sw.Restart();
            MeshBatchQueries.IsInsideBatch(tHier, pts, hierRes, InsideMethod.ExactWindingNumber);
            sw.Stop();
            if (r >= 0) hierMs[r] = sw.Elapsed.TotalMilliseconds;

            var tFwn = new DMeshAABBTree3(mesh, true);
            sw.Restart();
            MeshBatchQueries.IsInsideBatch(tFwn, pts, fwnRes, InsideMethod.WindingNumber);
            sw.Stop();
            if (r >= 0) fwnMs[r] = sw.Elapsed.TotalMilliseconds;
        }

        int matches = 0;
        for (int i = 0; i < nQueries; ++i)
            if (linRes[i] == hierRes[i]) matches++;

        double lt = Median(linMs), ht = Median(hierMs), ft = Median(fwnMs);
        Console.WriteLine($"| {label} | {mesh.TriangleCount} | {boundaryCount} | {nQueries} | {lt:F1} | {ht:F1} | {ft:F1} | {lt / Math.Max(ht, 1e-6):F1}x | {matches}/{nQueries} |");
    }
}
