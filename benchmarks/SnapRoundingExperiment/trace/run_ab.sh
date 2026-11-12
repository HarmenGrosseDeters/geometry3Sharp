#!/bin/bash
# A/B of the scratch switches of instrument.py on the rounding phase: master's behaviour (base), the separating-axis
# prefilter (sepaxis), and the prefilter with the passes stopped at the evaluation limit (sepaxis_stop); the three
# configurations in turn per case, <rounds> rounds, one JSON line per operation with the result hash.
#   run_ab.sh <tool dir of the instrumented build> <out.jsonl> <rounds> <case dir>...
tool=$1; out=$2; rounds=$3; shift 3
for round in $(seq 1 "$rounds"); do for dir in "$@"; do id=$(basename "$dir")
  for cfg in base sepaxis sepaxis_stop; do
    grep -q "\"cfg\": \"$cfg\", \"round\": $round, \"case\": \"$id\"" "$out" 2>/dev/null && continue
    sa=0; st=0; [ $cfg != base ] && sa=1; [ $cfg = sepaxis_stop ] && st=1
    G3_SEPAXIS=$sa G3_STOP_AT_LIMIT=$st dotnet "$tool/BooleanComparison.dll" profile --repeat 1 --safe-rounding "$dir" 2>/dev/null | python3 -c "
import sys, json
for l in sys.stdin:
    d = json.loads(l); p = d['phases_ms']
    print(json.dumps({'cfg': '$cfg', 'round': $round, 'case': '$id', 'op': d['op'], 'rounding_ms': p.get('rounding'),
                      'total_ms': sum(v for k, v in p.items() if '.' not in k), 'clean': d.get('rounding_clean'),
                      'limit': d.get('rounding_limit'), 'hash': d.get('hash')}))" >> "$out"
  done
done; done
