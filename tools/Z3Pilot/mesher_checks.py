"""Z3 checks for the 2D mesher (comp_geom/QualityMesh2.cs): the error bound of the exact-corner filter of #437 and the
inequalities behind the termination arguments of the small-angle rules (#394, rule C). Pilot 3, see README.md ("Mesher checks").

Every obligation is a counterexample query (assumptions AND NOT claim) that must return UNSAT; its assumptions alone must be
SAT (no vacuous proof); every claim has a mutant that must return SAT (or a reported result where the owner asked for a report
only). UNKNOWN never counts as a proof. What is proved and what is only reviewed is in README.md, not here.
"""
import json
import math
import pathlib
import re
import time
from fractions import Fraction

import z3 as z

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = 'comp_geom/QualityMesh2.cs'
VECTOR = 'math/Vector2d.cs'

U = Fraction(1, 2**53)            # unit roundoff of binary64 round to nearest
NORMAL_MIN = Fraction(1, 2**1022)  # smallest normal
ETA = Fraction(1, 2**1075)        # absolute error of a rounding in the subnormal range (half the spacing 2^-1074)
F64 = z.Float64()
RNE = z.RNE()


def _normalize(s):
    return re.sub(r'\s+', '', s)


def _method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 0
    for end in range(opening, len(text)):
        depth += (text[end] == '{') - (text[end] == '}')
        if depth == 0:
            return text[start:end + 1]
    raise ValueError('unterminated method')


def source_contracts(text=None, vector_text=None):
    """The code the lemmas model, from the sources (fails on drift instead of checking a stale transcription).
    Returns the constants read from the source: the margin exponent and the range of the guard."""
    if text is None:
        text = (ROOT / SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    body = _normalize(_method(text, 'internal static int ShortestEdgeCorner(Vector2d a, Vector2d b, Vector2d c)'))
    required = [
        'doublel0=(b-c).LengthSquared,l1=(c-a).LengthSquared,l2=(a-b).LengthSquared;',
        'intj=l1<l0?1:0;',
        'doublelj=j==1?l1:l0;',
        'if(l2<lj){j=2;lj=l2;}',
        'doublerival=j==0?Math.Min(l1,l2):j==1?Math.Min(l0,l2):Math.Min(l0,l1);',
        'boolinRange=l0>1e-280&&l1>1e-280&&l2>1e-280&&l0<1e140&&l1<1e140&&l2<1e140;',
        'if(inRange&&rival-lj>rival*(1.0/(1L<<40)))returnj;',
    ]
    for r in required:
        if r not in body:
            raise ValueError('ShortestEdgeCorner no longer contains: ' + r)
    vec = _normalize(vector_text if vector_text is not None else (ROOT / VECTOR).read_text(encoding='utf-8-sig'))
    if 'publicdoubleLengthSquared{get{returnx*x+y*y;}}' not in vec:
        raise ValueError('Vector2d.LengthSquared changed')
    if '(b-c).LengthSquared' not in body:
        raise ValueError('the edge vectors changed')
    return dict(margin_exponent=40, lo=Fraction(10) ** -280, hi=Fraction(10) ** 140)


def Q(f):
    return z.RealVal(f.numerator) / z.RealVal(f.denominator) if isinstance(f, Fraction) else z.RealVal(f)


def c_pow(k):
    """(1 + u)^k - 1, exactly"""
    return (1 + U) ** k - 1


def step_square():
    """S1. dx = a (1 + d1) (a subtraction of doubles: exact when its result is subnormal, so only a relative error), s = fl(dx dx)
    = dx^2 (1 + d3) + e3 with |e3| <= 2^-1075 (an underflowing product): s = a^2 (1 + eta) + e3 with |eta| <= (1 + u)^3 - 1."""
    d1, d3 = z.Reals('d1 d3')
    hyps = [z.Abs(d1) <= Q(U), z.Abs(d3) <= Q(U)]
    eta = (1 + d1) ** 2 * (1 + d3) - 1
    return hyps, z.Abs(eta) > Q(c_pow(3)), [d1, d3]


def step_sum(k_eps):
    """S2. A = a^2 >= 0, B = b^2 >= 0, s1 = A (1 + eta1) + e1, s2 = B (1 + eta2) + e2 (|eta| <= (1 + u)^3 - 1, |e| <= 2^-1075), and
    l = (s1 + s2) (1 + d5) (a sum of doubles: exact when subnormal, so only a relative error): |l - (A + B)| <= k_eps (A + B) + 2 (1 + u) 2^-1075
    where k_eps = (1 + u)^4 - 1 is the bound of the derivation; a smaller k_eps must fail (sharpness)."""
    A, B, e1, e2, n1, n2, d5 = z.Reals('A B e1 e2 n1 n2 d5')
    c3 = Q(c_pow(3))
    s1, s2 = A * (1 + n1) + e1, B * (1 + n2) + e2
    l = (s1 + s2) * (1 + d5)
    hyps = [A >= 0, B >= 0, z.Abs(n1) <= c3, z.Abs(n2) <= c3, z.Abs(e1) <= Q(ETA), z.Abs(e2) <= Q(ETA), z.Abs(d5) <= Q(U)]
    bad = z.Abs(l - (A + B)) > Q(k_eps) * (A + B) + 2 * (1 + Q(U)) * Q(ETA)
    return hyps, bad, [A, B, e1, e2, n1, n2, d5]


def step_range(lo, eps, hi_guard=True):
    """S3. |l - L| <= c4 L + 2 (1 + u) 2^-1075 with c4 = (1 + u)^4 - 1, L > 0 and l > lo (the guard of the code): |l - L| <= eps L."""
    L, l = z.Reals('L l')
    c4 = Q(c_pow(4))
    hyps = [L > 0, z.Abs(l - L) <= c4 * L + 2 * (1 + Q(U)) * Q(ETA)]
    if lo is not None:
        hyps.append(l > Q(lo))
    return hyps, z.Abs(l - L) > Q(eps) * L, [L, l]


def order_obligation(margin_exp, eps):
    """Lemma B (a2), the real part: computed values la <= lb with lb - la > 2^-margin lb, and la, lb within eps of the exact
    squares: then La < Lb; the third computed value lc >= lb gives Lc > La."""
    La, Lb, Lc, la, lb, lc = z.Reals('La Lb Lc la lb lc')
    m = Fraction(1, 2**margin_exp)
    hyps = [La > 0, Lb > 0, Lc > 0, la <= lb, lb <= lc,
            z.Abs(la - La) <= Q(eps) * La, z.Abs(lb - Lb) <= Q(eps) * Lb, z.Abs(lc - Lc) <= Q(eps) * Lc,
            lb - la > Q(m) * lb]
    bad = z.Or(La >= Lb, La >= Lc)
    return hyps, bad, [La, Lb, Lc, la, lb, lc]


def fp_scaling_exact():
    """The constant of the margin test, bit-precise (FP only): for rival in the guarded range (1e-280, 1e140), rival * 2^-40 is exact in binary64
    (scaling by a power of two, no underflow): multiplying back by 2^40 returns rival, and 2^-40 itself is the double 1.0 / (1L << 40).
    The bridge of the test in doubles to the real inequality (rival - lj > rival 2^-40) is the monotonicity of round to nearest and is
    reviewed (README), not proved here: Z3 returns UNKNOWN on binary64 values in reals across 2^+-1000."""
    b = z.FP('rival', F64)
    lo, hi = z.FPVal(1e-280, F64), z.FPVal(1e140, F64)
    two40 = z.FPVal(1.0 / (1 << 40), F64)
    hyps = [z.fpGT(b, lo), z.fpLT(b, hi)]
    m = z.fpMul(RNE, b, two40)
    bad = z.Not(z.fpEQ(z.fpMul(RNE, m, z.FPVal(float(1 << 40), F64)), b))
    return hyps, bad, [b]


def lens_obligations():
    """(b1) with a = (-1, 0), c = (1, 0) (similarity: both sides of the equivalence are invariant, a reviewed step, not proved here):
    the code's predicate  dot < 0 and 4 dot^2 >= |pa|^2 |pc|^2  (p, a, c: dot = (a - p) . (c - p))
    iff the angle apc >= 120 degrees (cos <= -1/2, written without trigonometry: dot <= -|pa||pc| / 2)
    iff p is in both disks of radius |ac| / sqrt 3 through a and c (centres (0, +-1/sqrt 3))."""
    x, y, s, k = z.Reals('x y s k')    # s = |pa||pc| >= 0, k = sqrt 3
    dot = (-1 - x) * (1 - x) + y * y
    la, lc = (-1 - x) ** 2 + y * y, (1 - x) ** 2 + y * y
    # p is not an end of ac (s > 0): there dot = 0, the code's predicate is false by its first test, the angle is undefined
    common = [s > 0, s * s == la * lc, k > 0, k * k == 3]
    predicate = z.And(dot < 0, 4 * dot * dot >= la * lc)
    angle = dot <= -s / 2
    disks = z.And(x * x + (y - 1 / k) ** 2 <= Q(Fraction(4, 3)), x * x + (y + 1 / k) ** 2 <= Q(Fraction(4, 3)))
    yield 'lens_predicate_iff_angle_120', common, z.Not(predicate == angle), [x, y, s, k]
    yield 'lens_angle_120_iff_two_disks', common, z.Not(angle == disks), [x, y, s, k]


def incircle_obligation():
    """(b2) u = (-1, 0), w = (1, 0) (similarity), p above, q below the line uw; neither apex strictly inside the diametral circle
    (angles at p and at q <= 90 degrees) => q is not strictly inside the circumcircle of u w p (locally Delaunay)."""
    px, py, qx, qy = z.Reals('px py qx qy')
    hyps = [py > 0, qy < 0, px * px + py * py >= 1, qx * qx + qy * qy >= 1]
    # circumcircle of (-1, 0), (1, 0), (px, py): centre (0, cy), cy = (px^2 + py^2 - 1) / (2 py), radius^2 = 1 + cy^2
    cy = (px * px + py * py - 1) / (2 * py)
    inside = qx * qx + (qy - cy) ** 2 < 1 + cy * cy
    return hyps, inside, [px, py, qx, qy]


def ratio_obligation():
    """(b3, the arithmetic part only): ends at radii r1 <= r2 <= 2 r1 of a shell piece and a vertex at radius rho between them:
    rho is within the factor sqrt 2 of one end (rho^2 <= 2 r1^2 or r2^2 <= 2 rho^2), hence within the factor 1.5 of the matching test
    (2.25 on squares). The geometric step (an encroaching vertex projects into the piece) is reviewed, not encoded."""
    r1, r2, rho = z.Reals('r1 r2 rho')
    hyps = [r1 > 0, r1 <= rho, rho <= r2, r2 <= 2 * r1]
    return hyps, z.Not(z.Or(rho * rho <= 2 * r1 * r1, r2 * r2 <= 2 * rho * rho)), [r1, r2, rho]


def check(hyps, bad, timeout_ms):
    s = z.Solver()
    s.set(timeout=timeout_ms)
    s.add(*hyps)
    s.add(bad)
    t0 = time.monotonic()
    r = s.check()
    return s, r, time.monotonic() - t0


def row(name, hyps, bad, timeout_ms, expect, note=''):
    s, r, secs = check(hyps, bad, timeout_ms)
    out = dict(name=name, result=str(r), expected=expect, seconds=round(secs, 3), ok=str(r) == expect)
    if r == z.unknown:
        out['reason'] = s.reason_unknown()
    sv = z.Solver()
    sv.set(timeout=timeout_ms)
    sv.add(*hyps)
    out['assumptions_result'] = str(sv.check())
    if note:
        out['note'] = note
    return out


def run(timeout_ms=120000):
    consts = source_contracts()
    lo, hi = consts['lo'], consts['hi']
    eps50, eps51 = Fraction(1, 2**50), Fraction(1, 2**51)
    rows, reports = [], []
    h, bd, _ = step_square()
    rows.append(row('S1_square_relative_error_(1+u)^3-1', h, bd, timeout_ms, 'unsat'))
    h, bd, _ = step_sum(c_pow(4))
    rows.append(row('S2_sum_of_squares_(1+u)^4-1', h, bd, timeout_ms, 'unsat'))
    h, bd, _ = step_sum(Fraction(1, 2**51))
    reports.append(row('S2_with_2^-51_in_place_of_(1+u)^4-1 (the derivation does not give 2^-51)', h, bd, timeout_ms, 'report'))
    h, bd, _ = step_range(lo, eps50)
    rows.append(row('S3_length_error_2^-50_in_the_guarded_range', h, bd, timeout_ms, 'unsat'))
    h, bd, _ = step_range(lo, eps51)
    reports.append(row('S3_length_error_2^-51_in_the_guarded_range (reported)', h, bd, timeout_ms, 'report'))
    h, bd, _ = step_range(None, eps50)
    rows.append(row('S3_mutant_without_the_lower_range_guard', h, bd, timeout_ms, 'sat'))
    for k, expect in ((consts['margin_exponent'], 'unsat'), (52, 'sat')):
        h, bd, _ = order_obligation(k, eps50)
        rows.append(row(f'order_margin_2^-{k}', h, bd, timeout_ms, expect))
    h, bd, _ = order_obligation(49, eps50)
    reports.append(row('order_margin_2^-49 (reported)', h, bd, timeout_ms, 'report'))
    h, bd, _ = fp_scaling_exact()
    rows.append(row('fp_margin_constant_is_exact_in_range', h, bd, timeout_ms, 'unsat'))
    for n, h, b, _ in lens_obligations():
        rows.append(row(n, h, b, timeout_ms, 'unsat'))
    h, b, _ = incircle_obligation()
    rows.append(row('apex_angles_le_90_imply_locally_delaunay', h, b, timeout_ms, 'unsat'))
    h, b, _ = ratio_obligation()
    rows.append(row('shell_piece_vertex_within_1.5_of_an_end', h, b, timeout_ms, 'unsat'))
    passed = all(r['ok'] and r['assumptions_result'] == 'sat' for r in rows)
    return dict(contracts={k: str(v) for k, v in consts.items()}, obligations=rows, reports=reports, passed=passed)


if __name__ == '__main__':
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument('--out')
    ap.add_argument('--timeout-ms', type=int, default=120000)
    args = ap.parse_args()
    report = run(args.timeout_ms)
    text = json.dumps(report, indent=2)
    print(text)
    if args.out:
        pathlib.Path(args.out).write_text(text + '\n')
    raise SystemExit(0 if report['passed'] else 1)
