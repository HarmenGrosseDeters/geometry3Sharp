import json
import os
import unittest
from pathlib import Path

from numerical_certificate_pilots import PILOTS


class NumericalCertificatePilotTests(unittest.TestCase):
    def test_local_certificates(self):
        timeout_ms = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
        reports = {}
        for name, run in PILOTS.items():
            with self.subTest(pilot=name):
                report = run(timeout_ms)
                reports[name] = report
                self.assertTrue(report['passed'], json.dumps(report, indent=2))
        filename = os.environ.get('Z3_NUMERICAL_CERTIFICATE_REPORT')
        if filename:
            Path(filename).write_text(json.dumps(reports, indent=2) + '\n', encoding='utf-8')
