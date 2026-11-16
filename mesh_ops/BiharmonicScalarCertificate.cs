// Distributed under the Boost Software License, Version 1.0.
// Scalar Rump verification; see docs/design/BIHARMONIC_CERTIFICATE_PROOF.md.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Interval = g3.BiharmonicFixedInterval;

namespace g3
{

internal static class BiharmonicScalarCertificate
{
    // Exact unit for every finite binary64 value, including subnormals.
    const int UnitBits = 1074;
    const double MinNormal = 2.2250738585072014E-308;
    internal static BigInteger Units(double value)
    {
        if (!MathUtil.IsFinite(value)) throw new ArithmeticException("nonfinite");
        long raw = BitConverter.DoubleToInt64Bits(value);
        int field = (int)((raw >> 52) & 2047);
        BigInteger mantissa = raw & 0x000fffffffffffffL;
        if (field != 0) mantissa |= BigInteger.One << 52;
        var units = mantissa << (field == 0 ? 0 : field-1);
        return raw < 0 ? -units : units;
    }
    internal static double Directed(BigInteger units, bool upward)
    {
        if (units.Sign < 0) return -Directed(-units, !upward);
        if (units.IsZero) return 0;
        byte[] bytes = units.ToByteArray();
        int last = bytes.Length-1; while (last > 0 && bytes[last] == 0) last--;
        int length = last*8; byte top=bytes[last]; while(top!=0) {length++;top>>=1;}
        if (length <= 52) return BitConverter.Int64BitsToDouble((long)units);
        int cut = length-53, field = length-52;
        BigInteger mantissa = units >> cut;
        if (upward && (mantissa << cut) != units) mantissa++;
        if (mantissa == (BigInteger.One << 53)) { mantissa >>= 1; field++; }
        if (field >= 2047) throw new ArithmeticException("bound overflow");
        return BitConverter.Int64BitsToDouble(((long)field << 52) | ((long)mantissa & 0x000fffffffffffffL));
    }
    static double Checked(double value)
    {
        long magnitude = BitConverter.DoubleToInt64Bits(value) & long.MaxValue;
        if (magnitude >= 0x7ff0000000000000L || (magnitude != 0 && magnitude < 0x0010000000000000L))
            throw new ArithmeticException("nonfinite or subnormal");
        return value;
    }
    sealed class Arithmetic
    {
        internal long Products;
        internal long Limit;
        // Separate non-inlined product prevents contraction into a fused subtract.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal double Product(double a, double b) {
            if (++Products > Limit) throw new ArithmeticException("product budget");
            double result = Checked(a*b);
            if ((result == 0 && a != 0 && b != 0) || Math.Abs(result) == MinNormal)
                throw new ArithmeticException("product underflow boundary");
            return result;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal double Difference(double a, double b) => Checked(a-b);
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal double Quotient(double a, double b) {
            double result = Checked(a/b);
            if ((result == 0 && a != 0) || Math.Abs(result) == MinNormal)
                throw new ArithmeticException("division underflow boundary");
            return result;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal double Root(double a) => Checked(Math.Sqrt(a));
    }
    // Supported execution contract: .NET 10+ x64, nearest-even binary64,
    // gradual underflow, no floating-point control changes during Compute.
    internal static bool IsSupported {
        get {
#if NET10_0_OR_GREATER
            return RuntimeInformation.ProcessArchitecture == Architecture.X64 && EnvironmentIsStandard();
#else
            return false;
#endif
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static double Multiply(double a,double b) => a*b;
    [MethodImpl(MethodImplOptions.NoInlining)]
    static double Sum(double a,double b) => a+b;
    static bool EnvironmentIsStandard() =>
        BitConverter.DoubleToInt64Bits(Multiply(MinNormal,0.5)) == 0x0008000000000000L &&
        Multiply(double.Epsilon,4503599627370496.0) == MinNormal &&
        Sum(1,1.1102230246251565E-16) == 1 &&
        Sum(1,-5.5511151231257827E-17) == 1 &&
        Sum(1.0000000000000002,1.1102230246251565E-16) == 1.0000000000000004;

    internal static bool Verify(Dictionary<int,Interval>[] input, long remainingUpdates = 1000000, int maxLiveEntries = 50000)
    {
        if (!IsSupported) return false;
        int n = input.Length;
        var arithmetic = new Arithmetic { Limit = remainingUpdates };
        try {
            if (n < 1 || n > 10000 || Interval.Bits > UnitBits) throw new ArithmeticException("size/precision cap");
            var columns = new HashSet<int>[n]; var matrix = new Dictionary<int,double>[n];
            var radiusRows = new BigInteger[n]; BigInteger diagonalSum = 0;
            for (int c = 0; c < n; c++) {
                columns[c] = new HashSet<int>(); matrix[c] = new Dictionary<int,double>();
                foreach (var entry in input[c]) {
                    int r = entry.Key; if (r < c || r >= n) throw new ArithmeticException("lower pattern");
                    BigInteger lo = entry.Value.Lo << (UnitBits-Interval.Bits), hi = entry.Value.Hi << (UnitBits-Interval.Bits);
                    double midpoint = Checked(Directed((lo+hi)>>1, false));
                    BigInteger center = Units(midpoint), radius = BigInteger.Max(BigInteger.Abs(center-lo),BigInteger.Abs(hi-center));
                    radiusRows[r] += radius; if (r != c) radiusRows[c] += radius;
                    matrix[c].Add(r,midpoint); if (r != c) columns[c].Add(r);
                    if (r == c) { if (midpoint <= 0) throw new ArithmeticException("nonpositive diagonal"); diagonalSum += center; }
                }
                if (!matrix[c].ContainsKey(c)) throw new ArithmeticException("missing diagonal");
            }
            BigInteger radiusMax = 0; foreach (var radius in radiusRows) radiusMax = BigInteger.Max(radiusMax,radius);
            // gamma/(1-gamma) = (n+1)/(2^53-2(n+1)); the exact
            // trace is in 2^-1074 units, and the quotient is rounded upward.
            BigInteger denominator = (BigInteger.One << 53)-2*(n+1), remainder;
            BigInteger correction = BigInteger.DivRem(diagonalSum*(n+1),denominator,out remainder);
            if (!remainder.IsZero) correction++;
            for (int i = 0; i < n; i++) {
                matrix[i][i] = Checked(Directed(Units(matrix[i][i])-correction-radiusMax,false));
            }
            var rows = new List<KeyValuePair<int,double>>[n]; for (int i = 0; i < n; i++) rows[i] = new List<KeyValuePair<int,double>>();
            long symbolic = 0, stored = 0, liveEntries = n;
            foreach (var column in columns) { stored += column.Count; liveEntries += column.Count; }
            if (liveEntries > maxLiveEntries || remainingUpdates < 0) return false;
            if (stored > 2000000) return false;
            for (int j = 0; j < n; j++) {
                var neighbors = new List<int>(columns[j]); neighbors.Sort();
                long work = (long)neighbors.Count*(neighbors.Count+1)/2;
                if (work > remainingUpdates-symbolic) throw new ArithmeticException("symbolic budget");
                symbolic += work;
                for (int p = 0; p < neighbors.Count; p++) for (int q = p+1; q < neighbors.Count; q++) if(columns[neighbors[p]].Add(neighbors[q])) {
                    if (++stored > 2000000 || ++liveEntries > maxLiveEntries) return false;
                }
                stored -= columns[j].Count; liveEntries -= columns[j].Count+1; columns[j].Clear();
                double diagonal = matrix[j][j];
                foreach (var entry in rows[j]) diagonal = arithmetic.Difference(diagonal,arithmetic.Product(entry.Value,entry.Value));
                if (!(diagonal > 0)) throw new ArithmeticException("nonpositive scalar pivot at "+j);
                double pivot = arithmetic.Root(diagonal);
                foreach (int i in neighbors) {
                    double value; if (!matrix[j].TryGetValue(i,out value)) value = 0;
                    int a = 0, b = 0;
                    while (a < rows[i].Count && b < rows[j].Count) {
                        var x = rows[i][a]; var y = rows[j][b];
                        if (x.Key < y.Key) a++; else if (x.Key > y.Key) b++;
                        else { value = arithmetic.Difference(value,arithmetic.Product(x.Value,y.Value)); a++; b++; }
                    }
                    if (++stored > 2000000) return false;
                    rows[i].Add(new KeyValuePair<int,double>(j,arithmetic.Quotient(value,pivot)));
                }
                matrix[j].Clear();
            }
            return true;
        }
        catch (ArithmeticException) {
            return false;
        }
    }
}
}
