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
0.2), `fidelity=0` skips the fidelity measures. The JSON line of a case has the keys `off` and `on`, or `off` and
`on_<q>` per threshold with `qualities=`. `base=<q>` sets the quality of the off run (default 0), `si=1` turns on
`Remesher.PreventSelfIntersections` in the on runs, `si_from=<k>` only from pass k on (0-based), `pin=<deg>`
`MeshConstraintUtil.PinSharpVertices` with that angle deficit in the on runs. One JSON line per case (off and on), then the summary
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
4 cores, net10.0, Release, `runs=3` (median time; the remeshing is deterministic, two full runs before the fidelity
fields gave the same numbers, and MeshFidelityMetrics samples with a fixed seed), the command above (6 min 41 s), master 121590c plus the fidelity fields of this benchmark. Off = 0, on = 0.2:

| group | cases | q<0.1 off / on | q<1e-3 off / on | cases with q<1e-3 off / on | self-intersecting pairs off / on | cases with self-int. off / on | cases inside-out off / on | cases with rejected passes off / on | mean hausdorff/target off / on | time s off / on | worse: more pairs / more q<1e-3 / hausdorff / inside-out |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| primitives | 150 | 50 / 7 | 38 / 0 | 22 / 0 | 409 / 281 | 35 / 16 | 0 / 0 | 2 / 2 | 0.330 / 0.341 | 4.5 / 6.2 | 11 / 0 / 15 / 0 |
| gears | 18 | 1229 / 438 | 602 / 0 | 10 / 0 | 7697 / 0 | 17 / 0 | 0 / 0 | 0 / 0 | 0.161 / 0.010 | 0.3 / 0.4 | 0 / 0 / 0 / 0 |
| tubes | 24 | 113 / 100 | 1 / 0 | 1 / 0 | 5969 / 5352 | 20 / 24 | 0 / 0 | 8 / 4 | 0.239 / 0.311 | 0.2 / 0.3 | 6 / 0 / 9 / 0 |
| thingi | 20 | 5750 / 1640 | 1536 / 204 | 17 / 11 | 19768 / 3105 | 19 / 17 | 0 / 0 | 1 / 1 | 1.115 / 1.017 | 6.9 / 10.6 | 0 / 0 / 5 / 0 |

Fidelity (means over the cases where both values are finite: Edge Chamfer is infinite and edge recall undefined
when only one or neither of the meshes has sharp edges, 1 Thingi10K case):

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

On the primitives the three thresholds give 337 / 281 / 192 pairs and 18 / 15 / 20 (0.1 / 0.2 / 0.3) cases with a
larger Hausdorff distance (mostly the cone apices, below). The three values are close to each other on every measure,
with trade-offs and no clear winner: 0.3 leaves fewer pairs on the primitives (192 vs 281), a better edge recall
(0.935 vs 0.929) and mean Hausdorff distance (0.900 vs 1.017) on Thingi10K, but 20 instead of 15 primitives with a
larger Hausdorff distance and more flat triangles on Thingi10K (237 vs 204); 0.1 has the most pairs on the primitives
and 2 Thingi10K cases with a worse edge recall. 0.2, the value recommended so far, is kept.

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
  the rim intersect without flipping a normal, and which intersections survive the last pass is chance.
  `PreventSelfIntersections` (below) prevents them, at about 17x the time.
- **Hausdorff distance, cone apex (12 primitives; fixed by `PinSharpVertices`, below):** cone12, cone16, cone24 and cone32 at some targets, by up to 0.81
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

## PreventSelfIntersections (opt-in)
`Remesher.PreventSelfIntersections` rejects the collapses, flips and splits whose new triangles would intersect
another triangle, and reverts the smoothing and projection moves that would. 4 cores, net10.0, Release, `runs=1`:
```
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 si=1 thingi=benchmarks/RemeshBenchmark/data/meshes
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0 quality=0 si=1 thingi=benchmarks/RemeshBenchmark/data/meshes
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 si=1 si_from=15 thingi=benchmarks/RemeshBenchmark/data/meshes
```

| group | config | self-intersecting pairs without / with | cases with pairs | cases not clean | q<1e-3 | mean hausdorff/target | F 0.1 % | time s |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| primitives (150) | q 0.2 | 281 / 0 | 16 / 0 | 16 / 0 | 0 / 0 | 0.341 / 0.376 | 0.8282 / 0.8276 | 6.8 / 119.2 |
| gears (18) | q 0.2 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0.010 / 0.010 | 1.0000 / 1.0000 | 0.4 / 12.8 |
| tubes (24) | q 0.2 | 5352 / 0 | 24 / 0 | 24 / 0 | 0 / 0 | 0.311 / 0.996 | 0.0457 / 0.0192 | 0.3 / 2.9 |
| thingi (20) | q 0.2 | 3105 / 1108 | 17 / 5 | 17 / 5 | 204 / 284 | 1.017 / 0.949 | 0.7205 / 0.7200 | 11.3 / 195.7 |
| primitives (150) | q 0 | 409 / 0 | 35 / 0 | 37 / 4 | 38 / 4 | 0.330 / 0.340 | 0.8221 / 0.8213 | 4.5 / 150.1* |
| gears (18) | q 0 | 7697 / 0 | 17 / 0 | 17 / 9 | 602 / 141 | 0.161 / 0.012 | 0.9824 / 1.0000 | 0.3 / 25.3* |
| tubes (24) | q 0 | 5969 / 0 | 20 / 0 | 20 / 0 | 1 / 4 | 0.239 / 0.933 | 0.2055 / 0.0229 | 0.3 / 3.4* |
| thingi (20) | q 0 | 19768 / 1040 | 19 / 4 | 19 / 12 | 1536 / 2049 | 1.115 / 1.024 | 0.7154 / 0.7135 | 7.6 / 545.6* |
| thingi (20) | q 0.2, check from pass 15 | 3105 / 2411 | 17 / 16 | 17 / 16 | 204 / 204 | 1.017 / 1.003 | 0.7205 / 0.7196 | 10.9 / 19.6 |

\* measured before the parallel first round of the move check (the results are the same, the times about a
quarter lower now: q 0.2 thingi 262.2 -> 193.5 s, primitives 154.7 -> 113.3 s). The q 0.2 rows are from after the
review fix (flips re-checked after reverted moves), which changed a few results (thingi mean Hausdorff 0.960 -> 0.949).

- **Every result whose input is free of self-intersections is free of them** (all procedural cases, 15 of the 20
  Thingi10K meshes). The pairs that remain are in the 5 Thingi10K meshes whose input already intersects itself
  (1053376: input 148 pairs -> 7, 237642: 32 -> 28, 242225: 32 -> 32, 39460: 12 -> 4, 63709: 1298 -> 1037, q 0.2).
  The check does not remove such pairs, and it blocks the operations near them.
- **Fidelity:** unchanged on the primitives, gears and Thingi10K (F and edge recall within 0.002,
  normal consistency on Thingi10K 0.943 -> 0.948; the Thingi10K mean Hausdorff distance improves, 17 primitives get a larger one, mostly cone apices). On Thingi10K
  with q 0.2 the blocked operations leave more flat triangles (204 -> 284; 4 meshes worse). On the tubes (walls far
  below the edge length) the folds are blocked rather than avoided: no pairs, but F 0.1 % 0.046 -> 0.019 and the
  Hausdorff distance 0.31 -> 1.0 target. Those inputs need thickness-aware sizing (`docs/ROADMAP.md`).
- **Cost:** in total 17x on Thingi10K (per mesh median 13x, up to 29x), 17x on the primitives, 31x on the gears. Profiled
  on 1043471 (20k triangles, CAD-like): about 18 exact pair tests per changed triangle, nearly all against neighbours
  that share a vertex; on flat faces these neighbours are coplanar and `Orient3d` has to reach its exact stage to
  return 0. The edge operations (serial) take about 60 % of the check's time. Tried:
  - an exact shortcut for neighbours sharing one vertex (the other two corners of one strictly on one side of the
    other's plane): about 7 %;
  - the first round of the move check in parallel over an AABB tree (each pair once): 44 -> 35 s on 1043471, 262 ->
    194 s on all 20;
  - neighbours in one axis plane decided in 2D without `Orient3d`: another 35 -> 25 s on 1043471, but left out: it
    disagrees with `ExactTriangleIntersection.IntersectsBeyondShared` on coplanar slivers, where the library is wrong
    (its projection axis comes from a rounded cross product; ROADMAP stage R). Possible once that is fixed;
  - checking only the last 5 passes (`si_from=15`): 1.8x the time, but 2411 of 3105 pairs remain (most come from the
    earlier passes and survive them), so not a substitute.
  Open: a clearance filter (a triangle whose nearest non-neighbour is farther than its corners move cannot meet a
  new one; most triangles are far from everything but their neighbours).

## PinSharpVertices (opt-in)
`MeshConstraintUtil.PinSharpVertices(r, 60)` after `PreserveBoundaryAndFeatures`: vertices without a curve constraint
whose angle deficit is at least 60 degrees are pinned (the apex of the cones here: 199 degrees). 4 cores, net10.0,
Release, `runs=1`:
```
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 pin=60 thingi=benchmarks/RemeshBenchmark/data/meshes
```

| group | mean hausdorff/target without / with | pairs | F 0.1 % | normal consistency | Edge Chamfer | time s |
|---|---:|---:|---:|---:|---:|---:|
| primitives (150) | 0.341 / 0.115 | 281 / 168 | 0.8282 / 0.8261 | 0.9895 / 0.9935 | 0.0154 / 0.0197 | 6.7 / 6.3 |
| gears (18) | 0.010 / 0.010 | 0 / 0 | 1.0000 / 1.0000 | 1.0000 / 1.0000 | 0.0000 / 0.0000 | 0.4 / 0.4 |
| tubes (24) | 0.311 / 0.311 | 5352 / 5352 | 0.0457 / 0.0457 | 0.9355 / 0.9355 | 0.0630 / 0.0630 | 0.3 / 0.3 |
| thingi (20) | 1.017 / 0.994 | 3105 / 3104 | 0.7205 / 0.7205 | 0.9430 / 0.9430 | 0.0088 / 0.0088 | 10.9 / 10.6 |

- The cones with 12, 16, 24 and 32 slices end 0.20..0.66 target from the input instead of 1.37..3.15 (all 48 cone
  cases with and without `MinTriangleQuality`, `PinSharpVerticesTests`); no case gets a larger Hausdorff distance.
- Their F 0.1 % drops slightly in 15 cone cases (by up to 0.07, cone12_x0.6) and the Edge Chamfer distance rises: the
  pinned, faceted tip has edges sharper than 30 degrees around it that the measures count as extra feature edges,
  while normal consistency rises (eg cone16_x0.6 0.748 -> 0.908). 3 cones end with a few more pairs (5..10).
- On the 20 Thingi10K meshes it pins 1 vertex (39460) at any threshold of 30..90 degrees and changes no measure
  beyond noise. The gears and tubes have no such vertices.

## Decision
- `g3cli remesh` turns the option on: `min_quality` defaults to 0.2 (`--min-quality 0` for the old behaviour). On the
  inputs the command sees (Thingi10K, CAD-like gears) every fidelity mean is equal or better with it (F-score,
  normal consistency, Edge Chamfer, edge recall, Hausdorff), the zero-area triangles are gone (17 -> 0 results),
  the 1 % worst min angle rises (7.5 -> 9.3 degrees on Thingi10K, 2.4 -> 4.5 on the gears) and 84 % of the
  self-intersecting pairs go away; the price is about 1.5x the time (6.9 -> 10.6 s) and a larger Hausdorff distance on
  5 of 20 meshes (up to +0.30 target, mean 1.115 -> 1.017). The means hide per-mesh losses: F 0.1 % is lower in 9 of
  20 meshes (2 by more than 0.01), normal consistency in 4, edge recall in 6.
- On primitives (coarse cylinders, cones, boxes) the picture is mixed: no flat triangles left (38 -> 0), 31 % fewer
  pairs and 16 instead of 37 results not clean, but the 1 % worst min angle drops (28.3 -> 27.5 degrees, by more than
  a degree in 61 of 150 cases), 11 cases end with more pairs and 15 with a larger Hausdorff distance, up to +0.81
  target at cone apices (cone16_x0.1 2.35 -> 3.15). The losses are local and the cone apices are lost with and
  without the option; the gains on real meshes decide.
- The library default `Remesher.MinTriangleQuality` stays 0: callers of the upstream API get the upstream behaviour
  and time, and the thin walls far below the edge length (`tubes`) get worse with it (F 0.1 % 0.21 -> 0.05, 15 of 24
  cases with a lower edge recall), where `g3cli remesh` already warns about the edge length.
- Open: the cone apices (below) and the self-intersections that remain (`docs/ROADMAP.md`).

Earlier decision (#158): the default stayed 0 until the fidelity metrics were measured; this section replaces it.
