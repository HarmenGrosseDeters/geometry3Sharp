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
    ///   - components, genus, boundary loops before and after: MeshTopologySummary, combinatorial, exact;
    ///   - NestingPreserved: the closed components' nesting depths and volume signs (as multisets) are the same, read by winding
    ///     numbers at interior samples (MeshPostRepairCleanup's reading; floating point: checked, not exact);
    ///   - NotCleanReason: MeshRobustRepair.NotCleanReason of the result ("" if clean).
    /// WithinBand: both Hausdorff upper bounds at most the band given (false without one). Passed: all of them (with a band:
    /// within it). Scale invariant (relative tolerances; the 2^k scales give the same answer bit for bit).
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
        /// <summary> both Hausdorff upper bounds within the band (false if no band was given) </summary>
        public bool WithinBand;
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
            c.NotCleanReason = MeshRobustRepair.NotCleanReason(result);

            if (input.TriangleCount > 0 && result.TriangleCount > 0) {
                double tol = 1e-4 * (maxDeviation > 0 ? maxDeviation : input.CachedBounds.DiagonalLength);
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
            if (c.ComponentsBefore == c.ComponentsAfter && c.TopologyChanges == MeshTopologySummary.Changes.None) {
                // genus per component too (the totals can agree while a handle moved), as sorted multisets
                var gb = before.ComponentList.Select(x => x.Genus).OrderBy(x => x).ToList();
                var ga = after.ComponentList.Select(x => x.Genus).OrderBy(x => x).ToList();
                if (!gb.SequenceEqual(ga)) c.TopologyChanges |= MeshTopologySummary.Changes.HandlesClosed | MeshTopologySummary.Changes.HandlesCreated;
            }

            var nb = MeshPostRepairCleanup.NestingDepths(input).Select(x => (x.depth, x.volumeSign)).OrderBy(x => x).ToList();
            var na = MeshPostRepairCleanup.NestingDepths(result).Select(x => (x.depth, x.volumeSign)).OrderBy(x => x).ToList();
            c.NestingPreserved = nb.SequenceEqual(na);

            c.WithinBand = maxDeviation > 0 && c.HausdorffResultToInput <= maxDeviation && c.HausdorffInputToResult <= maxDeviation;
            c.Passed = c.Closed == input.IsClosed() && c.Manifold && c.SelfIntersectingPairs == 0 && c.NotCleanReason == ""
                && c.HausdorffConverged && c.TopologyChanges == MeshTopologySummary.Changes.None && c.NestingPreserved
                && (!(maxDeviation > 0) || c.WithinBand);
            return c;
        }
    }
}
