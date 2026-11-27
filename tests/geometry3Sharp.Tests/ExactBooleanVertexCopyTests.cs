using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1693: where ExactMeshResultBuilder.TryBuild gives null (a result vertex with more than 32,766 triangles), the serial assembly appends the triangles one by
    /// one. A triangle at a vertex that is full is refused by AppendTriangle (#1684): append_triangle takes a copy of that vertex for it, as for a triangle at an
    /// edge that cannot be added, instead of losing the triangle. The builder tests are in ExactBooleanVertexLimitTests.
    /// </summary>
    public class ExactBooleanVertexCopyTests
    {
        const int Max = 32766;      // triangles at one vertex

        static ExactMeshBoolean AnyBoolean()
        {
            DMesh3 box = new TrivialBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            return new ExactMeshBoolean(box, new DMesh3(box), ExactMeshBoolean.Operations.Union);
        }

        static Index3i Rot(int a, int b, int c, int k) { return k == 0 ? new Index3i(a, b, c) : (k == 1 ? new Index3i(c, a, b) : new Index3i(b, c, a)); }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AppendTriangle_AtAVertexWithTheMostTriangles_TakesACopyOfThatVertex(int corner)
        {
            // the result mesh of the serial assembly with a full vertex c (a closed fan of 32,766 triangles); the next triangle at c cannot be added by AppendTriangle
            var fan = new ExactBooleanVertexLimitTests.Fan(Max);
            DMesh3 m = ExactBooleanVertexLimitTests.Appended(fan);
            int c = Max;
            int p = m.AppendVertex(new Vector3d(5, 5, 0)), q = m.AppendVertex(new Vector3d(6, 5, 0));
            var vertexPoint = new List<int>(fan.VertexPoint) { Max + 1, Max + 2 };
            ExactMeshBoolean eb = AnyBoolean();
            int triangles = m.TriangleCount, vertices = m.VertexCount;
            Index3i t = Rot(c, p, q, corner);
            eb.append_triangle(m, t.a, t.b, t.c, vertexPoint);
            // the triangle is there (not lost), at a copy of c: a new vertex at the same position, with the point of c; c keeps its 32,766
            Assert.Equal(triangles + 1, m.TriangleCount);
            Assert.Equal(vertices + 1, m.VertexCount);
            Assert.Equal(vertices + 1, vertexPoint.Count);
            Assert.Equal(c, vertexPoint[vertexPoint.Count - 1]);
            Assert.Equal(Max + 1, m.VerticesRefCounts.RawRefCounts[c]);
            int copy = vertices;
            Assert.Equal(m.GetVertex(c), m.GetVertex(copy));
            Index3i added = m.GetTriangle(triangles);
            Assert.True(added.a == copy || added.b == copy || added.c == copy);
            Assert.False(added.a == c || added.b == c || added.c == c);
            Assert.Equal(1, eb.NonManifoldTriangles);       // (reported, as for a triangle that cannot be added at an edge)
        }

        [Fact]
        public void AppendTriangle_BelowTheLimit_TakesNoCopy()
        {
            var fan = new ExactBooleanVertexLimitTests.Fan(Max - 1);
            DMesh3 m = ExactBooleanVertexLimitTests.Appended(fan);
            int p = m.AppendVertex(new Vector3d(5, 5, 0)), q = m.AppendVertex(new Vector3d(6, 5, 0));
            var vertexPoint = new List<int>(fan.VertexPoint) { Max + 1, Max + 2 };
            ExactMeshBoolean eb = AnyBoolean();
            int vertices = m.VertexCount;
            eb.append_triangle(m, Max - 1, p, q, vertexPoint);       // c = Max - 1 here
            Assert.Equal(vertices, m.VertexCount);
            Assert.Equal(Max, m.VerticesRefCounts.RawRefCounts[Max - 1] - 1);
            Assert.Equal(0, eb.NonManifoldTriangles);
        }

        [Fact]
        public void AppendTriangle_AtAnEdgeThatCannotBeAdded_StillTakesTheVertexThatHasRoom()
        {
            // the triangle (a, b, c) cannot be added because it would run the edge (a, b) like the triangle there; c has 32,765 triangles, so one more fits: the copy is a
            // copy of a (the first mask), not of c
            var fan = new ExactBooleanVertexLimitTests.Fan(Max - 1);
            DMesh3 m = ExactBooleanVertexLimitTests.Appended(fan);
            int c = Max - 1;
            int a = m.AppendVertex(new Vector3d(5, 5, 0)), b = m.AppendVertex(new Vector3d(6, 5, 0)), x = m.AppendVertex(new Vector3d(5.5, 6, 0)), y = m.AppendVertex(new Vector3d(5.5, 4, 0));
            Assert.True(m.AppendTriangle(a, b, x) >= 0);
            Assert.True(m.AppendTriangle(b, a, y) >= 0);
            var vertexPoint = new List<int>(fan.VertexPoint) { 100, 101, 102, 103 };
            ExactMeshBoolean eb = AnyBoolean();
            int triangles = m.TriangleCount, vertices = m.VertexCount;
            eb.append_triangle(m, a, b, c, vertexPoint);
            Assert.Equal(triangles + 1, m.TriangleCount);
            Assert.Equal(vertices + 1, m.VertexCount);
            Index3i added = m.GetTriangle(triangles);
            Assert.Equal(new Index3i(vertices, b, c), added);                 // the copy of a, with b and c as they were
            Assert.Equal(m.GetVertex(a), m.GetVertex(vertices));
            Assert.Equal(Max + 1 - 1 + 1, m.VerticesRefCounts.RawRefCounts[c]);   // c: 1 + 32,765 + the triangle = 32,767
            Assert.Equal(1, eb.NonManifoldTriangles);
        }
    }
}
