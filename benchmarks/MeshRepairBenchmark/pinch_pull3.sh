#!/bin/bash
# Pinch-cap measurement (runs/pinch-pull-3): the raw MeshAlphaWrap (BENCH_REPAIR=wrap) and the full wrap pipeline
# (BENCH_REPAIR=wrappipe: wrap, SimplifyWithin, no quality pass) at 300/3000, with and without PullPinchPoints, on the
# three meshes of the question. One process at a time; one JSON row per run in the output file.
# Usage: pinch_pull3.sh <out.jsonl>   (meshes in data/ids/meshes, fetched by fetch_data.py --ids; run from this directory)
set -u
OUT=${1:?out.jsonl}
DLL=bin/Release/net10.0/MeshRepairBenchmark.dll
export DOTNET_CLI_TELEMETRY_OPTOUT=1
: > "$OUT"
for id in 102793 118678 1505037; do
  for mode in wrap wrappipe; do
    for pull in 0 1; do
      s=$(date +%s.%N)
      row=$(BENCH_REPAIR=$mode BENCH_WRAP_ALPHA=300 BENCH_WRAP_OFFSET=3000 BENCH_WRAP_PULL=$pull \
            timeout 1200 dotnet "$DLL" "data/ids/meshes/$id.stl" std 2>/dev/null | tail -1)
      e=$(date +%s.%N)
      printf '{"id":"%s","mode":"%s","pull":%s,"harness_s":%.1f,"row":%s}\n' "$id" "$mode" "$pull" "$(echo "$e - $s" | bc)" "${row:-null}" >> "$OUT"
    done
  done
done
echo done
