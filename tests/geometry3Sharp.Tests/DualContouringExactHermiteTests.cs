using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DualContouring.ExactHermite (default on, with QEFEigenTolerance 0.02): the edge crossing is bisected on Implicit and the
    /// normal is the gradient there, instead of the interpolated crossing and the gradient at it.
    /// </summary>
    public class DualContouringExactHermiteTests
    {
        // box [-0.8, 0.8]^3 intersected with the sphere of radius 1, minus a cylinder of radius 0.3 along z, under the similarity sim
        // (field scaled with it: a power of two scale then changes no bit of the computation, only exponents)
        sealed class Csg : ImplicitFunction3d
        {
            public Similarity Sim = Similarity.Identity;
            public double Value(ref Vector3d pt)
            {
                Vector3d p = Sim.Inverse(pt);
                double bx = Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))) - 0.8;
                double d = Math.Max(bx, p.Length - 1.0);
                d = Math.Max(d, 0.3 - Math.Sqrt(p.x * p.x + p.y * p.y));
                return Sim.Scale * d;
            }
        }

        // a convex polytope, max of planes (rotated cube faces and vertex cuts): creases everywhere, linear between them
        sealed class Polytope : ImplicitFunction3d
        {
            static readonly Vector3d[] N;
            static Polytope()
            {
                Matrix3d r = Matrix3d.AxisAngleD(Vector3d.AxisZ, 23) * Matrix3d.AxisAngleD(Vector3d.AxisX, 11);
                var list = new System.Collections.Generic.List<Vector3d>();
                foreach (Vector3d a in new[] { Vector3d.AxisX, -Vector3d.AxisX, Vector3d.AxisY, -Vector3d.AxisY, Vector3d.AxisZ, -Vector3d.AxisZ })
                    list.Add(r * a);
                for (int i = 0; i < 8; ++i)
                    list.Add(r * new Vector3d((i & 1) == 0 ? 1 : -1, (i & 2) == 0 ? 1 : -1, (i & 4) == 0 ? 1 : -1).Normalized);
                N = list.ToArray();
            }
            public double Value(ref Vector3d p)
            {
                double f = double.MinValue;
                for (int i = 0; i < N.Length; ++i) f = Math.Max(f, N[i].Dot(p) - (i < 6 ? 0.7 : 0.95));
                return f;
            }
        }

        sealed class Sphere : ImplicitFunction3d
        {
            public double Value(ref Vector3d p) { return p.Length - 0.9; }
        }

        const double Cell = 2.2 / 24;   // 24 cells over [-1.1, 1.1]

        static DualContouring Make(Similarity sim, bool parallel, bool exact = true, double? tolerance = null)
        {
            var box = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -1.1) - Cell * new Vector3d(0.37, 0.61, 0.23), new Vector3d(1.1, 1.1, 1.1));
            var grid = AxisAlignedBox3d.Empty;
            for (int q = 0; q < 8; ++q) grid.Contain(sim.Apply(box.Corner(q)));
            var dc = new DualContouring() {
                Implicit = new Csg() { Sim = sim }, InsideIsLess = true, IsoValue = 0, CellSize = sim.ApplyLength(Cell),
                Bounds = grid, ParallelCompute = parallel, ExactHermite = exact
            };
            if (tolerance.HasValue) dc.QEFEigenTolerance = tolerance.Value;
            return dc;
        }

        static DMesh3 Contour(Similarity sim, bool parallel, bool exact = true, double? tolerance = null)
        {
            DualContouring dc = Make(sim, parallel, exact, tolerance);
            dc.Generate();
            Assert.Equal(0, dc.NonManifoldTriangles);
            return dc.Mesh;
        }

        // surface error in cells, mean and max, of the vertices of a and b against the other mesh
        static void Error(DMesh3 a, DMesh3 b, out double mean, out double max)
        {
            var ta = new DMeshAABBTree3(a, true); var tb = new DMeshAABBTree3(b, true);
            double sum = 0, mx = 0; int n = 0;
            foreach (var pair in new[] { Tuple.Create(a, tb), Tuple.Create(b, ta) }) {
                foreach (int v in pair.Item1.VertexIndices()) {
                    double dsq; pair.Item2.FindNearestTriangle(pair.Item1.GetVertex(v), out dsq);
                    double d = Math.Sqrt(dsq) / Cell; sum += d; n++; if (d > mx) mx = d;
                }
            }
            max = mx;
            mean = sum / n;
        }

        static DMesh3 Reference()
        {
            var mc = new MarchingCubes() {
                Implicit = new Csg(), IsoValue = 0, CubeSize = Cell / 4, Bounds = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -1.1), new Vector3d(1.1, 1.1, 1.1)),
                RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 30
            };
            mc.Generate();
            return mc.Mesh;
        }

        static string Digest(DMesh3 m)
        {
            var sb = new StringBuilder();
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';'); }
            foreach (int t in m.TriangleIndices()) sb.Append(m.GetTriangle(t).ToString()).Append(';');
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        [Fact]
        public void Defaults_AreExactHermiteAndLowTolerance()
        {
            var dc = new DualContouring();
            Assert.True(dc.ExactHermite);
            Assert.Equal(0.02, dc.QEFEigenTolerance);
        }

        [Fact]
        public void SharpFeatures_ExactIsMoreAccurateThanInterpolation()
        {
            DMesh3 reference = Reference();
            double meanExact, maxExact, meanOld, maxOld;
            Error(Contour(Similarity.Identity, true), reference, out meanExact, out maxExact);
            Error(Contour(Similarity.Identity, true, false, 0.1), reference, out meanOld, out maxOld);
            // measured: exact max 0.135 mean 0.0064, interpolated max 0.234 mean 0.0115 (cells)
            Assert.True(maxExact < 0.17, $"max {maxExact}");
            Assert.True(meanExact < 0.01, $"mean {meanExact}");
            Assert.True(maxOld > 0.2, $"interpolated max {maxOld}");
            Assert.True(meanOld > 1.5 * meanExact, $"means {meanOld} vs {meanExact}");
        }

        [Fact]
        public void Polytope_CreasesAreSharperThanWithInterpolation()
        {
            Func<bool, DMesh3> run = exact => {
                var dc = new DualContouring() {
                    Implicit = new Polytope(), InsideIsLess = true, CellSize = Cell, Bounds = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -1.1) - Cell * new Vector3d(0.37, 0.61, 0.23), new Vector3d(1.1, 1.1, 1.1)),
                    ExactHermite = exact, QEFEigenTolerance = exact ? 0.02 : 0.1
                };
                dc.Generate();
                return dc.Mesh;
            };
            var mc = new MarchingCubes() {
                Implicit = new Polytope(), IsoValue = 0, CubeSize = Cell / 4, Bounds = new AxisAlignedBox3d(new Vector3d(-1.1, -1.1, -1.1), new Vector3d(1.1, 1.1, 1.1)),
                RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 30
            };
            mc.Generate();
            double meanExact, maxExact, meanOld, maxOld;
            Error(run(true), mc.Mesh, out meanExact, out maxExact);
            Error(run(false), mc.Mesh, out meanOld, out maxOld);
            // measured (cells): exact mean 0.0014 max 0.150; interpolated mean 0.0082 max 0.580. Exact crossings only: 0.0058 / 0.58,
            // exact crossings with the gradient step of a quarter cell: 0.0029 / 0.142
            Assert.True(meanExact < 0.002, $"mean {meanExact}");
            Assert.True(maxExact < 0.2, $"max {maxExact}");
            Assert.True(meanOld > 0.006 && maxOld > 0.4, $"interpolated {meanOld} {maxOld}");
        }

        [Fact]
        public void OptOut_IsTheInterpolationOfBefore()
        {
            // the old tolerance and the old Hermite data, bit for bit (digest taken from the library before ExactHermite)
            DMesh3 old = Contour(Similarity.Identity, false, false, 0.1);
            Assert.Equal(OldDigest, Digest(old));
            // and the same code path as a HermiteF that declines
            DualContouring dc = Make(Similarity.Identity, false, true, 0.1);
            dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) => { point = p; normal = Vector3d.AxisX; return false; };
            dc.Generate();
            Assert.Equal(OldDigest, Digest(dc.Mesh));
            // the exact default differs from it
            Assert.NotEqual(OldDigest, Digest(Contour(Similarity.Identity, false)));
        }

        const string OldDigest = "3F21B0B6F5488C135D228BB1D0AE95F5A3ADD637F207DA3B20B6FA0416D61CBD";

        [Fact]
        public void HermiteF_IsNotAffectedByExactHermite()
        {
            Func<bool, DMesh3> run = exact => {
                DualContouring dc = Make(Similarity.Identity, false, exact, 0.1);
                var f = new Csg();
                dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) => {
                    point = p;
                    double s = Cell / 8;
                    Vector3d[] d = { s * Vector3d.AxisX, s * Vector3d.AxisY, s * Vector3d.AxisZ };
                    Vector3d g = Vector3d.Zero;
                    for (int c = 0; c < 3; ++c) { Vector3d p0 = p + d[c], p1 = p - d[c]; g[c] = f.Value(ref p0) - f.Value(ref p1); }
                    normal = g;
                    return true;
                };
                dc.Generate();
                return dc.Mesh;
            };
            Assert.Equal("", Invariance.Difference(run(true), run(false)));
        }

        [Fact]
        public void Result_IsClosedManifoldFiniteAndClean()
        {
            DMesh3 m = Contour(Similarity.Identity, true);
            Assert.True(m.IsClosed());
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.True(Invariance.AllFinite(m));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        [Fact]
        public void Similarities_Pow2BitIdentical_AllClean()
        {
            DMesh3 r0 = Contour(Similarity.Identity, true);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 r = Contour(sim, true);
                Assert.True(r.IsClosed(), sim.Name);
                Assert.True(Invariance.AllFinite(r), sim.Name);
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(r0, Invariance.MappedBack(r, sim)));
                else
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(Invariance.MappedBack(r, sim)));
            }
        }

        [Fact]
        public void Reuse_GenerateTwice_GivesTheSameMesh()
        {
            DualContouring dc = Make(Similarity.Identity, true);
            dc.Generate();
            DMesh3 first = new DMesh3(dc.Mesh);
            dc.Generate();
            Assert.Equal("", Invariance.Difference(first, dc.Mesh));
        }

        [Fact]
        public void ParallelEqualsSerial_AndRepeats()
        {
            DMesh3 serial = Contour(Similarity.Identity, false);
            for (int rep = 0; rep < 3; ++rep)
                Assert.Equal("", Invariance.Difference(serial, Contour(Similarity.Identity, true)));
        }

        [Fact]
        public void SmoothShape_IsNotWorseThanBefore()
        {
            // on a sphere both are as accurate as a grid of this size allows (measured: 0.006 cells vs 0.004, max 0.026 vs 0.025)
            Func<bool, DMesh3> run = exact => {
                var dc = new DualContouring() {
                    Implicit = new Sphere(), InsideIsLess = true, CellSize = Cell, Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.1),
                    ExactHermite = exact, QEFEigenTolerance = exact ? 0.02 : 0.1
                };
                dc.Generate();
                return dc.Mesh;
            };
            foreach (bool exact in new[] { true, false }) {
                DMesh3 m = run(exact);
                Assert.True(m.IsClosed());
                foreach (int v in m.VertexIndices())
                    Assert.InRange(m.GetVertex(v).Length, 0.9 - 0.05 * Cell, 0.9 + 0.05 * Cell);
            }
            Assert.Equal(run(false).TriangleCount, run(true).TriangleCount);
        }
    }
}
