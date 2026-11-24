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
    /// the padding of the winding query, the exactness test of a difference (see ExactBooleanBoundValueTests).
    /// Mirrors (change detectors of derived bounds, bit for bit): ToGrid, input_segment (value test). Properties against exact
    /// arithmetic: exact_difference, the containment of the exact segment ends in the pad of input_segment. Contract tests of
    /// policy constants: the factor of search (0.99 against 1.01 of 1e-12), the pad of patch_winding (0.999 against 1.001)
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
            ExactBooleanBoundValueTests.Field(typeof(ExactMeshBoolean), "arr").SetValue(op, arr);
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
                // property (exact arithmetic): every exact grid point within ep (eq) of gp (gq) per coordinate, in input coordinates
                // (g + c) 2^scaleExp, lies within the pad of the computed end (the extreme positions of each coordinate are the worst ones)
                for (int k = 0; k < 3; ++k)
                    foreach (int sign in new[] { -1, 1 }) {
                        AssertWithinPad(gp[k], sign * ep, center[k], scaleExp, ip[k], pad, $"trial {trial} p[{k}]");
                        AssertWithinPad(gq[k], sign * eq, center[k], scaleExp, iq[k], pad, $"trial {trial} q[{k}]");
                    }
            }
        }

        /// <summary> |(g + delta + c) 2^s - computed| &lt;= pad in exact arithmetic (g, delta, computed, pad doubles; c an integer) </summary>
        static void AssertWithinPad(double g, double delta, BigInteger c, int scaleExp, double computed, double pad, string what)
        {
            BigInteger unit = BigInteger.One << 1074;                           // Scaled() is v 2^1074
            BigInteger exact = Scaled(g) + Scaled(delta) + c * unit;           // the exact grid coordinate, in units of 2^-1074
            BigInteger lhs = Scaled(computed), padS = Scaled(pad);
            if (scaleExp >= 0) exact <<= scaleExp; else { lhs <<= -scaleExp; padS <<= -scaleExp; }
            Assert.True(BigInteger.Abs(lhs - exact) <= padS, $"{what}: the exact end is outside the pad");
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
        // search: the factor (1 + 1e-12) of the accumulated cost, the comparison with the margin, the costs per boundary
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A chain of pieces 0 - 1 - 2 - 3 (and an outsider adjacent to piece 1 but in no patch) of the fixture's output, chosen by
        /// property: the first pieces whose cost is finite and positive under every boundary given. The adjacency is the rig's own
        /// (search only reads patch, start and adj), so that the costs and the order of the pieces are the only fixture dependence
        /// </summary>
        sealed class SearchRig
        {
            public readonly ExactBooleanBoundValueTests.Fixture F;
            public readonly int[] Chain = new int[4];
            public readonly int Outsider;
            readonly int n;
            readonly int[] patch, start, adj;

            public SearchRig(ExactBooleanBoundValueTests.Fixture f, params ExactMeshBoolean.BoundaryBound[] bounds)
            {
                F = f;
                n = f.Arr.Output.Count;
                var picked = new List<int>();
                for (int p = 0; p < n && picked.Count < 5; ++p)
                    if (bounds.All(b => { double c = Cost(b, p); return c > 0 && c < double.MaxValue; }))
                        picked.Add(p);
                Assert.True(picked.Count == 5, "the fixture has fewer than 5 pieces of a finite cost under the boundaries");
                for (int i = 0; i < 4; ++i) Chain[i] = picked[i];
                Outsider = picked[4];
                var adjOf = new Dictionary<int, List<int>>();
                for (int i = 0; i < 4; ++i) {
                    adjOf[Chain[i]] = new List<int>();
                    if (i > 0) adjOf[Chain[i]].Add(Chain[i - 1]);
                    if (i + 1 < 4) adjOf[Chain[i]].Add(Chain[i + 1]);
                }
                adjOf[Chain[1]].Add(Outsider);
                adjOf[Outsider] = new List<int>();
                start = new int[n + 1];
                var list = new List<int>();
                for (int p = 0; p < n; ++p) {
                    start[p] = list.Count;
                    if (adjOf.ContainsKey(p)) list.AddRange(adjOf[p]);
                }
                start[n] = list.Count;
                adj = list.ToArray();
                patch = new int[n];
                for (int p = 0; p < n; ++p) patch[p] = adjOf.ContainsKey(p) && p != Outsider ? 0 : -1;
            }

            double Cost(ExactMeshBoolean.BoundaryBound b, int p)
            {
                return (double)ExactBooleanBoundValueTests.Call(F.Op, "piece_cost", p, b, new Dictionary<int, double>());
            }

            /// <summary> the costs of the chain's pieces under b, from a fresh cache </summary>
            public double[] Costs(ExactMeshBoolean.BoundaryBound b) { return Chain.Select(p => Cost(b, p)).ToArray(); }

            public static object NewWorkSpace()
            {
                return Activator.CreateInstance(ExactBooleanBoundValueTests.Nested(typeof(ExactMeshBoolean), "PatchSearch"), true);
            }

            /// <summary> search from the chain's first piece with the margin; returns how many it decided, and which </summary>
            public int Run(object ws, ExactMeshBoolean.BoundaryBound b, double margin, out bool[] decided)
            {
                decided = new bool[n];
                return (int)ExactBooleanBoundValueTests.Call(F.Op, "search", ws, 0, Chain[0], margin, patch, start, adj, b, decided);
            }
        }

        static ExactMeshBoolean.BoundaryBound Boundary(double reach)
        {
            return ExactMeshBoolean.boundary_bound(new[] { new Vector3d(reach, reach, reach), new Vector3d(-reach, reach, -reach), new Vector3d(reach, -reach, 0.3 * reach), new Vector3d(-reach, -0.7 * reach, reach) });
        }

        [Fact]
        public void Search_DecidesAPieceWhereItsAccumulatedCostIsBelowTheMargin_ThePlainSumTimesAFactorOfAboutOnePlusOneE12()
        {
            var f = ExactBooleanBoundValueTests.Fixture.Instance.Value;
            var b = Boundary(3e18);          // far from the model: every piece has a finite cost
            var rig = new SearchRig(f, b);
            double[] c = rig.Costs(b);
            // the plain accumulated costs of the recurrence aq(q) = (aq(p) + c(p) + c(q)) f, along the chain, without the factor
            var plain = new double[4];
            plain[1] = c[0] + c[1];
            plain[2] = plain[1] + c[1] + c[2];
            plain[3] = plain[2] + c[2] + c[3];
            Assert.True(plain[1] * 1.001 < plain[2] && plain[2] * 1.001 < plain[3], "the plain costs of the chain do not grow");
            object ws = SearchRig.NewWorkSpace();      // (one work space for all the searches: it is cleared at each start)
            for (int target = 1; target <= 3; ++target) {
                // contract of the factor f = 1 + 1e-12 (kept, not derived: it is at least the 1 + 2.2e-16 that the rounding needs, and
                // keeps the decisions of #1350): the true accumulated cost lies between plain (1 + f') and plain (1 + f')^k
                double below = plain[target] * (1 + 0.99e-12), above = plain[target] * Math.Pow(1 + 1.01e-12, target);
                bool[] d;
                int got = rig.Run(ws, b, below, out d);
                Assert.Equal(target - 1, got);
                for (int q = 1; q <= 3; ++q) Assert.Equal(q < target, d[rig.Chain[q]]);
                got = rig.Run(ws, b, above, out d);
                Assert.Equal(target, got);
                for (int q = 1; q <= 3; ++q) Assert.Equal(q <= target, d[rig.Chain[q]]);
                Assert.False(d[rig.Chain[0]]);
                Assert.False(d[rig.Outsider]);
            }
            bool[] none;
            Assert.Equal(0, rig.Run(ws, b, 0, out none));                     // no margin: nothing is decided
            Assert.Equal(0, rig.Run(ws, b, -1, out none));
            Assert.Equal(0, rig.Run(ws, b, double.NaN, out none));
        }

        [Fact]
        public void Search_OnAWorkSpaceThatServedAFarBoundary_UsesTheCostsOfTheNearerBoundaryOfTheNextOpenMesh()
        {
            // PatchSearch is per worker and reused across the patches and across the open meshes j, each with its own boundary bb[j]; its
            // cost cache is keyed by the piece only, so it must be cleared at each search: costs of a farther boundary are smaller, and
            // pieces would be decided on an accumulated variation that is too small
            var f = ExactBooleanBoundValueTests.Fixture.Instance.Value;
            var far = Boundary(3e18);
            var near = Boundary(2e17);
            var rig = new SearchRig(f, far, near);
            double[] cFar = rig.Costs(far), cNear = rig.Costs(near);
            for (int q = 0; q < 4; ++q)
                Assert.True(cNear[q] > 2 * cFar[q], $"piece {q}: nearer boundary cost {cNear[q]:R} not above twice the far one {cFar[q]:R}");
            // a margin above the accumulated costs of the far boundary for the whole chain, and below the first accumulated cost of the
            // near one: the fresh near search decides nothing, a search on the stale far costs would decide the three pieces
            double staleChain = (cFar[0] + cFar[1]) + cFar[1] + cFar[2] + cFar[2] + cFar[3];
            double margin = staleChain * 2;
            Assert.True(margin < cNear[0] + cNear[1], "the margin is not below the first accumulated cost of the nearer boundary");
            bool[] dFresh, dReused, dFar;
            Assert.Equal(0, rig.Run(SearchRig.NewWorkSpace(), near, margin, out dFresh));
            object ws = SearchRig.NewWorkSpace();
            Assert.Equal(3, rig.Run(ws, far, margin, out dFar));               // the far boundary decides the chain on the same margin
            int got = rig.Run(ws, near, margin, out dReused);                   // the same work space, the next open mesh's boundary
            Assert.Equal(0, got);
            Assert.Equal(dFresh, dReused);
            // and the other way round, from the near boundary (large costs, cached) to the far one: the far search must find its own costs
            object ws2 = SearchRig.NewWorkSpace();
            rig.Run(ws2, near, margin, out dFresh);
            Assert.Equal(3, rig.Run(ws2, far, margin, out dReused));
            Assert.Equal(dFar, dReused);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // patch_winding: the pad of the query box around the centroid (1e-9 of its L1 norm)
        // ---------------------------------------------------------------------------------------------------------------
        [Fact]
        public void PatchWinding_QueriesTheBoxesWithinAPadOfOneNanoOfTheCentroidsL1Norm()
        {
            // (an instance of its own: the test replaces the released trees of the arrangement, which the shared fixture must keep)
            var f = ExactBooleanBoundValueTests.Fixture.MakeInstance();
            var arr = f.Arr;
            // a piece whose centroid has mixed signs, every coordinate at least 15 % of the L1 norm, and the norm well away from 1
            int piece = -1;
            Vector3d pv = Vector3d.Zero;
            for (int i = 0; i < arr.Output.Count && piece < 0; ++i) {
                ExactPoint3 c = (ExactPoint3)ExactBooleanBoundValueTests.Call(f.Op, "centroid_point", arr.Output[i], true);
                Vector3d v = arr.ToInputVector(c);
                double l1 = Math.Abs(v.x) + Math.Abs(v.y) + Math.Abs(v.z);
                double min = Math.Min(Math.Abs(v.x), Math.Min(Math.Abs(v.y), Math.Abs(v.z)));
                bool mixed = (v.x < 0 || v.y < 0 || v.z < 0) && (v.x > 0 || v.y > 0 || v.z > 0);
                if (mixed && min > 0.15 * l1 && l1 > 1.8) { piece = i; pv = v; }
            }
            Assert.True(piece >= 0, "no piece with such a centroid in the fixture");
            // (the finished computation has released arr.Trees; patch_winding evaluates the winding number of every candidate mesh with
            // them after it has made the candidate list. Empty trees stand in: no triangle is met, the winding numbers are 0)
            Assert.Null(arr.Trees);
            var emptyTree = new TriangleBoxTree(new List<AxisAlignedBox3d> { AxisAlignedBox3d.Empty });
            arr.Trees = new[] { emptyTree, emptyTree };
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
                        ExactBooleanBoundValueTests.Call(f.Op, "patch_winding", piece, new List<int>(), tree, new List<int>(), candidates);
                        Assert.DoesNotContain(99, candidates);
                        bool has = candidates.Contains(0);
                        Assert.True(has == (factor < 1), $"axis {axis} dir {dir} factor {factor}: candidates {string.Join(",", candidates)}");
                        if (has) ++included;
                    }
            Assert.Equal(6, included);
        }
    }
}
