// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Predicate compiler, bounds only (docs/design/FIXED_WIDTH_EXACT_POINTS.md sections 5 and 13). It reads integer sign
// predicates, written once as polynomial formulas over the components of points whose components are bounded by
// |v| <= m = 2^b - 1 (b given per predicate), and derives by machine, for every intermediate value:
//   - a proven magnitude bound (the value rule below), so the bits and limbs the value needs; this part knows
//     nothing of a runtime;
//   - the limbs FixedArith gives it (the length rule below, a model of that one runtime), so the scratch one call
//     takes from its FixedArena.
// The predicates here are those of ExactMeshArrangement on fixed-width points (FixedPoint3, b = 255).
// The output is the bound table of the design doc (section 5), which a test keeps equal to this derivation, and the
// tests check the hand-written kernels against it (their scratch constants, and the formula against FixedArith).
// This is the ipredicate part of the experiment of #221, retargeted from two's complement inputs with fitted bit
// bounds to FixedPoint3 / FixedInt (sign and magnitude). Generating the kernels as well was measured without a gain
// (section 13); the spike is on branch claude/predicate-compiler-fixed-spike.
//
// Further analyses (a floating-point filter with a derived error bound, expansion stages, as in #221; inputs that are
// expressions of a construction's inputs, i.e. implicit points) would be passes over the same expression graph
// (Node) next to these two; definitions already let a predicate's values be any expressions of its inputs.
//
// Value rule (exact integers; for FixedPoint3, m = 2^255 - 1 bounds every component, section 4.1):
//   input component:  |v| <= m
//   x + y, x - y:     |v| <= Mx + My
//   x y:              |v| <= Mx My
// Each bound is a polynomial in m with nonnegative integer coefficients, evaluated exactly. |v| <= M needs
// bitlength(M) bits, ceil(bitlength(M) / 64) limbs as a magnitude (sign and magnitude needs no sign bit).
//
// Length rule (FixedArith on trimmed magnitudes, docs section 4.5; an upper bound of the limbs it takes):
//   input component:  at most ceil(b / 64) limbs (copied to the caller's stack, not taken from the arena)
//   x y:              takes la + lb limbs (nothing if a factor is 0)
//   x + y, x - y:     takes max(la, lb) + 1 limbs (nothing if an operand is 0)
// with la, lb the lengths of the operands: at most the limbs of their value bounds (the results are trimmed). The scratch of one call is the sum over the non-input values, each operation
// taking its own buffer. The same sum without the value bounds (lengths only, as the comment in FixedArith counts)
// is LengthScratch.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace G3PredicateCompiler
{
    public enum Op { Input, Add, Sub, Mul }

    /// <summary> an exact rational (the filter's error coefficients) </summary>
    public readonly struct Rational
    {
        public readonly BigInteger Num, Den;    // Den > 0, reduced
        public Rational(BigInteger num, BigInteger den)
        {
            if (den.Sign < 0) { num = -num; den = -den; }
            var g = BigInteger.GreatestCommonDivisor(num, den);
            if (!g.IsZero && !g.IsOne) { num /= g; den /= g; }
            Num = num; Den = den;
        }
        public static Rational Int(long v) => new Rational(v, 1);
        public static readonly Rational Zero = Int(0), One = Int(1);
        /// <summary> u = 2^-53, the unit roundoff of round to nearest </summary>
        public static readonly Rational U = new Rational(1, BigInteger.Pow(2, 53));
        public static Rational operator +(Rational a, Rational b) => new Rational(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
        public static Rational operator -(Rational a, Rational b) => new Rational(a.Num * b.Den - b.Num * a.Den, a.Den * b.Den);
        public static Rational operator *(Rational a, Rational b) => new Rational(a.Num * b.Num, a.Den * b.Den);
        public static Rational operator /(Rational a, Rational b) => new Rational(a.Num * b.Den, a.Den * b.Num);
        public static bool operator <=(Rational a, Rational b) => (b - a).Num.Sign >= 0;
        public static bool operator >=(Rational a, Rational b) => (a - b).Num.Sign >= 0;
        public static Rational Max(Rational a, Rational b) => a >= b ? a : b;
        /// <summary> the exact value of a finite double </summary>
        public static Rational FromDouble(double d)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            e -= 1075;
            BigInteger M = (bits < 0) ? -m : m;
            return e >= 0 ? new Rational(M * BigInteger.Pow(2, e), 1) : new Rational(M, BigInteger.Pow(2, -e));
        }
        /// <summary> the smallest double >= this (positive, normal range) </summary>
        public double CeilDouble()
        {
            // an approximation from the top 60 bits of both (numerator and denominator can exceed the double range)
            int k = Math.Max(0, (int)Num.GetBitLength() - 60), j = Math.Max(0, (int)Den.GetBitLength() - 60);
            double d = Math.ScaleB((double)(Num >> k) / (double)(Den >> j), k - j);
            if (!(d > 0) || double.IsInfinity(d))
                throw new NotSupportedException("CeilDouble: not a positive value in the double range");
            while (!(FromDouble(d) >= this)) d = Math.BitIncrement(d);
            while (FromDouble(Math.BitDecrement(d)) >= this) d = Math.BitDecrement(d);
            return d;
        }
        /// <summary> as a multiple of u, for comments </summary>
        public string InUnitsOfU()
        {
            var q = this / U;
            return ((double)q.Num / (double)q.Den).ToString("0.######", CultureInfo.InvariantCulture) + " u";
        }
    }

    /// <summary> a polynomial in m with nonnegative integer coefficients: degree -> coefficient </summary>
    public sealed class Bound
    {
        public readonly SortedDictionary<int, BigInteger> Terms = new SortedDictionary<int, BigInteger>();
        public static Bound Input() { var b = new Bound(); b.Terms[1] = 1; return b; }
        public static Bound operator +(Bound a, Bound b)
        {
            var r = new Bound();
            foreach (var t in a.Terms) r.Terms[t.Key] = t.Value;
            foreach (var t in b.Terms) r.Terms[t.Key] = (r.Terms.TryGetValue(t.Key, out var c) ? c : 0) + t.Value;
            return r;
        }
        public static Bound operator *(Bound a, Bound b)
        {
            var r = new Bound();
            foreach (var x in a.Terms)
                foreach (var y in b.Terms) {
                    int d = x.Key + y.Key;
                    r.Terms[d] = (r.Terms.TryGetValue(d, out var c) ? c : 0) + x.Value * y.Value;
                }
            return r;
        }
        /// <summary> the bound at the given m </summary>
        public BigInteger At(BigInteger m)
        {
            BigInteger s = 0;
            foreach (var t in Terms) s += t.Value * BigInteger.Pow(m, t.Key);
            return s;
        }
        public override string ToString() =>
            string.Join(" + ", Terms.Reverse().Select(t => (t.Value.IsOne ? "" : t.Value.ToString(CultureInfo.InvariantCulture) + " ")
                + (t.Key == 1 ? "m" : "m^" + t.Key.ToString(CultureInfo.InvariantCulture))));
    }

    public sealed class Node
    {
        public int Id;
        public Op Op;
        public Node A, B;
        public string Point;    // input: the point
        public char Comp;       // input: 'x' (axis i), 'y' (axis j), 'w' (component 3)
        public string Name;     // the name of its definition, if any
        public Bound Bound;
        public int Degree;      // the total degree in the input components
        public BigInteger Max;  // the bound at m
        public int Bits => Max.IsZero ? 0 : (int)Max.GetBitLength();
        public int Limbs => (Bits + 63) / 64;
        public int ArenaLimbs;  // the length rule: the limbs the operation takes (inputs: their length)
        public int LengthOnly;  // the same without the value bounds
        // the filter (double inputs in the predicate's range gate): |e~ - e| <= C m~, e~ the computed value, m~ the
        // computed magnitude; Leaf: a sum or difference of two inputs (m~ = |e~|)
        public Rational C;
        public bool Leaf => (Op == Op.Add || Op == Op.Sub) && A.Op == Op.Input && B.Op == Op.Input;
        // the expansion stage (Shewchuk): the static worst-case number of components of the value as an expansion from
        // exact double inputs: input 1, a sum or difference la + lb, a product 2 la lb (a difference of two inputs: 2)
        public BigInteger ExpansionLength;
        // the most limbs the value has (trimmed): its bound's, never more than the operation took (a product of
        // values below 2^(64 la), 2^(64 lb) is below 2^(64 (la + lb)), a sum below 2^(64 max + 1))
        public int Length => Limbs;
    }

    public sealed class Predicate
    {
        public string Name;
        public List<string> Points = new List<string>();
        /// <summary> every input component has |v| &lt;= InputBound = 2^InputBits - 1 (the m of the bounds) </summary>
        public int InputBits;
        public BigInteger InputBound => BigInteger.Pow(2, InputBits) - 1;
        public List<Node> Nodes = new List<Node>();     // topological order
        /// <summary> the first sign factor (the predicate's sign is the product of the signs of Signs) </summary>
        public Node Root => Signs[0];
        public List<Node> Signs = new List<Node>();
        /// <summary> the inputs: p.x, p.y, p.w (components i, j of the projection, and W) or, with 'inputs xyz', the
        /// coordinates p.x, p.y, p.z of integer points (W = 1) </summary>
        public bool Xyz;
        public string Comps => Xyz ? "xyz" : "xyw";
        /// <summary> the limbs one call takes from its FixedArena (the length rule) </summary>
        public int ArenaScratch;
        /// <summary> the expansion stage is generated only for a predicate whose every sign factor has fewer than this
        /// many components in the worst case (owner decision: below 1024, docs/design/INDIRECT_PREDICATES.md section 5) </summary>
        public const int MaxExpansionLength = 1024;
        public bool HasExpansionStage => Signs.All(n => n.ExpansionLength < MaxExpansionLength);
        /// <summary> every sum and difference adds values of one degree (scaling all inputs by s scales a value of degree
        /// k by s^k, so a common power of two keeps every sign) </summary>
        public bool Homogeneous;
        public int Degree;
        /// <summary> the filter's range: inputs 0 or of magnitude in [2^-GateLow, 2^GateHigh] </summary>
        public int GateLow, GateHigh;
        /// <summary> the filter's test constant per sign factor: the factor's sign is decided if |e~| > fl(K m~) </summary>
        public List<double> FilterK = new List<double>();
        /// <summary> 'prepare p, q, ...': the points of a construction evaluated against many others; its values are
        /// computed once (Prepare) and the rest per call (docs/design/ENVELOPE.md 4.1, item 2) </summary>
        public List<string> Prepare = new List<string>();
        /// <summary> ArenaScratch without the value bounds (the operands' lengths only) </summary>
        public int LengthScratch;
        /// <summary> the limbs one call needs if every value had a buffer of its bound (the value rule) </summary>
        public int ValueScratch;
    }

    public static class PredicateCompiler
    {
        // point names that would clash with the generated code: the axes, and C# keywords
        static readonly HashSet<string> Reserved = new HashSet<string>(("i j abstract as base bool break byte case catch char checked class const continue " +
            "decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int " +
            "interface internal is lock long namespace new null object operator out override params private protected public readonly ref " +
            "return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe " +
            "ushort using virtual void volatile while").Split(' '));
        public const string TableBegin = "<!-- generated by tools/PredicateCompiler --table; do not edit -->";
        public const string TableEnd = "<!-- end of the generated table -->";

        /// <summary> the predicates of a spec (see predicates.spec) with the bounds of all their values </summary>
        public static List<Predicate> Analyze(string spec)
        {
            var preds = Parse(spec);
            foreach (var p in preds) {
                value_bounds(p);
                fixedarith_lengths(p);
                if (p.Xyz)
                    filter_bounds(p);       // only the inputs xyz predicates get a filter (the double entry)
                expansion_lengths(p);
            }
            return preds;
        }

        // the value rule: independent of any runtime
        static void value_bounds(Predicate p)
        {
            foreach (var n in p.Nodes) {
                switch (n.Op) {
                    case Op.Input: n.Bound = Bound.Input(); break;
                    case Op.Add: case Op.Sub: n.Bound = n.A.Bound + n.B.Bound; break;
                    case Op.Mul: n.Bound = n.A.Bound * n.B.Bound; break;
                }
                n.Max = n.Bound.At(p.InputBound);
                n.Degree = n.Op == Op.Input ? 1 : n.Op == Op.Mul ? n.A.Degree + n.B.Degree : Math.Max(n.A.Degree, n.B.Degree);
            }
            p.ValueScratch = p.Nodes.Where(n => n.Op != Op.Input).Sum(n => n.Limbs);
            p.Homogeneous = p.Nodes.All(n => n.Op == Op.Input || n.Op == Op.Mul || n.A.Degree == n.B.Degree);
            p.Degree = p.Nodes.Max(n => n.Degree);
        }

        // the filter rule, for double inputs in the range gate (each 0 or of magnitude in [2^-a, 2^b]), evaluated in
        // double with round to nearest (u = 2^-53).
        // No underflow: a nonzero input is a multiple of its ulp >= 2^-(a + 52), so every computed value of degree k (a
        // rounded sum of products of such values) is a multiple of 2^-(k (a + 52)); with k (a + 52) <= 960 for the
        // predicate's degree k, every nonzero value and magnitude is >= 2^-960, so every result is normal (the decision
        // product K m~ too: K > 2^-60), fl(x op y) = (x op y)(1 + d), |d| <= u, and a product of nonzero values is
        // nonzero. No overflow: b is the largest with every value's bound below 2^1000 at |x| <= 2^b.
        // Invariant |e~ - e| <= c m~ with |e~| <= m~ (rounding is monotone):
        //   input:           e~ = x, m~ = |x|, c = 0
        //   input +- input:  m~ = |e~|, c = u           (|fl(s) - s| <= u |fl(s)|)
        //   x +- y:          m~ = fl(m~x + m~y), c = (u + max(cx, cy)) / (1 - u)
        //                    (|e~ - e| <= u |s| + cx m~x + cy m~y <= (u + max) (m~x + m~y), m~x + m~y <= m~ / (1 - u))
        //   x y:             m~ = fl(m~x m~y), c = (u + cx + cy + cx cy) / (1 - u)
        //                    (|e~ - e| <= u |x~ y~| + |x~ - x| |y~| + |x| |y~ - y|, |x| <= (1 + cx) m~x)
        // A sign factor e is decided if m~ = 0 (then e = 0: every product is an exact 0) or |e~| > fl(K m~) with
        // K >= c / (1 - u) a double (fl(K m~) >= K m~ (1 - u) >= c m~, so |e| >= |e~| - c m~ > 0 and the signs agree).
        // (No overflow within the gate: b is derived from the bounds, see above; an overflow would give inf or NaN in m~
        // and the test would not decide.)
        // The model is strict IEEE double rounding at every operation (RyuJIT / SSE2; x87 only with 53-bit precision
        // control), the same assumption as AdaptivePredicates.
        static void filter_bounds(Predicate p)
        {
            var u = Rational.U;
            var one = Rational.One;
            foreach (var n in p.Nodes) {
                switch (n.Op) {
                    case Op.Input: n.C = Rational.Zero; break;
                    case Op.Add: case Op.Sub:
                        n.C = n.Leaf ? u : (u + Rational.Max(n.A.C, n.B.C)) / (one - u);
                        break;
                    case Op.Mul:
                        n.C = (u + n.A.C + n.B.C + n.A.C * n.B.C) / (one - u);
                        break;
                }
            }
            p.FilterK = p.Signs.Select(n => (n.C / (one - u)).CeilDouble()).ToList();

            if (!p.Homogeneous)
                throw new NotSupportedException($"{p.Name}: the double entry needs a homogeneous formula (sums of equal degree)");
            p.GateLow = 960 / p.Degree - 52;
            // the decision product K m~ must stay normal for m~ >= 2^-960: K >= 2^-62
            if (p.Signs.Any(n => (n.C / (one - u)).CeilDouble() < Math.ScaleB(1.0, -62)))
                throw new NotSupportedException($"{p.Name}: a filter constant below 2^-62");
            p.GateHigh = 0;
            while (p.GateHigh < 1000 && p.Nodes.All(n => n.Bound.At(BigInteger.Pow(2, p.GateHigh + 1)).GetBitLength() <= 1000))
                p.GateHigh++;
            if (p.GateLow < 1 || p.GateHigh < 1)
                throw new NotSupportedException($"{p.Name}: no filter range (degree {p.Degree})");
        }

        // the static worst-case length of each value as an expansion (Shewchuk) from exact double inputs: an input 1, a
        // sum or difference la + lb (expansion sum), a product 2 la lb (each component of one scaled by the other)
        static void expansion_lengths(Predicate p)
        {
            foreach (var n in p.Nodes)
                n.ExpansionLength = n.Op == Op.Input ? 1 : n.Op == Op.Mul ? 2 * n.A.ExpansionLength * n.B.ExpansionLength
                    : n.A.ExpansionLength + n.B.ExpansionLength;
        }

        // the length rule: a model of FixedArith (Mul / Add / Sub on a FixedArena), after value_bounds
        static void fixedarith_lengths(Predicate p)
        {
            foreach (var n in p.Nodes) {
                switch (n.Op) {
                    case Op.Input:
                        n.ArenaLimbs = n.LengthOnly = n.Limbs;
                        break;
                    case Op.Add: case Op.Sub:
                        n.ArenaLimbs = Math.Max(n.A.Length, n.B.Length) + 1;
                        n.LengthOnly = Math.Max(n.A.LengthOnly, n.B.LengthOnly) + 1;
                        break;
                    case Op.Mul:
                        n.ArenaLimbs = n.A.Length + n.B.Length;
                        n.LengthOnly = n.A.LengthOnly + n.B.LengthOnly;
                        break;
                }
            }
            var ops = p.Nodes.Where(n => n.Op != Op.Input).ToList();
            p.ArenaScratch = ops.Sum(n => n.ArenaLimbs);
            p.LengthScratch = ops.Sum(n => n.LengthOnly);
        }

        /// <summary>
        /// the bound table of every predicate of a spec, as Markdown between TableBegin and TableEnd (the block of the
        /// design doc). Values of the same shape (the same operations on values of the same shapes) share a row.
        /// </summary>
        public static string Table(string spec)
        {
            var sb = new StringBuilder();
            sb.Append(TableBegin).Append('\n');
            sb.Append("| predicate | values | bound | bits | limbs | limbs taken by FixedArith |\n");
            sb.Append("|---|---|---|---|---|---|\n");
            var preds = Analyze(spec);
            foreach (var p in preds) {
                sb.Append($"| `{p.Name}` | every input component | `m = 2^{inv(p.InputBits)} - 1` | {inv(p.InputBits)} | {inv((p.InputBits + 63) / 64)} | |\n");
                var shapes = new Dictionary<Node, string>();
                foreach (var n in p.Nodes) shapes[n] = shape(n, shapes);
                foreach (var g in p.Nodes.Where(n => n.Op != Op.Input).GroupBy(n => shapes[n])) {
                    var nodes = g.ToList();
                    string label = nodes.Contains(p.Root) ? (p.Signs.Count == 1 ? "the determinant" : "the determinant (sign factor 1)")
                        : nodes.Any(n => p.Signs.Contains(n)) ? label_of(nodes) + " (a sign factor)" : label_of(nodes);
                    var n0 = nodes[0];
                    sb.Append($"| `{p.Name}` | {label} | `{n0.Bound}` | {inv(n0.Bits)} | {inv(n0.Limbs)} | {inv(n0.ArenaLimbs)} |\n");
                }
            }
            foreach (var p in preds)
                sb.Append($"| `{p.Name}` | scratch of one call (sum over the values; lengths only: {inv(p.LengthScratch)}) | | | {inv(p.ValueScratch)} | {inv(p.ArenaScratch)} |\n");
            sb.Append(TableEnd).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// the generated C# of a spec (math/Generated/IndirectPredicates.cs): per predicate, the exact stage on
        /// FixedArith for inputs within the bound (a FixedArena of the derived scratch), and a BigInteger evaluation of
        /// the same graph for inputs beyond it
        /// </summary>
        public static string Code(string spec, string specPath, string outPath, string className)
        {
            var preds = Analyze(spec);
            var sb = new StringBuilder();
            // (no <auto-generated> marker: Roslyn skips such files in analyzers, and the exact-code analyzers must see it)
            sb.Append($"// Generated by tools/PredicateCompiler from {specPath}. Do not edit; regenerate with:\n");
            sb.Append($"//   dotnet run --project tools/PredicateCompiler -- --code {specPath} {outPath}\n");
            sb.Append("// (GeneratedPredicatesTests fails when this file differs from the generator's output.)\n");
            sb.Append("// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt\n");
            sb.Append("using System;\nusing System.Numerics;\nusing System.Runtime.CompilerServices;\n\nnamespace g3\n{\n");
            sb.Append("    /// <summary>\n");
            sb.Append("    /// Exact sign predicates generated from their formulas by tools/PredicateCompiler (docs/design/INDIRECT_PREDICATES.md).\n");
            sb.Append("    /// Inputs within the bound of a predicate are evaluated on FixedArith, every value with the limbs its derived bound\n");
            sb.Append("    /// allows (the comments), on a FixedArena of the derived scratch; inputs beyond it in BigInteger. No allocation on\n");
            sb.Append("    /// the FixedArith path. A predicate with several sign factors is 0 when any factor is 0: an indirect predicate is\n");
            sb.Append("    /// also 0 when its point does not exist (denominator d = 0: a line parallel to the plane, a degenerate triangle,\n");
            sb.Append("    /// dependent planes); a caller that must tell this apart checks d itself. Orient3d*(..., a, b, c) is the sign of\n");
            sb.Append("    /// det(x - c, a - c, b - c) = ExactPredicates.Orient3d(a, b, c, x) for the implicit point x.\n");
            sb.Append("    /// </summary>\n");
            sb.Append($"    internal static unsafe class {className}\n    {{\n");
            sb.Append("        static int __len(ref FixedPoint3 p, int k) { return p.M[k] & 7; }\n");
            sb.Append(DoubleHelpers);
            foreach (var p in preds) {
                if (p.InputBits % 64 != 0)
                    throw new NotSupportedException($"{p.Name}: generated code needs a bound of whole limbs (b a multiple of 64)");
                sb.Append('\n');
                emit(p, sb);
            }
            sb.Append("    }\n}\n");
            return sb.ToString();
        }

        // generated names: points match ^[a-z][a-z0-9]*$, so p_x and the names with a leading _ cannot clash with them
        static string val(Node n) => n.Op == Op.Input ? n.Point + "_" + n.Comp : "_v" + inv(n.Id);

        static void emit(Predicate p, StringBuilder sb)
        {
            string I1 = "        ", I2 = I1 + "    ";
            int L = p.InputBits / 64;
            var inputs = p.Nodes.Where(n => n.Op == Op.Input).ToList();
            string axes = p.Xyz ? "" : ", int i, int j";
            string comp(Node n) => n.Comp == 'x' ? (p.Xyz ? "0" : "i") : n.Comp == 'y' ? (p.Xyz ? "1" : "j") : p.Xyz ? "2" : "3";
            string sign(Func<Node, string> f) => string.Join(" * ", p.Signs.Select(n => f(n) + ".Sign"));
            sb.Append($"{I1}// {p.Name}: the sign of {string.Join(" times ", p.Signs.Select(ref_))}; every input component |v| <= m = 2^{inv(p.InputBits)} - 1\n");
            foreach (var n in p.Signs)
                sb.Append($"{I1}// {ref_(n)}: |.| <= {n.Bound} < 2^{inv(n.Bits)} ({inv(n.Limbs)} limbs)\n");
            sb.Append($"{I1}// scratch: {inv(p.ArenaScratch)} limbs (FixedArith, derived)\n");
            sb.Append("#if NET\n");
            sb.Append($"{I1}[SkipLocalsInit]\n");
            sb.Append("#endif\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}({string.Join(", ", p.Points.Select(x => "ref FixedPoint3 " + x))}{axes})\n{I1}{{\n");
            if (p.Xyz) {
                sb.Append($"{I2}if (!({string.Join(" && ", p.Points.Select(x => x + ".WIsOne"))}))\n");
                sb.Append($"{I2}    throw new ArgumentException(\"{p.Name}: the inputs must be integer points (W = 1)\");\n");
            }
            var fits = inputs.Select(n => $"__len(ref {n.Point}, {comp(n)}) <= {inv(L)}");
            sb.Append($"{I2}if (!({string.Join(" && ", fits)}))\n");
            sb.Append($"{I2}    return {p.Name}Big({string.Join(", ", p.Points.Select(x => x + ".ToExact()"))}{(p.Xyz ? "" : ", i, j")});\n");
            arena_body(p, sb, I2, (n, dst) => $"{n.Point}.Get({comp(n)}, {dst})");
            sb.Append($"{I1}}}\n\n");
            if (p.Xyz)
                emit_double(p, sb);     // its exact stage has its own bound (two limbs, rebound)
            if (p.Prepare.Count > 0)
                emit_prepared(p, sb);
            // BigInteger
            sb.Append($"{I1}/// <summary> {p.Name} in BigInteger (inputs beyond the bound) </summary>\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}Big({string.Join(", ", p.Points.Select(x => "ExactPoint3 " + x))}{axes})\n{I1}{{\n");
            if (p.Xyz) {
                sb.Append($"{I2}if (!({string.Join(" && ", p.Points.Select(x => x + ".W.IsOne"))}))\n");
                sb.Append($"{I2}    throw new ArgumentException(\"{p.Name}: the inputs must be integer points (W = 1)\");\n");
            }
            foreach (var n in p.Nodes) {
                string e = n.Op == Op.Input
                    ? (p.Xyz ? $"{n.Point}.{char.ToUpperInvariant(n.Comp)}" : n.Comp == 'w' ? $"{n.Point}.W" : $"{n.Point}[{comp(n)}]")
                    : $"{val(n.A)} {opc(n.Op)} {val(n.B)}";
                sb.Append($"{I2}BigInteger {val(n)} = {e};\n");
            }
            sb.Append($"{I2}return {sign(val)};\n{I1}}}\n");
        }


        // the graph on FixedArith (inputs loaded by load(input, buffer)), returning the product of the signs
        static void arena_body(Predicate p, StringBuilder sb, string I2, Func<Node, string, string> load)
        {
            int L = p.InputBits / 64;
            var inputs = p.Nodes.Where(n => n.Op == Op.Input).ToList();
            // one buffer per input (no pointer arithmetic: nothing to get wrong, and no equivalent mutants)
            for (int i = 0; i < inputs.Count; ++i)
                sb.Append($"{I2}ulong* __i{inv(i)} = stackalloc ulong[{inv(L)}];\n");
            // one constant for the buffer and the arena: they cannot disagree (the arena's Take checks against it)
            sb.Append($"{I2}const int __n = {inv(p.ArenaScratch)};\n");
            sb.Append($"{I2}ulong* __s = stackalloc ulong[__n];\n");
            sb.Append($"{I2}var __ar = new FixedArena(__s, __n);\n");
            int k = 0;
            foreach (var n in p.Nodes) {
                string code = n.Op == Op.Input
                    ? $"FixedInt {val(n)} = {load(n, "__i" + inv(k++))};"
                    : $"FixedInt {val(n)} = FixedArith.{(n.Op == Op.Mul ? "Mul" : n.Op == Op.Add ? "Add" : "Sub")}({val(n.A)}, {val(n.B)}, ref __ar);";
                sb.Append($"{I2}{code}".PadRight(84)).Append($" // {(n.Op == Op.Input ? ref_(n) : describe(n))}: <= {n.Bound}\n");
            }
            sb.Append($"{I2}return {string.Join(" * ", p.Signs.Select(n => val(n) + ".Sign"))};\n");
        }

        // the helpers of the double entries (emitted once per generated class)
        const string DoubleHelpers = @"        // each coordinate on its own (a sum of finite coordinates can overflow): x - x is 0 exactly for finite x
        static bool __finite(Vector3d p) { return p.x - p.x == 0 && p.y - p.y == 0 && p.z - p.z == 0; }
        static bool __in(double x, double lo, double hi) { double a = Math.Abs(x); return x == 0 || (a >= lo && a <= hi); }
        // the filter's range: every coordinate 0 or of magnitude in [lo, hi]
        static bool __gate(Vector3d p, double lo, double hi) { return __in(p.x, lo, hi) && __in(p.y, lo, hi) && __in(p.z, lo, hi); }
        // x = +-m 2^e with m odd (x != 0)
        static void __dec(double x, out ulong m, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            m = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            if (be == 0) e = -1074; else { m |= 1UL << 52; e = be - 1075; }
#if NET
            int tz = System.Numerics.BitOperations.TrailingZeroCount(m);
            m >>= tz; e += tz;
#else
            while ((m & 1) == 0) { m >>= 1; e++; }
#endif
        }
        static int __bits(ulong m)
        {
#if NET
            return 64 - System.Numerics.BitOperations.LeadingZeroCount(m);
#else
            int n = 0; while (m != 0) { n++; m >>= 1; } return n;
#endif
        }
        // the smallest exponent of the nonzero coordinates
        static void __emin(Vector3d p, ref int emin) { __emin1(p.x, ref emin); __emin1(p.y, ref emin); __emin1(p.z, ref emin); }
        static void __emin1(double x, ref int emin) { if (x == 0) return; ulong m; int e; __dec(x, out m, out e); if (e < emin) emin = e; }
        // x 2^-emin, an integer, fits two limbs
        static bool __fits(Vector3d p, int emin) { return __fits1(p.x, emin) && __fits1(p.y, emin) && __fits1(p.z, emin); }
        static bool __fits1(double x, int emin) { if (x == 0) return true; ulong m; int e; __dec(x, out m, out e); return e - emin + __bits(m) <= 128; }
        // x 2^-emin in two limbs (after __fits)
        static FixedInt __align(double x, int emin, ulong* dst)
        {
            if (x == 0) return new FixedInt(dst, 0, false);
            ulong m; int e; __dec(x, out m, out e);
            int s = e - emin;
            if (s >= 64) { dst[0] = 0; dst[1] = m << (s - 64); }
            else if (s == 0) { dst[0] = m; dst[1] = 0; }
            else { dst[0] = m << s; dst[1] = m >> (64 - s); }
            return new FixedInt(dst, dst[1] != 0 ? 2 : 1, x < 0);
        }
        // the point times 2^-emin as an integer point (the BigInteger path)
        static ExactPoint3 __big(Vector3d p, int emin) { return ExactPoint3.FromInteger(__big1(p.x, emin), __big1(p.y, emin), __big1(p.z, emin)); }
        static BigInteger __big1(double x, int emin) { if (x == 0) return BigInteger.Zero; ulong m; int e; __dec(x, out m, out e); BigInteger v = new BigInteger(m) << (e - emin); return x < 0 ? -v : v; }
";

        // p with other input bits: the same graph (same ids), its bounds and lengths again
        static Predicate rebound(Predicate p, int bits)
        {
            var q = new Predicate { Name = p.Name, Points = p.Points, InputBits = bits, Xyz = p.Xyz };
            var map = new Dictionary<Node, Node>();
            foreach (var n in p.Nodes) {
                var c = new Node { Id = n.Id, Op = n.Op, Point = n.Point, Comp = n.Comp, Name = n.Name,
                    A = n.A == null ? null : map[n.A], B = n.B == null ? null : map[n.B] };
                map[n] = c;
                q.Nodes.Add(c);
            }
            q.Signs = p.Signs.Select(n => map[n]).ToList();
            value_bounds(q);
            fixedarith_lengths(q);
            return q;
        }

        static string lit(double d) => d.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");

        // the entry on doubles: the filter, then the FixedArith graph from the doubles (no allocation)
        static void emit_double(Predicate p, StringBuilder sb)
        {
            string I1 = "        ", I2 = I1 + "    ";
            string pars = string.Join(", ", p.Points.Select(x => "Vector3d " + x));
            string args = string.Join(", ", p.Points);
            sb.Append($"{I1}/// <summary>\n");
            sb.Append($"{I1}/// {p.Name} of points given as doubles: the filter if every coordinate is 0 or of magnitude in\n");
            sb.Append($"{I1}/// [2^-{inv(p.GateLow)}, 2^{inv(p.GateHigh)}], then the exact stage (FixedArith after aligning the exponents; BigInteger\n");
            sb.Append($"{I1}/// beyond two limbs); throws for coordinates that are not finite\n");
            sb.Append($"{I1}/// </summary>\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}({pars})\n{I1}{{\n");
            sb.Append($"{I2}if (!({string.Join(" && ", p.Points.Select(x => $"__finite({x})"))}))\n");
            sb.Append($"{I2}    throw new ArgumentException(\"{p.Name}: the coordinates must be finite\");\n");
            if (!p.HasExpansionStage)
                sb.Append($"{I2}// no expansion stage: as expansions the sign factors have up to {string.Join(" / ", p.Signs.Select(n => n.ExpansionLength.ToString(CultureInfo.InvariantCulture)))} components (limit {inv(Predicate.MaxExpansionLength)})\n");
            else
                throw new NotSupportedException($"{p.Name}: the expansion stage is not generated yet (its static length is within the limit)");
            string lo = lit(Math.ScaleB(1.0, -p.GateLow)), hi = lit(Math.ScaleB(1.0, p.GateHigh));
            sb.Append($"{I2}if ({string.Join(" && ", p.Points.Select(x => $"__gate({x}, {lo}, {hi})"))}) {{\n");
            sb.Append($"{I2}    int __r = {p.Name}Filter({args});\n");
            sb.Append($"{I2}    if (__r != 2) return __r;\n");
            sb.Append($"{I2}}}\n");
            sb.Append($"{I2}return {p.Name}Exact({args});\n{I1}}}\n\n");
            // the filter
            sb.Append($"{I1}/// <summary> the filter of {p.Name}: the sign, or 2 if it does not decide; every coordinate 0 or of magnitude in [2^-{inv(p.GateLow)}, 2^{inv(p.GateHigh)}] </summary>\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}Filter({pars})\n{I1}{{\n");
            foreach (var n in p.Nodes) emit_filter_value(n, sb, I2);
            emit_filter_decision(p, sb, I2);
            sb.Append($"{I1}}}\n\n");
            // exact from doubles: the exponents aligned, two limbs per coordinate (the graph again for |x| < 2^128)
            var q = rebound(p, 128);
            sb.Append($"{I1}/// <summary> the exact stage of {p.Name} from doubles: every coordinate times 2^-emin (emin the smallest\n");
            sb.Append($"{I1}/// exponent) is an integer; within two limbs on FixedArith (scratch {inv(q.ArenaScratch)} limbs), else in BigInteger </summary>\n");
            sb.Append("#if NET\n");
            sb.Append($"{I1}[SkipLocalsInit]\n");
            sb.Append("#endif\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}Exact({pars})\n{I1}{{\n");
            sb.Append($"{I2}int __e = int.MaxValue;\n");
            foreach (var x in p.Points) sb.Append($"{I2}__emin({x}, ref __e);\n");
            sb.Append($"{I2}if (__e == int.MaxValue) return 0;     // every coordinate 0\n");
            sb.Append($"{I2}if (!({string.Join(" && ", p.Points.Select(x => $"__fits({x}, __e)"))}))\n");
            sb.Append($"{I2}    return {p.Name}Big({string.Join(", ", p.Points.Select(x => $"__big({x}, __e)"))});\n");
            arena_body(q, sb, I2, (n, dst) => $"__align({n.Point}.{n.Comp}, __e, {dst})");
            sb.Append($"{I1}}}\n");
        }

        // the filter split at the prepared points: the values that depend on them only are computed once and kept
        // (with their m~); the call computes the rest, with the same operations in the same order, so its decisions
        // equal the unsplit filter's bit for bit. Undecided, or outside the gate: the exact stage of the whole predicate.
        static void emit_prepared(Predicate p, StringBuilder sb)
        {
            string I1 = "        ", I2 = I1 + "    ";
            var prep = new HashSet<Node>();
            foreach (var n in p.Nodes)
                if (n.Op == Op.Input ? p.Prepare.Contains(n.Point) : prep.Contains(n.A) && prep.Contains(n.B)) prep.Add(n);
            // the prepared values the call reads: operands of its values and prepared sign factors
            var keep = new List<Node>();
            foreach (var n in p.Nodes) {
                bool used = p.Signs.Contains(n) || p.Nodes.Any(x => !prep.Contains(x) && x.Op != Op.Input && (x.A == n || x.B == n));
                if (prep.Contains(n) && n.Op != Op.Input && used) keep.Add(n);
            }
            var others = p.Points.Where(x => !p.Prepare.Contains(x)).ToList();
            string lo = lit(Math.ScaleB(1.0, -p.GateLow)), hi = lit(Math.ScaleB(1.0, p.GateHigh));
            string st = p.Name + "Prepared";
            sb.Append($"{I1}/// <summary> {p.Name} with {string.Join(", ", p.Prepare)} prepared ({p.Name}Prepare): their points and the filter's\n");
            sb.Append($"{I1}/// values that depend on them only, with m~ </summary>\n");
            sb.Append($"{I1}internal struct {st}\n{I1}{{\n");
            sb.Append($"{I2}internal Vector3d {string.Join(", ", p.Prepare)};\n");
            sb.Append($"{I2}internal bool Ready;    // set by {p.Name}Prepare: a default struct is rejected by the call\n");
            sb.Append($"{I2}internal bool Gate;     // the prepared points within the filter's range\n");
            if (keep.Count > 0)
                sb.Append($"{I2}internal double {string.Join(", ", keep.Select(n => $"{val(n)}, m{val(n)}"))};\n");
            sb.Append($"{I1}}}\n\n");
            // prepare
            sb.Append($"{I1}/// <summary> prepares {p.Name} for {string.Join(", ", p.Prepare)}; throws for coordinates that are not finite </summary>\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static void {p.Name}Prepare({string.Join(", ", p.Prepare.Select(x => "Vector3d " + x))}, out {st} __s)\n{I1}{{\n");
            sb.Append($"{I2}if (!({string.Join(" && ", p.Prepare.Select(x => $"__finite({x})"))}))\n");
            sb.Append($"{I2}    throw new ArgumentException(\"{p.Name}: the coordinates must be finite\");\n");
            sb.Append($"{I2}__s = default({st});\n");
            sb.Append($"{I2}__s.Ready = true;\n");
            foreach (var x in p.Prepare) sb.Append($"{I2}__s.{x} = {x};\n");
            sb.Append($"{I2}__s.Gate = {string.Join(" && ", p.Prepare.Select(x => $"__gate({x}, {lo}, {hi})"))};\n");
            sb.Append($"{I2}if (!__s.Gate) return;\n");
            foreach (var n in p.Nodes.Where(prep.Contains)) emit_filter_value(n, sb, I2);
            foreach (var n in keep) sb.Append($"{I2}__s.{val(n)} = {val(n)}; __s.m{val(n)} = m{val(n)};\n");
            sb.Append($"{I1}}}\n\n");
            // the call
            sb.Append($"{I1}/// <summary> {p.Name}(prepared, {string.Join(", ", others)}): the same sign as the unsplit predicate </summary>\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}(ref {st} __s, {string.Join(", ", others.Select(x => "Vector3d " + x))})\n{I1}{{\n");
            sb.Append($"{I2}if (!__s.Ready)\n");
            sb.Append($"{I2}    throw new InvalidOperationException(\"{p.Name}: the struct was not prepared ({p.Name}Prepare)\");\n");
            sb.Append($"{I2}if (!({string.Join(" && ", others.Select(x => $"__finite({x})"))}))\n");
            sb.Append($"{I2}    throw new ArgumentException(\"{p.Name}: the coordinates must be finite\");\n");
            sb.Append($"{I2}if (__s.Gate && {string.Join(" && ", others.Select(x => $"__gate({x}, {lo}, {hi})"))}) {{\n");
            sb.Append($"{I2}    int __r = {p.Name}FilterPrepared(ref __s, {string.Join(", ", others)});\n");
            sb.Append($"{I2}    if (__r != 2) return __r;\n");
            sb.Append($"{I2}}}\n");
            sb.Append($"{I2}return {p.Name}Exact({string.Join(", ", p.Points.Select(x => p.Prepare.Contains(x) ? "__s." + x : x))});\n{I1}}}\n\n");
            // the filter part of the call
            sb.Append($"{I1}/// <summary> the filter of {p.Name} on prepared values: as {p.Name}Filter, bit for bit; requires a prepared struct\n");
            sb.Append($"{I1}/// with Gate and every point within the gate (the call checks both) </summary>\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}FilterPrepared(ref {st} __s, {string.Join(", ", others.Select(x => "Vector3d " + x))})\n{I1}{{\n");
            foreach (var n in keep) sb.Append($"{I2}double {val(n)} = __s.{val(n)}, m{val(n)} = __s.m{val(n)};\n");
            // the prepared points' coordinates that the call's values read
            foreach (var n in p.Nodes.Where(n => n.Op == Op.Input && prep.Contains(n)
                    && p.Nodes.Any(x => !prep.Contains(x) && x.Op != Op.Input && (x.A == n || x.B == n))))
                sb.Append($"{I2}double {val(n)} = __s.{n.Point}.{n.Comp}, m{val(n)} = Math.Abs({val(n)});\n");
            foreach (var n in p.Nodes.Where(n => !prep.Contains(n))) emit_filter_value(n, sb, I2);
            emit_filter_decision(p, sb, I2);
            sb.Append($"{I1}}}\n\n");
        }

        static void emit_filter_value(Node n, StringBuilder sb, string I2)
        {
            string e, m;
            if (n.Op == Op.Input) {
                e = $"{n.Point}.{n.Comp}";
                m = $"Math.Abs({val(n)})";
            } else {
                e = $"{val(n.A)} {opc(n.Op)} {val(n.B)}";
                m = n.Leaf ? $"Math.Abs({val(n)})" : n.Op == Op.Mul ? $"m{val(n.A)} * m{val(n.B)}" : $"m{val(n.A)} + m{val(n.B)}";
            }
            sb.Append($"{I2}double {val(n)} = {e}, m{val(n)} = {m};".PadRight(96)).Append($" // c = {n.C.InUnitsOfU()}\n");
        }

        static void emit_filter_decision(Predicate p, StringBuilder sb, string I2)
        {
            sb.Append($"{I2}int __r = 1;\n");
            for (int f = 0; f < p.Signs.Count; ++f) {
                var n = p.Signs[f];
                sb.Append($"{I2}// {ref_(n)}: |e~ - e| <= {n.C.InUnitsOfU()} m~ (K = c / (1 - u), rounded up)\n");
                sb.Append($"{I2}if (m{val(n)} == 0) return 0;\n");
                sb.Append($"{I2}if (!(Math.Abs({val(n)}) > {lit(p.FilterK[f])} * m{val(n)})) return 2;\n");
                sb.Append($"{I2}if ({val(n)} < 0) __r = -__r;\n");
            }
            sb.Append($"{I2}return __r;\n");
        }

        static string describe(Node n) => (n.Name != null ? n.Name + " = " : "") + $"{ref_(n.A)} {opc(n.Op)} {ref_(n.B)}";

        static string inv(int v) => v.ToString(CultureInfo.InvariantCulture);
        static string opc(Op op) => op == Op.Add ? "+" : op == Op.Sub ? "-" : "*";
        static string ref_(Node n) => n.Op == Op.Input ? n.Point + "." + n.Comp : n.Name ?? $"({ref_(n.A)} {opc(n.Op)} {ref_(n.B)})";

        // the shape of a value: inputs are alike, + and - are alike (same bounds), * and + / - commute
        static string shape(Node n, Dictionary<Node, string> shapes)
        {
            if (n.Op == Op.Input) return "c";
            string a = shapes[n.A], b = shapes[n.B];
            if (string.CompareOrdinal(a, b) > 0) { var t = a; a = b; b = t; }
            return n.Op == Op.Mul ? $"({a}*{b})" : $"({a}+{b})";
        }

        static string label_of(List<Node> nodes)
        {
            var named = nodes.Where(n => n.Name != null).Select(n => "`" + n.Name + "`").ToList();
            var unnamed = nodes.Where(n => n.Name == null).ToList();
            string s = string.Join(", ", named);
            if (unnamed.Count > 0) {
                string e = "`" + trim(ref_(unnamed[0])) + "`";
                s = named.Count == 0 ? e + (unnamed.Count > 1 ? $" and {inv(unnamed.Count - 1)} more of its shape" : "")
                    : s + $" and {inv(unnamed.Count)} more like {e}";
            }
            return s;
        }

        static string trim(string e) => e.StartsWith("(") && e.EndsWith(")") ? e.Substring(1, e.Length - 2) : e;

        // --- parsing: "ipredicate Name(a, b, ...)" (the points), then "x = expr" lines, then "sign expr"; the inputs
        // are p.x, p.y, p.w of a point p; '#' comments

        static List<Predicate> Parse(string spec)
        {
            var result = new List<Predicate>();
            Predicate cur = null;
            Dictionary<string, Node> vars = null, cse = null;
            int lineNo = 0;
            foreach (var raw in spec.Replace("\r", "").Split('\n')) {
                lineNo++;
                string line = raw.Trim();
                if (line.StartsWith("#") || line.Length == 0) continue;
                if (line.StartsWith("ipredicate ")) {
                    if (cur != null) throw new FormatException($"line {lineNo}: predicate {cur.Name} has no sign line");
                    cur = new Predicate();
                    string head = line.Substring(line.IndexOf(' ') + 1);
                    int open = head.IndexOf('('), close = head.IndexOf(')');
                    if (open < 0 || close < open) throw new FormatException($"line {lineNo}: expected 'ipredicate Name(points) bound b'");
                    cur.Name = head.Substring(0, open).Trim();
                    cur.Points = head.Substring(open + 1, close - open - 1).Split(',').Select(s => s.Trim()).ToList();
                    var tail = head.Substring(close + 1).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (tail.Count == 4 && tail[2] == "inputs" && (tail[3] == "xyz" || tail[3] == "xyw")) {
                        cur.Xyz = tail[3] == "xyz";
                        tail.RemoveRange(2, 2);
                    }
                    if (tail.Count != 2 || tail[0] != "bound" || !int.TryParse(tail[1], NumberStyles.None, CultureInfo.InvariantCulture, out cur.InputBits)
                        || cur.InputBits < 1 || cur.InputBits > 4096)
                        throw new FormatException($"line {lineNo}: expected 'ipredicate Name(points) bound b [inputs xyz|xyw]' (|v| <= 2^b - 1, b in 1..4096)");
                    foreach (var pt in cur.Points)
                        if (!System.Text.RegularExpressions.Regex.IsMatch(pt, "^[a-z][a-z0-9]*$") || Reserved.Contains(pt))
                            throw new FormatException($"line {lineNo}: bad point name '{pt}'");
                    if (cur.Points.Distinct().Count() != cur.Points.Count) throw new FormatException($"line {lineNo}: duplicate point");
                    vars = new Dictionary<string, Node>();
                    cse = new Dictionary<string, Node>();
                    continue;
                }
                if (cur == null) throw new FormatException($"line {lineNo}: expected 'ipredicate'");
                var pc = cur; var pcse = cse;
                Func<string, Node> input = id => {
                    int dot = id.IndexOf('.');
                    if (dot < 0 || !pc.Points.Contains(id.Substring(0, dot)) || id.Length != dot + 2 || pc.Comps.IndexOf(id[dot + 1]) < 0)
                        return null;
                    return make(pc, pcse, Op.Input, null, null, id);
                };
                Func<Op, Node, Node, Node> mk = (op, a, b) => make(pc, pcse, op, a, b, null);
                if (line.StartsWith("prepare ")) {
                    // 'prepare p, q, ...': a proper subset of the points, before the sign line, for inputs xyz
                    var pts = line.Substring(8).Split(',').Select(x => x.Trim()).ToList();
                    if (!cur.Xyz || cur.Prepare.Count > 0 || pts.Distinct().Count() != pts.Count || pts.Any(x => !cur.Points.Contains(x))
                        || pts.Count >= cur.Points.Count)
                        throw new FormatException($"line {lineNo}: expected 'prepare' once, with distinct points of the predicate, not all of them, for inputs xyz");
                    cur.Prepare = pts;
                    continue;
                }
                if (line.StartsWith("sign ")) {
                    // 'sign e1, e2, ...': the product of their signs (e.g. a determinant and a denominator)
                    foreach (var part in line.Substring(5).Split(','))
                        cur.Signs.Add(new ExprParser(part, vars, input, mk).Parse());
                    if (cur.Signs.Any(n => n.Op == Op.Input)) throw new FormatException($"line {lineNo}: the sign of an input needs no predicate");
                    if (cur.Signs.Distinct().Count() != cur.Signs.Count) throw new FormatException($"line {lineNo}: a repeated sign factor");
                    result.Add(cur);
                    cur = null;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0) throw new FormatException($"line {lineNo}: expected 'name = expression'");
                string v = line.Substring(0, eq).Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^[A-Za-z_][A-Za-z0-9_]*$") || vars.ContainsKey(v))
                    throw new FormatException($"line {lineNo}: bad or repeated name '{v}'");
                var node = new ExprParser(line.Substring(eq + 1), vars, input, mk).Parse();
                if (node.Op == Op.Input) throw new FormatException($"line {lineNo}: '{v}' only renames an input");
                if (node.Name != null) throw new FormatException($"line {lineNo}: '{v}' repeats the value of '{node.Name}'");
                node.Name = v;
                vars[v] = node;
            }
            if (cur != null) throw new FormatException($"predicate {cur.Name} has no sign line");
            return result;
        }

        // the expression graph: inputs are shared, other values only through their names (products commute)
        static Node make(Predicate p, Dictionary<string, Node> cse, Op op, Node a, Node b, string input)
        {
            if (op == Op.Mul && a.Id > b.Id) { var t = a; a = b; b = t; }
            string key = op == Op.Input ? "in:" + input : $"{op}:{a.Id}:{b.Id}";
            if (cse.TryGetValue(key, out var n)) {
                // a value the kernel shares is named once and used by its name; a value written twice is an error (the
                // kernel would compute it twice, the derivation once: its scratch would be undercounted)
                if (op != Op.Input)
                    throw new FormatException($"{p.Name}: a value is written twice; name it and use the name");
                return n;
            }
            n = new Node { Id = p.Nodes.Count, Op = op, A = a, B = b };
            if (op == Op.Input) { n.Point = input.Substring(0, input.IndexOf('.')); n.Comp = input[input.Length - 1]; }
            p.Nodes.Add(n);
            cse[key] = n;
            return n;
        }

        sealed class ExprParser
        {
            readonly string s; int pos;
            readonly Dictionary<string, Node> vars;
            readonly Func<string, Node> input;
            readonly Func<Op, Node, Node, Node> make;
            public ExprParser(string s, Dictionary<string, Node> vars, Func<string, Node> input, Func<Op, Node, Node, Node> make)
            { this.s = s; this.vars = vars; this.input = input; this.make = make; }
            public Node Parse() { var n = sum(); skip(); if (pos != s.Length) throw new FormatException($"unexpected '{s.Substring(pos)}'"); return n; }
            void skip() { while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++; }
            Node sum()
            {
                var n = product();
                while (true) {
                    skip();
                    if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) {
                        char c = s[pos++];
                        n = make(c == '+' ? Op.Add : Op.Sub, n, product());
                    } else return n;
                }
            }
            Node product()
            {
                var n = atom();
                while (true) {
                    skip();
                    if (pos < s.Length && s[pos] == '*') { pos++; n = make(Op.Mul, n, atom()); } else return n;
                }
            }
            Node atom()
            {
                skip();
                if (pos < s.Length && s[pos] == '(') { pos++; var n = sum(); skip(); if (pos >= s.Length || s[pos] != ')') throw new FormatException("missing ')'"); pos++; return n; }
                int start = pos;
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_' || s[pos] == '.')) pos++;
                string id = s.Substring(start, pos - start);
                if (id.Length == 0) throw new FormatException($"expected a name at '{s.Substring(start)}'");
                if (vars.TryGetValue(id, out var v)) return v;
                return input(id) ?? throw new FormatException($"unknown name '{id}'");
            }
        }

        /// <summary>
        /// every value of p for the given components (point -> (x, y, w), or (x, y, z) with 'inputs xyz'), exactly; for
        /// the tests (the bounds hold, the formula is the kernel's)
        /// </summary>
        public static Dictionary<Node, BigInteger> Evaluate(Predicate p, Dictionary<string, (BigInteger x, BigInteger y, BigInteger w)> points)
        {
            var v = new Dictionary<Node, BigInteger>();
            foreach (var n in p.Nodes) {
                switch (n.Op) {
                    case Op.Input:
                        var c = points[n.Point];
                        v[n] = n.Comp == 'x' ? c.x : n.Comp == 'y' ? c.y : c.w;    // w or z
                        break;
                    case Op.Add: v[n] = v[n.A] + v[n.B]; break;
                    case Op.Sub: v[n] = v[n.A] - v[n.B]; break;
                    case Op.Mul: v[n] = v[n.A] * v[n.B]; break;
                }
            }
            return v;
        }
    }
}
