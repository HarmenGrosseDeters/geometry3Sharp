"""Checks the comparison's acceptance boundary and invalid tool output."""
import unittest
import compare_ux as c


class ComparisonTests(unittest.TestCase):
    def test_valid_and_too_large(self):
        self.assertTrue(c.compare('Absolute error (exact): 1.110224e-16 (0x1p-53)')['ok'])
        with self.assertRaises(ValueError):
            c.compare('Absolute error (exact): 1.776357e-15 (0x1p-49)')

    def test_no_silent_success(self):
        for output in ('', 'Absolute error (exact): 1.000000e+100 (0x1p-53)', 'Absolute error (exact): inf (0x1p-53)',
                       'Absolute error (exact): 1.110224e-16 (0x1p-53)\nAbsolute error (exact): 1.110224e-16 (0x1p-53)'):
            with self.assertRaises(ValueError):
                c.compare(output)


if __name__ == '__main__':
    unittest.main()
