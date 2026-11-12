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
`MeshConstraintUtil.PinSharpVertices` with that angle deficit in the on runs, `sizing=<f>`
`Remesher.TargetEdgeLengthF = RemeshSizing.FromWallThickness(input, f)` in the on runs. The JSON has the volume of the
result over the input's (`VolumeRatio`). One JSON line per case (off and on), then the summary
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
self-intersections (`docs/roadmap/alpha-wrap-and-repair.md`, "Remeshers produce self-intersections").

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
length is more than 4 times the estimated wall thickness (`docs/roadmap/alpha-wrap-and-repair.md`).

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
  surface (3 of 9 cases checked: up to 3 vertices). `docs/roadmap/alpha-wrap-and-repair.md`, "Feature detection misses vertex features".
- **Hausdorff distance, other primitives (3):** cylinder16_x0.15 0.121 -> 0.162, taper16_x0.6 0.178 -> 0.235,
  taper32_x0.6 0.131 -> 0.221 target.
- **Hausdorff distance, Thingi10K (5 of 20):** 237642 +0.02, 242225 +0.04, 299291 +0.14, 63709 +0.17, 1053376 +0.30
  target edge lengths; the mean improves from 1.115 to 1.017. Not analysed per mesh: the option rejects some smoothing
  moves, collapses and flips, so the vertices end in other places; these meshes also lose most of their flat triangles
  and self-intersections (eg 1053376: 292 -> 66 pairs, 48 -> 9 flat; 63709: 1366 -> 876 pairs, 12 -> 1 flat).

### Rim self-intersections: diagnosis and a fold check that was not adopted (#621)
Per-operation trace on master (every flip, collapse, split, smoothing and projection pass; pairs identified by their
triangles' vertex ids), the nine cases above plus cone12_x0.15:
- **One configuration.** Every pair left at the end involves a *fold*: a side triangle lying in the cap's plane,
  facing up, over cap triangles facing down (a coplanar overlap; no normal flips, so the normal checks pass it). In
  8 cases its corners are all feature vertices (rim curve or fixed corners), created by a valence flip whose new edge
  joins two rim vertices across a third, sometimes by a collapse of a side vertex onto a rim vertex; in the coarse
  x0.6 cases two corners are on the rim and the third was projected onto the cap. Valence flips of quads with only
  feature vertices are about 10% of the flips but create about 80% of the pairs that flips create (cone24_x0.3: 41
  such flips create 142 pairs, the other 366 flips 34).
- **The final count is chance.** During the passes thousands of pairs appear and disappear in both modes; the
  survivors are created in pass 19. The same inputs moved by 8 small translations (k * (0.37, -0.61, 0.23)), final
  pairs summed, quality 0 -> 0.2: taper16_x0.1 0 -> 66, taper32_x0.1 0 -> 46, cone24_x0.6 0 -> 248, cone32_x0.6
  0 -> 64, taper32_x0.6 0 -> 32, cone16_x0.45 0 -> 54, cone24_x0.3 20 -> 44, cone24_x0.15 16 -> 26, taper10_x0.15
  108 -> 128; better with the option: cone12_x0.15 137 -> 16, taper10_x0.3 160 -> 48, taper12_x0.15 27 -> 12. A
  gate on the single axis-aligned count overfits; the ensemble is the measure.
- **No large-coordinate bug.** cone24_x0.3 under the UTM translation ends 0.51 edges from the untranslated result,
  but a translation by (0.5, 0.25, 0.125) gives 0.25..0.53 and quality 0 gives 0.28 (master): rounding tips ties of
  the symmetric input and the passes amplify them. A vertex-distance bound of 0.25 edges holds for cylinder12 (the
  existing similarity test), not for these cones.

The fold check (`docs/research/REMESH_RIM_FOLD_CHECK.diff`, against master c013f31, with its tests): with
MinTriangleQuality, a flip or collapse is rejected if a new triangle with two or more feature vertices intersects
(exactly, `pair_intersects`) more triangles around its feature corners, facing more than 120 degrees away, than the
old triangles did; flips whose new edge is longer than 4 x the split length (the coarse input) are exempt. Results:
the ensemble above ends with 0 pairs in all cases except cone12_x0.15 (6); primitives 281 -> 134 pairs, cases with
pairs 16 -> 2 (the flattened cones cone12_x0.6, cone16_x0.6); mean Hausdorff/target 0.341 -> 0.344; time about
1.09x (paired, alternating runs of an earlier variant). It is not adopted:
- with `PinSharpVertices` (30 and 75 degrees; the 12 rim vertices of cone12 are pinned too) cone12_x0.3 ends with
  264 pairs (8 translations: 2023, master 0) and 2.5 target from the input, cone12_x0.2 with 81 (master 0): the 2..5
  fold flips rejected on the coarse input in passes 0..1 change the evolution, and from pass 2 projection carries
  long side triangles across the cap (transversal crossings, not folds; checking projection moves the same way
  reduces the count only to about 165);
- existing tests fail: `RemesherInversionGuardTests` (Tube135363 and the cavity end inside out with the option,
  RemesherPro parallel/serial), `PinSharpVerticesTests.Remeshed_ApexKept`, and RemesherPro on cone16_x0.45 under a
  translation (4 pairs);
- without the coarse-input exemption the pinned cases are worse (cone12_x0.1 893 pairs), with an exemption at 1 x
  the split length the rim folds come back (10 primitives with more pairs). Variants that also checked smoothing and
  projection moves changed nothing in the default setting.

`PreventSelfIntersections` remains the guarantee. A remaining idea, not tried: refine the coarse input to the target
before the guarded passes, so that the fold flips of the first passes do not arise.

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

The times above are from before #261 (`TriangleTriangleFilter`). In an alternating A/B on 2 November 2026 the q 0.2 rows
with the check took 35.9-37.2 / 2.5-2.6 / 2.0-2.1 / 101.7-101.8 s on master b7fbc52 (primitives / gears / tubes /
thingi) and 34.1-35.2 / 2.1-2.2 / 2.0-2.1 / 99.5-101.6 s with the filter's axis-plane case (0.95 / 0.85 / 1.00 / 0.99),
the results identical (`benchmarks/SelfIntersectionBenchmark`, README, 2 November
2026).

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
  Hausdorff distance 0.31 -> 1.0 target. Those inputs need thickness-aware sizing (`docs/roadmap/alpha-wrap-and-repair.md`).
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
    (its projection axis comes from a rounded cross product; `docs/roadmap/exact-boolean.md` stage R). Possible once that is fixed;
  - checking only the last 5 passes (`si_from=15`): 1.8x the time, but 2411 of 3105 pairs remain (most come from the
    earlier passes and survive them), so not a substitute.
  Open: a clearance filter (a triangle whose nearest non-neighbour is farther than its corners move cannot meet a
  new one; most triangles are far from everything but their neighbours).

## PinSharpVertices (opt-in)
`MeshConstraintUtil.PinSharpVertices(r)` after `PreserveBoundaryAndFeatures`: interior vertices without a curve
constraint whose absolute angle deficit is at least 75 degrees (the default) are pinned (the apex of the cones here:
199 degrees). 4 cores, net10.0, Release, `runs=1`:
```
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 pin=75 thingi=benchmarks/RemeshBenchmark/data/meshes
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0 quality=0 pin=75 thingi=benchmarks/RemeshBenchmark/data/meshes
```

| group | config | mean hausdorff/target without / with | pairs | cases not clean | F 0.1 % | normal consistency | Edge Chamfer | time s |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| primitives (150) | q 0.2 | 0.341 / 0.115 | 281 / 168 | 16 / 16 | 0.8282 / 0.8261 | 0.9895 / 0.9935 | 0.0154 / 0.0197 | 6.9 / 7.1 |
| gears (18) | q 0.2 | 0.010 / 0.010 | 0 / 0 | 0 / 0 | 1.0000 / 1.0000 | 1.0000 / 1.0000 | 0.0000 / 0.0000 | 0.4 / 0.4 |
| tubes (24) | q 0.2 | 0.311 / 0.311 | 5352 / 5352 | 24 / 24 | 0.0457 / 0.0457 | 0.9355 / 0.9355 | 0.0630 / 0.0630 | 0.3 / 0.3 |
| thingi (20) | q 0.2 | 1.017 / 0.994 | 3105 / 3104 | 17 / 17 | 0.7205 / 0.7205 | 0.9430 / 0.9430 | 0.0088 / 0.0088 | 12.5 / 12.0 |
| primitives (150) | q 0 | 0.330 / 0.115 | 409 / 387 | 37 / 43 | 0.8221 / 0.8205 | 0.9895 / 0.9928 | 0.0157 / 0.0206 | 5.3 / 5.0 |
| thingi (20) | q 0 | 1.115 / 1.086 | 19768 / 19796 | 19 / 19 | 0.7154 / 0.7156 | 0.9369 / 0.9370 | 0.0091 / 0.0092 | 8.0 / 7.8 |

- The cones with 12, 16, 24 and 32 slices end 0.20..0.66 target from the input instead of 1.37..3.15 (all 48 cone
  cases, 6 targets each with and without `MinTriangleQuality`); no case gets a larger Hausdorff distance.
- Their F 0.1 % drops slightly in 15 cone cases with q 0.2 (by up to 0.07, cone16_x0.6) and the Edge Chamfer distance
  rises: the pinned, faceted tip has edges sharper than 30 degrees around it that the measures count as extra feature
  edges, while normal consistency rises (eg cone16_x0.6 0.748 -> 0.908). Without `MinTriangleQuality` the pinned tip
  also keeps a few more poor triangles and pairs on the cones (6 more cases not clean); with it 3 cones end with 5..10
  more pairs.
- On the 20 Thingi10K meshes it pins 1 vertex, in 39460 (a point with a deficit of more than 90 degrees; the same at
  any threshold of 30..90): there the Hausdorff distance drops from 0.779 to 0.327 target (q 0, 0.842 -> 0.272), edge
  recall 0.979 -> 0.986, with 6 more poor triangles (26 -> 32) and a 1 % worst min angle of 2.05 instead of 2.52
  degrees. That mesh is the whole change of the Thingi10K means; the other 19 are identical. The gears and tubes have
  no such vertices.
- The default of 75 degrees is above the 60 degrees at every vertex of an icosahedron (60 would pin all 12, decided
  by rounding); a coarse tessellation of a smooth surface can still have every vertex above it (a cube: 90).

## Thin walls: thickness-aware sizing (opt-in)
`Remesher.TargetEdgeLengthF` sets a target edge length per point (capped by the global one);
`RemeshSizing.FromWallThickness(input, f)` makes it f times the local wall thickness (`MeshWallThickness`,
InscribedSphere at the input triangles' centroids, the smallest of the triangles at an input vertex, interpolated
over the nearest input triangle, at least 1e-3 of the diagonal; the remesher splits above 1.33 and collapses below
0.66 times it). The `tubes` (walls 0.05 / 0.1, targets 5..32 times the wall) and Thingi10K 135363 (walls
0.75, target 11.6), with the g3cli settings (q 0.2), 4 cores, net10.0, Release, `runs=1`:
```
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 si=1 only='tube|^135363$' thingi=benchmarks/RemeshBenchmark/data/meshes
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 sizing=1 only='tube|^135363$' thingi=benchmarks/RemeshBenchmark/data/meshes
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 sizing=1 si=1 only='tube'
```

| tubes (24) | pairs | clean | volume / input | F 0.1 % | mean hausdorff/target | q<1e-3 (cases) | min angle 1 % worst | triangles | time s (sum) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| q 0.2 (g3cli default) | 5352 | 0 | 0.16..7.04 | 0.015..0.125 | 0.311 | 0 (0) | 0.9..14.4 | 54..434 | 0.3 |
| + PreventSelfIntersections | 0 | 24 | 0.54..4.77 | 0.005..0.043 | 0.996 | 0 (0) | 0.7..11.4 | 54..346 | 3.3 |
| + sizing 1 | 1524 | 8 | 0.992..1.000 | 0.942..1.000 | 0.025 | 20 (4) | 32.8..34.7 | 12192..50390 | 57.9 |
| + sizing 1 + PreventSelfIntersections | 0 | 24 | 1.000 | 0.944..1.000 | 0.020 | 20 (8) | 33.2..35.0 | 12238..50198 | 2114 |

- `PreventSelfIntersections` alone blocks the folds but does not avoid them: no pairs, no inside-out result, but the
  volume and the surface stay far off (the coarse result cannot represent a wall thinner than its edges).
- The sizing keeps the walls: volume and surface as the input, and the 1 % worst min angle rises to about 33
  degrees, with a few near-degenerate triangles (20 with q < 1e-3 in 4 / 8 of the 24 results). The pairs that remain
  are removed by the check; on tube16_w0.05 (the one case looked at) they are folds of the inner wall onto itself
  (no shared vertex). The result does not depend on
  the global target any more (the field decides everywhere on these inputs), and it costs the triangles the walls
  need: 12k (wall 0.1) to 50k (wall 0.05) instead of about 100.
- The check costs most where the global target is far above the wall (tube64_w0.05: 57 s at target 0.5, 551 s at
  1.6): the passes start from the coarse input and many operations are rejected on the way.
- 135363: `PreventSelfIntersections` alone gives a clean result (0 pairs, `NotCleanReasons` empty) but with 1.31 of
  the volume, F 0.1 % 0.18 and Hausdorff 0.46 target. The sizing alone gives volume 1.00, F 0.1 % 0.98, Hausdorff
  0.04 target, but 1113702 triangles (514 before), 494 s, 22613 pairs (243 before), degenerate triangles, 146193
  triangles with q < 1e-3 (131 before) and a 1 % worst min angle of 0.0002 degrees (0.026 before). A diagnostic run
  (the earlier, not interpolated field: 210k triangles, 30718 pairs) found about 98 % of the pairs in planes x, y or
  z = const, nearly flat slivers; whether some are `ExactTriangleIntersection` false positives (`docs/roadmap/exact-boolean.md` stage R) is
  not checked. With the check as well (the earlier field) it ran for more than 90 minutes and was stopped. Repro for the faster self-intersection check:
  `dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=1 base=0.2 quality=0.2 sizing=1 si=1 only='^135363$' thingi=benchmarks/RemeshBenchmark/data/meshes`
- `g3cli remesh --thickness-sizing 1` uses it (off by default: it multiplies the triangle count at thin walls); the
  warning about an edge length far above the wall thickness names it, and stays when the fraction leaves the edges at
  the thinnest walls above 4 times their thickness.

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
- Open: the cone apices (below) and the self-intersections that remain (`docs/roadmap/alpha-wrap-and-repair.md`).

Earlier decision (#158): the default stayed 0 until the fidelity metrics were measured; this section replaces it.
