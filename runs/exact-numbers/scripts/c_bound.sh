#!/bin/bash
# Candidate C, classification part, upper bound on current master: one thread, core 0, no concurrent GC, no dynamic PGO;
# the instrumented library (math/CProbe.cs, local only) writes the centroid and operand-load timers at process exit.
S=/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
O=$S/results/c4; mkdir -p $O
run() { # name cases
  rm -f $O/$1.jsonl $O/$1.cprobe.jsonl
  CPROBE_OUT=$O/$1.cprobe.jsonl EXACT_SERIAL=1 EXACT_TIMERS=0 DOTNET_TieredPGO=0 DOTNET_gcConcurrent=0 \
    taskset -c 0 dotnet $S/bin/c_bound/BooleanComparison.dll list $2 $O/$1.jsonl > /dev/null 2>&1
  echo "$1 done"
}
run t107554 $S/data/cases_b_107554.txt
run t42327 $S/data/cases_d_42327.txt
run npairs $S/data/cases_normal_pairs.txt
run ncopy $S/data/cases_normal_copy.txt
echo c_bound finished
