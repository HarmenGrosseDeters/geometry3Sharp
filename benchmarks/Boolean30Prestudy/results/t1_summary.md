# T1 scaling levers: before / after (4-core cloud machine, alternating base / new, median of 5 rounds, best of 3 repeats per run)

Cases: procedural sphere pairs (2 x about 58k triangles; `welded` 2 x about 600k for the box trees), union, SafeRounding on,
`t1_ab.sh` / `t1_ab_analyze.py`. Result hashes equal on every run. Times in ms.

| case | cores | build | total | input | intersections.trees | assembly |
|---|---|---|---|---|---|---|
| soup (unwelded vertices) | 1 | base | 963 | 108 | 67 | 96 |
| soup | 1 | new | 963 | 80 | 67 | 89 |
| soup | 4 | base | 384 | 41 | 19 | 56 |
| soup | 4 | new | 399 | 24 | 26 | 68 |
| flipped (every 7th triangle) | 1 | base | 1296 | 387 | 64 | 98 |
| flipped | 1 | new | 1067 | 185 | 72 | 86 |
| flipped | 4 | base | 488 | 151 | 16 | 47 |
| flipped | 4 | new | 428 | 92 | 25 | 47 |
| open (2 % missing) | 1 | base | 4053 | 271 | 66 | 90 |
| open | 1 | new | 3689 | 98 | 66 | 87 |
| open | 4 | base | 1301 | 91 | 14 | 46 |
| open | 4 | new | 1047 | 30 | 16 | 44 |
| welded, large (box trees) | 4 | base | 2334 | 37 | 138 | 362 |
| welded, large | 4 | new | 2128 | 20 | 94 | 374 |

- Input preparation (hash weld, edge table, meshes in parallel): input phase -45 % to -67 % (flipped 1 core 387 -> 185,
  open 4 cores 91 -> 30); soup is within noise at 4 cores. The tree and assembly columns of the 58k cases run the same
  code in both builds (the parallel tree build starts at 262144 boxes): differences there are noise (about 10 ms).
- Box trees: parallel from 262144 boxes; at 600k triangles per mesh -32 % (138 -> 94 ms, 2 % of the op). At 170k triangles
  per mesh the parallel build was not faster (42-52 vs 42-76 ms), hence the threshold.
- Assembly (profile at 170k triangles per mesh, 4 cores, ms): group_edges 26-42 (the radix sort shared with the
  classification), appending triangles to the DMesh3 47-73, corner-slot union-find 11-14, vertices 13, flat-region
  simplify 15-24. Nothing of it is a per-region loop worth the risk: simplify is at most 3-4 % of the op, the
  triangle appends are serial inside DMesh3; the sort is the Opus lever. Dropped (no change).
