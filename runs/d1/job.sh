#!/bin/bash
# D1 reference rule (Boolean 2.1), cohort run of the Thingi10K target class: the 5413 target models of the Boolean 2.0
# final run (runs/boolean2-final, results/class.tsv: closed, selfint 0), 16 239 operations, each with its #385 pairing
# (B = A rotated 20 degrees about (1, 1, 0) and shifted by a quarter of the box: BooleanComparison export), union /
# intersection / difference, SafeRounding on, on two builds:
#   base   master with the apex rule (the PR's merge base)
#   cand   the library PR's head (the reference rule)
# Two passes (owner decision 2026-11-10: correctness and timing apart):
#   correctness  every model; N workers in parallel (N = min(4, cores)), each on its own core (taskset) with
#                DOTNET_PROCESSOR_COUNT=1; per model base, then cand; cand takes base's NotCleanReason where its result
#                hash equals base's (the same mesh, the same verdict) and evaluates it only where the result changed
#   timing       the 400 models of timing_ids.txt (every model with an operation over 1 s in the Boolean 2.0 final run,
#                332, and 68 stratified by time), one at a time with every core, base and cand interleaved per model
#                (the order alternates), no NotCleanReason; nothing else may run on the machine
# Each model is downloaded and exported once into $W/cache/<id> (BooleanComparison of the base build); both builds and
# both passes read it; the correctness pass deletes the export unless the model is in the timing sample, the timing
# pass deletes it after its model.
#
# usage (from the repository root of this branch):  bash runs/d1/job.sh correctness|timing [work dir]
#   IDS_FILE=<file> limits the pass to the ids in it (a smoke run, in a separate work dir)
# Resumable: rerun the same command after an interruption; a model whose rows exist is skipped (an interrupted model
# is run again for both builds). Then:
#   python3 runs/d1/analyze.py <work dir>/out > runs/d1/results/summary.md
set -euo pipefail
PASS=${1:?usage: job.sh correctness|timing [work dir]}
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/d1
W=${2:-$HOME/d1-cohort-run}
mkdir -p "$W/out/base" "$W/out/cand" "$W/out/timing_base" "$W/out/timing_cand" "$W/raw" "$W/cache" "$W/log"
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
SAMPLE="$J/timing_ids.txt"

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
    grep -qx "$id" "$SAMPLE" || rm -rf "${W:?}/cache/$id"
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
  timing)
    [ -n "${IDS_FILE:-}" ] || cp "$SAMPLE" "$W/ids.txt"
    echo "timing: $(wc -l < "$W/ids.txt") ids, every core, start $(date -u +%FT%TZ)"
    i=0
    while read -r id; do
      i=$((i + 1))
      [ -f "$W/out/timing_base/$id.jsonl" ] && [ -f "$W/out/timing_cand/$id.jsonl" ] && continue
      if export_model "$id"; then
        # (the order alternates per model, so that a drift of the machine does not favour one build)
        if [ $((i % 2)) -eq 0 ]; then order="base cand"; else order="cand base"; fi
        for b in $order; do run_build "$b" "$id" "$W/out/timing_$b/$id.jsonl.part" --no-eval; done
        for b in base cand; do mv "$W/out/timing_$b/$id.jsonl.part" "$W/out/timing_$b/$id.jsonl"; rm -f "$W/out/timing_$b/$id.jsonl.part.err"; done
      else
        for b in base cand; do echo "{\"id\":\"$id\",\"error\":\"export failed\"}" > "$W/out/timing_$b/$id.jsonl"; done
      fi
      rm -rf "${W:?}/cache/$id"
    done < "$W/ids.txt"
    for b in base cand; do cat "$W"/out/timing_$b/*.jsonl > "$W/out/timing_$b.jsonl"; done
    echo "timing done $(date -u +%FT%TZ): $(wc -l < "$W/out/timing_base.jsonl") base rows, $(wc -l < "$W/out/timing_cand.jsonl") cand rows"
    ;;
  *) echo "unknown pass $PASS"; exit 2 ;;
esac
