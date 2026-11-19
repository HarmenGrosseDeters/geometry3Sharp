// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Feasibility of a size-rule-only approach: which part of the surface has a unique mutual opposite side.
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

static class Pairing
{
    public class Row { public double Multiple, Tol; public double Hit, Anti, Near, Mutual, MutualStrict; }

    // For each triangle f (outward normal n, centroid c, local thickness t): the ray from c along -n hits g (first hit); g has a roughly
    // antiparallel normal (dot < -cos(maxAngle)), the distance d < multiple * t; the reverse ray from the hit point along -n_g hits h and
    // |hitpoint(h) - c| <= tol * d (mutual), or h == f (strict). Area fractions of the whole surface.
    public static List<Row> Analyze(DMesh3 mesh, double[] triThickness, double[] multiples, double[] tols, double maxAngleDeg = 30)
    {
        var tree = new DMeshAABBTree3(mesh, true);
        double diag = mesh.CachedBounds.DiagonalLength, eps = 1e-7 * diag;
        double cosMax = Math.Cos(maxAngleDeg * Math.PI / 180);
        int[] tids = mesh.TriangleIndices().ToArray();
        var area = new double[tids.Length]; var hit = new bool[tids.Length]; var dot = new double[tids.Length];
        var dist = new double[tids.Length]; var tt = new double[tids.Length]; var back = new double[tids.Length]; var strict = new bool[tids.Length];
        for (int i = 0; i < back.Length; ++i) back[i] = double.PositiveInfinity;
        System.Threading.Tasks.Parallel.For(0, tids.Length, i => {
            int f = tids[i];
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            mesh.GetTriVertices(f, ref a, ref b, ref c);
            Vector3d nn = (b - a).Cross(c - a); double len = nn.Length;
            area[i] = 0.5 * len; tt[i] = triThickness[f];
            if (!(len > 0)) return;
            Vector3d n = nn / len, cen = (a + b + c) / 3;
            var ray = new Ray3d(cen - eps * n, -n);
            int g = tree.FindNearestHitTriangle(ray);
            if (g == DMesh3.InvalidID) return;
            double s = Hit(mesh, g, ray);
            if (double.IsNaN(s)) return;
            Vector3d hp = ray.PointAt(s);
            hit[i] = true; dist[i] = s + eps;
            Vector3d ng = mesh.GetTriNormal(g);
            dot[i] = n.Dot(ng);
            var ray2 = new Ray3d(hp - eps * ng, -ng);
            int h = tree.FindNearestHitTriangle(ray2);
            if (h == DMesh3.InvalidID) return;
            double s2 = Hit(mesh, h, ray2);
            if (double.IsNaN(s2)) return;
            back[i] = ray2.PointAt(s2).Distance(cen) / dist[i];
            strict[i] = h == f;
        });
        double total = area.Sum();
        var rows = new List<Row>();
        foreach (double m in multiples)
            foreach (double tol in tols) {
                double ah = 0, aa = 0, an = 0, am = 0, ams = 0;
                for (int i = 0; i < tids.Length; ++i) {
                    if (!hit[i]) continue;
                    ah += area[i];
                    if (!(dot[i] < -cosMax)) continue;
                    aa += area[i];
                    if (!(dist[i] < m * tt[i])) continue;
                    an += area[i];
                    if (back[i] <= tol) am += area[i];
                    if (strict[i]) ams += area[i];
                }
                rows.Add(new Row() { Multiple = m, Tol = tol, Hit = ah / total, Anti = aa / total, Near = an / total, Mutual = am / total, MutualStrict = ams / total });
            }
        return rows;
    }

    static double Hit(DMesh3 mesh, int tid, Ray3d ray)
    {
        Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
        mesh.GetTriVertices(tid, ref a, ref b, ref c);
        var q = new IntrRay3Triangle3(ray, new Triangle3d(a, b, c));
        return q.Find() ? q.RayParameter : double.NaN;
    }
}
