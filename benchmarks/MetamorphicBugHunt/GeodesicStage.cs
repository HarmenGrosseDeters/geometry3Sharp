using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// The geodesic family: PolyhedralGeodesicDistance (and TracePath), MeshGeodesicDistance (heat), MeshGeodesicPath (FlipOut),
    /// MeshGeodesicBezier, VectorHeatMethod. Relations: the polyhedral distance is a metric of the surface, so it is symmetric,
    /// obeys the triangle inequality, lies between the straight distance and the edge-path distance, does not change when
    /// triangles are subdivided in their planes, when the mesh is permuted, flipped or gets a far second component, and
    /// d(A u B) = min(d(A), d(B)); 2^k gives the same bits; a traced path has the length of the distance and lies on the surface.
    /// The heat distance is within its documented band of the polyhedral one; a FlipOut path is never shorter than the polyhedral
    /// distance and never longer than its initial edge path.
    /// </summary>
    public static class GeodesicStage
    {
        const string S = "geodesic";

        static DMesh3 Terrain(int n, double amp, bool alternate)
        {
            var m = new DMesh3();
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                double x = i / (double)(n - 1), y = j / (double)(n - 1);
                m.AppendVertex(new Vector3d(x, y, amp * (Math.Sin(5 * x) * Math.Cos(4 * y) + 0.3 * Math.Sin(13 * x * y))));
            }
            for (int j = 0; j + 1 < n; ++j) for (int i = 0; i + 1 < n; ++i) {
                int a = j * n + i, b = a + 1, c = a + n + 1, d = a + n;
                if (alternate && ((i + j) & 1) == 1) { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
                else { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
            }
            return m;
        }

        /// <summary> every triangle split into four at its edge midpoints: the same piecewise flat surface, the old vertex ids kept </summary>
        public static DMesh3 Subdivide(DMesh3 mesh)
        {
            var m = new DMesh3();
            foreach (int v in mesh.VertexIndices()) { int id = m.AppendVertex(mesh.GetVertex(v)); if (id != v) throw new InvalidOperationException("not compact"); }
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b) { var k = a < b ? (a, b) : (b, a); if (!mid.TryGetValue(k, out int id)) mid[k] = id = m.AppendVertex(0.5 * (mesh.GetVertex(a) + mesh.GetVertex(b))); return id; }
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                int ab = Mid(tv.a, tv.b), bc = Mid(tv.b, tv.c), ca = Mid(tv.c, tv.a);
                m.AppendTriangle(tv.a, ab, ca); m.AppendTriangle(ab, tv.b, bc); m.AppendTriangle(ca, bc, tv.c); m.AppendTriangle(ab, bc, ca);
            }
            return m;
        }

        static double[] Poly(DMesh3 m, IEnumerable<int> src, out PolyhedralGeodesicStatus st, out PolyhedralGeodesicDistance g)
        {
            g = new PolyhedralGeodesicDistance(m);
            st = g.Compute(src);
            return g.Distance;
        }

        static double[] Dijkstra(DMesh3 m, int src)
        {
            var d = new double[m.MaxVertexID];
            for (int i = 0; i < d.Length; ++i) d[i] = double.PositiveInfinity;
            d[src] = 0;
            var pq = new SortedSet<(double, int)> { (0, src) };
            while (pq.Count > 0) {
                var (dv, v) = pq.Min; pq.Remove(pq.Min);
                if (dv > d[v]) continue;
                foreach (int w in m.VtxVerticesItr(v)) {
                    double nd = dv + (m.GetVertex(v) - m.GetVertex(w)).Length;
                    if (nd < d[w]) { pq.Remove((d[w], w)); d[w] = nd; pq.Add((nd, w)); }
                }
            }
            return d;
        }

        static string Close(string what, double[] a, double[] b, int n, double rel, double abs, Func<int, int> map = null)
        {
            double worst = 0; int at = -1;
            for (int v = 0; v < n; ++v) {
                double x = a[v], y = b[map == null ? v : map(v)];
                if (double.IsNaN(x) && double.IsNaN(y)) continue;
                double e = Math.Abs(x - y) - rel * Math.Max(Math.Abs(x), Math.Abs(y)) - abs;
                if (double.IsNaN(e)) return $"{what}: NaN at {v} ({x} vs {y})";
                if (e > worst) { worst = e; at = v; }
            }
            return at < 0 ? "" : $"{what}: vertex {at}: {a[at]:R} vs {b[map == null ? at : map(at)]:R}";
        }

        static (string, DMesh3)[] Meshes() => new[] {
            ("sphere", Shapes.Sphere(Vector3d.Zero, 1, 6)),
            ("torus", Shapes.Torus(Vector3d.Zero, 1, 0.35, 16, 10)),
            ("grid box", Shapes.GridBox(Vector3d.Zero, new Vector3d(1, 0.7, 0.5), 5)),
            ("box 12", Shapes.Box(Vector3d.Zero, new Vector3d(1, 0.7, 0.5))),
            ("terrain", Terrain(13, 0.15, false)),
            ("terrain alternating", Terrain(12, 0.3, true)),
            ("flat grid", Terrain(11, 0, false)),
            ("star prism", Shapes.Rotate(Shapes.Prism(Shapes.Star(5, 1.0, 0.45), -0.3, 0.3), new Vector3d(1, 2, 3), 37)),
        };

        public static void Run()
        {
            foreach (var (name, mesh) in Meshes()) {
                try { One(name, mesh); }
                catch (Exception e) { Program.Report(S, name, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
            }
        }

        static void One(string name, DMesh3 mesh)
        {
            int n = mesh.MaxVertexID;
            double extent = Checks.Bounds(mesh).Diagonal.Length;
            var rng = new Random(name.Length * 7919);
            int[] sources = { 0, n / 3, n - 1 };
            var D = new Dictionary<int, double[]>();
            double meanEdge = 0; foreach (int e in mesh.EdgeIndices()) meanEdge += mesh.GetEdgePoint(e, 0.0).Distance(mesh.GetEdgePoint(e, 1.0)); meanEdge /= mesh.EdgeCount;
            foreach (int s in sources) {
                string c = $"{name} | source {s}";
                double[] d = Program.Timed(() => Poly(mesh, new[] { s }, out var st0, out _), out long ms);
                Poly(mesh, new[] { s }, out PolyhedralGeodesicStatus st, out PolyhedralGeodesicDistance g);
                Program.Report(S, c + " | polyhedral status", st == PolyhedralGeodesicStatus.Ok ? "" : st.ToString(), ms);
                if (st != PolyhedralGeodesicStatus.Ok) continue;
                D[s] = d;
                // between the straight distance and the edge-path distance, zero at the source, finite
                double[] dj = Dijkstra(mesh, s);
                string bad = "";
                for (int v = 0; v < n && bad == ""; ++v) {
                    double straight = (mesh.GetVertex(v) - mesh.GetVertex(s)).Length;
                    if (double.IsNaN(d[v]) || double.IsInfinity(d[v])) bad = $"vertex {v}: {d[v]}";
                    else if (d[v] < straight * (1 - 1e-12) - 1e-15 * extent) bad = $"vertex {v}: {d[v]:R} below the straight distance {straight:R}";
                    else if (d[v] > dj[v] * (1 + 1e-12)) bad = $"vertex {v}: {d[v]:R} above the edge path {dj[v]:R}";
                }
                Program.Report(S, c + " | straight <= d <= edge path", bad);
                Program.Report(S, c + " | zero at the source", d[s] == 0 ? "" : d[s].ToString("R"));
                // reuse: other sources in between
                g.Compute(new[] { (s + 1) % n });
                g.Compute(new[] { s });
                Program.Report(S, c + " | reuse", Close("reuse", d, g.Distance, n, 0, 0));
                // traced paths: the length is the distance, the points are on the surface
                string pb = "";
                for (int k = 0; k < 12 && pb == ""; ++k) {
                    int t = rng.Next(n);
                    PolyhedralGeodesicPath p = g.TracePath(t);
                    if (p.Status != PolyhedralGeodesicPathStatus.Ok) { pb = $"target {t}: {p.Status}"; break; }
                    double len = 0;
                    for (int i = 1; i < p.Points.Count; ++i) len += (p.Points[i].Position - p.Points[i - 1].Position).Length;
                    if (Math.Abs(len - d[t]) > 1e-12 * d[t] + 1e-14 * extent) pb = $"target {t}: polyline {len:R} vs distance {d[t]:R}";
                    else if (Math.Abs(p.Length - d[t]) > 1e-12 * d[t] + 1e-14 * extent) pb = $"target {t}: Length {p.Length:R} vs distance {d[t]:R}";
                    else if (p.Points.Count > 0 && ((p.Points[0].Position - mesh.GetVertex(s)).Length > 1e-12 * extent && (p.Points[p.Points.Count - 1].Position - mesh.GetVertex(s)).Length > 1e-12 * extent)) pb = $"target {t}: no end at the source";
                    else foreach (SurfacePathPoint q in p.Points) if (Checks.DistanceTo(mesh, q.Position) > 1e-7 * extent) { pb = $"target {t}: a point {Checks.DistanceTo(mesh, q.Position):E2} off the surface"; break; }
                }
                Program.Report(S, c + " | traced paths", pb);
                // 2^k
                foreach (double sc in new[] { 1.0 / 1024, 1024.0 }) {
                    double[] ds = Poly(Shapes.Map(mesh, p => p * sc), new[] { s }, out var sts, out _);
                    Program.Report(S, c + " | scale " + sc, sts == st ? Close("scaled", d.Select(x => x * sc).ToArray(), ds, n, 0, 0) : "status " + sts);
                }
                // similarities, a mirror, the flipped orientation
                foreach (var (sn, f, sc) in LoftStage.Sims()) {
                    double[] ds = Poly(Shapes.Map(mesh, f), new[] { s }, out var sts, out _);
                    Program.Report(S, c + " | " + sn, sts == st ? Close(sn, d.Select(x => x * sc).ToArray(), ds, n, sn.Contains("utm") ? 1e-4 : 1e-8, 0) : "status " + sts);
                }
                {
                    var fl = new DMesh3(mesh); fl.ReverseOrientation();
                    double[] ds = Poly(fl, new[] { s }, out var sts, out _);
                    Program.Report(S, c + " | flipped orientation", sts == st ? Close("flipped", d, ds, n, 1e-12, 1e-14 * extent) : "status " + sts);
                    double[] dm = Poly(Shapes.Map(mesh, p => new Vector3d(-p.x, p.y, p.z)), new[] { s }, out sts, out _);
                    Program.Report(S, c + " | mirror", sts == st ? Close("mirror", d, dm, n, 1e-12, 1e-14 * extent) : "status " + sts);
                }
                // permutation (matched through the position)
                {
                    DMesh3 pm = Shapes.Permute(mesh, 3 + s);
                    var at = new Dictionary<Vector3d, int>(); foreach (int v in pm.VertexIndices()) at[pm.GetVertex(v)] = v;
                    double[] ds = Poly(pm, new[] { at[mesh.GetVertex(s)] }, out var sts, out _);
                    Program.Report(S, c + " | permuted", sts == st ? Close("permuted", d, ds, n, 1e-12, 1e-14 * extent, v => at[mesh.GetVertex(v)]) : "status " + sts);
                }
                // a far second component: the same distances on the first, nothing on the second
                {
                    DMesh3 two = Shapes.Combine(mesh, Shapes.Map(mesh, p => p + new Vector3d(100, 0, 0)));
                    double[] ds = Poly(two, new[] { s }, out var sts, out _);
                    Program.Report(S, c + " | far component status", sts == st ? "" : "status " + sts);
                    if (sts == st) {
                        Program.Report(S, c + " | far component, first part", Close("first", d, ds, n, 0, 0));
                        string other = ""; for (int v = n; v < 2 * n && other == ""; ++v) if (!double.IsNaN(ds[v]) && !double.IsInfinity(ds[v])) other = $"vertex {v} of the part without a source has the distance {ds[v]:R}";
                        Program.Report(S, c + " | far component, second part unreached", other);
                    }
                }
                // subdivision in the planes of the triangles: the same surface, the same distance at the old vertices
                {
                    DMesh3 sub = Subdivide(mesh);
                    double[] ds = Poly(sub, new[] { s }, out var sts, out _);
                    Program.Report(S, c + " | subdivided", sts == st ? Close("subdivided", d, ds, n, 1e-11, 1e-13 * extent) : "status " + sts);
                }
                // heat method within its documented band (2 % of the distance plus one mean edge length near boundaries)
                {
                    var heat = new MeshGeodesicDistance(mesh);
                    bool ok = heat.Compute(new[] { s });
                    if (!ok) Program.Report(S, c + " | heat", "Compute() false");
                    else {
                        double worst = 0; int wv = -1; string neg = "";
                        for (int v = 0; v < n; ++v) {
                            double h = heat.Distance[v];
                            if (double.IsNaN(h) || h < 0) { neg = $"vertex {v}: {h}"; break; }
                            double over = Math.Abs(h - d[v]) / (0.02 * d[v] + meanEdge);
                            if (over > worst) { worst = over; wv = v; }
                        }
                        Program.Report(S, c + " | heat non-negative, finite", neg);
                        Program.Report(S, c + " | heat zero at the source", heat.Distance[s] == 0 ? "" : heat.Distance[s].ToString("R"));
                        if (worst > 1) Program.Info(S, c + " | heat band", $"{worst:F2} times (2 % + one mean edge) at vertex {wv}: {heat.Distance[wv]:R} vs {d[wv]:R}");
                        else Program.Report(S, c + " | heat band", "");
                        // reuse and repeat
                        var heat2 = new MeshGeodesicDistance(mesh); heat2.Compute(new[] { (s + 1) % n }); heat2.Compute(new[] { s });
                        Program.Report(S, c + " | heat reuse", Close("reuse", heat.Distance, heat2.Distance, n, 0, 0));
                        foreach (double sc in new[] { 1.0 / 1024, 1024.0 }) {
                            var hs = new MeshGeodesicDistance(Shapes.Map(mesh, p => p * sc)); hs.Compute(new[] { s });
                            string same = Close("scaled", heat.Distance.Select(x => x * sc).ToArray(), hs.Distance, n, 0, 0);
                            if (same != "") { string near = Close("scaled", heat.Distance.Select(x => x * sc).ToArray(), hs.Distance, n, 1e-9, 0); if (near == "") Program.Info(S, c + " | heat scale " + sc, "not bit for bit: " + same); else Program.Report(S, c + " | heat scale " + sc, near); }
                            else Program.Report(S, c + " | heat scale " + sc, "");
                        }
                        DMesh3 two = Shapes.Combine(mesh, Shapes.Map(mesh, p => p + new Vector3d(100, 0, 0)));
                        var h2 = new MeshGeodesicDistance(two); bool ok2 = h2.Compute(new[] { s });
                        if (!ok2) Program.Report(S, c + " | heat far component", "Compute() false");
                        else {
                            Program.Report(S, c + " | heat far component, first part", Close("first", heat.Distance, h2.Distance, n, 1e-9, 1e-12 * extent));
                            string other = ""; for (int v = n; v < 2 * n && other == ""; ++v) if (!double.IsNaN(h2.Distance[v])) other = $"vertex {v}: {h2.Distance[v]:R}, documented NaN";
                            Program.Report(S, c + " | heat far component, second part NaN", other);
                        }
                    }
                }
                // FlipOut paths: polyhedral distance <= Length <= initial edge path, the polyline has the Length and lies on the surface
                {
                    string fb = ""; int longer = 0;
                    for (int k = 0; k < 10 && fb == ""; ++k) {
                        int t = rng.Next(n); if (t == s) continue;
                        var gp = new MeshGeodesicPath(mesh);
                        GeodesicPathStatus ps = gp.Compute(s, t);
                        if (ps != GeodesicPathStatus.Success) { fb = $"target {t}: {ps}"; break; }
                        double len = 0; for (int i = 1; i < gp.Path.Count; ++i) len += (gp.Path[i] - gp.Path[i - 1]).Length;
                        if (Math.Abs(len - gp.Length) > 1e-9 * gp.Length + 1e-12 * extent) fb = $"target {t}: polyline {len:R} vs Length {gp.Length:R}";
                        else if (gp.Length < d[t] * (1 - 1e-9) - 1e-12 * extent) fb = $"target {t}: Length {gp.Length:R} below the polyhedral distance {d[t]:R}";
                        else if (gp.Length > gp.InitialLength * (1 + 1e-12)) fb = $"target {t}: Length {gp.Length:R} above the initial {gp.InitialLength:R}";
                        else if (gp.Path.Count < 2 || (gp.Path[0] - mesh.GetVertex(s)).Length > 1e-12 * extent || (gp.Path[gp.Path.Count - 1] - mesh.GetVertex(t)).Length > 1e-12 * extent) fb = $"target {t}: the ends are not the vertices";
                        else foreach (Vector3d q in gp.Path) if (Checks.DistanceTo(mesh, q) > 1e-8 * extent) { fb = $"target {t}: a point {Checks.DistanceTo(mesh, q):E2} off the surface"; break; }
                        if (fb == "" && gp.Length > d[t] * (1 + 1e-6)) ++longer;
                        if (fb == "") {
                            // the reverse path has the same length when both are the shortest; at least it is a geodesic too
                            var back = new MeshGeodesicPath(mesh); GeodesicPathStatus bs = back.Compute(t, s);
                            if (bs != GeodesicPathStatus.Success) fb = $"reverse of target {t}: {bs}";
                            // 2^k: the length scales exactly
                            var sp = new MeshGeodesicPath(Shapes.Map(mesh, p => p * 1024)); sp.Compute(s, t);
                            if (fb == "" && sp.Length != gp.Length * 1024) fb = $"target {t}: scale 1024 gives {sp.Length:R} vs {gp.Length * 1024:R}";
                            // reuse
                            gp.Compute(t, s); gp.Compute(s, t);
                            if (fb == "" && gp.Length != len && Math.Abs(gp.Length - len) > 1e-9 * len) fb = $"target {t}: reuse gives {gp.Length:R} vs {len:R}";
                        }
                    }
                    Program.Report(S, c + " | FlipOut paths", fb);
                    if (longer > 0) Program.Info(S, c + " | FlipOut", $"{longer} of 10 paths are locally but not globally shortest (documented)");
                }
            }
            // symmetry and the triangle inequality between the sources
            foreach (int a in sources) foreach (int b in sources) {
                if (!D.ContainsKey(a) || !D.ContainsKey(b) || a >= b) continue;
                Program.Report(S, $"{name} | symmetry {a},{b}", Math.Abs(D[a][b] - D[b][a]) <= 1e-12 * D[a][b] + 1e-14 * extent ? "" : $"{D[a][b]:R} vs {D[b][a]:R}");
                string ti = "";
                for (int v = 0; v < n && ti == ""; ++v) if (D[a][v] > D[a][b] + D[b][v] + 1e-12 * extent) ti = $"d({a},{v}) = {D[a][v]:R} > d({a},{b}) + d({b},{v}) = {D[a][b] + D[b][v]:R}";
                Program.Report(S, $"{name} | triangle inequality {a},{b}", ti);
                // two sources: the minimum
                double[] both = Poly(mesh, new[] { a, b }, out var st, out _);
                Program.Report(S, $"{name} | sources {a},{b} = min", st == PolyhedralGeodesicStatus.Ok ? Close("min", both, Enumerable.Range(0, n).Select(v => Math.Min(D[a][v], D[b][v])).ToArray(), n, 1e-12, 1e-14 * extent) : "status " + st);
            }
            // Bezier: two control points give the geodesic; the points lie on the surface; the first and last are the control vertices
            {
                string c = name + " | bezier";
                int a = sources[0], b = sources[1], m3 = sources[2];
                var bz = new MeshGeodesicBezier(mesh);
                GeodesicPathStatus st = bz.Compute(new[] { a, m3, b }, 8);
                if (st != GeodesicPathStatus.Success) Program.Info(S, c, st.ToString());
                else {
                    string bad = "";
                    if (bz.Points.Count != 9) bad = "points " + bz.Points.Count;
                    else if ((bz.Points[0] - mesh.GetVertex(a)).Length > 1e-12 * extent || (bz.Points[8] - mesh.GetVertex(b)).Length > 1e-9 * extent) bad = "the ends are not the control vertices";
                    else foreach (Vector3d q in bz.Path) if (Checks.DistanceTo(mesh, q) > 1e-8 * extent) { bad = $"a point {Checks.DistanceTo(mesh, q):E2} off the surface"; break; }
                    double len = 0; for (int i = 1; i < bz.Path.Count; ++i) len += (bz.Path[i] - bz.Path[i - 1]).Length;
                    if (bad == "" && Math.Abs(len - bz.Length) > 1e-9 * len) bad = $"polyline {len:R} vs Length {bz.Length:R}";
                    Program.Report(S, c + " | on the surface", bad);
                    // the reversed control polygon gives the reversed curve (the same length), when the geodesics are unique
                    var rv = new MeshGeodesicBezier(mesh); GeodesicPathStatus rs = rv.Compute(new[] { b, m3, a }, 8);
                    if (rs == GeodesicPathStatus.Success && Math.Abs(rv.Length - bz.Length) > 1e-6 * bz.Length) Program.Info(S, c + " | reversed", $"length {rv.Length:R} vs {bz.Length:R} (geodesics not unique, documented)");
                    var again = new MeshGeodesicBezier(mesh); again.Compute(new[] { a, m3, b }, 8);
                    Program.Report(S, c + " | repeat", again.Length == bz.Length ? "" : $"{again.Length:R} vs {bz.Length:R}");
                    bz.Compute(new[] { b, a }, 4); bz.Compute(new[] { a, m3, b }, 8);
                    Program.Report(S, c + " | reuse", again.Length == bz.Length ? "" : $"{again.Length:R} vs {bz.Length:R}");
                    var sc = new MeshGeodesicBezier(Shapes.Map(mesh, p => p * 1024)); sc.Compute(new[] { a, m3, b }, 8);
                    Program.Report(S, c + " | scale 1024", sc.Length == bz.Length * 1024 ? "" : $"{sc.Length:R} vs {bz.Length * 1024:R}");
                }
                var two = new MeshGeodesicBezier(mesh); GeodesicPathStatus ts = two.Compute(new[] { a, b }, 4);
                var gp = new MeshGeodesicPath(mesh); gp.Compute(a, b);
                if (ts == GeodesicPathStatus.Success && gp.Status == GeodesicPathStatus.Success) {
                    // one segment is the geodesic itself; with more samples the points can come from two geodesics of the same length
                    // when the end points lie on each other's cut locus (documented on the class; the symmetric sphere has such pairs)
                    var seg1 = new MeshGeodesicBezier(mesh); seg1.Compute(new[] { a, b }, 1);
                    Program.Report(S, c + " | two control points, one segment = the geodesic", Math.Abs(seg1.Length - gp.Length) <= 1e-9 * gp.Length ? "" : $"{seg1.Length:R} vs {gp.Length:R}");
                    if (Math.Abs(two.Length - gp.Length) > 1e-9 * gp.Length) Program.Info(S, c + " | two control points, four segments", $"{two.Length:R} vs the geodesic {gp.Length:R} (O2: two shortest geodesics, cut locus)");
                    else Program.Report(S, c + " | two control points, four segments = the geodesic", "");
                }
            }
            // vector heat: the log map length is a distance, scalar extension of one source is constant
            {
                string c = name + " | vector heat";
                int s = sources[1];
                var vh = new VectorHeatMethod(mesh);
                double[] ext = vh.ExtendScalar(new[] { new KeyValuePair<int, double>(s, 3.5) });
                string bad = ""; for (int v = 0; v < n && bad == ""; ++v) if (!(Math.Abs(ext[v] - 3.5) < 1e-6)) bad = $"vertex {v}: {ext[v]:R}";
                Program.Report(S, c + " | one source extends to a constant", bad);
                Vector2d[] tr = vh.TransportVector(s, new Vector2d(1, 0));
                bad = ""; for (int v = 0; v < n && bad == ""; ++v) if (!(Math.Abs(tr[v].Length - 1) < 1e-6)) bad = $"vertex {v}: length {tr[v].Length:R}";
                Program.Report(S, c + " | transport keeps the length", bad);
                var vh2 = new VectorHeatMethod(mesh); vh2.TransportVector((s + 1) % n, new Vector2d(0, 1)); Vector2d[] tr2 = vh2.TransportVector(s, new Vector2d(1, 0));
                bad = ""; for (int v = 0; v < n && bad == ""; ++v) if (tr[v].x != tr2[v].x || tr[v].y != tr2[v].y) bad = $"vertex {v}";
                Program.Report(S, c + " | reuse", bad);
                var vs = new VectorHeatMethod(Shapes.Map(mesh, p => p * 1024)); Vector2d[] trs = vs.TransportVector(s, new Vector2d(1, 0));
                bad = ""; for (int v = 0; v < n && bad == ""; ++v) if ((tr[v] - trs[v]).Length > 1e-9) bad = $"vertex {v}: {tr[v]} vs {trs[v]}";
                Program.Report(S, c + " | scale 1024", bad);
            }
        }
    }
}
