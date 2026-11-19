// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// A test pin: a polyline axis (a straight bore, an angled channel) and a radius. The passage "stays open" in a wrap if the
    /// whole axis lies outside the wrap and at least Radius from its triangles.
    /// </summary>
    public sealed class PassagePin
    {
        /// <summary> the axis: at least one point (one point is a ball), finite coordinates </summary>
        public List<Vector3d> Axis;
        /// <summary> the pin radius: positive, finite, below the input's own clearance from the axis </summary>
        public double Radius;

        public PassagePin(IEnumerable<Vector3d> axis, double radius)
        {
            Axis = new List<Vector3d>(axis);
            Radius = radius;
        }
    }

    /// <summary>
    /// What <see cref="PassageWrap"/> measured on its result for one pin.
    /// </summary>
    public sealed class PassagePinResult
    {
        /// <summary> certified lower bound of the distance from the axis to the input triangles (before the wrap) </summary>
        public double InputClearance;
        /// <summary>
        /// the distance from the axis to the triangles of the returned wrap lies in [ClearanceLow, ClearanceHigh] (a branch and bound on
        /// the 1-Lipschitz distance function, ClearanceHigh - ClearanceLow at most 1e-3 times the pin radius unless the lower bound is
        /// already above the radius; double rounding is covered by a margin, see PassageWrap)
        /// </summary>
        public double ClearanceLow, ClearanceHigh;
        /// <summary> every vertex of the axis has winding number below 0.5 in the wrap: it lies outside </summary>
        public bool AxisOutside;
        /// <summary> ClearanceLow &gt;= Radius and AxisOutside: the pin passes the returned wrap (certified) </summary>
        public bool Passed;
    }

    /// <summary>
    /// Alpha wrap driven by a functional requirement, "this passage must stay open", instead of a raw alpha (opt-in; MeshAlphaWrap
    /// is unchanged). The caller gives test pins (polyline axis and radius) and optionally a triangle budget; Compute() chooses the
    /// coarsest alpha for which the wrap provably keeps the pins clear, wraps once, optionally simplifies, and measures the result.
    ///
    /// The a priori bound (own derivation, docs/design/PASSAGE_WRAP.md has the proof and its preconditions). Let r be the distance
    /// from a pin's axis to the input, d the offset and a the alpha of the wrap, and suppose every vertex of the wrap is within d of
    /// the input and every facet between an inside and an outside cell, at the end of the carving, has a smallest empty ball
    /// of radius Phi(f) at most a (that is what the carving's termination says). For a point q of the axis and a facet f with
    /// vertices p_i and q = sum l_i p_i (l_i &gt;= 0, sum 1) and any ball of centre m and radius R through the p_i:
    /// R^2 = |q - m|^2 + sum l_i |p_i - q|^2 (expand sum l_i |p_i - m|^2 about q). With |p_i - q| &gt;= r - d (triangle inequality) a
    /// facet containing q has Phi(f) &gt;= r - d, so r - d &gt; a means no facet of the carved wrap contains an axis point; and for y in
    /// f, with weights mu_i, dist(q, y)^2 = sum mu_i |p_i - q|^2 - sum mu_i |p_i - y|^2 &gt;= (r - d)^2 - Phi(f)^2. So the carved wrap
    /// stays at least sqrt((r - d)^2 - a^2) from the axis, and a pin of radius p passes if a &lt;= sqrt((r - d)^2 - p^2). With the
    /// offset a / OffsetRatio this is a quadratic in a, solved by <see cref="MaxAlpha"/>.
    ///
    /// The bound is a proof for the carving loop, not for everything the result went through: the manifold step, the simplification,
    /// rejected Steiner points and a reached point limit are outside its premises. So the result is always measured (the a
    /// posteriori check: the clearance of each axis from the wrap's triangles, certified, and the winding number of the axis'
    /// vertices); a pin counts as passed only if that check says so. If it fails, alpha is lowered (FallbackFactor, at most
    /// MaxFallbackSteps times) and the wrap repeated; if it still fails the status is PassageNotKept and the last wrap is
    /// returned with the measured intervals, never a claimed pass.
    /// </summary>
    public class PassageWrap
    {
        public enum PassageStatus
        {
            Success = 0,
            /// <summary> the input has no triangles, or non-finite coordinates </summary>
            InvalidInput = 1,
            /// <summary> no pins, a pin without axis points, non-finite axis points, a radius not positive and finite, or bad options </summary>
            InvalidPins = 2,
            /// <summary> a pin's radius is not below the input's clearance from its axis: no wrap with an offset keeps that pin open </summary>
            PinBlockedByInput = 3,
            /// <summary>
            /// MaxTriangles is set and the estimated size of the wrap at the alpha the pins need (EstimatedTriangles at Alpha) is above it:
            /// nothing was wrapped (Result is null). The wrap is never coarsened past the pins; the budget or the pins have to change.
            /// </summary>
            BudgetExceeded = 4,
            /// <summary> the wrap itself failed (see WrapStatus) </summary>
            WrapFailed = 5,
            /// <summary> every attempt, the fallbacks included, failed the a posteriori check: Result is the last wrap, PinResults say which pin </summary>
            PassageNotKept = 6,
            Cancelled = 7
        }

        /// <summary> input triangles (any mesh: soup, open or non-manifold); vertices without triangles are ignored </summary>
        public DMesh3 Mesh;
        /// <summary> the pins that must stay open (at least one) </summary>
        public List<PassagePin> Pins = new List<PassagePin>();
        /// <summary>
        /// alpha / offset (default 30, the library default; at least 3). The offset is Alpha / OffsetRatio. A smaller ratio is a larger
        /// offset: the wall is wrapped farther away, the passage needs more clearance (r - offset &gt; alpha).
        /// </summary>
        public double OffsetRatio = 30;
        /// <summary>
        /// Budget (0 or less = none): if the size of the wrap estimated at the alpha the pins need (MeshAlphaWrap.EstimateDensity *
        /// area / alpha^2, open components twice, as MeshAlphaWrap.MaxEstimatedTriangles) is above this, Compute() returns BudgetExceeded
        /// without wrapping. It never coarsens alpha past what the pins allow.
        /// </summary>
        public int MaxTriangles = 0;
        /// <summary> an upper limit of alpha for a finer wrap than the pins need (0 or less = none) </summary>
        public double MaxAlphaLimit = 0;
        /// <summary> simplify the wrap within half an offset (MeshRobustRepair.SimplifyAlphaWrap) and keep it if it passes too (default true) </summary>
        public bool Simplify = true;
        /// <summary> lower alpha by this factor (in (0, 1)) after a failed check, at most MaxFallbackSteps times (default 0.8, 3) </summary>
        public double FallbackFactor = 0.8;
        public int MaxFallbackSteps = 3;
        /// <summary> MeshAlphaWrap.PullPinchPoints: with it the bound uses 2 offsets as the distance of the vertices to the input (default false) </summary>
        public bool PullPinchPoints = false;
        /// <summary> threads of the wrap (MeshAlphaWrap.MaxThreads; the result does not depend on it) </summary>
        public int MaxThreads = 0;
        public Func<bool> CancelF = () => false;

        // outputs
        public DMesh3 Result;
        public PassageStatus Status;
        public string Error;
        /// <summary> the status of the last wrap run, and its Counts </summary>
        public MeshAlphaWrap.WrapStatus WrapStatus;
        public Dictionary<string, long> WrapCounts = new Dictionary<string, long>();
        /// <summary> alpha and offset of the returned wrap (absolute) </summary>
        public double Alpha, Offset;
        /// <summary> the alpha the bound allows for all pins (before any fallback) </summary>
        public double BoundAlpha;
        /// <summary> the estimated size of the wrap at BoundAlpha (MaxTriangles is compared with it) </summary>
        public double EstimatedTriangles;
        /// <summary> the fallback steps taken (0: the first wrap, at BoundAlpha, passed) </summary>
        public int FallbackSteps;
        /// <summary> the returned wrap is the simplified one </summary>
        public bool Simplified;
        /// <summary>
        /// true if the premises of the a priori bound held for the returned wrap: wrap status Success (no point limit), no rejected
        /// Steiner points, no box corner pinch points (CornerPinchPoints = 0). If false the bound is not a proof for this run (the
        /// measured intervals still are the answer).
        /// </summary>
        public bool BoundPremisesHeld;
        /// <summary> (diagnostics) the check failed for the first wrap, at the bound (then FallbackSteps &gt; 0 or the status is PassageNotKept) </summary>
        public bool FirstAttemptFailedCheck;
        public PassagePinResult[] PinResults = new PassagePinResult[0];
        /// <summary> all pins passed (certified); true exactly for Success </summary>
        public bool AllPassed { get { return PinResults.Length > 0 && PinResults.All(p => p.Passed); } }

        /// <summary> (tests) multiplies BoundAlpha for the first attempt, to exercise the fallback and its limit </summary>
        internal double FirstAlphaScale = 1;

        // the safety factor of the bound against rounding of the carving's bisection (1e-12 relative would do; this is plain)
        const double BoundSafety = 0.999;
        // the branch and bound stops at this fraction of the pin radius
        const double ClearanceTolerance = 1e-3;

        public PassageWrap(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        /// <summary>
        /// The largest alpha for which a pin of radius pinRadius is kept clear at an input clearance r with offset alpha / offsetRatio
        /// (or 2 alpha / offsetRatio with pulled points): the root of (r - k a)^2 - a^2 = p^2 with k = 1 / offsetRatio (2 / offsetRatio),
        /// 0 if r &lt;= p. Closed form: a = (-r k + sqrt(r^2 k^2 + (1 - k^2)(r^2 - p^2))) / (1 - k^2).
        /// </summary>
        public static double MaxAlpha(double inputClearance, double pinRadius, double offsetRatio, bool pullPinchPoints = false)
        {
            double k = (pullPinchPoints ? 2.0 : 1.0) / offsetRatio;
            if (!(inputClearance > pinRadius) || !(k > 0) || !(k < 1))
                return 0;
            double r = inputClearance, p = pinRadius;
            return (-r * k + Math.Sqrt(r * r * k * k + (1 - k * k) * (r * r - p * p))) / (1 - k * k);
        }

        /// <summary>
        /// The certified distance of a polyline to the triangles of a mesh: [lo, hi] with lo at most the true minimum, hi attained
        /// (up to a rounding margin of 1e-12 of the larger of the mesh and polyline size), hi - lo at most tolerance unless lo already
        /// exceeds stopAbove or the best value found is below stopBelow (a pin that fails needs no more precision). The distance to a mesh is 1-Lipschitz along the polyline, so an interval of length L with end values
        /// fa, fb has the lower bound (fa + fb - L) / 2; intervals are split until that is within the tolerance of the best value
        /// found. Cost: about the polyline length over the tolerance queries on a straight bore (each a nearest triangle query).
        /// </summary>
        internal static void PolylineClearance(DMeshAABBTree3 tree, IList<Vector3d> axis, double tolerance, double stopAbove,
            out double lo, out double hi, Func<bool> cancelF = null, double stopBelow = 0)
        {
            AxisAlignedBox3d box = tree.Mesh.CachedBounds;
            foreach (Vector3d p in axis) box.Contain(p);
            double margin = 1e-12 * box.DiagonalLength;
            // (a tolerance below 1e-4 of the size would cost hundreds of thousands of queries along a long straight bore)
            tolerance = Math.Max(tolerance, 1e-4 * box.DiagonalLength);
            Func<Vector3d, double> f = (p) => { tree.FindNearestTriangle(p, out double dsq); return Math.Sqrt(dsq); };
            var pq = new MinHeap();
            double best = double.MaxValue;
            var values = new double[axis.Count];
            for (int i = 0; i < axis.Count; ++i) {
                values[i] = f(axis[i]);
                best = Math.Min(best, values[i]);
            }
            for (int i = 0; i + 1 < axis.Count; ++i) {
                double len = (axis[i + 1] - axis[i]).Length;
                pq.Enqueue((axis[i], axis[i + 1], values[i], values[i + 1]), (values[i] + values[i + 1] - len) / 2);
            }
            double low = best;
            int pops = 0;
            while (pq.TryDequeue(out var it, out double lb)) {
                if (lb >= best - tolerance || lb > stopAbove || best < stopBelow) { low = Math.Min(best, lb); break; }
                if ((++pops & 255) == 0 && cancelF != null && cancelF()) { low = Math.Min(best, lb); break; }
                Vector3d m = 0.5 * (it.a + it.b);
                double fm = f(m);
                best = Math.Min(best, fm);
                pq.Enqueue((it.a, m, it.fa, fm), (it.fa + fm - (m - it.a).Length) / 2);
                pq.Enqueue((m, it.b, fm, it.fb), (fm + it.fb - (it.b - m).Length) / 2);
            }
            lo = Math.Max(0, low - margin);
            hi = best + margin;
        }

        /// <summary> computes Result; true on Success (see Status) </summary>
        public bool Compute()
        {
            Result = null; Error = null; PinResults = new PassagePinResult[0]; WrapCounts = new Dictionary<string, long>();
            Alpha = Offset = BoundAlpha = EstimatedTriangles = 0; FallbackSteps = 0; Simplified = false; BoundPremisesHeld = false; FirstAttemptFailedCheck = false;
            WrapStatus = MeshAlphaWrap.WrapStatus.Success;
            Status = validate();
            if (Status != PassageStatus.Success)
                return false;

            var inputTree = new DMeshAABBTree3(Mesh, true);
            var results = new PassagePinResult[Pins.Count];
            double bound = double.MaxValue;
            for (int i = 0; i < Pins.Count; ++i) {
                PassagePin pin = Pins[i];
                PolylineClearance(inputTree, pin.Axis, 1e-4 * pin.Radius, double.MaxValue, out double lo, out double hi, CancelF);
                if (CancelF()) { Status = PassageStatus.Cancelled; return false; }
                results[i] = new PassagePinResult { InputClearance = lo };
                double a = MaxAlpha(lo, pin.Radius, OffsetRatio, PullPinchPoints);
                if (!(a > 0)) {
                    PinResults = results;
                    Error = "pin " + i + ": radius " + pin.Radius + " is not below the input's clearance " + lo;
                    Status = PassageStatus.PinBlockedByInput;
                    return false;
                }
                bound = Math.Min(bound, a);
            }
            bound *= BoundSafety;
            // (a wrap coarser than the input's size is the hull of it: no use)
            bound = Math.Min(bound, Mesh.CachedBounds.DiagonalLength);
            if (MaxAlphaLimit > 0)
                bound = Math.Min(bound, MaxAlphaLimit);
            PinResults = results;
            BoundAlpha = bound;
            EstimatedTriangles = estimate(bound);
            if (MaxTriangles > 0 && EstimatedTriangles > MaxTriangles) {
                Status = PassageStatus.BudgetExceeded;
                Error = "estimated " + EstimatedTriangles.ToString("F0") + " triangles at alpha " + bound + ", budget " + MaxTriangles;
                return false;
            }

            double alpha = bound * FirstAlphaScale;
            for (int step = 0; step <= MaxFallbackSteps; ++step, alpha *= FallbackFactor) {
                if (CancelF()) { Status = PassageStatus.Cancelled; return false; }
                double offset = alpha / OffsetRatio;
                var wrap = new MeshAlphaWrap(Mesh) { Alpha = alpha, Offset = offset, MaxThreads = MaxThreads, PullPinchPoints = PullPinchPoints, CancelF = CancelF };
                bool ok = wrap.Compute();
                WrapStatus = wrap.Status;
                WrapCounts = new Dictionary<string, long>(wrap.Counts);
                if (wrap.Status == MeshAlphaWrap.WrapStatus.Cancelled) { Status = PassageStatus.Cancelled; return false; }
                if (wrap.Result == null || !(ok || wrap.Status == MeshAlphaWrap.WrapStatus.PointLimitReached)) {
                    Status = PassageStatus.WrapFailed;
                    Error = wrap.Error ?? wrap.Status.ToString();
                    return false;
                }
                FallbackSteps = step;
                Alpha = alpha; Offset = offset;
                BoundPremisesHeld = wrap.Status == MeshAlphaWrap.WrapStatus.Success && wrap.CornerPinchPoints == 0
                    && (!wrap.Counts.TryGetValue("rejected_points", out long rej) || rej == 0);

                DMesh3 chosen = wrap.Result;
                PassagePinResult[] measured = measure(chosen, results);
                bool passed = measured.All(p => p.Passed);
                if (step == 0 && !passed) FirstAttemptFailedCheck = true;
                Simplified = false;
                if (passed && Simplify) {
                    DMesh3 simple = null;
                    try {
                        simple = MeshRobustRepair.SimplifyWithin(chosen, 0.5 * offset, 0.5 * offset, true, CancelF, out int _,
                            MeshRobustRepair.WrapSimplifyMinQuality, true, MeshRobustRepair.WrapSimplifySlabs, MaxThreads, null, Mesh);
                    } catch (OperationCanceledException) { }
                    if (simple != null) {
                        PassagePinResult[] m2 = measure(simple, results);
                        if (m2.All(p => p.Passed)) { chosen = simple; measured = m2; Simplified = true; }
                    }
                }
                Result = chosen;
                PinResults = measured;
                if (passed) {
                    Status = wrap.Status == MeshAlphaWrap.WrapStatus.Success || wrap.Status == MeshAlphaWrap.WrapStatus.PointLimitReached
                        ? PassageStatus.Success : PassageStatus.WrapFailed;
                    return Status == PassageStatus.Success;
                }
            }
            Status = PassageStatus.PassageNotKept;
            Error = "the pins are not clear in the wrap after " + MaxFallbackSteps + " fallback steps";
            return false;
        }

        // the size estimate of MeshAlphaWrap with a budget (area, open components twice), at alpha
        double estimate(double alpha)
        {
            var tris = new List<Vector3d>(3 * Mesh.TriangleCount);
            double area = 0;
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tri = Mesh.GetTriangle(t);
                Vector3d a = Mesh.GetVertex(tri.a), b = Mesh.GetVertex(tri.b), c = Mesh.GetVertex(tri.c);
                tris.Add(a); tris.Add(b); tris.Add(c);
                area += 0.5 * (b - a).Cross(c - a).Length;
            }
            return MeshAlphaWrap.EstimateDensity * (area + AlphaWrapEstimate.OpenArea(tris, alpha)) / alpha / alpha;
        }

        // a binary min-heap of intervals by lower bound (netstandard2.0 has no PriorityQueue); ties by insertion order, so the
        // search is deterministic
        sealed class MinHeap
        {
            readonly List<(double key, long seq, (Vector3d a, Vector3d b, double fa, double fb) item)> h = new List<(double, long, (Vector3d, Vector3d, double, double))>();
            long seq;
            static bool Less((double key, long seq, (Vector3d a, Vector3d b, double fa, double fb) item) x, (double key, long seq, (Vector3d a, Vector3d b, double fa, double fb) item) y)
            { return x.key < y.key || (x.key == y.key && x.seq < y.seq); }
            public void Enqueue((Vector3d a, Vector3d b, double fa, double fb) item, double key)
            {
                h.Add((key, seq++, item));
                int i = h.Count - 1;
                while (i > 0) {
                    int parent = (i - 1) / 2;
                    if (!Less(h[i], h[parent])) break;
                    var t = h[i]; h[i] = h[parent]; h[parent] = t; i = parent;
                }
            }
            public bool TryDequeue(out (Vector3d a, Vector3d b, double fa, double fb) item, out double key)
            {
                if (h.Count == 0) { item = default; key = 0; return false; }
                item = h[0].item; key = h[0].key;
                var last = h[h.Count - 1]; h.RemoveAt(h.Count - 1);
                if (h.Count > 0) {
                    h[0] = last;
                    int i = 0;
                    while (true) {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < h.Count && Less(h[l], h[m])) m = l;
                        if (r < h.Count && Less(h[r], h[m])) m = r;
                        if (m == i) break;
                        var t = h[i]; h[i] = h[m]; h[m] = t; i = m;
                    }
                }
                return true;
            }
        }

        PassagePinResult[] measure(DMesh3 wrap, PassagePinResult[] inputResults)
        {
            var tree = new DMeshAABBTree3(wrap, true);
            var res = new PassagePinResult[Pins.Count];
            for (int i = 0; i < Pins.Count; ++i) {
                PassagePin pin = Pins[i];
                PolylineClearance(tree, pin.Axis, ClearanceTolerance * pin.Radius, pin.Radius, out double lo, out double hi, CancelF, pin.Radius);
                bool outside = true;
                foreach (Vector3d p in pin.Axis)
                    if (!(Math.Abs(tree.WindingNumber(p)) < 0.5)) { outside = false; break; }
                res[i] = new PassagePinResult {
                    InputClearance = inputResults[i].InputClearance, ClearanceLow = lo, ClearanceHigh = hi, AxisOutside = outside,
                    Passed = outside && lo >= pin.Radius
                };
            }
            return res;
        }

        PassageStatus validate()
        {
            if (Mesh == null || Mesh.TriangleCount == 0) { Error = "no input triangles"; return PassageStatus.InvalidInput; }
            foreach (int v in Mesh.VertexIndices()) {
                Vector3d p = Mesh.GetVertex(v);
                if (double.IsNaN(p.x + p.y + p.z) || double.IsInfinity(p.x + p.y + p.z)) { Error = "non-finite input coordinates"; return PassageStatus.InvalidInput; }
            }
            if (Pins == null || Pins.Count == 0) { Error = "no pins"; return PassageStatus.InvalidPins; }
            for (int i = 0; i < Pins.Count; ++i) {
                PassagePin pin = Pins[i];
                if (pin == null || pin.Axis == null || pin.Axis.Count == 0) { Error = "pin " + i + " has no axis"; return PassageStatus.InvalidPins; }
                if (!(pin.Radius > 0) || double.IsInfinity(pin.Radius)) { Error = "pin " + i + ": the radius must be positive and finite"; return PassageStatus.InvalidPins; }
                foreach (Vector3d p in pin.Axis)
                    if (double.IsNaN(p.x + p.y + p.z) || double.IsInfinity(p.x + p.y + p.z)) { Error = "pin " + i + ": non-finite axis point"; return PassageStatus.InvalidPins; }
            }
            if (!(OffsetRatio >= 3) || double.IsInfinity(OffsetRatio)) { Error = "OffsetRatio must be at least 3"; return PassageStatus.InvalidPins; }
            if (!(FallbackFactor > 0 && FallbackFactor < 1) || MaxFallbackSteps < 0) { Error = "bad fallback options"; return PassageStatus.InvalidPins; }
            return PassageStatus.Success;
        }
    }
}
