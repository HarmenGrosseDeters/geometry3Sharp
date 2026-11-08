#!/bin/bash
# Exact Boolean 2.0, D1: the apex drift rule (PR #424) against master on the 42 cohort models of B2-FINAL-1 and the 14
# Thingi10K models with D1 operations, each with its #385 pairing (B = A rotated 20 degrees about (1, 1, 0) and shifted,
# BooleanComparison export), union / intersection / difference, SafeRounding on: the clean status per operation, the
# results changed, the times.
#
# Builds (pinned commits):
#   master  88685b0b  master
#   d1      eb8d852e  PR #424 (the drift of an edit charged to its apex)
#
# Two rounds, the builds alternating per round; nothing else may run on the machine meanwhile.
# usage: bash runs/boolean2-d1/job.sh [work dir]   (from the repository root; about 1 hour on 4 cores)
# Resumable: rerun the same command after an interruption; finished (build, round, model) files are kept.
# then:  python3 runs/boolean2-d1/analyze.py <work dir>/out > runs/boolean2-d1/results/summary.md
#        copy <work dir>/out/*.jsonl and builds.txt to runs/boolean2-d1/results/, commit and push to runs/boolean2-combined
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/boolean2-d1
W=${1:-$HOME/boolean2-d1}
mkdir -p "$W/out" "$W/cases"
BUILDS="master:88685b0b6f0afbd6006649a3123870fd116f8a25 d1:eb8d852e455f6ba9ddaeecf4f059ba8d17abbcaa"

git -C "$ROOT" fetch -q origin master claude/d1-apex-drift
for b in $BUILDS; do
  name=${b%%:*}; sha=${b#*:}
  if [ ! -f "$W/tool_$name/BooleanComparison.dll" ]; then
    rm -rf "${W:?}/src_$name"
    git -C "$ROOT" worktree add -f --detach "$W/src_$name" "$sha"
    dotnet build "$W/src_$name/benchmarks/BooleanComparison" -c Release -f net10.0 -o "$W/tool_$name"
  fi
done
for b in $BUILDS; do echo "${b%%:*} ${b#*:}"; done > "$W/out/builds.txt"

IDS=$(cat "$J/ids.txt")
python3 "$J/fetch_thingi.py" "$W/raw" $IDS
for id in $IDS; do
  [ -f "$W/cases/$id/B.off" ] && continue
  f=$(ls "$W"/raw/"$id".* | head -1)
  dotnet "$W/tool_master/BooleanComparison.dll" export "$f" "$W/cases/$id" > "$W/cases/export_$id.log" 2>&1
done

# resumable: one file per (build, round, model) with the model's three operations; a file with three result lines is
# done and skipped, an incomplete one is run again (rerun the script after an interruption)
for r in 1 2; do
  for b in $BUILDS; do
    name=${b%%:*}
    mkdir -p "$W/out/${name}_r$r"
    for id in $IDS; do
      f="$W/out/${name}_r$r/$id.jsonl"
      if [ -f "$f" ] && [ "$(grep -c '"ms"' "$f")" -ge 3 ]; then continue; fi
      timeout 900 dotnet "$W/tool_$name/BooleanComparison.dll" profile --safe-rounding --repeat 1 "$W/cases/$id" > "$f.tmp" 2> "$f.err" \
        || echo "{\"case\":\"$id\",\"error\":\"exit $?\"}" >> "$f.tmp"
      mv "$f.tmp" "$f"
    done
    cat "$W/out/${name}_r$r"/*.jsonl > "$W/out/${name}_r$r.jsonl"
  done
done
echo "done: $(cat "$W"/out/*_r?.jsonl | wc -l) lines (expected 672)"
