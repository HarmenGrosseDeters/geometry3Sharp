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
        public void Regression_Line_1e9OutsideAFace_IsAMiss()
        {
            var line = new Line3d(new Vector3d(0, 0, 1 + 1e-9), new Vector3d(1, 0, 0));
            Assert.False(new IntrLine3AxisAlignedBox3(line, Unit).Find());
            Assert.False(new IntrLine3AxisAlignedBox3(line, Unit).Test());
            var obox = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5));
            Assert.False(new IntrLine3Box3(line, obox).Find());
            Assert.False(new IntrLine3Box3(line, obox).Test());
        }

        [Fact]
        public void Regression_Ray_1e9OutsideAFace_IsAMiss_AtSmallScale()
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
        public void Regression_Line_ThroughACorner_IsAPoint()
        {
            var line = new Line3d(new Vector3d(2, 0, 1), new Vector3d(-1, 1, 0).Normalized);
            var f = new IntrLine3AxisAlignedBox3(line, Unit);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.Equal(1, f.Quantity);
            Assert.Equal(new Vector3d(1, 1, 1), f.Point0);
        }

        [Fact]
        public void Regression_Segment_1e9ShortOfTheBox_IsAMiss()
        {
            var box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1));
            var seg = new Segment3d(new Vector3d(-3, 0, 0), new Vector3d(-1 - 1e-9, 0, 0));
            Assert.False(new IntrSegment3Box3(seg, box, true).Find());
            Assert.False(new IntrSegment3Box3(seg, box, true).Test());
            var touching = new Segment3d(new Vector3d(-3, 0, 0), new Vector3d(-1, 0, 0));
            Assert.True(new IntrSegment3Box3(touching, box, true).Find());
        }

        [Fact]
        public void Regression_Line_ParallelToAFaceAtADistance_GivesNoPointFarAway()
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

        // A difference origin - center that overflows, times an axis component that is exactly zero, was inf * 0 = NaN: every comparison
        // false, so the slab constrained nothing and a certified contact came out for a miss (review of #1432).
        static Box3d OverflowBox() { return new Box3d(new Vector3d(-1e308, 1e308, 0), Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ, Vector3d.One); }

        [Fact]
        public void Overflow_OriginMinusCenter_TimesAZeroAxisComponent_IsNoCertifiedHit()
        {
            Box3d box = OverflowBox();
            Vector3d o = new Vector3d(1e308, -1e308, 0), d = new Vector3d(1, 1, 1).Normalized;
            Ray3d ray = new Ray3d(o, d);
            Assert.False(new IntrRay3Box3(ray, box).Find());
            Assert.False(IntrRay3Box3.Intersects(ref ray, ref box));
            Assert.False(new IntrRay3Box3(ray, box).Test());
            Assert.False(new IntrLine3Box3(new Line3d(o, d), box).Find());
            Assert.False(new IntrLine3Box3(new Line3d(o, d), box).Test());
            Assert.False(new IntrSegment3Box3(new Segment3d(o, o + d * 1e300), box, true).Test());
        }

        [Fact]
        public void Overflow_OriginMinusCenter_AnOriginInTheBoxStaysAHit()
        {
            // the same far-away box, the ray starts inside it: the overflow guard must not turn a hit into a miss
            Box3d box = OverflowBox();
            Vector3d d = new Vector3d(1, 1, 1).Normalized;
            Ray3d ray = new Ray3d(new Vector3d(-1e308, 1e308, 0), d);
            Assert.True(IntrRay3Box3.Intersects(ref ray, ref box));
            Assert.True(new IntrRay3Box3(ray, box).Test());
            Assert.True(new IntrLine3Box3(new Line3d(ray.Origin, d), box).Test());
        }

        [Fact]
        public void Overflow_EveryEntry_TestAgreesWithFind_ForExtremeCentersAndZeroDirectionComponents()
        {
            double[] big = { 1e308, -1e308, 1e154, -1e154, 0 };
            double[] dir = { 0, 1, -1 };
            int n = 0;
            foreach (double cx in big) foreach (double cy in big) foreach (double ox in big) foreach (double oy in big)
            foreach (double dx in dir) foreach (double dy in dir) foreach (double dz in dir) {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                Vector3d c = new Vector3d(cx, cy, 0), o = new Vector3d(ox, oy, 0), d = new Vector3d(dx, dy, dz).Normalized;
                Box3d box = new Box3d(c, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ, Vector3d.One);
                AxisAlignedBox3d aab = new AxisAlignedBox3d(c - Vector3d.One, c + Vector3d.One);
                Ray3d ray = new Ray3d(o, d);
                var r = new IntrRay3Box3(ray, box); bool rf = r.Find();
                Assert.Equal(rf, new IntrRay3Box3(ray, box).Test());
                Assert.Equal(rf, IntrRay3Box3.Intersects(ref ray, ref box));
                var l = new IntrLine3Box3(new Line3d(o, d), box); bool lf = l.Find();
                Assert.Equal(lf, new IntrLine3Box3(new Line3d(o, d), box).Test());
                var ra = new IntrRay3AxisAlignedBox3(ray, aab); bool raf = ra.Find();
                Assert.Equal(raf, new IntrRay3AxisAlignedBox3(ray, aab).Test());
                Assert.Equal(raf, IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab));
                var la = new IntrLine3AxisAlignedBox3(new Line3d(o, d), aab); bool laf = la.Find();
                Assert.Equal(laf, new IntrLine3AxisAlignedBox3(new Line3d(o, d), aab).Test());
                Vector3d e = o + d * 1e308;
                if (double.IsInfinity(e.x) == false && double.IsInfinity(e.y) == false && double.IsInfinity(e.z) == false) {
                    var sg = new IntrSegment3Box3(new Segment3d(o, e), box, true); bool sf = sg.Find();
                    Assert.Equal(sf, new IntrSegment3Box3(new Segment3d(o, e), box, true).Test());
                }
                n++;
            }
            Assert.True(n > 1000);
        }

        [Fact]
        public void NonFiniteBoxOrExpand_IsNeverAHit_ForEveryComponent()
        {
            double bad1 = double.NaN, bad2 = double.PositiveInfinity;
            Ray3d ray = new Ray3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1, 0, 0));
            var line = new Line3d(ray.Origin, ray.Direction);
            // axis-aligned box: each of the six coordinates, and an expand that is not finite or makes a face overflow
            for (int k = 0; k < 6; k++) foreach (double bad in new[] { bad1, bad2 }) {
                Vector3d mn = new Vector3d(0, 0, 0), mx = new Vector3d(1, 1, 1);
                if (k < 3) mn[k] = bad; else mx[k - 3] = bad;
                var aab = new AxisAlignedBox3d(mn, mx);
                Assert.False(new IntrRay3AxisAlignedBox3(ray, aab).Find()); Assert.False(new IntrRay3AxisAlignedBox3(ray, aab).Test());
                Assert.False(new IntrLine3AxisAlignedBox3(line, aab).Find()); Assert.False(new IntrLine3AxisAlignedBox3(line, aab).Test());
                Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref aab));
            }
            // an expand that is not finite is no box; one that is finite (double.MaxValue, as DCurve3BoxTree starts with) keeps a hit, unless it
            // pushes a face beyond the range (1.7e308 + MaxValue)
            AxisAlignedBox3d small = new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref small, double.NaN));
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref small, double.PositiveInfinity));
            Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref small, double.NegativeInfinity));
            Assert.True(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref small, double.MaxValue));
            for (int k = 0; k < 3; k++) {
                Vector3d mx = new Vector3d(1, 1, 1); mx[k] = 1.7e308;
                var b2 = new AxisAlignedBox3d(new Vector3d(0, 0, 0), mx);
                Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref b2, double.MaxValue));
                Vector3d mn = new Vector3d(0, 0, 0); mn[k] = -1.7e308;
                var b3 = new AxisAlignedBox3d(mn, new Vector3d(1, 1, 1));
                Assert.False(IntrRay3AxisAlignedBox3.Intersects(ref ray, ref b3, double.MaxValue));
            }
            // oriented box: center, axes, extents and expand, one component at a time
            for (int k = 0; k < 15; k++) foreach (double bad in new[] { bad1, bad2 }) {
                Vector3d c = new Vector3d(0.5, 0.5, 0.5), ax = Vector3d.AxisX, ay = Vector3d.AxisY, az = Vector3d.AxisZ, e = new Vector3d(1, 1, 1);
                Vector3d[] vs = { c, ax, ay, az, e };
                vs[k / 3][k % 3] = bad;
                var box = new Box3d(vs[0], vs[1], vs[2], vs[3], vs[4]);
                Assert.False(new IntrRay3Box3(ray, box).Find()); Assert.False(new IntrRay3Box3(ray, box).Test());
                Assert.False(IntrRay3Box3.Intersects(ref ray, ref box));
                Assert.False(new IntrLine3Box3(line, box).Find()); Assert.False(new IntrLine3Box3(line, box).Test());
            }
            Box3d ok = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1, 1, 1));
            Assert.True(IntrRay3Box3.Intersects(ref ray, ref ok));
            Assert.True(IntrRay3Box3.Intersects(ref ray, ref ok, double.MaxValue));
            Assert.False(IntrRay3Box3.Intersects(ref ray, ref ok, double.NaN));
            Assert.False(IntrRay3Box3.Intersects(ref ray, ref ok, double.PositiveInfinity));
            Box3d huge = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1.7e308, 1, 1));
            Assert.False(IntrRay3Box3.Intersects(ref ray, ref huge, double.MaxValue));
        }
    }
}
