using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // (its own file, only the API the base library has: Reducer.KeepClearOf with its defaults, so this fails on the old code;
    // ReducerSweptClearTests needs the new KeepClearOfSwept and TetrahedraMeet)
    public class ReducerSweptClearBaseTests
    {
        [Fact]
        public void KeepClearOf_Default_ACollapseCannotJumpOverTheObstacle()
        {
            // the box [-1,1]^3 on a 13 x 13 grid per face, its top raised by up to 0.05 in a disk of radius 0.5, and a small
            // obstacle triangle just under the dome's flank: flattening the dome would leave it outside
            var gen = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = 13 };
            DMesh3 box = gen.Generate().MakeDMesh();
            foreach (int v in box.VertexIndices()) {
                Vector3d x = box.GetVertex(v);
                if (x.z > 1 - 1e-9)
                    box.SetVertex(v, new Vector3d(x.x, x.y, 1 + 0.05 * Math.Max(0, 1 - (x.x * x.x + x.y * x.y) / 0.25)));
            }
            Vector3d p0 = new Vector3d(0.25, -0.06, 1.032), p1 = new Vector3d(0.25, 0.06, 1.032), p2 = new Vector3d(0.27, 0, 1.031);
            var o = new DMesh3();
            o.AppendTriangle(o.AppendVertex(p0), o.AppendVertex(p1), o.AppendVertex(p2));
            Assert.True(box.WindingNumber(p2) > 0.5, "the obstacle starts inside");
            var r = new Reducer(box) { KeepClearOf = new DMeshAABBTree3(o, true), PreventSelfIntersections = true };
            r.ReduceToTriangleCount(12);
            Assert.True(box.IsClosed(), "closed");
            var tree = new DMeshAABBTree3(box, true);
            foreach (Vector3d p in new[] { p0, p1, p2, (p0 + p1 + p2) / 3 })
                Assert.True(Math.Abs(tree.WindingNumber(p) - 1) < 0.01, $"{p} not enclosed");
        }
    }
}
