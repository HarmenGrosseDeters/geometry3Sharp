# Summarises a results .jsonl: repair status per category, exceptions, triangle loss.
import json, csv, collections, os, sys
sel = {r['path'].split('/')[1]: r['category'] for f in ['data/selection.csv', 'data/selection_large.csv'] if os.path.exists(f) for r in csv.DictReader(open(f))}
rows = [json.loads(l) for l in open(sys.argv[1] if len(sys.argv) > 1 else 'results/default.jsonl') if l.strip()]
for r in rows: r['category'] = sel.get(r['file'], '?')
print(f"{len(rows)} meshes\n")
print("Status overall:", dict(collections.Counter(r['status'] for r in rows)))
print("\nStatus per category:")
cats = sorted(set(r['category'] for r in rows))
sts = ['closed', 'not_closed', 'emptied', 'exception', 'timeout_or_crash', 'load_failed']
print(f"{'category':32}" + "".join(f"{s:>17}" for s in sts))
for c in cats:
    cc = collections.Counter(r['status'] for r in rows if r['category'] == c)
    print(f"{c:32}" + "".join(f"{cc.get(s,0):>17}" for s in sts))
print("\nExceptions (grouped by message + top frame):")
ex = collections.Counter((r['error'][:90], r.get('trace', '').split(' | ')[0]) for r in rows if r['status'] == 'exception')
for (e, t), n in ex.most_common(): print(f"  {n:3}x  {e}\n        at {t}")
print("\nInvalid result meshes:", [r['file'] for r in rows if r.get('after_valid') is False])
lost = [(r['file'], r['before_tris'], r['after_tris']) for r in rows if 'after_tris' in r and r['after_tris'] < 0.9 * r['before_tris']]
print(f"\nLost >10% of triangles: {len(lost)}", lost[:15])
print("Clean controls changed:", [r['file'] for r in rows if r['category'] == 'clean' and r.get('after_tris') != r.get('before_tris')])
slow = sorted((r.get('repair_ms', 0), r['file']) for r in rows)[-5:]
print("Slowest:", slow)
