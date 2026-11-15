# grp-f, resumed 2026-11-14 (4 h on the corpus, 10:10 to 14:12 Berlin)

Continuation of `../TRIAGE.md` (the first 4 h, 60 crashes). Same `MESH_FUZZ_OPS=quality-mesh2,clipper2`, 3 workers,
fresh libFuzzer seed 20261114 on the corpus of the first part.

- Final, all four hours: 1 777 937 + 1 386 674 runs, 144 crash files, no timeout, no OOM, 14 `slow-unit-*`.
- Replay of all 144 with master 6413529f (`triage-master.txt`): **144 of 144 still fail**.

| Class | Crashes | Message | Issue |
|---|---|---|---|
| Q1 | 45 | oracle `MappingWrong` (coincident inputs map differently) | #863 |
| Q2 | 65 | oracle `NotDelaunayGlobal` (2 of them also `NotDelaunayLocal`, which is the class of #728 but on fuzzed input) | #864 |
| C1 | 26 | clipper2 Offset orientation | known, FINDINGS.md 5 |
| C2 | 8 | clipper2 Union sample points | known, FINDINGS.md 5 (owner decision 2026-11-11) |

`classes2.txt` lists every artifact with its class; `dump-*.txt` the decoded input of every crash.
