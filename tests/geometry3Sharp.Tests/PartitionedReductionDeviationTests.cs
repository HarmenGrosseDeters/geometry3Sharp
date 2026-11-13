using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PartitionedReduction with Reducer.MaxDeviation (issue #747): every region and the last pass must measure from the
    /// mesh given. Each Reducer used to take the mesh it starts on as its reference (the region of the second round, the
    /// last pass's already reduced mesh), so the deviations of the rounds added up past MaxDeviation.
    /// </summary>
    public class PartitionedReductionDeviationTests
    {
        [Fact]
        public void MaxDeviation_FromTheOriginalMesh_OverAllRounds()
        {
            DMesh3 sphere = SimplifyWithinRetryTests.BumpySphere(16);
            double d = 5e-3;
            var pr = new PartitionedReduction() { Slabs = 8, Configure = (r) => { r.MaxDeviation = d; r.AreaWeightedQuadrics = false; } };
            // (a quadric budget far above d: only MaxDeviation stops the collapses)
            DMesh3 m = pr.Reduce(sphere, 1.0);
            Assert.True(m.TriangleCount < sphere.TriangleCount * 0.9, $"{m.TriangleCount} of {sphere.TriangleCount}");
            var hd = new MeshHausdorffDistance(m, sphere) { Symmetric = false, Tolerance = 0.02 * d };
            hd.Compute();
            // (the upper bound is sharp up to the bound's tolerance: every collapse was certified within d, the bound adds at most
            // its tolerance; the old code gave 1.54 to 1.56 d. Unlike SimplifyWithinRetryTests.AssertCertified, which checks against
            // maxDistance itself: SimplifyWithin keeps its margin inside the operation, MaxDeviation 0.8 maxDistance; here only the
            // estimate can overshoot the certified d)
            Assert.True(hd.Converged && hd.UpperBound <= d + hd.Tolerance, $"deviation {hd.LowerBound / d} to {hd.UpperBound / d} times MaxDeviation, converged {hd.Converged}");
        }
    }
}
