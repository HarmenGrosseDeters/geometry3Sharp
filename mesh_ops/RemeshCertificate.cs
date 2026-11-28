// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// A check of a remesh result against its input (docs/design/CERTIFIED_REMESHING.md section 10, item 2): reads both meshes,
    /// changes neither. Each field says how it was decided:
    ///   - Closed, Manifold: combinatorial (IsClosed, CheckValidity), exact;
    ///   - SelfIntersectingPairs: MeshRepairMetrics.SelfIntersections, exact (ExactTriangleIntersection);
    ///   - HausdorffResultToInput / HausdorffInputToResult: MeshHausdorffDistance with CertifiedUpper, one direction each: an upper
    ///     bound that is never below the exact distance, rounding included; HausdorffConverged if both reached the tolerance;
    ///   - components, genus, boundary loops before and after: MeshTopologySummary, combinatorial, exact (the genus per
    ///     component as a multiset; the boundary loops as totals only);
    ///   - NestingPreserved: the closed components' nesting depths and volume signs (as multisets) are the same, read by winding
    ///     numbers at interior samples (MeshPostRepairCleanup's reading; floating point: checked, not exact);
    ///   - Defects, NotCleanReason: MeshRobustRepair.FindDefects of the result, without NotClosed if the input is open too
    ///     (the result of an open input keeps its boundary).
    /// WithinBand: both Hausdorff upper bounds at most the band given (false without one). Passed: closed if the input is,
    /// manifold, no self-intersection, no defect, both Hausdorff computations converged, no topology change, the nesting
    /// preserved and, with a band, within it. An empty input or result fails (no Hausdorff distance). Scale invariant
    /// (relative tolerances; the 2^k scales give the same answer bit for bit).
    /// </summary>
    public class RemeshCertificate
    {
        public bool Passed;
        public bool Closed, Manifold;
        public int SelfIntersectingPairs;
        /// <summary> certified upper bounds of the one-sided Hausdorff distances (result to input, input to result) </summary>
        public double HausdorffResultToInput, HausdorffInputToResult;
        public bool HausdorffConverged;
        public int ComponentsBefore, ComponentsAfter, BoundaryLoopsBefore, BoundaryLoopsAfter;
        public double GenusBefore, GenusAfter;
        /// <summary> what changed in the topology (components, handles, boundary loops) from the input to the result </summary>
        public MeshTopologySummary.Changes TopologyChanges;
        /// <summary>
        /// the closed components' nesting depths and volume signs are the same as multisets. NOT EXACT: the depths are read by
        /// floating-point winding numbers at sample points slightly inside each component (a vote, as
        /// MeshPostRepairCleanup.FindInvertedComponents reads them), so this is a check, not a certificate; the volume signs are exact
        /// </summary>
        public bool NestingPreserved;
        /// <summary>
        /// both Hausdorff upper bounds within the band (false if no band was given). Conservative: the upper bounds are computed to
        /// 1e-4 of the band, so a result within the band by less than that can read false; never true for a result outside it
        /// </summary>
        public bool WithinBand;
        /// <summary> MeshRobustRepair.FindDefects of the result (NotClosed masked if the input is open) </summary>
        public MeshRobustRepair.Defects Defects;
        /// <summary> MeshRobustRepair.DescribeDefects(Defects): "" if clean </summary>
        public string NotCleanReason = "";

        /// <summary>
        /// checks result against input; maxDeviation: the band (0: none; WithinBand false, Passed without it). The Hausdorff
        /// distances are computed to 1e-4 of the band, or of the input's diagonal without one
        /// </summary>
        public static RemeshCertificate Compute(DMesh3 input, DMesh3 result, double maxDeviation = 0)
        {
            var c = new RemeshCertificate();
            if (input == null || result == null)
                throw new ArgumentNullException(input == null ? nameof(input) : nameof(result));
            c.Closed = result.IsClosed();
            c.Manifold = result.CheckValidity(false, FailMode.ReturnOnly);     // (no bowtie vertices)
            c.SelfIntersectingPairs = result.TriangleCount > 0 ? MeshRepairMetrics.SelfIntersections(result) : 0;
            // (the self-intersections are counted above: FindDefects need not search them again)
            c.Defects = MeshRobustRepair.FindDefects(result, false);
            if (c.SelfIntersectingPairs > 0) c.Defects |= MeshRobustRepair.Defects.SelfIntersections;
            if (input.TriangleCount > 0 && !input.IsClosed()) c.Defects &= ~MeshRobustRepair.Defects.NotClosed;
            c.NotCleanReason = MeshRobustRepair.DescribeDefects(c.Defects);

            if (input.TriangleCount > 0 && result.TriangleCount > 0) {
                // (an infinite band: the tolerance of no band; WithinBand then holds for any finite distance)
                double tol = 1e-4 * (maxDeviation > 0 && MathUtil.IsFinite(maxDeviation) ? maxDeviation : input.CachedBounds.DiagonalLength);
                var fwd = new MeshHausdorffDistance(result, input) { Symmetric = false, CertifiedUpper = true, Tolerance = tol };
                var bwd = new MeshHausdorffDistance(input, result) { Symmetric = false, CertifiedUpper = true, Tolerance = tol };
                bool okF = fwd.Compute(), okB = bwd.Compute();
                c.HausdorffResultToInput = okF ? fwd.UpperBound : double.PositiveInfinity;
                c.HausdorffInputToResult = okB ? bwd.UpperBound : double.PositiveInfinity;
                c.HausdorffConverged = okF && okB && fwd.Converged && bwd.Converged;
            } else {
                c.HausdorffResultToInput = c.HausdorffInputToResult = double.PositiveInfinity;
            }

            var before = MeshTopologySummary.Compute(input);
            var after = MeshTopologySummary.Compute(result);
            c.ComponentsBefore = before.Components; c.ComponentsAfter = after.Components;
            c.GenusBefore = before.Genus; c.GenusAfter = after.Genus;
            c.BoundaryLoopsBefore = before.BoundaryLoops; c.BoundaryLoopsAfter = after.BoundaryLoops;
            c.TopologyChanges = MeshTopologySummary.Compare(before, after);
            // (Compare also compares the genus per component: a handle moved between components is a change)

            var nb = MeshPostRepairCleanup.NestingDepths(input).Select(x => (x.depth, x.volumeSign)).OrderBy(x => x).ToList();
            var na = MeshPostRepairCleanup.NestingDepths(result).Select(x => (x.depth, x.volumeSign)).OrderBy(x => x).ToList();
            c.NestingPreserved = nb.SequenceEqual(na);

            c.WithinBand = maxDeviation > 0 && c.HausdorffResultToInput <= maxDeviation && c.HausdorffInputToResult <= maxDeviation;
            c.Passed = c.Closed == input.IsClosed() && c.Manifold && c.SelfIntersectingPairs == 0 && c.Defects == MeshRobustRepair.Defects.None
                && c.HausdorffConverged && c.TopologyChanges == MeshTopologySummary.Changes.None && c.NestingPreserved
                && (!(maxDeviation > 0) || c.WithinBand);
            return c;
        }
    }
}
