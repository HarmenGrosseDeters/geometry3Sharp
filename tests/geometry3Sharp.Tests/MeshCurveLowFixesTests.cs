using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Low-severity shortcomings in mesh/ and curve/ from the class-by-class review
    /// (docs/Analyse-2026-10.md, "Mängel" of sections 2.2 and 2.3).
    /// </summary>
    public class MeshCurveLowFixesTests
    {
        static DMesh3 Sphere()
        {
            return new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        }

        // mesh/ -----------------------------------------------------------------------------

        /// <summary> RemesherPro whose projection set is empty, while the smoothing set is all vertices </summary>
        class NoProjectionRemesher : gs.RemesherPro
        {
            public NoProjectionRemesher(DMesh3 m) : base(m) { }
            protected override IEnumerable<int> project_vertices() { return new int[0]; }
        }

        class SphereTarget : IProjectionTarget
        {
            public double Radius;
            public Vector3d Project(Vector3d vPoint, int identifier = -1) { return Radius * vPoint.Normalized; }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RemesherPro_TrackedProjectionPass_UsesProjectionVertices(bool bParallel)
        {
            DMesh3 mesh = Sphere();
            var r = new NoProjectionRemesher(mesh) {
                EnableFlips = false, EnableSplits = false, EnableCollapses = false, EnableSmoothing = false,
                EnableParallelProjection = bParallel,
                ProjectionMode = Remesher.TargetProjectionMode.AfterRefinement,
            };
            r.SetProjectionTarget(new SphereTarget() { Radius = 2 });
            r.RemeshIteration();
            foreach (int vid in mesh.VertexIndices())
                Assert.Equal(1.0, mesh.GetVertex(vid).Length, 6);    // no vertex is in project_vertices()
        }

        [Fact]
        public void RemesherPro_TrackedProjectionPass_StillProjectsByDefault()
        {
            DMesh3 mesh = Sphere();
            var r = new gs.RemesherPro(mesh) {
                EnableFlips = false, EnableSplits = false, EnableCollapses = false, EnableSmoothing = false,
                ProjectionMode = Remesher.TargetProjectionMode.AfterRefinement,
            };
            r.SetProjectionTarget(new SphereTarget() { Radius = 2 });
            r.RemeshIteration();
            foreach (int vid in mesh.VertexIndices())
                Assert.Equal(2.0, mesh.GetVertex(vid).Length, 6);
        }

        /// <summary>
        /// timestamp++ in SetVertex is not atomic, so parallel passes lose increments. The timestamp
        /// still changes in every pass (each write stores a value read during the pass plus one, and
        /// the pass only starts after the cache was filled), so the cached bounds never go stale.
        /// </summary>
        [Fact]
        public void ParallelSetVertex_CachedBoundsAlwaysUpdated()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { EdgeVertices = 30 }.Generate().MakeDMesh();
            int[] vids = mesh.VertexIndices().ToArray();
            for (int pass = 1; pass <= 200; ++pass) {
                double before = mesh.CachedBounds.Min.x;
                gParallel.ForEach(vids, (vid) => {
                    Vector3d v = mesh.GetVertex(vid);
                    mesh.SetVertex(vid, v + Vector3d.AxisX);
                });
                Assert.Equal(before + 1, mesh.CachedBounds.Min.x, 9);
                Assert.Equal(mesh.GetBounds(), mesh.CachedBounds);
            }
        }

        [Fact]
        public void MeshEditor_AddTriangleStrip_UsesGroupID()
        {
            DMesh3 mesh = new DMesh3(true, false, false, true);
            var frames = new List<Frame3f>();
            var spans = new List<Interval1d>();
            for (int i = 0; i < 4; ++i) {
                frames.Add(new Frame3f(new Vector3f(i, 0, 0)));
                spans.Add(new Interval1d(0, 1));
            }
            int[] tris = new MeshEditor(mesh).AddTriangleStrip(frames, spans, 7);
            Assert.Equal(6, tris.Length);
            foreach (int tid in tris) {
                Assert.True(mesh.IsTriangle(tid));
                Assert.Equal(7, mesh.GetTriangleGroup(tid));
            }
        }

        static DMesh3 StripMesh(double z, int n)
        {
            DMesh3 mesh = new DMesh3();
            for (int i = 0; i < n; ++i) {
                mesh.AppendVertex(new Vector3d(i, 0, z));
                mesh.AppendVertex(new Vector3d(i, 1, z));
            }
            for (int i = 0; i + 1 < n; ++i) {
                mesh.AppendTriangle(2 * i, 2 * i + 2, 2 * i + 3);
                mesh.AppendTriangle(2 * i, 2 * i + 3, 2 * i + 1);
            }
            return mesh;
        }

        static EdgeSpan BottomSpan(DMesh3 mesh, int n, bool reversed = false)
        {
            var v = Enumerable.Range(0, n).Select(i => 2 * i).ToList();
            if (reversed)
                v.Reverse();
            return EdgeSpan.FromVertices(mesh, v);
        }

        [Fact]
        public void EdgeSpan_IsSameSpan()
        {
            DMesh3 m1 = StripMesh(0, 5), m2 = StripMesh(0, 5);
            EdgeSpan s1 = BottomSpan(m1, 5);
            Assert.True(s1.IsSameSpan(BottomSpan(m2, 5)));
            Assert.True(s1.IsSameSpan(s1));

            EdgeSpan rev = BottomSpan(m2, 5, true);
            Assert.False(s1.IsSameSpan(rev));
            Assert.True(s1.IsSameSpan(rev, true));
            Assert.False(s1.IsSameSpan(BottomSpan(m2, 5), true));

            Assert.False(s1.IsSameSpan(BottomSpan(m2, 4)));                 // different length
            Assert.False(s1.IsSameSpan(BottomSpan(StripMesh(0.1, 5), 5)));  // moved
            Assert.True(s1.IsSameSpan(BottomSpan(StripMesh(0.1, 5), 5), false, 0.2));
        }

        // curve/ ----------------------------------------------------------------------------

        [Fact]
        public void PlanarComplex_SolidSortComparer_IsConsistent()
        {
            var big = new AxisAlignedBox2d(0, 0, 10, 10);
            var small = new AxisAlignedBox2d(1, 1, 2, 2);
            var other = new AxisAlignedBox2d(20, 0, 25, 5);
            var same = new AxisAlignedBox2d(0, 0, 10, 10);
            var boxes = new[] { big, small, other, same };
            foreach (var x in boxes) {
                Assert.Equal(0, PlanarComplex.solid_sort_compare(x, x));
                foreach (var y in boxes)
                    Assert.Equal(-Math.Sign(PlanarComplex.solid_sort_compare(y, x)), Math.Sign(PlanarComplex.solid_sort_compare(x, y)));
            }
            Assert.True(PlanarComplex.solid_sort_compare(big, small) < 0);     // containers first
            Assert.True(PlanarComplex.solid_sort_compare(small, big) > 0);
        }

        /// <summary> Many loops with equal or nested bounds (more than 16, so List.Sort does not use insertion sort) </summary>
        [Fact]
        public void PlanarComplex_FindSolidRegions_ManyLoops()
        {
            PlanarComplex complex = new PlanarComplex();
            for (int k = 0; k < 10; ++k) {
                double s = 100 - 8 * k;         // concentric squares
                complex.Add(Polygon2d.MakeRectangle(Vector2d.Zero, s, s));
            }
            for (int k = 0; k < 12; ++k)
                complex.Add(Polygon2d.MakeCircle(1, 16).Translate(new Vector2d(200 + 3 * k, 0)));   // disjoint, all same size
            PlanarComplex.SolidRegionInfo info = complex.FindSolidRegions(0.0, false);
            // nested squares alternate outer/hole: 5 solids with one hole each, plus 12 disjoint circles
            Assert.Equal(5 + 12, info.Polygons.Count);
            Assert.Equal(5, info.Polygons.Count(p => p.Holes.Count == 1));
        }

        static Ellipse2d TestEllipse()
        {
            return new Ellipse2d(new Vector2d(1, -2), 30, 3, 1.5);
        }

        [Fact]
        public void Ellipse2d_FromCoefficients_RoundTrip()
        {
            Ellipse2d e = TestEllipse();
            double[] coeff = e.ToCoefficients();

            Ellipse2d e2 = new Ellipse2d(Vector2d.Zero, Vector2d.AxisX, Vector2d.AxisY, 1, 1);
            Assert.True(e2.FromCoefficients(coeff));
            Assert.True(e.Center.Distance(e2.Center) < 1e-9, $"center {e2.Center}");
            Assert.Equal(3, e2.Extent.x, 9);
            Assert.Equal(1.5, e2.Extent.y, 9);
            Assert.Equal(1, Math.Abs(e2.Axis0.Dot(e.Axis0)), 9);
            Assert.Equal(1, Math.Abs(e2.Axis1.Dot(e.Axis1)), 9);
            Assert.True(e2.Axis0.DotPerp(e2.Axis1) > 0);        // right-handed axes
            for (int i = 0; i < 16; ++i)
                Assert.Equal(0, e2.Evaluate(e.SampleT(i / 16.0)), 9);

            // the shared Matrix2d.Zero instance is not written into
            Assert.Equal(0, Matrix2d.Zero.m00); Assert.Equal(0, Matrix2d.Zero.m01);
            Assert.Equal(0, Matrix2d.Zero.m10); Assert.Equal(0, Matrix2d.Zero.m11);
        }

        [Fact]
        public void Ellipse2d_FromCoefficients_Circle()
        {
            // (x-2)^2 + (y-1)^2 = 4  ->  1 - 4x - 2y + x^2 + y^2 = 0
            Ellipse2d e = new Ellipse2d(Vector2d.Zero, Vector2d.AxisX, Vector2d.AxisY, 1, 1);
            Assert.True(e.FromCoefficients(new double[] { 1, -4, -2, 1, 0, 1 }));
            Assert.True(e.Center.Distance(new Vector2d(2, 1)) < 1e-12);
            Assert.Equal(2, e.Extent.x, 12);
            Assert.Equal(2, e.Extent.y, 12);
            Assert.Equal(0, e.Evaluate(new Vector2d(4, 1)), 12);
        }

        [Fact]
        public void Ellipse2d_FromCoefficients_RejectsNonEllipses()
        {
            Ellipse2d e = new Ellipse2d(Vector2d.Zero, Vector2d.AxisX, Vector2d.AxisY, 1, 1);
            Assert.False(e.FromCoefficients(new double[] { -1, 0, 0, 1, 0, -1 }));     // hyperbola x^2 - y^2 = 1
            Assert.False(e.FromCoefficients(new double[] { 1, 0, 0, 1, 0, 1 }));       // x^2 + y^2 = -1, no points
            Assert.False(e.FromCoefficients(new double[] { 0, 0, -1, 1, 0, 0 }));      // parabola y = x^2
        }

        [Fact]
        public void BezierCurve2_Reverse()
        {
            var ctrl = new[] { new Vector2d(0, 0), new Vector2d(1, 2), new Vector2d(3, 2), new Vector2d(4, -1) };
            BezierCurve2 c = new BezierCurve2(3, ctrl);
            BezierCurve2 r = (BezierCurve2)c.Clone();
            r.Reverse();
            for (int i = 0; i <= 10; ++i) {
                double t = i / 10.0;
                Assert.True(c.SampleT(t).Distance(r.SampleT(1 - t)) < 1e-12);
                Assert.True(c.TangentT(t).Distance(-r.TangentT(1 - t)) < 1e-9);
                Assert.True(c.GetSecondDerivative(t).Distance(r.GetSecondDerivative(1 - t)) < 1e-9);
            }
            Assert.Equal(c.ArcLength, r.ArcLength, 9);
            r.Reverse();
            for (int i = 0; i <= 10; ++i)
                Assert.True(c.SampleT(i / 10.0).Distance(r.SampleT(i / 10.0)) < 1e-12);
        }

        class Shift : ITransform2
        {
            public Vector2d TransformP(Vector2d p) { return p + new Vector2d(5, 0); }
            public Vector2d TransformN(Vector2d n) { return n; }
            public double TransformScalar(double s) { return s; }
        }

        [Fact]
        public void Polygon2d_SetVertices_UpdatesTimestamp()
        {
            Polygon2d p = Polygon2d.MakeRectangle(Vector2d.Zero, 2, 2);
            int ts = p.Timestamp;
            p.SetVertices(new List<Vector2d>() { Vector2d.Zero, Vector2d.AxisX, Vector2d.AxisY }, false);
            Assert.NotEqual(ts, p.Timestamp);
            ts = p.Timestamp;
            p.SetVertices(new List<Vector2d>() { Vector2d.Zero, Vector2d.AxisX, Vector2d.One }, true);
            Assert.NotEqual(ts, p.Timestamp);
        }

        static PolyLine2d Line()
        {
            return new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(2, 0), new Vector2d(3, 0) });
        }

        [Fact]
        public void PolyLine2d_Modifiers_UpdateTimestamp()
        {
            var ops = new Dictionary<string, Action<PolyLine2d>>() {
                { "VertexOffset", (p) => p.VertexOffset(0.5) },
                { "TrimStart", (p) => Assert.True(p.TrimStart(0.5)) },
                { "TrimEnd", (p) => Assert.True(p.TrimEnd(0.5)) },
                { "Trim", (p) => Assert.True(p.Trim(0.5)) },
                { "Transform", (p) => p.Transform(new Shift()) },
            };
            foreach (var op in ops) {
                PolyLine2d p = Line();
                int ts = p.Timestamp;
                op.Value(p);
                Assert.True(ts != p.Timestamp, op.Key + " did not change the timestamp");
            }
        }

        [Fact]
        public void NURBSCurve2_ConcurrentEvaluation()
        {
            int N = 8;
            var ctrl = new Vector2d[N];
            var w = new double[N];
            for (int i = 0; i < N; ++i) {
                ctrl[i] = new Vector2d(i, (i % 2 == 0) ? 0 : 3);
                w[i] = 1 + 0.25 * i;
            }
            NURBSCurve2 curve = new NURBSCurve2(N, ctrl, w, 3, false, true);

            int S = 2000;
            Vector2d[] expectP = new Vector2d[S], expectD = new Vector2d[S];
            for (int i = 0; i < S; ++i) {
                expectP[i] = curve.GetPosition(i / (double)(S - 1));
                expectD[i] = curve.GetFirstDerivative(i / (double)(S - 1));
            }

            int bad = 0;
            Parallel.For(0, 8, (thread) => {
                for (int rep = 0; rep < 20; ++rep) {
                    for (int k = 0; k < S; ++k) {
                        int i = (k * 7 + thread * 131) % S;
                        double t = i / (double)(S - 1);
                        if (curve.GetPosition(t).Distance(expectP[i]) > 1e-12 || curve.GetFirstDerivative(t).Distance(expectD[i]) > 1e-9)
                            System.Threading.Interlocked.Increment(ref bad);
                    }
                }
            });
            Assert.Equal(0, bad);
        }

        [Fact]
        public void DGraph_GetBounds_EmptyGraph_IsEmptyBox()
        {
            DGraph2 g2 = new DGraph2();
            Assert.Equal(AxisAlignedBox2d.Empty, g2.GetBounds());
            Assert.Equal(AxisAlignedBox2d.Empty, g2.CachedBounds);
            g2.AppendVertex(new Vector2d(3, 4));
            Assert.Equal(new AxisAlignedBox2d(new Vector2d(3, 4), new Vector2d(3, 4)), g2.CachedBounds);

            DGraph3 g3 = new DGraph3();
            Assert.Equal(AxisAlignedBox3d.Empty, g3.GetBounds());
            Assert.Equal(AxisAlignedBox3d.Empty, g3.CachedBounds);
        }
    }

    /// <summary> In a Debug build this changes Util.DebugBreakOnDevAssert, which is global </summary>
    [Collection("DevAssertThrows")]
    public class MeshCurveLowFixesTests_DevAssert
    {
        /// <summary>
        /// gDevAssert is [Conditional("DEBUG")], so in a Release build AppendTriangle returns InvalidID
        /// for invalid input and leaves the mesh unchanged. (In a Debug build the dev-assert fires first.)
        /// </summary>
        [Fact]
        public void AppendTriangle_InvalidInput_ReturnsInvalidIDInRelease()
        {
            DMesh3 mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(Vector3d.AxisY);
#if DEBUG
            bool prev = Util.DebugBreakOnDevAssert;
            Util.DebugBreakOnDevAssert = false;
            try {
                Assert.ThrowsAny<Exception>(() => mesh.AppendTriangle(a, b, 17));
            } finally {
                Util.DebugBreakOnDevAssert = prev;
            }
#else
            Assert.Equal(DMesh3.InvalidID, mesh.AppendTriangle(a, b, 17));      // not a vertex
            Assert.Equal(DMesh3.InvalidID, mesh.AppendTriangle(a, b, -1));
            Assert.Equal(DMesh3.InvalidID, mesh.AppendTriangle(a, b, b));       // duplicate vertex
            Assert.Equal(0, mesh.TriangleCount);
            Assert.Equal(0, mesh.EdgeCount);
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(0, mesh.AppendTriangle(a, b, c));
#endif
        }
    }
}
