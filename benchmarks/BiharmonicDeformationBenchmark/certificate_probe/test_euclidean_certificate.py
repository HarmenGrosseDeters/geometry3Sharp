import unittest
from fractions import Fraction as F
from euclidean_certificate import assess


class EuclideanCertificateTests(unittest.TestCase):
    def test_lost_tiny_response_fails_relative_contract(self):
        p = [[0, 0, 0], [1, 0, 0]]
        absolute, relative, *_ = assess(p, p, [1], 1., [F(1, 10**99), 0, 0])
        self.assertTrue(absolute)
        self.assertFalse(relative)

    def test_vector_bound_combines_axes(self):
        p = [[0, 0, 0], [1, 0, 0]]
        x = [[0, 0, 0], [10, 0, 0]]
        absolute, relative, *_ = assess(p, x, [1], 1., [F(3, 4), F(3, 4), 0], F(1), F(1))
        self.assertFalse(absolute)  # each axis passes alone; vector length exceeds one
        self.assertTrue(relative)

    def test_relative_boundary_and_exact_zero(self):
        p = [[0, 0, 0], [1, 0, 0]]
        x = [[0, 0, 0], [4, 0, 0]]
        self.assertTrue(assess(p, x, [1], 1., [1, 0, 0], F(1), F(1, 2))[1])
        self.assertFalse(assess(p, x, [1], 1., [F(1001, 1000), 0, 0], F(2), F(1, 2))[1])
        self.assertEqual((True, True), assess(p, p, [1], 1., [0, 0, 0])[:2])

    def test_scale_and_axis_permutation(self):
        p = [[0, 0, 0], [1, 0, 0]]
        x = [[0, 0, 0], [1, 1, 0]]
        args = ([1], 1., [F(1, 10**12)] * 3)
        expected = assess(p, x, *args)[:2]
        self.assertEqual((True, True), expected)
        q = [[0, 0, 0], [0, 0, 8]]
        y = [[0, 0, 0], [0, -8, 8]]
        self.assertEqual(expected, assess(q, y, [1], 8., args[2])[:2])


if __name__ == '__main__':
    unittest.main()
