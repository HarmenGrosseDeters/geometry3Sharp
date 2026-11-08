"""Z3 check of the Devillers-Guigue prefilter lemma (docs/design/TRITRI_PREFILTER.md, section 3).

Claim, over the reals: for a proper pair of triangles, normalized as in ExactMeshArrangement.apart_on_the_line
(p1 alone on its side of U's plane, p2 alone on its side of T's plane, q2/r2 and q1/r1 swapped so that p1 and p2 are on
the positive sides), the prefilter says apart, [p1, q1, p2, q2] > 0 or [p1, r1, r2, p2] > 0, iff the segments
T n plane(U) and U n plane(T) on the line of the two planes are disjoint.

Bridge (not checked by Z3, section 3 of the note): an affine map with positive determinant keeps every orientation
sign, maps segments to segments and keeps their order on the line; it maps (p2, q2, r2) to (0,0,0), (1,0,0),
(0,1,0) and p1 to a point with z > 0 (positive, as normalized). So U is that triangle without loss of generality,
and T's nine coordinates are free.

The segments are built from their definitions (edge parameters where an edge crosses the other plane, positions
along d = nT x nU), not from the prefilter's predicates. Every query must be UNSAT; the assumptions, and each side
of the prefilter's decision under them, must be SAT (no vacuous proof); mutants of the prefilter must be SAT.
Exact real arithmetic: the predicates of the code are exact (ExactPredicates.Orient3d on doubles), so the reals
model them without rounding.
"""
import json
import os
import sys

import z3


def vec(name):
    return [z3.Real(f'{name}_{c}') for c in 'xyz']


def sub(a, b):
    return [a[i] - b[i] for i in range(3)]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def orient(a, b, c, d):
    """ExactPredicates.Orient3d(a, b, c, d): ((b - a) x (c - a)) . (d - a)"""
    return dot(cross(sub(b, a), sub(c, a)), sub(d, a))


def lerp(a, b, t):
    return [a[i] + t * (b[i] - a[i]) for i in range(3)]


def model():
    p1, q1, r1 = vec('p1'), vec('q1'), vec('r1')
    zero = z3.RealVal(0)
    one = z3.RealVal(1)
    p2, q2, r2 = [zero, zero, zero], [one, zero, zero], [zero, one, zero]
    f = lambda x: orient(p1, q1, r1, x)          # side of x to T's oriented plane
    assumptions = [
        # T straddles U's plane z = 0 with p1 alone and positive (orient(p2, q2, r2, x) = x.z)
        orient(p2, q2, r2, p1) > 0, orient(p2, q2, r2, q1) < 0, orient(p2, q2, r2, r1) < 0,
        # U straddles T's plane with p2 alone and positive
        f(p2) > 0, f(q2) < 0, f(r2) < 0,
    ]
    # the ends of T n plane(U): on p1q1 (j) and p1r1 (i)
    sj, si, tl, tk = z3.Reals('sj si tl tk')
    assumptions += [sj * (p1[2] - q1[2]) == p1[2], si * (p1[2] - r1[2]) == p1[2]]
    j, i = lerp(p1, q1, sj), lerp(p1, r1, si)
    # the ends of U n plane(T): on p2q2 (l) and p2r2 (k); f is affine along an edge
    assumptions += [tl * (f(p2) - f(q2)) == f(p2), tk * (f(p2) - f(r2)) == f(p2)]
    l, k = lerp(p2, q2, tl), lerp(p2, r2, tk)
    d = cross(cross(sub(q1, p1), sub(r1, p1)), [zero, zero, one])     # direction of the line
    A1, A2, B1, B2 = dot(d, i), dot(d, j), dot(d, k), dot(d, l)
    overlap = z3.And(z3.Or(A1 <= B1, A1 <= B2, A2 <= B1, A2 <= B2), z3.Or(B1 <= A1, B1 <= A2, B2 <= A1, B2 <= A2))
    P = orient(p1, q1, p2, q2)
    Q = orient(p1, r1, r2, p2)
    ends = {'i': A1, 'j': A2, 'k': B1, 'l': B2}
    return assumptions, overlap, P, Q, ends


def check(formulas, timeout_ms):
    s = z3.Solver()
    s.set('timeout', timeout_ms)
    s.add(*formulas)
    r = s.check()
    return str(r)


def run(timeout_ms=120000):
    a, overlap, P, Q, e = model()
    apart = z3.Or(P > 0, Q > 0)
    queries = {
        # the ends along d: T n plane(U) = [i, j], U n plane(T) = [l, k]; each predicate compares one pair of ends
        'order_i_le_j': (a + [z3.Not(e['i'] <= e['j'])], 'unsat'),
        'order_l_le_k': (a + [z3.Not(e['l'] <= e['k'])], 'unsat'),
        'P_le_0_iff_l_le_j': (a + [z3.Not((P <= 0) == (e['l'] <= e['j']))], 'unsat'),
        'Q_le_0_iff_i_le_k': (a + [z3.Not((Q <= 0) == (e['i'] <= e['k']))], 'unsat'),
        # the lemma: both directions must be UNSAT
        'apart_but_overlap': (a + [apart, overlap], 'unsat'),
        'not_apart_but_disjoint': (a + [z3.Not(apart), z3.Not(overlap)], 'unsat'),
        # not vacuous: the assumptions and each decision are reachable
        'assumptions': (a, 'sat'),
        'assumptions_apart': (a + [apart], 'sat'),
        'assumptions_not_apart': (a + [z3.Not(apart)], 'sat'),
        'assumptions_touching': (a + [z3.Or(P == 0, Q == 0), z3.Not(apart)], 'sat'),
        # mutants must be caught (SAT counterexample)
        'mutant_ge': (a + [z3.Or(P >= 0, Q >= 0), overlap], 'sat'),
        'mutant_first_only': (a + [z3.Not(P > 0), z3.Not(overlap)], 'sat'),
        'mutant_second_only': (a + [z3.Not(Q > 0), z3.Not(overlap)], 'sat'),
        'mutant_swapped_sign': (a + [z3.Or(P < 0, Q < 0), overlap], 'sat'),
    }
    report = {}
    ok = True
    for name, (formulas, expected) in queries.items():
        got = check(formulas, timeout_ms)
        report[name] = {'expected': expected, 'got': got}
        ok = ok and got == expected
    return ok, report


if __name__ == '__main__':
    timeout = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
    ok, report = run(timeout)
    print(json.dumps(report, indent=1))
    out = os.environ.get('Z3_TRITRI_REPORT')
    if out:
        json.dump(report, open(out, 'w'), indent=1)
    sys.exit(0 if ok else 1)
