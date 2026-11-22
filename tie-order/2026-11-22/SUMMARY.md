# Tie-order measurement of the tet improver, 2026-11-22

Data branch (no PR, never merged; no meshes). Traces of `benchmarks/TetMeshBenchmark/compare/run_tie_order.sh` on master e09cf478 plus the
harness of branch `research/tet-tie-order` (note `docs/research/TET_TIE_ORDER_2026-11.md`, which has the commands and the tables).

- `tie-order.csv`, `tie-order-trace.csv`: the six inputs of the note's sections 2 and 3, order 0 and 8 relabelings
  (trace columns: name, floor, order, visits, tets below the floor, worst dihedral, phase).
- `tie-order-subset40-floor10-trace.csv`, `tie-order-rescued-floor10-trace.csv`: the traces of the two population runs of section 3.

Summary: `python3 benchmarks/TetMeshBenchmark/compare/summarize_tie_order.py tie-order.csv tie-order-trace.csv` (script on the PR branch).
