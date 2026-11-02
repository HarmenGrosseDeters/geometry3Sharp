using System;
using System.Linq;
using g3;
using Xunit;
using F = geometry3Sharp.Tests.DegenerateFamilies.Family;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The separation prototype of option A of the snap-rounding experiment (MeshRoundingRepair.Separate,
    /// docs/design/SAFE_ROUNDING.md section 7): after SafeRounding, linear programs move the vertices around what is
    /// left; each proposal is applied only if the exact check accepts it, within the drift bound.
    /// </summary>
    public class MeshRoundingRepairSeparationTests
    {
        // a dense cluster SafeRounding leaves with two degenerate triangles (one strip that branches)
        static DMesh3 Cluster() => DegenerateFamilies.Make(F.Cluster, 40, 0, 4);

        static ExactMeshBoolean Resolve(DMesh3 input)
        {
            var op = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
            Assert.True(op.Compute(), op.Error);
            return op;
        }

        [Fact]
        public void Cluster_SeparatedClean_WithinTheBound()
        {
            ExactMeshBoolean op = Resolve(Cluster());
            MeshRoundingRepair r = op.Rounding;
            Assert.False(r.IsClean);
            double driftBefore = r.MaxDrift;
            Assert.True(r.Separate());
            Assert.True(r.Separations > 0);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.Equal(0, r.RemainingIntersections);
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReasons(op.Result));
            Assert.True(r.MaxDrift <= r.MaxLength && r.MaxDrift >= driftBefore, $"drift {r.MaxDrift:G3}, bound {r.MaxLength:G3}");
            // the same solid as the sphere the cluster lies in, up to the drift
            Assert.Equal("", BooleanOracle.SameSolid(op.Result, DegenerateFamilies.Sphere(6), 2, r.MaxDrift));
            // and the result is 2e-separated (the certificate of Milenkovic and Sacks)
            Assert.Equal(0, SeparationCertificate.Check(op.Result).Uncertified);
        }

        // clusters the separation does not repair (strips that branch and fold): its proposals there are rejected by the
        // exact check, so the defects never grow, and the drift stays within the bound
        [Theory]
        [InlineData(0, 8, 1)]       // (one separation applied, still not clean)
        [InlineData(36, 8, 0)]      // (every proposal rejected)
        public void NotRepaired_NeverWorse(int k, int count, int seed)
        {
            ExactMeshBoolean op = Resolve(DegenerateFamilies.Make(F.Cluster, k, seed, count));
            MeshRoundingRepair r = op.Rounding;
            Assert.False(r.IsClean);
            int before = r.RemainingDegenerate + r.RemainingIntersections;
            int pairsBefore = gs.MeshRepairMetrics.SelfIntersections(op.Result);
            r.Separate();
            Assert.True(r.SeparationPrograms > 0);
            Assert.True(r.RemainingDegenerate + r.RemainingIntersections <= before, $"{before} -> {r.RemainingDegenerate} + {r.RemainingIntersections}");
            Assert.True(gs.MeshRepairMetrics.SelfIntersections(op.Result) <= pairsBefore);
            Assert.True(r.MaxDrift <= r.MaxLength);
        }

        [Fact]
        public void WithoutApply_Throws_TwiceIsStable()
        {
            // (no silent success without the state of Apply; independent review)
            Assert.Throws<InvalidOperationException>(() => new MeshRoundingRepair(Cluster()).Separate());
            // a second call on the same instance after a successful one changes nothing
            ExactMeshBoolean op = Resolve(Cluster());
            Assert.True(op.Rounding.Separate());
            var first = new DMesh3(op.Result);
            double drift = op.Rounding.MaxDrift;
            Assert.True(op.Rounding.Separate());
            Assert.Equal(0, op.Rounding.Separations);
            Assert.Equal(drift, op.Rounding.MaxDrift);
            Assert.Equal("", Invariance.Difference(first, op.Result));
            Assert.Equal(Vector3d.Zero, op.Rounding.RemainingDefectAt);
        }

        // the exact check decides, not the program: a proposal that pushes a corner of the upper box through the lower
        // one, 2^-30 away (within the bound), is rejected; one that moves it up is applied, with its drift
        [Fact]
        public void HarmfulProposal_Rejected()
        {
            ExactMeshBoolean op = Resolve(DegenerateFamilies.Make(F.SharedFace, 30, 0, 9));
            MeshRoundingRepair r = op.Rounding;
            Assert.True(r.IsClean);
            DMesh3 m = op.Result;
            double gap = Math.Pow(2, -30);
            // the corner of the upper box's bottom face at (0.6, 0.5, 1 + gap)
            int v = m.VertexIndices().First(i => m.GetVertex(i) == new Vector3d(0.6, 0.5, 1 + gap));
            var before = new DMesh3(m);
            Assert.False(r.propose_move(new System.Collections.Generic.List<int> { v }, new[] { m.GetVertex(v) - new Vector3d(0, 0, 4 * gap) }));
            Assert.Equal("", Invariance.Difference(before, m));
            Assert.Equal(0, r.Separations);
            // (and the bound: a move beyond the drift left is rejected too)
            Assert.False(r.propose_move(new System.Collections.Generic.List<int> { v }, new[] { m.GetVertex(v) + new Vector3d(0, 0, 2 * r.MaxLength) }));
            Assert.Equal("", Invariance.Difference(before, m));
        }

        [Fact]
        public void CleanResult_Unchanged()
        {
            ExactMeshBoolean op = Resolve(DegenerateFamilies.Make(F.Collinear, 0, 1, 3));
            Assert.True(op.RoundingClean);
            var before = new DMesh3(op.Result);
            Assert.True(op.Rounding.Separate());
            Assert.Equal(0, op.Rounding.Separations);
            Assert.Equal("", Invariance.Difference(before, op.Result));
        }

        [Fact]
        public void BoundBinds_NothingAppliedBeyondIt()
        {
            // the drift left after SafeRounding too small for any separation: nothing is applied, the drift stays
            var op = new ExactMeshBoolean(Cluster(), new DMesh3(), Ops.Union) { RoundingMaxDisplacement = 1e-9 };
            Assert.True(op.Compute());
            MeshRoundingRepair r = op.Rounding;
            Assert.False(r.IsClean);
            var before = new DMesh3(op.Result);
            double drift = r.MaxDrift;
            r.Separate();
            Assert.True(r.MaxDrift <= r.MaxLength);
            if (r.Separations == 0) {
                Assert.Equal(drift, r.MaxDrift);
                Assert.Equal("", Invariance.Difference(before, op.Result));
            }
        }

        [Fact]
        public void Repeated_SameResult()
        {
            ExactMeshBoolean a = Resolve(Cluster()), b = Resolve(Cluster());
            a.Rounding.Separate();
            b.Rounding.Separate();
            Assert.Equal("", Invariance.Difference(a.Result, b.Result));
            Assert.Equal(a.Rounding.MaxDrift, b.Rounding.MaxDrift);
        }

        // the separation alone (no edits of Apply: MaxPasses 0) on the exact resolve, rounded: the 2^k scales give the
        // same result bit for bit, the others stay within the bound. (SafeRounding's own edits are not 2^k-invariant on
        // these clusters on master: fix_degenerate uses Normalized, whose threshold is absolute; reported separately)
        static MeshRoundingRepair SeparateOnly(DMesh3 input)
        {
            var raw = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union) { SafeRounding = false };
            Assert.True(raw.Compute());
            var r = new MeshRoundingRepair(raw.Result) { SharedPositionsTouch = true, MaxPasses = 0 };
            r.Apply();
            r.Separate();
            return r;
        }

        [Fact]
        public void Similarities_PowerOfTwoBitIdentical_OthersWithinTheBound()
        {
            // (a few slivers 2^-52 inside an edge of the sphere's triangles)
            DMesh3 input = DegenerateFamilies.Make(F.Sliver, 52, 1, 61);
            MeshRoundingRepair r0 = SeparateOnly(input);
            Assert.True(r0.Separations > 0);
            foreach (Similarity sim in Similarity.All()) {
                MeshRoundingRepair r = SeparateOnly(Invariance.Transformed(input, sim));
                Assert.True(r.MaxDrift <= r.MaxLength, sim.Name);
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(r0.Mesh, Invariance.MappedBack(r.Mesh, sim)) == "", sim.Name);
                    Assert.Equal(r0.MaxDrift * sim.Scale, r.MaxDrift);
                    Assert.Equal(r0.Separations, r.Separations);
                }
            }
        }
    }
}
