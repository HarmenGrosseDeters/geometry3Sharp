#!/usr/bin/env python3
"""Compare the bounded Orient3dLPI cross-product component with its generator bound."""
import argparse
from fractions import Fraction
import json
from pathlib import Path
import subprocess
import sys
from compare_ux import compare

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
MODEL = '''Variables
 float64 sy in [1, 2], float64 sz in [1, 2],
 float64 ty in [1, 2], float64 tz in [1, 2],
 float64 ry in [0, 0.5], float64 rz in [0, 0.5]
;
Definitions
 uy rnd64= sy - ry,
 uz rnd64= sz - rz,
 wy rnd64= ty - ry,
 wz rnd64= tz - rz,
 p rnd64= uy * wz,
 q rnd64= uz * wy
;
Expressions
 rnd64(p - q)
;
'''


def source_contract(spec, compiler, model):
    lpi, tpi = 'ipredicate Orient3dLPI(', 'ipredicate Orient3dTPI('
    if spec.count(lpi) != 1 or spec.count(tpi) != 1 or spec.index(lpi) > spec.index(tpi):
        return False
    section = spec.split(lpi, 1)[1].split(tpi, 1)[0]
    lines = ['    uy = s.y - r.y', '    uz = s.z - r.z', '    wy = t.y - r.y',
             '    wz = t.z - r.z', '    nx = uy * wz - uz * wy']
    if model != MODEL or any(section.count(x + '\n') != 1 for x in lines):
        return False
    positions = [section.index(x + '\n') for x in lines]
    required = ['public static readonly Rational U = new Rational(1, BigInteger.Pow(2, 53));',
        'public bool Leaf => (Op == Op.Add || Op == Op.Sub) && A.Op == Op.Input && B.Op == Op.Input;',
        'n.C = n.Leaf ? u : (u + Rational.Max(n.A.C, n.B.C)) / (one - u);',
        'n.C = (u + n.A.C + n.B.C + n.A.C * n.B.C) / (one - u);',
        'm = n.Leaf ? $"Math.Abs({val(n)})" : n.Op == Op.Mul ? $"m{val(n.A)} * m{val(n.B)}" : $"m{val(n.A)} + m{val(n.B)}";']
    return positions == sorted(positions) and all(s in compiler for s in required)


def generator_bound():
    u = Fraction(1, 2**53)
    product = (u + 2*u + u*u) / (1-u)
    return 8 * (u + product) / (1-u)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fptaylor_dir', type=Path)
    args = parser.parse_args()
    spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
    compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()
    model = (HERE / 'orient3dlpi_nx.txt').read_text()
    if not source_contract(spec, compiler, model):
        raise SystemExit('model or source changed; re-check the comparison')
    run = subprocess.run([str(HERE / 'run.sh'), str(args.fptaylor_dir), str(HERE / 'orient3dlpi_nx.txt')],
                         text=True, capture_output=True)
    if run.returncode:
        print(run.stderr, file=sys.stderr)
        return 1
    try:
        result = compare(run.stdout, generator_bound())
    except (ValueError, OverflowError) as error:
        print(error, file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
