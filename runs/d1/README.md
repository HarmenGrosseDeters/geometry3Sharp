# D1 working data (implementing session, Exact Boolean 2.0; handed to the D1 drift-lemma session)

Not committed meshes; ids are Thingi10K file ids with their #385 pairing (BooleanComparison export).

- `master_d1.jsonl`, `apex_d1.jsonl`, `crease_d1.jsonl`: one row per op of the 14 D1 models (100344, 1012236, 1053875, 118037,
  129937, 1396900, 1514904, 472043, 472046, 548875, 548878, 551020, 669979, 686056; all three operations), the rounding counters per build: master before apex charging, the apex-charging build (#416 / eb8d852e),
  and a crease variant that was dropped. Fields as `benchmarks/LevyClassification/Program.cs` rows (phases_ms,
  rounding counters, max drift).
- `d1_r1.jsonl`: the first trace round of the D1 analysis (4 ops).
- `d1tally.py`: tallies per remaining degenerate triangle the attempts of its last FIX block and their outcome codes,
  from a trace log of the instrumented rounding (FIX / R<n> / REMAINING lines).
- `d1repro_Program.cs`, `d1repro.csproj`, `d1repro_log.txt`: the first procedural reproducer attempt (a needle chain
  in a cluster) and its output.
