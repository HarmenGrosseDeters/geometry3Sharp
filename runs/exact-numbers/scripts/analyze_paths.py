# Upper-bound table of the exact-number study from `Probe list` rows (instrumented local library).
# Usage: python3 analyze_paths.py <label>=<results.jsonl> ...  [--per-case]
# Shares are of the operation's CPU time (all threads), after subtracting the timer overhead (calls * pair_ns) from
# both the path time and the CPU time. Categories:
#   bigint     System.Numerics.BigInteger fallbacks (double-input *_bigint, arrangement and classification exact stages,
#              exact point constructions, planes, local points)
#   expansion  Shewchuk stages B-D (AdaptivePredicates)
#   fixed      fixed-width limb paths (orient3d_int, FixedArith on FixedPoint3)
import json, sys, statistics as st

BIG = ["o2.bigint", "o3.bigint", "ic.bigint", "insphere.bigint", "arr.orient.big", "arr.incircle.big", "arr.along.big",
       "arr.compare.big", "arr.exact_point.big", "arr.planes", "arr.local_get.big", "bool.side.big", "bool.orient3d.big",
       "bool.centroid.big", "bool.pair_around_edge.big", "arr.same_plane.big", "arr.edge_planes.big"]
EXP = ["o2.adaptive", "o3.adaptive", "ic.adaptive"]
FIX = ["o3.int", "arr.orient.fixed", "arr.incircle.fixed", "arr.along.fixed", "arr.compare.fixed"]
DOUBLE_INPUT_BIG = ["o2.bigint", "o3.bigint", "ic.bigint", "insphere.bigint"]

def load(path):
    rows = []
    for l in open(path):
        try: r = json.loads(l)
        except ValueError: continue
        if "paths" in r: rows.append(r)
    return rows

def cat(r, names):
    ms = calls = mb = 0.0
    for n in names:
        if n in r["paths"]:
            c, t, b = r["paths"][n]
            calls += c; ms += max(0.0, t - c * r["pair_ns"] * 1e-6); mb += b
    return calls, ms, mb

def shares(r):
    timed_calls = sum(v[0] for k, v in r["paths"].items() if not k.startswith("pt."))
    cpu = max(1.0, r["cpu_ms"] - timed_calls * r["pair_ns"] * 1e-6)
    out = {"cpu": cpu, "wall": r["ms"], "alloc": r["alloc_mb"], "gc": r["gc_pause_ms"]}
    for label, names in (("big", BIG), ("exp", EXP), ("fix", FIX), ("dbig", DOUBLE_INPUT_BIG)):
        c, ms, mb = cat(r, names)
        out[label + "_ms"] = ms; out[label + "_share"] = ms / cpu; out[label + "_mb"] = mb; out[label + "_calls"] = c
    out["gc_share"] = r["gc_pause_ms"] / max(1.0, r["ms"])
    out["big_alloc_share"] = out["big_mb"] / max(1e-9, r["alloc_mb"])
    pb = r["paths"].get("pt.box", [0, 0, 0]); pf = r["paths"].get("pt.fixed", [0, 0, 0])
    out["pt_box"] = pb[0]; out["pt_fixed"] = pf[0]; out["pt_box_mb"] = pb[2] * 1.0
    return out

def pct(x): return f"{100 * x:.1f}"

def main():
    per_case = "--per-case" in sys.argv
    sets = [a.split("=", 1) for a in sys.argv[1:] if "=" in a]
    print("| stratum | ops | CPU s (sum) | BigInteger % of CPU: total / median / max | of which double-input *_bigint % | expansions % (total / max) | fixed-width % (total / max) | GC pause % of wall (total / max) | alloc GB | allocated inside BigInteger paths % |")
    print("|---|---|---|---|---|---|---|---|---|---|")
    for label, path in sets:
        rows = [(r, shares(r)) for r in load(path)]
        if not rows: continue
        cpu = sum(s["cpu"] for _, s in rows); wall = sum(s["wall"] for _, s in rows)
        def tot(k): return sum(s[k] for _, s in rows) / cpu
        def mx(k): return max(s[k] for _, s in rows)
        print(f"| {label} | {len(rows)} | {cpu / 1000:.1f} | {pct(tot('big_ms'))} / {pct(st.median(s['big_share'] for _, s in rows))} / {pct(mx('big_share'))} | {pct(tot('dbig_ms'))} | {pct(tot('exp_ms'))} / {pct(mx('exp_share'))} | {pct(tot('fix_ms'))} / {pct(mx('fix_share'))} | {pct(sum(s['gc'] for _, s in rows) / wall)} / {pct(mx('gc_share'))} | {sum(s['alloc'] for _, s in rows) / 1024:.1f} | {pct(sum(s['big_mb'] for _, s in rows) / max(1e-9, sum(s['alloc'] for _, s in rows)))} |")
    if per_case:
        print()
        print("| case | op | tris A+B | wall s | CPU s | BigInteger % | top BigInteger paths (ms) | expansions % | fixed % | GC % wall | alloc MB | in BigInteger % | points fixed / fallback |")
        print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
        for label, path in sets:
            for r in load(path):
                s = shares(r)
                top = sorted(((max(0.0, v[1] - v[0] * r["pair_ns"] * 1e-6), k) for k, v in r["paths"].items() if k in BIG), reverse=True)[:3]
                tops = ", ".join(f"{k} {t:.0f}" for t, k in top if t >= 1)
                print(f"| {r['case']} | {r['op']} | {r['tris_a'] + r['tris_b']} | {r['ms'] / 1000:.2f} | {s['cpu'] / 1000:.2f} | {pct(s['big_share'])} | {tops} | {pct(s['exp_share'])} | {pct(s['fix_share'])} | {pct(s['gc_share'])} | {r['alloc_mb']:.0f} | {pct(s['big_alloc_share'])} | {int(s['pt_fixed'])} / {int(s['pt_box'])} |")

main()
