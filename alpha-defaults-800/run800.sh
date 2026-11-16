#!/bin/bash
# Alpha-defaults 800 runner: configs A (defaults), B (BENCH_WRAP_FEATURES=1), C (BENCH_WRAP_BUDGET=300000),
# interleaved per mesh, one process at a time, wrappipe, diagonal/300, offset/3000, full fidelity, TIMEOUT=400.
# Resumable: skips (mesh, config) pairs already in raw/<cfg>.jsonl. Commits and pushes every 100 meshes.
set -u
REPO=/home/user/geometry3Sharp
WT=/home/user/runs-alpha-defaults-800
B=$REPO/benchmarks/MeshRepairBenchmark
OUT=$WT/alpha-defaults-800
mkdir -p $OUT/raw
n=0
commit_push() {
  cd $WT && git add alpha-defaults-800 && git commit -q -m "alpha-defaults-800: raw results, $1 of 800 meshes done

Claude-Session: https://claude.ai/code/session_01AaGT5CL4Wce4u9stN4b2Qt" && git push -q -u origin runs/alpha-defaults-800 && echo "pushed at $1"
}
while read -r id; do
  [ -z "$id" ] && continue
  f=$id.stl
  for cfg in A B C; do
    grep -q "\"file\":\"$f\"" $OUT/raw/$cfg.jsonl 2>/dev/null && continue
    case $cfg in
      A) extra= ;;
      B) extra="BENCH_WRAP_FEATURES=1" ;;
      C) extra="BENCH_WRAP_BUDGET=300000" ;;
    esac
    t0=$(date +%s.%N)
    env -u BENCH_WRAP_FEATURES -u BENCH_WRAP_BUDGET -u BENCH_FIDELITY $extra BENCH_REPAIR=wrappipe TIMEOUT=400 \
      $B/run.sh $B/data/sample800_2611/meshes/$f >> $OUT/raw/$cfg.jsonl
    t1=$(date +%s.%N)
    printf '%s\t%s\t%.2f\n' "$f" "$cfg" "$(echo "$t1 - $t0" | bc)" >> $OUT/wall.tsv
  done
  n=$((n+1))
  if [ $((n % 100)) -eq 0 ]; then commit_push $n; fi
done < $OUT/list800.txt
commit_push $n
echo DONE
