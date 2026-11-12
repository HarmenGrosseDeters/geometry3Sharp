import json
import os
import pathlib
import unittest

import clipper_frame as cf


class ClipperFrameTests(unittest.TestCase):
    """Pilot 6 (QA 2.0 T10): the frame of the Clipper2 adapter (clipper_frame.py)."""

    @classmethod
    def setUpClass(cls):
        cls.report = cf.run(int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000')))
        out = os.environ.get('Z3_CLIPPER_FRAME_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(cls.report, indent=2) + '\n')

    def test_obligations_hold_and_are_not_vacuous(self):
        self.assertTrue(self.report['passed'], json.dumps(self.report, indent=2))
        for r in self.report['obligations']:
            self.assertEqual(r['expected'], r['result'], r['name'])
            self.assertEqual('sat', r['assumptions_result'], r['name'])

    def test_the_mutant_witnesses_replay(self):
        by_name = {r['name']: r for r in self.report['obligations']}
        w = by_name['mutant_floor_instead_of_ceiling_(extent_above_2^e)']['witness']
        m, biased = int(w['mantissa']), int(w['biased'])
        self.assertTrue(0 < m < 2 ** 52)              # (2^52 + m) 2^k is above 2^(k+52), which a floor would return
        from fractions import Fraction
        x = Fraction(2 ** 52 + m) * Fraction(2) ** (biased - 1075)
        self.assertTrue(x > Fraction(2) ** (biased - 1023))
        # the MaxPrecisionBits 53 enumeration: the frame it finds has integers above 2^52
        note = by_name['mutant_MaxPrecisionBits_53_gives_a_frame_with_integers_above_2^52']['note']
        bits, e = (int(v) for v in note.split('first failing (')[1].rstrip(')').split(','))
        self.assertEqual(53, bits)
        self.assertTrue(Fraction(2) ** bits > Fraction(2) ** 52)
        self.assertIsNone(cf._frames(50)[1])            # none with the real limit

    def test_ceil_exponent_replays_in_python(self):
        def ceil_exponent(x):
            import struct
            bits = struct.unpack('<Q', struct.pack('<d', x))[0]
            biased, mant = (bits >> 52) & 0x7FF, bits & ((1 << 52) - 1)
            if biased == 0:
                e = -1074
                while mant > 1:
                    mant = (mant + 1) >> 1
                    e += 1
                return e
            return biased - 1023 if mant == 0 else biased - 1023 + 1
        from fractions import Fraction
        for x in (5e-324, 1e-310, 2.2250738585072014e-308, 0.1, 1.0, 1.5, 3.0, 5000.0, 1e300):
            e = ceil_exponent(x)
            self.assertTrue(Fraction(2) ** (e - 1) < Fraction(x) <= Fraction(2) ** e, x)

    def test_the_source_contract_fails_on_drift(self):
        text = (cf.ROOT / cf.SOURCE).read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        cf.source_contracts(text)
        with self.assertRaises(ValueError):
            cf.source_contracts(text.replace('MaxPrecisionBits = 50', 'MaxPrecisionBits = 53'))
        with self.assertRaises(ValueError):
            cf.source_contracts(text.replace('return mantissa == 0 ? exponent : exponent + 1;', 'return exponent;'))
        with self.assertRaises(ValueError):
            cf.source_contracts(text.replace('if (n > 1023) return double.PositiveInfinity;', 'if (n > 1024) return double.PositiveInfinity;'))
        with self.assertRaises(ValueError):
            cf.source_contracts(text.replace('4503599627370496.0', '9007199254740992.0'))
        with self.assertRaises(ValueError):    # the range guard of precisionBits is deleted
            cf.source_contracts(text.replace('if (precisionBits < MinPrecisionBits || precisionBits > MaxPrecisionBits)', 'if (false)'))
        with self.assertRaises(ValueError):    # the extent loses its margin
            cf.source_contracts(text.replace('bounds.Max.y - origin.y)) + margin;', 'bounds.Max.y - origin.y));'))


if __name__ == '__main__':
    unittest.main()
