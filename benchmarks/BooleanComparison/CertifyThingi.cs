// The certifying checker (tests/.../Harness/BooleanCertificate.cs) on real meshes: self-unions of closed Thingi10K models and
// the pairs of the thingi-pairs and thingi-contact classes (docs/research/CERTIFICATE_HARDENING_2026-11.md). One case per
// process (the driver data/certify_thingi.py runs each with a timeout); for each operation ExactMeshBoolean with the
// certificate recorded, then BooleanCertificate.Check (CertifyRow: the reusable entry point, two meshes and an operation in, one
// verdict row out). The output is one JSON row per operation on stdout: ids, sizes, status, verdict, the failing clause and its
// message, times. No mesh and no derived mesh is written anywhere.
//
//   certify thingi self <mesh>                                   A op A (a copy), as read
//   certify thingi pair <meshA> <meshB> qx qy qz qw tx ty tz     as thingi-pair (ThingiPairs.cs: unit box, B rotated and moved)
//   certify thingi contact <meshA> <meshB> variant axis sign p   as thingi-contact (ContactTransforms.cs, exact placements)
//   optional last arguments: ops=union,intersection,difference  split=N (BooleanCertificate.SplitDepth, default 3)  nocheck
//   (the boolean only: its time and status, to tell a slow boolean from a slow checker)  clauses (the time of each clause, on stderr)
//   compare=region  (E' stage 4, #1630) instead of the certificate: each operation with and without ExactMeshBoolean.RegionClipping, the rows of
//   RegionClippingCompare (status, reason, exact difference of the results, the certificate with the switch on, times); `region-compare thingi ...` is this
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BoolCmp;
using g3;
using gs;
using geometry3Sharp.Tests;
using Ops = g3.ExactMeshBoolean.Operations;

static class CertifyThingi
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = ci;
        CultureInfo.CurrentCulture = ci;
        var ops = new List<Ops> { Ops.Union, Ops.Intersection, Ops.Difference };
        var opArg = args.FirstOrDefault(s => s.StartsWith("ops="));
        if (opArg != null) {
            ops = new List<Ops>();
            foreach (string o in opArg.Substring(4).Split(',')) {
                if (o == "union") ops.Add(Ops.Union);
                else if (o == "intersection") ops.Add(Ops.Intersection);
                else if (o == "difference") ops.Add(Ops.Difference);
                else { Console.Error.WriteLine($"certify thingi: unknown operation '{o}' in {opArg} (union, intersection, difference)"); return 1; }
            }
            args = args.Where(s => s != opArg).ToArray();
        }
        int split = -1;
        var splitArg = args.FirstOrDefault(s => s.StartsWith("split="));
        if (splitArg != null) { split = int.Parse(splitArg.Substring(6), ci); args = args.Where(s => s != splitArg).ToArray(); }
        bool noCheck = args.Contains("nocheck"), clauses = args.Contains("clauses"), region = args.Contains("compare=region");
        args = args.Where(s => s != "nocheck" && s != "clauses" && s != "compare=region").ToArray();
        string kind = args[0];
        DMesh3 a = Program.Load(args[1]), b = kind == "self" ? null : Program.Load(args[2]);
        var row = new Dictionary<string, object> { ["kind"] = kind, ["a"] = Path.GetFileNameWithoutExtension(args[1]),
            ["b"] = kind == "self" ? Path.GetFileNameWithoutExtension(args[1]) : Path.GetFileNameWithoutExtension(args[2]) };
        if (a == null || (kind != "self" && b == null)) { Emit(row, "error", "load failed"); return 0; }
        switch (kind) {
            case "self":
                b = new DMesh3(a);
                break;
            case "pair": {
                double[] p = args.Skip(3).Take(7).Select(x => double.Parse(x, ci)).ToArray();
                var q = new Quaterniond(p[0], p[1], p[2], p[3]);
                q.Normalize();
                ThingiPairs.Normalize(a, Quaterniond.Identity, Vector3d.Zero);
                ThingiPairs.Normalize(b, q, new Vector3d(p[4], p[5], p[6]));
                break;
            }
            case "contact": {
                ContactResult r = ContactTransforms.Build(a, b, ContactTransforms.ParseVariant(args[3]), int.Parse(args[4], ci), int.Parse(args[5], ci), int.Parse(args[6], ci), region);      // (region: a snap to a grid where no exact placement exists, so that the case is compared)
                row["variant"] = args[3];
                if (r.Info.ContainsKey("snapped")) { row["snapped"] = r.Info["snapped"]; row["inexact_before"] = r.Info["inexact_before"]; }
                if (r.Error != null) { Emit(row, "error", "inexact transform: " + r.Error); return 0; }
                a = r.A; b = r.B;
                break;
            }
            default:
                Console.Error.WriteLine("usage: certify thingi (self <mesh> | pair <a> <b> qx qy qz qw tx ty tz | contact <a> <b> variant axis sign param) [ops=union,...]");
                return 1;
        }
        foreach (Ops op in ops) {
            Dictionary<string, object> r = region ? RegionClippingCompare.Row(new List<DMesh3> { a, b }, op) : CertifyRow(a, b, op, split, noCheck, clauses);
            foreach (var kv in row) r[kv.Key] = kv.Value;
            Console.WriteLine(JsonSerializer.Serialize(r));
            Console.Out.Flush();
        }
        return 0;
    }

    /// <summary>
    /// The reusable entry point: a op b by ExactMeshBoolean with the certificate recorded, then BooleanCertificate.Check. Returns
    /// one verdict row (the format of the benchmark README, "The checker on Thingi10K"): op, trisA, trisB, inputs_clean,
    /// t_bool_ms, status, result_tris, t_check_ms, pieces, uncovered, verdict (certified | uncovered | removed parts | rejected |
    /// not applicable | boolean failed | boolean exception | checker exception | boolean only), clause (C0, C1, C2, C3/C4 or C5,
    /// only for rejected), message (Check's text, at most 400 characters). split: SplitDepth (negative: the default 3); noCheck:
    /// the boolean only; clauses: each clause timed on stderr as well. The meshes are not changed and nothing is written.
    /// </summary>
    public static Dictionary<string, object> CertifyRow(DMesh3 a, DMesh3 b, Ops op, int split = -1, bool noCheck = false, bool clauses = false)
    {
        var r = new Dictionary<string, object> { ["op"] = op.ToString().ToLowerInvariant() };
        if (split >= 0) r["split"] = split;
        r["trisA"] = a.TriangleCount; r["trisB"] = b.TriangleCount;
        r["inputs_clean"] = MeshRobustRepair.NotCleanReason(a) == "" && MeshRobustRepair.NotCleanReason(b) == "";
        var eb = new ExactMeshBoolean(new List<DMesh3> { a, b }, op) { AllowOpenMeshes = true, RecordCertificate = true, ComputeExactVertices = true };
        if (op == Ops.Intersection) eb.CullOutsideA = false;     // the checker's class (BooleanCertificate's comment)
        var sw = Stopwatch.StartNew();
        bool ok;
        try { ok = eb.Compute(); }
        catch (Exception e) { r["t_bool_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1); return Verdict(r, "boolean exception", e.GetType().Name + ": " + e.Message); }
        r["t_bool_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        r["status"] = eb.Status.ToString();
        if (!ok) return Verdict(r, "boolean failed", eb.Error);
        r["result_tris"] = eb.Result.TriangleCount;
        if (noCheck) return Verdict(r, "boolean only", "");
        sw.Restart();
        string v;
        BooleanCertificate cert;
        try {
            cert = BooleanCertificate.From(eb);
            if (split >= 0) cert.SplitDepth = split;
            if (clauses) {
                // (the clauses of Check in its order, each timed; the verdict is Check's)
                var parts = new (string, Func<string>)[] { ("C0", cert.CheckInputs), ("C1", cert.CheckPieces), ("C2", () => cert.CheckEmbedding()),
                    ("C3/C4", cert.CheckClassification), ("C5", cert.CheckResult) };
                foreach (var (n, f) in parts) {
                    var cw = Stopwatch.StartNew();
                    // (on a thread, so that C2's pair count can be reported while it runs: the count is set before the pair tests)
                    var task = System.Threading.Tasks.Task.Run(f);
                    while (task.Wait(30000) == false)
                        Console.Error.WriteLine($"{r["op"]} {n}: running {cw.Elapsed.TotalSeconds:0} s; C2 pieces {cert.EmbeddingPieces}, copies {cert.EmbeddingCopies}, pairs {cert.EmbeddingPairs}");
                    string x = task.Result;
                    Console.Error.WriteLine($"{r["op"]} {n}: {cw.Elapsed.TotalMilliseconds:0} ms {(x == "" ? "" : "'" + x + "'")}"
                        + (n == "C2" ? $" (pieces {cert.EmbeddingPieces}, copies {cert.EmbeddingCopies}, pairs {cert.EmbeddingPairs}, coplanar {cert.EmbeddingCoplanarPairs})" : ""));
                }
                cert = BooleanCertificate.From(eb);
                if (split >= 0) cert.SplitDepth = split;
                sw.Restart();
            }
            v = cert.Check();
        }
        catch (Exception e) { r["t_check_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1); return Verdict(r, "checker exception", e.GetType().Name + ": " + e.Message); }
        r["t_check_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        r["pieces"] = cert.PieceCorners?.Length ?? 0;
        r["uncovered"] = cert.Uncovered;
        string verdict;
        if (v.StartsWith("n/a")) verdict = "not applicable";
        else if (v.StartsWith("not clean")) verdict = "removed parts";
        else if (v != "") verdict = "rejected";
        else verdict = cert.Uncovered == 0 ? "certified" : "uncovered";
        if (verdict == "rejected") r["clause"] = v.Substring(0, v.IndexOf(':'));
        return Verdict(r, verdict, v);
    }

    static Dictionary<string, object> Verdict(Dictionary<string, object> row, string verdict, string message)
    {
        row["verdict"] = verdict;
        if (!string.IsNullOrEmpty(message)) row["message"] = message.Length > 400 ? message.Substring(0, 400) : message;
        return row;
    }

    static void Emit(Dictionary<string, object> row, string verdict, string message)
    {
        Verdict(row, verdict, message);
        Console.WriteLine(JsonSerializer.Serialize(row));
        Console.Out.Flush();
    }
}
