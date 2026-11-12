# Alpha Wrap 3.0, phase 1: profile, upper bounds, lever ranking (measure only)

Date 2026-11-12. No library change on master. The measurement build is the commit on this branch (instrumentation
only: per-phase snapshots of the wrap's tick counters, PartitionedReduction region timings, `wrappipe` passing the
wrap's input as `KeepClearOf` as the library does). M = measured bound, E = estimate.

## Setup

- Inputs: the 189 meshes over budget in the final 2.0 800 run (f800, runs/alpha-wrap-2) and 80 of the other 611
  (random, seed 1); Thingi10K sample800 seed 2611, fetched at run time. `BENCH_REPAIR=wrappipe`, 300/3000, warm
  (soup), quality pass off, `BENCH_FIDELITY=hd`; one process at a time, nothing else on the 4-core container.
  Commands: `drive.sh list_over.txt prof_over.jsonl`, `drive.sh list_rest80.txt prof_rest80.jsonl`;
  analysis `analyze_aw3.py`, `scenarios_aw3.py` (this branch, `aw3/`).
- Calibration (`cal.sh`, 16 meshes, instrumented and clean master alternating): clean master (2947179) gives the
  f800 output hash on 16/16 (master is bit-identical to the 2.0 final), and runs 1.136x slower here than the f800
  machine (median). The instrumented build costs +3.4 % (wrap +3.1 %, simplify +4.3 %; mostly `KeepClearOf`, the
  contact fix, which the `wrappipe` bench left out: **the f800 timings miss the contact fix's ~3 %**; a bench fix is
  PR 0 below). Pass counts below are on f800-machine-equivalent times (T / 1.136). The budget is an absolute time,
  so pass counts move with the machine: the acceptance run must be on the reference machine.

## Profile (205 meshes over budget on this machine, sum 2792 s; shares of pipeline time)

The 2.0 split overlapped: `wrap.intersection / touch / insert` were cumulative over the carving and the pinch and
quality passes. Separated:

| part | share | lever |
|---|---:|---|
| intersection oracle (`FirstEntry`, rule 1) | 15.5 % | 1, 3 |
| touch test (`TetrahedronTouches` + vertex distances) | 12.1 % | 1, 3 |
| rule-2 projection | 0.7 % | 1, 3 |
| Delaunay insertion (`Delaunay3.Insert`) | 12.2 % | 2 |
| gate pop, empty-ball recheck, dual edge | 2.9 % | 3 |
| carving bookkeeping (labels, gate pushes with facet radii, timer overhead) | 9.2 % | (3) |
| **pinch passes: manifold-step trials** | **15.8 %** | 4 |
| quality-refinement point search, setup, manifold, extract | 4.4 % | - |
| reduce: parallel regions (wall) | 13.3 % | 5 |
| reduce: serial parts (setup, stitch, last pass) | 5.5 % | 5 |
| self-intersection check (`NotCleanReason`) | 1.3 % | - |
| certification (Hausdorff, patch bound) | 6.5 % | 3b |

Within budget (64 meshes) the shares are similar; oracle + touch are smaller (17 %), reduce larger (26 %).

Counts (median over 269): 1.76 M gates popped, 42 % of them reach the oracle (the rest are stale and cheap);
2.9 µs per oracle + touch test; 75k Steiner points at 14.4 µs per insertion; 459k cells.
Pinch passes: median 4 pinch points, 32 of 189 meshes have none, yet each pass runs at least one **full** trial of the
manifold step (pass 1: 9.0 %, pass 2: 6.9 %). About 3 full trials per mesh, of which only the last is needed (its
labels replace `make_manifold`, `manifold_reused` = 1). `local_trials_missed` = 0 on all 269: the confirming full
trial never found a spot the local trial missed.

## Upper bounds per lever (189 over budget; 800 estimate = pass over + others' pass rate × 611)

| lever | scenario | 189: pass | T/B median | speedup on the 189 | 800 within budget |
|---|---|---:|---:|---:|---:|
| - | today (master + contact fix) | 25 | 1.09 | 1.00x | 621 (78 %) |
| 1 oracle + touch | M phase costs 0 | 186 | 0.79 | 1.40x | 797 |
| 1 | E serial 1.3x (prefilters, per-facet caching) | 71 | 1.02 | 1.07x | 674 |
| 2 insertion | M costs 0 | 124 | 0.96 | 1.14x | 735 |
| 2 | E 1.25x per insertion | 44 | 1.07 | 1.02x | 647 |
| 3 gate tests | M perfect on 4 cores | 182 | 0.85 | 1.31x | 793 |
| 3 | E 2.5x on 4 cores | 167 | 0.89 | 1.23x | 778 |
| 3b certification | M perfect on 4 cores | 69 | 1.03 | 1.05x | 672 |
| 3b | E 3x | 63 | 1.04 | 1.04x | 666 |
| 4 pinch trials | M cost 0 | 139 | 0.93 | 1.19x | 750 |
| 4 | E 2 of 3 full trials gone | 105 | 0.98 | 1.12x | 716 |
| 5 reduce | M cost 0 | 158 | 0.88 | 1.23x | 769 |
| 5 | M serial parts perfect on 4 cores (regions already parallel) | 55 | 1.04 | 1.04x | 658 |
| 1+3+3b | E stacked | 184 | 0.81 | 1.35x | 795 |
| 1+3+3b+4 | E | 188 | 0.70 | 1.57x | 799 |
| 1-4 | M all perfect on 4 cores, insertion serial (Amdahl) | 188 | 0.66 | 1.67x | 799 |

Most over-budget meshes are marginal (T/B median 1.09), so pass counts jump with small gains; the tail (T/B 1.3-1.7:
1514905, 48367, 69259, 78968, 40118, 860049, 500097, 61198) needs 1.3-1.7x. 48367 is 46 % touch test.

## Findings that change the lever list

- Lever 2: BRIO / Hilbert order does not apply. The alpha wrap inserts its Steiner points online, each from the
  current carving state, so there is no batch to sort. `Delaunay3` already has a flat int layout and reused buffers;
  what is left is per insertion (a `Dictionary` for the edge links, an exact `Orient3d` per cavity boundary facet).
  Upper bound 1.14x, E 1.02x: last.
- Lever 4 is mostly the full manifold-step trials, not the carving of the pinch passes. Dropping the redundant full
  trials (pass 1's confirmation when the quality refinement and pass 2 follow; the initial full trial, if non-manifold
  candidates are tracked locally during carving) is the cheapest large gain.
- Lever 3 can be bit-identical: the gate tests (oracle, touch, projection) are pure functions of the cell, its
  neighbour (cell stamps) and the static input. Evaluate the next batch of gates in parallel, apply them serially in
  queue order, and evaluate again any gate whose cells changed (the stamp check that is already there). Then serial =
  parallel = repeated, and 2^k scaling holds as it does today. Certification: the Hausdorff bound per triangle in
  parallel, max-reduced (order-independent, exact), also bit-identical.
- Lever 5 belongs to Reducer / Remesher 2.0 (coordinator rule). Its regions already run in parallel; the serial parts
  are 5.5 %. Not proposed here.

## Proposal (ranked, one PR each)

| # | PR | expected (E) | bit-identical | notes |
|---|---|---|---|---|
| 0 | bench: `wrappipe` passes the input as `KeepClearOf`; per-phase split (no shared tick counters) | - | yes (bench) | needs the counters in MeshAlphaWrap as internal `PhaseMilliseconds` keys, no behaviour change |
| 1 | L3: parallel gate-test batches with serial apply in queue order | 1.2-1.3x on the 189 | yes (proof: pure tests + stamps) | Opus review of the determinism; batch size fixed, independent of the core count |
| 2 | L4: drop the redundant full manifold trials | 1.1x | likely not guaranteed; identical on 269/269 if the missed count stays 0 | guarantee kept by the final manifold step |
| 3 | L1: oracle and touch prefilters / per-facet caching (#448 batch API where the queries batch) | 1.05-1.1x | yes if the filters are certified (exact fallback) | partly overlaps with 1 once the tests are parallel |
| 4 | L3b: parallel certification | 1.04x | yes | small; can go with 1 |
| 5 | L2: insertion micro-optimisations | 1.02x | yes | last, only if the budget is still missed |
| - | L5: reduce | - | - | coordinator / Reducer 2.0 |

Combined E (1+2+3+4): about 1.55x on the 189, about 99 % of the 800 within budget (f800-machine equivalent).

## Proposed targets (for the owner)

- (a) >= 95 % of the 800 within budget on the reference machine (today 76 % in f800); stretch 98 %.
- (b) 4-core speedup of the wrap pipeline on the 189: >= 1.4x against master (time sum), measured with the 800
  harness alternating with master on the runner.
- (c) unchanged: guarantees 800/800, the 2.0 angle floors and fidelity, deterministic (serial = parallel = repeated,
  2^k scaling bit-identical); PRs 1, 3 and 4 bit-identical to master's output, PR 2 identical on the 800 or the
  difference reported.

## Lever 6 (sharp features) on top of DC A+B

Re-measured on master 2947179 (DC options A and B in), `benchmarks/SharpFeatureProbe -- A,B,C3`, the 20 T1 meshes
and the 6 procedural solids (raw rows `lever6.jsonl`; times on this machine, compare within the run):

| T1 (20) | tris (A = 1) | time | clean | angle min | <0.5° / >170° | vertex distance min / offset | edge Chamfer vs A | crease normal vs A |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| A (pipeline) | 1 | 5.7 s | 20/20 | 4.67° | 0 / 0 | 0.35 | 1 | 1 |
| B (DC on the winding number) | 0.74 | 18.7 s (3.3x) | **20/20 (before 19/20)** | 0.097° | 67 / 17870 | 0 (on the input) | 0.22x | 0.22x |
| C3 (QEF post-pass on the raw wrap) | 0.84 | 8.0 s (1.4x) | 20/20 | 4.67° | 0 / 0 | 0.4 | 0.86x | 0.56x |

DC A+B removes B's one self-intersecting result; C3 does not use DC and is unchanged (as in #498). B's blockers stay:
no offset (it lies on the input, so "encloses the input" and the distance band fail), and the angles (17870 triangles
above 170°). Recommendation: **not now**. Lever 6 is a quality lever that costs time (C3 +40 %, B 3.3x), the opposite of
3.0's target. C3 is the buildable variant (every guarantee and angle floor kept), as an opt-in after the speed levers,
if the owner wants sharper creases; B belongs to the DC / tet-meshing track (it needs an offset and a remesh for the
angles before it could replace the wrap).

## Prior art

- Portaneri, Rouxel-Labbé, Hemmer, Cohen-Steiner, Alliez, "Alpha Wrapping with an Offset", ACM TOG 41(4) 2022
  (serial algorithm; paper only, never CGAL code).
- Blelloch, Fineman, Gibbons, Shun, "Internally deterministic parallel algorithms can be fast", PPoPP 2012
  (deterministic reservations: evaluate in parallel, commit in a fixed order; the pattern of lever 3).
- Spielman, Teng, Üngör, parallel Delaunay refinement (independent sets of points per round): the alternative to
  lever 3's serial apply; not proposed (parallel insertion is not bit-identical without more machinery).
- Amenta, Choi, Rote, "Incremental constructions con BRIO", SoCG 2003: needs a known point set, does not apply to the
  online refinement (lever 2).
- Marot, Pellerin, Remacle, "One machine, one minute, three billion tetrahedra", IJNME 2019 (compact parallel Delaunay;
  batch insertion, same limit).
- benchmarks/BatchQueryBenchmark (#448): sorted serial batches 1.0-1.6x, parallel 2.5-5.7x per query, bit-identical.
