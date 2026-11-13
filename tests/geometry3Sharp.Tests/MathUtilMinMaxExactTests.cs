// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MathUtil.MinExact / MaxExact (issue 803): one result on every runtime where Math.Min / Math.Max differ. For a +0
    /// and a -0, .NET returns the IEEE 754-2019 minimum / maximum (-0 / +0) and .NET Framework the second operand, so
    /// a value that reaches a result differed in the sign of zero. The bits are pinned here; the tests run on net10 and,
    /// in CI, on net48.
    /// </summary>
    public class MathUtilMinMaxExactTests
    {
        static long Bits(double x) { return BitConverter.DoubleToInt64Bits(x); }
        const long PlusZero = 0L, MinusZero = unchecked((long)0x8000000000000000UL);
        static readonly bool Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal);

        // (through arrays and a call that is not inlined, so that nothing is folded at compile or JIT time)
        static readonly double[] Values = { 0.0, -0.0, 1.0, -1.0, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue,
            double.PositiveInfinity, double.NegativeInfinity, 2.5, -2.5, double.NaN };
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static double At(int i) { return Values[i]; }

        [Fact]
        public void Zeros_MinIsMinusZero_MaxIsPlusZero_InBothOrders()
        {
            double pz = At(0), mz = At(1);
            Assert.Equal(PlusZero, Bits(pz));
            Assert.Equal(MinusZero, Bits(mz));
            Assert.Equal(MinusZero, Bits(MathUtil.MinExact(pz, mz)));
            Assert.Equal(MinusZero, Bits(MathUtil.MinExact(mz, pz)));
            Assert.Equal(PlusZero, Bits(MathUtil.MaxExact(pz, mz)));
            Assert.Equal(PlusZero, Bits(MathUtil.MaxExact(mz, pz)));
            // equal zeros keep their sign
            Assert.Equal(PlusZero, Bits(MathUtil.MinExact(pz, pz)));
            Assert.Equal(MinusZero, Bits(MathUtil.MinExact(mz, mz)));
            Assert.Equal(PlusZero, Bits(MathUtil.MaxExact(pz, pz)));
            Assert.Equal(MinusZero, Bits(MathUtil.MaxExact(mz, mz)));
            // the clamp of SnapRounding2 (Max(0, Min(1, q))): a -0 parameter becomes +0
            Assert.Equal(PlusZero, Bits(MathUtil.MaxExact(0, MathUtil.MinExact(1, mz))));
            Assert.Equal(PlusZero, Bits(MathUtil.MaxExact(0, MathUtil.MinExact(1, pz))));
            // the bound of the snap certificate (Max(up, -low)) with up = low = +0
            Assert.Equal(PlusZero, Bits(MathUtil.MaxExact(pz, -pz)));
        }

        [Fact]
        public void TheRuntimesMathMinMax_DifferExactlyWhereThisTestSays()
        {
            // what the helper replaces: on .NET Framework the second operand, on .NET the IEEE result. (If a runtime changes
            // this, the helper stays right and this test says that the reason for it has moved)
            double pz = At(0), mz = At(1);
            Assert.Equal(MinusZero, Bits(Math.Min(pz, mz)));
            Assert.Equal(PlusZero, Bits(Math.Max(mz, pz)));
            Assert.Equal(Framework ? PlusZero : MinusZero, Bits(Math.Min(mz, pz)));
            Assert.Equal(Framework ? MinusZero : PlusZero, Bits(Math.Max(pz, mz)));
        }

        [Fact]
        public void AllPairs_SymmetricAndOrdered_NaNPropagates()
        {
            for (int i = 0; i < Values.Length; ++i)
                for (int j = 0; j < Values.Length; ++j) {
                    double a = At(i), b = At(j);
                    double mn = MathUtil.MinExact(a, b), mx = MathUtil.MaxExact(a, b);
                    string what = $"({a:R}, {b:R})";
                    // symmetric, bit for bit
                    Assert.True(Bits(mn) == Bits(MathUtil.MinExact(b, a)), "min " + what);
                    Assert.True(Bits(mx) == Bits(MathUtil.MaxExact(b, a)), "max " + what);
                    if (double.IsNaN(a) || double.IsNaN(b)) {
                        Assert.True(double.IsNaN(mn) && double.IsNaN(mx), what);
                        Assert.True(double.IsNaN(Math.Min(a, b)) && double.IsNaN(Math.Max(a, b)), "Math " + what);       // (as Math.Min / Max)
                        continue;
                    }
                    // one of the operands, bit for bit, and in order
                    Assert.True(Bits(mn) == Bits(a) || Bits(mn) == Bits(b), "min " + what);
                    Assert.True(Bits(mx) == Bits(a) || Bits(mx) == Bits(b), "max " + what);
                    Assert.True(mn <= a && mn <= b && mx >= a && mx >= b, what);
                    // the two results are the two operands
                    Assert.True((Bits(mn) == Bits(a) && Bits(mx) == Bits(b)) || (Bits(mn) == Bits(b) && Bits(mx) == Bits(a)), what);
                    // the value is Math.Min / Max's everywhere; the bits too, except for two zeros of opposite sign
                    Assert.True(mn == Math.Min(a, b) && mx == Math.Max(a, b), what);
                    if (!(a == 0 && b == 0 && Bits(a) != Bits(b))) {
                        Assert.True(Bits(mn) == Bits(Math.Min(a, b)), "min bits " + what);
                        Assert.True(Bits(mx) == Bits(Math.Max(a, b)), "max bits " + what);
                    }
                }
        }

        // the rule of the helper, written out independently: IEEE 754-2019 minimum / maximum with double.NaN for a NaN
        static double RefMin(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            if (a == 0 && b == 0) return (Bits(a) == MinusZero || Bits(b) == MinusZero) ? -0.0 : 0.0;
            return a < b ? a : b;
        }
        static double RefMax(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            if (a == 0 && b == 0) return (Bits(a) == PlusZero || Bits(b) == PlusZero) ? 0.0 : -0.0;
            return a > b ? a : b;
        }

        [Fact]
        public void RandomPairsWithSpecialValues_AsTheRule_BitForBit()
        {
            // random bit patterns (NaNs with payloads among them), every third operand one of the special values, so that
            // equal operands, zeros of both signs and NaN meet each other and random values
            ulong s = 2026;
            Func<ulong> next = () => {
                ulong z = (s += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            };
            int zeros = 0, nans = 0, equal = 0;
            for (int i = 0; i < 200000; ++i) {
                ulong r = next();
                double a = (r % 3 == 0) ? At((int)((r >> 8) % (ulong)Values.Length)) : BitConverter.Int64BitsToDouble((long)next());
                double b = ((r >> 4) % 3 == 0) ? At((int)((r >> 20) % (ulong)Values.Length)) : BitConverter.Int64BitsToDouble((long)next());
                if (a == 0 && b == 0 && Bits(a) != Bits(b)) zeros++;
                if (double.IsNaN(a) || double.IsNaN(b)) nans++;
                if (a == b) equal++;
                Assert.True(Bits(RefMin(a, b)) == Bits(MathUtil.MinExact(a, b)), $"min ({a:R}, {b:R})");
                Assert.True(Bits(RefMax(a, b)) == Bits(MathUtil.MaxExact(a, b)), $"max ({a:R}, {b:R})");
            }
            Assert.True(zeros > 100 && nans > 1000 && equal > 1000, $"{zeros} {nans} {equal}");
        }

        // ---------------------------------------------------------------- a call site

        // two crease vertices p (0, 0) and q (dx, 0) inside a ring W, S, E, N (the mesh of TerrainSlopeBandMergeTests): the
        // band merge collapses pq onto p, which takes the lowest of the planes of both at its position
        static DMesh3 NearQuadruple(out System.Collections.Generic.List<System.Collections.Generic.List<int>> labels, out System.Collections.Generic.List<bool> onBarrier)
        {
            var m = new DMesh3(false, false, false, true);
            int p = m.AppendVertex(new Vector3d(0, 0, 0)), q = m.AppendVertex(new Vector3d(1e-9, 0, 0));
            int w = m.AppendVertex(new Vector3d(-10, 0, 0)), so = m.AppendVertex(new Vector3d(0, -10, 0));
            int e = m.AppendVertex(new Vector3d(10, 0, 0)), n = m.AppendVertex(new Vector3d(0, 10, 0));
            foreach (var t in new[] { new Index3i(w, so, p), new Index3i(p, so, q), new Index3i(q, so, e), new Index3i(e, n, q), new Index3i(q, n, p), new Index3i(p, n, w) })
                Assert.True(m.AppendTriangle(t) >= 0);
            labels = new System.Collections.Generic.List<System.Collections.Generic.List<int>> {
                new System.Collections.Generic.List<int> { 1, 2, 3 }, new System.Collections.Generic.List<int> { 2, 3, 4 },
                new System.Collections.Generic.List<int> { 1 }, new System.Collections.Generic.List<int> { 2 },
                new System.Collections.Generic.List<int> { 3 }, new System.Collections.Generic.List<int> { 4 } };
            onBarrier = new System.Collections.Generic.List<bool> { false, false, false, false, false, false };
            return m;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void BandMerge_HeightOfPlanesAtPlusAndMinusZero_OneZeroWhicheverPlaneComesLast(int minusZeroParity)
        {
            // the planes of the kept vertex pass through height zero, some as +0 and some as -0 (which the exact quotient
            // gives for a zero of either sign). With Math.Min the height of the output vertex was the zero of the plane that
            // came last on .NET Framework (+0 for one parity, -0 for the other) and -0 on .NET; now -0 everywhere, and for
            // fill, where the height is mirrored, +0
            double pz = At(0), mz = At(1);
            Func<int, double, double, double> height = (l, x, y) => (l % 2 == minusZeroParity) ? mz : pz;
            foreach (bool fill in new[] { false, true }) {
                System.Collections.Generic.List<System.Collections.Generic.List<int>> labels; System.Collections.Generic.List<bool> onBarrier;
                DMesh3 m = NearQuadruple(out labels, out onBarrier);
                double merged;
                Assert.Equal(1, gs.SlopeMap.BandMerge(m, labels, onBarrier, height, 1e-6, fill, null, out merged));
                Assert.True(m.IsVertex(0) && m.IsVertex(1) == false);
                Assert.Equal(4, labels[0].Count);
                Vector3d v = m.GetVertex(0);
                Assert.Equal(PlusZero, Bits(v.x));
                Assert.Equal(fill ? PlusZero : MinusZero, Bits(v.z));
            }
        }
    }
}
