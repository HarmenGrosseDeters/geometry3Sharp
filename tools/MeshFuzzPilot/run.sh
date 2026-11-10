#!/usr/bin/env bash
# Fresh, isolated instrumentation and a bounded libFuzzer run of the mesh operations.
set -euo pipefail
if [ "$#" -lt 4 ] || [ "$#" -gt 7 ]; then
    echo 'usage: run.sh <new-work-directory> <libfuzzer-dotnet> <sharpfuzz> <dotnet> [runs] [max_total_time_s] [jobs]' >&2
    exit 2
fi
work=$(realpath -m "$1")
fuzzer=$(realpath "$2")
instrumenter=$(realpath "$3")
dotnet=$(realpath "$4")
runs=${5:-1000}
seconds=${6:-0}
jobs=${7:-0}
[[ "$runs" =~ ^[1-9][0-9]*$ ]] || { echo 'runs must be a positive integer' >&2; exit 2; }
[[ "$seconds" =~ ^[0-9]+$ ]] || { echo 'max_total_time_s must be a non-negative integer' >&2; exit 2; }
[[ "$jobs" =~ ^[0-9]+$ ]] || { echo 'jobs must be a non-negative integer' >&2; exit 2; }
# jobs > 0: libFuzzer's fork mode with that many workers, which goes on after a crash or timeout and keeps each artifact
fork=()
if [ "$jobs" -gt 0 ]; then fork=("-fork=$jobs" -ignore_crashes=1 -ignore_timeouts=1 -ignore_ooms=1); fi
if [ -e "$work" ]; then echo "Work directory already exists: $work" >&2; exit 2; fi
mkdir -p "$work" "$work/artifacts"
here=$(cd "$(dirname "$0")" && pwd)
export DOTNET_ROOT="$(dirname "$dotnet")"
export DOTNET_ROLL_FORWARD=Major
export PATH="$DOTNET_ROOT:$PATH"
"$dotnet" publish "$here/MeshFuzzPilot.csproj" -c Release -o "$work/replay" -m:1 -nr:false > "$work/build.log" 2>&1
"$dotnet" "$work/replay/MeshFuzzPilot.dll" self-test
"$dotnet" "$work/replay/MeshFuzzPilot.dll" write-seeds "$work/corpus"
cp -a "$work/replay" "$work/bin"
"$instrumenter" "$work/bin/geometry3Sharp.dll"
# the Clipper2 target: the adapter and the Clipper2 library it calls get coverage guidance too
for dll in geometry3Sharp.Clipper2.dll Clipper2Lib.dll; do
    if [ -f "$work/bin/$dll" ]; then "$instrumenter" "$work/bin/$dll"; fi
done
"$fuzzer" -seed=20261109 "-runs=$runs" "-max_total_time=$seconds" -max_len=512 -timeout=30 -rss_limit_mb=3072 ${fork[@]+"${fork[@]}"} \
    "-artifact_prefix=$work/artifacts/" "--target_path=$dotnet" \
    "--target_arg=$work/bin/MeshFuzzPilot.dll" "$work/corpus" 2>&1 | tee "$work/fuzz.log"
