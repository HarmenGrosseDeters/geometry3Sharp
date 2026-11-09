# JOB-1 label A-part4: not a valid kill measurement

**Result:** Stryker finished with exit code 0, but all 282 mutants to test ended as `Timeout` (0 killed). The score
97.24 % counts timeouts as detected and must not be read as a kill rate. Cause open; see the open question below.

| Key number | Value |
|---|---|
| Machine | i7-8700, 6 cores / 12 threads, 15.7 GB RAM, Windows 11, .NET 10.0.401 (see `results/machine.txt`) |
| Capture (coverage pass, one test host) | 20:11:45 to 02:44:53 Berlin = 6 h 34 min (394 min) |
| Tests found | 174 (job reference: 147) |
| Mutants created / to test | 128 351 / 282 |
| Time per mutant | not measurable: all 282 ended as timeouts, and the mutant phase took about 5 s by the log times |
| Statuses | Timeout 282, NoCoverage 8 (ignored 1 409, compile error 295 are Stryker's own filters) |
| Score (Stryker formula, timeouts count) | 97.24 % |
| Exit code | 0 (`results/A-part4.exit`) |
| Concurrency / GC limit | 3 / 4 GB per test host (as the job) |

**Open question:** the job expects 1 to 3 h for the coverage pass; this one took 6.6 h on one test host, and the
mutant phase looks too short for 282 real runs. The full log was overwritten by label B1 (the job's
`rm -rf StrykerOutput`), so the cause cannot be checked from this run. The report (`results/A-part4-report.json`)
gives the statuses per mutant; the line spread is in the README.
