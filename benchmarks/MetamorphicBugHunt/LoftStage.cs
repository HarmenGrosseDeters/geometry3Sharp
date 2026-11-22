using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// ContourLoft and ContourLoftStack. Relations: a Success with caps is a solid (independent check, NotCleanReason) between the
    /// two planes; the band is the same under a rotation of the start vertex of either contour, under swapping the contours, under
    /// reversing a contour and under a mirror; 2^k gives the scaled mesh bit for bit; other similarities keep the status, the
    /// counts and the cost (times scale^2); a second Compute() on the instance gives the same mesh; a stack of two slices with one
    /// contour each is the single loft; a stack is the same solid with its slices given as rotated lists and with a far copy.
    /// </summary>
    public static class LoftStage
    {
        const string S = "loft";

        static List<Vector3d> At(IEnumerable<Vector2d> poly, double z, Vector2d shift = default(Vector2d), double scale = 1) =>
            poly.Select(p => new Vector3d(scale * p.x + shift.x, scale * p.y + shift.y, z)).ToList();

        static List<Vector3d> Rot(List<Vector3d> c, int k) { int n = c.Count; return Enumerable.Range(0, n).Select(i => c[(i + k) % n]).ToList(); }
        static List<Vector3d> Rev(List<Vector3d> c) { var r = new List<Vector3d>(c); r.Reverse(); return r; }
        static List<Vector3d> Map(List<Vector3d> c, Func<Vector3d, Vector3d> f) => c.Select(f).ToList();

        static List<Vector2d> Blob(int n, double r, double wobble, int lobes, double phase)
        {
            var l = new List<Vector2d>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                double rr = r * (1 + wobble * Math.Cos(lobes * a + phase));
                l.Add(new Vector2d(rr * Math.Cos(a), rr * Math.Sin(a)));
            }
            return l;
        }

        /// <summary> the triangles of a mesh as sorted position triples: equal for the same surface whatever the ids </summary>
        public static List<string> Canonical(DMesh3 m, Func<Vector3d, Vector3d> back = null)
        {
            var l = new List<string>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                var p = new[] { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) };
                if (back != null) for (int k = 0; k < 3; ++k) p[k] = back(p[k]);
                var s = p.Select(q => $"{q.x:R},{q.y:R},{q.z:R}").ToArray();
                // rotate so that the smallest string is first (keeps the orientation)
                int best = 0; for (int k = 1; k < 3; ++k) if (string.CompareOrdinal(s[k], s[best]) < 0) best = k;
                l.Add(s[best] + " | " + s[(best + 1) % 3] + " | " + s[(best + 2) % 3]);
            }
            l.Sort(string.CompareOrdinal);
            return l;
        }

        public static string SameSurface(DMesh3 a, DMesh3 b, Func<Vector3d, Vector3d> backB = null)
        {
            if (a == null || b == null) return a == b ? "" : "one is null";
            if (a.TriangleCount != b.TriangleCount) return $"triangles {a.TriangleCount} vs {b.TriangleCount}";
            var x = Canonical(a); var y = Canonical(b, backB);
            for (int i = 0; i < x.Count; ++i) if (x[i] != y[i]) return "triangle sets differ, first " + x[i] + "  vs  " + y[i];
            return "";
        }

        static string SameIds(DMesh3 a, DMesh3 b, double scale = 1)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"counts {a.VertexCount}/{a.TriangleCount} vs {b.VertexCount}/{b.TriangleCount}";
            foreach (int v in a.VertexIndices()) { Vector3d p = a.GetVertex(v) * scale, q = b.GetVertex(v); if (p.x != q.x || p.y != q.y || p.z != q.z) return $"vertex {v}: {p} vs {q}"; }
            foreach (int t in a.TriangleIndices()) { Index3i x = a.GetTriangle(t), y = b.GetTriangle(t); if (x.a != y.a || x.b != y.b || x.c != y.c) return "triangle " + t; }
            return "";
        }

        static (string, List<Vector3d>, List<Vector3d>)[] Pairs()
        {
            return new[] {
                ("square-square", At(Shapes.Ngon(4, 1, 0.3), 0), At(Shapes.Ngon(4, 1, 0.3), 1)),
                ("hexagon-triangle", At(Shapes.Ngon(6, 1, 0.1), 0), At(Shapes.Ngon(3, 0.7, 0.4), 0.8)),
                ("circle-small circle", At(Shapes.Ngon(24, 1, 0.05), 0), At(Shapes.Ngon(17, 0.5, 0.3), 1.2)),
                ("blob-blob", At(Blob(40, 1, 0.2, 3, 0.3), 0), At(Blob(31, 0.9, 0.15, 5, 1.1), 0.7)),
                ("star-circle", At(Shapes.Star(5, 1.0, 0.55, 0.2), 0), At(Shapes.Ngon(16, 0.8, 0.1), 1.0)),
                ("offset circles", At(Shapes.Ngon(20, 1, 0.07), 0), At(Shapes.Ngon(20, 1, 0.21), 1.5, new Vector2d(0.6, 0.2))),
                ("tilted pair", Map(At(Shapes.Ngon(12, 1, 0.11), 0), p => Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 40) * p), Map(At(Blob(15, 0.8, 0.1, 2, 0.5), 1), p => Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 40) * p)),
                ("dense-sparse", At(Shapes.Ngon(64, 1, 0.013), 0), At(Shapes.Ngon(5, 0.9, 0.5), 0.5)),
            };
        }

        static ContourLoft Make(ContourLoftCost cost, bool caps, bool normalize) => new ContourLoft { Cost = cost, Caps = caps, NormalizeForCost = normalize };

        static void Single()
        {
            foreach (var (name, a, b) in Pairs())
            foreach (ContourLoftCost cost in new[] { ContourLoftCost.MinimumArea, ContourLoftCost.MaximumVolume })
            foreach (bool normalize in new[] { false, true })
            foreach (bool caps in new[] { true, false }) {
                string c = $"{name} | {cost}{(normalize ? " normalized" : "")}{(caps ? " caps" : "")}";
                try {
                    var loft = Make(cost, caps, normalize);
                    ContourLoftResult r = Program.Timed(() => loft.Compute(a, b), out long ms);
                    if (r.Status != ContourLoftStatus.Success) { Program.Info(S, c, r.Status + " " + r.Reason); Program.Report(S, c + " | no mesh on failure", r.Mesh == null ? "" : "a mesh on " + r.Status);
                        // the status must be the same for every equivalent input
                        ContourLoftResult rr = Make(cost, caps, normalize).Compute(Rot(a, 2), Rot(b, 1));
                        Program.Report(S, c + " | failure status under start rotation", rr.Status == r.Status ? "" : rr.Status + " vs " + r.Status);
                        rr = Make(cost, caps, normalize).Compute(b, a);
                        Program.Report(S, c + " | failure status swapped", rr.Status == r.Status ? "" : rr.Status + " vs " + r.Status);
                        continue; }
                    Program.Report(S, c + " | time", "", ms);
                    DMesh3 m = r.Mesh;
                    int na = a.Count, nb = b.Count;
                    // vertex layout
                    string lay = "";
                    for (int i = 0; i < na && lay == ""; ++i) if (m.GetVertex(i) != a[i]) lay = "vertex " + i + " is not First[i]";
                    for (int j = 0; j < nb && lay == ""; ++j) if (m.GetVertex(na + j) != b[j]) lay = "vertex m + " + j + " is not Second[j]";
                    Program.Report(S, c + " | vertex layout", lay);
                    if (caps) {
                        Program.Report(S, c + " | solid", Checks.Solid(m));
                        Program.Report(S, c + " | outward", Checks.Volume(m) > 0 ? "" : "volume " + Checks.Volume(m));
                        Program.Report(S, c + " | Euler", Checks.Euler(m) == 2 ? "" : "Euler " + Checks.Euler(m));
                    } else {
                        Program.Report(S, c + " | band triangles", m.TriangleCount == na + nb ? "" : $"{m.TriangleCount} vs {na + nb}");
                        // the boundary of the band is the two contours: every contour edge once, every other edge twice
                        var cnt = new Dictionary<(int, int), int>();
                        foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); for (int k = 0; k < 3; ++k) { int u = tv[k], v = tv[(k + 1) % 3]; var e = u < v ? (u, v) : (v, u); cnt.TryGetValue(e, out int x); cnt[e] = x + 1; } }
                        string be = "";
                        foreach (var kv in cnt) {
                            bool onA = kv.Key.Item1 < na && kv.Key.Item2 < na, onB = kv.Key.Item1 >= na && kv.Key.Item2 >= na;
                            if ((onA || onB) && kv.Value != 1) be = $"contour edge {kv.Key} used {kv.Value}";
                            if (!(onA || onB) && kv.Value != 2) be = $"spanning edge {kv.Key} used {kv.Value}";
                        }
                        Program.Report(S, c + " | band edges", be);
                        Program.Report(S, c + " | finite cost", !double.IsNaN(r.TotalCost) && !double.IsInfinity(r.TotalCost) ? "" : "cost " + r.TotalCost);
                        if (cost == ContourLoftCost.MinimumArea && !normalize) Program.Report(S, c + " | cost is the area", Program.Rel("area", Checks.Area(m), r.TotalCost, 1e-12));
                    }
                    // reuse
                    ContourLoftResult again = loft.Compute(a, b);
                    Program.Report(S, c + " | reuse", again.Status == r.Status ? SameIds(m, again.Mesh) : "status " + again.Status);
                    // start rotation of either contour: the same surface, the same cost
                    foreach (var (ka, kb) in new[] { (1, 0), (0, 1), (na / 2, nb - 1) }) {
                        ContourLoftResult q = Make(cost, caps, normalize).Compute(Rot(a, ka), Rot(b, kb));
                        string tag = c + $" | start rotation {ka},{kb}";
                        if (q.Status != r.Status) { Program.Report(S, tag, "status " + q.Status + " " + q.Reason); continue; }
                        Program.Report(S, tag + " cost", Program.Rel("cost", q.TotalCost, r.TotalCost, 1e-12));
                        if (!caps) { string same = SameSurface(m, q.Mesh); if (same != "" && Math.Abs(q.TotalCost - r.TotalCost) <= 1e-12 * Math.Abs(r.TotalCost)) Program.Info(S, tag + " surface", "another band of the same cost: " + same.Substring(0, Math.Min(80, same.Length))); else Program.Report(S, tag + " surface", same); }
                        else Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), Checks.Volume(m), 1e-9));
                    }
                    // swapped contours
                    {
                        ContourLoftResult q = Make(cost, caps, normalize).Compute(b, a);
                        string tag = c + " | swapped";
                        if (q.Status != r.Status) Program.Report(S, tag, "status " + q.Status + " " + q.Reason);
                        else {
                            Program.Report(S, tag + " cost", Program.Rel("cost", q.TotalCost, r.TotalCost, 1e-12));
                            if (caps) { Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), Checks.Volume(m), 1e-9)); Program.Report(S, tag + " solid", Checks.Solid(q.Mesh)); }
                        }
                    }
                    // reversed contour(s)
                    foreach (var (ra, rb) in new[] { (true, false), (false, true), (true, true) }) {
                        ContourLoftResult q = Make(cost, caps, normalize).Compute(ra ? Rev(a) : a, rb ? Rev(b) : b);
                        string tag = c + $" | reversed {(ra ? "first" : "")}{(rb ? " second" : "")}";
                        if (q.Status != r.Status) { Program.Report(S, tag, "status " + q.Status + " " + q.Reason); continue; }
                        Program.Report(S, tag + " cost", Program.Rel("cost", q.TotalCost, r.TotalCost, 1e-12));
                        if (caps) { Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), Checks.Volume(m), 1e-9)); Program.Report(S, tag + " solid", Checks.Solid(q.Mesh)); }
                    }
                    // mirror x
                    {
                        Func<Vector3d, Vector3d> mir = p => new Vector3d(-p.x, p.y, p.z);
                        ContourLoftResult q = Make(cost, caps, normalize).Compute(Map(a, mir), Map(b, mir));
                        string tag = c + " | mirror";
                        if (q.Status != r.Status) Program.Report(S, tag, "status " + q.Status + " " + q.Reason);
                        else {
                            Program.Report(S, tag + " cost", Program.Rel("cost", q.TotalCost, r.TotalCost, 1e-12));
                            if (caps) { Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), Checks.Volume(m), 1e-9)); Program.Report(S, tag + " outward", Checks.Volume(q.Mesh) > 0 ? "" : "inward"); }
                        }
                    }
                    // 2^k
                    foreach (double s in new[] { 1.0 / 1024, 1024.0, Math.Pow(2, -200), Math.Pow(2, 200) }) {
                        ContourLoftResult q = Make(cost, caps, normalize).Compute(Map(a, p => p * s), Map(b, p => p * s));
                        string tag = c + " | scale 2^" + Math.Log(s, 2);
                        if (q.Status != r.Status) { if (caps && Math.Abs(Math.Log(s, 2)) > 100) Program.Info(S, tag, q.Status + " " + q.Reason); else Program.Report(S, tag, "status " + q.Status + " " + q.Reason); continue; }
                        Program.Report(S, tag + " bit for bit", SameIds(m, q.Mesh, s));
                    }
                    // similarities
                    foreach (var (sn, f, sc) in Sims()) {
                        ContourLoftResult q = Make(cost, caps, normalize).Compute(Map(a, f), Map(b, f));
                        string tag = c + " | " + sn;
                        if (q.Status != r.Status) { Program.Report(S, tag, "status " + q.Status + " " + q.Reason); continue; }
                        Program.Report(S, tag + " cost", Program.Rel("cost", q.TotalCost, r.TotalCost * (cost == ContourLoftCost.MinimumArea && !normalize ? sc * sc : (normalize ? 1 : sc * sc * sc)), 1e-6));
                        if (caps) { Program.Report(S, tag + " solid", Checks.Solid(q.Mesh)); Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), Checks.Volume(m) * sc * sc * sc, 1e-6)); }
                    }
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        public static (string, Func<Vector3d, Vector3d>, double)[] Sims()
        {
            Matrix3d r1 = Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);
            Matrix3d r2 = Matrix3d.AxisAngleD(Vector3d.AxisY, 23);
            var utm = new Vector3d(632117.25, 5712043.5, 113.75);
            return new (string, Func<Vector3d, Vector3d>, double)[] {
                ("utm", p => p + utm, 1),
                ("scale 1e-3", p => p * 1e-3, 1e-3),
                ("scale 1e3", p => p * 1e3, 1e3),
                ("rotated", p => r1 * p, 1),
                ("rotated 1e-3 utm", p => 1e-3 * (r2 * p) + utm, 1e-3),
            };
        }

        static IList<IList<IList<Vector3d>>> Slices(params List<Vector3d>[][] s) => s.Select(sl => (IList<IList<Vector3d>>)sl.Select(c => (IList<Vector3d>)c).ToList()).ToList();

        static void Stack()
        {
            var cases = new (string, List<Vector3d>[][])[] {
                ("column", new[] { new[] { At(Shapes.Ngon(12, 1, 0.1), 0) }, new[] { At(Shapes.Ngon(10, 0.8, 0.3), 1) }, new[] { At(Shapes.Ngon(14, 1.1, 0.2), 2) } }),
                ("branch", new[] { new[] { At(Shapes.Ngon(24, 2, 0.1), 0) }, new[] { At(Shapes.Ngon(12, 0.7, 0.1), 1, new Vector2d(-1, 0)), At(Shapes.Ngon(12, 0.7, 0.2), 1, new Vector2d(1, 0)) } }),
                ("merge", new[] { new[] { At(Shapes.Ngon(12, 0.7, 0.1), 0, new Vector2d(-1, 0)), At(Shapes.Ngon(12, 0.7, 0.2), 0, new Vector2d(1, 0)) }, new[] { At(Shapes.Ngon(24, 2, 0.1), 1) } }),
                ("tube", new[] { new[] { At(Shapes.Ngon(16, 2, 0.1), 0), At(Shapes.Ngon(10, 1, 0.3), 0) }, new[] { At(Shapes.Ngon(16, 2.1, 0.2), 1), At(Shapes.Ngon(9, 0.9, 0.1), 1) }, new[] { At(Shapes.Ngon(18, 1.9, 0.0), 2), At(Shapes.Ngon(11, 1.0, 0.2), 2) } }),
                ("ending", new[] { new[] { At(Shapes.Ngon(12, 1, 0.1), 0), At(Shapes.Ngon(8, 0.5, 0.1), 0, new Vector2d(4, 0)) }, new[] { At(Shapes.Ngon(12, 1, 0.1), 1) }, new[] { At(Shapes.Ngon(12, 1, 0.3), 2), At(Shapes.Ngon(8, 0.5, 0.2), 2, new Vector2d(-4, 0)) } }),
                ("two columns", new[] { new[] { At(Shapes.Ngon(8, 1, 0.1), 0), At(Shapes.Ngon(9, 1, 0.1), 0, new Vector2d(5, 0)) }, new[] { At(Shapes.Ngon(7, 1, 0.2), 1), At(Shapes.Ngon(10, 1, 0.3), 1, new Vector2d(5, 0)) } }),
            };
            foreach (var (name, sl) in cases)
            foreach (bool saddle in new[] { false, true }) {
                string c = "stack " + name + (saddle ? " saddle" : "");
                try {
                    Func<ContourLoftStack> make = () => new ContourLoftStack { CapEnds = true, LoweredSaddle = saddle };
                    ContourLoftStack st = make();
                    ContourLoftStackResult r = Program.Timed(() => st.Compute(Slices(sl)), out long ms);
                    if (r.Status != ContourLoftStackStatus.Success) { Program.Info(S, c, r.Status + " " + r.Reason); Program.Report(S, c + " | no mesh on failure", r.Mesh == null ? "" : "a mesh on " + r.Status); continue; }
                    Program.Report(S, c + " | time", "", ms);
                    DMesh3 m = r.Mesh;
                    Program.Report(S, c + " | solid", Checks.Solid(m));
                    Program.Report(S, c + " | outward", Checks.Volume(m) > 0 ? "" : "volume " + Checks.Volume(m));
                    int k = 0; string lay = "";
                    foreach (var slice in sl) foreach (var con in slice) foreach (Vector3d p in con) { if (lay == "" && m.GetVertex(k) != p) lay = "vertex " + k; ++k; }
                    Program.Report(S, c + " | vertex layout", lay);
                    ContourLoftStackResult again = st.Compute(Slices(sl));
                    Program.Report(S, c + " | reuse", again.Status == r.Status ? SameIds(m, again.Mesh) : "status " + again.Status);
                    double vol = Checks.Volume(m);
                    // contour order inside a slice, start vertices, direction
                    var variants = new (string, Func<List<Vector3d>[][]>)[] {
                        ("contours reversed in the lists", () => sl.Select(s => s.Reverse().ToArray()).ToArray()),
                        ("start vertices", () => sl.Select(s => s.Select((con, i) => Rot(con, 1 + i)).ToArray()).ToArray()),
                        ("directions", () => sl.Select(s => s.Select(con => Rev(con)).ToArray()).ToArray()),
                        ("mirror", () => sl.Select(s => s.Select(con => Map(con, p => new Vector3d(-p.x, p.y, p.z))).ToArray()).ToArray()),
                    };
                    foreach (var (vn, vf) in variants) {
                        ContourLoftStackResult q = make().Compute(Slices(vf()));
                        string tag = c + " | " + vn;
                        if (q.Status != r.Status) { Program.Report(S, tag, "status " + q.Status + " " + q.Reason); continue; }
                        Program.Report(S, tag + " solid", Checks.Solid(q.Mesh));
                        Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), vol, 1e-9));
                        Program.Report(S, tag + " triangles", q.Mesh.TriangleCount == m.TriangleCount ? "" : $"{q.Mesh.TriangleCount} vs {m.TriangleCount}");
                    }
                    // upside down: the slices in reverse order, mirrored in z (increasing along the normal again)
                    {
                        var ud = sl.Reverse().Select(s => s.Select(con => Map(con, p => new Vector3d(p.x, p.y, -p.z))).ToArray()).ToArray();
                        ContourLoftStackResult q = make().Compute(Slices(ud));
                        string tag = c + " | upside down";
                        if (q.Status != r.Status) Program.Report(S, tag, "status " + q.Status + " " + q.Reason);
                        else { Program.Report(S, tag + " solid", Checks.Solid(q.Mesh)); Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), vol, 1e-9)); }
                    }
                    foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                        ContourLoftStackResult q = make().Compute(Slices(sl.Select(x => x.Select(con => Map(con, p => p * s)).ToArray()).ToArray()));
                        string tag = c + " | scale " + s;
                        if (q.Status != r.Status) { Program.Report(S, tag, "status " + q.Status + " " + q.Reason); continue; }
                        Program.Report(S, tag + " bit for bit", SameIds(m, q.Mesh, s));
                    }
                    foreach (var (sn, f, sc) in Sims()) {
                        ContourLoftStackResult q = make().Compute(Slices(sl.Select(x => x.Select(con => Map(con, f)).ToArray()).ToArray()));
                        string tag = c + " | " + sn;
                        if (q.Status != r.Status) { Program.Report(S, tag, "status " + q.Status + " " + q.Reason); continue; }
                        Program.Report(S, tag + " solid", Checks.Solid(q.Mesh));
                        Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), vol * sc * sc * sc, 1e-6));
                    }
                    // a far copy of every contour in every slice: twice the volume
                    {
                        var far = sl.Select(s => s.Concat(s.Select(con => Map(con, p => p + new Vector3d(0, 64, 0)))).ToArray()).ToArray();
                        ContourLoftStackResult q = make().Compute(Slices(far));
                        string tag = c + " | far copy";
                        if (q.Status != r.Status) Program.Report(S, tag, "status " + q.Status + " " + q.Reason);
                        else { Program.Report(S, tag + " solid", Checks.Solid(q.Mesh)); Program.Report(S, tag + " volume", Program.Rel("volume", Checks.Volume(q.Mesh), 2 * vol, 1e-9)); Program.Report(S, tag + " triangles", q.Mesh.TriangleCount == 2 * m.TriangleCount ? "" : $"{q.Mesh.TriangleCount} vs {2 * m.TriangleCount}"); }
                    }
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
            // a stack of two slices with one contour each is the single loft with caps
            foreach (var (name, a, b) in Pairs()) {
                string c = "stack = loft | " + name;
                try {
                    ContourLoftResult one = new ContourLoft { Caps = true }.Compute(a, b);
                    ContourLoftStackResult st = new ContourLoftStack { CapEnds = true }.Compute(Slices(new[] { a }, new[] { b }));
                    bool s1 = one.Status == ContourLoftStatus.Success, s2 = st.Status == ContourLoftStackStatus.Success;
                    if (s1 != s2) { Program.Info(S, c, $"loft {one.Status} {one.Reason}, stack {st.Status} {st.Reason}"); continue; }
                    if (!s1) continue;
                    string same = SameSurface(one.Mesh, st.Mesh);
                    if (same != "") Program.Info(S, c, "another triangulation (the caps of cocircular contours): " + same.Substring(0, Math.Min(60, same.Length)));
                    Program.Report(S, c + " | same triangle count", one.Mesh.TriangleCount == st.Mesh.TriangleCount ? "" : $"{one.Mesh.TriangleCount} vs {st.Mesh.TriangleCount}");
                    Program.Report(S, c + " | same volume", Program.Rel("volume", Checks.Volume(st.Mesh), Checks.Volume(one.Mesh), 1e-12));
                    Program.Report(S, c + " | same area", Program.Rel("area", Checks.Area(st.Mesh), Checks.Area(one.Mesh), 1e-12));
                }
                catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        public static void Run()
        {
            Single();
            Stack();
        }
    }
}
