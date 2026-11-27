using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRobustRepair.OrientedReference (#1548): the gate (null for a welded, consistently oriented input: it pays nothing and keeps its
    /// results) and the copy (welded, consistently oriented, outward) for an unwelded soup or an input with a flipped triangle.
    /// </summary>
    public class RobustRepairOrientedReferenceUnitTests
    {
        static DMesh3 Box() { return AlphaWrapFeaturePassTests.PolyCube(4, 3, 2, (x, y, z) => true); }

        static DMesh3 Soup(DMesh3 m, Func<int, bool> flip)
        {
            var soup = new DMesh3();
            int i = 0;
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                int a = soup.AppendVertex(m.GetVertex(t.a)), b = soup.AppendVertex(m.GetVertex(t.b)), c = soup.AppendVertex(m.GetVertex(t.c));
                if (flip(i++)) soup.AppendTriangle(a, c, b);
                else soup.AppendTriangle(a, b, c);
            }
            return soup;
        }

        [Fact]
        public void WeldedConsistent_IsNull()
        {
            Assert.Null(MeshRobustRepair.OrientedReference(Box()));
            // (an open mesh with a hole: boundary vertices, but nothing within the tolerance of another)
            DMesh3 open = Box();
            open.RemoveTriangle(open.TriangleIndices().First());
            Assert.Null(MeshRobustRepair.OrientedReference(open));
            Assert.Null(MeshRobustRepair.OrientedReference(new DMesh3()));
        }

        [Fact]
        public void ConsistentSoup_IsWeldedAndClosed()
        {
            DMesh3 r = MeshRobustRepair.OrientedReference(Soup(Box(), i => false));
            Assert.NotNull(r);
            Assert.True(r.IsClosed());
            Assert.Equal(Box().VertexCount, r.VertexCount);
            Assert.Equal(Box().TriangleCount, r.TriangleCount);
            Assert.Equal(24, Invariance.Volume(r), 9);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        public void FlippedSoup_IsWeldedOrientedOutward(int k)
        {
            DMesh3 r = MeshRobustRepair.OrientedReference(Soup(Box(), i => i % k == k - 1));
            Assert.NotNull(r);
            Assert.True(r.IsClosed());
            Assert.Equal(104, r.TriangleCount);
            Assert.Equal(24, Invariance.Volume(r), 9);
        }

        [Fact]
        public void FlippedSoup_AllFlipped_IsOutward()
        {
            DMesh3 r = MeshRobustRepair.OrientedReference(Soup(Box(), i => true));
            Assert.NotNull(r);
            Assert.Equal(24, Invariance.Volume(r), 9);
        }

        // a welded mesh with one reversed triangle: no weld, but a conflict
        [Fact]
        public void WeldedWithOneFlippedTriangle_IsOriented()
        {
            DMesh3 m = Box();
            m.ReverseTriOrientation(m.TriangleIndices().ElementAt(7));
            DMesh3 r = MeshRobustRepair.OrientedReference(m);
            Assert.NotNull(r);
            Assert.Equal(24, Invariance.Volume(r), 9);
        }

        // vertices of a soup that are apart by more than the tolerance are not welded
        [Fact]
        public void VerticesApartBeyondTheTolerance_AreNotWelded()
        {
            DMesh3 soup = Soup(Box(), i => false);
            // one triangle moved away by a tenth: its edges no longer meet the others
            int tid = soup.TriangleIndices().First();
            Index3i t = soup.GetTriangle(tid);
            foreach (int v in new[] { t.a, t.b, t.c })
                soup.SetVertex(v, soup.GetVertex(v) + new Vector3d(0, 0, 0.1));
            DMesh3 r = MeshRobustRepair.OrientedReference(soup);
            Assert.NotNull(r);
            Assert.False(r.IsClosed());
        }
    }
}
