# Rescue-loop stop measurement (#1154), run 44586500696

- commit dd655203182a55e5a527f152b410429d94f22738; `TetMeshBenchmark refine <id>.stl 40 600` with G3_TET_FLOOR in {20x0 20v2 20v5 20v10 30x0 30v2 30v5 30v10}, G3_TET_WRAP_TIME=0, per-pass traces; 6 shards; timeout 300 s per mesh and floor


### floor 20: baseline 20x0

- meshes with a result: 500; improver time above 20 s: 3 (of them timeouts: 0); sum of the improver time 549 s
- median tets below the floor 0; median worst angle 20
- split attempts per vertex: all meshes p50 0.19 / p90 3.95 / max 119.91; meshes above 20 s (3) p10 70.51 / p50 104.53 / p90 119.91

| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |
|---|---|---|---|---|---|---|---|
| 20x0 | 0 | 3 | 0 | 549 | 0 (0.0 %) | 0 / 0 | 4912 / 0 / 0 / 0.00e+00 |
| 20v2 | 36 | 0 | 0 | 299 | 250 (45.6 %) | 5.62 / 14 | 17184 / 12272 / 5189 / 1.73e-01 |
| 20v5 | 17 | 0 | 0 | 387 | 162 (29.5 %) | 1.94 / 3 | 8822 / 3910 / 1956 / 6.52e-02 |
| 20v10 | 7 | 1 | 0 | 458 | 91 (16.6 %) | 1.94 / 1 | 6456 / 1544 / 993 / 3.31e-02 |

### floor 30: baseline 30x0

- meshes with a result: 493; improver time above 20 s: 118 (of them timeouts: 7); sum of the improver time 8516 s
- median tets below the floor 43; median worst angle 24.9
- split attempts per vertex: all meshes p50 14.70 / p90 89.00 / max 772.79; meshes above 20 s (111) p10 27.85 / p50 80.99 / p90 244.61

| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |
|---|---|---|---|---|---|---|---|
| 30x0 | 0 | 118 | 7 | 8516 | 0 (0.0 %) | 0 / 0 | 113131 / 0 / 0 / 0.00e+00 |
| 30v2 | 427 | 0 | 0 | 1655 | 6918 (81.2 %) | 10.4 / 293 | 527823 / 321830 / 6452 / 2.08e-01 |
| 30v5 | 238 | 12 | 0 | 3686 | 5000 (58.7 %) | 8.83 / 82 | 231911 / 42546 / 2278 / 1.12e-01 |
| 30v10 | 138 | 59 | 0 | 5331 | 3545 (41.6 %) | 7.04 / 36 | 202356 / 18790 / 997 / 8.50e-02 |
