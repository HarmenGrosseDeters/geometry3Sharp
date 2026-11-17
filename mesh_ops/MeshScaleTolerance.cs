using System;
using g3;

namespace gs
{
    /// <summary>
    /// Scale-relative default tolerances for the mesh repair operations whose public tolerance fields default to NaN
    /// ("relative"; a finite value is an absolute distance, as in 1.0.324).
    /// </summary>
    internal static class MeshScaleTolerance
    {
        /// <summary>
        /// factor times the diagonal of the bounds of the triangles of the mesh, but at least 16 rounding units of
        /// the largest coordinate (far from the origin a smaller distance does not exist). For a mesh of diagonal 1
        /// it is factor. Scaling the mesh by a power of two scales it exactly. A mesh without triangles, or without
        /// a finite size, gets factor.
        /// </summary>
        public static double Relative(DMesh3 mesh, double factor)
        {
            if (mesh.TriangleCount == 0)
                return factor;
            AxisAlignedBox3d box = MeshRepairMetrics.TriangleBounds(mesh);
            double diag = box.DiagonalLength;
            double magnitude = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs);
            if (!MathUtil.IsFinite(diag) || !MathUtil.IsFinite(magnitude))
                return factor;
            double t = Math.Max(factor * diag, 16 * MathUtil.Epsilon * magnitude);
            return MathUtil.IsFinite(t) ? t : factor;
        }
    }
}
