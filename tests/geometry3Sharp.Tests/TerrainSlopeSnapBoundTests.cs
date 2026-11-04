// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The bound of one composite triangle of the snap certificate (SlopeComposite.TriangleBound, docs/design/TERRAIN.md
    /// 5.5) on synthetic surfaces, one case per candidate class where that class alone holds the extreme of
    /// f = z - max(S_fill, min(T, S_cut)), and the steps at a slope surface's boundary edge. The triangle t is
    /// (0, 0), (10, 0), (0, 10) at z = 0, a terrain triangle unless said otherwise.
    /// </summary>
    public class TerrainSlopeSnapBoundTests
    {
        static readonly Vector3d A = new Vector3d(0, 0, 0), B = new Vector3d(10, 0, 0), C = new Vector3d(0, 10, 0);

        // a fan from a centre over the square [-20, 40]^2 (counter-clockwise), z = f at the corners, zc at the centre
        static DMesh3 Fan(Vector2d centre, double zc, Func<double, double, double> f)
        {
            var m = new DMesh3();
            var corners = new[] { new Vector2d(-20, -20), new Vector2d(40, -20), new Vector2d(40, 40), new Vector2d(-20, 40) };
            int c = m.AppendVertex(new Vector3d(centre.x, centre.y, zc));
            var ids = new int[4];
            for (int i = 0; i < 4; ++i) ids[i] = m.AppendVertex(new Vector3d(corners[i].x, corners[i].y, f(corners[i].x, corners[i].y)));
            for (int i = 0; i < 4; ++i) m.AppendTriangle(c, ids[i], ids[(i + 1) % 4]);
            return m;
        }

        // two triangles over the square, split by the diagonal x + y = 20, which misses t
        static DMesh3 Plane(Func<double, double, double> f, double x0 = -20, double y0 = -20, double x1 = 40, double y1 = 40)
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(x0, y0, f(x0, y0))), b = m.AppendVertex(new Vector3d(x1, y0, f(x1, y0)));
            int c = m.AppendVertex(new Vector3d(x1, y1, f(x1, y1))), d = m.AppendVertex(new Vector3d(x0, y1, f(x0, y1)));
            m.AppendTriangle(a, b, d);
            m.AppendTriangle(b, c, d);
            return m;
        }

        // a valley along the line x = xv (an edge of the mesh): z = k |x - xv| + o over the square
        static DMesh3 Valley(double xv, double k, double o)
        {
            var m = new DMesh3();
            Func<double, double, int> v = (x, y) => m.AppendVertex(new Vector3d(x, y, k * Math.Abs(x - xv) + o));
            int a = v(-20, -20), b = v(xv, -20), c = v(40, -20), d = v(-20, 40), e = v(xv, 40), f = v(40, 40);
            m.AppendTriangle(a, b, d); m.AppendTriangle(b, e, d);
            m.AppendTriangle(b, c, e); m.AppendTriangle(c, f, e);
            return m;
        }

        static double Bound(DMesh3 terrain, DMesh3 cut, DMesh3 fill = null, int ownRun = -1, double move = 0.01,
                            Vector3d? a = null, Vector3d? b = null, Vector3d? c = null)
        {
            var slopes = new List<SlopeComposite.SurfaceIndex>();
            var dirs = new List<int>();
            if (cut != null) { slopes.Add(new SlopeComposite.SurfaceIndex(cut)); dirs.Add(+1); }
            if (fill != null) { slopes.Add(new SlopeComposite.SurfaceIndex(fill)); dirs.Add(-1); }
            return SlopeComposite.TriangleBound(a ?? A, b ?? B, c ?? C, ownRun, new SlopeComposite.SurfaceIndex(terrain), slopes, dirs.ToArray(), move);
        }

        // (no slope surface where t is)
        static DMesh3 Away() => Plane((x, y) => -5, 100, 100, 110, 110);

        // class 1, the corners: T = -0.1 x, f = z - T = 0.1 x, largest at (10, 0)
        [Fact]
        public void Corner()
        {
            Assert.Equal(1.0, Bound(Plane((x, y) => -0.1 * x), Away()), 12);
        }

        // class 2, a terrain vertex inside t: a pit of the terrain at (2, 2), 1 below the plane z = 0
        [Fact]
        public void TerrainVertexInside()
        {
            Assert.Equal(1.0, Bound(Fan(new Vector2d(2, 2), -1, (x, y) => 0), Away()), 12);
        }

        // class 3, a slope vertex inside t: the terrain 0.5 above, the cut slope with a pit at (2, 2) 1 below; also as the
        // lower bound of a fill slope with a peak there
        [Fact]
        public void SlopeVertexInside()
        {
            Assert.Equal(1.0, Bound(Plane((x, y) => 0.5), Fan(new Vector2d(2, 2), -1, (x, y) => 0)), 12);
            Assert.Equal(1.0, Bound(Plane((x, y) => 0), null, Fan(new Vector2d(2, 2), 1, (x, y) => 0)), 12);
        }

        // class 4, a crossing of t's edge with a mesh edge: a terrain valley along x = 3, 3 below t there, so z - T is
        // largest on the valley's line, from (3, 0) to (3, 7) on t's edges (1.5 at the corners (0, 0) and (0, 10))
        [Fact]
        public void CrossingOfTheTrianglesEdge()
        {
            Assert.Equal(3.0, Bound(Valley(3, 0.5, -3), Away()), 12);
        }

        // the steps at a boundary edge of a slope surface: the cut slope at z = -1 on y >= -0.1 (its lower edge the toe),
        // the terrain at z = 0, so t (a terrain triangle) is 1 above the slope where it overlaps it. Its overlap within
        // the move m (0.01) of the edge's line is that step, displaced by the move (not compared); farther it counts
        [Theory]
        [InlineData(0.0, 0.3, 1.0)]       // t from 0.1 to 0.4 from the line: 1
        [InlineData(-0.1, -0.095, 0.0)]   // t within 0.005 of the line: the step
        [InlineData(-0.1, -0.08, 1.0)]    // t up to 0.02 from it: 1
        public void Steps(double y0, double y1, double expected)
        {
            DMesh3 cut = Plane((x, y) => -1, -20, -0.1, 40, 40);
            double b = Bound(Plane((x, y) => 0), cut, null, -1, 0.01,
                             new Vector3d(0, y0, 0), new Vector3d(10, y0, 0), new Vector3d(5, y1, 0));
            Assert.Equal(expected, b, 12);
        }

        // a triangle without area in XY (a vertical wall of a step, eg at a toe crossing) holds no point of the height
        // field: 0, whatever the surfaces
        [Fact]
        public void NoAreaInXY()
        {
            Assert.Equal(0.0, Bound(Plane((x, y) => -10), Away(), null, -1, 0.01,
                                    new Vector3d(0, 0, 0), new Vector3d(5, 0, 1), new Vector3d(10, 0, 0)));
        }

        // a candidate off the terrain (t reaches beyond it): not certified
        [Fact]
        public void OffTheTerrain()
        {
            Assert.Equal(double.PositiveInfinity, Bound(Plane((x, y) => 0, -20, -20, 5, 40), Away()));
        }

        // the step's capsule is around the boundary edge, not its line: the cut slope at z = -1 on an L-shaped domain
        // whose edge on y = -0.1 runs from x = -20 to -5 only (below it, for x > -5, the domain goes on). The slope
        // triangle of that edge, (a, b, q), overlaps t, so the edge is tested: near t the overlap is within m (0.5) of
        // its line but 5 from the edge, so it counts, 1
        [Fact]
        public void StepCapsuleNotLine()
        {
            var cut = new DMesh3();
            Func<double, double, int> v = (x, y) => cut.AppendVertex(new Vector3d(x, y, -1));
            int a = v(-20, -0.1), b = v(-5, -0.1), c = v(-5, -20), d = v(40, -20), q = v(40, 0.5), e = v(40, 40), f = v(-20, 40);
            cut.AppendTriangle(a, b, q); cut.AppendTriangle(b, c, d); cut.AppendTriangle(b, d, q);
            cut.AppendTriangle(a, q, e); cut.AppendTriangle(a, e, f);
            Assert.Equal(1.0, Bound(Plane((x, y) => 0), cut, null, -1, 0.5,
                                    new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(5, 0.3, 0)), 12);
        }

        // the slope triangles of the run itself: on its own plane (a cut triangle 1 below the terrain: 0; above it: by how
        // much), a fill triangle never above the clamp
        [Fact]
        public void OwnRun()
        {
            DMesh3 under = Plane((x, y) => -1);
            Assert.Equal(0.0, Bound(Plane((x, y) => 1), under, null, 0), 12);
            Assert.Equal(0.5, Bound(Plane((x, y) => -0.05 * x), under, null, 0), 12);
            Assert.Equal(0.0, Bound(Plane((x, y) => -1), null, under, 0), 12);
            // (a fill triangle below the terrain: f = z - max(S_fill, T) = -1)
            Assert.Equal(1.0, Bound(Plane((x, y) => 1), null, under, 0), 12);
        }
    }
}
