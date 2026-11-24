using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The open items O2 to O4 of stage 1 of the determinism proof (docs/design/DETERMINISM_PROOF.md section 5.4): the mutable statics of
    /// the scope are exactly the registered test knobs and only tests write them (O2), the dictionary of `SnapRounding2` that no consumer
    /// may enumerate is not visible (O3), and the index tie-break of the parallel sort's merge, which no run of the sort can reach, is
    /// attacked directly (O4).
    /// </summary>
    public class DeterminismStage1ObligationTests
    {
        static IEnumerable<string> ScopeFiles() { return DeterminismScopeAnalyzerTests.Kernel.Concat(DeterminismScopeAnalyzerTests.Stage1); }

        static SyntaxTree Parse(string rel) { return CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ExactCodeAnalyzerTests.RepoRoot(), rel))); }

        // the metadata names of the types that the scope files declare (nested types with '+', generic ones with their arity)
        static List<string> ScopeTypeNames()
        {
            var names = new List<string>();
            foreach (string f in ScopeFiles())
                foreach (var t in Parse(f).GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()) {
                    var parts = new List<string>();
                    string ns = null;
                    for (SyntaxNode n = t; n != null; n = n.Parent) {
                        if (n is BaseTypeDeclarationSyntax b) {
                            int arity = (b as TypeDeclarationSyntax)?.TypeParameterList?.Parameters.Count ?? 0;
                            parts.Insert(0, b.Identifier.Text + (arity > 0 ? "`" + arity : ""));
                        } else if (n is BaseNamespaceDeclarationSyntax ns0) ns = ns0.Name.ToString();
                    }
                    names.Add((ns != null ? ns + "." : "") + string.Join("+", parts));
                }
            return names.Distinct().ToList();
        }

        /// <summary> the mutable statics of the scope that are not [ThreadStatic] (those are registered site by site, R14) </summary>
        static readonly string[] Knobs = {
            "g3.TriangleBoxTree.ParallelBuildFrom", "g3.TriangleBoxTree.PermutationSeedForTests", "g3.TriangleBoxTree.PermutedTrees",
            "g3.ExactMeshBoolean.LibmPerturbForTests", "g3.ExactMeshBoolean.LibmPerturbRandomForTests", "g3.ExactMeshBoolean.LibmPerturbMaskForTests" };

        // ---- O2: the mutable statics of the scope ----

        [Fact]
        public void MutableStatics_OfTheScope_AreExactlyTheKnobs()
        {
            var asm = typeof(DMesh3).Assembly;
            var found = new List<string>();
            var names = ScopeTypeNames();
            Assert.Contains("g3.TriangleBoxTree", names);
            Assert.Contains("g3.ExactMeshBoolean", names);
            foreach (string name in names) {
                Type t = asm.GetType(name);
                Assert.True(t != null, name);
                foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.IsLiteral && !f.IsInitOnly && f.GetCustomAttributes(typeof(ThreadStaticAttribute), false).Length == 0)
                        found.Add(name + "." + f.Name);
            }
            // (a new mutable static in the scope needs an argument: an entry here and a sentence in DETERMINISM_PROOF.md O2)
            Assert.Equal(Knobs.OrderBy(x => x, StringComparer.Ordinal), found.OrderBy(x => x, StringComparer.Ordinal));
            // and they are all internal: no consumer of the package can write them
            foreach (string k in Knobs) {
                int dot = k.LastIndexOf('.');
                FieldInfo f = asm.GetType(k.Substring(0, dot)).GetField(k.Substring(dot + 1), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.True(!f.IsPublic && !f.IsFamily && !f.IsFamilyOrAssembly, k);
            }
        }

        // The library never writes a knob: only a test does. The one exception is the counter PermutedTrees, which `permute_for_tests`
        // increments after it has permuted (it runs only for a seed that a test set, and nothing reads the counter for a result).
        [Fact]
        public void Knobs_AreNotWrittenByTheScope()
        {
            var short_ = Knobs.Select(k => k.Substring(k.LastIndexOf('.') + 1)).ToList();
            var writes = new List<string>();
            foreach (string f in ScopeFiles()) {
                foreach (var id in Parse(f).GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()) {
                    if (!short_.Contains(id.Identifier.Text)) continue;
                    SyntaxNode p = id.Parent;
                    // a qualified use (TriangleBoxTree.ParallelBuildFrom) is a member access: look at it from the access on
                    SyntaxNode e = (p is MemberAccessExpressionSyntax ma && ma.Name == id) ? p : id;
                    SyntaxNode up = e.Parent;
                    bool write = (up is AssignmentExpressionSyntax a && a.Left == e)
                        || up is PostfixUnaryExpressionSyntax || (up is PrefixUnaryExpressionSyntax pu && (pu.IsKind(SyntaxKind.PreIncrementExpression) || pu.IsKind(SyntaxKind.PreDecrementExpression)))
                        || (up is ArgumentSyntax arg && !arg.RefKindKeyword.IsKind(SyntaxKind.None));
                    if (!write) continue;
                    var method = id.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    bool allowed = id.Identifier.Text == "PermutedTrees" && method != null && method.Identifier.Text == "permute_for_tests";
                    if (!allowed) writes.Add(f + ": " + up.ToString());
                }
            }
            Assert.Empty(writes);
            // (the exception exists, so that the scan is seen to find writes at all)
            Assert.Contains(Parse("mesh_ops/ExactMeshArrangement.cs").GetRoot().DescendantNodes().OfType<PostfixUnaryExpressionSyntax>(),
                x => x.Operand.ToString() == "PermutedTrees");
        }

        // the shared empty collections of an untouched input triangle: a write through one would reach every untouched triangle on every thread
        [Fact]
        public void SharedEmptyCollections_StayEmpty_AfterABoolean()
        {
            var nested = typeof(ExactMeshArrangement).GetNestedType("InputTriangle", BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(nested);
            var shared = new[] { "noSegments", "noPoints", "noCoplanar" }
                .Select(n => nested.GetField(n, BindingFlags.Static | BindingFlags.NonPublic)).ToList();
            Assert.DoesNotContain(null, shared);
            // two cubes crossing (some triangles meet segments, most do not), then three meshes, whose segments cross inside a triangle
            Func<double, double, double, DMesh3> box = (x, y, z) => {
                DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
                MeshTransforms.Translate(m, new Vector3d(x, y, z));
                return m;
            };
            var op = new ExactMeshBoolean(new[] { box(0, 0, 0), box(0.5, 0.5, 0.5), box(0.25, 0.75, 0.25) }, ExactMeshBoolean.Operations.Union);
            op.Compute();
            foreach (FieldInfo f in shared)
                {
                object list = f.GetValue(null);
                Assert.Equal(0, (int)list.GetType().GetProperty("Count").GetValue(list));
            }
        }

        // ---- O3: SnapRounding2.Origin ----

        [Fact]
        public void SnapRounding2_Origin_IsNotVisible()
        {
            // a Dictionary has no specified enumeration order; the members TryOrigin and Origins only look entries up, and Build adds in chain order.
            // Not public: no consumer of the class, inside or outside the assembly, can enumerate it (inside the class the analyzer registers
            // every enumeration, R8)
            Type t = typeof(SnapRounding2);
            FieldInfo f = t.GetField("Origin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(f);
            Assert.True(f.IsPrivate);
            Assert.Null(t.GetProperty("Origin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        // ---- O4: the index tie-break of the merge ----

        [Fact]
        public void MergeRuns_EqualCodes_GoByBoxIndex()
        {
            // the runs of the parallel sort are ranges of ascending box indices, so there the left run holds the smaller index and the
            // tie-break never decides; merge_runs is a total order on (code, index) for any two runs that are sorted by it
            long[] c = { 5, 5, 5, 5 };
            int[] o = { 7, 9, 2, 3 };
            var dc = new long[4]; var dord = new int[4];
            TriangleBoxTree.merge_runs(c, o, dc, dord, 0, 2, 4);
            Assert.Equal(new[] { 2, 3, 7, 9 }, dord);
            Assert.Equal(c, dc);
            // codes first, then the index; a short right run, and a run of one element
            long[] c2 = { 1, 4, 4, 2, 4 };
            int[] o2 = { 8, 3, 6, 9, 1 };
            var dc2 = new long[5]; var dord2 = new int[5];
            TriangleBoxTree.merge_runs(c2, o2, dc2, dord2, 0, 3, 5);
            Assert.Equal(new long[] { 1, 2, 4, 4, 4 }, dc2);
            Assert.Equal(new[] { 8, 9, 1, 3, 6 }, dord2);
        }
    }
}
