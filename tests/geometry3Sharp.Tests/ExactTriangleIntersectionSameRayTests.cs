using System;
using System.Reflection;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactTriangleIntersection's same_ray (are a and b on one ray from s?) must be exact over the whole double range:
    /// the sign of a double dot product underflows to 0 near 1e-170 and overflows near 1e154 (#1349). The oracle is
    /// the answer for the same points scaled by 2^600 / 2^-600, where the products are well inside the range.
    /// </summary>
    public class ExactTriangleIntersectionSameRayTests
    {
        static readonly MethodInfo SameRay = typeof(ExactTriangleIntersection).GetMethod("same_ray",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        static bool Same(Vector2d s, Vector2d a, Vector2d b) => (bool)SameRay.Invoke(null, new object[] { s, a, b });

        static Vector2d Sc(Vector2d v, int k) => new Vector2d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k));

        [Fact]
        public void SameRay_IsFoundBySignsOfComponents_AtTinyAndHugeScale()
        {
            // s at the origin, a = (1, 2) t, b = (3, 6) t, c = -(1, 2) t, for t = 2^k over the double range
            foreach (int k in new[] { -1000, -600, -565, -300, -60, 0, 60, 300, 500, 505, 700, 1000 }) {
                var s = new Vector2d(0, 0);
                var a = Sc(new Vector2d(1, 2), k - 1);
                var b = Sc(new Vector2d(3, 6), k - 2);
                var c = Sc(new Vector2d(-1, -2), k);
                Assert.True(Same(s, a, b), $"2^{k}: a, b on one ray");
                Assert.False(Same(s, a, c), $"2^{k}: opposite rays");
                Assert.False(Same(s, a, s), $"2^{k}: b = s");
                Assert.False(Same(s, s, b), $"2^{k}: a = s");
                Assert.False(Same(s, a, Sc(new Vector2d(1, 3), k)), $"2^{k}: not collinear");
                // a shifted s: the differences a - s, b - s are the small vectors (exact where s and a are within a factor 2)
                var s2 = Sc(new Vector2d(1, -1), k);
                Assert.True(Same(s2, s2 + s2 * 0.5, s2 + s2 * 0.25));
                Assert.False(Same(s2, s2 + s2 * 0.5, s2 - s2 * 0.25));
            }
        }

        [Fact]
        public void SameRay_MatchesTheScaledTwin_OnRandomCollinearTriples()
        {
            var rnd = new Random(1349);
            int agree = 0;
            for (int i = 0; i < 4000; ++i) {
                // exact collinear dyadic points: s + u * m for small integers m (u = a dyadic direction)
                var s = new Vector2d(rnd.Next(-4, 5), rnd.Next(-4, 5));
                var u = new Vector2d(rnd.Next(-3, 4), rnd.Next(-3, 4));
                int ma = rnd.Next(-3, 4), mb = rnd.Next(-3, 4);
                var a = s + u * ma; var b = s + u * mb;
                bool at600 = Same(Sc(s, 600), Sc(a, 600), Sc(b, 600));
                bool expected = (u.x != 0 || u.y != 0) && ma != 0 && mb != 0 && (ma > 0) == (mb > 0);
                Assert.Equal(expected, at600);
                foreach (int k in new[] { -1000, -565, -170 * 3, 560, 1000 })
                    Assert.Equal(at600, Same(Sc(s, k), Sc(a, k), Sc(b, k)));
                agree++;
            }
            Assert.Equal(4000, agree);
        }

        [Fact]
        public void IntersectsBeyondShared_CoplanarCornersAtASharedVertex_AreScaleInvariant()
        {
            // two coplanar triangles with shared vertex 0 whose corners at s coincide (p, u on one ray and q, w on one)
            Vector3d[] A = { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
            Vector3d[] B = { new Vector3d(0, 0, 0), new Vector3d(3, 0, 0), new Vector3d(0, 5, 0) };
            foreach (int k in new[] { -1000, -570, -300, 0, 300, 540, 1000 }) {
                var ta = new Triangle3d(Sc3(A[0], k), Sc3(A[1], k), Sc3(A[2], k));
                var tb = new Triangle3d(Sc3(B[0], k - 2), Sc3(B[1], k - 2), Sc3(B[2], k - 2));
                Assert.True(ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref ta, new Index3i(0, 3, 4), ref tb), $"2^{k}");
            }
        }

        static Vector3d Sc3(Vector3d v, int k) => new Vector3d(ExactPoint3.ScaleB(v.x, k), ExactPoint3.ScaleB(v.y, k), ExactPoint3.ScaleB(v.z, k));
    }
}
