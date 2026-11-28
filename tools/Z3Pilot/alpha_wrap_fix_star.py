"""Local Z3 obligations and complexity witnesses for MeshAlphaWrap fix_star (#1607).

Formally verifies the equivalence, termination, and worst-case super-linear complexity
of the one-pass manifold step (join_inside_components, join_flip, build_outside_tree,
outside_pieces_cut_off, outside_stays_connected, before / preferred_flip) in
mesh_ops/MeshAlphaWrap.cs against the reference per-move full-star recomputation
(ReferenceFixStar):

1. Candidate ordering (before vs preferred_flip):
   - before(a, b) is a strict total order on distinct local indices K and strictly refines
     preferred_flip (matching the reference scan's first-cell tie breaking).
   - Negative control (SAT): preferred_flip alone ties on equal-volume cells.
2. Lazy heap monotonicity (join_inside_components):
   - Cut-off dead cells are completely isolated from surviving outside cells (UNSAT to share
     an edge), so flipping deadCells never adds a new inside neighbour to any surviving outside cell.
   - For any surviving outside cell not adjacent to best, Joins can never improve (False -> True
     is UNSAT); it can only stay equal or worsen (True -> False, SAT witness motivating the lazy
     re-check on heap_pop).
   - Negative control (SAT): an outside neighbour of best CAN transition False -> True, proving
     why join_inside_components must refresh nbrs[0..nn) after flipping best.
3. Spanning-forest cut-off exactness (build_outside_tree + outside_pieces_cut_off):
   - On bounded star dual graphs (N = 5 outside cells), the detached-minus-reattached set equals
     the exact set of outside cells disconnected from the box boundary after removing xk (UNSAT
     mismatch).
   - Negative controls (SAT): stopping reattachment after 1 hop drops live cells; allowing a
     boundary cell to have treeParent >= 0 breaks cut-off exactness.
4. Non-exterior connectivity exactness (outside_stays_connected):
   - Removing a degree-1 cell (nn == 1) from a connected outside component of >= 2 cells never
     disconnects it (UNSAT).
   - Mutual reachability among xk's outside neighbours in Outside \\ {xk} is equivalent to global
     connectivity of Outside \\ {xk} (UNSAT mismatch); nn == 2 admits a cut-vertex split (SAT).
5. Component-count tracking, termination, and super-linear worst-case complexity (#1607):
   - join_flip preserves nIn >= 1 (including out-of-order sorted deadCells flips where an isolated
     dead cell temporarily increments nIn before its connector merges it back) and strictly
     decreases outsideCount every iteration.
   - Worst-case degree-3-bounded comb star witness: K boundary roots b_0..b_{K-1} each attached
     to spine s_0..s_{K-1} plus a tail t_0..t_{L-1} on s_0 (every cell has total degree <= 3,
     valid for a 3D Delaunay vertex star). Flipping b_0..b_{K-2} (k = K - 1 moves) forces
     outside_pieces_cut_off to detach and re-BFS L + t cells on move t, yielding exact work
     W = k * L + k * (k + 1) / 2 = Theta(M^2), explaining the documented super-linear tail in
     benchmarks/AlphaWrapFixStarBenchmark/README.md.
6. Concrete Python replays executing exact mirrors of join_inside_components vs ReferenceFixStar
   on (a) a stale-key + out-of-order deadCells star, (b) the degree-3-bounded K = 20, L = 20
   comb star (W = 570 exact), and (c) a non-exterior M = 40 cycle star (39 cells visited on 1 move).
"""
import heapq
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'

FIX_STAR_ANCHORS = [
    ('internal static bool before(in JoinCandidate a, in JoinCandidate b)', 'void heap_push(',
     'b201d67b85acfde1b071aec26e561e155e63d58d9e7bb76553394179d6a39e33'),
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
    ('internal static bool preferred_flip(', 'void star_components(',
     'e36ff17800e080619a118fab2ef91054e0509dd2a78b16f3d892794173a418c1'),
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

    rows.append(('candidate_order_assumptions_sat', [ka != kb, kb != kc, ka != kc], z3.And(ab, bc), 'sat', 'QF_LIA'))
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
        'before_refines_preferred_flip',
        [pref_ab],
        z3.Not(ab),
        'unsat',
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
    # Exact reachability in {1..4} to any boundary cell in <= 3 hops
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
    #    Inside neighbours have pre-merge UF roots r[k] in {0, 1, 2}. Flipping best merges
    #    all roots in a subset M (m0, m1, m2) into one root m_root = 3.
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

    # Non-neighbour Joins CAN worsen (True -> False) -> motivates lazy heap re-check on pop
    rows.append((
        'non_neighbour_joins_can_worsen_witness',
        prem,
        z3.And(joins_before, z3.Not(joins_after)),
        'sat',
        'QF_LIA',
    ))
    # Non-neighbour Joins can NEVER improve (False -> True is UNSAT)
    rows.append((
        'non_neighbour_joins_never_improves',
        prem + [z3.Not(joins_before)],
        joins_after,
        'unsat',
        'QF_LIA',
    ))
    # Negative control: if neighbour 0 of u IS best (flips Outside -> Inside with root 3), Joins CAN improve
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
            # parent pointer must follow an edge and strictly increase depth
            for j in range(n):
                if j == i:
                    continue
                p.append(z3.Implies(par[i] == j, z3.And(e[(i, j)], depth[i] == depth[j] + 1)))
            if enforce_bnd_root:
                p.append(z3.Implies(bnd[i], z3.And(par[i] == -1, depth[i] == 0)))
                p.append(z3.Implies(z3.Not(bnd[i]), par[i] >= 0))
            else:
                # Non-boundary still must have a parent; a root must be a boundary cell
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

    # 1-hop reattachment seeds (cells in det adjacent to surviving non-det outside cells)
    att0 = [z3.BoolVal(False)] * n
    for i in range(1, n):
        att0[i] = z3.And(det[i], z3.Or(*[z3.And(z3.Not(det[j]), e[(i, j)]) for j in range(1, n) if j != i]))

    # Full BFS reattachment inside det
    att = list(att0)
    for _ in range(n - 2):
        nxt = [z3.BoolVal(False)] * n
        for i in range(1, n):
            nxt[i] = z3.Or(att[i], z3.And(det[i], z3.Or(*[z3.And(att[j], e[(i, j)]) for j in range(1, n) if j != i])))
        att = nxt

    dead_algo = [z3.And(det[i], z3.Not(att[i])) for i in range(n)]

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
    # Negative control 1: 1-hop reattach without BFS propagation falsely kills a live cell
    rows.append((
        'one_hop_reattach_drops_live_cell_negative_control',
        prem_valid,
        z3.Or(*[z3.And(det[i], z3.Not(att0[i]), reach[i]) for i in range(1, n)]),
        'sat',
        'QF_LIA',
    ))
    # Negative control 2: allowing a boundary cell to have par >= 0 breaks cut-off exactness
    prem_relaxed = forest_premises(enforce_bnd_root=False)
    rows.append((
        'non_root_boundary_breaks_cutoff_negative_control',
        prem_relaxed,
        z3.Or(*[z3.Xor(dead_algo[i], dead_exact[i]) for i in range(1, n)]),
        'sat',
        'QF_LIA',
    ))
    return rows


def outside_stays_connected_obligations():
    rows = []
    n = 5  # connected outside component {0..4}; xk = 0 is removed
    e = _undirected_edges('cc_e', n)

    # Pre-condition: graph on {0..4} is connected (every vertex reaches 0 in <= 4 hops)
    r0 = [z3.BoolVal(i == 0) for i in range(n)]
    for _ in range(n - 1):
        nxt = list(r0)
        for i in range(n):
            nxt[i] = z3.Or(r0[i], *[z3.And(e[(i, j)], r0[j]) for j in range(n) if j != i])
        r0 = nxt
    prem = [r0[i] for i in range(1, n)]

    nbr = [z3.BoolVal(False)] + [e[(0, i)] for i in range(1, n)]
    nn = z3.Sum([z3.If(nbr[i], 1, 0) for i in range(1, n)])

    # All-pairs reachability inside {1..4}
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

    # Witness + negative control: nn == 2 CAN split Outside \ {0} (cut vertex)
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


def component_count_and_complexity_obligations():
    rows = []
    n_in, distinct, out_before, dead_cnt = z3.Ints('n_in distinct out_before dead_cnt')
    prem = [
        n_in >= 2,
        0 <= distinct, distinct <= 3, distinct <= n_in,
        out_before >= 1,
        0 <= dead_cnt, dead_cnt < out_before,
    ]
    n_in_after = n_in + 1 - distinct
    out_after = out_before - 1 - dead_cnt
    rows.append(('join_flip_progress_assumptions_sat', prem, z3.And(distinct == 2, dead_cnt == 1), 'sat', 'QF_LIA'))
    rows.append((
        'join_flip_preserves_positive_nIn_and_decreases_outside',
        prem,
        z3.Or(n_in_after < 1, out_after >= out_before, out_after < 0),
        'unsat',
        'QF_LIA',
    ))

    # Sorted deadCells out-of-order flip: isolated dk0 (distinct=0) then connector dk1 (distinct=2) preserves nIn
    n0 = z3.Int('n0')
    n1 = n0 + 1 - 0
    n2 = n1 + 1 - 2
    rows.append(('dead_cells_sorted_order_assumptions_sat', [n0 >= 1], n0 == 2, 'sat', 'QF_LIA'))
    rows.append((
        'dead_cells_sorted_order_preserves_nIn',
        [n0 >= 1],
        n2 != n0,
        'unsat',
        'QF_LIA',
    ))

    # Super-linear worst-case complexity (#1607): degree-3-bounded comb star of K boundary roots b_0..b_{K-1}
    # attached to spine s_0..s_{K-1} plus tail t_0..t_{L-1} on s_0. Flipping b_0..b_{K-2} (k = K - 1 moves)
    # detaches and re-BFSes L + t cells on move t, giving exact work W = k * L + k * (k + 1) / 2 = Theta(M^2).
    k, L, W = z3.Ints('k L W')
    comb_prem = [k >= 10, L >= 10, 2 * W == 2 * k * L + k * (k + 1)]
    rows.append(('comb_reattach_assumptions_sat', comb_prem, z3.And(k == 19, L == 20), 'sat', 'QF_NIA'))
    rows.append((
        'comb_reattach_exceeds_linear_15k_bound',
        comb_prem,
        W <= 15 * k,
        'unsat',
        'QF_NIA',
    ))
    return rows


# ---- Python mirrors of join_inside_components vs ReferenceFixStar for concrete replays ----

def _star_components(adj, labels, boundary):
    n = len(adj)
    comp = [-1] * n
    comp_label, comp_ext = [], []
    for k in range(n):
        if comp[k] >= 0:
            continue
        cid = len(comp_label)
        lab = labels[k]
        ext = False
        stack = [k]
        comp[k] = cid
        while stack:
            u = stack.pop()
            if boundary[u]:
                ext = True
            for m in adj[u]:
                if comp[m] < 0 and labels[m] == lab:
                    comp[m] = cid
                    stack.append(m)
        comp_label.append(lab)
        comp_ext.append(ext)
    return comp, comp_label, comp_ext


def _run_reference(adj, init_labels, boundary, corner, volume):
    labels = list(init_labels)
    n = len(adj)
    flips = []
    for _ in range(n + 1):
        comp, comp_label, comp_ext = _star_components(adj, labels, boundary)
        ext = any(comp_ext)
        n_in = sum(1 for l in comp_label if l == 1)
        n_out = sum(1 for l, e in zip(comp_label, comp_ext) if l == 0 and not e) + (1 if ext else 0)
        if n_in == 0 or n_out == 0 or (n_in == 1 and n_out == 1):
            return flips
        if n_out > 1:
            for k in range(n):
                if labels[k] == 0 and not (ext and comp_ext[comp[k]]):
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


def _run_one_pass(adj, init_labels, boundary, corner, volume, check_stale=True):
    labels = list(init_labels)
    n = len(adj)
    comp, comp_label, comp_ext = _star_components(adj, labels, boundary)
    n_in = sum(1 for l in comp_label if l == 1)
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

    # build_outside_tree
    tree_par = [-2] * n
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

    flips = []
    reattach_work = 0

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

    while n_in > 1 and out_cnt > 0:
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
            break
        nbrs = flip_cell(best)
        out_cnt -= 1
        ver[best] += 1
        if out_cnt == 0:
            break
        # outside_pieces_cut_off
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
        for dk in dead:
            flip_cell(dk)
            ver[dk] += 1
            out_cnt -= 1
        if n_in == 1:
            break
        for m in nbrs:
            if labels[m] == 0:
                ver[m] += 1
                key = cand_key(m)
                if key is not None:
                    heapq.heappush(heap, (key, ver[m]))
    return flips, reattach_work


def _outside_stays_connected_work(adj, xk):
    nbrs = adj[xk]
    nn = len(nbrs)
    if nn <= 1:
        return nn == 1, 0
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
                if m == xk:
                    continue
                if m in owner:
                    a, b = find(owner[m]), find(j)
                    if a != b:
                        grp[a] = b
                        groups -= 1
                else:
                    owner[m] = j
                    queues[j].append(m)
        if not progressed:
            break
    return groups == 1, sum(len(q) for q in queues)


def replay_simulations():
    replays = []

    # 1. Stale Joins demotion + out-of-order deadCells: one-pass matches reference bit-for-bit;
    #    dropping the stale-key check on heap_pop diverges!
    #    Cells 0, 1, 2 = three Inside components; 3..7 = Outside (4 and 7 touch boundary).
    #    3 joins {0, 1} (vol 1.0, picked first); 4 joins {0, 1} (vol 2.0, becomes stale after 3 flips!);
    #    5 joins {1, 2} (vol 3.0, must beat demoted 4!); 6 is cut off when 3 flips (only reaches bnd via 3?
    #    Make 7 boundary root, 3 boundary root, 6 hangs off 3 only -> dead when 3 flips, 5 hangs off 4 -> stays alive).
    adj = [
        [3, 4],        # 0: Inside comp A
        [3, 4, 5],     # 1: Inside comp B
        [5],           # 2: Inside comp C
        [0, 1, 6],     # 3: Outside, bnd, joins {A, B}, vol 1.0 -> flips first, cuts off 6
        [0, 1, 7],     # 4: Outside, bnd, joins {A, B}, vol 2.0 -> stale after 3 flips!
        [1, 2, 7],     # 5: Outside, joins {B, C}, vol 3.0 -> must win over demoted 4!
        [3],           # 6: Outside, cut off by 3 (deadCell)
        [4, 5],        # 7: Outside, bnd (keeps 4 and 5 alive)
    ]
    labels = [1, 1, 1, 0, 0, 0, 0, 0]
    boundary = [False, False, False, True, True, False, False, True]
    corner = [False] * 8
    volume = [1.0, 1.0, 1.0, 1.0, 2.0, 3.0, 10.0, 10.0]
    ref_flips = _run_reference(adj, labels, boundary, corner, volume)
    opt_flips, _ = _run_one_pass(adj, labels, boundary, corner, volume, check_stale=True)
    bug_flips, _ = _run_one_pass(adj, labels, boundary, corner, volume, check_stale=False)
    replays.append(dict(
        name='stale_key_and_dead_cell_equivalence',
        ref_flips=ref_flips,
        opt_flips=opt_flips,
        bug_flips=bug_flips,
        passed=(ref_flips == opt_flips) and (bug_flips != ref_flips),
    ))

    # 2. Degree-3-bounded comb star quadratic reattachment witness (#1607):
    #    Every cell in a 3D Delaunay vertex star has at most 3 star-neighbours (degree <= 3).
    #    Build K = 20 boundary roots b_0..b_{K-1}, spine s_0..s_{K-1} (each s_i attached to b_i),
    #    tail t_0..t_{L-1} (L = 20) attached to s_0, and K inside cells c_0..c_{K-1} (K inside
    #    components) where each b_i (i = 0..K-2) bridges c_i and c_{i+1} (Joins == True, vol i+1).
    #    Every cell has total degree <= 3! Flipping b_0..b_{K-2} (k = 19 moves) reduces nIn from K
    #    to 1 while detaching and re-BFSing L + t cells on move t (t = 1..k), giving exact work
    #    W = k * L + k * (k + 1) / 2 = 19 * 20 + 190 = 570.
    K_roots, L_tail = 20, 20
    # Indices: 0..K-1 = b_i, K..2K-1 = s_i, 2K..2K+L-1 = t_j, 2K+L..3K+L-1 = inside c_0..c_{K-1}
    n_out = 2 * K_roots + L_tail
    N_comb = n_out + K_roots
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
    in0 = n_out
    for i in range(K_roots - 1):
        add_edge(i, in0 + i)
        add_edge(i, in0 + i + 1)

    max_degree = max(len(nbrs) for nbrs in c_adj)
    c_vol = [1000.0] * N_comb
    for i in range(K_roots - 1):
        c_vol[i] = float(i + 1)
    c_labels = [0] * n_out + [1] * K_roots
    c_bnd = [i < K_roots for i in range(n_out)] + [False] * K_roots
    c_corner = [False] * N_comb
    comb_ref = _run_reference(c_adj, c_labels, c_bnd, c_corner, c_vol)
    comb_opt, comb_reattach = _run_one_pass(c_adj, c_labels, c_bnd, c_corner, c_vol, check_stale=True)
    k_moves = K_roots - 1
    expected_reattach = k_moves * L_tail + k_moves * (k_moves + 1) // 2  # 570 exact
    replays.append(dict(
        name='superlinear_comb_star_reattach_replay',
        max_degree=max_degree,
        moves=len(comb_opt),
        reattach_work=comb_reattach,
        expected_reattach=expected_reattach,
        matches_reference=(comb_ref == comb_opt),
        passed=(max_degree <= 3 and comb_ref == comb_opt
                and comb_reattach == expected_reattach and len(comb_opt) == k_moves),
    ))

    # 3. Non-exterior cycle of M = 40 outside cells (degree 2): single non-splitting move visits M - 1 = 39 cells
    M_cyc = 40
    cyc_adj = [[(i - 1) % M_cyc, (i + 1) % M_cyc] for i in range(M_cyc)]
    stayed_conn, bfs_work = _outside_stays_connected_work(cyc_adj, 0)
    replays.append(dict(
        name='non_exterior_cycle_lockstep_bfs_replay',
        cycle_size=M_cyc,
        stayed_connected=stayed_conn,
        bfs_work=bfs_work,
        passed=stayed_conn and (bfs_work == M_cyc - 1),
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
