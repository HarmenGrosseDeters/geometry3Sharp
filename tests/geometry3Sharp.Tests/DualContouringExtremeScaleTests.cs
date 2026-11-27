using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A creased shape (a box, an L-shape) at the ends of the magnitude range gives the mesh of the unit shape (#1376): the degenerate
    /// triangle rule of the quad splitting and the cluster test of the simplification take the length of a cross product, a fourth
    /// power of an edge, which is infinite above about 2^256 and zero below about 2^-256.
    /// </summary>
    public class DualContouringExtremeScaleTests
    {
        sealed class BoxSdf : BoundedImplicitFunction3d
        {
            public double S = 1;
            public AxisAlignedBox3d Bounds() => new AxisAlignedBox3d(Vector3d.Zero, 0.9 * S);
            public double Value(ref Vector3d q)
            {
                Vector3d p = new Vector3d(ExactPoint3.ScaleB(q.x, -Exp), ExactPoint3.ScaleB(q.y, -Exp), ExactPoint3.ScaleB(q.z, -Exp));
                double dx = Math.Abs(p.x - 0.03) - 0.55, dy = Math.Abs(p.y + 0.02) - 0.36, dz = Math.Abs(p.z - 0.05) - 0.27;
                double d = new Vector3d(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0);
                return ExactPoint3.ScaleB(d, Exp);
            }
            public int Exp;
        }

        static string Run(int e, bool simplify, out DMesh3 mesh)
        {
            double s = ExactPoint3.ScaleB(1.0, e);
            var f = new BoxSdf { S = s, Exp = e };
            var dc = new DualContouring { Implicit = f, Bounds = new AxisAlignedBox3d(Vector3d.Zero, 0.9 * s), CellSize = 0.11 * s, InsideIsLess = true, Simplify = simplify };
            dc.Generate();
            var m = new DMesh3(dc.Mesh);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, new Vector3d(ExactPoint3.ScaleB(p.x, -e), ExactPoint3.ScaleB(p.y, -e), ExactPoint3.ScaleB(p.z, -e)));
            }
            mesh = m;
            ulong h = 1469598103934665603UL;
            void Mix(long x) { unchecked { for (int i = 0; i < 8; ++i) { h ^= (byte)(x >> (8 * i)); h *= 1099511628211UL; } } }
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); Mix(BitConverter.DoubleToInt64Bits(p.x)); Mix(BitConverter.DoubleToInt64Bits(p.y)); Mix(BitConverter.DoubleToInt64Bits(p.z)); }
            foreach (int t in m.TriangleIndices()) { Index3i i = m.GetTriangle(t); Mix(i.a); Mix(i.b); Mix(i.c); }
            return m.TriangleCount + " " + h.ToString("X16");
        }

        [Theory]
        [InlineData(300, true)]
        [InlineData(300, false)]
        [InlineData(-300, true)]
        [InlineData(-300, false)]
        public void Box_AtTheEndsOfTheRange_IsTheMeshOfTheUnitBox(int e, bool simplify)
        {
            string unit = Run(0, simplify, out DMesh3 um);
            string big = Run(e, simplify, out DMesh3 bm);
            Assert.True(um.TriangleCount > 0);
            Assert.Equal(unit, big);
            Assert.True(bm.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(bm));
        }
    }
}
