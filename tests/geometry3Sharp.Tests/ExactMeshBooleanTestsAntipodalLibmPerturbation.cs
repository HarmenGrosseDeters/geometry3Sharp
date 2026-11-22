using System;
using System.Collections.Generic;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Tree = geometry3Sharp.Tests.ExactMeshBooleanTestsAntipodalTree;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// F2 of docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md: antipodal_tree_value calls Math.Acos, Math.Asin and Math.Cos (twice)
    /// for fullBound, whose results differ by an ulp between C libraries. They do not enter the tree's value or its bound, only
    /// fullBound, and tree_decides compares that with 0.999e-6, 0.1% below the 1e-6 limit of the full sum. These tests move
    /// the four results through the test hook (ExactMeshBoolean.LibmPerturbForTests): by 1 ulp up, down and by the low bit of
    /// the value, and by 2^20 and 2^30 ulps, and ask that no decision becomes unsafe. 1 ulp moves fullBound by up to 1e-6 of itself
    /// (the cosine of a small angle, 1 - cos beta, amplifies it) and flips no decision; 2^30 ulps (the values change in the
    /// eighth digit) move it by a factor of about 1 and flip a few decisions of about 1900, which stay safe: whichever way
    /// tree_decides goes, the full sum gives a value at the ray, below 1e-6, with the same threshold.
    /// (The class name keeps the mutation shards' test filter.)
    /// (The collection runs alone, with the parallelization of xunit off: the hook is a process-wide static, and a perturbation,
    /// which does not change a result, changes the counters and the tree decisions of every other test that reaches
    /// antipodal_tree_value (about 18 files: the Boolean tests with open inputs, the metamorphic and invariance tests, the
    /// coverage rounds). ExactMeshBooleanTestsAntipodalTree is in it as well, as the one that compares those counters between runs.)
    /// </summary>
    [CollectionDefinition("AntipodalLibmPerturbation", DisableParallelization = true)]
    public class AntipodalLibmPerturbationCollection { }

    [Collection("AntipodalLibmPerturbation")]
    public class ExactMeshBooleanTestsAntipodalLibmPerturbation
    {
        readonly Xunit.Abstractions.ITestOutputHelper output;
        public ExactMeshBooleanTestsAntipodalLibmPerturbation(Xunit.Abstractions.ITestOutputHelper output) { this.output = output; }

        // (n ulps, direction by the low bit of the value)
        public static IEnumerable<object[]> Perturbations()
        {
            yield return new object[] { 1, false };
            yield return new object[] { -1, false };
            yield return new object[] { 1, true };
            yield return new object[] { 1 << 20, true };
            yield return new object[] { -(1 << 30), false };
            yield return new object[] { 1 << 30, true };
        }

        static void With(int ulps, bool byLowBit, Action a, int mask = 15)
        {
            ExactMeshBoolean.LibmPerturbMaskForTests = mask;
            ExactMeshBoolean.LibmPerturbForTests = ulps;
            ExactMeshBoolean.LibmPerturbRandomForTests = byLowBit;
            try {
                a();
            } finally {
                ExactMeshBoolean.LibmPerturbForTests = 0;
                ExactMeshBoolean.LibmPerturbMaskForTests = 15;
                ExactMeshBoolean.LibmPerturbRandomForTests = false;
            }
        }

        // each of the four calls on its own moves fullBound (a hook that misses one would leave it out of every other test)
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public void EveryCall_MovesFullBound(int mask)
        {
            var rand = new Random(767);
            int moved = 0, compared = 0;
            for (int trial = 0; trial < 12; ++trial) {
                Vector3d[] segs = Tree.Chains(400 + rand.Next(1200), rand, false);
                for (int k = 0; k < segs.Length; ++k)
                    segs[k] = new Vector3d(Math.Round(segs[k].x * 1048576), Math.Round(segs[k].y * 1048576), Math.Round(segs[k].z * 1048576));
                var tree = ExactMeshBoolean.AntipodalTree.Build(segs);
                for (int q = 0; q < 80; ++q) {
                    Vector3d p = new Vector3d(rand.NextDouble() * 6 - 3, rand.NextDouble() * 6 - 3, rand.NextDouble() * 2 - 1) * 1048576;
                    Vector3d x1 = Tree.RandomUnit(rand);
                    double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
                    if (ExactMeshBoolean.antipodal_tree_value(p, pm, x1, 0, segs, tree, 0.25, 0, 0, out _, out _, out double fb0) != 0)
                        continue;
                    double fb1 = 0;
                    With(1 << 20, false, () => ExactMeshBoolean.antipodal_tree_value(p, pm, x1, 0, segs, tree, 0.25, 0, 0, out _, out _, out fb1), mask);
                    compared++;
                    if (fb0 != fb1)
                        moved++;
                }
            }
            Assert.True(compared > 300 && moved > 0, $"mask {mask}: {moved} of {compared} moved");
        }

        [Theory]
        [MemberData(nameof(Perturbations))]
        public void TreeDecision_DoesNotChange_UnderPerturbationOfTheLibmValues(int ulps, bool byLowBit)
        {
            var rand = new Random(767);
            int compared = 0, decided = 0, moved = 0, flipped = 0;
            double maxRelative = 0;
            for (int trial = 0; trial < 24; ++trial) {
                Vector3d[] segs = Tree.Chains(400 + rand.Next(1200), rand, trial % 3 == 2);
                for (int k = 0; k < segs.Length; ++k)
                    segs[k] = new Vector3d(Math.Round(segs[k].x * 1048576), Math.Round(segs[k].y * 1048576), Math.Round(segs[k].z * 1048576));
                var tree = ExactMeshBoolean.AntipodalTree.Build(segs);
                for (int q = 0; q < 80; ++q) {
                    Vector3d p = q % 5 != 3 ? new Vector3d(rand.NextDouble() * 6 - 3, rand.NextDouble() * 6 - 3, rand.NextDouble() * 2 - 1) * 1048576
                        : segs[2 * rand.Next(segs.Length / 2)] + Tree.RandomUnit(rand) * (Math.Pow(10, -1 - 3 * rand.NextDouble()) * 1048576);
                    Vector3d x1 = Tree.RandomUnit(rand);
                    // (one in five: the ray just past a segment's end, where the full sum's bound can exceed its limit while the tree
                    // still gives a value: the gate fullBound <= 0.999e-6 refuses those)
                    if (q % 5 == 4) {
                        Vector3d toEnd = (segs[2 * rand.Next(segs.Length / 2)] - p).Normalized;
                        Vector3d side = toEnd.Cross(Tree.RandomUnit(rand)).Normalized;
                        x1 = -(toEnd + side * Math.Pow(10, -5 - 3 * rand.NextDouble())).Normalized;
                    }
                    double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
                    int s0 = ExactMeshBoolean.antipodal_tree_value(p, pm, x1, 0, segs, tree, 0.25, 0, 0, out double w0, out double b0, out double fb0);
                    int s1 = 0; double w1 = 0, b1 = 0, fb1 = 0;
                    With(ulps, byLowBit, () => s1 = ExactMeshBoolean.antipodal_tree_value(p, pm, x1, 0, segs, tree, 0.25, 0, 0, out w1, out b1, out fb1));
                    // (the value and its bound do not use the four calls; the status does not either)
                    Assert.Equal(s0, s1);
                    if (s0 != 0)
                        continue;
                    compared++;
                    Assert.Equal(BitConverter.DoubleToInt64Bits(w0), BitConverter.DoubleToInt64Bits(w1));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(b0), BitConverter.DoubleToInt64Bits(b1));
                    if (fb0 != fb1)
                        moved++;
                    if (fb0 > 0 && !double.IsInfinity(fb0) && !double.IsInfinity(fb1))
                        maxRelative = Math.Max(maxRelative, Math.Abs(fb1 - fb0) / fb0);
                    bool d0 = ExactMeshBoolean.tree_decides(w0, b0, fb0), d1 = ExactMeshBoolean.tree_decides(w1, b1, fb1);
                    if (d0 != d1)
                        flipped++;
                    // (whichever of the two decides: the full sum gives a value at this ray, below its limit, and the same threshold)
                    if (d0 || d1) {
                        int sf = ExactMeshBoolean.antipodal_value(p, pm, x1, 0, segs, out double wf, out double bf, out double rbf, 0, 0, 1e-6);
                        Assert.Equal(0, sf);
                        Assert.True(bf <= 1e-6, $"full bound {bf:R}");
                        Assert.Equal(Math.Ceiling(wf - 0.5), Math.Ceiling(w0 - 0.5));
                        decided++;
                    }
                }
            }
            Assert.True(compared > 1000 && decided > 500, $"{compared} compared, {decided} decided");
            // (the hook acts: fullBound moves in most of the cases)
            Assert.True(moved > compared / 4, $"{moved} of {compared} moved");
            output.WriteLine($"{ulps} ulps, low bit {byLowBit}: {compared} compared, {decided} decided, fullBound moved in {moved}, by a relative {maxRelative:E2} at most; {flipped} decisions flipped");
        }

        [Theory]
        [MemberData(nameof(Perturbations))]
        public void Boolean_SameResult_UnderPerturbationOfTheLibmValues(int ulps, bool byLowBit)
        {
            (DMesh3 a, DMesh3 b) = Tree.HoleySphere();
            ExactMeshBoolean reference = Tree.Run(a, b, Ops.Difference, true);
            Assert.True(reference.Counts["antipodal_hierarchical"] > 0);
            ExactMeshBoolean perturbed = null;
            With(ulps, byLowBit, () => perturbed = Tree.Run(a, b, Ops.Difference, true));
            Assert.Equal("", Invariance.Difference(reference.Result, perturbed.Result));
            Assert.Equal(reference.OpenWindingPerPiece.Length, perturbed.OpenWindingPerPiece.Length);
            for (int i = 0; i < reference.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(reference.OpenWindingPerPiece[i], perturbed.OpenWindingPerPiece[i]);
            Assert.Equal(reference.Counts["antipodal_undecided"], perturbed.Counts["antipodal_undecided"]);
        }
    }
}
