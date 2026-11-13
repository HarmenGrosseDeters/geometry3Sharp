# MeshFuzzPilot campaign grp-c: tree-nearest, tree-ray, tree-inside, tree-winding, batch-queries

Issue #624. Raw output only: logs and the artifact list. Same host, tools and validation as grp-a (`../grp-a/TRIAGE.md`).
`MESH_FUZZ_OPS=tree-nearest,tree-ray,tree-inside,tree-winding,batch-queries`, 3 fork workers, four 2 h blocks.

| Block | Start (Berlin) | Runs | cov | ft | corp |
|---|---|---|---|---|---|
| 1 | 2026-11-12 18:34 | 50 975 | 17 951 | 186 433 | 8 673 |
| 2 | 2026-11-12 20:35 | 26 907 | 145 835 | 188 716 | 9 338 |
| 3 | 2026-11-12 22:35 | 21 185 | 153 906 | 182 488 | 10 310 |
| 4 | 2026-11-13 00:36 | 15 647 | 164 853 | 183 093 | 10 191 |

Total 114 714 runs, 2026-11-12 18:34 to 2026-11-13 02:36 (Berlin). **No crash, no timeout, no OOM.** 8 `slow-unit-*`
(not findings; to be timed on the idle machine after the chain).

Caveat: in fork mode every block restart re-reads the whole corpus in one process before fuzzing. With about 10 000
inputs at about 2 per second this took over an hour of blocks 3 and 4, so the effective fuzzing time was well under 8 h.
Groups d to f run as one 8 h block for that reason.
