"""Check the PassageWrap model and reject source drift."""
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

import passage_wrap
from check_pilot_obligations import ROOT, run_test, source_contract


class PassageWrapTests(TestCase):
    def test_passage_wrap_obligations(self):
        run_test(self, passage_wrap.run, 'Z3_PASSAGE_WRAP_REPORT')

    def test_source_drift_invalidates_the_model(self):
        for before, after in (
            ('const double MinNormal = 2.2250738585072014e-308;',
             'const double MinNormal = 0;'),
            ('double oneMinusU = (r - p) / r;',
             'double oneMinusU = 1.0 - u;'),
            ('double halfLen = ExactInterval.Up(0.5 * len);',
             'double halfLen = 0.5 * len;'),
            ('if (m == it.a || m == it.b) {',
             'if (false && (m == it.a || m == it.b)) {'),
            ('double margin = Math.Max(1e-12 * diag, 64 * MathUtil.Epsilon * coordMax);',
             'double margin = 1e-12 * diag;'),
            ('return MeshAlphaWrap.EstimateDensity * ((total / alpha) / alpha);',
             'return MeshAlphaWrap.EstimateDensity * total / alpha / alpha;'),
            ('if (len2 >= MinNormal && !double.IsInfinity(len2)) {',
             'if (len2 > 0) {'),
        ):
            with self.subTest(mutation=before), TemporaryDirectory() as directory:
                root = Path(directory)
                original = (ROOT / passage_wrap.SOURCE).read_text(encoding='utf-8-sig')
                altered = original.replace(before, after, 1)
                self.assertNotEqual(original, altered)
                copy = root / passage_wrap.SOURCE
                copy.parent.mkdir(parents=True)
                copy.write_text(altered, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source_contract(passage_wrap.SOURCE, passage_wrap.ANCHORS, root=root)
