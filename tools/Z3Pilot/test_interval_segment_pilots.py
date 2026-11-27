import json
import os
import unittest
from pathlib import Path

from interval_segment_pilots import PILOTS


class IntervalAndSegmentPilotTests(unittest.TestCase):
    def test_interval_successor_and_segment_slabs(self):
        timeout_ms = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
        reports = {}
        for name, pilot in PILOTS.items():
            with self.subTest(pilot=name):
                report = pilot(timeout_ms)
                reports[name] = report
                self.assertTrue(report['passed'], json.dumps(report, indent=2))
        output = os.environ.get('Z3_INTERVAL_SEGMENT_REPORT')
        if output:
            Path(output).write_text(json.dumps(reports, indent=2) + '\n', encoding='utf-8')
