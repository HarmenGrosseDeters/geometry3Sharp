// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Exact test of a UV layout for overlaps: pairs of UV triangles whose interiors intersect, and UV triangles
    /// that are inverted (clockwise) or degenerate (zero area). Uses the exact orientation predicate
    /// (ExactPredicates.Orient2d) on the double coordinates, so touching triangles (shared edges and vertices,
    /// charts that only touch) are never reported and every reported pair really overlaps.
    ///
    /// Two triangles with disjoint interiors are separated by the line through one of their six edges
    /// (separating axis theorem for convex polygons), which is what is tested. Candidate pairs come from a
    /// uniform grid over the UV bounding box.
    /// </summary>
    public class UVOverlap
    {
        /// <summary> pairs of triangle ids whose UV interiors overlap </summary>
        public int OverlappingPairs;
        /// <summary> triangles with negative UV orientation (clockwise) </summary>
        public int InvertedTriangles;
        /// <summary> triangles with zero UV area (exactly collinear corners) </summary>
        public int DegenerateTriangles;
        /// <summary> the overlapping pairs (at most MaxReportedPairs) </summary>
        public List<Index2i> Pairs = new List<Index2i>();
        public int MaxReportedPairs = 100;

        /// <summary> no overlaps, no inverted and no degenerate UV triangles </summary>
        public bool IsInjective { get { return OverlappingPairs == 0 && InvertedTriangles == 0 && DegenerateTriangles == 0; } }

        /// <summary> test the UVs (by vertex id) of the triangles of the mesh </summary>
        public static UVOverlap Compute(DMesh3 mesh, IList<Vector2d> uv)
        {
            var r = new UVOverlap();
            r.compute(mesh, uv);
            return r;
        }

        /// <summary> test the vertex UVs of the mesh </summary>
        public static UVOverlap Compute(DMesh3 mesh)
        {
            if (!mesh.HasVertexUVs)
                throw new ArgumentException("UVOverlap.Compute: mesh has no vertex UVs");
            Vector2d[] uv = new Vector2d[mesh.MaxVertexID];
            foreach (int vid in mesh.VertexIndices())
                uv[vid] = (Vector2d)mesh.GetVertexUV(vid);
            return Compute(mesh, uv);
        }

        struct Tri
        {
            public int Id;
            public Vector2d A, B, C;   // counter-clockwise
            public AxisAlignedBox2d Box;
        }

        void compute(DMesh3 mesh, IList<Vector2d> uv)
        {
            var tris = new List<Tri>();
            AxisAlignedBox2d all = AxisAlignedBox2d.Empty;
            double sizeSum = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                Vector2d a = uv[t.a], b = uv[t.b], c = uv[t.c];
                if (!finite(a) || !finite(b) || !finite(c)) { DegenerateTriangles++; continue; }
                int o = ExactPredicates.Orient2d(a, b, c);
                if (o == 0) { DegenerateTriangles++; continue; }   // empty interior
                if (o < 0) { InvertedTriangles++; Vector2d tmp = b; b = c; c = tmp; }
                AxisAlignedBox2d box = new AxisAlignedBox2d(a);
                box.Contain(b); box.Contain(c);
                tris.Add(new Tri() { Id = tid, A = a, B = b, C = c, Box = box });
                all.Contain(box);
                sizeSum += Math.Max(box.Width, box.Height);
            }
            if (tris.Count < 2)
                return;

            // grid with cells of about the mean triangle size, at most ~4M cells
            double h = 2 * sizeSum / tris.Count;
            double W = Math.Max(all.Width, 1e-300), H = Math.Max(all.Height, 1e-300);
            if (!(h > 0)) h = Math.Max(W, H);
            int nx = (int)Math.Min(2048, Math.Max(1, Math.Ceiling(W / h)));
            int ny = (int)Math.Min(2048, Math.Max(1, Math.Ceiling(H / h)));
            double hx = W / nx, hy = H / ny;
            Func<double, int> cx = (x) => Math.Min(nx - 1, Math.Max(0, (int)((x - all.Min.x) / hx)));
            Func<double, int> cy = (y) => Math.Min(ny - 1, Math.Max(0, (int)((y - all.Min.y) / hy)));
            var cells = new Dictionary<int, List<int>>();
            for (int i = 0; i < tris.Count; ++i) {
                AxisAlignedBox2d b = tris[i].Box;
                int x0 = cx(b.Min.x), x1 = cx(b.Max.x), y0 = cy(b.Min.y), y1 = cy(b.Max.y);
                for (int y = y0; y <= y1; ++y)
                    for (int x = x0; x <= x1; ++x) {
                        int key = y * nx + x;
                        List<int> list;
                        if (!cells.TryGetValue(key, out list)) { list = new List<int>(); cells[key] = list; }
                        list.Add(i);
                    }
            }
            foreach (var kv in cells) {
                List<int> list = kv.Value;
                int key = kv.Key;
                for (int p = 0; p < list.Count; ++p)
                    for (int q = p + 1; q < list.Count; ++q) {
                        Tri s = tris[list[p]], t = tris[list[q]];
                        // interiors can only overlap if the open boxes overlap
                        if (!(s.Box.Min.x < t.Box.Max.x && t.Box.Min.x < s.Box.Max.x && s.Box.Min.y < t.Box.Max.y && t.Box.Min.y < s.Box.Max.y))
                            continue;
                        // test each pair once: in the cell of the lower left corner of the box intersection
                        int rx = cx(Math.Max(s.Box.Min.x, t.Box.Min.x)), ry = cy(Math.Max(s.Box.Min.y, t.Box.Min.y));
                        if (ry * nx + rx != key)
                            continue;
                        if (InteriorsOverlap(s.A, s.B, s.C, t.A, t.B, t.C)) {
                            OverlappingPairs++;
                            if (Pairs.Count < MaxReportedPairs)
                                Pairs.Add(new Index2i(Math.Min(s.Id, t.Id), Math.Max(s.Id, t.Id)));
                        }
                    }
            }
        }

        static bool finite(Vector2d p) { return MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y); }

        /// <summary>
        /// true if the interiors of the counter-clockwise triangles (a0,a1,a2) and (b0,b1,b2) intersect (exact)
        /// </summary>
        public static bool InteriorsOverlap(Vector2d a0, Vector2d a1, Vector2d a2, Vector2d b0, Vector2d b1, Vector2d b2)
        {
            return !separates(a0, a1, b0, b1, b2) && !separates(a1, a2, b0, b1, b2) && !separates(a2, a0, b0, b1, b2)
                && !separates(b0, b1, a0, a1, a2) && !separates(b1, b2, a0, a1, a2) && !separates(b2, b0, a0, a1, a2);
        }

        // the line through the counter-clockwise edge (p,q) has all of x,y,z on its closed right side
        static bool separates(Vector2d p, Vector2d q, Vector2d x, Vector2d y, Vector2d z)
        {
            return ExactPredicates.Orient2d(p, q, x) <= 0 && ExactPredicates.Orient2d(p, q, y) <= 0 && ExactPredicates.Orient2d(p, q, z) <= 0;
        }
    }
}
