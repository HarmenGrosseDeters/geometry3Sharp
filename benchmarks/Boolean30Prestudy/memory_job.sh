#!/bin/bash
# Exact Boolean 3.0 pre-study, topic 2: memory per operation (one process each) and the GC upper bound.
# Rows carry "gc_variant" (analyze_memory.py: cat <work dir>/memory/*/*.json > results/memory.jsonl).
# usage: bash memory_job.sh <work dir> <tool dir> "<ids>" [variants, default "default gen0 server"]
#   default: workstation concurrent GC (as shipped); gen0: DOTNET_GCgen0size=0x20000000 (512 MB gen0 budget);
#   server: DOTNET_gcServer=1. Rows go to <work dir>/memory/<variant>/<id>_<op>.json; resumable.
set -uo pipefail
W=$(cd "$1" && pwd); T=$(cd "$2" && pwd); IDS=$3; VARIANTS=${4:-"default gen0 server"}
for v in $VARIANTS; do
  mkdir -p "$W/memory/$v"
  for id in $IDS; do for op in union intersection difference; do
    f="$W/memory/$v/${id}_$op.json"; [ -s "$f" ] && continue
    case $v in
      default) env=();;
      gen0) env=(DOTNET_GCgen0size=0x20000000);;
      server) env=(DOTNET_gcServer=1);;
    esac
    extra=(); [ "$v" = default ] || extra=(--no-arrangement-probe)   # (the committed gen0 / server rows predate this flag: they also ran the probe)
    env "${env[@]}" timeout 1800 dotnet "$T/BooleanComparison.dll" prestudy-memory "$W/cases/$id" $op "${extra[@]}" 2> "$f.err" | sed "s/^{/{\"gc_variant\":\"$v\",/" > "$f.tmp" \
      || echo "{\"case\":\"$id\",\"op\":\"$op\",\"error\":\"exit $?\"}" > "$f.tmp"
    mv "$f.tmp" "$f"
  done; done
done
echo done
