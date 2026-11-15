#!/usr/bin/env bash
# The stage-3 growth measurement (runner job): certificate counts and times of MeshWindingNumberSolidify with the
# certificate off, on (stage 1), with blocks as they are (stage 2) and with grown blocks (stage 3), on the stage-1 set at
# / 64 and / 128, three repetitions.
#   bash benchmarks/ContouringCertificate/run_growth.sh <work dir>
# Output: <work dir>/growth_rep{1,2,3}.jsonl (one JSON line per input and resolution; a rerun skips finished lines) and
# the table of analyze_growth.py on stdout. Run alone on the machine (timings).
set -euo pipefail
W=${1:?work dir}
mkdir -p "$W/thingi"
IDS="96675 96773 64983 43551 1582399 57084 104640 94664 73088 1582404 37731 57714 116871 109459 472011 37111 113418 79961 1038438 66112"
python3 -I benchmarks/LevyClassification/fetch_thingi.py "$W/thingi" $IDS
FILES=""
for i in $IDS; do FILES="$FILES $(ls "$W/thingi/$i".* | head -1)"; done
dotnet build-server shutdown >/dev/null 2>&1 || true
dotnet build benchmarks/ContouringCertificate -c Release
for rep in 1 2 3; do
  dotnet run -c Release --no-build --project benchmarks/ContouringCertificate -- solid "$W/growth_rep$rep.jsonl" mesh:all $FILES -- 64,128
done
python3 benchmarks/ContouringCertificate/analyze_growth.py "$W"/growth_rep1.jsonl "$W"/growth_rep2.jsonl "$W"/growth_rep3.jsonl
