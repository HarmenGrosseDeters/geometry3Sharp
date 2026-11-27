"""Unit tests and source-contract drift checks for alpha_wrap_estimate."""
from pathlib import Path
import tempfile
import unittest

import alpha_wrap_estimate as pilot
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapEstimateTests(unittest.TestCase):
    def test_obligations_and_replays(self):
        run_test(self, pilot.run, 'Z3_ALPHA_WRAP_ESTIMATE_REPORT')

    def test_source_drift_is_rejected(self):
        cases = [
            (pilot.ESTIMATE_SOURCE, pilot.ESTIMATE_ANCHORS,
             'const double SafeGridLimit = 1e18;',
             'const double SafeGridLimit = 9e18;'),
            (pilot.ESTIMATE_SOURCE, pilot.ESTIMATE_ANCHORS,
             'exactKeys = !(workCell > 0) || (2.0 / workCell > SafeGridLimit);',
             'exactKeys = false;'),
            (pilot.ESTIMATE_SOURCE, pilot.ESTIMATE_ANCHORS,
             '2 * Math.Abs(volume[root]) / area[root] < ThinFactor * workAlpha',
             '2 * Math.Abs(volume[root]) / area[root] < ThinFactor * alpha'),
            (pilot.ESTIMATE_SOURCE, pilot.ESTIMATE_ANCHORS,
             'var key = (NormBits(p.x), NormBits(p.y), NormBits(p.z));',
             'var key = (0L, 0L, 0L);'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'if (coarse / alpha > cap)',
             'if (coarse / alpha < cap)'),
        ]
        for rel, anchors, old, new in cases:
            with self.subTest(rel=rel, old=old):
                with tempfile.TemporaryDirectory() as tmp:
                    dst = Path(tmp) / rel
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    text = (ROOT / rel).read_text(encoding='utf-8-sig')
                    self.assertIn(old, text)
                    dst.write_text(text.replace(old, new, 1), encoding='utf-8')
                    with self.assertRaises(ValueError):
                        source_contract(rel, anchors, root=Path(tmp))


if __name__ == '__main__':
    unittest.main()
