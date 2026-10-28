#!/bin/bash
# Parallel scaling: the same profile on 1, 2 and 4 cores.   scaling.sh <tool dir> <out dir>
set -e
T=$(cd "$1" && pwd)/BooleanComparison.dll; O=$(mkdir -p "$2" && cd "$2" && pwd)
P=$(cd "$(dirname "$0")" && pwd)
cd "${BDATA:-$HOME/bdata}/cases"
for n in 1 2 4; do
  DOTNET_PROCESSOR_COUNT=$n taskset -c 0-$((n - 1)) dotnet "$T" profile --repeat 3 "$P/cases-cherchi_small.txt" "$P/cases-thingi12.txt" \
    grid:5 "cherchi/bunny200k.obj+cow200K.obj" > "$O/cores$n.jsonl" 2> "$O/cores$n.err"
done
