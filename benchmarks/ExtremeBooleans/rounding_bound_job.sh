#!/bin/bash
# Upper bound of the rounding-search levers (docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 9): per case one process with
# DOTNET_TieredPGO=0 (issue #825) and G3_ROUND_PROBE=1 on a build with probe_rounding_search.py applied,
# `profile --repeat 5 --median --safe-rounding --ops union`; one JSON line per case with the profile row and the
# ROUND_PROBE lines of the five timed runs (the warm-up booleans' lines dropped). Resumable.
#   rounding_bound_job.sh <instrumented tool dir> <out.jsonl> <case dir>...
T=$1; O=$2; shift 2; touch "$O"; tmp=$(mktemp -d)
for c in "$@"; do grep -q "\"case\": \"$c\"" "$O" && continue
  DOTNET_TieredPGO=0 G3_ROUND_PROBE=1 dotnet "$T/BooleanComparison.dll" profile --repeat 5 --median --safe-rounding --ops union "$c" > "$tmp/out" 2> "$tmp/err"
  python3 - "$c" "$tmp" >> "$O" <<'PY'
import json, sys
c, tmp = sys.argv[1], sys.argv[2]
lines = [l for l in open(tmp + "/out") if l.startswith("{")]
d = json.loads(lines[-1]) if lines else {"error": "no row"}
probes = [json.loads(l.split(" ", 1)[1]) for l in open(tmp + "/err") if l.startswith("ROUND_PROBE")]
d["case"] = c; d["round_probe"] = probes[-5:]
print(json.dumps(d))
PY
done
rm -rf "$tmp"
