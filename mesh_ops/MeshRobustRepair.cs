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
    /// reference; UsedInput). The solidify and the wrap themselves are not checked (they change the geometry by about
    /// a cell or the offset, and lose parts smaller than that).
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
        /// <summary> minimum volume IoU of the repaired mesh with the input's winding number solid (0 = no volume checks, also not CheckLostComponents) </summary>
        public double MinVolumeIoU = 0.99;
        /// <summary>
        /// also a result that lost a connected component of the input (MeshRepairMetrics.LostComponents: a part too small
        /// for the volume IoU, eg a screw of an assembly) is treated like one whose volume differs ("parts of the input lost").
        /// Only with MinVolumeIoU &gt; 0 (0 turns off both). Off by default: on the 430 Thingi10K meshes of
        /// benchmarks/MeshRepairBenchmark it found no lost part, but rejected 3 repairs whose nested closed parts were
        /// oriented differently from the reference (a part inside another taken for a cavity), and the fallbacks from
        /// the open input were worse (one mesh fewer repaired). For inputs with separate small parts (assemblies). The fallbacks after such a rejection (solidify, wrap) are not
        /// checked for it: their ResultReason can be "" although they lost the same part, see FallbackReason and UsedInput.
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
        /// repair after SeparateTouchingVertices (MeshRoundingRepair.MaxPairTests). Default 5M
        /// </summary>
        public long RoundingMaxPairTests = 5000000;
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
        /// both the solidify fallback (eg the grid would be too large) and the alpha wrap failed; Mesh is then not replaced. true otherwise, also for an
        /// unclean solidify result (when the wrap is off or failed): check ResultReason.
        /// </returns>
        public bool Apply()
        {
            lostPartInput = null;
            farParts = false;
            lostFarRejected = false;
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
            UsedSolidify = false;
            ResultReason = "not repaired";
            UsedInput = false;
            UsedAlphaWrap = false;
            AlphaWrapTriangles = 0;
            AlphaWrapSimplified = false;
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
            DMesh3 input = new DMesh3(Mesh);
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
                string differs = volume_check(input, result, out RepairVolumeIoU);
                // components far apart: the repair stage's cleanups (relative to all triangles) may have collapsed a part, which the
                // volume check does not see; the input is resolved instead
                if (differs == "" && lostPartInput != null && BypassEarlyLostPartChecks == false && lost_part(input, result))
                    differs = PartsLostFarReason;
                if (differs != "") {
                    FallbackReason = differs;
                    solidifyFrom = input;
                }
                end_phase("volume");
            } else if (result.TriangleCount == 0) {
                solidifyFrom = input;
            } else if (ExactResolve == false || input.TriangleCount == 0) {
                // (an empty input: the check sends the fallbacks to it, and Apply() stops below, as before)
                unclean_result_volume(input, result, ref solidifyFrom);
            } else {
                // with ExactResolve, the volume of an unclean result is checked only if its exact resolve fails: a
                // successful one replaces UsedInput and RepairVolumeIoU, and solidifyFrom is not used. The check, and
                // so its outputs, are the same as if it had run here
                uncleanVolumePending = true;
            }
            UsedInput = solidifyFrom == input;
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
                        exact = resolve_exactly(result, input, false);
                    // its volume may differ (or a part be lost) only because components are oriented differently from
                    // the input (eg a nested part taken for a cavity): then resolve it again, oriented by the input's
                    // solid, if that orientation differs at all
                    if (exact == null && (cleanButDiffers || ExactResolveReason == VolumeDiffers || ExactResolveReason == PartsLost || ExactResolveReason == PartsLostFarReason) && CancelF() == false)
                        exact = resolve_exactly(result, input, true);
                }
                bool fromResult = exact != null;
                if (exact == null && uncleanVolumePending) {
                    end_phase("exact");
                    unclean_result_volume(input, result, ref solidifyFrom);
                    UsedInput = solidifyFrom == input;
                }
                // then the input itself (a clean result whose volume differs, or an unclean one that lost volume)
                if (exact == null && solidifyFrom != result && CancelF() == false) {
                    string reason = ExactResolveReason;
                    exact = resolve_exactly(solidifyFrom, input, false);
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
                        from = input;
                    MaxScaleErosionShare = global_scale_share(from);
                    if (MaxScaleErosionShare > 0 && from != notClosedFrom) {
                        ScaleRetry = true;
                        string reason = ExactResolveReason;
                        exact = resolve_exactly(from, input, false, true);
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
                AlphaWrap = new MeshAlphaWrap(solidifyFrom) { RelativeAlpha = WrapRelativeAlpha, RelativeOffset = WrapRelativeOffset, CancelF = CancelF };
                AlphaWrap.Compute();
                end_phase("wrap");
                // (PointLimitReached: less detail, same guarantees)
                if ((AlphaWrap.Status == MeshAlphaWrap.WrapStatus.Success || AlphaWrap.Status == MeshAlphaWrap.WrapStatus.PointLimitReached)
                    && AlphaWrap.Result != null && AlphaWrap.Result.TriangleCount > 0) {
                    AlphaWrapTriangles = AlphaWrap.Result.TriangleCount;
                    DMesh3 wrapped = AlphaWrap.Result;
                    if (SimplifyAlphaWrap) {
                        wrapped = simplify_wrap(AlphaWrap.Result, AlphaWrap.UsedOffset, solidifyFrom) ?? AlphaWrap.Result;
                        end_phase("wrap.simplify");
                        if (CancelF())
                            return false;
                    }
                    if (AlphaWrapQualityPass && wrapped != AlphaWrap.Result) {
                        DMesh3 improved = ImproveWrapQuality(wrapped, AlphaWrap.Result, 0.5 * AlphaWrap.UsedOffset, CheckSelfIntersections, CancelF,
                            (p) => { DefaultWrapQuality(p); p.KeepClearOf = new DMeshAABBTree3(solidifyFrom, true); ConfigureWrapQuality?.Invoke(p); });
                        end_phase("wrap.quality");
                        if (CancelF())
                            return false;
                        if (improved != null) {
                            wrapped = improved;
                            AlphaWrapQualityImproved = true;
                        }
                    }
                    AlphaWrapSimplified = wrapped != AlphaWrap.Result;
                    Mesh = wrapped;
                    UsedAlphaWrap = true;
                    angle_stats(wrapped, out AlphaWrapMinAngleDegrees, out AlphaWrapMaxAngleDegrees, out AlphaWrapShareAbove170);
                    ResultReason = "";
                    return true;
                }
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
        const string PartsLostFarReason = "a part of the input lost (components far apart compared to their size)";
        /// <summary> ResultReason of an Apply() that is false because a closed part of the input is not in the result, where components lie far apart compared to their size </summary>
        public const string LostFarPart = "scale disparity: a part of the input is lost (components far apart compared to their size)";
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
        /// (Reducer.MaxDeviation): on alpha wraps that check took 10 to 30 times longer and changed nothing.
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

        internal static DMesh3 SimplifyWithin(DMesh3 mesh, double quadricDistance, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            out int hausdorffSubdivisions, double minQuality, bool qualityAgainstWorst, int slabs, int maxThreads,
            Dictionary<string, long> phases = null, DMesh3 keepClearOf = null)
        {
            hausdorffSubdivisions = 0;
            Stopwatch timer = Stopwatch.StartNew();
            DMesh3 m = new DMesh3(mesh);
            // (the wrap's input, read only: no collapse creates a triangle that touches it)
            DMeshAABBTree3 clearTree = keepClearOf != null ? new DMeshAABBTree3(keepClearOf, true) : null;
            Action<Reducer> configure = (r) => {
                r.KeepClearOf = clearTree;
                r.PreventSelfIntersections = true; r.Progress = new ProgressCancel(cancelF);
                r.AreaWeightedQuadrics = false; r.MinCollapseTriangleQuality = minQuality; r.CollapseQualityAgainstWorst = qualityAgainstWorst;
                r.EdgeFlipTolerance = Math.Cos(60 * MathUtil.Deg2Rad);
            };
            if (slabs > 0) {
                m = new PartitionedReduction() { Slabs = slabs, MaxThreads = maxThreads, Configure = configure, CancelF = cancelF }.Reduce(m, quadricDistance * quadricDistance);
            } else {
                Reducer reducer = new Reducer(m);
                configure(reducer);
                reducer.ReduceToMaxQuadricError(quadricDistance * quadricDistance);
                m = new DMesh3(m, true);
            }
            lap(phases, "reduce", timer);
            if (cancelF() || NotCleanReason(m, checkSelfIntersections) != "")
                return null;
            lap(phases, "check", timer);
            bool within = certified_within(m, mesh, maxDistance, cancelF, ref hausdorffSubdivisions);
            lap(phases, "certify", timer);
            return within ? m : null;
        }

        // the smallest and largest triangle angle (degrees) and the share of triangles with an angle above 170 degrees
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
        /// configure widens the operations' tolerance beyond that, the Hausdorff check decides as before.
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
                    RoundingMaxPairTests = RoundingMaxPairTests
                };
                DMesh3 res = resolve.Compute() ? resolve.Result : null;
                ExactResolveRounding = resolve.Rounding;
                SeparatedRounding = null;
                add_phase("exact.resolve", timer.ElapsedMilliseconds); timer.Restart();
                ExactResolveRoundsUsed++;
                if (res == null || res.TriangleCount == 0) {
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
                            AbsoluteTolerance = StrictRounding ? 0 : RoundingAbsoluteTolerance, MaxPairTests = RoundingMaxPairTests
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
        /// "self-intersections". Useful as a test assertion: NotCleanReason(mesh) == "".
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

        // a triangle with area below 1e-12 * diagonal^2 (of the bounds of the triangles). Outside an extent of
        // [2^-100, 2^100] the test runs on coordinate differences scaled by a power of two to an extent of about 1
        // (exact, MeshRepairMetrics.unit_scale): the area (the length of a cross product, a fourth power of the edges
        // inside) underflowed below edges of about 2^-255, so well-shaped triangles were degenerate; below a diagonal
        // of about 2^-517 the threshold underflowed to 0 and no triangle was, not even an exactly flat one. Differences,
        // not coordinates: a tiny extent far from the origin would overflow when its coordinates are scaled up. 2^k
        // scales give the same answer
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
