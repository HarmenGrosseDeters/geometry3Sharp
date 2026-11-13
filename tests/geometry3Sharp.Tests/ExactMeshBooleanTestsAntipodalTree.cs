using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        [Fact]
        public void TreeValue_AtADisplacedPoint_AdmitsOnlyWhereTheExactFullSumGivesAValue()
        {
            // (L2's approximated centroid: the tree runs at p + delta with pointError >= |delta|, the full sum at the exact
            // p; points next to a segment, where the full sum's status-1 test 1 + a.b <= tol can fire at p; review of #813)
            var rand = new Random(813);
            int admitted = 0, nearRefused = 0;
            for (int trial = 0; trial < 30; ++trial) {
                Vector3d[] segs = Chains(400 + rand.Next(1200), rand, trial % 3 == 2);
                for (int k = 0; k < segs.Length; ++k)
                    segs[k] = new Vector3d(Math.Round(segs[k].x * 1048576), Math.Round(segs[k].y * 1048576), Math.Round(segs[k].z * 1048576));
                var tree = ExactMeshBoolean.AntipodalTree.Build(segs);
                for (int q = 0; q < 80; ++q) {
                    int s = 2 * rand.Next(segs.Length / 2);
                    // (on the segment's line between its ends, off it by 10^-6 .. 10^-12 of its length, and far points)
                    Vector3d p = q % 4 == 3
                        ? new Vector3d(rand.NextDouble() * 6 - 3, rand.NextDouble() * 6 - 3, rand.NextDouble() * 2 - 1) * 1048576
                        : segs[s] + (segs[s + 1] - segs[s]) * (0.2 + 0.6 * rand.NextDouble())
                            + RandomUnit(rand) * ((segs[s + 1] - segs[s]).Length * Math.Pow(10, -6 - 6 * rand.NextDouble()));
                    Vector3d x1 = RandomUnit(rand);
                    double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
                    double shift = Math.Pow(10, -3 - 6 * rand.NextDouble()) * (segs[s + 1] - segs[s]).Length;
                    Vector3d pa = p + RandomUnit(rand) * shift;
                    double pma = Math.Abs(pa.x) + Math.Abs(pa.y) + Math.Abs(pa.z);
                    double pointError = shift * 1.01;
                    double wh, bh, fb, wf, bf, rbf;
                    int sh = ExactMeshBoolean.antipodal_tree_value(pa, pma, x1, 0, segs, tree, 0.25, pointError, 0, out wh, out bh, out fb);
                    int sf = ExactMeshBoolean.antipodal_value(p, pm, x1, 0, segs, out wf, out bf, out rbf, 0, 0, 1e-6);
                    if (sh == 0 && fb <= 0.999e-6) {
                        Assert.Equal(0, sf);
                        Assert.True(bf <= fb, $"trial {trial}: full bound {bf} above {fb}");
                        Assert.True(Math.Abs(wh - wf) <= bh + bf, $"trial {trial}: tree {wh} +- {bh}, full {wf} +- {bf}");
                        admitted++;
                    } else if (sf == 1)
                        nearRefused++;
                }
            }
            Assert.True(admitted > 100 && nearRefused > 50, $"{admitted} admitted, {nearRefused} refused where the full sum is on the segment");
        }

        [Fact]
        public void TreeValue_NextToASegment_TinyDisplacement_ExactFullSumNotOnTheSegment()
        {
            // (review of #813, M2: points where 1 + a.b of the segment's term is within a few tol of tol, the approximation
            // displaced by about the rounding: the full sum at the exact point must not take its status-1 test where the
            // tree, at the approximation, admits)
            var rand = new Random(8132);
            Vector3d[] segs = Chains(200, rand, false);
            for (int k = 0; k < segs.Length; ++k)
                segs[k] = new Vector3d(Math.Round(segs[k].x * 1048576), Math.Round(segs[k].y * 1048576), Math.Round(segs[k].z * 1048576));
            var tree = ExactMeshBoolean.AntipodalTree.Build(segs);
            int admitted = 0, band = 0;
            for (int q = 0; q < 40000; ++q) {
                int s = 2 * rand.Next(segs.Length / 2);
                Vector3d a = segs[s], b = segs[s + 1], mid = (a + b) * 0.5, dir = (b - a).Normalized;
                Vector3d nrm = dir.Cross(RandomUnit(rand)).Normalized;
                double l = (b - a).Length, pmEst = Math.Abs(mid.x) + Math.Abs(mid.y) + Math.Abs(mid.z);
                // (today's tol at mid, and the distance h where 1 + a.b, about 8 (h / l)^2, meets it, times 0.5 .. 2)
                double tol = 16 * (2 * 2.3e-16 * (pmEst + Math.Abs(a.x) + Math.Abs(a.y) + Math.Abs(a.z) + Math.Abs(b.x) + Math.Abs(b.y) + Math.Abs(b.z)) / l * 2 + 1e-15);
                double h = l * Math.Sqrt(tol / 8) * Math.Pow(2, 2 * rand.NextDouble() - 1);
                Vector3d p = mid + nrm * h;
                // (the approximation 1 to 3 ulps off in one coordinate, pointError that distance: the rounding parts of
                // tol then exceed pointError's, where the margin of the old test can be negative)
                Vector3d pa = p;
                int axis = rand.Next(3);
                pa[axis] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(p[axis]) + (rand.Next(2) == 0 ? -1 : 1) * (1 + rand.Next(3)));
                double shift = (pa - p).Length;
                Vector3d x1 = RandomUnit(rand);
                double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z), pma = Math.Abs(pa.x) + Math.Abs(pa.y) + Math.Abs(pa.z);
                double wh, bh, fb, wf, bf, rbf;
                int sh = ExactMeshBoolean.antipodal_tree_value(pa, pma, x1, 0, segs, tree, 0.25, shift * 1.01 + 1e-300, 0, out wh, out bh, out fb);
                int sf = ExactMeshBoolean.antipodal_value(p, pm, x1, 0, segs, out wf, out bf, out rbf, 0, 0, 1e-6);
                if (sf == 1)
                    band++;
                if (sh == 0 && fb <= 0.999e-6) {
                    Assert.True(sf == 0, $"query {q}: the tree admits at the approximation, the full sum gives status {sf} at the exact point");
                    admitted++;
                }
            }
            Assert.True(admitted > 1000 && band > 1000, $"{admitted} admitted, {band} on the segment");
        }

        [Fact]
        public void TreeTerm_AtADisplacedPoint_RefusesWithinTwiceTolOfTheSegment()
        {
            // (review of #813, M2, as a rule: at a displaced point the segment test is 1 + a.b > 2 tol, so that the full sum
            // at the exact point, where 1 + a.b moves by up to about tol, cannot take its status-1 test where the tree
            // admits; at the exact point, today's 1 + a.b > tol; the band between them is found by bisection on the
            // distance from the segment, 1 + a.b computed with the code's own expressions)
            Vector3d a0 = new Vector3d(1000, 2000, 3000), b0 = new Vector3d(41000, -7000, 9000), x1 = new Vector3d(0, 0.6, 0.8);
            Vector3d mid = (a0 + b0) * 0.5, nrm = (b0 - a0).Cross(new Vector3d(1, 0, 0)).Normalized;
            int checkedBand = 0;
            for (int i = 0; i < 400; ++i) {
                double hgt = (b0 - a0).Length * 1e-8 * Math.Pow(10, 2.5 * i / 400.0);
                Vector3d p = mid + nrm * hgt;
                double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
                foreach (double pointError in new[] { 0.0, 1e-9 }) {
                    double th, te, tol, hy;
                    int st = ExactMeshBoolean.tree_term(a0, b0, p, pm, x1, pointError, 0, out th, out te, out tol, out hy);
                    // (the same expressions as tree_term)
                    Vector3d a = a0 - p, b = b0 - p;
                    a /= Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z);
                    b /= Math.Sqrt(b.x * b.x + b.y * b.y + b.z * b.z);
                    double opp = 1 + a.Dot(b);
                    if (opp <= tol || !(tol < 0.01))
                        Assert.Equal(3, st);
                    else if (pointError > 0 && opp <= 2 * tol) {
                        Assert.Equal(3, st);
                        checkedBand++;
                    }
                }
            }
            Assert.True(checkedBand > 5, $"{checkedBand} points in the band (tol, 2 tol]");
        }

        [Fact]
        public void TreeNodes_VaErrBoundsTheRoundingOfVa_AlsoOnNearlyStraightRuns()
        {
            // (review of #813, M1: va of every node against its exact value, with c as stored; nearly straight runs have
            // |cross| about 0 while the roundings of the differences are not)
            var rand = new Random(8133);
            for (int trial = 0; trial < 6; ++trial) {
                int n = 300;
                var segs = new Vector3d[2 * n];
                Vector3d start = new Vector3d(rand.Next(1 << 20), rand.Next(1 << 20), rand.Next(1 << 20));
                Vector3d step = new Vector3d(rand.Next(1, 4096), rand.Next(1, 4096), rand.Next(1, 4096));
                for (int i = 0; i < n; ++i) {
                    // (on a line, every point moved by at most 1 grid unit: nearly straight)
                    segs[2 * i] = i == 0 ? start : segs[2 * i - 1];
                    segs[2 * i + 1] = start + step * (i + 1) + new Vector3d(rand.Next(-1, 2), rand.Next(-1, 2), 0) * (trial % 2);
                }
                var tree = ExactMeshBoolean.AntipodalTree.Build(segs);
                for (int k = 0; k < tree.Lo.Length; ++k) {
                    var pts = new List<Vector3d>();
                    for (int q = tree.Lo[k]; q < tree.Hi[k]; ++q)
                        pts.Add(segs[tree.Seg[q]]);
                    pts.Add(segs[tree.Seg[tree.Hi[k] - 1] + 1]);
                    BigInteger[] exact = ExactVa(pts, new Vector3d(tree.Cx[k], tree.Cy[k], tree.Cz[k]), out int scale);
                    double[] va = { tree.Vx[k], tree.Vy[k], tree.Vz[k] };
                    for (int c = 0; c < 3; ++c) {
                        double err = Math.Abs(va[c] - ExactPoint3.ToDouble(exact[c], BigInteger.One, -scale));
                        Assert.True(err <= tree.VaErr[k] + 1e-300, $"trial {trial} node {k}: va error {err} above VaErr {tree.VaErr[k]}");
                    }
                }
            }
        }

        // half the sum of (v_i - c) x (v_i+1 - c) over the closed polygon, exactly, times 2^scale
        static BigInteger[] ExactVa(List<Vector3d> pts, Vector3d c, out int scale)
        {
            const int S = 1100;
            scale = S + 1;
            Func<double, BigInteger> q = (v) => {
                BigInteger m; int e;
                ExactPoint3.Decompose(v, out m, out e);
                return m.IsZero ? BigInteger.Zero : (e + S >= 0 ? m << (e + S) : m >> -(e + S));
            };
            BigInteger cx = q(c.x), cy = q(c.y), cz = q(c.z);
            var sum = new BigInteger[3];
            for (int i = 0; i < pts.Count; ++i) {
                Vector3d v = pts[i], w = pts[(i + 1) % pts.Count];
                BigInteger vx = q(v.x) - cx, vy = q(v.y) - cy, vz = q(v.z) - cz, wx = q(w.x) - cx, wy = q(w.y) - cy, wz = q(w.z) - cz;
                sum[0] += vy * wz - vz * wy;
                sum[1] += vz * wx - vx * wz;
                sum[2] += vx * wy - vy * wx;
            }
            // (scaled by 2^(2 S), halved: 2^(2 S + 1); bring back to 2^scale)
            for (int k = 0; k < 3; ++k)
                sum[k] >>= S;
            return sum;
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
