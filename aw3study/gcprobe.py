import json, sys, statistics as st
def load(p): return {r['file']: r for r in (json.loads(l) for l in open(p) if l.strip())}
base = load(sys.argv[1])
print('variant | n | pipe ratio (sum) | median | wrap ratio | simplify ratio | gc pause s (base -> var) | same hash')
for p in sys.argv[2:]:
    v = load(p); fs = [f for f in v if f in base and 'pipe_ms' in v[f]]
    if not fs: continue
    s = lambda d, k: sum(k(d[f]) for f in fs)
    pm = lambda r: r['pipe_ms']; wm = lambda r: r['wrap_ms']; sm = lambda r: r['pipe_phases']['simplify']; gp = lambda r: r['study_gc_pause_ms']
    print(f"{p[:-6]} | {len(fs)} | {s(v,pm)/s(base,pm):.3f} | {st.median(v[f]['pipe_ms']/base[f]['pipe_ms'] for f in fs):.3f} | {s(v,wm)/s(base,wm):.3f} | {s(v,sm)/s(base,sm):.3f} | {s(base,gp)/1000:.1f} -> {s(v,gp)/1000:.1f} | {sum(v[f]['after_hash']==base[f]['after_hash'] for f in fs)}/{len(fs)}")
