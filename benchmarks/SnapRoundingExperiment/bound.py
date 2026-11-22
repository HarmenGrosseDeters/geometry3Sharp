#!/usr/bin/env python3
"""The upper-bound measurement of docs/research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md (GuaranteeBound.cs).

  python3 bound.py run <out.jsonl> <set>... [-j 4] [--timeout 600]   one `bound <case>` per process, resumable
  python3 bound.py tables <out.jsonl>...                             the tables of the note

Sets: scale, clusters, utm, booleans, cylinder (the tool's `list <set>`).
"""
import argparse, json, os, statistics, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(HERE, "bin", "Release", "net10.0", "SnapRoundingExperiment.dll")


def run(args):
    cases = []
    for s in args.sets:
        out = subprocess.run(["dotnet", TOOL, "list", s], capture_output=True, text=True, check=True).stdout
        cases += [line for line in out.splitlines() if line]
    done = set()
    if os.path.exists(args.out):
        with open(args.out) as f:
            done = {json.loads(line)["case"] for line in f if line.startswith("{")}
    todo = [c for c in cases if c not in done]

    def one(case):
        try:
            p = subprocess.run(["dotnet", TOOL, "bound", case], capture_output=True, text=True, timeout=args.timeout)
            rows = [l for l in p.stdout.splitlines() if l.startswith("{")]
            if rows:
                return rows[-1]
        except subprocess.TimeoutExpired:
            pass
        return json.dumps({"case": case, "ok": False, "error": "timeout or crash (%d s)" % args.timeout})

    with ThreadPoolExecutor(args.j) as pool, open(args.out, "a") as f:
        for row in pool.map(one, todo):
            f.write(row + "\n")
            f.flush()
    print("%d cases run, %d already there" % (len(todo), len(done)))


def family(case):
    p = case.split(":")
    return {"cluster": "clusters", "bool": "booleans"}.get(p[0], p[0])


def tables(args):
    rows = []
    for name in args.files:
        with open(name) as f:
            rows += [json.loads(line) for line in f if line.startswith("{")]
    ok = [r for r in rows if r.get("ok")]
    fams = []
    for r in rows:
        if family(r["case"]) not in fams:
            fams.append(family(r["case"]))

    print("## Today: what SafeRounding leaves and removes\n")
    print("| set | cases | failed | plain rounding clean | SafeRounding: rounding clean | clean | at the effort limit | results with removed components | removed components | of them thinner than one cell | of them one cell or thicker |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
    for fam in fams + ["all"]:
        a = [r for r in rows if fam == "all" or family(r["case"]) == fam]
        o = [r for r in a if r.get("ok")]
        rem = [c for r in o for c in r.get("removed", [])]
        thin = sum(1 for c in rem if c["thickness_cells"] < 1)
        # (the tool's list of removed components is a heuristic; the count is the repair's own)
        print("| %s | %d | %d | %d | %d | %d | %d | %d | %d | %d | %d |" % (
            fam, len(a), len(a) - len(o), sum(r["plain_clean"] for r in o), sum(r["rounding_clean"] for r in o),
            sum(r["clean"] for r in o), sum(r["limit"] for r in o), sum(r["removed_components"] > 0 for r in o),
            sum(r["removed_components"] for r in o), thin, len(rem) - thin))
    listed = sum(len(r.get("removed", [])) for r in ok)
    counted = sum(r["removed_components"] for r in ok)
    print("\nRemoved components listed by the tool: %d; counted by the repair: %d." % (listed, counted))
    thick = [(r["case"], c) for r in ok for c in r.get("removed", []) if c["thickness_cells"] >= 1]
    for case, c in thick:
        print("- one cell or thicker: %s, %d triangles, %.3g cells thick, %.3g cells long" % (case, c["tris"], c["thickness_cells"], c["box_max_cells"]))
    rr = [r for r in ok if r["removed_components"] > 0]
    print("\nThe same repair without the removal of tiny and thin components, on the %d results with a removed component: clean on %d." % (
        len(rr), sum(r.get("no_removal_clean", False) for r in rr)))
    drop = [r for r in ok if family(r["case"]) == "drop"]
    if drop:
        print("\n## Valid small and thin solids (the set drop)\n")
        print("| case | cells per `maxLength` | result triangles | removed components | thickness of the removed, cells | without the removal: clean |")
        print("|---|---:|---:|---:|---|---|")
        for r in drop:
            print("| %s | %.1f | %d | %d | %s | %s |" % (r["case"], r["max_length"] / r["grid"], r["tris"], r["removed_components"],
                  ", ".join("%.3g" % c["thickness_cells"] for c in r.get("removed", [])) or "-",
                  ("yes" if r["no_removal_clean"] else "no") if "no_removal_clean" in r else "-"))
    grid = [r for r in ok if "grid" in r]
    print("\nGrid of the area lemma: fits the drift bound on %d of %d results, representable on %d." % (
        sum(r["grid_fits_drift"] for r in grid), len(grid), sum(r["grid_representable"] for r in grid)))

    print("\n## Cost of the rounding today\n")
    print("| set | results | rounding, share of the boolean: median | largest | summed rounding / summed boolean |")
    print("|---|---:|---:|---:|---|")
    for fam in fams:
        o = [r for r in ok if family(r["case"]) == fam and r["ms_safe"] > 0]
        if not o:
            continue
        shares = [r["ms_rounding"] / r["ms_safe"] for r in o]
        print("| %s | %d | %.0f %% | %.0f %% | %.1f of %.1f s |" % (fam, len(o), 100 * statistics.median(shares), 100 * max(shares),
              sum(r["ms_rounding"] for r in o) / 1e3, sum(r["ms_safe"] for r in o) / 1e3))

    print("\n## The global scheme: a lower bound of its output size\n")
    print("| case | result triangles n | boolean | of it rounding | thin slabs | output triangles, at least | factor | on a grid as fine as the coordinates | factor | n sqrt(n) |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
    for r in ok:
        if family(r["case"]) != "scale" or "out_tris_min" not in r:
            continue
        n = r["in_tris"]
        print("| %s | %d | %.2f s | %.2f s | %d | %d | %.0f | %d | %.0f | %.0f |" % (r["case"], n, r["ms_safe"] / 1e3, r["ms_rounding"] / 1e3,
              r["thin_slabs"], r["out_tris_min"], r["out_tris_min"] / n, r["out_tris_min_fine"], r["out_tris_min_fine"] / n, n ** 1.5))
    print("\n| set | results | factor of the lower bound: median | smallest | largest |")
    print("|---|---:|---:|---:|---:|")
    for fam in fams:
        f = [r["out_tris_min"] / r["in_tris"] for r in ok if family(r["case"]) == fam and r.get("in_tris")]
        if f:
            print("| %s | %d | %.1f | %.1f | %.1f |" % (fam, len(f), statistics.median(f), min(f), max(f)))

    print("\n## A local variant: the patch around what SafeRounding leaves\n")
    left = [r for r in ok if r.get("defect_tris", 0) > 0 and "patch_in_tris" in r]
    print("| set | results with a defect left | defect triangles: median, largest | patch (3 rings): median, largest | share of the result: median, largest | lower bound of the patch after the scheme, factor: median, largest | results whose patch has triangles closer than one cell | such triangles, share of the patch: median, largest |")
    print("|---|---:|---|---|---|---|---:|---|")
    for fam in fams + ["all"]:
        a = [r for r in left if fam == "all" or family(r["case"]) == fam]
        if not a:
            continue
        d = [r["defect_tris"] for r in a]
        pt = [r["patch_in_tris"] for r in a]
        sh = [r["patch_in_tris"] / r["tris"] for r in a]
        fa = [r["patch_out_tris_min"] / r["patch_in_tris"] for r in a]
        cl = [r.get("patch_close_tris", 0) / r["patch_in_tris"] for r in a]
        if fam == "drop":
            continue
        print("| %s | %d | %d, %d | %d, %d | %.0f %%, %.0f %% | %.1f, %.1f | %d | %.0f %%, %.0f %% |" % (fam, len(a), statistics.median(d), max(d),
              statistics.median(pt), max(pt), 100 * statistics.median(sh), 100 * max(sh), statistics.median(fa), max(fa),
              sum(c > 0 for c in cl), 100 * statistics.median(cl), 100 * max(cl)))


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("out")
    r.add_argument("sets", nargs="+")
    r.add_argument("-j", type=int, default=4)
    r.add_argument("--timeout", type=int, default=600)
    t = sub.add_parser("tables")
    t.add_argument("files", nargs="+")
    a = ap.parse_args()
    (run if a.cmd == "run" else tables)(a)


if __name__ == "__main__":
    main()
