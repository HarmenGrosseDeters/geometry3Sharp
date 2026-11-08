import json
import os
import pathlib
import subprocess
import sys
import textwrap
import unittest


class ConservativeMcTests(unittest.TestCase):
    @staticmethod
    def child(code):
        # Keep this pilot's SMT context out of the pre-existing FP pilots.
        # Z3's nonlinear search heuristics depend on global expression IDs.
        result = subprocess.run([sys.executable, '-c', textwrap.dedent(code)],
                                cwd=pathlib.Path(__file__).parent, text=True, capture_output=True, check=True)
        return json.loads(result.stdout)

    def test_adapter_and_fp_envelopes(self):
        # (the child reads Z3_PILOT_TIMEOUT_MS itself: conservative_mc.DEFAULT_TIMEOUT_MS)
        report = self.child('import json, conservative_mc as mc; print(json.dumps(mc.run()))')
        out = os.environ.get('Z3_CONSERVATIVE_MC_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2)+'\n')
        self.assertTrue(report['obligations_passed'], json.dumps(report, indent=2))
        self.assertTrue(report['sharpness_replayed'])
        self.assertTrue(report['wrong_crossing_replayed'])
        self.assertTrue(report['underflow_control_replayed'])
        self.assertTrue(all(r['replayed'] for r in report['fp_replays']))
        self.assertTrue(all(r['result'] == 'sat' and r['replayed'] for r in report['fp_mutants']))
        self.assertEqual(260, len(report['obligations']))
        self.assertTrue(all(r['assumptions_result'] == 'sat' and r['result'] == 'unsat' for r in report['obligations']))

    def test_relative_model_covers_actual_binary64_outside_old_domain(self):
        report = self.child("""
            import json, conservative_mc as mc
            print(json.dumps(mc.binary64_replays()))
        """)
        from fractions import Fraction
        by_name = {r['name']: r for r in report}
        self.assertTrue(all(r['replayed'] for r in report), json.dumps(report, indent=2))
        self.assertGreater(float.fromhex(by_name['large_samples']['operands'][0]), 1)
        self.assertLess(Fraction(by_name['tiny_sample_gap']['sample_gap']), Fraction(1, 2**20))
        subnormal = by_name['subnormal_differences']
        self.assertLess(Fraction(subnormal['sample_gap']), Fraction(1, 2**1022))
        self.assertEqual('0', subnormal['errors']['dn'])
        self.assertEqual('0', subnormal['errors']['dd'])
        underflow = by_name['subnormal_quotient']
        self.assertFalse(underflow['pure_relative_division_holds'])
        self.assertNotEqual('0', underflow['errors']['eta'])
        self.assertFalse(underflow['endpoint_collapsed'])
        scales = [r for r in report if r['name'].startswith('field_scale_')]
        self.assertGreater(len(scales), 1)
        self.assertEqual({(.25).hex()}, {r['rounded_parameter'] for r in scales})

    def test_relative_fp_control_mutations_are_rejected(self):
        reports = self.child("""
            import json
            from unittest.mock import patch
            import conservative_mc as mc
            with patch.object(mc, 'UNDERFLOW_ALLOWANCE', 0):
                missing_underflow = mc.run()
            with patch.object(mc, 'PARAMETER_ENVELOPE', 2*mc.UNIT_ROUNDOFF):
                too_tight = mc.run()
            with patch.object(mc, 'relative_ratio_mutant', lambda timeout: dict(result='unknown', replayed=False)):
                unknown_control = mc.run()
            print(json.dumps(dict(missing_underflow=missing_underflow, too_tight=too_tight, unknown_control=unknown_control)))
        """)
        self.assertFalse(reports['missing_underflow']['obligations_passed'])
        underflow = next(r for r in reports['missing_underflow']['fp_replays'] if r['name'] == 'subnormal_quotient')
        self.assertFalse(underflow['replayed'])
        # Removing the underflow case merely narrows the model: the real lemmas
        # can still pass. The actual binary64 replay must prevent false success.
        self.assertTrue(all(r['passed'] for r in reports['missing_underflow']['obligations']))
        self.assertFalse(reports['too_tight']['obligations_passed'])
        ratio = next(r for r in reports['too_tight']['obligations'] if r['name'] == 'fp_ratio_envelope')
        self.assertEqual('sat', ratio['result'])
        self.assertFalse(reports['unknown_control']['obligations_passed'])
        self.assertTrue(all(r['passed'] for r in reports['unknown_control']['obligations']))

    def test_overflow_is_not_certified(self):
        refused = self.child("""
            import json, conservative_mc as mc
            largest = float.fromhex('0x1.fffffffffffffp+1023')
            try:
                mc.replay_binary64('overflow', -largest, largest, 0.0)
                refused = False
            except ValueError:
                refused = True
            print(json.dumps(refused))
        """)
        self.assertTrue(refused)

    def test_source_drift_is_rejected(self):
        report = self.child("""
            import json, pathlib
            from unittest.mock import patch
            import conservative_mc as mc
            read = pathlib.Path.read_text
            rejected = []
            for before, after in [('cell.f[i] < IsoValue', 'cell.f[i] > IsoValue'),
                                  ('initialize_cell_values_nohash(cell, false)', 'initialize_cell_values_grid(cell, false)'),
                                  ('double t = (IsoValue - fa) / gap;', 'double t = 0.5;'),
                                  ('Mesh.AppendTriangle(a, b, c)', 'Mesh.AppendTriangle(a, c, b)')]:
                def changed(path, *args, **kwargs):
                    return read(path, *args, **kwargs).replace(before, after)
                try:
                    with patch.object(pathlib.Path, 'read_text', changed):
                        mc.source_contracts()
                except ValueError:
                    rejected.append(before)
            print(json.dumps(rejected))
        """)
        self.assertEqual(4, len(report))

    def test_wrong_winding_breaks_high_cap(self):
        rejected = self.child("""
            import copy, json, conservative_mc as mc
            rows, _ = mc.source_contracts()
            rows = copy.deepcopy(rows)
            rows[1][0], rows[1][1] = rows[1][1], rows[1][0]
            try:
                mc.table_certificate(rows)
                rejected = False
            except ValueError:
                rejected = True
            print(json.dumps(rejected))
        """)
        self.assertTrue(rejected)

    def test_timeout_comes_from_the_environment(self):
        timeout = self.child('''
            import json, os
            os.environ['Z3_PILOT_TIMEOUT_MS'] = '1234'
            import conservative_mc as mc
            print(json.dumps(mc.DEFAULT_TIMEOUT_MS))
        ''')
        self.assertEqual(1234, timeout)

    def test_no_vacuous_or_unknown_success(self):
        report = self.child("""
            import json
            from unittest.mock import patch
            import conservative_mc as mc
            z = mc.z
            vacuous = mc.check('vacuous', [z.BoolVal(False)], z.BoolVal(False))
            class UnknownSolver:
                def set(self, **kwargs): pass
                def add(self, *args): pass
                def check(self): return z.unknown
            with patch.object(z, 'Solver', UnknownSolver):
                unknown = mc.check('unknown', [], z.BoolVal(False))
            print(json.dumps(dict(vacuous=vacuous, unknown=unknown)))
        """)
        self.assertFalse(report['vacuous']['passed'])
        self.assertFalse(report['unknown']['passed'])

    def test_direct_all_low_is_nonvacuous(self):
        row = self.child("""
            import json, conservative_mc as mc
            rows, _ = mc.source_contracts()
            print(json.dumps(mc.direct_query(255, rows[255], mc.DEFAULT_TIMEOUT_MS)))
        """)
        self.assertTrue(row['passed'], row)


if __name__ == '__main__':
    unittest.main()
