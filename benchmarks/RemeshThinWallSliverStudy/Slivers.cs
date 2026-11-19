// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Part B: offline repair of the pinned slivers of the crease study (docs/design/REMESHER_CREASE_SLIVERS.md): pins (VertexConstraint.Fixed)
// and the feature curves stay, the patch boundary stays; (a) the free third corner moves (on the input surface, or along its curve),
// (b) the patch is re-triangulated (edge flips up to depth 3, vertex removal of the free corner with the best polygon triangulation,
// patch of 1 ring = the stars of the three corners, 2 rings = the stars of all their neighbours too), (c) both.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using gs;

static partial class Program
{
    static readonly int[] CaseIds = { 104582, 125069, 135363, 196126, 105686, 78295, 93370, 299291, 39460, 1053376 };

    static double MeanEdgeLen(DMesh3 m) => m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

    static double MinAngleDeg(Vector3d a, Vector3d b, Vector3d c, out double maxAngle)
    {
        double A(Vector3d p, Vector3d q, Vector3d r) { var u = q - p; var v = r - p; double cr = u.Cross(v).Length, d = u.Dot(v); return Math.Atan2(cr, d) * 180 / Math.PI; }
        double x = A(a, b, c), y = A(b, c, a), z = A(c, a, b);
        maxAngle = Math.Max(x, Math.Max(y, z));
        double mn = Math.Min(x, Math.Min(y, z));
        return double.IsNaN(mn) ? 0 : mn;
    }

    class Sliver
    {
        public int Tid; public int[] V; public int Pins; public int Free = -1; public double MinAngle, MaxAngle;
        public bool FreeOnCurve; public double PinPairDist;     // distance of the closest pair of pins / target
        public double WedgeMin = 180;                            // smallest angle between two feature edges at a pin of the sliver
        // best min angle over the changed patch per method and deviation tolerance index
        public double[] A, B1, B2, C1, C2;
    }

    class Study
    {
        public DMesh3 M; public MeshConstraints Cons; public MeshProjectionTarget Proj; public double Target; public double DevTol;
        public Dictionary<(int, int, int), (bool ok, double ang)> Memo = new Dictionary<(int, int, int), (bool, double)>();
        public HashSet<(int, int, int)> Orig = new HashSet<(int, int, int)>();
        public (int, int, int) SliverKey;
        public Dictionary<int, Vector3d> Pos = new Dictionary<int, Vector3d>();      // overrides (the moved free vertex)
        public Vector3d P(int v) => Pos.TryGetValue(v, out var p) ? p : M.GetVertex(v);

        public static (int, int, int) Key(int a, int b, int c)
        {
            if (a < b && a < c) return (a, b, c);
            if (b < a && b < c) return (b, c, a);
            return (c, a, b);
        }

        // validity and min angle of a triangle (a, b, c) at the current positions
        public (bool ok, double ang) Eval(int a, int b, int c, bool useMemo)
        {
            var key = Key(a, b, c);
            if (useMemo && Memo.TryGetValue(key, out var r)) return r;
            Vector3d pa = P(a), pb = P(b), pc = P(c);
            double ang = MinAngleDeg(pa, pb, pc, out _);
            Vector3d n = (pb - pa).Cross(pc - pa);
            bool ok = n.Length > 0 && !double.IsNaN(ang);
            if (ok) {
                Vector3d cen = (pa + pb + pc) / 3;
                Vector3d near = Proj.Project(cen, out Vector3d refN);
                ok = n.Normalized.Dot(refN) > 0 && near.Distance(cen) <= DevTol * Target;
            }
            if (useMemo) Memo[key] = (ok, ang);
            return (ok, ang);
        }
    }

    // ---- local triangulation states ----
    class State { public List<(int a, int b, int c)> T; public string Sig; }
    static string Signature(List<(int a, int b, int c)> t) =>
        string.Join(";", t.Select(x => Study.Key(x.a, x.b, x.c)).OrderBy(k => k).Select(k => $"{k.Item1},{k.Item2},{k.Item3}"));

    // score of a state: the minimum min angle over the changed triangles (not in the original patch) and the sliver if it is still there;
    // -1 if an invalid new triangle
    static double Score(Study s, List<(int a, int b, int c)> tris, bool stateMoved = false, HashSet<int> moved = null)
    {
        double m = 1000;
        foreach (var t in tris) {
            var key = Study.Key(t.a, t.b, t.c);
            bool isOrig = s.Orig.Contains(key);
            bool touchesMoved = moved != null && (moved.Contains(t.a) || moved.Contains(t.b) || moved.Contains(t.c));
            if (isOrig && !touchesMoved) {
                if (key.Equals(s.SliverKey)) m = Math.Min(m, s.Eval(t.a, t.b, t.c, true).ang);
                continue;
            }
            var (ok, ang) = s.Eval(t.a, t.b, t.c, !touchesMoved);
            if (!ok) return -1;
            m = Math.Min(m, ang);
        }
        return m;
    }

    // BFS over flip sequences up to depth; returns the states (best first) up to a cap
    static List<(List<(int a, int b, int c)> t, double score)> FlipSearch(Study s, List<(int a, int b, int c)> start, int depth, int cap, out bool capped, HashSet<int> moved = null)
    {
        capped = false;
        var seen = new HashSet<string> { Signature(start) };
        var all = new List<(List<(int, int, int)>, double)> { (start, Score(s, start, false, moved)) };
        var frontier = new List<List<(int a, int b, int c)>> { start };
        for (int d = 0; d < depth; ++d) {
            var next = new List<List<(int a, int b, int c)>>();
            foreach (var st in frontier) {
                // directed edge -> triangle index
                var dir = new Dictionary<(int, int), int>();
                for (int i = 0; i < st.Count; ++i) { var t = st[i]; dir[(t.a, t.b)] = i; dir[(t.b, t.c)] = i; dir[(t.c, t.a)] = i; }
                foreach (var kv in dir) {
                    int u = kv.Key.Item1, w = kv.Key.Item2;
                    if (u > w) continue;
                    if (!dir.TryGetValue((w, u), out int j)) continue;
                    int i1 = kv.Value;
                    int c = Third(st[i1], u, w), dd = Third(st[j], w, u);
                    if (c == dd) continue;
                    // the edge must be flippable and the new diagonal must not exist in the mesh
                    int eid = s.M.FindEdge(u, w);
                    if (eid != DMesh3.InvalidID && s.Cons != null && s.Cons.HasEdgeConstraint(eid) && !s.Cons.GetEdgeConstraint(eid).CanFlip) continue;
                    if (s.M.FindEdge(c, dd) != DMesh3.InvalidID) continue;
                    var nt = new List<(int a, int b, int c)>(st);
                    var t1 = (u, dd, c); var t2 = (dd, w, c);
                    if (!s.Eval(t1.Item1, t1.Item2, t1.Item3, moved == null).ok || !s.Eval(t2.Item1, t2.Item2, t2.Item3, moved == null).ok) continue;
                    int lo = Math.Min(i1, j), hi = Math.Max(i1, j);
                    nt.RemoveAt(hi); nt.RemoveAt(lo);
                    nt.Add(t1); nt.Add(t2);
                    string sig = Signature(nt);
                    if (!seen.Add(sig)) continue;
                    if (all.Count >= cap) { capped = true; goto done; }
                    next.Add(nt);
                    all.Add((nt, Score(s, nt, false, moved)));
                }
            }
            frontier = next;
        }
        done:
        return all.OrderByDescending(x => x.Item2).ToList();
    }
    static int Third((int a, int b, int c) t, int x, int y)
    {
        if (t.a != x && t.a != y) return t.a;
        if (t.b != x && t.b != y) return t.b;
        return t.c;
    }

    // vertex removal of v from a state: the best polygon triangulation (max-min of the min angle) of its link; returns the new state or null
    static (List<(int a, int b, int c)> t, double score)? RemoveVertex(Study s, List<(int a, int b, int c)> st, int v)
    {
        var star = st.Where(t => t.a == v || t.b == v || t.c == v).ToList();
        if (star.Count < 3 || star.Count > 12) return null;
        // ordered link: for each triangle (v, x, y) (rotated so v first), x -> y
        var next = new Dictionary<int, int>();
        foreach (var t in star) {
            int x, y;
            if (t.a == v) { x = t.b; y = t.c; } else if (t.b == v) { x = t.c; y = t.a; } else { x = t.a; y = t.b; }
            if (next.ContainsKey(x)) return null;
            next[x] = y;
        }
        var poly = new List<int>();
        int cur = next.Keys.First();
        for (int i = 0; i < star.Count; ++i) {
            poly.Add(cur);
            if (!next.TryGetValue(cur, out cur)) return null;
        }
        if (cur != poly[0] || poly.Distinct().Count() != poly.Count) return null;
        int n = poly.Count;
        // polygon (poly[0..n-1]) is ccw as seen with v inside: triangles (v, poly[i], poly[i+1]) are ccw
        var f = new double[n, n]; var split = new int[n, n];
        for (int len = 1; len < n; ++len)
            for (int i = 0; i + len < n; ++i) {
                int j = i + len;
                if (len == 1) { f[i, j] = 1000; continue; }
                double best = -2; int bk = -1;
                for (int k = i + 1; k < j; ++k) {
                    if (f[i, k] < 0 || f[k, j] < 0) continue;
                    var e = s.Eval(poly[i], poly[k], poly[j], false);
                    if (!e.ok) continue;
                    // diagonals (i,k), (k,j) must not exist in the mesh unless they are polygon sides
                    if (k - i > 1 && s.M.FindEdge(poly[i], poly[k]) != DMesh3.InvalidID) continue;
                    if (j - k > 1 && s.M.FindEdge(poly[k], poly[j]) != DMesh3.InvalidID) continue;
                    double sc = Math.Min(e.ang, Math.Min(f[i, k], f[k, j]));
                    if (sc > best) { best = sc; bk = k; }
                }
                f[i, j] = bk < 0 ? -1 : best; split[i, j] = bk;
            }
        if (f[0, n - 1] < 0) return null;
        var res = st.Where(t => !(t.a == v || t.b == v || t.c == v)).ToList();
        var stack = new Stack<(int, int)>(); stack.Push((0, n - 1));
        while (stack.Count > 0) {
            var (i, j) = stack.Pop();
            if (j - i < 2) continue;
            int k = split[i, j];
            res.Add((poly[i], poly[k], poly[j]));
            stack.Push((i, k)); stack.Push((k, j));
        }
        return (res, Score(s, res));
    }

    // move the free vertex v within its star in the given state: grid and refinement; returns the best score and sets the position
    static double MoveSearch(Study s, List<(int a, int b, int c)> st, int v, VertexConstraint vc, int[] curveNbrs, out Vector3d best)
    {
        var star = st.Where(t => t.a == v || t.b == v || t.c == v).ToList();
        var others = st.Where(t => !(t.a == v || t.b == v || t.c == v)).ToList();
        Vector3d p0 = s.M.GetVertex(v);
        double othersScore = Score(s, others);
        best = p0;
        Vector3d bestP = p0;
        if (othersScore < 0) return -1;
        var moved = new HashSet<int> { v };
        Vector3d Project(Vector3d p) => vc.Target != null ? vc.Target.Project(p) : s.Proj.Project(p);
        double Eval(Vector3d p)
        {
            s.Pos[v] = p;
            double m = othersScore;
            foreach (var t in star) {
                var e = s.Eval(t.a, t.b, t.c, false);
                if (!e.ok) { s.Pos.Remove(v); return -1; }
                m = Math.Min(m, e.ang);
            }
            s.Pos.Remove(v);
            return m;
        }
        double bestScore = Eval(p0);
        // the original position is valid as is (the triangles of the original star are exempt in Score but Eval checks them; accept it)
        if (bestScore < 0) bestScore = Score(s, st);
        var cands = new List<Vector3d>();
        var link = new HashSet<int>(); foreach (var t in star) { link.Add(t.a); link.Add(t.b); link.Add(t.c); }
        link.Remove(v);
        if (vc.Target == null) {
            foreach (var t in star) {
                int x = t.a == v ? t.b : (t.b == v ? t.c : t.a), y = t.a == v ? t.c : (t.b == v ? t.a : t.b);
                Vector3d a = s.M.GetVertex(x), b = s.M.GetVertex(y);
                for (int i = 0; i <= 6; ++i) for (int j = 0; i + j <= 6; ++j) {
                    double wa = i / 6.0, wb = j / 6.0;
                    cands.Add(Project((1 - wa - wb) * p0 + wa * a + wb * b));
                }
            }
        } else if (curveNbrs != null) {
            foreach (int nb in curveNbrs)
                for (int i = -10; i <= 10; ++i) cands.Add(Project(p0 + (i / 10.0) * (s.M.GetVertex(nb) - p0)));
        }
        void Try(Vector3d p) { double e = Eval(p); if (e > bestScore) { bestScore = e; bestP = p; } }
        foreach (var p in cands) Try(p);
        // refinement: rings around the best, radius relative to the distance to the nearest link vertex
        for (int round = 0; round < 3; ++round) {
            var c = bestP;
            double rad = link.Min(l => s.M.GetVertex(l).Distance(c)) * (0.3 / (1 + round));
            s.Proj.Project(c, out Vector3d n);
            Vector3d u = n.UnitCross(Math.Abs(n.z) < 0.9 ? Vector3d.AxisZ : Vector3d.AxisX), w = n.Cross(u);
            for (int k = 0; k < 16; ++k) {
                double a = 2 * Math.PI * k / 16;
                foreach (double f in new[] { 0.35, 0.7, 1.0 }) Try(Project(c + f * rad * (Math.Cos(a) * u + Math.Sin(a) * w)));
            }
        }
        best = bestP;
        return bestScore;
    }

    static List<int> PatchTriangles(DMesh3 m, IEnumerable<int> verts)
    {
        var set = new HashSet<int>();
        foreach (int v in verts) foreach (int t in m.VtxTrianglesItr(v)) set.Add(t);
        return set.ToList();
    }

    static int Slivers(Dictionary<string, string> A, Regex only)
    {
        string dir = A.ContainsKey("thingi") ? A["thingi"] : "benchmarks/RemeshBenchmark/data/meshes";
        double[] devTols = (A.ContainsKey("dev") ? A["dev"] : "0.03,0.1,0.3").Split(',').Select(x => double.Parse(x, IC)).ToArray();
        int depth = A.ContainsKey("depth") ? int.Parse(A["depth"]) : 3;
        var all = new List<(int id, List<Sliver> list, Study[] studies, double secs)>();
        foreach (int id in CaseIds) {
            if (only != null && !only.IsMatch(id.ToString())) continue;
            DMesh3 input = StandardMeshReader.ReadMesh(Path.Combine(dir, id + ".stl"));
            double target = MeanEdgeLen(input);
            DMesh3 mesh = new DMesh3(input);
            var r = new Remesher(mesh);
            r.SmoothSpeedT = 0.5; r.MinTriangleQuality = 0.2;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(target);
            for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
            var cons = r.Constraints;
            var proj = MeshProjectionTarget.Auto(input);
            var sw = Stopwatch.StartNew();
            var slivers = new List<Sliver>();
            int nSliver = 0, nPinned2 = 0, nPinned1 = 0, nPinned0 = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(tid);
                double mn = MinAngleDeg(mesh.GetVertex(tv.a), mesh.GetVertex(tv.b), mesh.GetVertex(tv.c), out double mx);
                if (!(mn < 0.5 || mx > 170)) continue;
                ++nSliver;
                int pins = 0, free = -1;
                for (int k = 0; k < 3; ++k) if (cons.GetVertexConstraint(tv[k]).Fixed) pins++; else free = tv[k];
                if (pins >= 2) nPinned2++; else if (pins == 1) nPinned1++; else nPinned0++;
                if (pins < 2) continue;
                var sl = new Sliver() { Tid = tid, V = new[] { tv.a, tv.b, tv.c }, Pins = pins, Free = pins == 2 ? free : -1, MinAngle = mn, MaxAngle = mx };
                if (sl.Free >= 0) sl.FreeOnCurve = cons.GetVertexConstraint(sl.Free).Target != null;
                var pv = sl.V.Where(v => cons.GetVertexConstraint(v).Fixed).ToArray();
                double dmin = double.MaxValue;
                for (int i = 0; i < pv.Length; ++i) for (int j = i + 1; j < pv.Length; ++j) dmin = Math.Min(dmin, mesh.GetVertex(pv[i]).Distance(mesh.GetVertex(pv[j])));
                sl.PinPairDist = dmin / target;
                foreach (int pvv in pv) {
                    var fe = new List<Vector3d>();
                    foreach (int nb in mesh.VtxVerticesItr(pvv)) {
                        int eid = mesh.FindEdge(pvv, nb);
                        if (cons.HasEdgeConstraint(eid) && cons.GetEdgeConstraint(eid).Target != null || (cons.HasEdgeConstraint(eid) && !cons.GetEdgeConstraint(eid).CanFlip))
                            fe.Add((mesh.GetVertex(nb) - mesh.GetVertex(pvv)).Normalized);
                    }
                    for (int i = 0; i < fe.Count; ++i) for (int j = i + 1; j < fe.Count; ++j)
                        sl.WedgeMin = Math.Min(sl.WedgeMin, Math.Acos(Math.Max(-1, Math.Min(1, fe[i].Dot(fe[j])))) * 180 / Math.PI);
                }
                slivers.Add(sl);
            }
            Console.Error.WriteLine($"# {id}: target {target:F3}, result {mesh.TriangleCount} tris, slivers {nSliver} (2+ pins {nPinned2}, 1 pin {nPinned1}, 0 pins {nPinned0})");
            var studies = devTols.Select(d => new Study() { M = mesh, Cons = cons, Proj = proj, Target = target, DevTol = d }).ToArray();
            foreach (var sl in slivers) {
                sl.A = new double[devTols.Length]; sl.B1 = new double[devTols.Length]; sl.B2 = new double[devTols.Length]; sl.C1 = new double[devTols.Length]; sl.C2 = new double[devTols.Length];
                for (int di = 0; di < devTols.Length; ++di) {
                    var s = studies[di];
                    s.Memo.Clear(); s.Pos.Clear();
                    s.SliverKey = Study.Key(sl.V[0], sl.V[1], sl.V[2]);
                    double baseline = sl.MinAngle;
                    sl.A[di] = sl.B1[di] = sl.B2[di] = sl.C1[di] = sl.C2[di] = baseline;
                    var p1 = PatchTriangles(mesh, sl.V);
                    var p2 = PatchTriangles(mesh, p1.SelectMany(t => { var tv = mesh.GetTriangle(t); return new[] { tv.a, tv.b, tv.c }; }).Distinct());
                    foreach (int ring in new[] { 1, 2 }) {
                        var pt = (ring == 1 ? p1 : p2).Select(t => { var tv = mesh.GetTriangle(t); return (tv.a, tv.b, tv.c); }).ToList();
                        s.Orig = new HashSet<(int, int, int)>(pt.Select(t => Study.Key(t.a, t.b, t.c)));
                        s.Memo.Clear();
                        // (b) flips (+ removal of the free corner)
                        var states = FlipSearch(s, pt, depth, 20000, out _);
                        double bBest = Math.Max(baseline, states[0].score);
                        bool canRemove = sl.Free >= 0 && !sl.FreeOnCurve;
                        if (canRemove)
                            foreach (var st in states.Take(40)) {
                                var rm = RemoveVertex(s, st.t, sl.Free);
                                if (rm.HasValue) bBest = Math.Max(bBest, rm.Value.score);
                            }
                        // (c) moves of the free corner on top of the best flip states
                        double cBest = bBest;
                        double aBest = baseline;
                        if (sl.Free >= 0) {
                            var vc = cons.GetVertexConstraint(sl.Free);
                            int[] cn = null;
                            if (vc.Target != null) cn = mesh.VtxVerticesItr(sl.Free).Where(nb => { int e = mesh.FindEdge(sl.Free, nb); return cons.HasEdgeConstraint(e) && cons.GetEdgeConstraint(e).Target != null; }).ToArray();
                            // (a): moves in the original state (1 ring only: the star of the free corner is the same in both rings)
                            double ma = MoveSearch(s, pt, sl.Free, vc, cn, out Vector3d bp);
                            aBest = Math.Max(baseline, ma);
                            cBest = Math.Max(cBest, aBest);
                            foreach (var st in states.Take(25)) {
                                if (st.score < 0 && st.t != pt) continue;
                                double mc = MoveSearch(s, st.t, sl.Free, vc, cn, out Vector3d bp2);
                                if (mc > cBest) {
                                    cBest = mc;
                                    // one more flip round with the moved corner
                                    s.Pos[sl.Free] = bp2;
                                    var st2 = FlipSearch(s, st.t, 2, 5000, out _, new HashSet<int> { sl.Free });
                                    cBest = Math.Max(cBest, st2[0].score);
                                    s.Pos.Remove(sl.Free);
                                }
                            }
                        }
                        if (ring == 1) { sl.A[di] = aBest; sl.B1[di] = bBest; sl.C1[di] = Math.Max(cBest, Math.Max(aBest, bBest)); }
                        else { sl.B2[di] = bBest; sl.C2[di] = Math.Max(cBest, Math.Max(Math.Max(aBest, bBest), sl.C1[di])); }
                    }
                }
            }
            all.Add((id, slivers, studies, sw.Elapsed.TotalSeconds));
            Console.Error.WriteLine($"#   search {sw.Elapsed.TotalSeconds:F0} s");
        }
        // report
        for (int di = 0; di < devTols.Length; ++di) {
            Console.WriteLine($"\n### deviation guard {devTols[di]} x target\n");
            Console.WriteLine("| case | pinned slivers (2+ pins, of all) | median worst min angle before | (a) move >=10 / >=20 | (b) 1-ring >=10 / >=20 | (b) 2-ring >=10 / >=20 | (c) 1-ring >=10 / >=20 | (c) 2-ring >=10 / >=20 | best >=10 / >=20 | search s |");
            Console.WriteLine("|---|---:|---:|---|---|---|---|---|---|---:|");
            var tot = new List<Sliver>();
            foreach (var (id, list, _, secs) in all) {
                tot.AddRange(list);
                Console.WriteLine(Row(id.ToString(), list, di, secs));
            }
            Console.WriteLine(Row("**all**", tot, di, all.Sum(x => x.secs)));
        }
        // remaining configurations (primary guard = middle value)
        int pi = Math.Min(1, devTols.Length - 1);
        var allS = all.SelectMany(x => x.list).ToList();
        Console.WriteLine($"\n### remaining slivers (best < 20 degrees, guard {devTols[pi]}) by type\n");
        var rem = allS.Where(s => Best(s, pi) < 20).ToList();
        string Type(Sliver s) {
            if (s.Pins == 3) return "3 pins";
            if (s.PinPairDist < 0.1) return "2 pins closer than 0.1 target";
            if (s.PinPairDist < 0.3) return "2 pins 0.1..0.3 target";
            if (s.WedgeMin < 20) return "2 pins >= 0.3 target, crease wedge at a pin < 20 deg";
            if (s.FreeOnCurve) return "2 pins >= 0.3 target, free corner on a curve";
            return "2 pins >= 0.3 target, other";
        }
        Console.WriteLine("| type | slivers | best < 10 | share of all pinned slivers |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (var g in rem.GroupBy(Type).OrderByDescending(g => g.Count()))
            Console.WriteLine($"| {g.Key} | {g.Count()} | {g.Count(s => Best(s, pi) < 10)} | {100.0 * g.Count() / allS.Count:F1} % |");
        Console.WriteLine($"\nbest after the free-corner / pin-pair distance (pinned slivers by 2-pin distance / target):\n");
        Console.WriteLine("| pin pair distance / target | slivers | best >= 10 | best >= 20 | median best |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        foreach (var (lo, hi) in new[] { (0.0, 0.05), (0.05, 0.1), (0.1, 0.3), (0.3, 0.6), (0.6, 1e9) }) {
            var g = allS.Where(s => s.PinPairDist >= lo && s.PinPairDist < hi).ToList();
            if (g.Count == 0) continue;
            Console.WriteLine($"| {lo}..{(hi > 1e8 ? "inf" : hi.ToString(IC))} | {g.Count} | {g.Count(s => Best(s, pi) >= 10)} | {g.Count(s => Best(s, pi) >= 20)} | {Median(g.Select(s => Best(s, pi)).ToList()):F1} |");
        }
        return 0;
    }

    static double Best(Sliver s, int di) => Math.Max(Math.Max(s.C1[di], s.C2[di]), Math.Max(Math.Max(s.A[di], s.B1[di]), s.B2[di]));
    static double Median(List<double> l) { if (l.Count == 0) return double.NaN; l.Sort(); return l[l.Count / 2]; }
    static string Row(string name, List<Sliver> l, int di, double secs)
    {
        if (l.Count == 0) return $"| {name} | 0 | - | - | - | - | - | - | - | {secs:F0} |";
        string P(Func<Sliver, double> f) => $"{100.0 * l.Count(s => f(s) >= 10) / l.Count:F0} % / {100.0 * l.Count(s => f(s) >= 20) / l.Count:F0} %";
        return $"| {name} | {l.Count} | {Median(l.Select(s => s.MinAngle).ToList()):F2} | {P(s => s.A[di])} | {P(s => s.B1[di])} | {P(s => s.B2[di])} | {P(s => s.C1[di])} | {P(s => s.C2[di])} | {P(s => Best(s, di))} | {secs:F0} |";
    }
}
