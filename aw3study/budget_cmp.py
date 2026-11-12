import json, sys, statistics as st
def load(p): return {r['file']: r for r in (json.loads(l) for l in open(p) if l.strip())}
f800 = load('f800_aw3.jsonl'); base = load('base4_tail.jsonl'); b = load(sys.argv[1])
print('mesh | rel alpha (300 -> x) | raw tris | final tris | T s | T ratio | hd wrap->input upper, % diag (offset+alpha bound) | hd input->wrap upper % diag | min angle | p01 | status')
R = []
for f, r in sorted(b.items(), key=lambda kv: -base[kv[0]]['pipe_ms']):
    if 'pipe_ms' not in r: print(f, r.get('status')); continue
    o = f800[f]; a = r['wrap_rel_alpha']
    R.append((r['pipe_ms'] / base[f]['pipe_ms'], r['pipe_ms'], base[f]['pipe_ms'], r['raw_tris'], a))
    print(f"{f[:-4]} | {a:.0f} | {base[f]['raw_tris']} -> {r['raw_tris']} | {base[f]['after_tris']} -> {r['after_tris']} | {base[f]['pipe_ms']/1000:.1f} -> {r['pipe_ms']/1000:.1f} | {r['pipe_ms']/base[f]['pipe_ms']:.2f} | {100*o['hd_back_hi_rel']:.3f} -> {100*r['hd_back_hi_rel']:.3f} ({100*(1/300+1/3000):.3f} -> {100*(1/a+0.1/a):.3f}) | {100*o['hd_fwd_hi_rel']:.3f} -> {100*r['hd_fwd_hi_rel']:.3f} | {o['after_angle_min']:.1f} -> {r['after_angle_min']:.1f} | {o['after_angle_p01']:.1f} -> {r['after_angle_p01']:.1f} | {r['status']}")
print(f"n {len(R)}; time sum {sum(x[2] for x in R)/1000:.0f} -> {sum(x[1] for x in R)/1000:.0f} s ({sum(x[1] for x in R)/sum(x[2] for x in R):.2f}); max {max(x[2] for x in R)/1000:.1f} -> {max(x[1] for x in R)/1000:.1f} s; raw median {st.median(x[3] for x in R):.0f}; alpha rel min {min(x[4] for x in R):.0f}; us/raw median {st.median(x[1]*1000/x[3] for x in R):.1f}")
