#!/usr/bin/env python3
"""Summary of the tet-mesher comparison (run_shard.sh CSVs, columns of TetFiles.Header without a header line).
Usage: summarize.py <dir with ours.csv, ft4.csv, ft1.csv ...> > SUMMARY.md"""
import csv, os, statistics, sys

COLS = ("name,time_s,threads,tets,vertices,neg_tets,zero_tets,inverted,valid,boundary_tris,boundary_self_intersecting,"
        "re_worst,re_p5,re_median,re_gt2_share,mindih_worst,mindih_p1,mindih_p5,mindih_median,dih_lt10_share,dih_ge10_all,"
        "dev_out_max_rel,dev_out_mean_rel,dev_in_max_rel,dev_in_mean_rel").split(",")


def load(path):
    rows = {}
    with open(path) as f:
        for r in csv.reader(f):
            if not r or r[0] in ("name", "FINISHED"):
                continue
            d = dict(zip(COLS, r))
            if len(r) < len(COLS):
                d["status"] = r[3] if len(r) > 3 else "?"
            else:
                d["status"] = "ok"
            rows[r[0]] = d
    return rows


def num(d, k):
    try:
        return float(d.get(k, ""))
    except ValueError:
        return None


def med(xs):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else float("nan")


def g(x, p=3):
    return "-" if x is None or x != x else f"{x:.{p}g}"


def main():
    d = sys.argv[1]
    variants = [v for v in ("ours", "ft4", "ft1") if os.path.exists(os.path.join(d, v + ".csv"))]
    data = {v: load(os.path.join(d, v + ".csv")) for v in variants}
    names = sorted(set().union(*[set(x) for x in data.values()]))
    print("| variant | meshes | output | valid (exact) | inverted tets (sum) | time median / max s | tets median | "
          "min dihedral worst: median / best mesh | p01 dihedral median | meshes with every tet >= 10 deg | "
          "share of tets < 10 deg (median) | dev out max (median, rel. diag) | dev in max (median) |")
    print("|---|" + "---:|" * 12)
    for v in variants:
        rs = list(data[v].values())
        ok = [r for r in rs if r["status"] == "ok"]
        valid = sum(1 for r in ok if r["valid"] == "yes")
        times = [num(r, "time_s") for r in rs if num(r, "time_s") is not None]
        md = [num(r, "mindih_worst") for r in ok]
        thr = sorted(set(r.get("threads", "") for r in rs if r.get("threads")))
        print(f"| {v} ({'/'.join(thr)} thr) | {len(rs)} | {len(ok)} | {valid} | {int(sum(num(r, 'inverted') or 0 for r in ok))} | "
              f"{g(med(times))} / {g(max(times) if times else None)} | {g(med(num(r, 'tets') for r in ok), 4)} | "
              f"{g(med(md))} / {g(max([x for x in md if x is not None], default=None))} | {g(med(num(r, 'mindih_p1') for r in ok))} | "
              f"{sum(1 for r in ok if r['dih_ge10_all'] == 'yes')} | {g(med(num(r, 'dih_lt10_share') for r in ok))} | "
              f"{g(med(num(r, 'dev_out_max_rel') for r in ok))} | {g(med(num(r, 'dev_in_max_rel') for r in ok))} |")
    fails = [(v, n, r["status"] if r["status"] != "ok" else r["valid"]) for v in variants for n, r in data[v].items()
             if r["status"] != "ok" or r["valid"] != "yes"]
    if fails:
        print("\nNot valid or no output:\n")
        for v, n, s in fails:
            print(f"- {v} {n}: {s}")
    print("\nPer mesh (time s / tets / worst min dihedral deg / p01 deg / dev out max rel):\n")
    print("| mesh | " + " | ".join(variants) + " |")
    print("|---|" + "---|" * len(variants))
    for n in names:
        cells = []
        for v in variants:
            r = data[v].get(n)
            if r is None:
                cells.append("")
            elif r["status"] != "ok":
                cells.append(r["status"])
            else:
                cells.append(f"{g(num(r, 'time_s'))} / {int(num(r, 'tets'))} / {g(num(r, 'mindih_worst'))} / "
                             f"{g(num(r, 'mindih_p1'))} / {g(num(r, 'dev_out_max_rel'))}" + ("" if r["valid"] == "yes" else " INVALID"))
        print(f"| {n} | " + " | ".join(cells) + " |")


if __name__ == "__main__":
    main()
