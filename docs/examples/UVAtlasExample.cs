using g3;

namespace g3.Examples
{
    /// <summary>
    /// UV atlas of an arbitrary mesh (closed or open, any genus): seams or charts, a parameterization per
    /// chart (BFF, with cone singularities where they give an overlap-free single chart), packing into the
    /// unit square, and an exact overlap test. The input is not changed.
    /// </summary>
    public static class UVAtlasExample
    {
        /// <summary> The atlas: atlas.AtlasMesh has the triangles of mesh, split along the seams, with vertex UVs. </summary>
        public static UVAtlasGenerator Run(DMesh3 mesh)
        {
            var atlas = new UVAtlasGenerator(mesh) {
                MaxAreaDistortion = 0.25,     // per chart: mean |log(area ratio)|; charts above it are split
                MaxConformalDistortion = 1.2, // per chart: mean angle distortion s1/s2
                Padding = 0.01,
            };
            atlas.Compute();
            // atlas.Status: Success, DistortionLimitExceeded (overlap-free, but some charts over the limits:
            // MaxCharts reached), InvalidMesh, Failed. AtlasToInputVertex / AtlasToInputTriangle map back to mesh.
            return atlas;
        }

        /// <summary> Only the seams: cut a closed mesh of any genus into a disc, then flatten it with BFF. </summary>
        public static ParameterizationResult CutAndFlatten(DMesh3 mesh, out DMesh3 disc)
        {
            var cut = new MeshDiscCut(mesh) { FeatureWeight = 1 };   // seams prefer sharp edges
            disc = cut.Compute() ? cut.CutMesh : null;                // one disc per component
            if (disc == null)
                return null;                                          // cut.Status, cut.Error say why
            return new BoundaryFirstFlattening(disc).Compute();       // UVs by vertex id of disc
        }

        /// <summary> BFF with cones: curvature concentrated at automatically placed cones, cut through them. </summary>
        public static ConeFlattening Cones(DMesh3 mesh)
        {
            var cones = new ConeFlattening(mesh) { MaxScaleFactor = 0.2 };   // stop placing cones when |log scale| <= 0.2
            cones.Compute();                    // cones.Result: UVs on cones.CutMesh; cones.ConeVertices, ConeCurvature
            return cones;
        }
    }
}
