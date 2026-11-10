using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // The diagnostics hook of DualContouring (measurement for docs/design/DC_INTERSECTION_FREE.md) is a mutable static:
    // these tests do not run in parallel with each other or with other collections.
    [CollectionDefinition("DualContouringDiagnostics", DisableParallelization = true)]
    public class DualContouringDiagnosticsCollection { }

    [Collection("DualContouringDiagnostics")]
    public class DualContouringDiagnosticsTests
    {
        class Fn : ImplicitFunction3d
        {
            public Func<Vector3d, double> F;
            public double Value(ref Vector3d p) { return F(p); }
        }

        // a gyroid sheet clipped to a sphere: many cells with several patches, raw intersections at this size
        static double Gyroid(Vector3d p)
        {
            double k = 2 * Math.PI * 1.5;
            double g = Math.Sin(k * p.x) * Math.Cos(k * p.y) + Math.Sin(k * p.y) * Math.Cos(k * p.z) + Math.Sin(k * p.z) * Math.Cos(k * p.x);
            return Math.Max(Math.Abs(g) / k - 0.03, p.Length - 0.8);
        }

        static DualContouring Contour(Func<Vector3d, double> f, int res, double scale = 1)
        {
            double cell = 2.2 / res;
            Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * new Vector3d(0.1371, 0.2713, 0.3891);
            var dc = new DualContouring() {
                Implicit = new Fn() { F = p => f(p / scale) }, InsideIsLess = true, CellSize = cell * scale,
                Bounds = new AxisAlignedBox3d(scale * o, scale * (o + 2.2 * Vector3d.One))
            };
            dc.Generate();
            return dc;
        }

        static T WithDiagnostics<T>(bool on, Func<T> f)
        {
            bool old = DualContouring.CollectDiagnostics;
            DualContouring.CollectDiagnostics = on;
            try { return f(); } finally { DualContouring.CollectDiagnostics = old; }
        }

        static void AssertSameMesh(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            Assert.Equal(a.MaxTriangleID, b.MaxTriangleID);
            for (int v = 0; v < a.MaxVertexID; ++v)
                Assert.Equal(a.GetVertex(v), b.GetVertex(v));
            for (int t = 0; t < a.MaxTriangleID; ++t)
                Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        [Fact]
        public void Diagnostics_DoNotChangeTheResult_DenseMultiPatch()
        {
            DualContouring off = WithDiagnostics(false, () => Contour(Gyroid, 24));
            DualContouring on = WithDiagnostics(true, () => Contour(Gyroid, 24));
            Assert.Null(off.Diagnostics);
            Assert.NotNull(on.Diagnostics);
            // the input exercises what the hook observes: multi-patch polygons and a repair that moves vertices
            Assert.True(on.Diagnostics.MultiPatchPolygons > 0);
            Assert.True(on.SelfIntersectionsFound > 0);
            Assert.True(on.Diagnostics.ResolveMovedVertices > 0);
            AssertSameMesh(off.Mesh, on.Mesh);
            Assert.Equal(off.SelfIntersectionsFound, on.SelfIntersectionsFound);
            Assert.Equal(off.SelfIntersectionsLeft, on.SelfIntersectionsLeft);
            Assert.True(on.SelfIntersectionFree);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(on.Mesh));
        }

        [Fact]
        public void Diagnostics_DoNotChangeTheResult_AdaptiveWindingContour()
        {
            // two overlapping boxes as a soup: the adaptive winding-number path (octree, sparse cells)
            DMesh3 soup = new DMesh3();
            foreach (Vector3d c in new[] { Vector3d.Zero, new Vector3d(0.37, 0.21, 0.13) }) {
                DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(c, new Vector3d(0.5, 0.4, 0.3)) }.Generate().MakeDMesh();
                MeshTransforms.Rotate(box, Vector3d.Zero, Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 23));
                MeshEditor.Append(soup, box);
            }
            Func<DMesh3> run = () => {
                var s = new MeshWindingNumberSolidify(soup) { Adaptive = true, CellSize = soup.CachedBounds.DiagonalLength / 40 };
                Assert.True(s.Compute());
                return s.Result;
            };
            DMesh3 a = WithDiagnostics(false, run), b = WithDiagnostics(true, run);
            AssertSameMesh(a, b);
            Assert.True(b.IsClosed());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(b));
        }

        [Fact]
        public void Diagnostics_CountsAreConsistent_AndScaleInvariant()
        {
            DualContouring dc = WithDiagnostics(true, () => Contour(Gyroid, 24));
            DualContouring big = WithDiagnostics(true, () => Contour(Gyroid, 24, Math.Pow(2, 10)));
            var d = dc.Diagnostics;
            // Wang, Remark 3: with every cell vertex strictly inside its cell, at most two vertices are concave
            Assert.Equal(0, d.VerticesNotStrictlyInCell);
            Assert.Equal(0, d.Concave[3] + d.Concave[4]);
            Assert.True(d.Concave[1] > 0);
            // no intersecting pair has both triangles inside their envelopes, and every pair is counted once
            Assert.Equal(0, d.CoveredPairs);
            Assert.Equal(dc.SelfIntersectionsFound, d.Pairs);
            Assert.Equal(d.Polygons, d.TransitionPolygons + d.MultiPatchPolygons + d.DegeneratePolygons + d.Concave.Sum());
            // 2^10: the same counts (the predicates are exact, the grid scales by a power of two)
            var e = big.Diagnostics;
            Assert.Equal(d.Concave, e.Concave);
            Assert.Equal(d.MultiPatchPolygons, e.MultiPatchPolygons);
            Assert.Equal(d.Pairs, e.Pairs);
            Assert.Equal(d.IntersectingByClass, e.IntersectingByClass);
        }

        // cell vertices around the z axis edge from (0,0,-1) to (0,0,1), counter-clockwise around +z
        static Vector3d[] Square()
        {
            return new[] { new Vector3d(-0.5, -0.5, 0), new Vector3d(0.5, -0.5, 0), new Vector3d(0.5, 0.5, 0), new Vector3d(-0.5, 0.5, 0) };
        }
        static readonly Vector3d EMinus = new Vector3d(0, 0, -1), EPlus = new Vector3d(0, 0, 1);

        [Fact]
        public void ConcaveEnvelopeVertices_ConvexSquare_HasNone()
        {
            int at; bool onPlane;
            Assert.Equal(0, DualContouring.ConcaveEnvelopeVertices(Square(), EMinus, EPlus, out at, out onPlane));
            Assert.False(onPlane);
            Assert.Equal(-1, at);
        }

        [Fact]
        public void ConcaveEnvelopeVertices_VertexPushedTowardTheEdge_IsConcave()
        {
            // v0 moved inward past the line v3-v1 (still in its quadrant): the edge v0-e+ is reflex
            Vector3d[] v = Square();
            v[0] = new Vector3d(-0.1, -0.1, 0.3);
            int at; bool onPlane;
            Assert.Equal(1, DualContouring.ConcaveEnvelopeVertices(v, EMinus, EPlus, out at, out onPlane));
            Assert.Equal(0, at);
            Assert.False(onPlane);
            // exactly on the segment v3-v1 (dyadic coordinates, so exactly coplanar with e+ and e-): counted as concave
            v = Square();
            v[3] = new Vector3d(-0.5, 0.25, 0); v[1] = new Vector3d(0.25, -0.5, 0); v[0] = new Vector3d(-0.125, -0.125, 0);
            Assert.Equal(1, DualContouring.ConcaveEnvelopeVertices(v, EMinus, EPlus, out at, out onPlane));
            Assert.Equal(0, at);
            Assert.True(onPlane);
        }

        [Fact]
        public void ConcaveEnvelopeVertices_TwoAdjacentConcave()
        {
            // v0 high and v1 low near the edge: both reflex (Wang: then an edge vertex is needed)
            Vector3d[] v = Square();
            v[0] = new Vector3d(-0.05, -0.05, 0.6);
            v[1] = new Vector3d(0.05, -0.05, -0.6);
            int at; bool onPlane;
            Assert.Equal(2, DualContouring.ConcaveEnvelopeVertices(v, EMinus, EPlus, out at, out onPlane));
        }
    }
}
