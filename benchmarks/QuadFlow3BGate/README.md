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
dotnet run -c Release --project benchmarks/QuadFlow3BGate -- [only=<regex>] [profile=1] [repeat=<n>]
```

`profile=1` adds a `PROF` line per run with the stages of the attempt (`QuadFlowRepair` profile: setup, flow preparation, flow network and
min-cost flow, subdivision, greedy moves, SAT, position solve, extraction) and the solver counters. `results_gate.txt` is the full output of
`profile=1 repeat=3` (net10.0 Release, 4-core cloud container, 2026-11-19).

## Results (2026-11-19)

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
