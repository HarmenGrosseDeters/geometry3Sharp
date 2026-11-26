"""Check the source face splits and canonical face keys."""
from collections import Counter, defaultdict
from itertools import combinations, product
import re

from check_pilot_obligations import check, main, report, source

SOURCE = 'mesh_generators/TwoLevelMarchingTetrahedra.cs'
CONTRACTS = [
    ('static readonly int[][] EvenTets =', 'public void Generate()',
     '15efadd3f5a6cf5ddd666a590a9c4f018e21986171c25f45628fbc5bb521392c'),
    ('int id = i + n1x * (j + n1y * k);', 'MeshTet(mesh, tet);',
     '868260baafe709f2c9e08159c47dcd081fbf05777831764427af8f5faad1bac3'),
    ('int Polygon(int[] tet, int mask)', 'double Crossing(',
     'cc23ef51d3de6298a2064d4134a0c374488181e66593c7fd27d03f49becec72b'),
    ('int mask = (v[tet[0]] < 0', 'int m = Polygon(tet, mask);',
     '56a911216db5fc562f2e37ed18e181e23ceee387fffa4c9cf9a9aa9b308d54af'),
    ('int n0 = polyA[p], n1 = polyB[p]', 'int lowF = Math.Min(c, o)',
     'e7d3f14da6f608ee1921bd7c88c4cd6d89c7799a349b1f3dfa873eeb7ece4e7e'),
]
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
    terms = list(terms)
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
    active = disjunction(term for terms in ls.values() for term in terms)
    return declarations, disjunction(differences), active, lf == rf and len(lf) == 2 and set(lf.values()) == {1}


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
        code, differs, active, same_triangles = mismatch((splits[parity], splits[1 - parity]), axis)
        conforming &= same_triangles
        name = f'axis_{axis}_parity_{parity}'
        rows.append(check(name + '_reachable', 'QF_UF', code, active, 'sat', timeout_ms))
        rows.append(check(name, 'QF_UF', code, differs, 'unsat', timeout_ms))
        mutant, mutant_differs, _, _ = mismatch((splits[parity], splits[parity]), axis)
        rows.append(check(name + '_same_parity_mutant', 'QF_UF', mutant, mutant_differs, 'sat', timeout_ms))

    keys = ('a b c', 'a c b', 'b c a')
    differs = disjunction(
        f'(or (distinct (lo {values}) a) (distinct (mid {values}) b) (distinct (hi {values}) c))'
        for values in keys)
    rows.append(check('face_key_reachable', 'QF_LIA', FACE_KEY, '(= a 0)', 'sat', timeout_ms))
    rows.append(check('face_key_differs', 'QF_LIA', FACE_KEY, differs, 'unsat', timeout_ms))
    rows.append(check('face_key_skip_first_swap_mutant', 'QF_LIA', FACE_KEY,
                      '(distinct c (mid a c b))', 'sat', timeout_ms))
    result = report(rows, shared_face_triangulations_match=conforming)
    result['passed'] &= conforming
    return result


if __name__ == '__main__':
    raise SystemExit(main(run))
