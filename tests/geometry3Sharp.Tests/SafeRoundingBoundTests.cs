using System;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The displacement bound of MeshRoundingRepair (SafeRounding): the drift of every vertex stays at most
    /// MaxDisplacement times the diagonal (edits beyond it are not applied), MaxDrift reports it, and the repaired
    /// surface is the same solid as the one at the start up to MaxDrift.
    /// </summary>
    public class SafeRoundingBoundTests
    {
        static DMesh3 Sphere() => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();

        // edges of a sphere split at the given distances from their first end (needles and caps)
        static DMesh3 SplitSphere(params double[] at)
        {
            DMesh3 m = Sphere();
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (eid % 7 != 0 || m.IsEdge(eid) == false)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b), dir = (b - a).Normalized;
                int e = eid;
                // the farthest split first, then the nearer ones on the edge towards a
                foreach (double s in at.OrderByDescending(x => x)) {
                    Assert.Equal(MeshResult.Ok, m.SplitEdge(e, out DMesh3.EdgeSplitInfo info));
                    m.SetVertex(info.vNew, a + s * dir);
                    e = m.FindEdge(ev.a, info.vNew);
                }
            }
            return m;
        }

        static double Diagonal(DMesh3 m) => gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;

        static void AssertWithinBound(DMesh3 start, DMesh3 repaired, MeshRoundingRepair r, string what)
        {
            double bound = r.MaxDisplacement * Diagonal(start);
            Assert.True(r.MaxDrift <= bound, $"{what}: drift {r.MaxDrift:G3} above {bound:G3}");
            Assert.True(r.MaxDrift > 0 || r.Collapses + r.Flips + r.Moves + r.Zips == 0, what);
            Assert.True(BooleanOracle.SameSolid(repaired, start, 2, r.MaxDrift) == "", what);
        }

        [Fact]
        public void Needles_RepairedWithinTheBound()
        {
            DMesh3 start = SplitSphere(1e-13), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.True(r.Collapses > 0);
            AssertWithinBound(start, m, r, "needles");
        }

        [Fact]
        public void BudgetBinds_ChainedContractionsStopAtTheBound()
        {
            // two needles in a row on each split edge (1e-13 and 2e-13 from its end): contracting both into the end moves
            // the surface by up to 3e-13 in total
            double diag = Diagonal(SplitSphere(1e-13, 2e-13));
            DMesh3 free = SplitSphere(1e-13, 2e-13);
            var unbounded = new MeshRoundingRepair(free);
            Assert.True(unbounded.Apply());
            Assert.True(unbounded.MaxDrift > 2.2e-13, $"drift {unbounded.MaxDrift:G3}");
            // with a bound of 2.2e-13 (MaxDisplacement relative to the diagonal) the second contraction into the same
            // end is not applied where it would exceed it
            DMesh3 start = SplitSphere(1e-13, 2e-13), m = new DMesh3(start);
            var bounded = new MeshRoundingRepair(m) { MaxDisplacement = 2.2e-13 / diag };
            bounded.Apply();
            AssertWithinBound(start, m, bounded, "bounded");
            Assert.True(bounded.MaxDrift <= 2.2e-13);
            Assert.True(bounded.Collapses > 0);
        }

        [Fact]
        public void NearlyCoincidentBoxes_SafeRoundingWithinTheBound()
        {
            DMesh3 a = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(new Vector3d(0.3, 0.5, 0.8).Normalized, 1e-11 * MathUtil.Rad2Deg));
            MeshTransforms.Translate(b, 1e-11 * new Vector3d(0.7, 0.2, 0.4));
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection }) {
                var raw = new ExactMeshBoolean(a, b, op) { SafeRounding = false };
                Assert.True(raw.Compute(), raw.Error);
                var safe = new ExactMeshBoolean(a, b, op);
                Assert.True(safe.Compute(), safe.Error);
                Assert.True(safe.RoundingClean);
                Assert.True(safe.Rounding.RemovedComponents == 0);
                AssertWithinBound(raw.Result, safe.Result, safe.Rounding, op.ToString());
            }
        }

        [Fact]
        public void PowerOfTwoScales_SameDriftScaled_AndReuse()
        {
            DMesh3 start = SplitSphere(1e-13, 2e-13);
            var r0 = new MeshRoundingRepair(new DMesh3(start));
            r0.Apply();
            foreach (Similarity sim in Similarity.All().Where(s => s.IsExact)) {
                DMesh3 m = new DMesh3(start);
                MeshTransforms.PerVertexTransform(m, sim.Apply);
                var r = new MeshRoundingRepair(m);
                r.Apply();
                Assert.Equal(r0.MaxDrift * sim.Scale, r.MaxDrift);
                Assert.Equal(r0.Collapses, r.Collapses);
            }
            // reuse: a second Apply on the repaired mesh starts its drift at 0 and has nothing to do
            DMesh3 again = new DMesh3(start);
            var reused = new MeshRoundingRepair(again);
            Assert.True(reused.Apply());
            Assert.True(reused.Apply());
            Assert.Equal(0, reused.MaxDrift);
            Assert.Equal(0, reused.Collapses + reused.Flips + reused.Moves + reused.Zips);
        }
    }
}
