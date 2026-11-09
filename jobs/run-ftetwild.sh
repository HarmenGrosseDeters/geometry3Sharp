#!/bin/bash
# Usage: run-ftetwild.sh <repo checkout (branch claude/tet-fetwild-eval, built)> <mesh dir (as JOB-1)> <fTetWild binary> <work dir> <out csv> <variant: default|paper> [threads=8]
# Resumable. One mesh at a time. Wall-clock time per mesh from `date`. Timeout 1800 s per mesh.
set -u
REPO=$1; MESH=$2; BIN=$3; WORK=$4; OUT=$5; VARIANT=$6; THREADS=${7:-8}
IDS="$(dirname "$0")/thingi-subset-ids.txt"
EVAL=$REPO/benchmarks/TetMeshBenchmark/bin/Release/net10.0/TetMeshBenchmark.dll
mkdir -p "$WORK"; touch "$OUT"
for id in $(cat "$IDS"); do
  grep -q "^$id.stl," "$OUT" && continue
  f=$MESH/$id.stl
  [ -s "$f" ] || { echo "$id.stl,MISSING_MESH" >> "$OUT"; continue; }
  rm -f "$WORK/out_$id"*
  extra=""; [ "$VARIANT" = "paper" ] && extra="$PAPER_FLAGS"      # set PAPER_FLAGS from `--help` (see JOB-2.md step 4)
  t0=$(date +%s.%N)
  timeout 1800 "$BIN" -i "$f" -o "$WORK/out_$id" $extra > "$WORK/log_$id.txt" 2>&1; rc=$?
  t1=$(date +%s.%N); secs=$(echo "$t1 - $t0" | bc -l)
  res=$(ls "$WORK"/out_$id*.msh "$WORK"/out_$id*.mesh 2>/dev/null | head -1)
  if [ "$rc" -eq 124 ]; then echo "$id.stl,TIMEOUT_1800s" >> "$OUT"
  elif [ -z "$res" ]; then echo "$id.stl,NO_OUTPUT_rc$rc,$secs" >> "$OUT"
  else dotnet "$EVAL" eval "$res" "$id.stl" "$secs" "$THREADS" 2>&1 | tail -1 >> "$OUT"; fi
done
echo FINISHED >> "$OUT"
