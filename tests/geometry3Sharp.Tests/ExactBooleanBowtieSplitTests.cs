using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean splits the bowtie vertices of a result with an open input (the per-piece classification can keep
    /// fans that meet only at a vertex): one vertex per fan, deterministic (ExactBooleanOpenPerPieceTests). In its own
    /// file because it uses internals the base library lacks (the per-piece tests then still run against the base).
    /// </summary>
    public class ExactBooleanBowtieSplitTests
    {
        [Fact]
        public void SplitBowties_HandBuilt_EachFanItsOwnVertex()
        {
            // vertex 0 shared by two closed fans (the apexes of two tetrahedra) and an open fan of two triangles; vertex
            // 1 is not a bowtie. The fan with the smallest triangle id keeps vertex 0, the others get a copy each, in the
            // order of their smallest triangle id, at the same position and with the same point id
            var m = new DMesh3();
            var o = new Vector3d(0.25, -0.5, 1.5);
            m.AppendVertex(o);
            Vector3d[] p = { new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(1, 0, 1), new Vector3d(-1, 0, 0), new Vector3d(-1, 0, 1), new Vector3d(-1, 1, 0),
                new Vector3d(0, -1, 2), new Vector3d(0.5, -1, 2), new Vector3d(1, -1, 2) };
            foreach (Vector3d q in p)
                m.AppendVertex(o + q);
            void tet(int b, int c, int d) { m.AppendTriangle(0, c, b); m.AppendTriangle(0, b, d); m.AppendTriangle(0, d, c); m.AppendTriangle(b, c, d); }
            tet(1, 2, 3);
            tet(4, 5, 6);
            m.AppendTriangle(0, 7, 8);
            m.AppendTriangle(0, 8, 9);
            Assert.True(m.IsBowtieVertex(0));
            var vertexPoint = Enumerable.Range(0, m.MaxVertexID).Select(v => 100 + v).ToList();
            Assert.Equal(1, ExactMeshBoolean.split_bowties(m, vertexPoint));
            Assert.Equal(12, m.VertexCount);
            Assert.Equal(new[] { 100, 100 }, vertexPoint.Skip(10));
            Assert.Equal(o, m.GetVertex(10));
            Assert.Equal(o, m.GetVertex(11));
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            // the first tetrahedron (triangles 0-2) keeps vertex 0, the second (4-6) gets 10, the open fan (8, 9) 11
            int[] owner = { 0, 0, 0, -1, 10, 10, 10, -1, 11, 11 };
            for (int t = 0; t < owner.Length; ++t)
                if (owner[t] >= 0)
                    Assert.True(m.GetTriangle(t).a == owner[t], "triangle " + t);
            Assert.Equal(new[] { 1, 2, 3 }, m.GetTriangle(3).array.OrderBy(x => x));
            // nothing to split now
            Assert.Equal(0, ExactMeshBoolean.split_bowties(m, vertexPoint));
            Assert.Equal(12, vertexPoint.Count);
        }
    }
}
