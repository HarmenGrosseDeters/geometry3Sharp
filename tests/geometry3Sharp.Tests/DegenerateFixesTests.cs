using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Queries with degenerate input: triangles with collinear or coincident vertices (upstream #85) and
    /// segments of zero length (upstream #137). See docs/Analyse-2026-10.md.
    /// </summary>
    public class DegenerateFixesTests
    {
        static Triangle3d Tri(Vector3d a, Vector3d b, Vector3d c) { return new Triangle3d(a, b, c); }

        // the triangles from upstream #85
        static readonly Triangle3d PointTri = Tri(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero);
        static readonly Triangle3d SegmentTri = Tri(Vector3d.Zero, Vector3d.Zero, new Vector3d(0, 1, 0));
        static readonly Triangle3d FarTri = Tri(new Vector3d(0, 0, 1), new Vector3d(0, 1, 1), new Vector3d(1, 1, 1));

        // a triangle in the z=0 plane, and collinear 'triangles' along a vertical line through it or beside it
        static readonly Triangle3d Flat = Tri(Vector3d.Zero, new Vector3d(2, 0, 0), new Vector3d(0, 2, 0));
        static Triangle3d Vertical(double x, double y) { return Tri(new Vector3d(x, y, -1), new Vector3d(x, y, 1), new Vector3d(x, y, 0.5)); }

        static bool TestQuery(Triangle3d a, Triangle3d b) { return new IntrTriangle3Triangle3(a, b).Test(); }
        static bool FindQuery(Triangle3d a, Triangle3d b) { return new IntrTriangle3Triangle3(a, b).Find(); }
        static bool StaticQuery(Triangle3d a, Triangle3d b) { return IntrTriangle3Triangle3.Intersects(ref a, ref b); }

        [Fact]
        public void TriTri_Issue85_DegenerateSeparated()
        {
            // the segment x=z=0, 0<=y<=1 is one unit away from FarTri
            foreach (var q in new System.Func<Triangle3d, Triangle3d, bool>[] { TestQuery, FindQuery, StaticQuery }) {
                Assert.False(q(SegmentTri, FarTri));
                Assert.False(q(FarTri, SegmentTri));
                Assert.False(q(PointTri, FarTri));
                Assert.False(q(FarTri, PointTri));
            }
        }

        [Fact]
        public void TriTri_Issue85_DegenerateTouching()
        {
            // the point is the end of the segment
            Assert.True(TestQuery(PointTri, SegmentTri));
            Assert.True(StaticQuery(SegmentTri, PointTri));
            Assert.True(FindQuery(PointTri, SegmentTri));
        }

        [Fact]
        public void TriTri_SegmentThroughTriangle()
        {
            Triangle3d through = Vertical(0.5, 0.5), beside = Vertical(1.5, 1.5);
            foreach (var q in new System.Func<Triangle3d, Triangle3d, bool>[] { TestQuery, FindQuery, StaticQuery }) {
                Assert.True(q(through, Flat));
                Assert.True(q(Flat, through));
                Assert.False(q(beside, Flat));
                Assert.False(q(Flat, beside));
            }
        }

        [Fact]
        public void TriTri_SegmentAboveTriangle()
        {
            // a horizontal segment above the triangle: the in-plane axes of Flat do not separate it
            Triangle3d above = Tri(new Vector3d(0.2, 0.2, 1), new Vector3d(0.6, 0.6, 1), new Vector3d(0.4, 0.4, 1));
            foreach (var q in new System.Func<Triangle3d, Triangle3d, bool>[] { TestQuery, FindQuery, StaticQuery }) {
                Assert.False(q(above, Flat));
                Assert.False(q(Flat, above));
            }
        }

        [Fact]
        public void TriTri_TwoDegenerate()
        {
            Triangle3d alongX = Tri(new Vector3d(-1, 0, 0), new Vector3d(1, 0, 0), Vector3d.Zero);
            Triangle3d alongY = Tri(new Vector3d(0, -1, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0.5, 0));
            Triangle3d alongYAbove = Tri(new Vector3d(0, -1, 1), new Vector3d(0, 1, 1), new Vector3d(0, 0.5, 1));
            foreach (var q in new System.Func<Triangle3d, Triangle3d, bool>[] { TestQuery, FindQuery, StaticQuery }) {
                Assert.True(q(alongX, alongY));
                Assert.False(q(alongX, alongYAbove));
                Assert.False(q(alongYAbove, alongX));
            }
        }

        [Fact]
        public void TriTri_NonDegenerateUnchanged()
        {
            Triangle3d crossing = Tri(new Vector3d(0.5, 0.5, -1), new Vector3d(0.5, 0.5, 1), new Vector3d(1.5, 0.5, 0));
            Triangle3d apart = Tri(new Vector3d(0.5, 0.5, 1), new Vector3d(0.5, 0.5, 2), new Vector3d(1.5, 0.5, 1.5));
            foreach (var q in new System.Func<Triangle3d, Triangle3d, bool>[] { TestQuery, FindQuery, StaticQuery }) {
                Assert.True(q(crossing, Flat));
                Assert.False(q(apart, Flat));
            }
        }

        // --- Segment2d.Intersects with zero-length segments (upstream #137) ---

        [Fact]
        public void Segment2d_Issue137_ZeroLengthOutside()
        {
            var a = new Segment2d(new Vector2d(56.6598662, 34.32530824), new Vector2d(58.18386662, 35.87470824));
            var b = new Segment2d(new Vector2d(58.05, 34.1964), new Vector2d(58.05, 34.1964));
            Assert.False(a.Intersects(b));
            Assert.False(b.Intersects(a));
        }

        [Fact]
        public void Segment2d_ZeroLengthOnSegment()
        {
            var a = new Segment2d(new Vector2d(0, 0), new Vector2d(2, 0));
            var p = new Segment2d(new Vector2d(1, 0), new Vector2d(1, 0));
            var beyond = new Segment2d(new Vector2d(3, 0), new Vector2d(3, 0));
            Assert.True(a.Intersects(p));
            Assert.True(p.Intersects(a));
            Assert.False(a.Intersects(beyond));
            Assert.False(beyond.Intersects(a));
            Assert.True(p.Intersects(new Segment2d(new Vector2d(1, 0), new Vector2d(1, 0))));
            Assert.False(p.Intersects(beyond));
        }
    }
}
