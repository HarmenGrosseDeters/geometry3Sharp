#!/bin/bash
# Local all-target builds and API-validating packs of the validation run.
# Placeholders: $DOTNET (SDK, default dotnet), $ARTIFACTS (a separate --artifacts-path, so the
# binaries of a running recheck stay fixed), $PACKAGES (pack output) and $LOGS (log directory).
set -e
cd "$(dirname "$0")/../../../../.."
sdk=${DOTNET:-dotnet}
artifacts=${ARTIFACTS:?set ARTIFACTS}
packages=${PACKAGES:?set PACKAGES}
logs=${LOGS:?set LOGS}
"$sdk" build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > "$logs/meshing-qa-net48.log" 2>&1
"$sdk" pack geometry3Sharp_netstandard.csproj -c Release -o "$packages" -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > "$logs/meshing-qa-pack.log" 2>&1
"$sdk" build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true --artifacts-path "$artifacts" > "$logs/meshing-qa-clipper-net48.log" 2>&1
"$sdk" pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o "$packages" -p:Version=1.0.0-ci.1 --artifacts-path "$artifacts" > "$logs/meshing-qa-clipper-pack.log" 2>&1
