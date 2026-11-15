#!/bin/bash
# Candidate C, the route without private fields (section 7): one binary, three settings
#   base     the library as it is (master 1f5e5e9)
#   carried  EXACT_PROTO=carried            the planes' and the centres' limbs converted once and carried; the kernels read them
#   carcen   EXACT_PROTO=carried,centroid   and the unreduced centroid on stack limbs (its limbs carried without a conversion)
# one thread (core 0, no concurrent GC) and 4 cores; DOTNET_TieredPGO=0 everywhere (#825); 5 rounds, settings rotated
S=/tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum
O=$S/results/c7; mkdir -p $O
run() { # mode setting cases name round
  f=$O/$1_$4_$2_$5.jsonl; [ -s $f ] && return; rm -f $f
  case $2 in base) proto="";; carried) proto="carried";; carcen) proto="carried,centroid";; esac
  if [ $1 = one ]; then
    EXACT_PROTO=$proto EXACT_SERIAL=1 EXACT_TIMERS=0 DOTNET_TieredPGO=0 DOTNET_gcConcurrent=0 taskset -c 0 dotnet $S/bin/c7/BooleanComparison.dll list $3 $f > /dev/null 2>&1
  else
    EXACT_PROTO=$proto EXACT_TIMERS=0 DOTNET_TieredPGO=0 dotnet $S/bin/c7/BooleanComparison.dll list $3 $f > /dev/null 2>&1
  fi
}
orders=("base carried carcen" "carcen carried base" "carried base carcen" "carcen base carried" "base carcen carried")
for r in 1 2 3 4 5; do
  for s in ${orders[$((r-1))]}; do
    run one $s $S/data/cases_b_107554.txt t107554 $r
    run one $s $S/data/cases_d_42327.txt t42327 $r
    run one $s $S/data/cases_normal_pairs.txt npairs $r
    run one $s $S/data/cases_normal_copy.txt ncopy $r
    run mt $s $S/data/cases_b_107554.txt t107554 $r
    run mt $s $S/data/cases_d_42327.txt t42327 $r
    run mt $s $S/data/cases_normal_pairs.txt npairs $r
    run mt $s $S/data/cases_normal_copy.txt ncopy $r
  done
  echo "round $r done $(date -u +%H:%M)"
done
echo c7_probe finished
