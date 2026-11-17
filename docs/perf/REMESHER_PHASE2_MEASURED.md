# Remesher Phase 2 open items, measured (2026-11-17)

Measurement first, as the owner asked (2026-11-17): degenerate triangles, self-intersections and folded walls in the results of
`Remesher` with the `g3cli remesh` settings (20 passes, smoothing 0.5, projection onto the input, boundary and 30 degree
creases kept), by default and with each opt-in (`MinTriangleQuality` 0.2, `PreventSelfIntersections`, both), and what each
costs. **No library default is changed; the recommendation at the end is for the owner.**

## Setup

- Tool: `benchmarks/RemeshQualityBenchmark` (this PR adds `sweep=1`, see below). Classification: `MeshRepairMetrics.SelfIntersections`
  (pairs), triangle quality q < 0.1 "poor" and q < 1e-3 "flat", `MeshPostRepairCleanup.FindInvertedComponents`,
  `MeshRobustRepair.NotCleanReasons`, volume ratio to the input.
- Machine: Linux container, 4 cores, net10.0, Release, `runs=1`, library master `03a3cd26`; the remeshing is deterministic.
  Timings of one run (noise about 10 %); the "default" time of the same set was 4.3 / 4.5 / 4.6 / 5.0 s in four runs.
- Inputs: the 150 `primitives`, 18 `gears`, 24 thin-walled `tubes` (procedural) and the 20 closed, manifold Thingi10K meshes of
  `benchmarks/RemeshBenchmark` (fetched at run time by `fetch_data.py`, nothing committed; a local run here: the
  fetch worked from this container, so no new workflow was needed). Thingi10K 135363 with the sizing (1.1M triangles) is its own run, below.
- Commands: `qualities=0.2` (q), `qualities=0,0.2 si=1` (si and q+si), `si=1 si_from=<k>`, all with `fidelity=0`
  and `thingi=benchmarks/RemeshBenchmark/data/meshes`.

## Result 1: how many results are defective, and what the opt-ins cost

Sums over the group; "cases" = results with the defect. Pairs = self-intersecting triangle pairs. Time = sum of the group, seconds.

| group (cases) | | default | q 0.2 | si | q 0.2 + si |
|---|---|---:|---:|---:|---:|
| **Thingi10K (20)** | pairs (cases) | 19768 (19) | 2079 (15) | 1040 (4) | 1108 (5) |
| | flat q<1e-3 (cases) | 1536 (17) | 195 (7) | 2050 (17) | 284 (9) |
| | poor q<0.1 | 5750 | 1641 | 6488 | 1894 |
| | inside-out | 0 | 0 | 0 | 0 |
| | time s (x default) | 4.3 | 8.1 (1.9x) | 179.4 (**42x**) | 82.8 (**19x**, 10x of q) |
| **primitives (150)** | pairs (cases) | 409 (35) | 129 (2) | 0 | 0 |
| | flat (cases) | 38 (22) | 0 | 3 (3) | 0 |
| | time s | 2.5 | 4.2 | 29.4 (12x) | 29.7 (12x) |
| **gears (18)** | pairs (cases) | 7523 (17) | 0 | 0 | 0 |
| | time s | 0.2 | 0.3 | 2.2 | 1.7 |
| **tubes (24, ill-posed)** | pairs (cases) | 5969 (20) | 5100 (23) | 0 | 0 |
| | mean Hausdorff / target | 0.24 | 0.30 | 0.93 | 1.00 |
| | time s | 0.2 | 0.2 | 1.6 | 1.6 |

- **Degenerate triangles.** Default: flat triangles in 17 of 20 Thingi10K results and in 22 of 150 primitives. `MinTriangleQuality` removes
  almost all (195 left in 7 cases; the rest sit at features and in the coarse parts); `PreventSelfIntersections` alone makes them *more*
  (2050) because a blocked operation leaves the sliver.
- **Self-intersections.** Default: 19 of 20 Thingi10K results. With `si`, no result has a pair that the input did not have: the 1040 /
  1108 left are all in the five inputs that arrive with pairs (input pairs: 63709: 1298, 1053376: 148, 237642: 32, 242225: 32,
  39460: 12; results with `si` / `q` + `si`: 971 / 1037, 0 / 7, 28 / 28, 32 / 32, 9 / 4: the option blocks the operations near
  the pairs, removes some by accident and does not repair them). `q` alone leaves 2079 pairs in 15 results.
- **Cost of `si`.** 42x the default on Thingi10K (10x on top of `q`, which already removes most of the work that `si` blocks), 12x on
  the primitives. Cheaper variants with the existing `si_from=<k>` (the check only from pass k, `q` 0.2, Thingi10K):
  k = 10: 17.0 s (3.7x default), 1723 pairs in 14 cases; k = 15: 13.3 s (2.9x), 1451 pairs in 13 cases. They are cheaper and
  not a guarantee (the pairs made in the unchecked passes stay), so they are a middle level, not a safe default.
- **Folded walls** (`tubes`, targets 5-32 times the wall; Thingi10K 135363 with target 11.6 on 0.75 walls). Default and `q`: pairs
  in 20 / 23 of 24 tubes, 135363: 564 pairs. `si` gives 0 pairs by blocking the folds: the mean Hausdorff distance rises from 0.24
  to 0.93-1.00 target (volume 0.54-4.77 of the input): the wall is not folded and not kept. Only the sizing keeps the wall
  (volume 0.992-1.000, [README](../../benchmarks/RemeshQualityBenchmark/README.md#thin-walls-thickness-aware-sizing-opt-in)).
- **135363 at full size** (the mesh as fetched, 2612 triangles; `RemeshSizing.FromWallThickness(input, 1)`, `q` 0.2, no `si`,
  this run): 1,159,814 triangles in 321.5 s, volume ratio 1.0008, Hausdorff 0.041 target, **17,916 pairs**, 400,598 poor and
  174,753 flat triangles, not clean (degenerate triangles, self-intersections). Without the sizing: 502 triangles, volume 0.055,
  180 pairs. The sizing keeps the wall and still leaves 18k pairs.
  The roadmap's earlier figure for this case with `si` (more than 90 minutes) was not repeated here; the unguarded result is not clean.

## Result 2: where the cost of `si` is, and whether existing blocks cut it

Not re-profiled here; the existing measurements stand ([REDUCER_REMESHER_20.md](../design/REDUCER_REMESHER_20.md) 12.4 and 13,
[RemeshPreventBenchmark](../../benchmarks/RemeshPreventBenchmark/README.md)): about 8M pair tests per 18k-triangle mesh, 92 % decided
by `TriangleTriangleFilter`, about 1 % reach the exact stage; the cost is the number of neighbour triangles visited and the filter's
`Orient2d` calls on ring triangles that share the collapse vertex; the grid query is 12 % after the cell size fix. Every cheap
lever there was measured and closed (coplanar shortcut, clearance filter, plane shortcut first 0.93-0.95x, refit tree 2.5 %, ...).
New here: **a continuous sweep is not a cheaper check.** `sweep=1` runs `MeshSweepCollision` over the motion of each buffered
smoothing / projection pass (the same 800 passes of the 20 Thingi10K meshes, `q` 0.2 + `si`): 30.8M candidate pairs, **329 s** of
sweep against **84 s** for the whole remesh with its static checks, so in this implementation (an after-the-fact sweep of every move pass, not an optimised pass check) a sweep per pass costs 4x the entire guarded run (a run of the 135363 sizing case with the sweep had not finished its first case after 46 minutes and was stopped: not measured; the
sweep numbers below are the 20 Thingi10K meshes only). The edge operations (collapse, flip, split) have
no motion to sweep: they are discrete local changes whose new triangles are tested against the grid, which CCD does not replace.
Existing blocks that remain candidates are not new: slabs for the Remesher's edge operations (<= 2.1x on 4 cores, large meshes
only, a big build, [roadmap](../roadmap/alpha-wrap-and-repair.md)).

## Result 3: can a move pass through a wall and end in a state the static check accepts?

Yes, measurably but rarely. With `sweep=1` every buffered move pass is checked after the fact: the vertices go linearly from the
old to the new positions, status of `MeshSweepCollision`, counted only when the pass starts and ends without a self-intersection
(`MeshRepairMetrics`, so the static check has accepted it):

| run (800 move passes) | start state touching (ToI 0) | contact in the middle of the step | contact at the end (ToI > 0.9999) |
|---|---:|---:|---:|
| Thingi10K, `q` 0.2, no `si` | 0 | 4 | 0 |
| Thingi10K, `q` 0.2 + `si` | 65 | 32 | 16 |
| tubes (690 passes), `q` 0.2 + `si` | 0 | 1 | 0 |

(Bucketed from the stderr lines of the run by the earliest time of impact; any sweep status other than `NoCollision` counts, the
`IterationLimit` ones too. The 4 passes of the row without `si` come from a run in which 605 of 800 passes collide, mostly from inputs
that already have pairs.)

A "contact in the middle" is a vertex-triangle or edge-edge pair whose paths meet before the end while the end state is clean: a
move through a wall, or a touch the conservative CCD cannot rule out (the status says "may touch"; I did not classify them one by
one). So up to 32 of 800 (4 %) guarded passes are flagged with such a motion (unclassified: an upper bound on real crossings), which the static end check cannot see. The results are still free of
new pairs, so no harm to the output showed; a stricter guarantee against tunnelling would need the sweep (4x cost, above) or a
smaller step. For the hotspots question: the sweep flags up to 1-4 % of the move passes and no real crossing was verified; no defective result
followed in this sample. Whether a move can truly pass a wall and end accepted stays open until the flagged contacts are classified
(the next step if the owner wants it: print the pair, test whether the vertex ends on the other side of the triangle's plane).

## Recommendation (owner decision; no default changed)

1. **What a safe-by-default remesher costs today:** `MinTriangleQuality` 0.2 + `PreventSelfIntersections` is the only combination
   with no new pairs and almost no flat triangles (primitives and gears 0, Thingi10K 284 flat in 9 results), at 10x the time of
   `q` alone and 19x the plain default on Thingi10K (12x on the primitives). `q` alone is 1.9x and removes 90 % of the pairs and 87 % of
   the flat triangles but leaves 2079 pairs in 15 of 20 results. Defaults stay: the owner kept upstream's.
2. **The lever to build is not CCD and not another filter** (a sweep per move pass 4x in this implementation; the filter levers 0.93-0.95x). The two things that remove
   work are (a) the `q` option first (10x of the `si` cost is operations that `q` already prevents), so a "safe" preset should be
   `q` 0.2 + `si`, not `si` alone (42x -> 19x); (b) a middle level, `si` from pass ~15 (2.9x, 1451 pairs against 1108 with the full check),
   already available as `si_from` in the benchmark but not a public option: a `PreventSelfIntersectionsFromPass` would give the
   caller a documented 3x step before the 19x one. Needs a decision before it is built (a new public option, results change).
3. **Folded walls** are an input problem (edge length far above the wall): neither `q` nor `si` keeps the wall (`si` blocks the fold and
   stays 1.0 target off); the working lever is the sizing, and it needs the size of 1.1M triangles for 135363 (322 s, 18k pairs left;
   the roadmap's earlier figure with `si` is more than 90 minutes, not repeated here). A preset that applies `RemeshSizing.FromWallThickness` when the target exceeds about 4 times the wall
   (the `g3cli` warning threshold) and switches `si` on only below a triangle budget is the practical safe path; the
   costly case (135363 at 1.1M) should be refused or coarsened by a budget as the alpha wrap does (`MaxEstimatedTriangles`).
4. Slabs for the edge operations (<= 2.1x) stay the only speed lever of `si` that is left; not recommended for this item.

## What this PR adds

`RemeshQualityBenchmark sweep=1`: after each buffered move pass the motion is checked with `MeshSweepCollision` (outside the
reported time; counters `SweepPasses`, `SweepCollisions`, `SweepTunnelled`, `SweepCandidates`, `SweepSeconds`,
`SweepComputeSeconds` in the JSON, one stderr line per tunnelled pass). Library unchanged.
