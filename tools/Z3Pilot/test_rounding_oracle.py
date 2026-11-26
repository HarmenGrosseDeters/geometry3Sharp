"""Unit tests of the rounding oracle's encoding (rounding_oracle.py) on constructed cases with a known answer:

- a sliver (a, b, c) whose apex c is 1e-9 off the segment a-b, inside a square tube of four static walls around the line a-b
  (x from -2 to 3, two triangles each). The
  degenerate area needs the apex 5e-4 from the line; the drift bound is 1e-3.
  * tube half-width 1e-2: one move of c (the corners a, b fixed) repairs it: SAT, and the witness is clean exactly;
  * tube half-width 1e-4: every position of c within the bound that lifts the triangle crosses a wall: UNSAT (the walls are
    needed: without them it is SAT, so the UNSAT comes from the intersection encoding);
  * bound 1e-4 without walls: the area cannot be reached at all: UNSAT.
- the encoding of a pair: two triangles that cross are refused, two apart (and two with one shared vertex, apart) accepted.
"""
import os
import struct
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    import numpy  # noqa: F401  (the oracle's float relaxation; not in requirements.txt, so the Z3 pilot job skips this test)
    import rounding_oracle as ro  # noqa: E402
except ImportError:  # pragma: no cover
    ro = None


def h(x):
    return struct.pack('>d', float(x)).hex()


def hv(p):
    return [h(c) for c in p]


def state(points, tris, L, eps, fixed=()):
    """a State from plain data: anchors at the current points, no carried bound, no ghosts"""
    data = dict(case='constructed', max_length=h(L), area_eps=[h(eps)], max_length_c=[h(L)], size_area=[h(eps)],
                vertices=[dict(id=i, p=hv(p), fixed=i in fixed, boundary=False, bowtie=False, component=0, canonical=i,
                               room=h(L)) for i, p in enumerate(points)],
                triangles=[], ghosts=[], bad=[])
    st0 = ro.State(dict(data))
    for t, tv in enumerate(tris):
        a, b, c = (st0.V[v]['p'] for v in tv)
        n = ro.cross(ro.sub(b, a), ro.sub(c, a))
        area2 = ro.dot(n, n)
        bad = area2 < 4 * ro.F(eps) * ro.F(eps)
        data['triangles'].append(dict(id=t, v=list(tv), anchors=[hv(points[v]) for v in tv], carry=h(0), bad=bool(bad)))
        if bad:
            data['bad'].append(t)
    return ro.State(data)


def tube(width, L, walls=True):
    eps = 2.5e-4               # needs the apex 5e-4 from the line a-b (|a-b| = 1)
    pts = [(0, 0, 0), (1, 0, 0), (0.5, 1e-9, 0)]
    tris = [(0, 1, 2)]
    if walls:
        w = width
        # (each wall twice as wide as the tube, so that the walls overlap at the corners: a triangle through a corner of the
        # tube crosses a wall's interior, not only the edge two walls share, which the weak separation would admit)
        for (y0, z0), (y1, z1) in [((-2 * w, -w), (2 * w, -w)), ((w, -2 * w), (w, 2 * w)), ((2 * w, w), (-2 * w, w)), ((-w, 2 * w), (-w, -2 * w))]:
            k = len(pts)
            pts += [(-2, y0, z0), (3, y0, z0), (3, y1, z1), (-2, y1, z1)]
            tris += [(k, k + 1, k + 2), (k, k + 2, k + 3)]
    return state(pts, tris, L, eps, fixed=(0, 1))


@unittest.skipIf(ro is None, 'numpy is not installed (the research oracle needs it; the pilot gate does not)')
class TestEncoding(unittest.TestCase):
    def solve(self, st, k=1):
        pb = ro.Problem(st, st.bad[0], ring=0, allow_fixed=False)
        self.assertEqual(pb.movable, [2])
        return pb, ro.solve(pb, k, timeout_s=60)

    def test_wide_tube_one_move_repairs(self):
        st = tube(1e-2, 1e-3)
        pb, r = self.solve(st)
        self.assertEqual(r['answer'], 'sat')
        self.assertEqual(ro.full_check(pb, r['witness']), [])
        _, back = ro.to_doubles(pb, r['witness'])
        self.assertEqual(ro.full_check(pb, back), [], 'the witness rounded to doubles is clean as well')

    def test_narrow_tube_unsat_by_the_walls(self):
        st = tube(1e-4, 1e-3)
        self.assertEqual(st.bad, [0], 'the walls are not degenerate')
        pb, r = self.solve(st)
        self.assertEqual(len(pb.pairs), 8)
        self.assertEqual(r['answer'], 'unsat', r)
        self.assertTrue(any(key[0] == 'p' for key in r['active']), 'a wall constraint was needed')
        # (the same without the walls: SAT, so the walls make the difference)
        _, r2 = self.solve(tube(1e-4, 1e-3, walls=False))
        self.assertEqual(r2['answer'], 'sat')

    def test_bound_too_small_unsat(self):
        _, r = self.solve(tube(1e-2, 1e-4, walls=False))
        self.assertEqual(r['answer'], 'unsat', r)

    def test_pair_encoding(self):
        F = ro.F
        P = lambda *xs: tuple(F(x) for x in xs)
        crossing = ([P(0, 0, 0), P(2, 0, 0), P(0, 2, 0)], [P(0.5, 0.5, -1), P(0.5, 0.5, 1), P(3, 3, 0)])
        apart = ([P(0, 0, 0), P(2, 0, 0), P(0, 2, 0)], [P(0.5, 0.5, 1), P(1.5, 0.5, 1), P(0.5, 1.5, 2)])
        touching = ([P(0, 0, 0), P(2, 0, 0), P(0, 2, 0)], [P(0.5, 0.5, 0), P(1.5, 0.5, 1), P(0.5, 1.5, 2)])
        k = lambda p: tuple(ro.Poly.k(c) for c in p)
        sep = lambda pair: ro.holds(ro.sep_disjoint(ro.PB, [k(p) for p in pair[0]], [k(p) for p in pair[1]]), {})
        self.assertFalse(sep(crossing))
        self.assertTrue(sep(apart))
        # (touching: weakly separated, admitted by the relaxation)
        self.assertTrue(sep(touching))
        s = P(0, 0, 0)
        # one shared vertex s: (s, a, b) in z = 0, (s, c, d) folded into it (crossing its interior) and turned away
        self.assertFalse(ro.holds(ro.sep_shared_vertex(ro.PB, k(s), k(P(1, 0, 0)), k(P(0, 1, 0)), k(P(1, 1, 1)), k(P(1, 1, -1))), {}))
        self.assertTrue(ro.holds(ro.sep_shared_vertex(ro.PB, k(s), k(P(1, 0, 0)), k(P(0, 1, 0)), k(P(-1, -1, 1)), k(P(-1, 0, 2))), {}))


if __name__ == '__main__':
    unittest.main()
