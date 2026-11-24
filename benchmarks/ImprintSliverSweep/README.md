# ImprintSliverSweep

Sweep of `ExactMeshImprint` on ulp-shifted near-duplicates (issue #1489, [IMPRINT_SLIVER_CLEANUP.md](../../docs/design/IMPRINT_SLIVER_CLEANUP.md) section 5).
A rotated box (literal corners) and a capped cone frustum (snapped to 2^-24), both libm-free, are shifted by 1 to 12 ulps in both directions along one axis and cut into
each other both ways: 48 runs per cell. A run is unclean if `Compute` fails, `MeshRobustRepair.NotCleanReason` is not empty or `RoundingClean` is false. The frames: the
origin, the translated frame of the test harness (`Similarity.Utm`, far from the origin), its negation, mixed signs, and only z translated. Public API only.

```
dotnet run -c Release --project benchmarks/ImprintSliverSweep            # the table (about 30 s)
dotnet run -c Release --project benchmarks/ImprintSliverSweep -- stats   # with the repair's mean work per cell: initial degenerate / intersections / edits / runs with the final stage
dotnet run -c Release --project benchmarks/ImprintSliverSweep -- detail  # one line per unclean run
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

The two left in the translated frames were the evaluation limit at 6 ulps (23 slivers). Since the stage's limit is 400 per defect (the Boolean keeps 50) they are clean. Time of the
whole sweep, two runs each, on a loaded machine: limit 50 84 s and 99 s, limit 400 94 s and 107 s (the earlier measurement on an idle machine: 31 to 32 s for both). Worst case:
the limit is a ceiling; a stage that spends all of it is bounded by 10000 + 400 D evaluations against 10000 + 50 D (see IMPRINT_SLIVER_CLEANUP.md section 5); no case of the sweep, also with
the neutral contractions switched off, spends it.
