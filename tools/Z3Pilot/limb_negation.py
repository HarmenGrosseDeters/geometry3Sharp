"""Bit-vector proof of the complement-plus-one step in ExactLimbSigns.load."""
from check_pilot_obligations import check, main, report, source

SOURCE = 'math/ExactLimbSigns.cs'
CONTRACTS = [
    ('ulong carry = 1;', 'int len = n;', '862d5711c6bf66f9d7ea49611093aeea2b38e141b160e0708e050d6145bc4079'),
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
    source(SOURCE, *CONTRACTS[0])
    checks = [
        ('wide_carry_reachable', 'QF_BV', MODEL, 'high', 'sat'),
        ('word_or_carry_differs', 'QF_BV', MODEL,
         '(or (distinct next ((_ extract 63 0) wide)) (distinct carry high))', 'unsat'),
        ('carry_ignores_incoming_bit_mutant', 'QF_BV', MODEL,
         '(distinct (= next (_ bv0 64)) high)', 'sat'),
        ('two_limb_negation_differs', 'QF_BV', TWO_LIMBS,
         '(distinct (concat nextUpper next) (bvneg (concat upper word)))', 'unsat'),
        ('upper_carry_reachable', 'QF_BV', TWO_LIMBS, 'carry', 'sat'),
        ('skip_upper_carry_mutant', 'QF_BV', TWO_LIMBS,
         '(distinct (concat (bvnot upper) next) (bvneg (concat upper word)))', 'sat'),
    ]
    return report([check(*item, timeout_ms) for item in checks])


if __name__ == '__main__':
    raise SystemExit(main(run))
