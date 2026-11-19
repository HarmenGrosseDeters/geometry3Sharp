// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;
using gs;
using gs.Lab;

/// <summary>
/// Cause classification of the irregular vertices and non-quad faces of a stage 2 result.
/// Per input triangle the holonomy of the position field is computed from the integer lattice offsets of its three edges
/// (SlotOffsetFixed: translation and quarter turns of the neighbour's frame, composed around the triangle):
///   F  net rotation != 0: a singularity of the cross field (the lattice has a cone there);
///   P  rotation 0, translation != 0: a position-field singularity (a dislocation) that is not a field singularity;
///   M  an edge whose two roundings are further apart than MismatchLimit (the extraction ignores that edge).
/// An output item (irregular vertex or non-quad face centroid) gets the class of the nearest cause within R = 1.5 s:
/// F (also the CrossField's own singular faces), else P, else B (an input boundary vertex within R), else M, else X
/// (no cause near: an extraction artefact such as a split or collapsed edge, or a resolution effect).
/// </summary>
static class Diagnose
{
    static readonly double R = double.Parse(Environment.GetEnvironmentVariable("DIAG_R") ?? "1.5", CultureInfo.InvariantCulture);

    public static void Run(string name, DMesh3 input, CrossField cf, QuadRemesherLab qr, PolyMeshMetrics met, double s)
    {
        var pf = qr.Positions; var L = pf.L0;
        // ---- holonomy per input triangle
        bool dbg = Environment.GetEnvironmentVariable("DIAGDBG") == "1"; var mmHist = new int[10, 3]; int dbgN = 0;
        var fTri = new List<int>(); var pTri = new List<int>(); var mTri = new List<int>();
        Func<int, int, int> slot = (i, j) => { for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) if (L.AJ[k] == j) return k; return -1; };
        foreach (int t in input.TriangleIndices()) {
            Index3i tv = input.GetTriangle(t);
            int[] c = { pf.ToCompact[tv.a], pf.ToCompact[tv.b], pf.ToCompact[tv.c] };
            if (c[0] < 0 || c[1] < 0 || c[2] < 0) continue;
            int rotTot = 0; int tq = 0, tt = 0; bool mm = false;
            // translation holonomy T = off_ab + R^{r_ab}(off_bc + R^{r_bc}(off_ca)): evaluate from the inside out
            var offs = new (int dq, int dt, int r)[3]; bool ok = true;
            for (int e = 0; e < 3; ++e) {
                int i = c[e], j = c[(e + 1) % 3]; int k = slot(i, j);
                if (k < 0) { ok = false; break; }
                int dq, dt, r; double mis;
                pf.SlotOffsetFixed(i, k, out dq, out dt, out mis, out r);
                if (mis > qr.MismatchLimit) mm = true;
                offs[e] = (dq, dt, r);
            }
            if (!ok) continue;
            int uq = offs[2].dq, ut = offs[2].dt;
            for (int e = 1; e >= 0; --e) {
                int r = offs[e].r;
                int rq = uq, rt = ut;
                for (int z = 0; z < r; ++z) { int nq = -rt, nt = rq; rq = nq; rt = nt; }     // quarter turn: (u0, u1) -> (-u1, u0)
                uq = offs[e].dq + rq; ut = offs[e].dt + rt;
            }
            tq = uq; tt = ut; rotTot = (offs[0].r + offs[1].r + offs[2].r) & 3;
            if (dbg) { double mx = 0; for (int e = 0; e < 3; ++e) { int i = c[e], j = c[(e + 1) % 3]; int dq, dt, rr; double mis; pf.SlotOffsetFixed(i, slot(i, j), out dq, out dt, out mis, out rr); mx = Math.Max(mx, mis); } int bin = Math.Min(9, (int)(mx * 10)); mmHist[bin, rotTot != 0 ? 2 : (tq != 0 || tt != 0 ? 1 : 0)]++; }
            if (dbg && dbgN < 6 && rotTot == 0 && (tq != 0 || tt != 0)) { dbgN++; Console.WriteLine($"  tri {t}: offs {offs[0]} {offs[1]} {offs[2]} -> ({tq},{tt})"); for (int e = 0; e < 3; ++e) { int i = c[e], j = c[(e + 1) % 3]; var dv = pf.Origins[j] - pf.Origins[i]; Console.WriteLine($"    edge {e}: |o_j-o_i|/s={dv.Length / s:F3} q.dv/s={L.Q[i].Dot(dv) / s:F2} t.dv/s={L.T[i].Dot(dv) / s:F2}"); } }
            if (rotTot != 0) fTri.Add(t);
            else if (tq != 0 || tt != 0) pTri.Add(t);
            else if (mm) mTri.Add(t);
            if (rotTot != 0 && mm) { /* counted as F */ }
        }
        if (dbg) for (int b = 0; b < 10; ++b) Console.WriteLine($"  max-mismatch bin {b / 10.0:F1}: ok {mmHist[b, 0]} P {mmHist[b, 1]} F {mmHist[b, 2]}");
        foreach (int t in cf.SingularFaces) if (!fTri.Contains(t)) fTri.Add(t);
        int fClusters = Clusters(input, fTri), pClusters = Clusters(input, pTri);
        Func<List<int>, List<Vector3d>> cent = l => l.Select(t => input.GetTriCentroid(t)).ToList();
        var fC = cent(fTri); var pC = cent(pTri); var mC = cent(mTri);
        var bC = new List<Vector3d>();
        foreach (int v in input.VertexIndices()) if (input.IsBoundaryVertex(v)) bC.Add(input.GetVertex(v));
        double lim = R * s;
        Func<Vector3d, List<Vector3d>, double> near = (p, l) => { double b = double.MaxValue; foreach (var q in l) b = Math.Min(b, p.Distance(q)); return b; };
        Func<Vector3d, char> classify = p => {
            if (near(p, fC) < lim) return 'F';
            if (near(p, pC) < lim) return 'P';
            if (near(p, bC) < lim) return 'B';
            if (near(p, mC) < lim) return 'M';
            return 'X';
        };
        int differ = 0, differUsed = 0, rotEdges = 0;
        for (int i = 0; i < L.N; ++i)
            for (int k = L.AS[i]; k < L.AS[i + 1]; ++k) {
                if (L.AJ[k] < i) continue;
                int a1, b1, a2, b2, rr; double m1, m2;
                pf.SlotOffset(i, k, out a1, out b1, out m1); pf.SlotOffsetFixed(i, k, out a2, out b2, out m2, out rr);
                if (rr != 0) rotEdges++;
                if (a1 != a2 || b1 != b2) { differ++; if (m2 <= qr.MismatchLimit && (Math.Abs(a1) + Math.Abs(b1) <= 1 || Math.Abs(a2) + Math.Abs(b2) <= 1)) differUsed++; }
            }
        Console.WriteLine($"  {name}: edges with a rotated frame {rotEdges}; edges where the library's SlotOffset differs from the corrected one {differ} (voting {differUsed})");
        // ---- curl of the field in lattice units: per triangle without a field singularity, the sum over its three edges of the edge
        // vector in the (averaged, aligned) cross frame of the edge, divided by s. Zero where the frames form an integrable lattice
        // (the edge vectors of a closed loop sum to zero in a constant frame); the Burgers vector of a loop is the sum over its triangles.
        double curlSum = 0, curlMax = 0; int curlTris = 0, curlOver = 0, curlOver2 = 0; var fSet = new HashSet<int>(fTri);
        foreach (int t in input.TriangleIndices()) {
            if (fSet.Contains(t)) continue;
            Index3i tv = input.GetTriangle(t);
            int[] c = { pf.ToCompact[tv.a], pf.ToCompact[tv.b], pf.ToCompact[tv.c] };
            if (c[0] < 0 || c[1] < 0 || c[2] < 0) continue;
            Vector3d qa = L.Q[c[0]], na = L.Nrm[c[0]], ta = na.Cross(qa);
            double wx = 0, wy = 0;
            for (int e = 0; e < 3; ++e) {
                int i = c[e], j = c[(e + 1) % 3];
                Vector3d q0, q1; PositionFieldLab.CompatOrient(qa, na, L.Q[i], L.Nrm[i], out q0, out q1);
                Vector3d u0, u1; PositionFieldLab.CompatOrient(qa, na, L.Q[j], L.Nrm[j], out u0, out u1);
                Vector3d qe = q1 + u1; double ql = qe.Length; if (ql < 1e-9) continue; qe /= ql;
                Vector3d ev = (L.V[j] - L.V[i]) / s;
                wx += ev.Dot(qe); wy += ev.Dot(na.Cross(qe));
            }
            double w = Math.Sqrt(wx * wx + wy * wy);
            curlSum += w; curlTris++; curlMax = Math.Max(curlMax, w); if (w > 0.05) curlOver++; if (w > 0.1) curlOver2++;
        }
        Console.WriteLine($"  {name}: curl of the field in lattice units: sum |w| over {curlTris} triangles {curlSum:F1}, max {curlMax:F3}, triangles above 0.05: {curlOver}, above 0.1: {curlOver2}; P triangles {pTri.Count} ({pC.Count(pc => near(pc, bC) < lim)} within R of the boundary), P clusters {pClusters}");
        // ---- output items
        DPolyMesh res = qr.Result; int nv = res.VertexCount;
        var edges = new Dictionary<long, int>(); var val = new int[nv]; var onB = new bool[nv]; var used = new bool[nv];
        for (int f = 0; f < res.FaceCount; ++f) {
            int[] fv = res.GetFace(f);
            for (int k = 0; k < fv.Length; ++k) { int a = fv[k], b = fv[(k + 1) % fv.Length]; used[a] = true; long key = a < b ? (long)a * nv + b : (long)b * nv + a; edges.TryGetValue(key, out int c); edges[key] = c + 1; }
        }
        foreach (var kv in edges) { int a = (int)(kv.Key / nv), b = (int)(kv.Key % nv); val[a]++; val[b]++; if (kv.Value == 1) { onB[a] = true; onB[b] = true; } }
        var irrCls = new Dictionary<char, int>(); var irrVal = new Dictionary<char, SortedDictionary<int, int>>();
        for (int v = 0; v < nv; ++v) {
            if (!used[v]) continue;
            bool irr = onB[v] ? val[v] != 3 : val[v] != 4;
            if (!irr) continue;
            char cl = classify(res.GetVertex(v));
            irrCls.TryGetValue(cl, out int n0); irrCls[cl] = n0 + 1;
            if (!irrVal.ContainsKey(cl)) irrVal[cl] = new SortedDictionary<int, int>();
            irrVal[cl].TryGetValue(val[v], out int n1); irrVal[cl][val[v]] = n1 + 1;
        }
        var faceCls = new Dictionary<char, int>(); int nonQuad = 0;
        for (int f = 0; f < res.FaceCount; ++f) {
            int[] fv = res.GetFace(f);
            if (fv.Length == 4) continue;
            nonQuad++;
            Vector3d c = Vector3d.Zero; foreach (int v in fv) c += res.GetVertex(v); c /= fv.Length;
            char cl = classify(c);
            faceCls.TryGetValue(cl, out int n0); faceCls[cl] = n0 + 1;
        }
        Func<Dictionary<char, int>, char, int> g = (d, c) => d.TryGetValue(c, out int x) ? x : 0;
        int sing = cf.SingularFaces.Count;
        int irrLeft = g(irrCls, 'F') + g(irrCls, 'B');                 // a perfect fix of P, M and X leaves these
        int irrLeftP = irrLeft + g(irrCls, 'M') + g(irrCls, 'X');      // a perfect fix of P only
        int gate = 2 * sing + 4;
        int nfLeft = g(faceCls, 'F') + g(faceCls, 'B'), nfLeftP = nfLeft + g(faceCls, 'M') + g(faceCls, 'X');
        double qUb = 100.0 * (1 - (double)nfLeft / met.Faces), qUbP = 100.0 * (1 - (double)nfLeftP / met.Faces);
        string hist = string.Join(" ", irrVal.OrderBy(kv => kv.Key).Select(kv => kv.Key + "[" + string.Join(",", kv.Value.Select(x => x.Key + ":" + x.Value)) + "]"));
        Console.WriteLine($"| {name} | {met.IrregularVertices} | {sing} | {g(irrCls, 'F')} / {g(irrCls, 'P')} / {g(irrCls, 'B')} / {g(irrCls, 'M')} / {g(irrCls, 'X')} | {nonQuad} ({F1(100.0 * met.QuadFraction)} %) | {g(faceCls, 'F')} / {g(faceCls, 'P')} / {g(faceCls, 'B')} / {g(faceCls, 'M')} / {g(faceCls, 'X')} | {fTri.Count} ({fClusters}) / {pTri.Count} ({pClusters}) / {mTri.Count} | {irrLeft} (gate {gate}: {(irrLeft <= gate ? "reached" : "NOT reached")}) | {irrLeftP} | {F1(qUb)} % | {F1(qUbP)} % | {hist} |");
    }

    static string F1(double x) { return x.ToString("F1", CultureInfo.InvariantCulture); }

    // connected components of a set of triangles (shared vertex)
    static int Clusters(DMesh3 m, List<int> tris)
    {
        var parent = new Dictionary<int, int>();
        Func<int, int> find = null; find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
        foreach (int t in tris) {
            Index3i tv = m.GetTriangle(t);
            foreach (int v in new[] { tv.a, tv.b, tv.c }) {
                if (!parent.ContainsKey(v)) parent[v] = v;
            }
            int ra = find(tv.a), rb = find(tv.b), rc = find(tv.c);
            parent[rb] = ra; parent[find(rc)] = find(ra);
        }
        return parent.Keys.Select(find).Distinct().Count();
    }
}
