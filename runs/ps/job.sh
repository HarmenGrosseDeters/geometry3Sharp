#!/bin/bash
# Position search of SafeRounding (the library PR of branch claude/rounding-position-search), cohort run of the
# Thingi10K target class: the 5413 target models of the Boolean 2.0 final run (runs/boolean2-final, results/class.tsv:
# closed, selfint 0), 16 239 operations, each with its #385 pairing (B = A rotated 20 degrees about (1, 1, 0) and
# shifted by a quarter of the box: BooleanComparison export), union / intersection / difference, SafeRounding on, on
# two builds:
#   base   master, the PR's merge base
#   cand   the PR's head
# One pass, correctness: every model; N workers in parallel (N = min(4, cores)), each on its own core (taskset) with
# DOTNET_PROCESSOR_COUNT=1; per model base, then cand; cand takes base's NotCleanReason where its result hash equals
# base's (the same mesh, the same verdict) and evaluates it only where the result changed. Each model is downloaded
# and exported once into $W/cache/<id> and deleted after its two builds.
#
# usage (from the repository root of this branch):  bash runs/ps/job.sh correctness [work dir]
#   IDS_FILE=<file> limits the pass to the ids in it (a smoke run, in a separate work dir)
# Resumable: rerun the same command after an interruption; a model whose rows exist is skipped (an interrupted model
# is run again for both builds). Then:
#   python3 runs/ps/analyze.py <work dir>/out > runs/ps/results/summary.md
set -euo pipefail
PASS=${1:?usage: job.sh correctness [work dir]}
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/ps
W=${2:-$HOME/ps-cohort-run}
mkdir -p "$W/out/base" "$W/out/cand" "$W/raw" "$W/cache" "$W/log"
BUILDS="base:1f3b6b79ff5cbe65f823ea1f84a8815c3f856692 cand:36ff5bf909f9b84cddf7bbf7744ecc4721ebefb7"

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
# the inventory once, before the workers (they would race on it)
python3 - "$W/raw" "$J/fetch_thingi.py" <<'PY'
import sys, os
src = open(sys.argv[2]).read().split('def main')[0]
exec(src)
inventory(os.path.join(sys.argv[1], 'inventory.json'))
PY

# the export of a model in $W/cache/$id (once); false if it fails
export_model() {
  local id=$1 f raw="$W/raw/$1"
  [ -f "$W/cache/$id/A.off" ] && [ -f "$W/cache/$id/B.off" ] && return 0
  rm -rf "${W:?}/cache/$id" "$raw"; mkdir -p "$raw"
  cp "$W/raw/inventory.json" "$raw/"
  python3 "$J/fetch_thingi.py" "$raw" "$id" > /dev/null 2>&1 || true
  f=$(ls "$raw"/"$id".* 2> /dev/null | head -1 || true)
  if [ -n "$f" ]; then
    timeout 1800 dotnet "$W/tool_export/BooleanComparison.dll" export "$f" "$W/cache/$id" > "$W/log/export_$id.log" 2>&1 || true
  fi
  rm -rf "$raw"
  [ -f "$W/cache/$id/A.off" ] && [ -f "$W/cache/$id/B.off" ]
}

# run build $1 on model $2 into $3 (a row file), extra runner arguments after; PIN: a command prefix (taskset)
run_build() {
  local build=$1 id=$2 out=$3; shift 3
  timeout 3600 ${PIN:-} dotnet "$W/runner_$build/D1Runner.dll" "$W/cache/$id" "$id" "$@" > "$out.tmp" 2> "$out.err" \
    || echo "{\"id\":\"$id\",\"error\":\"exit $?\"}" >> "$out.tmp"
  mv "$out.tmp" "$out"
}

cp "${IDS_FILE:-$J/target_ids.txt}" "$W/ids.txt"

correctness_worker() {   # worker $1 of $2: the ids at positions = $1 mod $2, on core $1
  local k=$1 n=$2 i=0 id
  export DOTNET_PROCESSOR_COUNT=1
  PIN="taskset -c $k"
  while read -r id; do
    i=$((i + 1))
    [ $(((i - 1) % n)) -eq "$k" ] || continue
    [ -f "$W/out/base/$id.jsonl" ] && [ -f "$W/out/cand/$id.jsonl" ] && continue
    if export_model "$id"; then
      run_build base "$id" "$W/out/base/$id.jsonl.part"
      run_build cand "$id" "$W/out/cand/$id.jsonl.part" --base-rows "$W/out/base/$id.jsonl.part"
      mv "$W/out/base/$id.jsonl.part" "$W/out/base/$id.jsonl"
      mv "$W/out/cand/$id.jsonl.part" "$W/out/cand/$id.jsonl"
    else
      for b in base cand; do echo "{\"id\":\"$id\",\"error\":\"export failed\"}" > "$W/out/$b/$id.jsonl"; done
    fi
    rm -rf "${W:?}/cache/$id"
    rm -f "$W/out/base/$id.jsonl.part.err" "$W/out/cand/$id.jsonl.part.err"
  done < "$W/ids.txt"
}

case $PASS in
  correctness)
    N=$(nproc); [ "$N" -gt 4 ] && N=4
    echo "correctness: $(wc -l < "$W/ids.txt") ids, $N workers, start $(date -u +%FT%TZ)"
    for k in $(seq 0 $((N - 1))); do correctness_worker "$k" "$N" > "$W/log/worker_$k.log" 2>&1 & done
    wait
    for b in base cand; do cat "$W"/out/$b/*.jsonl > "$W/out/$b.jsonl"; done
    echo "correctness done $(date -u +%FT%TZ): $(wc -l < "$W/out/base.jsonl") base rows, $(wc -l < "$W/out/cand.jsonl") cand rows"
    ;;
  *) echo "unknown pass $PASS"; exit 2 ;;
esac
