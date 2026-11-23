using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Found by the site review of the determinism proof stage 2 (docs/design/DETERMINISM_PROOF.md 5.5): the Coplanar list of an input
    /// triangle that touches coplanar triangles of the other mesh only at shared vertices or edges (PreDecideCoplanar: no point and no segment
    /// is added) was filled by the parallel pair loop and never sorted (prepare_triangle returns early for a triangle without points and
    /// segments), so its order followed the schedule; coplanar_stack turns it into the order of a floating-point sum
    /// (front_generalized_winding). These tests reverse and shuffle the pair loop and ask for ascending lists.
    /// </summary>
    public class ExactArrangementCoplanarOrderTests
    {
        static readonly int[] Orders = { -1, 1, 2, 3 };

        static T WithOrder<T>(int order, Func<T> f)
        {
            bool serial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            gParallel.SerialOrderOnThisThread = order;
            try {
                return f();
            } finally {
                gParallel.SerialOrderOnThisThread = 0;
                gParallel.SerialOnThisThread = serial;
            }
        }

        // an open triangle and an open fan of triangles around its vertex (2, 0, 0), all in the plane z = 0, meeting it only at that vertex
        static (DMesh3, DMesh3) TriangleAndFan(int fan)
        {
            var a = new DMesh3();
            a.AppendVertex(new Vector3d(0, 0, 0)); a.AppendVertex(new Vector3d(2, 0, 0)); a.AppendVertex(new Vector3d(0, 2, 0));
            a.AppendTriangle(0, 1, 2);
            var b = new DMesh3();
            int v = b.AppendVertex(new Vector3d(2, 0, 0));
            var rim = new List<int>();
            for (int k = 0; k <= fan; ++k) {
                double angle = -0.6 + 3.0 * k / fan * 0.5;        // (from below the x axis to above the line x + y = 2: all outside the triangle at the vertex)
                rim.Add(b.AppendVertex(new Vector3d(2 + Math.Cos(angle) * (1 + 0.125 * k), Math.Sin(angle) * (1 + 0.125 * k), 0)));
            }
            for (int k = 0; k < fan; ++k) b.AppendTriangle(v, rim[k], rim[k + 1]);
            return (a, b);
        }

        static List<List<int>> CoplanarLists(int order, int fan)
        {
            return WithOrder(order, () => {
                (DMesh3 a, DMesh3 b) = TriangleAndFan(fan);
                var r = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
                r.Compute();
                ExactMeshArrangement arr = r.Arrangement;
                Assert.NotNull(arr);
                var lists = new List<List<int>>();
                for (int t = 0; t < arr.Triangles.Count; ++t)
                    lists.Add(new List<int>(arr.Triangles[t].Coplanar));
                return lists;
            });
        }

        [Fact]
        public void CoplanarLists_AreAscending_UnderEveryLoopOrder()
        {
            const int fan = 6;
            List<List<int>> reference = CoplanarLists(0, fan);
            // (the triangle of the first mesh meets every triangle of the fan: the list that has no point and no segment to sort)
            Assert.True(reference.Any(l => l.Count >= fan), "no triangle with the whole fan in its Coplanar list: " + string.Join(" | ", reference.Select(l => string.Join(",", l))));
            foreach (int order in new[] { 0 }.Concat(Orders)) {
                List<List<int>> lists = CoplanarLists(order, fan);
                for (int t = 0; t < lists.Count; ++t)
                    Assert.True(lists[t].SequenceEqual(lists[t].OrderBy(x => x)), $"loop order {order}, triangle {t}: {string.Join(",", lists[t])}");
                Assert.Equal(reference.Select(l => string.Join(",", l)), lists.Select(l => string.Join(",", l)));
            }
        }
    }
}
