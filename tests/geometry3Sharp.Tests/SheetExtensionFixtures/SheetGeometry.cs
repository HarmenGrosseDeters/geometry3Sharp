using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Status = gs.MeshSheetExtension.ExtensionStatus;

namespace geometry3Sharp.Tests.SheetExt
{
    // procedural inputs and the analytic helpers of the probe's cases (nothing here is part of the construction)
    internal static class SheetGeometry
    {
        public static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[] t = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 7, 3, 2, 6, 7, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
            for (int i = 0; i < 36; i += 3)
                m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
            return m;
        }

        // a height field z = h(x, y) on the grid lines xs x ys (diagonal from (i, j) to (i + 1, j + 1)); cells with keep == false are left out
        public static DMesh3 Grid(double[] xs, double[] ys, Func<double, double, double> h, bool up, Func<double, double, bool> keep = null)
        {
            var m = new DMesh3();
            int nx = xs.Length - 1, ny = ys.Length - 1;
            var id = new int[nx + 1, ny + 1];
            for (int i = 0; i <= nx; ++i)
                for (int j = 0; j <= ny; ++j)
                    id[i, j] = m.AppendVertex(new Vector3d(xs[i], ys[j], h(xs[i], ys[j])));
            for (int i = 0; i < nx; ++i)
                for (int j = 0; j < ny; ++j) {
                    if (keep != null && !keep((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2))
                        continue;
                    int a = id[i, j], b = id[i + 1, j], c = id[i + 1, j + 1], d = id[i, j + 1];
                    if (up) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
                }
            return new DMesh3(m, true);
        }

        public static double[] Lin(double a, double b, int n)
        {
            var r = new double[n + 1];
            for (int i = 0; i <= n; ++i) r[i] = a + (b - a) * i / n;
            r[n] = b;
            return r;
        }

        // a closed prism: the polygon (x, z) extruded along y from y0 to y1; outward normals (the sign of the volume is fixed)
        public static DMesh3 PrismXZ(IList<Vector2d> poly0, double y0, double y1)
        {
            var poly = new List<Vector2d>(poly0);
            double area2 = 0;
            for (int i = 0; i < poly.Count; ++i) { Vector2d a = poly[i], b = poly[(i + 1) % poly.Count]; area2 += a.x * b.y - b.x * a.y; }
            if (area2 < 0) poly.Reverse();      // counter-clockwise in (x, z)
            int n = poly.Count;
            var m = new DMesh3();
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, y0, poly[i].y));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, y1, poly[i].y));
            foreach (Index3i t in EarClip(poly)) {
                m.AppendTriangle(t.a, t.b, t.c);
                m.AppendTriangle(n + t.a, n + t.c, n + t.b);
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(i, n + j, j);
                m.AppendTriangle(i, n + i, n + j);
            }
            if (MeshSheetExtension.Volume(m) < 0)
                m.ReverseOrientation();
            return m;
        }

        static List<Index3i> EarClip(IList<Vector2d> p)
        {
            int n = p.Count;
            double area = 0;
            for (int i = 0; i < n; ++i) { Vector2d a = p[i], b = p[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            var idx = Enumerable.Range(0, n).ToList();
            if (area < 0) idx.Reverse();
            var res = new List<Index3i>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000) {
                bool clipped = false;
                for (int k = 0; k < idx.Count; ++k) {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    Vector2d a = p[ia], b = p[ib], c = p[ic];
                    double cr = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cr <= 1e-14) continue;
                    bool ear = true;
                    foreach (int q in idx) {
                        if (q == ia || q == ib || q == ic) continue;
                        Vector2d pt = p[q];
                        double d1 = (b.x - a.x) * (pt.y - a.y) - (b.y - a.y) * (pt.x - a.x);
                        double d2 = (c.x - b.x) * (pt.y - b.y) - (c.y - b.y) * (pt.x - b.x);
                        double d3 = (a.x - c.x) * (pt.y - c.y) - (a.y - c.y) * (pt.x - c.x);
                        if (d1 >= 0 && d2 >= 0 && d3 >= 0) { ear = false; break; }
                    }
                    if (!ear) continue;
                    res.Add(new Index3i(ia, ib, ic));
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) res.Add(new Index3i(idx[0], idx[1], idx[2]));
            return res;
        }

        public static DMesh3 Octahedron(double r)
        {
            var m = new DMesh3();
            Vector3d[] v = { new Vector3d(r, 0, 0), new Vector3d(-r, 0, 0), new Vector3d(0, r, 0), new Vector3d(0, -r, 0), new Vector3d(0, 0, r), new Vector3d(0, 0, -r) };
            foreach (Vector3d p in v) m.AppendVertex(p);
            int[,] f = { { 0, 2, 4 }, { 2, 1, 4 }, { 1, 3, 4 }, { 3, 0, 4 }, { 2, 0, 5 }, { 1, 2, 5 }, { 3, 1, 5 }, { 0, 3, 5 } };
            for (int i = 0; i < 8; ++i) m.AppendTriangle(f[i, 0], f[i, 1], f[i, 2]);
            return m;
        }

        public static DMesh3 Sphere(Vector3d c, double r, int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { Radius = r, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) + c);
            if (MeshSheetExtension.Volume(m) < 0) m.ReverseOrientation();
            return m;
        }

        public static DMesh3 Union(params DMesh3[] meshes)
        {
            var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Union);
            if (!op.Compute()) throw new Exception("union failed: " + op.Error);
            return op.Result;
        }

        public static DMesh3 Intersect(DMesh3 a, DMesh3 b)
        {
            var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Intersection);
            if (!op.Compute()) throw new Exception("intersection failed: " + op.Error);
            return op.Result;
        }

        public static DMesh3 Merge(DMesh3 a, DMesh3 b)
        {
            var m = new DMesh3(a);
            var map = new Dictionary<int, int>();
            foreach (int v in b.VertexIndices()) map[v] = m.AppendVertex(b.GetVertex(v));
            foreach (int t in b.TriangleIndices()) { Index3i tv = b.GetTriangle(t); m.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]); }
            return m;
        }

        public static DMesh3 Triangles(Vector3d[] verts, int[] tris)
        {
            var m = new DMesh3();
            foreach (Vector3d v in verts) m.AppendVertex(v);
            for (int i = 0; i < tris.Length; i += 3) m.AppendTriangle(tris[i], tris[i + 1], tris[i + 2]);
            return m;
        }

        // the meshes as one, vertices at equal positions shared
        public static DMesh3 Weld(params DMesh3[] meshes)
        {
            var r = new DMesh3();
            var at = new Dictionary<Vector3d, int>();
            foreach (DMesh3 m in meshes) {
                var map = new Dictionary<int, int>();
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    if (!at.TryGetValue(p, out int id)) at[p] = id = r.AppendVertex(p);
                    map[v] = id;
                }
                foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); r.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]); }
            }
            return r;
        }

        public static DMesh3 Reversed(DMesh3 m) { var r = new DMesh3(m); r.ReverseOrientation(); return r; }

        // ---- frames: a rotation about a fixed axis plus a translation, applied to every vertex in doubles
        public static DMesh3 Transformed(DMesh3 m, Func<Vector3d, Vector3d> f)
        {
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) r.SetVertex(v, f(r.GetVertex(v)));
            return r;
        }

        public static Func<Vector3d, Vector3d> Rotation(Vector3d axis, double degrees, Vector3d shift)
        {
            axis = axis.Normalized;
            double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return p => p * c + axis.Cross(p) * s + axis * (axis.Dot(p) * (1 - c)) + shift;
        }

        // ---- analytic volumes

        // the integral of z over the part of the triangles' xy projection inside [x0, x1] x [y0, y1] (the volume under a height field)
        public static double VolumeUnder(DMesh3 sheet, double x0, double y0, double x1, double y1)
        {
            double sum = 0;
            foreach (int t in sheet.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                sheet.GetTriVertices(t, ref a, ref b, ref c);
                var poly = new List<Vector2d> { new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y) };
                // z is the plane through the three points: z = z0 + gx (x - ax) + gy (y - ay)
                double det = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                if (Math.Abs(det) < 1e-300) continue;
                double gx = ((b.z - a.z) * (c.y - a.y) - (c.z - a.z) * (b.y - a.y)) / det;
                double gy = ((c.z - a.z) * (b.x - a.x) - (b.z - a.z) * (c.x - a.x)) / det;
                poly = Clip(poly, 0, x0, true); poly = Clip(poly, 0, x1, false);
                poly = Clip(poly, 1, y0, true); poly = Clip(poly, 1, y1, false);
                if (poly.Count < 3) continue;
                double area = 0; var cen = Vector2d.Zero;
                for (int i = 1; i + 1 < poly.Count; ++i) {
                    Vector2d p0 = poly[0], p1 = poly[i], p2 = poly[i + 1];
                    double ar = ((p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y)) / 2;
                    area += ar;
                    cen += (p0 + p1 + p2) / 3 * ar;
                }
                if (Math.Abs(area) < 1e-300) continue;
                cen /= area;
                double sign = det > 0 ? 1 : -1;
                sum += Math.Abs(area) * (a.z + gx * (cen.x - a.x) + gy * (cen.y - a.y));
            }
            return sum;
        }

        static List<Vector2d> Clip(List<Vector2d> poly, int axis, double v, bool keepGreater)
        {
            var res = new List<Vector2d>();
            for (int i = 0; i < poly.Count; ++i) {
                Vector2d p = poly[i], q = poly[(i + 1) % poly.Count];
                double dp = (axis == 0 ? p.x : p.y) - v, dq = (axis == 0 ? q.x : q.y) - v;
                if (!keepGreater) { dp = -dp; dq = -dq; }
                if (dp >= 0) res.Add(p);
                if ((dp >= 0) != (dq >= 0)) {
                    double t = dp / (dp - dq);
                    res.Add(p + (q - p) * t);
                }
            }
            return res;
        }
    }
}
