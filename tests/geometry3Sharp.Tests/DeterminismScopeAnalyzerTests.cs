using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using G3ExactCodeAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The closed world of the determinism proof scope, stages 0 and 1 (docs/design/DETERMINISM_PROOF.md, issue 859): the rules
    /// G3D0001-9 of tools/analyzers/G3ExactCodeAnalyzers/DeterminismAnalyzers.cs on small sources, the allow list
    /// DeterministicApis.txt (well formed, every member and test it names exists), the scope in .editorconfig (exactly the
    /// kernel and the stage 1 files) and the ledger of the design note, which is generated from the list.
    /// </summary>
    public class DeterminismScopeAnalyzerTests
    {
        sealed class Text : AdditionalText
        {
            readonly string text;
            public Text(string path, string text) { Path = path; this.text = text; }
            public override string Path { get; }
            public override SourceText GetText(CancellationToken ct = default) { return SourceText.From(text); }
        }

        static readonly Lazy<List<MetadataReference>> Refs = new Lazy<List<MetadataReference>>(() =>
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList());

        // the files named Lib*.cs are outside the scope (their rules off, as .editorconfig does for the library)
        sealed class ScopeOptions : SyntaxTreeOptionsProvider
        {
            public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken ct) { return GeneratedKind.NotGenerated; }
            public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken ct, out ReportDiagnostic severity)
            {
                severity = ReportDiagnostic.Suppress;
                return Path.GetFileName(tree.FilePath).StartsWith("Lib", StringComparison.Ordinal) && diagnosticId.StartsWith("G3D", StringComparison.Ordinal);
            }
            public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken ct, out ReportDiagnostic severity)
            {
                severity = ReportDiagnostic.Default;
                return false;
            }
        }

        static CSharpCompilation Compile(string source, bool net, string library = null)
        {
            var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: net ? new[] { "NET" } : new string[0]);
            var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, parse, "Kernel.cs") };
            if (library != null)
                trees.Add(CSharpSyntaxTree.ParseText(library, parse, "Lib.cs"));
            return CSharpCompilation.Create("ScopeTest", trees, Refs.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true).WithSyntaxTreeOptionsProvider(new ScopeOptions()));
        }

        static List<Diagnostic> Analyze(string source, string list, bool net = true, string library = null)
        {
            CSharpCompilation c = Compile(source, net, library);
            Assert.Empty(c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            var options = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(new Text("/repo/" + DeterministicApiList.FileName, list)));
            return c.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DeterminismScopeAnalyzer()), options)
                .GetAnalyzerDiagnosticsAsync().Result.ToList();
        }

        static List<string> Ids(List<Diagnostic> d, string rule)
        {
            return d.Where(x => x.Id == rule).Select(x => x.Properties[DeterminismScopeAnalyzer.IdProperty]).ToList();
        }

        const string Usings = "using System; using System.Numerics; using System.Collections.Generic; using System.Runtime.CompilerServices; ";

        // ---- G3D0001: every external member that the scope uses is on the list ----

        [Theory]
        [InlineData("static double M(double x) { return Math.Sqrt(x); }", "M:System.Math.Sqrt(System.Double)")]
        [InlineData("static double M() { return double.MaxValue; }", "F:System.Double.MaxValue")]
        [InlineData("static int M(int[] a) { return a.Length; }", "P:System.Array.Length")]
        [InlineData("static object M() { return new List<int>(); }", "M:System.Collections.Generic.List`1.#ctor")]
        [InlineData("static int M(List<long> l) { return l.Count; }", "P:System.Collections.Generic.List`1.Count")]
        [InlineData("static BigInteger M(BigInteger a) { return a + a; }", "M:System.Numerics.BigInteger.op_Addition(System.Numerics.BigInteger,System.Numerics.BigInteger)")]
        [InlineData("static BigInteger M(long a) { return a; }", "M:System.Numerics.BigInteger.op_Implicit(System.Int64)~System.Numerics.BigInteger")]
        [InlineData("static double M(BigInteger a) { return (double)a; }", "M:System.Numerics.BigInteger.op_Explicit(System.Numerics.BigInteger)~System.Double")]
        [InlineData("static BigInteger M(BigInteger a) { a += 1; return a; }", "M:System.Numerics.BigInteger.op_Addition(System.Numerics.BigInteger,System.Numerics.BigInteger)")]
        [InlineData("static BigInteger M(BigInteger a) { a++; return a; }", "M:System.Numerics.BigInteger.op_Increment(System.Numerics.BigInteger)")]
        [InlineData("static BigInteger M(BigInteger a) { return -a; }", "M:System.Numerics.BigInteger.op_UnaryNegation(System.Numerics.BigInteger)")]
        [InlineData("[MethodImpl(MethodImplOptions.NoInlining)] static void M() { }", "M:System.Runtime.CompilerServices.MethodImplAttribute.#ctor(System.Runtime.CompilerServices.MethodImplOptions)")]
        [InlineData("[MethodImpl(MethodImplOptions.NoInlining)] static void M() { }", "F:System.Runtime.CompilerServices.MethodImplOptions.NoInlining")]
        [InlineData("[ThreadStatic] static int f;", "M:System.ThreadStaticAttribute.#ctor")]
        [InlineData("static object o = new object(); static void M() { lock (o) { } }", "M:System.Threading.Monitor.Enter(System.Object,System.Boolean@)")]
        [InlineData("static int M(List<int> l) { int s = 0; foreach (int x in l) s += x; return s; }", "M:System.Collections.Generic.List`1.GetEnumerator")]
        [InlineData("static int M(Dictionary<int, int> d) { int s = 0; foreach (var kv in d) s += kv.Key; return s; }", "M:System.Collections.Generic.Dictionary`2.GetEnumerator")]
        [InlineData("static int M(Func<int> f) { return f(); }", "M:System.Func`1.Invoke")]
        [InlineData("static int M() { Func<double, int> f = x => Math.Sign(x); return f(1); }", "M:System.Math.Sign(System.Double)")]
        [InlineData("static int M() { return L(2); int L(int x) { return Math.Max(x, 1); } }", "M:System.Math.Max(System.Int32,System.Int32)")]
        [InlineData("static long M(double x) { return BitConverter.DoubleToInt64Bits(x); }", "M:System.BitConverter.DoubleToInt64Bits(System.Double)")]
        [InlineData("static string M(int x) { return x.ToString(); }", "M:System.Int32.ToString")]
        [InlineData("static int M() { return Environment.ProcessorCount; }", "P:System.Environment.ProcessorCount")]
        [InlineData("static double M() { return new Random(1).NextDouble(); }", "M:System.Random.#ctor(System.Int32)")]
        [InlineData("static void M(IDisposable d) { using (d) { } }", "M:System.IDisposable.Dispose")]
        // constructs that the compiler lowers to framework calls (String.Concat, String.Format, ValueTuple, ...)
        [InlineData("static string M(int x) { return \"x = \" + x; }", "construct:string-concatenation")]
        [InlineData("static string M(string a) { a += \"!\"; return a; }", "construct:string-concatenation")]
        [InlineData("static string M(double x) { return $\"{x}\"; }", "construct:interpolated-string")]
        // (a tuple literal of up to 7 elements is the constructor of its ValueTuple type; a longer one stays a pseudo id)
        [InlineData("static object M(int a) { return (a, 2); }", "M:System.ValueTuple`2.#ctor(`0,`1)")]
        [InlineData("static object M(int a) { return (a, 2, 3); }", "M:System.ValueTuple`3.#ctor(`0,`1,`2)")]
        [InlineData("static int M((int x, int y) t) { return t.y; }", "F:System.ValueTuple`2.Item2")]
        [InlineData("static int M(int a) { var (x, y) = (a, 2); return x + y; }", "M:System.ValueTuple`2.#ctor(`0,`1)")]
        [InlineData("static object M(int a) { return (a, 2, 3, 4, 5, 6, 7, 8); }", "construct:tuple")]
        [InlineData("static object M(int a) { return new { a }; }", "construct:anonymous-type")]
        [InlineData("static async System.Threading.Tasks.Task M(System.Threading.Tasks.Task t) { await t; }", "construct:await")]
        [InlineData("static int[] M(int[] a) { return a[1..]; }", "construct:range")]
        [InlineData("static bool M(string a, string b) { return a == b; }", "construct:string-equality")]
        [InlineData("static decimal M(decimal a) { return a + 1; }", "construct:decimal")]
        [InlineData("static double M(decimal a) { return (double)a; }", "construct:decimal")]
        [InlineData("static Action M(Action a, Action b) { return a + b; }", "construct:delegate-combination")]
        [InlineData("static T M<T>() where T : new() { return new T(); }", "construct:new-type-parameter")]
        [InlineData("static void M(IDisposable d) { using var x = d; }", "M:System.IDisposable.Dispose")]
        [InlineData("static int M(List<int> l) { return l is [1, ..] ? 1 : 0; }", "P:System.Collections.Generic.List`1.Count")]
        [InlineData("static int M(List<int> l) { return l is [1, .. var r] ? 1 : 0; }", "construct:slice-pattern")]
        [InlineData("static int M(KeyValuePair<int, int> p) { return p is (1, var v) ? v : 0; }", "M:System.Collections.Generic.KeyValuePair`2.Deconstruct(`0@,`1@)")]
        [InlineData("static Func<double, double> M() { return Math.Sqrt; }", "M:System.Math.Sqrt(System.Double)")]
        [InlineData("static void M(AppDomain d) { d.ProcessExit += null; }", "E:System.AppDomain.ProcessExit")]
        [InlineData("static double M(BigInteger[] a) { double s = 0; foreach (double d in a) s += d; return s; }", "M:System.Numerics.BigInteger.op_Explicit(System.Numerics.BigInteger)~System.Double")]
        [InlineData("static int[] M() { int[] a = [1, 2]; return a; }", "construct:unmodelled-CollectionExpression")]
        [InlineData("static unsafe int M(Span<int> s) { fixed (int* p = s) return p[0]; }", "construct:unmodelled-FixedStatement")]
        // (inside the exempted pointer constructs, what they evaluate is still checked)
        [InlineData("static unsafe ulong M() { ulong* p = stackalloc ulong[Environment.ProcessorCount]; return p[0]; }", "P:System.Environment.ProcessorCount")]
        [InlineData("static unsafe ulong M(int i) { ulong* p = stackalloc ulong[4]; return p[Math.Abs(i)]; }", "M:System.Math.Abs(System.Int32)")]
        [InlineData("static unsafe int M() { fixed (int* q = Array.Empty<int>()) return q == null ? 0 : 1; }", "M:System.Array.Empty``1")]
        // (the target is chosen at run time)
        [InlineData("delegate double D(double x); static double M(D f) { return f(1); }", "construct:delegate-invocation")]
        [InlineData("static unsafe double M(delegate*<double, double> f) { return f(1); }", "construct:function-pointer-call")]
        public void UnlistedExternalMember_IsReported(string member, string id)
        {
            var d = Analyze(Usings + "static class C { " + member + " }", "");
            Assert.Contains(id, Ids(d, DeterminismScopeAnalyzer.ApiId));
            Assert.All(d.Where(x => x.Id == DeterminismScopeAnalyzer.ApiId), x => Assert.Equal("Kernel.cs", x.Location.SourceTree.FilePath));
        }

        [Fact]
        public void ListedMembers_AndOwnMembers_AndBuiltInOperators_AreNotReported()
        {
            string source = Usings + @"static class C {
                static double Own(double x) { return x * 2 + 1; }
                static int[] a = new int[3];
                static double M(double x, long k) { int i = (int)k; return Own(x) / 3 - a[i] + Math.Sqrt(x) + i % 2 + (k << 2); }
            }";
            var d = Analyze(source, "M:System.Math.Sqrt(System.Double) ; IEEE 754 squareRoot");
            Assert.Empty(d);
        }

        // stackalloc, pointer element access, fixed-size buffers and fixed over arrays call nothing
        [Fact]
        public void PointerConstructs_UseNoMember()
        {
            string source = Usings + @"unsafe struct B { public fixed ulong L[4]; } sealed class H { public B b; }
                static unsafe class C { static ulong M(int[] a, H h, B b) {
                    ulong* p = stackalloc ulong[4]; p[0] = 1; ulong s = p[0] + b.L[1];
                    fixed (int* q = a) s += (ulong)q[0];
                    fixed (ulong* r = h.b.L) s += r[2];
                    int x = 3; int* y = &x; return s + (ulong)*y; } }";
            Assert.Empty(Analyze(source, ""));
        }

        // members of this assembly declared outside the scope's files are external: the kernel inherits what they compute
        [Fact]
        public void OwnMembersOutsideTheScope_AreExternal()
        {
            const string library = "public struct V { public double X; public static V operator +(V a, V b) { a.X += b.X; return a; } public double Len => System.Math.Sqrt(X); public V(double x) { X = x; } }";
            string source = "static class C { static double M(V a) { V b = new V(2); return (a + b).Len + a.X; } static double Own(double x) { return x; } static double N() { return Own(1); } }";
            var d = Analyze(source, "", library: library);
            Assert.Equal(new[] { "F:V.X", "M:V.#ctor(System.Double)", "M:V.op_Addition(V,V)", "P:V.Len" }, Ids(d, DeterminismScopeAnalyzer.ApiId).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            // the library file itself is outside the scope: its Math.Sqrt is not reported
            Assert.All(d, x => Assert.Equal("Kernel.cs", x.Location.SourceTree.FilePath));
        }

        // a member of this assembly outside the scope is pinned to the declaration that was reviewed: [body:h]
        [Fact]
        public void OwnMembersOutsideTheScope_ArePinnedToTheirReviewedDeclaration()
        {
            const string library = "public struct V { public double X; public static V operator +(V a, V b) { a.X += b.X; return a; } }";
            const string source = "static class C { static V M(V a) { return a + a; } }";
            ISymbol op = DocumentationCommentId.GetFirstSymbolForDeclarationId("M:V.op_Addition(V,V)", Compile(source, true, library));
            string h = DeterminismScopeAnalyzer.BodyHash(op);
            Assert.Matches("^[0-9a-f]{16}$", h);
            // (the operator reads X: by the closure rule X is listed and pinned too)
            string x = "\nF:V.X ; [body:" + DeterminismScopeAnalyzer.BodyHash(DocumentationCommentId.GetFirstSymbolForDeclarationId("F:V.X", Compile(source, true, library))) + "] storage";
            Func<string, List<Diagnostic>> unused = list => Analyze(source, list + x, library: library).Where(d => d.Id == DeterminismScopeAnalyzer.UnusedId).ToList();
            // without a tag, and with a wrong one: reported, with the hash to pin
            Assert.Contains(unused("M:V.op_Addition(V,V) ; componentwise +"), x => x.GetMessage().Contains("[body:" + h + "]"));
            Assert.Contains(unused("M:V.op_Addition(V,V) ; [body:0000000000000000] componentwise +"), x => x.GetMessage().Contains("changed since its review"));
            Assert.Empty(unused("M:V.op_Addition(V,V) ; [body:" + h + "] componentwise +"));
            // a re-indent keeps the hash, a changed body does not (and a BCL entry needs no tag)
            // (white space within a line; a changed line structure is a change, BodyHash_CanonicalText_AndAKnownAnswer)
            string reindented = library.Replace("{ a.X += b.X; return a; }", "{   a.X +=\tb.X;  return a; }");
            Assert.Equal(h, DeterminismScopeAnalyzer.BodyHash(DocumentationCommentId.GetFirstSymbolForDeclarationId("M:V.op_Addition(V,V)", Compile(source, true, reindented))));
            string changed = library.Replace("a.X += b.X", "a.X -= b.X");
            Assert.NotEqual(h, DeterminismScopeAnalyzer.BodyHash(DocumentationCommentId.GetFirstSymbolForDeclarationId("M:V.op_Addition(V,V)", Compile(source, true, changed))));
            Assert.Contains(Analyze(source, "M:V.op_Addition(V,V) ; [body:" + h + "] componentwise +" + x, library: changed),
                x => x.Id == DeterminismScopeAnalyzer.UnusedId && x.GetMessage().Contains("changed since its review"));
            Assert.Empty(Analyze(Usings + "static class C { static double M(double x) { return Math.Sqrt(x); } }", "M:System.Math.Sqrt(System.Double) ; IEEE"));
        }

        static ISymbol Lib(string library, string id)
        {
            return DocumentationCommentId.GetFirstSymbolForDeclarationId(id, Compile("static class C { }", true, library));
        }

        // the canonical text: tokens verbatim, a line break as one line break, indentation and spacing as one space
        [Fact]
        public void BodyHash_CanonicalText_AndAKnownAnswer()
        {
            const string op = "public struct V { public double X; public static V operator +(V a, V b) {\r\n        a.X  +=  b.X; // sum\r\n        return a;\r\n    } }";
            SyntaxNode decl = Lib(op, "M:V.op_Addition(V,V)").DeclaringSyntaxReferences[0].GetSyntax();
            Assert.Equal("public static V operator +(V a, V b) {\na.X += b.X; // sum\nreturn a;\n}", DeterminismScopeAnalyzer.Canonical(decl));
            // (FNV-1a 64 of that text, computed independently in Python: the hash is the same on every runtime and its algorithm is pinned)
            Assert.Equal("c5e9b5f906a3d688", DeterminismScopeAnalyzer.BodyHash(Lib(op, "M:V.op_Addition(V,V)")));
            // LF checkout and another indentation: the same; a joined line (the comment now swallows the statement): not
            Assert.Equal(DeterminismScopeAnalyzer.BodyHash(Lib(op, "M:V.op_Addition(V,V)")),
                DeterminismScopeAnalyzer.BodyHash(Lib(op.Replace("\r\n", "\n").Replace("        ", "\t"), "M:V.op_Addition(V,V)")));
            Assert.NotEqual(DeterminismScopeAnalyzer.BodyHash(Lib(op, "M:V.op_Addition(V,V)")),
                DeterminismScopeAnalyzer.BodyHash(Lib(op.Replace("// sum\r\n        return a;", "// sum return a;\r\n return a;"), "M:V.op_Addition(V,V)")));
            // spaces inside a string literal count; spaces between tokens do not, but a missing one between two tokens does
            const string str = "public struct S { public override string ToString() { return string.Format(\"{0} {1}\", 1, 2); } }";
            Assert.NotEqual(DeterminismScopeAnalyzer.BodyHash(Lib(str, "M:S.ToString")), DeterminismScopeAnalyzer.BodyHash(Lib(str.Replace("{0} {1}", "{0}  {1}"), "M:S.ToString")));
            const string neg = "public static class N { public static int F(int x, int y) { return x - -y; } }";
            Assert.Contains("return x - -y;", DeterminismScopeAnalyzer.Canonical(Lib(neg, "M:N.F(System.Int32,System.Int32)").DeclaringSyntaxReferences[0].GetSyntax()));
        }

        // a field is pinned with its whole field declaration (all its declarators, its type and initializer)
        [Fact]
        public void BodyHash_FieldsTakeTheirWholeDeclaration()
        {
            const string f = "public struct P { public double X, Y; }";
            Assert.Equal(DeterminismScopeAnalyzer.BodyHash(Lib(f, "F:P.X")), DeterminismScopeAnalyzer.BodyHash(Lib(f, "F:P.Y")));
            Assert.NotEqual(DeterminismScopeAnalyzer.BodyHash(Lib(f, "F:P.X")), DeterminismScopeAnalyzer.BodyHash(Lib(f.Replace("X, Y", "X, Y2"), "F:P.X")));
            Assert.NotEqual(DeterminismScopeAnalyzer.BodyHash(Lib(f, "F:P.X")), DeterminismScopeAnalyzer.BodyHash(Lib(f.Replace("double", "float"), "F:P.X")));
        }

        // closure: a pinned declaration may use members of this assembly outside the scope only if they are listed too
        [Fact]
        public void PinnedDeclarations_UseOnlyListedMembers()
        {
            const string library = "public struct V { public double X; public static double Twice(double a) { return a * 2; } public double T() { return Twice(X); } }";
            const string source = "static class C { static double M(V a) { return a.T(); } }";
            Func<string, string> pin = id => "[body:" + DeterminismScopeAnalyzer.BodyHash(Lib(library, id)) + "]";
            string list = "M:V.T ; " + pin("M:V.T") + " calls Twice";
            var d = Analyze(source, list, library: library).Where(x => x.Id == DeterminismScopeAnalyzer.UnusedId).ToList();
            Assert.Contains(d, x => x.GetMessage().Contains("'M:V.Twice(System.Double)', which is not on the list"));
            Assert.Contains(d, x => x.GetMessage().Contains("'F:V.X', which is not on the list"));
            // listed and pinned: clean, and counted as used (the scope does not call Twice itself)
            list += "\nM:V.Twice(System.Double) ; " + pin("M:V.Twice(System.Double)") + " a * 2\nF:V.X ; " + pin("F:V.X") + " storage";
            Assert.Empty(Analyze(source, list, library: library));
        }

        // the closure sees what the operations use beyond the symbols of the expressions: a user-defined implicit
        // conversion, the Deconstruct of a positional pattern; and reports each member once
        [Fact]
        public void PinnedDeclarations_Closure_CoversImplicitConversionsAndPatterns()
        {
            const string library = @"public struct W { public double Y;
                public static implicit operator V(W w) { V v; v.X = w.Y; return v; }
                public void Deconstruct(out double a, out double b) { a = Y; b = Y; } }
            public struct V { public double X;
                public double T(W w) { V v = w; if (w is (var p, var q)) return v.X + X + p + q; return w.Y + w.Y; } }";
            const string source = "static class C { static double M(V a, W w) { return a.T(w); } }";
            string list = "M:V.T(W) ; [body:" + DeterminismScopeAnalyzer.BodyHash(Lib(library, "M:V.T(W)")) + "] x\nF:V.X ; [body:"
                + DeterminismScopeAnalyzer.BodyHash(Lib(library, "F:V.X")) + "] storage";
            var d = Analyze(source, list, library: library).Where(x => x.Id == DeterminismScopeAnalyzer.UnusedId).Select(x => x.GetMessage()).ToList();
            Assert.Single(d, x => x.Contains("'M:W.op_Implicit(W)~V', which is not on the list"));
            Assert.Single(d, x => x.Contains("'M:W.Deconstruct(System.Double@,System.Double@)', which is not on the list"));
            Assert.Single(d, x => x.Contains("'F:W.Y', which is not on the list"));
            Assert.Equal(3, d.Count);
        }

        // an #if in a pinned declaration (around a leading attribute, and in the body): the same text and hash whichever
        // branch the target compiles; a change in either branch changes it on both targets
        [Fact]
        public void BodyHash_TheSameWhicheverBranchTheTargetCompiles()
        {
            const string library = @"public struct V { public double X;
#if NET
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
            public double T() {
#if NET
                return X  *  2; /* net
                     branch */
#else
                return X + X; // portable
#endif
            } }";
            Func<string, bool, ISymbol> t = (lib, net) => DocumentationCommentId.GetFirstSymbolForDeclarationId("M:V.T", Compile("static class C { }", net, lib));
            string canonical = DeterminismScopeAnalyzer.Canonical(t(library, true).DeclaringSyntaxReferences[0].GetSyntax());
            Assert.Equal(canonical, DeterminismScopeAnalyzer.Canonical(t(library, false).DeclaringSyntaxReferences[0].GetSyntax()));
            Assert.StartsWith("#if NET\n[System.Runtime", canonical);
            Assert.Contains("return X * 2; /* net\n                     branch */\n#else\nreturn X + X; // portable\n#endif", canonical);
            string h = DeterminismScopeAnalyzer.BodyHash(t(library, true));
            Assert.Equal(h, DeterminismScopeAnalyzer.BodyHash(t(library, false)));
            foreach (string changed in new[] { library.Replace("X + X", "X - X"), library.Replace("X  *  2", "X * 3"), library.Replace("AggressiveInlining", "NoInlining") })
                foreach (bool net in new[] { true, false })
                    Assert.NotEqual(h, DeterminismScopeAnalyzer.BodyHash(t(changed, net)));
        }

        [Fact]
        public void BodyTags_OnlyOnTheLibrarysMembers_AndOnce()
        {
            string source = Usings + "static class C { static double M(double x) { return Math.Sqrt(x); } }";
            Assert.Contains(Analyze(source, "M:System.Math.Sqrt(System.Double) ; [body:0123456789abcdef] IEEE"), x => x.Id == DeterminismScopeAnalyzer.UnusedId && x.GetMessage().Contains("pins declarations of this assembly only"));
            Assert.Contains(Analyze(source, "M:System.Math.Sqrt(System.Double) ; [body:0123456789abcdef][body:0123456789abcdee] IEEE"), x => x.Id == DeterminismScopeAnalyzer.UnusedId && x.GetMessage().Contains("two [body:] tags"));
            Assert.Contains(Analyze(source, "M:System.Math.Sqrt(System.Double) ; [body:0123456789ABCDEF] IEEE"), x => x.Id == DeterminismScopeAnalyzer.UnusedId && x.GetMessage().Contains("unknown tag"));
        }

        // arrays are lowered to an index loop: no enumerator member is used
        [Fact]
        public void ForEachOverAnArray_UsesNoMember()
        {
            Assert.Empty(Analyze(Usings + "static class C { static int M(int[] a) { int s = 0; foreach (int x in a) s += x; return s; } }", ""));
        }

        // ---- G3D0002: restricted entries ----

        [Fact]
        public void SitesEntry_NeedsTheMemberRegistered()
        {
            string source = Usings + "static class C { static double A(double x) { return Math.Min(x, 1); } static double B(double x) { return Math.Min(x, 2); } }";
            string entry = "M:System.Math.Min(System.Double,System.Double) ; [sites] min of zeros differs";
            var d = Analyze(source, entry);
            Assert.Equal(new[] { "M:C.A(System.Double)", "M:C.B(System.Double)" },
                d.Where(x => x.Id == DeterminismScopeAnalyzer.RestrictedId).Select(x => x.Properties[DeterminismScopeAnalyzer.MemberProperty]).OrderBy(x => x));
            d = Analyze(source, entry + "\nsite M:System.Math.Min(System.Double,System.Double) @ M:C.A(System.Double) ; compared only");
            Assert.Equal(new[] { "M:C.B(System.Double)" }, d.Where(x => x.Id == DeterminismScopeAnalyzer.RestrictedId).Select(x => x.Properties[DeterminismScopeAnalyzer.MemberProperty]));
            // a site of another entry does not count
            d = Analyze(source, entry + "\nM:System.Math.Max(System.Double,System.Double) ; [sites] x\nsite M:System.Math.Max(System.Double,System.Double) @ M:C.A(System.Double) ; x\nsite M:System.Math.Max(System.Double,System.Double) @ M:C.B(System.Double) ; x");
            Assert.Equal(2, d.Count(x => x.Id == DeterminismScopeAnalyzer.RestrictedId));
        }

        [Fact]
        public void HashValue_OnlyInsideAGetHashCodeImplementation()
        {
            string list = "M:System.Numerics.BigInteger.GetHashCode ; [hash] seeded per process";
            string ok = Usings + @"struct P { public BigInteger X; public override int GetHashCode() { int h = X.GetHashCode(); return h * 31; } }
                sealed class K : IEqualityComparer<BigInteger> { public bool Equals(BigInteger a, BigInteger b) { return a == b; } public int GetHashCode(BigInteger k) { return k.GetHashCode() * 7; } }";
            Assert.Empty(Analyze(ok, list + "\nM:System.Numerics.BigInteger.op_Equality(System.Numerics.BigInteger,System.Numerics.BigInteger) ; x"));
            // into an order, a result or an index: reported, also the hash of the scope's own type (its dispatch is not followed)
            foreach (string bad in new[] {
                "static int M(BigInteger x) { return x.GetHashCode() % 8; }",
                "static int M(P p) { return p.GetHashCode(); }",
                "static int M(object o) { return o.GetHashCode(); }",
                "static int M(P a, P b) { return a.GetHashCode().CompareTo(b.GetHashCode()); }",
                // (a method named GetHashCode that is not an override or a comparer's implementation)
                "static int GetHashCode(BigInteger x) { return x.GetHashCode(); }",
                "} class Q { public BigInteger X; public new int GetHashCode() { return X.GetHashCode(); }" }) {
                var d = Analyze(ok + " static class C { " + bad + " }", list);
                Assert.True(d.Any(x => x.Id == DeterminismScopeAnalyzer.RestrictedId), bad);
            }
        }

        // ---- G3D0005: conditional compilation ----

        const string IfSource = @"static class C {
            static int A(ulong x) {
#if NET
                return 1;
#else
                return 2;
#endif
            }
#if NET
            [System.Runtime.CompilerServices.SkipLocalsInit]
#endif
            static int B() { return 0; }
        }";

        [Fact]
        public void ConditionalBlocks_MustBeRegistered_WithTheirCount()
        {
            const string skip = "M:System.Runtime.CompilerServices.SkipLocalsInitAttribute.#ctor ; [NET] x\nskiplocalsinit M:C.B ; x\n";
            foreach (bool net in new[] { true, false }) {
                var d = Analyze(IfSource, skip, net);
                Assert.Equal(new[] { "M:C.A(System.UInt64)", "M:C.B" }, Ids(d, DeterminismScopeAnalyzer.ConditionalId).OrderBy(x => x));
                d = Analyze(IfSource, skip + "if M:C.A(System.UInt64) ; 1 ; T.A ; x\nif M:C.B ; 1 ; T.B ; x", net);
                Assert.Empty(d.Where(x => x.Id == DeterminismScopeAnalyzer.ConditionalId));
                Assert.Empty(d.Where(x => x.Id == DeterminismScopeAnalyzer.UnusedId));
                // a second block in a registered member
                d = Analyze(IfSource.Replace("static int B() { return 0; }", "static int B() {\n#if NET\n return 0;\n#else\n return 1;\n#endif\n}"),
                    skip + "if M:C.A(System.UInt64) ; 1 ; T.A ; x\nif M:C.B ; 1 ; T.B ; x", net);
                Assert.Equal(2, d.Count(x => x.Id == DeterminismScopeAnalyzer.ConditionalId));
                Assert.All(d.Where(x => x.Id == DeterminismScopeAnalyzer.ConditionalId), x => Assert.Equal("M:C.B", x.Properties[DeterminismScopeAnalyzer.IdProperty]));
            }
        }

        [Fact]
        public void ElifBlocks_AreCounted()
        {
            string source = "static class C { static int A() {\n#if NET\n return 1;\n#elif DEBUG\n return 2;\n#else\n return 3;\n#endif\n} }";
            Assert.Equal("2", Analyze(source, "").First(x => x.Id == DeterminismScopeAnalyzer.ConditionalId).Properties[DeterminismScopeAnalyzer.CountProperty]);
            Assert.Empty(Analyze(source, "if M:C.A ; 2 ; T.A ; x"));
        }

        // an #if around whole members belongs to the first member after the block, and an #if around an attribute to its
        // member: the same on every target, although the disabled members do not exist on one of them
        [Fact]
        public void ConditionalBlocks_BelongToTheSameMember_OnEveryTarget()
        {
            const string source = @"static class C {
#if NET
            static int Helper() { return 1; }
#endif
            static int A() { return 0; }
#if NET
            [System.Runtime.CompilerServices.SkipLocalsInit]
#endif
            static int B() { return 0; }
        }";
            const string list = "M:System.Runtime.CompilerServices.SkipLocalsInitAttribute.#ctor ; [NET] x\nskiplocalsinit M:C.B ; x\n";
            foreach (bool net in new[] { true, false })
                Assert.Equal(new[] { "M:C.A", "M:C.B" }, Ids(Analyze(source, list, net), DeterminismScopeAnalyzer.ConditionalId).OrderBy(x => x, StringComparer.Ordinal));
        }

        // whole members under an #if hold no block (they do not exist on every target): two such blocks in a row, and an
        // #if inside a conditional member, belong to the member after them; an #if in a body stays with its member
        [Fact]
        public void ConditionalBlocks_InsideConditionalMembers_BelongToTheMemberAfterThem()
        {
            const string source = @"static class C {
#if NET
            static int A() { return 1; }
#endif
#if NET
            static int B() { return 2; }
#endif
            static int D() { return 0; }
#if NET
            static int H() {
#if DEBUG
                return 3;
#else
                return 4;
#endif
            }
#endif
            static int M() {
#if NET
                return 5;
#endif
                return 6;
            }
            static int Z() { return 0; }
        }";
            foreach (bool net in new[] { true, false }) {
                var d = Analyze(source, "", net).Where(x => x.Id == DeterminismScopeAnalyzer.ConditionalId).ToList();
                Assert.Equal(2, d.Count(x => x.GetMessage().Contains("'M:C.D'")));
                Assert.Equal(3, d.Count(x => x.GetMessage().Contains("'M:C.M'")));
                Assert.Equal(5, d.Count);
            }
        }

        // ---- G3D0007: directives that switch rules off or change what is compiled ----

        [Theory]
        [InlineData("#pragma warning disable G3D0001")]
        [InlineData("#pragma warning disable CS0162, G3D0006")]
        [InlineData("#pragma warning disable")]
        [InlineData("#pragma warning disable RS0030")]
        [InlineData("#pragma warning disable G3E0003")]
        [InlineData("#define EXTRA")]
        [InlineData("#undef NET")]
        [InlineData("#line 1 \"other.cs\"")]
        [InlineData("#line (1, 1) - (1, 10) \"other.cs\"")]
        public void Directive_IsReported(string directive)
        {
            string source = directive + "\nstatic class C { static double M(double x) { return Math.Sqrt(x); } }";
            if (directive.StartsWith("#define") || directive.StartsWith("#undef"))
                source = directive + "\nusing System;\nstatic class C { static double M(double x) { return Math.Sqrt(x); } }";
            else
                source = "using System;\n" + source;
            var d = Analyze(source, "M:System.Math.Sqrt(System.Double) ; IEEE");
            Assert.Single(d, x => x.Id == DeterminismScopeAnalyzer.DirectiveId);
            // and once more at the end, at the start of the file, where no pragma of the file reaches
            Assert.Contains(d, x => x.Id == DeterminismScopeAnalyzer.UnusedId && x.Location.SourceSpan.Start == 0);
        }

        [Fact]
        public void PragmaAtTheStartOfTheFile_DoesNotSuppressTheEndReport()
        {
            string source = "#pragma warning disable G3D0007, G3D0009, G3D0001\nusing System;\nstatic class C { static double M(double x) { return Math.Floor(x); } }";
            CSharpCompilation c = Compile(source, true);
            var options = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(new Text("/repo/" + DeterministicApiList.FileName, "")));
            var d = c.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DeterminismScopeAnalyzer()), options)
                .GetAnalyzerDiagnosticsAsync().Result;
            // (the pragma silences G3D0001 and G3D0007 after it, but not the end report at offset 0)
            Assert.Contains(d, x => x.Id == DeterminismScopeAnalyzer.UnusedId && x.Location.SourceSpan.Start == 0 && !x.IsSuppressed);
        }

        // a file marked as generated stays in the scope
        [Fact]
        public void GeneratedCode_IsAnalyzed()
        {
            var d = Analyze("// <auto-generated/>\nusing System;\nstatic class C { static double M(double x) { return Math.Floor(x); } }", "");
            Assert.Contains("M:System.Math.Floor(System.Double)", Ids(d, DeterminismScopeAnalyzer.ApiId));
        }

        [Fact]
        public void HarmlessPragmas_AreNotReported()
        {
            string source = "using System;\n#pragma warning disable CS0162\n#pragma warning restore G3D0001\nstatic class C { static double M(double x) { return Math.Sqrt(x); } }";
            Assert.Empty(Analyze(source, "M:System.Math.Sqrt(System.Double) ; IEEE"));
        }

        // ---- G3D0006: double -> integer conversions ----

        [Theory]
        [InlineData("static int M(double x) { return (int)x; }")]
        [InlineData("static long M(double x) { return checked((long)x); }")]
        [InlineData("static ulong M(double x) { return (ulong)(x * 2); }")]
        [InlineData("static byte M(double x) { return (byte)x; }")]
        [InlineData("static System.DayOfWeek M(double x) { return (System.DayOfWeek)x; }")]
        [InlineData("static int P { get { double x = 2.5; return (int)x; } }")]
        [InlineData("static int? M(double x) { return (int?)x; }")]
        [InlineData("static int M(double? x) { return (int)x; }")]
        [InlineData("static long M(double[] a) { long s = 0; foreach (long i in a) s += i; return s; }")]
        public void DoubleToIntegerCast_IsReported(string member)
        {
            var d = Analyze(Usings + "static class C { " + member + " }", "");
            Assert.Single(d, x => x.Id == DeterminismScopeAnalyzer.CastId);
        }

        [Fact]
        public void OtherConversions_AreNotCasts()
        {
            // (a constant conversion is evaluated by the compiler)
            var d = Analyze(Usings + "static class C { const double H = 2.5; static double M(int i, long k, double x) { long a = i; double b = k; int c = (int)k; uint u = (uint)i; int e = (int)H + (int)1.5; return a + b + c + u + e + (double)x; } }", "");
            Assert.Empty(d.Where(x => x.Id == DeterminismScopeAnalyzer.CastId));
        }

        [Fact]
        public void Casts_MustBeRegistered_WithTheirCount()
        {
            string source = Usings + "static class C { static long M(double x, double y) { if (!(Math.Abs(x) < 1e18)) return 0; return (long)x + (long)y; } }";
            const string abs = "M:System.Math.Abs(System.Double) ; exact\n";
            Assert.Equal(2, Analyze(source, abs).Count(x => x.Id == DeterminismScopeAnalyzer.CastId));
            Assert.Empty(Analyze(source, abs + "cast M:C.M(System.Double,System.Double) ; 2 ; guarded"));
            var d = Analyze(source, abs + "cast M:C.M(System.Double,System.Double) ; 1 ; guarded");
            Assert.Equal(2, d.Count);
            Assert.All(d, x => { Assert.Equal(DeterminismScopeAnalyzer.CastId, x.Id); Assert.Equal("2", x.Properties[DeterminismScopeAnalyzer.CountProperty]); });
        }

        // ---- stage 1: the type scope, tuples, counted constructs, argument forms, assumed members ----

        [Fact]
        public void TypeScope_RestrictsAScopeFileToItsTypes()
        {
            string source = Usings + "static class A { static double M(double x) { return Math.Sqrt(x); } } static class B { static double M(double x) { return Math.Abs(x); } "
                + "\n#if NET\n static int N() { return 1; }\n#endif\n }";
            string sqrt = "M:System.Math.Sqrt(System.Double) ; sqrt\n";
            // without a 'type' line the whole file is in the scope: Abs in B is unlisted, and so is the #if block of B
            var d = Analyze(source, sqrt);
            Assert.Equal(new[] { "M:System.Math.Abs(System.Double)" }, Ids(d, DeterminismScopeAnalyzer.ApiId));
            Assert.Single(Ids(d, DeterminismScopeAnalyzer.ConditionalId));
            // with it only A is: B's calls and blocks are not looked at
            Assert.Empty(Analyze(source, sqrt + "type Kernel.cs ; A"));
            // A's own unlisted call is still reported
            Assert.Equal(new[] { "M:System.Math.Sqrt(System.Double)" }, Ids(Analyze(source, "type Kernel.cs ; A"), DeterminismScopeAnalyzer.ApiId));
            // a type that is not in the file is an unused line (the scope would silently be empty)
            Assert.Contains(Analyze(source, sqrt + "type Kernel.cs ; Missing"), x => x.Id == DeterminismScopeAnalyzer.UnusedId);
            // nested types belong to their type
            string nested = Usings + "static class A { static class N { static double M(double x) { return Math.Sqrt(x); } } } static class B { static double M(double x) { return Math.Abs(x); } }";
            Assert.Equal(new[] { "M:System.Math.Sqrt(System.Double)" }, Ids(Analyze(nested, "type Kernel.cs ; A"), DeterminismScopeAnalyzer.ApiId));
        }

        [Fact]
        public void TypeScope_ForbiddenDirectives_ReachIntoTheType_AndAreReportedInTheWholeFile()
        {
            // a pragma before the type switches off the rules inside it: reported although it lies outside the type
            string source = "#pragma warning disable G3D0001\n" + Usings + "static class B { } static class A { static double M(double x) { return Math.Sqrt(x); } }";
            Assert.Contains(Analyze(source, "type Kernel.cs ; A"), x => x.Id == DeterminismScopeAnalyzer.DirectiveId);
        }

        [Fact]
        public void TypeScope_MembersOutsideTheType_AreExternal_AndMayBeAssumed()
        {
            string source = Usings + "static class A { static int M() { return B.F(2); } } static class B { internal static int F(int x) { return x + 1; } }";
            string list = "type Kernel.cs ; A\n";
            Assert.Equal(new[] { "M:B.F(System.Int32)" }, Ids(Analyze(source, list), DeterminismScopeAnalyzer.ApiId));
            // assumed: on the list without a pinned body (a later stage owns it)
            Assert.Empty(Analyze(source, list + "M:B.F(System.Int32) ; [assumed] stage 2 obligation S2-0"));
            // pinned: its declaration is hashed, as for any member of the assembly outside the scope
            Assert.Contains(Analyze(source, list + "M:B.F(System.Int32) ; plain reason"), x => x.Id == DeterminismScopeAnalyzer.UnusedId);
            // assumed is for the assembly's own members, and not together with a pin
            Assert.Contains(Analyze(source, list + "M:B.F(System.Int32) ; [assumed][body:0123456789abcdef] pinned and assumed"), x => x.Id == DeterminismScopeAnalyzer.UnusedId);
            Assert.Contains(Analyze(source, list + "M:B.F(System.Int32) ; [assumed] ok\nM:System.Math.Abs(System.Double) ; [assumed] not a member of the assembly"), x => x.Id == DeterminismScopeAnalyzer.UnusedId);
            // an assumed entry that nothing uses is unused like any other
            Assert.Contains(Analyze(source, list + "M:B.F(System.Int32) ; [assumed] ok\nM:B.G ; [assumed] unused"), x => x.Id == DeterminismScopeAnalyzer.UnusedId);
        }

        [Fact]
        public void DelegateInvocations_MustBeRegistered_WithTheirCount_AndAForm()
        {
            string source = Usings + "static class C { static int M(Func<int, int> f) { return f(1) + f(2); } }";
            const string invoke = "M:System.Func`2.Invoke(`0) ; a delegate call\n";
            const string member = "M:C.M(System.Func{System.Int32,System.Int32})";
            Assert.Equal(2, Ids(Analyze(source, invoke), DeterminismScopeAnalyzer.ApiId).Count(x => x == "construct:delegate-invocation"));
            Assert.Empty(Analyze(source, invoke + "construct " + member + " ; delegate-invocation ; 2 ; P4 [T.M] the callback is an exact predicate"));
            // a wrong count, a missing form and an unknown form are reported
            Assert.NotEmpty(Analyze(source, invoke + "construct " + member + " ; delegate-invocation ; 1 ; P4 [T.M] x"));
            Assert.NotEmpty(Analyze(source, invoke + "construct " + member + " ; delegate-invocation ; 2 ; the callback is an exact predicate"));
            Assert.NotEmpty(Analyze(source, invoke + "construct " + member + " ; delegate-invocation ; 2 ; P6 [T.M] x"));
            Assert.NotEmpty(Analyze(source, invoke + "construct " + member + " ; delegate-invocation ; 2 ; P4 x"));
            // a registration of another member or of another construct does not count
            Assert.NotEmpty(Analyze(source, invoke + "construct M:C.N ; delegate-invocation ; 2 ; P4 [T.M] x"));
            Assert.NotEmpty(Analyze(source, invoke + "construct " + member + " ; string-concatenation ; 2 ; x"));
        }

        [Fact]
        public void StringConcatenations_MustBeRegistered_WithTheirCount()
        {
            string source = Usings + "static class C { static string M(int a) { string s = \"a\" + a; s += \"b\"; return s; } }";
            const string member = "M:C.M(System.Int32)";
            Assert.Equal(2, Ids(Analyze(source, ""), DeterminismScopeAnalyzer.ApiId).Count(x => x == "construct:string-concatenation"));
            Assert.Empty(Analyze(source, "construct " + member + " ; string-concatenation ; 2 ; a message"));
            Assert.NotEmpty(Analyze(source, "construct " + member + " ; string-concatenation ; 3 ; a message"));
            // the other pseudo ids cannot be registered
            var d = Analyze(Usings + "static class C { static object M(int a) { return new { a }; } }", "construct M:C.M(System.Int32) ; anonymous-type ; 1 ; x");
            Assert.Contains(d, x => x.Id == DeterminismScopeAnalyzer.UnusedId);
            Assert.Contains("construct:anonymous-type", Ids(d, DeterminismScopeAnalyzer.ApiId));
        }

        [Fact]
        public void FormEntries_NeedTheArgumentForm_AndTheTestsInEverySiteLine()
        {
            string source = Usings + "static class C { static double M(double x) { return Math.Abs(x); } }";
            const string entry = "M:System.Math.Abs(System.Double) ; [sites][form] folds\n";
            const string site = "site M:System.Math.Abs(System.Double) @ M:C.M(System.Double) ; ";
            Assert.Empty(Analyze(source, entry + site + "P3 [DeterminismStage1Tests.A] sorted by a total order"));
            Assert.Empty(Analyze(source, entry + site + "P0 [T.A, T.B] a list in index order"));
            Assert.Equal(0, Analyze(source, entry + site + "P3 [T.A] x").Count);
            foreach (string bad in new[] { "sorted by a total order", "P3 sorted", "P3 [] x", "P7 [T.A] x", "P3 [T.A]", "[T.A] P3 x", "P3 [T A] x" })
                Assert.Contains(Analyze(source, entry + site + bad), x => x.Id == DeterminismScopeAnalyzer.RestrictedId);
            // an entry without [form] takes any reason
            Assert.Empty(Analyze(source, "M:System.Math.Abs(System.Double) ; [sites] folds\n" + site + "compared only"));
        }

        // ---- G3D0008: [SkipLocalsInit] ----

        [Fact]
        public void SkipLocalsInit_MustBeRegistered()
        {
            string source = Usings + "static unsafe class C { [SkipLocalsInit] static int M() { int* p = stackalloc int[4]; p[0] = 1; return p[0]; } static int N() { return 0; } }";
            const string attr = "M:System.Runtime.CompilerServices.SkipLocalsInitAttribute.#ctor ; x\n";
            Assert.Equal(new[] { "M:C.M" }, Ids(Analyze(source, attr), DeterminismScopeAnalyzer.SkipLocalsInitId));
            Assert.Empty(Analyze(source, attr + "skiplocalsinit M:C.M ; p[0] is written first"));
            // on the type: every member
            source = source.Replace("static unsafe class C { [SkipLocalsInit] static int M()", "[SkipLocalsInit] static unsafe class C { static int M()");
            Assert.Equal(new[] { "M:C.M", "M:C.N" }, Ids(Analyze(source, attr), DeterminismScopeAnalyzer.SkipLocalsInitId).OrderBy(x => x));
            // a registration of the type covers the type's attribute, not a member's own one
            Assert.Empty(Analyze(source, attr + "skiplocalsinit T:C ; every buffer written first"));
            string own = source.Replace("static int N()", "[SkipLocalsInit] static int N()");
            Assert.Equal(new[] { "M:C.N" }, Ids(Analyze(own, attr + "skiplocalsinit T:C ; every buffer written first"), DeterminismScopeAnalyzer.SkipLocalsInitId));
        }

        [Theory]
        [InlineData("[SkipLocalsInit] static unsafe int P { get { int* p = stackalloc int[2]; p[0] = 1; return p[0]; } }", "M:C.get_P")]
        [InlineData("static unsafe int M() { return L(); [SkipLocalsInit] static int L() { int* p = stackalloc int[2]; p[0] = 1; return p[0]; } }", "M:C.M")]
        public void SkipLocalsInit_OnPropertiesAndLocalFunctions_MustBeRegistered(string member, string id)
        {
            const string attr = "M:System.Runtime.CompilerServices.SkipLocalsInitAttribute.#ctor ; x\n";
            Assert.Contains(id, Ids(Analyze(Usings + "static class C { " + member + " }", attr), DeterminismScopeAnalyzer.SkipLocalsInitId));
        }

        // ---- G3D0009: the list is exact ----

        [Fact]
        public void UnusedEntries_AndMalformedLines_AreReported()
        {
            string source = Usings + "static class C { static double M(double x) { return Math.Sqrt(x); } }";
            const string used = "M:System.Math.Sqrt(System.Double) ; IEEE\n";
            Assert.Empty(Analyze(source, used));
            foreach (string extra in new[] {
                "M:System.Math.Abs(System.Double) ; exact",                       // unused entry
                "M:System.Math.Min(System.Double,System.Double) ; [sites] x\nsite M:System.Math.Min(System.Double,System.Double) @ M:C.M(System.Double) ; x",
                "if M:C.M(System.Double) ; 1 ; T.M ; x",                         // unused registrations
                "cast M:C.M(System.Double) ; 1 ; x",
                "skiplocalsinit M:C.M(System.Double) ; x",
                "M:System.Math.Abs(System.Double)",                               // malformed
                "M:System.Math.Abs(System.Double) ; ",
                "M:System.Math.Abs(System.Double) ; [nope] x",
                "if M:C.M(System.Double) ; zero ; T.M ; x",
                "site M:System.Math.Min(System.Double,System.Double) M:C.M(System.Double) ; x",
                "M:System.Math.Sqrt(System.Double) ; twice",
                "skiplocalsinit  ; no member",
                "cast C.M ; 1 ; not a documentation id" }) {
                var d = Analyze(source, used + extra);
                Assert.True(d.Any(x => x.Id == DeterminismScopeAnalyzer.UnusedId), extra);
                Assert.All(d.Where(x => x.Id == DeterminismScopeAnalyzer.UnusedId), x => Assert.Equal("Kernel.cs", x.Location.SourceTree.FilePath));
            }
            // a site of a [NET] entry need not be used without NET
            Assert.Empty(Analyze(source, used + "M:System.Math.Abs(System.Double) ; [NET][sites] x\nsite M:System.Math.Abs(System.Double) @ M:C.M(System.Double) ; x", net: false));
            // a [NET] entry need not be used without NET; with NET it must
            Assert.Empty(Analyze(source, used + "M:System.Math.Abs(System.Double) ; [NET] x", net: false));
            Assert.Single(Analyze(source, used + "M:System.Math.Abs(System.Double) ; [NET] x", net: true));
        }

        // ---- the real list ----

        static string ListText()
        {
            string root = ExactCodeAnalyzerTests.RepoRoot();
            Assert.NotNull(root);
            return File.ReadAllText(Path.Combine(root, DeterministicApiList.FileName));
        }

        [Fact]
        public void TheList_IsWellFormed_AndNamesExistingMembersAndTests()
        {
            DeterministicApiList list = DeterministicApiList.Parse(ListText());
            Assert.Empty(list.Errors);
            CSharpCompilation c = Compile("", true).AddReferences(MetadataReference.CreateFromFile(typeof(g3.DMesh3).Assembly.Location))
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithMetadataImportOptions(MetadataImportOptions.All));
            foreach (DeterministicApiList.Entry e in list.Entries.Values) {
                ISymbol s = DocumentationCommentId.GetSymbolsForDeclarationId(e.Id, c).FirstOrDefault();
                Assert.True(s != null, "no symbol for " + e.Id);
                // (the library's own members carry the hash of their reviewed declaration, the assumed ones of a later stage the tag
                // [assumed] instead, and the implicit constructors of a class neither; the build checks it)
                bool own = e.Id.Substring(2).StartsWith("g3.", StringComparison.Ordinal);
                Assert.True(!(e.Assumed && e.Body != null), e.Id);
                Assert.True(!own || e.Body != null || e.Assumed || e.Id.Contains(".#ctor"), e.Id);
                Assert.True(own || (e.Body == null && !e.Assumed), e.Id);
                Assert.True(e.Reason.Length >= 5, e.Id);
            }
            var tests = typeof(DeterminismScopeAnalyzerTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods().Select(m => t.Name + "." + m.Name)).ToImmutableHashSet();
            // the argument forms of the stage 1 lines name tests ("P3 [Class.Test, ...] why"): every one exists
            foreach (string reason in list.Registrations.Where(x => x.Kind == "site" && list.Entries[x.Api].Form || x.Kind == "construct" && x.Api == "construct:delegate-invocation").Select(x => x.Reason)) {
                System.Text.RegularExpressions.Match fm = System.Text.RegularExpressions.Regex.Match(reason, @"^(P[0-5]) \[([^\]]+)\]");
                Assert.True(fm.Success, reason);
                foreach (string test in fm.Groups[2].Value.Split(new[] { ", " }, StringSplitOptions.None))
                    Assert.True(tests.Contains(test), "no test " + test + " named by: " + reason);
            }
            foreach (DeterministicApiList.Registration r in list.Registrations) {
                if (r.Kind == "type") {
                    // (a file of the scope that the editorconfig names, and a type of it)
                    string file = Directory.GetFiles(ExactCodeAnalyzerTests.RepoRoot(), r.Member, SearchOption.AllDirectories).Single(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
                    Assert.Matches(@"\b(class|struct) " + r.Test + @"\b", File.ReadAllText(file));
                    continue;
                }
                ISymbol member = DocumentationCommentId.GetSymbolsForDeclarationId(r.Member, c).FirstOrDefault();
                Assert.True(member != null, "no member for " + r.Kind + " " + r.Member);
                Assert.Equal("geometry3Sharp", member.ContainingAssembly.Name);
                if (r.Kind == "site") {
                    DeterministicApiList.Entry e;
                    Assert.True(list.Entries.TryGetValue(r.Api, out e) && e.Sites, r.Api);
                }
                if (r.Kind == "if")
                    Assert.True(tests.Contains(r.Test), "no test " + r.Test + " for " + r.Member);
            }
            // the restricted entries of the brief (issue 859) carry their restriction
            Assert.True(list.Entries["M:System.Numerics.BigInteger.GetHashCode"].Hash);
            foreach (string id in new[] { "M:System.Math.Min(System.Double,System.Double)", "M:System.Math.Max(System.Double,System.Double)",
                                          "M:System.Threading.Interlocked.Increment(System.Int32@)", "M:System.Threading.Volatile.Read(System.Int32@)" })
                Assert.True(list.Entries[id].Sites, id);
            // nothing ambient is allowed: clock, randomness, environment, culture-dependent text, platform libm, hash seeds
            foreach (string id in list.Entries.Keys.Where(k => !list.Entries[k].Sites))
                foreach (string banned in new[] { "System.Random", "System.DateTime", "System.Diagnostics.Stopwatch", "System.Environment", "System.Guid",
                                                  "ToString", "Parse", "System.HashCode", "System.Math.Sin", "System.Math.Cos", "System.Math.Exp", "System.Math.Log",
                                                  "System.Math.Pow", "System.Math.Atan", "System.Math.FusedMultiplyAdd", "System.Threading.Tasks", "System.Linq" })
                    Assert.DoesNotContain(banned, id);
        }

        /// <summary> the kernel files of stage 0 (issue 859); a file missing from the .editorconfig section falls out of the proof </summary>
        public static readonly string[] Kernel = {
            "math/ExactIntervals.cs", "math/ExactPoint3.cs", "math/ExactPredicates.cs", "math/ExactRayTriangle.cs", "math/ExactSlopeKernel.cs",
            "math/ExactSlopePredicates.cs", "math/AdaptivePredicates.cs", "math/FixedPoint3.cs", "math/ExactLimbSigns.cs", "math/Generated/IndirectPredicates.cs",
            "math/Generated/DoublePredicates.cs",
            "intersection/ExactTriangleIntersection.cs", "intersection/TriangleTriangleFilter.cs" };

        /// <summary> the files of stage 1 (the exact building blocks); ExactMeshArrangement.cs is in the scope for its class TriangleBoxTree only (a 'type' line) </summary>
        public static readonly string[] Stage1 = { "mesh_ops/ExactTriangulation2.cs", "mesh_ops/ExactPlanarSimplify.cs", "comp_geom/SnapRounding2.cs", "mesh_ops/ExactMeshArrangement.cs" };

        static readonly string[] ScopeRules = { DeterminismScopeAnalyzer.ApiId, DeterminismScopeAnalyzer.RestrictedId, DeterminismScopeAnalyzer.ConditionalId,
            DeterminismScopeAnalyzer.CastId, DeterminismScopeAnalyzer.DirectiveId, DeterminismScopeAnalyzer.SkipLocalsInitId, DeterminismScopeAnalyzer.UnusedId };

        [Fact]
        public void EditorConfig_MakesTheScopeRulesErrorsInExactlyTheKernel()
        {
            string root = ExactCodeAnalyzerTests.RepoRoot();
            AnalyzerConfigSet set = ExactCodeAnalyzerTests.EditorConfig(root);
            foreach (string f in Kernel.Concat(Stage1)) {
                Assert.True(File.Exists(Path.Combine(root, f)), f);
                foreach (string id in ScopeRules)
                    Assert.True(ExactCodeAnalyzerTests.Severity(set, root, f, id) == ReportDiagnostic.Error, f + " " + id);
            }
            // every other library file is outside stages 0 and 1, also the exact files of the later stage
            var kernel = Kernel.Concat(Stage1).ToImmutableHashSet();
            var others = Directory.GetFiles(Path.Combine(root, "math"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(root, "mesh_ops"), "Exact*.cs"))
                .Concat(Directory.GetFiles(Path.Combine(root, "intersection"), "*.cs"))
                .Concat(Directory.GetFiles(Path.Combine(root, "comp_geom"), "*.cs"))
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(f => !kernel.Contains(f)).ToList();
            Assert.Contains("mesh_ops/ExactMeshBoolean.cs", others);
            Assert.Contains("comp_geom/ConstrainedMesh2.cs", others);
            // the one file that is in the scope for a type only: ExactMeshArrangement.cs, for TriangleBoxTree (stage 2 owns the rest)
            var types = DeterministicApiList.Parse(ListText()).Registrations.Where(x => x.Kind == "type").Select(x => x.Member + ":" + x.Test).ToList();
            Assert.Equal(new[] { "ExactMeshArrangement.cs:TriangleBoxTree" }, types);
            foreach (string f in others)
                foreach (string id in ScopeRules)
                    Assert.True(ExactCodeAnalyzerTests.Severity(set, root, f, id) == ReportDiagnostic.Suppress, f + " " + id);
        }

        [Fact]
        public void LibraryProject_PassesTheList()
        {
            string root = ExactCodeAnalyzerTests.RepoRoot();
            string csproj = File.ReadAllText(Path.Combine(root, "geometry3Sharp_netstandard.csproj"));
            Assert.Contains(@"<AdditionalFiles Include=""" + DeterministicApiList.FileName + @""" />", csproj);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(csproj, "<(NoWarn|WarningsNotAsErrors)>([^<]*)<"))
                foreach (string id in ScopeRules)
                    Assert.DoesNotContain(id, m.Groups[2].Value);
        }

        // nothing outside the analyzer's own configuration switches the scope's rules off: no SuppressMessage, NoWarn,
        // global config or editorconfig section names them (a pragma in a kernel file is G3D0007)
        [Fact]
        public void NothingElse_SuppressesTheScopeRules()
        {
            string root = ExactCodeAnalyzerTests.RepoRoot();
            var allowed = new[] { ".editorconfig", "DeterministicApis.txt", "tools/analyzers/G3ExactCodeAnalyzers/DeterminismAnalyzers.cs",
                "tests/geometry3Sharp.Tests/DeterminismScopeAnalyzerTests.cs" };
            var hits = new List<string>();
            foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
                string rel = Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith(".git/") || rel.Contains("/bin/") || rel.Contains("/obj/") || rel.StartsWith("bin/") || rel.StartsWith("obj/") || rel.StartsWith("docs/"))
                    continue;
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (!(ext == ".cs" || ext == ".props" || ext == ".targets" || ext == ".csproj" || ext == ".globalconfig" || ext == ".editorconfig"
                      || ext == ".ruleset" || ext == ".json" || Path.GetFileName(f) == ".editorconfig"))
                    continue;
                if (allowed.Contains(rel))
                    continue;
                string text = File.ReadAllText(f);
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"SuppressMessage\([^)]*G3D|#pragma\s+warning\s+disable[^\n]*G3D|<(NoWarn|WarningsNotAsErrors|WarningsAsErrors)>[^<]*G3D|dotnet_diagnostic\.G3D|G3D\d{4}\s*=|^\s*generated_code\s*=", System.Text.RegularExpressions.RegexOptions.Multiline))
                    hits.Add(rel);
            }
            Assert.True(hits.Count == 0, string.Join(", ", hits));
            // and nothing marks a kernel file as generated (the rules analyze generated code, but a marker would also
            // switch off G3E0001-4)
            foreach (string f in Kernel)
                Assert.DoesNotContain("<auto-generated", File.ReadAllText(Path.Combine(root, f)));
            // the root .editorconfig names them only in the two sections of this rule set (all off, then the kernel)
            string ec = File.ReadAllText(Path.Combine(root, ".editorconfig"));
            Assert.DoesNotContain("generated_code", ec);
            Assert.Equal(14, System.Text.RegularExpressions.Regex.Matches(ec, @"dotnet_diagnostic\.G3D\d{4}\.severity").Count);
        }

        /// <summary> the ledger of stages 0 and 1 as the design note states it, generated from the list </summary>
        public static string Ledger(DeterministicApiList list)
        {
            var e = list.Entries.Values.ToList();
            var r = list.Registrations;
            int ifs = r.Where(x => x.Kind == "if").Sum(x => x.Count), casts = r.Where(x => x.Kind == "cast").Sum(x => x.Count);
            Func<DeterministicApiList.Registration, bool> delegates = x => x.Kind == "construct" && x.Api == "construct:delegate-invocation";
            Func<DeterministicApiList.Registration, bool> concats = x => x.Kind == "construct" && x.Api == "construct:string-concatenation";
            Func<DeterministicApiList.Entry, bool> own = x => x.Id.Substring(2).StartsWith("g3.", StringComparison.Ordinal);
            return "**Ledger** (generated from `DeterministicApis.txt`, checked by `DeterminismScopeAnalyzerTests`; stages 0 and 1): "
                + e.Count + " external members (" + e.Count(own) + " of them geometry3Sharp members declared outside the scope: "
                + e.Count(x => own(x) && x.Body != null) + " pinned, " + e.Count(x => x.Assumed) + " assumed as obligations of stage 2; "
                + e.Count(x => x.NetOnly) + " only on the `#if NET` side), "
                + e.Count(x => x.Sites) + " restricted to registered sites (" + r.Count(x => x.Kind == "site") + " sites, "
                + r.Count(x => x.Kind == "site" && list.Entries[x.Api].Form) + " of them with an argument form and its tests), "
                + e.Count(x => x.Hash) + " restricted to `GetHashCode` implementations; "
                + ifs + " conditional-compilation blocks in " + r.Count(x => x.Kind == "if") + " members; "
                + casts + " `double` to integer conversions in " + r.Where(x => x.Kind == "cast").Select(x => x.Member).Distinct().Count() + " member(s); "
                + r.Count(x => x.Kind == "skiplocalsinit") + " `[SkipLocalsInit]` members; "
                + r.Where(delegates).Sum(x => x.Count) + " delegate calls in " + r.Count(delegates) + " members and "
                + r.Where(concats).Sum(x => x.Count) + " string concatenations in " + r.Count(concats) + " members, registered; "
                + r.Count(x => x.Kind == "type") + " file restricted to a named type.";
        }

        [Fact]
        public void DesignNote_StatesTheGeneratedLedger()
        {
            string root = ExactCodeAnalyzerTests.RepoRoot();
            string doc = File.ReadAllText(Path.Combine(root, "docs", "design", "DETERMINISM_PROOF.md")).Replace("\r\n", "\n");
            string ledger = Ledger(DeterministicApiList.Parse(ListText()));
            Assert.True(doc.Contains(ledger), "docs/design/DETERMINISM_PROOF.md section 5.4 must contain: " + ledger);
        }
    }
}
