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
        for source, anchors, before, after in (
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'const double MinNormal = 2.2250738585072014e-308;',
             'const double MinNormal = 0;'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'double oneMinusU = (r - p) / r;',
             'double oneMinusU = 1.0 - u;'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'if (d2 >= 4 * MinNormal && !double.IsInfinity(d2))',
             'if (d2 >= MinNormal && !double.IsInfinity(d2))'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'double halfLen = ExactInterval.Up(0.5 * len);',
             'double halfLen = 0.5 * len;'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'if (m == it.a || m == it.b) {',
             'if (false && (m == it.a || m == it.b)) {'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'double margin = Math.Max(1e-12 * diag, 64 * MathUtil.Epsilon * coordMax);',
             'double margin = 1e-12 * diag;'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'return MeshAlphaWrap.EstimateDensity * ((total / alpha) / alpha);',
             'return MeshAlphaWrap.EstimateDensity * total / alpha / alpha;'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'if (Math.Abs(dot) >= 4 * MinNormal && !double.IsInfinity(dot)) {',
             'if (!double.IsInfinity(dot) && !double.IsNaN(dot)) {'),
            (passage_wrap.SOURCE, passage_wrap.ANCHORS,
             'return SafeLength(qa - (proj * mq) * sab);',
             'return SafeLength(qa - (proj * ratio) * ab);'),
            (passage_wrap.MATHUTIL_SOURCE, passage_wrap.MATHUTIL_ANCHORS,
             'public const double Epsilon = 2.2204460492503131e-016;',
             'public const double Epsilon = 1e-15;'),
        ):
            with self.subTest(source=source, mutation=before), TemporaryDirectory() as directory:
                root = Path(directory)
                original = (ROOT / source).read_text(encoding='utf-8-sig')
                altered = original.replace(before, after, 1)
                self.assertNotEqual(original, altered)
                copy = root / source
                copy.parent.mkdir(parents=True)
                copy.write_text(altered, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source_contract(source, anchors, root=root)
