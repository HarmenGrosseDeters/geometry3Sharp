#!/bin/bash
set -e
work=${G3_PILOT_WORK:-$(mktemp -d)}
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# Run from the repository root.
sdk=${G3_PILOT_DOTNET:-dotnet}
artifacts=$work/strtree-final-validation-artifacts
"$sdk" build geometry3Sharp_netstandard.csproj -c Release --artifacts-path "$artifacts" > $work/strtree-validation-core-build.log 2>&1
"$sdk" build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > $work/strtree-validation-net48.log 2>&1
"$sdk" build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > $work/strtree-validation-clipper-net48.log 2>&1
"$sdk" pack geometry3Sharp_netstandard.csproj -c Release -o $work/strtree-pilot-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > $work/strtree-validation-pack.log 2>&1
"$sdk" pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o $work/strtree-pilot-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > $work/strtree-validation-clipper-pack.log 2>&1
G3_ORACLE_POINT_INDEX=strtree "$sdk" test tests/geometry3Sharp.Tests -c Release -f net10.0 --artifacts-path "$artifacts" --filter 'FullyQualifiedName~Meshing2Oracle|FullyQualifiedName~Meshing2Golden|FullyQualifiedName~QualityMesh2Oracle' --logger 'trx;LogFileName=strtree-oracle-tests.trx' --results-directory $work/strtree-test-results > $work/strtree-validation-tests.log 2>&1
"$sdk" run --project benchmarks/OracleIndexPilot -c Release --artifacts-path "$artifacts" -- selftest > $work/strtree-validation-selftest.log 2>&1
python3 -m unittest discover -s benchmarks/OracleIndexPilot -p 'test_*.py' -v > $work/strtree-validation-python.log 2>&1
