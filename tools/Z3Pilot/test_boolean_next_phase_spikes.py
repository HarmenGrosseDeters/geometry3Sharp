import unittest

import boolean_next_phase_spikes
from check_pilot_obligations import run_test


class BooleanNextPhaseSpikesTests(unittest.TestCase):
    def test_boolean_next_phase_spikes(self):
        run_test(self, boolean_next_phase_spikes.run, 'Z3_BOOLEAN_NEXT_PHASE_SPIKES_REPORT')

    def test_every_family_has_both_unsat_and_sat_checks(self):
        rows = boolean_next_phase_spikes.obligations()
        names = [r[0] for r in rows]
        self.assertEqual(len(names), len(set(names)))
        unsat_count = sum(1 for r in rows if r[3] == 'unsat')
        sat_count = sum(1 for r in rows if r[3] == 'sat')
        self.assertEqual(unsat_count, 10)
        self.assertEqual(sat_count, 20)


if __name__ == '__main__':
    unittest.main()
