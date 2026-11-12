#!/bin/bash
# perfrun.sh <mesh.stl> <outdir> : wrappipe under perf (cpu-clock, 499 Hz, frame-pointer stacks), then folded stacks
# inside the measured pipeline window (study_t0_ns..study_t1_ns, CLOCK_MONOTONIC). Extra env is inherited.
B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark
PERF=/usr/lib/linux-tools/6.8.0-31-generic/perf
dll=$(ls -t $B/bin/Release/net*/MeshRepairBenchmark.dll | head -1)
f=$(basename $1 .stl); mkdir -p $2
export DOTNET_PerfMapEnabled=1 DOTNET_EnableWriteXorExecute=0 DOTNET_GCName=libclrgc.so
BENCH_REPAIR=${BENCH_REPAIR:-wrappipe} BENCH_FIDELITY=0 $PERF record -q -e cpu-clock -F 499 -g -k CLOCK_MONOTONIC -o $2/$f.data -- dotnet $dll "$@" 2>/dev/null | tail -1 > $2/$f.row.json
$PERF script -i $2/$f.data -F tid,time,ip,sym,dso 2>/dev/null | python3 $(dirname $0)/fold.py $2/$f.row.json > $2/$f.folded
rm -f $2/$f.data /tmp/perf-*.map
