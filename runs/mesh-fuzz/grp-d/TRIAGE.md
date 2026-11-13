# MeshFuzzPilot campaign grp-d: alpha-wrap, partitioned-reducer

Issue #624. Raw output only: logs, `triage.txt` (replay of every crash), the `dump` of every crash input, the overflow
check. No corpus and no crash files. Same host, tools and validation as grp-a (`../grp-a/TRIAGE.md`).
`MESH_FUZZ_OPS=alpha-wrap,partitioned-reducer`, 3 fork workers, **one 8 h block** (a block restart re-reads the whole
corpus in one process, see `../grp-c/TRIAGE.md`).

## Campaign

- 2026-11-13 02:36 to 10:40 (Berlin), 29 002 s, no interruption.
- 379 284 runs; final cov 124 322, ft 395 865, corp 7 640. cov stopped growing in the first hour; ft still grew slowly.
- Artifacts: 82 crash files (libFuzzer counted 83: equal inputs give one file), no timeout, no OOM, no slow unit.

## Triage

Replay of all 82 with the unmodified build and the same filter (`triage.txt`): **one group**, all `partitioned-reducer`,
all rc 134 with

```
Finding: ...: the result for the copy scaled by 1099511627776 is not the scaled result
```

Cause: a domain gap of the harness, not a library defect (**#794**). `RunPartitionedReducer` scales the normalized,
subdivided mesh by 2^40 with only a lower bound (`HasTinyComponents`, 2^-200). Every input is a tiny shape far from the
origin: log2(max |coordinate| / largest extent) of A is 983.2 to 1019.3 (`overflow-check.txt`, computed without squares).
In 81 of 82 the 2^40 copy overflows (2^1024 and above); in the last one it is about 2^1023, where the quadric products of
the reducer overflow as well. The `reducer` target bounds the same claim to 2^150 (`Targets2.cs:168`); #794 asks for that
bound here.

Shapes of A: 16 vertices / 24 triangles 56, 8 / 8 9, 6 / 8 7, 4 / 4 5, 8 / 12 3, 8 / 10 2.

`alpha-wrap`: no artifact in 379 284 runs (about half of them, the first byte selects between the two operations).

Because this one class fills the artifacts, a second round of (d) after #794 is due (#624 stop rule).
