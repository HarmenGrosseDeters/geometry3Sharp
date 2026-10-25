// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Exact cut and fill volumes between two terrain surfaces (TINs / height fields), eg the surveys
    /// before and after excavation or a design surface against the current terrain.
    ///
    /// The triangles of both surfaces are overlaid in XY: each overlap of a triangle of Before with a
    /// triangle of After is a convex polygon on which both surfaces are linear, so the height difference
    /// is linear too. The polygon is split at the zero line of the difference and both parts are
    /// integrated exactly (area times the difference at the centroid). Only the common footprint counts.
    ///
    /// Inputs must be height fields (each XY point covered by at most one triangle per surface);
    /// triangle soups and mixed orientation are fine. Computed relative to a local origin, so UTM
    /// coordinates do not lose precision.
    /// </summary>
    public class TerrainVolumeDifference
    {
        public DMesh3 Before, After;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> volume where After is below Before (material removed, Abtrag) </summary>
        public double CutVolume;
        /// <summary> volume where After is above Before (material added, Auftrag) </summary>
        public double FillVolume;
        /// <summary> FillVolume - CutVolume </summary>
        public double NetVolume { get { return FillVolume - CutVolume; } }
        /// <summary> area of the common footprint, and the parts of it with cut and fill </summary>
        public double OverlapArea, CutArea, FillArea;


        public TerrainVolumeDifference(DMesh3 before, DMesh3 after)
        {
            Before = before; After = after;
        }


        public bool Compute()
        {
            CutVolume = FillVolume = OverlapArea = CutArea = FillArea = 0;
            if (Before.TriangleCount == 0 || After.TriangleCount == 0)
                return false;
            AxisAlignedBox3d bounds = Before.CachedBounds;
            bounds.Contain(After.CachedBounds);
            Vector3d origin = bounds.Center;

            DMeshAABBTree3 spatial = new DMeshAABBTree3(After, true);
            int[] tris = new List<int>(Before.TriangleIndices()).ToArray();
            double[,] partial = new double[tris.Length, 5];

            gParallel.ForEach(Interval1i.Range(tris.Length), (k) => {
                if (CancelF()) return;
                Vector3d a0 = Vector3d.Zero, a1 = Vector3d.Zero, a2 = Vector3d.Zero;
                Before.GetTriVertices(tris[k], ref a0, ref a1, ref a2);
                a0 -= origin; a1 -= origin; a2 -= origin;
                Plane planeA;
                if (Plane.Of(a0, a1, a2, out planeA) == false)
                    return;     // vertical or degenerate: no footprint
                List<Vector2d> polyA = ccw(a0.xy, a1.xy, a2.xy);

                AxisAlignedBox2d box = new AxisAlignedBox2d(a0.xy);
                box.Contain(a1.xy); box.Contain(a2.xy);
                box.Min += origin.xy; box.Max += origin.xy;
                var traversal = new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (tb, depth) => tb.Min.x <= box.Max.x && tb.Max.x >= box.Min.x && tb.Min.y <= box.Max.y && tb.Max.y >= box.Min.y,
                    NextTriangleF = (tid) => {
                        Vector3d b0 = Vector3d.Zero, b1 = Vector3d.Zero, b2 = Vector3d.Zero;
                        After.GetTriVertices(tid, ref b0, ref b1, ref b2);
                        b0 -= origin; b1 -= origin; b2 -= origin;
                        Plane planeB;
                        if (Plane.Of(b0, b1, b2, out planeB) == false)
                            return;
                        List<Vector2d> poly = clip_convex(polyA, ccw(b0.xy, b1.xy, b2.xy));
                        if (poly.Count < 3)
                            return;
                        integrate(poly, planeA, planeB, k, partial);
                    }
                };
                spatial.DoTraversal(traversal);
            });

            for (int k = 0; k < tris.Length; ++k) {
                FillVolume += partial[k, 0]; CutVolume += partial[k, 1];
                FillArea += partial[k, 2]; CutArea += partial[k, 3]; OverlapArea += partial[k, 4];
            }
            return CancelF() == false;
        }


        // z = c0 + cx * x + cy * y over a non-vertical triangle
        struct Plane
        {
            public double c0, cx, cy;
            public double Z(Vector2d p) { return c0 + cx * p.x + cy * p.y; }
            public static bool Of(Vector3d a, Vector3d b, Vector3d c, out Plane plane)
            {
                plane = new Plane();
                Vector3d n = (b - a).Cross(c - a);
                if (Math.Abs(n.z) <= 1e-12 * n.Length || n.z == 0)
                    return false;
                plane.cx = -n.x / n.z; plane.cy = -n.y / n.z;
                plane.c0 = a.z - plane.cx * a.x - plane.cy * a.y;
                return true;
            }
        }

        static List<Vector2d> ccw(Vector2d a, Vector2d b, Vector2d c)
        {
            double area2 = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            return (area2 >= 0) ? new List<Vector2d>(3) { a, b, c } : new List<Vector2d>(3) { a, c, b };
        }

        // Sutherland-Hodgman: subject clipped by the convex counter-clockwise polygon clip
        static List<Vector2d> clip_convex(List<Vector2d> subject, List<Vector2d> clip)
        {
            List<Vector2d> output = subject;
            int nc = clip.Count;
            for (int i = 0; i < nc && output.Count > 0; ++i) {
                Vector2d e0 = clip[i], e1 = clip[(i + 1) % nc];
                Vector2d d = e1 - e0;
                List<Vector2d> input = output;
                output = new List<Vector2d>(input.Count + 2);
                int n = input.Count;
                for (int j = 0; j < n; ++j) {
                    Vector2d p = input[j], q = input[(j + 1) % n];
                    double sp = d.x * (p.y - e0.y) - d.y * (p.x - e0.x), sq = d.x * (q.y - e0.y) - d.y * (q.x - e0.x);
                    if (sp >= 0) output.Add(p);
                    if ((sp > 0 && sq < 0) || (sp < 0 && sq > 0))
                        output.Add(p + (sp / (sp - sq)) * (q - p));
                }
            }
            return output;
        }

        // split the polygon at diff = 0 and integrate both parts exactly (diff is linear on the polygon)
        static void integrate(List<Vector2d> poly, Plane a, Plane b, int k, double[,] partial)
        {
            int n = poly.Count;
            double[] diff = new double[n];
            for (int i = 0; i < n; ++i)
                diff[i] = b.Z(poly[i]) - a.Z(poly[i]);
            List<Vector2d> pos = new List<Vector2d>(n + 2), neg = new List<Vector2d>(n + 2);
            List<double> posd = new List<double>(n + 2), negd = new List<double>(n + 2);
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                if (diff[i] >= 0) { pos.Add(poly[i]); posd.Add(diff[i]); }
                if (diff[i] <= 0) { neg.Add(poly[i]); negd.Add(diff[i]); }
                if ((diff[i] > 0 && diff[j] < 0) || (diff[i] < 0 && diff[j] > 0)) {
                    Vector2d x = poly[i] + (diff[i] / (diff[i] - diff[j])) * (poly[j] - poly[i]);
                    pos.Add(x); posd.Add(0); neg.Add(x); negd.Add(0);
                }
            }
            double areaPos, volPos, areaNeg, volNeg;
            integrate_linear(pos, posd, out areaPos, out volPos);
            integrate_linear(neg, negd, out areaNeg, out volNeg);
            partial[k, 0] += volPos;
            partial[k, 1] += -volNeg;
            partial[k, 2] += areaPos;
            partial[k, 3] += areaNeg;
            partial[k, 4] += area(poly);
        }

        // area and integral of the linear function with vertex values f over the convex polygon (fan)
        static void integrate_linear(List<Vector2d> p, List<double> f, out double totalArea, out double integral)
        {
            totalArea = integral = 0;
            for (int i = 1; i + 1 < p.Count; ++i) {
                double ar = 0.5 * ((p[i].x - p[0].x) * (p[i + 1].y - p[0].y) - (p[i].y - p[0].y) * (p[i + 1].x - p[0].x));
                totalArea += ar;
                integral += ar * (f[0] + f[i] + f[i + 1]) / 3;
            }
        }

        static double area(List<Vector2d> p)
        {
            double a = 0;
            for (int i = 1; i + 1 < p.Count; ++i)
                a += 0.5 * ((p[i].x - p[0].x) * (p[i + 1].y - p[0].y) - (p[i].y - p[0].y) * (p[i + 1].x - p[0].x));
            return a;
        }
    }
}
