# 2D meshing M1: exact point-set foundation

Status: **proposal for owner approval; no implementation authorized by this document yet**.
Tier: **L** (new, specification-driven module). Branch: `feature/meshing2d`, based on
`origin/master` at `93880ed`; independent of QA PR #193.

## 1. Specification reconciliation

The supplied "Pasted text #1" and [MESHING2D_SPEC.md](MESHING2D_SPEC.md) compare identically with
`git diff --no-index -- docs/design/MESHING2D_SPEC.md <attachment-path>` (exit 0).
There is no new specification content to import. The attachment remains read-only.
The decisions in specification section 5 override sections 1–4:

- One algorithm, always-exact predicates; net48 remains mandatory.
- Ring-based holes and regions, region labels and region area limits are in the first complete version.
- A size function replaces the arbitrary refinement predicate; no maximum-angle option.
- Input indices are mesh identities; merged inputs require an explicit mapping.
- Optional heights are interpolated on the input reference CDT, not the evolving refined mesh.
- Failures are explicit statuses with diagnostics; exceptions are for arguments and cancellation.
- Quality exceptions and the Steiner-limit flag are observable. The default Steiner budget is
  `10 * (points + segments) + 100000`.
- Automatic relative snapping is the default; data-precision snapping is opt-in.
- Voronoi output, finished-mesh smoothing, algorithm selection, file formats and bandwidth renumbering
  are not part of the first version. Terrain simplification is a separate topic.

Consequences for the catalogue: F-28/F-29 and the maximum-angle portions of F-30/F-31/F-41 are not
requirements for a removed option; the minimum-angle/area portions remain relevant. F-63 belongs to
terrain simplification. F-64 must be separated into deferred finished-mesh smoothing and applicable
future Steiner-smoothing safety tests. F-9 uses a failure status rather than an exception. F-46 uses
ring semantics, not the historical seed-and-spread limitation. Historical timings are targets, not
measurements of this repository or acceptance results.

## 2. Proposed delivery split

| Stage | Deliverable | Deferred until this stage |
|---|---|---|
| **M1 (this brief)** | Exact point-set Delaunay mesh over the convex hull; stable identities, topology, point location, validation | No constraints or coordinate movement |
| M2 | Normalized planar constraints and reference CDT | Segment noding, snapping, marker/provenance rules, ring domains/holes/regions and collapsed-ring handling |
| M3 | Conforming/quality refinement on that reference domain | Minimum angle, area/size function, splitting permissions, bounded insertion, reuse/refinement, reference-mesh heights |
| M4 | Grid-constrained output and postprocessing | Snap-rounding guarantees, wedge policy integration, Steiner reduction/smoothing, complete production acceptance and performance |

M2–M4 are a dependency outline, **not approved implementation briefs**. Grid/wedge requirements must
inform the M2 representation and M3 design; they cannot be implemented as unchecked final rounding.
Each stage may need several independently reviewable PRs. Do not describe M1 as the complete mesher
or as satisfying default automatic snapping in O11/Q14: it is an explicit exact-input foundation.

M1 is deliberately smaller than reference CDT: failing cleanly on adversarial intersections is not
an acceptable substitute for designing their representation, normalization and displacement bounds.

## 3. M1 task brief

**Goal.** A caller can triangulate finite planar points without moving them, locate points in the
result, and inspect a validated, deterministic Delaunay mesh on net10 and net48.

**Proposed implementation scope.** New `comp_geom/DelaunayMesh2.cs`, `comp_geom/Triangulation2Result.cs`,
`comp_geom/TriangulationMesh2.cs` and directly necessary internal topology code; focused tests under
`tests/geometry3Sharp.Tests`; a procedural `benchmarks/Meshing2Benchmark` harness; this design,
`docs/QA.md` and `docs/SOURCES.md`. File boundaries may be consolidated before API approval.
Reuse the existing `ExactPredicates.Orient2d` and `InCircle` without changing their implementation.

**Non-goals.** No segments, ring classification, snapping, grid, refinement, heights, spatial-index
implementation, new file readers, CLI/MCP changes, parallel triangulation algorithm or dependencies.
Do not expose placeholder options for those features. No changes to `ExactMesh*`, arithmetic/error
bounds or mutation tooling as incidental work. Preserve the upstream 1.0.324 API and existing callers.

**Sources.** Only the behavioural specification, published literature and existing repository code
with documented clean-room provenance. [SOURCES.md](../SOURCES.md) records `ExactTriangulation2` and
predicates as paper-derived. Never read Triangle/Triangle.NET, the owner's quadtree, company code or
GPL implementations. Do not consult the owner-only failure-to-commit mapping. No mesh files are to
be committed. No external corpus is needed for M1; later corpus use requires a license check.

**Budget/stop rule.** Stop this planning task at owner approval. After approval, first freeze the API
and commission independent hidden acceptance tests. Run a bounded bootstrap/numerical-range spike
before the public implementation. If existing-kernel reuse needs changes to exact arithmetic,
unproved constructions or broad changes to Boolean callers, stop and submit a revised scope/design.
Do not expand into M2 or suppress failing adversarial inputs to finish M1. Performance targets must
be negotiated from the baseline; no unmeasured throughput promises.

**Report.** One M1 PR, commands/raw results, mutation evidence, bounds review and independent review.
No automatic merge. All local CI gates precede a push.

## 4. Proposed API contract (names/signatures for approval)

This is a contract sketch, not a compilable implementation. Types live in `g3` and use APIs available
through netstandard2.0; no net10-only public types.

```csharp
public sealed class DelaunayMesh2
{
    public Triangulation2Result Compute(
        IReadOnlyList<Vector2d> points,
        CancellationToken cancellationToken = default(CancellationToken));
}

public enum Triangulation2Status
{
    Success,
    Empty,
    Failed
}

public enum Triangulation2FailureKind
{
    None,
    NumericalFailure,
    IterationLimit,
    InvalidTopology
}

public sealed class Triangulation2Result
{
    public Triangulation2Status Status { get; }
    public Triangulation2FailureKind FailureKind { get; }
    public Vector2d? ProblemCoordinate { get; }
    public int? ProblemInputIndex { get; }
    public TriangulationMesh2 Mesh { get; }
}

public sealed class TriangulationMesh2
{
    public IReadOnlyList<Vector2d> Vertices { get; }
    public IReadOnlyList<int> InputToVertex { get; }
    public IReadOnlyList<Index3i> Triangles { get; }
    public IReadOnlyList<Index3i> Neighbours { get; }
    public IEnumerable<Index2i> Edges();
    public IEnumerable<int> IncidentTriangles(int vertexId);
    public bool TryLocate(Vector2d point, out int triangleId);
    public bool IsValid(out string reason);
}
```

### Identity and ownership

- Input list index `i` is the vertex ID. No caller-owned vertex objects and no uniqueness requirement
  on application IDs: database keys or duplicate user IDs remain caller metadata indexed by `i`.
  A future segment refers to input indices, never to an ambiguous application ID. This interpretation
  of Q8 versus I1/R2/F-59 needs owner approval before the public API is frozen.
- M1 retains one coordinate slot per input, without compaction. Exactly coincident points map to the
  smallest input index via `InputToVertex`; triangles use only that representative. `+0` and `-0`
  compare equal; emitted zero coordinates are canonical `+0`. All other coordinate bits are preserved.
  Alias slots have empty incident-triangle enumeration; callers can query the representative explicitly.
- Every distinct point participates in a nondegenerate mesh, including points on hull edges. For a
  collinear/insufficient set, preserve the vertex slots/mapping and return no triangles or mesh edges.
- No Steiner points in M1. Later insertions must use IDs at least the original input count and must not
  compact away that reserved range. Triangle IDs are dense result-local indices, not insertion IDs.
- Inputs are snapshotted before computation. Concurrent mutation of the caller's list while the snapshot
  is being taken is not supported. Results own their storage; exposed collections must not allow mutation
  by casting to arrays/lists. Reusing an operation never changes any earlier result.
- Instances may retain private scratch storage, but concurrent `Compute` calls on the same instance are
  not supported. Independent instances can run concurrently. Result reads and nested enumerations are
  independent and thread-safe. No shared mutable random state or returned pooled buffers.

### Topology and queries

- Triangles are strictly counter-clockwise under exact orientation. Their union is exactly the convex
  hull of the distinct input coordinates, with no gaps, overlap, T-junctions or unused distinct points.
- `Neighbours[t][k]` is the triangle across the edge opposite `Triangles[t][k]`; `-1` denotes the hull.
  Neighbour relations are reciprocal. `Edges()` emits each undirected mesh edge once, lower endpoint
  first. Invalid vertex indices throw `ArgumentOutOfRangeException`.
- Point location uses closed triangles: points exactly on edges or vertices are hits; outside points
  and empty meshes return `false` and triangle ID `-1`. A multi-hit returns the lowest triangle ID.
  No tolerance silently converts an exterior point to an interior point.
- `IsValid` checks finite coordinates, index/mapping consistency, positive exact orientation, incidence,
  symmetric adjacency, non-overlap, hull coverage and exact Delaunay legality. This is an explicit
  potentially expensive diagnostic, not a promise of allocation-free or constant-time validation.
- Compute must gate successful output with sufficient invariant checks; `Failed` never publishes an
  invalid/partial mesh (`Mesh == null`). `Success` and `Empty` always carry an owned mesh. Validation
  failures have `FailureKind != None`; normal/empty results have `None` and no problem location.

### Numeric range, ties and failures

- Proposed public coordinate cap: finite `abs(x), abs(y) <= 1e70`, inclusive of the represented boundary
  value; no positive lower cutoff. Test tiny/subnormal and mixed-magnitude coordinates explicitly.
  Validate coordinates component by component, including point-location queries. Null lists, nonfinite
  and out-of-range values are argument errors identifying the input index/component.
- Degeneracy uses exact predicates, not a rounded area or epsilon. There is no predicate-disable option.
- Avoid assuming that uniform normalization or a finite padded supertriangle preserves signs over the
  full mixed-magnitude range. A construction can fail even when a predicate's sign is exact.
- Cocircular ties must use a consistent total ordering based on representative input indices. Specify
  the exact tie rule during the spike, prove it cannot cause a flip cycle, and canonicalize emitted
  triangle/edge ordering. Do not rely on dictionary iteration, thread scheduling or runtime trig results.
  Reordering the input is not promised to preserve the diagonal; fixed input is bit-identical across runs.
- Cancellation throws `OperationCanceledException`, with no input/earlier-result mutation. A detected
  invariant or progress failure returns a diagnostic status. A progress guard is a safety net, not an
  accepted outcome on valid M1 acceptance inputs. Do not catch out-of-memory or unrelated programming
  exceptions and call them successful empty meshes.

## 5. Existing-kernel fit and bounds checkpoint

[ExactTriangulation2](../../mesh_ops/ExactTriangulation2.cs) is internal. Its documented preconditions
require distinct points in one supplied enclosing triangle, and already-split noncrossing constraints
that do not pass through other points. It is not a public point-set or arbitrary-PSLG mesher.
Its predicate delegates and triangle enumeration are useful reuse candidates; M1 cannot simply expose
its constructor and claim the specification is implemented.

Before coding the adapter, the spike must decide between a proved general-hull bootstrap integrated
with the existing topology and a separate paper-derived point-set kernel. It must demonstrate exact
hull coverage, on-edge insertion, consistent cocircular ties and cancellation/progress behaviour.
A finite artificial enclosing triangle is not assumed safe. No discarded artificial vertex may leave
missing hull triangles. Any modification of the existing kernel also requires its direct tests and
Boolean caller regression tests; modifications outside the approved scope need a brief revision.

The independent bounds review must examine the complete coordinate flow: predicate domain, comparisons,
intermediate constructions, integer/count overflow, termination, ordering, and ownership after failure.
Existing exact predicates are a dependency, not evidence that new constructions are automatically exact.
No new tolerance/error constant is approved here. Record any derived bounds before implementation.

## 6. Acceptance and evidence plan

These are **proposed tests/targets, not measured outcomes**. A separate clean-room agent authors hidden
acceptance cases from the approved contract/spec alone; the implementation author sees results but
must not modify those tests. The acceptance author must not use mesher production predicates as the
only correctness oracle: use independently evaluated exact dyadic determinants and convex-hull/area
checks on bounded fixtures.

| Group | Required evidence | Specification mapping |
|---|---|---|
| Basic/degenerate | Empty, one/two distinct points, all identical, all collinear, duplicates and signed zero; exact hull coverage for triangle/square/interior and hull-edge points | I1, O18, F-57, F-61 |
| Predicate range | Nearly collinear/cocircular sets, accepted cap, rejected values beyond it, NaN/Inf, tiny/subnormal and mixed magnitudes; determinant-sign oracle | F-51–F-54 (applicable point-set subset) |
| Identity | Alias mapping, noncompacted IDs, empty alias iteration, caller metadata surviving via input indices | Q8, F-59 (point-set subset) |
| Delaunay/ties | Fixed dyadic cocircular sets and grids; strictly positive faces, exact empty circumcircles, no crossing edges, reciprocal neighbours, exact hull area | F-56, R3/R5/R6/R9 |
| Queries | Interior, exterior, hull, every interior edge and vertex; stable lowest-ID tie and empty result | F-62, R7 |
| State | Repeat one instance, alternate large/small/empty/failing/cancelled runs, unchanged input and old results, independent parallel instances equal serial | I9, O20/O22, F-58/F-60 |
| Invariance | `Similarity.All()` interpreted in the transformed plane's orthonormal basis (never simply drop z after a 3D tilt); exact representable power-of-two transformations preserve topology bit for bit; additional planar reflections use inverse mapping and CCW canonicalization | CLAUDE.md |
| Termination | Adversarial degenerate/high-degree point sets in child processes with an explicit timeout; cancellation checked during work | Global guarantees |

Transformations that round away distinct input coordinates are different geometry; test their validity
separately rather than asserting impossible bitwise invariance. Cross-runtime fixtures use fixed bits
or deterministic integer construction, not runtime `Sin`/`Cos` generating different inputs.

Mutation checks must break orientation/legality decisions, alias mapping, neighbour assignment, boundary
point location and result ownership, and demonstrate red tests. Do not count missing-API compilation
failure as the sole regression evidence. Use existing mutation facilities without editing their config;
if required scope is unsupported, agree on targeted reversible mutations rather than altering tooling.

Benchmark the bootstrap candidate versus the selected implementation on identical procedural uniform,
grid and nearly degenerate point sets. Capture net10/net48 commands, input fingerprints, wall time,
allocations and topology validation for both; label this as A/B within this repository, not a comparison
with restricted implementations. Establish results before negotiating a hard throughput gate.

Before push: run all local CI in CLAUDE.md, including package API validation and every benchmark build.
Before ready/merge: independent code/bounds review, hidden acceptance, targeted mutation checks and
recorded A/B evidence. A green build alone does not complete M1.

## 7. Approval requested

1. Deliver point-set M1 first, with no snapping or constraints; full automatic snapping remains an M2
   requirement, not an implicitly disabled option in the complete mesher.
2. Approve the proposed public names/result ownership and input-index identities with caller-side
   application metadata; otherwise revise the model before tests/API are frozen.
3. Approve the exact-input numeric cap, closed point-location semantics and empty-result contract.

Later designs must still resolve overlapping nonzero markers, conflicting heights on merged inputs,
size-function units/combination with area limits, exact automatic/wedge displacement bounds and grid
rounding ties. This proposal does not invent answers or supersede the owner's existing decisions.
