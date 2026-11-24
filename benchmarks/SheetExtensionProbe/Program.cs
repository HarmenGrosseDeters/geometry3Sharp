using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using gs;

namespace SheetExtensionProbe
{
    sealed class Frame
    {
        public string Name;
        public Func<Vector3d, Vector3d> Map;
        public double Tol;      // relative tolerance of the volumes
    }

    sealed class Row
    {
        public string Case, Group, Frame, Expected, Actual, Reason = "", Plain = "", Clean = "", Note = "";
        public bool Pass, WrongAccepted;
        public double MaxGRel = double.NaN, U = double.NaN, URel = double.NaN, IntentErrRel = double.NaN;
        public double Kept = double.NaN, Analytic = double.NaN, VolErrRel = double.NaN, Intent = double.NaN, IntentErr = double.NaN;
        public double StripArea = double.NaN, MeanDepth = double.NaN, MinAlphaDeg = double.NaN, MaxDeltaDeg = double.NaN;
        public int Curves = -1, CurveIds = -1, Runs, ChosenEdges, SubdivisionPoints, NewTriangles;
        public double ConstructMs, VerifyMs, ImprintMs, CutMs;
        public string Witness = "";
    }

    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static List<Frame> Frames()
        {
            return new List<Frame> {
                new Frame { Name = "identity", Map = p => p, Tol = 1e-9 },
                new Frame { Name = "shift 1e2", Map = p => p + new Vector3d(100.3, -100.7, 100.11), Tol = 1e-8 },
                new Frame { Name = "shift 6e5", Map = p => p + new Vector3d(600000.3, -600000.7, 600000.11), Tol = 1e-6 },
                new Frame { Name = "shift 5.7e6", Map = p => p + new Vector3d(5700000.3, -5700000.7, 5700000.11), Tol = 1e-5 },
                new Frame { Name = "rotated", Map = Geometry.Rotation(new Vector3d(0.3, 0.5, 0.8), 37, new Vector3d(3.1, -2.2, 1.7)), Tol = 1e-8 },
            };
        }

        static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "cases";
            switch (mode) {
                case "cases": return RunCases(false, args.Skip(1).ToArray());
                case "selftest": return RunCases(true, args.Skip(1).ToArray());
                case "mutants": return RunMutants(args.Skip(1).ToArray());
                case "all": { int a = RunCases(false, new string[0]); int b = RunMutants(new string[0]); RunCost(); return a != 0 ? a : b; }
                case "file": return RunFile(args.Skip(1).ToArray());
                case "cost": return RunCost();
                default:
                    Console.WriteLine("usage: cases [filter] | selftest | mutants [filter] | all | cost | file <workpiece> <sheet> [reverse] [uncut=outside|inside]");
                    return 2;
            }
        }

        // ---------------------------------------------------------------- one case in one frame

        static DMesh3 Map(DMesh3 m, Frame f) { return f.Name == "identity" ? m : Geometry.Transformed(m, f.Map); }

        static string PlainCut(DMesh3 w, List<DMesh3> sheets, ExactMeshBoolean.SheetUncutSides uncut, out int witnessMesh)
        {
            witnessMesh = -1;
            var meshes = new List<DMesh3> { w }; meshes.AddRange(sheets);
            var roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid };
            foreach (var s in sheets) roles.Add(ExactMeshBoolean.InputRole.Sheet);
            var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = true, Roles = roles, SheetUncutSide = uncut };
            op.Compute();
            if (op.Status == ExactMeshBoolean.BooleanStatus.SheetRefused) { witnessMesh = op.SheetWitnessMesh; return "refused " + op.SheetReason + " (mesh " + op.SheetWitnessMesh + ")"; }
            return op.Status.ToString();
        }

        static ExtensionOptions Copy(ExtensionOptions b)
        {
            return new ExtensionOptions {
                MaxExtension = b.MaxExtension, MaxOvershoot = b.MaxOvershoot, MinOvershootUnits = b.MinOvershootUnits, DeltaMin = b.DeltaMin,
                AngleMargin = b.AngleMargin, MaxUOverSmallerPart = b.MaxUOverSmallerPart, MaxSubdivisionDepth = b.MaxSubdivisionDepth,
                Verify = b.Verify, Rim = b.Rim, Exit = b.Exit, Overshoot = b.Overshoot, Subdivide = b.Subdivide, FlipQuads = b.FlipQuads
            };
        }

        static Row Run(Case c, Frame f, ExtensionOptions baseOpt, bool first)
        {
            CaseData d = c.Build();
            var row = new Row { Case = c.Name, Group = c.Group, Frame = f.Name };
            DMesh3 w = Map(d.W, f);
            var sheets = d.Sheets.Select(s => Map(s, f)).ToList();
            row.Expected = c.Expect == Kind.Refused ? "refused: " + string.Join("|", c.Reasons) : c.Expect.ToString().ToLowerInvariant();
            row.Plain = PlainCut(w, sheets, d.Uncut, out int wm);

            ExtensionOptions opt = Copy(baseOpt);
            opt.UncutSide = d.Uncut; opt.OtherTools = new List<DMesh3>(); opt.OtherRoles = new List<ExactMeshBoolean.InputRole>();
            // with several sheets the sheet role names the refused one; otherwise the case says which one is short
            int k = wm > 0 ? wm - 1 : d.ShortIndex;
            if (sheets.Count > 1 && wm > 0 && wm - 1 != d.ShortIndex)
                row.Note = "the role named sheet " + (wm - 1) + ", expected " + d.ShortIndex;
            for (int i = 0; i < sheets.Count; ++i)
                if (i != k) { opt.OtherTools.Add(sheets[i]); opt.OtherRoles.Add(ExactMeshBoolean.InputRole.Sheet); }

            ExtensionReport rep;
            try { SheetExtension.TryExtend(sheets[k], w, opt, out rep); }
            catch (Exception ex) { row.Actual = "exception"; row.Reason = ex.GetType().Name + ": " + ex.Message; row.Pass = false; return row; }

            row.MaxGRel = rep.MaxDepthRelative; row.U = rep.U; row.URel = rep.URelative; row.Curves = rep.CurveCount; row.CurveIds = rep.CurveIdCount; row.Runs = rep.Runs.Count;
            row.ChosenEdges = rep.ChosenEdges; row.SubdivisionPoints = rep.SubdivisionPoints; row.NewTriangles = rep.NewTriangles;
            row.ConstructMs = rep.ConstructMs; row.VerifyMs = rep.VerifyMs; row.ImprintMs = rep.ImprintMs; row.CutMs = rep.CutMs;
            if (rep.Runs.Count > 0 && rep.Extended) {
                row.StripArea = rep.Runs.Sum(r => double.IsNaN(r.StripAreaInside) ? 0 : r.StripAreaInside);
                double len = rep.Runs.Sum(r => r.Length);
                row.MeanDepth = len > 0 ? rep.Runs.Sum(r => r.MeanDepth * r.Length) / len : 0;
                var al = rep.Runs.Where(r => !double.IsNaN(r.MinAlpha)).ToList();
                if (al.Count > 0) row.MinAlphaDeg = al.Min(r => r.MinAlpha) * 180 / Math.PI;
                row.MaxDeltaDeg = rep.Runs.Max(r => r.MaxDelta) * 180 / Math.PI;
            }
            row.Witness = rep.Witness + (rep.HasWitnessPoint ? " at (" + F(rep.WitnessPoint.x, "0.######") + ", " + F(rep.WitnessPoint.y, "0.######") + ", " + F(rep.WitnessPoint.z, "0.######") + ")" : "");
            if (rep.Refusal != ExtensionRefusal.None) {
                row.Actual = "refused: " + rep.Refusal;
                row.Reason = rep.Refusal == ExtensionRefusal.SheetRoleRefused ? rep.SheetReason.ToString() : rep.Refusal.ToString();
            }
            else row.Actual = rep.Extended ? "extended" : "unchanged";

            double analytic = d.Analytic;
            if (double.IsNaN(analytic) && d.AnalyticFromExtended != null) {
                if (first && rep.Candidate != null) c.CachedAnalytic = d.AnalyticFromExtended(rep.Candidate);
                analytic = c.CachedAnalytic;
            }
            row.Analytic = analytic;
            row.Intent = d.Intent;
            if (rep.Cut != null) {
                row.Kept = SheetExtension.Volume(rep.Cut);
                row.Clean = MeshRobustRepair.NotCleanReason(rep.Cut) ?? "";
                double vw = Math.Abs(SheetExtension.Volume(w));
                if (!double.IsNaN(analytic)) row.VolErrRel = (row.Kept - analytic) / vw;
                if (!double.IsNaN(d.Intent)) { row.IntentErrRel = (row.Kept - d.Intent) / vw; row.IntentErr = row.Kept - d.Intent; }
            }
            // a curved sheet: the strip's inner part depends on how a non-planar corner quad is split (the subdivision pattern follows the frame), far below U
            bool volOk = double.IsNaN(row.VolErrRel) || Math.Abs(row.VolErrRel) <= (d.AnalyticFromExtended != null ? Math.Max(f.Tol, 1e-6) : f.Tol);
            bool cleanOk = row.Clean == "";
            bool refusedOk = row.Actual.StartsWith("refused") && (c.Expect == Kind.Any || (c.Expect == Kind.Refused && c.Reasons.Any(r => row.Actual == "refused: " + r)));
            bool accepted = row.Actual == "extended" || row.Actual == "unchanged";
            if (refusedOk) row.Pass = true;
            else if (accepted) {
                bool kindOk = c.Expect == Kind.Any || (c.Expect == Kind.Extended && row.Actual == "extended") || (c.Expect == Kind.Unchanged && row.Actual == "unchanged");
                row.Pass = kindOk && volOk && cleanOk;
                row.WrongAccepted = !row.Pass;
            }
            return row;
        }

        // ---------------------------------------------------------------- output

        static string F(double v, string fmt = "R") { return double.IsNaN(v) ? "" : v.ToString(fmt, Inv); }

        static string Json(Row r)
        {
            var sb = new StringBuilder("{");
            void s(string k, string v, bool last = false) { sb.Append('"').Append(k).Append("\":\"").Append((v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'); if (!last) sb.Append(','); }
            void n(string k, double v) { sb.Append('"').Append(k).Append("\":").Append(double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("R", Inv)).Append(','); }
            s("case", r.Case); s("group", r.Group); s("frame", r.Frame); s("expected", r.Expected); s("actual", r.Actual); s("reason", r.Reason);
            sb.Append("\"pass\":").Append(r.Pass ? "true" : "false").Append(",\"wrongAccepted\":").Append(r.WrongAccepted ? "true" : "false").Append(',');
            n("maxDepthOverDiagonal", r.MaxGRel); n("U", r.U); n("UOverSmallerPart", r.URel); n("keptVolume", r.Kept); n("analyticVolume", r.Analytic); n("volumeErrorOverWorkpiece", r.VolErrRel);
            n("stripAreaInside", r.StripArea); n("meanDepth", r.MeanDepth); n("minExitAngleDeg", r.MinAlphaDeg); n("maxDeltaDeg", r.MaxDeltaDeg); n("intentVolume", r.Intent); n("keptMinusIntent", r.IntentErr);
            n("constructMs", r.ConstructMs); n("verifyMs", r.VerifyMs); n("imprintMs", r.ImprintMs); n("cutMs", r.CutMs);
            sb.Append("\"curves\":").Append(r.Curves).Append(",\"curveIds\":").Append(r.CurveIds).Append(",\"runs\":").Append(r.Runs).Append(",\"chosenEdges\":").Append(r.ChosenEdges)
              .Append(",\"subdivisionPoints\":").Append(r.SubdivisionPoints).Append(",\"newTriangles\":").Append(r.NewTriangles).Append(',');
            s("plainSheetRole", r.Plain); s("notClean", r.Clean); s("witness", r.Witness, true);
            return sb.Append('}').ToString();
        }

        static string Md(IEnumerable<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| case | frame | expected | actual | ok | g max / diag | U | U / smaller | kept | analytic | vol err / W | kept - intent | curves | construct ms | verify ms | imprint ms | cut ms |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (Row r in rows)
                sb.AppendLine("| " + r.Case + " | " + r.Frame + " | " + r.Expected + " | " + r.Actual + " | " + (r.Pass ? "yes" : "**NO**") + " | " + F(r.MaxGRel, "0.0000") + " | " + F(r.U, "0.0000") + " | " + F(r.URel, "0.00000")
                    + " | " + F(r.Kept, "0.0000") + " | " + F(r.Analytic, "0.0000") + " | " + F(r.VolErrRel, "0.0e+0") + " | " + F(r.IntentErr, "0.0000") + " | " + (r.Curves < 0 ? "" : r.Curves.ToString()) + " | "
                    + F(r.ConstructMs, "0.0") + " | " + F(r.VerifyMs, "0.0") + " | " + F(r.ImprintMs, "0.0") + " | " + F(r.CutMs, "0.0") + " |");
            return sb.ToString();
        }

        static string ResultsDir()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; ++i) {
                if (File.Exists(Path.Combine(dir, "SheetExtensionProbe.csproj"))) return Path.Combine(dir, "results");
                dir = Path.GetDirectoryName(dir);
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "results");
        }

        // ---------------------------------------------------------------- the modes

        static int RunCases(bool selftest, string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            var rows = new List<Row>();
            int failed = 0;
            foreach (Case c in Cases.All()) {
                if (filter != null && c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool first = true;
                foreach (Frame f in Frames()) {
                    bool passing = c.Expect == Kind.Extended || c.Expect == Kind.Unchanged;
                    if (f.Name != "identity" && (!c.Frames || !passing)) continue;
                    Row r = Run(c, f, new ExtensionOptions(), first);
                    first = false;
                    rows.Add(r);
                    if (!r.Pass) ++failed;
                    Console.WriteLine((r.Pass ? "ok   " : "FAIL ") + c.Name + " [" + f.Name + "]: " + r.Actual + (r.Pass ? "" : "   (expected " + r.Expected + "; " + r.Witness + " " + r.Reason + ")")
                        + "  g/D=" + F(r.MaxGRel, "0.0000") + " U=" + F(r.U, "0.0000") + " kept=" + F(r.Kept, "0.0000") + " analytic=" + F(r.Analytic, "0.0000") + " curves=" + r.Curves + " plain=" + r.Plain
                        + (r.Clean != "" ? " NOTCLEAN " + r.Clean : ""));
                }
            }
            if (selftest || filter == null) {
                int bad = Invariance(filter);
                failed += bad;
            }
            Console.WriteLine(failed == 0 ? "all " + rows.Count + " rows as expected" : failed + " of " + rows.Count + " rows NOT as expected");
            if (!selftest && filter == null) {
                string dir = ResultsDir(); Directory.CreateDirectory(dir);
                File.WriteAllLines(Path.Combine(dir, "cases.jsonl"), rows.Select(Json));
                File.WriteAllText(Path.Combine(dir, "cases.md"), Md(rows));
                Console.WriteLine("written to " + dir);
            }
            return failed == 0 ? 0 : 1;
        }

        // the extension under a scaling by a power of two: the same outcome, and every vertex of the extended sheet bit for bit the scaled one
        static int Invariance(string filter)
        {
            int bad = 0, n = 0;
            foreach (Case c in Cases.All()) {
                if (filter != null && c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (c.Expect != Kind.Extended && c.Expect != Kind.Unchanged && c.Expect != Kind.Refused) continue;
                CaseData d = c.Build();
                if (d.Sheets.Count != 1) continue;
                var o1 = new ExtensionOptions { UncutSide = d.Uncut };
                DMesh3 e1 = SheetExtension.TryExtend(d.Sheets[0], d.W, o1, out ExtensionReport r1);
                // a second run on the same inputs (reuse, repeated runs): the same outcome and the same vertices
                DMesh3 e1b = SheetExtension.TryExtend(d.Sheets[0], d.W, new ExtensionOptions { UncutSide = d.Uncut }, out ExtensionReport r1b);
                ++n;
                if (r1b.Refusal != r1.Refusal || (e1 == null) != (e1b == null) || (e1 != null && (e1.VertexCount != e1b.VertexCount || e1.TriangleCount != e1b.TriangleCount || e1.VertexIndices().Any(v => e1.GetVertex(v) != e1b.GetVertex(v)))))
                { ++bad; Console.WriteLine("FAIL repeated run differs: " + c.Name); }
                foreach (int k in new[] { -7, 5, 20 }) {
                    double sc = Math.Pow(2, k);
                    Func<Vector3d, Vector3d> f = p => p * sc;
                    DMesh3 w = Geometry.Transformed(d.W, f), sh = Geometry.Transformed(d.Sheets[0], f);
                    var o2 = new ExtensionOptions { UncutSide = d.Uncut };
                    DMesh3 e2 = SheetExtension.TryExtend(sh, w, o2, out ExtensionReport r2);
                    ++n;
                    string why = null;
                    if (r1.Refusal != r2.Refusal) why = "outcome " + r1.Refusal + " vs " + r2.Refusal;
                    else if ((e1 == null) != (e2 == null)) why = "null mismatch";
                    else if (e1 != null && e1.MaxVertexID == e2.MaxVertexID && e1.MaxTriangleID == e2.MaxTriangleID) {
                        foreach (int v in e1.VertexIndices()) {
                            Vector3d a = e1.GetVertex(v) * sc, b = e2.GetVertex(v);
                            if (a.x != b.x || a.y != b.y || a.z != b.z) { why = "vertex " + v + " differs"; break; }
                        }
                    }
                    else if (e1 != null) why = "different ids";
                    if (why == null && Math.Abs(r1.U * sc * sc * sc - r2.U) > 1e-9 * Math.Abs(r2.U)) why = "U not scaled";
                    if (why != null) { ++bad; Console.WriteLine("FAIL invariance " + c.Name + " scale 2^" + k + ": " + why); }
                }
            }
            Console.WriteLine(bad == 0 ? "repeated runs and scaling by 2^k: " + n + " runs, all bit for bit" : bad + " of " + n + " invariance runs differ");
            return bad;
        }

        sealed class Mutant { public string Name; public Action<ExtensionOptions> Apply; }

        static List<Mutant> Mutants()
        {
            Func<Rules, Mutant> off = r => new Mutant { Name = "rule off: " + r, Apply = o => o.Verify &= ~r };
            return new List<Mutant> {
                off(Rules.Depth), off(Rules.Grazing), off(Rules.NewPointOutside), off(Rules.InvertedQuad), off(Rules.OuterEdge), off(Rules.CurveRule),
                off(Rules.CoplanarContact), off(Rules.MeetsSheet), off(Rules.MeetsOtherTool),
                new Mutant { Name = "last exit instead of first", Apply = o => o.Exit = ExitChoice.Last },
                new Mutant { Name = "fixed overshoot (o_max) instead of the adaptive rule", Apply = o => o.Overshoot = OvershootRule.Fixed },
                new Mutant { Name = "choice by vertex instead of by edge", Apply = o => o.Rim = RimChoice.ByVertex },
                new Mutant { Name = "no subdivision of the outer edge", Apply = o => o.Subdivide = false },
                new Mutant { Name = "quads oriented against the sheet", Apply = o => o.FlipQuads = true },
                new Mutant { Name = "fixed overshoot and curve rule off", Apply = o => { o.Overshoot = OvershootRule.Fixed; o.Verify &= ~Rules.CurveRule; } },
                new Mutant { Name = "last exit and curve rule off", Apply = o => { o.Exit = ExitChoice.Last; o.Verify &= ~Rules.CurveRule; } },
                new Mutant { Name = "inverted-quad test and self-intersection test off", Apply = o => o.Verify &= ~(Rules.InvertedQuad | Rules.MeetsSheet) },
                new Mutant { Name = "fixed overshoot, new-point test and curve rule off", Apply = o => { o.Overshoot = OvershootRule.Fixed; o.Verify &= ~(Rules.NewPointOutside | Rules.CurveRule); } },
                new Mutant { Name = "new-point test and coplanar test off", Apply = o => o.Verify &= ~(Rules.NewPointOutside | Rules.CoplanarContact) },
                new Mutant { Name = "new-point test and curve rule off", Apply = o => o.Verify &= ~(Rules.NewPointOutside | Rules.CurveRule) },
            };
        }

        static int RunMutants(string[] args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| mutant | cases that no longer end as expected | wrong accepted cut (kept vs analytic) | still acceptable, but another reason |");
            sb.AppendLine("|---|---|---|---|");
            var lines = new List<string>();
            var baseline = new Dictionary<string, string>();
            foreach (Case c in Cases.All())
                baseline[c.Name] = Run(c, Frames()[0], new ExtensionOptions(), true).Actual;
            foreach (Mutant m in Mutants()) {
                var opt = new ExtensionOptions(); m.Apply(opt);
                var bad = new List<string>(); var wrong = new List<string>(); var moved = new List<string>();
                foreach (Case c in Cases.All()) {
                    Row r = Run(c, Frames()[0], opt, true);
                    if (r.Pass) {
                        if (r.Actual != baseline[c.Name]) moved.Add(c.Name + " -> " + r.Actual);
                        continue;
                    }
                    bad.Add(c.Name + " -> " + r.Actual);
                    if (r.WrongAccepted) wrong.Add(c.Name + " (" + F(r.Kept, "0.####") + " vs " + (double.IsNaN(r.Analytic) ? "refusal expected" : F(r.Analytic, "0.####")) + ")");
                }
                Console.WriteLine(m.Name + ": " + (bad.Count == 0 ? "no case fails" : string.Join("; ", bad)) + (wrong.Count > 0 ? "   WRONG ACCEPTED: " + string.Join("; ", wrong) : "")
                    + (moved.Count > 0 ? "   MOVED: " + string.Join("; ", moved) : ""));
                sb.AppendLine("| " + m.Name + " | " + (bad.Count == 0 ? "none" : string.Join("; ", bad)) + " | " + (wrong.Count == 0 ? "none" : string.Join("; ", wrong)) + " | " + (moved.Count == 0 ? "none" : string.Join("; ", moved)) + " |");
                lines.Add("{\"mutant\":\"" + m.Name + "\",\"failing\":" + bad.Count + ",\"wrongAccepted\":" + wrong.Count + ",\"moved\":" + moved.Count + "}");
            }
            if (args.Length == 0) {
                string dir = ResultsDir(); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "mutants.md"), sb.ToString());
                File.WriteAllLines(Path.Combine(dir, "mutants.jsonl"), lines);
            }
            return 0;
        }

        // the cost of the construction against the cut: a sphere of growing resolution, a planar sheet z = 0.5 whose far edge ends 4.5 from the centre
        // (a chord: subdivided), a grid of k x k cells; the median of 5 runs after one warm-up
        static int RunCost()
        {
            var sb = new StringBuilder();
            sb.AppendLine("| sphere triangles | sheet triangles | strip triangles | subdivision points | construct ms | verify ms (without imprint) | imprint ms | cut ms | imprint / cut |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (int[] cfg in new[] { new[] { 14, 12 }, new[] { 30, 24 }, new[] { 60, 48 } }) {
                DMesh3 sphere = Geometry.Sphere(Vector3d.Zero, 5, cfg[0]);
                DMesh3 sheet = Geometry.Grid(Geometry.Lin(-8, 8, cfg[1]), Geometry.Lin(-8, 4.5, cfg[1]), (x, y) => 0.5, true);
                var cons = new List<double>(); var ver = new List<double>(); var imp = new List<double>(); var cut = new List<double>();
                ExtensionReport last = null;
                for (int i = 0; i < 6; ++i) {
                    SheetExtension.TryExtend(sheet, sphere, new ExtensionOptions(), out ExtensionReport rep);
                    last = rep;
                    if (i == 0) continue;
                    cons.Add(rep.ConstructMs); ver.Add(rep.VerifyMs); imp.Add(rep.ImprintMs); cut.Add(rep.CutMs);
                }
                Func<List<double>, double> med = l => { var q = l.OrderBy(x => x).ToList(); return q[q.Count / 2]; };
                sb.AppendLine("| " + sphere.TriangleCount + " | " + sheet.TriangleCount + " | " + last.NewTriangles + " | " + last.SubdivisionPoints + " | " + F(med(cons), "0.0") + " | " + F(med(ver), "0.0") + " | " + F(med(imp), "0.0") + " | " + F(med(cut), "0.0")
                    + " | " + F(med(imp) / med(cut), "0.00") + " |" + (last.Refusal != ExtensionRefusal.None ? " REFUSED " + last.Refusal : ""));
                Console.WriteLine(sb.ToString().Split('\n').Reverse().Skip(1).First());
            }
            string dir = ResultsDir(); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "cost.md"), sb.ToString());
            return 0;
        }

        static DMesh3 Read(string path)
        {
            var b = new DMesh3Builder();
            var r = StandardMeshReader.ReadFile(path, new ReadOptions { }, b);
            if (r.code != IOCode.Ok || b.Meshes.Count == 0) throw new Exception("cannot read " + path + ": " + r.message);
            return b.Meshes[0];
        }

        static int RunFile(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("file <workpiece> <sheet> [reverse] [uncut=outside|inside]"); return 2; }
            DMesh3 w = Read(args[0]), s = Read(args[1]);
            var uncut = ExactMeshBoolean.SheetUncutSides.Refuse;
            foreach (string a in args.Skip(2)) {
                if (a == "reverse") s = Geometry.Reversed(s);
                else if (a == "uncut=outside") uncut = ExactMeshBoolean.SheetUncutSides.Outside;
                else if (a == "uncut=inside") uncut = ExactMeshBoolean.SheetUncutSides.Inside;
            }
            string plain = PlainCut(w, new List<DMesh3> { s }, uncut, out int wm);
            var opt = new ExtensionOptions { UncutSide = uncut };
            SheetExtension.TryExtend(s, w, opt, out ExtensionReport rep);
            var row = new Row { Case = Path.GetFileName(args[1]), Group = "file", Frame = "as given", Expected = "-", Plain = plain };
            row.Actual = rep.Refusal != ExtensionRefusal.None ? "refused: " + rep.Refusal : (rep.Extended ? "extended" : "unchanged");
            row.Reason = rep.Refusal == ExtensionRefusal.SheetRoleRefused ? rep.SheetReason.ToString() : "";
            row.MaxGRel = rep.MaxDepthRelative; row.U = rep.U; row.URel = rep.URelative; row.Curves = rep.CurveCount; row.Runs = rep.Runs.Count;
            row.ConstructMs = rep.ConstructMs; row.VerifyMs = rep.VerifyMs; row.ImprintMs = rep.ImprintMs; row.CutMs = rep.CutMs; row.Witness = rep.Witness;
            row.ChosenEdges = rep.ChosenEdges; row.SubdivisionPoints = rep.SubdivisionPoints; row.NewTriangles = rep.NewTriangles;
            if (rep.Cut != null) { row.Kept = SheetExtension.Volume(rep.Cut); row.Clean = MeshRobustRepair.NotCleanReason(rep.Cut) ?? ""; }
            row.Pass = rep.Refusal == ExtensionRefusal.None;
            Console.WriteLine("plain sheet role: " + plain);
            Console.WriteLine(Json(row));
            foreach (RunReport r in rep.Runs)
                Console.WriteLine("  run " + r.Id + ": edges " + r.Edges + ", length " + F(r.Length) + ", g max " + F(r.MaxDepth) + ", g mean " + F(r.MeanDepth) + ", strip area inside " + F(r.StripAreaInside)
                    + ", min alpha " + F(r.MinAlpha * 180 / Math.PI, "0.##") + " deg, max delta " + F(r.MaxDelta * 180 / Math.PI, "0.##") + " deg, U " + F(r.U) + ", U/smaller " + F(r.URelative) + ", curves " + r.CurveComponents);
            return 0;
        }
    }
}
