#!/usr/bin/env python3
"""Exact Boolean 3.0 pre-study, topic 7: tables of results/many_a.jsonl (T3a) and results/many_b.jsonl (T3b), from many_job.sh.
usage: python3 analyze_many.py results/many_a.jsonl results/many_b.jsonl
T3a pruning bound (an estimate): the pieces of the Bi outside A (share s) are dropped before triangulation and
classification, and the Bi's mutual intersections outside A are skipped (share s of the intersections of the
arrangement of the Bi alone): bound = s (triangulation + classification of the one pass + intersections of the Bi alone)."""
import json, sys
from collections import defaultdict

a = defaultdict(dict)
for line in open(sys.argv[1]):
    r = json.loads(line)
    if 'case' in r:
        a[r['case']][r['variant']] = r
print('### T3a: A - (B1 u ... u Bn)\n')
print('| case | A tris | B tris | one pass ms | chained ms | union first ms | pairwise tree ms | result tris one pass / chained / union first / tree | '
      'clean (all four) | max volume difference to one pass | Bi pieces outside A | pruning bound |')
print('|' + '---|' * 12)
for c, v in a.items():
    o, ch, t, tr, p = (v.get(k, {}) for k in ('onepass', 'chained', 'twostep', 'tree', 'probe'))
    if not o or 'error' in o:
        print(f'| {c} | error |')
        continue
    clean = all(x.get('not_clean_reason', '?') == '' for x in (o, ch, t, tr))
    vd = max(abs(x['volume'] - o['volume']) / abs(o['volume']) for x in (ch, t, tr) if 'volume' in x)
    s = p['b_pieces_outside_a'] / max(p['b_pieces'], 1) if p else 0
    ph = o['phases_ms']
    bound = s * (ph.get('triangulation', 0) + ph.get('classification', 0) + p.get('arr_b_only_phases_ms', {}).get('intersections', 0)) / o['ms'] if p else 0
    print(f"| {c} | {o['a_tris']} | {o['b_tris']} | {o['ms']} | {ch.get('ms')} | {t.get('ms')} | {tr.get('ms')} | "
          f"{o['tris']} / {ch.get('tris')} / {t.get('tris')} / {tr.get('tris')} | {'yes' if clean else 'no'} | {vd:.1e} | {s:.2f} | {bound * 100:.0f} % |")

b = defaultdict(dict)
for line in open(sys.argv[2]):
    r = json.loads(line)
    if 'case' in r:
        b[r['case']][(r['cores'], r['variant'])] = r
print('\n### T3b: A n Bi for every i (separate results)\n')
print('| case | cores | serial ms | parallel over i ms | speedup | hashes equal | A alone ms x n | share of serial | results not clean |')
print('|' + '---|' * 9)
for c, v in b.items():
    for cores in (4, 2, 1):
        se, pa, al = v.get((cores, 'serial')), v.get((cores, 'parallel')), v.get((cores, 'a_alone'))
        if not se or not pa or not al:
            continue
        fixed = al['ms'] * se['n']
        print(f"| {c} | {cores} | {se['ms']} | {pa['ms']} | {se['ms'] / max(pa['ms'], 1):.2f} | {pa.get('hashes_equal_serial')} | "
              f"{al['ms']} x {se['n']} = {fixed} | {min(fixed / se['ms'], 1) * 100:.0f} % | {se['not_clean']} |")
