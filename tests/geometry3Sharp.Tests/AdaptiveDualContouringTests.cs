using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// AdaptiveDualContouring (docs/design/DC_ADAPTIVE_SIMPLIFICATION.md): closed manifold self-intersection free results,
    /// error within the tolerance, fewer triangles than the uniform contour, tolerance 0 equals it, invariance, reuse, determinism.
    /// </summary>
    public class AdaptiveDualContouringTests
    {
        class Fn : ImplicitFunction3d
        {
            readonly Func<Vector3d, double> f;
            public Fn(Func<Vector3d, double> f) { this.f = f; }
            public double Value(ref Vector3d p) { return f(p); }
            public AxisAlignedBox3d Bounds() { return AxisAlignedBox3d.Empty; }
        }

        static double Box(Vector3d p, double he)
        {
            Vector3d q = new Vector3d(Math.Abs(p.x) - he, Math.Abs(p.y) - he, Math.Abs(p.z) - he);
            Vector3d o = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return o.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
        }
        static double Cyl(double a, double b, double r) { return Math.Sqrt(a * a + b * b) - r; }

        // box half extent 1 intersected with a sphere of radius 1.35, minus three axis cylinders of radius 0.55
        static double Csg(Vector3d p)
        {
            double f = Math.Max(Box(p, 1), p.Length - 1.35);
            return Math.Max(f, -Math.Min(Cyl(p.y, p.z, 0.55), Math.Min(Cyl(p.x, p.z, 0.55), Cyl(p.x, p.y, 0.55))));
        }
        static double Sphere(Vector3d p) { return p.Length - 1; }
        // two parallel slabs of thickness 0.3 with a gap of 0.4: several sheets through one block
        static double Slabs(Vector3d p) { return Math.Min(Math.Max(Math.Abs(p.z - 0.35) - 0.15, Math.Max(Math.Abs(p.x), Math.Abs(p.y)) - 1), Math.Max(Math.Abs(p.z + 0.35) - 0.15, Math.Max(Math.Abs(p.x), Math.Abs(p.y)) - 1)); }

        const double Ext = 1.6;
        static readonly Quaterniond Tilt = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 23);
        static double RotatedBox(Vector3d p) { return Box(Tilt.Inverse() * p, 0.9); }

        static AdaptiveDualContouring Make(Func<Vector3d, double> f, int n, double tol, bool parallel = true, double Ext = Ext)
        {
            return new AdaptiveDualContouring() {
                Implicit = new Fn(f), InsideIsLess = true, CellSize = 2 * Ext / n, Tolerance = tol, ParallelCompute = parallel,
                Bounds = new AxisAlignedBox3d(new Vector3d(-Ext, -Ext, -Ext), new Vector3d(Ext, Ext, Ext))
            };
        }

        public static IEnumerable<object[]> Shapes()
        {
            foreach (string s in new[] { "csg", "sphere", "slabs", "rotbox" })
                foreach (double tol in new[] { 0.0, 0.1, 0.5 })
                    yield return new object[] { s, tol };
        }
        static Func<Vector3d, double> ShapeByName(string s) { return s == "csg" ? (Func<Vector3d, double>)Csg : s == "sphere" ? Sphere : s == "slabs" ? Slabs : RotatedBox; }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void Result_IsClosedManifoldFiniteAndSelfIntersectionFree(string shape, double tol)
        {
            var ad = Make(ShapeByName(shape), 40, tol);
            ad.Generate();
            Assert.True(ad.Mesh.TriangleCount > 100);
            Assert.True(Invariance.AllFinite(ad.Mesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ad.Mesh, true));
            Assert.True(ad.SelfIntersectionFree, "base " + ad.BaseSelfIntersectionFree + " nm " + ad.NonManifoldTriangles);
            Assert.True(tol == 0 ? ad.Clusters == 0 : ad.Clusters > 0);
        }

        static double ThinSlab(Vector3d p) { return Math.Max(Math.Abs(p.z) - 0.04, Math.Max(Math.Abs(p.x), Math.Abs(p.y)) - 1); }
        static double Shell(Vector3d p) { return Math.Abs(p.Length - 0.8) - 0.05; }
        static double TwoSpheres(Vector3d p) { return Math.Min((p - new Vector3d(-0.5, 0, 0)).Length - 0.45, (p - new Vector3d(0.5, 0, 0)).Length - 0.45); }
        static double Cone(Vector3d p) { return Math.Max(Math.Sqrt(p.x * p.x + p.y * p.y) * 0.6 + p.z * 0.8 - 0.3, -p.z - 0.8); }

        // walls thinner than a cell and spheres that nearly touch: clusters are refused by the manifold, duplicate-triangle and
        // orientation checks (measured: the refusals of each check happen on these inputs)
        [Theory]
        [InlineData("thinslab", 24, 0.3)]
        [InlineData("thinslab", 40, 0.8)]
        [InlineData("shell", 24, 0.8)]
        [InlineData("shell", 40, 0.8)]
        [InlineData("twospheres", 40, 0.8)]
        [InlineData("cone", 24, 0.8)]
        public void HardInputs_StayClosedManifoldAndOriented(string shape, int n, double tol)
        {
            Func<Vector3d, double> f = shape == "thinslab" ? (Func<Vector3d, double>)ThinSlab : shape == "shell" ? Shell : shape == "cone" ? Cone : TwoSpheres;
            var ad = Make(f, n, tol);
            ad.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ad.Mesh, true));
            Assert.True(ad.Clusters > 0);
            if (shape == "twospheres") {
                // no folded triangle: every normal points along the field gradient (outward) at its centroid
                foreach (int t in ad.Mesh.TriangleIndices()) {
                    Vector3d c = ad.Mesh.GetTriCentroid(t), nrm = ad.Mesh.GetTriNormal(t);
                    double e = 1e-5;
                    Vector3d g = new Vector3d(f(c + new Vector3d(e, 0, 0)) - f(c - new Vector3d(e, 0, 0)), f(c + new Vector3d(0, e, 0)) - f(c - new Vector3d(0, e, 0)), f(c + new Vector3d(0, 0, e)) - f(c - new Vector3d(0, 0, e)));
                    Assert.True(nrm.Dot(g.Normalized) > 0, "triangle " + t + " points against the field gradient");
                }
            }
        }

        [Fact]
        public void TwoSheetsInOneBlock_StayTwoClusters()
        {
            var uniform = Make(Slabs, 32, 0); uniform.Generate();
            var ad = Make(Slabs, 32, 0.5); ad.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ad.Mesh, true));
            Assert.True(ad.Mesh.TriangleCount < uniform.Mesh.TriangleCount / 3);
            // the gap between the slabs is still there: ray along z at (0.3, 0.2) crosses the mesh four times
            var tree = new DMeshAABBTree3(ad.Mesh, true);
            int hits = 0;
            foreach (int t in ad.Mesh.TriangleIndices()) {
                Index3i it = ad.Mesh.GetTriangle(t);
                var tri = new Triangle3d(ad.Mesh.GetVertex(it.a), ad.Mesh.GetVertex(it.b), ad.Mesh.GetVertex(it.c));
                var ray = new Ray3d(new Vector3d(0.3, 0.2, -3), Vector3d.AxisZ);
                IntrRay3Triangle3 q = new IntrRay3Triangle3(ray, tri);
                if (q.Find()) hits++;
            }
            Assert.Equal(4, hits);
        }

        // flat faces: every vertex is within the tolerance of the true surface (the Hermite planes are the faces)
        [Theory]
        [InlineData(0.1)]
        [InlineData(0.5)]
        public void RotatedBox_MaxErrorWithinTolerance(double tol)
        {
            var ad = Make(RotatedBox, 40, tol);
            ad.Generate();
            Assert.True(ad.Clusters > 0);
            double worst = ad.Mesh.Vertices().Max(v => Math.Abs(RotatedBox(v)));
            Assert.True(worst <= tol * ad.CellSize + 1e-9, "max |f| " + worst + " cells " + worst / ad.CellSize);
        }

        [Fact]
        public void Sphere_ErrorGrowsOnlyByTheSagittaOfTheBlock()
        {
            double tol = 0.25;
            var ad = Make(Sphere, 40, tol);
            ad.Generate();
            int maxLevel = ad.ClustersByLevel.Length - 1;
            double block = ad.CellSize * (1 << maxLevel);
            double worst = ad.Mesh.Vertices().Max(v => Math.Abs(Sphere(v)));
            Assert.True(maxLevel >= 2);
            Assert.True(worst <= tol * ad.CellSize + block * block / 8 + 1e-9, "max |f| " + worst + " block " + block);
        }

        [Theory]
        [InlineData("csg", 3.0)]
        [InlineData("sphere", 4.0)]
        [InlineData("rotbox", 4.0)]
        public void FewerTrianglesThanUniform(string shape, double factor)
        {
            var uniform = Make(ShapeByName(shape), 48, 0); uniform.Generate();
            var ad = Make(ShapeByName(shape), 48, 0.25); ad.Generate();
            Assert.True(ad.Mesh.TriangleCount * factor <= uniform.Mesh.TriangleCount, ad.Mesh.TriangleCount + " vs " + uniform.Mesh.TriangleCount);
            Assert.Equal(uniform.Mesh.TriangleCount, ad.UniformTriangles);
        }

        [Fact]
        public void ZeroTolerance_IsTheUniformContour_AndATinyOneOnlyMergesCoplanarVertices()
        {
            var zero = Make(Csg, 32, 0); zero.Generate();
            Assert.Equal(0, zero.Clusters);
            Assert.Equal(zero.UniformTriangles, zero.Mesh.TriangleCount);
            Assert.Equal(zero.UniformVertices, zero.Mesh.VertexCount);
            // a tolerance of 1e-12 cells accepts only clusters whose Hermite planes coincide (flat faces): the result is
            // clean and no farther from the surface than the uniform contour
            var tiny = Make(Csg, 32, 1e-12); tiny.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(tiny.Mesh, true));
            Assert.True(tiny.Mesh.TriangleCount < zero.Mesh.TriangleCount);
            Assert.True(tiny.Mesh.Vertices().Max(v => Math.Abs(Csg(v))) <= zero.Mesh.Vertices().Max(v => Math.Abs(Csg(v))) + 1e-9);
        }

        [Fact]
        public void CheckRepair_TakesBackClustersAndStaysClean()
        {
            // at this tolerance some merges are refused by the topology or intersection check (measured: 104 of ~1600)
            var ad = Make(Csg, 64, 0.1, true, 1.5);
            ad.Generate();
            Assert.True(ad.RejectedClusters > 0);
            Assert.True(ad.CheckRounds > 1);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ad.Mesh, true));
        }

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Invariance_AllSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            var plain = Make(Csg, 32, 0.25); plain.Generate();
            var corners = new List<Vector3d>();
            for (int c = 0; c < 8; ++c) corners.Add(s.Apply(new Vector3d((c & 1) == 0 ? -Ext : Ext, (c & 2) == 0 ? -Ext : Ext, (c & 4) == 0 ? -Ext : Ext)));
            var box = new AxisAlignedBox3d(corners[0]);
            foreach (var c in corners) box.Contain(c);
            double scale = s.Scale;
            var ad = new AdaptiveDualContouring() {
                Implicit = new Fn(p => scale * Csg(s.Inverse(p))), InsideIsLess = true, CellSize = plain.CellSize * scale, Tolerance = 0.25, Bounds = box
            };
            ad.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ad.Mesh, true));
            if (s.IsExact) {
                Assert.Equal("", Invariance.GeometricDifference(plain.Mesh, Invariance.MappedBack(ad.Mesh, s)));
                Assert.Equal(plain.Clusters, ad.Clusters);
            } else {
                // another grid: not the same triangles, but the same quality
                Assert.InRange((double)ad.Mesh.TriangleCount / plain.Mesh.TriangleCount, 0.5, 2.0);
                double worst = Invariance.MappedBack(ad.Mesh, s).Vertices().Max(v => Math.Abs(Csg(v)));
                Assert.True(worst <= 0.6 * plain.CellSize * 4, "max |f| " + worst);
            }
        }
        public static IEnumerable<object[]> Similarities() { return Similarity.Names(); }

        [Fact]
        public void Reuse_GenerateTwice_GivesTheSameMesh()
        {
            var ad = Make(Csg, 32, 0.25);
            ad.Generate();
            DMesh3 first = ad.Mesh;
            ad.Generate();
            Assert.False(ReferenceEquals(first, ad.Mesh));
            Assert.Equal("", Invariance.Difference(first, ad.Mesh));
        }

        [Fact]
        public void ParallelEqualsSerial_AndRepeatedRunsAreIdentical()
        {
            var serial = Make(Csg, 40, 0.25, false); serial.Generate();
            Assert.True(serial.Clusters > 0);
            for (int k = 0; k < 3; ++k) {
                var par = Make(Csg, 40, 0.25, true); par.Generate();
                Assert.Equal("", Invariance.Difference(serial.Mesh, par.Mesh));
                var again = Make(Csg, 40, 0.25, false); again.Generate();
                Assert.Equal("", Invariance.Difference(serial.Mesh, again.Mesh));
            }
        }
    }
}
