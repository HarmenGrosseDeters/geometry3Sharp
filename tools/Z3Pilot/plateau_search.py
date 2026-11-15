"""Z3 check of the plateau variant of the position search (docs/design/SNAP_ROUNDING_3D.md, section 10; a design that is
not built: there is no library code and no source contract. The acceptance rule is prototyped in
benchmarks/SnapRoundingExperiment/LatticeProbe.cs, LATTICE_STRICT / LATTICE_STALL).

The rule. After the position search of SAFE_ROUNDING.md section 9 has accepted nothing, for each defect that is left a
fixed number of *chains*. A chain draws candidates (one vertex, one of the 26 x 8 positions of the built search) from a
seeded integer sequence. A candidate is evaluated like every edit (the drift bound, no triangle turned over, the local
counts of degenerate triangles and of intersecting pairs: newB <= oldB and newI <= oldI) and is
  - an *improving* step if newB + newI < oldB + oldI (the rule of today): it is applied, the chain is committed;
  - a *sideways* step if both counts are equal: it is applied and written to the chain's journal (vertex, old
    position);
  - rejected otherwise.
A chain ends committed, or after B draws or K sideways steps, or at the effort limit of the repair: then its journal
is undone, the last entry first. A round that committed a chain is followed by the passes and another round; a round that committed nothing ends the search.

What is checked (each obligation UNSAT with satisfiable assumptions, each mutant SAT; UNKNOWN never counts):
  T   termination: the measure (N, q, ch, dr), N the number of defects, q = 1 while the round has committed a chain,
      ch the chains left in the round, dr the draws left in the chain, decreases lexicographically in every transition
      (a rejected draw, a sideways step, a commit, a chain that ends, a new round) and stays within fixed bounds.
      Bridge (not Z3): the lexicographic order on bounded tuples of naturals is well-founded. The cap K on the sideways
      steps is carried in the state (sw) and is not part of the measure: every sideways step uses a draw, so B alone
      bounds a chain; K bounds how far a chain can wander. A chain that ends at the effort limit is the transition
      "chain_ends" with no chain left afterwards.
  A   the counts: with the local counts complete for the defects at the moved vertex (bridge: a defect whose triangles
      have no corner at the moved vertex is unchanged), a sideways step keeps the number of degenerate triangles and
      the number of intersecting pairs of the whole mesh, an improving step lowers their sum and raises neither.
  R   the rollback: undoing a journal of up to three steps, the last first, gives the positions at the chain's start
      for any vertices, also when one vertex moved more than once (no hypotheses). Bridge (not Z3): for a longer
      journal by induction, undoing the last entry gives the state before the last step (R for one step).
  D   the bound: every applied step was checked against the bound and a rollback returns to a checked state, so the
      largest bound of the state never exceeds L (the drift of a state is a function of the positions and of anchors
      and constants that a displacement edit does not change: SAFE_ROUNDING_D1_DRIFT.md, displacement edit).
  P2  the number of draws (exact integers, not Z3): the longest run of the transition system from N0 defects, found by
      a search over every draw at which a chain can commit and every number of defects a commit can remove, for
      small N0, S, B (K is not modelled: it can only shorten a chain), is B S N0 (N0 + 1) / 2 (every round commits
      one chain at its last draw; the evaluation limit of the repair caps it in any case).

Mutants (must be SAT): a sideways step that counts as a move of the round (the rounds then repeat with the same N);
a rejected draw that is not counted; a new round after a round that committed nothing; a sideways step that refunds
the draws and is not capped (dr and sw both kept); acceptance by the sum of the counts, for the sideways and for the
improving step (a degenerate triangle traded for a new intersection); a journal undone the first entry first; a
journal of the new positions; a sideways step without the bound check.
"""
import json
import os
import sys
from functools import lru_cache

import z3


def check(formulas, timeout_ms):
    s = z3.Solver()
    s.set(timeout=timeout_ms)
    s.add(*formulas)
    return str(s.check())


# ---- T: the transition system ------------------------------------------------------------------------------------

def state(tag):
    return [z3.Int(f'{n}{tag}') for n in ('N', 'q', 'ch', 'dr', 'sw')]


def consts():
    return z3.Int('C'), z3.Int('B'), z3.Int('K')      # chains per round at most C, draws per chain B, sideways steps K


def inv(s):
    N, q, ch, dr, sw = s
    C, B, K = consts()
    return [N >= 0, q >= 0, q <= 1, ch >= 0, ch <= C, dr >= 0, dr <= B, sw >= 0, sw <= K, C >= 1, B >= 1, K >= 1]


def lex_less(a, b):
    """a < b lexicographically on (N, q, ch, dr)"""
    return z3.Or(a[0] < b[0],
                 z3.And(a[0] == b[0], a[1] < b[1]),
                 z3.And(a[0] == b[0], a[1] == b[1], a[2] < b[2]),
                 z3.And(a[0] == b[0], a[1] == b[1], a[2] == b[2], a[3] < b[3]))


def transitions(mutant=None):
    """name -> (guard and relation between the state s and the next state t)"""
    s, t = state(''), state("'")
    N, q, ch, dr, sw = s
    N2, q2, ch2, dr2, sw2 = t
    C, B, K = consts()
    in_chain = [N > 0, ch > 0, dr > 0, sw > 0]
    out = {}
    # a draw that is rejected
    out['reject'] = in_chain + [N2 == N, q2 == q, ch2 == ch, dr2 == (dr if mutant == 'reject_not_counted' else dr - 1), sw2 == sw]
    # a sideways step: the counts stay (A), one draw and one sideways step used
    if mutant == 'sideways_counts_as_a_move':
        out['sideways'] = in_chain + [N2 == N, q2 == 1, ch2 == ch, dr2 == dr - 1, sw2 == sw - 1]
    elif mutant == 'sideways_refunds_the_draws':
        out['sideways'] = in_chain + [N2 == N, q2 == q, ch2 == ch, dr2 == B, sw2 == sw]
    else:
        out['sideways'] = in_chain + [N2 == N, q2 == q, ch2 == ch, dr2 == dr - 1, sw2 == sw - 1]
    # an improving step: the chain is committed, the next chain starts
    out['commit'] = in_chain + [N2 < N, N2 >= 0, q2 == 1, ch2 == ch - 1, dr2 == B, sw2 == K]
    # the chain ends without one: rolled back, the next chain starts
    out['chain_ends'] = [N > 0, ch > 0, z3.Or(dr == 0, sw == 0), N2 == N, q2 == q, ch2 == ch - 1, dr2 == B, sw2 == K]
    # the round is over (no chain left, or no defect left) and it committed a chain: the passes (they never raise N),
    # then a new round
    restart = (q >= 0) if mutant == 'new_round_without_a_move' else (q == 1)
    out['new_round'] = [z3.Or(ch == 0, N == 0), restart, N2 <= N, N2 >= 0, q2 == 0, ch2 >= 0, ch2 <= C, dr2 == B, sw2 == K]
    return s, t, out


def termination(mutant=None):
    s, t, trans = transitions(mutant)
    queries = {}
    for name, rel in trans.items():
        hyp = inv(s) + rel
        bad = z3.Not(z3.And(lex_less(t, s), *inv(t)))
        queries[name] = (hyp, bad)
    return queries


# ---- A: the counts -----------------------------------------------------------------------------------------------

def counts(rule='per_kind'):
    """The whole mesh has NB degenerate triangles and NI intersecting pairs; oldB, oldI of them are at the moved
    vertex's triangles; after the step newB, newI are. Returns the hypotheses and the claims of a sideways and of an
    improving step."""
    NB, NI, oldB, oldI, newB, newI = z3.Ints('NB NI oldB oldI newB newI')
    hyp = [oldB >= 0, oldI >= 0, newB >= 0, newI >= 0, oldB <= NB, oldI <= NI]
    NB2, NI2 = NB - oldB + newB, NI - oldI + newI              # the bridge: the other defects are unchanged
    if rule == 'per_kind':
        evaluated = [newB <= oldB, newI <= oldI]
        sideways = evaluated + [newB + newI == oldB + oldI]
        improving = evaluated + [newB + newI < oldB + oldI]
    else:                                                       # the mutant: the sum only
        sideways = [newB + newI == oldB + oldI]
        improving = [newB + newI < oldB + oldI]
    return hyp, sideways, z3.And(NB2 == NB, NI2 == NI), improving, z3.And(NB2 + NI2 < NB + NI, NB2 <= NB, NI2 <= NI, NB2 >= 0, NI2 >= 0)


# ---- R: the rollback ---------------------------------------------------------------------------------------------

def rollback(steps=3, order='last_first', journal='old'):
    """positions as an array vertex -> value; a chain of `steps` sideways steps at any vertices, then the undo"""
    pos = z3.Array('pos', z3.IntSort(), z3.IntSort())
    vs = [z3.Int(f'v{i}') for i in range(steps)]
    ps = [z3.Int(f'p{i}') for i in range(steps)]
    cur, entries = pos, []
    for v, p in zip(vs, ps):
        entries.append((v, z3.Select(cur, v) if journal == 'old' else p))
        cur = z3.Store(cur, v, p)
    for v, old in (reversed(entries) if order == 'last_first' else entries):
        cur = z3.Store(cur, v, old)
    x = z3.Int('x')
    return [], z3.Select(cur, x) == z3.Select(pos, x)


# ---- D: the bound ------------------------------------------------------------------------------------------------

def bound(check_sideways=True):
    """most = the largest bound B(t) of the state; a step gives the bounds of the triangles at the moved vertex
    (`step`) and leaves the others (`rest` <= most). Applied only if checked."""
    most, rest, step, L = z3.Reals('most rest step L')
    improving = z3.Bool('improving')
    hyp = [most <= L, rest <= most, rest >= 0, step >= 0]
    applied = (step <= L) if check_sideways else z3.Or(z3.Not(improving), step <= L)
    most2 = z3.If(rest >= step, rest, step)
    return hyp + [applied], most2 <= L


# ---- P2: the number of draws, exact integers ---------------------------------------------------------------------

def longest_run(n0, S, B):
    """the largest number of draws of any run from n0 defects: a round has S chains per defect at its start, a chain
    at most B draws (K <= B sideways steps do not shorten the bound), a commit removes at least one defect"""
    sys.setrecursionlimit(100000)

    @lru_cache(maxsize=None)
    def round_(n):
        if n == 0:
            return 0
        return chain(n, S * n, False)

    @lru_cache(maxsize=None)
    def chain(n, ch, moved):
        if ch == 0 or n == 0:
            return round_(n) if moved else 0
        best = B + chain(n, ch - 1, moved)                           # B draws, no commit
        for used in range(1, B + 1):                                 # a commit at any draw of the chain
            for n2 in range(n):                                      # that removes one or more defects
                best = max(best, used + chain(n2, ch - 1, True))
        return best

    return round_(n0)


def draws_bound(n0, S, B):
    return B * S * n0 * (n0 + 1) // 2


def run(timeout_ms=120000):
    report = {}
    ok = True

    def add(name, formulas, expected):
        nonlocal ok
        got = check(formulas, timeout_ms)
        report[name] = {'expected': expected, 'got': got}
        ok = ok and got == expected

    for name, (hyp, bad) in termination().items():
        add('T_' + name, hyp + [bad], 'unsat')
        add('T_' + name + '_assumptions', hyp, 'sat')
    for mutant, transition in (('sideways_counts_as_a_move', 'sideways'), ('reject_not_counted', 'reject'),
                               ('sideways_refunds_the_draws', 'sideways'), ('new_round_without_a_move', 'new_round')):
        hyp, bad = termination(mutant)[transition]
        add('mutant_T_' + mutant, hyp + [bad], 'sat')

    hyp, sideways, keeps, improving, lowers = counts()
    add('A_sideways_keeps_both_counts', hyp + sideways + [z3.Not(keeps)], 'unsat')
    add('A_improving_lowers_the_sum_and_raises_neither', hyp + improving + [z3.Not(lowers)], 'unsat')
    add('A_sideways_assumptions', hyp + sideways, 'sat')
    add('A_improving_assumptions', hyp + improving, 'sat')
    hyp, sideways, keeps, improving, lowers = counts('sum')
    add('mutant_A_sideways_by_the_sum', hyp + sideways + [z3.Not(keeps)], 'sat')
    add('mutant_A_improving_by_the_sum', hyp + improving + [z3.Not(lowers)], 'sat')

    for steps in (1, 2, 3):
        hyp, same = rollback(steps)
        add(f'R_rollback_of_{steps}_steps', hyp + [z3.Not(same)], 'unsat')
    hyp, same = rollback(2, order='first_first')
    add('mutant_R_first_entry_first', hyp + [z3.Not(same)], 'sat')
    hyp, same = rollback(1, journal='new')
    add('mutant_R_journal_of_new_positions', hyp + [z3.Not(same)], 'sat')

    hyp, within = bound()
    add('D_bound_after_a_step', hyp + [z3.Not(within)], 'unsat')
    add('D_assumptions', hyp, 'sat')
    hyp, within = bound(check_sideways=False)
    add('mutant_D_sideways_without_the_check', hyp + [z3.Not(within)], 'sat')

    worst = []
    for n0 in range(0, 7):
        for S in (1, 2, 3):
            for B in (1, 2, 5):
                got, cap = longest_run(n0, S, B), draws_bound(n0, S, B)
                worst.append((n0, S, B, got, cap))
    holds = all(got <= cap for _, _, _, got, cap in worst)
    tight = all(got == cap for _, _, _, got, cap in worst)
    report['P2_draws_at_most_B_S_N0_(N0+1)/2_(exact_integers,_not_Z3)'] = {
        'expected': 'holds, attained', 'got': ('holds' if holds else 'fails') + (', attained' if tight else ', not attained'),
        'cases': len(worst)}
    ok = ok and holds and tight
    return ok, report


if __name__ == '__main__':
    timeout = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
    ok, report = run(timeout)
    print(json.dumps(report, indent=1))
    out = os.environ.get('Z3_PLATEAU_SEARCH_REPORT')
    if out:
        json.dump(report, open(out, 'w'), indent=1)
    sys.exit(0 if ok else 1)
