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
- Casts of NaN or infinity to `int` / `long` (undefined result).
- Every new error bound in `math/Exact*`, `AdaptivePredicates.cs`, implicit points: derived and checked in
  the review.

## 6. Integer overflow, sizes and hard limits (#68, #75, #81, #104)

- `int` products of grid dimensions or indices (`nx * ny * nz`, `i * n + j`), cell counts over large
  extents; is there a limit, and is it reported?
- Fixed caps (eg 500 components, max iterations): what happens beyond them?

## 7. Stale state and reuse (#66, #76, #102, #104, #107)

- Is every cache, counter, status and option derived value reset at the start of `Compute()`?
- Is a cached value keyed on everything it depends on (mesh timestamp, options)?
- Does an exception leave the instance in a state that a second call would misread?
- Tests: compute A, then B on the same instance, and compare with a fresh instance (eg
  `InvarianceTests.ExactBoolean_ReusedInstance_EqualsFreshInstance`).

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
- Tests: repeated runs give bit-identical output (`Invariance.Difference`); parallel equals serial where there
  is a switch.

## 10. Complexity (#63, #72, #105)

- Nested loops over all elements of the input (pairs of segments, components, charts): what is the size in
  a realistic large input? Use a grid or tree.
- Tests: time or candidate counts at n and 4n.

## 11. Compatibility and documentation (#92, #94)

- Public API unchanged against 1.0.324 (pack runs the package validation; an accepted break goes into
  `CompatibilitySuppressions.xml` with a reason).
- A changed result of an existing method goes into [UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md).
- Limits of a new operation are documented at the class and in [OPERATIONS.md](OPERATIONS.md).
