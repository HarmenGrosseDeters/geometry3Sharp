#!/bin/bash
# budget.sh <base.jsonl> <budget raw tris> <out.jsonl> : each mesh of the base rows whose raw wrap exceeds the budget, with
# alpha scaled so that the raw wrap would have about <budget> triangles (raw ~ 1/alpha^2), offset = alpha / 10; BENCH_FIDELITY=hd
B=/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark
python3 - "$1" "$2" <<'P' > /tmp/budget_list.txt
import json,sys,math
for l in open(sys.argv[1]):
    r=json.loads(l); raw=r['raw_tris']; bud=float(sys.argv[2])
    if raw>bud:
        a=300/math.sqrt(raw/bud); print(r['file'], '%.4f'%a, '%.4f'%(10*a))
P
while read f a o; do
  grep -q "\"file\":\"$f\"" "$3" 2>/dev/null && continue
  BENCH_REPAIR=wrappipe BENCH_FIDELITY=hd BENCH_WRAP_ALPHA=$a BENCH_WRAP_OFFSET=$o TIMEOUT=600 $B/run.sh $B/data/sample800_2611/meshes/$f >> "$3"
done < /tmp/budget_list.txt
echo DONE
