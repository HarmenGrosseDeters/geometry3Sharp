using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Which evaluation of the edge angle (ExactWindingCache.finish_edge_angle) an input takes: the tests that count the calls of
    /// accurate_edge_angle_x, so they need the members that only this version has. They are in a file of their own: the accuracy
    /// tests of ExactWindingCacheInternalTestsNearOpenEdge use boundary_edge_angle_x and the public API only, and compile (and fail)
    /// against the library of the base. The class name starts with ExactWindingCacheInternalTests so that the mutation shard's filter runs it.
    /// </summary>
    public class ExactWindingCacheInternalTestsAccurateEdgeAngleRouting
    {
        [Fact]
        public void SubnormalGuard_KeepsTheNormalRangeOnTheRoundedPath()
        {
            // S1 = |ay bz| + |az by| about 2^-944, above the guard 2^-1000 (SubnormalS1): the products are normal numbers, the sign of
            // num (about 3e-285, above 1e-12 S1 is false, but far above the rounding of the products) is certain, and den (about -1e-60)
            // is clearly negative, so the rounded evaluation decides: no call of the exact evaluation, and the angle is -pi (num < 0, den < 0).
            // (A guard moved up by 2^100 would send this edge to the exact evaluation.)
            int c0 = ExactWindingCache.AccurateEdgeAngleCalls;
            var a = new Vector3d(1.0, 1e-30, 2e-255);
            var b = new Vector3d(1.0, -1e-30, 1e-255);
            double got = ExactWindingCache.boundary_edge_angle_x(Vector3d.Zero, a, b);
            Assert.Equal(c0, ExactWindingCache.AccurateEdgeAngleCalls);
            Assert.True(Math.Abs(got + Math.PI) <= 1e-13, $"{got:R}");
            double err = Math.Abs(got - ExactWindingCache.accurate_edge_angle_x(Vector3d.Zero, a, b));
            Assert.True(err <= 2e-13, $"{err:E2}");
        }

        [Fact]
        public void SubnormalProducts_GoToTheExactEvaluation_AtTheBranchCut()
        {
            // S1 below 2^-1000: the products are rounded to the subnormal grid and the sign of the rounded num is not certain, so the
            // exact evaluation decides wherever den <= 1e-12 S2 (here den < 0). The reviewer's input of the case where it was +pi.
            int c0 = ExactWindingCache.AccurateEdgeAngleCalls;
            var p = new Vector3d(0, 5.334489550943501e-77, 0);
            var a = new Vector3d(7.888609052210118e-31, 6.963444032852826e-61, 1.3734963702551047e-262);
            var b = new Vector3d(7.888609052210118e-31, -7.374133604637322e-61, -1.4545023542892055e-262);
            double got = ExactWindingCache.boundary_edge_angle_x(p, a, b);
            Assert.True(ExactWindingCache.AccurateEdgeAngleCalls > c0);
            Assert.True(Math.Abs(got + Math.PI) <= 1e-13, $"{got:R}");
        }

        [Fact]
        public void PointExactlyOnTheYzLineOfTheEdge_DoesNotEnterTheIntegerPath_AndKeepsTheValueOfTheExactEvaluation()
        {
            // p, a, b with the yz parts exactly collinear (small dyadic numbers, so the products are exact): exact num == 0, a defined
            // limit (0, +-pi, the open-edge value). The route through Orient2d and the signs of the differences must give the value
            // of the integer evaluation (accurate_edge_angle_x, which decides num == 0 from its exact dot product) without calling it.
            var rnd = new Random(2024);
            var ps = new List<(Vector3d p, Vector3d a, Vector3d b)>();
            for (int i = 0; i < 4000; ++i) {
                double s = i % 4 == 0 ? 1.0 : Math.Pow(2, rnd.Next(-8, 8) * (i % 4 == 1 ? 40 : 1));       // 2^+-320: the rescaled path
                Func<double> q = () => rnd.Next(-6, 7) / 8.0 * s;
                Vector3d p = new Vector3d(q(), q(), q());
                int u = rnd.Next(-3, 4), v = rnd.Next(-3, 4);
                if (u == 0 && v == 0) u = 1;
                int m = rnd.Next(-4, 5), n = rnd.Next(-4, 5);           // the multiples of (u, v): 0 (a vertex), either side, both ends beyond or between
                Vector3d a = p + new Vector3d(rnd.Next(-6, 7) / 8.0 * s, m * u / 8.0 * s, m * v / 8.0 * s);
                Vector3d b = p + new Vector3d(rnd.Next(-6, 7) / 8.0 * s, n * u / 8.0 * s, n * v / 8.0 * s);
                if (i % 7 == 0) b.x = a.x;                              // an edge in a plane x = const through p: the open-edge case
                ps.Add((p, a, b));
            }
            int c0 = ExactWindingCache.AccurateEdgeAngleCalls;
            var got = new double[ps.Count];
            for (int i = 0; i < ps.Count; ++i)
                got[i] = ExactWindingCache.boundary_edge_angle_x(ps[i].p, ps[i].a, ps[i].b);
            Assert.Equal(c0, ExactWindingCache.AccurateEdgeAngleCalls);         // no call of the integer evaluation
            int pis = 0, halves = 0;
            for (int i = 0; i < ps.Count; ++i) {
                Assert.True(ExactWindingCacheInternalTestsNearOpenEdge.IsDegenerate(ps[i].p, ps[i].a, ps[i].b), $"input {i} is not on the yz line");
                // (p on the yz line of a vertex, a - p or b - p with a zero yz part, is decided earlier, as a vertex case, not by this route)
                if ((ps[i].a.y == ps[i].p.y && ps[i].a.z == ps[i].p.z) || (ps[i].b.y == ps[i].p.y && ps[i].b.z == ps[i].p.z)) continue;
                double expected = ExactWindingCache.accurate_edge_angle_x(ps[i].p, ps[i].a, ps[i].b);
                Assert.True(got[i] == expected, $"input {i}: p {ps[i].p} a {ps[i].a} b {ps[i].b}: {got[i]:R} vs {expected:R}");
                if (Math.Abs(got[i]) == Math.PI) ++pis;
                if (Math.Abs(got[i]) == Math.PI * 0.5) ++halves;
            }
            Assert.True(pis > 100 && halves > 20, $"{pis} values +-pi, {halves} values +-pi/2");     // the branch cut and the open edge are really covered
            Assert.True(ExactWindingCache.AccurateEdgeAngleCalls > c0);         // (the reference calls were counted: the counter works)
        }
    }
}
