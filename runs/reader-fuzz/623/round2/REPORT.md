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

ft growth from 100k to 200k (the last libFuzzer line at or before 100k, and the DONE line):

| Target | ft at <=100k | ft at end | growth |
|---|---:|---:|---:|
| stl | 5507 | 5934 | +8 % |
| obj | 5219 | 5346 | +2 % |
| off | 2597 | 2719 | +5 % |
| ply | 4696 | 4796 | +2 % |
| 3mf | 934 | 1061 | +14 % |
| glb | 4353 | 4474 | +3 % |
| g3mesh | 1542 | 1814 | +18 % |
| minijson | 768 | 869 | +13 % |
| esri | 389 | 409 | +5 % |
| xyz | 590 | 624 | +6 % |
| poly | 2889 | 3209 | +11 % |
| node | 323 | 333 | +3 % |
| ele | 225 | 225 | 0 % |
| mcp | 364 | 490 | +35 % |

Every target except `ele` still grew between 100k and 200k, so the doubling rule would call for a further round on those
targets. The stop rule ("two rounds over all targets") ends the campaign after round 2, as planned; the growth is recorded
here for the owner's decision on a third round.

Validation before the campaign: the pilot's six Python tests pass; `verify_crash_path.py` needed the anchor update of #630
(the synthetic crash was saved and reproduced). The run used the branch `local/623-reader-fuzz` with that update, not master.

Raw output: `<target>.log` is the libFuzzer log of the run (`fuzz.log`); `summary.tsv` has the table as data.
No corpus, crash or instrumented files are committed.
