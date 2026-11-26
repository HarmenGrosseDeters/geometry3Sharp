from pilot_obligations import PilotTest
from ray_aabb_may import run


class RayAabbMayTests(PilotTest):
    def test_finite_slabs(self):
        self.check_pilot(run, 'Z3_RAY_AABB_MAY_REPORT')
