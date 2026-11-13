#!/bin/bash
# the steps of .github/workflows/ci.yml (CLAUDE.md, "Before every push"), each with its exit code
cd "$1"
P=/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/ci/pkg_b; rm -rf $P; mkdir -p $P
step() { name="$1"; shift; out=$("$@" 2>&1); rc=$?; echo "[$rc] $name: $(echo "$out" | grep -E "Passed!|Failed!|error |Error\(s\)|Successfully created package|smoke|OK|ok" | tail -3 | cut -c1-200 | tr '\n' ' ')"; }
step "library tests" dotnet test tests/geometry3Sharp.Tests -c Release
step "g3cli tests" dotnet test tests/g3cli.Tests -c Release
step "pack (API validation against 1.0.324)" dotnet pack geometry3Sharp_netstandard.csproj -c Release -o $P -p:Version=1.0.0-ci.1
step "package smoke test" dotnet run --project tests/PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 -p:G3PackageSource=$P -- $P/geometry3Sharp.1.0.0-ci.1.nupkg
step "net48 build of the tests" dotnet build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true
step "Clipper2 tests" dotnet test tests/geometry3Sharp.Clipper2.Tests -c Release
step "Clipper2 pack" dotnet pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o $P -p:Version=1.0.0-ci.1
step "Clipper2 package smoke test" dotnet run --project tests/Clipper2PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 -p:G3PackageSource=$P -- $P/geometry3Sharp.Clipper2.1.0.0-ci.1.nupkg
step "Clipper2 net48 build" dotnet build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true
for b in benchmarks/*/; do n=$(basename $b); [ -f $b$n.csproj ] && step "benchmark $n" dotnet build $b$n.csproj -c Release; done
step "PredicateCompiler" dotnet build tools/PredicateCompiler -c Release
echo local_ci finished
