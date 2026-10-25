using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Filling holes, in place: find the boundary loops with MeshBoundaryLoops, then fill each one.
    /// MinimalHoleFill builds a fill of minimal area without new vertices; SmoothedHoleFill adds vertices
    /// at about the border edge length (better for large holes); AutoHoleFill tries minimal and falls back.
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
