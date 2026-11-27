"""Unit tests and source-contract drift checks for alpha_wrap_passes."""
from pathlib import Path
import tempfile
import unittest

import alpha_wrap_passes as pilot
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapPassesTests(unittest.TestCase):
    def test_obligations_and_replays(self):
        run_test(self, pilot.run, 'Z3_ALPHA_WRAP_PASSES_REPORT')

    def test_source_drift_is_rejected(self):
        cases = [
            (pilot.FEATURE_SOURCE, pilot.FEATURE_ANCHORS,
             'Vector3d dir = (n1 + n2) / Math.Max(1 + c, 1e-12);',
             'Vector3d dir = (n1 + n2) / (1 + c);'),
            (pilot.FEATURE_SOURCE, pilot.FEATURE_ANCHORS,
             'return x.IsFinite;',
             'return MathUtil.IsFinite(x.x + x.y + x.z);'),
            (pilot.FEATURE_SOURCE, pilot.FEATURE_ANCHORS,
             'double lens = SafeLength(n);',
             'double lens = n.Length;'),
            (pilot.FEATURE_SOURCE, pilot.FEATURE_ANCHORS,
             'const int CellLimit = 1 << 28;',
             'const int CellLimit = 1 << 30;'),
            (pilot.FEATURE_SOURCE, pilot.FEATURE_ANCHORS,
             'return ExactPoint3.ScaleB(SafeLength(sqa - proj * sab), eq);',
             'return SafeLength(qa);'),
            (pilot.QUALITY_SOURCE, pilot.QUALITY_ANCHORS,
             'if (f >= 66) return 66;',
             'if (f >= 65) return 65;'),
            (pilot.QUALITY_SOURCE, pilot.QUALITY_ANCHORS,
             'return Math.Sqrt(Math.Max(0, d2)) <= sr;',
             'return d2 <= sr * sr;'),
            (pilot.QUALITY_SOURCE, pilot.QUALITY_ANCHORS,
             'Vector3d us = ScaleVec(u, -Pow2Exp(su));',
             'Vector3d us = u;'),
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
