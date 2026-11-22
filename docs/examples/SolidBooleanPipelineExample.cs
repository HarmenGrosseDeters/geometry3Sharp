using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Soup-to-solid boolean + guarded reduction (SolidBooleanPipeline) and exact surface imprinting (ExactMeshImprint).
    /// </summary>
    public static class SolidBooleanPipelineExample
    {
        /// <summary>
        /// Computes the exact union of two subdivided solids and simplifies the result with self-intersection
        /// and quality guards in one pipeline call, returning both the clean solid and stage diagnostics.
        /// </summary>
        public static SolidBooleanPipeline UnionAndReduce()
        {
            DMesh3 a = new GridBox3Generator() { EdgeVertices = 6, Box = new Box3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            DMesh3 b = new GridBox3Generator() { EdgeVertices = 6, Box = new Box3d(new Vector3d(0.8, 0, 0), new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();

            var pipeline = new SolidBooleanPipeline(a, b, ExactMeshBoolean.Operations.Union) {
                TargetFraction = 0.25,
                PreventSelfIntersections = true
            };
            pipeline.Compute();
            return pipeline;
        }

        /// <summary>
        /// Cuts the exact intersection curves of a cutter box into a target box without removing any triangles.
        /// Preserves target vertex IDs, orientation, and labels the imprinted curve edges.
        /// </summary>
        public static ExactMeshImprint ImprintBox()
        {
            DMesh3 target = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            DMesh3 cutter = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0), new Vector3d(0.5, 0.5, 1.5)) }.Generate().MakeDMesh();

            var imprint = new ExactMeshImprint() {
                Target = target,
                Cutter = cutter,
                LabelCurves = true,
                SafeRounding = true
            };
            imprint.Compute();
            return imprint;
        }
    }
}
