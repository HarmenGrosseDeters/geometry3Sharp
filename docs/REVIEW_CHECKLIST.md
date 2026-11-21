# Review checklist

Use this list for the review that CLAUDE.md requires before every merge. It comes from the bugs that the
retroactive reviews (packages A-F, PRs #60-#112) and the benchmarks found; each heading lists the PRs that
fixed a bug of that class. See [QA.md](QA.md) for the strategy behind the list.

## How to use (diff triggers)

Every class has a **Trigger** line that can be read from the diff (file paths, identifiers, patterns). Do not
read all classes against every diff:

1. Read the diff (`git diff base...head`). List which triggers match. `python3 tools/review_triggers.py base...head`
   does this and prints the matching classes (it reads the machine-readable `<!-- trigger ... -->` line under each
   heading, so this file is the only source; a trigger is a hint from the diff text, so also add a class whose
   trigger you can see applies although the tool missed it).
2. Check those classes, and for each yes, check that a test covers it. A test must fail on the old code (mutation
   check). Class 0 ("always") applies to every diff.
3. Classes without a trigger match are skipped. The review names which classes it checked (and which it skipped)
   in its report, so that a missing class can be seen. A small diff matches 3 to 8 classes; a large PR (a new operation) can match most of them, and then the review is the full list.
4. A fault class found in review that has no class here is added in the same PR, with its trigger
   ([AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) section 4). A bug that a trigger missed is a bug of the trigger: widen it.

Trigger syntax of the machine line: `path=` globs (shell style, `*` also crosses `/`) matched against the changed
file paths, `add=` / `del=` regular expressions matched against the added / removed lines of code files in the diff (comments are not matched), `ladd=` a regular expression matched against added lines outside `tests/` and `benchmarks/`, `always` for a
class that applies to every diff. A class matches when any of its parts matches. Inside a broad class, a bullet that starts with `*(when: ...)*`
needs checking only when that condition holds for the diff; a bullet without it always applies once its class matches.

## 0. Always (every diff)

**Trigger:** every diff.
<!-- trigger: always -->

- Do the new or changed tests fail on the old code (mutation check; CI `New tests on base`, required for the
  `bugfix` label)? A change without tests needs a stated reason.
- Do the tests of a success path check the result (closed, manifold, finite, eg `MeshRobustRepair.NotCleanReason`),
  not only the status?
- Does a test take at most 5 s (`Slow` trait and a reason up to 10 s, see CLAUDE.md)? No mesh files committed,
  test meshes are procedural.
- Do the docs a changed behaviour touches (class comment, [OPERATIONS.md](OPERATIONS.md), roadmap item) say what the
  code now does? (More in class 11 when it triggers.)

## 1. Silent success (#60, #61, #75, #79, #84, #108, #109, #110, #111, #112)

**Trigger:** a `Success` / `true` / `Converged` / status or reason value, a failure status, a certificate, an oracle or a proof tool (Z3) is added or changed; a new success path of an operation; a change of a repair, cleanup or metrics file or of benchmark acceptance criteria.
<!-- trigger: path=mesh_ops/*Repair* mesh_ops/*Cleanup* mesh_ops/*Metrics* benchmarks/*; add=\b(Success|Converged|NotCleanReason|Failure|Certificate|Z3)\b|\bStatus\b|\bReason\b|\bUNKNOWN\b|\bunsat\b|return true|IsClean|Closed|Manifold|IsValid -->

- Does every `Success`, `true` or `Converged` path check the result it reports? Closed, manifold, no
  self-intersections, no lost components, walk arrived, factorization succeeded, finite output.
- Does a stage that cannot verify its result say so (status, reason), instead of passing the input on?
- Does a hard limit (component count, iterations, steps) end in a failure status, never in "clean"?
- *(when: a new test asserts on a float or a computed value)* Can an assertion in the new tests never fail (eg a float check on values that are already float)?
- *(when: items are skipped, exempted or left unsplit)* Is a set of skipped items (exempt, unsplittable) visible in the output, as a list or a diagnostic, so that a bound
  silently left unmet can be detected (meshing M3 review)?

- *(when: a proof or model-checking tool (Z3))* For proof tools: does a counterexample (SAT), including an optional diagnostic query, fail the overall
  check? UNKNOWN/timeouts must not be counted as proofs. Are assumptions satisfiable, source/model drift
  detected, and model mutations covered so contradictory assumptions cannot pass vacuously? (Z3 pilot)

- *(when: a certificate or check that compares with a reference copy)* A certificate or check against a reference: is the reference the one the documentation names, not a normalized copy
  the algorithm works on (the contouring certificate compared with the oriented copy, in which inside-out parts are
  flipped, and certified a wrong topology for inputs with an inside-out part; #935)?

## 2. Scale, units, far from the origin (#61, #64, #80, #81, #95, #97, #102, #106)

**Trigger:** a tolerance or epsilon constant (`1e-6`, `Epsilon`, `Tolerance`), a bounding-box or size-derived bound, a `float` / `Vector3f` in a path, a new operation (needs the `Similarity.All()` test), or a change of `tests/**/Harness/Invariance.cs`.
<!-- trigger: path=*Invariance* *Similarity*; add=\b1e-\d+|\b0\.0+\d*\b|\bEpsilon\b|Tolerance|\bfloat\b|Vector3f|Similarity\.All|GetBounds|\bBounds\b|AxisAlignedBox -->

- Are tolerances relative to the input (bounding box of the *used* vertices, edge lengths), not absolute
  constants like `1e-6` or `0.001`?
- Does the code survive UTM coordinates (eg x = 632000, y = 5712000) and millimetre vs metre inputs? Any
  `float` or `Vector3f` in the path (AABB boxes, caches, hash keys)?
- Are bounds computed from the triangles, not from all vertices (an unused far vertex blows them up)?
- Tests: run the operation over `Similarity.All()` (`tests/geometry3Sharp.Tests/Harness/Invariance.cs`): UTM,
  1e-3, 1e3, rotation, and 2^-10 / 2^10, where the result must be the same up to the last bits (mostly bit for bit). Compare the results
  with each other, not with absolute bounds.

## 3. Degenerate input (#69, #70, #87, #93, #97, #105, #109)

**Trigger:** a division by an area, length or determinant, a triangle or polygon operation, `Normalized`, an offset or a call into a polygon library (Clipper2), a ring or hole input, or a new public geometry operation.
<!-- trigger: path=parameterization/*; add=Area|Degenerate|Sliver|Collinear|Normalized|Normalize\(|Clipper|Offset|Hole|Bowtie -->

- Zero-area, collinear and coincident-vertex triangles; slivers; T-junctions; charts or components made only
  of such triangles; empty meshes; a single triangle.
- Divisions by areas, lengths or determinants: what happens at 0 and at 1e-300? Are weights bounded?
- No NaN or infinity in the output; no inverted output triangles.

- *(when: a polygon library or offset gets ring or hole input)* Zero-area rings, bowtie rings and overlapping or misplaced holes: does a library underneath read them the way the
  docs promise (a zero-area group made Clipper2 inflate on a negative offset; NonZero filled overlapping holes)?
  (#243)

## 4. Open, non-manifold and bowtie input (#60, #64, #99, #100, #110)

**Trigger:** an operation that takes a mesh (`DMesh3`, `IMesh`, a triangle or edge loop, `MeshBoundaryLoops`), a repair, boolean, hole-fill or parameterization change, or a new mesh-reading entry point.
<!-- trigger: path=parameterization/* mesh_selection/*; add=\bDMesh3\b|\bNonManifold|\bIsBoundary|\bBoundaryLoops|\bInvalidMesh\b|\bBowtie -->

- Open meshes, bowtie vertices (interior and boundary), non-manifold edges, touching sheets, inconsistent
  orientation, several components including tiny and nested ones.
- Does the operation reject what it cannot handle (a failure status, eg `InvalidMesh` in parameterization), or
  handle it? Never silently
  return a broken result.

## 5. Floating-point range: subnormal, overflow (#62, #71, #73, #74, #103, #104, #110)

**Trigger:** a change in `math/Exact*`, `math/AdaptivePredicates.cs`, `mesh_ops/ExactMesh*`, an error bound, `Math.Sqrt` / `ScaleB` / `ToDouble` / `Normalized` / `UnitCross`, a cast to `int` or `long` of a double, a distance or length or squared value, or a tolerance applied to a computed value.
<!-- trigger: path=math/Exact* math/AdaptivePredicates* mesh_ops/ExactMesh* core/ExactCodeAllowList*; add=ErrBound|ErrorBound|\bulp\b|ScaleB|ToDouble|Math\.Sqrt|\bNormalized\b|UnitCross|\(int\)|\(long\)|double\.(NaN|Epsilon|IsFinite|IsNaN)|IsInfinity|\.LengthSquared|\.DistanceSquared|DistanceSquared\(|\.Distance\(|\.Dot\( -->

- *(when: a product, sum or finiteness check over input values)* Products and sums of large values (`1e200 * 1e200`), finiteness checks by summing values (the sum can
  overflow), `ToDouble` of exact values beyond the double range.
- *(when: a filter or error bound in exact code)* Error bounds of filters when partial products are subnormal; `ScaleB` and intermediate values.
- *(when: a component or product is scaled, projected or clipped)* Scaling a finite component or product down to zero: can a lost projection coefficient become significant
  again when multiplied by a large segment length or height? Exercise mixed exponents, not only uniform scales.
- *(when: a clipping interval or an ambiguous comparison)* Rounded clipping intervals: closed crossings must survive opposite-direction rounding of the two bounds;
  ambiguous comparisons need a conservative bound and a verified fallback.
- *(when: a nearest or tie-break comparison of distances)* Distances overflowing to infinity: preserve actual nearest ordering before the stable-id tie break; an
  infinite approximation is not proof that two finite-input geometries are equally far away.
- *(when: a cast of a double to `int` / `long`)* Casts of NaN or infinity to `int` / `long` (undefined result).
- *(when: `Normalized` / `UnitCross` on a vector of any scale)* `Normalized` returns the zero vector below `MathUtil.Epsilon` and when the squared length overflows: a unit vector
  built from a small or huge vector silently vanishes at extreme scales (ContourLoft review: the normalized cost lost its
  separation at 2^-30). Scale by a power of two first.
- *(when: a tolerance applied to a normalized or cross-product result)* A tolerance applied to the result of `UnitCross` / `Normalized` (`Vector3d.Normalize` uses `MathUtil.Epsilon`):
  every cross product longer than 2.2e-16 becomes a unit vector, so a test like `n.Dot(n) < ZeroTolerance` only
  means "exactly zero" and the tolerance is dead code (`IntrTriangle3Triangle3` parallel test, #253 review). Compare
  the unnormalized cross product, or decide exactly.
- *(when: a new error bound in `math/Exact*`, `AdaptivePredicates.cs` or implicit points)* Every new error bound in `math/Exact*`, `AdaptivePredicates.cs`, implicit points: derived and checked in
  the review.
- *(when: a rounding error or a geometric bound is stated or proved)* A rounding error stated as relative where it is absolute: a distance computed from coordinates of size M has an
  error of a few ulps of M, not of the distance (a computed distance 0 for a true 1e-14 at coordinates near 100;
  SafeRounding bounds review). A proof of a geometric bound also covers the edge cases of the edit (an edit that adds
  no triangle, eg contracting an ear) and only claims what it derives (a volume bound from a neighbourhood argument
  misses the Steiner terms at degenerate triangles).

- *(when: values (heights, distances) interpolated along a clipped edge)* Values interpolated along a clipped edge (heights, distances) carry the rounding of the clip parameter times the
  slope: a positivity or area threshold relative to the projected box still admits arbitrarily steep triangles. An
  upper bound built from interpolated values needs a slack for this and a slope limit (patch bound of
  `TriangleDeviationBounds`, wrap-resolution review).

- *(when: an exact comparison with a threshold from an irrational constant)* An exact comparison with a threshold built from an irrational constant in double (eg d = √3 ulp, `Math.Sqrt(3) * ulp`)
  is not exact: the double constant is rounded, so a value exactly at the threshold passes. Square it in exact
  arithmetic (d² = 3 ulp²) and keep the double only where a margin covers its rounding (snap-rounding certificate,
  independent review).

- *(when: a plain double helper (dot, squared distance) next to exact predicates)* Plain double helpers (dot products, squared distances, tolerance squared) underflow at small magnitudes even
  where the predicates are exact: scale differences by a power of two first (meshing M2 review: a walk found no
  wedge at 2^-560).

## 6. Integer overflow, sizes and hard limits (#68, #75, #81, #104)

**Trigger:** a new grid, cell or index product (`nx * ny`, `i * n + j`), a fixed cap or maximum (iterations, components), a count read from input, a hard cap removed or added (`Count > 500`), a hash-grid key range, or a call into another library with scaled values (Clipper2 integer grid).
<!-- trigger: path=addons/* spatial/*Grid* mesh_ops/*Arrangement*; add=\bCount\s*[<>]=?\s*\d{2,}|new (int|long)\[|\bBatch|\bnx\b|\bMax[A-Z]\w*\s*=|const int; del=\bCount\s*[<>]=?\s*\d{2,}|\bMax[A-Z]\w*\s*=|\bny\b|\bnz\b|\bconst\s+int\b|\bint\.MaxValue|\bCount\s*\*|Clipper|\bIntPoint|\bPoint64 -->

- *(when: an `int` product of grid dimensions or indices)* `int` products of grid dimensions or indices (`nx * ny * nz`, `i * n + j`), cell counts over large
  extents; is there a limit, and is it reported?
- *(when: a fixed cap or maximum)* Fixed caps (eg 500 components, max iterations): what happens beyond them?

- *(when: a call into another library with scaled values)* Calls into another library with scaled values (eg Clipper2 on an integer grid): its internal limits (an `int`
  cast of a distance, a fallback when a parameter is out of its range, such as an arc tolerance above the offset)
  and the margin our own values need (offset distance times the miter limit) must fit; test with extreme options
  (#243).

## 7. Stale state and reuse (#66, #76, #102, #104, #107)

**Trigger:** a `Compute()` or a public mutable-state class, a cache, counter or option-derived field, an instance reused across calls, a swap or reversal of inputs, or per-element tags or labels (CDT, constraints, dirty lists).
<!-- trigger: add=\bCompute\(|\bCache|\bcache|\bReset\(|\bClear\(|\bDirty|\bTimestamp|\bswap|\bReverse -->

- Is every cache, counter, status and option derived value reset at the start of `Compute()`?
- Is a cached value keyed on everything it depends on (mesh timestamp, options)?
- Does an exception leave the instance in a state that a second call would misread?
- Tests: compute A, then B on the same instance, and compare with a fresh instance (eg
  `InvarianceTests.ExactBoolean_ReusedInstance_EqualsFreshInstance`).
- *(when: a tag or label survives a lifted invariant (a constraint removed and re-inserted))* Is per-element state (a tag, a label) kept right while an invariant is lifted for a moment, eg a constraint removed
  and re-inserted around a split? Test with an enclosed hole or region of a single triangle next to the operation
  (meshing M3 review: the flips around a lifted constraint filled a hole).
- *(when: a dirty list or changed-since set)* Does state that is updated only for "touched" elements (a dirty list, a changed-since set) see every change? A change of
  meaning without a change of topology escapes it, eg a chain rerouted through a vertex already there: no triangle is
  rewritten, yet the triangles between the old and the new chain change sides (meshing: a grid split onto an existing
  vertex left stale domain tags). Test the path where the operation adds nothing new, with the full check from scratch
  after every step (`CheckTagsForTests`).

- *(when: inputs are swapped or reversed internally)* An operation that swaps or reverses its inputs internally (the shorter contour as the start contour, a reversed winding):
  is every reported index, id and orientation mapped back to the caller's roles? Test each output field with the roles
  swapped (ContourLoft review: the start offset was an index into the other contour when the first one was shorter).

## 8. Untrusted input (#86, #88, #90, #91)

**Trigger:** a reader, writer or file format, the CLI (`tools/g3cli`) or MCP server, a path or URI from input, a size or count read from a file or network, `Parse` / `ReadAll` / a deserializer, or a change of `ReaderFuzzTests`.
<!-- trigger: path=io/* tools/g3cli/* tests/g3cli.Tests/* *ReaderFuzz*; add=\bStream\b|\bFile\.(Open|Read)|\bdouble\.Parse|\bint\.Parse|\bTryParse|\bXmlReader|\bBinaryReader|\bZipArchive|\bUri\b|ReadAll|Deserializ|\bRead[A-Z]\w*\( -->

- Readers and the CLI / MCP server: path traversal (`../` in URIs), sizes from the file (allocation before
  validation), recursion and DAGs (exponential expansion), zip bombs, DTDs, locale (`1,5`), `1e400`, NaN.
- Every size from the input is checked against a limit before allocating.

## 9. Ordering, determinism and exactness (#67, #77; and the findings of the determinism tests, see QA.md)

**Trigger:** a `Parallel.For` / `gParallel` / `Task` / thread or lock, an entry in `core/ExactCodeAllowList.cs`, a sort or `Array.Sort`, a tie break, a hash-set / dictionary iteration that feeds output, an angle (`atan2`, `Math.Atan2`, a wrap at 2 pi, signposts), a bulk builder of a data structure, or an incremental step that relies on an invariant (CDT, Delaunay).
<!-- trigger: path=core/ExactCodeAllowList* core/gParallel* *Determinism*; add=Parallel\.|gParallel|\bTask\.|\bThread\b|\block\s*\(|Interlocked|ConcurrentBag|Concurrent\w+|Array\.Sort|\.Sort\(|\bOrderBy|Atan2|\bwrap\(|Math\.PI|TwoPI|\bHashSet|\bDictionary|FromEdgeStructure|ForBlocks -->

- Is an exact decision made with an inexact tool (`atan2`, angle sums, float sort keys)?
- Angle wraps at 2 pi, boundary fans with angle sums above 2 pi.
- *(when: a parallel loop)* Parallel loops: no shared mutable state (`BitArray`, `List.Add`, caches) without a lock; results must not
  depend on the thread schedule or on hash-set iteration order. Ties (equal distances, equal costs) are broken
  by id, not by which thread came first; a value cached in lower precision is returned as cached, also the
  first time.
- *(when: a new entry in `core/ExactCodeAllowList.cs`)* A new entry in `core/ExactCodeAllowList.cs` (a parallel loop in exact code): the loop aggregates no floating-point
  value across threads, and every write goes to its own index or a thread-local.
- Tests: repeated runs give bit-identical output (`Invariance.Difference`); parallel equals serial where there
  is a switch.
- *(when: an output depends on order (ids, neighbour arrays, sorts))* Order-dependent outputs (element ids, neighbour arrays, "smallest id" answers) need their own 2^k test, at
  extreme exponents too: a helper in doubles (`c / width` for a sort key) can overflow there although the
  predicates stay exact. `Array.Sort` is not stable, and runtimes may break ties differently: break them by
  index (meshing M1 review).

- *(when: a caller of a parallel helper (`ForBlocks`) in exact code)* A parallel loop behind a helper (`ExactMeshEdgeGroups.ForBlocks`): the analyzer flags only the helper's own call of `gParallel`, so
  its callers escape the allow-list. A new caller in exact code needs the same review (no floating-point reduction across tasks, one
  writer per slot) and its own line in the header of the allow-list entry.
- *(when: blocks over a sorted range whose groups can straddle a border)* Blocks over a sorted range whose groups can straddle a block border (a group belongs to the block it begins in): tests run the
  operation with block sizes of 2-16 entries, and compare the counters the loop sums (ConflictEdges, NonManifoldEdges), not only the
  ids; a group seen twice or in two halves is invisible where the join is idempotent. Test inputs with groups of 3 and 4 (touching solids).
- *(when: a bulk builder of a data structure)* A bulk builder of a data structure that appends otherwise (`DMesh3.FromEdgeStructure`): compare every observable, also the reference
  counts, time stamps, normals and the internal state of its containers (a resized DVector at a multiple of its block size), the
  rejection checks with crafted inputs, a counter that proves the fast path was taken, and edits applied to both afterwards.

- *(when: an incremental step that assumes an invariant (CDT, Delaunay))* An incremental step that assumes an invariant (CDT, Delaunay) right after an operation that breaks it, such as
  removing a constraint before inserting a point: restore the invariant first, and verify it at the end (meshing M2 review).

## 10. Complexity (#63, #72, #105)

**Trigger:** a nested loop over elements of the input, a spatial index / grid / tree / hash build, a pair or candidate search, a fallback path, a benchmark or performance claim, or a change under `benchmarks/` or `spatial/`.
<!-- trigger: path=benchmarks/* spatial/* queries/* *PairSearch* *AabbTree* mesh_ops/*Arrangement*; add=AABB|AabbTree|SpatialHash|\bGrid\b|\bBenchmark|\bFallback|PointHash|BVH -->

- *(when: a nested loop over input elements)* Nested loops over all elements of the input (pairs of segments, components, charts): what is the size in
  a realistic large input? Use a grid or tree.
- Tests: time or candidate counts at n and 4n, on clustered, outlier and multi-scale inputs too: a fixed-
  resolution grid or space-filling-curve key over the bounding box collapses when one point is far away
  (meshing M1 review: a quadratic build from one outlier).
- *(when: a benchmark reports allocations)* Benchmark allocation columns identify the measured scope: current-thread counters do not include parallel
  workers, and retained heap deltas are neither total allocations nor peak memory. Compare equivalent preprocessing.
- *(when: an index built on a type that stores a derived form (`Segment3d`))* A type that stores a derived form rounds its input: `Segment3d` keeps a center, a direction and an extent, so `P0` is not
  what went in. An index built on it missed its own vertices. Test with non-dyadic inputs through the public constructor.
- *(when: a fallback path next to a fast path)* A correct fallback can hide a broken fast path: a search that falls back to the full region when its bounded region is
  empty gives right answers with the wrong complexity, so mutants of the region rule survive. Count the fallbacks and the work
  (including resets and clears, not only visited nodes) and bound them in a test (ContourLoft: a per-row reset of the whole
  state array made the search O(m n^2)).
- *(when: a spatial index or tree)* Correct results can hide linear cost: a spatial index that keeps all items at its root answers every query right. Count
  the visited items on thin strips, far outliers and sorted inserts, rather than timing them.
- *(when: an A/B measurement after a source rollback)* Source rollback can leave incremental-build binaries stale. Force a rebuild and verify binary identities before
  freezing an A/B candidate; do not label measurements by source changes alone.
- *(when: an indexed existence query beside a scan)* An indexed existence query can defeat a scan's early exit by materializing every candidate first. Test dense/unknown
  boxes and clusters beside outliers; bound extra enumeration and keep an exact scan fallback.
- *(when: a reproduction command or campaign script)* Reproduction commands must fail on unequal required reports/fingerprints. Saving hashes or relying on a one-off
  manual comparison is not an acceptance gate; verify completed records again after resuming a campaign.
- *(when: a pair enumeration callback)* Pair enumeration callbacks may emit either index order. Normalize unordered pairs before deduplicating;
  a `j <= i` guard is safe only when the caller guarantees the selected query is the lower index. Compare the
  complete pair set with exhaustive enumeration, including nonempty hits (oracle edge-index review).

## 10a. The oracle itself

**Trigger:** a test oracle, certificate or acceptance check is added or relaxed (`*Oracle*`, an exemption window or tolerance in a check), a bound that depends on orientation / winding, a statistical rule (confidence interval, stop rule), or a sample region built over bounds.
<!-- trigger: path=*Oracle* *Certificate*; add=Oracle|Certificate|Exempt|\bwinding|Winding|Bonferroni|confidence|\bz\s*= -->

- *(when: an oracle check is relaxed)* Every relaxation of an oracle check comes with a known-wrong result that must still fail (meshing M2 review: a
  loosened chain end accepted a mesh without a shared parcel border).
- *(when: an exemption window or tolerance in an oracle)* An exemption window (a distance ratio, a tolerance) is as narrow as the rule it implements, with a known-wrong result
  just outside it (meshing M3 review: a factor-2 window accepted improvable triangles).

- *(when: a bound that holds for one orientation of its input)* A bound that is valid, or only finite, for one orientation of its input: do the callers keep that orientation through
  their subdivisions, and do the tests run both windings?
- *(when: a topological certificate (coverage, winding count))* A topological certificate (coverage by the boundary edges of a patch, a winding count) has one mutation test per
  clause: the empty-overlap guard, the orientation clause, the positivity filter, the margin. Each clause is a separate
  way to accept wrongly (wrap-resolution review).

- *(when: a statistical rule (interval, stop rule))* A statistical rule (a confidence interval, a stop rule, a Bonferroni split) is barely visible to tests that sample:
  its mutants change only rare failure probabilities. Extract the interval as a function and test it deterministically
  against a brute-force scan (volume check cells review: the corners of the ratio interval, the split z).
- *(when: a sample region built over the bounds of all parts)* A sample region built over the bounds of all parts (a grid, an octree with a depth or cell budget) is compared with the
  per-part region it replaces: one small part far away makes its cells huge (volume check cells review).

## 11. Compatibility and documentation (#92, #94)

**Trigger:** a public type or member is added, removed or changed (`public` in a library file), `CompatibilitySuppressions.xml`, the result of an existing method changes, a new operation, a roadmap item changes status, or test code that runs on net48.
<!-- trigger: path=CompatibilitySuppressions.xml docs/ROADMAP.md docs/roadmap/* docs/UPSTREAM_CHANGES.md docs/OPERATIONS.md docs/guides/* docs/sources/*; add=\.IsFinite|\bIsFinite\(; ladd=^\s*public\s|^\s*protected\s -->

- Public API unchanged against 1.0.324 (pack runs the package validation; an accepted break goes into
  `CompatibilitySuppressions.xml` with a reason).
- A changed result of an existing method goes into [UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md).
- Limits of a new operation are documented at the class, in the matching [docs/guides/](guides/getting-started.md) page, and linked from [OPERATIONS.md](OPERATIONS.md); citations go into the matching [docs/sources/](sources/README.md) catalog; new design docs are listed in [docs/design/README.md](design/README.md); run `python3 tools/check_docs.py`.
- *(when: a roadmap item starts, finishes or changes)* A PR that starts, finishes or changes a roadmap item updates it ([ROADMAP.md](ROADMAP.md) "Now" and the `docs/roadmap/` or
  `docs/design/` page of the item: status, date, PR numbers), see [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) section 6.

- *(when: test code)* Test code builds for net48 too (`-f net48 -p:TestNet48=true`): no `double.IsFinite` and other APIs of newer runtimes (use the test helpers).

## 12. Test oracles and generators (#229)

**Trigger:** a test harness, fuzzer, generator, shrinker or replay command, a tool result validator or coverage gate (`tools/*`, `tests/**/Harness/*`, `*Fuzz*`, `*Generator*`), or an "exact" comparison in a test.
<!-- trigger: path=tools/* tests/*/Harness/* *Fuzz* *Generator* tests/mutation/*; add=\ballowDegenerate\b|\bFuzz|\bGenerator|\bShrink|\bReplay -->

- *(when: a tool-result validator)* Tool-result validation: parse decimal bounds without binary floating-point underflow masking a negative value;
  reject malformed, non-finite or missing results.

- *(when: a fuzz harness or a reader wrapper)* Fuzz harnesses: does seed validation propagate parse errors, and does a reader wrap internal exceptions in
  error results that the harness must surface? Verify a stored artificial crash can be replayed without instrumentation.

- *(when: a generator)* Does each generator produce only inputs of the class it claims (eg "clean slivers"), and is that asserted,
  not let through by a relaxed check (`allowDegenerate`, a skipped probe)?
- *(when: a self-test of a harness (shrinking, replay))* A self-test of a test harness (shrinking, replay): does it read the value it means to check (the shrunk seed,
  not the first seed in the message), and can its assertions fail?
- *(when: a documented replay or filter command)* Do documented replay or filter commands actually select a test? Run them once.
- *(when: a coverage gate)* Coverage gates must validate the same line-record level they evaluate: method-only records cannot count as
  complete evidence when the gate reads class-level lines. Missing records must fail closed.
- *(when: an "exact" comparison in a test)* Is an "exact" comparison exact for every generated input (a sum of products in double is not, once coordinates
  have many bits)?
- *(when: a fuzzer of an operation with several input classes)* Do the fuzzers cover every input class the class doc advertises (meshing M3 review: no holes, no regions with an area
  bound and no locked segments in the refinement fuzz, so a filled hole and wrong labels went unnoticed)?
