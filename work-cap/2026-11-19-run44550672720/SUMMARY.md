# Rescue-loop stop measurement (#1154), run 44550672720

- commit a7687c2a5ca751609b38d065d616100146a25130; `TetMeshBenchmark refine <id>.stl 40 600` with G3_TET_FLOOR in {20x0 20x8 20x24 20x64 30x0 30x8 30x24 30x64}, G3_TET_WRAP_TIME=0, per-pass traces; 6 shards; timeout 300 s per mesh and floor


### floor 20: baseline 20x0

- meshes with a result: 500; improver time above 20 s: 4 (of them timeouts: 0); sum of the improver time 533 s
- median tets below the floor 0; median worst angle 20
- split attempts per vertex: all meshes p50 0.19 / p90 3.95 / max 119.91; meshes above 20 s (4) p10 34.27 / p50 104.53 / p90 119.91

| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |
|---|---|---|---|---|---|---|---|
| 20x0 | 0 | 4 | 0 | 533 | 0 (0.0 %) | 0 / 0 | 4912 / 0 / 0 / 0.00e+00 |
| 20x8 | 36 | 2 | 0 | 464 | 69 (13.0 %) | 4.2 / 7 | 9840 / 4928 / 2186 / 7.28e-02 |
| 20x24 | 13 | 3 | 0 | 473 | 60 (11.3 %) | 1.94 / 2 | 6482 / 1570 / 849 / 2.83e-02 |
| 20x64 | 3 | 4 | 0 | 511 | 22 (4.1 %) | 1.94 / 1 | 5768 / 856 / 589 / 1.96e-02 |

### floor 30: baseline 30x0

- meshes with a result: 493; improver time above 20 s: 107 (of them timeouts: 7); sum of the improver time 8013 s
- median tets below the floor 43; median worst angle 24.9
- split attempts per vertex: all meshes p50 14.70 / p90 89.00 / max 772.79; meshes above 20 s (100) p10 29.59 / p50 84.74 / p90 263.09

| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |
|---|---|---|---|---|---|---|---|
| 30x0 | 0 | 107 | 7 | 8013 | 0 (0.0 %) | 0 / 0 | 113131 / 0 / 0 / 0.00e+00 |
| 30x8 | 388 | 73 | 1 | 6665 | 2519 (31.4 %) | 7.17 / 132 | 172547 / 16812 / 1008 / 6.88e-02 |
| 30x24 | 177 | 84 | 1 | 7484 | 1873 (23.4 %) | 5.69 / 42 | 165002 / 5271 / 483 / 4.44e-02 |
| 30x64 | 79 | 106 | 3 | 7818 | 1081 (13.5 %) | 5.45 / 10 | 146706 / 2359 / 443 / 4.21e-02 |
