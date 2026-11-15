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
        if (args.Length >= 3 && args[0] == "refine") { refineMode = true; return SolidMain(args); }
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

    static bool refineMode;

    static void Solid(string input, int res, Dictionary<string, object> r)
    {
        if (refineMode) { Refine(input, res, r); return; }
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

    // refinement driven by the certificate: the default solidify without it (contour time), then with the certificate and
    // CertificateRefineLevels 0 (stage 1), 1, 2, 3
    static void Refine(string input, int res, Dictionary<string, object> r)
    {
        double cellOverride = 0;
        DMesh3 mesh = input.StartsWith("imp:") ? ImplicitMesh(input.Substring(4), res, out cellOverride)
            : input.StartsWith("mesh:") ? Procedural.Make(input.Substring(5)) : StandardMeshReader.ReadMesh(input);
        r["in_tris"] = mesh.TriangleCount;
        double diag = mesh.CachedBounds.DiagonalLength;
        DMesh3 L = ContouringLevelSet.CleanCopy(mesh);
        r["in_clean"] = L != null;
        if (L == null) return;
        int comps, lcomps;
        bool[] inL = ContouringLevelSet.Classify(L, out comps, out lcomps);
        var cc = new MeshConnectedComponents(L); cc.FindConnectedT();
        var genera = new List<double>();
        foreach (var comp in cc.Components)
            if (inL[comp.Indices[0]]) genera.Add(MeshTopologySummary.Compute(new DSubmesh3(L, comp.Indices).SubMesh).Genus);
        r["truth_sig"] = Sig(genera);
        if (Environment.GetEnvironmentVariable("SIL_BASE") == "1") {
            // baseline: of cubes of the solidify cell size centred at 2000 random points of L, how many meet a silhouette edge
            double h = cellOverride > 0 ? cellOverride : diag / res;
            var rnd = new Random(1); var sample = new List<ContouringUncertifiedCell>();
            var tris = L.TriangleIndices().ToArray();
            for (int i = 0; i < 2000; ++i) {
                int t = tris[rnd.Next(tris.Length)]; double u = rnd.NextDouble(), v = rnd.NextDouble(); if (u + v > 1) { u = 1 - u; v = 1 - v; }
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero; L.GetTriVertices(t, ref a, ref b, ref c);
                Vector3d q = a + u * (b - a) + v * (c - a);
                sample.Add(new ContouringUncertifiedCell() { Box = new AxisAlignedBox3d(q, h / 2) });
            }
            r["base_near_silhouette_of_2000"] = NearSilhouette(L, sample);
            return;
        }
        HashSet<Vector3i> coarse0 = null; Vector3d anchor = Vector3d.Zero; double cs = 0;
        foreach (int lv in new[] { -1, 0, 1, 2, 3 }) {
            var sw = Stopwatch.StartNew();
            var s = new MeshWindingNumberSolidify(mesh) { Adaptive = true, CellSize = cellOverride > 0 ? cellOverride : diag / res, CleanupResult = false, ReduceTolerance = 0,
                ComputeTopologyCertificate = lv >= 0, CertificateRefineLevels = Math.Max(lv, 0) };
            s.Compute();
            string p = lv < 0 ? "off_" : "k" + lv + "_";
            r[p + "ms"] = sw.ElapsedMilliseconds;
            r[p + "contour_ms"] = s.PhaseMilliseconds.TryGetValue("contour", out long c) ? c : -1;
            r[p + "tris"] = s.Result == null ? -1 : s.Result.TriangleCount;
            r[p + "refined_cells"] = s.RefinedCells; r[p + "factor"] = s.RefineFactor;
            r[p + "used_cell"] = s.UsedCellSize / (cellOverride > 0 ? cellOverride : diag / res);
            if (s.Result != null) r[p + "result_sig"] = Sig(MeshTopologySummary.Compute(s.Result).ComponentList.Select(x => x.Genus));
            if (lv < 0) continue;
            r[p + "status"] = s.TopologyCertificateStatus.ToString();
            r[p + "uncertified"] = s.UncertifiedCells.Count;
            r[p + "rounds"] = s.CertificateRefineRounds;
            foreach (var g in s.UncertifiedCells.GroupBy(x => x.Reason)) r[p + "n_" + g.Key] = g.Count();
            // the coarse cells (of the unrefined grid) that hold an uncertified cell: of those of stage 1, still; and new ones
            if (lv == 0) {
                coarse0 = new HashSet<Vector3i>();
                if (s.UncertifiedCells.Count > 0) { anchor = s.UncertifiedCells[0].Box.Min; cs = s.UncertifiedCells[0].Box.Width; }
            }
            var cur = new HashSet<Vector3i>();
            foreach (var u in s.UncertifiedCells) {
                Vector3d q = (u.Box.Center - anchor) / cs;
                cur.Add(new Vector3i((int)Math.Floor(q.x), (int)Math.Floor(q.y), (int)Math.Floor(q.z)));
            }
            r[p + "near_silhouette"] = NearSilhouette(L, s.UncertifiedCells);
            if (lv == 0) { coarse0 = cur; r["k0_coarse"] = cur.Count; continue; }
            r[p + "coarse_left"] = cur.Count(c => coarse0.Contains(c));
            r[p + "coarse_new"] = cur.Count(c => !coarse0.Contains(c));
        }
    }

    // how many of the uncertified cells meet (closed boxes) a silhouette edge of L: a mesh edge whose two triangle normals
    // have components of opposite sign along a grid axis (an axis line near it can cross L twice within one grid edge)
    static int NearSilhouette(DMesh3 L, List<ContouringUncertifiedCell> cells)
    {
        var boxes = new List<AxisAlignedBox3d>();
        foreach (int e in L.EdgeIndices()) {
            Index2i t = L.GetEdgeT(e);
            if (t.b < 0) continue;
            Vector3d n1 = L.GetTriNormal(t.a), n2 = L.GetTriNormal(t.b);
            bool sil = false;
            for (int a = 0; a < 3; ++a) if (n1[a] * n2[a] < 0) sil = true;
            if (!sil) continue;
            Index2i ev = L.GetEdgeV(e);
            var b = new AxisAlignedBox3d(L.GetVertex(ev.a)); b.Contain(L.GetVertex(ev.b));
            boxes.Add(b);
        }
        int n = 0;
        foreach (var c in cells) {
            bool hit = false;
            foreach (var b in boxes) if (b.Intersects(c.Box)) { hit = true; break; }
            if (hit) n++;
        }
        return n;
    }

    // imp:<shape>:<k>@<seed>: a feature-width family of benchmarks/ContouringTopology (feature width k cells of the solidify
    // grid, 2.2 / res), under a random rotation, meshed by marching cubes at 2.2 / 192 (a clean closed mesh, whose level set is
    // the certificate's exact truth); the solidify cell is 2.2 / res
    static DMesh3 ImplicitMesh(string spec, int res, out double cell)
    {
        int at = spec.IndexOf('@');
        int seed = at >= 0 ? int.Parse(spec.Substring(at + 1)) : 0;
        if (at >= 0) spec = spec.Substring(0, at);
        string[] parts = spec.Split(':');
        double k = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        cell = 2.2 / res;
        double w = k * cell;
        var rnd = new Random(seed);
        Matrix3d R = Rot(Vector3d.AxisX, rnd.NextDouble() * 6.28) * Rot(Vector3d.AxisY, rnd.NextDouble() * 6.28) * Rot(Vector3d.AxisZ, rnd.NextDouble() * 6.28);
        Func<Vector3d, double> f0;
        switch (parts[0]) {
            case "torus": f0 = p => { double a = Math.Sqrt(p.x * p.x + p.y * p.y) - 0.6; return Math.Sqrt(a * a + p.z * p.z) - w / 2; }; break;
            case "tunnel": f0 = p => Math.Max(p.Length - 0.8, w / 2 - Math.Sqrt(p.x * p.x + p.y * p.y)); break;
            case "gap": f0 = p => Math.Min((p - new Vector3d(0.45 + w / 2, 0, 0)).Length, (p + new Vector3d(0.45 + w / 2, 0, 0)).Length) - 0.45; break;
            case "rod": f0 = p => {
                double balls = Math.Min((p - new Vector3d(0.6, 0, 0)).Length, (p + new Vector3d(0.6, 0, 0)).Length) - 0.3;
                double x = Math.Max(Math.Abs(p.x) - 0.6, 0);
                return Math.Min(balls, Math.Sqrt(x * x + p.y * p.y + p.z * p.z) - w / 2); }; break;
            case "shell": f0 = p => Math.Max(p.Length - 0.8, 0.8 - w - p.Length); break;
            default: throw new ArgumentException(parts[0]);
        }
        var mc = new MarchingCubes() {
            Implicit = new Fn() { F = p => f0(R * p) }, IsoValue = 0, CubeSize = 2.2 / 192,
            Bounds = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -1.1) + 0.0013 * Vector3d.One, new Vector3d(1.1, 1.1, 1.1)),
            RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 10
        };
        mc.Generate();
        DMesh3 m = mc.Mesh;
        MeshEditor.RemoveUnusedVertices(m);
        return m;
    }
    static Matrix3d Rot(Vector3d axis, double radians) => Matrix3d.AxisAngleD(axis, radians * 180 / Math.PI);
    class Fn : ImplicitFunction3d { public Func<Vector3d, double> F; public double Value(ref Vector3d p) { return F(p); } }

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
