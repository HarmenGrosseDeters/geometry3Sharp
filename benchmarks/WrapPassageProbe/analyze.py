#!/usr/bin/env python3
"""Tables of the passage study from the probe's jsonl (usage: analyze.py main.jsonl [--rows])."""
import json, sys, collections

rows = [json.loads(l) for l in open(sys.argv[1])]
show_rows = '--rows' in sys.argv


def passes(d, v, p):
    """pin of radius p passes the wrap variant v (raw / simp): certified clearance >= p and the axis ends outside"""
    lo = d.get(v + '_clr_lo')
    if lo is None or d.get(v + '_w0') is None:
        return False
    return lo >= p and abs(d[v + '_w0']) < 0.5 and abs(d[v + '_w1']) < 0.5


def opens(d, v):
    """the passage is open (clearance certified above 1 % of the input clearance, both ends outside)"""
    return passes(d, v, 0.01 * d['r_lo'])


groups = collections.defaultdict(list)
for d in rows:
    groups[(d['part'], d['R'], d['tri'])].append(d)

print('## per row: part R tri k | raw: tris s clr/r open half | simp: tris s clr/r open half | bound/r bound-ok')
if show_rows:
    for g in sorted(groups):
        for d in sorted(groups[g], key=lambda x: x['ratio']):
            r = d['r_lo']
            def f(v):
                if d.get(v + '_clr_lo') is None:
                    return '   -   '
                return '%6d %5.1fs %.3f %s %s' % (d[v + '_tris'], d.get(v[:4] == 'raw_' and 'raw_s' or v + '_s', d.get(v + '_s', 0) or 0) if False else (d['raw_s'] if v == 'raw' else d.get('simp_s', 0)),
                                                    d[v + '_clr_lo'] / r, 'Y' if opens(d, v) else '-', 'Y' if passes(d, v, 0.5 * r) else '-')
            ok = '' if d['bound'] == 0 else ('ok' if d['raw_clr_lo'] >= d['bound'] - 1e-9 else 'VIOLATED')
            print('%-6s R=%-3g t%d k=%-4g | %s | %s | %.3f %s' % (d['part'], d['R'], d['tri'], d['ratio'], f('raw'), f('simp'), d['bound'] / r, ok))

print('\n## theorem check: raw clearance >= sqrt((r-offset)^2 - alpha^2) wherever the bound is positive')
n = viol = 0
worst = None
for d in rows:
    if d['bound'] > 0 and d.get('raw_clr_lo') is not None:
        n += 1
        slack = d['raw_clr_lo'] - d['bound']
        if slack < -1e-9:
            viol += 1
        rel = d['raw_clr_lo'] / d['bound']
        if worst is None or rel < worst[0]:
            worst = (rel, d['part'], d['R'], d['tri'], d['ratio'])
print('rows with a positive bound: %d, violations: %d, smallest measured/bound %s' % (n, viol, worst))
rel = sorted(d['raw_clr_lo'] / d['bound'] for d in rows if d['bound'] > 0 and d.get('raw_clr_lo'))
if rel:
    print('measured / bound: min %.3f median %.3f max %.3f' % (rel[0], rel[len(rel) // 2], rel[-1]))

print('\n## open passage (clearance > 1 %% of r, ends outside) and half pin (p = r/2) by k; Y = pass in all configs of the cell')
ks = sorted({d['ratio'] for d in rows})
for v in ('raw', 'simp'):
    print('variant', v)
    print('part    R   ' + ' '.join('%5g' % k for k in ks) + '   (open: count of passing / configs per k; configs = 2 triangulations)')
    for part in sorted({d['part'] for d in rows}):
        for R in sorted({d['R'] for d in rows if d['part'] == part}):
            cells = []
            for k in ks:
                c = [d for d in rows if d['part'] == part and d['R'] == R and d['ratio'] == k]
                cells.append('%d/%d' % (sum(opens(d, v) for d in c), len(c)))
            print('%-7s %-3g ' % (part, R) + ' '.join('%5s' % x for x in cells))

print('\n## monotonicity per config (open passage): first k that fails, and passes after a fail')
for v in ('raw', 'simp'):
    nonmono = 0
    firstfail = []
    for g, lst in sorted(groups.items()):
        lst = sorted(lst, key=lambda x: x['ratio'])
        seq = [opens(d, v) for d in lst]
        ff = next((d['ratio'] for d, s in zip(lst, seq) if not s), None)
        after = [d['ratio'] for d, s in zip(lst, seq) if s and ff is not None and d['ratio'] > ff]
        firstfail.append((g, ff, after))
        if after:
            nonmono += 1
    print(v, 'configs with a pass after a fail:', nonmono, 'of', len(firstfail))
    for g, ff, after in firstfail:
        print('  ', g, 'first fail k =', ff, 'pass after fail at', after)

print('\n## largest pin radius that passes (clearance / r) vs the a priori value, per k, median over configs')
for v in ('raw', 'simp'):
    print('variant', v, ' k: median clr/r (a priori bound/r median)')
    for k in ks:
        c = [d for d in rows if d['ratio'] == k and d.get(v + '_clr_lo') is not None and abs(d[v + '_w0']) < 0.5 and abs(d[v + '_w1']) < 0.5]
        if not c:
            continue
        m = sorted(d[v + '_clr_lo'] / d['r_lo'] for d in c)
        b = sorted(d['bound'] / d['r_lo'] for d in c)
        print('  k=%-5g %.3f  (%.3f)  n=%d' % (k, m[len(m) // 2], b[len(b) // 2], len(c)))

print('\n## size and time (median over configs): raw / simplified triangles, raw s, simplify s')
for k in ks:
    c = [d for d in rows if d['ratio'] == k and d.get('raw_tris')]
    med = lambda xs: sorted(xs)[len(xs) // 2] if xs else float('nan')
    print('  k=%-5g raw tris %7.0f  simp tris %7.0f  raw %5.1fs  simp %5.1fs' % (
        k, med([d['raw_tris'] for d in c]), med([d['simp_tris'] for d in c if d.get('simp_tris')]),
        med([d['raw_s'] for d in c]), med([d['simp_s'] for d in c if d.get('simp_s') is not None])))
