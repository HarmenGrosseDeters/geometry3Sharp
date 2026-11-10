#!/bin/bash
# Exact Boolean 3.0 pre-study, T5 counting step: prestudy-antipodal on the open cohort (the 21 open models of the Lévy
# samples, cases from `BooleanComparison export`). usage: bash antipodal_job.sh <work dir> <tool dir>
set -uo pipefail
W=$(cd "$1" && pwd); T=$(cd "$2" && pwd); O=$W/antipodal.jsonl; touch "$O"
for id in 100030 102815 1005586 1017015 107554 1087141 1087144 1088051 1088054 1088138 109088 112535 112536 112798 113906 116062 117959 118289 118923 122796 124526; do
  grep -q "\"case\":\"$id\"" "$O" && continue
  timeout 1800 dotnet "$T/BooleanComparison.dll" prestudy-antipodal "$W/cases/$id" >> "$O" 2> /dev/null || echo "{\"case\":\"$id\",\"error\":\"exit\"}" >> "$O"
done
echo done
