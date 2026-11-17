# The imprint comparison on the Thingi10K classes (README.md, "Imprint: ExactMeshImprint against MeshMeshCut"): the pairs of
# thingi-pairs (a range of thingi-pairs/pairs.csv) and of thingi-contact, run by compare.py with the imprint methods.
#
#   python3 data/thingi_imprint.py fetch-pairs <dir> <start> <end>       the models of pairs [start, end) of pairs.csv -> <dir>/meshes
#   python3 data/thingi_imprint.py manifest-pairs <dir> <out.jsonl> <start> <end>
#                                                                     the case manifest of these pairs (class thingi-pairs)
#   (the contact pairs: data/thingi_contact.py fetch / manifest)
#   python3 data/thingi_imprint.py summary results.jsonl...           the failure table, the time ratio and the cases where the methods disagree
#
# Models are fetched at run time (never committed); only the pair lists are part of the repository.
import csv, json, math, os, sys
import statistics as st

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import thingi_pairs as tp   # noqa: E402

CUT = ("exact_imprint", "float_cut")       # methods of the operations a_by_b, b_by_a
UNION = ("exact_union", "float_union")     # methods of the operation union
CUT_OPS = ("a_by_b", "b_by_a")
LENGTH_TOL = 1e-3     # relative difference of the curve lengths above which the cuts disagree
VOLUME_TOL = 1e-6     # relative difference (to vA + vB) of the union volumes above which the unions disagree
MAX_LISTED = 60       # disagreements listed per table


def pair_range(start, end):
    with open(os.path.join(tp.LISTS, "pairs.csv")) as f:
        return list(csv.DictReader(l for l in f if not l.startswith("#")))[start:end]


def cmd_fetch_pairs(d, start, end):
    os.makedirs(d, exist_ok=True)
    files = tp.file_list(d)
    ids = sorted({r[k] for r in pair_range(start, end) for k in ("a", "b")}, key=int)
    with tp.ThreadPoolExecutor(4) as ex:
        list(ex.map(lambda k: tp.fetch_file(f"{tp.BASE}/resolve/main/{files[k]}", os.path.join(d, "meshes", os.path.basename(files[k]))), ids))
    print(len(ids), "models fetched")


def cmd_manifest_pairs(d, out, start, end):
    rows = pair_range(start, end)
    with open(out, "w") as f:
        for r in rows:
            f.write(json.dumps({"class": "thingi-pairs", "name": f"p{r['pair']}_{r['a']}+{r['b']}",
                                "thingi_pair": [tp.mesh_path(d, r["a"]), tp.mesh_path(d, r["b"])] + [r[k] for k in tp.HEADER[3:]]}) + "\n")
    print(len(rows), "cases ->", out)


# --- summary

def group(r):
    """thingi-pairs, or the variant of a thingi-contact case (the case name starts with it)"""
    return r["file"].split("_p")[0] if r.get("class") == "thingi-contact" else r.get("class", "?")


def outcome(r, op):
    """'good' or why not: the status if the run did not end ok; for a cut: 'opened' (a closed target gave an open result), 'unclean: <reason>'
    (a clean target gave a result that NotCleanReason refuses), 'gaps' (MeshMeshCut.FailedSegmentInsertions > 0); for a union: 'not closed',
    'unclean: <reason>'"""
    s = r.get(f"{op}_status")
    if s != "ok":
        return s or "missing"
    reason = r.get(f"{op}_result_reason")
    if op == "union":
        if not r.get(f"{op}_result_closed"):
            return "not closed"
        return "good" if reason == "" else f"unclean: {reason}"
    if r.get(f"{op}_target_closed") and not r.get(f"{op}_result_closed"):
        return "opened"
    if r.get(f"{op}_target_reason") == "" and reason != "":
        return f"unclean: {reason}"
    if r.get(f"{op}_failed_insertions"):
        return "gaps"
    return "good"


def inserted_nothing(r, op):
    """an ok cut that found intersection segments and added no vertex"""
    return r.get(f"{op}_status") == "ok" and (r.get(f"{op}_segments") or 0) > 0 and r.get(f"{op}_new_vertices") == 0


def has_curve(r, op):
    return (r.get(f"{op}_curve_length") or 0) > 0


def read_rows(files):
    last = {}
    for f in files:
        with open(f) as fh:
            for l in fh:
                if not l.strip():
                    continue
                try:
                    r = json.loads(l)
                except ValueError:
                    continue        # a line cut off by an interrupted run
                last[(r.get("class"), r["file"], r["method"])] = r
    return list(last.values())


def geomean(x):
    return math.exp(sum(math.log(v) for v in x) / len(x)) if x else float("nan")


def disagreements(by_case):
    """per (group, case, op) where the exact and the float method disagree: the kind and both outcomes"""
    out = []
    for (g, name), ms in sorted(by_case.items()):
        e, f = ms.get("exact_imprint"), ms.get("float_cut")
        if e and f:
            for op in CUT_OPS:
                oe, of = outcome(e, op), outcome(f, op)
                kind = None
                if inserted_nothing(f, op) and (e.get(f"{op}_new_vertices") or 0) > 0:
                    # the float cut found intersection segments but snapped every point to a vertex of the target (VertexSnapTol):
                    # its result is the target unchanged, which its own checks cannot tell from a good cut
                    kind = f"float inserted nothing (exact: {e[f'{op}_new_vertices']} vertices)"
                elif oe != of and (oe == "good" or of == "good"):
                    kind = "only exact good" if oe == "good" else "only float good"
                elif oe == "good" and has_curve(e, op) != has_curve(f, op):
                    kind = "curve only exact" if has_curve(e, op) else "curve only float"
                elif oe == "good":
                    le, lf = e.get(f"{op}_curve_length") or 0, f.get(f"{op}_curve_length") or 0
                    if abs(le - lf) > LENGTH_TOL * max(le, lf):
                        kind = f"curve length {lf / le:.3f} of exact" if le > 0 else "curve length"
                elif oe != of:
                    kind = "both fail"
                if kind:
                    out.append((g, name, op, kind, oe, of))
        e, f = ms.get("exact_union"), ms.get("float_union")
        if e and f:
            oe, of = outcome(e, "union"), outcome(f, "union")
            kind = None
            if oe != of and (oe == "good" or of == "good"):
                kind = "only exact good" if oe == "good" else "only float good"
            elif oe == "good":
                inp = e.get("input") or {}
                scale = abs(inp.get("vA") or 0) + abs(inp.get("vB") or 0)
                ve, vf = e.get("union_volume") or 0, f.get("union_volume") or 0
                if scale > 0 and abs(ve - vf) > VOLUME_TOL * scale:
                    kind = f"volume differs {abs(ve - vf) / scale:.1e}"
            elif oe != of:
                kind = "both fail"
            if kind:
                out.append((g, name, "union", kind, oe, of))
    return out


KINDS = ["float inserted nothing", "only exact good", "only float good", "curve only exact", "curve only float", "curve length",
         "volume differs", "both fail"]


def category(kind):
    """the kind of a disagreement without its numbers"""
    return next(k for k in KINDS if kind.startswith(k))


def summary(files, out=sys.stdout):
    rows = read_rows(files)
    export = [r for r in rows if r["method"] == "export"]
    rows = [r for r in rows if r["method"] in CUT + UNION]
    by = {}
    by_case = {}
    for r in rows:
        by.setdefault((group(r), r["method"]), []).append(r)
        by_case.setdefault((group(r), r["file"]), {})[r["method"]] = r
    order = tp_order()
    w = out.write
    w("| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |\n")
    w("|" + "---|" * 14 + "\n")
    for (g, m), rs in sorted(by.items(), key=lambda kv: (order(kv[0][0]), (CUT + UNION).index(kv[0][1]))):
        ops = CUT_OPS if m in CUT else ("union",)
        n = good = curves = gaps = opened = nothing = rounding = 0
        bad, unclean, ms = {}, {}, []
        for r in rs:
            for op in ops:
                n += 1
                o = outcome(r, op)
                if r.get(f"{op}_status") == "ok":
                    ms.append(r.get(f"{op}_ms") or 0)
                    if has_curve(r, op):
                        curves += 1
                    if inserted_nothing(r, op):
                        nothing += 1
                    if r.get(f"{op}_rounding_clean") is False:
                        rounding += 1
                if o == "good":
                    good += 1
                elif o in ("opened", "not closed"):
                    opened += 1
                elif o.startswith("unclean"):
                    unclean[o[9:]] = unclean.get(o[9:], 0) + 1
                elif o == "gaps":
                    gaps += 1
                else:
                    bad[o] = bad.get(o, 0) + 1
        fails = " / ".join(str(bad.get(k, 0)) for k in ("failed", "exception", "crash", "timeout"))
        other = sum(v for k, v in bad.items() if k not in ("failed", "exception", "crash", "timeout"))
        if other:
            fails += f" (+{other} other)"
        w(f"| {g} | {m} | {len(rs)} | {n} | {good} | {fails} | {opened} | {', '.join(f'{k} {v}' for k, v in sorted(unclean.items())) or '–'} | "
          f"{gaps if m == 'float_cut' else '–'} | {curves if m in CUT else '–'} | {nothing if m == 'float_cut' else '–'} | "
          f"{rounding if m == 'exact_imprint' else '–'} | {st.median(ms) if ms else float('nan'):.0f} | {max(ms, default=float('nan')):.0f} |\n")
    # time ratio float / exact, over the operations both ran ok
    w("\nTime ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):\n\n")
    w("| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |\n|---|---|---|---|---|\n")
    ratios = {}
    for (g, name), ms in by_case.items():
        for pair, ops in ((("exact_imprint", "float_cut"), CUT_OPS), (("exact_union", "float_union"), ("union",))):
            e, f = ms.get(pair[0]), ms.get(pair[1])
            if not (e and f):
                continue
            for op in ops:
                if e.get(f"{op}_status") == "ok" and f.get(f"{op}_status") == "ok":
                    ratios.setdefault((g, pair[0]), []).append(max(f.get(f"{op}_ms") or 0, 1) / max(e.get(f"{op}_ms") or 0, 1))
    for g in sorted({g for g, _ in ratios}, key=order):
        c, u = ratios.get((g, "exact_imprint"), []), ratios.get((g, "exact_union"), [])
        w(f"| {g} | {geomean(c):.2f} | {len(c)} | {geomean(u):.2f} | {len(u)} |\n")
    dis = disagreements(by_case)
    kinds = {}
    for d in dis:
        k = category(d[3])
        kinds[(d[0], k)] = kinds.get((d[0], k), 0) + 1
    w(f"\nDisagreements (exact against float, per operation): {len(dis)}")
    if kinds:
        w(" (" + ", ".join(f"{g}: {k} {v}" for (g, k), v in sorted(kinds.items(), key=lambda kv: (order(kv[0][0]), kv[0][1]))) + ")")
    w("\n\n")
    if dis:
        w("| class | case | operation | kind | exact | float |\n|---|---|---|---|---|---|\n")
        for d in dis[:MAX_LISTED]:
            w("| " + " | ".join(d) + " |\n")
        if len(dis) > MAX_LISTED:
            w(f"\n({len(dis) - MAX_LISTED} more in the raw rows)\n")
    if export:
        w(f"\nCases not written (placement refused as inexact, or load failed): {len(export)}\n")
    return dis


def tp_order():
    known = ["thingi-pairs", "face-contact", "partial-overlap", "ulp-shift", "ulp-shift-few", "mirror"]
    return lambda g: (known.index(g) if g in known else 99, g)


if __name__ == "__main__":
    a = sys.argv[1:]
    if a[:1] == ["fetch-pairs"]: cmd_fetch_pairs(a[1], int(a[2]), int(a[3]))
    elif a[:1] == ["manifest-pairs"]: cmd_manifest_pairs(a[1], a[2], int(a[3]), int(a[4]))
    elif a[:1] == ["summary"]: summary(a[1:])
    else: sys.exit(__doc__)
