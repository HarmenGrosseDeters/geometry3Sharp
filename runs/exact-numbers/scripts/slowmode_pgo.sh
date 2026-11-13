#!/bin/bash
# one 42327 union per process, N processes per arm, alternating ABBA: default against DOTNET_TieredPGO=0 (base binary)
S=/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
N=${1:-40}; O=$S/results/d5; mkdir -p $O
run() { # arm index
  f=$O/$1_$2.jsonl; [ -s $f ] && return
  rm -f $f
  if [ $1 = nopgo ]; then DOTNET_TieredPGO=0 EXACT_TIMERS=0 dotnet $S/bin/t_base/BooleanComparison.dll list $S/data/cases_slow_one.txt $f > /dev/null 2>&1
  else EXACT_TIMERS=0 dotnet $S/bin/t_base/BooleanComparison.dll list $S/data/cases_slow_one.txt $f > /dev/null 2>&1; fi
}
for i in $(seq 1 $N); do
  if [ $((i % 2)) = 1 ]; then run dflt $i; run nopgo $i; else run nopgo $i; run dflt $i; fi
done
echo done
