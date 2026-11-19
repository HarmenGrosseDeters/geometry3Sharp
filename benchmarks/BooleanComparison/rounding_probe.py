#!/usr/bin/env python3
"""Driver and summary of the rounding probes (RoundingProbe.cs, branch runs/rounding-probes; measurement only).

  rounding_probe.py screen rows.jsonl [--seeds 30] [--steps 8]     chain-exact-proc mixed per seed: today's chain and the one pass per step
  rounding_probe.py proc screen.jsonl out.jsonl [--from-step 5]    rounding-probe-proc for each (seed, step) whose one pass is unclean and
                                                                    whose chained result is clean (steps >= from-step)
  rounding_probe.py summary rows.jsonl [more.jsonl ...]            the tables of Q1, Q2 and Q3 (markdown)

The tool is `dotnet <BooleanComparison.dll>` (BOOLCMP_TOOL, default the Release build). Procedural meshes only; nothing derived from
external models is written except the rows (case ids, counts, numbers)."""
import json, os, subprocess, sys, concurrent.futures as cf

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.environ.get("BOOLCMP_TOOL") or os.path.join(HERE, "bin", "Release", "net10.0", "BooleanComparison.dll")
CLEAN = ("", "empty")

def run(args, timeout=3600):
    s = subprocess.run(["dotnet", TOOL] + args, capture_output=True, text=True, timeout=timeout)
    return [json.loads(l) for l in s.stdout.splitlines() if l.startswith("{")], s.stderr

def screen(out, seeds=30, steps=8, workers=4):
    def one(seed):
        rows, err = run(["chain-exact-proc", "mixed", str(seed), str(steps)])
        return seed, rows, err
    with cf.ThreadPoolExecutor(workers) as ex, open(out, "w") as f:
        for seed, rows, err in ex.map(one, range(1, seeds + 1)):
            for r in rows:
                r["seed"] = seed
                f.write(json.dumps(r) + "\n")
            if not rows:
                print("seed", seed, "no rows", err[-200:], file=sys.stderr)

def selected(path, from_step):
    sel = []
    for l in open(path):
        r = json.loads(l)
        if r.get("step", 0) >= from_step and r.get("chained_reason") in CLEAN and r.get("onepass_reason") not in CLEAN and r.get("onepass_ok", True):
            sel.append((r["seed"], r["step"]))
    return sel

def proc(screen_path, out, from_step=5, workers=4):
    sel = selected(screen_path, from_step)
    print(len(sel), "cases:", sel, flush=True)
    def one(c):
        rows, err = run(["rounding-probe-proc", "mixed", str(c[0]), str(c[1]), "x"])
        return c, rows, err
    with cf.ThreadPoolExecutor(workers) as ex, open(out, "w") as f:
        for c, rows, err in ex.map(one, sel):
            for r in rows:
                r["class"] = "rounding-probe"
                f.write(json.dumps(r) + "\n")
            f.flush()
            if not rows:
                print("case", c, "no rows", err[-300:], file=sys.stderr)

# ---------------------------------------------------------------- summary

def load(paths):
    rows = {}
    for p in paths:
        for l in open(p):
            if l.startswith("{"):
                r = json.loads(l)
                if r.get("class") in ("rounding-probe", "abc-rounding-probe") and "step" in r:
                    rows[(r.get("file") or (r.get("family"), r.get("seed")), r["step"])] = r      # the last row of a case (partial rows come first)
    return list(rows.values())

def name(r):
    if r.get("family") == "abc":
        return f"{r.get('file', '?')}"
    return f"{r['family']} s{r['seed']} k{r['step']}"

def fmt(x, nd=3):
    return f"{x:.{nd}g}" if isinstance(x, float) else str(x)

def outcome(reason):
    return "clean" if reason == "" else "empty-clean" if reason == "empty" else "unclean"

def summary(paths):
    rows = load(paths)
    if not rows:
        print("no rows")
        return
    groups = {}
    for r in rows:
        groups.setdefault("abc" if r.get("family") == "abc" else "procedural", []).append(r)
    for g, rs in groups.items():
        print(f"\n## {g}: {len(rs)} cases\n")
        # outcome counts
        cnt = {}
        for r in rs:
            for k, key in (("chained", "chained_reason"), ("one pass, first rounding only", "first_rounding_reason"), ("one pass, SafeRounding", "rounded_reason")):
                if key in r:
                    cnt.setdefault(k, {}).setdefault(outcome(r[key]), 0)
                    cnt[k][outcome(r[key])] += 1
        print("| result | clean | empty-clean | unclean |\n|---|---|---|---|")
        for k, c in cnt.items():
            print(f"| {k} | {c.get('clean', 0)} | {c.get('empty-clean', 0)} | {c.get('unclean', 0)} |")
        # Q1
        print("\n### Q1: components of the exact result\n")
        print("| case | exact tris | comps (real/artefact/unclear) | lost | broken | kept ok | exact volume | rounded volume | thinnest t_avg (ulps) | thinnest t_min (ulps) | largest comp t_avg (ulps) | in X-pair results (>=0.99 of volume) | chained-exact comps (thin/total) |\n|---|---|---|---|---|---|---|---|---|---|---|---|---|")
        for r in rs:
            if "summary_by_class" not in r:
                print(f"| {name(r)} | error: {r.get('error', '?')} |")
                continue
            s = r["summary_by_class"]
            n = "/".join(str(s[c]["n"]) for c in ("real", "artefact", "unclear"))
            lost = sum(s[c]["lost"] for c in s); broken = sum(s[c]["broken"] for c in s); ok = sum(s[c]["ok"] for c in s)
            th = r["components_thinnest"][0] if r.get("components_thinnest") else None
            big = r["components_top"][0] if r.get("components_top") else None
            pairs = ", ".join(p["pair"] for p in r.get("unchained_pairs", []) if p["fraction_of_exact_volume"] >= 0.99) or "-"
            ce = r.get("chained_exact")
            cex = "-"
            if ce:
                bc = ce["by_class"]
                cex = f"{bc['artefact']['n']}/{ce['components']}"
            print(f"| {name(r)} | {r['exact_tris']} | {n} | {lost} | {broken} | {ok} | {fmt(r['exact_volume'])} | {fmt(r['rounded_volume'])} | "
                  f"{fmt(th['t_avg_ulps']) if th else '-'} | {fmt(th['t_min_ulps']) if th else '-'} | {fmt(big['t_avg_ulps']) if big else '-'} | {pairs} | {cex} |")
        # Q2
        print("\n### Q2: defects left after SafeRounding and quad flips\n")
        print("| case | reason after SafeRounding | defects: degenerate / intersecting pairs / triangles | quad candidates | quad-fixable | flips applied | reason after flips |\n|---|---|---|---|---|---|---|")
        tot = [0, 0, 0, 0]
        for r in rs:
            q = r.get("q2")
            if not q:
                continue
            print(f"| {name(r)} | {r.get('rounded_reason')} | {q['degenerate']} / {q['pairs']} / {q['defect_triangles']} | {q['candidates']} | {q['quad_fixable']} | {q['flips_applied']} | {q['reason_after'] or 'clean'} |")
            tot[0] += 1; tot[1] += q["defect_triangles"]; tot[2] += q["quad_fixable"]; tot[3] += q["clean_after"]
        qf = [r for r in rs if r.get("q2_first_rounding")]
        if qf:
            print("\nSanity of the quad detector on the first rounding (before any repair; a sample of at most 150 candidate flips per case):\n")
            print("| case | defect triangles | candidates | quad-fixable in the sample | flips applied | reason after |\n|---|---|---|---|---|---|")
            for r in qf:
                q = r["q2_first_rounding"]
                print(f"| {name(r)} | {q['defect_triangles']} | {q['candidates']} | {q['quad_fixable_in_sample']} | {q['flips_applied']} | {q['reason_after'] or 'clean'} |")
        print(f"\nCases with defects after SafeRounding: {tot[0]}; defect triangles: {tot[1]}; quad-fixable: {tot[2]}; cases clean after the flips: {tot[3]}.")
        # Q3
        print("\n### Q3: the exact result rounded about an origin (then the same repair)\n")
        agg = q3_aggregate(rs)
        print("| origin | runs | clean | empty-clean | unclean |\n|---|---|---|---|---|")
        for o, a in agg.items():
            print(f"| {o} | {a[0]} | {a[1]} | {a[2]} | {a[3]} |")
        print()
        print("| case | origin | max abs coordinate | result | tris | components | volume / exact | removed components |\n|---|---|---|---|---|---|---|---|")
        for r in rs:
            for q in r.get("q3", []):
                print(f"| {name(r)} | {q['origin']} | {fmt(q['max_abs_coord'])} | {outcome(q['reason'])} ({q['reason'] or 'clean'}) | {q['tris']} | {q['components']} | {fmt(q['volume_ratio_to_exact'])} | {q['removed_components']} |")

def q3_aggregate(rows):
    agg = {}
    for r in rows:
        for q in r.get("q3", []):
            o = q["origin"] if not q["origin"].startswith("control") else "control"
            a = agg.setdefault(o, [0, 0, 0, 0])
            a[0] += 1
            a[{"clean": 1, "empty-clean": 2, "unclean": 3}[outcome(q["reason"])]] += 1
    return agg

if __name__ == "__main__":
    a = sys.argv[1:]
    if a[:1] == ["screen"]:
        screen(a[1], int(a[a.index("--seeds") + 1]) if "--seeds" in a else 30, int(a[a.index("--steps") + 1]) if "--steps" in a else 8)
    elif a[:1] == ["proc"]:
        proc(a[1], a[2], int(a[a.index("--from-step") + 1]) if "--from-step" in a else 5)
    elif a[:1] == ["summary"]:
        summary(a[1:])
    else:
        print(__doc__)
