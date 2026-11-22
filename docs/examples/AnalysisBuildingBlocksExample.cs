using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Shape-analysis building blocks: wall thickness (MeshWallThickness), signed deviation maps
    /// with certified Hausdorff bounds (MeshDeviation), and RANSAC + LM primitive fitting (PrimitiveFit3).
    /// </summary>
    public static class AnalysisBuildingBlocksExample
    {
        /// <summary> Measures wall thickness of a slab of thickness 0.4 along Z using inward rays. </summary>
        public static MeshWallThickness MeasureSlabThickness()
        {
            DMesh3 slab = new GridBox3Generator() { EdgeVertices = 4, Box = new Box3d(Vector3d.Zero, new Vector3d(2, 2, 0.2)) }.Generate().MakeDMesh();
            var wt = new MeshWallThickness(slab) { Method = MeshWallThickness.Methods.Ray };
            wt.Compute();
            return wt;
        }

        /// <summary>
        /// Measures signed vertex deviation and certified Hausdorff bounds between a sphere of radius 1.1
        /// and a reference sphere of radius 1.0 (positive outside the reference solid).
        /// </summary>
        public static MeshDeviation MeasureSphereDeviation()
        {
            DMesh3 reference = new Sphere3Generator_NormalizedCube() { Radius = 1.0, EdgeVertices = 8 }.Generate().MakeDMesh();
            DMesh3 enlarged = new Sphere3Generator_NormalizedCube() { Radius = 1.1, EdgeVertices = 8 }.Generate().MakeDMesh();
            var dev = new MeshDeviation(enlarged, reference) {
                Sign = MeshDeviation.Signs.PseudoNormal,
                ComputeCertifiedBound = true,
                CertifiedTolerance = 1e-3
            };
            dev.Compute();
            return dev;
        }

        /// <summary> Fits a sphere to vertices of a procedural sphere of radius 2 centered at (1, 2, 3). </summary>
        public static PrimitiveFit3 FitSphere()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 2.0, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sphere, new Vector3d(1, 2, 3));
            var fit = new PrimitiveFit3(sphere) {
                Primitive = PrimitiveFit3.Primitives.Sphere,
                Seed = 1
            };
            fit.Compute();
            return fit;
        }
    }
}
