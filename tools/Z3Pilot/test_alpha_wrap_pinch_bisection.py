"""Unit tests and source-contract drift checks for alpha_wrap_pinch_bisection."""
from pathlib import Path
import tempfile
import unittest

import alpha_wrap_pinch_bisection as pilot
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapPinchBisectionZ3Tests(unittest.TestCase):
    def test_obligations_and_replays(self):
        run_test(self, pilot.run, 'Z3_ALPHA_WRAP_PINCH_BISECTION_REPORT')

    def test_source_drift_is_rejected(self):
        # 8 unique-context drift mutations covering all 6 WRAP_ANCHORS and both ORACLE_ANCHORS
        cases = [
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'internal const double PullMaxOffsets = 2.0;',
             'internal const double PullMaxOffsets = 4.0;'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'for (int k = 0; k < 60; ++k) {\n                double mid = 0.5 * (lo + hi);',
             'for (int k = 0; k < 30; ++k) {\n                double mid = 0.5 * (lo + hi);'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'else if (trial == WrapStatus.Success && pinch.Count == 0 && local) {\n                    confirmTrials++;',
             'else if (trial == WrapStatus.Success && pinch.Count == 0 && !local) {\n                    confirmTrials++;'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'if (LocalPinchTrials && changedSinceTrial != null && !full) {',
             'if (LocalPinchTrials && changedSinceTrial != null) {'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'if (PullPinchPoints && pulled_pinch_point(c, cc, depth, out p)) {',
             'if (pulled_pinch_point(c, cc, depth, out p)) {'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'if (!(dx >= offset && dx <= PullMaxOffsets * offset))',
             'if (!(dx >= offset))'),
            (pilot.ORACLE_SOURCE, pilot.ORACLE_ANCHORS,
             'if (box_entry(0, ref sg, r, 1) < 0)\n                return -1;',
             'if (box_entry(0, ref sg, r, 1) <= 0)\n                return -1;'),
            (pilot.ORACLE_SOURCE, pilot.ORACLE_ANCHORS,
             'double disc = B * B - A * C;\n            if (disc < 0) return -1;\n            double t = (-B - Math.Sqrt(disc)) / A;\n            return (t >= 0 && t <= tmax) ? t : -1;',
             'double disc = B * B - A * C;\n            if (disc <= 0) return -1;\n            double t = (-B - Math.Sqrt(disc)) / A;\n            return (t >= 0 && t <= tmax) ? t : -1;'),
        ]
        for src, anchors, old, new in cases:
            with self.subTest(src=src, old=old[:45]):
                with tempfile.TemporaryDirectory() as tmp:
                    dst = Path(tmp) / src
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    text = (ROOT / src).read_text(encoding='utf-8-sig')
                    self.assertEqual(text.count(old), 1, f'expected unique match in {src} for {old!r}')
                    dst.write_text(text.replace(old, new, 1), encoding='utf-8')
                    with self.assertRaises(ValueError):
                        source_contract(src, anchors, root=Path(tmp))


if __name__ == '__main__':
    unittest.main()
