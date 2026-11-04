using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using g3;
using g3cli;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The "mesh" command: our mesher on one case, written in the plain text output format of
    /// docs/design/MESHING2D_GOLDEN_FORMAT.md, section 4 (or as Triangle .node / .ele / .poly files), so that a reference session can
    /// compare its output with ours. It uses the candidate and the oracle expectations of the golden rows
    /// (CandidateRegistry.For(milestone), the case's Expect), so the fingerprints it prints are those of our golden row of the case.
    /// The whole command is here, not in Program, so that the tests run it as the command line does.
    /// </summary>
    public static class MeshCommand
    {
        /// <summary>
        /// The expectations of the `oracle` command: the milestone, the tube (null or "auto": the oracle's own), and the area the case
        /// states with its tolerance (a seed-only hole removes a cell of the input constraints, which the oracle does not compute: such a
        /// case must state its area, eg F-17/b, or the verdict is CannotJudge)
        /// </summary>
        public static OracleExpect OracleExpectation(Milestone m, string tube, string statedArea, string statedAreaTolerance)
        {
            var e = new OracleExpect { Milestone = m };
            if (tube != null && tube != "auto") e.Tube = Q.FromDouble(TextFormat.ParseCoordinate(tube));
            if (statedArea != null) e.StatedArea = Q.FromDouble(TextFormat.ParseCoordinate(statedArea));
            if (statedAreaTolerance != null) e.StatedAreaTolerance = Q.FromDouble(TextFormat.ParseCoordinate(statedAreaTolerance));
            return e;
        }

        /// <summary>
        /// for before/after measurements on the calling thread: G3_M2_OFFCENTRES (0 or 1) switches off-centres, G3_M2_CONFORMING (0 or 1)
        /// conforming Delaunay in the M3 candidate (what O2 costs), G3_M2_LENSES (0 or 1) diametral lenses regardless of the rule,
        /// G3_M2_KEEPAPART (0 or 1) the refinement rule of #360 that keeps chains apart (0: the refinement before it);
        /// G3_M2_LENS_DELETION (0 or 1) Chew's deletion before a bisection with lenses; G3_M2_PINCHFREE (0 or 1) the pinch rule (a split
        /// point is never another chain's vertex of refinement); unset keeps the defaults. G3_M2_COUNTS (mesh command) prints what
        /// refinement inserted and refused, by cause
        /// </summary>
        public static void ApplyRefinementVariant()
        {
            bool? Flag(string name) { var v = Environment.GetEnvironmentVariable(name); return v == null ? (bool?)null : v != "0"; }
            g3.QualityMesh2.Refiner.OffCentresForBenchmarks = Flag("G3_M2_OFFCENTRES");
            QualityMesh2Candidate.ConformingForBenchmarks = Flag("G3_M2_CONFORMING");
            g3.QualityMesh2.Refiner.LensesForBenchmarks = Flag("G3_M2_LENSES");
            g3.QualityMesh2.Refiner.DeleteInCircleForBenchmarks = Flag("G3_M2_LENS_DELETION");
            g3.QualityMesh2.Refiner.KeepPinchFreeForTests = Flag("G3_M2_PINCHFREE");
            g3.QualityMesh2.Refiner.KeepChainsApartForTests = Flag("G3_M2_KEEPAPART");
        }

        public const string Usage =
            "mesh --case <id> --output <file>\n" +
            "mesh --input <in.txt|in.poly|in.node> --output <file> [--milestone M1|M2|M3] [--min-angle <deg>] [--max-area <a>] [--steiner-limit <n>] [--snap-tolerance <t>]\n" +
            "  --no-oracle (either form): mesh and write only, no oracle and no self-check (timing the mesher; exit 0 when written); the times are printed\n" +
            "  output: the plain text output format, or Triangle files when <file> ends in .node, .ele or .poly\n" +
            "  exit code: 0 written, valid and equal to the golden row (or none recorded); 1 written but invalid or different, or the mesher\n" +
            "  threw or failed where Triangle files were asked for; 2 wrong usage, unreadable input, unwritable output, an option or input the\n" +
            "  mesher refuses";

        public sealed class Result
        {
            public Mesh2Case Case;
            public string Producer;
            public Mesh2Output Output;
            public OracleReport Report;
            /// <summary> the row of section 5 for this output (reason empty) </summary>
            public GoldenRow Row;
            /// <summary> the output in the text format of section 4 </summary>
            public string Text;
            /// <summary> false with --no-oracle: the report is empty, nothing was judged </summary>
            public bool Judged = true;
            /// <summary> wall-clock time of the mesher (on its thread) and of the oracle, milliseconds </summary>
            public double MeshMs, OracleMs;
        }

        /// <summary>
        /// The milestone a text input asks for when none is given: M3 with a quality option (minimum angle, area, size, Steiner limit,
        /// conforming, a region with an area), M2 with constraints (segments, rings, hole seeds, regions), noding switched off or an
        /// explicit snap tolerance, otherwise M1.
        /// </summary>
        public static Milestone InferMilestone(Mesh2Input input)
        {
            if (input.MinAngleDeg != null || input.MaxArea != null || input.SizeConstant != null || input.SizeFunction != null
                || input.SteinerLimit != null || input.Conforming || input.Regions.Any(r => r.MaxArea != null))
                return Milestone.M3;
            if (input.Segments.Count > 0 || input.Rings.Count > 0 || input.HoleSeeds.Count > 0 || input.Regions.Count > 0 || !input.ResolveCrossings
                || input.SnapTolerance != 0)
                return Milestone.M2;
            return Milestone.M1;
        }

        /// <summary>
        /// The case for an input. An input equal to a known case (the same canonical text, so the same input_sha256) is that case, with
        /// its id, milestone and oracle expectations (eg F-9, where a failure is required), unless another milestone is asked for. Any
        /// other input is judged with the default expectations of its milestone (default: inferred), which know nothing a case states.
        /// </summary>
        public static Mesh2Case FromInput(Mesh2Input input, string id, Milestone? milestone, IEnumerable<Mesh2Case> known = null)
        {
            string sha = TextFormat.InputSha256(input);
            var match = known?.FirstOrDefault(c => (milestone == null || c.Milestone == milestone) && TextFormat.InputSha256(c.Input) == sha);
            if (match != null) return match;
            var m = milestone ?? InferMilestone(input);
            return new Mesh2Case { Id = id, Milestone = m, Input = input, Generator = "text:" + id, Expect = new OracleExpect { Milestone = m } };
        }

        /// <summary> A Triangle input as the gates see it (the heights are not part of it). </summary>
        public static Mesh2Input FromTriangle(ConstrainedMesh2Input c)
        {
            var input = new Mesh2Input { X = c.Points.Select(p => p.x).ToArray(), Y = c.Points.Select(p => p.y).ToArray() };
            foreach (var s in c.Segments) input.Segments.Add(new SegmentInput(s.A, s.B, s.Marker, s.Locked));
            foreach (var r in c.Rings) input.Rings.Add(new RingInput(r.Vertices, r.Hole, r.Marker));
            foreach (var h in c.HoleSeeds) input.HoleSeeds.Add(new[] { h.x, h.y });
            foreach (var g in c.Regions)
                input.Regions.Add(new RegionInput { Label = g.Label, MaxArea = g.MaxArea, Ring = g.Ring, Seed = g.Ring == null ? new[] { g.Seed.x, g.Seed.y } : null });
            return input;
        }

        /// <summary> Runs the candidate of the case's milestone (on a thread with a large stack, as `rows` does), judges the result and makes its row and text. </summary>
        public static Result Run(Mesh2Case c, bool judge = true)
        {
            var candidate = CandidateRegistry.For(c.Milestone) ?? throw new InvalidOperationException("no candidate for " + c.Milestone);
            Mesh2Output output = null; Exception error = null;
            long[] counts = Environment.GetEnvironmentVariable("G3_M2_COUNTS") != null ? new long[7] : null;
            long deleted = 0;
            var thread = new System.Threading.Thread(() => {
                try { MeshCommand.ApplyRefinementVariant(); g3.QualityMesh2.Refiner.CountsForBenchmarks = counts; output = candidate.Run(c.Input); deleted = g3.QualityMesh2.Refiner.DeletedForTests; }
                catch (Exception e) { error = e; }
                finally { g3.QualityMesh2.Refiner.CountsForBenchmarks = null; }
            }, 256 << 20);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            thread.Start();
            thread.Join();
            double meshMs = clock.Elapsed.TotalMilliseconds;
            if (error != null) throw new MesherException(candidate.Name, error);
            if (counts != null)
                Console.Error.WriteLine("refinement counts: " + counts[0] + " subsegments split as encroached, " + counts[1] + " for a point behind them, " + counts[2]
                    + " encroached by a point, " + counts[3] + " circumcentres, " + counts[4] + " off-centres, " + counts[5] + " grid-blocked triangles, " + counts[6]
                    + " subsegments unsplittable on the grid, " + deleted + " free vertices deleted before bisections (lenses)");
            clock.Restart();
            var report = judge ? Oracle.Check(c.Input, output, c.Expect) : new OracleReport();
            double oracleMs = clock.Elapsed.TotalMilliseconds;
            return new Result {
                Case = c, Producer = candidate.Name, Output = output, Report = report, Judged = judge, MeshMs = meshMs, OracleMs = oracleMs,
                Row = GoldenRow.From(c, candidate.Name, output, report, ""),
                Text = TextFormat.WriteOutput(output, candidate.Name),
            };
        }

        /// <summary> The mesher threw (a bug of the mesher, not of the command). </summary>
        public sealed class MesherException : Exception
        {
            public MesherException(string candidate, Exception inner) : base(candidate + " threw " + inner.GetType().Name + ": " + inner.Message, inner) { }
        }

        /// <summary> The recorded golden row of a case in the golden file of its milestone, or null. </summary>
        public static GoldenRow FindGolden(string goldenDir, Mesh2Case c)
        {
            if (goldenDir == null) return null;
            string path = Path.Combine(goldenDir, c.Milestone.ToString().ToLowerInvariant() + ".json");
            if (!File.Exists(path)) return null;
            return GoldenFile.Parse(File.ReadAllText(path)).Rows.FirstOrDefault(r => r.Case == c.Id);
        }

        /// <summary> The lines printed to stdout: the row's fingerprints and counts, and how they compare with the golden row. </summary>
        public static IEnumerable<string> Summary(Result r, GoldenRow golden)
        {
            yield return "case " + r.Case.Id + " " + r.Case.Milestone + " producer " + r.Producer + " input_sha256 " + r.Row.InputSha256;
            yield return "status " + r.Row.Status + ", " + r.Row.Triangles + " triangles, " + r.Row.Vertices + " vertices, " + r.Row.Subsegments + " subsegments, "
                + r.Row.ConstructedVertices + " constructed, unique " + r.Row.Unique.ToString().ToLowerInvariant();
            yield return "triangles_fp " + r.Row.TrianglesFp;
            yield return "subsegments_fp " + r.Row.SubsegmentsFp;
            yield return "vertices_fp " + r.Row.VerticesFp;
            if (r.Row.IdentityFp != null) yield return "identity_fp " + r.Row.IdentityFp;
            yield return "area2 " + r.Row.Area2;
            yield return "min_angle_sin2 " + (r.Row.MinAngleSin2 ?? "-");
            yield return "time: mesh " + r.MeshMs.ToString("F0", CultureInfo.InvariantCulture) + " ms, oracle " + (r.Judged ? r.OracleMs.ToString("F0", CultureInfo.InvariantCulture) + " ms" : "not run");
            if (!r.Judged) { yield return "oracle: not run (--no-oracle), no golden comparison"; yield break; }
            yield return "oracle " + (r.Report.Ok ? "VALID" : "INVALID: " + r.Report);
            if (golden == null) {
                yield return "golden: no recorded row for " + r.Case.Id;
            } else {
                var differences = golden.Differences(r.Row).ToList();
                yield return differences.Count == 0 ? "golden: same as the recorded row" : "golden: DIFFERS: " + string.Join("; ", differences);
            }
        }

        sealed class UsageException : Exception { public UsageException(string m) : base(m) { } }

        static string Ext(string path) => Path.GetExtension(path).ToLowerInvariant();

        /// <summary> the path without its extension if it names Triangle files (.node, .ele, .poly), else null </summary>
        public static string TriangleBase(string path)
        {
            string ext = Ext(path);
            return ext == ".node" || ext == ".ele" || ext == ".poly" ? path.Substring(0, path.Length - ext.Length) : null;
        }

        /// <summary>
        /// The Triangle files of a result (numbered from 1): base.node (vertices of the output's numbering, boundary markers), base.ele
        /// (triangles, the region labels as an attribute if the input has regions), base.poly (subsegments with markers, the input's hole
        /// points and seed regions). Returns the paths.
        /// </summary>
        public static string[] WriteTriangleFiles(string basePath, Mesh2Input input, Mesh2Output o)
        {
            var vertices = Enumerable.Range(0, o.VX.Length).Select(i => new Vector2d(o.VX[i], o.VY[i])).ToList();
            var subsegments = (o.Subsegments ?? new List<SubsegmentOutput>()).Select(s => (s.A, s.B, s.Marker)).ToList();
            var triangles = o.Triangles.Select(t => new Index3i(t[0], t[1], t[2])).ToList();
            var labels = input.Regions.Count > 0 ? o.TriangleLabels : null;
            var holes = input.HoleSeeds.Select(h => new Vector2d(h[0], h[1])).ToList();
            var regions = input.Regions.Where(g => g.Seed != null).Select(g => Mesh2Region.BySeed(new Vector2d(g.Seed[0], g.Seed[1]), g.Label, g.MaxArea)).ToList();
            var files = new[] { basePath + ".node", basePath + ".ele", basePath + ".poly" };
            File.WriteAllText(files[0], TrianglePolyFormat.WriteNode(vertices, null, o.Subsegments != null ? subsegments : null));
            File.WriteAllText(files[1], TrianglePolyFormat.WriteEle(triangles, labels));
            File.WriteAllText(files[2], TrianglePolyFormat.WritePolyOfSubsegments(subsegments, holes, regions));
            return files;
        }

        /// <summary>
        /// A result read back from Triangle files written by <see cref="WriteTriangleFiles"/>: vertices, triangles, subsegments with
        /// markers, labels; a foreign numbering (no map), as a reference session would read it.
        /// </summary>
        public static Mesh2Output ReadTriangleFiles(string basePath)
        {
            var nodes = TrianglePolyFormat.ReadNode(File.ReadAllText(basePath + ".node"), false, out var info);
            var triangles = TrianglePolyFormat.ReadEle(File.ReadAllText(basePath + ".ele"), nodes.Points.Count, info.FirstIndex, out var attribute);
            var poly = TrianglePolyFormat.ReadPoly(File.ReadAllText(basePath + ".poly"), () => File.ReadAllText(basePath + ".node"), false, out _);
            return new Mesh2Output {
                Status = triangles.Count > 0 ? Mesh2Status.Success : Mesh2Status.Empty,
                VX = nodes.Points.Select(p => p.x).ToArray(), VY = nodes.Points.Select(p => p.y).ToArray(),
                Triangles = triangles.Select(t => new[] { t.a, t.b, t.c }).ToArray(),
                Subsegments = poly.Segments.Select(s => new SubsegmentOutput(s.A, s.B, s.Marker)).ToList(),
                TriangleLabels = attribute?.Select(a => (int)a).ToArray(),
                IdentityRules = false, NeighboursReported = false,
            };
        }

        static string Arg(IList<string> args, string name)
        {
            int i = args.IndexOf(name);
            if (i < 0) return null;
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new UsageException(name + " needs a value");
            return args[i + 1];
        }

        static Milestone ParseMilestone(string text)
        {
            // M1, M2 or M3 in any case; Enum.Parse would also take numbers such as 7
            if (!Regex.IsMatch(text, "^[mM][123]$")) throw new UsageException("--milestone takes M1, M2 or M3, got " + text);
            return (Milestone)Enum.Parse(typeof(Milestone), text.ToUpperInvariant());
        }

        static double Number(string text, string name)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v)) return v;
            throw new UsageException(name + " takes a finite number, got " + text);
        }

        /// <summary> An input file: Triangle .poly / .node by extension, otherwise the plain text input format of section 2.6. </summary>
        public static Mesh2Input ReadInputFile(string path)
        {
            string text = File.ReadAllText(path);
            string ext = Ext(path);
            if (ext == ".poly") {
                string node = Path.ChangeExtension(path, ".node");
                return FromTriangle(TrianglePolyFormat.ReadPoly(text, () => File.Exists(node) ? File.ReadAllText(node) : null, false, out _));
            }
            if (ext == ".node") return FromTriangle(TrianglePolyFormat.ReadNode(text, false, out _));
            return TextFormat.ReadInput(text);
        }

        /// <summary> The command (arguments after "mesh"), writing to out and err; returns the exit code (see <see cref="Usage"/>). </summary>
        public static int Execute(IList<string> args, TextWriter output, TextWriter error, IEnumerable<Mesh2Case> cases, string goldenDir)
        {
            Mesh2Case c;
            string path;
            bool known;     // a case of the catalogue: compared with its golden row
            try {
                string id = Arg(args, "--case"), inputPath = Arg(args, "--input");
                path = Arg(args, "--output");
                if (path == null || (id == null) == (inputPath == null)) throw new UsageException("mesh needs --output and exactly one of --case and --input");
                string milestone = Arg(args, "--milestone"), minAngle = Arg(args, "--min-angle"), maxArea = Arg(args, "--max-area"), limit = Arg(args, "--steiner-limit"),
                    snap = Arg(args, "--snap-tolerance");
                if (id != null) {
                    if (milestone != null || minAngle != null || maxArea != null || limit != null || snap != null)
                        throw new UsageException("--milestone, --min-angle, --max-area, --steiner-limit and --snap-tolerance are for --input; a case has its own");
                    c = cases.FirstOrDefault(x => x.Id == id) ?? throw new UsageException("unknown case: " + id + " (see 'cases')");
                    known = true;
                } else {
                    var input = ReadInputFile(inputPath);
                    bool triangle = Ext(inputPath) == ".poly" || Ext(inputPath) == ".node";
                    if ((minAngle != null || maxArea != null || limit != null || snap != null) && !triangle)
                        throw new UsageException("--min-angle, --max-area, --steiner-limit and --snap-tolerance are for Triangle input; the text format states its options");
                    if (snap != null) {
                        double t = Number(snap, "--snap-tolerance");
                        if (t < 0) throw new UsageException("--snap-tolerance must be >= 0");
                        input.SnapTolerance = t;
                    }
                    if (minAngle != null) input.MinAngleDeg = Number(minAngle, "--min-angle");
                    if (maxArea != null) input.MaxArea = Number(maxArea, "--max-area");
                    if (limit != null) {
                        if (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) throw new UsageException("--steiner-limit takes a count, got " + limit);
                        input.SteinerLimit = n;
                    }
                    c = FromInput(input, Path.GetFileNameWithoutExtension(inputPath), milestone == null ? (Milestone?)null : ParseMilestone(milestone), cases);
                    if (c.Milestone == Milestone.M1 && c.Input.SnapTolerance != 0)
                        throw new UsageException("a snap tolerance needs M2 or M3: the M1 mesher (DelaunayMesh2) takes none");
                    known = cases.Contains(c);
                    if (known) output.WriteLine("input: the input of case " + c.Id + " (same input_sha256), judged with its expectations");
                }
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!Directory.Exists(dir)) throw new UsageException("the output directory does not exist: " + dir);
            } catch (UsageException e) {
                error.WriteLine(e.Message);
                error.WriteLine(Usage);
                return 2;
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException) {
                error.WriteLine("cannot read the input: " + e.Message);
                return 2;
            }

            Result result;
            try {
                result = Run(c, !args.Contains("--no-oracle"));
            } catch (MesherException e) when (e.InnerException is ArgumentException) {
                // the mesher refused an option or the input (eg a snap tolerance too small for the coordinates)
                error.WriteLine("invalid input: " + e.InnerException.Message);
                return 2;
            } catch (MesherException e) {
                error.WriteLine(e.Message);
                return 1;
            } catch (Exception e) {
                // the oracle or the row on an input nobody foresaw: reported, not an unhandled crash
                error.WriteLine("internal error: " + e.GetType().Name + ": " + e.Message);
                return 1;
            }
            string tbase = TriangleBase(path);
            try {
                if (tbase != null) {
                    if (result.Output.Status == Mesh2Status.Failed) {
                        foreach (var line in Summary(result, known ? FindGolden(goldenDir, c) : null)) output.WriteLine(line);
                        error.WriteLine("the mesher failed (" + result.Output.FailureKind + "): Triangle files have no failure status, none written");
                        return 1;
                    }
                    foreach (var f in WriteTriangleFiles(tbase, c.Input, result.Output)) output.WriteLine("wrote " + f);
                    if (c.Input.Regions.Any(g => g.Ring != null))
                        output.WriteLine("note: regions given by ring have no .poly form (a region is a seed point there): the .poly has only the seed regions; the labels are in the .ele");
                } else {
                    File.WriteAllText(path, result.Text);
                    output.WriteLine("wrote " + path);
                }
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                error.WriteLine("cannot write the output: " + e.Message);
                return 2;
            }
            var golden = known && result.Judged ? FindGolden(goldenDir, c) : null;
            foreach (var line in Summary(result, golden)) output.WriteLine(line);
            if (!result.Judged) return 0;
            bool differs = golden != null && golden.Differences(result.Row).Any();
            return result.Report.Ok && !differs ? 0 : 1;
        }
    }
}
