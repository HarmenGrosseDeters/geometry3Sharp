import json,sys
K=['scales','positions','tree','full','incr','degen','passes','tiny']
for f in sys.argv[1:]:
    rows=[json.loads(l) for l in open(f)]
    T=sum(r['total'] for r in rows); B=sum(r['boolean_ms'] for r in rows)
    print(f,len(rows),'ops; boolean',round(B),'ms; rounding (Apply)',round(T),'ms =',round(100*T/B,1),'% of boolean')
    for k in K:
        v=sum(r[k] for r in rows); print(f'  {k:10s} {v:9.0f} ms  {100*v/T:5.1f} % of rounding  {100*v/B:5.1f} % of boolean')
    rest=T-sum(sum(r[k] for k in K) for r in rows); print(f'  rest       {rest:9.0f} ms  {100*rest/T:5.1f} %')
    near=sum(r['near'] for r in rows); print(f'  (near inside passes {near:.0f} ms {100*near/T:.1f} % of rounding; calls {sum(r["near_calls"] for r in rows)}, hits/call {sum(r["near_hits"] for r in rows)/max(1,sum(r["near_calls"] for r in rows)):.1f})')
    bc=sum(r['body_cpu'] for r in rows); tc=sum(r['test_cpu'] for r in rows)
    print(f'  full search: cpu {bc:.0f} ms, of it triangle tests {tc:.0f} ms ({100*tc/max(bc,1):.0f} %), enumeration+rest {bc-tc:.0f} ms; candidates {sum(r["cand"] for r in rows)}, tests {sum(r["tests"] for r in rows)}, triangles {sum(r["tris"] for r in rows)}, skipped exact {sum(r["skipped"] for r in rows)}, searches {sum(r["full_n"] for r in rows)}')
    med=sorted(100*r['total']/r['boolean_ms'] for r in rows)[len(rows)//2]; print('  median per-op rounding share',round(med,1),'%')
