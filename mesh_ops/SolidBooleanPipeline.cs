using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
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

    /// <summary>
    /// What a SolidBooleanPipeline run returned, as a fact next to its Status (SolidBooleanReport.ResultKind): it tells the results a caller
    /// treats differently apart, which Status (Success) and Clean do not: a clean solid, a solid with contacts, a solid with another defect, an
    /// empty result that is exact, and an empty result that the pipeline made empty. It judges nothing: Clean, NotCleanReason and OnlyContacts
    /// keep their meaning.
    /// </summary>
    public enum SolidBooleanResultKind
    {
        /// <summary> no result: Compute() failed (Status says why), was cancelled, was refused by Verification, or has not run </summary>
        None = 0,
        /// <summary> a non-empty result that is clean (Report.Clean). A result from which the rounding removed some parts, and that is clean, is a Solid too: Report.RoundingRemoved lists them </summary>
        Solid = 1,
        /// <summary>
        /// a non-empty result whose only defects are contacts of the exact solid (Report.OnlyContacts: the solids touch at points or along
        /// edges; NotCleanReason says "self-intersections" there). It is the exact result, valid, with duplicated vertices at the contacts
        /// </summary>
        SolidWithContacts = 2,
        /// <summary> a non-empty result with another defect: Report.NotCleanReason says which (and with "self-intersections", Report.OnlyContacts is false: a real crossing) </summary>
        SolidNotClean = 3,
        /// <summary>
        /// an empty result and the exact result is empty (eg the intersection of two boxes that touch in a plane, or of disjoint solids): the
        /// answer is right. Report.NotCleanReason is "empty" then, as for any empty mesh. "Exact" means exact for the inputs as fed to the exact
        /// stage: after an InputGrid snap (or the Grid step) a thin overlap may have been closed by the snap and the snapped solids only touch;
        /// Report.InputSnappedVertices above 0 is the hint
        /// </summary>
        EmptyExact = 4,
        /// <summary>
        /// an empty result that the pipeline made empty, although the exact result had volume (rounding, MinThickness) or possibly had volume
        /// (ThinInputs = TreatAsEmpty): the rounding removed all of it as a component thinner than its bound can resolve (Report.RoundingRemoved
        /// lists them: a real slab of 1e-12 at coordinates of 0.5), or MinThickness removed the solids (Report.RemovedThinSolids), or an input
        /// that MinThickness found thin was treated as empty (Report.ThinInputs; the exact stage never saw it, so the exact result may have been
        /// empty too). Not an answer that "the solids do not overlap"
        /// </summary>
        EmptyAfterRounding = 5
    }

    /// <summary> one input that was not closed in the winding number sense and was repaired before the boolean </summary>
    public sealed class SolidBooleanInputRepair
    {
        /// <summary> index of the input </summary>
        public int Index;
        /// <summary> "MeshAutoRepair", "MeshRobustRepair", or "MeshMiteredOffset" (the repair that made the input closed) </summary>
        public string Method;
        /// <summary> triangles of the input and of its repaired copy </summary>
        public int TrianglesBefore, TrianglesAfter;
        /// <summary> time of the repair or repairs of this input, in milliseconds (without the self-intersection check of a MeshAutoRepair copy, which Report.RepairMilliseconds includes) </summary>
        public long Milliseconds;
    }

    /// <summary>
    /// One step of the route a SolidBooleanPipeline run took for its inputs (Report.Route), in the order taken: what was done, why, and what it changed.
    /// The names so far: "SheetRole" (an open tool of a Difference is used as a sheet; after a cut, Change ends with the share of the first
    /// input's volume that the Difference removed), "Fallback" (the sheet role refused: the inputs are used as
    /// solids), "InputRepair" (an open input was closed on a copy), "Refused" (an open input is left and the run fails with OpenInput) and
    /// "Grid" (with AutomaticSteps: the run failed and is done once more with the inputs snapped to the cell in Grid; the steps after it are
    /// those of that run, or, where it failed too, those of the run that goes on without the grid).
    /// </summary>
    public sealed class SolidBooleanRouteStep
    {
        /// <summary> the name of the step, see the class </summary>
        public string Name;
        /// <summary> the input the step is about (index of Inputs), or -1 for a step about the run </summary>
        public int Input = -1;
        /// <summary> why the step was taken </summary>
        public string Reason;
        /// <summary> what the step changed </summary>
        public string Change;
        /// <summary> Fallback, and Grid after a refused sheet: the reason the sheet role refused (the witness is in Reason), else None </summary>
        public ExactMeshBoolean.SheetRefusal SheetReason = ExactMeshBoolean.SheetRefusal.None;
        /// <summary> for the step Grid: the cell the inputs were snapped to (a power of two); 0 otherwise </summary>
        public double Grid;
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

    /// <summary>
    /// one closed component of a closed input that was inside-out and thinner than its bound (1e-5 of its own extent), and that the pipeline
    /// dropped (from a copy) before the boolean instead of refusing the run (SolidBooleanReport.DroppedInsideOutComponents, #1555)
    /// </summary>
    public sealed class SolidBooleanDroppedComponent
    {
        /// <summary> index of the input it belongs to </summary>
        public int Input;
        /// <summary> its triangles </summary>
        public int Triangles;
        /// <summary> its signed volume (negative: inside-out) and its area </summary>
        public double Volume, Area;
        /// <summary> 2 |Volume| / Area, below Bound </summary>
        public double Thickness;
        /// <summary> the bound it was compared with: 1e-5 (the default ExactMeshBoolean.RoundingMaxDisplacement) times Extent (#1567; until then the box diagonal of the largest component of all closed inputs) </summary>
        public double Bound;
        /// <summary> the extent of the component itself, the size that Bound is relative to (#1567): the largest distance from its first vertex to its other vertices, between half its diameter and its diameter, the same in every orientation up to rounding </summary>
        public double Extent;
        /// <summary> the centre of its bounding box </summary>
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
        /// <summary>
        /// what the run returned, as a fact next to Status (see SolidBooleanResultKind): None without a result; Solid, SolidWithContacts or
        /// SolidNotClean for a non-empty result by Clean, OnlyContacts and NotCleanReason; EmptyExact or EmptyAfterRounding for an empty one.
        /// Set by Compute() for the result it returns (also after the Grid step, the sheet route's fallback or an input repair: the final
        /// result decides, and the removals it lists), None for a failed run
        /// </summary>
        public SolidBooleanResultKind ResultKind = SolidBooleanResultKind.None;
        /// <summary>
        /// the indices (as passed to the pipeline, ascending) of the closed inputs whose winding number is outside {0, 1} somewhere: 2 or more
        /// where the input overlaps itself (eg two overlapping copies of a box in one input, a cavity wall given outward), -1 where it is inside out.
        /// Read from the exact stage's winding numbers on both sides of the pieces of the arrangement, at no extra cost; empty if none, if the
        /// exact stage did not run, for an open input and a sheet, and for the pieces that the early-out of an Intersection or a Difference
        /// culled outside the first input (or of all inputs for an Intersection). The inputs of the run that is the result (after a Grid
        /// step or a fallback). It is a signal about the inputs, not a judgement of the result: the result is exact for the inputs as given, in
        /// winding number semantics, so a flagged input is one whose meaning may differ from what the caller meant. Status, Clean, NotCleanReason and
        /// OnlyContacts keep their meaning. Also set for a run that fails after the classification: InsideOutInput, a Verification refusal, a Cancelled after the boolean (ResultKind is None). Empty for a failure of the boolean itself: RoundingNotClean, OpenInput, BudgetExceeded, Cancelled during the boolean. It is computed on the pieces the exact stage classified, with the early-out cull on
        /// </summary>
        public List<int> InputsWithWindingOutsideZeroOne = new List<int>();
        /// <summary> triangles per input as given (before any repair) </summary>
        public int[] InputTriangles = new int[0];
        /// <summary> per input: ExactMeshBoolean.IsWindingClosed of the input as given (with InputGrid: of its snapped copy) </summary>
        public bool[] InputClosed = new bool[0];
        /// <summary> with InputGrid, or in the retry of the Grid step (AutomaticSteps): the input vertices that the snap moved (of all inputs); 0 otherwise </summary>
        public int InputSnappedVertices;
        /// <summary> the inputs that were repaired, with the method (empty if none) </summary>
        public List<SolidBooleanInputRepair> RepairedInputs = new List<SolidBooleanInputRepair>();
        /// <summary>
        /// the route of the inputs that are not closed, as the steps taken in their order, each with its reason and what it changed (see
        /// SolidBooleanRouteStep), and the Grid step of AutomaticSteps; empty when no input was tried as a sheet, repaired or refused as open
        /// and no Grid step ran
        /// </summary>
        public List<SolidBooleanRouteStep> Route = new List<SolidBooleanRouteStep>();
        /// <summary> triangles of the exact boolean result (before the reduction) </summary>
        public int BooleanTriangles;
        /// <summary> the boolean's InsideOutCells: the components of the closed inputs that are inside-out (see ExactMeshBoolean.InsideOutCells) </summary>
        public int InsideOutCells;
        /// <summary> the boolean's InsideOutUndecided: components whose inside-out test found no ray (not expected) </summary>
        public int InsideOutUndecided;
        /// <summary> the boolean's Contacts and NonManifoldEdges (touching solids, see ExactMeshBoolean.Contacts); with MinThickness, ContactsBoolean counts what is left after the removal </summary>
        public int ContactsBoolean, NonManifoldEdgesBoolean;
        /// <summary>
        /// components of the boolean result thinner than the strict rounding bound (ExactMeshBoolean.Rounding.ScaleDisparity),
        /// which the strict boolean refused although the rounding left no intersection, degenerate triangle or false contact:
        /// the pipeline keeps that result (BooleanStatus stays RoundingNotClean) and counts them here; 0 otherwise. With
        /// MinThickness they are removed if they are thinner than it (RemovedThinSolids)
        /// </summary>
        public int ThinComponentsKept;
        /// <summary>
        /// the closed components the boolean's rounding removed (ExactMeshBoolean.Rounding.RemovedComponentInfos: triangles, area, volume,
        /// thickness, reason), also where the run failed with BooleanStatus RoundingNotClean because one of them is a solid its
        /// coordinates resolve (#1216); empty if none
        /// </summary>
        public List<MeshRoundingRepair.RemovedComponentInfo> RoundingRemoved = new List<MeshRoundingRepair.RemovedComponentInfo>();
        /// <summary>
        /// the signed volume of the boolean's result before and after its rounding repair (MeshRoundingRepair.VolumeBefore, VolumeAfter):
        /// their difference is the volume the rounding moved, removed components included (RoundingRemoved lists those). Equal when the
        /// rounding changed nothing; both 0 when no rounding repair ran. They describe the rounding only: MinThickness and the reduction
        /// change the volume after it
        /// </summary>
        public double RoundingVolumeBefore, RoundingVolumeAfter;
        /// <summary>
        /// Verification NoInventedSurface: the share of the boolean result's area (before the reduction) in triangles with a sample farther than
        /// InventedSurfaceTolerance from every input as passed, the largest distance of a sample to an input surface, and the tolerance used;
        /// the distance of a sample that the fast path accepted is the exact distance to the triangle it accepted (an upper bound of the nearest distance,
        /// at most the tolerance), that of a far sample the exact nearest distance; all 0 without the mode or without a result surface
        /// </summary>
        public double InventedSurfaceShare, InventedSurfaceMaxDistance, InventedSurfaceTolerance;
        /// <summary> Verification NoInventedSurface: the area of the invented triangles (the share is the area over that of the result) </summary>
        public double InventedSurfaceArea;
        /// <summary> Verification NoInventedSurface: the number of the invented triangles (those with a sample farther than the tolerance) </summary>
        public int InventedTriangles;
        /// <summary> Verification RoundingVolume: |RoundingVolumeAfter - RoundingVolumeBefore| / |RoundingVolumeBefore| (0 when that is 0, or no rounding repair ran), the value MaxRoundingVolumeChange is compared with </summary>
        public double RoundingVolumeChange;
        /// <summary> the time of the Verification checks (0 without them) </summary>
        public long VerificationMilliseconds;
        /// <summary> the solids of the boolean result that MinThickness removed (empty without it) </summary>
        public List<SolidBooleanThinSolid> RemovedThinSolids = new List<SolidBooleanThinSolid>();
        /// <summary>
        /// the inside-out components of the closed inputs that were thinner than 1e-5 of their own extent and were dropped before the
        /// boolean (see SolidBooleanPipeline.AllowInsideOutInputs); empty otherwise
        /// </summary>
        public List<SolidBooleanDroppedComponent> DroppedInsideOutComponents = new List<SolidBooleanDroppedComponent>();
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
        /// only with NotCleanReason "self-intersections" (0 otherwise, nothing is computed): the triangles of the returned mesh
        /// that cross another triangle beyond what they share, exact (the set of CrossingTriangles over all vertices; vertices at
        /// one position count as one: a touch at a contact is not a crossing, a triangle that passes through another one is).
        /// Conservative in these cases of touching, which count as crossings: coincident, oppositely oriented triangles of a face
        /// contact without welded vertices (three shared positions, the far corners coincide), and a touch at a vertex or an
        /// edge without a duplicated vertex (eg a box standing on the face of another one), and in general two solids that touch
        /// along a plane with different triangulations (their coplanar triangles overlap and share at most one position). So the
        /// count is an upper bound of
        /// the true crossings and 0 means there is none. NotCleanReason reports degenerate triangles before self-intersections,
        /// so none is degenerate here. -1: the mesh has more than 20000 intersecting triangle pairs (contacts included): they are not
        /// counted, so that a heavily self-intersecting result does not make the final check expensive (and OnlyContacts is false)
        /// </summary>
        public int SelfIntersectingTriangles;
        /// <summary>
        /// true if NotCleanReason is "self-intersections" only because of contacts: ContactsResult &gt; 0 and
        /// SelfIntersectingTriangles == 0 (no triangle crosses another beyond what they share, with the two conservative cases of SelfIntersectingTriangles counted as crossings). False if the result is clean,
        /// has another NotCleanReason, or has a real crossing (a crossing next to a contact is told from a contact alone by this)
        /// </summary>
        public bool OnlyContacts;
        /// <summary>
        /// the returned mesh is clean (NotCleanReason == ""). A result with contacts is valid (manifold, with duplicated
        /// vertices where the solids touch) but NotCleanReason reports it as "self-intersections" at the touching points:
        /// with ContactsResult &gt; 0 look at OnlyContacts and SelfIntersectingTriangles before treating "self-intersections" as a
        /// defect (it is the last check of NotCleanReason, so closed, bowties, degenerate triangles and orientation have passed;
        /// OnlyContacts tells a contact alone from a real crossing, with or without a contact next to it). An empty result is Status Success with NotCleanReason "empty" and Clean false
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
        /// with SeparateSelfContacts: the positions of the result where one solid touches itself (two or more vertices of one
        /// solid at one position; a solid is an outer shell with its cavities), before and after the separation; 0 without the option
        /// </summary>
        public int SelfContactsFound, SelfContactsLeft;
        /// <summary> with SeparateSelfContacts: the vertices moved </summary>
        public int SelfContactVerticesMoved;
        /// <summary>
        /// with SeparateSelfContacts: the contact vertices not moved because the move would have been at or below 16 units in the
        /// last place of their coordinates (Skipped), and the moves put back because a triangle then crossed another one or was
        /// degenerate (UndoneCrossing) or turned over (UndoneFlipped)
        /// </summary>
        public int SelfContactVerticesSkipped, SelfContactMovesUndoneCrossing, SelfContactMovesUndoneFlipped;
        /// <summary>
        /// with SeparateSelfContacts: true if the moves were all put back because the final check made again after them found the result
        /// worse (Solid > SolidWithContacts > SolidNotClean, eg a triangle that a move shrank below the tolerance of NotCleanReason without
        /// making it exactly degenerate); the result is then the one without the option, SelfContactsLeft is SelfContactsFound and
        /// SelfContactVerticesMoved is 0
        /// </summary>
        public bool SelfContactMovesRevertedAsWorse;
        /// <summary> with SeparateSelfContacts: the largest move of a vertex (up to the distance, plus the rounding of the coordinates) </summary>
        public double SelfContactMaxMove;
        /// <summary>
        /// PairContactsMode Auto: the second reduction (pairing on) ran because the first result was not clean with contacts left; it
        /// is kept (PairPassKept) if its NotCleanReason is the same or empty and it has fewer triangles (or as many and fewer contacts). False if it did not run
        /// </summary>
        public bool PairPassRan, PairPassKept;
        /// <summary> with PairPassRan: triangles of the first (unpaired) reduction's result </summary>
        public int PairPassFirstTriangles;
        /// <summary> time of the second reduction (PairPassRan) with its final check, in milliseconds; part of TotalMilliseconds, not of ReductionMilliseconds </summary>
        public long PairPassMilliseconds;
        /// <summary> time per stage in milliseconds: the input checks, the input repairs, the boolean, the reduction, the final check (with SeparateSelfContacts including the separation), and the sum </summary>
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

        // the four fields of the final check (FinalCheck), taken over from the report of another check
        internal void SetFinalCheck(SolidBooleanReport from)
        {
            ContactsResult = from.ContactsResult;
            NotCleanReason = from.NotCleanReason;
            SelfIntersectingTriangles = from.SelfIntersectingTriangles;
            OnlyContacts = from.OnlyContacts;
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
    /// so an STL-like soup of a closed solid is closed). An open input of a Difference that is not the first and has no entry in
    /// InputRoles is tried as a sheet first (ExactMeshBoolean.InputRole.Sheet: the first input is cut along it exactly, nothing is
    /// repaired); if the sheet role refuses, the run is done again with the inputs as solids, as described next, and Report.Route
    /// lists the steps and their reasons. An input that fails and that the boolean refuses as open fails the
    /// run with Status OpenInput by default (InputRepair = Refuse): a missing face is missing information, and no repair
    /// knows what the input means (a solid with a hole, or an open cutting surface). An input that is closed once its
    /// flipped triangles agree is not open: the boolean refuses it with InconsistentOrientation by default (see
    /// Orientation). Only on request is an open input repaired, on a copy: with InputRepair = AutoRepair by MeshAutoRepair,
    /// with AutoRepairThenRobust also by MeshRobustRepair (which can resolve, solidify or wrap, so it changes the geometry
    /// more) as the fallback, or with ThickenOpenSheet by MeshMiteredOffset (DoubleSided thickening into a closed mitered
    /// shell). RepairedInputs of the report names each repaired input and the method.</description></item>
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
            /// almost nothing, and the cut would remove almost nothing (#1211). Where that fill crosses the input (an open sheet whose
            /// surface crosses the fill of its boundary loop, eg a non-planar or wavy sheet), the closed copy self-intersects: the
            /// pipeline checks every MeshAutoRepair copy exactly for self-intersections (its own ExactMeshArrangement) and fails
            /// with Status OpenInput if the copy has some and the input as given has none (an input that self-intersects already
            /// is used as before; the check runs only for repaired inputs). That also refuses a flat sheet, whose fill lies on it
            /// (coincident faces count). A sheet whose fill neither crosses nor touches it (eg a U-shaped sheet, whose fill closes
            /// its opening) still gives a closed copy that encloses the wrong solid, with Status Success. Use it only for
            /// inputs known to be solids with small holes
            /// </summary>
            AutoRepair = 1,
            /// <summary>
            /// MeshAutoRepair, and if the copy is still not closed, MeshRobustRepair of the input (it may solidify or wrap: the geometry
            /// can change). A MeshRobustRepair result is used only if its Apply() is true: a refused one (false, eg ThinShellWrap, a lost part,
            /// NestingAmbiguous, ScaleDisparity (a clean mesh flagged only by the degenerate-area diagnostic, components far apart compared with their size); the mesh may be closed but is not a repair of the input) is not used and the run fails with Status OpenInput,
            /// Error naming the ResultReason.
            /// MeshRobustRepair returns Apply() true also for an unclean solidify result (ResultReason = SolidifyReason, eg self-intersections), and the pipeline still uses such a winding-closed result: a known gap, a candidate follow-up. A MeshAutoRepair copy that self-intersects where the input does not is refused as with AutoRepair
            /// (Status OpenInput), without falling back to MeshRobustRepair
            /// </summary>
            AutoRepairThenRobust = 2,
            /// <summary>
            /// Thicken open inputs into closed mitered solids of thickness 2 * halfThickness using <see cref="MeshMiteredOffset"/>
            /// (<see cref="MeshMiteredOffset.OffsetModes.DoubleSided"/>, Cao et al. SIGGRAPH 2026) instead of hole-filling or alpha wrapping.
            /// Designed for intentionally open CAD / sheet-metal shells in Union, Intersection, or Difference where planar faces and
            /// sharp creases should stay crisp with low triangle count. Closed inputs and inputs with explicit role
            /// <see cref="ExactMeshBoolean.InputRole.Sheet"/> are left untouched.
            /// Note: <see cref="MinThickness"/> evaluates unthickened inputs before repair (open sheets have thickness 0), and
            /// <see cref="VerificationModes.NoInventedSurface"/> compares against the original unthickened inputs (so offset faces count as invented surface).
            /// </summary>
            ThickenOpenSheet = 3
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
            /// <summary> an input is not closed and InputRepair is Refuse, or no allowed repair closed it, or MeshAutoRepair closed it into a self-intersecting mesh (see Error and InputRepairModes.AutoRepair) </summary>
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
            InconsistentOrientation = 7,
            /// <summary>
            /// a closed input is inside-out (the winding number in front of a component of it is odd and negative: its solid is
            /// empty or reversed) and AllowInsideOutInputs is false (Report.InsideOutCells counts the components); Result is null
            /// </summary>
            InsideOutInput = 8,
            /// <summary>
            /// Verification measured a result that exceeds a limit (MaxInventedSurfaceShare or MaxRoundingVolumeChange); Result is null, Error names the check,
            /// the measured value and the limit, and Report keeps the run (the route with its Verification step, the measurements)
            /// </summary>
            VerificationFailed = 9
        }

        /// <summary> the checks of a finished result that Verification switches on (opt-in; combine with |) </summary>
        [Flags]
        public enum VerificationModes
        {
            /// <summary> no check: nothing is built or measured, and the run is the one without the option, bit for bit (default) </summary>
            None = 0,
            /// <summary>
            /// the boolean result lies on the surfaces of the inputs as the caller passed them, up to what the pipeline itself may move a point by. A
            /// Boolean only keeps pieces of the inputs' surfaces, so surface that lies on none of them was made up: the hole fill of an input repair that
            /// closed an open tool, a wrap of MeshRobustRepair, or a defect. Measured by sampling (see Verification); the share of the result's area
            /// that is invented is limited by MaxInventedSurfaceShare
            /// </summary>
            NoInventedSurface = 1,
            /// <summary>
            /// the volume that the rounding repair moved (Report.RoundingVolumeBefore, RoundingVolumeAfter) stays within MaxRoundingVolumeChange of the
            /// volume before it, unless the change is below a floor for slivers (see Verification)
            /// </summary>
            RoundingVolume = 2
        }

        /// <summary>
        /// false (default): an input that is closed and consistently oriented but inside-out (eg a cube whose faces all point
        /// inward, an empty solid for the boolean) fails the run with Status InsideOutInput instead of being taken as an empty
        /// solid silently. true: such an input is used as it is (Report.InsideOutCells still counts it). Orientation = ByNesting
        /// derives the solid the surface encloses instead for an input that is embedded (no self-intersections); a closed input
        /// that self-intersects keeps its given orientation under ByNesting (see ExactMeshBoolean.OrientationModes), so a part of it
        /// that is inside-out stays so and is refused again: the Error then names the self-intersections, and a closed copy that
        /// the input repair produced (Report.RepairedInputs).
        /// One exception (owner 2026-11-25, #1555): with AllowInsideOutInputs false and Orientation not ByNesting, a closed component
        /// of a closed input (closed and consistently oriented by its edges) whose exact volume is negative, whose thickness 2 |Volume| / Area is below the
        /// bound (1e-5, the default ExactMeshBoolean.RoundingMaxDisplacement, times the extent of the component itself (SolidBooleanDroppedComponent.Extent):
        /// two nearly coincident sheets, thin by their own extent; fixed, ConfigureBoolean does not change it; #1567) and that lies outside the rest of its input
        /// at a vertex (exact winding number; a cavity, every vertex inside, stays, a thin one too) is
        /// dropped from a copy of the input before the boolean and listed in Report.DroppedInsideOutComponents: two nearly
        /// coincident sheets of the wrong orientation, a residue that the rounding cannot resolve; the run is not refused for it.
        /// Never all of an input: an input that is nothing but such components is used as it is. The bound does not depend on the
        /// other inputs or on the other components of the input (until #1567 it was relative to the largest component of all closed
        /// inputs, so a real 10 x 10 x 0.01 plate of one input could be dropped next to a large other input): a flipped part that is
        /// not a sheet-like residue by its own size is refused (InsideOutInput) whatever the inputs around it. Check
        /// Report.DroppedInsideOutComponents (Status and Report.Clean do not show a drop)
        /// </summary>
        public bool AllowInsideOutInputs = false;

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
        /// Half-thickness (offset distance on each side; total wall thickness 2 * OpenSheetHalfThickness) used when
        /// <see cref="InputRepair"/> is <see cref="InputRepairModes.ThickenOpenSheet"/>. Must be finite and &gt;= 0.
        /// When 0 (default), <see cref="RelativeOpenSheetHalfThickness"/> times the open input's bounding-box diagonal is used.
        /// </summary>
        public double OpenSheetHalfThickness = 0;
        /// <summary>
        /// Half-thickness as a fraction of the open input's bounding-box diagonal when <see cref="InputRepair"/> is
        /// <see cref="InputRepairModes.ThickenOpenSheet"/> and <see cref="OpenSheetHalfThickness"/> == 0 (default 0.01, i.e., 2% wall thickness; must be finite and &gt; 0).
        /// </summary>
        public double RelativeOpenSheetHalfThickness = 0.01;
        /// <summary> Miter limit forwarded to <see cref="MeshMiteredOffset.MiterLimit"/> when <see cref="InputRepair"/> is <see cref="InputRepairModes.ThickenOpenSheet"/> (default 4.0; must be finite and in [1, 1e4]). </summary>
        public double OpenSheetMiterLimit = 4.0;
        /// <summary>
        /// the role of each input (entry i for Inputs[i]; null or a shorter list: the others are Solid). An input with the role Sheet
        /// (<see cref="ExactMeshBoolean.InputRole.Sheet"/>) is an open, oriented cutting surface, the tool of a Difference: it is handed to the boolean
        /// as given (never repaired, reoriented, or counted as an open input), the boolean cuts the first input along it exactly, and refuses
        /// (BooleanStatus SheetRefused, SheetReason) where the sheet does not define the sides; see <see cref="ExactMeshBoolean.SheetRefusal"/>.
        /// A sheet in a Union or Intersection, or as the first input, is refused. InputGrid still snaps its coordinates.
        /// An entry here is a statement and is used as it is. An open input of a Difference without an entry (not the first) is tried as a sheet
        /// and, if the sheet role refuses, first tried once more on the data grid when the refusal is RimInsideWorkpiece (AutomaticSteps), then used as a solid
        /// (see Compute and Report.Route); the entry Solid switches that off for an input.
        /// </summary>
        public List<ExactMeshBoolean.InputRole> InputRoles;
        /// <summary> the side of the sheets that a part of the first input is on that no sheet touches (see <see cref="ExactMeshBoolean.SheetUncutSide"/>; default Refuse) </summary>
        public ExactMeshBoolean.SheetUncutSides UncutSide = ExactMeshBoolean.SheetUncutSides.Refuse;
        /// <summary>
        /// the reason of a refused sheet (BooleanStatus SheetRefused), else None; the witness is in Error. A sheet that the route tried and that
        /// refused is not the run's result: its reason is in the Fallback step (or, with AutomaticSteps, the Grid step: its SheetReason field and Reason text) of Report.Route
        /// </summary>
        public ExactMeshBoolean.SheetRefusal SheetReason = ExactMeshBoolean.SheetRefusal.None;
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
        /// <summary>
        /// The automatic steps of a run (AutomaticSteps); combine with |. The default is GridBeforeFallback | GridAfterRoundingNotClean; Grid, which
        /// includes both and also answers a stated sheet, is opt-in
        /// </summary>
        [Flags]
        public enum AutomaticStepModes
        {
            /// <summary> no automatic step </summary>
            None = 0,
            /// <summary>
            /// everything: a run that fails with RoundingNotClean or the sheet refusal RimInsideWorkpiece (of a sheet that the pipeline routed or
            /// that the caller stated) is done once more on a grid taken from the data. It includes GridBeforeFallback and GridAfterRoundingNotClean
            /// and adds the retry of a stated sheet
            /// </summary>
            Grid = 1,
            /// <summary>
            /// only the sheet route: an open tool of a Difference that the pipeline routes to the sheet role by itself (no entry in InputRoles) and
            /// that the sheet role refuses with RimInsideWorkpiece is done once more on the grid taken from the data, before the Fallback to solids
            /// (one extra boolean at most, none on a success). Other refusals, and a sheet that the caller stated, are not retried by this value; Grid
            /// retries a stated sheet. The snap closes any gap or skin up to about one cell between a rim and a face (see AutomaticSteps)
            /// </summary>
            GridBeforeFallback = 2,
            /// <summary>
            /// a run that fails with RoundingNotClean is done once more on the grid taken from the data (one extra boolean on the snapped inputs, none
            /// on a success). A sheet refusal is not retried by this value, so a sheet that the caller stated behaves as without the step. The result
            /// of a retry is the boolean of the SNAPPED inputs (within one cell of the given ones), see AutomaticSteps
            /// </summary>
            GridAfterRoundingNotClean = 4
        }
        /// <summary>
        /// Default GridBeforeFallback | GridAfterRoundingNotClean (owner 2026-11-24: the runs that the first default still refused with RoundingNotClean
        /// succeeded with the grid retry on private inputs, nearly all with the right volume). A default Success can therefore come with Report.Clean == false and
        /// Report.OnlyContacts == true (a solid whose parts touch): a caller who needs a clean solid checks Report.Clean, not only Status. GridBeforeFallback: an open tool of a Difference that the pipeline routed to the sheet role by itself (no entry in InputRoles) and that
        /// the sheet role refuses with RimInsideWorkpiece (a rim meant to lie in a face of the first input that sits a little inside it) is done once more
        /// on the grid of the data before the Fallback (LIMIT: it helps where the rim lies in a face parallel to a coordinate plane, or where every rim
        /// vertex coincides with a vertex of the workpiece, which the snap makes identical in any orientation; a rim vertex in the interior of a slanted
        /// face or on an edge of the workpiece that is not a vertex keeps its refusal, because the snap rounds each coordinate on its own), whatever InputRepair is; Report.Route shows SheetRole, Grid, SheetRole on success. A run
        /// without such a refusal is the run without this option, bit for bit; a refused sheet costs one more boolean at most. None: no automatic
        /// step (a refused sheet goes to the Fallback at once). GridAfterRoundingNotClean: a run that ends with the boolean's RoundingNotClean (any
        /// route, also after the fallback) is done once more on the grid, one extra boolean on the snapped inputs, and nothing on a run that does not fail
        /// that way. Grid includes both and also answers a sheet stated in InputRoles; it is not in the default, so that a caller who states roles keeps
        /// the refusal of a stated sheet as before. A RESULT OF THE RETRY IS THE BOOLEAN OF THE SNAPPED INPUTS: it can differ from the exact one by
        /// up to about one cell of the data (see CAPTURE DISTANCE), the route says so (Grid step: cell, vertices moved, size change), Report.RoundingVolumeBefore
        /// and RoundingVolumeAfter are those of the run that is the result, and the result can be reported as only contacts (Report.OnlyContacts).
        /// AutomaticSteps = None gives the exact run and its refusal; GridBeforeFallback keeps the sheet retry alone.
        /// The retry snaps all inputs as InputGrid does. The cell is a power of two, 2^12 times the rounding unit (ulp) of the largest absolute
        /// coordinate of the vertices that triangles use (about 4.5e-13 of that coordinate; 3.8e-6 at 5.7e6, 7.6e-6 at 1.2e7), so it scales with the
        /// inputs, and a vertex moves by at most half a cell per coordinate. CAPTURE DISTANCE: the faces of the first input move too, so a rim,
        /// gap or skin of up to about one cell between a tool and a face is closed. That is meant for a rim a few ulps inside a face, and it can
        /// change the topology of a result where the gap was by design: a tool that stops one micro-unit short of a face, far from the origin, then
        /// cuts through it, and an open bucket that leaves a thin skin under the top face gives an open pocket instead of an enclosed cavity (which
        /// the repair of the open tool would give). Features thinner than the cell can collapse, as with InputGrid. Set AutomaticSteps = None to
        /// switch the step off. The step is not run where the cell is more than 1/64 of the size of any input (thickness 2V/A of a closed
        /// input, width 2A/L of an open one, L the length of its boundary edges; the step says which input and the first failure is the result), and a
        /// retry with an empty result of a first input that is not empty is rejected the same way. These guards apply to the opt-in Grid retries too. It runs once, never on a success, and not when InputGrid is set (the caller's
        /// grid is used as stated). Its result is checked like any other. Report.Route gets a step "Grid" with the failure it answers, the cell,
        /// the number of vertices the snap moved and the largest relative change of an input's size (volume of a closed input, area of an open one);
        /// SheetReason is the refusal the step answers. If the retry fails too, the first failure is the
        /// result and the step says so; inputs that are on the cell already are not run again (the step says that). A snap makes coordinates
        /// equal: it cures contacts on axis-parallel faces and vertices meant to coincide, not a rim a little inside a slanted face.
        /// ConfigureBoolean runs once more for the retry (three times, not twice, for a refused rim that the grid does not cure). A value with
        /// other bits than those of AutomaticStepModes: InvalidInput
        /// </summary>
        public AutomaticStepModes AutomaticSteps = AutomaticStepModes.GridBeforeFallback | AutomaticStepModes.GridAfterRoundingNotClean;
        /// <summary>
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
        /// <summary>
        /// Default None (nothing is built or measured; the run is the one without this option, bit for bit). The checks of VerificationModes run on
        /// the run that is the result (after a Grid retry or the fallback) and fill the Report (InventedSurfaceShare, InventedSurfaceMaxDistance,
        /// InventedSurfaceTolerance, RoundingVolumeChange) also when they pass; a Route step "Verification" says which checks ran and their numbers. A
        /// result that exceeds a limit is refused: Status VerificationFailed, Result null. Bits other than those of VerificationModes: InvalidInput.
        /// NoInventedSurface: every triangle of the boolean's result (after MinThickness, before the reduction: the reduction is guarded by MaxDeviation
        /// and is no repair that adds surface) is sampled at its three vertices, its centroid and its three edge midpoints; it counts as invented when
        /// a sample is farther than InventedSurfaceTolerance from every input as the caller passed it (before the input repair, the grid snap and any
        /// reorientation; sheets and open inputs too). InventedSurfaceShare is the area of the invented triangles over the area of the result. The
        /// tolerance is 3 times (the bound the rounding repair applied, MeshRoundingRepair.MaxLength, or 256 ulp of the largest coordinate of the result and
        /// the inputs if that is more, plus the diagonal of the grid cell if a grid step or InputGrid snapped the inputs); it scales with the inputs.
        /// It costs one AABB tree per input and, per result triangle, seven nearest-triangle queries. It is a sampled check, not a proof: it misses an
        /// invented region whose samples all lie within the tolerance of some input surface (the spacing of the samples is the size of a triangle of
        /// the result, so a point inside a triangle can be farther than the tolerance while all of its samples pass; the share counts whole triangles; or
        /// a region that coincides with another input's surface). A correct result is not flagged: every point of it lies on an input surface up to the moves of the
        /// grid snap and the rounding, which the tolerance covers. A repair that closes a hole of a solid adds surface by design: its share is measured
        /// too. RoundingVolume: dV = |RoundingVolumeAfter - RoundingVolumeBefore| fails when it exceeds both MaxRoundingVolumeChange times
        /// |RoundingVolumeBefore| and a floor: RoundingVolumeFloorShare of the smallest |volume| of the closed inputs as passed (without a closed input,
        /// RoundingVolumeFloorDiagonal of the cubed diagonal of the inputs), a loss that is immaterial against the inputs. The change of a PART that is thinner
        /// than its own rounding length before and after the repair (RoundingVolumeThinChangeOf: 2 |volume| / area at most MaxDisplacement times the diagonal
        /// of the part, or 256 ulp) is taken out of dV first, each part by its own change: a rounding that moves every vertex by at most that length changes
        /// the volume of a component of area A by at most length * A, so a slab thinner than the length may lose or gain all of its volume legitimately (the
        /// repair removes such a component), which a purely relative rule would refuse. The parts that are thicker are judged by their own change alone, so a
        /// thin part never hides a loss of a thick one. A volume or floor that is not finite fails. Report.RoundingVolumeChange is
        /// dV / |RoundingVolumeBefore| of the whole result (0 when that is 0, or when no rounding repair ran)
        /// </summary>
        public VerificationModes Verification = VerificationModes.None;
        /// <summary> NoInventedSurface: the share of the result's area that may be invented (default 0: a tolerance that is a real bound makes the share of a correct result exactly 0, so any invented triangle is evidence); PositiveInfinity measures and reports only. Negative or NaN: InvalidInput </summary>
        public double MaxInventedSurfaceShare = 0;
        /// <summary> RoundingVolume: the relative change of the volume that the rounding may make (default 1e-3); PositiveInfinity measures and reports only. Negative or NaN: InvalidInput </summary>
        public double MaxRoundingVolumeChange = 1e-3;
        // RoundingVolume: the floor of dV as a share of the smallest closed input's |volume|: a change of the volume that is immaterial against the
        // inputs is not refused however large its share of a tiny result (the share of the result is the check's reading, this is its scale). It does NOT
        // cover a sliver result: a sliver thinner than the rounding bound has a volume of up to (bound * area), which is not below a share of the
        // inputs (a thin plate input makes the share small); the thin parts' own change is taken out of dV instead (RoundingVolumeThinChangeOf)
        internal const double RoundingVolumeFloorShare = 1e-6;
        // RoundingVolume: the floor of dV as a share of the cubed diagonal of the inputs when no input is a closed solid with volume
        internal const double RoundingVolumeFloorDiagonal = 1e-9;
        // NoInventedSurface: the safety factor of the tolerance (the tolerance adds the legitimate moves, which are bounds; a rare worst case of a
        // few of them at one point, or a diagonal taken from the box of the result instead of the largest component, stays inside)
        internal const double InventedSurfaceSafety = 3;
        // NoInventedSurface: the ulps of the largest coordinate that rounding the boolean's exact points to doubles may move a point by (the ulp term
        // of the rounding bound, MeshRoundingRepair.UlpFactor)
        internal const double InventedSurfaceUlps = 256;

        /// <summary>
        /// configures the boolean (eg MaxPieces); runs before AllowOpenMeshes = false and StrictRounding = true are set, which stay. It runs once for
        /// every boolean of a run: again after an input repair, again for the run without the route after a refused sheet, and again for the retry of the Grid
        /// step (AutomaticSteps; a refused rim that the grid does not cure runs the boolean three times: the sheet, the grid, the fallback) (Report.Route)
        /// </summary>
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
        /// <summary>
        /// Default 0 (off: the result is the one without this option, bit for bit). Otherwise a length: where a solid of the
        /// result touches itself at a vertex (two or more vertices of one solid at one position; a solid is an outer shell with
        /// the cavities inside it, so a cavity that touches its outer shell or another of its cavities counts; eg a difference
        /// whose tools touch each other at an edge or a corner inside the body), every vertex there moves into its own part of
        /// the solid by up to this distance (back into its own fan of triangles, along the area-weighted direction to their
        /// centroids; at most a quarter of its shortest edge; plus the rounding of the new coordinates, a few units in their last
        /// place; a move at or below 16 units in the last place of the largest coordinate of the vertex is skipped), so the solid no longer touches
        /// itself there. Such a result is a valid closed, manifold mesh, but a boundary representation that merges coincident
        /// vertices (a mesh-to-solid conversion of a CAD system) sees a non-manifold vertex there and refuses it. Only contacts at
        /// coincident vertices are found: a vertex that lies on an edge or a face of the same solid without a vertex there is not
        /// (NotCleanReason still reports it). A move is put back if a triangle of the vertex then crosses another triangle beyond
        /// what they share (an exact test; vertices at one position count as one), is degenerate (exact), or turns over (its
        /// normal against the one before, in floating point); putting back repeats until a check finds nothing, so the result
        /// is the state that was checked. A vertex put back stays put back, so a contact can be left although another choice of
        /// moves would separate it (Report.SelfContactsLeft). A contact of another solid at the same position is separated as well (the vertices of this solid move away from it); contacts elsewhere between different solids are not touched. The
        /// volume changes by about the moved distance times the area of the triangles around the moved vertices. The step runs
        /// after the reduction and the pair pass; if a vertex moved, the final check is made again (Report.Clean, NotCleanReason,
        /// ContactsResult, OnlyContacts, ResultKind): a result whose self-contacts are all separated and that is otherwise clean
        /// is a Solid, one with contacts left stays SolidWithContacts (also one whose moves are all put back because the check made again after them was worse than before, eg a triangle shrunk below its tolerance: Report.SelfContactMovesRevertedAsWorse; the result is then the one without the option). Verification does not see the moves (NoInventedSurface measures the boolean result before the reduction, RoundingVolume is that of the rounding); they are bounded by the distance. FinalCheckMilliseconds includes the time of this step.
        /// Report.SelfContactsFound / SelfContactsLeft / SelfContactVerticesMoved / SelfContactMaxMove /
        /// SelfContactVerticesSkipped / SelfContactMovesUndoneCrossing / SelfContactMovesUndoneFlipped say what was done.
        /// Negative or not finite: InvalidInput
        /// </summary>
        public double SeparateSelfContacts = 0;

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

        // the open tools of a Difference without a stated role that this run hands to the boolean as sheets (null: none)
        bool[] autoSheet;
        // the sheet role refused such a tool: the reason and the boolean's text (None: it did not, and the text is not read)
        ExactMeshBoolean.SheetRefusal autoRefusal;
        string autoRefusalText;
        // the refused sheet is one that this run routed (the boolean's witness mesh is an auto-routed tool), not one the caller stated
        bool autoRefusalRouted;
        // the steps of the run so far (they outlive the report of a refused sheet route)
        List<SolidBooleanRouteStep> route;
        // the first step of the current run in route (the steps before it belong to an earlier run of this Compute)
        int runStart;
        // the grid of the current run: InputGrid, or the cell of the Grid step
        double grid;

        void step(string name, int input, string reason, string change, ExactMeshBoolean.SheetRefusal sheetReason = ExactMeshBoolean.SheetRefusal.None)
        {
            route.Add(new SolidBooleanRouteStep { Name = name, Input = input, Reason = reason, Change = change, SheetReason = sheetReason });
        }

        /// <summary>
        /// runs the pipeline; true if Result is set (Status Success). An open input of a Difference that is not the first and has no entry in InputRoles
        /// goes to the sheet role first (the first input is cut along it exactly, nothing is repaired). If the sheet role refuses with RimInsideWorkpiece, the run is done once more on a
        /// grid taken from the data first (AutomaticSteps, default GridBeforeFallback | GridAfterRoundingNotClean; see there for what the snap can change). If the sheet role refuses
        /// otherwise, or the grid does not help, the run is done again with the inputs as solids, as without this route: InputRepair decides
        /// (Refuse: Status OpenInput, with the sheet's refusal in Error).
        /// Report.Route lists the steps taken and why. A stated role (an entry in InputRoles, Solid or Sheet) is used as stated.
        /// The sheet's orientation is taken as given: the part of the first input behind it is removed, so an open tool whose normals point
        /// inward removes the other part than its repaired, reoriented copy would; state the role Solid for a tool that is a solid with a hole
        /// and may be oriented either way. Only a refusal of the sheet falls back: any other failure of the boolean with the sheet (a budget,
        /// a strict rounding refusal) is the result. With MinThickness a tool without a stated role is measured as before, and one that is
        /// left out as thin is not routed. ConfigureBoolean runs once more for the run after a refused sheet. With AutomaticSteps = Grid a run
        /// that fails with RoundingNotClean or a stated sheet's refusal RimInsideWorkpiece is done once more on a grid taken from the data too (see there).
        /// </summary>
        public bool Compute()
        {
            var total = Stopwatch.StartNew();
            bool done = computeRoute(total);
            if (done && Verification != VerificationModes.None)
                done = verify(total);
            Report.ResultKind = KindOf(Status, Result, Report, ThinInputs, Operation);
            return done;
        }

        // the kind of the result that a run returned (None without one)
        internal static SolidBooleanResultKind KindOf(PipelineStatus status, DMesh3 result, SolidBooleanReport report, ThinInputModes thinInputs,
            ExactMeshBoolean.Operations operation = ExactMeshBoolean.Operations.Union)
        {
            if (status != PipelineStatus.Success || result == null)
                return SolidBooleanResultKind.None;
            if (result.TriangleCount == 0)
                return RemovedSomething(report, thinInputs, operation) ? SolidBooleanResultKind.EmptyAfterRounding : SolidBooleanResultKind.EmptyExact;
            if (report.Clean)
                return SolidBooleanResultKind.Solid;
            return report.OnlyContacts ? SolidBooleanResultKind.SolidWithContacts : SolidBooleanResultKind.SolidNotClean;
        }

        // the empty result of this run is not the exact result: the pipeline removed what the exact stage had (a component that the rounding repair
        // removed and that is not a residue, a solid that MinThickness removed, an input treated as empty)
        internal static bool RemovedSomething(SolidBooleanReport report, ThinInputModes thinInputs, ExactMeshBoolean.Operations operation)
        {
            foreach (MeshRoundingRepair.RemovedComponentInfo c in report.RoundingRemoved)
                if (c.Reason != MeshRoundingRepair.RemovalReasons.InsideOutResidue)
                    return true;
            if (report.RemovedThinSolids.Count > 0)
                return true;
            // (an input left out as thin removes volume from the result only if the result can shrink without it: a tool of a Difference cannot, so
            // the exact result is a subset of the empty one; any Union operand, the first operand of a Difference and any input of an Intersection can)
            if (thinInputs == ThinInputModes.TreatAsEmpty)
                foreach (SolidBooleanThinInput t in report.ThinInputs)
                    if (operation != ExactMeshBoolean.Operations.Difference || t.Index == 0)
                        return true;
            return false;
        }

        // the run with its steps (sheet route, Grid step, fallback); the result is checked by Compute (Verification)
        bool computeRoute(Stopwatch total)
        {
            route = new List<SolidBooleanRouteStep>();
            grid = InputGrid;
            bool gridTried = false;
            bool done = compute(total, true);
            if (autoRefusal != ExactMeshBoolean.SheetRefusal.None) {
                ExactMeshBoolean.SheetRefusal refusal = autoRefusal;
                string text = autoRefusalText;
                // the Grid step answers a refused sheet before the tool is given up as a sheet (one try: not again after the fallback)
                if (refusal == ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece) {
                    int stepsBefore = route.Count;
                    bool retried = gridRetry(total, true, "the sheet role refused: " + refusal + ": " + text, refusal, autoRefusalRouted);
                    // (one try: a step that was made, also a failed one, is the try; an option that does not answer a sheet leaves it for the failure after the fallback)
                    gridTried = route.Count > stepsBefore || retried;
                    if (retried)
                        return true;
                    if (Status == PipelineStatus.Cancelled)
                        return false;
                }
                long attempt = Report.BooleanMilliseconds;
                step("Fallback", -1, "the sheet role refused: " + refusal + ": " + text,
                    "the inputs are used as solids, an open one goes to InputRepair (" + InputRepair + ")", refusal);
                done = compute(total, false);
                // (the time of the refused sheet attempt is boolean time of this run)
                Report.BooleanMilliseconds += attempt;
                if (Status == PipelineStatus.OpenInput)
                    Error += " (the open tool was tried as a sheet first, which refused: " + refusal + ": " + text + ")";
                if (!gridTried && gridFailure())
                    done = gridRetry(total, false, Error, SheetReason);
            } else if (gridFailure())
                done = gridRetry(total, true, Error, SheetReason);
            return done;
        }

        // the failures of a run that the Grid step answers: the boolean's RoundingNotClean, and a stated sheet refused with its rim inside the workpiece
        bool gridFailure()
        {
            return Status == PipelineStatus.BooleanFailed && (BooleanStatus == ExactMeshBoolean.BooleanStatus.RoundingNotClean
                || (BooleanStatus == ExactMeshBoolean.BooleanStatus.SheetRefused && SheetReason == ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece));
        }

        // Whether AutomaticSteps answers the failure that the run has now: Grid answers every failure of gridFailure; GridBeforeFallback the refused sheet
        // that the pipeline routed itself (before the fallback); GridAfterRoundingNotClean the boolean's RoundingNotClean and no sheet refusal
        bool GridStepWanted(bool beforeFallback)
        {
            if ((AutomaticSteps & AutomaticStepModes.Grid) != 0)
                return true;
            if (beforeFallback && (AutomaticSteps & AutomaticStepModes.GridBeforeFallback) != 0)
                return true;
            return (AutomaticSteps & AutomaticStepModes.GridAfterRoundingNotClean) != 0 && Status == PipelineStatus.BooleanFailed
                && BooleanStatus == ExactMeshBoolean.BooleanStatus.RoundingNotClean;
        }

        // The Grid step (AutomaticSteps): the failed run once more, on the same route, with the inputs snapped to the cell of the data. True: the retry
        // is the result. False: no retry (the step is off, InputGrid is set, or the data gives no cell: no step; every vertex is on the cell already: a
        // step that says so), or it failed too; then the first failure is the result again, with the attempt as a step, except for a cancelled
        // retry (Status Cancelled). The stage times of the run that is not the result are added to those of the one that is
        bool gridRetry(Stopwatch total, bool sheetRoute, string reason, ExactMeshBoolean.SheetRefusal sheetReason, bool beforeFallback = false)
        {
            if (!GridStepWanted(beforeFallback) || InputGrid != 0)
                return false;
            double cell = DataGrid(Inputs);
            if (cell == 0)
                return false;
            string cellText = "2^" + CellExponent(cell) + " (" + cell.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
            var s = new SolidBooleanRouteStep { Name = "Grid", Reason = reason, Grid = cell, SheetReason = sheetReason };
            route.Add(s);
            // (inputs that are on the cell already: the retry would be the run that failed)
            if (OffGrid(Inputs, cell) == false) {
                s.Change = "every input vertex is on the cell " + cellText + " already, so the run is not done again and the first failure is the result";
                return false;
            }
            // the snap must not distort an input: the cell must be small against each input's own size (CellShareOfSize)
            string sizeText;
            if (!CellSmallAgainstInputs(cell, cellText, out sizeText)) {
                s.Change = sizeText;
                return false;
            }
            SolidBooleanReport firstReport = Report;
            PipelineStatus firstStatus = Status;
            string firstError = Error;
            ExactMeshBoolean.BooleanStatus firstBoolean = BooleanStatus;
            ExactMeshBoolean.SheetRefusal firstSheet = SheetReason;
            int mine = route.Count;
            grid = cell;
            bool done = compute(total, sheetRoute);
            grid = InputGrid;
            s.Change = "the inputs are snapped to the cell " + cellText + ", which moved " + Report.InputSnappedVertices + " vertices" + sizeText + ", and the run is done again";
            // an empty result of a non-empty first input means that the snap destroyed the inputs: the retry is rejected as a failed one
            bool destroyed = done && DestroyedByRetry(Inputs[0], Result);
            // (the retry is the result, also where it was cancelled: the time of the failed first run is time of this run)
            if ((done && !destroyed) || Status == PipelineStatus.Cancelled) {
                addStageTimes(Report, firstReport);
                return done;
            }
            s.Change += destroyed ? "; its result is empty although the first input is not, so it is rejected and the first failure is the result"
                : "; it failed too (" + Status + ": " + Error + "), so the first failure is the result";
            Result = null;
            route.RemoveRange(mine, route.Count - mine);
            addStageTimes(firstReport, Report);
            firstReport.TotalMilliseconds = total.ElapsedMilliseconds;
            Report = firstReport;
            Status = firstStatus;
            Error = firstError;
            BooleanStatus = firstBoolean;
            SheetReason = firstSheet;
            return false;
        }

        // The share of an input's own size that the cell may be at most. The snap moves a point by up to half a cell per coordinate, so it can change
        // an input by up to about one cell in every direction; an input whose size (thickness 2V/A of a closed one, width 2A/L of an open one) is
        // not many cells is distorted by the retry, in a way that the result's check does not see (it checks the result, not the inputs). At the
        // limit a size changes by about 1.6 %. The intended cases are far below it: a block of 10 at 1.2e6 (cell 9.5e-7, size 2.5): 4e-7; a block of
        // 0.1 x 0.1 x 0.05 at 5.7e6 (cell 3.8e-6, size 0.017): 2e-4; a plate of 1e-3 at 5.7e6 to 1.2e7 (cell 3.8e-6 to 7.6e-6): 4e-3 to 8e-3.
        // The share is a judgement, not a derived number (owner's call)
        internal const double CellShareOfSize = 1.0 / 64;

        // The cell against the size of every input with triangles. False: the cell is more than CellShareOfSize of the smallest size (text: which input
        // and why). True: text reports the largest relative change of an input's size by the snap (volume of a closed input, area of an open one).
        // The size is the thickness 2|V|/A for a closed input (what MinThickness measures), and for an input that is not winding-closed the width
        // 2A/L, A the area and L the length of its boundary edges (edges used by one triangle, by position): for a strip of width w and length l it is
        // w, for an open box of edge e (a bucket) 2.5e, for a square sheet of side e e/2, for a sheet with a hole or a slit the width of the
        // part between the rims. An open input without boundary edges gets the thickness; a closed input of volume 0 (a doubled sheet) is not measured
        bool CellSmallAgainstInputs(double cell, string cellText, out string text)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double smallest = double.MaxValue, worst = 0;
            int smallestInput = -1, worstInput = -1;
            string smallestKind = "", worstKind = "";
            for (int i = 0; i < Inputs.Count; ++i) {
                DMesh3 m = Inputs[i];
                if (m.TriangleCount == 0)
                    continue;
                bool closed = Report.InputClosed != null && i < Report.InputClosed.Length ? Report.InputClosed[i] : ExactMeshBoolean.IsWindingClosed(m);
                double v, a;
                MeasureInput(m, out v, out a);
                // (a closed input of volume 0, such as a sheet that is given twice with opposite orientations, has no thickness that the snap could distort:
                // it is not measured and does not stop the step; the volume is 0 up to the rounding of the measure, which is below 1e-13 * area * extent)
                if (closed && Math.Abs(v) <= 1e-13 * a * m.GetBounds().MaxDim)
                    continue;
                double boundary = closed ? 0 : BoundaryLength(m);
                bool strip = boundary > 0;
                double size = strip ? 2 * a / boundary : a > 0 ? 2 * Math.Abs(v) / a : 0;
                string kind = strip ? "width 2A/L" : "thickness 2V/A";
                if (size < smallest) {
                    smallest = size; smallestInput = i; smallestKind = kind;
                }
                int moved;
                double v2, a2;
                MeasureInput(Snapped(m, cell, out moved), out v2, out a2);
                double change = strip ? (a > 0 ? (a2 - a) / a : 0) : (v != 0 ? (v2 - v) / v : 0);
                if (worstInput < 0 || Math.Abs(change) > Math.Abs(worst)) {
                    worst = change; worstInput = i; worstKind = strip ? "area" : "volume";
                }
            }
            if (smallestInput >= 0 && cell > CellShareOfSize * smallest) {
                text = "the cell " + cellText + " is more than 1/64 of the size of input " + smallestInput + " (" + smallestKind + " = " + smallest.ToString("R", inv)
                    + "), which the snap would distort, so the run is not done again and the first failure is the result";
                return false;
            }
            text = worstInput < 0 ? "" : ", and changed the " + worstKind + " of input " + worstInput + " by " + worst.ToString("R", inv) + " of it (the largest relative change of an input)";
            return true;
        }

        // the length of the edges that one triangle uses, edges by position (vertices at one position are one)
        internal static double BoundaryLength(DMesh3 m)
        {
            // (one map: the number of triangles that use the edge, and its length; -0.0 is 0.0 in the key, as the two hash alike on every runtime)
            var edges = new Dictionary<(double, double, double, double, double, double), (int count, double length)>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    Vector3d p = m.GetVertex(tv[k]), q = m.GetVertex(tv[(k + 1) % 3]);
                    bool swap = p.x != q.x ? p.x > q.x : p.y != q.y ? p.y > q.y : p.z > q.z;
                    if (swap) { Vector3d u = p; p = q; q = u; }
                    var key = (p.x + 0.0, p.y + 0.0, p.z + 0.0, q.x + 0.0, q.y + 0.0, q.z + 0.0);
                    (int count, double length) e;
                    edges.TryGetValue(key, out e);
                    edges[key] = (e.count + 1, (q - p).Length);
                }
            }
            double sum = 0;
            foreach (var e in edges.Values)
                if (e.count == 1)
                    sum += e.length;
            return sum;
        }

        // the retry has an empty result although the first input has triangles
        internal static bool DestroyedByRetry(DMesh3 first, DMesh3 result)
        {
            return first != null && first.TriangleCount > 0 && (result == null || result.TriangleCount == 0);
        }

        // the stage times of a run that failed at its boolean (the input check, the repair, the boolean), added to those of the run that is the result
        static void addStageTimes(SolidBooleanReport to, SolidBooleanReport from)
        {
            to.InputCheckMilliseconds += from.InputCheckMilliseconds;
            to.RepairMilliseconds += from.RepairMilliseconds;
            to.BooleanMilliseconds += from.BooleanMilliseconds;
        }

        // true if the snap to the cell g would move a vertex of an input (as Snapped counts them)
        internal static bool OffGrid(IEnumerable<DMesh3> inputs, double g)
        {
            foreach (DMesh3 m in inputs)
                foreach (Vector3d p in UsedVertices(m))
                    if (!same(snap(p.x, g), p.x) || !same(snap(p.y, g), p.y) || !same(snap(p.z, g), p.z))
                        return true;
            return false;
        }

        /// <summary>
        /// the number of binades of the Grid step's cell above the rounding unit of the largest absolute input coordinate
        /// (docs/design/PIPELINE_GRID_STEP.md derives it)
        /// </summary>
        internal const int GridBinades = 12;

        // The cell of the Grid step: 2^GridBinades times the rounding unit (ulp) of the largest absolute finite coordinate of all inputs, a
        // power of two, so that inputs scaled by 2^k get the cell scaled by 2^k. 0: no cell (no finite coordinate other than 0, or the cell
        // would not be a normal number)
        internal static double DataGrid(IEnumerable<DMesh3> inputs)
        {
            double max = 0;
            // (the vertices that a triangle uses: one that no triangle uses does not move the cell, however far it is)
            foreach (DMesh3 m in inputs)
                foreach (Vector3d p in UsedVertices(m)) {
                    max = largerFinite(max, p.x);
                    max = largerFinite(max, p.y);
                    max = largerFinite(max, p.z);
                }
            // the exponent field of max is e + 1023 for max in [2^e, 2^(e+1)), and that of its ulp 2^(e-52) is 52 less
            long field = (BitConverter.DoubleToInt64Bits(max) >> 52) - 52 + GridBinades;
            return field < 1 ? 0 : BitConverter.Int64BitsToDouble(field << 52);
        }

        // the positions of the vertices that triangles use (a vertex of several triangles comes once per triangle corner)
        static IEnumerable<Vector3d> UsedVertices(DMesh3 m)
        {
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                yield return m.GetVertex(tv.a);
                yield return m.GetVertex(tv.b);
                yield return m.GetVertex(tv.c);
            }
        }

        static double largerFinite(double max, double x)
        {
            double a = Math.Abs(x);
            return a > max && !double.IsInfinity(a) ? a : max;
        }

        // the exponent of a normal power of two
        internal static int CellExponent(double cell) { return (int)(BitConverter.DoubleToInt64Bits(cell) >> 52) - 1023; }

        // one run; sheetRoute: open tools of a Difference without a stated role are sheets (autoSheet)
        bool compute(Stopwatch total, bool sheetRoute)
        {
            Result = null;
            Error = null;
            Status = PipelineStatus.NotComputed;
            BooleanStatus = ExactMeshBoolean.BooleanStatus.NotComputed;
            SheetReason = ExactMeshBoolean.SheetRefusal.None;
            Report = new SolidBooleanReport();
            Report.Route = route;
            runStart = route.Count;
            autoSheet = null;
            autoRefusal = ExactMeshBoolean.SheetRefusal.None;
            autoRefusalRouted = false;
            roundingRan = false;
            roundingExplained = 0;
            roundingThinParts = 0;
            roundingMeasureMs = 0;
            roundingBound = 0;
            VerificationSamples = 0;
            VerificationFallbacks = 0;

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
            if ((AutomaticSteps & ~(AutomaticStepModes.Grid | AutomaticStepModes.GridBeforeFallback | AutomaticStepModes.GridAfterRoundingNotClean)) != 0)
                return fail(PipelineStatus.InvalidInput, "AutomaticSteps is not a defined value", total);
            if ((Verification & ~(VerificationModes.NoInventedSurface | VerificationModes.RoundingVolume)) != 0)
                return fail(PipelineStatus.InvalidInput, "Verification is not a defined value", total);
            if (!(MaxInventedSurfaceShare >= 0) || !(MaxRoundingVolumeChange >= 0))
                return fail(PipelineStatus.InvalidInput, "MaxInventedSurfaceShare and MaxRoundingVolumeChange must not be negative or NaN", total);
            if (!(MinThickness >= 0) || double.IsInfinity(MinThickness))
                return fail(PipelineStatus.InvalidInput, "MinThickness must be finite and not negative", total);
            if (!(SeparateSelfContacts >= 0) || double.IsInfinity(SeparateSelfContacts))
                return fail(PipelineStatus.InvalidInput, "SeparateSelfContacts must be finite and not negative", total);
            if (ThinInputs < ThinInputModes.Keep || ThinInputs > ThinInputModes.Refuse)
                return fail(PipelineStatus.InvalidInput, "ThinInputs is not a defined value", total);
            if (Inputs == null || Inputs.Count == 0)
                return fail(PipelineStatus.InvalidInput, "no input meshes", total);
            for (int i = 0; i < Inputs.Count; ++i)
                if (Inputs[i] == null)
                    return fail(PipelineStatus.InvalidInput, "input " + i + " is null", total);
            if (InputRoles != null && InputRoles.Count > Inputs.Count)
                return fail(PipelineStatus.InvalidInput, "more InputRoles than inputs", total);
            if (UncutSide < ExactMeshBoolean.SheetUncutSides.Refuse || UncutSide > ExactMeshBoolean.SheetUncutSides.Inside)
                return fail(PipelineStatus.InvalidInput, "UncutSide is not a defined value", total);
            if (InputRepair < InputRepairModes.Refuse || InputRepair > InputRepairModes.ThickenOpenSheet)
                return fail(PipelineStatus.InvalidInput, "InputRepair is not a defined value", total);
            if (InputRepair == InputRepairModes.ThickenOpenSheet) {
                if (!MathUtil.IsFinite(OpenSheetHalfThickness) || OpenSheetHalfThickness < 0)
                    return fail(PipelineStatus.InvalidInput, "OpenSheetHalfThickness must be finite and >= 0", total);
                if (OpenSheetHalfThickness == 0 && (!MathUtil.IsFinite(RelativeOpenSheetHalfThickness) || !(RelativeOpenSheetHalfThickness > 0)))
                    return fail(PipelineStatus.InvalidInput, "RelativeOpenSheetHalfThickness must be finite and > 0", total);
                if (!MathUtil.IsFinite(OpenSheetMiterLimit) || !(OpenSheetMiterLimit >= 1.0) || OpenSheetMiterLimit > 1e4)
                    return fail(PipelineStatus.InvalidInput, "OpenSheetMiterLimit must be finite and in [1, 1e4]", total);
            }

            // 1. the snap to InputGrid (on copies), then the cheap check per input
            var clock = Stopwatch.StartNew();
            int n = Inputs.Count;
            List<DMesh3> given = Inputs;
            if (grid > 0) {
                given = new List<DMesh3>(n);
                for (int i = 0; i < n; ++i) {
                    int moved;
                    given.Add(Snapped(Inputs[i], grid, out moved));
                    Report.InputSnappedVertices += moved;
                }
            }
            Report.InputTriangles = new int[n];
            Report.InputClosed = new bool[n];
            bool anyOpen = false;
            for (int i = 0; i < n; ++i) {
                Report.InputTriangles[i] = Inputs[i].TriangleCount;
                Report.InputClosed[i] = ExactMeshBoolean.IsWindingClosed(given[i]);
                // the sheet route: an open tool of a Difference whose role is not stated (not when ThickenOpenSheet is explicitly requested)
                if (sheetRoute && InputRepair != InputRepairModes.ThickenOpenSheet && Operation == ExactMeshBoolean.Operations.Difference && i > 0 && !Report.InputClosed[i] && (InputRoles == null || i >= InputRoles.Count)) {
                    if (autoSheet == null)
                        autoSheet = new bool[n];
                    autoSheet[i] = true;
                }
                // (a sheet is open by role: not an open input to refuse or repair)
                anyOpen |= !Report.InputClosed[i] && !isSheet(i);
            }
            // the thin inside-out components of the closed inputs (#1555): dropped from copies before the boolean
            if (!AllowInsideOutInputs && Orientation != ExactMeshBoolean.OrientationModes.ByNesting) {
                var solid = new bool[n];
                for (int i = 0; i < n; ++i)
                    solid[i] = Report.InputClosed[i] && !isSheet(i);
                given = DropThinInsideOut(given, solid, Report.DroppedInsideOutComponents);
            }
            // the inputs thinner than MinThickness; active: the indices of the inputs the boolean gets
            var active = new List<int>(n);
            for (int i = 0; i < n; ++i)
                active.Add(i);
            if (MinThickness > 0) {
                var thin = new bool[n];
                for (int i = 0; i < n; ++i) {
                    // (a surface has no volume: it is not thin. Only by a stated role: an open tool that the route would try as a sheet is
                    // measured as before the route, so MinThickness and ThinInputs keep their meaning for it)
                    if (statedSheet(i))
                        continue;
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
                    anyOpen |= !Report.InputClosed[i] && !isSheet(i);
            }
            // the sheet route of this run: the routed tools that the boolean gets (one left out as thin is not routed)
            if (autoSheet != null) {
                bool any = false;
                foreach (int i in active) {
                    if (autoSheet[i] == false)
                        continue;
                    any = true;
                    step("SheetRole", i, "input " + i + " is not closed and a tool of the Difference, and InputRoles states no role for it",
                        "input " + i + " is handed to the boolean as a sheet (an oriented cutting surface): not repaired, the first input is cut along it");
                }
                if (any == false)
                    autoSheet = null;
            }
            clock.Restart();
            ExactMeshBoolean op = runBoolean(used, active);
            Report.BooleanMilliseconds = clock.ElapsedMilliseconds;
            BooleanStatus = op.Status;
            // (the sheet role refused with an open tool that was made a sheet here: Compute runs again without the route)
            if (autoSheet != null && op.Status == ExactMeshBoolean.BooleanStatus.SheetRefused) {
                autoRefusal = op.SheetReason;
                autoRefusalText = op.Error;
                // (the witness is the refused sheet's index among the inputs the boolean got: active maps it to the pipeline's index)
                autoRefusalRouted = op.SheetWitnessMesh >= 0 && op.SheetWitnessMesh < active.Count && autoSheet[active[op.SheetWitnessMesh]];
                return fail(PipelineStatus.BooleanFailed, op.Status + ": " + op.Error, total);
            }
            if (anyOpen && op.Status == ExactMeshBoolean.BooleanStatus.OpenInput) {
                if (InputRepair == InputRepairModes.Refuse) {
                    step("Refused", -1, "an input is not closed and InputRepair is Refuse", "no result (Status OpenInput)");
                    var open = new List<string>();
                    // (the boolean's own verdict, by this pipeline's indices: InputClosed also counts an input with flipped
                    // triangles as open, which the boolean accepts with Orientation = AreaMajority)
                    for (int k = 0; k < active.Count; ++k)
                        if (op.ExactWinding != null && k < op.ExactWinding.Length && !op.ExactWinding[k] && !isSheet(active[k]))
                            open.Add(active[k].ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (open.Count == 0)
                        return fail(PipelineStatus.OpenInput, "an input is not closed (InputRepair is Refuse): " + op.Error, total);
                    return fail(PipelineStatus.OpenInput, (open.Count == 1 ? "input " : "inputs ") + string.Join(", ", open) + (open.Count == 1 ? " is" : " are") +
                        " not closed (InputRepair is Refuse). State what the input is: an open cutting surface used as the tool of a Difference gets the role Sheet (InputRoles); " +
                        "a solid with holes can be repaired (InputRepair = AutoRepair or AutoRepairThenRobust; a repair of a cutting surface cuts nothing)", total);
                }
                // (the boolean refuses an input that IsWindingClosed accepts: nothing to repair)
                if (!anyOpen)
                    return fail(PipelineStatus.OpenInput, op.Error, total);
                used = new List<DMesh3>(used);
                clock.Restart();
                for (int k = 0; k < active.Count; ++k) {
                    int i = active[k];
                    if (Report.InputClosed[i] || isSheet(i))
                        continue;
                    DMesh3 repaired;
                    SolidBooleanInputRepair r = repair(i, given[i], out repaired);
                    if (r == null && Cancellation.IsCancellationRequested)
                        return fail(PipelineStatus.Cancelled, "cancelled", total);
                    if (r == null) {
                        step("Refused", i, "input " + i + " is not closed and no allowed repair closed it (InputRepair = " + InputRepair + ")" + refusalSuffix(), "no result (Status OpenInput)");
                        return fail(PipelineStatus.OpenInput, "input " + i + " is not closed and no allowed repair closed it" + refusalSuffix(), total);
                    }
                    Report.RepairedInputs.Add(r);
                    step("InputRepair", i, "input " + i + " is not closed and the boolean refuses it as open (InputRepair = " + InputRepair + ")",
                        "closed by " + r.Method + " on a copy, " + r.TrianglesBefore + " -> " + r.TrianglesAfter + " triangles");
                    // (a MeshAutoRepair copy that self-intersects where the input did not: the repair crosses the input, eg the fill of the
                    // boundary of an open sheet; such a copy is not the solid the input was meant to bound, see InputRepairModes.AutoRepair)
                    if (r.Method == "MeshAutoRepair") {
                        int added = repairSelfIntersections(given[i], repaired);
                        Report.RepairMilliseconds = clock.ElapsedMilliseconds;
                        if (added < 0)
                            return fail(PipelineStatus.Cancelled, "cancelled", total);
                        if (added > 0) {
                            step("Refused", i, "MeshAutoRepair closed input " + i + " into a self-intersecting mesh (" + added + " pairs; the input as given has none)", "no result (Status OpenInput)");
                            return fail(PipelineStatus.OpenInput, "input " + i + " is not closed, and MeshAutoRepair closed it into a self-intersecting mesh (" + added +
                                " pairs of its triangles meet beyond a common edge or vertex; the input as given has none): the repair (eg its fill of a hole) crosses the input. An open " +
                                "cutting surface is not a solid; close the input before the boolean so that it bounds the intended solid", total);
                        }
                    }
                    used[k] = repaired;
                    if (Cancellation.IsCancellationRequested)
                        return fail(PipelineStatus.Cancelled, "cancelled", total);
                }
                Report.RepairMilliseconds = clock.ElapsedMilliseconds;
                clock.Restart();
                op = runBoolean(used, active);
                BooleanStatus = op.Status;
                Report.BooleanMilliseconds += clock.ElapsedMilliseconds;
            }
            if (op.Status == ExactMeshBoolean.BooleanStatus.Cancelled)
                return fail(PipelineStatus.Cancelled, "cancelled", total);
            if (op.Status == ExactMeshBoolean.BooleanStatus.InconsistentOrientation || op.Status == ExactMeshBoolean.BooleanStatus.SelfIntersectingInconsistent)
                return fail(PipelineStatus.InconsistentOrientation, op.Error, total);
            if (op.Status == ExactMeshBoolean.BooleanStatus.InvalidInput) {
                return fail(PipelineStatus.InvalidInput, op.Error, total);
            }
            // a strict refusal whose only cause is components thinner than the bound: the rounded result is kept (the rounding
            // left no intersection, degenerate triangle or false contact, checked exactly by the repair)
            bool thinOnly = op.Status == ExactMeshBoolean.BooleanStatus.RoundingNotClean && OnlyThinComponents(op.Rounding);
            if (op.Rounding != null) {
                Report.RoundingRemoved = new List<MeshRoundingRepair.RemovedComponentInfo>(op.Rounding.RemovedComponentInfos);
                roundingRan = true;
                Report.RoundingVolumeBefore = op.Rounding.VolumeBefore;
                Report.RoundingVolumeAfter = op.Rounding.VolumeAfter;
            }
            if (op.Status != ExactMeshBoolean.BooleanStatus.Success && !thinOnly) {
                BooleanStatus = op.Status;
                SheetReason = op.SheetReason;
                return fail(op.Status == ExactMeshBoolean.BooleanStatus.OpenInput ? PipelineStatus.OpenInput : PipelineStatus.BooleanFailed, op.Status + ": " + op.Error, total);
            }
            Report.InsideOutCells = op.InsideOutCells;
            Report.InsideOutUndecided = op.InsideOutUndecided;
            if (op.WindingOutsideZeroOneOfInput != null)
                for (int k = 0; k < op.WindingOutsideZeroOneOfInput.Length && k < active.Count; ++k)
                    if (op.WindingOutsideZeroOneOfInput[k])
                        Report.InputsWithWindingOutsideZeroOne.Add(active[k]);
            if (op.InsideOutUndecided > 0 && !AllowInsideOutInputs)
                return fail(PipelineStatus.InsideOutInput, op.InsideOutUndecided + " component(s) of the closed inputs could not be tested for being inside-out (no ray found); set AllowInsideOutInputs to use them as they are", total);
            if (op.InsideOutCells > 0 && !AllowInsideOutInputs)
                return fail(PipelineStatus.InsideOutInput, op.InsideOutCells + " component(s) of the closed inputs are inside-out (the winding number in front of them is negative: an empty or reversed solid, not what the surface encloses); " +
                    InsideOutAdvice(op, active, Report.RepairedInputs), total);
            DMesh3 mesh = thinOnly ? op.Rounding.Mesh : op.Result;
            BooleanStatus = op.Status;
            Report.BooleanTriangles = mesh.TriangleCount;
            // Verification RoundingVolume: the change of the parts that the rounding may move without a relative reading (RoundingVolumeThinChangeOf), of this
            // run's boolean result (a later run, the retry or the fallback, replaces it; its time is counted only if it is the run that is the result)
            roundingExplained = 0;
            roundingThinParts = 0;
            roundingMeasureMs = 0;
            if ((Verification & VerificationModes.RoundingVolume) != 0 && roundingRan && op.Rounding != null) {
                var partsClock = Stopwatch.StartNew();
                roundingExplained = RoundingVolumeThinChangeOf(op.Rounding, mesh, out roundingThinParts);
                roundingMeasureMs = partsClock.ElapsedMilliseconds;
            }
            // the sheet route followed the orientation of the sheets as given: the share of the first input that the Difference removed goes
            // into the steps, so that a sheet oriented the other way than meant shows (the volumes in doubles: a number for the reader)
            if (autoSheet != null) {
                double before, after, area;
                MeasureInput(used[0], out before, out area);
                MeasureInput(mesh, out after, out area);
                if (Math.Abs(before) > 0) {
                    double share = Math.Max(0, Math.Min(1, 1 - Math.Abs(after) / Math.Abs(before)));
                    string text = "; the Difference removed " + (100 * share).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " % of the first input's volume (the part behind the sheet)";
                    for (int k = runStart; k < route.Count; ++k)
                        if (route[k].Name == "SheetRole")
                            route[k].Change += text;
                }
            }
            Report.ContactsBoolean = thinOnly ? countContacts(mesh, null) : op.Contacts;
            Report.NonManifoldEdgesBoolean = op.NonManifoldEdges;
            Report.ThinComponentsKept = thinOnly ? op.Rounding.ScaleDisparity : 0;
            if (MinThickness > 0) {
                Report.RemovedThinSolids = RemoveThinSolids(mesh, MinThickness);
                // (the contacts of what is left: the pair pass of PairContactsMode Auto depends on them; BooleanTriangles stays the boolean's count)
                if (Report.RemovedThinSolids.Count > 0)
                    Report.ContactsBoolean = countContacts(mesh, null);
            }

            // Verification NoInventedSurface: the boolean's result against the inputs as passed (before the reduction, see Verification)
            if ((Verification & VerificationModes.NoInventedSurface) != 0) {
                clock.Restart();
                bool measured = measureInventedSurface(mesh);
                Report.VerificationMilliseconds += clock.ElapsedMilliseconds;
                if (!measured)
                    return fail(PipelineStatus.Cancelled, "cancelled", total);
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
            FinalCheck(result, Report);
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
                var check2 = new SolidBooleanReport();
                FinalCheck(paired, check2, false);      // (contacts and reason decide; the crossings are counted only for a kept result)
                if ((check2.NotCleanReason.Length == 0 || check2.NotCleanReason == Report.NotCleanReason)
                    && (paired.TriangleCount < result.TriangleCount || (paired.TriangleCount == result.TriangleCount && check2.ContactsResult < Report.ContactsResult))) {
                    result = paired;
                    Report.PairPassKept = true;
                    Report.ResultTriangles = paired.TriangleCount;
                    FinalCheck(paired, check2);
                    Report.SetFinalCheck(check2);
                } else
                    Report.SetReductionState(first);
                Report.PairPassMilliseconds = clock.ElapsedMilliseconds;
            }

            // 6. the self-contacts of the result, separated (the final check is made again if a vertex moved)
            if (SeparateSelfContacts > 0 && result.TriangleCount > 0) {
                clock.Restart();
                SeparateSelfContactsChecked(result, SeparateSelfContacts, Report, Cancellation);
                if (Cancellation.IsCancellationRequested)
                    return fail(PipelineStatus.Cancelled, "cancelled", total);
                Report.FinalCheckMilliseconds += clock.ElapsedMilliseconds;
            }

            Result = result;
            Status = PipelineStatus.Success;
            Report.TotalMilliseconds = total.ElapsedMilliseconds;
            return true;
        }

        /// <summary>
        /// The step of SeparateSelfContacts with its guard: report holds the final check of the mesh as it is. The moves are made
        /// (SeparateSelfContactsOf), the final check is made again if a vertex moved, and if the result is worse than before
        /// (WorseThan) all moves are put back and the final check of before is restored (SelfContactMovesRevertedAsWorse)
        /// </summary>
        internal static void SeparateSelfContactsChecked(DMesh3 result, double delta, SolidBooleanReport report, CancellationToken cancel)
        {
            var checkedBefore = new SolidBooleanReport();
            checkedBefore.SetFinalCheck(report);
            var movedFrom = new Dictionary<int, Vector3d>();
            SeparateSelfContactsOf(result, delta, report, cancel, movedFrom);
            if (cancel.IsCancellationRequested || report.SelfContactVerticesMoved == 0)
                return;
            FinalCheck(result, report);
            if (WorseThan(report, checkedBefore)) {
                // (the moves made the result worse: the tolerance test of NotCleanReason finds what the exact tests of the step do not)
                foreach (var kv in movedFrom)
                    result.SetVertex(kv.Key, kv.Value);
                report.SetFinalCheck(checkedBefore);
                report.SelfContactMovesRevertedAsWorse = true;
                report.SelfContactVerticesMoved = 0;
                report.SelfContactMaxMove = 0;
                report.SelfContactsLeft = report.SelfContactsFound;
            }
        }

        // the positions where two or more vertices of one solid lie (a solid: an outer shell with the cavities inside it,
        // ComponentSolids; so a cavity that touches its outer shell or another cavity of it counts): per position the vertices
        // of the solids that occur there more than once
        internal static List<List<int>> SelfContacts(DMesh3 mesh)
        {
            var byPos = new Dictionary<Vector3d, List<int>>();
            foreach (int v in mesh.VertexIndices()) {
                if (mesh.GetVtxEdgeCount(v) == 0)
                    continue;
                Vector3d p = mesh.GetVertex(v);
                var key = new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0);      // (+0.0: -0 and 0 are one position)
                List<int> list;
                if (!byPos.TryGetValue(key, out list))
                    byPos[key] = list = new List<int>();
                list.Add(v);
            }
            var result = new List<List<int>>();
            ComponentSolids cs = null;
            foreach (List<int> vs in byPos.Values) {
                if (vs.Count < 2)
                    continue;
                if (cs == null)
                    cs = ComponentSolids.Of(mesh);       // (only a mesh with a position of two vertices needs the solids)
                var self = new List<int>();
                foreach (int v in vs) {
                    int n = 0;
                    foreach (int w in vs)
                        if (cs.SolidOfVertex(w) == cs.SolidOfVertex(v))
                            n++;
                    if (n > 1)
                        self.Add(v);
                }
                if (self.Count > 0)
                    result.Add(self);
            }
            return result;
        }

        /// <summary>
        /// Moves every vertex of a self-contact (SelfContacts) into its own part of the solid: back into its own fan (the
        /// area-weighted direction to the centroids of its triangles), by delta, at most a quarter of its shortest edge; a vertex
        /// whose move would be at or below 16 units in the last place of its largest coordinate stays. A move is put back if a
        /// triangle of the vertex then crosses another triangle beyond what they share (vertices at one position count as one,
        /// so a touch at another contact is allowed; CrossingTriangles, exact), is degenerate, or turned over. Putting back
        /// repeats until a check finds nothing left to put back: every round puts back at least one move, so this ends, and
        /// the state left is the one the last round checked. The counts go to the report (SelfContacts*)
        /// </summary>
        internal static void SeparateSelfContactsOf(DMesh3 mesh, double delta, SolidBooleanReport report, CancellationToken cancel = default(CancellationToken), Dictionary<int, Vector3d> movedFrom = null)
        {
            List<List<int>> contacts = SelfContacts(mesh);
            report.SelfContactsFound = report.SelfContactsLeft = contacts.Count;
            report.SelfContactVerticesMoved = report.SelfContactVerticesSkipped = 0;
            report.SelfContactMovesUndoneCrossing = report.SelfContactMovesUndoneFlipped = 0;
            report.SelfContactMaxMove = 0;
            if (contacts.Count == 0)
                return;
            var old = new Dictionary<int, Vector3d>();          // moved vertex -> its position before
            var oldNormal = new Dictionary<int, BigInteger[]>();    // triangle at a moved vertex -> its normal before (exact, scaled)
            var target = new List<KeyValuePair<int, Vector3d>>();       // (every move is taken from the mesh before any of them, so the order of the vertices does not matter)
            double largestStep = 0;
            foreach (List<int> vs in contacts)
                foreach (int v in vs) {
                    // the direction: back into the own fan of the vertex, along the area-weighted direction to the centroids of
                    // its triangles (along its own surface, away from the other sheet; a normal would be unreliable where thin
                    // layers fold back on themselves)
                    Vector3d p = mesh.GetVertex(v);
                    double shortest = double.MaxValue;
                    foreach (int w in mesh.VtxVerticesItr(v))
                        shortest = Math.Min(shortest, p.Distance(mesh.GetVertex(w)));
                    // (in units of the shortest edge, so that the sum neither under- nor overflows at tiny or huge coordinates, and is
                    // the same bit for bit under scaling by 2^k)
                    Vector3d n = Vector3d.Zero;
                    if (shortest > 0 && shortest < double.MaxValue)
                        foreach (int t in mesh.VtxTrianglesItr(v)) {
                            Index3i tv = mesh.GetTriangle(t);
                            Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), c = mesh.GetVertex(tv.c);
                            n += (((a + b + c) / 3 - p) / shortest) * (0.5 * ((b - a) / shortest).Cross((c - a) / shortest).Length);
                        }
                    double largest = Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z)));
                    double len = n.Length;
                    double step = Math.Min(delta, 0.25 * shortest);
                    if (!(len > 0) || !(step > 16 * largest * 2.220446049250313e-16)) {
                        report.SelfContactVerticesSkipped++;
                        continue;
                    }
                    foreach (int t in mesh.VtxTrianglesItr(v))
                        if (!oldNormal.ContainsKey(t))
                            oldNormal[t] = ExactNormal(mesh, t);
                    old[v] = p;
                    target.Add(new KeyValuePair<int, Vector3d>(v, p + n * (step / len)));
                    largestStep = Math.Max(largestStep, step);
                }
            foreach (var kv in target)
                mesh.SetVertex(kv.Key, kv.Value);
            if (old.Count > 0) {
                // one tree over a copy of the moved mesh (same ids; a tree over the mesh itself refuses queries once a move is
                // put back): a triangle stays within largestStep of its box there whatever is put back, so the queries grow
                // their boxes by twice that and read the current coordinates from the mesh
                var tree = new DMeshAABBTree3(new DMesh3(mesh, false, MeshComponents.None), true);
                double pad = TreePad(largestStep);
                // (every round puts back at least one move and checks all that remain: quadratic in the moved vertices in the worst case,
                // linear in practice, where one round puts back what there is. Kept so: a state that is not checked is never returned)
                while (old.Count > 0) {
                    if (cancel.IsCancellationRequested) {
                        // (a state that was not checked is not kept: every move still standing is put back)
                        foreach (var kv in old)
                            mesh.SetVertex(kv.Key, kv.Value);
                        old.Clear();
                        break;
                    }
                    var undoX = new HashSet<int>();
                    var undoF = new HashSet<int>();
                    foreach (int t in CrossingTriangles(mesh, old.Keys, tree, pad)) {
                        Index3i tv = mesh.GetTriangle(t);
                        for (int j = 0; j < 3; ++j)
                            if (old.ContainsKey(tv[j]))
                                undoX.Add(tv[j]);
                    }
                    foreach (var kv in oldNormal)
                        if (DotSign(ExactNormal(mesh, kv.Key), kv.Value) <= 0) {
                            Index3i tv = mesh.GetTriangle(kv.Key);
                            for (int j = 0; j < 3; ++j)
                                if (old.ContainsKey(tv[j]) && !undoX.Contains(tv[j]))
                                    undoF.Add(tv[j]);
                        }
                    if (undoX.Count == 0 && undoF.Count == 0)
                        break;
                    report.SelfContactMovesUndoneCrossing += undoX.Count;
                    report.SelfContactMovesUndoneFlipped += undoF.Count;
                    undoX.UnionWith(undoF);
                    foreach (int v in undoX) {
                        mesh.SetVertex(v, old[v]);
                        old.Remove(v);
                    }
                }
            }
            report.SelfContactVerticesMoved = old.Count;
            if (movedFrom != null)
                foreach (var kv in old)
                    movedFrom[kv.Key] = kv.Value;
            foreach (var kv in old)
                report.SelfContactMaxMove = Math.Max(report.SelfContactMaxMove, mesh.GetVertex(kv.Key).Distance(kv.Value));
            report.SelfContactsLeft = SelfContacts(mesh).Count;
        }

        // the coordinate as an integer, times 2^1074 (exact: a double is an integer times 2^-1074)
        static BigInteger Scaled(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e > 0)
                m |= 1L << 52;
            BigInteger r = new BigInteger(m) << (e > 0 ? e - 1 : 0);
            return bits < 0 ? -r : r;
        }

        // the exact normal (the cross product of two edges, times 2^2148) of a triangle: no rounding, no tolerance, no under- or overflow
        internal static BigInteger[] ExactNormal(DMesh3 mesh, int t)
        {
            Index3i tv = mesh.GetTriangle(t);
            Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), c = mesh.GetVertex(tv.c);
            BigInteger e1x = Scaled(b.x) - Scaled(a.x), e1y = Scaled(b.y) - Scaled(a.y), e1z = Scaled(b.z) - Scaled(a.z);
            BigInteger e2x = Scaled(c.x) - Scaled(a.x), e2y = Scaled(c.y) - Scaled(a.y), e2z = Scaled(c.z) - Scaled(a.z);
            return new[] { e1y * e2z - e1z * e2y, e1z * e2x - e1x * e2z, e1x * e2y - e1y * e2x };
        }

        internal static int DotSign(BigInteger[] u, BigInteger[] v)
        {
            return (u[0] * v[0] + u[1] * v[1] + u[2] * v[2]).Sign;
        }

        // is the check of a result worse than that of before: a lower kind (clean before contacts only before another defect), or in the
        // same kind another reason (a new class of defect), more positions with contacts, or more triangles that cross another
        // (-1: not counted, more than the cap)
        internal static bool WorseThan(SolidBooleanReport after, SolidBooleanReport before)
        {
            int ra = after.Clean ? 2 : after.OnlyContacts ? 1 : 0, rb = before.Clean ? 2 : before.OnlyContacts ? 1 : 0;
            if (ra != rb)
                return ra < rb;
            if (after.NotCleanReason != before.NotCleanReason || after.ContactsResult > before.ContactsResult)
                return true;
            int ca = after.SelfIntersectingTriangles < 0 ? int.MaxValue : after.SelfIntersectingTriangles;
            int cb = before.SelfIntersectingTriangles < 0 ? int.MaxValue : before.SelfIntersectingTriangles;
            return ca > cb;
        }

        // how far the boxes of the stale tree of SeparateSelfContactsOf are grown: a triangle is within largestStep of its box there
        // whatever is put back, and so is the one that is checked
        internal static double TreePad(double largestStep) { return 2 * largestStep; }

        /// <summary>
        /// The triangles at the given vertices that cross another triangle of the mesh beyond what they share, where vertices
        /// at one position count as one (so a touch at a contact, a position with several vertices, is not a crossing, but a
        /// triangle that passes through another one is; ExactTriangleIntersection.IntersectsBeyondShared, exact), or that are
        /// degenerate, or that share a position with a degenerate triangle (conservative, kept from before #1551, when
        /// IntersectsBeyondShared needed non-degenerate triangles there; it decides them exactly now). tree: a tree over the mesh whose triangles have moved at most pad / 2 since it was
        /// built (null: one is built). Not used by the pipeline: the reference of CrossingTrianglesOfAll (its tests compare the two)
        /// </summary>
        internal static HashSet<int> CrossingTriangles(DMesh3 mesh, IEnumerable<int> vertices, DMeshAABBTree3 tree = null, double pad = 0)
        {
            var canon = new Dictionary<Vector3d, int>();
            Func<int, int> id = (v) => {
                Vector3d p = mesh.GetVertex(v);
                var key = new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0);
                int c;
                if (!canon.TryGetValue(key, out c))
                    canon[key] = c = v;
                return c;
            };
            if (tree == null)
                tree = new DMeshAABBTree3(mesh, true);
            var mine = new HashSet<int>();
            foreach (int v in vertices)
                foreach (int t in mesh.VtxTrianglesItr(v))
                    mine.Add(t);
            var crossing = new HashSet<int>();
            foreach (int t in mine) {
                var A = new Triangle3d();
                mesh.GetTriVertices(t, ref A.V0, ref A.V1, ref A.V2);
                if (ExactTriangleIntersection.IsDegenerate(ref A)) {
                    crossing.Add(t);
                    continue;
                }
                Index3i ta = mesh.GetTriangle(t);
                var ia = new Index3i(id(ta.a), id(ta.b), id(ta.c));
                var box = new AxisAlignedBox3d(A.V0);
                box.Contain(A.V1); box.Contain(A.V2);
                box.Expand(pad);
                bool hit = false;
                var trav = new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (b, depth) => !hit && b.Intersects(box),
                    NextTriangleF = (u) => {
                        if (hit || u == t)
                            return;
                        var B = new Triangle3d();
                        mesh.GetTriVertices(u, ref B.V0, ref B.V1, ref B.V2);
                        Index3i tb = mesh.GetTriangle(u);
                        var ib = new Index3i(id(tb.a), id(tb.b), id(tb.c));
                        bool shares = ia.a == ib.a || ia.a == ib.b || ia.a == ib.c || ia.b == ib.a || ia.b == ib.b || ia.b == ib.c || ia.c == ib.a || ia.c == ib.b || ia.c == ib.c;
                        if (shares && ExactTriangleIntersection.IsDegenerate(ref B))
                            hit = true;     // (conservative, kept from before #1551: IntersectsBeyondShared decides degenerate neighbours exactly now)
                        else if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B))
                            hit = true;
                    }
                };
                tree.DoTraversal(trav);
                if (hit)
                    crossing.Add(t);
            }
            return crossing;
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
                && r.RemainingFalseContacts == 0 && !r.ScaleNotFinite && !r.OutsideRange && r.RemovedResolvable == 0;
        }

        /// <summary>
        /// the bound of DropThinInsideOut, relative to the extent of the component: the default ExactMeshBoolean.RoundingMaxDisplacement
        /// (fixed: a RoundingMaxDisplacement set in ConfigureBoolean does not change it)
        /// </summary>
        internal const double InsideOutThinBound = 1e-5;

        /// <summary>
        /// the extent of a component (#1567): the largest distance from its first vertex (the lowest vertex index among its
        /// triangles) to its other vertices, between half its diameter and its diameter. Unlike the box diagonal it does not depend on
        /// the orientation: it scales exactly with 2^k and moves only by rounding under a rotation or a translation
        /// </summary>
        static double ComponentExtent(DMesh3 mesh, int[] ct)
        {
            int first = int.MaxValue;
            foreach (int t in ct) {
                Index3i tv = mesh.GetTriangle(t);
                first = Math.Min(first, Math.Min(tv.a, Math.Min(tv.b, tv.c)));
            }
            Vector3d o = mesh.GetVertex(first);
            double d2 = 0;
            foreach (int t in ct) {
                Index3i tv = mesh.GetTriangle(t);
                d2 = Math.Max(d2, Math.Max((mesh.GetVertex(tv.a) - o).LengthSquared, Math.Max((mesh.GetVertex(tv.b) - o).LengthSquared, (mesh.GetVertex(tv.c) - o).LengthSquared)));
            }
            return Math.Sqrt(d2);
        }

        /// <summary>
        /// The inputs (given) with the thin inside-out components of the solid ones dropped (#1555), each listed in dropped. A
        /// candidate is a component (by edges) of a solid input that has no boundary edge, is consistently oriented (the two
        /// triangles of every edge run along it in opposite directions), has an exactly negative volume (ExactPredicates.VolumeSign)
        /// and 2 |Volume| / Area below InsideOutThinBound times the extent of the component itself (the magnitude in doubles,
        /// first; the sign exact; #1567). The rule is the shape of a residue (two nearly coincident sheets, thin by their own
        /// extent) and depends on no other component or input: a plate 10 x 10 x 0.01 (1e-3 of its extent) is not one wherever it lies. It is dropped where the rest of its input (every triangle that is no candidate: the input is winding-closed,
        /// so this is also exact for shells closed only by position, or split into several components) has a winding number of
        /// 0 or less at one of its vertices (exact, ShellIndex.ExactWinding; a vertex on or within the touch distance of the rest
        /// is skipped): it lies outside the solid there. A cavity (every vertex inside the rest) stays, and so does a candidate
        /// no vertex decides; a candidate that crosses the rest with every vertex inside it stays too. An input that would lose all
        /// of its triangles is kept as it is. Returns given itself when nothing is dropped, else a new list with copies of the
        /// changed inputs (the others are the same objects). Volumes are taken relative to a vertex of each component, in a fixed
        /// order: a scale by 2^k scales every value exactly
        /// </summary>
        internal static List<DMesh3> DropThinInsideOut(List<DMesh3> given, bool[] solid, List<SolidBooleanDroppedComponent> dropped)
        {
            int n = given.Count;
            var tris = new int[n][][];
            var vol = new double[n][];
            var area = new double[n][];
            var boxes = new AxisAlignedBox3d[n][];
            var extents = new double[n][];
            for (int i = 0; i < n; ++i) {
                if (!solid[i] || given[i].TriangleCount == 0)
                    continue;
                DMesh3 mesh = given[i];
                var cc = new MeshConnectedComponents(mesh);
                cc.FindConnectedT();
                int m = cc.Count;
                tris[i] = new int[m][];
                vol[i] = new double[m];
                area[i] = new double[m];
                boxes[i] = new AxisAlignedBox3d[m];
                extents[i] = new double[m];
                for (int c = 0; c < m; ++c) {
                    int[] ct = cc.Components[c].Indices;
                    tris[i][c] = ct;
                    Vector3d o = mesh.GetVertex(mesh.GetTriangle(ct[0]).a);
                    double v6 = 0, a2 = 0;
                    var box = AxisAlignedBox3d.Empty;
                    foreach (int t in ct) {
                        Index3i tv = mesh.GetTriangle(t);
                        Vector3d p = mesh.GetVertex(tv.a), q = mesh.GetVertex(tv.b), r = mesh.GetVertex(tv.c);
                        box.Contain(p); box.Contain(q); box.Contain(r);
                        v6 += (p - o).Dot((q - o).Cross(r - o));
                        a2 += (q - p).Cross(r - p).Length;
                    }
                    vol[i][c] = v6 / 6;
                    area[i][c] = a2 / 2;
                    boxes[i][c] = box;
                    extents[i][c] = ComponentExtent(mesh, ct);
                }
            }
            List<DMesh3> result = given;
            for (int i = 0; i < n; ++i) {
                if (tris[i] == null)
                    continue;
                DMesh3 mesh = given[i];
                int m = tris[i].Length;
                var candidates = new List<int>();
                int candidateTriangles = 0;
                for (int c = 0; c < m; ++c) {
                    // (the cheap test first, thin by the doubles; then, for the few that pass, closed and consistent by the edges and the
                    // exact sign of the volume: the sign is decided exactly, the doubles only bound the magnitude)
                    if (!(2 * Math.Abs(vol[i][c]) < InsideOutThinBound * extents[i][c] * area[i][c]))
                        continue;
                    if (!closed_and_consistent(mesh, tris[i][c]) || MeshPostRepairCleanup.volume_sign(mesh, tris[i][c]) >= 0)
                        continue;
                    candidates.Add(c);
                    candidateTriangles += tris[i][c].Length;
                }
                // (never all of an input: then it is used as it is, and the boolean decides as before)
                if (candidates.Count == 0 || candidateTriangles == mesh.TriangleCount)
                    continue;
                // the rest of the input: one index for all candidates of the input
                var isCandidate = new bool[m];
                foreach (int c in candidates)
                    isCandidate[c] = true;
                var restTris = new List<int>(mesh.TriangleCount - candidateTriangles);
                var restBox = AxisAlignedBox3d.Empty;
                for (int c = 0; c < m; ++c) {
                    if (isCandidate[c])
                        continue;
                    restTris.AddRange(tris[i][c]);
                    restBox.Contain(boxes[i][c]);
                }
                var rest = new ShellIndex(mesh, restTris.ToArray(), restBox);
                var drop = new List<int>();
                foreach (int c in candidates)
                    if (rest.AnyVertexOutside(mesh, tris[i][c]))
                        drop.Add(c);
                if (drop.Count == 0)
                    continue;
                var copy = new DMesh3(mesh);
                foreach (int c in drop) {
                    foreach (int t in tris[i][c])
                        copy.RemoveTriangle(t, true, false);
                    dropped.Add(new SolidBooleanDroppedComponent {
                        Input = i, Triangles = tris[i][c].Length, Volume = vol[i][c], Area = area[i][c],
                        Thickness = 2 * Math.Abs(vol[i][c]) / area[i][c], Bound = InsideOutThinBound * extents[i][c], Extent = extents[i][c], Center = boxes[i][c].Center
                    });
                }
                if (result == given)
                    result = new List<DMesh3>(given);
                result[i] = copy;
            }
            return result;
        }

        // every edge of the triangles tris has two triangles that run along it in opposite directions: closed and consistently
        // oriented by the edges. (A guard: the input is winding-closed, so a component that is closed by its edges but not
        // consistent would need another component to balance it at the same positions; no test input reaches it)
        static bool closed_and_consistent(DMesh3 mesh, int[] tris)
        {
            foreach (int t in tris)
                for (int j = 0; j < 3; ++j)
                    if (!consistent_edge(mesh, mesh.GetTriEdge(t, j)))
                        return false;
            return true;
        }

        // an edge with two triangles that run along it in opposite directions (a boundary edge is not)
        static bool consistent_edge(DMesh3 mesh, int eid)
        {
            Index4i ev = mesh.GetEdge(eid);
            if (ev.d == DMesh3.InvalidID)
                return false;
            return runs_along(mesh.GetTriangle(ev.c), ev.a, ev.b) != runs_along(mesh.GetTriangle(ev.d), ev.a, ev.b);
        }

        // does triangle t have the directed edge a -> b
        static bool runs_along(Index3i t, int a, int b)
        {
            return (t.a == a && t.b == b) || (t.b == a && t.c == b) || (t.c == a && t.a == b);
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
            ComponentSolids cs = ComponentSolids.Of(mesh);
            int n = cs.Count;
            double[] vol = cs.Volume, area = cs.Area;
            bool[] open = cs.Open;
            AxisAlignedBox3d[] boxes = cs.Box;
            int[][] tris = cs.Triangles;
            int[] owner = cs.Owner;
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

        /// <summary>
        /// The connected components of a mesh (by edges) with their triangles, volume, area, box, whether they have a boundary
        /// edge, and the solid they belong to: an outer shell (positive volume) is its own solid; a cavity belongs to the smallest
        /// outer shell that contains it, decided exactly (ShellIndex.Contains; Owner -1: in no outer shell). Volumes are taken
        /// relative to a vertex of each component (exact enough far from the origin).
        /// </summary>
        internal sealed class ComponentSolids
        {
            public int Count;
            public int[][] Triangles;
            public double[] Volume, Area;
            public bool[] Open;
            public AxisAlignedBox3d[] Box;
            public int[] Owner;
            /// <summary>
            /// the component of each vertex (-1 for a vertex of no triangle); a vertex shared by two components that are not connected by
            /// an edge (a bowtie) names the last one (such a result is flagged by the final check anyway)
            /// </summary>
            public int[] ComponentOfVertex;

            public static ComponentSolids Of(DMesh3 mesh)
            {
                var cc = new MeshConnectedComponents(mesh);
                cc.FindConnectedT();
                int n = cc.Count;
                var r = new ComponentSolids() {
                    Count = n, Triangles = new int[n][], Volume = new double[n], Area = new double[n], Open = new bool[n],
                    Box = new AxisAlignedBox3d[n], Owner = new int[n], ComponentOfVertex = new int[mesh.MaxVertexID] };
                for (int v = 0; v < r.ComponentOfVertex.Length; ++v)
                    r.ComponentOfVertex[v] = -1;
                for (int c = 0; c < n; ++c) {
                    int[] tris = r.Triangles[c] = cc.Components[c].Indices;
                    // (the volume relative to a vertex of the component: far from the origin the terms of absolute coordinates
                    // cancel, and the sign of a thin shell could be noise)
                    Vector3d o = mesh.GetVertex(mesh.GetTriangle(tris[0]).a);
                    double v6 = 0, a2 = 0;
                    r.Box[c] = AxisAlignedBox3d.Empty;
                    foreach (int t in tris) {
                        Index3i tv = mesh.GetTriangle(t);
                        r.ComponentOfVertex[tv.a] = r.ComponentOfVertex[tv.b] = r.ComponentOfVertex[tv.c] = c;
                        Vector3d p = mesh.GetVertex(tv.a), q = mesh.GetVertex(tv.b), w = mesh.GetVertex(tv.c);
                        r.Box[c].Contain(p); r.Box[c].Contain(q); r.Box[c].Contain(w);
                        Vector3d pa = p - o, qa = q - o, wa = w - o;
                        v6 += pa.Dot(qa.Cross(wa));
                        a2 += (q - p).Cross(w - p).Length;
                        for (int j = 0; j < 3 && !r.Open[c]; ++j)
                            r.Open[c] = mesh.IsBoundaryEdge(mesh.GetTriEdge(t, j));
                    }
                    r.Volume[c] = v6 / 6;
                    r.Area[c] = a2 / 2;
                }
                // the solid of each component: an outer shell is its own; a cavity goes to the smallest outer shell that contains
                // it, decided exactly (ShellIndex.Contains). A cavity and another shell share at most points where they touch, so the
                // cavity is inside or outside the shell as a whole: one point of the cavity that is not on the shell decides (the
                // probes are the cavity's vertices, exact points of the cavity)
                var shellIndex = new Dictionary<int, ShellIndex>();
                for (int c = 0; c < n; ++c) {
                    r.Owner[c] = r.Volume[c] > 0 ? c : -1;
                    if (r.Volume[c] > 0)
                        continue;
                    for (int o = 0; o < n; ++o) {
                        if (!(r.Volume[o] > 0) || (r.Owner[c] >= 0 && r.Volume[o] >= r.Volume[r.Owner[c]]) || !ShellIndex.BoxHolds(r.Box[o], r.Box[c]))
                            continue;
                        ShellIndex index;
                        if (!shellIndex.TryGetValue(o, out index))
                            shellIndex[o] = index = new ShellIndex(mesh, r.Triangles[o], r.Box[o]);
                        if (index.Contains(mesh, r.Triangles[c]))
                            r.Owner[c] = o;
                    }
                }
                return r;
            }

            /// <summary> the solid of the component of vertex v: its owner, or the component itself if it has none (-1: no triangle) </summary>
            public int SolidOfVertex(int v)
            {
                int c = ComponentOfVertex[v];
                return c < 0 ? -1 : Owner[c] >= 0 ? Owner[c] : c;
            }
        }

        bool isSheet(int i)
        {
            return (autoSheet != null && autoSheet[i]) || statedSheet(i);
        }

        bool statedSheet(int i)
        {
            return InputRoles != null && i < InputRoles.Count && InputRoles[i] == ExactMeshBoolean.InputRole.Sheet;
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

            /// <summary>
            /// the distance below which a point counts as on a shell with this box: 256 units of the rounding of the largest
            /// coordinate M of the box (eps = 2^-52, so 256 eps M). A shell and a cavity that were moved or rotated in double
            /// precision by the same transform (the same input doubles give the same output doubles, so the corners they share stay
            /// shared; only a cavity vertex inside a wall triangle can come apart from it) end up apart by at most about
            /// sqrt(3) (e_wall + e_cavity), where one coordinate of a rotation y = R x + t has an error of at most about
            /// gamma3 sum |R_ij x_j| + u |y|, about 3 eps M, with u = eps / 2: so some 10 eps M, and 256 eps M is about 25 times
            /// that. It is not a bound for every history: a chain whose intermediate coordinates are much larger than M (a
            /// translation by 1e8 and back), single-precision or text-rounded coordinates, or two shells moved by different
            /// transforms can be apart by more. Then the touching vertices are not skipped, and a vertex that is on the wrong side
            /// of its wall by rounding decides: the failure is a wrong owner, not only an unowned cavity. A probe within this
            /// distance of the shell is skipped, never decided, so the tolerance cannot turn an exact decision around, it
            /// can only leave a cavity unowned (every probe within it: a wall thinner than 256 eps M)
            /// </summary>
            internal static double Touch(AxisAlignedBox3d box)
            {
                double magnitude = 0;
                for (int k = 0; k < 3; ++k)
                    magnitude = Math.Max(magnitude, Math.Max(Math.Abs(box.Min[k]), Math.Abs(box.Max[k])));
                return 256 * 2.220446049250313e-16 * magnitude;
            }

            /// <summary>
            /// does the box of a shell hold the box of a cavity, up to Touch: a cavity that touches the shell from inside has a box
            /// that sticks out of the shell's by the rounding of a moved or rotated coordinate
            /// </summary>
            internal static bool BoxHolds(AxisAlignedBox3d shellBox, AxisAlignedBox3d cavityBox)
            {
                shellBox.Expand(Touch(shellBox));
                return shellBox.Contains(cavityBox);
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
                touch = Touch(box);
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
            /// Is the winding number of this shell 0 or less at a vertex of the triangles comp of mesh (in the order of their index;
            /// a vertex outside the box is outside; one on the shell or within the touch distance of it is skipped)? False if every
            /// vertex that decides is inside, or none decides (DropThinInsideOut)
            /// </summary>
            internal bool AnyVertexOutside(DMesh3 mesh, int[] comp)
            {
                var set = new HashSet<int>();
                foreach (int t in comp) {
                    Index3i tv = mesh.GetTriangle(t);
                    set.Add(tv.a); set.Add(tv.b); set.Add(tv.c);
                }
                int[] vertices = new int[set.Count];
                set.CopyTo(vertices);
                Array.Sort(vertices);
                int w;
                foreach (int v in vertices) {
                    Vector3d p = mesh.GetVertex(v);
                    if (!touches(p) && ExactWinding(p, out w) && w <= 0)
                        return true;
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
                // (at least the touch distance: a box much smaller than the rounding of its coordinates, far from the origin, would
                // otherwise round the end of the segment back into it)
                double margin = Math.Max(1e-3 * box.DiagonalLength, touch) + 1e-300;
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

        // The advice of the InsideOutInput refusal, per input with inside-out components (active[k]: the pipeline's index of the
        // boolean's input k). Orientation = ByNesting derives the enclosed solid only for an embedded input: one whose own
        // arrangement has self-intersecting pairs keeps its given orientation (ExactMeshBoolean.OrientationModes.ByNesting), so for
        // it the advice names the self-intersections, and a repaired copy as what the repair produced. ByNesting is recommended
        // where it was not tried, with the self-intersections as its condition when the boolean found some
        internal static string InsideOutAdvice(ExactMeshBoolean op, List<int> active, List<SolidBooleanInputRepair> repaired)
        {
            int[] cells = op.InsideOutCellsOfInput, pairs = op.NestingSelfIntersectingPairsOfInput;
            var parts = new List<string>();
            bool nestingCanHelp = false;
            for (int k = 0; cells != null && k < cells.Length; ++k) {
                if (cells[k] == 0)
                    continue;
                int i = k < active.Count ? active[k] : k;
                SolidBooleanInputRepair r = repaired.Find(x => x.Index == i);
                if (op.Orientation != ExactMeshBoolean.OrientationModes.ByNesting || pairs == null || pairs[k] <= 0) {
                    nestingCanHelp = true;
                    if (r != null)
                        parts.Add("input " + i + " is the closed copy that the input repair " + r.Method + " produced from the open input");
                    continue;
                }
                parts.Add("input " + i + (r != null ? " (the closed copy that the input repair " + r.Method + " produced from the open input)" : "") +
                    " self-intersects (" + pairs[k] + " pairs of its triangles meet beyond a common edge or vertex), so Orientation = ByNesting cannot orient it " +
                    "(its given orientation stays) and " + cells[k] + " of its components are inside-out at least in part; fix the self-intersections" +
                    (r != null ? " (close the input before the boolean)" : ""));
            }
            if (nestingCanHelp || parts.Count == 0) {
                string nesting = op.Orientation == ExactMeshBoolean.OrientationModes.ByNesting ? "orient them" :
                    "orient them (Orientation = ByNesting derives the enclosed solid" +
                    (op.SelfIntersectingPairs > 0 ? " of an input without self-intersections; the inputs have " + op.SelfIntersectingPairs + " self-intersecting pairs" : "") + ")";
                parts.Add(nesting);
            }
            return string.Join("; ", parts) + " or set AllowInsideOutInputs";
        }

        ExactMeshBoolean runBoolean(List<DMesh3> meshes, List<int> active)
        {
            var op = new ExactMeshBoolean(meshes, Operation);
            op.Orientation = Orientation;
            op.CountInsideOutCells = true;      // (the InsideOutInput refusal and Report.InsideOutCells)
            if (ConfigureBoolean != null)
                ConfigureBoolean(op);
            // (the roles of the inputs the boolean gets, in its order)
            if (InputRoles != null || autoSheet != null) {
                var roles = new List<ExactMeshBoolean.InputRole>(active.Count);
                foreach (int i in active)
                    roles.Add(isSheet(i) ? ExactMeshBoolean.InputRole.Sheet : InputRoles != null && i < InputRoles.Count ? InputRoles[i] : ExactMeshBoolean.InputRole.Solid);
                op.Roles = roles;
                op.SheetUncutSide = UncutSide;
            }
            op.AllowOpenMeshes = false;
            op.StrictRounding = true;
            op.Cancellation = Cancellation;
            // (the rounding repair that this boolean creates records the components it found, for RoundingVolume)
            MeshRoundingRepair.RecordComponentsOfNewRepairs = (Verification & VerificationModes.RoundingVolume) != 0;
            try {
                op.Compute();
            } finally {
                MeshRoundingRepair.RecordComponentsOfNewRepairs = false;
            }
            roundingBound = op.Rounding != null ? op.Rounding.MaxLength : 0;
            return op;
        }

        // ---- Verification ----

        // the run's boolean ran a rounding repair (Report.RoundingVolumeBefore and After are its volumes)
        bool roundingRan;
        // RoundingVolume: the signed change of the volume of the thin parts of the run's boolean result (RoundingVolumeThinChangeOf), their number, and the
        // time that measuring them took
        double roundingExplained;
        int roundingThinParts;
        long roundingMeasureMs;
        // the bound that the rounding repair of the run's last boolean applied (MeshRoundingRepair.MaxLength: the largest over the components of the mesh before
        // the repair), 0 if it did not run
        double roundingBound;
        // the samples that NoInventedSurface measured in the run and how many of them needed the exact search (see NearestInputDistance)
        internal long VerificationSamples, VerificationFallbacks;

        // The tolerance of NoInventedSurface: what the pipeline itself may legitimately move a point of the result away from the inputs as passed.
        //  - the rounding repair moves a point by at most the bound it applied (MaxLength, read from the repair: it is MaxDisplacement times the diagonal of the
        //    largest component of the mesh BEFORE the repair, the same for all components; the result's box after MinThickness or after the repair removed
        //    that component can be smaller, so the result's own diagonal is not that bound). The pipeline's boolean is strict (no ulp term of its own)
        //  - rounding the boolean's exact points to doubles moves a point by ulps of the coordinates: 256 ulp (the repair's own ulp term) of the largest
        //    coordinate. The distance of a sample to a reference triangle is computed at the magnitude of the reference's coordinates, so that is the larger
        //    of the result's and the inputs' largest used coordinate
        //  - a snap to a grid (InputGrid, or the cell of the Grid step) moves an input vertex by at most half a cell per coordinate, the result lies on the
        //    snapped surfaces and a triangle of one is within the largest vertex move of the original: a half diagonal of the cell. The diagonal (twice
        //    that) is taken
        //  - the reduction is not included: it runs after the check, on the result that was measured (MaxDeviation guards it); MinThickness removes
        //    whole solids and moves nothing; the input repair is the thing to detect, not a move to allow; a reorientation flips triangles only
        // The sum is multiplied by InventedSurfaceSafety. Every term scales with the inputs by a power of two exactly (the bound by its definition, the ulp
        // by its exponent, the cell is a power of two), so the tolerance and the share do not change under scaling by 2^k
        internal static double InventedSurfaceToleranceOf(double roundingBound, double largestCoordinate, double cell)
        {
            double rounding = Math.Max(roundingBound, InventedSurfaceUlps * ulpOf(largestCoordinate));
            double snap = cell > 0 ? cell * Math.Sqrt(3) : 0;
            return InventedSurfaceSafety * (rounding + snap);
        }

        // the largest absolute finite coordinate of the vertices that triangles use, over the meshes
        static double LargestUsedCoordinate(IEnumerable<DMesh3> meshes)
        {
            double max = 0;
            foreach (DMesh3 m in meshes)
                foreach (Vector3d p in UsedVertices(m)) {
                    max = largerFinite(max, p.x);
                    max = largerFinite(max, p.y);
                    max = largerFinite(max, p.z);
                }
            return max;
        }

        // the rounding unit of x: a power of two (2^-52 times the power of two at or below |x|); 0 for 0 and a value that is not finite or too small
        static double ulpOf(double x)
        {
            double a = Math.Abs(x);
            if (!(a > 0) || double.IsInfinity(a))
                return 0;
            long field = (BitConverter.DoubleToInt64Bits(a) >> 52) - 52;
            return field < 1 ? 0 : BitConverter.Int64BitsToDouble(field << 52);
        }

        /// <summary> what InventedSurfaceOf measured </summary>
        internal sealed class InventedSurfaceMeasure
        {
            /// <summary> the area of the invented triangles over the area of the mesh (0 for a mesh without area) </summary>
            public double Share;
            /// <summary> the area and the number of the invented triangles </summary>
            public double Area;
            public int Triangles;
            /// <summary> the largest distance of a sample to the nearest reference </summary>
            public double MaxDistance;
            /// <summary> the samples taken, and those that needed the exact search over all references (NearestInputDistance) </summary>
            public long Samples, Fallbacks;
        }

        // Samples every triangle of the mesh (see InventedSurfaceOf) against the inputs as passed and fills the Report. False: cancelled
        bool measureInventedSurface(DMesh3 mesh)
        {
            if (mesh.TriangleCount == 0)
                return true;
            double largest = Math.Max(LargestUsedCoordinate(new[] { mesh }), LargestUsedCoordinate(Inputs));
            Report.InventedSurfaceTolerance = InventedSurfaceToleranceOf(roundingBound, largest, grid);
            // (the references are the inputs as passed: Inputs is never changed by the run, the repair and the snap work on copies)
            InventedSurfaceMeasure m = InventedSurfaceOf(mesh, Inputs, Report.InventedSurfaceTolerance, Cancellation);
            if (m == null)
                return false;
            Report.InventedSurfaceShare = m.Share;
            Report.InventedSurfaceArea = m.Area;
            Report.InventedTriangles = m.Triangles;
            Report.InventedSurfaceMaxDistance = m.MaxDistance;
            VerificationSamples = m.Samples;
            VerificationFallbacks = m.Fallbacks;
            return true;
        }

        /// <summary>
        /// The share of the area of mesh in triangles that have a sample farther than tolerance from every reference mesh (0 for a mesh without area), the
        /// area and number of those triangles, and the largest distance of a sample to the nearest reference. The samples of a triangle are its three
        /// vertices, its centroid and its three edge midpoints; the distance is that to the nearest point of a reference triangle (DistPoint3Triangle3),
        /// found with one AABB tree per reference (NearestInputDistance). Null: cancelled
        /// </summary>
        internal static InventedSurfaceMeasure InventedSurfaceOf(DMesh3 mesh, IList<DMesh3> references, double tolerance, CancellationToken cancellation)
        {
            var result = new InventedSurfaceMeasure();
            var trees = new List<DMeshAABBTree3>(references.Count);
            foreach (DMesh3 m in references)
                if (m.TriangleCount > 0)
                    trees.Add(new DMeshAABBTree3(m, true));
            double total = 0;
            int count = 0;
            var samples = new Vector3d[7];
            // (the search of a sample prunes with this bound; the decision is made on the exact distance, so the widening only keeps a distance that
            // equals the tolerance in the search)
            double bound = tolerance * (1 + 1e-6);
            foreach (int t in mesh.TriangleIndices()) {
                if ((++count & 1023) == 0 && cancellation.IsCancellationRequested)
                    return null;
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                samples[0] = a; samples[1] = b; samples[2] = c;
                samples[3] = (a + b + c) / 3;
                samples[4] = (a + b) / 2; samples[5] = (b + c) / 2; samples[6] = (c + a) / 2;
                double far = 0;
                foreach (Vector3d p in samples) {
                    bool exact;
                    double nearest = NearestInputDistance(trees, p, bound, tolerance, out exact);
                    result.Samples++;
                    if (exact)
                        result.Fallbacks++;
                    if (nearest > far)
                        far = nearest;
                }
                double area = triangleArea(a, b, c);
                total += area;
                if (far > result.MaxDistance)
                    result.MaxDistance = far;
                if (far > tolerance) {
                    result.Area += area;
                    result.Triangles++;
                }
            }
            result.Share = total > 0 ? result.Area / total : 0;
            return result;
        }

        // The distance of p to the nearest triangle of the trees (|p - closest point|, DistPoint3Triangle3).
        // Fast path: a search bounded by `bound`, which prunes the tree for a point on an input (the usual sample). The tree ranks triangles by the squared
        // form of the distance (MeshQueries.TriDistanceSqr), whose absolute error is about eps E^2 for a triangle of size E: for a triangle much larger than
        // the tolerance that ranking is noise, and the search may return a triangle that is far from p, or miss the near one. So the fast path decides only
        // what it can prove: a triangle that it returns and whose EXACT distance is at most `tolerance` shows that p is within the tolerance of an input
        // (that distance is returned, an upper bound of the nearest; the exact decision is not changed by it).
        // Exact path (exact = true): every other sample (nothing found, or a triangle found that is farther than the tolerance by its exact distance) is
        // decided by the search that ranks by the exact closest point (DMeshAABBTree3.FindNearestTriangleByClosestPoint) in every tree: only far samples
        // pay for it
        static double NearestInputDistance(List<DMeshAABBTree3> trees, Vector3d p, double bound, double tolerance, out bool exact)
        {
            double nearest = double.MaxValue;
            foreach (DMeshAABBTree3 tree in trees) {
                double d2;
                int t = tree.FindNearestTriangle(p, out d2, Math.Min(bound, nearest));
                if (t == DMesh3.InvalidID)
                    continue;
                var tri = new Triangle3d();
                tree.Mesh.GetTriVertices(t, ref tri.V0, ref tri.V1, ref tri.V2);
                Vector3d closest, bary;
                DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out closest, out bary);
                double d = p.Distance(closest);
                if (d < nearest)
                    nearest = d;
            }
            exact = false;
            if (nearest <= tolerance)
                return nearest;
            exact = true;
            nearest = double.MaxValue;
            foreach (DMeshAABBTree3 tree in trees) {
                double d;
                tree.FindNearestTriangleByClosestPoint(p, out d);
                if (d < nearest)
                    nearest = d;
            }
            return nearest;
        }

        static double triangleArea(Vector3d a, Vector3d b, Vector3d c)
        {
            return (b - a).Cross(c - a).Length / 2;
        }

        /// <summary>
        /// The verdict of RoundingVolume. change: |after - before| / |before| (0 if dV is 0, +infinity if before is 0 and dV is not); fails when dV is more
        /// than limit times |before| (before 0: any dV) and more than floor; never with an infinite limit. A volume, a dV or a floor that is not finite
        /// (coordinates whose volume overflows) cannot be compared: it fails, also with an infinite limit (like a tolerance that is not finite)
        /// </summary>
        internal static bool RoundingVolumeExceeds(double before, double after, double limit, double floor, out double change)
        {
            return RoundingVolumeExceeds(before, after, 0, limit, floor, out change);
        }

        /// <summary>
        /// The same with explained, the signed change of the thin parts (RoundingVolumeThinChangeOf): change is of the whole result, the verdict is of
        /// dV = |after - before - explained|, the change of the other parts
        /// </summary>
        internal static bool RoundingVolumeExceeds(double before, double after, double explained, double limit, double floor, out double change)
        {
            double all = Math.Abs(after - before);
            change = before != 0 ? all / Math.Abs(before) : (all > 0 ? double.PositiveInfinity : 0);
            double dV = Math.Abs(after - before - explained);
            if (!IsFinite(before) || !IsFinite(after) || !IsFinite(explained) || !IsFinite(dV) || !IsFinite(floor))
                return true;
            if (double.IsPositiveInfinity(limit))
                return false;
            return (before != 0 ? dV > limit * Math.Abs(before) : dV > 0) && dV > floor;
        }

        static bool IsFinite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

        // The checks of Verification on the result of the run that is the result. False: Status VerificationFailed (Result null)
        bool verify(Stopwatch total)
        {
            var clock = Stopwatch.StartNew();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Func<double, string> r = x => x.ToString("R", inv);
            var text = new List<string>();
            var failures = new List<string>();
            if ((Verification & VerificationModes.NoInventedSurface) != 0) {
                double tol = Report.InventedSurfaceTolerance;
                if (Result.TriangleCount == 0 || Report.BooleanTriangles == 0)
                    text.Add("NoInventedSurface: the result has no surface, share 0");
                else {
                    text.Add("NoInventedSurface: invented share " + r(Report.InventedSurfaceShare) + " of the area (" + Report.InventedTriangles + " triangles, area "
                        + r(Report.InventedSurfaceArea) + "; limit " + r(MaxInventedSurfaceShare) + "), largest distance of a sample to an input "
                        + r(Report.InventedSurfaceMaxDistance) + ", tolerance " + r(tol));
                    if (double.IsNaN(tol) || double.IsInfinity(tol))
                        failures.Add("NoInventedSurface: the tolerance is not finite (" + r(tol) + "), so the result cannot be compared with the inputs (non-finite coordinates, or a rounding bound that overflowed)");
                    else if (Report.InventedSurfaceShare > MaxInventedSurfaceShare) {
                        var ran = new List<string>();
                        foreach (SolidBooleanInputRepair x in Report.RepairedInputs)
                            ran.Add("input " + x.Index + " by " + x.Method);
                        string repairs = ran.Count == 0 ? "no input repair ran" : "input repair ran: " + string.Join(", ", ran);
                        failures.Add("NoInventedSurface: " + r(Report.InventedSurfaceShare) + " of the result's area (" + Report.InventedTriangles + " triangles, area " + r(Report.InventedSurfaceArea)
                            + ") lies farther than " + r(tol) + " from every input as passed (the largest distance of a sample is " + r(Report.InventedSurfaceMaxDistance)
                            + "), more than MaxInventedSurfaceShare " + r(MaxInventedSurfaceShare) + ": the result contains surface that is on no input (" + repairs + ")");
                    }
                }
            }
            if ((Verification & VerificationModes.RoundingVolume) != 0) {
                double before = Report.RoundingVolumeBefore, after = Report.RoundingVolumeAfter;
                double dV = Math.Abs(after - before);
                double rest = Math.Abs(after - before - roundingExplained);
                Report.VerificationMilliseconds += roundingMeasureMs;
                if (!roundingRan) {
                    Report.RoundingVolumeChange = 0;
                    text.Add("RoundingVolume: no rounding, change 0");
                } else {
                    double share = RoundingVolumeFloor();
                    double floor = share;
                    bool exceeds = RoundingVolumeExceeds(before, after, roundingExplained, MaxRoundingVolumeChange, floor, out Report.RoundingVolumeChange);
                    string basis = before == 0 ? "Before = 0, relative change " + r(Report.RoundingVolumeChange) : "relative change " + r(Report.RoundingVolumeChange);
                    text.Add("RoundingVolume: the rounding moved " + r(dV) + " of the volume " + r(before) + ", " + basis + " (limit " + r(MaxRoundingVolumeChange) + ", floor " + r(floor) + " of the inputs; " + roundingThinParts + " parts thinner than their rounding length changed by " + r(roundingExplained) + ", the rest by " + r(rest) + ")");
                    if (exceeds && (!IsFinite(before) || !IsFinite(after) || !IsFinite(dV) || !IsFinite(rest) || !IsFinite(roundingExplained) || !IsFinite(floor)))
                        failures.Add("RoundingVolume: the volume is not finite (before " + r(before) + ", after " + r(after) + ", floor " + r(floor) + "), so the change cannot be compared with MaxRoundingVolumeChange (coordinates whose volume overflows)");
                    else if (exceeds)
                        failures.Add("RoundingVolume: the rounding repair moved " + r(dV) + " of the volume " + r(before) + " (" + basis + "; the parts that are not thin moved " + r(rest) + "), more than MaxRoundingVolumeChange "
                            + r(MaxRoundingVolumeChange) + " and the floor " + r(floor));
                }
            }
            step("Verification", -1, "Verification = " + Verification, string.Join("; ", text) + (failures.Count > 0 ? "; refused" : "; passed"));
            Report.VerificationMilliseconds += clock.ElapsedMilliseconds;
            if (failures.Count == 0) {
                Report.TotalMilliseconds = total.ElapsedMilliseconds;
                return true;
            }
            return fail(PipelineStatus.VerificationFailed, string.Join("; ", failures), total);
        }

        /// <summary>
        /// The change of the volume that the rounding may make legitimately, part by part: the signed sum, over the closed components of the mesh as the
        /// rounding repair found it (MeshRoundingRepair.RecordComponents) that are thin before AND after, of (volume after - volume before). A part is thin
        /// when 2 |volume| / area is at most its own rounding length ell = max(MaxDisplacement x the diagonal of the part, InventedSurfaceUlps ulp(its largest
        /// coordinate)): the length that the repair's bound would have if the part were alone, so what is said of a part does not depend on the other parts. A
        /// rounding that moves every vertex by at most ell changes the volume of a component of area A by at most ell A, so such a part may lose or gain all of
        /// its volume legitimately: a slab that the repair removes (volume after 0; thin before) or that it moves. The parts of the mesh after are matched to
        /// the parts before by their vertices (the repair adds no vertex and edits within a component); a part after that names no single part before, or is
        /// open, or a mesh that is not the repair's: nothing is taken out (0), so the answer errs towards refusing. A part that is thicker than its length is
        /// never taken out, whatever the thin ones do. Serial, in the order of the components: the same on every run, every term scales by a power of two
        /// exactly. parts: how many parts were taken out
        /// </summary>
        internal static double RoundingVolumeThinChangeOf(MeshRoundingRepair rounding, DMesh3 mesh, out int parts)
        {
            parts = 0;
            List<MeshRoundingRepair.ComponentRecord> before = rounding.ComponentsBefore;
            int[] map = rounding.ComponentOfVertexBefore;
            if (before == null || map == null || !ReferenceEquals(rounding.Mesh, mesh))
                return 0;
            var afterVolume = new double[before.Count];
            var afterArea = new double[before.Count];
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            foreach (int first in mesh.TriangleIndices()) {
                if (seen.Contains(first))
                    continue;
                // (a component by shared edges; its volume about its first vertex, which keeps a thin one far from the origin well conditioned)
                Vector3d o = mesh.GetVertex(mesh.GetTriangle(first).a);
                double v6 = 0, a2 = 0;
                bool closed = true;
                int found = -1;
                seen.Add(first);
                stack.Push(first);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    Index3i tv = mesh.GetTriangle(t);
                    Vector3d p = mesh.GetVertex(tv.a) - o, q = mesh.GetVertex(tv.b) - o, w = mesh.GetVertex(tv.c) - o;
                    v6 += p.Dot(q.Cross(w));
                    a2 += (q - p).Cross(w - p).Length;
                    for (int k = 0; k < 3; ++k) {
                        int b = tv[k] < map.Length ? map[tv[k]] : -1;
                        if (b == -2)
                            continue;
                        if (b == -1 || (found >= 0 && b != found))
                            return 0;
                        found = b;
                    }
                    Index3i nb = mesh.GetTriNeighbourTris(t);
                    for (int k = 0; k < 3; ++k) {
                        int n = nb[k];
                        if (n == DMesh3.InvalidID)
                            closed = false;
                        else if (seen.Add(n))
                            stack.Push(n);
                    }
                }
                if (!closed || found < 0)
                    return 0;
                afterVolume[found] += v6 / 6;
                afterArea[found] += a2 / 2;
            }
            double explained = 0;
            for (int j = 0; j < before.Count; ++j) {
                MeshRoundingRepair.ComponentRecord b = before[j];
                double ell = Math.Max(rounding.MaxDisplacement * b.Diagonal, InventedSurfaceUlps * ulpOf(b.Largest));
                if (!b.Closed || !(b.Area > 0) || !(ell > 0) || double.IsInfinity(ell))
                    continue;
                bool thinBefore = 2 * Math.Abs(b.Volume) <= ell * b.Area;
                bool thinAfter = afterArea[j] == 0 || 2 * Math.Abs(afterVolume[j]) <= ell * afterArea[j];
                if (thinBefore && thinAfter) {
                    explained += afterVolume[j] - b.Volume;
                    ++parts;
                }
            }
            return explained;
        }

        // the smallest dV of RoundingVolume that can fail (RoundingVolumeFloorOf) for the inputs as passed: whether an input is closed is asked of the input
        // itself, not of the snapped copy that the check of a run with a grid saw
        double RoundingVolumeFloor()
        {
            return RoundingVolumeFloorOf(Inputs, i => ExactMeshBoolean.IsWindingClosed(Inputs[i]));
        }

        /// <summary>
        /// The floor of RoundingVolume: RoundingVolumeFloorShare of the smallest |volume| of the inputs that closed(i) calls closed (a closed input of
        /// volume 0, a sheet given twice, has no volume to take a share of and is skipped); without such an input RoundingVolumeFloorDiagonal of the
        /// cubed diagonal of the box of all inputs. Sizes and volumes are those of the vertices that triangles use (a vertex that no triangle uses is
        /// not part of the input)
        /// </summary>
        internal static double RoundingVolumeFloorOf(IList<DMesh3> inputs, Func<int, bool> closed)
        {
            double smallest = double.MaxValue;
            var all = AxisAlignedBox3d.Empty;
            for (int i = 0; i < inputs.Count; ++i) {
                DMesh3 m = inputs[i];
                if (m.TriangleCount == 0)
                    continue;
                var box = AxisAlignedBox3d.Empty;
                foreach (Vector3d p in UsedVertices(m))
                    box.Contain(p);
                all.Contain(box);
                if (!closed(i))
                    continue;
                // (volume and area relative to the centre of the used vertices' box)
                Vector3d o = box.Center;
                double v6 = 0, a2 = 0;
                foreach (int t in m.TriangleIndices()) {
                    Index3i tv = m.GetTriangle(t);
                    Vector3d p = m.GetVertex(tv.a), q = m.GetVertex(tv.b), s = m.GetVertex(tv.c);
                    v6 += (p - o).Dot((q - o).Cross(s - o));
                    a2 += (q - p).Cross(s - p).Length;
                }
                double v = v6 / 6, a = a2 / 2;
                if (Math.Abs(v) <= 1e-13 * a * box.MaxDim)
                    continue;
                smallest = Math.Min(smallest, Math.Abs(v));
            }
            if (smallest < double.MaxValue)
                return RoundingVolumeFloorShare * smallest;
            double diag = all.Max.x >= all.Min.x ? all.DiagonalLength : 0;
            return RoundingVolumeFloorDiagonal * diag * diag * diag;
        }

        // tests: called with the triangle count of each mesh whose self-intersections the repair check computes (the copy, then the input)
        internal Action<int> RepairCheckObserverForTests;

        // The self-intersecting pairs of a repaired copy (its own arrangement, exact, not triangulated) if the input as given has
        // none, else 0 (the input is used as before: its self-intersections are its own); -1 if cancelled. The input's own
        // arrangement runs only if the copy has some.
        int repairSelfIntersections(DMesh3 input, DMesh3 repaired)
        {
            int copy = selfIntersectingPairs(repaired);
            if (copy <= 0)
                return copy;
            int own = selfIntersectingPairs(input);
            return own < 0 ? -1 : own == 0 ? copy : 0;
        }

        int selfIntersectingPairs(DMesh3 mesh)
        {
            RepairCheckObserverForTests?.Invoke(mesh.TriangleCount);
            var a = new ExactMeshArrangement(new List<DMesh3> { mesh }) { SelfIntersections = true, Cancellation = Cancellation };
            a.TriangulateMeshes = new[] { false };
            a.Compute();
            return a.Stopped == ExactMeshArrangement.StopReason.Cancelled ? -1 : a.SelfIntersectingPairs;
        }

        // the cheapest allowed repair that makes input i closed (on a copy), or null
        SolidBooleanInputRepair repair(int i, DMesh3 input, out DMesh3 repaired)
        {
            repaired = null;
            robustRefusal = null;
            var clock = Stopwatch.StartNew();
            if (InputRepair == InputRepairModes.ThickenOpenSheet) {
                double halfThick = OpenSheetHalfThickness;
                if (halfThick == 0) {
                    AxisAlignedBox3d b = AxisAlignedBox3d.Empty;
                    bool hasTri = false;
                    foreach (int tid in input.TriangleIndices()) {
                        Index3i t = input.GetTriangle(tid);
                        Vector3d va = input.GetVertex(t.a);
                        Vector3d vb = input.GetVertex(t.b);
                        Vector3d vc = input.GetVertex(t.c);
                        if (va == vb || vb == vc || vc == va)
                            continue;
                        b.Contain(va);
                        b.Contain(vb);
                        b.Contain(vc);
                        hasTri = true;
                    }
                    double diag = 0;
                    if (hasTri) {
                        double wx = b.Width, wy = b.Height, wz = b.Depth;
                        double m = Math.Max(wx, Math.Max(wy, wz));
                        if (m > 0 && MathUtil.IsFinite(m)) {
                            double rx = wx / m, ry = wy / m, rz = wz / m;
                            diag = m * Math.Sqrt(rx * rx + ry * ry + rz * rz);
                        }
                    }
                    halfThick = RelativeOpenSheetHalfThickness * diag;
                }
                var miter = new MeshMiteredOffset {
                    Mesh = input,
                    Mode = MeshMiteredOffset.OffsetModes.DoubleSided,
                    Distance = halfThick,
                    MiterLimit = OpenSheetMiterLimit,
                    SafeRounding = true,
                    Cancellation = Cancellation
                };
                if (!miter.Compute() || miter.Result == null || miter.Result.TriangleCount == 0 || !ExactMeshBoolean.IsWindingClosed(miter.Result)) {
                    robustRefusal = "MeshMiteredOffset " + miter.Status + (string.IsNullOrEmpty(miter.Error) ? "" : ": " + miter.Error);
                    return null;
                }
                repaired = miter.Result;
                return new SolidBooleanInputRepair { Index = i, Method = "MeshMiteredOffset", TrianglesBefore = input.TriangleCount, TrianglesAfter = repaired.TriangleCount, Milliseconds = clock.ElapsedMilliseconds };
            }
            var auto = new MeshAutoRepair(new DMesh3(input));
            if (SkipAutoRepairForTests == false)
                auto.Apply();
            if (SkipAutoRepairForTests == false && ExactMeshBoolean.IsWindingClosed(auto.Mesh) && auto.Mesh.TriangleCount > 0) {
                repaired = auto.Mesh;
                return new SolidBooleanInputRepair { Index = i, Method = "MeshAutoRepair", TrianglesBefore = input.TriangleCount, TrianglesAfter = repaired.TriangleCount, Milliseconds = clock.ElapsedMilliseconds };
            }
            if (InputRepair != InputRepairModes.AutoRepairThenRobust)
                return null;
            var robust = new MeshRobustRepair(new DMesh3(input));
            robust.CancelF = () => Cancellation.IsCancellationRequested;
            RobustRepairSetupForTests?.Invoke(robust);
            // Apply() false: the mesh in robust.Mesh is not a repair of the input (a thin shell around a solid, a part lost, ambiguous nesting, a
            // scale it does not handle, cancelled: see MeshRobustRepair.Apply), even where it is closed. It is not used; the run is refused and
            // names the reason
            if (robust.Apply() == false) {
                robustRefusal = robust.ResultReason != "" ? robust.ResultReason : "Apply() returned false";
                return null;
            }
            if (ExactMeshBoolean.IsWindingClosed(robust.Mesh) && robust.Mesh.TriangleCount > 0) {
                repaired = robust.Mesh;
                return new SolidBooleanInputRepair { Index = i, Method = "MeshRobustRepair", TrianglesBefore = input.TriangleCount, TrianglesAfter = repaired.TriangleCount, Milliseconds = clock.ElapsedMilliseconds };
            }
            return null;
        }

        // why MeshRobustRepair or MeshMiteredOffset was refused by the last repair(), else null
        string robustRefusal;
        // tests: skip MeshAutoRepair so that an input reaches MeshRobustRepair (MeshAutoRepair closes nearly every open surface)
        internal bool SkipAutoRepairForTests;
        // tests: settings of the MeshRobustRepair of the repair (a cheaper grid and wrap than the defaults)
        internal Action<MeshRobustRepair> RobustRepairSetupForTests;

        string refusalSuffix()
        {
            if (robustRefusal == null)
                return "";
            if (InputRepair == InputRepairModes.ThickenOpenSheet)
                return ": " + robustRefusal;
            return ": MeshRobustRepair did not repair it (Apply() was false, ResultReason: " + robustRefusal + ")";
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

        /// <summary> the most intersecting triangle pairs (raw ids, contacts included) for which the final check counts the crossing triangles; above it SelfIntersectingTriangles is -1 </summary>
        internal const int FinalCheckPairCap = 20000;

        /// <summary>
        /// the final check of a result into the report: ContactsResult, NotCleanReason, and for "self-intersections" the exact
        /// SelfIntersectingTriangles (CrossingTriangles over all vertices) and OnlyContacts; the three others are reset.
        /// countCrossings false: the two are left at 0 / false without the (more expensive) count of the crossing pairs, which
        /// is the full enumeration of the intersecting pairs (no cap, no cancellation check: its cost grows with their number)
        /// </summary>
        internal static void FinalCheck(DMesh3 mesh, SolidBooleanReport report, bool countCrossings = true, int pairCap = FinalCheckPairCap)
        {
            report.ContactsResult = countContacts(mesh, null);
            report.NotCleanReason = MeshRobustRepair.NotCleanReason(mesh);
            report.SelfIntersectingTriangles = 0;
            report.OnlyContacts = false;
            if (countCrossings && report.NotCleanReason == "self-intersections") {
                if (MeshRepairMetrics.SelfIntersectionsAtMost(mesh, pairCap) > pairCap) {
                    // (more intersecting pairs than the cap: not counted, so that a heavily self-intersecting result does not make the check
                    // expensive; it cannot be told that they are contacts only)
                    report.SelfIntersectingTriangles = -1;
                    return;
                }
                report.SelfIntersectingTriangles = CrossingTrianglesOfAll(mesh).Count;
                report.OnlyContacts = report.ContactsResult > 0 && report.SelfIntersectingTriangles == 0;
            }
        }

        /// <summary>
        /// The same set as CrossingTriangles(mesh, all vertices) for a mesh without degenerate triangles (the final check has
        /// passed those), at a cost proportional to the intersecting pairs instead of to every candidate pair of every triangle:
        /// the intersecting pairs come from MeshRepairMetrics.SelfIntersectionPairs (exact, parallel, raw ids, a touch at
        /// coincident positions included), and only those get the test with canonical ids (IntersectsBeyondShared, vertices at one
        /// position count as one); a pair that intersects only at shared positions is a contact and counts for neither triangle.
        /// A triangle that crosses another beyond what they share intersects it, so every such pair is among the candidates
        /// </summary>
        internal static HashSet<int> CrossingTrianglesOfAll(DMesh3 mesh)
        {
            var crossing = new HashSet<int>();
            List<Index2i> pairs = MeshRepairMetrics.SelfIntersectionPairs(mesh);
            if (pairs.Count == 0)
                return crossing;
            var canon = new Dictionary<Vector3d, int>();
            Func<int, int> id = (v) => {
                Vector3d p = mesh.GetVertex(v);
                var key = new Vector3d(p.x + 0.0, p.y + 0.0, p.z + 0.0);
                int c;
                if (!canon.TryGetValue(key, out c))
                    canon[key] = c = v;
                return c;
            };
            foreach (Index2i pr in pairs) {
                if (crossing.Contains(pr.a) && crossing.Contains(pr.b))
                    continue;
                var A = new Triangle3d();
                var B = new Triangle3d();
                mesh.GetTriVertices(pr.a, ref A.V0, ref A.V1, ref A.V2);
                mesh.GetTriVertices(pr.b, ref B.V0, ref B.V1, ref B.V2);
                Index3i ta = mesh.GetTriangle(pr.a), tb = mesh.GetTriangle(pr.b);
                var ia = new Index3i(id(ta.a), id(ta.b), id(ta.c));
                var ib = new Index3i(id(tb.a), id(tb.b), id(tb.c));
                if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B)) {
                    crossing.Add(pr.a);
                    crossing.Add(pr.b);
                }
            }
            return crossing;
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
