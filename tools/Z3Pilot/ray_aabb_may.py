"""Finite slab-time comparison and face tests in IntrExactBox3.RayAabbMay.

Faces are the rounded expanded endpoints, as used by the exact predicate.
For a hit every exit is nonnegative; positive entry candidates and exits
have nonnegative computed signs. The selected min/max candidates need
only bound their own exact times. The quotient-error envelope below
follows from two roundings (difference and division); a subnormal
difference of two binary64 values is exact. Overflowing intermediates
are checked separately at the final comparison, conditional on
nonnegative selected endpoints for an exact hit.
"""
from fractions import Fraction

from continuous_collision import rational
from pilot_obligations import main, source_contract, verify

SOURCE = 'intersection/IntrExactBox3.cs'
ANCHORS = [
    ('internal const double U', 'internal struct Dy', '82cf2190a78c8ae1db3f89d98f54539b2940a1efa6637b19b2cace6b54444a2e'),
    ('internal static bool RayAabbMay(', 'internal static bool RayEntry(', '7e19c25bb7e3e1d35012ee87fe4c263e8e23a611d024e76a0d74f8e5fb77a360'),
]


def run(timeout_ms=120000):
    source_contract(SOURCE, ANCHORS)
    u = Fraction(1, 2**53)
    abs_err = Fraction(1e-320)
    # In terms of the computed magnitude: each rounding can be inverted
    # with relative loss at most u/(1-u), and division adds half a spacing.
    qerr = (1 / (1 - u))**2 - 1
    subnormal = Fraction(1, 2**1074)
    model = f"""
    (define-fun u () Real {rational(u)})
    (define-fun absErr () Real {rational(abs_err)})
    (define-fun close ((x Real) (y Real) (e Real)) Bool
      (and (<= (- x y) e) (<= (- y x) e)))
    (declare-const n Real)
    (declare-const f Real)
    (declare-const exactN Real)
    (declare-const exactF Real)
    (declare-const sum Real)
    (declare-const relative Real)
    (declare-const margin Real)
    (declare-const limit Real)
    (declare-const faceLo Real)
    (declare-const faceHi Real)
    (declare-const origin Real)
    (declare-const direction Real)
    (declare-const t Real)
    (assert (and (>= n 0) (>= f 0) (>= margin 0)))
    (assert (close n exactN (+ (* {rational(qerr)} n) {rational(subnormal)})))
    (assert (close f exactF (+ (* {rational(qerr)} f) {rational(subnormal)})))
    (assert (close sum (+ n f) (* u (+ n f))))
    (assert (close relative (* 3 u sum) (+ (* 3 u u sum) {rational(subnormal / 2)})))
    (assert (close margin (+ relative absErr) (* u (+ relative absErr))))
    (assert (close limit (+ f margin) (* u (+ f margin))))
    (define-fun earlyMiss () Bool
      (or (and (>= direction 0) (> origin faceHi))
          (and (<= direction 0) (< origin faceLo))))
    """
    result = verify('QF_LRA', model, [
        ('finite_hit_reachable', '(and (<= exactN exactF) (= n f))', 'sat'),
        ('finite_miss_reachable', '(> n limit)', 'sat'),
        ('finite_false_miss', '(and (<= exactN exactF) (> n limit))', 'unsat'),
        ('drop_margin_mutant', '(and (<= exactN exactF) (> n f))', 'sat'),
        # movement is the sign-compatible product t*direction, abstracted
        # to a free real so only linear arithmetic is needed.
        ('early_face_false_miss',
         '(and (<= faceLo (+ origin t)) (<= (+ origin t) faceHi) '
         '(=> (>= direction 0) (>= t 0)) (=> (<= direction 0) (<= t 0)) earlyMiss)',
         'unsat'),
    ], timeout_ms)
    # The exact-hit sign premise excludes a negative infinite exit.
    # If the slab times or their nonnegative sum are infinite, the
    # rounded margin/limit propagates positive infinity.
    overflow = """
    (declare-const n (_ FloatingPoint 11 53))
    (declare-const f (_ FloatingPoint 11 53))
    (assert (fp.geq n (_ +zero 11 53)))
    (assert (fp.geq f (_ +zero 11 53)))
    (define-fun sum () (_ FloatingPoint 11 53) (fp.add RNE (fp.abs n) (fp.abs f)))
    (define-fun relative () (_ FloatingPoint 11 53)
      (fp.mul RNE ((_ to_fp 11 53) #x3cb8000000000000) sum))
    (define-fun margin () (_ FloatingPoint 11 53)
      (fp.add RNE relative ((_ to_fp 11 53) #x00000000000007e8)))
    (define-fun limit () (_ FloatingPoint 11 53) (fp.add RNE f margin))
    """
    infinite = verify('QF_FP', overflow, [
        ('infinite_slabs_reachable', '(fp.isInfinite sum)', 'sat'),
        ('sum_overflow_false_miss', '(and (fp.isInfinite sum) (fp.gt n limit))', 'unsat'),
        ('limit_overflow_reachable', '(and (not (fp.isInfinite sum)) (fp.isInfinite limit))', 'sat'),
        ('limit_overflow_false_miss', '(and (fp.isInfinite limit) (fp.gt n limit))', 'unsat'),
    ], timeout_ms)
    return dict(passed=result['passed'] and infinite['passed'], finite=result, infinite=infinite)


if __name__ == '__main__':
    raise SystemExit(main(run))
