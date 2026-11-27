from pathlib import Path
import tempfile
import unittest

from check_pilot_obligations import run_test
import continuous_collision as ccd


class ContinuousCollisionTests(unittest.TestCase):
    def test_corner_bound_culling_and_subnormal_mutant(self):
        run_test(self, ccd.run, 'Z3_CONTINUOUS_COLLISION_REPORT')

    def test_source_coupling(self):
        source = (ccd.ROOT / ccd.SOURCE).read_text(encoding='utf-8')
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / ccd.SOURCE
            path.parent.mkdir()
            for old, new in (
                ('FilterAbsolute = 16', 'FilterAbsolute = 0'),
                ('lo > threshold || hi < -threshold', 'lo >= threshold || hi <= -threshold'),
            ):
                with self.subTest(old=old):
                    changed = source.replace(old, new)
                    self.assertNotEqual(changed, source)
                    path.write_text(changed, encoding='utf-8')
                    with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                        ccd.source_contract(Path(tmp))
