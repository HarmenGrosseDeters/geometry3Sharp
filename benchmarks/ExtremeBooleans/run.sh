#!/bin/bash
# Profiles ExactMeshBoolean per case directory and operation, one process per operation, with its peak RSS.
#   run.sh <tool dir> <out.jsonl> <ops> <case dir>...       ops: union,intersection,difference (or a subset)
# One JSON line per operation: the `profile --repeat 1 --safe-rounding` line plus "peak_rss_mb" and "wall_s" from
# /usr/bin/time -v. The GC heap is capped (DOTNET_GCHeapHardLimit, default 13 GB; G3_HEAP_LIMIT to change) so that
# an out-of-memory case ends with an error line instead of taking the container down. Resumable: a case and
# operation already in <out.jsonl> is skipped. G3_TIMEOUT (default 1800 s) per operation. G3_EXTRA: more `profile`
# arguments (eg --antipodal-unlimited); G3_TAG: a suffix of the row's id for them (eg "+A").
tool=$1; out=$2; ops=$3; shift 3
limit=${G3_HEAP_LIMIT:-0x340000000}; to=${G3_TIMEOUT:-1800}
tmp=$(mktemp -d); touch "$out"
for dir in "$@"; do id=${dir#*cases/}$G3_TAG; for op in ${ops//,/ }; do
  grep -q "\"id\": \"$id\", \"op\": \"$op\"" "$out" && continue
  DOTNET_GCHeapHardLimit=$limit /usr/bin/time -v -o "$tmp/time" timeout "$to" dotnet "$tool/BooleanComparison.dll" \
    profile --repeat 1 --safe-rounding $G3_EXTRA --ops "$op" "$dir" > "$tmp/out" 2> "$tmp/err"; rc=$?
  python3 - "$id" "$op" "$rc" "$tmp" >> "$out" <<'PY'
import json, re, sys
id, op, rc, tmp = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
t = open(tmp + "/time").read()
rss = int(re.search(r"Maximum resident set size \(kbytes\): (\d+)", t).group(1)) / 1024
m = re.search(r"Elapsed \(wall clock\) time \(h:mm:ss or m:ss\): (\S+)", t).group(1).split(":")
wall = sum(float(x) * 60 ** i for i, x in enumerate(reversed(m)))
lines = [l for l in open(tmp + "/out").read().splitlines() if l.startswith("{")]
d = json.loads(lines[-1]) if lines else {}
if rc != 0 or not lines:
    err = open(tmp + "/err").read()
    d["error"] = "timeout" if rc == 124 else ("out of memory" if "OutOfMemory" in err else "exit %d" % rc)
    d["stderr_tail"] = err[-300:]
d = {"id": id, "op": op, "peak_rss_mb": round(rss), "wall_s": round(wall, 1), **d}
print(json.dumps(d))
PY
done; done
rm -rf "$tmp"
