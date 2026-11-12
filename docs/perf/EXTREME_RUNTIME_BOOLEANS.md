# ExactMeshBoolean: the examples with extreme run time, profiled, and the levers left

Research note (owner task 2026-11-12). Every known `ExactMeshBoolean` example with an extreme run time, measured on one
commit set, split by stage, grouped by its dominant stage, and a ranked list of levers with a measured upper bound each.
Cheap levers were falsified before they are listed; closed levers are listed with their bound. No library code changed:
the probes are scratch patches applied to a throw-away worktree (`benchmarks/ExtremeBooleans/probe_*.py`), as
`benchmarks/SnapRoundingExperiment/trace/instrument.py` does. Thingi10K meshes are fetched at run time, never committed.

## 1. Result in short

- **66 operations, 2354 s** (the rows of section 3.1 without the n-ary CSG pre-study rows and the 8 option-A rows): classification 68.5 %, triangulation 8.4 %, rounding
  8.3 %, intersections 6.1 %, merge 4.9 %, assembly 2.2 %. GC pauses are 11.3 % of it, up to 24-27 % on single cases.
- **The extreme cases are classification cases.** 34 of the 40 operations above 10 s are dominated by classification:
  the large open models (117959, 1087141 and its four siblings, 112798, 1005586: 47-111 s, 64-93 % classification),
  55928 (380 s), 42327 and the Antipodal models 107554 / 113906 (21-40 s). The rest is rounding (1368057 union 39 s,
  the fuzz case of #711) and, below 20 s, intersections (519511, 1517923).
- **#711 is BigInteger arithmetic in the rounding.** On the timeout input 87 % of the time is in `orient3d_bigint` /
  `orient2d_bigint`: corners at 1.8e-284 next to coordinates of 1e-32 span 2^840, so neither the 62-bit integer stage
  nor the expansion range admits them. With those corners set to 0 the operation takes 50 ms. The fuzzer's 47 s are two
  booleans (the strict re-run of `MeshFuzzPilot`). All 60 slow grp-a units have the same cause (section 3.4): span
  2^839-2^1934, rounding 90 % of the time, BigInteger 85 % of it, median 4.8 s per boolean on master. The grp-b repair
  timeouts (#712, up to 49 s) are another matter: alpha wrap and `Reducer` under heavy GC, with almost no exact stage.
- **Shared profile table** (section 4.8, the AW3 format): the exact stage matters only in the #711 class (77-88 %), 42327
  (32 %), 55928 (28 %) and n-ary (20 %); in a normal stratum of 20 random Thingi10K unions it is 5 % plus 3 % filters.
- **Ranked levers** (section 5): (1) a hierarchical Antipodal boundary sum after option A (bound: 61 % of 117959, 18 %
  of 1087141; 23 instead of 6007 terms per evaluation, error at most 3.4e-4 before the fallback); (2) option A itself,
  already decided (the open union group 421 -> 334 s, 1087141 -57 %, 122796 +56 %); (3) a cost-weighted rounding budget
  for wide exponent spans (#711: rounding 15.6 -> 0.66 s at the probe's budget; 0 BigInteger stages on three Thingi10K
  cases, so they stay bit-identical); (4) server GC as documented advice, and the allocation task of the roadmap, whose
  trigger is now met (server GC -19 % / -11 % on 107554 / 42327, bit-identical); (5) the n-ary one-pass levers of the
  pre-study, restated with today's numbers (boxes_64_1 38.4 s against 0.57 s chained).
- **Closed below the stop rule:** a leading-term certificate for the BigInteger stage (decides 1.4 %), scaling into the
  expansion range (3 % of the 3D calls), stopping at the evaluation limit on #711 (0 %), the `BigInteger[4,4]` matrix
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
- **"total"** is the sum of the top-level phases without input loading; "wall" is the process (start-up, JIT, input).
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
| 67709 | union | 9.0 | 10.1 | 378 | rounding | 36 % | classification 32 % | Success | False | True | 1.5 |
| 67709 | intersection | 8.4 | 9.4 | 342 | rounding | 37 % | classification 31 % | Success | False | True | 1.3 |
| 67709 | difference | 9.0 | 10.0 | 343 | classification | 37 % | rounding 30 % | Success | False | True | 1.4 |
| 42327 | union | 40.2 | 40.9 | 850 | classification | 45 % | rounding 30 % | Success | False | True | 23.0 |
| 42327 | intersection | 34.3 | 35.1 | 922 | classification | 59 % | triangulation 16 % | Success | False | True | 20.6 |
| 42327 | difference | 34.8 | 35.4 | 847 | classification | 62 % | triangulation 13 % | Success | False | True | 20.6 |
| 1368057 | union | 39.0 | 40.6 | 856 | rounding | 53 % | classification 25 % | Success | False | True | 12.8 |
| 1368057 | intersection | 13.3 | 15.0 | 561 | classification | 47 % | triangulation 17 % | Success | False | True | 3.3 |
| 1368057 | difference | 11.8 | 13.4 | 678 | classification | 54 % | triangulation 16 % | Success | False | False | 3.3 |
| 102375 | union | 10.3 | 11.8 | 502 | rounding | 55 % | classification 16 % | Success | False | True | 1.5 |
| 102375 | intersection | 6.9 | 8.2 | 393 | rounding | 51 % | classification 17 % | Success | False | True | 1.1 |
| 102375 | difference | 9.0 | 10.3 | 466 | rounding | 48 % | classification 21 % | Success | False | True | 1.5 |
| 498458 | union | 17.0 | 17.9 | 870 | classification | 26 % | rounding 21 % | Success | False | False | 7.0 |
| 498458 | intersection | 12.8 | 14.0 | 713 | classification | 41 % | merge 26 % | Success | True | False | 5.8 |
| 498458 | difference | 13.8 | 14.8 | 781 | classification | 31 % | merge 26 % | Success | False | False | 6.1 |
| 486343 | union | 3.5 | 4.4 | 202 | rounding | 69 % | classification 21 % | Success | False | True | 0.3 |
| 486343 | intersection | 2.2 | 3.1 | 210 | rounding | 54 % | classification 30 % | Success | False | False | 0.3 |
| 486343 | difference | 2.4 | 3.3 | 202 | rounding | 57 % | classification 29 % | Success | False | False | 0.3 |
| 519511 | union | 20.3 | 20.2 | 1261 | intersections | 27 % | rounding 26 % | Success | True | False | 6.4 |
| 519511 | intersection | 14.3 | 14.3 | 869 | intersections | 31 % | assembly 14 % | Success | True | False | 4.8 |
| 519511 | difference | 15.4 | 15.4 | 1025 | intersections | 28 % | assembly 14 % | Success | True | False | 5.0 |
| 1517923 | union | 6.1 | 7.2 | 385 | intersections | 57 % | rounding 10 % | Success | True | False | 1.0 |
| 1517923 | intersection | 4.3 | 5.2 | 368 | intersections | 34 % | triangulation 14 % | Success | True | False | 0.8 |
| 1517923 | difference | 5.0 | 6.0 | 348 | intersections | 48 % | triangulation 11 % | Success | True | False | 0.9 |
| 1063860 | union | 3.8 | 5.6 | 629 | intersections | 40 % | rounding 27 % | Success | True | False | 0.9 |
| 1063860 | intersection | 1.9 | 3.6 | 378 | intersections | 46 % | rounding 19 % | Success | True | False | 0.4 |
| 1063860 | difference | 2.8 | 4.6 | 492 | intersections | 34 % | rounding 29 % | Success | True | False | 0.7 |
| 668056 | union | 5.8 | 6.8 | 517 | rounding | 30 % | intersections 23 % | Success | True | False | 1.7 |
| 668056 | intersection | 4.3 | 5.3 | 425 | intersections | 25 % | triangulation 17 % | Success | True | False | 1.4 |
| 668056 | difference | 4.6 | 5.6 | 437 | intersections | 25 % | triangulation 16 % | Success | True | False | 1.5 |
| 119829 | union | 3.8 | 5.4 | 580 | rounding | 62 % | intersections 17 % | Success | True | False | 1.0 |
| 119829 | intersection | 0.7 | 2.2 | 254 | intersections | 50 % | setup 14 % | Success | True | False | 0.1 |
| 119829 | difference | 2.3 | 3.8 | 297 | rounding | 43 % | intersections 32 % | Success | True | False | 0.4 |
| 124526 | union | 7.6 | 9.3 | 704 | classification | 70 % | intersections 8 % | Success | True | False | 1.0 |
| 124526 | intersection | 6.3 | 8.1 | 452 | classification | 71 % | intersections 10 % | Success | True | False | 0.8 |
| 124526 | difference | 6.9 | 8.5 | 538 | classification | 74 % | intersections 8 % | Success | True | False | 0.9 |
| 122796 | union | 11.7 | 14.4 | 1261 | classification | 66 % | intersections 9 % | Success | False | False | 2.6 |
| 122796 | intersection | 10.4 | 13.1 | 935 | classification | 75 % | intersections 9 % | Success | True | False | 2.1 |
| 122796 | difference | 11.2 | 14.1 | 1011 | classification | 70 % | intersections 9 % | Success | True | False | 2.3 |
| 107554 | union | 30.6 | 31.7 | 1178 | classification | 34 % | rounding 29 % | Success | False | True | 12.1 |
| 107554 | intersection | 23.9 | 25.2 | 1184 | classification | 47 % | merge 14 % | Success | False | False | 10.0 |
| 107554 | difference | 23.5 | 24.8 | 1120 | classification | 46 % | merge 15 % | Success | False | True | 10.0 |
| 113906 | union | 28.6 | 29.4 | 954 | classification | 47 % | rounding 19 % | Success | False | True | 10.1 |
| 113906 | intersection | 21.1 | 22.4 | 650 | classification | 56 % | merge 15 % | Success | False | False | 8.9 |
| 113906 | difference | 24.0 | 25.0 | 859 | classification | 56 % | merge 13 % | Success | False | False | 9.1 |
| 1087141 | union | 108.8 | 113.6 | 3570 | classification | 83 % | intersections 5 % | Success | False | True | 7.7 |
| 1087141 | intersection | 99.6 | 104.0 | 2216 | classification | 88 % | intersections 5 % | Success | False | False | 5.2 |
| 1087141 | difference | 98.7 | 103.3 | 2670 | classification | 88 % | intersections 5 % | Success | False | False | 5.9 |
| 1087144 | union | 105.0 | 109.7 | 3185 | classification | 83 % | intersections 5 % | Success | False | True | 7.7 |
| 1088051 | union | 101.3 | 105.6 | 3117 | classification | 83 % | intersections 5 % | Success | False | True | 7.6 |
| 1088054 | union | 98.1 | 102.7 | 3111 | classification | 83 % | rounding 5 % | Success | False | True | 7.7 |
| 1088138 | union | 99.8 | 104.6 | 3593 | classification | 83 % | rounding 5 % | Success | False | True | 7.6 |
| 117959 | union | 110.6 | 118.4 | 5216 | classification | 90 % | intersections 3 % | Success | True | False | 9.3 |
| 117959 | intersection | 108.1 | 115.3 | 3889 | classification | 92 % | intersections 3 % | Success | True | False | 7.5 |
| 117959 | difference | 108.8 | 116.2 | 4184 | classification | 92 % | intersections 3 % | Success | True | False | 8.0 |
| 112798 | union | 53.7 | 62.7 | 5806 | classification | 74 % | rounding 6 % | Success | False | False | 12.3 |
| 112798 | intersection | 47.1 | 55.1 | 4416 | classification | 82 % | intersections 6 % | Success | False | False | 9.4 |
| 112798 | difference | 48.9 | 57.3 | 5524 | classification | 78 % | intersections 6 % | Success | True | False | 10.8 |
| 1005586 | union | 69.8 | 81.9 | 9003 | classification | 64 % | rounding 15 % | Success | False | False | 16.8 |
| 1005586 | intersection | 53.4 | 62.7 | 4951 | classification | 83 % | intersections 8 % | Success | False | False | 10.7 |
| 1005586 | difference | 57.6 | 67.6 | 6892 | classification | 74 % | intersections 7 % | Success | False | False | 13.6 |
| 55928 | difference | 380.0 | 382.1 | 11815 | classification | 59 % | triangulation 30 % | Success | False | False | 199.4 |
| grid:10 (n-ary, 2000 meshes) | one pass | 6.1 | 7.2 | 682 | classification | 31 % | intersections 27 % | Success | True | False | 2.2 |
| boxes_64_1 (n-ary CSG, pre-study) | one pass | 38.4 | 81.4 for both variants | 1723 | classification | 91 % | triangulation 7 % | Success | - | - | - |
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
| the slowest boolean tests of `SLOW_BACKLOG.txt` | run, but they are not extreme per operation: they are slow by count | trx durations on this machine: `SafeRoundingDegenerateInputTests.DegenerateInput_InBooleans_CleanWithinTheBound` 15.1 s (198 iterations, 396 booleans, about 38 ms each), `ExactBooleanPropertyTests.Boxes_RepresentationInvariance` 13.0 s, `MeshRobustRepairTests.UncleanResultResolvedExactly_VolumeCheckedOnce` 13.2 s (a repair), `LocalRefinementTests.Boolean_LocalGap_CostsMuchLessThanGlobalRefinement` 6.4 s, `MeshBooleanSeamTests.Union_RandomSpherePairs_AreClosed` 6.1 s (30 unions), `ExactBooleanPropertyTests.Boxes` 1.3-2.0 s per campaign of 300-400 samples. Their lever is the test tier (#700 and the slow-backlog PRs), not the boolean |

### 3.3 Option A on the open models (union, both Antipodal switches lifted)

Owner 2026-11-12, option (A): `AntipodalMaxBoundarySegments` and `AntipodalMaxTriangles` unlimited (follow-up PR after
#675). Measured with `profile --antipodal-unlimited` (added to `Profile.cs` for this note), the same rows otherwise:

| model | default s | option A s | change | peak RSS MB default / A |
|---|---|---|---|---|
| 124526 | 7.6 | 6.4 | -16 % | 704 / 579 |
| 122796 | 11.7 | 18.3 | **+56 %** | 1261 / 1125 |
| 107554 | 30.6 | 29.9 | -2 % | 1178 / 1339 |
| 113906 | 28.6 | 27.2 | -5 % | 954 / 947 |
| 1087141 | 108.8 | 47.2 | **-57 %** | 3570 / 3013 |
| 117959 | 110.6 | 103.0 | -7 % | 5216 / 4461 |
| 112798 | 53.7 | 45.1 | -16 % | 5806 / 6008 |
| 1005586 | 69.8 | 56.9 | -18 % | 9003 / 7325 |
| sum | 421.4 | 334.0 | -21 % | |

This confirms #675's numbers on the merged tree (122796 slower by about 1.6x here, 2x there). Allocations double under
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
- Since none reaches the limit, a lower plain budget would not cap them. A cost-weighted budget would (lever 3).

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
read with `speedscope_top.py` (self and inclusive CPU share, waiting threads left out) and `speedscope_callers.py`.

### 4.1 Rounding on a wide exponent span (#711)

The timeout input of #711 (difference; 16 + 16 vertices, 762 pieces, 152 result triangles): rounding 15.5 of 16.3 s,
13.6 GB allocated, `RoundingClean` false, the evaluation limit reached. The rounding trace of #721's `instrument.py`
(it applies to this tree unchanged): 13 333 evaluations of proposed edits, 768 k pair tests in them, 17.5 s, about
16 µs per pair test (the first full search: 2151 pairs, 5.8 µs each). The sampling profile: `orient3d_bigint` 63 %,
`orient2d_bigint` 24 %, `BigInteger.Multiply` 59 % inclusive.

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

117959, 1087141 and its siblings, 112798, 1005586, 122796, 124526: classification 64-93 %. Today they take the
fast-winding-number path (the switches route them away from Antipodal). Profile of 117959 union and 1087141 union:
`DMeshAABBTree3.branch_fast_winding_num` 77 % of the CPU. The FWN probe (`probe_fwn.py`): two queries per piece
(1 574 056 pieces of 1087141 -> 1 574 056 queries against the piece's own mesh and as many against the other; the own
queries are about 56 % of the FWN time).

Under option A the same models take L2's traced Antipodal path. Profile of 117959 union with A: `antipodal_value` 61 %
self (the boundary sum: 6007 segments per traced evaluation, 925 k traced pieces), `trace_open_winding` 83 % inclusive;
1087141 union with A: `antipodal_value` 18 %, GC suspension 12 %, BigInteger construction in `carried_crossings`
10 % inclusive, `orient3d` on `ExactPoint3` 10.5 % inclusive. The per-patch L1 decision (`l1_patch_decided`) is 0 in
every row: it runs only where L2 tracing is off (`TracedOpenWinding == false`), so it is superseded, not a lever.

### 4.3 Classification with many coplanar or self-intersecting pieces (42327, 107554, 113906, 498458, 1368057 int / diff)

These take the Antipodal path already (both operands; 42327: 1.03 M pieces, 9.7 M winding plane sides). 42327
intersection, sampling profile: `Monitor.Enter` slow path 19.5 % self, of it 11.2 % in `ExactMeshArrangement.AddPoint`
called from `classify` (the pieces' centroid points, 2.0 M calls; 17.2 % inclusive); BigInteger construction 11.6 %
self, inclusive in `carried_crossings` 19.4 %; GC suspension 6.6 %; `orient3d` on `ExactPoint3` 4.8 % inclusive.
Classification allocates 18 GB on 42327 union; GC pauses are 23-27 % of these operations (section 4.4).
hotspots.md's `coplanar_stack` figure (72 % of classification on the raw 42327) is from the old classification; on
the rotated pair today `inside_coplanar` is 3.5 % self.

### 4.4 Allocation and GC (across the groups)

GC pause time is 11.3 % of the inventory's 2354 s (the 66 operations); the largest shares: 107554 26.9 % (difference) and 26.6 %
(intersection), 498458 intersection 26.1 %, 55928 difference 24.1 % (91 s of pauses, 199 GB allocated), 42327
23.2-23.8 %. A/B of the GC configuration (one process each, intersection; hashes equal in every row):

| case | default s / pause ms | server GC | gen0 512 MB |
|---|---|---|---|
| 107554 | 20.5 / 5256 | 16.6 / 1035 (-19 %) | 20.8 / 4530 |
| 42327 | 29.7 / 6733 | 26.3 / 1387 (-11 %) | 33.2 / 7554 |

### 4.5 Intersections (519511, 1517923, 1063860, 668056; at most 20 s)

intersections 25-57 % of 1.9-20.3 s. 519511 union profile: GC suspension 14 %, `self_pairs` 11.7 %, `Orient3d` 9.3 %,
`TriangleBoxTree.Query` 10 %, `TriangleTriangleFilter.by_projection` 5.3 %. The scaling pre-study puts the stage at
2.95x on 4 cores (`benchmarks/Boolean30Prestudy/results/scaling_summary.md`). None of these operations is above 20 s.

### 4.6 n-ary one pass

boxes_64_1 (the CSG pre-study's worst case): one pass 38.4 s (classification 34.8 s, 158 M winding plane sides,
1.13 M pieces from 768 input triangles; triangulation 2.6 s) against 0.57 s chained. grid:10 (2000 meshes) 6.1 s
(27 s in the roadmap's first measurement).

### 4.7 The 55928 tail case

Difference 380 s (356 s in #675's note, 370-425 s before), classification 59 %, triangulation 30 %, 199 GB allocated,
91 s of GC pauses, peak 11.8 GB. Its levers have their own note (`docs/design/EXACT_BOOLEAN_TAIL_55928.md`); it is not
profiled again here. It is the largest single row; section 4.4 applies to it.

### 4.8 Shared profile table (exact / BVH / GC / rounding / rest)

Owner request 2026-11-12: one table in the format of the AW3 study, to merge into hotspots.md.

**Commit sets.**
- The rows of section 3.1 (all but the grp-a unit): master `8124a7d3` + #675 `a9b00da3`, merged locally.
- The traces, the grp-a unit, grp-b and the normal stratum: master `0325530c`, which contains #675. Its hashes equal the
  earlier rows on the three cases of the spot check.

**How the shares are measured.**
- One `dotnet-trace` sampling profile per case (`traces_job.sh`), classified by `speedscope_categories.py`.
- Only CPU samples inside `ExactMeshBoolean.Compute` count; for grp-b, everything under the `MeshFuzzPilot` replay.
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

**Normal stratum.** 20 Thingi10K models with 2k-50k triangles, drawn with `random.seed(20261112)` from
`geometry_data.csv` (ids 104969, 131971, 1722409, 232911, 237631, 239188, 281457, 314592, 363052, 44106, 501151, 56483, 669966, 67927, 68203, 740249, 779964, 80105, 82256, 888459). Each model is paired with its rotated copy, as above, and run as a
union. Their shares are CPU-weighted over the 20 traces.

| case | op | total s | (i) exact stage | (i) filters | (ii) AABB / BVH | (iii) GC, trace | (iii) GC pause, wall | (iii) alloc GB | (iv) rounding | (v) rest | largest in rest |
|---|---|---|---|---|---|---|---|---|---|---|---|
| #711 timeout | difference | 16.3 | 87.7 % | 5.2 % | 0.3 % | 3.6 % | 1 % | 13.5 | 2.9 % | 0.3 % |  |
| #711 grp-a unit 0edbe479 | intersection | 6.6 | 77.0 % | 5.5 % | 0.5 % | 8.1 % | 0 % | 3.7 | 7.2 % | 1.7 % |  |
| 55928 | difference | 380.0 | 27.6 % | 9.2 % | 4.7 % | 6.9 % | 24 % | 199.4 | 0.1 % | 51.5 % | carried_crossings 17.0 |
| 117959 | union | 110.6 | 2.7 % | 0.3 % | 81.2 % | 4.7 % | 4 % | 9.3 | 1.9 % | 9.1 % |  |
| 117959 (A) | union | 103.0 | 2.7 % | 1.6 % | 6.0 % | 4.1 % | 10 % | 19.5 | 2.2 % | 83.4 % | antipodal_value 61.1 |
| 1087141 | union | 108.8 | 2.9 % | 1.4 % | 80.3 % | 5.1 % | 3 % | 7.7 | 2.7 % | 7.6 % |  |
| 1087141 (A) | union | 47.2 | 8.6 % | 5.5 % | 14.1 % | 19.3 % | 19 % | 20.2 | 5.4 % | 47.1 % | antipodal_value 18 |
| 1005586 | union | 69.8 | 4.5 % | 1.1 % | 51.6 % | 9.5 % | 11 % | 16.8 | 9.8 % | 23.5 % | EdgeTable.Build 5.5 |
| 112798 | union | 53.7 | 5.1 % | 0.6 % | 56.7 % | 8.7 % | 12 % | 12.3 | 5.3 % | 23.6 % | EdgeTable.Build 7.0 |
| 122796 | union | 11.7 | 7.6 % | 1.3 % | 42.9 % | 14.3 % | 14 % | 2.6 | 6.3 % | 27.6 % | EdgeTable.Build 6.5 |
| 122796 (A) | union | 18.3 | 6.1 % | 1.8 % | 9.7 % | 10.2 % | 14 % | 4.7 | 2.8 % | 69.4 % | antipodal_value 33 |
| 42327 | intersection | 34.3 | 32.1 % | 3.5 % | 3.7 % | 9.4 % | 24 % | 20.6 | 8.5 % | 42.8 % | lock waits in AddPoint 11.2 |
| 1368057 | union | 39.0 | 9.5 % | 4.1 % | 12.2 % | 5.9 % | 8 % | 12.8 | 51.2 % | 17.1 % |  |
| 107554 | intersection | 23.9 | 16.6 % | 6.2 % | 11.4 % | 34.3 % | 27 % | 10.0 | 5.9 % | 25.6 % | lock waits 5.6 |
| 519511 | union | 20.3 | 8.8 % | 16.6 % | 29.1 % | 14.4 % | 11 % | 6.4 | 7.0 % | 24.1 % | self_pairs 11.7 |
| boxes_64_1 n-ary (both variants) | one pass | 38.4 (one pass) | 20.1 % | 0.4 % | 14.4 % | 56.4 % | - | - | 0.1 % | 8.6 % |  |
| **normal stratum**, 20 random Thingi10K models (2k-50k triangles), union | union | 11.1 in total (median 0.32, max 3.3) | 5.4 % | 3.4 % | 10.6 % | 32.0 % | 3.5 % | 2.1 in total | 15.3 % | 33.1 % | |
| grp-b, the 10 repair timeouts (#712; root `MeshFuzzPilot` replay) | robust-repair / soup | 6.1-49.1 wall | 0.6 % | 8.3 % | 2.6 % | 32.2 % | - | - | 0.0 % | 56.2 % | Reducer and alpha wrap (simplify_wrap) |

**Reading.**
- **(i) matters in four places:** #711 and its grp-a units (77-88 %, all BigInteger), 42327 (32 %), 55928 (28 %) and
  boxes_64 (20 %). In the normal stratum it is 5.4 % plus 3.4 % filters. Predicate-kernel work (eg an InCircle limb
  kernel, filter tuning) would pay in those cases, not in the normal stratum.
- **(ii) dominates the default path of the large open models** (43-81 %: the fast-winding tree). Option A removes most of
  it, and with option A the time moves into (v), into `antipodal_value`, which is lever 1.
- **(iii) GC** is the largest share in the normal stratum's trace (32 %; 3.5 % wall pauses). It is 34 % in 107554,
  56 % in the n-ary one pass, and 24-40 % in grp-b.
- **grp-b (#712) is not an `ExactMeshBoolean` cost.** It is `MeshRobustRepair`'s alpha wrap and `Reducer` under heavy
  GC. It is passed to the wrap sessions.

## 5. Levers

Upper bound = the share of the stage or function the lever removes, times the best possible speedup of that part,
measured on the named rows. A lever whose bound is under 10 % of its group's time is closed without an A/B (brief).

### 5.1 Proposed, ranked by gain per effort

| # | lever | group | measured upper bound (command) | falsification | cost | risk |
|---|---|---|---|---|---|---|
| 1 | **Hierarchical Antipodal boundary sum.** The open winding's boundary term is summed over all B segments per traced evaluation. Order the segments into chains, build a binary tree over each chain, and replace a cluster far from p (radius / distance < θ, and the ray x1 missing the cluster's cone) by the term of its chord plus the dipole of the closed polygon (Barnes and Hut 1986; the dipole as in Barill et al. 2018). With a certified far-field bound added to the margin that L2 already uses, a piece whose margin does not clear falls back to today's full sum, so decisions stay those of today | 4.2 under option A | `antipodal_value` 61 % of 117959 union (A) and 18 % of 1087141 union (A) (profiles above); terms per evaluation 23 of 6007 (117959) and 26 of 1196 (1087141) at θ = 0.25: about 60 s of 117959's 103 s and 8 s of 1087141's 47 s | `probe_boundary_sum.py` (300 sampled points per model): error modulo whole turns at most 3.4e-4 (117959, θ = 0.25), 8.6e-14 (1087141); at θ = 0.5 2.5e-3 and 3.3 % whole-turn jumps, so θ about 0.25 and the integer bookkeeping are design items | M-L: `mesh_ops/ExactMeshBoolean*` (locked for 3.0 work, exact-code review of the new bound, the classification mutation shards) | low if the fallback keeps decisions; the whole-turn bookkeeping of the chord identity must be proven (the probe shows 0-0.3 % jumps at θ <= 0.25 with the cone test) |
| 2 | **Option A** (lift both switches; owner decision 2026-11-12, follow-up PR after #675) | 4.2 | the open union group 421 -> 334 s (-21 %); 1087141 -57 % (its four siblings are the same part and should follow, not run) | measured, section 3.3; hashes change (Antipodal is right where FWN and Antipodal differ, #675) | decided | 122796 +56 % (1.6x); lever 1 targets exactly that cost |
| 3 | **Cost-weighted rounding budget.** Count a BigInteger exact stage of the rounding's pair tests as k pair tests toward `MaxPairTests` (eg k = 20-100), or stop the evaluations once the BigInteger time exceeds a share of the rounding | 4.1 | rounding 95 % of the #711 timeout op; `profile --rounding-max-pair-tests` 5M / 1M / 250k / 50k: rounding 15.6 / 12.0 / 3.0 / 0.66 s, `RoundingClean` false in every row (152-156 result triangles) | the knob shows the time follows the budget; 0 BigInteger stages on 1368057, 486343, 42327 (union) with `probe_bigint.py`, so those results stay bit-identical | S: `mesh_ops/MeshRoundingRepair.cs` (the rounding shards) | results change only on inputs whose rounding needs BigInteger; an owner decision (it changes results, as #335 did). Checked on the 60 slow units: all are this class (85 % BigInteger), none reaches today's limit, so only the weighting caps them |
| 4 | **Server GC as documented advice for large booleans** (OPERATIONS.md), and the roadmap's allocation task ("Boolean allocations", owner 2026-11-04), whose trigger (>= 10 % on large inputs) these data meet | 4.4 | GC pauses 11.3 % of the inventory, 23-27 % on 107554, 498458, 42327, 55928 | A/B, section 4.4: server GC -19 % (107554) and -11 % (42327), hashes equal; a 512 MB gen0 gains nothing | S for the advice; M for the allocation PR (largest allocators first: classification on 42327, 18 GB; `carried_crossings` BigInteger construction, section 4.3) | none for the advice (more memory with server GC); the allocation PR bit-identical by design |
| 5 | **n-ary one pass: the pre-study's levers, with today's numbers** (propagation of the classification, pruning before triangulation, a tree-aware evaluation; EXACT_BOOLEAN_30_PRESTUDY.md section 3) | 4.6 | classification 91 % of boxes_64_1's 38.4 s; the pre-study's propagation bound: 64 -> 4.9 s, still 8x the chain | not re-probed (the pre-study's bound stands) | M-L | n-ary is a 3.0 use case (T3a / T3b); boxes_64 is its worst case, not its typical one |

### 5.2 Candidates with a measured bound but no A/B yet (not proposed)

| lever | bound | what is open |
|---|---|---|
| BigInteger construction in `carried_crossings` (L2's crossing count between pieces) | 19.4 % inclusive of 42327 intersection's CPU, 10.2 % of 1087141 union (A) | which values are built per call and whether a fixed-width or reused representation decides the same signs; part of lever 4's allocation work |
| `ExactMeshArrangement.AddPoint` from `classify` | 17.2 % inclusive of 42327 intersection (11.2 % in `Monitor.Enter`'s slow path), 2.0 M calls | the shards are evenly used (largest 1.6 % of the calls, 64 of 64 used: `probe_shards.py`), so the lock waits are not a hash bias; whether the centroid points need the shared index at all is not diagnosed |
| A faster wide-integer kernel for the BigInteger stage (fixed 64-bit limbs on the stack, no allocation) | 87-89 % of the #711 op is in the BigInteger stage; the gain per call is not measured (an estimate from limb counts is 2-3x) | a microbenchmark; lever 3 caps the same cost at less effort |
| The intersections stage of 519511 / 1517923 | 25-57 % of operations of at most 20 s; GC 14 %, `self_pairs` 12 % of 519511 union | below the extreme range of this note; levers 4 (GC) applies |

### 5.3 Closed, with their bound

| lever | bound | how measured |
|---|---|---|
| Leading-term certificate for the BigInteger stage (flush coordinates below 2^(Emax-200) to 0, keep the sign of the flushed determinant if it exceeds the flushed parts' bound) | decides 1.4 % of the orient3d BigInteger calls of #711 (25 093 of 1.79 M) | `probe_bigint.py` (0 wrong signs) |
| Scale by 2^k into the expansion range (a relative range check instead of the absolute [2^-100, 2^100]) | admits 2.9 % of the orient3d BigInteger calls of #711; 24 % of the orient2d calls, about 1.1 s of 16.3 s (6.5 %) | `probe_bigint.py` (span <= 2^200) |
| Stop the rounding passes at the evaluation limit (#721 option 1) on #711 | 0 %: rounding 15.9 vs 16.2 s, the same hash | #721's `instrument.py`, `G3_STOP_AT_LIMIT=1`: the work is before the limit |
| `orient3d` on `ExactPoint3` without the `BigInteger[4,4]` matrix | 42327 intersection -4 % / -1 % (two rounds, 30.2 / 29.5 -> 28.9 / 29.3 s), 1087141 union (A) +3 % / +0.4 %; hashes equal | `probe_det4.py`, A/B of two builds of the same tree |
| Shard hash mixing in `AddPoint` | no bias to remove (largest shard 1.6 %) | `probe_shards.py`, `G3_SHARD_PROBE=1` |
| L1 per-patch open winding | never runs (L2 tracing supersedes it): `l1_patch_decided` 0 in all 74 rows | inventory counts |
| FWN per piece on the default path (two tree queries per piece, 77 % of 117959 / 1087141) | superseded by option A, which moves these models to the Antipodal path | section 4.2 |
| JIT settings for cold single calls | `TieredCompilation=0` -0.5 s of 7.6 s (about 7 %); gen0 256 MB none | section 2 |
| Rounding levers of SNAP_ROUNDING_3D.md 4.2 (stop at the limit, `near`, box-neighbour enumeration, setup; certified empty ball, clearance skins, separating-axis prefilter) | measured there; not repeated | #721 |

## 6. Recommendation

In order: lever 2 (decided) with lever 1 behind it in the same area (it removes option A's regression on 122796 and
most of 117959's remaining time; its design needs the far-field bound and the whole-turn bookkeeping); lever 3 as a
small owner decision (the 60 slow fuzz units are checked: the same cause, so it covers the whole #711 class); lever 4's advice at once
and its allocation PR when the 3.0 locks allow. Lever 5 stays with the n-ary plan. With levers 1 and 2 the open union
group would fall from 421 s to roughly 260 s (lever 1's bound on the two profiled models; the other six are not
profiled under A), still with 117959 near 45 s and 55928 untouched.

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
```
