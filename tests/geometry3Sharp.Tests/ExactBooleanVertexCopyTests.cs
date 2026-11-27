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

        [Fact]
        public void AppendTriangle_SeveralTrianglesAtAFullVertex_ShareOneCopy_OneManifoldSeam()
        {
            // a strip of four triangles (c, p_i, p_i+1) at the full vertex c: they were a flap each (4 copies); now one copy holds the fan of the strip
            var fan = new ExactBooleanVertexLimitTests.Fan(Max);
            DMesh3 m = ExactBooleanVertexLimitTests.Appended(fan);
            int c = Max;
            int[] p = new int[5];
            for (int i = 0; i < 5; ++i) p[i] = m.AppendVertex(new Vector3d(5 + i, 5, 0));
            var vertexPoint = new List<int>(fan.VertexPoint);
            for (int i = 0; i < 5; ++i) vertexPoint.Add(1000 + i);
            ExactMeshBoolean eb = AnyBoolean();
            int vertices = m.VertexCount, triangles = m.TriangleCount;
            for (int i = 0; i < 4; ++i)
                eb.append_triangle(m, c, p[i], p[i + 1], vertexPoint);
            Assert.Equal(vertices + 1, m.VertexCount);                // one copy, not four
            Assert.Equal(vertices + 1, vertexPoint.Count);
            Assert.Equal(triangles + 4, m.TriangleCount);
            int copy = vertices;
            Assert.Equal(4, m.GetVtxTriangleCount(copy));
            Assert.False(m.IsBowtieVertex(copy));
            Assert.Equal(Max + 1, m.VerticesRefCounts.RawRefCounts[c]);
            // the seam: the strip is a fan of its own with the boundary of four rim edges and two end spokes; the closed fan stays closed
            int boundary = 0;
            foreach (int e in m.BoundaryEdgeIndices()) ++boundary;
            Assert.Equal(Max + 6, boundary);                         // (the rim of the fan of 32,766 is a boundary too)
            Assert.Equal(4, eb.NonManifoldTriangles);
        }

        [Fact]
        public void AppendTriangle_TheCopyOfAFullVertex_IsNotKeptForAnotherMesh()
        {
            ExactMeshBoolean eb = AnyBoolean();
            int[] copies = new int[2];
            for (int round = 0; round < 2; ++round) {
                var fan = new ExactBooleanVertexLimitTests.Fan(Max);
                DMesh3 m = ExactBooleanVertexLimitTests.Appended(fan);
                int p = m.AppendVertex(new Vector3d(5, 5, 0)), q = m.AppendVertex(new Vector3d(6, 5, 0));
                if (round == 1) { m.AppendVertex(new Vector3d(7, 5, 0)); m.AppendVertex(new Vector3d(8, 5, 0)); }      // (the id of the copy differs between the meshes)
                var vertexPoint = new List<int>(fan.VertexPoint);
                while (vertexPoint.Count < m.VertexCount) vertexPoint.Add(1000 + vertexPoint.Count);
                int vertices = m.VertexCount;
                int r = m.AppendVertex(new Vector3d(9, 5, 0));
                vertexPoint.Add(5000);
                int withR = m.VertexCount;
                eb.append_triangle(m, Max, p, q, vertexPoint);
                eb.append_triangle(m, Max, q, r, vertexPoint);        // the second triangle of the strip takes the copy of the first, in this mesh too
                Assert.Equal(withR + 1, m.VertexCount);               // a new copy in each mesh, though the full vertex has the same id, and one for the two
                Assert.Equal(2, m.GetVtxTriangleCount(withR));
                vertices = withR;
                copies[round] = vertices;
            }
            Assert.NotEqual(copies[0], copies[1]);
        }

        [Fact]
        public void AppendTriangle_ADegeneratePieceAtAFullVertex_IsNotLost()
        {
            // the piece (a, a, c) with c full: the mask that copies c only keeps the two equal corners, and AppendTriangle refuses a triangle with two equal vertices
            // (its result was ignored: the triangle and the copy were lost). Such a mask is skipped: the first mask that keeps no equal corners and not the full vertex
            // (copies of the first and the third corner) adds the triangle
            var fan = new ExactBooleanVertexLimitTests.Fan(Max);
            DMesh3 m = ExactBooleanVertexLimitTests.Appended(fan);
            int c = Max;
            int a = m.AppendVertex(new Vector3d(5, 5, 0));
            var vertexPoint = new List<int>(fan.VertexPoint) { 1000 };
            ExactMeshBoolean eb = AnyBoolean();
            int triangles = m.TriangleCount, vertices = m.VertexCount;
            eb.append_triangle(m, a, a, c, vertexPoint);
            Assert.Equal(triangles + 1, m.TriangleCount);
            Assert.Equal(vertices + 2, m.VertexCount);
            Assert.Equal(vertices + 2, vertexPoint.Count);
            Index3i t = m.GetTriangle(triangles);
            Assert.True(t.a >= vertices && t.c >= vertices && t.b == a);
            Assert.NotEqual(t.a, t.b);
            Assert.NotEqual(t.c, t.b);
            Assert.Equal(0, eb.DroppedTriangles);
            Assert.Equal(Max + 1, m.VerticesRefCounts.RawRefCounts[c]);
        }

        [Fact]
        public void AppendTriangle_AtAnEdgeThatCannotBeAdded_EachTriangleTakesACopyOfItsOwn()
        {
            // the old behaviour for a vertex that is not full: no sharing of copies (the map is only for the vertices that are full)
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), x = m.AppendVertex(new Vector3d(0.5, 1, 0)), y = m.AppendVertex(new Vector3d(0.5, -1, 0));
            int c1 = m.AppendVertex(new Vector3d(0.5, 2, 1)), c2 = m.AppendVertex(new Vector3d(0.5, 3, 1));
            Assert.True(m.AppendTriangle(a, b, x) >= 0);
            Assert.True(m.AppendTriangle(b, a, y) >= 0);           // the edge (a, b) has its two triangles: another one that runs it a -> b cannot be added
            var vertexPoint = new List<int> { 0, 1, 2, 3, 4, 5 };
            ExactMeshBoolean eb = AnyBoolean();
            int vertices = m.VertexCount;
            eb.append_triangle(m, a, b, c1, vertexPoint);
            eb.append_triangle(m, a, b, c2, vertexPoint);
            Assert.Equal(vertices + 2, m.VertexCount);
            Assert.Equal(2, eb.NonManifoldTriangles);
            Assert.Equal(m.GetVertex(a), m.GetVertex(vertices));
            Assert.Equal(m.GetVertex(a), m.GetVertex(vertices + 1));
            Assert.Equal(1, m.GetVtxTriangleCount(vertices));
            Assert.Equal(1, m.GetVtxTriangleCount(vertices + 1));
        }

        [Fact]
        public void AppendTriangle_WhenTheCopyIsFullToo_TakesAnotherCopy()
        {
            // a strip of 32,767 triangles at the full vertex: the first copy takes 32,766, the next triangle a second copy; none is lost
            // (the full vertex has the largest id: find_edge scans the edges of the smaller one, so no call walks the 32,766 edges of the full vertex)
            int n = Max + 1;
            var m = new DMesh3();
            int[] p = new int[n + 1];
            for (int i = 0; i <= n; ++i) p[i] = m.AppendVertex(new Vector3d(5 + 1e-3 * i, 5, 0));
            int rim0 = m.VertexCount;
            for (int i = 0; i < Max; ++i) m.AppendVertex(new Vector3d(Math.Cos(2 * Math.PI * i / Max), Math.Sin(2 * Math.PI * i / Max), 0));
            int c = m.AppendVertex(Vector3d.Zero);
            for (int i = 0; i < Max; ++i) Assert.True(m.AppendTriangle(rim0 + i, rim0 + (i + 1) % Max, c) >= 0);
            Assert.Equal(Max, m.GetVtxTriangleCount(c));
            var vertexPoint = new List<int>();
            while (vertexPoint.Count < m.VertexCount) vertexPoint.Add(vertexPoint.Count);
            ExactMeshBoolean eb = AnyBoolean();
            int vertices = m.VertexCount, triangles = m.TriangleCount;
            for (int i = 0; i < n; ++i)
                eb.append_triangle(m, c, p[i], p[i + 1], vertexPoint);
            Assert.Equal(triangles + n, m.TriangleCount);
            Assert.Equal(0, eb.DroppedTriangles);
            Assert.Equal(vertices + 2, m.VertexCount);                // the full vertex, then the first copy when it is full: two copies
            Assert.Equal(Max, m.GetVtxTriangleCount(vertices));
            Assert.Equal(1, m.GetVtxTriangleCount(vertices + 1));
            Assert.Equal(Max, m.GetVtxTriangleCount(c));
            Assert.Equal(n, eb.NonManifoldTriangles);
        }
    }
}
