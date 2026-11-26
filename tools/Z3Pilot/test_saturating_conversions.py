from pilot_obligations import PilotTest
from saturating_conversions import run


class SaturatingConversionTests(PilotTest):
    def test_branch_equivalence_and_grid_neighbor(self):
        self.check_pilot(run, 'Z3_SATURATING_CONVERSIONS_REPORT')
