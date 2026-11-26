"""Check the source face splits and the local two-level clipping rule."""
from collections import Counter, defaultdict
from itertools import combinations, product
import re

from check_pilot_obligations import check, main, report, source

SOURCE = 'mesh_generators/TwoLevelMarchingTetrahedra.cs'
CONTRACTS = [
    ('int id = i + n1x * (j + n1y * k);', 'CertificateRecord done =', '22df4cbd9c7dbd91944cea4fb1a02f660d9cb7824e787b9dc6d49023d59dc3af'),
    ('int Polygon(int[] tet, int mask)', 'double Crossing(', 'cc23ef51d3de6298a2064d4134a0c374488181e66593c7fd27d03f49becec72b'),
    ('void MeshTet(DMesh3 mesh, int[] tet)', 'int m = Polygon(tet, mask);', 'c023fe834718838f4e48da1ae40a9a4afc8a982f7c892a21b0ef262005b39c41'),
    ('int m = Polygon(tet, mask);', 'if (emitted.Count < 3)', '9086d9681c9f1bf403b93274457cc16ce9b26980e4b887a50df72a75365240a8'),
    ('bool NegativeAt(', 'Vector3d EdgePoint(', 'bf229f67beb3bcfbbf2831e1ab485bbc5fae1b09db40ed868cb290fc9affea21'),
    ('int FaceVertex(', 'int vid;', '2754c3ad0fa8517b237dd489294fbc2bdc7ad429d2fd599b2521617c6d2ab0c2'),
]
PINS = [(SOURCE, CONTRACTS, False)]
CORNERS = tuple((index & 1, (index >> 1) & 1, index >> 2) for index in range(8))


def source_splits(root):
    text = (root / SOURCE).read_text(encoding='utf-8')
    result = []
    for name in ('EvenTets', 'OddTets'):
        match = re.search(rf'\b{name}\s*=\s*\{{([^;]+)\}};', text)
        if match is None:
            raise ValueError(f'Missing {name} in {SOURCE}')
        tets = tuple(tuple(int(x) for x in re.findall(r'\d+', row))
                     for row in re.findall(r'new\[\]\s*\{([^}]+)\}', match[1]))
        if len(tets) != 5 or any(len(tet) != 4 or len(set(tet)) != 4
                                 or not set(tet) <= set(range(8)) for tet in tets):
            raise ValueError(f'Invalid {name} in {SOURCE}')
        result.append(tets)
    return result


def polygon_edges(tet, mask):
    inside, outside = ([tet[i] for i in range(4) if ((mask >> i) & 1) == bit] for bit in (1, 0))
    if not inside or not outside:
        return []
    if len(inside) == 2:
        a, b = inside
        c, d = outside
        pairs = [(a, c), (a, d), (b, d), (b, c)]
    else:
        alone = inside if len(inside) == 1 else outside
        others = outside if len(inside) == 1 else inside
        pairs = [(alone[0], point) for point in others]
    return [tuple(sorted(edge)) for edge in pairs]


def boundary_segments(tets, points, face):
    segments = defaultdict(list)
    faces = Counter()
    for indices in tets:
        tet = tuple(points[i] for i in indices)
        for tri in combinations(tet, 3):
            if set(tri) <= face:
                faces[tuple(sorted(tri))] += 1
        for mask in range(1, 15):
            polygon = polygon_edges(tet, mask)
            for a, b in zip(polygon, polygon[1:] + polygon[:1]):
                if set(a + b) <= face:
                    key = tuple(sorted((a, b)))
                    signs = [point if (mask >> index) & 1 else f'(not {point})'
                             for index, point in enumerate(tet)]
                    segments[key].append('(and ' + ' '.join(signs) + ')')
    return faces, segments


def disjunction(terms):
    return '(or ' + ' '.join(terms) + ')' if terms else 'false'


def mismatch(pair, axis):
    # Numeric vertex ids use strides for a grid of width 3 in all axes.
    shift = 3**axis
    left = [f'v{x + 3*y + 9*z}' for x, y, z in CORNERS]
    right = [f'v{x + 3*y + 9*z + shift}' for x, y, z in CORNERS]
    face = set(left) & set(right)
    declarations = '\n'.join(f'(declare-const {point} Bool)' for point in sorted(set(left + right)))
    lf, ls = boundary_segments(pair[0], left, face)
    rf, rs = boundary_segments(pair[1], right, face)
    differences = [f'(distinct {disjunction(ls[key])} {disjunction(rs[key])})'
                   for key in sorted(ls.keys() | rs.keys())]
    return declarations, disjunction(differences), lf == rf and len(lf) == 2 and set(lf.values()) == {1}


# For nc=2, each polygon edge is clipped by exactly one other level.
# The two endpoint classifications use canonical, shared grid-edge ids.
# Compare the unordered boundary contribution when the neighbor traverses
# the segment in reverse. F stands for the shared intersection vertex.
CLIP = """
(declare-const atP Bool)
(declare-const atQ Bool)
(define-fun hasP ((p Bool) (q Bool)) Bool p)
(define-fun hasQ ((p Bool) (q Bool)) Bool q)
(define-fun hasF ((p Bool) (q Bool)) Bool (distinct p q))
"""
# FaceVertex begins with the two ordered nodes of one crossing edge
# and inserts the remaining node from the other edge.
FACE_KEY = """
(declare-const a Int)
(declare-const b Int)
(declare-const c Int)
(assert (and (< a b) (< b c)))
(define-fun lo ((x Int) (y Int) (z Int)) Int
  (ite (< z y) (ite (< z x) z x) x))
(define-fun mid ((x Int) (y Int) (z Int)) Int
  (ite (< z y) (ite (< z x) x z) y))
(define-fun hi ((x Int) (y Int) (z Int)) Int (ite (< z y) y z))
"""


def run(timeout_ms=120000, root=None):
    from check_pilot_obligations import ROOT
    root = ROOT if root is None else root
    for item in CONTRACTS:
        source(SOURCE, *item, root=root)
    splits = source_splits(root)
    rows = []
    conforming = True
    for axis, parity in product(range(3), range(2)):
        code, differs, same_triangles = mismatch((splits[parity], splits[1 - parity]), axis)
        conforming &= same_triangles
        rows.append(check(f'axis_{axis}_parity_{parity}', 'QF_UF', code, differs, 'unsat', timeout_ms))
    code, differs, _ = mismatch((splits[0], splits[0]), 0)
    rows.append(check('same_parity_neighbor_mutant', 'QF_UF', code, differs, 'sat', timeout_ms))
    rows.append(check('clip_endpoint_order_changes_boundary', 'QF_UF', CLIP,
                      '(or (distinct (hasP atP atQ) (hasQ atQ atP)) '
                      '(distinct (hasQ atP atQ) (hasP atQ atP)) '
                      '(distinct (hasF atP atQ) (hasF atQ atP)))', 'unsat', timeout_ms))
    for key_inputs in ('a b c', 'a c b', 'b c a'):
        rows.append(check(f'face_key_{key_inputs.replace(" ", "")}', 'QF_LIA', FACE_KEY,
                          f'(or (distinct (lo {key_inputs}) a) (distinct (mid {key_inputs}) b) '
                          f'(distinct (hi {key_inputs}) c))', 'unsat', timeout_ms))
    result = report(rows, shared_face_triangulations_match=conforming)
    result['passed'] &= conforming
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
