using g3;

namespace g3.Examples
{
    /// <summary>
    /// Offset surface from a signed distance grid: MeshSignedDistanceGrid samples the distance to a closed mesh
    /// (negative inside), MarchingCubes extracts the iso-surface at the offset distance.
    /// </summary>
    public static class OffsetSurfaceExample
    {
        /// <summary> Surface at signed distance offset from the closed mesh (positive = outward). Returns a new mesh. </summary>
        public static DMesh3 Run(DMesh3 mesh, double offset, double cellSize)
        {
            // exact distances only in a band around the surface: make it wide enough for the offset
            int band = (int)(System.Math.Abs(offset) / cellSize) + 2;
            var sdf = new MeshSignedDistanceGrid(mesh, cellSize) { ExactBandWidth = band };
            sdf.Compute();

            var implicitF = new DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOriginD, sdf.CellSize);
            AxisAlignedBox3d bounds = mesh.CachedBounds;
            bounds.Expand(System.Math.Max(offset, 0) + 2 * cellSize);
            var mc = new MarchingCubes() {
                Implicit = implicitF,
                IsoValue = offset,
                Bounds = bounds,
                CubeSize = cellSize
            };
            mc.Generate();
            return mc.Mesh;
        }
    }
}
