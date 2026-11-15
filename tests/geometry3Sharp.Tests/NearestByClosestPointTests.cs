using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #873: the building blocks of MeshHausdorffDistance's coplanar fix. DMeshAABBTree3.FindNearestTriangleByClosestPoint picks the
    /// triangle by |p - c| (c the closest point), not by the squared-distance form, which cancels to about 1e-8 E near the surface;
    /// TriangleDeviationBounds.PointDistance measures the float cascade's corner distances so.
    /// </summary>
    public class NearestByClosestPointTests
    {
        static double ClosestPointDistance(DMesh3 mesh, int tid, Vector3d p)
        {
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out Vector3d c, out Vector3d _);
            return p.Distance(c);
        }

        /// <summary> the triangle found is the one of least |p - c| over all triangles (brute force, the same evaluation), for points
        /// in the plane of a jittered grid, just off it and far from it; FindNearestTriangle's choice, by the cancelling form, is
        /// farther for some of the points in the plane </summary>
        [Fact]
        public void FindsTheLeastClosestPointDistance()
        {
            DMesh3 grid = HausdorffCoplanarTests.Grid(9, 0.3, 11);
            var tree = new DMeshAABBTree3(grid, true);
            var rnd = new Random(12);
            int worseByForm = 0;
            foreach (double z in new[] { 0.0, 1e-12, 1e-6, 0.3, 50 })
                for (int k = 0; k < 400; ++k) {
                    var p = new Vector3d(1.2 * rnd.NextDouble() - 0.1, 1.2 * rnd.NextDouble() - 0.1, z);
                    int t = tree.FindNearestTriangleByClosestPoint(p, out double d);
                    double best = double.MaxValue;
                    foreach (int tid in grid.TriangleIndices())
                        best = Math.Min(best, ClosestPointDistance(grid, tid, p));
                    Assert.Equal(best, d);
                    Assert.Equal(d, ClosestPointDistance(grid, t, p));
                    if (ClosestPointDistance(grid, tree.FindNearestTriangle(p), p) > d) ++worseByForm;
                }
            // (points in the plane up to 1e-8 beside an edge: inside the square their exact distance is 0, the found one within an
            // ulp of it; the form's noise, about 1e-8 here, can pick the triangle on the other side of the edge)
            var rnd2 = new Random(13);
            foreach (int eid in grid.EdgeIndices()) {
                Index2i ev = grid.GetEdgeV(eid);
                Vector3d e0 = grid.GetVertex(ev.a), e1 = grid.GetVertex(ev.b), side = new Vector3d(e0.y - e1.y, e1.x - e0.x, 0).Normalized;
                Vector3d p = Vector3d.Lerp(e0, e1, 0.1 + 0.8 * rnd2.NextDouble()) + Math.Pow(10, -8 - 4 * rnd2.NextDouble()) * (rnd2.Next(2) * 2 - 1) * side;
                if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) continue;
                tree.FindNearestTriangleByClosestPoint(p, out double d);
                Assert.True(d <= 1e-15, $"{d:R}");
                if (ClosestPointDistance(grid, tree.FindNearestTriangle(p), p) > 1e-15) ++worseByForm;
            }
            Assert.True(worseByForm > 0);
        }

        [Fact]
        public void EmptyFilter_FindsNothing()
        {
            DMesh3 grid = HausdorffCoplanarTests.Grid(2, 0, 1);
            var tree = new DMeshAABBTree3(grid, true) { TriangleFilterF = (t) => false };
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangleByClosestPoint(new Vector3d(0.5, 0.5, 0), out double d));
            Assert.Equal(double.MaxValue, d);
        }

        /// <summary> a small triangle in the plane of two target triangles, across their shared edge (the corners nearest to different
        /// ones): with PointDistance the cascade's bound is the rounding (the plane split puts the cut points on the shared edge, at
        /// distance 0 from both); without it the corners' distances to the other triangle are the cancelling form's noise </summary>
        [Fact]
        public void PointDistance_CoplanarAcrossAnEdge()
        {
            var target = new DMesh3();
            int a = target.AppendVertex(new Vector3d(0, 0, 0)), b = target.AppendVertex(new Vector3d(1, 0, 0)),
                c = target.AppendVertex(new Vector3d(1, 1, 0)), d = target.AppendVertex(new Vector3d(0, 1, 0));
            int t0 = target.AppendTriangle(a, b, c), t1 = target.AppendTriangle(a, c, d);
            var rnd = new Random(14);
            double worst = 0, worstByForm = 0;
            for (int k = 0; k < 200; ++k) {
                double x = 0.1 + 0.8 * rnd.NextDouble(), h = 1e-3 * (0.5 + rnd.NextDouble());
                // (p0 below the diagonal y = x, p1 and p2 above)
                Vector3d p0 = new Vector3d(x + h, x - h, 0), p1 = new Vector3d(x - h, x + h, 0), p2 = new Vector3d(x + 0.3 * h, x + 2 * h, 0);
                var bounds = new TriangleDeviationBounds(target) { PointDistance = true };
                double u = bounds.UpperBound(p0, p1, p2, ClosestPointDistance(target, t0, p0), ClosestPointDistance(target, t1, p1),
                    ClosestPointDistance(target, t1, p2), t0, t1, t1, 0);
                worst = Math.Max(worst, u);
                var byForm = new TriangleDeviationBounds(target);
                worstByForm = Math.Max(worstByForm, byForm.UpperBound(p0, p1, p2, ClosestPointDistance(target, t0, p0),
                    ClosestPointDistance(target, t1, p1), ClosestPointDistance(target, t1, p2), t0, t1, t1, 0));
            }
            Assert.True(worst <= 1e-15, $"{worst:R}");
            // (the flag is what makes the difference: by the form some of these bounds are the noise, about 1e-9)
            Assert.True(worstByForm > 1e-12, $"{worstByForm:R}");
        }
    }
}
