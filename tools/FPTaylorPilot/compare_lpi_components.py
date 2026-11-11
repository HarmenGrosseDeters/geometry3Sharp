#!/usr/bin/env python3
"""Compare bounded models of Orient3dLPI's ny, nz and denominator d with their generator bounds.

Each model is one expression with its own input ranges (the README's restricted domain: segment and plane
points in [1,2] (p, q, s, t) and r in [0,0.5]). The generator bound is the relative rule of
tools/PredicateCompiler/Compiler.cs (filter_bounds) evaluated over the magnitude tree of the domain.
"""
from fractions import Fraction
from pathlib import Path
import argparse
import json
import subprocess
import sys
from compare_ux import compare
import compare_nx

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
U = Fraction(1, 2**53)
ONE = Fraction(1)

# The rules of Compiler.cs filter_bounds, applied to the expression trees of the models.
REQUIRED_RULES = [
    'public static readonly Rational U = new Rational(1, BigInteger.Pow(2, 53));',
    'public bool Leaf => (Op == Op.Add || Op == Op.Sub) && A.Op == Op.Input && B.Op == Op.Input;',
    'n.C = n.Leaf ? u : (u + Rational.Max(n.A.C, n.B.C)) / (one - u);',
    'n.C = (u + n.A.C + n.B.C + n.A.C * n.B.C) / (one - u);',
    'm = n.Leaf ? $"Math.Abs({val(n)})" : n.Op == Op.Mul ? $"m{val(n.A)} * m{val(n.B)}" : $"m{val(n.A)} + m{val(n.B)}";',
]


def rule_add(a, b):
    # Add/Sub of two non-leaf operands (and the magnitude sum): C = (u + max(Ca, Cb)) / (1 - u)
    return (U + max(a, b)) / (ONE - U)


def rule_mul(a, b):
    # Mul: C = (u + Ca + Cb + Ca Cb) / (1 - u)
    return (U + a + b + a * b) / (ONE - U)


def leaf_c():
    return U  # a difference of two inputs (a leaf), relative bound u


def d_bound():
    """Absolute bound of d = lq - lp over the domain: C_d * M_d with M_d = 96 (magnitude tree below)."""
    # nx = nxa - nxb, products of two leaves: C_nx as in compare_nx.generator_bound
    c_prod = rule_mul(leaf_c(), leaf_c())
    c_nx = rule_add(c_prod, c_prod)
    # nx * prx (prx a leaf): the product rule; the same for ny * pry and nz * prz
    c_m = rule_mul(c_nx, leaf_c())
    # lpa = lp1 + lp2 and lp = lpa + lp3 (sums of products with the same C)
    c_lpa = rule_add(c_m, c_m)
    c_lp = rule_add(c_lpa, c_m)
    # lq has the same structure; d = lq - lp
    c_d = rule_add(c_lp, c_lp)
    # magnitudes over the domain: |leaf| <= 2, |nx| <= 8 (2*2 + 2*2), |nx prx| <= 16, lp <= 48, d <= 96
    m_d = Fraction(96)
    return c_d * m_d

MODEL_NY = 'Variables\n float64 sx in [1, 2], float64 sz in [1, 2],\n float64 tx in [1, 2], float64 tz in [1, 2],\n float64 rx in [0, 0.5], float64 rz in [0, 0.5]\n;\nDefinitions\n ux rnd64= sx - rx,\n uz rnd64= sz - rz,\n wx rnd64= tx - rx,\n wz rnd64= tz - rz,\n p rnd64= uz * wx,\n q rnd64= ux * wz\n;\nExpressions\n rnd64(p - q)\n;\n'

MODEL_NZ = 'Variables\n float64 sx in [1, 2], float64 sy in [1, 2],\n float64 tx in [1, 2], float64 ty in [1, 2],\n float64 rx in [0, 0.5], float64 ry in [0, 0.5]\n;\nDefinitions\n ux rnd64= sx - rx,\n uy rnd64= sy - ry,\n wx rnd64= tx - rx,\n wy rnd64= ty - ry,\n p rnd64= ux * wy,\n q rnd64= uy * wx\n;\nExpressions\n rnd64(p - q)\n;\n'

MODEL_D = 'Variables\n float64 sx in [1, 2], float64 sy in [1, 2], float64 sz in [1, 2],\n float64 tx in [1, 2], float64 ty in [1, 2], float64 tz in [1, 2],\n float64 px in [1, 2], float64 py in [1, 2], float64 pz in [1, 2],\n float64 qx in [1, 2], float64 qy in [1, 2], float64 qz in [1, 2],\n float64 rx in [0, 0.5], float64 ry in [0, 0.5], float64 rz in [0, 0.5]\n;\nDefinitions\n ux rnd64= sx - rx,\n uy rnd64= sy - ry,\n uz rnd64= sz - rz,\n wx rnd64= tx - rx,\n wy rnd64= ty - ry,\n wz rnd64= tz - rz,\n nxa rnd64= uy * wz,\n nxb rnd64= uz * wy,\n nx rnd64= nxa - nxb,\n nya rnd64= uz * wx,\n nyb rnd64= ux * wz,\n ny rnd64= nya - nyb,\n nza rnd64= ux * wy,\n nzb rnd64= uy * wx,\n nz rnd64= nza - nzb,\n prx rnd64= px - rx,\n pry rnd64= py - ry,\n prz rnd64= pz - rz,\n qrx rnd64= qx - rx,\n qry rnd64= qy - ry,\n qrz rnd64= qz - rz,\n lp1 rnd64= nx * prx,\n lp2 rnd64= ny * pry,\n lp3 rnd64= nz * prz,\n lpa rnd64= lp1 + lp2,\n lp rnd64= lpa + lp3,\n lq1 rnd64= nx * qrx,\n lq2 rnd64= ny * qry,\n lq3 rnd64= nz * qrz,\n lqa rnd64= lq1 + lq2,\n lq rnd64= lqa + lq3\n;\nExpressions\n rnd64(lq - lp)\n;\n'


MODELS = {
    'ny': {
        'file': 'orient3dlpi_ny.txt',
        'text': MODEL_NY,
        'lines': ['    ny = uz * wx - ux * wz'],
        'bound': lambda: compare_nx.generator_bound(),
    },
    'nz': {
        'file': 'orient3dlpi_nz.txt',
        'text': MODEL_NZ,
        'lines': ['    nz = ux * wy - uy * wx'],
        'bound': lambda: compare_nx.generator_bound(),
    },
    'd': {
        'file': 'orient3dlpi_d.txt',
        'text': MODEL_D,
        'lines': ['    lp = nx * prx + ny * pry + nz * prz', '    lq = nx * qrx + ny * qry + nz * qrz',
                  '    d = lq - lp', '    prx = p.x - r.x', '    qrx = q.x - r.x',
                  '    nx = uy * wz - uz * wy'],
        'bound': d_bound,
    },
}


def source_contract(spec, compiler, name):
    lpi, tpi = 'ipredicate Orient3dLPI(', 'ipredicate Orient3dTPI('
    if spec.count(lpi) != 1 or spec.count(tpi) != 1 or spec.index(lpi) > spec.index(tpi):
        return False
    section = spec.split(lpi, 1)[1].split(tpi, 1)[0]
    lines = MODELS[name]['lines']
    if any(section.count(x + chr(10)) != 1 for x in lines):
        return False
    return all(rule in compiler for rule in REQUIRED_RULES)


def run_one(name, fptaylor_dir):
    spec_model = HERE / MODELS[name]['file']
    spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
    compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()
    if (spec_model.read_text() != MODELS[name]['text'] or not source_contract(spec, compiler, name)):
        raise SystemExit(f'{name}: model or source changed; re-check the comparison')
    run = subprocess.run([str(HERE / 'run.sh'), str(fptaylor_dir), str(spec_model)], text=True, capture_output=True)
    if run.returncode:
        print(run.stderr, file=sys.stderr)
        return 1
    try:
        bound = MODELS[name]['bound']()
        result = compare(run.stdout, bound)
    except (ValueError, OverflowError) as error:
        print(f'{name}: {error}', file=sys.stderr)
        return 1
    result['expression'] = name
    print(json.dumps(result, indent=2))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fptaylor_dir', type=Path)
    parser.add_argument('names', nargs='*', default=list(MODELS), choices=list(MODELS))
    args = parser.parse_args()
    status = 0
    for name in args.names:
        status |= run_one(name, args.fptaylor_dir)
    return status


if __name__ == '__main__':
    sys.exit(main())
