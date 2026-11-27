using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Metamorphic relations of two further operations, on the procedural solids of CurvedSolids: MeshOffset
    /// (offset(d) contains offset(d') for d > d', also for insets; the volumes follow) and Remesher with a projection target
    /// (the volume and the area stay within a tolerance of the input's, the result stays clean).
    /// </summary>
    public class OperationMetamorphicTests
    {
        static DMesh3 Solid(string name)
        {
            switch (name) {
                case "sphere": return CurvedSolids.Sphere(1, Vector3d.Zero, 7);
                case "torus": return CurvedSolids.Torus(1, 0.35, Vector3d.Zero);
                default: return CurvedSolids.Rotated(CurvedSolids.Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)), new Vector3d(1, 2, 3), 30, Vector3d.Zero);
            }
        }

        public static IEnumerable<object[]> Names() { foreach (string n in new[] { "sphere", "torus", "rotated box" }) yield return new object[] { n }; }

        static DMesh3 Offset(DMesh3 m, double d, out MeshOffset op)
        {
            op = new MeshOffset(m, d) { RelativeCellSize = 0.5 };
            Assert.True(op.Compute(), $"offset {d}: {op.ResultStatus}");
            Assert.Equal(MeshOffset.Status.Success, op.ResultStatus);
            Assert.True(op.SelfIntersectionFree, $"offset {d}: self-intersections");
            return op.Result;
        }

        // every vertex of the inner surface is inside the outer one, at least the gap between them minus the band deviations from it
        static void AssertInside(DMesh3 inner, DMesh3 outer, double gap, double slack, string what)
        {
            var tree = new DMeshAABBTree3(outer, true);
            tree.WindingNumber(Vector3d.Zero);   // builds the winding data
            int checkedVertices = 0;
            foreach (int v in inner.VertexIndices().Where((x, i) => inner.VertexCount < 200 || i % 5 == 0)) {
                Vector3d p = inner.GetVertex(v);
                double w = tree.WindingNumber(p);
                Assert.True(w > 0.5, $"{what}: vertex {v} outside (winding {w:G4})");
                int t = tree.FindNearestTriangle(p, out double dsq);
                Assert.True(t >= 0 && Math.Sqrt(dsq) >= gap - slack, $"{what}: vertex {v} at distance {Math.Sqrt(dsq):G4} from the outer surface, gap {gap}");
                ++checkedVertices;
            }
            Assert.True(checkedVertices >= 8, what + ": too few vertices checked");
        }

        [Theory, MemberData(nameof(Names))]
        public void MeshOffset_LargerOffsetContainsTheSmaller_AndTheVolumesFollow(string name)
        {
            DMesh3 m = Solid(name);
            DMesh3 o1 = Offset(m, 0.2, out MeshOffset a), o2 = Offset(m, 0.4, out MeshOffset b);
            double slack = 2 * (a.BandDeviation + b.BandDeviation) + 1e-9;
            AssertInside(m, o1, 0.2, slack, name + " input in offset 0.2");
            AssertInside(o1, o2, 0.2, slack, name + " offset 0.2 in offset 0.4");
            double v0 = BooleanOracle.Volume(m), v1 = BooleanOracle.Volume(o1), v2 = BooleanOracle.Volume(o2);
            Assert.True(v0 < v1 && v1 < v2, $"{name}: volumes {v0:G6} {v1:G6} {v2:G6} not increasing");
        }

        [Theory, MemberData(nameof(Names))]
        public void MeshOffset_Inset_IsContainedInTheInputAndInTheSmallerInset(string name)
        {
            DMesh3 m = Solid(name);
            DMesh3 i1 = Offset(m, -0.08, out MeshOffset a), i2 = Offset(m, -0.16, out MeshOffset b);
            double slack = 2 * (a.BandDeviation + b.BandDeviation) + 1e-9;
            AssertInside(i1, m, 0.08, slack, name + " inset 0.08 in input");
            AssertInside(i2, i1, 0.08, slack, name + " inset 0.16 in inset 0.08");
            double v0 = BooleanOracle.Volume(m), v1 = BooleanOracle.Volume(i1), v2 = BooleanOracle.Volume(i2);
            Assert.True(v0 > v1 && v1 > v2 && v2 > 0, $"{name}: volumes {v0:G6} {v1:G6} {v2:G6} not decreasing");
        }

        [Theory, MemberData(nameof(Names))]
        public void Remesher_WithProjection_KeepsVolumeAndAreaAndStaysClean(string name)
        {
            DMesh3 original = Solid(name), m = new DMesh3(original);
            double v0 = BooleanOracle.Volume(original), a0 = Invariance.Area(original);
            var r = new Remesher(m) { PreventSelfIntersections = true };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(original));
            r.SetTargetEdgeLength(0.15);
            for (int i = 0; i < 10; ++i) r.BasicRemeshPass();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            double v = BooleanOracle.Volume(m), a = Invariance.Area(m);
            Assert.True(Math.Abs(v - v0) <= 0.05 * v0, $"{name}: volume {v0:G6} -> {v:G6}");
            Assert.True(Math.Abs(a - a0) <= 0.05 * a0, $"{name}: area {a0:G6} -> {a:G6}");
        }
    }
}
