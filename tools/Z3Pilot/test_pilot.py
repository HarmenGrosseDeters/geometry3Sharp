import json
import os
import pathlib
import unittest
from unittest.mock import patch
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


if __name__ == '__main__':
    unittest.main()
