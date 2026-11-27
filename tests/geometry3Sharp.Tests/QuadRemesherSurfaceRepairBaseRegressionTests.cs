using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1689 / #1696 on the public entry points only (they compile against the library of master, where each of them fails): the default path of these fixtures dropped a face and left a hole.
    /// gear/35: a fold whose neighbour orders disagree; gear/22: two links that cross on the surface; gear/14: a thin face across the fold that the area test rejected.
    /// </summary>
    public class QuadRemesherSurfaceRepairBaseRegressionTests
    {
        static void AssertClosedSphereWithoutDroppedFaces(string name, int passes)
        {
            var qr = new QuadRemesher(QuadRemesherClosedOutputTests.Shape(name, passes)) { TargetEdgeLength = QuadRemesherClosedOutputTests.TargetOf(name) };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(0, qr.DroppedFaces);
            PolyMeshMetrics pm = PolyMeshMetrics.Compute(qr.Result);
            Assert.Equal(0, pm.BoundaryVertices);
            Assert.Equal(2, pm.EulerCharacteristic);
            Assert.True(pm.IsManifold);
        }

        [Fact]
        public void TheFoldOfAGearTooth_GivesAClosedOutput() { AssertClosedSphereWithoutDroppedFaces("gear", 35); }

        [Fact]
        public void TwoLinksThatCrossOnTheSurfaceOfAGear_GiveAClosedOutput() { AssertClosedSphereWithoutDroppedFaces("gear", 22); }

        [Fact]
        public void AThinFaceAcrossTheFoldOfAGear_GivesAClosedOutput() { AssertClosedSphereWithoutDroppedFaces("gear", 14); }

        [Fact]
        public void TheLProfileFixtures_GiveAClosedOutput() { AssertClosedSphereWithoutDroppedFaces("l-profile", 35); AssertClosedSphereWithoutDroppedFaces("l-profile", 60); }
    }
}
