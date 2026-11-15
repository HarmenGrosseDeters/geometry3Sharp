// Stage 1 spike of the contouring topology certificate (docs/design/CONTOURING_TOPOLOGY.md, sections 4.4 and 7.4):
// the exact level-set tests of Theorem 3 on the leaf grid of MeshWindingNumberSolidify (adaptive), measured on the
// lever-6 set: how many surface cells they refuse, why, and what they cost against the dual contouring.
// Measurement only.
//
//   dotnet run -c Release --project benchmarks/ContouringCertificate -- spike <out.jsonl> <input>... [-- res1,res2]
//   input = mesh:<procedural CAD solid> | mesh:all | <mesh file>
// One JSON line per input and resolution; a rerun skips the pairs already in the file.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "predicates") { PredicateTiming.Run(); return 0; }
        if (args.Length >= 3 && args[0] == "solid") return SolidMain(args);
        if (args.Length >= 3 && args[0] == "clusters") { clusterMode = true; return SolidMain(args); }
        if (args.Length < 3 || args[0] != "spike") { Console.Error.WriteLine("usage: ContouringCertificate spike <out.jsonl> <input>... [-- res,...]"); return 2; }
        string outFile = args[1];
        var inputs = new List<string>();
        int[] resolutions = { 64, 128 };
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--") { resolutions = args[i + 1].Split(',').Select(int.Parse).ToArray(); break; }
            if (args[i] == "mesh:all") inputs.AddRange(Procedural.Names.Select(n => "mesh:" + n));
            else inputs.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile))
                using (var doc = JsonDocument.Parse(line))
                    done.Add(doc.RootElement.GetProperty("input").GetString() + "|" + doc.RootElement.GetProperty("res").GetInt32());
        Spike("mesh:box", 16, new Dictionary<string, object>());   // warm-up (JIT), not recorded
        foreach (string input in inputs) {
            string name = input.StartsWith("mesh:") ? input : Path.GetFileName(input);
            foreach (int res in resolutions) {
                if (done.Contains(name + "|" + res)) continue;
                var r = new Dictionary<string, object>();
                r["input"] = name; r["res"] = res;
                try { Spike(input, res, r); } catch (Exception e) { r["status"] = "exception"; r["error"] = e.GetType().Name + ": " + e.Message; }
                string json = JsonSerializer.Serialize(r);
                File.AppendAllText(outFile, json + "\n");
                Console.WriteLine(json);
            }
        }
        return 0;
    }

    // the wired certificate: MeshWindingNumberSolidify (adaptive, default refinement, cleanup and reduction off) with
    // ComputeTopologyCertificate, against the exact truth of clean inputs (the level set's components and their genus).
    //   solid <out.jsonl> <input>... [-- res,...]
    static int SolidMain(string[] args)
    {
        string outFile = args[1];
        var inputs = new List<string>();
        int[] resolutions = { 64, 128 };
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--") { resolutions = args[i + 1].Split(',').Select(int.Parse).ToArray(); break; }
            if (args[i] == "mesh:all") inputs.AddRange(Procedural.Names.Select(n => "mesh:" + n));
            else inputs.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile))
                using (var doc = JsonDocument.Parse(line))
                    done.Add(doc.RootElement.GetProperty("input").GetString() + "|" + doc.RootElement.GetProperty("res").GetInt32());
        Solid("mesh:box", 16, new Dictionary<string, object>());   // warm-up
        foreach (string input in inputs) {
            string name = input.StartsWith("mesh:") ? input : Path.GetFileName(input);
            foreach (int res in resolutions) {
                if (done.Contains(name + "|" + res)) continue;
                var r = new Dictionary<string, object>();
                r["input"] = name; r["res"] = res;
                try { Solid(input, res, r); } catch (Exception e) { r["status"] = "exception"; r["error"] = e.GetType().Name + ": " + e.Message; }
                string json = JsonSerializer.Serialize(r);
                File.AppendAllText(outFile, json + "\n");
                Console.WriteLine(json);
            }
        }
        return 0;
    }

    static bool clusterMode;

    static void Solid(string input, int res, Dictionary<string, object> r)
    {
        if (clusterMode) { Clusters(input, res, r); return; }
        DMesh3 mesh = input.StartsWith("mesh:") ? Procedural.Make(input.Substring(5)) : StandardMeshReader.ReadMesh(input);
        r["in_tris"] = mesh.TriangleCount;
        double diag = mesh.CachedBounds.DiagonalLength;
        DMesh3 L = ContouringLevelSet.CleanCopy(mesh);
        r["in_clean"] = L != null;
        if (L != null) {
            int comps, lcomps;
            bool[] inL = ContouringLevelSet.Classify(L, out comps, out lcomps);
            var cc = new MeshConnectedComponents(L); cc.FindConnectedT();
            var genera = new List<double>();
            foreach (var comp in cc.Components)
                if (inL[comp.Indices[0]]) genera.Add(MeshTopologySummary.Compute(new DSubmesh3(L, comp.Indices).SubMesh).Genus);
            r["truth_sig"] = Sig(genera);
        }
        foreach (bool cert in new[] { false, true }) {
            var sw = Stopwatch.StartNew();
            var s = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = diag / res, CleanupResult = false, ReduceTolerance = 0, ComputeTopologyCertificate = cert };
            s.Compute();
            string p = cert ? "on_" : "off_";
            r[p + "ms"] = sw.ElapsedMilliseconds;
            if (!cert) { r["contour_ms"] = s.PhaseMilliseconds.TryGetValue("contour", out long c) ? c : -1; continue; }
            r["certificate_ms"] = s.PhaseMilliseconds.TryGetValue("contour.certificate", out long cm) ? cm : -1;
            foreach (var kv in s.PhaseMilliseconds) if (kv.Key.StartsWith("contour.certificate.")) r[kv.Key.Substring(8)] = kv.Value;
            r["status"] = s.TopologyCertificateStatus.ToString();
            r["certified"] = s.TopologyCertified;
            r["uncertified_cells"] = s.UncertifiedCells.Count;
            foreach (var g in s.UncertifiedCells.GroupBy(x => x.Reason)) r["n_" + g.Key] = g.Count();
            r["refined_cells"] = s.RefinedCells; r["refine_factor"] = s.RefineFactor;
            if (s.Result != null) r["result_sig"] = Sig(MeshTopologySummary.Compute(s.Result).ComponentList.Select(c => c.Genus));
        }
    }

    // Upper bound of merging uncertified cells into box blocks (design note section 7.10): the uncertified cells of the
    // default solidify with the certificate, clustered (closed boxes that touch), each cluster replaced by its bounding box,
    // boxes that touch merged again until none touch. Every face and edge on a final box's boundary then lies on a cell
    // outside all boxes, which the certificate certified, so it passes; whether the parts of L and of the contour in the
    // box are disks is not tested here (an upper bound). Per cap (largest box side in cells of the solidify's cell size):
    // the uncertified cells in boxes within the cap, and whether the run would be certified.
    static void Clusters(string input, int res, Dictionary<string, object> r)
    {
        DMesh3 mesh = input.StartsWith("mesh:") ? Procedural.Make(input.Substring(5)) : StandardMeshReader.ReadMesh(input);
        r["in_tris"] = mesh.TriangleCount;
        double diag = mesh.CachedBounds.DiagonalLength;
        var s = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = diag / res, CleanupResult = false, ReduceTolerance = 0, ComputeTopologyCertificate = true };
        s.Compute();
        r["status"] = s.TopologyCertificateStatus.ToString();
        r["refine_factor"] = s.RefineFactor;
        var cells = s.UncertifiedCells;
        r["uncertified"] = cells.Count;
        if (cells.Count == 0) return;
        double h = double.MaxValue; Vector3d anchor = cells[0].Box.Min;
        foreach (var c in cells) { h = Math.Min(h, c.Box.Width); anchor = new Vector3d(Math.Min(anchor.x, c.Box.Min.x), Math.Min(anchor.y, c.Box.Min.y), Math.Min(anchor.z, c.Box.Min.z)); }
        int n = cells.Count;
        var lo = new Vector3i[n]; var hi = new Vector3i[n];
        for (int i = 0; i < n; ++i) {
            Vector3d a = (cells[i].Box.Min - anchor) / h, b = (cells[i].Box.Max - anchor) / h;
            lo[i] = new Vector3i((int)Math.Round(a.x), (int)Math.Round(a.y), (int)Math.Round(a.z));
            hi[i] = new Vector3i((int)Math.Round(b.x), (int)Math.Round(b.y), (int)Math.Round(b.z));
        }
        bool touch(Vector3i l1, Vector3i h1, Vector3i l2, Vector3i h2) =>
            l1.x <= h2.x && l2.x <= h1.x && l1.y <= h2.y && l2.y <= h1.y && l1.z <= h2.z && l2.z <= h1.z;
        // boxes: start with one per cell, merge touching ones into their hull until none touch
        var bl = new List<Vector3i>(lo); var bh = new List<Vector3i>(hi); var cnt = Enumerable.Repeat(1, n).ToList();
        bool changed = true;
        while (changed) {
            changed = false;
            for (int i = 0; i < bl.Count && !changed; ++i)
                for (int j = i + 1; j < bl.Count; ++j)
                    if (touch(bl[i], bh[i], bl[j], bh[j])) {
                        bl[i] = new Vector3i(Math.Min(bl[i].x, bl[j].x), Math.Min(bl[i].y, bl[j].y), Math.Min(bl[i].z, bl[j].z));
                        bh[i] = new Vector3i(Math.Max(bh[i].x, bh[j].x), Math.Max(bh[i].y, bh[j].y), Math.Max(bh[i].z, bh[j].z));
                        cnt[i] += cnt[j];
                        bl.RemoveAt(j); bh.RemoveAt(j); cnt.RemoveAt(j);
                        changed = true; break;
                    }
            // (restart the scan after each merge: a grown box may touch earlier ones)
        }
        double unit = h * s.RefineFactor;   // the solidify's cell size, in fine cells: RefineFactor
        r["boxes"] = bl.Count;
        foreach (int cap in new[] { 2, 3, 4, 6, 8 }) {
            int inCap = 0; bool all = true;
            for (int i = 0; i < bl.Count; ++i) {
                Vector3i d = bh[i] - bl[i];
                double side = Math.Max(d.x, Math.Max(d.y, d.z)) * h / unit;
                if (side <= cap + 1e-9) inCap += cnt[i]; else all = false;
            }
            r["cells_cap" + cap] = inCap;
            r["run_cap" + cap] = all;
        }
        var sides = new List<double>();
        for (int i = 0; i < bl.Count; ++i) { Vector3i d = bh[i] - bl[i]; sides.Add(Math.Max(d.x, Math.Max(d.y, d.z)) * h / unit); }
        r["max_side"] = sides.Max();
        // general blocks: clusters of the uncertified cells (as unit cubes of the finest cell, 26-connected), and the
        // Euler characteristic of each cluster's union (1 for a ball; 0 for a solid torus around a ridge loop)
        var cubes = new Dictionary<Vector3i, int>();     // unit cube -> reported cell
        for (int i = 0; i < n; ++i)
            for (int z = lo[i].z; z < hi[i].z; ++z) for (int y = lo[i].y; y < hi[i].y; ++y) for (int x = lo[i].x; x < hi[i].x; ++x)
                cubes[new Vector3i(x, y, z)] = i;
        var comp = new Dictionary<Vector3i, int>(); int nc = 0;
        foreach (var start in cubes.Keys) {
            if (comp.ContainsKey(start)) continue;
            var stack = new Stack<Vector3i>(); stack.Push(start); comp[start] = nc;
            while (stack.Count > 0) {
                var c = stack.Pop();
                for (int dz = -1; dz <= 1; ++dz) for (int dy = -1; dy <= 1; ++dy) for (int dx = -1; dx <= 1; ++dx) {
                    var q = new Vector3i(c.x + dx, c.y + dy, c.z + dz);
                    if (cubes.ContainsKey(q) && !comp.ContainsKey(q)) { comp[q] = nc; stack.Push(q); }
                }
            }
            nc++;
        }
        // chi = V - E + F - C per component (elements keyed by doubled coordinates of their centers)
        var elems = new HashSet<(int, Vector3i)>(); var chi = new long[nc]; var cellsOf = new List<HashSet<int>>();
        for (int k = 0; k < nc; ++k) cellsOf.Add(new HashSet<int>());
        foreach (var kv in comp) {
            Vector3i c = kv.Key; int k = kv.Value; cellsOf[k].Add(cubes[c]);
            for (int dz = 0; dz <= 2; ++dz) for (int dy = 0; dy <= 2; ++dy) for (int dx = 0; dx <= 2; ++dx) {
                var key = new Vector3i(2 * c.x + dx, 2 * c.y + dy, 2 * c.z + dz);
                int odd = (dx & 1) + (dy & 1) + (dz & 1);     // 0 vertex, 1 edge, 2 face, 3 cube
                if (elems.Add((k, key))) chi[k] += (odd % 2 == 0) ? 1 : -1;
            }
        }
        int ballCells = 0, ballClusters = 0, maxCluster = 0;
        for (int k = 0; k < nc; ++k) {
            maxCluster = Math.Max(maxCluster, cellsOf[k].Count);
            if (chi[k] == 1) { ballCells += cellsOf[k].Count; ballClusters++; }
        }
        r["clusters"] = nc; r["clusters_chi1"] = ballClusters; r["cells_chi1"] = ballCells; r["max_cluster_cells"] = maxCluster;
        r["run_chi1"] = ballClusters == nc;
    }

    // vertices with identical coordinates merged (exact: the geometry and the winding number do not change)
    static DMesh3 ExactWeld(DMesh3 m)
    {
        var map = new Dictionary<Vector3d, int>();
        var outm = new DMesh3();
        var vmap = new Dictionary<int, int>();
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            if (!map.TryGetValue(p, out int id)) { id = outm.AppendVertex(p); map[p] = id; }
            vmap[v] = id;
        }
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            int a = vmap[tv.a], b = vmap[tv.b], c = vmap[tv.c];
            if (a == b || b == c || a == c || outm.AppendTriangle(a, b, c) < 0) return null;   // not manifold after the weld
        }
        return outm;
    }

    static string Clean(DMesh3 m)
    {
        if (m == null) return "weld";
        if (!m.IsClosed()) return "open";
        foreach (int v in m.VertexIndices()) if (m.IsBowtieVertex(v)) return "bowtie";
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            var T = new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c));
            if (ExactTriangleIntersection.IsDegenerate(ref T)) return "degenerate";
        }
        if (MeshRepairMetrics.InconsistentlyOrientedEdges(m) > 0) return "orientation";
        if (MeshRepairMetrics.HasSelfIntersections(m)) return "selfintersecting";
        return "clean";
    }

    class UniformGrid : CertificateGrid
    {
        public Vector3d Origin; public double H; public WindingNumberOctree Octree; public double Iso;
        public override double Coord(int axis, int i) { return Origin[axis] + H * i; }
        public override int BinSize { get { return 1; } }
        public override void CellFaces(Vector3i min, int size, List<GridFace> faces)
        {
            for (int a = 0; a < 3; ++a)
                for (int s = 0; s < 2; ++s) { Vector3i m = min; m[a] += s; faces.Add(new GridFace(m, a, 1)); }
        }
        public override void FaceEdges(GridFace f, List<GridSegment> edges)
        {
            for (int k = 0; k < 4; ++k) {
                Vector3i c0 = f.Corner(k), c1 = f.Corner((k + 1) % 4);
                Vector3i d = c1 - c0;
                int axis = d.x != 0 ? 0 : d.y != 0 ? 1 : 2;
                edges.Add(new GridSegment(d[axis] > 0 ? c0 : c1, axis, 1));
            }
        }
        readonly Dictionary<Vector3i, bool> signs = new Dictionary<Vector3i, bool>();
        public override bool NodeInside(Vector3i n)
        {
            bool s;
            if (!signs.TryGetValue(n, out s)) {
                // as DualContouring: the float value against the iso value
                float v = (float)MathUtil.Clamp(Octree.NodeValue(n), -float.MaxValue / 2, float.MaxValue / 2);
                s = v > Iso;
                signs[n] = s;
            }
            return s;
        }
        // the contour's crossed edges (as DualContouring: the sign-changing edges of its cells; no other edge is crossed)
        public HashSet<GridSegment> CrossedEdges = new HashSet<GridSegment>();
        public override bool Crossed(GridSegment e) { return CrossedEdges.Contains(e); }
    }

    static void Spike(string input, int res, Dictionary<string, object> r)
    {
        DMesh3 mesh = input.StartsWith("mesh:") ? Procedural.Make(input.Substring(5)) : StandardMeshReader.ReadMesh(input);
        if (mesh == null || mesh.TriangleCount == 0) { r["status"] = "load_failed"; return; }
        r["in_tris"] = mesh.TriangleCount;
        double diag = mesh.CachedBounds.DiagonalLength;

        // the contouring as MeshWindingNumberSolidify runs it by default (adaptive), without cleanup and reduction: its phases
        var sw = Stopwatch.StartNew();
        // (CERT_SKIP_DC=1: the certificate only, for profiling)
        if (Environment.GetEnvironmentVariable("CERT_SKIP_DC") != "1") {
        var solid = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = diag / res, CleanupResult = false, ReduceTolerance = 0 };
        solid.Compute();
        r["dc_total_ms"] = sw.ElapsedMilliseconds;
        foreach (var kv in solid.PhaseMilliseconds) r["dc_" + kv.Key] = kv.Value;
        r["dc_refined_cells"] = solid.RefinedCells; r["dc_refine_factor"] = solid.RefineFactor;
        if (solid.Result != null) {
            var top = MeshTopologySummary.Compute(solid.Result);
            r["dc_sig"] = Sig(top.ComponentList.Select(c => c.Genus));
        }
        }

        // the same frame and leaf grid (WindingContouring.build_octree)
        DMesh3 scaled = new DMesh3(mesh);
        double s = MeshRepairMetrics.unit_scale_for_mesh(mesh, true, 0, 0);
        if (s != 1) MeshRepairMetrics.ScaleVertices(scaled, s);
        double cell = diag / res * s;
        var wc = new WindingContouring(new DMesh3[] { scaled }, null, 1.5);
        string clean = Clean(ExactWeld(wc.Meshes[0]));
        DMesh3 L = ContouringLevelSet.CleanCopy(wc.Meshes[0]);
        if ((L != null) != (clean == "clean")) throw new Exception("CleanCopy disagrees with the spike's check");
        r["in_clean"] = clean;
        if (clean != "clean") { r["status"] = "ok"; return; }

        sw.Restart();
        int comps, lcomps;
        bool[] inL = ContouringLevelSet.Classify(L, out comps, out lcomps);
        r["classify_ms"] = sw.ElapsedMilliseconds;
        if (inL == null) { r["status"] = "unclassified"; return; }
        r["components"] = comps; r["levelset_components"] = lcomps;
        {
            var cc = new MeshConnectedComponents(L); cc.FindConnectedT();
            var genera = new List<double>();
            foreach (var comp in cc.Components)
                if (inL[comp.Indices[0]]) genera.Add(MeshTopologySummary.Compute(new DSubmesh3(L, comp.Indices).SubMesh).Genus);
            r["truth_sig"] = Sig(genera);
        }

        sw.Restart();
        var octree = new WindingNumberOctree(wc.Meshes, wc.Spatials, null) {
            TargetLeafCellSize = cell, WindingIso = 0.5, GridOffset = new Vector3d(0.1371, 0.2713, 0.3891)
        };
        octree.Build();
        r["octree_ms"] = sw.ElapsedMilliseconds;
        var grid = new UniformGrid() { Origin = octree.Origin, H = octree.LeafCellSize, Octree = octree, Iso = 0.5 };

        // the cells: those L's triangles overlap, and those of the contouring (candidates and the cells around their
        // sign-changing edges)
        sw.Restart();
        var cert = new ContouringTopologyCertificate(L, inL, grid);
        long binMs = sw.ElapsedMilliseconds;
        var cells = new HashSet<Vector3i>(cert.Bins);
        sw.Restart();
        var dcCells = new HashSet<Vector3i>(octree.SurfaceCells);
        foreach (Vector3i c in octree.SurfaceCells)
            for (int a = 0; a < 3; ++a)
                for (int du = 0; du < 2; ++du)
                    for (int dv = 0; dv < 2; ++dv) {
                        Vector3i n0 = c; n0[(a + 1) % 3] += du; n0[(a + 2) % 3] += dv;
                        Vector3i n1 = n0; n1[a] += 1;
                        if (grid.NodeInside(n0) == grid.NodeInside(n1)) continue;
                        grid.CrossedEdges.Add(new GridSegment(n0, a, 1));
                        for (int q = 0; q < 4; ++q) {
                            Vector3i nc = n0; nc[(a + 1) % 3] -= (q == 0 || q == 3) ? 1 : 0; nc[(a + 2) % 3] -= (q < 2) ? 1 : 0;
                            dcCells.Add(nc);
                        }
                    }
        long signMs = sw.ElapsedMilliseconds;
        cells.UnionWith(dcCells);
        sw.Restart();
        var counts = new Dictionary<string, int>();
        int meetsCount = 0, refusedMeets = 0, tested = 0;
        var cellTimer = new Stopwatch();
        long emptyTicks = 0, meetTicks = 0;
        foreach (Vector3i c in cells) {
            bool meets;
            long t0 = Stopwatch.GetTimestamp();
            var reason = cert.Cell(c, 1, dcCells.Contains(c), out meets);
            // (a refused cell counts as a surface cell: it is reported)
            if (reason != ContouringCertificateReason.None) meets = true;
            long dt = Stopwatch.GetTimestamp() - t0;
            if (meets) meetTicks += dt; else emptyTicks += dt;
            tested++;
            if (meets) meetsCount++;
            string key = reason.ToString();
            counts[key] = (counts.TryGetValue(key, out int x) ? x : 0) + 1;
            if (meets && reason != ContouringCertificateReason.None) refusedMeets++;
        }
        r["cert_ms"] = sw.ElapsedMilliseconds + binMs;
        r["bin_ms"] = binMs;
        r["empty_cells_ms"] = emptyTicks * 1000 / Stopwatch.Frequency;
        r["meet_cells_ms"] = meetTicks * 1000 / Stopwatch.Frequency;
                r["segtri"] = cert.SegmentTriangleTests; r["segface"] = cert.SegmentFaceTests;
        r["sign_ms"] = signMs;
        r["cells_tested"] = tested; r["cells_meet_L"] = meetsCount; r["dc_cells"] = dcCells.Count;
        r["refused_meet_L"] = refusedMeets;
        foreach (var kv in counts) r["n_" + kv.Key] = kv.Value;
        r["status"] = "ok";
    }

    static string Sig(IEnumerable<double> genera)
    {
        var g = genera.OrderBy(x => x).ToList();
        return "c=" + g.Count + ";g=" + string.Join(",", g.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}

static class PredicateTiming
{
    // ns per call of the two predicates of the certificate on random non-degenerate inputs (the filter path)
    public static void Run()
    {
        var rnd = new Random(1);
        int n = 1 << 20;
        var pts = new Vector3d[n + 8];
        for (int i = 0; i < pts.Length; ++i) pts[i] = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
        int acc = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int rep = 0; rep < 4; ++rep)
            for (int i = 0; i < n; ++i) acc += ExactPredicates.Orient3d(pts[i], pts[i + 1], pts[i + 2], pts[i + 3]);
        double o3 = sw.Elapsed.TotalMilliseconds * 1e6 / (4.0 * n);
        sw.Restart();
        for (int rep = 0; rep < 4; ++rep)
            for (int i = 0; i < n; ++i) acc += ExactPredicates.Orient2d(new Vector2d(pts[i].x, pts[i].y), new Vector2d(pts[i + 1].x, pts[i + 1].y), new Vector2d(pts[i + 2].x, pts[i + 2].y));
        double o2 = sw.Elapsed.TotalMilliseconds * 1e6 / (4.0 * n);
        Console.WriteLine($"{{\"orient3d_ns\":{o3:F1},\"orient2d_ns\":{o2:F1},\"acc\":{acc}}}");
    }
}
