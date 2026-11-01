#!/usr/bin/env bash
# Runs one Stryker config in the mode select_shards.py chose (.github/workflows/mutation.yml, README.md):
#   full    the whole config, with its break threshold
#   source  incremental (incremental.py): Stryker mutates only the lines changed against the baseline report in
#           $BASELINE_DIR (default ./baseline, restored from the Actions cache); the score of the shard combines
#           these results with the baseline's for the unchanged code, and the break threshold applies to it.
#           Without a usable baseline the whole config runs, as in full (and StrykerOutput/full-run marks that)
#   tests   --since: the mutants covered by the changed tests, report only
# Usage (in tests/mutation): MODE=... BASE=<base sha> [BASELINE_DIR=<dir>] bash run_stryker.sh <config file>
set -euo pipefail
config="$1"
here="$(cd "$(dirname "$0")" && pwd)"
baseline="${BASELINE_DIR:-baseline}/stryker-report.json"
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"

full() {
  mkdir -p StrykerOutput
  touch StrykerOutput/full-run
  echo "dotnet stryker --config-file $config"
  dotnet stryker --config-file "$config"
}

latest_report() {
  ls StrykerOutput/*/reports/mutation-report.json 2>/dev/null | sort | tail -1
}

case "${MODE:-full}" in
  tests)
    echo "dotnet stryker --config-file $config --since:$BASE --break-at 0"
    dotnet stryker --config-file "$config" "--since:$BASE" --break-at 0 ;;
  source)
    if [ ! -f "$baseline" ]; then
      echo "::notice::no baseline in the cache for $config: running it in full"
      full
      exit $?
    fi
    echo "baseline: $(cat "$(dirname "$baseline")/source.txt" 2>/dev/null || echo unknown)"
    out="$(python3 "$here/incremental.py" plan "$config" "$baseline" incremental-run.json)"
    echo "$out"
    plan="$(sed -n 's/^plan=//p' <<< "$out" | tail -1)"
    case "$plan" in
      run)
        echo "dotnet stryker --config-file incremental-run.json --break-at 0"
        dotnet stryker --config-file incremental-run.json --break-at 0
        report="$(latest_report)"
        if [ -z "$report" ]; then
          echo "::error::the incremental run wrote no report"
          exit 1
        fi
        python3 "$here/incremental.py" merge "$config" "$baseline" "$report" "$summary" ;;
      none)
        python3 "$here/incremental.py" merge "$config" "$baseline" - "$summary" ;;
      *)
        full ;;
    esac ;;
  full)
    full ;;
  *)
    echo "unknown mode $MODE" >&2
    exit 2 ;;
esac
