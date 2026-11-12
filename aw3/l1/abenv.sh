#!/bin/bash
# abenv.sh <list> <out> : same build, FACEMODE=0 (v=B) and 3 (v=C) alternating per mesh
D=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark/data/sample800_2611/meshes
B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark
while read f; do
  [ -z "$f" ] && continue
  grep -q "\"file\":\"$f\"" "$2" 2>/dev/null && continue
  for v in B C; do
    m=0; [ $v = C ] && m=3
    o=$(FACEMODE=$m BENCH_REPAIR=wrappipe BENCH_FIDELITY=hd TIMEOUT=400 $B/run.sh $D/$f)
    echo "{\"v\":\"$v\",\"row\":$o}" >> "$2"
  done
done < "$1"
echo DONE
