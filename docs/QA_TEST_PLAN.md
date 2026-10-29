# Differential and core data-structure tests: implementation plan

Status: tests and D1 implemented on `qa/differential-core-tests`, based on `origin/master` at
`93880ed`, in the separate `D:\work\geometry3Sharp-QA` worktree. Focused net10/net48 validation and
fault-sensitivity checks have completed; acceptance is blocked by reproducible production defects.
No production code was changed, and no commit, push or PR was created. See the execution report below.
This document is the task brief for the requested roadmap items
D(e), D(f), E.1 and E.3. The current [roadmap](ROADMAP.md) uses named phases rather than these labels;
the labels below preserve the request's traceability, not an assertion about the current headings.
The related QA work is [differential testing](QA.md#24-differential-tests-against-exact-results),
[mesh-input fuzzing](QA.md#25-fuzzing-of-the-readers-and-of-mesh-inputs) and
[the invariance harness (QA section 2.1)](QA.md).

The reported zero coverage of `GetCoplanarIntersection` remains an investigation lead, not a reproduced
measurement. Tests were executed as recorded below; no coverage or performance measurements were
collected. Case counts in the briefs are execution budgets; only the execution report gives measured results.

## Delivery and boundaries

| Brief | Requested item | Deliverable | Tier | Dependency |
|---|---|---|---|---|
| T1 | D(e) | AABB-tree queries compared with a linear scan | M | None |
| T2 | D(f) | Coplanar triangle intersection compared with an exact oracle | M, with escalation below | None |
| T3 | E.1 | Seeded stateful `DMesh3` mutation tests | M | None |
| T4 | E.1 | `gSerialization` round-trip tests | M | None; may reuse T3 fixtures later |
| T5 | E.1 | Self-touching boundary-loop tests | M | None |
| D1 | E.3 | README section “Additions in this fork” | S | None |

One PR-sized topic per row; no merge as part of a worker's brief. Suggested execution order:
T1, T2, T3, T4, T5, D1. D1 can be done independently first. T3 is not a prerequisite for T4:
serialization tests must have their own small procedural fixtures, not depend on a random sequence
happening to construct the desired mesh.

The plan itself is a tier-S documentation change. Test implementations use the tiers listed above.
Follow [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md), [CLAUDE.md](../CLAUDE.md), and the applicable parts of
[REVIEW_CHECKLIST.md](REVIEW_CHECKLIST.md).

### Shared scope and non-goals

- Test topics change only their named test files and directly related QA documentation. Prefer local
  helpers; extract a harness helper only when another test actually needs it.
- No production fixes, public API changes, new package dependencies, binary fixtures, committed mesh
  files, changes to the serialization format, or changes to mutation infrastructure.
- Existing exact predicates may be consumed as a black-box oracle. Do not change `math/Exact*`,
  `math/AdaptivePredicates.cs`, error bounds, implicit points, or `mesh_ops/ExactMesh*` in these briefs.
  If a result needs such a change, stop and create a separate tier-L brief with owner-approved design,
  independent acceptance tests and a bounds review. Do not build a new general-purpose exact kernel
  inside the test project to evade that boundary.
- Use the repository's existing tests, public contracts and mathematical definitions as sources.
  No Triangle / Triangle.NET code, the owner's quadtree, company code, or GPL sources. If an outside
  paper or permitted implementation becomes necessary, record it in [SOURCES.md](SOURCES.md).
- Preserve unrelated working-tree edits. At planning time these include the repair benchmark, repair
  tests and the test project file; none is part of these briefs.

### Reproducibility and failure handling

1. Use fixed theory seeds and bounded input sizes. Sort live IDs before random selection; never derive
   a seed from time, thread scheduling, hash iteration or an unordered container.
2. A failure prints its seed, fixture, operation/query index, parameters and concrete coordinates or
   full operation log. Seed alone is not a portable reproducer across runtimes. Use round-trip numeric
   formatting with invariant culture.
3. Reduce a failure to the shortest failing prefix, then remove unnecessary commands/geometry while
   preserving each operation's preconditions. Commit a small procedural regression, not a mesh file.
4. Classify the mismatch as a test/oracle mistake, an unsupported/unspecified input contract, or a
   production defect. Do not increase tolerances, catch every exception, add `Skip`, or silently drop
   inconvenient seeds to make the suite green.
5. For a production defect, preserve the failing regression and command output on the topic branch and
   request a separate fix brief. The affected case is blocked, not completed. Do not merge a red suite;
   unaffected test work can be split out with the blocked scope explicitly tracked.
6. A correct baseline is allowed to pass all new tests: pure assurance tests do not require an invented
   pre-existing bug. Demonstrate sensitivity with a deliberate temporary fault in an isolated checkout;
   the relevant test must fail, and must pass again after restoring the fault. Exact-code mutations
   are outside these briefs; inject faults in the non-exact operation or comparison boundary instead.

### Budgets and common acceptance

- Initial CI budget: eight fixed seeds per randomized family; each brief sets its bounded case/step
  count. Named edge cases run regardless of random generation. Increase budgets only with recorded
  runtime and a concrete missing case, not as an unmeasured quality claim.
- Every operation family has a deterministic fixture exercising it successfully. Coverage of an
  operation cannot be inferred from an attempted random call that was always rejected.
- Run representative cases over `Similarity.All()`. Compare discrete results exactly and map geometry
  back before comparison. Powers-of-two scaling uses bitwise equality for applicable floating outputs;
  other similarities use the harness's coordinate-resolution-based tolerance. Do not impose bitwise
  equality on quantities not preserved by the operation's representation or contract.
- Reuse means A, B, A on the same object against fresh objects where the API permits it; repeating only
  A can miss stale state. Parallel-versus-serial tests apply only where a parallel path exists. Repeated
  runs and input permutations remain useful without a parallel switch.
- Validate returned geometry/topology, not only status. Closedness is asserted only for closed fixtures;
  an intentionally open or bowtie fixture must not be rejected by a blanket “closed manifold” check.
- Stop after the named matrix, sensitivity checks and validation have been completed. After a mismatch
  remains unresolved following a reproducer and contract/oracle check, hand it over rather than expanding
  into algorithm work. Reports contain the commands, outputs, affected cases and remaining blockers.

## T1 — AABB-tree differential tests

**Goal and scope.** Add `tests/geometry3Sharp.Tests/AABBTreeDifferentialTests.cs` for the actual class
[`DMeshAABBTree3`](../spatial/DMeshAABBTree.cs). No tree implementation changes. Existing guards in
[ReviewFixesTests](../tests/geometry3Sharp.Tests/ReviewFixesTests.cs),
[SpatialFixesTests](../tests/geometry3Sharp.Tests/SpatialFixesTests.cs) and
[QueryThresholdScaleTests](../tests/geometry3Sharp.Tests/QueryThresholdScaleTests.cs) remain in place.
The deliverable is a systematic differential query matrix, not another copy of those regressions.

**Interface and independent reference.** Enumerate live triangle IDs without consulting tree bounds,
traversal or candidate lists. Use `MeshQueries.TriDistanceSqr` for point distance and
`IntrRay3Triangle3.Intersects` for ray/triangle leaves. Sharing the primitive calculation deliberately
isolates tree construction, pruning and filtering; it is not an independent proof of that primitive.
Use [MeshQueries](../queries/MeshQueries.cs)'s existing linear helpers when their contracts match; use
an explicit scan to implement maximum-distance and filter cases and to collect all equally optimal IDs.

| Query family | Comparison | Mandatory cases |
|---|---|---|
| `FindNearestTriangle`, both overloads | InvalidID or a member of the minimizing ID set; squared distance; closest point on the selected triangle | Interior/edge/vertex closest points, tied triangles, finite and default cutoffs, cutoff below/at/above the actual distance, zero cutoff |
| `FindNearestHitTriangle` | Minimum forward ray parameter and an optimal triangle ID | Hit/miss, front/back face, origin inside a closed mesh, hit at origin, shared edge/vertex, parallel ray, finite cutoff |
| `FindAllHitTriangles` | Hit-ID multiset and returned count, not traversal order | Empty/all/some hits; null list versus populated list; a nonempty destination must retain its prefix; no duplicate IDs |
| `TestIntersection(Triangle3d)` and small tree/tree cases | Exhaustive triangle or triangle-pair primitive tests | Disjoint bounds, overlapping bounds without intersection, intersecting and touching pairs, single-child trees |

Before implementing the last row, match the public query's leaf predicate and coplanar policy explicitly;
using the more permissive exact oracle there would confound T1 with T2. Winding-number approximations,
containment on ambiguous surfaces, nearest-vertex queries and tree/tree distance optimization are
non-goals of this first bounded brief.

**Fixtures and build matrix.** Empty mesh, one triangle, procedural open grid, closed box, disconnected
components, equal-centroid triangles, sparse triangle IDs after deletion and an isolated far-away vertex.
Add small nonzero-area slivers; degenerate distance-kernel behavior is a separate contract probe, not a
finite-distance assumption hidden inside the main oracle. Exercise `TopDownMidpoint`, `TopDownMedian`
and `BottomUpFromOneRings`; cover `Fastest`, `FastVolumeMetric` and `MinimalVolume` on small bottom-up
fixtures, plus the default aliases. Vary top-down leaf size so the same query visits leaf-only, balanced
and single-child shapes. Use odd triangle counts as a named all-hit case.

**Contract details.** Current nearest-leaf comparisons are strict `<`, so a triangle exactly at the
cutoff is excluded; encode this deliberately rather than assuming `<=`. The all-hit ray test also uses
strict `<`. Equal-distance ties have no documented smallest-ID guarantee: compare the optimum and the
set of admissible IDs, not one linear traversal's winner. The ray direction must be normalized; test
rejection separately. `TriangleFilterF` can change without rebuilding: none, all, some, then none again.
A position/topology edit invalidates the tree (`IsValid`); queries must reject stale state until `Build()`.
After rebuilding, compare with a fresh tree. Check repeated `Build()` and repeated queries, and verify
that read-only queries do not change the mesh.

**Budget and acceptance.** Eight seeds, 128 point/ray queries per generated mesh, at most 128 triangles
per mesh. Run the full strategy matrix on named small fixtures and distribute random cases across it
without a large Cartesian product. Run representative fixtures under every similarity, transforming
rays and length cutoffs as well as meshes. Unique-hit cases must agree on the ID; tied cases agree on
geometry/distance. Every row must run on hits and misses; no sampled discrepancy is silently excluded.

**Sensitivity checks.** Temporarily bypass `TriangleFilterF`, prune an intersecting child, or duplicate a
reported hit, one at a time in an isolated checkout. Each fault must be caught by its targeted family.
Do not mutate exact arithmetic. Report which assertion failed and the restored passing run.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~AABBTreeDifferentialTests|FullyQualifiedName~ReviewFixesTests.DMeshAABBTree|FullyQualifiedName~SpatialFixesTests|FullyQualifiedName~QueryThresholdScaleTests"
```

## T2 — Coplanar triangle differential tests

**Goal and scope.** Add `tests/geometry3Sharp.Tests/CoplanarTriangleDifferentialTests.cs`. Exercise the
private `GetCoplanarIntersection` through the public
[`IntrTriangle3Triangle3`](../intersection/IntrTriangle3Triangle3.cs) API:
construct the object, set `ReportCoplanarIntersection = true`, then call `Find()` / `Compute()`.
No reflection or visibility changes. `Test()` is not a substitute for the polygon-producing path.
The current [SolverIntersectionFixesTests](../tests/geometry3Sharp.Tests/SolverIntersectionFixesTests.cs)
check status reset on a noncoplanar query; the new suite targets coplanar dispatch, projection and output.

**Exact oracle.** Use the existing public
[`ExactTriangleIntersection.Intersects`](../intersection/ExactTriangleIntersection.cs) for the Boolean
answer on closed triangles; sanity-check named cases with its independent `IntersectsByEdges` route.
Use `ExactPredicates.Orient3d` to assert exact coplanarity of generated reference inputs and `Orient2d`
for planar orientation/containment decisions. Do not call `IntrTriangle2Triangle2.Find()` to compute
expected results: that is the implementation under test's clipping routine.

**Two distinct contracts.** Exact predicates classify the supplied doubles; the legacy `Find()` uses an
epsilon-thick plane test. The main differential family therefore constructs exactly coplanar,
nondegenerate triangles from integer/dyadic planar coordinates. Offset planes just below, at and above
`MathUtil.ZeroTolerance` are a separate characterization family; a difference there is not automatically
a defect. Record exact and legacy answers and assert the deliberately chosen legacy contract. Do not
claim that this floating-point API becomes an exact intersection API through the new tests.

| Family | Required inputs | Assertions |
|---|---|---|
| Disjoint / containment / equality | Disjoint triangles, one strictly inside the other, identical triangles | Exact Boolean answer; expected polygon for containment/equality |
| Positive-area overlap | Hand-constructed intersections with triangle, quadrilateral, pentagon and hexagon outlines | Exact Boolean answer; expected canonical boundary and area; finite reconstructed coordinates |
| Closed-set contact | One shared vertex, full/partial shared edge, collinear overlapping edges | Exact Boolean answer and nonempty contact geometry, without assuming a Point/Segment type in this legacy coplanar path |
| Projection | XY, XZ, YZ planes, rational oblique planes and tied dominant-normal components | Exercise each dropped coordinate and reconstruct points on both input planes |
| Ordering | Swap triangles; all corner permutations of named cases | Same intersection set and area, independent of winding/start vertex |
| Conditioning | Thin but nonzero-area dyadic triangles, large translations, powers-of-two scales, tiny positive overlap/gap | Exact reference classification, finite geometry; any mismatch minimized and classified |
| Degenerate control | Segment/point triangles and separated/touching counterparts | Documented early route: Boolean result, `Type.Unknown` on contact, zero `Quantity`; not counted as coplanar-method coverage |

**Geometry oracle.** For named outline cases, derive expected vertices and area analytically, choosing
dyadic intersections where possible. Canonicalize cyclic order and orientation before comparing; retain
raw points/counts to detect duplicates or unexpected collapse. Validate the polygon against both input
triangles, its plane residual, convexity and area. Rounded boundary points need a tolerance derived from
coordinate resolution and operation conditioning; do not demand an exact zero predicate on a rounded
constructed point. Exact predicates remain decisive for the input classification. Random positives
must satisfy these geometry checks and swap symmetry, not only `Find() == true`. Missing portions of a
polygon are caught by the named expected outlines, not by containment alone.

**Reuse and similarities.** Run hit, miss, hit by assigning `Triangle0`/`Triangle1` through their setters
on the same instance; compare `Result`, `Type`, `Quantity` and the valid geometry with fresh instances.
Only inspect `PolygonPoints` when the result makes it valid. Repeated `Compute()` must agree. Test the
default flag on a fresh instance; changing only the public flag does not invalidate the cached result,
so do not accidentally turn a flag-toggle caching assumption into the polygon oracle.

Run every `Similarity.All()` transform on stable positive-area fixtures. Arbitrary rotations/translations
can turn exactly coplanar mathematical points into noncoplanar doubles. Check that explicitly: use the
exact differential contract for coplanarity-preserving transforms; keep rounded transforms in the
legacy-plane/metamorphic family, with known stable overlap and plane-residual checks. Report both
families rather than dropping transformed inputs. Use axis permutations and exactly representable
oblique constructions to cover projection independently of that rounding effect. Powers-of-two cases
must preserve applicable answers and mapped-back coordinates bit for bit.

**Budget, acceptance and stop rule.** Eight seeds with 256 coplanar pairs each; named outline/contact
cases always execute. Every projection branch and winding correction is reached through `Find()` with
the flag enabled. A contact mismatch is a candidate defect or a contract question requiring a reproducer,
not permission to redefine triangles as open sets. No production repair inside this brief. Existing exact
oracles suffice: if a new exact-construction subsystem appears necessary, stop and rescope rather than
silently increasing the tier.

**Sensitivity checks.** Disable coplanar dispatch, reverse a projection component, or perturb a lifted
output coordinate in the non-exact implementation. The Boolean, projection and polygon assertions must
respectively detect these faults. The oracle implementation remains unchanged.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~CoplanarTriangleDifferentialTests|FullyQualifiedName~SolverIntersectionFixesTests.IntrTriangle"
```

**Coverage evidence.** The inspected test project references xUnit/VSTest but no coverage collector;
the inspected CI does not collect coverage. Do not promise that `--collect:"XPlat Code Coverage"` works
without additional setup. Branch-targeted cases and fault injection are the required evidence here.
If an already configured external coverage tool is available during implementation, record its exact
command and baseline/new report for this method. Adding coverage tooling is a separate authorized task,
not an implicit dependency or an excuse to claim the reported zero coverage was verified.

## T3 — Stateful DMesh3 property tests

**Goal and scope.** Add `tests/geometry3Sharp.Tests/DMesh3PropertyTests.cs`. Test sequences of supported
public mutations of [`DMesh3`](../mesh/DMesh3.cs) and
[its edge operators](../mesh/DMesh3_edge_operators.cs), not a new property-test framework. Existing
[MeshFixesTests](../tests/geometry3Sharp.Tests/MeshFixesTests.cs) cover individual copy/compaction bugs;
retain them and add composition/lifetime coverage.

**Generator and command model.** Start from empty/isolated-vertex meshes, a triangle, an oriented open
grid and a closed box. Each command stores its chosen IDs and parameters before executing. Rebuild the
sorted eligible-ID list after every mutation; never reuse an ID merely because it once existed.
A deterministic prelude covers each operation's success before the random suffix:

- `AppendVertex`, attach an oriented triangle to an eligible boundary edge, and append a disconnected
  triangle with fresh vertices. Attribute-enabled fixtures use distinguishable normals/colors/UVs/groups.
- `SetVertex` with finite positions and bounded displacement; attribute setters on live IDs.
- `SplitEdge` on boundary and interior edges; `FlipEdge` on eligible interior edges; `CollapseEdge` with
  explicit keep/remove IDs; `PokeTriangle` with interior barycentric coordinates.
- `RemoveTriangle` with explicit isolated-vertex handling and `bPreserveManifold: true`; remove an
  isolated vertex; controlled `RemoveVertex` cases with documented parameters.
- `CompactInPlace(true)` after deletion and copy construction at checkpoints, retaining a separate map
  of logical entities across ID changes.

The strict random family preserves vertex manifoldness; do not indiscriminately append arbitrary
triples or remove triangles with the manifold guard disabled. A separate, small bowtie fixture can use
`CheckValidity(true, ...)` to characterize allowed nonmanifold vertices, but this must never weaken the
strict family. Geometry quality and self-intersection freedom are not implied by structural validity.

**After every command, including rejected ones.**

1. Assert `CheckValidity(false, FailMode.ReturnOnly)` in the strict family, with seed/log context.
2. Independently enumerate live vertices/triangles/edges; compare counts and check every live triangle's
   distinct live vertices, reciprocal edge/triangle incidence, boundary adjacency and vertex incidence.
   Build expected incidence from triangles instead of invoking the same adjacency helper on both sides.
3. Verify operation-specific effects: removed IDs no longer live, created IDs live and distinct, split
   position/incident-face changes, unchanged remote triangles, attributes retained where specified,
   and compaction map preserving positions and oriented triangles. This prevents a no-op mutation from
   passing merely because the mesh is still valid.
4. Maintain expected live counts for simple append/remove commands. For compound edge edits use their
   documented result info and independently counted local changes; do not build a second mesh editor.
5. Check positions and enabled attributes are finite. On the named closed, topology-preserving fixtures,
   also assert closedness and expected topology; open/deletion sequences are not required to stay closed.

**Rejection and state.** Add deterministic invalid-ID, boundary-flip, duplicate/nonmanifold triangle and
manifold-preserving removal rejection cases. Distinguish `MeshResult` from negative `AppendTriangle`
return IDs and documented exceptions. Assert no mutation only for failure paths checked to reject
before editing; do not impose transactionality on every compound failure without a contract.
Copy and replay must be independent: edits on one instance cannot alter the other. Warm cached bounds
before an edit and compare with freshly recomputed bounds afterward.

**Budget and acceptance.** Eight seeds per starting topology, 128 commands per sequence and a live-size
cap of 256 triangles (choose removal when at the cap). Track successful executions per command family;
the deterministic preludes ensure non-vacuity. Replay one fixed valid command trace through all
similarities; preserve topology/status and compare positions mapped back, using exact dyadic split/poke
weights for the power-of-two checks. Repeat traces on fresh meshes for determinism; no parallel API is
introduced solely for a determinism test.

**Sensitivity checks.** Omit a local adjacency/refcount update in an isolated copy, make a successful
append into a no-op, and omit one attribute propagation. Structural, operation-effect and attribute
assertions must fail respectively. Restore each fault before the next. CheckValidity itself is not the
only oracle and is not modified in the committed change.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~DMesh3PropertyTests|FullyQualifiedName~MeshFixesTests"
```

## T4 — gSerialization round trips

**Goal and scope.** Add `tests/geometry3Sharp.Tests/SerializationRoundTripTests.cs` using
[`gSerialization`](../io/gSerialization.cs), `MemoryStream`, `BinaryWriter` and `BinaryReader`.
No file-format changes or disk mesh fixtures. Existing
[CoreIOFixesTests](../tests/geometry3Sharp.Tests/CoreIOFixesTests.cs) already cover isolated vertices,
point clouds and version-1 meshes; [G3MeshReaderTests](../tests/geometry3Sharp.Tests/G3MeshReaderTests.cs)
cover valid files and malformed buffers. Extend positive round-trip coverage instead of repeating the
reader fuzz suite. The filesystem `SimpleStore` wrapper and new malformed-input fuzzing are non-goals.

| Family | Matrix | Semantic comparison |
|---|---|---|
| `DMesh3` | Empty, point cloud, open/closed, disconnected, isolated vertex, sparse vertex/triangle/edge IDs | Component flags, live ID sets, positions, oriented triangles, edge endpoints/incidence, enabled attributes and groups |
| Mesh attributes | Every combination of normals, colors, UVs and groups on a small sparse mesh | Exact stored component values; absence stays absence, not default-filled presence |
| DVector buffers | `int`, `short`, `float`, `double`; empty, one item, block-size minus/at/plus one, multiple blocks and shortened last block | Length and every element, including distinguishable values at block boundaries |
| Value types | Vector2/3 float/double, quaternion, frame, box, segment, circle, arc | Stored fields, extents, radii, angles and reversal flags; no accidental coordinate swap |
| Collections/text | Supported numeric lists, string and string array; empty/nonempty and non-ASCII text | Exact sequence/order and text; signed zero for directly stored floating fields |
| Curves/regions | Open/closed `DCurve3`, polyline, polygon with holes, mixed/nested `ParametricCurveSequence2`, `PlanarSolid2d` | Vertex/control data, closure, hole count/order and concrete supported curve types |

Use public `DVector.BlockCount` as the block-size boundary rather than hard-coding its current size.
For mesh values, use finite dyadic coordinates and distinguishable float attributes; do not normalize
restored vectors or recompute normals before comparison. For primitive storage, compare bits where
storage is direct; constructor-normalized types are compared by the actual stored representation.

**Version and ID contract.** Version 2 stores vertex reference counts and preserves isolated vertices;
version 1 cannot encode those. Construct a valid version-1 stream explicitly following the existing
fixture pattern, without changing the shared mutable `DMesh3Version` static during parallel tests.
Assert its representable subset only. A round trip preserves live IDs/topology, not necessarily the
allocator free-list order, timestamps, caches or the next newly allocated ID. Do not compare unused
buffer slots as if they were public geometry.

**Reuse and stronger checks.** Store A, restore it, compare against A independently of serialized bytes;
then store/restore the restored value again and compare semantically. Store two objects back-to-back
and verify exact stream consumption. For a reused mesh destination, restore rich A, sparse/unattributed
B, then A and compare against fresh destinations: old optional components must be discarded. Existing
list, polyline, polygon, `DCurve3` and sequence `Restore` overloads append to their destination; tests must
respect those overload-specific semantics rather than impose replacement globally. Use fresh instances
for their basic round trips and explicit prefix-preservation cases for append behavior.

Check mesh validity and independent incidence after restoration, then perform a small split or append/
remove sequence on the restored mesh to expose broken rebuilt bookkeeping. Do not use serialization
itself as the only equality helper: paired Store/Restore mistakes could cancel. For selected primitives
also inspect documented field order with `BinaryReader`; use distinct channel values.

**Budget and acceptance.** Named matrices above plus eight seeds with 32 small attributed meshes each;
cap random meshes at 128 triangles. Serialize transformed fixtures from `Similarity.All()` and require
exact equality to each transformed source (no arithmetic should be introduced by mesh serialization).
Then apply the normal inverse-transform comparison separately. All stream-based tests dispose their
streams and leave no files. Every directly supported family in the table has a nonempty success case.

**Sensitivity checks.** Omit a serialized component, retain an old destination attribute, or truncate a
DVector's last block in an isolated copy. Comparisons of fields, component presence and boundary values
must detect the faults. No fuzzing/tooling dependency is needed.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~SerializationRoundTripTests|FullyQualifiedName~CoreIOFixesTests.gSerialization|FullyQualifiedName~G3MeshReaderTests"
```

## T5 — Self-touching boundary loops

**Goal and scope.** Extend the existing
[MeshBoundaryLoopsTests](../tests/geometry3Sharp.Tests/MeshBoundaryLoopsTests.cs), reusing its procedural
fixtures and `Validate` helper. It already has two diagonally touching holes, verifies simple oriented
loops, fills the holes and checks the result. The gap is a broader adversarial matrix and lifecycle
coverage, not a missing first bowtie test. Existing external-file cases are not copied or expanded.

**Contract.** [`MeshBoundaryLoops`](../mesh_selection/MeshBoundaryLoops.cs) extracts simple cycles,
shares bowtie vertices between cycles, covers boundary edges, and does not modify the mesh. `Compute()`
always returns true, so that return value alone proves nothing. Default span/failure policies can
convert a failed walk to spans; valid named cycle fixtures must not pass by taking that fallback.

| Procedural case | Expected structure |
|---|---|
| Two separate fans sharing one vertex | Two simple cycles; shared vertex allowed across cycles but not repeated inside either cycle |
| Several fans around one shared vertex | One cycle per explicitly disconnected fan; no dropped/repeated edge |
| Two diagonal holes (existing fixture), with swapped insertion order | Outer cycle plus separate hole cycles; same edge partition |
| Chain of touching holes / hole touching outer border at a vertex | Fixture-defined simple-cycle decomposition, not one repeated-vertex walk |
| Tilted/nonplanar fans and near-aligned incident edges | Same topological decomposition under geometric perturbation that does not change incidence |
| Sparse IDs after deletions and permuted vertex/triangle insertion | Same geometric edge partition after remapping; not the same incidental loop order |
| Reversed whole mesh and a deliberately inconsistent local orientation | Consistent loops follow oriented boundary edges; inconsistent case reports `SawInconsistentOrientation` appropriately |
| Closed mesh, empty mesh, single triangle | No loops/spans for the first two, one simple triangle cycle for the last |

Build the expected edge partition from the known fans/holes before calling the extractor; for an
ambiguous high-valence fixture assert invariant coverage/simplicity instead of inventing an undocumented
pairing choice. Validate that each intended shared vertex really is a topological bowtie, rather than
several equal-position vertices with different IDs. Inputs may use `CheckValidity(true, FailMode.ReturnOnly)`;
record why allowing nonmanifold vertices is necessary. Edge manifoldness and finite positions still hold.

**Result checks.** Every expected boundary edge appears exactly once, adjacent loop vertices match its
endpoints, each cycle is simple and closed, all vertices/edges are live, and the mesh snapshot is unchanged.
Use the existing `Validate` helper for these assertions and add explicit expected partitions where
unique. For unfiltered well-formed cases require no spans, no fallback flag and no reported failure
bowties. If a simple planar hole is filled as an additional check, validate the resulting mesh and
positive triangle areas, not only the filler's Boolean result; do not require arbitrary nonplanar fans
to be fillable.

**Reuse and policies.** Use `bAutoCompute: false`, compute A, switch `Mesh` to a closed B, then back to A;
compare loops, spans and status fields to fresh instances. Separately remove one boundary edge with
`EdgeFilterF` to exercise `SpanBehaviors.Compute`, `Ignore` and `ThrowException`, and then clear the filter:
old spans/failure flags must not leak into a fresh successful computation. An intentionally filtered
open chain is not a failure of the unfiltered cycle contract.

**Budget and acceptance.** The named fixtures, eight fixed insertion-order permutations each, and every
similarity on a representative bowtie and chain. Compare canonical edge sets, preserving the separate
orientation assertions. Repeated execution agrees without imposing a loop-start or loop-list order
that the API does not promise. No parallel implementation is introduced.

**Sensitivity checks.** Bypass splitting a repeated-vertex walk, omit a boundary edge or retain an old
span flag. The simplicity, exact coverage and reuse assertions must respectively fail.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~MeshBoundaryLoopsTests"
```

## D1 — README “Additions in this fork” (tier S)

**Goal.** Make fork-specific additions easy to discover without confusing them with upstream features
or presenting plans as shipped functionality.

**Current state.** [README.md](../README.md) already starts with “About this fork” and a substantial
list of additions. This is a reorganization and targeted refresh, not a second competing feature list.
[OPERATIONS.md](OPERATIONS.md) is the detailed map of implemented operations; [UPSTREAM_CHANGES.md](UPSTREAM_CHANGES.md)
records observable compatibility changes. Keep these roles separate.

**Scope.** Only the fork-owned introductory section of `README.md`; mark D1 complete in the roadmap
only after the new section is present and checked. Do not rewrite the upstream text starting at
“A Short Note about the future of geometry3Sharp”, build instructions, format tables, or operation docs.

**Interface and outline.** Retain the “About this fork” introduction, the relationship to upstream
1.0.324 and its existing links. Add `## Additions in this fork` before the existing addition list, and
organize the material into concise user-facing groups:

- Robust geometry: exact predicates, exact booleans/arrangements, repair and alpha wrapping.
- Surface processing: intrinsic/heat geodesics, vector heat, UV parameterization/atlas tools.
- Analysis and application building blocks: certified deviation/simplification bounds where supported,
  thickness, fitting and terrain tools.
- Integration: PLY/3MF/glTF support, CLI/MCP, current target frameworks, XML docs and tests.

Link to the operation guide and CLI guide rather than copying their API tables. Retain useful existing
limitations, particularly open-input boolean behavior and rounded output coordinates. Move existing
material within the section or shorten duplication; do not introduce unsupported guarantees. Preserve
existing examples, or link to their tested counterparts rather than writing untested new examples.
Separate bug-fix history from the short feature overview via `UPSTREAM_CHANGES.md`.

**Acceptance.** The heading exists once near the top; every listed capability is implemented and has a
working relative link to its operation/example or existing guide. Planned 2D meshing/index work and other
unchecked roadmap items are not advertised as additions. The upstream baseline and Boost license are
unchanged. No new coverage, performance or universal robustness claims. Check the Markdown diff and
links; run `DocExamplesTests` only if a code example changes. Before a push, run the repository's full
local CI gate even for this documentation topic.

**Sources, budget and report.** README, OPERATIONS, UPSTREAM_CHANGES and the implemented entries of
ROADMAP only. One section reorganization, one link/claim verification pass; stop rather than extending
into an operation-documentation rewrite. Report the final heading/link and the checked diff. No
independent review agent is required specifically for tier S; apply the repository's pre-merge rules.

## Validation and delivery commands

Commands in this section are to be run during implementation, not evidence of completed execution.
Use the repository's existing xUnit/VSTest runner. Each topic first runs its focused selector, including
nearby existing tests when noted in the brief. Record actual before/after test and coverage results with
the command that generated them; do not repeat the reported zero coverage as a verified baseline.

Before each push, execute all local CI steps from [CLAUDE.md](../CLAUDE.md):

```powershell
dotnet build geometry3Sharp_netstandard.csproj -c Release
dotnet test tests\geometry3Sharp.Tests -c Release
dotnet test tests\g3cli.Tests -c Release
$packageDir = Join-Path $env:TEMP ("g3-qa-plan-" + [guid]::NewGuid().ToString("N"))
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o $packageDir -p:Version=1.0.0-ci.1
dotnet run --project tests\PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 "-p:G3PackageSource=$packageDir" -- "$packageDir\geometry3Sharp.1.0.0-ci.1.nupkg"
dotnet build tests\geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true
Get-ChildItem benchmarks -Filter *.csproj -Recurse | ForEach-Object {
    dotnet build $_.FullName -c Release
    if ($LASTEXITCODE -ne 0) { throw "Benchmark build failed: $($_.FullName)" }
}
```

Run each step separately and stop on failure; do not let a later success mask an earlier failure.
On Windows also execute each new test topic on net48, using `-p:TestNet48=true`, before delivery to check
that the tests and APIs work against netstandard2.0. Keep test output outside the source tree or in its
existing ignored results directory. Remove only the temporary package directory created for this run
after preserving the required logs.

Each tier-M PR gets one independent review with the diff, this brief and REVIEW_CHECKLIST, but not the
author's reasoning. Report any measured coverage with the collection command and raw output; coverage
is supplementary, not a substitute for oracle correctness or a demonstrated fault caught by a test.
After pushing, create/watch the topic PR and fix red CI within its agreed scope; never mark a blocked
production defect or an unexecuted test matrix as done.

## Execution report

### Implemented deliverables

- T1: [AABBTreeDifferentialTests.cs](../tests/geometry3Sharp.Tests/AABBTreeDifferentialTests.cs),
  linear-scan and analytic oracles, build-policy matrix, filters, rebuild/lifetime checks and similarities.
- T2: [CoplanarTriangleDifferentialTests.cs](../tests/geometry3Sharp.Tests/CoplanarTriangleDifferentialTests.cs),
  exact Boolean classification, analytic outlines, projections, permutations, seeded pairs, similarities,
  reuse, legacy epsilon behavior, degenerate inputs and thin/tiny dyadic geometry.
- T3: [DMesh3PropertyTests.cs](../tests/geometry3Sharp.Tests/DMesh3PropertyTests.cs), stateful mutation
  effects, independent incidence/attribute checks, replay, similarities and reduced compaction regressions.
- T4: [SerializationRoundTripTests.cs](../tests/geometry3Sharp.Tests/SerializationRoundTripTests.cs),
  component/topology/attribute round trips, malformed inputs and reduced destination-reuse regressions.
- T5: [MeshBoundaryLoopsTests.cs](../tests/geometry3Sharp.Tests/MeshBoundaryLoopsTests.cs), procedural
  touching fans/holes, independently expected edge partitions, orientation, input snapshots and filter reuse.
- D1: [README.md](../README.md#additions-in-this-fork), grouped implemented additions and checked links.

An independent diff review identified a missing independent positive-area oracle in the thin/tiny T2
fixtures. These now require the analytic triangle's raw vertex count, distinct points, expected outline
and positive expected area. Coordinate tolerance is capped by the shortest expected edge; area tolerance
uses that tolerance and the expected perimeter, rather than a dimensionally incorrect coordinate tolerance.
A reduced vertex-on-edge case from a failing seed was also added to the analytic outline matrix.

### Reproducible focused validation

Run from `D:\work\geometry3Sharp-QA`. Raw `.log` and `.trx` outputs are retained outside the source tree
in this session's `files` directory. The diagnostic framework runs are independent, not a continuation
of the failed push gate.

```powershell
$out = 'C:\Users\Harmen\.copilot\session-state\944e7e52-a0da-4e46-a71c-957b8e9ce973\files'
$filter = 'FullyQualifiedName~AABBTreeDifferentialTests|FullyQualifiedName~CoplanarTriangleDifferentialTests|FullyQualifiedName~DMesh3PropertyTests|FullyQualifiedName~SerializationRoundTripTests|FullyQualifiedName~MeshBoundaryLoopsTests|FullyQualifiedName~SolverIntersectionFixesTests.IntrTriangle'
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter $filter --logger 'trx;LogFileName=qa-final-net10.trx' --results-directory $out
dotnet test tests\geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true --filter $filter --logger 'trx;LogFileName=qa-final-net48.trx' --results-directory $out
```

| Command above | Total | Passed | Failed | Skipped | Exit code | Output |
|---|---:|---:|---:|---:|---:|---|
| net10.0 | 603 | 495 | 108 | 0 | 1 | `qa-final-net10.log`, `qa-final-net10.trx` |
| net48 | 603 | 495 | 108 | 0 | 1 | `qa-final-net48.log`, `qa-final-net48.trx` |

The previous same-topic matrix, before the additional vertex-on-edge theory row, reported 602 total,
495 passed and 107 failed on both frameworks (`qa-matrix-net10.trx`, `qa-matrix-net48.trx`). The newly
added failing analytic row accounts for the final difference; existing passing cases remain passing.

Failures below are theory rows, not counts of independent production bugs. Both final framework runs
produce the same families:

| Failing test family | Rows per framework | Observed behavior |
|---|---:|---|
| T1 `EmptyMesh_QueriesAgreeWithEmptyScan` | 84 | Empty-tree queries throw `ArgumentOutOfRangeException`. |
| T1 `StaleOtherTree_IntersectionMustRejectInvalidArgument` | 1 | A stale argument tree is accepted instead of rejected. |
| T3 `Compact_LiveIsolatedVertexAboveDeletionHole_PreservesMappedPayload` | 4 | Moving the isolated live vertex throws `SmallListSet.MoveTo: list at 3 is empty!`. |
| T3 `SeededMutations_ValidateEffectsIncidenceAttributesAndReplay` | 4 | Stateful traces reach the same compaction failure. |
| T4 `MeshRestore_ReusedDestination_RichSparseRichMatchesFresh` | 1 | Reused destination has invalid triangle-edge data. |
| T4 `MeshRestore_ReusedDestination_SingleTriangleThenDeletedMatchesFresh` | 1 | Reduced restore-reuse case reproduces invalid triangle-edge data. |
| T2 `NamedOutlines_AllProjectionsAndCornerPermutations` | 5 | Closed vertex, vertex-on-edge and collinear edge contacts are not reported. |
| T2 `SeededExactlyCoplanarPairs_AgreeWithExactClassificationAndSwap` | 6 | Exact oracle says true, legacy query false; concrete inputs are in TRX. |
| T5 `FilterPolicies_OneMissingEdgeAndResetDoNotRetainSpanFlags` | 2 | `SawOpenSpans` survives filter removal and recomputation. |

For the seed failures, shared-vertex inputs occur in seeds 7, 101, 701 and 4093; seed 1237 has a
collinear edge overlap. Seed 307's first failing XZ pair reduces to the named vertex-on-edge fixture:
A=(6,-10),(12,9),(14,-5), B=(16,-10),(4,-11),(12,-7), expected outline=(6,-10).
The named fixture independently verifies the exact closed-contact classification and expected point.
These failures are retained, not skipped or hidden by increased tolerances.

### Fault sensitivity and review correction

In an isolated detached checkout, one representative fault per topic was introduced at a comparison
boundary or in non-exact production code. The exact oracle and mutation infrastructure were untouched.
The control selection was executed before faults, with faults, and after restoration:

```powershell
$filter = 'FullyQualifiedName~NamedOracles_ExerciseStrict|FullyQualifiedName~CoplanarTriangleDifferentialTests.Reuse_|FullyQualifiedName~EarlyRejections_InvalidIDsBoundaryFlip|FullyQualifiedName~SerializationRoundTripTests.MeshMatrix|FullyQualifiedName~AdversarialBowties_'
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter $filter --logger "trx;LogFileName=qa-sensitivity-$stage.trx" --results-directory $out
```

Here `$stage` was `before`, `faults` and `restored`. The results were respectively 95/95 passing,
80 failed with 15 passing, and 95/95 passing. Faults: incorrect nearest ID (T1), off-plane polygon point
(T2), false successful boundary flip (T3), changed restored UV (T4), missing boundary-loop edge (T5).
Raw outputs: `qa-sensitivity-before`, `qa-sensitivity-faults`, `qa-sensitivity-restored` (`.log`/`.trx`).

The review correction received its own isolated control/fault/restoration check. A comparison-boundary
fault replaced each positive thin/tiny polygon by its first contained point:

```powershell
$filter = 'FullyQualifiedName~CoplanarTriangleDifferentialTests.TinyDyadic|FullyQualifiedName~CoplanarTriangleDifferentialTests.ThinDyadic'
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter $filter --logger "trx;LogFileName=qa-review-sensitivity-$stage.trx" --results-directory $out
```

Stages `before`, `fault`, `restored` produced 3 passed/0 failed, 1 passed/2 failed, 3 passed/0 failed.
Both positive-area tests reject the collapse at the analytic raw vertex-count assertion; the gap stays
passing. The restored thin/tiny cases also pass in the final net48 matrix. Both temporary sensitivity
worktrees were removed after preserving output; no faults remain in the QA branch.

### Acceptance and remaining gates

The focused matrix is executed, but T1-T5 acceptance is blocked by the production failures above.
Production fixes require separate briefs; exact-kernel work, if needed, requires tier-L approval.
The full library suite, CLI suite, pack/API compatibility validation, package smoke test and benchmark
builds were not completed for this delivery: the library diagnostic tests already failed, so the gate
stops under the brief's failure rule. No coverage, benchmark or green-CI claim is made. Do not push,
mark ready or merge this branch until the defects and the complete local CI gate are addressed.
