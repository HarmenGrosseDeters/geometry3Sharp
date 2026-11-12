using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> ConstrainedMesh2.ToDMesh3: the 2.5D output (owner decision 2026-11-12) </summary>
    public class ConstrainedMesh2ToDMesh3Tests
    {
        /// <summary> a square with an inner region (label 3), heights on the corners, refined to 25 degrees </summary>
        static ConstrainedMesh2Input Terrain()
        {
            var input = new ConstrainedMesh2Input {
                Points = { new Vector2d(0, 0), new Vector2d(8, 0), new Vector2d(8, 8), new Vector2d(0, 8),
                           new Vector2d(2, 2), new Vector2d(5, 2), new Vector2d(5, 6), new Vector2d(2, 6) },
                Heights = new List<double> { 0, 4, 10, 6, 1, 2, 7, 3 },
            };
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            input.Rings.Add(new Mesh2Ring(new[] { 4, 5, 6, 7 }));
            input.Regions.Add(Mesh2Region.BySeed(new Vector2d(3, 4), 3));
            return input;
        }

        static ConstrainedMesh2 Mesh(ConstrainedMesh2Input input) => new QualityMesh2 { MinAngle = 25 }.Compute(input).Mesh;

        /// <summary> the result is the same mesh: ids, xy, z = height, groups = labels, orientation towards +z, a valid DMesh3 </summary>
        static void AssertSameMesh(ConstrainedMesh2 m, DMesh3 d, bool groups = true)
        {
            Assert.True(d.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(m.Vertices.Count, d.VertexCount);
            Assert.Equal(m.TriangleCount, d.TriangleCount);
            for (int i = 0; i < m.Vertices.Count; ++i)
                Assert.Equal(new Vector3d(m.Vertices[i].x, m.Vertices[i].y, m.Heights == null ? 0 : m.Heights[i]), d.GetVertex(i));
            Assert.Equal(groups, d.HasTriangleGroups);
            for (int t = 0; t < m.TriangleCount; ++t) {
                Assert.Equal(m.Triangles[t], d.GetTriangle(t));
                if (groups) Assert.Equal(m.TriangleLabels[t], d.GetTriangleGroup(t));
                Index3i tri = d.GetTriangle(t);
                Vector3d a = d.GetVertex(tri.a), b = d.GetVertex(tri.b), c = d.GetVertex(tri.c);
                Assert.True((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 0, "triangle " + t + " is not counter-clockwise in xy");
            }
        }

        [Fact]
        public void Heights_BecomeZ_AndLabels_BecomeGroups()
        {
            var m = Mesh(Terrain());
            Assert.NotNull(m.Heights);
            Assert.True(m.Vertices.Count > m.InputCount, "the quality mesh should have Steiner points");
            Assert.Contains(3, m.TriangleLabels); Assert.Contains(0, m.TriangleLabels);
            var d = m.ToDMesh3();
            AssertSameMesh(m, d);
            Assert.True(d.IsClosed() == false && d.Vertices().All(p => p.z >= 0 && p.z <= 10));
        }

        [Fact]
        public void WithoutHeights_TheMeshIsFlatAtZeroWithoutGroups()
        {
            var input = Terrain(); input.Heights = null;
            var m = Mesh(input);
            Assert.Null(m.Heights);
            var d = m.ToDMesh3(labelsAsGroups: false);
            AssertSameMesh(m, d, groups: false);
            Assert.All(d.Vertices(), p => Assert.Equal(0.0, p.z));
        }

        /// <summary> an exact duplicate keeps its slot and is in no triangle: an isolated vertex, so the ids still match </summary>
        [Fact]
        public void UnusedVertices_StayIsolated_IdsMatch()
        {
            var input = new ConstrainedMesh2Input { Points = { new Vector2d(0, 0), new Vector2d(4, 4), new Vector2d(0, 4), new Vector2d(4, 0), new Vector2d(4, 4) } };
            input.Heights = new List<double> { 0, 8, 1, 3, 9 };
            input.Segments.Add(new Mesh2Segment(0, 1)); input.Segments.Add(new Mesh2Segment(2, 3));
            var m = new ConstrainedDelaunayMesh2().Compute(input).Mesh;
            var d = m.ToDMesh3();
            AssertSameMesh(m, d);
            Assert.Equal(1, m.InputToVertex[4]);
            Assert.Equal(0, d.GetVtxEdgeCount(4));
            Assert.Equal(new Vector3d(2, 2, 4), d.GetVertex(5));   // the crossing, its height along the segment given first
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        /// <summary> under every similarity (xy moved, heights scaled by the scale) the result is the mesher's mesh; at the 2^k scales it is the
        /// scaled mesh bit for bit </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Terrain(); var moved = Terrain();
            for (int i = 0; i < moved.Points.Count; ++i) {
                var q = s.Apply(new Vector3d(plain.Points[i].x, plain.Points[i].y, 0));
                moved.Points[i] = q.xy; moved.Heights[i] = s.ApplyLength(plain.Heights[i]);
            }
            moved.Regions[0].Seed = s.Apply(new Vector3d(plain.Regions[0].Seed.x, plain.Regions[0].Seed.y, 0)).xy;
            var mm0 = Mesh(moved);
            Assert.Contains(3, mm0.TriangleLabels);
            var mm = mm0;
            var b = mm.ToDMesh3();
            AssertSameMesh(mm, b);
            if (s.IsExact) {
                var a = Mesh(plain).ToDMesh3();
                Assert.Equal(a.VertexCount, b.VertexCount);
                for (int v = 0; v < a.VertexCount; ++v) Assert.Equal(a.GetVertex(v) * s.Scale, b.GetVertex(v));
                for (int t = 0; t < a.TriangleCount; ++t) { Assert.Equal(a.GetTriangle(t), b.GetTriangle(t)); Assert.Equal(a.GetTriangleGroup(t), b.GetTriangleGroup(t)); }
            }
        }

        /// <summary> two calls give equal, independent meshes </summary>
        [Fact]
        public void TwoCalls_GiveEqualIndependentMeshes()
        {
            var m = Mesh(Terrain());
            var a = m.ToDMesh3(); var b = m.ToDMesh3();
            a.SetVertex(0, new Vector3d(-1, -1, -1));
            Assert.Equal(new Vector3d(0, 0, 0), b.GetVertex(0));
            Assert.Equal(new Vector3d(0, 0, 0), m.ToDMesh3().GetVertex(0));
        }
    }
}
