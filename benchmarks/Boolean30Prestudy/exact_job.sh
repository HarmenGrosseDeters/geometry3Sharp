#!/bin/bash
# Exact Boolean 3.0 pre-study, topic 4: exact output vs rounded output (prestudy-exact), one process per op.
# usage: bash exact_job.sh <work dir> <tool dir> "<ids>"; rows to <work dir>/exact.jsonl, resumable
set -uo pipefail
W=$(cd "$1" && pwd); T=$(cd "$2" && pwd); O=$W/exact.jsonl; touch "$O"
for id in $3; do for op in union intersection difference; do
  grep -q "\"case\":\"$id\",\"op\":\"$op\"" "$O" && continue
  timeout 1800 dotnet "$T/BooleanComparison.dll" prestudy-exact "$W/cases/$id" $op >> "$O" 2> /dev/null || echo "{\"case\":\"$id\",\"op\":\"$op\",\"error\":\"exit\"}" >> "$O"
done; done
echo done
