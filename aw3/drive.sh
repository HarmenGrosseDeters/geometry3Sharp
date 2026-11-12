#!/bin/bash
# drive.sh <list> <out.jsonl>: wrappipe on each mesh, one process at a time, resumable
B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark
while read f; do
  [ -z "$f" ] && continue
  grep -q "\"file\":\"$f\"" "$2" 2>/dev/null && continue
  BENCH_REPAIR=wrappipe BENCH_FIDELITY=hd TIMEOUT=400 $B/run.sh $B/data/sample800_2611/meshes/$f >> "$2"
done < "$1"
echo DONE
