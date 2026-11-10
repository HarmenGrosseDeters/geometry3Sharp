using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // (its own file: it needs the internal Reducer.TrianglesTouch, so the other new tests still compile against the base library)
    public class ReducerTrianglesTouchTests
    {
        [Fact]
        public void TrianglesTouch_FindsContactInAnAxisPlaneAndAtASharedPoint()
        {
            // the obstacle lies in the plane x = 0: its boxes are flat, which an open box test never overlaps
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(0, 1, 0)), m.AppendVertex(new Vector3d(0, 0, 1)));
            var tree = new DMeshAABBTree3(m, true);
            // coplanar and overlapping
            Assert.True(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(0, 0.1, 0.1), new Vector3d(0, 0.3, 0.1), new Vector3d(0, 0.1, 0.3)) }));
            // only a corner in common, the rest on one side of the plane
            Assert.True(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(-1, -1, 0)) }));
            // crossing the plane
            Assert.True(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(-0.5, 0.2, 0.2), new Vector3d(0.5, 0.2, 0.2), new Vector3d(0, 0.4, 0.4)) }));
            // parallel, apart
            Assert.False(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(1e-3, 0.1, 0.1), new Vector3d(1e-3, 0.3, 0.1), new Vector3d(1e-3, 0.1, 0.3)) }));
            Assert.False(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(2, 2, 2), new Vector3d(3, 2, 2), new Vector3d(2, 3, 2)) }));
        }
    }
}
