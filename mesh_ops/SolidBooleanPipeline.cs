using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using g3;

namespace gs
{
    /// <summary>
    /// Why a SolidBooleanPipeline reduction stopped where it did (SolidBooleanReport.StopReason).
    /// </summary>
    public enum SolidBooleanStopReason
    {
        /// <summary> no reduction criterion was set: the exact boolean result is returned as it is </summary>
        NotReduced = 0,
        /// <summary> the target (triangle count, or no edge shorter than MinEdgeLength left, or the MaxQuadricError budget used up) was reached </summary>
        TargetReached = 1,
        /// <summary> the boolean result has no triangles (eg the intersection of disjoint meshes): nothing to reduce </summary>
        EmptyResult = 2,
        /// <summary> most blocked edges would have made a triangle intersect another one (Reducer.PreventSelfIntersections), eg thin walls or narrow gaps (a mesh at its minimum is reported as Exhausted) </summary>
        SelfIntersectionGuard = 3,
        /// <summary>
        /// as SelfIntersectionGuard, but at least half of the edges the guard blocks have an end where the result's solids touch
        /// (duplicated vertices of one position, SolidBooleanReport.ContactsResult): a collapse that keeps the surface on a
        /// contact would touch the other sheet there, and the guard refuses it. The contacts limit the reduction also
        /// through the other causes (the sheets along a contact line cannot follow the collapses next to it): the detail
        /// then says how many edges are at contacts
        /// </summary>
        Contacts = 4,
        /// <summary> most blocked edges would have moved the surface farther than MaxDeviation from the boolean result </summary>
        DeviationBound = 5,
        /// <summary> most blocked edges would have flipped or folded a triangle over, or made one below MinTriangleQuality </summary>
        TriangleQuality = 6,
        /// <summary> most blocked edges were not possible in the topology (the link condition: a collapse would pinch the surface) </summary>
        Topology = 7,
        /// <summary> no edge was blocked, or the result is a minimal mesh (4 triangles per connected component, a tetrahedron; the guard then refuses the last collapse): nothing left to collapse </summary>
        Exhausted = 8
    }

    /// <summary> one input that was not closed in the winding number sense and was repaired before the boolean </summary>
    public sealed class SolidBooleanInputRepair
    {
        /// <summary> index of the input </summary>
        public int Index;
        /// <summary> "MeshAutoRepair" or "MeshRobustRepair" (the repair that made the input closed) </summary>
        public string Method;
        /// <summary> triangles of the input and of its repaired copy </summary>
        public int TrianglesBefore, TrianglesAfter;
        /// <summary> time of the repair or repairs of this input, in milliseconds </summary>
        public long Milliseconds;
    }

    /// <summary>
    /// What a SolidBooleanPipeline run did: counts, time per stage, repaired inputs and the reason the reduction stopped.
    /// </summary>
    public sealed class SolidBooleanReport
    {
        /// <summary> triangles per input as given (before any repair) </summary>
        public int[] InputTriangles = new int[0];
        /// <summary> per input: ExactMeshBoolean.IsWindingClosed of the input as given </summary>
        public bool[] InputClosed = new bool[0];
        /// <summary> the inputs that were repaired, with the method (empty if none) </summary>
        public List<SolidBooleanInputRepair> RepairedInputs = new List<SolidBooleanInputRepair>();
        /// <summary> triangles of the exact boolean result (before the reduction) </summary>
        public int BooleanTriangles;
        /// <summary> the boolean's Contacts and NonManifoldEdges (touching solids, see ExactMeshBoolean.Contacts) </summary>
        public int ContactsBoolean, NonManifoldEdgesBoolean;
        /// <summary> the triangle count asked for (TargetTriangleCount, or TargetFraction of BooleanTriangles); 0 for the other criteria </summary>
        public int TargetTriangles;
        /// <summary> triangles of the returned mesh </summary>
        public int ResultTriangles;
        /// <summary> positions of the returned mesh where two or more vertices lie (touching solids); at most ContactsBoolean: with PreventSelfIntersections (the default) the reduction creates no new ones; tested on touching boxes </summary>
        public int ContactsResult;
        /// <summary> MeshRobustRepair.NotCleanReason of the returned mesh (self-intersection check on): "" if clean </summary>
        public string NotCleanReason = "";
        /// <summary>
        /// the returned mesh is clean (NotCleanReason == ""). A result with contacts is valid (manifold, with duplicated
        /// vertices where the solids touch) but NotCleanReason reports it as "self-intersections" at the touching points:
        /// with ContactsResult &gt; 0 look at that count before treating "self-intersections" as a defect (it is the last check of
        /// NotCleanReason, so closed, bowties, degenerate triangles and orientation have passed; a real self-intersection
        /// next to a contact is not told apart). An empty result is Status Success with NotCleanReason "empty" and Clean false
        /// </summary>
        public bool Clean { get { return NotCleanReason.Length == 0; } }
        /// <summary> why the reduction stopped (see SolidBooleanStopReason) </summary>
        public SolidBooleanStopReason StopReason = SolidBooleanStopReason.NotReduced;
        /// <summary> a sentence on the stop reason with the counts it is based on </summary>
        public string StopDetail = "";
        /// <summary>
        /// the edges left in the result whose last collapse attempt was refused, by cause: the self-intersection guard, MaxDeviation,
        /// flip / fold-over / triangle quality, topology (all other); 0 for all if the reduction reached its target or none ran.
        /// An edge that was never tried (the run stopped at the target) is in none of them
        /// </summary>
        public int BlockedByGuard, BlockedByDeviation, BlockedByQuality, BlockedByTopology;
        /// <summary> of the blocked edges, those with an end at a position where the result's solids touch (ContactsResult) </summary>
        public int BlockedAtContacts;
        /// <summary> with PairContacts: the collapses of an edge together with its twin edge (two edges each); 0 otherwise </summary>
        public int PairedCollapses;
        /// <summary>
        /// PairContactsMode Auto: the second reduction (pairing on) ran because the first result was not clean with contacts left; it
        /// is kept (PairPassKept) if its NotCleanReason is the same or empty and it has fewer triangles (or as many and fewer contacts). False if it did not run
        /// </summary>
        public bool PairPassRan, PairPassKept;
        /// <summary> with PairPassRan: triangles of the first (unpaired) reduction's result </summary>
        public int PairPassFirstTriangles;
        /// <summary> time of the second reduction (PairPassRan) with its final check, in milliseconds; part of TotalMilliseconds, not of ReductionMilliseconds </summary>
        public long PairPassMilliseconds;
        /// <summary> time per stage in milliseconds: the input checks, the input repairs, the boolean, the reduction, the final check, and the sum </summary>
        public long InputCheckMilliseconds, RepairMilliseconds, BooleanMilliseconds, ReductionMilliseconds, FinalCheckMilliseconds, TotalMilliseconds;

        // the fields a reduction sets (to put back those of the first reduction if the second one is not kept)
        internal SolidBooleanReport ReductionState()
        {
            return new SolidBooleanReport {
                StopReason = StopReason, StopDetail = StopDetail, TargetTriangles = TargetTriangles, PairedCollapses = PairedCollapses,
                BlockedByGuard = BlockedByGuard, BlockedByDeviation = BlockedByDeviation, BlockedByQuality = BlockedByQuality,
                BlockedByTopology = BlockedByTopology, BlockedAtContacts = BlockedAtContacts
            };
        }

        internal void SetReductionState(SolidBooleanReport s)
        {
            StopReason = s.StopReason; StopDetail = s.StopDetail; TargetTriangles = s.TargetTriangles; PairedCollapses = s.PairedCollapses;
            BlockedByGuard = s.BlockedByGuard; BlockedByDeviation = s.BlockedByDeviation; BlockedByQuality = s.BlockedByQuality;
            BlockedByTopology = s.BlockedByTopology; BlockedAtContacts = s.BlockedAtContacts;
        }
    }

    /// <summary>
    /// An opt-in pipeline for the workflow "exact boolean on triangle soups, then a clean, closed solid with few
    /// triangles" without a prophylactic MeshRobustRepair of the inputs. It chains existing parts and changes none of
    /// them:
    /// <list type="number">
    /// <item><description>Every input is checked with ExactMeshBoolean.IsWindingClosed (cheap; unwelded copies of a vertex count as one,
    /// so an STL-like soup of a closed solid is closed). Only if an input fails and the boolean refuses it as open (it
    /// reorients flipped triangles itself) is that input repaired, on a copy, with the cheapest repair that makes it closed:
    /// MeshAutoRepair, and only with InputRepair = AutoRepairThenRobust, MeshRobustRepair (which can resolve, solidify or
    /// wrap, so it changes the geometry more) as the fallback. RepairedInputs of the report names each repaired input and
    /// the method. With InputRepair = Refuse an open input fails the run with Status OpenInput.</description></item>
    /// <item><description>ExactMeshBoolean of all inputs (Union, Intersection or Difference: first minus the others), with AllowOpenMeshes = false and
    /// StrictRounding = true (ConfigureBoolean runs before these two are forced): the result is exact up to the
    /// rounding of the new vertices, within the relative bound of StrictRounding, or the run fails with BooleanFailed.</description></item>
    /// <item><description>One reduction with Reducer (PreventSelfIntersections, MaxDeviation if set, MinCollapseTriangleQuality =
    /// MinTriangleQuality): to a triangle count (TargetTriangleCount, or TargetFraction of the boolean result), to the
    /// edge length MinEdgeLength, or to the quadric error MaxQuadricError; at most one of them. Every collapse is
    /// checked exactly against all other triangles, so the reduction creates no intersection and no new contact; it can
    /// remove a contact of the boolean result (a contact vertex moved off the touching sheet), which ContactsResult
    /// shows. Vertices of the contacts are not frozen: they only block the collapses that would keep the surface on the
    /// touching sheet, and measured on touching boxes, spheres and cylinders that left the target reachable (the report
    /// counts the refusals at contacts).</description></item>
    /// <item><description>MeshRobustRepair.NotCleanReason of the returned mesh (Report.NotCleanReason, Report.Clean), and, if the target was
    /// not reached, the cause (Report.StopReason, StopDetail).</description></item>
    /// </list>
    /// Compute() can be called again (also after changing the settings); Result and Report belong to the last call.
    /// Inputs are not modified.
    /// </summary>
    public class SolidBooleanPipeline
    {
        /// <summary> what happens to an input that is not closed in the winding number sense </summary>
        public enum InputRepairModes
        {
            /// <summary> no repair: Compute() fails with Status OpenInput </summary>
            Refuse = 0,
            /// <summary> MeshAutoRepair on a copy of the input (default) </summary>
            AutoRepair = 1,
            /// <summary> MeshAutoRepair, and if the copy is still not closed, MeshRobustRepair of the input (it may solidify or wrap: the geometry can change) </summary>
            AutoRepairThenRobust = 2
        }

        /// <summary> when the reduction collapses the edges of a contact line together with their twin edges (PairContactsMode) </summary>
        public enum PairContactsModes
        {
            /// <summary> never (unless PairContacts is true) </summary>
            Off = 0,
            /// <summary> always: the same as PairContacts = true </summary>
            On = 1,
            /// <summary>
            /// reduce as with Off. Only if that result keeps contacts (ContactsResult &gt; 0) and is not clean, reduce the boolean result a
            /// second time with pairing and keep the paired result if its NotCleanReason is the same or empty and it has fewer
            /// triangles (or as many and fewer contacts). A result that is clean, or whose boolean result has no contacts, is bit for bit the one of Off
            /// </summary>
            Auto = 2
        }

        /// <summary> result code of Compute() </summary>
        public enum PipelineStatus
        {
            /// <summary> Result and Report are set </summary>
            Success = 0,
            /// <summary> no input, a null input, non-finite coordinates, or more than one reduction criterion (see Error) </summary>
            InvalidInput = 1,
            /// <summary> an input is not closed and InputRepair is Refuse, or no allowed repair closed it (see Error) </summary>
            OpenInput = 2,
            /// <summary> the boolean failed (BooleanStatus and Error), eg RoundingNotClean or BudgetExceeded; Result is null </summary>
            BooleanFailed = 3,
            /// <summary> Cancellation was cancelled; Result is null </summary>
            Cancelled = 4,
            /// <summary> Compute() was not called yet </summary>
            NotComputed = 5
        }

        /// <summary> the input meshes (not modified) </summary>
        public List<DMesh3> Inputs;
        /// <summary> Difference is the first input minus all others, see ExactMeshBoolean.Operations </summary>
        public ExactMeshBoolean.Operations Operation;

        /// <summary> what to do with an input that is not closed (default AutoRepair) </summary>
        public InputRepairModes InputRepair = InputRepairModes.AutoRepair;
        /// <summary> configures the boolean (eg MaxPieces); runs before AllowOpenMeshes = false and StrictRounding = true are set, which stay </summary>
        public Action<ExactMeshBoolean> ConfigureBoolean;
        // (tests) runs before the reduction
        internal Action BeforeReduction;
        // (tests) runs before the second reduction of PairContactsMode Auto
        internal Action BeforePairPass;
        /// <summary> cancels the boolean and the reduction; Status Cancelled and no Result </summary>
        public CancellationToken Cancellation;

        /// <summary> reduction criterion: at most this many triangles (0: not used) </summary>
        public int TargetTriangleCount = 0;
        /// <summary> reduction criterion: this fraction of the boolean result's triangles, in (0, 1) (0: not used); at least 4 triangles </summary>
        public double TargetFraction = 0;
        /// <summary> reduction criterion: collapse all edges shorter than this that can be collapsed (0: not used) </summary>
        public double MinEdgeLength = 0;
        /// <summary> reduction criterion: collapse until the Reducer's quadric error (Reducer.ReduceToMaxQuadricError: area-weighted squared distances) would exceed this (0: not used) </summary>
        public double MaxQuadricError = 0;
        /// <summary>
        /// the reduced surface stays within this distance of the boolean result (Reducer.MaxDeviation, certified); 0: no bound
        /// (the quadric error and the checks below still apply)
        /// </summary>
        public double MaxDeviation = 0;
        /// <summary> with MaxDeviation: also every point of the boolean result is within it of the reduced mesh (Reducer.MaxDeviationTwoSided; about 1.4 times the time) </summary>
        public bool MaxDeviationTwoSided = false;
        /// <summary>
        /// every collapse is checked exactly against the other triangles (Reducer.PreventSelfIntersections, 2-3 times the reduction
        /// time). Default true: it is what keeps the reduced result free of self-intersections. Switch it off only for a quick
        /// look: the final check then finds what the reduction broke
        /// </summary>
        public bool PreventSelfIntersections = true;
        /// <summary>
        /// Reducer.MinCollapseTriangleQuality: a collapse is refused if it makes a triangle with 4 sqrt(3) area / (sum of squared edge lengths)
        /// below this. Default 0.02: without it, the reduction of flat faces (boxes, plates) leaves zero-area triangles on
        /// the creases (NotCleanReason "degenerate triangles", measured on a subdivided box at 25 % and 10 %); 0 switches it off
        /// </summary>
        public double MinTriangleQuality = 0.02;
        /// <summary>
        /// Default false (the result is then the one without this option, bit for bit). Where the solids touch, the boolean result
        /// holds the contact as sheets with duplicated vertices at one position (Report.ContactsBoolean); without this option the
        /// reduction cannot collapse a contact line (about 12 triangles per vertex of it stay, SolidBooleanStopReason.Contacts). With it
        /// the reduction collapses each edge of a contact line together with its twin edge (Reducer.PairCoincidentVertices): both
        /// sheets move to the same new position, so the contact stays exactly coincident (no gap, no crossing), and every collapse is
        /// still checked as before (PreventSelfIntersections with the sheets touching, not intersecting, along the contact;
        /// MaxDeviation; fold-over; MinTriangleQuality). NotCleanReason is unchanged: a result that keeps contacts
        /// (Report.ContactsResult &gt; 0) is still reported as "self-intersections" at them (see SolidBooleanReport.Clean).
        /// Not combined with MaxDeviationTwoSided (InvalidInput).
        /// </summary>
        public bool PairContacts = false;
        /// <summary>
        /// Default Off. PairContacts = true is the same as On. Auto: a second reduction with pairing, only for a result that keeps
        /// contacts and is not clean (see PairContactsModes.Auto, Report.PairPassRan / PairPassKept); costs up to one more reduction
        /// for those results and nothing for the others (a copy of the boolean result is made if it has contacts). With
        /// MaxDeviationTwoSided Auto does nothing (pairing does not support it), On is InvalidInput as PairContacts
        /// </summary>
        public PairContactsModes PairContactsMode = PairContactsModes.Off;

        /// <summary> the returned solid, or null if Status is not Success </summary>
        public DMesh3 Result;
        /// <summary> the code of the last Compute() </summary>
        public PipelineStatus Status = PipelineStatus.NotComputed;
        /// <summary> the reason of a Status that is not Success </summary>
        public string Error;
        /// <summary> if Status is BooleanFailed: the boolean's status </summary>
        public ExactMeshBoolean.BooleanStatus BooleanStatus = ExactMeshBoolean.BooleanStatus.NotComputed;
        /// <summary> what the last Compute() did (set also when it failed, as far as it got) </summary>
        public SolidBooleanReport Report = new SolidBooleanReport();

        public SolidBooleanPipeline(IEnumerable<DMesh3> inputs, ExactMeshBoolean.Operations op)
        {
            Inputs = new List<DMesh3>(inputs ?? new DMesh3[0]);
            Operation = op;
        }

        public SolidBooleanPipeline(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)
            : this(new[] { a, b }, op)
        {
        }

        bool fail(PipelineStatus status, string error, Stopwatch total)
        {
            Result = null;
            Status = status;
            Error = error;
            Report.TotalMilliseconds = total.ElapsedMilliseconds;
            return false;
        }

        /// <summary> runs the pipeline; true if Result is set (Status Success) </summary>
        public bool Compute()
        {
            var total = Stopwatch.StartNew();
            Result = null;
            Error = null;
            Status = PipelineStatus.NotComputed;
            BooleanStatus = ExactMeshBoolean.BooleanStatus.NotComputed;
            Report = new SolidBooleanReport();

            int criteria = (TargetTriangleCount > 0 ? 1 : 0) + (TargetFraction > 0 ? 1 : 0) + (MinEdgeLength > 0 ? 1 : 0) + (MaxQuadricError > 0 ? 1 : 0);
            if (criteria > 1)
                return fail(PipelineStatus.InvalidInput, "more than one reduction criterion (TargetTriangleCount, TargetFraction, MinEdgeLength, MaxQuadricError)", total);
            if (TargetFraction < 0 || TargetFraction >= 1 || double.IsNaN(TargetFraction) || MinEdgeLength < 0 || double.IsNaN(MinEdgeLength)
                || MaxQuadricError < 0 || double.IsNaN(MaxQuadricError) || MaxDeviation < 0 || double.IsNaN(MaxDeviation) || double.IsInfinity(MaxDeviation)
                || double.IsInfinity(MinEdgeLength) || double.IsInfinity(MaxQuadricError) || TargetTriangleCount < 0 || !(MinTriangleQuality >= 0 && MinTriangleQuality <= 1))
                return fail(PipelineStatus.InvalidInput, "a reduction setting is out of range (TargetFraction in [0, 1), MinTriangleQuality in [0, 1], the others finite and not negative)", total);
            if ((PairContacts || PairContactsMode == PairContactsModes.On) && MaxDeviation > 0 && MaxDeviationTwoSided)
                return fail(PipelineStatus.InvalidInput, "PairContacts is not supported with MaxDeviationTwoSided", total);
            if (PairContactsMode < PairContactsModes.Off || PairContactsMode > PairContactsModes.Auto)
                return fail(PipelineStatus.InvalidInput, "PairContactsMode is not a defined value", total);
            if (Inputs == null || Inputs.Count == 0)
                return fail(PipelineStatus.InvalidInput, "no input meshes", total);
            for (int i = 0; i < Inputs.Count; ++i)
                if (Inputs[i] == null)
                    return fail(PipelineStatus.InvalidInput, "input " + i + " is null", total);

            // 1. the cheap check per input
            var clock = Stopwatch.StartNew();
            int n = Inputs.Count;
            Report.InputTriangles = new int[n];
            Report.InputClosed = new bool[n];
            bool anyOpen = false;
            for (int i = 0; i < n; ++i) {
                Report.InputTriangles[i] = Inputs[i].TriangleCount;
                Report.InputClosed[i] = ExactMeshBoolean.IsWindingClosed(Inputs[i]);
                anyOpen |= !Report.InputClosed[i];
            }
            Report.InputCheckMilliseconds = clock.ElapsedMilliseconds;
            if (Cancellation.IsCancellationRequested)
                return fail(PipelineStatus.Cancelled, "cancelled", total);

            // 2. the boolean; an input that fails the check is tried as it is first (the boolean reorients flipped triangles, which the
            // check counts as open), and repaired only if the boolean refuses it
            List<DMesh3> used = Inputs;
            clock.Restart();
            ExactMeshBoolean op = runBoolean(used);
            Report.BooleanMilliseconds = clock.ElapsedMilliseconds;
            BooleanStatus = op.Status;
            if (anyOpen && op.Status == ExactMeshBoolean.BooleanStatus.OpenInput) {
                if (InputRepair == InputRepairModes.Refuse)
                    return fail(PipelineStatus.OpenInput, "an input is not closed (InputRepair is Refuse): " + op.Error, total);
                // (the boolean refuses an input that IsWindingClosed accepts: nothing to repair)
                if (Array.IndexOf(Report.InputClosed, false) < 0)
                    return fail(PipelineStatus.OpenInput, op.Error, total);
                used = new List<DMesh3>(Inputs);
                clock.Restart();
                for (int i = 0; i < n; ++i) {
                    if (Report.InputClosed[i])
                        continue;
                    DMesh3 repaired;
                    SolidBooleanInputRepair r = repair(i, out repaired);
                    if (r == null)
                        return fail(PipelineStatus.OpenInput, "input " + i + " is not closed and no allowed repair closed it", total);
                    Report.RepairedInputs.Add(r);
                    used[i] = repaired;
                    if (Cancellation.IsCancellationRequested)
                        return fail(PipelineStatus.Cancelled, "cancelled", total);
                }
                Report.RepairMilliseconds = clock.ElapsedMilliseconds;
                clock.Restart();
                op = runBoolean(used);
                BooleanStatus = op.Status;
                Report.BooleanMilliseconds += clock.ElapsedMilliseconds;
            }
            if (op.Status == ExactMeshBoolean.BooleanStatus.Cancelled)
                return fail(PipelineStatus.Cancelled, "cancelled", total);
            if (op.Status == ExactMeshBoolean.BooleanStatus.InvalidInput) {
                return fail(PipelineStatus.InvalidInput, op.Error, total);
            }
            if (op.Status != ExactMeshBoolean.BooleanStatus.Success) {
                BooleanStatus = op.Status;
                return fail(op.Status == ExactMeshBoolean.BooleanStatus.OpenInput ? PipelineStatus.OpenInput : PipelineStatus.BooleanFailed, op.Status + ": " + op.Error, total);
            }
            DMesh3 mesh = op.Result;
            BooleanStatus = op.Status;
            Report.BooleanTriangles = mesh.TriangleCount;
            Report.ContactsBoolean = op.Contacts;
            Report.NonManifoldEdgesBoolean = op.NonManifoldEdges;

            // 3. the reduction (Auto: with a copy of the boolean result if it has contacts, for the second reduction)
            bool pairAlways = PairContacts || PairContactsMode == PairContactsModes.On;
            DMesh3 spare = null;
            if (!pairAlways && PairContactsMode == PairContactsModes.Auto && Report.ContactsBoolean > 0 && !(MaxDeviation > 0 && MaxDeviationTwoSided))
                spare = new DMesh3(mesh);      // (not compacted: the same ids as the mesh of the first reduction, so the second reduction is the one of On)
            clock.Restart();
            if (BeforeReduction != null)
                BeforeReduction();
            reduce(mesh, pairAlways);
            Report.ReductionMilliseconds = clock.ElapsedMilliseconds;
            if (Cancellation.IsCancellationRequested)
                return fail(PipelineStatus.Cancelled, "cancelled", total);
            DMesh3 result = new DMesh3(mesh, true);

            // 4. the final check
            clock.Restart();
            Report.ResultTriangles = result.TriangleCount;
            Report.ContactsResult = countContacts(result, null);
            Report.NotCleanReason = MeshRobustRepair.NotCleanReason(result);
            Report.FinalCheckMilliseconds = clock.ElapsedMilliseconds;

            // 5. Auto: a result that keeps contacts and is not clean is reduced a second time with pairing
            if (spare != null && Report.ContactsResult > 0 && !Report.Clean && Report.StopReason != SolidBooleanStopReason.NotReduced) {
                clock.Restart();
                Report.PairPassRan = true;
                Report.PairPassFirstTriangles = result.TriangleCount;
                SolidBooleanReport first = Report.ReductionState();
                if (BeforePairPass != null)
                    BeforePairPass();
                reduce(spare, true);
                if (Cancellation.IsCancellationRequested)
                    return fail(PipelineStatus.Cancelled, "cancelled", total);
                DMesh3 paired = new DMesh3(spare, true);
                int contacts2 = countContacts(paired, null);
                string reason2 = MeshRobustRepair.NotCleanReason(paired);
                if ((reason2.Length == 0 || reason2 == Report.NotCleanReason)
                    && (paired.TriangleCount < result.TriangleCount || (paired.TriangleCount == result.TriangleCount && contacts2 < Report.ContactsResult))) {
                    result = paired;
                    Report.PairPassKept = true;
                    Report.ResultTriangles = paired.TriangleCount;
                    Report.ContactsResult = contacts2;
                    Report.NotCleanReason = reason2;
                } else
                    Report.SetReductionState(first);
                Report.PairPassMilliseconds = clock.ElapsedMilliseconds;
            }

            Result = result;
            Status = PipelineStatus.Success;
            Report.TotalMilliseconds = total.ElapsedMilliseconds;
            return true;
        }

        ExactMeshBoolean runBoolean(List<DMesh3> meshes)
        {
            var op = new ExactMeshBoolean(meshes, Operation);
            if (ConfigureBoolean != null)
                ConfigureBoolean(op);
            op.AllowOpenMeshes = false;
            op.StrictRounding = true;
            op.Cancellation = Cancellation;
            op.Compute();
            return op;
        }

        // the cheapest allowed repair that makes input i closed (on a copy), or null
        SolidBooleanInputRepair repair(int i, out DMesh3 repaired)
        {
            repaired = null;
            DMesh3 input = Inputs[i];
            var clock = Stopwatch.StartNew();
            var auto = new MeshAutoRepair(new DMesh3(input));
            auto.Apply();
            if (ExactMeshBoolean.IsWindingClosed(auto.Mesh) && auto.Mesh.TriangleCount > 0) {
                repaired = auto.Mesh;
                return new SolidBooleanInputRepair { Index = i, Method = "MeshAutoRepair", TrianglesBefore = input.TriangleCount, TrianglesAfter = repaired.TriangleCount, Milliseconds = clock.ElapsedMilliseconds };
            }
            if (InputRepair != InputRepairModes.AutoRepairThenRobust)
                return null;
            var robust = new MeshRobustRepair(new DMesh3(input));
            robust.Apply();
            if (ExactMeshBoolean.IsWindingClosed(robust.Mesh) && robust.Mesh.TriangleCount > 0) {
                repaired = robust.Mesh;
                return new SolidBooleanInputRepair { Index = i, Method = "MeshRobustRepair", TrianglesBefore = input.TriangleCount, TrianglesAfter = repaired.TriangleCount, Milliseconds = clock.ElapsedMilliseconds };
            }
            return null;
        }

        // Reducer that keeps the last refusal of each edge (ProcessResult is the Reducer's protected enum): the edges left at the
        // end and why they were not collapsed
        sealed class CountingReducer : Reducer
        {
            readonly Dictionary<int, ProcessResult> last = new Dictionary<int, ProcessResult>();

            public CountingReducer(DMesh3 m) : base(m) { }

            protected override ProcessResult CollapseEdge(int edgeID, Vector3d vNewPos, out int collapseToV)
            {
                ProcessResult r = base.CollapseEdge(edgeID, vNewPos, out collapseToV);
                if (r == ProcessResult.Ok_Collapsed)
                    last.Remove(edgeID);
                else
                    last[edgeID] = r;
                return r;
            }

            // the edges of the mesh now with a refusal as their last result: (guard, deviation, quality, topology), and the
            // numbers of these at a vertex of contactVertices (by cause as well: guardAtContacts, all)
            public void Blocked(HashSet<int> contactVertices, out int guard, out int deviation, out int quality, out int topology, out int guardAtContacts, out int allAtContacts)
            {
                guard = deviation = quality = topology = guardAtContacts = allAtContacts = 0;
                foreach (int eid in mesh.EdgeIndices()) {
                    ProcessResult r;
                    if (last.TryGetValue(eid, out r) == false)
                        continue;
                    Index2i ev = mesh.GetEdgeV(eid);
                    bool atContact = contactVertices.Contains(ev.a) || contactVertices.Contains(ev.b);
                    switch (r) {
                        case ProcessResult.Ignored_CreatesIntersection: guard++; if (atContact) guardAtContacts++; break;
                        case ProcessResult.Ignored_ExceedsDeviation: deviation++; break;
                        case ProcessResult.Ignored_CreatesFlip: quality++; break;
                        case ProcessResult.Ignored_CannotCollapse:
                        case ProcessResult.Failed_OpNotSuccessful: topology++; break;
                        default: continue;
                    }
                    if (atContact) allAtContacts++;
                }
            }
        }

        void reduce(DMesh3 mesh, bool pair)
        {
            bool byCount = TargetTriangleCount > 0 || TargetFraction > 0;
            if (mesh.TriangleCount == 0) {
                Report.StopReason = SolidBooleanStopReason.EmptyResult;
                Report.StopDetail = "the boolean result has no triangles";
                return;
            }
            if (byCount == false && MinEdgeLength <= 0 && MaxQuadricError <= 0) {
                Report.StopReason = SolidBooleanStopReason.NotReduced;
                Report.StopDetail = "no reduction criterion set";
                return;
            }
            var reducer = new CountingReducer(mesh) {
                PreventSelfIntersections = PreventSelfIntersections,
                MaxDeviation = MaxDeviation,
                MaxDeviationTwoSided = MaxDeviation > 0 && MaxDeviationTwoSided,
                MinCollapseTriangleQuality = MinTriangleQuality,
                PairCoincidentVertices = pair,
                Progress = new ProgressCancel(() => Cancellation.IsCancellationRequested)
            };
            int target = 0;
            if (TargetTriangleCount > 0)
                target = TargetTriangleCount;
            else if (TargetFraction > 0)
                target = Math.Max(4, (int)Math.Ceiling(TargetFraction * mesh.TriangleCount));
            Report.TargetTriangles = target;
            if (target > 0) reducer.ReduceToTriangleCount(target);
            else if (MaxQuadricError > 0) reducer.ReduceToMaxQuadricError(MaxQuadricError);
            else reducer.ReduceToEdgeLength(MinEdgeLength);
            Report.PairedCollapses = (int)reducer.PairedCollapses;

            bool reached;
            if (target > 0) reached = mesh.TriangleCount <= target;
            else if (MinEdgeLength > 0) reached = shortestEdgeAtLeast(mesh, MinEdgeLength);
            else reached = true;      // (MaxQuadricError: the budget is the criterion; blocked edges are not diagnosed)
            if (reached) {
                Report.StopReason = SolidBooleanStopReason.TargetReached;
                Report.StopDetail = target > 0 ? "reached " + mesh.TriangleCount + " triangles (target " + target + ")" : "criterion reached";
                return;
            }

            // why the edges that are left were not collapsed (their last result), and which of them are at contacts
            var contactVertices = new HashSet<int>();
            int contacts = countContacts(mesh, contactVertices);
            int guardAtContacts;
            reducer.Blocked(contactVertices, out Report.BlockedByGuard, out Report.BlockedByDeviation, out Report.BlockedByQuality, out Report.BlockedByTopology, out guardAtContacts, out Report.BlockedAtContacts);
            int cg = Report.BlockedByGuard, cd = Report.BlockedByDeviation, cq = Report.BlockedByQuality, ct = Report.BlockedByTopology;
            int max = Math.Max(Math.Max(cg, cd), Math.Max(cq, ct));
            string what = target > 0 ? "target " + target + " triangles" : "edge length " + MinEdgeLength;
            string counts = "stopped at " + mesh.TriangleCount + " triangles (" + what + "); edges left and blocked: " + cg + " by the self-intersection guard (" + guardAtContacts
                + " at contacts), " + cd + " by MaxDeviation, " + cq + " by flip / fold-over / quality, " + ct + " by the topology; " + contacts + " contacts left, "
                + Report.BlockedAtContacts + " blocked edges at them";
            Report.StopDetail = counts;
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            if (max == 0 || mesh.TriangleCount <= 4 * comps.Count)
                Report.StopReason = SolidBooleanStopReason.Exhausted;
            else if (max == cg)
                Report.StopReason = contacts > 0 && 2 * guardAtContacts >= cg ? SolidBooleanStopReason.Contacts : SolidBooleanStopReason.SelfIntersectionGuard;
            else if (max == cd)
                Report.StopReason = SolidBooleanStopReason.DeviationBound;
            else if (max == cq)
                Report.StopReason = SolidBooleanStopReason.TriangleQuality;
            else
                Report.StopReason = SolidBooleanStopReason.Topology;
        }

        static bool shortestEdgeAtLeast(DMesh3 mesh, double length)
        {
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                if (mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)) < length)
                    return false;
            }
            return true;
        }

        // the number of positions with two or more vertices (the vertices of those, if a set is given)
        static int countContacts(DMesh3 mesh, HashSet<int> vertices)
        {
            var first = new Dictionary<(double, double, double), int>();
            var multiple = new HashSet<(double, double, double)>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                var key = (p.x == 0 ? 0 : p.x, p.y == 0 ? 0 : p.y, p.z == 0 ? 0 : p.z);
                if (first.ContainsKey(key)) {
                    multiple.Add(key);
                } else
                    first[key] = vid;
            }
            if (vertices != null && multiple.Count > 0)
                foreach (int vid in mesh.VertexIndices()) {
                    Vector3d p = mesh.GetVertex(vid);
                    if (multiple.Contains((p.x == 0 ? 0 : p.x, p.y == 0 ? 0 : p.y, p.z == 0 ? 0 : p.z)))
                        vertices.Add(vid);
                }
            return multiple.Count;
        }
    }
}
