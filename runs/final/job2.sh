#!/bin/bash
# Boolean runner job v2 (owner decision 2026-11-10): correctness and timing apart, exports cached across phases and
# builds, NotCleanReason only where a result differs from the reference build's. job.sh stays as it was for the runs
# pinned with it (FINAL-1 rest and confirmation).
#
#   bash runs/final/job2.sh correct <work dir> <name>:<sha> [classes] [ref rows dir]
#        every model of the classes (default: target; a comma list of target, selfint, unknown, open) on that build,
#        4 models at a time, each on 1 core (DOTNET_PROCESSOR_COUNT=1, taskset): rows in <work dir>/out/correct_<name>/,
#        timings in them are not comparable (several processes at once); with a ref rows dir (the out/correct_<ref>/ of
#        another build), a result whose hash equals the reference's takes its reason over instead of NotCleanReason
#   bash runs/final/job2.sh timing <work dir> <sample file> <name>:<sha> [<name>:<sha> ...]
#        the ids of the sample file (runs/final/sample_timing.py writes one), alone, all cores, the builds interleaved
#        per model (A B A B ... in two rounds), without NotCleanReason: rows in <work dir>/out/timing/
#
# The cache (CACHE, default ~/b2cache): per id its class and its export (A.off, B.off, info.json) from the pinned
# exporter, made once and reused by every phase, build and work dir. Its disk need is about the size of the exports
# of the models used (the run stops if less than MIN_FREE_GB, default 20, is free). IDS_FILE limits it to the ids in it.
# Resumable: rerun the same command; finished (id, build) files are kept.
set -euo pipefail
MODE=${1:?usage: job2.sh correct|timing <work dir> ...}
W=${2:?work dir}
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/final
CACHE=${CACHE:-$HOME/b2cache}
MIN_FREE_GB=${MIN_FREE_GB:-20}
# the exporter of the cache: always the one of the first FINAL-1 phase (the inputs must not change between runs)
EXPORTER=1fd946aa8a3c7c0cf15f4c9cddbe06449254668d
mkdir -p "$W/out" "$CACHE/cases" "$CACHE/class" "$CACHE/raw"

build_runner() {   # name sha -> $W/runner_<name>/FinalRunner.dll (rebuilt if the sha differs)
  local name=$1 sha=$2
  if [ ! -f "$W/runner_$name/FinalRunner.dll" ] || [ "$(cat "$W/runner_$name/build.sha" 2>/dev/null || true)" != "$sha" ]; then
    rm -rf "${W:?}/runner_$name" "${W:?}/rsrc_$name"
    git -C "$ROOT" worktree remove --force "$W/src_$name" 2>/dev/null || rm -rf "${W:?}/src_$name"
    git -C "$ROOT" fetch -q origin "$sha" 2>/dev/null || true
    git -C "$ROOT" worktree add -f --detach "$W/src_$name" "$sha"
    cp -r "$J/FinalRunner" "$W/rsrc_$name"
    dotnet build "$W/rsrc_$name" -c Release -p:G3Src="$W/src_$name" -o "$W/runner_$name" > "$W/build_$name.log"
    echo "$sha" > "$W/runner_$name/build.sha"
  fi
}

if [ ! -f "$CACHE/tool_export/BooleanComparison.dll" ]; then
  git -C "$ROOT" worktree add -f --detach "$CACHE/src_export" "$EXPORTER" 2>/dev/null || true
  dotnet build "$CACHE/src_export/benchmarks/BooleanComparison" -c Release -f net10.0 -o "$CACHE/tool_export" > "$CACHE/build_export.log"
fi

class_of() {
  python3 -c "
import json, sys
try: i = json.load(open(sys.argv[1]))
except Exception: print('unavailable'); sys.exit()
print('open' if not i.get('closed') else 'target' if i.get('selfint') == 0 else 'selfint' if (i.get('selfint') or 0) > 0 else 'unknown')" "$1"
}

free_gb() { df -Pk "$CACHE" | awk 'NR==2 {print int($4 / 1048576)}'; }

cached() {   # id -> its class; downloads and exports it into the cache first if it is not there
  local id=$1 f
  if [ -f "$CACHE/class/$id" ]; then cat "$CACHE/class/$id"; return; fi
  if [ "$(free_gb)" -lt "$MIN_FREE_GB" ]; then echo "less than $MIN_FREE_GB GB free in $CACHE: stopped" >&2; exit 2; fi
  rm -rf "${CACHE:?}/cases/$id"
  python3 "$J/fetch_thingi.py" "$CACHE/raw" "$id" > /dev/null || true
  f=$(ls "$CACHE"/raw/"$id".* 2> /dev/null | grep -v inventory | head -1 || true)
  if [ -n "$f" ]; then
    timeout 1800 dotnet "$CACHE/tool_export/BooleanComparison.dll" export "$f" "$CACHE/cases/$id" > "$CACHE/cases/export_$id.log" 2>&1 || true
    rm -f "$f"
  fi
  local cls; cls=$(class_of "$CACHE/cases/$id/info.json")
  # (a failed download is not cached as unavailable: the next run tries again)
  if [ -n "$f" ] || [ "$cls" != unavailable ]; then echo "$cls" > "$CACHE/class/$id"; fi
  [ "$cls" = unavailable ] && rm -rf "${CACHE:?}/cases/$id"
  echo "$cls"
}

all_ids() {
  if [ -n "${IDS_FILE:-}" ]; then cat "$IDS_FILE"; return; fi
  FETCH="$J/fetch_thingi.py" python3 - "$CACHE/raw" <<'PY'
import os, sys
sys.argv = ['fetch_thingi.py', sys.argv[1]]
exec(open(os.environ['FETCH']).read().split('def main')[0])
inv = inventory(sys.argv[1] + '/inventory.json')
print('\n'.join(sorted(inv, key=lambda s: int(s) if s.isdigit() else 0)))
PY
}

case $MODE in
correct)
  B=${3:?name:sha}; CLASSES=${4:-target}; REF=${5:-}
  name=${B%%:*}; sha=${B#*:}
  build_runner "$name" "$sha"
  O="$W/out/correct_$name"; mkdir -p "$O"
  echo "$name $sha classes $CLASSES ref ${REF:-none}" > "$O/build.txt"
  # the classes first (serially: the downloads and exports), then the models of the classes, 4 at a time
  all_ids > "$W/ids_all.txt"
  : > "$W/ids_$name.txt"
  while read -r id; do
    cls=$(cached "$id")
    case ",$CLASSES," in *",$cls,"*) echo "$id $cls" >> "$W/ids_$name.txt" ;; esac
  done < "$W/ids_all.txt"
  echo "correct $name: $(wc -l < "$W/ids_$name.txt") models of $CLASSES"
  run_one() {   # id class core
    local id=$1 cls=$2 core=$3 out="$O/$1.jsonl" ref=()
    [ -f "$out" ] && return 0
    [ -n "$REF" ] && [ -f "$REF/$id.jsonl" ] && ref=(--ref "$REF/$id.jsonl")
    DOTNET_PROCESSOR_COUNT=1 timeout 7200 taskset -c "$core" dotnet "$W/runner_$name/FinalRunner.dll" "$CACHE/cases/$id" "$id" "$cls" "${ref[@]}" \
      > "$out.tmp" 2> "$out.err" || echo "{\"id\":\"$id\",\"class\":\"$cls\",\"error\":\"exit $?\"}" >> "$out.tmp"
    mv "$out.tmp" "$out"
  }
  # 4 workers, worker k on core k with every 4th model (a fixed split: each core runs one model at a time)
  for k in 0 1 2 3; do
    ( n=0; while read -r id cls; do
        [ $((n % 4)) -eq $k ] && run_one "$id" "$cls" "$k"
        n=$((n + 1))
      done < "$W/ids_$name.txt" ) &
  done
  wait
  cat "$O"/*.jsonl > "$W/out/correct_$name.jsonl"
  echo "correct $name done: $(wc -l < "$W/out/correct_$name.jsonl") rows"
  ;;
timing)
  SAMPLE=${3:?sample file}; shift 3
  [ $# -ge 1 ] || { echo "timing: at least one build" >&2; exit 1; }
  for b in "$@"; do build_runner "${b%%:*}" "${b#*:}"; done
  O="$W/out/timing"; mkdir -p "$O"
  printf '%s\n' "$@" > "$O/builds.txt"
  for r in 1 2; do
    while read -r id _; do
      [ -z "$id" ] && continue
      cls=$(cached "$id")
      [ "$cls" = unavailable ] && continue
      # (the builds alternate: in round 2 in the other order)
      builds=("$@"); [ $r -eq 2 ] && builds=($(printf '%s\n' "$@" | tac))
      for b in "${builds[@]}"; do
        name=${b%%:*}; out="$O/${id}_${name}_r$r.jsonl"
        [ -f "$out" ] && continue
        timeout 3600 dotnet "$W/runner_$name/FinalRunner.dll" "$CACHE/cases/$id" "$id" "$cls" --no-eval > "$out.tmp" 2> "$out.err" \
          || echo "{\"id\":\"$id\",\"class\":\"$cls\",\"error\":\"exit $?\"}" >> "$out.tmp"
        python3 -c "import json,sys; [print(json.dumps(dict(json.loads(l), build=sys.argv[1], round=int(sys.argv[2])))) for l in open(sys.argv[3]) if l.strip()]" "$name" "$r" "$out.tmp" > "$out"
        rm -f "$out.tmp"
      done
    done < "$SAMPLE"
  done
  cat "$O"/*.jsonl > "$W/out/timing.jsonl"
  echo "timing done: $(wc -l < "$W/out/timing.jsonl") rows"
  ;;
*) echo "mode: correct | timing" >&2; exit 1 ;;
esac
