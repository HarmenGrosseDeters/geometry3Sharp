// The "joint" mode (research note docs/research/ROUNDING_DEGENERATE_LEFT_2026-11.md, "Attempt at a joint move"): on the state the repair
// leaves, enumerate joint moves of two free vertices of a left-over degenerate triangle (each vertex by its room over 2^j along the 26
// directions of the position search) and count the pairs the rule of the repair's `improves` would accept (no new degenerate triangle,
// no new intersection, a net gain, no triangle that turns over). A measurement harness: no library change; the repair's private
// methods are called by reflection.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using g3;
using gs;

static partial class Program
{
    sealed class Probe
    {
        public MeshRoundingRepair R;
        public DMesh3 M;
        public Func<int, bool> IsBad, IsFixed;
        public Func<int, int, bool> Intersects;
        public Func<int, double> Room, SizeArea;
        public Func<int, int> Dummy;
        public MethodInfo DegTris;
        public double Oriented = 100;

        public Probe(MeshRoundingRepair r)
        {
            R = r; M = r.Mesh;
            const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            Type t = typeof(MeshRoundingRepair);
            IsBad = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), r, t.GetMethod("is_bad", any, null, new[] { typeof(int) }, null));
            IsFixed = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), r, t.GetMethod("is_fixed", any));
            Intersects = (Func<int, int, bool>)Delegate.CreateDelegate(typeof(Func<int, int, bool>), r, t.GetMethod("intersects", any, null, new[] { typeof(int), typeof(int) }, null));
            Room = (Func<int, double>)Delegate.CreateDelegate(typeof(Func<int, double>), r, t.GetMethod("room_at", any));
            SizeArea = (Func<int, double>)Delegate.CreateDelegate(typeof(Func<int, double>), r, t.GetMethod("sizeArea_of", any));
            DegTris = t.GetMethod("degenerate_triangles", any);
        }

        public List<int> Bad() { return (List<int>)DegTris.Invoke(R, null); }

        public AxisAlignedBox3d Box(int t)
        {
            Index3i f = M.GetTriangle(t);
            var b = new AxisAlignedBox3d(M.GetVertex(f.a));
            b.Contain(M.GetVertex(f.b)); b.Contain(M.GetVertex(f.c));
            return b;
        }

        // intersecting pairs of the mesh with at least one triangle in `star` (each pair once)
        public int Pairs(HashSet<int> star, AxisAlignedBox3d[] boxes)
        {
            int n = 0;
            foreach (int a in star) {
                AxisAlignedBox3d ba = Box(a);
                foreach (int b in M.TriangleIndices()) {
                    if (b == a || (star.Contains(b) && b < a)) continue;
                    if (!ba.Intersects(boxes[b])) continue;
                    if (Intersects(a, b)) n++;
                }
            }
            return n;
        }
    }

    static int Joint(string[] args)
    {
        var cases = new List<string>();
        int sizes = 5;
        bool single = false;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--nojoint") JointNoJoint = true;
            else if (args[i] == "--case") cases.Add(args[++i]);
            else if (args[i] == "--sizes") sizes = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--single") single = true;
            else { Console.WriteLine("unknown argument " + args[i]); return 2; }
        }
        foreach (string spec in cases)
            JointCase(spec, sizes, single);
        return 0;
    }

    // (--nojoint: the state without the joint stage, as the oracle's positive control; the best pair is printed with its bits)
    static bool JointNoJoint;

    static void JointCase(string spec, int sizes, bool single)
    {
        if (JointNoJoint) {
            MeshRoundingRepair s0 = OracleState(spec, true);
            JointOn(spec, s0, sizes, single);
            return;
        }
        string[] f = spec.Split(',');
        var (hn, h) = Surfaces.First(x => x.Item1 == f[0]);
        int n = int.Parse(f[1], CultureInfo.InvariantCulture), dn = int.Parse(f[2], CultureInfo.InvariantCulture);
        double t = double.Parse(f[3], CultureInfo.InvariantCulture), e = double.Parse(f[4], CultureInfo.InvariantCulture);
        double ratio = double.Parse(f[5], CultureInfo.InvariantCulture), shift = double.Parse(f[6], CultureInfo.InvariantCulture);
        bool diff = f[7] == "D";
        Func<Vector3d, Vector3d> sh = p => p + new Vector3d(shift, shift * 0.5, -shift * 0.25);
        DMesh3 coarse = Plate(n, t, h, sh), fine = Plate(n + dn, t, h, p => sh(Rot(p, e, ratio * e)));
        DMesh3 a = diff ? coarse : fine, b = diff ? fine : coarse;
        var plain = new ExactMeshBoolean(a, b, D);
        plain.Compute();
        JointOn(spec, plain.Rounding, sizes, single);
    }

    static void JointOn(string spec, MeshRoundingRepair r, int sizes, bool single)
    {
        Console.WriteLine($"CASE {spec}: left deg {r.RemainingDegenerate} intersections {r.RemainingIntersections} contacts {r.RemainingFalseContacts}");
        var P = new Probe(r);
        DMesh3 m = P.M;
        List<int> bad = P.Bad();
        Console.WriteLine($"  mesh triangles {m.TriangleCount}, bad {bad.Count}, bound {r.MaxLength:G4}");
        var boxes = new AxisAlignedBox3d[m.MaxTriangleID];
        foreach (int tid in m.TriangleIndices()) boxes[tid] = P.Box(tid);
        var dirs = (Vector3d[])typeof(MeshRoundingRepair).GetField("SearchDirections", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (int tid in bad) {
            Index3i tv = m.GetTriangle(tid);
            Console.WriteLine($"  BAD triangle {tid}: corners {tv.a} {tv.b} {tv.c}");
            var movable = new List<int>();
            for (int k = 0; k < 3; ++k) {
                int v = tv[k];
                Vector3d p = m.GetVertex(v);
                bool fx = P.IsFixed(v), bd = m.IsBoundaryVertex(v), bt = m.IsBowtieVertex(v);
                double room = P.Room(v);
                int valence = m.GetVtxEdgeCount(v);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    v{0} ({1:R}, {2:R}, {3:R}) valence {4} fixed {5} boundary {6} bowtie {7} room {8:G4} ulp(max) {9:G3}",
                    v, p.x, p.y, p.z, valence, fx, bd, bt, room, Math.Pow(2, Math.Floor(Math.Log(Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))), 2)) - 52)));
                if (!fx && !bd && !bt && room > 0) movable.Add(v);
            }
            for (int k = 0; k < 3; ++k) {
                int u = tv[k], w = tv[(k + 1) % 3];
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    edge v{0}-v{1}: {2:G4}", u, w, m.GetVertex(u).Distance(m.GetVertex(w))));
            }
            Console.WriteLine($"    movable corners {movable.Count}: {string.Join(",", movable)}");
            if (single) {
                foreach (int v in movable) Enumerate(P, tid, new[] { v }, sizes, dirs, boxes);
            }
            else {
                for (int i = 0; i < movable.Count; ++i)
                    for (int j = i + 1; j < movable.Count; ++j)
                        Enumerate(P, tid, new[] { movable[i], movable[j] }, sizes, dirs, boxes);
                if (movable.Count == 1) {
                    // one free corner: pair it with each neighbour of the triangle's other corners (free ones, not corners of tid)
                    int v = movable[0];
                    var nb = new SortedSet<int>();
                    for (int k = 0; k < 3; ++k)
                        foreach (int u in m.VtxVerticesItr(tv[k]))
                            if (u != tv.a && u != tv.b && u != tv.c && !P.IsFixed(u) && !m.IsBoundaryVertex(u) && !m.IsBowtieVertex(u) && P.Room(u) > 0)
                                nb.Add(u);
                    Console.WriteLine($"    one free corner: neighbour partners {nb.Count}");
                    foreach (int u in nb)
                        Enumerate(P, tid, new[] { v, u }, sizes, dirs, boxes);
                }
            }
        }
    }

    static void Enumerate(Probe P, int tid, int[] vs, int sizes, Vector3d[] dirs, AxisAlignedBox3d[] boxes)
    {
        DMesh3 m = P.M;
        var star = new HashSet<int>();
        foreach (int v in vs) foreach (int x in m.VtxTrianglesItr(v)) star.Add(x);
        var starList = star.OrderBy(x => x).ToList();
        int oldB = starList.Count(x => P.IsBad(x));
        int oldI = P.Pairs(star, boxes);
        var oldCross = new Dictionary<int, Vector3d>();
        foreach (int x in starList) {
            Index3i f = m.GetTriangle(x);
            Vector3d p0 = m.GetVertex(f.a), p1 = m.GetVertex(f.b), p2 = m.GetVertex(f.c);
            oldCross[x] = (p1 - p0).Cross(p2 - p0);
        }
        var orig = vs.Select(v => m.GetVertex(v)).ToArray();
        var room = vs.Select(v => P.Room(v)).ToArray();
        long cheap = 0; var hist = new SortedDictionary<string, int>(); long total = 0, noBad = 0, noInter = 0, accepted = 0, acceptedNoFold = 0, removesTid = 0;
        double bestSize = double.PositiveInfinity;
        string best = "";
        Vector3d[] bestPos = null;
        Vector3d[] firstRej = null; int rejA = -1, rejB = -1;
        int[] js = new int[vs.Length];
        // candidates: per vertex (size j, direction d) or "stays" (only for pairs: one of the two may stay: a single move)
        int per = sizes * dirs.Length;
        long count = 1;
        for (int k = 0; k < vs.Length; ++k) count *= per;
        for (long c = 0; c < count; ++c) {
            long rem = c;
            double maxStepRel = 0;
            double maxStep = 0;
            for (int k = 0; k < vs.Length; ++k) {
                int idx = (int)(rem % per); rem /= per;
                int j = idx / dirs.Length, di = idx % dirs.Length;
                double s = room[k] / (1 << (sizes - 1)) * (1 << j);
                Vector3d np = orig[k] + s * dirs[di];
                m.SetVertex(vs[k], np);
                maxStepRel = Math.Max(maxStepRel, s / room[k]);
                maxStep = Math.Max(maxStep, orig[k].Distance(np));
            }
            total++;
            int newB = starList.Count(x => P.IsBad(x));
            if (newB < oldB) {
                noBad++;
                bool fold = false;
                foreach (int x in starList) {
                    Index3i f = m.GetTriangle(x);
                    Vector3d p0 = m.GetVertex(f.a), p1 = m.GetVertex(f.b), p2 = m.GetVertex(f.c);
                    Vector3d cr = (p1 - p0).Cross(p2 - p0), oc = oldCross[x];
                    double longest = Math.Max(p0.Distance(p1), Math.Max(p1.Distance(p2), p2.Distance(p0)));
                    if (oc.Length < 2 * P.Oriented * P.SizeArea(f.a) || oc.Length <= 2 * maxStep * longest) continue;
                    if (cr.Dot(oc) <= 0) { fold = true; break; }
                }
                if (!fold) cheap++;
                // intersections: the boxes of the star are recomputed
                var save = new Dictionary<int, AxisAlignedBox3d>();
                foreach (int x in starList) { save[x] = boxes[x]; boxes[x] = P.Box(x); }
                int newI = P.Pairs(star, boxes);
                foreach (int x in starList) boxes[x] = save[x];
                if (newI > oldI && firstRej == null) {
                    firstRej = vs.Select(v => m.GetVertex(v)).ToArray();
                    foreach (int xa in starList) { foreach (int xb in m.TriangleIndices()) { if (xb == xa || (star.Contains(xb) && xb < xa)) continue; if (P.Box(xa).Intersects(P.Box(xb)) && P.Intersects(xa, xb)) { rejA = xa; rejB = xb; goto found; } } }
                    found:;
                }
                if (newI <= oldI) {
                    noInter++;
                    if (newB + newI < oldB + oldI) {
                        accepted++;
                        { string key = string.Join("/", Enumerable.Range(0, vs.Length).Select(k => Math.Round(Math.Log(m.GetVertex(vs[k]).Distance(orig[k]) / room[k], 2)).ToString())); hist[key] = hist.TryGetValue(key, out int hv) ? hv + 1 : 1; }
                        if (!fold) {
                            acceptedNoFold++;
                            if (maxStepRel < bestSize) {
                                bestSize = maxStepRel;
                                bestPos = vs.Select(v => m.GetVertex(v)).ToArray();
                                best = string.Join("; ", vs.Select((v, k) => string.Format(CultureInfo.InvariantCulture, "v{0} by {1:G4} ({2:G3} of its room)", v, m.GetVertex(v).Distance(orig[k]), m.GetVertex(v).Distance(orig[k]) / room[k])));
                            }
                        }
                    }
                }
                if (!P.IsBad(tid)) removesTid++;
            }
            // (restored below)
        }
        if (bestPos != null) {
            for (int k = 0; k < vs.Length; ++k) m.SetVertex(vs[k], bestPos[k]);
            var all = (List<Index2i>)typeof(MeshRoundingRepair).GetMethod("intersecting_pairs_all", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(P.R, new object[] { false });
            Console.WriteLine($"    best candidate from scratch: bad {P.Bad().Count} intersecting pairs {all.Count}");
            Console.WriteLine("    BEST {\"moves\": [" + string.Join(",", vs.Select((v, k) => $"{{\"v\": {v}, \"p\": {HexV(bestPos[k])}}}")) + "]}");
        }
        if (firstRej != null) {
            Console.WriteLine($"    first intersection-rejected candidate: pair of triangles {rejA} {rejB}: {string.Join(" | ", new[] { rejA, rejB }.Select(x => { Index3i q = m.GetTriangle(x); return $"t{x}({q.a},{q.b},{q.c})"; }))}");
        }
        for (int k = 0; k < vs.Length; ++k) m.SetVertex(vs[k], orig[k]);
        Console.WriteLine($"    cheap pass (fewer bad, no turned-over): {cheap}; accepted by log2(step/room) of the two vertices: {string.Join(" ", hist.Select(kv => kv.Key + ":" + kv.Value))}");
        Console.WriteLine($"    ENUM {(vs.Length == 1 ? "single" : "pair")} {string.Join(",", vs)}: candidates {total}, fewer bad in star {noBad} (oldB {oldB}), no new intersection {noInter} (oldI {oldI}), accepted by the improves rule {accepted}, of them without a turned-over triangle {acceptedNoFold}; best (smallest larger step / room) {(acceptedNoFold > 0 ? bestSize.ToString("G3", CultureInfo.InvariantCulture) + " " + best : "none")}");
    }
}
