// MeshOffset (docs/design/MESH_OFFSET.md, stage A1) against the signed distance grid + marching cubes path of `g3cli offset`
// (docs/examples/OffsetSurfaceExample.cs), on procedural inputs only (no mesh files). Both results get the same band
// certificate (MeshOffset.CertifyBand, tolerance half a cell, 6 splits), so the band column compares like with like.
// Usage:
//   dotnet run -c Release --project benchmarks/MeshOffsetBenchmark -- [repetitions (default 3)] [serial]
//   dotnet run -c Release --project benchmarks/MeshOffsetBenchmark -- certify [repetitions (default 3)] [serial]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

static class Program
{
    static DMesh3 Box(Vector3d min, Vector3d max) => new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)) }.Generate().MakeDMesh();

    static IEnumerable<(string, DMesh3)> Inputs()
    {
        yield return ("sphere", new Sphere3Generator_NormalizedCube() { Radius = 5, EdgeVertices = 11 }.Generate().MakeDMesh());
        foreach (string n in Procedural.Names) yield return (n, Procedural.Make(n));
        DMesh3 hollow = Box(Vector3d.Zero, new Vector3d(4, 4, 4));
        DMesh3 inner = Box(Vector3d.One, new Vector3d(3, 3, 3));
        inner.ReverseOrientation();
        new MeshEditor(hollow).AppendMesh(inner);
        yield return ("hollow", hollow);
    }

    static DMesh3 SdfMc(DMesh3 mesh, double d, double cell, bool parallel)
    {
        AxisAlignedBox3d box = mesh.CachedBounds;
        double margin = Math.Max(d, 0) + 2 * cell;
        var gridBox = new AxisAlignedBox3d(box.Min - margin * Vector3d.One, box.Max + margin * Vector3d.One);
        int band = (int)Math.Ceiling(Math.Abs(d) / cell) + 2;
        var sdf = new MeshSignedDistanceGrid(mesh, cell) { ExactBandWidth = band, UseParallel = parallel };
        sdf.Compute();
        var mc = new MarchingCubes() {
            Implicit = new DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOriginD, sdf.CellSize), IsoValue = d, Bounds = gridBox, CubeSize = cell, ParallelCompute = parallel
        };
        mc.Generate();
        return mc.Mesh;
    }

    static void RunCertify(int reps, bool parallel)
    {
        var inv = CultureInfo.InvariantCulture;
        var cases = new[] {
            ("sphere", 0.05, 0.125),
            ("sphere", -0.05, 0.125),
            ("box", 0.05, 0.125),
            ("box", -0.05, 0.125),
            ("lshape", 0.05, 0.125),
            ("hollow", 0.05, 0.125),
        };
        var byName = Inputs().ToDictionary(x => x.Item1, x => x.Item2);
        Console.WriteLine($"# MeshOffset.CertifyBand & Compute, {Environment.ProcessorCount} threads{(parallel ? "" : " (serial)")}, median of {reps}");
        Console.WriteLine("| input | d / diag | cell / abs(d) | tris | CertifyBand ms | Compute ms | band / abs(d) | certified |");
        Console.WriteLine("|---|---|---|---:|---:|---:|---:|---|");
        foreach (var (name, rel, cellRel) in cases) {
            DMesh3 mesh = byName[name];
            double diag = mesh.CachedBounds.Diagonal.Length;
            double d = rel * diag, cell = cellRel * Math.Abs(d), tol = 0.5 * cell;
            // Warmup + reference surface for isolated CertifyBand timing
            var warm = new MeshOffset(mesh, d) { RelativeCellSize = cellRel, UseParallel = parallel };
            warm.Compute();
            DMesh3 surf = warm.Result;
            double dev = double.PositiveInfinity; int unc = 0; bool cert = false;
            MeshOffset.CertifyBand(mesh, surf, d, tol, out dev, out unc, 6, parallel);

            var bandTimes = new List<double>();
            for (int r = 0; r < reps; ++r) {
                var sw = Stopwatch.StartNew();
                cert = MeshOffset.CertifyBand(mesh, surf, d, tol, out dev, out unc, 6, parallel);
                sw.Stop();
                bandTimes.Add(sw.Elapsed.TotalMilliseconds);
            }
            bandTimes.Sort();

            var fullTimes = new List<double>();
            MeshOffset o = null;
            for (int r = 0; r < reps; ++r) {
                o = new MeshOffset(mesh, d) { RelativeCellSize = cellRel, UseParallel = parallel };
                var sw = Stopwatch.StartNew(); o.Compute(); sw.Stop();
                fullTimes.Add(sw.Elapsed.TotalMilliseconds);
            }
            fullTimes.Sort();

            Console.WriteLine(string.Format(inv,
                "| {0} | {1} | {2} | {3} | {4:F1} | {5:F1} | {6:F4} | {7} |",
                name, rel, cellRel, surf.TriangleCount, bandTimes[reps / 2], fullTimes[reps / 2], dev / Math.Abs(d), cert && unc == 0));
        }
    }

    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "certify") {
            int creps = args.Length > 1 ? int.Parse(args[1]) : 3;
            bool cpar = !(args.Length > 2 && args[2] == "serial");
            RunCertify(creps, cpar);
            return;
        }
        int reps = args.Length > 0 ? int.Parse(args[0]) : 3;
        bool parallel = !(args.Length > 1 && args[1] == "serial");
        Console.WriteLine($"# MeshOffset vs SDF+MC, {Environment.ProcessorCount} threads{(parallel ? "" : " (serial)")}, median of {reps}");
        Console.WriteLine("| input | d / diag | cell / abs(d) | method | ms | tris | clean | SI-free | band / abs(d) | certified (tol cell/2) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, mesh) in Inputs()) {
            double diag = mesh.CachedBounds.Diagonal.Length;
            foreach (double rel in new[] { 0.05, -0.05 })
                foreach (double cellRel in new[] { 0.25, 0.125 }) {
                    double d = rel * diag, cell = cellRel * Math.Abs(d), tol = 0.5 * cell;
                    // MeshOffset
                    var times = new List<long>();
                    MeshOffset o = null;
                    for (int r = 0; r < reps; ++r) {
                        o = new MeshOffset(mesh, d) { RelativeCellSize = cellRel, UseParallel = parallel };
                        var sw = Stopwatch.StartNew(); o.Compute(); times.Add(sw.ElapsedMilliseconds);
                    }
                    times.Sort();
                    string clean = o.Result != null ? MeshRobustRepair.NotCleanReason(o.Result) : o.ResultStatus.ToString();
                    Console.WriteLine($"| {name} | {rel} | {cellRel} | MeshOffset | {times[reps / 2]} | {o.Result?.TriangleCount} | {(clean == "" ? "yes" : clean)} | {o.SelfIntersectionFree} | {o.BandDeviation / Math.Abs(d):F4} | {o.BandCertified} |");
                    // SDF + marching cubes, then the same certificate
                    times.Clear();
                    DMesh3 mc = null;
                    for (int r = 0; r < reps; ++r) {
                        var sw = Stopwatch.StartNew(); mc = SdfMc(mesh, d, cell, parallel); times.Add(sw.ElapsedMilliseconds);
                    }
                    times.Sort();
                    double dev = double.PositiveInfinity; int unc;
                    bool cert = mc.TriangleCount > 0 && MeshOffset.CertifyBand(mesh, mc, d, tol, out dev, out unc, 6, parallel);
                    string mclean = mc.TriangleCount > 0 ? MeshRobustRepair.NotCleanReason(mc) : "empty";
                    bool si = mc.TriangleCount > 0 && MeshRepairMetrics.HasSelfIntersections(mc);
                    Console.WriteLine($"| {name} | {rel} | {cellRel} | SDF+MC | {times[reps / 2]} | {mc.TriangleCount} | {(mclean == "" ? "yes" : mclean)} | {!si} | {dev / Math.Abs(d):F4} | {cert} |");
                }
        }
    }
}
