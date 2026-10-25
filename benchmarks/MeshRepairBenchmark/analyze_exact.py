# Summarises results of BENCH_REPAIR=exact: ExactMeshBoolean of each mesh with a rotated, shifted copy of itself.
# Usage: python3 analyze_exact.py results/exact.jsonl [data/selection.csv]
import collections, csv, json, statistics as st, sys
rows = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
cat = {}
try:
    for r in csv.DictReader(open(sys.argv[2] if len(sys.argv) > 2 else 'data/selection.csv')):
        cat[r['path'].split('/')[-1]] = r['category']
except FileNotFoundError:
    pass
ops = ['union', 'intersection', 'difference']

def outcome(r):
    if r.get('status') in ('timeout_or_crash', 'exception', 'load_failed'):
        return r['status'] + (': ' + r.get('error', '')[:70] if r.get('error') else '')
    if 'exact_error' in r:
        return 'refused: ' + r['exact_error']
    kind = 'exact winding' if r.get('exact_windings_exact', True) else 'generalized winding (open input)'
    wbad = [op for op in ops if not r.get(f'exact_{op}_wclosed', r.get(f'exact_{op}_closed', False))]
    bad = [op for op in ops if not r.get(f'exact_{op}_closed', False)]
    si = [op for op in ops if r.get(f'exact_{op}_selfint', 0) > 0]
    if wbad: return kind + ': not closed'
    if bad: return kind + ': closed, non-manifold edges'
    if si: return kind + ': closed, self-intersections'
    return kind + ': closed, clean'

by = collections.Counter(outcome(r) for r in rows)
print(f"{len(rows)} meshes")
for k, v in by.most_common():
    print(f"  {v:4}  {k}")

print("\nper category (clean / with results / all):")
cats = collections.defaultdict(list)
for r in rows:
    cats[cat.get(r['file'], '?')].append(r)
for c, rs in sorted(cats.items()):
    clean = sum(outcome(r).endswith('closed, clean') for r in rs)
    res = sum('exact_ms' in r for r in rs)
    print(f"  {c:30} {clean:3} / {res:3} / {len(rs):3}")

ok = [r for r in rows if 'exact_ms' in r]
if ok:
    def pct(xs, q): xs = sorted(xs); return xs[min(len(xs) - 1, int(q * len(xs)))]
    # volume identities only mean something for exact windings (closed inputs)
    solid = [r for r in ok if min(abs(r['exact_vA']), abs(r['exact_vB'])) > 0 and r.get('exact_windings_exact', True)]
    for key, name in [('exact_ie_error', 'V(AuB)+V(AnB) vs V(A)+V(B)'), ('exact_diff_error', 'V(A-B) vs V(A)-V(AnB)')]:
        e = [r[key] for r in solid]
        print(f"\n{name} (relative): median {st.median(e):.2e}, p90 {pct(e, .9):.2e}, max {max(e):.2e}")
        print("  worst:", [(f"{r[key]:.1e}", r["file"], cat.get(r["file"], "?")) for r in sorted(solid, key=lambda r: -r[key])[:5]])
    t = [r['exact_ms'] / 1000 for r in ok]
    print(f"\ntime for union + intersection + difference: median {st.median(t):.2f} s, p90 {pct(t, .9):.1f} s, max {max(t):.1f} s")
    print("slowest:", sorted(((round(r['exact_ms'] / 1000, 1), r['file'], r.get('before_tris', '?')) for r in ok), reverse=True)[:5])
fails = [r for r in rows if r.get('status') in ('exception', 'timeout_or_crash')]
if fails:
    print("\nexceptions / timeouts:")
    for msg, n in collections.Counter((r.get('error', r['status'])[:100], ) for r in fails).most_common(8):
        print(f"  {n:4}  {msg[0]}")
