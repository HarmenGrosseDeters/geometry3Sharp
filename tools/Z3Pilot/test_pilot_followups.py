import json
import os
import unittest
from pathlib import Path

from pilot_followups import run


class PilotFollowupTests(unittest.TestCase):
    def test_assumptions_and_replays(self):
        report = run(int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000')))
        output = os.environ.get('Z3_PILOT_FOLLOWUPS_REPORT')
        if output:
            Path(output).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        self.assertTrue(report['passed'], json.dumps(report, indent=2))
