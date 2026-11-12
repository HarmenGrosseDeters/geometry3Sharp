# tail800.py <rows.jsonl> [f800_baseline.jsonl]: budget pass counts, the >20 s tail with phase split, parallel share, Amdahl ceilings
import json, sys, statistics as st
F = 1.136   # this container vs the f800 machine
rows = [json.loads(l) for l in open(sys.argv[1])]
ok = [r for r in rows if 'pipe_ms' in r]
base = {}
if len(sys.argv) > 2:
    for l in open(sys.argv[2]):
        d = json.loads(l); base[d['file']] = d
def budget(r): return max(5000, 0.05 * r['raw_tris'])
def ph(r, k): return r['pipe_phases'].get(k, 0)
within = sum(1 for r in ok if r['pipe_ms'] / F <= budget(r))
within_raw = sum(1 for r in ok if r['pipe_ms'] <= budget(r))
print(f"rows {len(rows)}, results {len(ok)}; statuses {sorted(set(r.get('status') for r in rows))}")
print(f"within budget: {within}/{len(ok)} ({100*within/len(ok):.1f} %) at f800-machine-equivalent times (T/{F}); {within_raw} at raw container times")
if base:
    bw = sum(1 for r in ok if r['file'] in base and base[r['file']]['pipe_ms'] <= budget(base[r['file']]))
    same = sum(1 for r in ok if r['file'] in base and base[r['file']].get('after_hash') == r.get('after_hash'))
    sb = sum(base[r['file']]['pipe_ms'] for r in ok if r['file'] in base); sc = sum(r['pipe_ms'] for r in ok if r['file'] in base) / F
    print(f"f800 (2.0 final, its machine): within {bw}; output identical to f800 on {same}; time sum f800 {sb/1000:.0f} s vs now {sc/1000:.0f} s (f800-equivalent, ratio {sc/sb:.3f})")
upt = sorted(r['pipe_ms'] / r['raw_tris'] * 1000 for r in ok)   # us per raw triangle
med = st.median(upt)
print(f"median {med:.1f} us per raw triangle (pipeline); median pipeline {st.median(r['pipe_ms'] for r in ok)/1000:.1f} s, max {max(r['pipe_ms'] for r in ok)/1000:.1f} s")
tail = sorted([r for r in ok if r['pipe_ms'] > 20000], key=lambda r: -r['pipe_ms'])
print(f"\n{len(tail)} meshes above 20 s (container time); columns: in tris, components, raw / final tris, pipeline s, us/raw tri (x median), wrap %, of which carve-loop oracle+touch / insert / pinch trials %, reduce %, certify %, parallel share %, Amdahl 4->16 cores (current parallel parts), all-perfect ceiling 4 / 16 cores")
print("| mesh | in tris | comp | raw | final | T s | us/raw (x med) | wrap % | o+t % | ins % | trial % | reduce % | cert % | par % | 16c now | 4c ideal | 16c ideal |")
print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
for r in tail:
    T = r['pipe_ms']; wrap = ph(r, 'wrap') + ph(r, 'wrap.pinches') + ph(r, 'wrap.quality') + ph(r, 'wrap.pinches2') + ph(r, 'wrap.setup') + ph(r, 'wrap.manifold') + ph(r, 'wrap.extract')
    ot = sum(v for k, v in r['pipe_phases'].items() if k.endswith('/intersection') or k.endswith('/touch'))
    ins = sum(v for k, v in r['pipe_phases'].items() if k.endswith('/insert'))
    trial = sum(v for k, v in r['pipe_phases'].items() if k.endswith('/trial')) + ph(r, 'wrap.manifold')
    red = ph(r, 'simplify.reduce'); cert = ph(r, 'simplify.certify')
    # parallel now (4 cores): reduce rounds (slab regions) and certify; the rest is serial
    par = ph(r, 'simplify.reduce/rounds') + cert
    ser = T - par
    now16 = ser + par * 4 / 16
    u = T / r['raw_tris'] * 1000
    print(f"| {r['file']} | {r['before_tris']} | {r.get('before_components')} | {r['raw_tris']} | {r['after_tris']} | {T/1000:.1f} | {u:.0f} ({u/med:.2f}) | {100*wrap/T:.0f} | {100*ot/T:.0f} | {100*ins/T:.0f} | {100*trial/T:.0f} | {100*red/T:.0f} | {100*cert/T:.0f} | {100*par/T:.0f} | {now16/1000:.1f} | {(ser/4+par)/1000:.1f} | {(ser/16+par/4)/1000:.1f} |")
