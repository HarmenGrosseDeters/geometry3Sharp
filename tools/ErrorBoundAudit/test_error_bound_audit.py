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


if __name__ == '__main__':
    unittest.main()
