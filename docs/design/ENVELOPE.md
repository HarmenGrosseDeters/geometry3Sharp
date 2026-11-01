# Polyhedral envelope containment (design, for the owner's approval)

**Status:** draft for review (ROADMAP stage 7, tier L: design before code). No code yet. It builds on the indirect
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

Sources: the paper (its method sections, summarized for this document from the owner's copy) and, as a reference
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
   - **filter:** the error rule of #250 extended by the absolute term for underflow of #221 (round to nearest,
     products that underflow with an error `≤ 2^-1075`), with a range gate (inputs 0 or of magnitude in
     `[2^-k, 2^k]`, `k` derived so that the degree-9 TPI predicate cannot overflow; the absolute term covers
     underflow). Beyond the gate: straight to the exact stage.
   - **exact stage:** per call, the inputs are aligned to their smallest exponent (`x = m 2^e`), which makes them
     integers of `53 + spread` bits, `spread` the exponent range within the call (small for local geometry). The
     generated `FixedArith` graph is generated for an input bound of 2 limbs (`b = 128`: spread up to 75 bits), with
     `BigInteger` beyond. The sign is invariant under the common power of two: scaling every coordinate by `2^-e`
     multiplies each sign factor by a positive power of two.
2. **Prepare / evaluate split.** The spec marks the construction part (`N`, `d` of the implicit point). The
   generator emits a struct with the construction's values (doubles with their error terms `c`, `m̃`; on the exact
   path its `FixedArith` limbs) and an evaluation against three points. The error analysis already has every value's
   `c` and `m̃`; the evaluation part starts from them.
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
  the envelope accepts more, with the caps of Prop. 3.1 at acute vertices.
- **Degenerate triangles:** a polyhedron around the longest edge or the point (as `MeshEnvelope`).
- **Search:** the existing box tree of the arrangement (`ExactMeshArrangement`'s array box tree) or
  `DMeshAABBTree3` over the polyhedra's boxes; candidate lists per query without allocation.
- **Queries:** as in section 3; the predicates of 4.1; no `BigInteger` on the common path.

### 4.3 What it does not do

No changes to the `Reducer`, the remeshers or the alpha wrap in this work: wiring a consumer in is its own PR and
decision after the acceptance.

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

1. **Generator for double inputs** (4.1, items 1 and 2): the filter with the absolute term and the range gate, the
   exact stage by exponent alignment (2 limbs, `BigInteger` beyond), the prepare / evaluate split; tests against
   the oracle at all scales; bounds review; mutation.
2. **Polyhedra and point / segment queries:** construction with its proven bound, the box tree, `Contains`,
   `ContainsSegment`; oracle and guarantee tests.
3. **Triangle query** (TPI critical points), the oracle against `MeshEnvelope`'s clipping on the same polyhedra,
   `benchmarks/ReducerBenchmark queries` numbers.
4. **Decision** on public API and consumers (owner), then one PR per consumer.

Stop rule: if after PR 3 the triangle query is not within a factor of 5 of the distance bounds on the
`ReducerBenchmark` cases, stop and write the finding here.

## 7. Questions for the owner

1. **Double inputs:** filter with the absolute term plus exponent alignment for the exact stage (proposal), or
   map the whole envelope to one integer grid (as the arrangement does; simpler predicates, but grids of more than
   64 bits for meshes with mixed magnitudes)?
2. **Polyhedron shape:** the volume-maximizing fractions of `MeshEnvelope` (0.8 / 0.6, more accepted) or
   `fast-envelope`'s ε / √3 (simpler)?
3. **API:** a new internal class first, the public decision after the acceptance (proposal); or replace
   `MeshEnvelope`'s implementation directly?
4. **Order:** the plan of section 6, one PR per step?

## 8. Sources

- B. Wang, T. Schneider, Y. Hu, M. Attene, D. Panozzo, *Exact and Efficient Polyhedral Envelope Containment Check*,
  ACM TOG 39(4), 2020; reference code `fast-envelope` (MIT; `src/` read).
- M. Attene, *Indirect Predicates for Geometric Constructions*, CAD 2020 (the indirect predicates).
- J. R. Shewchuk 1997 (expansions, not needed per INDIRECT_PREDICATES.md section 10).

## 9. Still to check in the paper

The method sections are summarized in section 3 (from the owner's copy, via the coordinator). Open: the exact
statement of Prop. 3.1 for degenerate and very thin triangles, and the proof's treatment of queries that touch a
facet or a polyhedron's edge (the openness of P makes such points "outside"; the tests of PR 2 and 3 must cover
them). If needed, the owner can upload the PDF into the session.
