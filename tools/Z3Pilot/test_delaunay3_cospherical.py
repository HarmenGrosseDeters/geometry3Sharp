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
        # 8 unique-context drift mutations covering all 6 distinct DT_ANCHORS (including CellVolume and plan_insert star-shape)
        cases = [
            ('return SafeDistance(a, b) <= minSeparation;',
             'return a.DistanceSquared(b) <= minSeparation * minSeparation;'),
            ('return ExactPoint3.ScaleB(us.Cross(vs).Dot(ws) / 6.0, ex + ey + ez + pu + pv + pw);',
             'return us.Cross(vs).Dot(ws) / 6.0;'),
            ('int shift = maxE - 1000;',
             'int shift = 0;'),
            ('if (n < 0)\n                                return InsertResult.Outside;',
             'if (n < 0)\n                                break;'),
            ('foreach (int v in boundaryVerts)\n                if (WithinSeparation(vertices[v], p, minSeparation)) return InsertResult.TooClose;\n\n            if (support) {',
             'foreach (int v in boundaryVerts)\n                if (vertices[v].DistanceSquared(p) <= minSeparation * minSeparation) return InsertResult.TooClose;\n\n            if (support) {'),
            ('if (n < 0) { outside = true; break; }\n                            inCavity.Add(n);',
             'if (n < 0) { outside = false; break; }\n                            inCavity.Add(n);'),
            ('if (!vset.Contains(cv[4 * c + i])) return InsertResult.Failed;\n            foreach (int v in boundaryVerts)\n                if (WithinSeparation(vertices[v], p, minSeparation)) return InsertResult.TooClose;',
             'if (!vset.Contains(cv[4 * c + i])) return InsertResult.Failed;\n            foreach (int v in boundaryVerts)\n                if (vertices[v].DistanceSquared(p) <= minSeparation * minSeparation) return InsertResult.TooClose;'),
            ('for (int k = nb; k < nt; ++k)\n                free_cell(plan.Cavity[k]);',
             'for (int k = nt; k < nb; ++k)\n                free_cell(plan.Cavity[k]);'),
        ]
        for old, new in cases:
            with self.subTest(old=old[:50]):
                with tempfile.TemporaryDirectory() as tmp:
                    dst = Path(tmp) / pilot.DT_SOURCE
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    text = (ROOT / pilot.DT_SOURCE).read_text(encoding='utf-8-sig')
                    self.assertEqual(text.count(old), 1, f'expected unique match for {old!r}')
                    dst.write_text(text.replace(old, new, 1), encoding='utf-8')
                    with self.assertRaises(ValueError):
                        source_contract(pilot.DT_SOURCE, pilot.DT_ANCHORS, root=Path(tmp))


if __name__ == '__main__':
    unittest.main()
