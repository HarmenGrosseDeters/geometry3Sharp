using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Lever 1 (docs/design/EXACT_BOOLEAN_ANTIPODAL_HIERARCHICAL.md): the Antipodal boundary sum over a tree of the boundary
    /// chains. Its value must be within its bound plus the full sum's of the full sum wherever it gives one; the cone test
    /// on -x1 is what keeps whole turns out; the classification is the same mesh, bit for bit, with and without the tree.
    /// (The class name keeps the mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsAntipodalTree
    {
        // a wavy closed loop (n segments) and an open chain, as segment pairs
        static Vector3d[] Chains(int n, Random rand, bool open)
        {
            var pts = new List<Vector3d>();
            double a1 = rand.NextDouble() * 0.3, a2 = rand.NextDouble() * 0.3;
            int f1 = 3 + rand.Next(5), f2 = 2 + rand.Next(7);
            for (int i = 0; i <= n; ++i) {
                double t = (open ? 1.5 : 2) * Math.PI * i / n;
                pts.Add(new Vector3d(Math.Cos(t) * (1 + a1 * Math.Sin(f1 * t)), Math.Sin(t) * (1 + a1 * Math.Sin(f1 * t)), a2 * Math.Cos(f2 * t)));
            }
            if (!open)
                pts[n] = pts[0];
            var segs = new Vector3d[2 * n];
            for (int i = 0; i < n; ++i) {
                segs[2 * i] = pts[i];
                segs[2 * i + 1] = pts[i + 1];
            }
            return segs;
        }

        static Vector3d RandomUnit(Random rand)
        {
            while (true) {
                var v = new Vector3d(rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1);
                if (v.Length > 0.1 && v.Length <= 1)
                    return v.Normalized;
            }
        }

        [Fact]
        public void TreeValue_WithinItsBoundOfTheFullSum()
        {
            var rand = new Random(767);
            int used = 0, compared = 0, admitted = 0, decided = 0, fullFails = 0;
            double worst = 0;
            for (int trial = 0; trial < 40; ++trial) {
                Vector3d[] segs = Chains(400 + rand.Next(1200), rand, trial % 3 == 2);
                // (the grid scale of the Antipodal path: integers of about 2^20)
                for (int k = 0; k < segs.Length; ++k)
                    segs[k] = new Vector3d(Math.Round(segs[k].x * 1048576), Math.Round(segs[k].y * 1048576), Math.Round(segs[k].z * 1048576));
                var tree = ExactMeshBoolean.AntipodalTree.Build(segs);
                for (int q = 0; q < 80; ++q) {
                    // (two in five next to the chain, where the full sum's bound can exceed its limit)
                    Vector3d p = q % 5 != 3 ? new Vector3d(rand.NextDouble() * 6 - 3, rand.NextDouble() * 6 - 3, rand.NextDouble() * 2 - 1) * 1048576
                        : segs[2 * rand.Next(segs.Length / 2)] + RandomUnit(rand) * (Math.Pow(10, -1 - 3 * rand.NextDouble()) * 1048576);
                    Vector3d x1 = RandomUnit(rand);
                    // (one in five: the ray just past a segment's end, a near-singular term: the full sum's bound can then
                    // exceed its limit while the tree still gives a value, which the gate must refuse)
                    if (q % 5 == 4) {
                        Vector3d toEnd = (segs[2 * rand.Next(segs.Length / 2)] - p).Normalized;
                        Vector3d side = toEnd.Cross(RandomUnit(rand)).Normalized;
                        x1 = -(toEnd + side * Math.Pow(10, -5 - 3 * rand.NextDouble())).Normalized;
                    }
                    double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
                    double wf, bf, rbf, wh, bh, fb;
                    int sf = ExactMeshBoolean.antipodal_value(p, pm, x1, 0, segs, out wf, out bf, out rbf, 0, 0, 1e-6);
                    int sh = ExactMeshBoolean.antipodal_tree_value(p, pm, x1, 0, segs, tree, 0.25, 0, 0, out wh, out bh, out fb);
                    if (sh == 0)
                        used++;
                    // (where the tree's upper bound of the full sum's bound admits a value, the full sum gives one, within it;
                    // where the tree decides, the full sum gives a value with the same threshold and does not snap)
                    if (sh == 0 && fb <= 0.999e-6) {
                        Assert.Equal(0, sf);
                        Assert.True(bf <= fb, $"trial {trial}: full bound {bf} above the tree's upper bound {fb}");
                        admitted++;
                    }
                    if (sh == 0 && ExactMeshBoolean.tree_decides(wh, bh, fb)) {
                        Assert.Equal(0, sf);
                        Assert.Equal(Math.Ceiling(wf - 0.5), Math.Ceiling(wh - 0.5));
                        Assert.Equal(wf, ExactMeshBoolean.snap_half(wf, bf));
                        decided++;
                    }
                    if (sf != 0 && sh == 0)
                        fullFails++;
                    if (sf != 0 || sh != 0)
                        continue;
                    compared++;
                    worst = Math.Max(worst, Math.Abs(wh - wf) / (bh + bf));
                    Assert.True(Math.Abs(wh - wf) <= bh + bf, $"trial {trial}: tree {wh} +- {bh}, full {wf} +- {bf}");
                }
            }
            Assert.True(compared > 1500, $"{compared} compared, {used} tree values");
            Assert.True(admitted > 1000, $"{admitted} admitted of {used}");
            // (the points next to the chain give cases where the full sum fails with a ray and the tree gives a value: the
            // gate must refuse those)
            Assert.True(decided > 1000 && fullFails > 20, $"{decided} decided, {fullFails} with the full sum failing");
            Assert.True(worst < 1, $"worst ratio {worst}");
        }

        // a sphere with many small holes (every seventh triangle removed): an open mesh with more than 1000 boundary
        // segments, and a smaller closed sphere through its surface
        static (DMesh3, DMesh3) HoleySphere()
        {
            DMesh3 a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
            int n = 0;
            foreach (int t in a.TriangleIndices().ToList())
                if (n++ % 7 == 3)
                    a.RemoveTriangle(t, true, false);
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 0.45, EdgeVertices = 10 }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(0.7, 0.35, 0.2));
            return (a, b);
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool tree)
        {
            var r = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) { AntipodalHierarchical = tree, RecordOpenWinding = true };
            Assert.True(r.Compute(), r.Error);
            Assert.True(r.Result.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r.Result));
            Assert.True(r.Result.CheckValidity(true, FailMode.ReturnOnly));
            return r;
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Difference)]
        public void Classification_SameWithAndWithoutTheTree(Ops op)
        {
            (DMesh3 a, DMesh3 b) = HoleySphere();
            ExactMeshBoolean full = Run(a, b, op, false), tree = Run(a, b, op, true);
            Assert.True(tree.OpenBoundarySegments[0] >= 1000, $"{tree.OpenBoundarySegments[0]} segments");
            Assert.True(tree.Counts["antipodal_hierarchical"] > 0);
            Assert.Equal(0, full.Counts["antipodal_hierarchical"]);
            Assert.Equal("", Invariance.Difference(full.Result, tree.Result));
            Assert.Equal(full.Counts["antipodal_undecided"], tree.Counts["antipodal_undecided"]);
            for (int i = 0; i < full.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(full.OpenWindingPerPiece[i], tree.OpenWindingPerPiece[i]);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Classification_UnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = HoleySphere();
            DMesh3 ta = Invariance.Transformed(a, sim), tb = Invariance.Transformed(b, sim);
            ExactMeshBoolean tree = Run(ta, tb, Ops.Union, true), full = Run(ta, tb, Ops.Union, false);
            Assert.Equal("", Invariance.Difference(full.Result, tree.Result));
            if (sim.IsExact) {
                ExactMeshBoolean plain = Run(a, b, Ops.Union, true);
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(tree.Result, sim)));
                Assert.Equal(plain.Counts["antipodal_hierarchical"], tree.Counts["antipodal_hierarchical"]);
                Assert.Equal(plain.Counts["antipodal_hierarchical_fallbacks"], tree.Counts["antipodal_hierarchical_fallbacks"]);
            }
        }

        [Fact]
        public void Classification_SerialEqualsParallel_RepeatedReused()
        {
            (DMesh3 a, DMesh3 b) = HoleySphere();
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Run(a, b, Ops.Difference, true);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            var reused = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Difference) { RecordOpenWinding = true };
            for (int run = 0; run < 2; ++run) {
                Assert.True(reused.Compute(), reused.Error);
                Assert.Equal("", Invariance.Difference(serial.Result, reused.Result));
                foreach (string c in new[] { "antipodal_hierarchical", "antipodal_hierarchical_fallbacks", "l2_traced", "l2_carried", "l2_direct" })
                    Assert.Equal(serial.Counts[c], reused.Counts[c]);
            }
        }
    }
}
