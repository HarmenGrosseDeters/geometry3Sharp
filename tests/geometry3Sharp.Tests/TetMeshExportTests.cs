using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage P2 of docs/design/TET_MESHING.md: the writers of TetMeshWriter (Medit, Gmsh 4.1 and 2.2, VTK legacy), TetMeshMarkers and the
    /// quality target check (P5(a)). The files are checked through a minimal parser written for the test, independent of the writers:
    /// counts, indices (1-based in Medit and Gmsh, 0-based in VTK), orientation of every tet, references.
    /// </summary>
    public class TetMeshExportTests
    {
        // ---------------------------------------------------------------- the test-only parser

        sealed class Parsed
        {
            public List<Vector3d> V = new List<Vector3d>();
            public List<int[]> Tets = new List<int[]>(), Tris = new List<int[]>();     // 0-based
            public List<int> TetRef = new List<int>(), TriRef = new List<int>();
            public int MinRawIndex = int.MaxValue;                                       // the smallest index as written
            public int DeclaredVertices, DeclaredTets, DeclaredTris;
            public string Version;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static double D(string s) => MeshIOUtil.ParseDouble(s);
        static int I(string s) => int.Parse(s, Inv);
        static string[] Tok(string s) => s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        static Parsed Parse(string text, TetMeshWriter.Format f)
        {
            Assert.DoesNotContain("\r", text);
            Assert.EndsWith("\n", text);
            switch (f) {
                case TetMeshWriter.Format.Medit: return ParseMedit(text);
                case TetMeshWriter.Format.Msh22: return ParseMsh22(text);
                case TetMeshWriter.Format.Msh41: return ParseMsh41(text);
                default: return ParseVtk(text);
            }
        }

        static Parsed ParseMedit(string text)
        {
            var p = new Parsed();
            string[] t = Tok(text);
            int k = 0;
            Assert.Equal("MeshVersionFormatted", t[k++]); p.Version = t[k++]; Assert.Equal("2", p.Version);
            Assert.Equal("Dimension", t[k++]); Assert.Equal("3", t[k++]);
            string last = null;
            while (k < t.Length) {
                string key = t[k++];
                if (key == "End") { last = key; break; }
                int n = I(t[k++]);
                if (key == "Vertices") {
                    p.DeclaredVertices = n;
                    for (int i = 0; i < n; ++i) { p.V.Add(new Vector3d(D(t[k]), D(t[k + 1]), D(t[k + 2]))); Assert.Equal("0", t[k + 3]); k += 4; }
                } else if (key == "Triangles") {
                    p.DeclaredTris = n;
                    for (int i = 0; i < n; ++i) { p.Tris.Add(new[] { I(t[k]) - 1, I(t[k + 1]) - 1, I(t[k + 2]) - 1 }); p.MinRawIndex = Math.Min(p.MinRawIndex, Math.Min(I(t[k]), Math.Min(I(t[k + 1]), I(t[k + 2])))); p.TriRef.Add(I(t[k + 3])); k += 4; }
                } else if (key == "Tetrahedra") {
                    p.DeclaredTets = n;
                    for (int i = 0; i < n; ++i) {
                        p.Tets.Add(new[] { I(t[k]) - 1, I(t[k + 1]) - 1, I(t[k + 2]) - 1, I(t[k + 3]) - 1 });
                        p.MinRawIndex = Math.Min(p.MinRawIndex, Math.Min(Math.Min(I(t[k]), I(t[k + 1])), Math.Min(I(t[k + 2]), I(t[k + 3]))));
                        p.TetRef.Add(I(t[k + 4])); k += 5;
                    }
                } else Assert.Fail("unknown keyword " + key);
            }
            Assert.Equal("End", last);
            Assert.Equal(t.Length, k);
            return p;
        }

        static string[] Lines(string text) => text.Split('\n').Take(text.Split('\n').Length - 1).ToArray();

        static Parsed ParseMsh22(string text)
        {
            var p = new Parsed();
            string[] L = Lines(text);
            int k = 0;
            Assert.Equal("$MeshFormat", L[k++]); Assert.Equal("2.2 0 8", L[k++]); p.Version = "2.2"; Assert.Equal("$EndMeshFormat", L[k++]);
            Assert.Equal("$Nodes", L[k++]);
            int nn = I(L[k++]); p.DeclaredVertices = nn;
            for (int i = 0; i < nn; ++i) { string[] f = Tok(L[k++]); Assert.Equal(i + 1, I(f[0])); p.V.Add(new Vector3d(D(f[1]), D(f[2]), D(f[3]))); }
            Assert.Equal("$EndNodes", L[k++]);
            Assert.Equal("$Elements", L[k++]);
            int ne = I(L[k++]);
            var byId = new SortedDictionary<int, string[]>();
            for (int i = 0; i < ne; ++i) { string[] f = Tok(L[k++]); byId.Add(I(f[0]), f); }
            Assert.Equal("$EndElements", L[k++]);
            Assert.Equal(L.Length, k);
            Assert.Equal(Enumerable.Range(1, ne), byId.Keys);
            foreach (string[] f in byId.Values) {
                int type = I(f[1]), ntags = I(f[2]);
                Assert.Equal(2, ntags);
                Assert.Equal(f[3], f[4]);                                               // physical = elementary
                int[] nodes = f.Skip(3 + ntags).Select(x => I(x)).ToArray();
                p.MinRawIndex = Math.Min(p.MinRawIndex, nodes.Min());
                if (type == 4) { Assert.Equal(4, nodes.Length); p.Tets.Add(nodes.Select(x => x - 1).ToArray()); p.TetRef.Add(I(f[3])); }
                else { Assert.Equal(2, type); Assert.Equal(3, nodes.Length); p.Tris.Add(nodes.Select(x => x - 1).ToArray()); p.TriRef.Add(I(f[3])); }
            }
            p.DeclaredTets = p.Tets.Count; p.DeclaredTris = p.Tris.Count;
            return p;
        }

        static Parsed ParseMsh41(string text)
        {
            var p = new Parsed();
            string[] L = Lines(text);
            int k = 0;
            Assert.Equal("$MeshFormat", L[k++]); Assert.Equal("4.1 0 8", L[k++]); p.Version = "4.1"; Assert.Equal("$EndMeshFormat", L[k++]);
            Assert.Equal("$Entities", L[k++]);
            string[] c = Tok(L[k++]);
            int np = I(c[0]), nc = I(c[1]), ns = I(c[2]), nv = I(c[3]);
            Assert.Equal(0, np); Assert.Equal(0, nc);
            var physical = new Dictionary<(int, int), int>();                           // (dim, tag) -> physical tag, 0 if none
            for (int dim = 2; dim <= 3; ++dim)
                for (int i = 0; i < (dim == 2 ? ns : nv); ++i) {
                    string[] f = Tok(L[k++]);
                    int tag = I(f[0]);
                    for (int b = 0; b < 3; ++b) Assert.True(D(f[1 + b]) <= D(f[4 + b]));   // a bounding box
                    int nphys = I(f[7]);
                    Assert.InRange(nphys, 0, 1);
                    Assert.Equal(8 + nphys + 1, f.Length);
                    Assert.Equal("0", f[f.Length - 1]);                                    // no bounding entities
                    Assert.False(physical.ContainsKey((dim, tag)));
                    physical[(dim, tag)] = nphys == 1 ? I(f[8]) : 0;
                }
            Assert.Equal("$EndEntities", L[k++]);
            Assert.Equal("$Nodes", L[k++]);
            c = Tok(L[k++]);
            int nBlocks = I(c[0]), nNodes = I(c[1]); p.DeclaredVertices = nNodes;
            if (nNodes > 0) { Assert.Equal("1", c[2]); Assert.Equal(nNodes.ToString(Inv), c[3]); }
            var nodeIds = new List<int>();
            for (int b = 0; b < nBlocks; ++b) {
                string[] h = Tok(L[k++]);
                Assert.Equal("3", h[0]); Assert.True(physical.ContainsKey((3, I(h[1])))); Assert.Equal("0", h[2]);
                int cnt = I(h[3]);
                for (int i = 0; i < cnt; ++i) nodeIds.Add(I(L[k++]));
                for (int i = 0; i < cnt; ++i) { string[] f = Tok(L[k++]); Assert.Equal(3, f.Length); p.V.Add(new Vector3d(D(f[0]), D(f[1]), D(f[2]))); }
            }
            Assert.Equal(Enumerable.Range(1, nNodes), nodeIds);
            Assert.Equal("$EndNodes", L[k++]);
            Assert.Equal("$Elements", L[k++]);
            c = Tok(L[k++]);
            nBlocks = I(c[0]); int nElem = I(c[1]);
            var tets = new SortedDictionary<int, (int[], int)>(); var tris = new SortedDictionary<int, (int[], int)>();
            for (int b = 0; b < nBlocks; ++b) {
                string[] h = Tok(L[k++]);
                int dim = I(h[0]), tag = I(h[1]), type = I(h[2]), cnt = I(h[3]);
                Assert.Equal(dim == 3 ? 4 : 2, type);
                Assert.True(physical.ContainsKey((dim, tag)), "block of an entity that is not declared");
                for (int i = 0; i < cnt; ++i) {
                    string[] f = Tok(L[k++]);
                    Assert.Equal(1 + (dim == 3 ? 4 : 3), f.Length);
                    int[] nodes = f.Skip(1).Select(x => I(x)).ToArray();
                    p.MinRawIndex = Math.Min(p.MinRawIndex, nodes.Min());
                    (dim == 3 ? tets : tris).Add(I(f[0]), (nodes.Select(x => x - 1).ToArray(), physical[(dim, tag)]));
                }
            }
            Assert.Equal("$EndElements", L[k++]);
            Assert.Equal(L.Length, k);
            Assert.Equal(nElem, tets.Count + tris.Count);
            if (nElem > 0) { Assert.Equal("1", c[2]); Assert.Equal(nElem.ToString(Inv), c[3]); }
            Assert.Equal(Enumerable.Range(1, nElem), tets.Keys.Concat(tris.Keys).OrderBy(x => x));    // every element tag once, 1 .. n
            foreach (var kv in tets) { p.Tets.Add(kv.Value.Item1); p.TetRef.Add(kv.Value.Item2); }
            foreach (var kv in tris) { p.Tris.Add(kv.Value.Item1); p.TriRef.Add(kv.Value.Item2); }
            p.DeclaredTets = p.Tets.Count; p.DeclaredTris = p.Tris.Count;
            return p;
        }

        static Parsed ParseVtk(string text)
        {
            var p = new Parsed();
            string[] L = Lines(text);
            int k = 0;
            Assert.Equal("# vtk DataFile Version 3.0", L[k++]); p.Version = "3.0";
            k++;                                                                        // title
            Assert.Equal("ASCII", L[k++]); Assert.Equal("DATASET UNSTRUCTURED_GRID", L[k++]);
            string[] h = Tok(L[k++]);
            Assert.Equal("POINTS", h[0]); Assert.Equal("double", h[2]);
            int n = I(h[1]); p.DeclaredVertices = n;
            for (int i = 0; i < n; ++i) { string[] f = Tok(L[k++]); p.V.Add(new Vector3d(D(f[0]), D(f[1]), D(f[2]))); }
            h = Tok(L[k++]);
            Assert.Equal("CELLS", h[0]);
            int nc = I(h[1]), size = I(h[2]);
            var cells = new List<int[]>();
            int sum = 0;
            for (int i = 0; i < nc; ++i) { string[] f = Tok(L[k++]); int[] c = f.Skip(1).Select(x => I(x)).ToArray(); Assert.Equal(I(f[0]), c.Length); cells.Add(c); sum += f.Length; p.MinRawIndex = Math.Min(p.MinRawIndex, c.Min()); }
            Assert.Equal(size, sum);
            h = Tok(L[k++]);
            Assert.Equal("CELL_TYPES", h[0]); Assert.Equal(nc, I(h[1]));
            var types = new List<int>();
            for (int i = 0; i < nc; ++i) types.Add(I(L[k++]));
            h = Tok(L[k++]);
            Assert.Equal("CELL_DATA", h[0]); Assert.Equal(nc, I(h[1]));
            Assert.Equal("SCALARS ref int 1", L[k++]); Assert.Equal("LOOKUP_TABLE default", L[k++]);
            for (int i = 0; i < nc; ++i) {
                int r = I(L[k++]);
                if (types[i] == 10) { Assert.Equal(4, cells[i].Length); p.Tets.Add(cells[i]); p.TetRef.Add(r); }
                else { Assert.Equal(5, types[i]); Assert.Equal(3, cells[i].Length); p.Tris.Add(cells[i]); p.TriRef.Add(r); }
            }
            Assert.Equal(L.Length, k);
            // tets before triangles
            Assert.Equal(Enumerable.Repeat(10, p.Tets.Count).Concat(Enumerable.Repeat(5, p.Tris.Count)), types);
            p.DeclaredTets = p.Tets.Count; p.DeclaredTris = p.Tris.Count;
            return p;
        }

        static string Text(TetMesh3 m, TetMeshMarkers mk, TetMeshWriter.Format f)
        {
            var w = new StringWriter(Inv);
            TetMeshWriter.Write(w, m, mk, f);
            return w.ToString();
        }

        public static IEnumerable<object[]> Formats() => Enum.GetValues(typeof(TetMeshWriter.Format)).Cast<TetMeshWriter.Format>().Select(f => new object[] { f });

        static bool OneBased(TetMeshWriter.Format f) => f != TetMeshWriter.Format.Vtk;

        // ---------------------------------------------------------------- golden cases (hand-made two-tet mesh)

        // two disjoint tets: vertices 0-3 and 4-7 (shifted by 2 in x); the boundary triangles of each tet, outward
        static TetMesh3 TwoTets(out TetMeshMarkers markers)
        {
            var m = new TetMesh3();
            foreach (double dx in new[] { 0.0, 2.0 }) {
                m.Vertices.Add(new Vector3d(dx, 0, 0)); m.Vertices.Add(new Vector3d(dx + 1, 0, 0));
                m.Vertices.Add(new Vector3d(dx, 1, 0)); m.Vertices.Add(new Vector3d(dx, 0, 1));
            }
            foreach (int o in new[] { 0, 4 }) {
                m.Tets.Add(new Index4i(o, o + 1, o + 2, o + 3));
                m.BoundaryTriangles.Add(new Index3i(o + 1, o + 2, o + 3)); m.BoundaryTriangles.Add(new Index3i(o, o + 3, o + 2));
                m.BoundaryTriangles.Add(new Index3i(o, o + 1, o + 3)); m.BoundaryTriangles.Add(new Index3i(o, o + 2, o + 1));
            }
            markers = new TetMeshMarkers() {
                TetRegion = new[] { 0, 1 }, RegionCount = 2,
                BoundaryMarker = new[] { 0, 0, 3, 3, 3, 3, -1, -1 },
                NearestInputTriangle = new[] { 0, 0, 3, 3, 3, 3, -1, -1 },
                BoundaryDistance = new double[8],
            };
            return m;
        }

        const string VerticesBlock = "0 0 0\n1 0 0\n0 1 0\n0 0 1\n2 0 0\n3 0 0\n2 1 0\n2 0 1\n";

        [Fact]
        public void TwoTets_IsAValidMesh()
        {
            TetMesh3 m = TwoTets(out _);
            Assert.Null(TetMeshQuality.NotValidReason(m));
        }

        [Fact]
        public void Golden_Medit()
        {
            TetMesh3 m = TwoTets(out TetMeshMarkers mk);
            string expected = "MeshVersionFormatted 2\nDimension 3\nVertices\n8\n" + VerticesBlock.Replace("\n", " 0\n") +
                "Triangles\n8\n2 3 4 1\n1 4 3 1\n1 2 4 4\n1 3 2 4\n6 7 8 4\n5 8 7 4\n5 6 8 0\n5 7 6 0\nTetrahedra\n2\n1 2 3 4 1\n5 6 7 8 2\nEnd\n";
            Assert.Equal(expected, Text(m, mk, TetMeshWriter.Format.Medit));
            // without markers every ref is 0, and a mesh without boundary triangles has no Triangles section
            string noMarkers = Text(m, null, TetMeshWriter.Format.Medit);
            Assert.Contains("1 2 3 4 0\n5 6 7 8 0\n", noMarkers);
            Assert.Contains("2 3 4 0\n", noMarkers);
            var bare = new TetMesh3(); bare.Vertices.AddRange(m.Vertices.Take(4)); bare.Tets.Add(m.Tets[0]);
            Assert.DoesNotContain("Triangles", Text(bare, null, TetMeshWriter.Format.Medit));
        }

        [Fact]
        public void Golden_Msh22()
        {
            TetMesh3 m = TwoTets(out TetMeshMarkers mk);
            string nodes = string.Concat(VerticesBlock.TrimEnd('\n').Split('\n').Select((s, i) => (i + 1) + " " + s + "\n"));
            string expected = "$MeshFormat\n2.2 0 8\n$EndMeshFormat\n$Nodes\n8\n" + nodes + "$EndNodes\n$Elements\n10\n" +
                "1 4 2 1 1 1 2 3 4\n2 4 2 2 2 5 6 7 8\n" +
                "3 2 2 1 1 2 3 4\n4 2 2 1 1 1 4 3\n5 2 2 4 4 1 2 4\n6 2 2 4 4 1 3 2\n7 2 2 4 4 6 7 8\n8 2 2 4 4 5 8 7\n9 2 2 0 0 5 6 8\n10 2 2 0 0 5 7 6\n" +
                "$EndElements\n";
            Assert.Equal(expected, Text(m, mk, TetMeshWriter.Format.Msh22));
        }

        [Fact]
        public void Golden_Msh41()
        {
            TetMesh3 m = TwoTets(out TetMeshMarkers mk);
            string expected = "$MeshFormat\n4.1 0 8\n$EndMeshFormat\n$Entities\n0 0 3 2\n" +
                "1 0 0 0 1 1 1 1 1 0\n2 0 0 0 3 1 1 1 4 0\n3 2 0 0 3 1 1 0 0\n" +          // surfaces: refs 1, 4, then 0 (no physical tag)
                "1 0 0 0 1 1 1 1 1 0\n2 2 0 0 3 1 1 1 2 0\n$EndEntities\n" +                // volumes: refs 1, 2
                "$Nodes\n1 8 1 8\n3 1 0 8\n1\n2\n3\n4\n5\n6\n7\n8\n" + VerticesBlock + "$EndNodes\n" +
                "$Elements\n5 10 1 10\n3 1 4 1\n1 1 2 3 4\n3 2 4 1\n2 5 6 7 8\n" +
                "2 1 2 2\n3 2 3 4\n4 1 4 3\n2 2 2 4\n5 1 2 4\n6 1 3 2\n7 6 7 8\n8 5 8 7\n2 3 2 2\n9 5 6 8\n10 5 7 6\n$EndElements\n";
            Assert.Equal(expected, Text(m, mk, TetMeshWriter.Format.Msh41));
            // no markers: one volume entity (tag 1) and one surface entity, no physical tags
            string plain = Text(m, null, TetMeshWriter.Format.Msh41);
            Assert.Contains("$Entities\n0 0 1 1\n1 0 0 0 3 1 1 0 0\n1 0 0 0 3 1 1 0 0\n$EndEntities\n", plain);
        }

        [Fact]
        public void Golden_Vtk()
        {
            TetMesh3 m = TwoTets(out TetMeshMarkers mk);
            string expected = "# vtk DataFile Version 3.0\ngeometry3Sharp tetrahedral mesh\nASCII\nDATASET UNSTRUCTURED_GRID\nPOINTS 8 double\n" + VerticesBlock +
                "CELLS 10 42\n4 0 1 2 3\n4 4 5 6 7\n3 1 2 3\n3 0 3 2\n3 0 1 3\n3 0 2 1\n3 5 6 7\n3 4 7 6\n3 4 5 7\n3 4 6 5\n" +
                "CELL_TYPES 10\n10\n10\n5\n5\n5\n5\n5\n5\n5\n5\nCELL_DATA 10\nSCALARS ref int 1\nLOOKUP_TABLE default\n1\n2\n1\n1\n4\n4\n4\n4\n0\n0\n";
            Assert.Equal(expected, Text(m, mk, TetMeshWriter.Format.Vtk));
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void GoldenMesh_ParsesBackToTheSameMesh(TetMeshWriter.Format f)
        {
            TetMesh3 m = TwoTets(out TetMeshMarkers mk);
            Parsed p = Parse(Text(m, mk, f), f);
            AssertSameMesh(m, mk, p, f);
        }

        [Fact]
        public void VerticesWithoutTets_WriteValidFiles()
        {
            var m = new TetMesh3(); m.Vertices.Add(new Vector3d(1, 2, 3)); m.Vertices.Add(new Vector3d(0, 0, 0));
            foreach (TetMeshWriter.Format f in Enum.GetValues(typeof(TetMeshWriter.Format))) {
                Parsed p = Parse(Text(m, null, f), f);
                Assert.Equal(m.Vertices, p.V); Assert.Empty(p.Tets);
            }
        }

        [Fact]
        public void EmptyMesh_WritesValidFiles()
        {
            foreach (TetMeshWriter.Format f in Enum.GetValues(typeof(TetMeshWriter.Format))) {
                Parsed p = Parse(Text(new TetMesh3(), null, f), f);
                Assert.Empty(p.V); Assert.Empty(p.Tets); Assert.Empty(p.Tris);
            }
        }

        // ---------------------------------------------------------------- the mesher's output through the writers

        // a small real result and its markers, computed once (deterministic): the sphere of the stage-2 tests without triangle groups
        // (so the markers are triangle ids; the group case makes its own copy)
        static readonly Lazy<(DMesh3 input, MeshTetrahedralization tm, TetMeshMarkers markers)> Sphere = new Lazy<(DMesh3, MeshTetrahedralization, TetMeshMarkers)>(() => {
            DMesh3 input = new DMesh3(TetMeshingStage2Tests.Input("sphere"));
            input.DiscardTriangleGroups();                                               // the generator's groups would make the markers group ids
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12 };
            Assert.True(tm.Compute());
            return (input, tm, TetMeshMarkers.Compute(tm.Result, input));
        });

        static void AssertSameMesh(TetMesh3 m, TetMeshMarkers mk, Parsed p, TetMeshWriter.Format f)
        {
            Assert.Equal(m.Vertices.Count, p.DeclaredVertices);
            Assert.Equal(m.Vertices, p.V);                                              // doubles round trip exactly
            Assert.Equal(m.Tets.Count, p.Tets.Count);
            Assert.Equal(m.BoundaryTriangles.Count, p.Tris.Count);
            for (int i = 0; i < m.Tets.Count; ++i) {
                Assert.Equal(new[] { m.Tets[i].a, m.Tets[i].b, m.Tets[i].c, m.Tets[i].d }, p.Tets[i]);
                Assert.Equal(mk == null ? 0 : TetMeshWriter.Ref(mk.TetRegion[i]), p.TetRef[i]);
            }
            for (int i = 0; i < m.BoundaryTriangles.Count; ++i) {
                Assert.Equal(new[] { m.BoundaryTriangles[i].a, m.BoundaryTriangles[i].b, m.BoundaryTriangles[i].c }, p.Tris[i]);
                Assert.Equal(mk == null ? 0 : TetMeshWriter.Ref(mk.BoundaryMarker[i]), p.TriRef[i]);
            }
            // 1-based where the format says so: the smallest index written is 1 (0 for VTK) and the last vertex is used
            Assert.Equal(OneBased(f) ? 1 : 0, p.MinRawIndex);
            // every tet is positively oriented in the file, as the format wants (normal of (v0, v1, v2) points to v3)
            foreach (int[] t in p.Tets)
                Assert.True(ExactPredicates.Orient3d(p.V[t[0]], p.V[t[1]], p.V[t[2]], p.V[t[3]]) > 0);
            foreach (int[] t in p.Tets.Concat(p.Tris)) foreach (int v in t) Assert.InRange(v, 0, p.V.Count - 1);
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void MesherOutput_RoundTripsThroughEveryFormat(TetMeshWriter.Format f)
        {
            var (input, tm, mk) = Sphere.Value;
            Assert.True(tm.Result.Tets.Count > 100);
            Parsed p = Parse(Text(tm.Result, mk, f), f);
            AssertSameMesh(tm.Result, mk, p, f);
            Assert.Equal(mk.RegionCount, p.TetRef.Distinct().Count());
            Assert.Contains(tm.Result.Vertices.Count, p.Tets.Concat(p.Tris).SelectMany(t => t.Select(v => v + 1)).Distinct());   // the last vertex is used
        }

        [Fact]
        public void Msh41_HasOneSurfaceEntityPerMarker_AndPhysicalTagsAreTheRefs()
        {
            var (input, tm, mk) = Sphere.Value;
            string text = Text(tm.Result, mk, TetMeshWriter.Format.Msh41);
            string[] L = Lines(text);
            string[] c = Tok(L[Array.IndexOf(L, "$Entities") + 1]);
            Assert.Equal(mk.BoundaryMarker.Distinct().Count(), I(c[2]));
            Assert.Equal(mk.RegionCount, I(c[3]));
        }

        [Fact]
        public void WithoutMarkers_EveryRefIsZero()
        {
            var (input, tm, mk) = Sphere.Value;
            foreach (TetMeshWriter.Format f in Enum.GetValues(typeof(TetMeshWriter.Format))) {
                Parsed p = Parse(Text(tm.Result, null, f), f);
                AssertSameMesh(tm.Result, null, p, f);
                Assert.All(p.TetRef, r => Assert.Equal(0, r));
                Assert.All(p.TriRef, r => Assert.Equal(0, r));
            }
        }

        [Fact]
        public void Writers_AreCultureIndependent_AndDeterministic()
        {
            var (input, tm, mk) = Sphere.Value;
            var saved = CultureInfo.CurrentCulture;
            try {
                foreach (TetMeshWriter.Format f in Enum.GetValues(typeof(TetMeshWriter.Format))) {
                    CultureInfo.CurrentCulture = new CultureInfo("en-US");
                    string a = TextCurrentCulture(tm.Result, mk, f);
                    CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                    string b = TextCurrentCulture(tm.Result, mk, f);
                    Assert.Equal(a, b);
                    Assert.Equal(a, TextCurrentCulture(tm.Result, mk, f));
                }
            } finally { CultureInfo.CurrentCulture = saved; }
        }

        static string TextCurrentCulture(TetMesh3 m, TetMeshMarkers mk, TetMeshWriter.Format f)
        {
            var w = new StringWriter();                                                 // the writer must not depend on the writer's own format provider either
            TetMeshWriter.Write(w, m, mk, f);
            return w.ToString();
        }

        [Fact]
        public void WriteToFile_ChoosesTheFormatByExtension_AndRejectsTheRest()
        {
            var (input, tm, mk) = Sphere.Value;
            string dir = Path.Combine(Path.GetTempPath(), "tetexport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                string[] files = { "a.mesh", "a.MSH", "a.vtk" };
                TetMeshWriter.Format[] formats = { TetMeshWriter.Format.Medit, TetMeshWriter.Format.Msh41, TetMeshWriter.Format.Vtk };
                for (int i = 0; i < files.Length; ++i) {
                    string path = Path.Combine(dir, files[i]);
                    TetMeshWriter.Write(path, tm.Result, mk);
                    Assert.Equal(Text(tm.Result, mk, formats[i]), File.ReadAllText(path));
                    Assert.Equal(formats[i], TetMeshWriter.FormatOfExtension(path));
                }
                string p22 = Path.Combine(dir, "b.msh");
                TetMeshWriter.Write(p22, tm.Result, mk, TetMeshWriter.Format.Msh22);
                Assert.StartsWith("$MeshFormat\n2.2 0 8\n", File.ReadAllText(p22));
                Assert.Throws<ArgumentException>(() => TetMeshWriter.Write(Path.Combine(dir, "a.txt"), tm.Result, mk));
                Assert.Null(TetMeshWriter.FormatOfExtension("a.stl"));
                Assert.Null(TetMeshWriter.FormatOfExtension(null));
                // markers of another mesh are refused
                TetMesh3 other = TwoTets(out _);
                Assert.Throws<ArgumentException>(() => TetMeshWriter.Write(new StringWriter(), other, mk, TetMeshWriter.Format.Medit));
                Assert.Throws<ArgumentNullException>(() => TetMeshWriter.Write(new StringWriter(), null, null, TetMeshWriter.Format.Medit));
                TetMesh3 two = TwoTets(out TetMeshMarkers good);
                var badMarkers = new TetMeshMarkers() { TetRegion = good.TetRegion, BoundaryMarker = new int[3] };
                Assert.Throws<ArgumentException>(() => TetMeshWriter.Write(new StringWriter(), two, badMarkers, TetMeshWriter.Format.Medit));
            } finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Ref_IsIdPlusOne_AndZeroForNoId()
        {
            Assert.Equal(1, TetMeshWriter.Ref(0));
            Assert.Equal(8, TetMeshWriter.Ref(7));
            Assert.Equal(0, TetMeshWriter.Ref(-1));
            Assert.Equal(0, TetMeshWriter.Ref(int.MinValue));
            Assert.Equal(0, TetMeshWriter.Ref(int.MaxValue));
            Assert.Equal(int.MaxValue, TetMeshWriter.Ref(int.MaxValue - 1));
        }

        // ---------------------------------------------------------------- markers

        // the nearest input triangle by brute force over all triangles
        static (double dist, int tri) Brute(DMesh3 input, Vector3d p)
        {
            double best = double.MaxValue; int bt = -1;
            foreach (int tid in input.TriangleIndices()) {
                double d = Math.Sqrt(MeshQueries.TriDistanceSqr(input, tid, p));
                if (d < best) { best = d; bt = tid; }
            }
            return (best, bt);
        }

        static Vector3d Centroid(TetMesh3 t, int i) => t.Vertices[t.BoundaryTriangles[i].a] / 3.0 + t.Vertices[t.BoundaryTriangles[i].b] / 3.0 + t.Vertices[t.BoundaryTriangles[i].c] / 3.0;

        [Fact]
        public void Markers_AreTheNearestInputTriangleIds_WithTheDistance()
        {
            var (input, tm, mk) = Sphere.Value;
            TetMesh3 t = tm.Result;
            Assert.False(mk.MarkersAreGroups);
            Assert.Equal(t.BoundaryTriangles.Count, mk.BoundaryMarker.Length);
            Assert.Equal(t.Tets.Count, mk.TetRegion.Length);
            for (int i = 0; i < t.BoundaryTriangles.Count; ++i) {
                var (d, tri) = Brute(input, Centroid(t, i));
                Assert.Equal(mk.NearestInputTriangle[i], mk.BoundaryMarker[i]);       // no groups: the marker is the triangle id
                Assert.Equal(d, mk.BoundaryDistance[i], 12);
                // the reported triangle is a nearest one (a tie may name another triangle at the same distance)
                Assert.Equal(d, Math.Sqrt(MeshQueries.TriDistanceSqr(input, mk.NearestInputTriangle[i], Centroid(t, i))), 12);
                if (mk.NearestInputTriangle[i] != tri) Assert.Equal(d, mk.BoundaryDistance[i], 12);
            }
            Assert.Equal(mk.BoundaryDistance.Max(), mk.MaxBoundaryDistance);
            Assert.Equal(mk.BoundaryDistance.Average(), mk.MeanBoundaryDistance, 12);
            // the boundary is the offset wrap: every centroid is at most about alpha from the input, and none touches it
            Assert.True(mk.MaxBoundaryDistance < tm.UsedAlpha * 1.5);
            Assert.True(mk.BoundaryDistance.Min() > 0);
        }

        [Fact]
        public void Markers_AreTheGroupIds_IfTheInputHasGroups()
        {
            var (input, tm, _) = Sphere.Value;
            var grouped = new DMesh3(input);
            Assert.False(grouped.HasTriangleGroups);
            grouped.EnableTriangleGroups(0);
            foreach (int tid in grouped.TriangleIndices()) grouped.SetTriangleGroup(tid, 100 + (grouped.GetTriCentroid(tid).z > 0 ? 1 : 0));
            TetMeshMarkers mk = TetMeshMarkers.Compute(tm.Result, grouped);
            TetMeshMarkers plain = Sphere.Value.markers;
            Assert.True(mk.MarkersAreGroups);
            Assert.Equal(plain.NearestInputTriangle, mk.NearestInputTriangle);          // the groups change the label, not the nearest triangle
            Assert.Equal(plain.BoundaryDistance, mk.BoundaryDistance);
            for (int i = 0; i < mk.BoundaryMarker.Length; ++i) Assert.Equal(grouped.GetTriangleGroup(mk.NearestInputTriangle[i]), mk.BoundaryMarker[i]);
            Assert.Equal(new[] { 100, 101 }, mk.BoundaryMarker.Distinct().OrderBy(x => x));
            // the refs in a file are group + 1
            Parsed p = Parse(Text(tm.Result, mk, TetMeshWriter.Format.Medit), TetMeshWriter.Format.Medit);
            Assert.Equal(new[] { 101, 102 }, p.TriRef.Distinct().OrderBy(x => x));
        }

        [Fact]
        public void Markers_OfAnInputWithoutTriangles_AreMinusOne()
        {
            TetMesh3 m = TwoTets(out _);
            foreach (DMesh3 input in new DMesh3[] { null, new DMesh3() }) {
                TetMeshMarkers mk = TetMeshMarkers.Compute(m, input);
                Assert.All(mk.BoundaryMarker, x => Assert.Equal(-1, x));
                Assert.All(mk.NearestInputTriangle, x => Assert.Equal(-1, x));
                Assert.All(mk.BoundaryDistance, d => Assert.True(double.IsPositiveInfinity(d)));
                Assert.Equal(0, mk.MaxBoundaryDistance);
                Assert.Equal(2, mk.RegionCount);
                Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0, 0 }, p0(Parse(Text(m, mk, TetMeshWriter.Format.Medit), TetMeshWriter.Format.Medit).TriRef));
            }
            Assert.Throws<ArgumentNullException>(() => TetMeshMarkers.Compute(null, new DMesh3()));
        }
        static int[] p0(List<int> l) => l.ToArray();

        [Fact]
        public void Regions_AreTheConnectedComponents_NumberedByTheirLowestTet()
        {
            TetMesh3 m = TwoTets(out _);
            TetMeshMarkers mk = TetMeshMarkers.Compute(m, null);
            Assert.Equal(new[] { 0, 1 }, mk.TetRegion);
            // a second tet glued to the first by the facet (1, 2, 3): one region
            var glued = new TetMesh3();
            glued.Vertices.AddRange(m.Vertices.Take(4)); glued.Vertices.Add(new Vector3d(1, 1, 1));
            glued.Tets.Add(new Index4i(0, 1, 2, 3)); glued.Tets.Add(new Index4i(1, 2, 3, 4));
            Assert.Null(TetMeshQuality.NotValidReason(WithBoundary(glued)));
            Assert.Equal(1, TetMeshMarkers.Compute(glued, null).RegionCount);
            // two tets that share only a vertex are two regions; the numbering follows the lowest tet, whatever the listing order
            var touching = new TetMesh3();
            touching.Vertices.AddRange(new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, -1) });
            touching.Tets.Add(new Index4i(0, 1, 2, 3)); touching.Tets.Add(new Index4i(0, 5, 4, 6)); touching.Tets.Add(new Index4i(0, 1, 2, 3));
            Assert.Equal(new[] { 0, 1, 0 }, TetMeshMarkers.Compute(touching, null).TetRegion);
        }

        static TetMesh3 WithBoundary(TetMesh3 t)
        {
            var o = new TetMesh3(); o.Vertices.AddRange(t.Vertices); o.Tets.AddRange(t.Tets);
            var faces = new HashSet<(int, int, int)>();
            Func<int, int, int, (int, int, int)> canon = (a, b, c) => a <= b && a <= c ? (a, b, c) : b <= a && b <= c ? (b, c, a) : (c, a, b);
            foreach (Index4i q in o.Tets) for (int i = 0; i < 4; ++i) faces.Add(canon(q[Delaunay3.FacetIndices[i, 0]], q[Delaunay3.FacetIndices[i, 1]], q[Delaunay3.FacetIndices[i, 2]]));
            foreach (var f in faces) if (!faces.Contains(canon(f.Item1, f.Item3, f.Item2))) o.BoundaryTriangles.Add(new Index3i(f.Item1, f.Item2, f.Item3));
            return o;
        }

        [Fact]
        public void Regions_OfTwoSeparateInputParts_AreTwo()
        {
            DMesh3 a = TetMeshingStage2Tests.Input("sphere");
            var both = new DMesh3(a);
            var b = new DMesh3(a);
            foreach (int v in b.VertexIndices()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(5, 0, 0));
            new MeshEditor(both).AppendMesh(b);
            var tm = new MeshTetrahedralization(both) { RelativeAlpha = 8 };
            Assert.True(tm.Compute());
            TetMeshMarkers mk = TetMeshMarkers.Compute(tm.Result, both);
            Assert.Equal(2, mk.RegionCount);
            Assert.Equal(new[] { 0, 1 }, mk.TetRegion.Distinct().OrderBy(x => x));
            Assert.Equal(0, mk.TetRegion[0]);
            // every boundary triangle is on one region's tets only: its marker's input triangle is in the same part as the region
            var part = new Dictionary<int, int>();
            for (int c = 0; c < tm.Result.Tets.Count; ++c) {
                Index4i q = tm.Result.Tets[c];
                int side = tm.Result.Vertices[q.a].x > 2.5 ? 1 : 0;
                Assert.Equal(side, mk.TetRegion[c] == mk.TetRegion[0] ? 0 : 1);
            }
            Parsed p = Parse(Text(tm.Result, mk, TetMeshWriter.Format.Msh41), TetMeshWriter.Format.Msh41);
            Assert.Equal(new[] { 1, 2 }, p.TetRef.Distinct().OrderBy(x => x));
        }

        [Fact]
        public void Markers_AreRepeatableAndEqualTheSerialSingleQueries()
        {
            var (input, tm, mk) = Sphere.Value;
            var tree = new DMeshAABBTree3(input, true);
            for (int run = 0; run < 3; ++run) {
                TetMeshMarkers again = TetMeshMarkers.Compute(tm.Result, input);
                Assert.Equal(mk.BoundaryMarker, again.BoundaryMarker);
                Assert.Equal(mk.NearestInputTriangle, again.NearestInputTriangle);
                Assert.Equal(mk.BoundaryDistance, again.BoundaryDistance);
                Assert.Equal(mk.TetRegion, again.TetRegion);
            }
            for (int i = 0; i < tm.Result.BoundaryTriangles.Count; ++i) {                // the parallel batch equals the single serial query, bit for bit
                int tid = tree.FindNearestTriangle(Centroid(tm.Result, i), out double dSqr);
                Assert.Equal(tid, mk.NearestInputTriangle[i]);
                Assert.Equal(Math.Sqrt(dSqr), mk.BoundaryDistance[i]);
            }
        }

        // ---------------------------------------------------------------- invariance over Similarity.All()

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Markers_OfTheSameMesh_AreInvariantUnderEverySimilarity(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            var (input, tm, mk) = Sphere.Value;
            // the same tets and input, transformed: the topology is the same, so regions and nearest triangles are the same, the distances scale
            var t = new TetMesh3();
            t.Vertices.AddRange(tm.Result.Vertices.Select(s.Apply)); t.Tets.AddRange(tm.Result.Tets); t.BoundaryTriangles.AddRange(tm.Result.BoundaryTriangles);
            TetMeshMarkers a = TetMeshMarkers.Compute(t, Invariance.Transformed(input, s));
            Assert.Equal(mk.TetRegion, a.TetRegion);
            Assert.Equal(mk.RegionCount, a.RegionCount);
            DMesh3 moved = Invariance.Transformed(input, s);
            int ties = 0;
            for (int i = 0; i < mk.BoundaryDistance.Length; ++i) {
                if (s.IsExact) {                                                         // 2^k scales are exact: same triangle, scaled distance, bit for bit
                    Assert.Equal(mk.BoundaryMarker[i], a.BoundaryMarker[i]);
                    Assert.Equal(mk.NearestInputTriangle[i], a.NearestInputTriangle[i]);
                    Assert.Equal(s.Scale * mk.BoundaryDistance[i], a.BoundaryDistance[i]);
                    continue;
                }
                double tol = s.DistanceResolution(2) * 10 + 1e-9 * s.Scale;
                Assert.Equal(s.Scale * mk.BoundaryDistance[i], a.BoundaryDistance[i], tol);
                if (mk.NearestInputTriangle[i] != a.NearestInputTriangle[i]) {          // a rounding-level tie (the sphere is symmetric): both triangles are nearest
                    ties++;
                    Assert.Equal(a.BoundaryDistance[i], Math.Sqrt(MeshQueries.TriDistanceSqr(moved, mk.NearestInputTriangle[i], Centroid(t, i))), tol);
                } else Assert.Equal(mk.BoundaryMarker[i], a.BoundaryMarker[i]);
            }
            Assert.True(ties < mk.BoundaryDistance.Length / 10, "ties " + ties);
            if (s.IsExact) Assert.Equal(s.Scale * mk.MaxBoundaryDistance, a.MaxBoundaryDistance);
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void MesherOutput_Markers_AreTheSameForPowerOfTwoScales(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            var (input, tm, mk) = Sphere.Value;
            var b = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12 };
            Assert.True(b.Compute());
            TetMeshMarkers mb = TetMeshMarkers.Compute(b.Result, b.Mesh);
            Assert.Null(TetMeshQuality.NotValidReason(b.Result));
            Assert.Equal(mb.TetRegion.Max() + 1, mb.RegionCount);
            // markers of this result agree with the brute force on its own input
            for (int i = 0; i < b.Result.BoundaryTriangles.Count; i += 7)
                Assert.Equal(Brute(b.Mesh, Centroid(b.Result, i)).dist, mb.BoundaryDistance[i], (int)Math.Max(6, 12 - Math.Log10(Math.Max(1, s.Scale))));
            if (s.IsExact) {
                Assert.Equal(mk.TetRegion, mb.TetRegion);
                Assert.Equal(mk.BoundaryMarker, mb.BoundaryMarker);
                Assert.Equal(mk.NearestInputTriangle, mb.NearestInputTriangle);
                for (int i = 0; i < mk.BoundaryDistance.Length; ++i) Assert.Equal(s.Scale * mk.BoundaryDistance[i], mb.BoundaryDistance[i]);
                // and the files agree: the scaled coordinates, the same indices and refs
                Parsed pa = Parse(Text(tm.Result, mk, TetMeshWriter.Format.Msh22), TetMeshWriter.Format.Msh22);
                Parsed pb = Parse(Text(b.Result, mb, TetMeshWriter.Format.Msh22), TetMeshWriter.Format.Msh22);
                Assert.Equal(pa.Tets, pb.Tets); Assert.Equal(pa.TetRef, pb.TetRef); Assert.Equal(pa.TriRef, pb.TriRef);
                for (int i = 0; i < pa.V.Count; ++i) Assert.Equal(s.Scale * pa.V[i], pb.V[i]);
            }
        }

        // ---------------------------------------------------------------- P5(a): the targets, checked on the output

        static TetMesh3 SingleTet(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            var t = new TetMesh3();
            t.Vertices.AddRange(new[] { a, b, c, d }); t.Tets.Add(new Index4i(0, 1, 2, 3));
            return t;
        }

        [Fact]
        public void CheckTargets_CountsTheTetsThatMissEachTarget()
        {
            // the regular tetrahedron: smallest dihedral 70.53 degrees, radius-edge sqrt(6)/4 = 0.612
            TetMesh3 regular = SingleTet(new Vector3d(1, 1, 1), new Vector3d(1, -1, -1), new Vector3d(-1, 1, -1), new Vector3d(-1, -1, 1));
            if (ExactPredicates.Orient3d(regular.Vertices[0], regular.Vertices[1], regular.Vertices[2], regular.Vertices[3]) < 0)
                regular.Tets[0] = new Index4i(1, 0, 2, 3);
            // a sliver: four points almost in one plane (smallest dihedral about 1 degree)
            TetMesh3 sliver = SingleTet(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0.02));
            if (ExactPredicates.Orient3d(sliver.Vertices[0], sliver.Vertices[1], sliver.Vertices[2], sliver.Vertices[3]) < 0)
                sliver.Tets[0] = new Index4i(1, 0, 2, 3);
            TetTargetReport r = TetMeshQuality.CheckTargets(regular, 70, 0.7);
            Assert.True(r.Met && r.MinDihedralMet && r.RadiusEdgeMet);
            Assert.Equal(0, r.TetsBelowMinDihedral); Assert.Equal(0, r.TetsAboveRadiusEdge);
            Assert.Equal(70.5288, r.WorstMinDihedral, 3); Assert.Equal(0.6124, r.WorstRadiusEdge, 3);
            r = TetMeshQuality.CheckTargets(regular, 71, 0.6);                           // both targets just out of reach
            Assert.False(r.Met); Assert.False(r.MinDihedralMet); Assert.False(r.RadiusEdgeMet);
            Assert.Equal(1, r.TetsBelowMinDihedral); Assert.Equal(1, r.TetsAboveRadiusEdge);
            r = TetMeshQuality.CheckTargets(regular, 71, 0.7);                           // one of the two
            Assert.False(r.MinDihedralMet); Assert.True(r.RadiusEdgeMet); Assert.False(r.Met);
            r = TetMeshQuality.CheckTargets(regular, 70, 0.6);
            Assert.True(r.MinDihedralMet); Assert.False(r.RadiusEdgeMet); Assert.False(r.Met);

            var both = new TetMesh3(); both.Vertices.AddRange(regular.Vertices.Concat(sliver.Vertices));
            both.Tets.Add(regular.Tets[0]); both.Tets.Add(new Index4i(sliver.Tets[0].a + 4, sliver.Tets[0].b + 4, sliver.Tets[0].c + 4, sliver.Tets[0].d + 4));
            r = TetMeshQuality.CheckTargets(both, 10, 2);
            Assert.Equal(2, r.Tets);
            Assert.Equal(1, r.TetsBelowMinDihedral);
            Assert.True(r.WorstMinDihedral < 3);
            Assert.False(r.MinDihedralMet);
            r = TetMeshQuality.CheckTargets(both, 1, 100);
            Assert.True(r.MinDihedralMet && r.TetsBelowMinDihedral == 0);
            // an off target (0 or less) is met whatever the tets are
            r = TetMeshQuality.CheckTargets(both, 0, 0);
            Assert.True(r.Met); Assert.Equal(0, r.TetsBelowMinDihedral + r.TetsAboveRadiusEdge);
            // a tet that is not exactly positive counts as missing both targets
            TetMesh3 flipped = SingleTet(regular.Vertices[0], regular.Vertices[1], regular.Vertices[2], regular.Vertices[3]);
            flipped.Tets[0] = new Index4i(regular.Tets[0].b, regular.Tets[0].a, regular.Tets[0].c, regular.Tets[0].d);
            r = TetMeshQuality.CheckTargets(flipped, 1, 100);
            Assert.Equal(1, r.TetsBelowMinDihedral); Assert.Equal(1, r.TetsAboveRadiusEdge);
            // no tets: met, nothing to count
            r = TetMeshQuality.CheckTargets(new TetMesh3(), 10, 2);
            Assert.True(r.Met); Assert.Equal(0, r.Tets); Assert.Equal(180, r.WorstMinDihedral); Assert.Equal(0, r.WorstRadiusEdge);
            Assert.Equal(0, r.TetsBelowMinDihedral + r.TetsAboveRadiusEdge);
        }

        [Fact]
        public void MeshTetrahedralization_Targets_AreMeasuredOnTheOutput()
        {
            var (input, tm, _) = Sphere.Value;
            TetTargetReport r = tm.Targets;
            Assert.NotNull(r);
            Assert.Equal(tm.SliverDihedralDegrees, r.MinDihedralDegrees);
            Assert.Equal(tm.MaxRadiusEdgeRatio, r.MaxRadiusEdgeRatio);
            TetQualityStatistics q = TetMeshQuality.Measure(tm.Result);               // the library's own counts, computed independently of CheckTargets
            Assert.Equal(q.MinDihedralBelow10, r.TetsBelowMinDihedral);
            Assert.Equal(q.RadiusEdgeAbove2, r.TetsAboveRadiusEdge);
            Assert.Equal(q.MinDihedral.Worst, r.WorstMinDihedral);
            Assert.Equal(q.RadiusEdge.Worst, r.WorstRadiusEdge);
            Assert.Equal(r.TetsBelowMinDihedral == 0, r.MinDihedralMet);
            Assert.Equal(r.TetsAboveRadiusEdge == 0, r.RadiusEdgeMet);
            Assert.Equal(r.MinDihedralMet && r.RadiusEdgeMet, r.Met);
            Assert.Equal(tm.Result.Tets.Count, r.Tets);
        }

        [Fact]
        public void Targets_AreCheckedNotAssumed_AndFollowTheParameters()
        {
            DMesh3 input = TetMeshingStage2Tests.Input("sphere");
            // with the improvement off and a demanding dihedral target the flag must say no where tets are below it
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, Improve = false, SliverDihedralDegrees = 0 };
            Assert.Null(tm.Targets);
            Assert.True(tm.Compute());
            Assert.True(tm.Targets.MinDihedralMet);                                     // target off: met by definition
            Assert.Equal(0, tm.Targets.TetsBelowMinDihedral);
            TetTargetReport demanding = TetMeshQuality.CheckTargets(tm.Result, 60, tm.MaxRadiusEdgeRatio);
            Assert.False(demanding.MinDihedralMet);
            Assert.True(demanding.TetsBelowMinDihedral > 0);
            // radius-edge target of the run: tets left above it are counted (unrefined at the fixed surface)
            Assert.Equal(TetMeshQuality.Measure(tm.Result).RadiusEdgeAbove2, tm.Targets.TetsAboveRadiusEdge);
            // a failed run has no report; a new run replaces the old report
            var failed = new MeshTetrahedralization(new DMesh3());
            Assert.False(failed.Compute());
            Assert.Null(failed.Targets);
            var reuse = new MeshTetrahedralization(input) { RelativeAlpha = 12 };
            Assert.True(reuse.Compute());
            TetTargetReport first = reuse.Targets;
            Assert.True(reuse.Compute());
            Assert.NotSame(first, reuse.Targets);
            Assert.Equal(first.TetsBelowMinDihedral, reuse.Targets.TetsBelowMinDihedral);
            Assert.Equal(first.TetsAboveRadiusEdge, reuse.Targets.TetsAboveRadiusEdge);
            Assert.Equal(first.WorstMinDihedral, reuse.Targets.WorstMinDihedral);
            Assert.Equal(first.WorstRadiusEdge, reuse.Targets.WorstRadiusEdge);
        }

        [Fact]
        public void Targets_UseTheParametersOfTheRun_AndAreMetAtExactlyTheMeasuredWorst()
        {
            DMesh3 input = TetMeshingStage2Tests.Input("sphere");
            var tm = new MeshTetrahedralization(input) { RelativeAlpha = 12, SliverDihedralDegrees = 8, MaxRadiusEdgeRatio = 3 };
            Assert.True(tm.Compute());
            TetTargetReport r = tm.Targets;
            Assert.Equal(8, r.MinDihedralDegrees);
            Assert.Equal(3, r.MaxRadiusEdgeRatio);
            TetQualityStatistics q = TetMeshQuality.Measure(tm.Result);
            Assert.Equal(q.MinDihedral.Worst >= 8, r.MinDihedralMet);
            Assert.Equal(q.RadiusEdge.Worst <= 3, r.RadiusEdgeMet);
            // a target equal to the measured worst value is met (a tet misses a target only strictly), one step beyond it is not
            TetTargetReport at = TetMeshQuality.CheckTargets(tm.Result, q.MinDihedral.Worst, q.RadiusEdge.Worst);
            Assert.True(at.Met);
            Assert.Equal(0, at.TetsBelowMinDihedral + at.TetsAboveRadiusEdge);
            TetTargetReport beyond = TetMeshQuality.CheckTargets(tm.Result, q.MinDihedral.Worst * (1 + 1e-9), q.RadiusEdge.Worst * (1 - 1e-9));
            Assert.False(beyond.MinDihedralMet); Assert.False(beyond.RadiusEdgeMet);
            Assert.True(beyond.TetsBelowMinDihedral >= 1 && beyond.TetsAboveRadiusEdge >= 1);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Targets_ArePow2ScaleInvariant(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            if (!s.IsExact) return;
            var (input, tm, _) = Sphere.Value;
            var b = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12 };
            Assert.True(b.Compute());
            Assert.Equal(tm.Targets.TetsBelowMinDihedral, b.Targets.TetsBelowMinDihedral);
            Assert.Equal(tm.Targets.TetsAboveRadiusEdge, b.Targets.TetsAboveRadiusEdge);
            Assert.Equal(tm.Targets.WorstMinDihedral, b.Targets.WorstMinDihedral);
            Assert.Equal(tm.Targets.Met, b.Targets.Met);
        }

        // ---------------------------------------------------------------- the existing result stays bit for bit the same

        // FNV-1a over the bits of the vertices and the indices of tets and boundary triangles, and the counts
        static ulong Fingerprint(MeshTetrahedralization tm)
        {
            ulong h = 14695981039346656037UL;
            Action<ulong> add = x => { for (int i = 0; i < 8; ++i) { h ^= (x >> (8 * i)) & 0xFF; h *= 1099511628211UL; } };
            TetMesh3 t = tm.Result;
            foreach (Vector3d v in t.Vertices) { add((ulong)BitConverter.DoubleToInt64Bits(v.x)); add((ulong)BitConverter.DoubleToInt64Bits(v.y)); add((ulong)BitConverter.DoubleToInt64Bits(v.z)); }
            foreach (Index4i q in t.Tets) { add((ulong)q.a); add((ulong)q.b); add((ulong)q.c); add((ulong)q.d); }
            foreach (Index3i f in t.BoundaryTriangles) { add((ulong)f.a); add((ulong)f.b); add((ulong)f.c); }
            foreach (var kv in tm.Counts.OrderBy(k => k.Key, StringComparer.Ordinal)) { foreach (char ch in kv.Key) add(ch); add((ulong)kv.Value); }
            return h;
        }

        // pinned on master before P2 (the library without TetTargetReport, TetMeshMarkers and TetMeshWriter)
        [Fact]
        public void ExistingResult_IsBitForBitTheSameAsBeforeP2()
        {
            // pinned on linux / .NET (libm atan2 and sin differ in the last bit on other platforms, which can change the hill climbing there)
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ||
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64 ||
                !System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET ", StringComparison.Ordinal) ||
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal)) return;
            var box = new MeshTetrahedralization(TetMeshingStage2Tests.Input("box"));
            Assert.True(box.Compute());
            Assert.Equal(FingerprintBox, Fingerprint(box));
            Assert.Equal(CountBox, box.Result.Tets.Count);
        }
        const ulong FingerprintBox = 2703540905802959250UL;
        const int CountBox = 8013;
    }
}
