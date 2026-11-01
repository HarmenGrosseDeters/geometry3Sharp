using System;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding keeps small features of the exact result (MeshRoundingRepair.ExactVertexF): a triangle below the
    /// degenerate area whose vertices are exact, at least one not an input vertex, and that is well-shaped (small,
    /// not a needle or a cap) is not a rounding defect and is left as the arrangement gives it (Finding 1 of the
    /// Boolean property tests, ExactBooleanPropertyFindings). Needles, caps, triangles with a rounded vertex and
    /// triangles of input vertices only are repaired as before.
    /// </summary>
    public class SafeRoundingExactFeatureTests
    {
        static readonly double T = Math.Pow(2, -20);

        // the reproduction of Finding 1: the unit cube with its edge (0,0,0)-(1,0,0) split at (2^-20, 0, 0), and a slab
        // whose bottom plane z = 2^-20 cuts the two slivers at the split into pieces of area about 2^-41
        static DMesh3 CubeWithSplitEdge()
        {
            DMesh3 a = new BoxCase(new IntBox(0, 0, 0, 1, 1, 1), new IntBox(0, 0, 0, 1, 1, 1)).MeshA();
            int e = a.FindEdge(0, 1);
            Assert.Equal(MeshResult.Ok, a.SplitEdge(e, out DMesh3.EdgeSplitInfo info, 0.5));
            a.SetVertex(info.vNew, new Vector3d(T, 0, 0));
            return a;
        }

        // tilt: the bottom of the slab rises to z = (1 + tilt) 2^-20 at y = 2 (a non-planar quad, two triangles), so
        // that the cut points on the cube are not doubles (rounded) unless tilt = 0
        static DMesh3 Slab(double tilt = 0)
        {
            DMesh3 m = new TrivialBox3Generator() {
                Box = new Box3d(new AxisAlignedBox3d(new Vector3d(-1, -1, T), new Vector3d(2, 2, 2)))
            }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToArray()) {
                Vector3d p = m.GetVertex(v);
                if (p.z == T && p.y == 2)
                    m.SetVertex(v, new Vector3d(p.x, p.y, (1 + tilt) * T));
            }
            return m;
        }

        static readonly Dyadic ExpectedSixVolume = Dyadic.Of(6 * (18 - 8 * T));

        static ExactMeshBoolean Union(DMesh3 a, DMesh3 b, bool safeRounding = true)
        {
            var boolean = new ExactMeshBoolean(a, b, Ops.Union) { SafeRounding = safeRounding };
            Assert.True(boolean.Compute(), boolean.Error);
            return boolean;
        }

        // the small exact triangles that the repair keeps are degenerate by the area threshold, nothing else is wrong
        static void AssertOnlyKeptFeatures(ExactMeshBoolean b)
        {
            ResultChecks.AssertClosed(b.Result);
            Assert.Equal(0, b.Rounding.RemainingIntersections);
            Assert.True(b.Rounding.ExactDegenerate > 0);
            Assert.Equal(b.Rounding.ExactDegenerate, b.Rounding.RemainingDegenerate);
            Assert.False(b.RoundingClean);
            Assert.Equal("degenerate triangles", gs.MeshRobustRepair.NotCleanReasons(b.Result));
        }

        [Fact]
        public void ExactSmallFeature_Kept_VolumeExact()
        {
            ExactMeshBoolean b = Union(CubeWithSplitEdge(), Slab());
            Assert.Equal(ExpectedSixVolume, BooleanOracle.ExactSixVolume(b.Result));
            AssertOnlyKeptFeatures(b);
            Assert.Equal(0, b.Rounding.Collapses + b.Rounding.Flips + b.Rounding.Moves + b.Rounding.Zips);
            // the same mesh as without SafeRounding
            Assert.Equal("", Invariance.Difference(Union(CubeWithSplitEdge(), Slab(), false).Result, b.Result));
        }

        [Fact]
        public void RoundedSmallFeature_Repaired()
        {
            // tilted: the pieces have rounded vertices, a rounding defect, repaired to a clean result as before
            ExactMeshBoolean raw = Union(CubeWithSplitEdge(), Slab(1.0 / 3), false);
            Assert.Contains("degenerate", gs.MeshRobustRepair.NotCleanReasons(raw.Result));
            ExactMeshBoolean b = Union(CubeWithSplitEdge(), Slab(1.0 / 3));
            ResultChecks.AssertClean(b.Result);
            Assert.True(b.RoundingClean);
            Assert.Equal(0, b.Rounding.ExactDegenerate);
            Assert.True(b.Rounding.Collapses + b.Rounding.Flips + b.Rounding.Moves + b.Rounding.Zips > 0);
        }

        [Fact]
        public void Similarities_ScalesExact_OthersValid()
        {
            ExactMeshBoolean b0 = Union(CubeWithSplitEdge(), Slab());
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 a = CubeWithSplitEdge(), s = Slab();
                MeshTransforms.PerVertexTransform(a, sim.Apply);
                MeshTransforms.PerVertexTransform(s, sim.Apply);
                ExactMeshBoolean b = Union(a, s);
                ResultChecks.AssertClosed(b.Result, sim.ToString());
                Assert.True(b.Rounding.RemainingIntersections == 0, sim.ToString());
                if (sim.IsExact) {
                    // the same result, scaled, bit for bit
                    DMesh3 back = new DMesh3(b.Result);
                    MeshTransforms.PerVertexTransform(back, sim.Inverse);
                    Assert.True(Invariance.Difference(b0.Result, back) == "", sim.ToString());
                    Assert.Equal(b0.Rounding.ExactDegenerate, b.Rounding.ExactDegenerate);
                    Assert.Equal(ExpectedSixVolume, BooleanOracle.ExactSixVolume(back));
                }
            }
        }

        [Fact]
        public void ReusedInstance_EqualsFreshInstance()
        {
            var b = new ExactMeshBoolean(CubeWithSplitEdge(), Slab(1.0 / 3), Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(0, b.Rounding.ExactDegenerate);
            // then the case with kept features on the same instance, and again
            b.MeshA = CubeWithSplitEdge(); b.MeshB = Slab();
            for (int k = 0; k < 2; ++k) {
                Assert.True(b.Compute(), b.Error);
                ExactMeshBoolean fresh = Union(CubeWithSplitEdge(), Slab());
                Assert.Equal("", Invariance.Difference(fresh.Result, b.Result));
                Assert.Equal(fresh.Rounding.ExactDegenerate, b.Rounding.ExactDegenerate);
            }
        }

        [Fact]
        public void Repeated_AndSerial_Identical()
        {
            ExactMeshBoolean first = Union(CubeWithSplitEdge(), Slab());
            for (int run = 0; run < 3; ++run)
                Assert.Equal("", Invariance.Difference(first.Result, Union(CubeWithSplitEdge(), Slab()).Result));
            gParallel.SerialOnThisThread = true;
            try {
                ExactMeshBoolean serial = Union(CubeWithSplitEdge(), Slab());
                Assert.Equal("", Invariance.Difference(first.Result, serial.Result));
                Assert.Equal(first.Rounding.ExactDegenerate, serial.Rounding.ExactDegenerate);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        // ---------------------------------------------------------------- MeshRoundingRepair alone

        // the result without SafeRounding: closed, with small well-shaped exact triangles
        static DMesh3 RawResult() => Union(CubeWithSplitEdge(), Slab(), false).Result;

        [Fact]
        public void Repair_ExactVertexF_EachConditionMatters()
        {
            // exact, not all fixed, well-shaped: kept
            DMesh3 m = RawResult();
            var keep = new MeshRoundingRepair(m) { SharedPositionsTouch = true, ExactVertexF = v => true };
            Assert.False(keep.Apply());
            Assert.True(keep.ExactDegenerate > 0);
            Assert.Equal(keep.InitialDegenerate, keep.RemainingDegenerate);
            Assert.Equal(0, keep.Collapses + keep.Flips + keep.Moves + keep.Zips);
            Assert.Equal("", Invariance.Difference(RawResult(), m));

            // without ExactVertexF (as before): repaired
            var old = new MeshRoundingRepair(RawResult()) { SharedPositionsTouch = true };
            Assert.True(old.Apply());
            Assert.Equal(0, old.ExactDegenerate);

            // all vertices fixed (a triangle of input vertices only, eg a degenerate input triangle): repaired
            var allFixed = new MeshRoundingRepair(RawResult()) { SharedPositionsTouch = true, ExactVertexF = v => true, FixedVertexF = v => true };
            Assert.True(allFixed.Apply());
            Assert.Equal(0, allFixed.ExactDegenerate);

            // not exact: repaired
            var rounded = new MeshRoundingRepair(RawResult()) { SharedPositionsTouch = true, ExactVertexF = v => false };
            Assert.True(rounded.Apply());
            Assert.Equal(0, rounded.ExactDegenerate);
        }

        [Fact]
        public void Repair_ExactNeedles_StillRepaired()
        {
            // needles and caps of exact vertices (edges of a sphere split 1e-13 from their end) are not well-shaped:
            // repaired exactly as without ExactVertexF
            Func<DMesh3> make = () => {
                DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
                foreach (int eid in m.EdgeIndices().ToArray()) {
                    if (eid % 7 != 0 || m.IsEdge(eid) == false)
                        continue;
                    Index2i ev = m.GetEdgeV(eid);
                    Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                    if (m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info) == MeshResult.Ok)
                        m.SetVertex(info.vNew, a + 1e-13 * (b - a).Normalized);
                }
                return m;
            };
            DMesh3 exact = make(), plain = make();
            var repair = new MeshRoundingRepair(exact) { ExactVertexF = v => true };
            bool clean = repair.Apply();
            Assert.True(clean, $"{repair.InitialDegenerate} {repair.RemainingDegenerate} {repair.ExactDegenerate} {repair.RemainingIntersections} {repair.Collapses} {repair.Flips} {repair.Moves} {repair.Zips} {repair.RemovedTriangles}");
            Assert.True(repair.InitialDegenerate > 0);
            Assert.Equal(0, repair.ExactDegenerate);
            Assert.True(new MeshRoundingRepair(plain).Apply());
            Assert.Equal("", Invariance.Difference(plain, exact));
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(exact, true));
        }
    }
}
