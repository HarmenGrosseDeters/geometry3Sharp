using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    static class PolyMeshTestUtil
    {
        /// <summary>A 3 x 3 grid of quads (groups 0..2 by row), a hexagon, a pentagon and a triangle beside it, in the plane z = 0.25.</summary>
        public static DPolyMesh QuadsAndNgons()
        {
            DPolyMesh m = new DPolyMesh();
            for (int j = 0; j <= 3; ++j)
                for (int i = 0; i <= 3; ++i)
                    m.AppendVertex(i * 0.5, j * 0.5, 0.25);
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < 3; ++i)
                    m.AppendQuad(j * 4 + i, j * 4 + i + 1, (j + 1) * 4 + i + 1, (j + 1) * 4 + i, j);
            int h = m.VertexCount;
            double[] hex = { 3, 0, 4, 0, 5, 1, 4, 2, 3, 2, 2.5, 1 };
            for (int k = 0; k < 6; ++k) m.AppendVertex(hex[2 * k], hex[2 * k + 1], 0.25);
            m.AppendFace(new[] { h, h + 1, h + 2, h + 3, h + 4, h + 5 }, 3);
            int p = m.VertexCount;
            for (int k = 0; k < 5; ++k) m.AppendVertex(6 + Math.Cos(k * 1.2566370614359172), Math.Sin(k * 1.2566370614359172), 0.25);
            m.AppendFace(new[] { p, p + 1, p + 2, p + 3, p + 4 }, 3);
            int t = m.VertexCount;
            m.AppendVertex(8, 0, 0.25); m.AppendVertex(9, 0, 0.25); m.AppendVertex(8, 1, 0.25);
            m.AppendTriangle(t, t + 1, t + 2, 4);
            return m;
        }

        /// <summary>The 6 quads of a cube (outward orientation), 8 vertices.</summary>
        public static DPolyMesh QuadCube()
        {
            DPolyMesh m = new DPolyMesh();
            for (int k = 0; k < 8; ++k) m.AppendVertex(k & 1, (k >> 1) & 1, (k >> 2) & 1);
            m.AppendQuad(0, 2, 3, 1); m.AppendQuad(4, 5, 7, 6);   // z = 0 (down), z = 1 (up)
            m.AppendQuad(0, 1, 5, 4); m.AppendQuad(2, 6, 7, 3);   // y = 0, y = 1
            m.AppendQuad(0, 4, 6, 2); m.AppendQuad(1, 3, 7, 5);   // x = 0, x = 1
            return m;
        }

        public static bool Same(DPolyMesh a, DPolyMesh b)
        {
            return a.VertexCoordinates.SequenceEqual(b.VertexCoordinates) && a.FaceOffsets.SequenceEqual(b.FaceOffsets)
                && a.FaceIndices.SequenceEqual(b.FaceIndices) && a.FaceGroups.SequenceEqual(b.FaceGroups);
        }

        public static string ObjText(DPolyMesh m)
        {
            var sw = new StringWriter();
            PolyMeshIO.WriteOBJ(sw, m);
            return sw.ToString();
        }

        public static DPolyMesh ReadObj(string text, List<string> warnings = null)
        {
            DPolyMesh m;
            IOReadResult r = PolyMeshIO.ReadOBJ(new StringReader(text), ReadOptions.Defaults, out m, warnings == null ? (ParsingMessagesHandler)null : (s, e) => warnings.Add(s));
            Assert.True(r.code == IOCode.Ok, r.message);
            return m;
        }

        public static byte[] PlyBytes(DPolyMesh m, bool binary, bool doublePrecision = true)
        {
            var ms = new MemoryStream();
            if (binary) {
                var w = new BinaryWriter(ms);
                PolyMeshIO.WritePLY(w, m, doublePrecision);
                w.Flush();
            } else {
                var w = new StreamWriter(ms, new UTF8Encoding(false));
                PolyMeshIO.WritePLY(w, m, doublePrecision);
                w.Flush();
            }
            return ms.ToArray();
        }

        public static string Header(byte[] data)
        {
            string head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 600));
            return head.Substring(0, head.IndexOf("end_header", StringComparison.Ordinal));
        }

        public static DPolyMesh ReadPly(byte[] data)
        {
            DPolyMesh m;
            IOReadResult r = PolyMeshIO.ReadPLY(new BinaryReader(new MemoryStream(data)), ReadOptions.Defaults, out m);
            Assert.True(r.code == IOCode.Ok, r.message);
            return m;
        }

        public static List<Index3i> Triangulate(DPolyMesh m, int f)
        {
            var tris = new List<Index3i>();
            m.TriangulateFace(f, tris);
            return tris;
        }
    }


    public class PolyMeshTests
    {
        // ---- construction and validation ----

        [Fact]
        public void BulkAppend_EqualsSingleAppends()
        {
            DPolyMesh single = PolyMeshTestUtil.QuadsAndNgons();
            DPolyMesh bulk = new DPolyMesh(single.VertexCount, single.FaceCount, single.FaceIndices.Count);
            Assert.Equal(0, bulk.AppendVertices(single.VertexCoordinates.ToArray()));
            var counts = Enumerable.Range(0, single.FaceCount).Select(single.FaceVertexCount).ToArray();
            Assert.Equal(0, bulk.AppendFaces(counts, single.FaceIndices.ToArray(), single.FaceGroups.ToArray()));
            Assert.True(PolyMeshTestUtil.Same(single, bulk));
            // a second bulk append continues at the end
            Assert.Equal(single.VertexCount, bulk.AppendVertices(new double[] { 1, 2, 3 }));
            Assert.Equal(single.FaceCount, bulk.AppendFaces(new[] { 3 }, new[] { 0, 1, 2 }));
            Assert.Equal(0, bulk.GetFaceGroup(single.FaceCount));
            Assert.Equal(new[] { 0, 1, 2 }, bulk.GetFace(single.FaceCount));
        }

        [Fact]
        public void BulkAppend_RejectsInconsistentArrays_AppendsNothing()
        {
            DPolyMesh m = new DPolyMesh();
            Assert.Throws<ArgumentException>(() => m.AppendVertices(new double[] { 1, 2 }));
            Assert.Throws<ArgumentException>(() => m.AppendFaces(new[] { 3, 3 }, new[] { 0, 1, 2 }));
            Assert.Throws<ArgumentException>(() => m.AppendFaces(new[] { 4, -1 }, new[] { 0, 1, 2 }));
            Assert.Throws<ArgumentException>(() => m.AppendFaces(new[] { 3 }, new[] { 0, 1, 2 }, new[] { 0, 0 }));
            Assert.Equal(0, m.FaceCount);
            Assert.Empty(m.FaceIndices);
            Assert.Equal(new[] { 0 }, m.FaceOffsets.ToArray());
        }

        [Fact]
        public void Validate_ReportsEveryProblemKind_WithoutThrowing()
        {
            DPolyMesh m = new DPolyMesh();
            for (int k = 0; k < 5; ++k) m.AppendVertex(k, k * k, 0);
            m.AppendVertex(double.NaN, 0, 0);
            m.AppendVertex(0, double.PositiveInfinity, 0);
            m.AppendQuad(0, 1, 2, 3);                           // 0 ok
            m.AppendFace(new[] { 0, 1 });                       // 1 too few
            m.AppendFace(new int[0]);                           // 2 empty
            m.AppendQuad(0, 1, 1, 2);                           // 3 repeated
            m.AppendTriangle(0, 1, 9);                              // 4 out of range
            m.AppendTriangle(0, 1, -1);                             // 5 negative
            m.AppendFace(new[] { 7, 7 });                       // 6 out of range (a repeat is only reported for indices in range)
            m.AppendFace(new[] { 1, 1 });                       // 7 too few and repeated
            m.AppendTriangle(0, 2, 4);                          // 8 ok
            PolyMeshReport r = m.Validate();
            Assert.False(r.IsValid);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, r.InvalidFaces.ToArray());
            Assert.Equal(new[] {
                PolyFaceIssues.TooFewVertices, PolyFaceIssues.TooFewVertices, PolyFaceIssues.RepeatedVertex,
                PolyFaceIssues.IndexOutOfRange, PolyFaceIssues.IndexOutOfRange,
                PolyFaceIssues.TooFewVertices | PolyFaceIssues.IndexOutOfRange,
                PolyFaceIssues.TooFewVertices | PolyFaceIssues.RepeatedVertex
            }, r.Issues.ToArray());
            Assert.Equal(new[] { 5, 6 }, r.NonFiniteVertices.ToArray());
            Assert.True(m.IsFaceValid(0));
            Assert.False(m.IsFaceValid(3));
            Assert.True(PolyMeshTestUtil.QuadsAndNgons().Validate().IsValid);
        }

        // ---- ToDMesh3 and the triangulation rule ----

        [Fact]
        public void ToDMesh3_QuadCube_IsClosedManifoldWithFaceMap()
        {
            DPolyMesh cube = PolyMeshTestUtil.QuadCube();
            int[] triToFace;
            DMesh3 mesh = cube.ToDMesh3(out triToFace);
            Assert.True(mesh.IsClosed());
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(8, mesh.VertexCount);
            Assert.Equal(12, mesh.TriangleCount);
            Assert.Equal(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 }, triToFace);
            Assert.True(mesh.IsCompact);
            // the outward orientation is kept: the volume (sum of signed tetrahedra against the origin) of the unit cube is +1
            double volume = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                volume += a.Dot(b.Cross(c)) / 6;
            }
            Assert.Equal(1.0, volume, 12);
        }

        [Fact]
        public void ToDMesh3_CarriesGroups_AndSkipsInvalidFaces()
        {
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            int nfaces = m.FaceCount;
            m.AppendQuad(0, 1, 1, 5);          // repeated vertex
            m.AppendFace(new[] { 0, 1 });      // too few
            m.AppendTriangle(0, 1, 999);           // out of range
            m.AppendVertex(100, 100, 100);     // unreferenced, still present
            var skipped = new List<int>();
            int[] triToFace;
            DMesh3 mesh = m.ToDMesh3(out triToFace, skipped);
            Assert.Equal(new[] { nfaces, nfaces + 1, nfaces + 2 }, skipped.ToArray());
            Assert.Equal(m.VertexCount, mesh.VertexCount);
            Assert.Equal(9 * 2 + 4 + 3 + 1, mesh.TriangleCount);
            Assert.True(mesh.HasTriangleGroups);
            for (int t = 0; t < mesh.TriangleCount; ++t)
                Assert.Equal(m.GetFaceGroup(triToFace[t]), mesh.GetTriangleGroup(t));
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2 }, Enumerable.Range(0, 18).Select(t => mesh.GetTriangleGroup(t)).ToArray());
            // a face that DMesh3 refuses (third triangle on an edge) is reported, the rest stays
            DPolyMesh nm = new DPolyMesh();
            for (int k = 0; k < 6; ++k) nm.AppendVertex(k, k % 2, 0);
            nm.AppendTriangle(0, 1, 2); nm.AppendTriangle(1, 0, 3); nm.AppendTriangle(0, 1, 4);   // 3 triangles on edge 0-1
            var sk = new List<int>();
            DMesh3 m3 = nm.ToDMesh3(out triToFace, sk);
            Assert.Equal(new[] { 2 }, sk.ToArray());
            Assert.Equal(2, m3.TriangleCount);
        }

        [Fact]
        public void Triangulation_ConvexFace_IsFanFromVertexZero()
        {
            DPolyMesh m = new DPolyMesh();
            for (int k = 0; k < 7; ++k) m.AppendVertex(Math.Cos(k * 2 * Math.PI / 7), Math.Sin(k * 2 * Math.PI / 7), 0);
            m.AppendFace(new[] { 0, 1, 2, 3, 4, 5, 6 });
            m.AppendQuad(0, 1, 2, 3);
            Assert.Equal(Enumerable.Range(1, 5).Select(k => new Index3i(0, k, k + 1)).ToArray(), PolyMeshTestUtil.Triangulate(m, 0).ToArray());
            Assert.Equal(new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3) }, PolyMeshTestUtil.Triangulate(m, 1).ToArray());
        }

        [Fact]
        public void Triangulation_ConcaveFaces_CoverTheFaceWithOrientedTriangles()
        {
            // an arrow-head quad: vertex 2 is reflex, so the diagonal 0-2 would lie outside
            DPolyMesh m = new DPolyMesh();
            m.AppendVertex(0, 0, 0); m.AppendVertex(4, 0, 0); m.AppendVertex(1, 1, 0); m.AppendVertex(0, 4, 0);
            m.AppendQuad(0, 1, 2, 3);
            // an L-shaped hexagon
            double[] l = { 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2 };
            int b = m.VertexCount;
            for (int k = 0; k < 6; ++k) m.AppendVertex(l[2 * k], l[2 * k + 1], 0);
            m.AppendFace(new[] { b, b + 1, b + 2, b + 3, b + 4, b + 5 });
            // a comb with a vertex on a line (collinear corner)
            double[] c = { 0, 0, 3, 0, 3, 1, 2, 1, 2, 0.5, 1, 0.5, 1, 1, 0, 1 };
            int cb = m.VertexCount;
            for (int k = 0; k < 8; ++k) m.AppendVertex(c[2 * k], c[2 * k + 1], 0);
            m.AppendFace(Enumerable.Range(cb, 8).ToArray());
            for (int f = 0; f < 3; ++f) {
                var tris = PolyMeshTestUtil.Triangulate(m, f);
                Assert.Equal(m.FaceVertexCount(f) - 2, tris.Count);
                double sum = 0, polygon = 0;
                int n = m.FaceVertexCount(f);
                for (int k = 0; k < n; ++k) {
                    Vector3d p = m.GetVertex(m.GetFaceVertex(f, k)), q = m.GetVertex(m.GetFaceVertex(f, (k + 1) % n));
                    polygon += 0.5 * (p.x * q.y - q.x * p.y);
                }
                foreach (Index3i t in tris) {
                    double a = 0.5 * (m.GetVertex(t.b) - m.GetVertex(t.a)).Cross(m.GetVertex(t.c) - m.GetVertex(t.a)).z;
                    Assert.True(a > 0, $"face {f}: triangle {t} is flipped or degenerate");
                    sum += a;
                }
                Assert.Equal(polygon, sum, 12);
                Assert.True(polygon > 0);
            }
        }

        [Fact]
        public void Triangulation_PinnedExamples()
        {
            // arrow head: corner 1 is convex but the triangle (0,1,2) contains no vertex -> first ear is corner 1
            DPolyMesh m = new DPolyMesh();
            m.AppendVertex(0, 0, 0); m.AppendVertex(4, 0, 0); m.AppendVertex(1, 1, 0); m.AppendVertex(0, 4, 0);
            m.AppendQuad(0, 1, 2, 3);
            Assert.Equal(new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3) }, PolyMeshTestUtil.Triangulate(m, 0).ToArray());
            // reflex vertex at index 1: corner 1 is no ear, corner 2 is
            DPolyMesh r = new DPolyMesh();
            r.AppendVertex(0, 0, 0); r.AppendVertex(1, 1, 0); r.AppendVertex(4, 0, 0); r.AppendVertex(0, 4, 0);
            r.AppendQuad(0, 1, 2, 3);
            Assert.Equal(new[] { new Index3i(1, 2, 3), new Index3i(0, 1, 3) }, PolyMeshTestUtil.Triangulate(r, 0).ToArray());
            // a skew (non-planar) quad: Newell normal decides, still two triangles, the fan from 0
            DPolyMesh s = new DPolyMesh();
            s.AppendVertex(0, 0, 0); s.AppendVertex(1, 0, 0); s.AppendVertex(1, 1, 0.5); s.AppendVertex(0, 1, 0);
            s.AppendQuad(0, 1, 2, 3);
            Assert.Equal(new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3) }, PolyMeshTestUtil.Triangulate(s, 0).ToArray());
            // a face on a line (zero normal): fan from vertex 0
            DPolyMesh z = new DPolyMesh();
            for (int k = 0; k < 4; ++k) z.AppendVertex(k, 0, 0);
            z.AppendQuad(0, 1, 2, 3);
            Assert.Equal(new[] { new Index3i(0, 1, 2), new Index3i(0, 2, 3) }, PolyMeshTestUtil.Triangulate(z, 0).ToArray());
            // an invalid face adds nothing
            z.AppendQuad(0, 1, 1, 2);
            Assert.Empty(PolyMeshTestUtil.Triangulate(z, 1));
        }

        [Fact]
        public void Triangulation_CollinearCorner_IsNeverClippedAsADegenerateTriangle()
        {
            DPolyMesh m = new DPolyMesh();
            double[] c = { 0, 0, 1, 0, 2, 0, 2, 2, 0, 2 };
            for (int k = 0; k < 5; ++k) m.AppendVertex(c[2 * k], c[2 * k + 1], 0);
            m.AppendFace(Enumerable.Range(0, 5).ToArray());
            var tris = PolyMeshTestUtil.Triangulate(m, 0);
            Assert.Equal(3, tris.Count);
            foreach (Index3i t in tris)
                Assert.True((m.GetVertex(t.b) - m.GetVertex(t.a)).Cross(m.GetVertex(t.c) - m.GetVertex(t.a)).z > 0, $"degenerate triangle {t}");
        }

        [Fact]
        public void Triangulation_VertexTouchingAnEarCorner_DoesNotBlockTheEar()
        {
            // a pinched octagon: vertices 2 and 6 are at the same position (2, 2), so the ear at corner 2 has another ring
            // vertex on its own corner, which must not count as inside
            DPolyMesh m = new DPolyMesh();
            double[] c = { 0, 0, 4, 0, 2, 2, 4, 4, 0, 4, -2, 6, 2, 2, -1, 3 };
            for (int k = 0; k < 8; ++k) m.AppendVertex(c[2 * k], c[2 * k + 1], 0);
            m.AppendFace(Enumerable.Range(0, 8).ToArray());
            var tris = PolyMeshTestUtil.Triangulate(m, 0);
            Assert.Equal(6, tris.Count);
            Assert.Equal(new Index3i(0, 1, 2), tris[0]);       // (corner 2 is at the position of vertex 6)
            double polygon = 0, sum = 0;
            for (int k = 0; k < 8; ++k) {
                Vector3d p = m.GetVertex(k), q = m.GetVertex((k + 1) % 8);
                polygon += 0.5 * (p.x * q.y - q.x * p.y);
            }
            foreach (Index3i t in tris)
                sum += 0.5 * (m.GetVertex(t.b) - m.GetVertex(t.a)).Cross(m.GetVertex(t.c) - m.GetVertex(t.a)).z;
            Assert.Equal(polygon, sum, 12);
        }

        [Fact]
        public void Triangulation_HugeFace_IsFanAndFast()
        {
            int n = 20000;
            DPolyMesh m = new DPolyMesh();
            for (int k = 0; k < n; ++k) m.AppendVertex(Math.Cos(2 * Math.PI * k / n), Math.Sin(2 * Math.PI * k / n), 0);
            m.AppendFace(Enumerable.Range(0, n).ToArray());
            int[] map;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            DMesh3 mesh = m.ToDMesh3(out map);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "took " + watch.Elapsed);
            Assert.Equal(n - 2, mesh.TriangleCount);
            Assert.Equal(new Index3i(0, 5, 6), PolyMeshTestUtil.Triangulate(m, 0)[4]);
            // a repeated vertex in a large face is still found
            DPolyMesh r = new DPolyMesh();
            for (int k = 0; k < 40; ++k) r.AppendVertex(k, k * k, 0);
            r.AppendFace(Enumerable.Range(0, 30).Concat(new[] { 7 }).ToArray());
            Assert.False(r.IsFaceValid(0));
        }

        [Fact]
        public void Obj_NegativeGroupsRoundTrip()
        {
            DPolyMesh m = new DPolyMesh();
            m.AppendVertex(0, 0, 0); m.AppendVertex(1, 0, 0); m.AppendVertex(0, 1, 0);
            m.AppendTriangle(0, 1, 2, -3);
            m.AppendTriangle(0, 1, 2, 5);
            Assert.True(PolyMeshTestUtil.Same(m, PolyMeshTestUtil.ReadObj(PolyMeshTestUtil.ObjText(m))));
        }

        [Fact]
        public void Triangulation_SelfIntersectingBowtie_IsDeterministicAndComplete()
        {
            DPolyMesh m = new DPolyMesh();
            m.AppendVertex(0, 0, 0); m.AppendVertex(1, 1, 0); m.AppendVertex(1, 0, 0); m.AppendVertex(0, 1, 0);
            m.AppendQuad(0, 1, 2, 3);
            var a = PolyMeshTestUtil.Triangulate(m, 0);
            var b = PolyMeshTestUtil.Triangulate(m, 0);
            Assert.Equal(2, a.Count);
            Assert.Equal(a, b);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void ToDMesh3_IsInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DPolyMesh original = Concave();
            DPolyMesh moved = new DPolyMesh();
            for (int i = 0; i < original.VertexCount; ++i) moved.AppendVertex(sim.Apply(original.GetVertex(i)));
            moved.AppendFaces(Enumerable.Range(0, original.FaceCount).Select(original.FaceVertexCount).ToArray(), original.FaceIndices.ToArray(), original.FaceGroups.ToArray());

            int[] map0, map1;
            DMesh3 a = original.ToDMesh3(out map0), b = moved.ToDMesh3(out map1);
            Assert.Equal(map0, map1);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            for (int t = 0; t < a.TriangleCount; ++t) {
                Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
                Assert.Equal(a.GetTriangleGroup(t), b.GetTriangleGroup(t));
            }
            for (int v = 0; v < a.VertexCount; ++v)
                Assert.Equal(sim.Apply(a.GetVertex(v)), b.GetVertex(v));
            if (sim.IsExact) {
                for (int v = 0; v < a.VertexCount; ++v)
                    Assert.Equal(a.GetVertex(v) * sim.Scale, b.GetVertex(v));
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        static DPolyMesh Concave()
        {
            // the plane faces of the arrow, the L and the comb of the coverage test, plus the quads and n-gons, tilted out of z = const
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            int b = m.VertexCount;
            double[] l = { 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2 };
            for (int k = 0; k < 6; ++k) m.AppendVertex(l[2 * k] + 10, l[2 * k + 1], 0.3 * l[2 * k]);
            m.AppendFace(Enumerable.Range(b, 6).ToArray(), 5);
            b = m.VertexCount;
            double[] c = { 0, 0, 3, 0, 3, 1, 2, 1, 2, 0.5, 1, 0.5, 1, 1, 0, 1 };
            for (int k = 0; k < 8; ++k) m.AppendVertex(c[2 * k] + 14, c[2 * k + 1], 0.1 * c[2 * k + 1]);
            m.AppendFace(Enumerable.Range(b, 8).ToArray(), 6);
            return m;
        }

        [Fact]
        public void ToDMesh3_Repeated_GivesSameResult()
        {
            DPolyMesh m = Concave();
            int[] a, b;
            DMesh3 m1 = m.ToDMesh3(out a), m2 = m.ToDMesh3(out b);
            Assert.Equal(a, b);
            for (int t = 0; t < m1.TriangleCount; ++t) Assert.Equal(m1.GetTriangle(t), m2.GetTriangle(t));
        }

        // ---- DMesh3 and SimpleQuadMesh ----

        [Fact]
        public void FromDMesh3_Compacts_AndRoundTrips()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { EdgeVertices = 4 }.Generate().MakeDMesh();
            mesh.EnableTriangleGroups(0);
            foreach (int t in mesh.TriangleIndices()) mesh.SetTriangleGroup(t, t % 3);
            mesh.RemoveTriangle(5, true);
            mesh.RemoveTriangle(9, true);          // leaves holes in the id spaces
            DPolyMesh poly = DPolyMesh.FromDMesh3(mesh);
            Assert.Equal(mesh.VertexCount, poly.VertexCount);
            Assert.Equal(mesh.TriangleCount, poly.FaceCount);
            Assert.True(poly.Validate().IsValid);
            int[] triToFace;
            DMesh3 back = poly.ToDMesh3(out triToFace);
            Assert.Equal(mesh.TriangleCount, back.TriangleCount);
            Assert.Equal(mesh.TriangleCount, triToFace.Length);
            // same triangles as positions and groups, in triangle-id order
            int ti = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Index3i a = mesh.GetTriangle(t), b = back.GetTriangle(ti);
                for (int j = 0; j < 3; ++j) Assert.Equal(mesh.GetVertex(a[j]), back.GetVertex(b[j]));
                Assert.Equal(mesh.GetTriangleGroup(t), back.GetTriangleGroup(ti));
                ti++;
            }
        }

        [Fact]
        public void TryToSimpleQuadMesh_AllQuads()
        {
            DPolyMesh cube = PolyMeshTestUtil.QuadCube();
            for (int f = 0; f < 6; ++f) cube.SetFaceGroup(f, 10 + f);
            SimpleQuadMesh q;
            Assert.True(cube.TryToSimpleQuadMesh(out q));
            Assert.Equal(8, q.VertexCount);
            Assert.Equal(6, q.QuadCount);
            Assert.Equal(cube.FaceIndices.ToArray(), Enumerable.Range(0, 24).Select(i => q.Quads[i]).ToArray());
            Assert.Equal(cube.FaceGroups.ToArray(), Enumerable.Range(0, 6).Select(i => q.FaceGroups[i]).ToArray());
            Assert.Equal(cube.VertexCoordinates.ToArray(), Enumerable.Range(0, 24).Select(i => q.Vertices[i]).ToArray());
            Assert.Equal(MeshComponents.FaceGroups, q.Components);
        }

        [Fact]
        public void TryToSimpleQuadMesh_RejectsOthers()
        {
            SimpleQuadMesh q;
            Assert.False(new DPolyMesh().TryToSimpleQuadMesh(out q));
            Assert.Null(q);
            Assert.False(PolyMeshTestUtil.QuadsAndNgons().TryToSimpleQuadMesh(out q));
            Assert.Null(q);
            DPolyMesh bad = PolyMeshTestUtil.QuadCube();
            bad.AppendQuad(0, 1, 2, 99);
            Assert.False(bad.TryToSimpleQuadMesh(out q));
            Assert.Null(q);
            Assert.True(bad.IsAllQuads());
        }

        // ---- OBJ ----

        [Fact]
        public void Obj_RoundTrip_KeepsFacesGroupsAndCoordinatesExactly()
        {
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            m.SetVertex(0, new Vector3d(0.1, -1e-300, 1.0 / 3));
            DPolyMesh back = PolyMeshTestUtil.ReadObj(PolyMeshTestUtil.ObjText(m));
            Assert.True(PolyMeshTestUtil.Same(m, back));
            string text = PolyMeshTestUtil.ObjText(m);
            Assert.Contains("f 1 2 6 5", text);
            Assert.Contains("g mmGroup3", text);
        }

        [Fact]
        public void Obj_NoGroupsWritten_WhenAllZero()
        {
            DPolyMesh cube = PolyMeshTestUtil.QuadCube();
            string text = PolyMeshTestUtil.ObjText(cube);
            Assert.DoesNotContain("g ", text);
            Assert.True(PolyMeshTestUtil.Same(cube, PolyMeshTestUtil.ReadObj(text)));
        }

        [Fact]
        public void Obj_Writer_SkipsUnwritableFaces_KeepsRepeatedVertices()
        {
            DPolyMesh m = PolyMeshTestUtil.QuadCube();
            m.AppendFace(new[] { 0, 1 });
            m.AppendTriangle(0, 1, 77);
            m.AppendQuad(0, 1, 1, 2);
            DPolyMesh back = PolyMeshTestUtil.ReadObj(PolyMeshTestUtil.ObjText(m));
            Assert.Equal(7, back.FaceCount);
            Assert.Equal(new[] { 0, 1, 1, 2 }, back.GetFace(6));
        }

        [Fact]
        public void Obj_Reader_ParsesTokenFormsNegativeIndicesAndSkipsBadFaces()
        {
            string text = string.Join("\n",
                "# comment", "v 0 0 0", "v 1 0 0 0.5 0.5 0.5", "v 1 1 0", "v 0 1 0", "vt 0 0", "vn 0 0 1",
                "f 1/1/1 2/1/1 3/1/1 4/1/1", "f 1//1 2//1 3//1", "f -4 -3 -2 -1", "f 1 2", "f 1 2 9", "f 0 1 2", "f 1 x 2",
                "g wall", "f 1 2 3", "g wall", "f 2 3 4", "g other", "f 1 3 4", "g mmGroup7", "f 1 2 4", "g", "f 2 3 4");
            var warnings = new List<string>();
            DPolyMesh m = PolyMeshTestUtil.ReadObj(text, warnings);
            Assert.Equal(4, m.VertexCount);
            Assert.Equal(new[] { 0, 1, 2, 3 }, m.GetFace(0));
            Assert.Equal(new[] { 0, 1, 2 }, m.GetFace(1));
            Assert.Equal(new[] { 0, 1, 2, 3 }, m.GetFace(2));
            Assert.Equal(new[] { 0, 0, 0, 1, 1, 2, 7, 3 }, m.FaceGroups.ToArray());
            Assert.Single(warnings);
            Assert.Contains("skipped 4", warnings[0]);
        }

        [Fact]
        public void Obj_Reader_WithoutFacesOrVertices_IsGarbage()
        {
            DPolyMesh m;
            Assert.Equal(IOCode.GarbageDataError, PolyMeshIO.ReadOBJ(new StringReader("v 0 0 0\nv 1 0 0\n"), ReadOptions.Defaults, out m).code);
            Assert.Null(m);
            Assert.Equal(IOCode.GarbageDataError, PolyMeshIO.ReadOBJ(new StringReader("f 1 2 3\n"), ReadOptions.Defaults, out m).code);
            Assert.Null(m);
        }

        [Fact]
        public void Obj_Reader_RejectsNonFinitePositions_ByDefault()
        {
            DPolyMesh m;
            IOReadResult r = PolyMeshIO.ReadOBJ(new StringReader("v 0 0 0\nv 1e999 0 0\nv 0 1 0\nf 1 2 3\n"), ReadOptions.Defaults, out m);
            Assert.NotEqual(IOCode.Ok, r.code);
            Assert.Null(m);
        }

        [Fact]
        public void Obj_PolygonFile_IsStillReadAsFanByTheStandardReader()
        {
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            string dir = MeshFormatTestUtil.TempDir();
            try {
                string path = Path.Combine(dir, "poly.obj");
                Assert.Equal(IOCode.Ok, PolyMeshIO.WriteFile(path, m).code);
                IOReadResult r;
                DMesh3 tri = MeshFormatTestUtil.ReadAll(path, out r).Single();
                Assert.Equal(IOCode.Ok, r.code);
                int[] map;
                Assert.Equal(m.ToDMesh3(out map).TriangleCount, tri.TriangleCount);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        // ---- PLY ----

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Ply_RoundTrip_KeepsFacesGroupsAndCoordinatesExactly(bool binary)
        {
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            m.SetVertex(1, new Vector3d(0.1, -1e-300, 1.0 / 3));
            byte[] data = PolyMeshTestUtil.PlyBytes(m, binary);
            Assert.True(PolyMeshTestUtil.Same(m, PolyMeshTestUtil.ReadPly(data)));
            string header = PolyMeshTestUtil.Header(data);
            Assert.Contains("property list uchar int vertex_indices", header);
            Assert.Contains("property int face_group", header);
            Assert.Contains(binary ? "binary_little_endian" : "ascii", header);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Ply_GroupPropertyOmittedWhenAllZero_FloatPrecision(bool binary)
        {
            DPolyMesh cube = PolyMeshTestUtil.QuadCube();
            byte[] data = PolyMeshTestUtil.PlyBytes(cube, binary, false);
            Assert.DoesNotContain("face_group", PolyMeshTestUtil.Header(data));
            DPolyMesh back = PolyMeshTestUtil.ReadPly(data);
            Assert.Equal(cube.FaceIndices.ToArray(), back.FaceIndices.ToArray());
            Assert.Equal(cube.VertexCoordinates.Select(v => (double)(float)v).ToArray(), back.VertexCoordinates.ToArray());
            Assert.Contains("property float x", PolyMeshTestUtil.Header(data));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Ply_FaceWithMoreThan255Vertices_UsesIntCount(bool binary)
        {
            DPolyMesh m = new DPolyMesh();
            int n = 300;
            for (int k = 0; k < n; ++k) m.AppendVertex(Math.Cos(2 * Math.PI * k / n), Math.Sin(2 * Math.PI * k / n), 0);
            m.AppendFace(Enumerable.Range(0, n).ToArray(), 2);
            m.AppendTriangle(0, 1, 2);
            byte[] data = PolyMeshTestUtil.PlyBytes(m, binary);
            Assert.Contains("property list int int vertex_indices", PolyMeshTestUtil.Header(data));
            Assert.True(PolyMeshTestUtil.Same(m, PolyMeshTestUtil.ReadPly(data)));
        }

        [Fact]
        public void Ply_VerticesOnly_AndEmpty()
        {
            DPolyMesh pts = new DPolyMesh();
            pts.AppendVertex(1, 2, 3);
            DPolyMesh back = PolyMeshTestUtil.ReadPly(PolyMeshTestUtil.PlyBytes(pts, true));
            Assert.Equal(1, back.VertexCount);
            Assert.Equal(0, back.FaceCount);
            Assert.Equal(0, PolyMeshTestUtil.ReadPly(PolyMeshTestUtil.PlyBytes(new DPolyMesh(), false)).VertexCount);
        }

        [Fact]
        public void Ply_Reader_SkipsInvalidFacesWithWarning_KeepsRepeatedVertices()
        {
            string text = "ply\nformat ascii 1.0\nelement vertex 4\nproperty float x\nproperty float y\nproperty float z\n"
                + "element face 5\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n1 1 0\n0 1 0\n"
                + "4 0 1 2 3\n2 0 1\n3 0 1 9\n3 0 -1 2\n4 0 1 1 2\n";
            DPolyMesh m = PolyMeshTestUtil.ReadPly(Encoding.ASCII.GetBytes(text));
            Assert.Equal(2, m.FaceCount);
            Assert.Equal(new[] { 0, 1, 1, 2 }, m.GetFace(1));
            Assert.Equal(PolyFaceIssues.RepeatedVertex, m.Validate().Issues.Single());
        }

        [Fact]
        public void Ply_StandardReader_StillFanTriangulates_AndIgnoresFaceGroup()
        {
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            byte[] data = PolyMeshTestUtil.PlyBytes(m, true);
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult r = new PLYReader().Read(new BinaryReader(new MemoryStream(data)), ReadOptions.Defaults, builder);
            Assert.Equal(IOCode.Ok, r.code);
            DMesh3 tri = builder.Meshes.Single();
            Assert.Equal(9 * 2 + 4 + 3 + 1, tri.TriangleCount);
            // the pentagon (face 10) is the fan (0,1,2) (0,2,3) (0,3,4)
            int p = m.GetFaceVertex(10, 0);
            Assert.Equal(new Index3i(p, p + 1, p + 2), tri.GetTriangle(9 * 2 + 4));
            Assert.False(tri.HasTriangleGroups);
        }

        [Fact]
        public void Ply_PolygonBuilder_PassesFaceGroupFromNegativeAndLargeValues()
        {
            string text = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n"
                + "element face 3\nproperty list uchar int vertex_indices\nproperty int face_group\nend_header\n0 0 0\n1 0 0\n0 1 0\n"
                + "3 0 1 2 -5\n3 0 1 2 2147483647\n3 0 1 2 4\n";
            DPolyMesh m = PolyMeshTestUtil.ReadPly(Encoding.ASCII.GetBytes(text));
            Assert.Equal(new[] { -5, int.MaxValue, 4 }, m.FaceGroups.ToArray());
        }

        // ---- files ----

        [Fact]
        public void WriteFileReadFile_ByExtension()
        {
            DPolyMesh m = PolyMeshTestUtil.QuadsAndNgons();
            string dir = MeshFormatTestUtil.TempDir();
            try {
                foreach (string name in new[] { "a.obj", "b.ply" }) {
                    string path = Path.Combine(dir, name);
                    Assert.Equal(IOCode.Ok, PolyMeshIO.WriteFile(path, m).code);
                    DPolyMesh back;
                    Assert.Equal(IOCode.Ok, PolyMeshIO.ReadFile(path, ReadOptions.Defaults, out back).code);
                    Assert.True(PolyMeshTestUtil.Same(m, back), name);
                }
                string bin = Path.Combine(dir, "c.ply");
                Assert.Equal(IOCode.Ok, PolyMeshIO.WriteFile(bin, m, true).code);
                DPolyMesh back2;
                Assert.Equal(IOCode.Ok, PolyMeshIO.ReadFile(bin, ReadOptions.Defaults, out back2).code);
                Assert.True(PolyMeshTestUtil.Same(m, back2));
                Assert.Equal(IOCode.FormatNotSupportedError, PolyMeshIO.WriteFile(Path.Combine(dir, "x.stl"), m).code);
                Assert.Equal(IOCode.FormatNotSupportedError, PolyMeshIO.ReadFile(Path.Combine(dir, "x.stl"), ReadOptions.Defaults, out back2).code);
                Assert.Equal(IOCode.FileAccessError, PolyMeshIO.ReadFile(Path.Combine(dir, "missing.obj"), ReadOptions.Defaults, out back2).code);
                Assert.Null(back2);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        // the triangle writers are untouched: their output for a fixed mesh stays as it was
        [Fact]
        public void TriangleWriters_OutputUnchanged()
        {
            DMesh3 mesh = new DMesh3();
            mesh.AppendVertex(new Vector3d(0, 0, 0)); mesh.AppendVertex(new Vector3d(1, 0, 0)); mesh.AppendVertex(new Vector3d(0, 1, 0));
            mesh.AppendTriangle(0, 1, 2);
            var sw = new StringWriter();
            new PLYWriter().Write(sw, new List<WriteMesh> { new WriteMesh(mesh) }, WriteOptions.Defaults);
            Assert.Equal("ply\nformat ascii 1.0\ncomment written by geometry3Sharp\nelement vertex 3\nproperty double x\nproperty double y\nproperty double z\n"
                + "element face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n", sw.ToString().Replace("\r\n", "\n"));
        }
    }
}
