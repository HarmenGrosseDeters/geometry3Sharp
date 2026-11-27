# ParallelTinyLoops

Crossover of `gParallel` (`Parallel.For` per call) against a plain loop, and the effect of the Small loops of #1502
(`gParallel.ForRangeSmall`, `ForEachSmall`, `BlockStartEndSmall`: items run inline on the calling thread until a 250 us budget is
spent, then the rest goes to the parallel loop). Procedural inputs only. Linux container, 4 cores, `net10.0`, Release.

```
dotnet run -c Release --project benchmarks/ParallelTinyLoops -- crossover [busy] [budget=<us>]
dotnet run -c Release --project benchmarks/ParallelTinyLoops -- e2e [large|tiny] [busy] [serial] [only=<name>] [budget=<us>]
```

"busy" is the load of the issue: four items that each loop `Parallel.For` of 64 items x ~10 ms. `e2e` caps a run at 120 s when busy.
The base numbers come from the same harness built against the base commit (`-p:DefineConstants=BASELINE`), run in the same job.

## 1. Crossover (median time per call, us)

Per-item cost is 1 ns (0 spins), 20 ns, 200 ns, 2 us. Alone, a call of `Parallel.For` costs 3-15 us; it wins once the serial work
is above about 10-50 us. That is 1000+ items at 1 ns and 4 items at 2 us: **a count threshold cannot decide it**, the work can.

| items | per item | serial | ForRange | ForRangeSmall (250 us) |
|---|---|---|---|---|
| 16 | 1 ns | 0.2 | 9.7 | 1.1 |
| 256 | 1 ns | 3.0 | 19 | 8.2 |
| 4096 | 1 ns | 7.0 | 444 | 20 |
| 64 | 20 ns | 1.2 | 4.9 | 0.9 |
| 1024 | 20 ns | 11 | 12 | 7.6 |
| 4096 | 20 ns | 45 | 26 | 36 |
| 64 | 200 ns | 12 | 10 | 13 |
| 256 | 200 ns | 41 | 19 | 45 |
| 1024 | 200 ns | 178 | 58 | 181 |
| 4096 | 200 ns | 720 | 192 | 473 |
| 64 | 2 us | 149 | 78 | 148 |
| 1024 | 2 us | 2441 | 810 | 939 |

The price of the inline phase is at most the budget (a loop of 250 us - 1 ms of work loses up to ~125 us); above ~2 ms it is
within 15-40 %, and for the large loops of a large mesh it is not measurable (section 3). With the pool busy, one `Parallel.For`
call takes 5-150 ms whatever its size (full tables: `crossover busy`), so every loop that stays inline saves that.

Budgets tried on tiny inputs with the busy pool (Remesher small / Reducer small, median ms): 25 us 2641 / 3764; 250 us 325 / 542;
1000 us 302 / 161 (same run series, noisy). 250 us was taken: large meshes showed no slowdown at 25, 250 and 1000 us.

## 2. Tiny inputs (ms per run, median of 5)

| case | alone, base | alone, Small | busy pool, base | busy pool, Small |
|---|---|---|---|---|
| Remesher small (300 tris, 12 remeshes x 3 passes) | 76 | 86 | 3235 | 87 |
| Reducer small (Sphere(12), 20 x to 100 tris) | 85 | 79 | 2767 | 288 |
| Boolean box pairs (40 pairs x union, intersection) | 167 | 135 | 41585 | not changed (exact code, see below) |

## 3. Large meshes alone (ms per run, median; base / Small)

| case | base | Small |
|---|---|---|
| Reducer, 199692 tris to 20000 | 858 | 951 (alternating 6 runs: 852 / 823 / 848 vs 809 / 855 / 783) |
| Reducer with ProjectToFarMesh | 736 | 920 (alternating: 818 / 743 / 775 vs 788 / 769 / 767) |
| Remesher, 41772 tris, 5 passes | 100 | 96 |
| RemesherPro, same | 98 | 88 |
| Boolean union 2 x 18252 tris | 171 | 159 |
| Boolean union 2 x 169932 tris | 704 | 702 |

The first Reducer rows differ by more than 10 % in the single run, but not in the alternating runs: it was noise (run-to-run
spread of this machine is about 15 % on these rows).

## What is not covered

The loops of `ExactMeshArrangement` / `ExactMeshBoolean` call `ForRange` and stay as they are: their `gParallel` use is pinned in
`DeterministicApis.txt` (a changed `ForRange` body or a new member used from these files is a build error of the determinism proof
scope until the ledger and `DETERMINISM_PROOF.md` 5.4 are updated), and the Boolean cases above are dominated by loops of 2-15
heavy items (a body per mesh or patch) that an inline-first loop cannot take.

## 4. Caller-first loops (#1502 part 2, 2026-11-25)

`ForRange`, `ForEach` and the handovers of the Small loops no longer call `Parallel.For` / `Parallel.ForEach`: the caller queues
helpers (one at a time: each helper queues the next while indices are left) and takes guided chunks of the indices itself; it waits
only for chunks a helper has taken. `Parallel.For` waited for every task it had queued, and from a thread outside the pool (the
benchmark's `Main`, a test runner's thread, an application thread) those tasks sit in the global queue behind the busy pool's work.
Base = master `eee440ae`, new = this branch, built side by side and run alternately in one job (base, new, base, ...).

Busy pool, caller on a dedicated thread (`e2e ... busy`; ms per run, median):

| case | base | new |
|---|---|---|
| Boolean box pairs (40 x union, intersection) | 20219 | 118-181 |
| Reducer small | 343 | 132-152 |
| Remesher small | 124 | 58-76 |
| Reducer 199692 tris -> 20000 | 1685 | 931-1224 |
| Remesher 41772 tris | 292 | 107-153 |
| RemesherPro 41772 tris | 465 | 133-235 |
| Boolean Union 2 x 18252 tris | 2456 | 250-337 |
| Boolean Union 2 x 169932 tris | 3844 | 1255-1330 |
| `MeshRobustRepair` test body of `SolidifyNotClean_AlphaWrapped` (scratch probe, 5 steps) | 82672 | 15740 (all serial: 15032) |
| one `ForRange` of 32 trivial items (crossover busy, per call) | 12000-177000 us | 1.5 us |

Alone (ms per run, median over all runs of three alternating series; Reducer rows six more series):

| case | runs | base | new | new / base |
|---|---|---|---|---|
| Reducer 199692 tris -> 20000 | 45 | 863 | 819 | 0.95 |
| Reducer (projection) | 27 | 769 | 725 | 0.94 |
| Remesher 41772 tris, 5 passes | 15 | 87 | 78 | 0.90 |
| RemesherPro, same | 15 | 70 | 73 | 1.04 |
| Boolean Union 2 x 18252 tris | 15 | 155 | 132 | 0.85 |
| Boolean Union 2 x 169932 tris | 9 | 588 | 555 | 0.94 |
| Boolean box pairs | 10 | 146 | 98 | 0.67 |
| Remesher small | 10 | 68 | 56 | 0.82 |
| **Reducer small** (20 x Sphere(12) to 100 tris) | 10 | 80 | 92 | **1.16** (an earlier series of the first version: 91 / 98, 1.07) |
| `HausdorffBenchmark`, sum of 21 rows (first version) | 2 | 1678 | 1680 | 1.00 |
| `SolidifyBenchmark`, sum of 7 medians (first version; result hashes identical) | 2 | 15723 | 16004 | 1.02 |

Crossover alone (median of 4 alternating runs, us per call): `ForRange` new / base is 0.04-0.9 below 64 items and above 1024 items,
and **1.07-1.20 for 64-256 items** (1-3 us per call; for example 128 items x 200 ns: 11.5 / 13.5 us). There the Small loops or a
serial loop are faster than either. `ForEach` new / base is 0.05-0.9 except one row (128 items x 2 us: 90 / 116 us, 1.28, not
seen in the other series). Raw tables: the PR.

Design steps measured on the way (scratch micro-benchmark, alone, new / `Parallel.For`): fixed chunks of n / 16 participants with all
helpers queued at once and a blocking wait were 1.15-1.39 on mid-size loops of 200 ns items and 1.5-3.7 on loops of a few us;
guided chunks with a spinning wait brought the mid-size rows to 0.98-1.07, queuing one helper at a time the few-us rows from 3.0 to
1.9-2.5.
