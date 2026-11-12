#!/bin/bash
# alternating: instrumented (I) and clean master (M) on each mesh of the list
S=/tmp/claude-0/-home-user-geometry3Sharp/efd1d2d6-01ab-5d80-b9ac-064b763f7799/scratchpad
D=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark/data/sample800_2611/meshes
while read f; do
  for v in I M; do
    if [ $v = I ]; then B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark; else B=$S/master/benchmarks/MeshRepairBenchmark; fi
    out=$(BENCH_REPAIR=wrappipe BENCH_FIDELITY=hd TIMEOUT=400 $B/run.sh $D/$f)
    echo "{\"v\":\"$v\",\"row\":$out}" >> $S/cal.jsonl
  done
done < $S/list_cal.txt
echo DONE
