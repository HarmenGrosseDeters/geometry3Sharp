#!/bin/sh
# The D1 probe of docs/design/SAFE_ROUNDING_D1_DRIFT.md section 5: the reference rule as a prototype (a patch of
# mesh_ops/MeshRoundingRepair.cs, switched on by G3_D1=ref; never merged as it is) against master's apex rule, on the
# 14 Thingi10K models with D1 operations and 119829 / 119830. The models are fetched at run time and never committed.
#   sh run.sh <work dir>        (from the repository root, at the commit the patch was made for: ca831be)
set -e
WORK=$1
ROOT=$(pwd)
mkdir -p "$WORK/thingi"
git worktree add --detach "$WORK/proto" HEAD
(cd "$WORK/proto" && git apply "$ROOT/benchmarks/BooleanComparison/perf/d1/reference_rule_prototype.patch" \
    && dotnet build benchmarks/BooleanComparison -c Release)
for id in 1012236 1053875 118037 129937 1396900 472043 548875 548878 551020 100344 1514904 472046 669979 686056 119829 119830; do
    [ -s "$WORK/thingi/$id.stl" ] || curl -sSL -o "$WORK/thingi/$id.stl" \
        "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl"
done
TOOL="$WORK/proto/benchmarks/BooleanComparison/bin/Release/net10.0/BooleanComparison.dll"
for f in "$WORK"/thingi/*.stl; do
    for rule in apex ref; do
        G3_D1=$rule G3_D1_HAUSDORFF=1 dotnet "$TOOL" d1 "$f" >> "$WORK/thingi.txt"
    done
done
python3 benchmarks/BooleanComparison/perf/d1/table.py "$WORK/thingi.txt"
# thingi_regressions_*.txt: the same loop for 103194 1036402 135793 366253 471990 837999 838003 958469 958470
# the procedural patches (dense_patch_*.txt): in the patched tree,
#   G3_D1=apex|ref G3_D1_OUT=<file> dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~D1ProbeTmp
# the test suites of the rounding under the prototype:
#   G3_D1=ref dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~Rounding|FullyQualifiedName~SafeRounding|FullyQualifiedName~ExactBooleanIssueCasesTests|FullyQualifiedName~ExactMeshBooleanTests"
