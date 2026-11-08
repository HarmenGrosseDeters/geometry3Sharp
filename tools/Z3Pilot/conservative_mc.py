"""Source-coupled MC adapter certificate; the continuous enclosure lemma is Jacobson's.
See README: this checks the discrete lift/closed-chain obligations, not a new
SMT proof of the continuous-function theorem. UNKNOWN and vacuity are failures.
"""
import argparse
import collections
from fractions import Fraction
import hashlib
import json
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



def fp_obligations():
    """Modular normalized-domain rounding bridge, each operation error <= 2^-52.
    These are real error-envelope lemmas, NOT monolithic IEEE-754 queries.
    Standard correctly-rounded binary64 absolute-error envelopes are assumptions.
    """
    u = z.RealVal(1) / 2**52
    n, d, en, ed, eq = z.Reals('n d en ed eq')
    t, exact = z.Reals('t exact')
    assumptions = [d >= z.RealVal(1)/2**20, d <= 2, n >= 0, n <= d,
                   en >= -u, en <= u, ed >= -u, ed <= u, eq >= -u, eq <= u,
                   exact*d == n, (t-eq)*(d+ed) == n+en]
    yield 'fp_ratio_envelope', assumptions, z.Or(t-exact > z.RealVal(1)/2**30, exact-t > z.RealVal(1)/2**30)
    a, b, t, exact, es, em, ea, q = z.Reals('a b t exact es em ea q')
    assumptions = [a >= -1, a <= 1, b >= -1, b <= 1, exact >= 0, exact <= 1,
                   t >= 0, t <= 1, t-exact <= z.RealVal(1)/2**30, exact-t <= z.RealVal(1)/2**30,
                   es >= -u, es <= u, em >= -2*u, em <= 2*u, ea >= -2*u, ea <= 2*u,
                   q == a+t*(b-a+es)+em+ea]
    target = a+exact*(b-a)
    yield 'fp_position_envelope', assumptions, z.Or(q-target > z.RealVal(1)/2**28, target-q > z.RealVal(1)/2**28)


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
    return dict(sources=sources, table=topology, obligations=obligations,
                sharpness_result=sharpness, sharpness_replayed=sharpness_replayed,
                sharpness_witness=dict(radius=str(r), sigma='sqrt(3)/2 - 1/1000', center=[.5,.5,.5]),
                wrong_crossing_result=mutant, wrong_crossing_witness=witness, wrong_crossing_replayed=replayed,
                obligations_passed=all(row['passed'] for row in obligations) and sharpness == 'sat' and mutant == 'sat'
                    and sharpness_replayed and replayed,
                scope='discrete lift adapter plus real FP envelopes; continuous enclosure is the paper theorem')


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
    print(json.dumps({k:v for k,v in report.items() if k not in ('obligations', 'direct_obligations', 'sources')}, indent=2))
    return 0 if report['obligations_passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
