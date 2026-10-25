using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Winding number solidify: rebuild a broken mesh (open, self-intersecting, a triangle soup) as a closed,
    /// manifold solid. The input triangulation is not kept; features smaller than the cell size are lost.
    /// </summary>
    public static class WindingNumberSolidifyExample
    {
        /// <summary> Two overlapping spheres, one of them with a hole, as a single mesh -> their closed union. </summary>
        public static DMesh3 Run()
        {
            DMesh3 broken = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            DMesh3 second = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshTransforms.Translate(second, new Vector3d(1.2, 0, 0));
            MeshEditor.Append(broken, second);                          // overlapping shells: self-intersecting
            for (int tid = 0; tid < 10; ++tid)
                broken.RemoveTriangle(tid);                             // and a hole

            var solidify = new MeshWindingNumberSolidify(broken) {
                GridResolution = 96,    // cells along the bounding box diagonal (or set CellSize)
                Adaptive = true         // octree: evaluates the winding number only near the surface
            };
            if (!solidify.Compute())
                return null;
            return solidify.Result;     // a new mesh, the input is not modified
        }
    }
}
