import json
import os
from pathlib import Path
import unittest

from topology_winding_morton import PILOTS


class TopologyWindingMortonTests(unittest.TestCase):
    def test_topology_decisions_and_scheduling(self):
        timeout_ms = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
        reports = {}
        for name, run in PILOTS.items():
            with self.subTest(pilot=name):
                report = run(timeout_ms)
                reports[name] = report
                self.assertTrue(report['passed'], json.dumps(report, indent=2))
        output = os.environ.get('Z3_TOPOLOGY_WINDING_MORTON_REPORT')
        if output:
            Path(output).write_text(json.dumps(reports, indent=2) + '\n', encoding='utf-8')
