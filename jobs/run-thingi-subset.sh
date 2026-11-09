#!/bin/bash
# Usage: run-thingi-subset.sh <repo checkout> <mesh dir> <out csv> [relativeAlpha=20] [relativeOffset=600]
# Resumable: ids already in <out csv> are skipped. One mesh at a time, one process, timeout 180 s (900 s above 30 MB).
set -u
REPO=$1; MESH=$2; OUT=$3; RA=${4:-20}; RO=${5:-600}
IDS="$(dirname "$0")/thingi-subset-ids.txt"
DLL=$REPO/benchmarks/TetMeshBenchmark/bin/Release/net10.0/TetMeshBenchmark.dll
mkdir -p "$MESH"; touch "$OUT"
for id in $(cat "$IDS"); do
  grep -q "^$id.stl," "$OUT" && continue
  f=$MESH/$id.stl
  [ -s "$f" ] || curl -sS -L -m 300 -o "$f" "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl"
  sz=$(stat -c %s "$f"); lim=180; [ "$sz" -gt 30000000 ] && lim=900
  line=$(timeout $lim dotnet "$DLL" file "$f" $RA $RO 2>&1 | grep -v "^input," | tail -1); rc=${PIPESTATUS[0]}
  if [ "$rc" -eq 124 ]; then echo "$id.stl,TIMEOUT_${lim}s" >> "$OUT"; else echo "$line" >> "$OUT"; fi
done
echo "FINISHED" >> "$OUT"
