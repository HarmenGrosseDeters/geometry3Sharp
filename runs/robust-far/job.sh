#!/bin/bash
# MeshRobustRepair far parts (PR #425) against its base (#420's head) on the repair benchmark's 400 set (fetch_data.py
# --no-large: 6 defect categories x 60 Thingi10K meshes + 40 clean controls): BENCH_REPAIR=robust BENCH_FIDELITY=0
# TIMEOUT=300, one mesh at a time per build (BENCH_P=2: the two builds in turn, two meshes in parallel).
#
# Builds (pinned commits):
#   base  05ce0b3b  PR #420 head (claude/far-component-tolerance)
#   pr    7af8d37e  PR #425 head (claude/robust-repair-far-parts)
#
# usage: bash runs/robust-far/job.sh [work dir]   (from the repository root; a few hours on 4 cores)
# Resumable: rerun the same command after an interruption; finished (build, mesh) lines are kept.
# then:  python3 runs/robust-far/analyze.py <work dir>/out > runs/robust-far/results/summary.md
#        copy <work dir>/out/*.jsonl and builds.txt to runs/robust-far/results/, commit and push to runs/boolean2-combined
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
W=${1:-$HOME/robust-far}
mkdir -p "$W/out"
BUILDS="base:05ce0b3bc19c1c5568351c80dbc7f277e96048ba pr:7af8d37ebbd63704a6d4ed0e71f4cdce0d8054e0"
git -C "$ROOT" fetch -q origin claude/far-component-tolerance claude/robust-repair-far-parts
for b in $BUILDS; do
  name=${b%%:*}; sha=${b#*:}
  if [ ! -f "$W/src_$name/benchmarks/MeshRepairBenchmark/bin/Release/net10.0/MeshRepairBenchmark.dll" ]; then
    rm -rf "${W:?}/src_$name"
    git -C "$ROOT" worktree add -f --detach "$W/src_$name" "$sha"
    dotnet build "$W/src_$name/benchmarks/MeshRepairBenchmark" -c Release -f net10.0 -v q
  fi
done
for b in $BUILDS; do echo "${b%%:*} $(git -C "$ROOT" rev-parse "${b#*:}")"; done > "$W/out/builds.txt"
# the 400 set, fetched once (into the base build's benchmark directory, shared by both)
D=$W/src_base/benchmarks/MeshRepairBenchmark
[ -d "$D/data/meshes" ] && [ "$(ls "$D/data/meshes" | wc -l)" -ge 400 ] || python3 "$D/fetch_data.py" --no-large
for b in $BUILDS; do
  name=${b%%:*}
  f="$W/out/$name.jsonl"
  touch "$f"
  for mesh in "$D"/data/meshes/*; do
    base=$(basename "$mesh")
    grep -q "\"file\":\"$base\"" "$f" && continue
    echo "$mesh"
  done | BENCH_REPAIR=robust BENCH_FIDELITY=0 TIMEOUT=300 xargs -P 2 -I{} bash "$W/src_$name/benchmarks/MeshRepairBenchmark/run.sh" {} >> "$f"
done
echo "done: base $(wc -l < "$W/out/base.jsonl"), pr $(wc -l < "$W/out/pr.jsonl") lines (expected 400 each)"
