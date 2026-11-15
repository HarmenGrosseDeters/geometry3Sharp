import json, os, statistics, sys
root = sys.argv[1]
med = statistics.median
def load(mode, name, setting):
    out = []
    for r in (1, 2, 3):
        f = os.path.join(root, "%s_%s_%s_%d.jsonl" % (mode, name, setting, r))
        if not os.path.exists(f): continue
        rows = [json.loads(l) for l in open(f) if l.strip()]
        out.append(rows)
    return out
for mode, title in (("one", "one thread (core 0, no concurrent GC)"), ("mt", "4 cores")):
    print("==", title)
    for name in ("t107554", "t42327", "npairs", "ncopy"):
        base = load(mode, name, "base")
        if not base: continue
        line = []
        for setting in ("base", "fast", "fastcen"):
            runs = load(mode, name, setting)
            if not runs: continue
            tot = [sum(x["ms"] for x in rows) / 1000 for rows in runs]
            gc = [sum(x["gc_pause_ms"] for x in rows) / 1000 for rows in runs]
            al = [sum(x["alloc_mb"] for x in rows) / 1024 for rows in runs]
            cls = [sum(x["phases_ms"].get("classification", 0) for x in rows) / 1000 for rows in runs]
            eq = all(len(rows) == len(base[0]) and all(a["hash"] == b["hash"] and a["status"] == b["status"] for a, b in zip(rows, base[0])) for rows in runs)
            line.append((setting, tot, gc, al, cls, eq, len(runs)))
        b = med(line[0][1])
        for setting, tot, gc, al, cls, eq, n in line:
            print("  %-8s %-8s runs %d  time %.2f s (%.2f-%.2f) %+.1f %%  classification %.2f s  GC %.2f s  alloc %.2f GB  hashes equal %s" % (
                name, setting, n, med(tot), min(tot), max(tot), 100 * (med(tot) / b - 1), med(cls), med(gc), med(al), eq))
