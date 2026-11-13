#!/usr/bin/env python3
"""Summary of the stage 1 spike (benchmarks/ContouringCertificate spike): refused surface cells by reason, the wrong
contours (truth != result), and the time of the check against the dual contouring, per input class and resolution.

usage: python3 analyze.py <spike.jsonl>..."""
import json, sys

rows = [json.loads(l) for f in sys.argv[1:] for l in open(f)]
print('| set | cell | inputs | surface cells | refused | share | max per input | by reason | wrong contours (refused cells) | check ms | contour ms |')
print('|---|---|---|---|---|---|---|---|---|---|---|')
for res in sorted({r['res'] for r in rows}):
    for grp in ('procedural', 'T1 clean'):
        S = [r for r in rows if r['res'] == res and r.get('in_clean') == 'clean' and r['input'].startswith('mesh:') == (grp == 'procedural')]
        if not S:
            continue
        meet = sum(r['cells_meet_L'] for r in S)
        ref = sum(r['refused_meet_L'] for r in S)
        reasons = {}
        for r in S:
            for k, v in r.items():
                if k.startswith('n_') and k != 'n_None':
                    reasons[k[2:]] = reasons.get(k[2:], 0) + v
        share_max = max(r['refused_meet_L'] / r['cells_meet_L'] for r in S)
        wrong = ['%s (%d)' % (r['input'].replace('.stl', ''), r['refused_meet_L']) for r in S if r['truth_sig'] != r.get('dc_sig')]
        check = sum(r['cert_ms'] + r['classify_ms'] for r in S)
        dc = sum(r['dc_contour'] for r in S)
        print('| %s | / %d | %d | %d | %d | %.2f %% | %.2f %% | %s | %s | %d | %d |' % (
            grp, res, len(S), meet, ref, 100.0 * ref / meet, 100.0 * share_max,
            ', '.join('%s %d' % kv for kv in sorted(reasons.items())) or '-', ', '.join(wrong) or '-', check, dc))
notclean = sorted({(r['input'], r.get('in_clean')) for r in rows if r.get('in_clean') not in (None, 'clean')})
print()
print('not clean (not tested):', ', '.join('%s %s' % x for x in notclean))
