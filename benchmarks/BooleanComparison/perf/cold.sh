#!/bin/bash
# Cold booleans, one process per boolean (as compare.py runs the tools): cold.sh <tool dir> <out.jsonl> <case list>...
set -e
T=$(cd "$1" && pwd)/BooleanComparison.dll; O=$(cd "$(dirname "$2")" && pwd)/$(basename "$2"); shift 2
P=$(cd "$(dirname "$0")" && pwd)
cd "${BDATA:-$HOME/bdata}/cases"
TMP=$(mktemp -d)
: > "$O"
for list in "$@"; do
  for c in $(cat "$P/$list"); do
    for op in union intersection difference; do
      r=$(dotnet "$T" run "$c/A.off" "$c/B.off" $op "$TMP/out.off" 2>/dev/null | head -1)
      echo "{\"case\":\"$c\",\"op\":\"$op\",\"r\":$r}" >> "$O"
    done
  done
done
rm -rf "$TMP"
