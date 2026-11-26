from intrinsic_flip import run
from check_pilot_obligations import run_test
from unittest import TestCase


class IntrinsicFlipTests(TestCase):
    def test_integer_arithmetic(self):
        run_test(self, run, 'Z3_INTRINSIC_FLIP_REPORT')
