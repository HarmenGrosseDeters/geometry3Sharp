import json
import os
import pathlib
import unittest

import separating_axis

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
ROOT = pathlib.Path(__file__).resolve().parents[2]

# the prototype the model describes (a benchmark tool, not library code): a change to these lines must be checked
# against the model again
CONTRACT = [
    'const double Gamma = 8 * 1.1102230246251565e-16;',
    'return Math.Abs(a.x * x.x) + Math.Abs(a.y * x.y) + Math.Abs(a.z * x.z);',
    'double m = Gamma * Math.Max(absdot(a, u.V0), Math.Max(absdot(a, u.V1), absdot(a, u.V2)));',
    'if (Math.Min(p0, Math.Min(p1, p2)) - m > hi[k] || Math.Max(p0, Math.Max(p1, p2)) + m < lo[k])',
]


class SeparatingAxisTests(unittest.TestCase):
    def test_lemma_and_mutants(self):
        ok, report = separating_axis.run(TIMEOUT_MS)
        out = os.environ.get('Z3_SEPARATING_AXIS_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(ok, report)
        self.assertEqual(14, len(report))

    def test_unit_roundoff_and_margin(self):
        self.assertEqual(separating_axis.U, 2.0 ** -53)
        self.assertEqual(float(separating_axis.GAMMA), 8 * 1.1102230246251565e-16)

    def test_source_contract(self):
        text = (ROOT / 'benchmarks' / 'SnapRoundingExperiment' / 'CandidateCulling.cs').read_text(encoding='utf-8')
        for line in CONTRACT:
            self.assertIn(line, text, 'the prototype changed: check it against tools/Z3Pilot/separating_axis.py')


if __name__ == '__main__':
    unittest.main()
