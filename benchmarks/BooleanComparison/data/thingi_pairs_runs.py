# Helper of .github/workflows/thingi-pairs.yml: the full runs of the thingi-pairs classes (README.md, "Thingi10K pairs after the
# EMBER protocol", "Full runs"). Nothing of the data is part of this repository; the meshes are fetched at run time.
#
#   python3 data/thingi_pairs_runs.py plan [--shard N] [--size-shard N] [--cgal-shard N] [--methods M] [--classes C] [--max-pairs N]
#                                                  the job matrix (JSON {"include": [...]}) of one run: shards of pairs.csv (normal),
#                                                  pairs_size.csv (size) and pairs_cgal_sample.csv (cgal; the method cgal_epeck only)
#   python3 data/thingi_pairs_runs.py shard <kind> <lo> <hi> <out.csv>
#                                                  the pair list of one job: rows [lo, hi) of the list of <kind> (normal | size | cgal);
#                                                  the file name of a size shard must contain "size" (thingi_pairs.py manifest names the class by it)
#   python3 data/thingi_pairs_runs.py fetch <dir> <pairs.csv>
#                                                  the models of a pair list -> <dir>/meshes (huggingface)
#   python3 data/thingi_pairs_runs.py summary <results.jsonl | directory>...
#                                                  per class and method: success, clean, NotCleanReason histogram, max identity error,
#                                                  time median / p90 / max, operations above 5 s and 20 s
#   python3 data/thingi_pairs_runs.py compare <base> <final> [--method ours] [--floor-ms 100] [--strict]
#                                                  baseline against final, per pair and operation: clean before and not after,
#                                                  slower than 2x, faster, and the table of the time ratios (--strict: exit 1 on a regression)
#
# A <directory> argument is searched (recursively) for results*.jsonl. The last row of a (class, file, method) wins, as compare.py
# --resume may append a case again. The oracle is the one of thingi_pairs.py (NotCleanReason of the result, the volume identities
# with the tolerance 1e-7, an empty result counts only if the identities hold and no result of the pair self-intersects).
import csv, json, math, os, sys
import thingi_pairs as tp

OPS = ["union", "intersection", "difference"]
KINDS = {"normal": "pairs.csv", "size": "pairs_size.csv", "cgal": "pairs_cgal_sample.csv"}
CLASS_OF = {"normal": "thingi-pairs", "size": "thingi-pairs-size", "cgal": "thingi-pairs"}
ALL_METHODS = ["ours", "manifold", "cgal_epeck"]
SLOW_S = (5, 20)        # operations above these times are counted (CLAUDE.md: a test above 5 s is slow, a library operation above 20 s a potential defect)

def read_list(kind):
    """(comment lines, rows) of a committed pair list"""
    with open(os.path.join(tp.LISTS, KINDS[kind])) as f:
        lines = f.read().splitlines()
    comments = [l for l in lines if l.startswith("#")]
    rows = list(csv.DictReader(l for l in lines if not l.startswith("#")))
    return comments, rows

# --- plan and shard

def shards(count, size):
    return [(lo, min(lo + size, count)) for lo in range(0, count, size)]

def plan(shard=50, size_shard=10, cgal_shard=20, methods="ours,manifold,cgal_epeck", classes="normal,size,cgal", max_pairs=0):
    """the matrix of jobs; a job runs the methods of its kind on the pairs [lo, hi) of its list. normal and size run the methods
    except cgal_epeck, cgal runs cgal_epeck only (owner 2026-11-08: CGAL EPECK on the fixed 100-pair sample)"""
    ms, cs = [m for m in methods.split(",") if m], [c for c in classes.split(",") if c]
    for m in ms:
        if m not in ALL_METHODS:
            raise ValueError(f"unknown method {m}")
    for c in cs:
        if c not in KINDS:
            raise ValueError(f"unknown class {c}")
    own = ",".join(m for m in ms if m != "cgal_epeck")
    include = []
    for kind, size in (("normal", shard), ("size", size_shard), ("cgal", cgal_shard)):
        methods_of_kind = "cgal_epeck" if kind == "cgal" and "cgal_epeck" in ms else None if kind == "cgal" else own
        if kind not in cs or not methods_of_kind:
            continue
        if size < 1:
            raise ValueError("a shard needs at least one pair")
        count = len(read_list(kind)[1])
        if max_pairs > 0:
            count = min(count, max_pairs)
        for i, (lo, hi) in enumerate(shards(count, size)):
            include.append({"id": f"{kind}-{i}", "kind": kind, "lo": lo, "hi": hi, "methods": methods_of_kind})
    return {"include": include}

def write_shard(kind, lo, hi, out):
    comments, rows = read_list(kind)
    if not 0 <= lo < hi <= len(rows):
        raise ValueError(f"shard [{lo}, {hi}) outside the {len(rows)} rows of {KINDS[kind]}")
    if (kind == "size") != ("size" in os.path.basename(out)):
        raise ValueError("the file name of a size shard contains 'size', the name of any other shard does not (the class is named by it)")
    with open(out, "w") as f:
        f.write("".join(c + "\n" for c in comments[:1]) + f"# shard [{lo}, {hi}) of {KINDS[kind]}\n")
        w = csv.DictWriter(f, fieldnames=tp.HEADER, lineterminator="\n")
        w.writeheader()
        w.writerows(rows[lo:hi])

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

# --- rows

def result_files(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            for root, _, names in sorted(os.walk(p)):
                out += [os.path.join(root, n) for n in sorted(names) if n.startswith("results") and n.endswith(".jsonl")]
        elif os.path.exists(p):
            out.append(p)
    return out

def load(paths):
    """{(class, file, method): row}; the last row wins"""
    last = {}
    for p in result_files(paths):
        with open(p) as f:
            for l in f:
                if not l.strip():
                    continue
                try:
                    r = json.loads(l)
                except ValueError:
                    continue            # a line cut off by an interrupted run
                if r.get("method") != "export" and "class" in r:
                    last[(r["class"], r.get("file"), r["method"])] = r
    return last

def consistent(r):
    """as thingi_pairs.consistent, but a self-intersection count of -1 (not computed: a result above 400k triangles) is unknown, not a hit"""
    return all((r.get(k) or 0) <= tp.TOL for k in ("ie_error", "diff_error")) and \
        all(r.get(f"{op}_clean_reason") != "self-intersections" and (r.get(f"{op}_selfint") or 0) <= 0 for op in OPS)

def outcome(r, op):
    """'clean' | 'empty' (volume-consistent) | the NotCleanReason, 'empty (inconsistent)' or the status (rejected, failed, crash, timeout, ...)"""
    s = r.get(f"{op}_status")
    if s != "ok":
        return s or "no result"
    reason = r.get(f"{op}_clean_reason")
    if reason == "":
        return "clean"
    if reason == "empty":
        return "empty" if consistent(r) else "empty (inconsistent)"
    return reason if reason is not None else "not evaluated"

def good(o):
    return o in ("clean", "empty")

def pct(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p / 100 * len(v)))] if v else float("nan")

def op_ms(r, op):
    return r.get(f"{op}_ms")

# --- summary

def summary(paths):
    rows = load(paths)
    by = {}
    for (cls, _, m), r in rows.items():
        by.setdefault((cls, m), []).append(r)
    out = ["| class | method | pairs | operations | success | clean | empty (volume-consistent) | not clean / not ok | max identity error | median ms | p90 ms | max ms | ops > 5 s | ops > 20 s |",
           "|" + "---|" * 14]
    for (cls, m), rs in sorted(by.items()):
        ops = ok = clean = empty = 0
        hist, errs, ms = {}, [], []
        for r in rs:
            for op in OPS:
                ops += 1
                o = outcome(r, op)
                ok += r.get(f"{op}_status") == "ok"
                if o == "clean": clean += 1
                elif o == "empty": empty += 1
                else: hist[o] = hist.get(o, 0) + 1
                if op_ms(r, op) is not None and r.get(f"{op}_status") == "ok":
                    ms.append(op_ms(r, op))
            errs += [r[k] for k in ("ie_error", "diff_error") if r.get(k) is not None]
        over = [sum(1 for v in ms if v > s * 1000) for s in SLOW_S]
        h = ", ".join(f"{k} {v}" for k, v in sorted(hist.items(), key=lambda kv: (-kv[1], kv[0]))) or "–"
        out.append(f"| {cls} | {m} | {len(rs)} | {ops} | {ok} | {clean} | {empty} | {h} | {max(errs, default=float('nan')):.2e} | "
                   f"{pct(ms, 50):.0f} | {pct(ms, 90):.0f} | {max(ms, default=float('nan')):.0f} | {over[0]} | {over[1]} |")
    return "\n".join(out)

# --- baseline against final

def compare(base_paths, final_paths, method="ours", floor_ms=100):
    """per pair and operation of one method. A pair that is good in the baseline and has no row in the final run (a job that died, an
    export error) is a regression ("missing"), not skipped. "pinches" (the result is clean except where the solid touches itself) is
    a reason of the final tool only: the baseline tool reports such a result differently, so clean before and pinches after is
    listed apart from the regressions."""
    base, final = load([base_paths] if isinstance(base_paths, str) else base_paths), load([final_paths] if isinstance(final_paths, str) else final_paths)
    bkeys = sorted(k for k in base if k[2] == method)
    keys = [k for k in bkeys if k in final]
    missing = [k for k in bkeys if k not in final]
    only_final = sum(1 for k in final if k[2] == method and k not in base)
    lost, pinch, gained, slower, faster, ratios = [], [], [], [], [], []
    ops = 0
    for k in missing:
        for op in OPS:
            if good(outcome(base[k], op)):
                lost.append((f"{k[0]} {k[1]} {op}", outcome(base[k], op), "missing in the final run"))
    for k in keys:
        b, f = base[k], final[k]
        for op in OPS:
            ops += 1
            ob, of = outcome(b, op), outcome(f, op)
            tag = f"{k[0]} {k[1]} {op}"
            if good(ob) and of == "pinches": pinch.append((tag, ob, of))
            elif good(ob) and not good(of): lost.append((tag, ob, of))
            if not good(ob) and good(of): gained.append((tag, ob, of))
            mb, mf = op_ms(b, op), op_ms(f, op)
            if b.get(f"{op}_status") == "ok" and f.get(f"{op}_status") == "ok" and mb and mf:
                ratios.append(mf / mb)
                if mf > 2 * mb: slower.append((tag, mb, mf))
                elif mb > 2 * mf: faster.append((tag, mb, mf))
    out = [f"Baseline against final, method {method}: {len(keys)} pairs in both runs, {ops} operations "
           f"({len(missing)} pairs of the baseline have no row in the final run, {only_final} pairs only in the final run).", ""]
    out += ["| | operations |", "|---|---|",
            f"| clean (or volume-consistent empty) before, not after (including a missing pair) | {len(lost)} |",
            f"| clean before, pinches after (a reason of the final tool only) | {len(pinch)} |",
            f"| not clean before, clean after | {len(gained)} |",
            f"| slower than 2x (final ms > 2 x baseline ms) | {len(slower)} |",
            f"| ... of which baseline >= {floor_ms} ms | {sum(1 for _, mb, _ in slower if mb >= floor_ms)} |",
            f"| faster than 2x | {len(faster)} |"]
    if ratios:
        g = math.exp(sum(math.log(max(r, 1e-6)) for r in ratios) / len(ratios))
        out += ["", f"Time ratio final / baseline over {len(ratios)} operations ok in both: median {pct(ratios, 50):.2f}, "
                    f"p10 {pct(ratios, 10):.2f}, p90 {pct(ratios, 90):.2f}, max {max(ratios):.2f}, geomean {g:.2f}."]
    for title, rows in (("Regressions (clean before, not after)", lost), ("Clean before, pinches after", pinch)):
        if rows:
            out += ["", f"**{title}:**", "", "| pair and operation | before | after |", "|---|---|---|"]
            out += [f"| {t} | {a} | {b} |" for t, a, b in rows]
    if slower:
        out += ["", "**Slower than 2x:**", "", "| pair and operation | baseline ms | final ms | ratio |", "|---|---|---|---|"]
        out += [f"| {t} | {a:.0f} | {b:.0f} | {b / a:.1f} |" for t, a, b in sorted(slower, key=lambda x: -x[2] / x[1])]
    compare.regressions = len(lost)
    return "\n".join(out)

def main(a):
    def opt(name, default, cast=str):
        return cast(a[a.index(name) + 1]) if name in a else default
    if a[:1] == ["plan"]:
        print(json.dumps(plan(opt("--shard", 50, int), opt("--size-shard", 10, int), opt("--cgal-shard", 20, int),
                              opt("--methods", "ours,manifold,cgal_epeck"), opt("--classes", "normal,size,cgal"), opt("--max-pairs", 0, int))))
    elif a[:1] == ["shard"]: write_shard(a[1], int(a[2]), int(a[3]), a[4])
    elif a[:1] == ["fetch"]: cmd_fetch(a[1], a[2])
    elif a[:1] == ["summary"]: print(summary(a[1:]))
    elif a[:1] == ["compare"]:
        print(compare(a[1], a[2], opt("--method", "ours"), opt("--floor-ms", 100, float)))
        if "--strict" in a and compare.regressions:
            sys.exit(1)
    else: sys.exit(__doc__)

if __name__ == "__main__":
    main(sys.argv[1:])
