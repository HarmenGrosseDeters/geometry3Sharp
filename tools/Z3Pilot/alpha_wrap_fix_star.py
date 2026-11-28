"""Local Z3 obligations and complexity witnesses for MeshAlphaWrap fix_star (#1607).

Formally verifies the equivalence, inductive invariants, termination, and worst-case
super-linear complexity of the one-pass manifold step (fix_star, before, heap_push/heap_pop,
uf_find, join_candidate, join_inside_components, join_flip, build_outside_tree,
outside_pieces_cut_off, outside_stays_connected, preferred_flip, star_components) in
mesh_ops/MeshAlphaWrap.cs against the reference per-move full-star recomputation
(ReferenceFixStar):

1. Candidate ordering (before vs preferred_flip):
   - Models finite non-NaN cell volumes (Delaunay3.CellVolume on MeshAlphaWrap's <= 2^128
     coordinate range) as z3.Int.
   - before(a, b) is a strict total order on distinct local indices K and is equivalent to
     the reference scan order: preferred_flip(a, b) || (!preferred_flip(b, a) && a.K < b.K).
   - Negative controls (SAT): preferred_flip alone ties on equal-volume cells; reversing the
     tie-break to a.K > b.K breaks scan-order equivalence.
2. Lazy heap monotonicity (join_inside_components):
   - Cut-off dead cells are completely isolated from surviving outside cells (UNSAT to share
     an edge), so flipping deadCells never adds a new inside neighbour to any surviving outside cell.
   - For any surviving outside cell not adjacent to best, Joins can never improve (False -> True
     is UNSAT); it can only stay equal or worsen (True -> False, SAT witness motivating the lazy
     re-check on heap_pop).
   - Negative control (SAT): an outside neighbour of best CAN transition False -> True, proving
     why join_inside_components must refresh nbrs[0..nn) after flipping best.
3. Spanning-forest cut-off exactness and inductive preservation (build_outside_tree + outside_pieces_cut_off):
   - On bounded star dual graphs (N = 5 outside cells), the detached-minus-reattached set equals
     the exact set of outside cells disconnected from the box boundary after removing xk (UNSAT
     mismatch).
   - Inductive step: the updated treeParent' pointers produced by BFS reattachment preserve the
     boundary-rooted acyclic spanning-forest invariant on all surviving outside cells (UNSAT to
     create a cycle, point to xk/dead, or break boundary rooting).
   - Negative controls (SAT): 1-hop reattachment drops live cells; non-root boundary cells break
     cut-off exactness; reversing treeParent[mk] = u to treeParent[u] = mk breaks the forest invariant.
4. Non-exterior connectivity criterion (outside_stays_connected):
   - Verifies the graph-theoretic criterion (abstract connectivity on bounded graphs; the lockstep
     multi-source BFS with group-exhaustion early exit is mirrored in Python replays):
     - Removing a degree-1 cell (nn == 1) from a connected outside component of >= 2 cells never
       disconnects it (UNSAT, validating `if (nn == 1) return true;`).
     - Mutual reachability among xk's outside neighbours in Outside \\ {xk} is equivalent to global
       connectivity of Outside \\ {xk} (UNSAT mismatch); nn == 2 admits a cut-vertex split (SAT).
5. Bounded-graph join_flip component counting, termination, and super-linear complexity (#1607):
   - On a bounded 4-cell graph with arbitrary initial inside/outside labels and any two-cell flip
     order (including sorted deadCells where an isolated cell flips with distinct == 0 before its
     connector), incremental nIn' = nIn + 1 - distinct equals the exact connected-component count
     of the resulting inside subgraph (UNSAT mismatch; dropping `+ 1` is SAT).
   - Progress matches MeshAlphaWrap.cs:2634,2645: on the non-fallback path dead_cnt < out_before - 1,
     so out_after >= 1 and strictly decreases; dead_cnt == out_before - 1 triggers return -1.
   - Super-linear comb-star complexity: for any linear constant c >= 1, choosing k = L >= c + 1
     in W = k * L + k * (k + 1) / 2 proves W > c * k (UNSAT for W <= c * k), showing no linear
     bound O(k) holds across star sizes.
6. Concrete Python replays (all enforcing the 3-star-facet bound len(adj[u]) + int(boundary[u]) <= 3
   of a 3D Delaunay vertex star) mirroring fix_star / join_inside_components / outside_stays_connected:
   (a) stale-key demotion + 2-cell out-of-order deadCells (distinct == 0 then distinct == 3),
   (b) degree-3-facet comb star (K = 20, L = 20, exact reattachment work W = 570), and
   (c) non-exterior cycle (M = 40, 39 cells visited) + unbalanced split early-exit check.
"""
import heapq
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'

FIX_STAR_ANCHORS = [
    ('int fix_star(int v, int w)', 'readonly List<int> ufParent',
     'e11d51799070e76d89bf0d359a4bf5c4528266fb1a0d5b37c1fb3450f16f0919'),
    ('internal static bool before(in JoinCandidate a, in JoinCandidate b)', 'int join_inside_components(',
     '7307339d0ef7ca42a3e7098b7a53f4928b8c1af929308978f6a5b9548a61bf8c'),
    ('int join_inside_components(int v, int w, int nIn, bool allExterior, ref int flipped)', 'readonly List<int> deadCells',
     '469399dd6505fca4c353c6033c18b55239fc3bc4da7e90204788a50c345abcc9'),
    ('int join_flip(int k, int v, int w, ref int nIn, int[] nbrs, ref int flipped)', 'int[] treeParent',
     '9463bbdf22c86709a1907f019b4372e6d9086964f755a8b00274f9bbb940b2fc'),
    ('void build_outside_tree(int v, int w)', 'internal bool ReferenceFixStar;',
     '93d2e91c420eb5d0975228a111d20a34821b3ffa45021eafdb31be447a807499'),
    ('void outside_pieces_cut_off(int xk, int v, int w, List<int> dead)', 'bool outside_stays_connected(',
     '715de25356a2ae2feac3e1ca7728edbfc4eb9a2cc69e8e12b84a5e9a26a82b47'),
    ('bool outside_stays_connected(int v, int w, int[] nbrs, int nn)', 'internal static bool preferred_flip(',
     '868a735fefef8d3c6bf53f2dae4f122d2530ea5520aca589ce21fcbd0a9bb21c'),
    ('internal static bool preferred_flip(', 'bool has_corner(',
     '748be5b7f6f5d79880fb9f8b612ed93c4ce6d038445999d0641b04f93529e7cb'),
]


def _before_expr(ca, ja, va, ka, cb, jb, vb, kb):
    return z3.If(
        ca != cb, z3.Not(ca),
        z3.If(ja != jb, ja, z3.If(va != vb, va < vb, ka < kb))
    )


def _pref_expr(ca, ja, va, cb, jb, vb):
    return z3.If(ca != cb, z3.Not(ca), z3.If(ja != jb, ja, va < vb))


def candidate_order_obligations():
    rows = []
    ca, cb, cc = z3.Bools('ca cb cc')
    ja, jb, jc = z3.Bools('ja jb jc')
    va, vb, vc, ka, kb, kc = z3.Ints('va vb vc ka kb kc')

    ab = _before_expr(ca, ja, va, ka, cb, jb, vb, kb)
    ba = _before_expr(cb, jb, vb, kb, ca, ja, va, ka)
    bc = _before_expr(cb, jb, vb, kb, cc, jc, vc, kc)
    ac = _before_expr(ca, ja, va, ka, cc, jc, vc, kc)
    aa = _before_expr(ca, ja, va, ka, ca, ja, va, ka)
    pref_ab = _pref_expr(ca, ja, va, cb, jb, vb)
    pref_ba = _pref_expr(cb, jb, vb, ca, ja, va)
    scan_ab = z3.Or(pref_ab, z3.And(z3.Not(pref_ba), ka < kb))
    ab_rev_k = z3.If(
        ca != cb, z3.Not(ca),
        z3.If(ja != jb, ja, z3.If(va != vb, va < vb, ka > kb))
    )

    rows.append(('candidate_order_assumptions_sat', [ka != kb, kb != kc, ka != kc], z3.And(ab, bc, pref_ab), 'sat', 'QF_LIA'))
    rows.append((
        'before_is_irreflexive_and_asymmetric',
        [],
        z3.Or(aa, z3.And(ab, ba)),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'before_is_transitive',
        [ab, bc],
        z3.Not(ac),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'before_is_trichotomous_on_distinct_k',
        [ka != kb],
        z3.Not(z3.Or(ab, ba)),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'before_equals_reference_scan_order',
        [ka != kb],
        z3.Xor(ab, scan_ab),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'before_reversed_k_breaks_scan_order_negative_control',
        [ka != kb],
        z3.Xor(ab_rev_k, scan_ab),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'preferred_flip_ties_without_k_negative_control',
        [ka != kb, ca == cb, ja == jb, va == vb],
        z3.And(z3.Not(pref_ab), z3.Not(pref_ba)),
        'sat',
        'QF_LIA',
    ))
    return rows


def _undirected_edges(prefix, n):
    e = {}
    for i in range(n):
        for j in range(i + 1, n):
            v = z3.Bool(f'{prefix}_{i}_{j}')
            e[(i, j)] = v
            e[(j, i)] = v
    return e


def lazy_heap_monotonicity_obligations():
    rows = []

    # 1. Dead-cell isolation on N = 5 outside cells (0 = best, 1..4 = remaining outside cells)
    n = 5
    e = _undirected_edges('iso_e', n)
    bnd = [z3.Bool(f'iso_bnd_{i}') for i in range(n)]
    reach = [bnd[i] for i in range(n)]
    for _ in range(n - 2):
        nxt = [z3.BoolVal(False)] * n
        for i in range(1, n):
            nxt[i] = z3.Or(reach[i], *[z3.And(e[(i, j)], reach[j]) for j in range(1, n) if j != i])
        reach = nxt
    dead = [z3.Not(reach[i]) for i in range(n)]
    surv = [reach[i] for i in range(n)]

    rows.append((
        'dead_isolation_assumptions_sat',
        [],
        z3.And(dead[1], surv[2], e[(0, 1)], e[(0, 2)]),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'dead_cells_isolated_from_surviving_outside',
        [],
        z3.Or(*[z3.And(dead[i], surv[j], e[(i, j)]) for i in range(1, n) for j in range(1, n) if i != j]),
        'unsat',
        'QF_LIA',
    ))

    # 2. Non-neighbour Joins monotonicity: cell u has up to 3 star-neighbours (0, 1, 2).
    is_in = [z3.Bool(f'u_in_{k}') for k in range(3)]
    r = [z3.Int(f'u_r_{k}') for k in range(3)]
    in_m = [z3.Bool(f'in_m_{c}') for c in range(3)]
    prem = [z3.And(0 <= r[k], r[k] <= 2) for k in range(3)]

    def merged(rk):
        return z3.If(
            z3.Or(z3.And(rk == 0, in_m[0]), z3.And(rk == 1, in_m[1]), z3.And(rk == 2, in_m[2])),
            z3.IntVal(3),
            rk,
        )

    rp = [merged(r[k]) for k in range(3)]
    joins_before = z3.Or(*[
        z3.And(is_in[i], is_in[j], r[i] != r[j]) for i in range(3) for j in range(i + 1, 3)
    ])
    joins_after = z3.Or(*[
        z3.And(is_in[i], is_in[j], rp[i] != rp[j]) for i in range(3) for j in range(i + 1, 3)
    ])

    rows.append((
        'non_neighbour_joins_can_worsen_witness',
        prem,
        z3.And(joins_before, z3.Not(joins_after)),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'non_neighbour_joins_never_improves',
        prem + [z3.Not(joins_before)],
        joins_after,
        'unsat',
        'QF_LIA',
    ))
    joins_after_nbr = z3.Or(
        joins_after,
        z3.And(z3.Not(is_in[0]), is_in[1], rp[1] != 3),
        z3.And(z3.Not(is_in[0]), is_in[2], rp[2] != 3),
    )
    rows.append((
        'neighbour_of_best_joins_can_improve_negative_control',
        prem + [z3.Not(is_in[0]), z3.Not(joins_before)],
        joins_after_nbr,
        'sat',
        'QF_LIA',
    ))
    return rows


def spanning_forest_cutoff_obligations():
    rows = []
    n = 5  # outside cells 0..4; xk = 0 is moved inside
    e = _undirected_edges('sf_e', n)
    bnd = [z3.Bool(f'sf_bnd_{i}') for i in range(n)]
    par = [z3.Int(f'sf_par_{i}') for i in range(n)]
    depth = [z3.Int(f'sf_dep_{i}') for i in range(n)]

    def forest_premises(enforce_bnd_root):
        p = []
        for i in range(n):
            p.append(z3.And(-1 <= par[i], par[i] < n, par[i] != i))
            p.append(z3.And(0 <= depth[i], depth[i] < n))
            for j in range(n):
                if j == i:
                    continue
                p.append(z3.Implies(par[i] == j, z3.And(e[(i, j)], depth[i] == depth[j] + 1)))
            if enforce_bnd_root:
                p.append(z3.Implies(bnd[i], z3.And(par[i] == -1, depth[i] == 0)))
                p.append(z3.Implies(z3.Not(bnd[i]), par[i] >= 0))
            else:
                p.append(z3.Implies(par[i] == -1, z3.And(bnd[i], depth[i] == 0)))
                p.append(z3.Implies(z3.Not(bnd[i]), par[i] >= 0))
        return p

    # Subtree det of xk = 0 in treeParent
    det = [z3.BoolVal(False)] + [par[i] == 0 for i in range(1, n)]
    for _ in range(n - 2):
        nxt = [z3.BoolVal(False)] * n
        for i in range(1, n):
            nxt[i] = z3.Or(det[i], *[z3.And(par[i] == j, det[j]) for j in range(1, n) if j != i])
        det = nxt

    # Reattachment BFS waves inside det: wave[0][i] = seed adjacent to non-det surviving cell;
    # wave[s][i] = reached within <= s hops inside det.
    wave = []
    att0 = [z3.BoolVal(False)] * n
    for i in range(1, n):
        att0[i] = z3.And(det[i], z3.Or(*[z3.And(z3.Not(det[j]), e[(i, j)]) for j in range(1, n) if j != i]))
    wave.append(att0)
    for _ in range(n - 2):
        prev = wave[-1]
        nxt = [z3.BoolVal(False)] * n
        for i in range(1, n):
            nxt[i] = z3.Or(prev[i], z3.And(det[i], z3.Or(*[z3.And(prev[j], e[(i, j)]) for j in range(1, n) if j != i])))
        wave.append(nxt)
    att = wave[-1]

    dead_algo = [z3.And(det[i], z3.Not(att[i])) for i in range(n)]
    surv = [z3.BoolVal(False)] + [z3.Not(dead_algo[i]) for i in range(1, n)]

    # Exact reachability in Outside \ {0} to any boundary root
    reach = [z3.BoolVal(False)] + [bnd[i] for i in range(1, n)]
    for _ in range(n - 2):
        nxt = [z3.BoolVal(False)] * n
        for i in range(1, n):
            nxt[i] = z3.Or(reach[i], *[z3.And(e[(i, j)], reach[j]) for j in range(1, n) if j != i])
        reach = nxt
    dead_exact = [z3.Not(reach[i]) for i in range(n)]

    prem_valid = forest_premises(enforce_bnd_root=True)
    rows.append((
        'spanning_forest_cutoff_assumptions_sat',
        prem_valid,
        z3.And(det[1], att[1], dead_algo[2]),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'outside_cutoff_matches_exact_disconnection',
        prem_valid,
        z3.Or(*[z3.Xor(dead_algo[i], dead_exact[i]) for i in range(1, n)]),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'one_hop_reattach_drops_live_cell_negative_control',
        prem_valid,
        z3.Or(*[z3.And(det[i], z3.Not(att0[i]), reach[i]) for i in range(1, n)]),
        'sat',
        'QF_LIA',
    ))
    prem_relaxed = forest_premises(enforce_bnd_root=False)
    rows.append((
        'non_root_boundary_breaks_cutoff_negative_control',
        prem_relaxed,
        z3.Or(*[z3.Xor(dead_algo[i], dead_exact[i]) for i in range(1, n)]),
        'sat',
        'QF_LIA',
    ))

    # Inductive preservation of the boundary-rooted acyclic spanning forest on surviving outside cells:
    # Model treeParent' (par2) and a witness depth (dep2) after outside_pieces_cut_off's BFS reattachment.
    par2 = [z3.Int(f'sf_par2_{i}') for i in range(n)]
    # Exact BFS discovery layer layer[i] in {0..n} (0 for non-det surviving, 1 for att0, s+1 for newly reached at step s)
    layer = [z3.IntVal(0)] * n
    for i in range(1, n):
        expr = z3.IntVal(n)
        for s in range(len(wave) - 1, -1, -1):
            expr = z3.If(wave[s][i], z3.IntVal(s + 1), expr)
        layer[i] = z3.If(z3.Not(det[i]), z3.IntVal(0), expr)

    reattach_prem = list(prem_valid)
    for i in range(1, n):
        # Non-detached surviving cells keep their parent; reattached cells pick a neighbour with strictly smaller layer
        reattach_prem.append(z3.Implies(z3.Not(det[i]), par2[i] == par[i]))
        reattach_prem.append(z3.Implies(
            att[i],
            z3.And(
                1 <= par2[i], par2[i] < n, par2[i] != i,
                *[z3.Implies(par2[i] == j, z3.And(surv[j], e[(i, j)], layer[j] < layer[i])) for j in range(1, n) if j != i],
            ),
        ))

    # Rank witness: non-det keeps depth[i]; reattached gets n + layer[i] -> strictly decreases along every parent edge!
    rank2 = [z3.IntVal(0)] + [z3.If(z3.Not(det[i]), depth[i], n + layer[i]) for i in range(1, n)]
    # Check all surviving forest invariants:
    # (1) bnd[i] => par2[i] == -1; (2) !bnd[i] => par2[i] in 1..n-1, surv[par2[i]], e[i, par2[i]], rank2[i] > rank2[par2[i]]
    bad_forest = []
    for i in range(1, n):
        bad_bnd = z3.And(surv[i], bnd[i], par2[i] != -1)
        parent_ok = z3.Or(*[
            z3.And(par2[i] == j, surv[j], e[(i, j)], rank2[i] > rank2[j])
            for j in range(1, n) if j != i
        ])
        bad_non_bnd = z3.And(surv[i], z3.Not(bnd[i]), z3.Not(parent_ok))
        bad_forest.extend([bad_bnd, bad_non_bnd])

    rows.append((
        'inductive_forest_assumptions_sat',
        reattach_prem,
        z3.And(det[1], att[1], det[2], att[2], z3.Not(att0[2])),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'reattached_forest_preserves_invariants',
        reattach_prem,
        z3.Or(*bad_forest),
        'unsat',
        'QF_LIA',
    ))
    # Negative control: reversing treeParent[mk] = u to treeParent[u] = mk (pointing to a higher BFS layer
    # instead of a strictly lower one) violates the surviving forest rank/acyclicity invariant (bad_forest is SAT)
    par2_rev = [z3.Int(f'sf_par2_rev_{i}') for i in range(n)]
    rev_prem = list(prem_valid)
    bad_forest_rev = []
    for i in range(1, n):
        rev_prem.append(z3.Implies(z3.Not(det[i]), par2_rev[i] == par[i]))
        rev_prem.append(z3.Implies(
            att[i],
            z3.And(
                1 <= par2_rev[i], par2_rev[i] < n, par2_rev[i] != i,
                *[z3.Implies(par2_rev[i] == j, z3.And(surv[j], e[(i, j)])) for j in range(1, n) if j != i],
            ),
        ))
        # At least one reattached vertex points to a higher-layer neighbour (reversing treeParent[mk] = u)
        parent_ok_rev = z3.Or(*[
            z3.And(par2_rev[i] == j, surv[j], e[(i, j)], rank2[i] > rank2[j])
            for j in range(1, n) if j != i
        ])
        bad_forest_rev.append(z3.And(surv[i], z3.Not(bnd[i]), z3.Not(parent_ok_rev)))
    rows.append((
        'reversed_reattach_parent_breaks_forest_negative_control',
        rev_prem,
        z3.Or(*bad_forest_rev),
        'sat',
        'QF_LIA',
    ))
    return rows


def outside_stays_connected_obligations():
    rows = []
    n = 5  # connected outside component {0..4}; xk = 0 is removed
    e = _undirected_edges('cc_e', n)

    r0 = [z3.BoolVal(i == 0) for i in range(n)]
    for _ in range(n - 1):
        nxt = list(r0)
        for i in range(n):
            nxt[i] = z3.Or(r0[i], *[z3.And(e[(i, j)], r0[j]) for j in range(n) if j != i])
        r0 = nxt
    prem = [r0[i] for i in range(1, n)]

    nbr = [z3.BoolVal(False)] + [e[(0, i)] for i in range(1, n)]
    nn = z3.Sum([z3.If(nbr[i], 1, 0) for i in range(1, n)])

    r = [[z3.BoolVal(i == j) for j in range(n)] for i in range(n)]
    for i in range(1, n):
        for j in range(1, n):
            if i != j:
                r[i][j] = e[(i, j)]
    for _ in range(n - 3):
        nxt = [[r[i][j] for j in range(n)] for i in range(n)]
        for i in range(1, n):
            for j in range(1, n):
                if i != j:
                    nxt[i][j] = z3.Or(r[i][j], *[z3.And(r[i][k], e[(k, j)]) for k in range(1, n) if k != i and k != j])
        r = nxt

    global_conn = z3.And(*[r[i][j] for i in range(1, n) for j in range(i + 1, n)])
    nbrs_conn = z3.And(*[
        z3.Implies(z3.And(nbr[i], nbr[j]), r[i][j])
        for i in range(1, n) for j in range(i + 1, n)
    ])

    rows.append((
        'degree_one_assumptions_sat',
        prem + [nn == 1],
        global_conn,
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'degree_two_removal_can_split_witness',
        prem + [nn == 2],
        z3.Not(global_conn),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'degree_one_removal_keeps_connectivity',
        prem + [nn == 1],
        z3.Not(global_conn),
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'neighbour_reachability_equals_global_connectivity',
        prem + [nn >= 1],
        z3.Xor(nbrs_conn, global_conn),
        'unsat',
        'QF_LIA',
    ))
    return rows


def _exact_inside_comp_count(e, inside, n):
    """Exact connected-component count of the induced inside subgraph on {0..n-1}."""
    # Reachability within inside subgraph in <= n-1 hops
    r = [[z3.BoolVal(i == j) if i == j else z3.And(inside[i], inside[j], e[(i, j)]) for j in range(n)] for i in range(n)]
    for _ in range(n - 2):
        nxt = [[r[i][j] for j in range(n)] for i in range(n)]
        for i in range(n):
            for j in range(n):
                if i != j:
                    nxt[i][j] = z3.Or(
                        r[i][j],
                        *[z3.And(r[i][k], inside[k], inside[j], e[(k, j)]) for k in range(n) if k != i and k != j],
                    )
        r = nxt
    # Each component has a unique minimum-index inside vertex (representative)
    reps = []
    for i in range(n):
        no_smaller = z3.And(*[z3.Not(r[i][j]) for j in range(i)])
        reps.append(z3.If(z3.And(inside[i], no_smaller), 1, 0))
    return z3.Sum(reps), r


def component_count_and_complexity_obligations():
    rows = []

    # 1. Bounded-graph join_flip exactness on N = 4 cells {0, 1, 2, 3}:
    #    Cells 0 and 1 start Outside and are flipped sequentially (0 then 1, e.g. sorted deadCells);
    #    cells 2 and 3 have arbitrary initial Inside/Outside labels.
    n = 4
    e = _undirected_edges('jf_e', n)
    in0 = [z3.BoolVal(False), z3.BoolVal(False), z3.Bool('jf_in_2'), z3.Bool('jf_in_3')]
    in1 = [z3.BoolVal(True), z3.BoolVal(False), in0[2], in0[3]]
    in2 = [z3.BoolVal(True), z3.BoolVal(True), in0[2], in0[3]]

    n_in_0, r0 = _exact_inside_comp_count(e, in0, n)
    n_in_1_exact, r1 = _exact_inside_comp_count(e, in1, n)
    n_in_2_exact, _ = _exact_inside_comp_count(e, in2, n)

    def distinct_touched_comps(cell, prev_in, prev_r):
        # Number of distinct inside components of prev_in adjacent to `cell`:
        # count inside neighbours j that are minimum-index among cell's neighbours in their component
        terms = []
        for j in range(n):
            if j == cell:
                continue
            adj_j = z3.And(prev_in[j], e[(cell, j)])
            no_smaller_adj = z3.And(*[
                z3.Not(z3.And(prev_in[m], e[(cell, m)], prev_r[j][m]))
                for m in range(j) if m != cell
            ])
            terms.append(z3.If(z3.And(adj_j, no_smaller_adj), 1, 0))
        return z3.Sum(terms)

    d1 = distinct_touched_comps(0, in0, r0)
    n_in_1_algo = n_in_0 + 1 - d1
    d2 = distinct_touched_comps(1, in1, r1)
    n_in_2_algo = n_in_1_algo + 1 - d2

    # Witness: out-of-order flip where cell 0 has distinct == 0 (temporarily increments nIn), then cell 1 merges it
    rows.append((
        'join_flip_isolated_then_merge_witness',
        [n_in_0 >= 1],
        z3.And(d1 == 0, d2 >= 2),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'join_flip_matches_exact_inside_components',
        [],
        z3.Or(n_in_1_algo != n_in_1_exact, n_in_2_algo != n_in_2_exact),
        'unsat',
        'QF_LIA',
    ))
    # Negative control: dropping `+ 1` in `nIn = nIn + 1 - distinct` disagrees with exact component count
    rows.append((
        'join_flip_missing_plus_one_negative_control',
        [],
        (n_in_0 - d1) != n_in_1_exact,
        'sat',
        'QF_LIA',
    ))

    # 2. Termination / progress matching MeshAlphaWrap.cs:2634,2645:
    #    after best flips (outsideCount-- -> rem = out_before - 1), non-fallback path has dead_cnt < rem
    out_before, dead_cnt = z3.Ints('out_before dead_cnt')
    rem = out_before - 1
    out_after = rem - dead_cnt
    prog_prem = [out_before >= 2, 0 <= dead_cnt, dead_cnt < rem]
    rows.append(('join_progress_assumptions_sat', prog_prem, dead_cnt == 1, 'sat', 'QF_LIA'))
    rows.append((
        'join_non_fallback_strictly_decreases_positive_outside',
        prog_prem,
        z3.Or(out_after < 1, out_after >= out_before),
        'unsat',
        'QF_LIA',
    ))

    # 3. Super-linear worst-case complexity (#1607): for ANY linear constant c >= 1, the comb star
    #    with k = L >= c + 1 has exact reattachment work W = k * L + k * (k + 1) / 2 > c * k
    c, k, L, W = z3.Ints('c k L W')
    comb_prem = [c >= 1, k >= c + 1, L >= c + 1, 2 * W == 2 * k * L + k * (k + 1)]
    rows.append(('comb_reattach_assumptions_sat', comb_prem, z3.And(c == 15, k == 19, L == 20), 'sat', 'QF_NIA'))
    rows.append((
        'comb_reattach_beats_any_linear_bound',
        comb_prem,
        W <= c * k,
        'unsat',
        'QF_NIA',
    ))
    # Negative control: a constant-work star (W == 3 * k) does NOT beat c = 5
    rows.append((
        'linear_work_does_not_beat_bound_negative_control',
        [c == 5, k >= 10, W == 3 * k],
        W <= c * k,
        'sat',
        'QF_LIA',
    ))
    return rows


# ---- Python mirrors of fix_star / join_inside_components / outside_stays_connected ----

def _star_components(adj, labels, boundary, corner, volume):
    n = len(adj)
    comp = [-1] * n
    comp_label, comp_ext, comp_corner, comp_vol = [], [], [], []
    for k in range(n):
        if comp[k] >= 0:
            continue
        cid = len(comp_label)
        lab = labels[k]
        ext = False
        has_c = False
        vol = 0.0
        stack = [k]
        comp[k] = cid
        while stack:
            u = stack.pop()
            if boundary[u]:
                ext = True
            if corner[u]:
                has_c = True
            vol += volume[u]
            for m in adj[u]:
                if comp[m] < 0 and labels[m] == lab:
                    comp[m] = cid
                    stack.append(m)
        comp_label.append(lab)
        comp_ext.append(ext)
        comp_corner.append(has_c)
        comp_vol.append(vol)
    return comp, comp_label, comp_ext, comp_corner, comp_vol


def _outside_stays_connected_work(adj, labels, xk, nbrs):
    nn = len(nbrs)
    if nn == 1:
        return True, 0
    if nn == 0:
        return False, 0
    queues = [[u] for u in nbrs]
    heads = [0] * nn
    owner = {u: j for j, u in enumerate(nbrs)}
    grp = list(range(nn))

    def find(a):
        while grp[a] != a:
            grp[a] = grp[grp[a]]
            a = grp[a]
        return a

    groups = nn
    while groups > 1:
        progressed = False
        for j in range(nn):
            if groups == 1 or heads[j] >= len(queues[j]):
                continue
            progressed = True
            u = queues[j][heads[j]]
            heads[j] += 1
            for m in adj[u]:
                if m == xk or labels[m] != 0:
                    continue
                if m in owner:
                    a, b = find(owner[m]), find(j)
                    if a != b:
                        grp[a] = b
                        groups -= 1
                else:
                    owner[m] = j
                    queues[j].append(m)
        if groups == 1:
            break
        if not progressed:
            return False, sum(len(q) for q in queues)
        # Group-exhaustion early exit (MeshAlphaWrap.cs:2854-2860)
        for g in range(nn):
            if find(g) != g:
                continue
            alive = any(find(j) == g and heads[j] < len(queues[j]) for j in range(nn))
            if not alive:
                return False, sum(len(q) for q in queues)
    return True, sum(len(q) for q in queues)


def _run_reference(adj, init_labels, boundary, corner, volume):
    labels = list(init_labels)
    n = len(adj)
    flips = []
    for _ in range(n + 1):
        comp, comp_label, comp_ext, comp_corner, comp_vol = _star_components(adj, labels, boundary, corner, volume)
        ext = any(comp_ext)
        n_in = sum(1 for l in comp_label if l == 1)
        n_out = sum(1 for l, e in zip(comp_label, comp_ext) if l == 0 and not e) + (1 if ext else 0)
        if n_in == 0 or n_out == 0 or (n_in == 1 and n_out == 1):
            return flips
        if n_out > 1:
            keep = -1
            if not ext:
                for cid in range(len(comp_label)):
                    if comp_label[cid] != 0:
                        continue
                    if (keep < 0 or (comp_corner[cid] and not comp_corner[keep])
                            or (comp_corner[cid] == comp_corner[keep] and comp_vol[cid] > comp_vol[keep])):
                        keep = cid
            for k in range(n):
                if labels[k] != 0:
                    continue
                stays = comp_ext[comp[k]] if ext else (comp[k] == keep)
                if not stays:
                    labels[k] = 1
                    flips.append(k)
            continue
        best = None
        for k in range(n):
            if labels[k] != 0:
                continue
            in_comps = {comp[m] for m in adj[k] if labels[m] == 1}
            if not in_comps:
                continue
            key = (corner[k], len(in_comps) < 2, volume[k], k)
            if best is None or key < best[0]:
                best = (key, k)
        if best is None:
            return flips
        labels[best[1]] = 1
        flips.append(best[1])
    return flips


def _run_fix_star(adj, init_labels, boundary, corner, volume, check_stale=True):
    """Full fix_star mirror including join_inside_components and general-step fallback."""
    labels = list(init_labels)
    n = len(adj)
    flips = []
    reattach_work = 0
    for _ in range(n + 1):
        comp, comp_label, comp_ext, comp_corner, comp_vol = _star_components(adj, labels, boundary, corner, volume)
        ext = any(comp_ext)
        n_in = sum(1 for l in comp_label if l == 1)
        n_out = sum(1 for l, e in zip(comp_label, comp_ext) if l == 0 and not e) + (1 if ext else 0)
        if n_in == 0 or n_out == 0 or (n_in == 1 and n_out == 1):
            return flips, reattach_work
        if n_out > 1:
            keep = -1
            if not ext:
                for cid in range(len(comp_label)):
                    if comp_label[cid] != 0:
                        continue
                    if (keep < 0 or (comp_corner[cid] and not comp_corner[keep])
                            or (comp_corner[cid] == comp_corner[keep] and comp_vol[cid] > comp_vol[keep])):
                        keep = cid
            for k in range(n):
                if labels[k] != 0:
                    continue
                stays = comp_ext[comp[k]] if ext else (comp[k] == keep)
                if not stays:
                    labels[k] = 1
                    flips.append(k)
            continue
        outside_comps = sum(1 for l in comp_label if l == 0)
        all_ext = all(comp_ext[cid] for cid, l in enumerate(comp_label) if l == 0)
        if not (n_in > 1 and (outside_comps == 1 or all_ext)):
            return flips, reattach_work
        # join_inside_components
        uf = list(range(len(comp_label)))

        def find(a):
            while uf[a] != a:
                uf[a] = uf[uf[a]]
                a = uf[a]
            return a

        def cand_key(k):
            roots = {find(comp[m]) for m in adj[k] if labels[m] == 1}
            if not roots:
                return None
            return (corner[k], len(roots) < 2, volume[k], k)

        tree_par = [-2] * n
        if all_ext:
            q = []
            for k in range(n):
                if labels[k] == 0 and boundary[k]:
                    tree_par[k] = -1
                    q.append(k)
            for u in q:
                for m in adj[u]:
                    if labels[m] == 0 and tree_par[m] == -2:
                        tree_par[m] = u
                        q.append(m)

        ver = [0] * n
        heap = []
        out_cnt = sum(1 for l in labels if l == 0)
        for k in range(n):
            if labels[k] == 0:
                key = cand_key(k)
                if key is not None:
                    heapq.heappush(heap, (key, 0))

        def flip_cell(k):
            nonlocal n_in
            labels[k] = 1
            flips.append(k)
            root_in = -1
            distinct = 0
            nbrs = []
            for m in adj[k]:
                if labels[m] == 1:
                    r = find(comp[m])
                    if root_in < 0:
                        root_in = r
                        distinct = 1
                    elif r != root_in:
                        uf[r] = root_in
                        distinct += 1
                else:
                    nbrs.append(m)
            if root_in < 0:
                root_in = len(uf)
                uf.append(root_in)
            comp[k] = root_in
            n_in = n_in + 1 - distinct
            return nbrs

        fallback = False
        while True:
            best = -1
            while heap:
                key, v = heapq.heappop(heap)
                k = key[3]
                if v != ver[k] or labels[k] != 0:
                    continue
                now = cand_key(k)
                if now is None:
                    continue
                if check_stale and (now[0] != key[0] or now[1] != key[1]):
                    heapq.heappush(heap, (now, v))
                    continue
                best = k
                break
            if best < 0:
                return flips, reattach_work
            nbrs = flip_cell(best)
            out_cnt -= 1
            ver[best] += 1
            if out_cnt == 0:
                return flips, reattach_work
            if all_ext:
                det = [m for m in adj[best] if labels[m] == 0 and tree_par[m] == best]
                for u in det:
                    for m in adj[u]:
                        if labels[m] == 0 and tree_par[m] == u:
                            det.append(m)
                det_set = set(det)
                att = []
                att_set = set()
                for u in det:
                    for m in adj[u]:
                        if labels[m] == 0 and m not in det_set and m not in att_set:
                            tree_par[u] = m
                            att.append(u)
                            att_set.add(u)
                            break
                for u in att:
                    for m in adj[u]:
                        if labels[m] == 0 and m in det_set and m not in att_set:
                            tree_par[m] = u
                            att.append(m)
                            att_set.add(m)
                reattach_work += len(att)
                dead = sorted(u for u in det if u not in att_set)
                if dead:
                    if len(dead) == out_cnt:
                        fallback = True
                        break
                    for dk in dead:
                        flip_cell(dk)
                        ver[dk] += 1
                        out_cnt -= 1
            else:
                stayed, _ = _outside_stays_connected_work(adj, labels, best, nbrs)
                if not stayed:
                    fallback = True
                    break
            if n_in == 1:
                return flips, reattach_work
            for m in nbrs:
                if labels[m] == 0:
                    ver[m] += 1
                    key = cand_key(m)
                    if key is not None:
                        heapq.heappush(heap, (key, ver[m]))
        if not fallback:
            return flips, reattach_work
    return flips, reattach_work


def _max_star_facets(adj, boundary):
    return max(len(adj[u]) + int(boundary[u]) for u in range(len(adj)))


def replay_simulations():
    replays = []

    # 1. Stale Joins demotion + 2-cell out-of-order deadCells (distinct == 0 then distinct == 3),
    #    strictly within the 3-star-facet bound len(adj[u]) + int(boundary[u]) <= 3 for every cell:
    #    - 0, 1, 2, 10 = four Inside components (A, B, C, D).
    #    - 3 (vol 1.0) bridges {A, B} -> flips first; 4 (vol 2.0) also bridges {A, B} -> becomes STALE!
    #    - 5 (vol 3.0) bridges {B, C} -> beats demoted 4 (dropping stale check wrongly takes 4 before 5!).
    #    - 8 (bnd, vol 1.5, Joins=False) bridges {C, 7} -> flips next, cutting off 2-cell dead piece [6, 7]:
    #      6 (adj=[7], no inside neighbour yet -> distinct=0) flips BEFORE 7 (adj=[8, 6, 10] -> distinct=3),
    #      merging all remaining components into nIn == 1.
    adj = [
        [3, 4],        # 0: Inside A (2 facets)
        [3, 4, 5],     # 1: Inside B (3 facets)
        [5, 8],        # 2: Inside C (2 facets)
        [0, 1, 9],     # 3: Outside, bridges {A, B}, vol 1.0 (3 facets)
        [0, 1, 9],     # 4: Outside, bridges {A, B}, vol 2.0 -> stale after 3 flips (3 facets)
        [1, 2, 11],    # 5: Outside, bridges {B, C}, vol 3.0 (3 facets)
        [7],           # 6: Outside, isolated deadCell (distinct == 0 when flipped first!) (1 facet)
        [8, 6, 10],    # 7: Outside, connector deadCell (distinct == 3 when flipped second!) (3 facets)
        [2, 7],        # 8: Outside, bnd, vol 1.5 -> flips after 5, cuts off [6, 7] (2 + 1 = 3 facets)
        [3, 4, 11],    # 9: Outside, connects 3 & 4 to bnd cell 11 (3 facets)
        [7],           # 10: Inside D (1 facet)
        [5, 9],        # 11: Outside, bnd (2 + 1 = 3 facets)
    ]
    labels = [1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0]
    boundary = [False, False, False, False, False, False, False, False, True, False, False, True]
    corner = [False] * 12
    volume = [1.0, 1.0, 1.0, 1.0, 2.0, 3.0, 10.0, 10.0, 1.5, 10.0, 1.0, 10.0]
    max_f1 = _max_star_facets(adj, boundary)
    ref_flips = _run_reference(adj, labels, boundary, corner, volume)
    opt_flips, _ = _run_fix_star(adj, labels, boundary, corner, volume, check_stale=True)
    bug_flips, _ = _run_fix_star(adj, labels, boundary, corner, volume, check_stale=False)
    replays.append(dict(
        name='stale_key_and_out_of_order_dead_cells_equivalence',
        max_star_facets=max_f1,
        ref_flips=ref_flips,
        opt_flips=opt_flips,
        bug_flips=bug_flips,
        passed=(max_f1 <= 3 and ref_flips == opt_flips == [3, 5, 8, 6, 7] and bug_flips != ref_flips),
    ))

    # 2. Degree-3-star-facet comb star quadratic reattachment witness (#1607):
    #    Every cell satisfies len(adj[u]) + int(boundary[u]) <= 3 (valid 3D Delaunay vertex star!).
    #    K = 20 boundary roots b_0..b_{K-1} (each b_0..b_{K-2} has 1 bnd facet + s_i + a_i = 3 facets),
    #    spine s_0..s_{K-1} (each degree <= 3), tail t_0..t_{L-1} (L = 20) on s_0, inside chain A
    #    (a_0..a_{K-2}), and inside cell B attached to s_{K-1} (vol 999 -> flips after b_0..b_{K-2},
    #    cutting off the remaining spine/tail as deadCells and joining A and B).
    #    Flipping b_0..b_{K-2} (k = 19 moves) detaches and re-BFSes L + t cells on move t (t = 1..k),
    #    giving exact reattachment work W = k * L + k * (k + 1) / 2 = 570.
    K_roots, L_tail = 20, 20
    n_out = 2 * K_roots + L_tail
    n_in_a = K_roots - 1
    N_comb = n_out + n_in_a + 1
    c_adj = [[] for _ in range(N_comb)]

    def add_edge(u, v):
        c_adj[u].append(v)
        c_adj[v].append(u)

    for i in range(K_roots):
        add_edge(i, K_roots + i)  # b_i -- s_i
        if i + 1 < K_roots:
            add_edge(K_roots + i, K_roots + i + 1)  # s_i -- s_{i+1}
    tail0 = 2 * K_roots
    add_edge(K_roots, tail0)  # s_0 -- t_0
    for j in range(L_tail - 1):
        add_edge(tail0 + j, tail0 + j + 1)
    in_a0 = n_out
    in_b = N_comb - 1
    for i in range(n_in_a):
        add_edge(i, in_a0 + i)  # b_i -- a_i (bnd + s_i + a_i = 3 star facets!)
        if i + 1 < n_in_a:
            add_edge(in_a0 + i, in_a0 + i + 1)
    add_edge(2 * K_roots - 1, in_b)  # s_{K-1} -- in_b (b_{K-1} + s_{K-2} + in_b = 3 star facets!)

    c_bnd = [i < K_roots for i in range(n_out)] + [False] * (n_in_a + 1)
    max_f2 = _max_star_facets(c_adj, c_bnd)
    c_vol = [1000.0] * N_comb
    for i in range(K_roots - 1):
        c_vol[i] = float(i + 1)
    c_vol[2 * K_roots - 1] = 999.0  # s_{K-1} flips on move 20 after all 19 roots b_0..b_{K-2}
    c_labels = [0] * n_out + [1] * (n_in_a + 1)
    c_corner = [False] * N_comb
    comb_ref = _run_reference(c_adj, c_labels, c_bnd, c_corner, c_vol)
    comb_opt, comb_reattach = _run_fix_star(c_adj, c_labels, c_bnd, c_corner, c_vol, check_stale=True)
    k_moves = K_roots - 1
    expected_reattach = k_moves * L_tail + k_moves * (k_moves + 1) // 2  # 570 exact
    replays.append(dict(
        name='superlinear_comb_star_reattach_replay',
        max_star_facets=max_f2,
        prefix_moves=k_moves,
        total_flips=len(comb_opt),
        reattach_work=comb_reattach,
        expected_reattach=expected_reattach,
        matches_reference=(comb_ref == comb_opt),
        passed=(max_f2 <= 3 and comb_ref == comb_opt and comb_reattach == expected_reattach),
    ))

    # 3. Non-exterior lockstep BFS:
    #    (a) cycle of M = 40 outside cells (2 star-facets each): 1 non-splitting move visits M - 1 = 39 cells.
    #    (b) unbalanced split (1-cell leaf vs 38-cell tail): group-exhaustion early exit stops after <= 4 cells!
    M_cyc = 40
    cyc_adj = [[(i - 1) % M_cyc, (i + 1) % M_cyc] for i in range(M_cyc)]
    cyc_labels = [0] * M_cyc
    cyc_labels[0] = 1
    stayed_conn, bfs_work = _outside_stays_connected_work(cyc_adj, cyc_labels, 0, cyc_adj[0])
    # Unbalanced split: 0 connects to leaf 1 and to path 2..39
    split_adj = [[] for _ in range(M_cyc)]
    split_adj[0] = [1, 2]
    split_adj[1] = [0]
    split_adj[2] = [0, 3]
    for i in range(3, M_cyc - 1):
        split_adj[i] = [i - 1, i + 1]
    split_adj[M_cyc - 1] = [M_cyc - 2]
    split_labels = [0] * M_cyc
    split_labels[0] = 1
    split_conn, split_work = _outside_stays_connected_work(split_adj, split_labels, 0, split_adj[0])
    replays.append(dict(
        name='non_exterior_cycle_and_early_exit_split_replay',
        cycle_size=M_cyc,
        stayed_connected=stayed_conn,
        bfs_work=bfs_work,
        split_connected=split_conn,
        split_early_exit_work=split_work,
        passed=(stayed_conn and bfs_work == M_cyc - 1 and (not split_conn) and split_work <= 4),
    ))
    return replays


def run(timeout_ms=120000):
    source_contract(WRAP_SOURCE, FIX_STAR_ANCHORS)
    obligations = (
        candidate_order_obligations()
        + lazy_heap_monotonicity_obligations()
        + spanning_forest_cutoff_obligations()
        + outside_stays_connected_obligations()
        + component_count_and_complexity_obligations()
    )
    rows = [
        check_expr(name, prem, goal, expected, timeout_ms, logic)
        for name, prem, goal, expected, logic in obligations
    ]
    replays = replay_simulations()
    out = report(rows, replays=replays)
    out['passed'] = out['passed'] and all(r['passed'] for r in replays)
    return out


if __name__ == '__main__':
    sys.exit(main(run))
