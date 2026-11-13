# T1b profile (2026-11-13): where SafeRounding's time goes on master 58644af

Rows only, no meshes (Thingi10K fetched at run time with runs/d1/fetch_thingi.py, exported with BooleanComparison).
`rrprof.patch`: temporary stopwatch instrumentation of `MeshRoundingRepair.Apply` (not for master), built into
`Prof` (one JSON row per op: ExactMeshBoolean phases `ph.*`, rounding sub-phases `rr.*` in ms).
Machine: 4-core cloud container; 4 cores, and 1 core with `taskset -c 0` and `DOTNET_PROCESSOR_COUNT=1`.

- `prof{1,4}.jsonl`: 9 models with the largest rounding times of the D1 timing set (27 ops; the ids are the
  directory order 100831 1053875 1150177 119829 1514900 375065 519511 688367 763718).
- `s{1,4}.jsonl`: `sample_ids.txt`, every 7th model of the D1 timing set (runs/d1-cohort timing_cand) by rounding
  time, without the 9 above: 56 models, 168 ops.
- `fine4.jsonl`: 1150177 375065 688367 763718 at 4 cores with setup and final split further.

`python3 analyze.py s1.jsonl s4.jsonl` prints the sums per sub-phase (the remainder of z.apply is the first tree build).

## A/B of the bit-identical serial-work change (branch claude/rounding-serial-work)

`HashRun` (build with `-p:G3Src=<source tree>`): per op the rounding and op times and a SHA-256 of the result. Run on
the 65 exported cases (`ls -d cache/*/`), whole sweeps before, after, before, after at 4 cores, then after on 1 core;
the rows are in `benchmarks/BooleanComparison/perf/rounding_serial_work_2026-11-13.jsonl` on that branch.
