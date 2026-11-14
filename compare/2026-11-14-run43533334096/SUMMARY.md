# Tet mesher comparison, run 43533334096

- geometry3Sharp commit: 2fea6ac7945ab6671900a1cbe47b594c0fe334c1 (benchmarks/TetMeshBenchmark, modes ours / eval)
- fTetWild commit: 8118f810478e0e65a7bf2d8cecdc5e203a876e97 (black box, MPL-2.0; built and run on the hosted runner only; outputs deleted after evaluation)
- Command per mesh: `FloatTetwild_bin -i <id>.stl -o <out>.mesh --max-threads N --no-binary --level 3` (its defaults: -l 0.05, -e 1e-3), N in "4" (column ftN); ours: `TetMeshBenchmark ours <id>.stl 20 600` (serial)
- Timeout per mesh and variant: 1800 s; 4 shards, one runner each (ubuntu-latest, see machines.txt); within a shard one mesher at a time
- Checks are ours (TetFiles.Evaluate): exact orientation, matched facets, closed manifold boundary, exact boundary self-intersection test

| variant | meshes | output | valid (exact) | inverted tets (sum) | time median / max s | tets median | min dihedral worst: median / best mesh | p01 dihedral median | meshes with every tet >= 10 deg | share of tets < 10 deg (median) | dev out max (median, rel. diag) | dev in max (median) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ours (1 thr) | 40 | 40 | 40 | 0 | 0.316 / 5.31 | 3444 | 0.223 / 1.29 | 2.31 | 0 | 0.0941 | 0.00167 | 0.0313 |
| ft4 (4 thr) | 40 | 0 | 0 | 0 | 0.00736 / 0.00777 | - | - / - | - | 0 | - | - | - |
| ft1 ( thr) | 0 | 0 | 0 | 0 | - / - | - | - / - | - | 0 | - | - | - |

Not valid or no output:

- ft4 100026.stl: NO_OUTPUT_rc127
- ft4 103538.stl: NO_OUTPUT_rc127
- ft4 1066900.stl: NO_OUTPUT_rc127
- ft4 110372.stl: NO_OUTPUT_rc127
- ft4 113956.stl: NO_OUTPUT_rc127
- ft4 120628.stl: NO_OUTPUT_rc127
- ft4 1288649.stl: NO_OUTPUT_rc127
- ft4 132560.stl: NO_OUTPUT_rc127
- ft4 1382599.stl: NO_OUTPUT_rc127
- ft4 1489591.stl: NO_OUTPUT_rc127
- ft4 1614065.stl: NO_OUTPUT_rc127
- ft4 186204.stl: NO_OUTPUT_rc127
- ft4 208721.stl: NO_OUTPUT_rc127
- ft4 236172.stl: NO_OUTPUT_rc127
- ft4 265729.stl: NO_OUTPUT_rc127
- ft4 301798.stl: NO_OUTPUT_rc127
- ft4 347926.stl: NO_OUTPUT_rc127
- ft4 37622.stl: NO_OUTPUT_rc127
- ft4 39579.stl: NO_OUTPUT_rc127
- ft4 416682.stl: NO_OUTPUT_rc127
- ft4 44041.stl: NO_OUTPUT_rc127
- ft4 46460.stl: NO_OUTPUT_rc127
- ft4 472179.stl: NO_OUTPUT_rc127
- ft4 49884.stl: NO_OUTPUT_rc127
- ft4 51809.stl: NO_OUTPUT_rc127
- ft4 55429.stl: NO_OUTPUT_rc127
- ft4 58733.stl: NO_OUTPUT_rc127
- ft4 613009.stl: NO_OUTPUT_rc127
- ft4 65285.stl: NO_OUTPUT_rc127
- ft4 68659.stl: NO_OUTPUT_rc127
- ft4 70379.stl: NO_OUTPUT_rc127
- ft4 73153.stl: NO_OUTPUT_rc127
- ft4 75915.stl: NO_OUTPUT_rc127
- ft4 78322.stl: NO_OUTPUT_rc127
- ft4 81132.stl: NO_OUTPUT_rc127
- ft4 839690.stl: NO_OUTPUT_rc127
- ft4 88564.stl: NO_OUTPUT_rc127
- ft4 91589.stl: NO_OUTPUT_rc127
- ft4 94240.stl: NO_OUTPUT_rc127
- ft4 97168.stl: NO_OUTPUT_rc127

Per mesh (time s / tets / worst min dihedral deg / p01 deg / dev out max rel):

| mesh | ours | ft4 | ft1 |
|---|---|---|---|
| 100026.stl | 0.358 / 2269 / 0.44 / 3.15 / 0.00167 | NO_OUTPUT_rc127 |  |
| 103538.stl | 0.407 / 4796 / 0.23 / 1.97 / 0.00167 | NO_OUTPUT_rc127 |  |
| 1066900.stl | 0.272 / 1243 / 1.55e-11 / 1.65 / 0.00167 | NO_OUTPUT_rc127 |  |
| 110372.stl | 0.457 / 3382 / 0.203 / 1.86 / 0.00167 | NO_OUTPUT_rc127 |  |
| 113956.stl | 0.488 / 5236 / 0.121 / 1.8 / 0.00167 | NO_OUTPUT_rc127 |  |
| 120628.stl | 5.31 / 4546 / 0.521 / 2.23 / 0.00167 | NO_OUTPUT_rc127 |  |
| 1288649.stl | 0.367 / 3558 / 0.0485 / 3.01 / 0.00167 | NO_OUTPUT_rc127 |  |
| 132560.stl | 0.208 / 1835 / 0.891 / 2.34 / 0.00167 | NO_OUTPUT_rc127 |  |
| 1382599.stl | 0.398 / 4122 / 0.119 / 2.37 / 0.00167 | NO_OUTPUT_rc127 |  |
| 1489591.stl | 0.185 / 3559 / 0.224 / 3.4 / 0.00167 | NO_OUTPUT_rc127 |  |
| 1614065.stl | 0.279 / 2512 / 0.0108 / 2.61 / 0.00167 | NO_OUTPUT_rc127 |  |
| 186204.stl | 0.285 / 2406 / 0.166 / 2.85 / 0.00167 | NO_OUTPUT_rc127 |  |
| 208721.stl | 0.396 / 4265 / 0.0777 / 0.71 / 0.00167 | NO_OUTPUT_rc127 |  |
| 236172.stl | 0.349 / 8534 / 0.0823 / 1.64 / 0.00167 | NO_OUTPUT_rc127 |  |
| 265729.stl | 0.387 / 3063 / 0.419 / 2.18 / 0.00167 | NO_OUTPUT_rc127 |  |
| 301798.stl | 0.699 / 5324 / 0.145 / 2.16 / 0.00167 | NO_OUTPUT_rc127 |  |
| 347926.stl | 0.347 / 2932 / 0.399 / 2.84 / 0.00167 | NO_OUTPUT_rc127 |  |
| 37622.stl | 0.126 / 2111 / 0.261 / 3.63 / 0.00167 | NO_OUTPUT_rc127 |  |
| 39579.stl | 0.156 / 1559 / 0.229 / 1.9 / 0.00167 | NO_OUTPUT_rc127 |  |
| 416682.stl | 0.2 / 2190 / 0.0285 / 4.24 / 0.00167 | NO_OUTPUT_rc127 |  |
| 44041.stl | 0.186 / 1754 / 0.247 / 2.86 / 0.00167 | NO_OUTPUT_rc127 |  |
| 46460.stl | 0.68 / 5461 / 0.647 / 2.4 / 0.00167 | NO_OUTPUT_rc127 |  |
| 472179.stl | 0.192 / 894 / 0.64 / 2.28 / 0.00167 | NO_OUTPUT_rc127 |  |
| 49884.stl | 0.261 / 3964 / 0.336 / 2.58 / 0.00167 | NO_OUTPUT_rc127 |  |
| 51809.stl | 0.663 / 3640 / 0.996 / 3.22 / 0.00167 | NO_OUTPUT_rc127 |  |
| 55429.stl | 0.235 / 3798 / 0.236 / 2.19 / 0.00167 | NO_OUTPUT_rc127 |  |
| 58733.stl | 0.619 / 6408 / 0.0846 / 2.16 / 0.00167 | NO_OUTPUT_rc127 |  |
| 613009.stl | 0.49 / 3599 / 0.153 / 1.9 / 0.00167 | NO_OUTPUT_rc127 |  |
| 65285.stl | 0.664 / 3505 / 0.222 / 2.4 / 0.00167 | NO_OUTPUT_rc127 |  |
| 68659.stl | 0.117 / 1714 / 0.236 / 2.89 / 0.00167 | NO_OUTPUT_rc127 |  |
| 70379.stl | 0.1 / 663 / 1.29 / 2.36 / 0.00167 | NO_OUTPUT_rc127 |  |
| 73153.stl | 0.163 / 1349 / 0.42 / 2.4 / 0.00167 | NO_OUTPUT_rc127 |  |
| 75915.stl | 0.142 / 1447 / 0.153 / 2.28 / 0.00167 | NO_OUTPUT_rc127 |  |
| 78322.stl | 0.209 / 3521 / 0.0864 / 2.67 / 0.00167 | NO_OUTPUT_rc127 |  |
| 81132.stl | 0.159 / 1689 / 0.415 / 4.06 / 0.00167 | NO_OUTPUT_rc127 |  |
| 839690.stl | 0.468 / 7206 / 0.0758 / 1.04 / 0.00167 | NO_OUTPUT_rc127 |  |
| 88564.stl | 0.789 / 13176 / 0.0498 / 0.917 / 0.00167 | NO_OUTPUT_rc127 |  |
| 91589.stl | 0.0935 / 1214 / 0.287 / 1.86 / 0.00167 | NO_OUTPUT_rc127 |  |
| 94240.stl | 0.518 / 5999 / 0.0891 / 1.63 / 0.00167 | NO_OUTPUT_rc127 |  |
| 97168.stl | 0.0882 / 338 / 3.13e-13 / 1.59 / 0.00167 | NO_OUTPUT_rc127 |  |
