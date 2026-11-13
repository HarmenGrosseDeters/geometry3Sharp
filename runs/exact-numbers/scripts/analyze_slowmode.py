import json, sys, glob, os, re, statistics
root = sys.argv[1]
series = [("d2","mt","107554 first, default"),("d2","rev","42327 first, default"),("d2","nopgo","107554 first, TieredPGO=0"),
          ("d2","dflt42327","42327 alone, default"),("d2","nc42327","42327 alone, gcConcurrent=0"),
          ("d4","dflt","107554 first, default (2nd day part)"),("d4","cap4","107554 first, pool capped at 4")]
def rows(f):
    out = []
    for line in open(f):
        line = line.strip()
        if line: out.append(json.loads(line))
    return out
for d, pre, title in series:
    print("==", title)
    for b in ("base","new"):
        files = sorted(glob.glob(os.path.join(root, d, pre + "_" + b + "_*.jsonl")), key=lambda f: int(re.findall(r"_(\d+)\.jsonl", f)[0]))
        per = []
        for f in files:
            r = [x for x in rows(f) if "42327" in x["case"]]
            if not r: continue
            cls = [x["phases_ms"]["classification"] / 1000.0 for x in r]
            rest = [(x["ms"] - x["phases_ms"]["classification"]) / 1000.0 for x in r]
            cpu = [x["cpu_ms"] / 1000.0 for x in r]
            per.append((statistics.median(cls), statistics.median(rest), statistics.median(cpu), cls))
        print("  %-4s cls per process (median of the 3 ops): %s" % (b, " ".join("%.1f" % p[0] for p in per)))
        print("       rest of the operation:                %s" % " ".join("%.1f" % p[1] for p in per))
        print("       cpu per op:                           %s" % " ".join("%.1f" % p[2] for p in per))
