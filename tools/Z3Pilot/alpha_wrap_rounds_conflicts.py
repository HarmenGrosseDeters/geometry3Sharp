"""Local Z3 obligations and conflict-set tightness analysis for parallel Alpha Wrap rounds (#774).

Formally analyzes the parallel round reservation, serialization lemma, deferral bounds,
and candidate conflict-set relaxations of carve_rounds_loop (mesh_ops/MeshAlphaWrap.cs)
and PlanInsert / AssignInsertIds / ApplyInsert (comp_geom/Delaunay3.cs; see also
docs/design/ALPHA_WRAP_ROUNDS.md):

1. Current reservation soundness & serialization (carve_rounds_loop lines 989-1031):
   - Models all four selection-relevant gate classes: NOOP (Drop / Requeue / Rejected,
     R = {C, N}, W = empty), INSERT_REJECTED (R = {C, N} U Cav U Outer(Cav), W = empty),
     INSERT (non-empty Cav, R = W = {C, N} U Cav U Outer(Cav)), and CARVE (R = {C, N},
     W = {C}).
   - Greedy heap-order selection guarantees pairwise disjoint write sets AND no
     read-after-write R(g_k) cap W(g_j) == empty for all kept j < k (Serialization Lemma).
   - Even without pop-time roundInner[c] dedup, Step 3's round_marked(g.C) alone prevents
     two kept carvings from colliding on C (UNSAT; dropping round_marked(g.C) is SAT).
   - Carve (g0) followed by kept Insert (g1): g0's outside cell N0 CANNOT lie in g1's
     Cavity (UNSAT, because adj(C0, N0) puts C0 in Outer(Cav1) <= R1, clashing with W0={C0});
     N0 CAN lie in Outer(Cav1) (SAT witness), where ApplyInsert writes cn[4*N0 + slot] and
     Carve writes label[C0].
2. Livelock-freedom, real outcome accounting & worst-case hub deferrals:
   - Modeled directly from a 4-gate round selection (not circular integer assumptions):
     a round partitions live gates into kept (Insert/Carve/Requeue), consumed without
     re-queue (Drop/Rejected/RejectedInsert), and deferred (clashing or after point limit).
   - Because roundMark starts clear, roundGates[0] is never deferred -> kept + consumed >= 1
     and deferred <= live - 1 (deferred == live is UNSAT), while kept == 0 is reachable when
     roundGates[0] is consumed (SAT witness) and deferred == live - 1 is reachable on a hub
     conflict (SAT witness).
3. Candidate conflict-set tightness (Relaxations A, B, C, and A+C):
   - Relaxation A (W_A(Insert) = Cavity only, keeping R_A(Insert) = {C, N} U Cav U Outer(Cav)):
     * Between two kept Insertions: Cav1 cap (Cav2 U Outer(Cav2)) == empty, yet Outer(Cav1)
       and Outer(Cav2) can share an outer cell u (SAT witness). Binding u's 4 facet slots to
       distinct neighbour cell IDs u_nbr[0..3] proves ApplyInsert's written slots f1, f2 on u
       are ALWAYS distinct (f1 == f2 is UNSAT; dropping kept_A makes f1 == f2 SAT).
     * Between Insert followed by Carve(u) where u in Outer(Cav1): kept under Relaxation A
       (SAT), where post-round label[nc] == Inside and label[u] == Outside cause BOTH
       compute_pushes(g_insert) and compute_pushes(g_carve) to push facet (nc, u); prevented
       under the current W(Insert) containing Outer(Cav) (UNSAT).
   - Relaxation B (dropping Outer(Cav) from BOTH R and W, allowing adjacent cavities):
     * Current R prevents Cav1 cap Outer(Cav2) != empty (UNSAT); Relaxation B admits adjacent
       cavities (SAT), where g1 overwrites/frees c1 while g2 writes cn[4*c1 + stale_facet].
   - Relaxation C (dropping N from R(carve)) vs combined A+C:
     * Under the CURRENT W(Insert) (which marks Outer(Cav)), dropping N from R(carve) ALONE
       still prevents N2 in Cav1 (UNSAT!), because adj(C2, N2) puts C2 in Cav1 U Outer(Cav1)
       <= W(g1), so round_marked(C2) still defers g2! It only admits Carve(C2, N2) with
       N2 in Outer(Cav1) (SAT witness, where N2 stays Outside with unchanged vertices).
     * Combining Relaxation A + C (dropping Outer(Cav) from W(Insert) AND N from R(carve))
       IS REFUTED (SAT): N2 in Cav1 with C2 in Outer(Cav1) is left unmarked, so g2 carves C2
       even though g1 replaced N2 with an Inside cell, breaking serialization.
4. Concrete 4-facet-per-tetrahedron Python replays running real pop-time roundInner dedup,
   selection, ApplyInsert slot updates, label writes, and compute_pushes.
"""
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'
DT_SOURCE = 'comp_geom/Delaunay3.cs'

WRAP_ANCHORS = [
    ('bool round_marked(int c)', 'void round_for(',
     '6592a9d5d3054eb4a199e2d2428e615e3a2c7e06ef31e6fc28d8790a5352b0fb'),
    ('if (++roundMarkValue == int.MaxValue)', 'int window = Math.Max(1, Math.Min(RoundWindow,',
     '1d5486946c96fd68d2686fd3d77a10ad3b30a82529707346618eb9820aa62d18'),
    ('int window = Math.Max(1, Math.Min(RoundWindow,', 'if (roundGates.Count == 0) {',
     '83c670208d5b99fa42adc86134c8e3a3817c99f5f41c3e6439aa9dbabc8b4f64'),
    ('for (int k = 0; k < roundGates.Count; ++k) {', 'if (cpuEval > 0) {',
     'd463d5d104437e37a2160c8bc99e0f1f91b9910c27146057c33890c2ac558a49'),
    ('void apply_gate(RoundGate g)', 'void add_push(RoundGate g, int c, int i)',
     '9b7df9d91d11c19f1209a135dc7be494db3e3073ab5ab466d9fa143d9909d707'),
]

DT_ANCHORS = [
    ('// the read set: the cavity and every cell adjacent to it', 'internal void AssignInsertIds(InsertPlan plan)',
     'b9f41903ebb534824b3ab766d5629171f2d8ee612686e45dd75687c7a0033bb6'),
    ('internal void AssignInsertIds(InsertPlan plan)', 'internal void ApplyInsert(InsertPlan plan)',
     'e35eb1673995bb5e3923e73a8261d4d2525f330b993e826dd7f613bcbdc0d19e'),
    ('internal void ApplyInsert(InsertPlan plan)', 'public string Validate(bool checkDelaunay)',
     '29cf6bd012efcd513c594d6a3970643f869fc9ba09871b5b3814a207c7f843ad'),
]

# Selection-relevant gate classes (matching MeshAlphaWrap.cs:867,992-1030)
KIND_NOOP = 0             # Drop / Requeue / GateRejected: R = {C, N}, W = empty
KIND_INSERT = 1           # GateInsert (succeeded):        R = W = {C, N} U Cav U Outer(Cav)
KIND_CARVE = 2            # GateCarve:                     R = {C, N}, W = {C}
KIND_INSERT_REJECTED = 3  # GateInsert (plan rejected):    R = {C, N} U Cav U Outer(Cav), W = empty


def _build_round_model(prefix, n_cells, n_gates, enforce_pop_dedup=True):
    """Build symmetric cell adjacency, gate variables, and Outer(Cavity) predicates."""
    adj = {}
    for u in range(n_cells):
        for v in range(u + 1, n_cells):
            b = z3.Bool(f'{prefix}_adj_{u}_{v}')
            adj[(u, v)] = b
            adj[(v, u)] = b

    kind = [z3.Int(f'{prefix}_kind_{g}') for g in range(n_gates)]
    c_cell = [z3.Int(f'{prefix}_c_{g}') for g in range(n_gates)]
    n_cell = [z3.Int(f'{prefix}_n_{g}') for g in range(n_gates)]
    in_cav = [[z3.Bool(f'{prefix}_cav_{g}_{u}') for u in range(n_cells)] for g in range(n_gates)]
    # Start-of-round cell label: True = Inside, False = Outside
    lbl0 = [z3.Bool(f'{prefix}_lbl0_{u}') for u in range(n_cells)]

    prem = []
    for g in range(n_gates):
        prem.append(z3.And(0 <= kind[g], kind[g] <= 3))
        prem.append(z3.And(0 <= c_cell[g], c_cell[g] < n_cells))
        prem.append(z3.And(0 <= n_cell[g], n_cell[g] < n_cells))
        prem.append(c_cell[g] != n_cell[g])
        # Accepted insertions have non-empty cavity (c0 in cavity, Delaunay3.cs:715)
        prem.append(z3.Implies(kind[g] == KIND_INSERT, z3.Or(*in_cav[g])))
        # Every live gate's C starts Inside, N starts Outside, and C shares a facet with N
        for u in range(n_cells):
            prem.append(z3.Implies(c_cell[g] == u, lbl0[u]))
            prem.append(z3.Implies(n_cell[g] == u, z3.Not(lbl0[u])))
            for v in range(n_cells):
                if u != v:
                    prem.append(z3.Implies(z3.And(c_cell[g] == u, n_cell[g] == v), adj[(u, v)]))
    if enforce_pop_dedup:
        for g1 in range(n_gates):
            for g2 in range(g1 + 1, n_gates):
                prem.append(c_cell[g1] != c_cell[g2])

    in_outer = []
    for g in range(n_gates):
        og = []
        for u in range(n_cells):
            has_cav_nbr = z3.Or(*[z3.And(in_cav[g][v], adj[(u, v)]) for v in range(n_cells) if v != u])
            og.append(z3.And(z3.Not(in_cav[g][u]), has_cav_nbr))
        in_outer.append(og)

    return adj, kind, c_cell, n_cell, in_cav, in_outer, lbl0, prem


def _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, n_cells,
                        mark_outer_on_insert=True, check_outer_on_insert=True,
                        check_n_on_carve=True, check_c_on_carve=True):
    """Build R[g][u], W[g][u], and greedy kept[g] expressions for a conflict policy."""
    n_gates = len(kind)
    R, W = [], []
    for g in range(n_gates):
        rg, wg = [], []
        for u in range(n_cells):
            c_hit = (c_cell[g] == u)
            n_hit = (n_cell[g] == u)
            cn_hit = z3.Or(c_hit, n_hit)
            plan_read = z3.Or(in_cav[g][u], in_outer[g][u]) if check_outer_on_insert else in_cav[g][u]
            ins_r = z3.Or(cn_hit, plan_read)
            carve_r = z3.Or(c_hit if check_c_on_carve else z3.BoolVal(False),
                            n_hit if check_n_on_carve else z3.BoolVal(False))
            r_u = z3.If(
                z3.Or(kind[g] == KIND_INSERT, kind[g] == KIND_INSERT_REJECTED), ins_r,
                z3.If(kind[g] == KIND_CARVE, carve_r, cn_hit),
            )
            ins_w = z3.Or(cn_hit, in_cav[g][u], in_outer[g][u]) if mark_outer_on_insert else in_cav[g][u]
            w_u = z3.If(
                kind[g] == KIND_INSERT, ins_w,
                z3.If(kind[g] == KIND_CARVE, c_hit, z3.BoolVal(False)),
            )
            rg.append(r_u)
            wg.append(w_u)
        R.append(rg)
        W.append(wg)

    marked = [[z3.BoolVal(False)] * n_cells]
    kept = []
    for g in range(n_gates):
        prev_m = marked[-1]
        clash = z3.Or(*[z3.And(R[g][u], prev_m[u]) for u in range(n_cells)])
        k_g = z3.Not(clash)
        kept.append(k_g)
        nxt_m = [z3.Or(prev_m[u], z3.And(k_g, W[g][u])) for u in range(n_cells)]
        marked.append(nxt_m)
    return R, W, kept


def current_reservation_obligations():
    """Verify soundness of carve_rounds_loop's current R/W sets on a 3-gate, 6-cell bounded model."""
    rows = []
    n_cells, n_gates = 6, 3
    adj, kind, c_cell, n_cell, in_cav, in_outer, _, prem = _build_round_model('cur', n_cells, n_gates, enforce_pop_dedup=True)
    R, W, kept = _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, n_cells)

    # 1a. Non-vacuity: round keeping two shared-outside carves and an accepted insertion (non-empty cavity)
    rows.append((
        'current_reservation_assumptions_sat',
        prem,
        z3.And(kept[0], kept[1], kept[2], kind[0] == KIND_CARVE, kind[1] == KIND_CARVE,
               n_cell[0] == n_cell[1], kind[2] == KIND_INSERT),
        'sat',
        'QF_LIA',
    ))

    # 1b. Pairwise disjoint write sets across all kept gates
    write_overlap = z3.Or(*[
        z3.And(kept[g1], kept[g2], W[g1][u], W[g2][u])
        for g1 in range(n_gates) for g2 in range(g1 + 1, n_gates) for u in range(n_cells)
    ])
    rows.append(('kept_writes_pairwise_disjoint', prem, write_overlap, 'unsat', 'QF_LIA'))

    # 1c. Serialization Lemma hypothesis: no kept gate (including INSERT_REJECTED) reads an earlier kept write
    read_after_write = z3.Or(*[
        z3.And(kept[g1], kept[g2], W[g1][u], R[g2][u])
        for g1 in range(n_gates) for g2 in range(g1 + 1, n_gates) for u in range(n_cells)
    ])
    rows.append(('kept_later_read_misses_earlier_write', prem, read_after_write, 'unsat', 'QF_LIA'))

    # 1d. Carve (g0) followed by kept Insert (g1): N0 CANNOT lie in Cav1 (UNSAT); N0 CAN lie in Outer(Cav1) (SAT)
    carve_ins_prem = prem + [kind[0] == KIND_CARVE, kind[1] == KIND_INSERT, kept[0], kept[1]]
    n0_in_cav1 = z3.Or(*[z3.And(n_cell[0] == u, in_cav[1][u]) for u in range(n_cells)])
    n0_in_outer1 = z3.Or(*[z3.And(n_cell[0] == u, in_outer[1][u]) for u in range(n_cells)])
    rows.append(('carve_then_insert_shared_outer_witness', carve_ins_prem, n0_in_outer1, 'sat', 'QF_LIA'))
    rows.append(('carve_then_insert_cannot_replace_shared_outside', carve_ins_prem, n0_in_cav1, 'unsat', 'QF_LIA'))

    # 1e. First gate of any non-empty round is ALWAYS kept/processed (never deferred)
    rows.append(('first_gate_never_deferred', prem, z3.Not(kept[0]), 'unsat', 'QF_LIA'))

    # 1f. Step 3 round_marked(g.C) prevents same-cell carve collisions even WITHOUT pop-time dedup
    _, nd_kind, nd_c, nd_n, nd_cav, nd_out, _, nd_prem = _build_round_model('nd', 4, 2, enforce_pop_dedup=False)
    _, _, nd_kept = _select_expressions(nd_kind, nd_c, nd_n, nd_cav, nd_out, 4, check_c_on_carve=True)
    _, _, nd_kept_bug = _select_expressions(nd_kind, nd_c, nd_n, nd_cav, nd_out, 4, check_c_on_carve=False)
    carve2_prem = nd_prem + [nd_kind[0] == KIND_CARVE, nd_kind[1] == KIND_CARVE]
    rows.append((
        'step3_mark_prevents_same_cell_carves_without_pop_dedup',
        carve2_prem + [nd_kept[0], nd_kept[1]],
        nd_c[0] == nd_c[1],
        'unsat',
        'QF_LIA',
    ))
    rows.append((
        'dropping_c_mark_allows_same_cell_carves_negative_control',
        carve2_prem + [nd_kept_bug[0], nd_kept_bug[1]],
        nd_c[0] == nd_c[1],
        'sat',
        'QF_LIA',
    ))

    # 1g. Negative control: checking only {C, N} (dropping Plan.Reads clash check on Insert) causes write-write race
    clash1_cn_only = z3.Or(*[
        z3.And(z3.Or(c_cell[1] == u, n_cell[1] == u), W[0][u]) for u in range(n_cells)
    ])
    rows.append((
        'dropping_plan_reads_clash_causes_write_race_negative_control',
        prem + [kind[0] == KIND_INSERT, kind[1] == KIND_INSERT, z3.Not(clash1_cn_only)],
        z3.Or(*[z3.And(in_cav[0][u], in_cav[1][u]) for u in range(n_cells)]),
        'sat',
        'QF_LIA',
    ))
    return rows


def deferral_and_progress_obligations():
    """Model a 4-gate round selection with real consumed/kept/deferred accounting (MeshAlphaWrap.cs:989-1031)."""
    rows = []
    n_cells, n_gates = 6, 4
    _, kind, c_cell, n_cell, in_cav, in_outer, _, prem = _build_round_model('prog', n_cells, n_gates, enforce_pop_dedup=True)
    _, _, not_clashed = _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, n_cells)

    # Match MeshAlphaWrap.cs:999-1030:
    # - GateDrop / GateRejected / rejected GateInsert -> consumed (neither in roundKept nor roundDeferred)
    # - GateRequeue / GateInsert (succeeded) / GateCarve -> added to roundKept
    # - clashing gate -> added to roundDeferred
    is_requeue = [z3.Bool(f'prog_req_{g}') for g in range(n_gates)]
    added_kept = [
        z3.And(not_clashed[g], z3.Or(
            kind[g] == KIND_INSERT,
            kind[g] == KIND_CARVE,
            z3.And(kind[g] == KIND_NOOP, is_requeue[g]),
        ))
        for g in range(n_gates)
    ]
    consumed = [
        z3.And(not_clashed[g], z3.Or(
            kind[g] == KIND_INSERT_REJECTED,
            z3.And(kind[g] == KIND_NOOP, z3.Not(is_requeue[g])),
        ))
        for g in range(n_gates)
    ]
    deferred = [z3.Not(not_clashed[g]) for g in range(n_gates)]

    kept_cnt = z3.Sum([z3.If(added_kept[g], 1, 0) for g in range(n_gates)])
    consumed_cnt = z3.Sum([z3.If(consumed[g], 1, 0) for g in range(n_gates)])
    def_cnt = z3.Sum([z3.If(deferred[g], 1, 0) for g in range(n_gates)])

    # Non-circular progress: derived directly from selection (no kept_cnt >= 1 assumption!)
    rows.append((
        'progress_consumed_zero_kept_witness',
        prem,
        z3.And(kept_cnt == 0, consumed_cnt >= 1),
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'round_strict_progress_and_accounting',
        prem,
        z3.Or(kept_cnt + consumed_cnt + def_cnt != n_gates, kept_cnt + consumed_cnt < 1, def_cnt >= n_gates),
        'unsat',
        'QF_LIA',
    ))
    # Hub deferral amplification witness: g0 inserts at a hub and defers all n_gates - 1 remaining gates
    rows.append((
        'hub_worst_case_w_minus_1_deferrals_witness',
        prem + [kind[0] == KIND_INSERT],
        def_cnt == n_gates - 1,
        'sat',
        'QF_LIA',
    ))
    return rows


def conflict_tightness_obligations():
    """Analyze candidate conflict-set relaxations A, B, C, and A+C against ApplyInsert & compute_pushes."""
    rows = []

    # ---- Relaxation A (Insert-Insert): W_A(Insert) = Cavity only, R_A(Insert) = {C, N} U Cav U Outer(Cav) ----
    # Model 6 cells {0..5} where cell u = 5 is a shared outer cell with 4 facet slots bound to distinct neighbour IDs u_nbr[0..3].
    n_cells = 6
    u_nbr = [z3.Int(f'ra_unbr_{s}') for s in range(4)]
    cav1 = [z3.Bool(f'ra_cav1_{c}') for c in range(n_cells)]
    cav2 = [z3.Bool(f'ra_cav2_{c}') for c in range(n_cells)]
    # Cell 5 is an outer cell (not in cav1, not in cav2)
    slot_prem = [z3.Not(cav1[5]), z3.Not(cav2[5])]
    for s in range(4):
        slot_prem.append(z3.And(0 <= u_nbr[s], u_nbr[s] < 5))
        for t in range(s + 1, 4):
            slot_prem.append(u_nbr[s] != u_nbr[t])
    # Relaxation A selection rule between Insert g1 (W_A = cav1) and Insert g2 (R_A = cav2 U outer2):
    # since cav2 <= R_A(g2), kept_A(g2) implies cav1 cap cav2 == empty!
    kept_a_disjoint = z3.And(*[z3.Not(z3.And(cav1[c], cav2[c])) for c in range(n_cells)])

    f1, f2 = z3.Ints('ra_f1 ra_f2')
    f1_faces_c1 = z3.And(0 <= f1, f1 < 4, z3.Or(*[
        z3.And(f1 == s, u_nbr[s] == c, cav1[c]) for s in range(4) for c in range(5)
    ]))
    f2_faces_c2 = z3.And(0 <= f2, f2 < 4, z3.Or(*[
        z3.And(f2 == s, u_nbr[s] == c, cav2[c]) for s in range(4) for c in range(5)
    ]))
    rel_a_prem = slot_prem + [f1_faces_c1, f2_faces_c2]

    rows.append((
        'relaxation_a_shared_outer_insert_insert_witness',
        rel_a_prem + [kept_a_disjoint],
        f1 != f2,
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'relaxation_a_insert_insert_outer_slots_always_disjoint',
        rel_a_prem + [kept_a_disjoint],
        f1 == f2,
        'unsat',
        'QF_LIA',
    ))
    # Negative control: dropping kept_a_disjoint (allowing cav1 cap cav2 != empty) lets f1 == f2 collide!
    rows.append((
        'relaxation_a_overlapping_cavities_collide_slot_negative_control',
        rel_a_prem,
        f1 == f2,
        'sat',
        'QF_LIA',
    ))

    # ---- Unified 6-cell, 2-gate policy comparison for Relaxations A (Insert+Carve), B, C, and A+C ----
    adj, kind, c_cell, n_cell, in_cav, in_outer, _, prem = _build_round_model('pol', 6, 2, enforce_pop_dedup=True)
    _, _, kept_cur = _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, 6,
                                         mark_outer_on_insert=True, check_outer_on_insert=True, check_n_on_carve=True)
    _, _, kept_a = _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, 6,
                                       mark_outer_on_insert=False, check_outer_on_insert=True, check_n_on_carve=True)
    _, _, kept_b = _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, 6,
                                       mark_outer_on_insert=False, check_outer_on_insert=False, check_n_on_carve=True)
    _, _, kept_c = _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, 6,
                                       mark_outer_on_insert=True, check_outer_on_insert=True, check_n_on_carve=False)
    _, _, kept_ac = _select_expressions(kind, c_cell, n_cell, in_cav, in_outer, 6,
                                        mark_outer_on_insert=False, check_outer_on_insert=True, check_n_on_carve=False)

    # A2: Insert (g0) + Carve (g1) where C1 in Outer(Cav0):
    #     both apply -> post-round nc0 is Inside and C1 is Outside -> BOTH compute_pushes emit (nc0, C1)!
    ic_prem = prem + [kind[0] == KIND_INSERT, kind[1] == KIND_CARVE]
    c1_in_outer0 = z3.Or(*[z3.And(c_cell[1] == u, in_outer[0][u]) for u in range(6)])
    rows.append(('current_policy_prevents_insert_carve_outer_duplicate_push', ic_prem + [kept_cur[1]], c1_in_outer0, 'unsat', 'QF_LIA'))
    rows.append(('relaxation_a_insert_plus_carve_outer_duplicates_push_witness', ic_prem + [kept_a[1]], c1_in_outer0, 'sat', 'QF_LIA'))

    # B: Adjacent cavities Cav0 cap Outer(Cav1) != empty between two Insertions:
    #    prevented under current R (UNSAT), admitted under Relaxation B (SAT)!
    ii_prem = prem + [kind[0] == KIND_INSERT, kind[1] == KIND_INSERT]
    cav0_hits_outer1 = z3.Or(*[z3.And(in_cav[0][u], in_outer[1][u]) for u in range(6)])
    rows.append(('current_policy_prevents_adjacent_cavities', ii_prem + [kept_cur[1]], cav0_hits_outer1, 'unsat', 'QF_LIA'))
    rows.append(('relaxation_b_adjacent_cavities_corrupts_outer_refuted', ii_prem + [kept_b[1]], cav0_hits_outer1, 'sat', 'QF_LIA'))

    # C vs A+C: Carve(C1, N1) after Insert(Cav0) when dropping N from R(carve):
    # - Under CURRENT W(Insert) (marks Outer(Cav0)): N1 in Cav0 is STILL UNSAT because adj(C1, N1)
    #   forces C1 in Cav0 U Outer(Cav0) <= W(g0), so round_marked(C1) still defers g1!
    #   Relaxation C alone only admits Carve(C1, N1) with N1 in Outer(Cav0) (SAT witness).
    # - Under COMBINED Relaxation A+C (W_A = Cav0, R_C = {C}): N1 in Cav0 IS SAT -> stale carve!
    n1_in_cav0 = z3.Or(*[z3.And(n_cell[1] == u, in_cav[0][u]) for u in range(6)])
    n1_in_outer0 = z3.Or(*[z3.And(n_cell[1] == u, in_outer[0][u]) for u in range(6)])
    rows.append(('relaxation_c_alone_still_blocks_carve_into_cavity', ic_prem + [kept_c[1]], n1_in_cav0, 'unsat', 'QF_LIA'))
    rows.append(('relaxation_c_alone_allows_carve_on_outer_witness', ic_prem + [kept_c[1]], n1_in_outer0, 'sat', 'QF_LIA'))
    rows.append(('relaxation_ac_combined_allows_stale_carve_refuted', ic_prem + [kept_ac[1]], n1_in_cav0, 'sat', 'QF_LIA'))
    return rows


# ---- Realizable 4-facet-per-tetrahedron Python simulator (pop dedup, select, ApplyInsert, compute_pushes) ----

def _pop_and_select(raw_gates, mark_outer_on_insert=True, check_outer_on_insert=True,
                    mark_n_on_carve=False, check_n_on_carve=True):
    """Mirror MeshAlphaWrap.cs:957-1031: pop-time roundInner[c] dedup followed by Step 3 selection."""
    seen_inner = set()
    round_gates, deferred = [], []
    for g in raw_gates:
        if g['c'] in seen_inner:
            deferred.append(g)
            continue
        seen_inner.add(g['c'])
        round_gates.append(g)

    marked = set()
    kept = []
    for g in round_gates:
        kind = g['kind']
        reads = {g['c']}
        if kind != KIND_CARVE or check_n_on_carve:
            reads.add(g['n'])
        if kind in (KIND_INSERT, KIND_INSERT_REJECTED):
            reads |= set(g['cav'])
            if check_outer_on_insert:
                reads |= set(g['outer'])
        if any(x in marked for x in reads):
            deferred.append(g)
            continue
        if kind == KIND_INSERT:
            if mark_outer_on_insert:
                marked.add(g['c'])
                marked.add(g['n'])
                marked |= set(g['cav']) | set(g['outer'])
            else:
                marked |= set(g['cav'])
            kept.append(g)
        elif kind == KIND_CARVE:
            marked.add(g['c'])
            if mark_n_on_carve:
                marked.add(g['n'])
            kept.append(g)
        elif g.get('requeue', False):
            kept.append(g)
        # Drop / Rejected / RejectedInsert are consumed without adding to kept or deferred
    return kept, deferred


def _apply_and_compute_pushes(cn, labels, kept_gates):
    """Mirror Delaunay3.ApplyInsert + MeshAlphaWrap.apply_gate + compute_pushes on a 4-facet-per-cell mesh."""
    cn_post = {u: list(nbrs) for u, nbrs in cn.items()}
    lbl_post = dict(labels)
    slot_writes = []  # (cell, slot, gate_idx)
    new_ids_by_gate = {}
    next_id = max(cn_post.keys()) + 1

    # Step 4: ApplyInsert / Carve
    for gi, g in enumerate(kept_gates):
        if g['kind'] == KIND_INSERT:
            new_ids = []
            # Create one new Inside cell per boundary facet (c_in_cav -> outer, outer_slot)
            for outer, outer_slot in g['boundary']:
                nc = next_id
                next_id += 1
                cn_post[nc] = [-1, -1, -1, outer]
                lbl_post[nc] = True  # Inside
                if outer >= 0:
                    slot_writes.append((outer, outer_slot, gi))
                    cn_post[outer][outer_slot] = nc
                new_ids.append(nc)
            new_ids_by_gate[gi] = new_ids
        elif g['kind'] == KIND_CARVE:
            lbl_post[g['c']] = False  # Outside

    # Step 5: compute_pushes from post-round state (MeshAlphaWrap.cs:1220-1233)
    pushes = []
    for gi, g in enumerate(kept_gates):
        if g['kind'] == KIND_INSERT:
            for nc in new_ids_by_gate[gi]:
                for j, m in enumerate(cn_post[nc]):
                    if m >= 0 and not lbl_post[m]:
                        pushes.append((nc, j, m))
        elif g['kind'] == KIND_CARVE:
            c = g['c']
            for m in cn_post[c]:
                if m >= 0 and lbl_post[m]:
                    j = cn_post[m].index(c)
                    pushes.append((m, j, c))
    return cn_post, lbl_post, slot_writes, pushes


def _max_facets_ok(cn):
    return all(len(nbrs) <= 4 for nbrs in cn.values())


def replay_rounds():
    replays = []

    # 1. Shared-outside carve speedup (ALPHA_WRAP_ROUNDS.md:40-41), strictly 4-facet-per-tetrahedron:
    #    16 finite outside tetrahedra (0..15), each sharing its 4 facets with 4 distinct inside cells (16..79) = 64 carvings.
    #    Current policy (W(carve) = {C}) keeps all 64 (0 deferrals); old W = {C, N} keeps 16 and defers 48 (75%)!
    cn1 = {u: [] for u in range(80)}
    lbl1 = {u: (u >= 16) for u in range(80)}
    carve_gates = []
    for out_c in range(16):
        for s in range(4):
            in_c = 16 + 4 * out_c + s
            cn1[out_c].append(in_c)
            cn1[in_c].append(out_c)
            carve_gates.append(dict(kind=KIND_CARVE, c=in_c, n=out_c))
    kept_cur, def_cur = _pop_and_select(carve_gates, mark_n_on_carve=False)
    kept_old, def_old = _pop_and_select(carve_gates, mark_n_on_carve=True)
    replays.append(dict(
        name='shared_outside_carve_policy_replay',
        max_facets_ok=_max_facets_ok(cn1),
        kept_current=len(kept_cur),
        deferred_current=len(def_cur),
        kept_old_wn=len(kept_old),
        deferred_old_wn=len(def_old),
        passed=(_max_facets_ok(cn1) and len(kept_cur) == 64 and len(def_cur) == 0
                and len(kept_old) == 16 and len(def_old) == 48),
    ))

    # 2. Realizable 4-facet-per-tet hub vertex deferral amplification (W = 256):
    #    A 128-tet ring cavity (cells 0..127) around a hub vertex: each cavity tet has 2 neighbours in the ring
    #    and 2 outer neighbours (cells 128..383, 256 distinct outer cells -> <= 4 facets per cell!).
    #    Gate 0 inserts into the 128-tet cavity; gates 1..255 are carves on distinct c = 128 + k (each in outer!).
    #    Plus 1 duplicate-c gate (c = 129) verifying pop-time roundInner dedup.
    cn2 = {u: [] for u in range(384 + 256)}
    for i in range(128):
        nxt = (i + 1) % 128
        cn2[i].append(nxt)
        cn2[nxt].append(i)
        o1, o2 = 128 + 2 * i, 128 + 2 * i + 1
        cn2[i].extend([o1, o2])
        cn2[o1].append(i)
        cn2[o2].append(i)
    hub_outer = set(range(128, 384))
    hub_gates = [dict(kind=KIND_INSERT, c=0, n=128, cav=set(range(128)), outer=hub_outer)]
    for k in range(1, 256):
        c_k = 128 + k
        n_k = 384 + k  # dedicated outside neighbour for carve gate k
        cn2[c_k].append(n_k)
        cn2[n_k].append(c_k)
        hub_gates.append(dict(kind=KIND_CARVE, c=c_k, n=n_k))
    kept_hub, def_hub = _pop_and_select(hub_gates)
    kept_dup, def_dup = _pop_and_select([hub_gates[1], dict(hub_gates[1])])
    replays.append(dict(
        name='hub_deferral_amplification_256_replay',
        max_facets_ok=_max_facets_ok(cn2),
        window=256,
        kept=len(kept_hub),
        deferred=len(def_hub),
        pop_dedup_deferred=len(def_dup),
        passed=(_max_facets_ok(cn2) and len(kept_hub) == 1 and len(def_hub) == 255
                and len(kept_dup) == 1 and len(def_dup) == 1),
    ))

    # 3. Realizable 4-facet-per-tet conflict-set tightness replay (Relaxations A, B, C, and A+C),
    #    running real _apply_and_compute_pushes to observe actual cn slot writes and compute_pushes output:
    #
    #    (A1) Insert-Insert sharing outer cell 2 on distinct slots (2, 0) <-> cav1={0} and (2, 1) <-> cav2={1}:
    cn_a1 = {0: [2, 3, -1, -1], 1: [2, 4, -1, -1], 2: [0, 1, -1, -1], 3: [0, -1, -1, -1], 4: [1, -1, -1, -1]}
    lbl_a1 = {0: True, 1: True, 2: False, 3: False, 4: False}
    ins_ins = [
        dict(kind=KIND_INSERT, c=0, n=3, cav={0}, outer={2, 3}, boundary=[(2, 0), (3, 0)]),
        dict(kind=KIND_INSERT, c=1, n=4, cav={1}, outer={2, 4}, boundary=[(2, 1), (4, 0)]),
    ]
    k_std, _ = _pop_and_select(ins_ins, mark_outer_on_insert=True)
    k_ra, _ = _pop_and_select(ins_ins, mark_outer_on_insert=False)
    _, _, writes_a1, _ = _apply_and_compute_pushes(cn_a1, lbl_a1, k_ra)
    written_slots_a1 = [(c, s) for c, s, _ in writes_a1]
    ra_slots_disjoint = (len(written_slots_a1) == len(set(written_slots_a1)) == 4)

    #    (A2) Insert (cav={0}, outer={2, 4}) + Carve (c=2, n=3) under Relaxation A:
    #         real compute_pushes emits the exact same facet (nc, 3, 2) TWICE!
    cn_a2 = {0: [2, 4, -1, -1], 2: [0, 3, -1, -1], 3: [2, -1, -1, -1], 4: [0, -1, -1, -1]}
    lbl_a2 = {0: True, 2: True, 3: False, 4: False}
    ins_carve = [
        dict(kind=KIND_INSERT, c=0, n=4, cav={0}, outer={2, 4}, boundary=[(2, 0), (4, 0)]),
        dict(kind=KIND_CARVE, c=2, n=3),
    ]
    k_ic_std, _ = _pop_and_select(ins_carve, mark_outer_on_insert=True)
    k_ic_ra, _ = _pop_and_select(ins_carve, mark_outer_on_insert=False)
    _, _, _, pushes_a2 = _apply_and_compute_pushes(cn_a2, lbl_a2, k_ic_ra)
    dup_pushes_a2 = len(pushes_a2) > len(set(pushes_a2))

    #    (B) Adjacent cavities (cav1={0}, outer1={1, 3}; cav2={1}, outer2={0, 4}):
    #        Relaxation B keeps both -> g2 writes cn[0][0] into overwritten cavity cell 0!
    adj_cavs = [
        dict(kind=KIND_INSERT, c=0, n=3, cav={0}, outer={1, 3}),
        dict(kind=KIND_INSERT, c=1, n=4, cav={1}, outer={0, 4}),
    ]
    k_rb_std, _ = _pop_and_select(adj_cavs, mark_outer_on_insert=True, check_outer_on_insert=True)
    k_rb, _ = _pop_and_select(adj_cavs, mark_outer_on_insert=False, check_outer_on_insert=False)
    rb_corrupts = len(k_rb_std) == 1 and len(k_rb) == 2 and bool(k_rb[0]['cav'] & k_rb[1]['outer'])

    #    (C vs A+C) Carve(c=2, n=0) after Insert(cav={0}, outer={1, 2}):
    #        because c=2 borders n=0 in cav={0}, 2 is geometrically in outer={1, 2}.
    #        Relaxation C alone STILL defers g2 (len == 1); combined A+C wrongly keeps g2 (stale carve)!
    carve_after_ins = [
        dict(kind=KIND_INSERT, c=1, n=0, cav={0}, outer={1, 2}),
        dict(kind=KIND_CARVE, c=2, n=0),
    ]
    k_c_only, _ = _pop_and_select(carve_after_ins, mark_outer_on_insert=True, check_n_on_carve=False)
    k_ac, _ = _pop_and_select(carve_after_ins, mark_outer_on_insert=False, check_n_on_carve=False)
    ac_stale_carve = (len(k_c_only) == 1) and (len(k_ac) == 2) and (k_ac[1]['n'] in k_ac[0]['cav'])

    replays.append(dict(
        name='conflict_tightness_relaxations_abc_replay',
        max_facets_ok=_max_facets_ok(cn_a1) and _max_facets_ok(cn_a2),
        std_kept_insert_insert=len(k_std),
        ra_kept_insert_insert=len(k_ra),
        ra_slots_disjoint=ra_slots_disjoint,
        std_kept_insert_carve=len(k_ic_std),
        ra_insert_carve_duplicate_pushes=dup_pushes_a2,
        rb_corrupts_outer=rb_corrupts,
        c_only_kept=len(k_c_only),
        ac_executes_stale_carve=ac_stale_carve,
        passed=(
            _max_facets_ok(cn_a1) and _max_facets_ok(cn_a2)
            and len(k_std) == 1 and len(k_ra) == 2 and ra_slots_disjoint
            and len(k_ic_std) == 1 and len(k_ic_ra) == 2 and dup_pushes_a2
            and rb_corrupts and ac_stale_carve
        ),
    ))
    return replays


def run(timeout_ms=120000):
    source_contract(WRAP_SOURCE, WRAP_ANCHORS)
    source_contract(DT_SOURCE, DT_ANCHORS)
    obligations = (
        current_reservation_obligations()
        + deferral_and_progress_obligations()
        + conflict_tightness_obligations()
    )
    rows = [
        check_expr(name, prem, goal, expected, timeout_ms, logic)
        for name, prem, goal, expected, logic in obligations
    ]
    replays = replay_rounds()
    out = report(rows, replays=replays)
    out['passed'] = out['passed'] and all(r['passed'] for r in replays)
    return out


if __name__ == '__main__':
    sys.exit(main(run))
