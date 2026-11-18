// Throwaway spike diagnostics (stage 3B, inverted-triangle analysis). SPIKE_DBG2=1.
using System;
using System.Collections.Generic;
using System.Linq;

partial class QuadFlowSpike
{
    int[][] origNb; int[] origFlag; int fixedAdj;     // bit 1 touches a fixed (unbalanced) edge, 2 a flow-changed edge, 4 a frame jump (M != 0)

    void DiagRecord(int F, bool[] balanced, V2[] preFlow, V2[] initDv)
    {
        origNb = new int[F][]; origFlag = new int[F];
        for (int f = 0; f < F; ++f) origNb[f] = (int[])tris[f].Nb.Clone();
        for (int k = 0; k < edges.Count; ++k) {
            var e = edges[k]; int b = 0;
            if (!balanced[k]) b |= 1;
            if (e.Dv != preFlow[k]) b |= 2;
            if (e.F1 >= 0 && e.M != 0) b |= 4;
            if (!balanced[k] && preFlow[k] != initDv[k]) { b |= 8; fixedAdj++; }
            origFlag[e.F0] |= b; if (e.F1 >= 0) origFlag[e.F1] |= b;
        }
    }

    int[] OrigDist(int bit)
    {
        int F = origNb.Length; var d = new int[F]; var q = new Queue<int>();
        for (int f = 0; f < F; ++f) { d[f] = -1; if ((origFlag[f] & bit) != 0) { d[f] = 0; q.Enqueue(f); } }
        while (q.Count > 0) { int f = q.Dequeue(); foreach (int n in origNb[f]) if (n >= 0 && d[n] < 0) { d[n] = d[f] + 1; q.Enqueue(n); } }
        return d;
    }

    static string Hist(IEnumerable<int> xs, int cap = 6)
    {
        var h = new int[cap + 1]; foreach (int x in xs) h[Math.Min(cap, Math.Max(0, x))]++;
        return string.Join(" ", h.Select((c, i) => (i == cap ? ">=" : "") + i + ":" + c));
    }

    // winding number of the closed polygon around p
    static int Winding(List<(double x, double y)> poly, double px, double py)
    {
        int w = 0, n = poly.Count;
        for (int i = 0; i < n; ++i) {
            var a = poly[i]; var b = poly[(i + 1) % n];
            if (a.y <= py) { if (b.y > py && (b.x - a.x) * (py - a.y) - (px - a.x) * (b.y - a.y) > 0) w++; }
            else if (b.y <= py && (b.x - a.x) * (py - a.y) - (px - a.x) * (b.y - a.y) < 0) w--;
        }
        return w;
    }

    public class RegionInfo { public bool Disk, ConeInside, Inconsistent; public int MinW, MaxW, NegTris, Tris, InvTris, BoundaryLen, WAtSeed; public Dictionary<int, (int x, int y)> Pos; public List<int> Loop; public HashSet<int> Set; }

    // analyse a triangle region: disk test, cone test, integer layout, boundary winding at the nondegenerate triangles
    public RegionInfo AnalyseRegion(HashSet<int> reg, int seedTri, List<int> ft, List<int> fi, List<int> cum)
    {
        var R = new RegionInfo { Tris = reg.Count, Set = reg };
        // boundary half-edges
        var next = new Dictionary<int, int>(); int nb = 0; bool dupStart = false;
        var verts = new HashSet<int>(); int ePairs = 0;
        foreach (int f in reg)
            for (int sl = 0; sl < 3; ++sl) {
                verts.Add(tris[f].V[sl]);
                int n = tris[f].Nb[sl];
                if (n >= 0 && reg.Contains(n)) { ePairs++; continue; }
                int a = tris[f].V[sl], b = tris[f].V[(sl + 1) % 3];
                if (next.ContainsKey(a)) dupStart = true; next[a] = b; nb++;
            }
        int E = ePairs / 2 + nb, V = verts.Count, Fc = reg.Count;
        // single loop?
        var loop = new List<int>();
        if (nb > 0 && !dupStart) { int s0 = next.Keys.First(), c = s0; do { loop.Add(c); if (!next.TryGetValue(c, out c)) { loop = null; break; } } while (c != s0 && loop.Count <= nb); }
        R.Disk = !dupStart && loop != null && loop.Count == nb && V - E + Fc == 1;
        R.BoundaryLen = nb; R.Loop = loop;
        if (!R.Disk) return R;
        var bset = new HashSet<int>(loop);
        foreach (int v in verts) if (!bset.Contains(v) && !Fan(v, ft, fi, cum)) { R.ConeInside = true; return R; }
        // layout by BFS over triangles
        var rho = new Dictionary<int, int>(); var pos = new Dictionary<int, (int, int)>();
        int t0 = reg.First(); rho[t0] = 0; var q = new Queue<int>(); q.Enqueue(t0);
        { var t = tris[t0]; pos[t.V[0]] = (0, 0); pos[t.V[1]] = (t.D[0].X, t.D[0].Y); var p2 = t.D[0] + t.D[1]; pos[t.V[2]] = (p2.X, p2.Y); }
        while (q.Count > 0) {
            int f = q.Dequeue(); var t = tris[f];
            for (int sl = 0; sl < 3; ++sl) {
                V2 dg = V2.Rot(t.D[sl], rho[f]);
                var pa = pos.TryGetValue(t.V[sl], out var xx) ? xx : ((int, int)?)null;
                if (pa.HasValue) {
                    var want = (pa.Value.Item1 + dg.X, pa.Value.Item2 + dg.Y);
                    if (pos.TryGetValue(t.V[(sl + 1) % 3], out var have)) { if (have != want) R.Inconsistent = true; }
                    else pos[t.V[(sl + 1) % 3]] = want;
                }
                int n = t.Nb[sl]; if (n < 0 || !reg.Contains(n) || rho.ContainsKey(n)) continue;
                rho[n] = ((rho[f] - t.Rot[sl]) % 4 + 4) % 4; q.Enqueue(n);
            }
        }
        R.Pos = pos;
        // orientation of each triangle in the global frame equals its own det (rotations keep det)
        var poly = loop.Select(v => ((double)pos[v].Item1, (double)pos[v].Item2)).ToList();
        R.MinW = int.MaxValue; R.MaxW = int.MinValue;
        foreach (int f in reg) {
            var t = tris[f]; int det = Det(t); if (det == 0) continue;
            double cx = (pos[t.V[0]].Item1 + pos[t.V[1]].Item1 + pos[t.V[2]].Item1) / 3.0 + 1.234567e-4, cy = (pos[t.V[0]].Item2 + pos[t.V[1]].Item2 + pos[t.V[2]].Item2) / 3.0 + 2.345671e-4;
            int w = Winding(poly, cx, cy);
            R.MinW = Math.Min(R.MinW, w); R.MaxW = Math.Max(R.MaxW, w);
            if (det < 0) { R.InvTris++; if (w < 0) R.NegTris++; }
            if (f == seedTri) R.WAtSeed = w;
        }
        return R;
    }

    HashSet<int> Grow(IEnumerable<int> seed, int rings)
    {
        var reg = new HashSet<int>(seed); var front = new List<int>(reg);
        for (int r = 0; r < rings; ++r) {
            var nf = new List<int>();
            foreach (int f in front) for (int sl = 0; sl < 3; ++sl) { int n = tris[f].Nb[sl]; if (n >= 0 && reg.Add(n)) nf.Add(n); }
            front = nf;
        }
        return FillHoles(reg);
    }

    // add every complement component except the largest (so the region has one boundary loop where possible)
    HashSet<int> FillHoles(HashSet<int> reg)
    {
        var seen = new HashSet<int>(reg); var comps = new List<List<int>>();
        for (int f = 0; f < tris.Count; ++f) if (seen.Add(f)) {
            var c = new List<int>(); var st = new Stack<int>(); st.Push(f);
            while (st.Count > 0) { int g = st.Pop(); c.Add(g); for (int sl = 0; sl < 3; ++sl) { int n = tris[g].Nb[sl]; if (n >= 0 && seen.Add(n)) st.Push(n); } }
            comps.Add(c);
        }
        if (comps.Count <= 1) return reg;
        int big = comps.Select((c, i) => (c.Count, i)).Max().Item2;
        for (int i = 0; i < comps.Count; ++i) if (i != big) foreach (int f in comps[i]) reg.Add(f);
        return reg;
    }

    public List<List<int>> InvComponents()
    {
        // vertex-connected components of inverted triangles
        var byV = new Dictionary<int, List<int>>();
        for (int f = 0; f < tris.Count; ++f) if (Det(tris[f]) < 0) foreach (int v in tris[f].V) { if (!byV.TryGetValue(v, out var l)) byV[v] = l = new List<int>(); l.Add(f); }
        var seen = new HashSet<int>(); var comps = new List<List<int>>();
        for (int f = 0; f < tris.Count; ++f) if (Det(tris[f]) < 0 && seen.Add(f)) {
            var c = new List<int>(); var st = new Stack<int>(); st.Push(f);
            while (st.Count > 0) { int g = st.Pop(); c.Add(g); foreach (int v in tris[g].V) foreach (int h in byV[v]) if (seen.Add(h)) st.Push(h); }
            comps.Add(c);
        }
        return comps;
    }

    void Diag2()
    {
        BuildCorners();
        var ft = new List<int>(); var fi = new List<int>(); var cum = new List<int>();
        // (A) relation of the inverted triangles to the fixed edges, the flow-changed edges and the frame jumps (original mesh rings)
        var inv = Enumerable.Range(0, tris.Count).Where(f => Det(tris[f]) < 0).ToList();
        int Fo = origNb.Length;
        foreach (var (bit, name) in new[] { (1, "fixed edge"), (2, "flow-changed edge"), (4, "frame jump"), (8, "fixed edge changed by the B=0 greedy") }) {
            var d = OrigDist(bit);
            Console.WriteLine($"  [dbg2] ring distance to nearest {name}: inverted {Hist(inv.Select(f => d[tris[f].Orig]))} | all original triangles {Hist(Enumerable.Range(0, Fo).Select(f => d[f]))}");
        }
        Console.WriteLine($"  [dbg2] fixed edges changed by the B=0 greedy: {fixedAdj}");
        // det histogram of all triangles
        var dh = new SortedDictionary<int, int>(); foreach (var t in tris) { int d = Det(t); dh.TryGetValue(d, out int c); dh[d] = c + 1; }
        Console.WriteLine("  [dbg2] det histogram: " + string.Join(" ", dh.Select(kv => kv.Key + ":" + kv.Value)));
        // (B) boundary winding of growing disks around each inverted component
        var comps = InvComponents();
        Console.WriteLine($"  [dbg2] vertex-connected inverted components: {comps.Count}");
        int ci = 0;
        foreach (var c in comps) {
            var line = new List<string>(); int firstClean = -1, stop = -1; string why = "";
            for (int r = 0; r <= 40; ++r) {
                var reg = Grow(c, r);
                var ri = AnalyseRegion(reg, c[0], ft, fi, cum);
                if (!ri.Disk) { if (r == 0) { line.Add("r0 notdisk"); continue; } stop = r; why = "not a disk"; break; }
                if (ri.ConeInside) { stop = r; why = "cone inside"; break; }
                if (ri.Inconsistent) { stop = r; why = "inconsistent"; break; }
                if (r <= 3 || r % 5 == 0) line.Add($"r{r}:W[{ri.MinW},{ri.MaxW}] neg{ri.NegTris}/{ri.InvTris} n{ri.Tris}");
                if (firstClean < 0 && ri.MinW >= 0 && ri.MaxW <= 1) firstClean = r;
            }
            Console.WriteLine($"  [dbg2] comp {ci++} size {c.Count} det {string.Join(",", c.Select(f => Det(tris[f])))}: first r with boundary winding in [0,1]: {firstClean}; stop at r{stop} ({why}); " + string.Join(" ", line));
        }
    }
}
