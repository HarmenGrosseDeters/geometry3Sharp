from check_pilot_obligations import run_test
from unittest import TestCase
from saturating_conversions import run


class SaturatingConversionTests(TestCase):
    def test_branch_equivalence_and_grid_neighbor(self):
        run_test(self, run, 'Z3_SATURATING_CONVERSIONS_REPORT')
