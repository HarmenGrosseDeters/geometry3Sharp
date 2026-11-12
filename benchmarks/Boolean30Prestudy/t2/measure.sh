#!/bin/bash
# usage: measure.sh <tool dir> <label>   -> $S/res_<label>.txt
S=${S:-/path/to/work/dir}
T=$1; L=$2; cd $S; : > res_$L.txt
for c in sph300 cross150; do
  for op in union intersection difference; do
    dotnet $T/BooleanComparison.dll prestudy-memory cases/$c $op | python3 -c "
import json,sys; r=json.load(sys.stdin); t=r['tris_a']+r['tris_b']
print('$L mem $c $op arrB/tri',round(r['arr_retained_mb']*2**20/t,1),'peakB/tri',round(r['peak_ws_mb']*2**20/t),'alloc_mb',r['alloc_mb'])" >> res_$L.txt
  done
  dotnet $T/BooleanComparison.dll profile --repeat 5 cases/$c | python3 -c "
import json,sys
for l in sys.stdin:
    r=json.loads(l); ms=sorted(r['ms']); print('$L time $c',r['op'],'median_ms',ms[len(ms)//2],'min',ms[0],'hash',r['hash'].split()[0],'det',r['deterministic'])" >> res_$L.txt
done
cat res_$L.txt
