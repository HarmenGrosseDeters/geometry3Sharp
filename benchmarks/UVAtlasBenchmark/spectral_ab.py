#!/usr/bin/env python3
# Summary of `UVAtlasBenchmark.dll chart-ab` (one JSON line per atlas chart): LSCM (the default) against
# SpectralConformalParameterization (the opt-in) on the same charts. Usage: python3 spectral_ab.py results/chart_ab.jsonl
import json, sys, collections

def num(x):
    return float(x)

def q(xs, p):
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(p * len(xs)))] if xs else float('nan')

def mx(xs):
    return max(xs) if xs else float('nan')

rows = [json.loads(l) for f in sys.argv[1:] for l in open(f) if l.strip()]
ch = [r for r in rows if 'chart' in r]
print(f"meshes {len(set(r['file'] for r in rows))}, charts {len(ch)}; meshes without an atlas: {sum(1 for r in rows if 'atlas' in r)}")
print()
print('| | LSCM (default) | spectral (opt-in) |')
print('|---|---|---|')
def status(m):
    c = collections.Counter(r[m]['status'] + (' (nearly double)' if r[m].get('double') else '') for r in ch)
    return ', '.join(f'{k} {v}' for k, v in c.most_common())
print(f"| status | {status('lscm')} | {status('spec')} |")
for name, key in [('charts with flipped or degenerate UV triangles', 'fold')]:
    print(f"| {name} | {sum(1 for r in ch if (r['lscm'].get(key) or 0) > 0)} | {sum(1 for r in ch if (r['spec'].get(key) or 0) > 0)} |")
print(f"| Success but not injective (exact overlap test) | {sum(1 for r in ch if r['lscm']['status'] == 'Success' and r['lscm'].get('inj') is False)} | {sum(1 for r in ch if r['spec']['status'] == 'Success' and r['spec'].get('inj') is False)} |")
both = [r for r in ch if r['lscm']['status'] == 'Success' and r['spec']['status'] == 'Success']
print(f"| Success where the other is FlippedTriangles | {sum(1 for r in ch if r['lscm']['status'] == 'Success' and r['spec']['status'] == 'FlippedTriangles')} | {sum(1 for r in ch if r['spec']['status'] == 'Success' and r['lscm']['status'] == 'FlippedTriangles')} |")
for label, key in [('mean conformal', 'meanK'), ('max conformal', 'maxK'), ('mean area distortion', 'meanA')]:
    L = [num(r['lscm'][key]) for r in both]; S = [num(r['spec'][key]) for r in both]
    print(f"| {label}, median / p90 / max (both Success, {len(both)}) | {q(L, .5):.4g} / {q(L, .9):.4g} / {mx(L):.4g} | {q(S, .5):.4g} / {q(S, .9):.4g} / {mx(S):.4g} |")
    lo = sum(1 for a, b in zip(L, S) if b < a * (1 - 1e-6)); hi = sum(1 for a, b in zip(L, S) if b > a * (1 + 1e-6))
    rat = [b / a for a, b in zip(L, S) if a > 0]
    print(f"| {label}: spectral lower / higher / equal (1e-6); spectral / LSCM p1 / median / p99 / max | | {lo} / {hi} / {len(L) - lo - hi}; {q(rat, .01):.4g} / {q(rat, .5):.4g} / {q(rat, .99):.4g} / {mx(rat):.4g} |")
for label, sel in [('all both-Success charts', both), ('charts with >= 2000 triangles', [r for r in both if r['tris'] >= 2000])]:
    tl = sum(num(r['lscm']['ms']) for r in sel); ts = sum(num(r['spec']['ms']) for r in sel)
    rat = [num(r['spec']['ms']) / num(r['lscm']['ms']) for r in sel if num(r['lscm']['ms']) > 0]
    ratio = f"{ts / tl:.2f}x" if tl > 0 else "-"
    print(f"| time, {label} ({len(sel)}): total; per chart spectral / LSCM median, p90 | {tl / 1000:.1f} s | {ts / 1000:.1f} s ({ratio}); {q(rat, .5):.2f}x, {q(rat, .9):.2f}x |")
it = [r['spec']['it'] for r in ch if r['spec']['status'] == 'Success']
it2 = [r['spec']['it2'] for r in ch if r['spec']['status'] == 'Success']
print(f"| spectral iterations median / p90 / max (next pair: median) | | {q(it, .5)} / {q(it, .9)} / {mx(it)} ({q(it2, .5)}) |")
dbl = [r for r in ch if r['spec'].get('double')]
print(f"| refused as nearly double: atlas method of the chart, median triangles | | {dict(collections.Counter(r['method'] for r in dbl))}, {q([r['tris'] for r in dbl], .5)} |")
