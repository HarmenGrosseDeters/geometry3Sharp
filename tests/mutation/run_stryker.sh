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

# what the runner has (a standard runner of a public repository should have 4 vCPU and 16 GB: the basis of the
# concurrency and heap limit of the configs)
echo "runner: $(nproc 2>/dev/null || echo '?') cpus, $(free -m 2>/dev/null | awk '/^Mem:/ {print $2}' || true) MB memory"

# dotnet stryker with a progress line every 10 minutes in the job log: Stryker's progress reporter prints nothing
# without a terminal, so the line counts the test runs started, one log file each in StrykerOutput/*/logs (written with
# --log-to-file), against the mutants to test from Stryker's own log. A shard that will not finish within the job's
# limit shows it before it is cancelled.
# Early stop: once the coverage pass is over (Stryker's count of mutants to test is known), the rate of test runs since
# then projects the total run time (time so far + the mutants left at that rate). When the projection is above the limit
# times G3_STOP_FACTOR (1.15) at two ticks in a row, and at least G3_MIN_RUNS (30) runs were started since, the run is
# stopped and the script ends with 3: a shard that will be cancelled at the job's limit anyway frees its runner hours
# earlier, and the log says why. G3_LIMIT_SECONDS is the job's limit (21000 s = 350 min), G3_NO_EARLY_STOP=1 switches it off.
# kills a process and all its descendants (Stryker's test hosts would otherwise run on after the stop)
kill_tree() {
  local c
  for c in $(pgrep -P "$1" 2>/dev/null); do kill_tree "$c"; done
  kill -KILL "$1" 2>/dev/null
  return 0
}

stryker() {
  local started=$SECONDS
  rm -f StrykerOutput/stopped-early
  dotnet stryker --log-to-file "$@" &
  local pid=$!
  # (the subshell must not inherit `set -e` / pipefail: `ls` of a log directory that does not exist yet would end it, and
  # no progress line would ever appear when Stryker writes its logs late; it also means a real bug in the ticker
  # shows as a missing progress line, not as a failed job, which is acceptable for a progress line)
  ( set +e +o pipefail
    limit="${G3_LIMIT_SECONDS:-21000}"; base_t=""; base_r=""; strikes=0
    while sleep "${PROGRESS_SECONDS:-600}" </dev/null >/dev/null 2>&1; do
      logs="$(ls -d StrykerOutput/*/logs 2>/dev/null | sort | tail -1 || true)"
      [ -n "$logs" ] || continue
      total="$(grep -ho '[0-9]\+ *total mutants will be tested' "$logs"/log-*.txt 2>/dev/null | tail -1 | grep -o '^[0-9]\+' || true)"
      runs="$(ls "$logs" 2>/dev/null | grep -c '^Runner ' || true)"
      line="[progress $(date -u +%H:%M) UTC] test runs started: $runs, mutants to test: ${total:-not yet known}"
      if [ -n "$total" ]; then
        now=$SECONDS
        if [ -z "$base_t" ]; then
          base_t=$now; base_r=$runs
        else
          proj="$(awk -v n="$now" -v s="$started" -v t="$base_t" -v r="$runs" -v r0="$base_r" -v tot="$total" -v min="${G3_MIN_RUNS:-30}" \
                  'BEGIN { d = r - r0; if (d < min || n <= t) { exit } rem = tot - r; if (rem < 0) rem = 0; printf "%d", (n - s) + rem / (d / (n - t)) }')"
          if [ -n "$proj" ]; then
            line="$line, projected total: $((proj / 60)) min (limit $((limit / 60)) min)"
            if awk -v p="$proj" -v l="$limit" -v f="${G3_STOP_FACTOR:-1.15}" 'BEGIN { exit !(p > l * f) }'; then
              strikes=$((strikes + 1))
            else
              strikes=0
            fi
          fi
        fi
      fi
      echo "$line"
      if [ "$strikes" -ge 2 ] && [ "${G3_NO_EARLY_STOP:-0}" != 1 ]; then
        msg="stopped early: $runs of $total mutants started after $(((SECONDS - started) / 60)) min, the projection of the total ($((proj / 60)) min) is over the limit of $((limit / 60)) min"
        echo "::error::$msg"
        echo "$msg" > StrykerOutput/stopped-early
        echo "### Mutation run stopped early" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"; echo "$msg" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
        kill_tree "$pid"
        break
      fi
    done ) &
  local ticker=$!
  local rc=0
  wait "$pid" || rc=$?
  if [ -f StrykerOutput/stopped-early ]; then
    wait "$ticker" 2>/dev/null
    return 3
  fi
  kill "$ticker" 2>/dev/null || true
  return $rc
}

full() {
  mkdir -p StrykerOutput
  touch StrykerOutput/full-run
  echo "dotnet stryker --config-file $config"
  stryker --config-file "$config"
}

latest_report() {
  ls StrykerOutput/*/reports/mutation-report.json 2>/dev/null | sort | tail -1
}

case "${MODE:-full}" in
  tests)
    echo "dotnet stryker --config-file $config --since:$BASE --break-at 0"
    stryker --config-file "$config" "--since:$BASE" --break-at 0 ;;
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
        stryker --config-file incremental-run.json --break-at 0
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
