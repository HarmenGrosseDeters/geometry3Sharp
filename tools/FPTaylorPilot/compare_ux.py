#!/usr/bin/env python3
"""Compare a bounded model of Orient3dLPI's ux leaf with the generator rule."""
from decimal import Decimal
from fractions import Fraction
from pathlib import Path
import argparse
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def compare(report, bound=Fraction(1, 2**52)):
    # The hex value preserves the tool's printed bound exactly; the decimal is rounded for display.
    match = re.findall(r'^Absolute error \(exact\):\s+(\S+)\s+\((0x[0-9a-fA-F.]+p[+-]\d+)\)', report, re.M)
    if len(match) != 1:
        raise ValueError('expected one hexadecimal absolute bound')
    decimal, hexadecimal = match[0]
    if not re.fullmatch(r'[0-9]\.[0-9]{6}e[+-][0-9]+', decimal) or not Decimal(decimal).is_finite() or Decimal(decimal) < 0:
        raise ValueError('invalid bound')
    parsed = float.fromhex(hexadecimal)
    value = Fraction.from_float(parsed)
    printed = Decimal(decimal)
    tolerance = Decimal(1).scaleb(printed.as_tuple().exponent)
    if abs(printed - Decimal.from_float(parsed)) > tolerance:
        raise ValueError('decimal and hexadecimal bounds disagree')
    if value < 0 or value > bound:
        raise ValueError('FPTaylor bound exceeds the conservative generator bound')
    return {'fptaylor_bound_hex': hexadecimal, 'fptaylor_bound_exact': str(value),
            'generator_bound_exact': str(bound), 'ok': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fptaylor_dir', type=Path)
    args = parser.parse_args()
    spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
    compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()
    model = 'Variables\n float64 sx in [1, 2],\n float64 rx in [0, 0.5]\n;\nExpressions\n rnd64(sx - rx)\n;\n'
    if (spec.count('ipredicate Orient3dLPI(') != 1 or spec.count('ipredicate Orient3dTPI(') != 1 or
        spec.index('ipredicate Orient3dLPI(') > spec.index('ipredicate Orient3dTPI(')):
        raise SystemExit('predicate headers changed; re-check the model')
    section = spec.split('ipredicate Orient3dLPI(', 1)[1].split('ipredicate Orient3dTPI(', 1)[0]
    if ((HERE / 'orient3dlpi_ux.txt').read_text() != model or
        '    ux = s.x - r.x\n' not in section or
        'public static readonly Rational U = new Rational(1, BigInteger.Pow(2, 53));' not in compiler or
        'public bool Leaf => (Op == Op.Add || Op == Op.Sub) && A.Op == Op.Input && B.Op == Op.Input;' not in compiler or
        'n.C = n.Leaf ? u : (u + Rational.Max(n.A.C, n.B.C)) / (one - u);' not in compiler or
        'm = n.Leaf ? $"Math.Abs({val(n)})"' not in compiler):
        raise SystemExit('source rule changed; re-check the model')
    run = subprocess.run([str(HERE / 'run.sh'), str(args.fptaylor_dir), str(HERE / 'orient3dlpi_ux.txt')],
                         text=True, capture_output=True)
    if run.returncode:
        print(run.stderr, file=sys.stderr)
        return 1
    try:
        result = compare(run.stdout)
    except (ValueError, OverflowError) as error:
        print(error, file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
