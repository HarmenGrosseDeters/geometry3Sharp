# usage: analyze_c7_rounds.py <results dir>   per-round values and paired differences against base
import json, os, statistics, sys
root = sys.argv[1]
def val(mode, name, setting, r):
    f = os.path.join(root, "%s_%s_%s_%d.jsonl" % (mode, name, setting, r))
    rows = [json.loads(l) for l in open(f) if l.strip()]
    tot = sum(x["ms"] for x in rows) / 1000
    cls = sum(x["phases_ms"].get("classification", 0) for x in rows) / 1000
    gc = sum(x["gc_pause_ms"] for x in rows) / 1000
    return tot, cls, gc
for mode in ("one", "mt"):
    for name in ("t107554", "t42327", "npairs", "ncopy"):
        print("==", mode, name)
        for r in range(1, 6):
            v = {s: val(mode, name, s, r) for s in ("base", "carried", "carcen")}
            print("  round %d  total %s   classification %s   rest %s" % (r,
                " ".join("%7.2f" % v[s][0] for s in v), " ".join("%7.2f" % v[s][1] for s in v), " ".join("%7.2f" % (v[s][0] - v[s][1]) for s in v)))
        for s in ("carried", "carcen"):
            d = []; dc = []; dr = []
            for r in range(1, 6):
                b = val(mode, name, "base", r); x = val(mode, name, s, r)
                d.append(100 * (x[0] / b[0] - 1)); dc.append(100 * (x[1] / b[1] - 1)); dr.append(100 * ((x[0] - x[1]) / (b[0] - b[1]) - 1))
            print("  %-8s paired total %+.1f %% (%+.1f..%+.1f)  classification %+.1f %% (%+.1f..%+.1f)  rest %+.1f %% (%+.1f..%+.1f)" % (s,
                statistics.median(d), min(d), max(d), statistics.median(dc), min(dc), max(dc), statistics.median(dr), min(dr), max(dr)))
