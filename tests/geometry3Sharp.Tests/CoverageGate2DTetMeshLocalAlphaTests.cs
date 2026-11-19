// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Branches of MeshTetrahedralization (the local alpha, 17.13) that no other test reached: the refinement that has nothing to refine
    /// (alpha is not above 3 offsets) and the retry whose second run is kept.
    /// </summary>
    public class CoverageGate2DTetMeshLocalAlphaTests
    {
        static DMesh3 Box() =>
            new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();

        [Fact]
        public void LocalAlpha_WithAlphaNotAboveThreeOffsets_HasNothingToRefine_AndStillSucceeds()
        {
            // alpha = diag / 40 and offset = diag / 60: 3 offsets = diag / 20 > alpha, so the smallest local alpha (not below 3 offsets) is not below the uniform one
            DMesh3 input = Box();
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 40, RelativeOffset = 60, MaxSurfaceDeviation = input.CachedBounds.DiagonalLength / 150 };
            Assert.True(tm.Compute(), tm.Status.ToString());
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status);
            Assert.True(3 * tm.UsedOffset >= tm.UsedAlpha);
            // the local pass ran (the counts exist) but no pass refined anything: no field, no fine samples, the wrap is the uniform one
            Assert.Equal(0, tm.Counts["local_alpha_passes"]);
            Assert.Equal(0, tm.Counts["local_alpha_fine_samples"]);
            Assert.Null(tm.Wrap.AlphaField);
            Assert.False(tm.Counts.ContainsKey("local_alpha_retry"));
            TetMeshingStage2Tests.Invariants(tm);
            Assert.Equal(input.CachedBounds.DiagonalLength / 40, tm.UsedAlpha, 12);
        }

        [Fact]
        public void MissedDihedralTarget_SmootherFieldGivesALargerSmallestAngle_ItIsKept()
        {
            // the comb at alpha diag / 20, deviation diag / 100, a 20 degree dihedral target one improve pass cannot reach: the first run's smallest dihedral
            // angle is 8.3 degrees, the run with half the gradation slope gives 9.7 (measured): the second run is kept
            DMesh3 input = TetMeshLocalAlphaTests.Comb();
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 20, MaxSurfaceDeviation = input.CachedBounds.DiagonalLength / 100, SliverDihedralDegrees = 20, ImprovePasses = 1, RescuePasses = 0 };
            Assert.True(tm.Compute(), tm.Status.ToString());
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tm.Status);
            Assert.Equal(1, tm.Counts["local_alpha_retry"]);
            Assert.Equal(1, tm.Counts["local_alpha_retry_kept"]);
            Assert.Equal(0.25, tm.GradationFirst); Assert.Equal(0.125, tm.GradationRetry);
            Assert.False(tm.Targets.MinDihedralMet);                                         // (the target stays missed: reported, never silent)
            Assert.False(double.IsNaN(tm.RetryFirstWorstMinDihedral));
            Assert.True(tm.Targets.WorstMinDihedral > tm.RetryFirstWorstMinDihedral, tm.Targets.WorstMinDihedral + " against the first run's " + tm.RetryFirstWorstMinDihedral);
            // the kept result is the second run's: valid, closed and manifold, and its targets belong to it
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
            var check = TetMeshQuality.CheckTargets(tm.Result, 20, tm.MaxRadiusEdgeRatio);
            Assert.Equal(check.WorstMinDihedral, tm.Targets.WorstMinDihedral);
            Assert.True(tm.Result.ToSurfaceMesh().IsClosed());
        }
    }
}
