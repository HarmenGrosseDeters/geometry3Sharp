#!/usr/bin/env python3
"""Tables for benchmarks/ContouringTopology: analyze.py <jsonl>..."""
import json, sys, collections

rows = [json.loads(l) for f in sys.argv[1:] for l in open(f)]


def short(sig):
    """c=3;g=0,0,2 -> 3 comp, genus 2 (the per-component list stays the key of every comparison)"""
    if sig is None: return '?'
    c, g = sig.split(';')
    gs = [float(x) for x in g[2:].split(',') if x]
    return f"{c[2:]} comp, genus {sum(gs):g}"


imp = [r for r in rows if r['input'].startswith('imp:') and r.get('status') == 'ok']
failed = [r for r in rows if r.get('status') != 'ok']
if failed:   # never dropped silently: a failed run is in no table below
    print(f"{len(failed)} runs without a result (not in the tables):", ', '.join(f"{r['input']}@{r['res']}:{r.get('status')}" for r in failed[:20]))
probe = [r for r in rows if r['input'].startswith('probe:')]
wind = [r for r in rows if not r['input'].startswith(('imp:', 'probe:'))]

# reference topology of the shapes without a truth by construction: the finest runs must agree
ref = {}
for r in imp:
    if 'truth_sig' not in r and r['res'] >= 192:
        ref.setdefault(r['shape'], collections.Counter())[(r['res'], r['sig'])] += 1
for s, c in sorted(ref.items()):
    print('reference', s, {(res, short(sig)): n for (res, sig), n in c.items()})
# a reference only if all fine runs agree (the gyroid's do not: it has none)
refsig = {s: next(iter(c))[1] for s, c in ref.items() if len({sig for (res, sig) in c}) == 1}

def table(rs, keyf, title):
    g = collections.defaultdict(list)
    for r in rs: g[keyf(r)].append(r)
    print('\n' + title)
    print('| shape | res | feature / cell | runs | wrong topology | components wrong | genus wrong | not closed | truth | most common wrong |')
    print('|---|---|---|---|---|---|---|---|---|---|')
    for k in sorted(g):
        v = g[k]
        truth = v[0].get('truth_sig') or refsig.get(v[0]['shape'])
        if truth is None:
            print(f"| {k[0]} | {k[1]} | {k[2] if k[2] else '-'} | {len(v)} | no reference | | | {sum(1 for r in v if not r['closed'])} | | |")
            continue
        # an open result is wrong whatever its genus (the signature does not carry the boundary loops)
        wrong = [r for r in v if r['sig'] != truth or not r['closed']]
        tc = int(truth.split(';')[0][2:])
        cw = sum(1 for r in wrong if r['components'] != tc)
        gw = sum(1 for r in wrong if r['components'] == tc)
        nc = sum(1 for r in v if not r['closed'])
        mc = collections.Counter(r['sig'] for r in wrong).most_common(1)
        print(f"| {k[0]} | {k[1]} | {k[2] if k[2] else '-'} | {len(v)} | {len(wrong)} | {cw} | {gw} | {nc} | {short(truth)} | {short(mc[0][0]) if mc else ''} |")

fam = [r for r in imp if r['k'] > 0]
old = [r for r in imp if r['k'] == 0 and r['res'] < 192]
if fam: table(fam, lambda r: (r['shape'], r['res'], r['k']), 'Feature-width families')
if old: table(old, lambda r: (r['shape'], r['res'], 0), 'Shapes of DualContouringIntersections')
if fam:
    print('\n| feature / cell | runs | wrong | share |')
    print('|---|---|---|---|')
    g = collections.defaultdict(list)
    for r in fam: g[r['k']].append(r['sig'] != r['truth_sig'] or not r['closed'])
    for k in sorted(g): print(f"| {k} | {len(g[k])} | {sum(g[k])} | {100 * sum(g[k]) / len(g[k]):.0f} % |")

if wind:
    print('\n| input | clean | truth / reference | ' + ' | '.join(f'{p} {res}' for res in sorted({r['res'] for r in wind}) for p in ('raw', 'final')) + ' |')
    by = collections.defaultdict(dict)
    for r in wind: by[r['input']][r['res']] = r
    allres = sorted({r['res'] for r in wind})
    print('|---' * (3 + 2 * len(allres)) + '|')
    stats = collections.Counter()
    for name in sorted(by):
        rs = by[name]
        any_r = next((r for r in rs.values() if 'in_clean' in r), next(iter(rs.values())))
        clean = any_r.get('in_clean', False)
        top = max(rs)
        truth = any_r.get('truth_sig') if clean else rs[top].get('raw_sig')
        if truth is None:
            print(f"| {name} | {'yes' if clean else 'no'} | no reference (the finest run has no result) |" + ' |' * (2 * len(allres)))
            continue
        cells = []
        for res in allres:
            r = rs.get(res)
            for p in ('raw_', 'final_'):
                if r is None or p + 'sig' not in r: cells.append('?'); continue
                ok = r[p + 'sig'] == truth and r.get(p + 'closed', True)
                if not (not clean and res == top):
                    stats[(clean, res, p, ok)] += 1
                extra = '' if r.get(p + 'closed', True) else ' open'
                cells.append(('=' if ok else short(r[p + 'sig'])) + extra)
        print(f"| {name} | {'yes' if clean else 'no'} | {short(truth)}{'' if clean else ' (res ' + str(top) + ')'} | " + ' | '.join(cells) + ' |')
    print('\n| input class | res | stage | runs | differs |')
    print('|---|---|---|---|---|')
    for clean in (True, False):
        for res in allres:
            for p in ('raw_', 'final_'):
                a, b = stats[(clean, res, p, True)], stats[(clean, res, p, False)]
                if a + b: print(f"| {'clean (exact truth)' if clean else 'not clean (finest run as reference)'} | {res} | {p[:-1]} | {a + b} | {b} |")
    print('\n| input | res | hidden gap / wall edges | refined cells | raw ms | final ms |')
    print('|---|---|---|---|---|---|')
    for name in sorted(by):
        for res in allres:
            r = by[name].get(res)
            if r and 'raw_ms' in r: print(f"| {name} | {res} | {r.get('raw_hidden_gaps')} / {r.get('raw_hidden_walls')} | {r.get('raw_refined_cells')} | {r['raw_ms']} | {r['final_ms']} |")

if probe:
    print('\n| set | res | inputs | surface cells | multi-sheet | not monotone | fails local criterion | fails PV variation |')
    print('|---|---|---|---|---|---|---|---|')
    g = collections.defaultdict(list)
    for r in probe:
        if 'probe_cells' in r: g[('procedural' if 'mesh:' in r['input'] else 'T1 clean' if r.get('in_clean') else 'T1 not clean', r['res'])].append(r)
    for k in sorted(g):
        v = g[k]; n = sum(r['probe_cells'] for r in v)
        f = lambda key: f"{100 * sum(r[key] for r in v) / n:.2f} %"
        print(f"| {k[0]} | {k[1]} | {len(v)} | {n} | {f('probe_multi_sheet')} | {f('probe_not_monotone')} | {f('probe_fail')} | {f('probe_fail_pv')} |")
    print('\nper input, fails local criterion (share of surface cells) by res:')
    by = collections.defaultdict(dict)
    for r in probe:
        if 'probe_cells' in r: by[r['input']][r['res']] = r
    for name in sorted(by):
        print(' ', name, 'clean' if next(iter(by[name].values())).get('in_clean') else 'not clean', ' '.join(f"{res}:{100 * r['probe_fail'] / r['probe_cells']:.2f}%({r['probe_fail']})" for res, r in sorted(by[name].items())))
