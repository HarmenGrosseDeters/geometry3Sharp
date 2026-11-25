"""Z3 lemma: the separation test of ContouringTopologyCertificate.may_meet_box (mesh_ops/ContouringTopologyCertificate.cs)
is sound. docs/qa/exact-bound-inventory.md, gap G3: the function culls a triangle without an exact recheck, so an `err`
that is too small drops a triangle that meets the cell box, and the certificate is wrong.

The test: with the computed normal n (any vector separates if the projections are apart, so n's own rounding does
not matter) the box's largest projection bmax and a triangle point's projection t are sums of three rounded products,
summed left to right; the triangle is dropped if fl(bmax + err) < fl(tmin - err) (or the mirrored test), with
err = fl(fl(12 u M) + 4 eta) and M the largest |rounded product| (12 u = 4 * Epsilon * 3 is exact).

Rounding model (binary64, round to nearest; Higham 2.2): a computed product P of the exact product p has
|P - p| <= u |P| + eta (eta = 2^-1075, the absolute error of a subnormal product), a computed sum or difference S of
exact operands x has |S - x| <= u |S| (exact where subnormal). The box's largest projection is the projection of one
corner (the rounding is monotone: max(fl(l), fl(h)) = fl(max(l, h))), and tmin <= every computed projection of the
triangle's points, so the lemma for one corner and one point covers the function. With |x| written as a case split the
queries are linear real arithmetic (u, eta constants): Z3 decides them exactly.

Obligations (UNSAT): `apart_above` (bmax + err < tmin - err => every exact box projection < every exact triangle
projection) and `apart_below` (the mirror). Their assumptions are SAT (vacuity). Controlled mutants (SAT, the model
replayed in exact rationals): err with 8 u M (the factor 1 instead of 3) and without the 4 eta term. The smallest
factor k of `k * 4 u M` that the lemma admits is reported (the margin).
"""
import re
import time
from fractions import Fraction

import z3 as z

import pilot

ROOT = pilot.ROOT
SOURCE = 'mesh_ops/ContouringTopologyCertificate.cs'
U = Fraction(1, 2**53)
ETA = Fraction(1, 2**1075)

CONTRACT = [
    'double ta = n.x * a.x + n.y * a.y + n.z * a.z, tb = n.x * b.x + n.y * b.y + n.z * b.z, tc = n.x * c.x + n.y * c.y + n.z * c.z;',
    'double tmin = Math.Min(ta, Math.Min(tb, tc)), tmax = Math.Max(ta, Math.Max(tb, tc));',
    'double bmin = 0, bmax = 0, M = 0;',
    # the model covers finite values only: the guards that keep the triangle otherwise are part of the contract
    'if (!(n.x == n.x && n.y == n.y && n.z == n.z) || double.IsInfinity(n.x) || double.IsInfinity(n.y) || double.IsInfinity(n.z))',
    'if (!(Math.Abs(tmin) <= double.MaxValue && Math.Abs(tmax) <= double.MaxValue && Math.Abs(bmin) <= double.MaxValue && Math.Abs(bmax) <= double.MaxValue))',
    'double l = n[k] * lo[k], h = n[k] * hi[k];',
    'bmin += Math.Min(l, h); bmax += Math.Max(l, h);',
    'M = Math.Max(M, Math.Max(Math.Abs(l), Math.Abs(h)));',
    'M = Math.Max(M, Math.Max(Math.Abs(n[k] * a[k]), Math.Max(Math.Abs(n[k] * b[k]), Math.Abs(n[k] * c[k]))));',
    'if (!(err <= double.MaxValue)) return true;',
    'return !(bmax + err < tmin - err || bmin - err > tmax + err);',
    'const double Epsilon = 1.1102230246251565e-16;     // u = 2^-53',
]


def _normalize(s):
    return re.sub(r'\s+', '', s)


def source_contract(root=ROOT):
    text = _normalize((root / SOURCE).read_text(encoding='utf-8'))
    start = text.index(_normalize('internal static bool may_meet_box('))
    body = text[start:start + 3000]
    missing = [line for line in CONTRACT if _normalize(line) not in (body if 'Epsilon =' not in line else text)]
    if missing:
        raise ValueError(f'{SOURCE}: may_meet_box changed, check it against tools/Z3Pilot/may_meet_box.py: {missing}')
    # the err line is read, not pinned: its factor and its eta units go into the lemma
    m = re.search(r'doubleerr=4\*Epsilon\*(\d+)\*M\+(\d+)\*double\.Epsilon;', body)
    if m is None:
        raise ValueError(f'{SOURCE}: the err of may_meet_box has a new form, check it against tools/Z3Pilot/may_meet_box.py')
    return dict(factor=int(m.group(1)), eta_units=int(m.group(2)))


def _abs(x):
    return z.If(x >= 0, x, -x)


def _q(x):
    return z.RealVal(str(x)) if isinstance(x, Fraction) else z.RealVal(x)


class Model:
    """the constraints of one comparison: a box corner's projection against a triangle point's (three products each)"""

    def __init__(self, factor, eta_units, mirrored):
        u, eta = _q(U), _q(ETA)
        self.v = {}
        R = lambda n: self.v.setdefault(n, z.Real(n))
        self.c = []
        rnd_prod = lambda P, p: self.c.append(_abs(P - p) <= u * _abs(P) + eta)
        rnd_sum = lambda S, x: self.c.append(_abs(S - x) <= u * _abs(S))
        M = R('M')
        sums = {}
        for side in 'bt':        # b: the box corner, t: the triangle point
            P = [R(f'{side}P{k}') for k in range(3)]
            p = [R(f'{side}p{k}') for k in range(3)]
            for k in range(3):
                rnd_prod(P[k], p[k])
                self.c.append(_abs(P[k]) <= M)
            S2, S3 = R(f'{side}S2'), R(f'{side}S3')
            rnd_sum(S2, P[0] + P[1])
            rnd_sum(S3, S2 + P[2])
            sums[side] = (S3, p[0] + p[1] + p[2])
        E1, err = R('E1'), R('err')
        rnd_prod(E1, 12 * u * M)                        # 4 * Epsilon * 3 = 12 u exactly, then times M
        if factor != 3:
            self.c[-1] = _abs(E1 - 4 * factor * u * M) <= u * _abs(E1) + eta
        rnd_sum(err, E1 + eta_units * 2 * eta)          # double.Epsilon = 2^-1074 = 2 eta
        X, Y = R('X'), R('Y')
        (Bc, Bx), (Tc, Tx) = sums['b'], sums['t']
        if not mirrored:   # bmax + err < tmin - err
            rnd_sum(X, Bc + err)
            rnd_sum(Y, Tc - err)
            self.decides = X < Y
            self.claim = Bx < Tx
        else:              # bmin - err > tmax + err
            rnd_sum(X, Bc - err)
            rnd_sum(Y, Tc + err)
            self.decides = X > Y
            self.claim = Bx > Tx


def _check(formulas, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*formulas)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def _replay(model, m):
    """the Z3 model in exact rationals: every constraint and the decision hold and the claim fails"""
    vals = {}
    for n, var in m.v.items():
        val = model.eval(var, model_completion=True)
        if not z.is_rational_value(val):
            return False
        vals[n] = val.as_fraction()
    sub = [(var, z.RealVal(str(vals[n]))) for n, var in m.v.items()]
    ok = all(z.is_true(z.simplify(z.substitute(c, *sub))) for c in m.c)
    return ok and z.is_true(z.simplify(z.substitute(m.decides, *sub))) and z.is_false(z.simplify(z.substitute(m.claim, *sub)))


def run(timeout_ms=120000):
    c = source_contract()
    rows, muts = [], []
    for mirrored, name in [(False, 'apart_above'), (True, 'apart_below')]:
        m = Model(c['factor'], c['eta_units'], mirrored)
        s, r, secs = _check(m.c + [m.decides, z.Not(m.claim)], timeout_ms)
        _, rv, _ = _check(m.c + [m.decides], timeout_ms)
        rows.append(dict(name=name, result=str(r), seconds=round(secs, 3), assumptions_result=str(rv)))
    for name, factor, eta_units in [('factor_1_instead_of_3', 1, c['eta_units']), ('no_eta_term', c['factor'], 0)]:
        m = Model(factor, eta_units, False)
        s, r, secs = _check(m.c + [m.decides, z.Not(m.claim)], timeout_ms)
        muts.append(dict(name=name, result=str(r), seconds=round(secs, 3), replayed=r == z.sat and _replay(s.model(), m)))
    # the margin: the smallest factor k (of k * 4 u M) for which both obligations hold
    smallest = None
    for k in (1, 2, 3):
        if all(str(_check(Model(k, c['eta_units'], mir).c + [Model(k, c['eta_units'], mir).decides,
                                                            z.Not(Model(k, c['eta_units'], mir).claim)], timeout_ms)[1]) == 'unsat'
               for mir in (False, True)):
            smallest = k
            break
    passed = (all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' for r in rows)
              and all(x['result'] == 'sat' and x['replayed'] for x in muts))
    return dict(z3=z.get_version_string(), contract=c, obligations=rows, mutants=muts, smallest_factor=smallest,
                passed=passed, total_seconds=round(sum(r['seconds'] for r in rows + muts), 3),
                proof_scope='The separation test is sound under the rounding model of the docstring (half an ulp of '
                            'the result, eta for products), for finite values (the function keeps the triangle '
                            'otherwise); the choice of the corner and of tmin is the monotonicity argument stated there.')


if __name__ == '__main__':
    import json
    report = run()
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report['passed'] else 1)
