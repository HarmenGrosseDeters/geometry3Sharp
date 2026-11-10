using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Envelope triangulation of DualContouring (option A of docs/design/DC_INTERSECTION_FREE.md): quads of single-patch
    // cells are split as Wang's criterion requires, with an edge vertex where neither diagonal is covered, and only
    // the uncovered triangles go through the exact self-intersection check. Uses the diagnostics hook (a static), so
    // it shares the non-parallel collection with DualContouringDiagnosticsTests.
    [Collection("DualContouringDiagnostics")]
    public class DualContouringEnvelopeTests
    {
        class Fn : ImplicitFunction3d
        {
            public Func<Vector3d, double> F;
            public double Value(ref Vector3d p) { return F(p); }
        }

        static double Box(Vector3d p, Vector3d hs)
        {
            Vector3d q = new Vector3d(Math.Abs(p.x) - hs.x, Math.Abs(p.y) - hs.y, Math.Abs(p.z) - hs.z);
            Vector3d m = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return m.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
        }
        static readonly Matrix3d R1 = Matrix3d.AxisAngleD(Vector3d.AxisX, 21.2) * Matrix3d.AxisAngleD(Vector3d.AxisY, 34.9) * Matrix3d.AxisAngleD(Vector3d.AxisZ, 13.2);
        static readonly Matrix3d R2 = Matrix3d.AxisAngleD(Vector3d.AxisZ, 40.7) * Matrix3d.AxisAngleD(Vector3d.AxisX, 16.6);

        // union of two rotated boxes: sharp convex and concave creases
        static double TwoBoxes(Vector3d p) =>
            Math.Min(Box(R1 * p, new Vector3d(0.6, 0.25, 0.25)), Box(R2 * (p - new Vector3d(0.1, 0.1, 0)), new Vector3d(0.2, 0.6, 0.3)));

        // gyroid sheet clipped to a sphere, turned: thin sheets, many multi-patch cells and two-concave quads
        static Func<Vector3d, double> Gyroid(int seed)
        {
            var rnd = new Random(seed);
            Matrix3d R = Matrix3d.AxisAngleD(Vector3d.AxisX, rnd.NextDouble() * 360) * Matrix3d.AxisAngleD(Vector3d.AxisY, rnd.NextDouble() * 360);
            return p => {
                Vector3d q = R * p;
                double k = 2 * Math.PI * 1.5;
                double g = Math.Sin(k * q.x) * Math.Cos(k * q.y) + Math.Sin(k * q.y) * Math.Cos(k * q.z) + Math.Sin(k * q.z) * Math.Cos(k * q.x);
                return Math.Max(Math.Abs(g) / k - 0.03, q.Length - 0.8);
            };
        }

        static DualContouring Contour(Func<Vector3d, double> f, int res, Similarity s = null, bool parallel = true)
        {
            s = s ?? Similarity.Identity;
            double cell = 2.2 / res;
            Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * new Vector3d(0.1371, 0.2713, 0.3891);
            var box = new AxisAlignedBox3d(s.Apply(o), s.Apply(o));
            for (int c = 0; c < 8; ++c)
                box.Contain(s.Apply(o + 2.2 * new Vector3d(c & 1, (c >> 1) & 1, (c >> 2) & 1)));
            var dc = new DualContouring() {
                Implicit = new Fn() { F = p => f(s.Inverse(p)) }, InsideIsLess = true, CellSize = s.ApplyLength(cell),
                Bounds = box, ParallelCompute = parallel
            };
            dc.Generate();
            return dc;
        }

        static T WithDiagnostics<T>(Func<T> f)
        {
            bool old = DualContouring.CollectDiagnostics;
            DualContouring.CollectDiagnostics = true;
            try { return f(); } finally { DualContouring.CollectDiagnostics = old; }
        }

        static void AssertClean(DualContouring dc, string what)
        {
            Assert.True(dc.SelfIntersectionFree, what);
            Assert.Equal(0, dc.SelfIntersectionsLeft);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(dc.Mesh));
            Assert.True(dc.Mesh.Vertices().All(v => v.IsFinite), what);
            if (dc.NonManifoldTriangles == 0)
                Assert.True(dc.Mesh.IsClosed(), what);
            Assert.Empty(MeshIterators.BowtieVertices(dc.Mesh));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(7)]
        public void EmittedQuadTriangles_StayInsideTheirEnvelopes(int seed)
        {
            // every triangle of a quad of four single-patch cells is one the criterion covers (class 1). The
            // normal-based diagonal of the old code put some against the criterion (class 2), and neither diagonal
            // is covered where two vertices are concave (an edge vertex is needed)
            DualContouring dc = WithDiagnostics(() => Contour(Gyroid(seed), 24));
            int outside = 0;
            foreach (int t in dc.Mesh.TriangleIndices()) {
                Index3i tri = dc.Mesh.GetTriangle(t);
                byte cls;
                if (dc.Diagnostics.TriangleClass.TryGetValue(TriKey(tri.a, tri.b, tri.c), out cls) && cls == 2)
                    outside++;
            }
            Assert.Equal(0, outside);
            Assert.Equal(0, dc.Diagnostics.CoveredPairs);
            AssertClean(dc, "gyroid " + seed);
        }

        static long TriKey(int a, int b, int c)
        {
            int[] v = { a, b, c };
            Array.Sort(v);
            return (long)v[0] | ((long)v[1] << 21) | ((long)v[2] << 42);
        }

        [Fact]
        public void OnlyUncoveredTriangles_AreChecked_AndTheResultIsClean()
        {
            // CAD-like creases: nearly all triangles are covered; the check starts from the rest only
            DualContouring dc = Contour(TwoBoxes, 48);
            Assert.Equal(dc.Mesh.TriangleCount, dc.CoveredTriangles + dc.CheckedTriangles);
            Assert.True(dc.CheckedTriangles < dc.Mesh.TriangleCount / 10, $"{dc.CheckedTriangles} of {dc.Mesh.TriangleCount} checked");
            Assert.True(dc.ForcedDiagonals > 0);
            AssertClean(dc, "two boxes");
            // the thin-sheet case: many uncovered (multi-patch) triangles, intersections there are found and resolved
            DualContouring g = Contour(Gyroid(0), 24);
            Assert.True(g.SelfIntersectionsFound > 0);
            Assert.True(g.EdgeVertices > 0);
            AssertClean(g, "gyroid");
        }

        [Fact]
        public void EdgeVertices_LieOnTheirGridEdges()
        {
            // an edge vertex keeps the node coordinates across its edge exactly (it is on the segment, so the four
            // triangles are inside the envelope) and is strictly between the end nodes
            DualContouring dc = Contour(Gyroid(0), 24);
            Assert.True(dc.EdgeVertices > 0);
            double cell = 2.2 / 24;
            Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * new Vector3d(0.1371, 0.2713, 0.3891);
            int onEdges = 0;
            foreach (Vector3d v in dc.Mesh.Vertices()) {
                int onNode = 0;
                for (int k = 0; k < 3; ++k) {
                    double n = Math.Round((v[k] - o[k]) / cell);
                    if (o[k] + cell * n == v[k]) onNode++;
                }
                Assert.True(onNode <= 2, "a vertex on a grid node");
                if (onNode == 2) onEdges++;
            }
            Assert.True(onEdges >= dc.EdgeVertices, $"{onEdges} vertices on grid edges, {dc.EdgeVertices} edge vertices");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Envelope_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DualContouring d0 = Contour(TwoBoxes, 40), d1 = Contour(TwoBoxes, 40, s);
            AssertClean(d1, s.Name);
            if (s.IsExact) {
                Assert.Equal("", Invariance.Difference(d0.Mesh, Invariance.MappedBack(d1.Mesh, s)));
                Assert.Equal(d0.CoveredTriangles, d1.CoveredTriangles);
                Assert.Equal(d0.ForcedDiagonals, d1.ForcedDiagonals);
                Assert.Equal(d0.EdgeVertices, d1.EdgeVertices);
            } else {
                double v0 = Invariance.Volume(d0.Mesh), v1 = Invariance.Volume(Invariance.MappedBack(d1.Mesh, s));
                Assert.True(Math.Abs(v1 - v0) < 0.02 * v0, $"{s.Name}: volume {v1} vs {v0}");
            }
        }

        [Fact]
        public void Generate_Twice_GivesTheSameResultAndCounts()
        {
            DualContouring dc = Contour(Gyroid(3), 24);
            DMesh3 first = new DMesh3(dc.Mesh);
            int covered = dc.CoveredTriangles, checkedTris = dc.CheckedTriangles, forced = dc.ForcedDiagonals, ev = dc.EdgeVertices;
            dc.Generate();
            Assert.Equal("", Invariance.Difference(first, dc.Mesh));
            Assert.Equal(covered, dc.CoveredTriangles);
            Assert.Equal(checkedTris, dc.CheckedTriangles);
            Assert.Equal(forced, dc.ForcedDiagonals);
            Assert.Equal(ev, dc.EdgeVertices);
        }

        [Fact]
        public void Parallel_EqualsSerial()
        {
            DualContouring a = Contour(Gyroid(7), 24, null, true), b = Contour(Gyroid(7), 24, null, false);
            Assert.Equal("", Invariance.Difference(a.Mesh, b.Mesh));
            Assert.Equal(a.CoveredTriangles, b.CoveredTriangles);
            Assert.Equal(a.SelfIntersectionsFound, b.SelfIntersectionsFound);
        }

        // a cube without its top face (a square hole) and a small closed tetrahedron poking up through the hole's plane
        static DMesh3 OpenCubeWithSpike(bool spike)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            var top = m.TriangleIndices().Where(t => m.GetTriNormal(t).z > 0.9).ToList();
            foreach (int t in top) m.RemoveTriangle(t, true, false);
            if (spike) {
                var tet = new DMesh3();
                int a = tet.AppendVertex(new Vector3d(-0.25, -0.25, 0.5)), b = tet.AppendVertex(new Vector3d(0.25, -0.25, 0.5));
                int c = tet.AppendVertex(new Vector3d(0, 0.25, 0.5)), d = tet.AppendVertex(new Vector3d(0, 0, 1.5));
                tet.AppendTriangle(a, c, b); tet.AppendTriangle(a, b, d); tet.AppendTriangle(b, c, d); tet.AppendTriangle(c, a, d);
                MeshEditor.Append(m, tet);
            }
            return m;
        }

        [Fact]
        public void FillSmallHolesChecked_ReportsAFillThatIntersects()
        {
            // the fill of the square hole crosses the tetrahedron: the check must see it (the unchecked fill made
            // a contour with 63 intersecting pairs pass, docs/design/DC_INTERSECTION_FREE.md 2.1)
            DMesh3 m = OpenCubeWithSpike(true);
            int pairs = WindingContouring.FillSmallHolesChecked(m);
            Assert.True(pairs > 0);
            Assert.Equal(pairs, MeshRepairMetrics.SelfIntersections(m));
            DMesh3 clean = OpenCubeWithSpike(false);
            Assert.Equal(0, WindingContouring.FillSmallHolesChecked(clean));
            Assert.True(clean.IsClosed());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(clean));
        }
    }
}
