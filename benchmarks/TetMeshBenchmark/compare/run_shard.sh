#!/bin/bash
# One shard of the tet-mesher comparison (docs/design/TET_MESHING.md section 10.2; workflow .github/workflows/tet-compare.yml).
# Usage: run_shard.sh <shard> <shards> <evaluator dll> <fTetWild binary or ""> <mesh dir> <work dir> <out dir> [timeout_s=1800]
# For every id i of thingi-subset-ids.txt with (line index % shards) == shard, one at a time:
#   ours   our stage-0 path (alpha wrap 20 / 600 + inside cells), serial, timed in process
#   ft<N>  fTetWild with its defaults (-l 0.05 -e 1e-3 per its README), --max-threads N for N in $FT_THREADS (default "4 1"; the workflow passes "4"),
#          wall time from `date`; its output is read back and checked by our evaluator (exact orientation, facets, boundary)
# fTetWild is a black box (MPL-2.0): run only, never read; its output files stay in <work dir> and are never published.
# One CSV per variant in <out dir> (columns of TetFiles.Header, no header line); failure rows: <id>.stl,<time>,<threads>,<STATUS>.
# Resumable: ids already in a CSV are skipped.
set -u
SHARD=$1; SHARDS=$2; EVAL=$3; FT=$4; MESH=$5; WORK=$6; OUT=$7; TO=${8:-1800}
FT_THREADS=${FT_THREADS:-"4 1"}
IDS="$(dirname "$0")/thingi-subset-ids.txt"
mkdir -p "$MESH" "$WORK" "$OUT"
idx=0
for id in $(cat "$IDS"); do
  k=$idx; idx=$((idx + 1))
  [ $((k % SHARDS)) -eq "$SHARD" ] || continue
  f=$MESH/$id.stl
  [ -s "$f" ] || curl -sS -L --retry 4 -m 600 -o "$f" "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/$id.stl"
  if [ ! -s "$f" ]; then
    for v in ours $(for n in $FT_THREADS; do echo ft$n; done); do echo "$id.stl,,,MISSING_MESH" >> "$OUT/$v.csv"; done
    continue
  fi
  touch "$OUT/ours.csv"
  if ! grep -q "^$id.stl," "$OUT/ours.csv"; then
    line=$(timeout "$TO" dotnet "$EVAL" ours "$f" 20 600 2> "$WORK/ours_err_$id.txt" | tail -1); rc=${PIPESTATUS[0]}
    if [ "$rc" -eq 124 ]; then echo "$id.stl,$TO,1,TIMEOUT" >> "$OUT/ours.csv"
    elif [ "$rc" -ne 0 ] || [ -z "$line" ]; then echo "$id.stl,,1,ERROR_rc$rc" >> "$OUT/ours.csv"
    else echo "$line" >> "$OUT/ours.csv"; fi
  fi
  [ -n "$FT" ] || continue
  for n in $FT_THREADS; do
    csv=$OUT/ft$n.csv; touch "$csv"
    grep -q "^$id.stl," "$csv" && continue
    rm -f "$WORK"/out_${id}_$n*
    t0=$(date +%s.%N)
    timeout "$TO" "$FT" -i "$f" -o "$WORK/out_${id}_$n.mesh" --max-threads "$n" --no-binary --level 3 > "$WORK/log_${id}_$n.txt" 2>&1; rc=$?
    t1=$(date +%s.%N); secs=$(echo "$t1 - $t0" | bc -l)
    res=$(ls "$WORK"/out_${id}_$n*.mesh "$WORK"/out_${id}_$n*.msh 2>/dev/null | grep -v "_sf\." | head -1)
    if [ "$rc" -eq 124 ]; then echo "$id.stl,$secs,$n,TIMEOUT" >> "$csv"
    elif [ -z "$res" ]; then echo "$id.stl,$secs,$n,NO_OUTPUT_rc$rc" >> "$csv"
    else
      line=$(dotnet "$EVAL" eval "$res" "$f" "$id.stl" "$secs" "$n" 2> "$WORK/eval_err_${id}_$n.txt" | tail -1); erc=${PIPESTATUS[0]}
      if [ "$erc" -ne 0 ] || [ -z "$line" ]; then echo "$id.stl,$secs,$n,UNREADABLE_OUTPUT_rc$rc" >> "$csv"; head -c 300 "$res" > "$WORK/head_${id}_$n.txt"
      else echo "$line" >> "$csv"; fi
    fi
    rm -f "$WORK"/out_${id}_$n*                     # foreign output: never kept
  done
done
