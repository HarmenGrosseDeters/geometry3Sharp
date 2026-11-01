# Polyhedral envelope containment (design, for the owner's approval)

**Status:** approved by the owner (decisions in section 7; ROADMAP stage 7, tier L). PR 1 (double inputs) and PR 2 (`PolyhedralEnvelope`, internal: polyhedra, point and segment queries) are on #263; PR 3 (triangle queries) is next. It builds on the indirect
predicates of `docs/design/INDIRECT_PREDICATES.md` (#246, #250, #257).

## 1. Goal

An exact and fast test whether a point, segment or triangle lies in a polyhedral envelope around a triangle mesh
(B. Wang, T. Schneider, Y. Hu, M. Attene, D. Panozzo, *Exact and Efficient Polyhedral Envelope Containment Check*,
ACM TOG 2020). "Contained" must guarantee that every point of the query is within ε of the mesh, whatever the
rounding. Consumers, in order of value:

1. the `Reducer`'s `MaxDeviation` check (92-97 % of its time today, `benchmarks/ReducerBenchmark`);
2. the remeshers' quality passes that must stay near the input;
3. the alpha-wrap simplification (stage 7b).

## 2. Current state

- **`MeshEnvelope`** (prototype, `mesh_ops/MeshEnvelope.cs`, tested in `MeshEnvelopeTests`): per triangle a convex
  prism (a slab of 0.8 ε along the normal, 14 lateral half-spaces at 0.554 ε) given as planes (normal and offset in
  doubles); point / segment / triangle queries decided exactly in `BigInteger` after floating-point filters; a
  triangle by clipping the query against the prisms' cross-sections until nothing or a piece of positive area is
  left.
- **Cost** (`benchmarks/ReducerBenchmark`, `queries`; terrain 40 × 40, tolerance 0.003, 951 reduced triangles):
  the distance bounds (`TriangleDeviationBounds.IsWithin`) 62 µs per triangle, all 951 within; the envelope
  7109 µs per triangle (728 of 951 exact), 625 within. The reduced triangles span many prisms, the filters cannot
  decide, and the exact clipping runs. The envelope also accepts fewer triangles: its prisms are thinner than the
  ε ball.
- **Indirect predicates** (#246, #250): `Orient3dLPI` (a segment's crossing with the plane through three points,
  against three points) and `Orient3dTPI` (the point of three planes, each through three points, against three
  points), generated with a filter and an exact `FixedArith` stage; 47-84 ns per call when the filter decides,
  about 1-2 µs when it does not; for integer inputs `|x| ≤ 2^53`.

## 3. The algorithm (the paper, sections 3 and 4)

Sources: the paper (read in full from the owner's copy) and, as a reference
only, `fast-envelope` (github.com/wangbolun300/fast-envelope, MIT license checked: `LICENSE`, copyright 2019 the
authors; its `src/` read). Its bundled `indirectPredicates/` and `geogram_predicates/` stay unread (their licenses are
not checked, and nothing of them is needed: our predicates are generated).

**Setup.** Input: a triangle soup M (it may self-intersect or share vertices) and ε. Stage 1 builds one open convex
polyhedron P per input triangle; their union PS is the envelope. Stage 2 decides whether a query triangle T lies
strictly inside PS: a point on the boundary of PS counts as outside. The union is never built (an explicit Boolean
union took the authors 3 h for 312 faces).

**Stage 1: the polyhedron per triangle (Prop. 3.1).** With δ = ε / √3, P is bounded by: a floor and a ceiling plane
parallel to T at distance δ; three side planes orthogonal to T, each parallel to one edge at distance δ; and for every
acute or right vertex a cap plane at distance δ from the vertex, orthogonal to the line from the vertex to the
barycenter (obtuse vertices need none). Every point of P is within ε of T: the in-plane overshoot is at most √2 δ, so
the distance is at most √(2 δ² + δ²) = ε. The envelope is conservative (only δ thick on flat regions; about √3 times
the volume of the L2 envelope). **Each facet is an oriented plane given by three points**, not by coefficients and
not as a polygon: polygon corners in doubles are not exactly coplanar and could make P non-convex. Containment means
strictly below every plane. Other shapes (closer to L2, adaptive sizes) are allowed: only convexity and the
half-space representation matter.

**Stage 2: the containment theorem (Thm. 3.2).** T is inside PS if and only if:
- **C1:** each vertex of T is inside some P;
- **C2:** every point where an edge of T meets a facet of some P lies in a polyhedron P* ≠ P;
- **C3:** for every pair of facets F_l (of P_i) and F_m (of P_j), P_i = P_j allowed, the point T ∩ F_l ∩ F_m, if
  inside T, lies in a polyhedron P* other than P_i and P_j.

The proof uses that the P are open and PS is finite: the edges are covered by walking from polyhedron to
polyhedron, then the interior through the vertices of the covering's boundary polygon.

**Predicates: no constructions, no divisions.**
- C1: orient3d of a vertex against each facet's three points.
- C2: `orient3d_LPI(r, s; t, u, v; a, b, c)`, the sign of orient3d(p, a, b, c) for the point p of line rs and plane
  tuv. The paper writes p = r + (α / β)(s − r) and multiplies all three rows by β, so its determinant is β³ times the
  orientation. Ours (`Orient3dLPI`, #246) multiplies only the row of p: `det(N − d c, a − c, b − c)` with `d` its
  denominator, so the sign is that of the determinant times `sign(d)`, two degrees lower (6 instead of 8 in the
  coordinates). That p lies on the segment is checked separately: r and s strictly on opposite sides of plane tuv
  (plain orient3d), which also guarantees `d ≠ 0`.
- C3: `orient3d_TPI(three planes; q1, q2, q3)`: normals from each plane's points, offsets, β the determinant of the
  normals, the numerator by Cramer's rule; ours is `Orient3dTPI` (#246), again with the point's row alone multiplied.
  Whether p lies inside T: three `orient3d_TPI` calls against the planes through each edge of T and a point off T's
  plane.
- Evaluation chain in the paper: a semi-static filter (Meyer & Pion style: a constant times the product of the
  per-axis maxima of the coordinate differences), then interval arithmetic, then expansions. Its constants (LPI: β
  about 5.11e-15 · δ1 δ2 δ3, the orientation about 1.39e-13 · six δs; TPI β about 8.89e-14 · six δs) are for its own
  formulas; ours are derived by our generator (#250) and are not copied.

**Algorithm (Listing 1, naive):** C1 for the three vertices; for every polyhedron, facet and edge whose endpoints are
strictly on opposite sides of the facet's plane, search a P' ≠ P containing the LPI point (strictly below all facets
of P'); none: OUT. For every pair of facets whose TPI point is inside T, search a P' outside {P_i, P_j} containing it;
none: OUT. Otherwise IN.

**Acceleration (section 3.2.4, about 100× together):**
- an AABB tree selects the candidate polyhedra;
- a float-only prefilter after C1: a vertex outside gives OUT; one P containing all three vertices gives IN;
  otherwise only the polyhedra whose facets may intersect T are kept (a polyhedron is dropped only when the filters
  are certain: about 30 % go); the prefilter also lists per polyhedron the facets that may intersect T, C2 and C3
  iterate over those only, and the inner orientation loop tests them first;
- covering: start from a polyhedron P_C containing a vertex, keep a cover set C; each new intersection point is
  searched in C first, then in PS; a point already covered by C creates no new points, else its polyhedron joins C
  and its intersection points are processed; edges first, then the interior.

**Results (paper):** a naive rational implementation is 100× slower than with their predicates and 10 000× slower
than the accelerated version; on Thingi10K, in QSlim and remeshing, the method is exact and faster than sampling or
Hausdorff bounds when the envelope is thin.

## 4. Design

### 4.1 Predicates (generator, `tools/PredicateCompiler`)

The predicates are the ones of #246 / #250. Three extensions are needed:

1. **Double inputs.** The polyhedra's points are arbitrary doubles, not integers of one grid. Two parts:
   - **filter:** the error rule of #250 with a range gate derived from the formula (inputs 0 or of magnitude in
     `[2^-a, 2^b]`): within it no value can underflow, so no absolute term is needed (built in PR 1; see
     INDIRECT_PREDICATES.md section 11: LPI `[2^-108, 2^164]`, TPI `[2^-54, 2^109]`). Beyond the gate: straight to the
     exact stage.
   - **exact stage:** per call, the inputs are aligned to their smallest exponent (`x = m 2^e`), which makes them
     integers of `53 + spread` bits, `spread` the exponent range within the call (small for local geometry). The
     generated `FixedArith` graph is generated for an input bound of 2 limbs (`b = 128`: spread up to 75 bits), with
     `BigInteger` beyond. The sign is invariant under the common power of two: scaling every coordinate by `2^-e`
     multiplies each sign factor by a positive power of two.
2. **Prepare / evaluate split** (built, 1b on #263). The spec marks the construction's points
   (`prepare p, q, r, s, t`). The generator emits:
   - a struct with those points, their gate, and the filter values that depend on them only, with m~ (for LPI: lp, lq,
     d);
   - `Prepare`;
   - a call against the other points. It computes the rest with the same operations in the same order, so its filter
     decides as the unsplit filter, bit for bit. Undecided, or outside the gate: the exact stage of the whole predicate.

   `ContainsSegment` prepares each crossing once. Measured in alternating rounds against 88d50d2 (`BooleanComparison
   envelope`, µs per segment): sphere 10: 66 / 86 → 41 / 48; sphere 20: 107 / 137 → 57 / 70; sphere 20 at 0.2 edge:
   28 / 28 → 13 / 17; sphere 40: 147 / 132 → 68 / 76. That is 1.6× to 2.2×, more than the operation count's
   estimate of 1.7×.
3. **No further predicates:** C1 and the segment-facet side tests are orient3d on explicit doubles
   (`ExactPredicates`); C2 is `Orient3dLPI`, C3 and the inside-T test of a TPI point are `Orient3dTPI` (section 3).
   The containment test needs nothing else.

### 4.2 The envelope

A new internal class `PolyhedralEnvelope` (`mesh_ops/`), with the query API of `MeshEnvelope`:
`Contains(Vector3d)`, `ContainsSegment(a, b)`, `ContainsTriangle(a, b, c)`, `Status`, thread-safe queries, a
snapshot of the mesh. Not public until the acceptance (section 5) and the owner decide: then either a public
class, or `MeshEnvelope`'s implementation replaced behind its unchanged API (compatible with upstream 1.0.324).

- **Polyhedra:** each facet as three points computed in double (the cap and side planes are constructed points).
  The polyhedron is then exactly the region strictly below those three-point planes; its distance to the triangle is
  ε only up to the rounding of the constructed points. The design takes that rounding off the offsets, as
  `MeshEnvelope` does: the computed points are within a proven bound of the ideal ones (a few ulps of the
  coordinates, derived in the code), the offsets are reduced by it, and the doc states the resulting guarantee:
  every point of every polyhedron within ε of its triangle, and the triangle strictly inside. Proposal for the
  shape (question 2): the paper allows any convex half-space shape; the slab and lateral fractions of `MeshEnvelope`
  (0.8 ε along the normal, 0.6 ε sideways: 0.8² + 0.6² = 1) give about 1.4 times δ = ε / √3 in each direction, so
  the envelope accepts more, with the caps of Prop. 3.1 at acute vertices. (Decided, section 7: ε / √3 is the
  default; 0.8 / 0.6 only as a proven option.)
- **Degenerate triangles:** a box around a non-zero edge (the longest) or the point (section 4.4).
- **Search:** the existing box tree of the arrangement (`ExactMeshArrangement`'s array box tree) or
  `DMeshAABBTree3` over the polyhedra's boxes. PR 2 uses `TriangleBoxTree.QuerySegment`; it allocates a candidate list
  and a stack per query. Allocation-free lists come with the triangle query (PR 3).
- **Queries:** as in section 3; the predicates of 4.1; no `BigInteger` on the common path.
  - In PR 2, `ContainsSegment` tests every plane crossing against every candidate polyhedron: O((8 k)²) `Orient3dLPI`
    calls for k candidates. The paper's covering set (section 3.2.4) comes with the triangle query (PR 3), measured on
    `ReducerBenchmark`.
  - One triangle that cannot get a polyhedron (`TooThin`, `OutOfRange`) sets the status of the whole envelope, as in
    `MeshEnvelope`: a partial envelope would silently answer false in its region.

### 4.3 What it does not do

No changes to the `Reducer`, the remeshers or the alpha wrap in this work: wiring a consumer in is its own PR and
decision after the acceptance.

### 4.4 The polyhedra in floating point (PR 2)

The paper builds each facet from three points computed in floating point and does not bound their rounding. Here
the computed polyhedron `P~` is proven to lie inside the ideal polyhedron `P*` of Prop. 3.1 (exact directions, offset
δ = ε / √3), so the guarantee "every point of `P~` is within ε of T" holds whatever the rounding. That T is strictly
inside `P~` is not derived but checked exactly, with `ExactPredicates.Orient3d` of every vertex of T against every
facet. A failure of either check makes the status `TooThin`.

**Notation.**
- u = 2^-53.
- `c = v0` is the first vertex of T, so c ∈ T.
- ρ ≥ max_k |v_k − c| is an upper bound.
- R = ρ + 2ε.
- `h_T(m) = max_k m · v_k` is the support function of T.
- `P* = ∩ {x : m*_j · x ≤ h_T(m*_j) + δ}` with exact unit directions `m*_j`. On the paper's facets
  `h_T(m*_j)` is the paper's offset: a cap direction is supported by its acute vertex. Then P* ⊂ T ⊕ B(ε) ⊂ B(c, ρ + ε).

**Lemma 1 (direction error).** Let `m~` be any vector with |m~ − m*| ≤ θ, and |x − c| ≤ R. Then
`m* · x − h_T(m*) ≤ m~ · x − h_T(m~) + θ (R + ρ)`.
*Proof:* the support function of T − c is ρ-Lipschitz, and |(m* − m~) · (x − c)| ≤ θ R.

**Lemma 2 (three points).** Let φ~(x) = m~ · x − s~, and let q~_0, q~_1, q~_2 be the facet's double points with
|φ~(q~_k)| ≤ μ. Then the affine function φ' that vanishes on the plane through the q~_k and has the same gradient
component along m~ satisfies |φ' − φ~| ≤ Λ μ on B(c, R). Here Λ ≥ max Σ_k |λ_k(x)|, where λ_k are the barycentric
coordinates of the projection of x (along m~) with respect to the projected q~_k.

The points are `q~_0 = fl(o)`, `q~_1 = fl(o + S a)`, `q~_2 = fl(o + S b)`, with:
- `o = c + t m~` on the plane;
- a, b unit vectors orthogonal to m~ (up to rounding);
- S = 4 R.

With them Σ |λ_k| ≤ 1 + 2 √2 (R + ζ) / (S (1 − κ)) ≤ 2 =: Λ. Here ζ ≤ R / 100 bounds the rounding of q~_0 and κ ≤ 1 / 100
the non-orthogonality of a, b and the rounding of q~_1, q~_2. The build checks √3 u ‖q~‖_∞ ≤ R / 200, which gives ζ and
the rounding part of κ. The non-orthogonality of a and b is derived, not checked: it is a few u. μ is computed per
facet a posteriori: |fl(m~ · q~_k − s~)| + γ_4 (Σ_i |m~_i q~_ki| + |s~|) + 1e-300 (products that underflow).

**Orientation.** The computed half-space is {x : `Orient3d(q~_0, q~_1, q~_2, x)` < 0}. It is {φ' < 0} only if
(q~_1 − q~_0) × (q~_2 − q~_0) has a positive component along m~. It does: b = m~ × a, so a × b ≈ m~. A flipped facet
would also fail the exact check that T is strictly inside (`TooThin`), so a wrong orientation cannot go unnoticed.

**Lemma 3 (convexity).** If every computed open half-space satisfies "x ∈ H~_j and |x − c| ≤ R ⇒ x ∈ H*_j", and c ∈ P~,
then P~ ⊂ P*.
*Proof:* if some y ∈ P~ had |y − c| > ρ + ε, the segment c → y would cross the sphere of radius
r = (ρ + ε + R) / 2 < R at a point z ∈ P~ (P~ is convex). By the hypothesis z ∈ P*, but P* ⊂ B(c, ρ + ε): a
contradiction. So P~ ⊂ B(c, ρ + ε) ⊂ B(c, R), and there the hypothesis gives P~ ⊂ P*.

**Offsets.** With Lemmas 1-3, a facet j is safe if
`s~_j ≤ h_T(m~_j) + δ − θ_j (R + ρ) − Λ μ_j`.
The code computes s~_j as follows:
- `h~_j = max_k fl(m~_j · v_k)`, with |h~_j − h_T(m~_j)| ≤ η_j = γ_3 max_k Σ_i |m~_ji v_ki| + 1e-300 (the code
  uses 4 u for γ_3);
- `margin_j = θ_j (R + ρ) + Λ μ_plan + η_j`;
- `corr_j = 4 u (|h~_j| + δ)`; the code computes `s~_j = fl(h~_j + fl(fl(δ − margin_j) − corr_j))`. The roundings
  cost at most u δ (the first subtraction), u δ (the second), u (|h~_j| + δ) (the sum) and 3 u δ / 10 (the margin's
  own sum, below δ / 10), in all less than corr_j;
- μ_plan = 32 u (‖c‖_∞ + 4 R) is planned before the points exist (the rounding of o, t, the products and the
  points, about 28 u ‖c‖_∞ + 84 u R with the a posteriori term), and μ_j ≤ μ_plan is checked after;
- `margin_j + corr_j ≤ δ / 10` is required, else `TooThin`. The polyhedron keeps at least 0.9 δ.

**Direction errors θ** (against the exact ideal unit vectors). The unit-vector routine scales by a power of two, so
that the largest component is in [1, 2), then divides by the rounded length: |unit(v~) − v / |v|| ≤ 2 α + 4 u when
|v~ − v| ≤ α |v|. The scaling is exact except for components that become subnormal, which lose at most 2^-1075
absolutely, against a length ≥ 1: covered by the one u the code adds to every θ. ρ is rounded up by 8 u; for
subnormal edges that is not relative, but the 2 ε in R covers it.
- **normal:** N = (v1 − v0) × (v2 − v0) is computed exactly (BigInteger on the doubles' mantissas), then converted to
  doubles. The shift truncates absolutely, below 2^-61 of the largest component, so the bound is in norm:
  |N~ − N| ≤ (u + √3 2^-61) |N| < 1.02 u |N| (a small component may be off by more, relatively). So θ_n ≤ 8 u, independent of the triangle's shape: slivers need no
  special case. N = 0 exactly means T is degenerate (see below).
- **side planes:** w*_k = unit(e_k × n*) (outward), computed from fl(e_k) and n~. |fl(e~ × n~) − e × n*| ≤
  (u + 8 u + √2 γ_2) |e| ≤ 12 u |e| (|e × n*| = |e|), so θ_w = 28 u.
- **caps:** at every vertex whose computed dot product of the two edges is ≥ −(|a|² + |b|²) / 4. This includes every
  acute and right vertex, as Prop. 3.1 needs; an extra cap only shrinks P*. Direction w* = unit(−(a + b)), where
  |a + b|² ≥ (|a|² + |b|²) / 2 there, so fl(a~ + b~) has α ≤ 3 u and θ_c = 10 u.
- **degenerate T (N = 0):** all three vertices lie on one line (or coincide). The code takes the longest edge by the
  scaled length; the squares of edges below 1.5e-162 underflow and must not decide this. Any edge of non-zero length
  would do: the offsets are T's support, so P* is the box around T itself (a segment): ±d* along it, ±p*_1, ±p*_2
  perpendicular, all with offset δ. Every point of it is within √3 δ = ε of T.
  - d~ = unit(fl(e_L)): θ_d = 6 u.
  - p~_1 = unit(d~ × axis): the axis is the one of d~'s smallest component, so the cross product is exact and has length
    ≥ √(2/3) − 6 u. This gives θ_1 = 19 u.
  - p~_2 = unit(fl(d~ × p~_1)): θ_2 = 60 u.
  - A point (all three edges exactly zero) gets the axis cube: θ = 0.

**Search boxes:** P~ ⊂ T ⊕ B(ε), so the box of T expanded by ε (1 + 4 u) + 4 u ‖v‖_∞ contains P~.

For ε well above the margins, the polyhedron loses at most δ / 10 per facet. The margins add up to at most about
`61 u (R + ρ) + 64 u ‖c‖_∞ + 256 u R`, which is 4e-14 times the triangle size plus 7e-15 times the coordinates. So the
status is `TooThin` below ε ≈ 7e-13 times the triangle size plus 1.2e-13 times the coordinates; `MeshEnvelope` is
`TooThin` below about 1.3e-12 times the coordinates. The tests check a posteriori (`PolyhedralEnvelopeTests`), on
random, right-angled, sliver and degenerate triangles at scales 1, 2^-10, 1e±150 and at UTM coordinates:
- every θ against the exact ideal direction (400-bit fixed point);
- μ (exactly);
- Λ (over the ball);
- δ, ρ and R;
- the offset inequality, exactly with the true μ.

They also check that every vertex of the polyhedron is within ε of the triangle and that some vertex is beyond 0.85 ε,
so the polyhedron is not much smaller than the paper's.

## 5. Acceptance

- **Exactness against an oracle:** the same polyhedra, the query decided by an independent method: clipping the
  query triangle against the polyhedra in `BigInteger` (the method of `MeshEnvelope`, a different algorithm from
  the critical points), on random, nearly degenerate and exactly degenerate inputs (queries touching faces,
  edges, vertices; coplanar with faces; across many polyhedra), at every scale of `Similarity.All()` (2^k scales
  bit for bit).
- **The guarantee:** for every accepted query, sample points are within ε of the mesh (exact distance in
  `BigInteger` or with a proven bound); every input triangle is accepted.
- **Speed:** `benchmarks/ReducerBenchmark queries` (terrain 40 × 40, tolerance 0.003, and the bumpy sphere): time
  per triangle and triangles accepted, against `TriangleDeviationBounds.IsWithin` (62 µs, 951 within) and
  `MeshEnvelope.ContainsTriangle` (7109 µs, 625 within). Target for a consumer to be worth wiring: within a small
  factor of the distance bounds (the README estimates 20-50 µs for a filtered envelope; to be measured), and more
  triangles accepted than `MeshEnvelope`.
- **The usual rules:** invariance (`Similarity.All()`), determinism (parallel queries equal serial ones), reuse
  (queries after queries), bounds review of every generated bound and filter, mutation shards for the new files
  (`math/Generated/*`, the envelope), the exact-code analyzers on the new exact files.

## 6. PR plan

1. **Generator for double inputs** (4.1, item 1): the filter with the range gate, the exact stage by exponent
   alignment (2 limbs, `BigInteger` beyond); tests against the oracle at all scales; bounds review; mutation.
   1b. **Prepare / evaluate split** (4.1, item 2). It is a pure speed step, so it follows PR 2 (the same PR, #263)
   only if the share of the construction part in `ContainsSegment` makes it measurable (process rule: an upper-bound
   check before a speed step). Measured (`BooleanComparison envelope`, benchmark README): `Orient3dLPI` is about
   90 % of `ContainsSegment` (270 to 1800 calls per segment on spheres), and its construction part is about 30 of its
   65 filter operations. So the bound is about 1.7×, and 1b goes ahead. Built: 1.6× to 2.2× (section 4.1, item 2).
2. **Polyhedra and point / segment queries:** construction with its proven bound, the box tree, `Contains`,
   `ContainsSegment`; oracle and guarantee tests.
3. **Triangle query** (TPI critical points), the oracle against `MeshEnvelope`'s clipping on the same polyhedra,
   `benchmarks/ReducerBenchmark queries` numbers.
4. **Decision** on public API and consumers (owner), then one PR per consumer.

Stop rule: if after PR 3 the triangle query is not within a factor of 5 of the distance bounds on the
`ReducerBenchmark` cases, stop and write the finding here.

**Stop criterion met (PR 3, #268); under discussion with the owner.** The owner set the criterion: stop if the best
variant stays above 5× `IsWithin`, or accepts clearly fewer queries than `MeshEnvelope`. Both happen. The code is kept
and merges as an internal tool (owner decision); the decision on the stop itself is open.

Measured with `benchmarks/ReducerBenchmark queries` at a tolerance of 0.003 of the diagonal, on the triangles a
reduction with the distance bounds produces. Each entry is triangles accepted, µs per triangle, and the factor to
`IsWithin`:

| case | `IsWithin` | `MeshEnvelope` | ε/√3 (Prop. 3.1), naive | ε/√3, accelerated | 0.8 / 0.6 prism, accelerated |
|---|---|---|---|---|---|
| terrain 40 × 40, 951 triangles | 951, 48 | 625, 6425 | 164, 351 | 164, 359 (7.5×) | 625, 6206 (130×) |
| bumpy sphere, 536 triangles | 536, 229 | 258, 32826 | 41, 17175 | 41, 1708 (7.5×) | 258, 33375 (146×) |

- **Accelerations.** Two of the paper's: a cover set tried first, and C3 only on points in the closure of both
  polyhedra (the vertices of the boundary arrangement). A third is a box test on facet pairs. Together they make the
  query 10× faster on the bumpy sphere. The independent oracle agrees on every test case.
- **What remains.** Per query on the terrain there are about 360 facet pairs whose polyhedra's boxes meet, at about
  1 µs each, and the exact stage is rare (about 1 call per query). The pair loop is the cost.
- **Acceptance.** It is the shape's, not the algorithm's. ε/√3 reaches about 0.58 ε along the normal, and the
  `Reducer` pushes triangles to the tolerance, so it accepts 17 % and 8 % of what `IsWithin` certifies.
  - The 0.8 / 0.6 prism (`MeshEnvelope`'s shape: 2 + 14 facets) accepts exactly as many as `MeshEnvelope`.
  - With 16 facets per polyhedron it has 4× the pairs, and its time is that of `MeshEnvelope`'s clipping.
  - It was a measurement only (commit 9472f19, removed after it): no proof in this doc, no bounds review.
- **Why the paper's numbers do not carry over.** fast-envelope's users (fTetWild, the remeshers) ask many small,
  local queries that the prefilter decides: one polyhedron holds all three vertices. The `Reducer` asks about large
  reduced triangles that span tens of input triangles at the full tolerance. That is the worst case for both speed
  (many candidates and facet pairs) and acceptance.

**Look before you build (the owner's rule, AGENT_WORKFLOW §3), recorded for PR 2 / 3 and the accelerations.**
- **Library:**
  - Predicates: `ExactPredicates.Orient3d` for C1 and the side tests. `tools/PredicateCompiler` generates the
    indirect predicates (LPI / TPI with filters, the prepared form, `TPIDenominator`) instead of hand-derived bounds
    (ROADMAP "Shared hotspots": filter bounds by hand).
  - Candidates: segments use the arrangement's `TriangleBoxTree.QuerySegment` (closed, conservative boxes).
    Triangles use a linear scan over the boxes. At 3200-10092 polyhedra it costs a few µs of the 359-1708 µs.
    `TriangleBoxTree` has no box query, and adding one would change exact-arrangement code another session owns.
  - `DMeshAABBTree3` indexes a live mesh's triangles, not the snapshot's polyhedron boxes. The quadtree is 2D.
  - The construction follows `MeshEnvelope`'s structure (offsets from the support, rounding taken off the offsets) with
    its own proof (4.4). The test oracle reuses `MeshEnvelope`'s clipping method, plus the 1D covering that open
    polyhedra need.
  - OPERATIONS.md lists `Reducer` / `MeshEnvelope` for guaranteed deviation; nothing else in the library decides
    envelope containment.
- **Literature:**
  - Wang et al. 2020 and its reference code `fast-envelope` (MIT; `src/` read, the bundled predicate libraries not),
    for Prop. 3.1, Thm. 3.2, Listing 1 and the accelerations (cover set, boundary restriction, prefilter).
  - Attene 2020 for indirect predicates.
  - For the alternative direction: Cohen et al. 1996 (simplification envelopes), Borouchaki & Frey 2005 (Hausdorff
    envelope), and BVH upper bounds of the Hausdorff distance (e.g. "Cascading upper bounds for triangle soup
    Pompeiu-Hausdorff distance", CGF 2024, not read).
  - Sources are in docs/SOURCES.md. No GPL code was read or used.

Kept on #263 / #268, internal, as a tested exact tool: `PolyhedralEnvelope` (points, segments, triangles), the
generated indirect predicates on doubles with the prepared form, `TPIDenominator`, and the triangle oracle.
`MeshEnvelope` stays as it is. For the `Reducer`, the open direction is faster distance bounds
(`TriangleDeviationBounds`; see the BVH Hausdorff upper bounds in the literature, ROADMAP Phase 5 #2), not an
envelope.

## 7. Decisions (owner, on the questions of the draft)

1. **Double inputs:** a filter with an underflow term plus exponent alignment per call for the exact stage (4.1). No
   integer grid.
2. **Polyhedron shape:** ε / √3, the paper's Prop. 3.1 (proven), is the default. PR 3 measures how much less it
   accepts than `MeshEnvelope`'s 0.8 / 0.6 shape on the `ReducerBenchmark` queries. 0.8 / 0.6 is offered as an
   option only with a proof of the same ε guarantee; otherwise this document says why not.
3. **API:** an internal class first. `MeshEnvelope` is replaced only once the clipping oracle agrees and the stop rule
   (within 5× of `IsWithin`) holds; that replacement is a separate PR for the owner.
4. **Order:** one PR per step (section 6).

## 8. Sources

- B. Wang, T. Schneider, Y. Hu, M. Attene, D. Panozzo, *Exact and Efficient Polyhedral Envelope Containment Check*,
  ACM TOG 39(4), 2020; reference code `fast-envelope` (MIT; `src/` read).
- M. Attene, *Indirect Predicates for Geometric Constructions*, CAD 2020 (the indirect predicates).
- J. R. Shewchuk 1997 (expansions, not needed per INDIRECT_PREDICATES.md section 10).

## 9. Notes from the paper's full text

- **Degenerate input triangles** are not treated in the paper: Prop. 3.1 assumes a proper triangle (its proof uses
  the barycenter and the vertex angles). `fast-envelope` handles them in code. Ours (PR 2) builds a polyhedron
  around the longest edge or the point, as `MeshEnvelope` does, with its own proof of the ε bound in the code and
  tests at the degenerate limit.
- **Queries touching the envelope:** the polyhedra are open and T counts as inside only if every point is in the
  interior of PS, so a vertex, LPI or TPI point on a facet is "not inside" (orient3d 0 is OUT). The proof of
  Thm. 3.2 relies on this openness. PR 2 and 3 test touching vertices, edges along facets and coplanar triangles.
- **Translation invariance.** The paper's semi-static filters use coordinate differences only (relative to a point
  of each plane triple and to the reference points). Our LPI / TPI specs (#246) still contain raw coordinates
  (`N − d c` with `p.x lq`); PR 1 rewrites them in differences, `(p − c) lq − (q − c) lp` for LPI and
  `Σ_k (n_k · (a_k − c)) e_k` for TPI (Cramer's identity), the same values and degree, so that the filter and the
  bounds do not grow with the distance from the origin (UTM-like coordinates).
- Appendix B shows why an explicit intersection point must not be rounded (an LPI point at (1/3, 1/3, 1/3) misread
  as off the plane); the predicates here never construct it.
