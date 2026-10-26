#!/bin/bash
# Builds the benchmark and runs it on all downloaded meshes (python3 fetch_data.py first).
# Usage: run_all.sh [auto|charts|cones|single]    writes results/<method>.jsonl and results/<method>_large.jsonl
# Env: BENCH_P (meshes in parallel, default 4)
set -e
cd "$(dirname "$0")"
dotnet build -c Release -v q
mkdir -p results
method=${1:-auto}
ls data/meshes/* | xargs -P ${BENCH_P:-4} -I{} ./run.sh {} $method > results/$method.jsonl
if [ -d data/meshes_large ]; then
  ls data/meshes_large/* | TIMEOUT=1200 xargs -P ${BENCH_P:-4} -I{} ./run.sh {} $method > results/${method}_large.jsonl
fi
echo results/$method.jsonl
