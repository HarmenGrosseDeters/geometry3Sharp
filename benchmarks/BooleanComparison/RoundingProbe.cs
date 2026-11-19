// Measurement-only study (owner go 2026-11-19, branch runs/rounding-probes): why does the final rounding of an exact Boolean result
// lose or break it? No library behaviour is changed; nothing here is part of the library. One JSON row per probed case:
//   rounding-probe <A.off> <B.off> <C.off> <ucd|dui|iud>        the step-3 result of an abc-chained plan
//   rounding-probe-proc <mixed|generic> <seed> <steps> x         the step-<steps> result of a procedural chain
// Questions (per case):
//   Q1  per component of the exact result E (SafeRounding off, exact vertices): thickness (2V/A, and the smallest distance along the
//       inward normal to the opposite sheet, both in exact fixed-point arithmetic, 2^-320 absolute) against the ulp of the
//       coordinates; whether the component is kept, broken or removed by SafeRounding; whether the region exists in the exact
//       results of pair operations on the original operands (unchained) and in the exact result of today's chained last step.
//   Q2  defects left after SafeRounding (R): how many lie in an exactly coplanar convex quad (exact points of the vertex ids) whose
//       other diagonal gives two triangles of exact positive orientation at the rounded coordinates and does not add a defect
//       (the existing detector of MeshRoundingRepair, reflection); the flips applied and NotCleanReason after them.
//   Q3  local origin: the exact result's vertices minus an origin (exactly), rounded, the same repair; origins 0, the bounding box
//       centre (a double), an exact vertex of the thinnest component.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using g3;
using gs;

static class RoundingProbe
{
    const int F = 320;      // fractional bits of the fixed-point coordinates (absolute error 2^-320 of the unit)

    // ------------------------------------------------------------------ small helpers

    static double Big(BigInteger v, int exp2) { return v.IsZero ? 0 : ExactPoint3.ToDouble(v.Sign < 0 ? -v : v, BigInteger.One, exp2) * (v.Sign < 0 ? -1 : 1); }

    static double Ratio(BigInteger num, BigInteger den)
    {
        if (den.Sign < 0) { num = -num; den = -den; }
        if (num.IsZero) return 0;
        double r = ExactPoint3.ToDouble(num.Sign < 0 ? -num : num, den, 0);
        return num.Sign < 0 ? -r : r;
    }

    static BigInteger Fix(BigInteger x, BigInteger w) { return BigInteger.Divide(x << F, w); }

    static BigInteger FixD(double x) { return x == 0 ? BigInteger.Zero : new BigInteger(Math.ScaleB(x, F)); }

    static double ToD(ExactPoint3 p, int k) { return ExactPoint3.ToDouble(p[k], p.W, 0); }

    static double Ulp(double x) { x = Math.Abs(x); return Math.BitIncrement(x) - x; }

    static BigInteger[] Cross(BigInteger[] a, BigInteger[] b)
    {
        return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    }
    static BigInteger Dot(BigInteger[] a, BigInteger[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
    static BigInteger[] Sub(BigInteger[] a, BigInteger[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }

    // exact orientation sign of four rational points (homogeneous 4x4 determinant, W > 0): the sign of orient3d
    static int Orient3(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
    {
        // rows (X, Y, Z, W); subtract-free expansion along the W column
        BigInteger[][] r = { new[] { a.X, a.Y, a.Z, a.W }, new[] { b.X, b.Y, b.Z, b.W }, new[] { c.X, c.Y, c.Z, c.W }, new[] { d.X, d.Y, d.Z, d.W } };
        BigInteger det = Det4(r);
        return det.Sign;
    }
    static BigInteger Det3(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger e, BigInteger f, BigInteger g, BigInteger h, BigInteger i)
    {
        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }
    static BigInteger Det4(BigInteger[][] m)
    {
        BigInteger s = 0;
        for (int col = 0; col < 4; ++col) {
            int[] o = Enumerable.Range(0, 4).Where(x => x != col).ToArray();
            BigInteger minor = Det3(m[1][o[0]], m[1][o[1]], m[1][o[2]], m[2][o[0]], m[2][o[1]], m[2][o[2]], m[3][o[0]], m[3][o[1]], m[3][o[2]]);
            s += ((col & 1) == 0 ? 1 : -1) * m[0][col] * minor;
        }
        return s;
    }
    // exact orient2d of rational points projected along axis `drop` (W > 0)
    static int Orient2Proj(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int drop)
    {
        int i = (drop + 1) % 3, j = (drop + 2) % 3;
        return Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W).Sign;
    }

    static string Reason(DMesh3 m) { return MeshRobustRepair.NotCleanReason(m, false); }
    static bool CleanReason(string r) { return r == "" || r == "empty"; }

    // ------------------------------------------------------------------ exact mesh view and components

    sealed class Exact
    {
        public DMesh3 M;
        public ExactPoint3[] P;                 // per vertex id of M
        public int[] Weld;                      // per vertex id: welded point id (equal exact points share one)
        public BigInteger[][] Fx;               // per welded id: fixed-point coordinates
        public List<ExactPoint3> WeldPoints = new List<ExactPoint3>();

        public Exact(DMesh3 m, ExactPoint3[] p)
        {
            M = m; P = p;
            Weld = new int[m.MaxVertexID];
            var map = new Dictionary<ExactPoint3, int>();
            foreach (int v in m.VertexIndices()) {
                int id;
                if (!map.TryGetValue(p[v], out id)) { map[p[v]] = id = WeldPoints.Count; WeldPoints.Add(p[v]); }
                Weld[v] = id;
            }
            Fx = new BigInteger[WeldPoints.Count][];
            for (int i = 0; i < Fx.Length; ++i) {
                ExactPoint3 q = WeldPoints[i];
                Fx[i] = new[] { Fix(q.X, q.W), Fix(q.Y, q.W), Fix(q.Z, q.W) };
            }
        }

        public Index3i WTri(int t) { Index3i f = M.GetTriangle(t); return new Index3i(Weld[f.a], Weld[f.b], Weld[f.c]); }

        public List<List<int>> Components()
        {
            int maxT = M.MaxTriangleID;
            var parent = new int[maxT];
            for (int i = 0; i < maxT; ++i) parent[i] = i;
            int find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var first = new Dictionary<long, int>();
            foreach (int t in M.TriangleIndices()) {
                Index3i w = WTri(t);
                if (w.a == w.b || w.b == w.c || w.a == w.c) continue;
                for (int k = 0; k < 3; ++k) {
                    int u = w[k], v = w[(k + 1) % 3];
                    long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
                    int o;
                    if (first.TryGetValue(key, out o)) parent[find(t)] = find(o); else first[key] = t;
                }
            }
            var groups = new SortedDictionary<int, List<int>>();
            foreach (int t in M.TriangleIndices()) {
                int r = find(t);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
                l.Add(t);
            }
            return groups.Values.ToList();
        }
    }

    // exact signed volume x 6 (fixed-point units^3) and area of a triangle list of the exact mesh
    static void VolArea(Exact e, List<int> tris, out BigInteger v6, out double area, out int openEdges)
    {
        v6 = BigInteger.Zero; area = 0;
        BigInteger[] o = e.Fx[e.WTri(tris[0]).a];
        var edgeCount = new Dictionary<long, int>();
        foreach (int t in tris) {
            Index3i w = e.WTri(t);
            BigInteger[] a = Sub(e.Fx[w.a], o), b = Sub(e.Fx[w.b], o), c = Sub(e.Fx[w.c], o);
            v6 += Dot(a, Cross(b, c));
            BigInteger[] n = Cross(Sub(b, a), Sub(c, a));
            double nx = Big(n[0], -2 * F), ny = Big(n[1], -2 * F), nz = Big(n[2], -2 * F);
            area += 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
            for (int k = 0; k < 3; ++k) {
                int u = w[k], v = w[(k + 1) % 3];
                long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
                edgeCount.TryGetValue(key, out int cnt);
                edgeCount[key] = cnt + (u < v ? 1 : -1);
            }
        }
        openEdges = edgeCount.Values.Count(x => x != 0);
    }

    sealed class CompInfo
    {
        public int Tris;
        public double Volume, Area, ThicknessAvg, ThicknessMin, Ulp, Diag, MaxAbs;
        public bool Capped;
        public int OpenEdges;
        public double[] Lo = new double[3], Hi = new double[3];
        public HashSet<int> Verts = new HashSet<int>();     // vertex ids of M
        public int KeptTris, DefectTris;
        public string Class;
    }

    // smallest distance along the inward normal from the centroid of a triangle to another triangle of the component (exact fixed-point
    // arithmetic for the hit, a grid of double boxes as the broad phase); capped at 2^-20 of the diagonal; at most maxRays triangles
    static double MinOpposite(Exact e, List<int> tris, double sign, double diag, int maxRays, out bool capped)
    {
        capped = true;
        double cap = diag * Math.Pow(2, -20);
        int n = tris.Count;
        var lo = new double[n][]; var hi = new double[n][];
        double[] glo = { double.MaxValue, double.MaxValue, double.MaxValue }, ghi = { double.MinValue, double.MinValue, double.MinValue };
        for (int i = 0; i < n; ++i) {
            Index3i f = e.M.GetTriangle(tris[i]);
            lo[i] = new double[3]; hi[i] = new double[3];
            for (int k = 0; k < 3; ++k) {
                double a = ToD(e.P[f.a], k), b = ToD(e.P[f.b], k), c = ToD(e.P[f.c], k);
                lo[i][k] = Math.Min(a, Math.Min(b, c)); hi[i][k] = Math.Max(a, Math.Max(b, c));
                glo[k] = Math.Min(glo[k], lo[i][k]); ghi[k] = Math.Max(ghi[k], hi[i][k]);
            }
        }
        double pad = diag * 1e-9;
        int res = Math.Max(1, Math.Min(64, (int)Math.Round(Math.Pow(n, 1.0 / 3.0) * 2)));
        double[] cell = new double[3];
        for (int k = 0; k < 3; ++k) cell[k] = Math.Max((ghi[k] - glo[k]) / res, 1e-300);
        int ci(int k, double x) { return Math.Max(0, Math.Min(res - 1, (int)((x - glo[k]) / cell[k]))); }
        var grid = new Dictionary<long, List<int>>();
        for (int i = 0; i < n; ++i)
            for (int x = ci(0, lo[i][0] - pad); x <= ci(0, hi[i][0] + pad); ++x)
                for (int y = ci(1, lo[i][1] - pad); y <= ci(1, hi[i][1] + pad); ++y)
                    for (int z = ci(2, lo[i][2] - pad); z <= ci(2, hi[i][2] + pad); ++z) {
                        long key = ((long)x * res + y) * res + z;
                        if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                        l.Add(i);
                    }
        double best = double.MaxValue;
        int step = Math.Max(1, n / maxRays);
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i += step) {
            Index3i fi = e.WTri(tris[i]);
            BigInteger[] A = e.Fx[fi.a], B = e.Fx[fi.b], C = e.Fx[fi.c];
            BigInteger[] nt = Cross(Sub(B, A), Sub(C, A));
            if (nt[0].IsZero && nt[1].IsZero && nt[2].IsZero) continue;
            BigInteger[] d = sign > 0 ? new[] { -nt[0], -nt[1], -nt[2] } : nt;     // inward
            double nlen = Math.Sqrt(Math.Pow(Big(nt[0], -2 * F), 2) + Math.Pow(Big(nt[1], -2 * F), 2) + Math.Pow(Big(nt[2], -2 * F), 2));
            double[] c = new double[3];
            Index3i f = e.M.GetTriangle(tris[i]);
            for (int k = 0; k < 3; ++k) c[k] = (ToD(e.P[f.a], k) + ToD(e.P[f.b], k) + ToD(e.P[f.c], k)) / 3;
            double[] dir = { Big(d[0], -2 * F) / nlen, Big(d[1], -2 * F) / nlen, Big(d[2], -2 * F) / nlen };
            double reach = Math.Min(cap, best) + pad;
            double[] rlo = new double[3], rhi = new double[3];
            for (int k = 0; k < 3; ++k) {
                double a = c[k] - pad, b = c[k] + dir[k] * reach + pad;
                rlo[k] = Math.Min(a, b); rhi[k] = Math.Max(a, b);
            }
            seen.Clear();
            BigInteger[] P0 = { A[0] + B[0] + C[0], A[1] + B[1] + C[1], A[2] + B[2] + C[2] };      // 3 x centroid
            for (int x = ci(0, rlo[0]); x <= ci(0, rhi[0]); ++x)
                for (int y = ci(1, rlo[1]); y <= ci(1, rhi[1]); ++y)
                    for (int z = ci(2, rlo[2]); z <= ci(2, rhi[2]); ++z) {
                        if (!grid.TryGetValue(((long)x * res + y) * res + z, out var l)) continue;
                        foreach (int j in l) {
                            if (j == i || !seen.Add(j)) continue;
                            bool near = true;
                            for (int k = 0; k < 3; ++k) if (hi[j][k] + pad < rlo[k] || lo[j][k] - pad > rhi[k]) near = false;
                            if (!near) continue;
                            Index3i fj = e.WTri(tris[j]);
                            if (fj.a == fi.a || fj.a == fi.b || fj.a == fi.c || fj.b == fi.a || fj.b == fi.b || fj.b == fi.c || fj.c == fi.a || fj.c == fi.b || fj.c == fi.c) continue;
                            BigInteger[] U0 = Mul3(e.Fx[fj.a]), U1 = Mul3(e.Fx[fj.b]), U2 = Mul3(e.Fx[fj.c]);
                            BigInteger[] nu = Cross(Sub(U1, U0), Sub(U2, U0));
                            BigInteger den = Dot(nu, d);
                            if (den.IsZero) continue;
                            BigInteger num = Dot(nu, Sub(U0, P0));
                            if (den.Sign < 0) { num = -num; den = -den; }
                            if (num.Sign <= 0) continue;
                            // hit point H = P0 + num/den d, scaled by den
                            BigInteger[] H = { P0[0] * den + num * d[0], P0[1] * den + num * d[1], P0[2] * den + num * d[2] };
                            bool inside = true;
                            BigInteger[][] V = { U0, U1, U2 };
                            for (int k = 0; k < 3 && inside; ++k) {
                                BigInteger[] a0 = V[k], a1 = V[(k + 1) % 3];
                                BigInteger[] ev = Sub(a1, a0);
                                BigInteger[] w = { H[0] - a0[0] * den, H[1] - a0[1] * den, H[2] - a0[2] * den };
                                if (Dot(nu, Cross(ev, w)).Sign < 0) inside = false;
                            }
                            if (!inside) continue;
                            // distance (integer units of the 3x scaled positions -> real): s |d| / 3 * 2^-F, s = num / den, |d| = nlen * 2^(2F)
                            double dist = Ratio(num, den) * nlen * Math.Pow(2, F) / 3;
                            if (dist < best) best = dist;
                        }
                    }
        }
        if (best <= cap) capped = false;
        return best == double.MaxValue ? double.PositiveInfinity : best;
    }
    static BigInteger[] Mul3(BigInteger[] a) { return new[] { 3 * a[0], 3 * a[1], 3 * a[2] }; }

    static List<CompInfo> Analyse(Exact e, List<List<int>> comps, int maxRays)
    {
        var res = new List<CompInfo>();
        foreach (var tris in comps) {
            var ci = new CompInfo { Tris = tris.Count };
            for (int k = 0; k < 3; ++k) { ci.Lo[k] = double.MaxValue; ci.Hi[k] = double.MinValue; }
            foreach (int t in tris) {
                Index3i f = e.M.GetTriangle(t);
                foreach (int v in new[] { f.a, f.b, f.c }) {
                    if (!ci.Verts.Add(v)) continue;
                    for (int k = 0; k < 3; ++k) { double x = ToD(e.P[v], k); ci.Lo[k] = Math.Min(ci.Lo[k], x); ci.Hi[k] = Math.Max(ci.Hi[k], x); ci.MaxAbs = Math.Max(ci.MaxAbs, Math.Abs(x)); }
                }
            }
            ci.Diag = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(ci.Hi[k] - ci.Lo[k], 2)));
            ci.Ulp = Ulp(ci.MaxAbs);
            BigInteger v6; double area; int open;
            VolArea(e, tris, out v6, out area, out open);
            ci.OpenEdges = open;
            ci.Area = area;
            ci.Volume = Big(v6, -3 * F) / 6;
            ci.ThicknessAvg = area > 0 ? Math.Abs(2 * ci.Volume) / area : 0;
            ci.ThicknessMin = tris.Count >= 4 && open == 0 ? MinOpposite(e, tris, v6.Sign >= 0 ? 1 : -1, ci.Diag, maxRays, out bool cap) : double.NaN;
            ci.Capped = ci.ThicknessMin > ci.Diag * Math.Pow(2, -20) && !double.IsNaN(ci.ThicknessMin);
            res.Add(ci);
        }
        return res;
    }

    // real thin feature / chain artefact / unclear, from the thickness against the ulp (documented rule: see the report)
    static string Classify(CompInfo c)
    {
        // by the average thickness 2V/A against the ulp of the largest coordinate (the smallest opposite distance is reported
        // next to it: a thick body can have a thin spot): sub-few-ulp slab (artefact) up to 4 ulps, real from 16 ulps
        double ua = c.ThicknessAvg / c.Ulp;
        return ua <= 4 ? "artefact" : ua >= 16 ? "real" : "unclear";
    }

    // ------------------------------------------------------------------ defects (the repair's own detector, reflection)

    static MeshRoundingRepair Detector(DMesh3 m, Exact e, Func<int, bool> fixedF)
    {
        var r = new MeshRoundingRepair(m) {
            SharedPositionsTouch = true, FixedVertexF = fixedF, FixedWhereCoarse = false, MaxPasses = 0,
            PointIdF = v => v < e.Weld.Length ? e.Weld[v] : -1, ExactVertexF = fixedF,
        };
        r.Apply();
        return r;
    }
    static T Priv<T>(MeshRoundingRepair r, string name) { return (T)typeof(MeshRoundingRepair).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(r, null); }

    static Func<int, bool> FixedFor(DMesh3 m, Exact e)
    {
        // a vertex at its exact position (the exact point is a double vector): kept where it is
        return v => {
            if (v >= e.P.Length || !m.IsVertex(v)) return false;
            Vector3d p = m.GetVertex(v);
            ExactPoint3 q = e.P[v];
            for (int k = 0; k < 3; ++k) if (!IsDouble(q[k], q.W) || ToD(q, k) != p[k]) return false;
            return true;
        };
    }
    static bool IsDouble(BigInteger x, BigInteger w)
    {
        if (x.IsZero) return true;
        if (!(w & (w - 1)).IsZero) return false;
        BigInteger ax = BigInteger.Abs(x);
        while (ax.IsEven) ax >>= 1;
        return ax.GetBitLength() <= 53;
    }

    sealed class Q2Result
    {
        public int DefectTotal, Degenerate, Pairs, Fixable, Applied;
        public string ReasonBefore, ReasonAfter;
        public bool CleanAfter;
        public int Candidates, NotInExact;
    }

    static int DefectCount(DMesh3 m, Exact e, Func<int, bool> fx, out List<int> bad, out List<Index2i> pairs)
    {
        if (m.TriangleCount == 0) { bad = new List<int>(); pairs = new List<Index2i>(); return 0; }
        var det = Detector(m, e, fx);
        bad = Priv<List<int>>(det, "degenerate_triangles");
        pairs = Priv<List<Index2i>>(det, "intersecting_pairs");
        return det.InitialDegenerate + det.InitialIntersections;
    }

    static Q2Result QuadFlips(DMesh3 r, Exact e, int maxCandidates)
    {
        var res = new Q2Result { ReasonBefore = Reason(r) };
        var work = new DMesh3(r, false, MeshComponents.None);
        Func<int, bool> fx = FixedFor(work, e);
        int total0 = DefectCount(work, e, fx, out var bad, out var pairs);
        var defectTris = new SortedSet<int>(bad);
        foreach (var p in pairs) { defectTris.Add(p.a); defectTris.Add(p.b); }
        res.Degenerate = bad.Count; res.Pairs = pairs.Count; res.DefectTotal = defectTris.Count;
        // candidate flips: per defect triangle its edges whose quad is exactly coplanar, convex, and positively oriented when rounded
        var cand = new List<(int t, int eid)>();
        foreach (int t in defectTris) {
            Index3i f = work.GetTriangle(t);
            for (int k = 0; k < 3; ++k) {
                int eid = work.FindEdge(f[k], f[(k + 1) % 3]);
                if (eid < 0 || work.IsBoundaryEdge(eid)) continue;
                if (QuadOk(work, e, eid, out bool inExact)) cand.Add((t, eid));
                if (!inExact) res.NotInExact++;
            }
        }
        res.Candidates = cand.Count;
        // per defect triangle: does some candidate flip lower the defect count (on its own, from the state of R)?
        var fixable = new Dictionary<int, (int eid, int a, int b)>();
        int tested = 0;
        foreach (var c in cand) {
            if (fixable.ContainsKey(c.t) || tested++ >= maxCandidates) continue;
            var trial = new DMesh3(work, false, MeshComponents.None);
            Index2i ev = trial.GetEdgeV(c.eid);
            if (trial.FlipEdge(c.eid, out var info) != MeshResult.Ok) continue;
            int tot = DefectCount(trial, e, FixedFor(trial, e), out _, out _);
            if (tot < total0) fixable[c.t] = (c.eid, ev.a, ev.b);
        }
        res.Fixable = fixable.Count;
        // apply them one after the other, each only if it still lowers the defect count on the current mesh
        int cur = total0;
        foreach (var kv in fixable) {
            int eid = work.FindEdge(kv.Value.a, kv.Value.b);
            if (eid < 0 || work.IsBoundaryEdge(eid) || !QuadOk(work, e, eid, out _)) continue;
            var trial = new DMesh3(work, false, MeshComponents.None);
            if (trial.FlipEdge(eid, out var info) != MeshResult.Ok) continue;
            int tot = DefectCount(trial, e, FixedFor(trial, e), out _, out _);
            if (tot < cur) { work = trial; cur = tot; res.Applied++; }
        }
        res.ReasonAfter = Reason(work);
        res.CleanAfter = CleanReason(res.ReasonAfter);
        return res;
    }

    // the quad of the edge: four exact points coplanar, convex (the diagonals cross), both flipped triangles positively oriented at the rounded coordinates
    static bool QuadOk(DMesh3 m, Exact e, int eid, out bool inExact)
    {
        inExact = true;
        Index2i ev = m.GetEdgeV(eid), et = m.GetEdgeT(eid);
        int a = ev.a, b = ev.b;
        int c = IndexUtil.orient_tri_edge_and_find_other_vtx(ref a, ref b, m.GetTriangle(et.a).array);
        int d = IndexUtil.find_tri_other_vtx(a, b, m.GetTriangle(et.b).array);
        if (c < 0 || d < 0 || a >= e.P.Length || b >= e.P.Length || c >= e.P.Length || d >= e.P.Length) { inExact = false; return false; }
        ExactPoint3 pa = e.P[a], pb = e.P[b], pc = e.P[c], pd = e.P[d];
        if (Orient3(pa, pb, pc, pd) != 0) return false;
        // dominant axis of the plane normal (exact, then as double)
        BigInteger[] u = { pb.X * pa.W - pa.X * pb.W, pb.Y * pa.W - pa.Y * pb.W, pb.Z * pa.W - pa.Z * pb.W };     // (b - a) x (W_a W_b), up to a positive factor
        BigInteger[] v = { pc.X * pa.W - pa.X * pc.W, pc.Y * pa.W - pa.Y * pc.W, pc.Z * pa.W - pa.Z * pc.W };
        BigInteger[] n = Cross(u, v);
        int drop = 0;
        for (int k = 1; k < 3; ++k) if (BigInteger.Abs(n[k]) > BigInteger.Abs(n[drop])) drop = k;
        if (n[drop].IsZero) return false;
        // convex: c, d on opposite sides of ab and a, b on opposite sides of cd, all strict
        int s1 = Orient2Proj(pa, pb, pc, drop), s2 = Orient2Proj(pa, pb, pd, drop), s3 = Orient2Proj(pc, pd, pa, drop), s4 = Orient2Proj(pc, pd, pb, drop);
        if (s1 == 0 || s2 == 0 || s3 == 0 || s4 == 0 || s1 == s2 || s3 == s4) return false;
        // the flipped triangles (c, d, b) and (d, c, a) at the rounded positions, projected along the same axis: the sign of the exact orientation
        int e1 = Orient2Proj(pc, pd, pb, drop), e2 = Orient2Proj(pd, pc, pa, drop);
        int i = (drop + 1) % 3, j = (drop + 2) % 3;
        int r1 = AdaptivePredicates.Orient2d(m.GetVertex(c)[i], m.GetVertex(c)[j], m.GetVertex(d)[i], m.GetVertex(d)[j], m.GetVertex(b)[i], m.GetVertex(b)[j]);
        int r2 = AdaptivePredicates.Orient2d(m.GetVertex(d)[i], m.GetVertex(d)[j], m.GetVertex(c)[i], m.GetVertex(c)[j], m.GetVertex(a)[i], m.GetVertex(a)[j]);
        return r1 != 0 && r2 != 0 && r1 == e1 && r2 == e2;
    }

    // ------------------------------------------------------------------ Q3: the exact result rounded about an origin

    sealed class OriginRef { public string Name; public BigInteger X, Y, Z, W; }

    static DMesh3 LocalMesh(Exact e, OriginRef o, out bool exactSub)
    {
        var m = new DMesh3(e.M, false, MeshComponents.None);
        exactSub = true;
        foreach (int v in e.M.VertexIndices()) {
            ExactPoint3 p = e.P[v];
            // (p - o) = (X Wo - Xo W) / (W Wo)
            BigInteger den = p.W * o.W;
            double x = ExactPoint3.ToDouble(p.X * o.W - o.X * p.W, den, 0), y = ExactPoint3.ToDouble(p.Y * o.W - o.Y * p.W, den, 0), z = ExactPoint3.ToDouble(p.Z * o.W - o.Z * p.W, den, 0);
            m.SetVertex(v, new Vector3d(x, y, z));
        }
        return m;
    }

    static Dictionary<string, object> RunLocal(Exact e, OriginRef o, double exactVolume, ExactMeshBoolean defaults, bool keepWhenEmptied)
    {
        var row = new Dictionary<string, object> { ["origin"] = o.Name };
        DMesh3 m = LocalMesh(e, o, out bool exactSub);
        var fe = new Exact(m, e.P);       // (weld by the exact points of the original frame)
        row["max_abs_coord"] = m.TriangleCount > 0 ? m.VertexIndices().Max(v => Math.Max(Math.Abs(m.GetVertex(v).x), Math.Max(Math.Abs(m.GetVertex(v).y), Math.Abs(m.GetVertex(v).z)))) : 0.0;
        row["first_rounding_reason"] = Reason(m);
        DMesh3 first = new DMesh3(m, false, MeshComponents.None);
        // exactly representable vertices stay (as the boolean keeps input points): the local coordinate equals the exact local value
        var exactAt = new HashSet<int>();
        foreach (int v in m.VertexIndices()) {
            ExactPoint3 p = e.P[v];
            BigInteger den = p.W * o.W;
            Vector3d q = m.GetVertex(v);
            bool ok = true;
            BigInteger[] num = { p.X * o.W - o.X * p.W, p.Y * o.W - o.Y * p.W, p.Z * o.W - o.Z * p.W };
            for (int k = 0; k < 3 && ok; ++k) ok = IsDouble(num[k], den) && ExactPoint3.ToDouble(num[k], den, 0) == q[k];
            if (ok) exactAt.Add(v);
        }
        Func<int, bool> fx = v => exactAt.Contains(v);
        var rr = new MeshRoundingRepair(m) {
            SharedPositionsTouch = true, FixedVertexF = fx, FixedWhereCoarse = false, PointIdF = v => v < e.Weld.Length ? e.Weld[v] : -1, ExactVertexF = fx,
            UlpFactor = defaults.RoundingUlpFactor, AbsoluteTolerance = defaults.RoundingAbsoluteTolerance, MaxDisplacement = defaults.RoundingMaxDisplacement,
            MaxPairTests = defaults.RoundingMaxPairTests, PairTestsDefectScale = defaults.RoundingPairTestsDefectScale, PairTestsMaxGrowth = defaults.RoundingPairTestsMaxGrowth,
            KeepWhenEmptied = keepWhenEmptied,
        };
        rr.Apply();
        row["clean_flag"] = rr.IsClean;
        row["reason"] = Reason(m);
        row["reason_full"] = MeshRobustRepair.NotCleanReason(m, true);
        row["tris"] = m.TriangleCount;
        row["remaining"] = new[] { rr.RemainingIntersections, rr.RemainingDegenerate, rr.RemainingFalseContacts };
        row["removed_components"] = rr.RemovedComponents;
        row["evaluation_limit"] = rr.EvaluationLimitReached;
        row["max_drift"] = rr.MaxDrift;
        var stat = MeshStats(m);
        row["components"] = stat.count;
        row["volume"] = stat.volume;
        row["volume_ratio_to_exact"] = exactVolume != 0 ? stat.volume / exactVolume : 0;
        return row;
    }

    // component count (edge-connected by vertex id) and exact volume of a double mesh
    static (int count, double volume) MeshStats(DMesh3 m)
    {
        if (m.TriangleCount == 0) return (0, 0);
        var ex = new ExactPoint3[m.MaxVertexID];
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            ex[v] = ExactPoint3.Create(FixD(p.x), FixD(p.y), FixD(p.z), BigInteger.One << F);
        }
        // (the welding is by the double position: the same as by exact point)
        var e = new Exact(m, ex);
        var comps = e.Components();
        double vol = 0;
        foreach (var c in comps) { VolArea(e, c, out BigInteger v6, out double _, out int _); vol += Big(v6, -3 * F) / 6; }
        return (comps.Count, vol);
    }

    // ------------------------------------------------------------------ the case

    static string F3(double v) { return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }

    static Dictionary<string, object> CompRow(CompInfo c, int index)
    {
        return new Dictionary<string, object> {
            ["tris"] = c.Tris, ["volume"] = c.Volume, ["area"] = c.Area, ["t_avg"] = c.ThicknessAvg, ["t_min"] = double.IsInfinity(c.ThicknessMin) ? -1 : c.ThicknessMin,
            ["t_min_capped"] = c.Capped, ["ulp"] = c.Ulp, ["t_avg_ulps"] = c.ThicknessAvg / c.Ulp, ["t_min_ulps"] = double.IsNaN(c.ThicknessMin) ? -2 : double.IsInfinity(c.ThicknessMin) ? -1 : c.ThicknessMin / c.Ulp,
            ["diag"] = c.Diag, ["open_edges"] = c.OpenEdges, ["class"] = c.Class, ["kept_tris"] = c.KeptTris, ["defect_tris"] = c.DefectTris,
        };
    }

    static Dictionary<string, object> Summarize(List<CompInfo> comps, bool withKept = true)
    {
        var d = new Dictionary<string, object>();
        foreach (string cls in new[] { "real", "artefact", "unclear" }) {
            var l = comps.Where(c => c.Class == cls).ToList();
            if (!withKept) { d[cls] = new Dictionary<string, object> { ["n"] = l.Count, ["tris"] = l.Sum(c => c.Tris), ["volume"] = l.Sum(c => c.Volume) }; continue; }
            d[cls] = new Dictionary<string, object> {
                ["n"] = l.Count, ["tris"] = l.Sum(c => c.Tris), ["volume"] = l.Sum(c => c.Volume),
                ["lost"] = l.Count(c => c.KeptTris == 0), ["broken"] = l.Count(c => c.KeptTris > 0 && c.DefectTris > 0), ["ok"] = l.Count(c => c.KeptTris > 0 && c.DefectTris == 0),
            };
        }
        return d;
    }

    internal static int Run(List<DMesh3> ops, List<ChainExact.Step> plan, Dictionary<string, object> info)
    {
        var row = new Dictionary<string, object>(info);
        int k = plan.Count - 1;
        row["step"] = k + 1; row["op"] = plan[k].Op.ToString();
        // today's chain up to step k
        DMesh3 prev = null, prevBefore = null;
        bool alive = true;
        ExactMeshBoolean last = null;
        for (int i = 0; i <= k && alive; ++i) {
            var b = new ExactMeshBoolean(plan[i].Left < 0 ? prev : ops[plan[i].Left], ops[plan[i].Right], plan[i].Op);
            bool ok = b.Compute();
            if (i == k) {
                last = b;
                row["chained_ok"] = ok;
                row["chained_tris"] = ok ? b.Result.TriangleCount : -1;
                row["chained_reason"] = ok ? Reason(b.Result) : "failed";
                row["chained_removed_components"] = b.Rounding?.RemovedComponents ?? 0;
            }
            if (ok && b.Result.TriangleCount > 0) { prevBefore = prev; prev = b.Result; } else alive = false;
        }
        // (prev is the result of step k only if the chain is alive; the left operand of step k is the result of step k-1)
        DMesh3 leftOfLast = null;
        if (k > 0 && plan[k].Left < 0) {
            DMesh3 p = null; bool al = true;
            for (int i = 0; i < k && al; ++i) {
                var b = new ExactMeshBoolean(plan[i].Left < 0 ? p : ops[plan[i].Left], ops[plan[i].Right], plan[i].Op);
                if (b.Compute() && b.Result.TriangleCount > 0) p = b.Result; else al = false;
            }
            if (al) leftOfLast = p;
        }
        // one pass over the operands
        var used = new SortedSet<int>();
        for (int i = 0; i <= k; ++i) { if (plan[i].Left >= 0) used.Add(plan[i].Left); used.Add(plan[i].Right); }
        var slot = new Dictionary<int, int>();
        var meshes = new List<DMesh3>();
        foreach (int x in used) { slot[x] = meshes.Count; meshes.Add(ops[x]); }
        Func<int[], bool> pred = ChainExact.Predicate(plan, k, slot);
        var bE = new ExactMeshBoolean(meshes, pred) { SafeRounding = false, ComputeExactVertices = true };
        var bR = new ExactMeshBoolean(meshes, pred) { ComputeExactVertices = true };
        bool okE = bE.Compute(), okR = bR.Compute();
        row["exact_ok"] = okE; row["rounded_ok"] = okR;
        if (!okE || !okR) { row["error"] = bE.Error ?? bR.Error; Console.WriteLine(JsonSerializer.Serialize(row)); return 0; }
        DMesh3 mE = bE.Result, mR = bR.Result;
        row["exact_tris"] = mE.TriangleCount; row["exact_verts"] = mE.VertexCount;
        row["first_rounding_reason"] = Reason(mE);
        row["rounded_tris"] = mR.TriangleCount;
        row["rounded_reason"] = Reason(mR);
        row["rounded_clean"] = CleanReason(Reason(mR));
        row["rounding_clean_flag"] = bR.RoundingClean;
        row["rounding_removed_components"] = bR.Rounding?.RemovedComponents ?? 0;
        row["rounding_removed_triangles"] = bR.Rounding?.RemovedTriangles ?? 0;
        row["rounding_evaluation_limit"] = bR.Rounding?.EvaluationLimitReached ?? false;
        row["rounding_remaining"] = bR.Rounding != null ? new[] { bR.Rounding.RemainingIntersections, bR.Rounding.RemainingDegenerate, bR.Rounding.RemainingFalseContacts } : new int[3];
        row["rounded_new_vertices"] = mR.VertexIndices().Count(v => v >= bE.ResultExactVertices.Length);
        bool keepWhenEmptied = meshes.Exists(m => MeshRoundingRepair.IsThinOrTiny(m, bR.RoundingMaxDisplacement, 1e-12));
        var e = new Exact(mE, bE.ResultExactVertices);
        // Q1 ----------------------------------------------------------------------------------------------------
        var comps = Analyse(e, e.Components(), 3000);
        foreach (var c in comps) c.Class = Classify(c);
        Func<int, bool> fxR = FixedFor(mR, e);
        List<int> badR = new List<int>(); List<Index2i> pairsR = new List<Index2i>();
        int defR = DefectCount(mR, e, fxR, out badR, out pairsR);
        var defectTris = new HashSet<int>(badR);
        foreach (var p in pairsR) { defectTris.Add(p.a); defectTris.Add(p.b); }
        var owner = new Dictionary<int, CompInfo>();
        foreach (var c in comps) foreach (int v in c.Verts) owner[v] = c;
        foreach (int t in mR.TriangleIndices()) {
            Index3i f = mR.GetTriangle(t);
            if (f.a < e.P.Length && owner.TryGetValue(f.a, out var c) && c.Verts.Contains(f.b) && c.Verts.Contains(f.c)) {
                c.KeptTris++;
                if (defectTris.Contains(t)) c.DefectTris++;
            }
        }
        double exactVol = comps.Sum(c => c.Volume);
        var statR = MeshStats(mR);
        row["exact_components"] = comps.Count;
        row["exact_volume"] = exactVol;
        row["rounded_components"] = statR.count; row["rounded_volume"] = statR.volume;
        row["summary_by_class"] = Summarize(comps);
        row["components_top"] = comps.OrderByDescending(c => c.Tris).Take(25).Select((c, i) => CompRow(c, i)).ToList();
        row["components_thinnest"] = comps.Where(c => c.Tris >= 4).OrderBy(c => c.ThicknessAvg / c.Ulp).Take(25).Select((c, i) => CompRow(c, i)).ToList();
        double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue }, hi = { double.MinValue, double.MinValue, double.MinValue };
        foreach (var c in comps) for (int a = 0; a < 3; ++a) { lo[a] = Math.Min(lo[a], c.Lo[a]); hi[a] = Math.Max(hi[a], c.Hi[a]); }
        row["bbox_lo"] = lo; row["bbox_hi"] = hi;
        double maxAbs = Math.Max(lo.Select(Math.Abs).Max(), hi.Select(Math.Abs).Max());
        row["max_abs_coord"] = maxAbs; row["ulp_at_max"] = Ulp(maxAbs); row["diag"] = Math.Sqrt(Enumerable.Range(0, 3).Sum(a => Math.Pow(hi[a] - lo[a], 2)));
        row["partial"] = "q1"; Console.WriteLine(JsonSerializer.Serialize(row)); row.Remove("partial");
        // unchained: the volume of E that lies in the exact result of a pair operation on the original operands
        try {
            var zres = new List<Dictionary<string, object>>();
            var zops = ops.Count <= 3 ? Enumerable.Range(0, ops.Count).ToList() : new List<int> { 0, ops.Count - 2, ops.Count - 1 };
            zops = zops.Where(x => slot.ContainsKey(x)).ToList();
            foreach (int x in zops) foreach (int y in zops) {
                if (x == y) continue;
                foreach (string zo in new[] { "diff", "inter" }) {
                    if (zo == "inter" && x > y) continue;
                    int sx = slot[x], sy = slot[y];
                    Func<int[], bool> z = zo == "diff" ? (Func<int[], bool>)(w => w[sx] > 0 && w[sy] <= 0) : (w => w[sx] > 0 && w[sy] > 0);
                    var bz = new ExactMeshBoolean(meshes, w => pred(w) && z(w)) { SafeRounding = false, ComputeExactVertices = true };
                    if (!bz.Compute()) continue;
                    double vol = 0;
                    if (bz.Result.TriangleCount > 0) { var ez = new Exact(bz.Result, bz.ResultExactVertices); foreach (var cc in ez.Components()) { VolArea(ez, cc, out BigInteger v6, out double _, out int _); vol += Big(v6, -3 * F) / 6; } }
                    zres.Add(new Dictionary<string, object> { ["pair"] = (zo == "diff" ? $"X{x}-X{y}" : $"X{x}&X{y}"), ["fraction_of_exact_volume"] = exactVol != 0 ? vol / exactVol : 0 });
                }
            }
            row["unchained_pairs"] = zres;
        } catch (Exception ex) { row["unchained_error"] = ex.Message; }
        // today's chained last step, exact (unrounded)
        try {
            if (leftOfLast != null) {
                var bx = new ExactMeshBoolean(leftOfLast, ops[plan[k].Right], plan[k].Op) { SafeRounding = false, ComputeExactVertices = true };
                if (bx.Compute()) {
                    var ex2 = new Exact(bx.Result, bx.ResultExactVertices);
                    var c2 = bx.Result.TriangleCount > 0 ? Analyse(ex2, ex2.Components(), 3000) : new List<CompInfo>();
                    foreach (var c in c2) c.Class = Classify(c);
                    row["chained_exact"] = new Dictionary<string, object> { ["tris"] = bx.Result.TriangleCount, ["components"] = c2.Count, ["volume"] = c2.Sum(c => c.Volume), ["by_class"] = Summarize(c2, false) };
                }
            }
        } catch (Exception ex) { row["chained_exact_error"] = ex.Message; }
        // Q2 ----------------------------------------------------------------------------------------------------
        try {
            var fe = FixedFor(mE, e);
            DefectCount(new DMesh3(mE, false, MeshComponents.None), e, fe, out var bad1, out var pairs1);
            row["first_rounding_defects"] = new[] { bad1.Count, pairs1.Count };
            row["after_rounding_defects"] = new[] { badR.Count, pairsR.Count };
            if (bad1.Count + pairs1.Count > 0 && mE.TriangleCount <= 20000) {
                Q2Result q1 = QuadFlips(mE, e, 150);
                row["q2_first_rounding"] = new Dictionary<string, object> { ["defect_triangles"] = q1.DefectTotal, ["candidates"] = q1.Candidates, ["quad_fixable_in_sample"] = q1.Fixable, ["flips_applied"] = q1.Applied, ["reason_after"] = q1.ReasonAfter, ["sample_limit"] = 150 };
            }
            if (badR.Count + pairsR.Count > 0 || !CleanReason(Reason(mR))) {
                Q2Result q = QuadFlips(mR, e, 60);
                row["q2"] = new Dictionary<string, object> {
                    ["defect_triangles"] = q.DefectTotal, ["degenerate"] = q.Degenerate, ["pairs"] = q.Pairs, ["candidates"] = q.Candidates, ["quad_fixable"] = q.Fixable,
                    ["flips_applied"] = q.Applied, ["reason_before"] = q.ReasonBefore, ["reason_after"] = q.ReasonAfter, ["clean_after"] = q.CleanAfter,
                };
            }
        } catch (Exception ex) { row["q2_error"] = ex.ToString().Substring(0, Math.Min(400, ex.ToString().Length)); }
        row["partial"] = "q2"; Console.WriteLine(JsonSerializer.Serialize(row)); row.Remove("partial");
        // Q3 ----------------------------------------------------------------------------------------------------
        try {
            var origins = new List<OriginRef> { new OriginRef { Name = "world", X = 0, Y = 0, Z = 0, W = 1 } };
            // bounding box centre, snapped to a double (so a double minus a double: exact for the arrangement's doubles near it)
            var cx = new double[3];
            for (int a = 0; a < 3; ++a) cx[a] = 0.5 * (lo[a] + hi[a]);
            origins.Add(DoubleOrigin("bbox_centre", cx));
            var thin = comps.Where(c => c.Tris >= 4).OrderBy(c => c.ThicknessAvg / c.Ulp).FirstOrDefault();
            if (thin != null) {
                int v0 = thin.Verts.Min();
                ExactPoint3 p = e.P[v0];
                origins.Add(new OriginRef { Name = "thin_vertex", X = p.X, Y = p.Y, Z = p.Z, W = p.W });
            }
            // controls: the bounding box centre moved by dyadic offsets (a different origin of the same magnitude: if the outcome varies as much,
            // it is not the local resolution but the different set of rounded coordinates)
            double[][] offs = { new[] { 0.125, -0.0625, 0.03125 }, new[] { -0.25, 0.125, -0.0625 }, new[] { 0.0625, 0.25, -0.125 } };
            for (int i = 0; i < offs.Length; ++i) origins.Add(DoubleOrigin("control" + i, new[] { cx[0] + offs[i][0], cx[1] + offs[i][1], cx[2] + offs[i][2] }));
            var q3 = new List<Dictionary<string, object>>();
            foreach (var o in origins) q3.Add(RunLocal(e, o, exactVol, bR, keepWhenEmptied));
            row["q3"] = q3;
        } catch (Exception ex) { row["q3_error"] = ex.ToString().Substring(0, Math.Min(400, ex.ToString().Length)); }
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }

    static OriginRef DoubleOrigin(string name, double[] c)
    {
        var num = new BigInteger[3];
        for (int a = 0; a < 3; ++a) {
            if (c[a] == 0) continue;
            long bits = BitConverter.DoubleToInt64Bits(c[a]);
            int ex = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (ex != 0) mant |= 1L << 52; else ex = 1;
            int e2 = ex - 1075;                                  // value = mant * 2^e2, e2 >= -1074
            BigInteger v = new BigInteger(mant) << (e2 + 1074);
            num[a] = bits < 0 ? -v : v;
        }
        return NormalizeOrigin(name, num, BigInteger.One << 1074);
    }
    static OriginRef NormalizeOrigin(string name, BigInteger[] num, BigInteger den)
    {
        ExactPoint3 p = ExactPoint3.Create(num[0], num[1], num[2], den);
        return new OriginRef { Name = name, X = p.X, Y = p.Y, Z = p.Z, W = p.W };
    }
}
