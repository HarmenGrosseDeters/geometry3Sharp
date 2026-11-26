import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import continuous_collision as ccd

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))


class ContinuousCollisionTests(unittest.TestCase):
    def test_corner_bound_culling_and_subnormal_mutant(self):
        report = ccd.run(TIMEOUT_MS)
        output = os.environ.get('Z3_CONTINUOUS_COLLISION_REPORT')
        if output:
            Path(output).write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        self.assertTrue(report['passed'], report)

    def test_source_coupling(self):
        source = (ccd.ROOT / ccd.SOURCE).read_text(encoding='utf-8')
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / ccd.SOURCE
            path.parent.mkdir()
            path.write_text(source.replace('FilterAbsolute = 16', 'FilterAbsolute = 0'), encoding='utf-8')
            constants = ccd.source_contract(Path(tmp))
            self.assertEqual((47, 0), constants)
            with patch.object(ccd, 'source_contract', return_value=constants):
                report = ccd.run(TIMEOUT_MS)
            self.assertFalse(report['passed'], report)
            self.assertEqual('sat', report['results']['corner_error'])
            path.write_text(source.replace('lo > threshold || hi < -threshold',
                                           'lo >= threshold || hi <= -threshold'), encoding='utf-8')
            with self.assertRaises(ValueError):
                ccd.source_contract(Path(tmp))
