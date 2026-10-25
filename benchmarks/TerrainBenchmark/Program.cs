// Terrain benchmark on open geodata of the Hambach open-pit mine: runs one case and prints one JSON line.
// Usage: TerrainBenchmark <case> key=value ...
//   common keys: win=x0,y0,x1,y1 (UTM window), coords=utm|local (local: everything shifted to the window corner)
//   volume   before=<grid> after=<grid> [simplify=<m>] [heightcheck=1]
//   repair   grid=<grid> variant=clean|tiles|overlap|holes mode=auto|cleanup|robust|tdc
//   tdc      grid=<grid> [points=<cloud>] [cell=1] [tol=0]
//   offset   grid=<grid> d=<m> [cell=1]
//   safety   grid=<grid> d=<m> mode=horizontal|surface [terrain=grid|tdc] [tol=0.05] [angle=20]
//   geodesic grid=<grid> [src=center|crest] [tf=1]
//   csf      points=<cloud> grid=<dgm grid> [res=1] [threshold=0.5] [rigidness=2]
// Grids and clouds are names in data/ (see fetch_data.py).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using g3;
using gs;
using TerrainBenchmark;

class Program
{
    static Dictionary<string, string> A = new Dictionary<string, string>();
    static Dictionary<string, object> R = new Dictionary<string, object>();
    static string dataDir = "data";
    static Vector3d origin = Vector3d.Zero;

    static string Arg(string k, string def = null) { string v; return A.TryGetValue(k, out v) ? v : def; }
    static double ArgD(string k, double def) { string v = Arg(k); return v == null ? def : double.Parse(v, CultureInfo.InvariantCulture); }

    static int Main(string[] args)
    {
        var total = Stopwatch.StartNew();
        string kase = args[0];
        R["case"] = kase;
        foreach (string a in args.Skip(1)) {
            int k = a.IndexOf('=');
            A[a.Substring(0, k)] = a.Substring(k + 1);
            R[a.Substring(0, k)] = a.Substring(k + 1);
        }
        dataDir = Environment.GetEnvironmentVariable("TERRAIN_DATA") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "../../../data");
        string stage = "setup";
        try {
            double[] w = (Arg("win") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (w.Length == 4 && Arg("coords", "utm") == "local")
                origin = new Vector3d(Math.Floor(w[0]), Math.Floor(w[1]), 0);
            R["status"] = "ok";
            switch (kase) {
                case "volume": stage = kase; Volume(w); break;
                case "repair": stage = kase; Repair(w); break;
                case "tdc": stage = kase; Tdc(w); break;
                case "offset": stage = kase; Offset(w); break;
                case "safety": stage = kase; Safety(w); break;
                case "geodesic": stage = kase; Geodesic(w); break;
                case "csf": stage = kase; Csf(w); break;
                default: throw new Exception("unknown case " + kase);
            }
        } catch (Exception e) {
            R["status"] = "exception";
            R["stage"] = stage;
            R["error"] = e.GetType().Name + ": " + e.Message;
            R["trace"] = e.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
        }
        R["total_s"] = total.Elapsed.TotalSeconds;
        R["peak_mb"] = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0;
        Console.WriteLine(JsonSerializer.Serialize(R, new JsonSerializerOptions() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        return 0;
    }

    static Grid LoadGrid(string name, double[] w)
    {
        Grid g = Grid.Load(System.IO.Path.Combine(dataDir, name));
        return (w.Length == 4) ? g.Window(w[0], w[1], w[2], w[3]) : g;
    }

    static double Secs(Stopwatch sw) { double s = sw.Elapsed.TotalSeconds; sw.Restart(); return s; }

    static int NaNVertices(DMesh3 m)
    {
        int n = 0;
        foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); if (!(p.x + p.y + p.z).IsFinite()) n++; }
        return n;
    }


    // ---------------------------------------------------------------- volume
    static void Volume(double[] w)
    {
        var sw = Stopwatch.StartNew();
        Grid gb = LoadGrid(Arg("before"), w), ga = LoadGrid(Arg("after"), w);
        DMesh3 before = gb.BuildMesh(origin), after = ga.BuildMesh(origin);
        R["build_s"] = Secs(sw);
        double simplify = ArgD("simplify", 0);
        if (simplify > 0) {
            before = Simplify(before, gb.Dx, simplify);
            after = Simplify(after, ga.Dx, simplify);
            R["simplify_s"] = Secs(sw);
        }
        R["before_tris"] = before.TriangleCount; R["after_tris"] = after.TriangleCount;
        var vd = new TerrainVolumeDifference(before, after) { RequireHeightField = Arg("heightcheck", "1") == "1" };
        bool ok = vd.Compute();
        R["volume_s"] = Secs(sw);
        R["ok"] = ok;
        R["cut"] = vd.CutVolume; R["fill"] = vd.FillVolume; R["net"] = vd.NetVolume;
        R["overlap_area"] = vd.OverlapArea; R["cut_area"] = vd.CutArea; R["fill_area"] = vd.FillArea;
        R["before_overlaps"] = vd.BeforeOverlaps; R["after_overlaps"] = vd.AfterOverlaps;
    }

    static DMesh3 Simplify(DMesh3 mesh, double cell, double tol)
    {
        var tdc = new TerrainDualContouring(mesh) { CellSize = cell, Tolerance = tol };
        if (tdc.Compute() == false) throw new Exception("TerrainDualContouring failed");
        return tdc.Result;
    }


    // ---------------------------------------------------------------- repair
    static void Repair(double[] w)
    {
        var sw = Stopwatch.StartNew();
        Grid g = LoadGrid(Arg("grid"), w);
        string variant = Arg("variant", "clean");
        DMesh3 mesh = BuildVariant(g, variant);
        R["build_s"] = Secs(sw);
        TerrainMetrics(mesh, g, "before");
        R["metrics_before_s"] = Secs(sw);
        string mode = Arg("mode", "auto");
        DMesh3 result;
        if (mode == "tdc") {
            // the terrain way: rebuild the top surface from vertical rays (soups, overlaps, seams do not matter)
            var tdc = new TerrainDualContouring(mesh) { CellSize = g.Dx, Tolerance = 0.01 };
            R["ok"] = tdc.Compute();
            result = tdc.Result;
        } else if (mode == "robust") {
            var rr = new MeshRobustRepair(mesh) { ConfigureRepair = (ar) => { ar.MinEdgeLengthTol = 1e-4; } };
            R["ok"] = rr.Apply();
            R["used_solidify"] = rr.UsedSolidify;
            R["fallback_reason"] = rr.FallbackReason;
            result = rr.Mesh;
        } else {
            var ar = new MeshAutoRepair(mesh) { PostRepairCleanup = (mode == "cleanup") };
            R["ok"] = ar.Apply();
            result = ar.Mesh;
        }
        R["repair_s"] = Secs(sw);
        TerrainMetrics(result, g, "after");
    }

    // the grid TIN, as the defects of real survey data: tiles not welded, overlapping tiles, holes
    static DMesh3 BuildVariant(Grid g, string variant)
    {
        int cx = g.NX - 1, cy = g.NY - 1;
        if (variant == "clean")
            return g.BuildMesh(origin);
        if (variant == "tiles" || variant == "overlap") {
            // 2 x 2 tiles, each with its own vertices; "overlap": each extends 2 cells into its neighbours
            int ov = (variant == "overlap") ? 2 : 0;
            DMesh3 mesh = new DMesh3(MeshComponents.None);
            for (int a = 0; a < 2; ++a)
                for (int b = 0; b < 2; ++b) {
                    int i0 = a * cx / 2, i1 = (a + 1) * cx / 2, j0 = b * cy / 2, j1 = (b + 1) * cy / 2;
                    i0 = Math.Max(0, i0 - ov); j0 = Math.Max(0, j0 - ov);
                    i1 = Math.Min(cx, i1 + ov); j1 = Math.Min(cy, j1 + ov);
                    MeshEditor.Append(mesh, g.BuildMesh(origin, i0, j0, i1, j1));
                }
            return mesh;
        }
        if (variant == "holes") {
            // 16 no-data holes (radius 2..17 m) at fixed places, like gaps in a survey (water, shadows)
            DMesh3 mesh = g.BuildMesh(origin);
            var holes = new List<Vector3d>();
            for (int k = 0; k < 16; ++k)
                holes.Add(new Vector3d(g.X0 + g.Dx * cx * (0.1 + 0.8 * ((k * 7) % 16) / 15.0), g.Y0 + g.Dx * cy * (0.1 + 0.8 * ((k * 11) % 16) / 15.0), 2 + k));
            var remove = new List<int>();
            foreach (int tid in mesh.TriangleIndices()) {
                Vector3d c = mesh.GetTriCentroid(tid) + origin;
                if (holes.Any(h => c.xy.Distance(h.xy) < h.z)) remove.Add(tid);
            }
            foreach (int tid in remove) mesh.RemoveTriangle(tid, true, false);
            return new DMesh3(mesh, true);
        }
        throw new Exception("unknown variant " + variant);
    }

    // properties that matter for a terrain: still a height field over the same footprint, same heights
    static void TerrainMetrics(DMesh3 mesh, Grid g, string p)
    {
        R[p + "_tris"] = mesh.TriangleCount;
        R[p + "_verts"] = mesh.VertexCount;
        R[p + "_nan_verts"] = NaNVertices(mesh);
        int bdry = 0;
        foreach (int eid in mesh.BoundaryEdgeIndices()) bdry++;
        R[p + "_boundary_edges"] = bdry;
        R[p + "_closed"] = mesh.IsClosed();
        R[p + "_bowties"] = MeshIterators.BowtieVertices(mesh).Count();
        double up = 0, down = 0, vert = 0;
        foreach (int tid in mesh.TriangleIndices()) {
            Vector3d nrm = mesh.GetTriNormal(tid);
            double a = mesh.GetTriArea(tid);
            if (Math.Abs(nrm.z) < 0.05) vert += a; else if (nrm.z > 0) up += a * nrm.z; else down += -a * nrm.z;
        }
        R[p + "_footprint_up"] = up;      // XY area of the triangles facing up
        R[p + "_footprint_down"] = down;
        R[p + "_area_vertical"] = vert;
        R[p + "_xy_overlaps"] = mesh.TriangleCount <= 30000000 ? TerrainVolumeDifference.XYOverlaps(mesh) : -1;
        // top surface along vertical rays on a 200 x 200 sample grid (1 cell inside the window) vs the grid TIN
        var tree = new DMeshAABBTree3(mesh, true);
        double top = mesh.CachedBounds.Max.z + 10, depth = mesh.CachedBounds.Depth + 20;
        var dev = new List<double>();
        int miss = 0, n = 200;
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                double x = g.X0 + g.Dx + (g.X1 - g.X0 - 2 * g.Dx) * (i + 0.5) / n, y = g.Y0 + g.Dx + (g.Y1 - g.Y0 - 2 * g.Dx) * (j + 0.5) / n;
                double zr = g.Height(x, y);
                var ray = new Ray3d(new Vector3d(x, y, top) - origin, -Vector3d.AxisZ);
                int tid = tree.FindNearestHitTriangle(ray, depth);
                if (tid < 0) { miss++; continue; }
                var intr = MeshQueries.TriangleIntersection(mesh, tid, ray);
                if (intr.Find() == false) { miss++; continue; }
                dev.Add(Math.Abs(ray.PointAt(intr.RayParameter).z + origin.z - zr));
            }
        R[p + "_samples_missed"] = miss;
        Checks.Stats(dev, p + "_dz", R);
        int big = dev.Count(d => d > 0.01);
        R[p + "_samples_dz_gt_1cm"] = big;
    }


    // ---------------------------------------------------------------- tdc
    static void Tdc(double[] w)
    {
        var sw = Stopwatch.StartNew();
        Grid g = LoadGrid(Arg("grid"), w);
        TerrainDualContouring tdc;
        string points = Arg("points");
        if (points != null) {
            PointCloud pc = PointCloud.Load(System.IO.Path.Combine(dataDir, points));
            if (w.Length == 4) pc = pc.Window(w[0], w[1], w[2], w[3]);
            var ground = new List<Vector3d>();
            for (int k = 0; k < pc.P.Length; ++k)
                if (pc.Class[k] == 2) ground.Add(pc.P[k] - origin);
            R["points"] = ground.Count;
            tdc = new TerrainDualContouring(ground);
        } else {
            DMesh3 mesh = g.BuildMesh(origin);
            R["input_tris"] = mesh.TriangleCount;
            tdc = new TerrainDualContouring(mesh);
        }
        R["load_s"] = Secs(sw);
        tdc.CellSize = ArgD("cell", 1);
        tdc.Tolerance = ArgD("tol", 0);
        bool ok = tdc.Compute();
        R["tdc_s"] = Secs(sw);
        R["ok"] = ok;
        if (!ok) return;
        DMesh3 res = tdc.Result;
        R["result_tris"] = res.TriangleCount;
        R["leaf_cells"] = tdc.LeafCells; R["finest_cells"] = tdc.FinestCells; R["skipped_tris"] = tdc.SkippedTriangles;
        R["split_leaves"] = tdc.SplitLeaves; R["removed_tris"] = tdc.RemovedTriangles;
        R["nan_verts"] = NaNVertices(res);
        R["xy_overlaps"] = TerrainVolumeDifference.XYOverlaps(res);
        // vertical error at the grid nodes (2 m inside the window)
        var tree = new DMeshAABBTree3(res, true);
        double top = res.CachedBounds.Max.z + 10, depth = res.CachedBounds.Depth + 20;
        var err = new List<double>();
        int miss = 0, stride = Math.Max(1, (int)Math.Sqrt(g.NX * (double)g.NY / 250000));
        for (int j = 2; j < g.NY - 2; j += stride)
            for (int i = 2; i < g.NX - 2; i += stride) {
                var ray = new Ray3d(new Vector3d(g.X(i), g.Y(j), top) - origin, -Vector3d.AxisZ);
                int tid = tree.FindNearestHitTriangle(ray, depth);
                IntrRay3Triangle3 intr = (tid < 0) ? null : MeshQueries.TriangleIntersection(res, tid, ray);
                if (intr == null || intr.Find() == false) { miss++; continue; }
                err.Add(Math.Abs(ray.PointAt(intr.RayParameter).z - g[i, j]));
            }
        R["missed"] = miss;
        Checks.Stats(err, "dz", R);
        R["check_s"] = Secs(sw);
    }


    // ---------------------------------------------------------------- offset
    static void Offset(double[] w)
    {
        var sw = Stopwatch.StartNew();
        Grid g = LoadGrid(Arg("grid"), w);
        DMesh3 mesh = g.BuildMesh(origin);
        double d = ArgD("d", 5);
        var off = new TerrainOffset(mesh, d) { CellSize = ArgD("cell", 1) };
        bool ok = off.Compute();
        R["offset_s"] = Secs(sw);
        R["ok"] = ok;
        if (!ok) return;
        DMesh3 res = off.Result;
        R["result_tris"] = res.TriangleCount;
        R["nan_verts"] = NaNVertices(res);
        // distance of the result vertices to the terrain should be |d| (away from the window border,
        // where the offset of the cut-off terrain is not defined)
        var err = new List<double>();
        int wrongSide = 0, n = 0;
        var verts = res.VertexIndices().ToList();
        int stride = Math.Max(1, verts.Count / 20000);
        for (int k = 0; k < verts.Count; k += stride) {
            Vector3d p = res.GetVertex(verts[k]) + origin;
            if (p.x < g.X0 + 2 * Math.Abs(d) || p.x > g.X1 - 2 * Math.Abs(d) || p.y < g.Y0 + 2 * Math.Abs(d) || p.y > g.Y1 - 2 * Math.Abs(d)) continue;
            n++;
            double dist = Checks.DistanceToTIN(g, p, Math.Abs(d) + 2 * g.Dx);
            err.Add(double.IsInfinity(dist) ? double.MaxValue : dist - Math.Abs(d));
            if (Math.Sign(p.z - g.Height(p.x, p.y)) != Math.Sign(d)) wrongSide++;
        }
        R["checked_verts"] = n;
        R["wrong_side"] = wrongSide;
        Checks.Stats(err, "dist_err", R);
        R["check_s"] = Secs(sw);
    }


    // ---------------------------------------------------------------- safety lines
    static void Safety(double[] w)
    {
        var sw = Stopwatch.StartNew();
        Grid g = LoadGrid(Arg("grid"), w);
        DMesh3 terrain = g.BuildMesh(origin);
        if (Arg("terrain", "grid") == "tdc")
            terrain = Simplify(terrain, g.Dx, ArgD("tol", 0.05));
        R["terrain_tris"] = terrain.TriangleCount;
        R["terrain_s"] = Secs(sw);
        var bl = new TerrainBreakLines(terrain) { MinAngle = ArgD("angle", 20) };
        bl.Compute();
        R["breaklines_s"] = Secs(sw);
        R["crests"] = bl.Crests.Count; R["toes"] = bl.Toes.Count;
        R["crest_length"] = bl.Crests.Sum(l => Length(l));
        R["toe_length"] = bl.Toes.Sum(l => Length(l));
        double d = ArgD("d", 5);
        string mode = Arg("mode", "horizontal");
        var sl = new TerrainSafetyLines(terrain, bl.Crests, d) {
            Mode = (mode == "surface") ? TerrainSafetyLines.Modes.AlongSurface : TerrainSafetyLines.Modes.Horizontal
        };
        bool ok = sl.Compute();
        R["safety_s"] = Secs(sw);
        R["ok"] = ok;
        R["lines"] = sl.Lines.Count;
        R["line_length"] = sl.Lines.Sum(l => Length(l));
        int nanPts = sl.Lines.Sum(l => l.Count(p => !(p.x + p.y + p.z).IsFinite()));
        R["nan_points"] = nanPts;
        R["remeshed_tris"] = sl.RemeshedTerrain?.TriangleCount ?? -1;
        R["geodesic_ok"] = sl.Geodesic?.Distance != null;

        // checks in world coordinates, away from the window border (2 d)
        var sources = bl.Crests.Select(l => l.Select(p => p + origin).ToList()).ToList();
        var lines = sl.Lines.Select(l => l.Select(p => p + origin).ToList()).ToList();
        Func<Vector3d, bool> inside = (p) => p.x > g.X0 + 2 * d && p.x < g.X1 - 2 * d && p.y > g.Y0 + 2 * d && p.y < g.Y1 - 2 * d;
        var hd = new Checks.HorizontalDistance(sources, 2 * d);
        var hErr = new List<double>();
        var onTerrain = new List<double>();
        var linePts = lines.SelectMany(l => l).Where(inside).ToList();
        foreach (Vector3d p in linePts) {
            hErr.Add(hd.Distance(p.xy, 4 * d) - d);
            if (Arg("terrain", "grid") == "grid") onTerrain.Add(Math.Abs(p.z - g.Height(p.x, p.y)));
        }
        Checks.Stats(hErr, "hdist_err", R);
        Checks.Stats(onTerrain, "line_dz", R);
        // coverage: source points (every 1 m) without a line point within d + 1 m horizontally
        var lineHd = new Checks.HorizontalDistance(lines, 2 * d);
        int srcN = 0, uncovered = 0;
        foreach (var l in sources)
            foreach (Vector3d p in Densify(l, 1.0)) {
                if (!inside(p)) continue;
                srcN++;
                if (double.IsInfinity(lineHd.Distance(p.xy, d + 1))) uncovered++;
            }
        R["source_pts"] = srcN; R["source_pts_uncovered"] = uncovered;
        R["check_h_s"] = Secs(sw);

        if (mode == "surface" && Arg("terrain", "grid") == "grid") {
            // shortest surface paths on the grid TIN from the densified crests: an upper bound (up to
            // about 1%) of the distance along the surface; line points with dijkstra < d are too close
            var srcPts = sources.SelectMany(l => Densify(l, 0.25 * g.Dx)).ToList();
            Grid dij = Checks.SurfaceDijkstra(g, srcPts);
            R["dijkstra_s"] = Secs(sw);
            var gErr = new List<double>();
            var eErr = new List<double>();
            var srcHash = new PointHashGrid3d<int>(d, -1);
            for (int k = 0; k < srcPts.Count; ++k) srcHash.InsertPointUnsafe(k, srcPts[k]);
            int tooClose = 0;
            foreach (Vector3d p in linePts) {
                double gd = dij.Height(p.x, p.y);
                if (double.IsNaN(gd)) continue;
                gErr.Add(gd - d);
                if (gd < d) tooClose++;
                var near = srcHash.FindNearestInRadius(p, 4 * d, (k) => srcPts[k].Distance(p));
                eErr.Add(near.Key >= 0 ? near.Value - d : double.MaxValue);
            }
            R["surface_too_close"] = tooClose;
            Checks.Stats(gErr, "surface_err", R);       // dijkstra distance - d
            Checks.Stats(eErr, "euclid_err", R);        // straight 3D distance - d (a lower bound)
            R["check_s_s"] = Secs(sw);
        }
    }

    static double Length(List<Vector3d> l) { double s = 0; for (int i = 1; i < l.Count; ++i) s += l[i].Distance(l[i - 1]); return s; }

    static IEnumerable<Vector3d> Densify(List<Vector3d> l, double step)
    {
        if (l.Count == 1) yield return l[0];
        for (int i = 0; i + 1 < l.Count; ++i) {
            int n = Math.Max(1, (int)Math.Ceiling(l[i].Distance(l[i + 1]) / step));
            for (int k = 0; k < n; ++k) yield return Vector3d.Lerp(l[i], l[i + 1], (double)k / n);
        }
        if (l.Count > 1) yield return l[l.Count - 1];
    }


    // ---------------------------------------------------------------- geodesic
    static void Geodesic(double[] w)
    {
        var sw = Stopwatch.StartNew();
        Grid g = LoadGrid(Arg("grid"), w);
        DMesh3 mesh = g.BuildMesh(origin);       // vertex i + NX j (no no-data in the data used)
        if (mesh.VertexCount != g.NX * g.NY) throw new Exception("grid with no data");
        R["verts"] = mesh.VertexCount;
        var src = new List<int>();
        if (Arg("src", "center") == "center") {
            src.Add(g.NX / 2 + g.NX * (g.NY / 2));
        } else {
            var bl = new TerrainBreakLines(mesh);
            bl.Compute();
            foreach (var l in bl.CrestVertices) src.AddRange(l);
        }
        R["sources"] = src.Count;
        var geo = new MeshGeodesicDistance(mesh) { TimeFactor = ArgD("tf", 1) };
        bool ok = geo.Compute(src);
        R["geodesic_s"] = Secs(sw);
        R["ok"] = ok;
        if (!ok) return;
        var srcPts = src.Select(v => mesh.GetVertex(v) + origin).ToList();
        Grid dij = Checks.SurfaceDijkstra(g, srcPts);
        R["dijkstra_s"] = Secs(sw);
        var rel = new List<double>();
        int nan = 0, belowEuclid = 0, n = 0;
        var srcHash = new PointHashGrid3d<int>(10, -1);
        for (int k = 0; k < srcPts.Count; ++k) srcHash.InsertPointUnsafe(k, srcPts[k]);
        for (int v = 0; v < mesh.VertexCount; ++v) {
            double h = geo.Distance[v], r = dij.Z[v];
            if (double.IsNaN(h)) { nan++; continue; }
            if (!(r > 10)) continue;
            n++;
            rel.Add((h - r) / r);
            // the straight 3D distance is a lower bound of the geodesic distance; the documented accuracy
            // of the heat method is about 2% plus half an edge length
            if (srcPts.Count == 1) {
                double e = (mesh.GetVertex(v) + origin).Distance(srcPts[0]);
                if (h < 0.98 * e - 0.5 * g.Dx * 1.2) belowEuclid++;
            }
        }
        R["nan"] = nan;
        R["below_euclid_beyond_accuracy"] = belowEuclid;
        Checks.Stats(rel, "rel_err", R);
        R["check_s"] = Secs(sw);
    }


    // ---------------------------------------------------------------- ground filter
    static void Csf(double[] w)
    {
        var sw = Stopwatch.StartNew();
        PointCloud pc = PointCloud.Load(System.IO.Path.Combine(dataDir, Arg("points")));
        if (w.Length == 4) pc = pc.Window(w[0], w[1], w[2], w[3]);
        Grid dgm = Grid.Load(System.IO.Path.Combine(dataDir, Arg("grid")));
        if (Arg("coords", "utm") == "local" && w.Length != 4)
            origin = new Vector3d(Math.Floor(dgm.X0), Math.Floor(dgm.Y0), 0);
        var pts = pc.P.Select(p => p - origin).ToArray();
        R["points"] = pts.Length;
        R["load_s"] = Secs(sw);
        var csf = new ClothSimulationFilter(pts) {
            ClothResolution = ArgD("res", 1), ClassThreshold = ArgD("threshold", 0.5), Rigidness = (int)ArgD("rigidness", 2)
        };
        bool ok = csf.Compute();
        R["csf_s"] = Secs(sw);
        R["ok"] = ok;
        R["iterations"] = csf.Iterations;
        if (!ok) return;
        // confusion against the LAS classes: ground = 2, non-ground = 1 (unclassified: vegetation, buildings,
        // vehicles); class 20 is reported separately (mostly near the ground), 26 (synthetic) and 18 (noise) too
        var perClass = new Dictionary<int, int[]>();
        for (int k = 0; k < pts.Length; ++k) {
            int c = pc.Class[k];
            if (!perClass.ContainsKey(c)) perClass[c] = new int[2];
            perClass[c][csf.IsGround[k] ? 1 : 0]++;
        }
        foreach (var kv in perClass) { R["class" + kv.Key + "_n"] = kv.Value[0] + kv.Value[1]; R["class" + kv.Key + "_as_ground"] = kv.Value[1]; }
        long tp = perClass.ContainsKey(2) ? perClass[2][1] : 0, fn = perClass.ContainsKey(2) ? perClass[2][0] : 0;
        long fp = perClass.ContainsKey(1) ? perClass[1][1] : 0, tn = perClass.ContainsKey(1) ? perClass[1][0] : 0;
        double nn = tp + fn + fp + tn;
        R["type1"] = fn / (double)Math.Max(1, tp + fn);        // ground rejected
        R["type2"] = fp / (double)Math.Max(1, fp + tn);        // non-ground accepted
        R["total_error"] = (fn + fp) / Math.Max(1, nn);
        double po = (tp + tn) / nn, pe = ((tp + fn) * (double)(tp + fp) + (fp + tn) * (double)(fn + tn)) / (nn * nn);
        R["kappa"] = (po - pe) / (1 - pe);
        // against the DGM (independent of the classes): accepted points more than 0.5 m above the terrain,
        // rejected points within 0.15 m of it
        int acceptedHigh = 0, rejectedOnGround = 0, accepted = 0, rejected = 0;
        for (int k = 0; k < pts.Length; ++k) {
            double h = pc.P[k].z - dgm.Height(pc.P[k].x, pc.P[k].y);
            if (double.IsNaN(h)) continue;
            if (csf.IsGround[k]) { accepted++; if (h > 0.5) acceptedHigh++; } else { rejected++; if (Math.Abs(h) < 0.15) rejectedOnGround++; }
        }
        R["accepted"] = accepted; R["accepted_above_dgm_0_5"] = acceptedHigh;
        R["rejected"] = rejected; R["rejected_on_dgm_0_15"] = rejectedOnGround;
        R["check_s"] = Secs(sw);
    }
}

static class Ext
{
    public static bool IsFinite(this double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
}
