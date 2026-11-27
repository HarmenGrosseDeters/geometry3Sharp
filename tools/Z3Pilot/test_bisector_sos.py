from pathlib import Path
import tempfile
import unittest

import bisector_sos
from check_pilot_obligations import ROOT, run_test, source


class BisectorSoSTests(unittest.TestCase):
    def test_bisector_sos(self):
        run_test(self, bisector_sos.run, 'Z3_BISECTOR_SOS_REPORT')

    def test_every_unsat_obligation_has_a_sat_mutant_nearby(self):
        rows = bisector_sos.obligations()
        # every lemma is paired with a broken rule that must be SAT (the check has power)
        self.assertEqual(sum(1 for r in rows if r[3] == 'unsat'), sum(1 for r in rows if r[3] == 'sat'))
        self.assertEqual(len({r[0] for r in rows}), len(rows))

    def test_changed_sources_break_the_contract(self):
        # a changed coefficient in the cascade, and a changed factor in the spec, must stop the pilot
        for (path, start, end, digest), old, new in [
                (bisector_sos.CONTRACTS[7], '-BisectorPredicates.DetSitesNormal(a, e, c', 'BisectorPredicates.DetSitesNormal(a, e, c'),
                (bisector_sos.CONTRACTS[3], 'rd2 = rd + rd', 'rd2 = rd'),
                (bisector_sos.CONTRACTS[6], ': sd;', ': -sd;')]:
            original = (ROOT / path).read_text(encoding='utf-8')
            changed = original.replace(old, new, 1)
            self.assertNotEqual(original, changed)
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                (root / path).parent.mkdir(parents=True)
                (root / path).write_text(changed, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source(path, start, end, digest, root=root)


if __name__ == '__main__':
    unittest.main()
