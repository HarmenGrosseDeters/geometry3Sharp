"""Check the AlphaWrapOracle pruning model and reject source drift."""
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

import alpha_wrap_oracle
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapOracleTests(TestCase):
    def test_oracle_obligations(self):
        run_test(self, alpha_wrap_oracle.run, 'Z3_ALPHA_WRAP_ORACLE_REPORT')

    def test_source_drift_invalidates_the_model(self):
        for before, after in (
            ('Gap2[f] = (l2 > 0 && m2 >= MinNormal && !double.IsInfinity(m2)) ? m2 * l2 : double.NaN;',
             'Gap2[f] = (l2 > 0) ? m2 * l2 : double.NaN;'),
            ('double slabSlack = 64 * MathUtil.Epsilon * (Math.Max(nd[node].Min.MaxAbs, nd[node].Max.MaxAbs) + t.MaxAbs);',
             'double slabSlack = 0;'),
            ('if (gap2 >= MinNormal && !double.IsInfinity(gap2)) {',
             'if (gap2 > 0 && !double.IsInfinity(gap2)) {'),
            ('if (m2 >= MinNormal && !double.IsInfinity(m2))\n                return dx * dx + dy * dy + dz * dz > m2;',
             'if (m2 > 0)\n                return dx * dx + dy * dy + dz * dz > m2;'),
            ('double gap2 = (m2 >= MinNormal && !double.IsInfinity(m2)) ? m2 * l2 : double.NaN;',
             'double gap2 = m2 * l2;'),
            ('double lo = ExactInterval.Down(mn - o), hi = ExactInterval.Up(mx - o);',
             'double lo = mn - o, hi = mx - o;'),
            ('double disc = B * B - A * C;', 'double disc = B * B + A * C;'),
        ):
            with self.subTest(mutation=before), TemporaryDirectory() as directory:
                root = Path(directory)
                original = (ROOT / alpha_wrap_oracle.SOURCE).read_text(encoding='utf-8-sig')
                altered = original.replace(before, after, 1)
                self.assertNotEqual(original, altered)
                copy = root / alpha_wrap_oracle.SOURCE
                copy.parent.mkdir(parents=True)
                copy.write_text(altered, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source_contract(alpha_wrap_oracle.SOURCE, alpha_wrap_oracle.ANCHORS, root=root)
