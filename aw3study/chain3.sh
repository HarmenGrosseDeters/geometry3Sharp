#!/bin/bash
cd $(dirname $0)
S=$(pwd)
for V in master 703; do
  if [ $V = master ]; then B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark; else B=$S/../wt703/benchmarks/MeshRepairBenchmark; fi
  dll=$(ls -t $B/bin/Release/net*/MeshRepairBenchmark.dll | head -1)
  for c in 692 693:1e8 693:1e12 693:1e3 mesh:$S/c636.txt mesh:$S/c712a.txt mesh:$S/c712b.txt; do
    BENCH_REPAIR=studyrepair STUDY_REPEAT=2 timeout 900 dotnet $dll "$c" 2>/dev/null | sed "s/^{/{\"v\":\"$V\",/" >> repair_cases.jsonl
  done
done
# perf on the slow repair cases (master)
B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark
for c in 692 693:1e8 mesh:$S/c712b.txt mesh:$S/c636.txt; do
  BENCH_REPAIR=studyrepair STUDY_REPEAT=1 ./perfrun.sh "$c" perf_repair
done
# THP always vs madvise, alternating per list
echo always > /sys/kernel/mm/transparent_hugepage/enabled
for L in tail8 normal10; do ./drive.sh list_$L.txt thp_always_$L.jsonl; done
echo madvise > /sys/kernel/mm/transparent_hugepage/enabled
cat /sys/kernel/mm/transparent_hugepage/enabled
echo DONE
