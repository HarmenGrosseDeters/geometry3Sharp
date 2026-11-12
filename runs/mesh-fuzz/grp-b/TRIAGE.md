# MeshFuzzPilot campaign grp-b: robust-repair, robust-repair-soup, rounding-repair

Issue #624. Raw output only: logs, `dump` and replay text, stopwatch timing, range classification. No corpus, no crash
or timeout files (they stay in the container at `/work/grp-b/artifacts`).

## Campaign

Same host, tools and validation as grp-a (`../grp-a/TRIAGE.md`). `MESH_FUZZ_OPS=robust-repair,robust-repair-soup,rounding-repair`,
3 fork workers, four 2 h blocks on one work directory (`/work/drive.sh`), no interruption.

| Block | Start (Berlin) | Length | Runs | cov | ft | corp | crash/timeout |
|---|---|---|---|---|---|---|---|
| 1 | 2026-11-12 08:28 | 7217 s | 30 764 | 110 705 | 511 422 | 2 974 | 1 / 9 |
| 2 | 2026-11-12 10:29 | 7278 s | 7 149 | 499 990 | 511 501 | 1 852 | 0 / 0 |
| 3 | 2026-11-12 12:30 | 7278 s | 5 769 | 505 264 | 510 416 | 1 787 | 0 / 1 |
| 4 | 2026-11-12 14:31 | 7278 s | 5 343 | 504 678 | 509 009 | 1 800 | 0 / 0 |

Total 49 025 runs, 2026-11-12 08:28 to 16:33 (Berlin). ft stopped growing after block 1 (511 422, then 511 501, 510 416,
509 009: each block is a new libFuzzer process, so the values are per block). The throughput fell from about 4 to under 1
input per second after block 1: the corpus is dominated by slow inputs.

## Artifacts

1 crash, 10 timeouts, 53 `slow-unit-*`. Replay with the unmodified build, idle machine (`triage.txt`, times include about
0.2 s .NET start). Range per README "Range domains", judged over the whole input as the target does (`range-class.txt`,
`range-classify.py`).

### Crash `crash-81de73cb06453617f1e5a3c2ca759d67ddffe19b` (rounding-repair): known class, no new issue

`IsClean but 8 degenerate triangles (at the rounding's scale)`. Two tetrahedra; A has an extent of 3.6e-307 at a position
of 2e-34 (a disparity of 2^-906 between a component's extent and its position). This is the class of FINDINGS.md items
5a / 6 (the rounding repair's range check, held until #513), not a new finding.

### 10 timeouts (robust-repair 4, robust-repair-soup 6): #712

All inside the robust repair's range, all finish and pass their check (rc 0), 5.3 s to 52.3 s, on 4 to 20 vertices.
One group (no message; the same module). Two soup inputs contain a component whose corners all coincide.

### slow-unit-* (53)

`slow-timing.txt`: 1.0 s to 5.9 s; 5 above 5 s (robust-repair-soup 3, rounding-repair 2). The same operations as the
timeouts, so part of #712, not a separate item.
