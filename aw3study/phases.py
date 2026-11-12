# phases.py <base4.jsonl> <p1.jsonl> : per mesh wall, triangles, phase split, parallel share, Amdahl; and us per raw triangle per phase
import json, sys, statistics as st
def load(p): return {r['file']: r for r in (json.loads(l) for l in open(p) if l.strip())}
def split(r):
    ph = r['pipe_phases']; g = lambda k: ph.get(k, 0)
    P = ['wrap', 'wrap.pinches', 'wrap.quality', 'wrap.pinches2']
    o = {}
    o['oracle'] = sum(g(p + '/intersection') for p in P)
    o['touch'] = sum(g(p + '/touch') for p in P)
    o['insert'] = sum(g(p + '/insert') for p in P)
    o['carve_book'] = sum(g(p + '/carve') - g(p + '/intersection') - g(p + '/touch') - g(p + '/insert') for p in P)
    o['trials'] = sum(g(p + '/trial') for p in P)
    o['wrap_other'] = r['wrap_ms'] - sum(o.values())     # setup, quality point search, manifold, extract
    o['reduce_par'] = g('simplify.reduce/rounds')
    o['reduce_ser'] = g('simplify.reduce') - g('simplify.reduce/rounds')
    o['check'] = g('simplify.check')
    o['certify'] = g('simplify.certify')
    o['simp_other'] = g('simplify') - g('simplify.reduce') - g('simplify.check') - g('simplify.certify')
    return o
KEYS = ['oracle', 'touch', 'insert', 'carve_book', 'trials', 'wrap_other', 'reduce_par', 'reduce_ser', 'check', 'certify', 'simp_other']
if __name__ == '__main__':
    a, b = load(sys.argv[1]), load(sys.argv[2])
    rows = []
    print('mesh | T4 s | T1 s | in | raw | final | us/raw | ' + ' | '.join(KEYS) + ' (% of T4) | par share of T1 | cpu/wall | Amdahl today 4/16 | all-parallel floor 4/16 s | gc pause % | alloc GB')
    for f, r in sorted(a.items(), key=lambda kv: -kv[1]['pipe_ms']):
        s4, s1 = split(r), split(b[f]); T4, T1 = r['pipe_ms'], b[f]['pipe_ms']
        # parallel work = 1-core time of the phases that got faster on 4 cores (reduce rounds, check, certify)
        par1 = sum(s1[k] for k in ('reduce_par', 'check', 'certify')); ser1 = T1 - par1
        am = lambda n: T1 / (ser1 + par1 / n)
        rows.append((f, r, s4, s1, T4, T1, par1))
        print(f"{f[:-4]} | {T4/1000:.1f} | {T1/1000:.1f} | {r['before_tris']} | {r['raw_tris']} | {r['after_tris']} | {T4*1000/r['raw_tris']:.1f} | " + ' | '.join(f"{100*s4[k]/T4:.0f}" for k in KEYS)
              + f" | {100*par1/T1:.0f}% | {r['study_cpu_ms']/T4:.2f} | {am(4):.2f}x/{am(16):.2f}x | {T1/4000:.1f}/{T1/16000:.1f} | {100*r['study_gc_pause_ms']/T4:.1f} | {r['study_alloc_mb']/1024:.1f}")
    print('\nus per raw triangle, median over the set: ' + ' '.join(f"{k}={st.median(s4[k]*1000/r['raw_tris'] for f,r,s4,s1,T4,T1,p in rows):.2f}" for k in KEYS)
          + f" total={st.median(T4*1000/r['raw_tris'] for f,r,s4,s1,T4,T1,p in rows):.1f}")
    print('sum shares: ' + ' '.join(f"{k}={100*sum(s4[k] for f,r,s4,s1,T4,T1,p in rows)/sum(T4 for f,r,s4,s1,T4,T1,p in rows):.1f}%" for k in KEYS), 'sumT4 %.0f s' % (sum(x[4] for x in rows)/1000), 'sumT1 %.0f s' % (sum(x[5] for x in rows)/1000))
    print('1-core / 4-core per phase (sum): ' + ' '.join(f"{k}={sum(x[3][k] for x in rows)/max(1,sum(x[2][k] for x in rows)):.2f}" for k in KEYS))
