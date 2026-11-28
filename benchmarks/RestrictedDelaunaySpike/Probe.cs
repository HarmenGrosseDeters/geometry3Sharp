// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Stage 1b stop rule (upper bound before the library code): the spike's refinement plus the incremental restricted Voronoi
// cells (RvcIncremental, floating point) and the exact predicates the exact clipping would call, timed on a sample of their
// real arguments. `probe [only=<regex>]`; one JSON line per case.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;

namespace RestrictedDelaunaySpike
{
    static class Probe
    {
        static string F(double x) => x.ToString("G4", CultureInfo.InvariantCulture);

        // ns per call of f over n items, repeated until 0.3 s
        static double Time(int n, Func<int, int> f)
        {
            if (n == 0) return double.NaN;
            long sink = 0;
            for (int k = 0; k < n; ++k) sink += f(k);    // warm-up
            var sw = Stopwatch.StartNew();
            long calls = 0;
            while (sw.Elapsed.TotalSeconds < 0.3) { for (int k = 0; k < n; ++k) sink += f(k); calls += n; }
            double ns = sw.Elapsed.TotalSeconds * 1e9 / calls;
            if (sink == long.MinValue) Console.Error.WriteLine(sink);
            return ns;
        }

        public static void Run(DMesh3 input, string name, double rmaxFrac, Action<DMesh3> rotate, Func<DMesh3, double, List<Vector3d>> initial, bool cut, bool lazy = false)
        {
            rotate(input);
            double diag = input.CachedBounds.Diagonal.Length;
            double rmax = rmaxFrac * diag, hmin = rmax / 16;
            var rdr = new Rdr(input) { RMax = rmax, HMin = hmin };
            RvcIncremental inc = null;
            rdr.Seeded = () => { inc = new RvcIncremental(input, rdr.Dt, rmax) { Cut = cut, Lazy = lazy, Prune = Environment.GetEnvironmentVariable("PRUNE") != null, PruneByTriangle = Environment.GetEnvironmentVariable("PRUNE") == "tri" }; inc.Seed(); };
            rdr.BeforeRegionRound = round => inc.Flush();
            rdr.AfterInsert = (nv, rem, add) => inc.Update(nv, rem, add);
            rdr.Run(initial(input, 4 * rmax));
            double flushInRun = inc.FlushSeconds;
            inc.Flush();
            DMesh3 D = rdr.Output(out _);
            int V = D.VertexCount;
            double spike = rdr.Seconds - rdr.RegionSeconds - inc.SeedSeconds - inc.UpdateSeconds - flushInRun;   // Flush runs inside RegionSeconds (BeforeRegionRound) or after Run
            double incr = inc.SeedSeconds + inc.UpdateSeconds + inc.FlushSeconds;

            // check the incremental cells against a full computation on the final triangulation
            var full = new RvcCheck(input, rdr.Dt); full.Compute();
            int mism = 0, onlyOne = 0;
            foreach (var kv in full.Cells) {
                if (!inc.Cells.TryGetValue(kv.Key, out var c)) { onlyOne++; continue; }
                if (c.Chi != kv.Value.Chi || c.Components != kv.Value.Components || c.Pieces != kv.Value.Pieces) mism++;
            }
            onlyOne += inc.Cells.Keys.Count(k => !full.Cells.ContainsKey(k));

            // exact entries on the sampled arguments
            var s1 = inc.S1; var s2 = inc.S2; var s3 = inc.S3; var d1 = inc.D1; var d2 = inc.D2; var d3 = inc.D3;
            var r = new Vector3d(rmax, 0, 0);
            double tS1 = Time(s1.Count, k => BisectorPredicates.BisectorSide1(s1[k].v, s1[k].a, s1[k].e));
            double tS2 = Time(s2.Count, k => BisectorPredicates.BisectorSide2(s2[k].q0, s2[k].q1, s2[k].a, s2[k].b, s2[k].c));
            double tS3 = Time(s3.Count, k => BisectorPredicates.BisectorSide3(s3[k].a, s3[k].b, s3[k].c, s3[k].t0, s3[k].t1, s3[k].t2, s3[k].e));
            var prep = new BisectorPredicates.BisectorSide3Prepared[s3.Count];
            double tPrep = Time(s3.Count, k => { BisectorPredicates.BisectorSide3Prepare(s3[k].a, s3[k].b, s3[k].c, s3[k].t0, s3[k].t1, s3[k].t2, out prep[k]); return 0; });
            double tS3p = Time(s3.Count, k => BisectorPredicates.BisectorSide3(ref prep[k], s3[k].e));
            double tD1 = Time(d1.Count, k => BisectorPredicates.DistanceAbove1(d1[k].v, d1[k].a, r));
            double tD2 = Time(d2.Count, k => BisectorPredicates.DistanceAbove2(d2[k].q0, d2[k].q1, d2[k].a, d2[k].b, r));
            double tD3 = Time(d3.Count, k => BisectorPredicates.DistanceAbove3(d3[k].a, d3[k].b, d3[k].c, d3[k].t0, d3[k].t1, d3[k].t2, r));
            // the float side test of the clipping on the same points (s1's v as the polygon vertex)
            double tF = Time(s1.Count, k => { Vector3d n = s1[k].e - s1[k].a, m = (s1[k].a + s1[k].e) * 0.5; return (s1[k].v - m).Dot(n) > 0 ? 1 : 0; });
            // ties (0 on the plain predicate: the symbolic cascade decides)
            int z1 = s1.Count(x => BisectorPredicates.BisectorSide1(x.v, x.a, x.e) == 0);
            int z2 = s2.Count(x => BisectorPredicates.BisectorSide2(x.q0, x.q1, x.a, x.b, x.c) == 0);
            int z3 = s3.Count(x => BisectorPredicates.BisectorSide3(x.a, x.b, x.c, x.t0, x.t1, x.t2, x.e) == 0);

            double exactNs = inc.Side1 * tS1 + inc.Side2 * tS2 + inc.Prepare3 * tPrep + inc.Side3 * tS3p + inc.Dist1 * tD1 + inc.Dist2 * tD2 + inc.Dist3 * tD3;
            double floatNs = (inc.Side1 + inc.Side2 + inc.Side3) * tF;
            double ratio = exactNs / floatNs;
            double usSpike = 1e6 * spike / V, usIncr = 1e6 * incr / V, usExact = 1e-3 * exactNs / V;
            Console.WriteLine($"{{\"case\":\"{name}\",\"mode\":\"{(cut ? (lazy ? "cut_lazy" : "cut") : "recompute") + (Environment.GetEnvironmentVariable("PRUNE") is string pr ? "_prune_" + pr : "")}\",\"out_vertices\":{V},\"insertions\":{inc.Insertions},\"spike_us\":{F(usSpike)},\"rvc_float_us\":{F(usIncr)}," +
                $"\"rvc_seed_s\":{F(inc.SeedSeconds)},\"rvc_update_s\":{F(inc.UpdateSeconds)},\"rvc_flush_s\":{F(inc.FlushSeconds)},\"rvc_cut_s\":{F(inc.CutSeconds)},\"rvc_newcell_s\":{F(inc.NewCellSeconds)},\"cut_pieces_per_ins\":{F((double)inc.CutPieces / inc.Insertions)},\"pruned_per_ins\":{F((double)inc.Pruned / inc.Insertions)},\"spike_facet_us\":{F(1e6 * rdr.FacetSeconds / V)},\"recomputes_per_ins\":{F((double)inc.Recomputes / inc.Insertions)},\"cand_tris_per_recompute\":{F((double)inc.CandidateTris / inc.Recomputes)},\"clips_per_ins\":{F((double)inc.Clips / inc.Insertions)}," +
                $"\"side1_per_ins\":{F((double)inc.Side1 / inc.Insertions)},\"side2_per_ins\":{F((double)inc.Side2 / inc.Insertions)},\"side3_per_ins\":{F((double)inc.Side3 / inc.Insertions)},\"prepare3_per_ins\":{F((double)inc.Prepare3 / inc.Insertions)}," +
                $"\"dist1_per_ins\":{F((double)inc.Dist1 / inc.Insertions)},\"dist2_per_ins\":{F((double)inc.Dist2 / inc.Insertions)},\"dist3_per_ins\":{F((double)inc.Dist3 / inc.Insertions)}," +
                $"\"ns\":{{\"side1\":{F(tS1)},\"side2\":{F(tS2)},\"side3\":{F(tS3)},\"prepare3\":{F(tPrep)},\"side3_prepared\":{F(tS3p)},\"dist1\":{F(tD1)},\"dist2\":{F(tD2)},\"dist3\":{F(tD3)},\"float_side\":{F(tF)}}}," +
                $"\"ties\":[{z1},{z2},{z3}],\"samples\":[{s1.Count},{s2.Count},{s3.Count}],\"exact_us\":{F(usExact)},\"exact_over_float\":{F(ratio)}," +
                $"\"bound_count_us\":{F(usSpike + usIncr + usExact)},\"bound_ratio_us\":{F(usSpike + usIncr * ratio)},\"check_mismatch\":{mism},\"check_only_one\":{onlyOne},\"full_cells\":{full.Cells.Count}}}");
        }
    }
}
