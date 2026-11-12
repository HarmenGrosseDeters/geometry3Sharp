# project.py : projected tail times per lever combination on 4 and 16 cores, from the measured phase split (base4_tail.jsonl),
# the measured size-budget run (budget300k_tail.jsonl) and the AW3 session's (B) round ceilings (runs/alpha-wrap-3 aw3/B: W 256,
# 3.94x on 4 cores, 14.9x on 16, re-evaluation +12 % (3-18 %)). M = measured input, E = model.
import json, statistics as st
def load(p): return {r['file']: r for r in (json.loads(l) for l in open(p) if l.strip())}
base, bud = load('base4_tail.jsonl'), load('budget300k_tail.jsonl')
BC = {4: 3.94, 16: 14.9}; RE = 1.12
def parts(r):
    ph = r['pipe_phases']; g = lambda k: ph.get(k, 0)
    d = {}
    d['carve'] = g('wrap')                                                   # main carving loop (oracle, touch, insert, bookkeeping)
    d['pinch_carve'] = sum(g(p + '/carve') for p in ('wrap.pinches', 'wrap.pinches2'))
    d['trials'] = sum(g(p + '/trial') for p in ('wrap', 'wrap.pinches', 'wrap.quality', 'wrap.pinches2'))
    d['quality'] = g('wrap.quality') - g('wrap.quality/trial')
    d['par'] = g('simplify.reduce/rounds') + g('simplify.check') + g('simplify.certify')      # parallel today (wall on 4 cores)
    d['rest'] = r['pipe_ms'] - sum(d.values())                               # setup, pinch-pass search, manifold, extract, reduce setup / stitch / last pass
    return d
def t(d, n, b=False, trials=1.0, restpar=False):
    par = d['par'] * 4 / n
    carve = d['carve'] * RE / BC[n] if b else d['carve']
    pc = d['pinch_carve'] * RE / BC[n] if (b and restpar) else d['pinch_carve']
    q = d['quality'] / n if restpar else d['quality']
    rest = d['rest'] / n if restpar else d['rest']
    tr = d['trials'] * trials / (n if restpar else 1)
    return (carve + pc + q + rest + tr + par) / 1000
scen = [
 ('today (master 1230482), M on 4 cores', lambda d, n, f: t(d, n)),
 ('(B) alone: main carve in rounds', lambda d, n, f: t(d, n, b=True)),
 ('(B) + trials halved (local trials only)', lambda d, n, f: t(d, n, b=True, trials=0.5)),
 ('(B) + trials free (bound)', lambda d, n, f: t(d, n, b=True, trials=0.0)),
 ('(B) + every rest phase perfectly parallel (floor)', lambda d, n, f: t(d, n, b=True, restpar=True)),
 ('everything perfectly parallel (Amdahl floor, no re-evaluation)', lambda d, n, f: (sum(d.values()) - d['par'] + d['par'] * 4) / n / 1000 if n == 4 else (sum(d.values()) - d['par'] + d['par'] * 4) / n / 1000),
 ('size budget raw <= 300k, M on 4 cores', lambda d, n, f: t(parts(bud[f]), n) if f in bud else t(d, n)),
 ('size budget + (B)', lambda d, n, f: t(parts(bud[f]), n, b=True) if f in bud else t(d, n, b=True)),
 ('size budget + (B) + trials halved', lambda d, n, f: t(parts(bud[f]), n, b=True, trials=0.5) if f in bud else t(d, n, b=True, trials=0.5)),
]
print('scenario | cores | max s | median s | sum s | meshes > 20 s | > 10 s | speedup (sum) | 40118 | 860049 | 69261')
T0 = sum(r['pipe_ms'] for r in base.values()) / 1000
for name, fn in scen:
    for n in (4, 16):
        v = {f: fn(parts(r), n, f) for f, r in base.items()}
        x = list(v.values())
        print(f"{name} | {n} | {max(x):.1f} | {st.median(x):.1f} | {sum(x):.0f} | {sum(a > 20 for a in x)} | {sum(a > 10 for a in x)} | {T0/sum(x):.2f}x | {v['40118.stl']:.1f} | {v['860049.stl']:.1f} | {v['69261.stl']:.1f}")
d = [parts(r) for r in base.values()]; T = sum(sum(x.values()) for x in d)
print('tail phase shares for the model: ' + ', '.join(f"{k} {100*sum(x[k] for x in d)/T:.1f}%" for k in d[0]))
