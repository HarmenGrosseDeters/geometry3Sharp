# PROTOTYPE SPEC (exact-number study, runs/exact-numbers): the double-input predicates as exact sums of monomials
# (P2, the replacement of ExactPredicates.*_bigint). PROPOSED extension of the spec language of
# tools/PredicateCompiler; today's compiler does not read 'dpredicate'.
#
#   dpredicate Name(points) inputs xy | xyz
#       definitions and one 'sign' line, as for ipredicate; every leaf is an input (no differences of inputs are
#       needed: the formula is the homogeneous determinant with a column of ones)
#
# Input model: every input is a finite double v = m 2^e with an integer |m| <= 2^53 - 1 and
# e in [-1074, 971] (subnormals: e = -1074, |m| < 2^52). No range gate.
#
# New pass (monomial rule), over the same expression graph as the value rule:
#   1. Expand the sign expression into monomials over the inputs, with integer coefficients (like terms merged;
#      a formula whose monomials do not all have the same degree d is rejected).
#   2. Value rule per monomial: |c m_1 ... m_d| <= |c| (2^53 - 1)^d, so ceil(53 d / 64) limbs (+ the bits of |c|).
#   3. Accumulator rule: the positive and the negative monomials are added, each shifted left by
#      (e_1 + ... + e_d) - d emin, into two magnitude accumulators. With s = emax - emin over the nonzero inputs of
#      the call and g = ceil(log2(sum of |c|)):
#          each accumulator < 2^(d s + 53 d + g)
#          limbs L(s) = floor(d s / 64) + ceil((53 d + g + 63) / 64)        (a monomial is written to at most d + 1
#          limbs starting at floor(offset / 64), offset <= d s; the sum never carries out of L(s) limbs)
#      worst case s = 2045. The sign is the comparison of the two accumulators from the top limb.
#   4. Generated code: one unrolled block per monomial (d - 1 multiplications of 64 x 64 -> 128, a shift, an add with
#      carry of d + 1 limbs), two stackalloc'ed accumulators of L(s) limbs cleared per call, no heap allocation, no
#      floating-point operation (so no FMA, no rounding mode, no overflow or underflow).
# Derived table (scripts/check_bounds.py, checked for every s in [0, 2045] with exact integers):
#   predicate  d  monomials  g  bits of a monomial  L(0)  L(2045)  stack bytes at s = 2045 (both accumulators)
#   orient2d   2      6      3        106             3      66        1056
#   orient3d   3     24      5        159             4      99        1584
#   incircle   4     48      6        212             5     132        2112
#   insphere   5    360      9        265             6     165        2640
# Tests the generator PR needs (as GeneratedPredicatesTests does for ipredicate): the table equals the derivation;
# the sign equals ExactPredicates.*_bigint on the differential corpus (random, degenerate, 2^k scaled, subnormal,
# span 2^2045); inputs with every |m| = 2^53 - 1 at s = 0 and s = 2045 fill the top limb without a carry out.

# sign of (b - a) x (c - a) = det [a 1; b 1; c 1]
dpredicate Orient2dDoubles(a, b, c) inputs xy
    bc = b.x * c.y - b.y * c.x
    ac = a.x * c.y - a.y * c.x
    ab = a.x * b.y - a.y * b.x
    sign bc - ac + ab

# sign of ((b - a) x (c - a)) . (d - a) = det(b, c, d) - det(a, c, d) + det(a, b, d) - det(a, b, c)
dpredicate Orient3dDoubles(a, b, c, d) inputs xyz
    bcd = b.x * (c.y * d.z - c.z * d.y) + b.y * (c.z * d.x - c.x * d.z) + b.z * (c.x * d.y - c.y * d.x)
    acd = a.x * (c.y * d.z - c.z * d.y) + a.y * (c.z * d.x - c.x * d.z) + a.z * (c.x * d.y - c.y * d.x)
    abd = a.x * (b.y * d.z - b.z * d.y) + a.y * (b.z * d.x - b.x * d.z) + a.z * (b.x * d.y - b.y * d.x)
    abc = a.x * (b.y * c.z - b.z * c.y) + a.y * (b.z * c.x - b.x * c.z) + a.z * (b.x * c.y - b.y * c.x)
    sign bcd - acd + abd - abc

# incircle: det [p.x p.y p.x^2 + p.y^2 1] over a, b, c, d (the sign of ExactPredicates.incircle_bigint)
dpredicate InCircleDoubles(a, b, c, d) inputs xy
    la = a.x * a.x + a.y * a.y
    lb = b.x * b.x + b.y * b.y
    lc = c.x * c.x + c.y * c.y
    ld = d.x * d.x + d.y * d.y
    bcd = b.x * (c.y * ld - lc * d.y) - b.y * (c.x * ld - lc * d.x) + lb * (c.x * d.y - c.y * d.x)
    acd = a.x * (c.y * ld - lc * d.y) - a.y * (c.x * ld - lc * d.x) + la * (c.x * d.y - c.y * d.x)
    abd = a.x * (b.y * ld - lb * d.y) - a.y * (b.x * ld - lb * d.x) + la * (b.x * d.y - b.y * d.x)
    abc = a.x * (b.y * lc - lb * c.y) - a.y * (b.x * lc - lb * c.x) + la * (b.x * c.y - b.y * c.x)
    sign abc - abd + acd - bcd
