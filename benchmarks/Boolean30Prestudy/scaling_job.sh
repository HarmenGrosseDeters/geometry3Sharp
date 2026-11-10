#!/bin/bash
# Exact Boolean 3.0 pre-study, topic 1: per-phase profile at 1, 2 and 4 cores (docs/design/EXACT_BOOLEAN_30_PRESTUDY.md).
# usage: bash scaling_job.sh <work dir> <ids file> [cores list, default "4 2 1"]
#   <work dir> holds tool_master/ (BooleanComparison built from the measured commit, net10.0) and cases/<id>/{A,B}.off
#   (`BooleanComparison export`, the #385 pairing); <ids file>: "<thingi id> <cohort>" per line.
# One process per (cores, model), all three operations, SafeRounding on, one timed run each; resumable (a finished
# file with three result lines is kept). Nothing else may run on the machine while this times.
set -uo pipefail
W=$(cd "$1" && pwd); IDS=$2; CORES=${3:-"4 2 1"}
for n in $CORES; do
  mkdir -p "$W/scaling/c$n"
  for id in $(cut -d' ' -f1 "$IDS"); do
    f="$W/scaling/c$n/$id.jsonl"
    if [ -f "$f" ] && [ "$(grep -c '"phases_ms"' "$f")" -ge 3 ]; then continue; fi
    DOTNET_PROCESSOR_COUNT=$n timeout 3600 taskset -c 0-$((n - 1)) dotnet "$W/tool_master/BooleanComparison.dll" profile \
      --safe-rounding --repeat 1 "$W/cases/$id" > "$f.tmp" 2> "$f.err" || echo "{\"case\":\"$id\",\"error\":\"exit $?\"}" >> "$f.tmp"
    mv "$f.tmp" "$f"
  done
done
echo done
