# Exact Boolean 4.0 stage 0 (docs/design/EXACT_BOOLEAN_40_DESIGN.md, section 4): today's round-each-step chain against exact
# intermediates with one final rounding (the one-pass n-ary boolean over the original operands, `chain-exact` of the tool).
#
#   python3 chain_exact.py proc <out.jsonl> [--seeds N] [--steps K] [--families mixed,generic]   procedural chains, resumable
#   python3 chain_exact.py proc <out.jsonl> ... --mode diag --factors 1,4,16   stage 1: today's chain and ExactMesh.Round() with the
#                                                       rounding's pair-test budget times each factor (chain-exact-diag-proc)
#   python3 chain_exact.py proc <out.jsonl> ... --mode time --reps 5   stage 1: order-randomized timing (chain-exact-time-proc)
#   python3 chain_exact.py diag-summary <rows.jsonl>...   per family and step: cleanness of the chain and of ExactMesh.Round() per factor
#   python3 chain_exact.py time-summary <rows.jsonl>...   per family and step: medians, ExactMesh.Round() / chain steps 1..k
#   python3 chain_exact.py summary <rows.jsonl>...      per family and step: cleanness (chained / one pass), attribution of
#                                                       the unclean results, sizes, bits against the bounds, times
#
# The rows of the abc-chained class come from `abc_chained.py run ... --methods ours_exact` (rows with class
# "abc-chained-exact"); summary reads both kinds. The tool is $BOOLCMP_TOOL (a built BooleanComparison.dll) or the
# Release build next to this script.
import json, math, os, statistics as st, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
EXACT_CLASS = "abc-chained-exact"

def tool():
    t = os.environ.get("BOOLCMP_TOOL") or os.path.join(HERE, "bin", "Release", "net10.0", "BooleanComparison.dll")
    return ["dotnet", t]

def rows_of(text):
    out = []
    for l in (text or "").splitlines():
        l = l.strip()
        if l.startswith("{"):
            try:
                out.append(json.loads(l))
            except ValueError:
                pass
    return out

def cmd_proc(out, seeds, steps, families, timeout=3600, command="chain-exact-proc", extra=()):
    # a chain is done when it has a row for every step (or ended early with an empty chained and exact result);
    # chains with a runner error or missing steps are run again (their old rows stay, the summary keeps the last row per step)
    have = {}
    if os.path.exists(out):
        for l in open(out):
            try:
                r = json.loads(l)
                have.setdefault((r["family"], r["seed"]), []).append(r)
            except (ValueError, KeyError):
                pass
    want = {"chain-exact-proc": "measure", "chain-exact-diag-proc": "diag", "chain-exact-time-proc": "time"}[command]
    done = {k for k, rs in have.items() if len({r["step"] for r in rs if "step" in r and kind(r) == want}) >= steps}
    with open(out, "a") as f:
        for fam in families:
            for s in range(1, seeds + 1):
                if (fam, s) in done:
                    continue
                try:
                    p = subprocess.run(tool() + [command, fam, str(s), str(steps)] + list(extra), capture_output=True, text=True, timeout=timeout)
                    rows = rows_of(p.stdout)
                    if not rows:
                        rows = [{"family": fam, "seed": s, "runner_error": (p.stderr or p.stdout)[-300:]}]
                except subprocess.TimeoutExpired as ex:
                    out_text = ex.stdout.decode() if isinstance(ex.stdout, bytes) else ex.stdout
                    rows = rows_of(out_text) + [{"family": fam, "seed": s, "runner_error": "timeout"}]
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

def kind(r):
    return "time" if "lazy_median_ms" in r else "diag" if "factors" in r else "measure"

def load(files, want="measure"):
    """the rows of chain-exact of one kind (measure, diag or time; procedural rows have "family", abc rows class
    abc-chained-exact); other rows (other methods of an abc-chained results.jsonl, other kinds) and truncated lines are skipped;
    the last row of a (chain, step) wins"""
    last = {}
    for fn in files:
        for l in open(fn):
            try:
                r = json.loads(l)
            except ValueError:
                continue
            if "step" not in r or not ("family" in r or r.get("class") == EXACT_CLASS) or kind(r) != want:
                continue
            last[(key(r), r.get("file") or r.get("seed"), r["step"])] = r
    return list(last.values())

def ratio(a, b):
    return a / b if a is not None and b else None

def cmd_summary(files):
    rows = load(files)
    groups = {}
    for r in rows:
        groups.setdefault((key(r), r["step"]), []).append(r)
    print("Clean: NotCleanReason(result, false) in {'', 'empty'} on the unwelded in-memory result and RoundingClean (not the abc-chained eval,")
    print("which welds the OFF result, splits pinches and tests self-intersections). Ratios are medians of per-result ratios; the volume")
    print("deviation is |V chained - V one pass| / V one pass (both rounded; the one pass is the exact chain rounded once).")
    print()
    print("| family | step | rows | chained clean | one pass clean | unclean only chained (intermediate rounding) | unclean in both (final rounding) | unclean only one pass | removed comps chained / one pass | median tris chained / exact / one pass rounded | median ms chained (rounding) / one pass / exact unrounded | max num / W bits | mean bits per vertex | vertices double / LPI / TPI / edge-TPI / beyond (sums) | tris chained / exact, median (max) | volume deviation median (max) |")
    print("|" + "---|" * 16)
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
        tr = [x for x in (ratio(r.get("chained_tris"), r.get("exact_tris")) for r in rs if r.get("chained_tris")) if x is not None]
        dv = [abs(r["chained_volume"] - r["onepass_volume"]) / r["onepass_volume"] for r in rs
              if r.get("chained_tris") and r.get("onepass_tris") and r.get("onepass_volume")]
        trs = f"{st.median(tr):.2f} ({max(tr):.2f})" if tr else "-"
        dvs = f"{st.median(dv):.1e} ({max(dv):.1e})" if dv else "-"
        print(f"| {fam} | {k} | {len(rs)} | {cc}/{len(both)} | {oc}/{len(both)} | {inter} | {final} | {worse} | {rc} / {ro} | {tris} | {ms} | {mn} / {mw} | {mb} | {cls} | {trs} | {dvs} |")
    print()
    print(f"All steps: {tot['n']} results with both variants; chained clean {tot['chained']}, one pass clean {tot['onepass']}; "
          f"unclean only chained {tot['inter']}, in both {tot['final']}, only one pass {tot['worse']}.")
    # stage 1 (lazy expression): one pass for the final result against the summed chained steps 1..k
    chains = {}
    for r in rows:
        chains.setdefault((key(r), r.get("file") or r.get("seed")), {})[r["step"]] = r
    print()
    print("| family | k | chains | one pass ms of step k / summed chained ms of steps 1..k: median | p90 | max |")
    print("|---|---|---|---|---|---|")
    ks = sorted({k for (_, k) in groups})
    for fam in sorted({f for (f, _) in groups}):
        for k in ks:
            rat = []
            for (f, _), stp in chains.items():
                if f != fam or k not in stp or not all(i in stp and stp[i].get("chained_ok") and "chained_ms" in stp[i] for i in range(1, k + 1)):
                    continue
                if stp[k].get("onepass_ms") is None:
                    continue
                rat.append(stp[k]["onepass_ms"] / max(1, sum(stp[i]["chained_ms"] for i in range(1, k + 1))))
            if rat:
                rat.sort()
                print(f"| {fam} | {k} | {len(rat)} | {st.median(rat):.2f} | {rat[max(0, math.ceil(0.9 * len(rat)) - 1)]:.2f} | {rat[-1]:.2f} |")
    reasons = {}
    for r in rows:
        if clean(r, "chained") is None or clean(r, "onepass") is None:
            continue
        for pre in ("chained", "onepass"):
            if clean(r, pre) is False:
                k2 = (pre, (r.get(pre + "_reason") or "rounding not clean") + (", evaluation limit" if r.get(pre + "_evaluation_limit") else ""))
                reasons[k2] = reasons.get(k2, 0) + 1
    if reasons:
        print()
        print("Unclean reasons (results with both variants; the repair's evaluation limit noted where reached): " + "; ".join(f"{p} {k}: {n}" for (p, k), n in sorted(reasons.items())))

def cmd_diag_summary(files):
    """per family and step: clean results of today's chain and of ExactMesh.Round() per budget factor, over the results where
    both exist; the newly unclean (clean chained, unclean lazy) with their rounding diagnostics"""
    rows = load(files, "diag")
    groups = {}
    for r in rows:
        groups.setdefault((key(r), r["step"]), []).append(r)
    factors = sorted({f for r in rows for f in r.get("factors", [])})
    pre = ["lazy_x" + (str(int(f)) if float(f).is_integer() else str(f)) for f in factors]
    print("| family | step | results | chained clean | " + " | ".join(f"ExactMesh clean, budget x{f:g} (evaluation limit)" for f in factors) + f" | newly unclean x{factors[0]:g} / x{factors[-1]:g} |")
    print("|" + "---|" * (5 + len(factors)))
    for (fam, k), rs in sorted(groups.items()):
        both = [r for r in rs if clean(r, "chained") is not None and all(clean(r, p) is not None for p in pre)]
        cc = sum(1 for r in both if clean(r, "chained"))
        cols = []
        for i, p in enumerate(pre):
            n = sum(1 for r in both if clean(r, p)); lim = sum(1 for r in both if r.get(p + "_evaluation_limit"))
            cols.append(f"{n}/{len(both)} ({lim})")
        w1 = sum(1 for r in both if clean(r, "chained") and not clean(r, pre[0]))
        wl = sum(1 for r in both if clean(r, "chained") and not clean(r, pre[-1]))
        print(f"| {fam} | {k} | {len(both)} | {cc}/{len(both)} | " + " | ".join(cols) + f" | {w1} / {wl} |")
    print()
    print(f"Newly unclean results (clean chained, ExactMesh unclean at budget x{factors[0]:g}): the rounding's state per factor")
    print()
    print("| family | chain | step | factor | reason | evaluation limit | remaining intersections / degenerate / false contacts | smallest rejected drift / bound | removed components |")
    print("|---|---|---|---|---|---|---|---|---|")
    for (fam, k), rs in sorted(groups.items()):
        for r in rs:
            if clean(r, "chained") and clean(r, pre[0]) is False:
                for f, p in zip(factors, pre):
                    srd, ml = r.get(p + "_smallest_rejected_drift", -1), r.get(p + "_max_length") or 0
                    rej = f"{srd / ml:.2f}" if srd and srd > 0 and ml else "none"
                    print(f"| {fam} | {r.get('file') or r.get('seed')} | {k} | {f:g} | {r.get(p + '_reason') or ('rounding not clean' if not r.get(p + '_rounding_clean') else '')} | "
                          f"{r.get(p + '_evaluation_limit')} | {'/'.join(str(x) for x in r.get(p + '_remaining', []))} | {rej} | {r.get(p + '_removed_components')} |")

def cmd_time_summary(files):
    """per family and step: median ms of today's chain (steps 1..k summed, and step k alone) and of ExactMesh.Round() of step k
    (each the median over the order-randomized repetitions of one chain), and the ratio ExactMesh / chain 1..k: median, p90, max over
    the chains"""
    rows = load(files, "time")
    groups, dropped = {}, 0
    for r in rows:
        if r.get("chain_cum_median_ms") is not None and r.get("lazy_median_ms") is not None and r.get("lazy_ok", True) and r.get("chain_ok", True):
            groups.setdefault((key(r), r["step"]), []).append(r)
        else:
            dropped += 1
    print("| family | step | chains | median ms chain steps 1..k | median ms chain step k | median ms ExactMesh.Round() | ExactMesh / chain 1..k: median | p90 | max | summed ms chain 1..k / ExactMesh |")
    print("|---|---|---|---|---|---|---|---|---|---|")
    for (fam, k), rs in sorted(groups.items()):
        rat = sorted(r["lazy_median_ms"] / max(1e-3, r["chain_cum_median_ms"]) for r in rs)
        sc = sum(r["chain_cum_median_ms"] for r in rs); sl = sum(r["lazy_median_ms"] for r in rs)
        print(f"| {fam} | {k} | {len(rs)} | {st.median([r['chain_cum_median_ms'] for r in rs]):.1f} | {st.median([r['chain_step_median_ms'] for r in rs]):.1f} | "
              f"{st.median([r['lazy_median_ms'] for r in rs]):.1f} | {st.median(rat):.2f} | {rat[max(0, math.ceil(0.9 * len(rat)) - 1)]:.2f} | {rat[-1]:.2f} | {sc:.0f} / {sl:.0f} |")
    print()
    print(f"{dropped} rows left out (the chain ended before the step, or a boolean failed).")

if __name__ == "__main__":
    a = sys.argv[1:]
    def opt(name, default=None, cast=str):
        return cast(a[a.index(name) + 1]) if name in a else default
    if a[:1] == ["proc"]:
        mode = opt("--mode", "measure")
        command = {"measure": "chain-exact-proc", "diag": "chain-exact-diag-proc", "time": "chain-exact-time-proc"}[mode]
        extra = {"measure": [], "diag": [opt("--factors", "1,4,16")], "time": [str(opt("--reps", 5, int))]}[mode]
        cmd_proc(a[1], opt("--seeds", 20, int), opt("--steps", 8, int), opt("--families", ["mixed", "generic"], lambda s: s.split(",")),
                 command=command, extra=extra)
    elif a[:1] == ["diag-summary"]:
        cmd_diag_summary(a[1:])
    elif a[:1] == ["time-summary"]:
        cmd_time_summary(a[1:])
    elif a[:1] == ["summary"]:
        cmd_summary(a[1:])
    else:
        sys.exit(__doc__)
