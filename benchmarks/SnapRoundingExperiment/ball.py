#!/usr/bin/env python3
"""The empty ball as a guarantee: exact clearances of the exact result against the rounding displacement.

docs/research/SNAP_ROUNDING_EMPTY_BALL_GUARANTEE_2026-11.md. Reads the JSON lines of `SnapRoundingExperiment ball <case>`
(EmptyBall.cs) and decides everything with fractions (no floating-point decision):

- delta_v = |round(p_v) - p_v|, the displacement of plain rounding (p_v the exact rational vertex);
- for every listed pair of triangles with no common vertex (all pairs closer than 16 ulps in doubles; the others are
  farther than any delta_A + delta_B), the exact distance d(A, B);
- lemma L1 of the note: the pair cannot meet after the move if d(A, B) > delta_A + delta_B, delta_T the largest
  displacement of T's corners. A triangle is *certified* if all its pairs satisfy this, a vertex if its star is;
- where SafeRounding's leftovers sit: their vertices' clearance (the smallest d over the pairs of their star),
  displacement and the move SafeRounding made, all in ulps of the largest coordinate; for leftover degenerate triangles,
  whether the exact triangle is already below the degenerate area (then no motion of rounding size can clear it).

  python3 ball.py run <out.jsonl> <case>...     run the tool, one process per case
  python3 ball.py tables <out.jsonl> [--unclean] print the tables of the note (--unclean: the results SafeRounding
                                                does not report as rounding clean only)
  python3 ball.py selfcheck                     the exact distance against hand-computed cases
"""
import json
import math
import os
import subprocess
import sys
from fractions import Fraction as Q

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(HERE, "bin", "Release", "net10.0", "SnapRoundingExperiment.dll")


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def norm2(a): return dot(a, a)
def clamp01(t): return Q(0) if t < 0 else Q(1) if t > 1 else t


def orient(a, b, c, d):
    return dot(sub(b, a), cross(sub(c, a), sub(d, a)))


def seg_seg2(p1, q1, p2, q2):
    """the squared distance of two segments, exact (Ericson, Real-Time Collision Detection, 5.1.9, in rationals)"""
    d1, d2, r = sub(q1, p1), sub(q2, p2), sub(p1, p2)
    a, e, f = norm2(d1), norm2(d2), dot(d2, r)
    if a == 0 and e == 0:
        return norm2(r)
    if a == 0:
        s, t = Q(0), clamp01(f / e)
    else:
        c = dot(d1, r)
        if e == 0:
            t, s = Q(0), clamp01(-c / a)
        else:
            b = dot(d1, d2)
            den = a * e - b * b
            s = clamp01((b * f - c * e) / den) if den != 0 else Q(0)
            t = (b * s + f) / e
            if t < 0:
                t, s = Q(0), clamp01(-c / a)
            elif t > 1:
                t, s = Q(1), clamp01((b - c) / a)
    c1, c2 = add(p1, mul(d1, s)), add(p2, mul(d2, t))
    return norm2(sub(c1, c2))


def point_tri2(p, a, b, c):
    """the squared distance of a point to a triangle, exact (Ericson 5.1.5); a degenerate triangle by its edges"""
    if norm2(cross(sub(b, a), sub(c, a))) == 0:
        return min(seg_seg2(p, p, a, b), seg_seg2(p, p, b, c), seg_seg2(p, p, c, a))
    ab, ac, ap = sub(b, a), sub(c, a), sub(p, a)
    d1, d2 = dot(ab, ap), dot(ac, ap)
    if d1 <= 0 and d2 <= 0: return norm2(ap)
    bp = sub(p, b)
    d3, d4 = dot(ab, bp), dot(ac, bp)
    if d3 >= 0 and d4 <= d3: return norm2(bp)
    vc = d1 * d4 - d3 * d2
    if vc <= 0 and d1 >= 0 and d3 <= 0:
        return norm2(sub(p, add(a, mul(ab, d1 / (d1 - d3)))))
    cp = sub(p, c)
    d5, d6 = dot(ab, cp), dot(ac, cp)
    if d6 >= 0 and d5 <= d6: return norm2(cp)
    vb = d5 * d2 - d1 * d6
    if vb <= 0 and d2 >= 0 and d6 <= 0:
        return norm2(sub(p, add(a, mul(ac, d2 / (d2 - d6)))))
    va = d3 * d6 - d5 * d4
    if va <= 0 and (d4 - d3) >= 0 and (d5 - d6) >= 0:
        return norm2(sub(p, add(b, mul(sub(c, b), (d4 - d3) / ((d4 - d3) + (d5 - d6))))))
    den = va + vb + vc
    v, w = vb / den, vc / den
    return norm2(sub(p, add(a, add(mul(ab, v), mul(ac, w)))))


def seg_meets_tri(p, q, a, b, c):
    """a segment and a triangle that are not coplanar have a common point (exact signs)"""
    s0, s1 = orient(a, b, c, p), orient(a, b, c, q)
    if (s0 > 0 and s1 > 0) or (s0 < 0 and s1 < 0) or (s0 == 0 and s1 == 0):
        return False  # coplanar: the edge and vertex distances decide
    o = [orient(p, q, a, b), orient(p, q, b, c), orient(p, q, c, a)]
    return all(x >= 0 for x in o) or all(x <= 0 for x in o)


def tri_tri2(A, B):
    """the squared distance of two triangles, exact: 0 if an edge of one meets the other, else the smallest of the
    nine edge pairs and six corner-triangle distances (rational: every closest point has rational parameters)"""
    for (P, T) in ((A, B), (B, A)):
        for i in range(3):
            if seg_meets_tri(P[i], P[(i + 1) % 3], *T):
                return Q(0)
    best = None
    for i in range(3):
        for j in range(3):
            d = seg_seg2(A[i], A[(i + 1) % 3], B[j], B[(j + 1) % 3])
            best = d if best is None or d < best else best
    for i in range(3):
        best = min(best, point_tri2(A[i], *B), point_tri2(B[i], *A))
    return best


def gt_sum(d2, x2, y2):
    """d > x + y for d, x, y >= 0 given as squares, exact"""
    lhs = d2 - x2 - y2
    return lhs > 0 and lhs * lhs > 4 * x2 * y2


def analyse(row):
    ulp = Q(row["ulp"])
    verts = {}
    for k, v in row["verts"].items():
        r = tuple(Q(x) for x in v[0:3])
        w = int(v[6])
        e = (Q(int(v[3]), w), Q(int(v[4]), w), Q(int(v[5]), w))
        s = tuple(Q(x) for x in v[7:10]) if len(v) >= 10 else None
        verts[int(k)] = (r, e, s)
    tris = {int(k): v for k, v in row["tris"].items()}
    delta2 = {v: norm2(sub(r, e)) for v, (r, e, s) in verts.items()}
    tdelta2 = {t: max(delta2[c] for c in tv) for t, tv in tris.items()}
    ex = lambda t: tuple(verts[c][1] for c in tris[t])
    # exact distances of the listed pairs; the clearance of a triangle: its smallest one (None: farther than 16 ulps)
    clear2 = {t: None for t in tris}
    bad = set()  # triangles with a pair that the lemma cannot clear under plain rounding
    pair_d2 = {}
    uncert_pairs = 0
    for ta, tb, _ in row["pairs"]:
        d2 = tri_tri2(ex(ta), ex(tb))
        pair_d2[(min(ta, tb), max(ta, tb))] = d2
        for t in (ta, tb):
            if clear2[t] is None or d2 < clear2[t]: clear2[t] = d2
        if not gt_sum(d2, tdelta2[ta], tdelta2[tb]):
            uncert_pairs += 1
            bad.add(ta); bad.add(tb)
    star = {v: [] for v in verts}
    for t, tv in tris.items():
        for c in tv: star[c].append(t)
    vbad = {v for v in verts if any(t in bad for t in star[v])}
    # the contrapositive as a check of this script: a pair that plain rounding makes intersect and that shares no
    # vertex is never certified
    viol = 0
    plain_apart = 0
    for ta, tb in row["plain_pairs"]:
        if set(tris[ta]) & set(tris[tb]): continue
        plain_apart += 1
        if ta not in bad or tb not in bad: viol += 1
    # the leftovers of SafeRounding
    def ulps(x2): return float(math.sqrt(x2) / ulp) if x2 is not None else None
    def vclear2(v):
        cs = [clear2[t] for t in star[v] if clear2[t] is not None]
        return min(cs) if cs else None
    left_v = set()
    for tv in row["left_degenerate"]: left_v.update(tv)
    for p in row["left_pairs"]:
        for tv in p: left_v.update(tv)
    unknown = sum(1 for v in left_v if v < 0)
    left_v.discard(-1)
    lv = []
    for v in sorted(left_v):
        r, e, s = verts[v]
        moved2 = norm2(sub(s, e)) if s is not None else None
        lv.append({"v": v, "certified": v not in vbad, "clear_ulps": ulps(vclear2(v)), "delta_ulps": ulps(delta2[v]),
                   "moved_ulps": ulps(moved2)})
    # leftover degenerate triangles: is the exact triangle below the area threshold already?
    diag = Q(row["diag"])
    eps2 = (Q(1, 10 ** 12) * diag * diag) ** 2
    deg = []
    for tv in row["left_degenerate"]:
        if min(tv) < 0: deg.append({"exact_below": None}); continue
        e = [verts[c][1] for c in tv]
        a2 = norm2(cross(sub(e[1], e[0]), sub(e[2], e[0]))) / 4
        # the move a corner needs at least to lift the area to the threshold: the height 2 eps / (longest edge) less the
        # height it has, in ulps (one corner moved perpendicular to the longest edge)
        lmax = math.sqrt(float(max(norm2(sub(e[1], e[0])), norm2(sub(e[2], e[1])), norm2(sub(e[0], e[2])))))
        need = (2 * math.sqrt(float(eps2)) - 2 * math.sqrt(float(a2))) / lmax / float(ulp) if lmax > 0 else None
        cl = [vclear2(c) for c in tv]
        cl = [x for x in cl if x is not None]
        deg.append({"exact_below": a2 < eps2, "exact_area_over_eps": float(math.sqrt(a2 / eps2)) if eps2 > 0 else None,
                    "need_ulps": need, "corner_clear_ulps": ulps(max(cl)) if cl else None})
    lp = []
    for pa, pb in row["left_pairs"]:
        share = bool(set(pa) & set(pb))
        d = None
        if not share and min(pa + pb) >= 0:
            d = ulps(tri_tri2(tuple(verts[c][1] for c in pa), tuple(verts[c][1] for c in pb)))
        lp.append({"shares_vertex": share, "exact_dist_ulps": d})
    nv, nt = len(verts), len(tris)
    return {"case": row["case"], "rounding_clean": row["rounding_clean"], "verts": nv, "tris": nt,
            "pairs_listed": len(row["pairs"]), "pairs_uncertified": uncert_pairs,
            "tris_certified": nt - len(bad), "verts_certified": nv - len(vbad),
            "plain_pairs": len(row["plain_pairs"]), "plain_pairs_apart": plain_apart, "contrapositive_violations": viol,
            "plain_degenerate": len(row["plain_degenerate"]),
            "left_vertices": lv, "left_unmapped": unknown, "left_degenerate": deg, "left_pairs": lp,
            "max_length_ulps": float(Q(row["max_length"]) / ulp), "max_drift_ulps": float(Q(row["max_drift"]) / ulp)}


def run(out, cases):
    done = set()
    if os.path.exists(out):
        done = {json.loads(l)["case"] for l in open(out)}
    for c in cases:
        if c in done: continue
        p = subprocess.run(["dotnet", TOOL, "ball", c], capture_output=True, text=True, timeout=1800)
        line = p.stdout.strip().splitlines()[-1] if p.stdout.strip() else json.dumps({"case": c, "ok": False})
        row = json.loads(line)
        res = analyse(row) if row.get("ok") else {"case": c, "ok": False}
        with open(out, "a") as f:
            f.write(json.dumps(res) + "\n")
        print(c, "done", flush=True)


def med(xs):
    xs = sorted(x for x in xs if x is not None)
    return xs[len(xs) // 2] if xs else None


def fmt(x):
    return "-" if x is None else ("%.3g" % x)


def tables(path, only_unclean=False):
    rows = [json.loads(l) for l in open(path) if '"left_vertices"' in l]
    if only_unclean:
        rows = [r for r in rows if not r["rounding_clean"]]
    print("| case | SafeRounding clean | triangles | certified triangles | certified vertices | pairs not certified | plain-rounding pairs apart (all not certified) | leftover vertices | of them certified | clearance, ulps: median, largest | displacement of plain rounding, ulps: median | SafeRounding's move, ulps: median, largest | clearance > 2 x max(rounding, move) |")
    print("|---|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---|---:|")
    tot = {"lv": 0, "lvc": 0, "ok": 0, "sub_ulp": 0, "deg": 0, "deg_below": 0, "lp": 0, "lp_share": 0, "viol": 0, "t": 0, "tc": 0, "v": 0, "vc": 0}
    for r in rows:
        lv = r["left_vertices"]
        cl = [x["clear_ulps"] for x in lv]
        mv = [x["moved_ulps"] for x in lv]
        ok = sum(1 for x in lv if x["clear_ulps"] is not None and x["clear_ulps"] > 2 * max(x["delta_ulps"], x["moved_ulps"] or 0))
        # (no listed pair: the clearance is above 16 ulps less the error of the double distance, taken as 14)
        okfar = sum(1 for x in lv if x["clear_ulps"] is None and 2 * max(x["delta_ulps"], x["moved_ulps"] or 0) < 14)
        tot["lv"] += len(lv); tot["lvc"] += sum(1 for x in lv if x["certified"]); tot["ok"] += ok + okfar
        tot["sub_ulp"] += sum(1 for x in cl if x is not None and x < 1)
        tot["deg"] += len(r["left_degenerate"]); tot["deg_below"] += sum(1 for x in r["left_degenerate"] if x["exact_below"])
        tot["lp"] += len(r["left_pairs"]); tot["lp_share"] += sum(1 for x in r["left_pairs"] if x["shares_vertex"])
        tot["viol"] += r["contrapositive_violations"]
        tot["t"] += r["tris"]; tot["tc"] += r["tris_certified"]; tot["v"] += r["verts"]; tot["vc"] += r["verts_certified"]
        print(f"| {r['case']} | {'yes' if r['rounding_clean'] else 'no'} | {r['tris']} | {r['tris_certified']} ({100 * r['tris_certified'] / r['tris']:.0f} %) | "
              f"{r['verts_certified']} ({100 * r['verts_certified'] / r['verts']:.0f} %) | {r['pairs_uncertified']} | {r['plain_pairs_apart']} | {len(lv)} | "
              f"{sum(1 for x in lv if x['certified'])} | {fmt(med(cl))}, {fmt(max([x for x in cl if x is not None], default=None))} | "
              f"{fmt(med([x['delta_ulps'] for x in lv]))} | {fmt(med(mv))}, {fmt(max([x for x in mv if x is not None], default=None))} | {ok + okfar} |")
    print()
    print(f"all {len(rows)} results: triangles certified {tot['tc']} of {tot['t']}, vertices {tot['vc']} of {tot['v']}; "
          f"leftover vertices {tot['lv']}, certified under plain rounding {tot['lvc']}, clearance above twice the larger of rounding and move {tot['ok']}, "
          f"clearance below one ulp {tot['sub_ulp']}; leftover degenerate triangles {tot['deg']}, exact triangle already below the area threshold {tot['deg_below']}; "
          f"needed move of a corner of a leftover degenerate triangle, ulps: median {fmt(med([x.get('need_ulps') for r in rows for x in r['left_degenerate']]))}, "
          f"largest clearance among its corners: median {fmt(med([x.get('corner_clear_ulps') for r in rows for x in r['left_degenerate']]))}; "
          f"leftover intersecting pairs {tot['lp']}, sharing a corner position {tot['lp_share']}; contrapositive violations {tot['viol']} (must be 0)")


def selfcheck():
    P = lambda *x: tuple(Q(v) for v in x)
    A = (P(0, 0, 0), P(1, 0, 0), P(0, 1, 0))
    checks = [
        (tri_tri2(A, (P(0, 0, 2), P(1, 0, 2), P(0, 1, 2))), Q(4)),            # parallel planes 2 apart
        (tri_tri2(A, (P(2, 2, 0), P(3, 2, 0), P(2, 3, 0))), Q(9, 2)),         # coplanar, corner (2,2) to edge x+y=1
        (tri_tri2(A, (P(Q(1, 4), Q(1, 4), -1), P(Q(1, 4), Q(1, 4), 1), P(5, 5, 5))), Q(0)),  # an edge pierces A
        (tri_tri2(A, (P(2, -1, 1), P(2, 1, 1), P(3, 0, 5))), Q(2)),           # edge to edge: (1,0,0) to (2,0,1)
        (point_tri2(P(Q(1, 4), Q(1, 4), 3), *A), Q(9)),
        (seg_seg2(P(0, 0, 0), P(2, 0, 0), P(1, 1, 0), P(3, 1, 0)), Q(1)),     # parallel overlapping
    ]
    ok = all(a == b for a, b in checks)
    print("selfcheck:", [str(a) for a, _ in checks], "ok" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    if sys.argv[1] == "run":
        run(sys.argv[2], sys.argv[3:])
    elif sys.argv[1] == "tables":
        tables(sys.argv[2], "--unclean" in sys.argv)
    elif sys.argv[1] == "selfcheck":
        sys.exit(selfcheck())
