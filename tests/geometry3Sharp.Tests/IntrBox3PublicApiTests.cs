using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact box decisions through the public API only (no internal members), so these tests compile against the base library:
    /// cases from the audit where the old 1e-8 slack and the rounded dot products gave a false hit, a false miss or a wrong type.
    /// </summary>
    public class IntrBox3PublicApiTests
    {
        static readonly AxisAlignedBox3d Unit = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));

        [Fact]
        public void Line_1e9OutsideAFace_IsAMiss()
        {
            var line = new Line3d(new Vector3d(0, 0, 1 + 1e-9), new Vector3d(1, 0, 0));
            Assert.False(new IntrLine3AxisAlignedBox3(line, Unit).Find());
            Assert.False(new IntrLine3AxisAlignedBox3(line, Unit).Test());
            var obox = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            Assert.False(new IntrLine3Box3(line, obox).Find());
            Assert.False(new IntrLine3Box3(line, obox).Test());
        }

        [Fact]
        public void Ray_1e9OutsideAFace_IsAMiss_AtSmallScale()
        {
            // the slack of 1e-8 is larger than a box of size 1e-6
            double s = 1e-6;
            var box = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(s, s, s));
            var ray = new Ray3d(new Vector3d(-s, 0.5 * s, s + 1e-9 * s), new Vector3d(1, 0, 0));
            Assert.False(new IntrRay3AxisAlignedBox3(ray, box).Find());
            Assert.False(new IntrRay3AxisAlignedBox3(ray, box).Test());
            var obox = new Box3d(new Vector3d(0.5 * s, 0.5 * s, 0.5 * s), new Vector3d(0.5 * s, 0.5 * s, 0.5 * s));
            Assert.False(new IntrRay3Box3(ray, obox).Find());
        }

        [Fact]
        public void Line_ThroughACorner_IsAPoint()
        {
            var line = new Line3d(new Vector3d(2, 0, 1), new Vector3d(-1, 1, 0).Normalized);
            var f = new IntrLine3AxisAlignedBox3(line, Unit);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.Equal(1, f.Quantity);
            Assert.Equal(new Vector3d(1, 1, 1), f.Point0);
        }

        [Fact]
        public void Segment_1e9ShortOfTheBox_IsAMiss()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var seg = new Segment3d(new Vector3d(-3, 0, 0), new Vector3d(-1 - 1e-9, 0, 0));
            Assert.False(new IntrSegment3Box3(seg, box, true).Find());
            Assert.False(new IntrSegment3Box3(seg, box, true).Test());
            var touching = new Segment3d(new Vector3d(-3, 0, 0), new Vector3d(-1, 0, 0));
            Assert.True(new IntrSegment3Box3(touching, box, true).Find());
        }

        [Fact]
        public void Line_ParallelToAFaceAtADistance_GivesNoPointFarAway()
        {
            // a line almost parallel to the z faces of a unit box: the contact points stay on the box
            var line = new Line3d(new Vector3d(-1, 0.5, 0.5), new Vector3d(1, 0, 1e-9).Normalized);
            var f = new IntrLine3AxisAlignedBox3(line, Unit);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Segment, f.Type);
            Assert.InRange(f.Point0.x, -1e-6, 1 + 1e-6);
            Assert.InRange(f.Point1.x, -1e-6, 1 + 1e-6);
            Assert.InRange(f.Point0.z, 0.5 - 1e-6, 0.5 + 1e-6);
        }

        [Fact]
        public void NonFiniteInput_IsNeverAHit()
        {
            var ray = new Ray3d(new Vector3d(double.NaN, 0.5, 0.5), new Vector3d(1, 0, 0));
            Assert.False(new IntrRay3AxisAlignedBox3(ray, Unit).Find());
            Assert.False(new IntrRay3AxisAlignedBox3(ray, Unit).Test());
            var obox = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            Assert.False(new IntrRay3Box3(ray, obox).Find());
        }
    }
}
