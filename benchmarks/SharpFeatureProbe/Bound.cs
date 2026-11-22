// Upper-bound probe of the study "provably valid sharp-feature recovery on the alpha wrap" (HP 4, plan #1280;
// docs/research/PROVABLE_SHARP_WRAP_STUDY.md). No library change; prototypes only.
//
//   SharpFeatureProbe bound <out.jsonl> <case>... [-- variants]
//
// A case is a procedural CAD-like solid, optionally dirty and transformed: [soup:]<solid>[@rot|@far|@rotfar], or twobox[@...].
// Solids: the six of Procedural.cs, gear / finegear / microgear (12 / 60 / 160 teeth), fillet (a block with rounded vertical
// edges), wedge:<deg> (a V-notch of that opening angle), ridge:<deg> (a prism with a convex crease of that interior angle),
// notch:<k> (a slot k alphas wide and 1 deep). Every case has a clean closed *reference* (the solid the dirty input means).
//
// The oracle O is the mitered offset of the reference, built directly: every reference vertex moves by the shortest d with
// n_i . d = offset for the planes n_i of its triangles (capped at 4 offsets, as the feature pass caps its miter lines). Its
// sharp edges and corners are the feature network every variant is measured against, so O scores 0 / 1 by construction; what is
// measured on O is whether it is valid (closed, manifold, free of self-intersections, the input inside), ie where the direct
// construction needs no wrap at all.
//
// Variants:
//   A    the pipeline (MeshAlphaWrap 300 / 3000, simplification with the input kept clear)
//   K    AlphaWrapFeaturePass on the raw wrap, then the simplification (variant KR of the probe)
//   KC   K, then a corner step: for every corner of the network the nearest wrap vertices are offered the corner position with
//        the pass's own TryMove (every exact check of the pass applies), then the simplification
//   KCK  KC, then the unmodified pass a second time before the simplification (it rebuilds its vertex labels, so this is a
//        second round of the same pass, not a retry that knows the first round's crease vertices)
//   KO   as K, with the reference instead of the dirty input as the pass's input (features and clearance from the reference: the
//        bound for "the features of a dirty input are known"); the simplification keeps clear of the real input. Sound here
//        only because the dirty input lies in the reference's closed solid (the result is measured against the real input)
//   KOC  KO with the corner step
//   O    the oracle itself
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

static class Bound
{
    const double RelAlpha = 300, RelOffset = 3000, MiterCap = 4;
    static readonly double CosSharp = Math.Cos(30 * Math.PI / 180);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: SharpFeatureProbe bound <out.jsonl> <case>... [-- variants]"); return 2; }
        string outFile = args[1];
        var cases = new List<string>();
        string[] variants = { "A", "K", "KC", "KCK", "KO", "KOC", "O" };
        for (int i = 2; i < args.Length; ++i) {
            if (args[i] == "--") { if (i + 1 < args.Length) variants = args[i + 1].Split(','); break; }
            cases.Add(args[i]);
        }
        var done = new HashSet<string>();
        if (File.Exists(outFile))
            foreach (string line in File.ReadLines(outFile)) {
                if (!line.EndsWith("}")) continue;      // (a row cut off by an interrupted run)
                using (var doc = JsonDocument.Parse(line)) done.Add(doc.RootElement.GetProperty("file").GetString() + "|" + doc.RootElement.GetProperty("variant").GetString());
            }
        int failed = 0;
        foreach (string name in cases) {
            // (KO and KOC exist for dirty cases only)
            bool dirtyName = name.StartsWith("soup:") || name.StartsWith("twobox");
            if (variants.All(v => done.Contains(name + "|" + v) || (!dirtyName && v.StartsWith("KO")))) continue;
            DMesh3 input, reference; bool dirty;
            try { Make(name, out input, out reference, out dirty); } catch (Exception e) { Console.Error.WriteLine(name + ": " + e.Message); failed++; continue; }
            double diag = input.CachedBounds.DiagonalLength, alpha = diag / RelAlpha, delta = diag / RelOffset;
            DMesh3 oracle = MiteredOffset(reference, delta, out double residual);
            Network net = new Network(oracle, alpha);
            var sw = Stopwatch.StartNew();
            var wrap = new MeshAlphaWrap(input) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset };
            wrap.Compute();
            long rawMs = sw.ElapsedMilliseconds;
            bool wrapped = wrap.Result != null && wrap.Result.TriangleCount > 0 && wrap.Status == MeshAlphaWrap.WrapStatus.Success;
            if (wrapped) { alpha = wrap.UsedAlpha; delta = wrap.UsedOffset; }
            foreach (string v in variants) {
                if (done.Contains(name + "|" + v)) continue;
                if ((v == "KO" || v == "KOC") && !dirty) continue;
                var r = new Dictionary<string, object>();
                r["file"] = name; r["variant"] = v; r["dirty"] = dirty; r["alpha"] = alpha; r["delta"] = delta; r["diag"] = diag;
                r["input_tris"] = input.TriangleCount; r["oracle_residual"] = residual / delta;
                r["net_len"] = net.Length / diag; r["net_corners"] = net.Corners.Count;
                try {
                    DMesh3 result = null; long ms = 0;
                    if (v == "O") result = oracle;
                    else if (!wrapped) { r["status"] = "wrap_" + wrap.Status; }
                    else if (v == "A") { sw.Restart(); result = Program.Simplify(new DMesh3(wrap.Result, true), delta, input); ms = rawMs + sw.ElapsedMilliseconds; }
                    else {
                        sw.Restart();
                        var kp = new AlphaWrapFeaturePass(new DMesh3(wrap.Result, true), v.StartsWith("KO") ? reference : input, alpha, delta);
                        kp.Compute();
                        r["k_input_creases"] = kp.InputCreases; r["k_crease_snaps"] = kp.CreaseSnaps; r["k_splits"] = kp.Splits; r["k_flips"] = kp.Flips; r["k_rejected"] = kp.Rejected;
                        if (v.EndsWith("C") || v == "KCK") CornerStep(kp, net, alpha, delta, r);
                        if (v == "KCK") { kp.Compute(); r["k2_crease_snaps"] = kp.CreaseSnaps; r["k2_splits"] = kp.Splits; r["k2_flips"] = kp.Flips; }
                        r["k_ms"] = sw.ElapsedMilliseconds;
                        result = Program.Simplify(new DMesh3(kp.Mesh, true), delta, input);
                        ms = rawMs + sw.ElapsedMilliseconds;
                    }
                    if (result != null) { r["ms"] = ms; Measure(input, oracle, net, result, alpha, delta, r); r["status"] = "ok"; }
                } catch (Exception e) { r["status"] = "exception"; r["error"] = e.GetType().Name + ": " + e.Message; }
                File.AppendAllText(outFile, JsonSerializer.Serialize(r, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }) + "\n");
                Console.WriteLine(name + " " + v + " " + r["status"] + (r.ContainsKey("cov025") ? $" tris={r["tris"]} cov025={r["cov025"]:F3} fh_max={r["fh_max"]:F2} corner025={r["corner025"]:F2} clean={r["clean"]} cross={r["cross_input"]}" : ""));
            }
        }
        return failed == 0 ? 0 : 1;
    }

    // ---- cases ----

    internal static void Make(string name, out DMesh3 input, out DMesh3 reference, out bool dirty)
    {
        string xf = "";
        int at = name.IndexOf('@');
        if (at >= 0) { xf = name.Substring(at + 1); name = name.Substring(0, at); }
        dirty = false;
        if (name == "twobox") {
            // two overlapping boxes as two closed components; what is meant is their union (a polycube): its concave creases are
            // intersection lines of the components, not edges of the input
            Func<int, int, int, bool> a = (x, y, z) => x < 6 && y < 6 && z < 4, b = (x, y, z) => x >= 3 && y >= 2 && y < 8 && z >= 1;
            input = Procedural.PolyCube(10, 8, 6, a);
            MeshEditor.Append(input, Procedural.PolyCube(10, 8, 6, b));
            reference = Procedural.PolyCube(10, 8, 6, (x, y, z) => a(x, y, z) || b(x, y, z));
            dirty = true;
        } else if (name.StartsWith("soup:")) {
            // every triangle on its own (no shared vertices): the input has no edge with two triangles
            reference = Solid(name.Substring(5));
            input = new DMesh3();
            foreach (int tid in reference.TriangleIndices()) {
                Index3i t = reference.GetTriangle(tid);
                input.AppendTriangle(input.AppendVertex(reference.GetVertex(t.a)), input.AppendVertex(reference.GetVertex(t.b)), input.AppendVertex(reference.GetVertex(t.c)));
            }
            dirty = true;
        } else {
            reference = Solid(name);
            input = new DMesh3(reference, true);
        }
        if (xf == "") return;
        if (xf != "rot" && xf != "far" && xf != "rotfar") throw new ArgumentException(xf);
        foreach (DMesh3 m in new[] { input, reference }) {
            if (xf.StartsWith("rot")) MeshTransforms.Rotate(m, Vector3d.Zero, new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37));
            if (xf.EndsWith("far")) MeshTransforms.Translate(m, new Vector3d(1e6, -2e6, 3e6));
        }
    }

    static DMesh3 Solid(string name)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double alpha0 = Math.Sqrt(10 * 10 + 6 * 6 + 4 * 4) / RelAlpha;      // of the 10 x 6 x 4 blocks below
        if (name == "gear") return Gear(12);
        if (name == "finegear") return Gear(60);
        if (name == "microgear") return Gear(160);
        if (name == "fillet") {
            // 10 x 6 x 4 with the four vertical edges rounded at radius 1.5, 12 segments each (7.5 degrees: not sharp)
            var p = new List<Vector2d>();
            double[] cx = { 8.5, 8.5, 1.5, 1.5 }, cy = { 1.5, 4.5, 4.5, 1.5 };
            for (int c = 0; c < 4; ++c)
                for (int k = 0; k <= 12; ++k) { double t = (c - 1) * Math.PI / 2 + k * Math.PI / 24; p.Add(new Vector2d(cx[c] + 1.5 * Math.Cos(t), cy[c] + 1.5 * Math.Sin(t))); }
            return Procedural.Prism(p, 4);
        }
        if (name.StartsWith("wedge:")) {
            double h = 2 * Math.Tan(0.5 * double.Parse(name.Substring(6), inv) * Math.PI / 180);
            return Procedural.Prism(new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(5 + h, 6), new Vector2d(5, 4), new Vector2d(5 - h, 6), new Vector2d(0, 6) }, 4);
        }
        if (name.StartsWith("ridge:")) {
            double h = 6 * Math.Tan(0.5 * double.Parse(name.Substring(6), inv) * Math.PI / 180);
            return Procedural.Prism(new List<Vector2d> { new Vector2d(5 - h, 0), new Vector2d(5 + h, 0), new Vector2d(5, 6) }, 4);
        }
        if (name.StartsWith("notch:")) {
            double w = 0.5 * double.Parse(name.Substring(6), inv) * alpha0;
            return Procedural.Prism(new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(5 + w, 6), new Vector2d(5 + w, 5), new Vector2d(5 - w, 5), new Vector2d(5 - w, 6), new Vector2d(0, 6) }, 4);
        }
        if (name.StartsWith("fin:")) {
            // a thin wall k alphas thick and 3 high on a 10 x 3 base
            double w = 0.5 * double.Parse(name.Substring(4), inv) * alpha0;
            return Procedural.Prism(new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 3), new Vector2d(5 + w, 3), new Vector2d(5 + w, 6), new Vector2d(5 - w, 6), new Vector2d(5 - w, 3), new Vector2d(0, 3) }, 4);
        }
        if (name == "fslot") {
            // a slot 2 wide and 2 deep whose two bottom edges are filleted at radius 0.5, 8 segments each (11.25 degrees: not sharp)
            var p = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 6), new Vector2d(6, 6) };
            for (int k = 0; k <= 8; ++k) { double t = -k * Math.PI / 16; p.Add(new Vector2d(5.5 + 0.5 * Math.Cos(t), 4.5 + 0.5 * Math.Sin(t))); }
            for (int k = 0; k <= 8; ++k) { double t = -Math.PI / 2 - k * Math.PI / 16; p.Add(new Vector2d(4.5 + 0.5 * Math.Cos(t), 4.5 + 0.5 * Math.Sin(t))); }
            p.Add(new Vector2d(4, 6)); p.Add(new Vector2d(0, 6));
            return Procedural.Prism(p, 4);
        }
        return Procedural.Make(name);
    }

    // a spur-gear-like prism: n trapezoid teeth between the radii 4 and 5, 2 thick
    static DMesh3 Gear(int n)
    {
        var p = new List<Vector2d>();
        double step = 2 * Math.PI / n;
        for (int i = 0; i < n; ++i) {
            double t = i * step;
            foreach (var (f, rad) in new[] { (0.0, 4.0), (0.15, 5.0), (0.40, 5.0), (0.55, 4.0) })
                p.Add(new Vector2d(rad * Math.Cos(t + f * step), rad * Math.Sin(t + f * step)));
        }
        return Procedural.Prism(p, 2);
    }

    // ---- the oracle: the mitered offset of a clean polyhedron, by moving its vertices ----

    internal static DMesh3 MiteredOffset(DMesh3 reference, double delta, out double maxResidual)
    {
        var o = new DMesh3(reference, true);
        maxResidual = 0;
        double cosSame = Math.Cos(1 * Math.PI / 180);
        foreach (int vid in reference.VertexIndices()) {
            var ns = new List<Vector3d>();
            foreach (int tid in reference.VtxTrianglesItr(vid)) {
                Vector3d n = reference.GetTriNormal(tid);
                if (n.LengthSquared < 0.5 || ns.Any(m => m.Dot(n) > cosSame)) continue;
                ns.Add(n);
            }
            Vector3d d = Shift(ns, delta);
            foreach (Vector3d n in ns) maxResidual = Math.Max(maxResidual, Math.Abs(n.Dot(d) - delta));
            o.SetVertex(vid, reference.GetVertex(vid) + d);
        }
        return o;
    }

    // the shortest d with n_i . d = delta (least squares for more than three planes), capped at MiterCap offsets
    static Vector3d Shift(List<Vector3d> ns, double delta)
    {
        Vector3d d;
        if (ns.Count == 0) return Vector3d.Zero;
        if (ns.Count == 1) d = delta * ns[0];
        else if (ns.Count == 2) d = (ns[0] + ns[1]) * (delta / Math.Max(1 + ns[0].Dot(ns[1]), 1e-12));
        else {
            // normal equations (N^T N) d = N^T (delta 1); three independent planes: the corner point
            var m = new Matrix3d(); Vector3d rhs = Vector3d.Zero;
            double[] a = new double[9];
            foreach (Vector3d n in ns) {
                for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) a[3 * i + j] += n[i] * n[j];
                rhs += delta * n;
            }
            m = new Matrix3d(a);
            double det = m.Determinant;
            if (Math.Abs(det) < 1e-9) d = (ns[0] + ns[1]) * (delta / Math.Max(1 + ns[0].Dot(ns[1]), 1e-12));     // (planes through one line)
            else d = m.Inverse() * rhs;
        }
        double len = d.Length;
        return len > MiterCap * delta ? d * (MiterCap * delta / len) : d;
    }

    // ---- the feature network of the oracle: sharp edges (by class) and corners ----

    internal class Network
    {
        public List<EdgeClass.Seg> Convex = new List<EdgeClass.Seg>(), Concave = new List<EdgeClass.Seg>();
        public List<Vector3d> Corners = new List<Vector3d>();
        public EdgeClass.SegGrid Grid;
        public double Length;
        public Network(DMesh3 o, double alpha)
        {
            var dirs = new Dictionary<int, List<Vector3d>>();
            foreach (int eid in o.EdgeIndices()) {
                Index2i et = o.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) continue;
                Vector3d n1 = o.GetTriNormal(et.a), n2 = o.GetTriNormal(et.b);
                if (n1.LengthSquared < 0.5 || n2.LengthSquared < 0.5 || n1.Dot(n2) >= CosSharp) continue;
                Index2i ev = o.GetEdgeV(eid);
                Vector3d a = o.GetVertex(ev.a), b = o.GetVertex(ev.b);
                Index3i t2 = o.GetTriangle(et.b);
                int opp = (t2.a != ev.a && t2.a != ev.b) ? t2.a : ((t2.b != ev.a && t2.b != ev.b) ? t2.b : t2.c);
                (n1.Dot(o.GetVertex(opp) - a) < 0 ? Convex : Concave).Add(new EdgeClass.Seg { A = a, B = b });
                Length += a.Distance(b);
                foreach (var (v, w) in new[] { (ev.a, b - a), (ev.b, a - b) }) {
                    if (!dirs.TryGetValue(v, out var l)) dirs[v] = l = new List<Vector3d>();
                    l.Add(w.Normalized);
                }
            }
            // a corner: a vertex of the network that is not the inner point of a straight crease
            foreach (var kv in dirs.OrderBy(kv => kv.Key))
                if (kv.Value.Count != 2 || kv.Value[0].Dot(kv.Value[1]) > -CosSharp) Corners.Add(o.GetVertex(kv.Key));
            var all = Convex.Concat(Concave).ToList();
            if (all.Count > 0) Grid = new EdgeClass.SegGrid(all, Math.Max(alpha, 1e-12), o.CachedBounds.Min - 4 * alpha * Vector3d.One);
        }
    }

    // ---- prototype: corners with the pass's own checked move ----

    static void CornerStep(AlphaWrapFeaturePass kp, Network net, double alpha, double delta, Dictionary<string, object> r)
    {
        DMesh3 m = kp.Mesh;
        int moved = 0, already = 0, tries = 0;
        foreach (Vector3d c in net.Corners) {
            // the six nearest vertices within 1.5 alpha, nearest first (ties by id: deterministic)
            var cand = new List<(double d2, int vid)>();
            foreach (int vid in m.VertexIndices()) {
                double d2 = m.GetVertex(vid).DistanceSquared(c);
                if (d2 <= 2.25 * alpha * alpha) cand.Add((d2, vid));
            }
            cand.Sort();
            if (cand.Count > 0 && cand[0].d2 <= 1e-6 * delta * delta) { already++; continue; }
            for (int k = 0; k < Math.Min(6, cand.Count); ++k) {
                tries++;
                if (kp.TryMove(cand[k].vid, c)) { moved++; break; }
            }
        }
        r["corner_moved"] = moved; r["corner_already"] = already; r["corner_tries"] = tries;
    }

    // ---- metrics against the network (distances in offsets) ----

    static List<Vector3d> Sample(List<EdgeClass.Seg> segs, int n)
    {
        var pts = new List<Vector3d>();
        double total = 0;
        foreach (var s in segs) total += s.A.Distance(s.B);
        if (!(total > 0)) return pts;
        double step = total / n, next = 0.5 * step, acc = 0;
        foreach (var s in segs) {
            double len = s.A.Distance(s.B);
            while (next < acc + len) { pts.Add(Vector3d.Lerp(s.A, s.B, (next - acc) / len)); next += step; }
            acc += len;
        }
        return pts;
    }

    static List<EdgeClass.Seg> SharpEdges(DMesh3 m)
    {
        var list = new List<EdgeClass.Seg>();
        foreach (int eid in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Vector3d n1 = m.GetTriNormal(et.a), n2 = m.GetTriNormal(et.b);
            if (n1.LengthSquared < 0.5 || n2.LengthSquared < 0.5 || n1.Dot(n2) >= CosSharp) continue;
            Index2i ev = m.GetEdgeV(eid);
            list.Add(new EdgeClass.Seg { A = m.GetVertex(ev.a), B = m.GetVertex(ev.b) });
        }
        return list;
    }

    static void Coverage(List<Vector3d> pts, EdgeClass.SegGrid grid, DMeshAABBTree3 surface, double delta, double clamp, string suffix, Dictionary<string, object> r)
    {
        if (pts.Count == 0) return;
        var ds = new double[pts.Count]; var dsurf = new double[pts.Count];
        gParallel.ForEach(Interval1i.Range(pts.Count), (i) => {
            ds[i] = (grid == null ? clamp : grid.Nearest(pts[i], clamp)) / delta;
            if (surface != null) { double d2; surface.FindNearestTriangle(pts[i], out d2); dsurf[i] = Math.Sqrt(d2) / delta; }
        });
        int n = pts.Count;
        r["cov01" + suffix] = ds.Count(d => d <= 0.1) / (double)n;
        r["cov025" + suffix] = ds.Count(d => d <= 0.25) / (double)n;
        r["cov1" + suffix] = ds.Count(d => d <= 1) / (double)n;
        var sorted = ds.OrderBy(d => d).ToArray();
        r["fh_max" + suffix] = sorted[n - 1]; r["fh_p95" + suffix] = sorted[(int)(0.95 * (n - 1))]; r["fh_mean" + suffix] = ds.Average();
        if (surface != null) { r["surf_max" + suffix] = dsurf.Max(); r["surf_mean" + suffix] = dsurf.Average(); }
    }

    internal static void Measure(DMesh3 input, DMesh3 oracle, Network net, DMesh3 result, double alpha, double delta, Dictionary<string, object> r)
    {
        r["tris"] = result.TriangleCount;
        r["not_clean"] = MeshRobustRepair.NotCleanReasons(result, true);
        r["clean"] = (string)r["not_clean"] == "";
        r["cross_input"] = EdgeClass.Crossings(result, input, alpha);
        var inputTree = new DMeshAABBTree3(input, true);
        var resTree = new DMeshAABBTree3(result, true);
        // band: the result's sample points (corners, edge midpoints, centroids) against the input; a vertex inside the input's solid
        double dmin = double.MaxValue, dmax = 0, minAngle = 180; int inside = 0;
        foreach (int tid in result.TriangleIndices()) {
            Triangle3d t = Snap.Tri(result, tid);
            minAngle = Math.Min(minAngle, Snap.MinAngle(t.V0, t.V1, t.V2));
            foreach (Vector3d p in new[] { t.V0, t.V1, t.V2, 0.5 * (t.V0 + t.V1), 0.5 * (t.V1 + t.V2), 0.5 * (t.V2 + t.V0), (t.V0 + t.V1 + t.V2) / 3 }) {
                double d2; inputTree.FindNearestTriangle(p, out d2);
                dmin = Math.Min(dmin, Math.Sqrt(d2)); dmax = Math.Max(dmax, Math.Sqrt(d2));
            }
        }
        foreach (int vid in result.VertexIndices()) if (inputTree.FastWindingNumber(result.GetVertex(vid)) > 0.5) inside++;
        r["band_min"] = dmin / delta; r["band_max"] = dmax / delta; r["verts_inside"] = inside; r["angle_min"] = minAngle;
        // recall: the network against the result's sharp edges (and against its surface)
        double clamp = 2 * alpha;
        var res = SharpEdges(result);
        var resGrid = res.Count > 0 ? new EdgeClass.SegGrid(res, Math.Max(alpha, 1e-12), oracle.CachedBounds.Min - 4 * alpha * Vector3d.One) : null;
        Coverage(Sample(net.Convex.Concat(net.Concave).ToList(), 20000), resGrid, resTree, delta, clamp, "", r);
        Coverage(Sample(net.Convex, 20000), resGrid, null, delta, clamp, "_cvx", r);
        Coverage(Sample(net.Concave, 20000), resGrid, null, delta, clamp, "_ccv", r);
        // precision: the result's sharp edges against the network
        var back = Sample(res, 20000);
        if (back.Count > 0 && net.Grid != null) {
            var ds = new double[back.Count];
            gParallel.ForEach(Interval1i.Range(back.Count), (i) => { ds[i] = net.Grid.Nearest(back[i], clamp) / delta; });
            r["prec025"] = ds.Count(d => d <= 0.25) / (double)ds.Length; r["rh_max"] = ds.Max();
        }
        // corners: the nearest result vertex
        if (net.Corners.Count > 0) {
            int near = 0; double worst = 0, sum = 0;
            foreach (Vector3d c in net.Corners) {
                double best = double.MaxValue;
                foreach (int vid in result.VertexIndices()) best = Math.Min(best, result.GetVertex(vid).DistanceSquared(c));
                double d = Math.Sqrt(best) / delta;
                if (d <= 0.25) near++; worst = Math.Max(worst, d); sum += d;
            }
            r["corner025"] = near / (double)net.Corners.Count; r["corner_max"] = worst; r["corner_mean"] = sum / net.Corners.Count;
        } else r["corner025"] = double.NaN;
        // the surfaces: the result's vertices against the oracle, and the oracle's against the result
        var oTree = new DMeshAABBTree3(oracle, true);
        double h1 = 0, h2 = 0;
        foreach (int vid in result.VertexIndices()) { double d2; oTree.FindNearestTriangle(result.GetVertex(vid), out d2); h1 = Math.Max(h1, Math.Sqrt(d2)); }
        foreach (int vid in oracle.VertexIndices()) { double d2; resTree.FindNearestTriangle(oracle.GetVertex(vid), out d2); h2 = Math.Max(h2, Math.Sqrt(d2)); }
        r["h_to_oracle"] = h1 / delta; r["h_from_oracle"] = h2 / delta;
    }
}
