#!/bin/bash
# Rejected candidates vs final result of MeshRobustRepair (CandidateBench.cs), both option sets, 400 + 30 set.
# Usage: BENCH_P=3 ./run_candidates.sh   -> results/candidates.jsonl (defaults), results/candidates_lost.jsonl (CheckLostComponents)
# Analysis: python3 analyze_candidates.py results/candidates.jsonl results/candidates_lost.jsonl
set -e
cd "$(dirname "$0")"
dotnet build -c Release -v q
mkdir -p results
for cfg in default lost; do
  if [ $cfg = lost ]; then out=results/candidates_lost.jsonl; export BENCH_LOST_COMPONENTS=1; else out=results/candidates.jsonl; export BENCH_LOST_COMPONENTS=0; fi
  : > $out
  ls data/meshes/* | BENCH_REPAIR=candidates TIMEOUT=600 xargs -P ${BENCH_P:-3} -I{} ./run.sh {} std none rel >> $out
  if [ -d data/meshes_large ]; then
    ls data/meshes_large/* | BENCH_REPAIR=candidates TIMEOUT=1200 xargs -P ${BENCH_P:-3} -I{} ./run.sh {} std none rel >> $out
  fi
done
