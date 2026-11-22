#!/usr/bin/env python3
"""Hand mutants of the certifying checker of the exact boolean (docs/QA.md 2.4a).

The checker (tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs) is test code, which Stryker does not mutate. This
script switches off or reverses each of its conditions and signs in turn, runs BooleanCertificateTests (without the
Nightly test), and reports whether the tests fail (KILLED) or not (SURVIVED). The file is restored afterwards, also on
an error. Every mutant must be killed. About 45 minutes (one build and test run per mutant).

Usage (from the repository root, with the .NET 10 SDK): python3 tools/certificate_hand_mutants.py [name ...]
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
("stack ignores others", "if (inside > 0) s.Add(u);", "if (false) s.Add(u);"),
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
("C2 fold off", "if (O2(a, b, p[(i + 2) % 3]) * O2(a, b, qc) > 0) return true;", "if (false) return true;"),
("C2 coplanar proper crossing off", "if (o[j] * o[(j + 1) % 3] < 0 && o2(c, d, a) * o2(c, d, b) < 0) return true;", "if (false) return true;"),
("C2 q's edges against p off", "return EdgesMeet(p, sp, q) || EdgesMeet(q, sq, p);", "return EdgesMeet(p, sp, q);"),
("C2 pairs not tested", "if (test.MeetBeyondShared(pieceV[i], pieceV[j])) {", "if (false) {"),
("C2 copies are paired", "int[] part = Enumerable.Range(0, PieceCorners.Length).Where(i => classified[i]).ToArray();", "int[] part = Enumerable.Range(0, PieceCorners.Length).ToArray();"),
("C2 not in Check", "r = embedding ? CheckEmbedding() : \"\";", "r = \"\";"),
("C2 long determinant sign", "return Math.Sign(bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx));", "return -Math.Sign(bx * (cy * dz - cz * dy) - by * (cx * dz - cz * dx) + bz * (cx * dy - cy * dx));"),
("C2 straddle off", "&& StraddlesInPlane(PieceCorners[i], triCorners[u]))", "&& false)"),
("C2 straddle: inside rule", "if (inside) return false;", "if (inside) return true;"),
("C2 straddle: separation rule", "if (pOut || uOut) return false;", "if (pOut) return false;"),
("removed parts not reported", "if (RemovedParts.Count > 0)", "if (false)"),
]


def main(selected):
    original = open(CHECKER, encoding="utf-8").read()
    survived = 0
    try:
        for name, old, new in M:
            if selected and name not in selected:
                continue
            if original.count(old) != 1:
                print(f"{name}: the text is not in the checker exactly once ({original.count(old)})")
                survived += 1
                continue
            with open(CHECKER, "w", encoding="utf-8") as f:
                f.write(original.replace(old, new))
            run = subprocess.run(["dotnet", "test", os.path.join(ROOT, "tests", "geometry3Sharp.Tests"), "-c", "Release",
                                  "-f", "net10.0", "--filter", "FullyQualifiedName~BooleanCertificateTests&Category!=Nightly"],
                                 capture_output=True, text=True)
            killed = "Failed!" in run.stdout
            survived += 0 if killed else 1
            print(f"{name}: {'KILLED' if killed else 'SURVIVED'}", flush=True)
    finally:
        with open(CHECKER, "w", encoding="utf-8") as f:
            f.write(original)
    return 1 if survived else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
