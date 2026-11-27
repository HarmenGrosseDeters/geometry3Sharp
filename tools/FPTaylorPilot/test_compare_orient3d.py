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

    def test_box_file_is_the_box_of_the_witness(self):
        self.assertEqual(c.box(), json.loads(c.BOX_FILE.read_text()))

    def test_checked_in_symbolic_taylor_result(self):
        # the C# Symbolic Taylor probe (benchmarks/SymbolicTaylorBoundsProbe, a box-max consistency check): within the
        # library's (7 + 56 u) u * permanent on the box, and 4 u below its sampled first-order error
        r = c.compare_symtaylor(json.loads(c.SYMTAYLOR_FILE.read_text()))
        for mode in ('sign_filter', 'with_final_rounding'):
            self.assertTrue(r[mode]['within_library_bound'], r)
            self.assertTrue(r[mode]['four_u_below_sampled_error'], r)
            self.assertLess(r[mode]['bound_over_u_permanent'], 7)
            self.assertGreater(r[mode]['bound_over_u_permanent'], 5.9)

    def test_symtaylor_result_is_of_the_current_probe_model(self):
        # a change of PredicateModels.BuildOrient3dStageA without a new run (compare_orient3d.py --symbolic-taylor) leaves a stale result
        stored = json.loads(c.SYMTAYLOR_FILE.read_text()).get('model_sha256')
        self.assertEqual(c.probe_model_hash(), stored, 'stale result: re-run compare_orient3d.py --symbolic-taylor')

    def test_probe_model_hash_follows_the_model_text(self):
        h = c.probe_model_hash()
        text = c.PROBE_MODELS.read_text(encoding='utf-8')
        changed = text.replace('int t3 = g.Mul(wz, g.Sub(uxvy, uyvx));', 'int t3 = g.Mul(wz, g.Sub(uyvx, uxvy));', 1)
        self.assertNotEqual(text, changed)
        orig = c.PROBE_MODELS
        import tempfile, pathlib
        with tempfile.TemporaryDirectory() as d:
            c.PROBE_MODELS = pathlib.Path(d) / 'PredicateModels.cs'
            c.PROBE_MODELS.write_text(changed, encoding='utf-8')
            try:
                self.assertNotEqual(h, c.probe_model_hash())
            finally:
                c.PROBE_MODELS = orig

    def test_compare_symtaylor_rejects_a_bound_above_the_constant(self):
        lo = c.permanent_lower_bound()
        big = repr(float(c.K * lo * (1 + Fraction(1, 2**30))))
        r = c.compare_symtaylor({m: dict(refined_bound=big, sampled_lower=big) for m in ('sign_filter', 'with_final_rounding')})
        self.assertFalse(r['sign_filter']['within_library_bound'])

    def test_checked_in_fptaylor_result(self):
        # optional cross-check with OCaml FPTaylor, run locally (README)
        if not c.RESULT_FILE.exists():
            self.skipTest('optional OCaml FPTaylor cross-check not run (orient3d_stagea_result.json); the C# Symbolic '
                          'Taylor result above is the checked one; run compare_orient3d.py <fptaylor_dir>')
        r = json.loads(c.RESULT_FILE.read_text())
        again = c.compare(Fraction(r['fptaylor_bound_exact']))
        self.assertEqual(again['ok'], r['ok'])
        self.assertTrue(r['ok'], r)
        self.assertTrue(r['replay']['four_u_insufficient'])


if __name__ == '__main__':
    unittest.main()
