# RemeshQualityBenchmark

`Remesher.MinTriangleQuality` (opt-in, default 0 = off) on and off, with the settings of `g3cli remesh` (20 passes,
smoothing 0.5, projection onto the input, boundary and 30 degree creases kept by `PreserveBoundaryAndFeatures`,
target = a multiple of the input's mean edge length; on = 0.2). Per case: poor (q < 0.1) and flat (q < 1e-3)
triangles, self-intersecting triangle pairs (`MeshRepairMetrics.SelfIntersections`), inside-out components
(`MeshPostRepairCleanup.FindInvertedComponents`, directly: `NotCleanReason` stops at its first failed check), Hausdorff
distance to the input (`MeshHausdorffDistance`, within 0.01 target) and time, and the cases that get worse with the option.

## Usage
```
python3 benchmarks/RemeshBenchmark/fetch_data.py       # optional: the 20 Thingi10K meshes of RemeshBenchmark
mkdir -p benchmarks/RemeshQualityBenchmark/results
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=3 thingi=benchmarks/RemeshBenchmark/data/meshes > benchmarks/RemeshQualityBenchmark/results/all.txt
```
`only=<regex>` selects cases, `quality=<q>` the threshold. One JSON line per case (off and on), then the summary
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
4 cores, net10.0, Release, `runs=3` (median time; the results are deterministic, two full runs gave the same numbers).

| group | cases | q<0.1 off / on | q<1e-3 off / on | cases with q<1e-3 off / on | self-intersecting pairs off / on | cases with self-int. off / on | cases inside-out off / on | mean hausdorff/target off / on | time s off / on | worse: more pairs / more q<1e-3 / hausdorff / inside-out |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| primitives | 150 | 50 / 7 | 38 / 0 | 22 / 0 | 461 / 285 | 35 / 16 | 0 / 0 | 0.333 / 0.344 | 4.7 / 6.6 | 10 / 0 / 14 / 0 |
| gears | 18 | 1229 / 438 | 602 / 0 | 10 / 0 | 7697 / 0 | 17 / 0 | 0 / 0 | 0.161 / 0.010 | 0.3 / 0.4 | 0 / 0 / 0 / 0 |
| tubes | 24 | 132 / 103 | 1 / 0 | 1 / 0 | 6098 / 5344 | 24 / 24 | 3 / 0 | 0.402 / 0.307 | 0.2 / 0.2 | 4 / 0 / 4 / 0 |
| thingi | 20 | 5763 / 1638 | 1541 / 205 | 17 / 11 | 19910 / 3158 | 19 / 17 | 1 / 1 | 1.115 / 1.017 | 7.2 / 11.4 | 0 / 0 / 5 / 0 |

"worse" counts the cases with more self-intersecting pairs, with more q < 1e-3 triangles, with a Hausdorff
distance more than 0.02 target larger (the two measurement tolerances) than without the option, or inside-out only
with it. Without the
self-intersection check, `MeshRobustRepair.NotCleanReason` finds 17 of the 20 Thingi10K results not clean without the
option (degenerate triangles), 1 with it (135363: inside-out; the degenerate test of `NotCleanReason`, area below
1e-12 diagonal^2, is far weaker than q < 1e-3). 135363 is inside-out without the option too, hidden behind
"degenerate triangles" (next section).

### Target far above the wall thickness: undefined result in both variants
135363 is a 0.8 plate with thin-walled tubes (walls 0.75); the target (its mean edge) is 11.6. The remeshers fold the
inner and outer walls through each other (a tube becomes about four chords) and the signed volume drifts through
zero: 4448 (input) -> -4706 without the option (sign change at pass 10), -4021 with it (pass 8). The `tubes` group
reproduces this procedurally: every case ends with dozens to hundreds of self-intersecting pairs and 2%..700% of the
input volume, the sign of the volume changing back and forth over the passes; which cases end inside-out is chance
(`Remesher`: 3 without the option, none with it; `RemesherPro`: tube16_w0.05_t1.6 only without, tube32_w0.05_t1.6
only with it). The 4 tubes with more pairs and the 4 with a larger Hausdorff distance are of the same kind.
Results for such inputs are undefined with and without `MinTriangleQuality`; a guard that reports inside-out
results and a wall-thickness warning are in `docs/ROADMAP.md`.

### Cases that get worse
- **More self-intersecting pairs (10 primitives, none of the gears or Thingi meshes):** taper10_x0.15 (12 -> 16),
  taper16_x0.1 (0 -> 12), taper32_x0.1 (0 -> 12), taper32_x0.6 (0 -> 8), cone16_x0.45 (0 -> 10), cone16_x0.6
  (51 -> 67), cone24_x0.15 (0 -> 6), cone24_x0.3 (0 -> 10), cone24_x0.6 (0 -> 31), cone32_x0.6 (0 -> 8). All at the
  bottom rim, where the flat cap meets the side. Counted per phase (taper16_x0.1, taper10_x0.15), the option creates
  far fewer intersections during the passes (edge operations 1248 -> 230 and 395 -> 56, before the review fixes), but projection moves across
  the rim intersect without flipping a normal, and which intersections survive the last pass is chance. Preventing
  them needs an intersection test per operation: `docs/ROADMAP.md`, "Remeshers produce self-intersections".
- **Hausdorff distance, cone apex (11 primitives):** cone12, cone16, cone24 and cone32 at some targets, by up to 0.81
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
The default stays 0 (the original behaviour). Whether `g3cli remesh` or the internal callers turn the option on is
decided later with the fidelity metrics (F-score, normal consistency, feature preservation) of the benchmark
integration, not on the Hausdorff distance alone.
