#!/bin/bash
# Open-class re-measure after #528 (open operands classified per piece; owner/coordinator 2026-11-10): a stratified
# sample of 300 open models instead of the whole class (1474 models, about 30 h), the full class only if the sample
# shows a problem.
#
#   bash runs/final/open_remeasure.sh <rest rows dir> <work dir>
#        <rest rows dir>: the per-model rows of FINAL-1's rest phase (job.sh, master 79a2c1ab, before #528), eg
#        ~/b2final/out/rest
#
# 1. the sample (runs/final/open_sample.py): every open model with an operation slower than 5 s, not clean ("" or
#    "empty") or with an error in the rest phase, filled to 300 from the other open models stratified by their slowest
#    operation (5 strata, seeded); and from it the timing sample (sample_timing.py, 8 per stratum)
# 2. correctness: job2.sh correct on 293d0e3b (#528), 4 models at a time, 1 core each
# 3. timing: job2.sh timing of the timing sample, pre (79a2c1ab) and fix (293d0e3b) interleaved, nothing else running
# 4. the open-class line: python3 runs/final/open_compare.py <rest rows dir> <work dir> > <work dir>/open_line.md
# Resumable (rerun the same command). Then commit open_line.md, the sample files and <work dir>/out/correct_fix.jsonl
# and <work dir>/out/timing.jsonl to runs/final/results/open_remeasure/ on runs/boolean2-final.
set -euo pipefail
REST=${1:?rest rows dir}
W=${2:?work dir}
ROOT=$(git rev-parse --show-toplevel)
J=$ROOT/runs/final
PRE=79a2c1ab15d3ce9e419b2a994af343e32877881c
FIX=293d0e3b2af5e7c8aacfa31129bbf6cb2600a729
mkdir -p "$W"
[ -f "$W/open_sample.txt" ] || python3 "$J/open_sample.py" "$REST" 300 > "$W/open_sample.txt"
cut -d' ' -f1 "$W/open_sample.txt" > "$W/open_ids.txt"
echo "sample: $(wc -l < "$W/open_ids.txt") open models ($(grep -c ' must$' "$W/open_sample.txt") slow or not clean in the rest phase)"
IDS_FILE="$W/open_ids.txt" bash "$J/job2.sh" correct "$W" "fix:$FIX" open
if [ ! -f "$W/open_timing.txt" ]; then
  ls "$REST"/*.jsonl | grep -F -f <(sed 's|^|/|; s|$|.jsonl|' "$W/open_ids.txt") | xargs cat > "$W/rest_sample.jsonl"
  python3 "$J/sample_timing.py" "$W/rest_sample.jsonl" --per-stratum 8 > "$W/open_timing.txt"
fi
bash "$J/job2.sh" timing "$W" "$W/open_timing.txt" "pre:$PRE" "fix:$FIX"
python3 "$J/open_compare.py" "$REST" "$W" > "$W/open_line.md"
cat "$W/open_line.md"
