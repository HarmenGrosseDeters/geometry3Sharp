using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshArrangement.SortPoints (#1464, the order of the exact result by its geometry): the list sorted by ComparePoints,
    /// whatever order it is handed and whichever way it takes. In a file of its own: it names members the base branch does not have,
    /// and the tests of ExactBooleanInputOrderTests must compile against the base (tools/new_tests_on_base.py).
    /// </summary>
    public class ExactMeshArrangementSortPointsTests
    {
        const ExactMeshBoolean.Operations Difference = ExactMeshBoolean.Operations.Difference;

        static double Bowl(double x, double y) { return 0.3 * ((x - 0.5) * (x - 0.5) + (y - 0.5) * (y - 0.5)); }
        static double Dome(double x, double y) { return Math.Sqrt(4 - (x - 0.5) * (x - 0.5) - (y - 0.5) * (y - 0.5)); }

        static readonly Vector3d Center = new Vector3d(0.5, 0.5, 0);

        /// <summary>
        /// A curved plate: the surface z = h(x, y) over the unit square on an n by n grid, the thickness t along z, closed
        /// by side walls; map moves its vertices.
        /// </summary>
        static DMesh3 Plate(int n, double t, Func<double, double, double> h, Func<Vector3d, Vector3d> map = null)
        {
            var m = new DMesh3();
            int N = n + 1;
            var top = new int[N, N];
            var bot = new int[N, N];
            for (int i = 0; i < N; ++i)
                for (int j = 0; j < N; ++j) {
                    double x = (double)i / n, y = (double)j / n, z = h(x, y);
                    Vector3d a = new Vector3d(x, y, z + t / 2), b = new Vector3d(x, y, z - t / 2);
                    top[i, j] = m.AppendVertex(map != null ? map(a) : a);
                    bot[i, j] = m.AppendVertex(map != null ? map(b) : b);
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    m.AppendTriangle(top[i, j], top[i + 1, j], top[i + 1, j + 1]);
                    m.AppendTriangle(top[i, j], top[i + 1, j + 1], top[i, j + 1]);
                    m.AppendTriangle(bot[i, j], bot[i + 1, j + 1], bot[i + 1, j]);
                    m.AppendTriangle(bot[i, j], bot[i, j + 1], bot[i + 1, j + 1]);
                }
            Action<int, int, int, int> wall = (a0, a1, b0, b1) => { m.AppendTriangle(a0, b0, b1); m.AppendTriangle(a0, b1, a1); };
            for (int i = 0; i < n; ++i) {
                wall(top[i, 0], top[i + 1, 0], bot[i, 0], bot[i + 1, 0]);
                wall(top[i + 1, n], top[i, n], bot[i + 1, n], bot[i, n]);
                wall(top[0, i + 1], top[0, i], bot[0, i + 1], bot[0, i]);
                wall(top[n, i], top[n, i + 1], bot[n, i], bot[n, i + 1]);
            }
            return m;
        }


        static Vector3d Rotated(Vector3d p, double ax, double ay)
        {
            Vector3d q = p - Center;
            double co = Math.Cos(ax), si = Math.Sin(ax);
            q = new Vector3d(q.x, co * q.y - si * q.z, si * q.y + co * q.z);
            co = Math.Cos(ay);
            si = Math.Sin(ay);
            q = new Vector3d(co * q.x + si * q.z, q.y, -si * q.x + co * q.z);
            return Center + q;
        }

        // a plate minus its copy shrunk by 1e-6 about Center: a skin of 1e-6 of the plate's size on its outer side
        static (DMesh3 a, DMesh3 b) ShrunkCopy()
        {
            return (Plate(6, 0.02, Bowl), Plate(6, 0.02, Bowl, p => Center + (1 - 1e-6) * (p - Center)));
        }

        // two tessellations of one plate, one turned by 1e-7.75: skins between the two tessellations
        static (DMesh3 a, DMesh3 b) TwoTessellations()
        {
            double e = Math.Pow(10, -7.75);
            return (Plate(8, 0.02, Dome, p => Rotated(p, e, 0.7 * e)), Plate(6, 0.02, Dome));
        }

        static ulong Next(ref ulong s)
        {
            s = s * 6364136223846793005UL + 1442695040888963407UL;
            return s >> 33;
        }

        static int[] Ordered(IEnumerable<int> ids, int order)
        {
            int[] a = ids.ToArray();
            if (order == -1)
                Array.Reverse(a);
            else if (order != 0) {
                // (a Fisher-Yates shuffle with a generator of its own: the same on every runtime)
                ulong s = (ulong)order * 0x9E3779B97F4A7C15UL + 1;
                for (int i = a.Length - 1; i > 0; --i) {
                    int j = (int)(Next(ref s) % (ulong)(i + 1));
                    int tmp = a[i]; a[i] = a[j]; a[j] = tmp;
                }
            }
            return a;
        }

        /// <summary>
        /// ExactMeshArrangement.SortPoints gives the list sorted by ComparePoints, whatever order it is handed and whichever
        /// way it takes: the sort by the filter's key with the insertion sort after it, or (budget below 0, or used up at
        /// the first move with 0) ComparePoints alone. The points are those of two nearly coincident plates: many lie closer
        /// along the key's direction than the errors of their keys.
        /// </summary>
        [Fact]
        public void SortPoints_IsTheOrderOfComparePoints_OnEveryPath()
        {
            // (the rotated plates: there the keys alone give another order than ComparePoints, so the insertion sort moves points)
            var tess = TwoTessellations();
            var shrunk = ShrunkCopy();
            Similarity rotated = Similarity.ByName("rotated");
            foreach (DMesh3[] meshes in new[] { new[] { tess.a, tess.b }, new[] { Invariance.Transformed(shrunk.a, rotated), Invariance.Transformed(shrunk.b, rotated) } }) {
                var arr = new ExactMeshArrangement(new List<DMesh3>(meshes));
                arr.Compute();
                var seen = new HashSet<int>();
                foreach (var t in arr.Output) {
                    seen.Add(t.A);
                    seen.Add(t.B);
                    seen.Add(t.C);
                }
                var expected = new List<int>(seen);
                expected.Sort(arr.ComparePoints);
                Assert.True(expected.Count > 100);
                for (int i = 1; i < expected.Count; ++i)
                    Assert.True(arr.ComparePoints(expected[i - 1], expected[i]) < 0);
                foreach (int budget in new[] { 8, 0, -1 })
                    foreach (int order in new[] { 0, -1, 5, 77 }) {
                        arr.SortPointsMoveBudget = budget;
                        var ids = new List<int>(Ordered(expected, order));
                        arr.SortPoints(ids);
                        Assert.True(expected.SequenceEqual(ids), "budget " + budget + ", order " + order);
                    }
                var none = new List<int>();
                arr.SortPoints(none);
                Assert.Empty(none);
            }
        }
    }
}
