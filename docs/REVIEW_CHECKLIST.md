# Review checklist

Use this list for the review that CLAUDE.md requires before every merge. It comes from the bugs that the
retroactive reviews (packages A-F, PRs #60-#112) and the benchmarks found; each heading lists the PRs that
fixed a bug of that class. Go through the classes that apply to the diff,
and for each yes, check that a test covers it. A test must fail on the old code (mutation check). See
[QA.md](QA.md) for the strategy behind the list.

## 1. Silent success (#60, #61, #75, #79, #84, #108, #109, #110, #111, #112)

- Does every `Success`, `true` or `Converged` path check the result it reports? Closed, manifold, no
  self-intersections, no lost components, walk arrived, factorization succeeded, finite output.
- Does a stage that cannot verify its result say so (status, reason), instead of passing the input on?
- Does a hard limit (component count, iterations, steps) end in a failure status, never in "clean"?
- Can an assertion in the new tests never fail (eg a float check on values that are already float)?
- Is a set of skipped items (exempt, unsplittable) visible in the output, as a list or a diagnostic, so that a bound
  silently left unmet can be detected (meshing M3 review)?

## 2. Scale, units, far from the origin (#61, #64, #80, #81, #95, #97, #102, #106)

- Are tolerances relative to the input (bounding box of the *used* vertices, edge lengths), not absolute
  constants like `1e-6` or `0.001`?
- Does the code survive UTM coordinates (eg x = 632000, y = 5712000) and millimetre vs metre inputs? Any
  `float` or `Vector3f` in the path (AABB boxes, caches, hash keys)?
- Are bounds computed from the triangles, not from all vertices (an unused far vertex blows them up)?
- Tests: run the operation over `Similarity.All()` (`tests/geometry3Sharp.Tests/Harness/Invariance.cs`): UTM,
  1e-3, 1e3, rotation, and 2^-10 / 2^10, where the result must be the same up to the last bits (mostly bit for bit). Compare the results
  with each other, not with absolute bounds.

## 3. Degenerate input (#69, #70, #87, #93, #97, #105, #109)

- Zero-area, collinear and coincident-vertex triangles; slivers; T-junctions; charts or components made only
  of such triangles; empty meshes; a single triangle.
- Divisions by areas, lengths or determinants: what happens at 0 and at 1e-300? Are weights bounded?
- No NaN or infinity in the output; no inverted output triangles.

- Zero-area rings, bowtie rings and overlapping or misplaced holes: does a library underneath read them the way the
  docs promise (a zero-area group made Clipper2 inflate on a negative offset; NonZero filled overlapping holes)?
  (#243)

## 4. Open, non-manifold and bowtie input (#60, #64, #99, #100, #110)

- Open meshes, bowtie vertices (interior and boundary), non-manifold edges, touching sheets, inconsistent
  orientation, several components including tiny and nested ones.
- Does the operation reject what it cannot handle (a failure status, eg `InvalidMesh` in parameterization), or
  handle it? Never silently
  return a broken result.

## 5. Floating-point range: subnormal, overflow (#62, #71, #73, #74, #103, #104, #110)

- Products and sums of large values (`1e200 * 1e200`), finiteness checks by summing values (the sum can
  overflow), `ToDouble` of exact values beyond the double range.
- Error bounds of filters when partial products are subnormal; `ScaleB` and intermediate values.
- Scaling a finite component or product down to zero: can a lost projection coefficient become significant
  again when multiplied by a large segment length or height? Exercise mixed exponents, not only uniform scales.
- Rounded clipping intervals: closed crossings must survive opposite-direction rounding of the two bounds;
  ambiguous comparisons need a conservative bound and a verified fallback.
- Distances overflowing to infinity: preserve actual nearest ordering before the stable-id tie break; an
  infinite approximation is not proof that two finite-input geometries are equally far away.
- Casts of NaN or infinity to `int` / `long` (undefined result).
- A tolerance applied to the result of `UnitCross` / `Normalized` (`Vector3d.Normalize` uses `MathUtil.Epsilon`):
  every cross product longer than 2.2e-16 becomes a unit vector, so a test like `n.Dot(n) < ZeroTolerance` only
  means "exactly zero" and the tolerance is dead code (`IntrTriangle3Triangle3` parallel test, #253 review). Compare
  the unnormalized cross product, or decide exactly.
- Every new error bound in `math/Exact*`, `AdaptivePredicates.cs`, implicit points: derived and checked in
  the review.
- A rounding error stated as relative where it is absolute: a distance computed from coordinates of size M has an
  error of a few ulps of M, not of the distance (a computed distance 0 for a true 1e-14 at coordinates near 100;
  SafeRounding bounds review). A proof of a geometric bound also covers the edge cases of the edit (an edit that adds
  no triangle, eg contracting an ear) and only claims what it derives (a volume bound from a neighbourhood argument
  misses the Steiner terms at degenerate triangles).

- Values interpolated along a clipped edge (heights, distances) carry the rounding of the clip parameter times the
  slope: a positivity or area threshold relative to the projected box still admits arbitrarily steep triangles. An
  upper bound built from interpolated values needs a slack for this and a slope limit (patch bound of
  `TriangleDeviationBounds`, wrap-resolution review).

- An exact comparison with a threshold built from an irrational constant in double (eg d = √3 ulp, `Math.Sqrt(3) * ulp`)
  is not exact: the double constant is rounded, so a value exactly at the threshold passes. Square it in exact
  arithmetic (d² = 3 ulp²) and keep the double only where a margin covers its rounding (snap-rounding certificate,
  independent review).

- Plain double helpers (dot products, squared distances, tolerance squared) underflow at small magnitudes even
  where the predicates are exact: scale differences by a power of two first (meshing M2 review: a walk found no
  wedge at 2^-560).

## 6. Integer overflow, sizes and hard limits (#68, #75, #81, #104)

- `int` products of grid dimensions or indices (`nx * ny * nz`, `i * n + j`), cell counts over large
  extents; is there a limit, and is it reported?
- Fixed caps (eg 500 components, max iterations): what happens beyond them?

- Calls into another library with scaled values (eg Clipper2 on an integer grid): its internal limits (an `int`
  cast of a distance, a fallback when a parameter is out of its range, such as an arc tolerance above the offset)
  and the margin our own values need (offset distance times the miter limit) must fit; test with extreme options
  (#243).

## 7. Stale state and reuse (#66, #76, #102, #104, #107)

- Is every cache, counter, status and option derived value reset at the start of `Compute()`?
- Is a cached value keyed on everything it depends on (mesh timestamp, options)?
- Does an exception leave the instance in a state that a second call would misread?
- Tests: compute A, then B on the same instance, and compare with a fresh instance (eg
  `InvarianceTests.ExactBoolean_ReusedInstance_EqualsFreshInstance`).
- Is per-element state (a tag, a label) kept right while an invariant is lifted for a moment, eg a constraint removed
  and re-inserted around a split? Test with an enclosed hole or region of a single triangle next to the operation
  (meshing M3 review: the flips around a lifted constraint filled a hole).

## 8. Untrusted input (#86, #88, #90, #91)

- Readers and the CLI / MCP server: path traversal (`../` in URIs), sizes from the file (allocation before
  validation), recursion and DAGs (exponential expansion), zip bombs, DTDs, locale (`1,5`), `1e400`, NaN.
- Every size from the input is checked against a limit before allocating.

## 9. Ordering, determinism and exactness (#67, #77; and the findings of the determinism tests, see QA.md)

- Is an exact decision made with an inexact tool (`atan2`, angle sums, float sort keys)?
- Angle wraps at 2 pi, boundary fans with angle sums above 2 pi.
- Parallel loops: no shared mutable state (`BitArray`, `List.Add`, caches) without a lock; results must not
  depend on the thread schedule or on hash-set iteration order. Ties (equal distances, equal costs) are broken
  by id, not by which thread came first; a value cached in lower precision is returned as cached, also the
  first time.
- A new entry in `core/ExactCodeAllowList.cs` (a parallel loop in exact code): the loop aggregates no floating-point
  value across threads, and every write goes to its own index or a thread-local.
- Tests: repeated runs give bit-identical output (`Invariance.Difference`); parallel equals serial where there
  is a switch.
- Order-dependent outputs (element ids, neighbour arrays, "smallest id" answers) need their own 2^k test, at
  extreme exponents too: a helper in doubles (`c / width` for a sort key) can overflow there although the
  predicates stay exact. `Array.Sort` is not stable, and runtimes may break ties differently: break them by
  index (meshing M1 review).

- An incremental step that assumes an invariant (CDT, Delaunay) right after an operation that breaks it, such as
  removing a constraint before inserting a point: restore the invariant first, and verify it at the end (meshing M2 review).

## 10. Complexity (#63, #72, #105)

- Nested loops over all elements of the input (pairs of segments, components, charts): what is the size in
  a realistic large input? Use a grid or tree.
- Tests: time or candidate counts at n and 4n, on clustered, outlier and multi-scale inputs too: a fixed-
  resolution grid or space-filling-curve key over the bounding box collapses when one point is far away
  (meshing M1 review: a quadratic build from one outlier).
- Benchmark allocation columns identify the measured scope: current-thread counters do not include parallel
  workers, and retained heap deltas are neither total allocations nor peak memory. Compare equivalent preprocessing.
- A type that stores a derived form rounds its input: `Segment3d` keeps a center, a direction and an extent, so `P0` is not
  what went in. An index built on it missed its own vertices. Test with non-dyadic inputs through the public constructor.
- Correct results can hide linear cost: a spatial index that keeps all items at its root answers every query right. Count
  the visited items on thin strips, far outliers and sorted inserts, rather than timing them.
- Source rollback can leave incremental-build binaries stale. Force a rebuild and verify binary identities before
  freezing an A/B candidate; do not label measurements by source changes alone.

## 10a. The oracle itself

- Every relaxation of an oracle check comes with a known-wrong result that must still fail (meshing M2 review: a
  loosened chain end accepted a mesh without a shared parcel border).
- An exemption window (a distance ratio, a tolerance) is as narrow as the rule it implements, with a known-wrong result
  just outside it (meshing M3 review: a factor-2 window accepted improvable triangles).

- A bound that is valid, or only finite, for one orientation of its input: do the callers keep that orientation through
  their subdivisions, and do the tests run both windings?
- A topological certificate (coverage by the boundary edges of a patch, a winding count) has one mutation test per
  clause: the empty-overlap guard, the orientation clause, the positivity filter, the margin. Each clause is a separate
  way to accept wrongly (wrap-resolution review).

## 11. Compatibility and documentation (#92, #94)

- Public API unchanged against 1.0.324 (pack runs the package validation; an accepted break goes into
  `CompatibilitySuppressions.xml` with a reason).
- A changed result of an existing method goes into [UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md).
- Limits of a new operation are documented at the class and in [OPERATIONS.md](OPERATIONS.md).

## 12. Test oracles and generators (#229)

- Does each generator produce only inputs of the class it claims (eg "clean slivers"), and is that asserted,
  not let through by a relaxed check (`allowDegenerate`, a skipped probe)?
- A self-test of a test harness (shrinking, replay): does it read the value it means to check (the shrunk seed,
  not the first seed in the message), and can its assertions fail?
- Do documented replay or filter commands actually select a test? Run them once.
- Is an "exact" comparison exact for every generated input (a sum of products in double is not, once coordinates
  have many bits)?
- Do the fuzzers cover every input class the class doc advertises (meshing M3 review: no holes, no regions with an area
  bound and no locked segments in the refinement fuzz, so a filled hole and wrong labels went unnoticed)?
