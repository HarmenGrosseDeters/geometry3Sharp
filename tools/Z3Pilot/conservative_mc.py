"""Source-coupled MC adapter certificate; the continuous enclosure lemma is Jacobson's.
See README: this checks the discrete lift/closed-chain obligations, not a new
SMT proof of the continuous-function theorem. UNKNOWN and vacuity are failures.
"""
import argparse
import collections
from fractions import Fraction
import hashlib
import json
import math
import os
import pathlib
import re
import time

import z3 as z

ROOT = pathlib.Path(__file__).resolve().parents[2]
CORNERS = ((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1),
           (0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1))
EDGES = ((0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6),
         (6, 7), (7, 4), (0, 4), (1, 5), (2, 6), (3, 7))
# Outward-oriented faces, using the source's (x,y,z) corner convention.
FACES = ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1),
         (3, 2, 6, 7), (0, 3, 7, 4), (1, 5, 6, 2))
TABLE_SHA = '0f9447f4b6579084662e91ef14e1ca2727bf665d9bd8818ababd8e3948c3f3ed'


def parse_table(text):
    body = text.split('static readonly int[,] triTable', 1)[1].split('};', 1)[0]
    rows = [list(map(int, re.findall(r'-?\d+', row)))
            for row in re.findall(r'\{([^{}]+)\}', body)]
    if len(rows) != 256 or any(len(row) != 16 for row in rows):
        raise ValueError('unexpected table dimensions')
    for row in rows:
        count = row.index(-1)
        if count % 3 or any(e != -1 for e in row[count:]) or any(e not in range(12) for e in row[:count]):
            raise ValueError('invalid triangle row')
    return rows


def source_contracts():
    result = {}
    tables = []
    for name in ('MarchingCubes', 'MarchingCubesPro'):
        path = ROOT / 'mesh_generators' / (name + '.cs')
        text = path.read_text(encoding='utf-8-sig')
        if hashlib.sha256(text.encode()).hexdigest() != SOURCE_SHA[name]:
            raise ValueError('MC source changed: review classification, grid, emission and sampling')
        rows = parse_table(text)
        digest = hashlib.sha256(repr(rows).encode()).hexdigest()
        if digest != TABLE_SHA:
            raise ValueError('case table changed: review separator certificate')
        # Reviewed method body hash also covers branch ordering, clamp and arithmetic.
        method = text[text.index('        void find_iso('):]
        method = method[:method.index('\n        }') + len('\n        }')]
        normalize = re.sub(r'\s+', '', method)
        expected = METHOD_SHA[name]
        if hashlib.sha256(normalize.encode()).hexdigest() != expected:
            raise ValueError('find_iso changed: review LinearEdges and FP model')
        edge_body = text.split('static readonly int[,] edge_indices', 1)[1].split('};', 1)[0]
        edges = [tuple(map(int, re.findall(r'\d+', v))) for v in re.findall(r'\{([^{}]+)\}', edge_body)]
        if tuple(edges) != EDGES:
            raise ValueError('edge numbering changed')
        masks = text.split('static readonly int[] edgeTable', 1)[1].split('};', 1)[0]
        masks = [int(v, 16) for v in re.findall(r'0x[0-9a-fA-F]+', masks)]
        if len(masks) != 256:
            raise ValueError('edge mask dimensions')
        for case in range(256):
            crossing = {e for e, (a, b) in enumerate(EDGES) if bool(case & (1 << a)) != bool(case & (1 << b))}
            if masks[case] != sum(1 << e for e in crossing):
                raise ValueError('edge mask/classification mismatch')
            if {e for e in rows[case] if e >= 0} != crossing:
                raise ValueError('table references noncrossing or omits crossing')
        tables.append(rows)
        result[str(path.relative_to(ROOT))] = dict(table_sha256=digest, method_sha256=expected)
    if tables[0] != tables[1]:
        raise ValueError('variants diverged')
    return tables[0], result


# Review tripwire, not a semantic check: these whole-file hashes only force a re-review when either file changes.
# The link between find_iso and the FP model is a review obligation; updating the hashes after a change (eg one
# that restores the endpoint nudge) keeps this pilot green. The semantic content is the table certificate.
SOURCE_SHA = {'MarchingCubes': '8b1b66a60797dc7d82722c091801fda47642e84e288623267f24ec6ac4227b86', 'MarchingCubesPro': '41b805f4e1b3128b12bbf9a5cff3dd7edd0c09b4a4fffbafb55d03d569a73042'}
METHOD_SHA = {'MarchingCubes': '46dc0c63f9101a7eb27d973c83e49f5678859f49dc80a42a37717aa95a534d50', 'MarchingCubesPro': '46dc0c63f9101a7eb27d973c83e49f5678859f49dc80a42a37717aa95a534d50'}  # Reviewed bodies.


def triangles(row):
    t = row[:row.index(-1)]
    return [tuple(t[i:i+3]) for i in range(0, len(t), 3)]


def face_segments(tris, face):
    fe = {i for i, (a, b) in enumerate(EDGES) if a in face and b in face}
    counts = collections.Counter()
    for tri in tris:
        for a, b in zip(tri, tri[1:] + tri[:1]):
            if a in fe and b in fe:
                counts[a, b] += 1
    for a, b in list(counts):
        n = min(counts[a, b], counts[b, a])
        counts[a, b] -= n
        counts[b, a] -= n
    return [pair for pair, n in counts.items() for _ in range(n)]


def table_certificate(rows):
    """Close each reversed MC patch by its high-side cube-face polygons.
    Corner nodes are 0..7, crossing nodes 8..19. Every cap uses only high
    corners and actual crossings. A closed oriented chain has winding zero
    outside the convex hull of its vertices, even without an embeddedness claim.
    """
    edge_id = {frozenset(pair): i for i, pair in enumerate(EDGES)}
    face_signatures = {}
    for case, row in enumerate(rows):
        tris = triangles(row)
        chain = collections.Counter()
        def add(a, b):
            chain[a, b] += 1
            chain[b, a] -= 1
        # Reverse the low-solid boundary to get the high-solid boundary.
        for a, b, c in tris:
            for u, v in ((b, a), (c, b), (a, c)):
                add(8+u, 8+v)
        for face in FACES:
            arcs = []
            for a, b in zip(face, face[1:] + face[:1]):
                high_a, high_b = not case & (1 << a), not case & (1 << b)
                e = 8 + edge_id[frozenset((a, b))]
                if high_a and high_b:
                    arcs.append((a, b))
                elif high_a:
                    arcs.append((a, e))
                elif high_b:
                    arcs.append((e, b))
            # On the outward cap the MC segments retain their original direction.
            arcs += [(8+a, 8+b) for a, b in face_segments(tris, face)]
            outgoing, incoming = collections.Counter(a for a, b in arcs), collections.Counter(b for a, b in arcs)
            if outgoing != incoming or any(n != 1 for n in outgoing.values()):
                raise ValueError(f'case {case}: high cap is not disjoint oriented cycles')
            for a, b in arcs:
                add(a, b)
            # Same face signs must produce the same undirected crossing pairs,
            # independent of the other four cell corners.
            bits = tuple(bool(case & (1 << v)) for v in face)
            sig = tuple(sorted(tuple(sorted(p)) for p in face_segments(tris, face)))
            key = (face, bits)
            if key in face_signatures and face_signatures[key] != sig:
                raise ValueError('face ambiguity depends on non-face corners')
            face_signatures[key] = sig
        if any(chain.values()):
            raise ValueError(f'case {case}: high boundary chain is not closed')
    # Compare adjacent cells in the same physical face coordinates (not face orientation).
    for low, high in (((0,1,2,3),(4,5,6,7)), ((0,1,5,4),(3,2,6,7)), ((0,3,7,4),(1,2,6,5))):
        def canonical(face, bits):
            case = sum(1 << v for i, v in enumerate(face) if bits >> i & 1)
            def label(e):
                return tuple(sorted(face.index(v) for v in EDGES[e]))
            return sorted(tuple(sorted((label(a), label(b)))) for a, b in face_segments(triangles(rows[case]), face))
        for bits in range(16):
            if canonical(low, bits) != canonical(high, bits):
                raise ValueError('neighboring face mismatch')
    return dict(cases=256, closed_high_boundary_chains=True, matching_neighbor_faces=True,
                interpretation='oriented winding chains; not an embeddedness certificate')


# Per-query timeout; Z3_PILOT_TIMEOUT_MS overrides it (as for the other pilots; CI sets 120 s). UNKNOWN fails.
DEFAULT_TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '20000'))


def check(name, assumptions, bad, timeout_ms=DEFAULT_TIMEOUT_MS):
    solver = z.Solver()
    solver.set(timeout=timeout_ms)
    solver.add(*assumptions)
    start = time.monotonic()
    feasible = solver.check()
    solver.add(bad)
    status = solver.check()
    return dict(name=name, assumptions_result=str(feasible), result=str(status),
                seconds=time.monotonic()-start,
                passed=feasible == z.sat and status == z.unsat)


def lift_obligation(case, row):
    """Verify this actual patch's high boundary lift using the paper's bridge.
    Every crossing has lift sigma (edge_identity, checked separately). High
    corners have lift >= sigma. A cap or patch triangle has only these nodes.
    The convex-hull support of the closed high chain is therefore in P.
    """
    sigma = z.RealVal(1)  # Lift coordinates normalized by the positive level.
    nodes = [('corner', i) for i in range(8) if not case & (1 << i)]
    nodes += [('edge', e) for e in sorted({e for e in row if e >= 0})]
    if not nodes:
        # All-low case: the high chain is empty, so there is no bad high point.
        return [sigma > 0], z.BoolVal(False)
    weights = z.Reals(' '.join('w'+str(i) for i in range(len(nodes))))
    contributions = z.Reals(' '.join('c'+str(i) for i in range(len(nodes))))
    assumptions = [sigma > 0, sum(weights) == 1]
    for w, c, (kind, _) in zip(weights, contributions, nodes):
        # c abstracts w*lift after division of lift by sigma. The separate
        # nonnegative_product query proves the high-corner lower bound;
        # edge_identity proves c=w for crossing vertices.
        assumptions += [w >= 0, c == w if kind == 'edge' else c >= w]
    return assumptions, sum(contributions) < sigma



UNIT_ROUNDOFF = Fraction(1, 2**53)
UNDERFLOW_ALLOWANCE = UNIT_ROUNDOFF**2
PARAMETER_ENVELOPE = 4*UNIT_ROUNDOFF
POSITION_ENVELOPE = Fraction(1, 2**28)


def relative_ratio_obligation(bound=None):
    """Normalize by the exact positive sample gap; no sample-size/gap floor.
    Subnormal differences of binary64 inputs are exact. Division uses a relative
    error plus an absolute underflow allowance; see the reviewed design bridge.
    """
    u = z.RealVal(str(UNIT_ROUNDOFF))
    uf = z.RealVal(str(UNDERFLOW_ALLOWANCE))
    exact, dn, dd, dq, eta, t = z.Reals('ratio_exact dn dd dq eta ratio_rounded')
    assumptions = [exact >= 0, exact <= 1, dn >= -u, dn <= u,
                   dd >= -u, dd <= u, dq >= -u, dq <= u, eta >= -uf, eta <= uf,
                   (t-eta)*(1+dd) == exact*(1+dn)*(1+dq)]
    error = z.RealVal(str(PARAMETER_ENVELOPE if bound is None else bound))
    bad = z.Or(t-exact > error, exact-t > error)
    variables = dict(exact=exact, dn=dn, dd=dd, dq=dq, eta=eta, t=t)
    return assumptions, bad, variables


def fp_obligations():
    """Relative field arithmetic + retained normalized-coordinate envelope.
    The IEEE rounding models themselves remain explicit mathematical bridges.
    """
    assumptions, bad, _ = relative_ratio_obligation()
    yield 'fp_ratio_envelope', assumptions, bad
    # Keep the existing coordinate allowances and E; only the parameter bound changes.
    u = z.RealVal(str(2*UNIT_ROUNDOFF))
    error = z.RealVal(str(PARAMETER_ENVELOPE))
    position = z.RealVal(str(POSITION_ENVELOPE))
    a, b, t, exact, es, em, ea, q = z.Reals('a b t exact es em ea q')
    assumptions = [a >= -1, a <= 1, b >= -1, b <= 1, exact >= 0, exact <= 1,
                   t >= 0, t <= 1, t-exact <= error, exact-t <= error,
                   es >= -u, es <= u, em >= -2*u, em <= 2*u, ea >= -2*u, ea <= 2*u,
                   q == a+t*(b-a+es)+em+ea]
    target = a+exact*(b-a)
    yield 'fp_position_envelope', assumptions, z.Or(q-target > position, target-q > position)


def relative_ratio_mutant(timeout_ms):
    """2u is too small for the independent-error envelope, not a source bug."""
    assumptions, bad, variables = relative_ratio_obligation(2*UNIT_ROUNDOFF)
    solver = z.Solver()
    solver.set(timeout=timeout_ms)
    solver.add(*assumptions, bad)
    status = solver.check()
    witness, replayed = None, False
    if status == z.sat:
        v = {name: solver.model().eval(var).as_fraction() for name, var in variables.items()}
        u = UNIT_ROUNDOFF
        replayed = (0 <= v['exact'] <= 1 and all(abs(v[k]) <= u for k in ('dn', 'dd', 'dq'))
                    and abs(v['eta']) <= UNDERFLOW_ALLOWANCE
                    and (v['t']-v['eta'])*(1+v['dd']) == v['exact']*(1+v['dn'])*(1+v['dq'])
                    and abs(v['t']-v['exact']) > 2*u)
        witness = {name: str(value) for name, value in v.items()}
    return dict(name='ratio_bound_two_u', result=str(status), replayed=replayed, witness=witness,
                scope='independent-error envelope; not necessarily attainable binary64 inputs')


def replay_binary64(name, fa, fb, level, a=0.0, b=1.0):
    """Independent Fraction replay of the source's separately rounded operations.
    This checks actual binary64 fixtures against the envelopes, not a whole mesh.
    """
    if not all(math.isfinite(x) for x in (fa, fb, level, a, b)) or not fa < level <= fb:
        raise ValueError('finite crossing operands required')
    n, d = Fraction(level)-Fraction(fa), Fraction(fb)-Fraction(fa)
    rn, rd = level-fa, fb-fa
    if not math.isfinite(rn) or not math.isfinite(rd) or not rd > 0:
        raise ValueError('source rejects an overflowing difference')
    quotient = Fraction(rn)/Fraction(rd)
    raw = rn/rd
    if not math.isfinite(raw):
        raise ValueError('source rejects a nonfinite quotient')
    dn, dd = Fraction(rn)/n-1, Fraction(rd)/d-1
    if quotient == 0 or abs(quotient) < Fraction.from_float(float.fromhex('0x1p-1022')):
        dq, eta = Fraction(0), Fraction(raw)-quotient
    else:
        dq, eta = Fraction(raw)/quotient-1, Fraction(0)
    exact = n/d
    clamped = max(0.0, min(1.0, raw))
    # Separate multiply/add, as in find_iso (no fused contraction).
    scaled = clamped*(b-a)
    q = max(min(a, b), min(max(a, b), a+scaled))
    target = Fraction(a)+exact*(Fraction(b)-Fraction(a))
    parameter_error, position_error = abs(Fraction(raw)-exact), abs(Fraction(q)-target)
    model = (all(abs(v) <= UNIT_ROUNDOFF for v in (dn, dd, dq)) and abs(eta) <= UNDERFLOW_ALLOWANCE
             and (Fraction(raw)-eta)*(1+dd) == exact*(1+dn)*(1+dq))
    passed = (model and abs(a) <= 1 and abs(b) <= 1 and parameter_error <= PARAMETER_ENVELOPE
              and abs(Fraction(clamped)-exact) <= PARAMETER_ENVELOPE and position_error <= POSITION_ENVELOPE)
    return dict(name=name, operands=[v.hex() for v in (fa, fb, level)], rounded_parameter=raw.hex(),
                sample_gap=str(d), exact_parameter=str(exact), parameter_error=str(parameter_error),
                position_error=str(position_error), errors={k:str(v) for k,v in dict(dn=dn, dd=dd, dq=dq, eta=eta).items()},
                pure_relative_division_holds=abs(Fraction(raw)-quotient) <= UNIT_ROUNDOFF*abs(quotient),
                endpoint_collapsed=q == a or q == b, replayed=bool(passed))


def binary64_replays():
    tiny = float.fromhex('0x0.0000000000001p-1022')
    big = 2.0**100
    fixtures = [
        ('large_samples', big, big+2.0**50, big+2.0**49),
        ('tiny_sample_gap', 0.0, 2.0**-1020, 2.0**-1021),
        ('subnormal_differences', tiny, 5*tiny, 3*tiny),
        ('subnormal_quotient', 0.0, .75, tiny),
        ('opposite_sign_cancellation', -2.0**900, 2.0**900, 2.0**-100),
        ('numerator_tie', -1.0, 1.0, 2.0**-53, -1.0, 1.0),
        ('denominator_tie', -1.0, math.nextafter(1.0, math.inf), 0.0),
    ]
    fixtures += [('field_scale_'+str(e), math.ldexp(2.0,e), math.ldexp(6.0,e), math.ldexp(3.0,e))
                 for e in (-1070, -1000, -500, 0, 500, 1000)]
    return [replay_binary64(*fixture) for fixture in fixtures]


def run(timeout_ms=DEFAULT_TIMEOUT_MS):
    rows, sources = source_contracts()
    topology = table_certificate(rows)
    obligations = []
    f0, f1, sigma, t = z.Reals('f0 f1 sigma t')
    obligations.append(check('edge_identity', [f0 < sigma, sigma <= f1, t*(f1-f0) == sigma-f0],
                             (1-t)*f0+t*f1 != sigma, timeout_ms))
    w, f, level = z.Reals('weight lift level')
    obligations.append(check('nonnegative_product', [w >= 0, f >= level], w*f < w*level, timeout_ms))
    for case, row in enumerate(rows):
        assumptions, bad = lift_obligation(case, row)
        obligations.append(check(f'case_{case:03d}_lift', assumptions, bad, timeout_ms))
    for name, assumptions, bad in fp_obligations():
        obligations.append(check(name, assumptions, bad, timeout_ms))
    # The paper's sharpness example: a genuinely positive-radius sphere is
    # invisible at every corner of this cell just below half its diagonal.
    radius, sigma, distance = z.Reals('radius sigma distance')
    solver = z.Solver()
    solver.set(timeout=timeout_ms)
    solver.add(distance > 0, distance*distance == z.RealVal(3)/4,
               sigma == distance-z.RealVal(1)/1000, radius > 0,
               radius < distance-sigma, distance-radius > sigma)
    sharpness = str(solver.check())
    # Exact independent replay of the constructive sphere witness, h=1.
    r = Fraction(1, 2000)
    gap = Fraction(1, 1000)
    sharpness_replayed = 0 < r < gap and Fraction(3, 4) > gap*gap
    # Controlled placement fault: replacing interpolation with an arbitrary
    # in-edge point must be detected by the lift identity.
    solver2 = z.SolverFor('QF_NRA')
    solver2.set(timeout=timeout_ms)
    solver2.add(f0 < sigma, sigma < f1, t > 0, t < 1, (1-t)*f0+t*f1 < sigma)
    mutant = str(solver2.check())
    witness = None
    replayed = False
    if mutant == 'sat':
        model = solver2.model()
        values = [model.eval(v).as_fraction() for v in (f0, f1, sigma, t)]
        lo, hi, level, parameter = values
        replayed = lo < level < hi and 0 < parameter < 1 and (1-parameter)*lo+parameter*hi < level
        witness = dict(zip(('f0', 'f1', 'sigma', 't'), map(str, values)))
    fp_mutant = relative_ratio_mutant(timeout_ms)
    replays = binary64_replays()
    underflow_replayed = any(r['name'] == 'subnormal_quotient' and r['replayed']
                             and not r['pure_relative_division_holds'] for r in replays)
    return dict(sources=sources, table=topology, obligations=obligations,
                fp_profile=dict(unit_roundoff=str(UNIT_ROUNDOFF), underflow_allowance=str(UNDERFLOW_ALLOWANCE),
                                parameter_envelope=str(PARAMETER_ENVELOPE), position_envelope=str(POSITION_ENVELOPE),
                                sample_magnitude_bound=None, minimum_sample_gap=None, coordinate_magnitude_bound=1),
                fp_mutants=[fp_mutant], fp_replays=replays, underflow_control_replayed=underflow_replayed,
                sharpness_result=sharpness, sharpness_replayed=sharpness_replayed,
                sharpness_witness=dict(radius=str(r), sigma='sqrt(3)/2 - 1/1000', center=[.5,.5,.5]),
                wrong_crossing_result=mutant, wrong_crossing_witness=witness, wrong_crossing_replayed=replayed,
                obligations_passed=all(row['passed'] for row in obligations) and sharpness == 'sat' and mutant == 'sat'
                    and sharpness_replayed and replayed and fp_mutant['result'] == 'sat' and fp_mutant['replayed']
                    and all(r['replayed'] for r in replays) and underflow_replayed,
                scope='discrete lift adapter plus relative field-error envelopes; continuous enclosure is the paper theorem')


def direct_query(case, row, timeout_ms):
    """Optional unreduced local NRA query, over-approximating the actual high side
    by the convex hull of its boundary vertices. Infeasible corner/zero-point
    assumptions are reported separately, never counted as a nonvacuous proof.
    """
    f = z.Reals(' '.join('f'+str(i) for i in range(8)))
    x = z.Reals('x y z')
    sigma = z.Real('sigma')
    assumptions = [sigma > 0, sigma*sigma > z.RealVal(3)/4]
    assumptions += [v >= 0 for v in x] + [v <= 1 for v in x]
    for i, p in enumerate(CORNERS):
        assumptions += [f[i] < sigma if case & (1 << i) else f[i] >= sigma,
                        f[i]*f[i] <= sum((x[d]-p[d])**2 for d in range(3))]
    vertices = [tuple(map(z.RealVal, CORNERS[i])) for i in range(8) if not case & (1 << i)]
    for edge in sorted({e for e in row if e >= 0}):
        a, b = EDGES[edge]
        t = z.Real('edge_t'+str(edge))
        assumptions += [t >= 0, t <= 1, t*(f[b]-f[a]) == sigma-f[a]]
        vertices.append(tuple(CORNERS[a][d]+t*(CORNERS[b][d]-CORNERS[a][d]) for d in range(3)))
    if vertices:
        w = z.Reals(' '.join('direct_w'+str(i) for i in range(len(vertices))))
        high = z.And(sum(w) == 1, *[v >= 0 for v in w],
                     *[x[d] == sum(weight*p[d] for weight, p in zip(w, vertices)) for d in range(3)])
    else:
        high = z.BoolVal(False)
    return check(f'direct_case_{case:03d}', assumptions, high, timeout_ms)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=pathlib.Path)
    parser.add_argument('--timeout-ms', type=int, default=DEFAULT_TIMEOUT_MS)
    parser.add_argument('--direct', action='store_true', help='also run unreduced zero-point NRA queries; UNKNOWN fails')
    args = parser.parse_args()
    report = run(args.timeout_ms)
    if args.direct:
        rows, _ = source_contracts()
        report['direct_obligations'] = [direct_query(i, row, args.timeout_ms) for i, row in enumerate(rows)]
        report['direct_passed'] = all(r['passed'] for r in report['direct_obligations'])
        # The all-high case has impossible zero-point assumptions above the bound.
        # Report that exclusion explicitly rather than calling it a SAT-guarded proof.
        report['direct_excluded_cases'] = [r['name'] for r in report['direct_obligations'] if r['assumptions_result'] == 'unsat']
        report['direct_passed'] = all(r['passed'] or r['assumptions_result'] == 'unsat' for r in report['direct_obligations'])
        report['obligations_passed'] &= report['direct_passed']
    data = json.dumps(report, indent=2)+'\n'
    if args.out:
        args.out.write_text(data)
    print(json.dumps({k:v for k,v in report.items() if k not in ('obligations', 'direct_obligations', 'sources', 'fp_replays')}, indent=2))
    return 0 if report['obligations_passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
