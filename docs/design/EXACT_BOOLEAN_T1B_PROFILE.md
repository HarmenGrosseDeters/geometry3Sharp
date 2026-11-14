# T1b: SafeRounding's edits in parallel, profiled and closed (2026-11-13)

T1b of the 3.0 pre-study (section 8): the SafeRounding edits in parallel over a fixed spatial partition, results
allowed to change (owner 2026-11-10), gated by a cohort run on the 16 239 target operations. Before building it, the
rounding was profiled on master 58644af. **Decision (coordinator 2026-11-13): T1b is closed as not worth its gate**;
the bit-identical work outside the edits is done instead (section 4). Re-open only if a later change makes the edit
loop a large share of the time (section 3).

## 1. Profile

`MeshRoundingRepair.Apply` split by temporary stopwatches (`runs/t1b-profile`, `runs/t1b/`: the patch, the probe
and the rows; no meshes). 4-core cloud container; 1 core with `taskset -c 0` and `DOTNET_PROCESSOR_COUNT=1`.
Sample: every 7th model of the D1 timing set (`runs/d1-cohort`, 400 models with ops over 1 s plus a stratum) by
rounding time, 56 models, 168 operations.

| part (sum over the 168 ops) | 1 core s | 4 cores s | share of the op at 4 cores |
|---|---|---|---|
| operation | 201.5 | 108.5 | 100 % |
| rounding (`Apply`) | 36.3 | 24.0 | 22 % |
| - the edit loop (`fix_degenerate`, `fix_intersection`: what T1b parallelizes) | 0.57 | 0.58 | 0.54 % |
| - pair searches (parallel already) | 18.6 | 7.8 | 7.2 % |
| - setup (anchors, boundary, components, finite and range scans, `atPosition`, exact triangles) | 5.8 | 5.9 | 5.4 % |
| - tree builds | 6.7 | 5.2 | 4.8 % |
| - degenerate scans | 1.9 | 1.8 | 1.7 % |
| - final (`remove_tiny_components`, false contacts) | 2.7 | 2.65 | 2.4 % |

The 9 models with the largest rounding times of the timing set (27 ops): edits 3.2 of 67.2 s at 4 cores (4.8 %).
The edit loop dominates the rounding only on inputs with thousands of edits (119829 union: 13 447 edits, 1.6 of 2.6 s;
1053875 union: 787 edits, 0.6 of 0.9 s). Most operations make a few edits (median 2, p90 27 in the timing set); the
rounding time follows the triangle count, not the edit count.

## 2. Upper bound (Amdahl)

The edit loop's share of the 1-core time is f = 0.28 % on the sample. With the edits perfectly parallel:
at most 0.4 % of today's 4-core time saved on 4 cores, at most 0.5 % on 16 cores; on the heavy tail at most 3.6 % /
4.5 %; on the single best operation (119829 union) at most 47 % / 59 %. Against that stand an L-tier change of the edit
order (results change) and a cohort run of hours as the gate. Not worth it now.

## 3. The design, recorded for a later re-opening

The edits are already propose-then-commit: a proposal (`begin_edit`, `removed` / `added`, `improves()`) reads the mesh,
only a commit writes it. That allows rounds over the sorted defect list (deterministic reservations: Blelloch, Fineman,
Gibbons, Shun, *Internally deterministic parallel algorithms can be fast*, PPoPP 2012; in this repository the same
pattern for Alpha Wrap, #774, `docs/design/ALPHA_WRAP_ROUNDS.md` on its branch):

- propose phase, parallel: each defect of the round runs its try sequence (`collapse_any`, flips and zips, strips,
  the move ladder) against the round-start state, with per-worker proposal state (`movedV`, `removed` / `added`,
  `scratch`, the near-query and pair caches), and records its read set (the triangles of its `near()` results and the
  one-rings it read) and its first accepted edit;
- commit phase, serial in defect order: an edit is committed if its read set meets no triangle or vertex written
  earlier in the round, else the defect is proposed again serially at its place; ids, ghosts, `atPosition`, the
  counters, `MaxDrift` and `SmallestRejectedDrift` change only here, and the evaluation budget is charged in commit
  order, so `EvaluationLimitReached` is deterministic;
- the result depends on the round-start state and the order only, not on the thread count. Conflicts are found by the
  read sets, so no fixed spatial partition is needed; a grid with colour classes is the alternative, with more conflicts
  at cell borders.

Results change against today's serial loop (a defect sees the round-start state, not its predecessors' edits), so the
owner's conditions of 2026-11-10 hold: not worse on the cohort, deterministic over thread counts and runs.

## 4. Done instead: the work outside the edits, bit-identical

- `remove_tiny_components`: its own depth-first search over the triangles (`connected_components`, the order of
  `MeshConnectedComponents.FindConnectedT` and each component in its order, so the area and volume sums are the same)
  and marks instead of hash sets.
- The per-triangle scans over blocks of 4096 ids in parallel: the anchors, the finite and range checks, the degenerate
  triangles and the tree's boxes. Each block writes its own ids' slots or its own entry; the blocks' flags and largest
  magnitudes are combined serially (a largest magnitude is exact in any order): no floating-point reduction across
  threads.
- `atPosition` sized for the vertices (no rehash; the capacity does not change the order of its entries, which
  `false_contacts` enumerates).
- Not changed: `component_scales` (a union-find in `MeshRepairMetrics.ComponentBoxes`), the exact triangles at the start
  (they call the caller's `ExactVertexF`, which need not be thread-safe), the dictionary's insertion order.

Numbers ([EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md), "SafeRounding's work outside the
edits"): on the 195 operations of the profile, `DOTNET_TieredPGO=0` on both sides, rounding 38.2 -> 32.2 s (−15.7 %),
whole boolean −4.7 %, hashes equal.
