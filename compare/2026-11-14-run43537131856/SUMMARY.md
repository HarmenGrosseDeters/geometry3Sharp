# Tet mesher comparison, run 43537131856

- geometry3Sharp commit: a307db28df8503daca742753a0f676c7111b42b0 (benchmarks/TetMeshBenchmark, modes ours / eval)
- fTetWild commit: 8118f810478e0e65a7bf2d8cecdc5e203a876e97 (black box, MPL-2.0; built and run on the hosted runner only; outputs deleted after evaluation)
- Command per mesh: `FloatTetwild_bin -i <id>.stl -o <out>.mesh --max-threads N --no-binary --level 3` (its defaults: -l 0.05, -e 1e-3), N in "4" (column ftN); ours: `TetMeshBenchmark ours <id>.stl 20 600` (serial)
- Timeout per mesh and variant: 30 s; 1 shards, one runner each (ubuntu-latest, see machines.txt); within a shard one mesher at a time
- Checks are ours (TetFiles.Evaluate): exact orientation, matched facets, closed manifold boundary, exact boundary self-intersection test

| variant | meshes | output | valid (exact) | inverted tets (sum) | time median / max s | tets median | min dihedral worst: median / best mesh | p01 dihedral median | meshes with every tet >= 10 deg | share of tets < 10 deg (median) | dev out max (median, rel. diag) | dev in max (median) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ours (1 thr) | 40 | 40 | 40 | 0 | 0.342 / 8.09 | 3444 | 0.223 / 1.29 | 2.31 | 0 | 0.0941 | 0.00167 | 0.0313 |
| ft4 (4 thr) | 40 | 32 | 26 | 0 | 11 / 30.1 | 9473 | 11.3 / 22.8 | 20.3 | 28 | 0 | 0.000548 | 0.000981 |
| ft1 ( thr) | 0 | 0 | 0 | 0 | - / - | - | - / - | - | 0 | - | - | - |

Not valid or no output:

- ft4 103538.stl: TIMEOUT
- ft4 110372.stl: a boundary edge occurs twice with the same direction
- ft4 120628.stl: TIMEOUT
- ft4 1382599.stl: TIMEOUT
- ft4 236172.stl: TIMEOUT
- ft4 265729.stl: a boundary edge occurs twice with the same direction
- ft4 301798.stl: TIMEOUT
- ft4 44041.stl: a boundary edge occurs twice with the same direction
- ft4 46460.stl: TIMEOUT
- ft4 49884.stl: a boundary edge occurs twice with the same direction
- ft4 51809.stl: TIMEOUT
- ft4 58733.stl: a boundary edge occurs twice with the same direction
- ft4 81132.stl: a boundary edge occurs twice with the same direction
- ft4 88564.stl: TIMEOUT

Per mesh (time s / tets / worst min dihedral deg / p01 deg / dev out max rel):

| mesh | ours | ft4 | ft1 |
|---|---|---|---|
| 100026.stl | 0.368 / 2269 / 0.44 / 3.15 / 0.00167 | 2.39 / 24 / 21.2 / 21.2 / 8.46e-09 |  |
| 103538.stl | 0.634 / 4796 / 0.23 / 1.97 / 0.00167 | TIMEOUT |  |
| 1066900.stl | 0.136 / 1243 / 1.55e-11 / 1.65 / 0.00167 | 3.55 / 5021 / 11.6 / 19.8 / 0.000606 |  |
| 110372.stl | 0.339 / 3382 / 0.203 / 1.86 / 0.00167 | 13.5 / 33421 / 11 / 20.2 / 0.000611 INVALID |  |
| 113956.stl | 0.469 / 5236 / 0.121 / 1.8 / 0.00167 | 14 / 10790 / 11.5 / 20.5 / 0.000531 |  |
| 120628.stl | 8.09 / 4546 / 0.521 / 2.23 / 0.00167 | TIMEOUT |  |
| 1288649.stl | 0.361 / 3558 / 0.0485 / 3.01 / 0.00167 | 24.5 / 18795 / 10.1 / 18 / 0.00059 |  |
| 132560.stl | 0.15 / 1835 / 0.891 / 2.34 / 0.00167 | 1.94 / 3385 / 10.8 / 13.5 / 0.0004 |  |
| 1382599.stl | 0.396 / 4122 / 0.119 / 2.37 / 0.00167 | TIMEOUT |  |
| 1489591.stl | 0.268 / 3559 / 0.224 / 3.4 / 0.00167 | 8.35 / 14753 / 10 / 18.2 / 0.000529 |  |
| 1614065.stl | 0.284 / 2512 / 0.0108 / 2.61 / 0.00167 | 9.17 / 25392 / 11.1 / 24.4 / 0.000548 |  |
| 186204.stl | 0.289 / 2406 / 0.166 / 2.85 / 0.00167 | 5.02 / 8156 / 11 / 21.7 / 0.000538 |  |
| 208721.stl | 0.391 / 4265 / 0.0777 / 0.71 / 0.00167 | 16.8 / 11525 / 9.1 / 18.1 / 0.000549 |  |
| 236172.stl | 0.532 / 8534 / 0.0823 / 1.64 / 0.00167 | TIMEOUT |  |
| 265729.stl | 0.392 / 3063 / 0.419 / 2.18 / 0.00167 | 16.6 / 16827 / 7.22 / 20.4 / 0.000553 INVALID |  |
| 301798.stl | 0.705 / 5324 / 0.145 / 2.16 / 0.00167 | TIMEOUT |  |
| 347926.stl | 0.349 / 2932 / 0.399 / 2.84 / 0.00167 | 17.6 / 7211 / 13.6 / 17.1 / 0.000551 |  |
| 37622.stl | 0.174 / 2111 / 0.261 / 3.63 / 0.00167 | 0.922 / 5004 / 15.3 / 27.7 / 1.66e-08 |  |
| 39579.stl | 0.157 / 1559 / 0.229 / 1.9 / 0.00167 | 4.43 / 7043 / 12.2 / 18.7 / 0.000474 |  |
| 416682.stl | 0.203 / 2190 / 0.0285 / 4.24 / 0.00167 | 7.23 / 7388 / 14.8 / 23.9 / 0.000553 |  |
| 44041.stl | 0.184 / 1754 / 0.247 / 2.86 / 0.00167 | 10.5 / 16097 / 10.7 / 20.1 / 0.00742 INVALID |  |
| 46460.stl | 0.872 / 5461 / 0.647 / 2.4 / 0.00167 | TIMEOUT |  |
| 472179.stl | 0.195 / 894 / 0.64 / 2.28 / 0.00167 | 7.44 / 16883 / 11.9 / 19.9 / 0.000602 |  |
| 49884.stl | 0.264 / 3964 / 0.336 / 2.58 / 0.00167 | 7.55 / 22168 / 9.78 / 25.6 / 0.00918 INVALID |  |
| 51809.stl | 0.661 / 3640 / 0.996 / 3.22 / 0.00167 | TIMEOUT |  |
| 55429.stl | 0.346 / 3798 / 0.236 / 2.19 / 0.00167 | 20.8 / 14734 / 11.2 / 18.3 / 0.000542 |  |
| 58733.stl | 0.613 / 6408 / 0.0846 / 2.16 / 0.00167 | 23.5 / 55954 / 10.1 / 21.3 / 0.000596 INVALID |  |
| 613009.stl | 0.486 / 3599 / 0.153 / 1.9 / 0.00167 | 20.4 / 40892 / 12.6 / 24 / 0.000556 |  |
| 65285.stl | 0.68 / 3505 / 0.222 / 2.4 / 0.00167 | 24.7 / 21443 / 11.3 / 20.7 / 0.000595 |  |
| 68659.stl | 0.164 / 1714 / 0.236 / 2.89 / 0.00167 | 0.744 / 1202 / 10.3 / 12.7 / 9.5e-09 |  |
| 70379.stl | 0.101 / 663 / 1.29 / 2.36 / 0.00167 | 0.599 / 834 / 22.8 / 27.1 / 0.000469 |  |
| 73153.stl | 0.17 / 1349 / 0.42 / 2.4 / 0.00167 | 1.13 / 1285 / 20.8 / 26.2 / 0.000487 |  |
| 75915.stl | 0.148 / 1447 / 0.153 / 2.28 / 0.00167 | 1.42 / 2595 / 15.4 / 19 / 0.000225 |  |
| 78322.stl | 0.295 / 3521 / 0.0864 / 2.67 / 0.00167 | 2.87 / 6214 / 11.7 / 19.8 / 0.000512 |  |
| 81132.stl | 0.161 / 1689 / 0.415 / 4.06 / 0.00167 | 5.8 / 6722 / 10.9 / 21.9 / 0.000588 INVALID |  |
| 839690.stl | 0.489 / 7206 / 0.0758 / 1.04 / 0.00167 | 11.4 / 15341 / 9.17 / 18.1 / 0.000564 |  |
| 88564.stl | 0.778 / 13176 / 0.0498 / 0.917 / 0.00167 | TIMEOUT |  |
| 91589.stl | 0.134 / 1214 / 0.287 / 1.86 / 0.00167 | 2.97 / 2646 / 10.8 / 24.8 / 0.000474 |  |
| 94240.stl | 0.527 / 5999 / 0.0891 / 1.63 / 0.00167 | 24.8 / 29623 / 11.9 / 20.4 / 0.000599 |  |
| 97168.stl | 0.0903 / 338 / 3.13e-13 / 1.59 / 0.00167 | 1.23 / 779 / 12.1 / 14 / 0.000401 |  |
