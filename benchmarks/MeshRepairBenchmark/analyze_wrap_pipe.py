# Summarizes BENCH_REPAIR=wrappipe runs (the alpha wrap fallback pipeline: raw wrap, simplification, quality pass):
# guarantees, offset band, fidelity, triangle quality of the raw wrap and of the result, time and its phases.
# Usage: python3 analyze_wrap_pipe.py run.jsonl [base.jsonl]   (with a base: paired per-mesh comparison)
#        --budget S   wall time budget per mesh: max(S seconds, --per-tri µs x raw-wrap triangles) (owner 2026-11-08:
#                     5 s and 50 µs, warm process); defaults 5 and 50
#        --per-tri U
import json, sys, statistics as st

PER_TRI = 50

def load(path):
    out = {}
    for line in open(path):
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            r = json.loads(line)
        except ValueError:
            continue
        out[r["file"]] = r
    return out

def q(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p * (len(v) - 1) + 0.5))] if v else float("nan")

def guarantees(r):
    """closed, valid, manifold (no bowties, no boundary), oriented, no exact self-intersections (-1: not checked)"""
    return (r.get("status") == "closed" and r.get("after_valid") is True and r.get("after_bowties") == 0
            and r.get("after_boundary_edges") == 0 and r.get("after_selfint_exact") == 0
            and r.get("after_inconsistent_orient_edges") == 0 and r.get("after_inverted_components") == 0
            and r.get("wrap_status") in ("Success", "PointLimitReached"))

def band(r, which="hi"):
    """certified Hausdorff result -> input (upper or lower bound) over (alpha + offset), both relative to the diagonal"""
    if f"hd_fwd_{which}_rel" not in r:
        return None
    return r[f"hd_fwd_{which}_rel"] / (1 / float(r["wrap_rel_alpha"]) + 1 / float(r["wrap_rel_offset"]))

def contract(r):
    """the documented distance for the result, over (alpha + offset): 1, plus 0.5 offset if simplified"""
    a, o = 1 / float(r["wrap_rel_alpha"]), 1 / float(r["wrap_rel_offset"])
    return (a + o + (0.5 * o if r.get("pipe_simplified") else 0)) / (a + o)

def summary(rs, budget, name):
    ok = [r for r in rs.values() if r.get("status") not in ("timeout_or_crash", "exception")]
    print(f"== {name}: {len(rs)} rows, {len(ok)} with a result, timeouts / crashes / exceptions "
          f"{sum(1 for r in rs.values() if r.get('status') in ('timeout_or_crash', 'exception'))}")
    g = [r for r in ok if guarantees(r)]
    print(f"guarantees met: {len(g)} / {len(ok)}; failures: "
          + ", ".join(f"{r['file']}({r.get('status')},si={r.get('after_selfint_exact')},{r.get('wrap_status')})" for r in ok if not guarantees(r))[:600])
    b = [band(r) for r in ok if band(r) is not None]
    print(f"band (hd fwd upper bound / (alpha + offset)): median {q(b, .5):.3f} p90 {q(b, .9):.3f} max {max(b):.3f}; > 1: {sum(1 for x in b if x > 1)}")
    # the documented value for the final output: alpha + offset for the wrap (class doc: "about"), plus the
    # simplification's certified 0.5 offset when it was simplified; a proven exceedance needs the lower bound above it
    lows = [(band(r, "lo"), contract(r), r["file"]) for r in ok if band(r, "lo") is not None]
    proven = sorted((x / c, f) for x, c, f in lows if x > c)
    ups = sum(1 for r in ok if band(r) is not None and band(r) > contract(r))
    print(f"contract alpha + offset (+ 0.5 offset if simplified): proven above it (lower bound) {len(proven)}, upper bound above it {ups}"
          + (": " + ", ".join(f"{f} {x:.3f}" for x, f in reversed(proven[-10:])) if proven else ""))
    for k in ["fm_f_0001", "fm_f_001", "iou", "hd_back_hi_rel"]:
        v = [r[k] for r in ok if r.get(k) is not None]
        if v:
            print(f"{k}: median {q(v, .5):.4f} p10 {q(v, .1):.4f} p90 {q(v, .9):.4f}")
    for p in ["raw", "after"]:
        v = [r for r in ok if f"{p}_angle_min" in r]
        if not v:
            continue
        lt = sum(r[f"{p}_angle_lt05"] for r in v); gt = sum(r[f"{p}_angle_gt170"] for r in v)
        mlt = sum(1 for r in v if r[f"{p}_angle_lt05"] > 0); mgt = sum(1 for r in v if r[f"{p}_angle_gt170"] > 0)
        p01 = [r[f"{p}_angle_p01"] for r in v]
        print(f"{p}: tris < 0.5 deg {lt} ({mlt} meshes), > 170 deg {gt} ({mgt} meshes); min angle global {min(r[f'{p}_angle_min'] for r in v):.3f} "
              f"median {q([r[f'{p}_angle_min'] for r in v], .5):.3f}; max global {max(r[f'{p}_angle_max'] for r in v):.2f} "
              f"median {q([r[f'{p}_angle_max'] for r in v], .5):.2f}; p01 median {q(p01, .5):.2f} min {min(p01):.2f}; "
              f"> 150: {sum(r[f'{p}_angle_gt150'] for r in v)}; tris median {q([r.get(p + '_tris', 0) for r in v], .5):.0f}")
    t = [r["pipe_ms"] / 1000 for r in ok if "pipe_ms" in r]
    def limit(r):
        return max(budget, PER_TRI * 1e-6 * r.get("raw_tris", 0))
    over = sorted(((r["pipe_ms"] / 1000, r["file"], limit(r)) for r in ok if "pipe_ms" in r and r["pipe_ms"] / 1000 > limit(r)), reverse=True)
    warm = sum(1 for r in ok if r.get("warmup") in ("soup", "same"))
    print(f"pipeline time (s, {'warm' if warm == len(ok) else f'{warm} of {len(ok)} warm'}): median {q(t, .5):.2f} p90 {q(t, .9):.2f} "
          f"p99 {q(t, .99):.2f} max {max(t):.2f}; over the budget max({budget} s, {PER_TRI} µs x raw triangles): {len(over)} of {len(t)} "
          f"(+ {len(rs) - len(ok)} without a result); pass {len(t) - len(over)}")
    if over:
        print("  over: " + ", ".join(f"{f} {x:.1f}/{b:.1f}" for x, f, b in over[:15]))
    cold = [r["pipe_ms_cold"] / 1000 for r in ok if "pipe_ms_cold" in r]
    if cold:
        print(f"cold (first run in the process): median {q(cold, .5):.2f} max {max(cold):.2f}")
    # phase shares, all meshes and over the budget
    for label, sel in [("all", ok), ("over the budget", [r for r in ok if "pipe_ms" in r and r["pipe_ms"] / 1000 > limit(r)])]:
        tot = {}
        for r in sel:
            for k, ms in r.get("pipe_phases", {}).items():
                tot[k] = tot.get(k, 0) + ms
        T = sum(r.get("pipe_ms", 0) for r in sel)
        if T:
            print(f"phases ({label}, {len(sel)} meshes, sum {T / 1000:.0f} s): "
                  + ", ".join(f"{k} {100 * v / T:.1f}%" for k, v in sorted(tot.items(), key=lambda kv: -kv[1])))
    return ok

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    global PER_TRI
    budget = float(sys.argv[sys.argv.index("--budget") + 1]) if "--budget" in sys.argv else 5
    PER_TRI = float(sys.argv[sys.argv.index("--per-tri") + 1]) if "--per-tri" in sys.argv else 50
    for opt in ("--budget", "--per-tri"):
        if opt in sys.argv:
            args.remove(sys.argv[sys.argv.index(opt) + 1])
    runs = [load(a) for a in args]
    for a, rs in zip(args, runs):
        summary(rs, budget, a)
    if len(runs) == 2:
        a, b = runs
        common = [f for f in a if f in b and "pipe_ms" in a[f] and "pipe_ms" in b[f]]
        ratio = [a[f]["pipe_ms"] / max(1, b[f]["pipe_ms"]) for f in common]
        print(f"== paired ({len(common)}): time ratio median {q(ratio, .5):.3f} p10 {q(ratio, .1):.3f} p90 {q(ratio, .9):.3f}; "
              f"identical results {sum(1 for f in common if a[f].get('after_hash') == b[f].get('after_hash'))}")

main()
