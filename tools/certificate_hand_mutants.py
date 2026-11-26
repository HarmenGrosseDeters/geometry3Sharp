#!/usr/bin/env python3
"""Hand mutants of the certifying checker of the exact boolean (docs/QA.md 2.4a).

The checker (tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs) is test code, which Stryker does not mutate. This
script switches off or reverses each of its conditions and signs in turn, runs BooleanCertificateTests (without the
Nightly test), and reports whether the tests fail (KILLED) or not (SURVIVED). The file is restored afterwards, also on
an error. Every mutant must be killed. About 75 minutes (one build and test run per mutant).

The mutants of the floating-point filters (2026-11-25) make each filter decide wrongly or too early; the mutants under
EQUIVALENT change only the cost, never a verdict (the reason is given with each), and `--equivalent` checks that they pass.

Usage (from the repository root, with the .NET 10 SDK): python3 tools/certificate_hand_mutants.py [--equivalent] [name ...]
"""

import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHECKER = os.path.join(ROOT, "tests", "geometry3Sharp.Tests", "Harness", "BooleanCertificate.cs")

# (name, the text in the checker, its replacement)
M=[
("wedge compare off", "if (va[m] != vb[m]) return", "if (false && va[m] != vb[m]) return"),
("anchors off", "if ((Care(v, m) || Care(truth, m)) && v[m] != truth[m])", "if (false)"),
("jump sign", "b[TriMesh[u]] += Dot(triNormal[u], ns).Sign > 0 ? 1 : -1;", "b[TriMesh[u]] += Dot(triNormal[u], ns).Sign > 0 ? -1 : 1;"),
("stack ignores others", "if (inside[g][k] > 0) s.Add(u);", "if (false) s.Add(u);"),
("C4 keep rule", "if (Keep[i] != (inF != inB)) return", "if (false) return"),
("C4 flip rule", "if (Keep[i] && Flip[i] != inF) return", "if (false) return"),
("C4 copies", "if (Keep[i]) return $\"piece {i}: a coplanar copy is kept\";", ";"),
("up/down swapped", "int[] va = up[ja] ? Front[a] : back[a];", "int[] va = up[ja] ? back[a] : Front[a];"),
("radial order reversed", "int s = (half[x] == 1 || half[x] == 3) ? -Orient(p, q, r[x], r[y]) : 0;", "int s = (half[x] == 1 || half[x] == 3) ? Orient(p, q, r[x], r[y]) : 0;"),
("ray crossing sign", "return nd > 0 ? 1 : -1;", "return nd > 0 ? -1 : 1;"),
("ray behind counted", "if (side * nd > 0) return 0;", "if (false) return 0;"),
("C1 area off", "if (num != nt[axis] * den) return", "if (false) return"),
("C1 inside off", "if (Dot(Cross(Sub(c[(e + 1) % 3], c[e]), Sub(p[k], c[e])), nt).Sign < 0) return", "if (false) return"),
("C1 degenerate off", "if (IsZero(np)) return \"degenerate\";", ";"),
("C1 twice off", "if (edges.ContainsKey(e)) return \"directed edge used twice\";", ";"),
("C1 boundary edges off", "if (on == false) return \"an unpaired", "if (false) return \"an unpaired"),
("C1 orientation off", "if (Dot(np, nt).Sign <= 0) return", "if (false) return"),
("C1 plane off", "if (Orient(c[0], c[1], c[2], p[k]) != 0) return", "if (false) return"),
("C5 closed off", "if (r != \"\") return \"result: \" + r;", ";"),
("C5 chain off", "if (last && acc != 0) return", "if (false) return"),
("C5 coverage violation off", "if (near == false) return $\"a corner", "if (false) return $\"a corner"),
("C5 Near always", "return Q.Compare(SquaredDistance(p, t[0], t[1], t[2]), delta2) <= 0;", "return true;"),
("care ignores cull", "bool Care(int[] v, int m) => CullMesh < 0 || m == CullMesh || v[CullMesh] > 0;", "bool Care(int[] v, int m) => true;"),
("anchor marks all", "if (Care(v, m)) anchored[m][Find(uf[m], i)] = true;", "for (int z = 0; z < n; ++z) anchored[m][z] = true;"),
("culled ray off", "if (w[CullMesh] > 0) return", "if (false) return"),
("culled meet off", "if (ExactTriangleIntersection.Intersects(ref u, ref v)) return", "if (false) return"),
("area check off", "if (kv.Value.Item1 * kv.Value.Item4 != kv.Value.Item3 * kv.Value.Item2)", "if (false)"),
("degenerate near off", "if (n[0].Sign == 0 && n[1].Sign == 0 && n[2].Sign == 0) {", "if (false) {"),
("segment clamp", "if (Q.Compare(t, l2) >= 0) { Q[] bp", "if (false) { Q[] bp"),
("reversed count off", "if (reversed != ReorientedTriangles) return", "if (false) return"),
("twice off", "if (seen.Add((TriMesh[t], TriTid[t])) == false) return", "if (false) return"),
("missing input off", "return $\"input triangle {m}:{t} is not in the arrangement\";", "continue;"),
("bound off", "if (MaxLength > MaxLengthBound) return", "if (false) return"),
("orientation check off", "if (consistentBefore[Root(t)]) return", "if (false) return"),
("exempt all components", "if (ex == false) continue;", ";"),
("classified rule", "classified[i] = s.Min() == src;", "classified[i] = true;"),
("C2 coplanar pairs skipped", "if (pos == 0 && neg == 0) { Coplanar++; return CoplanarMeet(p, q); }", "if (pos == 0 && neg == 0) { Coplanar++; return false; }"),
("C2 crossing of a plane off", "if (Mixed(O3(a, b, q[0], q[1]), O3(a, b, q[1], q[2]), O3(a, b, q[2], q[0])) == false) return true;", "if (false) return true;"),
("C2 end on the plane off", "if (Has(q, a) == false && InTriangle(a, q, b)) return true;", "if (false) return true;"),
("C2 fold off", [("if (O2(a, b, p[(i + 2) % 3]) * O2(a, b, qc) > 0) return true;", "if (false) return true;"),
                ("if (fp[i].Side(X[p[(i + 2) % 3]]) * Sp(i, Index(q, qc)) > 0) return true;", "if (false) return true;")]),
("C2 coplanar proper crossing off", [("if (o[j] * o[(j + 1) % 3] < 0 && o2(c, d, a) * o2(c, d, b) < 0) return true;", "if (false) return true;"),
                                    ("if (o[j] * o[(j + 1) % 3] < 0 && sb(j, i) * sb(j, ea) < 0) return true;", "if (false) return true;")]),
("C2 q's edges against p off", "return EdgesMeet(p, sp, q) || EdgesMeet(q, sq, p);", "return EdgesMeet(p, sp, q);"),
("C2 pairs not tested", [("if (InSamePlane(i, j)) meets[idx] = test.MeetBeyondSharedInPlane(", "if (InSamePlane(i, j)) meets[idx] = false && test.MeetBeyondSharedInPlane("),
                        ("else meets[idx] = test.MeetBeyondShared(pieceV[i], pieceV[j]);", "else meets[idx] = false;")]),
("C2 copies are paired", "int[] part = Enumerable.Range(0, PieceCorners.Length).Where(i => classified[i]).ToArray();", "int[] part = Enumerable.Range(0, PieceCorners.Length).ToArray();"),
("C2 not in Check", "r = embedding ? CheckEmbedding() : \"\";", "r = \"\";"),
("C2 long determinant sign", "return Math.Sign(bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx));", "return -Math.Sign(bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx));"),
("C2 straddle off", "&& StraddlesInPlaneOfSource(i, u)) {", "&& false) {"),
("C2 straddle: inside rule", [("            if (inside) return false;\n            for (int e = 0; e < 3; ++e) {\n                bool pOut = true, uOut = true;\n                for (int k = 0; k < 3; ++k) {", "            if (inside) return true;\n            for (int e = 0; e < 3; ++e) {\n                bool pOut = true, uOut = true;\n                for (int k = 0; k < 3; ++k) {")]),
("C2 straddle (forms): inside rule", [("            if (inside) return false;\n            for (int e = 0; e < 3; ++e) {\n                bool pOut = true, uOut = true;\n                for (int k = 0; k < 3 && pOut; ++k)", "            if (inside) return true;\n            for (int e = 0; e < 3; ++e) {\n                bool pOut = true, uOut = true;\n                for (int k = 0; k < 3 && pOut; ++k)")]),
("C2 straddle (forms): piece side of the separation off", "if (pOut || uOut) return false;\n            }\n            return true;\n        }\n\n        /// <summary>\n        /// Coplanar triangles p and u (both non-degenerate): p is neither inside", "if (uOut) return false;\n            }\n            return true;\n        }\n\n        /// <summary>\n        /// Coplanar triangles p and u (both non-degenerate): p is neither inside"),
("C2 straddle (forms): triangle side of the separation off", "if (pOut || uOut) return false;\n            }\n            return true;\n        }\n\n        /// <summary>\n        /// Coplanar triangles p and u (both non-degenerate): p is neither inside", "if (pOut) return false;\n            }\n            return true;\n        }\n\n        /// <summary>\n        /// Coplanar triangles p and u (both non-degenerate): p is neither inside"),
("C2 straddle: separation rule", "if (pOut || uOut) return false;\n            }\n            return true;\n        }\n\n        /// <summary>\n        /// Do the triangles p and q", "if (pOut) return false;\n            }\n            return true;\n        }\n\n        /// <summary>\n        /// Do the triangles p and q"),
# --- the cost work of C2 and its stacks (#1632): the edge forms, the groups of equal pieces, the parallel loops
("edge form: sign of the offset", "- d * p.W).Sign;", "+ d * p.W).Sign;"),
("edge form: normal and edge crossed the wrong way", "BigInteger[] m = Cross(n, Sub(b, a));", "BigInteger[] m = Cross(Sub(b, a), n);"),
("edge form: offset of the wrong point", "d = m[0] * a.X + m[1] * a.Y + m[2] * a.Z;", "d = m[0] * a.X + m[1] * a.Y + m[2] * a.W;"),
("pair forms: shared plane not by the key", "return pieceKeys[i].Length > 0 && pieceKeys[i] == pieceKeys[j];", "return pieceKeys[i].Length > 0;"),
("stacks: groups ignore the third corner", "ids[0] + \",\" + ids[1] + \",\" + ids[2];", "ids[0] + \",\" + ids[1];"),
("stacks: source of the piece not skipped in the table", "if (u == src) { s.Add(u); srcAdded = true; continue; }", "if (u == src) { srcAdded = true; }"),
("stacks: a centroid on an edge is not reported", "if (inside[g][k] == 0) { failure[i] =", "if (false) { failure[i] ="),
("stacks: failure of the highest piece", "            for (int i = 0; i < n; ++i)\n                if (failure[i] != null) throw new CertificateException(failure[i]);", "            for (int i = n - 1; i >= 0; --i)\n                if (failure[i] != null) throw new CertificateException(failure[i]);"),
("pairs: verdict of the highest pair", "for (int idx = 0; idx < pairs.Count; ++idx) {\n                if (coplanarPair[idx]) coplanarCount++;", "for (int idx = pairs.Count - 1; idx >= 0; --idx) {\n                if (coplanarPair[idx]) coplanarCount++;"),
("straddle: verdict of the highest piece", "            foreach (string r in straddle)\n                if (r != null) return r;", "            foreach (string r in straddle.Reverse())\n                if (r != null) return r;"),
("straddle: a piece out of its source's plane takes the forms", "if (InSourcePlane(i) == false) return StraddlesInPlane(PieceCorners[i], triCorners[u]);", ";"),
("removed parts not reported", "if (RemovedParts.Count > 0)", "if (false)"),
# --- the floating-point filters of 2026-11-24 (box filters of C2 / C3, the double filters of C5, the floor; added 2026-11-25)
("box filter: neither side widened", [("lo[k] = l - Math.Abs(l) * (1.0 / (1L << 49)) - double.Epsilon;", "lo[k] = l;"),
                                    ("hi[k] = h + Math.Abs(h) * (1.0 / (1L << 49)) + double.Epsilon;", "hi[k] = h;")]),
("box filter: widening tightened 2^-49 to 2^-62", [("lo[k] = l - Math.Abs(l) * (1.0 / (1L << 49))", "lo[k] = l - Math.Abs(l) * (1.0 / (1L << 62))"),
                                                 ("hi[k] = h + Math.Abs(h) * (1.0 / (1L << 49))", "hi[k] = h + Math.Abs(h) * (1.0 / (1L << 62))")]),
("box filter: NaN guard off", "if (double.IsNaN(l) || double.IsNaN(h) || double.IsInfinity(l) || double.IsInfinity(h)) {", "if (false) {"),
("box filter: scan drops the last member", "for (int i = 0; i < pb.Members.Length; ++i) {", "for (int i = 0; i < pb.Members.Length - 1; ++i) {"),
("box filter: scan tests a wrong axis", "&& pb.Lo[x + 2] <= qhi[2] && qlo[2] <= pb.Hi[x + 2])", "&& pb.Lo[x + 2] <= qhi[1] && qlo[2] <= pb.Hi[x + 1])"),
("box filter: tree visits one child", "if (nodeLeft[node] >= 0) { stack.Push(nodeLeft[node]); stack.Push(nodeRight[node]); continue; }", "if (nodeLeft[node] >= 0) { stack.Push(nodeLeft[node]); continue; }"),
("box filter: tree prunes on a wrong axis", "|| nodeBox[o + 2] > qhi[2] || qlo[2] > nodeBox[o + 5]) continue;", "|| nodeBox[o + 2] > qhi[1] || qlo[2] > nodeBox[o + 4]) continue;"),
("box filter: tree leaf drops its last item", "for (int i = nodeStart[node]; i < nodeEnd[node]; ++i) {\n                        int x = 3 * items[i];\n                        if (lo[x] <= qhi[0]", "for (int i = nodeStart[node]; i < nodeEnd[node] - 1; ++i) {\n                        int x = 3 * items[i];\n                        if (lo[x] <= qhi[0]"),
("C5 reject without margin", "double tol = covDelta + 3 * covEps;", "double tol = covDelta;"),
("C5 reject margin inside delta", "double tol = covDelta + 3 * covEps;", "double tol = covDelta - 3 * covEps;"),
("C5 reject on a wrong axis", "|| pa.z < ta.Lo[2] - tol || pa.z > ta.Hi[2] + tol) return false;", "|| pa.z < ta.Lo[1] - tol || pa.z > ta.Hi[1] + tol) return false;"),
("C5 accept without margin", "if (DistanceToTriangleApprox(pa, ta.A, ta.B, ta.C) + 3 * covEps <= covDelta) return true;", "if (DistanceToTriangleApprox(pa, ta.A, ta.B, ta.C) <= covDelta) return true;"),
("C5 accept margin outside delta", "if (DistanceToTriangleApprox(pa, ta.A, ta.B, ta.C) + 3 * covEps <= covDelta) return true;", "if (DistanceToTriangleApprox(pa, ta.A, ta.B, ta.C) <= covDelta + 3 * covEps) return true;"),
("C5 double filter decides, no exact fallback", "if (DistanceToTriangleApprox(pa, ta.A, ta.B, ta.C) + 3 * covEps <= covDelta) return true;", "return DistanceToTriangleApprox(pa, ta.A, ta.B, ta.C) <= covDelta;"),
("C5 approximation: both guards off", [("if (ExactPoint3.BitLength(q.N) > 1000 || ExactPoint3.BitLength(q.D) > 1000) return false;", ";"),
                                      ("return double.IsInfinity(v) == false && double.IsNaN(v) == false;", "return true;")]),
("C5 filter bound tightened 2^-40 to 2^-58", "Math.Max((maxAbs + extent) / (double)(1L << 40),", "Math.Max((maxAbs + extent) / (double)(1L << 58),"),
("C5 floor 2^-480 to 1e-300", "Math.Pow(2, -480));", "1e-300);"),
("C5 floor 2^-480 to 2^-560", "Math.Pow(2, -480));", "Math.Pow(2, -560));"),
("C5 filters see no slack in Cover", "covEps = FilterEps(", "covEps = 0 * FilterEps("),
# (first listed as equivalent, killed: a faulty piece outside its source triangle (InjectedCrossingSheet_AndFoldedCopies_AreCaught)
# needs src in the list, or the check throws instead of rejecting; CoverageTest_UsesTheSlackOfItsCoordinates bounds the slack above)
("box filter: source not added", "if (r.Contains(src) == false) r.Add(src);", ";"),
("C5 filter bound loosened 2^-40 to 2^-30", "Math.Max((maxAbs + extent) / (double)(1L << 40),", "Math.Max((maxAbs + extent) / (double)(1L << 30),"),
# (first listed as equivalent; not: a numerator below 2^1024 over a denominator above it converts to finite / infinity = 0, which the
# finiteness check passes; CoverageFilters_AgreeWithTheExactTest_AtDistanceDeltaWithRoundedCoordinates, review of #1604)
("C5 approximation: size guard off", "if (ExactPoint3.BitLength(q.N) > 1000 || ExactPoint3.BitLength(q.D) > 1000) return false;", ";"),
]

# Equivalent mutants (not run by default; `--equivalent` runs them, and each must survive). Each changes no verdict:
EQUIVALENT = [
("stacks: groups ignore the plane of the source", "string key = triPlane[PieceSource[i]] + \"|\" + ids[0]", "string key = \"|\" + ids[0]",
 "pieces with the same three corner ids are the same triangle, so in the same plane; the plane of the source only differs for a piece outside its source's plane, which C1 rejects before C2 and C3 run (Check)"),
("C5 accept: weights not clamped", "v = Math.Min(1, Math.Max(0, v)); w = Math.Min(1 - v, Math.Max(0, w));", ";",
 "not proved, equivalent in practice: the clamp is a belt; in face, edge and vertex regions the weights are in [0, 1] up to rounding, and no input of the thin-triangle test, of 4 million random thin triangles (step A) and of 1.2 million targeted near-collinear cases (2026-11-25, double routine against an exact Fraction reference) took them outside by more than an ulp of v + w, which moves the point by an ulp of the triangle, far below the margin 3 eps"),
("C5 filter bound without the extent", "Math.Max((maxAbs + extent) / (double)(1L << 40),", "Math.Max((maxAbs) / (double)(1L << 40),",
 "the extent is at most twice the largest coordinate, so the slack falls by at most a factor 3, still about 2^-41.6 of the coordinates against a rounding of a few 2^-52"),
("box filter: tree leaf drops touching boxes", "if (lo[x] <= qhi[0] && qlo[0] <= hi[x] && lo[x + 1] <= qhi[1]", "if (lo[x] < qhi[0] && qlo[0] < hi[x] && lo[x + 1] <= qhi[1]",
 "as above, in the tree's leaves"),
("box filter: scan drops touching boxes", "if (pb.Lo[x] <= qhi[0] && qlo[0] <= pb.Hi[x] &&", "if (pb.Lo[x] < qhi[0] && qlo[0] < pb.Hi[x] &&",
 "widened boxes that touch exactly belong to exact boxes apart by at least the widening less the errors (> 0)"),
("box filter: hi not widened", "hi[k] = h + Math.Abs(h) * (1.0 / (1L << 49)) + double.Epsilon;", "hi[k] = h;",
 "as above, the other side"),
("box filter: lo not widened", "lo[k] = l - Math.Abs(l) * (1.0 / (1L << 49)) - double.Epsilon;", "lo[k] = l;",
 "the widening of one side alone (2^-49 of the coordinate, about 8 ulp) is above the sum of both boxes' approximation errors (2 ulp each)"),
("box filter: widening loosened 2^-49 to 2^-30", [("lo[k] = l - Math.Abs(l) * (1.0 / (1L << 49))", "lo[k] = l - Math.Abs(l) * (1.0 / (1L << 30))"),
                                                ("hi[k] = h + Math.Abs(h) * (1.0 / (1L << 49))", "hi[k] = h + Math.Abs(h) * (1.0 / (1L << 30))")],
 "larger boxes only keep more pairs for the exact tests behind the filter (the filter never decides a pair by itself)"),
("box filter: always the tree", "if (m.Length > PlaneTreeMin) pb.Tree", "if (true) pb.Tree",
 "the tree and the linear scan return the same set (the same closed-box test), CoplanarNear sorts it"),
("box filter: never the tree", "if (m.Length > PlaneTreeMin) pb.Tree", "if (false) pb.Tree",
 "as above: only the cost changes"),
("C5 reject margin loosened x4", "double tol = covDelta + 3 * covEps;", "double tol = covDelta + 12 * covEps;",
 "a larger reject tolerance only hands more points to the exact test"),
("C5 accept margin loosened x4", "+ 3 * covEps <= covDelta) return true;", "+ 12 * covEps <= covDelta) return true;",
 "a larger accept margin only hands more points to the exact test"),
("C5 floor 2^-480 to 2^-440", "Math.Pow(2, -480));", "Math.Pow(2, -440));",
 "a larger floor only widens both margins (the safe direction)"),
("C5 floor 2^-480 to 2^-520", "Math.Pow(2, -480));", "Math.Pow(2, -520));",
 "still sound: the only underflow-sensitive step is the final length of p - q (the clamp keeps q a point of the triangle "
 "whatever d1..d6 lost); an accept needs delta >= 3 floor, and at distances d >= 2^-519 the squares of the length lose at most "
 "2^-1075 / d absolute, about 2^-556, far below the margin 3 * 2^-520 (the floor must stay above about 2^-539). The argument "
 "depends on the clamp: below about 2^-255 the products of the weights are subnormal noise and only the clamp keeps q in the "
 "triangle, so it stands or falls with 'weights not clamped' (CoverageFilters_AgreeWithTheExactDistanceTest_OnThinTriangles "
 "covers the scales 2^-250 to 2^-280)"),
("C5 approximation: finiteness check off", "return double.IsInfinity(v) == false && double.IsNaN(v) == false;", "return true;",
 "behind the 1000-bit guard numerator and denominator convert to finite doubles, the denominator at least 1, so the quotient is finite"),
("C5 accept: NaN weights not returned", "if (double.IsNaN(v) || double.IsNaN(w)) return double.NaN;", ";",
 "a NaN weight survives Math.Min / Math.Max (they propagate NaN) and gives a NaN distance, which no accept takes"),
]


def run_tests():
    return subprocess.run(["dotnet", "test", os.path.join(ROOT, "tests", "geometry3Sharp.Tests"), "-c", "Release",
                           "-f", "net10.0", "--filter", "(FullyQualifiedName~BooleanCertificateTests|FullyQualifiedName~BooleanCertificateFanTests)&Category!=Nightly"],
                          capture_output=True, text=True)


def edits(entry):
    """the (old, new) replacements of a mutant: one pair, or a list of pairs (a mutant of several sites)"""
    return entry[1] if isinstance(entry[1], list) else [(entry[1], entry[2])]


def main(selected):
    equivalent = "--equivalent" in selected
    selected = [s for s in selected if s != "--equivalent"]
    original = open(CHECKER, encoding="utf-8").read()
    # the baseline: on the unmutated checker the tests must pass, or every mutant would count as killed
    if "Passed!" not in run_tests().stdout:
        print("the tests fail on the unmutated checker: no mutant is run")
        return 2
    survived = 0
    try:
        for entry in (EQUIVALENT if equivalent else M):
            name = entry[0]
            if selected and name not in selected:
                continue
            pairs = edits(entry)
            bad = [old for old, _ in pairs if original.count(old) != 1]
            if bad:
                print(f"{name}: the text is not in the checker exactly once ({[original.count(old) for old in bad]})")
                survived += 1
                continue
            text = original
            for old, new in pairs:
                text = text.replace(old, new)
            with open(CHECKER, "w", encoding="utf-8") as f:
                f.write(text)
            run = run_tests()
            killed = "Failed!" in run.stdout
            passed = "Passed!" in run.stdout and not killed
            if equivalent:
                # an equivalent mutant must build and pass
                survived += 0 if passed else 1
                verdict = 'SURVIVED (equivalent: ' + entry[-1] + ')' if passed else 'KILLED (not equivalent)' if killed else 'NO TEST RUN (the build or the run failed)'
                print(f"{name}: {verdict}", flush=True)
            else:
                survived += 0 if killed else 1
                print(f"{name}: {'KILLED' if killed else 'SURVIVED'}", flush=True)
    finally:
        with open(CHECKER, "w", encoding="utf-8") as f:
            f.write(original)
    return 1 if survived else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
