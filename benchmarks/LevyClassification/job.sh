#!/bin/bash
# Phase-1 profile of the classification on the self-intersecting, unknown and open cohorts (sample.txt: Thingi10K id
# and cohort; the slowest models by classification time in the #385 interim rows, plus a seeded random sample).
# Each model with its #385 pairing (B = A rotated and shifted, `BooleanComparison export`), union / intersection /
# difference, SafeRounding on, one run per operation.
#
# Builds (pinned):  master = the commit this script is run from (HEAD's library);
#                   levers = 53665116 (#409 + #412 + #406 + #419, the queued speed levers, runs/b2-all)
# usage: bash benchmarks/LevyClassification/job.sh [work dir]   (from the repository root)
# Resumable: rerun after an interruption; finished (build, model) files with three result lines are kept.
# then:  python3 benchmarks/LevyClassification/analyze.py <work dir>/out > benchmarks/LevyClassification/results/summary.md
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/benchmarks/LevyClassification
W=${1:-$HOME/levy-phase1}
mkdir -p "$W/out" "$W/cases"
BUILDS="master:$(git -C "$ROOT" rev-parse HEAD) levers:536651163b9c351103bdaa9b846d872926b2a97b"
for b in $BUILDS; do
  name=${b%%:*}; sha=${b#*:}
  if [ ! -f "$W/tool_$name/LevyClassification.dll" ]; then
    rm -rf "${W:?}/src_$name"
    git -C "$ROOT" worktree add -f --detach "$W/src_$name" "$sha"
    mkdir -p "$W/src_$name/benchmarks/LevyClassification"
    cp "$J/Program.cs" "$J/LevyClassification.csproj" "$W/src_$name/benchmarks/LevyClassification/"
    dotnet build "$W/src_$name/benchmarks/LevyClassification" -c Release -o "$W/tool_$name"
  fi
done
if [ ! -f "$W/tool_export/BooleanComparison.dll" ]; then
  dotnet build "$W/src_master/benchmarks/BooleanComparison" -c Release -f net10.0 -o "$W/tool_export"
fi
for b in $BUILDS; do echo "${b%%:*} ${b#*:}"; done > "$W/out/builds.txt"

IDS=$(cut -d' ' -f1 "$J/sample.txt")
python3 "$J/fetch_thingi.py" "$W/raw" $IDS
for id in $IDS; do
  [ -f "$W/cases/$id/B.off" ] && continue
  f=$(ls "$W"/raw/"$id".* | head -1)
  dotnet "$W/tool_export/BooleanComparison.dll" export "$f" "$W/cases/$id" > "$W/cases/export_$id.log" 2>&1
done

for b in $BUILDS; do
  name=${b%%:*}
  mkdir -p "$W/out/$name"
  for id in $IDS; do
    f="$W/out/$name/$id.jsonl"
    if [ -f "$f" ] && [ "$(grep -c '"ms"' "$f")" -ge 3 ]; then continue; fi
    timeout 1200 dotnet "$W/tool_$name/LevyClassification.dll" "$W/cases/$id" > "$f.tmp" 2> "$f.err" \
      || echo "{\"case\":\"$id\",\"error\":\"exit $?\"}" >> "$f.tmp"
    mv "$f.tmp" "$f"
  done
  cat "$W/out/$name"/*.jsonl > "$W/out/$name.jsonl"
done
echo "done: $(cat "$W"/out/*.jsonl | wc -l) lines"
