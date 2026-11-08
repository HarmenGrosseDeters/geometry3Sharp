#!/bin/bash
# A/B of the local-centroid prototype (prototype/addpoint_local_centroid.diff, NOT library code): classification keeps
# the exact centroids local instead of ExactMeshArrangement.AddPoint. Variants base (master library) and addpt (diff).
# Round 1: every model of sample.txt and sample_random.txt, three ops, base and addpt alternating per model (result
# hashes for bit-identity, and timings). Round 2: the patch-heavy ops given as <id>:<op> arguments, both variants again.
# usage: bash benchmarks/LevyClassification/prototype/addpoint_job.sh <work dir> <id>:<op>...   (after job.sh)
# Resumable: one file per (round, variant, model or op); output in <work dir>/addpt/
set -euo pipefail
W=$1; shift
J=$(cd "$(dirname "$0")/.." && pwd)
SHA=$(git -C "$W/src_master" rev-parse HEAD)
for v in base addpt; do
  if [ ! -f "$W/tool_h_$v/LevyClassification.dll" ]; then
    if [ $v = addpt ]; then
      rm -rf "$W/src_addpt"; git -C "$J" worktree add -f --detach "$W/src_addpt" "$SHA"
      git -C "$W/src_addpt" apply "$J/prototype/addpoint_local_centroid.diff"; src="$W/src_addpt"
    else src="$W/src_master"; fi
    mkdir -p "$src/benchmarks/LevyClassification"
    cp "$J/Program.cs" "$J/LevyClassification.csproj" "$src/benchmarks/LevyClassification/"
    dotnet build "$src/benchmarks/LevyClassification" -c Release -o "$W/tool_h_$v"
  fi
done
O="$W/addpt"; mkdir -p "$O/r1" "$O/r2"
for id in $(cut -d' ' -f1 "$J/sample.txt" "$J/sample_random.txt"); do
  for v in base addpt; do
    f="$O/r1/${v}_$id.jsonl"
    if [ -f "$f" ] && [ "$(grep -c '"ms"' "$f")" -ge 3 ]; then continue; fi
    timeout 1200 dotnet "$W/tool_h_$v/LevyClassification.dll" "$W/cases/$id" > "$f.tmp" 2> "$f.err" \
      || echo "{\"case\":\"$id\",\"error\":\"exit $?\"}" >> "$f.tmp"
    mv "$f.tmp" "$f"
  done
done
for x in "$@"; do
  id=${x%%:*}; op=${x#*:}
  for v in base addpt; do
    f="$O/r2/${v}_${id}_$op.jsonl"
    [ -s "$f" ] && continue
    dotnet "$W/tool_h_$v/LevyClassification.dll" --ops "$op" "$W/cases/$id" > "$f.tmp" && mv "$f.tmp" "$f"
  done
done
echo done
