// Driver of the varifold feature / curvature study (docs/research/VARIFOLD_FEATURES_CURVATURE_2026-11.md). No library change.
//
//   SharpFeatureProbe soup crease <out.jsonl> [tris=50000] [shapes=box,slot,...] [breaks=clean,soup,...] [c=2]
//   SharpFeatureProbe soup curv   <out.jsonl> [tris=50000] [shapes=fillet,rbox] [breaks=...] [eps=3,5]
//   SharpFeatureProbe soup cloud  <out.jsonl> [tris=50000] [shapes=fillet,rbox] [noise=0,0.05,0.2] [eps=3,5]
//   SharpFeatureProbe soup time   <out.jsonl> [sizes=10000,50000,100000,200000]
//   SharpFeatureProbe soup check  <out.jsonl>                (determinism: runs, threads; Similarity.All() list)
//
// Shapes (SoupShapes.cs): box, slot, vnotch (creases), fillet (creases on lines and arcs, quarter cylinders), rbox (all edges
// rounded: no crease, cylinders and sphere octants). h = the generator's spacing (0.25 / m, so all straight borders conform);
// the methods only see the broken soup and take their own h from its median edge length.
// One JSON row per (shape, breakage, method). Crease rows: precision, recall, F1 over soup edges (ground truth: both ends and
// the midpoint of the edge's clean copy within 0.05 h of a true crease), the dihedral error over the true positives (degrees).
// Curvature rows: e_K = max(|k1 - K1|, |k2 - K2|) R and e_H = |H - (K1 + K2)| R (R = 0.25, the fillet radius: 1 / R = 4),
// median and p90 per region (flat, cylinder, sphere), and the fraction of queries with a value.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

static class SoupRun
{
    const double R = 0.25;

    public static int Run(string[] args)
    {
        string mode = args[1], outFile = args.Length > 2 ? args[2] : "soup.jsonl";
        var opt = new Dictionary<string, string>();
        for (int i = 3; i < args.Length; ++i) { int e = args[i].IndexOf('='); if (e > 0) opt[args[i].Substring(0, e)] = args[i].Substring(e + 1); }
        string Get(string k, string d) => opt.TryGetValue(k, out var v) ? v : d;
        double[] D(string k, string d) => Get(k, d).Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (opt.TryGetValue("threads", out var th)) SoupFeatures.Threads = int.Parse(th);
        if (opt.TryGetValue("lift", out var lf)) PlaneLift = double.Parse(lf, CultureInfo.InvariantCulture);
        int tris = int.Parse(Get("tris", "50000"));
        using var w = new StreamWriter(outFile, true);
        switch (mode) {
            case "crease":
                foreach (string shape in Get("shapes", "box,slot,vnotch,fillet,rbox").Split(','))
                    foreach (string br in Get("breaks", "clean;soup;nonconf;jitter:0.02;jitter:0.1;holes:0.1;dup:0.2;gap:0.3;gap:1;flip:0.2;mix:0.25;mix:0.5;mix:1").Split(';'))
                        Crease(w, shape, br, tris, D("c", "2"), D("tol", "0.3"));
                break;
            case "curv":
                foreach (string shape in Get("shapes", "fillet,rbox").Split(','))
                    foreach (string br in Get("breaks", "clean;soup;nonconf;jitter:0.02;jitter:0.1;holes:0.1;dup:0.2;gap:0.3;flip:0.2;mix:0.5;mix:1").Split(';'))
                        Curv(w, shape, br, tris, D("eps", "3,5"));
                break;
            case "cloud":
                foreach (string shape in Get("shapes", "fillet,rbox").Split(','))
                    foreach (double noise in D("noise", "0,0.05,0.2"))
                        Cloud(w, shape, noise, tris, D("eps", "3,5"));
                break;
            case "time": Time(w, D("sizes", "10000,50000,100000,200000")); break;
            case "check": Check(w, int.Parse(Get("tris", "20000"))); break;
            case "wrapbound": WrapBound(w, Get("shapes", "box,slot,vnotch,fillet").Split(','), tris); break;
            case "soupwrap":
                foreach (string shape in Get("shapes", "slot,vnotch,box,fillet").Split(','))
                    foreach (string br in Get("breaks", "clean;soup;nonconf;jitter:0.1;dup:0.2;gap:0.3;gap:1;flip:0.2;mixnh:0.25;mixnh:0.5;mixnh:1;holes:0.1").Split(';'))
                        SoupWrap(w, shape, br, tris, D("radius", "3.5"), D("planes", "1.5"));
                break;
            case "wrapbreak": WrapBreaks(w, Get("shapes", "slot"), tris, Get("breaks", "clean;soup;nonconf;jitter:0.025;holes:0.025;dup:0.05;gap:0.15;flip:0.1;mix:0.5").Split(';')); break;
            default: Console.Error.WriteLine("unknown soup mode " + mode); return 2;
        }
        return 0;
    }

    static double Spacing(string shape, int tris)
    {
        double area = SoupShapes.Area(shape), h = Math.Sqrt(2 * area / tris);
        int m = Math.Max(1, (int)Math.Round(0.25 / h));
        return 0.25 / m;
    }

    sealed class Case
    {
        public string Shape, Break; public double H; public List<Patch> Patches; public Soup Soup;
        public List<(Vector3d a, Vector3d b, double dih)> Creases; public List<(Vector3d a, Vector3d b)> Jumps;
    }

    static Case Build(string shape, string br, int tris, int seed = 1)
    {
        var c = new Case { Shape = shape, Break = br, H = Spacing(shape, tris), Patches = SoupShapes.Make(shape) };
        Soup clean = SoupShapes.Tessellate(c.Patches, c.H, null);
        (c.Creases, c.Jumps) = SoupShapes.Truth(c.Patches, clean, c.H);
        Soup basis = SoupBreak.NonConforming(br) ? SoupShapes.Tessellate(c.Patches, c.H, SoupBreak.Mult) : clean;
        c.Soup = SoupBreak.Apply(basis, br, c.H, seed);
        return c;
    }

    // ---- segment lookup with a value ----
    sealed class SegLookup
    {
        readonly Dictionary<(long, long, long), List<int>> cells = new Dictionary<(long, long, long), List<int>>();
        readonly List<(Vector3d a, Vector3d b, double v)> segs; readonly double cell;
        public SegLookup(List<(Vector3d a, Vector3d b, double v)> s, double cellSize)
        {
            segs = s; cell = cellSize;
            for (int i = 0; i < s.Count; ++i) {
                int n = Math.Max(1, (int)Math.Ceiling(s[i].a.Distance(s[i].b) / (0.5 * cell)));
                for (int k = 0; k <= n; ++k) {
                    var key = K(Vector3d.Lerp(s[i].a, s[i].b, (double)k / n));
                    if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>();
                    if (l.Count == 0 || l[l.Count - 1] != i) l.Add(i);
                }
            }
        }
        (long, long, long) K(Vector3d p) => ((long)Math.Floor(p.x / cell), (long)Math.Floor(p.y / cell), (long)Math.Floor(p.z / cell));
        /// <summary> the distance to the nearest segment within one cell, and its value (or +inf) </summary>
        public (double d, double v) Nearest(Vector3d p)
        {
            var (x, y, z) = K(p);
            double best = double.PositiveInfinity, val = 0;
            for (long i = -1; i <= 1; ++i) for (long j = -1; j <= 1; ++j) for (long k = -1; k <= 1; ++k)
                if (cells.TryGetValue((x + i, y + j, z + k), out var l))
                    foreach (int s in l) { double d = Math.Sqrt(new Segment3d(segs[s].a, segs[s].b).DistanceSquared(p)); if (d < best) { best = d; val = segs[s].v; } }
            return (best, val);
        }
    }

    // ---- creases ----

    static void Crease(StreamWriter w, string shape, string br, int tris, double[] cs, double[] tols = null)
    {
        var c = Build(shape, br, tris);
        Soup s = c.Soup;
        var gt = new SegLookup(c.Creases, c.H);
        int ne = 3 * s.Count;
        var truth = new bool[ne]; var gdih = new double[ne];
        double tolC = 0.05 * c.H;
        for (int t = 0; t < s.Count; ++t)
            for (int k = 0; k < 3; ++k) {
                Vector3d p = s.Clean[3 * t + k], q = s.Clean[3 * t + (k + 1) % 3];
                var (dm, v) = gt.Nearest(0.5 * (p + q));
                truth[3 * t + k] = dm <= tolC && gt.Nearest(p).d <= tolC && gt.Nearest(q).d <= tolC;
                gdih[3 * t + k] = v;
            }
        double hm = SoupFeatures.MedianEdge(s);
        var methods = new List<(string, Func<SoupFeatures.CreaseResult>)> {
            ("weld_exact", () => SoupFeatures.WeldDihedral(s, 1e-6 * hm)),
            ("weld_0.1h", () => SoupFeatures.WeldDihedral(s, 0.1 * hm)),
            ("weld_0.3h", () => SoupFeatures.WeldDihedral(s, 0.3 * hm)),
        };
        foreach (double cc in cs) {
            foreach (double tl in tols ?? new[] { 0.3 })
                methods.Add(("varifold_c" + cc.ToString(CultureInfo.InvariantCulture) + (tl == 0.3 ? "" : "_tol" + tl.ToString(CultureInfo.InvariantCulture)),
                    () => SoupFeatures.VarifoldCreases(s, hm, cc, tolH: tl)));
            methods.Add(("pca_c" + cc.ToString(CultureInfo.InvariantCulture), () => SoupFeatures.PcaCreases(s, hm, cc)));
        }
        foreach (var (name, f) in methods) {
            var r = f();
            bool hasDih = !name.StartsWith("pca");
            long tp = 0, fp = 0, fn = 0, fpNear = 0, fpFar = 0; var err = new List<double>();
            for (int e = 0; e < ne; ++e) {
                if (r.Flag[e] && truth[e]) { tp++; err.Add(Math.Abs(r.Dih[e] - gdih[e])); }
                else if (r.Flag[e]) {
                    fp++;
                    // where the false positives are: within 1.5 h of a true crease (a neighbour of it) or farther
                    int t = e / 3, k = e % 3;
                    if (gt.Nearest(0.5 * (s.Clean[3 * t + k] + s.Clean[3 * t + (k + 1) % 3])).d <= 1.5 * c.H) fpNear++; else fpFar++;
                    if (Environment.GetEnvironmentVariable("SOUP_DEBUG") == name && fp <= 20)
                        Console.Error.WriteLine($"FP {name} {s.Clean[3 * t + k]} -> {s.Clean[3 * t + (k + 1) % 3]} dih {r.Dih[e]:F1} d_mid {gt.Nearest(0.5 * (s.Clean[3 * t + k] + s.Clean[3 * t + (k + 1) % 3])).d / c.H:F2} h");
                }
                else if (truth[e]) fn++;
            }
            double prec = tp + fp == 0 ? 0 : (double)tp / (tp + fp), rec = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            var row = new Dictionary<string, object> {
                ["kind"] = "crease", ["shape"] = shape, ["break"] = br, ["method"] = name, ["tris"] = s.Count, ["h"] = c.H, ["h_median"] = hm,
                ["true_edges"] = tp + fn, ["tp"] = tp, ["fp"] = fp, ["fn"] = fn, ["fp_near"] = fpNear, ["fp_far"] = fpFar, ["precision"] = prec, ["recall"] = rec,
                ["f1"] = prec + rec == 0 ? 0 : 2 * prec * rec / (prec + rec),
                ["dih_err_med"] = hasDih ? Pct(err, 0.5) : -1, ["dih_err_p90"] = hasDih ? Pct(err, 0.9) : -1, ["seconds"] = r.Seconds
            };
            Emit(w, row);
        }
    }

    static double Pct(List<double> v, double q)
    {
        if (v.Count == 0) return double.NaN;
        var a = v.ToArray(); Array.Sort(a);
        return a[Math.Min(a.Length - 1, (int)(q * a.Length))];
    }

    static void Emit(StreamWriter w, Dictionary<string, object> row)
    {
        string line = JsonSerializer.Serialize(row, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        w.WriteLine(line); w.Flush(); Console.WriteLine(line);
    }

    // ---- curvature ----

    static int[] Queries(Case c, double dex, int max, int seed = 3)
    {
        var jumps = new SegLookup(c.Jumps.Select(j => (j.a, j.b, 0.0)).ToList(), Math.Max(dex, c.H));
        var cand = new List<int>();
        for (int i = 0; i < c.Soup.V.Count; ++i) if (jumps.Nearest(c.Soup.Clean[i]).d >= dex) cand.Add(i);
        var rnd = new Random(seed);
        for (int i = cand.Count - 1; i > 0; --i) { int j = rnd.Next(i + 1); var t = cand[i]; cand[i] = cand[j]; cand[j] = t; }
        return cand.Take(max).OrderBy(i => i).ToArray();
    }

    static string Region(Patch p) => p.K1 == 0 ? "flat" : p.K2 == 0 ? "cyl" : "sphere";

    static void CurvRows(StreamWriter w, Dictionary<string, object> head, string method, SoupFeatures.CurvResult r, Func<int, Patch> patchOf)
    {
        foreach (string reg in new[] { "flat", "cyl", "sphere" }) {
            var eK = new List<double>(); var eH = new List<double>(); int n = 0, valid = 0;
            for (int qi = 0; qi < r.Valid.Length; ++qi) {
                Patch p = patchOf(qi);
                if (Region(p) != reg) continue;
                n++;
                if (!r.Valid[qi]) continue;
                valid++;
                eK.Add(Math.Max(Math.Abs(r.K1[qi] - p.K1), Math.Abs(r.K2[qi] - p.K2)) * R);
                eH.Add(Math.Abs(r.H[qi] - (p.K1 + p.K2)) * R);
            }
            if (n == 0) continue;
            var row = new Dictionary<string, object>(head) {
                ["method"] = method, ["region"] = reg, ["queries"] = n, ["valid"] = (double)valid / n,
                ["eK_med"] = Pct(eK, 0.5), ["eK_p90"] = Pct(eK, 0.9), ["eH_med"] = Pct(eH, 0.5), ["eH_p90"] = Pct(eH, 0.9), ["seconds"] = r.Seconds
            };
            Emit(w, row);
        }
    }

    static void Curv(StreamWriter w, string shape, string br, int tris, double[] epss)
    {
        var c = Build(shape, br, tris);
        Soup s = c.Soup;
        double hm = SoupFeatures.MedianEdge(s);
        int[] qc = Queries(c, epss.Max() * c.H + c.H, 3000);
        var qpos = qc.Select(i => s.V[i]).ToList();
        Func<int, Patch> patchOf = qi => c.Patches[s.PatchOf[qc[qi] / 3]];
        var head = new Dictionary<string, object> { ["kind"] = "curv", ["shape"] = shape, ["break"] = br, ["tris"] = s.Count, ["h"] = c.H, ["R_over_h"] = R / c.H };
        foreach (double tol in new[] { 1e-6, 0.1, 0.3 })
            foreach (int rounds in new[] { 0, 2 })
                CurvRows(w, head, "mpc_weld" + tol.ToString(CultureInfo.InvariantCulture) + "h_r" + rounds, SoupFeatures.WeldCurvature(s, tol * hm, qc, rounds), patchOf);
        foreach (double e in epss) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ps = SoupFeatures.SoupMeasure(s, e * hm / 3);
            double tSample = sw.Elapsed.TotalSeconds;
            var vf = SoupFeatures.VarifoldCurvature(ps, qpos, e * hm); vf.Seconds += tSample;
            var vfl = SoupFeatures.VarifoldCurvature(ps, qpos, e * hm, true); vfl.Seconds += tSample;
            var jet = SoupFeatures.JetCurvature(ps, qpos, e * hm); jet.Seconds += tSample;
            string tag = e.ToString(CultureInfo.InvariantCulture);
            CurvRows(w, head, "varifold_e" + tag + "h", vf, patchOf);
            CurvRows(w, head, "varifoldLS_e" + tag + "h", vfl, patchOf);
            CurvRows(w, head, "jet_e" + tag + "h", jet, patchOf);
        }
    }

    static void Cloud(StreamWriter w, string shape, double noise, int tris, double[] epss)
    {
        var c = Build(shape, "clean", tris);
        Soup s = c.Soup;
        // n points, area-weighted on the clean soup (about the vertex count of the mesh), normal noise noise * spacing
        var area = new double[s.Count]; double total = 0;
        for (int t = 0; t < s.Count; ++t) { total += 0.5 * (s.V[3 * t + 1] - s.V[3 * t]).Cross(s.V[3 * t + 2] - s.V[3 * t]).Length; area[t] = total; }
        int n = s.Count / 2;
        double hp = Math.Sqrt(total / n);
        var rnd = new Random(5);
        var pts = new List<Vector3d>(); var src = new List<int>();
        for (int i = 0; i < n; ++i) {
            int t = Array.BinarySearch(area, rnd.NextDouble() * total); if (t < 0) t = ~t; t = Math.Min(t, s.Count - 1);
            double u = rnd.NextDouble(), v = rnd.NextDouble(); if (u + v > 1) { u = 1 - u; v = 1 - v; }
            Vector3d a = s.V[3 * t], b = s.V[3 * t + 1], cc = s.V[3 * t + 2];
            Vector3d nn = (b - a).Cross(cc - a).Normalized;
            // the exact surface point: the triangle point pushed onto the patch is not needed for flats; for curved patches the
            // chord error h^2 / (8 R) stays below the noise levels used (reported as noise 0)
            pts.Add(a + u * (b - a) + v * (cc - a) + noise * hp * (2 * rnd.NextDouble() - 1) * nn); src.Add(t);
        }
        var jumps = new SegLookup(c.Jumps.Select(j => (j.a, j.b, 0.0)).ToList(), epss.Max() * hp + hp);
        double dex = epss.Max() * hp + hp;
        var q = Enumerable.Range(0, n).Where(i => jumps.Nearest(pts[i]).d >= dex).Take(3000).ToArray();
        var qpos = q.Select(i => pts[i]).ToList();
        Func<int, Patch> patchOf = qi => c.Patches[s.PatchOf[src[q[qi]]]];
        var head = new Dictionary<string, object> { ["kind"] = "cloud", ["shape"] = shape, ["noise"] = noise, ["points"] = n, ["spacing"] = hp, ["R_over_spacing"] = R / hp };
        foreach (double e in epss) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ps = SoupFeatures.CloudMeasure(pts, total, 2.5 * hp);
            double tPca = sw.Elapsed.TotalSeconds;
            var vf = SoupFeatures.VarifoldCurvature(ps, qpos, e * hp); vf.Seconds += tPca;
            var vfl = SoupFeatures.VarifoldCurvature(ps, qpos, e * hp, true); vfl.Seconds += tPca;
            var jet = SoupFeatures.JetCurvature(ps, qpos, e * hp);
            string tag = e.ToString(CultureInfo.InvariantCulture);
            CurvRows(w, head, "varifold_e" + tag + "s", vf, patchOf);
            CurvRows(w, head, "varifoldLS_e" + tag + "s", vfl, patchOf);
            CurvRows(w, head, "jet_e" + tag + "s", jet, patchOf);
        }
    }

    // ---- time ----

    static void Time(StreamWriter w, double[] sizes)
    {
        Crease(new StreamWriter(Stream.Null), "box", "mix:0.5", 5000, new[] { 2.0 });   // warm-up (JIT)
        foreach (double size in sizes) {
            foreach (string shape in new[] { "box", "rbox" }) {
                var c = Build(shape, "mix:0.5", (int)size);
                Soup s = c.Soup; double hm = SoupFeatures.MedianEdge(s);
                var row = new Dictionary<string, object> { ["kind"] = "time", ["shape"] = shape, ["break"] = "mix:0.5", ["tris"] = s.Count, ["threads"] = SoupFeatures.Threads };
                row["weld_dihedral_s"] = SoupFeatures.WeldDihedral(s, 0.1 * hm).Seconds;
                row["varifold_crease_s"] = SoupFeatures.VarifoldCreases(s, hm, 2).Seconds;
                row["pca_crease_s"] = SoupFeatures.PcaCreases(s, hm, 2).Seconds;
                // curvature at one corner of every triangle (about twice the vertex count)
                int[] qc = Enumerable.Range(0, s.Count).Select(t => 3 * t).ToArray();
                var qpos = qc.Select(i => s.V[i]).ToList();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var ps = SoupFeatures.SoupMeasure(s, 3 * hm / 3);
                double tS = sw.Elapsed.TotalSeconds;
                row["varifold_curv_e3_s"] = tS + SoupFeatures.VarifoldCurvature(ps, qpos, 3 * hm).Seconds;
                row["jet_curv_e3_s"] = tS + SoupFeatures.JetCurvature(ps, qpos, 3 * hm).Seconds;
                row["mpc_weld_s"] = SoupFeatures.WeldCurvature(s, 0.1 * hm, qc, 0).Seconds;
                row["curv_queries"] = qc.Length;
                Emit(w, row);
            }
        }
    }

    // ---- the cost bound of a varifold crease input to AlphaWrapFeaturePass (docs/design/VARIFOLD_CREASE_INPUT.md) ----

    // per shape: the alpha wrap (300 / 3000) of the broken soup (mix:0.5) and its time; the feature pass on the clean welded input
    // (its crossing edges bound the crease-point queries: at most 3 per crossing edge) and on the soup (today: no input crease)
    static void WrapBound(StreamWriter w, string[] shapes, int tris)
    {
        foreach (string shape in shapes) {
            var c = Build(shape, "mix:0.5", tris);
            Soup clean = SoupShapes.Tessellate(c.Patches, c.H, null);
            DMesh3 welded = SoupWeld.Weld(clean, 1e-9 * c.H, out _);
            DMesh3 soup = c.Soup.ToMesh();
            var row = new Dictionary<string, object> { ["kind"] = "wrapbound", ["shape"] = shape, ["clean_tris"] = welded.TriangleCount, ["soup_tris"] = soup.TriangleCount };
            foreach (var (tag, input) in new[] { ("clean", welded), ("soup", soup) }) {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var wrap = new MeshAlphaWrap(input) { RelativeAlpha = 300, RelativeOffset = 3000 };
                wrap.Compute();
                row[tag + "_wrap_s"] = sw.Elapsed.TotalSeconds; row[tag + "_wrap_tris"] = wrap.Result.TriangleCount;
                sw.Restart();
                var fp = new AlphaWrapFeaturePass(new DMesh3(wrap.Result, true), input, wrap.UsedAlpha, wrap.UsedOffset);
                fp.Compute();
                row[tag + "_pass_s"] = sw.Elapsed.TotalSeconds; row[tag + "_input_creases"] = fp.InputCreases; row[tag + "_crossing_edges"] = fp.CrossingEdges;
                row[tag + "_snaps"] = fp.CreaseSnaps; row[tag + "_splits"] = fp.Splits; row["alpha"] = wrap.UsedAlpha; row["h_over_alpha"] = c.H / wrap.UsedAlpha;
            }
            // the varifold side: the soup's measure (spacing h) and its grid, then the full per-edge test for the per-query cost
            double hm = SoupFeatures.MedianEdge(c.Soup);
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var ps = SoupFeatures.SoupMeasure(c.Soup, hm);
            var grid = new SortedGrid(ps, 2 * hm);
            row["varifold_sample_s"] = sw2.Elapsed.TotalSeconds; row["varifold_points"] = ps.Count;
            var all = SoupFeatures.VarifoldCreases(c.Soup, hm, 2);
            row["varifold_all_edges_s"] = all.Seconds; row["varifold_us_per_query"] = 1e6 * (all.Seconds - (double)row["varifold_sample_s"]) / (3.0 * c.Soup.Count);
            Emit(w, row);
        }
    }

    // the sharp-feature pass on the wrap of a broken soup (docs/design/VARIFOLD_CREASE_INPUT.md): before (the default pass on the
    // soup: dihedral creases), after (VarifoldCreases on the soup) and the ceiling (the default pass on the wrap of the clean
    // welded copy). Every row measured against the clean welded copy's creases (EdgeClass: recall of the input's miter lines by the
    // result's sharp edges within 1 and 3 offsets, precision the other way), with the clean status and the times
    internal static double PlaneLift = 2;
    static void SoupWrap(StreamWriter w, string shape, string br, int tris, double[] radii, double[] planes)
    {
        var c = Build(shape, br, tris);
        Soup clean = SoupShapes.Tessellate(c.Patches, c.H, null);
        DMesh3 welded = SoupWeld.Weld(clean, 1e-9 * c.H, out _);
        DMesh3 soup = br == "clean" ? welded : c.Soup.ToMesh();
        // ceiling: the clean copy wrapped, its exact creases; oracle: the soup's wrap with the clean copy's exact creases (the same
        // wrap as before / after: the best a crease source can do there, and the time without the varifold)
        var runs = new List<(string name, DMesh3 input, bool vf, double radius, double planeRadius)> { ("ceiling", welded, false, 0, 0), ("oracle", welded, false, 0, 0), ("before", soup, false, 0, 0) };
        foreach (double rr in radii)
            foreach (double pr in planes)
                runs.Add(("after_r" + rr.ToString(CultureInfo.InvariantCulture) + (pr > 0 ? "_p" + pr.ToString(CultureInfo.InvariantCulture) : ""), soup, true, rr, pr));
        MeshAlphaWrap wrapSoup = null, wrapClean = null; double tSoup = 0, tClean = 0;
        foreach (var (name, input, vf, radius, planeRadius) in runs) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            MeshAlphaWrap wrap;
            bool soupWrap = name != "ceiling";
            if (soupWrap && wrapSoup != null) wrap = wrapSoup;
            else if (!soupWrap && wrapClean != null) wrap = wrapClean;
            else {
                wrap = new MeshAlphaWrap(soupWrap ? soup : welded) { RelativeAlpha = 300, RelativeOffset = 3000 };
                wrap.Compute();
                if (soupWrap) { wrapSoup = wrap; tSoup = sw.Elapsed.TotalSeconds; } else { wrapClean = wrap; tClean = sw.Elapsed.TotalSeconds; }
            }
            sw.Restart();
            var fp = new AlphaWrapFeaturePass(new DMesh3(wrap.Result, true), input, wrap.UsedAlpha, wrap.UsedOffset) { VarifoldCreases = vf };
            if (vf) { fp.VarifoldRadius = radius; fp.VarifoldPlaneRadius = planeRadius; fp.VarifoldPlaneLift = PlaneLift; }
            fp.Compute();
            double tPass = sw.Elapsed.TotalSeconds;
            var row = new Dictionary<string, object> { ["kind"] = "soupwrap", ["shape"] = shape, ["break"] = br, ["run"] = name, ["input_tris"] = input.TriangleCount,
                ["h_over_alpha"] = c.H / wrap.UsedAlpha, ["wrap_s"] = soupWrap ? tSoup : tClean, ["wrap_tris"] = wrap.Result.TriangleCount, ["pass_s"] = tPass,
                ["input_creases"] = fp.InputCreases, ["rejected_all"] = fp.Rejected, ["snaps"] = fp.CreaseSnaps, ["splits"] = fp.Splits, ["flips"] = fp.Flips, ["plane_moves"] = fp.PlaneMoves,
                ["not_clean"] = MeshRobustRepair.NotCleanReason(fp.Mesh) };
            if (vf) {
                // the varifold's share of the pass: the measure (built once) and the queries
                double f = 1.0 / System.Diagnostics.Stopwatch.Frequency;
                row["measure_s"] = fp.VarifoldBuildTicks * f; row["query_s"] = fp.VarifoldQueryTicks * f; row["queries"] = fp.VarifoldQueries;
                row["plane_s"] = fp.VarifoldPlaneTicks * f; row["plane_queries"] = fp.VarifoldPlaneQueries; row["planes_fitted"] = fp.VarifoldPlanesFitted;
                row["rejected"] = fp.Rejected;
                row["median_tilt"] = fp.VarifoldMedianTilt; row["planes_used"] = fp.VarifoldPlanesUsed;
            }
            EdgeClass.Add(welded, fp.Mesh, wrap.UsedAlpha, wrap.UsedOffset, row, 5000);
            MiterMetric(c, fp.Mesh, wrap.UsedAlpha, wrap.UsedOffset, row);
            // recall over both classes (by length) on the miter lines, within 1 and 3 offsets
            double lc = (double)row["ec_len_cvx"], lv = (double)row["ec_len_ccv"];
            double R(string k) => row.TryGetValue(k, out var o) && o is double d ? d : 0;
            row["recall_w1"] = (lc * R("ec_cvx_mi_w1") + lv * R("ec_ccv_mi_w1")) / Math.Max(1e-300, lc + lv);
            row["recall_w3"] = (lc * R("ec_cvx_mi_w3") + lv * R("ec_ccv_mi_w3")) / Math.Max(1e-300, lc + lv);
            row["precision_w3"] = R("ec_prec_mi_w3");
            Emit(w, row);
        }
    }

    // recall and precision of the result's sharp edges (dihedral > 30 degrees) against the offset surface's creases of the broken
    // input itself: every true crease moved with its two patches (the gap breakage moves each patch rigidly), its miter line at one
    // offset (n1 + n2) / (1 + n1.n2) from the moved planes' line, capped at 4 offsets as the pass caps it. (Jitter is noise around
    // these lines; the clean copy's miter lines, EdgeClass, are off by the gap.) Rows m_rec1, m_rec3, m_prec1, m_prec3.
    static void MiterMetric(Case c, DMesh3 result, double alpha, double offset, Dictionary<string, object> row)
    {
        Soup clean = SoupShapes.Tessellate(c.Patches, c.H, null);
        var (cr, _, pairs) = SoupShapes.TruthWithPatches(c.Patches, clean, c.H);
        var miter = new List<EdgeClass.Seg>();
        Vector3d Map(Vector3d x, int ia, int ib)
        {
            Patch pa = c.Patches[ia], pb = c.Patches[ib];
            Vector3d na = pa.N(x), nb = pb.N(x);
            c.Soup.Shift.TryGetValue(ia, out Vector3d da); c.Soup.Shift.TryGetValue(ib, out Vector3d db);
            double cc = na.Dot(nb), det = 1 - cc * cc;
            Vector3d p = x + 0.5 * (da + db);
            if (det > 1e-9) {
                double r1 = na.Dot(x + da) - na.Dot(p), r2 = nb.Dot(x + db) - nb.Dot(p);
                p += (r1 - cc * r2) / det * na + (r2 - cc * r1) / det * nb;
            }
            Vector3d sft = (na + nb) / Math.Max(1 + cc, 1e-12) * offset;
            if (sft.Length > 4 * offset) sft *= 4 * offset / sft.Length;
            return p + sft;
        }
        for (int i = 0; i < cr.Count; ++i) miter.Add(new EdgeClass.Seg { A = Map(cr[i].a, pairs[i].pa, pairs[i].pb), B = Map(cr[i].b, pairs[i].pa, pairs[i].pb) });
        var sharp = new List<EdgeClass.Seg>();
        double cos30 = Math.Cos(30 * MathUtil.Deg2Rad);
        foreach (int eid in result.EdgeIndices()) {
            Index2i et = result.GetEdgeT(eid);
            if (et.b < 0 || result.GetTriNormal(et.a).Dot(result.GetTriNormal(et.b)) > cos30) continue;
            Index2i ev = result.GetEdgeV(eid);
            sharp.Add(new EdgeClass.Seg { A = result.GetVertex(ev.a), B = result.GetVertex(ev.b) });
        }
        Vector3d o = result.CachedBounds.Min - 8 * alpha * Vector3d.One;
        List<Vector3d> Samples(List<EdgeClass.Seg> segs, int n)
        {
            double total = segs.Sum(g => g.A.Distance(g.B)), step = total / n, acc = 0, next = 0.5 * step;
            var pts = new List<Vector3d>();
            if (!(total > 0)) return pts;
            foreach (var g in segs) { double l = g.A.Distance(g.B); while (next < acc + l) { pts.Add(Vector3d.Lerp(g.A, g.B, (next - acc) / l)); next += step; } acc += l; }
            return pts;
        }
        (double, double) Within(List<Vector3d> pts, List<EdgeClass.Seg> segs)
        {
            if (pts.Count == 0 || segs.Count == 0) return (0, 0);
            var grid = new EdgeClass.SegGrid(segs, alpha, o);
            int w1 = 0, w3 = 0;
            foreach (var p in pts) { double d = grid.Nearest(p, 2 * alpha); if (d <= offset) w1++; if (d <= 3 * offset) w3++; }
            return ((double)w1 / pts.Count, (double)w3 / pts.Count);
        }
        var (r1, r3) = Within(Samples(miter, 4000), sharp);
        var (p1, p3) = Within(Samples(sharp, 4000), miter);
        row["m_rec1"] = r1; row["m_rec3"] = r3; row["m_prec1"] = p1; row["m_prec3"] = p3;
    }

    // the wrap time of one breakage at a time (diagnosis of the slow wraps of mixed soups)
    static void WrapBreaks(StreamWriter w, string shape, int tris, string[] breaks)
    {
        foreach (string br in breaks) {
            var c = Build(shape, br, tris);
            DMesh3 soup = br == "clean" ? SoupWeld.Weld(c.Soup, 1e-9 * c.H, out _) : c.Soup.ToMesh();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wrap = new MeshAlphaWrap(soup) { RelativeAlpha = 300, RelativeOffset = 3000 };
            wrap.Compute();
            Emit(w, new Dictionary<string, object> { ["kind"] = "wrapbreak", ["shape"] = shape, ["break"] = br, ["tris"] = soup.TriangleCount,
                ["wrap_s"] = sw.Elapsed.TotalSeconds, ["wrap_tris"] = wrap.Result.TriangleCount, ["status"] = wrap.Status.ToString(), ["h_over_alpha"] = c.H / wrap.UsedAlpha });
        }
    }

    // ---- determinism and similarity ----

    static ulong Hash(IEnumerable<double> xs)
    {
        ulong h = 1469598103934665603UL;
        foreach (double x in xs) { h ^= (ulong)BitConverter.DoubleToInt64Bits(x); h *= 1099511628211UL; }
        return h;
    }

    static (ulong crease, ulong curv, bool[] flags, double[] k1) Outputs(Soup s, double scaleBack)
    {
        double hm = SoupFeatures.MedianEdge(s);
        var cr = SoupFeatures.VarifoldCreases(s, hm, 2);
        int[] qc = Enumerable.Range(0, Math.Min(s.Count, 4000)).Select(t => 3 * t).ToArray();
        var qpos = qc.Select(i => s.V[i]).ToList();
        var ps = SoupFeatures.SoupMeasure(s, hm);
        var cu = SoupFeatures.VarifoldCurvature(ps, qpos, 3 * hm);
        ulong hc = Hash(cr.Flag.Select(f => f ? 1.0 : 0.0).Concat(cr.Dih));
        ulong hk = Hash(cu.K1.Concat(cu.K2).Concat(cu.H).Select(k => k * scaleBack));
        return (hc, hk, cr.Flag, cu.K1.Select(k => k * scaleBack).ToArray());
    }

    static Soup Transform(Soup s, double scale, Matrix3d rot, Vector3d tr)
    {
        var r = s.Copy();
        for (int i = 0; i < r.V.Count; ++i) r.V[i] = scale * (rot * r.V[i]) + tr;
        return r;
    }

    static void Check(StreamWriter w, int tris)
    {
        foreach (string shape in new[] { "fillet", "rbox" }) {
            var c = Build(shape, "mix:0.5", tris);
            int saved = SoupFeatures.Threads;
            SoupFeatures.Threads = 1; var a = Outputs(c.Soup, 1);
            SoupFeatures.Threads = Math.Max(4, saved); var b = Outputs(c.Soup, 1); var b2 = Outputs(c.Soup, 1);
            SoupFeatures.Threads = saved;
            Emit(w, new Dictionary<string, object> { ["kind"] = "check", ["shape"] = shape, ["what"] = "threads 1 vs " + Math.Max(4, saved) + ", repeated",
                ["crease_equal"] = a.crease == b.crease && b.crease == b2.crease, ["curv_equal"] = a.curv == b.curv && b.curv == b2.curv });
            var sims = new (string name, double s, Matrix3d rot, Vector3d t)[] {
                ("identity", 1, Matrix3d.Identity, Vector3d.Zero),
                ("utm", 1, Matrix3d.Identity, new Vector3d(632117.25, 5712043.5, 113.75)),
                ("scale 2^-10", 1.0 / 1024, Matrix3d.Identity, Vector3d.Zero),
                ("scale 2^10", 1024, Matrix3d.Identity, Vector3d.Zero),
                ("scale 1e-3", 1e-3, Matrix3d.Identity, Vector3d.Zero),
                ("scale 1e3", 1e3, Matrix3d.Identity, Vector3d.Zero),
                ("rotated", 1, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17), Vector3d.Zero),
                ("rotated, scale 1e-3, utm", 1e-3, Matrix3d.AxisAngleD(Vector3d.AxisY, 23), new Vector3d(632117.25, 5712043.5, 113.75)),
            };
            foreach (var sim in sims) {
                var o = Outputs(Transform(c.Soup, sim.s, sim.rot, sim.t), sim.s);
                int agree = 0; for (int i = 0; i < a.flags.Length; ++i) if (a.flags[i] == o.flags[i]) agree++;
                double maxRel = 0; for (int i = 0; i < a.k1.Length; ++i) maxRel = Math.Max(maxRel, Math.Abs(a.k1[i] - o.k1[i]) / Math.Max(1, a.k1[i]));
                Emit(w, new Dictionary<string, object> { ["kind"] = "similarity", ["shape"] = shape, ["sim"] = sim.name,
                    ["crease_bits_equal"] = o.crease == a.crease, ["curv_bits_equal"] = o.curv == a.curv,
                    ["flag_agreement"] = (double)agree / a.flags.Length, ["k1_max_rel_diff"] = maxRel });
            }
        }
    }
}
