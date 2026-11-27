using System.Collections.Generic;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// A cutting sheet that ends a little short of the workpiece's surface: the sheet role of the exact boolean refuses it (<c>RimInsideWorkpiece</c>), the diagnosis says how
    /// deep the rim is inside the workpiece, and, where the rim is within the limit, MeshSheetExtension continues it straight up to the surface (the first exit of each rim vertex's ray) so that the sheet
    /// cuts. Opt-in: nothing calls the extension unless you do. The extension is a guess about intent (a sheet that was meant to end there is cut through anyway), so look at the
    /// report before you use the cut; curved sheets are continued straight (<c>Report.U</c> estimates what that costs, as an estimate, not a bound).
    /// </summary>
    public static class SheetExtensionExample
    {
        /// <summary> what Run found: the plain cut's refusal, the diagnosis, the extension's status and the cut with the extended sheet (null where nothing was cut) </summary>
        public sealed class Outcome
        {
            /// <summary> the sheet role's reason on the sheet as given ("None" where it cuts as it is) </summary>
            public ExactMeshBoolean.SheetRefusal PlainRefusal;
            /// <summary> the diagnosis: per run of rim edges inside the workpiece the depth, the length inside and U (no strip is built for it) </summary>
            public SheetExtensionReport Diagnosis;
            /// <summary> the extension's status (Success, or the reason it refused: DepthAboveLimit, CurveRule, ...); NotComputed where the diagnosis said the rim is too deep to try </summary>
            public MeshSheetExtension.ExtensionStatus Status;
            /// <summary> the sheet with the strip added, the original triangles unchanged (null where it was not extended) </summary>
            public DMesh3 ExtendedSheet;
            /// <summary> the Difference of the workpiece by the sheet (the extended one where the plain one was refused), or null </summary>
            public DMesh3 Cut;
        }

        /// <summary> cuts the closed workpiece along the sheet (normals point away from the part that is removed), continuing a short rim where the sheet role refuses it </summary>
        public static Outcome Run(DMesh3 workpiece, DMesh3 sheet)
        {
            var outcome = new Outcome();
            // 1) the sheet role as it is: a rim inside the workpiece is refused with a reason, nothing is guessed
            DMesh3 cut = Difference(workpiece, sheet, out outcome.PlainRefusal);
            if (cut != null) {
                outcome.Cut = cut;
                return outcome;
            }
            if (outcome.PlainRefusal != ExactMeshBoolean.SheetRefusal.RimInsideWorkpiece)
                return outcome;

            // 2) the diagnosis alone: how deep and how long the rim is inside the workpiece (cheap: no strip, no imprint, no cut)
            var extension = new MeshSheetExtension { Sheet = sheet, Workpiece = workpiece };       // MaxExtension = 5 % of the sheet's diagonal
            if (!extension.Diagnose())
                return outcome;
            outcome.Diagnosis = extension.Report;
            // "the rim is N % of the diagonal inside the workpiece, at most M % deep": Report.InsideLengthRelative, Report.MaxDepthRelative
            if (extension.Report.Runs.Exists(run => run.NoExitWithinLimit))
                return outcome;                                                                    // too deep to be a sheet that ends short: fix the input, or use the swept cut

            // 3) the extension: the strip, verified exactly (new points strictly outside, one curve per run, no contact, the sheet role accepts it)
            outcome.Status = extension.Compute() ? MeshSheetExtension.ExtensionStatus.Success : extension.Status;
            if (extension.Result == null)
                return outcome;
            outcome.ExtendedSheet = extension.Result;

            // 4) the cut with the extended sheet is an ordinary sheet cut (the helper already ran it as its last check: extension.Report.Cut)
            outcome.Cut = Difference(workpiece, extension.Result, out ExactMeshBoolean.SheetRefusal refusal);
            return outcome;
        }

        /// <summary>
        /// The same cut through the pipeline: the sheet is stated as a sheet, and SheetExtension = StatedSheets lets the pipeline continue a short rim itself (after the direct cut and the grid
        /// retry), cut again, and report what it added (Report.SheetExtensions, the route step "SheetExtension"). A refused extension leaves the run as without the option.
        /// </summary>
        public static SolidBooleanPipeline RunInPipeline(DMesh3 workpiece, DMesh3 sheet)
        {
            var pipeline = new SolidBooleanPipeline(new List<DMesh3> { workpiece, sheet }, ExactMeshBoolean.Operations.Difference) {
                InputRoles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet },
                SheetExtension = SolidBooleanPipeline.SheetExtensionModes.StatedSheets
            };
            pipeline.Compute();
            return pipeline;
        }

        static DMesh3 Difference(DMesh3 workpiece, DMesh3 sheet, out ExactMeshBoolean.SheetRefusal refusal)
        {
            var op = new ExactMeshBoolean(new List<DMesh3> { workpiece, sheet }, ExactMeshBoolean.Operations.Difference) {
                AllowOpenMeshes = true,
                Roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet }
            };
            op.Compute();
            refusal = op.SheetReason;
            return op.Status == ExactMeshBoolean.BooleanStatus.Success ? op.Result : null;
        }
    }
}
