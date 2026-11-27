using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary> the way a SolidBooleanPipeline run got to the output (the steps of Report.Route, reduced to the ones that decide what a certificate can say) </summary>
    internal enum PipelineRoute
    {
        /// <summary> the inputs as passed went into one ExactMeshBoolean (what phase 1 runs) </summary>
        Direct,
        /// <summary> the inputs snapped to a grid, then one ExactMeshBoolean: the certificate is about the snapped inputs </summary>
        GridRetry,
        /// <summary> an open tool used as a sheet: the exact stage has open input (generalized winding numbers), BooleanCertificate does not apply </summary>
        SheetRole,
        /// <summary> an open input was closed on a copy (MeshAutoRepair / MeshRobustRepair): the certificate is about the repaired copy </summary>
        InputRepair,
        /// <summary> the sheet role refused and the inputs went in as solids (a second exact stage, certifiable as Direct) </summary>
        Fallback,
        /// <summary> an alpha wrap or another route without an exact arrangement: nothing to certify </summary>
        Wrap
    }

    /// <summary>
    /// Certified: every clause holds. CertifiedWithRemovals: every clause holds, but the rounding removed closed parts of the exact result
    /// (listed in Caveats; BooleanCertificate.Check calls that "not clean"): not a plain Certified. NotCertifiable: the certificate does not
    /// apply or cannot decide. Rejected: a clause failed.
    /// </summary>
    internal enum PipelineVerdictKind { Certified, CertifiedWithRemovals, NotCertifiable, Rejected }

    internal static class PipelineVerdictKindExtensions
    {
        /// <summary> Certified or CertifiedWithRemovals: no clause failed and the certificate applies </summary>
        public static bool IsCertified(this PipelineVerdictKind k) => k == PipelineVerdictKind.Certified || k == PipelineVerdictKind.CertifiedWithRemovals;
    }

    /// <summary>
    /// What the exact stage of one pipeline run hands to the certificate. This is the abstraction that phase 2's hook in
    /// SolidBooleanPipeline fills: for phase 1 DirectRun does it for a plain ExactMeshBoolean (the pipeline's direct route
    /// without input repair).
    /// </summary>
    internal interface IExactStageRun
    {
        PipelineRoute Route { get; }
        /// <summary> the ExactMeshBoolean that produced the output, after Compute (RecordCertificate and ComputeExactVertices were on) </summary>
        ExactMeshBoolean Boolean { get; }
        /// <summary> the inputs as the caller passed them to the pipeline (before repair, snap, reorientation) </summary>
        IReadOnlyList<DMesh3> OriginalInputs { get; }
        /// <summary> the largest distance the route moved any input point from the original (grid snap), 0 on the direct route </summary>
        double InputDisplacement { get; }
    }

    /// <summary> the pipeline's direct route, run here: one ExactMeshBoolean with the pipeline's own flags (StrictRounding, closed inputs only) and the recording </summary>
    internal sealed class DirectRun : IExactStageRun
    {
        /// <summary> Direct unless a test or a corpus labels the run with another route (the verdict depends on it) </summary>
        public PipelineRoute Route { get; set; } = PipelineRoute.Direct;
        public ExactMeshBoolean Boolean { get; private set; }
        public IReadOnlyList<DMesh3> OriginalInputs { get; private set; }
        public double InputDisplacement => 0;
        public bool Succeeded;
        public string Failure = "";
        public double BooleanMs;

        /// <summary>
        /// Runs the operation like SolidBooleanPipeline.runBoolean does (AllowOpenMeshes false, StrictRounding true) with the
        /// recording on. cullIntersection false leaves the early-out of an Intersection on, which the checker does not apply to
        /// (the verdict is then NotCertifiable); true (the default) turns it off, which costs the boolean some work.
        /// </summary>
        public static DirectRun Compute(IList<DMesh3> inputs, Ops op, bool certifiableIntersection = true, Action<ExactMeshBoolean> configure = null)
        {
            var run = new DirectRun { OriginalInputs = inputs.ToList() };
            var b = new ExactMeshBoolean(inputs.ToList(), op) { RecordCertificate = true, ComputeExactVertices = true, StrictRounding = true, AllowOpenMeshes = false };
            if (op == Ops.Intersection && certifiableIntersection) b.CullOutsideA = false;
            configure?.Invoke(b);
            var sw = Stopwatch.StartNew();
            run.Succeeded = b.Compute();
            run.BooleanMs = sw.Elapsed.TotalMilliseconds;
            run.Boolean = b;
            if (!run.Succeeded) run.Failure = b.Status + (string.IsNullOrEmpty(b.Error) ? "" : ": " + b.Error);
            return run;
        }
    }

    /// <summary> Certified, NotCertifiable(route) or Rejected(clause, numbers) </summary>
    internal sealed class PipelineVerdict
    {
        public PipelineVerdictKind Kind;
        public PipelineRoute Route;
        /// <summary> Rejected: the clause that failed (C0 .. C5, NotClean, NoInventedSurface, RoundingVolume, InputsFed); NotCertifiable: the reason's key (route, n/a, no-result) </summary>
        public string Clause = "";
        public string Detail = "";
        /// <summary> Rejected: the numbers of the clause (NoInventedSurface: share, area, triangles, max distance, tolerance; RoundingVolume: before, after, change, limit) </summary>
        public double[] Numbers = new double[0];
        /// <summary> Certified, with what the certificate does not cover (the rounding removed parts; the inputs the route altered) </summary>
        public List<string> Caveats = new List<string>();
        public double C0Ms, C1Ms, C2Ms, C3Ms, C5Ms, NotCleanMs, RoundingChecksMs, FedMs;
        /// <summary> the sum of the exact-stage clauses (what BooleanCertificate.Check costs) </summary>
        public double CertificateMs => C0Ms + C1Ms + C2Ms + C3Ms + C5Ms;
        public double TotalMs => CertificateMs + NotCleanMs + RoundingChecksMs + FedMs;
        public BooleanCertificate Certificate;

        public override string ToString()
        {
            string s = Kind == PipelineVerdictKind.NotCertifiable ? $"NotCertifiable({Route}: {Clause})"
                : Kind == PipelineVerdictKind.Rejected ? $"Rejected({Clause}: {string.Join(", ", Numbers.Select(n => n.ToString("G4", CultureInfo.InvariantCulture)))})" : Kind.ToString();
            return Detail == "" ? s : s + " " + Detail;
        }
    }

    /// <summary>
    /// The numbers the rounding checks need that are not in the BooleanCertificate: the original inputs and the volumes the
    /// rounding repair recorded. A seeded-defect test changes them (or the BooleanCertificate it holds) before Judge.
    /// </summary>
    internal sealed class PipelineEvidence
    {
        public PipelineRoute Route;
        public BooleanCertificate Certificate;
        public IReadOnlyList<DMesh3> OriginalInputs;
        /// <summary> the inputs the exact stage got (ExactMeshBoolean.Meshes) </summary>
        public IReadOnlyList<DMesh3> FedInputs;
        public double InputDisplacement;
        /// <summary> the triangles of the inputs as passed that the route reports as dropped on purpose (an explained drop); any other missing triangle on a route other than Direct is NotCertifiable </summary>
        public int ExplainedDroppedTriangles;
        /// <summary> the number of closed components the rounding repair listed as removed (Rounding.RemovedComponentInfos.Count) </summary>
        public int RemovedComponentInfoCount;
        public double RoundingVolumeBefore, RoundingVolumeAfter;
        public bool RoundingRan;
        /// <summary> the bound MeshRoundingRepair applied (Rounding.MaxLength), 0 when no rounding ran </summary>
        public double RoundingBound;
        public string NotApplicableReason;

        public static PipelineEvidence From(IExactStageRun run)
        {
            ExactMeshBoolean b = run.Boolean;
            var e = new PipelineEvidence { Route = run.Route, OriginalInputs = run.OriginalInputs, InputDisplacement = run.InputDisplacement };
            if (b == null || b.Result == null) { e.NotApplicableReason = "the exact stage has no result"; return e; }
            if (b.CertificateFront == null || b.Arrangement == null) { e.NotApplicableReason = "the exact stage ran without RecordCertificate"; return e; }
            e.Certificate = BooleanCertificate.From(b);
            e.FedInputs = e.Certificate.Inputs;
            if (b.Rounding != null) {
                e.RoundingRan = true;
                e.RoundingBound = b.Rounding.MaxLength;
                e.RemovedComponentInfoCount = b.Rounding.RemovedComponentInfos.Count;
                e.RoundingVolumeBefore = b.Rounding.VolumeBefore;
                e.RoundingVolumeAfter = b.Rounding.VolumeAfter;
            }
            return e;
        }
    }

    /// <summary>
    /// The certificate of a run of the pipeline's exact stage against its inputs, without a reference result
    /// (docs/research/PIPELINE_CERTIFICATE_SPIKE_2026-11.md). It composes: the exact stage's BooleanCertificate (C0, C1, C2, C3,
    /// C4, C5 with the rounding bound), MeshRobustRepair.NotCleanReason on the rounded output, the rounding checks of #1488 (reimplemented
    /// here, PROVISIONAL until #1488 is on master), and the check that the inputs the exact stage got are the inputs as passed.
    /// </summary>
    internal static class PipelineCertificate
    {
        public static PipelineVerdict Certify(IExactStageRun run) => Judge(PipelineEvidence.From(run));

        /// <summary> the verdict on the evidence (separate from Certify so that a test can change the evidence first) </summary>
        public static PipelineVerdict Judge(PipelineEvidence e)
        {
            var v = new PipelineVerdict { Route = e.Route, Certificate = e.Certificate };
            if (e.Route == PipelineRoute.Wrap) return NotCertifiable(v, "route", "an output of a route without an exact arrangement");
            if (e.NotApplicableReason != null) return NotCertifiable(v, "no-result", e.NotApplicableReason);
            BooleanCertificate c = e.Certificate;
            if (c.NotApplicableReason != null) return NotCertifiable(v, "n/a", c.NotApplicableReason);

            // the inputs the exact stage got against the inputs as passed
            var sw = Stopwatch.StartNew();
            string fed = InputsFedReason(e.OriginalInputs, e.FedInputs, out bool identical, out int missing, out int extra, e.Route == PipelineRoute.GridRetry ? e.InputDisplacement : 0);
            v.FedMs = sw.Elapsed.TotalMilliseconds;
            if (fed != "" && e.Route == PipelineRoute.Direct) return Reject(v, "InputsFed", fed);
            // (a route that changes the inputs may add surface, which NoInventedSurface sees; it must not lose surface that nothing explains:
            // lost surface of an input is invisible to every later clause)
            if (missing > e.ExplainedDroppedTriangles) return NotCertifiable(v, "inputs-altered", $"the route lost input surface: {fed}");
            // (a GridRetry whose triangles all match within the displacement is `identical`, yet the exact stage got snapped meshes: the caveat stays)
            bool snapped = e.Route == PipelineRoute.GridRetry && !SameMeshes(e.OriginalInputs, e.FedInputs);
            if (!identical || snapped) v.Caveats.Add($"the exact stage's inputs differ from the inputs as passed ({(fed != "" ? fed : "moved by at most " + e.InputDisplacement.ToString("G4", CultureInfo.InvariantCulture))}): the certificate is about the inputs it got");

            // the exact stage, clause by clause in the order of BooleanCertificate.Check (timed)
            string r;
            if (Time(v, ref v.C0Ms, c.CheckInputs, out r)) return Reject(v, "C0", r);
            if (Time(v, ref v.C1Ms, c.CheckPieces, out r)) return Reject(v, "C1", r);
            if (Time(v, ref v.C2Ms, c.CheckEmbedding, out r)) return Reject(v, "C2", r);
            if (Time(v, ref v.C3Ms, c.CheckClassification, out r)) return Reject(v, "C3/C4", r);
            if (Time(v, ref v.C5Ms, c.CheckResult, out r)) return Reject(v, "C5", r);
            // (a triangle that no single triangle covers within Delta is split further, up to depth 12; what is still not covered is undecided, not a violation)
            for (int depth = c.SplitDepth + 3; c.Uncovered != 0 && depth <= 12; depth += 3) {
                c.SplitDepth = depth;
                double ms = 0;
                bool bad = Time(v, ref ms, c.CheckResult, out r);
                v.C5Ms += ms;
                if (bad) return Reject(v, "C5", r);
            }
            if (c.Uncovered != 0) return NotCertifiable(v, "coverage-undecided", $"{c.Uncovered} triangles of the result are not covered within Delta by any one triangle of the result before rounding after the splits (the checker's limit, not a violation)");
            // (winding numbers of one input outside {0, 1}: an input that overlaps itself or is inside out. The exact stage takes the solid as
            // "winding number > 0" and the certificate is about that; whether it is what the caller meant is not checkable, so it is a caveat)
            string winding = WindingOutsideZeroOne(c);
            if (winding != "") v.Caveats.Add(winding);
            // (the solids the rounding removed: listed by the checker, never silently exempt. CheckResult skips every thin or tiny component of the
            // exact result once the rounding removed any, so the skipped components must be no more than the removed ones, else a second thin part
            // lost by a defect would hide behind the first removal)
            foreach (string part in c.RemovedParts) v.Caveats.Add("rounding removed: " + part);
            if (c.RemovedParts.Count > 0 && (c.ExemptComponents > c.RemovedComponents || c.RemovedComponents != e.RemovedComponentInfoCount))
                return NotCertifiable(v, "removals-unmatched", $"{c.ExemptComponents} thin or tiny components of the exact result are left out of the coverage, the rounding removed {c.RemovedComponents} ({e.RemovedComponentInfoCount} listed)");

            sw.Restart();
            string notClean = MeshRobustRepair.NotCleanReason(c.Result);
            v.NotCleanMs = sw.Elapsed.TotalMilliseconds;
            // (an empty result is clean where the exact result is empty too: the intersection of disjoint solids; where the exact result is not, the rounding removed all of it)
            if (notClean == "empty" && c.Assembled != null && c.Assembled.TriangleCount == 0) notClean = "";
            if (notClean == "self-intersections") {
                // (contacts: the solids touch at a point or along an edge, the result is manifold with duplicated vertices and NotCleanReason reports it;
                // the pipeline's own final check tells a contact alone from a crossing, which it keeps as OnlyContacts)
                var report = new SolidBooleanReport();
                SolidBooleanPipeline.FinalCheck(c.Result, report);
                if (report.OnlyContacts) { v.Caveats.Add($"contacts: {report.ContactsResult} positions where the solid touches itself (NotCleanReason: self-intersections, no triangle crosses another)"); notClean = ""; }
                else notClean += report.SelfIntersectingTriangles < 0 ? " (more than " + SolidBooleanPipeline.FinalCheckPairCap + " pairs)" : $" ({report.SelfIntersectingTriangles} crossing triangles, {report.ContactsResult} contacts)";
            }
            if (notClean != "") return Reject(v, "NotClean", notClean + (c.RemovedParts.Count > 0 ? "; the rounding removed " + c.RemovedParts.Count + " part(s) of the exact result" : ""));

            // the rounding checks of #1488 (provisional)
            sw.Restart();
            try {
                InventedSurface inv = NoInventedSurface(c.Result, e.OriginalInputs, Math.Max(e.RoundingBound, 0), e.InputDisplacement);
                if (inv.Share > 0) return Reject(v, "NoInventedSurface", $"{inv.Triangles} triangles, area {inv.Area:G4}, farthest sample {inv.MaxDistance:G4} > tolerance {inv.Tolerance:G4}",
                    inv.Share, inv.Area, inv.Triangles, inv.MaxDistance, inv.Tolerance);
                if (e.RoundingRan) {
                    RoundingVolumeResult vol = RoundingVolume(e.RoundingVolumeBefore, e.RoundingVolumeAfter, e.FedInputs ?? e.OriginalInputs, c.Result);
                    if (vol.Refused) return Reject(v, "RoundingVolume", $"volume {vol.Before:G6} -> {vol.After:G6}, change {vol.Change:G4} > {vol.Limit:G4} and above the floor {vol.Floor:G4}",
                        vol.Before, vol.After, vol.Change, vol.Limit, vol.Floor);
                }
            } finally { v.RoundingChecksMs = sw.Elapsed.TotalMilliseconds; }
            v.Kind = c.RemovedParts.Count > 0 ? PipelineVerdictKind.CertifiedWithRemovals : PipelineVerdictKind.Certified;
            return v;
        }

        static PipelineVerdict NotCertifiable(PipelineVerdict v, string key, string detail) { v.Kind = PipelineVerdictKind.NotCertifiable; v.Clause = key; v.Detail = detail; return v; }
        static PipelineVerdict Reject(PipelineVerdict v, string clause, string detail, params double[] numbers)
        {
            v.Kind = PipelineVerdictKind.Rejected; v.Clause = clause; v.Detail = detail; v.Numbers = numbers; return v;
        }

        static bool Time(PipelineVerdict v, ref double ms, Func<string> clause, out string result)
        {
            var sw = Stopwatch.StartNew();
            try { result = clause(); } finally { ms = sw.Elapsed.TotalMilliseconds; }
            return result != "";
        }

        /// <summary> "" or the first input mesh whose winding number outside {0, 1} the witness shows (front or back of a classified piece) </summary>
        public static string WindingOutsideZeroOne(BooleanCertificate c)
        {
            for (int i = 0; i < c.PieceCorners.Length; ++i) {
                if (c.Front[i] == null || !c.IsClassified(i)) continue;
                foreach (int[] w in new[] { c.Front[i], c.Back(i) })
                    for (int m = 0; m < w.Length; ++m)
                        if (w[m] < 0 || w[m] > 1) return $"input {m} has winding number {w[m]} next to piece {i}: it overlaps itself or is inside out; the solid is taken as winding number > 0 (the intent is not checkable)";
            }
            return "";
        }

        // ------------------------------------------------------------------ the inputs as passed against the inputs the exact stage got

        static bool SameMeshes(IReadOnlyList<DMesh3> a, IReadOnlyList<DMesh3> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; ++i) if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        // the triangle as a key: its corners as doubles, rotated so that the smallest corner is first (the orientation is kept)
        static (double, double, double, double, double, double, double, double, double) Key(DMesh3 m, int t)
        {
            Index3i tri = m.GetTriangle(t);
            Vector3d a = m.GetVertex(tri.a), b = m.GetVertex(tri.b), c = m.GetVertex(tri.c);
            int ia = Less(b, a) ? (Less(c, b) ? 2 : 1) : (Less(c, a) ? 2 : 0);
            Vector3d[] p = { a, b, c };
            Vector3d x = p[ia], y = p[(ia + 1) % 3], z = p[(ia + 2) % 3];
            return (x.x, x.y, x.z, y.x, y.y, y.z, z.x, z.y, z.z);
        }
        static bool Less(Vector3d p, Vector3d q) => p.x != q.x ? p.x < q.x : p.y != q.y ? p.y < q.y : p.z < q.z;

        public static string InputsFedReason(IReadOnlyList<DMesh3> original, IReadOnlyList<DMesh3> fed, out bool identical) =>
            InputsFedReason(original, fed, out identical, out _, out _, 0);

        /// <summary>
        /// "" if every mesh of fed has the triangles (by coordinates, orientation included) of the mesh of original at its index and
        /// no other, else how many are missing and extra. identical: the same mesh objects, or the same triangles. tolerance above 0
        /// (GridRetry): a triangle matches one whose corners are all within the tolerance of its corners, in the same orientation.
        /// The pipeline's input repair, grid snap and drop of components all show here; the direct route has none of them. A missing
        /// list (null) is no evidence that the inputs were fed: it is a reason, and counts as one missing triangle.
        /// </summary>
        public static string InputsFedReason(IReadOnlyList<DMesh3> original, IReadOnlyList<DMesh3> fed, out bool identical, out int missing, out int extra, double tolerance)
        {
            identical = true; missing = 0; extra = 0;
            if (original == null || fed == null) { identical = false; missing = 1; return "the inputs as passed or the inputs fed are not recorded"; }
            if (original.Count != fed.Count) { identical = false; missing = 1; return $"{original.Count} inputs passed, {fed.Count} fed"; }
            for (int i = 0; i < original.Count; ++i) {
                if (ReferenceEquals(original[i], fed[i])) continue;
                if (tolerance > 0) { MatchWithin(original[i], fed[i], tolerance, ref missing, ref extra); continue; }
                var count = new Dictionary<(double, double, double, double, double, double, double, double, double), int>();
                foreach (int t in original[i].TriangleIndices()) { var k = Key(original[i], t); count[k] = count.TryGetValue(k, out int n) ? n + 1 : 1; }
                foreach (int t in fed[i].TriangleIndices()) {
                    var k = Key(fed[i], t);
                    if (count.TryGetValue(k, out int n) && n > 0) count[k] = n - 1; else extra++;
                }
                missing += count.Values.Sum();
            }
            identical = missing == 0 && extra == 0;
            return identical ? "" : $"{missing} triangles of the inputs as passed are not fed, {extra} fed triangles are not in them";
        }

        // the triangles of a and b matched one to one, a corner of a within tol (per coordinate) of the corner of b at the same place of a rotation of b; a spatial hash of the centroids of b
        static void MatchWithin(DMesh3 a, DMesh3 b, double tol, ref int missing, ref int extra)
        {
            double cell = Math.Max(4 * tol, double.Epsilon);
            Func<Vector3d, (long, long, long)> cellOf = p => ((long)Math.Floor(p.x / cell), (long)Math.Floor(p.y / cell), (long)Math.Floor(p.z / cell));
            var grid = new Dictionary<(long, long, long), List<int>>();
            var used = new HashSet<int>();
            foreach (int t in b.TriangleIndices()) {
                Index3i tri = b.GetTriangle(t);
                var key = cellOf((b.GetVertex(tri.a) + b.GetVertex(tri.b) + b.GetVertex(tri.c)) / 3);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(t);
            }
            Func<Vector3d, Vector3d, bool> near = (p, q) => Math.Abs(p.x - q.x) <= tol && Math.Abs(p.y - q.y) <= tol && Math.Abs(p.z - q.z) <= tol;
            foreach (int t in a.TriangleIndices()) {
                Index3i ta = a.GetTriangle(t);
                Vector3d[] pa = { a.GetVertex(ta.a), a.GetVertex(ta.b), a.GetVertex(ta.c) };
                var (cx, cy, cz) = cellOf((pa[0] + pa[1] + pa[2]) / 3);
                bool found = false;
                for (long dx = -1; dx <= 1 && !found; ++dx)
                    for (long dy = -1; dy <= 1 && !found; ++dy)
                        for (long dz = -1; dz <= 1 && !found; ++dz) {
                            if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                            foreach (int u in list) {
                                if (used.Contains(u)) continue;
                                Index3i tb = b.GetTriangle(u);
                                Vector3d[] pb = { b.GetVertex(tb.a), b.GetVertex(tb.b), b.GetVertex(tb.c) };
                                for (int k = 0; k < 3 && !found; ++k)
                                    if (near(pa[0], pb[k]) && near(pa[1], pb[(k + 1) % 3]) && near(pa[2], pb[(k + 2) % 3])) { found = true; used.Add(u); }
                                if (found) break;
                            }
                        }
                if (!found) missing++;
            }
            extra += b.TriangleCount - used.Count;
        }

        // ------------------------------------------------------------------ the rounding checks of #1488 (PROVISIONAL: reimplemented from the PR text, not its code)

        internal struct InventedSurface { public double Share, Area, MaxDistance, Tolerance; public int Triangles; public int ExactSearches; }

        static double Ulp(double x)
        {
            x = Math.Abs(x);
            if (x == 0) return double.Epsilon;
            // (the distance to the next double away from 0)
            long bits = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(bits + 1) - x;
        }

        static double MaxUsedCoordinate(IEnumerable<DMesh3> meshes)
        {
            double m = 0;
            foreach (DMesh3 mesh in meshes)
                foreach (int t in mesh.TriangleIndices()) {
                    Index3i tri = mesh.GetTriangle(t);
                    for (int k = 0; k < 3; ++k) { Vector3d p = mesh.GetVertex(tri[k]); m = Math.Max(m, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z)))); }
                }
            return m;
        }

        /// <summary>
        /// #1488 NoInventedSurface: every triangle of the result is sampled at its 3 vertices, centroid and 3 edge midpoints; it is
        /// invented when a sample is farther than the tolerance from every input as passed. Tolerance = 3 (max(the rounding's bound,
        /// 256 ulp of the largest used coordinate of the result and the inputs) + the displacement of the route's input snap).
        /// A sample that the tree shows within the tolerance (by the floating-point distance of its closest point, MeshQueries.TriangleDistance) is decided there; every other one by the
        /// nearest-by-closest-point search over all inputs (the AABB tree's ranking is noise next to a big face).
        /// </summary>
        public static InventedSurface NoInventedSurface(DMesh3 result, IReadOnlyList<DMesh3> inputs, double roundingBound, double inputDisplacement)
        {
            var all = new List<DMesh3>(inputs) { result };
            double tol = 3 * (Math.Max(roundingBound, 256 * Ulp(MaxUsedCoordinate(all))) + inputDisplacement);
            var trees = inputs.Where(m => m.TriangleCount > 0).Select(m => new DMeshAABBTree3(m, true)).ToList();
            var r = new InventedSurface { Tolerance = tol };
            double area = 0, total = 0;
            foreach (int t in result.TriangleIndices()) {
                Index3i tri = result.GetTriangle(t);
                Vector3d a = result.GetVertex(tri.a), b = result.GetVertex(tri.b), c = result.GetVertex(tri.c);
                double ar = 0.5 * (b - a).Cross(c - a).Length;
                total += ar;
                Vector3d[] samples = { a, b, c, (a + b + c) / 3, 0.5 * (a + b), 0.5 * (b + c), 0.5 * (c + a) };
                bool invented = false;
                foreach (Vector3d p in samples) {
                    double d = NearestInput(trees, p, tol, ref r.ExactSearches);
                    r.MaxDistance = Math.Max(r.MaxDistance, d);
                    if (!(d <= tol)) invented = true;
                }
                if (invented) { r.Triangles++; area += ar; }
            }
            r.Area = area;
            r.Share = total > 0 ? area / total : (r.Triangles > 0 ? 1 : 0);
            return r;
        }

        static double NearestInput(List<DMeshAABBTree3> trees, Vector3d p, double tol, ref int exact)
        {
            // fast path: some tree has a triangle within the tolerance by the (floating-point) distance of its closest point
            foreach (DMeshAABBTree3 tree in trees) {
                int t = tree.FindNearestTriangle(p, tol * 1.0000001 + double.Epsilon);
                if (t != DMesh3.InvalidID) {
                    double d = MeshQueries.TriangleDistance(tree.Mesh, t, p).DistanceSquared;
                    if (Math.Sqrt(d) <= tol) return Math.Sqrt(d);
                }
            }
            exact++;
            double best = double.MaxValue;
            foreach (DMeshAABBTree3 tree in trees) {
                int t = tree.FindNearestTriangleByClosestPoint(p, out double d);
                if (t != DMesh3.InvalidID) best = Math.Min(best, d);
            }
            return best;
        }

        internal struct RoundingVolumeResult { public bool Refused; public double Before, After, Change, Limit, Floor; }

        static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        public static double SignedVolume(DMesh3 m)
        {
            double v = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                v += m.GetVertex(tri.a).Dot(m.GetVertex(tri.b).Cross(m.GetVertex(tri.c)));
            }
            return v / 6;
        }

        /// <summary>
        /// #1488 RoundingVolume: dV = |after - before| of the rounding repair; refused when dV > limit x |before| (limit 1e-3) and dV >
        /// floor, floor = 1e-6 x the smallest |volume| of the winding-closed inputs (none: 1e-9 x diagonal^3 of the result). PROVISIONAL:
        /// without #1488's allowance for the signed change of thin parts, so it refuses at least what #1488 refuses.
        /// </summary>
        public static RoundingVolumeResult RoundingVolume(double before, double after, IReadOnlyList<DMesh3> inputs, DMesh3 result, double limit = 1e-3)
        {
            var r = new RoundingVolumeResult { Before = before, After = after, Limit = limit };
            double dv = Math.Abs(after - before);
            r.Change = Math.Abs(before) > 0 ? dv / Math.Abs(before) : (dv > 0 ? double.PositiveInfinity : 0);
            double smallest = double.MaxValue;
            foreach (DMesh3 m in inputs)
                if (m.TriangleCount > 0 && ExactMeshBoolean.IsWindingClosed(m)) {
                    double vol = Math.Abs(SignedVolume(m));
                    if (vol > 0) smallest = Math.Min(smallest, vol);
                }
            if (smallest < double.MaxValue) r.Floor = 1e-6 * smallest;
            else { double diag = result.TriangleCount > 0 ? result.CachedBounds.DiagonalLength : 0; r.Floor = 1e-9 * diag * diag * diag; }
            r.Refused = !(IsFinite(before) && IsFinite(after) && IsFinite(r.Floor)) || (r.Change > limit && dv > r.Floor);
            return r;
        }
    }
}
