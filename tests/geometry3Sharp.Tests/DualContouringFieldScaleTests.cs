using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using g3.CoreMathPort;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The size of a field's values, and of the geometry against the values, is not part of its level set (#1376): DualContouring
    /// with ExactHermite gives the same mesh for c f as for f, for a field of order 1 on large geometry, and for a field whose
    /// gradient at the surface is far below 2.2e-16. Ordinary fields and ExactHermite = false keep their output (pinned).
    /// </summary>
    public class DualContouringFieldScaleTests
    {
        sealed class Field : BoundedImplicitFunction3d
        {
            public Func<Vector3d, double> F; public AxisAlignedBox3d Box;
            public double Value(ref Vector3d p) => F(p);
            public AxisAlignedBox3d Bounds() => Box;
        }

        static readonly AxisAlignedBox3d UnitBox = new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5));
        static readonly AxisAlignedBox3d BigBox = new AxisAlignedBox3d(new Vector3d(-2.5, -2.5, -2.5), new Vector3d(2.5, 2.5, 2.5));
        static double Sdf(Vector3d p) => (p - new Vector3d(0.03, -0.02, 0.05)).Length - 1;
        // -exp(-40 r^2) with the library's own exp (the same bits on every runtime): inside is where it is below -level
        static double Gauss(Vector3d p) => -CoreMath.Exp(-40 * p.LengthSquared);
        static double GaussVolume(double level) { double r = Math.Sqrt(-Math.Log(level) / 40); return 4.0 / 3 * Math.PI * r * r * r; }

        static readonly Dictionary<string, Action<DualContouring>> Configs = new Dictionary<string, Action<DualContouring>> {
            { "default", dc => { } },
            { "Simplify off", dc => dc.Simplify = false },
            { "ExactHermite off", dc => dc.ExactHermite = false },
        };

        static DualContouring Make(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, string config = "default", double iso = 0)
        {
            var dc = new DualContouring { Implicit = new Field { F = f, Box = box }, Bounds = box, CellSize = cell, InsideIsLess = true, IsoValue = iso };
            Configs[config](dc);
            return dc;
        }

        static DMesh3 Contour(Func<Vector3d, double> f, AxisAlignedBox3d box, double cell, string config = "default", double iso = 0)
        {
            DualContouring dc = Make(f, box, cell, config, iso);
            dc.Generate();
            return dc.Mesh;
        }

        static double Pow2(int e) => ExactPoint3.ScaleB(1.0, e);
        static AxisAlignedBox3d Scaled(AxisAlignedBox3d b, double s) => new AxisAlignedBox3d(b.Min * s, b.Max * s);

        static DMesh3 ScaledBack(DMesh3 m, int e)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) {
                Vector3d p = c.GetVertex(v);
                c.SetVertex(v, new Vector3d(ExactPoint3.ScaleB(p.x, -e), ExactPoint3.ScaleB(p.y, -e), ExactPoint3.ScaleB(p.z, -e)));
            }
            return c;
        }

        // the triangle count and an FNV-1a hash over the bits of the vertices and the triangle indices
        static string Digest(DMesh3 m)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long x) { unchecked { for (int i = 0; i < 8; ++i) { h ^= (byte)(x >> (8 * i)); h *= 1099511628211UL; } } }
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); Mix(BitConverter.DoubleToInt64Bits(p.x)); Mix(BitConverter.DoubleToInt64Bits(p.y)); Mix(BitConverter.DoubleToInt64Bits(p.z)); }
            foreach (int t in m.TriangleIndices()) { Index3i i = m.GetTriangle(t); Mix(i.a); Mix(i.b); Mix(i.c); }
            return m.TriangleCount + " " + h.ToString("X16");
        }

        static void AssertSolid(DMesh3 m, string what)
        {
            Assert.True(m.TriangleCount > 0, what + ": empty");
            Assert.True(m.IsClosed(), what + ": not closed");
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.True(Invariance.AllFinite(m), what + ": not finite");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        // ---------------------------------------------------------------- the relation c f = f

        public static IEnumerable<object[]> ValueScales() =>
            from e in new[] { -100, -60, -52, -20, 40, 100 } from c in new[] { "default", "Simplify off" } select new object[] { e, c };

        // fails before the fix for e <= -52: an empty mesh (default) or moved vertices (the Hermite normals were zero)
        [Theory]
        [MemberData(nameof(ValueScales))]
        public void ValuesTimesAPowerOfTwo_SameMeshBitForBit(int e, string config)
        {
            double c = Pow2(e);
            DMesh3 unit = Contour(Sdf, UnitBox, 0.125, config);
            AssertSolid(unit, "unit");
            Assert.Equal("", Invariance.Difference(unit, Contour(p => c * Sdf(p), UnitBox, 0.125, config)));
        }

        // the interpolated crossing and the gradient at it, the fallback of the ExactHermite path (here: a HermiteF that declines
        // every edge): the crossing parameter comes from the two node values, whose difference is far below 2.2e-16 at 2^-60.
        // Fails before the fix (the crossing at the middle of every edge, zero normals).
        [Theory]
        [InlineData(-60)]
        [InlineData(-100)]
        public void InterpolatedFallback_ValuesTimesAPowerOfTwo_SameMeshBitForBit(int e)
        {
            DMesh3 Run(double c)
            {
                DualContouring dc = Make(p => c * Sdf(p), UnitBox, 0.125);
                dc.HermiteF = (Vector3d a, Vector3d b, Vector3d crossing, out Vector3d point, out Vector3d normal) => { point = normal = Vector3d.Zero; return false; };
                dc.Generate();
                return dc.Mesh;
            }
            DMesh3 unit = Run(1);
            AssertSolid(unit, "unit");
            Assert.NotEqual(Digest(Contour(Sdf, UnitBox, 0.125, "Simplify off")), Digest(unit));    // not the bisected crossings
            Assert.Equal("", Invariance.Difference(unit, Run(Pow2(e))));
        }

        public static IEnumerable<object[]> GeometryScales() =>
            from e in new[] { -60, 60, 100, 200 } from c in new[] { "default", "Simplify off" } select new object[] { e, c };

        // a field of order 1 on geometry of size 2^e (its gradient is 2^-e); fails before the fix for e >= 60
        [Theory]
        [MemberData(nameof(GeometryScales))]
        public void GeometryTimesAPowerOfTwo_ValuesOfOrderOne_SameMeshBitForBit(int e, string config)
        {
            double s = Pow2(e);
            DMesh3 unit = Contour(Sdf, UnitBox, 0.125, config);
            DMesh3 scaled = Contour(p => Sdf(p / s), Scaled(UnitBox, s), 0.125 * s, config);
            Assert.Equal("", Invariance.Difference(unit, ScaledBack(scaled, e)));
        }

        // field and geometry together, far above the range where a product of two triangle normals is a normal double; fails before
        // the fix from 2^262 (the simplification rejected valid clusters: 542 triangles in place of 504)
        [Theory]
        [InlineData(-100)]      // edges below 2.2e-16: fails before the fix from 2^-48 down (other quad diagonals)
        [InlineData(-60)]
        [InlineData(-40)]
        [InlineData(200)]
        [InlineData(262)]
        [InlineData(300)]
        [InlineData(400)]
        public void FieldAndGeometryTimesAPowerOfTwo_SameSimplifiedMesh(int e)
        {
            double s = Pow2(e);
            DMesh3 unit = Contour(Sdf, UnitBox, 0.125);
            DMesh3 scaled = Contour(p => s * Sdf(p / s), Scaled(UnitBox, s), 0.125 * s);
            Assert.Equal("", Invariance.Difference(unit, ScaledBack(scaled, e)));
        }

        // a creased shape (the degenerate-split rule of the quad diagonals takes part): a box, field and geometry together.
        // Fails before the fix at 2^-60 (other quad diagonals: the edge vectors were normalized with an absolute threshold).
        [Theory]
        [InlineData(-60, "default")]
        [InlineData(-60, "Simplify off")]
        [InlineData(200, "default")]
        [InlineData(200, "Simplify off")]
        public void Box_FieldAndGeometryTimesAPowerOfTwo_SameMeshBitForBit(int e, string config)
        {
            Func<Vector3d, double> box = p => Math.Max(Math.Abs(p.x) - 0.7, Math.Max(Math.Abs(p.y) - 0.9, Math.Abs(p.z) - 1.1));
            double s = Pow2(e);
            DMesh3 unit = Contour(box, UnitBox, 0.125, config);
            AssertSolid(unit, "unit");
            DMesh3 scaled = Contour(p => s * box(p / s), Scaled(UnitBox, s), 0.125 * s, config);
            Assert.Equal("", Invariance.Difference(unit, ScaledBack(scaled, e)));
        }

        // the field under every similarity, with a value scale of 2^-70 on top (a gradient of 8e-22 at unit scale): the 2^k scales
        // bit for bit, the others clean and of the same volume. Fails before the fix (empty meshes).
        [Fact]
        public void Similarities_OfAFieldWithSmallValues_Pow2BitIdentical_AllClean()
        {
            double c = Pow2(-70);
            DMesh3 Run(Similarity sim)
            {
                var grid = AxisAlignedBox3d.Empty;
                for (int q = 0; q < 8; ++q) grid.Contain(sim.Apply(UnitBox.Corner(q)));
                return Contour(p => c * sim.Scale * Sdf(sim.Inverse(p)), grid, sim.ApplyLength(0.125));
            }
            DMesh3 r0 = Run(Similarity.Identity);
            AssertSolid(r0, "identity");
            double volume = Invariance.Volume(r0);
            Assert.InRange(volume, 4.18, 4.19);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 back = Invariance.MappedBack(Run(sim), sim);
                if (sim.IsExact) Assert.Equal("", Invariance.Difference(r0, back));
                else {
                    AssertSolid(back, sim.Name);
                    Assert.InRange(Invariance.Volume(back), volume * 0.98, volume * 1.02);
                }
            }
        }

        // ---------------------------------------------------------------- a field that is small only near its surface

        // exp(-40 r^2) at the level L is a sphere; the gradient at the surface is 80 r L, far below 2.2e-16. Empty before the fix.
        [Theory]
        [InlineData(1e-20, "default")]
        [InlineData(1e-30, "default")]
        [InlineData(1e-40, "default")]
        [InlineData(1e-20, "Simplify off")]
        [InlineData(1e-30, "Simplify off")]
        [InlineData(1e-40, "Simplify off")]
        public void LevelSetOfAGaussian_FarBelowEpsilon_IsTheSphere(double level, string config)
        {
            DMesh3 m = Contour(Gauss, BigBox, 0.125, config, -level);
            AssertSolid(m, config);
            double analytic = GaussVolume(level), volume = Invariance.Volume(m);
            Assert.InRange(volume, analytic * 0.99, analytic * 1.01);
            // every vertex near the sphere (half a cell)
            double r = Math.Sqrt(-Math.Log(level) / 40);
            foreach (Vector3d v in m.Vertices()) Assert.InRange(v.Length, r - 0.0625, r + 0.0625);
        }

        [Fact]
        public void Generate_Twice_OnOneInstance_SameMesh()
        {
            DualContouring dc = Make(Gauss, BigBox, 0.125, "default", -1e-20);
            dc.Generate();
            DMesh3 first = new DMesh3(dc.Mesh);
            AssertSolid(first, "first");
            dc.Generate();
            Assert.Equal("", Invariance.Difference(first, dc.Mesh));
            // another field in between does not leak into the next run
            DualContouring other = Make(p => Pow2(-80) * Sdf(p), UnitBox, 0.125);
            other.Generate();
            DMesh3 unit = Contour(Sdf, UnitBox, 0.125);
            Assert.Equal("", Invariance.Difference(unit, other.Mesh));
            other.Implicit = new Field { F = Sdf, Box = UnitBox };
            other.Generate();
            Assert.Equal("", Invariance.Difference(unit, other.Mesh));
        }

        [Theory]
        [InlineData("default")]
        [InlineData("Simplify off")]
        public void ParallelEqualsSerial_AndRepeats(string config)
        {
            DMesh3 Run(bool parallel)
            {
                DualContouring dc = Make(Gauss, BigBox, 0.125, config, -1e-30);
                dc.ParallelCompute = parallel;
                dc.Generate();
                return dc.Mesh;
            }
            DMesh3 serial = Run(false);
            AssertSolid(serial, "serial");
            for (int i = 0; i < 3; ++i) Assert.Equal("", Invariance.Difference(serial, Run(true)));
        }

        // ---------------------------------------------------------------- what must not change

        static readonly (string name, Func<Vector3d, double> f, double cell)[] Ordinary = {
            ("sphere", Sdf, 0.125),
            ("1000 x sphere", p => 1000 * Sdf(p), 0.125),
            ("box", p => Math.Max(Math.Abs(p.x) - 0.7, Math.Max(Math.Abs(p.y) - 0.9, Math.Abs(p.z) - 1.1)), 0.125),
            ("torus", p => { double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 0.9; return Math.Sqrt(q * q + p.z * p.z) - 0.35; }, 0.0625),
            ("quartic", p => p.x * p.x * p.x * p.x + p.y * p.y * p.y * p.y + p.z * p.z * p.z * p.z - 1, 0.125),
            ("two spheres", p => Math.Min((p - new Vector3d(0.5, 0, 0)).Length - 0.7, (p + new Vector3d(0.5, 0.1, 0)).Length - 0.6), 0.125),
            ("clipped sphere", p => Math.Max(Sdf(p), -0.25), 0.125),
        };

        // the output of master before the fix (e7210143), from sqrt, products and sums only: the same bits on every runtime
        [Theory]
        [InlineData("sphere", "default", "504 7159768185B9FF6D")]
        [InlineData("sphere", "Simplify off", "2408 ECC4F7ED561218E4")]
        [InlineData("sphere", "ExactHermite off", "2408 CC5446C9FDBD364F")]
        [InlineData("1000 x sphere", "default", "504 F30C0927EC1819C0")]
        [InlineData("1000 x sphere", "Simplify off", "2408 23304848A19D9EA0")]
        [InlineData("1000 x sphere", "ExactHermite off", "2408 58FD20FD593BFB80")]
        [InlineData("box", "default", "12 B8D0D2C8FB93B8F5")]
        [InlineData("box", "Simplify off", "2428 A9475736E0083267")]
        [InlineData("box", "ExactHermite off", "2428 A53DE2C8D781C69B")]
        [InlineData("torus", "default", "2160 4B54C3C060600EEE")]
        [InlineData("torus", "Simplify off", "9040 D32BD18E21097B97")]
        [InlineData("torus", "ExactHermite off", "9040 EC8D54915EBA880C")]
        [InlineData("quartic", "default", "444 575A30944372DB8A")]
        [InlineData("quartic", "Simplify off", "2652 6B24ADD11FB93F45")]
        [InlineData("quartic", "ExactHermite off", "2652 196BFEB6A6D0D70A")]
        [InlineData("two spheres", "default", "396 AE84D73A319F8AED")]
        [InlineData("two spheres", "Simplify off", "1760 8FEBC9E56E54DB6A")]
        [InlineData("two spheres", "ExactHermite off", "1760 8D1AA6D7306DDE9E")]
        [InlineData("clipped sphere", "default", "504 7159768185B9FF6D")]
        [InlineData("clipped sphere", "Simplify off", "2408 ECC4F7ED561218E4")]
        [InlineData("clipped sphere", "ExactHermite off", "2408 CC5446C9FDBD364F")]
        public void OrdinaryFields_KeepTheirOutput(string field, string config, string digest)
        {
            var (_, f, cell) = Ordinary.First(o => o.name == field);
            Assert.Equal(digest, Digest(Contour(f, UnitBox, cell, config)));
        }

        // ExactHermite = false is the output from before 2026-11 and stays it, also where the values are small: the exponential
        // field and the scaled distance field give the bits of master before the fix
        [Theory]
        [InlineData(1e-10, "1356 B969C8788C3F6C73")]
        [InlineData(1e-16, "2220 F8F4C74CC3DBB38A")]
        [InlineData(1e-20, "2796 11844516F5AFEFDC")]
        [InlineData(1e-30, "4188 BCEEDE5EF3C46F06")]
        [InlineData(1e-40, "5580 E286FD1099B569E6")]
        public void ExactHermiteOff_Gaussian_KeepsItsOutput(double level, string digest)
        {
            Assert.Equal(digest, Digest(Contour(Gauss, BigBox, 0.125, "ExactHermite off", -level)));
        }

        [Theory]
        [InlineData(-48, "2408 81FDF8BA19B6EAE8")]
        [InlineData(-60, "2408 46DBAE04F881022A")]
        [InlineData(-100, "2408 46DBAE04F881022A")]
        public void ExactHermiteOff_SmallValues_KeepItsOutput(int e, string digest)
        {
            double c = Pow2(e);
            Assert.Equal(digest, Digest(Contour(p => c * Sdf(p), UnitBox, 0.125, "ExactHermite off")));
        }

        // ... and where the geometry is small: the quad diagonals of ExactHermite = false are chosen as before (edge vectors below
        // 2.2e-16 have no direction there), for the geometry alone and for field and geometry together
        [Theory]
        [InlineData(false, "2408 C88D33E9DDF3246E")]
        [InlineData(true, "2408 C6BE37B01A0A267D")]
        public void ExactHermiteOff_SmallGeometry_KeepsItsOutput(bool scaleValues, string digest)
        {
            double s = Pow2(-60);
            DMesh3 m = Contour(p => (scaleValues ? s : 1) * Sdf(p / s), Scaled(UnitBox, s), 0.125 * s, "ExactHermite off");
            Assert.Equal(digest, Digest(ScaledBack(m, -60)));
        }
    }
}
