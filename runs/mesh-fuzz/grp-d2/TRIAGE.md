# MeshFuzzPilot campaign grp-d2: alpha-wrap, partitioned-reducer (second round)

Issue #624, second round of (d) after #796 (bound 2^150 of the partitioned-reducer scale claim, #794). Build: master
6413529f (includes 09783d1). `MESH_FUZZ_OPS=alpha-wrap,partitioned-reducer`, 3 workers, one 8 h block on a fresh
work directory (2026-11-15 04:34 to 12:35 Berlin).

- Runs: 409 814 (last progress line); cov 121 906, ft 394 744, corp 8 621.
- **No crash, no timeout, no OOM, no slow unit.**
- The first round (`../grp-d`) had 82 crashes of one class (#794); none of them recurs with this build.
