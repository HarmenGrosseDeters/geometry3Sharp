#!/usr/bin/env python3
"""Tables of the candidate measurement (CandidateBench.cs, BENCH_REPAIR=candidates).

Usage: python3 analyze_candidates.py results/candidates.jsonl [results/candidates_lost.jsonl ...]
(1) rejected candidates vs the final result: counts by rejection reason, comparison on IoU / Hausdorff / Chamfer / validity,
    and whether the final result fixes the rejection's finding at its location
(3) patch footprint of the best rejected candidate of the meshes that end in a fallback (solidify, wrap)
"""
import json, sys, collections

def load(path):
    rows = []
    for l in open(path):
        l = l.strip()
        if not l.startswith("{"):
            continue
        try:
            rows.append(json.loads(l))
        except Exception:
            pass
    return rows

def mid(f): return f.split(".")[0]

def hd(m):
    """two-sided certified Hausdorff upper bound (relative to the diagonal)"""
    a, b = num(m, "hd_fwd_hi"), num(m, "hd_back_hi")
    return None if a is None or b is None else max(a, b)

def clean(m): return m.get("not_clean", "x") == "" and m.get("si", 1) == 0

def num(m, k):
    v = m.get(k)
    return v if isinstance(v, (int, float)) and v == v else None

def cmp_geo(c, f, tol=1e-3):
    """per measure: +1 candidate better, 0 about equal, -1 worse (iou, hd, chamfer); equal within tol (iou: absolute; hd, chamfer: of the diagonal)"""
    res = {}
    if num(c, "iou") is not None and num(f, "iou") is not None:
        d = c["iou"] - f["iou"]
        res["iou"] = 1 if d > 0.002 else -1 if d < -0.002 else 0
    ch, fh = hd(c), hd(f)
    if ch is not None and fh is not None:
        d = fh - ch
        res["hd"] = 1 if d > 0.002 else -1 if d < -0.002 else 0
    if num(c, "chamfer") is not None and num(f, "chamfer") is not None:
        d = f["chamfer"] - c["chamfer"]
        res["chamfer"] = 1 if d > 1e-4 else -1 if d < -1e-4 else 0
    return res

def share(n, d): return "%d (%.0f%%)" % (n, 100.0 * n / d) if d else "0"

def part1(rows, title):
    print("\n## (1) %s" % title)
    ok = [r for r in rows if r.get("status") == "ok" and "real_stage" in r]
    print("meshes with a result row: %d of %d rows; status not ok: %d" % (len(ok), len(rows), len(rows) - len(ok)))
    st = collections.Counter(r["real_stage"] for r in ok)
    print("final stage (real run): " + ", ".join("%s %d" % kv for kv in sorted(st.items())))
    mis = [mid(r["file"]) for r in ok if not r.get("sim_matches_real", True)]
    print("benchmark copy of the chain disagrees with the real stage on %d meshes: %s" % (len(mis), " ".join(mis)))
    later = [r for r in ok if r.get("cand_later_final")]
    print("meshes whose final result comes from a later stage than an earlier rejected candidate: %d" % len([r for r in later if r.get("cand_rejected")]))
    print("\nrejections by candidate and reason (meshes that ended in a later stage; a mesh counts once per candidate kind):")
    cnt = collections.Counter(); meshes_with = collections.defaultdict(set)
    for r in later:
        for s in r.get("cand_rejected", []):
            kind, cls, reason = s.split(":", 2)
            cnt[(kind, reason)] += 1
            meshes_with[(kind, reason)].add(mid(r["file"]))
    print("| candidate | reason | meshes |"); print("|---|---|---|")
    for (kind, reason), n in sorted(cnt.items(), key=lambda kv: (kv[0][0], -kv[1])):
        extra = " " + " ".join(sorted(meshes_with[(kind, reason)])[:6]) if n <= 6 else ""
        print("| %s | %s | %d%s |" % (kind, reason, n, extra))
    # by final stage x first rejection
    print("\nfinal stage by what the repair result R was rejected for:")
    c2 = collections.Counter()
    for r in later:
        rr = [s for s in r.get("cand_rejected", []) if s.startswith("R:")]
        c2[(rr[0].split(":", 2)[2] if rr else "R accepted", r["real_stage"])] += 1
    print("| R rejected for | exact | solidify | wrap |"); print("|---|---|---|---|")
    for reason in sorted(set(k[0] for k in c2)):
        print("| %s | %d | %d | %d |" % (reason, c2[(reason, "exact")], c2[(reason, "solidify")], c2[(reason, "wrap")]))

    # comparison per rejected candidate with a mesh
    pairs = []
    for r in later:
        f = r.get("final_measures")
        for c in r.get("cand_list", []):
            if c.get("mesh") and f:
                pairs.append((r, c, f))
    print("\ncomparison of every rejected candidate with a mesh against the final result: %d pairs, %d meshes" % (len(pairs), len(set(p[0]["file"] for p in pairs))))
    groups = collections.defaultdict(list)
    for r, c, f in pairs:
        groups[(c["kind"], c["class"])].append((r, c, f))
    print("| candidate / rejected for | pairs | candidate clean | better on all 3 geometric | better on >= 2 | better IoU | better Hausdorff | better Chamfer | candidate clean AND better on >= 2 |")
    print("|---|---|---|---|---|---|---|---|---|")
    allp = {"all": pairs}
    for key, lst in list(sorted(groups.items())) + [(("all", ""), pairs)]:
        n = len(lst); cl = 0; a3 = 0; a2 = 0; bi = bh = bc = 0; cl2 = 0
        for r, c, f in lst:
            m = c["measures"]; g = cmp_geo(m, f)
            wins = [v for v in g.values() if v > 0]; loss = [v for v in g.values() if v < 0]
            if clean(m): cl += 1
            if len(g) == 3 and not loss and len(wins) >= 1 and len(wins) == 3: a3 += 1
            if len(wins) >= 2 and len(loss) == 0: a2 += 1
            if clean(m) and len(wins) >= 2 and len(loss) == 0: cl2 += 1
            bi += g.get("iou", 0) > 0; bh += g.get("hd", 0) > 0; bc += g.get("chamfer", 0) > 0
        print("| %s %s | %d | %d | %d | %d | %d | %d | %d | %d |" % (key[0], key[1], n, cl, a3, a2, bi, bh, bc, cl2))
    print("(better = candidate beats the final by more than 0.002 IoU / 0.2% of the diagonal Hausdorff / 1e-4 of the diagonal Chamfer; 'all 3' = better on each, 'ge 2' = better on at least 2 and worse on none)")

    # spatial finding
    print("\nthe rejection's finding: does the final result fix it at the location, and what does it cost elsewhere?")
    print("(near = within 2% of the diagonal of a finding point; 'fixed' = the final result has no defect there and keeps the input within 1% of the diagonal there)")
    hdr = ["pairs with finding points", "final defect-free near finding (always true if final clean)", "final keeps near geometry (in2m_near_max < 1%)", "FIXED at location", "final loses geometry far from finding (in2m_far_max >= 1%) while the candidate keeps it (< 1%)", "candidate near finding already within 1% (in2m, m2in)"]
    byc = collections.defaultdict(lambda: [0] * 6)
    for r, c, f in pairs:
        loc = c.get("local")
        if not loc: continue
        key = c["kind"] + " " + c["class"]
        for k in (key, "all"):
            b = byc[k]; b[0] += 1
            fl, cl_ = loc["final"], loc["cand"]
            nodef = not c.get("final_defect_near", False)
            keeps = fl["in2m_near_max"] < 0.01 and fl["m2in_near_max"] < 0.01
            b[1] += nodef; b[2] += keeps; b[3] += nodef and keeps
            b[4] += fl["in2m_far_max"] >= 0.01 and cl_["in2m_far_max"] < 0.01
            b[5] += cl_["in2m_near_max"] < 0.01 and cl_["m2in_near_max"] < 0.01
    print("| candidate | " + " | ".join(hdr) + " |"); print("|---" * (len(hdr) + 1) + "|")
    for k, b in sorted(byc.items()):
        print("| %s | %s |" % (k, " | ".join(str(x) for x in b)))
    # candidates without findings
    nofind = [(mid(r["file"]), c["kind"], c["class"]) for r, c, f in pairs if not c.get("local")]
    print("pairs without usable finding points: %d %s" % (len(nofind), " ".join("%s/%s" % (a, b) for a, b, _ in nofind[:12])))

    # candidates rejected with a clean mesh that beat the final
    print("\nrejected candidates that are clean (no defects) and not worse than the final on any geometric measure:")
    for r, c, f in pairs:
        m = c["measures"]
        if clean(m):
            g = cmp_geo(m, f)
            if g and all(v >= 0 for v in g.values()):
                print("  %s %s %s final %s: cand iou %.4f hd %s chamfer %.2e | final iou %.4f hd %s chamfer %.2e" % (
                    mid(r["file"]), c["kind"], c["reason"], r["real_stage"], m["iou"], "%.4f" % hd(m) if hd(m) is not None else "-", m.get("chamfer", float("nan")),
                    f["iou"], "%.4f" % hd(f) if hd(f) is not None else "-", f.get("chamfer", float("nan"))))
    # timing
    t = [r.get("cand_total_ms", 0) for r in ok]
    print("\nharness time per mesh: total %.1f min (sum), max %.0f s" % (sum(t) / 60000.0, max(t) / 1000.0 if t else 0))

def best_patch(p):
    """smallest single patch of the strategies (path joined / rings; with and without a ring of margin) with one simple boundary loop; a single
    patch is also a valid set of disjoint patches, so multi is at most single. Margin variants contain the plain ones: they are upper bounds too."""
    c = []
    for tag in ("single", "single_ring", "single_m1", "single_m1_ring"):
        k = tag + "_area_frac"
        if k in p and p.get(tag + "_ok", p.get("single_ok" if tag == "single" else "single_m1_ok")):
            c.append(p[k])
    p["single_area_frac"] = min(c) if c else p["single_area_frac"]
    p["single_valid"] = bool(c)
    p["multi_area_frac"] = min(p["multi_area_frac"], p["single_area_frac"]) if c else p["multi_area_frac"]
    return p

def part3(rows, title):
    print("\n## (3) %s" % title)
    fb = [r for r in rows if r.get("status") == "ok" and r.get("real_stage") in ("solidify", "wrap")]
    print("meshes ending in a fallback: %d (solidify %d, wrap %d)" % (len(fb), sum(r["real_stage"] == "solidify" for r in fb), sum(r["real_stage"] == "wrap" for r in fb)))
    best = []; nodef = []; nomesh = []
    for r in fb:
        ps = [p for p in r.get("patches", []) if "defects" in p]
        if not ps:
            nomesh.append(mid(r["file"])); continue
        withd = [p for p in ps if p["defects"].get("defect_tris", 0) > 0 and "single_area_frac" in p]
        zero = [p for p in ps if p["defects"].get("defect_tris", 0) == 0 and "capped" not in p["defects"]]
        if zero:
            nodef.append(mid(r["file"]))      # a rejected candidate without any defect (rejected by volume / parts only): no patch to measure, listed too
        if withd:
            p = best_patch(min(withd, key=lambda p: p["defects"]["defect_tris"])); best.append((mid(r["file"]), p))
        else:
            nomesh.append(mid(r["file"]) + "(capped)")
    print("a rejected candidate is defect-free (volume or lost parts only; a patch cannot fix that): %d  %s" % (len(nodef), " ".join(nodef)))
    print("(the tables below take, for every mesh with a rejected candidate that has defects, the one with the fewest defect triangles)")
    print("no rejected candidate with a mesh / too many intersections: %d  %s" % (len(nomesh), " ".join(nomesh)))
    print("meshes with a rejected candidate that has defects: %d" % len(best))
    def bucket(x): return "< 1 %" if x < 0.01 else "1-5 %" if x < 0.05 else "5-20 %" if x < 0.2 else "> 20 %"
    order = ["< 1 %", "1-5 %", "5-20 %", "> 20 %"]
    for tag, name in (("single", "single patch (best of 4 growth strategies; defects strictly inside not required)"),):
        b = collections.defaultdict(list)
        for i, p in best:
            b[bucket(p[tag + "_area_frac"])].append(i)
        print("\n%s: share of the surface area (greedy growth, not a proven minimum)" % name)
        print("| area share | meshes | ids |"); print("|---|---|---|")
        for k in order:
            print("| %s | %d | %s |" % (k, len(b[k]), " ".join(b[k][:40])))
        ok_ = sum(p["single_valid"] for _, p in best)
        print("a growth strategy reached a single simple boundary loop: %d of %d" % (ok_, len(best)))
    b = collections.defaultdict(list)
    for i, p in best:
        b[bucket(p["multi_area_frac"])].append(i)
    print("\ndisjoint patches (every defect cluster closed on its own, touching patches merged): total area share")
    print("| total area share | meshes | ids |"); print("|---|---|---|")
    for k in order:
        print("| %s | %d | %s |" % (k, len(b[k]), " ".join(b[k][:40])))
    pc = collections.Counter()
    for i, p in best:
        n = p["multi_patches"]; pc["1" if n == 1 else "2-3" if n <= 3 else "4-10" if n <= 10 else ">10"] += 1
    print("number of disjoint patches: " + ", ".join("%s: %d" % (k, pc[k]) for k in ["1", "2-3", "4-10", ">10"]))
    ll = sorted(p["single_loop_len_rel"] for _, p in best if p.get("single_valid"))
    if ll:
        print("boundary loop length of the single patch (relative to the bbox diagonal): median %.2f, p90 %.2f, max %.2f" % (ll[len(ll) // 2], ll[int(len(ll) * 0.9)], ll[-1]))
    dt = sorted(p["defects"]["defect_tris"] / max(1, p["defects"]["tris"]) for _, p in best)
    if dt:
        print("defect triangles as share of the candidate's triangles: median %.3f, p90 %.3f" % (dt[len(dt) // 2], dt[int(len(dt) * 0.9)]))
    print("\nper mesh (id, kind, defect tris / tris, defect comps (vertex), single area %, single m1 %, multi patches, multi area %):")
    for i, p in sorted(best, key=lambda x: x[1]["single_area_frac"]):
        print("  %s %s %d/%d comps %d single %.2f%% m1 %.2f%% patches %d multi %.2f%% loop %.2f" % (i, p["kind"], p["defects"]["defect_tris"], p["tris"], p["defect_components_vertex"],
              100 * p["single_area_frac"], 100 * p["single_m1_area_frac"], p["multi_patches"], 100 * p["multi_area_frac"], p["single_loop_len_rel"]))

if __name__ == "__main__":
    for path in sys.argv[1:]:
        rows = load(path)
        part1(rows, path)
        part3(rows, path)
