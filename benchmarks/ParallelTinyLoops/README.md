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
