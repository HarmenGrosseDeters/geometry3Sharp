# JOB-1 label B1-F1: capture not finished, stopped by the owner

**Result:** no capture time. The coverage capture (filter F1, 171 tests) ran from 03:56:47 to 07:20:29 Berlin when the
owner stopped it (3 h 23 min 42 s, lower bound). The line "total mutants will be tested" never appeared, so there is no
mutant phase, no score and no exit code from Stryker. The stop was manual (processes matched by their config name),
not the job's stop rule. `results/B1-F1.exit` says 127 because of how the stopped run was closed; treat it as "stopped".

| Key number | Value |
|---|---|
| Machine | i7-8700, 6 cores / 12 threads, 15.7 GB RAM, Windows 11, .NET 10.0.401 (see `results/machine.txt`) |
| Tests found (F1) | 171 (full part-4 filter: 174) |
| Mutants created | 128 351 |
| Capture (coverage pass) | 03:56:47 to 07:20:29 Berlin, not finished: at least 3 h 24 min |
| Mutants to test / time per mutant | not reached |
| Score | none |

**Comparison with label A** (full filter, 174 tests, same machine): its coverage capture took 6 h 33 min
(20:11:45 to 02:44:53). B1 was still running after 3 h 24 min, so it is consistent with A, but there is no number for
F1's capture time. Label B2 was removed from the chain (see `README.md`).
