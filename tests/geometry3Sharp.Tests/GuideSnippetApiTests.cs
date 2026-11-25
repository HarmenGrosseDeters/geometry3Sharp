using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The C# snippets of docs/guides are not compiled. This checks the part that went wrong in the remeshing guide (found
    /// by the browser demos, docs/research/DOGFOODING_DEMOS_2026-11.md): a call `Type.Method(..., name: value)` on a type
    /// of the library must name a parameter that some overload of that method has, with no more positional arguments
    /// before the named ones than that overload takes.
    /// </summary>
    public class GuideSnippetApiTests
    {
        static readonly Regex Call = new Regex(@"\b([A-Z]\w*)\.([A-Z]\w*)\(([^()]*(?:\([^()]*\)[^()]*)*)\)");
        static readonly Regex Named = new Regex(@"(?<![\w?.])([a-z]\w*):(?!:)");

        static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        static IEnumerable<string> Snippets(string markdown)
        {
            foreach (Match m in Regex.Matches(markdown, "```csharp\\r?\\n(.*?)```", RegexOptions.Singleline))
                yield return m.Groups[1].Value;
        }

        /// <summary> the problems of the named arguments in the snippets of one markdown text (empty: all fine) </summary>
        internal static List<string> Check(string markdown)
        {
            var types = typeof(DMesh3).Assembly.GetExportedTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList());
            var problems = new List<string>();
            foreach (string snippet in Snippets(markdown))
                foreach (Match c in Call.Matches(snippet)) {
                    string args = c.Groups[3].Value;
                    var names = Named.Matches(args).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                    if (names.Count == 0 || !types.TryGetValue(c.Groups[1].Value, out var candidates) || candidates.Count != 1)
                        continue;
                    var methods = candidates[0].GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                        .Where(mi => mi.Name == c.Groups[2].Value).ToList();
                    if (methods.Count == 0)
                        continue;
                    int positional = args.Substring(0, Named.Match(args).Index).Count(ch => ch == ',');
                    bool ok = methods.Any(mi => {
                        var ps = mi.GetParameters();
                        return names.All(n => ps.Any(p => p.Name == n && p.Position >= positional));
                    });
                    if (!ok)
                        problems.Add($"{c.Groups[1].Value}.{c.Groups[2].Value}({args})");
                }
            return problems;
        }

        [Fact]
        public void GuideSnippets_NamedArgumentsExistOnTheLibraryMethods()
        {
            var problems = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(Root(), "docs", "guides"), "*.md"))
                problems.AddRange(Check(File.ReadAllText(file)).Select(p => Path.GetFileName(file) + ": " + p));
            Assert.True(problems.Count == 0, "snippet calls with argument names no overload has:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void Check_FindsTheWrongNamesOfTheOldRemeshingSnippet()
        {
            string old = "```csharp\nMeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, mesh, creaseAngleDeg: 40);\n" +
                "MeshConstraintUtil.PinSharpVertices(remesher, mesh, minAngleDeficitDeg: 75);\n```\n";
            Assert.Equal(2, Check(old).Count);
            string fixedSnippet = "```csharp\nMeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, creaseAngle: 40);\n" +
                "MeshConstraintUtil.PinSharpVertices(remesher, minAngleDeficit: 75);\n```\n";
            Assert.Empty(Check(fixedSnippet));
        }
    }
}
