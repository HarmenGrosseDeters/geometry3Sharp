#!/bin/bash
# Exact Boolean 2.0: combined A/B of the rounding and tree changes on 42 Thingi10K cohort models (the slow ones and a
# random sample), each with its #385 pairing (B = A rotated 20 degrees about (1, 1, 0) and shifted, BooleanComparison
# export), union / intersection / difference, SafeRounding on.
#
# Builds (pinned commits):
#   master      eb938259  master (no change in the library since the branches' bases)
#   l1          11418afe  PR #406 (SafeRounding skips the pairs the exact arrangement decided)
#   l1near      865163cb  #406 + PR #409 (tree of the changed triangles rebuilt after 256 changes)
#   l1neartree  a7c26486  #406 + #409 + the Morton-ordered box tree (branch claude/box-tree-morton)
#
# Two rounds, the four builds alternating per round; nothing else may run on the machine meanwhile.
# usage: bash runs/boolean2-combined/job.sh [work dir]   (from the repository root; about 1.5 hours on 4 cores)
# then:  python3 runs/boolean2-combined/analyze.py <work dir>/out > runs/boolean2-combined/results/summary.md
#        copy <work dir>/out/*.jsonl to runs/boolean2-combined/results/, commit and push to runs/boolean2-combined
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/boolean2-combined
W=${1:-$HOME/boolean2-combined}
mkdir -p "$W/out" "$W/cases"
BUILDS="master:eb938259dc4db731a106a08e8584ae17a73bb906 l1:11418afe70f91ef9c0ef0d0fe785972d2fce7c7d l1near:865163cba559902d5aa943cc21e50c1e0778e874 l1neartree:a7c26486ff8889ba557ba10cf0f6ee3b25b0ddfe"

git -C "$ROOT" fetch -q origin master claude/rounding-skip-exact-pairs runs/boolean2-l1-near runs/boolean2-l1-near-tree
for b in $BUILDS; do
  name=${b%%:*}; sha=${b#*:}
  if [ ! -f "$W/tool_$name/BooleanComparison.dll" ]; then
    rm -rf "${W:?}/src_$name"
    git -C "$ROOT" worktree add -f --detach "$W/src_$name" "$sha"
    dotnet build "$W/src_$name/benchmarks/BooleanComparison" -c Release -f net10.0 -o "$W/tool_$name"
  fi
  echo "$name $sha" >> "$W/out/builds.txt"
done

IDS=$(cat "$J/ids.txt")
python3 "$J/fetch_thingi.py" "$W/raw" $IDS
for id in $IDS; do
  [ -f "$W/cases/$id/B.off" ] && continue
  f=$(ls "$W"/raw/"$id".* | head -1)
  dotnet "$W/tool_master/BooleanComparison.dll" export "$f" "$W/cases/$id" > "$W/cases/export_$id.log" 2>&1
done

for r in 1 2; do
  for b in $BUILDS; do
    name=${b%%:*}
    out="$W/out/${name}_r$r.jsonl"
    : > "$out"
    for id in $IDS; do
      timeout 900 dotnet "$W/tool_$name/BooleanComparison.dll" profile --safe-rounding --repeat 1 "$W/cases/$id" >> "$out" 2>> "$out.err" \
        || echo "{\"case\":\"$id\",\"error\":\"exit $?\"}" >> "$out"
    done
  done
done
echo "done: $(cat "$W"/out/*_r?.jsonl | wc -l) lines (expected 1008)"
