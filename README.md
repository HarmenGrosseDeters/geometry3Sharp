# runs/plane-kernel-study (data branch, never merged)

Probe code and raw numbers of docs/research/PLANE_BASED_KERNEL_STUDY_2026-11.md. No meshes.

- `probe/phases.py <dir>`: per-phase sums over the 294 T6 booleans (median of 3 rounds per boolean), from `phases.csv` and
  `cases.csv` of `runs/exact-numbers:runs/exact-numbers/results/ab-pr962-vs-master-2026-11-16/2-pr962-bf34eff7/`.
  Output: `results/phases_pr962_owner_1_4_12.txt`.
- `probe/gridbits.py <meshes dir>`: per raw model, the bits B of the smallest common power-of-two grid of its coordinates
  (columns: id, STL kind, B, max exponent). Models: the 190 Thingi10K models of the T6 set, fetched with
  `python3 benchmarks/BooleanComparison/perf/t6.py pairs t6pairs.csv && python3 perf/t6.py fetch <dir> t6pairs.csv`.
  Output: `results/gridbits_t6_raw.txt`.
