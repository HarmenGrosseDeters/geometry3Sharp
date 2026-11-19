// Probe driver of the task of 2026-11-19 (docs/perf/BOOLEAN_CHAINS_PROBE_2026-11.md): per step of a chain, the boolean's phase times with
// the counters of perf/chain_probe.patch (core/ChainProbe.cs, which must be applied to the library; without it the "probe.*" entries
// are absent and the skip configurations change nothing), and the experiments that skip work, each checked by the result hash.
//   chain-probe <A.off> <B.off> <C.off> <ucd|dui|iud> <reps>        the abc-chained plans, today's chain (each step on the previous rounded result)
//   chain-probe-proc <mixed|generic> <seed> <steps> <reps>          procedural chains
//   chain-probe-expr[-proc] ...                                      the same operands as an ExactMesh expression: step k is
//                                                                    Lazy(plan, k).ToBoolean(), one n-ary arrangement over X0..Xk
// One JSON line per step. Configurations (each repeated reps times, order randomized per repetition, a full GC before each):
//   off      probe off (the reference time)
//   p1       G3_CP_MODE=1: counters, self-join time per operand
//   p2s      G3_CP_MODE=2 and G3_PROBE_SERIAL=1: per-class CPU times (timestamps per pair, so slower than off)
//   skip     p1 and the self-join of the certified operands skipped (chain: the left operand of steps >= 2 when the previous result is clean;
//            expression: every source, the bound if all were certified); hash must equal off's where the operands are clean
//   fast     p1 and G3_CP_FASTTRIVIAL=1: trivial coplanar pairs decided by exact orientation tests before the clips (hash must equal off's)
//   fastchk  p1 and G3_CP_FASTCHECK=1: the decision is compared with the clips' outcome (probe.fast_mismatch must be 0)
//   fasts, p1s  fast and p1, serial (CPU time)
//   triv     p1 and G3_CP_SKIPTRIVIAL=1 (the coplanar pairs that add only corners and boundary edges add nothing); hash must equal off's
// Benchmark code only; nothing here is part of the library.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using g3;
using gs;

static class ChainProbeRun
{
    sealed class Config
    {
        public string Name; public int Mode; public bool Serial; public bool SkipSelf, SkipTrivial, Fast, FastCheck;
        public List<double> Ms = new List<double>();
        public Dictionary<string, long> Phases; public double PhasesMs = -1;
        public string Hash = "-"; public bool Ok = true; public string Error;
        public List<Dictionary<string, long>> AllPhases = new List<Dictionary<string, long>>();
    }

    static void Env(string k, string v) { Environment.SetEnvironmentVariable(k, v); }

    public static int Chain(List<DMesh3> ops, List<ChainExact.Step> plan, Dictionary<string, object> info, int reps, bool expr)
    {
        ChainExact.Warm();
        ExactMesh[] leaf = expr ? ChainExact.Leaves(ops) : null;
        DMesh3 prev = null;
        bool prevCert = false;
        var rng = new Random(2026119);
        for (int k = 0; k < plan.Count; ++k) {
            var step = plan[k];
            Func<ExactMeshBoolean> make;
            int meshesInArrangement;
            bool leftCert = false;
            if (expr) {
                ExactMesh e = ChainExact.Lazy(plan, k, leaf);
                make = () => e.ToBoolean();
                meshesInArrangement = e.Sources.Count;
            } else {
                DMesh3 left = step.Left < 0 ? prev : ops[step.Left], right = ops[step.Right];
                leftCert = step.Left < 0 && prevCert;
                make = () => new ExactMeshBoolean(left, right, step.Op);
                meshesInArrangement = 2;
            }
            var cfgs = new List<Config> {
                new Config { Name = "off", Mode = 0 },
                new Config { Name = "p1", Mode = 1 },
                new Config { Name = "p2s", Mode = 2, Serial = true },
                new Config { Name = "skip", Mode = 1, SkipSelf = true },
                new Config { Name = "triv", Mode = 1, SkipTrivial = true },
                new Config { Name = "fast", Mode = 1, Fast = true },
                new Config { Name = "fastchk", Mode = 1, FastCheck = true },
                new Config { Name = "fasts", Mode = 1, Fast = true, Serial = true },
                new Config { Name = "p1s", Mode = 1, Serial = true },
            };
            // (the skip configurations run only where there is something certified to skip: the chain's left operand of steps >= 2)
            if (!expr && !leftCert)
                cfgs.RemoveAll(c => c.SkipSelf);
            long skipMask = 0;
            if (expr) skipMask = (1L << meshesInArrangement) - 1;
            else if (leftCert) skipMask = 1;
            DMesh3 chainResult = null;
            bool chainRoundingClean = false;
            string chainReason = null;
            for (int rep = 0; rep < reps; ++rep) {
                var order = cfgs.OrderBy(_ => rng.Next()).ToList();
                foreach (Config c in order) {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    Env("G3_CP_MODE", c.Mode.ToString());
                    Env("G3_PROBE_SERIAL", c.Serial ? "1" : "0");
                    Env("G3_CP_SKIPSELF", c.SkipSelf ? skipMask.ToString() : "0");
                    Env("G3_CP_SKIPTRIVIAL", c.SkipTrivial ? "1" : "0");
                    Env("G3_CP_FASTTRIVIAL", c.Fast ? "1" : "0");
                    Env("G3_CP_FASTCHECK", c.FastCheck ? "1" : "0");
                    ExactMeshBoolean b = make();
                    var sw = Stopwatch.StartNew();
                    bool ok;
                    try { ok = b.Compute(); c.Error = b.Error; } catch (Exception ex) { ok = false; c.Error = ex.GetType().Name + ": " + ex.Message; }
                    double ms = sw.Elapsed.TotalMilliseconds;
                    c.Ok &= ok;
                    c.Ms.Add(ms);
                    c.AllPhases.Add(new Dictionary<string, long>(b.PhaseMilliseconds));
                    if (ok) {
                        string h = Profile.Hash(b.Result);
                        if (c.Hash != "-" && c.Hash != h) c.Hash += "|" + h; else c.Hash = h;
                        if (c.Name == "off" && rep == 0) {
                            chainResult = b.Result;
                            chainRoundingClean = b.RoundingClean;
                        }
                    }
                }
            }
            Env("G3_CP_MODE", "0"); Env("G3_PROBE_SERIAL", "0"); Env("G3_CP_SKIPSELF", "0"); Env("G3_CP_SKIPTRIVIAL", "0"); Env("G3_CP_FASTTRIVIAL", "0"); Env("G3_CP_FASTCHECK", "0");
            gParallel.SerialOnThisThread = false;
            if (chainResult != null)
                chainReason = MeshRobustRepair.NotCleanReason(chainResult, true);
            var row = new Dictionary<string, object>(info) { ["step"] = k + 1, ["op"] = ChainExact.Name(step.Op), ["reps"] = reps, ["expr"] = expr,
                ["left_certified"] = leftCert, ["arrangement_meshes"] = meshesInArrangement,
                ["result_rounding_clean"] = chainRoundingClean, ["result_reason_full"] = chainReason, ["result_tris"] = chainResult?.TriangleCount ?? -1 };
            string offHash = cfgs[0].Hash;
            foreach (Config c in cfgs) {
                var sorted = c.Ms.OrderBy(x => x).ToList();
                int mid = sorted.Count / 2;
                double med = sorted.Count == 0 ? -1 : sorted[mid];
                // phases of the repetition with the median time
                int idx = c.Ms.IndexOf(med);
                var ph = idx >= 0 ? c.AllPhases[idx] : new Dictionary<string, long>();
                row[c.Name] = new Dictionary<string, object> {
                    ["ok"] = c.Ok, ["ms"] = c.Ms, ["median_ms"] = med, ["hash"] = c.Hash, ["same_hash_as_off"] = c.Hash == offHash, ["phases"] = ph, ["error"] = c.Error };
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
            if (!expr) {
                if (chainResult == null || chainResult.TriangleCount == 0)
                    break;
                prev = chainResult;
                prevCert = chainRoundingClean && chainReason == "";
            }
        }
        return 0;
    }
}
