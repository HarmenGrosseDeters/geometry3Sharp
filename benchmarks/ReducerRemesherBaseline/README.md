# ReducerRemesherBaseline

Baseline of `Reducer` and `Remesher` for the "Reducer / Remesher 2.0" milestone (target proposal:
[REDUCER_REMESHER_20.md](../../docs/design/REDUCER_REMESHER_20.md)). No library change: the library defaults, measured as a
caller would use them. Per case and variant:

- **time** (median of `runs`, warm: a JIT warm-up run first; GC before each run) and µs per input triangle;
- **quality** of the result: min angle, p01 (the 1 % quantile of the triangles' smallest angles), triangles with an
  angle below 0.5° or above 170° (slivers, caps; a degenerate triangle counts in both);
- **fidelity** against the input: certified one-sided Hausdorff upper bounds both ways (`MeshHausdorffDistance`,
  tolerance 0.001 of the box diagonal, reported relative to that diagonal), F-score at 0.1 % and 0.5 % of the size
  (`MeshFidelityMetrics`, 20000 samples), volume IoU where both meshes are closed;
- **topology**: `MeshTopologySummary.Changes` against the input (handles, components, boundary loops), cases with
  self-intersecting triangle pairs above the input's (`MeshRepairMetrics.SelfIntersections`; a lower bound of the new ones, exact for the 19 inputs without pairs; five Thingi10K inputs have 12 to 1298 of their own),
  `MeshRobustRepair.NotCleanReasons` (without the self-intersection check, which the
  previous column has) - an open input is of course "not closed";
- **determinism**: two runs give the same mesh bit for bit (`repeat`), and for the Remesher also the run with
  `EnableParallelSmooth` and `EnableParallelProjection` off (`serial`; the Reducer has no parallel path);
- **scale invariance**: the eight similarities of `tests/geometry3Sharp.Tests/Harness/Invariance.cs`. A power-of-two scaling must give the same mesh bit for bit, mapped back; for the others the triangle count is compared
  (JSON field `inv_other_tri_dev_max`). Only for inputs up to `inv_max` triangles.

Variants: `default` (library defaults) and `si` (`PreventSelfIntersections`, only up to `si_max` triangles, it is 6-12 times slower).

- **Reducer**: `ReduceToTriangleCount` to 10 % and 1 % of the input, no projection target.
- **Remesher**: the settings of `g3cli remesh` (20 passes, smoothing 0.5, projection onto the input, boundary and 30° creases
  kept, `MinTriangleQuality` at the default 0), target edge length 1 x and 2 x the mean edge length of the input.

## Usage

```
python3 benchmarks/RemeshBenchmark/fetch_data.py        # the 20 Thingi10K meshes (2k-50k faces, closed, manifold, seed 42); nothing is committed
dotnet run -c Release --project benchmarks/ReducerRemesherBaseline -- thingi=benchmarks/RemeshBenchmark/data/meshes inv_max=100000 json=all.jsonl > summary.md
dotnet run -c Release --project benchmarks/ReducerRemesherBaseline -- summary=all.jsonl           # only the tables, from the JSON lines
```

Options: `only=<regex>`, `procedural=0`, `runs=<n>` (default 2), `si_max`, `inv_max`, `invariance=0`, `fidelity=0`,
`ops=reduce,remesh`, `hd_tol=<f>` (tolerance of the Hausdorff bounds relative to the diagonal, default 0.001: the bounds can be that much too high; use 0.0001 to compare small differences). Knobs for the probes of the design note, off in the baseline: `profile=1` (`Remesher.ENABLE_PROFILING`,
the phase times of each pass on stdout), `reduce_q=<q>` (`Reducer.MinCollapseTriangleQuality`), `remesh_q=<q>`
(`Remesher.MinTriangleQuality`), `remesh_crease=<deg>`, `remesh_corner=<deg>` (the crease angle of the constraints, 0: boundary only), `reduce_fold=0` (`Reducer.PreventFoldOver` off), `reduce_worst=1` (the internal `CollapseQualityAgainstWorst`, set by reflection). The logic that is not a measurement (statistics, similarities, mesh hash, angle
counts, procedural inputs) is in `Harness.cs` and tested by `BaselineHarnessTests`.

Inputs: 4 procedural meshes (a bumpy sphere of 95052 triangles, a torus of genus 1 with 67600, an open terrain with 96800,
a box with sharp 90° edges with 57132) and the 20 Thingi10K meshes (genus 0 to 96, five of them with 12 to 1298 self-intersecting pairs;
Thingi10K models have individual licenses, see `RemeshBenchmark/data/input_summary.csv`).

## Results (2026-11-09)

Cloud container, 4 cores (`nproc`), net10.0, Release, `runs=2`, library at master 2c7ba9f. The raw JSON lines are not committed.
Format: median (p10-p90); "(max)" is the maximum over the cases, p01 "(input)" the input's own p01.
The column `out tris / ...` is relative to the target triangle count for the Reducer and to the input triangle count for the Remesher.


### What the baseline shows

(The numbers are in the tables below; the thingi rows are the 20 real meshes, the procedural rows are clean.)

1. **Speed.** The default Reducer takes 3.2 µs per input triangle (thingi median; 4 µs on the 60-100k procedural meshes, 0.3-0.4 s),
   `PreventSelfIntersections` 21 µs (6-7 x). The Remesher takes 11-23 µs per input triangle (x2 / x1) and 121-282 µs with
   `PreventSelfIntersections` (12 x). The checks take 84 % and 92 % of the time.
2. **Reducer quality on real meshes is poor by default.** At 10 % the median p01 is 0.6° (input 0.1°), 19 of 20 meshes have
   slivers or caps (1644 below 0.5°, 1796 above 170° in total); at 1 % the Reducer stops short of the target (more than 1.1 x) on 8 of 20 meshes (p90 5.2 x, max 16.8 x; for 4 of them, genus 6, 7, 16 and 83, the target is below the minimum triangle count of that genus). On the clean procedural meshes p01 is 8° (input 27°) and the box has degenerate triangles.
3. **Reducer fidelity at 1 %**: the certified Hausdorff distance is 7 % of the box diagonal at the median of the thingi meshes (max 30 %); F-score at
   0.1 % is 0.17. At 10 % it is 0.9 % (max 5.7 %).
4. **The default Reducer creates self-intersections** where the input has none: 9 of the 19 inputs without pairs at 1 % (up to 449 pairs), 4 at 10 % (up to 192).
   With `si` none on the 12 such inputs that ran (the 3 above `si_max` did not), at 6-7 x the time.
5. **Remesher quality on real meshes**: p01 1.3° (x1) and 0° (x2), 17-18 of 20 meshes with degenerate triangles ("not clean"),
   18-19 with slivers; 14 of the 19 inputs without pairs get some (up to 10737); the procedural meshes are good (p01 40°, no slivers, hd 0.1 %).
   `si` removes the added self-intersections but not the degenerate triangles (4-8 of 16 still).
6. **Topology** is kept in all cases (no handle, component or loop changes) - for both operations and both variants.
7. **Determinism**: all repeated runs are identical; the Remesher with parallel smoothing and projection off gives the same mesh bit for bit in all cases.
8. **Scale invariance**: the Reducer is bit-identical under power-of-two scaling in all cases (0 differ). The Remesher is not in 4 cases
   (terrain x1 at 2^10; thingi 299291 at 2^-10, also with `si`; 93370 at 2^-10): an absolute constant somewhere (cause not diagnosed here).
   For the other five similarities (rounding differs) the triangle count differs by up to 25 % (Reducer; 31 % with `si`) and 45 % (Remesher with `si`, 3 % without) in single cases:
   the algorithms follow different paths in the float noise; a bound for that is a target of the proposal, not a defect of the baseline.

### Probes (the knobs off in the tables)

`reduce_q=0.3` (`Reducer.MinCollapseTriangleQuality`, the #405 guard in its plain form), 20 thingi meshes, no `si`:
procedural p01 8.4° -> 12.8° (1 %) and 8.2° -> 12.0° (10 %), no slivers, time and fidelity unchanged; **but on the thingi meshes the
target is missed by a factor 6 (10 %) to 59 (1 %)**, because the inputs have triangles below 0.3 and every collapse around them is refused. The guard
needs the "against the worst triangle" form (`CollapseQualityAgainstWorst`, internal) for general meshes.
`remesh_q=0.2` (`Remesher.MinTriangleQuality`): thingi x1 p01 1.3° -> 5.7°, slivers (both counts added) 6213 -> 1263 in 18 of 20, degenerate triangles 17 -> 0 of 20 meshes, cases with more pairs than the input 19 -> 13,
time +40 % (23 -> 32 µs/tri); x2 p01 0 -> 1.8°.
`profile=1`: 53 % (thingi 78295), 69 % (93370) and 74 % (bumpy sphere) of the Remesher's time are the serial edge operations
(flips are the largest part: 62 % on the sphere); smoothing and projection (26-47 %) are parallel already.

### Tables

### reduce 1% default, procedural (4 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.401 (0.302-0.576) | 4.25 (3.94-9.15) | 0.999 (0.998-1) | 8.35 (2.11-11.9) (27.2 (19.3-33.5)) | 3.09 (0.733-5.89) | 70 / 130 (2) | 0.00446 (0.00189-0.00582) (0.00619) | 0.0048 (0.00188-0.00731) (0.00803) | 0.781 (0.53-0.96) | 1 (0.999-1) | 0.994 (0.984-0.999) | 0 | 0 | 2 | 0 / 0 | 0 of 8 (0) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: not closed x1; degenerate triangles x1.


### reduce 1% default, thingi (20 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.0237 (0.00961-0.162) | 3.42 (2.79-4.14) | 1 (0.986-5.24) | 2.5 (0.0196-6.29) (0.0966 (0.0138-1.31)) | 0.7 (0.00913-6.03) | 372 / 513 (15) | 0.0703 (0.0243-0.22) (0.296) | 0.0412 (0.024-0.0968) (0.116) | 0.168 (0.0378-0.327) | 0.526 (0.183-0.721) | 0.616 (0.251-0.845) | 0 | 9 | 0 | 0 / 0 | 0 of 40 (0) |


### reduce 1% si, thingi (16 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.127 (0.0749-0.397) | 22.3 (16.1-44.7) | 1 (0.992-12.4) | 1.9 (0.0248-6.36) (0.124 (0.0196-1.89)) | 0.541 (0.00419-5.69) | 97 / 130 (12) | 0.0637 (0.0229-0.13) (0.29) | 0.044 (0.0236-0.105) (0.115) | 0.158 (0.0665-0.369) | 0.472 (0.249-0.722) | 0.657 (0.194-0.891) | 0 | 0 | 0 | 0 / 0 | 0 of 32 (0) |


### reduce 10% default, procedural (4 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.428 (0.269-0.648) | 4.46 (3.51-10.3) | 1 (1-1) | 8.19 (2.13-10.3) (27.2 (19.3-33.5)) | 1.42 (0.288-2.39) | 744 / 1032 (4) | 0.00138 (0.00111-0.00175) (0.00191) | 0.00142 (0.00112-0.00177) (0.00191) | 1 (1-1) | 1 (1-1) | 1 (0.998-1) | 0 | 0 | 2 | 0 / 0 | 0 of 8 (0) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: not closed x1; degenerate triangles x1.


### reduce 10% default, thingi (20 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.0241 (0.0105-0.136) | 3.15 (2.54-4.33) | 1 (0.998-1) | 0.605 (0.0426-1.75) (0.0966 (0.0138-1.31)) | 0.038 (0.00549-0.772) | 1644 / 1796 (19) | 0.00911 (0.003-0.0318) (0.0569) | 0.00778 (0.00267-0.0207) (0.0422) | 0.834 (0.322-1) | 0.999 (0.845-1) | 0.973 (0.798-0.995) | 0 | 4 | 0 | 0 / 0 | 0 of 40 (0) |


### reduce 10% si, thingi (16 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.128 (0.0662-0.386) | 21.1 (14.3-44.5) | 1 (0.997-1.29) | 0.605 (0.071-2.56) (0.124 (0.0196-1.89)) | 0.0916 (0.00419-0.893) | 258 / 346 (15) | 0.00928 (0.0032-0.0411) (0.105) | 0.011 (0.00291-0.0234) (0.0408) | 0.733 (0.282-0.99) | 0.999 (0.602-1) | 0.959 (0.731-0.995) | 0 | 0 | 0 | 0 / 0 | 0 of 32 (0) |


### remesh x1 default, procedural (4 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2.78 (2-3.33) | 34 (32.5-35.4) | 0.968 (0.869-1.01) | 40.1 (37.9-40.6) (27.2 (19.3-33.5)) | 16.2 (13-22.5) | 0 / 0 (0) | 0.00114 (0.00104-0.00116) (0.00116) | 0.00114 (0.00104-0.00116) (0.00116) | 1 (1-1) | 1 (1-1) | 1 (1-1) | 0 | 0 | 1 | 0 / 0 | 1 of 8 (1) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: not closed x1.


### remesh x1 default, thingi (20 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.191 (0.0478-1.01) | 23 (14-39.8) | 0.484 (0.215-0.953) | 1.3 (0-26.3) (0.0966 (0.0138-1.31)) | 0 (0-0.228) | 2529 / 3684 (18) | 0.0224 (0.00483-0.155) (0.244) | 0.0118 (0.00473-0.0339) (0.116) | 0.76 (0.419-0.963) | 0.978 (0.793-1) | 0.935 (0.653-0.993) | 0 | 19 | 17 | 0 / 0 | 2 of 40 (2) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: degenerate triangles x17.


### remesh x1 si, thingi (16 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1.56 (0.606-4.8) | 282 (189-423) | 0.593 (0.254-0.954) | 1.08 (0.00177-17.2) (0.124 (0.0196-1.89)) | 2.95E-05 (3.19E-15-9.13) | 2554 / 2791 (14) | 0.0146 (0.00512-0.152) (0.228) | 0.0127 (0.0052-0.0373) (0.116) | 0.79 (0.332-0.958) | 0.984 (0.783-1) | 0.954 (0.667-0.993) | 0 | 0 | 8 | 0 / 0 | 0 of 32 (0) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: degenerate triangles x8.


### remesh x2 default, procedural (4 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.909 (0.584-1.12) | 11.1 (9.62-11.6) | 0.235 (0.214-0.242) | 42.5 (40.5-43.2) (27.2 (19.3-33.5)) | 29.4 (18.9-33.8) | 0 / 0 (0) | 0.00142 (0.00111-0.00158) (0.00162) | 0.00144 (0.00112-0.00155) (0.00158) | 1 (1-1) | 1 (1-1) | 0.999 (0.998-1) | 0 | 0 | 1 | 0 / 0 | 0 of 8 (0) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: not closed x1.


### remesh x2 default, thingi (20 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.0833 (0.029-0.372) | 11.2 (7.77-14.9) | 0.233 (0.0884-0.318) | 0 (0-0.848) (0.0966 (0.0138-1.31)) | 0 (0-0.0153) | 3615 / 5390 (19) | 0.0402 (0.0204-0.148) (0.324) | 0.0225 (0.014-0.0531) (0.116) | 0.613 (0.25-0.818) | 0.78 (0.467-0.976) | 0.818 (0.438-0.971) | 0 | 19 | 18 | 0 / 0 | 0 of 40 (0) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: degenerate triangles x18.


### remesh x2 si, thingi (16 cases)

| time s median (p10-p90) | us per input tri | out tris / target (reduce) or input tris (remesh) | p01 deg (input) | min angle deg | slivers <0.5 / >170 deg (cases) | hd in->out (max) | hd out->in (max) | F 0.1% | F 0.5% | vol. IoU | topology changed | more self-int. pairs than input (cases) | not clean | repeat / serial differ | invariance: pow2 differ (cases) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0.669 (0.349-2.07) | 121 (74-229) | 0.26 (0.107-0.46) | 0.145 (0.00365-8.32) (0.124 (0.0196-1.89)) | 0.000329 (1.76E-13-0.917) | 764 / 1063 (15) | 0.0402 (0.0158-0.146) (0.254) | 0.0261 (0.0146-0.0659) (0.116) | 0.634 (0.241-0.826) | 0.803 (0.489-0.986) | 0.863 (0.421-0.975) | 0 | 0 | 4 | 0 / 0 | 1 of 32 (1) |
Hausdorff bound not converged (2M subdivisions): 0 cases. Not clean: degenerate triangles x4.
