#!/bin/bash
# Exact Boolean 3.0 pre-study, topic 5: prestudy-fwn on the open cohort, tool built from bfaa0158 (per-piece branch)
# with this branch's BooleanComparison sources. usage: bash fwn_job.sh <work dir> <tool dir> "<ids>"
set -uo pipefail
W=$(cd "$1" && pwd); T=$(cd "$2" && pwd); O=$W/fwn.jsonl; touch "$O"
for id in $3; do
  grep -q "\"case\":\"$id\"" "$O" && continue
  timeout 2400 dotnet "$T/BooleanComparison.dll" prestudy-fwn "$W/cases/$id" >> "$O" 2> /dev/null || echo "{\"case\":\"$id\",\"error\":\"exit\"}" >> "$O"
done
echo done
