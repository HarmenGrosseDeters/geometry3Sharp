from intrinsic_flip import run
from pilot_obligations import PilotTest


class IntrinsicFlipTests(PilotTest):
    def test_integer_arithmetic(self):
        self.check_pilot(run, 'Z3_INTRINSIC_FLIP_REPORT')
