using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// A remesh kept within a band of its input (Remesher.MaxDeviation: every triangle the remesher makes or moves is checked
    /// against the band, in floating point; a split's halves are not checked, they lie in the old triangles), then checked both ways by RemeshCertificate: closed, manifold, no
    /// self-intersection (exact), certified two-sided Hausdorff distance, the same components, genus and nesting.
    /// </summary>
    public static class RemeshCertificateExample
    {
        /// <summary> Remeshes mesh in place within band x its diagonal and returns the certificate of the result against the input. </summary>
        public static RemeshCertificate Run(DMesh3 mesh, double targetEdgeLength, double band = 0.005)
        {
            DMesh3 input = new DMesh3(mesh);
            double maxDeviation = band * input.CachedBounds.DiagonalLength;
            var remesher = new Remesher(mesh) { MaxDeviation = maxDeviation };     // the reference: the mesh at the first pass
            remesher.SetTargetEdgeLength(targetEdgeLength);
            remesher.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, 30);
            remesher.ApplySafePreset();         // MinTriangleQuality 0.2, PreventSelfIntersections
            for (int pass = 0; pass < 10; ++pass)
                remesher.BasicRemeshPass();
            // the band guard is one-sided (result to input); the certificate measures both directions
            return RemeshCertificate.Compute(input, mesh, maxDeviation);
        }
    }
}
