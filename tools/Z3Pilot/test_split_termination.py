import json
import math
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
        for name, sign in (('O2b_midpoint_can_be_plus_infinity_(finite_centers)', 1), ('O2b_midpoint_can_be_minus_infinity_(finite_centers)', -1)):
            w = by_name[name]['witness']
            a, b = float(w['a']), float(w['b'])
            self.assertTrue(a < b and math.isfinite(a) and math.isfinite(b))
            self.assertTrue(math.isinf((b + a) * 0.5) and (b + a) * 0.5 * sign > 0, name)

    def test_the_obligation_set(self):
        names = [r['name'] for r in self.report['obligations']]
        self.assertEqual(6, sum(n.startswith('O1_') for n in names))
        self.assertEqual(2, sum(n.startswith('O2b_') for n in names))
        self.assertEqual(len(set(names)), len(names))

    def test_the_source_contract_fails_on_drift(self):
        text = (st.ROOT / st.SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        interval = (st.ROOT / st.INTERVAL).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        st.source_contracts(text, interval)
        drifts = [
            ('if (n0 < 1 || n1 < 1) {', 'if (n0 < 1) {'),                                   # the guard of the empty side (before #451)
            ('interval.Center;', '(interval.a + interval.b) * 0.5;'),                       # the midpoint
            ('while (l < iCount && centers', 'while (centers'),                              # the left scan loses its bound
            ('while (r >= 0 && centers', 'while (centers'),                                  # the right scan loses its bound
            ('if ( iCount < minTriCount ) {', 'if ( iCount <= minTriCount ) {'),                 # the leaf test
            ('iStart, n0, depth + 1, minTriCount', 'iStart, n0 + 1, depth + 1, minTriCount'),            # the recursion on the left side
            ('iStart+n0, n1, depth + 1, minTriCount', 'iStart, n1, depth + 1, minTriCount'),             # and on the right side
            ('int leafLimit = Math.Max(TopDownLeafMaxTriCount, 2);', 'int leafLimit = TopDownLeafMaxTriCount;'),  # the leaf limit is not below 2
            (': split_tri_set_midpoint(triangles, centers, 0, count, 0, leafLimit,', ': split_tri_set_midpoint(triangles, centers, 0, count, 0, TopDownLeafMaxTriCount,'),
        ]
        for old, new in drifts:
            self.assertIn(old, text, old)
            with self.assertRaises(ValueError, msg=old):
                st.source_contracts(text.replace(old, new), interval)
        with self.assertRaises(ValueError):
            st.source_contracts(text, interval.replace('(b + a) * 0.5', '(b + a) / 2'))


if __name__ == '__main__':
    unittest.main()
