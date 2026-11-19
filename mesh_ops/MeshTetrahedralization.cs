// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Tetrahedral mesh of any input (docs/design/TET_MESHING.md section 14, stage 1): the alpha wrap (MeshAlphaWrap), whose inside
    /// cells are already a valid tet mesh of the wrap surface, refined inside by Delaunay refinement (Shewchuk, "Tetrahedral mesh
    /// generation by Delaunay refinement", SoCG 1998; Cheng, Dey, Shewchuk, "Delaunay Mesh Generation", 2012): a bad inside tet
    /// (radius-edge ratio above MaxRadiusEdgeRatio, or circumradius above MaxCircumradius) gets its circumcentre.
    ///
    /// The surface is fixed during the refinement: a point is kept only if every cell its insertion removes is inside, otherwise the
    /// insertion is undone (Delaunay3.UndoLastInsert) and counted (refused_surface). Then no surface facet lies inside the cavity, so
    /// every surface facet stays a facet, and the new cells are inside; the test is on exact labels. With PeelBoundarySlivers = false
    /// the boundary is therefore the wrap surface exactly (same vertices, same triangles). With peeling (the default) it is the wrap
    /// surface after 2-2 flips of peeled boundary slivers: the same vertices (all the wrap's), still closed, 2-manifold and
    /// intersection-free, touching no input triangle (exact), moved inwards by at most the heights of the peeled slivers.
    /// Stage 2 (Improve, the default; TetMeshImprover) then flips and smooths; with BoundarySlideFactor > 0 it also moves boundary
    /// vertices, each by at most BoundarySlideFactor offsets from its wrap position (cumulative), every move checked exactly: all
    /// tets stay positive, the boundary stays free of self-intersections and the input strictly inside it (TET_MESHING.md 15).
    /// Every tet is exactly positively oriented (Delaunay3). The price: a bad tet whose circumcentre lies beyond the surface, or
    /// whose cavity would reach it, stays (counted in unrefined_bad_tets); the surface's own quality bounds those tets.
    ///
    /// Termination: no point is inserted closer than MinFeatureFactor * min(Offset, Alpha - Offset) to a vertex (the floor, in units
    /// of the wrap's own vertex separation), so the vertices are a packing of a bounded region and the number of insertions is
    /// finite whatever the input's angles; tets whose point the floor refuses are counted (refused_floor, unrefined_bad_tets).
    ///
    /// The refinement has a work bound (MaxTrialInsertionsPerWrapVertex): below a radius-edge bound of 2 termination is not proven, and near 1 it does
    /// not end in practice; the run then stops with status PointLimitReached and Targets says what was reached (TET_MESHING.md 17.10).
    ///
    /// Serial and deterministic; relative parameters only, so scaling the input by 2^k gives the scaled result bit for bit.
    /// </summary>
    public class MeshTetrahedralization
    {
        public DMesh3 Mesh;
        /// <summary> the wrap's parameters (MeshAlphaWrap.RelativeAlpha / RelativeOffset: the bounding box diagonal over alpha / offset).
        /// The default alpha is diagonal / 40, finer than the wrap's own default of / 20 (docs/design/TET_MESHING.md section 17.8, 500-mesh
        /// Thingi10K sample: every tet >= 10 degrees on 494 instead of 461 meshes, the input-to-boundary distance 0.77x, 1.3x the time, 1.7x the
        /// tets). Larger values follow the input more closely at more tets (17.2). </summary>
        public double RelativeAlpha = 40, RelativeOffset = 600;
        /// <summary>
        /// Surface fidelity target, absolute, in the input's units (docs/design/TET_MESHING.md section 17.13); 0, a negative value, NaN or infinity = off
        /// (the default: the uniform alpha, the output as before bit for bit). With a value above 0 the wrap is refined only where the input is farther
        /// than this from the wrap's boundary: the wrap at RelativeAlpha measures the distance of area-weighted input samples to its boundary
        /// (concave detail and thin gaps are bridged there, and that is where the distance is, 17.2), and the next wrap uses a local alpha
        /// (MeshAlphaWrap.AlphaField) of alpha * MaxSurfaceDeviation / distance, between alpha / 4 and alpha, graded with a slope of 0.25. One
        /// pass costs one more wrap, and the tets stay below those of a uniform alpha with the same deviation (on 39 Thingi10K meshes about 0.6x
        /// the tets of the uniform alpha / 80 for its maximum distance, 17.13). The distance cannot go below about 2 offsets (the offset surface
        /// is the least distance of the wrap): the tolerance is raised to that, and the alpha floor of alpha / 4 bounds what is reachable;
        /// Counts["local_alpha_fine_samples"] says how many samples got a finer alpha. The wrap's surface angle refinement (SurfaceMinAngleDegrees) is not used
        /// with it (the short edges it adds where alpha changes cost the dihedral floor, 17.13). Only for an input with triangles; without,
        /// the option does nothing. Combines with every other option; the local alpha is for the wrap's boundary, the tets inside follow it.
        /// </summary>
        public double MaxSurfaceDeviation = 0;
        /// <summary>
        /// the passes of the local alpha (MaxSurfaceDeviation), 1 to 3 (other values count as 1): every pass measures the distance of the wrap before it and refines
        /// its alpha where it is still above the tolerance, at one more wrap. Two passes reach about 0.87x the maximum distance of one at 1.1x the tets (17.13)
        /// </summary>
        public int LocalAlphaPasses = 1;
        /// <summary> radius-edge bound rho (> 2 for the termination argument without the floor) </summary>
        public double MaxRadiusEdgeRatio = 2;
        /// <summary> size bound: tets whose circumradius is larger than this are refined; 0 = none (the mesh is graded by the surface) </summary>
        public double MaxCircumradius = 0;
        /// <summary>
        /// size control, interior only (docs/design/TET_MESHING.md 17.12): a uniform target edge length h (absolute, in the input's units);
        /// 0, a negative value, NaN or infinity = off. A tet is refined while its circumradius exceeds 0.75 h (h at the
        /// tet's centroid), so the mean edge is about h and the mesh has about volume / (0.12 h^3) tets. Combines with SizeFunction and GradingFactor (the smallest size at a point wins).
        /// The boundary keeps the wrap's spacing (RelativeAlpha): tets at the fixed surface are not refined below its triangles
        /// (counted in unrefined_bad_tets), and a size below MinSizeFactor * alpha is clamped.
        /// </summary>
        public double TargetEdgeLength = 0;
        /// <summary>
        /// size control: the wanted edge length at a point (absolute), evaluated at the centroid of each tet, serially, so it need not be thread-safe;
        /// null = off. A result that is NaN, not positive or infinite is invalid and means "no size here" (not the finest mesh: a buggy function must not turn into a
        /// run of minutes): the other size options still apply there, and the samples are counted in InvalidSizeSamples. A valid size below MinSizeFactor * alpha is clamped
        /// to it and counted in Counts["size_clamped_tets"]. It must be deterministic for a deterministic result.
        /// </summary>
        public Func<Vector3d, double> SizeFunction = null;
        /// <summary>
        /// size control, automatic: the wanted edge length grows with the distance d to the wrap's boundary, h(p) = (mean edge length of the
        /// wrap's surface) + GradingFactor * d (a scale-free rule: 0.5 roughly doubles the size every 2 boundary spacings away from the surface); 0, a negative value or NaN = off
        /// </summary>
        public double GradingFactor = 0;
        /// <summary> size control: sizes below this many alpha (UsedAlpha) are clamped and counted (a cost guard, 17.12); 0, a negative value or NaN = 0.25 </summary>
        public double MinSizeFactor = 0.25;
        /// <summary> size control: the tets of the refinement (before stage 2) at whose centroid SizeFunction gave an invalid value (NaN, not positive or infinite): the function applied no size there (other size options still do). 0 if no size option is set; reset by Compute() </summary>
        public long InvalidSizeSamples;
        /// <summary>
        /// sliver step (Li and Teng 2001): tets with a smallest dihedral angle below this (degrees) are refined too, and a new point
        /// is chosen among candidates near the circumcentre so that its new tets have no dihedral angle below it; 0 = off
        /// (plain circumcentres)
        /// </summary>
        public double SliverDihedralDegrees = 10;
        /// <summary>
        /// the wrap's surface quality refinement (MeshAlphaWrap.RefineMinAngleDegrees: points on the offset surface for facets with a
        /// smaller angle, Chew 1993, Boissonnat and Oudot 2005). The surface is fixed during the tet refinement, so its triangles
        /// bound the tets on it: a larger value gives better boundary tets.
        /// </summary>
        public double SurfaceMinAngleDegrees = 15;
        /// <summary>
        /// peel boundary slivers: a bad inside tet with exactly two surface facets is moved outside (a 2-2 flip of the surface onto
        /// its other two facets) if the new surface edge is not a surface edge yet and the tet touches no input triangle (exact).
        /// The surface stays closed, 2-manifold, intersection-free and around the input; it moves inwards by the sliver's height.
        /// </summary>
        public bool PeelBoundarySlivers = true;
        /// <summary> threads of the wrap's carving (MeshAlphaWrap.MaxThreads: 0 = all cores, 1 = one thread); the result is the same for every value </summary>
        public int MaxThreads = 0;
        /// <summary>
        /// stage 2: improve the refined mesh by flips and smoothing (TetMeshImprover, Klingner and Shewchuk 2007): every tet with a
        /// smallest dihedral angle below SliverDihedralDegrees is worked on. Flips never change the boundary triangles; with
        /// BoundarySlideFactor > 0 (the default) boundary vertices move, see there
        /// </summary>
        public bool Improve = true;
        /// <summary>
        /// stage 2 (TET_MESHING.md section 16): edge splits for tets the flips and smoothing leave below SliverDihedralDegrees. An
        /// edge of the tet is split at its midpoint (a boundary edge in place, so the boundary's two triangles become four in their
        /// planes, up to the midpoint's rounding, checked exactly), the new vertex is smoothed, and the split is undone unless the
        /// smallest dihedral angle of the tets around it then strictly increases (Klingner and Shewchuk 2007's insertion with roll-back)
        /// </summary>
        public bool SplitEdges = true;
        /// <summary>
        /// stage 2: contraction of an edge of a bad tet with an interior end into its other end (the boundary does not change), under
        /// the link condition and only if the smallest dihedral angle of the changed tets strictly increases
        /// </summary>
        public bool ContractEdges = true;
        /// <summary> stage 2: passes over the bad tets, at most (the improvement stops earlier when a pass changes nothing) </summary>
        public int ImprovePasses = 50;
        /// <summary>
        /// stage 2 (TET_MESHING.md section 17): if tets below SliverDihedralDegrees are left after ImprovePasses, up to this many more
        /// passes with smoothing also along the active-set direction (Freitag, Jones, Plassmann 1995); a mesh that reached the target
        /// is not changed by it. 0: none
        /// </summary>
        public int RescuePasses = 150;
        /// <summary> (diagnosis, docs/design/TET_MESHING.md section 16) after stage 2, classify the tets left below the target: "diag_*" counts and DiagnosticLines </summary>
        internal bool DiagnoseImprovement = false;
        internal List<string> DiagnosticLines;
        /// <summary>
        /// stage 2: boundary vertices may move by at most this many offsets from their place on the wrap (each move checked exactly:
        /// no input triangle in the swept region, no boundary self-intersection, all tets positive); 0 = the boundary is fixed.
        /// The result's boundary then lies within this band around the wrap surface, and strictly around the input.
        /// </summary>
        public double BoundarySlideFactor = 0.5;
        /// <summary> stage 2: boundary moves only in the tangent plane of the vertex's boundary fan (TET_MESHING.md 15: 39 of 40 instead of 40 of 40 on the subset) </summary>
        public bool BoundaryMovesTangentOnly = false;
        /// <summary> the floor in units of the wrap's vertex separation min(Offset, Alpha - Offset) </summary>
        public double MinFeatureFactor = 0.125;
        /// <summary> stop (status PointLimitReached, result still valid) after this many inserted points </summary>
        public int MaxSteinerPoints = 2000000;
        /// <summary>
        /// work bound of the refinement (docs/design/TET_MESHING.md section 17.10): stop (status PointLimitReached, result still valid, the
        /// count "work_limit_reached" = 1) after this many trial insertions (the count "trial_insertions", the refinement's unit of work,
        /// kept points and undone ones) per vertex of the wrap, plus 2000. Delaunay refinement is proven to end only for a radius-edge
        /// bound above 2; below about 1.3 it can cascade without end (measured: 370 to 1400 trial insertions per wrap vertex within 25 s and rising,
        /// against at most 2.5 on the default path and 7.2 for the slowest terminating runs, at radius-edge 1.1 to 1.3; measured at the default
        /// sliver and floor settings: more candidate points per split (a larger SliverDihedralDegrees) cost up to 30 trials per split, so such a run can reach the bound
        /// sooner and then reports it; raise this value or set it to 0 for it). Not used with a size
        /// bound (MaxCircumradius &gt; 0: the point count then follows the volume, not the wrap); 0, a negative value or NaN turns it off. Targets says what was reached.
        /// </summary>
        public double MaxTrialInsertionsPerWrapVertex = 48;
        public Func<bool> CancelF = () => false;

        public enum TetStatus
        {
            Success = 0,
            /// <summary> the alpha wrap did not give a result (WrapStatus says why) </summary>
            WrapFailed = 1,
            /// <summary> MaxSteinerPoints or the work bound (MaxTrialInsertionsPerWrapVertex, Counts "work_limit_reached") reached: Result is valid but not fully refined </summary>
            PointLimitReached = 2,
            Cancelled = 3
        }

        public TetStatus Status;
        public MeshAlphaWrap.WrapStatus WrapStatus;
        /// <summary>
        /// the wrap of the last Compute(); its Result is the boundary of Result triangle by triangle if PeelBoundarySlivers is false
        /// (with peeling: the same vertices, some triangles flipped; see the class summary)
        /// </summary>
        public MeshAlphaWrap Wrap;
        public TetMesh3 Result;
        /// <summary>
        /// the quality targets checked on Result (TET_MESHING.md section 17.3, P5(a)): the minimum dihedral angle target is
        /// SliverDihedralDegrees (0 = off), the radius-edge target MaxRadiusEdgeRatio. Counts and flags are measured on the output
        /// (TetMeshQuality.CheckTargets), not assumed from the method: stage 1 leaves tets whose circumcentre lies beyond the fixed
        /// surface (unrefined_bad_tets) and stage 2 is hill climbing, so the flags can be false. null if Compute() gave no Result.
        /// </summary>
        public TetTargetReport Targets;
        /// <summary> tet_splits (inserted points), refused_floor, refused_surface (undone: the cavity reached the surface), unrefined_bad_tets </summary>
        public Dictionary<string, long> Counts = new Dictionary<string, long>();
        public double UsedAlpha, UsedOffset, UsedFloor;

        public MeshTetrahedralization(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        MeshAlphaWrap MakeWrap(Func<Vector3d, double> alphaField, double surfaceAngle)
        {
            return new MeshAlphaWrap(Mesh) { RelativeAlpha = RelativeAlpha, RelativeOffset = RelativeOffset, CancelF = CancelF, MaxThreads = MaxThreads,
                RefineMinAngleDegrees = surfaceAngle, AlphaField = alphaField };
        }

        const int DeviationSamples = 20000;
        const double LocalAlphaGradation = 0.25, LocalAlphaMaxRefinement = 4;
        double gradation = LocalAlphaGradation;

        /// <summary>
        /// The passes of MaxSurfaceDeviation: starting from the uniform wrap (wrap), measure the distance d_j of fixed area-weighted input samples
        /// to the wrap's surface, give the samples farther than the tolerance T the alpha alpha_j = alpha * T / d_j (at least alpha / 4; with the
        /// next pass the smaller of the old alpha_j and the rule on the new distance), and wrap again with the graded field. false: a wrap failed or
        /// was cancelled (wrap is then that wrap).
        /// </summary>
        bool RefineAlpha(ref MeshAlphaWrap wrap, double surfaceAngle)
        {
            double a0 = wrap.UsedAlpha, tol = Math.Max(MaxSurfaceDeviation, 2 * wrap.UsedOffset);
            // (the wrap clamps a local alpha to 3 offsets: nothing to refine if alpha is not above that)
            double aMin = Math.Max(a0 / LocalAlphaMaxRefinement, MeshAlphaWrap.AlphaFieldFloorOffsets * wrap.UsedOffset);
            Vector3d[] pos = AlphaSizeField.Samples(Mesh, DeviationSamples);
            int passes = LocalAlphaPasses >= 1 && LocalAlphaPasses <= 3 ? LocalAlphaPasses : 1;
            if (pos.Length == 0 || !(aMin > 0) || !(aMin < a0))
                return true;
            var aj = new double[pos.Length];
            for (int i = 0; i < aj.Length; ++i) aj[i] = a0;
            AlphaSizeField field = null;
            int executed = 0;
            for (int pass = 0; pass < passes; ++pass) {
                var tree = new DMeshAABBTree3(wrap.Result, true);
                for (int i = 0; i < pos.Length; ++i) {
                    if (CancelF()) { localCancelled = true; return false; }
                    tree.FindNearestTriangle(pos[i], out double d2);
                    aj[i] = NextAlpha(aj[i], Math.Sqrt(d2), tol, aMin);
                }
                field = new AlphaSizeField(pos, aj, a0, aMin, gradation);
                if (field.FineSamples == 0)
                    break;
                MeshAlphaWrap next = MakeWrap(field.At, surfaceAngle);
                bool ok = next.Compute();
                wrap = next;
                executed++;
                if (!ok || next.Status != MeshAlphaWrap.WrapStatus.Success)
                    return false;
            }
            localAlphaPasses = executed; localFineSamples = field == null ? 0 : field.FineSamples;
            return true;
        }
        /// <summary>
        /// the alpha of a sample after a pass (tests): unchanged if the wrap is within the tolerance of it, else alphaJ * tol / d (the alpha it was wrapped with, scaled
        /// by how far the distance is above the tolerance; alphaJ is alpha at the first pass), at least aMin, and never above the old alphaJ, so a later pass never
        /// makes a sample coarser, also where the pass before got it within the tolerance. Written as alphaJ * (tol / d): no overflow of the product at extreme scales.
        /// </summary>
        internal static double NextAlpha(double alphaJ, double d, double tol, double aMin)
        {
            return d > tol ? Math.Min(alphaJ, Math.Max(aMin, alphaJ * (tol / d))) : alphaJ;
        }
        long localAlphaPasses, localFineSamples;
        bool localRan;
        bool localCancelled;

        /// <summary>
        /// Runs the pipeline. With MaxSurfaceDeviation, if the result misses the dihedral target (Targets.MinDihedralMet is false: the size jumps of
        /// the local alpha can cost the floor on a few meshes, 17.13), (and Improve is on: the target is about the improved mesh) it runs once more with half the gradation slope (0.125, a smoother field) and keeps
        /// the result that meets the target, else the one with the larger smallest dihedral angle (Counts["local_alpha_retry"] = 1, and "local_alpha_retry_kept" = 1 if the
        /// second was kept); the target is checked on the output as ever, so a miss is reported in Targets, never silent.
        /// </summary>
        public bool Compute()
        {
            gradation = LocalAlphaGradation;
            RetryFirstWorstMinDihedral = double.NaN;
            bool ok = ComputeOnce();
            if (!(MaxSurfaceDeviation > 0) || !Improve || Result == null || Targets == null || Targets.MinDihedralMet || localFineSamples == 0)
                return ok;
            // keep the first run, try the smoother field
            var first = (Result, Targets, Counts, Status, WrapStatus, Wrap, UsedAlpha, UsedOffset, UsedFloor, InvalidSizeSamples, DiagnosticLines, localAlphaPasses, localFineSamples, localRan, ok);
            RetryFirstWorstMinDihedral = Targets.WorstMinDihedral;
            gradation = LocalAlphaGradation / 2;
            bool ok2 = ComputeOnce();
            if (Status == TetStatus.Cancelled) return ok2;        // cancelled during the retry: cancelled, not the first result
            bool keepSecond = Result != null && Targets != null && (Status == TetStatus.Success || Status == first.Item4)
                && (Targets.MinDihedralMet || Targets.WorstMinDihedral > first.Item2.WorstMinDihedral);
            if (keepSecond) {
                Counts["local_alpha_retry"] = 1; Counts["local_alpha_retry_kept"] = 1;
                return ok2;
            }
            (Result, Targets, Counts, Status, WrapStatus, Wrap, UsedAlpha, UsedOffset, UsedFloor, InvalidSizeSamples, DiagnosticLines, localAlphaPasses, localFineSamples, localRan, ok) = first;
            Counts["local_alpha_retry"] = 1;
            return ok;
        }
        /// <summary> (tests) the smallest dihedral angle of the first run, when Compute() ran a second time with a smoother field </summary>
        internal double RetryFirstWorstMinDihedral;

        bool ComputeOnce()
        {
            localAlphaPasses = 0; localFineSamples = 0; localCancelled = false; localRan = false;
            Result = null;
            Targets = null;
            InvalidSizeSamples = 0;
            DiagnosticLines = null;
            Counts = new Dictionary<string, long>();
            bool local = MaxSurfaceDeviation > 0 && !double.IsInfinity(MaxSurfaceDeviation) && Mesh != null && Mesh.TriangleCount > 0;
            // (the surface angle refinement adds short edges where alpha changes: not with a local alpha, section 17.13)
            double surfaceAngle = local ? 0 : SurfaceMinAngleDegrees;
            MeshAlphaWrap wrap = MakeWrap(null, surfaceAngle);
            bool ok = wrap.Compute();
            Wrap = wrap;
            WrapStatus = wrap.Status;
            if (!ok || wrap.Status != MeshAlphaWrap.WrapStatus.Success) {
                Status = wrap.Status == MeshAlphaWrap.WrapStatus.Cancelled ? TetStatus.Cancelled : TetStatus.WrapFailed;
                return false;
            }
            localRan = local;
            if (local && !RefineAlpha(ref wrap, surfaceAngle)) {
                Wrap = wrap;
                WrapStatus = wrap.Status;
                Status = wrap.Status == MeshAlphaWrap.WrapStatus.Cancelled || localCancelled ? TetStatus.Cancelled : TetStatus.WrapFailed;
                return false;
            }
            Wrap = wrap;
            WrapStatus = wrap.Status;
            UsedAlpha = wrap.UsedAlpha; UsedOffset = wrap.UsedOffset;
            UsedFloor = MinFeatureFactor * Math.Min(UsedOffset, UsedAlpha - UsedOffset);
            Delaunay3 dt = wrap.Triangulation.Clone();
            dt.RefinementSupport = true;
            foreach (int c in dt.CellIndices())
                dt.SetCellLabel(c, wrap.IsInsideCell(c) ? 1 : 0);
            double workLimit = MaxTrialInsertionsPerWrapVertex > 0 && MaxCircumradius <= 0 ? MaxTrialInsertionsPerWrapVertex * dt.VertexCount + 2000 : double.PositiveInfinity;
            var r = new Refiner(dt, UsedFloor, MaxRadiusEdgeRatio, MaxCircumradius, SliverDihedralDegrees, MaxSteinerPoints, CancelF) {
                TrialLimit = workLimit,
                Input = PeelBoundarySlivers ? Mesh : null };
            if (BuildSizeField(wrap, out SizeField field)) {
                r.SizeAt = field;
                r.MinSize = (MinSizeFactor > 0 ? MinSizeFactor : 0.25) * UsedAlpha;
                if (workLimit < double.PositiveInfinity) r.TrialsPerSizeSplit = MaxTrialInsertionsPerWrapVertex;   // a point inserted for the size counts like a wrap vertex
            }
            TetStatus st = r.Run();
            Status = st;
            if (st == TetStatus.Cancelled) {
                foreach (var kv in r.Counts) Counts[kv.Key] = kv.Value;
                return false;
            }
            Result = r.Extract();
            Counts["unrefined_bad_tets"] = r.CountBadInside();
            if (localRan) { Counts["local_alpha_passes"] = localAlphaPasses; Counts["local_alpha_fine_samples"] = localFineSamples; }   // only then: the default counts do not change
            if (r.SizeAt != null) { r.CountClamped(); InvalidSizeSamples = r.InvalidTets; }
            foreach (var kv in r.Counts) Counts[kv.Key] = kv.Value;
            if (r.WorkLimitHit) Counts["work_limit_reached"] = 1;                  // only then: the default path's counts do not change
            if (Improve && SliverDihedralDegrees > 0) {
                var imp = new TetMeshImprover(Result) { TargetDegrees = SliverDihedralDegrees, CancelF = CancelF };
                if (BoundarySlideFactor > 0 && Mesh.TriangleCount > 0) {
                    imp.InputTree = new DMeshAABBTree3(Mesh, true);
                    imp.BoundarySlideLimit = BoundarySlideFactor * UsedOffset;
                    imp.TangentOnly = BoundaryMovesTangentOnly;
                }
                imp.MaxPasses = ImprovePasses;
                imp.RescuePasses = RescuePasses;
                imp.Contraction = ContractEdges;
                imp.Insertion = SplitEdges;
                imp.Run();
                if (DiagnoseImprovement) { imp.Diagnose(); DiagnosticLines = imp.DiagnosticLines; }
                Result = imp.Result();
                foreach (var kv in imp.Counts) Counts["improve_" + kv.Key] = kv.Value;
                if (CancelF()) { Status = TetStatus.Cancelled; Result = null; return false; }
            }
            Targets = TetMeshQuality.CheckTargets(Result, SliverDihedralDegrees, MaxRadiusEdgeRatio);
            return st == TetStatus.Success;
        }

        internal delegate double SizeField(Vector3d p, out bool invalid);

        // the size field of the options set (the smallest size at a point), or false if none is; sizes are not clamped here (the refiner does)
        bool BuildSizeField(MeshAlphaWrap wrap, out SizeField field)
        {
            field = null;
            double h = TargetEdgeLength;
            bool uniform = h > 0 && !double.IsInfinity(h);
            bool user = SizeFunction != null;
            bool graded = GradingFactor > 0 && !double.IsInfinity(GradingFactor);
            if (!uniform && !user && !graded) return false;
            DMesh3 surface = wrap.Result;
            DMeshAABBTree3 tree = null;
            double h0 = 0, g = GradingFactor;
            if (graded) {
                double sum = 0; int n = 0;
                foreach (int e in surface.EdgeIndices()) { Index2i v = surface.GetEdgeV(e); sum += surface.GetVertex(v.a).Distance(surface.GetVertex(v.b)); n++; }
                h0 = n > 0 ? sum / n : wrap.UsedAlpha;
                tree = new DMeshAABBTree3(surface, true);
            }
            Func<Vector3d, double> f = SizeFunction;
            field = (Vector3d p, out bool invalid) => {
                invalid = false;
                double s = double.PositiveInfinity;
                if (uniform) s = h;
                if (graded) {
                    int tid = tree.FindNearestTriangle(p);
                    double d = tid < 0 ? 0 : Math.Sqrt(MeshQueries.TriDistanceSqr(surface, tid, p));
                    s = Math.Min(s, h0 + g * d);
                }
                if (user) {
                    double u = f(p);
                    if (double.IsNaN(u) || u <= 0 || double.IsInfinity(u)) invalid = true;     // no size from the function here; the other sources stay
                    else s = Math.Min(s, u);
                }
                return s;
            };
            return true;
        }

        /// <summary>
        /// The refinement of the inside cells (label 1) of a Delaunay3 whose inside / outside facets are the surface. The surface is
        /// never changed: a point is kept only if every cell its insertion removes is inside (then no surface facet is in the
        /// cavity's interior, so all of them stay facets, and the new cells are inside); otherwise the insertion is undone.
        /// Labels are exact, so this test is exact.
        /// </summary>
        internal sealed class Refiner
        {
            readonly Delaunay3 dt;
            readonly double floor, rho, size, sliver;
            readonly int maxPoints;
            readonly Func<bool> cancel;
            readonly Queue<(int cell, int stamp)> tetQueue = new Queue<(int, int)>();
            internal readonly Dictionary<string, long> Counts = new Dictionary<string, long>();
            readonly List<int> removed = new List<int>(), added = new List<int>();
            int inserted;
            readonly int firstSteiner;
            /// <summary> stop after this many trial insertions (the work bound); infinity = none </summary>
            internal double TrialLimit = double.PositiveInfinity;
            internal bool WorkLimitHit;
            /// <summary> size control: the wanted edge length at a point (null = none), its lower clamp MinSize; see MeshTetrahedralization.SizeFunction </summary>
            internal SizeField SizeAt;
            internal double MinSize;
            internal long InvalidTets;
            /// <summary> trial insertions a point inserted for the size alone adds to the work bound (like a wrap vertex); 0 = none </summary>
            internal double TrialsPerSizeSplit;
            // the trials of points inserted for the size alone follow the volume (bounded by the packing, the floor, and MaxSteinerPoints); each such point
            // raises the work bound by TrialsPerSizeSplit, as a wrap vertex does (measured: at most 10 trials per such point)
            long sizeSplits;
            bool sizeCause;
            // a tet is too large if its circumradius exceeds 0.75 h. The regular tet of edge h has circumradius 0.612 h, but the refined tets are not regular:
            // measured (17.12) the mean edge of the result is 0.81 h with 0.612 and about h with 0.75, which gives the count volume / (0.12 h^3) within a factor 1.5
            const double RegularCircumradius = 0.75;

            internal Refiner(Delaunay3 dt, double floor, double rho, double size, double sliver, int maxPoints, Func<bool> cancel)
            {
                this.dt = dt; this.floor = floor; this.rho = rho; this.size = size; this.sliver = sliver; this.maxPoints = maxPoints;
                this.cancel = cancel ?? (() => false);
                firstSteiner = dt.VertexCount;
                foreach (string k in new[] { "tet_splits", "refused_floor", "refused_surface", "trial_insertions", "sliver_free_choice", "peeled", "peeled_in_sweep", "peel_refused_input", "peel_refused_edge" })
                    Counts[k] = 0;
                foreach (int c in dt.CellIndices())
                    if (dt.CellLabel(c) == 1) tetQueue.Enqueue((c, dt.CellStamp(c)));
            }

            void inc(string k) { Counts[k] = Counts[k] + 1; }

            internal TetStatus Run()
            {
                while (tetQueue.Count > 0) {
                    if (cancel()) return TetStatus.Cancelled;
                    if (inserted >= maxPoints) return TetStatus.PointLimitReached;
                    if (Counts["trial_insertions"] >= TrialLimit + TrialsPerSizeSplit * sizeSplits) { WorkLimitHit = true; return TetStatus.PointLimitReached; }
                    var (c, st) = tetQueue.Dequeue();
                    if (!dt.IsCell(c) || dt.CellStamp(c) != st || dt.CellLabel(c) != 1 || !is_bad(c)) continue;
                    split_tet(c);
                }
                if (Input != null) peel_sweep();
                return cancel() ? TetStatus.Cancelled : TetStatus.Success;
            }

            internal bool is_bad(int c) { return shape_bad(c) || field_bad(c); }

            // the wanted size at point p: false = no size here (no valid source); clamped = raised to MinSize
            bool wanted_size(Vector3d p, out double h, out bool clamped, out bool invalid)
            {
                h = SizeAt(p, out invalid);                  // invalid: SizeFunction gave no usable value here (counted even if another source gives a size)
                clamped = false;
                if (!(h > 0) || double.IsPositiveInfinity(h)) return false;       // no valid source
                if (h < MinSize) { clamped = true; h = MinSize; }
                return true;
            }

            // the circumradius of the tet exceeds 0.75 times the wanted edge length at its centroid
            bool field_bad(int c)
            {
                if (SizeAt == null) return false;
                Index4i t = dt.GetCell(c);
                Vector3d a = dt.GetVertex(t.a), b = dt.GetVertex(t.b), d = dt.GetVertex(t.c), e = dt.GetVertex(t.d);
                if (!wanted_size((a + b + d + e) * 0.25, out double h, out _, out _)) return false;
                double R = Delaunay3.Circumcenter(a, b, d, e).Distance(a);       // not squared: lim * lim would underflow for inputs of 1e-160 and below
                return !(R <= RegularCircumradius * h);
            }

            internal void CountClamped()
            {
                long n = 0, bad = 0;
                foreach (int c in dt.CellIndices()) {
                    if (dt.CellLabel(c) != 1) continue;
                    Index4i t = dt.GetCell(c);
                    wanted_size((dt.GetVertex(t.a) + dt.GetVertex(t.b) + dt.GetVertex(t.c) + dt.GetVertex(t.d)) * 0.25, out _, out bool clamped, out bool invalid);
                    if (clamped) n++;
                    if (invalid) bad++;
                }
                if (n > 0) Counts["size_clamped_tets"] = n;          // only then: a size that never applied leaves the counts as they are
                InvalidTets = bad;
            }

            // the shape criteria (radius-edge, dihedral, degenerate) and the fixed circumradius bound
            internal bool shape_bad(int c)
            {
                Index4i t = dt.GetCell(c);
                Vector3d a = dt.GetVertex(t.a), b = dt.GetVertex(t.b), d = dt.GetVertex(t.c), e = dt.GetVertex(t.d);
                if (!TetMeshQuality.Measure(a, b, d, e, out double re, out double mind, out _)) return true;   // degenerate
                if (!(re <= rho)) return true;
                if (sliver > 0 && mind < sliver) return true;
                if (size > 0) {
                    Vector3d cc = Delaunay3.Circumcenter(a, b, d, e);
                    if (!(cc.DistanceSquared(a) <= size * size)) return true;
                }
                return false;
            }

            // fixed candidate directions (6 axes, 8 diagonals): no random numbers, so the result is deterministic and scales by 2^k
            static readonly Vector3d[] Directions = make_directions();
            static Vector3d[] make_directions()
            {
                var l = new List<Vector3d>();
                for (int i = 0; i < 3; ++i) for (int sgn = -1; sgn <= 1; sgn += 2) { var v = Vector3d.Zero; v[i] = sgn; l.Add(v); }
                double k = 1 / Math.Sqrt(3);
                for (int m = 0; m < 8; ++m) l.Add(new Vector3d((m & 1) != 0 ? k : -k, (m & 2) != 0 ? k : -k, (m & 4) != 0 ? k : -k));
                return l.ToArray();
            }
            static readonly double[] Rings = { 0.3, 0.6 };

            readonly List<Vector3d> candidates = new List<Vector3d>();

            // the smallest dihedral angle of the cells added by the last insertion
            double added_min_dihedral()
            {
                double q = 180;
                foreach (int nc in added) {
                    Index4i t = dt.GetCell(nc);
                    if (!TetMeshQuality.Measure(dt.GetVertex(t.a), dt.GetVertex(t.b), dt.GetVertex(t.c), dt.GetVertex(t.d), out _, out double mind, out _)) return 0;
                    q = Math.Min(q, mind);
                }
                return q;
            }

            // tentative insertion: Inserted and every removed cell inside (else undone); the new cells stay
            bool try_insert(Vector3d p, int hint, out bool surface)
            {
                surface = false;
                if (double.IsNaN(p.x) || double.IsNaN(p.y) || double.IsNaN(p.z)) return false;
                removed.Clear(); added.Clear();
                if (dt.Insert(p, hint, floor, out _, removed, added) != Delaunay3.InsertResult.Inserted) return false;
                Counts["trial_insertions"]++;
                foreach (int k in removed)        // dead, but their labels stay until the ids are reused
                    if (dt.CellLabel(k) != 1) { dt.UndoLastInsert(); surface = true; return false; }
                return true;
            }

            void split_tet(int c)
            {
                sizeCause = SizeAt != null && !shape_bad(c);
                Index4i t = dt.GetCell(c);
                Vector3d cc = dt.Circumcenter(c);
                double R = cc.Distance(dt.GetVertex(t.a));
                // the circumcentre first; if it is refused by the floor or its new tets have a sliver: the rings around it (points
                // towards the centroid were tried for tets refused at the surface and made the result worse on the procedural set)
                int best = -1; double bestQ = -1; bool anySurface = false;
                candidates.Clear();
                candidates.Add(cc);
                for (int k = 0; k < candidates.Count; ++k) {
                    bool ok = try_insert(candidates[k], c, out bool surface);
                    anySurface |= surface;
                    if (k == 0) {
                        if (sliver > 0 && !surface)
                            foreach (double ring in Rings) foreach (Vector3d u in Directions) candidates.Add(cc + (ring * R) * u);
                    }
                    if (!ok) continue;
                    double q = sliver > 0 ? added_min_dihedral() : 180;
                    if (q >= sliver) { accept(k > 0); return; }
                    if (q > bestQ) { bestQ = q; best = k; }
                    dt.UndoLastInsert();
                }
                if (best >= 0 && try_insert(candidates[best], c, out _)) { accept(best > 0); return; }
                if (Input != null && !sizeCause && try_peel(c)) return;   // a tet too large is not peeled away
                inc(anySurface ? "refused_surface" : "refused_floor");
            }

            /// <summary> (peeling) the input the surface must keep enclosing; null = no peeling </summary>
            internal DMesh3 Input;
            DMeshAABBTree3 inputTree;

            /// <summary> (tests) TryPeel of cell c: true if it was moved outside </summary>
            internal bool TryPeel(int c) { return try_peel(c); }

            // after the queue: peel the remaining bad inside tets with two surface facets until none can be (a peel exposes
            // neighbours, which can become peelable in turn)
            void peel_sweep()
            {
                bool changed = true;
                while (changed && !cancel()) {
                    changed = false;
                    foreach (int c in dt.CellIndices())
                        if (dt.CellLabel(c) == 1 && shape_bad(c) && try_peel(c)) { changed = true; inc("peeled_in_sweep"); }
                }
            }

            bool try_peel(int c)
            {
                int i1 = -1, i2 = -1, nsurf = 0;
                for (int i = 0; i < 4; ++i) {
                    int nb = dt.Neighbour(c, i);
                    if (nb < 0 || dt.CellLabel(nb) != 1) { nsurf++; if (i1 < 0) i1 = i; else i2 = i; }
                }
                if (nsurf != 2) return false;
                int p = dt.CellVertex(c, i1), q = dt.CellVertex(c, i2);
                // the new surface edge (p, q) must not be on the surface already (else three or four surface facets around it)
                dt.IncidentCells(p, peelStar);
                foreach (int k in peelStar) {
                    if (dt.VertexIndexIn(k, q) < 0) continue;
                    for (int i = 0; i < 4; ++i) {
                        int w = dt.CellVertex(k, i);
                        if (w == p || w == q) continue;          // facet opposite w contains p and q
                        int nb = dt.Neighbour(k, i);
                        int lk = k == c ? 0 : dt.CellLabel(k), ln = nb < 0 ? 0 : nb == c ? 0 : dt.CellLabel(nb);
                        if (k == c || nb == c) continue;         // c's own facets around (p, q) become surface or vanish
                        if (lk != ln) { inc("peel_refused_edge"); return false; }
                    }
                }
                Index4i t = dt.GetCell(c);
                if (Input.TriangleCount > 0 && inputTree == null) inputTree = new DMeshAABBTree3(Input, true);
                if (inputTree != null && TetTouchesMesh(inputTree, dt.GetVertex(t.a), dt.GetVertex(t.b), dt.GetVertex(t.c), dt.GetVertex(t.d))) {
                    inc("peel_refused_input"); return false;
                }
                dt.SetCellLabel(c, 0);
                inc("peeled");
                return true;
            }
            readonly List<int> peelStar = new List<int>();

            /// <summary>
            /// does the closed tet (a, b, c, d) share a point with a triangle of the tree's mesh? Exact: a triangle meets one of the
            /// four faces (ExactTriangleIntersection, degenerate triangles as segments), or, meeting none, lies inside (one vertex in
            /// the closed tet). Only reads the tree.
            /// </summary>
            internal static bool TetTouchesMesh(DMeshAABBTree3 tree, Vector3d a, Vector3d b, Vector3d c, Vector3d d)
            {
                DMesh3 mesh = tree.Mesh;
                var box = new AxisAlignedBox3d(a); box.Contain(b); box.Contain(c); box.Contain(d);
                Triangle3d[] faces = { new Triangle3d(b, c, d), new Triangle3d(a, d, c), new Triangle3d(a, b, d), new Triangle3d(a, c, b) };
                bool hit = false;
                Func<AxisAlignedBox3d, bool> touch = (bx) => bx.Min.x <= box.Max.x && bx.Max.x >= box.Min.x && bx.Min.y <= box.Max.y && bx.Max.y >= box.Min.y
                    && bx.Min.z <= box.Max.z && bx.Max.z >= box.Min.z;
                int o = ExactPredicates.Orient3d(a, b, c, d);
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (bx, depth) => !hit && touch(bx),
                    NextTriangleF = (tid) => {
                        if (hit) return;
                        Index3i it = mesh.GetTriangle(tid);
                        var T = new Triangle3d(mesh.GetVertex(it.a), mesh.GetVertex(it.b), mesh.GetVertex(it.c));
                        var tb = new AxisAlignedBox3d(T.V0); tb.Contain(T.V1); tb.Contain(T.V2);
                        if (!touch(tb)) return;
                        for (int k = 0; k < 4 && !hit; ++k)
                            if (ExactTriangleIntersection.Intersects(ref faces[k], ref T)) hit = true;
                        // inside: on the side of each face where its opposite vertex is (Orient3d(face, opposite) = -o)
                        if (!hit && o != 0 && ExactPredicates.Orient3d(b, c, d, T.V0) * o <= 0 && ExactPredicates.Orient3d(a, d, c, T.V0) * o <= 0
                            && ExactPredicates.Orient3d(a, b, d, T.V0) * o <= 0 && ExactPredicates.Orient3d(a, c, b, T.V0) * o <= 0) hit = true;
                    }
                });
                return hit;
            }

            void accept(bool moved)
            {
                inserted++;
                if (sizeCause) sizeSplits++;
                inc("tet_splits");
                if (moved) inc("sliver_free_choice");
                foreach (int nc in added) tetQueue.Enqueue((nc, dt.CellStamp(nc)));
            }

            internal long CountBadInside()
            {
                long n = 0;
                var byWrapVertices = new long[5];
                foreach (int c in dt.CellIndices())
                    if (dt.CellLabel(c) == 1 && is_bad(c)) {
                        n++;
                        int k = 0;
                        for (int i = 0; i < 4; ++i) if (dt.CellVertex(c, i) < firstSteiner) k++;
                        byWrapVertices[k]++;
                        int f = 0;
                        for (int i = 0; i < 4; ++i) { int nb = dt.Neighbour(c, i); if (nb < 0 || dt.CellLabel(nb) != 1) f++; }
                        string key = "bad_with_" + f + "_surface_facets";
                        Counts[key] = (Counts.TryGetValue(key, out long x) ? x : 0) + 1;
                    }
                for (int k = 0; k <= 4; ++k) Counts["bad_with_" + k + "_wrap_vertices"] = byWrapVertices[k];
                return n;
            }

            internal TetMesh3 Extract()
            {
                var tm = new TetMesh3();
                var map = new Dictionary<int, int>();
                Func<int, int> vid = (v) => {
                    if (!map.TryGetValue(v, out int id)) { id = tm.Vertices.Count; tm.Vertices.Add(dt.GetVertex(v)); map[v] = id; }
                    return id;
                };
                foreach (int c in dt.CellIndices()) {
                    if (dt.CellLabel(c) != 1) continue;
                    Index4i q = dt.GetCell(c);
                    tm.Tets.Add(new Index4i(vid(q.a), vid(q.b), vid(q.c), vid(q.d)));
                    for (int i = 0; i < 4; ++i) {
                        int n = dt.Neighbour(c, i);
                        if (n >= 0 && dt.CellLabel(n) == 1) continue;
                        Index3i f = dt.FacetVertices(c, i);
                        tm.BoundaryTriangles.Add(new Index3i(vid(f.a), vid(f.b), vid(f.c)));
                    }
                }
                return tm;
            }
        }
    }
}
