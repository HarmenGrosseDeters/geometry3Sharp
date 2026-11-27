// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Triangle quality pass for a simplified alpha wrap (MeshRobustRepair.AlphaWrapQualityPass): edge flips,
    /// tangential vertex moves and, if switched on, cap splits, cap flips and needle collapses around triangles with a
    /// small minimum angle (or a large maximum angle), each kept only if
    ///   - it raises the smallest angle of the changed triangles by at least MinGainDegrees,
    ///   - no changed triangle turns by more than MaxNormalTurnDegrees or becomes degenerate,
    ///   - every changed triangle lies within Tolerance of the reference (the unsimplified wrap), certified by
    ///     TriangleDeviationBounds.IsWithin (with the patch bound),
    ///   - and, with CheckSelfIntersections, no changed triangle intersects another one (exact test, as the Reducer's
    ///     PreventSelfIntersections).
    /// So the result stays within Tolerance of the reference wherever it changed, and keeps the genus (splits and
    /// collapses change the vertices, collapses only under the link condition). The smallest angle of the mesh cannot
    /// drop (every operation keeps the smallest angle of its triangles), but the largest one is bounded only by splits
    /// and cap flips: a flip, move or collapse may raise the largest angle of its triangles. Serial and in a fixed
    /// order: deterministic. Scale invariant (angles and relative thresholds only).
    /// </summary>
    internal sealed class AlphaWrapQualityPass
    {
        internal DMesh3 Mesh;
        internal readonly DMesh3 Reference;
        internal double Tolerance;
        internal bool CheckSelfIntersections = true;
        /// <summary> a mesh (as a built tree, only read) that no new triangle may touch: the wrap's input, which the wrap encloses (see Reducer.KeepClearOf) </summary>
        internal DMeshAABBTree3 KeepClearOf = null;
        internal Func<bool> CancelF = () => false;

        /// <summary> triangles with a smaller minimum angle are improved (their edges flipped, their vertices moved) </summary>
        internal double TargetAngleDegrees = 30;
        internal double MinGainDegrees = 0.5;
        internal double MaxNormalTurnDegrees = 30;
        internal int MaxPasses = 8;
        /// <summary> the two kinds of operations (flips off: the Hu et al. 2017 variant, which avoids flips because they destroy creases) </summary>
        internal bool EnableFlips = true, EnableMoves = true;
        /// <summary> move along the reference: the tangential target is moved onto the reference at the vertex's own offset from it </summary>
        internal bool FollowReference = false;
        /// <summary> no flip of an edge whose two triangles meet at a larger angle (a crease) </summary>
        internal double MaxFlipDihedralDegrees = 180;
        /// <summary> visit the edges and vertices in each pass by increasing smallest angle (worst first), not by id </summary>
        internal bool WorstFirst = false;
        /// <summary>
        /// an operation must not raise the area-weighted mean angle between the changed triangles' normals and the
        /// reference's normals (at the reference triangle nearest to each triangle's centroid)
        /// </summary>
        internal bool KeepNormalDeviation = false;
        /// <summary> KeepNormalDeviation: the new triangles may deviate this much more (degrees) </summary>
        internal double NormalDeviationSlackDegrees = 0;
        /// <summary> KeepNormalDeviation: sample the reference's normal at the centroid only (1) or also at the corners and edge midpoints (7) </summary>
        internal int NormalSamples = 1;
        /// <summary> no move of a vertex on an edge whose triangles meet at a larger angle (crease and feature vertices stay; 180: off) </summary>
        internal double PinCreaseVerticesDegrees = 180;
        /// <summary>
        /// segments to keep away from (eg the input's sharp edges): no operation changes a triangle closer than
        /// ProtectedDistance to one of them. Empty: off
        /// </summary>
        internal List<Segment3d> ProtectedEdges = new List<Segment3d>();
        internal double ProtectedDistance = 0;
        /// <summary> ProtectedEdges hold for flips only (moves and splits near them stay allowed) </summary>
        internal bool ProtectFlipsOnly = false;
        bool protectThisOperation = true;
        Dictionary<Vector3i, List<int>> protectedCells;
        double protectedCell;
        Vector3d protectedOrigin;
        /// <summary>
        /// split the longest edge of triangles with a larger angle (caps) at the foot of the altitude from the opposite
        /// vertex: the new vertex lies on the edge up to rounding, so the surface hardly changes; the actual position is
        /// checked, as every operation (180: off). The owner's approval of splits (2026-11-02) caps the growth: at most
        /// MaxTriangleGrowth more triangles than at the start of each Compute
        /// </summary>
        internal double SplitCapsAboveDegrees = 180;
        internal double MaxTriangleGrowth = 0.05;
        /// <summary>
        /// cap flips: a flip that lowers the larger maximum angle of its two triangles from above CapAngleDegrees by at
        /// least 5 degrees, without lowering their smaller minimum angle, is made also without a minimum-angle gain;
        /// and KeepNormalDeviation does not apply to it if its two triangles meet at no more than CapFlipMaxDihedralDegrees
        /// (nearly flat: the normals change by at most that). 180: off
        /// </summary>
        internal double CapAngleDegrees = 180;
        internal double CapFlipMaxDihedralDegrees = 10;
        /// <summary>
        /// needle collapses (Hu et al. 2017): the shortest edge of a triangle below TargetAngleDegrees collapsed to its
        /// midpoint or one of its ends (a crease vertex, with PinCreaseVerticesDegrees, keeps its position), under the
        /// same guards as every operation and the link condition
        /// </summary>
        internal bool EnableCollapses = false;

        // outputs
        internal int Flips, Moves, Splits, Collapses, RejectedByDeviation, RejectedByIntersection, RejectedByNormals, RejectedByProtection, Passes;
        int maxTriangles;

        DMeshAABBTree3 referenceTree;
        TriangleDeviationBounds bounds;
        TriangleHashGrid grid;
        double cosTurn;

        internal AlphaWrapQualityPass(DMesh3 mesh, DMesh3 reference, double tolerance)
        {
            Mesh = mesh; Reference = reference; Tolerance = tolerance;
        }

        /// <summary> improves Mesh in place; false if cancelled (Mesh is then partly improved, still within Tolerance) </summary>
        internal bool Compute()
        {
            Prepare();
            for (int pass = 0; pass < MaxPasses; ++pass) {
                int before = Flips + Moves + Splits + Collapses;
                Passes++;
                if ((EnableCollapses && collapse_pass() == false) || (SplitCapsAboveDegrees < 180 && split_pass() == false) || (EnableFlips && flip_pass() == false) || (EnableMoves && move_pass() == false))
                    return false;
                if (Flips + Moves + Splits + Collapses == before)
                    break;
            }
            return true;
        }

        /// <summary> resets the counters and builds the queries (Compute does it; for tests of accept) </summary>
        internal void Prepare()
        {
            skipNormals = false;
            protectThisOperation = true;
            clock = 0;
            stamps = new int[Mesh.MaxVertexID];
            for (int k = 0; k < rejectedAt.Length; ++k) rejectedAt[k] = new Dictionary<int, int>();
            Flips = Moves = Splits = Collapses = RejectedByDeviation = RejectedByIntersection = RejectedByNormals = RejectedByProtection = Passes = SkippedRejections = 0;
            maxTriangles = MathUtil.FloorToIntSaturating(Mesh.TriangleCount * (1 + MaxTriangleGrowth));
            referenceTree = new DMeshAABBTree3(Reference, true);
            bounds = new TriangleDeviationBounds(Reference) { PatchBound = true };
            cosTurn = Math.Cos(MaxNormalTurnDegrees * MathUtil.Deg2Rad);
            grid = CheckSelfIntersections ? build_grid() : null;
            build_protected();
        }

        const double MinNormal = 2.2250738585072014e-308; // 2^-1022
        const double FastScaleMin = 7.888609052210118e-31; // 2^-100
        const double FastScaleMax = 1.2676506002282294e30; // 2^100

        static int Pow2Exp(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int biased = (int)((bits >> 52) & 0x7FFL);
            if (biased != 0)
                return biased - 1023;
            long sbits = BitConverter.DoubleToInt64Bits(v * 18014398509481984.0); // 2^54
            return (int)((sbits >> 52) & 0x7FFL) - 1023 - 54;
        }

        static Vector3d ScaleVec(Vector3d v, int n)
        {
            return new Vector3d(ExactPoint3.ScaleB(v.x, n), ExactPoint3.ScaleB(v.y, n), ExactPoint3.ScaleB(v.z, n));
        }

        static double SafeLength(Vector3d d)
        {
            double d2 = d.LengthSquared;
            if (d2 >= 4 * MinNormal && !double.IsInfinity(d2))
                return Math.Sqrt(d2);
            double m = d.MaxAbs;
            if (double.IsNaN(m)) return double.NaN;
            if (m == 0) return 0;
            if (double.IsInfinity(m)) return double.PositiveInfinity;
            int e = Pow2Exp(m);
            Vector3d ds = ScaleVec(d, -e);
            return ExactPoint3.ScaleB(Math.Sqrt(ds.LengthSquared), e);
        }

        static double SafeMaxDimDiv64(AxisAlignedBox3d b)
        {
            Vector3d d = b.Max - b.Min;
            if (d.IsFinite)
                return b.MaxDim / 64;
            double dx = Math.Max(0, 0.015625 * b.Max.x - 0.015625 * b.Min.x);
            double dy = Math.Max(0, 0.015625 * b.Max.y - 0.015625 * b.Min.y);
            double dz = Math.Max(0, 0.015625 * b.Max.z - 0.015625 * b.Min.z);
            return Math.Max(dx, Math.Max(dy, dz));
        }

        static Segment3d SafeSegment(Vector3d p0, Vector3d p1)
        {
            Vector3d d = p1 - p0;
            double s = d.MaxAbs;
            if (s >= FastScaleMin && s <= FastScaleMax && p0.MaxAbs <= FastScaleMax && p1.MaxAbs <= FastScaleMax)
                return new Segment3d(p0, p1);
            Vector3d center = 0.5 * p0 + 0.5 * p1;
            if (!d.IsFinite) {
                d = 0.5 * p1 - 0.5 * p0;
                double halfLen = SafeLength(d);
                if (!(halfLen > 0) || double.IsInfinity(halfLen))
                    return new Segment3d(center, Vector3d.AxisX, 0);
                int hexp = Pow2Exp(d.MaxAbs);
                Vector3d hds = ScaleVec(d, -hexp);
                return new Segment3d(center, hds / Math.Sqrt(hds.LengthSquared), halfLen);
            }
            double len = SafeLength(d);
            if (!(len > 0) || double.IsInfinity(len))
                return new Segment3d(center, Vector3d.AxisX, 0);
            int exp = Pow2Exp(s);
            Vector3d ds = ScaleVec(d, -exp);
            return new Segment3d(center, ds / Math.Sqrt(ds.LengthSquared), 0.5 * len);
        }

        static Vector3d tri_normal(DMesh3 m, int tid)
        {
            Vector3d n = m.GetTriNormal(tid);
            if (n.LengthSquared >= 0.5) return n;
            Index3i tv = m.GetTriangle(tid);
            return unit_normal(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c), out n) ? n : Vector3d.Zero;
        }

        // the protected segments by grid cells of size ProtectedDistance (at least 1 / 64 of the box)
        void build_protected()
        {
            protectedCells = null;
            if (ProtectedEdges.Count == 0 || !(ProtectedDistance > 0)) return;
            protectedCell = Math.Max(ProtectedDistance, SafeMaxDimDiv64(Mesh.CachedBounds));
            protectedOrigin = Mesh.CachedBounds.Min;
            protectedCells = new Dictionary<Vector3i, List<int>>();
            for (int i = 0; i < ProtectedEdges.Count; ++i) {
                Segment3d sg = ProtectedEdges[i];
                Vector3d p0 = sg.P0, p1 = sg.P1;
                if (!p0.IsFinite || !p1.IsFinite) continue;
                var bx = new AxisAlignedBox3d(p0); bx.Contain(p1);
                foreach (Vector3i c in cells(bx, 0)) {
                    List<int> l;
                    if (protectedCells.TryGetValue(c, out l) == false) protectedCells[c] = l = new List<int>();
                    l.Add(i);
                }
            }
        }

        // the cells of a box expanded by expand, relative to the mesh's box (small indices also far from the origin);
        // rounded outward and clamped monotonically to [-1, 66] so overlapping intervals always share a cell
        IEnumerable<Vector3i> cells(AxisAlignedBox3d bx, double expand)
        {
            double lx = expand > 0 ? ExactInterval.Down(ExactInterval.Down(bx.Min.x - expand) - protectedOrigin.x) : ExactInterval.Down(bx.Min.x - protectedOrigin.x);
            double ly = expand > 0 ? ExactInterval.Down(ExactInterval.Down(bx.Min.y - expand) - protectedOrigin.y) : ExactInterval.Down(bx.Min.y - protectedOrigin.y);
            double lz = expand > 0 ? ExactInterval.Down(ExactInterval.Down(bx.Min.z - expand) - protectedOrigin.z) : ExactInterval.Down(bx.Min.z - protectedOrigin.z);
            double hx = expand > 0 ? ExactInterval.Up(ExactInterval.Up(bx.Max.x + expand) - protectedOrigin.x) : ExactInterval.Up(bx.Max.x - protectedOrigin.x);
            double hy = expand > 0 ? ExactInterval.Up(ExactInterval.Up(bx.Max.y + expand) - protectedOrigin.y) : ExactInterval.Up(bx.Max.y - protectedOrigin.y);
            double hz = expand > 0 ? ExactInterval.Up(ExactInterval.Up(bx.Max.z + expand) - protectedOrigin.z) : ExactInterval.Up(bx.Max.z - protectedOrigin.z);
            Vector3i lo = new Vector3i(cell_lo(lx), cell_lo(ly), cell_lo(lz));
            Vector3i hi = new Vector3i(cell_hi(hx), cell_hi(hy), cell_hi(hz));
            for (int k = lo.z; k <= hi.z; ++k)
                for (int j = lo.y; j <= hi.y; ++j)
                    for (int i = lo.x; i <= hi.x; ++i)
                        yield return new Vector3i(i, j, k);
        }

        int cell_lo(double d)
        {
            return ClampProtectedCell(Math.Floor(ExactInterval.Down(d / protectedCell)), -1);
        }

        int cell_hi(double d)
        {
            return ClampProtectedCell(Math.Floor(ExactInterval.Up(d / protectedCell)), 66);
        }

        static int ClampProtectedCell(double f, int fallback)
        {
            if (double.IsNaN(f)) return fallback;
            if (f <= -1) return -1;
            if (f >= 66) return 66;
            return (int)f;
        }

        static bool SegmentTriangleWithin(Segment3d sg, Triangle3d t, double r)
        {
            if (!(r >= 0)) return false;
            Vector3d p0 = sg.P0, p1 = sg.P1;
            if (!p0.IsFinite || !p1.IsFinite || !t.V0.IsFinite || !t.V1.IsFinite || !t.V2.IsFinite)
                return false;
            if (double.IsInfinity(r)) return true;
            Vector3d sp0 = p0 - t.V0, sp1 = p1 - t.V0, e1 = t.V1 - t.V0, e2 = t.V2 - t.V0;
            double maxShift = Math.Max(Math.Max(sp0.MaxAbs, sp1.MaxAbs), Math.Max(e1.MaxAbs, e2.MaxAbs));
            if (r >= FastScaleMin && r <= FastScaleMax && maxShift <= FastScaleMax && (maxShift == 0 || maxShift >= FastScaleMin)) {
                double r2 = r * r;
                return new DistSegment3Triangle3(sg, t).GetSquared() <= r2;
            }
            int pre = 0;
            if (!sp0.IsFinite || !sp1.IsFinite || !e1.IsFinite || !e2.IsFinite) {
                sp0 = 0.5 * p0 - 0.5 * t.V0;
                sp1 = 0.5 * p1 - 0.5 * t.V0;
                e1 = 0.5 * t.V1 - 0.5 * t.V0;
                e2 = 0.5 * t.V2 - 0.5 * t.V0;
                pre = 1;
            }
            maxShift = Math.Max(Math.Max(sp0.MaxAbs, sp1.MaxAbs), Math.Max(e1.MaxAbs, e2.MaxAbs));
            if (!(maxShift > 0)) return true;
            int exp = Pow2Exp(maxShift);
            Vector3d q0 = ScaleVec(sp0, -exp), q1 = ScaleVec(sp1, -exp);
            Vector3d v1 = ScaleVec(e1, -exp), v2 = ScaleVec(e2, -exp);
            double sr = ExactPoint3.ScaleB(r, -(exp + pre));
            Segment3d ss = SafeSegment(q0, q1);
            Triangle3d st = new Triangle3d(Vector3d.Zero, v1, v2);
            double d2 = new DistSegment3Triangle3(ss, st).GetSquared();
            return Math.Sqrt(Math.Max(0, d2)) <= sr;
        }

        // is triangle tid of Mesh within ProtectedDistance of a protected segment?
        bool is_protected(int tid)
        {
            if (protectedCells == null) return false;
            Index3i tv = Mesh.GetTriangle(tid);
            Triangle3d t = new Triangle3d(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c));
            if (!t.V0.IsFinite || !t.V1.IsFinite || !t.V2.IsFinite) return false;
            var bx = new AxisAlignedBox3d(t.V0); bx.Contain(t.V1); bx.Contain(t.V2);
            var seen = new HashSet<int>();
            foreach (Vector3i c in cells(bx, ProtectedDistance)) {
                List<int> l;
                if (protectedCells.TryGetValue(c, out l) == false) continue;
                foreach (int i in l) {
                    if (seen.Add(i) == false) continue;
                    if (SegmentTriangleWithin(ProtectedEdges[i], t, ProtectedDistance)) return true;
                }
            }
            return false;
        }

        TriangleHashGrid build_grid()
        {
            double sum = 0; int n = 0;
            foreach (int eid in Mesh.EdgeIndices()) {
                Index2i ev = Mesh.GetEdgeV(eid);
                Vector3d d = Mesh.GetVertex(ev.a) - Mesh.GetVertex(ev.b);
                sum += d.IsFinite ? SafeLength(d) : 2.0 * SafeLength(0.5 * Mesh.GetVertex(ev.a) - 0.5 * Mesh.GetVertex(ev.b));
                n++;
            }
            var g = new TriangleHashGrid(Mesh.CachedBounds.Min, Math.Max(1.5 * sum / Math.Max(1, n), MinNormal));
            foreach (int tid in Mesh.TriangleIndices())
                g.Insert(tid, box(Mesh.GetVertex(Mesh.GetTriangle(tid).a), Mesh.GetVertex(Mesh.GetTriangle(tid).b), Mesh.GetVertex(Mesh.GetTriangle(tid).c)));
            return g;
        }

        static AxisAlignedBox3d box(Vector3d a, Vector3d b, Vector3d c)
        {
            var bx = new AxisAlignedBox3d(a); bx.Contain(b); bx.Contain(c);
            return bx;
        }

        static double min_angle(Vector3d a, Vector3d b, Vector3d c)
        {
            double A = angle(b - a, c - a), B = angle(a - b, c - b), C = angle(a - c, b - c);
            return Math.Min(A, Math.Min(B, C));
        }

        // angle between u and v in degrees (0 for a zero vector)
        static double angle(Vector3d u, Vector3d v)
        {
            double su = u.MaxAbs, sv = v.MaxAbs;
            if (su >= FastScaleMin && su <= FastScaleMax && sv >= FastScaleMin && sv <= FastScaleMax) {
                double lu = u.Length, lv = v.Length;
                if (!(lu > 0 && lv > 0)) return 0;
                return Math.Acos(MathUtil.Clamp(u.Dot(v) / (lu * lv), -1, 1)) * MathUtil.Rad2Deg;
            }
            if (!(su > 0 && sv > 0) || !u.IsFinite || !v.IsFinite) return 0;
            Vector3d us = ScaleVec(u, -Pow2Exp(su));
            Vector3d vs = ScaleVec(v, -Pow2Exp(sv));
            double lus = us.Length, lvs = vs.Length;
            if (!(lus > 0 && lvs > 0)) return 0;
            return Math.Acos(MathUtil.Clamp(us.Dot(vs) / (lus * lvs), -1, 1)) * MathUtil.Rad2Deg;
        }

        double min_angle(int tid)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return min_angle(Mesh.GetVertex(t.a), Mesh.GetVertex(t.b), Mesh.GetVertex(t.c));
        }

        // the unit normal of the triangle, or false if it is degenerate (relative to its size)
        static bool unit_normal(Vector3d a, Vector3d b, Vector3d c, out Vector3d n)
        {
            Vector3d u = b - a, v = c - a, w = c - b;
            double s = Math.Max(u.MaxAbs, Math.Max(v.MaxAbs, w.MaxAbs));
            if (s >= FastScaleMin && s <= FastScaleMax) {
                n = u.Cross(v);
                double l = Math.Max(u.LengthSquared, Math.Max(w.LengthSquared, v.LengthSquared));
                double len = n.Length;
                if (!(len > 1e-12 * l)) return false;
                n /= len;
                return true;
            }
            n = Vector3d.Zero;
            if (!a.IsFinite || !b.IsFinite || !c.IsFinite) return false;
            if (!u.IsFinite || !v.IsFinite || !w.IsFinite) {
                u = 0.5 * b - 0.5 * a;
                v = 0.5 * c - 0.5 * a;
                w = 0.5 * c - 0.5 * b;
            }
            s = Math.Max(u.MaxAbs, Math.Max(v.MaxAbs, w.MaxAbs));
            if (!(s > 0)) return false;
            int exp = Pow2Exp(s);
            Vector3d us = ScaleVec(u, -exp), vs = ScaleVec(v, -exp), ws = ScaleVec(w, -exp);
            n = us.Cross(vs);
            double ls = Math.Max(us.LengthSquared, Math.Max(ws.LengthSquared, vs.LengthSquared));
            double lens = SafeLength(n);
            if (!(lens > 1e-12 * ls)) return false;
            n /= lens;
            return true;
        }

        // new triangles (vertex ids, positions) replacing the triangles `removed`: within the tolerance and, if
        // checked, free of intersections with each other and with the rest of the mesh
        // area-weighted mean angle (degrees) between the triangles' normals and the reference's normal near them
        double normal_deviation(IEnumerable<Triangle3d> tris)
        {
            double sum = 0, area = 0;
            foreach (Triangle3d t in tris) {
                Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
                double a2 = n.Length;
                if (!(a2 > 0)) continue;
                Vector3d nu = n / a2;
                if (NormalSamples <= 1) {
                    int r = referenceTree.FindNearestTriangle((t.V0 + t.V1 + t.V2) / 3);
                    sum += a2 * Vector3d.AngleD(nu, Reference.GetTriNormal(r));
                } else {
                    // corners, edge midpoints and centroid, equally weighted
                    Vector3d c = (t.V0 + t.V1 + t.V2) / 3;
                    Vector3d[] pts = { t.V0, t.V1, t.V2, 0.5 * (t.V0 + t.V1), 0.5 * (t.V1 + t.V2), 0.5 * (t.V2 + t.V0), c };
                    double dsum = 0;
                    foreach (Vector3d q in pts)
                        dsum += Vector3d.AngleD(nu, Reference.GetTriNormal(referenceTree.FindNearestTriangle(q)));
                    sum += a2 * dsum / pts.Length;
                }
                area += a2;
            }
            return area > 0 ? sum / area : 0;
        }

        // KeepNormalDeviation: the new triangles deviate from the reference's normals no more than the removed ones
        bool skipNormals;

        bool keeps_normals(List<Triangle3d> tris, HashSet<int> removed)
        {
            if (KeepNormalDeviation == false || skipNormals) return true;
            var old = new List<Triangle3d>();
            foreach (int t in removed) {
                Index3i tv = Mesh.GetTriangle(t);
                old.Add(new Triangle3d(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c)));
            }
            return normal_deviation(tris) <= normal_deviation(old) + NormalDeviationSlackDegrees;
        }

        /// <summary> with KeepClearOf: also no operation whose swept region meets it (issue 743; false: the end state only) </summary>
        internal bool KeepClearOfSwept = true;

        // the region between the old triangles (removed) and the new ones (tris; every operation keeps the patch's boundary):
        // the chain old - new is closed, so it is the boundary of the cones of both from one apex, cone(old) - cone(new); a
        // point whose winding number the operation changes lies in one of those tetrahedra (Reducer.TetrahedraMeet). The apex
        // is a corner of every new triangle where there is one (the moved, split or collapsed vertex, the flip's c), so the
        // new triangles' cones are flat and left out
        readonly List<Vector3d> sweptCorners = new List<Vector3d>();
        bool sweeps_obstacle(List<Triangle3d> tris, HashSet<int> removed)
        {
            if (tris.Count == 0) return false;
            Vector3d apex = tris[0].V0;
            foreach (Vector3d cand in new[] { tris[0].V0, tris[0].V1, tris[0].V2 }) {
                bool all = true;
                foreach (Triangle3d t in tris)
                    if (t.V0 != cand && t.V1 != cand && t.V2 != cand) { all = false; break; }
                if (all) { apex = cand; break; }
            }
            sweptCorners.Clear();
            Vector3d va = Vector3d.Zero, vb = Vector3d.Zero, vc = Vector3d.Zero;
            foreach (int t in removed) {
                Mesh.GetTriVertices(t, ref va, ref vb, ref vc);
                if (va == apex || vb == apex || vc == apex) continue;
                sweptCorners.Add(va); sweptCorners.Add(vb); sweptCorners.Add(vc); sweptCorners.Add(apex);
            }
            foreach (Triangle3d t in tris) {
                if (t.V0 == apex || t.V1 == apex || t.V2 == apex) continue;
                sweptCorners.Add(t.V0); sweptCorners.Add(t.V1); sweptCorners.Add(t.V2); sweptCorners.Add(apex);
            }
            return Reducer.TetrahedraMeet(KeepClearOf, sweptCorners);
        }

        internal bool accept(List<Index3i> ids, List<Triangle3d> tris, HashSet<int> removed)
        {
            if (protectedCells != null && protectThisOperation) {
                foreach (int t in removed)
                    if (is_protected(t)) {
                        RejectedByProtection++;
                        return false;
                    }
            }
            if (keeps_normals(tris, removed) == false) {
                RejectedByNormals++;
                return false;
            }
            foreach (Triangle3d t in tris)
                if (!bounds.IsWithin(referenceTree, t.V0, t.V1, t.V2, Tolerance)) {
                    RejectedByDeviation++;
                    return false;
                }
            if (KeepClearOf != null && (Reducer.TrianglesTouch(KeepClearOf, tris) || (KeepClearOfSwept && sweeps_obstacle(tris, removed)))) {
                RejectedByIntersection++;
                return false;
            }
            if (grid == null)
                return true;
            for (int i = 0; i < tris.Count; ++i) {
                Triangle3d A = tris[i];
                Index3i ia = ids[i];
                for (int j = i + 1; j < tris.Count; ++j) {
                    Triangle3d B = tris[j];
                    if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ids[j], ref B)) {
                        RejectedByIntersection++;
                        return false;
                    }
                }
                bool hit = false;
                grid.Query(box(A.V0, A.V1, A.V2), (tc) => {
                    if (hit || removed.Contains(tc) || Mesh.IsTriangle(tc) == false) return;
                    Index3i ic = Mesh.GetTriangle(tc);
                    Triangle3d C = new Triangle3d(Mesh.GetVertex(ic.a), Mesh.GetVertex(ic.b), Mesh.GetVertex(ic.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref C)) return;
                    if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ic, ref C))
                        hit = true;
                });
                if (hit) {
                    RejectedByIntersection++;
                    return false;
                }
            }
            return true;
        }

        /// <summary> (for tests) the self-intersection grid holds exactly the mesh's triangles, each with its current box </summary>
        internal bool GridMatchesMesh()
        {
            if (grid == null) return true;
            if (grid.Count != Mesh.TriangleCount) return false;
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tv = Mesh.GetTriangle(t);
                AxisAlignedBox3d stored;
                if (!grid.TryGetBox(t, out stored)) return false;
                AxisAlignedBox3d cur = box(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c));
                if (stored.Min != cur.Min || stored.Max != cur.Max) return false;
            }
            return true;
        }

        // rejections that depend on the operation's own triangles only (normals, deviation, protection; not the
        // self-intersections, which depend on the rest of the mesh): the operation is not evaluated again while none
        // of the vertices of its triangles changed. Bit-identical to evaluating it again (the same inputs give the
        // same rejection); it saves the repeated evaluations of the passes after the first
        internal const int FlipOp = 0, MoveOp = 1, SplitOp = 2;
        readonly Dictionary<int, int>[] rejectedAt = new Dictionary<int, int>[3];
        int[] stamps = new int[0];
        int clock;
        /// <summary> (for tests) evaluations of accept skipped as known rejections </summary>
        internal int SkippedRejections;
        /// <summary> (for tests) false: every operation is evaluated again (the same result, slower) </summary>
        internal bool MemoizeRejections = true;

        int stamp(int v)
        {
            return v < stamps.Length ? stamps[v] : 0;
        }

        void set_stamp(int v)
        {
            if (v >= stamps.Length) Array.Resize(ref stamps, Math.Max(2 * stamps.Length, v + 1));
            stamps[v] = clock;
        }

        bool unchanged_since(int t, int at)
        {
            Index3i tv = Mesh.GetTriangle(t);
            return stamp(tv.a) <= at && stamp(tv.b) <= at && stamp(tv.c) <= at;
        }

        // accept, skipped (false) for an operation (kind, key) rejected before on the same triangles
        internal bool accept(int kind, int key, List<Index3i> ids, List<Triangle3d> tris, HashSet<int> removed)
        {
            if (MemoizeRejections == false)
                return accept(ids, tris, removed);
            int at;
            if (rejectedAt[kind].TryGetValue(key, out at)) {
                bool same = true;
                foreach (int t in removed)
                    if (!unchanged_since(t, at)) { same = false; break; }
                if (same) {
                    SkippedRejections++;
                    return false;
                }
                rejectedAt[kind].Remove(key);
            }
            int intersections = RejectedByIntersection;
            bool ok = accept(ids, tris, removed);
            if (!ok && RejectedByIntersection == intersections)
                rejectedAt[kind][key] = clock;
            return ok;
        }

        internal void regrid(IEnumerable<int> removed, IEnumerable<int> added)
        {
            // (every operation ends here: a new time for the vertices of its new triangles)
            ++clock;
            foreach (int t in added) {
                Index3i tv = Mesh.GetTriangle(t);
                set_stamp(tv.a); set_stamp(tv.b); set_stamp(tv.c);
            }
            if (grid == null) return;
            foreach (int t in removed) grid.Remove(t);
            foreach (int t in added) {
                Index3i tv = Mesh.GetTriangle(t);
                grid.Insert(t, box(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c)));
            }
        }

        bool flip_pass()
        {
            var ids = new List<Index3i>(2);
            var tris = new List<Triangle3d>(2);
            var removed = new HashSet<int>();
            foreach (int eid in visit_order(Mesh.MaxEdgeID, true)) {
                if (CancelF()) return false;
                if (Mesh.IsEdge(eid) == false || Mesh.IsBoundaryEdge(eid)) continue;
                Index2i et = Mesh.GetEdgeT(eid);
                double cur = Math.Min(min_angle(et.a), min_angle(et.b));
                if (!(cur < TargetAngleDegrees)) continue;
                Index2i ev = Mesh.GetEdgeV(eid);
                Index3i ta = Mesh.GetTriangle(et.a);
                // (a, b) in the order of triangle et.a
                if ((ta.a == ev.b && ta.b == ev.a) || (ta.b == ev.b && ta.c == ev.a) || (ta.c == ev.b && ta.a == ev.a))
                    ev = new Index2i(ev.b, ev.a);
                Index2i ov = Mesh.GetEdgeOpposingV(eid);
                if (Mesh.FindEdge(ov.a, ov.b) != DMesh3.InvalidID) continue;
                // the triangle et.a is (a, b, c), with c = its opposite vertex; et.b has the opposite vertex d
                int a = ev.a, b = ev.b;
                int c = opposite(et.a, a, b), d = opposite(et.b, a, b);
                Vector3d pa = Mesh.GetVertex(a), pb = Mesh.GetVertex(b), pc = Mesh.GetVertex(c), pd = Mesh.GetVertex(d);
                // after the flip: (c, a, d) and (d, b, c), the same orientation
                double nwMin = Math.Min(min_angle(pc, pa, pd), min_angle(pd, pb, pc));
                double curMax = Math.Max(max_angle(pa, pb, pc), max_angle(pb, pa, pd));
                bool capFlip = curMax > CapAngleDegrees && Math.Max(max_angle(pc, pa, pd), max_angle(pd, pb, pc)) <= curMax - 5 && nwMin >= cur;
                if (!(nwMin >= cur + MinGainDegrees) && !capFlip) continue;
                Vector3d n0, n1, m0, m1;
                if (!unit_normal(pa, pb, pc, out n0) || !unit_normal(pb, pa, pd, out n1)) continue;
                if (!unit_normal(pc, pa, pd, out m0) || !unit_normal(pd, pb, pc, out m1)) continue;
                if (m0.Dot(n0) < cosTurn || m0.Dot(n1) < cosTurn || m1.Dot(n0) < cosTurn || m1.Dot(n1) < cosTurn) continue;
                if (n0.Dot(n1) < Math.Cos(MaxFlipDihedralDegrees * MathUtil.Deg2Rad)) continue;
                ids.Clear(); tris.Clear(); removed.Clear();
                ids.Add(new Index3i(c, a, d)); tris.Add(new Triangle3d(pc, pa, pd));
                ids.Add(new Index3i(d, b, c)); tris.Add(new Triangle3d(pd, pb, pc));
                removed.Add(et.a); removed.Add(et.b);
                // (a cap flip on a nearly flat pair is not held to the normal deviation)
                skipNormals = capFlip && n0.Dot(n1) >= Math.Cos(CapFlipMaxDihedralDegrees * MathUtil.Deg2Rad);
                bool ok = accept(FlipOp, eid, ids, tris, removed);
                skipNormals = false;
                protectThisOperation = true;
                if (ok == false) continue;
                DMesh3.EdgeFlipInfo info;
                if (Mesh.FlipEdge(eid, out info) != MeshResult.Ok) continue;
                regrid(new[] { info.t0, info.t1 }, new[] { info.t0, info.t1 });
                Flips++;
            }
            return true;
        }

        Vector3d closest_on_reference(Vector3d p)
        {
            int t = referenceTree.FindNearestTriangle(p);
            return MeshQueries.TriangleDistance(Reference, t, p).TriangleClosest;
        }

        // the edges (or vertices) of this pass: by id, or with WorstFirst by the smallest angle of their triangles
        // (ties by id; deterministic)
        IEnumerable<int> visit_order(int maxId, bool edges)
        {
            if (WorstFirst == false) {
                for (int id = 0; id < maxId; ++id) yield return id;
                yield break;
            }
            var keyed = new List<KeyValuePair<double, int>>();
            for (int id = 0; id < maxId; ++id) {
                double a = double.MaxValue;
                if (edges) {
                    if (Mesh.IsEdge(id) == false) continue;
                    Index2i et = Mesh.GetEdgeT(id);
                    a = min_angle(et.a);
                    if (et.b >= 0) a = Math.Min(a, min_angle(et.b));
                } else {
                    if (Mesh.IsVertex(id) == false) continue;
                    foreach (int t in Mesh.VtxTrianglesItr(id)) a = Math.Min(a, min_angle(t));
                }
                if (a < TargetAngleDegrees) keyed.Add(new KeyValuePair<double, int>(a, id));
            }
            keyed.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
            foreach (var kv in keyed) yield return kv.Value;
        }

        // is v on an edge whose two triangles meet at more than PinCreaseVerticesDegrees?
        bool on_crease(int v)
        {
            double cos = Math.Cos(PinCreaseVerticesDegrees * MathUtil.Deg2Rad);
            foreach (int e in Mesh.VtxEdgesItr(v)) {
                Index2i et = Mesh.GetEdgeT(e);
                if (et.b < 0) continue;
                if (tri_normal(Mesh, et.a).Dot(tri_normal(Mesh, et.b)) < cos) return true;
            }
            return false;
        }

        static double max_angle(Vector3d a, Vector3d b, Vector3d c)
        {
            return Math.Max(angle(b - a, c - a), Math.Max(angle(a - b, c - b), angle(a - c, b - c)));
        }

        // caps (largest angle above SplitCapsAboveDegrees), largest angle first: their longest edge split at the foot
        // of the altitude from the opposite vertex (the split stays on the surface: no change of position or normal;
        // checked anyway, as every operation)
        bool split_pass()
        {
            var caps = new List<KeyValuePair<double, int>>();
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tv = Mesh.GetTriangle(t);
                double m = max_angle(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c));
                if (m > SplitCapsAboveDegrees) caps.Add(new KeyValuePair<double, int>(m, t));
            }
            caps.Sort((x, y) => x.Key != y.Key ? -x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
            var ids = new List<Index3i>(4);
            var tris = new List<Triangle3d>(4);
            var removed = new HashSet<int>();
            foreach (var kv in caps) {
                if (CancelF()) return false;
                if (Mesh.TriangleCount + 2 > maxTriangles) break;
                int t = kv.Value;
                if (Mesh.IsTriangle(t) == false) continue;
                Index3i tv = Mesh.GetTriangle(t);
                Vector3d[] p = { Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c) };
                if (!(max_angle(p[0], p[1], p[2]) > SplitCapsAboveDegrees)) continue;   // changed by an earlier split
                // the longest edge (k, k+1), opposite vertex k+2
                int k = 0;
                for (int j = 1; j < 3; ++j)
                    if (p[(j + 1) % 3].DistanceSquared(p[j]) > p[(k + 1) % 3].DistanceSquared(p[k])) k = j;
                int va = tv[k], vb = tv[(k + 1) % 3];
                int eid = Mesh.FindEdge(va, vb);
                if (eid == DMesh3.InvalidID || Mesh.IsBoundaryEdge(eid)) continue;
                Vector3d a = p[k], b = p[(k + 1) % 3], c = p[(k + 2) % 3];
                Vector3d ab = b - a;
                double l2 = ab.LengthSquared;
                if (!(l2 > 0)) continue;
                double s = MathUtil.Clamp((c - a).Dot(ab) / l2, 0.05, 0.95);
                Vector3d m = a + s * ab;
                // the four new triangles, in the orientation of the old ones
                Index2i et = Mesh.GetEdgeT(eid);
                int other = et.a == t ? et.b : et.a;
                int d = opposite(other, va, vb);
                int vc = tv[(k + 2) % 3];
                Vector3d pd = Mesh.GetVertex(d);
                ids.Clear(); tris.Clear(); removed.Clear();
                // t = (va, vb, vc) in its order; other = (vb, va, d)
                const int NewId = int.MaxValue;
                ids.Add(new Index3i(va, NewId, vc)); tris.Add(new Triangle3d(a, m, c));
                ids.Add(new Index3i(NewId, vb, vc)); tris.Add(new Triangle3d(m, b, c));
                ids.Add(new Index3i(vb, NewId, d)); tris.Add(new Triangle3d(b, m, pd));
                ids.Add(new Index3i(NewId, va, d)); tris.Add(new Triangle3d(m, a, pd));
                // valid, and no new extreme: no new triangle with a smaller smallest or a larger largest angle than the
                // two old ones had (a split near a corner would make a new needle or cap)
                double oldMin = Math.Min(min_angle(a, b, c), min_angle(b, a, pd)), oldMax = Math.Max(max_angle(a, b, c), max_angle(b, a, pd));
                bool valid = true;
                foreach (Triangle3d nt in tris) {
                    Vector3d n;
                    if (!unit_normal(nt.V0, nt.V1, nt.V2, out n) || min_angle(nt.V0, nt.V1, nt.V2) < oldMin || !(max_angle(nt.V0, nt.V1, nt.V2) < oldMax)) {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;
                removed.Add(t); removed.Add(other);
                protectThisOperation = !ProtectFlipsOnly;
                bool accepted = accept(SplitOp, t, ids, tris, removed);
                protectThisOperation = true;
                if (accepted == false) continue;
                DMesh3.EdgeSplitInfo info;
                if (Mesh.SplitEdge(eid, out info) != MeshResult.Ok) continue;
                Mesh.SetVertex(info.vNew, m);
                var changed = new List<int>();
                foreach (int nt in Mesh.VtxTrianglesItr(info.vNew)) changed.Add(nt);
                regrid(new[] { t, other }, changed);
                Splits++;
            }
            return true;
        }

        // the triangles below TargetAngleDegrees: by id, or with WorstFirst by increasing smallest angle (ties by id)
        List<int> needle_order()
        {
            var keyed = new List<KeyValuePair<double, int>>();
            foreach (int t in Mesh.TriangleIndices()) {
                double a = min_angle(t);
                if (a < TargetAngleDegrees) keyed.Add(new KeyValuePair<double, int>(WorstFirst ? a : 0, t));
            }
            keyed.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
            var order = new List<int>(keyed.Count);
            foreach (var kv in keyed) order.Add(kv.Value);
            return order;
        }

        // needles: their shortest edge (a, b) collapsed into a, placed at the candidate (midpoint, a or b; a pinned
        // crease vertex keeps its position) that gives the largest smallest angle and passes accept
        bool collapse_pass()
        {
            var ids = new List<Index3i>();
            var tris = new List<Triangle3d>();
            var removed = new HashSet<int>();
            var candidates = new List<KeyValuePair<double, Vector3d>>(3);
            foreach (int t in needle_order()) {
                if (CancelF()) return false;
                if (Mesh.IsTriangle(t) == false) continue;
                Index3i tv = Mesh.GetTriangle(t);
                Vector3d[] p = { Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c) };
                if (!(min_angle(p[0], p[1], p[2]) < TargetAngleDegrees)) continue;   // changed by an earlier operation
                int k = 0;
                for (int j = 1; j < 3; ++j)
                    if (p[(j + 1) % 3].DistanceSquared(p[j]) < p[(k + 1) % 3].DistanceSquared(p[k])) k = j;
                int a = tv[k], b = tv[(k + 1) % 3];
                int eid = Mesh.FindEdge(a, b);
                if (eid == DMesh3.InvalidID || Mesh.IsBoundaryEdge(eid) || Mesh.IsBoundaryVertex(a) || Mesh.IsBoundaryVertex(b)) continue;
                // link condition: a and b share exactly the two opposite vertices
                int common = 0;
                foreach (int x in Mesh.VtxVerticesItr(a))
                    if (x != b && Mesh.FindEdge(x, b) != DMesh3.InvalidID) common++;
                if (common != 2) continue;
                bool pinA = PinCreaseVerticesDegrees < 180 && on_crease(a), pinB = PinCreaseVerticesDegrees < 180 && on_crease(b);
                if (pinA && pinB) continue;
                Vector3d pa = Mesh.GetVertex(a), pb = Mesh.GetVertex(b);
                Index2i et = Mesh.GetEdgeT(eid);
                removed.Clear();
                foreach (int r in Mesh.VtxTrianglesItr(a)) removed.Add(r);
                foreach (int r in Mesh.VtxTrianglesItr(b)) removed.Add(r);
                double cur = double.MaxValue;
                foreach (int r in removed) cur = Math.Min(cur, min_angle(r));
                candidates.Clear();
                foreach (Vector3d q in pinA ? new[] { pa } : pinB ? new[] { pb } : new[] { 0.5 * (pa + pb), pa, pb }) {
                    double nw = double.MaxValue;
                    bool ok = true;
                    foreach (int r in removed) {
                        if (r == et.a || r == et.b) continue;
                        Index3i rv = Mesh.GetTriangle(r);
                        Vector3d A = Mesh.GetVertex(rv.a), B = Mesh.GetVertex(rv.b), C = Mesh.GetVertex(rv.c);
                        Vector3d n0, n1;
                        if (!unit_normal(A, B, C, out n0)) { ok = false; break; }
                        if (rv.a == a || rv.a == b) A = q;
                        if (rv.b == a || rv.b == b) B = q;
                        if (rv.c == a || rv.c == b) C = q;
                        if (!unit_normal(A, B, C, out n1) || n1.Dot(n0) < cosTurn) { ok = false; break; }
                        nw = Math.Min(nw, min_angle(A, B, C));
                    }
                    if (ok && nw >= cur + MinGainDegrees) candidates.Add(new KeyValuePair<double, Vector3d>(nw, q));
                }
                // best first; stable for equal angles (the candidate order above)
                for (int i = 1; i < candidates.Count; ++i)
                    for (int j = i; j > 0 && candidates[j].Key > candidates[j - 1].Key; --j) {
                        var tmp = candidates[j]; candidates[j] = candidates[j - 1]; candidates[j - 1] = tmp;
                    }
                foreach (var cand in candidates) {
                    Vector3d q = cand.Value;
                    ids.Clear(); tris.Clear();
                    foreach (int r in removed) {
                        if (r == et.a || r == et.b) continue;
                        Index3i rv = Mesh.GetTriangle(r);
                        Vector3d A = Mesh.GetVertex(rv.a), B = Mesh.GetVertex(rv.b), C = Mesh.GetVertex(rv.c);
                        if (rv.a == a || rv.a == b) { A = q; rv.a = a; }
                        if (rv.b == a || rv.b == b) { B = q; rv.b = a; }
                        if (rv.c == a || rv.c == b) { C = q; rv.c = a; }
                        ids.Add(rv); tris.Add(new Triangle3d(A, B, C));
                    }
                    protectThisOperation = !ProtectFlipsOnly;
                    bool accepted = accept(ids, tris, removed);   // (several candidates per needle: not memoized)
                    protectThisOperation = true;
                    if (accepted == false) continue;
                    DMesh3.EdgeCollapseInfo info;
                    if (Mesh.CollapseEdge(a, b, out info) != MeshResult.Ok) break;
                    Mesh.SetVertex(a, q);
                    var changed = new List<int>();
                    foreach (int nt in Mesh.VtxTrianglesItr(a)) changed.Add(nt);
                    regrid(removed, changed);
                    Collapses++;
                    break;
                }
            }
            return true;
        }

        int opposite(int tid, int a, int b)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return (t.a != a && t.a != b) ? t.a : (t.b != a && t.b != b) ? t.b : t.c;
        }

        bool move_pass()
        {
            var ring = new List<int>();
            var ids = new List<Index3i>();
            var tris = new List<Triangle3d>();
            var removed = new HashSet<int>();
            foreach (int v in visit_order(Mesh.MaxVertexID, false)) {
                if (CancelF()) return false;
                if (Mesh.IsVertex(v) == false || Mesh.IsBoundaryVertex(v)) continue;
                if (PinCreaseVerticesDegrees < 180 && on_crease(v)) continue;
                ring.Clear();
                foreach (int t in Mesh.VtxTrianglesItr(v)) ring.Add(t);
                if (ring.Count < 3) continue;
                double cur = double.MaxValue;
                foreach (int t in ring) cur = Math.Min(cur, min_angle(t));
                if (!(cur < TargetAngleDegrees)) continue;
                // target: the centroid of the one ring's vertices, moved into the tangent plane at v
                Vector3d p = Mesh.GetVertex(v), cen = Vector3d.Zero, nrm = Vector3d.Zero;
                int k = 0;
                foreach (int u in Mesh.VtxVerticesItr(v)) { cen += Mesh.GetVertex(u); k++; }
                cen /= k;
                foreach (int t in ring) {
                    Index3i tv = Mesh.GetTriangle(t);
                    nrm += (Mesh.GetVertex(tv.b) - Mesh.GetVertex(tv.a)).Cross(Mesh.GetVertex(tv.c) - Mesh.GetVertex(tv.a));
                }
                if (!(nrm.Length > 0)) continue;
                nrm.Normalize();
                Vector3d q = cen - (cen - p).Dot(nrm) * nrm;
                if (FollowReference) {
                    // the closest point of the reference to q, plus the vertex's own offset from the reference
                    Vector3d cp = closest_on_reference(p), cq = closest_on_reference(q);
                    q = cq + (p - cp);
                }
                if (q == p) continue;
                double nw = double.MaxValue;
                bool ok = true;
                ids.Clear(); tris.Clear(); removed.Clear();
                foreach (int t in ring) {
                    Index3i tv = Mesh.GetTriangle(t);
                    Vector3d A = Mesh.GetVertex(tv.a), B = Mesh.GetVertex(tv.b), C = Mesh.GetVertex(tv.c);
                    Vector3d n0, n1;
                    if (!unit_normal(A, B, C, out n0)) { ok = false; break; }
                    if (tv.a == v) A = q; else if (tv.b == v) B = q; else C = q;
                    if (!unit_normal(A, B, C, out n1) || n1.Dot(n0) < cosTurn) { ok = false; break; }
                    nw = Math.Min(nw, min_angle(A, B, C));
                    ids.Add(tv); tris.Add(new Triangle3d(A, B, C)); removed.Add(t);
                }
                if (!ok || !(nw >= cur + MinGainDegrees)) continue;
                protectThisOperation = !ProtectFlipsOnly;
                bool accepted = accept(MoveOp, v, ids, tris, removed);
                protectThisOperation = true;
                if (accepted == false) continue;
                Mesh.SetVertex(v, q);
                regrid(ring, ring);
                Moves++;
            }
            return true;
        }
    }
}
