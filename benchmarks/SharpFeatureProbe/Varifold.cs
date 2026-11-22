// Study "a kernel varifold distance as a quality measure of the sharp alpha wrap" (docs/research/VARIFOLD_WRAP_QUALITY_2026-11.md).
// No library change.
//
//   SharpFeatureProbe varifold <out.jsonl> <item>... [-- sigmas=5,10,20,40 q=1]
//
// Items:
//   <case>            a case of the bound probe (Bound.cs: box, lshape, ..., fin:<k>, fslot, [@rot|@far|@rotfar]). Variants:
//                       R the raw wrap, A the pipeline (wrap, simplification), K the feature pass then the simplification,
//                     each against two references: O the mitered offset of the clean reference (Bound.cs), I the input itself.
//   selftest          closed form (two parallel squares) and the convergence in the quadrature spacing
//   bevel:<w,w,...>   controlled damage, no wrap: an L-shaped prism against itself with every vertical crease chamfered w offsets wide
//   count:<w>         the same with 1, 2, ... of its 6 vertical creases chamfered w offsets wide
//   shift:<x,x,...>   the prism against its own mitered offset by x offsets (position error only)
//   floor:<case>      the floor row F of a case alone (no wrap), with the length of the oracle's feature network
//   pose:<case>       the metric of (A, O) of a case under similarities applied to both meshes
//
// sigma is given in offsets (offset = diagonal / 3000, the wrap's); the quadrature spacing is sigma / q.
// One JSON row per (item, variant, reference, sigma). Normalised distances, per normal kernel k (mass, current, varifold, oriented):
//   rel_k   = |S - T|^2 / (|S|^2 + |T|^2)          dimensionless, in [0, 1] for the kernels without cancellation
//   line_k  = |S - T|^2 / (sqrt(pi) sigma) / offset^3   for damage narrower than sigma along a crease: about width^2 x length
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

static class Varifold
{
    const double RelAlpha = 300, RelOffset = 3000;
    static double[] sigmas = { 5, 10, 20, 40 };
    static double q = 1;
    static string outFile;

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: SharpFeatureProbe varifold <out.jsonl> <item>... [-- sigmas=5,10,20,40 q=1]"); return 2; }
        outFile = args[1];
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var items = new List<string>();
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--") {
                for (int k = i + 1; k < args.Length; ++k) {
                    if (args[k].StartsWith("sigmas=")) sigmas = args[k].Substring(7).Split(',').Select(s => double.Parse(s, inv)).ToArray();
                    else if (args[k].StartsWith("q=")) q = double.Parse(args[k].Substring(2), inv);
                    else throw new ArgumentException(args[k]);
                }
                break;
            }
            items.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile)) {
                if (!line.EndsWith("}")) continue;
                using (var doc = JsonDocument.Parse(line)) done.Add(doc.RootElement.GetProperty("file").GetString());
            }
        int failed = 0;
        foreach (string item in items) {
            if (done.Contains(item)) continue;
            try {
                if (item == "selftest") failed += SelfTest();
                else if (item.StartsWith("bevel:")) Damage(item, item.Substring(6).Split(',').Select(s => double.Parse(s, inv)).ToArray(), "bevel");
                else if (item.StartsWith("count:")) Damage(item, new[] { double.Parse(item.Substring(6), inv) }, "count");
                else if (item.StartsWith("shift:")) Damage(item, item.Substring(6).Split(',').Select(s => double.Parse(s, inv)).ToArray(), "shift");
                else if (item.StartsWith("pose:")) Pose(item);
                else if (item.StartsWith("floor:")) Floor(item);
                else Case(item);
            } catch (Exception e) { Console.Error.WriteLine(item + ": " + e); failed++; }
        }
        return failed == 0 ? 0 : 1;
    }

    // ---- a wrap case ----

    static void Wraps(string name, out DMesh3 input, out DMesh3 oracle, out Bound.Network net, out double alpha, out double delta,
        out DMesh3 raw, out DMesh3 a, out DMesh3 k, out long msA, out long msK)
    {
        Bound.Make(name, out input, out DMesh3 reference, out bool dirty);
        double diag = input.CachedBounds.DiagonalLength;
        alpha = diag / RelAlpha; delta = diag / RelOffset;
        oracle = Bound.MiteredOffset(reference, delta, out double residual);
        net = new Bound.Network(oracle, alpha);
        var sw = Stopwatch.StartNew();
        var wrap = new MeshAlphaWrap(input) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset };
        wrap.Compute();
        if (wrap.Result == null || wrap.Status != MeshAlphaWrap.WrapStatus.Success) throw new InvalidOperationException("wrap " + wrap.Status);
        long rawMs = sw.ElapsedMilliseconds;
        alpha = wrap.UsedAlpha; delta = wrap.UsedOffset;
        raw = wrap.Result;
        sw.Restart();
        a = Program.Simplify(new DMesh3(raw, true), delta, input);
        msA = rawMs + sw.ElapsedMilliseconds;
        sw.Restart();
        var kp = new AlphaWrapFeaturePass(new DMesh3(raw, true), input, alpha, delta);
        kp.Compute();
        k = Program.Simplify(new DMesh3(kp.Mesh, true), delta, input);
        msK = rawMs + sw.ElapsedMilliseconds;
    }

    static void Case(string name)
    {
        Wraps(name, out DMesh3 input, out DMesh3 oracle, out Bound.Network net, out double alpha, out double delta, out DMesh3 raw, out DMesh3 a, out DMesh3 k, out long msA, out long msK);
        var refs = new[] { ("O", oracle), ("I", input) };
        var refCache = new Dictionary<string, Cached>();
        foreach (var (variant, result, ms) in new[] { ("R", raw, 0L), ("A", a, msA), ("K", k, msK) }) {
            var probe = new Dictionary<string, object>();
            Bound.Measure(input, oracle, net, result, alpha, delta, probe);
            var res = new Cached(result);
            foreach (var (rname, reference) in refs) {
                if (!refCache.TryGetValue(rname, out Cached rc)) refCache[rname] = rc = new Cached(reference);
                var common = new Dictionary<string, object>();
                common["file"] = name; common["variant"] = variant; common["ref"] = rname; common["delta"] = delta; common["alpha"] = alpha;
                common["tris"] = result.TriangleCount; common["ref_tris"] = reference.TriangleCount; common["pipeline_ms"] = ms;
                foreach (string key in new[] { "clean", "cross_input", "cov01", "cov025", "cov1", "fh_max", "fh_p95", "corner025", "prec025", "band_min", "band_max" })
                    if (probe.ContainsKey(key)) common[key] = probe[key];
                Rows(common, res, rc, delta);
            }
        }
    }

    // the same surface with another triangulation (every triangle split at an inner point): the floor of the quadrature
    static DMesh3 Resplit(DMesh3 m)
    {
        var r = new DMesh3(m, true);
        var rnd = new Random(4711);
        foreach (int tid in m.TriangleIndices()) {
            double u = 0.15 + 0.3 * rnd.NextDouble(), v = 0.15 + 0.3 * rnd.NextDouble();      // (u + v <= 0.9: inside)
            r.PokeTriangle(tid, new Vector3d(1 - u - v, u, v), out DMesh3.PokeTriangleInfo info);
        }
        return r;
    }

    // only the floor row of a case (no wrap), with the length of the oracle's feature network in offsets
    static void Floor(string item)
    {
        Bound.Make(item.Substring(6), out DMesh3 input, out DMesh3 reference, out bool dirty);
        double diag = input.CachedBounds.DiagonalLength, alpha = diag / RelAlpha, delta = diag / RelOffset;
        DMesh3 oracle = Bound.MiteredOffset(reference, delta, out double residual);
        var net = new Bound.Network(oracle, alpha);
        var common = new Dictionary<string, object>();
        common["file"] = item; common["variant"] = "F"; common["ref"] = "O"; common["delta"] = delta; common["tris"] = oracle.TriangleCount;
        common["net_len"] = net.Length / delta; common["ref_area"] = MeshMeasurements.AreaT(oracle, oracle.TriangleIndices()) / (delta * delta);
        Rows(common, new Cached(Resplit(oracle)), new Cached(oracle), delta);
    }

    // ---- rows: the varifold distances at every sigma, with the classical measures beside them ----

    // a mesh with its quadrature per spacing and its self products per sigma
    sealed class Cached
    {
        public readonly DMesh3 Mesh;
        public readonly DMeshAABBTree3 Tree;
        readonly Dictionary<double, VarifoldMeasure> measures = new Dictionary<double, VarifoldMeasure>();
        readonly Dictionary<double, double[]> self = new Dictionary<double, double[]>();
        public Cached(DMesh3 m) { Mesh = m; Tree = new DMeshAABBTree3(m, true); }
        public VarifoldMeasure Measure(double h) { if (!measures.TryGetValue(h, out var v)) measures[h] = v = VarifoldMeasure.Sample(Mesh, h); return v; }
        public double[] Self(double sigma, double h) { if (!self.TryGetValue(sigma, out var v)) self[sigma] = v = VarifoldMeasure.Inner(Measure(h), Measure(h), sigma); return v; }
        public void Drop(double h) { measures.Remove(h); }
        VarifoldMeasure centroids; double centroidH;
        public VarifoldMeasure Centroids(double h) { if (centroids == null || centroidH != h) { centroids = VarifoldMeasure.Sample(Mesh, h, 60_000_000, true); centroidH = h; } return centroids; }
    }

    static void Rows(Dictionary<string, object> common, Cached s, Cached t, double delta)
    {
        // the classical measures, on the quadrature points of the largest sigma (in offsets)
        double hc = sigmas.Max() * delta / q;
        var sw = Stopwatch.StartNew();
        OneSided(s.Centroids(hc), t, out double hausST, out double meanST, out double ncST);
        OneSided(t.Centroids(hc), s, out double hausTS, out double meanTS, out double ncTS);
        common["classic_ms"] = sw.ElapsedMilliseconds;
        common["haus"] = Math.Max(hausST, hausTS) / delta; common["haus_to_ref"] = hausST / delta; common["haus_from_ref"] = hausTS / delta;
        common["chamfer"] = 0.5 * (meanST + meanTS) / delta;
        common["normal_deg"] = 0.5 * (ncST + ncTS);
        double areaS = s.Centroids(hc).Area, areaT = t.Centroids(hc).Area;
        common["area_ratio"] = areaS / areaT;
        foreach (double sg in sigmas) {
            double sigma = sg * delta, h = sigma / q;
            sw.Restart();
            VarifoldMeasure ms = s.Measure(h), mt = t.Measure(h);
            long sampleMs = sw.ElapsedMilliseconds;
            double[] ss = s.Self(sigma, h), tt = t.Self(sigma, h), st = VarifoldMeasure.Inner(ms, mt, sigma);
            var r = new Dictionary<string, object>(common);
            r["sigma"] = sg; r["q"] = q; r["points"] = ms.Count; r["ref_points"] = mt.Count; r["sample_ms"] = sampleMs; r["varifold_ms"] = sw.ElapsedMilliseconds;
            r["ss"] = ss; r["st"] = st; r["tt"] = tt;
            for (int kn = 0; kn < VarifoldMeasure.Kernels; ++kn) {
                double d2 = ss[kn] - 2 * st[kn] + tt[kn];
                r["rel_" + VarifoldMeasure.KernelNames[kn]] = d2 / (ss[kn] + tt[kn]);
                // |S - T|^2 / (sqrt(pi) sigma) in offsets^3: for damage narrower than sigma along a crease, about (width^2 x length)
                r["line_" + VarifoldMeasure.KernelNames[kn]] = d2 / (Math.Sqrt(Math.PI) * sigma) / (delta * delta * delta);
            }
            File.AppendAllText(outFile, JsonSerializer.Serialize(r, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }) + "\n");
            Console.WriteLine($"{common["file"]} {common["variant"]} {common["ref"]} sigma={sg} pts={ms.Count}/{mt.Count} rel_o8={r["rel_oriented8"]:E3} rel_o2={r["rel_oriented2"]:E3} rel_vf={r["rel_varifold"]:E3} rel_cur={r["rel_current"]:E3} rel_mass={r["rel_mass"]:E3} haus={common["haus"]:F2} area={common["area_ratio"]:F5} nrm={common["normal_deg"]:F2} cov025={(common.ContainsKey("cov025") ? common["cov025"] : "-")} ms={sw.ElapsedMilliseconds}");
            if (sg != sigmas.Max()) { s.Drop(h); }
        }
    }

    // sampled one-sided Hausdorff and mean distance from s's points to t, and the mean angle to the nearest triangle's normal
    static void OneSided(VarifoldMeasure s, Cached t, out double haus, out double mean, out double normalDeg)
    {
        var d = new double[s.Count]; var ang = new double[s.Count];
        gParallel.ForEach(Interval1i.Range(s.Count), (i) => {
            int tid = t.Tree.FindNearestTriangle(new Vector3d(s.X[i], s.Y[i], s.Z[i]), out double d2);
            d[i] = Math.Sqrt(d2);
            Vector3d n = t.Mesh.GetTriNormal(tid);
            ang[i] = Math.Acos(Math.Max(-1, Math.Min(1, n.x * s.NX[i] + n.y * s.NY[i] + n.z * s.NZ[i]))) * 180 / Math.PI;
        });
        haus = 0; mean = 0; normalDeg = 0;
        for (int i = 0; i < s.Count; ++i) { haus = Math.Max(haus, d[i]); mean += s.A[i] * d[i]; normalDeg += s.A[i] * ang[i]; }
        mean /= s.Area; normalDeg /= s.Area;
    }

    // ---- controlled damage on an L-shaped prism (no wrap) ----

    static readonly Vector2d[] LPoly = { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 5), new Vector2d(5, 5), new Vector2d(5, 10), new Vector2d(0, 10) };

    // the polygon with its first `count` corners cut w wide (the two cut points w from the corner along its edges)
    static DMesh3 Chamfered(double w, int count)
    {
        var p = new List<Vector2d>();
        int n = LPoly.Length;
        for (int i = 0; i < n; ++i) {
            Vector2d c = LPoly[i], prev = LPoly[(i + n - 1) % n], next = LPoly[(i + 1) % n];
            if (i < count && w > 0) { p.Add(c + w * (prev - c).Normalized); p.Add(c + w * (next - c).Normalized); } else p.Add(c);
        }
        return Procedural.Prism(p, 4);
    }

    static void Damage(string item, double[] values, string kind)
    {
        DMesh3 reference = Chamfered(0, 0);
        double delta = reference.CachedBounds.DiagonalLength / RelOffset;
        var rc = new Cached(reference);
        var list = new List<(string, DMesh3)>();
        if (kind == "bevel") foreach (double w in values) list.Add(("w" + w.ToString(System.Globalization.CultureInfo.InvariantCulture), Chamfered(w * delta, LPoly.Length)));
        if (kind == "count") for (int c = 1; c <= LPoly.Length; ++c) list.Add(("n" + c, Chamfered(values[0] * delta, c)));
        if (kind == "shift") foreach (double x in values) list.Add(("x" + x.ToString(System.Globalization.CultureInfo.InvariantCulture), Bound.MiteredOffset(reference, x * delta, out double res)));
        foreach (var (variant, m) in list) {
            var common = new Dictionary<string, object>();
            common["file"] = item; common["variant"] = variant; common["ref"] = "P"; common["delta"] = delta; common["tris"] = m.TriangleCount;
            Rows(common, new Cached(m), rc, delta);
        }
    }

    // ---- the metric under similarities of both meshes ----

    static void Pose(string item)
    {
        Wraps(item.Substring(5), out DMesh3 input, out DMesh3 oracle, out Bound.Network net, out double alpha, out double delta, out DMesh3 raw, out DMesh3 a, out DMesh3 k, out long msA, out long msK);
        var rot = new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37);
        var poses = new (string, Func<Vector3d, Vector3d>, double)[] {
            ("identity", p => p, 1),
            ("scale 2^10", p => 1024 * p, 1024),
            ("scale 2^-10", p => p / 1024, 1.0 / 1024),
            ("scale 1e-3", p => 1e-3 * p, 1e-3),
            ("rotated", p => rot * p, 1),
            ("utm", p => p + new Vector3d(632117.25, 5712043.5, 113.75), 1),
            ("rotated, 1e-3, utm", p => 1e-3 * (rot * p) + new Vector3d(632117.25, 5712043.5, 113.75), 1e-3),
            ("far 1e6", p => p + new Vector3d(1e6, -2e6, 3e6), 1),
        };
        foreach (var (pname, f, scale) in poses) {
            DMesh3 s = new DMesh3(a, true), t = new DMesh3(oracle, true);
            MeshTransforms.PerVertexTransform(s, f); MeshTransforms.PerVertexTransform(t, f);
            var common = new Dictionary<string, object>();
            common["file"] = item; common["variant"] = pname; common["ref"] = "O"; common["delta"] = delta * scale; common["tris"] = s.TriangleCount;
            Rows(common, new Cached(s), new Cached(t), delta * scale);
        }
    }

    // ---- self test: closed form and convergence ----

    // int_0^L int_0^L exp(-(x - y)^2 / sigma^2) dx dy = 2 int_0^L (L - u) exp(-u^2 / sigma^2) du, by Simpson's rule
    static double Line(double L, double sigma)
    {
        int n = 200000;
        double h = L / n, sum = 0;
        for (int i = 0; i <= n; ++i) {
            double u = i * h, f = (L - u) * Math.Exp(-u * u / (sigma * sigma));
            sum += (i == 0 || i == n) ? f : (i % 2 == 1 ? 4 * f : 2 * f);
        }
        return 2 * sum * h / 3;
    }

    static DMesh3 Square(double L, double z, bool up, int n, double jitter)
    {
        // an L x L square at height z, n x n cells, inner vertices moved in the plane (an irregular triangulation)
        var m = new DMesh3();
        var rnd = new Random(12345);
        for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) {
            double x = i * L / n, y = j * L / n;
            if (i > 0 && i < n && j > 0 && j < n) { x += jitter * (rnd.NextDouble() - 0.5) * L / n; y += jitter * (rnd.NextDouble() - 0.5) * L / n; }
            m.AppendVertex(new Vector3d(x, y, z));
        }
        for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
            int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
            if (up) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); } else { m.AppendTriangle(a, d, b); m.AppendTriangle(a, c, d); }
        }
        return m;
    }

    static int SelfTest()
    {
        int bad = 0;
        double L = 1, sigma = 0.1;
        double exact = Line(L, sigma); exact *= exact;
        Console.WriteLine("closed form: two parallel L x L squares at distance g, <S,T> = I(L, sigma)^2 exp(-g^2 / sigma^2) k(+-1)");
        foreach (double g in new[] { 0, 0.05, 0.1, 0.2 })
            foreach (bool flip in new[] { false, true })
                foreach (double qq in new[] { 0.5, 1, 2, 4 }) {
                    // one square with 2 triangles, the other an irregular 7 x 7 triangulation
                    var s = VarifoldMeasure.Sample(Square(L, 0, true, 1, 0), sigma / qq);
                    var t = VarifoldMeasure.Sample(Square(L, g, !flip, 7, 0.6), sigma / qq);
                    double[] st = VarifoldMeasure.Inner(s, t, sigma);
                    double e = exact * Math.Exp(-g * g / (sigma * sigma)), sign = flip ? -1 : 1, same = flip ? 0 : e;
                    double err = new[] { st[0] - e, st[1] - sign * e, st[2] - e, st[3] - same, st[4] - same }.Max(v => Math.Abs(v)) / exact;
                    Console.WriteLine($"  g={g} flip={flip} q={qq} points={s.Count}/{t.Count} relative error {err:E2}");
                    if (qq >= 1 && err > 1e-4) bad++;
                    var r = new Dictionary<string, object>();
                    r["file"] = "selftest"; r["variant"] = "squares"; r["g"] = g; r["flip"] = flip; r["q"] = qq; r["error"] = err;
                    File.AppendAllText(outFile, JsonSerializer.Serialize(r) + "\n");
                }
        return bad;
    }
}
