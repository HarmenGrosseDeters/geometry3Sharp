#!/bin/bash
# Exact Boolean 2.0: combined A/B of the rounding and tree changes on 42 Thingi10K cohort models (the slow ones and a
# random sample), each with its #385 pairing (B = A rotated 20 degrees about (1, 1, 0) and shifted, BooleanComparison
# export), union / intersection / difference, SafeRounding on.
#
# Builds (pinned commits):
#   master      6d229316  master
#   near        db057730  PR #409 (tree of the changed triangles rebuilt after 256 changes), final review head
#   neartree    65862dec  #409 + PR #412 (box tree: median below 4096 boxes, Morton above, highest-bit splits)
#   all         53665116  #409 + #412 + PR #406 (L1) + PR #419 (Devillers-Guigue prefilter)
#
# Two rounds, the four builds alternating per round; nothing else may run on the machine meanwhile.
# usage: bash runs/boolean2-final/job.sh [work dir]   (from the repository root; about 1.5 hours on 4 cores)
# Resumable: rerun the same command after an interruption; finished (build, round, model) files are kept.
# then:  python3 runs/boolean2-final/analyze.py <work dir>/out > runs/boolean2-final/results/summary.md
#        copy <work dir>/out/*.jsonl to runs/boolean2-final/results/, commit and push to runs/boolean2-final
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/boolean2-final
W=${1:-$HOME/boolean2-final}
mkdir -p "$W/out" "$W/cases"
BUILDS="master:6d2293162ea324ed347e3bce91e452ef92ef06f8 near:db057730faf34462643637a15b2988e1d1cf08b8 neartree:65862decb67878fcb9ed88e1a1df5c757fa83e9e all:536651163b9c351103bdaa9b846d872926b2a97b"

git -C "$ROOT" fetch -q origin master claude/rounding-dirty-tree runs/b2-near-tree runs/b2-all
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
echo "done: $(cat "$W"/out/*_r?.jsonl | wc -l) lines (expected 1008)"
