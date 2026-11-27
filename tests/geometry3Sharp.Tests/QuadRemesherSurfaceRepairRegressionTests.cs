using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1689, public API only (they fail on master): the default path (PreserveCreases off) of these fixtures dropped one or two faces and left holes; the neighbour order from
    /// surface directions closes them. The 9 configurations of the 72 that still drop a face (the follow-up of the issue) are pinned in <see cref="QuadRemesherSurfaceRepairSweepTests"/>.
    /// </summary>
    public class QuadRemesherSurfaceRepairRegressionTests
    {
        internal static QuadRemesher RunDefault(DMesh3 m, double s, bool parallel = true)
        {
            var qr = new QuadRemesher(m) { TargetEdgeLength = s, Parallel = parallel };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            return qr;
        }

        static void AssertClosedSphere(QuadRemesher qr, string what)
        {
            Assert.Equal(0, qr.DroppedFaces);
            DPolyMesh r = qr.Result;
            Assert.True(r.Validate().IsValid, what);
            PolyMeshMetrics pm = PolyMeshMetrics.Compute(r);
            Assert.True(pm.IsManifold, what);
            Assert.Equal(0, pm.BoundaryVertices);
            Assert.Equal(2, pm.EulerCharacteristic);
            Assert.True(pm.MaxSides <= 6, what);
            for (int v = 0; v < r.VertexCount; ++v) Assert.True(r.GetVertex(v).IsFinite, what);
        }

        [Theory]
        [InlineData("sharp-cylinder", 14)]
        [InlineData("sharp-cylinder", 30)]
        [InlineData("gear", 10)]
        [InlineData("gear", 32)]
        [InlineData("gear", 35)]
        [InlineData("gear", 50)]
        [InlineData("gear", 60)]
        [InlineData("l-profile", 10)]
        [InlineData("l-profile", 40)]
        public void DefaultPath_ClosesTheHolesOfAFold(string name, int passes)
        {
            QuadRemesher qr = RunDefault(QuadRemesherClosedOutputTests.Shape(name, passes), QuadRemesherClosedOutputTests.TargetOf(name));
            AssertClosedSphere(qr, name + "/" + passes);
        }

        [Fact]
        public void Reuse_ComputingTwiceOnOneInstance_GivesTheSameResult()
        {
            var qr = new QuadRemesher(QuadRemesherClosedOutputTests.Shape("sharp-cylinder", 14)) { TargetEdgeLength = QuadRemesherClosedOutputTests.TargetOf("sharp-cylinder") };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            DPolyMesh first = qr.Result;
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            Assert.Equal(0, qr.DroppedFaces);
            QuadRemesherClosedOutputTests.AssertSame(first, qr.Result);
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial_AndRepeatedRunsAgree()
        {
            DMesh3 m = QuadRemesherClosedOutputTests.Shape("gear", 35);
            double s = QuadRemesherClosedOutputTests.TargetOf("gear");
            DPolyMesh serial = RunDefault(m, s, false).Result;
            QuadRemesherClosedOutputTests.AssertSame(serial, RunDefault(m, s, true).Result);
            QuadRemesherClosedOutputTests.AssertSame(serial, RunDefault(m, s, true).Result);
            QuadRemesherClosedOutputTests.AssertSame(serial, RunDefault(m, s, false).Result);
        }

        [Fact]
        public void UnderSimilarities_TheScaledByAPowerOfTwoResultIsTheSameBitForBit_AndTheOthersAreClosed()
        {
            DMesh3 m = QuadRemesherClosedOutputTests.Shape("sharp-cylinder", 14);
            double s = QuadRemesherClosedOutputTests.TargetOf("sharp-cylinder");
            DPolyMesh reference = RunDefault(m, s).Result;
            foreach (Similarity sim in Similarity.All()) {
                QuadRemesher qr = RunDefault(Invariance.Transformed(m, sim), s * sim.Scale);
                AssertClosedSphere(qr, sim.Name);
                if (sim.IsExact) QuadRemesherClosedOutputTests.AssertSame(reference, qr.Result, sim.Scale);
            }
        }
    }
}
