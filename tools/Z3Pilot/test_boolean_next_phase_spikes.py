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

        families = {
            'deg4_radial': lambda n: n.startswith('deg4_') or n.startswith('deg5_'),
            'moments': lambda n: n.startswith('moment_'),
            'shared3': lambda n: n.startswith('shared'),
            'sphere_obb': lambda n: n.startswith('sphere_') or n.startswith('obb_'),
        }
        expected_counts = {
            'deg4_radial': (2, 10),
            'moments': (4, 4),
            'shared3': (1, 3),
            'sphere_obb': (3, 6),
        }
        matched = 0
        for fam, pred in families.items():
            frows = [r for r in rows if pred(r[0])]
            matched += len(frows)
            unsat_n = sum(1 for r in frows if r[3] == 'unsat')
            sat_n = sum(1 for r in frows if r[3] == 'sat')
            self.assertEqual((unsat_n, sat_n), expected_counts[fam], fam)
        self.assertEqual(matched, len(rows))


if __name__ == '__main__':
    unittest.main()
