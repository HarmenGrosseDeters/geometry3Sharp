"""Z3 as an offline oracle for the degenerate triangles that MeshRoundingRepair leaves (#1465): can a left-over degenerate
triangle be removed by moving up to k vertices of its neighbourhood within their drift bounds, or is that provably
impossible? Research spike, docs/research/ROUNDING_Z3_ORACLE_2026-11.md. No library code; the state comes from
`BooleanInputOrder oracle dump` (every coordinate, anchor and bound as the bits of its double).

The question, per degenerate triangle t of a state, for a vertex set M (the movable vertices within ring r of t's corners:
no boundary vertex, no bowtie, the fixed ones only with allow_fixed) and a number k: is there a position for each vertex of M,
at most k of them moved, such that

  (D) every triangle and every ghost at a moved vertex keeps its drift bound: carry + |p - anchor| <= L for each moved corner
      (the reference rule of the repair, triangle_bound / ghost_bound; L = MaxLength of the component);
  (A) t and every triangle of the moved stars that is not degenerate now are not degenerate (the area test of is_bad);
  (I) no pair of triangles with a moved corner, both not degenerate, intersects beyond what they share (the repair's
      intersects, ExactTriangleIntersection.IntersectsBeyondShared), with any triangle in reach?

The model is a relaxation of that question (every configuration the library would accept satisfies it), so UNSAT is a
proof that no such configuration exists, SAT only a candidate that is checked with the library (verify mode of the dump
tool). The relaxations, each a necessary condition of the library's test:

  * positions are real, not doubles (the doubles are a subset);
  * (D) carry + |p - a| <= L in doubles implies |p - a|^2 <= (L - carry + 2^-40 L)^2 exactly (the rounding of the distance and
    of the sum is far below 2^-40 L);
  * (A) a triangle is not degenerate in doubles (0.5 |fl(cross)| >= eps) only if its exact cross product has
    |c|^2 >= tau^2, tau = 2 eps (1 - 2^-40) - 2^-40 Lam^2, Lam a bound on its longest edge (current longest + 4 L): the
    rounding error of the cross product is below 11 u |e1| |e2|, of the length 3 u, both far below the slack;
  * (I) for triangles without a shared vertex: disjoint triangles (compact convex) are separated by a plane whose normal is
    nonzero and a face normal or a cross product of an edge of each (the separating axis theorem: a facet of T - U that
    excludes 0, or the normal of T when T - U is flat); the model asks for weak separation along one of those 11 axes,
    nonzero. With one shared vertex s: the triangles meet only at s iff the cones at s meet only at s; two pointed
    polyhedral cones that meet only at the apex are weakly separated by a plane through it whose normal is +-g_i x g_j,
    nonzero, for two of the four edge directions g at s (an extreme ray of the cone of separating normals, or the common
    plane's normal); weak separation also admits touching pairs, which the library counts: a relaxation;
  * pairs that share an edge are left out (they meet beyond the edge only when coplanar and folded flat); pairs with a
    triangle that may be degenerate (one that is degenerate now and is not t, or a static degenerate one) are left out
    (the library does not test them); pairs of two static triangles are left out (the state has no intersecting pair);
  * the orientation rule of improves and SharedPositionsTouch are not modelled (acceptance rules, not cleanliness).

The decision procedure is a spatial branch and bound over boxes of the moves (in units S, a power of two near L), not a
nonlinear z3 query: z3's nonlinear arithmetic (nlsat) did not decide even the drift and the area of one triangle with the
state's long dyadic coordinates in 30 s (the record in the note). Per box every constraint is relaxed to linear form:
p(x) >= 0 implies p(c) + grad p(c) (x - c) + R >= 0 on the box, R the sum of |coefficient| times the half widths over the
monomials of degree 2 and more of p(c + h); the drift balls by their tangent planes (convex). The linear relaxation, with the
booleans of "at most k vertices move", is decided exactly by z3 (linear real arithmetic): unsat -> the box holds no
configuration. The relaxation is computed with floats and a rigorous padding (PAD times the absolute sums), on small boxes
exactly (Dyadic; no padding). A feasible box gives a candidate (the relaxation's model, or a model of the inner relaxation,
which implies the active constraints) that is checked exactly against every constraint: all hold -> SAT; a violated one is
added to the active set (lazily, counterexample-guided); else the box is split. UNSAT means every box was excluded by the
relaxation of a subset of the constraints, so no configuration satisfies the model, and hence none the library accepts.
SAT candidates also come from the library's own grid (26 directions, room / 2^j), screened in floats; every SAT is checked
exactly, after the rounding to doubles again, and then by the library (`oracle verify`).

  python tools/Z3Pilot/rounding_oracle.py <dump.json> [--ring 1] [--k 2,3,4] [--allow-fixed] [--timeout 300] [--out report.json]
"""
import argparse
import itertools
import json
import struct
import sys
import math
import time
from fractions import Fraction

import numpy as np


class Dyadic:
    """an exact dyadic rational m 2^e (every coordinate, anchor and bound of the state is one; products, sums and halves stay
    dyadic): exact like Fraction, without its gcd"""
    __slots__ = ('m', 'e')

    def __init__(self, x=0, den=None):
        if den is not None:
            assert den > 0 and den & (den - 1) == 0, 'not dyadic'
            m, e = int(x), -(den.bit_length() - 1)
        elif isinstance(x, Dyadic):
            m, e = x.m, x.e
        elif isinstance(x, int):
            m, e = x, 0
        elif isinstance(x, float):
            n, d = x.as_integer_ratio()
            m, e = n, -(d.bit_length() - 1)
        else:
            raise TypeError(type(x))
        if m == 0:
            e = 0
        else:
            z = (m & -m).bit_length() - 1
            if z:
                m >>= z
                e += z
        self.m, self.e = m, e

    @staticmethod
    def make(m, e):
        d = Dyadic.__new__(Dyadic)
        if m == 0:
            d.m, d.e = 0, 0
            return d
        z = (m & -m).bit_length() - 1
        d.m, d.e = m >> z, e + z
        return d

    @staticmethod
    def lift(o):
        return o if isinstance(o, Dyadic) else Dyadic(o)

    def __add__(self, o):
        if not isinstance(o, Dyadic):
            if isinstance(o, int):
                o = Dyadic(o)
            else:
                return NotImplemented
        if self.m == 0:
            return o
        if o.m == 0:
            return self
        if self.e <= o.e:
            return Dyadic.make(self.m + (o.m << (o.e - self.e)), self.e)
        return Dyadic.make((self.m << (self.e - o.e)) + o.m, o.e)

    __radd__ = __add__

    def __neg__(self):
        return Dyadic.make(-self.m, self.e)

    def __sub__(self, o):
        if isinstance(o, int):
            o = Dyadic(o)
        if not isinstance(o, Dyadic):
            return NotImplemented
        return self + (-o)

    def __rsub__(self, o):
        return Dyadic.lift(o) - self

    def __mul__(self, o):
        if not isinstance(o, Dyadic):
            if isinstance(o, int):
                o = Dyadic(o)
            else:
                return NotImplemented
        return Dyadic.make(self.m * o.m, self.e + o.e)

    __rmul__ = __mul__

    def __truediv__(self, o):
        o = Dyadic.lift(o)
        assert o.m in (1, -1), 'division by a power of two only'
        return Dyadic.make(self.m * o.m, self.e - o.e)

    def __pow__(self, k):
        if k >= 0:
            return Dyadic.make(self.m ** k, self.e * k)
        assert self.m in (1, -1)
        return Dyadic.make(self.m ** (-k), self.e * k)

    def cmp(self, o):
        o = Dyadic.lift(o)
        d = self - o
        return (d.m > 0) - (d.m < 0)

    def __lt__(self, o): return self.cmp(o) < 0
    def __le__(self, o): return self.cmp(o) <= 0
    def __gt__(self, o): return self.cmp(o) > 0
    def __ge__(self, o): return self.cmp(o) >= 0

    def __eq__(self, o):
        if isinstance(o, (int, Dyadic)):
            o = Dyadic.lift(o)
            return self.m == o.m and self.e == o.e
        return NotImplemented

    def __hash__(self):
        return hash((self.m, self.e))

    def __bool__(self):
        return self.m != 0

    def __abs__(self):
        return Dyadic.make(abs(self.m), self.e)

    def __float__(self):
        return float(Fraction(self.numerator, self.denominator))

    def __round__(self):
        return int(round(Fraction(self.numerator, self.denominator)))

    @property
    def numerator(self):
        return self.m << self.e if self.e > 0 else self.m

    @property
    def denominator(self):
        return 1 << -self.e if self.e < 0 else 1

    def __repr__(self):
        return f'{float(self):.17g}'


F = Dyadic


def dyadic_near(num, den, bits=96):
    """a dyadic close to num / den (z3 model values need not be dyadic)"""
    if num == 0:
        return F(0)
    q = Fraction(num, den)
    e = abs(num).bit_length() - den.bit_length() - bits
    return Dyadic.make(round(q / Fraction(2) ** e) if e >= 0 else round(q * (1 << -e)), e)

import z3

SLACK = F(1, 2 ** 40)


# ---- the state --------------------------------------------------------------------------------------------------------

def dbl(h):
    return struct.unpack('>d', bytes.fromhex(h))[0]


def fr(h):
    return F(dbl(h))


def vec(hs):
    return tuple(fr(h) for h in hs)


class State:
    def __init__(self, data):
        self.case = data.get('case', '')
        self.L = fr(data['max_length'])
        self.area_eps = [fr(h) for h in data['area_eps']]
        self.max_length_c = [fr(h) for h in data['max_length_c']]
        self.bad = list(data['bad'])
        self.V = {}
        for v in data['vertices']:
            self.V[v['id']] = dict(p=vec(v['p']), fixed=v['fixed'], boundary=v['boundary'], bowtie=v['bowtie'],
                                   comp=v['component'], canonical=v['canonical'], room=fr(v['room']))
        self.T = {}
        for t in data['triangles']:
            self.T[t['id']] = dict(v=tuple(t['v']), anchors=[vec(a) for a in t['anchors']], carry=fr(t['carry']), bad=t['bad'])
        self.ghosts = [dict(at=tuple(g['at']), anchors=[vec(a) for a in g['anchors']], carry=fr(g['carry']), lost=g['lost'])
                       for g in data['ghosts']]
        self.star = {v: [] for v in self.V}
        self.nbr = {v: set() for v in self.V}
        for tid, t in self.T.items():
            a, b, c = t['v']
            for x in t['v']:
                self.star[x].append(tid)
            self.nbr[a] |= {b, c}
            self.nbr[b] |= {a, c}
            self.nbr[c] |= {a, b}

    @staticmethod
    def load(path):
        with open(path, encoding='utf-8') as f:
            return State(json.load(f))


# ---- polynomials in the moves (exact) ---------------------------------------------------------------------------------

class Poly:
    """a polynomial with fraction coefficients in the move variables (index -> variable); monomial: sorted tuple of indices"""
    __slots__ = ('c',)

    def __init__(self, c=None):
        self.c = c or {}

    @staticmethod
    def k(x):
        return Poly({(): F(x)} if x != 0 else {})

    @staticmethod
    def var(i):
        return Poly({(i,): F(1)})

    @staticmethod
    def lift(x):
        return x if isinstance(x, Poly) else Poly.k(x)

    def __add__(self, o):
        o = Poly.lift(o)
        c = dict(self.c)
        for m, v in o.c.items():
            w = c.get(m, 0) + v
            if w:
                c[m] = w
            else:
                c.pop(m, None)
        return Poly(c)

    __radd__ = __add__

    def __neg__(self):
        return Poly({m: -v for m, v in self.c.items()})

    def __sub__(self, o):
        return self + (-Poly.lift(o))

    def __rsub__(self, o):
        return Poly.lift(o) - self

    def __mul__(self, o):
        o = Poly.lift(o)
        c = {}
        for m1, v1 in self.c.items():
            for m2, v2 in o.c.items():
                m = tuple(sorted(m1 + m2))
                w = c.get(m, 0) + v1 * v2
                if w:
                    c[m] = w
                else:
                    c.pop(m, None)
        return Poly(c)

    __rmul__ = __mul__

    def at(self, x):
        """exact value at the point x (index -> fraction)"""
        total = F(0)
        for m, v in self.c.items():
            p = v
            for i in m:
                p *= x[i]
            total += p
        return total

    def __ge__(self, o):
        return Atom(self - o, True)

    def __le__(self, o):
        return Atom(Poly.lift(o) - self, True)

    def nonzero(self):
        return Atom(self, False)


class Atom:
    """p >= 0 (ge) or p != 0"""
    __slots__ = ('p', 'ge')

    def __init__(self, p, ge):
        self.p, self.ge = p, ge


def And(*xs):
    return ('and', xs)


def Or(*xs):
    return ('or', xs)


def holds(f, x):
    """exact truth value of a formula at the point x"""
    if isinstance(f, Atom):
        v = f.p.at(x)
        return v >= 0 if f.ge else v != 0
    op, xs = f
    return all(holds(g, x) for g in xs) if op == 'and' else any(holds(g, x) for g in xs)


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


class PB:
    And = staticmethod(And)
    Or = staticmethod(Or)

    @staticmethod
    def const(x):
        return Poly.k(x)


def sep_disjoint(B, T, U):
    """weak separation of triangles T, U (no shared vertex) along one of the 11 axes of the separating axis theorem"""
    eT = [sub(T[1], T[0]), sub(T[2], T[1]), sub(T[0], T[2])]
    eU = [sub(U[1], U[0]), sub(U[2], U[1]), sub(U[0], U[2])]
    alts = []
    nT = cross(eT[0], sub(T[2], T[0]))
    s = [dot(nT, sub(q, T[0])) for q in U]
    alts.append(B.And(*[x >= 0 for x in s]))
    alts.append(B.And(*[x <= 0 for x in s]))
    nU = cross(eU[0], sub(U[2], U[0]))
    s = [dot(nU, sub(p, U[0])) for p in T]
    alts.append(B.And(*[x >= 0 for x in s]))
    alts.append(B.And(*[x <= 0 for x in s]))
    for e in eT:
        for f in eU:
            h = cross(e, f)
            # (T's projections are 2 distinct values at most along h: h is orthogonal to e; the same for U and f)
            pt = [dot(h, p) for p in T]
            pu = [dot(h, q) for q in U]
            # (a zero axis, two parallel edges, would separate anything: the axis must not be zero)
            nz = B.Or(h[0].nonzero(), h[1].nonzero(), h[2].nonzero())
            alts.append(B.And(*([a <= b for a in pt for b in pu] + [nz])))
            alts.append(B.And(*([a >= b for a in pt for b in pu] + [nz])))
    return B.Or(*alts)


def sep_shared_vertex(B, s, a, b, c, d):
    """triangles (s, a, b) and (s, c, d) meet only at s: a weakly separating plane through s with normal +-g_i x g_j, nonzero"""
    g = [sub(a, s), sub(b, s), sub(c, s), sub(d, s)]
    alts = []
    for i, j in itertools.combinations(range(4), 2):
        h = cross(g[i], g[j])
        pr = [dot(h, x) for x in g]
        nz = B.Or(h[0].nonzero(), h[1].nonzero(), h[2].nonzero())
        alts.append(B.And(pr[0] >= 0, pr[1] >= 0, pr[2] <= 0, pr[3] <= 0, nz))
        alts.append(B.And(pr[0] <= 0, pr[1] <= 0, pr[2] >= 0, pr[3] >= 0, nz))
    return B.Or(*alts)


# ---- the problem of one degenerate triangle ---------------------------------------------------------------------------

def box(pts, grow=0):
    lo = tuple(min(p[k] for p in pts) - grow for k in range(3))
    hi = tuple(max(p[k] for p in pts) + grow for k in range(3))
    return lo, hi


def overlap(b1, b2):
    return all(b1[0][k] <= b2[1][k] and b2[0][k] <= b1[1][k] for k in range(3))


class Problem:
    """the constraints of the question for triangle `target`, in scaled coordinates X = (p - O) / L (exact)"""

    def __init__(self, st, target, ring=1, allow_fixed=False, movable=None, also=()):
        """also: further degenerate triangles that must become good as well (a cluster of them, one question)"""
        self.st = st
        self.target = target
        self.targets = [target] + [t for t in also if t != target]
        tri = st.T[target]
        comp = st.V[tri['v'][0]]['comp']
        self.L = L = st.max_length_c[comp]
        # the unit of the scaled coordinates: a power of two near L (dyadic values stay dyadic, the numerals short)
        e = L.numerator.bit_length() - L.denominator.bit_length()
        self.S = S = F(2) ** e
        self.O = st.V[tri['v'][0]]['p']
        region = {v for t in self.targets for v in st.T[t]['v']}
        frontier = set(region)
        for _ in range(ring):
            frontier = set().union(*[st.nbr[v] for v in frontier]) - region
            region |= frontier
        self.region = sorted(region)
        if movable is None:
            movable = [v for v in self.region
                       if not st.V[v]['boundary'] and not st.V[v]['bowtie'] and st.V[v]['comp'] == comp
                       and (allow_fixed or not st.V[v]['fixed'])]
        self.movable = sorted(movable)
        mset = set(self.movable)
        self.changed = sorted({t for v in self.movable for t in st.star[v]})
        cset = set(self.changed)
        # the triangles that are not degenerate in every configuration of the model (an area constraint, or static and good)
        self.area_tris = [t for t in self.changed if t in self.targets or not st.T[t]['bad']]
        sure = set(self.area_tris) | {t for t in st.T if t not in cset and not st.T[t]['bad']}
        # drift balls per movable vertex (scaled): the triangles of its star and the ghosts on it, at the corners that are it
        self.balls = {}
        for v in self.movable:
            bl = []
            for t in st.star[v]:
                T = st.T[t]
                for i in range(3):
                    if T['v'][i] == v:
                        bl.append((self.scale(T['anchors'][i]), (L - T['carry'] + SLACK * L) / S, ('t', t)))
            for gi, g in enumerate(st.ghosts):
                if g['lost']:
                    continue
                for i in range(3):
                    if g['at'][i] == v:
                        bl.append((self.scale(g['anchors'][i]), (L - g['carry'] + SLACK * L) / S, ('g', gi)))
            self.balls[v] = bl
        # area thresholds (scaled by L^2)
        self.tau = {}
        eps = st.area_eps[comp]
        for t in self.area_tris:
            P = [st.V[x]['p'] for x in st.T[t]['v']]
            longest = max(max(abs(a - b) for a, b in zip(P[i], P[(i + 1) % 3])) for i in range(3)) * 2  # >= the length
            lam = longest + 4 * L
            tau = 2 * eps * (1 - SLACK) - SLACK * lam * lam
            self.tau[t] = tau / (S * S)
        # pairs: a changed triangle and any triangle whose box (each grown by 2 L + slack for a changed one) overlaps
        grow = 2 * L * (1 + SLACK)
        boxes = {}
        for t, T in st.T.items():
            P = [st.V[x]['p'] for x in T['v']]
            boxes[t] = box(P, grow if t in cset else 0)
        self.pairs = []
        self.dropped = {'edge': 0, 'degenerate': 0, 'same position': 0}
        for a in self.changed:
            for b in st.T:
                if b == a or (b in cset and b < a) or not overlap(boxes[a], boxes[b]):
                    continue
                va, vb = st.T[a]['v'], st.T[b]['v']
                shared = set(va) & set(vb)
                if len(shared) >= 2:
                    self.dropped['edge'] += 1
                    continue
                if a not in sure or b not in sure:
                    self.dropped['degenerate'] += 1
                    continue
                # (distinct vertices at one position count as shared in the repair (SharedPositionsTouch): left out)
                ca = {st.V[x]['canonical'] for x in va}
                cb = {st.V[x]['canonical'] for x in vb}
                if len(ca & cb) != len(shared):
                    self.dropped['same position'] += 1
                    continue
                self.pairs.append((a, b))

    def scale(self, p):
        return tuple((p[k] - self.O[k]) / self.S for k in range(3))

    def unscale(self, x):
        return tuple(self.O[k] + x[k] * self.S for k in range(3))

    # the move variables: 3 per movable vertex, the offset of its scaled position; X[v] the scaled position as a polynomial
    def setup_vars(self):
        self.index = {}
        self.X = {}
        self.bounds = {}
        for j, v in enumerate(self.movable):
            base = self.scale(self.st.V[v]['p'])
            self.X[v] = tuple(Poly.k(base[c]) + Poly.var(3 * j + c) for c in range(3))
            for c in range(3):
                self.index[(v, c)] = 3 * j + c
                # |offset| on axis c: within every drift ball (anchor a, radius r) at most |a_c - base_c| + r
                self.bounds[3 * j + c] = min(abs(a[c] - base[c]) + r for a, r, _ in self.balls[v])
        self.cache = {}

    def pos(self, v):
        if v in self.X:
            return self.X[v]
        return tuple(Poly.k(c) for c in self.scale(self.st.V[v]['p']))

    def area_c(self, t):
        P = [self.pos(v) for v in self.st.T[t]['v']]
        n = cross(sub(P[1], P[0]), sub(P[2], P[0]))
        tau = self.tau[t]
        return dot(n, n) >= Poly.k(tau * tau) if tau > 0 else And()

    def area_weak_c(self, t):
        """a necessary condition of area_c of degree 2: some component of the cross product at least tau 591 / 1024 < tau / sqrt(3)"""
        P = [self.pos(v) for v in self.st.T[t]['v']]
        n = cross(sub(P[1], P[0]), sub(P[2], P[0]))
        w = self.tau[t] * F(591, 1024)
        return Or(*([x >= Poly.k(w) for x in n] + [x <= Poly.k(-w) for x in n]))

    def pair_c(self, a, b):
        va, vb = self.st.T[a]['v'], self.st.T[b]['v']
        shared = set(va) & set(vb)
        if not shared:
            return sep_disjoint(PB, [self.pos(v) for v in va], [self.pos(v) for v in vb])
        s = next(iter(shared))
        oa = [v for v in va if v != s]
        ob = [v for v in vb if v != s]
        return sep_shared_vertex(PB, self.pos(s), self.pos(oa[0]), self.pos(oa[1]), self.pos(ob[0]), self.pos(ob[1]))

    def drift_c(self, v):
        cs = []
        for a, r, _ in self.balls[v]:
            d = sub(self.X[v], tuple(Poly.k(c) for c in a))
            cs.append(dot(d, d) <= Poly.k(r * r))
        return And(*cs)

    def formula(self, key):
        f = self.cache.get(key)
        if f is None:
            kind = key[0]
            f = (self.area_c(key[1]) if kind == 'a' else self.area_weak_c(key[1]) if kind == 'w'
                 else self.drift_c(key[1]) if kind == 'd' else self.pair_c(key[1], key[2]))
            self.cache[key] = f
        return f

    def point(self, Xs):
        """the move variables at scaled positions Xs (dict over moved vertices; a missing one is not moved)"""
        x = {}
        for j, v in enumerate(self.movable):
            base = self.scale(self.st.V[v]['p'])
            for c in range(3):
                x[3 * j + c] = (Xs[v][c] - base[c]) if v in Xs else F(0)
        return x

    def size(self):
        return dict(region=len(self.region), movable=len(self.movable), changed=len(self.changed),
                    area_constraints=len(self.area_tris), pair_constraints=len(self.pairs), dropped=dict(self.dropped),
                    balls=sum(len(b) for b in self.balls.values()))


# ---- the solver loop ----------------------------------------------------------------------------------------------------

TRUE = ('and', ())
FALSE = ('or', ())


def is_const(p):
    return all(m == () for m in p.c)


def simplify(f):
    """atoms with a constant polynomial decided, and/or flattened"""
    if isinstance(f, Atom):
        if is_const(f.p):
            v = f.p.c.get((), F(0))
            return TRUE if (v >= 0 if f.ge else v != 0) else FALSE
        return f
    op, xs = f
    out = []
    for g in xs:
        g = simplify(g)
        if g == TRUE:
            if op == 'or':
                return TRUE
            continue
        if g == FALSE:
            if op == 'and':
                return FALSE
            continue
        if not isinstance(g, Atom) and g[0] == op:
            out.extend(g[1])
        else:
            out.append(g)
    if not out:
        return TRUE if op == 'and' else FALSE
    if len(out) == 1:
        return out[0]
    return (op, tuple(out))


def shifted(p, c):
    """P(c + h) as a polynomial in h (exact)"""
    out = {}
    for m, a in p.c.items():
        # expand prod (c_i + h_i) over the variables of m
        terms = {(): a}
        for i in m:
            nxt = {}
            for mm, v in terms.items():
                w = v * c[i]
                if w:
                    nxt[mm] = nxt.get(mm, 0) + w
                k = tuple(sorted(mm + (i,)))
                nxt[k] = nxt.get(k, 0) + v
            terms = nxt
        for mm, v in terms.items():
            if v:
                w = out.get(mm, 0) + v
                if w:
                    out[mm] = w
                else:
                    out.pop(mm, None)
    return out


def Q(x):
    return z3.Q(x.numerator, x.denominator)


BITS = 40
# a box whose split variables are below 2^-SMALL of the start width is relaxed exactly
SMALL = 14
DUMP_UNKNOWN = None


def round_bits(a, bits):
    """a dyadic close to a with about `bits` significant bits"""
    if a == 0:
        return F(0)
    e = abs(a).numerator.bit_length() - abs(a).denominator.bit_length()
    q = F(2) ** (e - bits)
    return F(round(a / q)) * q


def round_up(a, bits):
    """a dyadic >= a with about `bits` significant bits (a value tiny against the unit is rounded on the grid 2^-(bits + 20))"""
    if a == 0:
        return F(0)
    e = max(abs(a).numerator.bit_length() - abs(a).denominator.bit_length(), -20)
    q = F(2) ** (e - bits)
    n = a / q
    k = n.numerator // n.denominator
    if F(k) < n:
        k += 1
    return F(k) * q


class Relaxer:
    """the linear relaxation of a formula on a box (centre c, half widths w): each atom p >= 0 becomes
    p(c) + grad p(c) (x - c) + R >= 0 with R the sum over the monomials of degree 2 and more of P(c + h) of |coefficient| times the
    product of the half widths: a necessary condition of p >= 0 on the box (sound). An atom p != 0 becomes true (a relaxation).
    Linear atoms are exact."""

    def __init__(self, zv):
        self.zv = zv

    def ge(self, coef, const, lo, hi):
        """sum coef_i x_i + const >= 0 on the box, with the coefficients rounded to BITS bits and the rounding error times the
        largest |x_i| of the box added to the constant (rounded up): a necessary condition, with short numerals"""
        terms = []
        slack = F(0)
        mn = mx = F(0)
        for i, a in coef.items():
            if not a:
                continue
            r = round_bits(a, BITS)
            slack += abs(a - r) * max(abs(lo[i]), abs(hi[i]))
            if r:
                terms.append((r, i))
                mn += min(r * lo[i], r * hi[i])
                mx += max(r * lo[i], r * hi[i])
        b = round_up(const + slack, BITS)
        # (decided on the box: true or false everywhere on it)
        if b + mn >= 0:
            return True
        if b + mx < 0:
            return False
        return z3.Sum([Q(r) * self.zv[i] for r, i in terms] + ([Q(b)] if b else [])) >= 0

    def atom(self, a, c, w, lo, hi):
        if not a.ge:
            return True
        deg = max(len(m) for m in a.p.c)
        if deg <= 1:
            coef = {m[0]: v for m, v in a.p.c.items() if m}
            return self.ge(coef, a.p.c.get((), F(0)), lo, hi)
        q = shifted(a.p, c)
        R = F(0)
        coef = {}
        const = q.get((), F(0))
        for m, v in q.items():
            if len(m) == 1:
                coef[m[0]] = v
            elif len(m) >= 2:
                b = abs(v)
                for i in m:
                    b *= w[i]
                R += b
        # p(c) + sum q_i (x_i - c_i) + R >= 0
        const = const + R - sum(v * c[i] for i, v in coef.items())
        return self.ge(coef, const, lo, hi)

    @staticmethod
    def sure(f, c, w):
        """does the formula hold everywhere on the box (a lower bound of each atom over it, exact)"""
        if isinstance(f, Atom):
            q = shifted(f.p, c)
            spread = F(0)
            for m, v in q.items():
                if m:
                    b = abs(v)
                    for i in m:
                        b *= w[i]
                    spread += b
            q0 = q.get((), F(0))
            return q0 - spread >= 0 if f.ge else abs(q0) > spread
        op, xs = f
        return all(Relaxer.sure(g, c, w) for g in xs) if op == 'and' else any(Relaxer.sure(g, c, w) for g in xs)

    def __call__(self, f, c, w, lo, hi):
        if isinstance(f, Atom):
            return self.atom(f, c, w, lo, hi)
        op, xs = f
        ys = []
        for g in xs:
            y = self(g, c, w, lo, hi)
            if y is True:
                if op == 'or':
                    return True
                continue
            if y is False:
                if op == 'and':
                    return False
                continue
            ys.append(y)
        if not ys:
            return op == 'and'
        if len(ys) == 1:
            return ys[0]
        return z3.And(*ys) if op == 'and' else z3.Or(*ys)


def dnf(f):
    """the formula as alternatives, each a conjunction of atoms (distributed: the formulas of the model are shallow)"""
    if isinstance(f, Atom):
        return [[f]]
    op, xs = f
    if op == 'or':
        return [alt for g in xs for alt in dnf(g)]
    out = [[]]
    for g in xs:
        out = [a + b for a in out for b in dnf(g)]
    return out


# relative padding of the floating-point relaxation: far above the rounding error of the few operations per term (each
# float operation is within 2^-53 of its exact result; a term has fewer than 20 of them, a sum fewer than 2^10 terms)
PAD = 2.0 ** -36


class Compiled:
    """one formula in alternatives of atoms, the atoms' polynomials as arrays of (atom, coefficient, exponents), to bound the
    atoms over a box with floats: P(c + h) = P(c) + grad P(c) h + rest, |rest| <= sum |a| (prod (|c| + w)^e - its terms of degree
    0 and 1 in w); every float result is padded by PAD times the absolute sum it comes from, so the bounds are rigorous"""

    def __init__(self, f, nvars):
        self.alts = dnf(f)
        atoms = []
        self.alt_atoms = []
        for alt in self.alts:
            ids = []
            for a in alt:
                ids.append(len(atoms))
                atoms.append(a)
            self.alt_atoms.append(ids)
        self.atoms = atoms
        self.ge = np.array([a.ge for a in atoms], bool)
        rows, coef, exps = [], [], []
        const = np.zeros(len(atoms))
        for k, a in enumerate(atoms):
            for m, v in a.p.c.items():
                if not m:
                    const[k] = float(v)
                    continue
                e = np.zeros(nvars, np.int64)
                for i in m:
                    e[i] += 1
                rows.append(k)
                coef.append(float(v))
                exps.append(e)
        self.n = nvars
        self.const = const
        self.rows = np.array(rows, np.int64)
        self.coef = np.array(coef, float)
        self.exps = np.array(exps, np.int64).reshape(len(rows), nvars)
        self.vars_of = [sorted({i for m in a.p.c for i in m}) for a in atoms]

    def bounds(self, c, w):
        """per atom: p(c) (float), gradient at c (atoms x vars), the rest bound R and the padding E (both >= 0)"""
        na, n = len(self.atoms), self.n
        if len(self.rows) == 0:
            return self.const.copy(), np.zeros((na, n)), np.zeros(na), np.abs(self.const) * PAD
        ce = np.power(c[None, :], self.exps)            # c_j^e_j per term and var
        val = self.coef * ce.prod(axis=1)
        p0 = self.const + np.bincount(self.rows, val, na)
        # gradient: a e_i c_i^(e_i - 1) prod_{j != i} c_j^e_j
        grad = np.zeros((na, n))
        for i in range(n):
            e = self.exps[:, i]
            sel = e > 0
            if not sel.any():
                continue
            cm = ce.copy()
            cm[:, i] = np.where(sel, np.power(c[i], np.maximum(e - 1, 0)) * e, 0)
            grad[:, i] = np.bincount(self.rows, self.coef * cm.prod(axis=1), na)
        ac = np.abs(c)
        full = np.power((ac + w)[None, :], self.exps).prod(axis=1)
        a0 = np.power(ac[None, :], self.exps)
        deg0 = a0.prod(axis=1)
        deg1 = np.zeros(len(self.rows))
        for i in range(n):
            e = self.exps[:, i]
            sel = e > 0
            if not sel.any():
                continue
            cm = a0.copy()
            cm[:, i] = np.where(sel, np.power(ac[i], np.maximum(e - 1, 0)) * e * w[i], 0)
            deg1 += np.where(sel, cm.prod(axis=1), 0)
        absc = np.abs(self.coef)
        rest = np.bincount(self.rows, absc * np.maximum(full - deg0 - deg1, 0), na)
        mag = np.abs(self.const) + np.bincount(self.rows, absc * full, na)
        return p0, grad, rest, mag * PAD + rest * PAD


def fq(x):
    n, d = float(x).as_integer_ratio()
    return z3.Q(n, d)


class FastRelaxer:
    """the linear relaxation of a compiled formula on a box, with floats and rigorous padding: atom p >= 0 becomes
    p(c) + g (x - c) + R + E >= 0 (R the rest of degree 2 and more over the box, E the padding); an atom p != 0 becomes true. An
    atom that holds (fails) for every x of the box in that linear form is decided."""

    def __init__(self, zv):
        self.zv = zv

    def relax(self, comp, c, w, lo, hi, inner=False):
        """outer (inner=False): a necessary condition of the formula on the box, the relaxation of the branch and bound;
        inner: a sufficient one (p(c) + g (x - c) - R - E >= 0 implies p >= 0), to find a candidate that holds exactly"""
        p0, grad, rest, pad = comp.bounds(c, w)
        xm = np.maximum(np.abs(lo), np.abs(hi))
        err = pad + np.abs(grad) @ np.abs(c) * PAD + (np.abs(grad) @ xm) * PAD * 2
        up = p0 + rest + err - grad @ c                    # upper linear form: p(x) <= g x + up on the box
        dn = p0 - rest - err - grad @ c                    # lower linear form: p(x) >= g x + dn on the box
        gl = np.minimum(grad * lo[None, :], grad * hi[None, :]).sum(axis=1)
        gh = np.maximum(grad * lo[None, :], grad * hi[None, :]).sum(axis=1)
        alts = []
        for ids in comp.alt_atoms:
            terms = []
            dead = False
            for k in ids:
                lin = [fq(grad[k, i]) * self.zv[i] for i in comp.vars_of[k] if grad[k, i] != 0]
                if not comp.ge[k]:
                    if not inner:
                        continue
                    # (p != 0: the lower form above 0 or the upper form below 0)
                    if dn[k] + gl[k] > 0 or up[k] + gh[k] < 0:
                        continue
                    if dn[k] + gh[k] <= 0 and up[k] + gl[k] >= 0:
                        dead = True
                        break
                    terms.append(z3.Or(z3.Sum(lin + [fq(dn[k])]) > 0, z3.Sum(lin + [fq(up[k])]) < 0) if lin else z3.BoolVal(False))
                    continue
                b = dn[k] if inner else up[k]
                if b + gl[k] >= 0:
                    continue
                if b + gh[k] < 0:
                    dead = True
                    break
                # (x in the box: sum g_i x_i + b, the float products exact in z3; the float sums are padded in err)
                terms.append(z3.Sum(lin + [fq(b)]) >= 0 if lin else z3.BoolVal(b >= 0))
            if dead:
                continue
            if not terms:
                return True
            alts.append(terms[0] if len(terms) == 1 else z3.And(*terms))
        if not alts:
            return False
        return alts[0] if len(alts) == 1 else z3.Or(*alts)

    @staticmethod
    def sure(comp, c, w):
        """does the formula hold everywhere on the box (rigorous lower bounds of the atoms)"""
        p0, grad, rest, pad = comp.bounds(c, w)
        spread = np.abs(grad) @ w + rest + pad
        for ids in comp.alt_atoms:
            if all((p0[k] - spread[k] >= 0) if comp.ge[k] else (abs(p0[k]) > spread[k]) for k in ids):
                return True
        return False

    @staticmethod
    def maybe_violated(comp, x):
        """a float screen of a candidate: False only if every alternative's atoms clearly fail or one clearly holds..."""
        p0, grad, rest, pad = comp.bounds(x, np.zeros_like(x))
        tol = pad * 1e3
        for ids in comp.alt_atoms:
            if all((p0[k] >= -tol[k]) if comp.ge[k] else (abs(p0[k]) > tol[k] or True) for k in ids):
                return False
        return True


def screen(pb, moves, tol=1e-9):
    """a float screen of N candidate configurations (moves: vertex -> N x 3 array of scaled offsets): the mask of those that
    satisfy every constraint of the model up to a relative tolerance (a candidate for the exact check, nothing more)"""
    st = pb.st
    N = len(next(iter(moves.values())))
    base = {v: np.array([float(c) for c in pb.scale(st.V[v]['p'])]) for v in st.V}

    def pos(v):
        return base[v] + moves[v] if v in moves else np.broadcast_to(base[v], (N, 3))

    def ge(a, b):
        # a >= b up to tol times their magnitude
        return a >= b - tol * (np.abs(a) + np.abs(b) + 1e-300)

    ok = np.ones(N, bool)
    for v, d in moves.items():
        for a, r, _ in pb.balls[v]:
            ok &= ((base[v] + d - np.array([float(q) for q in a])) ** 2).sum(1) <= float(r) ** 2 * (1 + tol)
    touched = set(moves)
    for t in pb.area_tris:
        if not touched & set(st.T[t]['v']):
            continue
        a, b, c = [pos(v) for v in st.T[t]['v']]
        n = np.cross(b - a, c - a)
        ok &= ge((n ** 2).sum(1), float(pb.tau[t]) ** 2)
    for ta, tb in pb.pairs:
        if not ok.any():
            break
        if not touched & (set(st.T[ta]['v']) | set(st.T[tb]['v'])):
            continue
        T = [pos(v) for v in st.T[ta]['v']]
        U = [pos(v) for v in st.T[tb]['v']]
        sh = set(st.T[ta]['v']) & set(st.T[tb]['v'])
        sep = np.zeros(N, bool)
        if not sh:
            eT = [T[1] - T[0], T[2] - T[1], T[0] - T[2]]
            eU = [U[1] - U[0], U[2] - U[1], U[0] - U[2]]
            for h in [np.cross(eT[0], T[2] - T[0]), np.cross(eU[0], U[2] - U[0])] + [np.cross(e, f) for e in eT for f in eU]:
                pt = np.stack([(h * p).sum(1) for p in T])
                pu = np.stack([(h * q).sum(1) for q in U])
                sep |= ge(pu.min(0), pt.max(0)) | ge(pt.min(0), pu.max(0))
        else:
            s = next(iter(sh))
            oa = [v for v in st.T[ta]['v'] if v != s]
            ob = [v for v in st.T[tb]['v'] if v != s]
            g = [pos(x) - pos(s) for x in oa + ob]
            for i, j in itertools.combinations(range(4), 2):
                h = np.cross(g[i], g[j])
                scale = np.linalg.norm(h, axis=1) * np.max([np.linalg.norm(x, axis=1) for x in g], axis=0)
                pr = [(h * x).sum(1) for x in g]
                pos_ = [p >= -tol * scale for p in pr]
                neg_ = [p <= tol * scale for p in pr]
                nz = (h != 0).any(1)
                sep |= nz & pos_[0] & pos_[1] & neg_[2] & neg_[3]
                sep |= nz & neg_[0] & neg_[1] & pos_[2] & pos_[3]
        ok &= sep
    return ok


def grid_seeds(pb, k, sizes=5, random_n=20000, seed=1, max_sets=120, log=None, smallest=1):
    """candidates in the manner of the library's position search: every set of at most k movable vertices with a corner of the
    target among them (sets of two: all; corners first), each vertex by room / 2^j (j < sizes) along one of the 26
    directions, every combination for one or two vertices, `random_n` random combinations for more; screened in floats.
    Returns exact points (dict var -> Dyadic) of the survivors, at most 50 (the solver checks them exactly)"""
    st = pb.st
    corners = sorted({v for t in pb.targets for v in st.T[t]['v'] if v in pb.movable})
    others = [v for v in pb.movable if v not in corners]
    dirs = np.array([(a, b, c) for a in (-1, 0, 1) for b in (-1, 0, 1) for c in (-1, 0, 1) if (a, b, c) != (0, 0, 0)], float)
    dirs /= np.linalg.norm(dirs, axis=1)[:, None]
    cand = np.array([dirs[i] / 2 ** j for j in range(sizes) for i in range(26)])
    rng = np.random.default_rng(seed)
    sets = []
    for size in range(smallest, k + 1):
        for nc in range(min(size, len(corners)), 0, -1):
            for cs in itertools.combinations(corners, nc):
                for os_ in itertools.combinations(others, size - nc):
                    sets.append(cs + os_)
    if len(sets) > max_sets:
        # (a deterministic sample of the sets, the corners-only ones first)
        keep = [x for x in sets if set(x) <= set(corners)]
        rest = [x for x in sets if not set(x) <= set(corners)]
        pick = rng.choice(len(rest), size=max_sets - len(keep), replace=False) if len(rest) > max_sets - len(keep) else range(len(rest))
        sets = keep + [rest[i] for i in sorted(pick)]
    out = []
    for vs in sets[:max_sets]:
        if len(vs) <= 2:
            N = len(cand) ** len(vs)
            idx = [(np.arange(N) // (len(cand) ** ci)) % len(cand) for ci in range(len(vs))]
        else:
            N = random_n
            idx = [rng.integers(0, len(cand), N) for _ in vs]
        moves = {}
        for ci, v in enumerate(vs):
            room = max(float(st.V[v]['room']), 0) / float(pb.S)
            moves[v] = cand[idx[ci]] * room
        ok = screen(pb, moves)
        if log and ok.any():
            log(f'    seeds: {vs} {int(ok.sum())} of {N} pass the float screen')
        for i in np.nonzero(ok)[0][:10]:
            x = {q: F(0) for q in range(3 * len(pb.movable))}
            for v in vs:
                j = pb.movable.index(v)
                for q in range(3):
                    x[3 * j + q] = F(float(moves[v][i][q]))
            out.append(x)
        if len(out) >= 50:
            break
    return out


def solve(pb, k, timeout_s=300, max_boxes=200000, log=None, seeds=(), start_boxes=None):
    """branch and bound over boxes of the moves. Per box the linear relaxation of the active constraints (FastRelaxer; the
    drift balls by their tangent planes at the box centre, exact) and "at most k vertices move" are decided exactly by z3
    (linear real arithmetic with booleans): an infeasible box is pruned; a feasible one gives a candidate that is checked
    exactly against every constraint: all hold -> SAT; a violated constraint that is not active -> it becomes active (for
    every box); else the box is split in half along the widest variable of a nonlinear term of a violated constraint. All
    boxes pruned -> UNSAT. `seeds`: candidate points (dict var -> Dyadic) checked first (eg the library's grid). Returns
    dict(answer, time, boxes, constraints, active, witness)"""
    t0 = time.time()
    pb.setup_vars()
    n = 3 * len(pb.movable)
    if pb.target not in pb.tau or not any(v in pb.X for v in pb.st.T[pb.target]['v']):
        return dict(answer='unsat', reason='no corner of the target is movable', time=0, boxes=0, constraints=0, active=[])
    zv = {i: z3.Real(f'x{i}') for i in range(n)}
    rel = FastRelaxer(zv)
    xrel = Relaxer(zv)
    nl_split = [False] * n
    Mv = {v: z3.Bool(f'm{v}') for v in pb.movable}
    base = []
    for j, v in enumerate(pb.movable):
        base.append(z3.Implies(z3.Not(Mv[v]), z3.And(*[zv[3 * j + c] == 0 for c in range(3)])))
    base.append(z3.Sum([z3.If(Mv[v], 1, 0) for v in pb.movable]) <= k)
    checks = [('a', t) for t in pb.area_tris] + [('p', a, b) for a, b in pb.pairs]
    simple, compiled = {}, {}

    def formula(key):
        if key not in simple:
            simple[key] = simplify(pb.formula(key))
        return simple[key]

    def comp(key):
        if key not in compiled:
            compiled[key] = Compiled(formula(key), n)
        return compiled[key]

    def drift_ok(x):
        return all(holds(pb.formula(('d', v)), x) for v in pb.movable)

    # (the start box rounded outwards to doubles: it contains every move within the drift bounds)
    hi0 = np.array([math.nextafter(float(pb.bounds[i]), math.inf) for i in range(n)])
    lo0 = -hi0
    c0, w0 = (lo0 + hi0) / 2, (hi0 - lo0) / 2
    t1 = time.time()
    # (a constraint that holds everywhere on the start box holds for every candidate: not checked)
    checks = [key for key in checks if not FastRelaxer.sure(comp(key), c0, w0)]
    if log:
        log(f'    {len(checks)} constraints can be violated in the start box ({time.time() - t1:.1f} s)')

    def violated(x):
        xf = np.array([float(x[i]) for i in range(n)])
        out = []
        for key in checks:
            if FastRelaxer.sure(comp(key), xf, np.zeros(n)):
                continue
            if not holds(formula(key), x):
                out.append(key)
        return out

    def witness(x):
        moved = {}
        for j, v in enumerate(pb.movable):
            if any(x[3 * j + q] != 0 for q in range(3)):
                b = pb.scale(pb.st.V[v]['p'])
                moved[v] = tuple(b[q] + x[3 * j + q] for q in range(3))
        return moved

    for x in seeds:
        if sum(1 for j in range(len(pb.movable)) if any(x[3 * j + q] for q in range(3))) <= k and drift_ok(x) and not violated(x):
            return dict(answer='sat', time=time.time() - t0, boxes=0, constraints=0, active=[], witness=witness(x), seeded=True)

    active = [('a', t) for t in pb.targets]
    cuts = []
    # (start_boxes: a cover of the feasible set, eg the products of the single-vertex covers; every other point is excluded already)
    stack = [(lo0, hi0)] if start_boxes is None else list(start_boxes)[::-1]
    boxes = 0
    while stack:
        if time.time() - t0 > timeout_s:
            return dict(answer='timeout', time=time.time() - t0, boxes=boxes, constraints=len(active), open_boxes=len(stack), active=active)
        if boxes >= max_boxes:
            return dict(answer='unknown', reason='box limit', time=time.time() - t0, boxes=boxes, constraints=len(active), active=active)
        lo, hi = stack.pop()
        boxes += 1
        c, w = (lo + hi) / 2, (hi - lo) / 2
        cd = {i: F(float(c[i])) for i in range(n)}
        # (first an inner relaxation of the active constraints and of the drift: a model of it holds exactly for them)
        si = z3.SolverFor('QF_LRA')
        si.add(*base)
        for i in range(n):
            si.add(zv[i] >= fq(lo[i]), zv[i] <= fq(hi[i]))
        inner_ok = True
        for key in active + [('d', v) for v in pb.movable]:
            y = rel.relax(comp(key), c, w, lo, hi, inner=True)
            if y is False:
                inner_ok = False
                break
            if y is not True:
                si.add(y)
        if inner_ok:
            si.set('timeout', max(1, int((timeout_s - (time.time() - t0)) * 1000)))
            if si.check() == z3.sat:
                m = si.model()
                x = {i: dyadic_near(m.eval(zv[i], model_completion=True).numerator_as_long(), m.eval(zv[i], model_completion=True).denominator_as_long()) for i in range(n)}
                viol = violated(x)
                if not viol and drift_ok(x):
                    return dict(answer='sat', time=time.time() - t0, boxes=boxes, constraints=len(active), active=active, witness=witness(x))
                new = [key for key in viol if key not in active]
                active.extend(new[:8])
        while True:
            s = z3.SolverFor('QF_LRA')
            s.add(*base)
            for i in range(n):
                s.add(zv[i] >= fq(lo[i]), zv[i] <= fq(hi[i]))
            # the drift balls: |base + x - a|^2 <= r^2 implies its tangent plane at the centre (convex), exact
            for j, v in enumerate(pb.movable):
                bp = pb.scale(pb.st.V[v]['p'])
                for a, r, _ in pb.balls[v]:
                    d0 = [bp[q] + cd[3 * j + q] - a[q] for q in range(3)]
                    const = sum(d * d for d in d0) - sum(2 * d0[q] * cd[3 * j + q] for q in range(3)) - r * r
                    s.add(z3.Sum([Q(2 * d0[q]) * zv[3 * j + q] for q in range(3)] + [Q(const)]) <= 0)
            s.add(*cuts)
            dead = False
            small = any(nl_split) and all(w[i] <= w0[i] * 2.0 ** -SMALL for i in range(n) if w0[i] > 0 and nl_split[i])
            for key in active:
                if small:
                    # (a small box: the exact relaxation (no float padding), which decides the boundary cases)
                    cdx = {i: F(float(c[i])) for i in range(n)}
                    wdx = {i: F(float(w[i])) for i in range(n)}
                    lox = {i: F(float(lo[i])) for i in range(n)}
                    hix = {i: F(float(hi[i])) for i in range(n)}
                    y = xrel(formula(key), cdx, wdx, lox, hix)
                else:
                    y = rel.relax(comp(key), c, w, lo, hi)
                if y is False:
                    dead = True
                    break
                if y is not True:
                    s.add(y)
            if dead:
                break
            left = timeout_s - (time.time() - t0)
            s.set('timeout', max(1, int(left * 1000)))
            r = s.check()
            if r == z3.unknown:
                return dict(answer='timeout', reason=s.reason_unknown(), time=time.time() - t0, boxes=boxes, constraints=len(active), active=active)
            if r == z3.unsat:
                break
            m = s.model()
            x = {}
            for i in range(n):
                v = m.eval(zv[i], model_completion=True)
                x[i] = dyadic_near(v.numerator_as_long(), v.denominator_as_long())
            viol = violated(x)
            if not viol and drift_ok(x):
                return dict(answer='sat', time=time.time() - t0, boxes=boxes, constraints=len(active), active=active, witness=witness(x))
            new = [key for key in viol if key not in active]
            if new:
                active.extend(new[:8])
                if log:
                    log(f'    box {boxes}: {len(active)} active, added {new[:3]} ({time.time() - t0:.1f} s, {len(stack)} open)')
                continue
            # split: the widest (relative to the start) variable of a nonlinear term of a violated constraint, or of the drift
            acc = set()
            for key in viol:
                for a in comp(key).atoms:
                    for mm in a.p.c:
                        if len(mm) >= 2:
                            acc.update(mm)
            if not drift_ok(x):
                # (a tangent plane of each violated drift ball at the candidate: valid everywhere, it cuts the candidate off)
                for j, v in enumerate(pb.movable):
                    bp = pb.scale(pb.st.V[v]['p'])
                    for a, r_, _ in pb.balls[v]:
                        d0 = [bp[q] + x[3 * j + q] - a[q] for q in range(3)]
                        if sum(d * d for d in d0) > r_ * r_:
                            const = sum(d * d for d in d0) - sum(2 * d0[q] * x[3 * j + q] for q in range(3)) - r_ * r_
                            cuts.append(z3.Sum([Q(2 * d0[q]) * zv[3 * j + q] for q in range(3)] + [Q(const)]) <= 0)
                if not viol:
                    continue
            acc = {i for i in acc if w[i] > w0[i] * 2.0 ** -40}
            if not acc:
                acc = {i for i in range(n) if w[i] > w0[i] * 2.0 ** -40}
            if not acc:
                return dict(answer='unknown', reason='box too small', time=time.time() - t0, boxes=boxes, constraints=len(active), active=active)
            i = max(acc, key=lambda i: (w[i] / w0[i] if w0[i] else 0, -i))
            nl_split[i] = True
            if w[i] <= w0[i] * 2.0 ** -40:
                return dict(answer='unknown', reason='box too small', time=time.time() - t0, boxes=boxes, constraints=len(active), active=active)
            a_hi = hi.copy(); a_hi[i] = c[i]
            b_lo = lo.copy(); b_lo[i] = c[i]
            # (the half without the candidate first: the candidate of the outer relaxation lies on its boundary)
            if float(x[i]) <= c[i]:
                stack.append((lo, a_hi)); stack.append((b_lo, hi))
            else:
                stack.append((b_lo, hi)); stack.append((lo, a_hi))
            break
    return dict(answer='unsat', time=time.time() - t0, boxes=boxes, constraints=len(active), active=active)


def full_check(pb, Xs):
    """every constraint of the model, exactly, at scaled positions Xs (dict over moved vertices)"""
    x = pb.point(Xs)
    keys = [('d', v) for v in pb.movable] + [('a', t) for t in pb.area_tris] + [('p', a, b) for a, b in pb.pairs]
    return [c for c in keys if not holds(pb.formula(c), x)]


def to_doubles(pb, witness):
    """the witness as doubles (nearest), unscaled, and back as exact scaled points"""
    out, back = {}, {}
    for v, x in witness.items():
        p = pb.unscale(x)
        d = tuple(float(c) for c in p)
        out[v] = d
        back[v] = pb.scale(tuple(F(c) for c in d))
    return out, back


def hexd(x):
    return struct.pack('>d', x).hex()


def attempt(st, t, ring, allow_fixed, k, timeout_s, prove=True, log=print, smallest=1, max_sets=120, random_n=20000):
    """one question: seeds of the library's grid first, then (prove) the branch and bound; a row of the report"""
    pb = Problem(st, t, ring=ring, allow_fixed=allow_fixed)
    pb.setup_vars()
    t0 = time.time()
    seeds = grid_seeds(pb, k, smallest=smallest, max_sets=max_sets, random_n=random_n)
    ts = time.time() - t0
    if seeds or prove:
        res = solve(pb, k, timeout_s=timeout_s if prove else 0.001, seeds=seeds)
    else:
        res = dict(answer='none', time=0, boxes=0, constraints=0, active=[])
    if not prove and res['answer'] != 'sat':
        res['answer'] = 'not found'
    row = dict(case=st.case, triangle=t, corners=list(st.T[t]['v']), fixed=[st.V[v]['fixed'] for v in st.T[t]['v']],
               ring=ring, k=k, allow_fixed=allow_fixed, size=pb.size(), answer=res['answer'], seed_time=round(ts, 1),
               solve_time=round(res['time'], 1), boxes=res.get('boxes', 0), active=len(res.get('active', [])))
    if 'reason' in res:
        row['reason'] = res['reason']
    if res['answer'] == 'unsat':
        core = sorted({v for key in res.get('active', []) for tt in key[1:] for v in st.T[tt]['v']})
        row['core_vertices'] = core
        row['core_is_corners'] = set(core) <= set(st.T[t]['v'])
    if res['answer'] == 'sat':
        dbls, back = to_doubles(pb, res['witness'])
        row['moved'] = sorted(res['witness'])
        row['moved_fixed'] = [v for v in row['moved'] if st.V[v]['fixed']]
        row['moved_corners'] = [v for v in row['moved'] if v in st.T[t]['v']]
        row['step_over_room'] = {str(v): round(float(sum((a - b) ** 2 for a, b in zip(res['witness'][v], pb.scale(st.V[v]['p'])))) ** 0.5
                                           * float(pb.S) / float(st.V[v]['room']), 3) if st.V[v]['room'] > 0 else None for v in row['moved']}
        row['double_check'] = [list(map(str, c)) for c in full_check(pb, back)]
        row['witness'] = {str(v): [hexd(c) for c in dbls[v]] for v in sorted(dbls)}
    if log:
        log(json.dumps({kk: vv for kk, vv in row.items() if kk not in ('witness', 'size')}))
    return row


def run(path, timeout_s=120, log=print, ring1_sets=120, random_n=20000):
    """the ladder per degenerate triangle of a state:
    1. the free corners only (the joint stage's rule), ring 0, k = their number: seeds, then the proof attempt;
    2. the fixed (soft) corners too, ring 0, k = 2, then 3: seeds, then the proof attempt;
    3. if nothing was found: ring 1 (free, then fixed too), k = 2, 3, 4: seeds only (a search, not a proof)."""
    st = State.load(path)
    rows = []
    for t in st.bad:
        free = [v for v in st.T[t]['v'] if not st.V[v]['fixed'] and not st.V[v]['boundary'] and not st.V[v]['bowtie']]
        r = attempt(st, t, 0, False, max(1, min(len(free), 3)), timeout_s, log=log)
        rows.append(r)
        found = r['answer'] == 'sat'
        for k in (2, 3):
            if found:
                break
            r = attempt(st, t, 0, True, k, timeout_s, log=log, smallest=k)
            rows.append(r)
            found = r['answer'] == 'sat'
        for af in (False, True):
            for k in (2, 3, 4):
                if found or ring1_sets <= 0:
                    break
                r = attempt(st, t, 1, af, k, timeout_s, prove=False, log=log, smallest=k, max_sets=ring1_sets, random_n=random_n)
                rows.append(r)
                found = r['answer'] == 'sat'
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dumps', nargs='+')
    ap.add_argument('--timeout', type=float, default=120, help='per proof attempt (s)')
    ap.add_argument('--ring1-sets', type=int, default=120, help='vertex sets searched per question in ring 1')
    ap.add_argument('--random-n', type=int, default=20000, help='random grid combinations per set of three or four vertices')
    ap.add_argument('--out')
    a = ap.parse_args()
    rows = []
    for p in a.dumps:
        rows += run(p, a.timeout, ring1_sets=a.ring1_sets, random_n=a.random_n)
        if a.out:
            with open(a.out, 'w', encoding='utf-8') as f:
                json.dump(dict(z3=z3.get_version_string(), rows=rows), f, indent=1)
    return 0


if __name__ == '__main__':
    sys.exit(main())
