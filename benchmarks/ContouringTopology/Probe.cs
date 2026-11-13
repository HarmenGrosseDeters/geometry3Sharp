// Cost probe for a per-cell topology certificate (docs/design/CONTOURING_TOPOLOGY.md, section 3): on a uniform grid
// with cell = diagonal / res, how many cells that the input surface passes through would fail a local criterion?
// Floating point, public API only: a probe, not a certificate.
//   sheets:   connected parts of the input surface inside the cell (triangles overlapping the cell, joined across
//             shared edges and vertices that reach into the cell); more than one = a "complex cell" (thin wall, gap,
//             two parts of the surface)
//   monotone: a direction d with d . n > 0 for the normals of all triangles of the cell exists (the surface in the
//             cell is a graph over the plane normal to d: the criterion of Snyder, for any direction)
//   pv:       all pairs of normals have a positive inner product (Plantinga and Vegter's small normal variation)
//   star4:    monotone over the cell and its 6 face neighbours together (recorded as probe_not_star; not in the
//             tables of analyze.py)
// Zero-area triangles are skipped, which can split a sheet that is connected only through one.
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

static class Probe
{
    public static void Run(DMesh3 input, int res, Dictionary<string, object> r)
    {
        DMesh3 mesh = new DMesh3(input, true);
        new MergeCoincidentEdges(mesh).Apply();
        AxisAlignedBox3d b = mesh.CachedBounds;
        double cell = b.DiagonalLength / res;
        Vector3d o = b.Min - 2.371 * cell * Vector3d.One;
        var cells = new Dictionary<Vector3i, List<int>>();
        Vector3d half = 0.5 * cell * Vector3d.One;
        foreach (int tid in mesh.TriangleIndices()) {
            Vector3d a = Vector3d.Zero, bb = Vector3d.Zero, c = Vector3d.Zero;
            mesh.GetTriVertices(tid, ref a, ref bb, ref c);
            if (mesh.GetTriArea(tid) == 0) continue;
            AxisAlignedBox3d tb = new AxisAlignedBox3d(a, a); tb.Contain(bb); tb.Contain(c);
            Vector3i lo = Index(tb.Min, o, cell), hi = Index(tb.Max, o, cell);
            for (int z = lo.z; z <= hi.z; ++z) for (int y = lo.y; y <= hi.y; ++y) for (int x = lo.x; x <= hi.x; ++x) {
                Vector3d center = o + cell * new Vector3d(x + 0.5, y + 0.5, z + 0.5);
                if (!WindingNumberOctree.TriangleBoxOverlap(a - center, bb - center, c - center, half)) continue;
                var key = new Vector3i(x, y, z);
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                list.Add(tid);
            }
        }
        int n = 0, multi = 0, notMono = 0, notPv = 0, notStar = 0, fail = 0, failPv = 0;
        var normals = new List<Vector3d>();
        foreach (var kv in cells) {
            n++;
            Vector3d center = o + cell * new Vector3d(kv.Key.x + 0.5, kv.Key.y + 0.5, kv.Key.z + 0.5);
            int sheets = Sheets(mesh, kv.Value, center, half);
            normals.Clear();
            foreach (int t in kv.Value) normals.Add(mesh.GetTriNormal(t));
            bool mono = Monotone(normals), pv = SmallVariation(normals);
            bool star = mono;
            if (star) {
                for (int d = 0; d < 6; ++d) {
                    Vector3i nb = kv.Key; nb[d / 2] += (d % 2 == 0) ? 1 : -1;
                    if (cells.TryGetValue(nb, out var other)) foreach (int t in other) normals.Add(mesh.GetTriNormal(t));
                }
                star = Monotone(normals);
            }
            if (sheets > 1) multi++;
            if (!mono) notMono++;
            if (!pv) notPv++;
            if (!star) notStar++;
            if (sheets > 1 || !mono) fail++;
            if (sheets > 1 || !pv) failPv++;
        }
        r["probe_cells"] = n; r["probe_multi_sheet"] = multi; r["probe_not_monotone"] = notMono; r["probe_not_pv"] = notPv;
        r["probe_not_star"] = notStar; r["probe_fail"] = fail; r["probe_fail_pv"] = failPv;
    }

    static Vector3i Index(Vector3d p, Vector3d o, double cell) =>
        new Vector3i((int)Math.Floor((p.x - o.x) / cell), (int)Math.Floor((p.y - o.y) / cell), (int)Math.Floor((p.z - o.z) / cell));

    static bool InBox(Vector3d p, Vector3d c, Vector3d h) => Math.Abs(p.x - c.x) <= h.x && Math.Abs(p.y - c.y) <= h.y && Math.Abs(p.z - c.z) <= h.z;

    static bool SegmentBox(Vector3d a, Vector3d b, Vector3d c, Vector3d h)
    {
        double t0 = 0, t1 = 1;
        for (int i = 0; i < 3; ++i) {
            double p = a[i] - c[i], d = b[i] - a[i];
            if (d == 0) { if (Math.Abs(p) > h[i]) return false; continue; }
            double u = (-h[i] - p) / d, v = (h[i] - p) / d;
            if (u > v) { double s = u; u = v; v = s; }
            t0 = Math.Max(t0, u); t1 = Math.Min(t1, v);
            if (t0 > t1) return false;
        }
        return true;
    }

    static int Sheets(DMesh3 mesh, List<int> tris, Vector3d c, Vector3d h)
    {
        if (tris.Count == 1) return 1;
        var index = new Dictionary<int, int>();
        for (int i = 0; i < tris.Count; ++i) index[tris[i]] = i;
        int[] parent = Enumerable.Range(0, tris.Count).ToArray();
        Func<int, int> find = null;
        find = (x) => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
        var byVertex = new Dictionary<int, int>();
        for (int i = 0; i < tris.Count; ++i) {
            Index3i tv = mesh.GetTriangle(tris[i]);
            Index3i te = mesh.GetTriEdges(tris[i]);
            for (int j = 0; j < 3; ++j) {
                if (InBox(mesh.GetVertex(tv[j]), c, h)) {
                    if (byVertex.TryGetValue(tv[j], out int first)) parent[find(i)] = find(first); else byVertex[tv[j]] = i;
                }
                Index2i ev = mesh.GetEdgeV(te[j]);
                if (!SegmentBox(mesh.GetVertex(ev.a), mesh.GetVertex(ev.b), c, h)) continue;
                Index2i et = mesh.GetEdgeT(te[j]);
                int other = et.a == tris[i] ? et.b : et.a;
                if (other != DMesh3.InvalidID && index.TryGetValue(other, out int k)) parent[find(i)] = find(k);
            }
        }
        int count = 0;
        for (int i = 0; i < tris.Count; ++i) if (find(i) == i) count++;
        return count;
    }

    // A direction with a positive inner product with every normal, by perceptron iterations. "true" is checked
    // (all inner products above 1e-6), so it is never wrong; "false" can be wrong: the fixed step does not converge
    // for margins below about 0.01 (a wedge of less than about one degree with unequal triangle counts per face),
    // so not_monotone, fail and not_star over-count on very thin wedges.
    static bool Monotone(List<Vector3d> normals)
    {
        Vector3d d = Vector3d.Zero;
        foreach (var nrm in normals) d += nrm;
        if (d.Length < 1e-12) d = normals[0];
        d.Normalize();
        for (int it = 0; it < 500; ++it) {
            double worst = double.MaxValue; int wi = -1;
            for (int i = 0; i < normals.Count; ++i) { double s = d.Dot(normals[i]); if (s < worst) { worst = s; wi = i; } }
            if (worst > 1e-6) return true;
            d += (0.02 - worst) * normals[wi];
            if (d.Length < 1e-12) return false;
            d.Normalize();
        }
        return false;
    }

    static bool SmallVariation(List<Vector3d> normals)
    {
        // distinct normals only: CAD faces have many coplanar triangles
        var distinct = new List<Vector3d>();
        foreach (var nrm in normals) {
            bool seen = false;
            foreach (var q in distinct) if (q.Dot(nrm) > 1 - 1e-12) { seen = true; break; }
            if (!seen) distinct.Add(nrm);
            if (distinct.Count > 2000) return false;
        }
        for (int i = 0; i < distinct.Count; ++i) for (int j = i + 1; j < distinct.Count; ++j) if (distinct[i].Dot(distinct[j]) <= 0) return false;
        return true;
    }
}
