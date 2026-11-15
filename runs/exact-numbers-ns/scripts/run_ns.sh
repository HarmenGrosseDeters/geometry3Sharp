#!/bin/bash
# netstandard2.0 probe (throwaway): the library's netstandard2.0 target under a .NET 10 host, one thread only.
#   base     EXACT_PROTO unset   the section 2 limb kernels (ToByteArray load), no carried limbs
#   carcen   EXACT_PROTO=carried,centroid   the carried limbs and the limb centroid (section 7's probe)
# strata: tail 107554 and 42327 (rotated copy, union/intersection/difference) and the normal 20 pairs; one thread
# (taskset core 0, serial loops, no concurrent GC); DOTNET_TieredPGO=0; 5 rounds, the order rotated
S=/tmp/claude-0/-home-user-geometry3Sharp/e8d0715a-b248-5ce2-943c-5f0c0f935a8b/scratchpad
O=$S/results/ns; mkdir -p $O
BIN=$S/probe/bin/Release/net10.0/BooleanComparison.dll
run() { # setting name casefile round
  f=$O/one_$2_$1_$4.jsonl; [ -s $f ] && return; rm -f $f
  case $1 in base) proto="";; carcen) proto="carried,centroid";; esac
  EXACT_PROTO=$proto EXACT_SERIAL=1 EXACT_TIMERS=0 DOTNET_TieredPGO=0 DOTNET_gcConcurrent=0 taskset -c 0 dotnet $BIN list $3 $f > $O/log_$2_$1_$4.txt 2>&1
}
for r in 1 2 3 4 5; do
  if [ $((r % 2)) -eq 1 ]; then orders="base carcen"; else orders="carcen base"; fi
  for s in $orders; do
    run $s t107554 $S/data/cases_b_107554.txt $r
    run $s t42327 $S/data/cases_d_42327.txt $r
    run $s npairs $S/data/cases_normal_pairs.txt $r
  done
  echo "round $r done $(date -u +%H:%M)"
done
echo ns_probe finished
