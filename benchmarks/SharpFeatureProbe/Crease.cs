// Normal deviation at creases: the input's sharp edges (dihedral angle above 30 degrees, no slivers) are sampled; on each of the
// two faces at a distance s from the edge a point p is taken, and the angle between the face normal and the normal of the
// result's triangle closest to p is measured. A result that keeps the crease sharp has its two faces' normals there, a
// rounded crease (the wrap's alpha ball) has normals turned toward each other. s = 2 offsets (below the wrap's resolution) and
// s = 1 alpha (the radius a rounded crease affects).
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

static class Crease
{
    public static void Add(DMesh3 orientedInput, DMesh3 result, double alpha, double delta, Dictionary<string, object> r, int maxEdges = 3000)
    {
        var tree = new DMeshAABBTree3(result, true);
        var edges = new List<(Vector3d m, Vector3d dir, Vector3d n1, Vector3d n2, Vector3d u1, Vector3d u2)>();
        double size = orientedInput.CachedBounds.DiagonalLength;
        foreach (int eid in orientedInput.EdgeIndices()) {
            Index2i et = orientedInput.GetEdgeT(eid);
            if (et.b == DMesh3.InvalidID) continue;
            Vector3d n1 = orientedInput.GetTriNormal(et.a), n2 = orientedInput.GetTriNormal(et.b);
            if (n1.Dot(n2) >= Math.Cos(30 * Math.PI / 180)) continue;
            Index2i ev = orientedInput.GetEdgeV(eid);
            Vector3d a = orientedInput.GetVertex(ev.a), b = orientedInput.GetVertex(ev.b);
            Vector3d e = b - a;
            double len = e.Length;
            if (!(len > 1e-3 * delta)) continue;
            e /= len;
            Vector3d u1 = InFace(orientedInput, et.a, a, e), u2 = InFace(orientedInput, et.b, a, e);
            if (u1 == Vector3d.Zero || u2 == Vector3d.Zero) continue;
            // (slivers: a face thinner than the sample distance gives no point on it)
            if (Height(orientedInput, et.a) < 1e-5 * size || Height(orientedInput, et.b) < 1e-5 * size) continue;
            edges.Add((0.5 * (a + b), e, n1, n2, u1, u2));
        }
        var rnd = new Random(1);
        if (edges.Count > maxEdges)
            edges = edges.OrderBy(_ => rnd.Next()).Take(maxEdges).ToList();
        r["crease_edges"] = edges.Count;
        foreach (var (tag, s) in new[] { ("d2", 2 * delta), ("a1", alpha) }) {
            var dev = new List<double>();
            foreach (var ed in edges) {
                for (int k = 0; k < 2; ++k) {
                    Vector3d u = k == 0 ? ed.u1 : ed.u2, n = k == 0 ? ed.n1 : ed.n2;
                    Vector3d p = ed.m + s * u;
                    int tid = tree.FindNearestTriangle(p);
                    if (tid == DMesh3.InvalidID) continue;
                    Vector3d nr = result.GetTriNormal(tid);
                    dev.Add(Math.Acos(MathUtil.Clamp(nr.Dot(n), -1, 1)) * MathUtil.Rad2Deg);
                }
            }
            dev.Sort();
            r["crease_nd_" + tag + "_mean"] = dev.Count > 0 ? dev.Average() : double.NaN;
            r["crease_nd_" + tag + "_p50"] = dev.Count > 0 ? dev[dev.Count / 2] : double.NaN;
            r["crease_nd_" + tag + "_p90"] = dev.Count > 0 ? dev[(int)(0.9 * (dev.Count - 1))] : double.NaN;
        }
    }

    // unit vector in the triangle, perpendicular to the edge direction e, pointing into the triangle
    static Vector3d InFace(DMesh3 m, int tid, Vector3d onEdge, Vector3d e)
    {
        Index3i t = m.GetTriangle(tid);
        Vector3d best = Vector3d.Zero; double bestLen = 0;
        foreach (int vi in new[] { t.a, t.b, t.c }) {
            Vector3d w = m.GetVertex(vi) - onEdge;
            Vector3d perp = w - w.Dot(e) * e;
            if (perp.Length > bestLen) { bestLen = perp.Length; best = perp; }
        }
        return bestLen > 0 ? best / bestLen : Vector3d.Zero;
    }

    static double Height(DMesh3 m, int tid)
    {
        Index3i t = m.GetTriangle(tid);
        Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
        double area2 = (b - a).Cross(c - a).Length;
        double maxEdge = Math.Max((b - a).Length, Math.Max((c - b).Length, (a - c).Length));
        return maxEdge > 0 ? area2 / maxEdge : 0;
    }
}
