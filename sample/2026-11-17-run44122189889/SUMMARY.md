# Tet meshing runner sample, run 44122189889

- geometry3Sharp commit: 637b148d77f01a085eb8ae14311becc608e9fe88; `TetMeshBenchmark refine <id>.stl <alpha> 600` per mesh and alpha (G3_TET_WRAP_TIME=0)
- 500 ids of benchmarks/TetMeshBenchmark/compare/thingi-sample-ids.txt; 6 shards (ubuntu-latest, machines.txt); timeout 600 s per mesh and alpha
- Rule: docs/design/TET_MESHING.md section 17.5 (benchmarks/TetMeshBenchmark/compare/summarize_sample.py)

| alpha (diagonal /) | rows | Success | valid | self-intersecting | every tet >= 10 | worst min dihedral | input to boundary max, median | boundary to input max, median | tets, median | time median / max (s) | failures |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| / 20 | 500 | 500 | 500 | 0 | 461 | 3.78 | 0.0343 | 0.0252 | 4138 | 0.637 / 8.6 | - |
| / 40 | 500 | 500 | 500 | 0 | 494 | 7.12 | 0.0228 | 0.00417 | 7290 | 0.827 / 9.81 | - |
| / 80 | 500 | 500 | 500 | 0 | 499 | 9.25 | 0.0135 | 0.00167 | 19534 | 1.41 / 11.7 | - |

**alpha / 40: no go** (failed: 1)
- criterion 1: every tet >= 10 degrees on 98.80% of 500 Success meshes (need >= 99%); below: 138102.stl 7.12, 763398.stl 9.15, 38781.stl 9.43, 282598.stl 9.53, 83719.stl 9.60, 50656.stl 9.88
- criterion 2: input-to-boundary max, median of per-mesh ratios 0.772 over 500 meshes (need <= 0.85)
- criterion 3: median time 0.827 s against 0.637 s at the base, ratio 1.30 (need <= 2.0)

**alpha / 80: no go** (failed: 3)
- criterion 1: every tet >= 10 degrees on 99.80% of 500 Success meshes (need >= 99%); below: 115070.stl 9.25
- criterion 2: input-to-boundary max, median of per-mesh ratios 0.469 over 500 meshes (need <= 0.85)
- criterion 3: median time 1.41 s against 0.637 s at the base, ratio 2.22 (need <= 2.0)

**Chosen by the rule of 17.5: none (stop and report)** (the coarsest candidate that passes; criterion 4 is checked on the subset)
