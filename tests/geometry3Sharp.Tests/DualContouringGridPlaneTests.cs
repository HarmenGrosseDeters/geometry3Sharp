using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A box whose edges lie on grid planes (a CAD dimension that is a multiple of the cell) keeps its corners (#1598): the default
    /// Simplify merges cells into blocks, and the vertex of a block was kept QEFCellMargin times the BLOCK size inside the block, so
    /// a feature on the block's boundary was moved by up to 1 % of the block (0.08 cell at 8 cells). The margin of a merged block is
    /// QEFCellMargin times the size of one cell.
    /// </summary>
    public class DualContouringGridPlaneTests
    {
        sealed class BoxSdf : BoundedImplicitFunction3d
        {
            public Similarity Sim = Similarity.Identity;
            public AxisAlignedBox3d Bounds()
            {
                var b = new AxisAlignedBox3d(Vector3d.Zero, 0.8); b.Expand(0.1);
                return new AxisAlignedBox3d(Sim.Apply(b.Min), Sim.Apply(b.Max));
            }
            public double Value(ref Vector3d q)
            {
                Vector3d p = Sim.Inverse(q);
                double dx = Math.Abs(p.x) - 0.5, dy = Math.Abs(p.y) - 0.35, dz = Math.Abs(p.z) - 0.25;
                return Sim.Scale * (new Vector3d(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0));
            }
        }

        static DualContouring Make(Similarity sim, bool parallel = true)
        {
            var f = new BoxSdf { Sim = sim };
            return new DualContouring { Implicit = f, Bounds = f.Bounds(), CellSize = 0.05 * sim.Scale, InsideIsLess = true, ExactHermite = true, ParallelCompute = parallel };
        }

        static string Digest(DMesh3 m)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long x) { unchecked { for (int i = 0; i < 8; ++i) { h ^= (byte)(x >> (8 * i)); h *= 1099511628211UL; } } }
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); Mix(BitConverter.DoubleToInt64Bits(p.x)); Mix(BitConverter.DoubleToInt64Bits(p.y)); Mix(BitConverter.DoubleToInt64Bits(p.z)); }
            foreach (int t in m.TriangleIndices()) { Index3i i = m.GetTriangle(t); Mix(i.a); Mix(i.b); Mix(i.c); }
            return m.TriangleCount + " " + h.ToString("X16");
        }

        static double Volume(DMesh3 m)
        {
            double v = 0;
            foreach (int t in m.TriangleIndices()) { m.GetTriVertices(t, ref _a, ref _b, ref _c); v += _a.Dot(_b.Cross(_c)) / 6; }
            return v;
        }
        static Vector3d _a, _b, _c;

        [Fact]
        public void BoxOnGridPlanes_KeepsItsCornersWithinTheMarginOfOneCell()
        {
            DualContouring dc = Make(Similarity.Identity);
            dc.Generate();
            DMesh3 m = dc.Mesh;
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(m));
            Assert.Equal(12, m.TriangleCount);
            double tol = dc.QEFCellMargin * dc.CellSize + 1e-12;       // the margin of one cell: the vertex of a single cell is that far inside
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Assert.True(Math.Abs(Math.Abs(p.x) - 0.5) <= tol && Math.Abs(Math.Abs(p.y) - 0.35) <= tol && Math.Abs(Math.Abs(p.z) - 0.25) <= tol, p.ToString());
            }
            Assert.InRange(Volume(m), 0.35 * (1 - 1e-3), 0.35 * (1 + 1e-3));
        }

        [Fact]
        public void PowerOfTwoScales_GiveTheBitsOfTheUnitBox()
        {
            DualContouring unit = Make(Similarity.Identity);
            unit.Generate();
            string expected = Digest(unit.Mesh);
            foreach (Similarity s in new[] { Similarity.Pow2Small, Similarity.Pow2Large }) {
                DualContouring dc = Make(s);
                dc.Generate();
                Assert.Equal(expected, Digest(Invariance.MappedBack(dc.Mesh, s)));
            }
        }

        [Fact]
        public void AllSimilarities_GiveAClosedCleanBoxOfTheRightVolume()
        {
            foreach (Similarity s in Similarity.All()) {
                if (s.Rotation.EpsilonEqual(Matrix3d.Identity, 0) == false) continue;    // (a rotated box is not on the grid planes)
                DualContouring dc = Make(s);
                dc.Generate();
                DMesh3 m = Invariance.MappedBack(dc.Mesh, s);
                Assert.True(m.IsClosed(), s.Name);
                Assert.Equal("", MeshRobustRepair.NotCleanReasons(dc.Mesh));
                Assert.InRange(Volume(m), 0.35 * 0.98, 0.35 * 1.02);
            }
        }

        [Fact]
        public void Generate_Twice_AndSerial_GiveTheSameBits()
        {
            DualContouring dc = Make(Similarity.Identity);
            dc.Generate();
            string first = Digest(dc.Mesh);
            dc.Generate();
            Assert.Equal(first, Digest(dc.Mesh));
            DualContouring serial = Make(Similarity.Identity, false);
            serial.Generate();
            Assert.Equal(first, Digest(serial.Mesh));
        }

        [Fact]
        public void BoundsNearTheLargestDouble_WhoseLastNodeOverflows_AreRejectedUpFront()
        {
            // (the last node is origin + CellSize (N - 1), up to a cell beyond Bounds.Max: +inf here, and ExactPredicates throws on it)
            var b = new AxisAlignedBox3d(new Vector3d(1.6e308, 1.6e308, 1.6e308), new Vector3d(1.79e308, 1.79e308, 1.79e308));
            var dc = new DualContouring { Implicit = new BoxSdf(), Bounds = b, CellSize = 2e306, InsideIsLess = true };
            var ex = Assert.Throws<ArgumentException>(() => dc.Generate());
            Assert.Contains("the grid has a node", ex.Message);
            // a grid that fits is not rejected by the check
            var ok = new DualContouring { Implicit = new BoxSdf(), Bounds = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)), CellSize = 0.5, InsideIsLess = true };
            ok.Generate();
            Assert.True(ok.Mesh.TriangleCount > 0);
        }
    }
}
