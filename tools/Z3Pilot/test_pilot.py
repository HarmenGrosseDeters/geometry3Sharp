import json
import os
import pathlib
import unittest
from unittest.mock import patch
import filter_rules
import pilot

# Generous per-query timeout: the slowest obligation (pixel_upper) took 8.4 s locally, so the
# 20 s default of pilot.py leaves too little headroom on a slow CI runner, where a timeout would
# turn into UNKNOWN (a failure, never a pass). Z3_PILOT_TIMEOUT_MS overrides it.
TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))


class PilotTests(unittest.TestCase):
    def test_all_obligations_and_exact_witness_replay(self):
        report = pilot.run(TIMEOUT_MS)
        # CI runs the solver only here and uploads this report (Z3_PILOT_REPORT), not a second pilot run.
        out = os.environ.get('Z3_PILOT_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2)+'\n')
        self.assertTrue(report['obligations_passed'], report)
        self.assertEqual(6, len(report['obligations']))
        self.assertEqual(4602678819172646911, report['witness']['x_bits'])
        self.assertEqual(0, report['witness']['exact_pixel'])
        self.assertEqual(1, report['witness']['mutant_pixel'])

    def test_source_drift_is_rejected(self):
        original = pilot.pathlib.Path.read_text
        for before, after in [('if (f < r)', 'if (f <= r)'),
                              ('* (1 + 1e-12) + 1e-300;', '* (1 + 1e-12);'),
                              ('pad = Math.Abs(v) * 1e-13 + 1e-300;', 'pad = 0;')]:
            with self.subTest(before=before):
                def changed(path, *args, **kwargs):
                    return original(path, *args, **kwargs).replace(before, after)
                with patch.object(pilot.pathlib.Path, 'read_text', changed):
                    with self.assertRaises(ValueError):
                        pilot.source_contracts()

    def test_unknown_is_not_success(self):
        original = pilot.check
        def unknown(name, assumptions, bad, timeout):
            if name == 'controlled_unknown':
                return None, pilot.z.unknown, dict(name=name, result='unknown', reason='controlled timeout')
            return original(name, assumptions, bad, timeout)
        with patch.object(pilot, 'check', unknown), patch.object(pilot, 'obligations',
                lambda: iter([('controlled_unknown', [pilot.z.BoolVal(True)], pilot.z.BoolVal(False))])):
            self.assertFalse(pilot.run()['obligations_passed'])

    def test_direct_counterexample_is_fatal(self):
        with patch.object(pilot, 'direct_query', lambda timeout: dict(result='sat')), patch.object(pilot, 'obligations',
                lambda: iter([('control', [pilot.z.BoolVal(True)], pilot.z.BoolVal(False))])):
            self.assertFalse(pilot.run(direct=True)['obligations_passed'])

    def test_vacuous_obligation_is_not_success(self):
        vacuous = [('wrong_guard', [pilot.z.BoolVal(False)], pilot.z.BoolVal(True))]
        with patch.object(pilot, 'obligations', lambda: iter(vacuous)):
            self.assertFalse(pilot.run()['obligations_passed'])


class FilterRuleTests(unittest.TestCase):
    """Pilot 2: the error-bound rules of tools/PredicateCompiler's filter (filter_rules.py, README section 'Pilot 2')."""

    def test_all_rule_lemmas_mutants_and_replays(self):
        report = filter_rules.run(TIMEOUT_MS)
        out = os.environ.get('Z3_FILTER_RULES_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2)+'\n')
        self.assertTrue(report['obligations_passed'], json.dumps(report, indent=2))
        rows = report['obligations']
        self.assertTrue(all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' for r in rows))
        self.assertTrue(all(m['result'] == 'sat' and m['replayed'] for m in report['mutants']))
        rules = {r['rule'] for r in rows}
        self.assertEqual(rules, {m['rule'] for m in report['mutants']})
        self.assertEqual({'R0 value rule', 'R2 leaf', 'R3 sum', 'R4 product', 'R5 decision', 'R6 zero shortcut',
                          'E gate exponents', 'M magnitude', 'G grid', 'O overflow'}, rules)
        self.assertEqual({'Orient3dLPI', 'Orient3dTPI'}, set(report['composition']))
        self.assertEqual('1', report['leaf_tightness']['error_over_u_m'])

    def test_rule_source_drift_is_rejected(self):
        original = pilot.pathlib.Path.read_text
        for before, after in [('(u + Rational.Max(n.A.C, n.B.C)) / (one - u)', '(u + Rational.Max(n.A.C, n.B.C))'),
                              ('n.Leaf ? u :', 'n.Leaf ? Rational.Zero :'),
                              ('u + n.A.C + n.B.C + n.A.C * n.B.C', 'u + n.A.C + n.B.C'),
                              ('p.GateLow = 960 / p.Degree - 52;', 'p.GateLow = 970 / p.Degree - 52;'),
                              ('Math.ScaleB(1.0, -62)', 'Math.ScaleB(1.0, -63)'),
                              ('new Rational(1, BigInteger.Pow(2, 53))', 'new Rational(1, BigInteger.Pow(2, 52))'),
                              ('if (m{val(n)} == 0) return 0;', 'if ({val(n)} == 0) return 0;'),
                              ('$"m{val(n.A)} * m{val(n.B)}"', '$"m{val(n.A)} + m{val(n.B)}"'),
                              ('return x == 0 || (a >= lo && a <= hi);', 'return a <= hi;')]:
            with self.subTest(before=before):
                def changed(path, *args, **kwargs):
                    return original(path, *args, **kwargs).replace(before, after)
                with patch.object(pilot.pathlib.Path, 'read_text', changed):
                    with self.assertRaises(ValueError):
                        filter_rules.source_contracts()
        # the lemma side: a constant of filter_rules.py that differs from Compiler.cs
        for constant, value in [('U', filter_rules.Fraction(1, 2**52)), ('GRID_EXPONENT', 970), ('INPUT_ULP_BITS', 51),
                                ('K_MIN_EXPONENT', -63), ('BOUND_BITS', 1001)]:
            with self.subTest(constant=constant), patch.object(filter_rules, constant, value):
                with self.assertRaises(ValueError):
                    filter_rules.source_contracts()

    def test_generated_code_drift_is_rejected(self):
        text = (filter_rules.ROOT / filter_rules.GENERATED).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        filter_rules.composition_check(text)
        k = '1.998401444325284e-15'
        below = repr(filter_rules.math.nextafter(float(k), 0.0))
        for before, after in [(f'> {k} *', f'> {below} *'),
                              ('m_v90 = m_v89 + m_v88;', 'm_v90 = m_v89 * m_v88;'),
                              ('// c = 18 u', '// c = 17 u'),
                              ('[2^-108, 2^164] </summary>', '[2^-109, 2^164] </summary>'),
                              ('[2^-54, 2^109] </summary>', '[2^-54, 2^110] </summary>'),
                              ('            if (m_v46 == 0) return 0;\n', '')]:
            with self.subTest(before=before):
                self.assertIn(before, text)
                with self.assertRaises(ValueError):
                    filter_rules.composition_check(text.replace(before, after, 1))

    def _only_zero_rule(self, obligation, mutant):
        empty = lambda: iter([])
        return [patch.object(filter_rules, 'real_obligations', lambda: iter([obligation])),
                patch.object(filter_rules, 'real_mutants', lambda: iter([mutant])),
                patch.object(filter_rules, 'fp_obligations', empty), patch.object(filter_rules, 'fp_mutants', empty)]

    def _run_patched(self, patches):
        for p in patches:
            p.start()
        try:
            return filter_rules.run(TIMEOUT_MS)
        finally:
            for p in patches:
                p.stop()

    ZERO = ('R6 zero shortcut', 'zero_magnitude', 'LRA', filter_rules.real_vars('e et m c'),
            lambda B, v: filter_rules.zero_rule(B, v), dict(e=0, et=0, m=0, c=1))
    ZERO_MUTANT = ('R6 zero shortcut', 'zero_tests_value_not_magnitude', filter_rules.real_vars('e et m c'),
                   lambda B, v: filter_rules.zero_rule(B, v, 'tests_value'))

    def test_controls_pass(self):
        self.assertTrue(self._run_patched(self._only_zero_rule(self.ZERO, self.ZERO_MUTANT))['obligations_passed'])

    def test_vacuous_rule_is_not_success(self):
        vacuous = self.ZERO[:4] + (lambda B, v: ([v.m == 0, v.m == 1], v.e == 5), dict(e=0, et=0, m=0, c=1))
        report = self._run_patched(self._only_zero_rule(vacuous, self.ZERO_MUTANT))
        self.assertEqual('unsat', report['obligations'][0]['assumptions_result'])
        self.assertFalse(report['obligations_passed'])

    def test_mutant_that_holds_is_not_success(self):
        report = self._run_patched(self._only_zero_rule(self.ZERO, self.ZERO[:2] + self.ZERO[3:5]))
        self.assertEqual('unsat', report['mutants'][0]['result'])
        self.assertFalse(report['obligations_passed'])

    def test_unknown_is_not_success(self):
        original = filter_rules._check
        def unknown(assumptions, bad, timeout_ms):
            if z.is_true(bad):
                return original(assumptions, bad, timeout_ms)
            class Unknown:
                def reason_unknown(self):
                    return 'controlled timeout'
            return Unknown(), filter_rules.z.unknown, 0.0
        z = filter_rules.z
        report = self._run_patched(self._only_zero_rule(self.ZERO, self.ZERO_MUTANT)
                                   + [patch.object(filter_rules, '_check', unknown)])
        self.assertEqual('unknown', report['obligations'][0]['result'])
        self.assertFalse(report['obligations_passed'])


if __name__ == '__main__':
    unittest.main()
