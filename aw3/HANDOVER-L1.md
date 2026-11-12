# Alpha Wrap 3.0: hand-over after PR 2 (2026-11-12)

State: PR 0 #651 merged (per-phase keys, wrappipe with KeepClearOf). PR 1 (L3) dropped by the owner (L3-SPIKE.md).
PR 2 #670 (L4, manifold step) under review: 269/269 identical, trial + manifold 0.49x, pipeline 0.93x. Targets (owner
2026-11-12): (a) >= 95 % of the 800 within budget on the reference machine, (b) about 1.25x on the 189 vs master on
4 cores, (c) guarantees, angles, fidelity, determinism unchanged; L1, L3b, L2 bit-identical to master.

Next, one PR each, from master: L1 (oracle + touch, 15.5 % + 12.1 % of the 189), L3b (parallel certification, 6.5 %,
bit-identical: per-triangle bound, max-reduce), L2 (insertion, 12.2 %; per-insertion only, no BRIO).

## Tools (this branch)
- `aw3/drive.sh <list> <out>`: wrappipe one process at a time (BENCH_FIDELITY=hd), resumable. Lists: list_over.txt (189),
  list_rest80.txt, list_269.txt, l4/list_ab.txt (24: 16 random + 8 pinch-heavy).
- `aw3/l4/ab.sh <list> <out>`: baseline (a master worktree with the bench built, path in the script) and candidate
  alternating per mesh; `aw3/l4/ab_analyze.py`: identical output (after_hash) and time ratios.
- Baseline hashes: prof_over/prof_rest80.jsonl (algorithm = master before #670 incl. KeepClearOf); l4_269.jsonl after #670
  (identical). Phase keys: `wrap/intersection`, `/touch`, `/insert`, `/carve`, `/trial` per phase (#651).
- Meshes: `python3 benchmarks/MeshRepairBenchmark/fetch_data.py --sample 800 --seed 2611 --no-large` (retry on resets).
- Machine factor: this container 1.136x slower than the f800 machine (cal.jsonl).

## L1 notes (not started)
- AlphaWrapOracle is read-only (thread-safe). FirstEntry: segment vs capsules / slab per primitive under a BVH with an
  extra slab axis per node; TetrahedronTouches: SAT per primitive. 2.9 µs per test (median).
- touch is cached per cell (stamp) and prefiltered by vertex distances (Lipschitz); 48367 is 46 % touch.
- Ideas to measure first (upper bound before a spike): share of FirstEntry calls returning -1 and their cost; a
  Lipschitz reject from cached circumcenter distances per cell (only bit-identical with a margin larger than
  FirstEntry's rounding: needs an argument); traversal order / leaf layout (SoA, see BatchQueryBenchmark #448).
