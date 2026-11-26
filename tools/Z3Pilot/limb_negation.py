"""Bit-vector proof of the complement-plus-one step in ExactLimbSigns.load."""
import re

from check_pilot_obligations import check, main, report, source

SOURCE = 'math/ExactLimbSigns.cs'
CONTRACTS = [
    ('ulong carry = 1;', 'int len = n;', '862d5711c6bf66f9d7ea49611093aeea2b38e141b160e0708e050d6145bc4079'),
    ('internal const int MaxOperandLimbs', '#if NET\n        // the limbs', 'a4f2e74f23c7ab47a922f4504da958e2b26ca599e14e8ec3fab0a46e558b7b41'),
    ('int total = na + nb + nc + nd + nx + ny + nz + nw;', 'FixedInt fa = load', 'c371771707627b20502428cbf4eae9ff6b27251e0c29b24711dc78b90fd6c29a'),
    ('int total = 0;', 'FixedInt* m = stackalloc FixedInt[16];', 'dffee85f9e43325252782412eb8b744ac0e191f151cc5345c5c3b4645a8760ed'),
]
MODEL = """
(declare-const word (_ BitVec 64))
(declare-const c (_ BitVec 64))
(assert (or (= c (_ bv0 64)) (= c (_ bv1 64))))
(define-fun next () (_ BitVec 64) (bvadd (bvnot word) c))
(define-fun carry () Bool (and (distinct c (_ bv0 64)) (= next (_ bv0 64))))
(define-fun wide () (_ BitVec 65)
  (bvadd ((_ zero_extend 1) (bvnot word)) ((_ zero_extend 1) c)))
(define-fun high () Bool (= ((_ extract 64 64) wide) #b1))
"""


TWO_LIMBS = MODEL + """
(declare-const upper (_ BitVec 64))
(assert (= c (_ bv1 64)))
(define-fun nextUpper () (_ BitVec 64)
  (bvadd (bvnot upper) (ite carry (_ bv1 64) (_ bv0 64))))
"""


def run(timeout_ms=120000):
    sections = [source(SOURCE, *item) for item in CONTRACTS]
    budgets = sections[1]
    side_limit = int(re.search(r'MaxOperandLimbs = (\d+)', budgets)[1])
    det_limit = int(re.search(r'MaxDeterminantLimbs = (\d+)', budgets)[1])
    # Each sum may take one limb beyond its larger input. Use the sum
    # of operand widths to bound each term for the side computation,
    # and row maxima to bound the cofactor expansion for the determinant.
    side = f"""
    (declare-const total Int)
    (assert (and (<= 8 total) (<= total {side_limit})))
    (define-fun need () Int (+ total (+ total 1) (+ total 2) (+ total 3)))
    (define-fun allocated () Int (+ (* 4 total) 16))
    """
    det = f"""
    (declare-const a Int)
    (declare-const b Int)
    (declare-const c Int)
    (declare-const d Int)
    (declare-const total Int)
    (assert (and (>= a 1) (>= b 1) (>= c 1) (>= d 1) (>= total (+ a b c d))))
    (define-fun minor () Int (+ (* 5 b) (* 14 (+ c d)) 11))
    (define-fun minorWidth () Int (+ b c d 3))
    (define-fun term () Int (+ a minorWidth))
    (define-fun need () Int (+ (* 4 minor) (* 4 term) (+ term 1) (+ term 2) (+ term 3)))
    (define-fun allocated () Int (+ (* 8 a) (* 28 b) (* 64 (+ c d)) 96))
    (assert (<= (+ total allocated) {det_limit}))
    """
    checks = [
        ('wide_carry_reachable', 'QF_BV', MODEL, 'high', 'sat'),
        ('word_or_carry_differs', 'QF_BV', MODEL,
         '(or (distinct next ((_ extract 63 0) wide)) (distinct carry high))', 'unsat'),
        ('carry_ignores_incoming_bit_mutant', 'QF_BV', MODEL,
         '(distinct (= next (_ bv0 64)) high)', 'sat'),
        ('two_limb_negation_differs', 'QF_BV', TWO_LIMBS,
         '(distinct (concat nextUpper next) (bvneg (concat upper word)))', 'unsat'),
        ('side_stack_path_reachable', 'QF_LIA', side, '(= total 8)', 'sat'),
        ('side_scratch_too_small', 'QF_LIA', side, '(> need allocated)', 'unsat'),
        ('det_stack_path_reachable', 'QF_LIA', det, '(= total 16)', 'sat'),
        ('det_scratch_too_small', 'QF_LIA', det, '(> need allocated)', 'unsat'),
    ]
    return report([check(*item, timeout_ms) for item in checks])


if __name__ == '__main__':
    raise SystemExit(main(run))
