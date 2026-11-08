#!/usr/bin/env bash
set -euo pipefail
: "${G3_SOURCES_BEFORE:?unmodified source checkout at 941d9473}"
: "${G3_SOURCES_AFTER:?source coverage feature checkout}"
: "${G3_SOURCES_WORK:?external writable work directory}"
sdk=${G3_DOTNET:-dotnet}
driver="$PWD/benchmarks/OracleSourcePilot"
unset G3_ORACLE_SOURCE_INDEX G3_ORACLE_TIMING G3_ORACLE_POINT_INDEX G3_ORACLE_EDGE_INDEX
test "$(git -C "$G3_SOURCES_BEFORE" rev-parse HEAD)" = 941d94730646c890091b9b4c3db7582ae17be6e0
test "$(git -C "$G3_SOURCES_AFTER" rev-parse HEAD)" = 3ad04065aabc0c7ec05ce2a162e2b23eb513dc81
git -C "$G3_SOURCES_BEFORE" diff --exit-code -- benchmarks/Meshing2Benchmark/Oracle
test -z "$(git -C "$G3_SOURCES_BEFORE" ls-files --others --exclude-standard -- benchmarks/Meshing2Benchmark/Oracle)"

# Only the diagnostic driver is added to the old checkout. Its oracle stays unmodified.
mkdir -p "$G3_SOURCES_BEFORE/benchmarks/OracleSourcePilot"
cp "$driver/Program.cs" "$driver/OracleSourcePilot.csproj" "$G3_SOURCES_BEFORE/benchmarks/OracleSourcePilot/"
"$sdk" build "$G3_SOURCES_BEFORE/benchmarks/OracleSourcePilot" -c Release -m:1 -nr:false --artifacts-path "$G3_SOURCES_WORK/before"
"$sdk" build "$G3_SOURCES_BEFORE/benchmarks/Meshing2Benchmark" -c Release -f net10.0 -m:1 -nr:false --artifacts-path "$G3_SOURCES_WORK/before"
"$sdk" build "$G3_SOURCES_AFTER/benchmarks/OracleSourcePilot" -c Release -m:1 -nr:false --artifacts-path "$G3_SOURCES_WORK/after"
before="$G3_SOURCES_WORK/before/bin/OracleSourcePilot/release/OracleSourcePilot.dll"
after="$G3_SOURCES_WORK/after/bin/OracleSourcePilot/release/OracleSourcePilot.dll"
producer="$G3_SOURCES_WORK/before/bin/Meshing2Benchmark/release_net10.0/Meshing2Benchmark.dll"

# Run these alone on the machine, sequentially. Do not rebuild frozen outputs.
python3 "$driver/profile_sources.py" --dotnet "$sdk" --pilot "$before" --mesher "$producer" --out "$G3_SOURCES_WORK/runs" --label baseline-unmodified
python3 "$driver/profile_sources.py" --dotnet "$sdk" --pilot "$after" --mesher "$producer" --out "$G3_SOURCES_WORK/runs" --label grid-final --mode grid --repeats 3
python3 "$driver/profile_sources.py" --dotnet "$sdk" --pilot "$after" --mesher "$producer" --out "$G3_SOURCES_WORK/runs" --label scan-final --mode scan
python3 "$driver/check_reported_boundary.py" --dotnet "$sdk" --before "$before" --after "$after" \
  --input "$G3_SOURCES_WORK/runs/lens-30001.input.txt" --mesh "$G3_SOURCES_WORK/runs/lens-30001.mesh.txt" --out "$G3_SOURCES_WORK/boundary"
python3 "$driver/verify_results.py" --runs "$G3_SOURCES_WORK/runs" --boundary "$G3_SOURCES_WORK/boundary"

# Separate validation outputs; do not time these or overlap them with the commands above.
(
  cd "$G3_SOURCES_AFTER"
  outputs="$G3_SOURCES_WORK/validation"
  "$sdk" build geometry3Sharp_netstandard.csproj -c Release -m:1 -nr:false --artifacts-path "$outputs"
  "$sdk" build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true -m:1 -nr:false --artifacts-path "$outputs"
  "$sdk" build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true -m:1 -nr:false --artifacts-path "$outputs"
  "$sdk" build benchmarks/Meshing2Benchmark -c Release -f net10.0 -m:1 -nr:false --artifacts-path "$outputs"
  "$sdk" pack geometry3Sharp_netstandard.csproj -c Release -p:Version=1.0.0-ci.1 -o "$G3_SOURCES_WORK/packages" -m:1 -nr:false --artifacts-path "$outputs"
  "$sdk" pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -p:Version=1.0.0-ci.1 -o "$G3_SOURCES_WORK/packages" -m:1 -nr:false --artifacts-path "$outputs"
  python3 benchmarks/OracleSourcePilot/mutate_source_coverage.py --dotnet "$sdk" --work "$G3_SOURCES_WORK/mutations"
  "$sdk" test tests/geometry3Sharp.Tests -c Release -f net10.0 -m:1 -nr:false --artifacts-path "$outputs" \
    --filter 'FullyQualifiedName~Meshing2OracleTests|FullyQualifiedName~Meshing2OracleModesTests|FullyQualifiedName~Meshing2OracleBoxPairsTests|FullyQualifiedName~Meshing2GoldenTests|FullyQualifiedName~Meshing2OracleSourceCoverageTests|FullyQualifiedName~Meshing2OracleFastPathCheckTests'
  benchmark="$outputs/bin/Meshing2Benchmark/release_net10.0/Meshing2Benchmark.dll"
  "$sdk" "$benchmark" ci
  G3_ORACLE_SOURCE_INDEX=scan "$sdk" "$benchmark" ci
  if G3_ORACLE_SOURCE_INDEX=invalid "$sdk" "$benchmark" selfcheck; then
    exit 1
  else
    test "$?" -eq 2
  fi
  python3 -m py_compile benchmarks/OracleSourcePilot/*.py
  python3 -m unittest discover -s benchmarks/OracleSourcePilot -p 'test_*.py' -v
)
