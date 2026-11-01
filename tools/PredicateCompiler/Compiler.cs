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
        public BigInteger Max;  // the bound at m
        public int Bits => Max.IsZero ? 0 : (int)Max.GetBitLength();
        public int Limbs => (Bits + 63) / 64;
        public int ArenaLimbs;  // the length rule: the limbs the operation takes (inputs: their length)
        public int LengthOnly;  // the same without the value bounds
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
        public Node Root;
        /// <summary> the limbs one call takes from its FixedArena (the length rule) </summary>
        public int ArenaScratch;
        /// <summary> ArenaScratch without the value bounds (the operands' lengths only) </summary>
        public int LengthScratch;
        /// <summary> the limbs one call needs if every value had a buffer of its bound (the value rule) </summary>
        public int ValueScratch;
    }

    public static class PredicateCompiler
    {
        public const string TableBegin = "<!-- generated by tools/PredicateCompiler --table; do not edit -->";
        public const string TableEnd = "<!-- end of the generated table -->";

        /// <summary> the predicates of a spec (see predicates.spec) with the bounds of all their values </summary>
        public static List<Predicate> Analyze(string spec)
        {
            var preds = Parse(spec);
            foreach (var p in preds) {
                value_bounds(p);
                fixedarith_lengths(p);
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
            }
            p.ValueScratch = p.Nodes.Where(n => n.Op != Op.Input).Sum(n => n.Limbs);
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
                    string label = nodes.Contains(p.Root) ? "the determinant" : label_of(nodes);
                    var n0 = nodes[0];
                    sb.Append($"| `{p.Name}` | {label} | `{n0.Bound}` | {inv(n0.Bits)} | {inv(n0.Limbs)} | {inv(n0.ArenaLimbs)} |\n");
                }
            }
            foreach (var p in preds)
                sb.Append($"| `{p.Name}` | scratch of one call (sum over the values; lengths only: {inv(p.LengthScratch)}) | | | {inv(p.ValueScratch)} | {inv(p.ArenaScratch)} |\n");
            sb.Append(TableEnd).Append('\n');
            return sb.ToString();
        }

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
                    var tail = head.Substring(close + 1).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tail.Length != 2 || tail[0] != "bound" || !int.TryParse(tail[1], NumberStyles.None, CultureInfo.InvariantCulture, out cur.InputBits)
                        || cur.InputBits < 1 || cur.InputBits > 4096)
                        throw new FormatException($"line {lineNo}: expected 'ipredicate Name(points) bound b' (|v| <= 2^b - 1, b in 1..4096)");
                    foreach (var pt in cur.Points)
                        if (!System.Text.RegularExpressions.Regex.IsMatch(pt, "^[a-z][a-z0-9]*$"))
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
                    if (dot < 0 || !pc.Points.Contains(id.Substring(0, dot)) || id.Length != dot + 2 || "xyw".IndexOf(id[dot + 1]) < 0)
                        return null;
                    return make(pc, pcse, Op.Input, null, null, id);
                };
                Func<Op, Node, Node, Node> mk = (op, a, b) => make(pc, pcse, op, a, b, null);
                if (line.StartsWith("sign ")) {
                    cur.Root = new ExprParser(line.Substring(5), vars, input, mk).Parse();
                    if (cur.Root.Op == Op.Input) throw new FormatException($"line {lineNo}: the sign of an input needs no predicate");
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

        // structural sharing (a value written twice is computed once, as the kernels do); products are commutative
        static Node make(Predicate p, Dictionary<string, Node> cse, Op op, Node a, Node b, string input)
        {
            if (op == Op.Mul && a.Id > b.Id) { var t = a; a = b; b = t; }
            string key = op == Op.Input ? "in:" + input : $"{op}:{a.Id}:{b.Id}";
            if (cse.TryGetValue(key, out var n)) return n;
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
        /// every value of p for the given components (point -> (x, y, w)), exactly; for the tests (the bounds hold, the
        /// formula is the kernel's)
        /// </summary>
        public static Dictionary<Node, BigInteger> Evaluate(Predicate p, Dictionary<string, (BigInteger x, BigInteger y, BigInteger w)> points)
        {
            var v = new Dictionary<Node, BigInteger>();
            foreach (var n in p.Nodes) {
                switch (n.Op) {
                    case Op.Input:
                        var c = points[n.Point];
                        v[n] = n.Comp == 'x' ? c.x : n.Comp == 'y' ? c.y : c.w;
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
