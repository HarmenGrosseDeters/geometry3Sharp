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
        /// <summary> the boolean result has no triangles (eg the intersection of disjoint meshes), or MinThickness removed all of it: nothing to reduce </summary>
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

    /// <summary> one solid of the boolean result that MinThickness removed (an outer shell with the cavities inside it) </summary>
    public sealed class SolidBooleanThinSolid
    {
        /// <summary> triangles and shells (the outer shell and its cavities) of the solid </summary>
        public int Triangles, Shells;
        /// <summary> its volume (the outer shell minus its cavities) and its area (of all its shells) </summary>
        public double Volume, Area;
        /// <summary> 2 Volume / Area: the thickness of a flat slab or wall; a cube of edge a gives a / 3, a ball of radius r gives 2 r / 3 </summary>
        public double Thickness;
        /// <summary> the centre of the bounding box of the outer shell </summary>
        public Vector3d Center;
    }

    /// <summary> one input thinner than MinThickness (SolidBooleanPipeline.ThinInputs) </summary>
    public sealed class SolidBooleanThinInput
    {
        /// <summary> index of the input </summary>
        public int Index;
        /// <summary> its volume (relative to the centre of its bounding box) and area </summary>
        public double Volume, Area;
        /// <summary> 2 |Volume| / Area </summary>
        public double Thickness;
        /// <summary> ExactMeshBoolean.IsWindingClosed of the input: for an open input the volume is an estimate </summary>
        public bool Closed;
    }

    /// <summary>
    /// What a SolidBooleanPipeline run did: counts, time per stage, repaired inputs and the reason the reduction stopped.
    /// </summary>
    public sealed class SolidBooleanReport
    {
        /// <summary> triangles per input as given (before any repair) </summary>
        public int[] InputTriangles = new int[0];
        /// <summary> per input: ExactMeshBoolean.IsWindingClosed of the input as given (with InputGrid: of its snapped copy) </summary>
        public bool[] InputClosed = new bool[0];
        /// <summary> with InputGrid: the input vertices that the snap moved (of all inputs); 0 otherwise </summary>
        public int InputSnappedVertices;
        /// <summary> the inputs that were repaired, with the method (empty if none) </summary>
        public List<SolidBooleanInputRepair> RepairedInputs = new List<SolidBooleanInputRepair>();
        /// <summary> triangles of the exact boolean result (before the reduction) </summary>
        public int BooleanTriangles;
        /// <summary> the boolean's Contacts and NonManifoldEdges (touching solids, see ExactMeshBoolean.Contacts); with MinThickness, ContactsBoolean counts what is left after the removal </summary>
        public int ContactsBoolean, NonManifoldEdgesBoolean;
        /// <summary>
        /// components of the boolean result thinner than the strict rounding bound (ExactMeshBoolean.Rounding.ScaleDisparity),
        /// which the strict boolean refused although the rounding left no intersection, degenerate triangle or false contact:
        /// the pipeline keeps that result (BooleanStatus stays RoundingNotClean) and counts them here; 0 otherwise. With
        /// MinThickness they are removed if they are thinner than it (RemovedThinSolids)
        /// </summary>
        public int ThinComponentsKept;
        /// <summary> the solids of the boolean result that MinThickness removed (empty without it) </summary>
        public List<SolidBooleanThinSolid> RemovedThinSolids = new List<SolidBooleanThinSolid>();
        /// <summary> with MinThickness: the inputs thinner than it (see ThinInputs); empty otherwise </summary>
        public List<SolidBooleanThinInput> ThinInputs = new List<SolidBooleanThinInput>();
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
    /// so an STL-like soup of a closed solid is closed). An input that fails and that the boolean refuses as open fails the
    /// run with Status OpenInput by default (InputRepair = Refuse): a missing face is missing information, and no repair
    /// knows what the input means (a solid with a hole, or an open cutting surface). An input that is closed once its
    /// flipped triangles agree is not open: the boolean refuses it with InconsistentOrientation by default (see
    /// Orientation). Only on request is an open input repaired, on a copy: with InputRepair = AutoRepair by MeshAutoRepair,
    /// with AutoRepairThenRobust also by MeshRobustRepair (which can resolve, solidify or wrap, so it changes the geometry
    /// more) as the fallback. RepairedInputs of the report names each repaired input and the method.</description></item>
    /// <item><description>ExactMeshBoolean of all inputs (Union, Intersection or Difference: first minus the others), with AllowOpenMeshes = false and
    /// StrictRounding = true (ConfigureBoolean runs before these two are forced): the result is exact up to the
    /// rounding of the new vertices, within the relative bound of StrictRounding, or the run fails with BooleanFailed. One
    /// exception: a strict refusal whose only cause is components thinner than the bound (no intersection, degenerate
    /// triangle or false contact left) keeps the rounded result and counts them (Report.ThinComponentsKept). Then, with
    /// MinThickness, the solids thinner than it are removed (Report.RemovedThinSolids).</description></item>
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
            /// <summary> no repair: Compute() fails with Status OpenInput (default) </summary>
            Refuse = 0,
            /// <summary>
            /// MeshAutoRepair on a copy of the input. It fills holes by its own rules, without knowing the other inputs or what
            /// the open input means. For a solid with a small hole that is usually right; for an open cutting surface (a sheet
            /// used as the tool of a Difference) it is about never where the missing face was: the closed copy can enclose
            /// almost nothing, and the cut then removes almost nothing, with Status Success and a clean report (#1211). Use
            /// it only for inputs known to be solids with small holes
            /// </summary>
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
            NotComputed = 5,
            /// <summary> an input is thinner than MinThickness and ThinInputs is Refuse (Report.ThinInputs lists them); Result is null </summary>
            ThinInput = 6,
            /// <summary>
            /// an input is closed only after flipping some of its triangles and Orientation is Refuse (see
            /// ExactMeshBoolean.OrientationModes and Error); Result is null
            /// </summary>
            InconsistentOrientation = 7
        }

        /// <summary>
        /// what the boolean does with an input whose triangles are inconsistently oriented (ExactMeshBoolean.Orientation; set
        /// before ConfigureBoolean runs). Default Refuse: Status InconsistentOrientation
        /// </summary>
        public ExactMeshBoolean.OrientationModes Orientation = ExactMeshBoolean.OrientationModes.Refuse;

        /// <summary> what happens to an input thinner than MinThickness (Report.ThinInputs) </summary>
        public enum ThinInputModes
        {
            /// <summary> it is listed in the report and used as it is (default) </summary>
            Keep = 0,
            /// <summary>
            /// it counts as an empty solid: left out of a union and as a tool of a difference; the result is empty if it is the
            /// first operand of a difference, any operand of an intersection, or if every input of a union is thin
            /// </summary>
            TreatAsEmpty = 1,
            /// <summary> Compute() fails with Status ThinInput </summary>
            Refuse = 2
        }

        /// <summary> the input meshes (not modified) </summary>
        public List<DMesh3> Inputs;
        /// <summary> Difference is the first input minus all others, see ExactMeshBoolean.Operations </summary>
        public ExactMeshBoolean.Operations Operation;

        /// <summary> what to do with an input that is not closed (default Refuse: Status OpenInput; see InputRepairModes.AutoRepair before choosing a repair) </summary>
        public InputRepairModes InputRepair = InputRepairModes.Refuse;
        /// <summary>
        /// Default 0 (off: the inputs are used as given, and the result is the one without this option, bit for bit). Otherwise a
        /// power of two (eg 2^-20, about 1e-6 in metres): every coordinate of every input is moved to the nearest multiple of it,
        /// on copies, before the input check (ties to even). With a power of two the snap is exact, so the snapped
        /// inputs are the grid points themselves at any distance from the origin, and scaling the inputs and the grid by
        /// 2^k scales the result, bit for bit. What it is for: faceted CAD solids whose shared faces are nearly but not exactly
        /// coincident (the vertices of the two sides differ by noise far below the model's tolerance, eg 1e-8 at coordinates
        /// of 1e6). The exact boolean resolves such noise faithfully into slivers below the double grid of the coordinates,
        /// and far from the origin their rounding can be beyond repair within the strict bound (Status BooleanFailed,
        /// BooleanStatus RoundingNotClean); on the grid, vertices of the two sides that lie within it of each other become one
        /// point, and faces through the same grid points coincide exactly, which the exact boolean handles without slivers.
        /// Vertices move by at most half the grid per coordinate (Report.InputSnappedVertices counts those that moved; a
        /// multiple that would overflow is replaced by its neighbour towards zero); features thinner than the grid can
        /// collapse. The input check and the repair see the snapped copies; a repaired input is not snapped again (a hole fill
        /// can add vertices off the grid). Not a power of two, negative or not finite: InvalidInput
        /// </summary>
        public double InputGrid = 0;
        /// Default 0 (off). Otherwise a length: every solid of the boolean result (an outer shell with the cavities inside it)
        /// whose thickness 2 Volume / Area is below it is removed before the reduction and listed in Report.RemovedThinSolids.
        /// For a flat slab or wall the measure is its thickness; compact solids measure less than their size (a cube of edge
        /// a: a / 3), so a value is chosen for sheets, eg the model tolerance of a CAD system (1e-3 in metres). What it is for:
        /// solids that are in practice surfaces, two nearly coincident sheets some 1e-5 to 1e-3 apart (eg left by a previous
        /// boolean of coincident faces); the exact boolean keeps them, correctly, and a caller that treats them as
        /// artifacts removes them here. A whole solid is removed or kept, so the result stays closed; if nothing is left the
        /// result is empty (Status Success, Report.NotCleanReason "empty"). The cavities are assigned to the smallest outer
        /// shell around them by its winding number at a point of the cavity. Negative or not finite: InvalidInput
        /// </summary>
        public double MinThickness = 0;
        /// <summary>
        /// With MinThickness: every input is measured by 2 |Volume| / Area (the volume relative to the centre of its bounding
        /// box) before the boolean, and an input below MinThickness is listed in Report.ThinInputs and handled as this says
        /// (default Keep: only listed). For a closed input the measure is that of the solids (all of them together); for an
        /// open one it is an estimate (the volume through a hole is not defined), good where the hole has little area, eg a
        /// sheet or a wedge whose top and bottom meet in a flap of zero thickness, the typical faceting of a layer that is in
        /// practice a surface. Invalid values: InvalidInput
        /// </summary>
        public ThinInputModes ThinInputs = ThinInputModes.Keep;
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
            if (!(InputGrid == 0 || IsPowerOfTwo(InputGrid)))
                return fail(PipelineStatus.InvalidInput, "InputGrid must be 0 or a positive power of two", total);
            if (!(MinThickness >= 0) || double.IsInfinity(MinThickness))
                return fail(PipelineStatus.InvalidInput, "MinThickness must be finite and not negative", total);
            if (ThinInputs < ThinInputModes.Keep || ThinInputs > ThinInputModes.Refuse)
                return fail(PipelineStatus.InvalidInput, "ThinInputs is not a defined value", total);
            if (Inputs == null || Inputs.Count == 0)
                return fail(PipelineStatus.InvalidInput, "no input meshes", total);
            for (int i = 0; i < Inputs.Count; ++i)
                if (Inputs[i] == null)
                    return fail(PipelineStatus.InvalidInput, "input " + i + " is null", total);

            // 1. the snap to InputGrid (on copies), then the cheap check per input
            var clock = Stopwatch.StartNew();
            int n = Inputs.Count;
            List<DMesh3> given = Inputs;
            if (InputGrid > 0) {
                given = new List<DMesh3>(n);
                for (int i = 0; i < n; ++i) {
                    int moved;
                    given.Add(Snapped(Inputs[i], InputGrid, out moved));
                    Report.InputSnappedVertices += moved;
                }
            }
            Report.InputTriangles = new int[n];
            Report.InputClosed = new bool[n];
            bool anyOpen = false;
            for (int i = 0; i < n; ++i) {
                Report.InputTriangles[i] = Inputs[i].TriangleCount;
                Report.InputClosed[i] = ExactMeshBoolean.IsWindingClosed(given[i]);
                anyOpen |= !Report.InputClosed[i];
            }
            // the inputs thinner than MinThickness; active: the indices of the inputs the boolean gets
            var active = new List<int>(n);
            for (int i = 0; i < n; ++i)
                active.Add(i);
            if (MinThickness > 0) {
                var thin = new bool[n];
                for (int i = 0; i < n; ++i) {
                    double v, a;
                    MeasureInput(given[i], out v, out a);
                    double t = a > 0 ? 2 * Math.Abs(v) / a : 0;
                    if (t < MinThickness) {
                        thin[i] = true;
                        Report.ThinInputs.Add(new SolidBooleanThinInput { Index = i, Volume = v, Area = a, Thickness = t, Closed = Report.InputClosed[i] });
                    }
                }
                if (Report.ThinInputs.Count > 0 && ThinInputs == ThinInputModes.Refuse)
                    return fail(PipelineStatus.ThinInput, Report.ThinInputs.Count + " input(s) thinner than MinThickness, the first: input " + Report.ThinInputs[0].Index, total);
                if (Report.ThinInputs.Count > 0 && ThinInputs == ThinInputModes.TreatAsEmpty) {
                    active.RemoveAll(i => thin[i]);
                    if (Operation == ExactMeshBoolean.Operations.Intersection || (Operation == ExactMeshBoolean.Operations.Difference && thin[0]) || active.Count == 0) {
                        Report.InputCheckMilliseconds = clock.ElapsedMilliseconds;
                        return emptyResult("an input thinner than MinThickness was treated as empty (ThinInputs), so the " + Operation + " is empty", total);
                    }
                }
            }
            Report.InputCheckMilliseconds = clock.ElapsedMilliseconds;
            if (Cancellation.IsCancellationRequested)
                return fail(PipelineStatus.Cancelled, "cancelled", total);

            // 2. the boolean; an input that fails the check is tried as it is first (with Orientation = AreaMajority the boolean reorients flipped triangles, which the
            // check counts as open), and repaired only if the boolean refuses it
            List<DMesh3> used = given;
            if (active.Count < n) {
                used = new List<DMesh3>(active.Count);
                foreach (int i in active)
                    used.Add(given[i]);
                anyOpen = false;
                foreach (int i in active)
                    anyOpen |= !Report.InputClosed[i];
            }
            clock.Restart();
            ExactMeshBoolean op = runBoolean(used);
            Report.BooleanMilliseconds = clock.ElapsedMilliseconds;
            BooleanStatus = op.Status;
            if (anyOpen && op.Status == ExactMeshBoolean.BooleanStatus.OpenInput) {
                if (InputRepair == InputRepairModes.Refuse)
                    return fail(PipelineStatus.OpenInput, "an input is not closed (InputRepair is Refuse): " + op.Error, total);
                // (the boolean refuses an input that IsWindingClosed accepts: nothing to repair)
                if (!anyOpen)
                    return fail(PipelineStatus.OpenInput, op.Error, total);
                used = new List<DMesh3>(used);
                clock.Restart();
                for (int k = 0; k < active.Count; ++k) {
                    int i = active[k];
                    if (Report.InputClosed[i])
                        continue;
                    DMesh3 repaired;
                    SolidBooleanInputRepair r = repair(i, given[i], out repaired);
                    if (r == null)
                        return fail(PipelineStatus.OpenInput, "input " + i + " is not closed and no allowed repair closed it", total);
                    Report.RepairedInputs.Add(r);
                    used[k] = repaired;
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
            if (op.Status == ExactMeshBoolean.BooleanStatus.InconsistentOrientation)
                return fail(PipelineStatus.InconsistentOrientation, op.Error, total);
            if (op.Status == ExactMeshBoolean.BooleanStatus.InvalidInput) {
                return fail(PipelineStatus.InvalidInput, op.Error, total);
            }
            // a strict refusal whose only cause is components thinner than the bound: the rounded result is kept (the rounding
            // left no intersection, degenerate triangle or false contact, checked exactly by the repair)
            bool thinOnly = op.Status == ExactMeshBoolean.BooleanStatus.RoundingNotClean && OnlyThinComponents(op.Rounding);
            if (op.Status != ExactMeshBoolean.BooleanStatus.Success && !thinOnly) {
                BooleanStatus = op.Status;
                return fail(op.Status == ExactMeshBoolean.BooleanStatus.OpenInput ? PipelineStatus.OpenInput : PipelineStatus.BooleanFailed, op.Status + ": " + op.Error, total);
            }
            DMesh3 mesh = thinOnly ? op.Rounding.Mesh : op.Result;
            BooleanStatus = op.Status;
            Report.BooleanTriangles = mesh.TriangleCount;
            Report.ContactsBoolean = thinOnly ? countContacts(mesh, null) : op.Contacts;
            Report.NonManifoldEdgesBoolean = op.NonManifoldEdges;
            Report.ThinComponentsKept = thinOnly ? op.Rounding.ScaleDisparity : 0;
            if (MinThickness > 0) {
                Report.RemovedThinSolids = RemoveThinSolids(mesh, MinThickness);
                // (the contacts of what is left: the pair pass of PairContactsMode Auto depends on them; BooleanTriangles stays the boolean's count)
                if (Report.RemovedThinSolids.Count > 0)
                    Report.ContactsBoolean = countContacts(mesh, null);
            }

            // 3. the reduction (Auto: with a copy of the boolean result if it has contacts, for the second reduction)
            bool pairAlways = PairContacts || PairContactsMode == PairContactsModes.On;
            DMesh3 spare = null;
            if (!pairAlways && PairContactsMode == PairContactsModes.Auto && Report.ContactsBoolean > 0 && !(MaxDeviation > 0 && MaxDeviationTwoSided)
                && (TargetTriangleCount > 0 || TargetFraction > 0 || MinEdgeLength > 0 || MaxQuadricError > 0))
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

        // a normal positive power of two: its stored significand bits are all zero (a subnormal power of two has one of them
        // set, and the only positive value with a zero exponent field and no significand bit is 0, excluded)
        static bool IsPowerOfTwo(double g)
        {
            if (!(g > 0) || double.IsInfinity(g))
                return false;
            return (BitConverter.DoubleToInt64Bits(g) & 0xFFFFFFFFFFFFFL) == 0;
        }

        // a copy of m with every coordinate at the nearest multiple of the power of two g (ties to even; exact: x / g and
        // the product are exact, and the coordinate is unchanged where it is already a multiple, eg where |x| / g >= 2^52);
        // a non-finite coordinate is left as it is (the boolean refuses it). moved: the vertices whose position changed (a
        // negative zero written as zero is not a move)
        internal static DMesh3 Snapped(DMesh3 m, double g, out int moved)
        {
            var c = new DMesh3(m);
            moved = 0;
            foreach (int v in c.VertexIndices()) {
                Vector3d p = c.GetVertex(v);
                var q = new Vector3d(snap(p.x, g), snap(p.y, g), snap(p.z, g));
                if (bits(q.x) != bits(p.x) || bits(q.y) != bits(p.y) || bits(q.z) != bits(p.z))
                    c.SetVertex(v, q);
                if (!same(q.x, p.x) || !same(q.y, p.y) || !same(q.z, p.z))
                    moved++;
            }
            return c;
        }

        static long bits(double x) { return BitConverter.DoubleToInt64Bits(x); }

        // the same position along one axis: equal values (0 and -0 too), or both NaN
        static bool same(double a, double b) { return a == b || (double.IsNaN(a) && double.IsNaN(b)); }

        static double snap(double x, double g)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
                return x;
            double s = x / g;
            if (double.IsInfinity(s))       // (a grid so fine that the quotient overflows: x is a multiple of it)
                return x;
            double r = Math.Round(s) * g;
            if (double.IsInfinity(r))       // (a grid so coarse that the nearest multiple overflows: the other neighbour, towards zero)
                r = Math.Truncate(s) * g;
            return r == 0 ? 0 : r;          // (no negative zero: -0.4 g, -0 and 0.4 g snap to the same point)
        }

        // Status Success with an empty result (ThinInputs TreatAsEmpty)
        bool emptyResult(string detail, Stopwatch total)
        {
            Result = new DMesh3();
            Status = PipelineStatus.Success;
            BooleanStatus = ExactMeshBoolean.BooleanStatus.NotComputed;
            Report.StopReason = SolidBooleanStopReason.EmptyResult;
            Report.StopDetail = detail;
            Report.NotCleanReason = MeshRobustRepair.NotCleanReason(Result);
            Report.TotalMilliseconds = total.ElapsedMilliseconds;
            return true;
        }

        /// <summary>
        /// the signed volume (relative to the centre of the bounding box, so that it is exact enough far from the origin and,
        /// for an open mesh, an estimate independent of where the mesh lies) and the area of a mesh
        /// </summary>
        internal static void MeasureInput(DMesh3 m, out double volume, out double area)
        {
            Vector3d o = m.TriangleCount > 0 ? m.GetBounds().Center : Vector3d.Zero;
            double v6 = 0, a2 = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Vector3d p = m.GetVertex(tv.a), q = m.GetVertex(tv.b), r = m.GetVertex(tv.c);
                v6 += (p - o).Dot((q - o).Cross(r - o));
                a2 += (q - p).Cross(r - p).Length;
            }
            volume = v6 / 6;
            area = a2 / 2;
        }

        // the strict rounding refused the result only for components thinner than its bound
        internal static bool OnlyThinComponents(MeshRoundingRepair r)
        {
            return r != null && r.Mesh != null && r.ScaleDisparity > 0 && r.RemainingIntersections == 0 && r.RemainingDegenerate == 0
                && r.RemainingFalseContacts == 0 && !r.ScaleNotFinite && !r.OutsideRange;
        }

        /// <summary>
        /// Removes from mesh every solid (an outer shell, positive volume, with the cavities, negative volume, inside it)
        /// whose 2 Volume / Area is below minThickness, and returns them. A cavity belongs to the smallest outer shell whose
        /// box contains the cavity's box and that contains the cavity (ShellIndex.Contains: exact, by a vertex of the cavity); a cavity
        /// in no outer shell (not a valid solid) is left alone, and so is a solid with a shell that has boundary edges (closed
        /// only by position). Whole components are removed, so a closed mesh stays closed. Volumes are taken relative to a
        /// vertex of each component (exact enough far from the origin).
        /// </summary>
        internal static List<SolidBooleanThinSolid> RemoveThinSolids(DMesh3 mesh, double minThickness)
        {
            var removed = new List<SolidBooleanThinSolid>();
            if (mesh.TriangleCount == 0)
                return removed;
            var cc = new MeshConnectedComponents(mesh);
            cc.FindConnectedT();
            int n = cc.Count;
            var vol = new double[n];
            var area = new double[n];
            var open = new bool[n];
            var boxes = new AxisAlignedBox3d[n];
            var tris = new int[n][];
            for (int c = 0; c < n; ++c) {
                tris[c] = cc.Components[c].Indices;
                // (the volume relative to a vertex of the component: far from the origin the terms of absolute coordinates
                // cancel, and the sign of a thin shell could be noise)
                Vector3d o = mesh.GetVertex(mesh.GetTriangle(tris[c][0]).a);
                double v6 = 0, a2 = 0;
                boxes[c] = AxisAlignedBox3d.Empty;
                foreach (int t in tris[c]) {
                    Index3i tv = mesh.GetTriangle(t);
                    Vector3d p = mesh.GetVertex(tv.a), q = mesh.GetVertex(tv.b), r = mesh.GetVertex(tv.c);
                    boxes[c].Contain(p); boxes[c].Contain(q); boxes[c].Contain(r);
                    Vector3d pa = p - o, qa = q - o, ra = r - o;
                    v6 += pa.Dot(qa.Cross(ra));
                    a2 += (q - p).Cross(r - p).Length;
                    for (int j = 0; j < 3 && !open[c]; ++j)
                        open[c] = mesh.IsBoundaryEdge(mesh.GetTriEdge(t, j));
                }
                vol[c] = v6 / 6;
                area[c] = a2 / 2;
            }
            // the solid of each component: an outer shell is its own; a cavity goes to the smallest outer shell that contains
            // it, decided exactly (ShellIndex.Contains). A cavity and another shell share at most points where they touch, so the
            // cavity is inside or outside the shell as a whole: one point of the cavity that is not on the shell decides (the
            // probes are the cavity's vertices, exact points of the cavity)
            var owner = new int[n];
            var shellIndex = new Dictionary<int, ShellIndex>();
            for (int c = 0; c < n; ++c) {
                owner[c] = vol[c] > 0 ? c : -1;
                if (vol[c] > 0)
                    continue;
                for (int o = 0; o < n; ++o) {
                    if (!(vol[o] > 0) || (owner[c] >= 0 && vol[o] >= vol[owner[c]]) || !boxes[o].Contains(boxes[c]))
                        continue;
                    ShellIndex index;
                    if (!shellIndex.TryGetValue(o, out index))
                        shellIndex[o] = index = new ShellIndex(mesh, tris[o], boxes[o]);
                    if (index.Contains(mesh, tris[c]))
                        owner[c] = o;
                }
            }
            var remove = new HashSet<int>();
            for (int o = 0; o < n; ++o) {
                if (!(vol[o] > 0))
                    continue;
                double v = 0, a = 0;
                int count = 0, shells = 0;
                bool anyOpen = false;
                for (int c = 0; c < n; ++c)
                    if (owner[c] == o) {
                        v += vol[c];
                        a += area[c];
                        count += tris[c].Length;
                        shells++;
                        anyOpen |= open[c];
                    }
                // (a solid with a shell that is open by its edges, closed only by position, is left: its removal could leave holes)
                double thickness = a > 0 ? 2 * v / a : 0;
                if (anyOpen || thickness >= minThickness)
                    continue;
                removed.Add(new SolidBooleanThinSolid { Triangles = count, Shells = shells, Volume = v, Area = a, Thickness = thickness, Center = boxes[o].Center });
                for (int c = 0; c < n; ++c)
                    if (owner[c] == o)
                        remove.Add(c);
            }
            foreach (int c in remove)
                foreach (int t in tris[c])
                    mesh.RemoveTriangle(t, true, false);
            return removed;
        }

        /// <summary> a closed shell (some triangles of a mesh) as a sub-mesh with its AABB tree, for exact containment tests </summary>
        internal sealed class ShellIndex
        {
            /// <summary>
            /// Does the closed shell (triangles tris of mesh, box its box) contain the cavity (triangles cavity of mesh)? See
            /// ShellIndex.Contains
            /// </summary>
            internal static bool Contains(DMesh3 mesh, int[] tris, AxisAlignedBox3d box, int[] cavity)
            {
                return new ShellIndex(mesh, tris, box).Contains(mesh, cavity);
            }

            /// <summary>
            /// Signed crossings of the segment p-q with the triangles tris of mesh (exact orientations; +1 where it leaves the
            /// back side of a triangle, -1 where it enters it), for p on no triangle. degenerate: the segment passes through an
            /// edge or a vertex of a triangle, or ends on or lies in the plane of one it touches; the count is then not a winding
            /// number
            /// </summary>
            internal static int CrossingsAlong(DMesh3 mesh, int[] tris, Vector3d p, Vector3d q, out bool degenerate)
            {
                int w = 0;
                degenerate = false;
                foreach (int t in tris) {
                    var tri = new Triangle3d();
                    mesh.GetTriVertices(t, ref tri.V0, ref tri.V1, ref tri.V2);
                    Vector3d a = tri.V0, b = tri.V1, c = tri.V2;
                    int op = ExactPredicates.Orient3d(a, b, c, p), oq = ExactPredicates.Orient3d(a, b, c, q);
                    if (op == 0 || oq == 0) {
                        // the segment ends on the plane of the triangle, or lies in it (p is not on the triangle itself, and a
                        // segment that starts in the plane and ends off it meets the triangle at most at p): it is not a clean
                        // crossing if it meets the triangle at its end or along the plane
                        if (oq == 0 && ExactTriangleIntersection.SegmentIntersectsTriangle(p, q, ref tri)) {
                            degenerate = true;
                            return w;
                        }
                        continue;
                    }
                    if (op == oq)
                        continue;
                    int s1 = ExactPredicates.Orient3d(p, q, a, b), s2 = ExactPredicates.Orient3d(p, q, b, c), s3 = ExactPredicates.Orient3d(p, q, c, a);
                    bool pos = s1 > 0 || s2 > 0 || s3 > 0, neg = s1 < 0 || s2 < 0 || s3 < 0;
                    if (pos && neg)
                        continue;           // the line passes beside the triangle
                    if (s1 == 0 || s2 == 0 || s3 == 0) {
                        degenerate = true;  // through an edge or a vertex
                        return w;
                    }
                    w += op > 0 ? -1 : 1;     // (Orient3d is positive on the side the normal points to: from behind the triangle, inside, out)
                }
                return w;
            }

            /// <summary> the winding number of the closed shell (triangles tris of mesh, box its box) at p; see ShellIndex.ExactWinding </summary>
            internal static bool ExactWinding(DMesh3 mesh, int[] tris, AxisAlignedBox3d box, Vector3d p, out int winding, Func<int, Vector3d> jitter = null)
            {
                return new ShellIndex(mesh, tris, box).ExactWinding(p, out winding, jitter);
            }

            readonly DMesh3 shell;
            readonly DMeshAABBTree3 tree;
            readonly AxisAlignedBox3d box;
            readonly double touch;

            public ShellIndex(DMesh3 mesh, int[] tris, AxisAlignedBox3d box)
            {
                shell = new DSubmesh3(mesh, tris).SubMesh;
                tree = new DMeshAABBTree3(shell, true);
                this.box = box;
                // the distance below which a probe counts as touching the shell: a few hundred units of rounding of the largest
                // coordinate (a mesh that was moved or rotated has its touching faces apart by that much)
                double magnitude = 0;
                for (int k = 0; k < 3; ++k)
                    magnitude = Math.Max(magnitude, Math.Max(Math.Abs(box.Min[k]), Math.Abs(box.Max[k])));
                touch = 256 * 2.220446049250313e-16 * magnitude;
            }

            // does p lie on the shell, or within the rounding of its coordinates of it?
            bool touches(Vector3d p)
            {
                double distance;
                return tree.FindNearestTriangleByClosestPoint(p, out distance) != DMesh3.InvalidID && distance <= touch;
            }

            // the triangles of the shell whose boxes meet the box q (the tree's boxes are supersets of the triangles' bounds, padded by
            // ComputeBoxPadding, so no triangle that meets q is missed)
            int[] near(AxisAlignedBox3d q)
            {
                var list = new List<int>();
                var trav = new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (b, depth) => b.Intersects(q),
                    NextTriangleF = (t) => list.Add(t)
                };
                tree.DoTraversal(trav);
                return list.ToArray();
            }

            /// <summary>
            /// Is the cavity (triangles cavity of mesh, a closed shell that does not cross this one) inside this shell (a positive
            /// winding number)? A cavity and this shell share at most points where they touch, so the cavity is inside or outside as
            /// a whole and one point of it that is not on this shell decides. The probes are the cavity's vertices in the order of
            /// their index (the input doubles, points of the cavity: the winding number at each is exact); a vertex outside the box
            /// is outside. A vertex on the shell, or within a few hundred units of rounding of it (touch), is skipped: a mesh that
            /// was moved or rotated has the touching faces of the two shells apart by that much, and which side a vertex falls on
            /// is noise. Only if every vertex touches the shell, the centroids of the cavity's triangles follow (the largest
            /// triangle first, ties by index; one outside the box or touching the shell is skipped): a centroid is a rounded point,
            /// so this fallback is not exact. False if no probe decides
            /// </summary>
            public bool Contains(DMesh3 mesh, int[] cavity)
            {
                var set = new HashSet<int>();
                foreach (int t in cavity) {
                    Index3i tv = mesh.GetTriangle(t);
                    set.Add(tv.a); set.Add(tv.b); set.Add(tv.c);
                }
                int[] vertices = new int[set.Count];
                set.CopyTo(vertices);
                Array.Sort(vertices);
                int w;
                foreach (int v in vertices) {
                    Vector3d p = mesh.GetVertex(v);
                    if (!touches(p) && ExactWinding(p, out w))
                        return w > 0;
                }
                var area = new Dictionary<int, double>();
                foreach (int t in cavity)
                    area[t] = mesh.GetTriArea(t);
                int[] byArea = (int[])cavity.Clone();
                Array.Sort(byArea, (x, y) => { int c = area[y].CompareTo(area[x]); return c != 0 ? c : x.CompareTo(y); });
                foreach (int t in byArea) {
                    Vector3d p = mesh.GetTriCentroid(t);
                    if (!box.Contains(p) || touches(p))
                        continue;
                    if (ExactWinding(p, out w))
                        return w > 0;
                }
                return false;
            }

            /// <summary>
            /// The winding number of the shell at p, exactly: false if p lies on the shell (exact orientation and point in the
            /// closed triangle), or if no direction avoided every edge and vertex (not expected). The count is taken along a
            /// segment from p to just beyond the nearest face of the shell's box (its direction jittered by a fixed seed, and
            /// another direction if the segment passes through an edge or a vertex), so only the triangles near it are tested.
            /// jitter (for tests): the jitter of attempt i instead of the seeded one
            /// </summary>
            public bool ExactWinding(Vector3d p, out int winding, Func<int, Vector3d> jitter = null)
            {
                winding = 0;
                if (!box.Contains(p))
                    return true;
                foreach (int t in near(new AxisAlignedBox3d(p))) {
                    var tri = new Triangle3d();
                    shell.GetTriVertices(t, ref tri.V0, ref tri.V1, ref tri.V2);
                    if (ExactPredicates.Orient3d(tri.V0, tri.V1, tri.V2, p) == 0 && ExactTriangleIntersection.SegmentIntersectsTriangle(p, p, ref tri))
                        return false;
                }
                // the nearest face of the box: its axis and side
                int axis = 0, side = 1;
                double best = double.MaxValue;
                for (int k = 0; k < 3; ++k) {
                    double lo = p[k] - box.Min[k], hi = box.Max[k] - p[k];
                    if (lo < best) { best = lo; axis = k; side = -1; }
                    if (hi < best) { best = hi; axis = k; side = 1; }
                }
                double margin = 1e-3 * box.DiagonalLength + 1e-300;
                var rng = new Random(20261119);
                for (int attempt = 0; attempt < 64; ++attempt) {
                    var dir = jitter != null ? jitter(attempt) : new Vector3d(0.25 * (rng.NextDouble() - 0.5), 0.25 * (rng.NextDouble() - 0.5), 0.25 * (rng.NextDouble() - 0.5));
                    dir[axis] = side;
                    dir = dir.Normalized;
                    // long enough to leave the box beyond the nearest face (and a bit more), whatever the jitter
                    double reach = (best + margin) / Math.Abs(dir[axis]) + margin;
                    Vector3d q = p + dir * reach;
                    if (box.Contains(q))
                        continue;
                    var seg = new AxisAlignedBox3d(p);
                    seg.Contain(q);
                    bool degenerate;
                    int w = CrossingsAlong(shell, near(seg), p, q, out degenerate);
                    if (!degenerate) {
                        winding = w;
                        return true;
                    }
                }
                return false;
            }
        }

        ExactMeshBoolean runBoolean(List<DMesh3> meshes)
        {
            var op = new ExactMeshBoolean(meshes, Operation);
            op.Orientation = Orientation;
            if (ConfigureBoolean != null)
                ConfigureBoolean(op);
            op.AllowOpenMeshes = false;
            op.StrictRounding = true;
            op.Cancellation = Cancellation;
            op.Compute();
            return op;
        }

        // the cheapest allowed repair that makes input i closed (on a copy), or null
        SolidBooleanInputRepair repair(int i, DMesh3 input, out DMesh3 repaired)
        {
            repaired = null;
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
                Report.StopDetail = Report.RemovedThinSolids.Count > 0 && Report.BooleanTriangles > 0
                    ? "MinThickness removed every solid of the boolean result" : "the boolean result has no triangles";
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
