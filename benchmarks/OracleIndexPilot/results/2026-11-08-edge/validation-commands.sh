#!/bin/bash
set -e
work=${G3_PILOT_WORK:-$(mktemp -d)}
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# Run from the repository root.
sdk=${G3_PILOT_DOTNET:-dotnet}
artifacts=${G3_PILOT_ARTIFACTS:-$work/g3-edge-validation-artifacts}
"$sdk" build geometry3Sharp_netstandard.csproj -c Release --artifacts-path "$artifacts" > $work/strtree-edge-validation-core-build.log 2>&1
"$sdk" build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > $work/strtree-edge-validation-net48.log 2>&1
"$sdk" build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > $work/strtree-edge-validation-clipper-net48.log 2>&1
"$sdk" pack geometry3Sharp_netstandard.csproj -c Release -o $work/g3-edge-validation-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > $work/strtree-edge-validation-pack.log 2>&1
"$sdk" pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o $work/g3-edge-validation-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > $work/strtree-edge-validation-clipper-pack.log 2>&1
G3_ORACLE_POINT_INDEX=strtree G3_ORACLE_EDGE_INDEX=strtree-hulls "$sdk" test tests/geometry3Sharp.Tests -c Release -f net10.0 --artifacts-path "$artifacts" --filter 'FullyQualifiedName~Meshing2OracleTests|FullyQualifiedName~Meshing2OracleModesTests|FullyQualifiedName~Meshing2OracleBoxPairsTests|FullyQualifiedName~Meshing2GoldenTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=strtree-edge-oracle-tests.trx' --results-directory $work/strtree-edge-test-results > $work/strtree-edge-validation-tests.log 2>&1
"$sdk" run --project benchmarks/OracleIndexPilot -c Release --artifacts-path "$artifacts" -- selftest > $work/strtree-edge-validation-selftest.log 2>&1
python3 -m unittest discover -s benchmarks/OracleIndexPilot -p 'test_*.py' -v > $work/strtree-edge-validation-python.log 2>&1
