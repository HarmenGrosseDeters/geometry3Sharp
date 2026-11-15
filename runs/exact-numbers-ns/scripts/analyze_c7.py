# usage: analyze_c7.py <results dir> <setting> [<setting> ...]   (the first setting is the base)
import json, os, statistics, sys
root = sys.argv[1]; settings = sys.argv[2:]
med = statistics.median
def load(mode, name, setting):
    out = []
    for r in range(1, 9):
        f = os.path.join(root, "%s_%s_%s_%d.jsonl" % (mode, name, setting, r))
        if not os.path.exists(f): continue
        rows = [json.loads(l) for l in open(f) if l.strip()]
        if rows: out.append(rows)
    return out
for mode, title in (("one", "one thread (core 0, no concurrent GC)"), ("mt", "4 cores")):
    print("==", title)
    for name in ("t107554", "t42327", "npairs", "ncopy"):
        base = load(mode, name, settings[0])
        if not base: continue
        b = None
        for setting in settings:
            runs = load(mode, name, setting)
            if not runs: continue
            tot = [sum(x["ms"] for x in rows) / 1000 for rows in runs]
            gc = [sum(x["gc_pause_ms"] for x in rows) / 1000 for rows in runs]
            al = [sum(x["alloc_mb"] for x in rows) / 1024 for rows in runs]
            cls = [sum(x["phases_ms"].get("classification", 0) for x in rows) / 1000 for rows in runs]
            eq = all(len(rows) == len(base[0]) and all(a["hash"] == c["hash"] and a["status"] == c["status"] for a, c in zip(rows, base[0])) for rows in runs)
            if b is None: b = med(tot)
            print("  %-8s %-8s runs %d  time %.2f s (%.2f-%.2f) %+.1f %%  classification %.2f s (%.2f-%.2f)  GC %.2f s  alloc %.2f GB  hashes equal %s" % (
                name, setting, len(runs), med(tot), min(tot), max(tot), 100 * (med(tot) / b - 1), med(cls), min(cls), max(cls), med(gc), med(al), eq))
