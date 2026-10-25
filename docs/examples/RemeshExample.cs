using g3;

namespace g3.Examples
{
    /// <summary>
    /// Isotropic remeshing with Remesher: splits, collapses, flips and smooths until the edges are close to a
    /// target length. Works in place. Project onto the original surface to keep the shape, and constrain the
    /// boundary, or it moves.
    /// </summary>
    public static class RemeshExample
    {
        /// <summary> Remeshes mesh in place to about targetEdgeLength; boundary edges stay exactly where they are. </summary>
        public static void Run(DMesh3 mesh, double targetEdgeLength)
        {
            var remesher = new Remesher(mesh);
            remesher.SetTargetEdgeLength(targetEdgeLength);      // min / max edge length 0.66 / 1.33 times the target
            remesher.SetProjectionTarget(MeshProjectionTarget.Auto(mesh));   // copies the mesh: without it the surface shrinks
            MeshConstraintUtil.FixAllBoundaryEdges(remesher);    // or PreserveBoundaryLoops(remesher): boundary slides along itself
            for (int pass = 0; pass < 20; ++pass)
                remesher.BasicRemeshPass();
            // (gs.RemesherPro has the same setup and FastestRemesh() for large meshes)
        }
    }
}
