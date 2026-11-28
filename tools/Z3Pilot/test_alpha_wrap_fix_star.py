"""Unit tests and source-contract drift checks for alpha_wrap_fix_star."""
from pathlib import Path
import tempfile
import unittest

import alpha_wrap_fix_star as pilot
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapFixStarZ3Tests(unittest.TestCase):
    def test_obligations_and_replays(self):
        run_test(self, pilot.run, 'Z3_ALPHA_WRAP_FIX_STAR_REPORT')

    def test_source_drift_is_rejected(self):
        cases = [
            ('if (nIn > 1 && !ReferenceFixStar && (outsideComps == 1 || allExterior)) {',
             'if (nIn > 1 && !ReferenceFixStar) {'),
            ('return a.K < b.K;', 'return a.K > b.K;'),
            ('else if (root != firstIn) joins = 1;', 'else if (root == firstIn) joins = 1;'),
            ('if (now.Corner != top.Corner || now.Joins != top.Joins)', 'if (false)'),
            ('nIn = nIn + 1 - distinct;', 'nIn = nIn - distinct;'),
            ('treeParent[k] = -1;', 'treeParent[k] = k;'),
            ('treeParent[mk] = u; treeMark[mk] = attMark; q.Add(mk);',
             'treeParent[u] = mk; treeMark[mk] = attMark; q.Add(mk);'),
            ('if (nn == 1) return true;', 'if (nn == 1) return false;'),
            ('return volumeA < volumeB;', 'return volumeA <= volumeB;'),
            ('if (m < 0) { compExterior[id] = true; continue; }',
             'if (m < 0) { continue; }'),
        ]
        for old, new in cases:
            with self.subTest(old=old):
                with tempfile.TemporaryDirectory() as tmp:
                    dst = Path(tmp) / pilot.WRAP_SOURCE
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    text = (ROOT / pilot.WRAP_SOURCE).read_text(encoding='utf-8-sig')
                    self.assertIn(old, text)
                    dst.write_text(text.replace(old, new, 1), encoding='utf-8')
                    with self.assertRaises(ValueError):
                        source_contract(pilot.WRAP_SOURCE, pilot.FIX_STAR_ANCHORS, root=Path(tmp))


if __name__ == '__main__':
    unittest.main()
