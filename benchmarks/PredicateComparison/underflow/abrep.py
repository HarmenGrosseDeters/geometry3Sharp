import json, sys, collections
rows = [json.loads(l) for l in open(sys.argv[1])]
ph = sys.argv[2].split(',') if len(sys.argv) > 2 else ['intersections.pairs']
by = collections.defaultdict(dict)
tools = []
for r in rows:
    by[r['case']][r['tool']] = r
    if r['tool'] not in tools: tools.append(r['tool'])
A, B = tools[:2]
tot = collections.Counter()
for c, d in by.items():
    if A not in d or B not in d: continue
    a, b = d[A], d[B]
    ma, mb = sorted(a['ms'])[len(a['ms'])//2], sorted(b['ms'])[len(b['ms'])//2]
    pa = sum(a['phases_ms'].get(p, 0) for p in ph); pb = sum(b['phases_ms'].get(p, 0) for p in ph)
    tot['ma'] += ma; tot['mb'] += mb; tot['pa'] += pa; tot['pb'] += pb
    tot['aa'] += a['alloc_mb']; tot['ab'] += b['alloc_mb']; tot['ga'] += a['gc']['pause_ms']; tot['gb'] += b['gc']['pause_ms']
    print("%-22s wall %6d %6d (%+5.1f%%)  phase %5d %5d (%+5.1f%%)  alloc %7.1f %7.1f  gc %4d %4d  %s" % (c[-22:], ma, mb, 100*(mb-ma)/max(ma,1), pa, pb, 100*(pb-pa)/max(pa,1), a['alloc_mb'], b['alloc_mb'], a['gc']['pause_ms'], b['gc']['pause_ms'], 'same' if a['hash']==b['hash'] else 'HASH DIFF'))
print("SUM wall %d %d (%+.1f%%)  phase %d %d (%+.1f%%) alloc %.0f %.0f gc %d %d" % (tot['ma'], tot['mb'], 100*(tot['mb']-tot['ma'])/tot['ma'], tot['pa'], tot['pb'], 100*(tot['pb']-tot['pa'])/max(tot['pa'],1), tot['aa'], tot['ab'], tot['ga'], tot['gb']))
