# T6 of Exact Boolean 3.0 (docs/roadmap/exact-boolean.md): time ratio ours / CGAL EPECK on 1k-100k triangles, and the same case set
# against Geogram (docs/research/GEOGRAM_BOOLEAN_COMPARISON_2026-11.md).
# Helper of .github/workflows/t6-cgal.yml; no meshes are part of the repository, they are fetched at run time.
#
#   python3 perf/t6.py pairs <out.csv>                   the pairs of the case set (see below) as a pair list
#   python3 perf/t6.py fetch <dir> <pairs.csv>           the models of the pair list -> <dir>/meshes (huggingface)
#   python3 perf/t6.py report [--other M] <label=results.jsonl>...   Markdown tables: ratios ours / M, clean rates, refusals,
#                                                        volume agreement, worst 5 (M: cgal_epeck, the default, or geogram)
#
# Case set: the fixed 100-pair CGAL sample (thingi-pairs/pairs_cgal_sample.csv) restricted to pairs whose operands
# have 1k-100k triangles in total (A + B); if that leaves fewer than MIN_PAIRS, pairs of pairs.csv of that size class
# are added in a fixed order of SplitMix64 seed SEED_TOPUP. Size buckets: A + B in 1k-10k and 10k-100k.
import csv, json, math, os, statistics as st, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BC = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(BC, "data"))
import thingi_pairs as tp   # noqa: E402

LO, HI = 1000, 100000
BUCKETS = [("1k-10k", 1000, 10000), ("10k-100k", 10000, 100001)]
MIN_PAIRS = 50
SEED_TOPUP = 20261115
OPS = ["union", "intersection", "difference"]
NAMES = {"cgal_epeck": "CGAL", "geogram": "Geogram", "manifold": "Manifold"}
# volumes of two results agree if they differ by at most this, relative to the volume of the inputs (vA + vB): rounding the result
# to doubles moves a volume by far less, a wrong classification of a part by about its volume
VOL_TOL = 1e-6
# a time below this (a fast operation, timed in whole ms) counts as this, so that no ratio divides by 0 and no fast case is dropped
MIN_MS = 0.5

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

def vol_diff(a, g, op):
    """|volume of ours - volume of the other| / (vA + vB), or None if a volume or the input volumes are missing"""
    va, vg = a.get(f"{op}_volume"), g.get(f"{op}_volume")
    inp = a.get("input") or {}
    scale = (inp.get("vA") or 0) + (inp.get("vB") or 0)
    if va is None or vg is None or not scale > 0 or not math.isfinite(va) or not math.isfinite(vg):
        return None
    return abs(va - vg) / scale

def clean_or_empty(r, op):
    return r.get(f"{op}_clean_reason") in ("", "empty")

def analyse(rows, tris, other="cgal_epeck"):
    """rows: {(file, method): row}. -> per bucket {ratios, volume differences, per method counters}, and all ratio records"""
    out, recs = {}, []
    files = sorted({f for f, _ in rows})
    for f in files:
        b = bucket_of(f, tris)
        if b is None:
            continue
        o = out.setdefault(b, {"pairs": 0, "ratios": [], "clean_ratios": [], "vdiff": [], "m": {m: dict(ops=0, ok=0, clean=0, empty=0, unclean={}, rejected=0, bad=0, timeout=0)
                                                                            for m in ("ours", other)}})
        o["pairs"] += 1
        for m in ("ours", other):
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
            a, g = rows.get((f, "ours")), rows.get((f, other))
            if a and g and a.get(f"{op}_status") == "ok" and g.get(f"{op}_status") == "ok":
                dv = vol_diff(a, g, op)
                if dv is not None:
                    o["vdiff"].append(dv)
                if g.get(f"{op}_ms") is not None and a.get(f"{op}_ms") is not None:
                    ratio = a[f"{op}_ms"] / max(g[f"{op}_ms"], MIN_MS)
                    o["ratios"].append(ratio)
                    if clean_or_empty(a, op) and clean_or_empty(g, op):
                        o["clean_ratios"].append(ratio)
                    recs.append(dict(bucket=b, file=f, op=op, ratio=ratio, ours=a[f"{op}_ms"], other=g[f"{op}_ms"],
                                     phases=a.get(f"{op}_phases"), tris=a.get(f"{op}_tris"), vdiff=dv))
    return out, recs

def all_clean(counters):
    return sum(c["ops"] for c in counters) > 0 and all(c["ok"] == c["clean"] + c["empty"] and c["bad"] == c["timeout"] == c["rejected"] == 0 for c in counters)

def report(label, rows, tris, other="cgal_epeck"):
    out, recs = analyse(rows, tris, other)
    n = NAMES.get(other, other)
    lines = [f"### {label}", "",
             f"Ratio: time of ours / time of {n} (below 1: ours is faster). Volumes agree: |difference| <= {VOL_TOL:g} (vA + vB).", "",
             "| bucket | pairs | ops (both ok) | median ratio | p90 | max | median ratio, both clean (n) | volumes agree | max volume diff | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | "
             f"{n} clean | {n} empty | {n} unclean | {n} refused | {n} failed / timeout |",
             "|" + "---|" * 19]
    for name, _, _ in BUCKETS:
        o = out.get(name)
        if not o:
            continue
        r, a, g = o["ratios"], o["m"]["ours"], o["m"][other]
        un = lambda c: ", ".join(f"{k} {v}" for k, v in sorted(c["unclean"].items())) or "0"
        med = lambda v, f: f(v) if v else float("nan")
        vd = o["vdiff"]
        cr = o["clean_ratios"]
        lines.append(f"| {name} | {o['pairs']} | {len(r)} | {med(r, st.median):.2f} | {pct(r, 90):.2f} | {med(r, max):.2f} | {med(cr, st.median):.2f} ({len(cr)}) | "
                     f"{sum(1 for x in vd if x <= VOL_TOL)}/{len(vd)} | {med(vd, max):.1e} | {a['clean']}/{a['ops']} | {a['empty']} | {un(a)} | "
                     f"{a['rejected']} | {a['bad']} / {a['timeout']} | {g['clean']}/{g['ops']} | {g['empty']} | {un(g)} | {g['rejected']} | {g['bad']} / {g['timeout']} |")
    allr = [x for o in out.values() for x in o["ratios"]]
    ours = [o["m"]["ours"] for o in out.values()]
    allclean = all_clean(ours)
    other_clean = all_clean([o["m"][other] for o in out.values()])
    allv = [x for o in out.values() for x in o["vdiff"]]
    lines += ["", f"All buckets: median ratio {st.median(allr) if allr else float('nan'):.2f}, p90 {pct(allr, 90):.2f}, "
                  f"max {max(allr) if allr else float('nan'):.2f} over {len(allr)} operations; volumes agree in "
                  f"{sum(1 for x in allv if x <= VOL_TOL)} of {len(allv)}.",
              f"All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **{'yes' if allclean else 'no'}**; "
              f"of {n}: **{'yes' if other_clean else 'no'}**.", ""]
    worst = sorted(recs, key=lambda x: -x["ratio"])[:5]
    lines += ["Five worst ratios (ours phases in ms):", "", f"| pair | op | bucket | ours ms | {n} ms | ratio | result tris | phases |", "|---|---|---|---|---|---|---|---|"]
    for w in worst:
        ph = ", ".join(f"{k} {v:.0f}" for k, v in sorted((w["phases"] or {}).items(), key=lambda kv: -kv[1]) if isinstance(v, (int, float))) or "–"
        lines.append(f"| {w['file']} | {w['op']} | {w['bucket']} | {w['ours']} | {w['other']} | {w['ratio']:.2f} | {w['tris']} | {ph} |")
    return "\n".join(lines) + "\n"

def cmd_report(args):
    other = "cgal_epeck"
    if args[:1] == ["--other"]:
        other, args = args[1], args[2:]
    tris = tris_of()
    for a in args:
        label, path = a.split("=", 1)
        print(report(label, load(path), tris, other))
    vols = volume_disagreements([load(a.split("=", 1)[1]) for a in args], other)
    if vols:
        n = NAMES.get(other, other)
        print(f"Volumes that disagree (> {VOL_TOL:g} of vA + vB), in any of the runs above:\n\n| pair | op | ours volume | {n} volume | diff / (vA + vB) |\n|---|---|---|---|---|")
        for f, op, va, vg, dv in vols:
            print(f"| {f} | {op} | {va:.9g} | {vg:.9g} | {dv:.1e} |")
        print()

def volume_disagreements(runs, other):
    """(file, op, ours volume, other volume, diff) of every operation, both ok, whose volumes disagree, first run that shows it"""
    seen, out = set(), []
    for rows in runs:
        for (f, m), a in sorted(rows.items()):
            g = rows.get((f, other))
            if m != "ours" or not g:
                continue
            for op in OPS:
                if (f, op) in seen or a.get(f"{op}_status") != "ok" or g.get(f"{op}_status") != "ok":
                    continue
                dv = vol_diff(a, g, op)
                if dv is not None and dv > VOL_TOL:
                    seen.add((f, op))
                    out.append((f, op, a[f"{op}_volume"], g[f"{op}_volume"], dv))
    return out

if __name__ == "__main__":
    a = sys.argv[1:]
    if a[:1] == ["pairs"]: cmd_pairs(a[1])
    elif a[:1] == ["fetch"]: cmd_fetch(a[1], a[2])
    elif a[:1] == ["report"]: cmd_report(a[1:])
    else: sys.exit(__doc__)
