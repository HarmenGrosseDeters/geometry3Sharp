// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Predicate compiler for the exact stages of ExactMeshArrangement on fixed-width points (docs/design/
// PREDICATE_COMPILER.md, docs/design/FIXED_WIDTH_EXACT_POINTS.md section 5). It reads integer sign predicates,
// written once as polynomial formulas over the components of FixedPoint3 points, and generates C# that evaluates
// them exactly with FixedArith (sign and magnitude, no allocation), together with a proven magnitude bound for every
// intermediate value. The bound sets the limbs of the value's buffer; the generated code passes that capacity to
// FixedArith, which throws if a result would not fit (so a wrong bound fails loudly instead of giving a wrong sign).
// This is the ipredicate part of the experiment of #221, retargeted from two's complement inputs with fitted bit
// bounds (IntegerRuntime) to FixedPoint3 / FixedInt; the double-input predicates of #221 are not part of it.
//
// Bound rules (exact integers; m = 2^255 - 1 is the bound of every component of a fixed point, docs section 4.1):
//   input component:  |v| <= m
//   x + y, x - y:     |v| <= Mx + My
//   x y:              |v| <= Mx My
// Each bound is kept as a polynomial in m with nonnegative integer coefficients (for the formulas here a single
// term k m^d), and evaluated exactly. A value with |v| <= M needs ceil(bitlength(M) / 64) limbs as a magnitude;
// sign and magnitude needs no sign bit, and FixedArith never forms -2^(64 limbs) or any other value outside
// [-M, M] in a buffer of that node.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace G3PredicateCompiler
{
    enum Op { Input, Add, Sub, Mul }

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
        /// <summary> the bound for m = the given value </summary>
        public BigInteger At(BigInteger m)
        {
            BigInteger s = 0;
            foreach (var t in Terms) s += t.Value * BigInteger.Pow(m, t.Key);
            return s;
        }
        public override string ToString() =>
            string.Join(" + ", Terms.Reverse().Select(t => (t.Value == 1 ? "" : t.Value.ToString(CultureInfo.InvariantCulture) + " ")
                + (t.Key == 1 ? "m" : "m^" + t.Key.ToString(CultureInfo.InvariantCulture)))).Replace("1 m", "m");
    }

    sealed class Node
    {
        public int Id;
        public Op Op;
        public Node A, B;
        public string Point;    // input: the point
        public char Comp;       // input: 'x' (axis i), 'y' (axis j), 'w' (component 3)
        public string Name;     // the name of a definition, if any (for comments)
        public Bound Bound;
        public BigInteger Max;  // Bound at m
        public int Bits => Max.IsZero ? 0 : (int)Max.GetBitLength();
        public int Limbs => (Bits + 63) / 64;
        public int Offset;      // in the generated scratch buffer
    }

    sealed class Predicate
    {
        public string Name;
        public string Comment;
        public List<string> Points = new List<string>();
        public List<Node> Nodes = new List<Node>();     // topological order
        public Node Root;
        public int Scratch;
    }

    public static class PredicateCompiler
    {
        /// <summary> FixedPoint3: limbs per component, and the bound m = 2^(64 Limbs - 1) - 1 of every component </summary>
        public const int ComponentLimbs = 4;
        public static readonly BigInteger M = BigInteger.Pow(2, 64 * ComponentLimbs - 1) - 1;
        // a stackalloc of more would risk the stack (the largest predicate here needs about a thousand)
        const int MaxFrameLimbs = 2048;
        static readonly HashSet<string> Reserved = new HashSet<string> { "s", "i", "j" };

        /// <summary> the generated C# file for a spec (see predicates.spec) </summary>
        public static string Compile(string spec)
        {
            var preds = Analyze(spec);
            var sb = new StringBuilder();
            sb.Append("// <auto-generated>\n");
            sb.Append("// Generated by tools/PredicateCompiler from tools/PredicateCompiler/predicates.spec. Do not edit;\n");
            sb.Append("// regenerate with: dotnet run --project tools/PredicateCompiler -- tools/PredicateCompiler/predicates.spec math/Generated/FixedPredicates.cs\n");
            sb.Append("// </auto-generated>\n");
            sb.Append("// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt\n");
            sb.Append("using System.Runtime.CompilerServices;\n\nnamespace g3\n{\n");
            sb.Append("    /// <summary>\n");
            sb.Append("    /// The exact stages of ExactMeshArrangement's predicates on fixed-width points, generated from their formulas\n");
            sb.Append("    /// (tools/PredicateCompiler). Every value has a buffer of the limbs its proven bound needs: |v| &lt;= M, the bound\n");
            sb.Append($"    /// written as a polynomial in m = 2^{64 * ComponentLimbs - 1} - 1, the bound of every component of a FixedPoint3. FixedArith\n");
            sb.Append("    /// throws if a result would not fit its buffer. No allocation.\n");
            sb.Append("    /// </summary>\n");
            sb.Append("    internal static unsafe class FixedPredicates\n    {\n");
            sb.Append($"        /// <summary> the limbs per component the bounds below were derived for </summary>\n");
            sb.Append($"        internal const int ComponentLimbs = {ComponentLimbs};\n");
            foreach (var p in preds) {
                sb.Append('\n');
                Emit(p, sb);
            }
            sb.Append("    }\n}\n");
            return sb.ToString();
        }

        /// <summary> the bound table of every predicate of a spec, as Markdown (for the design doc) </summary>
        public static string Table(string spec)
        {
            var sb = new StringBuilder();
            sb.Append("| predicate | value | bound (m = 2^255 - 1) | bits | limbs |\n|---|---|---|---|---|\n");
            foreach (var p in Analyze(spec)) {
                foreach (var n in p.Nodes.Where(n => n.Op != Op.Input))
                    sb.Append($"| `{p.Name}` | `{describe(n)}` | `{n.Bound}` | {n.Bits} | {n.Limbs} |\n");
                sb.Append($"| `{p.Name}` | scratch of one call | | | {p.Scratch} |\n");
            }
            return sb.ToString();
        }

        static List<Predicate> Analyze(string spec)
        {
            var preds = Parse(spec);
            foreach (var p in preds) {
                int off = 0;
                foreach (var n in p.Nodes) {
                    switch (n.Op) {
                        case Op.Input: n.Bound = Bound.Input(); break;
                        case Op.Add: case Op.Sub: n.Bound = n.A.Bound + n.B.Bound; break;
                        case Op.Mul: n.Bound = n.A.Bound * n.B.Bound; break;
                    }
                    n.Max = n.Bound.At(M);
                    n.Offset = off;
                    off += n.Limbs;
                }
                p.Scratch = off;
                if (p.Scratch > MaxFrameLimbs)
                    throw new NotSupportedException($"{p.Name}: {p.Scratch} limbs of scratch, at most {MaxFrameLimbs}");
            }
            return preds;
        }

        // --- parsing: "ipredicate Name(a, b, ...)" (the points), then "x = expr" lines, then "sign expr"; the inputs
        // are p.x, p.y, p.w of a point p; '#' comments

        static List<Predicate> Parse(string spec)
        {
            var result = new List<Predicate>();
            Predicate cur = null;
            Dictionary<string, Node> vars = null, cse = null;
            var comment = new StringBuilder();
            int lineNo = 0;
            foreach (var raw in spec.Replace("\r", "").Split('\n')) {
                lineNo++;
                string line = raw.Trim();
                if (line.StartsWith("#")) { comment.Append(line.Substring(1).Trim()).Append(' '); continue; }
                if (line.Length == 0) { comment.Clear(); continue; }    // only the comment block right before a predicate
                if (line.StartsWith("ipredicate ")) {
                    if (cur != null) throw new FormatException($"line {lineNo}: predicate {cur.Name} has no sign line");
                    cur = new Predicate { Comment = comment.ToString().Trim() };
                    comment.Clear();
                    string head = line.Substring(line.IndexOf(' ') + 1);
                    int open = head.IndexOf('('), close = head.IndexOf(')');
                    if (open < 0 || close < open) throw new FormatException($"line {lineNo}: expected 'ipredicate Name(points)'");
                    cur.Name = head.Substring(0, open).Trim();
                    cur.Points = head.Substring(open + 1, close - open - 1).Split(',').Select(s => s.Trim()).ToList();
                    foreach (var pt in cur.Points)
                        if (!System.Text.RegularExpressions.Regex.IsMatch(pt, "^[a-z][a-z0-9]*$") || Reserved.Contains(pt) || pt.StartsWith("v"))
                            throw new FormatException($"line {lineNo}: point name '{pt}' is not allowed");
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
                if (line.StartsWith("sign ")) {
                    cur.Root = new ExprParser(line.Substring(5), vars, input, (op, a, b) => make(pc, pcse, op, a, b, null)).Parse();
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
                var node = new ExprParser(line.Substring(eq + 1), vars, input, (op, a, b) => make(pc, pcse, op, a, b, null)).Parse();
                if (node.Name == null && node.Op != Op.Input) node.Name = v;
                vars[v] = node;
            }
            if (cur != null) throw new FormatException($"predicate {cur.Name} has no sign line");
            return result;
        }

        // structural sharing (common subexpressions are computed once); products are commutative
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

        // --- emitting

        static string inv(int v) => v.ToString(CultureInfo.InvariantCulture);
        static string val(Node n) => n.Op == Op.Input ? n.Point + "_" + n.Comp : "v" + inv(n.Id);
        static string opc(Op op) => op == Op.Add ? "+" : op == Op.Sub ? "-" : "*";
        static string describe(Node n) =>
            n.Op == Op.Input ? n.Point + "." + n.Comp
            : (n.Name != null ? n.Name + " = " : "") + $"{ref_(n.A)} {opc(n.Op)} {ref_(n.B)}";
        static string ref_(Node n) => n.Op == Op.Input ? n.Point + "." + n.Comp : n.Name ?? "v" + inv(n.Id);

        static void Emit(Predicate p, StringBuilder sb)
        {
            string I1 = "        ", I2 = I1 + "    ";
            var root = p.Root;
            foreach (var line in wrap(p.Name + ": " + p.Comment, 112))
                sb.Append($"{I1}// {line}\n");
            sb.Append($"{I1}// The sign of {ref_(root)}: |.| <= {root.Bound} < 2^{inv(root.Bits)} ({inv(root.Limbs)} limbs); widest value {inv(p.Nodes.Max(n => n.Limbs))} limbs, {inv(p.Scratch)} limbs of scratch.\n");
            sb.Append("#if NET\n");
            sb.Append($"{I1}[SkipLocalsInit]\n");
            sb.Append("#endif\n");
            sb.Append($"{I1}[MethodImpl(JitHints.Optimize)]\n");
            sb.Append($"{I1}internal static int {p.Name}({string.Join(", ", p.Points.Select(x => "ref FixedPoint3 " + x))}, int i, int j)\n{I1}{{\n");
            sb.Append($"{I2}ulong* s = stackalloc ulong[{inv(p.Scratch)}];\n");
            foreach (var n in p.Nodes) {
                string dst = $"s + {inv(n.Offset)}";
                string code;
                if (n.Op == Op.Input) {
                    string comp = n.Comp == 'x' ? "i" : n.Comp == 'y' ? "j" : "3";
                    code = $"FixedInt {val(n)} = {n.Point}.Get({comp}, {dst});";
                } else {
                    string f = n.Op == Op.Mul ? "Mul" : n.Op == Op.Add ? "Add" : "Sub";
                    code = $"FixedInt {val(n)} = FixedArith.{f}({val(n.A)}, {val(n.B)}, {dst}, {inv(n.Limbs)});";
                }
                sb.Append($"{I2}{code}".PadRight(80)).Append($" // {describe(n)}: <= {n.Bound} < 2^{inv(n.Bits)}\n");
            }
            sb.Append($"{I2}return {val(root)}.Sign;\n{I1}}}\n");
        }

        static IEnumerable<string> wrap(string text, int width)
        {
            var line = new StringBuilder();
            foreach (var w in text.Split(' ').Where(x => x.Length > 0)) {
                if (line.Length > 0 && line.Length + 1 + w.Length > width) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 0) yield return line.ToString();
        }
    }
}
