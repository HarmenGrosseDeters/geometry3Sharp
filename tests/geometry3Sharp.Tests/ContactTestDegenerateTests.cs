using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ContactTest.TrianglesTouch (MeshMeshCut's exact contact test) for triangles without an area, which are a segment or a point:
    /// segment against segment (coplanar or skew), point against point and point against segment.
    /// </summary>
    public class ContactTestDegenerateTests
    {
        static Triangle3d T(Vector3d a, Vector3d b, Vector3d c) => new Triangle3d(a, b, c);
        static Vector3d V(double x, double y, double z) => new Vector3d(x, y, z);

        // collinear corners: the segment from a to b with a middle corner
        static Triangle3d Seg(Vector3d a, Vector3d b) => T(a, b, (a + b) * 0.5);
        static Triangle3d Pt(Vector3d a) => T(a, a, a);

        static void BothWays(bool expected, Triangle3d t, Triangle3d u)
        {
            Assert.Equal(expected, ContactTest.TrianglesTouch(t, u));
            Assert.Equal(expected, ContactTest.TrianglesTouch(u, t));
        }

        [Fact]
        public void SkewSegments_DoNotTouch_ButCrossingOnesDo()
        {
            Triangle3d s = Seg(V(0, 0, 0), V(1, 0, 0));
            BothWays(false, s, Seg(V(0.5, 1, -1), V(0.5, 1, 1)));        // skew: not coplanar
            BothWays(true, s, Seg(V(0.5, -1, 0), V(0.5, 1, 0)));         // crossing in one point
            BothWays(false, s, Seg(V(2, -1, 0), V(2, 1, 0)));            // coplanar, disjoint
        }

        [Fact]
        public void Points_TouchOnlyWhenEqual()
        {
            BothWays(true, Pt(V(1, 2, 3)), Pt(V(1, 2, 3)));
            BothWays(false, Pt(V(1, 2, 3)), Pt(V(1, 2, 4)));
        }

        [Fact]
        public void PointAgainstSegment_NeedsTheSegmentToContainIt()
        {
            Triangle3d s = Seg(V(0, 0, 0), V(2, 0, 0));
            BothWays(true, Pt(V(1, 0, 0)), s);       // inside the segment
            BothWays(true, Pt(V(2, 0, 0)), s);       // at its end
            BothWays(false, Pt(V(3, 0, 0)), s);      // on the line, beyond the end
            BothWays(false, Pt(V(1, 1, 0)), s);      // off the line
        }
    }
}
