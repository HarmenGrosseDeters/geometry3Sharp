#!/bin/bash
# A/B of two BooleanComparison builds, alternating per case (A then B, then the next case with B then A), each
# `profile --repeat 5 --median --safe-rounding --ops union` in its own process with DOTNET_TieredPGO=0 (#825).
# One JSON line per case and side: the profile row with "side". Resumable.
#   ab_job.sh <tool A> <tool B> <out.jsonl> <case dir>...
A=$1; B=$2; O=$3; shift 3; touch "$O"; i=0
run() { grep -q "\"side\": \"$1\", \"case\": \"$3\"" "$O" && return
  DOTNET_TieredPGO=0 dotnet "$2/BooleanComparison.dll" profile --repeat 5 --median --safe-rounding --ops union "$3" 2>/dev/null \
    | python3 -c "import json,sys; d=json.loads(sys.stdin.read()); print(json.dumps({'side': '$1', 'case': '$3', **{k: v for k, v in d.items() if k != 'case'}}))" >> "$O"; }
for c in "$@"; do i=$((i+1))
  if [ $((i % 2)) = 1 ]; then run A "$A" "$c"; run B "$B" "$c"; else run B "$B" "$c"; run A "$A" "$c"; fi
done
