#!/bin/bash
# Exact Boolean 2.0 final run (coordinator, owner 2026-11-09): every model of the #385 Thingi10K harness (10,000 raw
# meshes, pinned dataset revision), each with its #385 pairing (B = A rotated 20 degrees about (1, 1, 0) and shifted:
# BooleanComparison export), union / intersection / difference, SafeRounding on. No Alpha Wrap.
#
#   phase target   master (with #449) on the target class: closed, selfint = 0 (5413 models in #385). Every model is
#                  downloaded and exported once here to learn its class; the others are only classified.
#   phase control  the #385 baseline build cd7fb8ab on the target class, same machine (timing comparison)
#   phase rest     master on the other classes: closed self-intersecting, closed with unknown selfint, open
#
# Builds (pinned commits):
#   master   1fd946aa  master with #449 (MeshRobustRepair / volume check at extreme extents)
#   base385  cd7fb8ab  the #385 baseline
# The export (input classes, A.off / B.off) always comes from master's BooleanComparison, so both builds see the
# same inputs. Models run one at a time (each operation uses every core); nothing else may run on the machine.
#
# usage (from the repository root):  bash runs/final/job.sh <phase> [work dir]      phase: target | control | rest
#        IDS_FILE=<file> limits the run to the ids in it (a smoke run in a separate work dir)
# Resumable: rerun the same command after an interruption; finished models are skipped. Raw meshes are deleted
# after each model (39 GB in all). Then:
#   python3 runs/final/analyze.py <work dir> > runs/final/results/summary_<phase>.md
#   copy <work dir>/out/<phase>.jsonl and class.tsv to runs/final/results/, commit, push to runs/boolean2-final
set -euo pipefail
PHASE=${1:?usage: job.sh target|control|rest [work dir]}
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/final
W=${2:-$HOME/boolean2-final-run}
mkdir -p "$W/out/$PHASE" "$W/out/class" "$W/raw" "$W/cases"
BUILDS="master:1fd946aa8a3c7c0cf15f4c9cddbe06449254668d base385:cd7fb8abba0af09d5725b60c700dfec95234e7ad"

git -C "$ROOT" fetch -q origin master
for b in $BUILDS; do
  name=${b%%:*}; sha=${b#*:}
  if [ ! -f "$W/runner_$name/FinalRunner.dll" ]; then
    rm -rf "${W:?}/src_$name" "${W:?}/rsrc_$name"
    git -C "$ROOT" worktree add -f --detach "$W/src_$name" "$sha"
    cp -r "$J/FinalRunner" "$W/rsrc_$name"
    dotnet build "$W/rsrc_$name" -c Release -p:G3Src="$W/src_$name" -o "$W/runner_$name"
  fi
done
if [ ! -f "$W/tool_export/BooleanComparison.dll" ]; then
  dotnet build "$W/src_master/benchmarks/BooleanComparison" -c Release -f net10.0 -o "$W/tool_export"
fi
for b in $BUILDS; do echo "${b%%:*} ${b#*:}"; done > "$W/out/builds.txt"

export FETCH="$J/fetch_thingi.py"
# the ids, from the pinned inventory
python3 - "$W/raw" > "$W/ids.txt" <<'PY'
import sys
sys.argv = ['fetch_thingi.py', sys.argv[1]]
exec(open(__import__('os').environ['FETCH']).read().split('def main')[0])
inv = inventory(sys.argv[1] + '/inventory.json')
print('\n'.join(sorted(inv, key=lambda s: int(s) if s.isdigit() else 0)))
PY
# (IDS_FILE: a subset, for a smoke run)
if [ -n "${IDS_FILE:-}" ]; then cp "$IDS_FILE" "$W/ids.txt"; fi
echo "phase $PHASE: $(wc -l < "$W/ids.txt") ids"

# class of a model from its export: target (closed, selfint 0), selfint (closed, > 0), unknown (closed, not counted),
# open; unavailable if the export failed
class_of() {
  python3 -c "
import json, sys
try: i = json.load(open(sys.argv[1]))
except Exception: print('unavailable'); sys.exit()
print('open' if not i.get('closed') else 'target' if i.get('selfint') == 0 else 'selfint' if (i.get('selfint') or 0) > 0 else 'unknown')" "$1"
}

prepare() {   # download and export one model into $W/cases/$1; prints its class
  local id=$1 f
  rm -rf "${W:?}/cases/$id"
  python3 "$J/fetch_thingi.py" "$W/raw" "$id" > /dev/null
  f=$(ls "$W"/raw/"$id".* 2> /dev/null | grep -v inventory | head -1 || true)
  if [ -n "$f" ]; then
    timeout 1800 dotnet "$W/tool_export/BooleanComparison.dll" export "$f" "$W/cases/$id" > "$W/cases/export_$id.log" 2>&1 || true
    rm -f "$f"
  fi
  class_of "$W/cases/$id/info.json"
}

run_model() {   # build id class -> out/$PHASE/$id.jsonl
  local build=$1 id=$2 cls=$3 out="$W/out/$PHASE/$2.jsonl"
  timeout 3600 dotnet "$W/runner_$build/FinalRunner.dll" "$W/cases/$id" "$id" "$cls" > "$out.tmp" 2> "$out.err" \
    || echo "{\"id\":\"$id\",\"class\":\"$cls\",\"error\":\"exit $?\"}" >> "$out.tmp"
  mv "$out.tmp" "$out"
}

n=0
while read -r id; do
  n=$((n + 1))
  out="$W/out/$PHASE/$id.jsonl"
  [ -f "$out" ] && continue
  cls=""
  [ -f "$W/out/class/$id" ] && cls=$(cat "$W/out/class/$id")
  case $PHASE in
    target)  [ -n "$cls" ] && [ "$cls" != target ] && continue ;;
    control) [ "$cls" != target ] && continue ;;
    rest)    { [ -z "$cls" ] || [ "$cls" = target ]; } && continue ;;
  esac
  cls=$(prepare "$id")
  echo "$cls" > "$W/out/class/$id"
  if [ "$PHASE" = target ] && [ "$cls" != target ]; then rm -rf "${W:?}/cases/$id"; continue; fi
  if [ "$cls" = unavailable ]; then
    echo "{\"id\":\"$id\",\"class\":\"unavailable\",\"error\":\"export failed\"}" > "$out"
  else
    run_model "$([ "$PHASE" = control ] && echo base385 || echo master)" "$id" "$cls"
  fi
  rm -rf "${W:?}/cases/$id"
  [ $((n % 100)) -eq 0 ] && echo "$(date -u +%FT%TZ) $PHASE: $n ids seen, $(ls "$W/out/$PHASE" | grep -c jsonl$) done"
done < "$W/ids.txt"
cat "$W"/out/"$PHASE"/*.jsonl > "$W/out/$PHASE.jsonl"
for f in "$W"/out/class/*; do echo "$(basename "$f") $(cat "$f")"; done > "$W/out/class.tsv"
echo "phase $PHASE done: $(wc -l < "$W/out/$PHASE.jsonl") rows; classes: $(cut -d' ' -f2 "$W/out/class.tsv" | sort | uniq -c | tr '\n' ' ')"
