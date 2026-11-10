#!/usr/bin/env python3
"""Exact Boolean 3.0 pre-study, topic 2: table of results/memory.jsonl (memory_job.sh rows).
usage: python3 analyze_memory.py results/memory.jsonl"""
import json, sys
rows = {}
for line in open(sys.argv[1]):
    r = json.loads(line)
    rows[(r['gc_variant'], r['case'], r['op'])] = r
print('| model | op | input tris | arrangement retained MB | B / input tri | points | of them fixed width | pieces | '
      'alloc MB | peak working set MB | GC pause ms | ms (default GC) | ms gen0 512 MB | ms server GC |')
print('|' + '---|' * 14)
for (v, c, o), r in sorted(rows.items(), key=lambda k: (k[0][1], k[0][2])):
    if v != 'default' or 'error' in r:
        continue
    t = r['tris_a'] + r['tris_b']
    g, s = rows.get(('gen0', c, o), {}), rows.get(('server', c, o), {})
    print(f"| {c} | {o} | {t} | {r['arr_retained_mb']} | {r['arr_retained_mb'] * 1048576 / t:.0f} | {r['points']} | "
          f"{r['fixed_points']} | {r['pieces']} | {r['alloc_mb']} | {r['peak_ws_mb']} | {r['gc']['pause_ms']} | {r['ms']} | "
          f"{g.get('ms', '-')} | {s.get('ms', '-')} |")
tot = {v: sum(r['ms'] for (vv, c, o), r in rows.items() if vv == v and 'ms' in r) for v in ('default', 'gen0', 'server')}
pause = sum(r['gc']['pause_ms'] for (vv, c, o), r in rows.items() if vv == 'default' and 'gc' in r)
print(f"\nsum ms: default {tot['default']}, gen0 {tot['gen0']} ({(tot['gen0'] / tot['default'] - 1) * 100:+.1f} %), "
      f"server {tot['server']} ({(tot['server'] / tot['default'] - 1) * 100:+.1f} %); GC pauses (default) {pause} ms = "
      f"{pause / tot['default'] * 100:.1f} % of the time")
