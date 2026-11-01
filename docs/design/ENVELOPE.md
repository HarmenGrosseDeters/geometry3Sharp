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

## 3. The algorithm (paper and `fast-envelope`, MIT)

Sources read: the paper's abstract and the reference implementation `fast-envelope`
(github.com/wangbolun300/fast-envelope, MIT license, checked: `LICENSE`, copyright 2019 the authors), its `src/`
only. Its bundled `indirectPredicates/` and `geogram_predicates/` were not read (their licenses are not checked, and
nothing of them is needed: our predicates are generated). The paper's full text could not be
fetched from this session (its hosts are outside the network policy); section 9 lists what to confirm in it.

- **Polyhedra.** Per input triangle a convex polyhedron that contains the triangle and lies within its ε
  neighbourhood: two caps parallel to the triangle at ±δ along the normal, one face per edge at δ outside it, and
  extra faces that cut the corners (at obtuse angles more). `fast-envelope` uses δ = ε / √3 (a cube-like bound:
  every point of the polyhedron is within √3 δ = ε of the triangle). **Each face is the plane through three points
  given as doubles**, not a plane equation: the faces are then exact, and every predicate is an orient3d on
  doubles or on implicit points built from doubles.
- **Point query:** inside some candidate polyhedron (orient3d against its faces).
- **Segment query:** both ends inside; for every polyhedron the segment crosses, the points where the segment
  crosses that polyhedron's faces (LPI points) must lie in some other polyhedron (`Orient3dLPI` against the other
  polyhedra's faces). If every such point is covered, the segment is.
- **Triangle query:** the three vertices; the three edges as segments; and the points where the triangle's plane
  meets a line of two faces of a crossed polyhedron (TPI points: the triangle's plane and two face planes), inside
  the triangle, must lie in some other polyhedron (`Orient3dTPI`). If all these critical points are covered, the
  triangle is (the paper's argument: the uncovered part of the triangle would have a vertex at one of them).
- **Search:** an AABB tree over the polyhedra's boxes gives the candidates of a query; the implementation keeps a
  queue of covering polyhedra and checks the most recent cover first.
- **Amortization:** an implicit point is set up once (`orient3D_LPI_prefilter` / `pre_exact`: its numerator and
  denominator with their error bounds) and then tested against many faces (`postfilter` / `post_exact`).

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
3. **More predicates:** `Orient3d` on explicit doubles exists (`ExactPredicates`); the segment-face and triangle-face
   cut tests are orient3d on explicit points; nothing else is needed for the containment test (to be confirmed
   against the paper, section 9).

### 4.2 The envelope

A new internal class `PolyhedralEnvelope` (`mesh_ops/`), with the query API of `MeshEnvelope`:
`Contains(Vector3d)`, `ContainsSegment(a, b)`, `ContainsTriangle(a, b, c)`, `Status`, thread-safe queries, a
snapshot of the mesh. Not public until the acceptance (section 5) and the owner decide: then either a public
class, or `MeshEnvelope`'s implementation replaced behind its unchanged API (compatible with upstream 1.0.324).

- **Polyhedra:** the faces as three double points each. The construction follows the guarantee, not
  `fast-envelope`'s constants: every point of the polyhedron within ε of its triangle, proven with the rounding of
  the construction (as `MeshEnvelope` does for its offsets), and the triangle inside. Proposal: δ along the normal
  and laterally chosen to maximize the volume under that bound (the slab and lateral fractions of `MeshEnvelope`,
  0.8 and 0.6, rather than 1/√3 each: the envelope then accepts more), with the corner faces as in the paper.
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

## 9. To confirm in the paper's full text

Not reachable from this session (network policy: par.nsf.gov and wangbolun300.github.io are not allowed). To check before PR 2: the polyhedron construction and its
proof of the ε bound; the argument that the critical points (LPI on edges, TPI on face lines) suffice for
triangle containment, including touching and coplanar cases; how degenerate triangles are handled; the reported
timings against sampling methods.
