from fractions import Fraction
from pathlib import Path
import unittest
import compare_lpi_components as c
import compare_nx
from compare_ux import compare

ROOT = Path(__file__).resolve().parents[2]
U = Fraction(1, 2**53)


class ComponentBoundTests(unittest.TestCase):
    def test_ny_and_nz_have_the_nx_structure_and_bound(self):
        self.assertEqual(c.MODELS['ny']['bound'](), compare_nx.generator_bound())
        self.assertEqual(c.MODELS['nz']['bound'](), compare_nx.generator_bound())

    def test_d_bound_is_about_864_u(self):
        # 96 = magnitude of d over the domain; C_d = 9u + O(u^2)
        bound = c.MODELS['d']['bound']()
        self.assertGreater(bound, 864 * U)
        self.assertLess(bound, 865 * U)

    def test_fptaylor_results_are_checked_against_each_bound(self):
        # 2^-50 = 4u lies below the bounds of ny, nz (about 32u) and d (about 864u); 2^-47 = 64u exceeds 32u
        # and 2^-43 = 1024u exceeds 864u. The decimal is printed with six digits, as run.sh's tool does.
        for name, fail in (('ny', 2**-47), ('nz', 2**-47), ('d', 2**-43)):
            with self.subTest(name=name):
                self.assertTrue(compare(report(2**-50), c.MODELS[name]['bound']())['ok'])
                with self.assertRaises(ValueError):
                    compare(report(fail), c.MODELS[name]['bound']())


def report(value):
    return f'Absolute error (exact): {value:.6e} ({float(value).hex()})'


class ModelAndSourceTests(unittest.TestCase):
    def test_model_constants_match_the_files(self):
        for name, entry in c.MODELS.items():
            self.assertEqual((c.HERE / entry['file']).read_text(), entry['text'], name)

    def test_source_contract_detects_drift(self):
        spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
        compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()
        for name in c.MODELS:
            self.assertTrue(c.source_contract(spec, compiler, name), name)
        self.assertFalse(c.source_contract(spec.replace('ny = uz * wx - ux * wz', 'ny = uz * wx + ux * wz'),
                                           compiler, 'ny'))
        self.assertFalse(c.source_contract(spec.replace('d = lq - lp', 'd = lq + lp'), compiler, 'd'))
        self.assertFalse(c.source_contract(spec, compiler.replace('n.A.C * n.B.C', 'n.A.C + n.B.C'), 'd'))
        self.assertFalse(c.source_contract(spec.replace('ipredicate Orient3dLPI(', 'ipredicate Other('),
                                           compiler, 'nz'))


if __name__ == '__main__':
    unittest.main()
