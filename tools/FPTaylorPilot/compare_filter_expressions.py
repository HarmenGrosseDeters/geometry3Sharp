#!/usr/bin/env python3
"""Compare bounded FPTaylor models of Orient3dLPI and Orient3dTPI filter expressions with generator bounds.

Each model is one expression over the README's restricted domain: segment and plane points in [1,2], r in
[0,0.5], c (LPI) and the TPI point c in [1,2]. The generator bound is the relative rule of
tools/PredicateCompiler/Compiler.cs (filter_bounds) applied to the expression tree, times the magnitude of the
expression over the domain (the magnitude is given in each bound function).
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

# The rules of Compiler.cs filter_bounds, checked against the source before any run.
REQUIRED_RULES = [
    'public static readonly Rational U = new Rational(1, BigInteger.Pow(2, 53));',
    'public bool Leaf => (Op == Op.Add || Op == Op.Sub) && A.Op == Op.Input && B.Op == Op.Input;',
    'n.C = n.Leaf ? u : (u + Rational.Max(n.A.C, n.B.C)) / (one - u);',
    'n.C = (u + n.A.C + n.B.C + n.A.C * n.B.C) / (one - u);',
    'm = n.Leaf ? $"Math.Abs({val(n)})" : n.Op == Op.Mul ? $"m{val(n.A)} * m{val(n.B)}" : $"m{val(n.A)} + m{val(n.B)}";',
]


def rule_add(a, b):
    # Add/Sub of non-leaf operands: C = (u + max(Ca, Cb)) / (1 - u)
    return (U + max(a, b)) / (ONE - U)


def rule_mul(a, b):
    # Mul: C = (u + Ca + Cb + Ca Cb) / (1 - u)
    return (U + a + b + a * b) / (ONE - U)


LEAF = U  # a difference of two inputs (a leaf): relative bound u


def c_nx():
    """C of nx = uy*wz - uz*wy (two products of leaves); magnitude 8 over the LPI domain."""
    product = rule_mul(LEAF, LEAF)
    return rule_add(product, product)


def c_lp():
    """C of lp = nx*prx + ny*pry + nz*prz (and of lq); magnitude 48."""
    m = rule_mul(c_nx(), LEAF)
    lpa = rule_add(m, m)
    return rule_add(lpa, m)


def c_d():
    """C of d = lq - lp (both sides have the same C); magnitude 96."""
    return rule_add(c_lp(), c_lp())


def c_coord_x():
    """C of Nx = pcx*lq - qcx*lp with |pcx|, |qcx| <= 1 (points in [1,2]); magnitude 96."""
    m = rule_mul(LEAF, c_lp())
    return rule_add(m, m)


def c_tpi_n():
    """C of n1x = u1y*w1z - u1z*w1y over the TPI domain (leaves of magnitude <= 1, so n1x <= 2)."""
    return c_nx()


def c_tpi_d1():
    """C of D1 = n1x*a1cx + n1y*a1cy + n1z*a1cz with |a1cx| <= 1; magnitude 6."""
    m = rule_mul(c_tpi_n(), LEAF)
    sa = rule_add(m, m)
    return rule_add(sa, m)


MODEL_NY = """Variables
 float64 sx in [1, 2], float64 sz in [1, 2],
 float64 tx in [1, 2], float64 tz in [1, 2],
 float64 rx in [0, 0.5], float64 rz in [0, 0.5]
;
Definitions
 ux rnd64= sx - rx,
 uz rnd64= sz - rz,
 wx rnd64= tx - rx,
 wz rnd64= tz - rz,
 p rnd64= uz * wx,
 q rnd64= ux * wz
;
Expressions
 rnd64(p - q)
;
"""

MODEL_NZ = """Variables
 float64 sx in [1, 2], float64 sy in [1, 2],
 float64 tx in [1, 2], float64 ty in [1, 2],
 float64 rx in [0, 0.5], float64 ry in [0, 0.5]
;
Definitions
 ux rnd64= sx - rx,
 uy rnd64= sy - ry,
 wx rnd64= tx - rx,
 wy rnd64= ty - ry,
 p rnd64= ux * wy,
 q rnd64= uy * wx
;
Expressions
 rnd64(p - q)
;
"""

# The LPI chain: nx, ny, nz, then lp and lq, then d = lq - lp.
MODEL_D = """Variables
 float64 sx in [1, 2], float64 sy in [1, 2], float64 sz in [1, 2],
 float64 tx in [1, 2], float64 ty in [1, 2], float64 tz in [1, 2],
 float64 px in [1, 2], float64 py in [1, 2], float64 pz in [1, 2],
 float64 qx in [1, 2], float64 qy in [1, 2], float64 qz in [1, 2],
 float64 rx in [0, 0.5], float64 ry in [0, 0.5], float64 rz in [0, 0.5]
;
Definitions
 ux rnd64= sx - rx,
 uy rnd64= sy - ry,
 uz rnd64= sz - rz,
 wx rnd64= tx - rx,
 wy rnd64= ty - ry,
 wz rnd64= tz - rz,
 nxa rnd64= uy * wz,
 nxb rnd64= uz * wy,
 nx rnd64= nxa - nxb,
 nya rnd64= uz * wx,
 nyb rnd64= ux * wz,
 ny rnd64= nya - nyb,
 nza rnd64= ux * wy,
 nzb rnd64= uy * wx,
 nz rnd64= nza - nzb,
 prx rnd64= px - rx,
 pry rnd64= py - ry,
 prz rnd64= pz - rz,
 qrx rnd64= qx - rx,
 qry rnd64= qy - ry,
 qrz rnd64= qz - rz,
 lp1 rnd64= nx * prx,
 lp2 rnd64= ny * pry,
 lp3 rnd64= nz * prz,
 lpa rnd64= lp1 + lp2,
 lp rnd64= lpa + lp3,
 lq1 rnd64= nx * qrx,
 lq2 rnd64= ny * qry,
 lq3 rnd64= nz * qrz,
 lqa rnd64= lq1 + lq2,
 lq rnd64= lqa + lq3
;
Expressions
 rnd64(lq - lp)
;
"""

# The Nx chain of d plus the Orient3d point o (c in the spec): Nx = pcx*lq - qcx*lp.
MODEL_COORD_X = """Variables
 float64 sx in [1, 2], float64 sy in [1, 2], float64 sz in [1, 2],
 float64 tx in [1, 2], float64 ty in [1, 2], float64 tz in [1, 2],
 float64 px in [1, 2], float64 py in [1, 2], float64 pz in [1, 2],
 float64 qx in [1, 2], float64 qy in [1, 2], float64 qz in [1, 2],
 float64 rx in [0, 0.5], float64 ry in [0, 0.5], float64 rz in [0, 0.5],
 float64 ox in [1, 2], float64 oy in [1, 2], float64 oz in [1, 2]
;
Definitions
 ux rnd64= sx - rx,
 uy rnd64= sy - ry,
 uz rnd64= sz - rz,
 wx rnd64= tx - rx,
 wy rnd64= ty - ry,
 wz rnd64= tz - rz,
 nxa rnd64= uy * wz,
 nxb rnd64= uz * wy,
 nx rnd64= nxa - nxb,
 nya rnd64= uz * wx,
 nyb rnd64= ux * wz,
 ny rnd64= nya - nyb,
 nza rnd64= ux * wy,
 nzb rnd64= uy * wx,
 nz rnd64= nza - nzb,
 prx rnd64= px - rx,
 pry rnd64= py - ry,
 prz rnd64= pz - rz,
 qrx rnd64= qx - rx,
 qry rnd64= qy - ry,
 qrz rnd64= qz - rz,
 lp1 rnd64= nx * prx,
 lp2 rnd64= ny * pry,
 lp3 rnd64= nz * prz,
 lpa rnd64= lp1 + lp2,
 lp rnd64= lpa + lp3,
 lq1 rnd64= nx * qrx,
 lq2 rnd64= ny * qry,
 lq3 rnd64= nz * qrz,
 lqa rnd64= lq1 + lq2,
 lq rnd64= lqa + lq3,
 pcx rnd64= px - ox,
 qcx rnd64= qx - ox,
 cm1 rnd64= pcx * lq,
 cm2 rnd64= qcx * lp,
 cxr rnd64= cm1 - cm2
;
Expressions
 rnd64(cxr)
;
"""

MODEL_LPI_LEAF = """Variables
 float64 px in [1, 2], float64 rx in [0, 0.5]
;
Definitions
 prx rnd64= px - rx
;
Expressions
 rnd64(prx)
;
"""

MODEL_TPI_LEAF = """Variables
 float64 ax in [1, 2], float64 bx in [1, 2]
;
Definitions
 u rnd64= bx - ax
;
Expressions
 rnd64(u)
;
"""

MODEL_TPI_N1X = """Variables
 float64 ax in [1, 2], float64 ay in [1, 2], float64 az in [1, 2],
 float64 bx in [1, 2], float64 by in [1, 2], float64 bz in [1, 2],
 float64 cx in [1, 2], float64 cy in [1, 2], float64 cz in [1, 2]
;
Definitions
 u1y rnd64= by - ay,
 u1z rnd64= bz - az,
 w1y rnd64= cy - ay,
 w1z rnd64= cz - az,
 pa rnd64= u1y * w1z,
 pb rnd64= u1z * w1y,
 n1x rnd64= pa - pb
;
Expressions
 rnd64(n1x)
;
"""

# The first plane's normal n1 (all three components) and D1 = n1 . (a1 - c).
MODEL_TPI_D1 = """Variables
 float64 ax in [1, 2], float64 ay in [1, 2], float64 az in [1, 2],
 float64 bx in [1, 2], float64 by in [1, 2], float64 bz in [1, 2],
 float64 cx in [1, 2], float64 cy in [1, 2], float64 cz in [1, 2],
 float64 ox in [1, 2], float64 oy in [1, 2], float64 oz in [1, 2]
;
Definitions
 u1x rnd64= bx - ax,
 u1y rnd64= by - ay,
 u1z rnd64= bz - az,
 w1x rnd64= cx - ax,
 w1y rnd64= cy - ay,
 w1z rnd64= cz - az,
 pa rnd64= u1y * w1z,
 pb rnd64= u1z * w1y,
 n1x rnd64= pa - pb,
 qa rnd64= u1z * w1x,
 qb rnd64= u1x * w1z,
 n1y rnd64= qa - qb,
 ra rnd64= u1x * w1y,
 rb rnd64= u1y * w1x,
 n1z rnd64= ra - rb,
 a1cx rnd64= ax - ox,
 a1cy rnd64= ay - oy,
 a1cz rnd64= az - oz,
 m1 rnd64= n1x * a1cx,
 m2 rnd64= n1y * a1cy,
 m3 rnd64= n1z * a1cz,
 sa rnd64= m1 + m2,
 D1 rnd64= sa + m3
;
Expressions
 rnd64(D1)
;
"""

MODELS = {
    'ny': {'file': 'orient3dlpi_ny.txt', 'text': MODEL_NY, 'section': 'LPI',
           'lines': ['    ny = uz * wx - ux * wz'],
           'bound': compare_nx.generator_bound},  # 8 C_nx
    'nz': {'file': 'orient3dlpi_nz.txt', 'text': MODEL_NZ, 'section': 'LPI',
           'lines': ['    nz = ux * wy - uy * wx'],
           'bound': compare_nx.generator_bound},
    'd': {'file': 'orient3dlpi_d.txt', 'text': MODEL_D, 'section': 'LPI',
          'lines': ['    lp = nx * prx + ny * pry + nz * prz', '    lq = nx * qrx + ny * qry + nz * qrz',
                    '    d = lq - lp', '    prx = p.x - r.x', '    qrx = q.x - r.x'],
          'bound': lambda: 96 * c_d()},
    'coord_x': {'file': 'orient3dlpi_coord_x.txt', 'text': MODEL_COORD_X, 'section': 'LPI',
                'lines': ['    Nx = pcx * lq - qcx * lp', '    pcx = p.x - c.x', '    qcx = q.x - c.x'],
                'bound': lambda: 96 * c_coord_x()},
    'lpi_leaf': {'file': 'orient3dlpi_leaf.txt', 'text': MODEL_LPI_LEAF, 'section': 'LPI',
                 'lines': ['    prx = p.x - r.x'],
                 'bound': lambda: 2 * LEAF},  # |prx| <= 2
    'tpi_leaf': {'file': 'orient3dtpi_leaf.txt', 'text': MODEL_TPI_LEAF, 'section': 'TPI',
                 'lines': ['    u1_x = b1.x - a1.x'],
                 'bound': lambda: 1 * LEAF},
    'tpi_n1x': {'file': 'orient3dtpi_n1x.txt', 'text': MODEL_TPI_N1X, 'section': 'TPI',
                'lines': ['    n1x = u1_y * w1_z - u1_z * w1_y', '    u1_y = b1.y - a1.y', '    w1_z = c1.z - a1.z',
                          '    u1_z = b1.z - a1.z', '    w1_y = c1.y - a1.y'],
                'bound': lambda: 2 * c_tpi_n()},
    'tpi_D1': {'file': 'orient3dtpi_D1.txt', 'text': MODEL_TPI_D1, 'section': 'TPI',
               'lines': ['    D1 = n1x * a1cx + n1y * a1cy + n1z * a1cz', '    a1cx = a1.x - c.x'],
               'bound': lambda: 6 * c_tpi_d1()},
}


def section_of(spec, section):
    start = spec.index(f'ipredicate Orient3d{section}(')
    end = spec.find('\nipredicate ', start + 1)
    return spec[start:] if end < 0 else spec[start:end]


def source_contract(spec, compiler, name):
    if spec.count('ipredicate Orient3dLPI(') != 1 or spec.count('ipredicate Orient3dTPI(') != 1:
        return False
    section = section_of(spec, MODELS[name]['section'])
    if any(section.count(line + '\n') != 1 for line in MODELS[name]['lines']):
        return False
    return all(rule in compiler for rule in REQUIRED_RULES)


def run_one(name, fptaylor_dir):
    model_file = HERE / MODELS[name]['file']
    spec = (ROOT / 'tools/PredicateCompiler/indirect.spec').read_text()
    compiler = (ROOT / 'tools/PredicateCompiler/Compiler.cs').read_text()
    if model_file.read_text() != MODELS[name]['text'] or not source_contract(spec, compiler, name):
        raise SystemExit(f'{name}: model or source changed; re-check the comparison')
    run = subprocess.run([str(HERE / 'run.sh'), str(fptaylor_dir), str(model_file)], text=True, capture_output=True)
    if run.returncode:
        print(run.stderr, file=sys.stderr)
        return 1
    try:
        result = compare(run.stdout, MODELS[name]['bound']())
    except (ValueError, OverflowError) as error:
        print(f'{name}: {error}', file=sys.stderr)
        return 1
    result['expression'] = name
    print(json.dumps(result, indent=2))
    return 0


def selected(names):
    return list(names) if names else list(MODELS)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fptaylor_dir', type=Path)
    parser.add_argument('names', nargs='*', choices=list(MODELS),
                        help='expressions to run (default: all); argparse rejects a default list with choices')
    args = parser.parse_args()
    status = 0
    for name in selected(args.names):
        status |= run_one(name, args.fptaylor_dir)
    return status


if __name__ == '__main__':
    sys.exit(main())
