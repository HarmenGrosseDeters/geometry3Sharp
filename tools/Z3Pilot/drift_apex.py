"""Z3 pilot 2, step 1: the D1 drift lemma of SafeRounding (docs/design/ROUNDING_DRIFT_APEX.md, corrected in
fd931c69), checked before the rule is built.

An edit removes the triangles R, adds A and moves the surface by at most delta. Every vertex has a drift d (0 at the
start); M is the largest drift over the vertices of R and A; the edit is accepted if M + delta <= maxLength (L).
The apex rule charges M + delta to one vertex c that lies in every triangle of A (an apex) and keeps every other
drift; an edit without an apex keeps today's rule (every vertex of the edit gets M + delta). Which edits have an
apex is decided by the triangles the code adds, not by the edit's name: zips add four triangles without a common
vertex (no apex); a strip zip (try_strip) re-adds the outer triangle at every loop edge, split at the chain
vertices: for a loop of three vertices e0, m, e1 every added triangle contains the middle chain vertex m (the
apex), for longer loops there is none (a common tip of all outer triangles is rejected by the code); an ear
contraction (try_collapse at a vertex with a single triangle) adds none, as collapse_keeps_boundary never moves the
kept boundary vertex (A empty, no apex).
The invariant, with D(T) the largest drift of the corners of T and S0 the surface at the start:
  (I1) every point x of a triangle T of S has dist(x, S0) <= D(T);
  (I2) every point y of S0 has a triangle T of S with dist(y, T) <= D(T).
The lemma: one edit that satisfies the two-sided bounds (E1) / (E2) keeps I, and d' >= d, and max d' <= L.

CHARGING below is the specification: the obligations are derived from it (edit_formula, obligations), so a change
of the specification is checked by Z3; source_contract() checks the code against it.

Model (README, section 'Pilot 2, step 1'): the distances are real variables, the geometric facts are hypotheses (the
witness points of (E1), (E2) and of I before the edit, and the triangle inequality); the combinatorics are fixed,
illustrative shapes, plus a shape-free version that uses only 'the apex is in every triangle of A' and 'M is over
R and A'. The edit obligations are linear real arithmetic; the chain step of the rounding is nonlinear (products
with g and eta). The stored drift is rounded with Higham (2.5), fl(s) = s / (1 + eta), |eta| <= u (valid for round to
nearest); the weaker (2.4) model fl(s) = s (1 + eta) cannot prove the chain bound and is kept as a SAT witness.

Every obligation must be UNSAT, its assumptions SAT with delta > 0 (the model replayed with fractions), every mutant
SAT with a witness replayed with fractions outside Z3; UNKNOWN never counts.

source_contract() checks the code against CHARGING: the drift accounting of mesh_ops/MeshRoundingRepair.cs
(improves: M over the vertices of R and A and the apex of A; within_bound: accepted if M + delta <= maxLength;
apex_of: a vertex of every added triangle; commit_drift: only the apex gets M + delta, else every vertex of R and A).
The contract compares the text of these methods; the check-mode blocks in improves and commit_drift (between the parts
it compares) are not covered by it: they are tested by the C# check-mode tests (RoundingDriftApexTests).
"""
import re
from fractions import Fraction

import z3 as z

from filter_rules import QB, ZB, Vars, _check, ROOT

U = Fraction(1, 2**53)          # unit roundoff of binary64 round to nearest

# the specification of the charging (ROUNDING_DRIFT_APEX.md section 2): with an apex (a vertex in every added
# triangle) only that vertex gets M + delta; without one, every vertex of R and A; M over the vertices of R and A;
# accepted if M + delta <= maxLength
CHARGING = dict(with_apex='apex', without_apex='all', M_over='R and A', acceptance='M + delta <= L')


def spec():
    """CHARGING as the parameters of edit_formula; raises on a specification the lemma does not cover"""
    with_apex, without_apex = CHARGING['with_apex'], CHARGING['without_apex']
    if with_apex not in ('apex', 'all'):
        raise ValueError(f"CHARGING['with_apex'] = {with_apex!r}: 'apex' (a vertex in every added triangle) or 'all'")
    if without_apex != 'all':
        raise ValueError(f"CHARGING['without_apex'] = {without_apex!r}: an edit without an apex must charge 'all'")
    m_over = {'R and A': 'RA', 'A': 'A', 'R': 'R'}.get(CHARGING['M_over'])
    if m_over is None:
        raise ValueError(f"CHARGING['M_over'] = {CHARGING['M_over']!r}")
    if CHARGING['acceptance'] != 'M + delta <= L':
        raise ValueError(f"CHARGING['acceptance'] = {CHARGING['acceptance']!r}")
    return with_apex, without_apex, m_over


def _if(B, c, a, b):
    return z.If(c, a, b) if B is ZB else (a if c else b)


def _max(B, xs):
    m = xs[0]
    for x in xs[1:]:
        m = _if(B, x > m, x, m)
    return m


# ---------------------------------------------------------------------------------------------------------------
# edit shapes: R (removed), A (added), for A empty the untouched triangles that hold the counterpart simplex of (E2).
# Illustrative: the proof uses only 'apex in every triangle of A' and 'M over R and A' (see generic_formula).

def apexes(shape):
    """the vertices in every added triangle, in the order of A[0] (the code takes the first)"""
    if not shape['A']:
        return []
    return [x for x in shape['A'][0] if all(x in t for t in shape['A'])]


def move(k):
    """a move or slide of v with valence k (closed fan): the same triangles, v at a new position"""
    ring = [f'r{i}' for i in range(k)]
    tris = [('v', ring[i], ring[(i + 1) % k]) for i in range(k)]
    return dict(name=f'move_valence_{k}', R=tris, A=tris, U=[])


def contraction(m, n):
    """contraction of a-b (try_collapse), a vanishes, b kept and moved: m + 1 triangles of a and n + 1 of b besides
    the two at the edge (l and r the opposite vertices); A: a's triangles with a replaced by b, and b's again"""
    p = ['l'] + [f'p{i}' for i in range(m)] + ['r']
    q = ['r'] + [f'q{i}' for i in range(n)] + ['l']
    a_side = [('a', p[i], p[i + 1]) for i in range(m + 1)]
    b_side = [('b', q[i], q[i + 1]) for i in range(n + 1)]
    R = a_side + b_side + [('a', 'b', 'l'), ('b', 'a', 'r')]
    A = [('b', x, y) for _, x, y in a_side] + b_side
    return dict(name=f'contraction_{m + 1}_{n + 1}', R=R, A=A, U=[])


def removal():
    """removal of v of valence 3 (ring a, b, c): one new triangle"""
    return dict(name='removal_valence_3', R=[('v', 'a', 'b'), ('v', 'b', 'c'), ('v', 'c', 'a')], A=[('a', 'b', 'c')],
                U=[])


def flip():
    """flip of a-b to c-d"""
    return dict(name='flip', R=[('a', 'b', 'c'), ('b', 'a', 'd')], A=[('c', 'a', 'd'), ('d', 'b', 'c')], U=[])


def strip_loop3(inner=False):
    """try_strip with a loop of three vertices e0, m, e1 (m the middle chain vertex): the strip (a sliver, or with an
    inner vertex i three triangles) and the three outer triangles are removed; the outer triangles at e0-m and m-e1
    come back unchanged, the one at e1-e0 split at m: all four added triangles contain m (the apex)"""
    strip = [('e0', 'm', 'i'), ('m', 'e1', 'i'), ('e1', 'e0', 'i')] if inner else [('e0', 'm', 'e1')]
    outer = [('m', 'e0', 'x0'), ('e1', 'm', 'x1'), ('e0', 'e1', 'x2')]
    A = [('m', 'e0', 'x0'), ('e1', 'm', 'x1'), ('m', 'e1', 'x2'), ('e0', 'm', 'x2')]
    return dict(name='strip_loop3_inner' if inner else 'strip_loop3', R=strip + outer, A=A, U=[])


def strip_loop4():
    """try_strip with a loop of four vertices (chains e0, m1, e1 and e0, m2, e1; merged order e0, m1, m2, e1): the
    outer triangles come back split at the chain vertices; no vertex is in all of them, so today's rule"""
    strip = [('e0', 'm1', 'e1'), ('e0', 'e1', 'm2')]
    outer = [('m1', 'e0', 'x0'), ('e1', 'm1', 'x1'), ('m2', 'e1', 'x2'), ('e0', 'm2', 'x3')]
    A = [('m1', 'e0', 'x0'), ('m2', 'm1', 'x1'), ('e1', 'm2', 'x1'), ('m2', 'e1', 'x2'), ('m1', 'm2', 'x3'),
         ('e0', 'm1', 'x3')]
    return dict(name='strip_loop4', R=strip + outer, A=A, U=[])


def zip_shape():
    """try_zip (c before d along a-b): removes (a, b, c), (b, a, d), (c, b, x), (d, a, y), adds (c, d, x),
    (d, b, x), (d, c, y), (c, a, y): no vertex is in all four, so today's rule"""
    return dict(name='zip', R=[('a', 'b', 'c'), ('b', 'a', 'd'), ('c', 'b', 'x'), ('d', 'a', 'y')],
                A=[('c', 'd', 'x'), ('d', 'b', 'x'), ('d', 'c', 'y'), ('c', 'a', 'y')], U=[])


def ear():
    """contraction of an ear v (single triangle v, a, b) into a (the kept vertex is on the boundary and never moved:
    collapse_keeps_boundary): A empty, its points go to the edge a-b,
    which lies in the untouched triangle a, b, w (w not a vertex of the edit)"""
    return dict(name='ear_in_place', R=[('v', 'a', 'b')], A=[], U=[('a', 'b', 'w')])


def shapes():
    for k in range(3, 9):
        yield move(k)
    for m in range(1, 6):
        for n in range(1, 6):
            yield contraction(m, n)
    yield removal()
    yield flip()
    yield strip_loop3()
    yield strip_loop3(inner=True)
    yield strip_loop4()
    yield zip_shape()
    yield ear()


# ---------------------------------------------------------------------------------------------------------------
# the lemma for one shape

def _verts(shape):
    edit = []
    for t in shape['R'] + shape['A']:
        for x in t:
            if x not in edit:
                edit.append(x)
    others = []
    for t in shape['U']:
        for x in t:
            if x not in edit and x not in others:
                others.append(x)
    return edit, edit + others


def variables(shape):
    _, verts = _verts(shape)
    names = [f'd_{x}' for x in verts] + ['delta', 'M', 'L', 'yz']
    targets = shape['A'] or shape['U']
    names += [f'{p}_{i}' for i in range(len(shape['A'])) for p in ('xz', 'dz', 'dx')]
    names += [f'{p}_{j}' for j in range(len(targets)) for p in ('zt', 'yt')]
    return Vars({n: z.Real(n) for n in names})


def edit_formula(B, v, shape, claim, apex=None, rule=None, m_over=None, only=None, accept=True):
    """(hyps, bad) for one claim: 'I1', 'I2', 'monotone' (d' >= d) or 'bound' (max d' <= L).
    By default the charging follows CHARGING (spec()): with apex given, CHARGING['with_apex']; without,
    CHARGING['without_apex']. Mutants override: rule 'no_delta' (the apex gets M), 'delta_only' (the apex gets
    delta), or a vertex name to charge instead; m_over 'A' (M over the added triangles only); accept=False drops the
    acceptance test. only: for 'I1', the one added triangle to check (the claim is a conjunction over them)."""
    with_apex, without_apex, spec_m_over = spec()
    if rule is None:
        rule = with_apex if apex is not None else without_apex
    m_over = spec_m_over if m_over is None else m_over
    edit, verts = _verts(shape)
    d = {x: v[f'd_{x}'] for x in verts}
    delta, M, L = v.delta, v.M, v.L
    m_set = {'RA': edit, 'A': [x for t in shape['A'] for x in t], 'R': [x for t in shape['R'] for x in t]}[m_over]
    hyps = [d[x] >= 0 for x in verts] + [delta >= 0, L >= 0] + [d[x] <= L for x in verts]
    hyps += [M >= d[x] for x in m_set] + [B.Or(*[M == d[x] for x in m_set])]
    if accept:
        hyps.append(M + delta <= L)
    value = {'no_delta': M, 'delta_only': delta}.get(rule, M + delta)
    if rule == 'apex':
        charged = {apex}
    elif rule == 'all':
        charged = set(edit)
    elif rule in ('no_delta', 'delta_only'):
        charged = {apex}
    else:
        charged = {rule}
    d2 = {x: (value if x in charged else d[x]) for x in verts}

    def D(t, dd):
        return _max(B, [dd[x] for x in t])
    if claim == 'monotone':
        return hyps, B.Or(*[d2[x] < d[x] for x in verts])
    if claim == 'bound':
        return hyps, B.Or(*[d2[x] > L for x in verts])
    if claim == 'I1':
        bad = []
        for i, t2 in enumerate(shape['A']):
            if only is not None and i != only:
                continue
            xz, dz, dx = v[f'xz_{i}'], v[f'dz_{i}'], v[f'dx_{i}']
            # (E1): x within delta of a point z of a removed triangle T; (I1) before at z; the triangle inequality
            hyps += [xz >= 0, dz >= 0, dx >= 0, xz <= delta, B.Or(*[dz <= D(t, d) for t in shape['R']]),
                     dx <= xz + dz]
            bad.append(dx > D(t2, d2))
        return hyps, B.Or(*bad)
    if claim == 'I2':
        # (I2) before: y within D(T) of a point z of a removed triangle T (an untouched T is kept: d' >= d);
        # (E2): z within delta of an added triangle, or for A empty of the counterpart simplex in an untouched one
        targets = shape['A'] or shape['U']
        hyps += [v.yz >= 0, B.Or(*[v.yz <= D(t, d) for t in shape['R']])]
        near, bad = [], []
        for j, t2 in enumerate(targets):
            zt, yt = v[f'zt_{j}'], v[f'yt_{j}']
            hyps += [zt >= 0, yt >= 0, yt <= v.yz + zt]
            near.append(zt <= delta)
            bad.append(yt > D(t2, d2))
        hyps.append(B.Or(*near))
        return hyps, B.And(*bad)
    raise ValueError(claim)


GENERIC_VARS = 'delta M L xz dz dx DT D2 dc dc2 yz zt yt'


def generic_formula(B, v, claim):
    """the shape-free lemma: T a removed triangle (D(T) <= M, as M is over R and A), T' an added triangle that
    contains the apex c (D'(T') >= d'(c)); only these two facts and the charging of the specification are used"""
    with_apex, _, m_over = spec()
    hyps = [v.delta >= 0, v.L >= 0, v.M >= 0, v.dc >= 0, v.M + v.delta <= v.L, v.D2 >= v.dc2, v.dc <= v.L]
    if m_over in ('RA', 'R'):
        hyps.append(v.DT <= v.M)            # every corner of a removed triangle is counted in M
    if m_over in ('RA', 'A'):
        hyps.append(v.dc <= v.M)            # the apex is a corner of an added triangle
    hyps.append(v.dc2 == v.M + v.delta)     # with_apex 'apex' or 'all': c gets M + delta
    if claim == 'I1':
        hyps += [v.xz >= 0, v.dz >= 0, v.xz <= v.delta, v.dz <= v.DT, v.dx <= v.xz + v.dz]
        return hyps, v.dx > v.D2
    if claim == 'I2':
        hyps += [v.yz >= 0, v.zt >= 0, v.yz <= v.DT, v.zt <= v.delta, v.yt <= v.yz + v.zt]
        return hyps, v.yt > v.D2
    if claim == 'monotone':
        return hyps, v.dc2 < v.dc
    if claim == 'bound':
        return hyps, v.dc2 > v.L
    raise ValueError(claim)


def obligations():
    """(label, name, shape, claim, apex): the charging of each comes from CHARGING (edit_formula)"""
    with_apex, _, _ = spec()
    for s in shapes():
        cs = apexes(s)
        choices = (cs if with_apex == 'apex' else [cs[0]]) if cs else [None]
        for c in choices:
            for claim in ('I1', 'I2', 'monotone', 'bound'):
                if claim == 'I1' and not s['A']:
                    continue
                label = 'apex rule' if c is not None else 'without apex'
                tag = f'apex_{c}' if c is not None else 'all'
                yield label, f"{s['name']}_{tag}_{claim}", s, claim, c


def mutants():
    """(label, name, shape, claim, apex, overrides): each must be SAT with a replayed witness"""
    c11 = contraction(1, 1)
    yield 'apex rule', 'contraction_charges_vanishing_a', c11, 'I1', 'b', dict(rule='a')
    yield 'apex rule', 'flip_charges_a_in_one_added_triangle_I1', flip(), 'I1', 'c', dict(rule='a')
    yield 'apex rule', 'flip_charges_a_in_one_added_triangle_I2', flip(), 'I2', 'c', dict(rule='a')
    yield 'apex rule', 'move_charges_ring_vertex', move(3), 'I1', 'v', dict(rule='r0')
    yield 'apex rule', 'move_charges_M_without_delta', move(3), 'I1', 'v', dict(rule='no_delta')
    yield 'apex rule', 'flip_charges_M_without_delta_I2', flip(), 'I2', 'c', dict(rule='no_delta')
    yield 'apex rule', 'contraction_M_over_A_only', c11, 'I1', 'b', dict(m_over='A')
    yield 'apex rule', 'removal_M_over_A_only', removal(), 'I1', 'a', dict(m_over='A')
    yield 'apex rule', 'move_apex_gets_delta_only_monotone', move(3), 'monotone', 'v', dict(rule='delta_only')
    yield 'apex rule', 'move_without_acceptance_bound', move(3), 'bound', 'v', dict(accept=False)
    yield 'without apex', 'zip_charges_d_in_three_of_four', zip_shape(), 'I1', None, dict(rule='d')
    yield 'without apex', 'ear_charges_only_vanishing_v', ear(), 'I2', None, dict(rule='v')


# ---------------------------------------------------------------------------------------------------------------
# the rounding of the stored drift over a chain of k edits

def chain_formula(B, v, model='2.5'):
    """one step k -> k + 1: true drift M_t <= M_h g + k eps (g = (1 + u)^k >= 1), true delta <= delta_h + eps, stored
    d_h' = fl(M_h + delta_h); claim: M_t + delta <= d_h' g (1 + u) + (k + 1) eps (nonlinear: g and eta)"""
    u = B.const(U)
    s = v.M_h + v.delta_h
    stored = s / (1 + v.eta) if model == '2.5' else s * (1 + v.eta)
    hyps = [v.M_h >= 0, v.delta_h >= 0, v.delta >= 0, v.eps >= 0, v.g >= 1, v.k >= 0, v.M_t <= v.M_h * v.g + v.k * v.eps,
            v.delta <= v.delta_h + v.eps, -u <= v.eta, v.eta <= u]
    return hyps, v.M_t + v.delta > stored * v.g * (1 + u) + (v.k + 1) * v.eps


CHAIN_VARS = 'M_t M_h delta delta_h eps g eta k'


def _replay(model, v, formula):
    vals = {}
    for k, var in v.items():
        val = model.eval(var, model_completion=True)
        if not z.is_rational_value(val):
            return dict(hyps=False, bad=False, values={}, reason=f'{k} is not rational')
        vals[k] = val.as_fraction()
    hyps, bad = formula(QB, Vars(vals))
    return dict(hyps=all(hyps), bad=bool(bad), values={k: str(x) for k, x in vals.items() if x != 0})


def _prove(f, v, timeout_ms):
    """(result, seconds, assumptions SAT with delta > 0 and replayed)"""
    hyps, bad = f(ZB, v)
    _, r, secs = _check(hyps, bad, timeout_ms)
    sat_s, rr, _ = _check(hyps + [v.delta > 0], z.BoolVal(True), timeout_ms)
    replayed = rr == z.sat and _replay(sat_s.model(), v, f)['hyps']
    return r, secs, rr, replayed


def _refute(f, v, timeout_ms):
    hyps, bad = f(ZB, v)
    sol, r, secs = _check(hyps, bad, timeout_ms)
    row = dict(result=str(r), seconds=round(secs, 3), replayed=False)
    if r == z.sat:
        rep = _replay(sol.model(), v, f)
        row.update(replayed=rep['hyps'] and rep['bad'], witness=rep['values'])
    return row


def run(timeout_ms=120000):
    rows, muts, partial = [], [], []
    for label, name, s, claim, apex in obligations():
        v = variables(s)
        parts = range(len(s['A'])) if claim == 'I1' else [None]
        results, secs, assumed, replayed = [], 0.0, [], True
        for i in parts:
            f = (lambda s, claim, apex, i: lambda B, w: edit_formula(B, w, s, claim, apex, only=i))(s, claim, apex, i)
            r, t, rr, rep = _prove(f, v, timeout_ms)
            results.append(r)
            secs += t
            assumed.append(rr)
            replayed = replayed and rep
        # one row per claim: UNSAT only if every query is UNSAT (I1: one query per added triangle)
        worst = next((r for r in results if r != z.unsat), z.unsat)
        rows.append(dict(rule=label, name=name, shape=s['name'], claim=claim, queries=len(results), added=len(s['A']),
                         result=str(worst), seconds=round(secs, 3),
                         assumptions_result='sat' if all(a == z.sat for a in assumed) else 'unsat or unknown',
                         assumptions_replayed=replayed))
    v = Vars({n: z.Real(n) for n in GENERIC_VARS.split()})
    for claim in ('I1', 'I2', 'monotone', 'bound'):
        f = (lambda claim: lambda B, w: generic_formula(B, w, claim))(claim)
        r, t, rr, rep = _prove(f, v, timeout_ms)
        rows.append(dict(rule='shape-free', name=f'generic_{claim}', claim=claim, queries=1, added=1, result=str(r),
                         seconds=round(t, 3), assumptions_result=str(rr), assumptions_replayed=rep))
    # 'only the apex is charged' is not vacuous: another vertex of the edit can stay below M + delta
    for s in shapes():
        edit, _ = _verts(s)
        for c in apexes(s):
            def f(B, w, s=s, c=c, edit=edit):
                hyps, _ = edit_formula(B, w, s, 'monotone', c)
                return hyps, B.Or(*[B.And(w[f'd_{x}'] < w.M + w.delta, w.delta > 0) for x in edit if x != c])
            row = _refute(f, variables(s), timeout_ms)
            partial.append(dict(name=f"{s['name']}_apex_{c}", **row))
    for label, name, s, claim, apex, over in mutants():
        v = variables(s)
        f = (lambda s, claim, apex, over: lambda B, w: edit_formula(B, w, s, claim, apex, **over))(s, claim, apex, over)
        muts.append(dict(rule=label, name=name, **_refute(f, v, timeout_ms)))
    # the chain step
    v = Vars({n: z.Real(n) for n in CHAIN_VARS.split()})
    f = lambda B, w: chain_formula(B, w, '2.5')
    r, t, rr, rep = _prove(f, v, timeout_ms)
    rows.append(dict(rule='rounding chain', name='chain_step_higham_2_5', claim='d <= d_h (1 + u)^k + k eps',
                     queries=1, added=0, result=str(r), seconds=round(t, 3), assumptions_result=str(rr),
                     assumptions_replayed=rep))
    f = lambda B, w: chain_formula(B, w, '2.4')
    muts.append(dict(rule='rounding chain', name='chain_step_with_2_4_model_only', **_refute(f, v, timeout_ms)))
    passed = (all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' and r['assumptions_replayed']
                  for r in rows)
              and all(r['queries'] == r['added'] for r in rows if r['claim'] == 'I1' and r['rule'] != 'shape-free')
              and all(p['result'] == 'sat' and p['replayed'] for p in partial)
              and all(m['result'] == 'sat' and m['replayed'] for m in muts))
    return dict(z3=z.get_version_string(), timeout_ms=timeout_ms, specification=dict(CHARGING), obligations=rows,
                partial_charge_nonvacuity=partial, mutants=muts,
                total_seconds=round(sum(r['seconds'] for r in rows + muts), 3), obligations_passed=passed,
                proof_scope='One edit of each illustrative shape and the shape-free version, distances as real '
                            'variables (README); (E1), (E2), the point/triangle distance model, I1/I2 => Hausdorff, the '
                            'induction over edits and the per-vertex chain counter are review-only.')



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
    """Fail on code drift: the drift accounting of MeshRoundingRepair must be CHARGING as proved here."""
    assert CHARGING == dict(with_apex='apex', without_apex='all', M_over='R and A', acceptance='M + delta <= L')
    text = (ROOT / 'mesh_ops/MeshRoundingRepair.cs').read_text(encoding='utf-8-sig')
    norm = lambda x: re.sub(r'\s+', '', re.sub(r'//[^\n]*', '', x))
    improves = norm(_method(text, 'bool improves('))
    # M over the vertices of R and A, every one of them recorded; then the apex of A
    drift_part = norm("""editVerts.Clear();
            double most = 0;
            foreach (int t in removed) {
                Index3i tv = Mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) { editVerts.Add(tv[k]); most = Math.Max(most, drift[tv[k]]); }
            }
            foreach (Index3i tv in added)
                for (int k = 0; k < 3; ++k) { editVerts.Add(tv[k]); most = Math.Max(most, drift[tv[k]]); }
            editApex = apex_of(added);""")
    if drift_part not in improves:
        raise ValueError('improves: M over R and A, or the apex of A, changed; review drift_apex.py')
    # the bound is checked before anything else that could apply the edit
    if improves.index(drift_part) > improves.index('within_bound(most,step)==false'):
        raise ValueError('improves: the bound is checked before M is complete')
    within = norm(_method(text, 'bool within_bound('))
    if 'editDrift=most+step;' not in within or 'if(!(step>=0)||!(editDrift<=maxLength)){' not in within:
        raise ValueError('within_bound: the acceptance M + delta <= maxLength changed; review drift_apex.py')
    apex = norm(_method(text, 'static int apex_of('))
    apex_expected = norm("""static int apex_of(List<Index3i> tris)
        {
            if (tris.Count == 0)
                return -1;
            for (int k = 0; k < 3; ++k) {
                int c = tris[0][k];
                bool all = true;
                for (int i = 1; i < tris.Count && all; ++i)
                    all = tris[i].a == c || tris[i].b == c || tris[i].c == c;
                if (all)
                    return c;
            }
            return -1;
        }""")
    if apex != apex_expected:
        raise ValueError('apex_of changed: the apex must be a vertex of every added triangle; review drift_apex.py')
    commit = norm(_method(text, 'void commit_drift('))
    charge = norm("""if (editApex >= 0) {""")
    apex_only = norm("""drift[editApex] = editDrift;
            } else
                foreach (int v in editVerts)
                    drift[v] = editDrift;
            MaxDrift = Math.Max(MaxDrift, editDrift);""")
    if not commit.startswith(norm('void commit_drift()') + '{' + charge) or not commit.endswith(apex_only + '}'):
        raise ValueError('commit_drift: only the apex gets M + delta, else every vertex of R and A; review drift_apex.py')
    if text.count('editApex =') != 1 or text.count('drift[editApex]') != 2:
        raise ValueError('editApex is set or read elsewhere; review drift_apex.py')

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
