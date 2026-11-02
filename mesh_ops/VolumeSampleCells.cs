// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The sample region of the volume IoU check (MeshRepairMetrics.VolumeIoUAtLeast) for two winding-closed meshes
    /// (ExactMeshBoolean.IsWindingClosed): an adaptive octree over a box (Meagher 1982: inside, outside and boundary
    /// cells), refined level by level where a triangle of either mesh touches a cell.
    ///
    /// The winding number of a winding-closed mesh is an integer that changes only across its surface (Jacobson et al.
    /// 2013), so it is constant on a cell that no triangle touches (decided exactly: TriangleTouchesBox). One exact
    /// evaluation at the cell's centre (ExactWinding, a ray with exactly decided crossings) classifies the whole cell: inside both solids, inside one of them, or in
    /// neither. Those cells are known exactly and are not sampled (stratified sampling); only the touched cells of the
    /// last level are (Sampled, Next). The union of both solids is then covered exactly by the known cells plus the
    /// sampled ones (docs/design/VOLUME_CHECK_SAMPLING.md, section 4), and the estimate of the IoU stays unbiased.
    ///
    /// Cell-size rule: a level is refined while its 8 children per touched cell fit in the remaining ChildBudget
    /// (tests of a child cell), at most MaxDepth levels; the cells are ever smaller near the surfaces, independent of
    /// the units (2^k scales give the same cells, scaled). Deterministic: the children are tested in parallel into
    /// their own slots and merged in a fixed order.
    /// </summary>
    internal sealed class VolumeSampleCells
    {
        /// <summary> tests of child cells (the cost of the build), for all levels together </summary>
        internal const int DefaultChildBudget = 4096;
        internal const int DefaultMaxDepth = 12;

        // the root box, and the cell spacing of each level: w / 2^L (exact, so that neighbouring cells of any level
        // share their faces bit for bit)
        readonly Vector3d origin;
        readonly AxisAlignedBox3d root;
        readonly Vector3d[] spacing;

        /// <summary> sampled cells (touched by a triangle, or not classified), as boxes, in a fixed order </summary>
        internal readonly List<AxisAlignedBox3d> Sampled = new List<AxisAlignedBox3d>();
        double[] cumulative;

        /// <summary> volume of the cells known to be inside both solids, inside only A, inside only B </summary>
        internal double KnownBoth, KnownOnlyA, KnownOnlyB;
        /// <summary> volume of the sampled cells </summary>
        internal double SampledVolume;
        internal int ChildTests, Depth;
        /// <summary> untouched cells without a non-degenerate ray from their centre (sampled, not classified; not expected) </summary>
        internal int Unclassified;
        /// <summary> centres at which the exact winding numbers were evaluated </summary>
        internal int WindingEvaluations;

        internal double KnownUnion { get { return KnownBoth + KnownOnlyA + KnownOnlyB; } }

        /// <summary> with keepKnown: the classified cells and their class (1 in A's solid, 2 in B's, 3 both, 0 neither; for tests) </summary>
        internal readonly List<KeyValuePair<AxisAlignedBox3d, int>> Known;

        struct Cell
        {
            public int i, j, k;
            public Cell(int i, int j, int k) { this.i = i; this.j = j; this.k = k; }
        }

        /// <summary>
        /// Builds the cells over root for the meshes a and b (both winding-closed, with their trees; their triangles
        /// within root). serial: no parallel loop (the same result).
        /// </summary>
        internal VolumeSampleCells(DMesh3 a, DMeshAABBTree3 treeA, DMesh3 b, DMeshAABBTree3 treeB, AxisAlignedBox3d root,
            int childBudget = DefaultChildBudget, int maxDepth = DefaultMaxDepth, bool serial = false, bool keepKnown = false)
        {
            if (keepKnown)
                Known = new List<KeyValuePair<AxisAlignedBox3d, int>>();
            origin = root.Min;
            this.root = root;
            Vector3d w = root.Max - root.Min;
            spacing = new Vector3d[maxDepth + 1];
            spacing[0] = w;
            for (int L = 1; L <= maxDepth; ++L)
                spacing[L] = 0.5 * spacing[L - 1];      // (exact)

            var level = new List<Cell>();
            int rootClass = classify(a, treeA, b, treeB, new Cell(0, 0, 0), 0);
            if (rootClass < 0)
                level.Add(new Cell(0, 0, 0));
            else
                add_known(rootClass, box(new Cell(0, 0, 0), 0));
            int L0 = 0, budget = childBudget;
            while (L0 < maxDepth && level.Count > 0 && 8L * level.Count <= budget) {
                budget -= 8 * level.Count;
                ChildTests += 8 * level.Count;
                var cls = new int[8 * level.Count];
                List<Cell> cur = level;
                int L = L0 + 1;
                gParallel.BlockStartEnd(0, cur.Count - 1, (start, end) => {
                    for (int n = start; n <= end; ++n)
                        classify_children(a, treeA, b, treeB, cur[n], L, cls, 8 * n);
                }, 16, serial);
                level = new List<Cell>();
                for (int n = 0; n < cur.Count; ++n)
                    for (int c = 0; c < 8; ++c) {
                        Cell ch = child(cur[n], c);
                        if (cls[8 * n + c] < 0)
                            level.Add(ch);
                        else
                            add_known(cls[8 * n + c], box(ch, L));
                    }
                L0 = L;
            }
            Depth = L0;
            cumulative = new double[level.Count];
            double sum = 0;
            for (int n = 0; n < level.Count; ++n) {
                AxisAlignedBox3d bx = box(level[n], L0);
                Sampled.Add(bx);
                cumulative[n] = sum += bx.Volume;
            }
            SampledVolume = sum;
        }

        void add_known(int cls, AxisAlignedBox3d bx)
        {
            double volume = bx.Volume;
            if (Known != null)
                Known.Add(new KeyValuePair<AxisAlignedBox3d, int>(bx, cls));
            if (cls == 3) KnownBoth += volume;
            else if (cls == 1) KnownOnlyA += volume;
            else if (cls == 2) KnownOnlyB += volume;
        }

        static Cell child(Cell c, int n)
        {
            return new Cell(2 * c.i + (n & 1), 2 * c.j + ((n >> 1) & 1), 2 * c.k + ((n >> 2) & 1));
        }

        AxisAlignedBox3d box(Cell c, int L)
        {
            Vector3d h = spacing[L];
            var min = new Vector3d(origin.x + c.i * h.x, origin.y + c.j * h.y, origin.z + c.k * h.z);
            var max = new Vector3d(origin.x + (c.i + 1) * h.x, origin.y + (c.j + 1) * h.y, origin.z + (c.k + 1) * h.z);
            return new AxisAlignedBox3d(min, max);
        }

        // -1: touched by a triangle of a or b, or not classified (sampled or refined); otherwise the class of the whole
        // cell: 1 in a's solid, 2 in b's, 3 in both, 0 in neither
        int classify(DMesh3 a, DMeshAABBTree3 treeA, DMesh3 b, DMeshAABBTree3 treeB, Cell c, int L)
        {
            AxisAlignedBox3d bx = box(c, L);
            if (Touches(a, treeA, bx) || Touches(b, treeB, bx))
                return -1;
            return classify_at(a, treeA, b, treeB, bx);
        }

        // the class of an untouched cell from the exact winding numbers at its centre (-1: no ray without a degenerate
        // crossing was found, not expected)
        int classify_at(DMesh3 a, DMeshAABBTree3 treeA, DMesh3 b, DMeshAABBTree3 treeB, AxisAlignedBox3d bx)
        {
            System.Threading.Interlocked.Increment(ref WindingEvaluations);
            Vector3d centre = 0.5 * (bx.Min + bx.Max);
            int wa, wb;
            if (ExactWinding(a, treeA, centre, root, out wa) == false || ExactWinding(b, treeB, centre, root, out wb) == false) {
                System.Threading.Interlocked.Increment(ref Unclassified);
                return -1;
            }
            return (wa > 0 ? 1 : 0) | (wb > 0 ? 2 : 0);
        }

        /// <summary>
        /// The winding number of a winding-closed mesh at p (not on its surface), exactly: the signed crossings of the
        /// segment from p to a point q outside root (which holds the mesh, so w(q) = 0), each decided by the signs of
        /// ExactPredicates.Orient3d. A segment through an edge or a vertex of a crossed triangle, or in a triangle's
        /// plane, is degenerate: another direction is tried (fixed sequence; the result does not depend on it). False if
        /// no direction of 64 works.
        /// </summary>
        internal static bool ExactWinding(DMesh3 mesh, DMeshAABBTree3 tree, Vector3d p, AxisAlignedBox3d root, out int winding)
        {
            winding = 0;
            Vector3d c = root.Center;
            double ext = 4 * Math.Max(root.MaxDim, Math.Max(Math.Max(Math.Abs(p.x - c.x), Math.Abs(p.y - c.y)), Math.Abs(p.z - c.z)));
            var rnd = new Random(17);
            Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
            for (int attempt = 0; attempt < 64; ++attempt) {
                // a far point: one coordinate at +-ext from the centre, the others within, odd fractions of ext
                int axis = rnd.Next(3);
                var dir = new Vector3d(rnd.Next(-1000, 1001), rnd.Next(-1000, 1001), rnd.Next(-1000, 1001)) / 1024.0;
                dir[axis] = rnd.Next(2) == 0 ? -1 : 1;
                Vector3d q = c + ext * dir;
                // pruning of the tree's boxes against the segment, widened (only the pruning; the crossings are exact)
                double pad = 1e-9 * (Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z) + Math.Abs(q.x) + Math.Abs(q.y) + Math.Abs(q.z));
                int w = 0;
                bool degenerate = false;
                Vector3d pp = p, qq = q;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (tb, depth) => degenerate == false && segment_meets_box(pp, qq, tb, pad),
                    NextTriangleF = (tid) => {
                        if (degenerate) return;
                        mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
                        int sp = ExactPredicates.Orient3d(v0, v1, v2, pp), sq = ExactPredicates.Orient3d(v0, v1, v2, qq);
                        if (sp == 0 && sq == 0) {
                            // in the plane of the triangle, or a triangle of no area (crossing it changes nothing)
                            if (collinear(v0, v1, v2) == false)
                                degenerate = true;
                            return;
                        }
                        if (sp == 0 || sq == 0 || sp == sq)
                            return;     // an end on the plane (outside the triangle: p is off the surface, q outside root)
                        int o0 = ExactPredicates.Orient3d(pp, qq, v0, v1), o1 = ExactPredicates.Orient3d(pp, qq, v1, v2),
                            o2 = ExactPredicates.Orient3d(pp, qq, v2, v0);
                        if (o0 == 0 || o1 == 0 || o2 == 0) {
                            // through the line of an edge: degenerate if within the triangle's edges
                            if ((o0 >= 0 && o1 >= 0 && o2 >= 0) || (o0 <= 0 && o1 <= 0 && o2 <= 0))
                                degenerate = true;
                            return;
                        }
                        // from q (w = 0) towards p: entering the back side of a triangle adds 1
                        if (o0 == o1 && o1 == o2)
                            w += sp < 0 ? 1 : -1;
                    }
                });
                if (degenerate == false) {
                    winding = w;
                    return true;
                }
            }
            return false;
        }

        static bool collinear(Vector3d a, Vector3d b, Vector3d c)
        {
            return ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y)) == 0
                && ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z)) == 0
                && ExactPredicates.Orient2d(new Vector2d(a.z, a.x), new Vector2d(b.z, b.x), new Vector2d(c.z, c.x)) == 0;
        }

        // does the segment pq meet the box widened by pad (slab test in doubles; conservative with the pad)
        static bool segment_meets_box(Vector3d p, Vector3d q, AxisAlignedBox3d box, double pad)
        {
            double t0 = 0, t1 = 1;
            for (int k = 0; k < 3; ++k) {
                double lo = box.Min[k] - pad, hi = box.Max[k] + pad, d = q[k] - p[k];
                if (d == 0) {
                    if (p[k] < lo || p[k] > hi) return false;
                    continue;
                }
                double ta = (lo - p[k]) / d, tb = (hi - p[k]) / d;
                if (ta > tb) { double t = ta; ta = tb; tb = t; }
                t0 = Math.Max(t0, ta - 1e-9);
                t1 = Math.Min(t1, tb + 1e-9);
                if (t0 > t1) return false;
            }
            return true;
        }

        // The 8 children of a touched cell into cls[offset..offset+7]. Every closed child contains the parent's centre, so
        // the untouched children together form a connected set that no triangle meets (the centre is then not on a
        // surface either): they have the same winding numbers, evaluated once, at the centre of the first of them.
        void classify_children(DMesh3 a, DMeshAABBTree3 treeA, DMesh3 b, DMeshAABBTree3 treeB, Cell parent, int L, int[] cls, int offset)
        {
            int first = -1, cl = -1;
            for (int c = 0; c < 8; ++c) {
                AxisAlignedBox3d bx = box(child(parent, c), L);
                if (Touches(a, treeA, bx) || Touches(b, treeB, bx)) {
                    cls[offset + c] = -1;
                    continue;
                }
                if (first < 0) {
                    first = c;
                    cl = classify_at(a, treeA, b, treeB, bx);
                }
                cls[offset + c] = cl;
            }
        }

        /// <summary> a uniform random point of the sampled cells (none: NaN) </summary>
        internal Vector3d Next(Random rnd)
        {
            if (Sampled.Count == 0)
                return new Vector3d(double.NaN, double.NaN, double.NaN);
            double u = rnd.NextDouble() * cumulative[cumulative.Length - 1];
            int k = Array.BinarySearch(cumulative, u);
            k = k < 0 ? ~k : k + 1;
            k = Math.Min(k, Sampled.Count - 1);
            AxisAlignedBox3d bx = Sampled[k];
            return bx.Min + new Vector3d(rnd.NextDouble() * bx.Width, rnd.NextDouble() * bx.Height, rnd.NextDouble() * bx.Depth);
        }

        /// <summary> does a triangle of mesh touch the closed box (exactly, TriangleTouchesBox)? </summary>
        internal static bool Touches(DMesh3 mesh, DMeshAABBTree3 tree, AxisAlignedBox3d box)
        {
            // the tree's boxes are stored as centre and extent, which can round inwards by an ulp: the pruning box is
            // widened by a few ulps of the coordinates (only the pruning; the triangles are tested exactly)
            double mag = Math.Max(Math.Max(Math.Abs(box.Min.x), Math.Abs(box.Max.x)), Math.Max(Math.Max(Math.Abs(box.Min.y), Math.Abs(box.Max.y)),
                Math.Max(Math.Abs(box.Min.z), Math.Abs(box.Max.z))));
            AxisAlignedBox3d query = box;
            query.Expand(16 * MathUtil.Epsilon * mag);
            bool found = false;
            Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero;
            tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                NextBoxDF = (tb, depth) => found == false && closed_overlap(tb, query),
                NextTriangleF = (tid) => {
                    if (found) return;
                    mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
                    if (TriangleTouchesBox(v0, v1, v2, box))
                        found = true;
                }
            });
            return found;
        }

        static bool closed_overlap(AxisAlignedBox3d p, AxisAlignedBox3d q)
        {
            return p.Min.x <= q.Max.x && q.Min.x <= p.Max.x && p.Min.y <= q.Max.y && q.Min.y <= p.Max.y
                && p.Min.z <= q.Max.z && q.Min.z <= p.Max.z;
        }

        /// <summary>
        /// Does the closed triangle (a, b, c) intersect the closed box? Exact (ExactPredicates; no tolerance): the
        /// separating axis test with the 13 axes of a triangle and a box (the 3 coordinate axes, the triangle's normal,
        /// the 9 cross products of its edges with the coordinate axes), each decided by signs only. Disjoint compact
        /// convex sets are strictly separated along one of them. A degenerate triangle (a segment or a point) has zero
        /// vectors among these axes, which never separate; the remaining ones are complete for it as well.
        /// </summary>
        internal static bool TriangleTouchesBox(Vector3d a, Vector3d b, Vector3d c, AxisAlignedBox3d box)
        {
            // coordinate axes
            for (int k = 0; k < 3; ++k) {
                double mn = Math.Min(a[k], Math.Min(b[k], c[k])), mx = Math.Max(a[k], Math.Max(b[k], c[k]));
                if (mn > box.Max[k] || mx < box.Min[k])
                    return false;
            }
            // the triangle's plane: all 8 corners strictly on one side
            int side = 0;
            bool separated = true;
            for (int n = 0; n < 8 && separated; ++n) {
                var p = new Vector3d((n & 1) == 0 ? box.Min.x : box.Max.x, (n & 2) == 0 ? box.Min.y : box.Max.y,
                    (n & 4) == 0 ? box.Min.z : box.Max.z);
                int s = ExactPredicates.Orient3d(a, b, c, p);
                if (s == 0 || (side != 0 && s != side))
                    separated = false;
                side = s;
            }
            if (separated)
                return false;
            // edge x coordinate axis k: in the projection along k, the line of the edge separates the projected box
            // (its 4 corners strictly on one side) from the third vertex (on the other side, or on the line). In that
            // projection the triangle and the box are convex polygons; when they are disjoint, the line of an edge of
            // one of them separates them (the box's edges are the coordinate axes, tested above)
            for (int k = 0; k < 3; ++k) {
                int u = (k + 1) % 3, v = (k + 2) % 3;
                Vector2d pa = new Vector2d(a[u], a[v]), pb = new Vector2d(b[u], b[v]), pc = new Vector2d(c[u], c[v]);
                if (edge_separates(pa, pb, pc, box, u, v) || edge_separates(pb, pc, pa, box, u, v) || edge_separates(pc, pa, pb, box, u, v))
                    return false;
            }
            return true;
        }

        // the line through p, q (projected) has the box's projection strictly on the side away from r
        static bool edge_separates(Vector2d p, Vector2d q, Vector2d r, AxisAlignedBox3d box, int u, int v)
        {
            if (p == q)
                return false;       // (no line; the axis is zero)
            int sr = ExactPredicates.Orient2d(p, q, r);
            int side = 0;
            for (int n = 0; n < 4; ++n) {
                var corner = new Vector2d((n & 1) == 0 ? box.Min[u] : box.Max[u], (n & 2) == 0 ? box.Min[v] : box.Max[v]);
                int s = ExactPredicates.Orient2d(p, q, corner);
                if (s == 0 || s == sr || (side != 0 && s != side))
                    return false;
                side = s;
            }
            return true;
        }
    }
}
