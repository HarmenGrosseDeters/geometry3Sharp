// Experimental scalar recurrence and Rump (2006) trace correction, not library code.
// Non-audited runs assume correctly rounded IEEE binary64 scalar operations.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

static class ScalarCholesky
{
    // Exact unit for every finite binary64 value, including subnormals.
    const int UnitBits = 1074;
    const double MinNormal = 2.2250738585072014E-308;
    internal static BigInteger Units(double value)
    {
        if (!double.IsFinite(value)) throw new ArithmeticException("nonfinite");
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
        int length = (int)units.GetBitLength();
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
        readonly TextWriter trace;
        internal long Products;
        internal Arithmetic(TextWriter trace) { this.trace = trace; }
        double Record(string operation, double a, double b, double result) {
            if (trace != null) trace.WriteLine(JsonSerializer.Serialize(new { operation,
                a = BitConverter.DoubleToInt64Bits(a), b = BitConverter.DoubleToInt64Bits(b), result = BitConverter.DoubleToInt64Bits(result) }));
            return result;
        }
        // Separate non-inlined product prevents contraction into a fused subtract.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal double Product(double a, double b) {
            if (++Products > 50000000) throw new ArithmeticException("product budget");
            double result = Checked(a*b);
            if ((result == 0 && a != 0 && b != 0) || Math.Abs(result) == MinNormal)
                throw new ArithmeticException("product underflow boundary");
            return Record("mul",a,b,result);
        }
        internal double Difference(double a, double b) => Record("sub",a,b,Checked(a-b));
        internal double Quotient(double a, double b) {
            double result = Checked(a/b);
            if ((result == 0 && a != 0) || Math.Abs(result) == MinNormal)
                throw new ArithmeticException("division underflow boundary");
            return Record("div",a,b,result);
        }
        internal double Root(double a) => Record("sqrt",a,0,Checked(Math.Sqrt(a)));
    }
    internal static bool Verify(Dictionary<int,Interval>[] input, string digest, string shift, string tracePath, bool envelopeBound = false, bool boundsOnly = false, bool useIndex = true)
    {
        var timer = Stopwatch.StartNew(); int n = input.Length;
        using var trace = tracePath == null ? null : new StreamWriter(new FileStream(tracePath,FileMode.CreateNew,FileAccess.Write));
        var arithmetic = new Arithmetic(trace);
        try {
            if (n < 1 || n > (boundsOnly ? 100000 : 2500) || Interval.Bits > UnitBits) throw new ArithmeticException("experimental size/precision cap");
            var columns = new HashSet<int>[n]; var matrix = new Dictionary<int,double>[n];
            var radiusRows = new BigInteger[n]; BigInteger diagonalSum = 0;
            var envelope = new int[n];
            var auditEntries = trace == null ? null : new List<object>();
            for (int c = 0; c < n; c++) {
                columns[c] = new HashSet<int>(); matrix[c] = new Dictionary<int,double>();
                foreach (var entry in input[c]) {
                    int r = entry.Key; if (r < c || r >= n) throw new ArithmeticException("lower pattern");
                    BigInteger lo = entry.Value.Lo << (UnitBits-Interval.Bits), hi = entry.Value.Hi << (UnitBits-Interval.Bits);
                    double midpoint = Checked(Directed((lo+hi)>>1, false));
                    BigInteger center = Units(midpoint), radius = BigInteger.Max(BigInteger.Abs(center-lo),BigInteger.Abs(hi-center));
                    radiusRows[r] += radius; if (r != c) radiusRows[c] += radius;
                    envelope[r] = Math.Max(envelope[r],r-c);
                    matrix[c].Add(r,midpoint); if (r != c) columns[c].Add(r);
                    if (r == c) { if (midpoint <= 0) throw new ArithmeticException("nonpositive diagonal"); diagonalSum += center; }
                    if (auditEntries != null) auditEntries.Add(new { row=r, column=c, lo=entry.Value.Lo.ToString(), hi=entry.Value.Hi.ToString(), midpoint=BitConverter.DoubleToInt64Bits(midpoint) });
                }
                if (!matrix[c].ContainsKey(c)) throw new ArithmeticException("missing diagonal");
            }
            BigInteger radiusMax = 0; foreach (var radius in radiusRows) radiusMax = BigInteger.Max(radiusMax,radius);
            // gamma/(1-gamma) = (n+1)/(2^53-2(n+1)); the exact
            // trace is in 2^-1074 units, and the quotient is rounded upward.
            BigInteger denominator = (BigInteger.One << 53)-2*(n+1), remainder;
            BigInteger correction = BigInteger.DivRem(diagonalSum*(n+1),denominator,out remainder);
            if (!remainder.IsZero) correction++;
            var globalCorrection = correction;
            if (envelopeBound) {
                BigInteger envelopeCorrection = 0;
                for (int i=0;i<n;i++) {
                    int k=envelope[i]+2;
                    var term=BigInteger.DivRem(Units(matrix[i][i])*k,(BigInteger.One<<53)-2*k,out remainder);
                    if (!remainder.IsZero) term++;
                    envelopeCorrection+=term;
                }
                correction=BigInteger.Min(correction,envelopeCorrection);
            }
            if (boundsOnly) {
                Console.WriteLine($"status=matrix_bounds_only size={n} input_sha256={digest} correction_units={correction} global_correction_units={globalCorrection} radius_units={radiusMax} unit_bits={UnitBits} elapsed_ms={timer.Elapsed.TotalMilliseconds:F3} position_certificate=not_checked");
                return false;
            }
            var adjusted = new long[n];
            for (int i = 0; i < n; i++) {
                matrix[i][i] = Checked(Directed(Units(matrix[i][i])-correction-radiusMax,false));
                adjusted[i] = BitConverter.DoubleToInt64Bits(matrix[i][i]);
            }
            if (trace != null) trace.WriteLine(JsonSerializer.Serialize(new { kind="bounds", input_sha256=digest, size=n,
                bits=Interval.Bits, envelope_bound=envelopeBound, envelope, correction_units=correction.ToString(), radius_units=radiusMax.ToString(), adjusted, entries=auditEntries }));
            var rows = new List<KeyValuePair<int,double>>[n]; for (int i = 0; i < n; i++) rows[i] = new List<KeyValuePair<int,double>>();
            var rowIndex = useIndex ? new int[n] : null;
            var rowValue = useIndex ? new double[n] : null;
            long symbolic = 0, symbolicTicks = 0, numericTicks = 0, symbolicAllocated = 0, numericAllocated = 0;
            double boundsMilliseconds = timer.Elapsed.TotalMilliseconds;
            for (int j = 0; j < n; j++) {
                long phaseStart = Stopwatch.GetTimestamp(), phaseAllocated = GC.GetAllocatedBytesForCurrentThread();
                var neighbors = new List<int>(columns[j]); neighbors.Sort();
                long work = (long)neighbors.Count*(neighbors.Count-1)/2;
                if (work > 50000000-symbolic) throw new ArithmeticException("symbolic budget");
                symbolic += work;
                for (int p = 0; p < neighbors.Count; p++) for (int q = p+1; q < neighbors.Count; q++) columns[neighbors[p]].Add(neighbors[q]);
                columns[j].Clear();
                symbolicTicks += Stopwatch.GetTimestamp()-phaseStart;
                symbolicAllocated += GC.GetAllocatedBytesForCurrentThread()-phaseAllocated;
                phaseStart = Stopwatch.GetTimestamp(); phaseAllocated = GC.GetAllocatedBytesForCurrentThread();
                double diagonal = matrix[j][j];
                foreach (var entry in rows[j]) diagonal = arithmetic.Difference(diagonal,arithmetic.Product(entry.Value,entry.Value));
                if (!(diagonal > 0)) throw new ArithmeticException("nonpositive scalar pivot at "+j);
                double pivot = arithmetic.Root(diagonal);
                int lastKey = rows[j].Count == 0 ? -1 : rows[j][rows[j].Count-1].Key;
                if (useIndex) foreach (var item in rows[j]) { rowIndex[item.Key]=j+1; rowValue[item.Key]=item.Value; }
                foreach (int i in neighbors) {
                    double value; if (!matrix[j].TryGetValue(i,out value)) value = 0;
                    if (useIndex) {
                        foreach (var item in rows[i]) {
                            if (item.Key>lastKey) break;
                            if (rowIndex[item.Key]==j+1)
                                value=arithmetic.Difference(value,arithmetic.Product(item.Value,rowValue[item.Key]));
                        }
                    } else {
                        int a = 0, b = 0;
                        while (a < rows[i].Count && b < rows[j].Count) {
                            var x = rows[i][a]; var y = rows[j][b];
                            if (x.Key < y.Key) a++; else if (x.Key > y.Key) b++;
                            else { value = arithmetic.Difference(value,arithmetic.Product(x.Value,y.Value)); a++; b++; }
                        }
                    }
                    rows[i].Add(new KeyValuePair<int,double>(j,arithmetic.Quotient(value,pivot)));
                }
                matrix[j].Clear();
                numericTicks += Stopwatch.GetTimestamp()-phaseStart;
                numericAllocated += GC.GetAllocatedBytesForCurrentThread()-phaseAllocated;
            }
            Console.WriteLine($"scalar_phase_bounds_ms={boundsMilliseconds:F3} symbolic_ms={1000.0*symbolicTicks/Stopwatch.Frequency:F3} numeric_ms={1000.0*numericTicks/Stopwatch.Frequency:F3} symbolic_allocated_bytes={symbolicAllocated} numeric_allocated_bytes={numericAllocated} row_index={useIndex} phase_scope=scalar_verifier_only_including_its_allocations_and_gc");
            Console.WriteLine($"status=positive_scalar_pivots mode=mesh-scalar size={n} products={arithmetic.Products} elapsed_ms={timer.Elapsed.TotalMilliseconds:F3} input_sha256={digest} shift_binary64={shift} arithmetic_audited=False trace_available={trace!=null} ieee_binary64_premise=True");
            return true;
        }
        catch (ArithmeticException error) {
            Console.WriteLine($"status=uncertain mode=mesh-scalar reason={error.Message} input_sha256={digest}");
            Environment.ExitCode = 1; return false;
        }
    }
    internal static void SelfTest()
    {
        var random = new Random(886);
        for (int i = 0; i < 2000; i++) {
            var units = new BigInteger(random.NextInt64(1,1L<<52)) << random.Next(0,2047);
            if (i%2 == 0) units = -units;
            try {
                double lo = Directed(units,false), hi = Directed(units,true);
                if (Units(lo) > units || Units(hi) < units || (lo != hi && Math.BitIncrement(lo) != hi)) throw new Exception("directed conversion");
            } catch (ArithmeticException) { if (BigInteger.Abs(units) <= Units(double.MaxValue)) throw; }
        }
        var arithmetic = new Arithmetic(null);
        foreach (Action operation in new Action[] {
            () => arithmetic.Product(MinNormal,.5), () => arithmetic.Product(MinNormal,1),
            () => arithmetic.Product(Math.ScaleB(1,-600),Math.ScaleB(1,-600)),
            () => arithmetic.Quotient(MinNormal,2), () => arithmetic.Quotient(MinNormal,1) }) {
            bool refused = false; try { operation(); } catch (ArithmeticException) { refused = true; }
            if (!refused) throw new Exception("underflow guard");
        }
        Console.WriteLine("scalar_selftest=passed conversion_cases=2000 underflow_controls=5");
    }
}
