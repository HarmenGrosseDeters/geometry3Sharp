import json, sys, glob, os, re
root = sys.argv[1]
files = sorted(glob.glob(os.path.join(root, "d2", "*.jsonl")) + glob.glob(os.path.join(root, "d4", "*.jsonl")))
recs = []
for f in files:
    name = os.path.basename(f)
    if not (name.startswith(("mt_", "rev_", "nopgo_", "dflt42327_", "nc42327_", "dflt_", "cap4_"))): continue
    for line in open(f):
        line = line.strip()
        if not line: continue
        x = json.loads(line)
        if "42327" not in x["case"]: continue
        recs.append((name, x))
keys = set()
for n, x in recs: keys |= set(x.get("counts", {}).keys())
# which counts vary across processes for the same op and binary?
import collections
for op in (0,):
    for b in ("base", "new"):
        sel = [(n, x) for n, x in recs if x["op"] == op and ("_" + b + "_") in n]
        print("op", op, b, "processes", len(sel))
        for k in sorted(keys):
            vals = [x["counts"].get(k) for n, x in sel]
            if len(set(vals)) > 1:
                print("   varies:", k, min(vals), max(vals))
        print("   hashes:", collections.Counter(x["hash"] for n, x in sel))
        for n, x in sorted(sel, key=lambda t: t[1]["phases_ms"]["classification"]):
            c = x["counts"]
            print("   %-24s cls %5.1f cpu %5.1f  %s" % (n, x["phases_ms"]["classification"]/1000.0, x["cpu_ms"]/1000.0,
                  " ".join("%s=%s" % (k.replace("winding_plane_sides","wps").replace("l2_",""), c.get(k)) for k in ("winding_plane_sides","winding_plane_sides_exact","l2_traced","l2_direct","l2_carried","l2_cross_steps","l2_cross_fallbacks","l2_cross_candidates","l2_clusters","l2_exact_centroids"))))
