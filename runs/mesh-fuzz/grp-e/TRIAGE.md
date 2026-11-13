# MeshFuzzPilot campaign grp-e: remesher, reducer

Issue #624. Raw output only: logs and the artifact list. Same host, tools and validation as grp-a (`../grp-a/TRIAGE.md`).
`MESH_FUZZ_OPS=remesher,reducer`, 3 fork workers, one 8 h block.

- 2026-11-13 10:40 to 18:40 (Berlin), 28 806 s, no interruption.
- 226 230 runs; final cov 57 992, ft 347 046, corp 9 126. cov stopped growing in the first minutes; ft still grew slowly
  (315 632 after one hour, 347 046 at the end). Throughput fell from about 20 to 1 or 2 inputs per second.
- **No crash, no timeout, no OOM.** 14 `slow-unit-*` (not findings; to be timed on the idle machine after the chain).
