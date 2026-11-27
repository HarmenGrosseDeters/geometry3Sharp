"""The checks of the error bound audit (docs/research/ERROR_BOUND_AUDIT_2026-11.md), fast enough for every CI run:

- every anchor of every model is still in the source (a changed filter or constant must be re-derived);
- every derivation still proves its bound, and the open findings are exactly the recorded ones;
- the analysis itself is sound on concrete doubles (each node's facts against exact fractions), and unsound rules or
  smaller constants are caught (mutants).

    python3 -m unittest discover -s tools/ErrorBoundAudit -p 'test_*.py'
"""
import os
import random
import sys
import unittest
from fractions import Fraction

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audit                                                    # noqa: E402
import bounds                                                   # noqa: E402
import fpbound                                                  # noqa: E402
from fpbound import Context, Mirrored, Sampler, U, dominates    # noqa: E402

# the findings the note records (id -> entry); a new one, or one that goes away, fails here until the note is updated
FINDINGS = {}


class AuditTable(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows = audit.run(samples=3)

    def test_anchors_are_in_the_source(self):
        for e, miss, _, _, _ in self.rows:
            self.assertEqual(miss, [], '%s: the source changed, re-derive the model' % e.id)

    def test_every_bound_is_proved_or_a_recorded_finding(self):
        found = {}
        for e, _, verdicts, _, _ in self.rows:
            self.assertTrue(verdicts, e.id)
            for label, v in verdicts:
                if v.ok:
                    continue
                self.assertTrue(getattr(v, 'finding', None), '%s / %s: not proved: %s' % (e.id, label, v.detail))
                found[v.finding] = e.id
        self.assertEqual(found, FINDINGS)

    def test_models_hold_on_concrete_doubles(self):
        for e, _, _, bad, _ in self.rows:
            self.assertEqual(bad, [], e.id)

    def test_margins_are_at_least_one(self):
        for e, _, verdicts, _, _ in self.rows:
            for label, v in verdicts:
                if v.ok and v.margin is not None:
                    self.assertGreaterEqual(v.margin, 1, '%s / %s' % (e.id, label))


class Mutants(unittest.TestCase):
    """the checker must reject what is wrong: smaller constants, and a rule made unsound"""

    def orient3d(self, constant, underflow_unit=2.0**-1066):
        c = Context()
        L = bounds.mleaves(c, 'ux uy uz vx vy vz wx wy wz')
        det = bounds.mdet3(*L)
        m = det.magnitude_atom('PERM')
        s = L[6].m + L[7].m + L[8].m + 1.0
        b = c.const(constant) * m + s * c.const(underflow_unit)
        return dominates(det.sign_error('PERM'), b.lo, b.defect)

    def test_orient3d_constant_is_tight_to_second_order(self):
        eps = bounds.EPS
        self.assertTrue(self.orient3d((7.0 + 56.0 * eps) * eps).ok)
        self.assertFalse(self.orient3d(7.0 * eps).ok)                      # first order alone is not enough
        self.assertFalse(self.orient3d((6.0 + 56.0 * eps) * eps).ok)
        self.assertFalse(self.orient3d((7.0 + 56.0 * eps) * eps, 0.0).ok)  # no underflow term
        self.assertFalse(self.orient3d((7.0 + 56.0 * eps) * eps, 2.0**-1075).ok)

    def test_sn5_constant_accepts_6e16_and_rejects_below_3u(self):
        def check(constant):
            c = Context()
            dx, dy = c.diff('dx'), c.diff('dy')
            cxv, CX = c.param('CX')
            cyv, CY = c.param('CY')
            ex = c.leaf('ex', CX * U + fpbound.Poly.atom('ex') * U)
            ey = c.leaf('ey', CY * U + fpbound.Poly.atom('ey') * U)
            o = dx * ey - dy * ex
            b = c.const(constant) * (dx.abs() * (ey.abs() + cyv) + dy.abs() * (ex.abs() + cxv))
            return fpbound.sign_dominates(o, b.lo, b.defect, abs_floor=Fraction(1e-140))

        v_ok = check(6e-16)
        self.assertTrue(v_ok.ok)
        self.assertGreater(v_ok.margin, Fraction(3, 2))
        self.assertFalse(check(3e-16).ok)     # below the proven 3 u (~3.331e-16) sign-filter threshold

    def test_a_guard_removed_from_one_function_is_seen(self):
        # the same guard line is in three functions: the anchor must be found in the entry's own function
        e = next(x for x in bounds.ENTRIES if x.id.startswith('EB-3'))
        self.assertEqual(audit.anchors_missing(e), [])
        moved = bounds.Entry(e.id, e.file, ['double a = pa[0] * (rx - gx), b = pa[1] * (ry - gy), c = pa[2] * (rz - gz);'],
                             e.guards, e.assumption, e.claimed, e.model, dict(scope=e.scope))
        self.assertEqual(len(audit.anchors_missing(moved)), 1)       # a line of front_side_filtered, not of plane_side_abs
        twice = bounds.Entry(e.id, e.file, e.anchors, e.guards, e.assumption, e.claimed, e.model, dict(scope='double v = a + b + c - d;'))
        self.assertTrue(audit.anchors_missing(twice)[0].startswith('scope'))

    def test_dominates_rejects_what_the_floor_does_not_pay(self):
        from fpbound import Poly, ETA
        x = Poly.atom('x')
        self.assertTrue(dominates(x * U + Poly.const(ETA), x * (2 * U), abs_floor=Fraction(1, 2**1000)).ok)
        self.assertFalse(dominates(x * U + Poly.const(ETA), x * (2 * U)).ok)                 # no constant, no guard
        self.assertFalse(dominates(x * U + Poly.const(ETA), x * (2 * U), abs_floor=ETA).ok)  # the floor is all used up
        self.assertFalse(dominates(x * (3 * U), x * (2 * U)).ok)
        y = Poly.atom('y')
        self.assertFalse(dominates(x * y * ETA, x * x + Poly.const(1)).ok)                   # amgm is off by default
        self.assertTrue(dominates(x * y * ETA, (x * x + y * y) * ETA, amgm=True).ok)
        self.assertFalse(dominates(x * y * ETA, (x * x + y * y) * (ETA / 4), amgm=True).ok)

    def test_an_unsound_rule_is_caught_on_doubles(self):
        # a product rule without its rounding term: the concrete run must see errors above the claimed facts
        saved = fpbound.Value.__mul__

        def bad_mul(self, o):
            v = saved(self, o)
            v.err = self.hi * o.err + (o.hi + o.err) * self.err
            self.ctx.check(v)
            return v
        fpbound.Value.__mul__ = bad_mul
        try:
            rng = random.Random(3)
            violations = 0
            for _ in range(200):
                c = Context(Sampler(rng))
                x, y = c.exact('x'), c.exact('y')
                _ = x * y
                violations += len(c.violations)
        finally:
            fpbound.Value.__mul__ = saved
        self.assertGreater(violations, 0)

    def test_the_mirrored_invariant_on_doubles(self):
        rng = random.Random(5)
        for _ in range(300):
            c = Context(Sampler(rng, emin=-340, emax=300))
            det = bounds.mdet3(*bounds.mleaves(c, 'ux uy uz vx vy vz wx wy wz'))
            self.assertEqual(c.violations, [])
            self.assertLess(det.c, 9 * U)

    def test_a_wrong_sign_is_reported(self):
        # the filter of EP-1 with a bound of zero decides wrongly on nearly collinear points
        rng = random.Random(7)
        wrong = 0
        for _ in range(200):
            c = Context(Sampler(rng))
            r = bounds.Run(c)
            ux, uy, vx, vy = bounds.mleaves(c, 'ux uy vx vy')
            det = ux * vy - uy * vx
            zero = c.const(0.0)
            r.observe_sign(det.v, zero)
            wrong += any(v[0] == 'WRONG SIGN' for v in c.violations)
        # (random differences seldom cancel: the check is that observe_sign can fail at all)
        c = Context(Sampler(rng))
        r = bounds.Run(c)
        one = c.exact('p')
        one.fv, one.xv = 1.0, Fraction(-1)
        r.observe_sign(one, c.const(0.5))
        self.assertTrue(any(v[0] == 'WRONG SIGN' for v in c.violations))


class BoundaryTreeModels(unittest.TestCase):
    """the models BT-1 to BT-6 of the node error of the BoundaryTree (#1427): present, proved with the margins of the hand table, and with teeth"""

    def entries(self):
        return {e.id.split()[0]: e for e in bounds.ENTRIES if e.id.startswith('BT-')}

    def test_the_six_clauses_are_modelled_and_proved_with_their_margins(self):
        es = self.entries()
        self.assertEqual(sorted(es), ['BT-1', 'BT-2', 'BT-3', 'BT-4', 'BT-5', 'BT-6'])
        by = {}
        for k, e in es.items():
            for label, v in audit.derive(e):
                self.assertTrue(v.ok, '%s / %s: %s' % (k, label, v.detail))
                by[(k, label)] = v.margin
        # the tight clauses of the hand table: A1 against its 1e-14 (the note: 9.9e-15), the charged 1e-12 (a margin of about 100), the sum order constant
        a1 = [v for (k, l), v in by.items() if k == 'BT-5' and l.startswith('A1: the three')]
        self.assertTrue(a1 and 1.0 <= a1[0] < 1.05, a1)
        charged = [v for (k, l), v in by.items() if k == 'BT-5' and l.startswith('the 1e-12 charged')]
        self.assertTrue(charged and 90 < charged[0] < 120, charged)
        order = [v for (k, l), v in by.items() if k == 'BT-4' and l.startswith('7.5e-16 N W')]
        self.assertTrue(order and 1.06 < order[0] < 1.07, order)         # 1.064: the constant against 2 u pi / (1 - N u), N u <= 0.0099
        sharp = [v for (k, l), v in by.items() if k == 'BT-5' and l.startswith('A1: the sharper')]
        self.assertTrue(sharp and 1.25 < sharp[0] < 1.35, sharp)         # 1.30: step 4 with 16 u S2 instead of 22 u S2
        self.assertTrue(1.01 < a1[0] < 1.02, a1)                         # 1.013: the proof's constants
        # BT-6: the rounded angle kept by finish_edge_angle is within (pi / 2) 22.1 u / 0.0220 = 1.75e-13 (plus 2 pi u and 2 ulp), whatever the distance from the edge
        near = [e for k, e in es.items() if k == 'BT-6'][0]
        labels = [l for l, _ in audit.derive(near)]
        self.assertTrue(any(l.startswith('the error of a kept rounded angle is at most 1.752e-13') and l.endswith('below 2e-13') for l in labels), labels)
        self.assertTrue(any(l.startswith('the sign of num is exact where |num| > 1e-12 S1 and S1 >= 2^-1000') for l in labels), labels)

    def test_bt6_rejects_a_guard_or_a_ratio_that_does_not_prove_the_claim(self):
        # the subnormal guard below 2^-1030 leaves products whose rounding (2^-1074) exceeds 1e-12 S1; a ratio 2^-6 gives hypot >= 0.011 (S1 + S2), error 3.5e-13
        near = [e for e in bounds.ENTRIES if e.id.startswith('BT-6')][0]
        saved = (bounds.SUBNORMAL_S1, bounds.FAST_ANGLE_RATIO)
        try:
            self.assertTrue(all(v.ok for _, v in audit.derive(near)))
            bounds.SUBNORMAL_S1 = 2.0 ** -1040
            self.assertFalse(all(v.ok for _, v in audit.derive(near)), 'a guard of 2^-1040 is not seen')
            bounds.SUBNORMAL_S1 = saved[0]
            bounds.FAST_ANGLE_RATIO = 2.0 ** -6
            self.assertFalse(all(v.ok for _, v in audit.derive(near)), 'a ratio of 2^-6 is not seen')
        finally:
            bounds.SUBNORMAL_S1, bounds.FAST_ANGLE_RATIO = saved

    def test_a_two_pi_error_on_a_certified_edge_is_a_violation_but_not_on_an_uncertified_one(self):
        # the branch cut of the arctangent is never crossed for the edges of an accepted node (part of the proof of A1), so a 2 pi difference
        # there is a real error and must not be reduced modulo 2 pi; for an uncertified edge the angles are compared as such
        es = self.entries()
        saved = bounds._fast_edge
        try:
            bounds._fast_edge = lambda p, a, b: (saved(p, a, b)[0] + 2 * 3.141592653589793, saved(p, a, b)[1])
            bad, _ = audit.concrete(es['BT-5'], 30)
            self.assertGreater(len(bad), 0, 'a 2 pi error on a certified edge is hidden')
            bad, _ = audit.concrete(es['BT-6'], 30)
            self.assertEqual(bad, [], 'an uncertified edge is compared modulo 2 pi')
        finally:
            bounds._fast_edge = saved

    def test_the_concrete_edge_samples_see_a_bound_that_is_too_small(self):
        es = self.entries()
        # BT-6: the formula divided by 100 is violated by the near-edge samples (they reach 0.04 of the formula)
        saved = bounds._angle_error_bound
        try:
            bad, worst = audit.concrete(es['BT-6'], 30)
            self.assertEqual(bad, [])
            self.assertGreater(worst, 0.001, 'the sampling reached no edge near p')
            bounds._angle_error_bound = lambda *a: saved(*a) / 100
            bad, _ = audit.concrete(es['BT-6'], 30)
            self.assertGreater(len(bad), 0, 'a formula 100 times too small is not seen')
        finally:
            bounds._angle_error_bound = saved
        # BT-5: the 1e-14 of A1 itself: accepted nodes reach 0.07 of it (7e-16); a claim of 1e-18 (plus the 4e-16 of the oracle) is violated
        one = bounds.ONE_E14_D
        try:
            bad, worst = audit.concrete(es['BT-5'], 30)
            self.assertEqual(bad, [])
            self.assertGreater(worst, 0.01, 'the sampling reached no accepted edge')
            bounds.ONE_E14_D = one / 10000
            bad, _ = audit.concrete(es['BT-5'], 30)
            self.assertGreater(len(bad), 0, 'an A1 of 1e-18 is not seen')
        finally:
            bounds.ONE_E14_D = one


if __name__ == '__main__':
    unittest.main()
