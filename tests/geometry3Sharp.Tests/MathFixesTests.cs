using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Bugs in math/ from the class-by-class review (docs/Analyse-2026-10.md, section 2.1).
    /// </summary>
    public class MathFixesTests
    {
        // --- MathUtil.SolveQuadratic ---

        [Fact]
        public void SolveQuadratic_ZeroLinearTerm()
        {
            // x^2 - 4 = 0: b = 0 made Math.Sign(b) = 0, so t = 0 and maxT = c / 0
            Assert.True(MathUtil.SolveQuadratic(1, 0, -4, out double minT, out double maxT));
            Assert.Equal(-2, minT, 12);
            Assert.Equal(2, maxT, 12);
        }

        [Fact]
        public void SolveQuadratic_Linear()
        {
            // 2x - 4 = 0 has the single root 2; a = 0 made minT = t / 0
            Assert.True(MathUtil.SolveQuadratic(0, 2, -4, out double minT, out double maxT));
            Assert.Equal(2, minT, 12);
            Assert.Equal(2, maxT, 12);
        }

        [Fact]
        public void SolveQuadratic_RegularCasesUnchanged()
        {
            Assert.True(MathUtil.SolveQuadratic(1, -3, 2, out double minT, out double maxT));
            Assert.Equal(1, minT, 12);
            Assert.Equal(2, maxT, 12);
            Assert.False(MathUtil.SolveQuadratic(1, 0, 4, out minT, out maxT));
        }

        // --- Matrix2d/f.Inverse returned the shared, mutable Zero instance ---

        [Fact]
        public void Matrix2d_SingularInverse_DoesNotShareZero()
        {
            Matrix2d inv = new Matrix2d(1, 2, 2, 4).Inverse();
            inv.m00 = 5;
            Assert.Equal(0, Matrix2d.Zero.m00);
        }

        [Fact]
        public void Matrix2f_SingularInverse_DoesNotShareZero()
        {
            Matrix2f inv = new Matrix2f(1, 2, 2, 4).Inverse();
            inv.m00 = 5;
            Assert.Equal(0, Matrix2f.Zero.m00);
        }

        // --- Box3d/f.MinExtent ---

        [Fact]
        public void Box3_MinExtent()
        {
            var b = new Box3d(Vector3d.Zero, new Vector3d(3, 2, 1));
            Assert.Equal(1, b.MinExtent);
            var f = new Box3f(Vector3f.Zero, new Vector3f(3, 2, 1));
            Assert.Equal(1, f.MinExtent);
        }

        // --- Vector2dTuple4 setter ---

        [Fact]
        public void Vector2dTuple4_Setter()
        {
            var t = new Vector2dTuple4();
            for (int i = 0; i < 4; ++i)
                t[i] = new Vector2d(i, 10 * i);
            for (int i = 0; i < 4; ++i)
                Assert.Equal(new Vector2d(i, 10 * i), t[i]);
        }

        // --- Ray3f.DistanceSquared ---

        [Fact]
        public void Ray3f_DistanceSquared_PointBehindOrigin()
        {
            var ray = new Ray3f(Vector3f.Zero, Vector3f.AxisX);
            // the closest point of the ray to (-3,4,0) is its origin
            Assert.Equal(25, ray.DistanceSquared(new Vector3f(-3, 4, 0)), 4);
            Assert.Equal(16, ray.DistanceSquared(new Vector3f(3, 4, 0)), 4);
        }

        // --- Quaternion slerp: q and -q are the same rotation ---

        [Fact]
        public void Quaterniond_Slerp_OppositeSigns()
        {
            Quaterniond p = Quaterniond.AxisAngleD(Vector3d.AxisY, 10);
            Quaterniond q = Quaterniond.AxisAngleD(Vector3d.AxisY, 30);
            Quaterniond qn = new Quaterniond(-q.x, -q.y, -q.z, -q.w);
            Quaterniond expected = Quaterniond.AxisAngleD(Vector3d.AxisY, 20);
            Quaterniond r = Quaterniond.Slerp(p, qn, 0.5);
            Assert.Equal(1, r.Length, 9);
            Assert.True(Math.Abs(Math.Abs(r.Dot(expected)) - 1) < 1e-9, $"slerp gave {r}");

            // identity to -identity: the same rotation, no zero quaternion
            Quaterniond id = Quaterniond.Identity, nid = new Quaterniond(0, 0, 0, -1);
            Quaterniond r2 = Quaterniond.Slerp(id, nid, 0.5);
            Assert.Equal(1, r2.Length, 9);
        }

        [Fact]
        public void Quaternionf_Slerp_OppositeSigns()
        {
            Quaternionf p = Quaternionf.AxisAngleD(Vector3f.AxisY, 10);
            Quaternionf q = Quaternionf.AxisAngleD(Vector3f.AxisY, 30);
            Quaternionf qn = new Quaternionf(-q.x, -q.y, -q.z, -q.w);
            Quaternionf expected = Quaternionf.AxisAngleD(Vector3f.AxisY, 20);
            Quaternionf r = Quaternionf.Slerp(p, qn, 0.5f);
            Assert.Equal(1, r.Length, 4);
            Assert.True(Math.Abs(Math.Abs(r.Dot(expected)) - 1) < 1e-4, $"slerp gave {r}");
        }

        // --- MathUtil.PlaneAngleSignedD (2D) ---

        [Fact]
        public void PlaneAngleSignedD_Antiparallel()
        {
            Assert.Equal(180, Math.Abs(MathUtil.PlaneAngleSignedD(new Vector2d(1, 0), new Vector2d(-1, 0))), 9);
            Assert.Equal(180, Math.Abs(MathUtil.PlaneAngleSignedD(new Vector2f(1, 0), new Vector2f(-1, 0))), 4);
            Assert.Equal(90, MathUtil.PlaneAngleSignedD(new Vector2d(1, 0), new Vector2d(0, 1)), 9);
            Assert.Equal(-90, MathUtil.PlaneAngleSignedD(new Vector2d(1, 0), new Vector2d(0, -1)), 9);
        }

        // --- Interval negation and negative scaling ---

        [Fact]
        public void Interval1d_NegateAndNegativeScale()
        {
            var i = new Interval1d(1, 2);
            Interval1d n = -i;
            Assert.Equal(-2, n.a); Assert.Equal(-1, n.b);
            Interval1d m = i * -3;
            Assert.Equal(-6, m.a); Assert.Equal(-3, m.b);
            Interval1d p = i * 2;
            Assert.Equal(2, p.a); Assert.Equal(4, p.b);
        }

        [Fact]
        public void Interval1i_NegateAndNegativeScale()
        {
            var i = new Interval1i(1, 2);
            Interval1i n = -i;
            Assert.Equal(-2, n.a); Assert.Equal(-1, n.b);
            Interval1i m = i * -3;
            Assert.Equal(-6, m.a); Assert.Equal(-3, m.b);
        }

        // --- AxisAlignedBox3d <-> 3f ---

        [Fact]
        public void AxisAlignedBox3_EmptyStaysEmpty()
        {
            // an empty box that takes in one point is that point; the conversion made it infinite
            AxisAlignedBox3f f = (AxisAlignedBox3f)AxisAlignedBox3d.Empty;
            f.Contain(new Vector3f(1, 2, 3));
            Assert.True(f.Min == new Vector3f(1, 2, 3) && f.Max == new Vector3f(1, 2, 3), $"empty 3d box became {f}");
            AxisAlignedBox3d d = AxisAlignedBox3f.Empty;
            d.Contain(new Vector3d(1, 2, 3));
            Assert.True(d.Min == new Vector3d(1, 2, 3) && d.Max == new Vector3d(1, 2, 3), $"empty 3f box became {d}");
        }

        // --- Segment2d.WhichSide: the instance method swapped P0/P1 against the static one and its doc ---

        [Fact]
        public void Segment2d_WhichSide_MatchesStaticAndDoc()
        {
            var seg = new Segment2d(new Vector2d(0, 0), new Vector2d(2, 0));
            Vector2d left = new Vector2d(1, 1), right = new Vector2d(1, -1), on = new Vector2d(1, 0);
            Vector2d p0 = seg.P0, p1 = seg.P1;
            Assert.Equal(-1, seg.WhichSide(left));      // doc: -1 on left of line
            Assert.Equal(+1, seg.WhichSide(right));
            Assert.Equal(0, seg.WhichSide(on));
            Assert.Equal(Segment2d.WhichSide(ref p0, ref p1, ref left), seg.WhichSide(left));
        }

        // --- PrimalQuery2d.ToLine(out order): position along the line ---

        [Fact]
        public void PrimalQuery2d_ToLine_Order()
        {
            var pts = new[] { new Vector2d(0, 0), new Vector2d(2, 0) };
            var q = new PrimalQuery2d((i) => pts[i]);
            int order;
            Assert.Equal(0, q.ToLine(new Vector2d(1, 0), 0, 1, out order)); Assert.Equal(0, order);    // interior
            q.ToLine(new Vector2d(4, 0), 0, 1, out order); Assert.Equal(+2, order);                    // beyond V1
            q.ToLine(new Vector2d(2, 0), 0, 1, out order); Assert.Equal(+1, order);                    // = V1
            q.ToLine(new Vector2d(0, 0), 0, 1, out order); Assert.Equal(-1, order);                    // = V0
            q.ToLine(new Vector2d(-1, 0), 0, 1, out order); Assert.Equal(-2, order);                   // before V0
            Assert.Equal(+1, q.ToLine(new Vector2d(1, -1), 0, 1, out order)); Assert.Equal(+3, order);
        }

        // --- TransformSequence2: uniform scale, directions, segments ---


        [Fact]
        public void TransformSequence2_ScaledSegmentAndEllipse()
        {
            // TransformN returns a unit direction; it was scaled, and with Extent scaled too the segment
            // grew with the square of the scale
            var xf = new TransformSequence2().Scale(new Vector2d(2, 2));
            Vector2d n = xf.TransformN(new Vector2d(0.6, 0.8));
            Assert.Equal(1, n.Length, 12);
            var seg = new Segment2d(new Vector2d(0, 0), new Vector2d(1, 0));
            seg.Transform(xf);
            Assert.Equal(new Vector2d(0, 0), seg.P0);
            Assert.Equal(new Vector2d(2, 0), seg.P1);

            var ellipse = new Ellipse2d(Vector2d.Zero, 0, 3, 1);
            ellipse.Transform(xf);
            Assert.True(ellipse.SampleT(0).EpsilonEqual(new Vector2d(6, 0), 1e-12), $"{ellipse.SampleT(0)}");
        }
    }

    /// <summary>
    /// TransformSequence2.ScaleIsUniform compared the scale with the pivot, so the uniform-scale assert in
    /// TransformN/TransformScalar failed for every scale. By default (Util.DebugBreakOnDevAssert) a failed
    /// gDevAssert only calls Debugger.Break, which is silent without a debugger; with it off it throws.
    /// The flag is global, so these tests run alone, not in parallel with other classes.
    /// </summary>
    [Collection("DevAssertThrows")]
    public class TransformSequence2DevAssertTests
    {
        static T WithThrowingDevAssert<T>(Func<T> f)
        {
            bool prev = Util.DebugBreakOnDevAssert;
            try {
                Util.DebugBreakOnDevAssert = false;
                return f();
            } finally {
                Util.DebugBreakOnDevAssert = prev;
            }
        }

        [Fact]
        public void UniformScale_PassesTheAssert()
        {
            var xf = new TransformSequence2().Scale(new Vector2d(2, 2));
            Assert.Equal(2, WithThrowingDevAssert(() => xf.TransformScalar(1)));
            var around = new TransformSequence2().Scale(new Vector2d(3, 3), new Vector2d(1, 1));
            Assert.Equal(3, WithThrowingDevAssert(() => around.TransformScalar(1)));
            Assert.Equal(1, WithThrowingDevAssert(() => around.TransformN(new Vector2d(1, 0))).x, 12);
            Assert.Equal(new Vector2d(4, 1), around.TransformP(new Vector2d(2, 1)));
        }

        [Fact]
        public void NonUniformScale_FailsTheAssert()
        {
            var xf = new TransformSequence2().Scale(new Vector2d(2, 3), new Vector2d(2, 3));
            Assert.ThrowsAny<Exception>(() => WithThrowingDevAssert(() => xf.TransformScalar(1)));
        }
    }

    [CollectionDefinition("DevAssertThrows", DisableParallelization = true)]
    public class DevAssertThrowsCollection { }
}
