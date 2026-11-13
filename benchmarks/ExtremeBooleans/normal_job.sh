#!/bin/bash
# The normal-stratum phase breakdown of docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 8: per case one process with
# `profile --repeat 5 --median --safe-rounding --ops union` (the phases, counts and GC of the median run of five) into
# <work dir>/normal5.jsonl, then one dotnet-trace sampling profile per case into <work dir>/traces_n. Resumable.
#   normal_job.sh <BooleanComparison tool dir> <work dir> <case dir>...
T=$1; W=$2; shift 2; DT=${DOTNET_TRACE:-$HOME/.dotnet/tools/dotnet-trace}
cd "$W"; mkdir -p traces_n; touch normal5.jsonl
for c in "$@"; do grep -q "\"case\": \"$c\"" normal5.jsonl && continue
  dotnet "$T/BooleanComparison.dll" profile --repeat 5 --median --safe-rounding --ops union "$c" 2>/dev/null \
    | python3 -c "import json,sys; d=json.loads(sys.stdin.read()); d['case']='$c'; print(json.dumps(d))" >> normal5.jsonl; done
for c in "$@"; do n=$(echo "$c" | tr '/' '_'); [ -f traces_n/$n.speedscope.json ] && continue
  "$DT" collect --profile dotnet-sampled-thread-time --format Speedscope -o traces_n/$n.nettrace -- \
    dotnet "$T/BooleanComparison.dll" profile --repeat 1 --safe-rounding --ops union "$c" > /dev/null 2>&1; rm -f traces_n/$n.nettrace; done
echo done
