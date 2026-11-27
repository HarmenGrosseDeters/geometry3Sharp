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
        /// inside them: the sheet role refuses (RimInsideWorkpiece). With the default AutomaticSteps (GridBeforeFallback | GridAfterRoundingNotClean) the run is done once more with
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
        /// Opt-in verification of a finished result. A bucket (a box without its top face) inside a block is an open tool that the sheet role
        /// refuses (its rim lies inside the block); InputRepair = AutoRepair closes it with a lid, and the Difference has a cavity whose lid lies on no
        /// input. Without Verification that is a success; with NoInventedSurface the run is refused (Status VerificationFailed, Result null) and
        /// Report.InventedSurfaceShare (4 of 120 of the area) and the "Verification" step of Report.Route say why.
        /// </summary>
        public static SolidBooleanPipeline RefuseAnInventedLid()
        {
            DMesh3 block = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(4, 4, 4))) }.Generate().MakeDMesh();
            var bucket = new DMesh3();
            var v = new int[8];
            for (int k = 0; k < 8; ++k)
                v[k] = bucket.AppendVertex(new Vector3d((k & 1) == 0 ? 1 : 3, (k & 2) == 0 ? 1 : 3, (k & 4) == 0 ? 1 : 3));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                bucket.AppendTriangle(v[q[0]], v[q[1]], v[q[2]]);
                bucket.AppendTriangle(v[q[0]], v[q[2]], v[q[3]]);
            }

            var pipeline = new SolidBooleanPipeline(block, bucket, ExactMeshBoolean.Operations.Difference) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair,
                Verification = SolidBooleanPipeline.VerificationModes.NoInventedSurface | SolidBooleanPipeline.VerificationModes.RoundingVolume
            };
            pipeline.Compute();
            return pipeline;
        }

        /// <summary>
        /// What to check after Compute() (#1576): not Clean, but ResultKind. Run on the intersection of two boxes that touch in a plane (the exact
        /// result is empty: EmptyExact, though NotCleanReason is "empty"), on one that overlap by 1e-12 (the exact result is a slab that the
        /// rounding removes: EmptyAfterRounding, with the slab in Report.RoundingRemoved) and on a union whose second input overlaps itself
        /// (Solid, and Report.InputsWithWindingOutsideZeroOne lists input 1). Returns the sentence that a caller would log.
        /// </summary>
        public static string ReadTheResultKind(SolidBooleanPipeline pipeline)
        {
            if (!pipeline.Compute())
                return pipeline.Status + ": " + pipeline.Error;
            SolidBooleanReport report = pipeline.Report;
            string text;
            switch (report.ResultKind) {
                case SolidBooleanResultKind.Solid:
                    text = "a clean solid";
                    break;
                case SolidBooleanResultKind.SolidWithContacts:
                    text = "a solid whose only defects are contacts (OnlyContacts " + report.OnlyContacts + ")";
                    break;
                case SolidBooleanResultKind.SolidNotClean:
                    text = "a solid that is not clean: " + report.NotCleanReason;
                    break;
                case SolidBooleanResultKind.EmptyExact:
                    text = "empty: the exact result is empty";
                    break;
                default:
                    text = "empty after the rounding removed " + report.RoundingRemoved.Count + " component(s) of volume "
                        + string.Join(", ", report.RoundingRemoved.ConvertAll(c => c.Volume.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
            if (report.InputsWithWindingOutsideZeroOne.Count > 0)
                text += "; input(s) " + string.Join(", ", report.InputsWithWindingOutsideZeroOne) + " overlap themselves or are inside out";
            return text;
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
