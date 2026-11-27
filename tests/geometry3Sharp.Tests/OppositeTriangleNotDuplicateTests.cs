using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #1556 (owner decision 2026-11-25): in DMesh3Builder, a triangle with the same three vertices as an existing
    /// one but the opposite orientation is not a duplicate. Both triangles are kept, whatever their order in the file;
    /// a same-orientation triangle is still a duplicate under DuplicateTriBehavior. The meshes are generated here as
    /// OBJ text: a welded box of 12 triangles plus a two-sided flap (two coincident, opposite triangles) on one box edge.
    /// Public API only, so this file compiles against master's library.
    /// </summary>
    public class OppositeTriangleNotDuplicateTests
    {
        // box corners 0..7 (OBJ index = label + 1), flap apex 8
        static readonly Vector3d[] Corners = {
            new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0),
            new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(1, 1, 1), new Vector3d(0, 1, 1),
            new Vector3d(0.5, -1, 0.5),
        };

        // outward box triangles; edge (0,1) is used by (0,1,5) as 0->1 and by (0,2,1) as 1->0
        static readonly int[][] BoxTris = {
            new[] { 0, 2, 1 }, new[] { 0, 3, 2 },     // z = 0
            new[] { 4, 5, 6 }, new[] { 4, 6, 7 },     // z = 1
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 },     // y = 0
            new[] { 3, 6, 2 }, new[] { 3, 7, 6 },     // y = 1
            new[] { 0, 4, 7 }, new[] { 0, 7, 3 },     // x = 0
            new[] { 1, 2, 6 }, new[] { 1, 6, 5 },     // x = 1
        };

        // a two-sided flap on box edge (0,1): the same three vertices in opposite orientations
        static readonly int[][] Flap = { new[] { 0, 1, 8 }, new[] { 1, 0, 8 } };

        static string Obj(IEnumerable<int[]> tris, Similarity s)
        {
            var sb = new StringBuilder();
            foreach (Vector3d c in Corners) {
                Vector3d p = s.Apply(c);
                sb.Append("v ").Append(p.x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(p.y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(p.z.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            foreach (int[] t in tris)
                sb.Append("f ").Append(t[0] + 1).Append(' ').Append(t[1] + 1).Append(' ').Append(t[2] + 1).Append('\n');
            return sb.ToString();
        }

        static DMesh3 ReadObj(string text)
        {
            var builder = new DMesh3Builder();
            var reader = new StandardMeshReader { MeshBuilder = builder };
            IOReadResult r = reader.Read(new MemoryStream(Encoding.ASCII.GetBytes(text)), "obj", ReadOptions.Defaults);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Single(builder.Meshes);
            return builder.Meshes[0];
        }

        static IEnumerable<int[]> PairFirst() => Flap.Concat(BoxTris);
        static IEnumerable<int[]> PairLast() => BoxTris.Concat(Flap);

        /// <summary> The mesh's triangles as input labels (found by exact position), each rotated to start at
        /// its smallest label, sorted. Orientation is kept. </summary>
        static List<string> LabelTriangles(DMesh3 mesh, Similarity s)
        {
            var labelOf = new Dictionary<Vector3d, int>();
            for (int i = 0; i < Corners.Length; ++i)
                labelOf.Add(s.Apply(Corners[i]), i);
            var result = new List<string>();
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(tid);
                int[] l = { labelOf[mesh.GetVertex(tv.a)], labelOf[mesh.GetVertex(tv.b)], labelOf[mesh.GetVertex(tv.c)] };
                int m = Array.IndexOf(l, l.Min());
                result.Add(l[m] + "," + l[(m + 1) % 3] + "," + l[(m + 2) % 3]);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        static List<string> ExpectedLabels(IEnumerable<int[]> tris)
        {
            var result = tris.Select(l => {
                int m = Array.IndexOf(l, l.Min());
                return l[m] + "," + l[(m + 1) % 3] + "," + l[(m + 2) % 3];
            }).ToList();
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary> Closed by position: every undirected edge (by label) is used as often in one direction as in the other. </summary>
        static bool ClosedByPosition(List<string> labelTris)
        {
            var balance = new Dictionary<(int, int), int>();
            foreach (string t in labelTris) {
                int[] l = t.Split(',').Select(int.Parse).ToArray();
                for (int e = 0; e < 3; ++e) {
                    int a = l[e], b = l[(e + 1) % 3];
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    balance.TryGetValue(key, out int v);
                    balance[key] = v + (a < b ? 1 : -1);
                }
            }
            return balance.Values.All(v => v == 0);
        }

        static void AssertValidFinite(DMesh3 mesh)
        {
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z));
            }
        }

        [Fact]
        public void Obj_OppositeFlapFirst_KeepsAll14Triangles()
        {
            // fails on master: the second flap triangle was discarded as a duplicate (13 triangles, the flap open)
            DMesh3 mesh = ReadObj(Obj(PairFirst(), Similarity.Identity));
            AssertValidFinite(mesh);
            Assert.Equal(14, mesh.TriangleCount);
            List<string> labels = LabelTriangles(mesh, Similarity.Identity);
            Assert.Equal(ExpectedLabels(PairFirst()), labels);
            Assert.True(ClosedByPosition(labels));
        }

        [Fact]
        public void Obj_OppositeFlapFirstOrLast_GiveTheSameTriangles()
        {
            DMesh3 first = ReadObj(Obj(PairFirst(), Similarity.Identity));
            DMesh3 last = ReadObj(Obj(PairLast(), Similarity.Identity));
            AssertValidFinite(first);
            AssertValidFinite(last);
            Assert.Equal(14, last.TriangleCount);
            Assert.Equal(last.TriangleCount, first.TriangleCount);
            Assert.Equal(last.VertexCount, first.VertexCount);
            List<string> lf = LabelTriangles(first, Similarity.Identity), ll = LabelTriangles(last, Similarity.Identity);
            Assert.Equal(ll, lf);
            Assert.True(ClosedByPosition(lf));
            // the box's 12 triangles are all there (closed by position; the connectivity differs between the orders:
            // first, the flap stays welded and one box triangle at the edge gets its own vertices; last, the flap does)
            Assert.Equal(ExpectedLabels(BoxTris), lf.Where(t => t.Contains('8') == false).ToList());
        }

        [Fact]
        public void Obj_SameOrientationDuplicate_IsStillDiscarded()
        {
            // the flap's first triangle again, as given and rotated: both are the same oriented triangle and are
            // discarded, before the box (where the flap shares the box vertices) and after it (where the non-manifold
            // rule gave the flap its own vertices; until #1556 each repeat got another copy there)
            int[][] dups = { Flap[0], new[] { 1, 8, 0 } };
            int[][] one = { Flap[0] };
            foreach (IEnumerable<int[]> order in new[] { one.Concat(dups).Concat(BoxTris), BoxTris.Concat(one).Concat(dups) }) {
                DMesh3 mesh = ReadObj(Obj(order, Similarity.Identity));
                AssertValidFinite(mesh);
                Assert.Equal(13, mesh.TriangleCount);
                Assert.Equal(ExpectedLabels(BoxTris.Concat(one)), LabelTriangles(mesh, Similarity.Identity));
            }

            // a box triangle again (as given and rotated), before or after the box: discarded
            int[] t = BoxTris[3];
            int[][] boxDups = { t, new[] { t[2], t[0], t[1] } };
            foreach (IEnumerable<int[]> tris in new[] { boxDups.Concat(BoxTris), BoxTris.Concat(boxDups) }) {
                DMesh3 box = ReadObj(Obj(tris, Similarity.Identity));
                AssertValidFinite(box);
                Assert.Equal(12, box.TriangleCount);
                Assert.Equal(9, box.VertexCount);           // the 8 corners and the unused apex
                Assert.True(box.IsClosed());
                Assert.Equal(ExpectedLabels(BoxTris), LabelTriangles(box, Similarity.Identity));
            }
        }

        [Fact]
        public void Builder_OppositeTriangle_IsNewTriangle_SameOrientation_ReturnsExisting()
        {
            var builder = new DMesh3Builder();
            builder.AppendNewMesh(false, false, false, false);
            int a = builder.AppendVertex(0, 0, 0), b = builder.AppendVertex(1, 0, 0), c = builder.AppendVertex(0, 1, 0);
            int t0 = builder.AppendTriangle(a, b, c);
            Assert.True(t0 >= 0);
            Assert.Equal(t0, builder.AppendTriangle(b, c, a));          // same orientation: duplicate, discarded
            Assert.Equal(t0, builder.AppendTriangle(c, a, b));
            int t1 = builder.AppendTriangle(b, a, c);                    // opposite: a new triangle with its own vertices
            Assert.True(t1 >= 0);
            Assert.NotEqual(t0, t1);
            DMesh3 mesh = builder.Meshes[0];
            Assert.Equal(2, mesh.TriangleCount);
            Assert.Equal(6, mesh.VertexCount);
            Index3i tv = mesh.GetTriangle(t1);
            Assert.Equal(mesh.GetVertex(b), mesh.GetVertex(tv.a));
            Assert.Equal(mesh.GetVertex(a), mesh.GetVertex(tv.b));
            Assert.Equal(mesh.GetVertex(c), mesh.GetVertex(tv.c));
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            // the first triangle again: still a same-orientation duplicate of t0
            Assert.Equal(t0, builder.AppendTriangle(a, b, c));
            Assert.Equal(2, mesh.TriangleCount);
        }

        [Fact]
        public void Builder_OppositeTriangle_WithNonManifoldDiscard_IsRefused()
        {
            // NonManifoldTriBehavior = DiscardTriangle: the opposite triangle takes the non-manifold path, so it is
            // refused (NonManifoldID), as the flap is when it comes after the box
            var builder = new DMesh3Builder { NonManifoldTriBehavior = DMesh3Builder.AddTriangleFailBehaviors.DiscardTriangle };
            builder.AppendNewMesh(false, false, false, false);
            int a = builder.AppendVertex(0, 0, 0), b = builder.AppendVertex(1, 0, 0), c = builder.AppendVertex(0, 1, 0);
            int t0 = builder.AppendTriangle(a, b, c);
            Assert.True(t0 >= 0);
            Assert.Equal(DMesh3.NonManifoldID, builder.AppendTriangle(b, a, c));
            Assert.Equal(1, builder.Meshes[0].TriangleCount);
        }

        [Fact]
        public void Obj_InwardCopyOfABoxTriangle_IsKept()
        {
            // the box triangle (0,2,1) once more, reversed: the box edge (0,1) also carries (0,1,5) in the orientation
            // 0->1, which is not the same triangle; the reversed copy is kept with its own vertices
            int[] reversed = { 0, 1, 2 };
            DMesh3 mesh = ReadObj(Obj(BoxTris.Concat(new[] { reversed }), Similarity.Identity));
            AssertValidFinite(mesh);
            Assert.Equal(13, mesh.TriangleCount);
            List<string> labels = LabelTriangles(mesh, Similarity.Identity);
            Assert.Equal(ExpectedLabels(BoxTris.Concat(new[] { reversed })), labels);
        }

        [Fact]
        public void Builder_ExistingMeshWithBothOrientations_ReturnsTheSameOrientedTriangle()
        {
            // a mesh built directly with DMesh3 can hold both orientations on one edge; the builder's duplicate is
            // the triangle of the same orientation, not the first one FindTriangle meets
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0)), b = mesh.AppendVertex(new Vector3d(1, 0, 0)), c = mesh.AppendVertex(new Vector3d(0, 1, 0));
            int t0 = mesh.AppendTriangle(a, b, c);
            int t1 = mesh.AppendTriangle(b, a, c);
            Assert.True(t0 >= 0 && t1 >= 0);
            var builder = new DMesh3Builder();
            builder.AppendNewMesh(mesh);
            Assert.Equal(t0, builder.AppendTriangle(a, b, c));
            Assert.Equal(t1, builder.AppendTriangle(b, a, c));
            Assert.Equal(t1, builder.AppendTriangle(c, b, a));
            Assert.Equal(2, mesh.TriangleCount);
            Assert.Equal(3, mesh.VertexCount);
        }

        [Fact]
        public void Builder_DuplicateAllVertices_DoesNotApplyToTheOppositeTriangle()
        {
            // DuplicateTriBehavior = DuplicateAllVertices copies a same-orientation duplicate; the opposite triangle
            // follows NonManifoldTriBehavior (here: refused), not DuplicateTriBehavior
            var builder = new DMesh3Builder {
                DuplicateTriBehavior = DMesh3Builder.AddTriangleFailBehaviors.DuplicateAllVertices,
                NonManifoldTriBehavior = DMesh3Builder.AddTriangleFailBehaviors.DiscardTriangle };
            builder.AppendNewMesh(false, false, false, false);
            int a = builder.AppendVertex(0, 0, 0), b = builder.AppendVertex(1, 0, 0), c = builder.AppendVertex(0, 1, 0);
            int t0 = builder.AppendTriangle(a, b, c);
            Assert.True(t0 >= 0);
            Assert.Equal(DMesh3.NonManifoldID, builder.AppendTriangle(b, a, c));
            Assert.Equal(1, builder.Meshes[0].TriangleCount);
            int t1 = builder.AppendTriangle(b, c, a);                    // same orientation: a copy with its own vertices
            Assert.True(t1 >= 0 && t1 != t0);
            Assert.Equal(2, builder.Meshes[0].TriangleCount);
            Assert.Equal(6, builder.Meshes[0].VertexCount);

            // and the reverse mix: the opposite triangle gets its own vertices, a same-orientation one is discarded
            var builder2 = new DMesh3Builder { NonManifoldTriBehavior = DMesh3Builder.AddTriangleFailBehaviors.DuplicateAllVertices };
            builder2.AppendNewMesh(false, false, false, false);
            a = builder2.AppendVertex(0, 0, 0); b = builder2.AppendVertex(1, 0, 0); c = builder2.AppendVertex(0, 1, 0);
            t0 = builder2.AppendTriangle(a, b, c);
            Assert.True(builder2.AppendTriangle(c, b, a) > t0);
            Assert.Equal(t0, builder2.AppendTriangle(c, a, b));
            Assert.Equal(2, builder2.Meshes[0].TriangleCount);
        }

        [Fact]
        public void Builder_DegenerateTriangleOnExistingEdge_StillReturnsExisting()
        {
            // a degenerate (a,b,a) has no orientation: unchanged, FindTriangle's triangle is returned
            var builder = new DMesh3Builder();
            builder.AppendNewMesh(false, false, false, false);
            int a = builder.AppendVertex(0, 0, 0), b = builder.AppendVertex(1, 0, 0), c = builder.AppendVertex(0, 1, 0);
            int t0 = builder.AppendTriangle(a, b, c);
            Assert.Equal(t0, builder.AppendTriangle(b, a, b));
            Assert.Equal(1, builder.Meshes[0].TriangleCount);
            Assert.Equal(3, builder.Meshes[0].VertexCount);
        }

        // ---------------------------------------------------------------- repeated copies of a detached triangle

        [Fact]
        public void Builder_RepeatedOppositeTriangle_IsADuplicateOfItsCopy()
        {
            // (b,a,c) twice and once rotated as (a,c,b): one copy with its own vertices, the repeats return it
            var builder = new DMesh3Builder();
            builder.AppendNewMesh(false, false, false, false);
            int a = builder.AppendVertex(0, 0, 0), b = builder.AppendVertex(1, 0, 0), c = builder.AppendVertex(0, 1, 0);
            int t0 = builder.AppendTriangle(a, b, c);
            int t1 = builder.AppendTriangle(b, a, c);
            Assert.True(t0 >= 0 && t1 >= 0 && t0 != t1);
            Assert.Equal(t1, builder.AppendTriangle(b, a, c));
            Assert.Equal(t1, builder.AppendTriangle(a, c, b));
            Assert.Equal(t1, builder.AppendTriangle(c, b, a));
            Assert.Equal(t0, builder.AppendTriangle(c, a, b));
            DMesh3 mesh = builder.Meshes[0];
            Assert.Equal(2, mesh.TriangleCount);
            Assert.Equal(6, mesh.VertexCount);
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void Builder_RepeatedOppositeTriangle_WithDuplicateAllVertices_IsCopiedAgain()
        {
            // DuplicateTriBehavior = DuplicateAllVertices: a repeat of the detached copy is a duplicate, so it is copied
            var builder = new DMesh3Builder { DuplicateTriBehavior = DMesh3Builder.AddTriangleFailBehaviors.DuplicateAllVertices };
            builder.AppendNewMesh(false, false, false, false);
            int a = builder.AppendVertex(0, 0, 0), b = builder.AppendVertex(1, 0, 0), c = builder.AppendVertex(0, 1, 0);
            builder.AppendTriangle(a, b, c);
            int t1 = builder.AppendTriangle(b, a, c);
            int t2 = builder.AppendTriangle(b, a, c);
            Assert.True(t1 >= 0 && t2 >= 0 && t1 != t2);
            Assert.Equal(3, builder.Meshes[0].TriangleCount);
            Assert.Equal(9, builder.Meshes[0].VertexCount);
        }

        [Fact]
        public void Builder_RemovedDetachedCopy_IsNotReturnedAsDuplicate()
        {
            // the record of a detached copy counts only while the copy is still there
            var builder = new DMesh3Builder();
            builder.AppendNewMesh(false, false, false, false);
            int a = builder.AppendVertex(0, 0, 0), b = builder.AppendVertex(1, 0, 0), c = builder.AppendVertex(0, 1, 0);
            builder.AppendTriangle(a, b, c);
            int t1 = builder.AppendTriangle(b, a, c);
            DMesh3 mesh = builder.Meshes[0];
            Assert.Equal(MeshResult.Ok, mesh.RemoveTriangle(t1, true, false));
            int t2 = builder.AppendTriangle(b, a, c);
            Assert.True(t2 >= 0 && mesh.IsTriangle(t2));
            Assert.Equal(2, mesh.TriangleCount);
            Assert.Equal(t2, builder.AppendTriangle(b, a, c));
            Assert.Equal(2, mesh.TriangleCount);

            // a copy whose vertex was moved is not the same triangle any more
            Index3i tv = mesh.GetTriangle(t2);
            mesh.SetVertex(tv.c, new Vector3d(0, 2, 0));
            int t3 = builder.AppendTriangle(b, a, c);
            Assert.True(t3 >= 0 && t3 != t2);
            Assert.Equal(3, mesh.TriangleCount);
        }

        [Fact]
        public void Obj_RepeatedOppositeFlapTriangle_BothOrders_Keeps14()
        {
            // the flap's opposite side three times (as given, again, rotated): one copy, in either order
            int[][] repeats = { Flap[1], new[] { 0, 8, 1 } };
            foreach (bool pairFirst in new[] { true, false }) {
                IEnumerable<int[]> tris = pairFirst ? PairFirst().Concat(repeats) : PairLast().Concat(repeats);
                DMesh3 mesh = ReadObj(Obj(tris, Similarity.Identity));
                AssertValidFinite(mesh);
                Assert.Equal(14, mesh.TriangleCount);
                List<string> labels = LabelTriangles(mesh, Similarity.Identity);
                Assert.Equal(ExpectedLabels(PairFirst()), labels);
                Assert.True(ClosedByPosition(labels));
            }
        }

        [Fact]
        public void Obj_BothFlapSidesDetached_RepeatsOfEitherSideAreDuplicates()
        {
            // after the box both flap sides get their own vertices; a repeat of either side (the one recorded first
            // and the one recorded last) is a duplicate of its own copy, not of the other side
            int[][] repeats = { Flap[0], Flap[1], new[] { 8, 0, 1 }, new[] { 8, 1, 0 } };
            DMesh3 mesh = ReadObj(Obj(PairLast().Concat(repeats), Similarity.Identity));
            AssertValidFinite(mesh);
            Assert.Equal(14, mesh.TriangleCount);
            Assert.Equal(15, mesh.VertexCount);         // 9 input vertices and the 6 of the two detached flap sides
            List<string> labels = LabelTriangles(mesh, Similarity.Identity);
            Assert.Equal(ExpectedLabels(PairLast()), labels);
            Assert.True(ClosedByPosition(labels));
        }

        [Fact]
        public void Stl_TwoSidedTriangleWithRepeatedBackSide_KeepsTwoTriangles()
        {
            // ASCII STL: ABC, BAC, BAC. Master kept 1 (the back side discarded); the back side is kept once
            var A = new Vector3d(0, 0, 0); var B = new Vector3d(1, 0, 0); var C = new Vector3d(0, 1, 0);
            Vector3d[][] facets = { new[] { A, B, C }, new[] { B, A, C }, new[] { B, A, C } };
            var sb = new StringBuilder("solid flap\n");
            foreach (Vector3d[] f in facets) {
                sb.Append("facet normal 0 0 0\nouter loop\n");
                foreach (Vector3d v in f)
                    sb.Append("vertex ").Append(v.x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                      .Append(v.y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                      .Append(v.z.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("endloop\nendfacet\n");
            }
            sb.Append("endsolid flap\n");
            foreach (STLReader.Strategy strategy in new[] { STLReader.Strategy.AutoBestResult, STLReader.Strategy.IdenticalVertexWeld, STLReader.Strategy.TolerantVertexWeld }) {
                var builder = new DMesh3Builder();
                var reader = new STLReader { RebuildStrategy = strategy };
                IOReadResult r = reader.Read(new StringReader(sb.ToString()), ReadOptions.Defaults, builder);
                Assert.Equal(IOCode.Ok, r.code);
                Assert.Single(builder.Meshes);
                DMesh3 mesh = builder.Meshes[0];
                Assert.Equal(2, mesh.TriangleCount);
                AssertValidFinite(mesh);
                var normals = mesh.TriangleIndices().Select(t => mesh.GetTriNormal(t).z).OrderBy(z => z).ToArray();
                Assert.Equal(new[] { -1.0, 1.0 }, normals);
            }
        }

        public static IEnumerable<object[]> Similarities() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Obj_OppositeFlap_BothOrders_InvariantUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (bool pairFirst in new[] { true, false }) {
                IEnumerable<int[]> tris = pairFirst ? PairFirst() : PairLast();
                DMesh3 reference = ReadObj(Obj(tris, Similarity.Identity));
                DMesh3 mesh = ReadObj(Obj(tris, s));
                AssertValidFinite(mesh);
                Assert.Equal(14, mesh.TriangleCount);
                // the builder's result is topological: the same triangles with the same vertex IDs, bit for bit
                Assert.Equal(reference.TriangleCount, mesh.TriangleCount);
                Assert.Equal(reference.VertexCount, mesh.VertexCount);
                foreach (int tid in reference.TriangleIndices())
                    Assert.Equal(reference.GetTriangle(tid), mesh.GetTriangle(tid));
                Assert.Equal(LabelTriangles(reference, Similarity.Identity), LabelTriangles(mesh, s));
            }
        }
    }
}
