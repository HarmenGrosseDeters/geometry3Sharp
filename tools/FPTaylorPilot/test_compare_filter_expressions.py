from fractions import Fraction
from pathlib import Path
import math
import unittest
import compare_filter_expressions as c
import compare_nx
from compare_ux import compare

ROOT = Path(__file__).resolve().parents[2]
U = Fraction(1, 2**53)
LPI, TPI = c.LPI_SPEC, c.TPI_SPEC

# the bound of each expression in units of u, exact
EXPECTED_MULTIPLES = {
    'ny': Fraction(2596148429267413814265248164610048, 81129638414606663681390495662081),
    'nz': Fraction(2596148429267413814265248164610048, 81129638414606663681390495662081),
    'd': Fraction(461373199758916509500371446221848876590601753269172093154055444282915616188866522127555144292761600, 533996758980227164885457289507134417714049456247824549019011922172948894275337421185487407677441),
    'coord_x': Fraction(5079164861253975067002512081381730019951283429333052435147346717726696617079329945250804705036369799694370077671424, 4809815209520806712740343400633163037320891686950862137681462377615438002719799664818182525120295947531328684031),
    'lpi_leaf': Fraction(2, 1),
    'tpi_leaf': Fraction(1, 1),
    'tpi_n1x': Fraction(649037107316853453566312041152512, 81129638414606663681390495662081),
    'tpi_D1': Fraction(2845706385096282542284914897610947170921172393301764497181125595079874202612793344, 59285549689505859146777197900334673229677235515577591173502484983630641288445951),
}


def report(value):
    return f'Absolute error (exact): {value:.6e} ({float(value).hex()})'


class ComponentBoundTests(unittest.TestCase):
    def bound(self, name):
        return c.MODELS[name]['bound']()

    def test_ny_and_nz_have_the_nx_structure_and_bound(self):
        self.assertEqual(self.bound('ny'), compare_nx.generator_bound())
        self.assertEqual(self.bound('nz'), compare_nx.generator_bound())

    def test_bounds_are_exactly_the_expected_multiples_of_u(self):
        # d: 96 * C_d (about 864u); coordinate Nx: 96 * C (about 1056u); TPI n1x: 2 * C (about 8u); TPI D1: 6 * C (about 48u);
        # the leaves: 2u (prx, |prx| <= 2) and u. The values are the generator rule of Compiler.cs applied by hand.
        for name, expected in EXPECTED_MULTIPLES.items():
            with self.subTest(name=name):
                self.assertEqual(self.bound(name) / U, expected)
        self.assertEqual(self.bound('lpi_leaf'), 2 * U)
        self.assertEqual(self.bound('tpi_leaf'), U)
        for name, low, high in (('d', 864, 865), ('coord_x', 1055, 1057), ('tpi_n1x', 8, 9), ('tpi_D1', 48, 49)):
            self.assertTrue(low < self.bound(name) / U < high, name)

    def test_the_bound_passes_and_one_ulp_above_it_fails(self):
        for name in c.MODELS:
            with self.subTest(name=name):
                bound = self.bound(name)
                at = float(bound)
                if Fraction(at) > bound:
                    at = math.nextafter(at, 0.0)      # the largest double not above the bound
                self.assertLessEqual(Fraction(at), bound)
                self.assertGreater(Fraction(math.nextafter(at, 1.0)), bound)
                self.assertTrue(compare(report(at), bound)['ok'])
                with self.assertRaises(ValueError):
                    compare(report(math.nextafter(at, 1.0)), bound)
        # a bound that is itself a double (the leaves): equal passes, one ulp more fails (not >=)
        for name in ('lpi_leaf', 'tpi_leaf'):
            self.assertEqual(Fraction(float(self.bound(name))), self.bound(name))

    def test_pass_and_fail_far_from_the_bounds(self):
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


class DriftTests(unittest.TestCase):
    spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
    compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()

    def test_changing_nx_fails_the_d_and_coord_x_contracts(self):
        changed = self.spec.replace('nx = uy * wz - uz * wy', 'nx = uy * wz + uz * wy')
        self.assertNotEqual(changed, self.spec)
        for name in ('d', 'coord_x'):
            self.assertFalse(c.source_contract(changed, self.compiler, name), name)
        # the expressions that do not use nx are unaffected
        self.assertTrue(c.source_contract(changed, self.compiler, 'ny'))

    def test_every_line_of_every_model_is_checked(self):
        for name, entry in c.MODELS.items():
            for line in entry['lines']:
                with self.subTest(name=name, line=line.strip()):
                    head, tail = line.split(' = ', 1)
                    changed = self.spec.replace(line + '\n', head + ' = ' + tail + ' + 0\n')
                    self.assertNotEqual(changed, self.spec)
                    self.assertFalse(c.source_contract(changed, self.compiler, name))

    def test_the_order_of_the_lines_is_checked(self):
        for name, first, second in (('d', LPI['wx'], LPI['wy']), ('tpi_n1x', TPI['u1_y'], TPI['u1_z']),
                                    ('tpi_D1', TPI['w1_x'], TPI['w1_y']), ('coord_x', LPI['pcx'], LPI['qcx'])):
            with self.subTest(name=name):
                swapped = self.spec.replace(first + '\n' + second + '\n', second + '\n' + first + '\n') \
                    if first + '\n' + second + '\n' in self.spec else None
                if swapped is None:      # not adjacent in the spec: swap by placeholders
                    swapped = self.spec.replace(first + '\n', '@@A@@\n', 1).replace(second + '\n', first + '\n', 1) \
                        .replace('@@A@@\n', second + '\n', 1)
                self.assertNotEqual(swapped, self.spec)
                self.assertFalse(c.source_contract(swapped, self.compiler, name))

    def test_models_use_the_spec_lines_they_are_built_from(self):
        # the model's definitions are the spec's lines one for one: the number of definitions (minus the leaves of the cross products)
        for name in ('d', 'coord_x'):
            lines = c.MODELS[name]['lines']
            self.assertEqual(lines[:len(c.LPI_D_CHAIN)], [LPI[n] for n in c.LPI_D_CHAIN], name)


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
