using System;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> #638: the defect cap of the refinement loop counts split attempts; it must still stop a refinement that works for ever
    /// (apart from QualityMesh2NarrowChannelTests, whose tests also compile against the library before the change) </summary>
    public class QualityMesh2RefinementCapTests
    {
        /// <summary> the cap still stops a real livelock: with a test hook a bad triangle is queued again instead of split, so refinement
        /// does work for ever without progress, and the run must end Failed (RefinementLimit), not hang and not report the Steiner limit </summary>
        [Fact]
        public void Livelock_IsStoppedByTheDefectCap()
        {
            var input = QualityMesh2NarrowChannelTests.Channel(0, Math.Pow(2, -10), 100);
            QualityMesh2.Refiner.StallForTests = true;
            try {
                var o = new QualityMesh2Candidate().Run(input);
                Assert.Equal(Mesh2Status.Failed, o.Status);
                Assert.Equal("RefinementLimit", o.FailureKind);
            } finally { QualityMesh2.Refiner.StallForTests = false; }
            // and without the hook the same input meshes
            QualityMesh2NarrowChannelTests.Run(input, "w = 2^-10, limit 100");
        }
    }
}
