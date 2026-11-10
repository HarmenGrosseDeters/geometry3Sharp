#!/bin/bash
# Exact Boolean 3.0 pre-study, topic 3: CSG trees chained vs one pass.
# usage: bash csg_job.sh <work dir> <tool dir>   (Thingi10K raw meshes in <work dir>/raw, fetch_thingi.py)
set -uo pipefail
W=$(cd "$1" && pwd); T=$(cd "$2" && pwd); O=$W/csg.jsonl
touch "$O"
run() { key="$*"; grep -q "\"key\":\"$key\"" "$O" && return
  timeout 1800 dotnet "$T/BooleanComparison.dll" "$@" 2> /dev/null | sed "s|^{|{\"key\":\"$key\",|" >> "$O"; }
for kind in boxes mixed; do for n in 4 8 16 32 64; do for seed in 1 2 3; do run prestudy-csg $kind $n $seed; done; done; done
for t in "37964 39025 42372" "57332 59757 78251" "79194 80761 89895" "91023 95433 98668" "105859 107389 110044" "116870 118287 178340"; do
  set -- $t; run prestudy-csg-thingi $(ls "$W"/raw/$1.* | head -1) $(ls "$W"/raw/$2.* | head -1) $(ls "$W"/raw/$3.* | head -1)
done
echo done
