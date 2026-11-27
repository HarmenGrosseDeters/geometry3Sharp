using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// The quality post-pass after a remesh: RemeshQualityPass improves the worst triangles first (needle collapses,
    /// cap and needle splits, flips, relocations; crease vertices move, are inserted and removed along the creases)
    /// and never lowers the smallest angle. Every triangle it makes is certified within MaxDeviation of the reference
    /// (with TwoSided also the other way); crease edges stay within it of the reference's creases.
    /// </summary>
    public static class RemeshQualityExample
    {
        /// <summary> Remeshes mesh in place (creases above 30 degrees kept), then runs the post-pass with a band of band x the input's diagonal. </summary>
        public static RemeshQualityResult Run(DMesh3 mesh, double targetEdgeLength, double band = 0.005)
        {
            DMesh3 input = new DMesh3(mesh);
            var remesher = new Remesher(mesh);
            remesher.SetTargetEdgeLength(targetEdgeLength);
            remesher.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, 30);
            remesher.MinTriangleQuality = 0.2;
            for (int pass = 0; pass < 20; ++pass)
                remesher.BasicRemeshPass();

            var quality = new RemeshQualityPass(mesh, input) {
                MaxDeviation = band * input.CachedBounds.DiagonalLength,   // absolute, in the mesh's units
                CreaseAngleDegrees = 30,                                    // the same creases as the remesher's
            };
            RemeshQualityResult r = quality.Compute();
            // r.MinAngleAfter >= r.MinAngleBefore always; r.ResultWithinBand tells whether the whole result is certified
            // (false if the remesh left triangles outside the band: the pass certifies only what it made)
            return r;
        }
    }
}
