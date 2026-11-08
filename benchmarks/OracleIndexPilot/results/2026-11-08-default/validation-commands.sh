#!/bin/bash
set -e
unset G3_ORACLE_POINT_INDEX G3_ORACLE_EDGE_INDEX
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# Run from the repository root.
sdk=${G3_PILOT_DOTNET:-dotnet}
artifacts=/tmp/g3-oracle-default-artifacts
"$sdk" build -m:1 -nr:false geometry3Sharp_netstandard.csproj -c Release --artifacts-path "$artifacts" > /tmp/strtree-default-validation-core-build.log 2>&1
"$sdk" build -m:1 -nr:false tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > /tmp/strtree-default-validation-net48.log 2>&1
"$sdk" build -m:1 -nr:false tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > /tmp/strtree-default-validation-clipper-net48.log 2>&1
"$sdk" pack -m:1 -nr:false geometry3Sharp_netstandard.csproj -c Release -o /tmp/g3-oracle-default-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > /tmp/strtree-default-validation-pack.log 2>&1
"$sdk" pack -m:1 -nr:false addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o /tmp/g3-oracle-default-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > /tmp/strtree-default-validation-clipper-pack.log 2>&1
"$sdk" test -m:1 -nr:false tests/geometry3Sharp.Tests -c Release -f net10.0 --artifacts-path "$artifacts" --filter 'FullyQualifiedName~Meshing2OracleTests|FullyQualifiedName~Meshing2OracleModesTests|FullyQualifiedName~Meshing2OracleBoxPairsTests|FullyQualifiedName~Meshing2GoldenTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=strtree-default-oracle-tests.trx' --results-directory /tmp/strtree-default-test-results > /tmp/strtree-default-validation-tests.log 2>&1
"$sdk" run --project benchmarks/OracleIndexPilot -c Release --artifacts-path "$artifacts" -- selftest > /tmp/strtree-default-validation-selftest.log 2>&1
python3 -m unittest discover -s benchmarks/OracleIndexPilot -p 'test_*.py' -v > /tmp/strtree-default-validation-python.log 2>&1

"$sdk" build benchmarks/Meshing2Benchmark -c Release -f net10.0 --artifacts-path "$artifacts" -m:1 -nr:false
"$sdk" /tmp/g3-oracle-default-artifacts/bin/Meshing2Benchmark/release_net10.0/Meshing2Benchmark.dll selfcheck > /tmp/strtree-default-validation-integrated.log 2>&1
G3_ORACLE_POINT_INDEX=sweep G3_ORACLE_EDGE_INDEX=sweep "$sdk" /tmp/g3-oracle-default-artifacts/bin/Meshing2Benchmark/release_net10.0/Meshing2Benchmark.dll selfcheck > /tmp/strtree-default-validation-sweep.log 2>&1
