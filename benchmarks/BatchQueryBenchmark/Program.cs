// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Diagnostics;
using System.Linq;
using g3;

// Upper bound for batch ray / point queries (README.md): loop of single calls vs batched serial vs batched parallel vs
// the Vector256 filter. Public API of g3 only; the prototype BVH lives in Bvh.cs / Kernels.cs.
static class Program
{
    static int rounds = 3;

    static DMesh3 Sphere(int e)
    {
        return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = e }.Generate().MakeDMesh();
    }

    static Vector3d RandDir(Random r)
    {
        Vector3d d;
        do d = new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
        while (d.LengthSquared > 1 || d.LengthSquared < 1e-3);
        return d.Normalized;
    }

    static void Shuffle<T>(T[] a, int seed)
    {
        var r = new Random(seed);
        for (int i = a.Length - 1; i > 0; --i) { int j = r.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
    }

    static Ray3d[] RayWorkload(string kind, int side)
    {
        int n = side * side; var rays = new Ray3d[n];
        if (kind == "random") {
            var r = new Random(11);
            for (int i = 0; i < n; ++i) { var o = 3 * RandDir(r); var target = 1.2 * RandDir(r) * Math.Pow(r.NextDouble(), 1 / 3.0); rays[i] = new Ray3d(o, (target - o).Normalized); }
        } else {    // pinhole camera at (0.3, 0.2, 4) looking at the sphere, raster order
            var eye = new Vector3d(0.3, 0.2, 4);
            for (int j = 0; j < side; ++j)
                for (int i = 0; i < side; ++i) {
                    var target = new Vector3d((i + 0.5) / side * 2.6 - 1.3, (j + 0.5) / side * 2.6 - 1.3, 0);
                    rays[j * side + i] = new Ray3d(eye, (target - eye).Normalized);
                }
            if (kind == "camera-shuffled") Shuffle(rays, 5);
        }
        return rays;
    }

    static Vector3d[] PointWorkload(string kind, int side)
    {
        int n = side * side * side; var pts = new Vector3d[n];
        if (kind == "random") {
            var r = new Random(13);
            for (int i = 0; i < n; ++i) pts[i] = new Vector3d(r.NextDouble() * 2.4 - 1.2, r.NextDouble() * 2.4 - 1.2, r.NextDouble() * 2.4 - 1.2);
        } else {
            for (int k = 0; k < side; ++k) for (int j = 0; j < side; ++j) for (int i = 0; i < side; ++i)
                pts[(k * side + j) * side + i] = new Vector3d((i + 0.5) / side * 2.4 - 1.2, (j + 0.5) / side * 2.4 - 1.2, (k + 0.5) / side * 2.4 - 1.2);
            if (kind == "grid-shuffled") Shuffle(pts, 6);
        }
        return pts;
    }

    // time: median of `rounds` after one warm-up, in ns per query
    static double Time(Action a, int n)
    {
        a();
        var t = new double[rounds];
        for (int r = 0; r < rounds; ++r) { var sw = Stopwatch.StartNew(); a(); t[r] = sw.Elapsed.TotalMilliseconds * 1e6 / n; }
        Array.Sort(t); return t[rounds / 2];
    }

    static void Row(string mesh, int tris, string query, string wl, string mode, double ns, double baseNs, string check)
    {
        Console.WriteLine($"{mesh},{tris},{query},{wl},{mode},{ns:F0},{1000.0 / ns:F2},{baseNs / ns:F2}x,{check}");
    }

    static void Rays(string name, DMesh3 mesh, DMeshAABBTree3 tree, Bvh bvh, string wl, int side)
    {
        var rays = RayWorkload(wl, side); int n = rays.Length;
        var res0 = new int[n]; var res = new int[n];
        double bb = 0;
        // (a) library, loop of single calls
        double a = Time(() => { for (int i = 0; i < n; ++i) res0[i] = tree.FindNearestHitTriangle(rays[i]); }, n);
        Row(name, mesh.TriangleCount, "FindNearestHit", wl, "a-loop-library", a, a, "ref");
        bb = a;
        string Chk(int[] r) { int diff = 0; for (int i = 0; i < n; ++i) if (r[i] != res0[i]) diff++; return diff == 0 ? "equal" : $"DIFF{diff}"; }

        // (b0) prototype BVH, input order, scalar filter, one ray at a time
        var stack = new int[128]; var stackT = new double[128];
        double b0 = Time(() => { for (int i = 0; i < n; ++i) res[i] = Kernels.Closest(bvh, rays[i], stack, stackT, false); }, n);
        Row(name, mesh.TriangleCount, "FindNearestHit", wl, "b2-ownbvh-loop-scalar", b0, bb, Chk(res));

        // sort cost and batched serial / parallel, scalar and SIMD filter
        var bbx = mesh.GetBounds(); double lo = bbx.Min.x - 4, ext = 8 + (bbx.Max.x - bbx.Min.x);
        var keys = new ulong[n]; var order = new int[n];
        Action sort = () => {
            for (int i = 0; i < n; ++i) { keys[i] = Kernels.RayKey(rays[i], lo, ext); order[i] = i; }
            Array.Sort(keys, order);
        };
        double sortNs = Time(sort, n);
        Row(name, mesh.TriangleCount, "FindNearestHit", wl, "sort-only", sortNs, bb, "-");
        // (b1) batching over the existing DMeshAABBTree3, public API only: sorted order, parallel, same single-call query
        var o1 = new int[n]; var o1p = new int[n];
        double b1 = Time(() => { sort(); for (int i = 0; i < n; ++i) { int k = order[i]; o1[k] = tree.FindNearestHitTriangle(rays[k]); } }, n);
        Row(name, mesh.TriangleCount, "FindNearestHit", wl, "b1-library-sorted-serial", b1, bb, Chk(o1));
        double b1p = Time(() => { sort(); gParallel.BlockStartEnd(0, n - 1, (s, e) => { for (int i = s; i <= e; ++i) { int k = order[i]; o1p[k] = tree.FindNearestHitTriangle(rays[k]); } }, 2048); }, n);
        Row(name, mesh.TriangleCount, "FindNearestHit", wl, "b1-library-sorted-parallel", b1p, bb, Chk(o1p) + (o1p.SequenceEqual(o1) ? "/par=ser" : "/PAR!=SER"));
        var outS = new int[n]; var outP = new int[n]; var outV = new int[n]; var outVP = new int[n];
        Action<bool, int[]> serial = (simd, o) => {
            sort();
            for (int i = 0; i < n; ++i) { int k = order[i]; o[k] = Kernels.Closest(bvh, rays[k], stack, stackT, simd); }
        };
        Action<bool, int[]> parallel = (simd, o) => {
            sort();
            gParallel.BlockStartEnd(0, n - 1, (s, e) => {
                var st = new int[128]; var stt = new double[128];
                for (int i = s; i <= e; ++i) { int k = order[i]; o[k] = Kernels.Closest(bvh, rays[k], st, stt, simd); }
            }, 2048);
        };
        double b = Time(() => serial(false, outS), n); Row(name, mesh.TriangleCount, "FindNearestHit", wl, "b2-ownbvh-sorted-serial-scalar", b, bb, Chk(outS));
        double c = Time(() => parallel(false, outP), n); Row(name, mesh.TriangleCount, "FindNearestHit", wl, "b2-ownbvh-sorted-parallel-scalar", c, bb, Chk(outP) + (outP.SequenceEqual(outS) ? "/par=ser" : "/PAR!=SER"));
        double d = Time(() => serial(true, outV), n); Row(name, mesh.TriangleCount, "FindNearestHit", wl, "b2-ownbvh-sorted-serial-simd", d, bb, Chk(outV) + (outV.SequenceEqual(outS) ? "/simd=scalar" : "/SIMD!=SCALAR"));
        double dp = Time(() => parallel(true, outVP), n); Row(name, mesh.TriangleCount, "FindNearestHit", wl, "b2-ownbvh-sorted-parallel-simd", dp, bb, Chk(outVP) + (outVP.SequenceEqual(outS) ? "/simd=scalar" : "/SIMD!=SCALAR"));
    }

    static void Points(string name, DMesh3 mesh, DMeshAABBTree3 tree, Bvh bvh, string wl, int side)
    {
        var pts = PointWorkload(wl, side); int n = pts.Length;
        var in0 = new bool[n]; var inR = new bool[n];
        double a = Time(() => { for (int i = 0; i < n; ++i) in0[i] = tree.IsInside(pts[i]); }, n);
        Row(name, mesh.TriangleCount, "IsInside", wl, "a-loop-library", a, a, "ref");
        string Chk(bool[] r) { int diff = 0; for (int i = 0; i < n; ++i) if (r[i] != in0[i]) diff++; return diff == 0 ? "equal" : $"DIFF{diff}"; }
        var bbx = mesh.GetBounds(); double lo = -1.3, ext = 2.6;
        var keys = new ulong[n]; var order = new int[n];
        Action sort = () => {
            for (int i = 0; i < n; ++i) keys[i] = Bvh.Morton((pts[i].x - lo) / ext, (pts[i].y - lo) / ext, (pts[i].z - lo) / ext);
            for (int i = 0; i < n; ++i) order[i] = i;
            Array.Sort(keys, order);
        };
        Row(name, mesh.TriangleCount, "IsInside", wl, "sort-only", Time(sort, n), a, "-");
        var o1 = new bool[n]; var o1p = new bool[n];
        double b1 = Time(() => { sort(); for (int i = 0; i < n; ++i) { int k = order[i]; o1[k] = tree.IsInside(pts[k]); } }, n);
        Row(name, mesh.TriangleCount, "IsInside", wl, "b1-library-sorted-serial", b1, a, Chk(o1));
        double b1p = Time(() => { sort(); gParallel.BlockStartEnd(0, n - 1, (s, e) => { for (int i = s; i <= e; ++i) { int k = order[i]; o1p[k] = tree.IsInside(pts[k]); } }, 2048); }, n);
        Row(name, mesh.TriangleCount, "IsInside", wl, "b1-library-sorted-parallel", b1p, a, Chk(o1p) + (o1p.SequenceEqual(o1) ? "/par=ser" : "/PAR!=SER"));
        var stack = new int[128];
        var outS = new bool[n]; var outP = new bool[n]; var outV = new bool[n]; var outVP = new bool[n];
        Action<bool, bool[], bool> run = (simd, o, par) => {
            sort();
            if (!par) { for (int i = 0; i < n; ++i) { int k = order[i]; o[k] = (Kernels.Crossings(bvh, pts[k], stack, simd) & 1) != 0; } }
            else gParallel.BlockStartEnd(0, n - 1, (s, e) => {
                var st = new int[128];
                for (int i = s; i <= e; ++i) { int k = order[i]; o[k] = (Kernels.Crossings(bvh, pts[k], st, simd) & 1) != 0; }
            }, 2048);
        };
        double b = Time(() => run(false, outS, false), n); Row(name, mesh.TriangleCount, "IsInside", wl, "b2-ownbvh-sorted-serial-scalar", b, a, Chk(outS));
        double c = Time(() => run(false, outP, true), n); Row(name, mesh.TriangleCount, "IsInside", wl, "b2-ownbvh-sorted-parallel-scalar", c, a, Chk(outP) + (outP.SequenceEqual(outS) ? "/par=ser" : "/PAR!=SER"));
        double d = Time(() => run(true, outV, false), n); Row(name, mesh.TriangleCount, "IsInside", wl, "b2-ownbvh-sorted-serial-simd", d, a, Chk(outV) + (outV.SequenceEqual(outS) ? "/simd=scalar" : "/SIMD!=SCALAR"));
        double dp = Time(() => run(true, outVP, true), n); Row(name, mesh.TriangleCount, "IsInside", wl, "b2-ownbvh-sorted-parallel-simd", dp, a, Chk(outVP) + (outVP.SequenceEqual(outS) ? "/simd=scalar" : "/SIMD!=SCALAR"));

        // winding number alternative (library public API): loop vs parallel loop in sorted order
        tree.FastWindingNumber(pts[0]);
        var w = new bool[n]; var wp = new bool[n];
        double f = Time(() => { for (int i = 0; i < n; ++i) w[i] = tree.FastWindingNumber(pts[i]) > 0.5; }, n);
        Row(name, mesh.TriangleCount, "FastWindingNumber>0.5", wl, "a-loop-library", f, a, "inside-agree=" + (n - Enumerable.Range(0, n).Count(i => w[i] != in0[i])) + "/" + n);
        double fp = Time(() => { sort(); gParallel.BlockStartEnd(0, n - 1, (s, e) => { for (int i = s; i <= e; ++i) { int k = order[i]; wp[k] = tree.FastWindingNumber(pts[k]) > 0.5; } }, 2048); }, n);
        Row(name, mesh.TriangleCount, "FastWindingNumber>0.5", wl, "c-batch-parallel", fp, a, wp.SequenceEqual(w) ? "par=ser" : "PAR!=SER");
    }

    // nearest triangle + point + distance: loop of single calls vs b1 (sorted, serial / parallel), public API only
    static void Nearest(string name, DMesh3 mesh, DMeshAABBTree3 tree, string wl, int side)
    {
        var pts = PointWorkload(wl, side); int n = pts.Length;
        var r0 = new int[n]; var d0 = new double[n]; var r1 = new int[n]; var d1 = new double[n]; var r2 = new int[n]; var d2 = new double[n];
        var qp0 = new Vector3d[n]; var qp1 = new Vector3d[n]; var qp2 = new Vector3d[n];
        Action<int, int[], double[], Vector3d[]> one = (i, tri, dist, cp) => {
            int t = tree.FindNearestTriangle(pts[i], out double dsq);
            tri[i] = t; dist[i] = Math.Sqrt(dsq);
            var dt = MeshQueries.TriangleDistance(mesh, t, pts[i]); cp[i] = dt.TriangleClosest;
        };
        double a = Time(() => { for (int i = 0; i < n; ++i) one(i, r0, d0, qp0); }, n);
        Row(name, mesh.TriangleCount, "NearestPoint", wl, "a-loop-library", a, a, "ref");
        string Chk(int[] r, double[] d, Vector3d[] q) { int diff = 0; for (int i = 0; i < n; ++i) if (r[i] != r0[i] || d[i] != d0[i] || q[i] != qp0[i]) diff++; return diff == 0 ? "equal" : $"DIFF{diff}"; }
        var keys = new ulong[n]; var order = new int[n];
        Action sort = () => {
            for (int i = 0; i < n; ++i) { keys[i] = Bvh.Morton((pts[i].x + 1.3) / 2.6, (pts[i].y + 1.3) / 2.6, (pts[i].z + 1.3) / 2.6); order[i] = i; }
            Array.Sort(keys, order);
        };
        double b = Time(() => { sort(); for (int i = 0; i < n; ++i) one(order[i], r1, d1, qp1); }, n);
        Row(name, mesh.TriangleCount, "NearestPoint", wl, "b1-library-sorted-serial", b, a, Chk(r1, d1, qp1));
        double c = Time(() => { sort(); gParallel.BlockStartEnd(0, n - 1, (s, e) => { for (int i = s; i <= e; ++i) one(order[i], r2, d2, qp2); }, 2048); }, n);
        Row(name, mesh.TriangleCount, "NearestPoint", wl, "b1-library-sorted-parallel", c, a, Chk(r2, d2, qp2));
    }

    // the library API (MeshBatchQueries) against the loop of single calls, results compared bit for bit
    static void Api(string name, DMesh3 mesh, DMeshAABBTree3 tree, int raySide, int pointSide)
    {
        foreach (var wl in new[] { "camera", "camera-shuffled", "random" }) {
            var rays = RayWorkload(wl, raySide); int n = rays.Length;
            var ref0 = new RayHit[n]; var hits = new RayHit[n];
            double a = Time(() => { for (int i = 0; i < n; ++i) { int t = tree.FindNearestHitTriangle(rays[i]); ref0[i] = new RayHit() { Triangle = t, T = t == DMesh3.InvalidID ? double.MaxValue : MeshQueries.TriangleIntersection(mesh, t, rays[i]).RayParameter }; } }, n);
            Row(name, mesh.TriangleCount, "IntersectBatch", wl, "a-loop-library", a, a, "ref");
            string Chk() { int d = 0; for (int i = 0; i < n; ++i) if (hits[i].Triangle != ref0[i].Triangle || BitConverter.DoubleToInt64Bits(hits[i].T) != BitConverter.DoubleToInt64Bits(ref0[i].T)) d++; return d == 0 ? "equal" : "DIFF" + d; }
            foreach (var (label, o) in new[] { ("api-serial-unsorted", new BatchOptions() { Parallel = false, SortForCoherence = false }), ("api-serial-sorted", new BatchOptions() { Parallel = false }), ("api-parallel-sorted", new BatchOptions()) }) {
                double x = Time(() => MeshBatchQueries.IntersectBatch(tree, rays, hits, null, o), n);
                Row(name, mesh.TriangleCount, "IntersectBatch", wl, label, x, a, Chk());
            }
        }
        foreach (var wl in new[] { "grid", "grid-shuffled", "random" }) {
            var pts = PointWorkload(wl, pointSide); int n = pts.Length;
            var ref0 = new NearestPointResult[n]; var res = new NearestPointResult[n];
            double a = Time(() => { for (int i = 0; i < n; ++i) { int t = tree.FindNearestTriangle(pts[i], out double dsq); var dt = MeshQueries.TriangleDistance(mesh, t, pts[i]); ref0[i] = new NearestPointResult() { Triangle = t, Point = dt.TriangleClosest, Bary = dt.TriangleBaryCoords, Distance = Math.Sqrt(dsq) }; } }, n);
            Row(name, mesh.TriangleCount, "NearestPointBatch", wl, "a-loop-library", a, a, "ref");
            string Chk() { int d = 0; for (int i = 0; i < n; ++i) if (res[i].Triangle != ref0[i].Triangle || res[i].Point != ref0[i].Point || res[i].Bary != ref0[i].Bary || BitConverter.DoubleToInt64Bits(res[i].Distance) != BitConverter.DoubleToInt64Bits(ref0[i].Distance)) d++; return d == 0 ? "equal" : "DIFF" + d; }
            foreach (var (label, o) in new[] { ("api-serial-unsorted", new BatchOptions() { Parallel = false, SortForCoherence = false }), ("api-serial-sorted", new BatchOptions() { Parallel = false }), ("api-parallel-sorted", new BatchOptions()) }) {
                double x = Time(() => MeshBatchQueries.NearestPointBatch(tree, pts, res, null, o), n);
                Row(name, mesh.TriangleCount, "NearestPointBatch", wl, label, x, a, Chk());
            }
        }
    }

    static void Main(string[] args)
    {
        int raySide = args.Length > 0 ? int.Parse(args[0]) : 512;      // rays = side^2
        int pointSide = args.Length > 1 ? int.Parse(args[1]) : 64;     // points = side^3
        var sizes = args.Length > 2 ? args[2].Split(',').Select(int.Parse).ToArray() : new[] { 30, 92, 290 };    // sphere EdgeVertices: ~10k, 100k, 1M triangles
        Console.WriteLine($"# cores={Environment.ProcessorCount} rays={raySide * raySide} points={pointSide * pointSide * pointSide} rounds={rounds}");
        Console.WriteLine("mesh,triangles,query,workload,mode,ns/query,Mqueries/s,speedup vs a,check");
        bool nearestOnly = args.Length > 3 && args[3] == "nearest";
        bool api = args.Length > 3 && args[3] == "api";
        foreach (int e in sizes) {
            var mesh = Sphere(e); var tree = new DMeshAABBTree3(mesh, true); var bvh = new Bvh(mesh);
            if (api) { Api("sphere", mesh, tree, raySide, pointSide); continue; }
            if (nearestOnly) { foreach (var wl in new[] { "grid", "grid-shuffled", "random" }) Nearest("sphere", mesh, tree, wl, pointSide); continue; }
            foreach (var wl in new[] { "camera", "camera-shuffled", "random" }) Rays("sphere", mesh, tree, bvh, wl, raySide);
            foreach (var wl in new[] { "grid", "grid-shuffled", "random" }) Points("sphere", mesh, tree, bvh, wl, pointSide);
        }
    }
}
