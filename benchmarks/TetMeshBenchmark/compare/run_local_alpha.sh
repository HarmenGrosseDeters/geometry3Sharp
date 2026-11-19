#!/bin/bash
# The local alpha (docs/design/TET_MESHING.md section 17.13) against uniform alpha on the 40-mesh subset (thingi-subset-ids.txt; meshes are fetched at run time
# from the Hugging Face mirror of Thingi10K, deleted after use, never committed). One process at a time, all configurations of a mesh on the same machine.
# Usage: run_local_alpha.sh <TetMeshBenchmark dll> <mesh dir> <out csv> [max triangles-MB]
# Rows: config,<refine row of TetMeshBenchmark>. Configurations: u40 u80 u160 (uniform RelativeAlpha), d250p1 / d250p2 / d150p1 (RelativeAlpha 40,
# MaxSurfaceDeviation = diagonal / 250 or / 150, LocalAlphaPasses 1 or 2).
set -u
DLL=$1; MESH=$2; OUT=$3; MAXMB=${4:-40}
IDS="$(dirname "$0")/thingi-subset-ids.txt"
mkdir -p "$MESH"; : > "$OUT"
export G3_TET_WRAP_TIME=0
for id in $(cat "$IDS"); do
  f=$MESH/$id.stl
  [ -s "$f" ] || curl -sS -L --retry 4 -m 600 -o "$f" "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl"
  [ -s "$f" ] || { echo "$id.stl,MISSING" >> "$OUT"; continue; }
  size=$(( $(stat -c %s "$f") / 1048576 ))
  [ "$size" -gt "$MAXMB" ] && { echo "skipped,$id.stl (${size} MB)" >> "$OUT"; rm -f "$f"; continue; }
  for cfg in u40:40: u80:80: u160:160: d250p1:40:250:1 d250p2:40:250:2 d150p1:40:150:1; do
    IFS=: read name alpha dev passes <<< "$cfg"
    if [ -n "$dev" ]; then export G3_TET_DEV=$dev G3_TET_LOCALPASSES=$passes; else unset G3_TET_DEV G3_TET_LOCALPASSES; fi
    line=$(timeout 600 dotnet "$DLL" refine "$f" "$alpha" 600 2>/dev/null | tail -n 1)
    [ -n "$line" ] && echo "$name,$line" >> "$OUT" || echo "$name,$id.stl,ERROR" >> "$OUT"
  done
  rm -f "$f"
done
