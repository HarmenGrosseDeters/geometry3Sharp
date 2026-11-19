// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Measurement only (no library change): (3) endpoint fallback of the midpoint collapse in FastCollapsePass with
// MaxDeviationTwoSided, and (4) certificate lifetime of the reverse (two-sided) certification. Both run on ReducerProbe /
// ReverseProbe, benchmark-only copies of mesh/Reducer.cs and mesh/ReducerReverseCoverage.cs with counters and hooks.
//
//   dotnet run -c Release --project benchmarks/ReducerBenchmark -- collapsepoint [rounds] [mesh files...]
//   (environment variable CP_ONLY=text: only the cases whose name contains the text)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class CollapsePoint
{
    static (double up, double low) Hausdorff(DMesh3 from, DMesh3 to, double eps)
    {
        var h = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = 0.01 * eps };
        h.Compute();
        return (h.UpperBound / eps, h.LowerBound / eps);
    }

    static readonly ReducerProbe.Reason[] Reasons = { ReducerProbe.Reason.Flip, ReducerProbe.Reason.ForwardDev, ReducerProbe.Reason.Reverse, ReducerProbe.Reason.Intersection, ReducerProbe.Reason.SampleFar };

    static ReducerProbe Probe(DMesh3 input, double eps, int mode, bool record)
    {
        return new ReducerProbe(new DMesh3(input)) { MaxDeviation = eps, MaxDeviationTwoSided = true, EndpointMode = mode, RecordReverse = record };
    }

    internal static void Run(Func<int, DMesh3> bumpy, Func<int, DMesh3> terrain, string[] args)
    {
        int rounds = args.Length > 1 && int.TryParse(args[1], out int r) ? r : 3;
        string only = Environment.GetEnvironmentVariable("CP_ONLY");
        var cases = new List<(string, DMesh3)> { ("bumpy sphere 40", bumpy(40)), ("spiky sphere 60", TwoSided.SpikySphere(60)), ("terrain 80", terrain(80)) };
        foreach (string f in args.Skip(1).Where(x => !int.TryParse(x, out int _))) {
            DMesh3 m = StandardMeshReader.ReadMesh(f);
            if (m != null && m.TriangleCount > 0) cases.Add((System.IO.Path.GetFileNameWithoutExtension(f), m));
        }
        if (only != null) cases = cases.Where(c => c.Item1.Contains(only)).ToList();
        Console.WriteLine($"rounds: {rounds}, processors: {Environment.ProcessorCount}");

        // warm-up of the JIT on a small case
        {
            var wm = bumpy(20); double e = 0.003 * wm.CachedBounds.DiagonalLength;
            new Reducer(new DMesh3(wm)) { MaxDeviation = e, MaxDeviationTwoSided = true }.FastCollapsePass(0.05 * wm.CachedBounds.DiagonalLength, 5);
            foreach (int mode in new[] { 0, 1, 2 }) Probe(wm, e, mode, mode == 0).FastCollapsePass(0.05 * wm.CachedBounds.DiagonalLength, 5);
        }

        var rows = new List<string>(); var hit = new List<string>(); var full = new List<string>(); var life = new List<string>(); var endrej = new List<string>();
        foreach (var (name, input) in cases)
            foreach (double rel in new[] { 3e-3, 1e-2 }) {
                double diag = input.CachedBounds.DiagonalLength, eps = rel * diag, len = 0.05 * diag;
                string label = $"{name} @ {rel:0.###}";
                Console.Error.WriteLine("case " + label);

                // timing: library (baseline), probe without endpoint retry (same result), probe with endpoint fallback
                var tl = new List<double>(); var t0 = new List<double>(); var t2 = new List<double>(); var checkMs = new List<double>();
                DMesh3 ml = null, m0 = null, m2 = null; var hashes2 = new HashSet<string>(); ReducerProbe p0 = null, p2 = null;
                for (int k = 0; k < rounds; ++k) {
                    ml = new DMesh3(input);
                    var sw = Stopwatch.StartNew();
                    new Reducer(ml) { MaxDeviation = eps, MaxDeviationTwoSided = true }.FastCollapsePass(len, 5);
                    tl.Add(sw.Elapsed.TotalMilliseconds);
                    p0 = Probe(input, eps, 0, false); m0 = p0.Mesh;
                    sw.Restart(); p0.FastCollapsePass(len, 5); t0.Add(sw.Elapsed.TotalMilliseconds);
                    checkMs.Add(1000.0 * p0.Reverse.TicksCheck / Stopwatch.Frequency);
                    p2 = Probe(input, eps, 2, false); m2 = p2.Mesh;
                    sw.Restart(); p2.FastCollapsePass(len, 5); t2.Add(sw.Elapsed.TotalMilliseconds);
                    hashes2.Add(ReducerHashes.Hash(m2));
                }
                tl.Sort(); t0.Sort(); t2.Sort(); checkMs.Sort();
                double bl = tl[tl.Count / 2], b0 = t0[t0.Count / 2], b2 = t2[t2.Count / 2], cm = checkMs[checkMs.Count / 2];
                string hl = ReducerHashes.Hash(ml), h0 = ReducerHashes.Hash(m0);

                // (a), (b): the endpoint retry of every rejected midpoint, dry run (the sequence stays that of the baseline)
                var p1 = Probe(input, eps, 1, false);
                p1.FastCollapsePass(len, 5);
                string h1 = ReducerHashes.Hash(p1.Mesh);
                long rej = 0, any = 0, feat = 0, oth = 0, both = 0;
                var parts = new List<string>(); var hparts = new List<string>();
                foreach (var rs in Reasons) {
                    int i = (int)rs;
                    if (p1.MidRej[i] == 0) continue;
                    rej += p1.MidRej[i]; any += p1.HitAny[i]; feat += p1.HitFeat[i]; oth += p1.HitOther[i]; both += p1.HitBoth[i];
                    parts.Add($"{rs} {p1.MidRej[i]}");
                    hparts.Add($"{rs}: {p1.MidRej[i]} rej, {p1.HitAny[i]} hit ({100.0 * p1.HitAny[i] / p1.MidRej[i]:F1} %), feat {p1.HitFeat[i]}, other {p1.HitOther[i]}, both {p1.HitBoth[i]}");
                }
                rows.Add($"| {label} | {input.TriangleCount} | {p1.MidAttempts} | {p1.MidCollapsed} | {rej} | {string.Join(", ", parts)} |");
                hit.Add($"| {label} | {rej} | {any} | {(rej > 0 ? 100.0 * any / rej : 0):F1} % | {feat} | {oth} | {both} | {string.Join("; ", hparts)} |");
                string er(long[] a) => string.Join(", ", Reasons.Concat(new[] { ReducerProbe.Reason.MeshOp, ReducerProbe.Reason.Other }).Where(x => a[(int)x] > 0).Select(x => $"{x} {a[(int)x]}"));
                endrej.Add($"| {label} | {er(p1.EndRejFeat)} | {er(p1.EndRejOther)} |");

                // (c) the full run with the endpoint fallback
                var (uf, _) = Hausdorff(input, m2, eps); var (ur, _) = Hausdorff(m2, input, eps);
                var (ubf, _) = Hausdorff(input, ml, eps); var (ubr, _) = Hausdorff(ml, input, eps);
                full.Add($"| {label} | {ml.TriangleCount}, {bl:F0} | {m2.TriangleCount}, {b2:F0} | {b2 / bl:F2}x | {ubf:F3} / {ubr:F3} | {uf:F3} / {ur:F3} | {p2.EndRetryCollapsed} | {(hashes2.Count == 1 ? "same" : "DIFFERS")} | {(hl == h0 && hl == h1 ? "probe == lib" : "PROBE DIFFERS")} (probe, no retry: {b0:F0} ms) |");

                // (4) certificate lifetime on the recorded sequence of the baseline run
                var pr = Probe(input, eps, 0, true);
                pr.FastCollapsePass(len, 5);
                var (actual, hind) = pr.Reverse.Analyze();
                long evaluated = pr.Reverse.Certified;
                double saved = actual - hind;
                double share = cm / b0;               // time in ReverseProbe.Check / total pass time
                double boundShare = share * saved / Math.Max(1, evaluated);
                life.Add($"| {label} | {pr.Reverse.Committed} / {pr.Reverse.Refused} | {evaluated} | {actual} | {hind} | {saved} ({100.0 * saved / Math.Max(1, actual):F1} % of events, {100.0 * saved / Math.Max(1, evaluated):F1} % of evaluations) | {cm:F0} of {b0:F0} ms ({100 * share:F0} %) | {100 * boundShare:F1} % |");
                Console.Error.WriteLine($"   done {label}");
            }

        Console.WriteLine("\n(a) midpoint collapse attempts of FastCollapsePass(0.05 diag, 5) with MaxDeviationTwoSided, rejections by reason (an attempt is an edge per round)");
        Console.WriteLine("| case | triangles | attempts | collapsed | rejected (retried) | by reason |\n|---|---|---|---|---|---|");
        rows.ForEach(Console.WriteLine);
        Console.WriteLine("\n(b) midpoint rejected, an endpoint would be accepted (dry run, every check repeated; feat = the end of larger angle defect kept)");
        Console.WriteLine("| case | rejected | hit | hit rate | feature end ok | other end ok | both ok | by reason |\n|---|---|---|---|---|---|---|---|");
        hit.ForEach(Console.WriteLine);
        Console.WriteLine("\n(b2) why the endpoints were rejected (dry run)");
        Console.WriteLine("| case | feature end rejected by | other end rejected by |\n|---|---|---|");
        endrej.ForEach(Console.WriteLine);
        Console.WriteLine("\n(c) full run with endpoint fallback (midpoint, then feature end, then other end) against the two-sided baseline; bounds are h(M,S) / h(S,M) upper bounds in eps");
        Console.WriteLine("| case | baseline: triangles, ms | fallback: triangles, ms | time | baseline bounds | fallback bounds | collapses by fallback | repeated runs | check |\n|---|---|---|---|---|---|---|---|---|");
        full.ForEach(Console.WriteLine);
        Console.WriteLine("\n(4) certificate lifetime (baseline sequence; committed / refused collapses; certificates evaluated incl. refused checks; recertification events of committed collapses: actual, with the longest-lived single-triangle carrier chosen in hindsight; share of the time in the reverse check; upper bound of the saved share of the pass)");
        Console.WriteLine("| case | committed / refused | evaluated | events | hindsight | saved | reverse check time | saved share of total time (bound) |\n|---|---|---|---|---|---|---|---|");
        life.ForEach(Console.WriteLine);
    }
}
