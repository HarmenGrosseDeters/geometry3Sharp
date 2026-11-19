#!/bin/bash
# One shard of the MaxDihedralDegrees sample (benchmarks/TetMeshBenchmark/MaxDihedral.cs): for every id of thingi-sample-ids.txt with
# (line index % shards) == shard, `TetMeshBenchmark maxdih <id>.stl <alpha> <floor> <G>` (rows master, p4, p4g, p4s). Meshes are fetched at run
# time (Hugging Face mirror of Thingi10K), deleted after use, never published. Resumable: ids already in the CSV are skipped.
# Usage: run_maxdih_shard.sh <shard> <shards> <TetMeshBenchmark dll> <mesh dir> <out csv> <timeout_s> <alpha> <floor> <G>
set -u
SHARD=$1; SHARDS=$2; DLL=$3; MESH=$4; OUT=$5; TO=$6; ALPHA=$7; FLOOR=$8; G=$9
IDS="$(dirname "$0")/thingi-sample-ids.txt"
mkdir -p "$MESH" "$(dirname "$OUT")"; touch "$OUT"
export G3_TET_WRAP_TIME=0
idx=0
for id in $(cat "$IDS"); do
  k=$idx; idx=$((idx + 1))
  [ $((k % SHARDS)) -eq "$SHARD" ] || continue
  grep -q "^$id.stl," "$OUT" && continue
  f=$MESH/$id.stl
  [ -s "$f" ] || curl -sS -L --retry 4 -m 600 -o "$f" "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl"
  if [ ! -s "$f" ]; then echo "$id.stl,$ALPHA,$FLOOR,$G,all,MISSING_MESH" >> "$OUT"; continue; fi
  timeout "$TO" dotnet "$DLL" maxdih "$f" "$ALPHA" "$FLOOR" "$G" nohdr > "$MESH/run$SHARD.out" 2> /dev/null; rc=$?
  if [ "$rc" -eq 124 ]; then echo "$id.stl,$ALPHA,$FLOOR,$G,all,TIMEOUT" >> "$OUT"
  elif [ "$rc" -ne 0 ] || [ ! -s "$MESH/run$SHARD.out" ]; then echo "$id.stl,$ALPHA,$FLOOR,$G,all,ERROR_rc$rc" >> "$OUT"
  else cat "$MESH/run$SHARD.out" >> "$OUT"; fi
  rm -f "$f" "$MESH/run$SHARD.out"
done
