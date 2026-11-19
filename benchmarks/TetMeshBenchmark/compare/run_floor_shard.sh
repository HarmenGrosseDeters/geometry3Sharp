#!/bin/bash
# (data branch runs/tet-rescue-stop only) One shard of the rescue-stop measurement of #1154: for every id of thingi-sample-ids.txt with
# (line index % shards) == shard, one process at a time, `TetMeshBenchmark refine <id>.stl 40 600` with G3_TET_FLOOR=<floor> for each floor,
# with the per-pass trace of the improver (G3_TET_TRACE_FILE). Meshes are fetched at run time, deleted after use, never published.
# Usage: run_floor_shard.sh <shard> <shards> <dll> <mesh dir> <out dir> <timeout_s> <floor ...>
# Output in <out dir>: f<floor>-<shard>.csv (refine rows, no header; failure rows <id>.stl,<time>,1,<STATUS>), trace-<floor>-<shard>.csv
# (<id>.stl,<phase>,<pass>,<tets below floor>,<worst angle>,<seconds since the improver started>). Resumable (ids already in a CSV are skipped).
set -u
SHARD=$1; SHARDS=$2; DLL=$3; MESH=$4; OUT=$5; TO=$6; shift 6
FLOORS="$*"
IDS="$(dirname "$0")/thingi-sample-ids.txt"
mkdir -p "$MESH" "$OUT"
export G3_TET_WRAP_TIME=0
idx=0
for id in $(cat "$IDS"); do
  k=$idx; idx=$((idx + 1))
  [ $((k % SHARDS)) -eq "$SHARD" ] || continue
  f=$MESH/$id.stl
  [ -s "$f" ] || curl -sS -L --retry 4 -m 600 -o "$f" "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl"
  for fl in $FLOORS; do
    csv=$OUT/f$fl-$SHARD.csv; touch "$csv"
    grep -q "^$id.stl," "$csv" && continue
    if [ ! -s "$f" ]; then echo "$id.stl,,1,MISSING_MESH" >> "$csv"; continue; fi
    rm -f "$MESH/trace.tmp"
    G3_TET_FLOOR=$fl G3_TET_TRACE_FILE="$MESH/trace.tmp" timeout "$TO" dotnet "$DLL" refine "$f" 40 600 > "$MESH/run.out" 2> /dev/null; rc=$?
    line=$(tail -n 1 "$MESH/run.out")
    if [ "$rc" -eq 124 ]; then echo "$id.stl,$TO,1,TIMEOUT" >> "$csv"
    elif [ "$rc" -ne 0 ] || [ -z "$line" ] || [ "${line#name,}" != "$line" ]; then echo "$id.stl,,1,ERROR_rc$rc" >> "$csv"
    else echo "$line" >> "$csv"; [ -s "$MESH/trace.tmp" ] && cat "$MESH/trace.tmp" >> "$OUT/trace-$fl-$SHARD.csv"; fi
  done
  rm -f "$f" "$MESH/run.out" "$MESH/trace.tmp"
done
