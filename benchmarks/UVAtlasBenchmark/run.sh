#!/bin/bash
# Runs the benchmark on one mesh in its own process with a timeout and prints one JSON line.
# Usage: run.sh <meshfile> [auto|charts|cones|single]
# Env: TIMEOUT (seconds, default 300)
dir=$(dirname "$0")
dll=$(ls -t "$dir"/bin/Release/net*/UVAtlasBenchmark.dll | head -1)
out=$(timeout ${TIMEOUT:-300} dotnet "$dll" "$1" "${2:-auto}" 2>/dev/null | tail -1)
if [ -z "$out" ]; then
  echo "{\"file\":\"$(basename "$1")\",\"method\":\"${2:-auto}\",\"status\":\"timeout_or_crash\"}"
else
  echo "$out"
fi
