#!/usr/bin/env bash
# Runs one Stryker config in the mode select_shards.py chose (.github/workflows/mutation.yml, README.md):
#   full    the whole config, with its break threshold
#   source  --with-baseline: the mutants of the changed code run, the others take their result from the baseline
#           report in ./baseline (restored from the Actions cache), with the break threshold; without a baseline
#           the whole config runs, as in full
#   tests   --since: the mutants covered by the changed tests, report only
# Usage (in tests/mutation): MODE=... BASE=<base sha> PR=<number> bash run_stryker.sh <config file>
set -euo pipefail
config="$1"
args=()
case "${MODE:-full}" in
  tests)
    args=("--since:$BASE" --break-at 0) ;;
  source)
    if [ -f baseline/stryker-report.json ]; then
      # Stryker's disk provider looks for StrykerOutput/baseline/<version>/stryker-report.json; with no baseline for
      # the --version given it falls back to the --with-baseline target, the base commit
      mkdir -p "StrykerOutput/baseline/$BASE"
      cp baseline/stryker-report.json "StrykerOutput/baseline/$BASE/stryker-report.json"
      args=("--with-baseline:$BASE" "--version:pr-${PR:-0}")
      echo "baseline: $(cat baseline/source.txt 2>/dev/null || echo unknown)"
    else
      echo "::notice::no baseline in the cache for $config: running it in full"
    fi ;;
  full) ;;
  *) echo "unknown mode $MODE" >&2; exit 2 ;;
esac
echo "dotnet stryker --config-file $config ${args[*]:-}"
dotnet stryker --config-file "$config" "${args[@]}"
