import json
import math
import os
import pathlib
import unittest

import step_loops as sl


class StepLoopsTests(unittest.TestCase):
    """Pilot 5 (QA 2.0 T14): the step loops of MeshRoundingRepair terminate (step_loops.py, the class of #452)."""

    @classmethod
    def setUpClass(cls):
        cls.report = sl.run(int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000')))
        out = os.environ.get('Z3_STEP_LOOPS_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(cls.report, indent=2) + '\n')

    def test_obligations_hold_and_are_not_vacuous(self):
        self.assertTrue(self.report['passed'], json.dumps(self.report, indent=2))
        for r in self.report['obligations']:
            self.assertEqual(r['expected'], r['result'], r['name'])
            self.assertEqual('sat', r['assumptions_result'], r['name'])

    def test_the_mutant_witnesses_replay(self):
        by_name = {r['name']: r for r in self.report['obligations']}
        zero = by_name['mutant_P1_with_a_start_of_0_(the_code_before_#452)']['witness']
        self.assertTrue(float(zero['s']) == 0 and zero['stuck'])      # the loop `s <= bound; s *= 4` never advances
        s = float(zero['s'])
        self.assertTrue(s * 4 == s and s <= float(zero['bound']))
        infinite = by_name['mutant_P1_with_an_infinite_bound_(no_guard_on_maxLengthC)']['witness']
        s, bound = float(infinite['s']), float(infinite['bound'])
        self.assertTrue(math.isinf(s) and math.isinf(bound) and s <= bound and s * 4 == s and infinite['stuck'])

    def test_the_progress_claims_replay_on_the_edge_values(self):
        for f in (4, 16):
            for s in (5e-324, 1e-310, 2.2250738585072014e-308, 1.0, 1e300, 1.7976931348623157e308 / 8):
                self.assertTrue(s * f > s)
        self.assertEqual(float('inf'), 1.7976931348623157e308 * 4)

    def test_the_source_contract_fails_on_drift(self):
        text = (sl.ROOT / sl.SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        sl.source_contracts(text)
        with self.assertRaises(ValueError):    # the guard against a start of 0 is removed (the code before #452)
            sl.source_contracts(text.replace('if (!(s0 > 0)) {', 'if (false) {'))
        with self.assertRaises(ValueError):
            sl.source_contracts(text.replace('if (!(s0 > 0 && s0 < double.PositiveInfinity))', 'if (s0 < 0)'))
        with self.assertRaises(ValueError):    # the bound may be +infinity: the guard of maxLengthC is deleted
            sl.source_contracts(text.replace('if (!(ml < double.PositiveInfinity))\n                    ml = double.NaN;', ''))
        with self.assertRaises(ValueError):    # a new step loop that the model does not know
            sl.source_contracts(text.replace('for (double s = s0; s <= bound; s *= 4)', 'for (double s = s0; s <= bound; s *= 2)', 1))
        with self.assertRaises(ValueError):    # ulp changes
            sl.source_contracts(text.replace('e > 52 ? BitConverter', 'e > 51 ? BitConverter'))


if __name__ == '__main__':
    unittest.main()
