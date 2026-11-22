// Candidate code shared by the `cull` prototype and the A/B inside the rounding: the split-clipped sub-boxes of a
// triangle and, at the end, the separating-axis prefilter with its axes kept flat. The sub-boxes are the candidate of docs/research/ROUNDING_COST_2026-11.md, section 5, for the
// box-neighbour enumeration of SafeRounding's first search. Not library code. The part between the BEGIN and END marks
// is also pasted into the throw-away worktree by trace/instrument_phases.py (the A/B inside the rounding), so it uses
// nothing outside the library.
//
// The rule (the idea of spatial splits in ray tracing: Ernst and Greiner 2007, "Early split clipping for bounding
// volume hierarchies"; Stich et al. 2009, "Spatial splits in bounding volume hierarchies"; papers only). The box of the
// triangle is cut into k slabs along its longest axis; sub-box j is a box of the part of the triangle inside slab j
// (the triangle clipped to the slab). The parts cover the triangle, so every point of the triangle lies in one of its
// sub-boxes, and two triangles with a common point have a pair of sub-boxes that overlap (closed). A search that tests
// every pair of triangles with such a pair of sub-boxes therefore finds every intersecting pair: the same set as the
// search over one box per triangle, with fewer candidates where a thin triangle is tilted in its box.
//
// Conservative in doubles. Along the split axis the sub-box is the slab itself (the cuts are doubles, compared exactly).
// On another axis c the part's extent is over the corners inside the slab (exact) and the crossings of the edges with
// the two cuts. A crossing of edge (p, q) with the cut s is p_c + (q_c - p_c) (s - p_a) / (q_a - p_a), computed in
// doubles as
//     y~ = fl(p_c + fl(fl(q_c - p_c) * fl(fl(s - p_a) / fl(q_a - p_a))))
// with |y~ - y| <= 14 u max(|p_c|, |q_c|), u = 2^-53: the quotient t has three roundings and 0 <= t <= 1, the
// difference one, the product one, the sum one, so |y~ - y| <= (1 + u) (((1 + u)^5 - 1) |q_c - p_c| t + u |y|)
// <= 6 u |q_c - p_c| + 2 u max(|p_c|, |q_c|) <= 14 u max(|p_c|, |q_c|) (|q_c - p_c| <= 2 max). The sub-box takes
// [y~ - m, y~ + m] with m = fl(Pad max(|p_c|, |q_c|)), Pad = 32 u (more than twice the bound), cut to the edge's own extent
// [min(p_c, q_c), max(p_c, q_c)], which the crossing never leaves. Assumption, as in the separating-axis lemma: no
// product underflows to a subnormal (the supported range of MeshRoundingRepair, [2^-200, 2^200], gives it here: the
// operands are coordinate differences and a quotient in [0, 1]; a difference is exact when it is subnormal, and a
// product below 2^-1022 has an absolute error below 2^-1074 against a margin of at least 2^-248 where a coordinate is
// not zero; where both are zero the product is zero). Not checked with Z3: the prototype's evidence is the equal pair
// sets and result hashes; a build would need the bounds review.
using System;
using g3;

static class SplitBoxes
{
    public static int Of(Vector3d a, Vector3d b, Vector3d c, int maxParts, double[] boxes)
    {
        return RoundingSplit.SplitBoxes(a, b, c, maxParts, boxes);
    }

    public static void AxesOf(ref Triangle3d t, double[] axis, int oa, double[] range, int or)
    {
        RoundingSplit.AxesOf(t.V0, t.V1, t.V2, axis, oa, range, or);
    }

    public static bool AxesApart(double[] axis, double[] range, int a, ref Triangle3d u)
    {
        return RoundingSplit.AxesApart(axis, 12 * a, range, 8 * a, u.V0, u.V1, u.V2);
    }
}

static class RoundingSplit
{
    // BEGIN shared with trace/instrument_phases.py
        const double SplitPad = 32 * 1.1102230246251565e-16;

        // the sub-boxes of triangle (a, b, c), at most maxParts of them, into boxes (6 doubles each: min x, y, z, max x,
        // y, z); returns their number, 1 (the triangle's box) where a split gains nothing. How many parts is a heuristic
        // in doubles (any number is correct): the box's second extent over the triangle's width, so that a part's box is
        // about as wide as the triangle
        internal static int SplitBoxes(Vector3d a, Vector3d b, Vector3d c, int maxParts, double[] boxes)
        {
            double minx = Math.Min(a.x, Math.Min(b.x, c.x)), maxx = Math.Max(a.x, Math.Max(b.x, c.x));
            double miny = Math.Min(a.y, Math.Min(b.y, c.y)), maxy = Math.Max(a.y, Math.Max(b.y, c.y));
            double minz = Math.Min(a.z, Math.Min(b.z, c.z)), maxz = Math.Max(a.z, Math.Max(b.z, c.z));
            boxes[0] = minx; boxes[1] = miny; boxes[2] = minz; boxes[3] = maxx; boxes[4] = maxy; boxes[5] = maxz;
            if (maxParts < 2)
                return 1;
            double ex = maxx - minx, ey = maxy - miny, ez = maxz - minz;
            int axis = ex >= ey && ex >= ez ? 0 : ey >= ez ? 1 : 2;
            double longest = Math.Max(ex, Math.Max(ey, ez));
            double second = axis == 0 ? Math.Max(ey, ez) : axis == 1 ? Math.Max(ex, ez) : Math.Max(ex, ey);
            // the triangle's width: twice its area over its longest edge
            Vector3d ab = b - a, ac = c - a, bc = c - b;
            double edge = Math.Sqrt(Math.Max(ab.LengthSquared, Math.Max(ac.LengthSquared, bc.LengthSquared)));
            double width = ab.Cross(ac).Length / edge;
            // (not finite, flat in its box or as wide as it: one box; `!(x > y)` also takes a NaN)
            if (!(second > 2 * width) || !(longest > 0) || double.IsInfinity(longest))
                return 1;
            double want = second / width;
            int parts = want >= maxParts ? maxParts : (int)want;
            if (parts < 2)
                return 1;
            double lo = axis == 0 ? minx : axis == 1 ? miny : minz, hi = axis == 0 ? maxx : axis == 1 ? maxy : maxz;
            int count = 0;
            double s0 = lo;
            for (int j = 1; j <= parts; ++j) {
                // (the cuts: any non-decreasing doubles from lo to hi)
                double s1 = j == parts ? hi : Math.Min(hi, Math.Max(s0, lo + longest * j / parts));
                double bminx = double.PositiveInfinity, bminy = double.PositiveInfinity, bminz = double.PositiveInfinity;
                double bmaxx = double.NegativeInfinity, bmaxy = double.NegativeInfinity, bmaxz = double.NegativeInfinity;
                for (int e = 0; e < 3; ++e) {
                    Vector3d p = e == 0 ? a : e == 1 ? b : c, q = e == 0 ? b : e == 1 ? c : a;
                    double pa = p[axis], qa = q[axis];
                    // the corner p, if it is in the slab (exact)
                    if (pa >= s0 && pa <= s1) {
                        if (p.x < bminx) bminx = p.x; if (p.x > bmaxx) bmaxx = p.x;
                        if (p.y < bminy) bminy = p.y; if (p.y > bmaxy) bmaxy = p.y;
                        if (p.z < bminz) bminz = p.z; if (p.z > bmaxz) bmaxz = p.z;
                    }
                    // the crossings of edge (p, q) with the two cuts (strictly inside the edge: at a corner the corner counts)
                    for (int side = 0; side < 2; ++side) {
                        double s = side == 0 ? s0 : s1;
                        if ((pa < s && s < qa) == false && (qa < s && s < pa) == false)
                            continue;
                        double t = (s - pa) / (qa - pa);
                        for (int k = 0; k < 3; ++k) {
                            if (k == axis)
                                continue;
                            double pc = p[k], qc = q[k];
                            double y = pc + (qc - pc) * t;
                            double m = SplitPad * Math.Max(Math.Abs(pc), Math.Abs(qc));
                            double ylo = Math.Max(y - m, Math.Min(pc, qc)), yhi = Math.Min(y + m, Math.Max(pc, qc));
                            // (a crossing that is not finite: the edge's whole extent)
                            if (!(ylo <= yhi)) { ylo = Math.Min(pc, qc); yhi = Math.Max(pc, qc); }
                            if (k == 0) { if (ylo < bminx) bminx = ylo; if (yhi > bmaxx) bmaxx = yhi; }
                            else if (k == 1) { if (ylo < bminy) bminy = ylo; if (yhi > bmaxy) bmaxy = yhi; }
                            else { if (ylo < bminz) bminz = ylo; if (yhi > bmaxz) bmaxz = yhi; }
                        }
                    }
                }
                // (the split axis: the slab)
                if (axis == 0) { bminx = s0; bmaxx = s1; } else if (axis == 1) { bminy = s0; bmaxy = s1; } else { bminz = s0; bmaxz = s1; }
                // (a slab without a part of the triangle cannot be: every cut between lo and hi meets it; kept as a guard)
                if (bminx <= bmaxx && bminy <= bmaxy && bminz <= bmaxz) {
                    int o = 6 * count++;
                    boxes[o] = bminx; boxes[o + 1] = bminy; boxes[o + 2] = bminz; boxes[o + 3] = bmaxx; boxes[o + 4] = bmaxy; boxes[o + 5] = bmaxz;
                }
                s0 = s1;
            }
            if (count == 0) {
                boxes[0] = minx; boxes[1] = miny; boxes[2] = minz; boxes[3] = maxx; boxes[4] = maxy; boxes[5] = maxz;
                return 1;
            }
            return count;
        }

        // The separating-axis prefilter of docs/design/SNAP_ROUNDING_3D.md section 5.3 (lemma: tools/Z3Pilot/separating_axis.py),
        // with the axes of a triangle computed once and kept flat: the triangle's normal and the three in-plane normals of
        // its edges into axis (12 doubles from oa), its interval on each, widened by the margin, into range (8 doubles from or)
        const double AxisGamma = 8 * 1.1102230246251565e-16;

        internal static void AxesOf(Vector3d v0, Vector3d v1, Vector3d v2, double[] axis, int oa, double[] range, int or)
        {
            Vector3d nrm = (v1 - v0).Cross(v2 - v0);
            for (int k = 0; k < 4; ++k) {
                Vector3d a = k == 0 ? nrm : k == 1 ? nrm.Cross(v1 - v0) : k == 2 ? nrm.Cross(v2 - v1) : nrm.Cross(v0 - v2);
                axis[oa + 3 * k] = a.x; axis[oa + 3 * k + 1] = a.y; axis[oa + 3 * k + 2] = a.z;
                double p0 = a.Dot(v0), p1 = a.Dot(v1), p2 = a.Dot(v2);
                // (an axis that is not finite never separates)
                bool ok = !(double.IsNaN(p0 + p1 + p2) || double.IsInfinity(p0 + p1 + p2));
                double m = AxisGamma * Math.Max(AbsDot(a, v0), Math.Max(AbsDot(a, v1), AbsDot(a, v2)));
                range[or + 2 * k] = ok ? Math.Min(p0, Math.Min(p1, p2)) - m : double.NegativeInfinity;
                range[or + 2 * k + 1] = ok ? Math.Max(p0, Math.Max(p1, p2)) + m : double.PositiveInfinity;
            }
        }

        static double AbsDot(Vector3d a, Vector3d x)
        {
            return Math.Abs(a.x * x.x) + Math.Abs(a.y * x.y) + Math.Abs(a.z * x.z);
        }

        // the corners u0, u1, u2 are strictly apart from the triangle of (axis, range) along one of its four axes, with the margins
        internal static bool AxesApart(double[] axis, int oa, double[] range, int or, Vector3d u0, Vector3d u1, Vector3d u2)
        {
            for (int k = 0; k < 4; ++k) {
                double ax = axis[oa + 3 * k], ay = axis[oa + 3 * k + 1], az = axis[oa + 3 * k + 2];
                double p0 = ax * u0.x + ay * u0.y + az * u0.z, p1 = ax * u1.x + ay * u1.y + az * u1.z, p2 = ax * u2.x + ay * u2.y + az * u2.z;
                double m0 = Math.Abs(ax * u0.x) + Math.Abs(ay * u0.y) + Math.Abs(az * u0.z), m1 = Math.Abs(ax * u1.x) + Math.Abs(ay * u1.y) + Math.Abs(az * u1.z),
                    m2 = Math.Abs(ax * u2.x) + Math.Abs(ay * u2.y) + Math.Abs(az * u2.z);
                double m = AxisGamma * Math.Max(m0, Math.Max(m1, m2));
                if (Math.Min(p0, Math.Min(p1, p2)) - m > range[or + 2 * k + 1] || Math.Max(p0, Math.Max(p1, p2)) + m < range[or + 2 * k])
                    return true;
            }
            return false;
        }
    // END shared
}
