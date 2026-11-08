#!/bin/bash
# Sampled stack traces of single operations (after job.sh, alone on the machine), split by trace_split.py.
# usage: bash benchmarks/LevyClassification/trace.sh <work dir> <build> <id>:<op>...
# writes <work dir>/traces/<build>_<id>_<op>.speedscope.json and prints one split line per trace
set -euo pipefail
W=$1; B=$2; shift 2
J=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$W/traces"
for x in "$@"; do
  id=${x%%:*}; op=${x#*:}
  o="$W/traces/${B}_${id}_${op}"
  if [ ! -f "$o.speedscope.json" ]; then
    ~/.dotnet/tools/dotnet-trace collect --profile dotnet-sampled-thread-time --format Speedscope -o "$o.nettrace" \
      -- dotnet "$W/tool_$B/LevyClassification.dll" --ops "$op" "$W/cases/$id" > "$o.log" 2>&1
  fi
  python3 "$J/trace_split.py" "$o.speedscope.json"
done
