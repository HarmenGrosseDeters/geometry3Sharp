"""Z3 lemmas: the error that TerrainSlopeMap.prefilter_skips assumes for the corners of a face (mesh_ops/TerrainSlopeMap.cs,
e = 4e-12 (|x| + |y|) + the smallest double) covers what ExactSlopeKernel.approx_position (math/ExactSlopeKernel.cs)
accepts. docs/qa/exact-bound-inventory.md, gap G1: the prefilter culls faces without an exact recheck; without the corner
error a face smaller than e was skipped (the comment of prefilter_skips). The constants are in two files; this couples
them.

History: until issue #1586 Approx accepted when err <= 1e-12 (|x| + |y|) alone. That was wrong at the subnormal edge
(FINDING of 2026-11-25, kept below as the replay `issue_1586`): when the quotient underflows to x = y = 0 the acceptance
no longer bounds the width of W; with X = [2^-1074, 2^-1074], Y = [0, 0], W = [-8, 4] Approx returned (-0, -0) while
the exact vertex for W* = 2^-1000 is x* = 2^-74. The fix adds two conditions, and each alone is not enough (the two
replays `width_only` and `guard_only`):
- the guard |x| + |y| >= 2^-1022: without it, X = Y = [2^-1073, 3 2^-1074], W = [4, 4] has x = y = 0 accepted and
  |x*| + |y*| = 1.5 2^-1074 > e (the absolute rounding of the quotient, the halving and the threshold);
- the narrow W, (W.Hi - W.Lo) 5e11 <= |w|: without it, X = [2^-1074, 2^-1074], Y = [0, 0], W = 2^-52 [0.875, 1.125]
  has x = 2^-1022 (normal) accepted with err = 0, because s (W.Hi - W.Lo) underflows to 0; x* is 14 % off.
The exact origin (X = Y = [0, 0]) stays accepted: then x* = y* = 0 = |x|, |y|. The code also rejects an |x| + |y| that
overflows (then err <= 1e-12 inf holds for any widths; replay `overflow`): the proof is for finite values.

Proof (for |x| + |y| >= 2^-1022 and both new conditions). Rounding model (Higham 2002, 2.1, with gradual underflow):
a sum or difference of doubles is fl(z) = z (1 + d), |d| <= u = 2^-53 (exact when subnormal); a product or quotient is
fl(z) = z (1 + d) + n, |d| <= u, |n| <= eta = 2^-1075; rounding is monotone. Step 0 (monotonicity, the lemma
`midpoint_in_interval` with Z3's floating-point theory, `run(fp=True)`, 89 s): w = fl(0.5 fl(W.Lo + W.Hi)) lies in W (if
the sum does not overflow; an overflow gives w = inf, x = y = 0, rejected), and alike xm in X, ym in Y (the lemma is for
any doubles a <= b), so |W* - w| <= dW, |X* - xm| <= dX and |Y* - ym| <= dY for the exact widths dX, dY, dW. The guard
tests the computed fl(|x| + |y|) >= 2^-1022; the lemmas assume the real |x| + |y| >= 2^-1022: the same, because a real
sum below 2^-1022 has two subnormal terms, whose sum is exact. Then, normalized (delta = dW / |w|,
a = (dX + dY) / |w|), one lemma per step, linear except L3:
- L1 width: (W.Hi - W.Lo) 5e11 <= |w| computed gives delta <= D = 1 / ((1 - u) (5e11 (1 - u) - 1/2)) ~ 2e-12 (a
  difference of doubles is 0 or at least 2^-1074, so the product's absolute rounding is at most half of it).
- L2 width of X, Y: err <= 1e-12 (|x| + |y|) computed gives a <= A s, A = (c (1 + u)^2 + 2 u) / (1 - u)^4 ~ 1e-12, with
  c the double 1e-12 and s = |x| + |y| >= 2^-1022 (so eta <= u s): only the X, Y term of err is used.
- L3 one coordinate (nlsat): with t = W* / w in [1 - delta, 1 + delta], e' = (X* - xm) / w, xi = xm / w and
  k = X* / W* = (xi + e') / t: |xi - k| (1 - D) <= |xi| D + dX / |w|.
- L4 the claim: |x - xi| <= u |xi| + eta per coordinate, L3 for both, a <= A s, and the consumer's e at least
  (1 - u)^3 C4 s (C4 the double 4e-12; the max over the corners is at least this corner's fl(|x| + |y|)) give
  |x - x*| + |y - y*| <= e.
Also kept from #1582 (its scope: no underflow): an accepted position has a W* of the sign of w (UNSAT, both signs), and
its mutant err without the W term (SAT, replayed).
Controlled mutants (SAT): L4 without the guard s >= 2^-1022, L4 with D twice as large (the width condition with 2.5e11), and
the consumer with 3e-12 instead of 4e-12 (the bound is used to about 3e-12 (|x| + |y|)).

Not covered here: that real ExactSlopeKernel line data produces such intervals (the replays use Approx's inputs
directly, as the .NET test ExactSlopeKernelTests.ApproxPosition_* does).
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
ETA = Fraction(1, 2**1075)           # half the smallest double: the absolute rounding of a subnormal product or quotient
TINY = Fraction(1, 2**1074)          # double.Epsilon
MIN_NORMAL = Fraction(1, 2**1022)

APPROX = """internal static bool approx_position(ExactInterval X, ExactInterval Y, ExactInterval W, out double x, out double y)
        {
            double w = 0.5 * (W.Lo + W.Hi);
            x = 0.5 * (X.Lo + X.Hi) / w; y = 0.5 * (Y.Lo + Y.Hi) / w;
            double s = Math.Abs(x) + Math.Abs(y);
            double err = ((X.Hi - X.Lo) + (Y.Hi - Y.Lo)) / Math.Abs(w) + s * (W.Hi - W.Lo) / Math.Abs(w);
            if (MathUtil.IsFinite(x) && MathUtil.IsFinite(y) && err <= %s * (Math.Abs(x) + Math.Abs(y))) {
                if (X.Lo == 0 && X.Hi == 0 && Y.Lo == 0 && Y.Hi == 0)
                    return true;
                if (s >= MinNormal && s <= double.MaxValue && (W.Hi - W.Lo) * %s <= Math.Abs(w))
                    return true;
            }
            return false;
        }"""

MIN_NORMAL_DECL = 'internal const double MinNormal = %s;'

PREFILTER = """double e = 0;
            foreach (var q in pos) e = Math.Max(e, Math.Abs(q.x) + Math.Abs(q.y));
            e = %s * e + double.Epsilon;
            double dmin = poly_segment_distance(pos, A, B);
            double lowerF = zmin + slb * Math.Max(0, dmin - e);"""


def _normalize(s):
    return re.sub(r'\s+', '', s)


def _find(text, template, path):
    """the constants in the template's %s, from the source; raises if the code around them changed"""
    parts = [_normalize(p) for p in template.split('%s')]
    t = _normalize(text)
    i = t.find(parts[0])
    if i < 0:
        raise ValueError(f'{path}: the code changed, check it against tools/Z3Pilot/slope_approx.py')
    i += len(parts[0])
    found = []
    for post in parts[1:]:
        j = t.find(post, i)
        if j < 0 or j - i > 30:
            raise ValueError(f'{path}: the code changed, check it against tools/Z3Pilot/slope_approx.py')
        found.append(t[i:j])
        i = j + len(post)
    return found


def source_contract(root=ROOT):
    kernel = (root / KERNEL).read_text(encoding='utf-8')
    accept, width = _find(kernel, APPROX, KERNEL)
    min_normal, = _find(kernel, MIN_NORMAL_DECL, KERNEL)
    if Fraction(float(min_normal)) != MIN_NORMAL:
        raise ValueError(f'{KERNEL}: MinNormal is not 2^-1022')
    consume, = _find((root / MAP).read_text(encoding='utf-8'), PREFILTER, MAP)
    return dict(accept=accept, width=width, consume=consume)


def _abs(x):
    return z.If(x >= 0, x, -x)


def _q(x):
    return z.RealVal(str(Fraction(x)))


def width_bound(width):
    """D of L1: the computed (W.Hi - W.Lo) * width <= |w| gives dW <= D |w|"""
    k = Fraction(float(width))
    return 1 / ((1 - U) * (k * (1 - U) - Fraction(1, 2)))


def xy_bound(accept):
    """A of L2: the computed err <= accept (|x| + |y|) gives (dX + dY) / |w| <= A (|x| + |y|)"""
    c = Fraction(float(accept))
    return (c * (1 + U)**2 + 2 * U) / (1 - U)**4


def _check(cons, neg, timeout_ms):
    """UNSAT of cons + [neg] proves cons => not neg; cons alone must be SAT (no vacuous proof)"""
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*cons)
    t0 = time.monotonic()
    ra = s.check()
    s.add(neg)
    r = s.check()
    return str(r), str(ra), round(time.monotonic() - t0, 3)


def lemma_width(width, timeout_ms):
    R = z.Real
    dW, dWc, p, aw = R('dW'), R('dWc'), R('p'), R('aw')
    u = _q(U)
    cons = [dW >= 0, aw >= _q(TINY),                                   # |w| a nonzero double
            dWc >= (1 - u) * dW, dWc >= 0, z.Or(dWc == 0, dWc >= _q(TINY)),   # dWc = fl(W.Hi - W.Lo), a double
            p >= (1 - u) * _q(Fraction(float(width))) * dWc - _q(ETA),     # p = fl(dWc * width)
            p <= aw]                                                    # the condition
    return _check(cons, dW > _q(width_bound(width)) * aw, timeout_ms)


def lemma_xy(accept, timeout_ms):
    """all values divided by |w| (nX = dX / |w|, ...): a rounding of a non-negative value is at least (1 - u) z - eta"""
    R = z.Real
    nX, nY, nXc, nYc, nS, qp, q, err, T, sc, s = (R(n) for n in 'nX nY nXc nYc nS qp q err T sc s'.split())
    u, eta = _q(U), _q(ETA)
    cons = [nX >= 0, nY >= 0, nXc >= (1 - u) * nX, nYc >= (1 - u) * nY,      # fl(X.Hi - X.Lo), fl(Y.Hi - Y.Lo)
            nS >= (1 - u) * (nXc + nYc),                                     # their sum
            qp >= (1 - u) * nS - eta,                                        # / |w|
            q >= 0,                                                          # the W term of err
            err >= (1 - u) * (qp + q), err <= T,                             # err, accepted
            T <= (1 + u) * _q(Fraction(float(accept))) * sc + eta,           # T = fl(c fl(|x| + |y|))
            sc <= (1 + u) * s, s >= _q(MIN_NORMAL)]                          # the guard
    return _check(cons, nX + nY > _q(xy_bound(accept)) * s, timeout_ms)


def lemma_coordinate(D, timeout_ms):
    R = z.Real
    xi, k, t, ep, nx, d = (R(n) for n in 'xi k t ep nx d'.split())
    cons = [d >= 0, d <= _q(D), t >= 1 - d, t <= 1 + d, _abs(ep) <= nx, k * t == xi + ep]
    return _check(cons, _abs(xi - k) * (1 - _q(D)) > _abs(xi) * _q(D) + nx, timeout_ms)


def lemma_claim(D, A, consume, timeout_ms, guard=True):
    R = z.Real
    x, y, xx, xy, kx, ky, nx, ny, e, emax, t1, sc = (R(n) for n in 'x y xx xy kx ky nx ny e emax t1 sc'.split())
    u, eta = _q(U), _q(ETA)
    s = _abs(x) + _abs(y)
    cons = [nx >= 0, ny >= 0, nx + ny <= _q(A) * s,                         # L2 (its eta <= u s needs the guard)
            _abs(x - xx) <= u * _abs(xx) + eta, _abs(y - xy) <= u * _abs(xy) + eta,   # x = fl(xm / w)
            _abs(xx - kx) * (1 - _q(D)) <= _abs(xx) * _q(D) + nx,           # L3
            _abs(xy - ky) * (1 - _q(D)) <= _abs(xy) * _q(D) + ny,
            sc >= (1 - u) * s, emax >= sc,                                   # the consumer's e
            t1 >= (1 - u) * _q(Fraction(float(consume))) * emax - eta, e >= (1 - u) * (t1 + _q(TINY))]
    if guard:
        cons.append(s >= _q(MIN_NORMAL))
    return _check(cons, _abs(x - kx) + _abs(y - ky) > e, timeout_ms)


def lemma_midpoint_fp(timeout_ms):
    """step 0 with IEEE binary64 (Z3 FloatingPoint, round to nearest even): W.Lo <= fl(0.5 fl(W.Lo + W.Hi)) <= W.Hi"""
    F, RN = z.Float64(), z.RNE()
    a, b = z.FP('a', F), z.FP('b', F)
    S = z.fpAdd(RN, a, b)
    m = z.fpMul(RN, z.FPVal(0.5, F), S)
    cons = [z.fpLEQ(a, b), z.Not(z.fpIsInf(a)), z.Not(z.fpIsInf(b)), z.Not(z.fpIsInf(S))]
    return _check(cons, z.Not(z.And(z.fpLEQ(a, m), z.fpLEQ(m, b))), timeout_ms)


# ---- from #1582: the W-sign lemmas away from the edge (no underflow, eta = 0) and their mutant. They model only the
# err acceptance (the guard and the width condition only make it stronger, so the UNSAT results carry over). Their
# rounding model of err ((1 - 8 u) times the formula) is valid without underflow only: that is their scope ----

def legacy_model(accept, consume, eta=None, w_term=True):
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



def _check_formulas(formulas, timeout_ms):
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



def legacy_obligations(accept, consume, timeout_ms):
    """away from the edge (|x| + |y| > 0, no underflow) acceptance forces W* to the sign of w (UNSAT, both signs);
    mutant: err without its W term lets W contain 0 (SAT, replayed)"""
    rows, muts = [], []
    v0, cons0, acc0, _ = legacy_model(accept, consume, eta=0)
    V0 = v0.__getitem__
    for name, pos in (('w_sign_normal_range_w_positive', True), ('w_sign_normal_range_w_negative', False)):
        hyp = cons0 + [acc0, _abs(V0('x')) + _abs(V0('y')) > 0, V0('w') > 0 if pos else V0('w') < 0]
        _, r, secs = _check_formulas(hyp + [V0('Ws') <= 0 if pos else V0('Ws') >= 0], timeout_ms)
        _, rv, _ = _check_formulas(hyp, timeout_ms)
        rows.append(dict(name=name, result=str(r), seconds=round(secs, 3), assumptions_result=str(rv)))
    v1, cons1, acc1, _ = legacy_model(accept, consume, eta=0, w_term=False)
    V1 = v1.__getitem__
    f = cons1 + [acc1, _abs(V1('x')) + _abs(V1('y')) > 0, V1('w') > 0, V1('Ws') <= 0]
    s, r, secs = _check_formulas(f, timeout_ms)
    muts.append(dict(name='err_without_the_w_term', result=str(r), seconds=round(secs, 3),
                     replayed=r == z.sat and _replay(s.model(), v1, f)))
    return rows, muts


# ---- the C# expressions in binary64 (Python floats: round to nearest, no FMA), the exact vertex in rationals ----

def approx_position(X, Y, W, accept, width, guard=True, narrow=True):
    w = 0.5 * (W[0] + W[1])
    try:
        x = 0.5 * (X[0] + X[1]) / w
        y = 0.5 * (Y[0] + Y[1]) / w
    except ZeroDivisionError:
        return False, 0.0, 0.0
    s = abs(x) + abs(y)
    err = ((X[1] - X[0]) + (Y[1] - Y[0])) / abs(w) + s * (W[1] - W[0]) / abs(w)
    if math.isfinite(x) and math.isfinite(y) and err <= float(accept) * (abs(x) + abs(y)):
        if X[0] == 0 and X[1] == 0 and Y[0] == 0 and Y[1] == 0:
            return True, x, y
        if (not guard or 2.0**-1022 <= s <= 1.7976931348623157e308) and (not narrow or (W[1] - W[0]) * float(width) <= abs(w)):
            return True, x, y
    return False, x, y


def prefilter_e(consume, x, y):
    return float(consume) * max(0.0, abs(x) + abs(y)) + 5e-324


T1074 = 2.0**-1074
# name: X, Y, W, an exact (X*, Y*, W*) inside them, the variant of approx_position that accepted it before the fix
REPLAYS = {
    'issue_1586': ((T1074, T1074), (0.0, 0.0), (-8.0, 4.0), (T1074, 0.0, 2.0**-1000), dict(guard=False, narrow=False)),
    'width_only': ((2 * T1074, 3 * T1074), (2 * T1074, 3 * T1074), (4.0, 4.0), (3 * T1074, 3 * T1074, 4.0),
                   dict(guard=False)),
    'overflow': ((0.5e308, 0.85e308), (0.45e308, 0.45e308), (0.5, 0.5 + 2.0**-53), (0.85e308, 0.45e308, 0.5),
                 dict(guard=False, narrow=False)),
    'guard_only': ((T1074, T1074), (0.0, 0.0), (0.875 * 2.0**-52, 1.125 * 2.0**-52), (T1074, 0.0, 0.875 * 2.0**-52),
                   dict(narrow=False)),
}


def ieee_replay(name, accept, width, consume):
    X, Y, W, (Xs, Ys, Ws), variant = REPLAYS[name]
    assert X[0] <= Xs <= X[1] and Y[0] <= Ys <= Y[1] and W[0] <= Ws <= W[1] and Ws != 0
    acc_v, xv, yv = approx_position(X, Y, W, accept, width, **variant)
    miss = abs(Fraction(xv) - Fraction(Xs) / Fraction(Ws)) + abs(Fraction(yv) - Fraction(Ys) / Fraction(Ws))
    acc_fixed, _, _ = approx_position(X, Y, W, accept, width)
    e = prefilter_e(consume, xv, yv)
    if math.isinf(e):
        # (|x| + |y| overflowed: e is infinite; measured against 4e-12 times the exact position's size instead)
        exact = Fraction(Xs) / Fraction(Ws), Fraction(Ys) / Fraction(Ws)
        e_ref = Fraction(float(consume)) * (abs(exact[0]) + abs(exact[1])) + TINY
    else:
        e_ref = Fraction(e)
    return dict(variant_accepts=acc_v, miss_over_e=float(miss / e_ref), variant_wrong=acc_v and miss > e_ref,
                fixed_accepts=acc_fixed)


def run(timeout_ms=120000, fp=False):
    c = source_contract()
    D, A = width_bound(c['width']), xy_bound(c['accept'])
    obligations = []
    for name, (r, ra, secs) in (('L1_width', lemma_width(c['width'], timeout_ms)),
                                ('L2_xy_width', lemma_xy(c['accept'], timeout_ms)),
                                ('L3_coordinate', lemma_coordinate(D, timeout_ms)),
                                ('L4_claim', lemma_claim(D, A, c['consume'], timeout_ms))):
        obligations.append(dict(name=name, result=r, assumptions_result=ra, seconds=secs))
    if fp:
        r, ra, secs = lemma_midpoint_fp(max(timeout_ms, 300000))
        obligations.append(dict(name='L0_midpoint_in_interval', result=r, assumptions_result=ra, seconds=secs))
    mutants = []
    for name, (r, ra, secs) in (('L4_without_the_guard', lemma_claim(D, A, c['consume'], timeout_ms, guard=False)),
                                ('L4_width_condition_2.5e11', lemma_claim(2 * D, A, c['consume'], timeout_ms)),
                                ('L4_consumer_3e-12', lemma_claim(D, A, '3e-12', timeout_ms))):
        mutants.append(dict(name=name, result=r, seconds=secs))
    rows, muts = legacy_obligations(c['accept'], c['consume'], timeout_ms)
    obligations += rows
    mutants += [dict(name=m['name'], result=m['result'], seconds=m['seconds']) for m in muts]
    replays = {n: ieee_replay(n, c['accept'], c['width'], c['consume']) for n in REPLAYS}
    passed = (all(o['result'] == 'unsat' and o['assumptions_result'] == 'sat' for o in obligations)
              and all(m['result'] == 'sat' for m in mutants) and all(m['replayed'] for m in muts)
              and all(r['variant_wrong'] and not r['fixed_accepts'] for r in replays.values()))
    return dict(z3=z.get_version_string(), contract=c, D=float(D), A=float(A), obligations=obligations,
                mutants=mutants, replays=replays, passed=passed,
                total_seconds=round(sum(x['seconds'] for x in obligations + mutants), 3),
                proof_scope='Proved: an accepted approx_position (|x| + |y| >= 2^-1022, a narrow W) is within the '
                            'corner error e of prefilter_skips (L1-L4; L0, the midpoint in its interval, with '
                            'run(fp=True), else by the monotonicity of rounding). The exact origin is exact. '
                            'Replayed: the finding of issue #1586, the two half fixes and an overflowed |x| + |y|, all rejected by the code.')


if __name__ == '__main__':
    import json
    import sys
    report = run(fp='--fp' in sys.argv)
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report['passed'] else 1)
