#!/bin/bash
# Runs the benchmark on one mesh in its own process with a timeout and prints one JSON line.
# Usage: run.sh <meshfile> [std|soup|manual] [none|meters|geo] [rel|tolerance factor]
# Env: TIMEOUT (seconds, default 120), BENCH_REPAIR (default|cleanup|instr)
dir=$(dirname "$0")
dll=$(ls -t "$dir"/bin/Release/net*/MeshRepairBenchmark.dll | head -1)
out=$(timeout ${TIMEOUT:-120} dotnet "$dll" "$1" "${2:-std}" "${3:-none}" "${4:-rel}" 2>/dev/null | tail -1)
if [ -z "$out" ]; then
  echo "{\"file\":\"$(basename "$1")\",\"status\":\"timeout_or_crash\"}"
else
  echo "$out"
fi
