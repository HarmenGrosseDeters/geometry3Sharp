// Research spike (docs/research/PIPELINE_CERTIFICATE_SPIKE_2026-11.md): can a run of SolidBooleanPipeline be certified against its
// inputs alone? Measures the harness tests/geometry3Sharp.Tests/Harness/PipelineCertificate.cs (the exact stage's BooleanCertificate, the
// NotClean check and the #1488-style rounding checks) per corpus: the shares Certified / NotCertifiable / Rejected (by clause), and the
// cost of the certificate as a factor of the boolean (BigInteger as built, and with the filtered embedding check of Certify.cs).
//
// Usage: BooleanComparison pipeline-cert <mode> ...
//   procedural [frames]            the 60 cases of benchmarks/SolidBooleanAudit/Corpus.cs in the frames (default all 10; or a |-separated
//                                  list of names), the inputs as given, one ExactMeshBoolean (the pipeline's direct route, no repair)
//   extras                         the audit's small / tiny cases (F1) and the #1567 plate, direct
//   pipeline [frames]              the same cases through SolidBooleanPipeline (defaults); the boolean of the run that is the result is captured
//                                  by ConfigureBoolean (what phase 2's hook will hand over) and certified; the audit's oracle judges the result
//   pipeline-repair [frames]       the open-input cases through the pipeline with InputRepair = AutoRepair (the InputRepair route: NoInventedSurface against the inputs as passed)
//   thingi <raw_meshes dir> <count> <maxTriangles> [seed]
//                                  a seeded sample of Thingi10K models, the pairing of "export" (B = A rotated 20 degrees about (1, 1, 0),
//                                  shifted by a quarter of the box), union / intersection / difference, direct
// Output: one line per run (TSV), then the summary tables.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using gs;
using geometry3Sharp.Tests;
using Ops = g3.ExactMeshBoolean.Operations;

static class PipelineCertBench
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    sealed class Row
    {
        public string Corpus, Name, Frame = "", Op, Oracle = "", Today = "";
        public PipelineVerdictKind Kind;
        public PipelineRoute Route;
        public string Clause = "", Detail = "";
        public int Tris;
        public double BoolMs, CertMs, C2Ms, FilteredC2aMs, OtherMs, C0Ms, C1Ms, C3Ms, C5Ms;
        public int FilteredViolations;
        public List<string> Caveats = new List<string>();
        public string Key => Kind == PipelineVerdictKind.Certified ? (Caveats.Any(c => c.StartsWith("rounding removed")) ? "Certified, rounding removed parts" : Caveats.Any(c => c.StartsWith("contacts")) ? "Certified, with contacts" : Caveats.Any(c => c.StartsWith("input ") && c.Contains("winding number")) ? "Certified, input winding outside 0..1" : "Certified") : Kind == PipelineVerdictKind.NotCertifiable ? $"NotCertifiable({Clause})" : $"Rejected({Clause})";
    }

    sealed class Captured : IExactStageRun
    {
        public PipelineRoute Route { get; set; }
        public ExactMeshBoolean Boolean { get; set; }
        public IReadOnlyList<DMesh3> OriginalInputs { get; set; }
        public double InputDisplacement { get; set; }
    }

    static double PlainBooleanMs(IList<DMesh3> inputs, Ops op, out bool ok)
    {
        var b = new ExactMeshBoolean(inputs.ToList(), op) { StrictRounding = true, AllowOpenMeshes = false };
        var sw = Stopwatch.StartNew();
        ok = b.Compute();
        return sw.Elapsed.TotalMilliseconds;
    }

    static void Fill(Row r, PipelineVerdict v, double boolMs)
    {
        r.Kind = v.Kind; r.Route = v.Route; r.Clause = v.Clause; r.Detail = v.Detail.Replace('\t', ' ').Replace('\n', ' ');
        r.Caveats = v.Caveats;
        r.BoolMs = boolMs; r.CertMs = v.CertificateMs; r.C2Ms = v.C2Ms; r.C0Ms = v.C0Ms; r.C1Ms = v.C1Ms; r.C3Ms = v.C3Ms; r.C5Ms = v.C5Ms; r.OtherMs = v.NotCleanMs + v.RoundingChecksMs + v.FedMs;
    }

    static void FilteredC2a(Row r, ExactMeshBoolean b)
    {
        try { r.FilteredC2aMs = Certify.FilteredC2aMs(b.Arrangement, out r.FilteredViolations); }
        catch (Exception e) { r.FilteredC2aMs = double.NaN; r.Detail += " [filtered C2a: " + e.GetType().Name + "]"; }
    }

    // a direct run of the exact stage, judged
    static Row Direct(string corpus, string name, string frame, IList<DMesh3> inputs, Ops op, Func<DMesh3, string> oracle = null)
    {
        var r = new Row { Corpus = corpus, Name = name, Frame = frame, Op = op.ToString(), Route = PipelineRoute.Direct, Tris = inputs.Sum(m => m.TriangleCount) };
        try {
            double plain = PlainBooleanMs(inputs, op, out bool plainOk);
            DirectRun run = DirectRun.Compute(inputs, op);
            if (!run.Succeeded) {
                // (the pipeline routes these elsewhere: repair, sheet, orientation; not the direct route's business)
                r.Kind = PipelineVerdictKind.NotCertifiable; r.Clause = "refused"; r.Detail = run.Failure; r.BoolMs = plain;
                return r;
            }
            PipelineVerdict v = PipelineCertificate.Certify(run);
            Fill(r, v, plain);
            if (v.Kind == PipelineVerdictKind.Certified) FilteredC2a(r, run.Boolean);
            if (oracle != null) r.Oracle = oracle(run.Boolean.Result);
        } catch (Exception e) {
            r.Kind = PipelineVerdictKind.NotCertifiable; r.Clause = "exception"; r.Detail = e.GetType().Name + ": " + e.Message.Replace('\n', ' ');
        }
        return r;
    }

    // ------------------------------------------------------------------ the audit's oracle (ground truth of the procedural cases)

    sealed class Prepared { public DMesh3[] Given, Intent; public Vector3d[] Points; public sbyte[] Truth; public Vector3d Origin; public double Scale, Diagonal; }

    static Func<bool[], bool> TruthOf(Ops op)
    {
        switch (op) {
            case Ops.Union: return w => w.Any(x => x);
            case Ops.Intersection: return w => w.All(x => x);
            default: return w => w[0] && !w.Skip(1).Any(x => x);
        }
    }

    static Prepared Prepare(Case c, Frame f)
    {
        var p = new Prepared();
        p.Given = c.Given().Select(m => f.Apply(m, true)).ToArray();
        p.Intent = (c.Intent ?? c.Given)().Select(m => f.Apply(m, false)).ToArray();
        AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
        foreach (DMesh3 m in p.Intent) box.Contain(m.GetBounds());
        p.Origin = box.Center; p.Diagonal = box.DiagonalLength; p.Scale = Oracle.Normalizer(p.Diagonal);
        p.Points = Oracle.SamplePoints(p.Intent.Concat(p.Given).ToList(), 3000, 12345);
        p.Truth = Oracle.Truth(p.Intent, TruthOf(c.Op), p.Points, p.Origin, p.Scale, 1);
        return p;
    }

    static string Judge(Case c, Frame f, Prepared prep, DMesh3 result)
    {
        if (result == null) return "";
        var j = Oracle.Judge(result, prep.Truth, prep.Points, prep.Origin, prep.Scale, 2);
        bool wrong = j.Fraction > 0.002 || j.Overlap > 0.002 * j.Samples;
        if (2 * j.Samples < j.Points) return "UNJUDGED";
        if (f.Exact && !double.IsNaN(c.ExpectVolume)) {
            double expect = c.ExpectVolume * f.VolumeScale, vol = Oracle.Volume(Oracle.Of(result, prep.Origin, prep.Scale));
            double err = expect != 0 ? Math.Abs(vol - expect) / expect : Math.Abs(vol) / (prep.Diagonal * prep.Diagonal * prep.Diagonal);
            wrong |= err > 1e-9;
        }
        return wrong ? "WRONG" : (Oracle.Closed(result) ? "ok" : "OPEN");
    }

    static IEnumerable<Frame> PickFrames(string[] args)
    {
        var all = Corpus.Frames();
        if (args.Length == 0) return all;
        var names = args[0].Split('|');
        return all.Where(f => names.Contains(f.Name));
    }

    // ------------------------------------------------------------------ procedural, direct

    static List<Row> Procedural(string[] args)
    {
        var rows = new List<Row>();
        foreach (Case c in Corpus.Cases())
            foreach (Frame f in PickFrames(args)) {
                Prepared prep = Prepare(c, f);
                rows.Add(Direct("procedural-direct:" + c.Class, c.Name, f.Name, prep.Given, c.Op, res => Judge(c, f, prep, res)));
            }
        return rows;
    }

    // the audit's F1 cases (thin and tiny solids the rounding removes) and the plate of #1567, direct
    static List<Row> Extras()
    {
        var rows = new List<Row>();
        var frames = Corpus.Frames().Where(f => f.Name == "id" || f.Name == "rot" || f.Name == "rot+utm").ToList();
        foreach (Frame f in frames) {
            foreach (int e in new[] { 8, 9, 12 }) {
                double a = Math.ScaleB(1, -e);
                var c = new Case { Class = "tiny", Name = "tiny cube 2^-" + e, Op = Ops.Union, Given = () => new[] { Corpus.Cube(0, 1024), Corpus.Box(1100, 0, 0, 1100 + a, a, a) } };
                Prepared prep = Prepare(c, f);
                rows.Add(Direct("extras:F1-tiny", c.Name, f.Name, prep.Given, c.Op, res => Judge(c, f, prep, res)));
            }
            foreach (string kind in new[] { "I slab", "D wall", "U plate" })
                foreach (int e in new[] { 12, 15, 17, 25, 35 }) {
                    double h = Math.ScaleB(1, -e);
                    var c = new Case { Class = "small", Name = kind + " 2^-" + e };
                    if (kind == "I slab") { c.Op = Ops.Intersection; c.Given = () => new[] { Corpus.Cube(0, 2), Corpus.Box(2 - h, 0, 0, 4, 2, 2) }; }
                    else if (kind == "D wall") { c.Op = Ops.Difference; c.Given = () => new[] { Corpus.Cube(0, 2), Corpus.Box(h, -1, -1, 3, 3, 3) }; }
                    else { c.Op = Ops.Union; c.Given = () => new[] { Corpus.Cube(0, 2), Corpus.Box(3, 0, 0, 5, 2, h) }; }
                    Prepared prep = Prepare(c, f);
                    rows.Add(Direct("extras:F1-small", c.Name, f.Name, prep.Given, c.Op, res => Judge(c, f, prep, res)));
                }
        }
        // #1567: a 10 x 10 x 0.01 inside-out part in B, next to a big A. The pipeline drops it before the exact stage; direct, the exact stage
        // gets B whole (certified against what it got), and gets B without the plate (certified too; only the InputsFed check sees the difference)
        Func<Vector3d, Vector3d, bool, DMesh3> box = (cc, ee, flip) => { var m = new TrivialBox3Generator { Box = new Box3d(cc, ee) }.Generate().MakeDMesh(); if (flip) m.ReverseOrientation(); return m; };
        DMesh3 B = box(new Vector3d(2000, 0, 0), new Vector3d(0.5, 0.5, 0.5), false);
        DMesh3 Bplate = box(new Vector3d(2000, 0, 0), new Vector3d(0.5, 0.5, 0.5), false);
        MeshEditor.Append(Bplate, box(new Vector3d(2000, 20, 0), new Vector3d(5, 5, 0.005), true));
        DMesh3 big = box(Vector3d.Zero, new Vector3d(500, 500, 500), false);
        rows.Add(Direct("extras:1567", "A 500^3 U B + inside-out plate (fed whole)", "id", new[] { big, Bplate }, Ops.Union));
        rows.Add(Direct("extras:1567", "A 500^3 U B (plate dropped by the caller)", "id", new[] { big, B }, Ops.Union));
        // the plate dropped in the way of the pipeline: certified against the dropped inputs, but the original inputs differ
        {
            DirectRun run = DirectRun.Compute(new[] { big, B }, Ops.Union);
            var cap = new Captured { Route = PipelineRoute.InputRepair, Boolean = run.Boolean, OriginalInputs = new[] { big, Bplate } };
            var r = new Row { Corpus = "extras:1567", Name = "same run, labelled as the pipeline's drop (original inputs = with the plate)", Frame = "id", Op = "Union", Route = PipelineRoute.InputRepair };
            Fill(r, PipelineCertificate.Certify(cap), run.BooleanMs);
            rows.Add(r);
        }
        return rows;
    }

    // ------------------------------------------------------------------ the pipeline, with the boolean captured

    static PipelineRoute RouteOf(SolidBooleanPipeline p, out double displacement)
    {
        displacement = 0;
        var names = p.Report.Route.Select(s => s.Name).ToList();
        var grid = p.Report.Route.FirstOrDefault(s => s.Name == "Grid");
        if (grid != null) displacement = grid.Grid * Math.Sqrt(3);
        if (names.Contains("InputRepair") || p.Report.DroppedInsideOutComponents.Count > 0) return PipelineRoute.InputRepair;
        if (grid != null) return PipelineRoute.GridRetry;
        if (names.Contains("Fallback")) return PipelineRoute.Fallback;
        if (names.Contains("SheetRole")) return PipelineRoute.SheetRole;
        return PipelineRoute.Direct;
    }

    static List<Row> PipelineRuns(string[] args, bool repair = false)
    {
        var rows = new List<Row>();
        foreach (Case c in Corpus.Cases().Where(c => !repair || c.Class == "open"))
            foreach (Frame f in PickFrames(args)) {
                Prepared prep = Prepare(c, f);
                var r = new Row { Corpus = (repair ? "procedural-pipeline-repair:" : "procedural-pipeline:") + c.Class, Name = c.Name, Frame = f.Name, Op = c.Op.ToString(), Tris = prep.Given.Sum(m => m.TriangleCount) };
                try {
                    var pipe = new SolidBooleanPipeline(prep.Given.ToList(), c.Op);
                    if (repair) pipe.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair;
                    ExactMeshBoolean last = null;
                    pipe.ConfigureBoolean = b => {
                        b.RecordCertificate = true; b.ComputeExactVertices = true;
                        if (c.Op == Ops.Intersection) b.CullOutsideA = false;
                        last = b;
                    };
                    var sw = Stopwatch.StartNew();
                    bool ok = pipe.Compute();
                    r.BoolMs = sw.Elapsed.TotalMilliseconds;
                    r.Route = PipelineRoute.Direct;
                    if (!ok || last == null) {
                        r.Kind = PipelineVerdictKind.NotCertifiable; r.Clause = "refused"; r.Detail = pipe.Status + (pipe.Status == SolidBooleanPipeline.PipelineStatus.BooleanFailed ? "/" + pipe.BooleanStatus : "");
                        rows.Add(r); continue;
                    }
                    PipelineRoute route = RouteOf(pipe, out double disp);
                    var cap = new Captured { Route = route, Boolean = last, OriginalInputs = prep.Given, InputDisplacement = disp };
                    PipelineVerdict v = PipelineCertificate.Certify(cap);
                    Fill(r, v, r.BoolMs);
                    r.Route = route;
                    if (v.Kind == PipelineVerdictKind.Certified) FilteredC2a(r, last);
                    r.Oracle = Judge(c, f, prep, pipe.Result);
                    // (what the pipeline's own report says about the run today: the baseline the certificate has to beat)
                    r.Today = $"{pipe.Status}, Clean={pipe.Report.Clean}, RoundingRemoved={pipe.Report.RoundingRemoved.Count}, DroppedInsideOut={pipe.Report.DroppedInsideOutComponents.Count}, RoundingClean={last.RoundingClean}";
                    // (what the pipeline does after the exact stage: the certificate is about the exact stage's output)
                    if (pipe.Result != null && last.Result != null && pipe.Result.TriangleCount != last.Result.TriangleCount)
                        r.Detail += $" [post-exact-stage change: {last.Result.TriangleCount} -> {pipe.Result.TriangleCount} triangles]";
                } catch (Exception e) {
                    r.Kind = PipelineVerdictKind.NotCertifiable; r.Clause = "exception"; r.Detail = e.GetType().Name + ": " + e.Message.Replace('\n', ' ');
                }
                rows.Add(r);
            }
        return rows;
    }

    // ------------------------------------------------------------------ Thingi10K

    static List<Row> Thingi(string[] args)
    {
        string dir = args[0];
        int count = int.Parse(args[1], ci), maxTris = int.Parse(args[2], ci), seed = args.Length > 3 ? int.Parse(args[3], ci) : 20261125;
        var files = Directory.GetFiles(dir, "*.stl").OrderBy(x => x, StringComparer.Ordinal).ToList();
        var rng = new Random(seed);
        for (int i = files.Count - 1; i > 0; --i) { int j = rng.Next(i + 1); (files[i], files[j]) = (files[j], files[i]); }
        var rows = new List<Row>();
        int taken = 0;
        // BOOLCMP_ONLY_IDS=a,b,c: only these models of the sample (a rerun of some of them)
        string onlyIds = Environment.GetEnvironmentVariable("BOOLCMP_ONLY_IDS");
        var only = string.IsNullOrEmpty(onlyIds) ? null : new HashSet<string>(onlyIds.Split(','));
        foreach (string file in files.Take(count)) {
            string id = Path.GetFileNameWithoutExtension(file);
            if (only != null && !only.Contains(id)) continue;
            DMesh3 a = null;
            try { a = Program.Load(file); } catch { }
            if (a == null || a.TriangleCount == 0) { rows.Add(Skip(id, "load failed")); continue; }
            if (a.TriangleCount > maxTris) { rows.Add(Skip(id, "size > " + maxTris, a.TriangleCount)); continue; }
            taken++;
            DMesh3 b = new DMesh3(a);
            AxisAlignedBox3d bounds = a.CachedBounds;
            MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                Row r = Direct("thingi", id, "", new[] { a, b }, op);
                rows.Add(r);
                Console.Error.WriteLine($"{id} {op} {r.Key} tris {r.Tris} bool {r.BoolMs:F0} cert {r.CertMs:F0}");
            }
        }
        Console.WriteLine($"# thingi: {files.Take(count).Count()} models sampled (seed {seed}), {taken} run (<= {maxTris} triangles each)");
        return rows;

        Row Skip(string id, string why, int tris = 0) => new Row { Corpus = "thingi", Name = id, Op = "-", Kind = PipelineVerdictKind.NotCertifiable, Clause = "skipped: " + why, Tris = tris };
    }

    // ------------------------------------------------------------------ diagnosis of a coverage-undecided run

    // the sampled distance from the triangles of one mesh to the other (a grid of barycentric points per triangle): the largest, in doubles
    static double SampledHausdorff(DMesh3 from, DMesh3 to, int grid)
    {
        var tree = new DMeshAABBTree3(to, true);
        double worst = 0;
        foreach (int t in from.TriangleIndices()) {
            Index3i tri = from.GetTriangle(t);
            Vector3d a = from.GetVertex(tri.a), b = from.GetVertex(tri.b), c = from.GetVertex(tri.c);
            for (int i = 0; i <= grid; ++i)
                for (int j = 0; j <= grid - i; ++j) {
                    double u = (double)i / grid, v = (double)j / grid;
                    Vector3d p = a + u * (b - a) + v * (c - a);
                    tree.FindNearestTriangleByClosestPoint(p, out double d);
                    worst = Math.Max(worst, d);
                }
        }
        return worst;
    }

    static int Diagnose(string[] args)
    {
        // diag <mesh file> <Union|Intersection|Difference>: the pairing of "export", the run, and the coverage at growing split depths
        DMesh3 a = Program.Load(args[0]);
        DMesh3 b = new DMesh3(a);
        AxisAlignedBox3d bounds = a.CachedBounds;
        MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
        Ops op = (Ops)Enum.Parse(typeof(Ops), args[1]);
        DirectRun run = DirectRun.Compute(new[] { a, b }, op);
        BooleanCertificate c = BooleanCertificate.From(run.Boolean);
        Console.WriteLine($"Delta {c.Delta:G4}, MaxLength {c.MaxLength:G4}, result {c.Result.TriangleCount} triangles, assembled {c.Assembled.TriangleCount}");
        foreach (int depth in new[] { 3, 6, 9, 12 }) {
            c.SplitDepth = depth;
            string r = c.CheckResult();
            Console.WriteLine($"split depth {depth}: Uncovered {c.Uncovered}, '{r}'");
        }
        Console.WriteLine($"sampled distance (doubles) from the result to the result before rounding: {SampledHausdorff(c.Result, c.Assembled, 8):G4}; the other way: {SampledHausdorff(c.Assembled, c.Result, 8):G4}");
        return 0;
    }

    // ------------------------------------------------------------------ output

    static string F(double x) => double.IsNaN(x) ? "nan" : x.ToString(x < 10 ? "0.00" : x < 100 ? "0.0" : "0", ci);

    static double Pct(List<double> v, double p) { if (v.Count == 0) return double.NaN; v.Sort(); return v[Math.Min(v.Count - 1, (int)(p * (v.Count - 1) + 0.5))]; }

    static void Print(List<Row> rows)
    {
        Console.WriteLine("corpus\tname\tframe\top\ttris\tverdict\troute\tdetail\toracle\ttoday (pipeline report)\tboolean ms\tcertificate ms\tC2 ms\tC0 ms\tC1 ms\tC3 ms\tC5 ms\tfiltered C2a ms\tother checks ms\tcaveats");
        foreach (Row r in rows)
            Console.WriteLine($"{r.Corpus}\t{r.Name}\t{r.Frame}\t{r.Op}\t{r.Tris}\t{r.Key}\t{r.Route}\t{r.Detail}\t{r.Oracle}\t{r.Today}\t{F(r.BoolMs)}\t{F(r.CertMs)}\t{F(r.C2Ms)}\t{F(r.C0Ms)}\t{F(r.C1Ms)}\t{F(r.C3Ms)}\t{F(r.C5Ms)}\t{F(r.FilteredC2aMs)}\t{F(r.OtherMs)}\t{string.Join(" | ", r.Caveats)}");
        Console.WriteLine();
        foreach (var corpus in rows.GroupBy(r => r.Corpus.Split(':')[0]).OrderBy(g => g.Key)) {
            var all = corpus.ToList();
            Console.WriteLine($"## {corpus.Key}: {all.Count} runs");
            foreach (var g in all.GroupBy(r => r.Key).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key}: {g.Count()} ({100.0 * g.Count() / all.Count:F1} %)");
            if (all.Any(r => r.Oracle != "")) {
                Console.WriteLine("  against the audit's oracle (runs with a result):");
                foreach (var g in all.Where(r => r.Oracle != "").GroupBy(r => (r.Oracle, r.Key)).OrderBy(g => g.Key.Oracle).ThenBy(g => g.Key.Key))
                    Console.WriteLine($"    oracle {g.Key.Oracle} / {g.Key.Key}: {g.Count()}");
            }
            if (all.Any(r => r.Today != "")) {
                Console.WriteLine("  oracle WRONG runs: the certificate's verdict against the pipeline's own report today:");
                foreach (var g in all.Where(r => r.Oracle == "WRONG").GroupBy(r => (r.Key, r.Today)).OrderBy(g => g.Key.Key))
                    Console.WriteLine($"    {g.Key.Key} | today: {g.Key.Today}: {g.Count()}");
            }
            var cert = all.Where(r => r.Kind == PipelineVerdictKind.Certified && r.BoolMs > 0).ToList();
            if (cert.Count > 0) {
                var big = cert.Where(r => r.BoolMs >= 20).ToList();
                foreach (var (label, set) in new[] { ("all certified", cert), ("boolean >= 20 ms", big) }) {
                    if (set.Count == 0) continue;
                    var fb = set.Select(r => r.CertMs / r.BoolMs).ToList();
                    var ff = set.Where(r => !double.IsNaN(r.FilteredC2aMs)).Select(r => (r.CertMs - r.C2Ms + r.FilteredC2aMs) / r.BoolMs).ToList();
                    var fa = set.Select(r => (r.CertMs + r.OtherMs) / r.BoolMs).ToList();
                    var fc = set.Where(r => r.CertMs > 0).Select(r => r.C2Ms / r.CertMs).ToList();
                    Console.WriteLine($"  cost, {label} ({set.Count}): certificate / boolean, BigInteger: median {F(Pct(fb, 0.5))}, p90 {F(Pct(fb, 0.9))}, max {F(Pct(fb, 1))}; "
                        + $"with the filtered embedding check: median {F(Pct(ff, 0.5))}, p90 {F(Pct(ff, 0.9))}, max {F(Pct(ff, 1))}; "
                        + $"with the NotClean and rounding checks added: median {F(Pct(fa, 0.5))}; C2's share of the certificate: median {F(Pct(fc, 0.5))}");
                }
                int mis = cert.Count(r => r.FilteredViolations != 0);
                if (mis > 0) Console.WriteLine($"  filtered C2a reported violations on {mis} certified runs");
            }
            int cav = all.Count(r => r.Caveats.Count > 0);
            if (cav > 0) Console.WriteLine($"  runs with caveats: {cav}");
        }
        var rej = rows.Where(r => r.Kind == PipelineVerdictKind.Rejected).ToList();
        if (rej.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("## every Rejected");
            foreach (Row r in rej) Console.WriteLine($"  {r.Corpus} | {r.Name} | {r.Frame} | {r.Op} | {r.Key} | {r.Detail} | oracle {r.Oracle}");
        }
    }

    public static int Run(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = ci; CultureInfo.CurrentCulture = ci;
        if (args.Length == 0) { Console.Error.WriteLine("usage: pipeline-cert procedural|extras|pipeline [frames] | thingi <dir> <count> <maxTriangles> [seed]"); return 1; }
        var rest = args.Skip(1).ToArray();
        List<Row> rows;
        switch (args[0]) {
            case "procedural": rows = Procedural(rest); break;
            case "extras": rows = Extras(); break;
            case "pipeline": rows = PipelineRuns(rest); break;
            case "pipeline-repair": rows = PipelineRuns(rest, true); break;
            case "thingi": rows = Thingi(rest); break;
            case "diag": return Diagnose(rest);
            default: Console.Error.WriteLine("unknown mode " + args[0]); return 1;
        }
        Print(rows);
        return 0;
    }
}
