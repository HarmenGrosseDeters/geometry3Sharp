import json
import unittest
from fractions import Fraction

import compare_orient3d as c


class CompareOrient3dTests(unittest.TestCase):
    def test_source_contract(self):
        self.assertEqual([], c.source_contract(), 'Orient3dFilter changed: re-check compare_orient3d.MODEL')

    def test_model_file_is_the_model_of_the_witness(self):
        self.assertEqual(c.model(), c.MODEL_FILE.read_text())

    def test_exact_replay_four_u_insufficient_seven_u_sufficient(self):
        r = c.exact_replay()
        self.assertTrue(r['four_u_insufficient'], r)
        self.assertTrue(r['seven_u_sufficient'], r)
        self.assertGreater(r['error_over_u_perm_plus_det'], 4.25)

    def test_permanent_lower_bound_is_below_the_witness_permanent(self):
        lo = c.permanent_lower_bound()
        perm = Fraction(c.exact_replay()['permanent'])
        self.assertTrue(lo <= perm and lo > perm * (1 - Fraction(1, 2**20)), (float(lo), float(perm)))

    def test_compare_rejects_a_bound_above_the_constant(self):
        lo = c.permanent_lower_bound()
        self.assertTrue(c.compare(c.K * lo)['ok'])
        self.assertFalse(c.compare(c.K * lo * (1 + Fraction(1, 2**40)))['ok'])

    def test_checked_in_result(self):
        if not c.RESULT_FILE.exists():
            self.skipTest('no FPTaylor run yet (orient3d_stagea_result.json): the github.com archives of install.sh were '
                          'blocked in the session that wrote the model; run compare_orient3d.py <fptaylor_dir>')
        r = json.loads(c.RESULT_FILE.read_text())
        again = c.compare(Fraction(r['fptaylor_bound_exact']))
        self.assertEqual(again['ok'], r['ok'])
        self.assertTrue(r['ok'], r)
        self.assertTrue(r['replay']['four_u_insufficient'])


if __name__ == '__main__':
    unittest.main()
