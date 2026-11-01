# RemeshQualityBenchmark

`Remesher.MinTriangleQuality` (opt-in, default 0 = off) on and off, with the settings of `g3cli remesh` (20 passes,
smoothing 0.5, projection onto the input, boundary and 30 degree creases kept by `PreserveBoundaryAndFeatures`,
target = a multiple of the input's mean edge length; on = 0.2). Per case: poor (q < 0.1) and flat (q < 1e-3)
triangles, self-intersecting triangle pairs (`MeshRepairMetrics.SelfIntersections`), inside-out components
(`MeshPostRepairCleanup.FindInvertedComponents`, directly: `NotCleanReason` stops at its first failed check), passes
rolled back by `Remesher.RejectInvertingPasses` (default on, in both runs), Hausdorff distance to the input
(`MeshHausdorffDistance`, within 0.01 target) and time, and the cases that get worse with the option. Fidelity
(`MeshFidelityMetrics` against the input, 20k samples): F-score at 0.1 % and 0.5 % of the size, normal consistency,
Edge Chamfer distance and edge recall at 0.5 % on 30 degree sharp edges, the min angle of the 1 % worst triangles,
triangles with zero area (`TriangleQualityStatistics.Degenerate`) and `MeshRobustRepair.NotCleanReasons`.

## Usage
```
python3 benchmarks/RemeshBenchmark/fetch_data.py       # optional: the 20 Thingi10K meshes of RemeshBenchmark
mkdir -p benchmarks/RemeshQualityBenchmark/results
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=3 qualities=0.1,0.2,0.3 thingi=benchmarks/RemeshBenchmark/data/meshes > benchmarks/RemeshQualityBenchmark/results/all.txt
```
`only=<regex>` selects cases, `quality=<q>` or `qualities=<q1>,<q2>,..` the thresholds compared with 0 (off, default
0.2), `fidelity=0` skips the fidelity measures. One JSON line per case (off and on), then the summary
and the list of cases that get worse. No mesh files are committed.

## Inputs
- `primitives` (150 cases): capped cylinders, cones and tapers (top radius 0.3) with 6, 8, 10, 12, 16, 24, 32
  slices, boxes with 2, 3, 4, 6 vertices per edge; targets 0.1, 0.15, 0.2, 0.3, 0.45, 0.6 x mean edge (refinement:
  the inputs have few, long edges). Rims, box edges and coarse sides are feature curves.
- `gears` (18 cases): extruded gears (12, 24, 40 teeth), a CAD-like part with many feature curves and junctions,
  caps as fans around their centres (long thin triangles), and with 3 or 4 collinear points per side (as CAD exports
  often have); targets 0.5, 1, 2 x mean edge.
- `tubes` (24 cases): closed thin-walled tubes (radius 1, height 3, wall 0.05 or 0.1, 16, 32, 64 slices), targets
  0.5, 0.8, 1.2, 1.6, ie 5 to 32 times the wall thickness: an ill-posed input (see below), the procedural equivalent
  of Thingi10K 135363.
- `thingi` (20 meshes): the closed, manifold Thingi10K selection of `benchmarks/RemeshBenchmark`, target 1 x mean edge.

## Results
4 cores, net10.0, Release, `runs=3` (median time; the results are deterministic, two full runs gave the same numbers),
the command above (6 min 41 s), master 121590c plus the fidelity fields of this benchmark. Off = 0, on = 0.2:

| group | cases | q<0.1 off / on | q<1e-3 off / on | cases with q<1e-3 off / on | self-intersecting pairs off / on | cases with self-int. off / on | cases inside-out off / on | cases with rejected passes off / on | mean hausdorff/target off / on | time s off / on | worse: more pairs / more q<1e-3 / hausdorff / inside-out |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| primitives | 150 | 50 / 7 | 38 / 0 | 22 / 0 | 409 / 281 | 35 / 16 | 0 / 0 | 2 / 2 | 0.330 / 0.341 | 4.5 / 6.2 | 11 / 0 / 15 / 0 |
| gears | 18 | 1229 / 438 | 602 / 0 | 10 / 0 | 7697 / 0 | 17 / 0 | 0 / 0 | 0 / 0 | 0.161 / 0.010 | 0.3 / 0.4 | 0 / 0 / 0 / 0 |
| tubes | 24 | 113 / 100 | 1 / 0 | 1 / 0 | 5969 / 5352 | 20 / 24 | 0 / 0 | 8 / 4 | 0.239 / 0.311 | 0.2 / 0.3 | 6 / 0 / 9 / 0 |
| thingi | 20 | 5750 / 1640 | 1536 / 204 | 17 / 11 | 19768 / 3105 | 19 / 17 | 0 / 0 | 1 / 1 | 1.115 / 1.017 | 6.9 / 10.6 | 0 / 0 / 5 / 0 |

Fidelity (means over the cases; Edge Chamfer and edge recall over the cases whose input has sharp edges):

| group | F 0.1% off / on | F 0.5% off / on | normal consistency off / on | Edge Chamfer off / on | edge recall 0.5% off / on | min angle 1% worst off / on | cases with zero-area tris off / on | cases not clean off / on | worse: F 0.1% by > 0.01 / edge recall by > 0.02 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| primitives | 0.8221 / 0.8282 | 0.9196 / 0.9245 | 0.9895 / 0.9895 | 0.0157 / 0.0154 | 0.8708 / 0.9017 | 28.3 / 27.5 | 22 / 0 | 37 / 16 | 5 / 4 |
| gears | 0.9824 / 1.0000 | 0.9873 / 1.0000 | 0.9946 / 1.0000 | 0.0037 / 0.0000 | 0.9763 / 1.0000 | 2.4 / 4.5 | 9 / 0 | 17 / 0 | 0 / 0 |
| tubes | 0.2055 / 0.0457 | 0.3702 / 0.2332 | 0.9512 / 0.9355 | 0.0458 / 0.0630 | 0.4393 / 0.3296 | 4.3 / 4.0 | 0 / 0 | 20 / 24 | 6 / 15 |
| thingi | 0.7154 / 0.7205 | 0.9018 / 0.9089 | 0.9369 / 0.9430 | 0.0091 / 0.0088 | 0.9094 / 0.9287 | 7.5 / 9.3 | 17 / 0 | 19 / 17 | 2 / 0 |

Other thresholds, Thingi10K (20), off / 0.1 / 0.2 / 0.3:

| measure | off | 0.1 | 0.2 | 0.3 |
|---|---:|---:|---:|---:|
| q < 0.1 | 5750 | 1852 | 1640 | 1708 |
| q < 1e-3 | 1536 | 206 | 204 | 237 |
| self-intersecting pairs | 19768 | 3167 | 3105 | 3156 |
| mean hausdorff/target | 1.115 | 0.915 | 1.017 | 0.900 |
| F 0.1 % | 0.7154 | 0.7241 | 0.7205 | 0.7208 |
| edge recall 0.5 % | 0.9094 | 0.9295 | 0.9287 | 0.9351 |
| min angle 1 % worst | 7.5 | 9.1 | 9.3 | 9.5 |
| cases worse: hausdorff / F 0.1 % / edge recall | | 4 / 1 / 2 | 5 / 2 / 0 | 5 / 2 / 0 |
| time s | 6.9 | 10.7 | 10.6 | 10.5 |

On the primitives the three thresholds give 337 / 281 / 192 pairs and 15 / 15 / 20 (0.1 / 0.2 / 0.3) cases with a
larger Hausdorff distance (mostly the cone apices, below). The thresholds differ little; 0.2 has no case with a worse
edge recall on Thingi10K and fewer Hausdorff regressions on the primitives than 0.3.

The degenerate test of `NotCleanReason` (area below 1e-12 diagonal^2) finds zero-area triangles in 17 of the 20
Thingi10K results without the option and in none with it; 17 results with it stay not clean, by their
self-intersections (`docs/ROADMAP.md`, "Remeshers produce self-intersections").

### Target far above the wall thickness: rolled-back passes
135363 is a 0.8 plate with thin-walled tubes (walls 0.75); the target (its mean edge) is 11.6. The remeshers fold the
inner and outer walls through each other (a tube becomes about four chords) and the signed volume drifts through
zero: before `RejectInvertingPasses`, 4448 (input) -> -4706 without the option (sign change at pass 10), -4021 with
it (pass 8), both results inside out. The `tubes` group reproduces this procedurally: every case ends with dozens to
hundreds of self-intersecting pairs and 2%..700% of the input volume, the sign of the volume changing back and forth
over the passes; which cases ended inside-out was chance (`Remesher`: 3 without the option, none with it;
`RemesherPro`: tube16_w0.05_t1.6 only without, tube32_w0.05_t1.6 only with it). `RejectInvertingPasses` (default on)
rolls such passes back: no result is inside out now, 135363 has 11 / 13 rolled-back passes (off / on), the tubes
8 / 4 cases with rolled-back passes. In 4 tubes without the option the first pass already inverts, so all 20 are
rolled back and the result is the input (0 pairs, Hausdorff 0.01): these account for most of the tubes counted as
worse with the option (more pairs, larger Hausdorff distance), where the passes happen to keep the orientation.
The results of such inputs remain self-intersecting and far off the volume; `g3cli remesh` warns when the edge
length is more than 4 times the estimated wall thickness (`docs/ROADMAP.md`).

The guard also catches cone12_x0.6 and cone16_x0.6: without it, the coarse remesh flattens the cone into a double
layer of zero volume (pass 17 / 16, Hausdorff 2.3 / 2.6 target); with it the last passes are rolled back.

### Cases that get worse
- **More self-intersecting pairs (11 primitives, none of the gears or Thingi meshes):** taper10_x0.15 (12 -> 16),
  taper16_x0.1 (0 -> 12), taper32_x0.1 (0 -> 12), taper32_x0.6 (0 -> 8), cone16_x0.45 (0 -> 10), cone24_x0.15
  (0 -> 6), cone24_x0.3 (0 -> 10), cone24_x0.6 (0 -> 31), cone32_x0.6 (0 -> 8): all at the bottom rim, where the flat
  cap meets the side; cone12_x0.6 (23 -> 70) and cone16_x0.6 (39 -> 52): the flattened cones above, where the
  rolled-back passes end in different states. Counted per phase (taper16_x0.1, taper10_x0.15), the option creates
  far fewer intersections during the passes (edge operations 1248 -> 230 and 395 -> 56, before the review fixes), but projection moves across
  the rim intersect without flipping a normal, and which intersections survive the last pass is chance. Preventing
  them needs an intersection test per operation: `docs/ROADMAP.md`, "Remeshers produce self-intersections".
- **Hausdorff distance, cone apex (12 primitives):** cone12, cone16, cone24 and cone32 at some targets, by up to 0.81
  target edge lengths (cone16_x0.1 2.35 -> 3.15). The apex of a cone with 12 or more slices is no feature (its side
  edges are no creases), so both variants round it off (1.4..2.8 without, 1.4..3.2 with the option); the option's
  guards (rejected smoothing moves, shortened projections) keep a few vertices near the apex up to an edge off the
  surface (3 of 9 cases checked: up to 3 vertices). `docs/ROADMAP.md`, "Feature detection misses vertex features".
- **Hausdorff distance, other primitives (3):** cylinder16_x0.15 0.121 -> 0.162, taper16_x0.6 0.178 -> 0.235,
  taper32_x0.6 0.131 -> 0.221 target.
- **Hausdorff distance, Thingi10K (5 of 20):** 237642 +0.02, 242225 +0.04, 299291 +0.14, 63709 +0.17, 1053376 +0.30
  target edge lengths; the mean improves from 1.115 to 1.017. Not analysed per mesh: the option rejects some smoothing
  moves, collapses and flips, so the vertices end in other places; these meshes also lose most of their flat triangles
  and self-intersections (eg 1053376: 292 -> 66 pairs, 48 -> 9 flat; 63709: 1366 -> 876 pairs, 12 -> 1 flat).

## Decision
- `g3cli remesh` turns the option on: `min_quality` defaults to 0.2 (`--min-quality 0` for the old behaviour). On the
  inputs the command sees (Thingi10K, CAD-like gears) every fidelity mean is equal or better with it (F-score,
  normal consistency, Edge Chamfer, edge recall, Hausdorff), the zero-area triangles are gone (17 -> 0 results),
  the 1 % worst min angle rises (7.5 -> 9.3 degrees on Thingi10K, 2.4 -> 4.5 on the gears) and 84 % of the
  self-intersecting pairs go away; the price is about 1.5x the time (6.9 -> 10.6 s) and a larger Hausdorff distance on
  5 of 20 meshes (up to +0.30 target, mean 1.115 -> 1.017).
- The library default `Remesher.MinTriangleQuality` stays 0: callers of the upstream API get the upstream behaviour
  and time, and the thin walls far below the edge length (`tubes`) get worse with it (F 0.1 % 0.21 -> 0.05, 15 of 24
  cases with a lower edge recall), where `g3cli remesh` already warns about the edge length.
- Open: the cone apices (below) and the self-intersections that remain (`docs/ROADMAP.md`).

Earlier decision (#158): the default stayed 0 until the fidelity metrics were measured; this section replaces it.
