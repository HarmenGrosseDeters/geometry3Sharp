#!/bin/bash
# usage: sweep.sh N  (first N meshes of list100.txt; resumable: skips meshes already present in all four files)
cd /home/user/bench/benchmarks/MeshRepairBenchmark
L=$1
cat $L | while read m; do
  b=$(basename $m)
  for cfg in off 30 45 60; do
    f=results/fa_$cfg.jsonl
    grep -qF "\"$b\"" $f 2>/dev/null && continue
    if [ $cfg = off ]; then unset G3_FEATURE_ANGLE; else export G3_FEATURE_ANGLE=$cfg; fi
    BENCH_WRAP_WARMUP=soup BENCH_WRAP_QUALITY=0 BENCH_REPAIR=wrappipe timeout 1800 dotnet /home/user/binA/MeshRepairBenchmark.dll $m std none rel 2>/dev/null | tail -1 >> $f
  done
done
