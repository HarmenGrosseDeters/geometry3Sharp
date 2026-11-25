import json
import os
import pathlib
import shutil
import tempfile
import unittest

import adaptive_range

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
ROOT = pathlib.Path(__file__).resolve().parents[2]
FILES = [adaptive_range.PREDICATES, adaptive_range.ADAPTIVE, 'mesh_generators/DualContouring.cs']


class AdaptiveRangeTests(unittest.TestCase):
    def test_range_lemmas_mutants_and_replays(self):
        report = adaptive_range.run(TIMEOUT_MS)
        out = os.environ.get('Z3_ADAPTIVE_RANGE_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(report['passed'], json.dumps(report, indent=1))
        self.assertEqual(10, len(report['obligations']))
        self.assertEqual(4, len(report['mutants']))
        self.assertEqual(dict(min='2^-100', max='2^100'), report['range'])

    def test_source_contract_reads_the_source(self):
        c = adaptive_range.source_contract()
        self.assertEqual((-100, 100), (c['min_exponent'], c['max_exponent']))


class SourceCouplingTests(unittest.TestCase):
    """the lemmas follow the source: a changed range, a new predicate or an unguarded call fails (on a copy)"""

    def setUp(self):
        self.dir = pathlib.Path(tempfile.mkdtemp())
        for f in FILES:
            (self.dir / f).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(ROOT / f, self.dir / f)

    def tearDown(self):
        shutil.rmtree(self.dir)

    def edit(self, rel, old, new):
        p = self.dir / rel
        text = p.read_text(encoding='utf-8')
        self.assertIn(old, text)
        p.write_text(text.replace(old, new, 1), encoding='utf-8')

    def underflow_results(self):
        c = adaptive_range.source_contract(self.dir)
        out = {}
        for step, name, hyps, bad, _ in adaptive_range.int_obligations(c):
            _, r, _ = adaptive_range._check(hyps, bad, TIMEOUT_MS)
            out[name] = str(r)
        return out

    def test_h6_widened_min_magnitude_fails_the_incircle_lemma(self):
        # the hand mutant H6 of #1572: 2^-100 to 2^-250
        self.edit(adaptive_range.PREDICATES, 'const double MinMagnitude = 7.888609052210118e-31;',
                  'const double MinMagnitude = 5.527147875260445e-76;')
        r = self.underflow_results()
        self.assertEqual('sat', r['no_underflow_incircle'])
        self.assertEqual('unsat', r['no_underflow_orient3d'])    # 3 (250 + 52) = 906 <= 1022: only InCircle breaks (as in the .NET replay)
        self.assertEqual('unsat', r['no_underflow_orient2d'])     # degree 2 needs only 2 (m + 52) <= 1022

    def test_widened_max_magnitude_fails_the_overflow_lemma(self):
        self.edit(adaptive_range.PREDICATES, 'const double MaxMagnitude = 1.2676506002282294e30;',
                  'const double MaxMagnitude = 1.8092513943330656e75;')
        self.assertEqual('sat', self.underflow_results()['no_overflow_incircle'])

    def test_range_at_its_proved_limit_passes(self):
        # 2^-203 is the smallest MinMagnitude the incircle lemma admits (the margins of the report)
        self.edit(adaptive_range.PREDICATES, 'const double MinMagnitude = 7.888609052210118e-31;',
                  'const double MinMagnitude = %r;' % 2.0**-203)
        self.assertTrue(all(v == 'unsat' for v in self.underflow_results().values()))

    def test_not_a_power_of_two_is_rejected(self):
        self.edit(adaptive_range.PREDICATES, 'const double MinMagnitude = 7.888609052210118e-31;',
                  'const double MinMagnitude = 1e-30;')
        with self.assertRaises(ValueError):
            adaptive_range.source_contract(self.dir)

    def test_changed_range_check_is_rejected(self):
        self.edit(adaptive_range.PREDICATES, 'ax >= MinMagnitude && ax <= MaxMagnitude', 'ax > MinMagnitude && ax <= MaxMagnitude')
        with self.assertRaises(ValueError):
            adaptive_range.source_contract(self.dir)

    def test_new_predicate_is_rejected(self):
        self.edit(adaptive_range.ADAPTIVE, 'public static int Orient2d(double',
                  'public static int InSphere(double ax) { return 0; }\n        public static int Orient2d(double')
        with self.assertRaises(ValueError):
            adaptive_range.source_contract(self.dir)

    def test_unguarded_call_is_rejected(self):
        p = self.dir / 'mesh_ops' / 'NewCaller.cs'
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text('class C { int f() {\n\n\n\n return AdaptivePredicates.InCircle(0, 0, 1, 0, 0, 1, 1, 1); } }\n')
        with self.assertRaises(ValueError):
            adaptive_range.source_contract(self.dir)
        p.write_text('class C { int f() {\n if (Expansion.InRange(0, 0, 1, 0, 0, 1, 1, 1) == false) return 0;\n'
                     ' return AdaptivePredicates.InCircle(0, 0, 1, 0, 0, 1, 1, 1); } }\n')
        adaptive_range.source_contract(self.dir)


if __name__ == '__main__':
    unittest.main()
