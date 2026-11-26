using System;
using System.Security.Cryptography;
using System.Threading;
using g3;
using g3.CoreMathPort;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1577: TpmsLattice.Evaluate uses the correctly rounded CoreMath sin / cos (the same bits on every runtime, WebAssembly included). The grid
    /// nodes take their sines and cosines from per-axis tables; that is only a speed-up: the result must be the bits of the direct CoreMath path.
    /// </summary>
    public class TpmsAxisTableTests
    {
        // the direct reference: the plain CoreMath drop-in of the original expression, written out here independently of EvaluateTrig
        static double Reference(TpmsType type, double px, double py, double pz)
        {
            double sx = CoreMath.Sin(px), cx = CoreMath.Cos(px), sy = CoreMath.Sin(py), cy = CoreMath.Cos(py), sz = CoreMath.Sin(pz), cz = CoreMath.Cos(pz);
            switch (type) {
                case TpmsType.Gyroid: return sx * cy + sy * cz + sz * cx;
                case TpmsType.SchwarzP: return cx + cy + cz;
                case TpmsType.SchwarzD: return sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
                case TpmsType.Neovius: return 3 * (cx + cy + cz) + 4 * cx * cy * cz;
                case TpmsType.IWP: return 2 * (cx * cy + cy * cz + cz * cx) - (CoreMath.Cos(2 * px) + CoreMath.Cos(2 * py) + CoreMath.Cos(2 * pz));
                default:
                    return 0.5 * (CoreMath.Sin(2 * px) * cy * sz + CoreMath.Sin(2 * py) * cz * sx + CoreMath.Sin(2 * pz) * cx * sy)
                         - 0.5 * (CoreMath.Cos(2 * px) * CoreMath.Cos(2 * py) + CoreMath.Cos(2 * py) * CoreMath.Cos(2 * pz) + CoreMath.Cos(2 * pz) * CoreMath.Cos(2 * px))
                         + 0.15;
            }
        }

        static long Bits(double d) { return BitConverter.DoubleToInt64Bits(d); }

        static string Hash(DMesh3 mesh)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                bytes.AddRange(BitConverter.GetBytes(vid));
                for (int k = 0; k < 3; ++k) bytes.AddRange(BitConverter.GetBytes(v[k]));
            }
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                bytes.AddRange(BitConverter.GetBytes(t.a)); bytes.AddRange(BitConverter.GetBytes(t.b)); bytes.AddRange(BitConverter.GetBytes(t.c));
            }
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "");
        }

        [Theory]
        [InlineData(TpmsType.Gyroid)]
        [InlineData(TpmsType.SchwarzP)]
        [InlineData(TpmsType.SchwarzD)]
        [InlineData(TpmsType.Neovius)]
        [InlineData(TpmsType.IWP)]
        [InlineData(TpmsType.Lidinoid)]
        public void Evaluate_IsTheCoreMathExpressionBitForBit(TpmsType type)
        {
            var rnd = new Random(1577);
            for (int i = 0; i < 2000; ++i) {
                double px = (rnd.NextDouble() - 0.5) * 200, py = (rnd.NextDouble() - 0.5) * 200, pz = (rnd.NextDouble() - 0.5) * 200;
                Assert.Equal(Bits(Reference(type, px, py, pz)), Bits(TpmsLattice.Evaluate(type, px, py, pz)));
            }
            // multiples of pi / 2, where the platform sin / cos used to differ in the last bit
            for (int a = -8; a <= 8; ++a)
                Assert.Equal(Bits(Reference(type, a * Math.PI / 2, 1 - a * 0.125, 3)), Bits(TpmsLattice.Evaluate(type, a * Math.PI / 2, 1 - a * 0.125, 3)));
        }

        static Func<TpmsLattice> Case(string name)
        {
            switch (name) {
                case "sheet": return () => new TpmsLattice { Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1) };
                case "skeletal": return () => new TpmsLattice { Type = TpmsType.SchwarzD, Style = TpmsStyle.SkeletalHigh, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1) };
                case "iwp": return () => new TpmsLattice { Type = TpmsType.IWP, Style = TpmsStyle.SkeletalLow, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1) };
                case "lidinoid": return () => new TpmsLattice { Type = TpmsType.Lidinoid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1), VolumeFraction = 0.4 };
                case "hybrid": return () => new TpmsLattice { Type = TpmsType.Gyroid, SecondType = TpmsType.IWP, Blend = TpmsLattice.SigmoidBlend(0, 0.5, 0.25), Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1) };
                case "twolevel": return () => new TpmsLattice { Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1), WallThickness = 0.08, TwoLevelSheet = true };
                case "enclosure": return () => new TpmsLattice { Type = TpmsType.SchwarzP, Style = TpmsStyle.SkeletalLow, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1), Enclosure = true };
                case "rotated": return () => new TpmsLattice { Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1),
                    Origin = new Vector3d(0.3, -1.2, 5), Rotation = new Quaterniond(Vector3d.AxisZ, 37.0).ToRotationMatrix() };
                case "graded": return () => new TpmsLattice { Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1),
                    Grade = TpmsLattice.LinearGrade(2, 0, 1, 2, 2, 2.0) };
                case "fractionAt": return () => new TpmsLattice { Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(Vector3d.Zero, 1),
                    VolumeFractionAt = p => 0.2 + 0.1 * (p.x + 1) / 2 };
                default: throw new ArgumentException(name);
            }
        }

        [Theory]
        [InlineData("sheet")]
        [InlineData("skeletal")]
        [InlineData("iwp")]
        [InlineData("lidinoid")]
        [InlineData("hybrid")]
        [InlineData("twolevel")]
        [InlineData("enclosure")]
        [InlineData("rotated")]
        [InlineData("graded")]
        [InlineData("fractionAt")]
        public void NodeTables_GiveTheDirectCoreMathMeshBitForBit(string name)
        {
            TpmsLattice tabled = Case(name)();
            tabled.Compute();
            TpmsLattice direct = Case(name)();
            TpmsLattice.DisableAxisTables = true;
            try { direct.Compute(); } finally { TpmsLattice.DisableAxisTables = false; }
            Assert.Equal(direct.Status, tabled.Status);
            Assert.True(tabled.Status == TpmsStatus.Ok, name + ": " + tabled.Status + ": " + tabled.Message);
            Assert.Equal(Hash(direct.Mesh), Hash(tabled.Mesh));
            Assert.Equal(Bits(direct.RealizedVolumeFraction), Bits(tabled.RealizedVolumeFraction));
            Assert.Equal(Bits(direct.Level), Bits(tabled.Level));
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(tabled.Mesh));
        }

        [Fact]
        public void NodeTables_ParallelEqualsSerial_AndReusable()
        {
            var par = Case("sheet")(); par.ParallelCompute = true; par.Compute();
            var ser = Case("sheet")(); ser.ParallelCompute = false; ser.Compute();
            Assert.Equal(Hash(ser.Mesh), Hash(par.Mesh));
            par.Compute();
            Assert.Equal(Hash(ser.Mesh), Hash(par.Mesh));
        }

        [Theory]
        [InlineData(TpmsType.Gyroid, TpmsStyle.Sheet)]
        [InlineData(TpmsType.IWP, TpmsStyle.SkeletalLow)]
        [InlineData(TpmsType.Lidinoid, TpmsStyle.SkeletalHigh)]
        public void QuantileTable_IsTheDirectOneBitForBit(TpmsType type, TpmsStyle style)
        {
            double[] tabled = TpmsLattice.BuildTable(type, style, 16);
            double[] direct;
            TpmsLattice.DisableAxisTables = true;
            try { direct = TpmsLattice.BuildTable(type, style, 16); } finally { TpmsLattice.DisableAxisTables = false; }
            Assert.Equal(direct.Length, tabled.Length);
            for (int i = 0; i < direct.Length; ++i) Assert.Equal(Bits(direct[i]), Bits(tabled[i]));
        }

        [Fact]
        public void AreaRatio_AndGradientRatio_AreTheDirectOnesBitForBit()
        {
            foreach (TpmsType type in new[] { TpmsType.Gyroid, TpmsType.IWP, TpmsType.Lidinoid }) {
                double tabled = TpmsLattice.ComputeAreaRatio(type);
                double direct;
                TpmsLattice.DisableAxisTables = true;
                try { direct = TpmsLattice.ComputeAreaRatio(type); } finally { TpmsLattice.DisableAxisTables = false; }
                Assert.Equal(Bits(direct), Bits(tabled));
                // the sampled gradient ratio, spelled out with Evaluate
                const int n = 24; double e = 1e-5, max = 0, step = 2 * Math.PI / n;
                for (int i = 0; i < n; ++i) for (int j = 0; j < n; ++j) for (int l = 0; l < n; ++l) {
                    double x = (i + 0.37) * step, y = (j + 0.61) * step, z = (l + 0.23) * step;
                    double gx = (TpmsLattice.Evaluate(type, x + e, y, z) - TpmsLattice.Evaluate(type, x - e, y, z)) / (2 * e);
                    double gy = (TpmsLattice.Evaluate(type, x, y + e, z) - TpmsLattice.Evaluate(type, x, y - e, z)) / (2 * e);
                    double gz = (TpmsLattice.Evaluate(type, x, y, z + e) - TpmsLattice.Evaluate(type, x, y, z - e)) / (2 * e);
                    max = Math.Max(max, Math.Sqrt(gx * gx + gy * gy + gz * gz));
                }
                Assert.Equal(Bits(Math.Min(1, 1.05 * max / TpmsLattice.GradientBound(type))), Bits(TpmsLattice.GradientRatio(type)));
            }
        }

        [Fact]
        public void NodeTables_AreHitByTheContouring()
        {
            // the tables only speed up if the nodes of MarchingCubes are found: almost every lookup of a box lattice is a node
            long l0 = TpmsLattice.TableLookups, h0 = TpmsLattice.TableHits;
            TpmsLattice.CountTableLookups = true;
            try { var t = Case("sheet")(); t.ParallelCompute = false; t.Compute(); Assert.Equal(TpmsStatus.Ok, t.Status); }
            finally { TpmsLattice.CountTableLookups = false; }
            long lookups = TpmsLattice.TableLookups - l0, hits = TpmsLattice.TableHits - h0;
            Assert.True(lookups > 1000, "lookups " + lookups);
            Assert.True(hits >= 0.9 * lookups, "hits " + hits + " of " + lookups);
        }
    }
}
