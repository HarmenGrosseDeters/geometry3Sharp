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
        self.assertEqual(dict(accept='1e-12', width='5e11', consume='4e-12'), report['contract'])
        # the finding of 2026-11-25 (issue #1586) is now an obligation: the corner error holds (L1-L4 UNSAT), and the
        # replay of the finding and of the two half fixes is rejected by the fixed code
        self.assertEqual(['L1_width', 'L2_xy_width', 'L3_coordinate', 'L4_claim', 'w_sign_normal_range_w_positive',
                          'w_sign_normal_range_w_negative'], [o['name'] for o in report['obligations']])
        self.assertIn('err_without_the_w_term', [m['name'] for m in report['mutants']])
        self.assertTrue(all(o['result'] == 'unsat' for o in report['obligations']))
        for name in ('issue_1586', 'width_only', 'guard_only', 'overflow'):
            self.assertTrue(report['replays'][name]['variant_wrong'], name)
            self.assertFalse(report['replays'][name]['fixed_accepts'], name)

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
            p.write_text(text, encoding='utf-8')
            k = d / slope_approx.KERNEL
            ktext = k.read_text(encoding='utf-8')
            # dropping the guard or the width condition is seen
            for old in ('if (s >= MinNormal && s <= double.MaxValue && (W.Hi - W.Lo) * 5e11 <= Math.Abs(w))',
                        's >= MinNormal && s <= double.MaxValue &&',
                        'if (X.Lo == 0 && X.Hi == 0 && Y.Lo == 0 && Y.Hi == 0)'):
                k.write_text(ktext.replace(old, 'if (true)'), encoding='utf-8')
                with self.assertRaises(ValueError):
                    slope_approx.source_contract(d)
            k.write_text(ktext.replace('* 5e11 <=', '* 2.5e11 <='), encoding='utf-8')
            self.assertEqual('2.5e11', slope_approx.source_contract(d)['width'])
            k.write_text(ktext.replace('MinNormal = 2.2250738585072014e-308', 'MinNormal = 1e-300'), encoding='utf-8')
            with self.assertRaises(ValueError):
                slope_approx.source_contract(d)
        finally:
            shutil.rmtree(d)


if __name__ == '__main__':
    unittest.main()
