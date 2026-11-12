#!/bin/bash
# ab.sh <list> <out.jsonl> [env...]: baseline (B, scratchpad/base) and candidate (C, the repo) alternating per mesh, resumable
S=/tmp/claude-0/-home-user-geometry3Sharp/efd1d2d6-01ab-5d80-b9ac-064b763f7799/scratchpad
D=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark/data/sample800_2611/meshes
list=$1; out=$2; shift 2
while read f; do
  [ -z "$f" ] && continue
  grep -q "\"file\":\"$f\"" "$out" 2>/dev/null && continue
  for v in B C; do
    if [ $v = B ]; then B=$S/base/benchmarks/MeshRepairBenchmark; else B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark; fi
    o=$(env "$@" BENCH_REPAIR=wrappipe BENCH_FIDELITY=hd TIMEOUT=400 $B/run.sh $D/$f)
    echo "{\"v\":\"$v\",\"row\":$o}" >> "$out"
  done
done < "$list"
echo DONE
