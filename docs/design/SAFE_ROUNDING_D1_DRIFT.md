# SafeRounding D1: the drift measured from reference triangles (the reference rule)

Status: implemented in Boolean 2.1 (`mesh_ops/MeshRoundingRepair.cs`; owner decision 2026-11-10). Proposed
2026-11-09 (research item 2 of [roadmap/research.md](../roadmap/research.md), "Hard open problems"); the lemma of
section 3 is checked by Z3 (`tools/Z3Pilot/drift_reference.py`, section 4, which now also holds the source contract
of the code against the rule), the effect was measured with a prototype (`benchmarks/BooleanComparison/perf/d1/`,
section 5). Sections 6 and 7a describe the state before the build; the tests of the build are listed in section 7b.

Result in short:
- Today's accounting adds an edit's bound to the largest drift among the edit's vertices. In a cluster the edits are
  neighbours, each reads what the last one wrote, and the budget `maxLength` is used up although no point of the
  surface has moved further than one edit: on the procedural patch the drift reaches 0.96 of the bound while the
  measured distance to the start is below 0.001 of it.
- The reference rule keeps, per triangle, where its corners were when its bound was last certified, and measures the
  displacement from there instead of adding steps. Moves and contractions then cost what they actually displace.
- On the 14 Thingi10K models with D1 operations plus 119829 / 119830 (48 operations): 13 are not clean on master,
  0 with the prototype, none newly unclean, times unchanged (section 5).
- It never rejects an edit that today's rule accepts in the same state (section 3.3, checked in section 4).

## 1. How the drift is accounted today, and why it is additive

The code is `mesh_ops/MeshRoundingRepair.cs`; the rule and its proof are in
[SAFE_ROUNDING.md](SAFE_ROUNDING.md) section 1 and [ROUNDING_DRIFT_APEX.md](ROUNDING_DRIFT_APEX.md).

- State: one number per vertex, `drift[v]`, 0 at the start of `Apply`.
- Every proposed edit goes through `improves`. It lists the triangles the edit removes (`removed`, R) and adds
  (`added`, A), computes `most` = M, the largest `drift` over **all vertices of R and A**, and calls
  `within_bound(most, step, maxLength_of(...))`: the edit is rejected unless `editDrift = most + step <= maxLength`.
  `step` is the edit's own bound δ: the distance a vertex moves (`try_move`, `try_slide`), `max(|p - a|, |p - b|)` for
  a contraction (`try_collapse`), the surface bounds of `try_flip`, `try_zip`, `try_strip`, `try_remove_vertex`.
- `commit_drift` writes `editDrift` to the edit's apex (`apex_of`: the first vertex of `added[0]` that is in every
  added triangle), or to every vertex of R and A if there is none.
- Invariant (ROUNDING_DRIFT_APEX.md section 3), with `D(T)` the largest drift of the corners of T: (I1) every point of
  a triangle T lies within `D(T)` of the start surface S0; (I2) every point of S0 lies within `D(T)` of some T.

Why it adds up. The proof of (I1) for an added triangle T' is a chain of two steps: a point of T' is within δ of a
point z of a removed triangle T (the edit's two-sided bound), and z is within `D(T) <= M` of S0 (the invariant). The
scalar `D(T)` says how far z may be from S0, but not in which direction or from which point of S0, so the triangle
inequality is the only way to combine the two: `M + δ`. That is tight if the edit moves z further away from the place
it came from, and loose by up to the whole of M if it does not. Two things make it loose in clusters:

1. **M is read from the neighbours.** M is the largest drift over every vertex of R and A, not the drift of the
   triangle the point came from. A contraction of `a` into `b` reads the drift of every vertex in the star of `a`.
   If one of them is the apex of an earlier contraction next door, the new apex gets that drift plus its own step,
   and the next neighbour reads it in turn.
2. **Steps are summed, not displacements.** A vertex that absorbs its neighbours one after the other is charged the
   sum of the edge lengths, while the points it stands for have moved by at most the radius of what was absorbed.

## 2. The D1 mechanism

A patch of tiny, well-shaped triangles whose area is below `DegenerateArea` (the D′ decision: they are contracted).
The repair works through the patch; every contraction has δ of about one edge, and its M comes from a neighbour that
was contracted before it. Along the order of the edits the charge grows by one edge per edit, as a path through the
patch: after k neighbouring contractions the apex carries about k edges, although every vertex of the start is still
within about one edge of its place. When the path reaches `maxLength / edge` edits, every further edit in the patch
is rejected with `editDrift` just above the bound.

Measured (the tests `RoundingDriftClusterTests`, section 6, and the raw rows in
`benchmarks/BooleanComparison/perf/d1/`):

| case | contractions | drift reached | smallest rejected | degenerate left | measured distance to the start |
|---|---|---|---|---|---|
| procedural patch (sphere, 1300 tiny triangles) | 378 | 0.961 | 1.004 | 2 | below 0.001 |
| the same with four times the bound | 384 | 2.78 (of the original bound) | none | 0 | below 0.001 |
| 119829 union | 13429 | 0.987 | 1.008 | 23 | 0.049 |
| 686056 union | 67 (and 14 flips) | 0.999 | 1.0002 | 16 | 0.106 |

(drift and distance in units of `maxLength`; the measured distance is the largest distance of the corners, edge
midpoints and centroids of either surface's triangles to the other surface, a lower estimate of the Hausdorff
distance.) The second row is the upper bound for any accounting: a clean result within a thousandth of the bound
exists and is reached by the same edits; only its certificate is missing.

The apex rule (D1 cause 1, merged) removed the chaining through the vertices that an edit does not charge. What is
left is the chaining through M: 9 of the 42 operations of the 14 D1 models were still unclean after it, today 9
(other ones in part, the result of an edit sequence depends on the order) plus the two unions and two differences of
119829 / 119830.

## 3. The reference rule

### 3.1 State and rule

Notation: S0 the surface at the start of `Apply`, S the current one; `pos(v)` the current position of a vertex;
L = `maxLength` of the component.

- Per triangle t of S: three **anchors** `α_t[0..2]`, one point per corner, and a **carried bound** `c_t >= 0`.
  At the start the anchors are the corners and `c_t = 0`. The triangle `T̂_t` spanned by the anchors is the
  *reference triangle* of t.
- A **ghost** is the same record for a triangle that a contraction has collapsed: three anchors, `c`, and for each
  corner the vertex of S it collapsed onto (two or three of them the same vertex).
- The bound of a triangle or ghost:

      B(t) = c_t + max over the corners k of |pos(v_k) - α_t[k]|

**Displacement edit** (`try_move`, `try_slide`, `try_collapse`: the same triangles, a vertex moved, or a vertex
renamed to the kept one): no anchor and no `c` changes. A triangle that collapses (it holds both ends of the
contracted edge) becomes a ghost; the ghosts of the vanishing vertex become ghosts of the kept one. Accepted if
`B <= L` for every triangle and ghost with a corner at a moved or renamed vertex, evaluated at the new positions.

**Generic edit** (`try_flip`, `try_zip`, `try_strip`, `try_remove_vertex`: R removed, A added, with the two-sided
bound δ of SAFE_ROUNDING.md section 1). A ghost is *lost* if the vertex or edge its corners span is no vertex or
edge of the mesh after the edit. Let

      N = max( max over t in R of B(t), max over the lost ghosts g of B(g) ) + δ.

Accepted if `N <= L`. Every triangle of A gets the anchors "its corners now" and `c = N`; the lost ghosts are
deleted.

`MaxDrift` is the largest bound an accepted edit evaluated (every bound that an edit changes is evaluated by it, so
this is the largest bound at the end or above it; the claim `reported` of section 4, part C).

### 3.2 Invariant and lemma

**Invariant J(S):**
- (J1) for every triangle t of S, every point of its reference triangle `T̂_t` lies within `c_t` of S0;
- (J2) every point of S0 lies within `c` of the reference triangle of some triangle or ghost;
- (J3) the vertices a ghost's corners collapsed onto span a vertex or an edge of S.

J holds at the start (the reference triangles are the triangles of S0, `c = 0`, no ghosts).

**Convexity (C).** Let x = Σ λ_k pos(v_k) be a point of t and x̂ = Σ λ_k α_t[k] the point of `T̂_t` with the same
barycentric coordinates. Then `|x - x̂| <= max_k |pos(v_k) - α_t[k]|`. The same holds for a ghost, whose image
is the point with the same coordinates on the vertex or edge it collapsed onto, which is in S by (J3).

**Theorem.** J(S) implies `H(S, S0) <= max over the triangles and ghosts of B`.
*Proof.* A point x of t: by (C) within `B(t) - c_t` of x̂, by (J1) x̂ within `c_t` of S0. A point y of S0: by
(J2) within `c` of a point x̂ of a reference triangle; its image x is in S ((J3) for a ghost) and within `B - c` of
x̂ by (C). ∎

**Lemma (one edit).** If J(S) holds and the edit is of one of the two kinds above, with (E1), (E2) of
ROUNDING_DRIFT_APEX.md section 3 for a generic edit, then J(S') holds for the surface and the state after it.

*Proof.*
- Displacement edit. No reference triangle and no `c` changes, and the set of reference triangles (of triangles
  and ghosts together) is the same, so (J1) and (J2) are untouched. (J3): a collapsed triangle held the contracted
  edge a-b and a third vertex x; its image is the edge b-x, which is an edge of S' (assumption A2); a ghost of `a`
  on an edge a-x is afterwards on b-x, an edge of S', or at the vertex b if x = b.
- Generic edit. (J1) for t' in A, whose reference triangle is t' itself: a point x of t' has by (E1) a point z of
  some t in R with `|x - z| <= δ`; by the theorem's first half `dist(z, S0) <= B(t)`; so `dist(x, S0) <= B(t) + δ
  <= N = c_t'`. (J2): let y in S0 have its witness in a removed triangle or a lost ghost w, so `|y - x̂| <= c_w`
  for a point x̂ of its reference triangle; the image z of x̂ lies in a triangle of R (for a lost ghost: on a
  vertex or edge of S that is none of S', hence a face of a triangle of R, assumption A3) and `|z - x̂| <= B(w) -
  c_w`; by (E2) a triangle t' of A has `dist(z, t') <= δ`; so `dist(y, t') <= B(w) + δ <= N = c_t'`, and t' is its
  own reference triangle. Witnesses in untouched triangles and kept ghosts stay. (J3): the kept ghosts are those
  whose vertex or edge is in S'. ∎

No apex is needed: every triangle of A carries its own `c`, and untouched triangles are untouched, also those that
share a vertex with the edit. An ear contraction is a displacement edit like any other (its triangle becomes a
ghost on the edge across); the special case of today's proof goes away.

### 3.3 Never worse than today's rule

**Claim.** Run both accountings on the same sequence of edits. Then for every triangle `B(t) <= D(t)`, the largest
drift of its corners under the apex rule; every ghost's bound is at most `D(T)` of a triangle T that contains the
vertex or edge it collapsed onto; and the value the reference rule compares with L is at most today's `M + δ`. So
an edit that today's rule accepts is accepted. This is a statement in real arithmetic: in doubles the two rules
compute different expressions (a distance from an anchor against a stored sum of steps), which can differ by a few
units in the last place either way, so an edit within that of the bound can fall on either side.

*Proof sketch.* A displacement edit moves every corner by at most its δ, so every bound it evaluates grows by at
most δ and was at most M before (the triangles and ghosts concerned have all their vertices among those M is taken
over); the apex, or every vertex, gets `M + δ`. A generic edit: `B(t) <= D(t) <= M` for t in R; a lost ghost's
dominating triangle contains its vertex or edge, so it is in R; `N <= M + δ`, and every triangle of A contains the
apex. Untouched bounds do not change, drifts do not decrease. ∎ (Checked on chains in section 4, part C.)

The converse fails, which is the point: after k neighbouring contractions the reference rule has the largest true
displacement of a start vertex where today's has the sum (the test `test_the_rule_is_tighter_on_a_chain_of_contractions`:
1 against 2 for two contractions side by side, 1.5 against 4.5 after a move back).

Results still depend on the order of the edits, and more edits pass, so single operations can come out differently
(as with the apex rule, where one operation became unclean). In the probe none did.

### 3.4 Assumptions about the code (true today; A1 and the form of the edits are held by `source_contract()`)

- A1: positions are written in one place (`set_vertex`), and only `try_move` and `try_collapse` propose a moved
  vertex; they list every triangle of a moved vertex (a contraction with `p == pos(b)` moves nothing but `a`).
- A2: after a contraction of a-b the edges b-x of the two collapsed triangles are edges of the mesh
  (`DMesh3.CollapseEdge` refuses the cases where they are not; the implementation asserts it in check mode).
- A3: a vertex or edge that is gone after a generic edit was a face of a removed triangle.
- A4: surviving triangles keep their id and corner order through `CollapseEdge` (the prototype checks it at every
  contraction; no violation in any run of section 5). The other edits do not: `FlipEdge` reuses the two ids with
  new corners, and zips, strip zips and vertex removals append triangles, so the record of an added triangle is
  written after the edit, by looking the triangle up. The undo paths of `try_zip`, `try_strip` and
  `try_remove_vertex` (an append failed: the old triangles are appended again, under new ids) must restore the old
  records with them; today's per-vertex drift needs no such care.
- A5: edits stay within one component and vertex ids are not reused (as for the apex rule).
- (E1), (E2) per edit type: unchanged, reviewed with the bound (SAFE_ROUNDING.md section 1), not part of this lemma.
- Removed components (tiny, thin) are outside the theorem, as today.

### 3.5 In doubles

Every step that is computed, with u = 2^-53 and round to nearest:

1. A corner's distance, `ρ̂ = Vector3d.Distance(pos, α)` = `sqrt(fl(fl(dx dx + dy dy) + dz dz))` with
   `dx = fl(x - αx)`, …: five rounded operations per square-sum path and the root. With fl(a op b) = (a op b)(1 +
   e), |e| <= u: each computed square is at least the true one times (1 - u)^3, the sum at least the true sum times
   (1 - u)^5, the root squared at least R^2 (1 - u)^7, so the true distance `ρ <= ρ̂ (1 + u)^4`, **relative**, not
   absolute (part D of section 4; `(1 - u)^-7 <= (1 + u)^8` is a fact of two fractions). Underflow of a square (a
   coordinate difference below 2^-511) adds an absolute `ε0 <= 2^-536`; overflow gives infinity, which fails
   `<= L` and rejects the edit. No FMA: the code has none, and the banned-symbol rule keeps it so.
2. The maximum over the corners: exact.
3. `B̂ = fl(ĉ + ρ̂)`, and for a generic edit `N̂ = fl(max B̂ + δ̂)`: one rounding each; in the form of Higham
   (2.5), the true sum is at most the stored one times (1 + u).
4. The edit's own bound: `δ <= δ̂ + ε`, ε the absolute error of the distance computations of the generic edits (a
   few ulps of the largest coordinate, as in the theorem of SAFE_ROUNDING.md today; unchanged).
5. The comparison with L: exact.

**Claim.** Let k(t) be the number of generic edits behind `c_t` (0 at the start; a generic edit gives its new
triangles 1 + the largest k among its removed triangles and lost ghosts; displacement edits do not count). Then the
true carried bound satisfies `c_t <= ĉ_t (1 + u)^(6k) + k (ε + ε0)`, and

      H(S, S0) <= max B̂ (1 + u)^(6K + 5) + (K + 1)(ε + ε0),   K the largest k.

The induction step (one generic edit) is the obligation `chain_step_higham_2_5` of section 4; with the other model,
(2.4), it is satisfiable, as for the apex rule. Against today: displacement edits no longer lengthen the chain of
roundings, because their distances are computed anew from the anchors every time instead of being added to a stored
sum; in a patch of contractions K = 0 and the bound is `max B̂ (1 + u)^5 + ε0`.

## 4. The Z3 check

`tools/Z3Pilot/drift_reference.py`, run by `tools/Z3Pilot/test_drift_reference.py` in the `Z3 pilot` workflow as the
other pilots (`python -m unittest discover -s tools/Z3Pilot -p 'test_*.py'`; report
`tools/Z3Pilot/results/drift_reference_2026-11-09.json`). Every obligation UNSAT with satisfiable assumptions; every
mutant SAT with a witness replayed with fractions outside Z3; UNKNOWN never counts.

| part | what | result |
|---|---|---|
| A | convexity (C) in R^3: `2 e.f <= e.e + f.f` in coordinates, then `Σ λ_j λ_k G_jk <= r^2` over the Gram entries | 2 UNSAT; 3 mutants SAT (weights not summing to 1, one corner unbounded, no dot bound) |
| B | the one-edit lemma, shape-free, distances as real variables: (I1), (I2) of a displacement edit, (J1), (J2) and `N <= L` of a generic edit | 5 UNSAT; 5 mutants SAT (N from `c` instead of B, N without δ, no acceptance) |
| C | bounded search: chains of 3, 4, 5 vertices on the real line, **every** script of up to 3 edits (2 for 5 vertices) of move, contraction in place, contraction with the kept vertex moved, vertex removal, re-anchoring, with every real position | 7038 scripts, all UNSAT for: each segment within its bound of S0; S0 within the largest bound of S; every bound at most the apex rule's (section 3.3); no bound at the end above the reported drift |
| C | seven mutants of the rule on the same model | 7 SAT, each pinned as a regression case (below) |
| D | the computed distance (three steps) and the induction step of the chain bound | 4 UNSAT; the (2.4) model SAT |

About 3 minutes in all (137 s of solver time, 104 s of it the 4852 scripts of 4 vertices and 3 edits).

A first attempt at (C) on the coordinates directly (18 variables, degree 4) returned unknown at a timeout of 20 s, and a
first version of the search with a segment's point as `p + λ (q - p)` was nonlinear and returned unknown; both are
now stated so that the queries are decided (the Gram form; a point *between* the ends as its own variable). The
search is linear real arithmetic, so it is complete for the configurations listed.

**What the search is and is not.** On the line a connected chain covers the interval between its extreme vertices,
so the Hausdorff claim is about the ends of that interval and about each segment's own points. That exercises the
accounting (anchors, carried bounds, ghosts, renaming, lost ghosts, the order of evaluation), which is where a rule
like this goes wrong, and not the geometry: the two-sided bounds δ of the edit types stay review-only, as for the
apex rule, and a ghost on an *edge* does not exist in a 1-complex (its loss is covered by part B and by the check
mode of the implementation, section 7).

**Counterexamples found, pinned as regression cases** (`PINNED` in `test_drift_reference.py`, replayed without Z3:
the mutant breaks its claim, the rule keeps all four claims and stays below the apex rule):

| mutant | script (start vertices 0, 1, …) | what goes wrong |
|---|---|---|
| no ghosts | contract 0 into 1 | the start's end at vertex 0 is covered by nothing |
| ghosts dropped with their vertex | contract 0 into 1, 1 into 2 | the same, one contraction later |
| generic edit takes `c`, not B | move 0, re-anchor 0 | the move is forgotten |
| generic edit ignores lost ghosts | contract 2 into 1, 1 into 3, remove 3 | start vertex 2 is a spike; both its segments collapsed onto vertex 3, which is then removed: bound 1/2, true distance 3/4 |
| generic edit without δ | remove 1 | |
| renamed corner takes the kept vertex's position as anchor | move 0, contract 1 into 0 | the per-vertex anchor: the contracted vertex's own displacement is lost |
| a move does not evaluate the ghosts at its vertex | contract 0 into 1, move 1 | the reported drift (3/4) is below a ghost's bound at the end |

The spike case is the reason for the ghosts and shows that they cannot be dropped as "covered by the neighbours":
after two contractions no triangle is left that has the spike's tip as an anchor. It is also why a per-vertex
anchor (one origin per vertex) is not enough.

Not checked by Z3 (review): (E1), (E2); assumptions A2 to A5; the induction over the edits; that the code's edits
are of the two kinds (A1 is held by `source_contract()`, which fails when `try_move`, `try_collapse`, `set_vertex`
or `Vector3d.Distance` change); the rounding model itself.

## 5. What it would fix (prototype, not committed as code)

The prototype is `benchmarks/BooleanComparison/perf/d1/reference_rule_prototype.patch` (153 lines in
`MeshRoundingRepair.cs`, switched by `G3_D1=ref`, plus a probe command); `run.sh` there applies it in a scratch
worktree, fetches the models at run time and prints the table (`table.py`). Machine: this cloud session, 4 cores;
times are single runs and only show "no slowdown".

**Thingi10K** (`thingi_2026-11-09.txt`; the harness's pair: the model with its copy rotated by 20° and shifted, as
the 16 239-operation run): the 14 models with D1 operations and 119829 / 119830, 48 operations.

| | master (apex rule) | reference rule |
|---|---|---|
| operations not clean (`RoundingClean` false or `NotCleanReason`) | 13 | 0 |
| newly not clean | – | 0 |
| degenerate triangles left, sum | 115 | 0 |
| 119829 / 119830 union, difference | 23 / 5 left each | 0, drift 0.30 / 0.45 of the bound |
| largest measured distance to the unrepaired result | 0.24 of the bound | 0.24 of the bound |
| time, sum of the 48 | 62.3 s | 62.9 s |

The 13: 100344 union and difference, 118037 intersection, 119829 and 119830 union and difference, 1514904
intersection, 472043 and 472046 difference, 548875 intersection, 686056 union and difference. Where the edits are
contractions (100344, 686056, 119829 / 119830, the unions of the last two with one flip among 13446 edits) the drift drops to 0.30-0.54; where flips and strip zips are
mixed in (118037, 1514904, 472043, 472046, 548875), it stays near the bound (0.93-0.99) and the gain is the last few edits. In every operation with edits the
reported drift is above the measured distance (at most 0.24 of the bound). The measured distance has a floor of
about 0.002 of the bound that is not repair displacement: 669979 union has no edit, a drift of 0 and a measured
0.0018 under both rules (the comparison is against a second run without the repair, and the point-triangle distance
has its own rounding), so the measured column is good to about that.

How this relates to "20 D1 operations": the triage counted 20 on the run before the apex rule; the 14 models are
those operations' models (ROUNDING_DRIFT_APEX.md section 5: 21 of their 42 operations unclean then, 9 after the apex
rule). On today's master 9 of the 42 are unclean, and the prototype clears all 9, plus the 4 of 119829 / 119830.
A full cohort run (16 239 operations: clean-status changes of operations that are clean today, `MaxDrift`
distribution, times) is not done here; it is a runner job and the gate before a merge (section 7).

**The 12 operations that became unclean against the #385 baseline** (FINAL-1 control of Boolean 2.0, all
"degenerate triangles"; `thingi_regressions_2026-11-09.txt` for the nine models not in the set above, same probe):

| operation | master: left, drift, smallest rejected | reference rule | D1 mechanism |
|---|---|---|---|
| 103194 intersection | 1, 0.56, 1.017 | clean, 0.65 | yes |
| 1036402 difference | 1, 0.94, 1.027 | clean, 0.93 | yes |
| 118037 intersection | 1, 0.91, 1.161 | clean, 0.93 | yes |
| 135793 intersection | 1, 0.99, 1.050 | clean, 0.76 | yes |
| 366253 intersection | 1, 0.87, 1.123 | clean, 0.85 | yes |
| 471990 intersection | 1, 0.97, 1.010 | clean, 0.99 | yes |
| 548875 intersection | 1, 1.00, 1.006 | clean, 0.94 | yes |
| 837999 intersection | 3, 0.02, no edit rejected by the bound | 3 left, unchanged | no: no edit improves, the bound is not involved |
| 837999 difference | 1, 0.79, 1.250 | 1 left, unchanged | no: the smallest rejected edit needs 1.25 times the bound under both rules |
| 838003 difference | 2, 0.97, 1.095 | 2 left, 1.105 | no: 1.10 times the bound under both rules |
| 958469 difference | 3, 0.46, 1.534 | 3 left, 0.37 | no |
| 958470 difference | 2, 0.54, 5.257 | 2 left, unchanged | no |

7 of the 12 are cleared; the other 5 are not budget cases (their rejected edits need 1.1 to 5.3 times the bound
under either accounting, or none is rejected), so no accounting helps them: they are single edits larger than
`maxLength`, a question of the edit set or of the bound. Among the 27 operations of these nine models none becomes
unclean with the rule (135793 union and difference report self-intersections by `NotCleanReason` under both rules,
with `RoundingClean` true: not a rounding defect).

**Procedural** (`dense_patch_2026-11-09.txt`): unions of the dense-patch sphere with its copy, patch radius 1e-5 to
2e-4 and edge 3.3e-6 or 1.5e-6 of the diagonal (128 to 413k degenerate triangles at the start): master leaves
degenerate triangles in all 8 (1 to 364k); the prototype is clean in 6 with a drift of 0.34-0.52, and the two
largest (105k and 413k degenerate triangles) stop at the existing effort limit `EvaluationLimitReached` under both
rules, with more repaired. One of them, 105k triangles, reaches a drift of 0.96 under the prototype without a
generic edit: it has 5 moves of `fix_degenerate`, whose steps go up to the bound itself, and its
`SmallestRejectedDrift` comes from the moves past the limit, which the prototype still computes from the old array;
not analysed further. The direct repairs of the patch (6 distinct meshes, each at the bound and at four times the bound; the
rows for the edges 2e-6 and 2.5e-6 are the same meshes): master is not clean in 4 of 6 at the bound and in 2 of 6 at
four times the bound; the prototype is clean in all 12 with a drift of 0.14-0.33 of the bound.

**Existing tests under the prototype** (404 tests of the rounding and the exact booleans, `G3_D1=ref`): 397 pass (`tests_under_prototype_2026-11-09.txt`).
Of the 7 failures, 6 are pins of "not clean today" that it repairs (`RoundingDenseTessellationTests` ×2,
`RoundingDriftClusterTests` ×2, `SafeRoundingBoundTests.StrictRounding_FailsWithTheReason_InsteadOfDefects`, and
`Utm_CleanWithTheUlpTolerance_WithinIt`: 102 of 114 clean with the relative bound only, pinned at most 95), and 1 is
the prototype's shortcut (`PastTheLimit_SameAsProposingInFull`: the moves past the evaluation limit still read the
old `drift` array). The audit `ReportedDrift_BoundsTheMeasuredDistance_AllSimilarities` passes under the prototype.

**What it does not fix.** Clusters dominated by generic edits still add (`N = max B + δ` per flip or zip), and the
strip zip's bound 2w is loose by itself (measured 0.91 · 2w at most, SAFE_ROUNDING.md); 1053875 (0.98-1.00 under both rules) and the intersection and difference of 129937 (0.97-0.99) are clean but sit at
the bound either way. A tighter bound for those would need the correspondence between S0 and S
through generic edits (Cohen et al.'s successive mappings, section 8), a different size of work. The degeneracy
criterion (well-shaped tiny triangles, the open question for 2.x) is independent: with it 119829 / 119830 would have
nothing to contract.

## 6. Tests in this PR

- `tools/Z3Pilot/test_drift_reference.py`: the gate above; the pinned counterexamples; the rule tighter than the
  apex rule on a chain; the specification read by the model; UNKNOWN is not a pass; the source contract and its
  reaction to four changes of the code.
- `tests/geometry3Sharp.Tests/RoundingDriftClusterTests.cs`: the D1 mechanism on a procedural patch repaired
  directly (the start surface is the input): the budget used up at a measured distance below 0.001 of it; a clean
  result within the original bound exists (four times the bound); the reported drift bounds the measured distance
  for `Similarity.All()`, with bit-identical results for the 2^k scales; the same result twice. The assertions
  marked "D1 pin" are today's accounting and change when the rule is built; the audit ones hold for any sound rule.
  The audit is weak on this patch (the measured distance is below 0.001 of the bound, so it only catches a rule that
  reports almost nothing); the tests with power against a wrong rule are the spike meshes of the plan (section 7).

## 7. Recommendation and plan

**Recommendation: build it**, as the next step of D1, tier L. It is a strict relaxation of today's acceptance
(section 3.3), its lemma is small and checked, it clears every D1 operation of the probe and the 119829 / 119830
residue at no measurable cost, and it lowers the reported `MaxDrift` (a tighter certificate for every user of the
bound). Effort: about 250-350 lines in `MeshRoundingRepair.cs` and the tests; 2-3 days with review and the cohort
run. Risk: results change on every operation with a rejected edit today (as with the apex rule), so the cohort run
decides, not the 48 operations here.

Plan, one PR:

1. **Code** (`mesh_ops/MeshRoundingRepair.cs` only; it is not one of the exact files, but nothing of the plan needs
   FMA, `float` or a parallel loop: the state is written by the serial edit loop only).
   - State: `refCarry[tid]`, `refAnchor[3 tid + k]`, allocated lazily or at `Apply` (72 + 8 bytes per triangle;
     520k triangles: 42 MB; lazily, per touched triangle, if the memory profile of the large inputs says so); ghosts
     as records listed per vertex.
   - `improves`: the bound of the proposal by the kind of the edit (`movedV >= 0`: displacement); `commit_drift`
     replaced; `drift`, `editApex`, `apex_of` and the apex rule go (prefer deleting; `drift_apex.py`'s source
     contract is then retired with a note, its lemma stays as the proof of 3.3's comparison).
   - The moves past the evaluation limit (`frozenMost`) compute the same bound from the anchors.
   - `SmallestRejectedDrift`, `MaxDrift`: the same meaning (the value compared with L).
   - Check mode (`CheckOnThisThread`): A2 (a new ghost's edge exists), A4 (ids and corner order kept), a lost
     ghost's vertex or edge is a face of a removed triangle, every added triangle found after the edit, and the
     bound recomputed from scratch.
   - Ghost lists: no cap is needed for correctness; if a profile shows long lists at one vertex, entries dominated
     at the same corner (`c' >= c + |α - α'|`) can be dropped (a cone-domination lemma, to be added to the Z3 file
     first).
2. **Tests.**
   - Fail on the old code: `RoundingDriftClusterTests` and `RoundingDenseTessellationTests` turned from "not clean"
     to clean with `MaxDrift` below half the bound; 119829-like union clean.
   - The pinned counterexamples of section 4 as meshes (a spike whose edges are contracted from both sides and
     whose remaining vertex is then removed or flipped away; a per-vertex-anchor case): the reported drift must
     bound the measured distance. Mutants in the code must fail them: no ghosts, ghosts not renamed, N from `c`,
     N without the lost ghosts, N without δ.
   - The audit over `Similarity.All()` (exists), the 2^k scales bit for bit (exists), reuse (`Apply` on a second
     copy, and a second `Apply` on the result), serial equals parallel (the search is parallel, the accounting is
     not; `RoundingDriftApexTests.Needles_SerialEqualsParallel_ApplyTwice` as the model).
   - Every edit type in check mode (`CheckMode_EveryEditType_SameResultAsWithout` extended).
   - Time: all of them within 5 s; the dense patch here runs in about 0.2 s (the xunit duration of
     `DensePatch_BudgetUsedUpByTheAccounting_NotByTheDistance`).
3. **Z3.** `drift_reference.py` gets its source contract of the rule (the text of the new `improves` part and
   commit, as `drift_apex.py` has today); the workflow already runs on changes of `MeshRoundingRepair.cs`.
4. **Mutation.** Shards `mesh-rounding-1` to `-7` (the byte ranges of `MeshRoundingRepair.cs` shift, so the
   configs change and the affected parts run in full on master after the merge, per the owner's rule of
   2026-11-09); new test classes named to match the shards' filter (`MeshRoundingRepairTests…`,
   `SafeRoundingBoundTests…`) or the filter extended by the coordinator.
5. **Gate before ready.** The cohort run as a runner job (the 16 239 operations: clean-status changes both ways,
   `MaxDrift` distribution, times, the operations without a rejected edit must come out the same, apart from an edit within
   a few units in the last place of the bound, section 3.3), then the thingi-pairs control run with
   the Boolean 2.0 final. Stop rule: any operation that is clean today and not clean with the rule is analysed
   before the merge (order dependence, as 548875 was).
6. **Docs.** SAFE_ROUNDING.md section 1 (the drift paragraph and the proof), the class comment, OPERATIONS.md if
   `MaxDrift`'s wording changes, the roadmap.

Not proposed: a wider bound; a per-vertex origin (unsound, section 4); direct distance queries against S0 per edit
(one-sided only without a correspondence, and a second spatial index).

## 7a. Hand-over for the build (2026-11-10)

The owner decided to build the rule (2026-11-10, [roadmap/research.md](../roadmap/research.md)); a later session
builds it. What exists and what is left:

**Where things are.**
- The plan: section 7. The lemma and the assumptions the code must keep: sections 3.1, 3.4.
- The prototype (a patch on ca831be, quick and not for merging): `benchmarks/BooleanComparison/perf/d1/`.
- A first real implementation, work in progress: branch `claude/d1-reference-rule` (commit ebeaf737, on top of this
  PR's branch; rebase it onto master once this PR is merged). It replaces the per-vertex drift and the apex rule in
  `mesh_ops/MeshRoundingRepair.cs` by `refAnchor` / `refCarry` per triangle id and `Ghost` records per vertex
  (`proposed_drift`, `commit_drift`, `move_bound` for the moves past the evaluation limit, `save_references` /
  `restore_reference` in the three undo paths, `check_bounds` / `check_ghosts` in check mode). State: it builds; of
  409 tests of the rounding and the exact booleans (the filter of `tests_under_prototype_2026-11-09.txt`) 403 pass,
  the 6 failures are the pins below. Not done on it: everything in the list that follows.

**Left to do, in this order.**
1. The 6 pins of "not clean today" that the rule repairs (change them to the clean result and say so in the PR):
   `RoundingDenseTessellationTests.DensePatch_Union_ValidNotCleanWithinTheBound` and
   `…_StrictRounding_RoundingNotClean`; `RoundingDriftClusterTests.DensePatch_BudgetUsedUpByTheAccounting_NotByTheDistance`
   and `…_WiderBound_CleanWithinTheOriginalBound` (the assertions marked "D1 pin");
   `SafeRoundingBoundTests.StrictRounding_FailsWithTheReason_InsteadOfDefects` ("Cluster k0 count 4 seed 0" is now
   clean: it needs another case that still fails) and `Utm_CleanWithTheUlpTolerance_WithinIt` (102 of 114 clean with
   the relative bound only; the pin is `<= 95`).
2. New tests (each must fail with the matching mutant in the code; names that match the rounding shards' filter,
   e.g. in `MeshRoundingRepairTests` or `SafeRoundingBoundTests`):
   - the 7 pinned counterexamples of `tools/Z3Pilot/test_drift_reference.py` (`PINNED`) as meshes, with the
     reported drift checked against a distance measured as in `RoundingDriftClusterTests.Measured`, on inputs where
     that distance is a large share of the bound (a spike, not the sphere patch, where it is below 0.001):
     no ghosts; ghosts not renamed in a contraction; a generic edit that takes `c` instead of B; one that ignores
     the lost ghosts (a spike vertex whose edges are contracted from both sides onto a vertex that a flip or a
     vertex removal then takes away); one without δ; a renamed corner that takes the kept vertex as anchor; a move
     that does not evaluate the ghosts at its vertex. The repair chooses its own edits, so each mesh has to be built
     so that the wanted sequence is the only one that improves; where that is not possible, drive the edits through
     an internal test hook rather than weaken the case;
   - a ghost on an *edge* that is lost (the Z3 chain has ghosts at vertices only): contract a vertex of valence 3 or
     4, then flip the edge the collapsed triangles lie on;
   - the undo paths (`try_zip`, `try_strip`, `try_remove_vertex` when an append fails) keep the records: not
     reachable from a valid mesh today ("not expected" in the code), so a check-mode assertion, not a test;
   - `Similarity.All()` with bit-identical 2^k scales (exists in `RoundingDriftClusterTests` and
     `RoundingDriftApexTests`), `Apply` twice and on a second copy, serial equals parallel
     (`RoundingDriftApexTests.Needles_SerialEqualsParallel_ApplyTwice`), every edit type in check mode
     (`CheckMode_EveryEditType_SameResultAsWithout`).
3. `RoundingDriftApexTests`: its class comment and the tests that speak of the apex are about the rule that goes;
   the needles case stays valid (the reference rule repairs it too) and should be kept under a name of its own.
4. Z3: `tools/Z3Pilot/drift_apex.py`'s `source_contract()` compares the text of `improves`, `within_bound`, `apex_of`
   and `commit_drift` and **fails on the new code** (so does `test_pilot.DriftApexTests.test_source_contract*`):
   retire that contract with a note (the lemma stays as the comparison of section 3.3) and give
   `drift_reference.source_contract()` the contract of the rule (the text of `proposed_drift` and `commit_drift`).
   `drift_reference.py`'s present contract also changes: `movedV = ...` assignments and `try_move` are as before on
   the branch, check it.
5. Docs: the class comment of `MeshRoundingRepair` (the "Bounded displacement" paragraph), SAFE_ROUNDING.md section 1
   (drift paragraph, proof), ROUNDING_DRIFT_APEX.md (status: superseded), OPERATIONS.md if it words `MaxDrift`.
6. Mutation: `tests/mutation/stryker-mesh-rounding-1..7.json` hold character offsets into
   `MeshRoundingRepair.cs` (split at `position_add`, `degenerate_triangles`, `segments_distance`, `strip_around`,
   `ulp`, `set_vertex`; tests/mutation/README.md, "The splits are character offsets"); the change moves all of
   them, `MutationConfigTests` fails until they are moved back to their methods, and the changed configs make the
   parts run in full on master after the merge (owner's rule of 2026-11-09).
7. The full local CI list of CLAUDE.md (existing library code changes), an independent code-only review with
   `docs/REVIEW_CHECKLIST.md` (every bound of section 3.5 against the code), and the Boolean session's review.
8. The cohort run, the gate before ready (below).

**Pitfalls met.**
- The value compared with the bound for a displacement edit must include the ghosts at the moved *and* the
  vanishing vertex, at the new position; and for a contraction with the kept vertex in place, the kept vertex's
  other triangles are not part of the edit and their bounds must not be re-checked against the edit's drift (the
  first check mode did, and threw on valid edits).
- `improves` knows a displacement edit by `movedV >= 0`, which `try_move` / `try_collapse` reset right after the
  call: the commit needs the kept and the vanishing vertex saved at the proposal.
- `FlipEdge` keeps the two triangle ids with new corners; zips, strip zips and vertex removals append. The record of
  an added triangle is written after the edit by `FindTriangle`, in the mesh's corner order.
- `SmallestRejectedDrift` and the moves past the evaluation limit: `MeshRoundingRepairIncrementalTests.
  PastTheLimit_SameAsProposingInFull` compares the shortcut with the full proposal, so `move_bound` must compute
  exactly what `proposed_drift` computes for a move (the same maxima of the same doubles).
- Results are not bit-identical to the apex rule's even without a rejected edit in rare cases: the two rules compute
  different expressions (section 3.3).
- The measured distance has a floor of about 0.002 of the bound (section 5); compare with a slack.

**The cohort job (draft; the runner executes literally, so fill in the two commits and check the paths against the
runner's other jobs on `runs/boolean2-final`, `runs/final/FinalRunner`).**

    JOB: D1 reference rule, cohort run of the Thingi10K target class (16 239 operations)
    Baseline: master at <commit A>. Candidate: the library PR's head <commit B>. Both built in this job, on this
    runner, Release, one after the other; the same model list and pairing as the Boolean 2.0 confirmation run
    (B = A rotated 20 degrees about (1, 1, 0) and shifted by a quarter of the box: BooleanComparison export).
    Per operation and build one JSON row: model, op, ok, rounding_clean, NotCleanReason, remaining degenerate,
    remaining intersections, MaxDrift / MaxLength, SmallestRejectedDrift / MaxLength, the edit counts
    (Collapses, Flips, Moves, Zips, StripZips, VertexRemovals), EvaluationLimitReached, ms of the rounding and of
    the operation, a hash of the result (as perf/hashes.py).
    Resumable: one row per case, a rerun skips finished rows; partial rows pushed to runs/d1-cohort hourly.
    Report (a committed script, not by hand):
      1. clean -> not clean, and not clean -> clean, each as a list (model, op, reason);
      2. operations whose baseline row has SmallestRejectedDrift = Infinity (no edit rejected by the bound): the
         hashes must be equal; list every difference;
      3. the distribution of MaxDrift / MaxLength (deciles) per build;
      4. times: sum, median, p95, max of the rounding and of the operation per build, and the operations slower
         by more than 20 % and 0.2 s.
    Stop rule for the PR: any operation in list 1 "clean -> not clean" or in list 2 is analysed before the merge.

## 7b. The build (Boolean 2.1)

- Code: `refAnchor` / `refCarry` per triangle id, `Ghost` records per vertex, `proposed_drift` / `commit_drift`;
  the ids of a generic edit's added triangles come from the edit (`addedIds`: `FlipEdge` keeps its two ids, the
  appends return theirs), not from a search by corners; a ghost is listed once per distinct vertex and taken off
  the lists when lost; check mode: references kept through contractions, ghosts on a vertex or edge of the mesh
  after every edit (also at the vertices a generic edit removed), every bound recomputed from the records at the
  end of the edits (`check_all_bounds`).
- A test hook (`Script`, internal): `Apply` makes the given edits, each checked by the drift bound only. The seven
  pinned counterexamples of section 4 are scripted 3D cases on a tent (a sphere with a poked triangle on a short
  edge) in `MeshRoundingRepairTestsDriftReference`; each fails with its mutant in the C# code (the reported drift
  falls below the measured distance by 10 % or more) and passes with the rule (measured distance 0.5 to 1 times the
  reported drift). The "ignores the lost ghosts" case is a ghost on an edge lost by a flip.
- The six pins of section 7a changed to the clean results (the dense patches clean with a drift below half the
  bound; the strict-rounding case is now `FoldedCluster k36 count 4 seed 1`; 102 of 114 UTM cases clean with the
  relative bound only).

## 8. Prior art (papers only; [S] from abstracts or search snippets, [M] from memory, not re-read here)

- **Milenkovic and Sacks 2019** (arXiv 1805.03252), read for SAFE_ROUNDING.md: displacement "at most d times the
  number of edits", no bound in terms of the input. Today's rule is of this kind; the reference rule is not.
- **Hobby 1999; Guibas and Marimont 1998** [M]: 2D snap rounding keeps every output chain within the pixel
  neighbourhood of *its input segment*: the bound is anchored to the input, never accumulated.
- **Halperin and Packer 2002; Packer 2008, "Iterated snap rounding with bounded drift"** [S]: iterating the rounding
  makes the drift of a segment large; the remedy bounds the drift against the original segment. The same disease
  and the same kind of cure in 2D.
- **Devillers, Lazard, Lenhart 2018; Valque and Lazard** (SAFE_ROUNDING.md sections 3 and 7): bounds relative to the
  input by construction (a grid), at a cost that was measured and closed (#313).
- **Rossignac and Borrel 1993** [M]: vertex clustering; the error of a cluster is the largest distance of a member
  to its representative. The reference rule for contractions only is exactly this, per triangle corner.
- **Klein, Liebich, Straßer 1996, "Mesh reduction with error control"** [S]: the Hausdorff distance to the
  *original* mesh is tracked per step, so errors do not accumulate.
- **Kobbelt, Campagna, Seidel 1998, "A general framework for mesh decimation"** [S]: names the alternative we have
  today: summing the deviation of each replaced submesh is "a very coarse but conservative estimate".
- **Cohen, Manocha, Olano 1997 / 2003, "Successive mappings"** [S]: a piecewise linear map between the original and
  the simplified surface gives tight bounds through every operation. The reference triangles are the part of such
  a map that costs nothing (the map is affine per triangle while only vertices move); generic edits break it, and
  there we fall back to adding, where successive mappings would continue.
- **Guéziec 1996; Bajaj and Schikore 1996** [M]: error volumes or error bounds stored per vertex or face and
  propagated: the accumulated kind.
- **Hu et al. 2018 (TetWild), Wang et al. 2020 (exact envelope)** [M]; ENVELOPE.md: containment in an ε-envelope of
  the input, checked directly: one-sided, no accumulation. The library has the check; it would give (J1) without
  bookkeeping but not (J2), and costs a query per edit.

Terms searched 2026-11-09: iterated snap rounding bounded drift; successive mappings error bound; mesh reduction
with error control Hausdorff; general framework mesh decimation error accumulation. No GPL or other copyleft code
was read (CGAL, Triangle, GEOS, TetGen), nor Trueform.
