#!/usr/bin/env python3
"""Tables of the varifold feature / curvature study from results/soup/*.jsonl (run_soup.sh)."""
import json, os, sys
from collections import defaultdict

d = sys.argv[1] if len(sys.argv) > 1 else 'benchmarks/SharpFeatureProbe/results/soup'
def rows(name):
    p = os.path.join(d, name)
    return [json.loads(l) for l in open(p)] if os.path.exists(p) else []

def f(x, nd=3):
    try:
        x = float(x)
    except (TypeError, ValueError):
        return str(x)
    return '-' if x != x or x < 0 else f'{x:.{nd}f}'

out = []
cr = rows('crease.jsonl')
if cr:
    shapes = [s for s in dict.fromkeys(r['shape'] for r in cr) if s != 'rbox']
    breaks = list(dict.fromkeys(r['break'] for r in cr))
    by = {(r['shape'], r['break'], r['method']): r for r in cr}
    def best_weld(s, b):
        ws = [by[k] for k in by if k[0] == s and k[1] == b and k[2].startswith('weld')]
        return max(ws, key=lambda r: r['f1'])
    out.append('## Creases (F1 / precision / recall / median dihedral error in degrees; weld = the best of the three weld tolerances)\n')
    for s in shapes:
        out.append(f'### {s} ({by[(s, "clean", "weld_exact")]["tris"]} triangles clean, {by[(s, "clean", "weld_exact")]["true_edges"]} true crease edges)\n')
        out.append('| breakage | weld dihedral (tol) | varifold c=1.5 | varifold c=2 | varifold c=3 | PCA c=2 |')
        out.append('|---|---|---|---|---|---|')
        for b in breaks:
            w = best_weld(s, b)
            cells = [f"{f(w['f1'])} / {f(w['precision'])} / {f(w['recall'])} / {f(w['dih_err_med'], 1)} ({w['method'][5:]})"]
            for m in ['varifold_c1.5', 'varifold_c2', 'varifold_c3', 'pca_c2']:
                r = by.get((s, b, m))
                cells.append('n/a' if r is None else f"{f(r['f1'])} / {f(r['precision'])} / {f(r['recall'])} / {f(r['dih_err_med'], 1)}")
            out.append(f'| {b} | ' + ' | '.join(cells) + ' |')
        out.append('')
    out.append('### rbox (no crease): false positives (edges)\n')
    out.append('| breakage | edges | weld exact | weld 0.1h | weld 0.3h | varifold c=1.5 | varifold c=2 | varifold c=3 | PCA c=2 |')
    out.append('|---|---|---|---|---|---|---|---|---|')
    for b in breaks:
        rr = [by.get(('rbox', b, m)) for m in ['weld_exact', 'weld_0.1h', 'weld_0.3h', 'varifold_c1.5', 'varifold_c2', 'varifold_c3', 'pca_c2']]
        if rr[0] is None: continue
        out.append(f'| {b} | {3 * rr[0]["tris"]} | ' + ' | '.join(str(r['fp']) if r else 'n/a' for r in rr) + ' |')
    out.append('')
    out.append('### Median over box, slot, vnotch, fillet\n')
    out.append('| breakage | weld F1 (best tol) | varifold c=2 F1 | P | R | dihedral err med (weld / varifold) | PCA F1 |')
    out.append('|---|---|---|---|---|---|---|')
    med = lambda v: sorted(v)[len(v) // 2] if v else float('nan')
    for b in breaks:
        ws = [best_weld(s, b) for s in shapes]; vs = [by[(s, b, 'varifold_c2')] for s in shapes]; ps = [by[(s, b, 'pca_c2')] for s in shapes]
        out.append(f"| {b} | {f(med([r['f1'] for r in ws]))} | {f(med([r['f1'] for r in vs]))} | {f(med([r['precision'] for r in vs]))} | {f(med([r['recall'] for r in vs]))} | "
                   f"{f(med([float(r['dih_err_med']) for r in ws if float(r['dih_err_med']) == float(r['dih_err_med'])]), 2)} / {f(med([float(r['dih_err_med']) for r in vs]), 2)} | {f(med([r['f1'] for r in ps]))} |")
    out.append('')

cg = rows('crease_gap.jsonl')
if cg:
    out.append('## Localisation tolerance on gaps (bound for an adaptive tolerance): varifold c=2, F1 / precision / recall\n')
    ms = list(dict.fromkeys(r['method'] for r in cg if r['method'].startswith('varifold')))
    out.append('| shape | breakage | ' + ' | '.join(ms) + ' |')
    out.append('|---|---|' + '---|' * len(ms))
    byg = {(r['shape'], r['break'], r['method']): r for r in cg}
    for s, b in dict.fromkeys((r['shape'], r['break']) for r in cg):
        cells = []
        for m in ms:
            r = byg[(s, b, m)]
            cells.append(f"{f(r['f1'])} / {f(r['precision'])} / {f(r['recall'])}" if s != 'rbox' else f"fp {r['fp']}")
        out.append(f'| {s} | {b} | ' + ' | '.join(cells) + ' |')
    out.append('')

def curv_table(name, key, title):
    cv = rows(name)
    if not cv: return
    out.append(f'## {title} (e_K median / p90 in units of 1 / R; cylinder and sphere regions; valid fraction if < 1)\n')
    methods = list(dict.fromkeys(r['method'] for r in cv))
    for s in dict.fromkeys(r['shape'] for r in cv):
        keys = list(dict.fromkeys(str(r[key]) for r in cv if r['shape'] == s))
        regions = [g for g in ['cyl', 'sphere', 'flat'] if any(r['shape'] == s and r['region'] == g for r in cv)]
        for g in regions:
            out.append(f'### {s}, {g}\n')
            out.append(f'| {key} | ' + ' | '.join(methods) + ' |')
            out.append('|---|' + '---|' * len(methods))
            for k in keys:
                cells = []
                for m in methods:
                    r = next((r for r in cv if r['shape'] == s and str(r[key]) == k and r['method'] == m and r['region'] == g), None)
                    cells.append('n/a' if r is None else f"{f(r['eK_med'])} / {f(r['eK_p90'])}" + ('' if r['valid'] >= 1 else f" ({f(r['valid'], 2)})"))
                out.append(f'| {k} | ' + ' | '.join(cells) + ' |')
            out.append('')

curv_table('curv.jsonl', 'break', 'Curvature on soups')
curv_table('cloud.jsonl', 'noise', 'Curvature on point clouds (noise in spacings)')

tm = rows('time.jsonl')
if tm:
    out.append('## Time (seconds, 4 threads, breakage mix:0.5; curvature at one corner per triangle, eps = 3 h)\n')
    cols = ['weld_dihedral_s', 'varifold_crease_s', 'pca_crease_s', 'varifold_curv_e3_s', 'jet_curv_e3_s', 'mpc_weld_s']
    out.append('| shape | triangles | ' + ' | '.join(cols) + ' |')
    out.append('|---|---|' + '---|' * len(cols))
    for r in tm:
        out.append(f"| {r['shape']} | {r['tris']} | " + ' | '.join(f(r[c], 2) for c in cols) + ' |')
    out.append('')

ck = rows('check.jsonl')
if ck:
    out.append('## Determinism and similarity (fillet, rbox; mix:0.5)\n')
    for r in ck:
        out.append('- ' + ', '.join(f'{k} {v}' for k, v in r.items() if k != 'kind'))
    out.append('')
print('\n'.join(out))
