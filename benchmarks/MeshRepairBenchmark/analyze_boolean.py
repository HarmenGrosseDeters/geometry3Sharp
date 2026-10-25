# Summarises results of BENCH_REPAIR=boolean: closedness and volume consistency of union / intersection /
# difference of each mesh with a rotated, shifted copy of itself.
import json, statistics as st, sys
rows = [json.loads(l) for l in open(sys.argv[1] if len(sys.argv) > 1 else 'results/boolean.jsonl') if l.strip()]
ok = [r for r in rows if 'bool_ie_error' in r]
print(f"{len(rows)} meshes, {len(ok)} with results, {len(rows) - len(ok)} failed:",
      sorted(set(r['status'] for r in rows if 'bool_ie_error' not in r)))
# meshes that are (nearly) flat or open sheets have ~zero solid volume; relative errors are meaningless there
solid = [r for r in ok if min(r['bool_vA'], r['bool_vB']) > 1e-3 * (r['bool_cell'] * 128) ** 3]
print(f"{len(solid)} with non-negligible solid volume\n")
for op in ['union', 'intersection', 'difference']:
    empty = sum(r[f'bool_{op}_tris'] == 0 for r in ok)
    open_ = [r['file'] for r in ok if r[f'bool_{op}_tris'] > 0 and not r[f'bool_{op}_closed']]
    bowt = sum(r[f'bool_{op}_bowties'] > 0 for r in ok)
    print(f"{op:13} empty {empty}, not closed {len(open_)} {open_[:5]}, with bowties {bowt}")
def pct(xs, q): xs = sorted(xs); return xs[min(len(xs) - 1, int(q * len(xs)))]
for key, name in [('bool_ie_error', 'V(AuB)+V(AnB) vs V(A)+V(B)'), ('bool_diff_error', 'V(A-B) vs V(A)-V(AnB)')]:
    e = [100 * r[key] for r in solid]
    print(f"\n{name}: median {st.median(e):.2f}%, p90 {pct(e, .9):.2f}%, p99 {pct(e, .99):.2f}%, max {max(e):.2f}%")
    print("  worst:", sorted(((round(100 * r[key], 2), r['file']) for r in solid), reverse=True)[:5])
# noise floor: A and its rigid copy B should have the same volume
n = [100 * abs(r['bool_vA'] - r['bool_vB']) / max(r['bool_vA'], 1e-12) for r in solid]
print(f"\nnoise floor |V(A)-V(B)|/V(A) (B is a rigid copy): median {st.median(n):.2f}%, p90 {pct(n, .9):.2f}%")
t = [r['bool_ms'] / 1000 for r in ok]
print(f"time for A, B, union, intersection, difference: median {st.median(t):.1f} s, max {max(t):.1f} s")
