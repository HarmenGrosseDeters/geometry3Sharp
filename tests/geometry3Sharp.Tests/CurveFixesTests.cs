using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Bugs in curve/ from the class-by-class review (docs/Analyse-2026-10.md, section 2.3).
    /// </summary>
    public class CurveFixesTests
    {
        static Polygon2d Square(double x0, double y0, double s)
        {
            return new Polygon2d(new[] { new Vector2d(x0, y0), new Vector2d(x0 + s, y0), new Vector2d(x0 + s, y0 + s), new Vector2d(x0, y0 + s) });
        }

        static void AssertNear(Vector2d expected, Vector2d actual, double tol = 1e-9)
        {
            Assert.True(expected.Distance(actual) < tol, $"expected {expected}, got {actual}");
        }

        static void AssertNoRepeatedVertices(IList<Vector2d> v, bool closed, string what)
        {
            for (int i = 0; i + 1 < v.Count; ++i)
                Assert.True(v[i].Distance(v[i + 1]) > 1e-12, $"{what}: vertices {i} and {i + 1} coincide ({v[i]})");
            if (closed && v.Count > 1)
                Assert.True(v[0].Distance(v[v.Count - 1]) > 1e-12, $"{what}: last vertex repeats the first ({v[0]})");
        }

        class MirrorX : ITransform2
        {
            public Vector2d TransformP(Vector2d p) { return new Vector2d(-p.x, p.y); }
            public Vector2d TransformN(Vector2d n) { return new Vector2d(-n.x, n.y); }
            public double TransformScalar(double s) { return s; }
        }
        class Shift : ITransform2
        {
            public Vector2d TransformP(Vector2d p) { return p + new Vector2d(5, 0); }
            public Vector2d TransformN(Vector2d n) { return n; }
            public double TransformScalar(double s) { return s; }
        }

        // --- Clone ---

        [Fact]
        public void NURBSCurve2_Clone_CanBeEvaluated()
        {
            var cps = new[] { new Vector2d(0, 0), new Vector2d(1, 1), new Vector2d(2, 0), new Vector2d(3, 1) };
            var curve = new NURBSCurve2(4, cps, new double[] { 1, 1, 1, 1 }, 3, false, true);
            var clone = curve.Clone();
            foreach (double t in new[] { 0.0, 0.3, 0.5, 1.0 })
                AssertNear(curve.SampleT(t), clone.SampleT(t));
        }

        [Fact]
        public void BezierCurve2_Clone_Degree2()
        {
            var curve = new BezierCurve2(2, new[] { new Vector2d(0, 0), new Vector2d(1, 1), new Vector2d(2, 0) });
            var clone = curve.Clone();
            AssertNear(curve.SampleT(0.5), clone.SampleT(0.5));
        }

        // --- Arc2d ---

        [Fact]
        public void Arc2d_Bounds_NegativeAngles()
        {
            var arc = new Arc2d(Vector2d.Zero, 1.0, -135, -45);
            AxisAlignedBox2d b = arc.Bounds;
            double s = Math.Sqrt(0.5);
            Assert.Equal(-s, b.Min.x, 9);
            Assert.Equal(s, b.Max.x, 9);
            Assert.Equal(-1, b.Min.y, 9);       // the arc passes -90 degrees
            Assert.Equal(-s, b.Max.y, 9);
        }

        [Fact]
        public void Arc2d_Transform_Mirror_KeepsTheArc()
        {
            var arc = new Arc2d(Vector2d.Zero, 1.0, 0, 90);
            Vector2d p0 = arc.P0, mid = arc.SampleT(0.5), p1 = arc.P1;
            double length = arc.ArcLength;
            arc.Transform(new MirrorX());
            Assert.Equal(length, arc.ArcLength, 9);
            AssertNear(new Vector2d(-p0.x, p0.y), arc.P0);
            AssertNear(new Vector2d(-mid.x, mid.y), arc.SampleT(0.5));
            AssertNear(new Vector2d(-p1.x, p1.y), arc.P1);
        }

        // --- Polygon2DCurve / PolyLine2DCurve ---

        [Fact]
        public void Polygon2DCurve_SamplesTheClosingEdge()
        {
            var curve = new Polygon2DCurve() { Polygon = Square(0, 0, 1) };
            Assert.Equal(4, curve.ParamLength);
            AssertNear(new Vector2d(0, 0.5), curve.SampleT(3.5));
            AssertNear(new Vector2d(0, 0), curve.SampleT(4));
            AssertNear(new Vector2d(0, -1), curve.TangentT(3.5));
            Assert.Equal(4, curve.ArcLength, 12);
            AssertNear(new Vector2d(0, 0.5), curve.SampleArcLength(3.5));
        }

        [Fact]
        public void PolyLine2DCurve_ParameterRangeAndArcLength()
        {
            var curve = new PolyLine2DCurve() { Polyline = new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(2, 0) }) };
            Assert.Equal(2, curve.ParamLength);
            var samples = CurveSampler2.SampleT(curve, 5);
            Assert.Equal(new[] { 0, 0.5, 1, 1.5, 2 }, samples.AsVector2d().Select(v => v.x).ToArray());
            AssertNear(new Vector2d(1.5, 0), curve.SampleArcLength(1.5));
            AssertNear(new Vector2d(1, 0), curve.TangentT(1.5));
        }

        [Fact]
        public void PlanarComplex_JoinElementsOfPolylines()
        {
            // JoinElements samples by arc length, which used to throw for polylines
            var pc = new PlanarComplex();
            pc.Add(new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(1, 0) }));
            pc.Add(new PolyLine2d(new[] { new Vector2d(1, 0), new Vector2d(2, 0) }));
            var ends = pc.EndpointsItr().ToList();
            Assert.True(pc.JoinElements(ends[1], ends[2]));
        }

        [Fact]
        public void PlanarComplex_Transform_DoesNotModifyTheCallersPolygon()
        {
            var pc = new PlanarComplex();
            var square = Square(0, 0, 1);
            pc.Add(square);
            pc.Transform(new Shift(), true, false);
            AssertNear(new Vector2d(0, 0), square[0]);
            pc.Transform(new Shift(), true, true);      // recompute from the (shifted) source
            var loop = pc.LoopsItr().First();
            AssertNear(new Vector2d(10, 0), loop.polygon[0]);
        }

        // --- sampling of closed curves ---

        [Fact]
        public void PlanarComplex_ClosedCurvePolygons_DoNotRepeatTheStart()
        {
            var pc = new PlanarComplex();
            var circle = pc.Add(new Circle2d(Vector2d.Zero, 1.0)) as PlanarComplex.SmoothLoopElement;
            var ellipse = pc.Add(new Ellipse2d(Vector2d.Zero, 0, 2, 1)) as PlanarComplex.SmoothLoopElement;
            AssertNoRepeatedVertices(circle.polygon.Vertices.ToList(), true, "circle");
            AssertNoRepeatedVertices(ellipse.polygon.Vertices.ToList(), true, "ellipse");
        }

        [Fact]
        public void CurveSampler2_ClosedCurves_DoNotRepeatTheStart()
        {
            var circle = new Circle2d(Vector2d.Zero, 1.0);
            AssertNoRepeatedVertices(CurveSampler2.SampleT(circle, 0.1).AsVector2d().ToList(), true, "SampleT");
            AssertNoRepeatedVertices(CurveSampler2.SampleArcLen(circle, 0.1).AsVector2d().ToList(), true, "SampleArcLen");
        }

        [Fact]
        public void ParametricCurveSequence2_IsClosedFromConstructor()
        {
            var open = new ParametricCurveSequence2(new IParametricCurve2d[] { new Segment2d(Vector2d.Zero, Vector2d.AxisX) }, false);
            var closed = new ParametricCurveSequence2(new IParametricCurve2d[] { new Segment2d(Vector2d.Zero, Vector2d.AxisX) }, true);
            Assert.False(open.IsClosed);
            Assert.True(closed.IsClosed);
        }

        // --- GeneralPolygon2d ---

        [Fact]
        public void GeneralPolygon2d_Contains_RejectsPolygonsOverlappingAHole()
        {
            var g = new GeneralPolygon2d(Square(0, 0, 10));
            var hole = Square(4, 4, 2);
            hole.Reverse();
            g.AddHole(hole);
            Assert.False(g.Contains(Square(3, 3, 4)), "encloses the hole");
            Assert.False(g.Contains(Square(5, 5, 3)), "crosses the hole");
            Assert.False(g.Contains(Square(4.5, 4.5, 1)), "lies in the hole");
            Assert.True(g.Contains(Square(1, 1, 2)), "away from the hole");
        }

        // --- DPolyLine2f ---

        [Fact]
        public void DPolyLine2f_OrderVertices()
        {
            var p = new DPolyLine2f();
            p.AddVertex(0, 0); p.AddVertex(1, 1); p.AddVertex(1, 0);
            p.AddEdge(0, 2); p.AddEdge(2, 1); p.AddEdge(1, 0);
            Assert.True(p.OrderVertices());
            // following the edges from vertex 0 gives (0,0) (1,0) (1,1), with edges between neighbours
            var v = p.Vertices;
            Assert.Equal(new[] { 0f, 1f, 1f }, v.Select(x => x.x).ToArray());
            Assert.Equal(new[] { 0f, 0f, 1f }, v.Select(x => x.y).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, v.Select(x => x.index).ToArray());
            Assert.Equal(new[] { (0, 1), (1, 2), (2, 0) }, p.Edges.Select(e => (e.v1, e.v2)).ToArray());
        }

        // --- CurveUtils / CurveResampler / CurveUtils2 ---

        [Fact]
        public void CurveUtils_IterativeSmooth_OpenKeepsEndpoints()
        {
            var l = new List<Vector3d> { new Vector3d(1, 1, 1), new Vector3d(2, 2, 0), new Vector3d(3, 1, 1), new Vector3d(4, 4, 4) };
            CurveUtils.IterativeSmooth(l, 0.5, 1, false);
            Assert.Equal(new Vector3d(1, 1, 1), l[0]);
            Assert.Equal(new Vector3d(4, 4, 4), l[3]);
            Assert.Equal(new Vector3d(2, 1.5, 0.5), l[1]);      // 0.5 * (2,2,0) + 0.5 * midpoint of (1,1,1) and (3,1,1)
        }

        [Fact]
        public void CurveUtils_Smooth_ClosedRangeWrappingAround()
        {
            var l = Enumerable.Range(0, 6).Select(i => new Vector3d(Math.Cos(i), Math.Sin(i), 0)).ToList();
            var a = new List<Vector3d>(l);
            var b = new List<Vector3d>(l);
            CurveUtils.IterativeSmooth(a, 4, 8, 0.5, 1, true);        // indices 4,5,0,1
            CurveUtils.InPlaceSmooth(b, 4, 8, 0.5, 1, true);
            Assert.NotEqual(l[0], a[0]);
            Assert.NotEqual(l[0], b[0]);
        }

        static DCurve3 Triangle() { return new DCurve3(new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(10, 10, 0) }, true); }

        [Fact]
        public void CurveResampler_ClosedCurves()
        {
            List<Vector3d> split = new CurveResampler().SplitResample(Triangle(), 4);
            Assert.NotNull(split);
            Assert.True(split[0].Distance(split[split.Count - 1]) > 1e-9, "last vertex repeats the first");
            for (int i = 0; i < split.Count; ++i)
                Assert.True(split[i].Distance(split[(i + 1) % split.Count]) <= 4 + 1e-9, $"edge {i} too long");

            List<Vector3d> both = new CurveResampler().SplitCollapseResample(Triangle(), 6, 0.1);
            Assert.True(both[0].Distance(both[both.Count - 1]) > 1e-9, "last vertex repeats the first");
        }

        [Fact]
        public void CurveUtils2_SplitToTargetLength_DoesNotRepeatTheStart()
        {
            Polygon2d result = CurveUtils2.SplitToTargetLength(Square(0, 0, 1), 2.0);
            Assert.Equal(4, result.VertexCount);
            AssertNoRepeatedVertices(result.Vertices.ToList(), true, "square");
            Polygon2d split = CurveUtils2.SplitToTargetLength(Square(0, 0, 4), 1.0);
            Assert.Equal(16, split.VertexCount);
            AssertNoRepeatedVertices(split.Vertices.ToList(), true, "split square");
        }

        // --- DGraph2 utilities ---

        [Fact]
        public void DGraph2Util_FindRayIntersection_TestsEdges()
        {
            // more edges than vertices, and the hit edge has an id >= VertexCount
            var g = new DGraph2();
            int c1 = g.AppendVertex(new Vector2d(3, 1)), c2 = g.AppendVertex(new Vector2d(10, 5)), c3 = g.AppendVertex(new Vector2d(12, 6)), c4 = g.AppendVertex(new Vector2d(14, 9));
            g.AppendEdge(c1, c2); g.AppendEdge(c1, c3); g.AppendEdge(c1, c4); g.AppendEdge(c2, c3); g.AppendEdge(c2, c4); g.AppendEdge(c3, c4);
            int hitEdge = g.AppendEdge(c1, g.AppendVertex(new Vector2d(3, -1)));
            Assert.True(DGraph2Util.FindRayIntersection(Vector2d.Zero, Vector2d.AxisX, out int eid, out double t, g));
            Assert.Equal(hitEdge, eid);
            Assert.Equal(3, t, 9);
        }

        [Fact]
        public void DGraph2Util_ChainOpenPaths_NoDuplicateJoinVertices()
        {
            var p1 = new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1) });
            var p2 = new PolyLine2d(new[] { new Vector2d(1, 1), new Vector2d(0, 1), new Vector2d(0, 0) });
            var c = new DGraph2Util.Curves() { Loops = new List<Polygon2d>(), Paths = new List<PolyLine2d> { p1, p2 } };
            DGraph2Util.ChainOpenPaths(c);
            Polygon2d loop = Assert.Single(c.Loops);
            Assert.Equal(4, loop.VertexCount);
            AssertNoRepeatedVertices(loop.Vertices.ToList(), true, "loop");
            AssertNear(new Vector2d(1, 1), p2.Start);       // the inputs are not modified
        }

        [Fact]
        public void DGraph2Resampler_EmptyGraph()
        {
            var r = new DGraph2Resampler(new DGraph2());
            r.CollapseFlatVertices();
            r.CollapseToMinEdgeLength(1.0);
        }

        // --- PolySimplification2 ---

        [Fact]
        public void PolySimplification2_PreserveStraightSegLen_IsALength()
        {
            // the default PreserveStraightSegLen is 2: a straight segment shorter than that is not preserved
            int Count(double L)
            {
                var pl = new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(L, 0), new Vector2d(L + 0.1, 0.05), new Vector2d(L + 0.2, 0) });
                var s = new PolySimplification2(pl);
                s.Simplify();
                return s.Result.Count;
            }
            Assert.Equal(Count(1.4), Count(1.9));
        }

        [Fact]
        public void PolySimplification2_NoRepeatedVertices()
        {
            var line = new PolySimplification2(new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(0.5, 0.5), new Vector2d(1, 0), new Vector2d(10, 0) }));
            line.Simplify();
            AssertNoRepeatedVertices(line.Result, false, "polyline");

            var loop = new PolySimplification2(Square(0, 0, 10));
            loop.Simplify();
            Assert.Equal(4, loop.Result.Count);
            AssertNoRepeatedVertices(loop.Result, true, "loop");
        }

        // --- Polygon2d.Chamfer ---

        [Fact]
        public void Polygon2d_Chamfer_ConvexAndConcaveThresholds()
        {
            // all corners of a square are convex, 90 degrees
            var a = Square(0, 0, 10);
            a.Chamfer(1, 100, 10);      // convex corners below 100 degrees are cut
            Assert.Equal(8, a.VertexCount);
            var b = Square(0, 0, 10);
            b.Chamfer(1, 10, 100);      // concave threshold does not apply
            Assert.Equal(4, b.VertexCount);
        }

        // --- deferred items: Circle2d/Ellipse2d.Transform with mirroring, periodic NURBS ---

        /// <summary> signed area of the curve sampled at n points: positive for counter-clockwise </summary>
        static double SignedArea(IParametricCurve2d c, int n = 64)
        {
            var poly = new Polygon2d();
            for (int i = 0; i < n; ++i)
                poly.AppendVertex(c.SampleT(c.ParamLength * i / n));
            return poly.SignedArea;
        }

        [Fact]
        public void Circle2d_Transform_Mirror_ReversesOrientation()
        {
            // the mirrored point set runs the other way round; the circle has no start angle, so only the
            // orientation (and the point set) can follow the transform
            var circle = new Circle2d(new Vector2d(2, 1), 1.5);
            double area = SignedArea(circle);
            circle.Transform(new MirrorX());
            AssertNear(new Vector2d(-2, 1), circle.Center);
            Assert.Equal(-area, SignedArea(circle), 9);
            circle.Transform(new MirrorX());
            Assert.Equal(area, SignedArea(circle), 9);
        }

        [Fact]
        public void Ellipse2d_Transform_Mirror_FollowsThePoints()
        {
            // the axes are mirrored too, so every sample is the mirrored sample (orientation included)
            var ellipse = new Ellipse2d(new Vector2d(2, 1), 30, 3, 1);
            Vector2d[] before = Enumerable.Range(0, 8).Select(i => ellipse.SampleT(i / 8.0)).ToArray();
            double area = SignedArea(ellipse);
            ellipse.Transform(new MirrorX());
            for (int i = 0; i < 8; ++i)
                AssertNear(new Vector2d(-before[i].x, before[i].y), ellipse.SampleT(i / 8.0));
            Assert.Equal(-area, SignedArea(ellipse), 9);
        }

        static NURBSCurve2 PeriodicSquareSpline(int degree)
        {
            var cps = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(1, 1), new Vector2d(0, 1) };
            return new NURBSCurve2(4, cps, new double[] { 1, 1, 1, 1 }, degree, true, false);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void NURBSCurve2_Periodic_ParamIntervals(int degree)
        {
            NURBSCurve2 curve = PeriodicSquareSpline(degree);
            List<double> l = curve.GetParamIntervals();
            Assert.Equal(0, l.First());
            Assert.Equal(1, l.Last());
            for (int i = 1; i < l.Count; ++i)
                Assert.True(l[i] > l[i - 1], $"not increasing: {string.Join(", ", l)}");
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void NURBSCurve2_Periodic_ContinuousParamIntervals(int degree)
        {
            // uniform periodic knots all have multiplicity 1: the curve is smooth over [0,1]
            NURBSCurve2 curve = PeriodicSquareSpline(degree);
            Assert.Equal(new List<double> { 0, 1 }, curve.GetContinuousParamIntervals());
        }

        [Fact]
        public void NURBSCurve2_Open_ParamIntervalsUnchanged()
        {
            var cps = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(2, 1), new Vector2d(3, 1), new Vector2d(4, 0) };
            var curve = new NURBSCurve2(5, cps, new double[] { 1, 1, 1, 1, 1 }, 2, false, true);
            Assert.Equal(new List<double> { 0, 1.0 / 3, 2.0 / 3, 1 }, curve.GetParamIntervals());
            Assert.Equal(new List<double> { 0, 1 }, curve.GetContinuousParamIntervals());
        }
    }
}
