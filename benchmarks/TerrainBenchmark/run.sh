#!/bin/bash
# Runs one benchmark case in its own process with a timeout and prints one JSON line.
# Usage: run.sh <case> key=value ...   Env: TIMEOUT (seconds, default 1800)
dir=$(cd "$(dirname "$0")" && pwd)
dll=$(ls -t "$dir"/bin/Release/net*/TerrainBenchmark.dll | head -1)
out=$(TERRAIN_DATA="$dir/data" timeout ${TIMEOUT:-1800} dotnet "$dll" "$@" 2>/dev/null | tail -1)
code=$?
if [ -z "$out" ]; then
  args=$(printf '"%s",' "$@")
  echo "{\"case\":\"$1\",\"args\":[${args%,}],\"status\":\"timeout_or_crash\"}"
else
  echo "$out"
fi
