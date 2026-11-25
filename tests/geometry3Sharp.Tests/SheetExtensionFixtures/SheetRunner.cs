using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Status = gs.MeshSheetExtension.ExtensionStatus;

namespace geometry3Sharp.Tests.SheetExt
{
    /// <summary> one run of MeshSheetExtension on a case, in a frame, with the plain sheet role's verdict on the unextended sheet and the verdict of the checks </summary>
    internal sealed class SheetRun
    {
        public SheetCase Case;
        public SheetCaseData Data;
        public DMesh3 W, Sheet;
        public List<DMesh3> Others = new List<DMesh3>();
        public MeshSheetExtension Op;
        public string Plain = "";
        public string Actual = "";
        public double Kept = double.NaN, Analytic = double.NaN, VolErrRel = double.NaN;
        public string NotClean = "";
        public Status Status => Op.Status;
        public SheetExtensionReport Report => Op.Report;
        public DMesh3 Result => Op.Result;
    }

    /// <summary> the cases of the design's section 8 run through the library helper, and the verdict the spike used (outcome, reason, volume, cleanliness) </summary>
    internal static class SheetRunner
    {
        public static double Volume(DMesh3 m) { return MeshSheetExtension.Volume(m); }

        public static DMesh3 Map(DMesh3 m, Func<Vector3d, Vector3d> f) { return f == null ? m : SheetGeometry.Transformed(m, f); }

        public static string PlainCut(DMesh3 w, List<DMesh3> sheets, ExactMeshBoolean.SheetUncutSides uncut, out int witnessMesh)
        {
            witnessMesh = -1;
            var meshes = new List<DMesh3> { w }; meshes.AddRange(sheets);
            var roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid };
            foreach (var s in sheets) roles.Add(ExactMeshBoolean.InputRole.Sheet);
            var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = true, Roles = roles, SheetUncutSide = uncut };
            op.Compute();
            if (op.Status == ExactMeshBoolean.BooleanStatus.SheetRefused) { witnessMesh = op.SheetWitnessMesh; return "refused " + op.SheetReason; }
            return op.Status.ToString();
        }

        /// <summary> run the helper on a case in a frame (null: identity); mutate sets the options (or the internal mutant switches) before Compute() </summary>
        public static SheetRun Run(SheetCase c, Func<Vector3d, Vector3d> frame = null, Action<MeshSheetExtension> mutate = null, SheetCaseData data = null)
        {
            SheetCaseData d = data ?? c.Build();
            var run = new SheetRun { Case = c, Data = d };
            run.W = Map(d.W, frame);
            var sheets = d.Sheets.Select(s => Map(s, frame)).ToList();
            run.Plain = PlainCut(run.W, sheets, d.Uncut, out int wm);
            // with several sheets the sheet role names the refused one; otherwise the case says which one is short
            int k = wm > 0 ? wm - 1 : d.ShortIndex;
            run.Sheet = sheets[k];
            var roles = new List<ExactMeshBoolean.InputRole>();
            for (int i = 0; i < sheets.Count; ++i)
                if (i != k) { run.Others.Add(sheets[i]); roles.Add(ExactMeshBoolean.InputRole.Sheet); }
            var op = new MeshSheetExtension { Sheet = run.Sheet, Workpiece = run.W, OtherTools = run.Others, OtherRoles = roles, UncutSide = d.Uncut };
            mutate?.Invoke(op);
            op.Compute();
            run.Op = op;
            run.Actual = op.Status == Status.Success ? (op.Report.Extended ? "extended" : "unchanged") : op.Status.ToString();
            double analytic = d.Analytic;
            if (double.IsNaN(analytic) && d.AnalyticFromExtended != null) {
                if (double.IsNaN(c.CachedAnalytic) && op.Report.Candidate != null && frame == null) c.CachedAnalytic = d.AnalyticFromExtended(op.Report.Candidate);
                analytic = c.CachedAnalytic;
            }
            run.Analytic = analytic;
            if (op.Report.Cut != null) {
                run.Kept = Volume(op.Report.Cut);
                run.NotClean = MeshRobustRepair.NotCleanReason(op.Report.Cut) ?? "";
                if (!double.IsNaN(analytic)) run.VolErrRel = (run.Kept - analytic) / Math.Abs(Volume(run.W));
            }
            return run;
        }

        /// <summary>
        /// the spike's verdict: null if the run ended as the case says (the outcome, an allowed reason, the analytic volume within tol of the workpiece, a clean cut), else why not.
        /// exact: if not null, the status must be this one (a refusal case whose reason the library gives deterministically)
        /// </summary>
        public static string Verdict(SheetRun r, double tol = 1e-9, Status? exact = null)
        {
            SheetCase c = r.Case;
            bool accepted = r.Status == Status.Success;
            if (!accepted) {
                if (c.Expect == SheetKind.Any) return null;
                if (c.Expect != SheetKind.Refused) return "refused " + r.Status + " (" + r.Report.Witness + "), expected " + c.Expect;
                if (exact.HasValue) return r.Status == exact.Value ? null : "refused " + r.Status + ", expected exactly " + exact.Value;
                return c.Reasons.Contains(r.Status) ? null : "refused " + r.Status + ", expected one of " + string.Join("|", c.Reasons);
            }
            if (c.Expect == SheetKind.Refused) return "accepted (" + r.Actual + "), expected refusal " + string.Join("|", c.Reasons);
            if (c.Expect == SheetKind.Extended && r.Actual != "extended") return "not extended";
            if (c.Expect == SheetKind.Unchanged && r.Actual != "unchanged") return "extended, expected unchanged";
            if (r.NotClean != "") return "cut not clean: " + r.NotClean;
            double lim = r.Data.AnalyticFromExtended != null ? Math.Max(tol, 1e-6) : tol;
            if (!double.IsNaN(r.VolErrRel) && Math.Abs(r.VolErrRel) > lim) return "volume " + r.Kept + " against " + r.Analytic + " (" + r.VolErrRel + " of the workpiece)";
            return null;
        }

        /// <summary> the extended sheet keeps every original triangle and vertex as they were (ids, corners, positions) and only adds </summary>
        public static void AssertOriginalKept(DMesh3 original, DMesh3 extended)
        {
            Assert.True(extended.MaxVertexID >= original.MaxVertexID && extended.MaxTriangleID >= original.MaxTriangleID);
            foreach (int v in original.VertexIndices()) {
                Assert.True(extended.IsVertex(v));
                Assert.Equal(original.GetVertex(v), extended.GetVertex(v));
            }
            foreach (int t in original.TriangleIndices()) {
                Assert.True(extended.IsTriangle(t));
                Assert.Equal(original.GetTriangle(t), extended.GetTriangle(t));
            }
            Assert.True(extended.TriangleCount >= original.TriangleCount && extended.VertexCount >= original.VertexCount);
        }

        /// <summary> every vertex of the extended sheet that is not the original's is strictly outside the workpiece (the exact winding number is 0) </summary>
        public static void AssertNewVerticesOutside(DMesh3 original, DMesh3 extended, DMesh3 w)
        {
            var tree = new DMeshAABBTree3(w, true);
            foreach (int v in extended.VertexIndices()) {
                if (original.IsVertex(v)) continue;
                Vector3d p = extended.GetVertex(v);
                Assert.True(Math.Abs(tree.WindingNumber(p)) < 0.5, "new vertex " + v + " " + p + " is inside the workpiece");
                tree.FindNearestTriangle(p, out double d2);
                Assert.True(d2 > 0, "new vertex " + v + " is on the workpiece's surface");
            }
        }

        /// <summary> no degenerate or non-finite triangle was added </summary>
        public static void AssertStripTrianglesProper(DMesh3 original, DMesh3 extended)
        {
            foreach (int t in extended.TriangleIndices()) {
                if (original.IsTriangle(t)) continue;
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                extended.GetTriVertices(t, ref a, ref b, ref c);
                double area = (b - a).Cross(c - a).Length / 2;
                Assert.True(area > 0 && !double.IsNaN(area) && !double.IsInfinity(area), "strip triangle " + t + " is degenerate");
            }
        }

        /// <summary>
        /// the cut with the extended sheet and the cut with the reversed one keep volumes that add up to the workpiece's. It holds by construction for any accepted
        /// cut (design section 5): it can only catch a bug of the implementation
        /// </summary>
        public static void AssertBothSidesAddUp(SheetRun r, double tol)
        {
            DMesh3 reversed = new DMesh3(r.Result);
            reversed.ReverseOrientation();
            var meshes = new List<DMesh3> { r.W, reversed }; meshes.AddRange(r.Others);
            var roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
            foreach (var o in r.Others) roles.Add(ExactMeshBoolean.InputRole.Sheet);
            var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = true, Roles = roles, SheetUncutSide = r.Data.Uncut };
            op.Compute();
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result) ?? "");
            Assert.True(Math.Abs(Volume(r.Op.Report.Cut) + Volume(op.Result) - Math.Abs(Volume(r.W))) <= tol * Math.Abs(Volume(r.W)), "the two sides do not add up to the workpiece");
        }

        /// <summary> the same cut run independently of the helper, on the returned sheet: the sheet role accepts it, the result is clean, closed, manifold and finite </summary>
        public static DMesh3 IndependentCut(SheetRun r)
        {
            var meshes = new List<DMesh3> { r.W, r.Result }; meshes.AddRange(r.Others);
            var roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
            foreach (var o in r.Others) roles.Add(ExactMeshBoolean.InputRole.Sheet);
            var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = true, Roles = roles, SheetUncutSide = r.Data.Uncut };
            op.Compute();
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result) ?? "");
            foreach (int v in op.Result.VertexIndices()) {
                Vector3d p = op.Result.GetVertex(v);
                Assert.True(!double.IsNaN(p.x + p.y + p.z) && !double.IsInfinity(p.x + p.y + p.z));
            }
            return op.Result;
        }
    }
}
