import json
import os
import pathlib
import unittest
from unittest.mock import patch
import drift_apex
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
                              ('return x == 0 || (a >= lo && a <= hi);', 'return a <= hi;'),
                              # review of #407: the gate literals of emit_double only, the __gate helper, the leaf test
                              ('lit(Math.ScaleB(1.0, -p.GateLow))', 'lit(Math.ScaleB(1.0, -p.GateLow - 20))'),
                              ('&& __in(p.z, lo, hi); }', '; }'),
                              ('$"__gate({x}, {lo}, {hi})"))};', '$"__gate({x}, {lo}, {lo})"))};'),
                              ('&& A.Op == Op.Input && B.Op == Op.Input;', '&& A.Op == Op.Input;'),
                              ('|| n.A.Degree == n.B.Degree);', '|| true);')]:
            with self.subTest(before=before):
                def changed(path, *args, **kwargs):
                    return original(path, *args, **kwargs).replace(before, after, 1)
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
        for before, after, *rest in [(f'> {k} *', f'> {below} *'),
                              ('m_v90 = m_v89 + m_v88;', 'm_v90 = m_v89 * m_v88;'),
                              ('// c = 18 u', '// c = 17 u'),
                              ('[2^-108, 2^164] </summary>', '[2^-109, 2^164] </summary>'),
                              ('[2^-54, 2^109] </summary>', '[2^-54, 2^110] </summary>'),
                              ('            if (m_v46 == 0) return 0;\n', ''),
                              # review of #407: the gate literals that run, the __gate helper, an extra line, an operator
                              ('3.0814879110195774e-33', '2.938735877055719e-39', -1),
                              ('&& __in(p.z, lo, hi); }', '; }'),
                              ('            int __r = 1;\n            // ((t1 - t2) + t3)',
                               '            if (m_v90 > 1) return 1;\n            int __r = 1;\n            // ((t1 - t2) + t3)'),
                              ('            return __r;\n        }\n\n        /// <summary> the exact stage of Orient3dLPI',
                               '            if (_v90 > 0) return 1;\n            return __r;\n        }\n\n        /// <summary> the exact stage of Orient3dLPI'),
                              ('if (_v90 < 0) __r = -__r;', 'if (_v90 > 0) __r = -__r;'),
                              ('_v15 = _v5 * _v14', '_v15 = _v5 / _v14'),
                              ('__s.Gate = __gate(p, ', '__s.Gate = true || __gate(p, '),
                              ('if (__s.Gate && __gate(a, 3.0814879110195774e-33', 'if (__s.Gate && __gate(a, 3.0814879110195774e-30'),
                              ('if (__gate(a1, 5.551115123125783e-17, 6.490371073168535e+32) && ',
                               'if (__gate(a1, 5.551115123125783e-17, 6.490371073168535e+32) || ')]:
            with self.subTest(before=before):
                self.assertIn(before, text)
                with self.assertRaises(ValueError):
                    filter_rules.composition_check(text.replace(before, after, *rest))

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



class DriftApexTests(unittest.TestCase):
    """Pilot 2, step 1: the D1 drift lemma of SafeRounding (drift_apex.py, README section 'Pilot 2, step 1')."""

    def test_drift_lemma_obligations_mutants_and_replays(self):
        report = drift_apex.run(TIMEOUT_MS)
        out = os.environ.get('Z3_DRIFT_APEX_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2)+'\n')
        self.assertTrue(report['obligations_passed'], json.dumps(report, indent=2))
        rows = report['obligations']
        self.assertTrue(all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' and r['assumptions_replayed']
                            for r in rows))
        # every apex shape: the four claims; today's rule for the ear and three apex shapes; the chain step
        self.assertEqual(4 * (6 + 25 + 3 + 2) + 3 + 3 * 4 + 1, len(rows))
        self.assertEqual('unsat', next(r for r in rows if r['name'] == 'chain_step_higham_2_5')['result'])
        names = {m['name'] for m in report['mutants'] if m['result'] == 'sat' and m['replayed']}
        self.assertEqual({'contraction_charges_vanishing_a', 'flip_charges_a_in_one_added_triangle',
                          'move_charges_ring_vertex', 'move_charges_M_without_delta', 'flip_charges_M_without_delta_I2',
                          'contraction_M_over_A_only', 'removal_M_over_A_only', 'ear_charges_only_vanishing_v',
                          'chain_step_with_2_4_model_only'}, names)
        self.assertTrue(all(p['result'] == 'sat' and p['replayed'] for p in report['partial_charge_nonvacuity']))

    def test_apex_is_in_every_added_triangle(self):
        for s in drift_apex.shapes():
            for c in s['apexes']:
                self.assertTrue(all(c in t for t in s['A']), (s['name'], c))
            self.assertTrue(s['apexes'])

    def _run_with(self, obligation):
        flip = drift_apex.flip()
        mutant = ('apex rule', 'flip_charges_a', flip, 'I1', 'a', None, 'RA')
        with patch.object(drift_apex, 'obligations', lambda: iter([obligation])), \
                patch.object(drift_apex, 'mutants', lambda: iter([mutant])), \
                patch.object(drift_apex, 'shapes', lambda: iter([flip])):
            return drift_apex.run(TIMEOUT_MS)

    def test_controls_pass(self):
        report = self._run_with(('apex rule', 'flip_I1', drift_apex.flip(), 'I1', 'apex', 'c'))
        self.assertTrue(report['obligations_passed'], report)

    def test_false_rule_is_not_success(self):
        # charging M without delta as if it were the rule: an obligation that is SAT must fail the run
        report = self._run_with(('apex rule', 'flip_I1_no_delta', drift_apex.flip(), 'I1', 'no_delta', 'c'))
        self.assertEqual('sat', report['obligations'][0]['result'])
        self.assertFalse(report['obligations_passed'])

    def test_unknown_is_not_success(self):
        original = drift_apex._check
        def unknown(assumptions, bad, timeout_ms):
            if drift_apex.z.is_true(bad):
                return original(assumptions, bad, timeout_ms)
            return None, drift_apex.z.unknown, 0.0
        with patch.object(drift_apex, '_check', unknown):
            report = self._run_with(('apex rule', 'flip_I1', drift_apex.flip(), 'I1', 'apex', 'c'))
        self.assertEqual('unknown', report['obligations'][0]['result'])
        self.assertFalse(report['obligations_passed'])

    @unittest.skip('TODO(D1): the apex rule is not implemented yet. The implementation PR adds the source contract: '
                   'the drift accounting of mesh_ops/MeshRoundingRepair.cs (improves, within_bound, commit_drift) '
                   'must match drift_apex.CHARGING (only the apex gets M + delta with an apex; every vertex without '
                   'one; acceptance M + delta <= maxLength with M over R and A), and fail on drift.')
    def test_source_contract_against_the_apex_rule(self):
        pass


if __name__ == '__main__':
    unittest.main()
