# Rescue-loop stop measurement (#1154), run 44536377935

- commit 39a57004fbe3fd6f176a3ad944904ec6d4c90025; `TetMeshBenchmark refine <id>.stl 40 600` with G3_TET_FLOOR in {20x0 30x0}, G3_TET_WRAP_TIME=0, per-pass traces; 6 shards; timeout 300 s per mesh and floor


### floor 20: baseline 20x0

- meshes with a result: 500; improver time above 20 s: 3 (of them timeouts: 0); sum of the improver time 533 s
- median tets below the floor 0; median worst angle 20
- split attempts per vertex: all meshes p50 0.00 / p90 0.00 / max 0.00; meshes above 20 s (0) p10 0.00 / p50 0.00 / p90 0.00

| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |
|---|---|---|---|---|---|---|---|
| 20x0 | 0 | 3 | 0 | 533 | 0 (0.0 %) | 0 / 0 | 4912 / 0 / 0 / 0.00e+00 |

### floor 30: baseline 30x0

- meshes with a result: 492; improver time above 20 s: 111 (of them timeouts: 8); sum of the improver time 7835 s
- median tets below the floor 43; median worst angle 24.9
- split attempts per vertex: all meshes p50 0.00 / p90 0.00 / max 0.00; meshes above 20 s (0) p10 0.00 / p50 0.00 / p90 0.00

| config | cap reached | improver time > 20 s | timeouts | improver time sum s | saved s (share) | worst angle lost: max / meshes > 0.5 deg | tets below floor: sum / added sum / added max / added share max |
|---|---|---|---|---|---|---|---|
| 30x0 | 0 | 111 | 8 | 7835 | 0 (0.0 %) | 0 / 0 | 107387 / 0 / 0 / 0.00e+00 |
