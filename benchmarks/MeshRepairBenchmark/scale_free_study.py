#!/usr/bin/env python3
"""Driver of the scale-free repair study (hard problem 9; docs/design/ROBUST_REPAIR_SCALE_FREE.md). Data branch
runs/scale-free-study only, run by its copy of .github/workflows/alpha-defaults.yml. MeshRobustRepair (BENCH_REPAIR=robust,
BENCH_FIDELITY=0: certified Hausdorff and IoU, no fm_*) over the first and the holdout selection of fetch_data.py
(cases/scale_free_list800.txt: id, selection, category), in these configurations:
  R  library defaults (the reference)
  P  R, every part of the box rule (closed components whose boxes meet; an open component makes one part) repaired alone
  Q  R, every part of the padded box rule over all components (open ones too) repaired alone
  S  R, the relative tolerances of the repair stage x 0.01      L  the same x 100
  G  R, solidify resolution 256                                 T  R on the input translated by (5e5, 5e6, 0) ("geo")
Shortcuts (the row of R is copied, marked "copied": true, because the configuration cannot differ by construction):
P / Q when R's input has one part by that rule; G when R did not reach the solidify.

  scale_free_study.py select  <list> <shard> <shards>          ids of one shard (round robin)
  scale_free_study.py run     <ids-file> <out-dir> [--configs RPQSLGT] [--timeout 600] [--deadline-min 0]   resumable
  scale_free_study.py merge   <out-dir> <shard-dir>...
  scale_free_study.py analyze <merged-dir> <list>               the summary (markdown) to stdout

Mesh files are read from data/ids/meshes (fetch_data.py --ids). Nothing here fetches from the network.
"""
import glob
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import criteria  # noqa: E402

CONFIG_ENV = {"R": {}, "P": {"BENCH_PARTS": "box"}, "Q": {"BENCH_PARTS": "all"}, "S": {"BENCH_TOL_SCALE": "0.01"},
              "L": {"BENCH_TOL_SCALE": "100"}, "G": {"BENCH_SOLID_RES": "256"}, "T": {}, "N": {"BENCH_NO_F1": "1"}}
CONFIG_ARGS = {"T": ["std", "geo"]}
UNSET = ("BENCH_PARTS", "BENCH_TOL_SCALE", "BENCH_SOLID_RES", "BENCH_FIDELITY", "BENCH_PROBE", "BENCH_NO_F1")


def read_list(path):
    return [line.rstrip("\n").split("\t") for line in open(path) if line.strip()]


def done_rows(path):
    rows = {}
    if os.path.exists(path):
        for line in open(path):
            try:
                r = json.loads(line)
                rows[r["file"]] = r
            except (ValueError, KeyError):
                pass  # a torn last line: the pair runs again
    return rows


def run_one(mesh, cfg, timeout):
    env = {k: v for k, v in os.environ.items() if k not in UNSET}
    env.update(CONFIG_ENV[cfg])
    env.update(BENCH_REPAIR="robust", BENCH_FIDELITY="0", TIMEOUT=str(timeout))
    t0 = time.monotonic()
    out = subprocess.run([os.path.join(HERE, "run.sh"), mesh] + CONFIG_ARGS.get(cfg, []), env=env,
                         capture_output=True, text=True).stdout
    wall = time.monotonic() - t0
    lines = [x for x in out.splitlines() if x.strip()]
    row = {"file": os.path.basename(mesh), "status": "timeout_or_crash"}
    if lines:
        try:
            r = json.loads(lines[-1])
            if r["file"]:
                row = r
        except (ValueError, KeyError, TypeError):
            pass
    return row, wall


def copy_reason(cfg, ref):
    """why cfg equals R by construction for this mesh (None: it has to run)"""
    if ref is None or ref.get("status") == "timeout_or_crash":
        return None
    if cfg == "P" and ref.get("sc_parts_box", 2) is not None and 0 <= ref.get("sc_parts_box", 2) <= 1:
        return "one part (box rule)"
    if cfg == "Q" and ref.get("sc_parts_all", 2) is not None and 0 <= ref.get("sc_parts_all", 2) <= 1:
        return "one part (padded rule)"
    if cfg == "G" and "solidify" not in ref.get("robust_phases", {"solidify": 0}):
        return "no solidify in R"
    return None


def cmd_run(args):
    ids_file, out = args[0], args[1]
    opt = dict(zip(args[2::2], args[3::2]))
    if set(opt) - {"--configs", "--timeout", "--deadline-min"} or len(args) % 2:
        sys.exit("unknown or incomplete options")
    cfgs, timeout = opt.get("--configs", "RPQSLGT"), int(opt.get("--timeout", "600"))
    if cfgs[0] != "R":
        sys.exit("R must come first (the shortcuts read its row)")
    deadline = float(opt.get("--deadline-min", "0")) * 60
    os.makedirs(os.path.join(out, "raw"), exist_ok=True)
    start = time.monotonic()
    for mid in [x.strip() for x in open(ids_file) if x.strip()]:
        if deadline and time.monotonic() - start > deadline:
            print(f"deadline reached before {mid}", flush=True)
            break
        found = sorted(glob.glob(os.path.join(HERE, "data", "ids", "meshes", mid + ".*")))
        if not found:
            print(f"{mid}: no mesh file", flush=True)
            continue
        mesh = found[0]
        f = os.path.basename(mesh)
        ref = done_rows(os.path.join(out, "raw", "R.jsonl")).get(f)
        for cfg in cfgs:
            raw = os.path.join(out, "raw", cfg + ".jsonl")
            if f in done_rows(raw):
                continue
            why = copy_reason(cfg, ref) if cfg != "R" else None
            if why is not None:
                row, wall = dict(ref, copied=why), 0.0
            else:
                row, wall = run_one(mesh, cfg, timeout)
            row["config"] = cfg
            with open(raw, "a") as w:
                w.write(json.dumps(row) + "\n")
            with open(os.path.join(out, "wall.tsv"), "a") as w:
                w.write(f"{f}\t{cfg}\t{wall:.2f}\n")
            if cfg == "R":
                ref = row
        print(f"{mid} done ({time.monotonic() - start:.0f} s)", flush=True)


def cmd_merge(args):
    out, shards = args[0], args[1:]
    os.makedirs(os.path.join(out, "raw"), exist_ok=True)
    for cfg in CONFIG_ENV:
        rows = {}
        for d in shards:
            for r in done_rows(os.path.join(d, "raw", cfg + ".jsonl")).values():
                rows.setdefault(r["file"], r)
        if rows:
            with open(os.path.join(out, "raw", cfg + ".jsonl"), "w") as w:
                for k in sorted(rows):
                    w.write(json.dumps(rows[k]) + "\n")
    walls = {}
    for d in shards:
        p = os.path.join(d, "wall.tsv")
        if os.path.exists(p):
            for line in open(p):
                parts = line.rstrip("\n").split("\t")
                if len(parts) == 3:
                    walls.setdefault((parts[0], parts[1]), parts[2])
    with open(os.path.join(out, "wall.tsv"), "w") as w:
        for (f, c), s in sorted(walls.items()):
            w.write(f"{f}\t{c}\t{s}\n")


def num(r, k, d=0.0):
    v = r.get(k, d)
    try:
        return float(v)
    except (TypeError, ValueError):
        return d


def path_of(r):
    if r.get("parts_used"):
        return "parts"
    if r.get("status") == "timeout_or_crash":
        return "timeout"
    return ("wrap" if r.get("robust_used_wrap") else "solidify" if r.get("robust_used_solidify")
            else "exact" if r.get("robust_used_exact") else "kept")


def miss_class(r):
    """the first reason a row misses the strict criterion (criteria.repaired)"""
    if r.get("status") != "closed":
        return "status " + str(r.get("status"))
    if not criteria.clean(r):
        only_scale = dict(r, after_degenerate_tris=0)
        return "not clean" + (" (degenerate by the scene only)" if criteria.clean(only_scale) and criteria.get(r, "after_degenerate_tris_own", 1) == 0 else "")
    if criteria.selfint(r) != 0:
        return "self-intersections"
    if criteria.hd(r) >= criteria.HD:
        return "Hausdorff"
    return "IoU"


def part_ok(r):
    """the scale-free part criterion: no closed input component (>= 4 triangles) off by more than 1 % of its own diagonal, either way"""
    return r.get("pf_skipped") or (num(r, "pf_fwd_off_closed") == 0 and num(r, "pf_back_off_closed") == 0)


def repaired_own(r):
    """the strict criterion with the degenerate area relative to each result component's own diagonal (option D's view)"""
    return criteria.repaired(dict(r, after_degenerate_tris=r.get("after_degenerate_tris_own", r.get("after_degenerate_tris"))))


def cmd_analyze(args):
    d, listing = args[0], read_list(args[1])
    sel = {i: (s, c) for i, s, c, *_ in listing}
    cfg_rows = {}
    for cfg in CONFIG_ENV:
        p = os.path.join(d, "raw", cfg + ".jsonl")
        if os.path.exists(p):
            cfg_rows[cfg] = {os.path.splitext(f)[0]: r for f, r in done_rows(p).items()}
    R = cfg_rows.get("R", {})
    out = []
    pr = out.append
    pr(f"# Scale-free repair study: corpus run\n\nMeshes with a row in R: {len(R)} of {len(listing)}.\n")
    # the reference: misses and their classes, per selection
    pr("## R (library defaults)\n")
    pr("| | first | holdout | all |\n|---|---|---|---|")
    def count(pred, rows=R):
        c = {"first": 0, "holdout": 0}
        for k, r in rows.items():
            if k in sel and pred(r):
                c[sel[k][0]] += 1
        return c["first"], c["holdout"], c["first"] + c["holdout"]
    pr("| rows | %d | %d | %d |" % count(lambda r: True))
    pr("| repaired (strict) | %d | %d | %d |" % count(criteria.repaired))
    pr("| misses | %d | %d | %d |" % count(lambda r: not criteria.repaired(r)))
    classes = sorted({miss_class(r) for r in R.values() if not criteria.repaired(r)})
    for c in classes:
        pr(f"| miss: {c} | %d | %d | %d |" % count(lambda r, c=c: not criteria.repaired(r) and miss_class(r) == c))
    pr("| repaired, but a closed part off by > 1 %% of its own diagonal | %d | %d | %d |" % count(lambda r: criteria.repaired(r) and not part_ok(r)))
    pr("| Apply() true, a closed part (>= 4 triangles) lost (> 25 %% of its diagonal) | %d | %d | %d |" % count(lambda r: r.get("apply_returned") and num(r, "pf_lost_closed") > 0))
    pr("| input: >= 2 components | %d | %d | %d |" % count(lambda r: num(r, "sc_components") >= 2))
    pr("| input: >= 2 parts, box rule (B stage 1) | %d | %d | %d |" % count(lambda r: num(r, "sc_parts_box") >= 2))
    pr("| input: >= 2 parts, padded rule over all components | %d | %d | %d |" % count(lambda r: num(r, "sc_parts_all") >= 2))
    pr("| input: far-part switch (a triangle below 1e-12 diag^2, >= 2 components) | %d | %d | %d |" % count(lambda r: bool(r.get("sc_disparity_switch"))))
    pr("| input: a triangle degenerate by the scene, not by its component | %d | %d | %d |" % count(lambda r: num(r, "sc_degenerate_by_scene_only") > 0))
    pr("| far-part mode used (FarPartsHandled) | %d | %d | %d |" % count(lambda r: bool(r.get("robust_far_parts"))))
    pr("| B stage 0 passed triangles through | %d | %d | %d |" % count(lambda r: num(r, "robust_passed_through_tris") > 0))
    misses_multi = count(lambda r: not criteria.repaired(r) and num(r, "sc_parts_box") >= 2)
    pr("| misses with >= 2 parts (box rule) | %d | %d | %d |" % misses_multi)
    pr("| misses with >= 2 parts (padded rule) | %d | %d | %d |" % count(lambda r: not criteria.repaired(r) and num(r, "sc_parts_all") >= 2))
    pr("")
    # each oracle against R
    pr("## Oracles against R (paired, meshes with a row in both)\n")
    pr("| config | rows (run / copied) | repaired | gained | lost | net | repaired, own-component degeneracy (D) | part criterion ok | part ok gained / lost | timeouts | wall sum (s) |")
    pr("|---|---|---|---|---|---|---|---|---|---|---|")
    walls = {}
    wp = os.path.join(d, "wall.tsv")
    if os.path.exists(wp):
        for line in open(wp):
            f, c, s = line.rstrip("\n").split("\t")
            walls[(os.path.splitext(f)[0], c)] = float(s)
    gains = {}
    for cfg, rows in cfg_rows.items():
        keys = [k for k in rows if k in R]
        g = [k for k in keys if criteria.repaired(rows[k]) and not criteria.repaired(R[k])]
        lo = [k for k in keys if not criteria.repaired(rows[k]) and criteria.repaired(R[k])]
        pg = [k for k in keys if part_ok(rows[k]) and not part_ok(R[k])]
        pl = [k for k in keys if not part_ok(rows[k]) and part_ok(R[k])]
        gains[cfg] = (g, lo, pg, pl)
        copied = sum(1 for k in keys if rows[k].get("copied"))
        rep = sum(1 for k in keys if criteria.repaired(rows[k]))
        pok = sum(1 for k in keys if part_ok(rows[k]))
        rown = sum(1 for k in keys if repaired_own(rows[k]))
        to = sum(1 for k in keys if rows[k].get("status") == "timeout_or_crash")
        ws = sum(v for (k, c), v in walls.items() if c == cfg)
        pr(f"| {cfg} | {len(keys) - copied} / {copied} | {rep} | {len(g)} | {len(lo)} | {len(g) - len(lo)} | {rown} | {pok} | {len(pg)} / {len(pl)} | {to} | {ws:.0f} |")
    pr("")
    for cfg, (g, lo, pg, pl) in gains.items():
        if cfg == "R" or not (g or lo or pg or pl):
            continue
        pr(f"### {cfg}\n")
        for name, ks in (("gained", g), ("lost", lo), ("part criterion gained", pg), ("part criterion lost", pl)):
            if ks:
                items = []
                for k in sorted(ks):
                    a, b = R[k], cfg_rows[cfg][k]
                    items.append(f"{k} ({sel.get(k, ('?',))[0]}; {path_of(a)} -> {path_of(b)}; {miss_class(a) if not criteria.repaired(a) else 'ok'} -> "
                                 f"{miss_class(b) if not criteria.repaired(b) else 'ok'}; parts {a.get('sc_parts_box')}/{a.get('sc_parts_all')})")
                pr(f"- {name} ({len(ks)}): " + ", ".join(items))
        pr("")
    # union of what any scale oracle wins (the upper bound)
    scale_cfgs = [c for c in ("P", "Q", "S", "L", "T") if c in gains]
    union = set()
    for c in scale_cfgs:
        union |= set(gains[c][0])
    pr(f"Misses of R repaired by at least one of {''.join(scale_cfgs)} (strict criterion): {len(union)}; "
       f"by G (resolution, not scale): {len(gains['G'][0]) if 'G' in gains else 'n/a'}.\n")
    # outcome flips under translation (T): the path or the result hash
    if "T" in cfg_rows:
        T = cfg_rows["T"]
        keys = [k for k in T if k in R]
        flips = [k for k in keys if criteria.repaired(T[k]) != criteria.repaired(R[k])]
        pathc = [k for k in keys if path_of(T[k]) != path_of(R[k])]
        pr(f"T: {len(keys)} paired; the strict outcome differs on {len(flips)}, the path on {len(pathc)}"
           + (": " + ", ".join(f"{k} ({path_of(R[k])} -> {path_of(T[k])})" for k in sorted(pathc)[:40]) if pathc else "") + ".\n")
    print("\n".join(out))


def cmd_f1(args):
    """the F1 A/B: N (without the final lost-part check) against R (with it), paired"""
    d = args[0]
    R = done_rows(os.path.join(d, "raw", "R.jsonl"))
    N = done_rows(os.path.join(d, "raw", "N.jsonl"))
    keys = sorted(k for k in R if k in N)
    flips = [k for k in keys if N[k].get("apply_returned") and not R[k].get("apply_returned")]
    back = [k for k in keys if R[k].get("apply_returned") and not N[k].get("apply_returned")]
    hash_diff = [k for k in keys if R[k].get("after_hash") != N[k].get("after_hash")]
    strict = [k for k in keys if criteria.repaired(R[k]) != criteria.repaired(N[k])]
    to = [k for k in keys if "timeout" in str(R[k].get("status")) or "timeout" in str(N[k].get("status"))]
    walls = {}
    p = os.path.join(d, "wall.tsv")
    if os.path.exists(p):
        for line in open(p):
            f, c, s = line.rstrip("\n").split("\t")
            walls[(f, c)] = float(s)
    wr = sum(walls.get((k, "R"), 0) for k in keys)
    wn = sum(walls.get((k, "N"), 0) for k in keys)
    out = ["# F1 A/B (R: with the final lost-part check, N: without)", "",
           f"paired meshes: {len(keys)}", f"Apply() true -> false (N true, R false): {len(flips)}" + (": " + ", ".join(f"{k} ({R[k].get('robust_result_reason', '')[:40]})" for k in flips) if flips else ""),
           f"Apply() false -> true: {len(back)}" + (": " + ", ".join(back) if back else ""),
           f"result hash differs: {len(hash_diff)}" + (": " + ", ".join(hash_diff[:40]) if hash_diff else ""),
           f"strict criterion differs: {len(strict)}" + (": " + ", ".join(strict) if strict else ""),
           f"timeouts (either): {len(to)}" + (": " + ", ".join(to) if to else ""),
           f"wall sum R / N (s): {wr:.0f} / {wn:.0f} ({(wr / wn - 1) * 100 if wn else 0:+.1f} %)"]
    print("\n".join(out))


def main(argv):
    if len(argv) < 2:
        sys.exit(__doc__)
    cmd, args = argv[1], argv[2:]
    if cmd == "select":
        ids = [r[0] for r in read_list(args[0])]
        print("\n".join(ids[int(args[1])::int(args[2])]))
    elif cmd == "run":
        cmd_run(args)
    elif cmd == "merge":
        cmd_merge(args)
    elif cmd == "analyze":
        cmd_analyze(args)
    elif cmd == "f1":
        cmd_f1(args)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv)
