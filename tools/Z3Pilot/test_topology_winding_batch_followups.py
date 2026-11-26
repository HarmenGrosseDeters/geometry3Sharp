import json
import os
from pathlib import Path
import unittest

from topology_winding_batch_followups import run


class TopologyWindingBatchFollowupTests(unittest.TestCase):
    def test_capacity_geometry_and_cancellation(self):
        report = run(int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000')))
        output = os.environ.get('Z3_TOPOLOGY_WINDING_BATCH_FOLLOWUPS_REPORT')
        if output:
            Path(output).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        self.assertTrue(report['passed'], json.dumps(report, indent=2))
