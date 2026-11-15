#!/bin/bash
# Candidate C, classification part, the probe on current master (079e8258): one binary, three settings
#   base      the library as it is
#   fast      EXACT_PROTO=fastload            operands read from BigInteger's own limbs (no TryWriteBytes)
#   fastcen   EXACT_PROTO=fastload,centroid   and the unreduced centroid on stack limbs
# one thread (core 0, no concurrent GC) and 4 cores; DOTNET_TieredPGO=0 everywhere (#825); 3 rounds, settings rotated
S=/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
O=$S/results/c5; mkdir -p $O
run() { # mode setting cases name round
  f=$O/$1_$4_$2_$5.jsonl; [ -s $f ] && return; rm -f $f
  case $2 in base) proto="";; fast) proto="fastload";; fastcen) proto="fastload,centroid";; esac
  if [ $1 = one ]; then
    EXACT_PROTO=$proto EXACT_SERIAL=1 EXACT_TIMERS=0 DOTNET_TieredPGO=0 DOTNET_gcConcurrent=0 taskset -c 0 dotnet $S/bin/c_ab/BooleanComparison.dll list $3 $f > /dev/null 2>&1
  else
    EXACT_PROTO=$proto EXACT_TIMERS=0 DOTNET_TieredPGO=0 dotnet $S/bin/c_ab/BooleanComparison.dll list $3 $f > /dev/null 2>&1
  fi
}
orders=("base fast fastcen" "fastcen fast base" "fast base fastcen")
for r in 1 2 3; do
  for s in ${orders[$((r-1))]}; do
    run one $s $S/data/cases_b_107554.txt t107554 $r
    run one $s $S/data/cases_d_42327.txt t42327 $r
    run one $s $S/data/cases_normal_pairs.txt npairs $r
    run one $s $S/data/cases_normal_copy.txt ncopy $r
  done
  echo "one-thread round $r done"
done
for r in 1 2 3; do
  for s in ${orders[$((r-1))]}; do
    run mt $s $S/data/cases_b_107554.txt t107554 $r
    run mt $s $S/data/cases_d_42327.txt t42327 $r
    run mt $s $S/data/cases_normal_pairs.txt npairs $r
    run mt $s $S/data/cases_normal_copy.txt ncopy $r
  done
  echo "4-core round $r done"
done
echo c_probe finished
