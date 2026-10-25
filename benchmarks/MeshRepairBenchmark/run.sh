#!/bin/bash
# Runs the benchmark on one mesh in its own process with a timeout and prints one JSON line.
# Usage: run.sh <meshfile> [std|soup|manual] [none|meters|geo] [tolerance factor]
# Env: TIMEOUT (seconds, default 120), BENCH_REPAIR (default|cleanup|instr)
dir=$(dirname "$0")
out=$(timeout ${TIMEOUT:-120} dotnet "$dir/bin/Release/net10.0/MeshRepairBenchmark.dll" "$1" $2 $3 $4 2>/dev/null | tail -1)
if [ -z "$out" ]; then
  echo "{\"file\":\"$(basename "$1")\",\"status\":\"timeout_or_crash\"}"
else
  echo "$out"
fi
