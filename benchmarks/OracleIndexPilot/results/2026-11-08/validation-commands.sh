#!/bin/bash
set -e
export DOTNET_CLI_HOME=/workspace/dotnet-user NUGET_PACKAGES=/workspace/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /workspace/geometry3Sharp-oracle-strtree
sdk=/workspace/dotnet/dotnet
artifacts=/workspace/strtree-final-validation-artifacts
"$sdk" build geometry3Sharp_netstandard.csproj -c Release --artifacts-path "$artifacts" > /tmp/strtree-validation-core-build.log 2>&1
"$sdk" build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > /tmp/strtree-validation-net48.log 2>&1
"$sdk" build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > /tmp/strtree-validation-clipper-net48.log 2>&1
"$sdk" pack geometry3Sharp_netstandard.csproj -c Release -o /workspace/strtree-pilot-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > /tmp/strtree-validation-pack.log 2>&1
"$sdk" pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o /workspace/strtree-pilot-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > /tmp/strtree-validation-clipper-pack.log 2>&1
G3_ORACLE_POINT_INDEX=strtree "$sdk" test tests/geometry3Sharp.Tests -c Release -f net10.0 --artifacts-path "$artifacts" --filter 'FullyQualifiedName~Meshing2Oracle|FullyQualifiedName~Meshing2Golden|FullyQualifiedName~QualityMesh2Oracle' --logger 'trx;LogFileName=strtree-oracle-tests.trx' --results-directory /tmp/strtree-test-results > /tmp/strtree-validation-tests.log 2>&1
"$sdk" run --project benchmarks/OracleIndexPilot -c Release --artifacts-path "$artifacts" -- selftest > /tmp/strtree-validation-selftest.log 2>&1
python3 -m unittest discover -s benchmarks/OracleIndexPilot -p 'test_*.py' -v > /tmp/strtree-validation-python.log 2>&1
