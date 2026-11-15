#!/bin/bash
# The inventory measurement of docs/perf/EXTREME_RUNTIME_BOOLEANS.md, in order of size. Resumable (run.sh skips done rows).
#   job.sh <tool dir> <work dir>      <work dir>/cases: fuzz/<id> (dump2off.py), rot/<id> (BooleanComparison export)
T=$1; W=$2; R=$(dirname "$0")/run.sh; O=$W/inventory.jsonl
cd "$W"
$R "$T" "$O" difference,union,intersection cases/fuzz/*
$R "$T" "$O" union,intersection,difference cases/rot/{67709,42327,1368057,102375,498458,486343,519511,1517923,1063860,668056,119829}
$R "$T" "$O" union,intersection,difference cases/rot/{124526,122796,107554,113906,1087141,117959,112798,1005586}
# the four siblings of 1087141 (the same part, 1196 boundary segments each): union only
$R "$T" "$O" union cases/rot/{1087144,1088051,1088054,1088138}
$R "$T" "$O" difference cases/rot/55928
$R "$T" "$O" onepass grid:10
# T5 option A (owner 2026-11-12): both Antipodal switches lifted, union of the open models
G3_EXTRA=--antipodal-unlimited G3_TAG=+A $R "$T" "$O" union cases/rot/{124526,122796,107554,113906,1087141,117959,112798,1005586}
echo done
