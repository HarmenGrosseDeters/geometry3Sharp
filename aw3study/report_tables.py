# report_tables.py : the markdown tables of the report (per mesh, zeroing bounds, categories)
import json, statistics as st, collections, re
from phases import split, KEYS
from cats import analyse
def load(p): return {r['file']: r for r in (json.loads(l) for l in open(p) if l.strip())}
f800 = load('f800_aw3.jsonl'); MED = st.median(r['pipe_ms'] * 1000 / r['raw_tris'] for r in f800.values() if r.get('raw_tris'))
EXACT_STAGE = re.compile(r'bigint|_int$|AdaptivePredicates|Expansion')
out = []
for s in ('tail', 'normal'):
    base = load(f'base4_{s}.jsonl'); perf = {r['name'] + '.stl': r for r in analyse(f'perf_{s}')}
    out.append(f'\n### Per mesh, {s} ({len(base)} meshes)\n')
    out.append('| mesh | T s | in tris | raw | final | us/raw (x 800 median) | oracle | touch | insert | gate bookkeeping | pinch / manifold trials | wrap other | reduce parallel | reduce serial | check | certify | serial share of wall | parallel work share of CPU | Amdahl today 4c / 16c (vs 1 core) | T on 16c, today\'s structure | all-parallel floor 4c / 16c s |')
    out.append('|---|' + '---:|' * 20)
    agg = collections.Counter(); catagg = collections.Counter(); subagg = collections.Counter(); tot = 0
    for f, r in sorted(base.items(), key=lambda kv: -kv[1]['pipe_ms']):
        sp = split(r); T = r['pipe_ms']; p = perf[f]; S, P = p['S'], p['P']; wall = p['row']['pipe_ms'] / 1000
        scale = (T / 1000) / wall                                     # the perf run's wall against the timing run's
        for k in KEYS: agg[k] += sp[k]
        agg['T'] += T
        out.append(f"| {f[:-4]} | {T/1000:.1f} | {r['before_tris']} | {r['raw_tris']} | {r['after_tris']} | {T*1000/r['raw_tris']:.1f} ({T*1000/r['raw_tris']/MED:.2f}) | " + ' | '.join(f"{100*sp[k]/T:.0f}" for k in KEYS[:-1])
                   + f" | {100*S/wall:.0f} % | {100*P/(S+P):.0f} % | {(S+P)/(S+P/4):.2f}x / {(S+P)/(S+P/16):.2f}x | {(S+P/16)*scale:.1f} | {(S+P)/4*scale:.1f} / {(S+P)/16*scale:.1f} |")
        for (c, sname), n in p['sub'].items():
            subagg[(c, sname)] += n; tot += n
    out.append(f"\nPhase shares of the summed time ({agg['T']/1000:.0f} s): " + ', '.join(f"{k} {100*agg[k]/agg['T']:.1f} %" for k in KEYS[:-1]))
    # zeroing bounds
    out.append(f'\n### Upper bounds by zeroing a phase, {s} (M: phase time from the timing keys set to 0; speedup of the summed time / median speedup per mesh)\n')
    out.append('| phase set to zero | speedup (sum) | median per mesh | max T after s |'); out.append('|---|---:|---:|---:|')
    sets = [('oracle + touch (L1 remainder)', ['oracle', 'touch']), ('Delaunay insertion', ['insert']), ('gate bookkeeping (pop, recheck, dual edge, labels, pushes)', ['carve_book']),
            ('pinch / manifold trials', ['trials']), ('whole main carving (oracle, touch, insert, bookkeeping)', ['oracle', 'touch', 'insert', 'carve_book']),
            ('reduction, parallel rounds', ['reduce_par']), ('reduction, serial parts', ['reduce_ser']), ('certification', ['certify']), ('self-intersection check', ['check']), ('whole simplification', ['reduce_par', 'reduce_ser', 'check', 'certify'])]
    for name, ks in sets:
        ratios = []; mx = 0; z = 0
        for f, r in base.items():
            sp = split(r); T = r['pipe_ms']; t = T - sum(sp[k] for k in ks); ratios.append(T / t); mx = max(mx, t); z += t
        out.append(f"| {name} | {agg['T']/z:.2f}x | {st.median(ratios):.2f}x | {mx/1000:.1f} |")
    # categories
    g = lambda pred: 100 * sum(n for (c, sn), n in subagg.items() if pred(c, sn)) / tot
    gcp = sum(r['study_gc_pause_ms'] for r in base.values()) / agg['T'] * 100
    out.append(f'\n### Profile categories, {s} (share of CPU samples inside the pipeline window, all threads)\n')
    out.append('| exact stage (beyond double) | filters (double predicates, tri-tri filter) | BVH (input oracle, AABB trees) | hash grid / spatial lists | GC (libclrgc) | GC pause, share of wall | alloc (runtime helpers, page faults, clears) | rounding | rest |'); out.append('|' + '---:|' * 9)
    ex = g(lambda c, sn: c == 'i' and EXACT_STAGE.search(sn)); fi = g(lambda c, sn: c == 'i') - ex
    bv = g(lambda c, sn: c == 'ii' and sn.startswith('bvh')); gr = g(lambda c, sn: c == 'ii' and sn.startswith('grid'))
    gc = g(lambda c, sn: c == 'iii' and sn.startswith('gc')); al = g(lambda c, sn: c == 'iii') - gc
    out.append(f"| {ex:.2f} % | {fi:.1f} % | {bv:.1f} % | {gr:.1f} % | {gc:.1f} % | {gcp:.1f} % | {al:.1f} % | {g(lambda c, sn: c == 'iv'):.1f} % | {g(lambda c, sn: c == 'v'):.1f} % |")
    out.append('\nRest, by owner: ' + ', '.join(f"{sn.split(' (')[0]} {100*n/tot:.1f} %" for (c, sn), n in subagg.most_common() if c == 'v' and 100 * n / tot >= 0.5))
open('tables.md', 'w').write('\n'.join(out) + '\n')
print(MED)
