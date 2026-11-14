# per case: all runs of all rounds pooled, median per tool; sums; GC and alloc as means over rounds; hashes
import json, sys, statistics as st, collections
A, B = sys.argv[1], sys.argv[2]; files = sys.argv[3:]
ms = collections.defaultdict(lambda: collections.defaultdict(list)); gc = collections.defaultdict(lambda: collections.defaultdict(list))
al = collections.defaultdict(lambda: collections.defaultdict(list)); hs = collections.defaultdict(set)
for f in files:
    for l in open(f):
        x = json.loads(l); c = x['case']; t = x['tool']
        ms[c][t] += x['ms']; gc[c][t].append(x['gc']['pause_ms']); al[c][t].append(x['alloc_mb']); hs[c].add(x['hash'])
tot = collections.Counter(); slower = []
for c in sorted(ms):
    a, b = st.median(ms[c][A]), st.median(ms[c][B])
    tot['a'] += a; tot['b'] += b
    tot['ga'] += st.mean(gc[c][A]); tot['gb'] += st.mean(gc[c][B]); tot['aa'] += st.mean(al[c][A]); tot['ab'] += st.mean(al[c][B])
    if b > a * 1.05: slower.append((c, a, b))
    if len(hs[c]) != 1: print('HASH DIFF', c)
print("cases %d  wall %.0f -> %.0f (%+.1f%%)  gc pause %.0f -> %.0f (%+.1f%%)  alloc MB %.0f -> %.0f (%+.1f%%)" % (len(ms), tot['a'], tot['b'], 100*(tot['b']/tot['a']-1), tot['ga'], tot['gb'], 100*(tot['gb']/max(tot['ga'],1)-1), tot['aa'], tot['ab'], 100*(tot['ab']/tot['aa']-1)))
for s in slower: print('  >5%% slower: %s %.0f %.0f' % s)
