#!/bin/bash
# D1 reference rule (Boolean 2.1), cohort run of the Thingi10K target class: the 5413 target models of the Boolean 2.0
# final run (runs/boolean2-final, results/class.tsv: closed, selfint 0), 16 239 operations, each with its #385 pairing
# (B = A rotated 20 degrees about (1, 1, 0) and shifted by a quarter of the box: BooleanComparison export), union /
# intersection / difference, SafeRounding on, on two builds one after the other per model, on this machine:
#   base   master with the apex rule (the PR's merge base)
#   cand   the library PR's head (the reference rule)
# The export (A.off / B.off) comes from BooleanComparison of the base build, once per model, used by both builds.
# Models run one at a time (each operation uses every core); nothing else may run on the machine.
#
# usage (from the repository root of this branch):  bash runs/d1/job.sh [work dir]
#   IDS_FILE=<file> limits the run to the ids in it (a smoke run, in a separate work dir)
# Resumable: rerun the same command after an interruption; a model whose two row files exist is skipped. Raw meshes
# and exports are deleted after each model. Then:
#   python3 runs/d1/analyze.py <work dir>/out > runs/d1/results/summary.md
#   cp <work dir>/out/base.jsonl <work dir>/out/cand.jsonl runs/d1/results/; commit; push to runs/d1-cohort
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/d1
W=${1:-$HOME/d1-cohort-run}
mkdir -p "$W/out/base" "$W/out/cand" "$W/raw" "$W/cases"
BUILDS="base:f2245b6fc632fd4997521adbef158c5607c11ce8 cand:c623df4ce76fcc51ca58736618e0adbd8ce89572"

git -C "$ROOT" fetch -q origin master
for b in $BUILDS; do
  name=${b%%:*}; sha=${b#*:}
  git -C "$ROOT" cat-file -e "$sha^{commit}" 2>/dev/null || git -C "$ROOT" fetch -q origin "$sha"
  if [ ! -f "$W/runner_$name/D1Runner.dll" ] || [ "$(cat "$W/runner_$name/build.sha" 2>/dev/null || true)" != "$sha" ]; then
    rm -rf "${W:?}/runner_$name"
    git -C "$ROOT" worktree remove --force "$W/src_$name" 2>/dev/null || true
    rm -rf "${W:?}/src_$name" "${W:?}/rsrc_$name"
    git -C "$ROOT" worktree add -f --detach "$W/src_$name" "$sha"
    cp -r "$J/D1Runner" "$W/rsrc_$name"
    dotnet build "$W/rsrc_$name" -c Release -p:G3Src="$W/src_$name" -o "$W/runner_$name"
    echo "$sha" > "$W/runner_$name/build.sha"
  fi
done
if [ ! -f "$W/tool_export/BooleanComparison.dll" ]; then
  dotnet build "$W/src_base/benchmarks/BooleanComparison" -c Release -f net10.0 -o "$W/tool_export"
fi
for b in $BUILDS; do echo "${b%%:*} ${b#*:}"; done > "$W/out/builds.txt"

cp "${IDS_FILE:-$J/target_ids.txt}" "$W/ids.txt"
echo "$(wc -l < "$W/ids.txt") ids"

run_model() {   # build id -> out/$build/$id.jsonl
  local build=$1 id=$2 out="$W/out/$1/$2.jsonl"
  timeout 3600 dotnet "$W/runner_$build/D1Runner.dll" "$W/cases/$id" "$id" > "$out.tmp" 2> "$out.err" \
    || echo "{\"id\":\"$id\",\"error\":\"exit $?\"}" >> "$out.tmp"
  mv "$out.tmp" "$out"
}

n=0
while read -r id; do
  n=$((n + 1))
  [ -f "$W/out/base/$id.jsonl" ] && [ -f "$W/out/cand/$id.jsonl" ] && continue
  rm -rf "${W:?}/cases/$id"
  python3 "$J/fetch_thingi.py" "$W/raw" "$id" > /dev/null
  f=$(ls "$W"/raw/"$id".* 2> /dev/null | grep -v inventory | head -1 || true)
  if [ -n "$f" ]; then
    timeout 1800 dotnet "$W/tool_export/BooleanComparison.dll" export "$f" "$W/cases/$id" > "$W/cases/export_$id.log" 2>&1 || true
    rm -f "$f"
  fi
  if [ ! -f "$W/cases/$id/A.off" ]; then
    for b in base cand; do echo "{\"id\":\"$id\",\"error\":\"export failed\"}" > "$W/out/$b/$id.jsonl"; done
  else
    # (both builds on the same export; an interrupted pair is run again in full)
    run_model base "$id"
    run_model cand "$id"
  fi
  rm -rf "${W:?}/cases/$id" "$W/cases/export_$id.log"
  [ $((n % 100)) -eq 0 ] && echo "$(date -u +%FT%TZ) $n ids seen, $(ls "$W/out/cand" | grep -c jsonl$) done"
done < "$W/ids.txt"
for b in base cand; do cat "$W"/out/$b/*.jsonl > "$W/out/$b.jsonl"; done
echo "done: $(wc -l < "$W/out/base.jsonl") base rows, $(wc -l < "$W/out/cand.jsonl") cand rows"
