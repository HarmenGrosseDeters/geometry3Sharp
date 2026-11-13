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

        [Fact]
        public void RandomPairs_AsMathMinMax_BitForBit()
        {
            ulong s = 2026;
            Func<double> next = () => {
                ulong z = (s += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                double x = BitConverter.Int64BitsToDouble((long)z);       // any bit pattern
                return double.IsNaN(x) ? 0.5 : x;
            };
            for (int i = 0; i < 200000; ++i) {
                double a = next(), b = (i % 7 == 0) ? a : next();
                Assert.Equal(Bits(Math.Min(a, b)), Bits(MathUtil.MinExact(a, b)));
                Assert.Equal(Bits(Math.Max(a, b)), Bits(MathUtil.MaxExact(a, b)));
            }
        }
    }
}
