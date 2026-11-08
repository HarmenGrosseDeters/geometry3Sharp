#!/usr/bin/env bash
set -euo pipefail
: "${G3_HULL_BEFORE:?source checkout at 0d08145b}"
: "${G3_HULL_AFTER:?source checkout at b58b0078}"
: "${G3_HULL_RECORDS:?original 18-record local edge recheck JSONL}"
: "${G3_HULL_WORK:?isolated writable build and result directory}"
sdk=${G3_DOTNET:-dotnet}
driver="$PWD/benchmarks/OracleIndexPilot"
test "$(git -C "$G3_HULL_BEFORE" rev-parse HEAD)" = 0d08145b06afab024e99837a8762acae547c76c0
test "$(git -C "$G3_HULL_AFTER" rev-parse HEAD)" = b58b007800015bf716136c591a8be1ce1f58d4dd
git -C "$G3_HULL_BEFORE" diff --exit-code -- benchmarks/Meshing2Benchmark/Oracle benchmarks/OracleIndexPilot
git -C "$G3_HULL_AFTER" diff --exit-code -- benchmarks/Meshing2Benchmark/Oracle benchmarks/OracleIndexPilot
"$sdk" build "$G3_HULL_BEFORE/benchmarks/OracleIndexPilot" -c Release -m:1 -nr:false --artifacts-path "$G3_HULL_WORK/before"
"$sdk" build "$G3_HULL_AFTER/benchmarks/OracleIndexPilot" -c Release -m:1 -nr:false --artifacts-path "$G3_HULL_WORK/after"
python3 "$driver/compare_hull_limit.py" --records "$G3_HULL_RECORDS" \
  --before "$G3_HULL_WORK/before/bin/OracleIndexPilot/release/OracleIndexPilot.dll" \
  --after "$G3_HULL_WORK/after/bin/OracleIndexPilot/release/OracleIndexPilot.dll" \
  --before-commit 0d08145b06afab024e99837a8762acae547c76c0 \
  --after-commit b58b007800015bf716136c591a8be1ce1f58d4dd \
  --dotnet "$sdk" --out "$G3_HULL_WORK/results"

# Optional attribution controls: b58b0078 checkouts with only the substitutions
# documented in README.md. Build them before starting this diagnostic.
if [[ -n ${G3_HULL_UNCAPPED:-} && -n ${G3_HULL_FIRST_COUNTER_OFF:-} ]]; then
  "$sdk" build "$G3_HULL_UNCAPPED/benchmarks/OracleIndexPilot" -c Release -m:1 -nr:false --artifacts-path "$G3_HULL_WORK/uncapped"
  "$sdk" build "$G3_HULL_FIRST_COUNTER_OFF/benchmarks/OracleIndexPilot" -c Release -m:1 -nr:false --artifacts-path "$G3_HULL_WORK/first-counter-off"
  : "${G3_HULL_MESH:?1044/q30 cached mesh path}"
  python3 "$driver/diagnose_hull_overhead.py" --dotnet "$sdk" --out "$G3_HULL_WORK/results" \
    --mesh "$G3_HULL_MESH" --mesh-sha256 56de8a1b8f6969f2fba35e9950b7bee0a6b746da18df0a00a694f464bacc6521 \
    --variant before "$G3_HULL_WORK/before/bin/OracleIndexPilot/release/OracleIndexPilot.dll" "$G3_HULL_BEFORE/benchmarks/Meshing2Benchmark/Oracle/StrtreeEdgeSearch.cs" \
    --variant bounded "$G3_HULL_WORK/after/bin/OracleIndexPilot/release/OracleIndexPilot.dll" "$G3_HULL_AFTER/benchmarks/Meshing2Benchmark/Oracle/StrtreeEdgeSearch.cs" \
    --variant uncapped "$G3_HULL_WORK/uncapped/bin/OracleIndexPilot/release/OracleIndexPilot.dll" "$G3_HULL_UNCAPPED/benchmarks/Meshing2Benchmark/Oracle/StrtreeEdgeSearch.cs" \
    --variant uncounted "$G3_HULL_WORK/first-counter-off/bin/OracleIndexPilot/release/OracleIndexPilot.dll" "$G3_HULL_FIRST_COUNTER_OFF/benchmarks/Meshing2Benchmark/Oracle/StrtreeEdgeSearch.cs"
fi
