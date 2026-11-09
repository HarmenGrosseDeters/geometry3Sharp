import json
import os
import pathlib
import unittest
from fractions import Fraction

import mesher_checks as mc


class MesherChecksTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = mc.run()
        out = os.environ.get('Z3_MESHER_CHECKS_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(cls.report, indent=2) + '\n')

    def test_all_obligations_hold_and_are_not_vacuous(self):
        self.assertTrue(self.report['passed'], json.dumps(self.report, indent=2))
        for r in self.report['obligations']:
            self.assertEqual(r['expected'], r['result'], r['name'])
            self.assertEqual('sat', r['assumptions_result'], r['name'])

    def test_mutants_are_sat(self):
        by_name = {r['name']: r for r in self.report['obligations']}
        self.assertEqual('sat', by_name['order_margin_2^-52']['result'])
        self.assertEqual('sat', by_name['S3_mutant_without_the_lower_range_guard']['result'])

    def test_reported_results_are_present_and_not_unknown(self):
        names = {r['name']: r['result'] for r in self.report['reports']}
        self.assertEqual(3, len(names), names)
        for n, r in names.items():
            self.assertIn(r, ('sat', 'unsat'), n)
        # the derivation gives (1 + u)^4 - 1, which is above 2^-51 (so 2^-51 is not proved by it) and below 2^-50
        self.assertGreater(mc.c_pow(4), Fraction(1, 2**51))
        self.assertLess(mc.c_pow(4), Fraction(1, 2**50))

    def test_the_source_contract_fails_on_drift(self):
        text = (mc.ROOT / mc.SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        mc.source_contracts(text)
        with self.assertRaises(ValueError):
            mc.source_contracts(text.replace('1.0 / (1L << 40)', '1.0 / (1L << 39)'))
        with self.assertRaises(ValueError):
            mc.source_contracts(text.replace('l0 > 1e-280', 'l0 > 1e-300'))

    def test_the_replay_of_the_order_counterexample(self):
        # margin 2^-52 with a relative error of 2^-50 per length: the computed order can be wrong; replay a witness in exact fractions
        eps, m = Fraction(1, 2**50), Fraction(1, 2**52)
        La, Lb = Fraction(1), Fraction(1) - Fraction(1, 2**51)    # La > Lb exactly
        la, lb = La * (1 - eps), Lb * (1 + eps)                   # computed la < lb
        self.assertTrue(la <= lb and lb - la > m * lb and La >= Lb)


if __name__ == '__main__':
    unittest.main()
