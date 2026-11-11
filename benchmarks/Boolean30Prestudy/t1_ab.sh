#!/bin/bash
# Exact Boolean 3.0 T1 (scaling levers): alternating A/B of two BooleanComparison builds on procedural cases.
# usage: bash t1_ab.sh <tool base dir> <tool new dir> <cases dir> <rounds> <cores list> <case...>
#   cases: <cases dir>/<name>/{A,B}.off (the generator of the PR: sphere pairs, welded / soup / flipped / open)
# One process per (cores, case, round, build), union, SafeRounding on, 3 timed repeats (the best is reported),
# output: one JSON line per run (jsonl on stdout); analyze with t1_ab_analyze.py. Nothing else may run meanwhile.
set -uo pipefail
BASE=$1; NEW=$2; CASES=$3; ROUNDS=$4; CORES=$5; shift 5
for n in $CORES; do for c in "$@"; do for r in $(seq "$ROUNDS"); do for t in base new; do
  dir=$([ $t = base ] && echo "$BASE" || echo "$NEW")
  DOTNET_PROCESSOR_COUNT=$n taskset -c 0-$((n - 1)) dotnet "$dir/BooleanComparison.dll" profile --safe-rounding --repeat 3 --ops union "$CASES/$c" \
    | python3 -I -c "
import sys, json
for l in sys.stdin:
    d = json.loads(l); d['build'] = '$t'; d['cores'] = $n; d['case'] = '$c'; d['round'] = $r; print(json.dumps(d))"
done; done; done; done
