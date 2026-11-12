#!/bin/bash
# cmp.sh <list> <out>: CGAL 5.6 alpha_wrap_3 (black box) and MeshAlphaWrap's raw wrap, alternating per mesh, 300/3000, resumable
S=/tmp/claude-0/-home-user-geometry3Sharp/e5fcfb26-4af0-5292-83f0-5c74dadcbf20/scratchpad
B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark
while read f; do
  [ -z "$f" ] && continue
  grep -q "\"v\":\"wrap\",\"row\":{\"file\":\"$f\"" "$2" 2>/dev/null && continue
  for m in cgalwrap wrap; do
    o=$(CGAL_WRAP=$S/cgal/cgal_wrap BENCH_REPAIR=$m BENCH_WRAP_ALPHA=300 BENCH_WRAP_OFFSET=3000 BENCH_FIDELITY=hd TIMEOUT=1800 $B/run.sh $B/data/sample800_2611/meshes/$f)
    echo "{\"v\":\"$m\",\"row\":$o}" >> "$2"
  done
done < "$1"
echo DONE
