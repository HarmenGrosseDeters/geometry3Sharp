"""Z3 pilot 2, step 1: the D1 drift lemma of SafeRounding (docs/design/ROUNDING_DRIFT_APEX.md), checked per edit
shape before the rule is built.

An edit removes the triangles R, adds A and moves the surface by at most delta. Every vertex has a drift d (0 at the
start); M is the largest drift over the vertices of R and A; the edit is accepted if M + delta <= maxLength (L).
The apex rule charges M + delta to one vertex c that lies in every triangle of A (the apex) and keeps every other
drift; edits without an apex (A empty: ears, zips, strip zips) keep today's rule (every vertex of the edit gets
M + delta). The invariant, with D(T) the largest drift of the corners of T and S0 the surface at the start:
  (I1) every point x of a triangle T of S has dist(x, S0) <= D(T);
  (I2) every point y of S0 has a triangle T of S with dist(y, T) <= D(T).
The lemma: one edit that satisfies the two-sided bounds (E1) / (E2) keeps I, and d' >= d, and max d' <= L.

Model (README, section 'Pilot 2, step 1'): the distances are real variables, the geometric facts are hypotheses (the
witness points of (E1), (E2) and of I before the edit, and the triangle inequality); the combinatorics of each edit
type are fixed shapes. All of it is linear real arithmetic. The rounding of the stored drift uses Higham (2.5),
fl(s) = s / (1 + eta), |eta| <= u (valid for round to nearest); the weaker (2.4) model fl(s) = s (1 + eta) cannot
prove the chain bound and is kept as a documented SAT witness.

Every obligation must be UNSAT, its assumptions SAT (the model replayed with fractions), every mutant SAT with a
witness replayed with fractions outside Z3; UNKNOWN never counts.

This file is the specification of the rule (CHARGING, the constants below). The library does not implement the apex
rule yet: TODO(D1) the implementation PR adds a source contract against its accounting in
mesh_ops/MeshRoundingRepair.cs (improves / within_bound / commit_drift), see test_pilot.DriftApexTests.
"""
import time
from fractions import Fraction

import z3 as z

from filter_rules import QB, ZB, Vars, _check

U = Fraction(1, 2**53)          # unit roundoff of binary64 round to nearest

# the specification of the charging (ROUNDING_DRIFT_APEX.md section 2): with an apex, only the apex gets M + delta;
# without one (A empty), every vertex of R and A; the acceptance M + delta <= maxLength with M over R and A
CHARGING = dict(with_apex='apex', without_apex='all', acceptance='M + delta <= L', M_over='R and A')


def _if(B, c, a, b):
    return z.If(c, a, b) if B is ZB else (a if c else b)


def _max(B, xs):
    m = xs[0]
    for x in xs[1:]:
        m = _if(B, x > m, x, m)
    return m


# ---------------------------------------------------------------------------------------------------------------
# edit shapes: R (removed), A (added), the apex choices, and for A empty the untouched triangles that hold the
# counterpart simplex of (E2)

def move(k):
    """a move or slide of v with valence k (closed fan): the same triangles, v at a new position; apex v"""
    ring = [f'r{i}' for i in range(k)]
    tris = [('v', ring[i], ring[(i + 1) % k]) for i in range(k)]
    return dict(name=f'move_valence_{k}', R=tris, A=tris, apexes=['v'], U=[])


def contraction(m, n):
    """contraction of a-b, a vanishes, b kept (possibly moved): m + 1 triangles of a and n + 1 of b besides the two
    at the edge (l and r the opposite vertices); A: a's triangles with a replaced by b, and b's again; apex b"""
    p = ['l'] + [f'p{i}' for i in range(m)] + ['r']
    q = ['r'] + [f'q{i}' for i in range(n)] + ['l']
    a_side = [('a', p[i], p[i + 1]) for i in range(m + 1)]
    b_side = [('b', q[i], q[i + 1]) for i in range(n + 1)]
    R = a_side + b_side + [('a', 'b', 'l'), ('b', 'a', 'r')]
    A = [('b', x, y) for _, x, y in a_side] + b_side
    return dict(name=f'contraction_{m + 1}_{n + 1}', R=R, A=A, apexes=['b'], U=[])


def removal():
    """removal of v of valence 3 (ring a, b, c): one new triangle; every corner is an apex"""
    return dict(name='removal_valence_3', R=[('v', 'a', 'b'), ('v', 'b', 'c'), ('v', 'c', 'a')], A=[('a', 'b', 'c')],
                apexes=['a', 'b', 'c'], U=[])


def flip():
    """flip of a-b to c-d: apex c or d"""
    return dict(name='flip', R=[('a', 'b', 'c'), ('b', 'a', 'd')], A=[('c', 'a', 'd'), ('d', 'b', 'c')],
                apexes=['c', 'd'], U=[])


def ear():
    """contraction of an ear (v has the single triangle v, a, b; A empty): its points go to the edge a-b, which is in
    the untouched triangle a, b, w (w not a vertex of the edit); today's rule"""
    return dict(name='ear', R=[('v', 'a', 'b')], A=[], apexes=[], U=[('a', 'b', 'w')])


def shapes():
    for k in range(3, 9):
        yield move(k)
    for m in range(1, 6):
        for n in range(1, 6):
            yield contraction(m, n)
    yield removal()
    yield flip()


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


def edit_formula(B, v, shape, claim, rule='apex', apex=None, m_over='RA', only=None):
    """(hyps, bad) for one claim: 'I1', 'I2', 'monotone' (d' >= d) or 'bound' (max d' <= L).
    rule: 'apex' (charge apex), 'all' (today's rule), 'no_delta' (apex gets M only), or a vertex name to charge
    instead of the apex (a mutant); m_over: 'RA' (the rule) or 'A' (a mutant: M over the added triangles only);
    only: for 'I1', the one added triangle to check (None: all at once; the claim is a conjunction over them, so one
    query per triangle is the same check, and much faster for the larger contractions)."""
    edit, verts = _verts(shape)
    d = {x: v[f'd_{x}'] for x in verts}
    delta, M, L = v.delta, v.M, v.L
    m_set = edit if m_over == 'RA' else [x for t in shape['A'] for x in t]
    hyps = [d[x] >= 0 for x in verts] + [delta >= 0, L >= 0] + [d[x] <= L for x in verts]
    hyps += [M >= d[x] for x in m_set] + [B.Or(*[M == d[x] for x in m_set]), M + delta <= L]
    if rule == 'apex':
        charged, value = {apex}, M + delta
    elif rule == 'all':
        charged, value = set(edit), M + delta
    elif rule == 'no_delta':
        charged, value = {apex}, M
    else:
        charged, value = {rule}, M + delta
    d2 = {x: (value if x in charged else d[x]) for x in verts}

    def D(t, dd):
        return _max(B, [dd[x] for x in t])
    if claim == 'monotone':
        return hyps, B.Or(*[d2[x] < d[x] for x in verts])
    if claim == 'bound':
        return hyps, B.Or(*[d2[x] > L for x in verts])
    if claim == 'I1':
        if not shape['A']:
            return None
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


def obligations():
    """(rule label, name, shape, claim, rule, apex)"""
    for s in shapes():
        for c in s['apexes']:
            assert all(c in t for t in s['A']), (s['name'], c)      # the apex is in every added triangle
            for claim in ('I1', 'I2', 'monotone', 'bound'):
                yield ('apex rule', f"{s['name']}_apex_{c}_{claim}", s, claim, 'apex', c)
    for s in [ear(), flip(), removal(), contraction(1, 1)]:
        for claim in ('I1', 'I2', 'monotone', 'bound'):
            if claim == 'I1' and not s['A']:
                continue
            yield ("today's rule", f"{s['name']}_all_{claim}", s, claim, 'all', None)


def mutants():
    """(rule label, name, shape, claim, rule, apex, m_over): each must be SAT with a replayed witness"""
    c11 = contraction(1, 1)
    yield 'apex rule', 'contraction_charges_vanishing_a', c11, 'I1', 'a', None, 'RA'
    yield 'apex rule', 'flip_charges_a_in_one_added_triangle', flip(), 'I1', 'a', None, 'RA'
    yield 'apex rule', 'move_charges_ring_vertex', move(3), 'I1', 'r0', None, 'RA'
    yield 'apex rule', 'move_charges_M_without_delta', move(3), 'I1', 'no_delta', 'v', 'RA'
    yield 'apex rule', 'flip_charges_M_without_delta_I2', flip(), 'I2', 'no_delta', 'c', 'RA'
    yield 'apex rule', 'contraction_M_over_A_only', c11, 'I1', 'apex', 'b', 'A'
    yield 'apex rule', 'removal_M_over_A_only', removal(), 'I1', 'apex', 'a', 'RA_A'
    yield "today's rule", 'ear_charges_only_vanishing_v', ear(), 'I2', 'v', None, 'RA'


# ---------------------------------------------------------------------------------------------------------------
# the rounding of the stored drift over a chain of k edits

def chain_formula(B, v, model='2.5', with_eps=True):
    """one step k -> k + 1: true drift M_t <= M_h g + k eps (g = (1 + u)^k >= 1), true delta <= delta_h + eps, stored
    d_h' = fl(M_h + delta_h); claim: M_t + delta <= d_h' g (1 + u) + (k + 1) eps"""
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
            return dict(replayed=False, reason=f'{k} is not rational')
        vals[k] = val.as_fraction()
    hyps, bad = formula(QB, Vars(vals))
    return dict(hyps=all(hyps), bad=bool(bad), values={k: str(x) for k, x in vals.items() if x != 0})


def run(timeout_ms=120000):
    rows, muts = [], []
    for label, name, s, claim, rule, apex in obligations():
        v = variables(s)
        parts = range(len(s['A'])) if claim == 'I1' else [None]
        results, secs, assumed, replayed = [], 0.0, [], True
        for i in parts:
            f = (lambda s, claim, rule, apex, i: lambda B, w: edit_formula(B, w, s, claim, rule, apex, 'RA', i))(
                s, claim, rule, apex, i)
            hyps, bad = f(ZB, v)
            sol, r, t = _check(hyps, bad, timeout_ms)
            results.append(r)
            secs += t
            sat_s, rr, _ = _check(hyps, z.BoolVal(True), timeout_ms)
            assumed.append(rr)
            replayed = replayed and rr == z.sat and _replay(sat_s.model(), v, f)['hyps']
        # one row per claim: UNSAT only if every part is UNSAT, assumptions SAT only if every part's are
        worst = next((r for r in results if r != z.unsat), z.unsat)
        row = dict(rule=label, name=name, claim=claim, queries=len(results), result=str(worst), seconds=round(secs, 3),
                   assumptions_result='sat' if all(a == z.sat for a in assumed) else 'unsat or unknown',
                   assumptions_replayed=replayed)
        rows.append(row)
    # the apex rule really leaves other vertices below M + delta (non-vacuity of 'only the apex')
    partial = []
    for s in shapes():
        c = s['apexes'][0]
        v = variables(s)
        edit, _ = _verts(s)

        def f(B, w, s=s, c=c, edit=edit):
            hyps, _ = edit_formula(B, w, s, 'monotone', 'apex', c)
            return hyps, B.Or(*[B.And(w[f'd_{x}'] < w.M + w.delta, w.delta > 0) for x in edit if x != c])
        hyps, bad = f(ZB, v)
        sol, r, _ = _check(hyps, bad, timeout_ms)
        partial.append(dict(name=s['name'], result=str(r),
                            replayed=r == z.sat and all(_replay(sol.model(), v, f)[k] for k in ('hyps', 'bad'))))
    for label, name, s, claim, rule, apex, m_over in mutants():
        v = variables(s)
        mo = 'A' if m_over in ('A', 'RA_A') else 'RA'
        f = (lambda s, claim, rule, apex, mo: lambda B, w: edit_formula(B, w, s, claim, rule, apex, mo))(s, claim, rule, apex, mo)
        hyps, bad = f(ZB, v)
        sol, r, secs = _check(hyps, bad, timeout_ms)
        row = dict(rule=label, name=name, result=str(r), seconds=round(secs, 3))
        if r == z.sat:
            rep = _replay(sol.model(), v, f)
            row.update(replayed=rep['hyps'] and rep['bad'], witness=rep['values'])
        else:
            row['replayed'] = False
        muts.append(row)
    # the chain step
    v = Vars({n: z.Real(n) for n in CHAIN_VARS.split()})
    f = lambda B, w: chain_formula(B, w, '2.5')
    hyps, bad = f(ZB, v)
    sol, r, secs = _check(hyps, bad, timeout_ms)
    row = dict(rule='rounding chain', name='chain_step_higham_2_5', claim='d <= d_h (1 + u)^k + k eps', result=str(r),
               seconds=round(secs, 3))
    sat_s, rr, _ = _check(hyps, z.BoolVal(True), timeout_ms)
    row['assumptions_result'] = str(rr)
    row['assumptions_replayed'] = rr == z.sat and _replay(sat_s.model(), v, f)['hyps']
    rows.append(row)
    f = lambda B, w: chain_formula(B, w, '2.4')
    hyps, bad = f(ZB, v)
    sol, r, secs = _check(hyps, bad, timeout_ms)
    row = dict(rule='rounding chain', name='chain_step_with_2_4_model_only', result=str(r), seconds=round(secs, 3))
    if r == z.sat:
        rep = _replay(sol.model(), v, f)
        row.update(replayed=rep['hyps'] and rep['bad'], witness=rep['values'])
    else:
        row['replayed'] = False
    muts.append(row)
    passed = (all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' and r['assumptions_replayed']
                  for r in rows)
              and all(p['result'] == 'sat' and p['replayed'] for p in partial)
              and all(m['result'] == 'sat' and m['replayed'] for m in muts))
    return dict(z3=z.get_version_string(), timeout_ms=timeout_ms, specification=CHARGING, obligations=rows,
                partial_charge_nonvacuity=partial, mutants=muts,
                total_seconds=round(sum(r['seconds'] for r in rows + muts), 3), obligations_passed=passed,
                proof_scope='One edit of each listed shape, distances as real variables (README); (E1), (E2), the '
                            'point/triangle distance model, I1/I2 => Hausdorff and the induction over edits are '
                            'review-only.')


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
