using System.Collections.Generic;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// Handle-based surface deformation: rotation-invariant ARAP (ArapMeshDeformer)
    /// and similarity-equivariant biharmonic displacement (BiharmonicMeshDeformer).
    /// </summary>
    public static class DeformationExample
    {
        static DMesh3 StripGrid(int nx, int ny)
        {
            var mesh = new DMesh3();
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    mesh.AppendTriangle(a, b, d);
                    mesh.AppendTriangle(a, d, c);
                }
            return mesh;
        }

        /// <summary>
        /// Pins the left edge (x = 0) and lifts the right edge (x = 6) by +1 in Z using both
        /// ArapMeshDeformer and BiharmonicMeshDeformer, returning both ResultInfo objects.
        /// </summary>
        public static void DeformStrip(out ArapMeshDeformer.ResultInfo arap, out BiharmonicMeshDeformer.ResultInfo biharmonic)
        {
            DMesh3 strip = StripGrid(6, 2);
            var handles = new Dictionary<int, Vector3d>();
            foreach (int vid in strip.VertexIndices()) {
                Vector3d p = strip.GetVertex(vid);
                if (p.x == 0)
                    handles[vid] = p;
                else if (p.x == 6)
                    handles[vid] = p + new Vector3d(0, 0, 1.0);
            }

            arap = new ArapMeshDeformer(strip).Compute(handles);
            biharmonic = new BiharmonicMeshDeformer(strip).Compute(handles);
        }
    }
}
