# QuadFlow3BGate

The gate measurement of stage 3B, pure quads by `QuadRemesher.PureQuads` ([QUAD_REMESHING.md](../../docs/design/QUAD_REMESHING.md) 4.4;
`mesh_ops/QuadFlowRepair.cs`, `solvers/MinCostFlow.cs`, `solvers/GroupSatSolver.cs`). 9 procedural shapes (sphere, torus, torus with
`CurvatureAlignment` 10, bumpy sphere, filleted box, noisy sphere, capsule, genus 2, capped cylinder) x 6 variations (target edge length
x 1, 0.8, 1.25, 1.5 at input edge 0.5 s; input edge 0.35 s and 0.7 s at x 1) = 54 runs, the same set as the throwaway spike
(`runs/quad-3b`, `benchmarks/QuadFlow3BSpike`, rounds 4 and 5). Every run computes the stage 2 mesh (`PureQuads = false`) and the pure-quad
attempt (`PureQuads = true`, same cross field) and classifies it:

- **clean**: `PureQuadStatus.Success` and independent checks on `Result` pass: manifold, 100 % quads, finite, valid, and no quad whose
  normal (cross of the diagonals) faces away from the input triangle nearest to the quad's centre (this check does not use the library's own
  `FlippedQuads`).
- **fallback**: another status, and `Result` equals the stage 2 mesh bit for bit (checked here).
- **undetected bad**: `Success` with a failed independent check, or a fallback whose `Result` is not the stage 2 mesh. **Must be 0**
  (the exit code is 1 otherwise).

Time gate (owner 2026-11-19, replaces the 2x gate of QUAD_REMESHING.md for 3B): a clean run takes at most max(3 x stage 2, 1 s). Both
times are the wall time of `Compute()` with the cross field given (`repeat=3`: the minimum of three runs, after a JIT warm-up), so the
pure-quad time includes the stage 2 extraction that the fallback needs.

```
dotnet run -c Release --project benchmarks/QuadFlow3BGate -- [only=<regex>] [profile=1] [repeat=<n>] [adapt=0|1|2] [mingain=<x>]
```

`profile=1` adds a `PROF` line per run with the stages of the attempt (`QuadFlowRepair` profile: setup, flow preparation, flow network and
min-cost flow, subdivision, greedy moves, SAT, position solve, extraction) and the solver counters. `adapt=0|1|2` selects
`SizeAdaptationMode.Off`, `Scale`, or `ScaleAndRotation` (with `mingain`, default `0.2`). `results_gate.txt` is the full output of
`profile=1 repeat=3` (net10.0 Release, 4-core cloud container, 2026-11-19).

## Results (2026-11-19)

(Measured with the stage 2 default `CollapseLength` 0.4. With the new default 0.6 (owner approved 2026-11-19) the 54 runs give the same counts: 25 clean / 29 fallback / 0 undetected bad, time gate 25 / 25, irregular gate 25 / 25, Hausdorff 24 / 25, corner p95 17 / 25; clean rows median 2.7 x stage 2, max 8.1.)

| | spike baseline (round 4, 54 runs) | spike, chosen levers (round 5) | **library (this PR)** |
|---|---|---|---|
| clean / fallback / undetected bad | 23 / 31 / 0 | 25 / 29 / 0 | **25 / 29 / 0** |
| clean rows: ratio to stage 2, median / max | 11.6 / 45.2 | 2.8 / 9.2 (position field + 3B against position + extract) | 2.7 / 7.7 (`Compute()` against `Compute()`) |
| clean rows: seconds, median / max | not recorded | 0.29 / 0.91 | 0.16 / 0.85 |
| clean rows within max(3 x stage 2, 1 s) | 0 / 23 (2x gate) | n/a | **25 / 25** |
| fallback rows: seconds, median / max | 7.4 / 50.1 | 0.69 / 2.2 | 0.48 / 2.13 |
| irregular <= singularities + 10 % of stage 2 (clean rows) | 23 / 23 | 25 / 25 | 25 / 25 |
| Hausdorff <= 0.5 s (clean rows) | 23 / 23 | 24 / 25 | 24 / 25 (filleted box 1.25: 0.52 s) |
| corner p95 <= 15 degrees (clean rows; stage 2 on the same rows) | 18 / 23 | 17 / 25 | 17 / 25 (18 / 25) |

The library has the same 25 clean rows as the spike's chosen configuration. Statuses of the 54 runs: Success 25, BudgetExceeded 26,
OrientationFolds 3 (the SAT budget of 300k decisions is exhausted on 26 of the 29 fallbacks; the other 3 end with folds the repair cannot
remove). Clean per shape (of 6): torus with alignment 6, genus 2 6, filleted box 5, sphere 3, torus 3, bumpy 1, noisy sphere 1, capsule 0,
capped cylinder 0.

Default output unchanged (`PureQuads = false`): the stage 2 results of these 54 runs (vertex coordinates bit for bit, faces) hash to the
same value on master (1d2a270a) and on this branch (FNV-1a over all runs, `95261eec9149b31d`).

## Where the time goes (sum over the rows, seconds, library, `repeat=3`)

| stage | 25 clean rows | 29 fallback rows |
|---|---|---|
| setup (offsets, triangles) | 0.33 | 0.51 |
| flow preparation (equation rotation, greedy balancing) | 0.12 | 0.16 |
| **flow (network build + min-cost flow, all capacities)** | **1.98 (50 %)** | **9.96 (54 %)** |
| subdivision | 0.04 | 0.07 |
| greedy vertex moves | 0.04 | 0.10 |
| SAT (local repair, 300k decisions at most) | 0.80 (20 %) | 7.27 (40 %) |
| position solve (contracted, `SparseCholesky`) | 0.29 | - (not reached) |
| extraction (pairing, projection onto the input, metrics) | 0.39 | - (not reached) |

The min-cost flow is the largest stage on both. On a fallback it is followed by the SAT, which runs until the budget is used up
(0.13-0.37 s per fallback; the spike without the total budget spent up to 29 s there). On clean rows the SAT is next, then the extraction and the
setup. A clean row spends 0.01 s to 0.33 s in the flow (3k to 27k input vertices). The large flow times are on fallback rows that need a capacity
above 2 (the flow is solved again for every capacity step: up to 1.8 s on the noisy sphere at input edge 0.35 s, 1.4 s on the capped cylinder at scale 0.8, which reaches capacity 10).

## Results (2026-11-21: geometric sign reference, cost-guided fixed-edge balancing, and `SizeAdaptation`)

Command (`net10.0`, Release, single-run `repeat=1` vs `repeat=3` in the 2026-11-19 table above):
```
dotnet run -c Release --project benchmarks/QuadFlow3BGate -- adapt=0
dotnet run -c Release --project benchmarks/QuadFlow3BGate -- adapt=1
dotnet run -c Release --project benchmarks/QuadFlow3BGate -- adapt=2
```

| metric (54 runs) | baseline (`adapt=0`) | updated (`adapt=0`) | updated (`adapt=1`, `Scale`) | updated (`adapt=2`, `ScaleAndRotation`) |
|---|---|---|---|---|
| clean / fallback / undetected bad | 25 / 29 / 0 | **43 / 11 / 0** | **43 / 11 / 0** | **48 / 6 / 0** |
| clean on 7 non-cylindrical shapes (of 42) | 25 / 42 | **42 / 42** | **42 / 42** | **42 / 42** |
| `capsule` / `capped-cylinder` clean (of 6 each) | 0 / 0 | 1 / 0 | 1 / 0 | 1 / **5** |
| time gate `max(3 x stage 2, 1 s)` (clean rows) | 25 / 25 | 41 / 43 | **43 / 43** | **48 / 48** |
| irregular gate (clean rows) | 25 / 25 | **43 / 43** | **43 / 43** | **48 / 48** |
| Hausdorff <= 0.5 s (clean rows) | 24 / 25 | 41 / 43 | 42 / 43 | 42 / 48 |
| corner p95 <= 15 deg (clean / stage 2 same rows) | 17 / 25 (18 / 25) | 20 / 43 (31 / 43) | 26 / 43 (42 / 43) | **29 / 48** (44 / 48) |
| ratio pure / stage 2 (median / max) | 2.6 / 8.9 | 2.5 / 5.3 | 1.6 / 3.0 | **1.4 / 2.0** |
| SAT time sum over clean rows (s) | 0.65 | **0.04** | **0.05** | **0.08** |

## Open and creased shapes (`open=1`, Phase 4c measurement, 2026-11-22)

`dotnet run -c Release --project benchmarks/QuadFlow3BGate -- open=1 [only=<regex>] [adapt=0|1|2]` runs 10 procedural shapes (closed sphere as the reference, open patch, sphere cap, open strip, open tube, sharp box, open box, sharp cylinder, L profile, gear) at the scales 1, 0.8 and 1.25 (input edge 0.5 s) and prints per run: the stage 2 mesh (`PreserveCreases` on the creased shapes) with its feature metrics (feature = boundary edge or crease edge above 30 degrees: coverage within 0.1 s and 10 degrees, angle p95, feature Hausdorff), what `PureQuads` does today, and the raw integer map of the position field (triangles, boundary slots, triangles whose offsets do not sum to 0, inverted triangles, largest |d|; through reflection, no library change). If `QuadFlowRepair` has the static field `FixFeatureEdges` (the prototype of the branch `runs/quad-4c-prototype`, never merged) the attempt is repeated with the feature edges fixed and a summary per group is printed. The numbers and the decision (stop rule) are in [QUAD_REMESHING.md 4.7](../../docs/design/QUAD_REMESHING.md).

## `align=<x>`: `CurvatureAlignment` on the capsule and the capped cylinder (2026-11-22)

`align=<x>` overrides `CrossField.CurvatureAlignment` of every selected shape (default: the per-shape value, 0 except `torus-align10`). `only='capsule|capped-cylinder' align=0|1|3|10|30|100` gives capsule 1, 1, 1, 3, 1, 5 clean runs of 6 and capped cylinder 0 of 6 at every weight, 0 undetected bad; the table and the reading are in [QUAD_REMESHING.md 4.4b](../../docs/design/QUAD_REMESHING.md).
