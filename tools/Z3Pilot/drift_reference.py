"""Z3 pilot 2, step 1b: the reference rule for SafeRounding's drift (docs/design/SAFE_ROUNDING_D1_DRIFT.md), the
tighter accounting proposed for D1, checked before it is built. No library code implements it yet.

Today (the apex rule, drift_apex.py) every edit adds its bound delta to M, the largest drift among its vertices, so
a chain of neighbouring edits adds up although no point of the surface moved further than one edit. The reference
rule keeps, per triangle t, a reference triangle (three anchor points, one per corner) and a carried bound c_t, and
per collapsed triangle a ghost (the same data, attached to the vertices it collapsed onto):

    B(t) = c_t + max over the corners k of |pos(v_k) - anchor_k(t)|             (the bound of a triangle or ghost)

  - a displacement edit (a move, a slide, a contraction: the same triangles with a corner moved or renamed) changes
    no anchor and no c; a triangle that collapses becomes a ghost; the edit is accepted if every B it changes is <= L;
  - a generic edit (flip, zip, strip zip, vertex removal: R removed, A added, two-sided bound delta) gives every
    triangle of A the anchors 'its corners now' and c = N = max(B over R, B over the ghosts it loses) + delta, where a
    ghost is lost if the vertex or edge it collapsed onto is no simplex of the mesh afterwards; accepted if N <= L.

Invariant J, with S0 the surface at the start: (J1) every point of a reference triangle of S lies within c_t of S0;
(J2) every point of S0 lies within c of the reference triangle of some triangle or ghost; (J3) a ghost's vertices
span a simplex of S. With the convexity of the norm, J gives H(S, S0) <= max B.

What is checked here (every obligation UNSAT with satisfiable, replayed assumptions; every mutant SAT with a witness
replayed with fractions outside Z3; UNKNOWN never counts):

  A. convexity, in R^3 with the Euclidean norm: a point of a triangle moves by at most the largest move of a corner;
  B. the one-edit lemma, shape-free, distances as real variables (as drift_apex.generic_formula);
  C. a bounded search on tiny configurations: chains of 3 to 5 vertices on the real line (a 1-complex: segments for
     triangles, a collapsed segment is a ghost at a vertex), every script of up to three edits (move, contraction
     with the kept vertex in place or moved, vertex removal, re-anchoring) with every real position: the true
     Hausdorff distance to the start is at most the largest bound, each segment's points lie within its bound, no
     bound at the end exceeds the largest value an edit compared with L (the reported drift), and
     every bound is at most the apex rule's drift at the same place (so the reference rule accepts whatever the apex
     rule accepts). On the line the search is linear real arithmetic and complete for the configurations listed;
     it exercises the accounting, not the geometry (the per-edit bounds delta stay review-only, as before);
  D. the rounding: the computed distance of two points (Vector3d.Distance), and one generic edit of the chain bound.

MUTANTS of the rule (C) are searched for a counterexample the same way; each one found is pinned in
test_drift_reference.py and replayed there with fractions.
"""
import itertools
import re
from fractions import Fraction

import z3 as z

from filter_rules import QB, ZB, Vars, _check, ROOT

U = Fraction(1, 2**53)          # unit roundoff of binary64 round to nearest

# the specification of the rule (SAFE_ROUNDING_D1_DRIFT.md section 3); the model below reads it, so a change of the
# specification is checked by Z3
RULE = dict(displacement='remeasure from the anchors', collapsed='ghost at the kept vertex',
            generic_carry='max B over R and the lost ghosts, plus delta', generic_anchor='the corners now',
            acceptance='every changed bound <= L')

# the mutants of the bounded search: (name, what it breaks)
MUTANTS = {
    'no_ghosts': 'a collapsed segment is forgotten',
    'ghost_dies_with_its_vertex': 'the ghosts of the vanishing vertex are dropped in a contraction',
    'generic_carry_only': 'a generic edit takes the largest carried bound of R, not the largest B',
    'generic_ignores_lost_ghosts': 'a generic edit forgets the ghosts of the vertex it removes',
    'generic_without_delta': 'a generic edit does not add its bound',
    'kept_vertex_anchor': 'a contraction gives the renamed corner the kept vertex\'s position as its anchor',
    'move_skips_ghosts': 'a move does not evaluate the ghosts at the moved vertex (the reported drift misses them)',
}


def spec():
    """RULE as the flags of the model; raises on a specification the lemma does not cover"""
    expected = dict(displacement='remeasure from the anchors', collapsed='ghost at the kept vertex',
                    generic_carry='max B over R and the lost ghosts, plus delta', generic_anchor='the corners now',
                    acceptance='every changed bound <= L')
    for k, v in expected.items():
        if RULE.get(k) != v:
            raise ValueError(f'RULE[{k!r}] = {RULE.get(k)!r}: the lemma covers {v!r} only')
    return set()


def _if(B, c, a, b):
    return z.If(c, a, b) if B is ZB else (a if c else b)


def _max(B, xs):
    m = xs[0]
    for x in xs[1:]:
        m = _if(B, x > m, x, m)
    return m


def _min(B, xs):
    m = xs[0]
    for x in xs[1:]:
        m = _if(B, x < m, x, m)
    return m


def _abs(B, x):
    return _if(B, x >= 0, x, -x)


# ---------------------------------------------------------------------------------------------------------------
# A. convexity (R^3, Euclidean): x = sum l_k p_k, xr = sum l_k a_k, |p_k - a_k| <= r  =>  |x - xr| <= r

# Z3's nonlinear solver returns unknown on the coordinates directly (at a timeout of 20 s, 2026-11-09), so the claim is
# split: (1) for two vectors e, f of R^3: 2 e.f <= |e|^2 + |f|^2 (|e - f|^2 >= 0, in coordinates); (2) with the Gram
# entries G_kk = |e_k|^2 <= r^2 and 2 G_jk <= G_jj + G_kk as real variables, |sum l_k e_k|^2 = sum l_j l_k G_jk <= r^2.

GRAM_VARS = ['R', 'l0', 'l1', 'l2', 'g00', 'g11', 'g22', 'g01', 'g02', 'g12']
DOT_VARS = [f'{n}{c}' for n in 'ef' for c in 'xyz']


def dot_formula(B, v):
    """(1): 2 e.f > |e|^2 + |f|^2 has no solution"""
    e, f = [v[f'e{c}'] for c in 'xyz'], [v[f'f{c}'] for c in 'xyz']
    dot = e[0] * f[0] + e[1] * f[1] + e[2] * f[2]
    return [], 2 * dot > e[0] * e[0] + e[1] * e[1] + e[2] * e[2] + f[0] * f[0] + f[1] * f[1] + f[2] * f[2]


def convexity_formula(B, v, variant=None):
    """(2): e_k = p_k - a_k the move of corner k, G its Gram matrix, R = r^2; the point's move is sum l_k e_k.
    Variants (mutants): 'no_sum' (the weights need not sum to 1), 'two_corners' (only two of the moves are bounded),
    'no_dot_bound' (without (1))."""
    l = [v.l0, v.l1, v.l2]
    g = {(0, 0): v.g00, (1, 1): v.g11, (2, 2): v.g22, (0, 1): v.g01, (0, 2): v.g02, (1, 2): v.g12}
    hyps = [v.R >= 0] + [x >= 0 for x in l] + [g[k, k] >= 0 for k in range(3)]
    if variant != 'no_sum':
        hyps.append(l[0] + l[1] + l[2] == 1)
    hyps += [g[k, k] <= v.R for k in range(3) if not (variant == 'two_corners' and k == 2)]
    if variant != 'no_dot_bound':
        hyps += [2 * g[j, k] <= g[j, j] + g[k, k] for j, k in ((0, 1), (0, 2), (1, 2))]
    square = sum(l[k] * l[k] * g[k, k] for k in range(3)) + 2 * sum(l[j] * l[k] * g[j, k] for j, k in ((0, 1), (0, 2), (1, 2)))
    return hyps, square > v.R


# ---------------------------------------------------------------------------------------------------------------
# B. the one-edit lemma, shape-free. T a triangle (or ghost) before, c its carried bound, rho the largest move of
# its corners from their anchors (before: rho, after a displacement edit: rho2), B = c + rho.

LEMMA_VARS = 'c rho rho2 L delta N Bmax xr_s0 x_xr x_s0 y_xr y_x z_zr zr_s0 z_s0 x_z y_zr y_z z_t y_t'


def lemma_formula(B, v, claim, variant=None):
    """claims (each the negation of what the lemma states):
    'D_I1': displacement edit, a point x of the moved triangle (its reference point xr, |x - xr| <= rho2 by A, and
            dist(xr, S0) <= c by J1) lies within B' = c + rho2 of S0;
    'D_I2': displacement edit, a point y of S0 with witness T (dist(y, xr) <= c by J2, xr's image x in the moved
            triangle or in the simplex a ghost collapsed onto, |x - xr| <= rho2) lies within B' of S;
    'G_J1': generic edit, a point x of an added triangle (its own reference triangle) lies within N of S0: (E1) gives
            z in a removed triangle T with |x - z| <= delta, z's reference point zr with |z - zr| <= rho,
            dist(zr, S0) <= c, and N >= c + rho + delta;
    'G_J2': generic edit, a point y of S0 whose witness is a removed triangle or a lost ghost T (dist(y, zr) <= c,
            the image z of zr in a removed triangle, |z - zr| <= rho) lies within N of an added triangle: (E2) gives
            dist(z, T') <= delta;
    'G_bound': N <= L if the edit is accepted.
    Variants (mutants): 'carry_only' (N >= c + delta), 'no_delta' (N >= c + rho), 'no_acceptance'."""
    spec()
    hyps = [x >= 0 for x in (v.c, v.rho, v.rho2, v.L, v.delta, v.N)]
    if claim == 'D_I1':
        hyps += [v.xr_s0 >= 0, v.x_xr >= 0, v.x_s0 >= 0, v.xr_s0 <= v.c, v.x_xr <= v.rho2, v.x_s0 <= v.x_xr + v.xr_s0]
        return hyps, v.x_s0 > v.c + v.rho2
    if claim == 'D_I2':
        hyps += [v.y_xr >= 0, v.x_xr >= 0, v.y_x >= 0, v.y_xr <= v.c, v.x_xr <= v.rho2, v.y_x <= v.y_xr + v.x_xr]
        return hyps, v.y_x > v.c + v.rho2
    n_at_least = {'carry_only': v.c + v.delta, 'no_delta': v.c + v.rho}.get(variant, v.c + v.rho + v.delta)
    hyps.append(v.N >= n_at_least)
    if claim == 'G_J1':
        hyps += [v.zr_s0 >= 0, v.z_zr >= 0, v.z_s0 >= 0, v.x_z >= 0, v.x_s0 >= 0, v.zr_s0 <= v.c, v.z_zr <= v.rho,
                 v.z_s0 <= v.z_zr + v.zr_s0, v.x_z <= v.delta, v.x_s0 <= v.x_z + v.z_s0]
        return hyps, v.x_s0 > v.N
    if claim == 'G_J2':
        hyps += [v.y_zr >= 0, v.z_zr >= 0, v.y_z >= 0, v.z_t >= 0, v.y_t >= 0, v.y_zr <= v.c, v.z_zr <= v.rho,
                 v.y_z <= v.y_zr + v.z_zr, v.z_t <= v.delta, v.y_t <= v.y_z + v.z_t]
        return hyps, v.y_t > v.N
    if claim == 'G_bound':
        # N is the largest of finitely many c + rho + delta; here the one that attains it, accepted if N <= L
        hyps.append(v.N == v.c + v.rho + v.delta)
        if variant != 'no_acceptance':
            hyps.append(v.N <= v.L)
        return hyps, v.N > v.L
    raise ValueError(claim)


# ---------------------------------------------------------------------------------------------------------------
# C. the bounded search: a chain on the real line

class Chain:
    """the accounting on a chain v0 - v1 - ... (a 1-complex) with positions on the line, over a backend B (Z3 terms
    or fractions). Carries the reference rule (with the mutants' flags) and, as a shadow, the apex rule of today
    (drift_apex.py: M over the vertices of R and A, the apex gets M + delta, without an apex every vertex of R, A)."""

    def __init__(self, B, start, mutant=None):
        if mutant is not None and mutant not in MUTANTS:
            raise ValueError(mutant)
        spec()
        self.B, self.mutant = B, mutant
        self.start = list(start)
        self.verts = list(range(len(start)))
        self.pos = dict(enumerate(start))
        # a segment: its two ends, the anchor of each end, the carried bound
        self.segs = [dict(ends=[i, i + 1], anc=[start[i], start[i + 1]], c=B.const(Fraction(0)))
                     for i in range(len(start) - 1)]
        self.ghosts = []                        # dict(rep, anc=[a0, a1], c)
        self.d = {i: B.const(Fraction(0)) for i in self.verts}     # the apex rule's drifts
        self.accepted_ref, self.accepted_apex = [], []             # per edit: the bound each rule compares with L
        self.side = []                          # side conditions of the script (eg the parameter of a removal)

    # the bounds
    def bound(self, s):
        B = self.B
        return s['c'] + _max(B, [_abs(B, self.pos[s['ends'][k]] - s['anc'][k]) for k in range(2)])

    def ghost_bound(self, g):
        B = self.B
        return g['c'] + _max(B, [_abs(B, self.pos[g['rep']] - a) for a in g['anc']])

    def segs_at(self, v):
        return [s for s in self.segs if v in s['ends']]

    def _apex_charge(self, R_verts, A_segs, delta):
        B = self.B
        m_set = list(dict.fromkeys(R_verts + [x for s in A_segs for x in s]))
        M = _max(B, [self.d[x] for x in m_set])
        apexes = [x for x in A_segs[0] if all(x in s for s in A_segs)] if A_segs else []
        charged = [apexes[0]] if apexes else m_set
        for x in charged:
            self.d[x] = M + delta
        self.accepted_apex.append(M + delta)

    # the edits
    def move(self, v, q):
        B = self.B
        delta = _abs(B, q - self.pos[v])
        at = self.segs_at(v)
        self._apex_charge([x for s in at for x in s['ends']], [tuple(s['ends']) for s in at], delta)
        self.pos[v] = q
        changed = [self.bound(s) for s in at]
        if self.mutant != 'move_skips_ghosts':
            changed += [self.ghost_bound(g) for g in self.ghosts if g['rep'] == v]
        self.accepted_ref.append(_max(B, changed))

    def contract(self, a, b, q=None):
        """a vanishes into b; b stays (q None: try_collapse with p = pos(b), R the segments of a) or moves to q (R
        the segments of a and of b)"""
        B = self.B
        i, j = self.verts.index(a), self.verts.index(b)
        if abs(i - j) != 1 or len(self.verts) < 3:
            raise ValueError('contract: adjacent vertices of a chain of at least three')
        target = self.pos[b] if q is None else q
        delta = _max(B, [_abs(B, target - self.pos[a]), _abs(B, target - self.pos[b])])
        collapsing = next(s for s in self.segs if set(s['ends']) == {a, b})
        others_a = [s for s in self.segs_at(a) if s is not collapsing]
        others_b = [s for s in self.segs_at(b) if s is not collapsing]
        R = [collapsing] + others_a + (others_b if q is not None else [])
        A = [tuple(b if x == a else x for x in s['ends']) for s in others_a]
        A += [tuple(s['ends']) for s in others_b] if q is not None else []
        self._apex_charge([x for s in R for x in s['ends']], A, delta)
        # the reference rule: rename a to b, the collapsing segment becomes a ghost at b, the ghosts of a move to b
        for s in others_a:
            k = s['ends'].index(a)
            s['ends'][k] = b
            if self.mutant == 'kept_vertex_anchor':
                s['anc'][k] = self.pos[b]
        self.segs.remove(collapsing)
        # (what the edit changes: the renamed segments, the new ghost, the ghosts of a; if b moves, all at b)
        touched_ghosts = [g for g in self.ghosts if g['rep'] == b] if q is not None else []
        if self.mutant != 'no_ghosts':
            self.ghosts.append(dict(rep=b, anc=list(collapsing['anc']), c=collapsing['c']))
            touched_ghosts.append(self.ghosts[-1])
        for g in list(self.ghosts):
            if g['rep'] == a:
                if self.mutant == 'ghost_dies_with_its_vertex':
                    self.ghosts.remove(g)
                else:
                    g['rep'] = b
                    touched_ghosts.append(g)
        self.verts.remove(a)
        del self.pos[a], self.d[a]
        self.pos[b] = target
        changed = [self.bound(s) for s in others_a + (others_b if q is not None else [])]
        changed += [self.ghost_bound(g) for g in touched_ghosts]
        self.accepted_ref.append(_max(B, changed) if changed else B.const(Fraction(0)))

    def _generic(self, R, A_ends, delta, lost):
        B = self.B
        carry_only = self.mutant == 'generic_carry_only'
        terms = [(s['c'] if carry_only else self.bound(s)) for s in R]
        if self.mutant != 'generic_ignores_lost_ghosts':
            terms += [self.ghost_bound(g) for g in lost]
        N = _max(B, terms) + (B.const(Fraction(0)) if self.mutant == 'generic_without_delta' else delta)
        where = self.segs.index(R[0])
        for s in R:
            self.segs.remove(s)
        for g in lost:
            self.ghosts.remove(g)
        for k, ends in enumerate(A_ends):
            self.segs.insert(where + k, dict(ends=list(ends), anc=[self.pos[x] for x in ends], c=N))
        self.accepted_ref.append(N)

    def remove(self, v, f):
        """the generic edit of the chain: an inner vertex v with neighbours u, w is removed, the segments u-v, v-w
        replaced by u-w. With v moved to a point f between pos(u) and pos(w), the old segments tile the new one,
        so delta = |pos(v) - f| is a two-sided bound ((E1), (E2)); the code's analogue takes the nearest f. (f is a
        variable of its own, not pos(u) + t (pos(w) - pos(u)): the search stays linear.)"""
        B = self.B
        i = self.verts.index(v)
        if i == 0 or i == len(self.verts) - 1:
            raise ValueError('remove: an inner vertex')
        u, w = self.verts[i - 1], self.verts[i + 1]
        self.side += [f >= _min(B, [self.pos[u], self.pos[w]]), f <= _max(B, [self.pos[u], self.pos[w]])]
        delta = _abs(B, self.pos[v] - f)
        R = [s for s in self.segs if v in s['ends']]
        self._apex_charge([x for s in R for x in s['ends']], [(u, w)], delta)
        lost = [g for g in self.ghosts if g['rep'] == v]
        self._generic(R, [(u, w)], delta, lost)
        self.verts.remove(v)
        del self.pos[v], self.d[v]

    def reanchor(self, v):
        """a generic edit that changes no point set (as a flip of a flat convex quad, delta = 0): the segments at v
        are removed and added again; no ghost is lost (v and its segments' ends stay)"""
        at = self.segs_at(v)
        zero = self.B.const(Fraction(0))
        self._apex_charge([x for s in at for x in s['ends']], [tuple(s['ends']) for s in at], zero)
        self._generic(at, [tuple(s['ends']) for s in at], zero, [])

    # the claims
    def all_bounds(self):
        return [self.bound(s) for s in self.segs] + [self.ghost_bound(g) for g in self.ghosts]

    def hull0(self):
        return _min(self.B, self.start), _max(self.B, self.start)

    def hull(self):
        ps = [self.pos[x] for x in self.verts]
        return _min(self.B, ps), _max(self.B, ps)

    def bad(self, claim, x=None, y=None):
        """the negation of a claim, as a formula over the backend.
        'H1': some segment has a point x (between its ends) farther than its bound from S0 (the start's hull: a
              chain on the line covers the interval between its extreme vertices);
        'H2': some point y of S0 is farther than the largest bound from S;
        'dominated': some bound exceeds the apex rule's drift there (a segment: the larger drift of its ends; a
              ghost: the largest such drift of a segment at its vertex, the apex rule's witness for its points), or
              the last edit's bound exceeds the apex rule's M + delta;
        'reported': some bound at the end exceeds the largest value an edit of the script compared with L (MaxDrift:
              every bound an edit changes is evaluated by it)."""
        B = self.B
        lo0, hi0 = self.hull0()
        if claim == 'H1':
            out = []
            for s in self.segs:
                p, q = (self.pos[e] for e in s['ends'])
                dist = _max(B, [B.const(Fraction(0)), lo0 - x, x - hi0])
                out.append(B.And(x >= _min(B, [p, q]), x <= _max(B, [p, q]), dist > self.bound(s)))
            return B.Or(*out)
        if claim == 'H2':
            lo, hi = self.hull()
            most = _max(B, self.all_bounds())
            dist = _max(B, [B.const(Fraction(0)), lo - y, y - hi])
            return B.And(y >= lo0, y <= hi0, dist > most)
        if claim == 'reported':
            return _max(B, self.all_bounds()) > _max(B, self.accepted_ref) if self.accepted_ref else B.Or(B.const(Fraction(0)) > B.const(Fraction(0)))
        if claim == 'dominated':
            out = [self.bound(s) > _max(B, [self.d[x] for x in s['ends']]) for s in self.segs]
            out += [self.ghost_bound(g) > _max(B, [self.d[x] for s in self.segs_at(g['rep']) for x in s['ends']])
                    for g in self.ghosts]
            if self.accepted_ref:
                out.append(self.accepted_ref[-1] > self.accepted_apex[-1])
            return B.Or(*out)
        raise ValueError(claim)


def scripts(n, length):
    """every script of exactly `length` edits on a chain of n vertices, as tuples of (kind, vertices); the vertex
    names are those of the start (0 .. n-1), the chain's order is kept"""
    def extend(verts, k):
        if k == 0:
            yield ()
            return
        steps = [('move', v) for v in verts] + [('reanchor', v) for v in verts]
        if len(verts) >= 3:
            for i in range(len(verts) - 1):
                a, b = verts[i], verts[i + 1]
                steps += [('contract', a, b), ('contract', b, a), ('contract_moving', a, b), ('contract_moving', b, a)]
            steps += [('remove', v) for v in verts[1:-1]]
        for st in steps:
            rest = [v for v in verts if not (st[0] in ('contract', 'contract_moving', 'remove') and v == st[1])]
            for tail in extend(rest, k - 1):
                yield (st,) + tail
    yield from extend(list(range(n)), length)


def run_script(B, n, script, values, mutant=None):
    """the chain after the script; values: p0..p(n-1), and per edit k its parameter q{k} (the new position of a
    move or of the kept vertex, or the point f of a removal)"""
    chain = Chain(B, [values[f'p{i}'] for i in range(n)], mutant)
    for k, st in enumerate(script):
        if st[0] == 'move':
            chain.move(st[1], values[f'q{k}'])
        elif st[0] == 'reanchor':
            chain.reanchor(st[1])
        elif st[0] == 'contract':
            chain.contract(st[1], st[2])
        elif st[0] == 'contract_moving':
            chain.contract(st[1], st[2], values[f'q{k}'])
        elif st[0] == 'remove':
            chain.remove(st[1], values[f'q{k}'])
        else:
            raise ValueError(st)
    return chain


def script_vars(n, script):
    names = [f'p{i}' for i in range(n)] + [f'q{k}' for k in range(len(script))] + ['x', 'y']
    return Vars({x: z.Real(x) for x in names})


def script_formula(n, script, claim, mutant=None):
    def f(B, v):
        chain = run_script(B, n, script, v, mutant)
        return list(chain.side), chain.bad(claim, x=v['x'], y=v['y'])
    return f


def hausdorff_on_the_line(n, script, values, mutant=None):
    """for the replay: (true Hausdorff distance of the chain after the script to the start, largest bound of the
    rule, largest bound of the apex rule), with fractions. Both sets are intervals, so the distance is the larger
    move of an end of the hull."""
    chain = run_script(QB, n, script, values, mutant)
    (lo0, hi0), (lo, hi) = chain.hull0(), chain.hull()
    return max(abs(lo - lo0), abs(hi - hi0)), max(chain.all_bounds()), max(chain.d.values())


# the configurations of the search: (vertices, script length)
SEARCH = [(3, 1), (3, 2), (3, 3), (4, 1), (4, 2), (4, 3), (5, 1), (5, 2)]
CLAIMS = ('H1', 'H2', 'dominated', 'reported')

# per mutant a script on which it has a counterexample (found with search() on the smallest configurations first,
# 2026-11-09; the lost ghost by hand: a ghost is lost only at an inner vertex that both segments of a start vertex
# collapsed onto, which needs five vertices and three edits, beyond SEARCH). run() asks Z3 for a witness on these
# scripts again; the witnesses themselves are pinned in test_drift_reference.py
MUTANT_SCRIPTS = {
    'no_ghosts': (3, (('contract', 0, 1),)),
    'ghost_dies_with_its_vertex': (4, (('contract', 0, 1), ('contract', 1, 2))),
    'generic_carry_only': (3, (('move', 0), ('reanchor', 0))),
    'generic_ignores_lost_ghosts': (5, (('contract', 2, 1), ('contract', 1, 3), ('remove', 3))),
    'generic_without_delta': (3, (('remove', 1),)),
    'kept_vertex_anchor': (3, (('move', 0), ('contract', 1, 0))),
    'move_skips_ghosts': (3, (('contract', 0, 1), ('move', 1))),
}


# ---------------------------------------------------------------------------------------------------------------
# D. the rounding

def distance_formulas():
    """(name, variables, formula): Vector3d.Distance computes sqrt(dx dx + dy dy + dz dz) with dx = fl(x2 - x1), ...
    (math/Vector3d.cs). Model: Higham (2.4), fl(a op b) = (a op b)(1 + e), |e| <= u, no underflow or overflow.
    With k = 1 - u: each computed square is at least the true one times k^3; the computed sum at least the true sum
    times k^5; the computed root r satisfies r^2 >= R^2 k^7. As k^-7 <= (1 + u)^8 (a fact of two fractions, checked
    in the test), the true distance R <= r (1 + u)^4."""
    u = lambda B: B.const(U)
    k = lambda B: B.const(1 - U)

    def square(B, v):
        dd = v.D * (1 + v.a)
        sq = dd * dd * (1 + v.b)
        hyps = [-u(B) <= v.a, v.a <= u(B), -u(B) <= v.b, v.b <= u(B)]
        return hyps, sq < v.D * v.D * B.const((1 - U) ** 3)

    def total(B, v):
        k3 = B.const((1 - U) ** 3)
        s1 = (v.X + v.Y) * (1 + v.e1)
        s2 = (s1 + v.Z) * (1 + v.e2)
        hyps = [v.x >= 0, v.y >= 0, v.zz >= 0, v.X >= v.x * k3, v.Y >= v.y * k3, v.Z >= v.zz * k3,
                -u(B) <= v.e1, v.e1 <= u(B), -u(B) <= v.e2, v.e2 <= u(B)]
        return hyps, s2 < (v.x + v.y + v.zz) * B.const((1 - U) ** 5)

    def root(B, v):
        k5 = B.const((1 - U) ** 5)
        hyps = [v.R >= 0, v.t >= 0, v.s >= v.R * v.R * k5, v.t * v.t == v.s, -u(B) <= v.e3, v.e3 <= u(B),
                v.r == v.t * (1 + v.e3)]
        return hyps, v.r * v.r < v.R * v.R * B.const((1 - U) ** 7)

    yield 'distance_square', 'D a b', square
    yield 'distance_sum', 'x y zz X Y Z e1 e2', total
    yield 'distance_root', 'R t s r e3', root


CHAIN_VARS = 'c ch rho rhoh delta deltah eps eps0 g k eta1 eta2'


def chain_formula(B, v, model='2.5'):
    """one generic edit, k -> k + 1 (k the number of generic edits behind the carried bound; displacement edits do
    not count: their distances are computed anew from the anchors). True carried bound c <= ch g + k e with
    g = (1 + u)^(6 k) >= 1 and e = eps + eps0; the true corner distance rho <= rhoh (1 + u)^4 + eps0 (D, eps0 the
    underflow term); the true bound of the edit delta <= deltah + eps; the stored sums Bh = fl(ch + rhoh),
    Nh = fl(Bh + deltah), rounded as Higham (2.5), fl(s) = s / (1 + eta). Claim: c + rho + delta <= Nh g (1 + u)^6
    + (k + 1) e. With the other model, (2.4), the claim fails (as for the apex rule's chain)."""
    u = B.const(U)
    w = B.const((1 + U) ** 4)
    e = v.eps + v.eps0
    if model == '2.5':
        Bh = (v.ch + v.rhoh) / (1 + v.eta1)
        Nh = (Bh + v.deltah) / (1 + v.eta2)
    else:
        Bh = (v.ch + v.rhoh) * (1 + v.eta1)
        Nh = (Bh + v.deltah) * (1 + v.eta2)
    hyps = [v.ch >= 0, v.rhoh >= 0, v.deltah >= 0, v.eps >= 0, v.eps0 >= 0, v.g >= 1, v.k >= 0, v.c >= 0, v.rho >= 0,
            v.delta >= 0, v.c <= v.ch * v.g + v.k * e, v.rho <= v.rhoh * w + v.eps0, v.delta <= v.deltah + v.eps,
            -u <= v.eta1, v.eta1 <= u, -u <= v.eta2, v.eta2 <= u]
    return hyps, v.c + v.rho + v.delta > Nh * v.g * B.const((1 + U) ** 6) + (v.k + 1) * e


# ---------------------------------------------------------------------------------------------------------------

def _replay(model, v, formula):
    vals = {}
    for k, var in v.items():
        val = model.eval(var, model_completion=True)
        if not z.is_rational_value(val):
            return dict(hyps=False, bad=False, values={}, reason=f'{k} is not rational')
        vals[k] = val.as_fraction()
    hyps, bad = formula(QB, Vars(vals))
    return dict(hyps=all(hyps), bad=bool(bad), values={k: str(x) for k, x in vals.items()})


def _prove(f, v, timeout_ms, replay=True):
    """(result, seconds, assumptions' result, assumptions replayed)"""
    hyps, bad = f(ZB, v)
    _, r, secs = _check(hyps, bad, timeout_ms)
    sol, rr, _ = _check(hyps, z.BoolVal(True), timeout_ms)
    replayed = rr == z.sat and (not replay or _replay(sol.model(), v, f)['hyps'])
    return r, secs, rr, replayed


def _refute(f, v, timeout_ms):
    hyps, bad = f(ZB, v)
    sol, r, secs = _check(hyps, bad, timeout_ms)
    row = dict(result=str(r), seconds=round(secs, 3), replayed=False)
    if r == z.sat:
        rep = _replay(sol.model(), v, f)
        row.update(replayed=rep['hyps'] and rep['bad'], witness=rep['values'])
    return row


def _real_vars(names):
    return Vars({n: z.Real(n) for n in (names.split() if isinstance(names, str) else names)})


def search(mutant, timeout_ms, configurations=None, scripts_of=None):
    """the first script (in the order of SEARCH and scripts(), or of the given (vertices, script) list) and claim
    for which the mutant has a counterexample with a replayed witness, or None"""
    todo = scripts_of if scripts_of is not None else (
        (n, script) for n, length in (configurations or SEARCH) for script in scripts(n, length))
    for n, script in todo:
        if True:
            for claim in ('H1', 'H2', 'reported'):
                v = script_vars(n, script)
                row = _refute(script_formula(n, script, claim, mutant), v, timeout_ms)
                if row['result'] == 'sat' and row['replayed']:
                    return dict(mutant=mutant, vertices=n, script=[list(st) for st in script], claim=claim, **row)
    return None


def run(timeout_ms=120000, configurations=None):
    rows, muts = [], []
    # A
    for name, names, f in (('convexity_dot', DOT_VARS, dot_formula),
                           ('convexity_triangle', GRAM_VARS, lambda B, w: convexity_formula(B, w))):
        r, t, rr, rep = _prove(f, _real_vars(names), timeout_ms)
        rows.append(dict(part='A', name=name, result=str(r), seconds=round(t, 3), assumptions_result=str(rr),
                         assumptions_replayed=rep))
    for variant in ('no_sum', 'two_corners', 'no_dot_bound'):
        f = (lambda variant: lambda B, w: convexity_formula(B, w, variant))(variant)
        muts.append(dict(part='A', name=f'convexity_{variant}', **_refute(f, _real_vars(GRAM_VARS), timeout_ms)))
    # B
    for claim in ('D_I1', 'D_I2', 'G_J1', 'G_J2', 'G_bound'):
        f = (lambda claim: lambda B, w: lemma_formula(B, w, claim))(claim)
        r, t, rr, rep = _prove(f, _real_vars(LEMMA_VARS), timeout_ms)
        rows.append(dict(part='B', name=f'lemma_{claim}', result=str(r), seconds=round(t, 3),
                         assumptions_result=str(rr), assumptions_replayed=rep))
    for claim, variant in (('G_J1', 'carry_only'), ('G_J2', 'carry_only'), ('G_J1', 'no_delta'), ('G_J2', 'no_delta'),
                           ('G_bound', 'no_acceptance')):
        f = (lambda claim, variant: lambda B, w: lemma_formula(B, w, claim, variant))(claim, variant)
        muts.append(dict(part='B', name=f'lemma_{claim}_{variant}', **_refute(f, _real_vars(LEMMA_VARS), timeout_ms)))
    # C: every script, every claim
    counts = []
    for n, length in (configurations or SEARCH):
        number, secs, worst, assumed = 0, 0.0, z.unsat, True
        for script in scripts(n, length):
            v = script_vars(n, script)
            # one query per script: some claim fails (the three claims share the chain)
            chain = run_script(ZB, n, script, v)
            hyps = list(chain.side)
            bad = z.Or(*[chain.bad(claim, x=v['x'], y=v['y']) for claim in CLAIMS])
            _, r, t = _check(hyps, bad, timeout_ms)
            secs += t
            number += 1
            if r != z.unsat:
                if worst == z.unsat:
                    worst = r
                which = [c for c in CLAIMS if _check(hyps, chain.bad(c, x=v['x'], y=v['y']), timeout_ms)[1] != z.unsat]
                counts.append(dict(vertices=n, script=[list(st) for st in script], claims=which, result=str(r)))
            # the script's side conditions are satisfiable (the claims are not vacuous)
            assumed = assumed and _check(hyps, z.BoolVal(True), timeout_ms)[1] == z.sat
        rows.append(dict(part='C', name=f'chain_{n}_vertices_{length}_edits', scripts=number, result=str(worst),
                         seconds=round(secs, 3), assumptions_result='sat' if assumed else 'unsat or unknown',
                         assumptions_replayed=assumed))
    for mutant in MUTANTS:
        n, script = MUTANT_SCRIPTS[mutant]
        found = search(mutant, timeout_ms, scripts_of=[(n, script)])
        muts.append(dict(part='C', name=mutant, **(found or dict(result='no counterexample', replayed=False))))
    # D
    for name, names, f in distance_formulas():
        r, t, rr, rep = _prove(f, _real_vars(names), timeout_ms)
        rows.append(dict(part='D', name=name, result=str(r), seconds=round(t, 3), assumptions_result=str(rr),
                         assumptions_replayed=rep))
    r, t, rr, rep = _prove(lambda B, w: chain_formula(B, w, '2.5'), _real_vars(CHAIN_VARS), timeout_ms)
    rows.append(dict(part='D', name='chain_step_higham_2_5', result=str(r), seconds=round(t, 3),
                     assumptions_result=str(rr), assumptions_replayed=rep))
    muts.append(dict(part='D', name='chain_step_with_2_4_model_only',
                     **_refute(lambda B, w: chain_formula(B, w, '2.4'), _real_vars(CHAIN_VARS), timeout_ms)))
    passed = (all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' and r['assumptions_replayed']
                  for r in rows)
              and all(m['result'] == 'sat' and m['replayed'] for m in muts))
    return dict(z3=z.get_version_string(), timeout_ms=timeout_ms, specification=dict(RULE), obligations=rows,
                failures=counts, mutants=muts, total_seconds=round(sum(r['seconds'] for r in rows), 3),
                obligations_passed=passed,
                proof_scope='A: convexity in R^3. B: one edit, shape-free, distances as real variables. C: chains of '
                            '3-5 vertices on the real line, every script of up to 3 edits (2 for 5 vertices), every '
                            'real position. D: the computed point distance and one generic edit of the chain bound. '
                            'Review only: (E1)/(E2) per edit type, which triangles an edit touches, lost ghosts on '
                            'edges (a 1-complex has ghosts at vertices only), the induction over edits.')


def _method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 0
    for end in range(opening, len(text)):
        depth += (text[end] == '{') - (text[end] == '}')
        if depth == 0:
            return text[start:end + 1]
    raise ValueError('unterminated method ' + signature)


def source_contract():
    """The facts about today's code that the note's lemma assumes (section 3.4), so that a change of them is seen
    before the rule is built on them. Not a contract of the rule itself: no code implements it yet."""
    text = (ROOT / 'mesh_ops/MeshRoundingRepair.cs').read_text(encoding='utf-8-sig')
    norm = lambda x: re.sub(r'\s+', '', re.sub(r'//[^\n]*', '', x))
    # one place writes positions
    if text.count('Mesh.SetVertex(') != 1 or 'Mesh.SetVertex(v, p);' not in _method(text, 'void set_vertex('):
        raise ValueError('positions are written outside set_vertex; review drift_reference.py')
    # the displacement edits are try_move and try_collapse: only they propose a moved vertex
    proposals = re.findall(r'movedV = (\w+); movedPos = (\w+);( vanishing = \w+;)?', text)
    if sorted(proposals) != sorted([('b', 'p', ' vanishing = a;'), ('v', 'p', ''), ('saveV', 'savePos', ''),
                                    ('saveV', 'savePos', '')]):
        raise ValueError('an edit other than try_move and try_collapse proposes a moved vertex; review '
                         'drift_reference.py')
    # a move removes and re-adds every triangle of the moved vertex, corner for corner
    move = norm(_method(text, 'bool try_move('))
    if norm("""foreach (int t in Mesh.VtxTrianglesItr(v)) {
                removed.Add(t);
                added.Add(Mesh.GetTriangle(t));
                addedFrom.Add(removed.Count - 1);
            }""") not in move:
        raise ValueError('try_move: the triangles of the moved vertex changed; review drift_reference.py')
    # a contraction: every triangle of a, renamed corner for corner unless it holds b; every triangle of b if b moves
    collapse = norm(_method(text, 'bool try_collapse('))
    if norm("""foreach (int t in Mesh.VtxTrianglesItr(a)) {
                removed.Add(t);
                Index3i tv = Mesh.GetTriangle(t);
                if (tv.a == b || tv.b == b || tv.c == b)
                    continue;
                added.Add(new Index3i(tv.a == a ? b : tv.a, tv.b == a ? b : tv.b, tv.c == a ? b : tv.c));
                addedFrom.Add(removed.Count - 1);
            }
            if (p != Mesh.GetVertex(b)) {
                foreach (int t in Mesh.VtxTrianglesItr(b)) {
                    Index3i tv = Mesh.GetTriangle(t);
                    if (tv.a == a || tv.b == a || tv.c == a)
                        continue;
                    removed.Add(t);
                    added.Add(tv);
                    addedFrom.Add(removed.Count - 1);
                }
            }""") not in collapse:
        raise ValueError('try_collapse: the triangles of the contraction changed; review drift_reference.py')
    # the distance of two points, as modelled in D
    vec = (ROOT / 'math/Vector3d.cs').read_text(encoding='utf-8-sig')
    if norm("""public double Distance(Vector3d v2) {
            double dx = v2.x-x, dy = v2.y-y, dz = v2.z-z;
			return Math.Sqrt(dx*dx + dy*dy + dz*dz);
		}""") not in norm(vec):
        raise ValueError('Vector3d.Distance changed; review the rounding model of drift_reference.py')


if __name__ == '__main__':
    import argparse
    import json
    parser = argparse.ArgumentParser()
    parser.add_argument('--timeout-ms', type=int, default=120000)
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    report = run(args.timeout_ms)
    with open(args.out, 'w') as fh:
        fh.write(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: report[k] for k in ('obligations_passed', 'total_seconds')}))
    raise SystemExit(0 if report['obligations_passed'] else 1)
