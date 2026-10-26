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
    /// hierarchy of uniform grids over the UV bounding box (cells of twice the median size of the triangles of
    /// each level), so that mixed triangle sizes stay near linear.
    /// </summary>
    public class UVOverlap
    {
        /// <summary> pairs of triangle ids whose UV interiors overlap </summary>
        public int OverlappingPairs;
        /// <summary> triangles with negative UV orientation (clockwise) </summary>
        public int InvertedTriangles;
        /// <summary> triangles with zero UV area (exactly collinear corners) </summary>
        public int DegenerateTriangles;
        /// <summary> ids of the inverted triangles (all of them) </summary>
        public List<int> InvertedTriangleIds = new List<int>();
        /// <summary> ids of the degenerate triangles (all of them) </summary>
        public List<int> DegenerateTriangleIds = new List<int>();
        /// <summary> the overlapping pairs (at most MaxReportedPairs) </summary>
        public List<Index2i> Pairs = new List<Index2i>();
        public int MaxReportedPairs = 100;

        /// <summary> no overlaps, no inverted and no degenerate UV triangles </summary>
        public bool IsInjective { get { return OverlappingPairs == 0 && InvertedTriangles == 0 && DegenerateTriangles == 0; } }

        /// <summary> test the UVs (by vertex id) of the triangles of the mesh </summary>
        public static UVOverlap Compute(DMesh3 mesh, IList<Vector2d> uv, int maxReportedPairs = 100)
        {
            var r = new UVOverlap() { MaxReportedPairs = maxReportedPairs };
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
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                Vector2d a = uv[t.a], b = uv[t.b], c = uv[t.c];
                if (!finite(a) || !finite(b) || !finite(c)) { DegenerateTriangles++; DegenerateTriangleIds.Add(tid); continue; }
                int o = ExactPredicates.Orient2d(a, b, c);
                if (o == 0) { DegenerateTriangles++; DegenerateTriangleIds.Add(tid); continue; }   // empty interior
                if (o < 0) { InvertedTriangles++; InvertedTriangleIds.Add(tid); Vector2d tmp = b; b = c; c = tmp; }
                AxisAlignedBox2d box = new AxisAlignedBox2d(a);
                box.Contain(b); box.Contain(c);
                tris.Add(new Tri() { Id = tid, A = a, B = b, C = c, Box = box });
                all.Contain(box);
            }
            if (tris.Count < 2)
                return;

            // a hierarchy of uniform grids: each level has cells of about twice the median size of the triangles
            // that did not fit a finer level, and keeps the triangles covering at most LargeCells of its cells (at
            // least half of them). Pairs within a level are tested in the cell of the lower left corner of their
            // box intersection, pairs across levels by looking up the finer triangle in the coarser grids.
            var levels = new List<Level>();
            int[] levelOf = new int[tris.Count];
            var remaining = new List<int>();
            for (int i = 0; i < tris.Count; ++i) remaining.Add(i);
            while (remaining.Count > 0) {
                var level = new Level(tris, remaining, all, levels.Count == MaxLevels - 1);
                foreach (int i in level.Members) levelOf[i] = levels.Count;
                levels.Add(level);
                remaining = level.Rest;
            }
            foreach (Level level in levels) {
                foreach (var kv in level.Cells) {
                    List<int> list = kv.Value;
                    for (int p = 0; p < list.Count; ++p)
                        for (int q = p + 1; q < list.Count; ++q) {
                            Tri s = tris[list[p]], t = tris[list[q]];
                            if (!boxesOverlap(s.Box, t.Box))
                                continue;
                            int rx = level.CX(Math.Max(s.Box.Min.x, t.Box.Min.x)), ry = level.CY(Math.Max(s.Box.Min.y, t.Box.Min.y));
                            if (ry * level.NX + rx != kv.Key)
                                continue;
                            test(s, t);
                        }
                }
            }
            int[] stamp = new int[tris.Count];
            for (int i = 0; i < tris.Count; ++i) {
                AxisAlignedBox2d b = tris[i].Box;
                for (int l = levelOf[i] + 1; l < levels.Count; ++l) {
                    Level level = levels[l];
                    int x0 = level.CX(b.Min.x), x1 = level.CX(b.Max.x), y0 = level.CY(b.Min.y), y1 = level.CY(b.Max.y);
                    for (int y = y0; y <= y1; ++y)
                        for (int x = x0; x <= x1; ++x) {
                            List<int> list;
                            if (!level.Cells.TryGetValue(y * level.NX + x, out list)) continue;
                            foreach (int j in list) {
                                if (stamp[j] == i + 1) continue;
                                stamp[j] = i + 1;
                                if (boxesOverlap(b, tris[j].Box))
                                    test(tris[i], tris[j]);
                            }
                        }
                }
            }
        }

        // interiors can only overlap if the open boxes overlap
        static bool boxesOverlap(AxisAlignedBox2d s, AxisAlignedBox2d t)
        {
            return s.Min.x < t.Max.x && t.Min.x < s.Max.x && s.Min.y < t.Max.y && t.Min.y < s.Max.y;
        }

        const int MaxLevels = 32;

        class Level
        {
            public int NX, NY;
            double hx, hy, x0, y0;
            public Dictionary<int, List<int>> Cells = new Dictionary<int, List<int>>();
            public List<int> Members = new List<int>(), Rest = new List<int>();

            public Level(List<Tri> tris, List<int> items, AxisAlignedBox2d all, bool last)
            {
                var sizes = new List<double>(items.Count);
                foreach (int i in items) sizes.Add(Math.Max(tris[i].Box.Width, tris[i].Box.Height));
                sizes.Sort();
                double h = 2 * sizes[sizes.Count / 2];
                double W = Math.Max(all.Width, 1e-300), H = Math.Max(all.Height, 1e-300);
                if (!(h > 0)) h = Math.Max(W, H);
                NX = (int)Math.Min(2048, Math.Max(1, Math.Ceiling(W / h)));
                NY = (int)Math.Min(2048, Math.Max(1, Math.Ceiling(H / h)));
                hx = W / NX; hy = H / NY; x0 = all.Min.x; y0 = all.Min.y;
                foreach (int i in items) {
                    AxisAlignedBox2d b = tris[i].Box;
                    int cx0 = CX(b.Min.x), cx1 = CX(b.Max.x), cy0 = CY(b.Min.y), cy1 = CY(b.Max.y);
                    if (!last && (long)(cx1 - cx0 + 1) * (cy1 - cy0 + 1) > LargeCells) { Rest.Add(i); continue; }
                    Members.Add(i);
                    for (int y = cy0; y <= cy1; ++y)
                        for (int x = cx0; x <= cx1; ++x) {
                            int key = y * NX + x;
                            List<int> list;
                            if (!Cells.TryGetValue(key, out list)) { list = new List<int>(); Cells[key] = list; }
                            list.Add(i);
                        }
                }
            }

            public int CX(double x) { return Math.Min(NX - 1, Math.Max(0, (int)((x - x0) / hx))); }
            public int CY(double y) { return Math.Min(NY - 1, Math.Max(0, (int)((y - y0) / hy))); }
        }

        const int LargeCells = 64;

        void test(Tri s, Tri t)
        {
            if (InteriorsOverlap(s.A, s.B, s.C, t.A, t.B, t.C)) {
                OverlappingPairs++;
                if (Pairs.Count < MaxReportedPairs)
                    Pairs.Add(new Index2i(Math.Min(s.Id, t.Id), Math.Max(s.Id, t.Id)));
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
