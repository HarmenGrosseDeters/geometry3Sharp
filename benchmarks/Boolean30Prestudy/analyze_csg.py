#!/usr/bin/env python3
"""Exact Boolean 3.0 pre-study, topic 3: table of results/csg.jsonl (csg_job.sh rows: chained vs one pass).
usage: python3 analyze_csg.py results/csg.jsonl"""
import json, sys
by = {}
for line in open(sys.argv[1]):
    r = json.loads(line)
    by.setdefault(r['case'], {})[r['variant']] = r
print('| case | inputs | input tris | chained ms | one pass ms | ratio | roundings chained | rounding ms chained / one pass | '
      'result tris chained / one pass | NotCleanReason chained / one pass | Hausdorff / diag chained / one pass | volume error chained |')
print('|' + '---|' * 12)
for c, v in by.items():
    a, b = v.get('chained', {}), v.get('onepass', {})
    if not a or not b or 'error' in a or 'error' in b:
        print(f'| {c} | error | {a.get("error")} {b.get("error")} |')
        continue
    print(f"| {c} | {a['inputs']} | {a['input_tris']} | {a['ms']} | {b['ms']} | {b['ms'] / max(a['ms'], 1):.2f} | {a['roundings']} | "
          f"{a['rounding_ms']} / {b['rounding_ms']} | {a['tris']} / {b['tris']} | {a['not_clean_reason'] or 'clean'} / "
          f"{b['not_clean_reason'] or 'clean'} | {a['hausdorff_rel']:.1e} / {b['hausdorff_rel']:.1e} | {a['volume_rel_err']:.1e} |")
