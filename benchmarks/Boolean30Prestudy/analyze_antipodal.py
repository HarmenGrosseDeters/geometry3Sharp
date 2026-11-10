#!/usr/bin/env python3
"""Exact Boolean 3.0 pre-study, T5 counting step: table of results/antipodal.jsonl (antipodal_job.sh rows).
usage: python3 analyze_antipodal.py results/antipodal.jsonl [results/antipodal_unit.jsonl]
With the unit file (prestudy-antipodal --unit-only under DOTNET_TieredCompilation=0: the per-segment cost after a long
warm-up, per mesh), the model uses those unit costs instead of the in-probe ones (which a short warm-up biased)."""
import json, sys
print('| model | triangles | queries | boundary segments (welded) | FWN us / query A, B | ns / segment A, B | ray us A, B | '
      'Antipodal / FWN A, B | FWN s, all queries (A + B, serial) | Antipodal s | switch B < 1000 s |')
print('|' + '---|' * 11)
F = A = S = 0.0
failed = []
unit = {}
if len(sys.argv) > 2:
    for line in open(sys.argv[2]):
        u = json.loads(line)
        unit[u['case']] = (u['mesh0']['ns_per_segment'], u['mesh1']['ns_per_segment'])
for line in open(sys.argv[1]):
    r = json.loads(line)
    if 'error' in r:
        failed.append(r['case'])
        continue
    m0, m1 = r['mesh0'], r['mesh1']
    if r['case'] in unit:
        for m, ns in zip((m0, m1), unit[r['case']]):
            m['ns_per_segment'] = ns
            a_us = m['boundary_segments'] * ns / 1000 + m['ray_us']
            m['antipodal_us_per_query'] = round(a_us, 2)
            m['ratio_antipodal_over_fwn'] = round(a_us / m['fwn_us_per_query'], 3)
            m['antipodal_s_all_queries'] = a_us * r['queries'] / 1e6
    f = m0['fwn_s_all_queries'] + m1['fwn_s_all_queries']
    a = m0['antipodal_s_all_queries'] + m1['antipodal_s_all_queries']
    s = sum(m['antipodal_s_all_queries'] if m['boundary_segments'] < 1000 else m['fwn_s_all_queries'] for m in (m0, m1))
    F += f; A += a; S += s
    print(f"| {r['case']} | {r['tris']} | {r['queries']} | {m0['boundary_segments']} | {m0['fwn_us_per_query']}, {m1['fwn_us_per_query']} | "
          f"{m0['ns_per_segment']}, {m1['ns_per_segment']} | {m0['ray_us']}, {m1['ray_us']} | {m0['ratio_antipodal_over_fwn']}, {m1['ratio_antipodal_over_fwn']} | "
          f"{f:.1f} | {a:.1f} | {s:.1f} |")
O = None
print(f'\nsums: FWN {F:.0f} s, Antipodal everywhere {A:.0f} s ({(A / F - 1) * 100:+.0f} %), switch at 1000 boundary segments '
      f'{S:.0f} s ({(S / F - 1) * 100:+.0f} %); no row (probe over its 30 min limit): {", ".join(failed)}')
