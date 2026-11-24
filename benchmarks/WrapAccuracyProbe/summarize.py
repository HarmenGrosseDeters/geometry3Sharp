#!/usr/bin/env python
"""Hypothesis summaries of Spike A from the JSONL files of the probe:  python summarize.py a file.jsonl [file2.jsonl ...]
H1  tightening the offset alone: growth of triangles / time per decade, and the error floor of the wrap
H2  snap back at a coarse offset against the wrap at finer offsets
H3  snap back error against alpha
H4  what the naive projection damages, what the validity-preserving one gives up
Errors are the two-sided certified Hausdorff upper bounds (relative to the diagonal) unless 'p99' is named."""
import json, sys, statistics, math, collections

def load(files):
    rows = []
    for f in files:
        for l in open(f):
            r = json.loads(l)
            for k, v in list(r.items()):
                if v in ('NaN', 'Infinity', '-Infinity'):
                    r[k] = float(v.replace('Infinity', 'inf'))
            rows.append(r)
    return rows

def H(r):
    return max(r['h_res_in'], r['h_in_res']) / r['diag']

def P(r, k):
    return max(r['res_in_' + k], r['in_res_' + k]) / r['diag']

def med(xs):
    xs = [x for x in xs if x == x and x not in (float('inf'),)]
    return statistics.median(xs) if xs else float('nan')

def geomean(xs):
    xs = [x for x in xs if x > 0]
    return math.exp(sum(math.log(x) for x in xs) / len(xs)) if xs else float('nan')

def main():
    rows = load([a for a in sys.argv[2:] if not a.startswith('--')])
    by = collections.defaultdict(dict)
    seen = set()
    for r in rows:
        if r.get('status') in ('exception',) or 'variant' not in r:
            continue
        by[(r['input'], r['alpha_den'], r['offset_rel'])][r['variant']] = r
    if '--coarse' in sys.argv:
        for k in [k for k in by if k[2] < 5e-5]:
            del by[k]
    allinputs = sorted(set(k[0] for k in by))
    if '--full' in sys.argv:          # only the inputs that have the complete 3 x 3 grid
        cnt = collections.Counter(k[0] for k in by)
        allinputs = [i for i in allinputs if cnt[i] >= 9]
    concave = [i for i in allinputs if i.startswith('lshape') or i.startswith('slot')]
    convex = [i for i in allinputs if i.startswith(('box', 'plate', 'cylinder', 'sphere'))]
    if concave and convex:
        for name, grp in (('all', allinputs), ('convex solids (box, plates, cylinder, sphere)', convex), ('concave solids (lshape, slots)', concave)):
            print()
            print('######## ' + name)
            summarize(by, grp, rows)
    else:
        summarize(by, allinputs, rows)

def summarize(by, inputs, rows):
    alphas = sorted(set(k[1] for k in by))
    offs = sorted(set(k[2] for k in by), reverse=True)
    print('inputs:', len(inputs), 'configs:', len([k for k in by if k[0] in inputs]))

    print('\nH1: growth per decade of the offset (geometric mean of the per-input ratios), wrap only')
    print('| alpha | offset step | triangles x | time x | wrap H x (error shrink) | snapped H x |')
    print('|---|---|---:|---:|---:|---:|')
    for a in alphas:
        for o1, o2 in zip(offs, offs[1:]):
            t, ms, h, hs = [], [], [], []
            for i in inputs:
                w1 = by.get((i, a, o1), {}); w2 = by.get((i, a, o2), {})
                if 'wrap' in w1 and 'wrap' in w2 and 'tris' in w1['wrap'] and 'tris' in w2['wrap'] and w1['wrap']['tris'] and w2['wrap']['tris']:
                    t.append(w2['wrap']['tris'] / w1['wrap']['tris']); ms.append(w2['wrap']['ms'] / w1['wrap']['ms'])
                    if 'h_res_in' in w1['wrap'] and 'h_res_in' in w2['wrap']:
                        h.append(H(w1['wrap']) / H(w2['wrap']))
                    if 'safe' in w1 and 'safe' in w2 and H(w2['safe']) > 0:
                        hs.append(H(w1['safe']) / H(w2['safe']))
            print('| d/%g | %g -> %g | %.2f | %.2f | %.2f | %.2f |' % (a, o1, o2, geomean(t), geomean(ms), geomean(h), geomean(hs)))

    print('\nH2: the snapped result at the coarsest offset against the wrap at finer offsets (same alpha): the finest offset whose wrap error is still above the snapped error')
    print('| alpha | snapped H (median) at offset %g | wrap H at %s | inputs where the snap at %g beats the wrap at %g |' % (offs[0], ' / '.join('%g' % o for o in offs), offs[0], offs[-1]))
    print('|---|---:|---|---:|')
    for a in alphas:
        sn = []; wr = {o: [] for o in offs}; beats = 0; n = 0
        for i in inputs:
            c = by.get((i, a, offs[0]), {})
            if 'safe' not in c or 'h_res_in' not in c['safe']: continue
            sn.append(H(c['safe']))
            fin = by.get((i, a, offs[-1]), {}).get('wrap')
            for o in offs:
                w = by.get((i, a, o), {}).get('wrap')
                if w and 'h_res_in' in w: wr[o].append(H(w))
            if fin and 'h_res_in' in fin:
                n += 1
                if H(c['safe']) <= H(fin): beats += 1
        print('| d/%g | %.3g | %s | %d of %d |' % (a, med(sn), ' / '.join('%.3g' % med(wr[o]) for o in offs), beats, n))

    print('\nH3: error against alpha (median over the inputs, snapped = validity-preserving), p99 in brackets')
    print('| offset | ' + ' | '.join('alpha d/%g' % a for a in alphas) + ' |')
    print('|---|' + '---:|' * len(alphas))
    for o in offs:
        cells = []
        for a in alphas:
            s = [H(by[(i, a, o)]['safe']) for i in inputs if (i, a, o) in by and 'safe' in by[(i, a, o)] and 'h_res_in' in by[(i, a, o)]['safe']]
            p = [P(by[(i, a, o)]['safe'], 'p99') for i in inputs if (i, a, o) in by and 'safe' in by[(i, a, o)] and 'res_in_p99' in by[(i, a, o)]['safe']]
            cells.append('%.3g (%.3g)' % (med(s), med(p)))
        print('| %g | %s |' % (o, ' | '.join(cells)))

    print('\nH4: naive projection damage and what the safe version gives up')
    bad = [r['bad_area_share'] for k, v in by.items() if k[0] in inputs for r in [v.get('naive')] if r and 'bad_area_share' in r]
    nclean = [v['naive']['verdict'] == 'clean' for k, v in by.items() if k[0] in inputs and 'naive' in v and 'verdict' in v['naive']]
    nself = [v['naive'].get('selfint', 0) > 0 for k, v in by.items() if k[0] in inputs and 'naive' in v]
    sclean = [v['safe']['verdict'] == 'clean' and v['safe'].get('selfint', 1) == 0 for k, v in by.items() if k[0] in inputs and 'safe' in v and 'verdict' in v['safe']]
    full = [v['safe']['moved_full'] for k, v in by.items() if k[0] in inputs and 'safe' in v and 'moved_full' in v['safe']]
    print('naive: configs %d, clean %d, with self-intersections %d, bad (flipped or collapsed) area share: median %.4f%%, max %.4f%%' % (len(nclean), sum(nclean), sum(nself), 100 * med(bad), 100 * max(bad) if bad else float('nan')))
    print('safe : configs %d, clean and 0 self-intersections %d, vertices moved fully: median %.2f%%, min %.2f%%' % (len(sclean), sum(sclean), 100 * med(full), 100 * min(full) if full else float('nan')))
    ratio = [H(v['safe']) / H(v['naive']) for k, v in by.items() if k[0] in inputs and 'safe' in v and 'naive' in v and 'h_res_in' in v['safe'] and 'h_res_in' in v['naive'] and H(v['naive']) > 0]
    print('safe H / naive H: median %.3f, max %.3f (1 = nothing lost)' % (med(ratio), max(ratio) if ratio else float('nan')))
    if len(sys.argv) > 3 or True:
        print('\nconfigs with exceptions / timeouts:')
        for r in rows:
            if r.get('status') not in (None, 'Success', 'PointLimitReached') and r.get('variant') == 'wrap':
                print(' ', r['input'], r['alpha_den'], r['offset_rel'], r.get('status'), r.get('error', ''), r.get('ms', ''))

if __name__ == '__main__' and sys.argv[1] == 'a':
    main()


# ---------------- Spike B ----------------
SOLID_AREA = {'box': 632.0, 'cylinder': 207.0, 'sphere': 310.0}     # of the base solids of Inputs.FineSolid (the cylinder and sphere approximately)

def summarize_b(files):
    rows = load(files)
    by = collections.defaultdict(dict)
    for r in rows:
        if 'variant' in r and r.get('status') != 'exception':
            by[(r['input'], r['alpha_den'], r['offset_rel'])][r['variant']] = r
    print('configs', len(by))
    print('\nmeans over the configs, per variant: added share, lost share (area shares of the whole result / input; tau = 4 offsets)')
    for v in ('wrap', 'snap', 'auto', 'robust'):
        a = [d[v]['added_share'] for d in by.values() if v in d and 'added_share' in d[v] and d[v]['added_share'] == d[v]['added_share']]
        l = [d[v]['lost_share'] for d in by.values() if v in d and 'lost_share' in d[v] and d[v]['lost_share'] == d[v]['lost_share']]
        c = [d[v]['verdict'] == 'clean' for d in by.values() if v in d and 'verdict' in d[v]]
        print('  %-7s configs %3d  added: median %.4f max %.4f   lost: median %.4f max %.4f   clean %d of %d' % (v, len(a), med(a), max(a) if a else float('nan'), med(l), max(l) if l else float('nan'), sum(c), len(c)))
    print('\nsolid with a hole against an open sheet: wrap, deep volume share (volume of the wrap farther than 4 offsets from the input, share of the wrap volume)')
    print('| input | hole eq. diameter / alpha | wrap deep share (alpha d/4, d/20, d/100 at offset 1e-3 / 1e-4) | wrap volume / solid |')
    print('|---|---|---|---|')
    names = sorted(set(k[0] for k in by))
    for n in names:
        cells = []; vols = []; ratio = []
        for a in (4, 20, 100):
            for o in (1e-3, 1e-4):
                d = by.get((n, a, o), {}).get('wrap')
                if d and 'deep_volume_share' in d:
                    cells.append('%.0f%%' % (100 * d['deep_volume_share']))
                    vols.append('%.2f' % d['vol_ratio'] if d.get('vol_ratio', float('nan')) == d.get('vol_ratio', float('nan')) else '-')
                    p = n.split('_')
                    if len(p) >= 4 and p[0] == 'hole' and p[2] in ('p', 'e'):
                        share = float(p[3]); sa = SOLID_AREA.get(p[1], 0)
                    elif len(p) >= 3 and p[0] == 'hole' and p[2] == 'face':
                        share = 160.0 / 632.0; sa = 632.0
                    else:
                        share = None
                    if share is not None and d['diag'] > 0:
                        ratio.append(2 * math.sqrt(share * sa / math.pi) / (d['diag'] / a))
                else:
                    cells.append('-'); vols.append('-')
        print('| %s | %s | %s | %s |' % (n, ('%.2g .. %.2g' % (min(ratio), max(ratio))) if ratio else '-', ' / '.join(cells), ' / '.join(vols)))
    print('\npatch statistics of the wrap (far parts: farther than 4 offsets from the input)')
    print('| input | alpha | offset | patches (matched) | patch area / hole area | largest patch / hole area | unmatched patch area / patch area |')
    print('|---|---|---|---:|---:|---:|---:|')
    for (n, a, o), d in sorted(by.items(), key=lambda kv: (kv[0][0], -kv[0][1], -kv[0][2])):
        w = d.get('wrap')
        if not w or 'patches' not in w or not n.startswith('hole'): continue
        um = w['patch_area_unmatched'] / w['patch_area'] if w.get('patch_area') else float('nan')
        print('| %s | d/%g | %g | %d (%d) | %.3g | %.3g | %.2g |' % (n, a, o, w['patches'], w['patches_matched'], w['patch_over_hole_area'], w['largest_patch_over_hole_area'], um))

if len(sys.argv) > 1 and sys.argv[1] == 'b':
    summarize_b(sys.argv[2:])
