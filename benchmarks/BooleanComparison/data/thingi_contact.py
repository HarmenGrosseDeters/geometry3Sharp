# The thingi-contact data class: pairs of Thingi10K models (or one model against itself) with deliberate exact contact
# (README.md, "Thingi10K pairs with exact contact"). Models are fetched at run time like the thingi-pairs class; only the
# pair list `thingi-pairs/contact_pairs.csv` is part of the repository.
#
#   python3 data/thingi_contact.py generate                       the list from clean_models.csv and the seed (no network)
#   python3 data/thingi_contact.py fetch <dir> [--limit N] [--variants v1,v2]   the models of the first N pairs of each variant -> <dir>/meshes
#   python3 data/thingi_contact.py manifest <dir> <out.jsonl> [--limit N] [--variants v1,v2]
#                                                                 the case manifest for compare.py (first N pairs per variant)
#   python3 data/thingi_contact.py summary results.jsonl...       per variant and method: status, kind of contact, outcomes
#
# `generate` is a pure function of clean_models.csv and the seed (SplitMix64 as thingi_pairs.py, fixed-format values).
import csv, json, os, sys
import statistics as st

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import thingi_pairs as tp   # noqa: E402

SEED = 20261116                  # the day of the owner's approval of the class
VARIANTS = ["face-contact", "partial-overlap", "ulp-shift", "ulp-shift-few", "mirror"]
SAME_MODEL = {"ulp-shift", "ulp-shift-few", "mirror"}     # A against a transformed copy of A: b == a
PAIRS_PER_VARIANT = 100          # the list; a run takes a prefix (the first run: 30)
CONTACT_TRIS = (1000, 30000)     # triangles per operand: the exact predicates are the point, not the size
PARAMS = {"face-contact": 1, "partial-overlap": 3, "ulp-shift": 1, "ulp-shift-few": 4, "mirror": 4}   # number of param values
HEADER = ["pair", "a", "b", "variant", "axis", "sign", "param"]
NAME = "contact_pairs.csv"
CLASS = "thingi-contact"

def pair_rows(models, variant, count):
    """count rows of a variant; the rows of a variant depend on the seed and the variant only (a prefix does not depend on count)"""
    rng = tp.SplitMix64(SEED + 7919 * VARIANTS.index(variant))
    rows = []
    for i in range(count):
        a = models[rng.below(len(models))]
        b = a
        if variant not in SAME_MODEL:
            while b == a:
                b = models[rng.below(len(models))]
        axis, sign, param = rng.below(3), 1 if rng.below(2) == 0 else -1, rng.below(PARAMS[variant])
        rows.append([f"{i:04d}", a, b, variant, axis, sign, param])
    return rows

def generate(clean=None, out=tp.LISTS):
    clean = clean or tp.read_clean()
    lo, hi = CONTACT_TRIS
    pool = sorted(k for k, n in clean if lo <= n <= hi)
    rows = [r for v in VARIANTS for r in pair_rows(pool, v, PAIRS_PER_VARIANT)]
    with open(os.path.join(out, NAME), "w", newline="") as f:
        f.write(f"# thingi-contact: {PAIRS_PER_VARIANT} pairs per variant, SplitMix64 seed {SEED} (+7919 x variant index), from the {len(pool)} clean models with {lo}-{hi} triangles of clean_models.csv (sorted by id)\n")
        f.write("# variant, axis (0 x, 1 y, 2 z), sign (+1 / -1: the side of A that B is put on, or the direction of the shift) and param (embedding depth 2^-param of partial-overlap, ulps - 2 of ulp-shift-few, rank of the mirror plane): ContactTransforms.cs\n")
        w = csv.writer(f, lineterminator="\n")
        w.writerow(HEADER)
        w.writerows(rows)

def read_list(path=None):
    path = path or os.path.join(tp.LISTS, NAME)
    with open(path) as f:
        return list(csv.DictReader(l for l in f if not l.startswith("#")))

def select(rows, limit=None, variants=None):
    """the first `limit` rows of each variant (all if None), in the order of the list"""
    count = {}
    out = []
    for r in rows:
        if variants and r["variant"] not in variants:
            continue
        count[r["variant"]] = count.get(r["variant"], 0) + 1
        if limit is None or count[r["variant"]] <= limit:
            out.append(r)
    return out

def cmd_fetch(d, limit, variants=None):
    os.makedirs(d, exist_ok=True)
    files = tp.file_list(d)
    ids = sorted({r[k] for r in select(read_list(), limit, variants) for k in ("a", "b")}, key=int)
    with tp.ThreadPoolExecutor(4) as ex:
        list(ex.map(lambda k: tp.fetch_file(f"{tp.BASE}/resolve/main/{files[k]}", os.path.join(d, "meshes", os.path.basename(files[k]))), ids))
    print(len(ids), "models fetched")

def cmd_manifest(d, out, limit, variants):
    rows = select(read_list(), limit, variants)
    with open(out, "w") as f:
        for r in rows:
            f.write(json.dumps({"class": CLASS, "name": f"{r['variant']}_p{r['pair']}_{r['a']}+{r['b']}",
                                "thingi_contact": [tp.mesh_path(d, r["a"]), tp.mesh_path(d, r["b"]), r["variant"], r["axis"], r["sign"], r["param"]]}) + "\n")
    print(len(rows), "cases ->", out)

def removed(r, op):
    """components that the rounding stage of ours removed from the result of op (None: not recorded, or another method)"""
    v = r.get(f"{op}_removed_components")
    if v is None:
        v = (r.get(f"{op}_counts") or {}).get("rounding_removed_components")      # rows of the first run (counts only)
    return v

def cmd_summary(files):
    """per variant and method: the outcomes as thingi_pairs.py summary, plus the kind of contact reached (face: planar faces on both
    sides; bbox: through vertices or edges only) and the placements refused as inexact"""
    rows = []
    for f in files:
        with open(f) as fh:
            rows += [json.loads(l) for l in fh if l.strip()]
    last, refused = {}, {}
    for r in rows:
        if r.get("method") == "export":
            refused[r["file"]] = r.get("error", "")
        else:
            last[(r["file"], r["method"])] = r
    ops = ["union", "intersection", "difference"]
    by = {}
    for r in last.values():
        by.setdefault((r["file"].split("_p")[0], r["method"]), []).append(r)
    print("| variant | method | pairs | face contact | operations | ok | failed / crash / timeout | clean | empty (volume-consistent) | empty, removed by rounding | unclean | max volume error | median ms | max ms | removed components |")
    print("|" + "---|" * 15)
    for (variant, m), rs in sorted(by.items(), key=lambda kv: (VARIANTS.index(kv[0][0]) if kv[0][0] in VARIANTS else 99, kv[0][1])):
        n = ok = bad = clean = empty = empty_removed = removed_total = 0
        unclean, errs, ms = {}, [], []
        for r in rs:
            for op in ops:
                n += 1
                s = r.get(f"{op}_status")
                if s != "ok":
                    bad += 1
                    continue
                ok += 1
                ms.append(r.get(f"{op}_ms") or 0)
                reason = r.get(f"{op}_clean_reason")
                if reason is None: reason = "not evaluated"        # the result was not read back by eval
                if reason == "": clean += 1
                elif reason == "empty" and tp.consistent(r):
                    # an empty result is "really empty" only if the rounding removed nothing; otherwise pieces (components) were dropped
                    if removed(r, op): empty_removed += 1
                    else: empty += 1
                elif reason == "empty": unclean["empty (inconsistent)"] = unclean.get("empty (inconsistent)", 0) + 1
                else: unclean[reason] = unclean.get(reason, 0) + 1
            removed_total += sum(removed(r, op) or 0 for op in ops)
            errs += [r[k] for k in ("ie_error", "diff_error", "expected_error") if r.get(k) is not None]
        faces = sum(1 for r in rs if (r.get("input") or {}).get("kind") == "face")
        print(f"| {variant} | {m} | {len(rs)} | {faces} | {n} | {ok} | {bad} | {clean} | {empty} | {empty_removed} | {', '.join(f'{k} {v}' for k, v in sorted(unclean.items())) or '–'} | "
              f"{max(errs, default=float('nan')):.2e} | {st.median(ms) if ms else float('nan'):.0f} | {max(ms, default=float('nan')):.0f} | {removed_total if m == 'ours' else '–'} |")
    if refused:
        cases = {r["file"] for r in last.values()} | set(refused)
        print(f"\nPlacements refused as inexact or failed to load: {len(refused)} of {len(cases)} cases:")
        for k, v in sorted(refused.items()):
            print(f"- {k}: {v}")

if __name__ == "__main__":
    a = sys.argv[1:]
    lim = int(a[a.index("--limit") + 1]) if "--limit" in a else None
    variants = a[a.index("--variants") + 1].split(",") if "--variants" in a else None
    if a[:1] == ["generate"]: generate()
    elif a[:1] == ["fetch"]: cmd_fetch(a[1], lim, variants)
    elif a[:1] == ["manifest"]: cmd_manifest(a[1], a[2], lim, variants)
    elif a[:1] == ["summary"]: cmd_summary(a[1:])
    else: sys.exit(__doc__)
