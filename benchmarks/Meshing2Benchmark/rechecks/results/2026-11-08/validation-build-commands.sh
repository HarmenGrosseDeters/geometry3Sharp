#!/bin/bash
set -e
export DOTNET_CLI_HOME=/workspace/dotnet-user NUGET_PACKAGES=/workspace/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /workspace/geometry3Sharp-meshing-qa
sdk=/workspace/dotnet/dotnet
artifacts=/workspace/meshing-qa-final-artifacts
"$sdk" build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > /tmp/meshing-qa-net48.log 2>&1
"$sdk" pack geometry3Sharp_netstandard.csproj -c Release -o /workspace/meshing-qa-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > /tmp/meshing-qa-pack.log 2>&1
"$sdk" build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > /tmp/meshing-qa-clipper-net48.log 2>&1
"$sdk" pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o /workspace/meshing-qa-packages -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > /tmp/meshing-qa-clipper-pack.log 2>&1
