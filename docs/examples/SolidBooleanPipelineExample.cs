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
        /// Cuts a box with an open cutting surface: the square z = 0.5, larger than the box, its normals up. The open tool of a
        /// Difference goes to the sheet role by default: the part of the box behind the sheet (below it) is removed, nothing is
        /// repaired, and Report.Route names the step ("SheetRole"). If the sheet role refused (eg the sheet ended inside the box),
        /// the route would add a "Fallback" step with the reason and the run would treat the tool as an open solid (InputRepair).
        /// </summary>
        public static SolidBooleanPipeline CutWithAnOpenSheet()
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            var sheet = new DMesh3();
            int a = sheet.AppendVertex(new Vector3d(-1, -1, 0.5)), b = sheet.AppendVertex(new Vector3d(2, -1, 0.5));
            int c = sheet.AppendVertex(new Vector3d(2, 2, 0.5)), d = sheet.AppendVertex(new Vector3d(-1, 2, 0.5));
            sheet.AppendTriangle(a, b, c);
            sheet.AppendTriangle(a, c, d);

            var pipeline = new SolidBooleanPipeline(box, sheet, ExactMeshBoolean.Operations.Difference);
            pipeline.Compute();
            return pipeline;
        }

        /// <summary>
        /// The same cut with a sheet that is meant to end flush on the side faces of the box but sits a few rounding units
        /// inside them: the sheet role refuses (RimInsideWorkpiece). With the default AutomaticSteps (GridBeforeFallback) the run is done once more with
        /// the inputs snapped to a cell taken from the data, and Report.Route lists "SheetRole", "Grid" (the refusal, the cell,
        /// the vertices moved) and "SheetRole" again.
        /// </summary>
        public static SolidBooleanPipeline CutWithAFlushSheetOnTheGrid()
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            double inside = 3 * MathUtil.Epsilon, lo = inside, hi = 1 - inside;
            var sheet = new DMesh3();
            int a = sheet.AppendVertex(new Vector3d(lo, lo, 0.5)), b = sheet.AppendVertex(new Vector3d(hi, lo, 0.5));
            int c = sheet.AppendVertex(new Vector3d(hi, hi, 0.5)), d = sheet.AppendVertex(new Vector3d(lo, hi, 0.5));
            sheet.AppendTriangle(a, b, c);
            sheet.AppendTriangle(a, c, d);

            var pipeline = new SolidBooleanPipeline(box, sheet, ExactMeshBoolean.Operations.Difference);
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
