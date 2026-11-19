# Rescue-loop stop measurement (#1154), run 44608723935

- commit 03ecd77cf2a7bac46700753039aacb3418bc7528; `TetMeshBenchmark refine <id>.stl 40 600` with G3_TET_FLOOR in {20x0 20v6 20v7 20v8}, G3_TET_WRAP_TIME=0, per-pass traces; 6 shards; timeout 300 s per mesh and floor


### floor 20: baseline 20x0

- meshes with a result: 500; improver time above 20 s: 3 (of them timeouts: 0); sum of the improver time 447 s
- median tets below the floor 0; median worst angle 20
- split attempts per vertex: all meshes p50 0.19 / p90 3.95 / max 119.91; meshes above 20 s (3) p10 70.51 / p50 104.53 / p90 119.91

| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |
|---|---|---|---|---|---|---|---|
| 20x0 | 0 | 3 | 0 | 447 | 0 (0.0 %) | 0 / 0 | 4912 / 0 / 0 / 0.00e+00 |
| 20v6 | 14 | 0 | 0 | 342 | 105 (23.4 %) | 1.94 / 3 | 7821 / 2909 / 1571 / 5.23e-02 |
| 20v7 | 13 | 0 | 0 | 354 | 93 (20.8 %) | 1.94 / 2 | 7310 / 2398 / 1338 / 4.46e-02 |
| 20v8 | 8 | 0 | 0 | 364 | 83 (18.5 %) | 1.94 / 1 | 6847 / 1935 / 1148 / 3.82e-02 |
