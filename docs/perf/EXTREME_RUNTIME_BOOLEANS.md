# ExactMeshBoolean: the examples with extreme run time, profiled, and the levers left

Research note (owner task 2026-11-12). Every known `ExactMeshBoolean` example with an extreme run time, measured on one
commit set, split by stage, grouped by its dominant stage, and a ranked list of levers with a measured upper bound each.
Cheap levers were falsified before they are listed; closed levers are listed with their bound. No library code changed:
the probes are scratch patches applied to a throw-away worktree (`benchmarks/ExtremeBooleans/probe_*.py`), as
`benchmarks/SnapRoundingExperiment/trace/instrument.py` does. Thingi10K meshes are fetched at run time, never committed.

## 1. Result in short

- **66 operations, 2412 s** (the rows of section 3.1 without the n-ary CSG rows and the 8 option-A rows):
  classification 66.9 %, triangulation 8.2 %, rounding 8.1 %, intersections 6.0 %, merge 4.8 %, input preparation 2.4 %,
  assembly 2.2 %. GC pauses are 11.0 % of it, and 23-27 % on single cases.
- **The extreme cases are classification cases.** 34 of the 40 operations above 10 s are dominated by classification:
  the large open models (117959, 1087141 and its four siblings, 112798, 1005586: 51-114 s, 61-90 % classification),
  55928 (380 s), 42327 and the Antipodal models 107554 / 113906 (21-40 s). The rest is rounding (1368057 union 39 s,
  102375 union 10.3 s, the fuzz case of #711) and intersections (519511, 14.3-20.3 s).
- **#711 is BigInteger arithmetic in the rounding.** On the timeout input 87 % of the time is in `orient3d_bigint` /
  `orient2d_bigint`: corners at 1.8e-284 next to coordinates of 1e-32 span 2^840, so neither the 62-bit integer stage
  nor the expansion range admits them. With those corners set to 0 the operation takes 50 ms. The fuzzer's 47 s are two
  booleans (the strict re-run of `MeshFuzzPilot`). All 60 slow grp-a units have the same cause (section 3.4): span
  2^839-2^1934, rounding 90 % of the time, BigInteger 85 % of it, median 4.8 s per boolean on master. The grp-b repair
  timeouts (#712, up to 49 s) are another matter: alpha wrap and `Reducer` under heavy GC, with almost no exact stage.
- **Shared profile table** (section 4.8, the AW3 format; CPU-sample shares over all threads): the exact stage matters in the
  #711 class (67-83 % of the samples, 85-89 % of the wall time), 42327 (46 %), 1368057, 55928 and n-ary (22-25 %). In the
  normal stratum of section 8 it is 7.6 %, plus 8.9 % filters.
- **Normal stratum** (section 8: 36 unions under 5 s, median of five, master cbf8b787). Rounding takes 24 % of the wall
  time, pair search 18 %, classification 19 % (from two open models; 6-13 % on closed ones), merge 11 %, assembly 10 %
  and GC pauses 8.6 %. No single lever reaches 10 %. The queued items (the rounding search, lever 4, lever 1) and the exact-numbers
  study's point and plane constructions (C, C') bound about 20-25 %. P2 does nothing here: no BigInteger stage runs in
  normal operations. The pair search (6 %) is the one uncovered lever of that size.
- **Ranked levers** (section 5): (1) a hierarchical Antipodal boundary sum after option A (bound: 70 % of 117959's CPU samples, 30 % of
  1087141's, 52 % of 122796's; 23 instead of 6007 terms per evaluation, error at most 3.4e-4 before the fallback); (2) option A itself,
  already decided (the open union group 437 -> 349 s, 1087141 -56 %, 122796 +52 %); (3) a cost-weighted rounding budget
  for wide exponent spans, **dropped by the owner in favour of P2**, the exact-numbers study's allocation-free kernel
  (298 -> 64 s on the 61 #711-class units, bit-identical); (4) server GC as documented advice, and the allocation task of the roadmap, whose
  trigger is now met (server GC -19 % / -11 % on 107554 / 42327, bit-identical); (5) the n-ary one-pass levers of the
  pre-study, restated with today's numbers (boxes_64_1 38.4 s against 0.57 s chained).
- **Closed** (no measurable gain, or dominated by another lever; the owner's rule of 2026-11-12): a leading-term certificate for the BigInteger stage (decides 1.4 % of the calls, 2 % of
  the time), scaling into the
  expansion range (8.3 % of the op), stopping at the evaluation limit on #711 (0 %), the `BigInteger[4,4]` matrix
  of `orient3d` on `ExactPoint3` (-4 % to +3 %, noise), shard hash mixing in `AddPoint` (no bias to fix), the per-patch L1
  path (never runs, superseded by L2), JIT tiering for cold calls (about 7 %).

## 2. Setup and commit set

- **Library:** master `8124a7d3` with PR #675 (head `a9b00da3`) merged locally (merge `a22943a9`, never pushed).
  #675 changes no result here: all 33 hashes of the 11 rotated models equal the baseline rows of #721
  (`benchmarks/SnapRoundingExperiment/trace/results/baseline_2026-11-12.jsonl` on #721's branch, master `46529be`).
  When #675 lands on master: spot check 2-3 cases there, a full re-run only if they differ.
- **Machine:** the 4-core cloud container (15 GB, no swap), nothing else running during a timed row.
- **Command per case and operation:** `run.sh` (one process per operation, so that the peak RSS is that of the
  operation), which runs `BooleanComparison profile --repeat 1 --safe-rounding --ops <op> <case>` under
  `/usr/bin/time -v` with `DOTNET_GCHeapHardLimit` = 13 GB (an out-of-memory case ends with an error row instead of
  taking the container down). Cases: Thingi10K raw meshes by id (`fetch.py`), paired with their rotated, shifted copy
  (`BooleanComparison export`, the pairing of #721, #675 and the 55928 note); the #711 timeout input from its dump
  (`dump2off.py`, round-trip coordinates); `grid:10` (the n-ary cube grid). The whole job: `job.sh`; table:
  `summarize.py`; raw rows: `benchmarks/ExtremeBooleans/results/inventory_2026-11-12.jsonl`.
- **"total"** is the sum of the top-level phases of `ExactMeshBoolean.Compute`, its input preparation (`input`: weld,
  orientation) included; "wall" is the whole process (start-up, JIT, reading the files). (A first version of this note
  left `input` out of the totals; every number here includes it.)
- **Cold processes:** each operation runs in a fresh process. The first operation of a process is as fast as in the
  #721 baseline (which ran three operations per process); the second and third run 1.4-2x faster warm (486343
  intersection 2.2 cold / 1.1 s warm, 67709 8.4 / 5.9 s, 102375 6.9 / 4.7 s, hashes equal). `TieredCompilation=0`
  takes 0.5 s of the 7.6 s cold 67709 intersection, server GC and a 256 MB gen0 none: the rest is first-touch memory
  and heap growth. Short operations are therefore pessimistic here by up to 2x; the long ones (> 30 s) by a few %.
  Use the same mode for every before/after comparison.

## 3. Inventory

Every case of the brief has a row; the 60 slow fuzz units could not be run (section 3.2). Peak RSS is the process's.

### 3.1 Measured rows

| case | op | total s | wall s | peak RSS MB | dominant | share | second | status | clean | limit | alloc GB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| #711 timeout (fuzz/t251fe028) | difference | 16.3 | 17.2 | 199 | rounding | 95 % | triangulation 3 % | Success | False | True | 13.5 |
| #711 timeout | union | 1.9 | 2.7 | 196 | rounding | 56 % | triangulation 30 % | Success | False | False | 1.0 |
| #711 timeout | intersection | 0.7 | 1.5 | 194 | triangulation | 59 % | classification 14 % | Success | True | False | 0.3 |
| 67709 | union | 9.1 | 10.1 | 378 | rounding | 35 % | classification 32 % | Success | False | True | 1.5 |
| 67709 | intersection | 8.5 | 9.4 | 342 | rounding | 37 % | classification 31 % | Success | False | True | 1.3 |
| 67709 | difference | 9.1 | 10.0 | 343 | classification | 37 % | rounding 30 % | Success | False | True | 1.4 |
| 42327 | union | 40.3 | 40.9 | 850 | classification | 45 % | rounding 30 % | Success | False | True | 23.0 |
| 42327 | intersection | 34.3 | 35.1 | 922 | classification | 59 % | triangulation 16 % | Success | False | True | 20.6 |
| 42327 | difference | 34.8 | 35.4 | 847 | classification | 62 % | triangulation 13 % | Success | False | True | 20.6 |
| 1368057 | union | 39.2 | 40.6 | 856 | rounding | 53 % | classification 25 % | Success | False | True | 12.8 |
| 1368057 | intersection | 13.5 | 15.0 | 561 | classification | 46 % | triangulation 17 % | Success | False | True | 3.3 |
| 1368057 | difference | 12.0 | 13.4 | 678 | classification | 53 % | triangulation 16 % | Success | False | False | 3.3 |
| 102375 | union | 10.4 | 11.8 | 502 | rounding | 55 % | classification 16 % | Success | False | True | 1.5 |
| 102375 | intersection | 7.0 | 8.2 | 393 | rounding | 50 % | classification 16 % | Success | False | True | 1.1 |
| 102375 | difference | 9.0 | 10.3 | 466 | rounding | 48 % | classification 21 % | Success | False | True | 1.5 |
| 498458 | union | 17.1 | 17.9 | 870 | classification | 26 % | rounding 20 % | Success | False | False | 7.0 |
| 498458 | intersection | 13.0 | 14.0 | 713 | classification | 40 % | merge 26 % | Success | True | False | 5.8 |
| 498458 | difference | 13.9 | 14.8 | 781 | classification | 31 % | merge 26 % | Success | False | False | 6.1 |
| 486343 | union | 3.5 | 4.4 | 202 | rounding | 68 % | classification 21 % | Success | False | True | 0.3 |
| 486343 | intersection | 2.2 | 3.1 | 210 | rounding | 53 % | classification 30 % | Success | False | False | 0.3 |
| 486343 | difference | 2.4 | 3.3 | 202 | rounding | 57 % | classification 29 % | Success | False | False | 0.3 |
| 519511 | union | 20.3 | 20.2 | 1261 | intersections | 27 % | rounding 26 % | Success | True | False | 6.4 |
| 519511 | intersection | 14.3 | 14.3 | 869 | intersections | 31 % | assembly 14 % | Success | True | False | 4.8 |
| 519511 | difference | 15.5 | 15.4 | 1025 | intersections | 28 % | assembly 14 % | Success | True | False | 5.0 |
| 1517923 | union | 6.2 | 7.2 | 385 | intersections | 56 % | rounding 9 % | Success | True | False | 1.0 |
| 1517923 | intersection | 4.4 | 5.2 | 368 | intersections | 33 % | triangulation 14 % | Success | True | False | 0.8 |
| 1517923 | difference | 5.1 | 6.0 | 348 | intersections | 48 % | triangulation 10 % | Success | True | False | 0.9 |
| 1063860 | union | 3.9 | 5.6 | 629 | intersections | 39 % | rounding 26 % | Success | True | False | 0.9 |
| 1063860 | intersection | 2.0 | 3.6 | 378 | intersections | 43 % | rounding 18 % | Success | True | False | 0.4 |
| 1063860 | difference | 2.9 | 4.6 | 492 | intersections | 33 % | rounding 28 % | Success | True | False | 0.7 |
| 668056 | union | 5.8 | 6.8 | 517 | rounding | 30 % | intersections 23 % | Success | True | False | 1.7 |
| 668056 | intersection | 4.3 | 5.3 | 425 | intersections | 25 % | triangulation 17 % | Success | True | False | 1.4 |
| 668056 | difference | 4.6 | 5.6 | 437 | intersections | 25 % | triangulation 16 % | Success | True | False | 1.5 |
| 119829 | union | 3.9 | 5.4 | 580 | rounding | 60 % | intersections 16 % | Success | True | False | 1.0 |
| 119829 | intersection | 0.8 | 2.2 | 254 | intersections | 45 % | setup 13 % | Success | True | False | 0.1 |
| 119829 | difference | 2.4 | 3.8 | 297 | rounding | 42 % | intersections 31 % | Success | True | False | 0.4 |
| 124526 | union | 8.0 | 9.3 | 704 | classification | 66 % | intersections 8 % | Success | True | False | 1.0 |
| 124526 | intersection | 6.7 | 8.1 | 452 | classification | 67 % | intersections 10 % | Success | True | False | 0.8 |
| 124526 | difference | 7.2 | 8.5 | 538 | classification | 70 % | intersections 8 % | Success | True | False | 0.9 |
| 122796 | union | 12.6 | 14.4 | 1261 | classification | 61 % | intersections 8 % | Success | False | False | 2.6 |
| 122796 | intersection | 11.4 | 13.1 | 935 | classification | 68 % | intersections 8 % | Success | True | False | 2.1 |
| 122796 | difference | 12.2 | 14.1 | 1011 | classification | 64 % | intersections 8 % | Success | True | False | 2.3 |
| 107554 | union | 31.0 | 31.7 | 1178 | classification | 34 % | rounding 28 % | Success | False | True | 12.1 |
| 107554 | intersection | 24.2 | 25.2 | 1184 | classification | 47 % | merge 14 % | Success | False | False | 10.0 |
| 107554 | difference | 23.7 | 24.8 | 1120 | classification | 45 % | merge 15 % | Success | False | True | 10.0 |
| 113906 | union | 28.8 | 29.4 | 954 | classification | 47 % | rounding 19 % | Success | False | True | 10.1 |
| 113906 | intersection | 21.3 | 22.4 | 650 | classification | 55 % | merge 15 % | Success | False | False | 8.9 |
| 113906 | difference | 24.3 | 25.0 | 859 | classification | 55 % | merge 13 % | Success | False | False | 9.1 |
| 1087141 | union | 110.8 | 113.6 | 3570 | classification | 82 % | intersections 5 % | Success | False | True | 7.7 |
| 1087141 | intersection | 101.7 | 104.0 | 2216 | classification | 87 % | intersections 5 % | Success | False | False | 5.2 |
| 1087141 | difference | 100.8 | 103.3 | 2670 | classification | 86 % | intersections 5 % | Success | False | False | 5.9 |
| 1087144 | union | 107.0 | 109.7 | 3185 | classification | 82 % | intersections 5 % | Success | False | True | 7.7 |
| 1088051 | union | 103.2 | 105.6 | 3117 | classification | 82 % | intersections 5 % | Success | False | True | 7.6 |
| 1088054 | union | 100.1 | 102.7 | 3111 | classification | 81 % | rounding 5 % | Success | False | True | 7.7 |
| 1088138 | union | 101.7 | 104.6 | 3593 | classification | 81 % | rounding 5 % | Success | False | True | 7.6 |
| 117959 | union | 114.2 | 118.4 | 5216 | classification | 87 % | intersections 3 % | Success | True | False | 9.3 |
| 117959 | intersection | 111.8 | 115.3 | 3889 | classification | 89 % | intersections 3 % | Success | True | False | 7.5 |
| 117959 | difference | 112.4 | 116.2 | 4184 | classification | 89 % | intersections 3 % | Success | True | False | 8.0 |
| 112798 | union | 58.1 | 62.7 | 5806 | classification | 68 % | input 7 % | Success | False | False | 12.3 |
| 112798 | intersection | 51.1 | 55.1 | 4416 | classification | 76 % | input 7 % | Success | False | False | 9.4 |
| 112798 | difference | 52.8 | 57.3 | 5524 | classification | 72 % | input 7 % | Success | True | False | 10.8 |
| 1005586 | union | 74.1 | 81.9 | 9003 | classification | 60 % | rounding 14 % | Success | False | False | 16.8 |
| 1005586 | intersection | 57.8 | 62.7 | 4951 | classification | 76 % | intersections 7 % | Success | False | False | 10.7 |
| 1005586 | difference | 62.0 | 67.6 | 6892 | classification | 69 % | intersections 7 % | Success | False | False | 13.6 |
| 55928 | difference | 380.4 | 382.1 | 11815 | classification | 59 % | triangulation 30 % | Success | False | False | 199.4 |
| grid:10 (n-ary, 2000 meshes) | one pass | 6.2 | 7.2 | 682 | classification | 31 % | intersections 27 % | Success | True | False | 2.2 |
| boxes_64_1 (the pre-study's n-ary CSG case, measured here) | one pass | 38.4 | 81.4 for both variants | 1723 | classification | 91 % | triangulation 7 % | Success | - | - | - |
| boxes_64_1 | chained | 0.57 | | | | | | Success | - | - | - |

The four siblings of 1087141 (the same part, 1196 boundary segments each) ran the union only. boxes_64_1:
`BooleanComparison prestudy-csg boxes 64 1` (both variants in one process, so one peak and one wall time).

**Memory.** No case ran out of memory under the 13 GB heap cap. The largest peaks: 55928 difference 11.8 GB,
1005586 union 9.0 GB (6.9 / 5.0 GB for difference / intersection), 112798 union 5.8 GB, 117959 union 5.2 GB. The
"out of memory at 15 GB" of 112798 and 1005586 in #675's reference check was the A/B harness, which holds both runs
(FWN and Antipodal) at once, not one boolean. With option A (section 3.3) 1005586 union peaks at 7.3 GB.

### 3.2 Not run, with the reason

| case | why not | what is known |
|---|---|---|
| the 60 `slow-unit-*` of fuzz #624 group a (#711) | run since: section 3.4 (the owner pushed the units to `runs/mesh-fuzz` after the first measurement) | |
| the slowest boolean tests of `SLOW_BACKLOG.txt` | run, but they are not extreme per operation: they are slow by count | trx durations on this machine (tests of the same tree, master 8124a7d3 + #675): `SafeRoundingDegenerateInputTests.DegenerateInput_InBooleans_CleanWithinTheBound` 15.1 s (198 iterations, 396 booleans, about 38 ms each), `ExactBooleanPropertyTests.Boxes_RepresentationInvariance` 13.0 s, `MeshRobustRepairTests.UncleanResultResolvedExactly_VolumeCheckedOnce` 13.2 s (a repair), `LocalRefinementTests.Boolean_LocalGap_CostsMuchLessThanGlobalRefinement` 6.4 s, `MeshBooleanSeamTests.Union_RandomSpherePairs_AreClosed` 6.1 s (30 unions), `ExactBooleanPropertyTests.Boxes` 1.3-2.0 s per campaign of 300-400 samples. Their lever is the test tier (#700 and the slow-backlog PRs), not the boolean |

### 3.3 Option A on the open models (union, both Antipodal switches lifted)

Owner 2026-11-12, option (A): `AntipodalMaxBoundarySegments` and `AntipodalMaxTriangles` unlimited (follow-up PR after
#675). Measured with `profile --antipodal-unlimited` (added to `Profile.cs` for this note), the same rows otherwise:

| model | default s | option A s | change | peak RSS MB default / A |
|---|---|---|---|---|
| 124526 | 8.0 | 6.8 | -14 % | 704 / 579 |
| 122796 | 12.6 | 19.2 | **+52 %** | 1261 / 1125 |
| 107554 | 31.0 | 30.2 | -3 % | 1178 / 1339 |
| 113906 | 28.8 | 27.4 | -5 % | 954 / 947 |
| 1087141 | 110.8 | 49.1 | **-56 %** | 3570 / 3013 |
| 117959 | 114.2 | 106.3 | -7 % | 5216 / 4461 |
| 112798 | 58.1 | 48.8 | -16 % | 5806 / 6008 |
| 1005586 | 74.1 | 61.2 | -17 % | 9003 / 7325 |
| sum | 437.4 | 349.1 | -20 % | |

This confirms #675's numbers on the merged tree (122796 slower by about 1.5x here, 2x in #675's note: classification
only, f0d3035d -> 896e2064). Allocations double under
A (1087141 7.7 -> 20.2 GB, 1005586 16.8 -> 30.6 GB), which shows up as GC time (section 4.4).

### 3.4 The fuzz units of #624 (master 0325530c)

The owner pushed the units to `runs/mesh-fuzz` (`grp-a/units/` commit 8b059874, `grp-b/units/` 61eb1a10; fuzzer bytes,
fetched at run time, not in this PR). Measured on plain master `0325530c`, which contains #675. A spot check of three
earlier rows on it gave equal hashes: 486343 union, 122796 union, #711 difference.

**grp-a, 60 slow units** (`union,intersection,difference`). Decoding: `MeshFuzzPilot dump` from a 05d1332b build; the
master decoder gives byte-identical dumps for all 60. Conversion with `dump2off.py`, then `units_job.sh` (`run.sh`, one
boolean per process). Raw rows: `results/fuzz_units_grp_a_2026-11-12.jsonl`.

| op | units | total s min / median / max | rounding share | peak RSS MB max | alloc GB median | RoundingClean |
|---|---|---|---|---|---|---|
| union | 47 | 2.7 / 4.7 / 9.1 | 94 % | 198 | 3.5 | 6 of 47 |
| intersection | 12 | 3.9 / 6.4 / 7.5 | 79 % | 196 | 3.7 | 0 of 12 |
| difference | 1 | 4.3 | 77 % | 198 | 2.7 | 0 of 1 |

- 20 of 60 are above 5 s for one boolean. The fuzzer's 4.7-16.4 s ran two booleans per unit, plus start-up.
- None reaches the evaluation limit.
- Every unit spans 2^839 to 2^1934 in the exponents of its nonzero coordinate magnitudes. The largest magnitude lies
  between 2^-143 and 2^907.
- The BigInteger probe (`probe_bigint.py` on master) finds BigInteger stages in every unit. They take 270 of 317 s in
  total (85 %), median 4.2 s per unit (`results/fuzz_units_bigint_probe_2026-11-12.txt`). All 60 are therefore the
  class of the #711 timeout (section 4.1).
- Since none reaches the limit, a lower plain budget would not cap them. A cost-weighted budget would (lever 3, since
  dropped in favour of P2, which removes the same cost bit-identically).

**grp-b, 10 timeouts and 53 slow units** (`robust-repair,robust-repair-soup,rounding-repair`; `MeshRobustRepair` and
`MeshRoundingRepair`, not `ExactMeshBoolean` directly). Each unit is replayed whole with `MeshFuzzPilot replay` under
`/usr/bin/time`, the target's check included (`results/fuzz_units_grp_b_2026-11-12.tsv`).

| kind | target | units | wall s min / median / max | peak RSS MB max | rc |
|---|---|---|---|---|---|
| timeout | robust-repair | 4 | 6.1 / 7.7 / 12.3 | 523 | 0 |
| timeout | robust-repair-soup | 6 | 6.2 / 14.8 / 49.1 | 1743 | 0 |
| slow | robust-repair | 18 | 2.5 / 4.9 / 5.1 | 299 | 0 |
| slow | robust-repair-soup | 31 | 1.2 / 3.5 / 6.5 | 336 | 0 |
| slow | rounding-repair | 4 | 1.8 / 5.7 / 7.5 | 174 | 0 |

Only 5 of the 63 run BigInteger stages at all, for 0.02-7.0 s. The long soup timeouts run none, so grp-b is a separate
cause (#712). Its profile is in section 4.8.

## 4. Groups by dominant stage, and what the time is

Profiles: `dotnet-trace collect --profile dotnet-sampled-thread-time --format Speedscope -- dotnet <tool>/BooleanComparison.dll profile --repeat 1 --safe-rounding --ops <op> <case>`,
read with `speedscope_top.py` (self and inclusive share) and `speedscope_callers.py`. **What a share is:** the share of
the library's samples on all threads whose state is "running managed code" (the sampler's CPU_TIME; blocked threads
are left out). It is a CPU share, not a wall share. Two caveats:
- The sampler counts runnable threads, not cores: 42327 intersection shows 486 s of such samples on 26 threads for
  90.6 s of user CPU (`/usr/bin/time`).
- Serial phases weigh less than in wall time. 1368057 union: rounding is 51 % of the wall clock but 25 % of the
  samples.

Region timers summed over threads have the opposite problem. Threads wait inside the timed regions (collections,
allocation, more runnable threads than cores), so the exact-numbers study saw such sums exceed the process CPU time by
up to 170 % on 42327. Its shares by representation are therefore measured on one thread (`gParallel.SerialOnThisThread`,
`taskset -c 0`, `DOTNET_gcConcurrent=0`; branch `runs/exact-numbers`, REPORT.md).

A first version of this note filtered the samples by `ExactMeshBoolean.Compute`, which keeps only the calling thread
(the workers of the parallel loops start in the thread pool). Every share below is over all threads.

### 4.1 Rounding on a wide exponent span (#711)

The timeout input of #711 (difference; 16 + 16 vertices, 762 pieces, 152 result triangles): rounding 15.5 of 16.3 s,
13.6 GB allocated, `RoundingClean` false, the evaluation limit reached. The rounding trace of #721's `instrument.py`
(it applies to this tree unchanged): 13 333 evaluations of proposed edits, 768 k pair tests in them, 17.5 s, about
16 µs per pair test (the first full search: 2151 pairs, 5.8 µs each). The sampling profile (all threads): `orient3d_bigint`
50 % inclusive, `orient2d_bigint` 19 %, `BigInteger.Multiply` 64 % inclusive. The probe's own timer gives 14.5 of 16.3 s
(89 %; the exact-numbers study measured 86 %).

Why BigInteger: the exact stage of `Orient3d` tries the 64-bit integer path (`orient3d_int`, coordinate differences
within 62 bits of their lowest set bit), then Shewchuk's expansions if every coordinate lies in [2^-100, 2^100]
(`Expansion.InRange`), else `to_integers` and BigInteger. Corners at 1.8e-284 (2^-943) next to 1e-32 give integers of
about 900 bits. The probe (`probe_bigint.py`, `G3_BIGINT_PROBE=1`):

| operation | orient3d BigInteger calls | ms | exactly zero | span <= 2^200 | leading-term certificate decides | orient2d calls | ms |
|---|---|---|---|---|---|---|---|
| difference | 1 792 091 | 10 110 | 1 978 | 52 554 (2.9 %) | 25 093 (1.4 %) | 4 235 156 (24 % span <= 2^200) | 4 439 |
| union | 83 390 | 817 | 1 016 | 264 | 4 419 | 230 923 | 413 |

Variants of the input (`scale_case.py`): scaled by 2^106 (all magnitudes near 1): 16.4 s, the same; the 1e-284
coordinates set to 0: 50 ms, `RoundingClean` true. The span is the cause, not the absolute scale. On the rotated
Thingi10K cases 1368057, 486343 and 42327 (union) the probe counts **no** BigInteger stage at all.

### 4.2 Classification of large open operands (FWN path today, Antipodal under option A)

117959, 1087141 and its siblings, 112798, 1005586, 122796, 124526: classification 61-90 % of the wall time. Today they take the
fast-winding-number path (the switches route them away from Antipodal). Profile of 117959 union and 1087141 union:
`DMeshAABBTree3.branch_fast_winding_num` 90 % and 88 % of the samples (61-69 % on 122796, 1005586,
112798). The FWN probe (`probe_fwn.py`): two queries per piece
(1 574 056 pieces of 1087141 -> 1 574 056 queries against the piece's own mesh and as many against the other; the own
queries are about 56 % of the FWN time).

Under option A the same models take L2's traced Antipodal path. Profile of 117959 union with A: `antipodal_value` 70 %
self (the boundary sum: 6007 segments per traced evaluation, 925 k traced pieces), `trace_open_winding` 96 % inclusive.
1087141 union with A: `antipodal_value` 30 %, GC suspension 13 %, `carried_crossings` 10 % inclusive, BigInteger
construction 6 %, `orient3d` on `ExactPoint3` 3 % inclusive. 122796 union with A: `antipodal_value` 52 %. The per-patch L1 decision (`l1_patch_decided`) is 0 in
every row: it runs only where L2 tracing is off (`TracedOpenWinding == false`), so it is superseded, not a lever.

### 4.3 Classification with many coplanar or self-intersecting pieces (42327, 107554, 113906, 498458, 1368057 int / diff)

These take the Antipodal path already (both operands; 42327: 1.03 M pieces, 9.7 M winding plane sides). 42327
intersection, sampling profile: `carried_crossings` (L2's crossing count) 37.5 % inclusive; BigInteger construction 22.5 %
self; `Monitor.Enter` slow path 13.5 % self, almost all of it in `ExactMeshArrangement.AddPoint` (12.8 % inclusive),
called from `classify` (the pieces' centroid points, 2.0 M calls); `orient3d` on `ExactPoint3` 11.9 % inclusive (its
`BigInteger[4,4]` 6.3 %); `FixedArith.Mul` 8.4 %; GC suspension 7.9 %.
Classification allocates 18 GB on 42327 union; GC pauses are 23-27 % of these operations (section 4.4).
hotspots.md's `coplanar_stack` figure (72 % of classification on the raw 42327, 2026-11-02, master c419546) is from the
old classification; on
the rotated pair today `inside_coplanar` is 3.8 % self.

### 4.4 Allocation and GC (across the groups)

GC pause time is 11.0 % of the inventory's 2412 s (the 66 operations); the largest shares: 107554 26.6 % (difference) and 26.3 %
(intersection), 498458 intersection 25.8 %, 55928 difference 24.1 % (91 s of pauses, 199 GB allocated), 42327
23.2-23.7 %. A/B of the GC configuration (one process each, intersection; hashes equal in every row):

| case | default s / pause ms | server GC | gen0 512 MB |
|---|---|---|---|
| 107554 | 20.5 / 5256 | 16.6 / 1035 (-19 %) | 20.8 / 4530 |
| 42327 | 29.7 / 6733 | 26.3 / 1387 (-11 %) | 33.2 / 7554 |

### 4.5 Intersections (519511, 1517923, 1063860, 668056; 20.3 s at most)

intersections 25-57 % of 1.9-20.3 s. 519511 union profile: `TriangleBoxTree.Query` 49 % inclusive (19 % self), GC suspension 8 %,
`ExactPredicates.Orient3d` 8 %, `self_pairs` 7.5 %, `TriangleTriangleFilter.by_projection` 7 %. The scaling pre-study puts the stage at
2.95x on 4 cores (`benchmarks/Boolean30Prestudy/results/scaling_summary.md`, master c70e08b). Only 519511 is above
10 s (14.3-20.3 s); 1517923 is at most 6.1 s.

### 4.6 n-ary one pass

boxes_64_1 (the CSG pre-study's worst case): one pass 38.4 s (classification 34.8 s, 158 M winding plane sides,
1.13 M pieces from 768 input triangles; triangulation 2.6 s) against 0.57 s chained. grid:10 (2000 meshes) 6.1 s
(27 s in the roadmap's first measurement).

### 4.7 The 55928 tail case

Difference 380 s (356 s in #675's note at 896e2064, 370-425 s before), classification 59 %, triangulation 30 %, 199 GB allocated,
91 s of GC pauses, peak 11.8 GB. Its levers have their own note (`docs/design/EXACT_BOOLEAN_TAIL_55928.md`); it is not
profiled again here. It is the largest single row; section 4.4 applies to it.

### 4.8 Shared profile table (exact / BVH / GC / rounding / rest)

Owner request 2026-11-12: one table in the format of the AW3 study, to merge into hotspots.md.

**Commit sets.**
- The rows of section 3.1 (all but the grp-a unit): master `8124a7d3` + #675 `a9b00da3`, merged locally.
- The traces, the grp-a unit and grp-b: master `0325530c`, which contains #675. Its hashes equal the earlier rows on
  the three cases of the spot check.
- The normal stratum: master `cbf8b787` (section 8).

**How the shares are measured.**
- One `dotnet-trace` sampling profile per case (`traces_job.sh`), classified by `speedscope_categories.py`.
- The samples are the library's: those with a geometry3Sharp frame, on all threads, without the mesh readers. They
  include the warm-up booleans of `profile`, which are negligible except in the normal stratum, where they are
  subtracted. These are CPU shares (section 4, "What a share is"), not wall shares; the wall shares of the stages are
  in section 3.1.
- Each sample goes to the first category found from the leaf upwards:
  - (i) exact: the exact stage, split from (i) filters (the floating-point filters and the predicates' entry points);
  - (ii) AABB / BVH queries, fast winding numbers included;
  - (iii) GC, trace: the runtime's GC and allocation frames, mostly threads waiting in `PollGC`. It counts every
    waiting thread, so it is larger than the wall-clock pause share;
  - (iv) rounding: the rest of `MeshRoundingRepair`;
  - (v) rest.

**The other columns.**
- (iii) GC pause, wall: the GC pause time of the profile row over its total.
- (iii) alloc GB: the bytes allocated in that row.
- "largest in rest": the largest self frame within (v), in % of all CPU.

**Normal stratum.** The 36 unions of section 8, CPU-weighted over their traces. The warm-up booleans are subtracted
using the trace of the smallest case: 1582414 takes 20 ms itself and shows 384 ms of library samples.

| case | op | total s | (i) exact stage | (i) filters | (ii) AABB / BVH | (iii) GC, trace | (iii) GC pause, wall | (iii) alloc GB | (iv) rounding | (v) rest | largest in rest (self, % of CPU) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| #711 timeout | difference | 16.3 | 83.0 % | 4.5 % | 0.3 % | 9.2 % | 1 % | 13.5 | 2.4 % | 0.6 % |  |
| #711 grp-a unit 0edbe479 | intersection | 6.6 | 67.1 % | 3.4 % | 0.6 % | 22.4 % | 0 % | 3.7 | 4.5 % | 2.1 % |  |
| 55928 | difference | 380.4 | 24.8 % | 12.4 % | 11.0 % | 4.7 % | 24 % | 199.4 | 0.0 % | 47.1 % | carried_crossings 16.2 |
| 117959 | union | 114.2 | 2.9 % | 0.2 % | 91.3 % | 0.6 % | 4 % | 9.3 | 0.3 % | 4.7 % |  |
| 117959 (A) | union | 106.3 | 3.1 % | 1.4 % | 4.3 % | 2.7 % | 10 % | 19.5 | 0.6 % | 87.9 % | antipodal_value 69.6 |
| 1087141 | union | 110.8 | 2.5 % | 0.9 % | 90.1 % | 0.9 % | 3 % | 7.7 | 0.9 % | 4.6 % |  |
| 1087141 (A) | union | 49.1 | 9.2 % | 6.1 % | 11.8 % | 15.5 % | 18 % | 20.2 | 3.4 % | 54.0 % | antipodal_value 29.6 |
| 1005586 | union | 74.1 | 7.7 % | 0.6 % | 73.2 % | 2.8 % | 11 % | 16.8 | 3.6 % | 12.0 % |  |
| 112798 | union | 58.1 | 7.8 % | 0.7 % | 74.6 % | 1.9 % | 11 % | 12.3 | 2.3 % | 12.7 % |  |
| 122796 | union | 12.6 | 12.5 % | 0.8 % | 68.6 % | 3.8 % | 13 % | 2.6 | 0.7 % | 13.5 % |  |
| 122796 (A) | union | 19.2 | 5.8 % | 2.3 % | 6.3 % | 8.4 % | 14 % | 4.7 | 0.2 % | 77.0 % | antipodal_value 52.3 |
| 42327 | intersection | 34.3 | 45.9 % | 1.0 % | 2.3 % | 14.2 % | 24 % | 20.6 | 1.0 % | 35.6 % | lock waits 13.5 (AddPoint 12.8 incl.) |
| 1368057 | union | 39.2 | 25.4 % | 6.2 % | 23.8 % | 8.5 % | 8 % | 12.8 | 12.1 % | 24.0 % |  |
| 107554 | intersection | 24.2 | 18.4 % | 2.8 % | 17.1 % | 34.4 % | 26 % | 10.0 | 1.0 % | 26.3 % | lock waits 11.5 |
| 519511 | union | 20.3 | 9.9 % | 17.0 % | 32.2 % | 8.4 % | 11 % | 6.4 | 6.8 % | 25.6 % | self_pairs 7.5 |
| boxes_64_1 n-ary (one process: chained and one pass) | one pass | 38.4 | 22.1 % | 0.2 % | 13.8 % | 57.4 % | - | - | 0.0 % | 6.6 % |  |
| **normal stratum**, 36 unions (section 8; master cbf8b787; warm-up subtracted) | union | 21.4 in total (median 0.29) | 7.6 % | 8.9 % | 17.5 % | 29.9 % | 8.6 % | 217 MB per op (median 50) | 8.5 % | 27.6 % | |
| grp-b, the 10 repair timeouts (#712; all threads of the `MeshFuzzPilot` replay) | robust-repair / soup | 6.1-49.1 wall | 0.8 % | 10.2 % | 3.2 % | 19.6 % | - | - | 0.0 % | 66.1 % | Reducer and alpha wrap (simplify_wrap) |

**Reading.**
- **(i) matters in five places:** #711 and its grp-a units (67-83 % of the samples; 85-89 % of the wall time by the
  probe's timer, all BigInteger), 42327 (46 %), 1368057 (25 %), 55928 (25 %) and boxes_64 (22 %). In the normal
  stratum it is 7.6 % plus 8.9 % filters; the exact-numbers study finds 5-6 % on its own normal set. Predicate-kernel work (eg an InCircle limb
  kernel, filter tuning) would pay in those cases, not in the normal stratum.
- **(ii) dominates the default path of the large open models** (69-91 %: the fast-winding tree). Option A removes most of
  it, and with option A the time moves into (v), into `antipodal_value`, which is lever 1.
- **(iii) GC** is a large share of the samples: the normal stratum 30 % (8.6 % wall pauses), 107554 34 %, the n-ary
  one pass 57 %, grp-b 20 %. The samples count every thread waiting for a collection, so this column overstates the wall
  cost; the pause column is the wall cost.
- **grp-b (#712) is not an `ExactMeshBoolean` cost.** It is `MeshRobustRepair`'s alpha wrap and `Reducer` under heavy
  GC. It is passed to the wrap sessions.

## 5. Levers

Upper bound = the share of the stage or function the lever removes, times the best possible speedup of that part,
measured on the named rows. A lever whose bound is under 10 % of its group's time is closed without an A/B (brief).

### 5.1 Proposed, ranked by gain per effort

| # | lever | group | measured upper bound (command) | falsification | cost | risk |
|---|---|---|---|---|---|---|
| 1 | **Hierarchical Antipodal boundary sum** (owner 2026-11-12: T3a's queue, after #711). The open winding's boundary term is summed over all B segments per traced evaluation. Order the segments into chains, build a binary tree over each chain, and replace a cluster far from p (radius / distance < θ, and the ray x1 missing the cluster's cone) by the term of its chord plus the dipole of the closed polygon (Barnes and Hut 1986; the dipole as in Barill et al. 2018). With a certified far-field bound added to the margin that L2 already uses, a piece whose margin does not clear falls back to today's full sum, so decisions stay those of today | 4.2 under option A | `antipodal_value` 70 % of the CPU samples of 117959 union (A), 30 % of 1087141 union (A) and 52 % of 122796 union (A) (profiles above; a CPU-share bound: in wall time it is capped by the classification phase, 92.6 of 106.3 s and 31.2 of 49.1 s); terms per evaluation 23 of 6007 (117959) and 26 of 1196 (1087141) at θ = 0.25 | `probe_boundary_sum.py` (300 sampled points per model): error modulo whole turns at most 3.4e-4 (117959, θ = 0.25), 8.6e-14 (1087141); at θ = 0.5 2.5e-3 and 3.3 % whole-turn jumps, so θ about 0.25 and the integer bookkeeping are design items | M-L: `mesh_ops/ExactMeshBoolean*` (locked for 3.0 work, exact-code review of the new bound, the classification mutation shards) | low if the fallback keeps decisions; the whole-turn bookkeeping of the chord identity must be proven (the probe shows 0-0.3 % jumps at θ <= 0.25 with the cone test) |
| 2 | **Option A** (lift both switches; owner decision 2026-11-12, follow-up PR after #675) | 4.2 | the open union group 437 -> 349 s (-20 %); 1087141 -56 % (its four siblings are the same part and should follow, not run) | measured, section 3.3; hashes change (Antipodal is right where FWN and Antipodal differ, #675) | decided | 122796 +52 % (1.5x); lever 1 targets exactly that cost |
| 3 | ~~**Cost-weighted rounding budget.**~~ **Dropped by the owner (2026-11-12) in favour of P2**, the allocation-free kernel of the exact-numbers study, which targets the same 85 % BigInteger share bit-identically (on the 60 grp-a units plus the timeout unit: 298 -> 64 s, median 4.21 -> 0.80 s, none above 5 s; dense limbs 2.5-2.7x per call, the sparse kernel 12-25x); re-measured after P2. As it was proposed: count a BigInteger exact stage of the rounding's pair tests as k pair tests toward `MaxPairTests` (eg k = 20-100), or stop the evaluations once the BigInteger time exceeds a share of the rounding | 4.1 | rounding 95 % of the #711 timeout op; `profile --rounding-max-pair-tests` 5M / 1M / 250k / 50k: rounding 15.6 / 12.0 / 3.0 / 0.66 s, `RoundingClean` false in every row (152-156 result triangles) | the knob shows the time follows the budget; 0 BigInteger stages on 1368057, 486343, 42327 (union) with `probe_bigint.py`, so those results stay bit-identical | S: `mesh_ops/MeshRoundingRepair.cs` (the rounding shards) | results change only on inputs whose rounding needs BigInteger; an owner decision (it changes results, as #335 did). Checked on the 60 slow units: all are this class (85 % BigInteger), none reaches today's limit, so only the weighting caps them |
| 4 | **Server GC as documented advice for large booleans** (OPERATIONS.md, added in this PR), and the roadmap's allocation task (owner 2026-11-12: queued with T3a after #711) ("Boolean allocations", owner 2026-11-04), whose trigger (>= 10 % on large inputs) these data meet | 4.4 | GC pauses 11.0 % of the inventory, 23-27 % on 107554, 498458, 42327, 55928 | A/B, section 4.4: server GC -19 % (107554) and -11 % (42327), hashes equal; a 512 MB gen0 gains nothing | S for the advice; M for the allocation PR (largest allocators first: classification on 42327, 18 GB; `carried_crossings` BigInteger construction, section 4.3) | none for the advice (more memory with server GC); the allocation PR bit-identical by design |
| 5 | **n-ary one pass: the pre-study's levers, with today's numbers** (propagation of the classification, pruning before triangulation, a tree-aware evaluation; EXACT_BOOLEAN_30_PRESTUDY.md section 3) | 4.6 | classification 91 % of boxes_64_1's 38.4 s; the pre-study's propagation bound: 64 -> 4.9 s, still 8x the chain | not re-probed (the pre-study's bound stands) | M-L | n-ary is a 3.0 use case (T3a / T3b); boxes_64 is its worst case, not its typical one |

### 5.2 Candidates with a measured bound but no A/B yet (not proposed)

| lever | bound | what is open |
|---|---|---|
| BigInteger construction in `carried_crossings` (L2's crossing count between pieces) | `carried_crossings` 37.5 % inclusive of 42327 intersection's samples (BigInteger construction 22.5 % self), 10 % of 1087141 union (A) | which values are built per call and whether a fixed-width or reused representation decides the same signs; part of lever 4's allocation work |
| `ExactMeshArrangement.AddPoint` from `classify` | 12.8 % inclusive of 42327 intersection's samples (nearly all of it in `Monitor.Enter`'s slow path), 2.0 M calls | the shards are evenly used (largest 1.6 % of the calls, 64 of 64 used: `probe_shards.py`), so the lock waits are not a hash bias; whether the centroid points need the shared index at all is not diagnosed |
| A faster wide-integer kernel for the BigInteger stage | 87-89 % of the #711 op is in the BigInteger stage | measured since by the exact-numbers study and chosen by the owner as P2 (lever 3's row) |
| The intersections stage of 519511 / 1517923 | 25-57 % of operations of at most 20 s; GC 14 %, `self_pairs` 12 % of 519511 union | below the extreme range of this note; levers 4 (GC) applies |

### 5.3 Closed, with their bound

The owner's rule of 2026-11-12 (AGENT_WORKFLOW.md section 8) keeps a lever below 10 % when it is measured never slower
and gains measurably somewhere. So each row below names why it is closed: no measurable gain, or dominated (another
lever covers the same work faster).

| lever | bound | how measured |
|---|---|---|
| Leading-term certificate for the BigInteger stage (flush coordinates below 2^(Emax-200) to 0, keep the sign of the flushed determinant if it exceeds the flushed parts' bound) | dominated by P2 (the same BigInteger calls, all of them, faster). Decides 1.4 % of the orient3d BigInteger calls of #711 (25 093 of 1.79 M), 266 ms of their 9 959 ms, and 70 ms of the orient2d calls' 4 418 ms: 0.34 s of 16.3 s (2 %; master cbf8b787 rerun) | `probe_bigint.py` (0 wrong signs; the time of the decided calls: `certified_ms`) |
| Scale by 2^k into the expansion range (a relative range check instead of the absolute [2^-100, 2^100]) | dominated by P2. Admits 2.9 % of the orient3d BigInteger calls of #711 (about 0.3 s) and 24 % of the orient2d calls (about 1.1 s): together about 1.4 s of 16.3 s (8.3 %) | `probe_bigint.py` (span <= 2^200) |
| Stop the rounding passes at the evaluation limit (#721 option 1) on #711 | no measurable gain: rounding 15.9 vs 16.2 s, the same hash | #721's `instrument.py`, `G3_STOP_AT_LIMIT=1`: the work is before the limit |
| `orient3d` on `ExactPoint3` without the `BigInteger[4,4]` matrix | no measurable gain (within the noise): 42327 intersection -4 % / -1 % (two rounds, 30.2 / 29.5 -> 28.9 / 29.3 s), 1087141 union (A) +3 % / +0.4 %; hashes equal | `probe_det4.py`, A/B of two builds of the same tree |
| Shard hash mixing in `AddPoint` | no measurable gain: no bias to remove (largest shard 1.6 %) | `probe_shards.py`, `G3_SHARD_PROBE=1` |
| L1 per-patch open winding | no gain possible: never runs (L2 tracing supersedes it): `l1_patch_decided` 0 in all 74 rows | inventory counts |
| FWN per piece on the default path (two tree queries per piece, 88-90 % of 117959 / 1087141) | closed by decision, not by the stop rule: option A (owner 2026-11-12) moves these models to the Antipodal path | section 4.2 |
| JIT settings for cold single calls | a host setting, not a library lever: `TieredCompilation=0` -0.5 s of 7.6 s (about 7 %); gen0 256 MB none | section 2 |
| Rounding levers of SNAP_ROUNDING_3D.md 4.2 (stop at the limit, `near`, box-neighbour enumeration, setup; certified empty ball, clearance skins, separating-axis prefilter) | measured there; not repeated | #721 |

## 6. Recommendation

In order: lever 2 (decided) with lever 1 behind it in the same area (it removes option A's regression on 122796 and
most of 117959's remaining time; its design needs the far-field bound and the whole-turn bookkeeping); lever 3 is dropped in favour of P2 (the
60 slow fuzz units are checked: the same cause, so P2 covers the whole #711 class; re-measure after P2); lever 4's advice at once
and its allocation PR when the 3.0 locks allow. Lever 5 stays with the n-ary plan. With levers 1 and 2 the open union
group would fall from 437 s to roughly 250 s. This applies lever 1's CPU-share bound to the wall times of the three
models profiled under A, as an estimate; the other five are not profiled under A. 117959 would remain near 35-40 s,
and 55928 is untouched.

## 7. Reproduce

```
W=<work dir>; T=<tool dir>                     # tool: dotnet build benchmarks/BooleanComparison -c Release -o $T
python3 benchmarks/ExtremeBooleans/fetch.py $W/meshes 67709 42327 ...                # the ids of section 3
for f in $W/meshes/*.stl; do dotnet $T/BooleanComparison.dll export $f $W/cases/rot/$(basename $f .stl); done
git show origin/runs/mesh-fuzz:runs/mesh-fuzz/grp-a/dump-timeout-251fe028ac01b4b0db737de05913b7a23a3b4450.txt > $W/d.txt
python3 benchmarks/ExtremeBooleans/dump2off.py $W/d.txt $W/cases/fuzz/t251fe028
cp benchmarks/ExtremeBooleans/{job.sh,run.sh} $W/ && $W/job.sh $T $W        # copy first: bash reads a running script lazily
python3 benchmarks/ExtremeBooleans/summarize.py $W/inventory.jsonl --md
# probes: a throw-away worktree of the same commit, then one of
python3 benchmarks/ExtremeBooleans/probe_bigint.py <wt>/math/ExactPredicates.cs          # G3_BIGINT_PROBE=1
python3 benchmarks/ExtremeBooleans/probe_fwn.py <wt>/mesh_ops/ExactMeshBoolean.cs        # after probe_bigint.py
python3 benchmarks/ExtremeBooleans/probe_shards.py <wt>/mesh_ops/ExactMeshArrangement.cs # G3_SHARD_PROBE=1
python3 benchmarks/ExtremeBooleans/probe_det4.py <wt>/mesh_ops/ExactMeshBoolean.cs       # A/B against an unpatched build
python3 -I benchmarks/ExtremeBooleans/probe_boundary_sum.py $W/cases/rot/117959/A.off $W/cases/rot/117959/B.off 300 0.1 0.25 0.5
# fuzz units (section 3.4): units from runs/mesh-fuzz grp-a/units and grp-b/units into $W/units/grp-a, grp-b; per grp-a unit
#   MESH_FUZZ_OPS=union,intersection,difference dotnet <MeshFuzzPilot>/MeshFuzzPilot.dll dump <unit> > d.txt
#   python3 benchmarks/ExtremeBooleans/dump2off.py d.txt $W/cases/fuzza/<sha1>     (prints the op: "<sha1> <op>" lines into $W/fuzza_ops.txt)
cp benchmarks/ExtremeBooleans/{units_job.sh,run.sh} $W/ && $W/units_job.sh $T <MeshFuzzPilot dir> $W
# shared profile table (section 4.8) and the normal stratum (section 8)
cp benchmarks/ExtremeBooleans/{traces_job.sh,run.sh,normal_job.sh} $W/
$W/traces_job.sh $T <MeshFuzzPilot dir> $W <normal ids>; $W/normal_job.sh $T $W <case dirs of section 8>
python3 benchmarks/ExtremeBooleans/speedscope_categories.py $W/traces/*.speedscope.json
```

## 8. The normal stratum: phase breakdown and levers (owner addendum 2026-11-12)

Typical operations, not the tail: what the time of an ordinary boolean is spent on, and which lever per phase is worth
what.

**Stratum.** 36 unions, each of a model with its rotated, shifted copy (`BooleanComparison export`):
- 20 Thingi10K models with 2k-50k triangles, drawn with `random.seed(20261112)` from `geometry_data.csv`: 104969,
  131971, 1722409, 232911, 237631, 239188, 281457, 314592, 363052, 44106, 501151, 56483, 669966, 67927, 68203, 740249,
  779964, 80105, 82256, 888459.
- 14 of 20 models drawn with the same seed from the Boolean30 pre-study's scaling set
  (`benchmarks/Boolean30Prestudy/results/scaling_ids.txt`). Six of the 20 were left out as above 5 s: 1041432, 519507,
  668056 and 668057 (5.3-5.6 s), 1088051 and 1088138 (about 100 s, the extreme group).
- The pre-study's procedural pair cases `cross150` (178 800 triangles) and `sph300` (717 600), from
  `t2/gen_spheres.py`.

**Command.** Master `cbf8b787`, 4 cores. `normal_job.sh` runs
`profile --repeat 5 --median --safe-rounding --ops union <case>`, one process per case, after its own warm-up booleans.
The row is the median of the five runs by time: its phases, its GC pause and its allocations. Then the job runs one
sampling profile per case. Raw rows: `results/normal_stratum_median5_2026-11-12.jsonl`; trace categories:
`results/normal_stratum_categories_2026-11-12.jsonl`. All 36 are deterministic (5 equal hashes each).

**Shares.**
- Phase shares are of the wall time, from the library's phase clocks. They are summed over the ops ("time-weighted")
  and, in the last column, taken as the median of the per-op shares.
- The phases include their GC pauses. The GC row is the pause time of the same runs, as a share of the wall time.
- The trace categories (section 4.8) are CPU-sample shares with the GC waits included.

| phase | normal Thingi10K, 20 models | Boolean30 scaling set, 14 models | procedural (cross150, sph300) | all 36 | all 36, median over the ops |
|---|---|---|---|---|---|
| ops; wall s in total (median op) | 20; 8.24 (0.29) | 14; 10.88 (0.16) | 2; 2.31 (1.16) | 36; 21.43 (0.29) | |
| input preparation | 1.4 % | 0.4 % | 0.7 % | 0.8 % | 0.9 % |
| setup (points, triangles) | 2.1 % | 1.9 % | 6.8 % | 2.5 % | 2.1 % |
| box trees | 2.0 % | 1.5 % | 4.2 % | 2.0 % | 2.1 % |
| pair search and triangle tests | 12.8 % | 19.8 % | 26.8 % | 17.9 % | 13.6 % |
| planes | 1.1 % | 1.0 % | 0.6 % | 1.0 % | 1.3 % |
| intersection points | 1.3 % | 2.9 % | 0.6 % | 2.0 % | 1.6 % |
| merge (exact points) | 5.1 % | 17.2 % | 2.8 % | 11.0 % | 7.3 % |
| triangulation | 5.3 % | 10.2 % | 1.8 % | 7.4 % | 8.0 % |
| classification | 35.0 % | 8.5 % | 12.9 % | 19.2 % | 6.1 % |
| ExactPlanarSimplify | 1.9 % | 2.6 % | 1.6 % | 2.2 % | 2.8 % |
| assembly | 9.1 % | 8.9 % | 15.7 % | 9.7 % | 11.5 % |
| rounding | 22.5 % | 25.0 % | 25.2 % | 24.0 % | 28.8 % |
| GC pauses, wall (inside the phases above) | 4.6 % | 12.4 % | 4.8 % | 8.6 % | 1.4 % |
| allocated MB per op, mean (median) | 106 (46) | 312 (48) | 656 (656) | 217 (50) | |

**Cross-cutting, CPU samples of the same 36** (all threads, warm-up subtracted):

| exact stage | filters | AABB / BVH queries | GC waits | rest of the rounding | rest |
|---|---|---|---|---|---|
| 7.6 % | 8.9 % | 17.5 % | 29.9 % | 8.5 % | 27.6 % |

**What drives the shares.**
- Rounding is the largest phase in all three groups: 22-25 % of the wall time, a median of 29 % per op.
- The pair search and triangle tests come second: 13-27 %.
- The time-weighted classification share comes from two open models on the Antipodal path. 56483 is 84 % of its 2.5 s,
  80105 60 % of its 0.6 s. On the closed models classification is 6-13 %.
- Merge is driven by 1017018: merge 54 % of its 2.0 s, GC 27 %.

**Lever candidates per phase.** Bound = the time-weighted wall share times (1 - 1/plausible speedup); with "free" for
the ceiling where it matters.

| phase (share, time-weighted / op median) | lever candidate | plausible speedup | upper bound on the stratum | effort | bit-identical | already covered |
|---|---|---|---|---|---|---|
| rounding (24.0 % / 28.8 %) | the first full search's box-neighbour enumeration and `near` (SNAP_ROUNDING_3D.md section 5.5, its option 3) | 1.5x on the rounding | 8 % | M | yes | snap rounding session (#721 option 3, after #725) |
| classification (19.2 % / 6.1 %) | on the open models (Antipodal path): lever 1, the hierarchical boundary sum; on closed models nothing measured | 2x on the open models' classification (2.5 of 4.1 s) | 6 % | M-L | decisions kept by the fallback | **T3a queue (lever 1)** and option A |
| pair search and triangle tests (17.9 % / 13.6 %) | box-tree traversal and the pair filter (`TriangleBoxTree.Query`, `TriangleTriangleFilter`) | 1.5x | 6 % | M | yes | none |
| planes and intersection points (3.1 % / 2.9 %; the rest of the intersections stage with the pair search and the box trees) | the planes of the intersecting triangles and the implicit points | 1.5x | 1 % | M (exact code) | yes | partly C' (planes, exact-numbers study) |
| GC pauses (8.6 % / 1.4 %) and 217 MB per op | fewer allocations (lever 4); server GC on the host | allocation-free in the largest phases: at most the pause share | 8.6 % ceiling | M | yes | **T3a queue (lever 4)**; OPERATIONS.md advice |
| exact stage across phases (7.6 % of the samples) | P2 gains nothing here: the exact-numbers study counts 0 calls of the `*_bigint` paths in its 99 normal operations. What remains is the exact point construction (its candidate C, on stack limbs) and the planes (C') | C 1.8-2.4x on stack limbs, no allocation | C: 2.0-5.0 %, C': 0.9-1.6 % (exact-numbers study, single thread) | M (exact code) | yes | exact-numbers study (C, C'); not P2 |
| merge (11.0 % / 7.3 %; `merge.points` 65 % of it) | the exact-point merge (sort and dedupe), its allocations | 1.5x | 3.7 % | M | yes | partly lever 4 (1017018 is GC-bound) |
| assembly (9.7 % / 11.5 %) | building the result mesh | 1.5x | 3.2 % | M | yes | none (T1 lever 3 dropped it at 3-4 % of large operations) |
| triangulation (7.4 % / 8.0 %) | the per-triangle CDT | 1.5x | 2.5 % | M | yes | none |
| input preparation, setup, box trees (5.3 % / 6.1 %) | already done in T1 (#631) | - | < 2 % | - | - | T1 |
| ExactPlanarSimplify (2.2 % / 2.8 %) | not diagnosed | 1.5x | 0.7 % | M | yes | none; a candidate under the owner's rule, lowest gain per effort |
| filters (8.9 % of the samples) | filter tuning | 1.3x | 2 % | M (exact code: bound review) | yes | none; a candidate under the owner's rule |

**Ranking by gain per effort**, with what is already queued marked so that nothing is counted twice:
1. The rounding search: 8 %, M, bit-identical; snap rounding session.
2. Lever 4, allocations: up to 8.6 %, M; T3a queue.
3. Lever 1 on the open models of the stratum: 6 %; T3a queue.
4. The pair search: 6 %, M, bit-identical; **the one uncovered lever with a gain of this size.**
5. C and C', the point construction and the planes on stack limbs: 2-5 % and 1-1.6 %; exact-numbers study. P2: 0 on
   this stratum.
6. Merge 4 %, assembly 3 %, triangulation 2.5 %, filters 2 %, ExactPlanarSimplify 0.7 %.

Under the owner's rule of 2026-11-12 (AGENT_WORKFLOW.md section 8: build what is measured never slower, normal stratum
and tail separately, median of 5, and gains measurably somewhere, even below 10 %), the items of point 6 are not dropped.
They are candidates in this order of gain per effort; each one is built only after its own never-slower A/B.

No single lever reaches 10 % of the normal stratum: the time of a typical boolean (median 0.29 s) is spread over five
phases of similar size. Together the queued items (rounding search, lever 4, lever 1) and C / C' bound about 20-25 % of
the stratum. The bounds overlap partly: the GC pauses sit inside the phases.
