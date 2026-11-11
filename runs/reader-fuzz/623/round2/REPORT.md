# Reader fuzzing issue #623, round 2

Each target ran 200000 iterations (round 1 on this branch: 100000). Environment: WSL2 Ubuntu 24.04 (x86-64), .NET SDK 10.0.404,
SharpFuzz.CommandLine 2.2.0, launcher SHA-256 c2c2a90d94c409a4af339a0d4f244e0442c5a5d249be0f1252ba07871f285958 (checked).
Seconds include build, seeds and instrumentation. No artifacts; every run exited 0.

| Target | Seconds | INITED ft | DONE ft | Artifacts |
|---|---:|---:|---:|---:|
| stl | 112 | 603 | 5934 | 0 |
| obj | 65 | 603 | 5346 | 0 |
| off | 25 | 456 | 2719 | 0 |
| ply | 30 | 679 | 4796 | 0 |
| 3mf | 24 | 585 | 1061 | 0 |
| glb | 27 | 868 | 4474 | 0 |
| g3mesh | 25 | 628 | 1814 | 0 |
| minijson | 22 | 171 | 869 | 0 |
| esri | 18 | 124 | 409 | 0 |
| xyz | 18 | 130 | 624 | 0 |
| poly | 23 | 660 | 3209 | 0 |
| node | 21 | 69 | 333 | 0 |
| ele | 20 | 48 | 225 | 0 |
| mcp | 21 | 275 | 490 | 0 |

Growth from 100k to 200k iterations is small (the DONE counter rose by about 1 to 15 percent on most targets; the 3mf
and g3mesh counters moved by less than the iteration doubling would suggest), so the stop rule's "ft stopped growing" holds
approximately, not exactly. The round-1 table above is the same campaign at 100k.

Validation before the campaign: the pilot's six Python tests pass; `verify_crash_path.py` needed the anchor update of #630
(the synthetic crash was saved and reproduced). The run used the branch `local/623-reader-fuzz` with that update, not master.

Raw output: `<target>.log` is the libFuzzer log of the run (`fuzz.log`); `summary.tsv` has the table as data.
No corpus, crash or instrumented files are committed.
