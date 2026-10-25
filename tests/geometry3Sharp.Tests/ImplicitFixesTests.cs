using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Bugs in implicit/ from the class-by-class review (docs/Analyse-2026-10.md, section 2.6).
    /// </summary>
    public class ImplicitFixesTests
    {
        /// <summary> constant 2D field with a fixed gradient, to see which child an operator picks </summary>
        class ConstField2d : ImplicitField2d
        {
            public float V;
            public float GX, GY;
            public AxisAlignedBox2f Box = new AxisAlignedBox2f(0, 0, 1, 1);
            public float Value(float fX, float fY) { return V; }
            public void Gradient(float fX, float fY, ref float fGX, ref float fGY) { fGX = GX; fGY = GY; }
            public AxisAlignedBox2f Bounds { get { return Box; } }
        }

        /// <summary> f(p) = p.x, exact gradient (1,0,0) </summary>
        class PlaneX3d : ImplicitFunction3d
        {
            public double Value(ref Vector3d pt) { return pt.x; }
        }

        // iso-radius of ImplicitPoint2d: (1 - d^2/r^2)^3 = iso
        static double IsoRadius(double r, double iso)
        {
            return r * Math.Sqrt(1 - Math.Pow(iso, 1.0 / 3.0));
        }

        static void AssertStrokeOnCircle(DPolyLine2f stroke, Vector2d c, double radius, double tol)
        {
            Assert.True(stroke.EdgeCount > 0, "no contour produced");
            foreach (var v in stroke.Vertices) {
                double d = new Vector2d(v.x, v.y).Distance(c);
                Assert.True(Math.Abs(d - radius) < tol, $"vertex ({v.x}, {v.y}) at distance {d}, expected {radius}");
            }
        }


        [Fact]
        public void Intersection2d_Gradient_UsesChildWithMinimumValue()
        {
            var op = new ImplicitIntersection2d();
            op.AddChild(new ConstField2d() { V = 0.5f, GX = 1, GY = 0 });
            op.AddChild(new ConstField2d() { V = 0.9f, GX = 2, GY = 0 });
            op.AddChild(new ConstField2d() { V = 0.7f, GX = 3, GY = 0 });
            float gx = 0, gy = 0;
            op.Gradient(0, 0, ref gx, ref gy);
            Assert.Equal(1.0f, gx);
        }

        [Fact]
        public void NAryOp2d_Bounds_DoesNotIncludeOrigin()
        {
            var op = new ImplicitUnion2d();
            op.AddChild(new ConstField2d() { Box = new AxisAlignedBox2f(5, 5, 6, 6) });
            AxisAlignedBox2f box = op.Bounds;
            Assert.Equal(5.0f, box.Min.x);
            Assert.Equal(5.0f, box.Min.y);
            Assert.Equal(6.0f, box.Max.x);
            Assert.Equal(6.0f, box.Max.y);
        }

        [Fact]
        public void Point2d_Gradient_UsesRadius()
        {
            // f = (1 - d^2/r^2)^3, df/dx = -6 x (1 - d^2/r^2)^2 / r^2
            var pt = new ImplicitPoint2d(0, 0, 2);
            float gx = 0, gy = 0;
            pt.Gradient(1, 0, ref gx, ref gy);
            Assert.Equal(-6.0 * 0.75 * 0.75 / 4.0, gx, 5);
            Assert.Equal(0.0, gy, 5);

            // compare against finite differences away from the axes
            const float h = 1e-3f;
            pt.Gradient(0.7f, -0.4f, ref gx, ref gy);
            double fdx = (pt.Value(0.7f + h, -0.4f) - pt.Value(0.7f - h, -0.4f)) / (2 * h);
            double fdy = (pt.Value(0.7f, -0.4f + h) - pt.Value(0.7f, -0.4f - h)) / (2 * h);
            Assert.Equal(fdx, gx, 2);
            Assert.Equal(fdy, gy, 2);
        }

        [Fact]
        public void Point2d_Gradient_IsZeroAtCenter()
        {
            var pt = new ImplicitPoint2d(3, 4, 1);
            float gx = 1, gy = 1;
            pt.Gradient(3, 4, ref gx, ref gy);
            Assert.Equal(0.0f, gx);
            Assert.Equal(0.0f, gy);
        }

        [Fact]
        public void MarchingQuads_PositiveBoundsMin_ProducesContour()
        {
            const float iso = 0.5f;
            var field = new ImplicitPoint2d(10, 10, 1);
            var mq = new MarchingQuads(64, new AxisAlignedBox2f(8, 8, 12, 12), iso);
            mq.AddSeedPoint(10, 10);
            mq.Polygonize(field);
            AssertStrokeOnCircle(mq.Stroke, new Vector2d(10, 10), IsoRadius(1, iso), 0.01);
        }

        [Fact]
        public void MarchingQuads_SecondRun_DoesNotReuseOldValues()
        {
            const float iso = 0.5f;
            var mq = new MarchingQuads(64, new AxisAlignedBox2f(-2, -2, 2, 2), iso);
            mq.AddSeedPoint(0, 0);
            mq.Polygonize(new ImplicitPoint2d(0, 0, 1));
            mq.ClearStroke();
            mq.Polygonize(new ImplicitPoint2d(0, 0, 1.8f));
            AssertStrokeOnCircle(mq.Stroke, Vector2d.Zero, IsoRadius(1.8, iso), 0.01);
        }


        [Fact]
        public void DenseGridTrilinear_Gradient_IsInWorldUnits()
        {
            const double cs = 0.5;
            var origin = new Vector3d(-1, -2, -3);
            var grid = new DenseGrid3f(8, 8, 8, 0);
            for (int k = 0; k < 8; ++k)
                for (int j = 0; j < 8; ++j)
                    for (int i = 0; i < 8; ++i)
                        grid[i, j, k] = (float)(origin.x + i * cs);
            var f = new DenseGridTrilinearImplicit(grid, origin, cs);
            var p = new Vector3d(0.3, -0.7, -1.1);
            Vector3d g = f.Gradient(ref p);
            Assert.True(g.Distance(Vector3d.AxisX) < 1e-6, $"gradient {g}, expected (1,0,0)");
        }

        [Fact]
        public void CachingDenseGridTrilinear_Gradient_IsInWorldUnits()
        {
            var f = new CachingDenseGridTrilinearImplicit(new Vector3d(-1, -2, -3), 0.5, new Vector3i(8, 8, 8)) { AnalyticF = new PlaneX3d() };
            var p = new Vector3d(0.3, -0.7, -1.1);
            Vector3d g = f.Gradient(ref p);
            Assert.True(g.Distance(Vector3d.AxisX) < 1e-6, $"gradient {g}, expected (1,0,0)");
        }

        [Fact]
        public void CachingDenseGridTrilinear_Value_JustBelowOriginIsOutside()
        {
            var f = new CachingDenseGridTrilinearImplicit(Vector3d.Zero, 1.0, new Vector3i(4, 4, 4)) { AnalyticF = new PlaneX3d() };
            var p = new Vector3d(-0.5, 1.5, 1.5);
            Assert.Equal(f.Outside, f.Value(ref p));
            p = new Vector3d(0.5, 1.5, 1.5);
            Assert.Equal(0.5, f.Value(ref p), 6);
        }


        static CachingMeshSDF MakeBoxSDF(double halfSize, double cellSize, bool computeSigns)
        {
            DMesh3 mesh = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, halfSize * Vector3d.One) }.Generate().MakeDMesh();
            var sdf = new CachingMeshSDF(mesh, cellSize, new DMeshAABBTree3(mesh, true)) {
                ComputeSigns = computeSigns, MaxOffsetDistance = (float)(2 * cellSize)
            };
            sdf.Initialize();
            return sdf;
        }

        [Fact]
        public void CachingMeshSDFImplicit_Gradient_IsInWorldUnits()
        {
            var f = new CachingMeshSDFImplicit(MakeBoxSDF(5, 0.25, true));
            var p = new Vector3d(5.33, 0.1, 0.2);
            Vector3d g = f.Gradient(ref p);
            Assert.True(g.Distance(Vector3d.AxisX) < 0.05, $"gradient {g}, expected about (1,0,0)");
        }

        [Fact]
        public void CachingMeshSDFImplicit_Value_JustBelowOriginIsOutside()
        {
            var f = new CachingMeshSDFImplicit(MakeBoxSDF(5, 0.25, true));
            var p = f.GridOrigin + new Vector3d(-0.1, 2, 2);
            Assert.Equal(f.Outside, f.Value(ref p));
        }

        [Fact]
        public void CachingMeshSDF_FarCell_IsCachedForLargeUpperBound()
        {
            // UpperBoundDistance = (ni+nj+nk)*CellSize is well above 2048 here,
            // where a +0.0001 nudge is lost in float precision
            var sdf = MakeBoxSDF(500, 10, false);
            var idx = Vector3i.Zero;
            float marker = sdf[idx];
            Assert.True(marker >= 2048, $"test setup: marker {marker} should be >= 2048");
            float v = sdf.GetValue(idx);
            Assert.NotEqual(marker, sdf[idx]);
            Assert.True(v >= marker, $"far value {v} should stay at least {marker}");
            Assert.Equal(v, sdf.GetValue(idx));
        }


        [Fact]
        public void Intersection3d_Bounds_IsIntersectionOfChildBounds()
        {
            var op = new ImplicitIntersection3d() {
                A = new ImplicitSphere3d() { Origin = Vector3d.Zero, Radius = 1 },
                B = new ImplicitSphere3d() { Origin = new Vector3d(1, 0, 0), Radius = 1 }
            };
            AxisAlignedBox3d box = op.Bounds();
            AxisAlignedBox3d expect = op.A.Bounds().Intersect(op.B.Bounds());
            Assert.True(box.Min.Distance(expect.Min) < 1e-9 && box.Max.Distance(expect.Max) < 1e-9, $"bounds {box}, expected {expect}");
        }
    }
}
