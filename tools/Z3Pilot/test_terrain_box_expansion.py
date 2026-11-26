from pilot_obligations import PilotTest
from terrain_box_expansion import run


class TerrainBoxExpansionTests(PilotTest):
    def test_local_enclosure_and_offset_replay(self):
        self.check_pilot(run, 'Z3_TERRAIN_BOX_REPORT')
