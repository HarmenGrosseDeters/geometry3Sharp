#!/bin/bash
# Warm profile of the benchmark classes (phase times, GC, allocations, hashes), one JSON line per case and operation.
#   run_classes.sh <tool dir> <out dir> [profile options, eg --repeat 5]
# BDATA: the directory with cases/<class>/<case>/{A,B}.off (see EXACT_BOOLEAN_PERFORMANCE.md); default ~/bdata.
# Build the tool first into its own directory, so that a rebuild during a run does not mix versions:
#   dotnet build benchmarks/BooleanComparison -c Release -o <tool dir>
set -e
T=$(cd "$1" && pwd)/BooleanComparison.dll; O=$(mkdir -p "$2" && cd "$2" && pwd); shift 2
P=$(cd "$(dirname "$0")" && pwd)
cd "${BDATA:-$HOME/bdata}/cases"
dotnet "$T" profile --repeat 3 "$@" "$P/cases-cherchi.txt" > "$O/cherchi.jsonl" 2> "$O/cherchi.err"
dotnet "$T" profile --repeat 3 "$@" "$P/cases-cgal.txt" > "$O/cgal.jsonl" 2> "$O/cgal.err"
dotnet "$T" profile --repeat 3 "$@" "$P/cases-thingi.txt" > "$O/thingi.jsonl" 2> "$O/thingi.err"
dotnet "$T" profile --repeat 2 "$@" grid:5 grid:10 spheres:20 > "$O/nary.jsonl" 2> "$O/nary.err"
echo finished > "$O/done"
