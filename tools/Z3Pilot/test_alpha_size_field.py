"""Check the AlphaSizeField model and reject source drift."""
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

import alpha_size_field
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaSizeFieldTests(TestCase):
    def test_size_field_obligations(self):
        run_test(self, alpha_size_field.run, 'Z3_ALPHA_SIZE_FIELD_REPORT')

    def test_source_drift_invalidates_the_model(self):
        for before, after in (
            ('radius = (alpha0 > alphaMin && slope > 0) ? (alpha0 - alphaMin) / slope : 0;',
             'radius = (alpha0 - alphaMin) / slope;'),
            ('return coordinate == 0 ? 0 : BitConverter.DoubleToInt64Bits(coordinate);',
             'return BitConverter.DoubleToInt64Bits(coordinate);'),
            ('if (d2 >= MinNormal && !double.IsInfinity(d2))',
             'if (d2 > 0)'),
            ('double lower = ExactInterval.Down(minAlpha + ExactInterval.Down(slope * boxDist));',
             'double lower = minAlpha + slope * boxDist;'),
        ):
            with self.subTest(mutation=before), TemporaryDirectory() as directory:
                root = Path(directory)
                original = (ROOT / alpha_size_field.SOURCE).read_text(encoding='utf-8-sig')
                altered = original.replace(before, after, 1)
                self.assertNotEqual(original, altered)
                copy = root / alpha_size_field.SOURCE
                copy.parent.mkdir(parents=True)
                copy.write_text(altered, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source_contract(alpha_size_field.SOURCE, alpha_size_field.ANCHORS, root=root)
