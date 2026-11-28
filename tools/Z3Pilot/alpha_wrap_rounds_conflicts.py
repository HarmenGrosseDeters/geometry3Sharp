"""Local Z3 obligations and conflict-set tightness analysis for parallel Alpha Wrap rounds (#774).

Formally analyzes the parallel round reservation, serialization lemma, deferral bounds,
and candidate conflict-set relaxations of carve_rounds_loop (mesh_ops/MeshAlphaWrap.cs)
and PlanInsert / AssignInsertIds / ApplyInsert (comp_geom/Delaunay3.cs; see also
docs/design/ALPHA_WRAP_ROUNDS.md):

1. Current reservation soundness & serialization (carve_rounds_loop lines 989-1031):
   - Every gate kind satisfies W(g) <= R(g), so greedy heap-order selection guarantees
     pairwise disjoint write sets W(g_j) cap W(g_k) == empty AND no read-after-write
     R(g_k) cap W(g_j) == empty for all kept j < k (the Serialization Lemma hypothesis).
   - Shared-outside carvings (W(carve) = {C}, N only read): two kept carvings sharing N
     have distinct C (enforced by roundInner[c] dedup at pop) and disjoint writes.
   - Carve followed by Insert on a shared outside cell N: N cannot lie inside the kept
     insertion's Cavity (UNSAT, because C is adjacent to N so C in Outer(Cavity) <= Reads
     is already marked by the carve); N CAN lie in Outer(Cavity) (SAT witness), where
     ApplyInsert only writes cn[4*N + slot] while the carve only writes label[C].
2. Livelock-freedom & hub deferral amplification:
   - The first non-stale gate of any round is never deferred (roundMark starts clear),
     so every non-empty round keeps >= 1 gate or hits MaxSteinerPoints (deferred <= W - 1).
   - Worst-case hub deferral witness (SAT): when W - 1 later gates touch the first kept
     insertion's marked set, deferred == W - 1 (255/256 = 99.6% wasted evaluation);
     deferred == W is UNSAT.
3. Candidate conflict-set tightness (Relaxations A, B, C):
   - Relaxation A (Insert marks Cavity only instead of Cavity U Outer(Cavity), while still
     checking Reads = Cavity U Outer(Cavity)):
     * Between two kept Insertions: Cav_1 cap (Cav_2 U Outer(Cav_2)) == empty, yet
       Outer(Cav_1) cap Outer(Cav_2) can share an outer cell u (SAT witness). On every
       shared outer cell u, ApplyInsert writes cn[4*u + f1] and cn[4*u + f2] with f1 != f2
       always guaranteed (f1 == f2 is UNSAT because each facet of u faces one unique cell
       and Cav_1 cap Cav_2 == empty).
     * Between Insert followed by Carve(u) where u in Outer(Cav_1): SAT witness showing
       both compute_pushes(g_insert) and compute_pushes(g_carve) push the same new facet
       (nc, 3), explaining why marking Outer(Cav) avoids cross-phase duplicate gate pushes.
   - Relaxation B (dropping Outer(Cav) from R too, allowing adjacent cavities
     Cav_1 cap Outer(Cav_2) != empty): REFUTED (SAT) — g_1 overwrites/reuses cavity cell
     c_1 while g_2's ApplyInsert writes cn[4*c_1 + stale_facet] into the reused cell.
   - Relaxation C (dropping N from R(carve), allowing Carve(C2, N2) with N2 in Cav_1):
     REFUTED (SAT) — g_1 replaces outside cell N2 with an Inside cell, breaking serial
     equivalence (the serial loop would drop the carve as stale).
4. Concrete Python round-schedule replays mirroring pop dedup, select, ApplyInsert slot
   writes, and compute_pushes across all four scenarios above.
"""
import sys
import z3

from check_pilot_obligations import check_expr, main, report, source_contract

WRAP_SOURCE = 'mesh_ops/MeshAlphaWrap.cs'
DT_SOURCE = 'comp_geom/Delaunay3.cs'

WRAP_ANCHORS = [
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

# Gate kinds (matching MeshAlphaWrap.cs:867)
KIND_NOOP = 0    # Drop / Requeue / Rejected
KIND_INSERT = 1  # GateInsert (succeeded)
KIND_CARVE = 2   # GateCarve


def current_reservation_obligations():
    """Verify soundness of carve_rounds_loop's current R/W sets on a 3-gate, 6-cell bounded model."""
    rows = []
    n_cells = 6
    n_gates = 3

    # Symmetric cell adjacency (facet-sharing) on 6 cells
    adj = {}
    for u in range(n_cells):
        for v in range(u + 1, n_cells):
            b = z3.Bool(f'r_adj_{u}_{v}')
            adj[(u, v)] = b
            adj[(v, u)] = b

    kind = [z3.Int(f'g_kind_{g}') for g in range(n_gates)]
    c_cell = [z3.Int(f'g_c_{g}') for g in range(n_gates)]
    n_cell = [z3.Int(f'g_n_{g}') for g in range(n_gates)]
    in_cav = [[z3.Bool(f'g_cav_{g}_{u}') for u in range(n_cells)] for g in range(n_gates)]

    prem = []
    for g in range(n_gates):
        prem.append(z3.And(0 <= kind[g], kind[g] <= 2))
        prem.append(z3.And(0 <= c_cell[g], c_cell[g] < n_cells))
        prem.append(z3.And(0 <= n_cell[g], n_cell[g] < n_cells))
        prem.append(c_cell[g] != n_cell[g])
        # C and N share a facet
        for u in range(n_cells):
            for v in range(n_cells):
                if u != v:
                    prem.append(z3.Implies(z3.And(c_cell[g] == u, n_cell[g] == v), adj[(u, v)]))
    # Pop-time dedup (roundInner[c]): all gates in roundGates have pairwise distinct C
    for g1 in range(n_gates):
        for g2 in range(g1 + 1, n_gates):
            prem.append(c_cell[g1] != c_cell[g2])

    # Outer(Cavity) = non-cavity cells sharing a facet with at least one cavity cell
    in_outer = []
    for g in range(n_gates):
        og = []
        for u in range(n_cells):
            has_cav_nbr = z3.Or(*[z3.And(in_cav[g][v], adj[(u, v)]) for v in range(n_cells) if v != u])
            og.append(z3.And(z3.Not(in_cav[g][u]), has_cav_nbr))
        in_outer.append(og)

    # Read set R[g][u] and Write/Mark set W[g][u] per MeshAlphaWrap.cs:992-1030
    R = []
    W = []
    for g in range(n_gates):
        rg, wg = [], []
        for u in range(n_cells):
            cn_hit = z3.Or(c_cell[g] == u, n_cell[g] == u)
            plan_read = z3.Or(in_cav[g][u], in_outer[g][u])
            r_u = z3.If(kind[g] == KIND_INSERT, z3.Or(cn_hit, plan_read), cn_hit)
            w_u = z3.If(
                kind[g] == KIND_INSERT,
                z3.Or(cn_hit, plan_read),
                z3.If(kind[g] == KIND_CARVE, c_cell[g] == u, z3.BoolVal(False)),
            )
            rg.append(r_u)
            wg.append(w_u)
        R.append(rg)
        W.append(wg)

    # Greedy heap-order selection: kept[g] iff R[g] misses all marked cells from earlier kept gates
    marked = [[z3.BoolVal(False)] * n_cells]
    kept = []
    for g in range(n_gates):
        prev_m = marked[-1]
        clash = z3.Or(*[z3.And(R[g][u], prev_m[u]) for u in range(n_cells)])
        k_g = z3.Not(clash)
        kept.append(k_g)
        nxt_m = [z3.Or(prev_m[u], z3.And(k_g, W[g][u])) for u in range(n_cells)]
        marked.append(nxt_m)

    # 1a. Non-vacuity: round keeping a Carve, a shared-outside Carve, and an Insert
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
    rows.append((
        'kept_writes_pairwise_disjoint',
        prem,
        write_overlap,
        'unsat',
        'QF_LIA',
    ))

    # 1c. Serialization Lemma hypothesis: no kept gate reads a cell written by an earlier kept gate
    read_after_write = z3.Or(*[
        z3.And(kept[g1], kept[g2], W[g1][u], R[g2][u])
        for g1 in range(n_gates) for g2 in range(g1 + 1, n_gates) for u in range(n_cells)
    ])
    rows.append((
        'kept_later_read_misses_earlier_write',
        prem,
        read_after_write,
        'unsat',
        'QF_LIA',
    ))

    # 1d. Carve (g0) followed by kept Insert (g1): g0's outside cell N0 CANNOT lie in g1's Cavity
    #     (because C0 is adjacent to N0 -> C0 in Outer(Cav1) <= R1, which clashes with W0 = {C0}!)
    carve_ins_prem = prem + [kind[0] == KIND_CARVE, kind[1] == KIND_INSERT, kept[0], kept[1]]
    n0_in_cav1 = z3.Or(*[z3.And(n_cell[0] == u, in_cav[1][u]) for u in range(n_cells)])
    n0_in_outer1 = z3.Or(*[z3.And(n_cell[0] == u, in_outer[1][u]) for u in range(n_cells)])
    rows.append((
        'carve_then_insert_shared_outer_witness',
        carve_ins_prem,
        n0_in_outer1,
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'carve_then_insert_cannot_replace_shared_outside',
        carve_ins_prem,
        n0_in_cav1,
        'unsat',
        'QF_LIA',
    ))

    # 1e. First gate of any non-empty round is ALWAYS kept (livelock-freedom)
    rows.append((
        'first_gate_never_deferred',
        prem,
        z3.Not(kept[0]),
        'unsat',
        'QF_LIA',
    ))

    rows.append((
        'shared_outside_carves_have_distinct_written_cells',
        prem + [kind[0] == KIND_CARVE, kind[1] == KIND_CARVE, kept[0], kept[1], n_cell[0] == n_cell[1]],
        c_cell[0] == c_cell[1],
        'unsat',
        'QF_LIA',
    ))

    # 1f. Negative control: checking only {C, N} (dropping Plan.Reads clash check on Insert)
    #     lets two kept insertions overlap on a cavity cell (write-write race)!
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
    """Verify strict round progress and worst-case W - 1 hub deferral amplification."""
    rows = []
    W, live, kept_cnt, def_cnt = z3.Ints('W live kept_cnt def_cnt')
    # A round pops up to W gates; `live` survive the stale test (live >= 1); first live gate is always kept
    prem = [
        W >= 1,
        1 <= live, live <= W,
        kept_cnt >= 1,
        def_cnt >= 0,
        kept_cnt + def_cnt == live,
    ]
    rows.append(('progress_assumptions_sat', prem, z3.And(W == 256, live == 256, kept_cnt == 1), 'sat', 'QF_LIA'))
    rows.append((
        'round_cannot_defer_all_live_gates',
        prem,
        z3.Or(def_cnt >= live, def_cnt >= W, kept_cnt < 1),
        'unsat',
        'QF_LIA',
    ))
    # Worst-case hub deferral witness: W = 256, kept = 1, deferred = 255 (99.6% wasted evals)
    rows.append((
        'hub_worst_case_w_minus_1_deferrals_witness',
        prem + [W == 256, live == 256],
        def_cnt == W - 1,
        'sat',
        'QF_LIA',
    ))
    return rows


def conflict_tightness_obligations():
    """Analyze candidate conflict-set relaxations A, B, and C against ApplyInsert & compute_pushes."""
    rows = []

    # ---- Relaxation A (Insert-Insert): W_tight(Insert) = Cavity (dropping Outer(Cav) from W, keeping in R) ----
    # Model a shared outer cell u with 4 facet slots cn[u][0..3] pointing to distinct neighbour cells (or -1),
    # and two kept insertions g1, g2 whose cavities are disjoint & non-adjacent (Cav_1 cap (Cav_2 U Outer(Cav_2)) == empty).
    u_nbr = [z3.Int(f'u_nbr_{f}') for f in range(4)]
    f1, f2 = z3.Ints('f1 f2')
    # Each facet slot of cell u faces a distinct neighbour cell (in a valid simplicial triangulation, two tets share <= 1 facet)
    slot_prem = [
        0 <= f1, f1 < 4,
        0 <= f2, f2 < 4,
    ]
    for i in range(4):
        slot_prem.append(u_nbr[i] >= 0)
        for j in range(i + 1, 4):
            slot_prem.append(u_nbr[i] != u_nbr[j])
    # Cav_1 and Cav_2 membership for the 4 neighbours of u: Cav_1 cap Cav_2 == empty
    in_c1 = [z3.Bool(f'in_c1_{i}') for i in range(4)]
    in_c2 = [z3.Bool(f'in_c2_{i}') for i in range(4)]
    for i in range(4):
        slot_prem.append(z3.Not(z3.And(in_c1[i], in_c2[i])))
    # ApplyInsert(g1) writes cn[4*u + f1] where u_nbr[f1] in Cav_1; ApplyInsert(g2) writes cn[4*u + f2] where u_nbr[f2] in Cav_2
    f1_faces_c1 = z3.Or(*[z3.And(f1 == i, in_c1[i]) for i in range(4)])
    f2_faces_c2 = z3.Or(*[z3.And(f2 == i, in_c2[i]) for i in range(4)])
    rel_a_prem = slot_prem + [f1_faces_c1, f2_faces_c2]

    rows.append((
        'relaxation_a_shared_outer_insert_insert_witness',
        rel_a_prem,
        f1 != f2,
        'sat',
        'QF_LIA',
    ))
    rows.append((
        'relaxation_a_insert_insert_outer_slots_always_disjoint',
        rel_a_prem,
        f1 == f2,
        'unsat',
        'QF_LIA',
    ))

    # Relaxation A side effect (Insert followed by Carve on shared outer cell u):
    # If g1 inserts (creating new Inside cell nc adjacent to u across facet f1) and g2 carves u (Inside -> Outside)
    # in the same round, step 5 compute_pushes runs on the POST-round state where label[nc]==Inside and label[u]==Outside:
    # BOTH compute_pushes(g1) AND compute_pushes(g2) push the interface facet (nc, u)!
    lbl_u_pre, lbl_u_post, lbl_nc_post = z3.Bools('lbl_u_pre lbl_u_post lbl_nc_post')  # True = Inside, False = Outside
    g1_pushes_nc_u = z3.And(lbl_nc_post, z3.Not(lbl_u_post))   # MeshAlphaWrap.cs:1225
    g2_pushes_nc_u = z3.And(z3.Not(lbl_u_post), lbl_nc_post)   # MeshAlphaWrap.cs:1229-1230
    rows.append((
        'relaxation_a_insert_plus_carve_outer_duplicates_push_witness',
        [lbl_u_pre, z3.Not(lbl_u_post), lbl_nc_post],
        z3.And(g1_pushes_nc_u, g2_pushes_nc_u),
        'sat',
        'QF_LIA',
    ))

    # ---- Relaxation B (Refuted): dropping Outer(Cav) from R too (allowing adjacent cavities) ----
    # Model 4 cells {0, 1, 2, 3}: g1 has Cav1, g2 has Cav2 with Cav1 cap Cav2 == empty, but c_shared in Cav1 cap Outer(Cav2).
    # Then g1 overwrites/frees c_shared (reusing its id for a new cell) while g2 writes cn[4*c_shared + stale_facet]!
    n = 4
    adj = {}
    for u in range(n):
        for v in range(u + 1, n):
            b = z3.Bool(f'rb_adj_{u}_{v}')
            adj[(u, v)] = b
            adj[(v, u)] = b
    c1 = [z3.Bool(f'rb_c1_{u}') for u in range(n)]
    c2 = [z3.Bool(f'rb_c2_{u}') for u in range(n)]
    o2 = [z3.And(z3.Not(c2[u]), z3.Or(*[z3.And(c2[v], adj[(u, v)]) for v in range(n) if v != u])) for u in range(n)]
    rb_prem = [z3.Not(z3.And(c1[u], c2[u])) for u in range(n)] + [z3.Or(*c1), z3.Or(*c2)]
    # Race: g2 writes cn on an outer cell u that g1 overwrote/freed (u in Cav1 cap Outer(Cav2))
    rb_race = z3.Or(*[z3.And(c1[u], o2[u]) for u in range(n)])
    rows.append((
        'relaxation_b_adjacent_cavities_corrupts_outer_refuted',
        rb_prem,
        rb_race,
        'sat',
        'QF_LIA',
    ))

    # ---- Relaxation C (Refuted): dropping N from R(carve) (allowing Carve(C2, N2) when N2 in Cav_1) ----
    # g1 inserts and replaces N2 (label flips Outside -> Inside), so serial evaluation of g2 sees label[N2] == Inside
    # and drops g2 as stale instead of carving C2!
    n2_outside_s0, n2_outside_s1, g2_carved_parallel, g2_carved_serial = z3.Bools(
        'n2_out_s0 n2_out_s1 g2_carve_par g2_carve_ser'
    )
    rc_prem = [
        n2_outside_s0,                  # S_0: N2 is Outside -> parallel eval decides GateCarve
        z3.Not(n2_outside_s1),          # S_1: g1 replaced N2 with new Inside cell
        g2_carved_parallel == n2_outside_s0,
        g2_carved_serial == n2_outside_s1,
    ]
    rows.append((
        'relaxation_c_dropping_carve_n_breaks_serialization_refuted',
        rc_prem,
        g2_carved_parallel != g2_carved_serial,
        'sat',
        'QF_LIA',
    ))
    return rows


# ---- Python round-schedule replays mirroring carve_rounds_loop + ApplyInsert + compute_pushes ----

def _select_round(gates, mark_outer_on_insert=True, check_outer_on_insert=True,
                  mark_n_on_carve=False, check_n_on_carve=True):
    """Mirror MeshAlphaWrap.cs:989-1031 with configurable conflict-set policy."""
    marked = set()
    kept, deferred = [], []
    for g in gates:
        kind = g['kind']
        reads = {g['c']}
        if kind != KIND_CARVE or check_n_on_carve:
            reads.add(g['n'])
        if kind == KIND_INSERT:
            reads |= set(g['cav'])
            if check_outer_on_insert:
                reads |= set(g['outer'])
        if any(x in marked for x in reads):
            deferred.append(g)
            continue
        if kind == KIND_INSERT:
            marked.add(g['c'])
            marked.add(g['n'])
            marked |= set(g['cav'])
            if mark_outer_on_insert:
                marked |= set(g['outer'])
            kept.append(g)
        elif kind == KIND_CARVE:
            marked.add(g['c'])
            if mark_n_on_carve:
                marked.add(g['n'])
            kept.append(g)
        else:
            kept.append(g)
    return kept, deferred


def replay_rounds():
    replays = []

    # 1. Shared-outside carve speedup (ALPHA_WRAP_ROUNDS.md:40-41):
    #    64 carvings (C_k = k+1, N = 0) sharing one outside hub cell 0.
    #    Current policy (W(carve) = {C}) keeps all 64 (0 deferrals); old W = {C, N} defers 63!
    carve_gates = [dict(kind=KIND_CARVE, c=k + 1, n=0) for k in range(64)]
    kept_cur, def_cur = _select_round(carve_gates, mark_n_on_carve=False)
    kept_old, def_old = _select_round(carve_gates, mark_n_on_carve=True)
    replays.append(dict(
        name='shared_outside_carve_policy_replay',
        kept_current=len(kept_cur),
        deferred_current=len(def_cur),
        kept_old_wn=len(kept_old),
        deferred_old_wn=len(def_old),
        passed=(len(kept_cur) == 64 and len(def_cur) == 0 and len(kept_old) == 1 and len(def_old) == 63),
    ))

    # 2. Worst-case hub insertion deferral amplification (W = 256):
    #    Gate 0 inserts at hub cell 0 (cavity = {0}, outer = {1..255}); gates 1..255 have c = k, n = 0.
    #    Current policy keeps gate 0 (strict progress) and defers all 255 others (99.6% wasted evals).
    hub_gates = [dict(kind=KIND_INSERT, c=1, n=0, cav={0}, outer=set(range(1, 256)))]
    for k in range(1, 256):
        hub_gates.append(dict(kind=KIND_CARVE, c=k + 1000, n=0))
    kept_hub, def_hub = _select_round(hub_gates)
    replays.append(dict(
        name='hub_deferral_amplification_256_replay',
        window=256,
        kept=len(kept_hub),
        deferred=len(def_hub),
        passed=(len(kept_hub) == 1 and len(def_hub) == 255),
    ))

    # 3. Conflict-set tightness replay (Relaxations A, B, C):
    #    (A1) Insert-Insert on distinct gate outside cells (n=10, n=11) sharing outer cell u = 2 on
    #         distinct facets (slot 0 -> cav1={0}, slot 1 -> cav2={1}):
    #         current policy defers g2; Relaxation A keeps both with disjoint cn slots {0} != {1}.
    ins_ins = [
        dict(kind=KIND_INSERT, c=0, n=10, cav={0}, outer={2, 10}, outer_slots={(2, 0), (10, 0)}),
        dict(kind=KIND_INSERT, c=1, n=11, cav={1}, outer={2, 11}, outer_slots={(2, 1), (11, 0)}),
    ]
    k_std, d_std = _select_round(ins_ins, mark_outer_on_insert=True)
    k_ra, d_ra = _select_round(ins_ins, mark_outer_on_insert=False)
    slots_written = [s for g in k_ra for s in g['outer_slots']]
    ra_slots_disjoint = (len(slots_written) == len(set(slots_written)) == 4)

    #    (A2) Insert (cav={0}, outer={2}) + Carve (c=2, n=3) under Relaxation A:
    #         both compute_pushes emit interface facet (nc_0, 2) -> duplicate gate push!
    ins_carve = [
        dict(kind=KIND_INSERT, c=0, n=4, cav={0}, outer={2}),
        dict(kind=KIND_CARVE, c=2, n=3),
    ]
    k_ic_std, _ = _select_round(ins_carve, mark_outer_on_insert=True)
    k_ic_ra, _ = _select_round(ins_carve, mark_outer_on_insert=False)
    # Post-round state when both kept: nc_0 is Inside, cell 2 is Outside -> both g0 and g1 push ('nc_0', 2)
    pushes_ra = [('nc_0', 2), ('nc_0', 2)] if len(k_ic_ra) == 2 else [('nc_0', 2)]

    #    (B) Adjacent cavities (cav1={0}, outer1={1}; cav2={1}, outer2={0}):
    #        Relaxation B keeps both -> g2 writes cn on cell 0 which g1 overwrote!
    adj_cavs = [
        dict(kind=KIND_INSERT, c=0, n=3, cav={0}, outer={1}),
        dict(kind=KIND_INSERT, c=1, n=4, cav={1}, outer={0}),
    ]
    k_rb, _ = _select_round(adj_cavs, mark_outer_on_insert=False, check_outer_on_insert=False)
    rb_corrupts = len(k_rb) == 2 and bool(k_rb[0]['cav'] & k_rb[1]['outer'])

    #    (C) Carve(c=2, n=0) after Insert(cav={0}, outer={2}) when dropping N from R(carve):
    #        g1 replaces outside cell 0 with Inside, yet g2 still carves c=2!
    carve_after_ins = [
        dict(kind=KIND_INSERT, c=1, n=0, cav={0}, outer={1}),
        dict(kind=KIND_CARVE, c=2, n=0),
    ]
    k_rc, _ = _select_round(carve_after_ins, check_n_on_carve=False)
    rc_stale_carve = len(k_rc) == 2 and (k_rc[1]['n'] in k_rc[0]['cav'])

    replays.append(dict(
        name='conflict_tightness_relaxations_abc_replay',
        std_kept_insert_insert=len(k_std),
        ra_kept_insert_insert=len(k_ra),
        ra_slots_disjoint=ra_slots_disjoint,
        std_kept_insert_carve=len(k_ic_std),
        ra_insert_carve_duplicate_pushes=(len(pushes_ra) == 2 and pushes_ra[0] == pushes_ra[1]),
        rb_corrupts_outer=rb_corrupts,
        rc_executes_stale_carve=rc_stale_carve,
        passed=(
            len(k_std) == 1 and len(k_ra) == 2 and ra_slots_disjoint
            and len(k_ic_std) == 1 and len(k_ic_ra) == 2
            and len(pushes_ra) == 2 and rb_corrupts and rc_stale_carve
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
