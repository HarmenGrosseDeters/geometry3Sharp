using System;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Sharp-feature Dual Contouring (DualContouring with ExactHermite) and certified mesh offsetting (MeshOffset).
    /// </summary>
    public static class DualContouringAndOffsetExample
    {
        sealed class BoxImplicit : BoundedImplicitFunction3d
        {
            readonly AxisAlignedBox3d box = new AxisAlignedBox3d(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            public AxisAlignedBox3d Bounds() { return new AxisAlignedBox3d(new Vector3d(-1.5, -1.5, -1.5), new Vector3d(1.5, 1.5, 1.5)); }
            public double Value(ref Vector3d pt)
            {
                double dx = Math.Abs(pt.x) - 1.0, dy = Math.Abs(pt.y) - 1.0, dz = Math.Abs(pt.z) - 1.0;
                double outside = new Vector3d(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length;
                double inside = Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0);
                return outside + inside;
            }
        }

        /// <summary> Extracts a sharp-edged cube from its exact signed distance field using DualContouring. </summary>
        public static DualContouring ContourSharpBox()
        {
            var implicitBox = new BoxImplicit();
            var dc = new DualContouring {
                Implicit = implicitBox,
                Bounds = implicitBox.Bounds(),
                CellSize = 0.25,
                InsideIsLess = true,
                ExactHermite = true
            };
            dc.Generate();
            return dc;
        }

        /// <summary>
        /// Computes a certified outward offset of a unit cube using MeshOffset (dual contouring of the exact
        /// distance field with certified one-sided band deviation and exact self-intersection verification).
        /// </summary>
        public static MeshOffset CertifiedOffset(double distance)
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            var op = new MeshOffset(box, distance) { RelativeCellSize = 0.35 };
            op.Compute();
            return op;
        }
    }
}
