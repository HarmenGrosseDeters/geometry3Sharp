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
        /// outside the input; otherwise the unsimplified wrap is (AlphaWrapSimplified false).
        /// </summary>
        public bool SimplifyAlphaWrap = true;

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
        internal enum WrapSimplificationPolicy { Default, FeatureConstrained, QualityFlips, LocalQualityRollback, TangentialQuality }
        internal WrapSimplificationPolicy WrapPolicy = WrapSimplificationPolicy.Default;
        internal bool DiagnoseWrapSimplification = false;
        internal Dictionary<string, object> WrapSimplificationDiagnostics = new Dictionary<string, object>();
        internal bool CaptureWrapCandidate;
        internal DMesh3 WrapCandidateBeforeCheck;
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
        /// false if cancelled, if the input is empty, or if both the solidify fallback (eg the grid
        /// would be too large) and the alpha wrap failed; Mesh is then not replaced. true otherwise, also for an
        /// unclean solidify result (when the wrap is off or failed): check ResultReason.
        /// </returns>
        public bool Apply()
        {
            UsedSolidify = false;
            ResultReason = "not repaired";
            UsedInput = false;
            UsedAlphaWrap = false;
            AlphaWrapTriangles = 0;
            AlphaWrapSimplified = false;
            WrapSimplificationDiagnostics.Clear();
            WrapCandidateBeforeCheck = null;
            SolidifyReason = "";
            Solidify = null;
            AlphaWrap = null;
            WrapReason = "";
            UsedExactResolve = false;
            ExactResolveReason = "";
            TopologyChanged = false;
            GenusBefore = GenusAfter = double.NaN;
            ExactResolveRoundsUsed = 0;
            ExactResolveSelfIntersections.Clear();
            ExactRoundingEdits.Clear();
            FallbackReason = "";
            RepairVolumeIoU = double.NaN;
            PhaseMilliseconds.Clear();
            phase_timer.Restart();
            // unused vertices are no part of the solid, but far away they would make every threshold relative to the
            // diagonal (degenerate area, solidify cell size, wrap alpha) meaningless
            MeshEditor.RemoveUnusedVertices(Mesh);
            DMesh3 input = new DMesh3(Mesh);
            DMesh3 result;
            if (RepairF != null) {
                result = RepairF(Mesh);
            } else {
                var repair = new MeshAutoRepair(Mesh) { PostRepairCleanup = true };
                ConfigureRepair?.Invoke(repair);
                repair.Apply();
                result = repair.Mesh;
            }
            end_phase("repair");
            if (CancelF())
                return false;
            FallbackReason = NotCleanReason(result, CheckSelfIntersections);
            end_phase("check");
            DMesh3 solidifyFrom = result;
            if (FallbackReason == "") {
                // (NaN: the input has no solid to compare with, accepted)
                string differs = volume_check(input, result, out RepairVolumeIoU);
                if (differs != "") {
                    FallbackReason = differs;
                    solidifyFrom = input;
                }
                end_phase("volume");
            } else {
                // an unclean result can have lost parts of the input as well (eg erosion of an open region, which
                // the volume check of the exact stage rejects, but the solidify and the wrap do not check): then the
                // fallbacks start from the input, like for a clean result whose volume differs
                if (result.TriangleCount == 0 || volume_check(input, result, out RepairVolumeIoU) != "")
                    solidifyFrom = input;
                end_phase("volume");
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
                    bool cleanButDiffers = FallbackReason == VolumeDiffers || FallbackReason == PartsLost;
                    if (cleanButDiffers == false)
                        exact = resolve_exactly(result, input, false);
                    // its volume may differ (or a part be lost) only because components are oriented differently from
                    // the input (eg a nested part taken for a cavity): then resolve it again, oriented by the input's
                    // solid, if that orientation differs at all
                    if (exact == null && (cleanButDiffers || ExactResolveReason == VolumeDiffers || ExactResolveReason == PartsLost) && CancelF() == false)
                        exact = resolve_exactly(result, input, true);
                }
                bool fromResult = exact != null;
                // then the input itself (a clean result whose volume differs, or an unclean one that lost volume)
                if (exact == null && solidifyFrom != result && CancelF() == false) {
                    string reason = ExactResolveReason;
                    exact = resolve_exactly(solidifyFrom, input, false);
                    if (exact == null && reason != "")
                        ExactResolveReason = reason + "; input: " + ExactResolveReason;
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
                        wrapped = simplify_wrap(AlphaWrap.Result, AlphaWrap.UsedOffset) ?? AlphaWrap.Result;
                        end_phase("wrap.simplify");
                        if (CancelF())
                            return false;
                    }
                    AlphaWrapSimplified = wrapped != AlphaWrap.Result;
                    Mesh = wrapped;
                    UsedAlphaWrap = true;
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

        // "" if the solid of res agrees with the input's (IoU at least MinVolumeIoU, no component lost), otherwise why not
        string volume_check(DMesh3 input, DMesh3 res, out double iou)
        {
            iou = double.NaN;
            if (MinVolumeIoU <= 0)
                return "";      // (no volume checks at all)
            // (the input's solid once per Apply(), the result's once for both checks)
            if (inputSolid == null || inputSolidOf != input) {
                inputSolid = new MeshRepairMetrics.SolidReference(input);
                inputSolidOf = input;
            }
            Func<Vector3d, bool> resultInside = MeshRepairMetrics.SolidInside(res);
            if (MinVolumeIoU > 0 && MeshRepairMetrics.VolumeIoUAtLeast(inputSolid, res, resultInside, MinVolumeIoU, out iou) == false)
                return VolumeDiffers;
            if (CheckLostComponents && MeshRepairMetrics.LostComponents(inputSolid, resultInside) > 0)
                return PartsLost;
            return "";
        }
        MeshRepairMetrics.SolidReference inputSolid;
        DMesh3 inputSolidOf;

        // the wrap simplified within half the offset, without self-intersections; null if that is not clean or certified
        DMesh3 simplify_wrap(DMesh3 wrap, double offset)
        {
            if (CancelF())
                return null;
            return SimplifyWithin(wrap, 0.5 * offset, 0.5 * offset, CheckSelfIntersections, CancelF,
                WrapPolicy == WrapSimplificationPolicy.FeatureConstrained ? 0.30 : 0.05,
                WrapPolicy == WrapSimplificationPolicy.FeatureConstrained,
                WrapPolicy == WrapSimplificationPolicy.QualityFlips,
                WrapPolicy == WrapSimplificationPolicy.LocalQualityRollback,
                DiagnoseWrapSimplification ? WrapSimplificationDiagnostics : null,
                WrapPolicy == WrapSimplificationPolicy.TangentialQuality,
                CaptureWrapCandidate ? (Action<DMesh3>)(snapshot => WrapCandidateBeforeCheck = snapshot) : null);
        }

        // Restrict the existing remesher to its poor-triangle quality criterion, not valence-only flips.
        sealed class WrapQualityFlipper : Remesher
        {
            internal WrapQualityFlipper(DMesh3 mesh) : base(mesh) { }

            protected override ProcessResult ProcessEdge(int edgeID)
            {
                if (!mesh.IsEdge(edgeID) || mesh.IsBoundaryEdge(edgeID))
                    return ProcessResult.Ignored_EdgeIsFine;
                Index2i edge = mesh.GetEdgeV(edgeID), opposite = mesh.GetEdgeOpposingV(edgeID);
                Vector3d a = mesh.GetVertex(edge.a), b = mesh.GetVertex(edge.b);
                Vector3d c = mesh.GetVertex(opposite.a), d = mesh.GetVertex(opposite.b);
                double before = Math.Min(TriangleQuality(ref a, ref b, ref c), TriangleQuality(ref b, ref a, ref d));
                double after = Math.Min(TriangleQuality(ref c, ref d, ref b), TriangleQuality(ref d, ref c, ref a));
                if (!(before < MinTriangleQuality && after > 2 * before))
                    return ProcessResult.Ignored_EdgeIsFine;
                return base.ProcessEdge(edgeID);
            }
        }

        // The immutable tree covers unchanged triangles; edited triangles are tested separately. This avoids
        // rebuilding a whole-mesh tree (or copying a mesh) per edge without using stale bounds for edited faces.
        internal sealed class LocalWrapQualityFlipper : Remesher
        {
            readonly DMeshAABBTree3 reference, unchanged;
            readonly TriangleDeviationBounds deviation;
            readonly SortedSet<int> edited = new SortedSet<int>();
            readonly double maxDistance;
            readonly Func<bool> cancelF;
            internal int Accepted, Rejected, AcceptedMoves, RejectedMoves;
            readonly TriangleHashGrid movingGrid;
            readonly Vector3d[] tangentNormals;
            readonly bool[] fixedVertices;

            internal LocalWrapQualityFlipper(DMesh3 mesh, DMeshAABBTree3 reference, double maxDistance, Func<bool> cancelF,
                bool tangential = false) : base(mesh)
            {
                this.reference = reference;
                this.maxDistance = maxDistance;
                this.cancelF = cancelF;
                deviation = new TriangleDeviationBounds(reference.Mesh);
                unchanged = new DMeshAABBTree3(new DMesh3(mesh), true);
                EnableFlips = true;
                EnableCollapses = EnableSplits = EnableSmoothing = EnableParallelSmooth = false;
                MinTriangleQuality = 0.30;
                PreventNormalFlips = true;
                Progress = new ProgressCancel(cancelF);
                if (tangential) {
                    var featureConstraints = new MeshConstraints();
                    MeshConstraintUtil.PreserveBoundaryAndFeatures(featureConstraints, mesh, 30);
                    SetExternalConstraints(featureConstraints);
                    tangentNormals = new Vector3d[mesh.MaxVertexID];
                    fixedVertices = new bool[mesh.MaxVertexID];
                    foreach (int vid in mesh.VertexIndices()) {
                        VertexConstraint constraint = featureConstraints.GetVertexConstraint(vid);
                        fixedVertices[vid] = constraint.Fixed || constraint.Target != null;
                        Vector3d normal = Vector3d.Zero;
                        foreach (int tid in mesh.VtxTrianglesItr(vid).OrderBy(t => t)) {
                            Triangle3d tri = triangle(mesh.GetTriangle(tid));
                            normal += (tri.V1 - tri.V0).Cross(tri.V2 - tri.V0);
                        }
                        tangentNormals[vid] = normal.Normalized;
                    }
                    double sum = 0; int edges = 0;
                    foreach (int eid in mesh.EdgeIndices()) {
                        Index2i ev = mesh.GetEdgeV(eid);
                        sum += mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); edges++;
                    }
                    movingGrid = new TriangleHashGrid(mesh.CachedBounds.Min, 1.5 * sum / Math.Max(1, edges));
                    foreach (int tid in mesh.TriangleIndices()) movingGrid.Insert(tid, mesh.GetTriBounds(tid));
                }
            }

            protected override ProcessResult ProcessEdge(int edgeID)
            {
                return TryFlip(edgeID) ? ProcessResult.Ok_Flipped : ProcessResult.Ignored_EdgeIsFine;
            }

            internal bool IsFixedVertex(int vid) { return fixedVertices != null && fixedVertices[vid]; }
            internal Vector3d TangentNormal(int vid) { return tangentNormals[vid]; }

            internal void TangentialPass()
            {
                foreach (int vid in mesh.VertexIndices()) {
                    if (cancelF()) return;
                    if (IsFixedVertex(vid)) continue;
                    Vector3d current = mesh.GetVertex(vid), normal = tangentNormals[vid], displacement = Vector3d.Zero;
                    int count = 0;
                    foreach (int neighbor in mesh.VtxVerticesItr(vid).OrderBy(v => v)) {
                        displacement += mesh.GetVertex(neighbor) - current; count++;
                    }
                    if (count == 0 || !normal.IsFinite || normal.LengthSquared == 0) continue;
                    displacement /= count;
                    displacement -= normal.Dot(displacement) * normal;
                    foreach (double fraction in new[] { 0.1, 0.05, 0.025 }) {
                        if (TryMove(vid, current + fraction * displacement)) break;
                    }
                }
            }

            internal bool TryMove(int vid, Vector3d position)
            {
                if (cancelF() || tangentNormals == null || !mesh.IsVertex(vid) || IsFixedVertex(vid)) return false;
                if (!position.IsFinite) { RejectedMoves++; return false; }
                int[] ring = mesh.VtxTrianglesItr(vid).OrderBy(t => t).ToArray();
                var replaced = new HashSet<int>(ring);
                var proposed = new Triangle3d[ring.Length];
                double before = double.PositiveInfinity, after = double.PositiveInfinity;
                for (int i = 0; i < ring.Length; ++i) {
                    Index3i ids = mesh.GetTriangle(ring[i]);
                    Triangle3d old = triangle(ids);
                    Triangle3d candidate = new Triangle3d(ids.a == vid ? position : old.V0,
                        ids.b == vid ? position : old.V1, ids.c == vid ? position : old.V2);
                    proposed[i] = candidate;
                    Vector3d oldNormal = (old.V1 - old.V0).Cross(old.V2 - old.V0);
                    Vector3d newNormal = (candidate.V1 - candidate.V0).Cross(candidate.V2 - candidate.V0);
                    if (!oldNormal.IsFinite || !newNormal.IsFinite || !(oldNormal.Dot(newNormal) > 0)
                        || ExactTriangleIntersection.IsDegenerate(ref candidate)
                        || !deviation.IsWithin(reference, candidate.V0, candidate.V1, candidate.V2, maxDistance)) {
                        RejectedMoves++; return false;
                    }
                    before = Math.Min(before, TriangleQuality(ref old.V0, ref old.V1, ref old.V2));
                    after = Math.Min(after, TriangleQuality(ref candidate.V0, ref candidate.V1, ref candidate.V2));
                }
                if (!(after > before) || double.IsInfinity(after) || double.IsNaN(after)) { RejectedMoves++; return false; }
                for (int i = 0; i < ring.Length; ++i) {
                    Triangle3d a = proposed[i];
                    for (int j = i + 1; j < ring.Length; ++j) {
                        Triangle3d b = proposed[j];
                        if (ExactTriangleIntersection.IntersectsBeyondShared(mesh.GetTriangle(ring[i]), ref a, mesh.GetTriangle(ring[j]), ref b)) {
                            RejectedMoves++; return false;
                        }
                    }
                    if (intersects(mesh.GetTriangle(ring[i]), a, replaced.Contains) || cancelF()) {
                        RejectedMoves++; return false;
                    }
                }
                mesh.SetVertex(vid, position);
                foreach (int tid in ring) movingGrid.Insert(tid, mesh.GetTriBounds(tid));
                AcceptedMoves++;
                return true;
            }

            internal bool TryFlip(int edgeID)
            {
                if (cancelF() || !mesh.IsEdge(edgeID) || mesh.IsBoundaryEdge(edgeID)
                    || (constraints != null && !constraints.GetEdgeConstraint(edgeID).CanFlip)) return false;
                Index2i edge = mesh.GetEdgeV(edgeID), opposite = mesh.GetEdgeOpposingV(edgeID), tris = mesh.GetEdgeT(edgeID);
                int a = edge.a, b = edge.b;
                Index3i old = mesh.GetTriangle(tris.a);
                IndexUtil.orient_tri_edge(ref a, ref b, ref old);
                int c = opposite.a, d = opposite.b;
                Vector3d pa = mesh.GetVertex(a), pb = mesh.GetVertex(b), pc = mesh.GetVertex(c), pd = mesh.GetVertex(d);
                double before = Math.Min(TriangleQuality(ref pa, ref pb, ref pc), TriangleQuality(ref pb, ref pa, ref pd));
                double after = Math.Min(TriangleQuality(ref pc, ref pd, ref pb), TriangleQuality(ref pd, ref pc, ref pa));
                if (!(before < 0.30 && after > 2 * before)) return false;
                Index3i i0 = new Index3i(c, d, b), i1 = new Index3i(d, c, a);
                Triangle3d t0 = new Triangle3d(pc, pd, pb), t1 = new Triangle3d(pd, pc, pa);
                if (mesh.FindEdge(c, d) != DMesh3.InvalidID || flip_inverts_normals(a, b, c, d, tris.a)
                    || ExactTriangleIntersection.IsDegenerate(ref t0) || ExactTriangleIntersection.IsDegenerate(ref t1)
                    || !deviation.IsWithin(reference, pc, pd, pb, maxDistance)
                    || !deviation.IsWithin(reference, pd, pc, pa, maxDistance)
                    || ExactTriangleIntersection.IntersectsBeyondShared(i0, ref t0, i1, ref t1)
                    || intersects(i0, t0, tris) || intersects(i1, t1, tris) || cancelF()) {
                    Rejected++;
                    return false;
                }
                DMesh3.EdgeFlipInfo info;
                if (mesh.FlipEdge(edgeID, out info) != MeshResult.Ok) {
                    Rejected++;
                    return false;
                }
                if (movingGrid != null) {
                    movingGrid.Insert(tris.a, mesh.GetTriBounds(tris.a));
                    movingGrid.Insert(tris.b, mesh.GetTriBounds(tris.b));
                } else {
                    edited.Add(tris.a);
                    edited.Add(tris.b);
                }
                Accepted++;
                return true;
            }

            bool intersects(Index3i indices, Triangle3d proposed, Index2i replaced)
            {
                return intersects(indices, proposed, tid => tid == replaced.a || tid == replaced.b);
            }

            bool intersects(Index3i indices, Triangle3d proposed, Func<int, bool> excluded)
            {
                AxisAlignedBox3d box = new AxisAlignedBox3d(proposed.V0);
                box.Contain(proposed.V1); box.Contain(proposed.V2);
                bool hit = false;
                Action<int> check = tid => {
                    if (hit || excluded(tid)) return;
                    Triangle3d other = triangle(mesh.GetTriangle(tid));
                    if (ExactTriangleIntersection.IntersectsBeyondShared(indices, ref proposed, mesh.GetTriangle(tid), ref other)) hit = true;
                };
                if (movingGrid != null) {
                    movingGrid.Query(box, tid => {
                        AxisAlignedBox3d otherBox;
                        if (movingGrid.TryGetBox(tid, out otherBox) && otherBox.Intersects(box)) check(tid);
                    });
                    return hit;
                }
                unchanged.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                    NextBoxDF = (bounds, depth) => !hit && bounds.Intersects(box),
                    NextTriangleF = tid => { if (!edited.Contains(tid)) check(tid); }
                });
                foreach (int tid in edited) {
                    if (hit || cancelF()) break;
                    if (mesh.GetTriBounds(tid).Intersects(box)) check(tid);
                }
                return hit;
            }

            Triangle3d triangle(Index3i t)
            {
                return new Triangle3d(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c));
            }
        }

        /// <summary>
        /// mesh reduced (quadric error up to quadricDistance^2, no collapse that creates a self-intersection), or null
        /// if the result is not clean or not certified within maxDistance of the mesh (one-sided Hausdorff upper
        /// bound, from the result to the mesh). The distance is checked afterwards rather than per collapse
        /// (Reducer.MaxDeviation): on alpha wraps that check took 10 to 30 times longer and changed nothing.
        /// Internal experiments may preserve 30-degree feature curves, or run two poor-triangle flip-only
        /// passes after reduction. Flips use the remesher's normal guards; cleanliness (including requested
        /// self-intersection checks) and the distance certificate are checked after all passes. LocalQualityRollback
        /// instead fixes q=.05, independently certifies the reduced baseline, and prevalidates every prospective
        /// flip against the immutable reference envelope and exact intersections. A rejected final improvement
        /// retains that baseline; cancellation returns null. TangentialQuality adds two fixed serial vertex/flip
        /// sweeps with reference tangent planes, pinned feature curves and the same prospective safety checks.
        /// diagnostic_ms measures only extra diagnostic checks,
        /// not required local safety checks, and is included in (not subtracted from) the simplification wall time.
        /// </summary>
        internal static DMesh3 SimplifyWithin(DMesh3 mesh, double quadricDistance, double maxDistance, bool checkSelfIntersections, Func<bool> cancelF,
            double minCollapseTriangleQuality = 0.05, bool preserveFeatures = false, bool flipOnlyQualityPasses = false,
            bool localQualityRollback = false, Dictionary<string, object> diagnostics = null, bool tangentialQuality = false,
            Action<DMesh3> captureCandidate = null)
        {
            if (tangentialQuality && (preserveFeatures || flipOnlyQualityPasses || localQualityRollback))
                throw new ArgumentException("Tangential quality is a separate fixed policy.");
            localQualityRollback |= tangentialQuality;
            if (localQualityRollback && preserveFeatures)
                throw new ArgumentException("Local quality rollback cannot be combined with feature constraints.");
            if (localQualityRollback) minCollapseTriangleQuality = 0.05;
            var stages = diagnostics == null ? null : new List<Dictionary<string, object>>();
            if (diagnostics != null) {
                diagnostics.Clear();
                diagnostics["stages"] = stages;
                diagnostics["reductionInputTriangles"] = mesh.TriangleCount;
                diagnostics["acceptedSource"] = "original";
                diagnostics["rejectionReason"] = "";
                diagnostics["localAcceptedFlips"] = 0;
                diagnostics["localRejectedFlips"] = 0;
                diagnostics["diagnostic_ms"] = 0L;
            }
            DMesh3 m = new DMesh3(mesh);
            Reducer reducer = new Reducer(m) {
                PreventSelfIntersections = true, Progress = new ProgressCancel(cancelF),
                AreaWeightedQuadrics = false, MinCollapseTriangleQuality = minCollapseTriangleQuality, EdgeFlipTolerance = Math.Cos(60 * MathUtil.Deg2Rad)
            };
            if (preserveFeatures) {
                var constraints = new MeshConstraints();
                MeshConstraintUtil.PreserveBoundaryAndFeatures(constraints, m, 30);
                reducer.SetExternalConstraints(constraints);
            }
            reducer.ReduceToMaxQuadricError(quadricDistance * quadricDistance);
            m = new DMesh3(m, true);
            if (!localQualityRollback && diagnostics == null)
                return original_wrap_finish(m, mesh, maxDistance, checkSelfIntersections, cancelF, flipOnlyQualityPasses);
            Dictionary<string, object> reducedStage = wrap_stage("reduction", m, maxDistance, stages);
            Dictionary<string, object>[] flipStages = null;
            if (!tangentialQuality && (flipOnlyQualityPasses || localQualityRollback))
                flipStages = new[] { wrap_stage("flip-pass-1", m, maxDistance, stages), wrap_stage("flip-pass-2", m, maxDistance, stages) };
            if (diagnostics != null) diagnostics["reductionOutputTriangles"] = m.TriangleCount;
            DMeshAABBTree3 spatial = null;
            DMesh3 reduced = null;
            if (localQualityRollback || diagnostics != null) {
                var timer = Stopwatch.StartNew();
                bool passed = check_wrap_stage(m, mesh, ref spatial, maxDistance, checkSelfIntersections, cancelF, reducedStage);
                if (localQualityRollback) {
                    if (!passed) {
                        wrap_decision(diagnostics, "original", wrap_rejection(reducedStage, cancelF));
                        return null;
                    }
                    reduced = m;
                    m = new DMesh3(reduced);
                } else if (diagnostics != null) {
                    diagnostics["diagnostic_ms"] = timer.ElapsedMilliseconds;
                }
            }
            if (tangentialQuality)
                return tangential_wrap_finish(reduced, m, mesh, spatial, maxDistance, checkSelfIntersections, cancelF, diagnostics, stages, captureCandidate);
            LocalWrapQualityFlipper local = localQualityRollback ? new LocalWrapQualityFlipper(m, spatial, maxDistance, cancelF) : null;
            Dictionary<string, object> lastStage = reducedStage;
            if (flipOnlyQualityPasses || localQualityRollback) {
                Remesher remesher = local;
                if (remesher == null) remesher = new WrapQualityFlipper(m) {
                    EnableFlips = true, EnableCollapses = false, EnableSplits = false, EnableSmoothing = false,
                    EnableParallelSmooth = false, MinTriangleQuality = 0.30, PreventNormalFlips = true,
                    Progress = new ProgressCancel(cancelF)
                };
                for (int pass = 0; pass < 2; ++pass) {
                    lastStage = flipStages[pass];
                    int acceptedBefore = local == null ? 0 : local.Accepted, rejectedBefore = local == null ? 0 : local.Rejected;
                    if (cancelF()) {
                        lastStage["certificateStatus"] = "cancelled";
                        wrap_decision(diagnostics, "original", "cancelled");
                        return null;
                    }
                    remesher.BasicRemeshPass();
                    lastStage["triangles"] = m.TriangleCount;
                    if (local != null) {
                        lastStage["localAcceptedFlips"] = local.Accepted - acceptedBefore;
                        lastStage["localRejectedFlips"] = local.Rejected - rejectedBefore;
                        if (diagnostics != null) {
                            diagnostics["localAcceptedFlips"] = local.Accepted;
                            diagnostics["localRejectedFlips"] = local.Rejected;
                        }
                    }
                    if (diagnostics != null && pass == 0) {
                        var timer = Stopwatch.StartNew();
                        check_wrap_stage(m, mesh, ref spatial, maxDistance, checkSelfIntersections, cancelF, lastStage);
                        diagnostics["diagnostic_ms"] = (long)diagnostics["diagnostic_ms"] + timer.ElapsedMilliseconds;
                    }
                }
            }
            if (localQualityRollback)
                return FinishLocalWrap(reduced, m, spatial, maxDistance, checkSelfIntersections, cancelF, diagnostics, lastStage, local.Accepted);
            bool accepted = check_wrap_stage(m, mesh, ref spatial, maxDistance, checkSelfIntersections, cancelF, lastStage);
            if (cancelF()) {
                wrap_decision(diagnostics, "original", "cancelled");
                return null;
            }
            if (accepted) {
                wrap_decision(diagnostics, flipOnlyQualityPasses ? "flipped" : "reduced", "");
                return m;
            }
            wrap_decision(diagnostics, "original", wrap_rejection(lastStage, cancelF));
            return null;
        }

        static DMesh3 tangential_wrap_finish(DMesh3 reduced, DMesh3 m, DMesh3 original, DMeshAABBTree3 reference,
            double maxDistance, bool checkSelfIntersections, Func<bool> cancelF, Dictionary<string, object> diagnostics,
            List<Dictionary<string, object>> stages, Action<DMesh3> captureCandidate)
        {
            var local = new LocalWrapQualityFlipper(m, reference, maxDistance, cancelF, true);
            if (diagnostics != null) {
                diagnostics["movementFractions"] = new[] { 0.1, 0.05, 0.025 };
                diagnostics["tangentAcceptedMoves"] = 0;
                diagnostics["tangentRejectedMoves"] = 0;
            }
            Dictionary<string, object> last = null;
            for (int pass = 0; pass < 2; ++pass) {
                last = wrap_stage("tangent-pass-" + (pass + 1), m, maxDistance, stages);
                if (cancelF()) { wrap_decision(diagnostics, "original", "cancelled"); return null; }
                int acceptedMoves = local.AcceptedMoves, rejectedMoves = local.RejectedMoves;
                local.TangentialPass();
                last["acceptedMoves"] = local.AcceptedMoves - acceptedMoves;
                last["rejectedMoveAttempts"] = local.RejectedMoves - rejectedMoves;
                if (diagnostics != null) {
                    diagnostics["tangentAcceptedMoves"] = local.AcceptedMoves;
                    diagnostics["tangentRejectedMoves"] = local.RejectedMoves;
                    var timer = Stopwatch.StartNew();
                    check_wrap_stage(m, original, ref reference, maxDistance, checkSelfIntersections, cancelF, last);
                    diagnostics["diagnostic_ms"] = (long)diagnostics["diagnostic_ms"] + timer.ElapsedMilliseconds;
                }
                last = wrap_stage("flip-pass-" + (pass + 1), m, maxDistance, stages);
                if (cancelF()) { wrap_decision(diagnostics, "original", "cancelled"); return null; }
                int acceptedFlips = local.Accepted, rejectedFlips = local.Rejected;
                local.BasicRemeshPass();
                last["localAcceptedFlips"] = local.Accepted - acceptedFlips;
                last["localRejectedFlips"] = local.Rejected - rejectedFlips;
                if (diagnostics != null) {
                    diagnostics["localAcceptedFlips"] = local.Accepted;
                    diagnostics["localRejectedFlips"] = local.Rejected;
                    if (pass == 0) {
                        var timer = Stopwatch.StartNew();
                        check_wrap_stage(m, original, ref reference, maxDistance, checkSelfIntersections, cancelF, last);
                        diagnostics["diagnostic_ms"] = (long)diagnostics["diagnostic_ms"] + timer.ElapsedMilliseconds;
                    }
                }
            }
            if (captureCandidate != null && !cancelF()) captureCandidate(new DMesh3(m));
            DMesh3 result = FinishLocalWrap(reduced, m, reference, maxDistance, checkSelfIntersections, cancelF,
                diagnostics, last, local.Accepted);
            if (diagnostics != null && ReferenceEquals(result, m) && (local.Accepted > 0 || local.AcceptedMoves > 0))
                diagnostics["acceptedSource"] = "tangential";
            return result;
        }

        // reduced is independently clean and certified before the caller creates or edits flipped.
        internal static DMesh3 FinishLocalWrap(DMesh3 reduced, DMesh3 flipped, DMeshAABBTree3 reference, double maxDistance,
            bool checkSelfIntersections, Func<bool> cancelF, Dictionary<string, object> diagnostics = null,
            Dictionary<string, object> stage = null, int acceptedFlips = 0)
        {
            if (stage == null) stage = wrap_stage("flip-pass-2", flipped, maxDistance, null);
            bool accepted = check_wrap_stage(flipped, reference.Mesh, ref reference, maxDistance, checkSelfIntersections, cancelF, stage);
            if (cancelF()) {
                wrap_decision(diagnostics, "original", "cancelled");
                return null;
            }
            wrap_decision(diagnostics, accepted && acceptedFlips > 0 ? "flipped" : "reduced",
                accepted ? "" : wrap_rejection(stage, cancelF));
            return accepted ? flipped : reduced;
        }

        static DMesh3 original_wrap_finish(DMesh3 m, DMesh3 mesh, double maxDistance, bool checkSelfIntersections,
            Func<bool> cancelF, bool flipOnlyQualityPasses)
        {
            if (flipOnlyQualityPasses) {
                var remesher = new WrapQualityFlipper(m) {
                    EnableFlips = true, EnableCollapses = false, EnableSplits = false, EnableSmoothing = false,
                    EnableParallelSmooth = false, MinTriangleQuality = 0.30, PreventNormalFlips = true,
                    Progress = new ProgressCancel(cancelF)
                };
                for (int pass = 0; pass < 2; ++pass) {
                    if (cancelF()) return null;
                    remesher.BasicRemeshPass();
                }
            }
            if (cancelF() || NotCleanReason(m, checkSelfIntersections) != "") return null;
            var spatial = new DMeshAABBTree3(mesh, true);
            foreach (double tol in new double[] { 0.1, 0.01 }) {
                var hd = new MeshHausdorffDistance(m, mesh) { Symmetric = false, SpatialB = spatial, Tolerance = tol * maxDistance, CancelF = cancelF };
                if (hd.Compute() == false || !(hd.LowerBound <= maxDistance)) return null;
                if (hd.UpperBound <= maxDistance) return m;
            }
            return null;
        }

        static Dictionary<string, object> wrap_stage(string name, DMesh3 mesh, double maxDistance, List<Dictionary<string, object>> stages)
        {
            var stage = new Dictionary<string, object> {
                { "stage", name }, { "triangles", mesh.TriangleCount }, { "cleanReason", "not-checked" },
                { "certificateStatus", "not-checked" }, { "maxDistance", maxDistance }
            };
            if (stages != null) stages.Add(stage);
            return stage;
        }

        static object json_distance(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? null : (object)value;
        }

        static bool check_wrap_stage(DMesh3 candidate, DMesh3 original, ref DMeshAABBTree3 spatial,
            double maxDistance, bool checkSelfIntersections, Func<bool> cancelF, Dictionary<string, object> stage)
        {
            if (cancelF()) { stage["certificateStatus"] = "cancelled"; return false; }
            string reason = NotCleanReason(candidate, checkSelfIntersections);
            stage["cleanReason"] = reason;
            if (reason != "") return false;
            if (spatial == null) spatial = new DMeshAABBTree3(original, true);
            // Keep the original two tolerances and acceptance rule. A straddling interval is incomplete,
            // not proof of excess; a lower bound above the budget is proof even without convergence.
            foreach (double tol in new double[] { 0.1, 0.01 }) {
                var hd = new MeshHausdorffDistance(candidate, original) {
                    Symmetric = false, SpatialB = spatial, Tolerance = tol * maxDistance, CancelF = cancelF
                };
                bool computed = hd.Compute();
                stage["lowerBound"] = json_distance(hd.LowerBound);
                stage["upperBound"] = json_distance(hd.UpperBound);
                stage["converged"] = hd.Converged;
                if (cancelF()) { stage["certificateStatus"] = "cancelled"; return false; }
                if (hd.LowerBound > maxDistance) { stage["certificateStatus"] = "exceeded"; return false; }
                stage["certificateStatus"] = "incomplete";
                if (!computed || !(hd.LowerBound <= maxDistance)) return false;
                if (hd.UpperBound <= maxDistance) { stage["certificateStatus"] = "passed"; return true; }
            }
            return false;
        }

        static string wrap_rejection(Dictionary<string, object> stage, Func<bool> cancelF)
        {
            if (cancelF() || (string)stage["certificateStatus"] == "cancelled") return "cancelled";
            string clean = (string)stage["cleanReason"];
            return clean != "" && clean != "not-checked" ? "cleanliness: " + clean : "certificate: " + stage["certificateStatus"];
        }

        static void wrap_decision(Dictionary<string, object> diagnostics, string source, string reason)
        {
            if (diagnostics == null) return;
            diagnostics["acceptedSource"] = source;
            diagnostics["rejectionReason"] = reason;
        }

        // the solid of the mesh with its self-intersections resolved exactly, or null (ExactResolveReason says why)
        DMesh3 resolve_exactly(DMesh3 from, DMesh3 input, bool orientByInput)
        {
            var timer = Stopwatch.StartNew();
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
                var resolve = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union) { SafeRounding = SafeRounding };
                DMesh3 res = resolve.Compute() ? resolve.Result : null;
                add_phase("exact.resolve", timer.ElapsedMilliseconds); timer.Restart();
                ExactResolveRoundsUsed++;
                if (res == null || res.TriangleCount == 0) {
                    ExactResolveReason = "resolve failed";
                    return null;
                }
                // slivers where intersection curves nearly meet: collapsed or flipped by the cleanup
                string reason = NotCleanReason(res, false);
                // the safe rounding's count of intersections holds only for the mesh it returned
                bool editedAfterRounding = false;
                if (reason == "degenerate triangles") {
                    editedAfterRounding = true;
                    new MeshPostRepairCleanup(res).Apply();
                    res = new DMesh3(res, true);
                    // the cleanup keeps slivers whose collapse would distort or fold a neighbour: collapsed anyway,
                    // the self-intersections that can create are found below and resolved by the next round (so
                    // only when they are checked)
                    if (CheckSelfIntersections && NotCleanReason(res, false) == "degenerate triangles" && CollapseSlivers(res) > 0)
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
                        var rounding = new MeshRoundingRepair(res);
                        rounding.Apply();
                        edits = rounding.Collapses + rounding.Flips + rounding.Moves + rounding.Zips + rounding.RemovedTriangles;
                        if (edits > 0)
                            res = new DMesh3(res, true);
                        si = rounding.RemainingIntersections;
                    }
                    ExactRoundingEdits.Add(new Index2i(first.Collapses + first.Flips + first.Moves + first.Zips + first.RemovedTriangles, edits));
                }
                reason = NotCleanReason(res, false);
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
            double diag = MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength;
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
            if (MeshIterators.BowtieVertices(mesh).Any()) return "bowties";
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
            if (MeshIterators.BowtieVertices(mesh).Any()) d |= Defects.Bowties;
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

        // a triangle with area below 1e-12 * diagonal^2 (of the bounds of the triangles)
        static bool has_degenerate_triangles(DMesh3 mesh)
        {
            double diag = MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength;
            double areaEps = 1e-12 * diag * diag;
            return mesh.TriangleIndices().Any(t => mesh.GetTriArea(t) < areaEps);
        }
    }
}
