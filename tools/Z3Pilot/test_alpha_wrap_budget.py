"""Check the Alpha Wrap budget model and reject source drift."""
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

import alpha_wrap_budget
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapBudgetTests(TestCase):
    def test_wrap_budget_obligations(self):
        run_test(self, alpha_wrap_budget.run, 'Z3_ALPHA_WRAP_BUDGET_REPORT')

    def test_source_drift_invalidates_the_model(self):
        for path, anchors, before, after in (
            (alpha_wrap_budget.SOURCE, alpha_wrap_budget.ANCHORS, 'if (coarse / alpha > cap)', 'if (coarse / alpha >= cap)'),
            (alpha_wrap_budget.SOURCE, alpha_wrap_budget.ANCHORS, 'public const double EstimateDensity = 3.0;',
             'public const double EstimateDensity = 4.0;'),
            (alpha_wrap_budget.SOURCE, alpha_wrap_budget.ANCHORS, 'double effectiveArea = area + terms.OpenArea;',
             'double effectiveArea = 2 * (area + terms.OpenArea);'),
            (alpha_wrap_budget.SOURCE, alpha_wrap_budget.ANCHORS, 'offsetMantissa * coarseMantissa, alphaMantissa',
             'offsetMantissa * alphaMantissa, coarseMantissa'),
            (alpha_wrap_budget.SOURCE, alpha_wrap_budget.ANCHORS, 'const double RangeMin = 2.9387358770557188e-39',
             'const double RangeMin = 1e-38'),
            (alpha_wrap_budget.SOURCE, alpha_wrap_budget.ANCHORS, '1e-3 * Math.Max(diag, offset)',
             '2e-3 * Math.Max(diag, offset)'),
            (alpha_wrap_budget.TERMS_SOURCE, alpha_wrap_budget.TERMS_ANCHORS,
             'public const double WeldCells = 8.0;', 'public const double WeldCells = 16.0;'),
            (alpha_wrap_budget.TERMS_SOURCE, alpha_wrap_budget.TERMS_ANCHORS,
             'long kx = WideCellKey(p.x, x, 1, ref exactAxes);', 'long kx = WideCellKey(p.x, x, 2, ref exactAxes);'),
            (alpha_wrap_budget.TERMS_SOURCE, alpha_wrap_budget.TERMS_ANCHORS,
             'pos[corner[i]] = tris[i];', 'pos[corner[i]] = 2 * tris[i];'),
            (alpha_wrap_budget.TERMS_SOURCE, alpha_wrap_budget.TERMS_ANCHORS,
             'terms.OpenArea += area[root];', 'terms.OpenArea += 2 * area[root];'),
            (alpha_wrap_budget.TERMS_SOURCE, alpha_wrap_budget.TERMS_ANCHORS,
             'Math.Abs(index) <= CellIndexLimit', 'Math.Abs(index) < CellIndexLimit'),
        ):
            with self.subTest(path=path, mutation=before), TemporaryDirectory() as directory:
                root = Path(directory)
                original = (ROOT / path).read_text(encoding='utf-8-sig')
                altered = original.replace(before, after, 1)
                self.assertNotEqual(original, altered)
                copy = root / path
                copy.parent.mkdir(parents=True)
                copy.write_text(altered, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source_contract(path, anchors, root=root)
