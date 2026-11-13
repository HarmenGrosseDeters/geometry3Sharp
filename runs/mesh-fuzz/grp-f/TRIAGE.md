# MeshFuzzPilot campaign grp-f: quality-mesh2, clipper2 (stopped after 4 h)

Issue #624. Raw output only: logs, replay summaries (campaign build and master), the class of each crash (`classes.txt`:
class, size, file), every dump. No corpus and no crash files. Same host, tools and validation as grp-a.
`MESH_FUZZ_OPS=quality-mesh2,clipper2`, 3 fork workers, one block planned for 8 h.

## Campaign

- 2026-11-13 18:40 to 22:49 (Berlin), 14 886 s: **stopped by the owner** (machine shut down) at about half of the block.
- 1 777 937 runs; cov 9 389 (flat after the first minutes), ft 42 064, corp 3 575.
- Artifacts: 60 crash files, no timeout, no OOM, 6 `slow-unit-*`.
- The corpus, the instrumented build and the artifacts are kept in the container (`/work/grp-f`) for a resume.

## Triage

Replay of all 60 with the campaign build (05d1332b, `triage.txt`) and with master 6413529f (`triage-master.txt`): all 60
fail on both, in four classes.

| Class | Op | Crashes | Message | Smallest | Issue |
|---|---|---|---|---|---|
| Q1 | quality-mesh2 | 21 | `MappingWrong`: inputs coincide but map differently | `crash-da38935e0fda` | #863 |
| Q2 | quality-mesh2 | 25 | `NotDelaunayGlobal` (6 with `AngleBelowBound`) | `crash-7e1184c25bdf` | #864 |
| C1 | clipper2 | 7 | Offset: an outer ring is not counterclockwise or a hole is not clockwise | `crash-c860fe6e2667` | known: FINDINGS.md 5, "Open, minor" |
| C2 | clipper2 | 7 | Union: sample points in the result that neither input fills | `crash-ed531523f499` | known: FINDINGS.md 5, "Clipper2 2.0.0 itself" (owner decision 2026-11-11) |
