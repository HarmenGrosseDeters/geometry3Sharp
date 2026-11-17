# Exact Boolean 4.0 stage 0 (docs/design/EXACT_BOOLEAN_40_DESIGN.md, section 4): today's round-each-step chain against exact
# intermediates with one final rounding (the one-pass n-ary boolean over the original operands, `chain-exact` of the tool).
#
#   python3 chain_exact.py proc <out.jsonl> [--seeds N] [--steps K] [--families mixed,generic]   procedural chains, resumable
#   python3 chain_exact.py summary <rows.jsonl>...      per family and step: cleanness (chained / one pass), attribution of
#                                                       the unclean results, sizes, bits against the bounds, times
#
# The rows of the abc-chained class come from `abc_chained.py run ... --methods ours_exact` (rows with class
# "abc-chained-exact"); summary reads both kinds. The tool is $BOOLCMP_TOOL (a built BooleanComparison.dll) or the
# Release build next to this script.
import json, os, statistics as st, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
EXACT_CLASS = "abc-chained-exact"

def tool():
    t = os.environ.get("BOOLCMP_TOOL") or os.path.join(HERE, "bin", "Release", "net10.0", "BooleanComparison.dll")
    return ["dotnet", t]

def rows_of(text):
    out = []
    for l in text.splitlines():
        l = l.strip()
        if l.startswith("{"):
            try:
                out.append(json.loads(l))
            except ValueError:
                pass
    return out

def cmd_proc(out, seeds, steps, families, timeout=3600):
    done = set()
    if os.path.exists(out):
        for l in open(out):
            try:
                r = json.loads(l)
                done.add((r["family"], r["seed"]))
            except (ValueError, KeyError):
                pass
    with open(out, "a") as f:
        for fam in families:
            for s in range(1, seeds + 1):
                if (fam, s) in done:
                    continue
                try:
                    p = subprocess.run(tool() + ["chain-exact-proc", fam, str(s), str(steps)], capture_output=True, text=True, timeout=timeout)
                    rows = rows_of(p.stdout)
                    if not rows:
                        rows = [{"family": fam, "seed": s, "runner_error": (p.stderr or p.stdout)[-300:]}]
                except subprocess.TimeoutExpired:
                    rows = [{"family": fam, "seed": s, "runner_error": "timeout"}]
                for r in rows:
                    f.write(json.dumps(r) + "\n")
                f.flush()
                print(fam, s, len(rows), flush=True)

def clean(r, pre):
    """clean as the abc-chained eval counts it: NotCleanReason without the self-intersection test ("" or empty), and the
    rounding's own exact check (contact-aware) clean"""
    if not r.get(pre + "_ok"):
        return None
    return r.get(pre + "_reason") in ("", "empty") and bool(r.get(pre + "_rounding_clean", True))

def key(r):
    if r.get("class") == EXACT_CLASS or r.get("family") == "abc":
        return "abc-" + r.get("variant", "?")
    return r.get("family", "?")

def fmt(xs, f=lambda v: f"{v:.0f}"):
    xs = [x for x in xs if x is not None]
    return f(st.median(xs)) if xs else "-"

def cmd_summary(files):
    rows = []
    for fn in files:
        rows += [json.loads(l) for l in open(fn) if l.strip()]
    rows = [r for r in rows if "step" in r]
    groups = {}
    for r in rows:
        groups.setdefault((key(r), r["step"]), []).append(r)
    print("| family | step | rows | chained clean | one pass clean | unclean only chained (intermediate rounding) | unclean in both (final rounding) | unclean only one pass | removed comps chained / one pass | median tris chained / exact / one pass rounded | median ms chained (rounding) / one pass / exact unrounded | max num / W bits | mean bits per vertex | vertices double / LPI / TPI / edge-TPI / beyond (sums) |")
    print("|" + "---|" * 14)
    tot = {"chained": 0, "onepass": 0, "inter": 0, "final": 0, "worse": 0, "n": 0}
    for (fam, k), rs in sorted(groups.items()):
        both = [r for r in rs if clean(r, "chained") is not None and clean(r, "onepass") is not None]
        cc = sum(1 for r in both if clean(r, "chained")); oc = sum(1 for r in both if clean(r, "onepass"))
        inter = sum(1 for r in both if not clean(r, "chained") and clean(r, "onepass"))
        final = sum(1 for r in both if not clean(r, "chained") and not clean(r, "onepass"))
        worse = sum(1 for r in both if clean(r, "chained") and not clean(r, "onepass"))
        for a, b in (("chained", cc), ("onepass", oc), ("inter", inter), ("final", final), ("worse", worse), ("n", len(both))):
            tot[a] += b
        rc = sum(r.get("chained_removed_components", 0) or 0 for r in rs); ro = sum(r.get("onepass_removed_components", 0) or 0 for r in rs)
        tris = f"{fmt([r.get('chained_tris') for r in rs])} / {fmt([r.get('exact_tris') for r in rs])} / {fmt([r.get('onepass_tris') for r in rs])}"
        ms = f"{fmt([r.get('chained_ms') for r in rs])} ({fmt([r.get('chained_rounding_ms') for r in rs])}) / {fmt([r.get('onepass_ms') for r in rs])} / {fmt([r.get('exact_ms') for r in rs])}"
        mn = max([r.get("exact_max_num_bits", 0) for r in rs] or [0]); mw = max([r.get("exact_max_w_bits", 0) for r in rs] or [0])
        mb = fmt([r.get("exact_mean_bits_per_vertex") for r in rs])
        cls = "/".join(str(sum(r.get(f, 0) or 0 for r in rs)) for f in ("exact_double_vertices", "exact_within_lpi_bound", "exact_within_tpi_bound", "exact_within_edge_tpi_bound", "exact_beyond_bounds"))
        print(f"| {fam} | {k} | {len(rs)} | {cc}/{len(both)} | {oc}/{len(both)} | {inter} | {final} | {worse} | {rc} / {ro} | {tris} | {ms} | {mn} / {mw} | {mb} | {cls} |")
    print()
    print(f"All steps: {tot['n']} results with both variants; chained clean {tot['chained']}, one pass clean {tot['onepass']}; "
          f"unclean only chained {tot['inter']}, in both {tot['final']}, only one pass {tot['worse']}.")
    reasons = {}
    for r in rows:
        for pre in ("chained", "onepass"):
            if clean(r, pre) is False:
                k2 = (pre, r.get(pre + "_reason") or ("rounding not clean" + (", evaluation limit" if r.get(pre + "_evaluation_limit") else "")))
                reasons[k2] = reasons.get(k2, 0) + 1
    if reasons:
        print()
        print("Unclean reasons: " + "; ".join(f"{p} {k}: {n}" for (p, k), n in sorted(reasons.items())))

if __name__ == "__main__":
    a = sys.argv[1:]
    def opt(name, default=None, cast=str):
        return cast(a[a.index(name) + 1]) if name in a else default
    if a[:1] == ["proc"]:
        cmd_proc(a[1], opt("--seeds", 20, int), opt("--steps", 8, int), opt("--families", ["mixed", "generic"], lambda s: s.split(",")))
    elif a[:1] == ["summary"]:
        cmd_summary(a[1:])
    else:
        sys.exit(__doc__)
