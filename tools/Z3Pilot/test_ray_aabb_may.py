from check_pilot_obligations import run_test
from unittest import TestCase
from ray_aabb_may import run


class RayAabbMayTests(TestCase):
    def test_finite_slabs(self):
        run_test(self, run, 'Z3_RAY_AABB_MAY_REPORT')
