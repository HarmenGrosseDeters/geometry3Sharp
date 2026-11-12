# Alpha Wrap 3.0 performance study: worst cases and normal cases (2026-11-12)

Measurement only. No library change, no PR. Probe patches are bench-only and local (`bench_probe.patch`,
`StudyRepair.cs.txt` in this folder, never pushed as code).

**Commit set of every table unless a row says otherwise:** master 1230482 (contains the merged AW3 PRs #651 phase keys,
#670 L4, #681 L1; the AW3 session has no open lever branch). Repair cases additionally with #703
(`claude/636-tiny-repair-time`, head 849223c, merged locally as 3f6a787). The (B) numbers are the AW3 session's
(`runs/alpha-wrap-3`, `aw3/B/`, master 14a27e45 + `rounds_probe.patch`).

**Setup.** `benchmarks/MeshRepairBenchmark`, `BENCH_REPAIR=wrappipe` (alpha = diag / 300, offset = diag / 3000, warm-up
soup, quality pass off), one process at a time on a 4-core cloud container (Xeon 2.3 GHz, 15 GB, .NET 10.0.115,
workstation concurrent GC, THP = madvise). Thingi10K sample800 seed 2611, fetched at run time. T = `pipe_ms` (wrap +
simplification + certification). Run-to-run drift of this container: a rerun of the same baseline gave 0.947 (8 tail
meshes) and 1.083 (10 normal meshes) of the first run, so differences below about 5-8 % between separate runs are noise.

**Corpus.** Tail: the 24 slowest of the AW3 session's 800 run (all 17 above 20 s plus 7), `list_tail.txt`. Normal: 30
random meshes between the 40th and 60th percentile of T (seed 1211), `list_normal.txt`. Repair: #692, #693, #636, #712.

**Methods.** Phase split from the `PhaseMilliseconds` keys (#651). Thread and category split from `perf record -e
cpu-clock -F 499 -g` with JIT symbol maps (`DOTNET_PerfMapEnabled=1`, `DOTNET_EnableWriteXorExecute=0`) and the
standalone GC library (`DOTNET_GCName=libclrgc.so`, so GC samples have their own module), samples restricted to the
measured pipeline window; output hashes identical to the plain run. Serial share = main-thread samples outside parallel
regions; parallel work = worker samples plus main-thread samples inside them. `DOTNET_PROCESSOR_COUNT=1` is not a
serial reference (the reduction still used 3 cores of CPU; small meshes became 1.5x slower in the wrap), so the
`p1_*.jsonl` rows are kept only as a record; `taskset -c 0` (`serial_*.jsonl`) gives the real one-core time: 1.55x
(tail 8), 2.27x (normal 10) of the 4-core time.

## 1. Summary

1. **The tail is size, and size makes every wrap operation dearer.** Per raw triangle the tail does the same number of
   operations as the median (Steiner points 0.50 / 0.50, gates 11.9 / 11.0, cells 3.13 / 3.01), but each costs more:
   over the 800 rows, from 50-100k to above 700k raw triangles, an insertion goes 9.5 -> 17.1 us, the oracle per popped
   gate 0.18 -> 0.62 us, the touch test 0.28 -> 0.50, gate bookkeeping 0.42 -> 0.66, trials per cell 0.83 -> 2.06 (1 -> 3
   local trials). The simplification does not grow (10.6 -> 10.0 us per raw triangle), the certification falls
   (5.2 -> 2.7). That is the 1.4x per raw triangle of the tail (51.1 against 36.1 us here). Cause of the growth: not
   the TLB (THP = always: no change, M), not GC (below); memory access over a 2.5 GB working set is the remaining
   candidate, unproven. Bound if the unit costs stayed at the 50-100k level: 1.48x on the tail (E).
2. **80 % of the tail's wall time is serial** (540 s of 672 s; the wrap). The parallel parts (reduction rounds, check,
   certification) are efficient: 470 CPU-s in 132 s wall, 3.6 of 4 cores. Today's structure on 16 cores: 69.0 -> 60.0 s
   for 40118, tail sum 1.21x.
3. **Exact arithmetic is not a cost.** Stages beyond double: 0.06 % of CPU. The double filters and the triangle-triangle
   filter: 9.9 %. BVH (input oracle, AABB trees) 16.1 %, hash grid 9.9 %, GC 3.2 % (pause 2.4 % of wall), other
   allocation cost 3.3 %, rounding 0, rest 57.6 % (the wrap's own loops 21 %, Reducer 11 %, Delaunay3 9 %, DMesh3 8 %).
4. **GC and allocation are not a lever** (M): Server GC, no concurrent GC, gen0 = 256 MB give 0.96 / 1.02 / 0.99 against
   the baseline rerun on the tail and nothing on the normal stratum.
5. **Levers that cut the tail** (24 meshes, max / sum; today 69.0 s / 680 s on 4 cores):
   a size budget of 300k raw triangles (M) 19.4 s / 338 s, no mesh above 20 s, at the price of closed cavities on
   lattices; (B) round-based carving alone (E from the AW3 ceilings) 40.2 s / 426 s on 4 cores, 24.2 s / 232 s on 16;
   (B) plus the trial passes halved 35.3 / 379 and 17.3 / 185; budget plus (B) 13.1 / 217 and 8.1 / 118.
   On 4 cores (B) alone leaves 8 meshes above 20 s. "90 s -> 10 s" needs 16 cores and (B) plus the rest phases, or the
   budget.
6. **Repair cases:** #703 removes #636 and #712 (6.9 -> 0.34 s, 3.4 -> 0.16 s, 42.2 -> 1.3 s). #692 is the same size
   effect below the budget (185k raw triangles from 144 input triangles, 11.6 s per Apply). **#693 is not a wrap case:**
   97.8 % of its 50 s is the fast winding number of the volume checks evaluating exact solid angles (with the far part
   the far-field cache is evidently not used; why, in the tree or its criterion, is not diagnosed); the same scene with
   the part at 1e3 takes 0.78 s.

## 2. Ranked levers

Speedups are of the summed time. M = measured (a run, or a phase's measured time set to zero), E = estimate / model.

### Tail (24 slowest; today max 69.0 s, sum 680 s, 18 above 20 s on this container)

| # | lever | 4 cores: max / sum / above 20 s | 16 cores: max / sum / above 20 s | speedup 4c / 16c | kind | cost / risk |
|---|---|---|---|---|---|---|
| 1 | size budget, raw <= 300k (alpha from area, certified) + (B) | 13.1 s / 217 s / 0 | 8.1 s / 118 s / 0 | 3.13x / 5.77x | M + E | both below |
| 2 | size budget, raw <= 300k | 19.4 s / 338 s / 0 | 16.1 s / 274 s / 0 | 2.01x / 2.48x | M | fidelity on lattices: wrap -> input distance 40118 0.46 -> 20.7 % diag, 860049 0.95 -> 2.50, 69261 0.82 -> 2.26, 111959 0.81 -> 1.62; other 19 < +0.4; input -> wrap +0.05-0.25 % diag (inside alpha + offset). 300k raw is about 15 s here: a 20 s promise needs the budget at 300k, not higher. The 1 / alpha^2 model hit 294-330k on 19 of 23; it breaks when cavities close (40118: 51k) |
| 3 | (B) + every rest phase parallel (floor of the parallel route) | 28.8 s / 302 s / 2 | 7.4 s / 77 s / 0 | 2.25x / 8.84x | E | (B) plus parallel pinch passes, quality refinement, reduction setup / stitch; not master's output |
| 4 | (B) + trial passes free (bound) / halved | 30.3 s / 332 s / 2; 35.3 s / 379 s / 4 | 11.7 s / 138 s / 0; 17.3 s / 185 s / 0 | 2.05x / 4.92x; 1.79x / 3.68x | E | the trials are whole-triangulation passes (remove_pockets, make_manifold, build_stars: 18 % of the serial time); incremental labels would be bit-identical |
| 5 | (B) alone, main carve in rounds (W 256: 3.94x / 14.9x, +12 % re-evaluation) | 40.2 s / 426 s / 8 | 24.2 s / 232 s / 2 | 1.60x / 2.94x | E (AW3 probe M for the ceilings) | not master's output (order-free guarantees carry over, min angle order-sensitive); needs a parallel-safe Delaunay insertion |
| 6 | unit costs at the 50-100k level (locality: cell / vertex layout, queue) | 39.9 s / 460 s | - | 1.48x | E | cause unproven (THP ruled out); a probe would be a compacting / spatially ordered Delaunay3 layout |
| 7 | pinch / manifold trials free, serial | 59.0 s / 587 s | - | 1.16x | M | upper bound of "L4b" (local / incremental trials); real gain about half |
| 8 | oracle + touch free (rest of L1) | 48.0 s / 523 s | - | 1.30x | M | L1 took the cheap part; no further idea measured |
| 9 | reduction's serial parts free | 67.4 s / 650 s | - | 1.05x | M | 15-17 % on 679615 / 49901 (large flat faces) |
| 10 | Server GC / gen0 size / no concurrent GC | - | - | <= 1.04x | M | a host setting, not a library lever; noise level |
| 11 | THP = always | - | - | 1.00x | M | none |
| - | alpha relative to the largest component (the #693 idea) | 69261 37 -> ~58 s, 49901 24 -> ~53 s, 49998 23 -> ~52 s | - | slower | E | raw grows by (diag_all / diag_largest)^2 = 1.56-2.29x on 3 of 24; 19 are single components. A fidelity lever; only with the budget |

Reading for the owner's decision: on the reference hardware (4 cores) only the size budget brings the tail under 20 s;
(B) alone gets 40118 to 40 s. On 16 cores (B) needs the rest phases (trials first) to reach about 10 s. Budget and (B)
multiply, and the budget also shrinks what (B) has to parallelize. The budget changes the result (coarser on high
area / diag^2 inputs), (B) changes the output bits but not the resolution.

### Normal stratum (30 meshes at the median; T median 4.9 s, sum 145 s)

| # | lever | speedup (sum) | kind | note |
|---|---|---|---|---|
| 1 | (B) on 4 cores (main carve 45 % of T, 3.94x, +12 %) | about 1.5x | E | same model as the tail; small meshes have fewer gates per round, not measured here |
| 2 | reduction rounds free / whole simplification free | 1.28x / 1.71x | M | the simplification is 41 % of T at the median (28 % on the tail); it is already 3.6 cores wide, so the lever is serial cost per collapse (hash grid 7.9 % of CPU, tri-tri filters) |
| 3 | oracle + touch free | 1.22x | M | |
| 4 | insertion free | 1.17x | M | AW3's L2 probe: <= 1.02x reachable |
| 5 | gate bookkeeping free | 1.15x | M | |
| 6 | certification free | 1.13x | M | AW3's L3b: already parallel, <= 1.02x reachable |
| 7 | trials free | 1.09x | M | median mesh has 0 pinch points, 1 local trial |
| 8 | JIT / tiering inside the measured run | <= 1.07x | M (6.9 % of CPU samples in libclrjit) | warm-up soup does not cover everything; a host matter (ReadyToRun / tiered PGO), not a library lever |
| 9 | GC settings | 1.00x | M | |
| - | size budget | 1.00x | M | no normal mesh is above 300k raw |

The normal case has no single hotspot: the four carving parts are 6-14 % each, the simplification 41 %. Nothing
measured gives more than about 1.2x there without parallel carving.

## 3. Upper bounds per phase and profile categories


### Per mesh, tail (24 meshes)

| mesh | T s | in tris | raw | final | us/raw (x 800 median) | oracle | touch | insert | gate bookkeeping | pinch / manifold trials | wrap other | reduce parallel | reduce serial | check | certify | serial share of wall | parallel work share of CPU | Amdahl today 4c / 16c (vs 1 core) | T on 16c, today's structure | all-parallel floor 4c / 16c s |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 40118 | 69.0 | 4252 | 1204976 | 107978 | 57.3 (1.52) | 21 | 9 | 14 | 14 | 14 | 5 | 15 | 2 | 0 | 4 | 83 % | 43 % | 1.48x / 1.69x | 60.0 | 25.3 / 6.3 |
| 860049 | 56.6 | 21373 | 840960 | 245664 | 67.3 (1.79) | 10 | 9 | 13 | 12 | 24 | 7 | 15 | 3 | 1 | 5 | 82 % | 43 % | 1.48x / 1.69x | 48.8 | 20.6 / 5.2 |
| 69261 | 37.0 | 6720 | 725154 | 119790 | 51.0 (1.36) | 16 | 12 | 17 | 14 | 10 | 5 | 18 | 2 | 1 | 6 | 81 % | 48 % | 1.55x / 1.80x | 31.8 | 14.3 / 3.6 |
| 222695 | 36.7 | 64318 | 717714 | 248356 | 51.2 (1.36) | 5 | 17 | 18 | 17 | 10 | 4 | 20 | 2 | 1 | 6 | 77 % | 52 % | 1.65x / 1.96x | 30.2 | 14.8 / 3.7 |
| 535629 | 35.2 | 2938 | 686084 | 145608 | 51.3 (1.36) | 13 | 10 | 17 | 15 | 9 | 8 | 17 | 4 | 1 | 6 | 80 % | 47 % | 1.55x / 1.80x | 29.6 | 13.3 / 3.3 |
| 500097 | 34.8 | 7192 | 607884 | 125364 | 57.3 (1.52) | 18 | 11 | 16 | 13 | 12 | 7 | 14 | 3 | 1 | 5 | 83 % | 42 % | 1.46x / 1.66x | 30.1 | 12.5 / 3.1 |
| 222687 | 34.5 | 32158 | 681274 | 242946 | 50.7 (1.35) | 6 | 17 | 18 | 18 | 9 | 4 | 18 | 3 | 1 | 6 | 78 % | 50 % | 1.60x / 1.89x | 28.7 | 13.6 / 3.4 |
| 520646 | 32.6 | 708 | 669034 | 99728 | 48.7 (1.30) | 13 | 8 | 17 | 14 | 13 | 5 | 18 | 4 | 1 | 5 | 79 % | 48 % | 1.56x / 1.81x | 27.4 | 12.4 / 3.1 |
| 78968 | 29.7 | 5182 | 486720 | 139782 | 60.9 (1.62) | 10 | 12 | 13 | 11 | 20 | 6 | 15 | 6 | 1 | 6 | 82 % | 43 % | 1.47x / 1.66x | 25.5 | 10.6 / 2.6 |
| 500115 | 29.6 | 3832 | 580516 | 106376 | 51.0 (1.36) | 16 | 9 | 16 | 14 | 15 | 7 | 14 | 3 | 1 | 5 | 81 % | 44 % | 1.49x / 1.69x | 25.1 | 10.6 / 2.7 |
| 500099 | 26.2 | 348 | 604086 | 80180 | 43.4 (1.15) | 11 | 7 | 19 | 16 | 13 | 5 | 17 | 5 | 1 | 6 | 76 % | 52 % | 1.65x / 1.97x | 21.4 | 10.5 / 2.6 |
| 111959 | 25.7 | 25562 | 412740 | 206992 | 62.2 (1.65) | 8 | 11 | 12 | 11 | 22 | 7 | 14 | 3 | 1 | 11 | 82 % | 43 % | 1.48x / 1.68x | 22.0 | 9.3 / 2.3 |
| 679615 | 25.4 | 94 | 603690 | 44278 | 42.0 (1.12) | 12 | 6 | 17 | 14 | 8 | 3 | 20 | 15 | 0 | 4 | 83 % | 42 % | 1.47x / 1.66x | 21.9 | 9.1 / 2.3 |
| 49901 | 23.8 | 172 | 569246 | 50200 | 41.9 (1.11) | 8 | 5 | 17 | 14 | 11 | 4 | 16 | 17 | 1 | 6 | 85 % | 41 % | 1.44x / 1.63x | 21.1 | 8.6 / 2.1 |
| 49998 | 22.6 | 12188 | 445596 | 199140 | 50.8 (1.35) | 11 | 16 | 18 | 16 | 10 | 4 | 14 | 4 | 1 | 6 | 82 % | 45 % | 1.51x / 1.73x | 19.5 | 8.4 / 2.1 |
| 224108 | 21.8 | 11180 | 539522 | 89982 | 40.4 (1.08) | 3 | 13 | 19 | 17 | 8 | 3 | 26 | 3 | 1 | 6 | 73 % | 57 % | 1.74x / 2.13x | 17.3 | 9.2 / 2.3 |
| 39368 | 20.6 | 53508 | 339842 | 115242 | 60.7 (1.61) | 8 | 18 | 13 | 12 | 17 | 6 | 14 | 5 | 1 | 6 | 83 % | 41 % | 1.45x / 1.63x | 17.8 | 7.3 / 1.8 |
| 84042 | 20.3 | 9328 | 387524 | 194534 | 52.4 (1.40) | 11 | 17 | 15 | 13 | 11 | 7 | 13 | 3 | 2 | 8 | 82 % | 44 % | 1.50x / 1.71x | 17.4 | 7.4 / 1.9 |
| 51909 | 18.5 | 8624 | 449318 | 162524 | 41.1 (1.09) | 7 | 10 | 18 | 16 | 9 | 5 | 19 | 3 | 1 | 11 | 75 % | 55 % | 1.70x / 2.06x | 14.8 | 7.6 / 1.9 |
| 466329 | 17.8 | 67888 | 369922 | 122454 | 48.1 (1.28) | 8 | 15 | 15 | 13 | 12 | 5 | 19 | 5 | 1 | 5 | 81 % | 44 % | 1.49x / 1.70x | 15.0 | 6.4 / 1.6 |
| 175914 | 16.6 | 1444 | 321886 | 321886 | 51.6 (1.37) | 10 | 10 | 15 | 12 | 21 | 5 | 14 | 4 | 1 | 7 | 81 % | 45 % | 1.52x / 1.74x | 14.2 | 6.2 / 1.5 |
| 139429 | 16.4 | 19426 | 318808 | 165362 | 51.4 (1.37) | 11 | 14 | 15 | 12 | 19 | 5 | 13 | 2 | 1 | 9 | 79 % | 47 % | 1.54x / 1.78x | 13.6 | 6.1 / 1.5 |
| 1514905 | 14.4 | 52424 | 212670 | 38048 | 67.5 (1.80) | 12 | 22 | 11 | 11 | 6 | 3 | 22 | 7 | 1 | 5 | 72 % | 57 % | 1.75x / 2.15x | 11.2 | 6.0 / 1.5 |
| 162898 | 14.0 | 2304 | 348204 | 40790 | 40.2 (1.07) | 7 | 6 | 15 | 13 | 20 | 7 | 19 | 6 | 1 | 6 | 76 % | 51 % | 1.62x / 1.93x | 11.4 | 5.5 / 1.4 |

Phase shares of the summed time (680 s): oracle 11.7 %, touch 11.5 %, insert 15.8 %, carve_book 14.1 %, trials 13.7 %, wrap_other 5.4 %, reduce_par 16.7 %, reduce_ser 4.3 %, check 0.9 %, certify 5.9 %

### Upper bounds by zeroing a phase, tail (M: phase time from the timing keys set to 0; speedup of the summed time / median speedup per mesh)

| phase set to zero | speedup (sum) | median per mesh | max T after s |
|---|---:|---:|---:|
| oracle + touch (L1 remainder) | 1.30x | 1.29x | 48.0 |
| Delaunay insertion | 1.19x | 1.19x | 59.2 |
| gate bookkeeping (pop, recheck, dual edge, labels, pushes) | 1.16x | 1.16x | 59.4 |
| pinch / manifold trials | 1.16x | 1.14x | 59.0 |
| whole main carving (oracle, touch, insert, bookkeeping) | 2.13x | 2.12x | 31.0 |
| reduction, parallel rounds | 1.20x | 1.19x | 58.4 |
| reduction, serial parts | 1.05x | 1.04x | 67.4 |
| certification | 1.06x | 1.06x | 66.2 |
| self-intersection check | 1.01x | 1.01x | 68.8 |
| whole simplification | 1.39x | 1.39x | 53.8 |

### Profile categories, tail (share of CPU samples inside the pipeline window, all threads)

| exact stage (beyond double) | filters (double predicates, tri-tri filter) | BVH (input oracle, AABB trees) | hash grid / spatial lists | GC (libclrgc) | GC pause, share of wall | alloc (runtime helpers, page faults, clears) | rounding | rest |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0.06 % | 9.9 % | 16.1 % | 9.9 % | 3.2 % | 2.4 % | 3.3 % | 0.0 % | 57.6 % |

Rest, by owner: MeshAlphaWrap 21.1 %, Reducer 10.6 %, Delaunay3 8.6 %, DMesh3 / mesh queries 8.3 %, deviation bounds 4.1 %, other 3.0 %, jit 1.8 %

### Per mesh, normal (30 meshes)

| mesh | T s | in tris | raw | final | us/raw (x 800 median) | oracle | touch | insert | gate bookkeeping | pinch / manifold trials | wrap other | reduce parallel | reduce serial | check | certify | serial share of wall | parallel work share of CPU | Amdahl today 4c / 16c (vs 1 core) | T on 16c, today's structure | all-parallel floor 4c / 16c s |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 73863 | 6.4 | 5772 | 149956 | 54170 | 42.8 (1.14) | 7 | 13 | 17 | 15 | 7 | 4 | 21 | 5 | 2 | 10 | 70 % | 59 % | 1.79x / 2.24x | 4.9 | 2.7 / 0.7 |
| 78660 | 5.6 | 99262 | 129006 | 92746 | 43.8 (1.16) | 5 | 17 | 14 | 13 | 8 | 7 | 15 | 5 | 3 | 12 | 78 % | 52 % | 1.63x / 1.94x | 4.7 | 2.3 / 0.6 |
| 84641 | 5.6 | 1364 | 135214 | 26940 | 41.1 (1.09) | 7 | 7 | 15 | 14 | 15 | 5 | 19 | 4 | 2 | 12 | 73 % | 57 % | 1.74x / 2.14x | 4.4 | 2.3 / 0.6 |
| 267657 | 5.6 | 4500 | 136230 | 48650 | 40.8 (1.09) | 9 | 11 | 13 | 12 | 10 | 4 | 22 | 8 | 2 | 9 | 71 % | 60 % | 1.81x / 2.26x | 4.3 | 2.4 / 0.6 |
| 669966 | 5.5 | 7174 | 132788 | 40290 | 41.3 (1.10) | 7 | 16 | 15 | 13 | 7 | 4 | 21 | 5 | 2 | 10 | 73 % | 58 % | 1.77x / 2.20x | 4.4 | 2.4 / 0.6 |
| 37283 | 5.5 | 810 | 163700 | 28972 | 33.4 (0.89) | 7 | 8 | 14 | 13 | 7 | 3 | 25 | 11 | 1 | 11 | 69 % | 60 % | 1.82x / 2.29x | 4.1 | 2.4 / 0.6 |
| 75654 | 5.2 | 54870 | 102376 | 55920 | 50.5 (1.34) | 7 | 20 | 14 | 14 | 5 | 7 | 19 | 4 | 2 | 9 | 76 % | 52 % | 1.64x / 1.96x | 4.2 | 2.0 / 0.5 |
| 1093600 | 5.1 | 52848 | 122040 | 68966 | 41.5 (1.10) | 6 | 21 | 14 | 14 | 8 | 6 | 17 | 3 | 3 | 9 | 77 % | 52 % | 1.63x / 1.94x | 4.2 | 2.0 / 0.5 |
| 1051670 | 5.0 | 25606 | 129162 | 72570 | 38.8 (1.03) | 6 | 17 | 14 | 13 | 8 | 4 | 18 | 5 | 4 | 11 | 74 % | 56 % | 1.72x / 2.10x | 4.0 | 2.1 / 0.5 |
| 497978 | 5.0 | 180 | 171888 | 26006 | 29.0 (0.77) | 4 | 8 | 15 | 14 | 8 | 3 | 30 | 7 | 1 | 10 | 69 % | 59 % | 1.80x / 2.26x | 3.8 | 2.1 / 0.5 |
| 109459 | 4.9 | 2502 | 158250 | 33540 | 31.0 (0.82) | 5 | 13 | 17 | 15 | 8 | 4 | 22 | 5 | 1 | 10 | 70 % | 59 % | 1.79x / 2.23x | 3.7 | 2.1 / 0.5 |
| 85580 | 4.8 | 194 | 138332 | 26782 | 34.8 (0.93) | 3 | 6 | 14 | 13 | 8 | 3 | 25 | 18 | 1 | 8 | 77 % | 53 % | 1.67x / 2.00x | 4.0 | 2.0 / 0.5 |
| 67121 | 4.8 | 10902 | 106774 | 106774 | 44.8 (1.19) | 5 | 15 | 12 | 12 | 9 | 7 | 21 | 6 | 2 | 12 | 71 % | 58 % | 1.76x / 2.17x | 3.7 | 2.0 / 0.5 |
| 55575 | 4.8 | 748 | 142418 | 28142 | 33.6 (0.89) | 5 | 9 | 14 | 14 | 8 | 4 | 25 | 6 | 2 | 15 | 68 % | 64 % | 1.93x / 2.52x | 3.6 | 2.3 / 0.6 |
| 84781 | 4.8 | 6826 | 130776 | 28996 | 36.5 (0.97) | 6 | 11 | 16 | 14 | 7 | 4 | 24 | 7 | 1 | 12 | 70 % | 60 % | 1.82x / 2.28x | 3.7 | 2.1 / 0.5 |
| 697192 | 4.8 | 232 | 160702 | 27304 | 29.7 (0.79) | 9 | 7 | 15 | 14 | 7 | 3 | 27 | 5 | 1 | 11 | 67 % | 63 % | 1.91x / 2.46x | 3.5 | 2.2 / 0.5 |
| 1036392 | 4.7 | 7722 | 130440 | 41678 | 36.4 (0.97) | 7 | 10 | 14 | 12 | 8 | 4 | 25 | 6 | 2 | 12 | 66 % | 64 % | 1.93x / 2.52x | 3.5 | 2.2 / 0.5 |
| 77461 | 4.7 | 38242 | 103116 | 36128 | 45.7 (1.22) | 5 | 18 | 14 | 14 | 7 | 5 | 20 | 4 | 2 | 10 | 76 % | 51 % | 1.63x / 1.93x | 3.8 | 1.8 / 0.5 |
| 70837 | 4.7 | 874 | 140878 | 21148 | 33.4 (0.89) | 5 | 6 | 13 | 12 | 13 | 6 | 24 | 6 | 1 | 13 | 68 % | 61 % | 1.84x / 2.33x | 3.5 | 2.0 / 0.5 |
| 224829 | 4.6 | 28004 | 129940 | 29792 | 35.2 (0.94) | 5 | 11 | 13 | 12 | 8 | 5 | 26 | 8 | 2 | 12 | 67 % | 62 % | 1.87x / 2.40x | 3.4 | 2.0 / 0.5 |
| 313873 | 4.6 | 41778 | 128026 | 41552 | 35.6 (0.95) | 6 | 14 | 15 | 13 | 7 | 6 | 21 | 7 | 2 | 10 | 72 % | 57 % | 1.75x / 2.16x | 3.6 | 1.9 / 0.5 |
| 220653 | 4.6 | 372 | 138388 | 16780 | 32.9 (0.88) | 6 | 7 | 15 | 13 | 8 | 4 | 26 | 7 | 1 | 13 | 65 % | 65 % | 1.95x / 2.55x | 3.3 | 2.1 / 0.5 |
| 479929 | 4.5 | 12934 | 112706 | 33234 | 40.2 (1.07) | 5 | 15 | 12 | 12 | 12 | 4 | 22 | 6 | 2 | 10 | 67 % | 63 % | 1.91x / 2.47x | 3.3 | 2.1 / 0.5 |
| 326895 | 4.5 | 290 | 129788 | 21692 | 34.7 (0.92) | 5 | 4 | 13 | 12 | 9 | 4 | 25 | 14 | 1 | 13 | 69 % | 59 % | 1.79x / 2.24x | 3.4 | 1.9 / 0.5 |
| 76714 | 4.5 | 5630 | 145108 | 40478 | 30.7 (0.82) | 5 | 13 | 15 | 14 | 7 | 4 | 27 | 5 | 2 | 9 | 69 % | 60 % | 1.81x / 2.27x | 3.4 | 1.9 / 0.5 |
| 61432 | 4.4 | 2398 | 105384 | 43596 | 42.2 (1.12) | 10 | 13 | 13 | 13 | 10 | 4 | 16 | 5 | 2 | 16 | 73 % | 55 % | 1.69x / 2.05x | 3.5 | 1.8 / 0.4 |
| 1312965 | 4.4 | 50866 | 103588 | 41586 | 42.4 (1.13) | 8 | 16 | 12 | 12 | 6 | 6 | 21 | 5 | 2 | 12 | 73 % | 57 % | 1.74x / 2.13x | 3.5 | 1.9 / 0.5 |
| 1582417 | 4.2 | 852 | 121760 | 51124 | 34.9 (0.93) | 8 | 7 | 14 | 13 | 11 | 4 | 19 | 6 | 4 | 14 | 69 % | 59 % | 1.79x / 2.22x | 3.2 | 1.8 / 0.4 |
| 472079 | 4.0 | 1264 | 143348 | 23072 | 27.7 (0.74) | 7 | 8 | 15 | 15 | 8 | 4 | 26 | 5 | 1 | 12 | 66 % | 64 % | 1.93x / 2.51x | 2.9 | 1.8 / 0.5 |
| 1165782 | 3.8 | 3772 | 105574 | 34052 | 35.8 (0.95) | 7 | 11 | 13 | 13 | 14 | 4 | 16 | 5 | 2 | 14 | 69 % | 61 % | 1.85x / 2.36x | 2.9 | 1.7 / 0.4 |

Phase shares of the summed time (145 s): oracle 6.3 %, touch 11.7 %, insert 14.2 %, carve_book 13.1 %, trials 8.5 %, wrap_other 4.5 %, reduce_par 22.0 %, reduce_ser 6.4 %, check 1.9 %, certify 11.3 %

### Upper bounds by zeroing a phase, normal (M: phase time from the timing keys set to 0; speedup of the summed time / median speedup per mesh)

| phase set to zero | speedup (sum) | median per mesh | max T after s |
|---|---:|---:|---:|
| oracle + touch (L1 remainder) | 1.22x | 1.22x | 5.1 |
| Delaunay insertion | 1.17x | 1.17x | 5.3 |
| gate bookkeeping (pop, recheck, dual edge, labels, pushes) | 1.15x | 1.15x | 5.5 |
| pinch / manifold trials | 1.09x | 1.08x | 6.0 |
| whole main carving (oracle, touch, insert, bookkeeping) | 1.83x | 1.80x | 3.2 |
| reduction, parallel rounds | 1.28x | 1.27x | 5.1 |
| reduction, serial parts | 1.07x | 1.06x | 6.1 |
| certification | 1.13x | 1.13x | 5.8 |
| self-intersection check | 1.02x | 1.02x | 6.3 |
| whole simplification | 1.71x | 1.70x | 4.0 |

### Profile categories, normal (share of CPU samples inside the pipeline window, all threads)

| exact stage (beyond double) | filters (double predicates, tri-tri filter) | BVH (input oracle, AABB trees) | hash grid / spatial lists | GC (libclrgc) | GC pause, share of wall | alloc (runtime helpers, page faults, clears) | rounding | rest |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0.08 % | 8.8 % | 12.2 % | 7.9 % | 4.0 % | 2.8 % | 9.9 % | 0.0 % | 57.1 % |

Rest, by owner: MeshAlphaWrap 14.0 %, Reducer 11.8 %, DMesh3 / mesh queries 7.5 %, jit 6.9 %, Delaunay3 6.8 %, other 5.2 %, deviation bounds 4.6 %

## 4. Diagnosis per tail mesh

Rule: pathological input, algorithmic hotspot, or size. None is pathological; every one is size (0.21-1.2 M raw
triangles from the fixed diag / 300 on inputs with a high area / diag^2). "Algorithmic share" marks a phase at or above
17 % of T (trials, touch test, oracle), which is where a non-size lever would act on that mesh.

| mesh | T s | raw | diagnosis | notes |
|---|---:|---:|---|---|
| 40118 | 69.0 | 1204976 | size + algorithmic share | intersection oracle 21 %; area / diag^2 10.0 |
| 860049 | 56.6 | 840960 | size + algorithmic share | pinch / manifold trials 24 % (1138 pinch points, 19 local trials); area / diag^2 6.9; 10 components, diag 1.04x the largest's |
| 69261 | 37.0 | 725154 | size | area / diag^2 5.8; 11 components, diag 1.25x the largest's |
| 222695 | 36.7 | 717714 | size | area / diag^2 1.4 |
| 535629 | 35.2 | 686084 | size | area / diag^2 5.6 |
| 500097 | 34.8 | 607884 | size + algorithmic share | intersection oracle 18 %; area / diag^2 4.9 |
| 222687 | 34.5 | 681274 | size + algorithmic share | touch test 17 % (dense input, 32158 triangles); area / diag^2 1.4 |
| 520646 | 32.6 | 669034 | size | area / diag^2 5.4 |
| 78968 | 29.7 | 486720 | size + algorithmic share | pinch / manifold trials 20 % (293 pinch points, 17 local trials); area / diag^2 4.0 |
| 500115 | 29.6 | 580516 | size | area / diag^2 4.7 |
| 500099 | 26.2 | 604086 | size | area / diag^2 4.9 |
| 111959 | 25.7 | 412740 | size + algorithmic share | pinch / manifold trials 22 % (2642 pinch points, 19 local trials); area / diag^2 1.5; 674 components, diag 1.04x the largest's |
| 679615 | 25.4 | 603690 | size | reduction's serial parts 15 % (large flat faces, 94 input triangles); area / diag^2 5.0 |
| 49901 | 23.8 | 569246 | size | reduction's serial parts 17 % (large flat faces, 172 input triangles); area / diag^2 4.7; 4 components, diag 1.50x the largest's |
| 49998 | 22.6 | 445596 | size | area / diag^2 3.7; 4 components, diag 1.51x the largest's |
| 224108 | 21.8 | 539522 | size | area / diag^2 1.1 |
| 39368 | 20.6 | 339842 | size + algorithmic share | touch test 18 % (dense input, 53508 triangles) |
| 84042 | 20.3 | 387524 | size + algorithmic share | touch test 17 % (dense input, 9328 triangles); area / diag^2 3.0 |
| 51909 | 18.5 | 449318 | size |  |
| 466329 | 17.8 | 369922 | size |  |
| 175914 | 16.6 | 321886 | size + algorithmic share | pinch / manifold trials 21 % (654 pinch points, 19 local trials); simplification not applied (final = raw) |
| 139429 | 16.4 | 318808 | size + algorithmic share | pinch / manifold trials 19 % (390 pinch points, 18 local trials) |
| 1514905 | 14.4 | 212670 | size + algorithmic share | touch test 22 % (dense input, 52424 triangles) |
| 162898 | 14.0 | 348204 | size + algorithmic share | pinch / manifold trials 20 % (63 pinch points, 16 local trials) |

## 5. Robust-repair cases

`BENCH_REPAIR=studyrepair` (local probe, `StudyRepair.cs.txt`): `MeshRobustRepair` with the tests' settings on the
procedural inputs, second of two runs in one process, 4 cores. Rows `repair_cases.jsonl`.

| case | input | master 1230482 | + #703 (849223c) | phases on master | diagnosis |
|---|---|---:|---:|---|---|
| #692 `SolidifyNotClean_AlphaWrapped` (one Apply; the test does two plus an IoU) | ThinPlates(6), 144 triangles | 11.6 s | 10.7 s | wrap 5.8 s (raw 185490), wrap.simplify 3.0 s, solidify 2.7 s | size: 185k raw triangles for 12 boxes. #703 does not apply (>= 64 triangles), a 300k budget does not either. Lever: alpha from the input's feature size for more than 64 triangles, or a smaller test input |
| #693 `FarPart_NotABlob` far = 1e8 | 2 spheres + far tetrahedron, 8668 triangles | 50.4 s | 49.7 s | exact.volume 31.4 s, volume 18.0 s; 196 CPU-s; wrap not used | algorithmic hotspot on a pathological scale: 97.8 % of CPU in `DMeshAABBTree3.branch_fast_winding_num` (63 % self, 33 % libm atan2) under `MeshRepairMetrics.volume_iou_at_least`: exact solid angles instead of the far-field cache (cause in the tree / criterion not diagnosed) |
| #693 far = 1e12 | same | 49.7 s | 49.9 s | exact.volume 30.9 s, volume 17.9 s | same |
| #693 far = 1e3 (reference) | same | 0.78 s | 0.93 s | exact 0.31 s | the bound for #693: about 50 s -> 1 s (M) |
| #636 | octahedron, 8 triangles at 2.4e-35 | 6.9 s | 0.34 s | wrap 3.5 s (raw 204222), wrap.simplify 3.1 s | size from the fixed resolution; fixed by #703 (raw 232) |
| #712 a | flattened tetrahedron, 4 triangles | 3.4 s | 0.16 s | wrap 1.6 s (raw 136018), wrap.simplify 1.6 s | fixed by #703 (raw 94) |
| #712 b | two open cubes at 1e-23, 24 triangles | 42.2 s | 1.3 s | wrap 23.5 s (raw 869654), wrap.simplify 17.7 s | fixed by #703 (raw 418); the rest is solidify 1.2 s |

Slow rows of `benchmarks/MeshRepairBenchmark`: the wrap rows are the tail above (same harness). The README's slow
solidify rows (the contouring with the fast winding number on CAD meshes with long triangles, "hundreds of exact solid
angles per query") are the same mechanism as #693 and were not re-measured here.

Profile categories of the repair cases (perf, share of CPU): #692 filters 7.6 %, BVH + grid 25.1 %, GC + alloc 21.6 %,
rest 45.7 %; #693 BVH 97.3 % (the winding number), everything else < 1.5 %; #636 on master filters 18.6 %, BVH + grid
18.5 %, GC + alloc 16.4 %, rest 46.5 %; #712 b on master filters 10.7 %, BVH + grid 21.9 %, GC + alloc 9.7 %, rest
57.7 %.

## 6. Side findings

- The simplification is sometimes not applied: 175914 on master (final = raw 321886, min angle 2.4 deg) and 51909 at
  alpha diag / 245 (final = raw 329444, min angle 1.2 deg). Not diagnosed; the sharp-feature study saw the same on 2 of
  15 inputs with thin triangles near the input (`SimplifyWithin` returns null). Such a result keeps the raw wrap's size
  and angles and takes the full reduction time first.
- Pinch-heavy meshes run 16-19 local trials (860049, 111959, 78968, 175914, 139429, 162898): 19-24 % of their time.
- 679615 and 49901 (94 and 172 input triangles, large flat faces) spend 15-17 % in the reduction's serial parts.
- Under `taskset -c 0` the wrap of a median mesh is 1.59x slower although it is serial code: background JIT tiering and
  the concurrent GC share the core. Time budgets for small meshes should be stated with the core count.
- CGAL 5.6 (AW3 session, black box, single-threaded, same alpha / offset): our raw wrap takes 0.24x (tail 20) and 0.20x
  (30 random) of its time; its worst case is 205 s against our 55 s raw / 69 s pipeline.

## 7. How others solve it (papers and documentation only; no CGAL code read)

- Portaneri et al., "Alpha Wrapping with an Offset", ACM TOG 41(4) 2022: serial refinement by a priority queue; the
  resolution is the user's alpha, with no size budget; cost grows with the offset surface's area / alpha^2, as measured.
- Blelloch, Fineman, Gibbons, Shun, PPoPP 2012 (deterministic reservations) and Blelloch, Gu, Shun, Sun, "Parallelism
  in randomized incremental algorithms", SPAA 2016: rounds of independent insertions; the pattern of (B).
- Marot, Pellerin, Remacle, IJNME 2019, and Shewchuk's and Amenta-Choi-Rote's notes on point order (BRIO, Hilbert):
  locality of the Delaunay structure decides the per-insertion cost at scale; applies to the layout (lever 6), not to
  the insertion order, which the carving fixes.
- Barill, Dickson, Schmidt, Levin, Jacobson, "Fast winding numbers for soups and clouds", SIGGRAPH 2018: the far-field
  expansion is valid outside a multiple of the cluster's radius, so a cluster that spans a far part is never "far". Whether
  that is #693's mechanism (tree clusters spanning the far part, or query points spread over the huge box) was not
  checked in the code; clusters per component would avoid it either way.

## 8. Files (branch `runs/alpha-wrap-3-fable`, folder `aw3study/`)

- Lists: `list_tail.txt`, `list_normal.txt`, `list_tail8.txt`, `list_normal10.txt`.
- Rows: `base4_*.jsonl` (baseline), `base4b_*`, `base4c_*` (reruns), `serial_*` (taskset -c 0), `p1_*`
  (DOTNET_PROCESSOR_COUNT=1, not a serial reference), `gc_noconc_*`, `gc_server_*`, `gc_gen0_256m_*`, `thp_always_*`,
  `thp2_always_*`, `budget300k_tail.jsonl`, `repair_cases.jsonl`.
- Tables: `tables.md` (per mesh, zeroing bounds, categories), `phases_*.txt`, `cats_*.txt`, `serial_hot_*.txt`,
  `projection.txt`, `budget300k.txt`, `gcprobe.txt`, `diagnosis.md`.
- Scripts: `drive.sh`, `perfrun.sh`, `fold.py`, `prof.py`, `cats.py`, `serial_hot.py`, `phases.py`, `gcprobe.py`,
  `budget.sh`, `budget_cmp.py`, `project.py`, `report_tables.py`, `chain2.sh`, `chain3.sh`.
- Probes (text only): `bench_probe.patch`, `StudyRepair.cs.txt`, inputs `c636.txt`, `c712a.txt`, `c712b.txt`.
- The perf stack files (260 MB) are not on the branch; `perfrun.sh` regenerates them.

## 9. Limits of this study

- One container, no alternating A/B for the probes; drift 5-8 % between runs. Shares and ratios inside one run are
  safe; a gain below 5 % is not established.
- The (B) rows are a model on top of the AW3 session's round ceilings: they assume the whole main carving loop
  (tests and insertions) runs at the round ceiling and nothing else changes; scheduling overhead and the parallel
  insertion's own cost are not in it.
- The category split is by symbol: the filtered InSphere inside `Delaunay3.Insert` is counted under rest (Delaunay3),
  not under filters (the AW3 session measured the cavity search at 45 % of an insertion), and unsymbolized runtime
  helpers are counted as allocation.
- Hardware counters are not available in the VM, so the cause of the size-dependent unit costs (cache misses) is
  inferred, not measured.
