# ImprintSliverSweep

Sweep of `ExactMeshImprint` on ulp-shifted near-duplicates (issue #1489, [IMPRINT_SLIVER_CLEANUP.md](../../docs/design/IMPRINT_SLIVER_CLEANUP.md) section 5).
A rotated box (literal corners) and a capped cone frustum (snapped to 2^-24), both libm-free, are shifted by 1 to 12 ulps in both directions along one axis and cut into
each other both ways: 48 runs per cell. A run is unclean if `Compute` fails, `MeshRobustRepair.NotCleanReason` is not empty or `RoundingClean` is false. The frames: the
origin, the translated frame of the test harness (`Similarity.Utm`, far from the origin), its negation, mixed signs, and only z translated. Public API only.

```
dotnet run -c Release --project benchmarks/ImprintSliverSweep            # the table (about 30 s)
dotnet run -c Release --project benchmarks/ImprintSliverSweep -- stats   # with the repair's mean work per cell: initial degenerate / intersections / edits / runs with the final stage
dotnet run -c Release --project benchmarks/ImprintSliverSweep -- detail  # one line per unclean run
dotnet run -c Release --project benchmarks/ImprintSliverSweep -- worst   # the large stage run (D about 2500) with a time ceiling
```

Unclean runs out of 48 (net10.0, 2026-11-24):

| shape, axis | origin | translated | negated | mixed | z only |
|---|---:|---:|---:|---:|---:|
| box z, master 4e81fa48a | 0 | 10 | 10 | 10 | 2 |
| cylinder z, master | 0 | 48 | 48 | 48 | 2 |
| cylinder x, master | 0 | 2 | 2 | 2 | 24 |
| box z, #1489 | 0 | 0 | 0 | 0 | 0 |
| cylinder z, #1489 | 0 | 0 | 0 | 0 | 0 |
| cylinder x, #1489 (neutral contractions only) | 0 | 2 | 2 | 2 | 0 |
| cylinder x, #1489 (and the stage's limit of 400) | 0 | 0 | 0 | 0 | 0 |

The two left in the translated frames were the evaluation limit at 6 ulps (23 slivers; 25 evaluations more than the limit of 50 gave, no neutral contraction involved: they are clean with the higher limit alone). Since the stage's limit is 400 per defect (the Boolean keeps 50) they are clean. Time of the
whole sweep, two runs each, on a loaded machine: limit 50 84 s and 99 s, limit 400 94 s and 107 s (the earlier measurement on an idle machine: 31 to 32 s for both). Worst case:
the limit is a ceiling; a stage that spends all of it is bounded by 10000 + 400 D evaluations against 10000 + 50 D (3.57 times at D = 116, tending to 8); no case of the sweep, also with
the neutral contractions switched off, spends it. A large case (48-slice cylinder, translated, along x, -20 ulps, D = 2534) ends by itself at 104 849 evaluations and 2.78 M pair tests (2.2 to 2.5 s) while the limit
grows from 136 700 to 1 023 600; the pair budget (5 M, not raised by the stage) bounds the evaluations that do pair tests to about 190 000, evaluations rejected by the cheap tests are bounded only by the limit (see IMPRINT_SLIVER_CLEANUP.md section 5).

**Worst-case time (`worst`).** The large stage run (48-slice cylinder, translated, along x, -20 ulps, about 2500 initial defects; the stage ends by itself) must stay below **15 s** on the
reference machine; the benchmark prints `CEILING EXCEEDED` if it does not. Measured: 2.2 to 2.7 s (2026-11-24, a loaded development machine). The 15 s is about the time of the whole
evaluation limit (10000 + 400 D evaluations at 21 microseconds, 20 s) rounded down: a run above it would mean that the stage spent more than the evaluations the measured case used by a factor of six.
The evaluations and pair tests of the run (104 849 and 2.78 M) are internal to the library and are not printed here (the benchmark uses the public API only).
