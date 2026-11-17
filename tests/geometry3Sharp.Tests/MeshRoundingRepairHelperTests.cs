using System;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // The exact helpers of MeshRoundingRepair, called directly: flat_convex (an edge flip whose two triangle pairs cover
    // the same convex quad moves the surface by 0) with drop (its projections), and ulp. The repair reaches them only on
    // rare inputs (a flat quad in an axis plane, a subnormal coordinate), so the cases of the repair tests leave most of
    // their mutants alive (mutation gate run 44013112256, rounding-4 and rounding-7). The class name starts with
    // MeshRoundingRepairTests so that the shared filter of the rounding shards selects it.
    public class MeshRoundingRepairTestsHelpers
    {
        static readonly MethodInfo FlatConvexMethod = typeof(MeshRoundingRepair).GetMethod("flat_convex", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo UlpMethod = typeof(MeshRoundingRepair).GetMethod("ulp", BindingFlags.NonPublic | BindingFlags.Static);

        static bool FlatConvex(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            Assert.NotNull(FlatConvexMethod);
            try {
                return (bool)FlatConvexMethod.Invoke(null, new object[] { a, b, c, d });
            } catch (TargetInvocationException e) {
                // (the exception of the method itself, eg a division by 0)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        static double Ulp(double x)
        {
            Assert.NotNull(UlpMethod);
            return (double)UlpMethod.Invoke(null, new object[] { x });
        }

        static Vector3d V(double x, double y, double z) => new Vector3d(x, y, z);

        // the four points with the coordinates permuted: (x, y, z) -> (y, z, x) k times, so the same quad lies in the plane
        // x = const, y = const or z = const
        static Vector3d Perm(Vector3d p, int k) => k == 0 ? p : k == 1 ? V(p.z, p.x, p.y) : V(p.y, p.z, p.x);

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void FlatConvex_InEachAxisPlane(int k)
        {
            // a quad in the plane z = 1 (k = 0), x = 1 (k = 1), y = 1 (k = 2): only one of the three projections of
            // flat_convex is not a line, and the other two must be skipped
            Func<double, double, Vector3d> P = (x, y) => Perm(V(x, y, 1), k);
            // the diagonals a-b and c-d cross properly
            Assert.True(FlatConvex(P(0, 0), P(2, 2), P(2, 0), P(0, 2)));
            // the same with the second diagonal reversed, and the first
            Assert.True(FlatConvex(P(0, 0), P(2, 2), P(0, 2), P(2, 0)));
            Assert.True(FlatConvex(P(2, 2), P(0, 0), P(2, 0), P(0, 2)));
            // c-d does not reach a-b (the line of c-d crosses a-b, the line of a-b not c-d)
            Assert.False(FlatConvex(P(0, 0), P(4, 0), P(2, -1), P(2, -0.5)));
            // a-b does not reach c-d
            Assert.False(FlatConvex(P(2, -1), P(2, -0.5), P(0, 0), P(4, 0)));
            // c on a-b (a T: not a proper crossing)
            Assert.False(FlatConvex(P(0, 0), P(4, 0), P(2, 0), P(2, 3)));
            // d on a-b
            Assert.False(FlatConvex(P(0, 0), P(4, 0), P(2, 3), P(2, 0)));
            // a on c-d
            Assert.False(FlatConvex(P(0, 0), P(0, 2), P(-1, 0), P(1, 0)));
            // b on c-d
            Assert.False(FlatConvex(P(0, 2), P(0, 0), P(-1, 0), P(1, 0)));
            // a shared end point
            Assert.False(FlatConvex(P(0, 0), P(2, 2), P(0, 0), P(0, 2)));
        }

        [Fact]
        public void FlatConvex_InAGeneralPlane()
        {
            // the plane z = x + 2 y (exact in doubles): every projection is a proper one
            Func<double, double, Vector3d> P = (x, y) => V(x, y, x + 2 * y);
            Assert.True(FlatConvex(P(0, 0), P(2, 2), P(2, 0), P(0, 2)));
            Assert.False(FlatConvex(P(0, 0), P(4, 0), P(2, -1), P(2, -0.5)));
            Assert.False(FlatConvex(P(0, 0), P(4, 0), P(2, 0), P(2, 3)));
            // not in one plane, though the XY projection crosses
            Assert.False(FlatConvex(P(0, 0), P(2, 2), P(2, 0), V(0, 2, 5)));
            Assert.False(FlatConvex(V(0, 0, 0), V(2, 2, 0), V(2, 0, 0), V(0, 2, 1e-300)));
        }

        [Fact]
        public void FlatConvex_CollinearPoints()
        {
            // in one plane and every projection a line: no quad
            Assert.False(FlatConvex(V(0, 0, 0), V(2, 2, 2), V(1, 1, 1), V(3, 3, 3)));
            Assert.False(FlatConvex(V(0, 0, 0), V(2, 0, 0), V(1, 0, 0), V(3, 0, 0)));
            Assert.False(FlatConvex(V(0, 0, 0), V(0, 0, 0), V(0, 0, 0), V(0, 0, 0)));
        }

        [Fact]
        public void FlatConvex_SimilarityInvariant()
        {
            // the decision is exact, so it does not change under the scales by 2^k of Similarity.All() (exact in doubles;
            // the rotations and translations round the points, so the four may leave their plane)
            var quads = new[] {
                new[] { V(0, 0, 0), V(2, 2, 6), V(2, 0, 2), V(0, 2, 4) },
                new[] { V(0, 0, 1), V(4, 0, 1), V(2, 0, 1), V(2, 3, 1) },
                new[] { V(1, 0, 0), V(1, 2, 2), V(1, 2, 0), V(1, 0, 2) },
                new[] { V(0, 0, 0), V(2, 2, 6), V(2, 0, 2), V(0, 2, 5) },
            };
            foreach (Vector3d[] q in quads) {
                bool want = FlatConvex(q[0], q[1], q[2], q[3]);
                foreach (Similarity s in Similarity.All()) {
                    if (s.IsExact == false) continue;
                    Assert.True(want == FlatConvex(s.Apply(q[0]), s.Apply(q[1]), s.Apply(q[2]), s.Apply(q[3])), s.Name);
                }
            }
        }

        [Fact]
        public void Ulp_IsTheUnitInTheLastPlace()
        {
            Assert.Equal(Math.Pow(2, -52), Ulp(1));
            Assert.Equal(Math.Pow(2, -51), Ulp(3));
            Assert.Equal(Math.Pow(2, 971), Ulp(double.MaxValue));
            // the normal numbers with an exponent field of 52 or less: the unit is a subnormal
            Assert.Equal(double.Epsilon, Ulp(Math.Pow(2, -1022)));
            Assert.Equal(Math.Pow(2, -1052), Ulp(Math.Pow(2, -1000)));
            Assert.Equal(Math.Pow(2, -1023), Ulp(1.5 * Math.Pow(2, -971)));
            // 0 and the subnormals: the smallest subnormal
            Assert.Equal(double.Epsilon, Ulp(0));
            Assert.Equal(double.Epsilon, Ulp(double.Epsilon));
            Assert.Equal(double.Epsilon, Ulp(Math.Pow(2, -1030)));
            // not finite
            Assert.True(double.IsNaN(Ulp(double.PositiveInfinity)));
            Assert.True(double.IsNaN(Ulp(double.NaN)));
        }
    }
}
