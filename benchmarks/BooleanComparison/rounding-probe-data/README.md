# Rounding probes: data (measurement only, branch runs/rounding-probes)

- `proc_screen.jsonl`: chain-exact-proc mixed, seeds 1..30, 8 steps, at master c57dd5d2 (today's chain and one pass per step).
- `proc_probe.jsonl`, `proc_summary.md`: rounding-probe-proc for the 12 (seed, step) results with step >= 5 that are unclean in the one pass and clean chained.
- `abc_report_run-44614769429.md`: the ABC step-3 probes (6 chains), GitHub run 44614769429; rows on branch runs/abc-chained, folder run-44614769429. Case ids and numbers only.
Reproduce: `rounding_probe.py screen|proc|summary`; ABC: workflow abc-chained.yml, methods=ours_probe.
