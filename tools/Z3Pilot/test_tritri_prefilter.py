import json
import os
import pathlib
import unittest

import tritri_prefilter

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
ROOT = pathlib.Path(__file__).resolve().parents[2]

# the code that the model describes (ExactMeshArrangement.apart_on_the_line and its use): a change to any of these
# lines must be checked against the model again
CONTRACT = [
    'bool proper = sT[0] != 0 && sT[1] != 0 && sT[2] != 0 && sU[0] != 0 && sU[1] != 0 && sU[2] != 0;',
    'int i = (sT[0] != sT[1] && sT[0] != sT[2]) ? 0 : (sT[1] != sT[0] && sT[1] != sT[2]) ? 1 : 2;',
    'int j = (sU[0] != sU[1] && sU[0] != sU[2]) ? 0 : (sU[1] != sU[0] && sU[1] != sU[2]) ? 1 : 2;',
    'Vector3d p1 = pick(t0, t1, t2, i), q1 = pick(t0, t1, t2, i + 1), r1 = pick(t0, t1, t2, i + 2);',
    'Vector3d p2 = pick(u0, u1, u2, j), q2 = pick(u0, u1, u2, j + 1), r2 = pick(u0, u1, u2, j + 2);',
    'if (sT[i] < 0) { Vector3d w = q2; q2 = r2; r2 = w; }',
    'if (sU[j] < 0) { Vector3d w = q1; q1 = r1; r1 = w; }',
    'return ExactPredicates.Orient3d(p1, q1, p2, q2) > 0 || ExactPredicates.Orient3d(p1, r1, r2, p2) > 0;',
]


class TriTriPrefilterTests(unittest.TestCase):
    def test_lemma_and_mutants(self):
        ok, report = tritri_prefilter.run(TIMEOUT_MS)
        out = os.environ.get('Z3_TRITRI_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(ok, report)
        self.assertEqual(14, len(report))

    def test_source_contract(self):
        text = (ROOT / 'mesh_ops' / 'ExactMeshArrangement.cs').read_text(encoding='utf-8')
        for line in CONTRACT:
            self.assertIn(line, text, 'apart_on_the_line changed: check it against tools/Z3Pilot/tritri_prefilter.py')


if __name__ == '__main__':
    unittest.main()
