# Contouring certificate block growth: timing and counts

- Commit: c7ea64f (branch claude/contouring-cert-stage3, issue #944, PR #946)
- Script: benchmarks/ContouringCertificate/run_growth.sh /tmp/w
- Inputs: 20 Thingi10K meshes fetched at run time (not committed)
- Runs: solid, three repetitions (growth_rep1.jsonl, growth_rep2.jsonl, growth_rep3.jsonl, 52 lines each)
- Start: 2026-11-15 ~18:08 UTC (19:08 Europe/Berlin), launched after SDK install and checkout
- End: 2026-11-15 18:14 UTC (19:14 Europe/Berlin), exit code 0, single uninterrupted run (no resume needed)
- nproc: 4
- CPU: Intel(R) Xeon(R) Processor @ 2.10GHz
- Tables: TABLES.md (printed output of the script)
