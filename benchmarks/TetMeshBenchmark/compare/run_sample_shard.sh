#!/bin/bash
# One shard of the tet-meshing runner sample (docs/design/TET_MESHING.md section 17.5, stage P1; workflow .github/workflows/tet-sample.yml).
# Usage: run_sample_shard.sh <shard> <shards> <TetMeshBenchmark dll> <mesh dir> <out dir> <timeout_s> <alpha ...>
# For every id of thingi-sample-ids.txt with (line index % shards) == shard, one process at a time: `TetMeshBenchmark refine <id>.stl
# <alpha> 600` for each alpha (MeshTetrahedralization with RelativeAlpha = alpha, every other parameter its default), so all alphas of a
# mesh run on the same runner. Meshes are fetched at run time (Hugging Face mirror of Thingi10K), deleted after use, never published.
# One CSV per alpha in <out dir>: a20-<shard>.csv, a40-<shard>.csv, ... (columns of the refine row, no header line); failure rows:
# <id>.stl,<time>,1,<STATUS>. Resumable: ids already in a CSV are skipped.
set -u
SHARD=$1; SHARDS=$2; DLL=$3; MESH=$4; OUT=$5; TO=$6; shift 6
ALPHAS="$*"
IDS="$(dirname "$0")/thingi-sample-ids.txt"
mkdir -p "$MESH" "$OUT"
export G3_TET_WRAP_TIME=0
idx=0
for id in $(cat "$IDS"); do
  k=$idx; idx=$((idx + 1))
  [ $((k % SHARDS)) -eq "$SHARD" ] || continue
  f=$MESH/$id.stl
  [ -s "$f" ] || curl -sS -L --retry 4 -m 600 -o "$f" "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl"
  for a in $ALPHAS; do
    csv=$OUT/a$a-$SHARD.csv; touch "$csv"
    grep -q "^$id.stl," "$csv" && continue
    if [ ! -s "$f" ]; then echo "$id.stl,,1,MISSING_MESH" >> "$csv"; continue; fi
    # exit code and output separately (a $(... | tail) substitution would lose timeout's 124)
    timeout "$TO" dotnet "$DLL" refine "$f" "$a" 600 > "$MESH/run.out" 2> /dev/null; rc=$?
    line=$(tail -n 1 "$MESH/run.out")
    if [ "$rc" -eq 124 ]; then echo "$id.stl,$TO,1,TIMEOUT" >> "$csv"
    elif [ "$rc" -ne 0 ] || [ -z "$line" ] || [ "${line#name,}" != "$line" ]; then echo "$id.stl,,1,ERROR_rc$rc" >> "$csv"
    else echo "$line" >> "$csv"; fi
  done
  rm -f "$f" "$MESH/run.out"
done
