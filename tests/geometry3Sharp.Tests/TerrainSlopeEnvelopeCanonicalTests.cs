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
        // the earlier construction: no merge during insertion, every part of a face takes every plane of the face
        static void Earlier(TerrainSlopeDesign d) { d.CompactDuringInsert = false; d.PartSectors = false; }

        // the output depends on the labelled envelope only (docs/design/SLOPE_ENVELOPE_CANONICAL.md): the default construction
        // (merge during insertion, planes per part by sector) against the earlier one, reversed, and a seeded order. Checked: the map
        // after every Simplify (labels and rounded corners per face, in OrderedFaces), status, builds, lowering, merges, and the
        // surface bit for bit with its triangle groups. Cheap cases that carry rays, an open toe, a fill and a ramp (InsertPolygonPiece)
        internal static IEnumerable<(string, Func<TerrainSlopeDesign>)> PrCases()
        {
            var all = TerrainSlopeOneBuildTests.Cases().ToList();
            yield return ("ngon10", all[0]);
            yield return ("ngon16", all[1]);
            yield return ("open", all[5]);
            yield return ("fill", all[6]);
            yield return ("ramp", () => TerrainSlopeRampTests.Straight());
        }

        static void AssertSame(TerrainSlopeDesign a, TerrainSlopeDesign b, List<string> ma, List<string> mb, string what)
        {
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.Attempts, b.Attempts);
            Assert.Equal(a.UsedToeLowering, b.UsedToeLowering);
            Assert.Equal(a.MergedPositions, b.MergedPositions);
            Assert.Equal(a.RayCount, b.RayCount);
            Assert.Equal(ma.Count, mb.Count);
            for (int i = 0; i < ma.Count; ++i) Assert.True(ma[i] == mb[i], $"{what}: the map of build {i} differs");
            Assert.Equal(a.SlopeSurface == null, b.SlopeSurface == null);
            if (a.SlopeSurface != null) {
                Assert.Equal("", Invariance.Difference(a.SlopeSurface, b.SlopeSurface));
                foreach (int t in a.SlopeSurface.TriangleIndices()) Assert.Equal(a.SlopeSurface.GetTriangleGroup(t), b.SlopeSurface.GetTriangleGroup(t));
            }
        }

        static TerrainSlopeDesign Run(Func<TerrainSlopeDesign> make, Action<TerrainSlopeDesign> variant, List<string> maps)
        {
            var d = make();
            if (variant != null) variant(d);
            d.AfterSimplify = m => maps.Add(m.PartitionText());
            d.Compute();
            return d;
        }

        [Fact]
        public void PureFunction_ConstructionAndOrder_SameMapAndSurface()
        {
            var variants = new (string, Action<TerrainSlopeDesign>)[] {
                ("earlier", Earlier),
                ("earlier reversed", d => { Earlier(d); d.InsertionOrder = n => Enumerable.Range(0, n).Reverse().ToArray(); }),
                ("seeded", d => d.InsertionOrder = n => TerrainSlopeDesign.shuffled(n, 7)),
                ("reversed", d => d.InsertionOrder = n => Enumerable.Range(0, n).Reverse().ToArray()),
            };
            int rays = 0;
            foreach (var (name, make) in PrCases()) {
                var ma = new List<string>();
                var a = Run(make, null, ma);
                Assert.True(a.SlopeSurface != null, name);
                Assert.NotEmpty(ma);
                if (a.RayCount > 0) rays++;
                foreach (var (vn, v) in variants) {
                    var mb = new List<string>();
                    AssertSame(a, Run(make, v, mb), ma, mb, name + " " + vn);
                }
            }
            Assert.True(rays >= 1, $"{rays}");
        }

        // R1 directly: the default construction gives the reference labels, those of the insertion without prefilters and with every
        // fan plane (SlopeMap.NoPrefilters): the same map after Simplify and the same surface
        [Fact]
        public void Default_GivesTheReferenceLabels()
        {
            foreach (var (name, make) in PrCases()) {
                var ma = new List<string>();
                var a = Run(make, null, ma);
                var mb = new List<string>();
                AssertSame(a, Run(make, d => d.NoPrefilters = true, mb), ma, mb, name + " reference");
            }
        }

        // #1530: the pit with a ramp in the frame rotated about z by 31 degrees. A face on the design side of a toe vertex's fan (the
        // gap between its end planes, 180 degrees wide) took no fan plane: the window around the face's directions (2 theta wide
        // margins) holds none, though the nearest end plane is the fan's maximum there. The labels were not the envelope (master: 218
        // surface vertices, the merge during insertion 216, the reference 217), and both constructions disagreed. Now both give the
        // reference labels: the same map after Simplify and the same surface
        [Fact]
        public void RampInARotatedFrame_BothConstructionsGiveTheReferenceLabels()
        {
            Similarity s0 = Similarity.All().First(x => x.Name == "rotated");
            Similarity s = new Similarity(s0.Name + " (about z)", s0.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s0.Translation);
            Func<TerrainSlopeDesign> make = () => InFrame("ramp", s);
            var mr = new List<string>();
            var reference = Run(make, d => d.NoPrefilters = true, mr);
            Assert.True(reference.SlopeSurface != null, reference.Message);
            foreach (var (vn, v) in new (string, Action<TerrainSlopeDesign>)[] { ("default", null), ("earlier", Earlier) }) {
                var m = new List<string>();
                AssertSame(reference, Run(make, v, m), mr, m, vn);
            }
        }

        // compute twice on one instance (the map's merge list and sector context are per build)
        [Fact]
        public void Reuse_SameMapAndSurface()
        {
            foreach (var (name, make) in PrCases().Take(2)) {
                var d = make();
                var maps = new List<string>();
                d.AfterSimplify = m => maps.Add(m.PartitionText());
                Assert.True(d.Compute(), d.Message);
                var first = d.SlopeSurface;
                int n = maps.Count;
                Assert.True(d.Compute(), d.Message);
                Assert.Equal("", Invariance.Difference(first, d.SlopeSurface));
                Assert.Equal(maps.Take(n), maps.Skip(n));
            }
        }

        // a case in the frame s (rotated about z, as in TerrainSlopePrefilterRegressionTests and the ramp tests): the toes, the ramps,
        // the height limit and the tolerance mapped; the ramp in a local frame, as in TerrainSlopeRampTests
        static TerrainSlopeDesign InFrame(string kind, Similarity s)
        {
            TerrainSlopeDesign d;
            if (kind == "ngon22")
                d = TerrainSlopeDesignTests.Design(12.984759201288576 * 0.6, 0.05, SlopeToe.Constant(TerrainSlopeDesignTests.Ngon(22, 12.984759201288576, 0, 0.5293125866583142), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(3.0107992275202644)));
            else if (kind == "open")
                d = TerrainSlopeDesignTests.Design(5, 0.02, SlopeToe.Constant(new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(10, 0, 1), new Vector3d(15, 8, 0) }, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1.5)));
            else {
                d = TerrainSlopeRampTests.Straight();
                d.LocalFrame = true;
            }
            foreach (var t in d.Toes) t.Points = t.Points.Select(p => s.Apply(p)).ToList();
            foreach (var r in d.Ramps) {
                r.Path = r.Path.Select(p => s.Apply(new Vector3d(p.x, p.y, 0)).xy).ToList();
                r.Width = s.ApplyLength(r.Width);
            }
            d.HeightLimit = s.Apply(new Vector3d(0, 0, d.HeightLimit)).z;
            d.Tolerance = s.ApplyLength(d.Tolerance);
            return d;
        }

        // in every frame of Similarity.All() (rotated about z): the exact frames map back to the identity's surface bit for bit, and
        // in the others the default construction gives the earlier one's bit for bit. A closed toe, an open toe with lateral limits,
        // and a ramp (in the rotated frame the fan window left out the nearest plane beyond a gap of the toe's fan, #1530)
        [Theory]
        [InlineData("ngon22")]
        [InlineData("open")]
        [InlineData("ramp")]
        public void Invariance_EveryFrame_SameAsTheEarlierConstruction(string kind)
        {
            var origin = InFrame(kind, Similarity.Identity);
            Assert.True(origin.Compute(), origin.Message);
            int exact = 0, other = 0;
            foreach (Similarity s0 in Similarity.All()) {
                Similarity s = s0.IsRotated ? new Similarity(s0.Name + " (about z)", s0.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s0.Translation) : s0;
                var d = InFrame(kind, s);
                Assert.True(d.Compute(), s.Name + " " + d.Message);
                if (s.IsExact) {
                    // (the identity is compared with the earlier construction in PureFunction_ConstructionAndOrder_SameMapAndSurface)
                    Assert.Equal("", Invariance.Difference(origin.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
                    exact++;
                } else {
                    var e = InFrame(kind, s);
                    Earlier(e);
                    Assert.True(e.Compute(), s.Name + " " + e.Message);
                    Assert.Equal("", Invariance.Difference(e.SlopeSurface, d.SlopeSurface));
                    other++;
                }
            }
            Assert.True(exact >= 2 && other >= 1, $"{exact} {other}");
        }

        // every piece inserted again into the final map changes no label (the envelope is idempotent; an incremental construction
        // that leaves a wrong label shows here); and the work it takes is small (the upper bound of SLOPE_ENVELOPE_CANONICAL.md 4)
        [Fact]
        public void Reinsert_IntoTheFinalMap_ChangesNothing()
        {
            foreach (var (name, make) in PrCases().Take(3)) {
                var d = make();
                var maps = new List<string>();
                d.AfterSimplify = m => maps.Add(m.PartitionText());
                Assert.True(d.Compute(), d.Message);
                var e = make();
                e.ReinsertAfterSimplify = true;
                var mapsE = new List<string>();
                SlopeMap last = null;
                e.AfterSimplify = m => { mapsE.Add(m.PartitionText()); last = m; };
                Assert.True(e.Compute(), e.Message);
                Assert.Equal("", Invariance.Difference(d.SlopeSurface, e.SlopeSurface));
                // (the map of the last build after its second Simplify: the same partition as without the re-insertion)
                Assert.Equal(maps.Count, mapsE.Count);
                Assert.Equal(maps.Take(maps.Count - 1), mapsE.Take(mapsE.Count - 1));
                Assert.NotNull(last);
                Assert.Equal(maps[maps.Count - 1], last.PartitionText());
                Assert.True(e.ReinsertSplits < e.InsertSplits, $"{name}: {e.ReinsertSplits} {e.InsertSplits}");
                output.WriteLine($"{name}: insert {e.InsertSplits} splits, again {e.ReinsertSplits}");
            }
        }

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
                    if (v == 1) { d.CompactDuringInsert = false; d.PartSectors = false; }
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

        // Nightly, the run is the check: a sweep over 600 random regular polygons (about 55 s), for a design that merges positions
        [Trait("Category", "Nightly")]
        [Fact]
        public void RandomRegularPolygons_MergeDuringInsertAndReversed_SameMapAndSurface()
        {
            var rnd = new Random(12345);
            int merging = 0;
            var variants = new (string, Action<TerrainSlopeDesign>)[] {
                ("earlier", d => { d.CompactDuringInsert = false; d.PartSectors = false; }),
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

        // Nightly, the run is the check: the designs that carry the near-degenerate merge (WavyOnWaves n 40, 7 merged positions, 8 s
        // per build) and the benchmark design (4 s), each under three constructions (about 40 s); no smaller design merging positions is known
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
                ("earlier", d => { d.CompactDuringInsert = false; d.PartSectors = false; }),
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

        // a barrier (toe segment or lateral limit) has the domain on one side only, at every stage (after the domain, after each
        // insertion with its merges, after Simplify): the merge during insertion cannot join faces across a barrier. (The mutant of
        // Compact without its barrier checks is therefore equivalent; this checks the premise, on toes with lateral limits)
        [Fact]
        public void Barriers_HaveTheDomainOnOneSideOnly()
        {
            var cases = PrCases().ToList();
            foreach (var side in new[] { SlopeToe.Sides.Left, SlopeToe.Sides.Right }) {
                var bent = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(10, 0, 1), new Vector3d(15, 8, 0) };
                cases.Add(($"bent {side}", () => TerrainSlopeDesignTests.Design(5, 0.02, SlopeToe.Constant(bent, false, side, SlopeSpec.Ratio(1.5)))));
            }
            int checkedMaps = 0;
            foreach (var (name, make) in cases) {
                var d = make();
                d.AfterSimplify = m => { Assert.Equal(0, m.BarriersWithDomainOnBothSides()); checkedMaps++; };
                d.AfterInsert = m => Assert.Equal(0, m.BarriersWithDomainOnBothSides());
                Assert.True(d.Compute(), name + " " + d.Message);
            }
            Assert.True(checkedMaps >= cases.Count, $"{checkedMaps}");
        }
    }
}
