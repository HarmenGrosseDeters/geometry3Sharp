# RemeshQualityBenchmark

`Remesher.MinTriangleQuality` (opt-in, default 0 = off) on and off, with the settings of `g3cli remesh` (20 passes,
smoothing 0.5, projection onto the input, boundary and 30 degree creases kept by `PreserveBoundaryAndFeatures`,
target = a multiple of the input's mean edge length; on = 0.2). Per case: poor (q < 0.1) and flat (q < 1e-3)
triangles, self-intersecting triangle pairs (`MeshRepairMetrics.SelfIntersections`), inside-out components
(`MeshPostRepairCleanup.FindInvertedComponents`, directly: `NotCleanReason` stops at its first failed check), passes
rolled back by `Remesher.RejectInvertingPasses` (default on, in both runs), Hausdorff distance to the input
(`MeshHausdorffDistance`, within 0.01 target) and time, and the cases that get worse with the option.

## Usage
```
python3 benchmarks/RemeshBenchmark/fetch_data.py       # optional: the 20 Thingi10K meshes of RemeshBenchmark
mkdir -p benchmarks/RemeshQualityBenchmark/results
dotnet run -c Release --project benchmarks/RemeshQualityBenchmark -- runs=3 thingi=benchmarks/RemeshBenchmark/data/meshes > benchmarks/RemeshQualityBenchmark/results/all.txt
```
`only=<regex>` selects cases, `quality=<q>` the threshold. One JSON line per case (off and on), then the summary
and the list of cases that get worse. No mesh files are committed.

## Alpha-wrap experiment control rerun (2026-10-29)

This is a **before/after code comparison**, not off versus on. Pair `off` before with `off` after, and `on`
before with `on` after. The alpha-wrap experiment changes neither remesher configuration nor defaults.

Command from the repository root, run before and after in Release:

```powershell
dotnet benchmarks\RemeshQualityBenchmark\bin\Release\net10.0\RemeshQualityBenchmark.dll runs=3 quality=0.2 thingi=C:\work\geometry3Sharp\benchmarks\RemeshBenchmark\data\meshes
```

Base SHA `93880ed97a282f356ae96aea465e539130bea69b`, .NET SDK 10.0.402, Intel Core i7-13850HX, Windows.
Raw mixed text/JSON outputs are preserved in the ignored sibling directory
`../MeshRepairBenchmark/results/alpha-quality-20261029/remesh-before.txt` and `remesh-after.txt`.
The session command `python <session-files>\analyze_wrap_experiment.py` extracts JSON lines, pairs `(group, case)`,
checks input fields and every output field except `Seconds`, and writes `original-experiment-summary.json`
in the same results directory.

All 212 cases are paired: 150 primitives, 18 gears, 24 tubes and 20 Thingi meshes. Input triangle/self-pair
counts and `Tris`, `Poor`, `Flat`, `SelfPairs`, `Inverted`, `RejectedPasses`, `Hausdorff`, `NotClean` are exactly
unchanged for **both** modes. No missing cases or geometric regressions were found.

| paired mode | sum of case median seconds before | after | after / before |
|---|---:|---:|---:|
| off -> off | 9.6221516 | 8.8796966 | 0.922839 |
| on -> on | 14.1857607 | 13.2186067 | 0.931822 |

These times are observed rerun variation, not an attributed speedup: remesher behavior is unchanged.
The failed alpha-wrap quality experiment and its separate fidelity gates are documented in
[MeshRepairBenchmark](../MeshRepairBenchmark/README.md#alpha-wrap-collapse-quality-experiment-2026-10-29).

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

| group | cases | q<0.1 off / on | q<1e-3 off / on | cases with q<1e-3 off / on | self-intersecting pairs off / on | cases with self-int. off / on | cases inside-out off / on | cases with rejected passes off / on | mean hausdorff/target off / on | time s off / on | worse: more pairs / more q<1e-3 / hausdorff / inside-out |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| primitives | 150 | 50 / 7 | 38 / 0 | 22 / 0 | 409 / 281 | 35 / 16 | 0 / 0 | 2 / 2 | 0.330 / 0.341 | 5.1 / 7.4 | 11 / 0 / 15 / 0 |
| gears | 18 | 1229 / 438 | 602 / 0 | 10 / 0 | 7697 / 0 | 17 / 0 | 0 / 0 | 0 / 0 | 0.161 / 0.010 | 0.3 / 0.4 | 0 / 0 / 0 / 0 |
| tubes | 24 | 113 / 100 | 1 / 0 | 1 / 0 | 5969 / 5352 | 20 / 24 | 0 / 0 | 8 / 4 | 0.239 / 0.311 | 0.3 / 0.3 | 6 / 0 / 9 / 0 |
| thingi | 20 | 5750 / 1640 | 1536 / 204 | 17 / 11 | 19768 / 3105 | 19 / 17 | 0 / 0 | 1 / 1 | 1.115 / 1.017 | 8.6 / 13.3 | 0 / 0 / 5 / 0 |

"worse" counts the cases with more self-intersecting pairs, with more q < 1e-3 triangles, with a Hausdorff
distance more than 0.02 target larger (the two measurement tolerances) than without the option, or inside-out only
with it. Without the
self-intersection check, `MeshRobustRepair.NotCleanReason` finds 17 of the 20 Thingi10K results not clean without the
option (degenerate triangles), none with it (the degenerate test of `NotCleanReason`, area below 1e-12 diagonal^2,
is far weaker than q < 1e-3).

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
The default stays 0 (the original behaviour). Whether `g3cli remesh` or the internal callers turn the option on is
decided later with the fidelity metrics (F-score, normal consistency, feature preservation) of the benchmark
integration, not on the Hausdorff distance alone.
