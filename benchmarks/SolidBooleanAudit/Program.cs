using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using g3;
using gs;

// An end-to-end audit of SolidBooleanPipeline on a procedural adversarial corpus, judged by an oracle that shares no code with
// the pipeline (Oracle.cs). See README.md for the modes and docs/research/SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md for the findings.
static class Program
{
    sealed class Config
    {
        public string Name;
        public Action<SolidBooleanPipeline> Set;
        public bool RepairFirst;        // MeshRobustRepair on every input before the pipeline (the prophylactic route)
    }

    static readonly Config Default = new Config { Name = "default", Set = p => { } };

    static readonly Config[] Escalations = {
        new Config { Name = "ByNesting", Set = p => p.Orientation = ExactMeshBoolean.OrientationModes.ByNesting },
        new Config { Name = "AreaMajority", Set = p => p.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority },
        new Config { Name = "AllowInsideOut", Set = p => p.AllowInsideOutInputs = true },
        new Config { Name = "AutoRepair", Set = p => p.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair },
        new Config { Name = "AutoRepairThenRobust", Set = p => p.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust },
        new Config { Name = "ByNesting+AutoRepairThenRobust", Set = p => { p.Orientation = ExactMeshBoolean.OrientationModes.ByNesting; p.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust; } },
        new Config { Name = "RobustRepairFirst", Set = p => { }, RepairFirst = true },
    };

    sealed class Prepared
    {
        public DMesh3[] Given, Intent;
        public Vector3d[] Points;
        public sbyte[] Truth;
        public Vector3d Origin;
        public double Diagonal;
        public double Scale = 1;    // the oracle's normaliser (a power of two)
    }

    sealed class Outcome
    {
        public string Verdict;      // ok, WRONG, OPEN (result not closed), or the refusal code
        public bool Success;
        public SolidBooleanPipeline.PipelineStatus Status;
        public ExactMeshBoolean.BooleanStatus BooleanStatus;
        public string Error = "";
        public Oracle.Judgement Judgement;
        public double Volume = double.NaN, VolumeError = double.NaN;
        public long Milliseconds;
        public SolidBooleanReport Report;
        public MeshRoundingRepair Rounding;
        public int ResultTriangles;
    }

    static DMesh3 prepResult;       // the result of the last successful run

    static Func<bool[], bool> truthOf(ExactMeshBoolean.Operations op)
    {
        switch (op) {
            case ExactMeshBoolean.Operations.Union: return w => w.Any(x => x);
            case ExactMeshBoolean.Operations.Intersection: return w => w.All(x => x);
            default: return w => w[0] && !w.Skip(1).Any(x => x);
        }
    }

    static Prepared prepare(Case c, Frame f, int samples)
    {
        var p = new Prepared();
        p.Given = c.Given().Select(m => f.Apply(m, true)).ToArray();
        p.Intent = (c.Intent ?? c.Given)().Select(m => f.Apply(m, false)).ToArray();
        AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
        foreach (DMesh3 m in p.Intent)
            box.Contain(m.GetBounds());
        p.Origin = box.Center;
        p.Diagonal = box.DiagonalLength;
        p.Scale = Oracle.Normalizer(p.Diagonal);
        p.Points = Oracle.SamplePoints(p.Intent.Concat(p.Given).ToList(), samples, 12345);
        p.Truth = Oracle.Truth(p.Intent, truthOf(c.Op), p.Points, p.Origin, p.Scale, 1);
        return p;
    }

    static string code(SolidBooleanPipeline p)
    {
        switch (p.Status) {
            case SolidBooleanPipeline.PipelineStatus.OpenInput: return "R:open";
            case SolidBooleanPipeline.PipelineStatus.InsideOutInput: return "R:insideout";
            case SolidBooleanPipeline.PipelineStatus.InconsistentOrientation: return "R:orient";
            case SolidBooleanPipeline.PipelineStatus.InvalidInput: return "R:invalid";
            case SolidBooleanPipeline.PipelineStatus.Cancelled: return "R:timeout";
            case SolidBooleanPipeline.PipelineStatus.BooleanFailed: return "R:" + p.BooleanStatus;
            default: return "R:" + p.Status;
        }
    }

    static Outcome run(Case c, Frame f, Prepared prep, Config cfg, int timeoutSeconds = 60)
    {
        var o = new Outcome();
        var inputs = prep.Given.ToList();
        var clock = Stopwatch.StartNew();
        if (cfg.RepairFirst)
            inputs = inputs.Select(m => { var r = new MeshRobustRepair(new DMesh3(m)); r.Apply(); return r.Mesh; }).ToList();
        var pipe = new SolidBooleanPipeline(inputs, c.Op);
        cfg.Set(pipe);
        ExactMeshBoolean last = null;
        pipe.ConfigureBoolean = b => last = b;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))) {
            pipe.Cancellation = cts.Token;
            try {
                o.Success = pipe.Compute();
            } catch (Exception e) {
                o.Verdict = "EXCEPTION";
                o.Error = e.GetType().Name + ": " + e.Message;
                o.Milliseconds = clock.ElapsedMilliseconds;
                return o;
            }
        }
        o.Milliseconds = clock.ElapsedMilliseconds;
        o.Status = pipe.Status; o.BooleanStatus = pipe.BooleanStatus; o.Error = pipe.Error ?? ""; o.Report = pipe.Report;
        o.Rounding = last != null ? last.Rounding : null;
        if (!o.Success) {
            o.Verdict = code(pipe);
            return o;
        }
        o.ResultTriangles = pipe.Result.TriangleCount;
        prepResult = pipe.Result;
        o.Judgement = Oracle.Judge(pipe.Result, prep.Truth, prep.Points, prep.Origin, prep.Scale, 2);
        o.Volume = Oracle.Volume(Oracle.Of(pipe.Result, prep.Origin, prep.Scale));
        // (a result that covers itself is wrong too: a solid has crossing number 0 or 1)
        bool wrong = o.Judgement.Fraction > 0.002 || o.Judgement.Overlap > 0.002 * o.Judgement.Samples;
        if (2 * o.Judgement.Samples < o.Judgement.Points) {
            // (the sampling could not judge: never "ok" by default)
            o.Verdict = "UNJUDGED";
            return o;
        }
        if (f.Exact && !double.IsNaN(c.ExpectVolume)) {
            double expect = c.ExpectVolume * f.VolumeScale;
            o.VolumeError = expect != 0 ? Math.Abs(o.Volume - expect) / expect : Math.Abs(o.Volume) / (prep.Diagonal * prep.Diagonal * prep.Diagonal);
            wrong |= o.VolumeError > 1e-9;
        }
        o.Verdict = wrong ? "WRONG" : (Oracle.Closed(pipe.Result) ? "ok" : "OPEN");
        return o;
    }

    static string F(double x, string fmt = "0.###e+0") { return x.ToString(fmt, CultureInfo.InvariantCulture); }

    // ---- mode corpus: every case in every frame with the defaults; what does not come out "ok" is tried with each escalation ----
    static void corpus(string[] args)
    {
        int samples = 6000;
        string only = args.Length > 1 ? args[1] : null;
        var cases = Corpus.Cases().Where(c => only == null || c.Class == only || c.Name.Contains(only)).ToList();
        var frames = Corpus.Frames();
        Console.WriteLine("| class | case | " + string.Join(" | ", frames.Select(f => f.Name)) + " |");
        Console.WriteLine("|---|---|" + string.Concat(frames.Select(f => "---|")));
        var details = new List<string>();
        var tally = new Dictionary<(string, string), int>();
        var slow = new List<string>();
        int runs = 0;
        foreach (Case c in cases) {
            var cells = new List<string>();
            foreach (Frame f in frames) {
                Prepared prep = prepare(c, f, samples);
                Outcome o = run(c, f, prep, Default);
                runs++;
                cells.Add(o.Verdict);
                string kind = o.Verdict == "ok" ? "ok" : (o.Verdict.StartsWith("R:") ? "refused" : o.Verdict);
                if (o.Milliseconds > 20000)
                    slow.Add(c.Name + " @ " + f.Name + ": " + o.Milliseconds + " ms");
                if (o.Verdict == "ok") {
                    bump(tally, c.Class, "ok");
                    continue;
                }
                // what would have given the right result?
                var good = new List<string>();
                var other = new List<string>();
                foreach (Config e in Escalations) {
                    Outcome oe = run(c, f, prep, e);
                    if (oe.Verdict == "ok")
                        good.Add(e.Name);
                    else
                        other.Add(e.Name + " " + oe.Verdict);
                }
                // and the plain boolean with open meshes allowed (stated) and no strict rounding
                string plain = plainBoolean(c, f, prep);
                bump(tally, c.Class, kind == "refused" ? (good.Count > 0 || plain == "ok" ? "refused, answer exists" : "refused, no setting answers") : kind);
                string d = "- `" + c.Name + "` @ `" + f.Name + "`: **" + o.Verdict + "**";
                if (o.Judgement != null)
                    d += " (mismatch " + F(o.Judgement.Fraction, "0.####") + " of " + o.Judgement.Samples + ": " + o.Judgement.FalseSolid + " falsely solid, " + o.Judgement.FalseEmpty + " falsely empty"
                        + (double.IsNaN(o.VolumeError) ? "" : "; volume error " + F(o.VolumeError)) + "; NotCleanReason \"" + o.Report.NotCleanReason + "\")";
                else
                    d += " (" + trim(o.Error) + ")";
                d += "; ok with: " + (good.Count > 0 ? string.Join(", ", good) : "none") + "; not ok: " + (other.Count > 0 ? string.Join(", ", other) : "none") + "; plain ExactMeshBoolean: " + plain;
                details.Add(d);
            }
            Console.WriteLine("| " + c.Class + " | " + c.Name + " | " + string.Join(" | ", cells) + " |");
        }
        Console.WriteLine();
        Console.WriteLine("Runs: " + runs + " (" + cases.Count + " cases x " + frames.Count + " frames), " + samples + " sample points each.");
        Console.WriteLine();
        var kinds = tally.Keys.Select(k => k.Item2).Distinct().OrderBy(k => k == "ok" ? 0 : 1).ThenBy(k => k).ToList();
        Console.WriteLine("| class | " + string.Join(" | ", kinds) + " |");
        Console.WriteLine("|---|" + string.Concat(kinds.Select(k => "---|")));
        foreach (string cls in cases.Select(c => c.Class).Distinct()) {
            Console.WriteLine("| " + cls + " | " + string.Join(" | ", kinds.Select(k => tally.TryGetValue((cls, k), out int v) ? v.ToString() : "0")) + " |");
        }
        Console.WriteLine("| all | " + string.Join(" | ", kinds.Select(k => tally.Where(t => t.Key.Item2 == k).Sum(t => t.Value).ToString())) + " |");
        Console.WriteLine();
        foreach (string d in details)
            Console.WriteLine(d);
        Console.WriteLine();
        Console.WriteLine("Runs above 20 s: " + (slow.Count == 0 ? "none" : string.Join("; ", slow)));
    }

    static string trim(string s) { s = s.Replace("\n", " "); return s.Length > 400 ? s.Substring(0, 400) + "..." : s; }

    static void bump(Dictionary<(string, string), int> t, string a, string b) { t.TryGetValue((a, b), out int v); t[(a, b)] = v + 1; }

    static string plainBoolean(Case c, Frame f, Prepared prep)
    {
        var op = new ExactMeshBoolean(prep.Given.ToList(), c.Op) { AllowOpenMeshes = true };
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60))) {
            op.Cancellation = cts.Token;
            try {
                if (!op.Compute())
                    return "R:" + op.Status;
            } catch (Exception e) {
                return "EXCEPTION " + e.GetType().Name;
            }
        }
        Oracle.Judgement j = Oracle.Judge(op.Result, prep.Truth, prep.Points, prep.Origin, prep.Scale, 3);
        if (2 * j.Samples < j.Points)
            return "UNJUDGED";
        bool wrong = j.Fraction > 0.002 || j.Overlap > 0.002 * j.Samples;
        if (f.Exact && !double.IsNaN(c.ExpectVolume)) {
            double expect = c.ExpectVolume * f.VolumeScale, v = Oracle.Volume(Oracle.Of(op.Result, prep.Origin, prep.Scale));
            wrong |= (expect != 0 ? Math.Abs(v - expect) / expect : Math.Abs(v) / Math.Pow(prep.Diagonal, 3)) > 1e-9;
        }
        return wrong ? "WRONG" : (Oracle.Closed(op.Result) ? "ok" : "OPEN") + (op.RoundingClean ? "" : " (rounding not clean)");
    }

    // ---- mode touch: how far a cavity vertex that lies in a wall of its outer shell comes apart from it under transforms ----
    // (the #1221 touch tolerance: 256 eps M of the shell's box; options B, relative to the largest input magnitude, and C, 4096)
    static void touch()
    {
        const double eps = 2.220446049250313e-16;
        var rng = new Random(7);
        Console.WriteLine("| history | magnitude M | trials | wall triangle collapsed (not counted) | max distance / (eps M) | share above 16 | share above 256 | share above 4096 |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        double[] magnitudes = { 1, 1e3, 5.5e6, 1e9 };
        string[] histories = { "1 rigid step", "8 rigid steps", "64 rigid steps", "away by 1e8 and back", "away by 1e12 and back", "float32", "text, 6 decimals", "text, 3 decimals" };
        foreach (string hist in histories)
            foreach (double M in magnitudes) {
                int trials = 4000, a16 = 0, a256 = 0, a4096 = 0, collapsed = 0;
                double worst = 0;
                for (int k = 0; k < trials; ++k) {
                    // a wall triangle and a point in it, exact (dyadic coordinates in the plane z = 1), in a model of size 4
                    Vector3d a = new Vector3d(0, 0, 1), b = new Vector3d(4, 0, 1), c = new Vector3d(0, 4, 1);
                    Vector3d q = new Vector3d(rng.Next(1, 1024) / 1024.0, rng.Next(1, 1024) / 1024.0, 1);
                    Func<Vector3d, Vector3d> map = history(hist, M, rng);
                    Vector3d A = map(a), B = map(b), C = map(c), Q = map(q);
                    // (a history that leaves the wall triangle without area, eg all corners rounded to one point, has no plane)
                    if ((B - A).Cross(C - A).Length == 0) { collapsed++; continue; }
                    double d = distance(A, B, C, Q);
                    double m = Math.Max(Math.Max(maxAbs(A), maxAbs(B)), Math.Max(maxAbs(C), maxAbs(Q)));
                    double r = d / (eps * m);
                    worst = Math.Max(worst, r);
                    if (r > 16) a16++;
                    if (r > 256) a256++;
                    if (r > 4096) a4096++;
                }
                int counted = Math.Max(1, trials - collapsed);
                Console.WriteLine("| " + hist + " | " + F(M, "0.#e+0") + " | " + trials + " | " + collapsed + " | " + F(worst, "0.##e+0") + " | " + F((double)a16 / counted, "0.####") + " | " + F((double)a256 / counted, "0.####") + " | " + F((double)a4096 / counted, "0.####") + " |");
            }
    }

    static double maxAbs(Vector3d p) { return Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))); }

    // the distance of q from the plane of (a, b, c): the determinant is exact, its conversion and the area are in doubles
    static double distance(Vector3d a, Vector3d b, Vector3d c, Vector3d q)
    {
        double det = Math.Abs(Dyadic.Orient(a, b, c, q).ToDouble());
        double area2 = (b - a).Cross(c - a).Length;
        return area2 > 0 ? det / area2 : 0;
    }

    static Func<Vector3d, Vector3d> rigid(Random rng, double M)
    {
        Vector3d axis = new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5).Normalized;
        var R = Matrix3d.AxisAngleD(axis, 360 * rng.NextDouble());
        Vector3d t = M * new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
        return p => R * p + t;
    }

    static Func<Vector3d, Vector3d> history(string hist, double M, Random rng)
    {
        Func<Vector3d, Vector3d> place = rigid(rng, 2 * M);
        switch (hist) {
            case "1 rigid step": return place;
            case "8 rigid steps":
            case "64 rigid steps": {
                // rotations about points near the model: the magnitude stays about M
                int n = hist.StartsWith("8") ? 8 : 64;
                var steps = new List<Func<Vector3d, Vector3d>> { place };
                for (int i = 1; i < n; ++i) {
                    Vector3d axis = new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5).Normalized;
                    var R = Matrix3d.AxisAngleD(axis, 360 * rng.NextDouble());
                    Vector3d pivot = M * new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
                    steps.Add(p => R * (p - pivot) + pivot);
                }
                return p => { foreach (var s in steps) p = s(p); return p; };
            }
            case "away by 1e8 and back": { Vector3d far = new Vector3d(1e8 * M, -1e8 * M, 1e8 * M); return p => (place(p) + far) - far; }
            case "away by 1e12 and back": { Vector3d far = new Vector3d(1e12 * M, -1e12 * M, 1e12 * M); return p => (place(p) + far) - far; }
            case "float32": return p => Corpus.Float32(place(p));
            case "text, 6 decimals": return p => text(place(p), 6);
            default: return p => text(place(p), 3);
        }
    }

    static Vector3d text(Vector3d p, int decimals)
    {
        string fmt = "F" + decimals;
        return new Vector3d(double.Parse(p.x.ToString(fmt, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            double.Parse(p.y.ToString(fmt, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            double.Parse(p.z.ToString(fmt, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }

    // ---- mode negvol: the negative-volume test of ByNesting for meshes that are not embedded, over the scale 2^k ----
    static void negvol()
    {
        // not embedded (two boxes overlap) with a consistently oriented inside-out component: documented as refused with
        // InconsistentOrientation (nesting cannot tell an inside-out solid from a cavity wall there)
        Func<DMesh3> mesh = () => Corpus.Combine(Corpus.Cube(0, 2), Corpus.Cube(1, 3), Corpus.Invert(Corpus.Box(5, 0, 0, 7, 2, 2)));
        Func<DMesh3> other = () => Corpus.Box(9, 0, 0, 10, 1, 1);
        Console.WriteLine("| scale 2^k | coordinates about | ExactMeshBoolean ByNesting: status, volume in the unit frame (16 = the inside-out box taken as empty) | pipeline ByNesting | pipeline ByNesting + AllowInsideOutInputs | pipeline, a clean pair of boxes at that scale |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (int k in new[] { 0, 100, 190, 198, 199, 200, 300, 338, 339, 340, 400, 1000, -100, -190, -200, -201, -202, -205, -300, -359, -360, -361, -400, -1000 }) {
            double s = Math.ScaleB(1, k);
            var f = new Frame { Name = "2^" + k, Map = p => s * p, Exact = true };
            var inputs = new List<DMesh3> { f.Apply(mesh(), true), f.Apply(other(), true) };
            var op = new ExactMeshBoolean(inputs, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = true, Orientation = ExactMeshBoolean.OrientationModes.ByNesting };
            string a;
            try {
                if (op.Compute()) {
                    var back = new DMesh3(op.Result);
                    foreach (int v in back.VertexIndices())
                        back.SetVertex(v, back.GetVertex(v) / s);
                    a = "Success (RoundingClean " + op.RoundingClean + "), " + F(Oracle.Volume(Oracle.Of(back, Vector3d.Zero)), "0.####");
                } else
                    a = op.Status.ToString();
            } catch (Exception e) { a = "EXCEPTION " + e.GetType().Name; }
            // a clean pair of boxes at the same scale, for what the pipeline does there at all
            var cleanPipe = new SolidBooleanPipeline(new List<DMesh3> { f.Apply(Corpus.Cube(0, 2), true), f.Apply(Corpus.Cube(1, 3), true) }, ExactMeshBoolean.Operations.Union);
            string d0;
            try { d0 = cleanPipe.Compute() ? "Success" : cleanPipe.Status + (cleanPipe.Status == SolidBooleanPipeline.PipelineStatus.BooleanFailed ? " / " + cleanPipe.BooleanStatus : ""); } catch (Exception e) { d0 = "EXCEPTION " + e.GetType().Name; }
            var pipe = new SolidBooleanPipeline(inputs, ExactMeshBoolean.Operations.Union) { Orientation = ExactMeshBoolean.OrientationModes.ByNesting };
            string b;
            try { b = pipe.Compute() ? "Success" : pipe.Status + (pipe.Status == SolidBooleanPipeline.PipelineStatus.BooleanFailed ? " / " + pipe.BooleanStatus : ""); } catch (Exception e) { b = "EXCEPTION " + e.GetType().Name; }
            var pipe2 = new SolidBooleanPipeline(inputs, ExactMeshBoolean.Operations.Union) { Orientation = ExactMeshBoolean.OrientationModes.ByNesting, AllowInsideOutInputs = true };
            string c2;
            try {
                if (pipe2.Compute()) {
                    // (the volume in the unit frame: the scale is a power of two, so dividing the coordinates is exact)
                    var back = new DMesh3(pipe2.Result);
                    foreach (int v in back.VertexIndices())
                        back.SetVertex(v, back.GetVertex(v) / s);
                    c2 = "Success, volume " + F(Oracle.Volume(Oracle.Of(back, Vector3d.Zero)), "0.####");
                } else
                    c2 = pipe2.Status.ToString() + (pipe2.Status == SolidBooleanPipeline.PipelineStatus.BooleanFailed ? " / " + pipe2.BooleanStatus : "");
            } catch (Exception e) { c2 = "EXCEPTION " + e.GetType().Name; }
            Console.WriteLine("| " + k + " | " + F(3 * s, "0.#e+0") + " | " + a + " | " + b + " | " + c2 + " | " + d0 + " |");
        }
    }

    // ---- mode small: thin solids at the limit of the rounding; what the pipeline returns and what its report says ----
    static void small()
    {
        Console.WriteLine("| frame | case | thickness | thickness / diagonal of the result | status | thin solid's volume / expected | components removed by the rounding (triangles) | NotCleanReason | anything in SolidBooleanReport says so |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        var frames = Corpus.Frames().Where(f => f.Name == "id" || f.Name == "rot" || f.Name == "rot+utm" || f.Name == "utm").ToList();
        foreach (Frame f in frames)
            foreach (string kind in new[] { "I slab", "D wall", "U plate" })
                foreach (int e in new[] { 10, 12, 14, 15, 16, 17, 18, 20, 30, 35, 40, 45, 50 }) {
                    double h = Math.ScaleB(1, -e);
                    var c = new Case { Class = "small", Name = kind + " 2^-" + e };
                    if (kind == "I slab") { c.Op = ExactMeshBoolean.Operations.Intersection; c.Given = () => new[] { Corpus.Cube(0, 2), Corpus.Box(2 - h, 0, 0, 4, 2, 2) }; }
                    else if (kind == "D wall") { c.Op = ExactMeshBoolean.Operations.Difference; c.Given = () => new[] { Corpus.Cube(0, 2), Corpus.Box(h, -1, -1, 3, 3, 3) }; }
                    else { c.Op = ExactMeshBoolean.Operations.Union; c.Given = () => new[] { Corpus.Cube(0, 2), Corpus.Box(3, 0, 0, 5, 2, h) }; }
                    double diag = kind == "U plate" ? Math.Sqrt(12) : Math.Sqrt(8 + h * h);     // (the largest component: the cube, or the slab itself)
                    Prepared prep = prepare(c, f, 200);
                    Outcome o = run(c, f, prep, Default);
                    string ratio = "", says = "";
                    if (o.Success) {
                        // the thin solid: the only component, or (the plate) the smaller of two
                        List<double> vols = Oracle.ComponentVolumes(prepResult);
                        double thin = kind == "U plate" ? (vols.Count == 2 ? vols[0] : 0) : (vols.Count == 1 ? vols[0] : 0);
                        ratio = F(thin / (4 * h), "0.####");
                        says = o.Report.ThinComponentsKept > 0 ? "ThinComponentsKept " + o.Report.ThinComponentsKept : (o.Report.RemovedThinSolids.Count > 0 ? "RemovedThinSolids" : "no");
                    }
                    Console.WriteLine("| " + f.Name + " | " + kind + " | 2^-" + e + " | " + F(h / diag, "0.#e+0") + " | " + (o.Success ? "Success" : o.Verdict) + " | " + ratio + " | "
                        + (o.Rounding != null ? o.Rounding.RemovedComponents + " (" + o.Rounding.RemovedTriangles + ")" : "-") + " | " + (o.Report != null ? "\"" + o.Report.NotCleanReason + "\"" : "") + " | " + says + " |");
                }
    }

    // ---- mode tiny: a well-shaped small solid beside a large one, in a union (the "tiny" rule of the rounding's component removal) ----
    static void tiny()
    {
        Console.WriteLine("| frame | small cube's edge | edge / diagonal of the large cube | status | components in the result | small cube's volume / expected | components removed by the rounding | Report.Clean |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (Frame f in Corpus.Frames().Where(f => f.Name == "id" || f.Name == "rot" || f.Name == "rot+utm"))
            foreach (int e in new[] { 0, 4, 8, 9, 10, 11, 12, 16 }) {
                double a = Math.ScaleB(1, -e);
                var c = new Case { Class = "tiny", Name = "cube 2^-" + e, Op = ExactMeshBoolean.Operations.Union, Given = () => new[] { Corpus.Cube(0, 1024), Corpus.Box(1100, 0, 0, 1100 + a, a, a) } };
                Prepared prep = prepare(c, f, 200);
                Outcome o = run(c, f, prep, Default);
                string comps = "", ratio = "";
                if (o.Success) {
                    List<double> vols = Oracle.ComponentVolumes(prepResult);
                    comps = vols.Count.ToString();
                    ratio = F(vols.Count == 2 ? vols[0] / (a * a * a) : 0, "0.####");
                }
                Console.WriteLine("| " + f.Name + " | 2^-" + e + " | " + F(a / (1024 * Math.Sqrt(3)), "0.#e+0") + " | " + (o.Success ? "Success" : o.Verdict) + " | " + comps + " | " + ratio + " | "
                    + (o.Rounding != null ? o.Rounding.RemovedComponents.ToString() : "-") + " | " + (o.Report != null ? o.Report.Clean.ToString() : "") + " |");
            }
    }

    // ---- mode utm32: float32 coordinates at UTM magnitudes (an STL written there), by model size ----
    static void utm32()
    {
        Console.WriteLine("float32 grid at the UTM point (500000, 5500000, 100): x " + F(MathF.BitIncrement(500000f) - 500000f) + ", y " + F(MathF.BitIncrement(5500000f) - 5500000f) + ", z " + F(MathF.BitIncrement(100f) - 100f));
        Console.WriteLine();
        Console.WriteLine("| model size (m) | case | zero-area triangles in the float32 inputs | damage of the float32 rounding (share of points that changed side) | default | ByNesting | AutoRepairThenRobust | InputGrid 2^-1 | recentred before float32 (the caller's fix): damage, default |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (double size in new[] { 0.5, 2, 8, 32, 128, 512 })
            foreach (string kind in new[] { "sphere U sphere", "box D cylinder", "box U box" }) {
                Func<DMesh3[]> canonical;
                ExactMeshBoolean.Operations op = ExactMeshBoolean.Operations.Union;
                if (kind == "sphere U sphere") canonical = () => new[] { Corpus.Sphere(Vector3d.Zero, 1, 12), Corpus.Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 12) };
                else if (kind == "box D cylinder") { canonical = () => new[] { Corpus.Cube(-1, 1), Corpus.Cylinder(new Vector3d(0.2, -1.5, 0.1), 0.4, 3, 24) }; op = ExactMeshBoolean.Operations.Difference; }
                else canonical = () => new[] { Corpus.Cube(-1, 1), Corpus.Cube(0, 2) };
                var c = new Case { Class = "utm32", Name = kind, Op = op, Given = canonical };
                Func<Func<Vector3d, Vector3d>, (string, string, string, string, string, int)> study = place => {
                    // the model in doubles, and what the float32 file holds; the pipeline is judged against the solid of the
                    // float32 meshes (what it was given), the damage is the float32 solid against the double one
                    var f64 = new Frame { Name = "f64", Map = place };
                    var f32 = new Frame { Name = "f32", Map = p => Corpus.Float32(place(p)), Soup = true };
                    Prepared model = prepare(c, f64, 6000);
                    Prepared prep = prepare(c, f32, 6000);
                    int zero = 0;
                    foreach (DMesh3 m in prep.Given)
                        foreach (int t in m.TriangleIndices())
                            if (m.GetTriArea(t) == 0) zero++;
                    sbyte[] rounded = Oracle.Truth(prep.Intent, truthOf(op), model.Points, model.Origin, model.Scale, 1);
                    int n = 0, diff = 0;
                    for (int i = 0; i < rounded.Length; ++i)
                        if (rounded[i] >= 0 && model.Truth[i] >= 0) { n++; if (rounded[i] != model.Truth[i]) diff++; }
                    Func<Outcome, string> show = o => o.Verdict + (o.Judgement != null && o.Verdict != "ok" ? " (" + F(o.Judgement.Fraction, "0.####") + ")" : "");
                    return (F(n > 0 ? (double)diff / n : 0, "0.####"), show(run(c, f32, prep, Default)), show(run(c, f32, prep, Escalations[0])), show(run(c, f32, prep, Escalations[4])),
                        show(run(c, f32, prep, new Config { Name = "grid", Set = p => p.InputGrid = 0.5 })), zero);
                };
                var utm = study(p => size * Corpus.Rotate(p) + Corpus.Utm);
                var local = study(p => size * Corpus.Rotate(p));
                Console.WriteLine("| " + F(2 * size, "0.##") + " | " + kind + " | " + utm.Item6 + " | " + utm.Item1 + " | " + utm.Item2 + " | " + utm.Item3 + " | " + utm.Item4 + " | " + utm.Item5 + " | " + local.Item1 + ", " + local.Item2 + " |");
            }
    }

    // ---- mode repair: open inputs, the candidates a repair selection rule could choose from, judged against the intent ----
    static void repair()
    {
        Console.WriteLine("| case | repair | closed after | area after / area given | volume / intent | mismatch against the intent | ms |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        var cases = new List<(string, Func<DMesh3>, Func<DMesh3>)> {
            ("cube, 1 triangle missing", () => Corpus.Without(Corpus.Cube(0, 2), (t, c) => t == 2), () => Corpus.Cube(0, 2)),
            ("cube, 1 face missing", () => Corpus.Without(Corpus.Cube(0, 2), (t, c) => c.z > 1.99), () => Corpus.Cube(0, 2)),
            ("cube, 2 adjacent faces missing", () => Corpus.Without(Corpus.Cube(0, 2), (t, c) => c.z > 1.99 || c.x > 1.99), () => Corpus.Cube(0, 2)),
            ("sphere, 1 triangle missing", () => Corpus.Without(Corpus.Sphere(Vector3d.Zero, 1, 12), (t, c) => t == 5), () => Corpus.Sphere(Vector3d.Zero, 1, 12)),
            ("sphere, cap missing (z > 0.8)", () => Corpus.Without(Corpus.Sphere(Vector3d.Zero, 1, 12), (t, c) => c.z > 0.8), () => Corpus.Sphere(Vector3d.Zero, 1, 12)),
            ("sphere, half missing (z > 0)", () => Corpus.Without(Corpus.Sphere(Vector3d.Zero, 1, 12), (t, c) => c.z > 0), () => Corpus.Sphere(Vector3d.Zero, 1, 12)),
            ("torus, 20 scattered triangles missing", () => Corpus.Without(Corpus.Torus(Vector3d.Zero, 1, 0.3, 32, 12), (t, c) => t % 37 == 0), () => Corpus.Torus(Vector3d.Zero, 1, 0.3, 32, 12)),
            ("T-junction cube", () => Corpus.TJunctionCube(), () => Corpus.Cube(0, 2)),
            ("cube with a duplicated face", () => Corpus.Combine(Corpus.Cube(0, 2), Corpus.Without(Corpus.Cube(0, 2), (t, c) => c.z < 1.99)), () => Corpus.Cube(0, 2)),
            ("cube with a debris triangle", () => Corpus.WithTriangle(Corpus.Cube(0, 2), new Vector3d(9, 9, 9), new Vector3d(9.5, 9, 9), new Vector3d(9, 9.5, 9)), () => Corpus.Cube(0, 2)),
            ("cube, one triangle flipped", () => Corpus.FlipTriangles(Corpus.Cube(0, 2), 3), () => Corpus.Cube(0, 2)),
            ("cube, half of the faces flipped", () => Corpus.FlipTriangles(Corpus.Cube(0, 2), 0, 1, 4, 5, 8, 9), () => Corpus.Cube(0, 2)),
            ("cube with a debris triangle (welded)", () => Corpus.WithTriangle(Corpus.Cube(0, 2), new Vector3d(9, 9, 9), new Vector3d(9.5, 9, 9), new Vector3d(9, 9.5, 9)), () => Corpus.Cube(0, 2)),
            ("cutting sheet (one face of a box 6 x 6 x 7)", () => Corpus.Without(Corpus.Box(-1, -1, 2, 5, 5, 9), (t, c) => c.z > 2.01), () => Corpus.Box(-1, -1, 2, 5, 5, 9)),
        };
        foreach (var (name, given, intent) in cases) {
            DMesh3 g = name.EndsWith("(welded)") ? given() : Corpus.Soup(given()), want = intent();
            Vector3d origin = want.GetBounds().Center;
            double wantVolume = Oracle.Volume(Oracle.Of(want, origin));
            Vector3d[] pts = Oracle.SamplePoints(new[] { want, g }, 6000, 99);
            double scale = Oracle.Normalizer(want.GetBounds().DiagonalLength);
            sbyte[] truth = Oracle.Truth(new[] { want }, w => w[0], pts, origin, scale, 5);
            foreach (string method in new[] { "MeshAutoRepair", "MeshRobustRepair", "weld only (MergeCoincidentEdges)" }) {
                var clock = Stopwatch.StartNew();
                DMesh3 r;
                try {
                    if (method == "MeshAutoRepair") { var a = new MeshAutoRepair(new DMesh3(g)); a.Apply(); r = a.Mesh; }
                    else if (method == "MeshRobustRepair") { var a = new MeshRobustRepair(new DMesh3(g)); a.Apply(); r = a.Mesh; }
                    else { r = new DMesh3(g); new MergeCoincidentEdges(r).Apply(); }
                } catch (Exception e) {
                    Console.WriteLine("| " + name + " | " + method + " | EXCEPTION " + e.GetType().Name + " | | | |");
                    continue;
                }
                long ms = clock.ElapsedMilliseconds;
                bool closed = r.TriangleCount > 0 && Oracle.Closed(r);
                Oracle.Judgement j = Oracle.Judge(r, truth, pts, origin, scale, 6);
                Console.WriteLine("| " + name + " | " + method + " | " + (closed ? "yes" : "no") + " | " + F(area(r) / area(g), "0.###") + " | " + F(Oracle.Volume(Oracle.Of(r, origin)) / wantVolume, "0.####") + " | " + F(j.Fraction, "0.####") + " | " + ms + " |");
            }
        }
    }

    static double area(DMesh3 m)
    {
        double a = 0;
        foreach (int t in m.TriangleIndices())
            a += m.GetTriArea(t);
        return a;
    }

    // ---- mode reduce: the reduction after the boolean (TargetFraction 0.25), judged like the boolean result ----
    static void reduce()
    {
        Console.WriteLine("| case | frame | boolean triangles | result triangles | stop | no MaxDeviation: verdict, mismatch, volume / unreduced | MaxDeviation 1e-3 diagonal: triangles, verdict, mismatch, volume / unreduced |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        var frames = Corpus.Frames().Where(f => f.Name == "soup" || f.Name == "rot+utm").ToList();
        foreach (Case c in Corpus.Cases().Where(c => c.Class == "base" || c.Class == "nested" || c.Class == "touch" || c.Class == "nonmanifold" || c.Name.Contains("small solid") || c.Name.Contains("needle")))
            foreach (Frame f in frames) {
                Prepared prep = prepare(c, f, 6000);
                Outcome plain = run(c, f, prep, Default);
                if (plain.Verdict != "ok" || plain.ResultTriangles <= 12)
                    continue;
                Outcome a = run(c, f, prep, new Config { Name = "reduce", Set = p => p.TargetFraction = 0.25 });
                Outcome b = run(c, f, prep, new Config { Name = "reduce+dev", Set = p => { p.TargetFraction = 0.25; p.MaxDeviation = 1e-3 * prep.Diagonal; } });
                Func<Outcome, string> show = o => o.Verdict + (o.Judgement != null ? ", " + F(o.Judgement.Fraction, "0.####") + ", " + F(o.Volume / plain.Volume, "0.#####") : "");
                Console.WriteLine("| " + c.Name + " | " + f.Name + " | " + plain.ResultTriangles + " | " + a.ResultTriangles + " | " + (a.Report != null ? a.Report.StopReason.ToString() : "") + " | " + show(a) + " | " + b.ResultTriangles + ", " + show(b) + " |");
            }
    }

    // ---- mode report: what Status, Error and SolidBooleanReport tell the caller in the situations the corpus found ----
    static void report()
    {
        Frame id = Corpus.Frames()[0], rot = Corpus.Frames().First(f => f.Name == "rot");
        double h = Math.ScaleB(1, -16);
        Action<string, Frame, ExactMeshBoolean.Operations, Func<DMesh3[]>, Action<SolidBooleanPipeline>, double> show = (name, f, op, given, set, expectVolume) => {
            var pipe = new SolidBooleanPipeline(given().Select(m => f.Apply(m, true)).ToList(), op);
            ExactMeshBoolean last = null;
            pipe.ConfigureBoolean = b => last = b;
            set(pipe);
            bool ok = pipe.Compute();
            SolidBooleanReport r = pipe.Report;
            Console.WriteLine("- **" + name + "**: Status `" + pipe.Status + "`, BooleanStatus `" + pipe.BooleanStatus + "`, Error `" + (pipe.Error ?? "") + "`"
                + (ok ? "; result volume " + F(Oracle.Volume(Oracle.Of(pipe.Result, pipe.Result.TriangleCount > 0 ? pipe.Result.GetBounds().Center : Vector3d.Zero)), "0.#####") + " (expected " + F(expectVolume, "0.#####") + "), "
                    + pipe.Result.TriangleCount + " triangles, " + Oracle.ComponentVolumes(pipe.Result).Count + " component(s)" : "")
                + "; Report: Clean " + r.Clean + ", NotCleanReason \"" + r.NotCleanReason + "\", InputClosed [" + string.Join(",", r.InputClosed) + "], RepairedInputs " + r.RepairedInputs.Count
                + ", InsideOutCells " + r.InsideOutCells + ", ThinComponentsKept " + r.ThinComponentsKept + ", RemovedThinSolids " + r.RemovedThinSolids.Count + ", StopReason " + r.StopReason
                + "; only on the boolean (through ConfigureBoolean): " + (last != null && last.Rounding != null ? "Rounding.RemovedComponents " + last.Rounding.RemovedComponents + ", RoundingClean " + last.RoundingClean + ", ReorientedTriangles " + last.ReorientedTriangles : "no rounding ran"));
        };
        show("open second input", id, ExactMeshBoolean.Operations.Union, () => new[] { Corpus.Cube(0, 2), Corpus.Without(Corpus.Cube(1, 3), (t, c) => t == 2) }, p => { }, 15);
        show("slab 2^-16 of an intersection, rotated frame", rot, ExactMeshBoolean.Operations.Intersection, () => new[] { Corpus.Cube(0, 2), Corpus.Box(2 - h, 0, 0, 4, 2, 2) }, p => { }, 4 * h);
        show("the same with SafeRounding = false set in ConfigureBoolean", rot, ExactMeshBoolean.Operations.Intersection, () => new[] { Corpus.Cube(0, 2), Corpus.Box(2 - h, 0, 0, 4, 2, 2) },
            p => { var keep = p.ConfigureBoolean; p.ConfigureBoolean = b => { keep(b); b.SafeRounding = false; }; }, 4 * h);
        show("empty intersection (face touch)", id, ExactMeshBoolean.Operations.Intersection, () => new[] { Corpus.Cube(0, 2), Corpus.Box(2, 0, 0, 4, 2, 2) }, p => { }, 0);
        show("box U box, TargetFraction 0.25, no MaxDeviation", id, ExactMeshBoolean.Operations.Union, () => new[] { Corpus.Cube(0, 2), Corpus.Cube(1, 3) }, p => p.TargetFraction = 0.25, 15);
        show("cavity wall given outward", id, ExactMeshBoolean.Operations.Union, () => new[] { Corpus.Combine(Corpus.Cube(0, 4), Corpus.Cube(1, 3)), Corpus.Box(6, 0, 0, 7, 1, 1) }, p => { }, 57);
        show("cutting sheet with AutoRepair", id, ExactMeshBoolean.Operations.Difference, () => new[] { Corpus.Cube(0, 4), Corpus.Without(Corpus.Box(-1, -1, 2, 5, 5, 9), (t, c) => c.z > 2.01) },
            p => p.InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair, 32);
        show("a cube of 2^-10 beside a cube of 1024 (1 : 2^20), union", id, ExactMeshBoolean.Operations.Union,
            () => new[] { Corpus.Cube(0, 1024), Corpus.Box(1100, 0, 0, 1100 + Math.ScaleB(1, -10), Math.ScaleB(1, -10), Math.ScaleB(1, -10)) }, p => { }, 1073741824.0);
        show("a cube of 2^-20 beside a cube of 1024 (1 : 2^30), union", id, ExactMeshBoolean.Operations.Union,
            () => new[] { Corpus.Cube(0, 1024), Corpus.Box(1100, 0, 0, 1100 + Math.ScaleB(1, -20), Math.ScaleB(1, -20), Math.ScaleB(1, -20)) }, p => { }, 1073741824.0);
        show("inside-out with CountInsideOutCells = false set in ConfigureBoolean", id, ExactMeshBoolean.Operations.Union, () => new[] { Corpus.Invert(Corpus.Cube(0, 2)), Corpus.Cube(1, 3) },
            p => { var keep = p.ConfigureBoolean; p.ConfigureBoolean = b => { keep(b); b.CountInsideOutCells = false; }; }, 15);
    }

    // ---- mode perf: the share of each stage, and the pipeline's checks against the boolean alone ----
    static void perf()
    {
        Console.WriteLine("| case | input triangles | plain ExactMeshBoolean ms | pipeline total ms | input check | boolean | final check | checks / total | pipeline with Orientation = ByNesting ms |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (int n in new[] { 16, 48, 128, 256 }) {
            foreach (string kind in new[] { "sphere U sphere", "sphere D sphere" }) {
                var inputs = new List<DMesh3> { Corpus.Soup(Corpus.Sphere(Vector3d.Zero, 1, n)), Corpus.Soup(Corpus.Sphere(new Vector3d(1.2, 0.1, 0.05), kind.Contains("D") ? 0.6 : 1, n)) };
                var op = kind.Contains("D") ? ExactMeshBoolean.Operations.Difference : ExactMeshBoolean.Operations.Union;
                long plain = long.MaxValue, total = long.MaxValue, check = 0, boolean = 0, final = 0, nesting = long.MaxValue;
                for (int rep = 0; rep < 3; ++rep) {
                    var clock = Stopwatch.StartNew();
                    new ExactMeshBoolean(inputs, op) { AllowOpenMeshes = true }.Compute();
                    plain = Math.Min(plain, clock.ElapsedMilliseconds);
                    var pipe = new SolidBooleanPipeline(inputs, op);
                    pipe.Compute();
                    if (pipe.Report.TotalMilliseconds < total) {
                        total = pipe.Report.TotalMilliseconds; check = pipe.Report.InputCheckMilliseconds; boolean = pipe.Report.BooleanMilliseconds; final = pipe.Report.FinalCheckMilliseconds;
                    }
                    var nest = new SolidBooleanPipeline(inputs, op) { Orientation = ExactMeshBoolean.OrientationModes.ByNesting };
                    nest.Compute();
                    nesting = Math.Min(nesting, nest.Report.TotalMilliseconds);
                }
                Console.WriteLine("| " + kind + " | " + inputs.Sum(m => m.TriangleCount) + " | " + plain + " | " + total + " | " + check + " | " + boolean + " | " + final + " | " + F(total > 0 ? (double)(check + final) / total : 0, "0.##") + " | " + nesting + " |");
            }
        }
    }

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        // the oracle checks itself first: the volume and the winding of a cube and of a hollow cube
        var cube = Oracle.Of(Corpus.Cube(0, 2), Vector3d.Zero);
        var hollow = Oracle.Of(Corpus.Combine(Corpus.Cube(0, 4), Corpus.Invert(Corpus.Cube(1, 3))), Vector3d.Zero);
        var rng = new Random(1);
        if (Oracle.Volume(cube) != 8 || Oracle.Volume(hollow) != 56 || Oracle.Winding(cube, new Vector3d(1, 1.1, 0.9), rng) != 1 || Oracle.Winding(cube, new Vector3d(3, 1, 1), rng) != 0
            || Oracle.Winding(hollow, new Vector3d(2, 2.1, 1.9), rng) != 0 || Oracle.Winding(hollow, new Vector3d(0.5, 2, 2), rng) != 1 || !Oracle.Closed(Corpus.Soup(Corpus.Cube(0, 2)))
            || Oracle.Closed(Corpus.TJunctionCube()) || Dyadic.Orient(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, new Vector3d(0, 0, 0.5)).ToDouble() != 0.5) {
            Console.WriteLine("oracle self-check failed");
            return 2;
        }
        // and it must tell a known-wrong result from the right one, at the three scales of the corpus: the hollow cube
        // against itself (no point differs) and against the filled cube (the cavity is falsely solid)
        foreach (double k in new[] { 1.0, Math.ScaleB(1, 40), Math.ScaleB(1, -40) }) {
            var fr = new Frame { Name = "self", Map = p => k * p };
            DMesh3 right = fr.Apply(Corpus.Combine(Corpus.Cube(0, 4), Corpus.Invert(Corpus.Cube(1, 3))), false), filled = fr.Apply(Corpus.Cube(0, 4), false);
            Vector3d o = right.GetBounds().Center;
            double sc = Oracle.Normalizer(right.GetBounds().DiagonalLength);
            Vector3d[] pts = Oracle.SamplePoints(new[] { right }, 2000, 3);
            sbyte[] truth = Oracle.Truth(new[] { right }, w => w[0], pts, o, sc, 1);
            Oracle.Judgement good = Oracle.Judge(right, truth, pts, o, sc, 2), bad = Oracle.Judge(filled, truth, pts, o, sc, 2);
            if (good.Mismatch != 0 || good.Samples < 1900 || bad.Fraction < 0.02 || bad.FalseEmpty != 0 || bad.Samples < 1900) {
                Console.WriteLine("oracle self-check failed (known-wrong result at scale " + k + ")");
                return 2;
            }
        }
        string mode = args.Length > 0 ? args[0] : "corpus";
        switch (mode) {
            case "corpus": corpus(args); break;
            case "touch": touch(); break;
            case "negvol": negvol(); break;
            case "small": small(); break;
            case "utm32": utm32(); break;
            case "repair": repair(); break;
            case "perf": perf(); break;
            case "reduce": reduce(); break;
            case "report": report(); break;
            case "tiny": tiny(); break;
            default: Console.WriteLine("modes: corpus [class or case name] | touch | negvol | small | tiny | utm32 | repair | reduce | report | perf"); return 1;
        }
        return 0;
    }
}
