using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1423, part 2: the error terms of the local frame and the centroid segment, the Dijkstra factor of the patch search,
    /// the padding of the winding query, the exactness test of a difference (see ExactBooleanBoundValueTests)
    /// </summary>
    public class ExactBooleanBoundValueFrameTests
    {
        const double U = 1.1102230246251565e-16;

        // ---------------------------------------------------------------------------------------------------------------
        // LocalFrame.exact_difference: a - b is exact iff TwoSum's error term is 0
        // ---------------------------------------------------------------------------------------------------------------
        static BigInteger Scaled(double v)       // v 2^1074, an integer for every finite double
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            BigInteger mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            if (bits < 0) mant = -mant;
            return mant << (exp - 1);
        }

        [Fact]
        public void ExactDifference_IsTrueExactlyWhereTheDifferenceIsAnExactDouble()
        {
            var rnd = new Random(77);
            int exact = 0, inexact = 0;
            var cases = new List<double[]>();
            for (int i = 0; i < 4000; ++i) {
                double a = (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(-30, 30)), b = (rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(-30, 30));
                if (i % 3 == 0) { a = Math.Round(a * 1024) / 1024; b = Math.Round(b * 1024) / 1024; }      // many exact ones
                if (i % 7 == 0) b = -b;
                cases.Add(new[] { a, b });
            }
            cases.Add(new[] { 1.0, 1e-20 }); cases.Add(new[] { 1e-20, 1.0 }); cases.Add(new[] { -1.0, 1e-20 }); cases.Add(new[] { 0.0, 0.0 });
            cases.Add(new[] { 1.0, 0.0 }); cases.Add(new[] { 0.0, 1e-300 }); cases.Add(new[] { 3.0, 3.0 }); cases.Add(new[] { 1.0, 0.5 });
            foreach (var c in cases) {
                double a = c[0], b = c[1], s;
                bool got = ExactMeshBoolean.LocalFrame.exact_difference(a, b, out s);
                Assert.Equal(a - b, s);
                bool truth = Scaled(a) - Scaled(b) == Scaled(a - b);
                Assert.True(truth == got, $"a {a:R} b {b:R}: exact {truth}, got {got}");
                if (truth) ++exact; else ++inexact;
            }
            Assert.True(exact > 500 && inexact > 500);
        }

        [Fact]
        public void ExactDifference_KnownCases_AlsoWherePartsOfTheErrorTermAreEqual()
        {
            double s;
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(1.0, Math.Pow(2, -60), out s));
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(1.0, -Math.Pow(2, -60), out s));
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(Math.Pow(2, -60), 1.0, out s));
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(-Math.Pow(2, 60), 1.0, out s));
            Assert.True(ExactMeshBoolean.LocalFrame.exact_difference(1.0, 0.25, out s)); Assert.Equal(0.75, s);
            Assert.True(ExactMeshBoolean.LocalFrame.exact_difference(-3.0, 0.5, out s)); Assert.Equal(-3.5, s);
            Assert.False(ExactMeshBoolean.LocalFrame.exact_difference(double.MaxValue, -double.MaxValue, out s));      // infinite
            // a grid of near-cancelling pairs, against the exact value
            for (int i = 1; i < 3000; ++i)
                foreach (double scale in new[] { 1.0, Math.Pow(2, -60), Math.Pow(2, -53), Math.Pow(2, -54), Math.Pow(2, -52) }) {
                    double a = 1 + i * Math.Pow(2, -52), b = -scale * (1 + (i % 7) * Math.Pow(2, -52));
                    bool got = ExactMeshBoolean.LocalFrame.exact_difference(a, b, out s);
                    Assert.True((Scaled(a) - Scaled(b) == Scaled(a - b)) == got, $"a {a:R} b {b:R}");
                    got = ExactMeshBoolean.LocalFrame.exact_difference(b, a, out s);
                    Assert.True((Scaled(b) - Scaled(a) == Scaled(b - a)) == got, $"b {b:R} a {a:R}");
                }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // LocalFrame.ToGrid: the grid error of a local point
        // ---------------------------------------------------------------------------------------------------------------
        [Fact]
        public void ToGrid_IsTheSumAndItsErrorBound_ForOriginsOfEveryTriangleOfTheFixture()
        {
            var f = ExactBooleanBoundValueTests.Fixture.Instance.Value;
            var rnd = new Random(3);
            int checkedFrames = 0;
            for (int ti = 0; ti < f.Arr.Triangles.Count && ti < 12; ++ti) {
                var frame = new ExactMeshBoolean.LocalFrame(f.Op, f.Arr.Triangles[ti]);
                Vector3d od = frame.Origin;
                double odError = 0;
                for (int k = 0; k < 3; ++k) odError = Math.Max(odError, 2.3e-16 * Math.Abs(od[k]));
                for (int trial = 0; trial < 5; ++trial) {
                    // (mixed signs, magnitudes far below the origin's: |l| + |od| differs from |l| - |od|)
                    var l = new Vector3d((rnd.NextDouble() - 0.5) * 4000, (rnd.NextDouble() - 0.5) * 4000, (rnd.NextDouble() - 0.5) * 4000);
                    double error = trial == 0 ? 0 : rnd.NextDouble() * 10;
                    double ge;
                    Vector3d g = frame.ToGrid(l, error, out ge);
                    Assert.Equal(l + od, g);
                    double m = 0;
                    for (int k = 0; k < 3; ++k) m = Math.Max(m, Math.Abs(l[k]) + Math.Abs(od[k]));
                    Assert.Equal((error + odError) * (1 + 1e-15) + 1.2e-16 * m, ge);
                    Assert.True(ge >= error + odError);
                }
                ++checkedFrames;
            }
            Assert.True(checkedFrames >= 6);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // input_segment: the pad of the segment between two centroids in input coordinates
        // ---------------------------------------------------------------------------------------------------------------
        static ExactMeshBoolean OpWithGrid(int scaleExp, BigInteger[] center)
        {
            var m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            var m2 = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            var op = new ExactMeshBoolean(m, m2, ExactMeshBoolean.Operations.Union);
            var arr = new ExactMeshArrangement(new List<DMesh3> { m, m2 }) { ScaleExp = scaleExp, Center = center };
            typeof(ExactMeshBoolean).GetField("arr", ExactBooleanBoundValueTests.NP).SetValue(op, arr);
            return op;
        }

        [Theory]
        [InlineData(0)] [InlineData(-54)] [InlineData(20)]
        public void InputSegment_IsTheScaledEndpointsAndTheDocumentedPad(int scaleExp)
        {
            var center = new[] { new BigInteger(123456789012345L), new BigInteger(-98765432109876L), new BigInteger(4503599627370497L) };
            var op = OpWithGrid(scaleExp, center);
            var rnd = new Random(9);
            for (int trial = 0; trial < 40; ++trial) {
                // (mixed signs, magnitudes around the centre's, errors of 1e-3 .. 1e3)
                Func<Vector3d> pt = () => new Vector3d((rnd.NextDouble() - 0.5) * 1e14, (rnd.NextDouble() - 0.5) * 1e14 * (trial % 2 + 1), (rnd.NextDouble() - 0.5) * 1e15);
                Vector3d gp = pt(), gq = pt();
                double ep = Math.Pow(10, rnd.Next(-3, 4)) * (1 + rnd.NextDouble()), eq = Math.Pow(10, rnd.Next(-3, 4)) * (1 + rnd.NextDouble());
                Vector3d ip, iq;
                double pad;
                Assert.True(op.input_segment(gp, ep, gq, eq, out ip, out iq, out pad));
                double e = 0;
                for (int k = 0; k < 3; ++k) {
                    double c = (double)center[k];
                    Assert.Equal(ExactPoint3.ScaleB(gp[k] + c, scaleExp), ip[k]);
                    Assert.Equal(ExactPoint3.ScaleB(gq[k] + c, scaleExp), iq[k]);
                    e = Math.Max(e, Math.Max(ep + 3.4e-16 * (Math.Abs(gp[k]) + Math.Abs(c)), eq + 3.4e-16 * (Math.Abs(gq[k]) + Math.Abs(c))));
                }
                Assert.Equal(1.01 * ExactPoint3.ScaleB(e, scaleExp) + 1e-300, pad);
            }
        }

        [Fact]
        public void InputSegment_OfZeroErrorsAtTheCentre_HasThePadOfTheSubnormalTermOnly()
        {
            var op = OpWithGrid(0, new[] { BigInteger.Zero, BigInteger.Zero, BigInteger.Zero });
            Vector3d ip, iq;
            double pad;
            Assert.True(op.input_segment(Vector3d.Zero, 0, Vector3d.Zero, 0, out ip, out iq, out pad));
            Assert.Equal(1e-300, pad);
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
        public void InputSegment_RefusesASumThatOverflowsOnlyThroughOneOfItsSevenTerms(int position)
        {
            var op = OpWithGrid(0, new[] { BigInteger.Zero, BigInteger.Zero, BigInteger.Zero });
            double big = 1e308;
            // terms in order: ip.x ip.y ip.z iq.x iq.y iq.z pad; the terms position - 1 and position are big, the others 0:
            // their sum overflows, but not if the operator in front of the term at `position` were a minus
            var t = new double[7];
            t[position - 1] = big; t[position] = big;
            Vector3d gp = new Vector3d(t[0], t[1], t[2]), gq = new Vector3d(t[3], t[4], t[5]);
            double ep = 0, eq = 0;
            if (position == 6) { ep = 0.99e308; }            // pad = 1.01 e: about 1e308
            Vector3d ip, iq;
            double pad;
            bool ok = op.input_segment(gp, ep, gq, eq, out ip, out iq, out pad);
            if (position == 6)
                Assert.True(pad > 0.99e308 && pad < 1.1e308, $"pad {pad}");
            Assert.False(ok);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // search: the factor (1 + 1e-12) of the accumulated cost and the comparison with the margin
        // ---------------------------------------------------------------------------------------------------------------
        [Fact]
        public void Search_AccumulatesTheCostsWithTheFactor_AndDecidesOnlyBelowTheMargin()
        {
            var f = ExactBooleanBoundValueTests.Fixture.Instance.Value;
            var arr = f.Arr;
            int n = arr.Output.Count;
            // a boundary far from the model: every piece has a finite cost
            var far = new[] { new Vector3d(3e18, 3e18, 3e18), new Vector3d(-3e18, 3e18, -3e18), new Vector3d(2e18, -3e18, 1e18), new Vector3d(-2e18, -2e18, 3e18) };
            var b = ExactMeshBoolean.boundary_bound(far);
            int[] chain = { 5, 17, 40, 77 };
            const int outsider = 90;           // adjacent to the second piece of the chain, in no patch: never reached
            var cache = new Dictionary<int, double>();
            double[] c = chain.Select(p => (double)ExactBooleanBoundValueTests.Call(f.Op, "piece_cost", p, b, cache)).ToArray();
            Assert.All(c, x => Assert.True(x > 0 && x < double.MaxValue));
            // the chain 0 - 1 - 2 - 3 as adjacency of the pieces 5, 17, 40, 77
            var adjOf = new Dictionary<int, List<int>>();
            for (int i = 0; i < chain.Length; ++i) {
                adjOf[chain[i]] = new List<int>();
                if (i > 0) adjOf[chain[i]].Add(chain[i - 1]);
                if (i + 1 < chain.Length) adjOf[chain[i]].Add(chain[i + 1]);
            }
            adjOf[chain[1]].Add(outsider);
            adjOf[outsider] = new List<int>();
            var start = new int[n + 1];
            var adj = new List<int>();
            for (int p = 0; p < n; ++p) {
                start[p] = adj.Count;
                if (adjOf.ContainsKey(p)) adj.AddRange(adjOf[p]);
            }
            start[n] = adj.Count;
            var patch = new int[n];
            for (int p = 0; p < n; ++p) patch[p] = adjOf.ContainsKey(p) && p != outsider ? 0 : -1;
            // the accumulated costs of the documented recurrence
            double factor = 1 + 1e-12;
            var aq = new double[4];
            aq[1] = (0 + c[0] + c[1]) * factor;
            aq[2] = (aq[1] + c[1] + c[2]) * factor;
            aq[3] = (aq[2] + c[2] + c[3]) * factor;
            Assert.True(aq[1] < aq[2] && aq[2] < aq[3]);
            Type wsType = typeof(ExactMeshBoolean).GetNestedType("PatchSearch", BindingFlags.NonPublic);
            object reuse = null;        // (one work space for all the searches: its heap and sums are cleared at each start)
            // margin: just above, exactly at, and just below each accumulated cost: the pieces below the margin are decided
            for (int target = 1; target <= 3; ++target) {
                foreach (int delta in new[] { -1, 0, 1 }) {
                    double margin = delta == 0 ? aq[target] : delta < 0 ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(aq[target]) - 1)
                                                                       : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(aq[target]) + 1);
                    int expected = 0;
                    for (int q = 1; q <= 3; ++q) if (aq[q] < margin) ++expected;
                    var decided = new bool[n];
                    object ws = reuse ?? Activator.CreateInstance(wsType, true);
                    reuse = ws;
                    int got = (int)ExactBooleanBoundValueTests.Call(f.Op, "search", ws, 0, chain[0], margin, patch, start, adj.ToArray(), b, decided);
                    Assert.Equal(expected, got);
                    for (int q = 1; q <= 3; ++q)
                        Assert.Equal(aq[q] < margin, decided[chain[q]]);
                    Assert.False(decided[chain[0]]);
                    Assert.False(decided[outsider]);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // patch_winding: the pad of the query box around the centroid (1e-9 of its L1 norm)
        // ---------------------------------------------------------------------------------------------------------------
        [Fact]
        public void PatchWinding_QueriesTheBoxesWithinAPadOfOneNanoOfTheCentroidsL1Norm()
        {
            var f = ExactBooleanBoundValueTests.Fixture.Instance.Value;
            var arr = f.Arr;
            // a piece whose centroid has mixed signs, every coordinate at least 15 % of the L1 norm, and the norm well away from 1
            int piece = -1;
            Vector3d pv = Vector3d.Zero;
            for (int i = 0; i < arr.Output.Count && piece < 0; ++i) {
                ExactPoint3 c = (ExactPoint3)typeof(ExactMeshBoolean).GetMethod("centroid_point", ExactBooleanBoundValueTests.NP).Invoke(f.Op, new object[] { arr.Output[i], true });
                Vector3d v = arr.ToInputVector(c);
                double l1 = Math.Abs(v.x) + Math.Abs(v.y) + Math.Abs(v.z);
                double min = Math.Min(Math.Abs(v.x), Math.Min(Math.Abs(v.y), Math.Abs(v.z)));
                bool mixed = (v.x < 0 || v.y < 0 || v.z < 0) && (v.x > 0 || v.y > 0 || v.z > 0);
                if (mixed && min > 0.15 * l1 && l1 > 1.8) { piece = i; pv = v; }
            }
            Assert.True(piece >= 0, "no piece with such a centroid in the fixture");
            double pad = 1e-9 * (Math.Abs(pv.x) + Math.Abs(pv.y) + Math.Abs(pv.z)) + 1e-300;
            int included = 0;
            for (int axis = 0; axis < 3; ++axis)
                foreach (int dir in new[] { -1, 1 })
                    foreach (double factor in new[] { 0.999, 1.001 }) {
                        // a tiny box of mesh 0, its nearest face at the distance factor * pad from the centroid along one axis
                        Vector3d lo = pv - new Vector3d(1e-7, 1e-7, 1e-7), hi = pv + new Vector3d(1e-7, 1e-7, 1e-7);
                        double gap = factor * pad;
                        if (dir > 0) { lo[axis] = pv[axis] + gap; hi[axis] = pv[axis] + gap + 1e-7; }
                        else { hi[axis] = pv[axis] - gap; lo[axis] = pv[axis] - gap - 1e-7; }
                        var boxes = new List<AxisAlignedBox3d> { new AxisAlignedBox3d(lo, hi), AxisAlignedBox3d.Empty };
                        var tree = new TriangleBoxTree(boxes);
                        var candidates = new List<int> { 99 };       // (stale content: cleared first)
                        // (the list of candidate meshes is complete before the winding numbers are evaluated, which needs the trees that the
                        // finished computation has released: a candidate then ends in a NullReferenceException, after the list was made)
                        try { ExactBooleanBoundValueTests.Call(f.Op, "patch_winding", piece, new List<int>(), tree, new List<int>(), candidates); }
                        catch (NullReferenceException) { }
                        Assert.DoesNotContain(99, candidates);
                        bool has = candidates.Contains(0);
                        Assert.True(has == (factor < 1), $"axis {axis} dir {dir} factor {factor}: candidates {string.Join(",", candidates)}");
                        if (has) ++included;
                    }
            Assert.Equal(6, included);
        }
    }
}
