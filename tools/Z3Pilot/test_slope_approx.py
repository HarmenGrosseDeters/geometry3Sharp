import json
import os
import pathlib
import shutil
import tempfile
import unittest

import slope_approx

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
ROOT = pathlib.Path(__file__).resolve().parents[2]


class SlopeApproxTests(unittest.TestCase):
    def test_lemma_finding_and_mutant(self):
        report = slope_approx.run(TIMEOUT_MS)
        out = os.environ.get('Z3_SLOPE_APPROX_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(report['passed'], json.dumps(report, indent=1))
        self.assertEqual(dict(accept='1e-12', consume='4e-12'), report['contract'])
        # the finding of 2026-11-25 stays pinned until ExactSlopeKernel.Approx is fixed (then this test fails: update
        # the lemma, the finding becomes an obligation)
        self.assertEqual('sat', report['finding']['result'])
        self.assertTrue(report['finding']['ieee']['replayed'])

    def test_source_coupling(self):
        d = pathlib.Path(tempfile.mkdtemp())
        try:
            for rel in (slope_approx.KERNEL, slope_approx.MAP):
                (d / rel).parent.mkdir(parents=True, exist_ok=True)
                shutil.copy(ROOT / rel, d / rel)
            p = d / slope_approx.MAP
            text = p.read_text(encoding='utf-8')
            p.write_text(text.replace('e = 4e-12 * e + double.Epsilon;', 'e = 1e-12 * e + double.Epsilon;'), encoding='utf-8')
            self.assertEqual('1e-12', slope_approx.source_contract(d)['consume'])
            p.write_text(text.replace('double lowerF = zmin + slb * Math.Max(0, dmin - e);', 'double lowerF = zmin + slb * dmin;'),
                         encoding='utf-8')
            with self.assertRaises(ValueError):
                slope_approx.source_contract(d)
        finally:
            shutil.rmtree(d)


if __name__ == '__main__':
    unittest.main()
