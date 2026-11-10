using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T1 finding R25: FastWindingNumber at a query point equal to a point of the set was NaN (the exact
    /// evaluation divided 0 by 0), so a mesh vertex as query point gave no answer.
    /// </summary>
    public class PointAABBTree3CoincidentQueryTests
    {
        [Fact]
        public void QueryAtAPointOfTheSet_IsFinite()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6);
            var tree = new PointAABBTree3(m, true) { FWNAreaEstimateF = v => 4 * Math.PI / m.VertexCount };
            foreach (int v in m.VertexIndices().Take(20)) {
                double w = tree.FastWindingNumber(m.GetVertex(v));
                Assert.True(MathUtil.IsFinite(w), "vertex " + v + ": " + w);
            }
        }

        [Fact]
        public void QueryAtAPoint_EqualsTheSumOverTheOtherPoints()
        {
            // a single triangle at the origin: the query at its first vertex sees the other two points
            var m = new DMesh3(true, false, false);
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(Vector3d.AxisX); m.AppendVertex(Vector3d.AxisY);
            for (int v = 0; v < 3; ++v) m.SetVertexNormal(v, new Vector3f(0, 0, 1));
            m.AppendTriangle(0, 1, 2);
            var tree = new PointAABBTree3(m, true) { FWNAreaEstimateF = v => 0.5 / 3 };
            double atVertex = tree.FastWindingNumber(Vector3d.Zero);
            Assert.True(MathUtil.IsFinite(atVertex));
            double expected = 0;
            for (int v = 1; v < 3; ++v) {
                Vector3d d = m.GetVertex(v);
                expected += (0.5 / 3) / (4 * Math.PI) * ((Vector3d)m.GetVertexNormal(v)).Dot(d / (d.Length * d.Length * d.Length));
            }
            Assert.Equal(expected, atVertex, 12);
        }
    }
}
