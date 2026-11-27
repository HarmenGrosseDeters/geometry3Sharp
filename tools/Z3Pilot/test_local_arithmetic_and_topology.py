from pathlib import Path
import tempfile
import unittest

import limb_negation
import ray_second_point
import tetrahedra_shared_faces
from check_pilot_obligations import ROOT, check, run_test, source


class LocalArithmeticAndTopologyTests(unittest.TestCase):
    def test_a_ground_expression_belongs_in_a_replay(self):
        with self.assertRaisesRegex(ValueError, 'ground expressions'):
            check('constant', 'QF_LIA', '(declare-const unrelated Int)', '(= 1 1)', 'sat', 100)

    def test_limb_negation(self):
        run_test(self, limb_negation.run, 'Z3_LIMB_NEGATION_REPORT')

    def test_unchanged_carry_source_contract(self):
        module = limb_negation
        original = (ROOT / module.SOURCE).read_text(encoding='utf-8')
        changed = original.replace('(carry != 0 && t == 0)', '(t == 0)')
        self.assertNotEqual(original, changed)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = root / module.SOURCE
            path.parent.mkdir()
            path.write_text(changed, encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                source(module.SOURCE, *module.CONTRACTS[0], root=root)

    def test_tetrahedra_shared_faces(self):
        run_test(self, tetrahedra_shared_faces.run, 'Z3_TETRAHEDRA_SHARED_FACES_REPORT')

    def test_second_point(self):
        run_test(self, ray_second_point.run, 'Z3_RAY_SECOND_POINT_REPORT')
