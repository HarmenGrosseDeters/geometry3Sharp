import math
import sys
import unittest
from fractions import Fraction as F
import scalar_cholesky_probe as pilot


class ArithmeticAuditTests(unittest.TestCase):
    def test_bit_neighbors_and_decode(self):
        for value in (0.0, -0.0, sys.float_info.min, -sys.float_info.min,
                      2**-1074, -2**-1074, 1.0, -1.0):
            self.assertEqual(F(value), pilot.rational(value))
            for upward in (True, False):
                expected = math.nextafter(value, math.inf if upward else -math.inf)
                self.assertEqual(pilot.bits(expected), pilot.bits(pilot.neighbor(value, upward)))
        for value in (2**-1074, -2**-1074):
            with self.assertRaises(ArithmeticError):
                pilot.checked(value)

    def test_nearest_even_ties(self):
        for value in (1.0, -1.0, 2.0, -2.0):
            lo, hi, even = pilot.rounding_cell(value)
            self.assertTrue(even)
            pilot.audit_round(value, lo)
            pilot.audit_round(value, hi)
            odd = math.nextafter(value, math.inf)
            lo, hi, even = pilot.rounding_cell(odd)
            self.assertFalse(even)
            for boundary in (lo, hi):
                with self.assertRaises(ArithmeticError):
                    pilot.audit_round(odd, boundary)

    def test_wrong_operation_and_sqrt_rejected(self):
        with self.assertRaises(ArithmeticError):
            pilot.audit_round(math.nextafter(1.0, math.inf), F(1))
        pilot.audit_round(math.sqrt(2.0), F(2), squared=True)
        with self.assertRaises(ArithmeticError):
            pilot.audit_round(math.nextafter(math.sqrt(2.0), math.inf), F(2), squared=True)

    def test_underflow_boundary(self):
        pilot.AUDIT = True
        try:
            for sign in (1, -1):
                with self.assertRaises(ArithmeticError):
                    pilot.product(sign * 2**-511, math.nextafter(2**-511, 0))
                with self.assertRaises(ArithmeticError):
                    pilot.quotient(sign * math.nextafter(2**-511, 0), 2**511)
                self.assertEqual(sign * sys.float_info.min, pilot.product(sign * sys.float_info.min, 1.0))
                self.assertEqual(sign * sys.float_info.min, pilot.quotient(sign * sys.float_info.min, 1.0))
        finally:
            pilot.AUDIT = False


if __name__ == '__main__':
    unittest.main()
