from fractions import Fraction
from pathlib import Path
import unittest
import compare_nx as c
from compare_ux import compare

class CrossProductTests(unittest.TestCase):
    def test_bound_and_results(self):
        self.assertEqual(c.generator_bound(), Fraction(288230376151711744, 81129638414606663681390495662081))
        result = compare('Absolute error (exact): 1.779827e-15 (0x1.0080000000001p-49)', c.generator_bound())
        self.assertTrue(result['ok'])
        with self.assertRaises(ValueError):
            compare('Absolute error (exact): 7.105427e-15 (0x1p-47)', c.generator_bound())

    def test_source_and_model_drift(self):
        root = Path(__file__).resolve().parents[2]
        spec = (root / 'tools/PredicateCompiler/indirect.spec').read_text()
        compiler = (root / 'tools/PredicateCompiler/Compiler.cs').read_text()
        self.assertTrue(c.source_contract(spec, compiler, c.MODEL))
        for changed in (spec.replace('uy = s.y - r.y', 'uy = s.y + r.y'),
                        spec.replace('nx = uy * wz - uz * wy', 'nx = uy * wy - uz * wz'),
                        spec.replace('ipredicate Orient3dLPI(', 'ipredicate Other(')):
            self.assertFalse(c.source_contract(changed, compiler, c.MODEL))
        self.assertFalse(c.source_contract(spec, compiler.replace('n.A.C * n.B.C', 'n.A.C + n.B.C'), c.MODEL))
        self.assertFalse(c.source_contract(spec, compiler, c.MODEL.replace('sy - ry', 'sy + ry')))
