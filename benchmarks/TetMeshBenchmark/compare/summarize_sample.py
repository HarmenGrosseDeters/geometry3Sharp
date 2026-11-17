#!/usr/bin/env python3
"""Summary and go / no-go of the tet-meshing runner sample (docs/design/TET_MESHING.md section 17.5, stage P1).

Usage: summarize_sample.py <dir> [base_alpha=20] [candidate alphas, coarsest first; default 40 80]
Reads <dir>/a<alpha>.csv (rows of `TetMeshBenchmark refine`, no header; failure rows <id>.stl,<time>,1,<STATUS>) and prints Markdown:
per alpha the counts and medians, then the rule of 17.5 for each candidate against the base (master's default) and the chosen one.
Criterion 4 of 17.5 (tets against fTetWild) is a subset criterion and is not evaluated here.
"""
import os
import statistics
import sys

COLS = ("name,time_s,threads,tets,vertices,neg_tets,zero_tets,inverted,valid,boundary_tris,boundary_self_intersecting,"
        "re_worst,re_p5,re_median,re_gt2_share,mindih_worst,mindih_p1,mindih_p5,mindih_median,dih_lt10_share,dih_ge10_all,"
        "dev_out_max_rel,dev_out_mean_rel,dev_in_max_rel,dev_in_mean_rel,status,wrap_s,refine_s,us_per_tet,counts").split(",")

GE10_SHARE = 0.99       # criterion 1: every tet >= 10 degrees on >= 99 % of the candidate's Success meshes
DEV_RATIO = 0.85        # criterion 2: median over meshes of the per-mesh ratio candidate / base of dev_in_max_rel
TIME_RATIO = 2.0        # criterion 3: median time <= 2x the base's median time over the same meshes
SLOW_S = 20.0           # criterion 3: a mesh above 20 s whose input has fewer than 1M triangles is a no-go
BIG_TRIS = 1_000_000


def load(path):
    rows = {}
    if not os.path.exists(path):
        return rows
    for line in open(path, encoding="utf-8"):
        p = line.rstrip("\n").split(",")
        if not p or not p[0]:
            continue
        if len(p) == len(COLS):
            r = dict(zip(COLS, p))
            r["ok"] = r["status"] == "Success"
            r["tris"] = next((int(c.split("=")[1]) for c in r["counts"].split() if c.startswith("input_tris=")), -1)
        else:
            r = {"name": p[0], "ok": False, "status": p[3] if len(p) > 3 else (p[1] if len(p) > 1 else "?")}
        rows[r["name"]] = r
    return rows


def med(v):
    return statistics.median(v) if v else float("nan")


def describe(alpha, rows):
    ok = [r for r in rows.values() if r["ok"]]
    f = lambda k: [float(r[k]) for r in ok]
    fails = {}
    for r in rows.values():
        if not r["ok"]:
            fails[r["status"]] = fails.get(r["status"], 0) + 1
    print(f"| / {alpha} | {len(rows)} | {len(ok)} | {sum(r['valid'] == 'yes' for r in ok)} | "
          f"{sum(r['boundary_self_intersecting'] == 'yes' for r in ok)} | {sum(r['dih_ge10_all'] == 'yes' for r in ok)} | "
          f"{min(f('mindih_worst'), default=float('nan')):.3g} | {med(f('dev_in_max_rel')):.3g} | {med(f('dev_out_max_rel')):.3g} | "
          f"{med(f('tets')):.0f} | {med(f('time_s')):.3g} / {max(f('time_s'), default=float('nan')):.3g} | "
          f"{' '.join(f'{k}={v}' for k, v in sorted(fails.items())) or '-'} |")


def judge(alpha, cand, base):
    """returns (passed, lines) for the rule of 17.5"""
    lines, nogo, fail = [], [], []
    for n, b in base.items():
        if b["ok"] and b["valid"] == "yes":
            c = cand.get(n)
            if c is None or not c["ok"]:
                nogo.append(f"{n}: Success at the base, {c['status'] if c else 'missing'} here")
            elif c["valid"] != "yes":
                nogo.append(f"{n}: not valid ({c['valid']})")
    for n, c in cand.items():
        if c["ok"] and c["boundary_self_intersecting"] == "yes":
            nogo.append(f"{n}: boundary self-intersection")
    ok = [c for c in cand.values() if c["ok"]]
    ge10 = sum(c["dih_ge10_all"] == "yes" for c in ok) / max(1, len(ok))
    below = sorted((float(c["mindih_worst"]), c["name"]) for c in ok if c["dih_ge10_all"] != "yes")
    lines.append(f"- criterion 1: every tet >= 10 degrees on {ge10:.2%} of {len(ok)} Success meshes (need >= {GE10_SHARE:.0%})"
                 + (f"; below: {', '.join(f'{n} {d:.2f}' for d, n in below[:12])}" + (" ..." if len(below) > 12 else "") if below else ""))
    if ge10 < GE10_SHARE:
        fail.append("1")
    common = [n for n in cand if cand[n]["ok"] and base.get(n, {}).get("ok")]
    ratios = [float(cand[n]["dev_in_max_rel"]) / max(1e-12, float(base[n]["dev_in_max_rel"])) for n in common]
    dr = med(ratios)
    lines.append(f"- criterion 2: input-to-boundary max, median of per-mesh ratios {dr:.3f} over {len(common)} meshes (need <= {DEV_RATIO})")
    if not dr <= DEV_RATIO:
        fail.append("2")
    tc, tb = med([float(cand[n]["time_s"]) for n in common]), med([float(base[n]["time_s"]) for n in common])
    lines.append(f"- criterion 3: median time {tc:.3g} s against {tb:.3g} s at the base, ratio {tc / tb:.2f} (need <= {TIME_RATIO})")
    if not tc <= TIME_RATIO * tb:
        fail.append("3")
    slow = sorted((float(c["time_s"]), c["name"], c["tris"]) for c in ok if float(c["time_s"]) > SLOW_S)
    for t, n, tris in slow:
        small = 0 <= tris < BIG_TRIS
        lines.append(f"  - above {SLOW_S:.0f} s (an issue with a diagnosis): {n} {t:.1f} s, {tris} input triangles"
                     + (" -> no-go (fewer than 1M triangles)" if small else ""))
        if small:
            nogo.append(f"{n}: {t:.1f} s with {tris} input triangles")
    for n in nogo:
        lines.append(f"- no-go: {n}")
    passed = not nogo and not fail
    lines.insert(0, f"**alpha / {alpha}: {'GO' if passed else 'no go'}**" + (f" (failed: {', '.join(fail)})" if fail else "")
                 + (f" ({len(nogo)} no-go items)" if nogo else ""))
    return passed, lines


def main():
    d = sys.argv[1]
    base_alpha = sys.argv[2] if len(sys.argv) > 2 else "20"
    cands = sys.argv[3:] or ["40", "80"]
    data = {a: load(os.path.join(d, f"a{a}.csv")) for a in [base_alpha] + cands}
    print("| alpha (diagonal /) | rows | Success | valid | self-intersecting | every tet >= 10 | worst min dihedral | "
          "input to boundary max, median | boundary to input max, median | tets, median | time median / max (s) | failures |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|")
    for a in [base_alpha] + cands:
        describe(a, data[a])
    print()
    chosen = None
    for a in cands:
        passed, lines = judge(a, data[a], data[base_alpha])
        print("\n".join(lines))
        print()
        if passed and chosen is None:
            chosen = a
    print(f"**Chosen by the rule of 17.5: {'alpha / ' + chosen if chosen else 'none (stop and report)'}** "
          "(the coarsest candidate that passes; criterion 4 is checked on the subset)")


if __name__ == "__main__":
    main()
