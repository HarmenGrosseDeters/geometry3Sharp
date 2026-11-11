#!/usr/bin/env bash
# Full local run of one or more mutation shards with the CI configuration, and the result file for the 3.0 completion gate
# (README, "Local runs"; local_result.py checks the file in CI).
#
#   bash tests/mutation/run_local.sh [--touched <base ref>] [shard ...]
#
# Shards are named as in the workflow (arithmetic, rounding-1, boolean-1, arrangement-3, ...); --touched <ref> takes the
# shards that a PR against <ref> would run (select_shards.py: the files changed between <ref> and HEAD). Needs: dotnet SDK
# (the version of the repository), python3, git, a CLEAN working tree at the commit to gate (a result is only valid for a
# commit), and several hours per shard. Stryker is the version of .config/dotnet-tools.json, the options are those of
# tests/mutation/stryker-*.json, as in the workflow (the heap limit of the mesh shards included). run_stryker.sh marks the library
# files the shard does not mutate as auto-generated while Stryker runs and restores them (and checks that, README "Coverage pass").
# Result: tests/mutation/local-results/<head sha>/<shard>.json (score, break threshold, survivors); commit it to the PR
# branch. The exit code is 1 when a shard is below its break threshold (the file is written anyway), 2 on an error.
set -uo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
shards=()
while [ $# -gt 0 ]; do
  case "$1" in
    --touched)
      [ $# -ge 2 ] || { echo "--touched needs a base ref" >&2; exit 2; }
      # (the mesh shards that need a run; "arithmetic" is the other config)
      out="$(git diff --name-only "$2...HEAD" | python3 tests/mutation/select_shards.py)" || exit 2
      arith="$(sed -n 's/^arithmetic=//p' <<< "$out")"
      [ -n "$arith" ] && [ "$arith" != none ] && shards+=(arithmetic)
      while read -r s; do [ -n "$s" ] && shards+=("$s"); done < <(sed -n 's/^mesh=//p' <<< "$out" | python3 -c 'import json,sys; [print(x["shard"]) for x in json.load(sys.stdin)]')
      shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) shards+=("$1"); shift ;;
  esac
done
[ ${#shards[@]} -gt 0 ] || { echo "no shard to run (name shards, or --touched <base ref>)" >&2; exit 2; }
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
  echo "the working tree is not clean: commit or stash first (a result is valid for a commit only)" >&2
  exit 2
fi
sha="$(git rev-parse HEAD)"
dotnet tool restore || exit 2
status=0
for shard in "${shards[@]}"; do
  if [ "$shard" = arithmetic ]; then config=stryker-config.json; else config="stryker-mesh-$shard.json"; fi
  [ -f "tests/mutation/$config" ] || { echo "unknown shard $shard" >&2; exit 2; }
  echo "=== $shard ($config) at $sha ==="
  rm -rf tests/mutation/StrykerOutput
  (
    cd tests/mutation
    if [ "$shard" != arithmetic ]; then export DOTNET_GCHeapHardLimit=0x180000000; fi     # 6 GB, as in the workflow
    export MODE=full G3_NO_EARLY_STOP=1
    unset NO_FULL
    bash run_stryker.sh "$config"
  )
  rc=$?
  report="$(ls tests/mutation/StrykerOutput/*/reports/mutation-report.json 2>/dev/null | sort | tail -1)"
  if [ -z "$report" ]; then echo "no report for $shard (Stryker exit code $rc)" >&2; status=2; continue; fi
  if [ "$(git rev-parse HEAD)" != "$sha" ] || [ -n "$(git status --porcelain --untracked-files=no)" ]; then
    echo "the working tree changed during the run of $shard: the result would not be valid" >&2; status=2; continue
  fi
  python3 tests/mutation/local_result.py record --root . --shard "$shard" --report "$report" \
      --out "tests/mutation/local-results/$sha/$shard.json" --platform "$(uname -sm)"
  r=$?
  [ $r -gt $status ] && status=$r
done
echo "results: tests/mutation/local-results/$sha/ (see tests/mutation/README.md, \"Local runs\", for how to hand them in)"
exit $status
