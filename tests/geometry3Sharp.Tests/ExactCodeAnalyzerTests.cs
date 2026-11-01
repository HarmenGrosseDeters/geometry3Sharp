using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using G3ExactCodeAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The machine checks of the exact-arithmetic code (docs/QA.md 4a item 3): the analyzers G3E0001 (float), G3E0002
    /// (parallel loops) and G3E0003 (fused multiply-add), and the configuration that switches them and the banned-API
    /// rule RS0030 on for the exact files: .editorconfig (evaluated with Roslyn's own editorconfig semantics), the
    /// project file, BannedSymbols.txt and core/ExactCodeAllowList.cs, whose entries must name existing symbols (a
    /// misspelled ID bans nothing, silently).
    /// </summary>
    public class ExactCodeAnalyzerTests
    {
        static readonly string[] Rules = { "RS0030", "G3E0001", "G3E0002", "G3E0003" };

        static string RepoRoot()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "BannedSymbols.txt")) && File.Exists(Path.Combine(d.FullName, "CLAUDE.md")))
                    return d.FullName;
            return null;
        }

        // the framework reference assemblies plus geometry3Sharp (with its internal members)
        static CSharpCompilation Compile(string source)
        {
            var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            refs.Add(MetadataReference.CreateFromFile(typeof(g3.DMesh3).Assembly.Location));
            return CSharpCompilation.Create("AnalyzerTest", new[] { CSharpSyntaxTree.ParseText(source) }, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithMetadataImportOptions(MetadataImportOptions.All));
        }

        static List<Diagnostic> Analyze(string source, string id)
        {
            CSharpCompilation c = Compile(source);
            Assert.Empty(c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new NoSinglePrecisionAnalyzer(), new ExactCodeApiAnalyzer());
            return c.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync().Result.Where(d => d.Id == id).ToList();
        }

        static List<Diagnostic> Member(string member, string id)
        {
            return Analyze("using g3; static class C { " + member + " }", id);
        }

        [Theory]
        [InlineData("static float f;")]
        [InlineData("static float P => 0;")]
        [InlineData("static double M(float a) { return 0; }")]
        [InlineData("static float M() { return 0; }")]
        [InlineData("static double M(double a) { return (float)a; }")]
        [InlineData("static double M() { var x = 1.5f; return 0; }")]
        [InlineData("static double M() { float x; x = 0; return 0; }")]
        [InlineData("static double M() { float x; return 0; }")]
        [InlineData("static double M() { return float.Epsilon; }")]
        [InlineData("static double M() { return System.Convert.ToSingle(1.0); }")]
        [InlineData("static double M() { return new Vector3f(1, 2, 3).x; }")]
        [InlineData("static double M() { return new float[3].Length; }")]
        [InlineData("static double M() { return new System.Collections.Generic.List<float>().Count; }")]
        [InlineData("static double M(double[] a) { return System.Linq.Enumerable.Sum(a, x => (float)x); }")]
        // structs with float fields: their values went through float rounding, also when converted to double
        [InlineData("static double M(Vector3f v) { Vector3d d = v; return d.x; }")]
        [InlineData("static double M(DMesh3 m) { Vector3d n = m.GetVertexNormal(0); return n.x; }")]
        [InlineData("static double M(AxisAlignedBox3f b) { AxisAlignedBox3d d = b; return d.Min.x; }")]
        [InlineData("static double M() { var l = new System.Collections.Generic.List<Vector2f>(); return l.Count; }")]
        public void Float_IsReported(string member)
        {
            Assert.NotEmpty(Member(member, NoSinglePrecisionAnalyzer.DiagnosticId));
        }

        [Theory]
        [InlineData("static double f;")]
        [InlineData("static double M(double a, int b, long c, decimal d) { return a * b + c + (double)d; }")]
        [InlineData("static double M() { double[] v = new double[3]; var l = new System.Collections.Generic.List<double>(); return v.Length + l.Count; }")]
        [InlineData("static double M() { return double.Epsilon + new Vector3d(1, 2, 3).x + Vector3d.AxisX.Length; }")]
        [InlineData("static double M(AxisAlignedBox3d b, Index3i t, System.Numerics.BigInteger i) { return b.Min.x + t.a + (double)i; }")]
        // classes hold float fields by reference; passing the mesh around reads none of them
        [InlineData("static double M(DMesh3 m) { return m.GetVertex(0).x + m.TriangleCount; }")]
        public void Double_IsNotReported(string member)
        {
            Assert.Empty(Member(member, NoSinglePrecisionAnalyzer.DiagnosticId));
        }

        [Theory]
        [InlineData("static void M(double[] v) { gParallel.ForEach(new[] { 1, 2 }, (i) => { }); }")]
        [InlineData("static void M(double[] v) { System.Threading.Tasks.Parallel.For(0, 3, (i) => { }); }")]
        [InlineData("static double M(double[] v) { return System.Linq.ParallelEnumerable.Sum(System.Linq.ParallelEnumerable.AsParallel(v)); }")]
        public void Parallel_IsReported(string member)
        {
            Assert.NotEmpty(Member(member, ExactCodeApiAnalyzer.ParallelId));
        }

        [Theory]
        [InlineData("static double M() { return System.Math.FusedMultiplyAdd(1.0, 2.0, 3.0); }")]
        [InlineData("static double M() { return double.FusedMultiplyAdd(1.0, 2.0, 3.0); }")]
        [InlineData("static double M() { return double.MultiplyAddEstimate(1.0, 2.0, 3.0); }")]
        [InlineData("static void M(System.Runtime.Intrinsics.Vector128<double> a) { System.Runtime.Intrinsics.Vector128.FusedMultiplyAdd(a, a, a); }")]
        [InlineData("static void M(System.Runtime.Intrinsics.Vector256<double> a) { System.Runtime.Intrinsics.Vector256.MultiplyAddEstimate(a, a, a); }")]
        [InlineData("static void M(System.Runtime.Intrinsics.Vector128<double> a) { System.Runtime.Intrinsics.X86.Fma.MultiplyAdd(a, a, a); }")]
        [InlineData("static System.Func<double, double, double, double> M() { return System.Math.FusedMultiplyAdd; }")]
        public void FusedMultiplyAdd_IsReported(string member)
        {
            Assert.NotEmpty(Member(member, ExactCodeApiAnalyzer.FmaId));
        }

        [Fact]
        public void MultiplyAndAdd_IsNotReported()
        {
            Assert.Empty(Member("static double M(double a, double b, double c) { return a * b + c + System.Math.Abs(a) + System.Math.Max(b, c); }",
                ExactCodeApiAnalyzer.FmaId));
            Assert.Empty(Member("static void M(int[] v) { for (int i = 0; i < v.Length; ++i) v[i] = i; }", ExactCodeApiAnalyzer.ParallelId));
        }

        // an allow-list entry (as in core/ExactCodeAllowList.cs) switches off the parallel rule only
        [Fact]
        public void AllowListEntry_SuppressesOnlyTheParallelRule()
        {
            string source = @"using g3;
                [assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(""Reliability"", ""G3E0002"", Scope = ""member"", Target = ""~M:C.M(System.Double[])~System.Double"")]
                static class C {
                    static double M(double[] v) {
                        gParallel.ForEach(new[] { 1, 2 }, (i) => { });
                        float f = 1;
                        return System.Math.FusedMultiplyAdd(v[0], v[1], v[2]) + f;
                    }
                    static void N() { gParallel.ForEach(new[] { 1, 2 }, (i) => { }); }
                }";
            var parallel = Analyze(source, ExactCodeApiAnalyzer.ParallelId);
            Assert.Single(parallel);      // the one in N
            Assert.Contains("N()", parallel[0].Location.SourceTree.GetText().GetSubText(
                parallel[0].Location.SourceTree.GetRoot().FindNode(parallel[0].Location.SourceSpan).Parent.Parent.Parent.Parent.Span).ToString());
            Assert.NotEmpty(Analyze(source, ExactCodeApiAnalyzer.FmaId));
            Assert.NotEmpty(Analyze(source, NoSinglePrecisionAnalyzer.DiagnosticId));
        }

        [Fact]
        public void BannedSymbols_AllResolve()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            CSharpCompilation c = Compile("");
            var ids = File.ReadAllLines(Path.Combine(root, "BannedSymbols.txt"))
                .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("//"))
                .Select(l => l.Split(';')[0].Trim()).ToList();
            Assert.Contains("T:System.Single", ids);
            Assert.Contains("M:System.Math.FusedMultiplyAdd(System.Double,System.Double,System.Double)", ids);
            Assert.Contains("M:System.Threading.Interlocked.CompareExchange(System.Double@,System.Double,System.Double)", ids);
            // parallel loops are G3E0002, which the allow-list can switch off; RS0030 has no allow-list
            Assert.DoesNotContain("T:g3.gParallel", ids);
            Assert.DoesNotContain("T:System.Threading.Tasks.Parallel", ids);
            foreach (string id in ids) {
                bool found = id.StartsWith("N:")
                    ? c.GlobalNamespace.GetNamespaceMembers().Any(n => Find(n, id.Substring(2)))
                    : DocumentationCommentId.GetSymbolsForDeclarationId(id, c).Any();
                Assert.True(found, "no symbol for " + id);
            }
        }

        static bool Find(INamespaceSymbol ns, string name)
        {
            string full = ns.ToDisplayString();
            if (full == name)
                return true;
            return name.StartsWith(full + ".", StringComparison.Ordinal) && ns.GetNamespaceMembers().Any(n => Find(n, name));
        }

        [Fact]
        public void AllowList_TargetsResolve_AndAreInExactCode_AndOnlyTheParallelRule()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            CSharpCompilation c = Compile("");
            string text = File.ReadAllText(Path.Combine(root, "core", "ExactCodeAllowList.cs"));
            var entries = Regex.Matches(text, @"SuppressMessage\(""([^""]+)"", ""([^""]+)"".*?Target = ""~([^""]+)""")
                .Cast<Match>().ToList();
            Assert.NotEmpty(entries);
            Assert.Equal(Regex.Matches(text, "SuppressMessage\\(").Count, entries.Count);
            foreach (Match e in entries) {
                Assert.Equal("G3E0002", e.Groups[2].Value);
                string id = Regex.Replace(e.Groups[3].Value, "~[^~]*$", "");    // without the return type
                ISymbol s = DocumentationCommentId.GetSymbolsForDeclarationId(id, c).FirstOrDefault();
                Assert.True(s != null, "no member for " + e.Groups[3].Value);
                Assert.StartsWith("ExactMesh", s.ContainingType.Name);
            }
        }

        static AnalyzerConfigSet EditorConfig(string root)
        {
            string path = Path.Combine(root, ".editorconfig");
            return AnalyzerConfigSet.Create(ImmutableArray.Create(AnalyzerConfig.Parse(File.ReadAllText(path), path)));
        }

        static ReportDiagnostic Severity(AnalyzerConfigSet set, string root, string relative, string id)
        {
            var options = set.GetOptionsForSourcePath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return options.TreeOptions.TryGetValue(id, out ReportDiagnostic r) ? r : ReportDiagnostic.Default;
        }

        // the rules are errors in every exact file, by Roslyn's own reading of .editorconfig, and off elsewhere
        [Fact]
        public void EditorConfig_MakesTheRulesErrorsInExactlyTheExactCode()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            AnalyzerConfigSet set = EditorConfig(root);
            var exact = Directory.GetFiles(Path.Combine(root, "math"), "Exact*.cs")
                .Concat(Directory.GetFiles(Path.Combine(root, "mesh_ops"), "ExactMesh*.cs"))
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/')).ToList();
            exact.AddRange(new[] { "math/AdaptivePredicates.cs", "math/FixedPoint3.cs", "mesh_ops/ExactTriangulation2.cs",
                "mesh_ops/ExactPlanarSimplify.cs", "intersection/ExactTriangleIntersection.cs" });
            Assert.True(exact.Count >= 8);
            foreach (string f in exact) {
                Assert.True(File.Exists(Path.Combine(root, f)), f);     // a renamed file falls out of the glob
                foreach (string id in Rules)
                    Assert.True(Severity(set, root, f, id) == ReportDiagnostic.Error, f + " " + id);
            }
            foreach (string f in new[] { "math/MathUtil.cs", "mesh/DMesh3.cs", "core/gParallel.cs", "mesh_ops/MeshRoundingRepair.cs",
                                         "core/ExactCodeAllowList.cs" })
                foreach (string id in Rules)
                    Assert.True(Severity(set, root, f, id) == ReportDiagnostic.Suppress, f + " " + id);
        }

        // the library build runs the analyzers: a project file without them would make every rule a no-op
        [Fact]
        public void LibraryProject_RunsTheAnalyzers()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            string csproj = File.ReadAllText(Path.Combine(root, "geometry3Sharp_netstandard.csproj"));
            Assert.Matches(@"<PackageReference Include=""Microsoft.CodeAnalysis.BannedApiAnalyzers""[^>]*PrivateAssets=""all""", csproj);
            Assert.Contains(@"<AdditionalFiles Include=""BannedSymbols.txt"" />", csproj);
            Assert.Matches(@"<ProjectReference Include=""tools\\analyzers\\G3ExactCodeAnalyzers\\G3ExactCodeAnalyzers.csproj""\s+OutputItemType=""Analyzer""", csproj);
            foreach (Match m in Regex.Matches(csproj, "<(NoWarn|WarningsNotAsErrors)>([^<]*)<"))
                foreach (string id in Rules)
                    Assert.DoesNotContain(id, m.Groups[2].Value);
            Assert.DoesNotContain("RunAnalyzers", csproj);
            Assert.DoesNotContain("EnableNETAnalyzers>false", csproj);
        }
    }
}
