"""Unit tests and source-contract drift checks for delaunay3_cospherical."""
from pathlib import Path
import tempfile
import unittest

import delaunay3_cospherical as pilot
from check_pilot_obligations import ROOT, run_test, source_contract


class Delaunay3CosphericalZ3Tests(unittest.TestCase):
    def test_obligations_and_replays(self):
        run_test(self, pilot.run, 'Z3_DELAUNAY3_COSPHERICAL_REPORT')

    def test_source_drift_is_rejected(self):
        cases = [
            ('return SafeDistance(a, b) <= minSeparation;',
             'return a.DistanceSquared(b) <= minSeparation * minSeparation;'),
            ('return a + ScaleVec(off, exp + pre);',
             'return a + off;'),
            ('if (ExactPredicates.Orient3d(a, b, d, p) >= 0) {',
             'if (ExactPredicates.Orient3d(a, b, d, p) > 0) {'),
            ('if (WithinSeparation(vertices[v], p, minSeparation)) return InsertResult.TooClose;',
             'if (vertices[v].DistanceSquared(p) <= minSeparation * minSeparation) return InsertResult.TooClose;'),
            ('if (!vset.Contains(cv[4 * c + i])) return InsertResult.Failed;',
             'if (vset.Contains(cv[4 * c + i])) return InsertResult.Failed;'),
            ('for (int k = nb; k < nt; ++k)',
             'for (int k = nt; k < nb; ++k)'),
        ]
        for old, new in cases:
            with self.subTest(old=old):
                with tempfile.TemporaryDirectory() as tmp:
                    dst = Path(tmp) / pilot.DT_SOURCE
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    text = (ROOT / pilot.DT_SOURCE).read_text(encoding='utf-8-sig')
                    self.assertIn(old, text)
                    dst.write_text(text.replace(old, new, 1), encoding='utf-8')
                    with self.assertRaises(ValueError):
                        source_contract(pilot.DT_SOURCE, pilot.DT_ANCHORS, root=Path(tmp))


if __name__ == '__main__':
    unittest.main()
