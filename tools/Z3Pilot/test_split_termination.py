import json
import os
import pathlib
import unittest

import split_termination as st


class SplitTerminationTests(unittest.TestCase):
    """Pilot 4 (QA 2.0 T14): the termination of the midpoint split of DMeshAABBTree3's build (split_termination.py)."""

    @classmethod
    def setUpClass(cls):
        cls.report = st.run(int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000')))
        out = os.environ.get('Z3_SPLIT_TERMINATION_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(cls.report, indent=2) + '\n')

    def test_obligations_hold_and_are_not_vacuous(self):
        self.assertTrue(self.report['passed'], json.dumps(self.report, indent=2))
        for r in self.report['obligations']:
            self.assertEqual(r['expected'], r['result'], r['name'])
            self.assertEqual('sat', r['assumptions_result'], r['name'])

    def test_the_midpoint_witnesses_replay_in_plain_floats(self):
        by_name = {r['name']: r for r in self.report['obligations']}
        onto_b = by_name['O2_midpoint_can_round_onto_b_(the_case_of_the_guard)']['witness']
        a, b = float(onto_b['a']), float(onto_b['b'])
        self.assertTrue(a < b and (b + a) * 0.5 == b)
        onto_a = by_name['O2_midpoint_can_round_onto_a']['witness']
        a, b = float(onto_a['a']), float(onto_a['b'])
        self.assertTrue(a < b and (b + a) * 0.5 == a)
        over = by_name['mutant_midpoint_of_unbounded_centers_overflows']['witness']
        a, b = float(over['a']), float(over['b'])
        self.assertTrue(a < b and (b + a) * 0.5 == float('inf'))

    def test_the_obligation_set(self):
        names = [r['name'] for r in self.report['obligations']]
        self.assertEqual(6, sum(n.startswith('O1_') for n in names))
        self.assertEqual(len(set(names)), len(names))

    def test_the_source_contract_fails_on_drift(self):
        text = (st.ROOT / st.SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        interval = (st.ROOT / st.INTERVAL).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        st.source_contracts(text, interval)
        with self.assertRaises(ValueError):    # the guard of the empty side is removed (the code before #451)
            st.source_contracts(text.replace('if (n0 < 1 || n1 < 1) {', 'if (n0 < 1) {'), interval)
        with self.assertRaises(ValueError):    # the midpoint changes
            st.source_contracts(text.replace('interval.Center;', '(interval.a + interval.b) * 0.5;'), interval)
        with self.assertRaises(ValueError):
            st.source_contracts(text, interval.replace('(b + a) * 0.5', '(b + a) / 2'))
        with self.assertRaises(ValueError):    # the left scan loses its bound
            st.source_contracts(text.replace('while (l < iCount && centers', 'while (centers'), interval)


if __name__ == '__main__':
    unittest.main()
