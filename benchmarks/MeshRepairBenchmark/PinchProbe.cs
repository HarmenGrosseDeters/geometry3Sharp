// Measurement probe (branch runs/pinch-pull-3, not for merge): exposure and angle metrics of a wrap output, for the
// pinch-cap question (docs/design/ALPHA_WRAP_MANIFOLD_QUALITY.md). Adds "<prefix>..." keys to the row of MeshRepairBenchmark.
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

static class PinchProbe
{
    static double Angle(Vector3d u, Vector3d v) => Math.Atan2(u.Cross(v).Length, u.Dot(v)) * 180 / Math.PI;

    // exposure: max circumradius / alpha over the triangles; max centroid distance to the input / (alpha + offset);
    // angles: min angle, p01 (1 % quantile of the per-triangle min angle), counts below 0.5 and above 170 degrees;
    // closed, bowties, exact self-intersection pairs, max vertex distance to the input / offset
    public static void Exposure(DMesh3 mesh, DMesh3 input, double alpha, double off, Dictionary<string, object> r, string prefix)
    {
        if (mesh == null || mesh.TriangleCount == 0) return;
        var tree = input.TriangleCount > 0 ? new DMeshAABBTree3(input, true) : null;
        double maxCirc = 0, maxCent = 0, minAng = double.MaxValue;
        int lt05 = 0, gt170 = 0;
        var minAngles = new List<double>();
        foreach (int tid in mesh.TriangleIndices()) {
            Vector3d a = default, b = default, c = default;
            mesh.GetTriVertices(tid, ref a, ref b, ref c);
            double ab = a.Distance(b), bc = b.Distance(c), ca = c.Distance(a);
            double area2 = (b - a).Cross(c - a).Length;   // twice the area
            if (area2 > 0) maxCirc = Math.Max(maxCirc, ab * bc * ca / (2 * area2));
            double a0 = Angle(b - a, c - a), a1 = Angle(a - b, c - b), a2 = Angle(a - c, b - c);
            double m = Math.Min(a0, Math.Min(a1, a2));
            minAngles.Add(m);
            minAng = Math.Min(minAng, m);
            if (m < 0.5) lt05++;
            if (Math.Max(a0, Math.Max(a1, a2)) > 170) gt170++;
            if (tree != null) {
                Vector3d cen = (a + b + c) / 3;
                int t = tree.FindNearestTriangle(cen);
                if (t >= 0) maxCent = Math.Max(maxCent, Math.Sqrt(MeshQueries.TriDistanceSqr(input, t, cen)));
            }
        }
        minAngles.Sort();
        r[prefix + "tris"] = mesh.TriangleCount;
        r[prefix + "circ_over_alpha_max"] = Math.Round(maxCirc / alpha, 4);
        r[prefix + "cent_over_alphaoff_max"] = Math.Round(maxCent / (alpha + off), 4);
        r[prefix + "min_angle"] = Math.Round(minAng, 5);
        r[prefix + "p01_min_angle"] = Math.Round(minAngles[(int)Math.Floor(0.01 * (minAngles.Count - 1))], 5);
        r[prefix + "lt05"] = lt05;
        r[prefix + "gt170"] = gt170;
        r[prefix + "closed"] = mesh.IsClosed();
        r[prefix + "bowties"] = MeshIterators.BowtieVertices(mesh).Count();
        r[prefix + "self_pairs"] = MeshRepairMetrics.SelfIntersections(mesh);
        if (tree != null) {
            double dmax = 0;
            foreach (Vector3d v in mesh.Vertices()) {
                int t = tree.FindNearestTriangle(v);
                if (t >= 0) dmax = Math.Max(dmax, Math.Sqrt(MeshQueries.TriDistanceSqr(input, t, v)));
            }
            r[prefix + "vdist_max_off"] = Math.Round(dmax / off, 4);
        }
    }
}
