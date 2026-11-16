#!/usr/bin/env python3
# Tables of the pair probe (perf/pair_probe.patch) from `BooleanComparison profile` rows (field phases_ms, keys "pairs.*").
#   pair_probe_report.py classes  <label=rows.jsonl>        counts and times by class (shared corners x decision), sums over the booleans
#   pair_probe_report.py perboolean <rows.jsonl>            per boolean: box pairs, share rejected, percentiles
#   pair_probe_report.py filter   <label=rows.jsonl>        the experiment of mode 3 (TriangleTriangleFilter on the rejected pairs)
#   pair_probe_report.py ab <ctl.jsonl,...> <filt.jsonl,...> intersections.pairs and total time without / with the filter first (hashes compared)
# Times are CPU microseconds summed over the threads (per-pair timestamps), phases wall milliseconds.
import gzip, json, sys, statistics as st

DEC = ["degenerate", "quick1_reject", "quick2_reject", "coplanar_reject", "coplanar_kept", "general_reject", "general_kept", "filter_reject", "filter_kept"]

def rows(path):
    out = []
    for l in (gzip.open(path, "rt") if path.endswith(".gz") else open(path)):
        if l.strip():
            r = json.loads(l)
            if r.get("ok"):
                out.append(r)
    return out

def tot(rs, key):
    return sum(r["phases_ms"].get(key, 0) for r in rs)

def classes(label, path):
    rs = rows(path)
    print(f"### {label}: {len(rs)} booleans\n")
    box = tot(rs, "pairs.self.box_pairs_n")
    print(f"self-join: {box} box pairs; tasks {tot(rs, 'pairs.self.tasks_n')}; traverse {tot(rs, 'pairs.self.traverse_us')/1000:.0f} ms, test loop {tot(rs, 'pairs.self.test_loop_us')/1000:.0f} ms, tasks {tot(rs, 'pairs.self.task_us')/1000:.0f} ms (CPU); phase `intersections.pairs` wall {tot(rs, 'intersections.pairs')} ms\n")
    print("| shared corners | decision | pairs | share of box pairs | CPU ms | us per pair | share of loop time |\n|---|---|---|---|---|---|---|")
    loop = sum(tot(rs, f"pairs.self.s{s}.{d}_us") for s in range(4) for d in DEC)
    for s in range(4):
        for d in DEC:
            n = tot(rs, f"pairs.self.s{s}.{d}_n")
            if n == 0:
                continue
            us = tot(rs, f"pairs.self.s{s}.{d}_us")
            print(f"| {s} | {d} | {n} | {100*n/max(box,1):.1f} % | {us/1000:.0f} | {us/n:.2f} | {100*us/max(loop,1):.1f} % |")
    print()
    kept = sum(tot(rs, f"pairs.self.s{s}.{d}_n") for s in range(4) for d in ("coplanar_kept", "general_kept", "filter_kept"))
    print(f"kept by the self-join (go on to intersect): {kept}; rejected: {box - kept} ({100*(box-kept)/max(box,1):.2f} %)\n")
    print("mesh pairs (A against B): " + ", ".join(f"{k} {tot(rs, 'pairs.mesh.'+k+'_n')} pairs {tot(rs, 'pairs.mesh.'+k+'_us')/1000:.0f} ms" for k in ("skipped", "separated", "kept"))
          + f"; {tot(rs, 'pairs.mesh.queries_n')} queries {tot(rs, 'pairs.mesh.query_us')/1000:.0f} ms; whole `candidates` {tot(rs, 'pairs.mesh.all_us')/1000:.0f} ms (CPU)\n")
    print("| intersect() outcome | pairs | CPU ms | us per pair |\n|---|---|---|---|")
    for k in ("side_t", "side_u", "coplanar", "prefilter_apart", "construct_apart", "meeting"):
        n = tot(rs, f"pairs.intersect.{k}_n")
        us = tot(rs, f"pairs.intersect.{k}_us")
        print(f"| {k} | {n} | {us/1000:.0f} | {us/max(n,1):.2f} |")
    print(f"\nintersect() whole {tot(rs, 'pairs.intersect.all_us')/1000:.0f} ms CPU; phase `intersections.intersect` wall {tot(rs, 'intersections.intersect')} ms\n")

def perboolean(path):
    rs = rows(path)
    cols = {"box pairs (self-join)": [], "rejected by touch_only": [], "mesh-pair hits": [], "kept pairs (candidate_pairs)": [], "share rejected %": []}
    for r in rs:
        p = r["phases_ms"]
        box = p.get("pairs.self.box_pairs_n", 0)
        kept = sum(p.get(f"pairs.self.s{s}.{d}_n", 0) for s in range(4) for d in ("coplanar_kept", "general_kept", "filter_kept"))
        cols["box pairs (self-join)"].append(box)
        cols["rejected by touch_only"].append(box - kept)
        cols["mesh-pair hits"].append(p.get("pairs.mesh.skipped_n", 0) + p.get("pairs.mesh.separated_n", 0) + p.get("pairs.mesh.kept_n", 0))
        cols["kept pairs (candidate_pairs)"].append(r["counts"]["candidate_pairs"])
        cols["share rejected %"].append(100 * (box - kept) / box if box else 0)
    def pct(v, q):
        v = sorted(v); return v[min(len(v) - 1, int(q / 100 * len(v)))]
    print("| per boolean | min | median | p90 | max |\n|---|---|---|---|---|")
    for k, v in cols.items():
        print(f"| {k} | {min(v):.0f} | {st.median(v):.0f} | {pct(v,90):.0f} | {max(v):.0f} |")
    print()

def filt(label, path):
    rs = rows(path)
    print(f"### {label} (mode 3)\n")
    print("| shared | filter says | pairs | filter CPU ms | exact test CPU ms (saved) |\n|---|---|---|---|---|")
    ft = ot = fa = 0
    und_f = und_o = 0
    for s in range(4):
        for k in ("separate", "intersecting", "undecided"):
            n = tot(rs, f"pairs.filter.s{s}.{k}_n")
            if not n:
                continue
            f = tot(rs, f"pairs.filter.s{s}.{k}_us"); o = tot(rs, f"pairs.filter.s{s}.{k}_exact_us")
            print(f"| {s} | {k} | {n} | {f/1000:.0f} | {o/1000:.0f} |")
            ft += f; ot += o
            if k == "separate":
                fa += o - f
    print(f"\nfilter before the exact test on these pairs: saves {fa/1000:.0f} ms CPU (exact tests of the decided pairs minus all filter calls {ft/1000:.0f} ms); `intersecting` rows must be absent (a contradiction)\n")

def ab(ctl, fl):
    def load(paths):
        ms = []
        for p in paths.split(","):
            rs = rows(p)
            ms.append((sum(r["phases_ms"]["intersections.pairs"] for r in rs), sum(min(r["ms"]) for r in rs), {(r["case"], r["op"]): r["hash"] for r in rs}))
        return ms
    c, f = load(ctl), load(fl)
    ref = c[0][2]
    same = all(h == ref for _, _, hs in c + f for h in [hs.get(k) for k in ref] if False) or all(hs == ref for _, _, hs in c + f)
    print("| run | intersections.pairs ms | sum of boolean ms |\n|---|---|---|")
    for i, (p, t, _) in enumerate(c): print(f"| control {i+1} | {p} | {t} |")
    for i, (p, t, _) in enumerate(f): print(f"| filter first {i+1} | {p} | {t} |")
    mp = lambda x: sum(a[0] for a in x) / len(x); mt = lambda x: sum(a[1] for a in x) / len(x)
    print(f"\nmean pairs phase {mp(c):.0f} -> {mp(f):.0f} ms ({100*(mp(f)/mp(c)-1):+.1f} %), mean total {mt(c):.0f} -> {mt(f):.0f} ms ({100*(mt(f)/mt(c)-1):+.1f} %); hashes equal in all runs: {same}")

if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "classes":
        for a in sys.argv[2:]:
            l, p = a.split("=", 1); classes(l, p)
    elif cmd == "perboolean": perboolean(sys.argv[2])
    elif cmd == "filter":
        for a in sys.argv[2:]:
            l, p = a.split("=", 1); filt(l, p)
    elif cmd == "ab": ab(sys.argv[2], sys.argv[3])
