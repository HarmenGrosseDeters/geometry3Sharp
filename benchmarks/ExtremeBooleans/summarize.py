# Summary table of run.sh output: one row per case and operation with total (sum of the top-level phases), wall time,
# peak RSS, the dominant stage and its share, the second stage, status, rounding_clean, the rounding limit and alloc.
#   python3 summarize.py <inventory.jsonl> [--md]
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1])]
md = "--md" in sys.argv
hdr = ["case", "op", "total s", "wall s", "peak RSS MB", "dominant", "share", "second", "status", "clean", "limit", "alloc GB"]
if md: print("| " + " | ".join(hdr) + " |\n|" + "---|" * len(hdr))
for d in rows:
    p = {k: v for k, v in d.get("phases_ms", {}).items() if "." not in k and k != "input"}
    tot = sum(p.values())
    top = sorted(((v, k) for k, v in p.items()), reverse=True)
    dom = top[0] if top else (0, "-"); sec = top[1] if len(top) > 1 else (0, "-")
    st = d.get("error") or d.get("status", "?")
    r = [d["id"], d["op"], "%.1f" % (tot / 1000), "%.1f" % d["wall_s"], str(d["peak_rss_mb"]), dom[1],
         "%d %%" % (100 * dom[0] / tot) if tot else "-", "%s %d %%" % (sec[1], 100 * sec[0] / tot) if tot else "-", st,
         str(d.get("rounding_clean", "-")), str(d.get("rounding_limit", "-")), "%.1f" % (d.get("alloc_mb", 0) / 1024)]
    print("| " + " | ".join(r) + " |" if md else "\t".join(r))
