# Reader fuzzing issue #623, round 1

Base: 85a3a49fa32cadfb4548b1c1037b8cf0f11c3d53. Each target ran 100000 iterations; wall time includes build, seed generation and instrumentation. No artifacts.

| Target | Seconds | INITED ft | Last ft at or before 90k | DONE ft | Artifacts |
|---|---:|---:|---:|---:|---:|
| stl | 60 | 604 | 5697 | 5702 | 0 |
| obj | 39 | 607 | 5201 | 5232 | 0 |
| off | 21 | 457 | 2582 | 2593 | 0 |
| ply | 22 | 680 | 4468 | 4477 | 0 |
| 3mf | 16 | 586 | 955 | 955 | 0 |
| glb | 17 | 866 | 4155 | 4210 | 0 |
| g3mesh | 18 | 629 | 1428 | 1431 | 0 |
| minijson | 14 | 171 | 772 | 774 | 0 |
| esri | 14 | 124 | 395 | 395 | 0 |
| xyz | 14 | 130 | 604 | 622 | 0 |
| poly | 18 | 655 | 3145 | 3155 | 0 |
| node | 15 | 69 | 305 | 305 | 0 |
| ele | 15 | 48 | 225 | 225 | 0 |
| mcp | 15 | 275 | 336 | 336 | 0 |

Validation: SDK 10.0.403; launcher SHA-256 c2c2a90d94c409a4af339a0d4f244e0442c5a5d249be0f1252ba07871f285958. The six Python tests passed. The original synthetic-crash verifier failed before its run because of source drift (#630). A local temporary update to its injection anchor retained its crash and replay checks; that check passed. The tool was then restored. No second round was started at the owner's request.
