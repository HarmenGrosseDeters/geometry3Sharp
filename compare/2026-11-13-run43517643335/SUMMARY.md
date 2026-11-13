# Tet mesher comparison, run 43517643335

- geometry3Sharp commit: d9465e42d50d52ab911517dcd8f4e507a93282fb (benchmarks/TetMeshBenchmark, modes ours / eval)
- fTetWild commit: 8118f810478e0e65a7bf2d8cecdc5e203a876e97 (black box, MPL-2.0; built and run on the hosted runner only; outputs deleted after evaluation)
- Command per mesh: `FloatTetwild_bin -i <id>.stl -o <out>.mesh --max-threads N --no-binary --level 3` (its defaults: -l 0.05, -e 1e-3), N in "4" (column ftN); ours: `TetMeshBenchmark ours <id>.stl 20 600` (serial)
- Timeout per mesh and variant: 60 s; 1 shards, one runner each (ubuntu-latest, see machines.txt); within a shard one mesher at a time
- Checks are ours (TetFiles.Evaluate): exact orientation, matched facets, closed manifold boundary, exact boundary self-intersection test

| variant | meshes | output | valid (exact) | inverted tets (sum) | time median / max s | tets median | min dihedral worst: median / best mesh | p01 dihedral median | meshes with every tet >= 10 deg | share of tets < 10 deg (median) | dev out max (median, rel. diag) | dev in max (median) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ours ( thr) | 0 | 0 | 0 | 0 | - / - | - | - / - | - | 0 | - | - | - |
| ft4 ( thr) | 0 | 0 | 0 | 0 | - / - | - | - / - | - | 0 | - | - | - |
| ft1 ( thr) | 0 | 0 | 0 | 0 | - / - | - | - / - | - | 0 | - | - | - |

Per mesh (time s / tets / worst min dihedral deg / p01 deg / dev out max rel):

| mesh | ours | ft4 | ft1 |
|---|---|---|---|
