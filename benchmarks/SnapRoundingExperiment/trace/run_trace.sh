#!/bin/bash
# The rounding trace of one or more BooleanComparison case directories (A.off, B.off), one JSON line per operation.
#   run_trace.sh <tool dir of the instrumented build> <out.jsonl> <dist 0|1> <case dir>...
# Resumable: operations already in <out.jsonl> are skipped.
tool=$1; out=$2; dist=$3; shift 3
tmp=$(mktemp -d)
for dir in "$@"; do id=$(basename "$dir"); for op in union intersection difference; do
  grep -q "\"case\": \"$id\", \"op\": \"$op\"" "$out" 2>/dev/null && continue
  G3_ROUNDING_TRACE=1 G3_ROUNDING_TRACE_DIST=$dist dotnet "$tool/BooleanComparison.dll" profile --repeat 1 --safe-rounding --ops $op "$dir" 2> "$tmp/err" > "$tmp/out"
  # (the last trace line is the timed run: the warm-up booleans of `profile` come first)
  t=$(grep ROUNDING_TRACE "$tmp/err" | tail -1 | sed 's/^ROUNDING_TRACE //')
  [ -n "$t" ] && echo "{\"case\": \"$id\", \"op\": \"$op\", \"dist\": $dist, \"trace\": $t}" >> "$out"
done; done
rm -rf "$tmp"
