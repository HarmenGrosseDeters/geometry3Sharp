from fractions import Fraction
from pathlib import Path
import unittest
import compare_filter_expressions as c
import compare_nx
from compare_ux import compare

ROOT = Path(__file__).resolve().parents[2]
U = Fraction(1, 2**53)


def report(value):
    return f'Absolute error (exact): {value:.6e} ({float(value).hex()})'


class ComponentBoundTests(unittest.TestCase):
    def bound(self, name):
        return c.MODELS[name]['bound']()

    def test_ny_and_nz_have_the_nx_structure_and_bound(self):
        self.assertEqual(self.bound('ny'), compare_nx.generator_bound())
        self.assertEqual(self.bound('nz'), compare_nx.generator_bound())

    def test_magnitudes_give_the_expected_multiples_of_u(self):
        # d: 96 * C_d with C_d about 9u; coordinate Nx: 96 * C about 11u; TPI n1x: 2 * C about 4u;
        # TPI D1: 6 * C about 8u; TPI leaf: magnitude 1, bound u.
        for name, low, high in (('d', 864, 865), ('coord_x', 1000, 1100), ('tpi_n1x', 7, 9),
                                ('tpi_D1', 40, 56), ('tpi_leaf', Fraction(1, 2), Fraction(3, 2))):
            with self.subTest(name=name):
                multiple = self.bound(name) / U
                self.assertGreater(multiple, low)
                self.assertLess(multiple, high)

    def test_pass_and_fail_for_every_expression(self):
        for name in c.MODELS:
            with self.subTest(name=name):
                self.assertTrue(compare(report(2**-60), self.bound(name))['ok'])
                with self.assertRaises(ValueError):
                    compare(report(2**-38), self.bound(name))  # above every bound (about 2^-43 at most)


class ModelAndSourceTests(unittest.TestCase):
    def test_model_constants_match_the_files(self):
        for name, entry in c.MODELS.items():
            self.assertEqual((c.HERE / entry['file']).read_text(), entry['text'], name)

    def test_source_contract_holds_for_every_expression(self):
        spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
        compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()
        for name in c.MODELS:
            self.assertTrue(c.source_contract(spec, compiler, name), name)

    def test_source_contract_detects_drift(self):
        spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
        compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()
        self.assertFalse(c.source_contract(spec.replace('ny = uz * wx - ux * wz', 'ny = uz * wx + ux * wz'),
                                           compiler, 'ny'))
        self.assertFalse(c.source_contract(spec.replace('d = lq - lp', 'd = lq + lp'), compiler, 'd'))
        self.assertFalse(c.source_contract(spec.replace('Nx = pcx * lq - qcx * lp', 'Nx = pcx * lq + qcx * lp'),
                                           compiler, 'coord_x'))
        self.assertFalse(c.source_contract(spec.replace('D1 = n1x * a1cx', 'D1 = n1x * a1cy'), compiler, 'tpi_D1'))
        self.assertFalse(c.source_contract(spec.replace('u1_x = b1.x - a1.x', 'u1_x = b1.x + a1.x'), compiler,
                                           'tpi_leaf'))
        self.assertFalse(c.source_contract(spec.replace('ipredicate Orient3dTPI(', 'ipredicate Other('),
                                           compiler, 'tpi_n1x'))
        self.assertFalse(c.source_contract(spec, compiler.replace('n.A.C * n.B.C', 'n.A.C + n.B.C'), 'd'))


class SelectionTests(unittest.TestCase):
    def test_no_names_selects_every_expression(self):
        # argparse used to reject the default list (nargs='*' with choices), so 'run all' failed
        self.assertEqual(c.selected([]), list(c.MODELS))
        self.assertEqual(c.selected(['d', 'ny']), ['d', 'ny'])

    def test_main_accepts_no_names_on_the_command_line(self):
        import unittest.mock as mock
        with mock.patch('sys.argv', ['compare_filter_expressions.py', '/nonexistent']),                 mock.patch.object(c, 'run_one', return_value=0) as run:
            self.assertEqual(c.main(), 0)
        self.assertEqual([call.args[0] for call in run.call_args_list], list(c.MODELS))


if __name__ == '__main__':
    unittest.main()
