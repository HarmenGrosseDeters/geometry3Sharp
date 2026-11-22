#!/usr/bin/env python3
"""The removal of thin and tiny components (#1216): docs/research/ROUNDING_THIN_SOLIDS_2026-11.md (ThinSolids.cs).

  python3 thin.py run <dir> [<set>...] [-j 4]   `thin set <set>` per set, one file <dir>/<set>.jsonl.gz each
  python3 thin.py tables <dir>                  the tables of the note
  python3 thin.py selftest                      the classification on hand-made rows

Sets: issue, sheet, plate, drop, copies, clusters, utm, booleans, cylinder.
"""
import argparse, gzip, json, math, os, subprocess
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(HERE, "bin", "Release", "net10.0", "SnapRoundingExperiment.dll")
SETS = ["issue", "sheet", "plate", "drop", "copies", "clusters", "utm", "booleans", "cylinder"]
# the solids of these sets are defined by the inputs (a sheet, a plate, a cube); the thin components of the others are
# what is left between nearly coincident surfaces
DESIGNED = {"issue", "sheet", "plate", "drop"}
BANDS = [(0, 1, "below 1"), (1, 4, "1 to 4"), (4, 256, "4 to 256"), (256, 2 ** 16, "256 to 2^16"),
         (2 ** 16, 2 ** 32, "2^16 to 2^32"), (2 ** 32, math.inf, "2^32 and more")]
RULES = [("cur", "today"), ("none", "no removal"), ("a_post_4", "(a) below 4 ulps, after the edits"),
         ("a_pre_4", "(a) below 4 ulps"), ("a_pre_256", "(a) below 256 ulps"), ("a_pre_65536", "(a) below 2^16 ulps"),
         ("a_pre_256_notiny", "(a) below 256 ulps, no tiny rule"), ("b", "(b) still defective"),
         ("c_4", "(c) below 4 ulps, or (b)"), ("c_256", "(c) below 256 ulps, or (b)"),
         ("d_4", "(d1) (c) at 4, or volume changed 2x"), ("d_256", "(d1) (c) at 256, or volume changed 2x"),
         ("e_4", "(d2) (c) at 4, or thinner than twice the drift"), ("e_256", "(d2) (c) at 256, or thinner than twice the drift")]


def ulp(x):
    """a unit in the last place of x > 0 (a power of two)"""
    return 2.0 ** (math.frexp(x)[1] - 53)


def thickness0(c):
    return 2 * abs(c["volume0"]) / c["area0"] if c["area0"] > 0 else 0.0


def ulps(c):
    """the thickness of the component before the edits, in units in the last place of its largest coordinate"""
    return thickness0(c) / ulp(c["largest0"])


def band(c):
    u = ulps(c)
    for lo, hi, name in BANDS:
        if lo <= u < hi:
            return name
    return BANDS[-1][2]


def state(c, removed):
    """what a rule leaves of a component: removed, or kept defective (a degenerate triangle, an intersection or a
    contact made by the rounding is left in it), kept deformed (its volume changed by more than a factor 2), kept"""
    if c["i"] in removed:
        return "removed"
    if c["bad"] > 0 or c["pairs"] > 0 or c["false_contacts"] > 0:
        return "defective"
    v0, v1 = abs(c["volume0"]), abs(c["volume1"])
    if v1 > 2 * v0 or 2 * v1 < v0:
        return "deformed"
    return "kept"


def run(args):
    os.makedirs(args.dir, exist_ok=True)

    def one(s):
        out = subprocess.run(["dotnet", TOOL, "thin", "set", s], capture_output=True, check=True).stdout
        # (compressed: the sheet and copies sets are megabytes of lines; mtime 0, so that a rerun gives the same file)
        with open(os.path.join(args.dir, s + ".jsonl.gz"), "wb") as f, gzip.GzipFile(fileobj=f, mode="wb", mtime=0) as z:
            z.write(out)
        return s

    with ThreadPoolExecutor(args.j) as pool:
        for s in pool.map(one, args.sets or SETS):
            print(s, "done")


def load(d):
    rows = {}
    for s in SETS:
        name = os.path.join(d, s + ".jsonl.gz")
        if os.path.exists(name):
            with gzip.open(name, "rt") as f:
                rows[s] = [json.loads(line) for line in f if line.startswith("{")]
    return rows


def removed_of(r, rule):
    return set(r["outcomes"][r["rules"][rule][0]]["removed"])


def sheet_tables(rows):
    print("## The sheet of #1216, scaled: the thinnest sheet kept today\n")
    print("Thickness t = L 2^-k, k = 7, 10, ... 52; per frame and offset the largest k (the thinnest sheet) that is kept"
          " over the four sizes L (smallest and largest of the four), and why the next one goes.\n")
    print("| frame | offset / L | thinnest kept, k | t / diagonal there | sheets removed | of them: by an edit elsewhere in the sheet | by their own side triangles (degenerate area) | untouched, zero volume | exact result empty |")
    print("|---|---:|---|---:|---:|---:|---:|---:|---:|")
    groups = {}
    for r in rows:
        _, k, L, off, frame = r["case"].split(":")
        groups.setdefault((frame, float(off)), []).append((int(k), L, r))
    order = ["axis", "rz", "rza", "issue", "gen", "gena"]
    for frame, off in sorted(groups, key=lambda g: (order.index(g[0]), g[1])):
        g = groups[(frame, off)]
        kept = {}
        removed = edit = walls = zero = empty = 0
        for k, L, r in g:
            if r["raw"]["tris"] == 0:
                empty += 1
                continue
            if r["safe"]["removed_components"] == 0:
                kept[L] = max(kept.get(L, 0), k)
                continue
            removed += 1
            c = max(r["comps"], key=lambda c: c["area0"])
            # the side walls of the sheet are triangles of area about L t / 2: degenerate below 1e-12 size^2
            t = thickness0(c)
            size = r["size"]
            if not c["touched"] and c["bad"] == 0:
                zero += 1
            elif float(L) * t / 2 < 1e-12 * size * size:
                walls += 1
            else:
                edit += 1
        ks = sorted(set(kept.values()))
        kk = "%d" % ks[0] if len(ks) == 1 else "%d to %d" % (ks[0], ks[-1])
        ratio = 2.0 ** -ks[0] / math.sqrt(2) if ks else float("nan")
        print("| %s | %g | %s | %.1e | %d | %d | %d | %d | %d |" % (frame, off, kk, ratio, removed, edit, walls, zero, empty))
    print()
    print("### The size of the edit that condemns a sheet\n")
    print("Sheets removed today whose own triangles are not degenerate (the column \"by an edit elsewhere\"): the largest"
          " drift of the repair's edits against the thickness of the sheet.\n")
    print("| frame | sheets | drift / thickness: smallest | median | largest | thickness / ulp: smallest | largest |")
    print("|---|---:|---:|---:|---:|---:|---:|")
    per = {}
    for r in rows:
        _, k, L, off, frame = r["case"].split(":")
        if r["raw"]["tris"] == 0 or r["safe"]["removed_components"] == 0:
            continue
        c = max(r["comps"], key=lambda c: c["area0"])
        t = thickness0(c)
        if (not c["touched"] and c["bad"] == 0) or float(L) * t / 2 < 1e-12 * r["size"] ** 2:
            continue
        per.setdefault(frame, []).append((r["safe"]["max_drift"] / t, ulps(c)))
    for frame in order:
        if frame in per:
            d = sorted(x[0] for x in per[frame])
            u = sorted(x[1] for x in per[frame])
            print("| %s | %d | %.1e | %.1e | %.1e | %.1e | %.1e |" % (frame, len(d), d[0], d[len(d) // 2], d[-1], u[0], u[-1]))
    print()


def today_table(rows):
    print("## Today, per set\n")
    print("| set | cases | exact result not empty | results with a removed component | emptied (Success, no triangles) | removed components | thickest removed, of the largest diagonal | thickest removed, ulps | replay equals the boolean |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|")
    for s, rs in rows.items():
        ok = [r for r in rs if r.get("ok") and r["raw"]["tris"] > 0]
        rem = [r for r in ok if r["safe"]["removed_components"] > 0]
        comps = [(c, r) for r in ok for c in r["comps"] if c["i"] in removed_of(r, "cur")]
        thick = max((thickness0(c) / r["size"] for c, r in comps), default=0)
        thick_u = max((ulps(c) for c, r in comps), default=0)
        print("| %s | %d | %d | %d | %d | %d | %.2g | %.2g | %d of %d |" % (
            s, len(rs), len(ok), len(rem), sum(1 for r in rem if r["safe"]["tris"] == 0),
            sum(r["safe"]["removed_components"] for r in ok), thick, thick_u, sum(1 for r in ok if r["replica"]), len(ok)))
    print()


def population(rows, designed):
    for s, rs in rows.items():
        if (s in DESIGNED) != designed:
            continue
        for r in rs:
            if r.get("ok") and r["raw"]["tris"] > 0:
                for c in r["comps"]:
                    yield s, r, c


def band_tables(rows):
    for designed, title in ((True, "Solids the inputs define (sets issue, sheet, plate, drop)"),
                            (False, "What is left between nearly coincident surfaces (sets copies, clusters, utm, booleans, cylinder)")):
        pop = list(population(rows, designed))
        print("## %s: %d components that are thin, tiny or of zero volume by today's tests\n" % (title, len(pop)))
        print("Per band of thickness (2 volume / area of the exact result, rounded, in units in the last place of the"
              " component's largest coordinate): removed / kept defective / kept deformed / kept.\n")
        names = [b[2] for b in BANDS]
        print("| rule | " + " | ".join(names) + " |")
        print("|---|" + "---|" * len(names))
        print("| components | " + " | ".join(str(sum(1 for _, _, c in pop if band(c) == n)) for n in names) + " |")
        for rule, label in RULES:
            cells = []
            for n in names:
                count = {"removed": 0, "defective": 0, "deformed": 0, "kept": 0}
                for _, r, c in pop:
                    if band(c) == n:
                        count[state(c, removed_of(r, rule))] += 1
                cells.append("%d / %d / %d / %d" % (count["removed"], count["defective"], count["deformed"], count["kept"]))
            print("| %s | " % label + " | ".join(cells) + " |")
        print()


def score_table(rows):
    print("## The rules, scored\n")
    print("A *solid lost*: a component of the sets issue, sheet, plate or drop of 4 ulps or more that is removed. A *solid"
          " deformed*: one kept with its volume changed by more than a factor 2 (*kept* without more: within a factor 2 of the"
          " exact result's, no defect left). A *sliver kept*: a component of the other"
          " sets below 4 ulps that stays. *Between* (4 ulps and more in those sets): the decision. A result is *not"
          " clean* if `NotCleanReasons` is not empty (an empty result counts as clean, as today).\n")
    des = [(r, c) for _, r, c in population(rows, True) if ulps(c) >= 4]
    byp = [(r, c) for _, r, c in population(rows, False)]
    sliv = [(r, c) for r, c in byp if ulps(c) < 4]
    grey = [(r, c) for r, c in byp if ulps(c) >= 4]
    results_d = [r for s, rs in rows.items() if s in DESIGNED for r in rs if r.get("ok") and r["raw"]["tris"] > 0]
    results_b = [r for s, rs in rows.items() if s not in DESIGNED for r in rs if r.get("ok") and r["raw"]["tris"] > 0]
    print("| rule | solids lost (of %d) | solids deformed | solids kept defective | slivers kept (of %d) | between: removed (of %d) | between: kept defective | between: kept deformed | between: kept | results not clean, solids' sets (of %d) | results not clean, other sets (of %d) | results emptied | results that differ from today |" % (
        len(des), len(sliv), len(grey), len(results_d), len(results_b)))
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
    for rule, label in RULES:
        def n(pop, what):
            return sum(1 for r, c in pop if state(c, removed_of(r, rule)) in what)

        def dirty(rs):
            return sum(1 for r in rs if r["outcomes"][r["rules"][rule][0]]["reasons"] not in ("", "empty") or r["rules"][rule][1] > 0)
        emptied = sum(1 for r in results_d + results_b if r["outcomes"][r["rules"][rule][0]]["tris"] == 0)
        differ = sum(1 for r in results_d + results_b if removed_of(r, rule) != removed_of(r, "cur"))
        print("| %s | %d | %d | %d | %d | %d | %d | %d | %d | %d | %d | %d | %d |" % (
            label, n(des, ("removed",)), n(des, ("deformed",)), n(des, ("defective",)), n(sliv, ("defective", "deformed", "kept")),
            n(grey, ("removed",)), n(grey, ("defective",)), n(grey, ("deformed",)), n(grey, ("kept",)), dirty(results_d), dirty(results_b), emptied, differ))
    print()


def cause_table(rows):
    print("## The cause: thin by the bound, and edited anywhere\n")
    print("Closed components thinner than the bound (1e-5 of the largest component's diagonal, or 256 ulps) that are not"
          " tiny and not of numerically zero volume, all sets: removed today, by whether the repair edited a vertex of the"
          " component or left a degenerate triangle in it.\n")
    print("| sets | edited: components | of them removed | not edited: components | of them removed |")
    print("|---|---:|---:|---:|---:|")
    for designed, label in ((True, "issue, sheet, plate, drop"), (False, "copies, clusters, utm, booleans, cylinder")):
        n = {True: [0, 0], False: [0, 0]}
        for _, r, c in population(rows, designed):
            if c["thin"] and not c["tiny"] and not c["zero_volume"]:
                e = c["touched"] or c["bad"] > 0
                n[e][0] += 1
                n[e][1] += c["i"] in removed_of(r, "cur")
        print("| %s | %d | %d | %d | %d |" % (label, n[True][0], n[True][1], n[False][0], n[False][1]))
    print()


def changed(c, f):
    v0, v1 = abs(c["volume0"]), abs(c["volume1"])
    return v1 > f * v0 or f * v1 < v0


def sensitivity_table(rows):
    print("## Rule (d1): the factor of the volume change\n")
    print("Rule (c) at 4 ulps, plus: an edited component that is thin or tiny by today's tests goes if the repair changed"
          " its volume by more than the factor (computed here from the components; at the factor 2 it must give the"
          " tool's own rule).\n")
    des = [(r, c) for _, r, c in population(rows, True) if ulps(c) >= 4]
    grey = [(r, c) for _, r, c in population(rows, False) if ulps(c) >= 4]
    agree = total = 0
    for _, rs in rows.items():
        for r in rs:
            if r.get("ok") and r["raw"]["tris"] > 0:
                mine = removed_of(r, "c_4") | {c["i"] for c in r["comps"] if c["i"] in removed_of(r, "cur") and changed(c, 2)}
                total += 1
                agree += mine == removed_of(r, "d_4")
    print("Check at the factor 2: the same removed components as the tool's rule on %d of %d results.\n" % (agree, total))
    print("| factor | solids lost (of %d) | solids kept | between: removed (of %d) | between: kept |" % (len(des), len(grey)))
    print("|---:|---:|---:|---:|---:|")
    for f in (1.000001, 1.001, 1.01, 1.1, 2, 10, 1000):
        def gone(pop):
            return sum(1 for r, c in pop if c["i"] in removed_of(r, "c_4") or (c["i"] in removed_of(r, "cur") and changed(c, f)))
        print("| %g | %d | %d | %d | %d |" % (f, gone(des), len(des) - gone(des), gone(grey), len(grey) - gone(grey)))
    print()


def grid_table(rows):
    print("## Against a grid rounding: the thickness of the lost solids in cells\n")
    print("The grid a guaranteed scheme would use (SNAP_ROUNDING_3D_GUARANTEE_2026-11.md section 3): the smallest power"
          " of two g with g^2 / 2 >= 1e-12 times the squared diagonal of the result's box. Such a scheme merges sheets"
          " closer than a cell and keeps a slab of about two cells or more (that note, section 4.2).\n")
    print("| solids of 4 ulps and more | all | 2 cells and thicker | 1 to 2 cells | below 1 cell |")
    print("|---|---:|---:|---:|---:|")
    des = [(r, c) for _, r, c in population(rows, True) if ulps(c) >= 4]

    def cells(r, c):
        return thickness0(c) / 2.0 ** math.ceil(math.log2(math.sqrt(2e-12) * r["box"]))
    for label, pop in (("thin, tiny or of zero volume by today's tests", des),
                       ("removed today", [(r, c) for r, c in des if c["i"] in removed_of(r, "cur")]),
                       ("removed today, kept by rule (c) at 4 ulps (volume within a factor 2)", [(r, c) for r, c in des if c["i"] in removed_of(r, "cur") and state(c, removed_of(r, "c_4")) == "kept"])):
        k = [cells(r, c) for r, c in pop]
        print("| %s | %d | %d | %d | %d |" % (label, len(k), sum(1 for x in k if x >= 2), sum(1 for x in k if 1 <= x < 2), sum(1 for x in k if x < 1)))
    print()


def copies_table(rows):
    print("## Nearly coincident copies: A minus A', by the distance of the copy\n")
    print("Per distance d = 2^e ulps of the largest coordinate (all shapes, modes, sizes and places): the thin components"
          " of the exact result, and what today's rule and three candidates do with them (removed / kept defective / kept"
          " deformed / kept).\n")
    print("| e | cases | exact result empty | thin components | thickness, ulps: median | largest | today | (b) | (c) at 4 ulps | (d1) at 4 ulps | results not clean: today | (b) | (c) at 4 | (d1) at 4 |")
    print("|---:|---:|---:|---:|---:|---:|---|---|---|---|---:|---:|---:|---:|")
    by = {}
    for r in rows:
        by.setdefault(int(r["case"].split(":")[3]), []).append(r)
    for e in sorted(by):
        rs = [r for r in by[e] if r.get("ok")]
        full = [r for r in rs if r["raw"]["tris"] > 0]
        comps = [(r, c) for r in full for c in r["comps"]]
        u = sorted(ulps(c) for _, c in comps)

        def cell(rule):
            count = {"removed": 0, "defective": 0, "deformed": 0, "kept": 0}
            for r, c in comps:
                count[state(c, removed_of(r, rule))] += 1
            return "%d / %d / %d / %d" % (count["removed"], count["defective"], count["deformed"], count["kept"])

        def dirty(rule):
            return sum(1 for r in full if r["outcomes"][r["rules"][rule][0]]["reasons"] not in ("", "empty") or r["rules"][rule][1] > 0)
        print("| %d | %d | %d | %d | %.2g | %.2g | %s | %s | %s | %s | %d | %d | %d | %d |" % (
            e, len(rs), len(rs) - len(full), len(comps), u[len(u) // 2] if u else 0, u[-1] if u else 0,
            cell("cur"), cell("b"), cell("c_4"), cell("d_4"), dirty("cur"), dirty("b"), dirty("c_4"), dirty("d_4")))
    print()


def tables(args):
    rows = load(args.dir)
    today_table(rows)
    if "sheet" in rows:
        sheet_tables(rows["sheet"])
    cause_table(rows)
    band_tables(rows)
    score_table(rows)
    sensitivity_table(rows)
    if "copies" in rows:
        copies_table(rows["copies"])
    grid_table(rows)


def selftest():
    assert ulp(1.0) == 2.0 ** -52 and ulp(1.5) == 2.0 ** -52 and ulp(2.0) == 2.0 ** -51 and ulp(0.75) == 2.0 ** -53
    c = {"i": 3, "area0": 2.0, "volume0": 2.0 ** -50, "largest0": 1.0, "volume1": 2.0 ** -50, "bad": 0, "pairs": 0, "false_contacts": 0}
    assert thickness0(c) == 2.0 ** -50 and ulps(c) == 4 and band(c) == "4 to 256"
    assert state(c, {3}) == "removed" and state(c, set()) == "kept"
    assert state(dict(c, bad=1), set()) == "defective" and state(dict(c, pairs=1), {3}) == "removed"
    assert state(dict(c, volume1=2.0 ** -48), set()) == "deformed" and state(dict(c, volume1=-2.0 ** -52), set()) == "deformed"
    assert state(dict(c, volume1=2.0 ** -49), set()) == "kept"
    assert band(dict(c, volume0=0.0)) == "below 1" and band(dict(c, volume0=1.0)) == "2^32 and more"
    print("ok")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("run"); p.add_argument("dir"); p.add_argument("sets", nargs="*"); p.add_argument("-j", type=int, default=4)
    p = sub.add_parser("tables"); p.add_argument("dir")
    sub.add_parser("selftest")
    a = ap.parse_args()
    {"run": run, "tables": tables, "selftest": lambda _: selftest()}[a.cmd](a)
