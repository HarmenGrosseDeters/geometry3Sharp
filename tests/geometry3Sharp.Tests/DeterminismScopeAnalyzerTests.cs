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
    /// The closed world of the determinism proof scope, stage 0 (docs/design/DETERMINISM_PROOF.md, issue 859): the rules
    /// G3D0001-9 of tools/analyzers/G3ExactCodeAnalyzers/DeterminismAnalyzers.cs on small sources, the allow list
    /// DeterministicApis.txt (well formed, every member and test it names exists), the scope in .editorconfig (exactly the
    /// kernel files) and the stage 0 ledger of the design note, which is generated from the list.
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

        static CSharpCompilation Compile(string source, bool net)
        {
            var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: net ? new[] { "NET" } : new string[0]);
            return CSharpCompilation.Create("ScopeTest", new[] { CSharpSyntaxTree.ParseText(source, parse, "Kernel.cs") }, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        }

        static List<Diagnostic> Analyze(string source, string list, bool net = true)
        {
            CSharpCompilation c = Compile(source, net);
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
        [InlineData("static object M(int a) { return (a, 2); }", "construct:tuple")]
        [InlineData("static object M(int a) { return new { a }; }", "construct:anonymous-type")]
        [InlineData("static async System.Threading.Tasks.Task M(System.Threading.Tasks.Task t) { await t; }", "construct:await")]
        [InlineData("static int[] M(int[] a) { return a[1..]; }", "construct:range")]
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
                sealed class K : IEqualityComparer<long> { public bool Equals(long a, long b) { return a == b; } public int GetHashCode(long k) { return (int)(k >> 3); } }";
            Assert.Empty(Analyze(ok, list).Where(x => x.Id != DeterminismScopeAnalyzer.ApiId));
            // into an order, a result or an index: reported, also the hash of the scope's own type (its dispatch is not followed)
            foreach (string bad in new[] {
                "static int M(BigInteger x) { return x.GetHashCode() % 8; }",
                "static int M(P p) { return p.GetHashCode(); }",
                "static int M(object o) { return o.GetHashCode(); }",
                "static int M(P a, P b) { return a.GetHashCode().CompareTo(b.GetHashCode()); }",
                // (a method named GetHashCode that is not an override or a comparer's implementation)
                "static int GetHashCode(BigInteger x) { return x.GetHashCode(); }" }) {
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

        // ---- G3D0006: double -> integer conversions ----

        [Theory]
        [InlineData("static int M(double x) { return (int)x; }")]
        [InlineData("static long M(double x) { return checked((long)x); }")]
        [InlineData("static ulong M(double x) { return (ulong)(x * 2); }")]
        [InlineData("static byte M(double x) { return (byte)x; }")]
        [InlineData("static System.DayOfWeek M(double x) { return (System.DayOfWeek)x; }")]
        [InlineData("static int P { get { double x = 2.5; return (int)x; } }")]
        public void DoubleToIntegerCast_IsReported(string member)
        {
            var d = Analyze(Usings + "static class C { " + member + " }", "");
            Assert.Single(d, x => x.Id == DeterminismScopeAnalyzer.CastId);
        }

        [Fact]
        public void OtherConversions_AreNotCasts()
        {
            var d = Analyze(Usings + "static class C { static double M(int i, long k, double x) { long a = i; double b = k; int c = (int)k; uint u = (uint)i; return a + b + c + u + (double)x; } }", "");
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
            Assert.Single(d);
            Assert.Equal(DeterminismScopeAnalyzer.CastId, d[0].Id);
            Assert.Equal("2", d[0].Properties[DeterminismScopeAnalyzer.CountProperty]);
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
                "M:System.Math.Sqrt(System.Double) ; twice" }) {
                var d = Analyze(source, used + extra);
                Assert.True(d.Any(x => x.Id == DeterminismScopeAnalyzer.UnusedId), extra);
                Assert.All(d.Where(x => x.Id == DeterminismScopeAnalyzer.UnusedId), x => Assert.Equal("Kernel.cs", x.Location.SourceTree.FilePath));
            }
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
                Assert.NotEqual("geometry3Sharp", s.ContainingAssembly.Name);
                Assert.True(e.Reason.Length >= 5, e.Id);
            }
            var tests = typeof(DeterminismScopeAnalyzerTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods().Select(m => t.Name + "." + m.Name)).ToImmutableHashSet();
            foreach (DeterministicApiList.Registration r in list.Registrations) {
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
            foreach (string id in list.Entries.Keys)
                foreach (string banned in new[] { "System.Random", "System.DateTime", "System.Diagnostics.Stopwatch", "System.Environment", "System.Guid",
                                                  "ToString", "Parse", "System.HashCode", "System.Math.Sin", "System.Math.Cos", "System.Math.Exp", "System.Math.Log",
                                                  "System.Math.Pow", "System.Math.Atan", "System.Math.FusedMultiplyAdd", "System.Threading.Tasks", "System.Linq" })
                    Assert.DoesNotContain(banned, id);
        }

        /// <summary> the kernel files of stage 0 (issue 859); a file missing from the .editorconfig section falls out of the proof </summary>
        public static readonly string[] Kernel = {
            "math/ExactIntervals.cs", "math/ExactPoint3.cs", "math/ExactPredicates.cs", "math/ExactRayTriangle.cs", "math/ExactSlopeKernel.cs",
            "math/ExactSlopePredicates.cs", "math/AdaptivePredicates.cs", "math/FixedPoint3.cs", "math/Generated/IndirectPredicates.cs",
            "intersection/ExactTriangleIntersection.cs", "intersection/TriangleTriangleFilter.cs" };

        static readonly string[] ScopeRules = { DeterminismScopeAnalyzer.ApiId, DeterminismScopeAnalyzer.RestrictedId, DeterminismScopeAnalyzer.ConditionalId,
            DeterminismScopeAnalyzer.CastId, DeterminismScopeAnalyzer.SkipLocalsInitId, DeterminismScopeAnalyzer.UnusedId };

        [Fact]
        public void EditorConfig_MakesTheScopeRulesErrorsInExactlyTheKernel()
        {
            string root = ExactCodeAnalyzerTests.RepoRoot();
            AnalyzerConfigSet set = ExactCodeAnalyzerTests.EditorConfig(root);
            foreach (string f in Kernel) {
                Assert.True(File.Exists(Path.Combine(root, f)), f);
                foreach (string id in ScopeRules)
                    Assert.True(ExactCodeAnalyzerTests.Severity(set, root, f, id) == ReportDiagnostic.Error, f + " " + id);
            }
            // every other library file is outside stage 0, also the exact files of the later stages
            var kernel = Kernel.ToImmutableHashSet();
            var others = Directory.GetFiles(Path.Combine(root, "math"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(root, "mesh_ops"), "Exact*.cs"))
                .Concat(Directory.GetFiles(Path.Combine(root, "intersection"), "*.cs"))
                .Concat(Directory.GetFiles(Path.Combine(root, "comp_geom"), "*.cs"))
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(f => !kernel.Contains(f)).ToList();
            Assert.Contains("mesh_ops/ExactMeshArrangement.cs", others);
            Assert.Contains("comp_geom/SnapRounding2.cs", others);
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

        /// <summary> the stage 0 ledger as the design note states it, generated from the list </summary>
        public static string Ledger(DeterministicApiList list)
        {
            var e = list.Entries.Values.ToList();
            var r = list.Registrations;
            int ifs = r.Where(x => x.Kind == "if").Sum(x => x.Count), casts = r.Where(x => x.Kind == "cast").Sum(x => x.Count);
            return "**Stage 0 ledger** (generated from `DeterministicApis.txt`, checked by `DeterminismScopeAnalyzerTests`): "
                + e.Count + " external members (" + e.Count(x => x.NetOnly) + " of them only on the `#if NET` side), "
                + e.Count(x => x.Sites) + " restricted to registered sites (" + r.Count(x => x.Kind == "site") + " sites), "
                + e.Count(x => x.Hash) + " restricted to `GetHashCode` implementations; "
                + ifs + " conditional-compilation blocks in " + r.Count(x => x.Kind == "if") + " members; "
                + casts + " `double` to integer conversions in " + r.Count(x => x.Kind == "cast") + " member(s); "
                + r.Count(x => x.Kind == "skiplocalsinit") + " `[SkipLocalsInit]` members.";
        }

        [Fact]
        public void DesignNote_StatesTheGeneratedLedger()
        {
            string root = ExactCodeAnalyzerTests.RepoRoot();
            string doc = File.ReadAllText(Path.Combine(root, "docs", "design", "DETERMINISM_PROOF.md")).Replace("\r\n", "\n");
            string ledger = Ledger(DeterministicApiList.Parse(ListText()));
            Assert.True(doc.Contains(ledger), "docs/design/DETERMINISM_PROOF.md section 5.2 must contain: " + ledger);
        }
    }
}
