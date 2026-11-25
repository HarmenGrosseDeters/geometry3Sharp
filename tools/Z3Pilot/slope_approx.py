"""Z3 lemma: the error that TerrainSlopeMap.prefilter_skips assumes for the corners of a face (mesh_ops/TerrainSlopeMap.cs,
e = 4e-12 (|x| + |y|) + the smallest double) covers what ExactSlopeKernel.Approx (math/ExactSlopeKernel.cs) accepts
(err <= 1e-12 (|x| + |y|)). docs/qa/exact-bound-inventory.md, gap G1: the prefilter culls faces without an exact
recheck; without the corner error a face smaller than e was skipped (the comment of prefilter_skips). The two constants
are in two files; this lemma couples them.

Approx: a vertex is the exact point (X*/W*, Y*/W*) with X*, Y*, W* inside the intervals X, Y, W (ExactInterval
enclosures). w = fl(0.5 fl(W.Lo + W.Hi)), x = fl(fl(0.5 fl(X.Lo + X.Hi)) / w) (y alike),
err = ((X.Hi - X.Lo) + (Y.Hi - Y.Lo)) / |w| + (|x| + |y|) (W.Hi - W.Lo) / |w|, accepted if err <= 1e-12 (|x| + |y|).
(A rounded vertex, v.rounded or Rounded, is within half an ulp per coordinate: far inside e.)

Rounding model: |fl(z) - z| <= u |fl(z)| + eta' for every operation (u = 2^-53; eta' = 2^-1074 covers the halving and
the quotient in the subnormal range); the computed err is at least (1 - 8 u) times its exact formula (seven roundings of
non-negative terms, rounded down at most u each), the computed acceptance threshold at most (1 + 2 u) times
1e-12 (|x| + |y|), and the consumer's e at least (1 - 3 u) 4e-12 (|x| + |y|) (two roundings and the max over the corners,
which is at least this corner's |x| + |y|). The quotients and products of the exact point are kept as products
(X* = x* W*): the queries are nonlinear real arithmetic, decided by Z3's nlsat.

Result (2026-11-25):
- FINDING (SAT, replayed with IEEE doubles): the claim accepted => |x - x*| + |y - y*| <= e fails at the subnormal edge.
  When the quotient underflows to x = y = 0, the acceptance err <= 1e-12 * 0 no longer bounds the width of W: with
  X = [2^-1074, 2^-1074], Y = [0, 0], W = [-8, 4] Approx returns (-0, -0) while the exact vertex for W* = 2^-1000 is
  x* = 2^-74. Z3 also finds witnesses with |x| + |y| > 0 in the subnormal range. Issue #1586; no library change
  here (a bugfix PR of its own). Not shown: that real ExactSlopeKernel line data produces such intervals. The report pins the finding as SAT while the code is unchanged.
- Proved (UNSAT, assumptions SAT): away from the edge (|x| + |y| > 0, no underflow) an accepted position has a W* of the
  sign of w, i.e. W is narrow. Controlled mutant: err without its W term (SAT, replayed).
- Undecided: the claim itself in the normal range (nlsat returns unknown after 300 s); not counted.
"""
import math
import re
import time
from fractions import Fraction

import z3 as z

import pilot

ROOT = pilot.ROOT
KERNEL = 'math/ExactSlopeKernel.cs'
MAP = 'mesh_ops/TerrainSlopeMap.cs'
U = Fraction(1, 2**53)
ETA = Fraction(1, 2**1074)

APPROX = """public Vector2d Approx(int vertex)
        {
            Vertex v = vertices[vertex];
            if (v.rounded) return new Vector2d(v.rx, v.ry);
            double w = 0.5 * (v.W.Lo + v.W.Hi);
            double x = 0.5 * (v.X.Lo + v.X.Hi) / w, y = 0.5 * (v.Y.Lo + v.Y.Hi) / w;
            // tight intervals only (nearly parallel lines have a wide W): else the rounded exact position
            double err = ((v.X.Hi - v.X.Lo) + (v.Y.Hi - v.Y.Lo)) / Math.Abs(w) + (Math.Abs(x) + Math.Abs(y)) * (v.W.Hi - v.W.Lo) / Math.Abs(w);
            if (MathUtil.IsFinite(x) && MathUtil.IsFinite(y) && err <= %s * (Math.Abs(x) + Math.Abs(y)))
                return new Vector2d(x, y);
            return Rounded(vertex);
        }"""

PREFILTER = """double e = 0;
            foreach (var q in pos) e = Math.Max(e, Math.Abs(q.x) + Math.Abs(q.y));
            e = %s * e + double.Epsilon;
            double dmin = poly_segment_distance(pos, A, B);
            double lowerF = zmin + slb * Math.Max(0, dmin - e);"""


def _normalize(s):
    return re.sub(r'\s+', '', s)


def _find(text, template, path):
    """the constant in the template's %s, from the source; raises if the code around it changed"""
    pre, post = (_normalize(p) for p in template.split('%s'))
    t = _normalize(text)
    i = t.find(pre)
    if i < 0:
        raise ValueError(f'{path}: the code changed, check it against tools/Z3Pilot/slope_approx.py')
    j = t.find(post, i + len(pre))
    if j < 0 or j - (i + len(pre)) > 30:
        raise ValueError(f'{path}: the code changed, check it against tools/Z3Pilot/slope_approx.py')
    return t[i + len(pre):j]


def source_contract(root=ROOT):
    accept = _find((root / KERNEL).read_text(encoding='utf-8'), APPROX, KERNEL)
    consume = _find((root / MAP).read_text(encoding='utf-8'), PREFILTER, MAP)
    return dict(accept=accept, consume=consume)


def _abs(x):
    return z.If(x >= 0, x, -x)


def _q(x):
    return z.RealVal(str(Fraction(x)))


def model(accept, consume, eta=None, w_term=True):
    u, eta = _q(U), _q(ETA if eta is None else eta)
    R = z.Real
    v = {n: R(n) for n in 'Xlo Xhi Ylo Yhi Wlo Whi Xs Ys Ws xs ys w x y xm ym Wsum Xsum Ysum errC'.split()}
    V = v.__getitem__
    c = []
    rnd = lambda F, f: c.append(_abs(F - f) <= u * _abs(F) + eta)
    c += [V('Xlo') <= V('Xs'), V('Xs') <= V('Xhi'), V('Ylo') <= V('Ys'), V('Ys') <= V('Yhi'),
          V('Wlo') <= V('Ws'), V('Ws') <= V('Whi'), V('Ws') != 0,
          V('xs') * V('Ws') == V('Xs'), V('ys') * V('Ws') == V('Ys')]
    # w = fl(0.5 fl(Wlo + Whi)), xm = fl(0.5 fl(Xlo + Xhi)), x = fl(xm / w) (as x w = xm within the rounding)
    rnd(V('Wsum'), V('Wlo') + V('Whi')); rnd(V('w'), V('Wsum') / 2)
    rnd(V('Xsum'), V('Xlo') + V('Xhi')); rnd(V('xm'), V('Xsum') / 2)
    rnd(V('Ysum'), V('Ylo') + V('Yhi')); rnd(V('ym'), V('Ysum') / 2)
    c.append(V('w') != 0)
    for q, qm in (('x', 'xm'), ('y', 'ym')):
        c.append(_abs(V(q) * V('w') - V(qm)) <= (u * _abs(V(q)) + eta) * _abs(V('w')))
    s = _abs(V('x')) + _abs(V('y'))
    # the computed err, at least (1 - 8 u) times its formula (written times |w|)
    formula_w = (V('Xhi') - V('Xlo')) + (V('Yhi') - V('Ylo')) + (s * (V('Whi') - V('Wlo')) if w_term else 0)
    c.append(V('errC') * _abs(V('w')) >= (1 - 8 * u) * formula_w)
    accepted = V('errC') <= (1 + 2 * u) * _q(float(accept)) * s
    e = (1 - 3 * u) * _q(float(consume)) * s + _q(ETA)     # double.Epsilon = 2^-1074 = ETA
    claim = _abs(V('x') - V('xs')) + _abs(V('y') - V('ys')) <= e
    return v, c, accepted, claim


def _check(formulas, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*formulas)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def _replay(m, v, formulas):
    vals = {}
    for n, var in v.items():
        val = m.eval(var, model_completion=True)
        if not z.is_rational_value(val):
            return False      # an algebraic number: not replayed (nlsat may answer with one)
        vals[n] = val.as_fraction()
    sub = [(var, z.RealVal(str(vals[n]))) for n, var in v.items()]
    return all(z.is_true(z.simplify(z.substitute(f, *sub))) for f in formulas)


def ieee_replay(accept, consume):
    """the finding with IEEE doubles: the C# expressions of Approx and prefilter_skips evaluated by Python (binary64,
    round to nearest, no FMA), the exact vertex in rationals. X and Y are point intervals, W is wide (contains 0)."""
    Xlo = Xhi = 2.0**-1074
    Ylo = Yhi = 0.0
    Wlo, Whi = -8.0, 4.0
    w = 0.5 * (Wlo + Whi)
    x = 0.5 * (Xlo + Xhi) / w
    y = 0.5 * (Ylo + Yhi) / w
    err = ((Xhi - Xlo) + (Yhi - Ylo)) / abs(w) + (abs(x) + abs(y)) * (Whi - Wlo) / abs(w)
    accepted = math.isfinite(x) and math.isfinite(y) and err <= float(accept) * (abs(x) + abs(y))
    e = float(consume) * max(0.0, abs(x) + abs(y)) + 5e-324
    Ws = Fraction(2)**-1000          # an exact W* inside W
    xs = Fraction(Xlo) / Ws
    miss = abs(Fraction(x) - xs) + abs(Fraction(y) - Fraction(Ylo) / Ws)
    return dict(X=[Xlo.hex(), Xhi.hex()], Y=[0.0, 0.0], W=[Wlo, Whi], w=w, x=x, y=y, err=err, accepted=accepted,
                e=e, exact_x=float(xs), replayed=accepted and miss > Fraction(e))


def run(timeout_ms=120000):
    c = source_contract()
    v, cons, accepted, claim = model(c['accept'], c['consume'])
    V = v.__getitem__
    s_ = _abs(V('x')) + _abs(V('y'))
    rows = []
    # W: away from the edge (|x| + |y| > 0, no underflow), acceptance forces W* to the sign of w: W is narrow
    v0, cons0, acc0, _ = model(c['accept'], c['consume'], eta=0)
    V0 = v0.__getitem__
    for name, pos in (('w_sign_normal_range_w_positive', True), ('w_sign_normal_range_w_negative', False)):
        hyp = cons0 + [acc0, _abs(V0('x')) + _abs(V0('y')) > 0, V0('w') > 0 if pos else V0('w') < 0]
        s, r, secs = _check(hyp + [V0('Ws') <= 0 if pos else V0('Ws') >= 0], timeout_ms)
        _, rv, _ = _check(hyp, timeout_ms)
        rows.append(dict(name=name, result=str(r), seconds=round(secs, 3), assumptions_result=str(rv)))
    # the finding: the claim fails at the edge (SAT); pinned while ExactSlopeKernel.Approx is unchanged
    s, r, secs = _check(cons + [accepted, z.Not(claim)], timeout_ms)
    finding = dict(name='accepted_corner_within_e', result=str(r), seconds=round(secs, 3),
                   z3_model_replayed=r == z.sat and _replay(s.model(), v, cons + [accepted, z.Not(claim)]),
                   ieee=ieee_replay(c['accept'], c['consume']))
    muts = []
    # W without the (|x| + |y|) (W.Hi - W.Lo) / |w| term of err: W may contain 0 (SAT, replayed)
    v1, cons1, acc1, _ = model(c['accept'], c['consume'], eta=0, w_term=False)
    V1 = v1.__getitem__
    f = cons1 + [acc1, _abs(V1('x')) + _abs(V1('y')) > 0, V1('w') > 0, V1('Ws') <= 0]
    s, r, secs = _check(f, timeout_ms)
    muts.append(dict(name='err_without_the_w_term', result=str(r), seconds=round(secs, 3),
                     replayed=r == z.sat and _replay(s.model(), v1, f)))
    passed = (all(x['result'] == 'unsat' and x['assumptions_result'] == 'sat' for x in rows)
              and all(m['result'] == 'sat' and m['replayed'] for m in muts)
              and finding['result'] == 'sat' and finding['ieee']['replayed'])
    return dict(z3=z.get_version_string(), contract=c, obligations=rows, finding=finding, mutants=muts, passed=passed,
                undecided='accepted => |x - x*| + |y - y*| <= e for |x| + |y| > 0 without underflow: Z3 4.15 nlsat '
                          'returns unknown after 300 s (also with the symmetry x, y >= 0, w, W* > 0 and the quotient '
                          'cleared); not counted as proved',
                total_seconds=round(sum(x['seconds'] for x in rows + muts) + finding['seconds'], 3),
                proof_scope='Proved: away from the subnormal edge an accepted Approx has a W of the sign of w (a narrow '
                            'W). Found: at the edge (x = y = 0 by underflow, or subnormal values) the corner error of '
                            'prefilter_skips does not hold; issue #1586.')


if __name__ == '__main__':
    import json
    report = run()
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report['passed'] else 1)
