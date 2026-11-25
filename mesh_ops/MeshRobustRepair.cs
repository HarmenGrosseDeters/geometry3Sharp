// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Repair that ends with a clean solid wherever possible: MeshAutoRepair (with the post-repair
    /// cleanup) first, which keeps the input triangles; if its result is not clean, its self-intersections
    /// are resolved exactly (ExactResolve: ExactMeshBoolean.Resolve, which keeps the geometry); if that is not
    /// clean either, the repaired mesh is solidified (winding number, adaptive dual contouring), which gives a
    /// closed, manifold surface without self-intersections at the cost of remeshing it (deviation about a
    /// cell, see SolidifyResolution). If the solidify fails or its result is not clean either (rare:
    /// self-intersections where thin sheets meet in few cells), the mesh is alpha wrapped (MeshAlphaWrap,
    /// AlphaWrapFallback), whose result is always closed, manifold and free of self-intersections, but wraps
    /// over cavities and gaps narrower than about 2 alpha and lies about Offset outside the input (simplified,
    /// SimplifyAlphaWrap: within half an Offset of that).
    ///
    /// Clean means: closed, no bowties, no degenerate triangles, consistently oriented, no inside-out components and, if
    /// CheckSelfIntersections, no self-intersections (exact, MeshRepairMetrics).
    ///
    /// A result can still have lost volume (eg a self-overlapping input made non-overlapping by dropping a
    /// layer, or parts removed by erosion): if its solid agrees with the input's winding number solid less than
    /// MinVolumeIoU, or it lost a whole component of the input (CheckLostComponents: a part too small for the IoU),
    /// the input itself is used instead, resolved exactly, solidified or wrapped (its winding number solid is the
    /// reference; UsedInput). The solidify and the wrap themselves are not checked for volume (they change the geometry by about
    /// a cell or the offset, and lose parts smaller than that); a separable part they lose is reported by the final check below.
    ///
    /// Components far apart compared to their size (eg a part 1e8 away from parts of size 1): the degenerate area of
    /// NotCleanReason, 1e-12 of the squared diagonal of all triangles, then covers their real triangles, and the cleanups
    /// of the exact resolve collapse them. If the exact resolve fails and some component has triangles below that area,
    /// it runs once more with the cleanups relative to the diagonal of the largest component; if that resolve is clean
    /// but for the area of NotCleanReason, Apply() returns false with the resolved mesh in Mesh and ResultReason
    /// ScaleDisparity (the solidify and the wrap, relative to the same diagonal, would lose the parts). With such components
    /// (the input has a triangle below that area) the repair stage's cleanup and tolerances (MeshAutoRepair.CleanupReferenceDiagonal,
    /// the relative tolerances) and the cleanups of the exact resolve are relative to the largest component from the first attempt on,
    /// so a part is not collapsed or eroded by the diagonal of all triangles (before: a part kept 70 to 85 % of its volume with true).
    /// A resolve the first attempts accept is otherwise unchanged, except where a closed part of the input is missing from it (judged relative to
    /// the largest connected component, MeshRepairMetrics.LostComponents): that resolve is rejected and the retry starts from the input.
    /// An Apply() that ends with such a part lost, by any stage, is false with ResultReason LostFarPart (never a silent loss
    /// with true). Only for inputs whose components lie so far apart that a triangle of one is below that area; the relative cleanups and tolerances only for a closed input of at most 64 components (a cracked surface or a soup has pieces, not parts), the lost part checks only with MinVolumeIoU &gt; 0; independent of CheckLostComponents, which judges relative to all triangles and stays off by default.
    /// Last, for any input of two or more separable parts (components whose boxes, padded by 1e-3 of their own diagonals, meet; closed
    /// and open), every part of at least 4 triangles is judged against its own diagonal: one the result does not reach makes Apply() false
    /// with LostFarOpenPart (open), LostFarPart (closed, components far apart as above) or LostSeparatePart (closed, otherwise), also where
    /// the far-part switch above does not fire (MeshRobustRepairLostParts;
    /// only with MinVolumeIoU &gt; 0, as the other checks against the input). Also a speck below 1e-3 of the largest component, which the
    /// repair removes and LostComponents does not judge, is reported then (the owner's rule of 2026-11-10: no silent loss of a part).
    ///
    /// Behaviour: the result is in <see cref="Mesh"/> after Apply(), a new mesh object. The mesh passed to the
    /// constructor is used as the working mesh of MeshAutoRepair and is modified by it (also its unused vertices are
    /// removed, they are no part of the solid), so pass a copy if
    /// you need the input afterwards. Check UsedExactResolve / UsedSolidify / UsedAlphaWrap / FallbackReason (and
    /// ExactResolveReason, SolidifyReason) to see which path was taken.
    /// Cost: the self-intersection check and the volume comparison are cheap compared to the solidify
    /// fallback, whose cost grows with SolidifyResolution. With the alpha wrap fallback (and
    /// CheckSelfIntersections), a solidify whose contour self-intersects is stopped before its cleanup and
    /// reduction (MeshWindingNumberSolidify.StopIfSelfIntersecting) and wrapped right away.
    ///
    /// Use MeshAutoRepair alone if the input triangles must be kept (no remeshing), MeshWindingNumberSolidify
    /// to always remesh, ExactMeshBoolean.Resolve to resolve self-intersections exactly without remeshing.
    ///
    /// Working frame: Apply() scales the mesh by a power of two (exact) to an extent in [1, 2), runs every stage there and scales the result back,
    /// so a scene and its 2^k scaled copy give the same result up to the scale, bit for bit, at every scale of the supported range (design note
    /// docs/design/ROBUST_REPAIR_SCALE_FREE.md, option A). Lengths set by the caller are in its units and are scaled into the frame
    /// (RoundingAbsoluteTolerance, the absolute tolerances and CleanupReferenceDiagonal set by ConfigureRepair, as constants: do not derive them from
    /// repair.Mesh, which is in the frame); the callbacks ConfigureRepair and ConfigureWrapQuality see frame objects. ConfigureRepair is called once on an empty
    /// probe first: with ToleranceMode.Absolute, or if it throws there, the frame is not used. The meshes and lengths of the public Solidify and AlphaWrap (Result, Mesh, UsedCellSize,
    /// UsedAlpha, UsedOffset) are in the caller's units after Apply(); the length diagnostics of ExactResolveRounding and SeparatedRounding
    /// (MaxDrift, MaxLength, ...) stay in the frame. There is no frame with RepairF (a function on the caller's coordinates) or for a mesh that
    /// cannot be scaled exactly (a tiny extent far from the origin). The result is checked once more by NotCleanReason on the mesh in the caller's frame.
    ///
    /// Components far apart compared to their size (the far-part mode): the alpha wrap, whose alpha and offset are fractions of the diagonal of
    /// all triangles, is taken only if alpha + offset is at most 5 % of the largest component's diagonal (WrapTooCoarse otherwise, in
    /// WrapReason): a part below alpha is replaced by a blob of that size, which no check sees. Only with MinVolumeIoU > 0, and for at most 64
    /// connected components (a triangle soup is not a set of parts), whatever WrapRelativeAlpha is (a coarse alpha is refused in that mode).
    ///
    /// Clean parts (PassThroughCleanParts, on by default): a closed component that is clean on its own and whose box meets no other component's box is passed through
    /// untouched, and only the other components go through the stages (design note, option B, stage 0); see PassThroughCleanParts. An input that is clean as a
    /// whole (NotCleanReason empty; any number of parts, their boxes may meet, eg a cavity) is returned as it is (ResultPath PassThrough), unless ConfigureRepair
    /// is set (a configured tolerance asks for a change of a clean mesh).
    ///
    /// Supported input range (docs/QA.md 2.5a): every nonzero coordinate has a magnitude in [2^-200, 2^400], no subnormal coordinates, and the extent of the input is
    /// at least 2^-40 of the largest magnitude. Parts that lie far apart
    /// compared to their size are handled up to the limits of ScaleDisparity (Apply() false beyond, the mesh is kept);
    /// the extent of the whole input is below about 1e154 (ScaleNotFinite). Outside the coordinate range the result is not guaranteed.
    /// </summary>
    public class MeshRobustRepair
    {
        /// <summary> input mesh; after a successful Apply() the repaired (or solidified) result </summary>
        public DMesh3 Mesh;
        /// <summary> cells along the bounding box diagonal for the solidify fallback </summary>
        public int SolidifyResolution = 128;
        /// <summary>
        /// if true, self-intersections make the repair result unclean (triggers the solidify fallback). If false, the
        /// exact stage does not check its results for them either: the sliver cleanup after the resolve can leave some
        /// </summary>
        public bool CheckSelfIntersections = true;
        /// <summary> minimum volume IoU of the repaired mesh with the input's winding number solid (0 = no volume checks, also not CheckLostComponents nor the lost part checks) </summary>
        public double MinVolumeIoU = 0.99;
        /// <summary>
        /// also a result that lost a connected component of the input (MeshRepairMetrics.LostComponents: a part too small
        /// for the volume IoU, eg a screw of an assembly) is treated like one whose volume differs ("parts of the input lost").
        /// Only with MinVolumeIoU &gt; 0 (0 turns off both). Off by default: on the 430 Thingi10K meshes of
        /// benchmarks/MeshRepairBenchmark it found no lost part, but rejected 3 repairs whose nested closed parts were
        /// oriented differently from the reference (a part inside another taken for a cavity: the ambiguity that ComponentsFlippedByNesting and ResultReason NestingAmbiguous report, #1566), and the fallbacks from
        /// the open input were worse (one mesh fewer repaired). For inputs with separate small parts (assemblies). The fallbacks after such a rejection (solidify, wrap) are not
        /// checked for it: their ResultReason can be "" although they lost the same part (unless it is a separable part, which the final
        /// lost-part check of Apply() reports), see FallbackReason and UsedInput.
        /// </summary>
        public bool CheckLostComponents = false;
        /// <summary> configures the first stage (eg tolerances) </summary>
        public Action<MeshAutoRepair> ConfigureRepair = null;
        /// <summary> replaces the first stage (input -> repaired mesh); null = MeshAutoRepair </summary>
        public Func<DMesh3, DMesh3> RepairF = null;
        /// <summary> return true to cancel (checked after the repair stage, during solidify, the alpha wrap and its simplification) </summary>
        public Func<bool> CancelF = () => { return false; };
        /// <summary>
        /// before solidifying: resolve the self-intersections exactly (the solid where the winding number is
        /// positive, ExactMeshBoolean.Resolve); used if the result is clean and its volume agrees with the input.
        /// Only for meshes that are closed in the winding number sense (exact winding numbers).
        /// </summary>
        public bool ExactResolve = true;
        /// <summary>
        /// exact resolve: repair the self-intersections and degenerate slivers that rounding the new vertices to double
        /// leaves, by local edits checked exactly (ExactMeshBoolean.SafeRounding; after SeparateTouchingVertices again
        /// with MeshRoundingRepair). Without it, the cleanup collapses slivers and the resolve is repeated.
        /// </summary>
        public bool SafeRounding = true;
        /// <summary>
        /// the settings of SafeRounding, passed to the exact resolve (ExactMeshBoolean.RoundingMaxDisplacement,
        /// RoundingUlpFactor, RoundingAbsoluteTolerance) and to the repair after SeparateTouchingVertices
        /// (MeshRoundingRepair.MaxDisplacement, UlpFactor, AbsoluteTolerance); see MeshRoundingRepair
        /// </summary>
        public double RoundingMaxDisplacement = 1e-5, RoundingUlpFactor = 256, RoundingAbsoluteTolerance = 0;
        /// <summary>
        /// SafeRounding's effort budget, passed to the exact resolve (ExactMeshBoolean.RoundingMaxPairTests) and to the
        /// repair after SeparateTouchingVertices (MeshRoundingRepair.MaxPairTests). Default 5M; both grow it with the initial
        /// defects (RoundingPairTestsDefectScale, RoundingPairTestsMaxGrowth; by up to 4 times at their defaults)
        /// </summary>
        public long RoundingMaxPairTests = 5000000;
        /// <summary> SafeRounding's growth of the budget with the defects (MeshRoundingRepair.PairTestsDefectScale), passed to the exact resolve and to the repair after SeparateTouchingVertices </summary>
        public double RoundingPairTestsDefectScale = 50000;
        /// <summary> its ceiling, as a factor of RoundingMaxPairTests (MeshRoundingRepair.PairTestsMaxGrowth) </summary>
        public double RoundingPairTestsMaxGrowth = 4;
        /// <summary>
        /// ExactMeshBoolean.StrictRounding for the exact resolve (and the relative bound only for the repair after
        /// SeparateTouchingVertices): a resolve whose rounding is not clean within the relative bound fails
        /// (ExactResolveReason starts with "rounding not clean (StrictRounding)"), and the repair goes on with its other
        /// stages. The repair after the separation does not fail on its own: what it leaves is handled as before (another
        /// round, or the stage's reason). No effect without SafeRounding. Off by default
        /// </summary>
        public bool StrictRounding = false;
        /// <summary>
        /// the repairs of the last exact resolve round: the resolve's SafeRounding (ExactMeshBoolean.Rounding) and the one
        /// after SeparateTouchingVertices; null if not run (diagnostics). Reset by Apply; a later stage that returns before
        /// its rounds keeps those of an earlier one
        /// </summary>
        public MeshRoundingRepair ExactResolveRounding, SeparatedRounding;
        /// <summary> rounding the new vertices to double can create tiny self-intersections: resolve again, up to this often </summary>
        public int ExactResolveRounds = 4;
        /// <summary>
        /// the exact resolve is not tried for meshes with more self-intersecting triangle pairs than this (its time
        /// grows with them; a heavily overlapping mesh is solidified instead); 0 = no limit
        /// </summary>
        public int ExactResolveMaxPairs = 200000;
        /// <summary> last resort: alpha wrap the mesh if the solidify fails or its result is not clean </summary>
        public bool AlphaWrapFallback = true;
        /// <summary> alpha wrap fallback: alpha = bounding box diagonal / WrapRelativeAlpha </summary>
        public double WrapRelativeAlpha = 300;
        /// <summary> alpha wrap fallback: offset = bounding box diagonal / WrapRelativeOffset </summary>
        public double WrapRelativeOffset = 3000;
        /// <summary>
        /// alpha wrap fallback for a small input: below this many triangles the wrap does not go finer than the input's own feature size,
        /// alpha = max(bounding box diagonal / WrapRelativeAlpha, median edge length / SmallInputWrapEdgeDivisor), with the offset in the
        /// same ratio to alpha as without it (WrapRelativeAlpha / WrapRelativeOffset). The wrap size depends on diagonal / alpha only, so
        /// a few triangles with the default alpha took seconds for about 200 000 wrap triangles (#636). The wrap still encloses the
        /// input at the offset it reports (UsedOffset). 0 turns it off; an input of this many triangles or more is wrapped as before, and so is one with far parts (the wrap is judged against the largest component then: WrapTooCoarse).
        /// A coarser wrap than before for such an input: alpha up to a quarter of its edge length, a small feature next to larger ones can be wrapped over.
        /// </summary>
        public int SmallInputWrapTriangles = 64;
        /// <summary> the divisor of the median edge length for the alpha of a small input (see <see cref="SmallInputWrapTriangles"/>) </summary>
        public double SmallInputWrapEdgeDivisor = 4;
        /// <summary>
        /// alpha wrap fallback: the raw-size budget of the wrap (MeshAlphaWrap.MaxEstimatedTriangles; 0 = off, the default): above this
        /// estimated size the wrap's alpha and offset are coarsened before the refinement, and AlphaWrapCoarsening reports the factor.
        /// The wrap's size, and the time of the wrap and its simplification, go with the input's surface area / alpha^2, not with its
        /// triangle count (issue #694: 260 triangles of a flattened sphere wrapped to 211,614 triangles, about 6 s per Apply).
        /// </summary>
        public int WrapMaxEstimatedTriangles = 0;
        /// <summary> alpha wrap fallback: the limit of the coarsening that WrapMaxEstimatedTriangles applies (MeshAlphaWrap.MaxAlphaCoarsening; 0 = no limit, the default) </summary>
        public double WrapMaxAlphaCoarsening = 0;
        /// <summary> threads of the alpha wrap's carving (MeshAlphaWrap.MaxThreads: 0 = all cores, 1 = one thread); the result is the same for every value </summary>
        public int WrapMaxThreads = 0;
        /// <summary>
        /// alpha wrap fallback: simplify the wrap (it has many small triangles, about alpha in size, also on flat
        /// regions). Collapses create no self-intersections (Reducer.PreventSelfIntersections, exact, also if
        /// CheckSelfIntersections is false); the result is kept only if it is clean and certified within half the
        /// wrap offset of the unsimplified wrap (one-sided Hausdorff upper bound), so it stays about half an offset
        /// outside the input (vertices at least about 0.35 offset, measured on 20 Thingi10K meshes); otherwise the unsimplified
        /// wrap is (AlphaWrapSimplified false). The wrap encloses the input strictly, and so does the simplification: a collapse
        /// whose new triangles touch or cross a triangle of the input (exact tests, Reducer.KeepClearOf) is skipped. Before
        /// 2026-11-09 only the distance to the wrap was certified, and 247 of 692,674 triangles (all 20 meshes) cut a convex
        /// crease of the input, by at most 0.07 offset.
        /// </summary>
        public bool SimplifyAlphaWrap = true;
        /// <summary>
        /// alpha wrap fallback, opt-in (0 = off, the default; the result is then bit for bit the one without this option): reduce the
        /// simplified wrap further, until it has at most this many triangles or no collapse is left that keeps the guarantees.
        /// The reduction continues from the simplified wrap with Reducer.MaxDeviation measured from the unsimplified wrap (a
        /// certified bound on every collapse, not only on the result) and the same exact checks as the simplification (no
        /// self-intersection, no contact with the input), so the result is closed, 2-manifold, encloses the input without touching
        /// it, and is certified within WrapTargetMaxDistance offsets of the unsimplified wrap (with the default 0.5 the certificate of
        /// the simplification itself). The target is an upper bound: a wrap that is simplified below it is not refined. Needs
        /// SimplifyAlphaWrap. If the target is out of reach, AlphaWrapTargetLimit says why and AlphaWrapTargetReached is the count
        /// reached. The raw wrap is not coarsened for the target (that fills cavities, see WrapMaxEstimatedTriangles, and
        /// can be combined with it). Takes precedence over WrapTargetFactor. Costs time: one serial Reducer with a certified deviation per
        /// collapse, measured against the unsimplified wrap (benchmarks/MeshRepairBenchmark README, "Alpha wrap triangle target"),
        /// so it grows with the wrap's size: 17 to 29 s more on a 210,000-triangle wrap.
        /// </summary>
        public int WrapTargetTriangles = 0;
        /// <summary>
        /// alpha wrap fallback, opt-in (0 = off, the default): WrapTargetTriangles as a factor of the triangle count of the
        /// input of Apply() (1 = about the input's size), used if WrapTargetTriangles is 0
        /// </summary>
        public double WrapTargetFactor = 0;
        /// <summary>
        /// alpha wrap fallback: the distance, in offsets of the wrap, within which the reduction to the target is certified of the
        /// unsimplified wrap (default 0.5, the simplification's own bound; a smaller value counts as 0.5). A larger one reaches
        /// smaller counts and keeps the result farther from the input: at most (distance + 1) offsets plus the wrap's own
        /// distance to the input at the vertices; the input is never touched
        /// </summary>
        public double WrapTargetMaxDistance = 0.5;
        /// <summary>
        /// alpha wrap fallback: after the simplification, improve the triangle quality (before the simplification's
        /// quality guard of Alpha Wrap 2.0, the simplified wrap kept slivers down to about 1.7 degrees): edge flips and tangential vertex moves around triangles with a minimum
        /// angle below 30 degrees, each kept only if every changed triangle stays within half the wrap offset of the
        /// unsimplified wrap (TriangleDeviationBounds.IsWithin) and, like the simplification, creates no
        /// self-intersection (exact). Every changed triangle is certified within the distance when it is made, the
        /// others keep the simplification's certificate, so the improved mesh is within half the offset by
        /// construction; it is kept only if it is clean, otherwise the simplified one (AlphaWrapQualityImproved false).
        /// The default settings (DefaultWrapQuality) also split caps above 160 degrees (at most 5 % more triangles),
        /// flip caps above 150 degrees, and keep the normals as close to the unsimplified wrap's (0.1 degree slack per
        /// operation). The smallest angle of the mesh does not drop; the largest is not bounded globally (flips and
        /// moves may raise it locally).
        /// Off by default (owner decision 2026-11-08, Alpha Wrap 2.0; on by default from #292 until then): since the
        /// simplification no longer creates caps (its quality guard), the pass adds little (the 1 %-worst angle about
        /// 0.15 degrees, no triangle below 0.5 or above 170 degrees either way) at about 30 % of the pipeline's time
        /// (benchmarks/MeshRepairBenchmark README, "Alpha wrap quality pass"). true runs it as before.
        /// </summary>
        public bool AlphaWrapQualityPass = false;
        /// <summary>
        /// alpha wrap fallback: before the simplification, recover the input's sharp edges on the raw wrap (AlphaWrapFeaturePass:
        /// vertices onto the offset surface's planes, vertices and split points onto its creases near the input's sharp edges,
        /// edges flipped along them). Every operation is checked exactly: no self-intersection, no contact with the input, no
        /// input triangle in the region it sweeps, vertices at least 0.9 offset from the input, angle floors; so the wrap's
        /// guarantees are kept. Off by default (opt-in, Alpha Wrap 3.0 lever 6; the default is decided on the 800 mesh run).
        /// On the 20 T1 meshes of the sharp-feature study: edge F 0.61 -> 0.88, crease normal error 14.7 -> 4.6 degrees,
        /// 0.72x the triangles, the pipeline 3.8 -> 5.1 s (median; the pass itself 0.8 s; benchmarks/SharpFeatureProbe, variant KR).
        /// </summary>
        public bool AlphaWrapSharpFeatures = false;
        /// <summary>
        /// with AlphaWrapSharpFeatures: take the input's creases from its unoriented varifold (a measure on position x plane, no
        /// connectivity) instead of the dihedral angles of its edges. For unwelded, duplicated, flipped or T-junctioned triangle
        /// soups, where an edge has one triangle and the dihedral finds no crease at all (so the pass did nothing). The same
        /// checks and guarantees; the cost is a measure of the input at 1.75 alpha spacing, built once (about 0.2-0.5 s at
        /// 100k-triangle CAD soups, docs/design/VARIFOLD_CREASE_INPUT.md). Off by default.
        /// </summary>
        public bool AlphaWrapSharpFeaturesOnSoups = false;
        /// <summary> alpha wrap fallback: true if AlphaWrapSharpFeatures ran and changed the raw wrap </summary>
        public bool AlphaWrapSharpFeaturesApplied;
        /// <summary> (benchmarks, tests) sets the switches of the sharp-feature pass (AlphaWrapSharpFeatures) </summary>
        internal Action<AlphaWrapFeaturePass> ConfigureWrapFeatures;
        /// <summary> (benchmarks, tests) sets the variant switches of the quality pass (AlphaWrapQualityPass) </summary>
        internal Action<AlphaWrapQualityPass> ConfigureWrapQuality;

        /// <summary>
        /// reject an exact resolve result whose genus differs from the mesh it resolved (TopologyChanged): the
        /// fallbacks (solidify, alpha wrap) run instead. Off by default: a union can change the genus legitimately
        /// (overlapping parts that form a ring), and in Thingi10K 357788 the extra handles are tunnels the exact
        /// union of a crumpled, self-overlapping region really has.
        /// </summary>
        public bool RejectTopologyChange = false;

        // outputs
        /// <summary>
        /// exact stage: true if the genus of its result (GenusAfter) differs from the genus of the mesh it resolved
        /// (GenusBefore, the repair result or the input) while the number of components stays the same: handles a
        /// component gained or lost by resolving its own self-intersections (eg tunnels between self-overlapping
        /// layers, Thingi10K 357788). A union of several parts changes the components, and so the genus, by
        /// construction (on Thingi10K 76 of 175 exact results): not flagged. Only judged when the resolved mesh is
        /// closed and has no bowtie vertices (genus defined); otherwise false and both genera NaN. Reported, not a
        /// defect: the result is clean. Valid together with UsedExactResolve: false and NaN when the result is not
        /// the exact resolve (a resolve rejected by RejectTopologyChange is reported in ExactResolveReason only).
        /// </summary>
        public bool TopologyChanged;
        /// <summary> exact stage: total genus (MeshTopologySummary) of the mesh it resolved and of its result; NaN if not judged </summary>
        public double GenusBefore = double.NaN, GenusAfter = double.NaN;
        /// <summary> true if the result was produced by the solidify fallback (remeshed), false if it is the repair result </summary>
        public bool UsedSolidify;
        /// <summary>
        /// true if the fallbacks (exact resolve, solidify, alpha wrap) started from the input instead of the repair
        /// result, because the repair result's solid differs from the input's (or it is empty). The exact resolve
        /// tries the repair result first even then (its result must agree with the input's volume): false if that
        /// was used
        /// </summary>
        public bool UsedInput;
        /// <summary> why the repair result was not used ("" if it was) </summary>
        public string FallbackReason = "";
        /// <summary>
        /// volume IoU with the input (NaN if the input has no solid or not checked) of the repair result, or of the
        /// exact resolve if that is used; an estimate from as many samples as the check needed
        /// (MeshRepairMetrics.VolumeIoUAtLeast)
        /// </summary>
        public double RepairVolumeIoU = double.NaN;
        /// <summary>
        /// Diagnostic, additive (changes no other output): measure ResultVolumeIoU after a successful Apply(). Off by default;
        /// see ResultVolumeIoU for what it costs.
        /// </summary>
        public bool MeasureResultVolumeIoU = false;
        /// <summary>
        /// Diagnostic (MeasureResultVolumeIoU): the volume IoU of the FINAL result in Mesh against the input as given to Apply(),
        /// whichever stage produced it (pass-through, repair, exact resolve, solidify, alpha wrap): the same reference (the input's
        /// winding number solid, inside-out parts oriented as the metrics do), the same sampling and the same estimate as the volume
        /// checks (an estimate from as many samples as the check needs: it stops early where the interval lies clearly on one side of MinVolumeIoU, so
        /// a value far from it is good to a few hundredths only) (MeshRepairMetrics.VolumeIoUAtLeast at MinVolumeIoU, 0.99 if the checks are off), so it equals RepairVolumeIoU
        /// where the repair result is the final one. It is not a guard: Apply(), ResultReason and every other output are
        /// the same with or without it. NaN where it is not computed (off, Apply() false, the input encloses no volume: an
        /// open sheet has almost no reference solid, and a low value for an open or self-intersecting input may say more about
        /// the reference than about the result, compare it with MeshFidelityMetrics.OuterVolumeIoU, docs/roadmap/alpha-wrap-and-repair.md).
        /// </summary>
        public double ResultVolumeIoU = double.NaN;
        /// <summary>
        /// Report, additive (changes no result, status or other output): measure how much of the INPUT's surface the result does not represent
        /// (LostArea, LostAreaShare, LostTriangleIds, LostAreaMaxDistance). Off by default: then no extra pass, no copy of the input and no
        /// allocation for it. On, it copies the input and costs one nearest-triangle query per sample (a few samples per input triangle; about 40 % of
        /// the repair on a sphere of 99k triangles, 25 to 110 % on the other inputs, benchmarks/LostAreaBenchmark README). It reports and does not judge: an
        /// interior wall of a solid is lost by design, a damaged result reads the same. It is a LOWER bound of the lost area by the blind band of the
        /// tolerance (LostAreaTolerance): input surface closer than that to the result counts as present. Cancelling (CancelF) during the report makes
        /// Apply() false, as a cancel in any stage does; the result stays in Mesh and the report is NaN.
        /// </summary>
        public bool MeasureLostArea = false;
        /// <summary>
        /// MeasureLostArea: the distance in the caller's units beyond which an input sample counts as lost. 0 (default) = derived (UsedLostAreaTolerance),
        /// from what the stage that made the result may move the surface by. Anything that is not a finite number above 0 (negative, NaN, infinity) counts
        /// as 0, as the other settings of this class that make no sense fall back to their defaults. The derived value is a sum of the stage's term and
        /// the query's own error: the stage's term is the largest of (the blind band of the path, in units of the input's size)
        ///   Solidify: 2 grid cells, the coarsest cell the contour used (Solidify.UsedCellSize * RefineFactor; 2 / SolidifyResolution of the diagonal, 1.6 % at the
        ///   default 128; the dual contour's vertices are within a cell diagonal of the surface, and the unrefined cells are the coarse ones);
        ///   AlphaWrap: the larger of 2 and (1 + WrapTargetMaxDistance, at least 1.5) offsets with a target (WrapTargetTriangles), else 2 offsets (the wrap lies at
        ///   the offset from the input, its simplification within half an offset of the wrap, the reduction to a target within WrapTargetMaxDistance offsets; 0.067 %
        ///   of the diagonal at the default offset of 1/3000);
        /// and, for every path, the rounding bound of the exact resolve: the largest of RoundingMaxDisplacement times the diagonal of the largest component
        /// (1e-5), RoundingUlpFactor times the unit in the last place of the largest coordinate and RoundingAbsoluteTolerance (MeshRoundingRepair's bound of an
        /// edit; also the movement of the repair's cleanups where it is not configured otherwise: set LostAreaTolerance for a repair that moves more). The query's
        /// own error is added: 64 units in the last place of the largest coordinate. The distance is that of the sample to the nearest point the query returns
        /// (not the query's own distance, which is wrong by about 5e-9 times the size of a triangle on a point on it); what is left is the sample's rounding (3
        /// operations of at most 1 unit each), the nearest point's (a few units) and the difference; measured on spheres and sheared boxes with samples on the result,
        /// at most 2.5 units far from the origin (offset 1e3 to 1e7) and 14 units near it, so the term is 4.5 times the worst measured. A setting of the rounding
        /// (RoundingMaxDisplacement, RoundingUlpFactor, RoundingAbsoluteTolerance) that is not a finite number of at least 0 counts as 0 there, the other terms stand
        /// (the repair passes such a setting on: MeshRoundingRepair rejects every edit for a bound that is not finite); with nothing valid to derive it from the
        /// report is not measured (NaN), never 0 lost.
        /// All terms are exact powers of two times the input's lengths, so the tolerance scales exactly with a 2^k scaled input
        /// </summary>
        public double LostAreaTolerance = 0;
        /// <summary> MeasureLostArea: the tolerance the last Apply() used, in the caller's units (NaN if not measured) </summary>
        public double UsedLostAreaTolerance = double.NaN;
        /// <summary>
        /// MeasureLostArea: the estimated area of the input (as given to Apply()) that lies farther than UsedLostAreaTolerance from the result in Mesh, in the
        /// caller's units squared; both sides of a sheet count as the input has them (a triangle counts once, with its area). Sampled: every input triangle at
        /// the centroids of 1, 4, 9 or 16 equal sub-triangles (fewer for larger inputs, about 500000 samples in all; a triangle of more than the mean
        /// area gets more, up to 1024, in proportion to its area), so an area is resolved to its sub-triangles. A lower bound by the blind band of the
        /// tolerance (see LostAreaTolerance): a removed wall, fin or contact face within it of a kept surface reads 0. NaN if not measured (off, Apply()
        /// without a result, empty input).
        /// </summary>
        public double LostArea = double.NaN;
        /// <summary> MeasureLostArea: LostArea divided by the area of the input (NaN if not measured or the input has no area); 0 for a result that covers the whole input </summary>
        public double LostAreaShare = double.NaN;
        /// <summary>
        /// MeasureLostArea: the triangle ids (of the input as given to Apply()) with area and more than half of their samples lost, in increasing order;
        /// empty if not measured. To locate the loss: the input is the caller's, so it can be kept by copying it before Apply() (the mesh is modified in place)
        /// </summary>
        public List<int> LostTriangleIds = new List<int>();
        /// <summary>
        /// MeasureLostArea: the largest distance of any sample of the input to the result (also if it is within the tolerance), in the caller's units;
        /// NaN if not measured, PositiveInfinity for an empty result
        /// </summary>
        public double LostAreaMaxDistance = double.NaN;
        /// <summary> the stage that produced the result in Mesh (ResultPath) </summary>
        public enum ResultPaths
        {
            /// <summary> no stage produced a result: Apply() was not run, or stopped before the stages (empty input, ScaleNotFinite, OutsideRange), or failed without a result. A false Apply() that leaves a result in Mesh (a lost part, ScaleDisparity) keeps the stage that made it </summary>
            None,
            /// <summary> every part of the input is clean and passed through untouched (PassThroughCleanParts), nothing was repaired </summary>
            PassThrough,
            /// <summary> the repair result (MeshAutoRepair, or RepairF); with PassedThroughTriangles &gt; 0 for the parts that were not passed through </summary>
            Repair,
            /// <summary> the exact resolve (UsedExactResolve) </summary>
            ExactResolve,
            /// <summary> the solidify fallback (UsedSolidify) </summary>
            Solidify,
            /// <summary> the alpha wrap fallback (UsedAlphaWrap) </summary>
            AlphaWrap
        }
        /// <summary>
        /// the stage that produced the result of the last Apply() (with passed through parts: the stage that produced the rest; they
        /// are in the result untouched, PassedThroughTriangles). Derived from UsedAlphaWrap, UsedSolidify and UsedExactResolve
        /// </summary>
        public ResultPaths ResultPath { get; private set; } = ResultPaths.None;
        /// <summary> the solidify fallback, if it ran (for its statistics); null otherwise </summary>
        public MeshWindingNumberSolidify Solidify;
        /// <summary>
        /// "" if the result in Mesh is clean (see class comment), otherwise why not (NotCleanReason): Apply() also returns
        /// true with an unclean solidify result when the alpha wrap is off or failed. Set by every successful Apply()
        /// from the checks it ran anyway (the repair, exact resolve and wrap results are clean by the checks or by
        /// construction).
        /// </summary>
        public string ResultReason = "";
        /// <summary> why the solidify result was not used ("" if it was or did not run) </summary>
        public string SolidifyReason = "";
        /// <summary> true if the result is the alpha wrap (last resort) </summary>
        public bool UsedAlphaWrap;
        /// <summary>
        /// why the alpha wrap result was not used ("" if it was or did not run): its Status and Error, eg a result that
        /// misses the guarantees (then the solidify result is used, or Apply() returns false)
        /// </summary>
        public string WrapReason = "";
        /// <summary> the alpha wrap fallback, if it ran; null otherwise </summary>
        public MeshAlphaWrap AlphaWrap;
        /// <summary> alpha wrap fallback: triangles of the wrap before simplification (0 if not run) </summary>
        public int AlphaWrapTriangles;
        /// <summary>
        /// alpha wrap fallback: the factor by which WrapMaxEstimatedTriangles coarsened the wrap's alpha and offset (1: not coarsened, or
        /// the wrap did not run); AlphaWrap.EstimatedTriangles is the estimate it was judged on (the two-sided one if WrapMaxEstimatedTriangles > 0, else the one-sided one)
        /// </summary>
        public double AlphaWrapCoarsening = 1;
        /// <summary> alpha wrap fallback: the target triangle count that was asked for (WrapTargetTriangles or WrapTargetFactor; 0 = none) </summary>
        public int AlphaWrapTarget;
        /// <summary> alpha wrap fallback with a target: the triangle count of the result (0 without a target or the wrap) </summary>
        public int AlphaWrapTargetReached;
        /// <summary>
        /// alpha wrap fallback with a target: "" if the result has at most AlphaWrapTarget triangles, otherwise what limits it:
        /// <see cref="TargetLimitDistance"/>, <see cref="TargetLimitNotSimplified"/>, <see cref="TargetLimitNotCertified"/> or
        /// <see cref="TargetLimitQualityPass"/>
        /// </summary>
        public string AlphaWrapTargetLimit = "";
        /// <summary> AlphaWrapTargetLimit: no further collapse keeps the guarantees within WrapTargetMaxDistance (raise it for fewer triangles) </summary>
        public const string TargetLimitDistance = "no collapse left within the certified distance";
        /// <summary> AlphaWrapTargetLimit: the wrap is not simplified (SimplifyAlphaWrap off, or the simplification not clean or certified), so there is no reduction to continue </summary>
        public const string TargetLimitNotSimplified = "the wrap is not simplified";
        /// <summary> AlphaWrapTargetLimit: the reduction to the target was not clean or not certified, the simplified wrap before it is the result </summary>
        public const string TargetLimitNotCertified = "the reduction to the target is not certified";
        /// <summary> AlphaWrapTargetLimit: the quality pass (AlphaWrapQualityPass) split caps and the result is above the target </summary>
        public const string TargetLimitQualityPass = "the quality pass added triangles";
        /// <summary> alpha wrap fallback: true if the result is the simplified wrap (SimplifyAlphaWrap) </summary>
        public bool AlphaWrapSimplified;
        /// <summary> alpha wrap fallback: true if the result is the improved wrap (AlphaWrapQualityPass) </summary>
        public bool AlphaWrapQualityImproved;
        /// <summary>
        /// alpha wrap fallback (UsedAlphaWrap): the triangle quality of the result, measured, not enforced: its
        /// smallest and largest triangle angle (degrees) and the share of its triangles with an angle above 170 degrees
        /// (0..1). NaN if the result is not the alpha wrap
        /// </summary>
        public double AlphaWrapMinAngleDegrees = double.NaN, AlphaWrapMaxAngleDegrees = double.NaN, AlphaWrapShareAbove170 = double.NaN;
        public bool UsedExactResolve;
        /// <summary> why the exact resolve was not used ("" if it was or was not tried) </summary>
        public string ExactResolveReason = "";
        /// <summary> resolves done by the exact stage (0 if not tried) </summary>
        public int ExactResolveRoundsUsed;
        /// <summary> per resolve round: self-intersecting triangle pairs of its result (diagnostics) </summary>
        public List<int> ExactResolveSelfIntersections = new List<int>();
        /// <summary>
        /// per resolve round with SafeRounding: edits (and triangles removed) of the repair after rounding the resolve,
        /// and of the repair after SeparateTouchingVertices (diagnostics)
        /// </summary>
        public List<Index2i> ExactRoundingEdits = new List<Index2i>();
        /// <summary>
        /// milliseconds per phase of the last Apply() (summed if a phase runs more than once): "repair", "check"
        /// (cleanness incl. self-intersections), "volume" (VolumeIoU), "exact" (exact resolve incl. its checks;
        /// parts of it in "exact.orient", "exact.closed", "exact.resolve", "exact.check", "exact.volume"),
        /// "solidify", "solidify." + the phases of MeshWindingNumberSolidify, "solidify.check" (cleanness of
        /// its result), "wrap" (alpha wrap fallback), "wrap.simplify" (SimplifyAlphaWrap) and "solidify.finish" (post-processing a solidify that was
        /// stopped on self-intersections, if the wrap failed)
        /// </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        Stopwatch phase_timer = new Stopwatch();
        void end_phase(string name)
        {
            add_phase(name, phase_timer.ElapsedMilliseconds);
            phase_timer.Restart();
        }
        void add_phase(string name, long ms)
        {
            long cur;
            PhaseMilliseconds.TryGetValue(name, out cur);
            PhaseMilliseconds[name] = cur + ms;
        }

        /// <summary> the mesh is modified by the repair stage (see class comment) </summary>
        public MeshRobustRepair(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        /// <summary>
        /// Repair, check, and solidify if needed (see class comment). On success Mesh is the result.
        /// </summary>
        /// <returns>
        /// false if cancelled, if the input is empty, if its diagonal is not finite (ResultReason ScaleNotFinite), or if
        /// both the solidify fallback (eg the grid would be too large) and the alpha wrap failed; Mesh is then not replaced. Also false with
        /// LostFarPart, LostSeparatePart (a closed part) or LostFarOpenPart (an open part), the result in Mesh, if a separable part of an input
        /// of two or more parts is not in the result (MeshRobustRepairLostParts: judged against the part's own size), or if the alpha wrap is a thin shell around an input that has a solid (ThinShellWrap, in Mesh). true otherwise, also for an unclean solidify result (when the wrap is off or failed): check ResultReason.
        /// </returns>
        public bool Apply()
        {
            lostPartInput = null;
            farParts = false;
            lostFarRejected = false;
            frameScale = 1;
            LastFrameScale = 1;
            reset_outputs();
            ComponentsFlippedByNesting = 0;
            ResultVolumeIoU = double.NaN;
            reset_lost_area();
            ResultPath = ResultPaths.None;
            PassedThroughTriangles = 0;
            PassThroughFellBack = false;
            LostPartCheckSkipped = "not reached";
            phase_timer.Restart();
            // unused vertices are no part of the solid, but far away they would make every threshold relative to the
            // diagonal (degenerate area, solidify cell size, wrap alpha) meaningless
            MeshEditor.RemoveUnusedVertices(Mesh);
            // a diagonal that is not finite (an extent above about 1e154, or coordinates that are not finite): every
            // threshold relative to it would be infinite (the repair collapsed whole components); not repaired, Mesh not
            // replaced
            if (Mesh.TriangleCount > 0 && !(MeshRepairMetrics.TriangleBounds(Mesh).DiagonalLength < double.PositiveInfinity)) {
                ResultReason = ScaleNotFinite;
                return false;
            }
            if (Mesh.TriangleCount > 0 && !CoordinatesInRange(Mesh)) {
                ResultReason = OutsideRange;
                return false;
            }
            // One working frame for the whole Apply(): the mesh scaled by a power of two (exact) to an extent in [1, 2), the result scaled back. Every
            // stage, tolerance, float cast and power then sees the same numbers for a scene and its 2^k scaled copy, so the result is the same
            // up to the scale, by construction (the windows of scales where single stages were not, F3 to F5 of RepairCampaignFindings, are
            // gone with it). Not with RepairF (a user function on user coordinates), and for a mesh that cannot be scaled exactly
            // (a tiny extent far from the origin).
            double s = RepairF == null && NoWorkingFrame == false && Mesh.TriangleCount > 0 && repair_settings_allow_frame() ? MeshRepairMetrics.unit_scale_for_mesh(Mesh, false, 0, 0) : 1;
            if (s == 1)
                return apply_reported();
            frameScale = s;
            double roundingAbsolute = RoundingAbsoluteTolerance;
            DMesh3 callersMesh = Mesh;
            MeshRepairMetrics.ScaleVertices(Mesh, s);
            RoundingAbsoluteTolerance = roundingAbsolute * s;
            bool ok = false;
            try {
                ok = apply_reported();
            } finally {
                RoundingAbsoluteTolerance = roundingAbsolute;
                map_back(1 / s, callersMesh);
                frameScale = 1;
            }
            return ok;
        }

        /// <summary> (tests, benchmarks: an A/B in one build) Apply() works on the mesh as it is, without the working frame </summary>
        internal bool NoWorkingFrame;
        // ConfigureRepair is applied to a probe first: ToleranceMode.Absolute (the 1.0.324 values, also some constants inside MeshAutoRepair that cannot be scaled
        // from here) means the caller's units matter, so there is no frame; a callback that throws on the probe (it needs a mesh) is not framed either
        bool repair_settings_allow_frame()
        {
            if (ConfigureRepair == null)
                return true;
            try {
                var probe = new MeshAutoRepair(new DMesh3());
                ConfigureRepair(probe);
                return probe.ToleranceMode != MeshAutoRepair.ToleranceModes.Absolute;
            } catch (Exception) {
                return false;
            }
        }

        // the scale of the working frame of the running Apply() (1: none); lengths the caller set are multiplied by it
        double frameScale = 1;
        /// <summary> (tests) the power of two the last Apply() scaled the mesh by for its working frame (1: it worked on the mesh as it was) </summary>
        internal double LastFrameScale { get; private set; } = 1;

        // the meshes the last Apply() made in the frame, and the lengths it reports, back to the caller's frame (s = 1 / frame scale; exact)
        void map_back(double s, DMesh3 callersMesh)
        {
            LastFrameScale = 1 / s;
            UsedLostAreaTolerance *= s;
            LostArea *= s * s;
            LostAreaMaxDistance *= s;
            var done = new HashSet<DMesh3>();
            Action<DMesh3> back = (m) => {
                if (m != null && done.Add(m))
                    MeshRepairMetrics.ScaleVertices(m, s);
            };
            back(callersMesh);          // (the mesh the caller handed over, which Mesh may no longer be: it was scaled into the frame)
            back(Mesh);
            if (Solidify != null) {
                back(Solidify.Mesh);
                back(Solidify.Result);
                Solidify.UsedCellSize *= s;
            }
            if (AlphaWrap != null) {
                back(AlphaWrap.Mesh);
                back(AlphaWrap.Result);
                AlphaWrap.UsedAlpha *= s;
                AlphaWrap.UsedOffset *= s;
            }
            back(ExactResolveRounding?.Mesh);
            back(SeparatedRounding?.Mesh);
            // (a setting, not a diagnostic: what the caller set, in its units)
            if (ExactResolveRounding != null) ExactResolveRounding.AbsoluteTolerance *= s;
            if (SeparatedRounding != null && SeparatedRounding != ExactResolveRounding) SeparatedRounding.AbsoluteTolerance *= s;
        }

        /// <summary>
        /// Stage 0 of the part handling (docs/design/ROBUST_REPAIR_SCALE_FREE.md, option B), on by default: a closed component of the input that is clean on its own
        /// (NotCleanReason of the component alone) and whose bounding box meets the box of no other component is passed through untouched, and only
        /// the other components go through the repair stages (at most 64 components; the result is the union). Where the repair result of the others
        /// reaches the box of a passed part, or the union is not clean by NotCleanReason (a part far away makes its degenerate area too large), the input is repaired as a whole
        /// (then, if that fails, the union is returned with ScaleDisparity). An input that is clean as a whole is returned as it is (the needle cleanup of the
        /// repair would move its corners, #1337), unless ConfigureRepair is set. Off: the whole input goes through the stages, as before.
        /// </summary>
        public bool PassThroughCleanParts = true;
        /// <summary> triangles of the clean parts the last Apply() passed through untouched (PassThroughCleanParts; 0 if none) </summary>
        public int PassedThroughTriangles { get; private set; }
        /// <summary> (tests) the last Apply() split off clean parts, but the result of the others met one of them, so the input was repaired as a whole </summary>
        internal bool PassThroughFellBack { get; private set; }
        /// <summary> (tests) called with the result of the others before it is checked against the passed parts (the geometry of the solidify and the wrap grows by about a cell) </summary>
        internal Action<DMesh3> PassThroughTestHook;
        bool passThroughOff;

        /// <summary> (tests, benchmarks: an A/B in one build) Apply() without the final lost-part check (MeshRobustRepairLostParts) </summary>
        internal bool NoLostPartCheck;
        /// <summary>
        /// (tests) why the final lost-part check of the last Apply() judged no part: "" if it judged the parts, else one of "off" (NoLostPartCheck),
        /// "no volume checks" (MinVolumeIoU 0), "empty", "one part", "too many components" (above MeshRobustRepairLostParts.MaxComponents), "no
        /// part judged" (every part below MinTriangles or without extent), "not reached" (Apply() was false before the check)
        /// </summary>
        internal string LostPartCheckSkipped { get; private set; } = "";
        // apply_checked, then the final lost-part check (MeshRobustRepairLostParts, F1 of docs/design/ROBUST_REPAIR_SCALE_FREE_STUDY.md): a
        // separable part of the input that the result does not reach, judged against the part's own size, makes Apply() false. Reasons: an open
        // part LostFarOpenPart; a closed part LostFarPart where the input's components lie far apart compared to their size (the far-part
        // switch, global_scale_share > 0: the meaning of LostFarPart), else LostSeparatePart. A ResultReason the stages set (eg an unclean
        // solidify) is kept after it ("; "). The earlier cleanups and checks judge sizes against the scene or the largest component, so a small
        // part could be lost with true. The parts are taken before the repair, which changes the mesh in place
        bool apply_reported()
        {
            string skipped;
            List<MeshRobustRepairLostParts.Part> parts = null;
            if (NoLostPartCheck)
                skipped = "off";
            else if (MinVolumeIoU <= 0)
                skipped = "no volume checks";
            else
                parts = MeshRobustRepairLostParts.Parts(Mesh, out skipped);
            bool far = parts != null && global_scale_share(Mesh) > 0;
            DMesh3 givenInput = MeasureResultVolumeIoU || MeasureLostArea ? new DMesh3(Mesh) : null;
            // (the solid the volume checks used, for ResultVolumeIoU: see OrientedReference)
            DMesh3 givenSolid = MeasureResultVolumeIoU ? (OrientedReference(givenInput) ?? givenInput) : null;
            DMesh3 nestedReading, nestedUnion;
            int nested;
            nesting_references(Mesh, out nestedReading, out nestedUnion, out nested);
            bool ok = apply_checked();
            if (ok == false && parts != null)
                skipped = "not reached";
            LostPartCheckSkipped = skipped;
            MeshRobustRepairLostParts.Part lost = ok ? MeshRobustRepairLostParts.FirstLost(parts, Mesh) : null;
            if (lost != null) {
                string reason = lost.Open ? LostFarOpenPart : far ? LostFarPart : LostSeparatePart;
                ResultReason = ResultReason == "" ? reason : reason + "; " + ResultReason;
                ok = false;
            }
            if (ok || ResultReason != "not repaired") {
                // (a stage flag: the pass-through of every part sets none)
                ResultPath = UsedAlphaWrap ? ResultPaths.AlphaWrap : UsedSolidify ? ResultPaths.Solidify : UsedExactResolve ? ResultPaths.ExactResolve
                    : ok && PassedThroughTriangles > 0 && wholePassedThrough ? ResultPaths.PassThrough : ResultPaths.Repair;
            }
            if (ok && givenInput != null && MeasureResultVolumeIoU)
                ResultVolumeIoU = final_volume_iou(givenSolid, Mesh);
            ComponentsFlippedByNesting = nested;
            // (#1566) outward shells inside others, taken for cavities: the solid differs from the union of the shells, which no reference derived the
            // same way can see
            if (ok && nested > 0 && MinVolumeIoU > 0 && CancelF() == false) {
                double nestingIoU;
                var readingSolid = new MeshRepairMetrics.SolidReference(nestedReading);
                var unionSolid = MeshRepairMetrics.SolidReference.Unoriented(nestedUnion);
                if (MeshRepairMetrics.VolumeIoUAtLeast(readingSolid, unionSolid, MinVolumeIoU, out nestingIoU, useCells: SampleCellsInVolumeCheck, childBudget: SampleCellsChildBudget) == false) {
                    ResultReason = ResultReason == "" ? NestingAmbiguous : NestingAmbiguous + "; " + ResultReason;
                    ok = false;
                }
            }
            // (a cancel during the report is a cancel: false, as in every stage; the result stays in Mesh)
            if (MeasureLostArea && givenInput != null && (ok || ResultPath != ResultPaths.None) && measure_lost_area(givenInput, Mesh) == false)
                ok = false;
            return ok;
        }

        void reset_lost_area()
        {
            UsedLostAreaTolerance = LostArea = LostAreaShare = LostAreaMaxDistance = double.NaN;
            if (LostTriangleIds == null || LostTriangleIds.Count > 0)
                LostTriangleIds = new List<int>();
        }

        // MeasureLostArea (MeshRobustRepairLostArea), in the working frame: the tolerance from the stage of the result, the lengths back to the caller's
        // frame in map_back. Also for a false Apply() that leaves a result in Mesh (a lost part): there the report is the most useful. false if cancelled
        bool measure_lost_area(DMesh3 input, DMesh3 result)
        {
            if (input.TriangleCount == 0)
                return true;
            double tol = LostAreaTolerance > 0 && LostAreaTolerance < double.PositiveInfinity ? LostAreaTolerance * frameScale : 0;
            if (!(tol > 0)) {
                // the stage's term: what the stage that made the result may move the surface by. A term that is not a finite number of at least 0
                // (NaN, infinity, negative: an invalid setting, or a diagnostic that is not set) counts as 0 and the others stand: the repair itself
                // passes such a setting on (MeshRoundingRepair rejects every edit for a bound that is not finite), and a NaN here would make every
                // comparison "not farther" and read 0 lost where walls were removed
                double stage = 0;
                if (ResultPath == ResultPaths.Solidify && Solidify != null)
                    stage = LostAreaTerm(2 * Solidify.UsedCellSize * Solidify.RefineFactor);
                else if (ResultPath == ResultPaths.AlphaWrap && AlphaWrap != null)
                    stage = LostAreaTerm(Math.Max(2, 1 + (AlphaWrapTarget > 0 ? (WrapTargetMaxDistance >= 0.5 ? WrapTargetMaxDistance : 0.5) : 0.5)) * AlphaWrap.UsedOffset);
                // the rounding bound (MeshRoundingRepair: the largest of the three terms), and the query's own error
                AxisAlignedBox3d box = MeshRepairMetrics.TriangleBounds(input);
                double ulp = MeshRobustRepairLostArea.Ulp(Math.Max(box.Min.MaxAbs, box.Max.MaxAbs));
                double rounding = Math.Max(LostAreaTerm(RoundingMaxDisplacement * MeshRepairMetrics.LargestComponentDiagonal(input)),
                    Math.Max(LostAreaTerm(RoundingUlpFactor * ulp), LostAreaTerm(RoundingAbsoluteTolerance)));
                tol = Math.Max(stage, rounding) + LostAreaQueryUlps * LostAreaTerm(ulp);
                // (nothing valid to derive it from, eg coordinates that are not finite: not measured, never "0 lost")
                if (!(tol > 0 && tol < double.PositiveInfinity))
                    return true;
            }
            MeshRobustRepairLostArea.Report rep = MeshRobustRepairLostArea.Measure(input, result, tol, true, CancelF);
            if (rep == null)
                return false;
            UsedLostAreaTolerance = tol;
            LostArea = rep.LostArea;
            LostAreaShare = rep.InputArea > 0 && rep.InputArea < double.PositiveInfinity ? rep.LostArea / rep.InputArea : double.NaN;
            LostTriangleIds = rep.LostTriangles;
            LostAreaMaxDistance = rep.MaxDistance;
            return true;
        }

        /// <summary> the query's own error in the derived tolerance of MeasureLostArea, in units in the last place of the input's largest coordinate (see LostAreaTolerance) </summary>
        internal const double LostAreaQueryUlps = 64;

        static double LostAreaTerm(double x) { return x >= 0 && x < double.PositiveInfinity ? x : 0; }

        // the volume IoU of the final result against the input as given (ResultVolumeIoU)
        double final_volume_iou(DMesh3 input, DMesh3 result)
        {
            double iou;
            if (input.TriangleCount == 0)
                return double.NaN;
            var inputSolid = new MeshRepairMetrics.SolidReference(input);
            var resultSolid = MeshRepairMetrics.SolidReference.Unoriented(result);
            MeshRepairMetrics.VolumeIoUAtLeast(inputSolid, resultSolid, MinVolumeIoU > 0 ? MinVolumeIoU : 0.99, out iou, useCells: SampleCellsInVolumeCheck, childBudget: SampleCellsChildBudget);
            return iou;
        }

        bool wholePassedThrough;

        bool apply_checked()
        {
            if (passThroughOff == false)
                PassThroughFellBack = false;
            PassedThroughTriangles = 0;
            wholePassedThrough = false;
            DMesh3 passed, rest;
            List<AxisAlignedBox3d> passedBoxes;
            // a clean input is returned as it is, whatever its parts (their boxes may meet: a cavity), unless ConfigureRepair asks for a change of a clean
            // mesh (eg MinEdgeLengthTol) or RepairF is set: #1337
            if (PassThroughCleanParts && passThroughOff == false && RepairF == null && ConfigureRepair == null && Mesh.TriangleCount > 0
                && NotCleanReason(Mesh, CheckSelfIntersections) == "") {
                PassedThroughTriangles = Mesh.TriangleCount;
                wholePassedThrough = true;
                Mesh = new DMesh3(Mesh);
                ResultReason = "";
                return true;
            }
            if (PassThroughCleanParts && passThroughOff == false && RepairF == null && Mesh.TriangleCount > 0
                && MeshRobustRepairPassThrough.Split(Mesh, MaxFarParts, CheckSelfIntersections, out passed, out rest, out passedBoxes))
                return apply_passing_through(passed, rest, passedBoxes);
            return apply_checked_whole();
        }

        // the parts that pass through are put back into the result of the others
        bool apply_passing_through(DMesh3 passed, DMesh3 rest, List<AxisAlignedBox3d> passedBoxes)
        {
            DMesh3 original = Mesh;
            int passedTriangles = passed.TriangleCount;
            bool ok;
            if (rest.TriangleCount == 0) {
                // every part is clean: nothing to repair
                wholePassedThrough = true;
                Mesh = passed;
                ok = true;
                ResultReason = "";
            } else {
                Mesh = rest;
                bool done = false;
                try {
                    ok = apply_checked_whole();
                    done = true;
                } finally {
                    if (done == false)
                        Mesh = original;        // (an exception: the instance keeps the mesh it was given)
                }
                if (ok && PassThroughTestHook != null)
                    PassThroughTestHook(Mesh);
                if (MeshRobustRepairPassThrough.MeetsAny(Mesh, passedBoxes) && ok) {
                    // the result of the others grew into a passed part (the solidify and the wrap change the geometry by about a cell): as a whole
                    Mesh = original;
                    reset_outputs();
                    passThroughOff = true;
                    PassThroughFellBack = true;
                    try { return apply_checked_whole(); } finally { passThroughOff = false; }
                }
                if (ok == false && Mesh == rest)
                    Mesh = original;        // (not repaired: Mesh is not replaced)
                else {
                    // (a new mesh: the result of the others may be the Result of the solidify or the wrap, which stay as they were)
                    var union = new DMesh3(Mesh);
                    MeshEditor.Append(union, passed);
                    Mesh = union;
                }
            }
            if (rest.TriangleCount == 0 || Mesh != original) {
                PassedThroughTriangles = passedTriangles;
                if (ok) {
                    // the union is judged as a whole: the degenerate area of NotCleanReason is relative to all triangles, which a part far
                    // away from the rest can make too large (the parts are unchanged, but the union is not clean by that criterion). Then the
                    // input is repaired as a whole as before; only if that fails too, the union is returned, reported as ScaleDisparity
                    if (ResultReason == "" && NotCleanReason(Mesh, CheckSelfIntersections) != "") {
                        DMesh3 union = Mesh;
                        Mesh = original;
                        reset_outputs();
                        passThroughOff = true;
                        PassThroughFellBack = true;
                        bool wholeOk;
                        try { wholeOk = apply_checked_whole(); } finally { passThroughOff = false; }
                        if (wholeOk) {
                            PassedThroughTriangles = 0;
                            return true;
                        }
                        Mesh = union;       // (the diagnostics are those of the failed repair as a whole)
                        PassedThroughTriangles = passedTriangles;
                        ResultReason = ScaleDisparity;
                        return false;
                    }
                }
            }
            return ok;
        }

        bool apply_checked_whole()
        {
            bool ok;
            try {
                ok = apply_core();
                // components far apart compared to their size: a closed part of the input that is not in the result is not repaired
                // (never a silent loss with true: the owner's rule for far parts, #425). The result stays in Mesh
                if (ok && lostPartInput != null && lost_part(lostPartInput, Mesh)) {
                    ResultReason = LostFarPart;
                    ok = false;
                }
            } finally {
                lostPartInput = null;
            }
            return ok;
        }

        void reset_outputs()
        {
            UsedSolidify = false;
            ResultReason = "not repaired";
            UsedInput = false;
            UsedAlphaWrap = false;
            WrapFilledLoops = WrapFillCandidateLoops = 0;
            AlphaWrapTriangles = 0;
            AlphaWrapCoarsening = 1;
            AlphaWrapSimplified = false;
            AlphaWrapTarget = AlphaWrapTargetReached = 0;
            AlphaWrapTargetLimit = "";
            AlphaWrapSharpFeaturesApplied = false;
            AlphaWrapQualityImproved = false;
            AlphaWrapMinAngleDegrees = AlphaWrapMaxAngleDegrees = AlphaWrapShareAbove170 = double.NaN;
            SolidifyReason = "";
            Solidify = null;
            AlphaWrap = null;
            WrapReason = "";
            UsedExactResolve = false;
            ExactResolveReason = "";
            scaleOnly = null;
            MaxScaleErosionShare = 0;
            ScaleRetry = false;
            notClosedFrom = null;
            TopologyChanged = false;
            GenusBefore = GenusAfter = double.NaN;
            ExactResolveRoundsUsed = 0;
            ExactResolveSelfIntersections.Clear();
            ExactRoundingEdits.Clear();
            ExactResolveRounding = SeparatedRounding = null;
            FallbackReason = "";
            RepairVolumeIoU = double.NaN;
            VolumeChecksRun = 0;
            VolumeCheckStats = new List<MeshRepairMetrics.VolumeCheckStats>();
            PhaseMilliseconds.Clear();
        }

        // the input of the last Apply() if its components lie so far apart that a triangle of one is below the degenerate
        // area of all triangles (global_scale_share > 0), null otherwise: only then are parts checked for loss
        DMesh3 lostPartInput;
        // the input of the last Apply() has far parts (see lostPartInput; independent of MinVolumeIoU): the cleanups are relative to the largest component
        bool farParts;
        /// <summary> (tests) the last Apply() treated the input as having far parts (relative cleanups and tolerances) </summary>
        internal bool FarPartsHandled { get { return farParts; } }
        const int MaxFarParts = 64;
        static bool few_parts(DMesh3 mesh)
        {
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            return comps.Count <= MaxFarParts;
        }
        // a resolve was rejected for a lost part in the last Apply(): the repair stage collapsed it (its cleanups are relative to the
        // diagonal of all triangles), so the retry relative to the largest component starts from the input
        bool lostFarRejected;
        /// <summary> (tests) the repair as before the far part handling: no cleanups relative to the largest component, no early lost part checks, so that the final one is reached </summary>
        internal bool BypassEarlyLostPartChecks;

        bool apply_core()
        {
            DMesh3 input = new DMesh3(Mesh);
            // the input's solid for the volume checks and the fallbacks: an oriented, welded copy if the input is unwelded or its triangles
            // disagree in orientation (#1548), else the input itself
            DMesh3 reference = OrientedReference(input) ?? input;
            bool disparity = global_scale_share(input) > 0;
            // far parts for the repair stage and the first resolve: few closed components (a cracked or unwelded surface, a soup, has many
            // open pieces that are no separate parts: its tolerances stay relative to the whole)
            farParts = disparity && BypassEarlyLostPartChecks == false && input.IsClosed() && few_parts(input);
            if (MinVolumeIoU > 0 && disparity)
                lostPartInput = input;
            DMesh3 result;
            if (RepairF != null) {
                result = RepairF(Mesh);
            } else {
                var repair = new MeshAutoRepair(Mesh) { PostRepairCleanup = true };
                ConfigureRepair?.Invoke(repair);
                if (frameScale != 1) {
                    // lengths the caller set are in its units: into the frame (a default value stays the default, which is relative)
                    if (repair.ToleranceMode == MeshAutoRepair.ToleranceModes.Absolute || repair.RepairTolerance != MathUtil.ZeroTolerancef)
                        repair.RepairTolerance *= frameScale;
                    if (repair.ToleranceMode == MeshAutoRepair.ToleranceModes.Absolute || repair.MinEdgeLengthTol != 0.0001)
                        repair.MinEdgeLengthTol *= frameScale;
                    if (!double.IsNaN(repair.CleanupReferenceDiagonal) && !double.IsInfinity(repair.CleanupReferenceDiagonal))
                        repair.CleanupReferenceDiagonal *= frameScale;
                }
                if (farParts) {
                    // the tolerances of the repair are fractions of the diagonal of all triangles (capped by the median edge): with parts far
                    // apart that is far larger than the parts, whose edges are then collapsed as short. Relative to the largest component
                    // (after ConfigureRepair: the final fractions are scaled; an absolute tolerance or ToleranceModes.Absolute stays as set)
                    double largest = MeshRepairMetrics.LargestComponentDiagonal(Mesh), all = MeshRepairMetrics.TriangleBounds(Mesh).DiagonalLength;
                    if (double.IsNaN(repair.CleanupReferenceDiagonal))
                        repair.CleanupReferenceDiagonal = largest;
                    if (largest > 0 && all > largest) {
                        repair.RelativeRepairTolerance *= largest / all;
                        repair.RelativeMinEdgeLength *= largest / all;
                    }
                }
                repair.Apply();
                result = repair.Mesh;
            }
            end_phase("repair");
            if (CancelF())
                return false;
            FallbackReason = NotCleanReason(result, CheckSelfIntersections);
            end_phase("check");
            DMesh3 solidifyFrom = result;
            bool uncleanVolumePending = false;
            if (FallbackReason == "") {
                // (NaN: the input has no solid to compare with, accepted)
                string differs = volume_check(reference, result, out RepairVolumeIoU);
                // components far apart: the repair stage's cleanups (relative to all triangles) may have collapsed a part, which the
                // volume check does not see; the input is resolved instead
                if (differs == "" && lostPartInput != null && BypassEarlyLostPartChecks == false && lost_part(reference, result))
                    differs = PartsLostFarReason;
                if (differs != "") {
                    FallbackReason = differs;
                    solidifyFrom = reference;
                }
                end_phase("volume");
            } else if (result.TriangleCount == 0) {
                solidifyFrom = reference;
            } else if (ExactResolve == false || input.TriangleCount == 0) {
                // (an empty input: the check sends the fallbacks to it, and Apply() stops below, as before)
                unclean_result_volume(reference, result, ref solidifyFrom);
            } else {
                // with ExactResolve, the volume of an unclean result is checked only if its exact resolve fails: a
                // successful one replaces UsedInput and RepairVolumeIoU, and solidifyFrom is not used. The check, and
                // so its outputs, are the same as if it had run here
                uncleanVolumePending = true;
            }
            UsedInput = solidifyFrom == reference;
            if (FallbackReason == "") {
                Mesh = result;
                ResultReason = "";
                return true;
            }
            if (solidifyFrom.TriangleCount == 0)
                return false;
            if (ExactResolve) {
                // the repair result first, also when it lost volume (UsedInput): its resolve is checked against the
                // input's volume, and resolving keeps the repaired geometry
                DMesh3 exact = null;
                if (result.TriangleCount > 0) {
                    // a clean result whose volume differs resolves to its own solid again: skip to the next try
                    bool cleanButDiffers = FallbackReason == VolumeDiffers || FallbackReason == PartsLost || FallbackReason == PartsLostFarReason;
                    if (cleanButDiffers == false)
                        exact = resolve_exactly(result, reference, false);
                    // its volume may differ (or a part be lost) only because components are oriented differently from
                    // the input (eg a nested part taken for a cavity): then resolve it again, oriented by the input's
                    // solid, if that orientation differs at all
                    if (exact == null && (cleanButDiffers || ExactResolveReason == VolumeDiffers || ExactResolveReason == PartsLost || ExactResolveReason == PartsLostFarReason) && CancelF() == false)
                        exact = resolve_exactly(result, reference, true);
                }
                bool fromResult = exact != null;
                if (exact == null && uncleanVolumePending) {
                    end_phase("exact");
                    unclean_result_volume(reference, result, ref solidifyFrom);
                    UsedInput = solidifyFrom == reference;
                }
                // then the input itself (a clean result whose volume differs, or an unclean one that lost volume)
                if (exact == null && solidifyFrom != result && CancelF() == false) {
                    string reason = ExactResolveReason;
                    exact = resolve_exactly(solidifyFrom, reference, false);
                    if (exact == null && reason != "")
                        ExactResolveReason = reason + "; input: " + ExactResolveReason;
                }
                // components far apart compared to their size: the degenerate area of NotCleanReason (relative to the
                // diagonal of all triangles) also covers real triangles, and the cleanups above collapse them. Once
                // more, with the cleanups relative to the largest component (only where the resolves above failed, so a
                // result they accept is unchanged)
                if (exact == null && CancelF() == false) {
                    DMesh3 from = solidifyFrom != result || result.TriangleCount == 0 ? solidifyFrom : result;
                    if (lostFarRejected)
                        from = reference;
                    MaxScaleErosionShare = global_scale_share(from);
                    if (MaxScaleErosionShare > 0 && from != notClosedFrom) {
                        ScaleRetry = true;
                        string reason = ExactResolveReason;
                        exact = resolve_exactly(from, reference, false, true);
                        if (exact == null)
                            ExactResolveReason = reason + "; relative to the largest component: " + ExactResolveReason;
                        else
                            fromResult = from == result;
                    }
                }
                end_phase("exact");
                if (CancelF())
                    return false;
                if (exact != null) {
                    Mesh = exact;
                    UsedExactResolve = true;
                    UsedInput = !fromResult;
                    ResultReason = "";
                    return true;
                }
                // resolved, and clean but for the degenerate area of NotCleanReason, which components far apart make
                // too large: not repaired (NotCleanReason rejects it), but the resolved mesh is returned, not the
                // solidify's or the wrap's, which cannot keep the parts at that scale
                if (scaleOnly != null) {
                    Mesh = scaleOnly;
                    UsedExactResolve = true;
                    UsedInput = scaleOnlyFromInput;
                    ResultReason = ScaleDisparity;
                    TopologyChanged = false;
                    GenusBefore = GenusAfter = double.NaN;
                    return false;
                }
                // the topology outputs describe the returned mesh only (a rejection stays in ExactResolveReason)
                TopologyChanged = false;
                GenusBefore = GenusAfter = double.NaN;
            }
            // a self-intersecting contour is discarded for the alpha wrap: stop the solidify before its
            // post-processing (which rarely removes them), finish it only if the wrap fails
            bool wrapOnSelfIntersections = CheckSelfIntersections && AlphaWrapFallback;
            Solidify = new MeshWindingNumberSolidify(solidifyFrom) {
                GridResolution = SolidifyResolution, Adaptive = true, CancelF = CancelF, StopIfSelfIntersecting = wrapOnSelfIntersections
            };
            bool solidified = Solidify.Compute() && Solidify.Result != null && Solidify.Result.TriangleCount > 0;
            end_phase("solidify");
            foreach (var kv in Solidify.PhaseMilliseconds) PhaseMilliseconds["solidify." + kv.Key] = kv.Value;
            if (CancelF())
                return false;
            if (solidified == false)
                SolidifyReason = "solidify failed";
            else if (Solidify.StoppedOnSelfIntersections)
                SolidifyReason = "self-intersections";
            else
                SolidifyReason = NotCleanReason(Solidify.Result, CheckSelfIntersections);
            end_phase("solidify.check");
            if (SolidifyReason != "" && AlphaWrapFallback) {
                double smallAlpha = 0, smallOffset = 0;
                if (SmallInputWrapTriangles > 0 && input.TriangleCount < SmallInputWrapTriangles && solidifyFrom.TriangleCount < SmallInputWrapTriangles && lostPartInput == null)
                    small_input_wrap(solidifyFrom, out smallAlpha, out smallOffset);
                // the short boundary loops of the welded input are holes of a surface meant to be closed: filled, so that the alpha ball does not
                // pass them and wrap both sides of the sheet (#1542, #1575). Not copied if there is none
                DMesh3 wrapFrom = solidifyFrom;
                WrapFilledLoops = WrapFillCandidateLoops = 0;
                if (WrapFillLoopAlphas > 0) {
                    double nominalAlpha = smallAlpha > 0 ? smallAlpha : WrapRelativeAlpha > 0 ? MeshRepairMetrics.TriangleBounds(solidifyFrom).DiagonalLength / WrapRelativeAlpha : 0;
                    DMesh3 filledCopy = RobustRepairWrapHoleFill.Fill(solidifyFrom, nominalAlpha, WrapFillLoopAlphas, CancelF, out WrapFilledLoops, out WrapFillCandidateLoops);
                    end_phase("wrap.fill");
                    if (CancelF())
                        return false;
                    if (filledCopy != null)
                        wrapFrom = filledCopy;
                }
                AlphaWrap = new MeshAlphaWrap(wrapFrom) { RelativeAlpha = WrapRelativeAlpha, RelativeOffset = WrapRelativeOffset, Alpha = smallAlpha, Offset = smallOffset, CancelF = CancelF,
                    MaxEstimatedTriangles = WrapMaxEstimatedTriangles, MaxAlphaCoarsening = WrapMaxAlphaCoarsening, MaxThreads = WrapMaxThreads };
                AlphaWrap.Compute();
                AlphaWrapCoarsening = AlphaWrap.AlphaCoarsening;
                end_phase("wrap");
                // (PointLimitReached: less detail, same guarantees)
                if ((AlphaWrap.Status == MeshAlphaWrap.WrapStatus.Success || AlphaWrap.Status == MeshAlphaWrap.WrapStatus.PointLimitReached)
                    && AlphaWrap.Result != null && AlphaWrap.Result.TriangleCount > 0) {
                    // components far apart compared to their size: the wrap is one global alpha, and a result with surface far from the input (one
                    // blob over the separated parts, which encloses the parts: the lost-part check cannot see it) is not a repair of it. The
                    // solidify result is taken instead, and judged as always
                    if (lostPartInput != null && few_parts(lostPartInput) && wrap_too_coarse(lostPartInput, AlphaWrap.UsedAlpha + AlphaWrap.UsedOffset))
                        WrapReason = WrapTooCoarse;
                    else {
                    AlphaWrapTriangles = AlphaWrap.Result.TriangleCount;
                    DMesh3 raw = AlphaWrap.Result;
                    if (AlphaWrapSharpFeatures) {
                        var features = new AlphaWrapFeaturePass(new DMesh3(raw, true), wrapFrom, AlphaWrap.UsedAlpha, AlphaWrap.UsedOffset) {
                            CancelF = CancelF, VarifoldCreases = AlphaWrapSharpFeaturesOnSoups };
                        ConfigureWrapFeatures?.Invoke(features);
                        bool computed = features.Compute();
                        end_phase("wrap.features");
                        if (!computed || CancelF())
                            return false;
                        if (features.PlaneMoves + features.CreaseSnaps + features.Splits + features.Flips > 0) {
                            raw = new DMesh3(features.Mesh, true);
                            AlphaWrapSharpFeaturesApplied = true;
                        }
                    }
                    DMesh3 wrapped = raw;
                    int target = WrapTargetTriangles > 0 ? WrapTargetTriangles
                        : WrapTargetFactor > 0 ? (int)Math.Min(int.MaxValue, Math.Max(1.0, Math.Ceiling(WrapTargetFactor * input.TriangleCount))) : 0;
                    if (SimplifyAlphaWrap) {
                        wrapped = simplify_wrap(raw, AlphaWrap.UsedOffset, wrapFrom) ?? raw;
                        end_phase("wrap.simplify");
                        if (CancelF())
                            return false;
                    }
                    string targetLimit = "";
                    if (target > 0) {
                        AlphaWrapTarget = target;
                        if (wrapped == raw) {
                            if (raw.TriangleCount > target)
                                targetLimit = TargetLimitNotSimplified;
                        }
                        else if (wrapped.TriangleCount > target) {
                            DMesh3 reduced = ReduceToTarget(wrapped, raw, target, (WrapTargetMaxDistance >= 0.5 ? WrapTargetMaxDistance : 0.5) * AlphaWrap.UsedOffset, CheckSelfIntersections, CancelF, wrapFrom);
                            end_phase("wrap.target");
                            if (CancelF())
                                return false;
                            if (reduced == null)
                                targetLimit = TargetLimitNotCertified;
                            else {
                                wrapped = reduced;
                                if (wrapped.TriangleCount > target)
                                    targetLimit = TargetLimitDistance;
                            }
                        }
                    }
                    if (AlphaWrapQualityPass && wrapped != raw) {
                        DMesh3 improved = ImproveWrapQuality(wrapped, raw, 0.5 * AlphaWrap.UsedOffset, CheckSelfIntersections, CancelF,
                            (p) => { DefaultWrapQuality(p); p.KeepClearOf = new DMeshAABBTree3(wrapFrom, true); ConfigureWrapQuality?.Invoke(p); });
                        end_phase("wrap.quality");
                        if (CancelF())
                            return false;
                        if (improved != null) {
                            wrapped = improved;
                            AlphaWrapQualityImproved = true;
                        }
                    }
                    AlphaWrapSimplified = wrapped != raw;
                    if (target > 0) {
                        AlphaWrapTargetReached = wrapped.TriangleCount;
                        AlphaWrapTargetLimit = targetLimit == "" && wrapped.TriangleCount > target ? TargetLimitQualityPass : targetLimit;
                    }
                    Mesh = wrapped;
                    UsedAlphaWrap = true;
                    angle_stats(wrapped, out AlphaWrapMinAngleDegrees, out AlphaWrapMaxAngleDegrees, out AlphaWrapShareAbove170);
                    // a wrap of both sides of a surface that is no solid (an open sheet, holes the fill did not close) is a thin shell around it: a
                    // clean mesh, but no repair of the input (#1575). Detected, not repaired: the shell stays in Mesh
                    if (is_thin_shell(wrapped, AlphaWrap.UsedOffset, wrapFrom, reference)) {
                        ResultReason = ThinShellWrap;
                        return false;
                    }
                    ResultReason = "";
                    return true;
                    }
                } else
                    WrapReason = AlphaWrap.Status + (AlphaWrap.Error != null ? ": " + AlphaWrap.Error : "");
            }
            if (solidified == false || CancelF())
                return false;
            if (Solidify.StoppedOnSelfIntersections) {
                // the wrap failed: the solidify result after all, post-processed as usual
                Solidify.FinishPostProcess();
                foreach (var kv in Solidify.PhaseMilliseconds) PhaseMilliseconds["solidify." + kv.Key] = kv.Value;
                end_phase("solidify.finish");
                if (CancelF() || Solidify.Result == null || Solidify.Result.TriangleCount == 0)
                    return false;
                SolidifyReason = NotCleanReason(Solidify.Result, CheckSelfIntersections);
            }
            Mesh = Solidify.Result;
            UsedSolidify = true;
            ResultReason = SolidifyReason;
            return true;
        }

        const string VolumeDiffers = "volume differs from the input", PartsLost = "parts of the input lost";
        /// <summary> WrapReason when the alpha wrap of an input with far parts is too coarse for its largest component and is not taken </summary>
        public const string WrapTooCoarse = "wrap rejected: alpha and offset are not small against the largest component (components far apart compared to their size)";
        // The wrap's alpha and offset are fractions of the diagonal of ALL triangles. With components far apart that is far larger than a component:
        // every part below alpha is replaced by an alpha-sized blob (a sphere of radius 1 next to a part at 4e6 became an octahedron of radius
        // 1400), and the result is "closed and encloses the input" but not a repair, which no check sees (the parts are inside their blobs). In that
        // mode a wrap is taken only if alpha + offset is at most this share of the largest component's diagonal (a single part at the defaults:
        // 0.0037, so a scene is accepted up to a disparity of about 13)
        const double WrapMaxResolutionShare = 0.05;

        internal static bool wrap_too_coarse(DMesh3 input, double alphaPlusOffset)
        {
            double largest = MeshRepairMetrics.LargestComponentDiagonal(input);
            return !(alphaPlusOffset <= WrapMaxResolutionShare * largest);
        }

        const string PartsLostFarReason = "a part of the input lost (components far apart compared to their size)";
        /// <summary> ResultReason of an Apply() that is false because a closed part of the input is not in the result, where components lie far apart compared to their size </summary>
        public const string LostFarPart = "scale disparity: a part of the input is lost (components far apart compared to their size)";
        /// <summary>
        /// ResultReason of an Apply() that is false because an open part of the input is not in the result, where parts lie apart (a
        /// separable part: components whose bounding boxes, padded by 1e-3 of their own diagonals, meet no other part's; the result does
        /// not reach it within a quarter of the part's own diagonal, see MeshRobustRepairLostParts). The result stays in Mesh. Any earlier
        /// ResultReason follows after "; "
        /// </summary>
        public const string LostFarOpenPart = "scale disparity: an open part of the input is lost (parts apart compared to their size)";
        /// <summary>
        /// ResultReason of an Apply() that is false because a closed separable part of the input (see LostFarOpenPart) is not in the result,
        /// where the components do not lie far apart in the sense of LostFarPart (which is reported there). "Far apart" is the far-part
        /// switch of LostFarPart: two or more components and a triangle below the degenerate area of NotCleanReason, relative to all
        /// triangles (also a sliver of the large part sets it), not a distance. The result stays in Mesh. Any earlier ResultReason follows
        /// after "; "
        /// </summary>
        public const string LostSeparatePart = "a separate part of the input is lost (the result does not reach it)";
        /// <summary>
        /// ResultReason when Apply() returns false because the input's diagonal is not finite: an extent above about
        /// 1e154 (eg finite coordinates near +-1e308), or infinite coordinates (and NaN ones where they show in the
        /// bounds); Mesh is not replaced (only its unused vertices are removed, as always)
        /// </summary>
        public const string ScaleNotFinite = "scale not finite: the diagonal of the input's bounds is not a finite double (coordinates that are not finite, or an extent above about 1e154); not repaired";
        /// <summary>
        /// ResultReason when Apply() returns false because the input is outside the supported range (class comment,
        /// docs/QA.md 2.5a): a nonzero coordinate with a magnitude outside [2^-200, 2^400] (subnormal numbers
        /// included), or an extent below 2^-40 of the largest coordinate magnitude (a scene far from the origin compared
        /// to its size). Mesh is not replaced. (Such inputs did not terminate beyond about 1e100 from the origin, or
        /// reported a clean result that was not, with subnormal coordinates.)
        /// </summary>
        public const string OutsideRange = "outside the supported range: a nonzero coordinate has a magnitude outside [2^-200, 2^400], or the extent is below 2^-40 of the largest magnitude (or a coordinate is not finite); not repaired";
        const double RangeMin = 6.2230152778611417e-61, RangeMax = 2.5822498780869086e120;   // 2^-200, 2^400

        /// <summary> the supported range of the class comment, for the used vertices </summary>
        static bool CoordinatesInRange(DMesh3 m)
        {
            Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
            double biggest = 0;
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                for (int k = 0; k < 3; ++k) {
                    Vector3d p = m.GetVertex(t[k]);
                    for (int c = 0; c < 3; ++c) {
                        double a = Math.Abs(p[c]);
                        if (!(a == 0 || (a >= RangeMin && a <= RangeMax)))
                            return false;
                        biggest = Math.Max(biggest, a);
                        lo[c] = Math.Min(lo[c], p[c]);
                        hi[c] = Math.Max(hi[c], p[c]);
                    }
                }
            }
            double extent = Math.Max(hi.x - lo.x, Math.Max(hi.y - lo.y, hi.z - lo.z));
            return extent >= biggest * 9.094947017729282e-13;      // 2^-40
        }

        /// <summary>
        /// ResultReason (and ExactResolveReason) when the exact resolve is clean but for the degenerate area of
        /// NotCleanReason, relative to the bounds of all triangles: components far apart compared to their size make it
        /// larger than their triangles. Apply() returns false with the resolved mesh, not the solidify's or the wrap's
        /// </summary>
        public const string ScaleDisparity = "scale disparity: degenerate triangles only by the diagonal of all triangles (components far apart compared to their size); clean by the diagonal of the largest component";
        // the exact resolve that is clean but for that (null: none), and whether it came from the input
        DMesh3 scaleOnly;
        bool scaleOnlyFromInput;

        /// <summary>
        /// the input's solid, for an input that is unwelded or whose triangles disagree in orientation (#1548), else null (the input itself is the
        /// reference). The winding number of such an input cancels at the flipped faces, so a volume reference taken from it is too small, a
        /// correct repair result fails the volume check, and the fallbacks would start from the raw soup. Here: the vertices on boundary edges
        /// welded within a hundredth of the median of the shortest edges (and of the shortest edge at either vertex), the welded mesh oriented consistently per connected component
        /// (MeshRepairOrientation.OrientComponents, the step of MeshAutoRepair), then the whole (WindingContouring.OrientedCopy). A
        /// welded, consistently oriented input gets null, and so the unchanged behaviour
        /// </summary>
        internal static DMesh3 OrientedReference(DMesh3 input)
        {
            DMesh3 welded = WeldedOriented(input);
            return welded == null ? null : WindingContouring.OrientedCopy(welded);
        }

        // the welded, per component consistently oriented mesh of OrientedReference (before the nesting and the global orientation), null for a welded,
        // consistently oriented input
        static DMesh3 WeldedOriented(DMesh3 input)
        {
            if (input.TriangleCount == 0)
                return null;
            int maxV = input.MaxVertexID;
            var onBoundary = new bool[maxV];
            bool conflict = false;
            foreach (int eid in input.EdgeIndices()) {
                Index2i et = input.GetEdgeT(eid);
                Index2i ev = input.GetEdgeV(eid);
                if (et.b == DMesh3.InvalidID) {
                    onBoundary[ev.a] = onBoundary[ev.b] = true;
                } else if (conflict == false) {
                    Index3i ta = input.GetTriangle(et.a), tb = input.GetTriangle(et.b);
                    conflict = (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref ta) != DMesh3.InvalidID)
                        == (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref tb) != DMesh3.InvalidID);
                }
            }
            // (a closed mesh has nothing to weld: the scan above is its gate)
            bool anyBoundary = false;
            foreach (bool b in onBoundary)
                anyBoundary |= b;
            if (anyBoundary == false && conflict == false)
                return null;
            // the shortest edge at each vertex: a vertex is welded to another only within a hundredth of it, and of the median of those (the weld
            // distance of MeshAutoRepair is 1e-8 of the diagonal, capped by a hundredth of the median edge; here the hundredth only, so that a soup with
            // noise on its corners is welded too, and local, so that a small part far from the others keeps its vertices)
            var shortest = new double[maxV];
            for (int i = 0; i < maxV; ++i)
                shortest[i] = double.PositiveInfinity;
            foreach (int eid in input.EdgeIndices()) {
                Index2i ev = input.GetEdgeV(eid);
                double len = input.GetVertex(ev.a).Distance(input.GetVertex(ev.b));
                if (len < shortest[ev.a]) shortest[ev.a] = len;
                if (len < shortest[ev.b]) shortest[ev.b] = len;
            }
            var finite = new List<double>();
            for (int i = 0; i < maxV; ++i)
                if (shortest[i] < double.PositiveInfinity)
                    finite.Add(shortest[i]);
            finite.Sort();
            double tol = finite.Count > 0 ? 0.01 * finite[finite.Count / 2] : 0;
            if (!(tol > 0 && tol < double.PositiveInfinity))
                return null;
            // the vertices on boundary edges, welded by the cells of the tolerance (the first one of a cell is the representative)
            var rep = new int[maxV];
            var cells = new Dictionary<Tuple<long, long, long>, List<int>>();
            bool welded = false;
            foreach (int vid in input.VertexIndices()) {
                rep[vid] = vid;
                if (onBoundary[vid] == false)
                    continue;
                Vector3d p = input.GetVertex(vid);
                if (!(Math.Abs(p.x) < double.PositiveInfinity && Math.Abs(p.y) < double.PositiveInfinity && Math.Abs(p.z) < double.PositiveInfinity))
                    return null;
                if (Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))) / tol > 1e18)
                    return null;      // (cells that a long cannot index: no weld)
                long cx = (long)Math.Floor(p.x / tol), cy = (long)Math.Floor(p.y / tol), cz = (long)Math.Floor(p.z / tol);
                int found = -1;
                for (long dx = -1; dx <= 1 && found < 0; ++dx)
                    for (long dy = -1; dy <= 1 && found < 0; ++dy)
                        for (long dz = -1; dz <= 1 && found < 0; ++dz) {
                            List<int> list;
                            if (cells.TryGetValue(Tuple.Create(cx + dx, cy + dy, cz + dz), out list))
                                foreach (int r in list) {
                                    double d = input.GetVertex(r).Distance(p);
                                    if (d <= tol && d <= 0.01 * Math.Min(shortest[r], shortest[vid])) {
                                        found = r;
                                        break;
                                    }
                                }
                        }
                if (found >= 0) {
                    rep[vid] = found;
                    welded = true;
                } else {
                    List<int> list;
                    var key = Tuple.Create(cx, cy, cz);
                    if (cells.TryGetValue(key, out list) == false)
                        cells[key] = list = new List<int>();
                    list.Add(vid);
                }
            }
            if (welded == false && conflict == false)
                return null;
            var m = new DMesh3(false, false, false);
            var newId = new int[maxV];
            foreach (int vid in input.VertexIndices())
                if (rep[vid] == vid)
                    newId[vid] = m.AppendVertex(input.GetVertex(vid));
            foreach (int vid in input.VertexIndices())
                newId[vid] = newId[rep[vid]];
            foreach (int tid in input.TriangleIndices()) {
                Index3i t = input.GetTriangle(tid);
                int a = newId[t.a], b = newId[t.b], c = newId[t.c];
                if (a != b && b != c && a != c)
                    m.AppendTriangle(a, b, c);      // (a triangle that would make an edge of three triangles is left out)
            }
            if (m.TriangleCount == 0)
                return null;
            new MeshRepairOrientation(m).OrientComponents();
            return m;
        }

        /// <summary>
        /// the number of closed components of the last Apply()'s input that the repair's reading of nesting flips although they are outward by
        /// their own volume (#1566): a closed shell inside an odd number of others counts as a cavity (MeshPostRepairCleanup.FindInvertedComponents,
        /// by parity), so a shell that is oriented outward inside another (an insert in a housing, a part modelled twice) is reversed and the
        /// result is hollow where the plain union of the input's shells (the nonzero winding number, the reading of ExactMeshBoolean.Resolve) is
        /// solid. 0 for an input without such a shell, and so for a real cavity (inward inside outward). The input as given to Apply(), counted
        /// on its welded, oriented copy if it is unwelded (#1548). If MinVolumeIoU is above 0 and the two readings differ in volume by more than
        /// 1 - MinVolumeIoU, Apply() is false with ResultReason NestingAmbiguous; below that difference it only reports the count
        /// </summary>
        public int ComponentsFlippedByNesting { get; private set; }

        /// <summary>
        /// ResultReason of an Apply() that is false because the input has outward shells inside others (ComponentsFlippedByNesting above 0) that the
        /// repair took for cavities, and the solid with them taken for cavities differs from the plain union of the input's shells by more than
        /// 1 - MinVolumeIoU: the input is ambiguous (an insert inside a housing, or a cavity modelled with the wrong orientation), and the repair
        /// does not decide it silently. The result (the reading as cavities) stays in Mesh. Any earlier ResultReason follows after "; "
        /// </summary>
        public const string NestingAmbiguous = "nesting ambiguous: inner shells taken as cavities";

        // the solid of the input as the repair reads its nesting (reference) and as the plain union of its shells (a component outward by its own
        // volume is not reversed for lying inside another); count: the components that differ, 0 if the two are the same solid
        static void nesting_references(DMesh3 given, out DMesh3 reading, out DMesh3 union, out int count)
        {
            reading = union = null;
            count = 0;
            if (given.TriangleCount == 0)
                return;
            DMesh3 b = WeldedOriented(given) ?? given;
            var flagged = MeshPostRepairCleanup.FindInvertedComponents(b);
            if (flagged.Count == 0)
                return;
            DMesh3 plain = new DMesh3(b, true);
            foreach (int[] comp in flagged) {
                bool outward = MeshPostRepairCleanup.volume_sign(b, comp) > 0;
                if (outward)
                    ++count;
                foreach (int tid in comp) {
                    if (outward == false)
                        plain.ReverseTriOrientation(tid);
                }
            }
            if (count == 0)
                return;
            reading = WindingContouring.OrientedCopy(b);
            Vector3d c = plain.CachedBounds.Center;
            if (MeshMeasurements.VolumeArea(plain, plain.TriangleIndices(), (vid) => plain.GetVertex(vid) - c).x < 0)
                plain.ReverseOrientation(false);
            union = plain;
        }

        // an unclean repair result can have lost parts of the input as well (eg erosion of an open region, which the
        // volume check of the exact stage rejects, but the solidify and the wrap do not check): then the fallbacks start
        // from the input, like for a clean result whose volume differs
        void unclean_result_volume(DMesh3 input, DMesh3 result, ref DMesh3 solidifyFrom)
        {
            if (volume_check(input, result, out RepairVolumeIoU) != "")
                solidifyFrom = input;
            end_phase("volume");
        }

        /// <summary> for tests: the volume checks (volume_check) the last Apply() ran </summary>
        internal int VolumeChecksRun;
        /// <summary>
        /// for tests and the benchmark (an A/B in one build): the volume IoU check samples only near the surfaces where
        /// the input and the result are winding-closed (VolumeSampleCells; default), false: in the region of
        /// MeshRepairMetrics.sample_region always
        /// </summary>
        internal bool SampleCellsInVolumeCheck = true;
        /// <summary> for the benchmark: the child budget of those cells (VolumeSampleCells) </summary>
        internal int SampleCellsChildBudget = gs.VolumeSampleCells.DefaultChildBudget;
        /// <summary> for tests and the benchmark: what each volume IoU check of the last Apply() did </summary>
        internal List<MeshRepairMetrics.VolumeCheckStats> VolumeCheckStats = new List<MeshRepairMetrics.VolumeCheckStats>();

        // true if a closed part of the input (at least MinComponentDiagonal of the largest connected component) is not in res: judged relative to the
        // largest connected component, not to all triangles (CheckLostComponents does that, and skips a small part next to a far one)
        bool lost_part(DMesh3 input, DMesh3 res)
        {
            if (inputSolid == null || inputSolidOf != input) {
                inputSolid = new MeshRepairMetrics.SolidReference(input);
                inputSolidOf = input;
            }
            var resultSolid = MeshRepairMetrics.SolidReference.Unoriented(res);
            return MeshRepairMetrics.LostComponents(inputSolid, resultSolid.Inside, relativeToLargest: true) > 0;
        }

        // "" if the solid of res agrees with the input's (IoU at least MinVolumeIoU, no component lost), otherwise why not
        string volume_check(DMesh3 input, DMesh3 res, out double iou)
        {
            iou = double.NaN;
            VolumeChecksRun++;
            if (MinVolumeIoU <= 0)
                return "";      // (no volume checks at all)
            // (the input's solid once per Apply(), the result's once for both checks)
            if (inputSolid == null || inputSolidOf != input) {
                inputSolid = new MeshRepairMetrics.SolidReference(input);
                inputSolidOf = input;
            }
            var resultSolid = MeshRepairMetrics.SolidReference.Unoriented(res);
            var stats = new MeshRepairMetrics.VolumeCheckStats();
            VolumeCheckStats.Add(stats);
            if (MinVolumeIoU > 0 && MeshRepairMetrics.VolumeIoUAtLeast(inputSolid, resultSolid, MinVolumeIoU, out iou, stats: stats, useCells: SampleCellsInVolumeCheck, childBudget: SampleCellsChildBudget) == false)
                return VolumeDiffers;
            if (CheckLostComponents && MeshRepairMetrics.LostComponents(inputSolid, resultSolid.Inside) > 0)
                return PartsLost;
            return "";
        }
        MeshRepairMetrics.SolidReference inputSolid;
        DMesh3 inputSolidOf;

        // the wrap simplified within half the offset, without self-intersections; null if that is not clean or certified
        DMesh3 simplify_wrap(DMesh3 wrap, double offset, DMesh3 input)
        {
            if (CancelF())
                return null;
            int subdivisions;
            return SimplifyWithin(wrap, 0.5 * offset, 0.5 * offset, CheckSelfIntersections, CancelF, out subdivisions,
                WrapSimplifyMinQuality, true, WrapSimplifySlabs, 0, null, input);
        }

        /// <summary>
        /// the triangle quality (Reducer.MinCollapseTriangleQuality, 4 sqrt(3) area / sum of squared edge lengths) below
        /// which the simplification of the wrap creates no triangle, unless it replaces a worse one around the collapsed
        /// edge (CollapseQualityAgainstWorst). 0.3 is an isosceles cap of 150 degrees; the earlier 0.05 (about 175 degrees)
        /// left caps above 170 degrees on nearly every wrap (benchmarks/MeshRepairBenchmark README, "Simplification
        /// quality guard")
        /// </summary>
        internal const double WrapSimplifyMinQuality = 0.3;

        /// <summary>
        /// mesh reduced (quadric error up to quadricDistance^2, no collapse that creates a self-intersection), or null
        /// if the result is not clean or not certified within maxDistance of the mesh (one-sided Hausdorff upper
        /// bound, from the result to the mesh). The distance is checked afterwards rather than per collapse
        /// (Reducer.MaxDeviation): on alpha wraps that check took 10 to 30 times longer and changed nothing on most. A
        /// reduction that is not certified is done once more with MaxDeviation (issue #747; 5 of the 800 sample wraps).
        /// </summary>
        internal static DMesh3 SimplifyWithin(DMesh3 mesh, double quadricDistance, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF)
        {
            int subdivisions;
            return SimplifyWithin(mesh, quadricDistance, maxDistance, checkSelfIntersections, cancelF, out subdivisions);
        }

        /// <summary> SimplifyWithin; hausdorffSubdivisions: the subdivisions of the distance check (for tests) </summary>
        internal static DMesh3 SimplifyWithin(DMesh3 mesh, double quadricDistance, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            out int hausdorffSubdivisions, Dictionary<string, long> phases = null)
        {
            return SimplifyWithin(mesh, quadricDistance, maxDistance, checkSelfIntersections, cancelF, out hausdorffSubdivisions,
                WrapSimplifyMinQuality, true, WrapSimplifySlabs, 0, phases);
        }

        /// <summary> SimplifyWithin with the quality guard given, one serial Reducer run (tests: the earlier 0.05 per triangle
        /// leaves the caps that AlphaWrapQualityPass is tested on, the inputs those tests were chosen on) </summary>
        internal static DMesh3 SimplifyWithin(DMesh3 mesh, double quadricDistance, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            out int hausdorffSubdivisions, double minQuality, bool qualityAgainstWorst, Dictionary<string, long> phases = null)
        {
            return SimplifyWithin(mesh, quadricDistance, maxDistance, checkSelfIntersections, cancelF, out hausdorffSubdivisions,
                minQuality, qualityAgainstWorst, 0, 0, phases);
        }

        /// <summary>
        /// the slabs of the wrap simplification (PartitionedReduction; Alpha Wrap 2.0 option A, owner decision 2026-11-08).
        /// Fixed, not the core count: the partition decides the result
        /// </summary>
        internal const int WrapSimplifySlabs = 8;

        /// <summary>
        /// SimplifyWithin on slabs (0: one serial Reducer run) with at most maxThreads threads (0: the platform default; the
        /// result does not depend on it)
        /// </summary>
        internal static DMesh3 SimplifyWithin(DMesh3 mesh, double quadricDistance, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            out int hausdorffSubdivisions, int slabs, int maxThreads)
        {
            return SimplifyWithin(mesh, quadricDistance, maxDistance, checkSelfIntersections, cancelF, out hausdorffSubdivisions,
                WrapSimplifyMinQuality, true, slabs, maxThreads);
        }

        /// <summary> the retry of SimplifyWithin bounds every collapse to this fraction of maxDistance (Reducer.MaxDeviation) </summary>
        internal const double RetryDeviationFraction = 0.8;
        /// <summary> the first retry of SimplifyWithin keeps sample points of every new triangle within this fraction of maxDistance (Reducer.SampleDeviation) </summary>
        internal const double RetrySampleFraction = 0.5;

        internal static DMesh3 SimplifyWithin(DMesh3 mesh, double quadricDistance, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            out int hausdorffSubdivisions, double minQuality, bool qualityAgainstWorst, int slabs, int maxThreads,
            Dictionary<string, long> phases = null, DMesh3 keepClearOf = null, bool retryWithDeviation = true,
            bool retrySampled = true)
        {
            hausdorffSubdivisions = 0;
            Stopwatch timer = Stopwatch.StartNew();
            // (the wrap's input, read only: no collapse creates a triangle that touches it)
            DMeshAABBTree3 clearTree = keepClearOf != null ? new DMeshAABBTree3(keepClearOf, true) : null;
            Action<Reducer> configure = wrap_reducer(clearTree, cancelF, minQuality, qualityAgainstWorst);
            DMesh3 m = reduce(mesh, quadricDistance, slabs, maxThreads, configure, cancelF, phases, "");
            lap(phases, "reduce", timer);
            if (cancelF() || NotCleanReason(m, checkSelfIntersections) != "")
                return null;
            lap(phases, "check", timer);
            bool within = certified_within(m, mesh, maxDistance, cancelF, ref hausdorffSubdivisions);
            lap(phases, "certify", timer);
            if (within || !retryWithDeviation || cancelF())
                return within ? m : null;
            // Not certified: the quadric error bounds the distance of each vertex to the planes it absorbed, not of the
            // triangles to the mesh (on the Thingi10K 800 sample 5 of 800 wraps, 1.2 to 10.7 times maxDistance, at a fold of
            // the wrap and elsewhere; issue 747). First once more with Reducer.SampleDeviation (the new triangles' centroids
            // and edge midpoints within RetrySampleFraction of maxDistance of the mesh: a few nearest-point queries per
            // collapse, about 1.3 times the plain reduction; it certified all 5), then certified again as a whole
            if (retrySampled) {
                var sampleRef = new DMeshAABBTree3(new DMesh3(mesh), true);
                Action<Reducer> sampled = (r) => { configure(r); r.SampleDeviation = RetrySampleFraction * maxDistance; r.SampleReference = sampleRef; };
                m = reduce(mesh, quadricDistance, slabs, maxThreads, sampled, cancelF, phases, "retry/");
                lap(phases, "retry", timer);
                if (cancelF())
                    return null;
                if (NotCleanReason(m, checkSelfIntersections) == "") {
                    within = certified_within(m, mesh, maxDistance, cancelF, ref hausdorffSubdivisions);
                    lap(phases, "retry/certify", timer);
                    if (within || cancelF())
                        return within ? m : null;
                }
            }
            // Still not: with Reducer.MaxDeviation, which certifies every collapse against the mesh (10 to 30 times slower,
            // so only here), then certified again as a whole (the bound with a margin: the certification's own bound cannot
            // prove a distance right at maxDistance)
            // (PartitionedReduction measures every slab and its last pass from the mesh given)
            Action<Reducer> bounded = (r) => { configure(r); r.MaxDeviation = RetryDeviationFraction * maxDistance; };
            m = reduce(mesh, quadricDistance, slabs, maxThreads, bounded, cancelF, phases, "retry2/");
            lap(phases, "retry2", timer);
            if (cancelF() || NotCleanReason(m, checkSelfIntersections) != "")
                return null;
            within = certified_within(m, mesh, maxDistance, cancelF, ref hausdorffSubdivisions);
            lap(phases, "retry2/certify", timer);
            return within ? m : null;
        }

        // the settings of every reduction of a wrap: no contact with keepClear's mesh, no self-intersection, the quality guard
        static Action<Reducer> wrap_reducer(DMeshAABBTree3 clearTree, Func<bool> cancelF, double minQuality, bool qualityAgainstWorst)
        {
            return (r) => {
                r.KeepClearOf = clearTree;
                r.PreventSelfIntersections = true; r.Progress = new ProgressCancel(cancelF);
                r.AreaWeightedQuadrics = false; r.MinCollapseTriangleQuality = minQuality; r.CollapseQualityAgainstWorst = qualityAgainstWorst;
                r.EdgeFlipTolerance = Math.Cos(60 * MathUtil.Deg2Rad);
            };
        }

        /// <summary>
        /// simplified (a certified reduction of wrap) reduced further until it has at most target triangles or no allowed collapse is
        /// left: one serial Reducer from simplified with MaxDeviation (RetryDeviationFraction of maxDistance, a certified bound on every
        /// collapse) measured from wrap, and the exact checks of SimplifyWithin (no self-intersection, no contact with keepClearOf).
        /// The result is checked clean and certified within maxDistance of wrap as a whole (one-sided Hausdorff upper bound); null
        /// if it is not, or on cancel (the caller keeps simplified). The count may stay above target
        /// </summary>
        internal static DMesh3 ReduceToTarget(DMesh3 simplified, DMesh3 wrap, int target, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF, DMesh3 keepClearOf)
        {
            DMesh3 m = new DMesh3(simplified);
            var reducer = new Reducer(m);
            wrap_reducer(keepClearOf != null ? new DMeshAABBTree3(keepClearOf, true) : null, cancelF, WrapSimplifyMinQuality, true)(reducer);
            reducer.MaxDeviation = RetryDeviationFraction * maxDistance;
            reducer.SetDeviationReference(new DMeshAABBTree3(wrap, true));
            reducer.ReduceToTriangleCount(target);
            m = new DMesh3(m, true);
            if (cancelF() || NotCleanReason(m, checkSelfIntersections) != "")
                return null;
            int subdivisions = 0;
            return certified_within(m, wrap, maxDistance, cancelF, ref subdivisions) ? m : null;
        }

        // a copy of mesh reduced to the quadric error quadricDistance^2 (PartitionedReduction on slabs, or one Reducer)
        static DMesh3 reduce(DMesh3 mesh, double quadricDistance, int slabs, int maxThreads, Action<Reducer> configure, Func<bool> cancelF,
            Dictionary<string, long> phases, string prefix)
        {
            DMesh3 m = new DMesh3(mesh);
            if (slabs > 0) {
                var partitioned = new PartitionedReduction() { Slabs = slabs, MaxThreads = maxThreads, Configure = configure, CancelF = cancelF };
                m = partitioned.Reduce(m, quadricDistance * quadricDistance);
                // (benchmarks: the parts of the reduction; the rounds run the slabs in parallel, setup and the last pass are serial)
                if (phases != null) {
                    phases[prefix + "reduce/setup"] = partitioned.SetupMs;
                    phases[prefix + "reduce/rounds"] = partitioned.RegionMs;
                    phases[prefix + "reduce/last"] = partitioned.LastMs;
                }
            } else {
                Reducer reducer = new Reducer(m);
                configure(reducer);
                reducer.ReduceToMaxQuadricError(quadricDistance * quadricDistance);
                m = new DMesh3(m, true);
            }
            return m;
        }

        // the smallest and largest triangle angle (degrees) and the share of triangles with an angle above 170 degrees
        /// <summary> the boundary loops filled before the alpha wrap (RobustRepairWrapHoleFill) in the last Apply() that wrapped </summary>
        internal int WrapFilledLoops;
        /// <summary> the boundary loops short enough to be filled (some may be skipped: invalid, or a fill larger than half its component) </summary>
        internal int WrapFillCandidateLoops;
        /// <summary> (tests) the longest boundary loop that the wrap's hole fill closes, in alphas of the wrap (RobustRepairWrapHoleFill.MaxLoopAlphas); 0 = no fill </summary>
        internal double WrapFillLoopAlphas = RobustRepairWrapHoleFill.MaxLoopAlphas;
        /// <summary> (tests) the thin-shell check of the wrap is off </summary>
        internal bool NoThinShellCheck;

        /// <summary> ResultReason when the alpha wrap is a thin shell around the input's surface, which has a solid (volume of the reference more than twice the wrap's): holes wider than alpha that the fill did not close let the ball pass, and the wrap covers both sides of the surface. Apply() is false, the shell is in Mesh </summary>
        public const string ThinShellWrap = "the wrap is a thin shell around the surface (holes wider than alpha: both sides of the surface are wrapped, a thin shell, not the solid)";
        /// <summary> the shell test: the volume of the wrap is at most this many times area * offset (a wrap of both sides of a sheet has volume = area * offset: 2 * offset thick, the area counted on both sides; a solid has the volume of its size) </summary>
        internal const double ThinShellVolumeFactor = 2;

        // The wrap is a thin shell around an input that has a solid (#1575): its volume is at most ThinShellVolumeFactor * area * offset (both sides of a sheet
        // are wrapped), the wrapped surface still has a boundary (a closed one is a solid, a flattened one a solid of no thickness, whose wrap is its wrap), and
        // the reference solid has more than twice the volume of the wrap. A wrap encloses its input: it has the volume of the solid and more, so that this cannot
        // hold for a repair, and a small solid (a few offsets across, thin by the first test) or a sheet (no solid) is not taken for a shell. The volume of the
        // reference is taken about the center of its bounding box (an open reference has no volume of its own: a large hole counts as a cone to the center)
        bool is_thin_shell(DMesh3 wrapped, double offset, DMesh3 wrapFrom, DMesh3 reference)
        {
            if (NoThinShellCheck || !(offset > 0) || wrapFrom.IsClosed() || reference.TriangleCount == 0)
                return false;
            Vector2d va = MeshMeasurements.VolumeArea(wrapped, wrapped.TriangleIndices(), wrapped.GetVertex);
            if (!(va.y > 0) || !(Math.Abs(va.x) <= ThinShellVolumeFactor * va.y * offset))
                return false;
            Vector3d center = MeshRepairMetrics.TriangleBounds(reference).Center;
            double volume = MeshMeasurements.VolumeArea(reference, reference.TriangleIndices(), (vid) => reference.GetVertex(vid) - center).x;
            return Math.Abs(volume) > 2 * Math.Abs(va.x);
        }

        internal static void angle_stats(DMesh3 m, out double minAngle, out double maxAngle, out double shareAbove170)
        {
            minAngle = double.MaxValue; maxAngle = 0;
            int above = 0, n = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in m.TriangleIndices()) {
                m.GetTriVertices(t, ref a, ref b, ref c);
                double A = corner(a, b, c), B = corner(b, c, a), C = corner(c, a, b);
                minAngle = Math.Min(minAngle, Math.Min(A, Math.Min(B, C)));
                double mx = Math.Max(A, Math.Max(B, C));
                maxAngle = Math.Max(maxAngle, mx);
                if (mx > 170) above++;
                n++;
            }
            if (n == 0) { minAngle = maxAngle = shareAbove170 = double.NaN; return; }
            shareAbove170 = (double)above / n;
        }

        // the angle at p between the edges to q and r (degrees; atan2, accurate also near 0 and 180)
        static double corner(Vector3d p, Vector3d q, Vector3d r)
        {
            Vector3d u = q - p, v = r - p;
            return Math.Atan2(u.Cross(v).Length, u.Dot(v)) * MathUtil.Rad2Deg;
        }

        /// <summary>
        /// the settings of the quality pass in the alpha wrap fallback (variant G1 of the 7b benchmark,
        /// docs/design/ALPHA_WRAP_QUALITY.md): the normal-deviation guard with 0.1 degree slack per operation, cap
        /// flips above 150 degrees, cap splits above 160 degrees
        /// </summary>
        internal static void DefaultWrapQuality(AlphaWrapQualityPass p)
        {
            p.KeepNormalDeviation = true;
            p.NormalDeviationSlackDegrees = 0.1;
            p.CapAngleDegrees = 150;
            p.SplitCapsAboveDegrees = 160;
        }

        /// <summary>
        /// the simplified wrap with a better triangle quality (AlphaWrapQualityPass), or null if the pass changed
        /// nothing, was cancelled, or its result is not clean.
        /// simplified must be certified within maxDistance of wrap (SimplifyWithin does that). The result is then
        /// certified within maxDistance by construction, without a second Hausdorff check: every operation of the pass
        /// certifies each of its new triangles, exactly as it puts them into the mesh, within 0.95 maxDistance of wrap
        /// (AlphaWrapQualityPass.accept, TriangleDeviationBounds.IsWithin), and the triangles it does not touch keep the
        /// certificate of simplified (AlphaWrapQualityPassTests.ImproveWrapQuality_CertifiedByConstruction checks it). If
        /// configure widens the operations' tolerance beyond that, the Hausdorff check decides as before. (After a reduction to a
        /// target with WrapTargetMaxDistance above 0.5 the untouched triangles keep the larger distance of ReduceToTarget, the new ones are
        /// held to 0.95 of the half offset passed here: the result is within the larger of the two.)
        /// </summary>
        internal static DMesh3 ImproveWrapQuality(DMesh3 simplified, DMesh3 wrap, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            Action<AlphaWrapQualityPass> configure = null, Dictionary<string, long> phases = null)
        {
            bool hausdorffChecked;
            return ImproveWrapQuality(simplified, wrap, maxDistance, checkSelfIntersections, cancelF, configure, out hausdorffChecked, phases);
        }

        /// <summary> ImproveWrapQuality; hausdorffChecked: whether the final Hausdorff check ran (tests) </summary>
        internal static DMesh3 ImproveWrapQuality(DMesh3 simplified, DMesh3 wrap, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            Action<AlphaWrapQualityPass> configure, out bool hausdorffChecked, Dictionary<string, long> phases = null)
        {
            hausdorffChecked = false;
            Stopwatch timer = Stopwatch.StartNew();
            // (the operations within 0.95 of maxDistance, by default: a margin below the distance that the result is
            // certified within; it also lets the Hausdorff check decide when a configuration widens the tolerance)
            var pass = new AlphaWrapQualityPass(new DMesh3(simplified), wrap, 0.95 * maxDistance) { CancelF = cancelF };
            configure?.Invoke(pass);
            // (the intersections are always checked, as by the simplification: an intersecting result is rejected below)
            bool computed = pass.Compute();
            lap(phases, "pass", timer);
            if (computed == false || pass.Flips + pass.Moves + pass.Splits + pass.Collapses == 0)
                return null;
            DMesh3 m = new DMesh3(pass.Mesh, true);
            if (cancelF() || NotCleanReason(m, checkSelfIntersections) != "")
                return null;
            lap(phases, "check", timer);
            // (a configuration that widened the operations' tolerance beyond 0.95 maxDistance: no certificate by
            // construction, the Hausdorff check decides)
            if (!(pass.Tolerance <= 0.95 * maxDistance)) {
                hausdorffChecked = true;
                int subdivisions = 0;
                bool within = certified_within(m, wrap, maxDistance, cancelF, ref subdivisions);
                lap(phases, "certify", timer);
                return within ? m : null;
            }
            return m;
        }

        // (benchmarks) adds the time since the last lap to phases[name], if phases is given
        static void lap(Dictionary<string, long> phases, string name, Stopwatch timer)
        {
            if (phases == null)
                return;
            phases.TryGetValue(name, out long ms);
            phases[name] = ms + timer.ElapsedMilliseconds;
            timer.Restart();
        }

        // is m certified within maxDistance of mesh (one-sided Hausdorff upper bound, from m to mesh)?
        static bool certified_within(DMesh3 m, DMesh3 mesh, double maxDistance, Func<bool> cancelF, ref int hausdorffSubdivisions)
        {
            // only the upper bound has to stay below maxDistance: a coarse tolerance first, a finer one if the
            // distance is close to maxDistance. With the patch bound: the large triangles of the result over the
            // many small, nearly flat ones of the mesh need few subdivisions (on the alpha wraps of the repair benchmark the check took 16 times less time)
            var spatial = new DMeshAABBTree3(mesh, true);
            foreach (double tol in new double[] { 0.1, 0.01 }) {
                var hd = new MeshHausdorffDistance(m, mesh) { Symmetric = false, SpatialB = spatial, Tolerance = tol * maxDistance, CancelF = cancelF, PatchBound = true };
                bool computed = hd.Compute();
                hausdorffSubdivisions += hd.Subdivisions;
                if (computed == false || !(hd.LowerBound <= maxDistance))
                    return false;
                if (hd.UpperBound <= maxDistance)
                    return true;
            }
            return false;
        }

        // the solid of the mesh with its self-intersections resolved exactly, or null (ExactResolveReason says why)
        // (largestScale: the cleanups relative to the diagonal of the largest component, and a resolve clean but for the
        // degenerate area of NotCleanReason kept as scaleOnly)
        DMesh3 resolve_exactly(DMesh3 from, DMesh3 input, bool orientByInput, bool largestScale = false)
        {
            var timer = Stopwatch.StartNew();
            // components far apart (farParts): the cleanups are relative to the largest component from the first attempt on (they
            // collapsed or eroded the parts that are small next to the diagonal of all triangles, with Apply() true)
            bool relativeScale = largestScale || farParts;
            // inside-out parts flipped, so that the solid is where the winding number is positive (guessed from
            // the nesting of the components, or from the input's solid)
            int flipped = 0;
            DMesh3 m = orientByInput ? oriented_by_reference(from, input, out flipped) : WindingContouring.OrientedCopy(from);
            add_phase("exact.orient", timer.ElapsedMilliseconds); timer.Restart();
            if (orientByInput && flipped == 0)
                return null;        // the same orientation as before: the same result (ExactResolveReason kept)
            bool closed = ExactMeshBoolean.IsWindingClosed(m);
            add_phase("exact.closed", timer.ElapsedMilliseconds); timer.Restart();
            if (closed == false) {
                ExactResolveReason = "not closed (winding number)";
                // (a scale cannot change that: the retry relative to the largest component is not tried for it)
                notClosedFrom = from;
                return null;
            }
            if (ExactResolveMaxPairs > 0) {
                if (MeshRepairMetrics.SelfIntersectionsAtMost(m, ExactResolveMaxPairs) > ExactResolveMaxPairs) {
                    ExactResolveReason = "too many self-intersections";
                    return null;
                }
            }
            for (int round = 0; round < ExactResolveRounds; ++round) {
                if (CancelF())
                    return null;
                timer.Restart();
                var resolve = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union) {
                    SafeRounding = SafeRounding, RoundingMaxDisplacement = RoundingMaxDisplacement, RoundingUlpFactor = RoundingUlpFactor,
                    RoundingAbsoluteTolerance = RoundingAbsoluteTolerance, StrictRounding = StrictRounding,
                    RoundingMaxPairTests = RoundingMaxPairTests, RoundingPairTestsDefectScale = RoundingPairTestsDefectScale,
                    RoundingPairTestsMaxGrowth = RoundingPairTestsMaxGrowth,
                    // (every round's mesh is closed: the first by the IsWindingClosed check above, a later one because the round
                    // before continues only with a result whose NotCleanReason, without the self-intersection check, is empty. The
                    // opt-in is kept as a guard, as before the default of AllowOpenMeshes changed)
                    AllowOpenMeshes = true
                };
                DMesh3 res = resolve.Compute() ? resolve.Result : null;
                ExactResolveRounding = resolve.Rounding;
                SeparatedRounding = null;
                add_phase("exact.resolve", timer.ElapsedMilliseconds); timer.Restart();
                ExactResolveRoundsUsed++;
                // (a rounding that kept components only because their removal would have emptied a thin input's result,
                // #985: not clean, as an empty result was before; the wrap fallback takes it, as it did then)
                if (res == null || res.TriangleCount == 0 || (resolve.Rounding != null && resolve.Rounding.KeptWhenEmptied > 0)) {
                    ExactResolveReason = resolve.Status == ExactMeshBoolean.BooleanStatus.RoundingNotClean
                        ? "rounding not clean (StrictRounding)" : "resolve failed";
                    return null;
                }
                // slivers where intersection curves nearly meet: collapsed or flipped by the cleanup
                string reason = NotCleanReason(res, false);
                // the safe rounding's count of intersections holds only for the mesh it returned
                bool editedAfterRounding = false;
                if (reason == "degenerate triangles") {
                    editedAfterRounding = true;
                    // (relative to the largest component, as the safe rounding, where the diagonal of all triangles would
                    // collapse real surface: a small part far from the rest must not make every other triangle
                    // degenerate; elsewhere relative to all triangles, as NotCleanReason, as before)
                    double scale = relativeScale ? MeshRepairMetrics.LargestComponentDiagonal(res) : MeshRepairMetrics.TriangleBounds(res).DiagonalLength;
                    new MeshPostRepairCleanup(res) { ReferenceDiagonal = relativeScale ? scale : double.NaN }.Apply();
                    res = new DMesh3(res, true);
                    // the cleanup keeps slivers whose collapse would distort or fold a neighbour: collapsed anyway,
                    // the self-intersections that can create are found below and resolved by the next round (so
                    // only when they are checked)
                    if (CheckSelfIntersections && NotCleanReason(res, false) == "degenerate triangles"
                        && collapse_slivers(res, 1e-4, 5, scale > 0 && scale < double.PositiveInfinity ? scale : MeshRepairMetrics.TriangleBounds(res).DiagonalLength) > 0)
                        res = new DMesh3(res, true);
                }
                // where solids touch (at a vertex or along an edge) Resolve gives manifold sheets with duplicated
                // vertices, which touch: separate them minimally
                var separated = new List<int>();
                SeparateTouchingVertices(res, SeparateFraction, separated);
                // (the separation can make new slivers and intersections: repaired again; without moves the safe
                // rounding's counts hold, there were no touching vertices)
                int si = -1;
                if (SafeRounding) {
                    var first = resolve.Rounding;
                    int edits = 0;
                    if (separated.Count == 0) {
                        // (the cleanup and CollapseSlivers above can add intersections: then counted below, as the
                        // count of MeshRepairMetrics)
                        if (editedAfterRounding == false)
                            si = first.RemainingIntersections;
                    } else {
                        // (all of the mesh: the edits at the former contacts, which the safe rounding must not
                        // make, are possible now)
                        var rounding = new MeshRoundingRepair(res) {
                            MaxDisplacement = RoundingMaxDisplacement, UlpFactor = StrictRounding ? 0 : RoundingUlpFactor,
                            AbsoluteTolerance = StrictRounding ? 0 : RoundingAbsoluteTolerance, MaxPairTests = RoundingMaxPairTests,
                            PairTestsDefectScale = RoundingPairTestsDefectScale, PairTestsMaxGrowth = RoundingPairTestsMaxGrowth
                        };
                        rounding.Apply();
                        SeparatedRounding = rounding;
                        edits = rounding.Collapses + rounding.Flips + rounding.Moves + rounding.Zips + rounding.VertexRemovals + rounding.RemovedTriangles;
                        if (edits > 0)
                            res = new DMesh3(res, true);
                        si = rounding.RemainingIntersections;
                    }
                    ExactRoundingEdits.Add(new Index2i(first.Collapses + first.Flips + first.Moves + first.Zips + first.VertexRemovals + first.RemovedTriangles, edits));
                }
                reason = NotCleanReason(res, false);
                // (clean but for the degenerate area of NotCleanReason: the rest of the checks as for a clean one)
                bool onlyScale = relativeScale && reason == "degenerate triangles" && not_clean_reason_at_scale(res) == "";
                if (onlyScale)
                    reason = "";
                if (reason == "" && CheckSelfIntersections) {
                    if (si < 0)
                        si = MeshRepairMetrics.SelfIntersections(res);
                    ExactResolveSelfIntersections.Add(si);
                    if (si > 0)
                        reason = "self-intersections";
                }
                add_phase("exact.check", timer.ElapsedMilliseconds); timer.Restart();
                if (reason == "self-intersections") {
                    // from rounding the new vertices (or the cleanup): tiny, resolved by the next round
                    ExactResolveReason = reason;
                    m = res;
                    continue;
                }
                if (reason != "") {
                    ExactResolveReason = reason;
                    return null;
                }
                {
                    double iou;
                    string differs = volume_check(input, res, out iou);
                    add_phase("exact.volume", timer.ElapsedMilliseconds);
                    if (differs != "") {
                        ExactResolveReason = differs;
                        return null;
                    }
                    // (a rejected result must not leave its IoU: checked first)
                    if (topology_changed(from, res) && RejectTopologyChange) {
                        ExactResolveReason = "topology changed (genus " + GenusBefore.ToString(CultureInfo.InvariantCulture)
                            + " -> " + GenusAfter.ToString(CultureInfo.InvariantCulture) + ")";
                        return null;
                    }
                    if (MinVolumeIoU > 0)
                        RepairVolumeIoU = iou;
                }
                // components far apart compared to their size: the cleanups relative to the diagonal of all triangles may have
                // collapsed a part, which the IoU and the box of all triangles do not see: not accepted, the retry relative to the
                // largest component follows
                if (largestScale == false && lostPartInput != null && BypassEarlyLostPartChecks == false && lost_part(input, res)) {
                    ExactResolveReason = PartsLostFarReason;
                    lostFarRejected = true;
                    return null;
                }
                if (onlyScale) {
                    // (the first one: the repair result's before the input's)
                    if (scaleOnly == null) {
                        scaleOnly = res;
                        scaleOnlyFromInput = from == input;
                    }
                    ExactResolveReason = ScaleDisparity;
                    return null;
                }
                ExactResolveReason = "";
                return res;
            }
            return null;
        }

        // sets TopologyChanged, GenusBefore and GenusAfter for the exact resolve of from into res (judged only if from
        // is closed and has no bowtie vertices); returns TopologyChanged
        bool topology_changed(DMesh3 from, DMesh3 res)
        {
            var before = MeshTopologySummary.Compute(from);
            if (before.BoundaryEdges > 0 || before.BowtieVertices > 0) {
                TopologyChanged = false;
                GenusBefore = GenusAfter = double.NaN;
                return false;
            }
            var after = MeshTopologySummary.Compute(res);
            GenusBefore = before.Genus;
            GenusAfter = after.Genus;
            // a union of several parts changes the components and with them the genus by construction (a ring of
            // overlapping parts, parts merged or split): judged only where the components are kept
            TopologyChanged = before.Components == after.Components && GenusBefore != GenusAfter;
            return TopologyChanged;
        }

        // the alpha and offset of the wrap of a small input: alpha from the median length of its edges (those that are not tiny against the
        // diagonal), if that is above the default diag / WrapRelativeAlpha; 0 and 0 (the default alpha) otherwise
        void small_input_wrap(DMesh3 mesh, out double alpha, out double offset)
        {
            alpha = offset = 0;
            if (!(WrapRelativeAlpha > 0) || !(WrapRelativeOffset > 0) || !(SmallInputWrapEdgeDivisor > 0))
                return;
            double diag = mesh.GetBounds().DiagonalLength;
            if (!(diag > 0) || double.IsInfinity(diag))
                return;
            var lengths = new List<double>();
            foreach (int e in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                double len = mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
                if (len > 1e-6 * diag)
                    lengths.Add(len);
            }
            if (lengths.Count == 0)
                return;
            lengths.Sort();
            double wanted = lengths[lengths.Count / 2] / SmallInputWrapEdgeDivisor;
            if (!(wanted > diag / WrapRelativeAlpha) || double.IsInfinity(wanted))
                return;
            alpha = wanted;
            offset = wanted * (WrapRelativeAlpha / WrapRelativeOffset);
        }

        /// <summary>
        /// Compacted copy of mesh with inside-out parts flipped (WindingContouring.OrientedCopy), then each
        /// closed component oriented by the solid of reference (where its generalized winding number is
        /// > 0.5), sampled just off both sides of its triangles: reversed if the reference has its solid on
        /// the outer side and none on the inner (OrientedCopy guesses a cavity or an inverted part from the
        /// nesting alone, eg makes a part nested in another one a cavity), and turned outwards if the
        /// reference is solid on both sides (an inner shell of a solid, not a cavity).
        /// </summary>
        public static DMesh3 OrientedByReference(DMesh3 mesh, DMesh3 reference)
        {
            int flipped;
            return oriented_by_reference(mesh, reference, out flipped);
        }
        // flipped: components reversed against WindingContouring.OrientedCopy
        static DMesh3 oriented_by_reference(DMesh3 mesh, DMesh3 reference, out int flipped)
        {
            flipped = 0;
            DMesh3 m = WindingContouring.OrientedCopy(mesh);
            if (reference == null || reference.TriangleCount == 0)
                return m;
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            var closedComps = comps.Components.Select(c => c.Indices).Where(tris => tris.All(t => {
                Index3i te = m.GetTriEdges(t);
                return !m.IsBoundaryEdge(te.a) && !m.IsBoundaryEdge(te.b) && !m.IsBoundaryEdge(te.c);
            })).ToList();
            if (closedComps.Count == 0)
                return m;
            var tree = new DMeshAABBTree3(WindingContouring.OrientedCopy(reference), true);
            tree.FastWindingNumber(Vector3d.Zero);     // cache before parallel use
            // up to 200 samples per component, at most about 50000 in all (assemblies with many parts), at least 8
            int perComp = Math.Max(8, Math.Min(200, 50000 / closedComps.Count));
            bool[] flip = new bool[closedComps.Count];
            gParallel.ForEach(Interval1i.Range(closedComps.Count), (ci) => {
                int[] tris = closedComps[ci];
                // area of the sampled triangles with the reference solid only on the inner side, only on the
                // outer side, on both
                double inner = 0, outer = 0, both = 0, total = 0;
                int samples = Math.Min(perComp, tris.Length);
                for (int k = 0; k < samples; ++k) {
                    int tid = tris[(int)((long)k * tris.Length / samples)];
                    Vector3d n, c;
                    double area;
                    m.GetTriInfo(tid, out n, out area, out c);
                    if (area <= 0 || n.IsNormalized == false)
                        continue;
                    double eps = 0.01 * Math.Sqrt(area);
                    bool sIn = tree.FastWindingNumber(c - eps * n) > 0.5, sOut = tree.FastWindingNumber(c + eps * n) > 0.5;
                    total += area;
                    if (sIn && sOut) both += area;
                    else if (sIn) inner += area;
                    else if (sOut) outer += area;
                }
                if (outer > inner && outer > 0.5 * total)
                    flip[ci] = true;
                else if (both > 0.5 * total && inner < 0.25 * total && outer < 0.25 * total)
                    flip[ci] = MeshMeasurements.VolumeArea(m, tris, m.GetVertex).x < 0;
            });
            for (int ci = 0; ci < closedComps.Count; ++ci) {
                if (flip[ci] == false)
                    continue;
                foreach (int tid in closedComps[ci])
                    m.ReverseTriOrientation(tid);
                flipped++;
            }
            return m;
        }

        /// <summary>
        /// Collapses the triangles below the degenerate area of NotCleanReason (1e-12 * diagonal^2): for each, its
        /// shortest edge that is shorter than maxCollapse * diagonal and whose collapse keeps the mesh valid (link
        /// condition), the vertices moved to the midpoint; otherwise an edge flipped, the longest first (caps).
        /// Closed components with a sliver and a total area below 1e-8 * diagonal^2 are removed (also a small but
        /// real separate part, about 1e-4 * diagonal across). Unlike MeshPostRepairCleanup it does not avoid
        /// distorting or folding the neighbours: they are tiny (the slivers come from intersection points that
        /// nearly coincide), so the caller has to check for (and resolve) self-intersections afterwards.
        /// Returns the triangles collapsed, flipped or removed.
        /// </summary>
        public static int CollapseSlivers(DMesh3 mesh, double maxCollapse = 1e-4, int maxPasses = 5)
        {
            return collapse_slivers(mesh, maxCollapse, maxPasses, double.NaN);
        }

        // CollapseSlivers relative to the diagonal diag (MeshRobustRepair: that of the largest connected component; NaN: that of
        // the bounds of the triangles, taken after the scaling below, as the diagonal itself overflows above about 1e154)
        static int collapse_slivers(DMesh3 mesh, double maxCollapse, int maxPasses, double diag)
        {
            // outside an extent of [2^-100, 2^100] the squares of the lengths and areas overflow or underflow: work on
            // the mesh scaled by a power of two to an extent of about 1 (exact) and scale it back
            double s = MeshRepairMetrics.unit_scale_for_mesh(mesh);
            if (s == 1)
                return collapse_slivers_unit(mesh, maxCollapse, maxPasses, double.IsNaN(diag) ? MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength : diag);
            MeshRepairMetrics.ScaleVertices(mesh, s);
            try {
                return collapse_slivers_unit(mesh, maxCollapse, maxPasses, double.IsNaN(diag) ? MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength : s * diag);
            } finally {
                MeshRepairMetrics.ScaleVertices(mesh, 1 / s);
            }
        }

        static int collapse_slivers_unit(DMesh3 mesh, double maxCollapse, int maxPasses, double diag)
        {
            double areaEps = 1e-12 * diag * diag, maxLength = maxCollapse * diag;
            int fixedCount = 0;
            for (int pass = 0; pass < maxPasses; ++pass) {
                int n = 0;
                foreach (int tid in mesh.TriangleIndices().ToArray()) {
                    if (mesh.IsTriangle(tid) == false || mesh.GetTriArea(tid) >= areaEps)
                        continue;
                    Index3i tv = mesh.GetTriangle(tid);
                    Index3i te = mesh.GetTriEdges(tid);
                    int[] order = { 0, 1, 2 };
                    double[] len = new double[3];
                    for (int k = 0; k < 3; ++k)
                        len[k] = mesh.GetVertex(tv[k]).Distance(mesh.GetVertex(tv[(k + 1) % 3]));
                    Array.Sort((double[])len.Clone(), order);
                    bool done = false;
                    foreach (int k in order) {
                        if (len[k] >= maxLength)
                            break;
                        Index2i ev = mesh.GetEdgeV(te[k]);
                        Vector3d mid = 0.5 * (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b));
                        DMesh3.EdgeCollapseInfo info;
                        if (mesh.CollapseEdge(ev.a, ev.b, out info) == MeshResult.Ok) {
                            mesh.SetVertex(info.vKept, mid);
                            done = true;
                            break;
                        }
                    }
                    // otherwise flip an edge (longest first): caps, and needles whose ends have more than two common
                    // neighbours (the collapse would not be manifold) until the next pass
                    for (int j = 2; j >= 0 && done == false; --j) {
                        DMesh3.EdgeFlipInfo flip;
                        done = mesh.FlipEdge(te[order[j]], out flip) == MeshResult.Ok;
                    }
                    if (done)
                        n++;
                }
                // tiny closed components with slivers (eg a flat tetrahedron where intersection points nearly
                // coincide): removed, they enclose no volume to speak of
                n += remove_tiny_components(mesh, areaEps);
                fixedCount += n;
                if (n == 0)
                    break;
            }
            return fixedCount;
        }

        static int remove_tiny_components(DMesh3 mesh, double areaEps)
        {
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            int removed = 0;
            foreach (var comp in comps.Components) {
                // closed, a sliver, and in all below 10^4 times the degenerate area (1e-8 * diagonal^2); an open
                // component is kept: it may close the rest by position (sheets that touch with their own vertices)
                if (comp.Indices.Any(tid => mesh.GetTriArea(tid) < areaEps) == false || comp.Indices.Sum(tid => mesh.GetTriArea(tid)) >= 1e4 * areaEps)
                    continue;
                if (comp.Indices.Any(tid => { Index3i te = mesh.GetTriEdges(tid); return mesh.IsBoundaryEdge(te.a) || mesh.IsBoundaryEdge(te.b) || mesh.IsBoundaryEdge(te.c); }))
                    continue;
                foreach (int tid in comp.Indices)
                    mesh.RemoveTriangle(tid, true, false);
                removed += comp.Indices.Length;
            }
            return removed;
        }

        /// <summary> how far SeparateTouchingVertices moves a vertex, relative to the distance to the centroid of its neighbours </summary>
        public double SeparateFraction = 1e-4;

        /// <summary>
        /// Vertices at the same position (eg from splitting a non-manifold vertex or edge into manifold sheets)
        /// make their sheets touch there. Each is moved by fraction of the way towards the centroid of its
        /// neighbours, so the sheets separate. Returns the number of vertices moved.
        /// </summary>
        public static int SeparateTouchingVertices(DMesh3 mesh, double fraction)
        {
            return SeparateTouchingVertices(mesh, fraction, null);
        }

        /// <summary> as SeparateTouchingVertices(mesh, fraction), and adds the vertices moved to moved (if not null) </summary>
        public static int SeparateTouchingVertices(DMesh3 mesh, double fraction, List<int> moved)
        {
            var at = new Dictionary<Vector3d, List<int>>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                List<int> list;
                if (at.TryGetValue(v, out list) == false)
                    at[v] = list = new List<int>();
                list.Add(vid);
            }
            var moves = new List<KeyValuePair<int, Vector3d>>();
            foreach (var kv in at) {
                if (kv.Value.Count < 2)
                    continue;
                foreach (int vid in kv.Value) {
                    Vector3d c = Vector3d.Zero;
                    int n = 0;
                    foreach (int nb in mesh.VtxVerticesItr(vid)) {
                        c += mesh.GetVertex(nb);
                        n++;
                    }
                    if (n > 0)
                        moves.Add(new KeyValuePair<int, Vector3d>(vid, kv.Key + fraction * (c / n - kv.Key)));
                }
            }
            foreach (var m in moves) {
                mesh.SetVertex(m.Key, m.Value);
                moved?.Add(m.Key);
            }
            return moves.Count;
        }

        /// <summary>
        /// "" if the mesh is clean (see class comment), otherwise the first problem found, one of "empty",
        /// "not closed", "bowties", "degenerate triangles" (area &lt; 1e-12 * diagonal^2, of the bounds of the triangles),
        /// "inconsistent orientation" (an interior edge whose two triangles run the same way), "inside-out components",
        /// "self-intersections" (exact; pairs of triangles that only touch count, also at positions where vertices coincide: a boolean result
        /// that touches itself, ExactMeshBoolean.Contacts &gt; 0, gets this reason although ExactMeshBoolean.RoundingClean is true, which
        /// exempts the contacts of the exact result; SolidBooleanPipeline.Report.OnlyContacts tells such a result from one with a crossing).
        /// Useful as a test assertion: NotCleanReason(mesh) == "".
        /// Stops at the first problem, which can hide the others (eg degenerate triangles hide self-intersections):
        /// NotCleanReasons / FindDefects report all of them.
        /// </summary>
        public static string NotCleanReason(DMesh3 mesh, bool checkSelfIntersections = true)
        {
            if (mesh.TriangleCount == 0) return "empty";
            if (mesh.IsClosed() == false) return "not closed";
            if (any_bowtie_vertex(mesh)) return "bowties";
            if (has_degenerate_triangles(mesh)) return "degenerate triangles";
            if (MeshRepairMetrics.InconsistentlyOrientedEdges(mesh) > 0) return "inconsistent orientation";
            if (MeshPostRepairCleanup.FindInvertedComponents(mesh).Count > 0) return "inside-out components";
            if (checkSelfIntersections && MeshRepairMetrics.HasSelfIntersections(mesh)) return "self-intersections";
            return "";
        }

        /// <summary> the checks of NotCleanReason, as flags (FindDefects) </summary>
        [Flags]
        public enum Defects
        {
            None = 0,
            Empty = 1,
            NotClosed = 2,
            Bowties = 4,
            DegenerateTriangles = 8,
            InconsistentOrientation = 16,
            InsideOutComponents = 32,
            SelfIntersections = 64
        }

        /// <summary>
        /// every failed check of NotCleanReason (None if the mesh is clean). An empty mesh is Empty only. Every other
        /// check runs whatever the earlier ones found: inside-out components are searched among the closed
        /// components, self-intersections ignore exactly degenerate triangles. A defect can follow from another one
        /// (the volume of a component with inconsistent orientation, and so inside-out, is not meaningful). Costs
        /// as much as NotCleanReason on a clean mesh; on an unclean one all checks run (with
        /// checkSelfIntersections also the expensive one).
        /// </summary>
        public static Defects FindDefects(DMesh3 mesh, bool checkSelfIntersections = true)
        {
            if (mesh.TriangleCount == 0) return Defects.Empty;
            Defects d = Defects.None;
            if (mesh.IsClosed() == false) d |= Defects.NotClosed;
            if (any_bowtie_vertex(mesh)) d |= Defects.Bowties;
            if (has_degenerate_triangles(mesh)) d |= Defects.DegenerateTriangles;
            if (MeshRepairMetrics.InconsistentlyOrientedEdges(mesh) > 0) d |= Defects.InconsistentOrientation;
            if (MeshPostRepairCleanup.FindInvertedComponents(mesh).Count > 0) d |= Defects.InsideOutComponents;
            if (checkSelfIntersections && MeshRepairMetrics.HasSelfIntersections(mesh)) d |= Defects.SelfIntersections;
            return d;
        }

        /// <summary>
        /// the reasons of NotCleanReason for the defects, in its order, separated by ", " ("" for None; bits that are
        /// no Defects value are ignored): the first one is what NotCleanReason returns
        /// </summary>
        public static string DescribeDefects(Defects defects)
        {
            var reasons = new List<string>();
            foreach (var kv in defect_reasons)
                if ((defects & kv.Key) != 0)
                    reasons.Add(kv.Value);
            return string.Join(", ", reasons);
        }

        /// <summary>
        /// all reasons why the mesh is not clean, eg "degenerate triangles, inside-out components" ("" if it is clean):
        /// DescribeDefects(FindDefects(mesh, checkSelfIntersections)). Starts with NotCleanReason(mesh, checkSelfIntersections).
        /// For a report: to decide on a single reason, compare NotCleanReason (or test the flags of FindDefects),
        /// not this string, which names more than one reason when there are several defects.
        /// </summary>
        public static string NotCleanReasons(DMesh3 mesh, bool checkSelfIntersections = true)
        {
            return DescribeDefects(FindDefects(mesh, checkSelfIntersections));
        }

        static readonly KeyValuePair<Defects, string>[] defect_reasons = {
            new KeyValuePair<Defects, string>(Defects.Empty, "empty"),
            new KeyValuePair<Defects, string>(Defects.NotClosed, "not closed"),
            new KeyValuePair<Defects, string>(Defects.Bowties, "bowties"),
            new KeyValuePair<Defects, string>(Defects.DegenerateTriangles, "degenerate triangles"),
            new KeyValuePair<Defects, string>(Defects.InconsistentOrientation, "inconsistent orientation"),
            new KeyValuePair<Defects, string>(Defects.InsideOutComponents, "inside-out components"),
            new KeyValuePair<Defects, string>(Defects.SelfIntersections, "self-intersections"),
        };

        // the last Apply(): whether the exact resolve ran once more relative to the largest component, and the share of
        // global_scale_share that decided it (benchmarks)
        internal bool ScaleRetry;
        internal double MaxScaleErosionShare;
        // the mesh whose exact resolve found it not closed by the winding number in the last Apply() (null: none); its
        // retry would fail the same way
        DMesh3 notClosedFrom;

        // the largest share, over the connected components, of a component's area in triangles below the degenerate
        // area of NotCleanReason (0 with fewer than two components)
        static double global_scale_share(DMesh3 mesh)
        {
            double diag = MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength;
            double areaEps = 1e-12 * diag * diag;
            int[] componentOf;
            List<AxisAlignedBox3d> boxes = MeshRepairMetrics.ComponentBoxes(mesh, out componentOf);
            if (boxes.Count < 2)
                return 0;
            var total = new double[boxes.Count];
            var below = new double[boxes.Count];
            foreach (int t in mesh.TriangleIndices()) {
                int c = componentOf[mesh.GetTriangle(t).a];
                double a = mesh.GetTriArea(t);
                total[c] += a;
                if (a < areaEps)
                    below[c] += a;
            }
            double share = 0;
            for (int c = 0; c < boxes.Count; ++c)
                if (below[c] > 0)
                    share = Math.Max(share, below[c] / total[c]);
            return share;
        }

        // NotCleanReason without self-intersections, the degenerate area relative to the diagonal of the largest
        // connected component (MeshRepairMetrics.LargestComponentDiagonal), NotCleanReason's if that is not finite
        static string not_clean_reason_at_scale(DMesh3 mesh)
        {
            string reason = NotCleanReason(mesh, false);
            if (reason != "degenerate triangles")
                return reason;
            double scale = MeshRepairMetrics.LargestComponentDiagonal(mesh);
            if (!(scale > 0 && scale < double.PositiveInfinity))
                return reason;
            double areaEps = 1e-12 * scale * scale;
            if (mesh.TriangleIndices().Any(t => mesh.GetTriArea(t) < areaEps)) return "degenerate triangles";
            if (MeshRepairMetrics.InconsistentlyOrientedEdges(mesh) > 0) return "inconsistent orientation";
            if (MeshPostRepairCleanup.FindInvertedComponents(mesh).Count > 0) return "inside-out components";
            return "";
        }

        // MeshIterators.BowtieVertices(mesh).Any(), the vertex ranges in parallel (reads only; the answer is a bool, so
        // it does not depend on the threads)
        static bool any_bowtie_vertex(DMesh3 mesh)
        {
            return any_in_blocks(mesh.MaxVertexID, (i) => mesh.IsVertex(i) && mesh.IsBowtieVertex(i));
        }

        // mesh.TriangleIndices().Any(f), the triangle id ranges in parallel (f must only read the mesh)
        static bool any_triangle(DMesh3 mesh, Func<int, bool> f)
        {
            return any_in_blocks(mesh.MaxTriangleID, (t) => mesh.IsTriangle(t) && f(t));
        }

        // is f(i) true for some i in [0, n)? Blocks of ids in parallel for large n, stopping once one is found
        static bool any_in_blocks(int n, Func<int, bool> f)
        {
            const int Block = 8192;
            if (n <= 2 * Block) {
                for (int i = 0; i < n; ++i)
                    if (f(i)) return true;
                return false;
            }
            int blocks = (n + Block - 1) / Block;
            int found = 0;
            gParallel.ForRange(blocks, () => 0, (b, unused) => {
                if (System.Threading.Volatile.Read(ref found) != 0) return;
                for (int i = b * Block, e = Math.Min(n, i + Block); i < e; ++i)
                    if (f(i)) { System.Threading.Volatile.Write(ref found, 1); return; }
            });
            return found != 0;
        }

        // a triangle with area below 1e-12 * diagonal^2 (of the bounds of the triangles). Outside an extent of
        // [2^-100, 2^100] the test runs on coordinate differences scaled by a power of two to an extent of about 1
        // (exact, MeshRepairMetrics.unit_scale): the area (the length of a cross product, a fourth power of the edges
        // inside) underflowed below edges of about 2^-255, so well-shaped triangles were degenerate; below a diagonal
        // of about 2^-517 the threshold underflowed to 0 and no triangle was, not even an exactly flat one. Differences,
        // not coordinates: a tiny extent far from the origin would overflow when its coordinates are scaled up. 2^k
        // scales give the same answer
        static bool has_degenerate_triangles(DMesh3 mesh)
        {
            AxisAlignedBox3d box = MeshRepairMetrics.TriangleBounds(mesh);
            double s = MeshRepairMetrics.unit_scale(box, -100, 100);
            if (s == 1) {
                double diag = box.DiagonalLength;
                double areaEps = 1e-12 * diag * diag;
                return any_triangle(mesh, t => mesh.GetTriArea(t) < areaEps);
            }
            double d = scaled_difference(box.Max, box.Min, s).Length, eps = 1e-12 * d * d;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                if (0.5 * scaled_difference(b, a, s).Cross(scaled_difference(c, a, s)).Length < eps)
                    return true;
            }
            return false;
        }

        // s * (p - q), exact for a power of two s unless subnormal: the difference first (it cannot overflow where the
        // scaled coordinates would, a tiny extent far from the origin), or, where the difference overflows (an extent
        // above 2^1024), the difference of the scaled coordinates
        static Vector3d scaled_difference(Vector3d p, Vector3d q, double s)
        {
            Vector3d d = p - q;
            if (Math.Abs(d.x) <= double.MaxValue && Math.Abs(d.y) <= double.MaxValue && Math.Abs(d.z) <= double.MaxValue)
                return s * d;
            return s * p - s * q;
        }
    }
}
