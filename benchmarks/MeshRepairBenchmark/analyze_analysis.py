#!/usr/bin/env python3
"""Summary of BENCH_REPAIR=analysis runs (AnalysisBench.cs): robustness and invariants of MeshWallThickness,
MeshDeviation and PrimitiveFit3 per Thingi10K category. With more runs (eg the meters and geo transforms of
the same meshes), also compares the relative values per mesh: they must not depend on scale or position.
Usage: analyze_analysis.py results/analysis.jsonl [results/analysis_std_meters.jsonl ...]"""
import csv, json, os, statistics, sys

def load(f):
    return {r["file"]: r for r in (json.loads(l) for l in open(f) if l.strip())}

def num(v):
    try:
        x = float(v)
        return None if x != x else x
    except (TypeError, ValueError):
        return None

here = os.path.dirname(os.path.abspath(__file__))
cat = {}
sel = os.path.join(here, "data", "selection.csv")
if os.path.exists(sel):
    for row in csv.DictReader(open(sel)):
        cat[os.path.basename(row["path"])] = row["category"]

runs = [(f, load(f)) for f in sys.argv[1:]]
f0, base = runs[0]
groups = {}
for name, r in base.items():
    groups.setdefault(cat.get(name, "?"), []).append(r)

def frac(rs, pred):
    return sum(1 for r in rs if pred(r))

def med(rs, key):
    v = [num(r.get(key)) for r in rs]
    v = [x for x in v if x is not None]
    return statistics.median(v) if v else float("nan")

print(f"{f0}: {len(base)} meshes")
cols = ["category", "n", "fail", "ray NaN", "ins>ray", "selfdev>0", "sign dis.", "HD viol.", "fit !ok", "nondet", "rot dq>1%", "ms"]
print(" | ".join(cols))
for c in sorted(groups) + ["all"]:
    rs = list(base.values()) if c == "all" else groups[c]
    fail = frac(rs, lambda r: r.get("status") != "ok")
    ok = [r for r in rs if r.get("status") == "ok"]
    ray_nan = med(ok, "wt_ray_nan_frac")
    ins_above = frac(ok, lambda r: (r.get("wt_inscribed_above_ray") or 0) > 0)
    selfdev = frac(ok, lambda r: (num(r.get("dev_self_max_rel")) or 0) > 1e-12)
    sign = frac(ok, lambda r: (num(r.get("dev_sign_disagree_frac")) or 0) > 0.01)
    hd = frac(ok, lambda r: r.get("dev_reduced_bound_violated") is True)
    prims = ["plane", "sphere", "cylinder", "cone"]
    fit_bad = frac(ok, lambda r: any(r.get(f"fit_{p}_max_err_ok") is False for p in prims))
    nondet = frac(ok, lambda r: any(r.get(f"fit_{p}_deterministic") is False for p in prims))
    # rotated input: a different model of (nearly) the same quality is a tie between equal features, a
    # different quality means RANSAC missed the best model in one of the runs
    rot = frac(ok, lambda r: any(abs((num(r.get(f"fit_{p}_rot_inlier_frac")) or 0) - (num(r.get(f"fit_{p}_inlier_frac")) or 0)) > 0.01 for p in prims))
    ms = med(ok, "analysis_ms")
    print(" | ".join(str(x) for x in [c, len(rs), fail, f"{ray_nan:.3f}", ins_above, selfdev, sign, hd, fit_bad, nondet, rot, f"{ms:.0f}"]))

# details of failures and invariant violations
print("\nfailures:")
for name, r in sorted(base.items()):
    if r.get("status") != "ok":
        print(f"  {name} [{cat.get(name, '?')}]: {r.get('status')} {r.get('stage', '')} {r.get('error', '')} {r.get('trace', '')}")
print("\nsign disagreement > 1% (pseudo-normal vs winding number), by category of the input:")
for name, r in sorted(base.items()):
    v = num(r.get("dev_sign_disagree_frac"))
    if v is not None and v > 0.01:
        print(f"  {name} [{cat.get(name, '?')}]: {v:.3f}")
print("\nclean meshes with any sign disagreement:")
for name, r in sorted(base.items()):
    v = num(r.get("dev_sign_disagree_frac"))
    if cat.get(name) == "clean" and v:
        print(f"  {name}: {v:.4f}")

# invariance across transforms: relative values per mesh
keys = ["wt_ray_median_rel", "wt_inscribedsphere_median_rel", "wt_shapediameter_median_rel",
        "wt_ray_nan_frac", "dev_reduced_max_rel", "fit_plane_inlier_frac", "fit_cylinder_inlier_frac"]
for f, other in runs[1:]:
    print(f"\n{f0} vs {f}: meshes whose relative value differs by more than 1e-6 (relative) / 1e-3 (fractions)")
    for k in keys:
        diffs = []
        for name, r in base.items():
            a, b = num(r.get(k)), num(other.get(name, {}).get(k))
            if a is None or b is None:
                continue
            tol = 1e-3 if "frac" in k else 1e-6 * max(abs(a), 1e-12)
            if abs(a - b) > tol:
                diffs.append((abs(a - b) / max(abs(a), 1e-12), name))
        diffs.sort(reverse=True)
        worst = ", ".join(f"{n} {d:.2g}" for d, n in diffs[:3])
        print(f"  {k}: {len(diffs)} {worst}")
