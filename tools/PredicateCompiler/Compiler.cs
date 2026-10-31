// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Predicate compiler (experiment, docs/design/PREDICATE_COMPILER.md): reads sign predicates written once as
// polynomial formulas over double inputs and generates C# with
//   stage A: a floating-point filter whose error bound is derived here, by machine, from the formula;
//   stage B: the formula exact as an expansion, with the rounded leaf differences as exact values, and its
//            derived error bound (Shewchuk's stage B);
//   stage D: the formula exact as an expansion, with the leaf differences exact (two components each);
//   a BigInteger fallback for inputs out of the expansion range.
// The expansion lengths are known statically (they are written into the output); the buffers are sized at run
// time from the actual lengths, on the stack.
//
// Error model (round to nearest, u = 2^-53, eta = 2^-1075): fl(x + y) = (x + y)(1 + d), |d| <= u (sums that
// underflow are exact); fl(x y) = x y (1 + d) + n, |n| <= eta. For each node e, with computed value e~ and
// computed magnitude m~ (|e~| <= m~ by monotonicity of rounding), the invariant is
//     |e~ - e| <= c m~ + K A eta,
// c a rational constant, A >= 0 a polynomial (positive integer coefficients) in the magnitudes, K a constant:
//   input x:                c = 0, m~ = |x|, A = 0
//   leaf difference x - y:  c = u, m~ = |e~|, A = 0      (|fl(z) - z| <= u |fl(z)|; subnormal results are exact)
//   sum x + y:              c = (u + max(cx, cy)) / (1 - u), m~ = fl(m~x + m~y), A = Ax + Ay, K = max(Kx, Ky)
//   product x y:            c = (u + cx + cy + cx cy) / (1 - u), m~ = fl(m~x m~y),
//                           A = 1 + Ax m~y + Ay m~x + Ax + Ay, K = 2 max(1, Kx, Ky)
// (product: |e~ - x~y~| <= u m~x m~y + eta; |x~y~ - xy| <= |x~ - x| m~y + |x| |y~ - y| with
// |x| <= (1 + cx) m~x + Kx Ax eta; m~x m~y <= (m~ + eta) / (1 - u); and Ky Ay eta <= 1, which holds when the
// computed A of the root is finite, since every A of a node appears in it with a coefficient >= 1.)
// The root is a sum: its sign is the sign of e~ when |e~| (1 - u) > max(cx, cy) (m~x + m~y) + K A eta. The
// filter tests |e~| > C m~ + fl(A~ + 1) U with C >= max(cx, cy) / (1 - u)^4 and U = 2^s eta, s the smallest
// with (1 - u)^(N + 3) 2^s >= max(K / (1 - u), 2), N the number of operations that evaluate A~ (all terms are
// nonnegative; the +1 absorbs underflow in A~ and the absolute errors of the two products of the bound).
// Overflow gives an infinite or NaN value, magnitude or bound: the test fails and the exact stages decide.
//
// Stage B: with the rounded leaf differences d~ taken as exact (|d~ - d| <= u |d~|) and exact arithmetic,
// cB(input) = 0, cB(leaf) = u, cB(x + y) = max, cB(x y) = cx + cy + cx cy, and |V - e| <= cB P with P the
// permanent at the |d~|. In range, no product of the magnitudes underflows, so P <= m~ / (1 - u)^depth, with
// depth(leaf) = 0, depth(x + y) = max + 1 and depth(x y) = dx + dy + 1 (the factors' roundings multiply); the
// stage tests the exact V against fl(CB m~), CB >= cB / (1 - u)^(depth + 1).
//
// Stage C: V + fl(r), r the first-order term of the tails (derivative times tails, as dual numbers), against
// fl(CC m~), CC >= (E + R) / (1 - u)^(depth + 1), E bounding the rounding of r and R = (1 + u)^D - 1 - D u the
// terms of order >= 2 (D the degree in the leaf differences); the rules for E are in Analyze.
//
// Formulas must be homogeneous (the BigInteger fallback scales all inputs by one power of two, which keeps the
// sign only then) and contain at least one leaf difference.
//
// Range: all inputs 0 or in [2^-100, 2^100]. Then inputs, differences and their rounding errors are multiples
// of 2^-152 and below 2^102; every product of up to 6 of them (and its rounding error) is a normal double.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace G3PredicateCompiler
{
    /// <summary> an exact rational (for the error constants) </summary>
    public readonly struct Rational
    {
        public readonly BigInteger Num, Den;
        public Rational(BigInteger num, BigInteger den)
        {
            if (den.Sign < 0) { num = -num; den = -den; }
            var g = BigInteger.GreatestCommonDivisor(num, den);
            if (g.IsZero) g = 1;
            Num = num / g; Den = den / g;
        }
        public static Rational Int(long v) => new Rational(v, 1);
        public static readonly Rational Zero = Int(0), One = Int(1);
        /// <summary> u = 2^-53 </summary>
        public static readonly Rational U = new Rational(1, BigInteger.Pow(2, 53));
        public static Rational Pow2(int e) => e >= 0 ? new Rational(BigInteger.Pow(2, e), 1) : new Rational(1, BigInteger.Pow(2, -e));
        public static Rational operator +(Rational a, Rational b) => new Rational(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
        public static Rational operator -(Rational a, Rational b) => new Rational(a.Num * b.Den - b.Num * a.Den, a.Den * b.Den);
        public static Rational operator *(Rational a, Rational b) => new Rational(a.Num * b.Num, a.Den * b.Den);
        public static Rational operator /(Rational a, Rational b) => new Rational(a.Num * b.Den, a.Den * b.Num);
        public static bool operator >=(Rational a, Rational b) => (a - b).Num.Sign >= 0;
        public static bool operator <=(Rational a, Rational b) => (b - a).Num.Sign >= 0;
        public static Rational Max(Rational a, Rational b) => a >= b ? a : b;
        public Rational Pow(int n) { Rational r = One; for (int i = 0; i < n; ++i) r = r * this; return r; }

        /// <summary> the exact value of a finite double </summary>
        public static Rational FromDouble(double d)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            int e = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = -1074; else { frac |= 1L << 52; e -= 1075; }
            if (bits < 0) frac = -frac;
            return Int(frac) * Pow2(e);
        }

        /// <summary> the smallest double >= this </summary>
        public double RoundUp()
        {
            double d = (double)Num / (double)Den;
            while (!(FromDouble(d) >= this)) d = Math.BitIncrement(d);
            while (FromDouble(Math.BitDecrement(d)) >= this) d = Math.BitDecrement(d);
            return d;
        }

        /// <summary> this as a multiple of u, for comments </summary>
        public string InUnitsOfU() => ((double)(this / U).Num / (double)(this / U).Den).ToString("0.###############", CultureInfo.InvariantCulture) + " u";
    }

    enum Op { Input, Add, Sub, Mul }

    sealed class Node
    {
        public int Id;
        public Op Op;
        public Node A, B;
        public string Name;     // input
        public int Degree;
        public bool IsLeafDiff => (Op == Op.Add || Op == Op.Sub) && A.Op == Op.Input && B.Op == Op.Input;

        // stage A
        public Rational C;
        public bool MagIsAbs;       // m~ = |e~|
        public bool MagIsValue;     // m~ = e~ (and e~ >= 0)
        public Poly Abs;            // A
        public Rational K;
        // stage B
        public Rational CB;
        // stage C: CP: |v~ - v| <= CP P for the value v at the rounded leaves; the first-order term r (the tails
        // times the derivative) has |r~ - r| <= E P, |r| <= G P; LeafDeg: the degree in the leaf differences
        public Rational CP, E, G;
        public bool HasTail;
        public int LeafDeg;
        public int Depth;
        // static expansion lengths
        public long LenB, LenD;
    }

    /// <summary> a polynomial with positive integer coefficients in magnitude variables (the A of the bounds) </summary>
    sealed class Poly
    {
        // monomial (sorted variable ids, joined) -> coefficient
        public readonly SortedDictionary<string, BigInteger> Terms = new SortedDictionary<string, BigInteger>(StringComparer.Ordinal);
        public static Poly Const(long c) { var p = new Poly(); if (c != 0) p.Terms[""] = c; return p; }
        public static Poly Var(int id) { var p = new Poly(); p.Terms[id.ToString(CultureInfo.InvariantCulture)] = 1; return p; }
        public bool IsZero => Terms.Count == 0;
        public bool IsConst => Terms.Keys.All(k => k == "");
        public BigInteger ConstValue => Terms.TryGetValue("", out var c) ? c : 0;
        public static Poly operator +(Poly a, Poly b)
        {
            var p = new Poly();
            foreach (var t in a.Terms) p.Terms[t.Key] = t.Value;
            foreach (var t in b.Terms) p.Terms[t.Key] = (p.Terms.TryGetValue(t.Key, out var c) ? c : 0) + t.Value;
            return p;
        }
        public Poly TimesVar(int id)
        {
            var p = new Poly();
            foreach (var t in Terms) {
                var vars = t.Key == "" ? new List<int>() : t.Key.Split('*').Select(int.Parse).ToList();
                vars.Add(id);
                vars.Sort();
                p.Terms[string.Join("*", vars)] = t.Value;
            }
            return p;
        }
        public IEnumerable<int> Vars => Terms.Keys.Where(k => k != "").SelectMany(k => k.Split('*')).Select(int.Parse).Distinct();
    }

    sealed class Predicate
    {
        public string Name;
        public List<string> Inputs = new List<string>();
        public List<Node> Nodes = new List<Node>();     // topological order
        public Node Root;
        public string Comment;
    }

    public static class PredicateCompiler
    {
        const int RangeExponent = 100;
        const int MaxDegree = 6;
        static readonly HashSet<string> Reserved = new HashSet<string> { "z", "c", "nc", "first", "bound", "boundB", "boundC", "skip", "sB" };

        /// <summary> the generated C# file for a spec (see predicates.spec) </summary>
        public static string Compile(string spec)
        {
            var preds = Parse(spec);
            var sb = new StringBuilder();
            sb.Append("// <auto-generated>\n");
            sb.Append("// Generated by tools/PredicateCompiler from tools/PredicateCompiler/predicates.spec. Do not edit;\n");
            sb.Append("// regenerate with: dotnet run --project tools/PredicateCompiler -- tools/PredicateCompiler/predicates.spec math/Generated/GeneratedPredicates.cs\n");
            sb.Append("// </auto-generated>\n");
            sb.Append("// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt\n");
            sb.Append("using System;\nusing System.Numerics;\nusing System.Runtime.CompilerServices;\n\nnamespace g3\n{\n");
            sb.Append("    /// <summary>\n");
            sb.Append("    /// Sign predicates generated from formulas (experiment, docs/design/PREDICATE_COMPILER.md; not used by the\n");
            sb.Append("    /// library yet). Each one: a floating-point filter with a machine-derived error bound, then exact\n");
            sb.Append("    /// expansion stages (PredicateRuntime), then BigInteger for inputs out of the expansion range.\n");
            sb.Append("    /// </summary>\n");
            sb.Append("    internal static unsafe class GeneratedPredicates\n    {\n");
            bool first = true;
            foreach (var p in preds) {
                if (!first) sb.Append('\n');
                first = false;
                Analyze(p);
                Emit(p, sb);
            }
            sb.Append("    }\n}\n");
            return sb.ToString();
        }

        // --- parsing: "predicate Name(a, b, ...)", then "x = expr" lines, then "sign expr"; '#' comments

        static List<Predicate> Parse(string spec)
        {
            var result = new List<Predicate>();
            Predicate cur = null;
            Dictionary<string, Node> vars = null;
            Dictionary<string, Node> cse = null;
            var comment = new StringBuilder();
            int lineNo = 0;
            foreach (var raw in spec.Replace("\r", "").Split('\n')) {
                lineNo++;
                string line = raw.Trim();
                if (line.StartsWith("#")) { comment.Append(line.Substring(1).Trim()).Append(' '); continue; }
                if (line.Length == 0) { comment.Clear(); continue; }    // only the comment block right before a predicate
                if (line.StartsWith("predicate ")) {
                    if (cur != null) throw new FormatException($"line {lineNo}: predicate {cur.Name} has no sign line");
                    cur = new Predicate { Comment = comment.ToString().Trim() };
                    comment.Clear();
                    string head = line.Substring(10);
                    int open = head.IndexOf('('), close = head.IndexOf(')');
                    cur.Name = head.Substring(0, open).Trim();
                    cur.Inputs = head.Substring(open + 1, close - open - 1).Split(',').Select(s => s.Trim()).ToList();
                    vars = new Dictionary<string, Node>();
                    cse = new Dictionary<string, Node>();
                    foreach (var name in cur.Inputs)
                        vars[name] = make(cur, cse, Op.Input, null, null, name);
                    continue;
                }
                if (cur == null) throw new FormatException($"line {lineNo}: expected 'predicate'");
                if (line.StartsWith("sign ")) {
                    cur.Root = new ExprParser(line.Substring(5), vars, (op, a, b) => make(cur, cse, op, a, b, null)).Parse();
                    if (cur.Root.Op != Op.Add && cur.Root.Op != Op.Sub)
                        throw new FormatException($"line {lineNo}: the formula must be a sum or difference at the top");
                    result.Add(cur);
                    cur = null;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0) throw new FormatException($"line {lineNo}: expected 'name = expression'");
                string v = line.Substring(0, eq).Trim();
                vars[v] = new ExprParser(line.Substring(eq + 1), vars, (op, a, b) => make(cur, cse, op, a, b, null)).Parse();
            }
            if (cur != null) throw new FormatException($"predicate {cur.Name} has no sign line");
            return result;
        }

        static Node make(Predicate p, Dictionary<string, Node> cse, Op op, Node a, Node b, string name)
        {
            // structural sharing; products are commutative
            if (op == Op.Mul && a.Id > b.Id) { var t = a; a = b; b = t; }
            string key = op == Op.Input ? "in:" + name : $"{op}:{a.Id}:{b.Id}";
            if (cse.TryGetValue(key, out var n)) return n;
            n = new Node { Id = p.Nodes.Count, Op = op, A = a, B = b, Name = name };
            p.Nodes.Add(n);
            cse[key] = n;
            return n;
        }

        sealed class ExprParser
        {
            readonly string s; int pos;
            readonly Dictionary<string, Node> vars;
            readonly Func<Op, Node, Node, Node> make;
            public ExprParser(string s, Dictionary<string, Node> vars, Func<Op, Node, Node, Node> make) { this.s = s; this.vars = vars; this.make = make; }
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
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
                string id = s.Substring(start, pos - start);
                if (id.Length == 0) throw new FormatException($"expected a name at '{s.Substring(start)}'");
                if (!vars.TryGetValue(id, out var v)) throw new FormatException($"unknown name '{id}'");
                return v;
            }
        }

        // --- analysis

        static void Analyze(Predicate p)
        {
            var u = Rational.U;
            var oneMinusU = Rational.One - u;
            foreach (var n in p.Nodes) {
                switch (n.Op) {
                    case Op.Input:
                        n.Degree = 1; n.C = Rational.Zero; n.MagIsAbs = true; n.Abs = Poly.Const(0); n.K = Rational.One;
                        n.CB = Rational.Zero; n.Depth = 0; n.LenB = 1; n.LenD = 1;
                        n.CP = n.E = n.G = Rational.Zero; n.HasTail = false; n.LeafDeg = 0;
                        break;
                    case Op.Add:
                    case Op.Sub:
                        if (n.A.Degree != n.B.Degree)
                            throw new NotSupportedException($"{p.Name}: not homogeneous (degrees {n.A.Degree} and {n.B.Degree} in a sum)");
                        n.Degree = n.A.Degree;
                        n.LenB = n.A.LenB + n.B.LenB;
                        n.LenD = n.A.LenD + n.B.LenD;
                        if (n.IsLeafDiff) {
                            n.C = u; n.MagIsAbs = true; n.Abs = Poly.Const(0); n.K = Rational.One;
                            n.CB = u; n.Depth = 0; n.LenB = 1; n.LenD = 2;
                            n.CP = n.E = Rational.Zero; n.G = u; n.HasTail = true; n.LeafDeg = 1;
                        } else {
                            n.C = (u + Rational.Max(n.A.C, n.B.C)) / oneMinusU;
                            n.MagIsValue = n.Op == Op.Add && n.A.MagIsValue && n.B.MagIsValue;
                            n.MagIsAbs = n.MagIsValue;
                            n.Abs = n.A.Abs + n.B.Abs;
                            n.K = Rational.Max(n.A.K, n.B.K);
                            n.CB = Rational.Max(n.A.CB, n.B.CB);
                            n.Depth = Math.Max(n.A.Depth, n.B.Depth) + 1;
                            var mc = Rational.Max(n.A.CP, n.B.CP);
                            n.CP = mc + u * (Rational.One + mc);
                            // r~ = fl(ra~ +- rb~): |error| <= Ea Pa + Eb Pb + u (|ra~| + |rb~|)
                            n.E = Rational.Max(n.A.HasTail ? n.A.E + u * (n.A.G + n.A.E) : Rational.Zero,
                                               n.B.HasTail ? n.B.E + u * (n.B.G + n.B.E) : Rational.Zero);
                            n.G = Rational.Max(n.A.G, n.B.G);
                            n.HasTail = n.A.HasTail || n.B.HasTail;
                            n.LeafDeg = Math.Max(n.A.LeafDeg, n.B.LeafDeg);
                        }
                        break;
                    case Op.Mul:
                        n.Degree = n.A.Degree + n.B.Degree;
                        n.C = (u + n.A.C + n.B.C + n.A.C * n.B.C) / oneMinusU;
                        n.MagIsAbs = n.A.MagIsAbs && n.B.MagIsAbs;
                        n.MagIsValue = (n.A == n.B && n.A.MagIsAbs) || (n.A.MagIsValue && n.B.MagIsValue);
                        n.Abs = Poly.Const(1) + n.A.Abs.TimesVar(n.B.Id) + n.B.Abs.TimesVar(n.A.Id) + n.A.Abs + n.B.Abs;
                        n.K = Rational.Int(2) * Rational.Max(Rational.One, Rational.Max(n.A.K, n.B.K));
                        n.CB = n.A.CB + n.B.CB + n.A.CB * n.B.CB;
                        n.Depth = n.A.Depth + n.B.Depth + 1;
                        n.LenB = 2 * n.A.LenB * n.B.LenB;
                        n.LenD = 2 * n.A.LenD * n.B.LenD;
                        {
                            Rational cx = n.A.CP, cy = n.B.CP;
                            n.CP = cx + cy + cx * cy + u * (Rational.One + cx) * (Rational.One + cy);
                            // r~ = fl(fl(vx~ ry~) + fl(vy~ rx~)); a = fl(vx~ ry~): |a - vx ry| <= (cx (Gy + Ey) + Ey
                            // + u (1 + cx)(Gy + Ey)) Px Py, |a| <= (1 + u)(1 + cx)(Gy + Ey) Px Py
                            Rational ea = cx * (n.B.G + n.B.E) + n.B.E + u * (Rational.One + cx) * (n.B.G + n.B.E);
                            Rational ma = (Rational.One + u) * (Rational.One + cx) * (n.B.G + n.B.E);
                            Rational eb = cy * (n.A.G + n.A.E) + n.A.E + u * (Rational.One + cy) * (n.A.G + n.A.E);
                            Rational mb = (Rational.One + u) * (Rational.One + cy) * (n.A.G + n.A.E);
                            if (n.A.HasTail && n.B.HasTail) n.E = ea + eb + u * (ma + mb);
                            else if (n.B.HasTail) n.E = ea;
                            else if (n.A.HasTail) n.E = eb;
                            else n.E = Rational.Zero;
                            n.G = n.A.G + n.B.G;
                            n.HasTail = n.A.HasTail || n.B.HasTail;
                            n.LeafDeg = n.A.LeafDeg + n.B.LeafDeg;
                        }
                        break;
                }
            }
            if (!p.Nodes.Any(n => n.IsLeafDiff))
                throw new NotSupportedException($"{p.Name}: no difference of two inputs (the exact stages start from those)");
            foreach (var name in p.Inputs)
                if (Reserved.Contains(name) || System.Text.RegularExpressions.Regex.IsMatch(name, "^(v|m|t|r|b|d|nb|nd)[0-9]+$") || name.EndsWith("_"))
                    throw new NotSupportedException($"{p.Name}: input name '{name}' clashes with the generated code");
            if (p.Root.Degree > MaxDegree)
                throw new NotSupportedException($"{p.Name}: degree {p.Root.Degree} > {MaxDegree} (the expansion range assumes at most {MaxDegree})");
            if (p.Root.K >= Rational.Pow2(50))
                throw new NotSupportedException($"{p.Name}: K too large");
        }

        // the bound of stage A: C, the A~ expression (null if constant), its constant, s
        sealed class FilterBound
        {
            public double C;
            public string AExpr;    // null: constant
            public BigInteger AConst;
            public int N, S;
            public double D;        // constant (A + 1) U, if AExpr == null
        }

        static FilterBound filter_bound(Predicate p, out Rational cExact)
        {
            var u = Rational.U;
            var oneMinusU = Rational.One - u;
            var r = p.Root;
            cExact = Rational.Max(r.A.C, r.B.C) / oneMinusU.Pow(4);
            var fb = new FilterBound { C = cExact.RoundUp() };
            var A = r.A.Abs + r.B.Abs;
            var K = Rational.Max(r.A.K, r.B.K);
            int n;
            fb.AExpr = poly_expr(A, out n);
            fb.N = n;
            if (A.IsConst) { fb.AExpr = null; fb.AConst = A.ConstValue; }
            var need = Rational.Max(K / oneMinusU, Rational.Int(2));
            int s = 0;
            while (!(oneMinusU.Pow(fb.N + 3) * Rational.Pow2(s) >= need)) s++;
            fb.S = s;
            if (fb.AExpr == null) {
                var d = new Rational(fb.AConst + 1, 1) * Rational.Pow2(s - 1075);
                fb.D = d.RoundUp();
                if (!(Rational.FromDouble(fb.D) <= d && Rational.FromDouble(fb.D) >= d))
                    throw new InvalidOperationException("constant bound not representable");
            }
            return fb;
        }

        // A~ as C# (grouped by coefficient), and its number of floating-point operations
        static string poly_expr(Poly a, out int ops)
        {
            ops = 0;
            var groups = new SortedDictionary<BigInteger, List<string>>();
            foreach (var t in a.Terms) {
                if (t.Key == "") continue;
                var vars = t.Key.Split('*').Select(v => "m" + v).ToList();
                ops += vars.Count - 1;
                if (!groups.TryGetValue(t.Value, out var list)) groups[t.Value] = list = new List<string>();
                list.Add(string.Join(" * ", vars));
            }
            var parts = new List<string>();
            foreach (var g in groups) {
                ops += g.Value.Count - 1;
                string sum = string.Join(" + ", g.Value);
                if (g.Key == 1) parts.Add(sum);
                else { ops++; parts.Add($"{g.Key}.0 * " + (g.Value.Count > 1 ? $"({sum})" : sum)); }
            }
            if (a.ConstValue != 0) parts.Add($"{a.ConstValue}.0");
            ops += Math.Max(0, parts.Count - 1);
            return string.Join(" + ", parts);
        }

        // --- emission

        static string args(Predicate p) => string.Join(", ", p.Inputs);
        static string pars(Predicate p) => string.Join(", ", p.Inputs.Select(i => "double " + i));
        static string val(Node n) => n.Op == Op.Input ? n.Name : "v" + n.Id;
        static string opc(Op op) => op == Op.Add ? "+" : op == Op.Sub ? "-" : "*";
        static string lit(double d) => d.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");

        static void Emit(Predicate p, StringBuilder sb)
        {
            Rational cExact;
            var fb = filter_bound(p, out cExact);
            var root = p.Root;
            var cB = root.CB;
            var CB = (cB / (Rational.One - Rational.U).Pow(root.Depth + 1));
            double cbD = CB.RoundUp();
            // stage C: |V + r~ - det| <= (E + R) P, R = (1 + u)^D - 1 - D u bounds the terms of order >= 2 in the tails
            int D = root.LeafDeg;
            var R = (Rational.One + Rational.U).Pow(D) - Rational.One - Rational.Int(D) * Rational.U;
            var cC = root.E + R;
            double ccD = (cC / (Rational.One - Rational.U).Pow(root.Depth + 1)).RoundUp();

            string I1 = "        ", I2 = I1 + "    ", I3 = I2 + "    ";
            sb.Append($"{I1}// {p.Name}: {p.Comment}\n");
            sb.Append($"{I1}// degree {root.Degree}; filter: |det| > C m + fl(A + 1) 2^{fb.S - 1075}, C = {lit(fb.C)} ({cExact.InUnitsOfU()} before rounding),\n");
            sb.Append($"{I1}//   K = {root.K.Num}, A~ in {fb.N} operations; stage B: cB = {cB.InUnitsOfU()}, depth {root.Depth};\n");
            sb.Append($"{I1}//   stage C: cC = {(cC / Rational.U).InUnitsOfU()} (times u), leaf degree {D};\n");
            sb.Append($"{I1}//   expansion lengths at most {root.LenB} (stage B), {root.LenD} (stage D)\n");

            // the filter values and magnitudes
            var magNeeded = new HashSet<int>();
            void needMag(Node n)
            {
                if (!magNeeded.Add(n.Id)) return;
                if (!n.MagIsAbs && n.Op != Op.Input) { needMag(n.A); needMag(n.B); }
            }
            needMag(root);
            foreach (var t in root.A.Abs.Vars.Concat(root.B.Abs.Vars)) needMag(p.Nodes[t]);

            void emitValues(StringBuilder o, string ind)
            {
                foreach (var n in p.Nodes)
                    if (n.Op != Op.Input)
                        o.Append($"{ind}double {val(n)} = {val(n.A)} {opc(n.Op)} {val(n.B)};\n");
            }
            void emitMags(StringBuilder o, string ind)
            {
                foreach (var n in p.Nodes) {
                    if (!magNeeded.Contains(n.Id)) continue;
                    string e = n.MagIsValue ? val(n) : n.MagIsAbs ? $"Math.Abs({val(n)})" : $"m{n.A.Id} {opc(n.Op == Op.Mul ? Op.Mul : Op.Add)} m{n.B.Id}";
                    o.Append($"{ind}double m{n.Id} = {e};\n");
                }
            }
            string boundExpr = fb.AExpr == null
                ? $"{lit(fb.C)} * m{root.Id} + {lit(fb.D)}"
                : $"{lit(fb.C)} * m{root.Id} + ({fb.AExpr} + 1.0) * {lit(Rational.Pow2(fb.S - 1075).RoundUp())}";

            // public entry: filter, then the exact stages
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}public static int {p.Name}({pars(p)})\n{I1}{{\n");
            sb.Append($"{I2}int s = {p.Name}Filter({args(p)});\n");
            sb.Append($"{I2}return s != 0 ? s : {p.Name}Exact({args(p)}, 0);\n{I1}}}\n\n");

            // the filter: 0 if undecided
            sb.Append($"{I1}/// <summary> the filter (stage A): +1, -1, or 0 if it does not decide </summary>\n");
            sb.Append($"{I1}[MethodImpl(MethodImplOptions.AggressiveInlining)]\n");
            sb.Append($"{I1}internal static int {p.Name}Filter({pars(p)})\n{I1}{{\n");
            emitValues(sb, I2);
            emitMags(sb, I2);
            sb.Append($"{I2}double bound = {boundExpr};\n");
            sb.Append($"{I2}if ({val(root)} > bound) return 1;\n");
            sb.Append($"{I2}if (-{val(root)} > bound) return -1;\n");
            sb.Append($"{I2}return 0;\n{I1}}}\n\n");

            // exact stages
            var leaves = p.Nodes.Where(n => n.IsLeafDiff).ToList();
            sb.Append($"{I1}/// <summary> the exact stages; skip (for tests) 1: without the bounds of stages B and C\n");
            sb.Append($"{I1}/// (stage D decides); 2: 2 if stages B and C do not decide; 3: 2 if stage B does not decide </summary>\n");
            sb.Append($"{I1}[MethodImpl(MethodImplOptions.NoInlining | JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}Exact({pars(p)}, int skip)\n{I1}{{\n");
            sb.Append($"{I2}if (!({string.Join(" && ", p.Inputs.Select(i => $"PredicateRuntime.InRange({i})"))}))\n");
            sb.Append($"{I3}return {p.Name}BigInteger({args(p)});\n");
            // stage B
            sb.Append($"{I2}// stage B: the rounded leaf differences as exact values\n");
            foreach (var n in leaves)
                sb.Append($"{I2}double {val(n)} = {val(n.A)} {opc(n.Op)} {val(n.B)};\n");
            emitExpansions(p, sb, I2, "b", stageD: false);
            var tails = leaves.Select(n => $"t{n.Id}").ToList();
            foreach (var n in leaves)
                sb.Append($"{I2}double t{n.Id} = PredicateRuntime.{(n.Op == Op.Sub ? "DiffTail" : "SumTail")}({val(n.A)}, {val(n.B)}, {val(n)});\n");
            sb.Append($"{I2}if ({string.Join(" && ", tails.Select(t => t + " == 0"))})\n");
            sb.Append($"{I3}return PredicateRuntime.Sign(b{root.Id}, nb{root.Id});     // the differences were exact: so is B\n");
            sb.Append($"{I2}if (skip != 1) {{\n");
            // the filter's values and the permanent of the rounded differences
            foreach (var n in p.Nodes)
                if (n.Op != Op.Input && !n.IsLeafDiff)
                    sb.Append($"{I3}double {val(n)} = {val(n.A)} {opc(n.Op)} {val(n.B)};\n");
            emitMags(sb, I3);
            sb.Append($"{I3}double boundB = {lit(cbD)} * m{root.Id};\n");
            sb.Append($"{I3}int sB = PredicateRuntime.Sign(b{root.Id}, nb{root.Id});\n");
            sb.Append($"{I3}if (sB != 0 && PredicateRuntime.CompareTo(b{root.Id}, nb{root.Id}, sB * boundB) == sB) return sB;\n");
            sb.Append($"{I3}if (skip == 3) return 2;\n");
            sb.Append($"{I3}// stage C: B + the first-order term of the tails (the derivative, propagated as dual numbers)\n");
            foreach (var n in p.Nodes) {
                if (!n.HasTail || n.IsLeafDiff) continue;
                string e;
                if (n.Op == Op.Mul) {
                    var terms = new List<string>();
                    if (n.B.HasTail) terms.Add($"{val(n.A)} * {tail(n.B)}");
                    if (n.A.HasTail) terms.Add($"{val(n.B)} * {tail(n.A)}");
                    e = string.Join(" + ", terms);
                } else if (n.A.HasTail && n.B.HasTail) e = $"{tail(n.A)} {opc(n.Op)} {tail(n.B)}";
                else if (n.A.HasTail) e = tail(n.A);
                else e = (n.Op == Op.Sub ? "-" : "") + tail(n.B);
                sb.Append($"{I3}double {tail(n)} = {e};\n");
            }
            sb.Append($"{I3}double first = {tail(root)};\n");
            sb.Append($"{I3}double* c = stackalloc double[nb{root.Id} + 1]; int nc = PredicateRuntime.Sum(b{root.Id}, nb{root.Id}, &first, 1, c, 1);\n");
            sb.Append($"{I3}double boundC = {lit(ccD)} * m{root.Id};\n");
            sb.Append($"{I3}if (PredicateRuntime.CompareTo(c, nc, boundC) > 0) return 1;\n");
            sb.Append($"{I3}if (PredicateRuntime.CompareTo(c, nc, -boundC) < 0) return -1;\n");
            sb.Append($"{I2}}}\n");
            sb.Append($"{I2}if (skip >= 2) return 2;\n");
            sb.Append($"{I2}// stage D: the leaf differences exact\n");
            emitExpansions(p, sb, I2, "d", stageD: true);
            sb.Append($"{I2}return PredicateRuntime.Sign(d{root.Id}, nd{root.Id});\n{I1}}}\n\n");

            // BigInteger
            sb.Append($"{I1}[MethodImpl(MethodImplOptions.NoInlining)]\n");
            sb.Append($"{I1}internal static int {p.Name}BigInteger({pars(p)})\n{I1}{{\n");
            sb.Append($"{I2}BigInteger[] z = ExactPredicates.to_integers({args(p)});\n");
            for (int i = 0; i < p.Inputs.Count; ++i)
                sb.Append($"{I2}BigInteger {p.Inputs[i]}_ = z[{i}];\n");
            foreach (var n in p.Nodes)
                if (n.Op != Op.Input)
                    sb.Append($"{I2}BigInteger {val(n)} = {bval(n.A)} {opc(n.Op)} {bval(n.B)};\n");
            sb.Append($"{I2}return {val(root)}.Sign;\n{I1}}}\n");
        }

        static string tail(Node n) => n.IsLeafDiff ? "t" + n.Id : "r" + n.Id;

        static string bval(Node n) => n.Op == Op.Input ? n.Name + "_" : "v" + n.Id;

        static void emitExpansions(Predicate p, StringBuilder sb, string ind, string pre, bool stageD)
        {
            foreach (var n in p.Nodes) {
                string e = pre + n.Id, len = "n" + pre + n.Id;
                switch (n.Op) {
                    case Op.Input:
                        // only when used directly in an expansion operation
                        if (!p.Nodes.Any(m => m.Op != Op.Input && !m.IsLeafDiff && (m.A == n || m.B == n))) continue;
                        sb.Append($"{ind}double* {e} = stackalloc double[1]; int {len} = PredicateRuntime.One({n.Name}, {e});\n");
                        break;
                    default:
                        if (n.IsLeafDiff) {
                            if (stageD)
                                sb.Append($"{ind}double* {e} = stackalloc double[2]; int {len} = PredicateRuntime.{(n.Op == Op.Sub ? "TwoDiff" : "TwoSum")}({n.A.Name}, {n.B.Name}, {e});\n");
                            else
                                sb.Append($"{ind}double* {e} = stackalloc double[1]; int {len} = PredicateRuntime.One({val(n)}, {e});\n");
                        } else if (n.Op == Op.Mul) {
                            string a = pre + n.A.Id, b = pre + n.B.Id;
                            sb.Append($"{ind}double* {e} = stackalloc double[2 * n{a} * n{b}]; int {len} = PredicateRuntime.Mul({a}, n{a}, {b}, n{b}, {e});\n");
                        } else {
                            string a = pre + n.A.Id, b = pre + n.B.Id;
                            sb.Append($"{ind}double* {e} = stackalloc double[n{a} + n{b}]; int {len} = PredicateRuntime.Sum({a}, n{a}, {b}, n{b}, {e}, {(n.Op == Op.Add ? "1" : "-1")});\n");
                        }
                        break;
                }
            }
        }
    }
}
