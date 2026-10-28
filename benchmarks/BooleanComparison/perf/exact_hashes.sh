#!/bin/bash
# The bit-identity check: one run per case and operation with the hash of the result mesh (vertex bits, triangles,
# ids) and of the exact result vertices (263 runs). Compare two outputs with hashes.py; any difference is a
# regression (the optimizations must not change a single bit).
#   exact_hashes.sh <tool dir> <out.jsonl>
set -e
T=$(cd "$1" && pwd)/BooleanComparison.dll; O=$(cd "$(dirname "$2")" && pwd)/$(basename "$2")
P=$(cd "$(dirname "$0")" && pwd)
cd "${BDATA:-$HOME/bdata}/cases"
dotnet "$T" profile --repeat 1 --exact "$P/cases-cgal.txt" "$P/cases-thingi.txt" "$P/cases-cherchi_small.txt" grid:5 spheres:20 > "$O" 2>&1
