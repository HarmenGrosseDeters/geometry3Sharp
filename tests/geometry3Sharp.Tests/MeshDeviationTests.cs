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
            // gaps in the reference's ID space: a second sphere appended first and removed again
            DMesh3 reference = Sphere(0.2, 5), mesh = Sphere(1.1, 9);
            MeshTransforms.Translate(reference, new Vector3d(5, 0, 0));
            int gap = reference.MaxTriangleID;
            new MeshEditor(reference).AppendMesh(Sphere(1, 20));
            foreach (int tid in reference.TriangleIndices().Where(t => t < gap).ToList())
                reference.RemoveTriangle(tid, true, false);
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
                Assert.True(d1.ClosestTriangle[vid] >= gap);
            }
            // the queries ran on a local copy (no tree on the far reference)
            Assert.Null(d1.ReferenceSpatial);
            // certified bound on the local copies: same interval
            var c0 = new MeshDeviation(mesh, reference) { ComputeCertifiedBound = true, CertifiedTolerance = 1e-4 };
            Assert.True(c0.Compute());
            Assert.InRange(c0.HausdorffLowerBound, d0.Statistics.MaxAbsolute - 1e-6, c0.HausdorffUpperBound);
            Assert.Equal(d0.Statistics.MaxAbsolute, d1.Statistics.MaxAbsolute, 6);
        }

        [Fact]
        public void ChangedReference_IsNotServedFromTheCache()
        {
            foreach (Vector3d off in new[] { Vector3d.Zero, new Vector3d(500000, 5000000, 300) }) {
                DMesh3 r1 = Sphere(1, 12), r2 = Sphere(0.5, 12), mesh = Sphere(1.1, 5);
                foreach (DMesh3 m in new[] { r1, r2, mesh }) MeshTransforms.Translate(m, off);
                Vector3d p = off + new Vector3d(2, 0, 0);
                foreach (var sign in new[] { MeshDeviation.Signs.Unsigned, MeshDeviation.Signs.PseudoNormal }) {
                    var d = new MeshDeviation(mesh, r1) { Sign = sign };
                    Assert.True(d.Compute());
                    Vector3d c; int t;
                    Assert.InRange(d.SignedDistance(p, out c, out t), 0.99, 1.01);
                    d.Reference = r2;               // another reference, same edit stamp
                    d.ReferenceSpatial = null;
                    Assert.InRange(d.SignedDistance(p, out c, out t), 1.49, 1.51);
                    d.Reference = r1;
                    d.ReferenceSpatial = new DMeshAABBTree3(r1, true);     // given by the caller
                    Assert.InRange(d.SignedDistance(p, out c, out t), 0.99, 1.01);
                }
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

        // reference plane z = 0 over [-3,3]^2 with the square hole [-1,1]^2, and one triangle in the plane
        // over it: every corner lies on the reference (deviation 0), the hole center is 1 away
        static void HoleCase(out DMesh3 mesh, out DMesh3 reference)
        {
            reference = Grid(-3, -3, 3, 3, 13);
            foreach (int tid in reference.TriangleIndices().ToList()) {
                Vector3d c = reference.GetTriCentroid(tid);
                if (Math.Abs(c.x) < 1 && Math.Abs(c.y) < 1)
                    reference.RemoveTriangle(tid, true, false);
            }
            mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(-2.5, -2.5, 0)), mesh.AppendVertex(new Vector3d(2.5, -2.5, 0)),
                mesh.AppendVertex(new Vector3d(0, 2.5, 0)));
        }

        [Fact]
        public void TriangleBounds_SeeTheHoleBetweenTheVertices()
        {
            DMesh3 mesh, reference;
            HoleCase(out mesh, out reference);
            var dev = new MeshDeviation(mesh, reference) { ComputeTriangleBounds = true, CertifiedTolerance = 1e-3 };
            Assert.True(dev.Compute());
            Assert.Equal(0, dev.Statistics.MaxAbsolute, 12);
            Assert.InRange(dev.TriangleDeviationLower[0], 1 - 1e-3 - 1e-12, 1 + 1e-12);
            Assert.InRange(dev.TriangleDeviationUpper[0], 1 - 1e-12, dev.TriangleDeviationLower[0] + 1e-3 + 1e-12);
            Assert.Equal(0, dev.TriangleBoundsUnconverged);
            // the lower bound is attained at its worst point, inside the hole
            Vector3d w = dev.TriangleWorstPoint[0];
            Vector3d c; int t;
            Assert.Equal(dev.TriangleDeviationLower[0], Math.Abs(dev.SignedDistance(w, out c, out t)), 12);
            Assert.True(Math.Abs(w.x) < 1 && Math.Abs(w.y) < 1 && w.z == 0);
            Assert.Equal(dev.TriangleDeviationUpper[0], dev.TriangleUpperStatistics.MaxAbsolute);
            Assert.Equal(0.0, dev.TriangleUpperStatistics.FractionWithin(0.5));

            // without subdivisions: a wider interval, still valid, and reported
            dev.MaxTriangleSubdivisions = 0;
            Assert.True(dev.Compute());
            Assert.Equal(0, dev.TriangleDeviationLower[0], 12);
            Assert.True(dev.TriangleDeviationUpper[0] >= 1);
            Assert.Equal(1, dev.TriangleBoundsUnconverged);
            double loose = dev.TriangleDeviationUpper[0];
            // ... capped by the certified Hausdorff bound if that is computed too
            dev.ComputeCertifiedBound = true;
            Assert.True(dev.Compute());
            Assert.True(loose > dev.HausdorffUpperBound + 1e-3, "loose " + loose);
            Assert.Equal(dev.HausdorffUpperBound, dev.TriangleDeviationUpper[0]);
            Assert.True(dev.TriangleDeviationUpper[0] >= 1 - 1e-12);
            Assert.Equal(1, dev.TriangleBoundsUnconverged);
        }

        [Fact]
        public void TriangleBounds_CapByHausdorffCanConverge()
        {
            // a corner at the hole center: the largest distance (1) is at that corner, so the Hausdorff
            // interval caps the loose bound without subdivisions to within the tolerance
            DMesh3 mesh, reference;
            HoleCase(out mesh, out reference);
            mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(0, 0, 0)), mesh.AppendVertex(new Vector3d(2.5, -2.5, 0)),
                mesh.AppendVertex(new Vector3d(2.5, 2.5, 0)));
            var dev = new MeshDeviation(mesh, reference) { ComputeTriangleBounds = true, CertifiedTolerance = 1e-3, MaxTriangleSubdivisions = 0 };
            Assert.True(dev.Compute());
            Assert.Equal(1, dev.TriangleDeviationLower[0], 12);
            Assert.True(dev.TriangleDeviationUpper[0] > 1 + 1e-3);
            Assert.Equal(1, dev.TriangleBoundsUnconverged);
            dev.ComputeCertifiedBound = true;
            Assert.True(dev.Compute());
            Assert.InRange(dev.TriangleDeviationUpper[0], 1 - 1e-12, 1 + 1e-3 + 1e-12);
            Assert.Equal(0, dev.TriangleBoundsUnconverged);
        }

        // a coarse sphere against a finer, larger and shifted one: corners of different nearest triangles
        static void SphereCase(Vector3d off, double scale, out DMesh3 mesh, out DMesh3 reference)
        {
            mesh = Sphere(1, 5);
            reference = Sphere(1.03, 14);
            MeshTransforms.Translate(reference, new Vector3d(0.04, -0.02, 0.01));
            foreach (DMesh3 m in new[] { mesh, reference }) {
                MeshTransforms.Scale(m, scale);
                MeshTransforms.Translate(m, off);
            }
        }

        [Fact]
        public void TriangleBounds_ContainSampledDistances()
        {
            DMesh3 mesh, reference;
            SphereCase(Vector3d.Zero, 1, out mesh, out reference);
            double tol = 1e-4;
            var dev = new MeshDeviation(mesh, reference) { ComputeTriangleBounds = true, CertifiedTolerance = tol };
            Assert.True(dev.Compute());
            Assert.Equal(0, dev.TriangleBoundsUnconverged);
            var tree = new DMeshAABBTree3(reference, true);
            int inside = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                double lo = dev.TriangleDeviationLower[tid], hi = dev.TriangleDeviationUpper[tid];
                Index3i tv = mesh.GetTriangle(tid);
                Vector3d a = mesh.GetVertex(tv.a), b = mesh.GetVertex(tv.b), c = mesh.GetVertex(tv.c);
                double corners = Math.Max(Math.Abs(dev.VertexDeviation[tv.a]), Math.Max(Math.Abs(dev.VertexDeviation[tv.b]), Math.Abs(dev.VertexDeviation[tv.c])));
                Assert.True(lo >= corners);
                Assert.True(hi - lo <= tol + 1e-15);
                // every sample of the triangle is within the upper bound
                const int n = 12;
                double sampled = 0;
                for (int i = 0; i <= n; ++i)
                    for (int j = 0; i + j <= n; ++j) {
                        Vector3d p = a + ((double)i / n) * (b - a) + ((double)j / n) * (c - a);
                        double d = Math.Sqrt(MeshQueries.TriDistanceSqr(reference, tree.FindNearestTriangle(p), p));
                        Assert.True(d <= hi + 1e-12, "sample " + d + " above " + hi);
                        sampled = Math.Max(sampled, d);
                    }
                Assert.True(sampled <= lo + tol + 1e-12);
                if (lo > corners + 1e-9) inside++;
            }
            // the case has interior maxima (else it would not test the subdivision)
            Assert.True(inside > 10, "inside " + inside);

            // the per triangle intervals and the certified Hausdorff interval overlap
            var h = new MeshDeviation(mesh, reference) { ComputeCertifiedBound = true, CertifiedTolerance = tol };
            Assert.True(h.Compute());
            double maxLo = mesh.TriangleIndices().Max(t => dev.TriangleDeviationLower[t]);
            double maxHi = mesh.TriangleIndices().Max(t => dev.TriangleDeviationUpper[t]);
            Assert.True(maxLo <= h.HausdorffUpperBound + 1e-12 && h.HausdorffLowerBound <= maxHi + 1e-12);
        }

        [Fact]
        public void TriangleBounds_DeterministicAndIndependentOfUnitAndOrigin()
        {
            DMesh3 m0, r0;
            SphereCase(Vector3d.Zero, 1, out m0, out r0);
            var d0 = new MeshDeviation(m0, r0) { ComputeTriangleBounds = true };
            Assert.True(d0.Compute());
            double[] lo0 = d0.TriangleDeviationLower, hi0 = d0.TriangleDeviationUpper;
            // the same run again, on a new tree: bitwise the same (each triangle is refined on its own)
            var d0b = new MeshDeviation(m0, r0) { ComputeTriangleBounds = true };
            Assert.True(d0b.Compute());
            Assert.Equal(lo0, d0b.TriangleDeviationLower);
            Assert.Equal(hi0, d0b.TriangleDeviationUpper);

            // default tolerance 1e-4 of the diagonal, in meters and far from the origin (geo coordinates)
            foreach (var c in new[] { new { off = Vector3d.Zero, scale = 0.001 }, new { off = new Vector3d(500000, 5000000, 300), scale = 1.0 } }) {
                DMesh3 m1, r1;
                SphereCase(c.off, c.scale, out m1, out r1);
                var d1 = new MeshDeviation(m1, r1) { ComputeTriangleBounds = true };
                Assert.True(d1.Compute());
                Assert.Equal(d0.TriangleBoundsUnconverged, d1.TriangleBoundsUnconverged);
                AxisAlignedBox3d box = m1.CachedBounds; box.Contain(r1.CachedBounds);
                AxisAlignedBox3d box0 = m0.CachedBounds; box0.Contain(r0.CachedBounds);
                double tol = 1e-4 * box0.DiagonalLength;
                Assert.Equal(1e-4 * box.DiagonalLength / c.scale, tol, 9);
                foreach (int tid in m0.TriangleIndices()) {
                    double lo1 = d1.TriangleDeviationLower[tid] / c.scale, hi1 = d1.TriangleDeviationUpper[tid] / c.scale;
                    // both intervals have width <= tol and contain the same exact value
                    Assert.True(hi1 - lo1 <= tol * (1 + 1e-9));
                    Assert.True(lo1 <= hi0[tid] + 1e-7 && lo0[tid] <= hi1 + 1e-7, tid + ": " + lo1 + " " + hi1 + " vs " + lo0[tid] + " " + hi0[tid]);
                    // the worst point is given in the coordinates of Mesh
                    Vector3d cp; int ct;
                    double dw = Math.Abs(d1.SignedDistance(d1.TriangleWorstPoint[tid], out cp, out ct));
                    Assert.True(Math.Abs(dw - d1.TriangleDeviationLower[tid]) <= 1e-6 * c.scale, tid + ": worst point " + dw);
                }
            }
        }

        [Fact]
        public void TriangleBounds_GapsInTheMeshAndGivenTree()
        {
            DMesh3 m0, r0;
            SphereCase(Vector3d.Zero, 1, out m0, out r0);
            var d0 = new MeshDeviation(m0, r0) { ComputeTriangleBounds = true };
            Assert.True(d0.Compute());
            // the same mesh with gaps in its IDs (a second copy appended first and removed), far from the
            // origin, against a tree given by the caller
            Vector3d off = new Vector3d(500000, 5000000, 300);
            DMesh3 m1 = new DMesh3();
            var ed = new MeshEditor(m1);
            ed.AppendMesh(m0);
            int gap = m1.MaxTriangleID;
            ed.AppendMesh(m0);
            foreach (int tid in m1.TriangleIndices().Where(t => t < gap).ToList())
                m1.RemoveTriangle(tid, true, false);
            DMesh3 r1 = new DMesh3(r0);
            MeshTransforms.Translate(m1, off);
            MeshTransforms.Translate(r1, off);
            var d1 = new MeshDeviation(m1, r1) { ComputeTriangleBounds = true, ReferenceSpatial = new DMeshAABBTree3(r1, true) };
            Assert.True(d1.Compute());
            Assert.Equal(0, d1.TriangleBoundsUnconverged);
            for (int tid = 0; tid < gap; ++tid)
                Assert.True(double.IsNaN(d1.TriangleDeviationLower[tid]) && double.IsNaN(d1.TriangleDeviationUpper[tid]));
            // the appended copy keeps the order of the triangles: tid + gap is triangle tid of m0. Far from the
            // origin without a local copy the rounding is about 1e-9
            AxisAlignedBox3d box = m1.CachedBounds; box.Contain(r1.CachedBounds);
            double tol = 1e-4 * box.DiagonalLength;
            foreach (int tid in m0.TriangleIndices()) {
                double lo = d1.TriangleDeviationLower[tid + gap], hi = d1.TriangleDeviationUpper[tid + gap];
                // the default tolerance is relative to the size of the meshes, also on far coordinates
                Assert.True(hi - lo <= tol * (1 + 1e-6), tid + ": width " + (hi - lo));
                Assert.True(lo <= d0.TriangleDeviationUpper[tid] + 1e-6 && d0.TriangleDeviationLower[tid] <= hi + 1e-6, tid + ": " + lo + " " + hi);
            }
        }

        [Fact]
        public void TriangleDeviationBounds_MaxDistance_Inputs()
        {
            DMesh3 mesh, reference;
            HoleCase(out mesh, out reference);
            var bounds = new TriangleDeviationBounds(reference);
            double lo, hi; Vector3d w;
            Assert.Throws<ArgumentException>(() => bounds.MaxDistance(new DMeshAABBTree3(mesh, true), Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY,
                0, 0, 0, 0, 0, 0, 1e-3, 10, out lo, out hi, out w));
            var tree = new DMeshAABBTree3(reference, true);
            Assert.False(bounds.MaxDistance(tree, new Vector3d(double.NaN, 0, 0), Vector3d.AxisX, Vector3d.AxisY, 0, 0, 0, 0, 0, 0, 1e-3, 10, out lo, out hi, out w));
            Assert.True(double.IsNaN(lo) && double.IsNaN(hi));
            // a triangle on the reference: all corners on one triangle, the interval is [0, 0] at once
            Index3i t = reference.GetTriangle(0);
            Assert.True(bounds.MaxDistance(tree, reference.GetVertex(t.a), reference.GetVertex(t.b), reference.GetVertex(t.c),
                0, 0, 0, 0, 0, 0, 0, 0, out lo, out hi, out w));
            Assert.Equal(0, lo); Assert.Equal(0, hi);
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
