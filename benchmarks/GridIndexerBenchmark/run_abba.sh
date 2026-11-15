#!/usr/bin/env bash
set -euo pipefail
if [ "$#" -ne 2 ]; then echo "usage: run_abba.sh <before-dll> <after-dll>" >&2; exit 2; fi
before=$(realpath "$1"); after=$(realpath "$2")
if cmp -s "$before" "$after"; then echo 'before and after DLLs are identical' >&2; exit 1; fi
here=$(cd "$(dirname "$0")" && pwd)
dotnet build "$here/GridIndexerBenchmark.csproj" -c Release -p:G3Assembly="$before"
out="$here/bin/Release/net10.0"
sha256sum "$before" "$after"
for side in before after after before before after; do
    if [ "$side" = before ]; then cp "$before" "$out/geometry3Sharp.dll"; else cp "$after" "$out/geometry3Sharp.dll"; fi
    echo "$side"
    dotnet "$out/GridIndexerBenchmark.dll"
done
