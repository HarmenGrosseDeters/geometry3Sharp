# Summarizes BENCH_REPAIR=wrappipe runs (the alpha wrap fallback pipeline: raw wrap, simplification, quality pass):
# guarantees, offset band, fidelity, triangle quality of the raw wrap and of the result, time and its phases.
# Usage: python3 analyze_wrap_pipe.py run.jsonl [base.jsonl]   (with a base: paired per-mesh comparison)
#        --budget S   wall time budget per mesh in seconds (default 5)
import json, sys, statistics as st

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
    """closed, valid, manifold (no bowties, no boundary), no exact self-intersections (-1: not checked)"""
    return (r.get("status") == "closed" and r.get("after_valid") is True and r.get("after_bowties") == 0
            and r.get("after_boundary_edges") == 0 and r.get("after_selfint_exact") == 0
            and r.get("wrap_status") in ("Success", "PointLimitReached"))

def band(r):
    """certified Hausdorff result -> input over (alpha + offset), both relative to the diagonal"""
    if "hd_fwd_hi_rel" not in r:
        return None
    return r["hd_fwd_hi_rel"] / (1 / float(r["wrap_rel_alpha"]) + 1 / float(r["wrap_rel_offset"]))

def summary(rs, budget, name):
    ok = [r for r in rs.values() if r.get("status") not in ("timeout_or_crash", "exception")]
    print(f"== {name}: {len(rs)} rows, {len(ok)} with a result, timeouts / crashes / exceptions "
          f"{sum(1 for r in rs.values() if r.get('status') in ('timeout_or_crash', 'exception'))}")
    g = [r for r in ok if guarantees(r)]
    print(f"guarantees met: {len(g)} / {len(ok)}; failures: "
          + ", ".join(f"{r['file']}({r.get('status')},si={r.get('after_selfint_exact')},{r.get('wrap_status')})" for r in ok if not guarantees(r))[:600])
    b = [band(r) for r in ok if band(r) is not None]
    print(f"band (hd fwd / (alpha + offset)): median {q(b, .5):.3f} p90 {q(b, .9):.3f} max {max(b):.3f}; > 1: {sum(1 for x in b if x > 1)}")
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
    over = sorted(((r["pipe_ms"] / 1000, r["file"]) for r in ok if r.get("pipe_ms", 0) > budget * 1000), reverse=True)
    print(f"pipeline time (s): median {q(t, .5):.2f} p90 {q(t, .9):.2f} p99 {q(t, .99):.2f} max {max(t):.2f}; "
          f"> {budget} s: {len(over)} of {len(t)} (+ {len(rs) - len(ok)} without a result)")
    # phase shares, all meshes and over the budget
    for label, sel in [("all", ok), (f"> {budget} s", [r for r in ok if r.get("pipe_ms", 0) > budget * 1000])]:
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
    budget = float(sys.argv[sys.argv.index("--budget") + 1]) if "--budget" in sys.argv else 5
    if "--budget" in sys.argv:
        args.remove(sys.argv[sys.argv.index("--budget") + 1])
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
