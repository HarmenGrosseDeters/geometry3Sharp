# FeatureAngle sweep on the alpha-wrap pipeline (option 1): no gain

- Base: bench/alpha-wrap-2-ABC @ 7aa740e + env_patch.diff (G3_FEATURE_ANGLE / G3_FEATURE_WEIGHT in SimplifyWithin; Reducer default weight 100).
- Run: sweep.sh <list> (BENCH_REPAIR=wrappipe, BENCH_WRAP_QUALITY=0, BENCH_WRAP_WARMUP=soup, full fidelity), configs off/30/45/60 alternating per mesh.
- Data: Thingi10K sample800 seed 2611 (not included); 20 meshes of T1 (every 5th by raw triangles). Analysis: `python3 cmp.py off 30 45 60` in benchmarks/MeshRepairBenchmark (results/fa_*.jsonl).
- Medians over 20 (off/30/45/60): fm_ecd 0.00148/0.00149/0.00148/0.00150; edge F@0.005 0.940/0.939/0.938/0.940;
  triangles 33048/34439/33805/33603; p01 11.59/11.60/11.59/11.59; time 7.77/7.56/7.72/7.68 s.
- 0/0 floors held and guarantees 20/20 in all configs; band max 1.091/1.133/1.177/1.177; meshes above the contract 4/5/5/8.
- Result: no fidelity gain (paired fm_ecd ratio ~1.002), more triangles, wider band. Stopped at 20 meshes. No meshes in this branch.
