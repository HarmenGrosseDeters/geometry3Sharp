// The class metric of the sharp-feature study (2026-11-12, runs/sharp-features): sharp-edge distances by crease class.
// The input's sharp edges (dihedral > 30 degrees, as MeshFidelityMetrics) are split into convex and concave ones, and each is taken
// twice: where it is ("in") and moved onto the miter line of the offset surface ("mi": edge + delta (n1 + n2) / (1 + n1.n2), the
// intersection of the two face planes shifted outward by delta). For samples on them: the distance to the nearest sharp edge of the
// result (clamped at 2 alpha), as a mean in offsets and as recall within 1 and 3 offsets (3 offsets = 0.1 % of the diagonal at 300 / 3000).
// The other way (precision): samples on the result's sharp edges to the nearest input sharp edge (in / mi).
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

static class EdgeClass
{
    public struct Seg { public Vector3d A, B; }

    public class SegGrid
    {
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly List<Seg> segs;
        readonly double h; readonly Vector3d o;
        public SegGrid(List<Seg> s, double cell, Vector3d origin)
        {
            segs = s; h = cell; o = origin;
            for (int i = 0; i < s.Count; ++i) {
                // rasterize along the segment in steps of half a cell
                double len = s[i].A.Distance(s[i].B);
                int n = Math.Max(1, (int)Math.Ceiling(len / (0.5 * h)));
                long last = long.MinValue;
                for (int k = 0; k <= n; ++k) {
                    long key = Key(Vector3d.Lerp(s[i].A, s[i].B, (double)k / n));
                    if (key == last) continue;
                    last = key;
                    if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>();
                    if (l.Count == 0 || l[l.Count - 1] != i) l.Add(i);
                }
            }
        }
        long Key(Vector3d p) { return Key((int)Math.Floor((p.x - o.x) / h), (int)Math.Floor((p.y - o.y) / h), (int)Math.Floor((p.z - o.z) / h)); }
        static long Key(int x, int y, int z) { return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF); }
        public double Nearest(Vector3d p, double maxDist)
        {
            int cx = (int)Math.Floor((p.x - o.x) / h), cy = (int)Math.Floor((p.y - o.y) / h), cz = (int)Math.Floor((p.z - o.z) / h);
            double best = maxDist;
            int maxRing = (int)Math.Ceiling(maxDist / h) + 1;
            for (int ring = 0; ring <= maxRing; ++ring) {
                if ((ring - 1) * h > best) break;
                for (int x = -ring; x <= ring; ++x) for (int y = -ring; y <= ring; ++y) for (int z = -ring; z <= ring; ++z) {
                    if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != ring) continue;
                    if (!cells.TryGetValue(Key(cx + x, cy + y, cz + z), out var l)) continue;
                    foreach (int i in l) {
                        var sg = new Segment3d(segs[i].A, segs[i].B);
                        double d = Math.Sqrt(sg.DistanceSquared(p));
                        if (d < best) best = d;
                    }
                }
            }
            return best;
        }
    }

    public static List<(Seg seg, Seg miter, bool convex)> InputCreases(DMesh3 m, double delta, double minHeight)
    {
        var list = new List<(Seg, Seg, bool)>();
        foreach (int eid in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Vector3d n1 = m.GetTriNormal(et.a), n2 = m.GetTriNormal(et.b);
            if (n1.LengthSquared < 0.5 || n2.LengthSquared < 0.5) continue;
            double c = n1.Dot(n2);
            if (c >= Math.Cos(30 * Math.PI / 180)) continue;
            if (Height(m, et.a) < minHeight || Height(m, et.b) < minHeight) continue;
            Index2i ev = m.GetEdgeV(eid);
            Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
            // convex: the opposite vertex of the second triangle lies below the plane of the first
            Index3i t2 = m.GetTriangle(et.b);
            int opp = (t2.a != ev.a && t2.a != ev.b) ? t2.a : ((t2.b != ev.a && t2.b != ev.b) ? t2.b : t2.c);
            bool convex = n1.Dot(m.GetVertex(opp) - a) < 0;
            // miter shift; clamped at 4 offsets for nearly folded creases
            Vector3d s = (n1 + n2) / Math.Max(1 + c, 0.25) * delta;
            if (s.Length > 4 * delta) s *= 4 * delta / s.Length;
            list.Add((new Seg { A = a, B = b }, new Seg { A = a + s, B = b + s }, convex));
        }
        return list;
    }

    static List<Seg> SharpEdges(DMesh3 m, double minHeight)
    {
        var list = new List<Seg>();
        foreach (int eid in m.EdgeIndices()) {
            Index2i et = m.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Vector3d n1 = m.GetTriNormal(et.a), n2 = m.GetTriNormal(et.b);
            if (n1.LengthSquared < 0.5 || n2.LengthSquared < 0.5) continue;
            if (n1.Dot(n2) >= Math.Cos(30 * Math.PI / 180)) continue;
            if (Height(m, et.a) < minHeight || Height(m, et.b) < minHeight) continue;
            Index2i ev = m.GetEdgeV(eid);
            list.Add(new Seg { A = m.GetVertex(ev.a), B = m.GetVertex(ev.b) });
        }
        return list;
    }

    static double Height(DMesh3 m, int tid)
    {
        Index3i t = m.GetTriangle(tid);
        Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
        double area2 = (b - a).Cross(c - a).Length;
        double maxEdge = Math.Max((b - a).Length, Math.Max((c - b).Length, (a - c).Length));
        return maxEdge > 0 ? area2 / maxEdge : 0;
    }

    // points on the segments, stratified by length: n samples in total (deterministic)
    static List<Vector3d> Sample(List<Seg> segs, int n)
    {
        var pts = new List<Vector3d>();
        double total = 0;
        foreach (Seg s in segs) total += s.A.Distance(s.B);
        if (!(total > 0)) return pts;
        double step = total / n, next = 0.5 * step, acc = 0;
        foreach (Seg s in segs) {
            double len = s.A.Distance(s.B);
            while (next < acc + len) { pts.Add(Vector3d.Lerp(s.A, s.B, (next - acc) / len)); next += step; }
            acc += len;
        }
        return pts;
    }

    static void Stats(List<Vector3d> pts, SegGrid grid, double delta, double clamp, string tag, Dictionary<string, object> r)
    {
        if (pts.Count == 0 || grid == null) { r[tag + "_n"] = pts.Count; return; }
        double sum = 0; int w1 = 0, w3 = 0;
        var ds = new double[pts.Count];
        gParallel.ForEach(Interval1i.Range(pts.Count), (i) => { ds[i] = grid.Nearest(pts[i], clamp); });
        foreach (double d in ds) { sum += d; if (d <= delta) w1++; if (d <= 3 * delta) w3++; }
        r[tag + "_n"] = pts.Count;
        r[tag + "_mean"] = sum / pts.Count / delta;
        r[tag + "_w1"] = (double)w1 / pts.Count;
        r[tag + "_w3"] = (double)w3 / pts.Count;
    }

    public static void Add(DMesh3 orientedInput, DMesh3 result, double alpha, double delta, Dictionary<string, object> r, int samples = 20000)
    {
        double size = orientedInput.CachedBounds.DiagonalLength;
        double minH = 1e-5 * size, clamp = 2 * alpha;
        var creases = InputCreases(orientedInput, delta, minH);
        var res = SharpEdges(result, minH);
        Vector3d origin = orientedInput.CachedBounds.Min - 4 * alpha * Vector3d.One;
        double cell = Math.Max(alpha, 1e-12);
        SegGrid resGrid = res.Count > 0 ? new SegGrid(res, cell, origin) : null;
        double lenCvx = 0, lenCcv = 0;
        foreach (var c in creases) { double l = c.seg.A.Distance(c.seg.B); if (c.convex) lenCvx += l; else lenCcv += l; }
        r["ec_len_cvx"] = lenCvx / size; r["ec_len_ccv"] = lenCcv / size;
        double lenRes = 0; foreach (Seg s in res) lenRes += s.A.Distance(s.B);
        r["ec_len_res"] = lenRes / size;
        foreach (bool convex in new[] { true, false }) {
            string cls = convex ? "cvx" : "ccv";
            Stats(Sample(creases.Where(c => c.convex == convex).Select(c => c.seg).ToList(), samples), resGrid, delta, clamp, "ec_" + cls + "_in", r);
            Stats(Sample(creases.Where(c => c.convex == convex).Select(c => c.miter).ToList(), samples), resGrid, delta, clamp, "ec_" + cls + "_mi", r);
        }
        // precision: the result's sharp edges against the input's (all classes)
        var resPts = Sample(res, samples);
        if (creases.Count > 0) {
            Stats(resPts, new SegGrid(creases.Select(c => c.seg).ToList(), cell, origin), delta, clamp, "ec_prec_in", r);
            Stats(resPts, new SegGrid(creases.Select(c => c.miter).ToList(), cell, origin), delta, clamp, "ec_prec_mi", r);
        }
    }

    /// <summary> exact count of the result's triangles that cross an input triangle </summary>
    public static int Crossings(DMesh3 m, DMesh3 input, double cell)
    {
        var grid = new TriangleHashGrid(input.CachedBounds.Min, Math.Max(cell, 1e-12));
        foreach (int tid in input.TriangleIndices()) grid.Insert(tid, Snap.Box(Snap.Tri(input, tid)));
        int tCross = 0;
        foreach (int tid in m.TriangleIndices()) {
            Triangle3d t = Snap.Tri(m, tid);
            bool cross = false;
            grid.Query(Snap.Box(t), (it) => {
                if (cross) return;
                Triangle3d u = Snap.Tri(input, it);
                if (ExactTriangleIntersection.IsDegenerate(ref u)) return;
                if (ExactTriangleIntersection.Intersects(ref t, ref u)) cross = true;
            });
            if (cross) tCross++;
        }
        return tCross;
    }
}
