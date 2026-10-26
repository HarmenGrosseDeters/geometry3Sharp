using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Filling holes, in place: find the boundary loops with MeshBoundaryLoops, then fill each one.
    /// MinimalHoleFill triangulates the boundary vertices (normally no new vertices) and flips towards a
    /// flat, developable fill (good for CAD-like holes); SmoothedHoleFill adds vertices at TargetEdgeLength
    /// (absolute, set it, eg to the border edge length; smooth patches for large holes); AutoHoleFill runs
    /// MinimalHoleFill and falls back to SmoothedHoleFill.
    /// </summary>
    public static class HoleFillExample
    {
        /// <summary> Fills every hole of mesh. Returns the number of holes that could not be filled. </summary>
        public static int Run(DMesh3 mesh)
        {
            // collect the loops first: filling changes the mesh
            List<EdgeLoop> holes = new MeshBoundaryLoops(mesh).Loops.ToList();
            int failed = 0;
            foreach (EdgeLoop loop in holes) {
                var fill = new MinimalHoleFill(mesh, loop);
                if (!fill.Apply())          // FillTriangles: the new triangles
                    failed++;
            }
            return failed;
        }
    }
}
