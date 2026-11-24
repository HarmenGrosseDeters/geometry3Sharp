// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class TerrainSlopeEnvelopeCanonicalTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeEnvelopeCanonicalTests(ITestOutputHelper output) { this.output = output; }

        // Stage 1 probes of docs/design/SLOPE_ENVELOPE_CANONICAL.md (section 3): the construction (merging during insertion, the
        // insertion order) does not reach the map after Simplify or the surface. Nightly: the run over the stress set is the check
        // (about 10 s, 55 s and 40 s); the PR-tier tests of stage 1 follow on this branch.
        [Trait("Category", "Nightly")]
        [Fact]
        public void StressCases_MergeDuringInsert_SameMapAndSurface()
        {
            int i = 0;
            foreach (var make in TerrainSlopeOneBuildTests.Cases()) {
                var maps = new List<string>[2];
                var ds = new TerrainSlopeDesign[2];
                for (int v = 0; v < 2; ++v) {
                    var l = new List<string>();
                    maps[v] = l;
                    var d = make();
                    d.CompactDuringInsert = v == 1;
                    d.AfterSimplify = m => l.Add(m.PartitionText());
                    d.Compute();
                    ds[v] = d;
                }
                bool sameMap = maps[0].Count == maps[1].Count && maps[0].Zip(maps[1], (a, b) => a == b).All(x => x);
                string diff = ds[0].SlopeSurface == null || ds[1].SlopeSurface == null ? "null" : Invariance.Difference(ds[0].SlopeSurface, ds[1].SlopeSurface);
                output.WriteLine($"case {i}: {ds[0].Status}/{ds[1].Status} maps {maps[0].Count}/{maps[1].Count} same {sameMap}, merged {ds[0].MergedPositions}/{ds[1].MergedPositions}, tris {ds[0].SlopeSurface?.TriangleCount}/{ds[1].SlopeSurface?.TriangleCount}, diff '{diff}'");
                if (!sameMap && maps[0].Count > 0 && maps[1].Count > 0) {
                    var a = maps[0][0].Split('\n'); var b = maps[1][0].Split('\n');
                    output.WriteLine($"  faces {a.Length}/{b.Length}");
                    foreach (var x in a.Except(b).Take(6)) output.WriteLine("  -" + x);
                    foreach (var x in b.Except(a).Take(6)) output.WriteLine("  +" + x);
                }
                Assert.True(sameMap, $"case {i}");
                Assert.Equal(ds[0].Status, ds[1].Status);
                Assert.Equal("", diff == "null" && ds[0].SlopeSurface == null && ds[1].SlopeSurface == null ? "" : diff);
                ++i;
            }
        }

        static string Compare(Func<TerrainSlopeDesign> make, Action<TerrainSlopeDesign> variant, out TerrainSlopeDesign a, out TerrainSlopeDesign b, out string mapDiff)
        {
            var maps = new List<string>[2];
            var ds = new TerrainSlopeDesign[2];
            for (int v = 0; v < 2; ++v) {
                var l = new List<string>();
                maps[v] = l;
                var d = make();
                if (v == 1) variant(d);
                d.AfterSimplify = m => l.Add(m.PartitionText());
                d.Compute();
                ds[v] = d;
            }
            a = ds[0]; b = ds[1];
            mapDiff = "";
            if (maps[0].Count != maps[1].Count) mapDiff = $"builds {maps[0].Count}/{maps[1].Count}";
            else for (int i = 0; i < maps[0].Count; ++i) if (maps[0][i] != maps[1][i]) {
                var x = maps[0][i].Split('\n'); var y = maps[1][i].Split('\n');
                mapDiff = $"build {i}: faces {x.Length}/{y.Length}\n  -" + string.Join("\n  -", x.Except(y).Take(6)) + "\n  +" + string.Join("\n  +", y.Except(x).Take(6));
                break;
            }
            if (a.Status != b.Status) return $"status {a.Status}/{b.Status}";
            if (a.SlopeSurface == null || b.SlopeSurface == null) return a.SlopeSurface == b.SlopeSurface ? "" : "null";
            return Invariance.Difference(a.SlopeSurface, b.SlopeSurface);
        }

        [Trait("Category", "Nightly")]
        [Fact]
        public void RandomRegularPolygons_MergeDuringInsertAndReversed_SameMapAndSurface()
        {
            var rnd = new Random(12345);
            int merging = 0;
            var variants = new (string, Action<TerrainSlopeDesign>)[] {
                ("compact", d => d.CompactDuringInsert = true),
                ("reversed", d => d.InsertionOrder = n => Enumerable.Range(0, n).Reverse().ToArray()),
            };
            for (int it = 0; it < 600; ++it) {
                int n = rnd.Next(3, 25);
                double r = 5 + 30 * rnd.NextDouble(), phase = rnd.NextDouble(), ratio = 0.5 + 2.5 * rnd.NextDouble(), h = r * (0.2 + rnd.NextDouble());
                bool utm = rnd.Next(4) == 0;
                Func<TerrainSlopeDesign> make = () => {
                    var c = utm ? new Vector2d(5.6e6, 3.5e5) : Vector2d.Zero;
                    double z = utm ? 100 : 0;
                    return new TerrainSlopeDesign() {
                        Toes = { SlopeToe.Constant(TerrainSlopeCatchLineTests.Ngon(n, r, z, c, phase), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio)) },
                        HeightLimit = z + h, Tolerance = 0.05
                    };
                };
                var plain = make();
                plain.Compute();
                if (plain.MergedPositions == 0) continue;
                merging++;
                output.WriteLine($"it {it}: n {n} r {r:R} phase {phase:R} ratio {ratio:R} h {h:R} utm {utm}: merged {plain.MergedPositions}, tris {plain.SlopeSurface?.TriangleCount}");
                foreach (var (name, v) in variants) {
                    TerrainSlopeDesign a, b; string md;
                    string diff = Compare(make, v, out a, out b, out md);
                    output.WriteLine($"  {name}: merged {a.MergedPositions}/{b.MergedPositions} tris {a.SlopeSurface?.TriangleCount}/{b.SlopeSurface?.TriangleCount} diff '{diff}' map '{md}'");
                    Assert.Equal("", diff);
                    Assert.Equal("", md);
                }
            }
            output.WriteLine($"{merging} with merges");
        }

        [Trait("Category", "Nightly")]
        [Fact]
        public void MergingAndBenchmarkDesigns_MergeDuringInsertAndReversed_SameMapAndSurface()
        {
            var makes = new (string, Func<TerrainSlopeDesign>)[] {
                ("wavyOnWaves40", () => TerrainSlopeBandRepairTests.WavyOnWaves(Similarity.Identity, new Vector3d(0, 0, -340), n: 40, r: 100)),
                ("hambach130", () => TerrainSlopeDesignTests.WavyHambach(130)),
                ("ngon16", () => TerrainSlopeDesignTests.Design(27.33760196358785 * 0.6, 0.05, SlopeToe.Constant(TerrainSlopeDesignTests.Ngon(16, 27.33760196358785, 0, 0.4471245046924448), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(2.280296943513815)))),
                ("ngon22", () => TerrainSlopeDesignTests.Design(12.984759201288576 * 0.6, 0.05, SlopeToe.Constant(TerrainSlopeDesignTests.Ngon(22, 12.984759201288576, 0, 0.5293125866583142), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(3.0107992275202644)))),
            };
            var variants = new (string, Action<TerrainSlopeDesign>)[] {
                ("compact", d => d.CompactDuringInsert = true),
                ("reversed", d => d.InsertionOrder = n => Enumerable.Range(0, n).Reverse().ToArray()),
            };
            foreach (var (cn, make) in makes)
                foreach (var (name, v) in variants) {
                    TerrainSlopeDesign a, b; string md;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string diff = Compare(make, v, out a, out b, out md);
                    output.WriteLine($"{cn} {name}: {sw.ElapsedMilliseconds} ms, merged {a.MergedPositions}/{b.MergedPositions} band {a.BandMerges}/{b.BandMerges} tris {a.SlopeSurface?.TriangleCount}/{b.SlopeSurface?.TriangleCount} diff '{diff}' map '{md}'");
                    output.WriteLine("   a: " + string.Join(", ", a.PhaseSeconds.Select(kv => $"{kv.Key} {kv.Value:F2}")));
                    output.WriteLine("   b: " + string.Join(", ", b.PhaseSeconds.Select(kv => $"{kv.Key} {kv.Value:F2}")));
                    Assert.Equal("", diff);
                    Assert.Equal("", md);
                    Assert.Equal(a.MergedPositions, b.MergedPositions);
                    if (cn == "wavyOnWaves40") Assert.True(a.MergedPositions > 0);
                }
        }
    }
}
