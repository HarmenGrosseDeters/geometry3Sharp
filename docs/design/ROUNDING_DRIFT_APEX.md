# SafeRounding: drift charged to the apex of an edit (D1 cause 1, the lemma for Z3 pilot 2 step 1)

Status: design note and probe, no library change. The rule is built only after Z3 has checked the lemma below
(ROADMAP, Z3 pilot 2 step 1) and an independent bound review (CLAUDE.md, a change to the D′ accounting).

## 1. Problem (Exact Boolean 2.0 finding D1, cause 1)

SafeRounding (`MeshRoundingRepair`) bounds how far the repaired surface moves from the rounded one: every vertex `v`
has a drift `d(v)` (0 at the start), and an edit that removes the triangles `R`, adds `A` and moves the surface by at
most `δ` (the per-edit bounds of SAFE_ROUNDING.md §1, unchanged here) is applied only if

    M + δ <= maxLength,   M = max { d(v) : v a vertex of R or A },

and then sets `d(v) = M + δ` for **every** vertex of `R` and `A`. In clusters of slivers the edits are neighbours, each
raises the drift of all its vertices, and the next edit starts from there: 1396900 union goes 0 → 0.357 → 0.432 →
0.835 → 0.993 × maxLength, after which every edit left in the cluster is over the bound. 20 Thingi10K cohort
operations are unclean for this reason (the rejected edits' vertices carry 0.989-0.993 × maxLength).

## 2. The rule: charge the apex

An **apex** of an edit is a vertex that lies in every triangle of `A` (`A` not empty). Moves and slides: the moved
vertex. Contractions (`a` vanishes, `b` kept): `b`. Vertex removals (valence 3, one new triangle): any of its three
corners. Flips of `ab` to `cd`: `c` or `d`. By the code (review of the Z3 pilot, #416):
- `try_zip` adds four triangles `(c,d,x), (d,b,x), (d,c,y), (c,a,y)`: no vertex is in all four, no apex, today's rule;
- `try_strip`: an apex `x` where all the strip's outer triangles share their tip, else none;
- `try_collapse` at an ear: with the kept vertex moved (`p != pos(b)`), `b`'s other triangles are in `R` and `A`, so
  `A` is not empty and `b` is the apex (the normal case); only an ear contraction that moves nothing has `A` empty and
  takes today's rule.

For every edit where the code finds an apex, its (E2) counterparts are triangles of `A` (not untouched triangles):
the cases with an untouched counterpart are exactly those with `A` empty.

    if the edit has an apex c:   d'(c) = M + δ,   d'(v) = d(v) for every other vertex
    otherwise:                    d'(v) = M + δ for every vertex v of R and A (as today)

The acceptance test (`M + δ <= maxLength`, `M` over all vertices of `R` and `A`) is unchanged. The implementation
takes the first vertex of `A[0]` that is in every added triangle, so the choice is deterministic.

## 3. The invariant and the lemma

Notation: `S0` the surface at the start of `Apply` (the rounded mesh), `S` the current one, a set of triangles;
`dist(x, Y)` the Euclidean distance of a point to a closed set; for a triangle `T`, `D(T) = max { d(v) : v ∈ T }`.

**Invariant** `I(S, d)`:
- (I1) for every triangle `T` of `S` and every point `x ∈ T`: `dist(x, S0) <= D(T)`;
- (I2) for every point `y ∈ S0` there is a triangle `T` of `S` with `dist(y, T) <= D(T)`.

`I(S0, 0)` holds. (I1) and (I2) give `H(S, S0) <= max d = MaxDrift`, the theorem of SAFE_ROUNDING.md §1.

**Edit assumptions** (proved per edit type in SAFE_ROUNDING.md §1 and the code comments; not part of this lemma):
- (E1) every point `x` of a triangle of `A` has a triangle `T ∈ R` with `dist(x, T) <= δ`;
- (E2) every point `x` of a triangle `T ∈ R` has a triangle `T' ∈ A` with `dist(x, T') <= δ` (the two-sided bounds;
  for edits with `A` empty the counterpart is an untouched triangle with all corners among the edit's vertices, so
  those edits use today's rule).

**Implicit assumptions** (true of the code, stated so the lemma's use is checkable):
- untouched triangles keep their geometry: every triangle of a moved vertex is in `R` (moves, slides, the kept vertex
  of a contraction);
- the rounding creates no vertices and reuses no vertex id (it appends none; a vanishing vertex's drift is not read
  again), so `d` is a function of the vertex for the whole repair; triangle ids may be reused, `D(T)` is taken from
  the triangle's current vertices.

**Lemma** (one edit). Let `I(S, d)`, (E1), (E2) hold, let `c` be an apex of the edit, `M = max d` over the vertices
of `R` and `A`, `S' = (S \ R) ∪ A`, and `d'` as in §2. Then `I(S', d')` holds, and `d' >= d` pointwise.

*Proof sketch* (what Z3 checks, §4):
- `d' >= d`: `d'(c) = M + δ >= d(c)` since `c` is a vertex of `A`, `M >= d(c)`, `δ >= 0`; other vertices unchanged.
- (I1) for `T' ∈ A`, `x ∈ T'`: by (E1) some `T ∈ R` and `z ∈ T` with `|x - z| <= δ`; by (I1) `dist(z, S0) <= D(T)
  <= M`; so `dist(x, S0) <= M + δ = d'(c) <= D'(T')`, because `c ∈ T'` (apex).
- (I1) for an untouched triangle: its vertices' drifts did not decrease.
- (I2) for `y ∈ S0`: by (I2) some `T ∈ S` with `dist(y, T) <= D(T)`. If `T` is untouched, done (`D'(T) >= D(T)`).
  If `T ∈ R`: a point `z ∈ T` with `|y - z| <= D(T) <= M`; by (E2) a `T' ∈ A` with `dist(z, T') <= δ`; so `dist(y,
  T') <= M + δ <= D'(T')` (apex). ∎

Without an apex the lemma is today's (every vertex of `A` gets `M + δ`, so `D'(T') = M + δ` for every `T' ∈ A`).

**In doubles.** The code computes `δ̂` and `M̂ + δ̂` in binary64. Model: `δ <= δ̂ + ε` with `ε` the absolute error of
the distance computation (a few ulps of the largest coordinate, as the theorem states today), and the stored sum in the
form of Higham (2.5): `M̂ + δ̂ = fl(M̂ + δ̂)(1 + η)`, `|η| <= u = 2^-53` (round to nearest), so the true sum is at most
`fl(M̂ + δ̂)(1 + u)`. Claim: the true drifts satisfy `d <= d̂ (1 + u)^k + k ε (1 + u)^k` after a chain of `k` edits,
so `H(S, S0) <= (MaxDrift + k ε)(1 + u)^k` (today's caveat, unchanged in form; the apex rule makes `k` the length of a
chain of edits through one vertex, not through a cluster; precisely, `k` is the depth of the charging DAG: an edit
charges its apex from the largest drift among its vertices, so `k` is the longest sequence of edits each of which read
a drift that the previous one wrote). With the other model, Higham (2.4), `fl(s) = s (1 + η)`,
the bound would need `(1 - u)^-k` instead: Z3 found the witness `M̂ = 1`, `δ = δ̂ = ε = 0`, stored `1 - u`, where
`(1 - u)(1 + u) < 1` (Z3 pilot 2 step 1, 2026-11-08). The acceptance test compares the stored `fl(M̂ + δ̂)` with
`maxLength`, so the guarantee reads `H <= maxLength (1 + u)^k + ...`, as today up to the rounding caveat.

## 4. What Z3 is asked to check (pilot 2, step 1)

Over the reals (the distances as real variables, the geometric facts as hypotheses), for one edit with a fixed
combinatorial shape, instantiated for each edit type that has an apex: a move or slide of a vertex of valence 3..8,
a contraction (the kept vertex with 2..6 triangles on each side, the two at the edge collapsing), a vertex removal of
valence 3, a flip.

- Variables: `d(v)` for the vertices, `δ >= 0`, `M`; for (I1): a point `x` of an added triangle, its witness `z` in a
  removed triangle, real distances `|x - z|`, `dist(z, S0)`, `dist(x, S0)` with the triangle inequality
  `dist(x, S0) <= |x - z| + dist(z, S0)`; for (I2): `y`, `z`, `dist(y, T)`, `dist(z, T')`, `dist(y, T')` likewise.
- Hypotheses: (I1), (I2) before, (E1), (E2) as the witnesses' bounds, `M` the maximum, `M + δ <= maxLength`.
- Claims (each as an UNSAT query of its negation): `I(S', d')` for the apex rule; `d' >= d`; `max d' <= maxLength`.
- Non-vacuity (SAT): the hypotheses; an edit where the apex rule leaves a non-apex vertex below `M + δ`.
- Mutants (SAT, counterexamples): charging a vertex that is **not** in every added triangle (eg `a` instead of `b`
  in a contraction, or a vertex of only one added triangle in a flip); charging `M` without `δ`; `M` over `A` only
  (dropping the removed triangles' vertices).
- The rounding model of §3 (`δ <= δ̂ + ε`, Higham (2.5): `s = fl(s)(1 + η)`, `|η| <= u`) for the bound
  `d <= (d̂ + k ε)(1 + u)^k` over a chain of `k` edits (an induction step: one edit, `k -> k + 1`).

The implementation's check mode asserts that the chosen apex lies in every added triangle.

Not checked by Z3: the per-edit bounds (E1), (E2) themselves (SAFE_ROUNDING.md §1, reviewed); that the code's apex is
in every added triangle (a test asserts it in check mode, §6).

## 5. Probe (2026-11-08, env-switched build, not committed)

The apex rule on the 14 Thingi10K models with D1 operations (42 operations), against master:

| | master | apex rule |
|---|---|---|
| unclean operations | 21 | 9 |
| newly unclean | – | 1 (548875 intersection) |
| results changed | – | 34 of 42 |

- 13 of the 21 become clean: 1012236 ×2, 1053875 union, 118037 ×2, 129937 ×2, 1396900 union, 472043 intersection and
  union, 548875 union, 548878 union, 551020 intersection.
- Still unclean: 100344 ×2, 1514904 intersection, 472043 difference, 472046 difference, 669979 intersection,
  686056 ×2 (other causes or longer chains through one vertex; not analysed yet).
- Times: within ±15 % (more edits accepted, slightly more work).
- The changed results are expected (more edits pass the bound); whether results that are clean today stay clean, and
  their drift, needs the cohort run before a PR (548875 intersection is one counterexample).

## 6. Plan

1. Z3 pilot 2 step 1 checks §3/§4 (the Z3 session; this note is its input).
2. Probe the regression (548875 intersection) and the cohort: clean-status changes on the 42-model set and the
   corpus, `MaxDrift` distribution, times (runner job).
3. Independent bound review of §2-§3.
4. Implementation with a check mode that asserts the apex is in every added triangle, tests on the D1 cases (fail on
   master), `Similarity.All()`, serial = parallel; its own PR.

## 7. Prior art

- Milenkovic and Sacks 2019: displacement at most d times the number of edits, no bound in terms of the input; the
  apex rule is the same kind of accounting, charged per vertex chain instead of per cluster.
- The invariant is the Hausdorff-distance induction of SAFE_ROUNDING.md §1 (D′); only the set of vertices charged
  changes.
