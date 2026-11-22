#!/bin/sh
# The A/B of docs/research/ROUNDING_COST_2026-11.md inside the rounding: a tool built with instrument_phases.py, each
# case with every variant in turn (one process each, so a drift of the machine hits all variants alike).
#   [VARIANTS="base axes split split+axes median pending"] [FIRST=1] run_ab_phases.sh <tool dir> <out dir> <rounds> <case dir>...
# Output: <out dir>/<variant>_r<round>.jsonl (the lines of `BooleanComparison profile --repeat 1 --safe-rounding`),
# rounds FIRST to FIRST + <rounds> - 1; tables: `summarize_phases.py ab <out dir>/base <out dir>/<variant> <rounds>`.
tool=$1; out=$2; rounds=$3; shift 3
mkdir -p "$out"
r=${FIRST:-1}
last=$((r + rounds - 1))
while [ "$r" -le "$last" ]; do
  for c in "$@"; do
    for v in ${VARIANTS:-base axes split split+axes median}; do
      case $v in
        base) dotnet "$tool/BooleanComparison.dll" profile --repeat 1 --safe-rounding "$c" >> "$out/${v}_r$r.jsonl" 2>/dev/null ;;
        pending) G3_NEAR_PENDING=cached dotnet "$tool/BooleanComparison.dll" profile --repeat 1 --safe-rounding "$c" >> "$out/${v}_r$r.jsonl" 2>/dev/null ;;
        median) G3_TREE_MEDIAN=1 dotnet "$tool/BooleanComparison.dll" profile --repeat 1 --safe-rounding "$c" >> "$out/${v}_r$r.jsonl" 2>/dev/null ;;
        *) G3_FIRST_SEARCH=$v dotnet "$tool/BooleanComparison.dll" profile --repeat 1 --safe-rounding "$c" >> "$out/${v}_r$r.jsonl" 2>/dev/null ;;
      esac
    done
  done
  r=$((r + 1))
done
