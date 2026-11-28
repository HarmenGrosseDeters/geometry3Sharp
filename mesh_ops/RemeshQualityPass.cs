// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary> outcome of RemeshQualityPass.Compute </summary>
    public enum RemeshQualityStatus
    {
        /// <summary> at least one operation was made </summary>
        Improved,
        /// <summary> no operation passed the guards (the mesh is unchanged) </summary>
        Unchanged,
        /// <summary> CancelF returned true: the mesh is partly improved, every guard still holds for what was done </summary>
        Cancelled,
        /// <summary> MaxDeviation not positive and finite, an empty mesh or reference, or a non-finite vertex: nothing was done </summary>
        InvalidInput
    }

    /// <summary> result of RemeshQualityPass.Compute (angles in degrees) </summary>
    public class RemeshQualityResult
    {
        public RemeshQualityStatus Status;
        /// <summary>
        /// the smallest angle of the mesh (from atan2, independent of the corner order: the measure the pass keeps from
        /// dropping), and the smallest angle at the 1 % rank (TriangleQualityStatistics), before and after
        /// </summary>
        public double MinAngleBefore, MinAngleAfter, Worst1PercentBefore, Worst1PercentAfter;
        /// <summary> operations made: edge flips, needle collapses, splits of non-crease edges, vertex relocations (crease vertices along their crease included) </summary>
        public int Flips, Collapses, Splits, Relocations;
        /// <summary> the crease operations of Wang et al. 2019: crease edges split (a vertex inserted on the crease), crease vertices removed (collapsed along the crease) </summary>
        public int CreaseInsertions, CreaseRemovals;
        /// <summary>
        /// candidate operations refused: by the band (a new triangle not certified within MaxDeviation of the reference, a
        /// new crease edge not within it of the reference's creases, or with TwoSided a reference triangle no longer within
        /// it of the mesh), by the exact self-intersection test, and by the angle rule (no gain of the smallest angle, a
        /// degenerate or turned triangle)
        /// </summary>
        public int RejectedByBand, RejectedByIntersection, RejectedByAngle;
        /// <summary>
        /// MaxDeviation: every triangle the pass made is certified within it of the reference, every crease edge it made
        /// within it of the reference's creases, and with TwoSided every reference triangle that was within it of the mesh at
        /// the start still is (floating point, as Reducer.MaxDeviation)
        /// </summary>
        public double CertifiedBand;
        /// <summary> the start mesh was within CertifiedBand (every triangle certified; with TwoSided, every reference triangle covered) </summary>
        public bool StartWithinBand;
        /// <summary> the result is within CertifiedBand (every triangle certified; with TwoSided, every reference triangle covered) </summary>
        public bool ResultWithinBand;

        // (tests, benchmarks) details
        internal int TrianglesBefore, TrianglesAfter, Passes, CreaseRelocations, RejectedByCrease, RejectedByCoverage;
        /// <summary> triangles of the start / result not certified within the band (the result's: unchanged triangles outside it at the start) </summary>
        internal int StartUncertifiedTriangles, UncertifiedTriangles;
        /// <summary> TwoSided: reference triangles not certified within the band of the mesh at the start (the pass keeps no promise for them) </summary>
        internal int UncoveredReferenceTriangles;
    }

    /// <summary>
    /// A worst-first triangle quality post-pass for a remeshed surface, after Hu, Zint, Pang, Lin, Chen and Wang,
    /// "Error-bounded and feature preserving surface remeshing with minimal angle improvement" (IEEE TVCG 2017), and,
    /// for the crease curves, the vertex insertion / removal / relocation along feature curves of Wang et al. 2019
    /// (papers only; docs/sources/meshing-and-repair.md). Opt-in, a separate step after Remesher / RemesherPro (their behaviour is
    /// unchanged).
    ///
    /// Operations (each pass in this order, every candidate visited by increasing smallest angle, worst first): needle
    /// collapses, cap splits (at the foot of the altitude; on a crease edge the new vertex is put onto the reference's
    /// crease), edge flips (never of a crease edge), and vertex relocations (tangential, onto the reference; a regular
    /// crease vertex moves along its crease, onto the reference's crease). An operation is made only if
    ///   - the smallest angle of the changed triangles rises by at least MinGainDegrees (a split: no new triangle has a
    ///     smaller smallest or a larger largest angle than the two it replaces), so the smallest angle of the mesh never
    ///     drops, and no triangle turns by more than MaxNormalTurnDegrees or becomes degenerate;
    ///   - every new triangle is within MaxDeviation of the reference (TriangleDeviationBounds.IsWithin, as
    ///     Reducer.MaxDeviation) and every new crease edge within MaxDeviation of the reference's crease edges;
    ///   - with TwoSided, every reference triangle that was within MaxDeviation of the mesh at the start still is
    ///     (RemeshQualityCoverage, as Reducer.MaxDeviationTwoSided);
    ///   - with PreventSelfIntersections, no new triangle intersects another one (exact test);
    ///   - a collapse meets the link condition and joins no two crease curves.
    /// Creases: edges of the mesh whose triangles meet at more than CreaseAngleDegrees and that lie within MaxDeviation of
    /// a reference crease (reference edges above CreaseAngleDegrees, and boundary edges); with Constraints instead the
    /// constrained edges and the boundary edges (no dihedral detection then). The constraints are read by the first Compute and
    /// not updated (the pass changes the mesh's edge ids); later Computes on the same instance keep the pass's own crease set. Crease vertices with other than two crease edges, a turn of more than CornerAngleDegrees, or a Fixed
    /// constraint keep their position.
    /// Serial and in a fixed order: deterministic; angles and relative thresholds only: scale invariant.
    /// </summary>
    public class RemeshQualityPass
    {
        internal DMesh3 Mesh { get; private set; }
        internal DMesh3 Reference { get; private set; }

        /// <summary> the band (absolute, in the mesh's units): required, positive </summary>
        public double MaxDeviation = 0;
        /// <summary> also keep every reference triangle within MaxDeviation of the mesh (two-sided, Hausdorff); costs more </summary>
        public bool TwoSided = false;
        /// <summary> triangles with a smaller smallest angle are improved </summary>
        public double TargetMinAngleDegrees = 30;
        /// <summary> the smallest gain of an operation (stops cycling) </summary>
        internal double MinGainDegrees = 0.1;
        internal double MaxNormalTurnDegrees = 30;
        /// <summary> creases: edges whose triangles meet at a larger angle (on the mesh and on the reference) </summary>
        public double CreaseAngleDegrees = 30;
        /// <summary> a crease vertex whose crease turns by more than this keeps its position </summary>
        internal double CornerAngleDegrees = 45;
        /// <summary>
        /// keep the crease vertices with other than two crease edges or a turn above CornerAngleDegrees (corners, junctions,
        /// ends of crease spans) at their position; false: they move too, along the reference's creases, each of their
        /// crease edges kept within MaxDeviation of them
        /// </summary>
        internal bool PinCreaseCorners = true;
        /// <summary>
        /// allow collapsing an edge between two creases (a needle across a narrow strip): the creases join at the kept
        /// vertex, every crease edge kept within MaxDeviation of the reference's creases
        /// </summary>
        internal bool MergeCreases = false;
        /// <summary> the Remesher's constraints (optional): constrained edges are the creases, Fixed vertices keep their position </summary>
        public MeshConstraints Constraints = null;
        public bool PreventSelfIntersections = true;
        public bool EnableFlips = true, EnableCollapses = true, EnableSplits = true, EnableRelocation = true;
        /// <summary> the crease operations (relocation, split and collapse along creases); off: crease vertices keep their position </summary>
        public bool EnableCreaseOperations = true;
        /// <summary> triangles with a larger largest angle are split </summary>
        internal double SplitCapsAboveDegrees = 120;
        /// <summary> at most this many more triangles (relative) than at the start of the first Compute of this instance </summary>
        public double MaxTriangleGrowth = 0.05;
        public int MaxPasses = 10;
        /// <summary> subdivision depth of the band certificates </summary>
        internal int MaxDeviationDepth = 10;
        /// <summary> subdivision depth of the coverage certificates (TwoSided) </summary>
        internal int CoverageDepth = 10;
        /// <summary> sub-triangles per coverage certificate at most (TwoSided; a cost bound) </summary>
        internal int CoveragePieces = 256;
        public Func<bool> CancelF = () => false;

        public RemeshQualityPass(DMesh3 mesh, DMesh3 reference)
        {
            Mesh = mesh; Reference = reference;
        }

        // state of one Compute
        RemeshQualityResult res;
        DMeshAABBTree3 referenceTree;
        TriangleDeviationBounds bounds;
        TriangleHashGrid grid;
        RemeshQualityCoverage coverage;
        double cosTurn, cosCorner;
        int maxTriangles, growthBase = -1;
        readonly HashSet<long> creases = new HashSet<long>();
        readonly HashSet<int> fixedVertices = new HashSet<int>();
        List<bool> certified;
        // reference crease segments, by grid cells
        readonly List<Segment3d> features = new List<Segment3d>();
        readonly Dictionary<Vector3i, List<int>> featureCells = new Dictionary<Vector3i, List<int>>();
        double featureCell;
        Vector3d featureOrigin;

        /// <summary> improves Mesh in place </summary>
        public RemeshQualityResult Compute()
        {
            res = new RemeshQualityResult() { CertifiedBand = MaxDeviation };
            if (!(MaxDeviation > 0) || double.IsInfinity(MaxDeviation) || Mesh == null || Reference == null || ReferenceEquals(Mesh, Reference) || Mesh.TriangleCount == 0 || Reference.TriangleCount == 0
                || !finite(Mesh) || !finite(Reference)) {
                res.Status = RemeshQualityStatus.InvalidInput;
                return res;
            }
            var before = TriangleQualityStatistics.Compute(Mesh);
            res.TrianglesBefore = Mesh.TriangleCount;
            res.MinAngleBefore = mesh_min_angle(); res.Worst1PercentBefore = before.MinAngle.Worst1Percent;
            Prepare();
            foreach (int t in Mesh.TriangleIndices())
                if (!certified[t]) res.StartUncertifiedTriangles++;
            res.UncoveredReferenceTriangles = coverage != null ? coverage.Uncovered : 0;
            res.StartWithinBand = res.StartUncertifiedTriangles == 0 && res.UncoveredReferenceTriangles == 0;
            bool cancelled = false;
            for (int pass = 0; pass < MaxPasses && !cancelled; ++pass) {
                int n0 = operations();
                res.Passes++;
                if ((EnableCollapses && !collapse_pass()) || (EnableSplits && !split_pass()) || (EnableFlips && !flip_pass()) || (EnableRelocation && !move_pass()))
                    cancelled = true;
                if (operations() == n0)
                    break;
            }
            var after = TriangleQualityStatistics.Compute(Mesh);
            res.TrianglesAfter = Mesh.TriangleCount;
            res.MinAngleAfter = mesh_min_angle(); res.Worst1PercentAfter = after.MinAngle.Worst1Percent;
            res.UncertifiedTriangles = 0;
            foreach (int t in Mesh.TriangleIndices())
                if (!certified[t]) res.UncertifiedTriangles++;
            res.ResultWithinBand = res.UncertifiedTriangles == 0 && res.UncoveredReferenceTriangles == 0;
            res.Status = cancelled ? RemeshQualityStatus.Cancelled : operations() > 0 ? RemeshQualityStatus.Improved : RemeshQualityStatus.Unchanged;
            return res;
        }

        double mesh_min_angle()
        {
            double m = double.MaxValue;
            foreach (int t in Mesh.TriangleIndices()) m = Math.Min(m, min_angle(t));
            return m;
        }

        bool cancelled() { return CancelF != null && CancelF(); }

        int operations()
        {
            return res.Flips + res.Collapses + res.Splits + res.Relocations + res.CreaseInsertions + res.CreaseRemovals;
        }

        static bool finite(DMesh3 m)
        {
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                if (!(p.x - p.x == 0 && p.y - p.y == 0 && p.z - p.z == 0)) return false;
            }
            return true;
        }

        /// <summary> (tests) builds the queries and the crease sets for the current mesh (Compute does it) </summary>
        internal void Prepare()
        {
            if (res == null) res = new RemeshQualityResult() { CertifiedBand = MaxDeviation };
            cosTurn = Math.Cos(MaxNormalTurnDegrees * MathUtil.Deg2Rad);
            cosCorner = Math.Cos(CornerAngleDegrees * MathUtil.Deg2Rad);
            // (the growth is counted from the first Compute: repeated Computes do not grow the mesh further)
            if (growthBase < 0) growthBase = Mesh.TriangleCount;
            maxTriangles = MathUtil.FloorToIntSaturating(growthBase * (1 + MaxTriangleGrowth));
            referenceTree = new DMeshAABBTree3(Reference, true);
            bounds = new TriangleDeviationBounds(Reference) { PatchBound = true };
            build_features();
            build_creases();
            grid = (PreventSelfIntersections || TwoSided) ? build_grid() : null;
            certified = new List<bool>(new bool[Mesh.MaxTriangleID]);
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in Mesh.TriangleIndices()) {
                Mesh.GetTriVertices(t, ref a, ref b, ref c);
                certified[t] = bounds.IsWithin(referenceTree, a, b, c, MaxDeviation, MaxDeviationDepth);
            }
            coverage = TwoSided ? new RemeshQualityCoverage(Reference, Mesh, grid, MaxDeviation, CoverageDepth, CoveragePieces) : null;
        }

        static long key(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        bool is_crease(int a, int b) { return creases.Contains(key(a, b)); }

        // ---------------- reference creases ----------------

        void build_features()
        {
            features.Clear(); featureCells.Clear();
            double cos = Math.Cos(CreaseAngleDegrees * MathUtil.Deg2Rad);
            double sum = 0;
            foreach (int e in Reference.EdgeIndices()) {
                Index2i et = Reference.GetEdgeT(e);
                bool sharp = et.b < 0 || sharp_edge(Reference, et, cos);
                if (!sharp) continue;
                Index2i ev = Reference.GetEdgeV(e);
                var s = new Segment3d(Reference.GetVertex(ev.a), Reference.GetVertex(ev.b));
                features.Add(s);
                sum += s.Length;
            }
            featureOrigin = Reference.CachedBounds.Min;
            featureCell = Math.Max(features.Count > 0 ? 2 * sum / features.Count : 1, Reference.CachedBounds.MaxDim / 256);
            if (!(featureCell > 0)) featureCell = 1;
            // each segment in the cells along it, not in every cell of its box (a long skew segment over small cells): the
            // pieces of at most a quarter cell each, every piece's box (at most 2 x 2 x 2 cells); so a segment is in every cell
            // it passes through, and in at most 8 cells per quarter cell of its length (plus the clamped border)
            var seen = new HashSet<Vector3i>();
            for (int i = 0; i < features.Count; ++i) {
                Segment3d sg = features[i];
                int pieces = MathUtil.ToIntSaturating(Math.Min(1e6, Math.Ceiling(4 * sg.Length / featureCell))) + 1;
                seen.Clear();
                Vector3d prev = sg.P0;
                for (int k = 1; k <= pieces; ++k) {
                    Vector3d next = k == pieces ? sg.P1 : sg.P0 + ((double)k / pieces) * (sg.P1 - sg.P0);
                    var bx = new AxisAlignedBox3d(prev); bx.Contain(next);
                    foreach (Vector3i c in cells(bx)) {
                        if (!seen.Add(c)) continue;
                        List<int> l;
                        if (!featureCells.TryGetValue(c, out l)) featureCells[c] = l = new List<int>();
                        l.Add(i);
                    }
                    prev = next;
                }
            }
        }

        // do the two triangles meet at more than the crease angle? An edge of a zero-area triangle (no normal) is not sharp:
        // its "dihedral" is noise, and a crease there would pin the vertex that repairs it
        static bool sharp_edge(DMesh3 m, Index2i et, double cos)
        {
            Vector3d n0, n1, a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(et.a, ref a, ref b, ref c);
            if (!AlphaWrapQualityPass.unit_normal(a, b, c, out n0)) return false;
            m.GetTriVertices(et.b, ref a, ref b, ref c);
            if (!AlphaWrapQualityPass.unit_normal(a, b, c, out n1)) return false;
            return n0.Dot(n1) < cos;
        }

        IEnumerable<Vector3i> cells(AxisAlignedBox3d bx)
        {
            Vector3i lo = new Vector3i(cell(bx.Min.x - featureOrigin.x), cell(bx.Min.y - featureOrigin.y), cell(bx.Min.z - featureOrigin.z));
            Vector3i hi = new Vector3i(cell(bx.Max.x - featureOrigin.x), cell(bx.Max.y - featureOrigin.y), cell(bx.Max.z - featureOrigin.z));
            for (int k = lo.z; k <= hi.z; ++k)
                for (int j = lo.y; j <= hi.y; ++j)
                    for (int i = lo.x; i <= hi.x; ++i)
                        yield return new Vector3i(i, j, k);
        }

        int cell(double d)
        {
            // (clamped: a point far outside the reference lands in a border cell, which only costs distance tests)
            double lim = Math.Ceiling(Reference.CachedBounds.MaxDim / featureCell) + 2;
            return MathUtil.ToIntSaturating(MathUtil.Clamp(Math.Floor(d / featureCell), -1, lim));
        }

        int[] seenAt = new int[0];
        int seenStamp;

        IEnumerable<KeyValuePair<Vector3i, List<int>>> in_range(Vector3i lo, Vector3i hi)
        {
            foreach (var kv in featureCells) {
                Vector3i c = kv.Key;
                if (c.x >= lo.x && c.x <= hi.x && c.y >= lo.y && c.y <= hi.y && c.z >= lo.z && c.z <= hi.z) yield return kv;
            }
        }

        IEnumerable<KeyValuePair<Vector3i, List<int>>> range_cells(Vector3i lo, Vector3i hi)
        {
            for (int k = lo.z; k <= hi.z; ++k)
                for (int j = lo.y; j <= hi.y; ++j)
                    for (int i = lo.x; i <= hi.x; ++i) {
                        List<int> l;
                        if (featureCells.TryGetValue(new Vector3i(i, j, k), out l)) yield return new KeyValuePair<Vector3i, List<int>>(new Vector3i(i, j, k), l);
                    }
        }

        // the nearest point of a reference crease within radius r of p (false if there is none)
        bool nearest_feature(Vector3d p, double r, out Vector3d q, out int seg)
        {
            q = p; seg = -1;
            if (features.Count == 0 || !(r >= 0)) return false;
            var bx = new AxisAlignedBox3d(p); bx.Expand(r);
            double best = r * r;
            if (++seenStamp == int.MaxValue) { Array.Clear(seenAt, 0, seenAt.Length); seenStamp = 1; }
            if (seenAt.Length < features.Count) seenAt = new int[features.Count];
            Vector3i lo = new Vector3i(cell(bx.Min.x - featureOrigin.x), cell(bx.Min.y - featureOrigin.y), cell(bx.Min.z - featureOrigin.z));
            Vector3i hi = new Vector3i(cell(bx.Max.x - featureOrigin.x), cell(bx.Max.y - featureOrigin.y), cell(bx.Max.z - featureOrigin.z));
            long range = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
            // (a range of more cells than are occupied: the occupied cells in it; the answer does not depend on the order)
            IEnumerable<KeyValuePair<Vector3i, List<int>>> occupied = range > featureCells.Count ? in_range(lo, hi) : range_cells(lo, hi);
            foreach (var kv in occupied) {
                foreach (int i in kv.Value) {
                    if (seenAt[i] == seenStamp) continue;
                    seenAt[i] = seenStamp;
                    Vector3d cp = features[i].NearestPoint(p);
                    double d2 = cp.DistanceSquared(p);
                    if (d2 < best || (d2 == best && (seg < 0 || i < seg))) { best = d2; q = cp; seg = i; }
                }
            }
            return seg >= 0;
        }

        // is every point of the segment (p, q) within MaxDeviation of the reference's creases? The distance to a set of
        // segments is 1-Lipschitz: on (p, q) it is at most (d(p) + d(q) + |pq|) / 2; and the distance to one segment is
        // convex: if p and q are within MaxDeviation of the same segment, so is all of (p, q). Else split, up to MaxDeviationDepth
        internal bool chord_on_feature(Vector3d p, Vector3d q)
        {
            Vector3d cp, cq; int sp, sq;
            if (!nearest_feature(p, MaxDeviation, out cp, out sp) || !nearest_feature(q, MaxDeviation, out cq, out sq)) return false;
            return chord_on_feature(p, q, cp.Distance(p), cq.Distance(q), sp, sq, MaxDeviationDepth);
        }

        bool chord_on_feature(Vector3d p, Vector3d q, double dp, double dq, int sp, int sq, int depth)
        {
            double eps = MaxDeviation;
            if (features[sp].DistanceSquared(q) <= eps * eps || features[sq].DistanceSquared(p) <= eps * eps) return true;
            double len = p.Distance(q);
            if ((dp + dq + len) / 2 <= eps) return true;
            if (depth <= 0) return false;
            Vector3d m = 0.5 * (p + q), cm; int sm;
            if (!nearest_feature(m, eps, out cm, out sm)) return false;
            double dm = cm.Distance(m);
            return chord_on_feature(p, m, dp, dm, sp, sm, depth - 1) && chord_on_feature(m, q, dm, dq, sm, sq, depth - 1);
        }

        // ---------------- mesh creases ----------------

        MeshConstraints constraintsRead;
        DMesh3 creasesMesh;

        void build_creases()
        {
            if (Constraints != null && constraintsRead == Constraints && creasesMesh == Mesh)
                return;     // (a later Compute: the constraints' edge ids no longer match the mesh; the crease set the pass kept does)
            creases.Clear(); fixedVertices.Clear();
            creasesMesh = Mesh;
            constraintsRead = Constraints;
            if (Constraints != null) {
                foreach (int e in Mesh.EdgeIndices())
                    if (Mesh.IsBoundaryEdge(e) || (Constraints.HasEdgeConstraint(e) && !Constraints.GetEdgeConstraint(e).IsUnconstrained)) {
                        Index2i ev = Mesh.GetEdgeV(e);
                        creases.Add(key(ev.a, ev.b));
                    }
                foreach (int v in Mesh.VertexIndices())
                    if (Constraints.HasVertexConstraint(v) && Constraints.GetVertexConstraint(v).Fixed)
                        fixedVertices.Add(v);
                return;
            }
            double cos = Math.Cos(CreaseAngleDegrees * MathUtil.Deg2Rad);
            foreach (int e in Mesh.EdgeIndices()) {
                Index2i et = Mesh.GetEdgeT(e), ev = Mesh.GetEdgeV(e);
                if (et.b < 0) { creases.Add(key(ev.a, ev.b)); continue; }
                if (sharp_edge(Mesh, et, cos) && chord_on_feature(Mesh.GetVertex(ev.a), Mesh.GetVertex(ev.b)))
                    creases.Add(key(ev.a, ev.b));
            }
        }

        // the crease neighbours of v (up to 3 are collected: more is a junction anyway)
        int crease_neighbours(int v, out int u, out int w)
        {
            u = w = -1;
            int n = 0;
            foreach (int x in Mesh.VtxVerticesItr(v)) {
                if (!is_crease(v, x)) continue;
                if (n == 0) u = x; else if (n == 1) w = x;
                n++;
            }
            return n;
        }

        readonly List<int> creaseList = new List<int>();
        // the crease neighbours of v, in the order of VtxVerticesItr
        List<int> crease_list(int v, List<int> l)
        {
            l.Clear();
            foreach (int x in Mesh.VtxVerticesItr(v))
                if (is_crease(v, x)) l.Add(x);
            return l;
        }

        /// <summary>
        /// the smallest / largest corner angle of the triangle (degrees), from atan2 of twice the area and the dot product
        /// (accurate for slivers), with the corners in a canonical order (lexicographic): the same value for every rotation
        /// and reflection of the corner order, so a triangle the pass checked has the same angles in the mesh, whatever
        /// order DMesh3 stores its corners in, and the smallest angle of the mesh never drops, bit for bit
        /// </summary>
        internal static double MinAngle(Vector3d a, Vector3d b, Vector3d c)
        {
            canonical(ref a, ref b, ref c);
            angles(a, b, c, out double A, out double B, out double C);
            return Math.Min(A, Math.Min(B, C));
        }

        internal static double MaxAngle(Vector3d a, Vector3d b, Vector3d c)
        {
            canonical(ref a, ref b, ref c);
            angles(a, b, c, out double A, out double B, out double C);
            return Math.Max(A, Math.Max(B, C));
        }

        static void angles(Vector3d a, Vector3d b, Vector3d c, out double A, out double B, out double C)
        {
            Vector3d ab = b - a, ac = c - a, bc = c - b;
            double twice = ab.Cross(ac).Length;
            if (!(ab.LengthSquared > 0 && ac.LengthSquared > 0 && bc.LengthSquared > 0)) { A = B = 0; C = 180; return; }
            A = Math.Atan2(twice, ab.Dot(ac)) * MathUtil.Rad2Deg;
            B = Math.Atan2(twice, -ab.Dot(bc)) * MathUtil.Rad2Deg;
            C = Math.Atan2(twice, ac.Dot(bc)) * MathUtil.Rad2Deg;
        }

        static bool less(Vector3d p, Vector3d q)
        {
            return p.x != q.x ? p.x < q.x : p.y != q.y ? p.y < q.y : p.z < q.z;
        }

        static void canonical(ref Vector3d a, ref Vector3d b, ref Vector3d c)
        {
            Vector3d t;
            if (less(b, a)) { t = a; a = b; b = t; }
            if (less(c, b)) { t = b; b = c; c = t; }
            if (less(b, a)) { t = a; a = b; b = t; }
        }

        internal const int Free = 0, Sliding = 1, Pinned = 2;

        /// <summary> (tests, benchmarks) the class of vertex v after Compute: Free, Sliding or Pinned </summary>
        internal int VertexClass(int v) { return vertex_class(v); }
        /// <summary> (tests, benchmarks) is triangle t certified within the band (after Compute) </summary>
        internal bool IsCertified(int t) { return t < certified.Count && certified[t]; }
        /// <summary> (benchmarks) the coverage of the last Compute (TwoSided), else null </summary>
        internal RemeshQualityCoverage Coverage { get { return coverage; } }
        /// <summary> (tests) the occupied cells of the reference-crease grid </summary>
        internal int FeatureCellCount { get { return featureCells.Count; } }
        /// <summary> (tests) the counters of the last Compute or Prepare </summary>
        internal RemeshQualityResult Counters { get { return res; } }
        /// <summary> (tests, benchmarks) is (a, b) a crease edge (after Compute) </summary>
        internal bool IsCrease(int a, int b) { return is_crease(a, b); }

        // Free: on no crease; Sliding: a regular crease vertex (two crease edges, a small turn, crease operations on);
        // Pinned: keeps its position (a corner, junction or crease end, Fixed, or a crease vertex with crease operations off)
        int vertex_class(int v)
        {
            if (fixedVertices.Contains(v)) return Pinned;
            int u, w;
            int n = crease_neighbours(v, out u, out w);
            if (n == 0) return Free;
            if (!EnableCreaseOperations) return Pinned;
            if (n != 2) return PinCreaseCorners ? Pinned : Sliding;
            Vector3d p = Mesh.GetVertex(v), d0 = p - Mesh.GetVertex(u), d1 = Mesh.GetVertex(w) - p;
            double l0 = d0.Length, l1 = d1.Length;
            if (!(l0 > 0 && l1 > 0) || d0.Dot(d1) < cosCorner * l0 * l1) return PinCreaseCorners ? Pinned : Sliding;
            return Sliding;
        }

        // ---------------- the guards ----------------

        internal bool accept(List<Index3i> ids, List<Triangle3d> tris, HashSet<int> removed, List<KeyValuePair<Vector3d, Vector3d>> creaseChords)
        {
            foreach (var ch in creaseChords)
                if (!chord_on_feature(ch.Key, ch.Value)) { res.RejectedByCrease++; res.RejectedByBand++; return false; }
            foreach (Triangle3d t in tris)
                if (!bounds.IsWithin(referenceTree, t.V0, t.V1, t.V2, MaxDeviation, MaxDeviationDepth)) { res.RejectedByBand++; return false; }
            if (PreventSelfIntersections) {
                for (int i = 0; i < tris.Count; ++i) {
                    Triangle3d A = tris[i];
                    Index3i ia = ids[i];
                    for (int j = i + 1; j < tris.Count; ++j) {
                        Triangle3d B = tris[j];
                        if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ids[j], ref B)) { res.RejectedByIntersection++; return false; }
                    }
                    bool hit = false;
                    grid.Query(AlphaWrapQualityPass.box(A.V0, A.V1, A.V2), (tc) => {
                        if (hit || removed.Contains(tc) || !Mesh.IsTriangle(tc)) return;
                        Index3i ic = Mesh.GetTriangle(tc);
                        Triangle3d C = new Triangle3d(Mesh.GetVertex(ic.a), Mesh.GetVertex(ic.b), Mesh.GetVertex(ic.c));
                        // (a zero-area triangle of the mesh is skipped, as in AlphaWrapQualityPass: the exact test is for
                        // proper triangles. Safe in practice: a new triangle that crosses it crosses the proper triangle on the
                        // other side of its longest edge too, which is tested, and a zero-area triangle in R is replaced, its
                        // new triangles tested; only a chain of zero-area triangles can hide a crossing. Such a triangle is a
                        // defect NotCleanReason reports already)
                        if (ExactTriangleIntersection.IsDegenerate(ref C)) return;
                        if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ic, ref C)) hit = true;
                    });
                    if (hit) { res.RejectedByIntersection++; return false; }
                }
            }
            if (coverage != null && !coverage.Check(Mesh, removed, tris)) { res.RejectedByCoverage++; res.RejectedByBand++; return false; }
            return true;
        }

        // after an accepted operation: the grid, the coverage and the certified flags of the new triangles
        void commit(HashSet<int> removed, List<int> added)
        {
            if (grid != null) {
                foreach (int t in removed) grid.Remove(t);
                foreach (int t in added) {
                    Index3i tv = Mesh.GetTriangle(t);
                    grid.Insert(t, AlphaWrapQualityPass.box(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c)));
                }
            }
            if (coverage != null) coverage.Commit(removed, added);
            while (certified.Count < Mesh.MaxTriangleID) certified.Add(false);
            foreach (int t in added) certified[t] = true;
        }

        TriangleHashGrid build_grid()
        {
            double sum = 0; int n = 0;
            foreach (int eid in Mesh.EdgeIndices()) {
                Index2i ev = Mesh.GetEdgeV(eid);
                sum += Mesh.GetVertex(ev.a).Distance(Mesh.GetVertex(ev.b)); n++;
            }
            var g = new TriangleHashGrid(Mesh.CachedBounds.Min, Math.Max(1.5 * sum / Math.Max(1, n), MathUtil.ZeroTolerance));
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i tv = Mesh.GetTriangle(tid);
                g.Insert(tid, AlphaWrapQualityPass.box(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c)));
            }
            return g;
        }

        double min_angle(int tid)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return MinAngle(Mesh.GetVertex(t.a), Mesh.GetVertex(t.b), Mesh.GetVertex(t.c));
        }

        // the area-weighted unit normal of the triangles (false if they have no area at all)
        bool ring_normal(IEnumerable<int> tids, out Vector3d n)
        {
            n = Vector3d.Zero;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in tids) {
                Mesh.GetTriVertices(t, ref a, ref b, ref c);
                n += (b - a).Cross(c - a);
            }
            double l = n.Length;
            if (!(l > 0)) return false;
            n /= l;
            return true;
        }

        int opposite(int tid, int a, int b)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return (t.a != a && t.a != b) ? t.a : (t.b != a && t.b != b) ? t.b : t.c;
        }

        // the ids (edges or vertices) with a triangle below the target, by increasing smallest angle (ties by id)
        List<int> worst_first(int maxId, bool edges)
        {
            var keyed = new List<KeyValuePair<double, int>>();
            for (int id = 0; id < maxId; ++id) {
                double a = double.MaxValue;
                if (edges) {
                    if (!Mesh.IsEdge(id)) continue;
                    Index2i et = Mesh.GetEdgeT(id);
                    a = min_angle(et.a);
                    if (et.b >= 0) a = Math.Min(a, min_angle(et.b));
                } else {
                    if (!Mesh.IsVertex(id)) continue;
                    foreach (int t in Mesh.VtxTrianglesItr(id)) a = Math.Min(a, min_angle(t));
                }
                if (a < TargetMinAngleDegrees) keyed.Add(new KeyValuePair<double, int>(a, id));
            }
            keyed.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
            var order = new List<int>(keyed.Count);
            foreach (var kv in keyed) order.Add(kv.Value);
            return order;
        }

        // ---------------- the operations ----------------

        readonly List<Index3i> ids = new List<Index3i>();
        readonly List<Triangle3d> tris = new List<Triangle3d>();
        readonly HashSet<int> removed = new HashSet<int>();
        readonly List<KeyValuePair<Vector3d, Vector3d>> chords = new List<KeyValuePair<Vector3d, Vector3d>>();

        bool flip_pass()
        {
            foreach (int eid in worst_first(Mesh.MaxEdgeID, true)) {
                if (cancelled()) return false;
                if (!Mesh.IsEdge(eid) || Mesh.IsBoundaryEdge(eid)) continue;
                Index2i ev = Mesh.GetEdgeV(eid);
                if (is_crease(ev.a, ev.b)) continue;
                Index2i et = Mesh.GetEdgeT(eid);
                double cur = Math.Min(min_angle(et.a), min_angle(et.b));
                if (!(cur < TargetMinAngleDegrees)) continue;
                Index3i ta = Mesh.GetTriangle(et.a);
                if ((ta.a == ev.b && ta.b == ev.a) || (ta.b == ev.b && ta.c == ev.a) || (ta.c == ev.b && ta.a == ev.a))
                    ev = new Index2i(ev.b, ev.a);
                Index2i ov = Mesh.GetEdgeOpposingV(eid);
                if (Mesh.FindEdge(ov.a, ov.b) != DMesh3.InvalidID) continue;
                int a = ev.a, b = ev.b, c = opposite(et.a, a, b), d = opposite(et.b, a, b);
                Vector3d pa = Mesh.GetVertex(a), pb = Mesh.GetVertex(b), pc = Mesh.GetVertex(c), pd = Mesh.GetVertex(d);
                double nw = Math.Min(MinAngle(pc, pa, pd), MinAngle(pd, pb, pc));
                if (!(nw >= cur + MinGainDegrees)) { res.RejectedByAngle++; continue; }
                Vector3d n0, n1, m0, m1;
                // (a flat old triangle has no normal: the new ones are held to the other old triangle's; both flat: refused)
                bool h0 = AlphaWrapQualityPass.unit_normal(pa, pb, pc, out n0), h1 = AlphaWrapQualityPass.unit_normal(pb, pa, pd, out n1);
                if (!AlphaWrapQualityPass.unit_normal(pc, pa, pd, out m0) || !AlphaWrapQualityPass.unit_normal(pd, pb, pc, out m1)
                    || (h0 && (m0.Dot(n0) < cosTurn || m1.Dot(n0) < cosTurn)) || (h1 && (m0.Dot(n1) < cosTurn || m1.Dot(n1) < cosTurn))
                    || (!h0 && !h1)) { res.RejectedByAngle++; continue; }
                ids.Clear(); tris.Clear(); removed.Clear(); chords.Clear();
                ids.Add(new Index3i(c, a, d)); tris.Add(new Triangle3d(pc, pa, pd));
                ids.Add(new Index3i(d, b, c)); tris.Add(new Triangle3d(pd, pb, pc));
                removed.Add(et.a); removed.Add(et.b);
                if (!accept(ids, tris, removed, chords)) continue;
                DMesh3.EdgeFlipInfo info;
                if (Mesh.FlipEdge(eid, out info) != MeshResult.Ok) continue;      // (DMesh3 refuses before it changes anything)
                commit(removed, new List<int> { info.t0, info.t1 });
                res.Flips++;
            }
            return true;
        }

        // caps (largest angle above SplitCapsAboveDegrees), largest angle first: the longest edge split at the foot of the
        // altitude from the opposite vertex; then needles (smallest angle below the target, worst first) whose shortest
        // edge could not be collapsed: a crease edge of theirs (the longest first), else their longest edge, split at its
        // midpoint (graded refinement next to short crease edges and across narrow strips between two creases). On a crease
        // edge the new vertex is put onto the reference's crease (Wang et al. 2019, insertion)
        bool split_pass()
        {
            var caps = new List<KeyValuePair<double, int>>();
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tv = Mesh.GetTriangle(t);
                double m = MaxAngle(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c));
                if (m > SplitCapsAboveDegrees) caps.Add(new KeyValuePair<double, int>(m, t));
            }
            caps.Sort((x, y) => x.Key != y.Key ? -x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
            foreach (var kv in caps) {
                if (cancelled()) return false;
                if (Mesh.TriangleCount + 2 > maxTriangles) return true;
                int t = kv.Value;
                if (!Mesh.IsTriangle(t)) continue;
                Index3i tv = Mesh.GetTriangle(t);
                Vector3d[] p = { Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c) };
                if (!(MaxAngle(p[0], p[1], p[2]) > SplitCapsAboveDegrees)) continue;
                int k = 0;
                for (int j = 1; j < 3; ++j)
                    if (p[(j + 1) % 3].DistanceSquared(p[j]) > p[(k + 1) % 3].DistanceSquared(p[k])) k = j;
                Vector3d a = p[k], b = p[(k + 1) % 3], c = p[(k + 2) % 3], ab = b - a;
                double l2 = ab.LengthSquared;
                if (!(l2 > 0)) continue;
                double s = MathUtil.Clamp((c - a).Dot(ab) / l2, 0.05, 0.95);
                try_split(t, tv[k], tv[(k + 1) % 3], tv[(k + 2) % 3], s, true);
            }
            if (!SplitNeedles) return true;
            foreach (int t in worst_triangles()) {
                if (cancelled()) return false;
                if (Mesh.TriangleCount + 2 > maxTriangles) return true;
                if (!Mesh.IsTriangle(t)) continue;
                Index3i tv = Mesh.GetTriangle(t);
                Vector3d[] p = { Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c) };
                if (!(MinAngle(p[0], p[1], p[2]) < TargetMinAngleDegrees)) continue;
                // the edges k = (k, k+1) by decreasing length, crease edges first
                var order = new List<int> { 0, 1, 2 };
                order.Sort((x, y) => {
                    bool cx = is_crease(tv[x], tv[(x + 1) % 3]), cy = is_crease(tv[y], tv[(y + 1) % 3]);
                    if (cx != cy) return cx ? -1 : 1;
                    double lx = p[x].DistanceSquared(p[(x + 1) % 3]), ly = p[y].DistanceSquared(p[(y + 1) % 3]);
                    return lx != ly ? -lx.CompareTo(ly) : x.CompareTo(y);
                });
                foreach (int k in order)
                    if (try_split(t, tv[k], tv[(k + 1) % 3], tv[(k + 2) % 3], 0.5, false)) break;
            }
            return true;
        }

        /// <summary> split a crease edge only, or every edge, for a needle that the collapses left (false: caps only) </summary>
        internal bool SplitNeedles = true;

        // the triangles below the target, by increasing smallest angle (ties by id)
        List<int> worst_triangles()
        {
            var keyed = new List<KeyValuePair<double, int>>();
            foreach (int t in Mesh.TriangleIndices()) {
                double an = min_angle(t);
                if (an < TargetMinAngleDegrees) keyed.Add(new KeyValuePair<double, int>(an, t));
            }
            keyed.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
            var order = new List<int>(keyed.Count);
            foreach (var kv in keyed) order.Add(kv.Value);
            return order;
        }

        // split edge (va, vb) of triangle t = (va, vb, vc) (in its order) at va + s (vb - va), on a crease edge moved onto
        // the reference's crease. cap: no new triangle with a smaller smallest or a larger largest angle than the two old
        // ones; else (a needle): the smallest angle of the four new triangles above that of the two old ones by MinGainDegrees
        bool try_split(int t, int va, int vb, int vc, double s, bool cap)
        {
            int eid = Mesh.FindEdge(va, vb);
            if (eid == DMesh3.InvalidID || Mesh.IsBoundaryEdge(eid)) return false;
            bool crease = is_crease(va, vb);
            if (crease && !EnableCreaseOperations) return false;
            Vector3d a = Mesh.GetVertex(va), b = Mesh.GetVertex(vb), c = Mesh.GetVertex(vc);
            Vector3d m = a + s * (b - a);
            if (crease) {
                Vector3d q; int seg;
                if (!nearest_feature(m, MaxDeviation, out q, out seg)) return false;
                m = q;
            }
            Index2i et = Mesh.GetEdgeT(eid);
            int other = et.a == t ? et.b : et.a;
            int d = opposite(other, va, vb);
            Vector3d pd = Mesh.GetVertex(d);
            ids.Clear(); tris.Clear(); removed.Clear(); chords.Clear();
            const int NewId = int.MaxValue;
            ids.Add(new Index3i(va, NewId, vc)); tris.Add(new Triangle3d(a, m, c));
            ids.Add(new Index3i(NewId, vb, vc)); tris.Add(new Triangle3d(m, b, c));
            ids.Add(new Index3i(vb, NewId, d)); tris.Add(new Triangle3d(b, m, pd));
            ids.Add(new Index3i(NewId, va, d)); tris.Add(new Triangle3d(m, a, pd));
            double oldMin = Math.Min(MinAngle(a, b, c), MinAngle(b, a, pd));
            double oldMax = Math.Max(MaxAngle(a, b, c), MaxAngle(b, a, pd));
            Vector3d o0, o1;
            // (a flat old triangle has no normal: its two halves are held to the other side's; both flat: refused)
            bool h0 = AlphaWrapQualityPass.unit_normal(a, b, c, out o0), h1 = AlphaWrapQualityPass.unit_normal(b, a, pd, out o1);
            if (!h0 && !h1) { res.RejectedByAngle++; return false; }
            if (!h0) { o0 = o1; h0 = true; } else if (!h1) { o1 = o0; h1 = true; }
            double nwMin = double.MaxValue;
            for (int i = 0; i < 4; ++i) {
                Triangle3d nt = tris[i];
                Vector3d n;
                if (!AlphaWrapQualityPass.unit_normal(nt.V0, nt.V1, nt.V2, out n) || ((i < 2 ? h0 : h1) && n.Dot(i < 2 ? o0 : o1) < cosTurn)) { res.RejectedByAngle++; return false; }
                double mn = MinAngle(nt.V0, nt.V1, nt.V2);
                if (cap && !(mn >= oldMin && MaxAngle(nt.V0, nt.V1, nt.V2) < oldMax)) { res.RejectedByAngle++; return false; }
                nwMin = Math.Min(nwMin, mn);
            }
            if (!cap && !(nwMin >= oldMin + MinGainDegrees)) { res.RejectedByAngle++; return false; }
            removed.Add(t); removed.Add(other);
            if (crease) { chords.Add(new KeyValuePair<Vector3d, Vector3d>(a, m)); chords.Add(new KeyValuePair<Vector3d, Vector3d>(m, b)); }
            if (!accept(ids, tris, removed, chords)) return false;
            DMesh3.EdgeSplitInfo info;
            if (Mesh.SplitEdge(eid, out info) != MeshResult.Ok) return false;      // (DMesh3 refuses before it changes anything)
            Mesh.SetVertex(info.vNew, m);
            if (crease) {
                creases.Remove(key(va, vb));
                creases.Add(key(va, info.vNew)); creases.Add(key(info.vNew, vb));
            }
            var added = new List<int>();
            foreach (int nt in Mesh.VtxTrianglesItr(info.vNew)) added.Add(nt);
            commit(removed, added);
            if (crease) res.CreaseInsertions++; else res.Splits++;
            return true;
        }

        // needles: their shortest edge collapsed. A non-crease edge: into the end that is on a crease or pinned (at its
        // position), else at the midpoint or either end; a crease edge (crease operations): a sliding crease vertex is
        // removed into its crease neighbour (Wang et al. 2019). Never two crease curves joined, never a pinned vertex moved
        bool collapse_pass()
        {
            var order = new List<int>();
            {
                var keyed = new List<KeyValuePair<double, int>>();
                foreach (int t in Mesh.TriangleIndices()) {
                    double an = min_angle(t);
                    if (an < TargetMinAngleDegrees) keyed.Add(new KeyValuePair<double, int>(an, t));
                }
                keyed.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
                foreach (var kv in keyed) order.Add(kv.Value);
            }
            // (keep, remove, position)
            var candidates = new List<KeyValuePair<double, (int keep, int rem, Vector3d q)>>(4);
            foreach (int t in order) {
                if (cancelled()) return false;
                if (!Mesh.IsTriangle(t)) continue;
                Index3i tv = Mesh.GetTriangle(t);
                Vector3d[] p = { Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c) };
                if (!(MinAngle(p[0], p[1], p[2]) < TargetMinAngleDegrees)) continue;
                int k = 0;
                for (int j = 1; j < 3; ++j)
                    if (p[(j + 1) % 3].DistanceSquared(p[j]) < p[(k + 1) % 3].DistanceSquared(p[k])) k = j;
                int a = tv[k], b = tv[(k + 1) % 3];
                int eid = Mesh.FindEdge(a, b);
                if (eid == DMesh3.InvalidID || Mesh.IsBoundaryEdge(eid) || Mesh.IsBoundaryVertex(a) || Mesh.IsBoundaryVertex(b)) continue;
                int common = 0;
                foreach (int x in Mesh.VtxVerticesItr(a))
                    if (x != b && Mesh.FindEdge(x, b) != DMesh3.InvalidID) common++;
                if (common != 2) continue;
                Index2i ov = Mesh.GetEdgeOpposingV(eid);
                // (the two opposite vertices joined already: a tetrahedron or a flat fold would collapse into a doubled triangle)
                if (Mesh.FindEdge(ov.a, ov.b) != DMesh3.InvalidID) continue;
                int ca = vertex_class(a), cb = vertex_class(b);
                bool creaseEdge = is_crease(a, b);
                Vector3d pa = Mesh.GetVertex(a), pb = Mesh.GetVertex(b);
                var opts = new List<(int keep, int rem, Vector3d q)>(3);
                bool merge = !creaseEdge && ca != Free && cb != Free;
                if (creaseEdge || merge) {
                    if (!EnableCreaseOperations || (merge && !MergeCreases)) continue;
                    // (no triangle of two crease edges with the opposite vertex: the curves would merge)
                    if ((is_crease(a, ov.a) && is_crease(b, ov.a)) || (is_crease(a, ov.b) && is_crease(b, ov.b))) continue;
                    if (cb == Sliding) opts.Add((a, b, pa));
                    if (ca == Sliding) opts.Add((b, a, pb));
                } else {
                    bool sa = ca != Free, sb = cb != Free;
                    if (sa) opts.Add((a, b, pa));
                    else if (sb) opts.Add((b, a, pb));
                    else { opts.Add((a, b, 0.5 * (pa + pb))); opts.Add((a, b, pa)); opts.Add((a, b, pb)); }
                }
                if (opts.Count == 0) continue;
                removed.Clear();
                foreach (int r in Mesh.VtxTrianglesItr(a)) removed.Add(r);
                foreach (int r in Mesh.VtxTrianglesItr(b)) removed.Add(r);
                Index2i et = Mesh.GetEdgeT(eid);
                bool ringHasNormal = ring_normal(removed, out Vector3d ringNormal);
                candidates.Clear();
                foreach (var o in opts) {
                    // the angle rule over the triangles that change: the two deleted ones, those of the removed vertex, and
                    // those of the kept vertex only if it moves (the others keep their geometry, so their angles cannot drop,
                    // and a bad triangle among them must not block the collapse)
                    bool keepMoves = o.q != Mesh.GetVertex(o.keep);
                    double cur = Math.Min(min_angle(et.a), et.b >= 0 ? min_angle(et.b) : double.MaxValue);
                    double nw = double.MaxValue;
                    bool ok = true;
                    foreach (int r in removed) {
                        if (r == et.a || r == et.b) continue;
                        Index3i rv = Mesh.GetTriangle(r);
                        bool changes = keepMoves || rv.a == o.rem || rv.b == o.rem || rv.c == o.rem;
                        if (changes) cur = Math.Min(cur, min_angle(r));
                        Vector3d A = Mesh.GetVertex(rv.a), B = Mesh.GetVertex(rv.b), C = Mesh.GetVertex(rv.c);
                        Vector3d n0, n1;
                        // (a flat old triangle has no normal: its new one is held to the ring's normal instead)
                        bool hasOld = AlphaWrapQualityPass.unit_normal(A, B, C, out n0);
                        if (!hasOld) { n0 = ringNormal; hasOld = ringHasNormal; if (!hasOld) { ok = false; break; } }
                        if (rv.a == a || rv.a == b) A = o.q;
                        if (rv.b == a || rv.b == b) B = o.q;
                        if (rv.c == a || rv.c == b) C = o.q;
                        if (!AlphaWrapQualityPass.unit_normal(A, B, C, out n1) || (hasOld && n1.Dot(n0) < cosTurn)) { ok = false; break; }
                        if (changes) nw = Math.Min(nw, MinAngle(A, B, C));
                    }
                    if (ok && nw >= cur + MinGainDegrees) candidates.Add(new KeyValuePair<double, (int, int, Vector3d)>(nw, o));
                    else res.RejectedByAngle++;
                }
                // best first; stable for equal angles (the order above)
                for (int i = 1; i < candidates.Count; ++i)
                    for (int j = i; j > 0 && candidates[j].Key > candidates[j - 1].Key; --j) {
                        var tmp = candidates[j]; candidates[j] = candidates[j - 1]; candidates[j - 1] = tmp;
                    }
                foreach (var cand in candidates) {
                    var (keep, rem, q) = cand.Value;
                    ids.Clear(); tris.Clear(); chords.Clear();
                    foreach (int r in removed) {
                        if (r == et.a || r == et.b) continue;
                        Index3i rv = Mesh.GetTriangle(r);
                        Vector3d A = Mesh.GetVertex(rv.a), B = Mesh.GetVertex(rv.b), C = Mesh.GetVertex(rv.c);
                        if (rv.a == a || rv.a == b) { A = q; rv.a = keep; }
                        if (rv.b == a || rv.b == b) { B = q; rv.b = keep; }
                        if (rv.c == a || rv.c == b) { C = q; rv.c = keep; }
                        ids.Add(rv); tris.Add(new Triangle3d(A, B, C));
                    }
                    collapse_chords(keep, rem, q, chords);
                    if (!accept(ids, tris, removed, chords)) continue;
                    crease_list(rem, creaseList);
                    DMesh3.EdgeCollapseInfo info;
                    if (Mesh.CollapseEdge(keep, rem, out info) != MeshResult.Ok) break;      // (DMesh3 refuses before it changes anything)
                    Mesh.SetVertex(keep, q);
                    foreach (int x in creaseList) {
                        creases.Remove(key(rem, x));
                        if (x != keep) creases.Add(key(keep, x));
                    }
                    var added = new List<int>();
                    foreach (int nt in Mesh.VtxTrianglesItr(keep)) added.Add(nt);
                    commit(removed, added);
                    if (creaseEdge || merge) res.CreaseRemovals++; else res.Collapses++;
                    break;
                }
            }
            return true;
        }

        // the new crease edges of collapsing rem into keep at q: the removed vertex's other crease edges become (keep, x)
        internal void collapse_chords(int keep, int rem, Vector3d q, List<KeyValuePair<Vector3d, Vector3d>> chords)
        {
            foreach (int x in crease_list(rem, creaseList))
                if (x != keep) chords.Add(new KeyValuePair<Vector3d, Vector3d>(q, Mesh.GetVertex(x)));
        }

        Vector3d closest_on_reference(Vector3d p)
        {
            int t = referenceTree.FindNearestTriangle(p);
            return MeshQueries.TriangleDistance(Reference, t, p).TriangleClosest;
        }

        // vertices of bad triangles moved: a free vertex to the centroid of its neighbours in its tangent plane, followed
        // onto the reference at its own offset from it; a sliding crease vertex towards the midpoint of its two crease
        // neighbours (and half way), put onto the reference's crease
        bool move_pass()
        {
            var ring = new List<int>();
            var targets = new List<Vector3d>(2);
            foreach (int v in worst_first(Mesh.MaxVertexID, false)) {
                if (cancelled()) return false;
                if (!Mesh.IsVertex(v)) continue;
                int cls = vertex_class(v);
                if (cls == Pinned) continue;
                if (cls == Free && Mesh.IsBoundaryVertex(v)) continue;
                ring.Clear();
                foreach (int t in Mesh.VtxTrianglesItr(v)) ring.Add(t);
                if (ring.Count < 2 || (cls == Free && ring.Count < 3)) continue;
                double cur = double.MaxValue;
                foreach (int t in ring) cur = Math.Min(cur, min_angle(t));
                if (!(cur < TargetMinAngleDegrees)) continue;
                Vector3d p = Mesh.GetVertex(v);
                targets.Clear();
                int n = crease_list(v, creaseList).Count;
                if (cls == Free || n != 2) {
                    Vector3d cen = Vector3d.Zero, nrm = Vector3d.Zero;
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
                    if (cls == Free) {
                        Vector3d cp = closest_on_reference(p), cq = closest_on_reference(q);
                        targets.Add(cq + (p - cp));
                    } else {
                        // a corner, junction or crease end (PinCreaseCorners off): towards the centroid, onto the creases
                        double r = 0;
                        foreach (int x in creaseList) r = Math.Max(r, p.Distance(Mesh.GetVertex(x)));
                        foreach (Vector3d g in new[] { q, 0.5 * (p + q) }) {
                            Vector3d f; int seg;
                            if (nearest_feature(g, r, out f, out seg)) targets.Add(f);
                        }
                    }
                } else {
                    int cu = creaseList[0], cw = creaseList[1];
                    Vector3d mid = 0.5 * (Mesh.GetVertex(cu) + Mesh.GetVertex(cw));
                    double r = Math.Max(p.Distance(Mesh.GetVertex(cu)), p.Distance(Mesh.GetVertex(cw)));
                    foreach (Vector3d g in new[] { mid, 0.5 * (p + mid) }) {
                        Vector3d q; int seg;
                        if (nearest_feature(g, r, out q, out seg)) targets.Add(q);
                    }
                }
                bool ringHasNormal = ring_normal(ring, out Vector3d ringNormal);
                foreach (Vector3d q in targets) {
                    if (q == p) continue;
                    double nw = double.MaxValue;
                    bool ok = true;
                    ids.Clear(); tris.Clear(); removed.Clear(); chords.Clear();
                    foreach (int t in ring) {
                        Index3i tv = Mesh.GetTriangle(t);
                        Vector3d A = Mesh.GetVertex(tv.a), B = Mesh.GetVertex(tv.b), C = Mesh.GetVertex(tv.c);
                        Vector3d n0, n1;
                        // (a flat old triangle has no normal: its new one is held to the ring's normal instead)
                        bool hasOld = AlphaWrapQualityPass.unit_normal(A, B, C, out n0);
                        if (!hasOld) { n0 = ringNormal; hasOld = ringHasNormal; if (!hasOld) { ok = false; break; } }
                        if (tv.a == v) A = q; else if (tv.b == v) B = q; else C = q;
                        if (!AlphaWrapQualityPass.unit_normal(A, B, C, out n1) || (hasOld && n1.Dot(n0) < cosTurn)) { ok = false; break; }
                        nw = Math.Min(nw, MinAngle(A, B, C));
                        ids.Add(tv); tris.Add(new Triangle3d(A, B, C)); removed.Add(t);
                    }
                    if (!ok || !(nw >= cur + MinGainDegrees)) { res.RejectedByAngle++; continue; }
                    foreach (int x in creaseList)
                        chords.Add(new KeyValuePair<Vector3d, Vector3d>(Mesh.GetVertex(x), q));
                    if (!accept(ids, tris, removed, chords)) continue;
                    Mesh.SetVertex(v, q);
                    commit(removed, new List<int>(ring));
                    res.Relocations++;
                    if (cls == Sliding) res.CreaseRelocations++;
                    break;
                }
            }
            return true;
        }
    }
}
