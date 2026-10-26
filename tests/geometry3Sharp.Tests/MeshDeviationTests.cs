using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Deviation maps (signed distance per vertex, statistics) between meshes with known offsets.
    /// </summary>
    public class MeshDeviationTests
    {
        // grid over [x0,x1] x [y0,y1] with n x n vertices, z = h(x,y)
        static DMesh3 Grid(double x0, double y0, double x1, double y1, int n, Func<double, double, double> h = null)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = x0 + (x1 - x0) * i / (n - 1), y = y0 + (y1 - y0) * j / (n - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, h == null ? 0 : h(x, y)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        static DMesh3 Sphere(double r, int n)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        }

        [Fact]
        public void HeightField_OverPlane_SignedDeviationIsHeight()
        {
            // measured surface z = 0.2 sin(x) cos(y) over the reference plane z = 0 (normal +z); the
            // reference is larger, so every closest point is the foot point
            Func<double, double, double> h = (x, y) => 0.2 * Math.Sin(x) * Math.Cos(y);
            DMesh3 mesh = Grid(-3, -3, 3, 3, 31, h);
            DMesh3 reference = Grid(-5, -5, 5, 5, 11);
            var dev = new MeshDeviation(mesh, reference);
            Assert.True(dev.Compute());
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                Assert.Equal(p.z, dev.VertexDeviation[vid], 10);
                Assert.True(dev.ClosestPoint[vid].Distance(new Vector3d(p.x, p.y, 0)) < 1e-12);
            }
            Assert.Equal(-dev.Statistics.Min, dev.Statistics.Max, 10);
            Assert.Equal(0, dev.Statistics.Mean, 3);
            Assert.True(dev.Statistics.MaxAbsolute <= 0.2 + 1e-12);
            Assert.True(dev.Statistics.MaxAbsolute > 0.19);

            // unsigned
            dev.Sign = MeshDeviation.Signs.Unsigned;
            Assert.True(dev.Compute());
            foreach (int vid in mesh.VertexIndices())
                Assert.Equal(Math.Abs(mesh.GetVertex(vid).z), dev.VertexDeviation[vid], 10);
        }

        [Fact]
        public void OffsetSpheres_DeviationIsRadiusDifference()
        {
            // vertices of spheres of radius 0.9 and 1.1 against the reference sphere 1: -0.1 inside, +0.1
            // outside, up to the chord error of the reference (its triangles lie inside the unit sphere)
            DMesh3 reference = Sphere(1, 40);
            double sag = 0;
            foreach (int tid in reference.TriangleIndices())
                sag = Math.Max(sag, 1 - Math.Sqrt(MeshQueries.TriangleDistance(reference, tid, Vector3d.Zero).DistanceSquared));
            foreach (var sign in new[] { MeshDeviation.Signs.PseudoNormal, MeshDeviation.Signs.WindingNumber }) {
                foreach (double r in new[] { 0.9, 1.1 }) {
                    DMesh3 mesh = Sphere(r, 13);
                    var dev = new MeshDeviation(mesh, reference) { Sign = sign };
                    Assert.True(dev.Compute());
                    double expected = r - 1;
                    foreach (int vid in mesh.VertexIndices())
                        Assert.InRange(dev.VertexDeviation[vid], expected - 1e-9, expected + sag + 1e-9);
                    Assert.InRange(dev.Statistics.Mean, expected, expected + sag);
                    Assert.True(dev.Statistics.StdDev < sag);
                    Assert.Equal(1.0, dev.Statistics.FractionWithin(Math.Abs(expected) + sag + 1e-9));
                    Assert.Equal(0.0, dev.Statistics.FractionWithin(Math.Abs(expected) - sag - 1e-9));
                }
            }
        }

        [Fact]
        public void PseudoNormal_SignsAtEdgesAndVertices()
        {
            // points of a cube offset outwards by 0.1 in every direction (including the corner and edge
            // regions, whose closest features are vertices and edges of the reference cube)
            DMesh3 cube = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = 3 }.Generate().MakeDMesh();
            DMesh3 outer = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, 1.1 * Vector3d.One), EdgeVertices = 3 }.Generate().MakeDMesh();
            DMesh3 inner = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, 0.9 * Vector3d.One), EdgeVertices = 3 }.Generate().MakeDMesh();
            var dOut = new MeshDeviation(outer, cube);
            var dIn = new MeshDeviation(inner, cube);
            Assert.True(dOut.Compute() && dIn.Compute());
            foreach (int vid in outer.VertexIndices()) {
                Vector3d p = outer.GetVertex(vid);
                // distance from the cube [-1,1]^3 of a point outside it
                Vector3d q = new Vector3d(Math.Max(Math.Abs(p.x) - 1, 0), Math.Max(Math.Abs(p.y) - 1, 0), Math.Max(Math.Abs(p.z) - 1, 0));
                Assert.Equal(q.Length, dOut.VertexDeviation[vid], 12);
            }
            foreach (int vid in inner.VertexIndices())
                Assert.Equal(-0.1, dIn.VertexDeviation[vid], 12);
        }

        [Fact]
        public void InsideOutReference_SignsAsIfOriented()
        {
            DMesh3 reference = Sphere(1, 20);
            reference.ReverseOrientation();
            foreach (var sign in new[] { MeshDeviation.Signs.PseudoNormal, MeshDeviation.Signs.WindingNumber }) {
                foreach (double r in new[] { 0.9, 1.1 }) {
                    var dev = new MeshDeviation(Sphere(r, 9), reference) { Sign = sign };
                    Assert.True(dev.Compute());
                    Assert.True(Math.Sign(dev.Statistics.Min) == Math.Sign(r - 1) && Math.Sign(dev.Statistics.Max) == Math.Sign(r - 1), sign + " " + r);
                }
            }
            // without the correction the pseudo-normal follows the stored orientation
            var raw = new MeshDeviation(Sphere(1.1, 9), reference) { OrientInvertedParts = false };
            Assert.True(raw.Compute());
            Assert.True(raw.Statistics.Max < 0);
        }

        [Fact]
        public void PseudoNormalSign_DoesNotDependOnTheUnit()
        {
            // cube offset outwards (edge and corner regions use edge and vertex pseudo-normals), in a tiny unit
            const double s = 1e-5;
            DMesh3 cube = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = 8 }.Generate().MakeDMesh();
            DMesh3 outer = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, 1.1 * Vector3d.One), EdgeVertices = 3 }.Generate().MakeDMesh();
            MeshTransforms.Scale(cube, s);
            MeshTransforms.Scale(outer, s);
            var dev = new MeshDeviation(outer, cube);
            Assert.True(dev.Compute());
            Assert.True(dev.Statistics.Min > 0, "min " + dev.Statistics.Min);
            Assert.Equal(0.1 * s, dev.Statistics.Min, 12);
        }

        [Fact]
        public void OpenReference_IsNeverFlipped()
        {
            // open terrain (normals +z), mostly flat with one tall peak: its signed volume about the bounds
            // center is negative, which says nothing about the orientation of an open sheet. Points above
            // the flat part must stay positive (OrientInvertedParts is on by default).
            Func<double, double, double> h = (x, y) => 8 * Math.Exp(-(x * x + y * y));
            DMesh3 reference = Grid(-10, -10, 10, 10, 41, h);
            Vector3d c = reference.CachedBounds.Center;
            double vol = 0;
            foreach (int tid in reference.TriangleIndices()) {
                Index3i t = reference.GetTriangle(tid);
                vol += (reference.GetVertex(t.a) - c).Dot((reference.GetVertex(t.b) - c).Cross(reference.GetVertex(t.c) - c));
            }
            Assert.True(vol < 0);
            DMesh3 mesh = Grid(-9, -9, -5, -5, 5, (x, y) => h(x, y) + 0.5);
            var dev = new MeshDeviation(mesh, reference);
            Assert.True(dev.Compute());
            Assert.True(dev.Statistics.Min > 0.49, "min " + dev.Statistics.Min);
        }

        [Fact]
        public void FarFromOrigin_SameAsAtOrigin()
        {
            Vector3d off = new Vector3d(500000, 5000000, 300);
            DMesh3 reference = Sphere(1, 20), mesh = Sphere(1.1, 9);
            var d0 = new MeshDeviation(mesh, reference);
            Assert.True(d0.Compute());
            MeshTransforms.Translate(reference, off);
            MeshTransforms.Translate(mesh, off);
            var d1 = new MeshDeviation(mesh, reference);
            Assert.True(d1.Compute());
            foreach (int vid in mesh.VertexIndices()) {
                Assert.Equal(d0.VertexDeviation[vid], d1.VertexDeviation[vid], 6);
                // (the closest triangle may differ where two are equally close, eg at an edge)
                Assert.True((d0.ClosestPoint[vid] + off).Distance(d1.ClosestPoint[vid]) < 1e-6);
            }
        }

        [Fact]
        public void CertifiedBound_ContainsVertexMaximum()
        {
            DMesh3 reference = Sphere(1, 12);
            DMesh3 mesh = Sphere(1.05, 7);
            var dev = new MeshDeviation(mesh, reference) { ComputeCertifiedBound = true, CertifiedTolerance = 1e-4 };
            Assert.True(dev.Compute());
            Assert.True(dev.HausdorffLowerBound >= dev.Statistics.MaxAbsolute - 1e-12);
            Assert.True(dev.HausdorffUpperBound >= dev.HausdorffLowerBound);
            Assert.True(dev.HausdorffUpperBound - dev.HausdorffLowerBound <= 1e-4 + 1e-12);
        }

        [Fact]
        public void Statistics_WeightedPercentilesAndHistogram()
        {
            var s = DeviationStatistics.Compute(new double[] { -2, -1, 0, 1, 2, double.NaN }, new double[] { 1, 1, 1, 1, 4, 1 });
            Assert.Equal(5, s.Count);
            Assert.Equal(8, s.TotalWeight);
            Assert.Equal(-2, s.Min);
            Assert.Equal(2, s.Max);
            Assert.Equal((-2 - 1 + 0 + 1 + 8) / 8.0, s.Mean, 12);
            Assert.Equal(Math.Sqrt((4 + 1 + 0 + 1 + 16) / 8.0), s.RMS, 12);
            Assert.Equal(1, s.Median);
            Assert.Equal(-1, s.Quantile(0.25));
            Assert.Equal(2, s.AbsolutePercentile(0.95));
            Assert.Equal(3 / 8.0, s.FractionWithin(1));
            double[] hist = s.Histogram(4, -2, 2);
            Assert.Equal(new double[] { 1, 1, 1, 5 }, hist);
        }
    }
}
