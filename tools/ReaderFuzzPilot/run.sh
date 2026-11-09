#!/usr/bin/env bash
# Fresh, isolated instrumentation and a bounded libFuzzer run.
set -euo pipefail
if [ "$#" -lt 4 ] || [ "$#" -gt 5 ]; then
    echo 'usage: run.sh <new-work-directory> <libfuzzer-dotnet> <sharpfuzz> <dotnet> [runs]' >&2
    exit 2
fi
work=$(realpath -m "$1")
fuzzer=$(realpath "$2")
instrumenter=$(realpath "$3")
dotnet=$(realpath "$4")
runs=${5:-1000}
[[ "$runs" =~ ^[1-9][0-9]*$ ]] || { echo 'runs must be a positive integer' >&2; exit 2; }
if [ -e "$work" ]; then echo "Work directory already exists: $work" >&2; exit 2; fi
mkdir -p "$work" "$work/artifacts"
here=$(cd "$(dirname "$0")" && pwd)
export DOTNET_ROOT="$(dirname "$dotnet")"
export DOTNET_ROLL_FORWARD=Major
export PATH="$DOTNET_ROOT:$PATH"
"$dotnet" publish "$here/ReaderFuzzPilot.csproj" -c Release -o "$work/replay" -m:1 -nr:false > "$work/build.log" 2>&1
python3 "$here/generate_seed.py" "$work/corpus"
"$dotnet" "$work/replay/ReaderFuzzPilot.dll" verify-seed "$work/corpus/triangle.stl"
cp -a "$work/replay" "$work/bin"
"$instrumenter" "$work/bin/geometry3Sharp.dll"
"$fuzzer" -seed=20261108 "-runs=$runs" -max_len=4096 -timeout=5 -rss_limit_mb=2048 \
    "-artifact_prefix=$work/artifacts/" "--target_path=$dotnet" \
    "--target_arg=$work/bin/ReaderFuzzPilot.dll" "$work/corpus" 2>&1 | tee "$work/fuzz.log"
