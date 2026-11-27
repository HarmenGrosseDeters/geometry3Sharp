from check_pilot_obligations import run_test
from unittest import TestCase
from terrain_box_expansion import run


class TerrainBoxExpansionTests(TestCase):
    def test_local_enclosure_and_offset_replay(self):
        run_test(self, run, 'Z3_TERRAIN_BOX_REPORT')
