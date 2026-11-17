using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// RC4a of docs/research/BOOLEAN_TRIAGE_2026-11.md (owner 2026-11-17): SafeRounding's effort budget grows with the initial
    /// defects, MaxPairTests times min(max(1, D / PairTestsDefectScale), PairTestsMaxGrowth) (defaults: min(max(5M, 100 D), 20M)). Measured on the abc-chained
    /// unions and intersections of a model with its 1-ulp copy, which reached the fixed 5M: they need 64 to 83 pair tests per
    /// defect and finish clean. The same law at a thousandth of the scale (5000 pair tests, a scale of 50 defects) runs here on
    /// two nearly coincident spheres (318 initial defects, 19076 pair tests to finish), so that the tests are fast.
    /// </summary>
    public class SafeRoundingBudgetTests
    {
        static DMesh3 Sphere(int n) => new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();

        static DMesh3[] NearlyCoincidentSpheres()
        {
            DMesh3 a = Sphere(6), b = new DMesh3(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(new Vector3d(0.6, 0.3, 0.1).Normalized, 1e-10 * MathUtil.Rad2Deg));
            MeshTransforms.Translate(b, 1e-10 * new Vector3d(0.2, 0.9, 0.5));
            return new[] { a, b };
        }

        static ExactMeshBoolean Union(DMesh3 a, DMesh3 b, long budget, double scale)
        {
            var u = new ExactMeshBoolean(a, b, Ops.Union) { RoundingMaxPairTests = budget, RoundingPairTestsDefectScale = scale };
            Assert.True(u.Compute(), u.Error);
            return u;
        }

        [Fact]
        public void PairBudget_Law()
        {
            Assert.Equal(5000000L, MeshRoundingRepair.pair_budget(5000000, 50000, 4, 0));
            Assert.Equal(5000000L, MeshRoundingRepair.pair_budget(5000000, 50000, 4, 50000));
            Assert.Equal(15000000L, MeshRoundingRepair.pair_budget(5000000, 50000, 4, 150000));
            Assert.Equal(20000000L, MeshRoundingRepair.pair_budget(5000000, 50000, 4, 225000));      // the ceiling, 4 x 5M
            Assert.Equal(20000000L, MeshRoundingRepair.pair_budget(5000000, 50000, 4, 10000000));
            Assert.Equal(5000000L, MeshRoundingRepair.pair_budget(5000000, 50000, 1, 150000));       // no growth allowed
            Assert.Equal(5000000L, MeshRoundingRepair.pair_budget(5000000, 0, 4, 150000));            // no growth
            Assert.Equal(5000000L, MeshRoundingRepair.pair_budget(5000000, double.NaN, 4, 150000));
            Assert.Equal(0L, MeshRoundingRepair.pair_budget(0, 50000, 4, 150000));                    // "stop at once" stays
            Assert.Equal(long.MaxValue, MeshRoundingRepair.pair_budget(long.MaxValue / 2, 1, 1e9, 10));
            Assert.Equal(50000.0, new MeshRoundingRepair(new DMesh3()).PairTestsDefectScale);
            Assert.Equal(50000.0, new ExactMeshBoolean(new DMesh3(), new DMesh3(), Ops.Union).RoundingPairTestsDefectScale);
            Assert.Equal(4.0, new MeshRoundingRepair(new DMesh3()).PairTestsMaxGrowth);
            Assert.Equal(4.0, new ExactMeshBoolean(new DMesh3(), new DMesh3(), Ops.Union).RoundingPairTestsMaxGrowth);
        }

        /// <summary>
        /// the RC4a case at a thousandth of the scale: the fixed budget stops the repair with defects left; the budget grown with
        /// the defects (100 per defect) lets it finish clean. Before the change (a fixed budget, scale 0) the repair stops
        /// </summary>
        [Fact]
        public void BudgetGrownWithTheDefects_FinishesClean()
        {
            var ab = NearlyCoincidentSpheres();
            ExactMeshBoolean fixedBudget = Union(ab[0], ab[1], 5000, 0);
            Assert.True(fixedBudget.Rounding.EvaluationLimitReached);
            Assert.True(fixedBudget.Rounding.StoppedByPairTests);
            Assert.False(fixedBudget.RoundingClean);
            ExactMeshBoolean grown = Union(ab[0], ab[1], 5000, 50);
            long d = grown.Rounding.InitialIntersections + grown.Rounding.InitialDegenerate + grown.Rounding.InitialFalseContacts;
            Assert.Equal(5000 * Math.Min(d / 50.0, 4), grown.Rounding.PairBudget, 0);
            Assert.False(grown.Rounding.EvaluationLimitReached);
            Assert.True(grown.RoundingClean);
            Assert.True(grown.Result.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(grown.Result));
            // the same result as with an unlimited budget, bit for bit
            Assert.Equal("", Invariance.Difference(Union(ab[0], ab[1], long.MaxValue, 0).Result, grown.Result));
        }

        /// <summary>
        /// bit-identical wherever the fixed budget was not reached: the budget is only compared with the count (several
        /// booleans of the default tests, the default 5M, with and without growth)
        /// </summary>
        [Fact]
        public void NotReachingTheBudget_IsBitIdentical()
        {
            var cases = new List<(DMesh3, DMesh3, Ops)>();
            var ab = NearlyCoincidentSpheres();
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                cases.Add((ab[0], ab[1], op));
            DMesh3 box = new TrivialBox3Generator { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 s = Sphere(8);
            MeshTransforms.Translate(s, new Vector3d(0.3, 0.2, 0.1));
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                cases.Add((box, s, op));
            foreach (var (a, b, op) in cases) {
                // (scale 1: the growth is active on every case, by up to 4 times; none reaches the fixed 5M)
                var withGrowth = new ExactMeshBoolean(a, b, op) { RoundingPairTestsDefectScale = 1 };
                var withoutGrowth = new ExactMeshBoolean(a, b, op) { RoundingPairTestsDefectScale = 0 };
                Assert.True(withGrowth.Compute() && withoutGrowth.Compute());
                Assert.False(withoutGrowth.Rounding.EvaluationLimitReached);
                Assert.Equal("", Invariance.Difference(withoutGrowth.Result, withGrowth.Result));
                Assert.Equal(withoutGrowth.Rounding.PairTests, withGrowth.Rounding.PairTests);
                long d = withGrowth.Rounding.InitialIntersections + withGrowth.Rounding.InitialDegenerate + withGrowth.Rounding.InitialFalseContacts;
                if (d > 1)
                    Assert.True(withGrowth.Rounding.PairBudget > withoutGrowth.Rounding.PairBudget);
            }
        }

        /// <summary> invariance (Similarity.All()): the 2^k scales give the same result bit for bit, every frame finishes clean </summary>
        [Fact]
        public void BudgetGrownWithTheDefects_InvariantUnderSimilarities()
        {
            var ab = NearlyCoincidentSpheres();
            ExactMeshBoolean u0 = Union(ab[0], ab[1], 5000, 50);
            int exact = 0;
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean u = Union(Invariance.Transformed(ab[0], sim), Invariance.Transformed(ab[1], sim), 5000, 50);
                Assert.True(u.Result.IsClosed(), sim.ToString());
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(u0.Result, Invariance.MappedBack(u.Result, sim)));
                    Assert.Equal(u0.Rounding.PairBudget, u.Rounding.PairBudget);
                    Assert.True(u.RoundingClean);
                    ++exact;
                }
            }
            Assert.True(exact >= 3);
        }

        /// <summary> the ceiling: with a growth of at most 2 (10000 pair tests) the repair stops again, at that budget </summary>
        [Fact]
        public void Ceiling_StopsTheGrowth()
        {
            var ab = NearlyCoincidentSpheres();
            var u = new ExactMeshBoolean(ab[0], ab[1], Ops.Union) { RoundingMaxPairTests = 5000, RoundingPairTestsDefectScale = 50, RoundingPairTestsMaxGrowth = 2 };
            Assert.True(u.Compute());
            Assert.Equal(10000, u.Rounding.PairBudget);
            Assert.True(u.Rounding.StoppedByPairTests);
            Assert.False(u.RoundingClean);
        }

        /// <summary> reuse: Compute twice on one instance gives the same result and budget </summary>
        [Fact]
        public void Reuse_IsIdentical()
        {
            var ab = NearlyCoincidentSpheres();
            var u = new ExactMeshBoolean(ab[0], ab[1], Ops.Union) { RoundingMaxPairTests = 5000, RoundingPairTestsDefectScale = 50 };
            Assert.True(u.Compute());
            DMesh3 first = u.Result;
            long budget = u.Rounding.PairBudget;
            Assert.True(u.Compute());
            Assert.Equal("", Invariance.Difference(first, u.Result));
            Assert.Equal(budget, u.Rounding.PairBudget);
        }
    }
}
