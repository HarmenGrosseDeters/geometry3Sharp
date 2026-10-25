using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Low-severity findings in math/ from the class-by-class review
    /// (docs/Analyse-2026-10.md, section 2.1, severity N, and the "Mängel" line).
    /// </summary>
    public class MathLowFixesTests
    {
        // --- Box3d(AxisAlignedBox3d) ---

        [Fact]
        public void Box3d_FromAxisAlignedBox_KeepsDoublePrecision()
        {
            // the extent went through Vector3f, so 0.1 became 0.100000001490116
            var aabb = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(0.2, 0.2, 0.2));
            var box = new Box3d(aabb);
            Assert.Equal(0.1, box.Extent.x);
            Assert.Equal(0.1, box.Extent.y);
            Assert.Equal(0.1, box.Extent.z);
        }

        // --- Quaterniond.AxisAngleR ---

        [Fact]
        public void Quaterniond_AxisAngleR_UsesDoubleConversion()
        {
            // Rad2Degf (float) made the angle off by ~1e-8 rad
            Quaterniond q = Quaterniond.AxisAngleR(Vector3d.AxisZ, Math.PI / 2);
            Assert.Equal(Math.Cos(Math.PI / 4), q.w, 14);
            Assert.Equal(Math.Sin(Math.PI / 4), q.z, 14);
        }

        // --- Vector4f.Normalized / IsNormalized ---

        [Fact]
        public void Vector4f_IsNormalized_FloatTolerance()
        {
            // the double tolerance 1e-8 is below float round-off
            int failures = 0;
            var rnd = new Random(31);
            for (int i = 0; i < 100; ++i) {
                var v = new Vector4f((float)rnd.NextDouble() + 0.1f, (float)rnd.NextDouble(),
                                     (float)rnd.NextDouble(), (float)rnd.NextDouble());
                if (v.Normalized.IsNormalized == false)
                    failures++;
            }
            Assert.Equal(0, failures);
        }

        [Fact]
        public void Vector4f_Normalized_MatchesNormalize()
        {
            // Normalized used the double epsilon 2.2e-16, Normalize() uses Epsilonf
            var v = new Vector4f(1e-8f, 0, 0, 0);
            var w = v;
            w.Normalize();
            Assert.Equal(w, v.Normalized);
        }

        // --- AxisAlignedBox3f.Contract ---

        [Fact]
        public void AxisAlignedBox3f_Contract_Clamps()
        {
            var box = new AxisAlignedBox3f(Vector3f.Zero, new Vector3f(1, 4, 4));
            box.Contract(1);
            // x extent is 1, contracting by 1 on each side must collapse to the center, not invert
            Assert.True(box.Min.x <= box.Max.x);
            Assert.Equal(0.5f, box.Min.x);
            Assert.Equal(0.5f, box.Max.x);
            Assert.Equal(1f, box.Min.y);
            Assert.Equal(3f, box.Max.y);
        }

        // --- AxisAlignedBox2i/3i empty box ---

        [Fact]
        public void AxisAlignedBoxi_Empty_HasZeroSize()
        {
            // int.MinValue - int.MaxValue wrapped around to 1
            Assert.Equal(0, AxisAlignedBox2i.Empty.Width);
            Assert.Equal(0, AxisAlignedBox2i.Empty.Height);
            Assert.Equal(0, AxisAlignedBox2i.Empty.Area);
            Assert.Equal(0, AxisAlignedBox3i.Empty.Width);
            Assert.Equal(0, AxisAlignedBox3i.Empty.Depth);
            Assert.Equal(0, AxisAlignedBox3i.Empty.Volume);
            Assert.Equal(3, new AxisAlignedBox3i(0, 0, 0, 3, 4, 5).Width);
            Assert.Equal(60, new AxisAlignedBox3i(0, 0, 0, 3, 4, 5).Volume);
        }

        // --- Equals(object) ---

        public static IEnumerable<object[]> EqualsValues()
        {
            yield return new object[] { new Vector2d(1, 2) };
            yield return new object[] { new Vector2f(1, 2) };
            yield return new object[] { new Vector2i(1, 2) };
            yield return new object[] { new Vector2l(1, 2) };
            yield return new object[] { new Vector3d(1, 2, 3) };
            yield return new object[] { new Vector3f(1, 2, 3) };
            yield return new object[] { new Vector3i(1, 2, 3) };
            yield return new object[] { new Vector4d(1, 2, 3, 4) };
            yield return new object[] { new Vector4f(1, 2, 3, 4) };
            yield return new object[] { new Index2i(1, 2) };
            yield return new object[] { new Index3i(1, 2, 3) };
            yield return new object[] { new Index4i(1, 2, 3, 4) };
            yield return new object[] { new Quaternionf(0, 0, 0, 1) };
            yield return new object[] { new AxisAlignedBox2i(0, 0, 1, 1) };
            yield return new object[] { new AxisAlignedBox3i(0, 0, 0, 1, 1, 1) };
            yield return new object[] { new AxisAlignedBox3f(1) };
            yield return new object[] { new AxisAlignedBox3d(1) };
        }

        [Theory]
        [MemberData(nameof(EqualsValues))]
        public void Equals_NullOrForeignType_ReturnsFalse(object value)
        {
            Assert.False(value.Equals(null));
            Assert.False(value.Equals("foo"));
            Assert.False(value.Equals(42));
            Assert.True(value.Equals(value));
        }

        // --- IndexUtil.sort_indices ---

        [Fact]
        public void SortIndices_WithDuplicates()
        {
            var cases = new[] {
                new Index3i(2, 1, 1), new Index3i(1, 2, 1), new Index3i(1, 1, 2),
                new Index3i(2, 2, 1), new Index3i(2, 1, 2), new Index3i(1, 2, 2),
                new Index3i(3, 3, 3), new Index3i(3, 1, 2), new Index3i(2, 3, 1),
                new Index3i(1, 3, 2), new Index3i(3, 2, 1), new Index3i(2, 1, 3),
            };
            foreach (Index3i input in cases) {
                Index3i t = input;
                IndexUtil.sort_indices(ref t);
                int[] expect = new[] { input.a, input.b, input.c }.OrderBy(x => x).ToArray();
                Assert.Equal(new Index3i(expect[0], expect[1], expect[2]), t);
            }
        }

        // --- Box3d.Contain(IEnumerable) ---

        [Fact]
        public void Box3d_ContainEmptyEnumerable_IsNoOp()
        {
            var box = new Box3d(new Vector3d(5, 5, 5), new Vector3d(1, 1, 1));
            box.Contain(new Vector3d[0]);
            box.Contain(new List<Vector3d>());
            Assert.Equal(new Vector3d(5, 5, 5), box.Center);
            Assert.Equal(new Vector3d(1, 1, 1), box.Extent);
        }

        // --- ToString culture ---

        [Fact]
        public void ToString_IsCultureInvariant()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            try {
                CultureInfo.CurrentCulture = comma;
                Assert.Equal("1.50000000 2.00000000", new Vector2d(1.5, 2).ToString());
                Assert.Equal("1.50000000 2.00000000", new Vector2f(1.5f, 2).ToString());
                Assert.Equal("1.50000000 2.00000000 3.00000000", new Vector3d(1.5, 2, 3).ToString());
                Assert.Equal("1.50000000 2.00000000 3.00000000", new Vector3f(1.5f, 2, 3).ToString());
                Assert.Equal("1.500 2.000 3.000", new Vector3d(1.5, 2, 3).ToString("F3"));
                Assert.Equal("1.500 2.000 3.000", new Vector3f(1.5f, 2, 3).ToString("F3"));
                Assert.Equal("1.50000000 2.00000000 3.00000000 4.00000000", new Vector4d(1.5, 2, 3, 4).ToString());
                Assert.Equal("1.50000000 2.00000000 3.00000000 4.00000000", new Vector4f(1.5f, 2, 3, 4).ToString());
                Assert.Equal("1.5 2.0 3.0 4.0", new Vector4d(1.5, 2, 3, 4).ToString("F1"));
                Assert.Equal("1.5 2.0 3.0 4.0", new Vector4f(1.5f, 2, 3, 4).ToString("F1"));
                Assert.Equal("0.50000000 0.00000000 0.00000000 1.00000000", new Quaterniond(0.5, 0, 0, 1).ToString());
                Assert.Equal("0.50000000 0.00000000 0.00000000 1.00000000", new Quaternionf(0.5f, 0, 0, 1).ToString());
                Assert.Equal("0.5 0.0 0.0 1.0", new Quaterniond(0.5, 0, 0, 1).ToString("F1"));
                Assert.Equal("0.5 0.0 0.0 1.0", new Quaternionf(0.5f, 0, 0, 1).ToString("F1"));
                Assert.Equal("[0.50000000,1.00000000]", new Interval1d(0.5, 1).ToString());
                Assert.Equal("[0.50000000,1.00000000] [0.00000000,1.00000000]",
                    new AxisAlignedBox2d(0.5, 0, 1, 1).ToString());
                Assert.Equal("[0.50000000,1.00000000] [0.00000000,1.00000000]",
                    new AxisAlignedBox2f(0.5f, 0, 1, 1).ToString());
                Assert.Equal("x[0.50000000,1.00000000] y[0.00000000,1.00000000] z[0.00000000,1.00000000]",
                    new AxisAlignedBox3d(0.5, 0, 0, 1, 1, 1).ToString());
                Assert.Equal("x[0.50000000,1.00000000] y[0.00000000,1.00000000] z[0.00000000,1.00000000]",
                    new AxisAlignedBox3f(0.5f, 0, 0, 1, 1, 1).ToString());
                Assert.DoesNotContain(",", Matrix3d.Identity.ToString());
                Assert.DoesNotContain(",", Matrix3f.Identity.ToString());
                Assert.Equal("[Frame3f: Origin=0.0000 0.0000 0.0000, X=1.0000 0.0000 0.0000, Y=0.0000 1.0000 0.0000, Z=0.0000 0.0000 1.0000]",
                    Frame3f.Identity.ToString());
            } finally {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // --- MathUtil.ClampAngleDeg / ClampAngleRad ---

        [Fact]
        public void ClampAngleDeg_FarFromCenter()
        {
            // interval [-360,-300] is [0,60] mod 360; 350 = -10 is closest to the -360 end.
            // theta - c = 680 needed two 360 corrections
            Assert.Equal(-360, MathUtil.ClampAngleDeg(350, -360, -300), 10);
            Assert.Equal(-360, MathUtil.ClampAngleDeg(-10, -360, -300), 10);
            Assert.Equal(-330, MathUtil.ClampAngleDeg(30, -360, -300), 10);
            // in-range values and the ordinary cases are unchanged
            Assert.Equal(45, MathUtil.ClampAngleDeg(45, 0, 90), 10);
            Assert.Equal(45, MathUtil.ClampAngleDeg(45 + 720, 0, 90), 10);
            Assert.Equal(90, MathUtil.ClampAngleDeg(100, 0, 90), 10);
            Assert.Equal(0, MathUtil.ClampAngleDeg(-20, 0, 90), 10);
        }

        [Fact]
        public void ClampAngleRad_FarFromCenter()
        {
            double d = Math.PI / 180;
            Assert.Equal(-360 * d, MathUtil.ClampAngleRad(350 * d, -360 * d, -300 * d), 10);
            Assert.Equal(-330 * d, MathUtil.ClampAngleRad(30 * d, -360 * d, -300 * d), 10);
            Assert.Equal(45 * d, MathUtil.ClampAngleRad(45 * d, 0, 90 * d), 10);
            Assert.Equal(90 * d, MathUtil.ClampAngleRad(100 * d, 0, 90 * d), 10);
        }

        // --- Interval1i ---

        [Fact]
        public void Interval1i_RangeInclusive_IsZeroToN()
        {
            // guard for the doc fix: the code returns [0,N], matching the name and IntSequence.RangeInclusive
            Interval1i r = Interval1i.RangeInclusive(5);
            Assert.Equal(0, r.a);
            Assert.Equal(5, r.b);
        }

        [Fact]
        public void Interval1i_Enumerator_StopsAtIntMaxValue()
        {
            // i <= int.MaxValue is always true, so ++i wrapped and the loop never ended
            var values = new Interval1i(int.MaxValue - 2, int.MaxValue).Take(10).ToList();
            Assert.Equal(new[] { int.MaxValue - 2, int.MaxValue - 1, int.MaxValue }, values);
            Assert.Empty(Interval1i.Empty);
            Assert.Equal(new[] { 2, 3, 4 }, new Interval1i(2, 4).ToArray());
        }

        // --- AxisAlignedBox2i/3i.Contain(double) ---

        [Fact]
        public void AxisAlignedBoxi_ContainDouble_Floors()
        {
            // (int)-0.5 truncates to 0, so the grid cell of -0.5 (-1) was not contained
            var box3 = new AxisAlignedBox3i(Vector3i.Zero);
            box3.Contain(new Vector3d(-0.5, -1.5, 0.5));
            Assert.Equal(new Vector3i(-1, -2, 0), box3.Min);
            Assert.Equal(new Vector3i(0, 0, 0), box3.Max);

            var box3b = AxisAlignedBox3i.Empty;
            box3b.Contain(new AxisAlignedBox3d(new Vector3d(-0.5, -0.5, -0.5), new Vector3d(1.5, 1.5, 1.5)));
            Assert.Equal(new Vector3i(-1, -1, -1), box3b.Min);
            Assert.Equal(new Vector3i(1, 1, 1), box3b.Max);

            var box2 = new AxisAlignedBox2i(Vector2i.Zero);
            box2.Contain(new Vector3d(-0.5, -1.5, 0));
            Assert.Equal(new Vector2i(-1, -2), box2.Min);

            var box2b = AxisAlignedBox2i.Empty;
            box2b.Contain(new AxisAlignedBox3d(new Vector3d(-0.5, -0.5, 0), new Vector3d(1.5, 1.5, 0)));
            Assert.Equal(new Vector2i(-1, -1), box2b.Min);
            Assert.Equal(new Vector2i(1, 1), box2b.Max);
        }

        // --- Matrix3d/3f.Inverse ---

        [Fact]
        public void Matrix3d_Inverse_SingularWithRoundoff_Throws()
        {
            // rank 2; the computed determinant is round-off (-6.9e-18), far above double.Epsilon,
            // so Inverse() returned entries of ~1e17 instead of throwing
            var m = new Matrix3d(0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9);
            Assert.Throws<Exception>(() => m.Inverse());
            Assert.Throws<Exception>(() => new Matrix3d(1, 2, 3, 4, 5, 6, 7, 8, 9).Inverse());
            Assert.Throws<Exception>(() => new Matrix3f(0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f).Inverse());
            Assert.Throws<Exception>(() => Matrix3d.Zero.Inverse());
        }

        [Fact]
        public void Matrix3d_Inverse_SmallScale_StillWorks()
        {
            // a tolerance must be relative: a uniformly tiny but well-conditioned matrix is invertible
            var m = new Matrix3d(1e-6, 0, 0, 0, 2e-6, 0, 0, 0, 4e-6);
            Matrix3d inv = m.Inverse();
            Assert.True((m * inv).EpsilonEqual(Matrix3d.Identity, 1e-12));
            var mf = new Matrix3f(1e-3f, 0, 0, 0, 2e-3f, 0, 0, 0, 4e-3f);
            Matrix3f invf = mf.Inverse();
            Assert.True((mf * invf).EpsilonEqual(Matrix3f.Identity, 1e-5f));
            var r = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 33).ToRotationMatrix();
            Assert.True((r * r.Inverse()).EpsilonEqual(Matrix3d.Identity, 1e-12));
        }

        // --- Box2d(Segment2d) ---

        [Fact]
        public void Box2d_FromSegment_IsRightHanded()
        {
            var box = new Box2d(new Segment2d(new Vector2d(0, 0), new Vector2d(2, 1)));
            // AxisX x AxisY = +1 like Box2d.Merge and the axis-aligned constructors
            Assert.Equal(1, box.AxisX.Cross(box.AxisY), 12);
        }

        // --- Frame3f.ToFrame/FromFrame(ref Box) ---

        [Fact]
        public void Frame3f_ToFrameBox_DoesNotModifyInput()
        {
            var frame = new Frame3f(new Vector3f(1, 2, 3), Quaternionf.AxisAngleD(Vector3f.AxisZ, 90));
            var box = new Box3d(new Vector3d(5, 0, 0), new Vector3d(1, 2, 3));
            Box3d orig = box;
            Box3d local = frame.ToFrame(ref box);
            Assert.Equal(orig.Center, box.Center);
            Assert.Equal(orig.AxisX, box.AxisX);
            Box3d back = frame.FromFrame(ref local);
            Assert.True(back.Center.EpsilonEqual(orig.Center, 1e-5));
            Assert.NotEqual(orig.Center, local.Center);

            var boxf = new Box3f(new Vector3f(5, 0, 0), new Vector3f(1, 2, 3));
            Box3f origf = boxf;
            Box3f localf = frame.ToFrame(ref boxf);
            Assert.Equal(origf.Center, boxf.Center);
            Assert.Equal(origf.AxisX, boxf.AxisX);
            Box3f localf2 = localf;
            frame.FromFrame(ref localf);
            Assert.Equal(localf2.Center, localf.Center);
        }

        // --- Vector3d.ComputeOrthogonalComplement ---

        [Fact]
        public void ComputeOrthogonalComplement_UpdatesCallerVectors()
        {
            // v1 and v2 are already ref; v0 is by value on purpose (callers keep their normal)
            Vector3d n = new Vector3d(0, 0, 2);
            Vector3d v1 = Vector3d.Zero, v2 = Vector3d.Zero;
            Vector3d.ComputeOrthogonalComplement(1, n, ref v1, ref v2);
            Assert.Equal(1, v1.Length, 12);
            Assert.Equal(1, v2.Length, 12);
            Assert.Equal(0, v1.Dot(n), 12);
            Assert.Equal(0, v2.Dot(n), 12);
            Assert.Equal(0, v1.Dot(v2), 12);
        }
    }
}
