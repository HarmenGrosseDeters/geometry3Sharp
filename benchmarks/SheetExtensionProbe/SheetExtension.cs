using System;
using System.Collections.Generic;
using g3;
using gs;

// The probe's view of the library helper gs.MeshSheetExtension (mesh_ops/MeshSheetExtension.cs). The construction used to live in this file
// (the spike, docs/research/SHEET_EXTENSION_SPIKE_2026-11.md); it is now the library's, and this file keeps the probe's option, report and refusal
// types so that the cases, the mutants and the reports of the spike run unchanged against it. The mutant switches are the library's internal test
// switches (InternalsVisibleTo).
namespace SheetExtensionProbe
{
    /// <summary> why the extension was refused (None: extended, or nothing to extend); the names are those of MeshSheetExtension.ExtensionStatus </summary>
    public enum ExtensionRefusal
    {
        None = 0, InvalidInput, BowtieVertex, NoDirection, NoExit, DepthAboveLimit, GrazingExit, NewPointNotOutside, OuterEdgeInside, InvertedQuad, CurveRule,
        CoplanarContact, MeetsSheet, MeetsItself, MeetsOtherTool, ImprintFailed, ConstructionFailed, SheetRoleRefused, CutFailed, UncertaintyAboveLimit,
        WorkpieceNotClosed, SheetNotSurface
    }

    /// <summary> the verifications that can be switched off (for the mutation runs of the probe; all on by default) </summary>
    [Flags]
    public enum Rules
    {
        None = 0, Depth = 1, Grazing = 2, NewPointOutside = 4, InvertedQuad = 8, OuterEdge = 16, CurveRule = 32, CoplanarContact = 64,
        MeetsSheet = 128, MeetsOtherTool = 256, All = 511
    }

    public enum RimChoice { ByEdge, ByVertex }
    public enum ExitChoice { First, Last }
    public enum OvershootRule { Adaptive, Fixed }

    public sealed class ExtensionOptions
    {
        public double MaxExtension = 0.05, MaxOvershoot = 0.01, MinOvershootUnits = 1024, DeltaMin = 0.05, AngleMargin = 0.05;
        public double MaxUOverSmallerPart = double.PositiveInfinity;
        public int MaxSubdivisionDepth = 14;
        public Rules Verify = Rules.All;
        public RimChoice Rim = RimChoice.ByEdge;
        public ExitChoice Exit = ExitChoice.First;
        public OvershootRule Overshoot = OvershootRule.Adaptive;
        public bool Subdivide = true;
        public bool FlipQuads = false;
        public IList<DMesh3> OtherTools;
        public IList<ExactMeshBoolean.InputRole> OtherRoles;
        public ExactMeshBoolean.SheetUncutSides UncutSide = ExactMeshBoolean.SheetUncutSides.Refuse;
        public bool RunCut = true;
    }

    public sealed class RunReport
    {
        public int Id, Edges;
        public double Length, InsideLength, MaxDepth, MeanDepth, MinAlpha = double.NaN, MaxDelta, U, URelative = double.NaN;
        public double StripAreaInside = double.NaN;
        public int CurveComponents = -1, CurveIds = -1;
        public ExtensionRefusal Refusal;
        public string Witness = "";
    }

    public sealed class ExtensionReport
    {
        public ExtensionRefusal Refusal;
        public string Witness = "";
        public Vector3d WitnessPoint;
        public bool HasWitnessPoint, Extended;
        public double Diagonal;
        public List<RunReport> Runs = new List<RunReport>();
        public int ChosenEdges, NewVertices, NewTriangles, SubdivisionPoints;
        public double MaxDepth, MaxDepthRelative, U, SmallerPart = double.NaN, URelative = double.NaN;
        public int CurveCount, CurveIdCount;
        public DMesh3 Candidate;
        public ExactMeshBoolean.SheetRefusal SheetReason;
        public string CutStatus = "";
        public DMesh3 Cut;
        public double ConstructMs, VerifyMs, ImprintMs, CutMs;
    }

    public static class SheetExtension
    {
        public static DMesh3 TryExtend(DMesh3 sheet, DMesh3 workpiece, ExtensionOptions options, out ExtensionReport report)
        {
            ExtensionOptions o = options ?? new ExtensionOptions();
            var op = new MeshSheetExtension {
                Sheet = sheet, Workpiece = workpiece, MaxExtension = o.MaxExtension, MaxOvershoot = o.MaxOvershoot, MinOvershootUnits = o.MinOvershootUnits,
                DeltaMin = o.DeltaMin, AngleMargin = o.AngleMargin, MaxUOverSmallerPart = o.MaxUOverSmallerPart, MaxSubdivisionDepth = o.MaxSubdivisionDepth,
                OtherTools = o.OtherTools, OtherRoles = o.OtherRoles, UncutSide = o.UncutSide, VerifyWithSheetRole = o.RunCut,
                Verify = (MeshSheetExtension.Rules)(int)o.Verify, ChooseByVertex = o.Rim == RimChoice.ByVertex, UseLastExit = o.Exit == ExitChoice.Last,
                FixedOvershoot = o.Overshoot == OvershootRule.Fixed, NoSubdivision = !o.Subdivide, FlipQuads = o.FlipQuads
            };
            op.Compute();
            SheetExtensionReport r = op.Report;
            var rep = new ExtensionReport {
                Refusal = Map(op.Status), Witness = r.Witness, WitnessPoint = r.WitnessPoint, HasWitnessPoint = r.HasWitnessPoint, Extended = r.Extended, Diagonal = r.Diagonal,
                ChosenEdges = r.ChosenEdges, NewVertices = r.NewVertices, NewTriangles = r.NewTriangles, SubdivisionPoints = r.SubdivisionPoints, MaxDepth = r.MaxDepth,
                MaxDepthRelative = r.MaxDepthRelative, U = r.U, SmallerPart = r.SmallerPart, URelative = r.URelative, CurveCount = r.CurveCount, CurveIdCount = r.CurveIdCount,
                Candidate = r.Candidate, SheetReason = r.SheetReason, CutStatus = r.CutStatus, Cut = r.Cut, ConstructMs = r.ConstructMs, VerifyMs = r.VerifyMs, ImprintMs = r.ImprintMs, CutMs = r.CutMs
            };
            foreach (SheetExtensionRun x in r.Runs)
                rep.Runs.Add(new RunReport {
                    Id = x.Id, Edges = x.Edges, Length = x.Length, InsideLength = x.InsideLength, MaxDepth = x.MaxDepth, MeanDepth = x.MeanDepth, MinAlpha = x.MinAlpha, MaxDelta = x.MaxDelta,
                    U = x.U, URelative = x.URelative, StripAreaInside = x.StripAreaInside, CurveComponents = x.CurveComponents, CurveIds = x.CurveIds, Refusal = Map(x.Refusal), Witness = x.Witness
                });
            report = rep;
            return op.Result;
        }

        static ExtensionRefusal Map(MeshSheetExtension.ExtensionStatus s)
        {
            return s == MeshSheetExtension.ExtensionStatus.Success ? ExtensionRefusal.None : (ExtensionRefusal)Enum.Parse(typeof(ExtensionRefusal), s.ToString());
        }

        public static double Volume(DMesh3 mesh) { return MeshSheetExtension.Volume(mesh); }
    }
}
