#!/usr/bin/env python3
"""The size of Orient3dBound (math/ExactPredicates.cs, stage A of Orient3d: (7 + 56 u) u * permanent), checked with
FPTaylor on a narrow box, and the insufficiency of 4 u by an exact replay (docs/qa/exact-bound-inventory.md, gap G6,
the hand mutant H5 of #1572).

The bound is semi-static (a constant times the computed permanent), FPTaylor's result is an absolute bound over a box.
On a box whose coordinates vary by at most 2^-30 around one point the permanent varies by a factor below 1 + 2^-25, so
FPTaylor's absolute bound E over the box compared with K * (the smallest computed permanent on the box) checks the
constant at that point's neighbourhood. This is a check at one point, not over all inputs: the proof over all inputs is
EP-2 of tools/ErrorBoundAudit (monomial dominance, exact rationals). Scaling: Orient3d is homogeneous of degree 3, and a
common 2^k scale of the inputs is exact without underflow and overflow, so the check holds for every 2^k copy of the box in
that range; the subnormal range is the underflow term's (UnderflowUnit, with_underflow), not modelled here.

The point is the witness of tests/geometry3Sharp.Tests/ExactPredicatesFilterBoundTests.cs (its evaluated error is
4.30 u (permanent + |det|)): there the exact replay shows that a constant of 4 u (the bound of VolumeSumWithBound is then
about 4 u (permanent + |det|)) is exceeded.

FPTaylor checks the expression of MODEL, not the C# code: source_contract pins the code text of Orient3dFilter and of
Orient3d's differences, so a code change cannot leave the model passing.

    python3 tools/FPTaylorPilot/compare_orient3d.py --write-model      # (re)writes orient3d_stagea.txt from the witness
    python3 tools/FPTaylorPilot/compare_orient3d.py --symbolic-taylor  # the C# Symbolic Taylor probe, writes orient3d_stagea_symtaylor_result.json
    python3 tools/FPTaylorPilot/compare_orient3d.py <fptaylor_dir>     # optional cross-check: OCaml FPTaylor, writes orient3d_stagea_result.json
"""
import argparse
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
MODEL_FILE = HERE / 'orient3d_stagea.txt'
RESULT_FILE = HERE / 'orient3d_stagea_result.json'
BOX_FILE = HERE / 'orient3d_stagea_box.json'
SYMTAYLOR_FILE = HERE / 'orient3d_stagea_symtaylor_result.json'
PROBE = ROOT / 'benchmarks/SymbolicTaylorBoundsProbe/SymbolicTaylorBoundsProbe.csproj'
PROBE_MODELS = ROOT / 'benchmarks/SymbolicTaylorBoundsProbe/PredicateModels.cs'
U = Fraction(1, 2**53)
K = (7 + 56 * U) * U
HALF_WIDTH = Fraction(1, 2**30)

# o, a, b, c of the witness (ExactPredicatesFilterBoundTests.Coordinates); Orient3d(o, a, b, c) evaluates
# w . (u x v) with u = a - o, v = b - o, w = c - o
WITNESS = [0.3640557419018742, -1.4664898648868974, 0.6463590366829446,
           -1.8905698592157203, 0.6400851890221262, 0.6474126792005838,
           4.376956089525014, 0.13574913818499035, 0.17814099369685746,
           -0.7130060362935128, 7.8161039759070094, -0.23345173296915972]
NAMES = ['ox', 'oy', 'oz', 'ax', 'ay', 'az', 'bx', 'by', 'bz', 'cx', 'cy', 'cz']

CONTRACT = [
    'int s = Orient3dFilter(b.x - a.x, b.y - a.y, b.z - a.z, c.x - a.x, c.y - a.y, c.z - a.z, d.x - a.x, d.y - a.y, d.z - a.z);',
    'internal const double Orient3dBound = (7.0 + 56.0 * Epsilon) * Epsilon;',
    'double uyvz = uy * vz, uzvy = uz * vy, uzvx = uz * vx, uxvz = ux * vz, uxvy = ux * vy, uyvx = uy * vx;',
    'double det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx);',
    'double permanent = Math.Abs(wx) * (Math.Abs(uyvz) + Math.Abs(uzvy))',
    'double bound = with_underflow(Orient3dBound * permanent, Math.Abs(wx) + Math.Abs(wy) + Math.Abs(wz) + 1);',
    'if (det > bound && permanent > UnderflowLimit) return 1;',
]


def _normalize(s):
    return re.sub(r'\s+', '', s)


def source_contract(root=ROOT):
    text = _normalize((root / 'math/ExactPredicates.cs').read_text(encoding='utf-8'))
    return [line for line in CONTRACT if _normalize(line) not in text]


def _dec(q):
    """the exact decimal of a dyadic rational"""
    q = Fraction(q)
    sign = '-' if q < 0 else ''
    q = abs(q)
    k = q.denominator.bit_length() - 1
    assert q.denominator == 2**k
    n = q.numerator * 5**k
    s = str(n).rjust(k + 1, '0')
    return sign + (s[:-k] + '.' + s[-k:] if k else s)


def model():
    lines = ['Variables']
    vs = []
    for n, x in zip(NAMES, WITNESS):
        c = Fraction(x)
        vs.append(f' float64 {n} in [{_dec(c - HALF_WIDTH)}, {_dec(c + HALF_WIDTH)}]')
    lines.append(',\n'.join(vs))
    lines += [';', 'Definitions',
              ' ux rnd64= ax - ox, uy rnd64= ay - oy, uz rnd64= az - oz,',
              ' vx rnd64= bx - ox, vy rnd64= by - oy, vz rnd64= bz - oz,',
              ' wx rnd64= cx - ox, wy rnd64= cy - oy, wz rnd64= cz - oz,',
              ' uyvz rnd64= uy * vz, uzvy rnd64= uz * vy, uzvx rnd64= uz * vx,',
              ' uxvz rnd64= ux * vz, uxvy rnd64= ux * vy, uyvx rnd64= uy * vx',
              ';', 'Expressions',
              # the order of Orient3dFilter: wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx), left to right
              ' rnd64(rnd64(rnd64(wx * rnd64(uyvz - uzvy)) + rnd64(wy * rnd64(uzvx - uxvz))) + rnd64(wz * rnd64(uxvy - uyvx)))',
              ';', '']
    return '\n'.join(lines)


def _interval_mul(a, b):
    ps = [a[0] * b[0], a[0] * b[1], a[1] * b[0], a[1] * b[1]]
    return (min(ps), max(ps))


def _abs_lo(i):
    return Fraction(0) if i[0] <= 0 <= i[1] else min(abs(i[0]), abs(i[1]))


def permanent_lower_bound():
    """a lower bound of the computed permanent over the box: the exact permanent of the exact differences, by interval
    arithmetic in exact rationals, times (1 - 16 u) for the roundings of the differences (u each, three factors), the
    products and the permanent's own sums and products (all of non-negative terms)"""
    box = [(Fraction(x) - HALF_WIDTH, Fraction(x) + HALF_WIDTH) for x in WITNESS]
    o, a, b, c = box[0:3], box[3:6], box[6:9], box[9:12]
    sub = lambda p, q: [(p[k][0] - q[k][1], p[k][1] - q[k][0]) for k in range(3)]
    u, v, w = sub(a, o), sub(b, o), sub(c, o)
    m = lambda i, j: _abs_lo(_interval_mul(i, j))
    perm = (_abs_lo(w[0]) * (m(u[1], v[2]) + m(u[2], v[1])) + _abs_lo(w[1]) * (m(u[2], v[0]) + m(u[0], v[2]))
            + _abs_lo(w[2]) * (m(u[0], v[1]) + m(u[1], v[0])))
    return perm * (1 - 16 * U)


def exact_replay():
    """the witness: the evaluated determinant (binary64, the order of Orient3dFilter) against the exact one"""
    ox, oy, oz, ax, ay, az, bx, by, bz, cx, cy, cz = WITNESS
    ux, uy, uz, vx, vy, vz, wx, wy, wz = ax - ox, ay - oy, az - oz, bx - ox, by - oy, bz - oz, cx - ox, cy - oy, cz - oz
    uyvz, uzvy, uzvx, uxvz, uxvy, uyvx = uy * vz, uz * vy, uz * vx, ux * vz, ux * vy, uy * vx
    det = wx * (uyvz - uzvy) + wy * (uzvx - uxvz) + wz * (uxvy - uyvx)
    perm = abs(wx) * (abs(uyvz) + abs(uzvy)) + abs(wy) * (abs(uzvx) + abs(uxvz)) + abs(wz) * (abs(uxvy) + abs(uyvx))
    F = [Fraction(x) for x in WITNESS]
    eu = [F[3] - F[0], F[4] - F[1], F[5] - F[2]]
    ev = [F[6] - F[0], F[7] - F[1], F[8] - F[2]]
    ew = [F[9] - F[0], F[10] - F[1], F[11] - F[2]]
    exact = (ew[0] * (eu[1] * ev[2] - eu[2] * ev[1]) + ew[1] * (eu[2] * ev[0] - eu[0] * ev[2])
             + ew[2] * (eu[0] * ev[1] - eu[1] * ev[0]))
    err = abs(Fraction(det) - exact)
    ratio = err / (U * (Fraction(perm) + abs(Fraction(det))))
    return dict(det=det, permanent=perm, error_over_u_perm_plus_det=float(ratio), error_over_u_perm=float(err / (U * Fraction(perm))),
                # a constant of 4 u: the error exceeds 4 u (perm + |det|) (1 + 16 u), the bound VolumeSumWithBound would have
                four_u_insufficient=err > 4 * U * (Fraction(perm) + abs(Fraction(det))) * (1 + 16 * U),
                seven_u_sufficient=err <= K * Fraction(perm))


def compare(fptaylor_bound):
    """FPTaylor's absolute bound E (exact rational) against K * the smallest computed permanent over the box"""
    lo = permanent_lower_bound()
    E = Fraction(fptaylor_bound)
    return dict(fptaylor_bound_exact=str(E), permanent_lower_bound=str(lo), library_bound_exact=str(K * lo),
                fptaylor_over_u_permanent=float(E / (U * lo)), ok=E <= K * lo)


def box():
    """the box as round-trip doubles (each edge c +- 2^-30 is a double: the ulps of the coordinates are below 2^-30)"""
    names = ['o.x', 'o.y', 'o.z', 'a.x', 'a.y', 'a.z', 'b.x', 'b.y', 'b.z', 'c.x', 'c.y', 'c.z']
    lo, hi = [], []
    for x in WITNESS:
        l, h = float(Fraction(x) - HALF_WIDTH), float(Fraction(x) + HALF_WIDTH)
        assert Fraction(l) == Fraction(x) - HALF_WIDTH and Fraction(h) == Fraction(x) + HALF_WIDTH
        lo.append(repr(l))
        hi.append(repr(h))
    return dict(names=names, lo=lo, hi=hi)


def probe_model_hash():
    """SHA-256 of the text of PredicateModels.BuildOrient3dStageA (the model the C# probe bounds), line endings normalized: stored in the
    checked-in probe result, so that a change of the model without a new run (a stale result) fails the unittest"""
    text = PROBE_MODELS.read_text(encoding='utf-8').replace('\r\n', '\n')
    start = text.index('public static ExprDag BuildOrient3dStageA()')
    end = text.index('    public static ExprDag Build', start + 1)
    return hashlib.sha256(text[start:end].strip().encode('utf-8')).hexdigest()


def compare_symtaylor(result):
    """the C# Symbolic Taylor bound (benchmarks/SymbolicTaylorBoundsProbe, a box-max consistency check, not a proof of the
    pointwise invariant) against K * the smallest computed permanent over the box, and the code's 4 u against it"""
    lo = permanent_lower_bound()
    out = {}
    for mode in ('sign_filter', 'with_final_rounding'):
        E = Fraction(float(result[mode]['refined_bound']))
        L = Fraction(float(result[mode]['sampled_lower']))
        out[mode] = dict(bound_over_u_permanent=float(E / (U * lo)), sampled_lower_over_u_permanent=float(L / (U * lo)),
                         within_library_bound=E <= K * lo,
                         # a constant of 4 u is below the sampled first-order error: insufficient on this box
                         four_u_below_sampled_error=4 * U * lo * (1 + 16 * U) < L)
    return out


def run_symtaylor():
    BOX_FILE.write_text(json.dumps(box(), indent=1) + '\n')
    run = subprocess.run(['dotnet', 'run', '--project', str(PROBE), '-c', 'Release', '--', '--orient3d-stagea', str(BOX_FILE)],
                         text=True, capture_output=True)
    if run.returncode:
        print(run.stdout, run.stderr, file=sys.stderr)
        return 1
    result = json.loads(run.stdout.strip().splitlines()[-1])
    result['model_sha256'] = probe_model_hash()
    SYMTAYLOR_FILE.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(dict(result=result, compare=compare_symtaylor(result)), indent=2))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fptaylor_dir', type=Path, nargs='?')
    parser.add_argument('--write-model', action='store_true')
    parser.add_argument('--symbolic-taylor', action='store_true', help='run the C# Symbolic Taylor probe (no OCaml)')
    args = parser.parse_args()
    missing = source_contract()
    if missing:
        raise SystemExit(f'source changed, re-check the model: {missing}')
    if args.write_model:
        MODEL_FILE.write_text(model())
        BOX_FILE.write_text(json.dumps(box(), indent=1) + '\n')
        return 0
    if args.symbolic_taylor:
        return run_symtaylor()
    if MODEL_FILE.read_text() != model():
        raise SystemExit('orient3d_stagea.txt is not the model of the witness: --write-model')
    run = subprocess.run([str(HERE / 'run.sh'), str(args.fptaylor_dir), str(MODEL_FILE)], text=True, capture_output=True)
    if run.returncode:
        print(run.stderr, file=sys.stderr)
        return 1
    from compare_ux import compare as parse_compare
    parsed = parse_compare(run.stdout, Fraction(1))      # the exact rational of FPTaylor's hexadecimal result
    result = compare(Fraction(parsed['fptaylor_bound_exact']))
    result['fptaylor_bound_hex'] = parsed['fptaylor_bound_hex']
    result['replay'] = {k: (bool(v) if isinstance(v, bool) else v) for k, v in exact_replay().items()}
    RESULT_FILE.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))
    return 0 if result['ok'] and result['replay']['four_u_insufficient'] else 1


if __name__ == '__main__':
    sys.exit(main())
