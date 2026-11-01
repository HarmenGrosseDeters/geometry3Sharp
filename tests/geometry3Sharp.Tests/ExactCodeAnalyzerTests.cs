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
    /// The machine checks of the exact-arithmetic code (docs/QA.md 4a item 3): the float analyzer G3E0001, and the
    /// configuration of the banned-API analyzer (BannedSymbols.txt, core/ExactCodeAllowList.cs, .editorconfig), whose
    /// entries must name existing symbols (a misspelled ID bans nothing, silently).
    /// </summary>
    public class ExactCodeAnalyzerTests
    {
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

        static List<Diagnostic> Float(string source)
        {
            CSharpCompilation c = Compile(source);
            Assert.Empty(c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            return c.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NoSinglePrecisionAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().Result.Where(d => d.Id == NoSinglePrecisionAnalyzer.DiagnosticId).ToList();
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
        [InlineData("static double M() { return new g3.Vector3f(1, 2, 3).x; }")]
        [InlineData("static double M() { return new float[3].Length; }")]
        [InlineData("static double M() { return new System.Collections.Generic.List<float>().Count; }")]
        [InlineData("static double M(double[] a) { return System.Linq.Enumerable.Sum(a, x => (float)x); }")]
        public void Float_IsReported(string member)
        {
            Assert.NotEmpty(Float("static class C { " + member + " }"));
        }

        [Fact]
        public void Double_IsNotReported()
        {
            Assert.Empty(Float(@"static class C {
                static double f;
                static double M(double a, int b, long c, decimal d) {
                    double[] v = new double[3]; var l = new System.Collections.Generic.List<double>();
                    return a * b + c + (double)d + v.Length + l.Count + double.Epsilon + new g3.Vector3d(1, 2, 3).x;
                }
            }"));
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
            Assert.Contains("T:g3.gParallel", ids);
            Assert.Contains("T:System.Threading.Tasks.Parallel", ids);
            Assert.Contains("M:System.Math.FusedMultiplyAdd(System.Double,System.Double,System.Double)", ids);
            foreach (string id in ids)
                Assert.True(DocumentationCommentId.GetSymbolsForDeclarationId(id, c).Any(), "no symbol for " + id);
        }

        [Fact]
        public void AllowList_TargetsResolve_AndAreInExactCode()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            CSharpCompilation c = Compile("");
            var targets = Regex.Matches(File.ReadAllText(Path.Combine(root, "core", "ExactCodeAllowList.cs")),
                @"Target = ""~([^""]+)""").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(targets);
            foreach (string t in targets) {
                string id = Regex.Replace(t, "~[^~]*$", "");    // without the return type
                ISymbol s = DocumentationCommentId.GetSymbolsForDeclarationId(id, c).FirstOrDefault();
                Assert.True(s != null, "no member for " + t);
                Assert.StartsWith("ExactMesh", s.ContainingType.Name);
            }
        }

        // the scoping: the rules are errors in exactly the exact files, off elsewhere
        [Fact]
        public void EditorConfig_ScopesTheRulesToTheExactCode()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            string text = File.ReadAllText(Path.Combine(root, ".editorconfig")).Replace("\r\n", "\n");
            string exact = "[{math/Exact*.cs,math/AdaptivePredicates.cs,mesh_ops/ExactMesh*.cs}]\n";
            int i = text.IndexOf(exact, StringComparison.Ordinal);
            Assert.True(i >= 0);
            string section = text.Substring(i + exact.Length).Split('[')[0];
            Assert.Contains("dotnet_diagnostic.RS0030.severity = error", section);
            Assert.Contains("dotnet_diagnostic.G3E0001.severity = error", section);
            // the files exist (a renamed file falls out of the glob)
            Assert.True(File.Exists(Path.Combine(root, "math", "AdaptivePredicates.cs")));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "math"), "Exact*.cs"));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "mesh_ops"), "ExactMesh*.cs"));
        }
    }
}
