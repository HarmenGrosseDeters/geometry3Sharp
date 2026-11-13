import json, sys, collections
rows = [json.loads(l) for l in open(sys.argv[1])]
by = collections.defaultdict(dict)
for r in rows: by[r['case']][r['tool']] = r
med = lambda r: sorted(r['ms'])[len(r['ms'])//2]
tot = collections.Counter(); n = 0; worse = []; hashok = True
for c, d in by.items():
    if len(d) < 3: continue
    n += 1
    for k, r in d.items():
        tot['wall_'+k] += med(r); tot['gc_'+k] += r['gc']['pause_ms']; tot['alloc_'+k] += r['alloc_mb']
    if len({r['hash'] for r in d.values()}) > 1: hashok = False
    worse.append((c, med(d['default']), med(d['gen0']), med(d['server'])))
print('cases', n, 'hashes equal' if hashok else 'HASH DIFF')
for k in ('default', 'gen0', 'server'):
    print("%-8s wall %7d (%+.1f%%)  gc pause %6d (%.1f%% of wall)  alloc %.0f MB" % (k, tot['wall_'+k], 100*(tot['wall_'+k]/tot['wall_default']-1), tot['gc_'+k], 100*tot['gc_'+k]/tot['wall_'+k], tot['alloc_'+k]))
import statistics
for k, i in (('gen0', 2), ('server', 3)):
    rel = [w[i]/w[1]-1 for w in worse]
    print(k, 'per-case median change %+.1f%%' % (100*statistics.median(rel)), 'best', '%+.1f%%' % (100*min(rel)), 'worst', '%+.1f%%' % (100*max(rel)))
for w in sorted(worse, key=lambda w: w[2]/w[1])[:6]: print('  gain', w)
