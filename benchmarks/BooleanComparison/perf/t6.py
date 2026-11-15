# T6 of Exact Boolean 3.0 (docs/roadmap/exact-boolean.md): time ratio ours / CGAL EPECK on 1k-100k triangles.
# Helper of .github/workflows/t6-cgal.yml; no meshes are part of the repository, they are fetched at run time.
#
#   python3 perf/t6.py pairs <out.csv>                   the pairs of the case set (see below) as a pair list
#   python3 perf/t6.py fetch <dir> <pairs.csv>           the models of the pair list -> <dir>/meshes (huggingface)
#   python3 perf/t6.py report <label=results.jsonl>...   Markdown tables: ratios, clean rates, refusals, worst 5
#
# Case set: the fixed 100-pair CGAL sample (thingi-pairs/pairs_cgal_sample.csv) restricted to pairs whose operands
# have 1k-100k triangles in total (A + B); if that leaves fewer than MIN_PAIRS, pairs of pairs.csv of that size class
# are added in a fixed order of SplitMix64 seed SEED_TOPUP. Size buckets: A + B in 1k-10k and 10k-100k.
import csv, json, os, statistics as st, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BC = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(BC, "data"))
import thingi_pairs as tp   # noqa: E402

LO, HI = 1000, 100000
BUCKETS = [("1k-10k", 1000, 10000), ("10k-100k", 10000, 100001)]
MIN_PAIRS = 50
SEED_TOPUP = 20261115
OPS = ["union", "intersection", "difference"]

def read_pairs(name):
    with open(os.path.join(tp.LISTS, name)) as f:
        return list(csv.DictReader(l for l in f if not l.startswith("#")))

def tris_of():
    with open(os.path.join(tp.LISTS, "clean_models.csv")) as f:
        return {r["id"]: int(r["tris"]) for r in csv.DictReader(l for l in f if not l.startswith("#"))}

def size_of(r, tris):
    return tris[r["a"]] + tris[r["b"]]

def select(sample, full, tris, min_pairs=MIN_PAIRS, seed=SEED_TOPUP):
    """sample pairs of 1k-100k triangles in total, topped up from the full list with a fixed seed"""
    keep = [r for r in sample if LO <= size_of(r, tris) <= HI]
    if len(keep) >= min_pairs:
        return keep
    have = {r["pair"] for r in keep}
    rest = [r for r in full if r["pair"] not in have and LO <= size_of(r, tris) <= HI]
    rng, extra = tp.SplitMix64(seed), []
    while rest and len(keep) + len(extra) < min_pairs:   # partial Fisher-Yates, as the sample
        i = rng.below(len(rest))
        extra.append(rest.pop(i))
    return keep + extra

def cmd_pairs(out):
    rows = select(read_pairs("pairs_cgal_sample.csv"), read_pairs("pairs.csv"), tris_of())
    with open(out, "w", newline="") as f:
        f.write("# T6 case set: " + str(len(rows)) + " pairs\n")
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()), lineterminator="\n")
        w.writeheader()
        w.writerows(rows)
    print(len(rows), "pairs ->", out)

def cmd_fetch(d, pairs):
    os.makedirs(d, exist_ok=True)
    files = tp.file_list(d)
    with open(pairs) as f:
        rows = list(csv.DictReader(l for l in f if not l.startswith("#")))
    ids = sorted({r[k] for r in rows for k in ("a", "b")}, key=int)
    with tp.ThreadPoolExecutor(4) as ex:
        list(ex.map(lambda k: tp.fetch_file(f"{tp.BASE}/resolve/main/{files[k]}",
                                            os.path.join(d, "meshes", os.path.basename(files[k]))), ids))
    print(len(ids), "models fetched")

# --- report

def pct(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p / 100 * len(v)))] if v else float("nan")

def load(path):
    last = {}
    for l in open(path):
        if l.strip():
            r = json.loads(l)
            if r.get("method") != "export":
                last[(r["file"], r["method"])] = r      # the last row of a case and method wins (--resume)
    return last

def bucket_of(file, tris):
    ids = file.split("_", 1)[1].split("+")
    n = tris[ids[0]] + tris[ids[1]]
    for name, lo, hi in BUCKETS:
        if lo <= n < hi:
            return name
    return None

def analyse(rows, tris):
    """rows: {(file, method): row}. -> per bucket {ratios, per method counters}, and all ratio records"""
    out, recs = {}, []
    files = sorted({f for f, _ in rows})
    for f in files:
        b = bucket_of(f, tris)
        if b is None:
            continue
        o = out.setdefault(b, {"pairs": 0, "ratios": [], "m": {m: dict(ops=0, ok=0, clean=0, empty=0, unclean={}, rejected=0, bad=0, timeout=0)
                                                              for m in ("ours", "cgal_epeck")}})
        o["pairs"] += 1
        for m in ("ours", "cgal_epeck"):
            r = rows.get((f, m))
            c = o["m"][m]
            for op in OPS:
                c["ops"] += 1
                s = r.get(f"{op}_status") if r else "missing"
                if s == "rejected": c["rejected"] += 1
                elif s == "timeout": c["timeout"] += 1
                elif s != "ok": c["bad"] += 1
                else:
                    c["ok"] += 1
                    reason = r.get(f"{op}_clean_reason")
                    if reason == "": c["clean"] += 1
                    elif reason == "empty": c["empty"] += 1
                    else: c["unclean"][reason] = c["unclean"].get(reason, 0) + 1
        for op in OPS:
            a, g = rows.get((f, "ours")), rows.get((f, "cgal_epeck"))
            if a and g and a.get(f"{op}_status") == "ok" and g.get(f"{op}_status") == "ok" and g.get(f"{op}_ms"):
                ratio = a[f"{op}_ms"] / g[f"{op}_ms"]
                o["ratios"].append(ratio)
                recs.append(dict(bucket=b, file=f, op=op, ratio=ratio, ours=a[f"{op}_ms"], cgal=g[f"{op}_ms"],
                                 phases=a.get(f"{op}_phases"), tris=a.get(f"{op}_tris")))
    return out, recs

def report(label, rows, tris):
    out, recs = analyse(rows, tris)
    lines = [f"### {label}", "",
             "| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | "
             "CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |",
             "|" + "---|" * 16]
    for name, _, _ in BUCKETS:
        o = out.get(name)
        if not o:
            continue
        r, a, g = o["ratios"], o["m"]["ours"], o["m"]["cgal_epeck"]
        un = lambda c: ", ".join(f"{k} {v}" for k, v in sorted(c["unclean"].items())) or "0"
        lines.append(f"| {name} | {o['pairs']} | {len(r)} | {st.median(r):.2f} | {pct(r, 90):.2f} | {max(r):.2f} | {a['clean']}/{a['ops']} | {a['empty']} | {un(a)} | "
                     f"{a['rejected']} | {a['bad']} / {a['timeout']} | {g['clean']}/{g['ops']} | {g['empty']} | {un(g)} | {g['rejected']} | {g['bad']} / {g['timeout']} |")
    allr = [x for o in out.values() for x in o["ratios"]]
    ours = [o["m"]["ours"] for o in out.values()]
    allclean = all(c["ok"] == c["clean"] + c["empty"] and c["bad"] == c["timeout"] == c["rejected"] == 0 for c in ours)
    lines += ["", f"All buckets: median ratio {st.median(allr):.2f}, p90 {pct(allr, 90):.2f}, max {max(allr):.2f} over {len(allr)} operations.",
              f"All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **{'yes' if allclean else 'no'}**.", ""]
    worst = sorted(recs, key=lambda x: -x["ratio"])[:5]
    lines += ["Five worst ratios (ours phases in ms):", "", "| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |", "|---|---|---|---|---|---|---|---|"]
    for w in worst:
        ph = ", ".join(f"{k} {v:.0f}" for k, v in sorted((w["phases"] or {}).items(), key=lambda kv: -kv[1]) if isinstance(v, (int, float))) or "–"
        lines.append(f"| {w['file']} | {w['op']} | {w['bucket']} | {w['ours']} | {w['cgal']} | {w['ratio']:.2f} | {w['tris']} | {ph} |")
    return "\n".join(lines) + "\n"

def cmd_report(args):
    tris = tris_of()
    for a in args:
        label, path = a.split("=", 1)
        print(report(label, load(path), tris))

if __name__ == "__main__":
    a = sys.argv[1:]
    if a[:1] == ["pairs"]: cmd_pairs(a[1])
    elif a[:1] == ["fetch"]: cmd_fetch(a[1], a[2])
    elif a[:1] == ["report"]: cmd_report(a[1:])
    else: sys.exit(__doc__)
