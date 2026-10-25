using System.Linq;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// Smoothing, in place. MeshIterativeSmooth: repeated uniform Laplacian steps on a set of vertices (the
    /// others stay put). LaplacianMeshSmoother: one global least-squares solve with soft or hard position
    /// constraints.
    /// </summary>
    public static class SmoothingExample
    {
        /// <summary> Smooths all interior vertices; boundary vertices keep their position. </summary>
        public static void Iterative(DMesh3 mesh)
        {
            int[] interior = mesh.VertexIndices().Where(vid => !mesh.IsBoundaryVertex(vid)).ToArray();
            new MeshIterativeSmooth(mesh, interior) { Alpha = 0.5, Rounds = 20 }.Smooth();
        }

        /// <summary>
        /// Global Laplacian smoothing: boundary vertices fixed, interior vertices softly held at their current
        /// position (weight). Returns false (mesh unchanged) if the solver does not converge.
        /// </summary>
        public static bool Laplacian(DMesh3 mesh, double weight)
        {
            var smoother = new LaplacianMeshSmoother(mesh);
            foreach (int vid in mesh.VertexIndices()) {
                if (mesh.IsBoundaryVertex(vid))
                    smoother.SetConstraint(vid, mesh.GetVertex(vid), 100, true);    // hard: exactly here
                else
                    smoother.SetConstraint(vid, mesh.GetVertex(vid), weight);       // soft: pulled towards here
            }
            return smoother.SolveAndUpdateMesh();
        }
    }
}
