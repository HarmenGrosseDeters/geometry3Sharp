#!/usr/bin/env bash
# Verdict comparison of MeshRobustRepair.NotCleanReason between two commits (correctness only, no timing).
# Usage: benchmarks/NotCleanVerdict/compare.sh <before-commit> <after-commit> <mesh-list-file> <mesh-dir> [out-dir]
# Needs the .NET 10 SDK. Builds each commit in its own git worktree, runs the tool on every mesh of the list with both
# builds, writes before.tsv / after.tsv and diff.txt to out-dir, prints the number of meshes whose line differs.
set -euo pipefail
BEFORE=$1; AFTER=$2; LIST=$(realpath "$3"); MESHES=$(realpath "$4"); OUT=${5:-verdict-out}
REPO=$(git rev-parse --show-toplevel); TOOL=$REPO/benchmarks/NotCleanVerdict
mkdir -p "$OUT"; OUT=$(realpath "$OUT")
for side in before after; do
  commit=$([ $side = before ] && echo "$BEFORE" || echo "$AFTER")
  wt="$OUT/wt-$side"
  git -C "$REPO" worktree add --detach "$wt" "$commit" >/dev/null
  mkdir -p "$wt/benchmarks/NotCleanVerdict"
  cp "$TOOL/NotCleanVerdict.csproj" "$TOOL/Program.cs" "$wt/benchmarks/NotCleanVerdict/"
  dotnet build "$wt/benchmarks/NotCleanVerdict" -c Release -f net10.0 >"$OUT/build-$side.log" 2>&1 || { echo "build of $side failed, see $OUT/build-$side.log"; exit 1; }
  dotnet run --no-build -c Release --project "$wt/benchmarks/NotCleanVerdict" -- "$LIST" "$MESHES" >"$OUT/$side.tsv"
  git -C "$REPO" worktree remove --force "$wt"
done
diff "$OUT/before.tsv" "$OUT/after.tsv" >"$OUT/diff.txt" || true
echo "meshes: $(wc -l <"$OUT/before.tsv"), verdict lines that differ: $(grep -c '^<' "$OUT/diff.txt" || true)"
