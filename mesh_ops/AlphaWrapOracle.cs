// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Distance queries against the input of MeshAlphaWrap: a bounding volume hierarchy over triangles,
    /// segments (degenerate triangles) and points, with
    ///   - the closest point to a point,
    ///   - whether a tetrahedron touches the input (separating axes with a margin),
    ///   - the first point of a segment at distance r (entry into the offset volume).
    /// Degenerate triangles are stored as the segment between their two farthest vertices (or as a point), so
    /// no query divides by a zero area.
    /// </summary>
    internal class AlphaWrapOracle
    {
        const int LeafSize = 4;

        // primitive k: kind[k] = 0 point (pa), 1 segment (pa, pb), 2 triangle (pa, pb, pc)
        Vector3d[] pa, pb, pc, pn;
        byte[] kind;
        AxisAlignedBox3d[] primBox;

        // nodes: leaf if Count > 0 (primitives order[Start .. Start + Count)), else children Start and Start + 1.
        // N, DMin, DMax: a slab around the node's primitives: unit normal (mean triangle normal), min / max of N . x;
        // thin where the input is locally flat, an extra separating axis for the queries. (One array of structs: a
        // query reads all fields of a node it visits, from one or two cache lines)
        struct Node
        {
            public Vector3d Min, Max, N;
            public double DMin, DMax;
            public int Start, Count;
        }
        Node[] nd;
        int[] order;
        int nodes = 0;

        /// <summary> (tests) the tetrahedron query skips nodes that a face plane of the tetrahedron separates from it </summary>
        internal bool FacePruning = true;

        public readonly AxisAlignedBox3d Bounds;
        public int PrimitiveCount { get { return kind.Length; } }
        public int Triangles, Segments, Points;

        /// <summary> triangles as consecutive vertex triples, plus single points </summary>
        public AlphaWrapOracle(IList<Vector3d> triangleVertices, IList<Vector3d> points)
        {
            int nt = triangleVertices.Count / 3, np = points.Count;
            var la = new List<Vector3d>(); var lb = new List<Vector3d>(); var lc = new List<Vector3d>();
            var lk = new List<byte>();
            for (int t = 0; t < nt; ++t)
                add_triangle(triangleVertices[3 * t], triangleVertices[3 * t + 1], triangleVertices[3 * t + 2], la, lb, lc, lk);
            for (int i = 0; i < np; ++i) {
                la.Add(points[i]); lb.Add(points[i]); lc.Add(points[i]); lk.Add(0);
            }
            pa = la.ToArray(); pb = lb.ToArray(); pc = lc.ToArray(); kind = lk.ToArray();
            int n = kind.Length;
            pn = new Vector3d[n];
            primBox = new AxisAlignedBox3d[n];
            Bounds = AxisAlignedBox3d.Empty;
            for (int k = 0; k < n; ++k) {
                AxisAlignedBox3d b = new AxisAlignedBox3d(pa[k]);
                b.Contain(pb[k]); b.Contain(pc[k]);
                primBox[k] = b;
                Bounds.Contain(b);
                if (kind[k] == 2)
                    pn[k] = Unit((pb[k] - pa[k]).Cross(pc[k] - pa[k]));
                if (kind[k] == 0) Points++; else if (kind[k] == 1) Segments++; else Triangles++;
            }
            if (n > 0)
                build();
        }

        /// <summary>
        /// v / |v| for any nonzero finite v (scaled by its largest component first, so no underflow or overflow of the
        /// squared length); not Vector3d.Normalized, which returns zero below an absolute length of 2.2e-16: a small
        /// but valid triangle then had no normal, and every segment near it counted as entering the offset volume
        /// </summary>
        internal static Vector3d Unit(Vector3d v)
        {
            double m = Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
            if (!(m > 0) || double.IsInfinity(m))
                return Vector3d.Zero;
            v /= m;
            return v / v.Length;
        }

        static void add_triangle(Vector3d a, Vector3d b, Vector3d c, List<Vector3d> la, List<Vector3d> lb, List<Vector3d> lc, List<byte> lk)
        {
            double lab = a.DistanceSquared(b), lbc = b.DistanceSquared(c), lca = c.DistanceSquared(a);
            double lmax = Math.Max(lab, Math.Max(lbc, lca));
            double n2 = (b - a).Cross(c - a).LengthSquared;
            if (lmax == 0) {
                la.Add(a); lb.Add(a); lc.Add(a); lk.Add(0);
            } else if (n2 <= 1e-28 * lmax * lmax) {
                // (height below 1e-14 of the longest edge: a segment, the geometry changes by less than that)
                Vector3d p = a, q = b;
                if (lbc == lmax) { p = b; q = c; } else if (lca == lmax) { p = c; q = a; }
                la.Add(p); lb.Add(q); lc.Add(q); lk.Add(1);
            } else {
                la.Add(a); lb.Add(b); lc.Add(c); lk.Add(2);
            }
        }


        void build()
        {
            int n = kind.Length;
            order = new int[n];
            for (int k = 0; k < n; ++k) order[k] = k;
            int maxNodes = 2 * (n / LeafSize + 1) + 1;
            nd = new Node[2 * maxNodes];
            Vector3d[] centers = new Vector3d[n];
            for (int k = 0; k < n; ++k) centers[k] = primBox[k].Center;
            nodes = 1;
            var stack = new Stack<(int node, int start, int count)>();
            stack.Push((0, 0, n));
            var keys = new double[n];
            while (stack.Count > 0) {
                var (node, start, count) = stack.Pop();
                AxisAlignedBox3d box = AxisAlignedBox3d.Empty, cbox = AxisAlignedBox3d.Empty;
                for (int k = start; k < start + count; ++k) {
                    box.Contain(primBox[order[k]]);
                    cbox.Contain(centers[order[k]]);
                }
                nd[node].Min = box.Min; nd[node].Max = box.Max;
                set_slab(node, start, count, box);
                if (count <= LeafSize) {
                    nd[node].Start = start; nd[node].Count = count;
                    continue;
                }
                Vector3d ext = cbox.Diagonal;
                int axis = (ext.x >= ext.y && ext.x >= ext.z) ? 0 : (ext.y >= ext.z ? 1 : 2);
                for (int k = start; k < start + count; ++k) keys[k] = centers[order[k]][axis];
                Array.Sort(keys, order, start, count);
                int half = count / 2;
                int child = nodes;
                nodes += 2;
                nd[node].Start = child; nd[node].Count = 0;
                stack.Push((child, start, half));
                stack.Push((child + 1, start + half, count - half));
            }
        }


        void set_slab(int node, int start, int count, AxisAlignedBox3d box)
        {
            Vector3d n = Vector3d.Zero, first = Vector3d.Zero;
            for (int k = start; k < start + count; ++k) {
                int i = order[k];
                if (kind[i] != 2) continue;
                Vector3d w = (pb[i] - pa[i]).Cross(pc[i] - pa[i]);     // area weighted
                if (first == Vector3d.Zero) first = w;
                n += (w.Dot(first) < 0) ? -w : w;                  // (soups: orientations are arbitrary)
            }
            double len = n.Length;
            if (len > 0) {
                n /= len;
            } else {
                Vector3d e = box.Diagonal;
                n = (e.x <= e.y && e.x <= e.z) ? Vector3d.AxisX : (e.y <= e.z ? Vector3d.AxisY : Vector3d.AxisZ);
            }
            double dmin = double.MaxValue, dmax = -double.MaxValue;
            for (int k = start; k < start + count; ++k) {
                int i = order[k];
                double a = n.Dot(pa[i]), b = n.Dot(pb[i]), c = n.Dot(pc[i]);
                dmin = Math.Min(dmin, Math.Min(a, Math.Min(b, c)));
                dmax = Math.Max(dmax, Math.Max(a, Math.Max(b, c)));
            }
            nd[node].N = n; nd[node].DMin = dmin; nd[node].DMax = dmax;
        }


        // ---- closest point ----

        /// <summary> squared distance from p to the input and the closest point </summary>
        public double DistanceSquared(Vector3d p, out Vector3d closest)
        {
            double best = double.MaxValue;
            closest = Vector3d.Zero;
            if (kind.Length == 0)
                return best;
            closest_rec(0, p, ref best, ref closest);
            return best;
        }

        public double Distance(Vector3d p)
        {
            Vector3d q;
            return Math.Sqrt(DistanceSquared(p, out q));
        }

        void closest_rec(int node, Vector3d p, ref double best, ref Vector3d closest)
        {
            if (nd[node].Count > 0) {
                int end = nd[node].Start + nd[node].Count;
                for (int k = nd[node].Start; k < end; ++k) {
                    int i = order[k];
                    Vector3d q = closest_on_primitive(i, p);
                    double d = q.DistanceSquared(p);
                    if (d < best) { best = d; closest = q; }
                }
                return;
            }
            int c0 = nd[node].Start, c1 = c0 + 1;
            double d0 = box_dist_sqr(c0, p), d1 = box_dist_sqr(c1, p);
            if (d1 < d0) { int t = c0; c0 = c1; c1 = t; double td = d0; d0 = d1; d1 = td; }
            if (d0 < best) closest_rec(c0, p, ref best, ref closest);
            if (d1 < best) closest_rec(c1, p, ref best, ref closest);
        }

        double box_dist_sqr(int node, Vector3d p)
        {
            Vector3d mn = nd[node].Min, mx = nd[node].Max;
            double dx = Math.Max(0, Math.Max(mn.x - p.x, p.x - mx.x));
            double dy = Math.Max(0, Math.Max(mn.y - p.y, p.y - mx.y));
            double dz = Math.Max(0, Math.Max(mn.z - p.z, p.z - mx.z));
            return dx * dx + dy * dy + dz * dz;
        }

        Vector3d closest_on_primitive(int i, Vector3d p)
        {
            switch (kind[i]) {
                case 0: return pa[i];
                case 1: return ClosestOnSegment(p, pa[i], pb[i]);
                default: return ClosestOnTriangle(p, pa[i], pb[i], pc[i]);
            }
        }

        internal static Vector3d ClosestOnSegment(Vector3d p, Vector3d a, Vector3d b)
        {
            Vector3d ab = b - a;
            double l2 = ab.LengthSquared;
            if (l2 == 0) return a;
            double t = (p - a).Dot(ab) / l2;
            if (t <= 0) return a;
            if (t >= 1) return b;
            return a + t * ab;
        }

        /// <summary> closest point on a (non-degenerate) triangle, by Voronoi regions (Ericson, Real-Time Collision Detection, 5.1.5) </summary>
        internal static Vector3d ClosestOnTriangle(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            Vector3d ab = b - a, ac = c - a, ap = p - a;
            double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3d bp = p - b;
            double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
            if (d3 >= 0 && d4 <= d3) return b;
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + (d1 / (d1 - d3)) * ab;
            Vector3d cp = p - c;
            double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
            if (d6 >= 0 && d5 <= d6) return c;
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + (d2 / (d2 - d6)) * ac;
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + ((d4 - d3) / ((d4 - d3) + (d5 - d6))) * (c - b);
            double denom = 1.0 / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        // x on the plane of the triangle: inside or on its edges
        static bool in_triangle(Vector3d x, Vector3d a, Vector3d b, Vector3d c, Vector3d n)
        {
            return (b - a).Cross(x - a).Dot(n) >= 0 && (c - b).Cross(x - b).Dot(n) >= 0 && (a - c).Cross(x - c).Dot(n) >= 0;
        }


        // ---- tetrahedron query ----

        /// <summary>
        /// false only if the (solid) tetrahedron a, b, c, d is farther than margin from the input: a separating
        /// axis with a gap above margin exists for each nearby primitive (tetrahedron face normals, primitive
        /// normal, edge-edge cross products). Conservative: can return true for distances slightly above margin.
        /// </summary>
        public bool TetrahedronTouches(Vector3d a, Vector3d b, Vector3d c, Vector3d d, double margin)
        {
            return TetrahedronTouches(a, b, c, d, margin, out int _);
        }

        /// <summary> (tests) TetrahedronTouches; primitiveTests: the primitives it tested with separating axes </summary>
        internal bool TetrahedronTouches(Vector3d a, Vector3d b, Vector3d c, Vector3d d, double margin, out int primitiveTests)
        {
            primitiveTests = 0;
            if (kind.Length == 0)
                return false;
            AxisAlignedBox3d box = new AxisAlignedBox3d(a);
            box.Contain(b); box.Contain(c); box.Contain(d);
            Tet t = new Tet(a, b, c, d, margin);
            bool touches = tet_rec(0, ref t, box, margin);
            primitiveTests = t.Tests;
            return touches;
        }

        struct Tet
        {
            public Vector3d V0, V1, V2, V3;
            public Vector3d N0, N1, N2, N3;      // face normals (not normalized)
            // per face normal N(f): the projections of the vertices, min and max, computed as separated() computes them;
            // margin^2 |N(f)|^2 as separated() computes it, NaN for a zero normal
            public Vector4d TMin, TMax, Gap2;
            // the face that separated the last node, -1 for none yet; the primitives tested (tests)
            public int Hint, Tests;
            public Tet(Vector3d a, Vector3d b, Vector3d c, Vector3d d, double margin)
            {
                V0 = a; V1 = b; V2 = c; V3 = d;
                N0 = (c - b).Cross(d - b); N1 = (d - a).Cross(c - a); N2 = (b - a).Cross(d - a); N3 = (c - a).Cross(b - a);
                TMin = TMax = Gap2 = Vector4d.Zero;
                Hint = -1; Tests = 0;
                for (int f = 0; f < 4; ++f) {
                    Vector3d axis = N(f);
                    double l2 = axis.LengthSquared;
                    double t0 = axis.Dot(V0), t1 = axis.Dot(V1), t2 = axis.Dot(V2), t3 = axis.Dot(V3);
                    TMin[f] = Math.Min(Math.Min(t0, t1), Math.Min(t2, t3));
                    TMax[f] = Math.Max(Math.Max(t0, t1), Math.Max(t2, t3));
                    Gap2[f] = (l2 > 0) ? margin * margin * l2 : double.NaN;
                }
            }
            public Vector3d V(int i) { return i == 0 ? V0 : (i == 1 ? V1 : (i == 2 ? V2 : V3)); }
            public Vector3d N(int i) { return i == 0 ? N0 : (i == 1 ? N1 : (i == 2 ? N2 : N3)); }
        }

        static readonly int[,] TetEdges = { { 0, 1 }, { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 }, { 2, 3 } };

        bool tet_rec(int node, ref Tet t, AxisAlignedBox3d box, double margin)
        {
            if (box_box_dist_sqr(node, box) > margin * margin)
                return false;
            Vector3d sn = nd[node].N;
            double t0 = sn.Dot(t.V0), t1 = sn.Dot(t.V1), t2 = sn.Dot(t.V2), t3 = sn.Dot(t.V3);
            if (Math.Min(Math.Min(t0, t1), Math.Min(t2, t3)) > nd[node].DMax + margin
                || Math.Max(Math.Max(t0, t1), Math.Max(t2, t3)) < nd[node].DMin - margin)
                return false;
            if (FacePruning && t.Hint >= 0 && face_separates(node, ref t, t.Hint))
                return false;
            if (nd[node].Count > 0) {
                if (FacePruning) {
                    for (int f = 0; f < 4; ++f)
                        if (f != t.Hint && face_separates(node, ref t, f)) { t.Hint = f; return false; }
                }
                int end = nd[node].Start + nd[node].Count;
                for (int k = nd[node].Start; k < end; ++k) {
                    int i = order[k];
                    AxisAlignedBox3d pb = primBox[i];
                    if (pb.Min.x - box.Max.x > margin || box.Min.x - pb.Max.x > margin || pb.Min.y - box.Max.y > margin
                        || box.Min.y - pb.Max.y > margin || pb.Min.z - box.Max.z > margin || box.Min.z - pb.Max.z > margin)
                        continue;
                    t.Tests++;
                    if (tet_primitive_touch(ref t, i, margin)) return true;
                }
                return false;
            }
            return tet_rec(nd[node].Start, ref t, box, margin) || tet_rec(nd[node].Start + 1, ref t, box, margin);
        }

        /// <summary>
        /// Does face plane f of the tetrahedron separate it from every primitive in the node, as tet_primitive_touch would
        /// find with separated() on the axis N(f)? Then the node holds no touching primitive and is skipped: the query's
        /// answer is the same as without this test (Alpha Wrap 3.0 lever 1). The tetrahedron's projections tmin, tmax and
        /// margin^2 |N|^2 are the values separated() computes. For a primitive vertex p in the node box, the computed box
        /// projection bmin = sum_k min(N_k mn_k, N_k mx_k) is at most the computed fl(N . p), without a rounding margin:
        /// mn_k <= p_k <= mx_k, rounding is monotone, so fl(N_k p_k) lies between fl(N_k mn_k) and fl(N_k mx_k), and
        /// both sums add the three terms in the same order (Vector3d.Dot: (x + y) + z; .NET does not contract them into
        /// fused multiply-adds). Likewise bmax >= fl(N . p), so each primitive's computed gap is at least the node's, and
        /// separated() finds a gap above margin |N| for it. NaN gives no separation.
        /// </summary>
        bool face_separates(int node, ref Tet t, int f)
        {
            Vector3d axis = t.N(f), mn = nd[node].Min, mx = nd[node].Max;
            double u0 = axis.x * mn.x, v0 = axis.x * mx.x, u1 = axis.y * mn.y, v1 = axis.y * mx.y, u2 = axis.z * mn.z, v2 = axis.z * mx.z;
            double bmin = Math.Min(u0, v0) + Math.Min(u1, v1) + Math.Min(u2, v2);
            double bmax = Math.Max(u0, v0) + Math.Max(u1, v1) + Math.Max(u2, v2);
            double gap = Math.Max(bmin - t.TMax[f], t.TMin[f] - bmax);
            return gap > 0 && gap * gap > t.Gap2[f];
        }

        double box_box_dist_sqr(int node, AxisAlignedBox3d b)
        {
            Vector3d mn = nd[node].Min, mx = nd[node].Max;
            double dx = Math.Max(0, Math.Max(mn.x - b.Max.x, b.Min.x - mx.x));
            double dy = Math.Max(0, Math.Max(mn.y - b.Max.y, b.Min.y - mx.y));
            double dz = Math.Max(0, Math.Max(mn.z - b.Max.z, b.Min.z - mx.z));
            return dx * dx + dy * dy + dz * dz;
        }

        // separating axis test with a gap: the projections onto axis are more than margin * |axis| apart
        static bool separated(ref Tet t, Vector3d axis, Vector3d p0, Vector3d p1, Vector3d p2, double margin)
        {
            double l2 = axis.LengthSquared;
            if (!(l2 > 0)) return false;
            double t0 = axis.Dot(t.V0), t1 = axis.Dot(t.V1), t2 = axis.Dot(t.V2), t3 = axis.Dot(t.V3);
            double tmin = Math.Min(Math.Min(t0, t1), Math.Min(t2, t3)), tmax = Math.Max(Math.Max(t0, t1), Math.Max(t2, t3));
            double q0 = axis.Dot(p0), q1 = axis.Dot(p1), q2 = axis.Dot(p2);
            double pmin = Math.Min(q0, Math.Min(q1, q2)), pmax = Math.Max(q0, Math.Max(q1, q2));
            double gap = Math.Max(pmin - tmax, tmin - pmax);
            return gap > 0 && gap * gap > margin * margin * l2;
        }

        bool tet_primitive_touch(ref Tet t, int i, double margin)
        {
            Vector3d a = pa[i], b = pb[i], c = pc[i];
            // the triangle's normal first: near the result the cells lie at about the offset above the input
            if (kind[i] == 2 && separated(ref t, pn[i], a, b, c, margin))
                return false;
            for (int f = 0; f < 4; ++f)
                if (separated(ref t, t.N(f), a, b, c, margin)) return false;
            if (kind[i] == 0)
                return true;
            int ne = kind[i] == 2 ? 3 : 1;
            for (int e = 0; e < 6; ++e) {
                Vector3d te = t.V(TetEdges[e, 1]) - t.V(TetEdges[e, 0]);
                for (int k = 0; k < ne; ++k) {
                    Vector3d pe = k == 0 ? b - a : (k == 1 ? c - b : a - c);
                    Vector3d axis = te.Cross(pe);
                    if (axis.LengthSquared <= 1e-24 * te.LengthSquared * pe.LengthSquared)
                        continue;       // parallel edges: covered by the other axes
                    if (separated(ref t, axis, a, b, c, margin)) return false;
                }
            }
            return true;
        }


        // ---- segment entry into the offset volume ----

        /// <summary>
        /// smallest t in [0, 1] with distance(a + t (b - a), input) less or equal r, -1 if none. 0 if a itself is
        /// that close.
        /// </summary>
        public double FirstEntry(Vector3d a, Vector3d b, double r)
        {
            if (kind.Length == 0)
                return -1;
            double best = 2;
            Seg sg = new Seg(a, b - a);
            if (box_entry(0, ref sg, r, 1) < 0)
                return -1;
            entry_rec(0, ref sg, r, ref best);
            return best <= 1 ? best : -1;
        }

        /// <summary>
        /// (tests) FirstEntry without the hierarchy: the smallest entry over all primitives, each by the three edge capsules
        /// and the slab (vertex balls tested twice, as before lever 1)
        /// </summary>
        internal double FirstEntryBruteForce(Vector3d a, Vector3d b, double r)
        {
            double best = -1;
            for (int i = 0; i < kind.Length; ++i) {
                double t = primitive_entry(i, a, b - a, r, 1);
                if (t >= 0 && (best < 0 || t < best)) best = t;
            }
            return best;
        }

        // the segment a + t d with the reciprocals of the direction (computed once per query, the same values as per node)
        struct Seg
        {
            public Vector3d A, D;
            public double Ix, Iy, Iz;
            public Seg(Vector3d a, Vector3d d) { A = a; D = d; Ix = 1.0 / d.x; Iy = 1.0 / d.y; Iz = 1.0 / d.z; }
        }

        // the caller found that the segment enters the node box (expanded by r) at a parameter t <= best: before lever 1,
        // each node computed that entry again with min(best, 1), which also gives t >= 0 then (the entry does not depend
        // on the end parameter, and t <= min(best, 1) passes each axis' clip)
        void entry_rec(int node, ref Seg sg, double r, ref double best)
        {
            double tmax = Math.Min(best, 1);
            Vector3d sn = nd[node].N;
            double s0 = sn.Dot(sg.A), s1 = s0 + tmax * sn.Dot(sg.D);
            if (Math.Min(s0, s1) > nd[node].DMax + r || Math.Max(s0, s1) < nd[node].DMin - r)
                return;
            if (nd[node].Count > 0) {
                int end = nd[node].Start + nd[node].Count;
                for (int k = nd[node].Start; k < end; ++k) {
                    double t = primitive_entry_once(order[k], sg.A, sg.D, r, Math.Min(best, 1));
                    if (t >= 0 && t < best) best = t;
                }
                return;
            }
            int c0 = nd[node].Start, c1 = c0 + 1;
            double t0 = box_entry(c0, ref sg, r, tmax), t1 = box_entry(c1, ref sg, r, tmax);
            if (t1 >= 0 && (t0 < 0 || t1 < t0)) { int tc = c0; c0 = c1; c1 = tc; double tt = t0; t0 = t1; t1 = tt; }
            if (t0 >= 0 && t0 <= best) entry_rec(c0, ref sg, r, ref best);
            if (t1 >= 0 && t1 <= best) entry_rec(c1, ref sg, r, ref best);
        }

        // entry parameter of a + t d, t in [0, tmax], into the node box expanded by r; -1 if it misses
        double box_entry(int node, ref Seg sg, double r, double tmax)
        {
            double t0 = 0, t1 = tmax;
            Vector3d mn = nd[node].Min, mx = nd[node].Max;
            if (!clip(mn.x - r, mx.x + r, sg.A.x, sg.D.x, sg.Ix, ref t0, ref t1)) return -1;
            if (!clip(mn.y - r, mx.y + r, sg.A.y, sg.D.y, sg.Iy, ref t0, ref t1)) return -1;
            if (!clip(mn.z - r, mx.z + r, sg.A.z, sg.D.z, sg.Iz, ref t0, ref t1)) return -1;
            return t0;
        }

        static bool clip(double mn, double mx, double o, double dir, double inv, ref double t0, ref double t1)
        {
            if (dir == 0)
                return !(o < mn || o > mx);
            double ta = (mn - o) * inv, tb = (mx - o) * inv;
            if (ta > tb) { double tt = ta; ta = tb; tb = tt; }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            return !(t0 > t1);
        }

        /// <summary>
        /// primitive_entry with each vertex ball of a triangle tested once (each capsule tested both of its balls). The
        /// result is the same: each part (ball, cylinder side, slab) has an entry that does not depend on tmax and is
        /// accepted if it is at most the current tmax, which then drops to it, so both give the smallest accepted entry
        /// of the same parts (a repeated ball gives the same value again)
        /// </summary>
        double primitive_entry_once(int i, Vector3d a, Vector3d d, double r, double tmax)
        {
            if (kind[i] != 2)
                return primitive_entry(i, a, d, r, tmax);
            Vector3d p = pa[i], q = pb[i], w = pc[i];
            double t = -1, u;
            if ((u = sphere_entry(a, d, p, r, tmax)) >= 0) { if (u == 0) return 0; t = tmax = u; }
            if ((u = sphere_entry(a, d, q, r, tmax)) >= 0) { if (u == 0) return 0; t = tmax = u; }
            if ((u = sphere_entry(a, d, w, r, tmax)) >= 0) { if (u == 0) return 0; t = tmax = u; }
            if ((u = cylinder_entry(a, d, p, q, r, tmax)) >= 0) { if (u == 0) return 0; t = tmax = u; }
            if ((u = cylinder_entry(a, d, q, w, r, tmax)) >= 0) { if (u == 0) return 0; t = tmax = u; }
            if ((u = cylinder_entry(a, d, w, p, r, tmax)) >= 0) { if (u == 0) return 0; t = tmax = u; }
            if ((u = slab_entry(a, d, p, q, w, pn[i], r, tmax)) >= 0) t = u;
            return t;
        }

        double primitive_entry(int i, Vector3d a, Vector3d d, double r, double tmax)
        {
            switch (kind[i]) {
                case 0: return sphere_entry(a, d, pa[i], r, tmax);
                case 1: return capsule_entry(a, d, pa[i], pb[i], r, tmax);
                default: {
                    double t = capsule_entry(a, d, pa[i], pb[i], r, tmax);
                    if (t == 0) return 0;
                    if (t >= 0) tmax = t;
                    double u = capsule_entry(a, d, pb[i], pc[i], r, tmax);
                    if (u >= 0) { t = u; tmax = u; }
                    u = capsule_entry(a, d, pc[i], pa[i], r, tmax);
                    if (u >= 0) { t = u; tmax = u; }
                    u = slab_entry(a, d, pa[i], pb[i], pc[i], pn[i], r, tmax);
                    if (u >= 0) t = u;
                    return t;
                }
            }
        }

        // entry of a + t d into the ball (c, r), t in [0, tmax]; -1 if none
        static double sphere_entry(Vector3d a, Vector3d d, Vector3d c, double r, double tmax)
        {
            Vector3d m = a - c;
            double C = m.LengthSquared - r * r;
            if (C <= 0) return 0;
            double A = d.LengthSquared, B = m.Dot(d);
            if (A == 0 || B >= 0) return -1;
            double disc = B * B - A * C;
            if (disc < 0) return -1;
            double t = (-B - Math.Sqrt(disc)) / A;
            return (t >= 0 && t <= tmax) ? t : -1;
        }

        // entry into the capsule around p-q: the two balls or the lateral surface of the cylinder
        static double capsule_entry(Vector3d a, Vector3d d, Vector3d p, Vector3d q, double r, double tmax)
        {
            double best = -1;
            double t = sphere_entry(a, d, p, r, tmax);
            if (t == 0) return 0;
            if (t >= 0) { best = t; tmax = t; }
            t = sphere_entry(a, d, q, r, tmax);
            if (t == 0) return 0;
            if (t >= 0) { best = t; tmax = t; }
            t = cylinder_entry(a, d, p, q, r, tmax);
            return t >= 0 ? t : best;
        }

        // entry through the lateral surface of the cylinder around p-q between p and q, t in [0, tmax]; 0 if a is inside
        // that part of the cylinder, -1 if none (also if a starts inside the infinite cylinder beyond p or q: it enters
        // through a ball then)
        static double cylinder_entry(Vector3d a, Vector3d d, Vector3d p, Vector3d q, double r, double tmax)
        {
            Vector3d e = q - p;
            double l2 = e.LengthSquared;
            if (l2 == 0) return -1;
            Vector3d m = a - p;
            double me = m.Dot(e), de = d.Dot(e);
            Vector3d mp = m - (me / l2) * e, dp = d - (de / l2) * e;
            double C = mp.LengthSquared - r * r;
            if (C <= 0) {
                double s0 = me / l2;
                return (s0 >= 0 && s0 <= 1) ? 0 : -1;
            }
            double A = dp.LengthSquared, B = mp.Dot(dp);
            if (A == 0 || B >= 0) return -1;
            double disc = B * B - A * C;
            if (disc < 0) return -1;
            double t = (-B - Math.Sqrt(disc)) / A;
            if (t < 0 || t > tmax) return -1;
            double s = (me + t * de) / l2;
            return (s >= 0 && s <= 1) ? t : -1;
        }

        // entry through the two faces of the triangle's prism (offset planes, inside the triangle)
        static double slab_entry(Vector3d a, Vector3d d, Vector3d p0, Vector3d p1, Vector3d p2, Vector3d n, double r, double tmax)
        {
            double h0 = (a - p0).Dot(n), dn = d.Dot(n);
            if (Math.Abs(h0) <= r) {
                Vector3d x = a - h0 * n;
                if (in_triangle(x, p0, p1, p2, n)) return 0;
                return -1;      // enters through a side wall, which is inside the edge capsules
            }
            double t;
            if (h0 > r) {
                if (dn >= 0) return -1;
                t = (r - h0) / dn;
            } else {
                if (dn <= 0) return -1;
                t = (-r - h0) / dn;
            }
            if (t < 0 || t > tmax) return -1;
            Vector3d y = a + t * d;
            Vector3d yp = y - (y - p0).Dot(n) * n;
            return in_triangle(yp, p0, p1, p2, n) ? t : -1;
        }
    }
}
