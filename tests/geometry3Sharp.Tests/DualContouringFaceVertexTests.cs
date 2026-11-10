using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Face vertices of DualContouring (option B of docs/design/DC_INTERSECTION_FREE.md): on ambiguous faces between two
    // single-patch cells (where the dual quads left out non-manifold quads, holes) and on faces between cells of
    // different size (refinement transitions), with fans around an edge vertex that stay inside one cell each.
    public class DualContouringFaceVertexTests
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

        // two turned plates 0.03 apart, each 0.05 thick: at 48 cells across the gap is under a cell, and many faces
        // between the plates are ambiguous between two single-patch cells
        static Func<Vector3d, double> Plates(int seed)
        {
            var rnd = new Random(seed);
            Matrix3d R = Matrix3d.AxisAngleD(Vector3d.AxisX, rnd.NextDouble() * 360) * Matrix3d.AxisAngleD(Vector3d.AxisY, rnd.NextDouble() * 360)
                * Matrix3d.AxisAngleD(Vector3d.AxisZ, rnd.NextDouble() * 360);
            return p => {
                Vector3d q = R * p;
                return Math.Min(Box(q - new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025)), Box(q + new Vector3d(0, 0, 0.04), new Vector3d(0.7, 0.6, 0.025)));
            };
        }

        static readonly Vector3d Shift = new Vector3d(0.1371, 0.2713, 0.3891);

        static DualContouring Contour(Func<Vector3d, double> f, int res, Similarity s = null, bool parallel = true)
        {
            s = s ?? Similarity.Identity;
            double cell = 2.2 / res;
            Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + cell * Shift;
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

        static void AssertClosedManifoldClean(DualContouring dc, string what)
        {
            Assert.Equal(0, dc.NonManifoldTriangles);
            Assert.True(dc.Mesh.IsClosed(), what + ": not closed");
            Assert.Empty(MeshIterators.BowtieVertices(dc.Mesh));
            Assert.True(dc.SelfIntersectionFree, what);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(dc.Mesh));
            Assert.True(dc.Mesh.Vertices().All(v => v.IsFinite), what);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(dc.Mesh));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(9)]
        public void AmbiguousFaces_NothingLeftOut_ClosedAndClean(int seed)
        {
            // the dual quads left out hundreds of non-manifold triangles here (open results)
            DualContouring dc = Contour(Plates(seed), 48);
            AssertClosedManifoldClean(dc, "plates " + seed);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        public void CoveredTriangles_HaveNoMultiPatchVertex(int seed)
        {
            // the envelope argument needs one vertex per cell: a fan or quad that touches a cell with several
            // patches is checked, never covered (thin plates and a gyroid sheet: many such cells next to
            // ambiguous faces)
            int faceVertices = 0;
            foreach (DualContouring dc in new[] { Contour(Plates(seed), 48), Contour(Gyroid(seed), 48) }) {
                faceVertices += dc.FaceVertices;
                Assert.Contains(dc.Mesh.VertexIndices(), v => dc.genPatches[v] > 1);
                foreach (int t in dc.coveredTriangles) {
                    Index3i tri = dc.Mesh.GetTriangle(t);
                    for (int j = 0; j < 3; ++j)
                        Assert.True(dc.genPatches[tri[j]] <= 1, $"covered triangle {t} has a vertex of a cell with {dc.genPatches[tri[j]]} patches");
                }
                AssertClosedManifoldClean(dc, "seed " + seed);
            }
            Assert.True(faceVertices > 0);
        }

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

        [Fact]
        public void FaceVertices_LieStrictlyInsideTheirFaces()
        {
            // on exactly one grid plane (the face), off every grid edge and node
            DualContouring dc = Contour(Plates(4), 48);
            double h = 2.2 / 48;
            Vector3d o = new Vector3d(-1.1, -1.1, -1.1) + h * Shift;
            Assert.True(dc.faceVertexIds.Count > 0);
            foreach (int v in dc.faceVertexIds) {
                Vector3d p = dc.Mesh.GetVertex(v);
                int planes = 0;
                for (int k = 0; k < 3; ++k)
                    if (o[k] + h * Math.Round((p[k] - o[k]) / h) == p[k]) planes++;
                Assert.Equal(1, planes);
            }
        }

        [Fact]
        public void RefinementTransitions_AreCovered_SoNothingIsChecked()
        {
            // a sphere (single-patch cells everywhere) with random refined regions: every triangle, those between
            // fine and coarse cells included, lies in its cell's envelope, so the exact check has nothing to start
            // from. Before the face vertices the transition polygons were checked (and could fold)
            Func<Vector3d, double> sphere = q => q.Length - 1;
            DualContouring dc = LocalRefinementTests.ContourMixed(sphere, 0.15, 1.3, 2, LocalRefinementTests.RandomRegion(1, 0.3));
            Assert.Equal(0, dc.CheckedTriangles);
            Assert.Equal(dc.Mesh.TriangleCount, dc.CoveredTriangles);
            AssertClosedManifoldClean(dc, "sphere");
            foreach (int v in dc.Mesh.VertexIndices())
                Assert.InRange(dc.Mesh.GetVertex(v).Length, 0.97, 1.03);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void FaceVertices_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DualContouring d0 = Contour(Plates(4), 48), d1 = Contour(Plates(4), 48, s);
            AssertClosedManifoldClean(d1, s.Name);
            if (s.IsExact) {
                Assert.Equal("", Invariance.Difference(d0.Mesh, Invariance.MappedBack(d1.Mesh, s)));
                Assert.Equal(d0.FaceVertices, d1.FaceVertices);
                Assert.Equal(d0.ClampedFaceVertices, d1.ClampedFaceVertices);
            } else {
                double v0 = Invariance.Volume(d0.Mesh), v1 = Invariance.Volume(Invariance.MappedBack(d1.Mesh, s));
                Assert.True(Math.Abs(v1 - v0) < 0.1 * v0, $"{s.Name}: volume {v1} vs {v0}");
            }
        }

        [Fact]
        public void Generate_Twice_AndParallel_EqualsSerial()
        {
            DualContouring a = Contour(Plates(9), 48, null, true), b = Contour(Plates(9), 48, null, false);
            Assert.Equal("", Invariance.Difference(a.Mesh, b.Mesh));
            Assert.Equal(a.FaceVertices, b.FaceVertices);
            DMesh3 first = new DMesh3(a.Mesh);
            int fv = a.FaceVertices, clamped = a.ClampedFaceVertices, ev = a.EdgeVertices;
            a.Generate();
            Assert.Equal("", Invariance.Difference(first, a.Mesh));
            Assert.Equal(fv, a.FaceVertices);
            Assert.Equal(clamped, a.ClampedFaceVertices);
            Assert.Equal(ev, a.EdgeVertices);
        }
    }
}
