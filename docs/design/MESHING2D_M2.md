# 2D meshing M2: normalized constraints and reference CDT

**Owner note (2026-10-30): the approvals recorded here are disputed.** Where this document says the owner approved something, or calls the API frozen, that is the record of an earlier session. The owner says this is not really the case: whether these domain decisions are the right ones must be questioned. Treat them as proposals.

Status: **proposal; domain decisions and API open for review; verification gates remain open**.
No M2 production implementation exists. The frozen API is defined in [MESHING2D_M2_API.md](MESHING2D_M2_API.md).
Historical proposal sections below are superseded by that contract.
Tier: **L**. Builds on [M1](MESHING2D_M1.md) and the owner decisions in
[MESHING2D_SPEC.md section 5](MESHING2D_SPEC.md#5-owner-decisions-october-2026).
M1 review/CI acceptance remains separate; starting this design does not mark M1 complete.

## 1. Task brief and delivery boundaries

**Goal.** Produce an owned, deterministic reference constrained Delaunay triangulation of normalized
planar points, segments and ring domains, with exact topology and auditable snapping/provenance.
The same normalized reference domain will be reused by M3, not recomputed with different snapping.

**Scope.** New planar input/result types and constrained mesher under `comp_geom`, directly necessary
exact planar coordinate/construction support, procedural tests, sources and operation documentation.
Existing M1 APIs and upstream 1.0.324 callers stay compatible. Any change to `math/Exact*` or existing
predicate bounds requires a separate explicit brief and independent bounds/mutation review.

**Non-goals.** Quality/conforming refinement, minimum angles, size functions, height interpolation,
grid/wedge rounding, Steiner smoothing, CLI/readers, new spatial-index implementation, dependencies,
parallel triangulation and performance promises. Maximum region area is stored for M3, not enforced here.
M2 is constrained Delaunay, not all-point empty-circumcircle Delaunay across protected constraints.

**Sources.** Behavioral specification and published literature only: Lawson edge flips, Sloan's
constrained triangulation paper, exact rational planar arrangements and documented snap-rounding
literature. No external implementation port. Never read Triangle/Triangle.NET, the owner's quadtree,
company code, owner-only defect mappings or GPL code. No mesh files.

**Budget/stop.** Design and independent acceptance drafts first. Stop for owner approval before importing
pending API tests or production code. The owner authorized the isolated construction/snapping spike on
2026-10-30; this does not freeze the public API or authorize production implementation. Use a standalone
C# executable with procedural assertions and raw evidence outside the repository, without reading the
independent acceptance drafts. Test fixtures precede the candidate implementation. Evaluate at most two
concrete routing candidates; distinguish exact proofs, passed experiments and unsupported cases.
A bounded exact-construction/snapping spike must pass adversarial fixtures before implementation is
frozen. Unproved displacement bounds, lost segment coverage or new normalization cycles require revising
the design, not silently rejecting supported inputs.

Deliver M2 in independently reviewable increments:

| Increment | Deliverable | Gate |
|---|---|---|
| M2a | Input snapshot, exact rational nodes, duplicates/overlaps and provenance; explicit failure when noding is disabled | Exact construction identities, coverage and identity tests; bounds review |
| M2b | Deterministic relative/explicit snapping and certified normalized arrangement | Displacement certificates, fixed point/idempotence, F-10–F-18 adversarial tests |
| M2c | Constraint recovery, reference CDT, outer/hole/region classification and queries | Independent exact topology/domain oracle, mutation checks and bounded A/B |

These are delivery increments, not alternate user-facing algorithms. M2a alone is not a completed M2.

## 2. Owner decisions obtained during this design

- Coincident subsegments retain **all** originating explicit segment indices and marker values.
  Exactly one distinct nonzero source marker gives that scalar marker; several give scalar marker `0`,
  with the full source-marker set retained. Zero never overwrites a nonzero source.
- Outer rings are combined by **union**. Holes subtract their union from that domain, even when internal
  breaklines split a hole. An overlapping or nested hole does not toggle parity back to filled.
- Region rings and seeds share **one ordered input list; last matching region wins**, including its
  maximum-area metadata. A seed labels its connected constraint cell. Seeds exactly on a constraint
  boundary are ignored with a diagnostic.
- The owner approved exact rational authoritative topology, a non-certified display-double view,
  the M2a–c split and the normalization policy below. Segment-routing proofs remain a spike gate.
- The owner approved even-odd fill within each self-crossing ring, the missing-outer convex hull rule,
  all-source refinement locking, and exact interior witnesses with a certified-double Try conversion.
- On 2026-10-30 the owner approved the provenance-preserving idempotence direction: normalizing an
  owned normalized object retains its original sources and certificates and must yield an equivalent
  verified object. Reimporting its coordinates and edges as fresh input is a separate operation, not the
  idempotence contract. This does not approve a specific routing implementation or waive other gates.
- These decisions do not authorize moving surviving input vertices, choosing conflicting input heights,
  or introducing grid rounding in M2.

## 3. Exact geometry versus display coordinates

A crossing of two dyadic input segments is generally rational, not exactly representable as a double.
Rounding it and applying exact predicates to the rounded coordinates would change the arrangement.
Therefore the reference topology is defined over **exact rational coordinates**, not display doubles.

Pending API type `ExactPlanarPoint2` stores `BigInteger XNumerator`, `YNumerator`, `Denominator` with positive
shared denominator, reduced canonically. Input double bits are converted exactly, including subnormals.
Intersections, orientation, incircle and domain classification use rational/homogeneous arithmetic.
No `1e70` double predicate guarantee is reused for newly constructed coordinates without a proof.

`Vertices` provides finite rounded doubles for compatibility/display, while `ExactVertices` is authoritative.
Rounded duplicate coordinates do not merge distinct exact nodes. Display rounding can produce zero-area
faces; do not advertise the display coordinate array as a certified double mesh. M4 owns certified grid
output. Queries from double coordinates are converted exactly before testing the reference mesh.

Before M2a implementation, the spike must establish determinant identities, positive denominator handling,
exact ordering, coincident-node canonicalization and finite conversion across the accepted componentwise
input range `abs <= 1e70`. Rational bit growth and bounded cancellation polling must be measured; no
fixed memory/time guarantee is invented. Exact construction must not rely on a floating intersection
followed by a tolerance merge to restore topology.

## 4. Normalization and snapping contract (owner-approved policy)

1. Snapshot all caller lists and validate indices, coordinates and options. Exceptions are reserved for
   argument errors and cancellation; unsupported construction/verification outcomes return Failed/null mesh.
2. One reserved output slot per input index; exact duplicates use the lowest representative input index.
   New arrangement nodes have ids at least the input count. Alias slots remain queryable metadata but
   are never used by faces. Caller objects and prior results are not mutated by reuse or cancellation.
3. Zero-length segments are ignored geometrically. Duplicate/reversed segments preserve provenance.
   Exact collinear overlap is split into atomic subsegments at every covered input endpoint.
4. With crossing resolution enabled, proper crossings, T-junctions and coincident crossings share exact
   nodes. With it disabled, a proper crossing requiring a new node returns `CrossingsDisabled` and no
   mesh. Existing-node splits and collinear overlap normalization still occur; no invalid mesh is returned.
5. Explicit positive `SnapTolerance` is a coordinate-unit distance. Owner-approved automatic tolerance is
   `2^-42 * max(maxAbsInputCoordinate, boundingBoxSpan)` evaluated exactly, without a positive floor.
   This targets numerical noise at UTM magnitude; it is **not translation invariant**. The resolved
   tolerance is reported. Explicit tolerance scales with a similarity; automatic policy is tested separately.
   The multiplier is an approved policy, not a measured optimum or a proved accuracy guarantee.
6. Candidate point merges use exact squared Euclidean distance and the lowest input-index representative.
   Every merged original point must lie within the resolved tolerance of its representative; transitive
   chains cannot move a distant endpoint beyond tolerance. Surviving input coordinates are not projected.
7. A vertex near a segment is retained; the normalized constraint is routed through it (F-10), rather than
   moving the vertex onto the old supporting line. Each output piece retains its source coverage interval
   and a displacement certificate against that source. Normalization must not accumulate unbounded drift
   through successive local snaps, or alternate between arrangements (F-15).
8. M2b must specify and prove its finite deterministic event ordering before implementation. The spike
   must reject candidate rules that lose coverage, violate the tolerance or cycle; this draft does not
   pretend that point clustering alone solves segment snapping or double-digitized borders.

Each source segment exposes its ordered surviving subsegment chain, source parameter intervals, and any
removed/collapsed intervals with diagnostics. Coverage means the source's documented normalized path,
not exact equality to the unsnapped original line after deliberate snapping. Strict zero-snap diagnostics
are planned internally for construction tests; public `0` retains its specified automatic meaning.
Refinement locking never prevents mandatory noding. On an overlapped piece, **all** covering sources
must permit optional refinement splitting; one locked source locks the piece.

## 5. Ring domains, holes and regions

- A repeated final input index or coordinate is accepted; orientation does not alter filled area.
- Self-crossing ring fill is even-odd **within each ring** (bowtie F-7), then union outer rings.
  Ring orientation is not a hole flag; the caller supplies Outer or Hole explicitly.
- With no outer ring, use the convex hull of the normalized point set, as M1 does.
- A collapsed/collinear/folded-back hole removes no area and retains nonzero boundary pieces as ordinary
  constraints. Nondegenerate hole interiors remove interior constraints; clipped sources retain provenance
  and diagnostics. Points outside the retained domain keep their slots but have no incident faces.
- Ring boundaries participate in noding. Closed-domain point location includes retained boundaries and
  returns the lowest face id on a shared edge/vertex; an interior hole point has no hit.
- Rings and seeds share one ordered region list. The last matching entry labels retained face interiors
  and supplies their maximum-area metadata. A default label is `0`; maximum area is nullable, finite and
  positive when supplied, carried separately from the label.
- A seed matches its connected cell bounded by normalized constraints. A seed exactly on a constraint
  boundary is ignored with a diagnostic; seeds outside the retained domain do not label any face.
- The ring interior-point helper returns an exact strictly interior witness when one exists. A separate
  Try conversion returns a double only after certifying that the rounded point is strictly interior.
  Degenerate rings have no witness; thin rings may have an exact witness without a representable-double
  witness (F-49/F-50). The final signatures and diagnostic layout remain an API gate.

## 6. Pending API sketch for acceptance drafts

Names below are **proposals**, not production compatibility commitments. The independently written test
draft is staged outside the repository so an unapproved missing API does not break unrelated builds.

```csharp
ConstraintTriangulation2Result ConstrainedDelaunayMesh2.Compute(
    PlanarConstraintInput2 input,
    ConstraintMeshingOptions2 options = null,
    CancellationToken cancellationToken = default);

PlanarConstraintInput2(IReadOnlyList<Vector2d> points,
    IReadOnlyList<ConstraintSegment2> segments,
    IReadOnlyList<ConstraintRing2> rings = null,
    IReadOnlyList<ConstraintRegion2> regions = null);
ConstraintSegment2(int a, int b, int marker = 0, bool allowRefinementSplit = true);
ConstraintRing2(IReadOnlyList<int> vertices, ConstraintRingKind2 kind, int marker = 0);
ConstraintRegion2(IReadOnlyList<int> vertices, int label, double? maximumArea = null);
ConstraintRegion2(Vector2d seed, int label, double? maximumArea = null);
```

The two region forms occupy the same `regions` list; its order, not separate ring/seed grouping, controls
precedence. These overloads remain proposed signatures pending API approval.

`ConstraintRingKind2`: Outer/Hole. Options initially contain `ResolveCrossings = true` and
`SnapTolerance = 0`. Result uses the existing `Triangulation2Status`; failure kind includes
`CrossingsDisabled`, construction/normalization failure and verification failure with offending sources.
No partial invalid mesh is returned. Empty retained domains return Empty with an owned metadata mesh.

`ConstraintMesh2` readonly views include `Vertices`, `ExactVertices`, `InputVertexMap`, `Triangles`,
`Neighbours`, `TriangleRegionLabels`, region area limits and `Subsegments`, plus closed `TryLocate`.
`ConstraintSubsegment2` includes `A`, `B`, scalar `Marker`, `SourceSegmentIndices`, `SourceMarkers`,
`AllowRefinementSplit`. Ring provenance is separate from explicit segment-list indices; it must not
fabricate an index into the caller's segment list. Source interval/coverage diagnostics are required but
need their final type layout at API approval. Result wrappers must not change M1's Delaunay semantics.

### 6.1 Proposed ownership and metadata surface (not approved)

The owner requested a full signature review rather than freezing this outline. The detailed
[public API proposal](MESHING2D_M2_API.md) lists signatures, default/invalid-value semantics,
source-span conventions and remaining owner decisions. In particular caller-ID/point-marker metadata,
seed-only holes and the normalization-versus-clipping boundary must not be silently omitted.

The following completes the **proposal**, not the final API. It must receive owner review and may change
before independent tests are imported. No names below imply production code exists.

```csharp
ConstraintNormalization2Result ConstraintNormalizer2.Normalize(
    PlanarConstraintInput2 input, ConstraintMeshingOptions2 options = null,
    CancellationToken cancellationToken = default);
ConstraintNormalization2Result ConstraintNormalizer2.Normalize(
    NormalizedConstraintInput2 input, CancellationToken cancellationToken = default);
ConstraintTriangulation2Result ConstrainedDelaunayMesh2.Compute(
    NormalizedConstraintInput2 input, CancellationToken cancellationToken = default);

bool ConstraintRingInterior2.TryGetExactWitness(
    IReadOnlyList<ExactPlanarPoint2> ring, out ExactPlanarPoint2 witness);
bool ConstraintRingInterior2.TryConvertWitnessToInteriorDouble(
    IReadOnlyList<ExactPlanarPoint2> ring, ExactPlanarPoint2 witness, out Vector2d point);
```

- The first normalization overload snapshots input and options. The second takes the owned normalized
  object, retains its original input and resolved tolerance, and returns an equivalent verified snapshot;
  it must not interpret output edges as fresh source segments. The corresponding normalization result
  carries status, failure kind, diagnostics and an owned `NormalizedConstraintInput2`; on failure the
  normalized object is null. The two `Compute` entry points must produce equivalent results for the same
  source and options. The `input` overload does not change M1's point-set API.
- `ExactRational2` is a reduced numerator/positive-denominator value used for source parameters and
  tolerance certificates. `ExactPlanarPoint2` uses a reduced shared positive denominator. Read-only
  exact values, input snapshots and all collections are owned; invalid/default exact value semantics
  require an explicit review when signatures and constructors are frozen.
- The first `points.Count` vertex slots preserve each original input's exact coordinate, including aliases;
  `InputVertexMap[i]` identifies the surviving representative used by topology. Alias slots never occur
  in faces or edges. Constructed vertices follow those slots in canonical exact-coordinate order.
  `Vertices` is only a finite rounded display view, including original coordinates in alias slots.
- Proposed source key `ConstraintSource2` distinguishes an explicit segment index from a ring index and
  its directed edge index. `ConstraintSourceSpan2` carries a source key, exact `[Start, End]` parameters
  in the caller's original segment/edge direction, a disposition (retained, clipped or collapsed), and
  for retained spans a subsegment index plus direction. Every source's ordered spans cover `[0,1]` without
  undocumented gaps. A shared subsegment retains every covering source and associated marker; ring edge
  provenance is never disguised as an explicit segment index. The exact representation of a degenerate
  zero-length source and area clipping remains an explicit API/design review item.
- `ConstraintDiagnostic2` includes a kind and optional source, region index and exact problem coordinate;
  relevant categories include boundary seed, ignored/outside seed, clipping, collapse, unsupported
  construction and verification failure. Names, multiplicity and deterministic ordering are not yet
  approved. The witness helper returns false on a degenerate or unrepresentable case as appropriate and
  never returns an uncertified boundary double. Its behavior for a caller-supplied non-interior exact
  witness is an argument-validation question, not a silently assumed result.

## 7. Test-first acceptance and verification

Independent tests derive only from the behavioral spec and this pending interface, not production code.
The draft covers integer and rational crossings, overlapping markers/locks, no-noding failure, holes with
internal breaklines, repeated/reversed rings, region overlap, identity, ownership, reuse and cancellation.
Snapping acceptance remains gated on the final policy and displacement proof; no hidden tests will be
rewritten to fit a failed implementation.

The oracle uses its own rational/BigInteger determinants and area calculation. It checks positive exact
faces, reciprocal opposite-vertex neighbours, no T-junctions/crossings, domain area and every source chain,
not only Success. CDT legality is checked only on unconstrained interior edges with convex adjacent faces.
Cocircular free-edge ties use representative vertex IDs as in M1; generated node IDs come from a canonical
exact-coordinate ordering, not hash enumeration or intersection discovery order.

Required before implementation acceptance:

- Compile-red for the approved missing API, followed by real behavioral red; temporary scaffolds cannot
  remain in the repository or count as implementation.
- F-1/F-2/F-3/F-6/F-7/F-8/F-9, F-10–F-18 and F-43–F-50 mapped to scoped normalization/domain tests;
  refinement-specific angle/count claims remain M3/M4, not waived or claimed in M2.
- Planar projections of `Similarity.All()`, exact power-of-two scaling, translations/reflections and
  explicit-tolerance scaling; separately test the translation-sensitive automatic policy.
- Compute twice on one instance, preservation of earlier results and caller data, cancellation during
  snapshot/normalization/recovery, deterministic repeated runs and independent concurrent instances.
- Reversible mutations of intersection canonicalization, coverage, marker union, hole classification,
  unconstrained legality, identity and ownership.
- Independent construction/bounds review, bounded procedural A/B with equivalent output contracts or
  explicitly itemized differences, and local CI/package/API compatibility checks before push.

## 8. Remaining gates and current evidence

The behavioral decisions in sections 2–5 are owner-approved; they do not need to be requested again.
The remaining gates are:

1. **Closed: API direction and signatures approved by the owner on 2026-10-30.** The authoritative
   [contract](MESHING2D_M2_API.md) includes caller IDs/markers, seed-only holes, ordered regions, witnesses,
   ownership, provenance and pre-clipping normalization. This is not a geometry acceptance result.
2. Complete the bounded exact-construction/snapping spike: deterministic routing event order, termination,
   displacement and source-coverage certificates, canonical rational identities and cancellation checks.
   Owner approval of the policy does not replace these proofs or adversarial validation.
3. Independent acceptance integration is staged outside the repository. The original draft was retained
   byte-for-byte and new contract tests added. The staging build is compile-red (CS0246: missing M2 types),
   not a behavioral pass. Complete F-10–F-18 coverage and an independently identified contradictory
   outside-domain expectation remain open. The independent follow-up failed with provider HTTP 502.
4. Obtain an approved implementation brief before starting M2 production, followed by the independent
   bounds review, mutations and benchmark gates above. Input-height conflicts remain M3 work.

This document records approved behavior and pending API/spike work. It does not authorize M2 production
implementation, waive M1's outstanding review/CI status, or assert that the complete specification is met.

## 9. Isolated spike findings (2026-10-30)

The standalone C# spike is stored outside the repository in the session files, not in production or hidden
acceptance tests. The worker's final report was interrupted by a provider error; saved code and evidence
were recovered directly. Both allowed routing candidates were exercised. They attract surviving input
vertices using exact projections onto original source segments, then node the routed pieces exactly.
The second candidate additionally suppresses endpoint-attraction graph cycles. This is an experimental
rule, not an approved exception to mandatory normalization behavior.

The fresh-input fixed-point gate remains **failed for both candidates**. With a source from `(0,0)` to
`(4,0)`, tolerance `0.1`, and retained points `P=(2,0.09)` and `Q=(1,0.135)`, the first pass routes through
P. Q is outside the original source's tolerance tube but inside the tolerance tube of the routed first
piece. Treating that output as new source geometry attracts Q and changes the arrangement. All decimal
fixture literals are converted from their double bits to exact rationals before these comparisons.
Adding Q to the original source path would violate its original-source displacement certificate.
This falsifies these one-shot candidates; it does not prove that every global normalizer is impossible.

The spike's original-input repeatability check was previously labeled origin-preserving idempotence.
That label has been corrected: recomputing the same original input is determinism, not normalization of
an output object. Neither an owned provenance-preserving normalization operation nor a globally stable
fresh-input normalizer has been implemented. No idempotence gate has been waived.

For a piece with affine source parameterization, endpoint displacement bounds extend to the whole piece:
its error vector is the convex combination of the endpoint error vectors, so its norm is at most their
maximum. This is a conditional mathematical certificate for a supplied piece and parameter interval,
not a proof that the routing rule preserves all required constraints. Sampled interior checks supplement
but do not replace that argument.

Recovered full-run evidence is `bounded-investigation.log` / `results-bounded-investigation.json`:
the original run reports failing fresh-input fixed-point checks. The stacked-ring experiment used a
bounded analogue; a stopped larger run is not a completed F-14 result. There is no full F-10–F-18,
incircle, public-API, independent bounds-review or M2 acceptance claim.

Focused recovery commands were run from the following standalone directory; logs and JSON results
remain there:

```powershell
Set-Location 'C:\Users\Harmen\.copilot\session-state\944e7e52-a0da-4e46-a71c-957b8e9ce973\files\M2ConstructionSnapSpike'
dotnet run --project Spike.csproj -c Release -- gate-verification --filter=gate/
dotnet run --project Spike.csproj -c Release -- fixedpoint-recheck --filter=fresh-arrangement-fixed-point
dotnet run --project Spike.csproj -c Release -- thirds-green --filter=exact-thirds
dotnet run --project Spike.csproj -c Release -- thirds-mutated --filter=exact-thirds
dotnet run --project Spike.csproj -c Release -- thirds-restored --filter=exact-thirds
```

- `gate-verification.log`: exact counterexample/displacement checks passed, exit 0. These checks validate
  the obstruction, not successful normalization.
- `fixedpoint-recheck.log`: both candidate fixed-point checks failed, exit 1.
- `thirds-green.log`: exact thirds/reversed-pair construction passed, exit 0.
- `thirds-mutated.log`: temporarily changing the intersection parameter from `t` to `t + 1` caused that
  test to fail, exit 1. The mutation was removed.
- `thirds-restored.log`: restored construction passed, exit 0.

The owner subsequently selected the provenance-preserving contract in section 2. The fresh-input
counterexample remains relevant evidence, but reimporting routed edges as new sources is no longer the
idempotence criterion. This selection does not by itself validate a routing algorithm.

## 10. Provenance-preserving spike (2026-10-30)

A standalone experimental owned arrangement snapshots original exact points, source segments and tolerance,
plus alias mapping, exact nodes, source paths with parameter intervals, edges and the second candidate's
suppressed-event diagnostics. Its output-normalization overload validates that snapshot, recomputes from
its retained original sources, and compares the resulting geometry, mapping, source paths and diagnostics
before returning a separate owned snapshot. It does not reinterpret routed edges as input sources.
Read-only wrappers and deep copies protect the earlier output and caller arrays. This prototype is not a
public API, and it lacks ring and marker provenance, clipping and full diagnostics.

The scoped event termination argument is conditional on finite, successful BigInteger operations and
sufficient resources: candidate 2 enumerates a finite set of input survivors for each original source,
processes each selected source/survivor pair once in a deterministic order, and never adds another
attraction event. It then considers a finite set of routed pieces, their survivor cuts and unordered piece
pairs once; each pair produces finitely many exact cuts. Sorting and recomputation over that finite data
terminate under the same conditions. Cancellation is polled between operations, not inside individual
BigInteger operations. This argument is for the prototype only, not a general resource bound or proof of
its geometric correctness. Deterministic recomputation is a fixed point only while the original snapshot,
policy and implementation remain unchanged; production versioning and ownership remain API gates.

The F-15 fixture exposes a critical remaining issue: candidate 2 suppresses attraction of vertex B to
source A-C to prevent a dependency cycle, while retaining the shared A-C piece. Its own diagnostics
record the suppressed event. A passing local area/arrangement fixture does **not** establish that this
suppression meets every approved near-segment-routing requirement. The independent domain/CDT oracle,
complete F-10–F-18 coverage and bounds review remain open; M2 production remains blocked. Further
routing-policy design is required rather than silently accepting or dropping the suppressed event.

In the standalone spike directory from section 9, the following targeted commands were run:

```powershell
dotnet run --project Spike.csproj -c Release -- owned-red --filter=owned/
dotnet run --project Spike.csproj -c Release -- owned-diagnostic-red --filter=owned/cycle
dotnet run --project Spike.csproj -c Release -- owned-fresh-mutated --filter=owned/output-renormalization
dotnet run --project Spike.csproj -c Release -- owned-copy-mutated --filter=owned/deep-snapshot
dotnet run --project Spike.csproj -c Release -- owned-final --filter=owned/
```

- `owned-red.log`: compile-red before the owned normalizer existed, exit 1; not a behavioral test.
- `owned-diagnostic-red.log`: compile-red before suppression diagnostics existed, exit 1.
- `owned-fresh-mutated.log`: replacing original-source recomputation with fresh routed edges fails the
  output-renormalization test, exit 1; the mutation was removed.
- `owned-copy-mutated.log`: removing the source snapshot fails the ownership test, exit 1; the mutation
  was removed.
- `owned-final.log`: six targeted owned-output, snapshot, alias/overlap, similarity/concurrency,
  suppression and cancellation checks passed, exit 0. These use the prototype's own verifier and are
  not independent acceptance or evidence that unsupported production behaviors are correct.

## 11. F-15: explicit attraction without suppression (2026-10-30)

The existing first routing candidate was revisited; no third candidate was introduced. For F-15 it
routes the source A-B through C and A-C through B, without suppressing either attraction. The resulting
arrangement contains a small triangle and a shared B-C edge. Unlike candidate 2, the two constraints do
not share an outgoing edge at A. The specification requires a valid domain of area 4, not that additional
shared-edge condition; the experimental `candidate1/F15-common-piece` assertion is therefore a stricter,
non-specification hypothesis, not an acceptance criterion. Its failure must not be presented as an F-15
specification failure or silently removed from the experimental evidence.

A new test explicitly checks both source chains and constructs a separate procedural triangulation of the
surrounding square. Exact orientation, edge incidence, source-edge inclusion and the sum of exact face
areas are checked. This demonstrates that this particular constraint arrangement admits the constructed
frame triangulation; it does **not** run the M2 CDT algorithm, establish Delaunay legality, verify all
ring/domain semantics, or establish a general rule for mutually attracting inputs. The test first failed
against candidate 2 because it suppressed an attraction. The prototype now defaults to candidate 1, and
stores the chosen experimental candidate in each owned snapshot so recomputation preserves its policy.
Candidate 2 remains available only for comparative diagnostics, not as an approved fallback.

Commands run from the standalone directory named in section 9:

```powershell
dotnet run --project Spike.csproj -c Release -- f15-current --filter=F15
dotnet run --project Spike.csproj -c Release -- owned-f15-red --filter=owned/f15-no-suppression
dotnet run --project Spike.csproj -c Release -- owned-f15-green --filter=owned/f15-no-suppression
dotnet run --project Spike.csproj -c Release -- owned-f15-mutated --filter=owned/f15-no-suppression
dotnet run --project Spike.csproj -c Release -- owned-restored-candidate1 --filter=owned/
```

- `f15-current.log`: both candidates passed the local arrangement verifier; candidate 1 failed the extra
  common-edge hypothesis, while candidate 2 passed it. Exit 1 due to that extra assertion.
- `owned-f15-red.log`: candidate 2 failed the new no-suppression check, exit 1.
- `owned-f15-green.log`: candidate 1 passed the F-15 owned/frame test, exit 0. The test's first draft
  omitted one frame face; that test defect was corrected before this passing result.
- `owned-f15-mutated.log`: switching the default back to candidate 2 made the new test fail, exit 1;
  the mutation was removed.
- `owned-restored-candidate1.log`: all seven focused owned tests passed after restoration, exit 0.

The earlier bounded-run JSON and logs predate the final F-15 assertion in the recovered source and must
not be used as evidence of that assertion's current outcome; `f15-current.log` is the relevant rerun.
The complete normalizer still needs adversarial global routing/coverage tests, independent oracles,
construction and bounds review, and the remaining F-10–F-18 cases. The F-15 example does not authorize
production implementation.

## 12. Seeded adversarial and exact-boundary validation

The standalone spike now includes `AdversarialTests.cs`. Its oracle uses separately written squared-distance,
determinant and point-on-segment expressions, rather than calling Engine verification helpers. It checks
lowest surviving representatives and alias disks, original-source near-point inclusion, ordered parameter
coverage, endpoint displacement, retained source edges, complete noding, absence of proper crossings,
owned replay and source reversal. This remains an author's oracle, **not independent acceptance**.

The seeded family uses `Random(1234567)`, 24 arrangements, eight dyadic-grid points and four input-pair
segments per arrangement, with tolerance 1/8. Reversible mutation restricting attraction to collinear points
failed on case 0; restoration passed. Exact commands from the spike directory:

```powershell
dotnet run --project Spike.csproj -c Release -- seeded-snap-mutated --filter=adversarial/
dotnet run --project Spike.csproj -c Release -- seeded-snap-restored --filter=adversarial/
```

The first log reports `near input survivor omitted from original source route`, exit 1. The restored log
reports the seeded test passed and `RESULT assertions=1 failures=0`, exit 0. Raw logs and result JSON remain
in the persistent standalone spike directory; no mutation was retained.

A second test now exercises exact inclusive disk-boundary attraction, two equal-projection survivors on
opposite sides, a point outside the original source tube, a duplicate endpoint and collapsed source coverage
at dyadic exponents -1070, 0 and 230. The extreme fixtures are exact finite double-representable inputs.
Each is also independently checked by the oracle and re-normalized as an owned object.

```powershell
dotnet run --project Spike.csproj -c Release -- boundary-adversarial --filter=adversarial/
```

`boundary-adversarial.log` reports both registered adversarial tests passed,
`RESULT assertions=2 failures=0`, exit 0. The runner count is test actions, not the number of inner assertions
or geometries. This does not establish full F-10�F-18 acceptance, a CDT implementation, quality refinement,
or the full forty-copy F-14 family. The known fresh-input fixed-point counterexample remains applicable.

The detailed API proposal is now separate for owner review. No approval has been inferred from the broad
implementation request. The independent bounds review remains pending; the existing independent acceptance
draft writer failed, and the draft has not been read or adapted by the implementation author.

## 13. API approval, staged independent acceptance and review outage

On 2026-10-30 the owner explicitly approved all five API decision groups. The frozen contract is
[MESHING2D_M2_API.md](MESHING2D_M2_API.md); it supersedes the historical sketches in section 6.

Independent acceptance integration is staged in the persistent session directory `M2AcceptanceIntegration`.
The original draft was kept byte-identical; new frozen-contract assertions were added independently from
specification and API alone. No implementation sources were read by that acceptance integrator.
The staged project's report records this command from its own directory:

```powershell
dotnet build .\M2AcceptanceIntegration.csproj -c Release --no-restore -p:BuildProjectReferences=false --verbosity quiet
```

The result is compile-red with CS0246 for absent M2 types. It referenced the existing library artifact without
rebuilding production. This is not runtime acceptance and does not exclude additional compiler diagnostics
once real M2 types exist. Full F-10�F-18 coverage, in-operation cancellation and other report-listed gaps
remain open. A contradictory original outside-domain expectation was preserved and awaits independent
clarification; the implementation author has not read or modified those test sources.

The whole-spike bounds reviewer and the later narrowed construction-formula reviewer both failed with
provider HTTP 502 without a completed finding report. The acceptance integrator's follow-up clarification
also failed with HTTP 502. No independent bounds approval exists; a direct author check must not substitute
for it. There is no M2 production implementation, commit or push in this phase.

## 14. Direct default-exact regression (not an independent bounds review)

A direct follow-up found that standalone `default(Q)` carried a zero denominator and was not equal to its
canonical zero, while `default(P).Homogeneous()` would not provide the approved origin representation.
The regression `exact-default-value-contract` was written and run before changing the spike primitive.
It checks equality, hashing, homogeneous origin, arithmetic and comparison over dyadic exponents.

```powershell
dotnet run --project Spike.csproj -c Release -- exact-default-red --filter=exact-default
dotnet run --project Spike.csproj -c Release -- exact-default-green --filter=exact-default
dotnet run --project Spike.csproj -c Release -- exact-default-mutated --filter=exact-default
```

The baseline log reports `default rational is not canonical zero`, exit 1. The corrected run passes, exit 0.
Removing the default-denominator fallback reproduces the same failure, exit 1; that mutation was removed.
Only the standalone spike changed: its stored denominator now exposes 1 for the zero-initialized value.
Constructed reduced rationals keep their previous representation. This is not production API implementation.

Preservation validation rebuilt the restored spike for the first selector, then reused that same binary:

```powershell
$filters = @('exact-', 'dyadic-', 'homogeneous-', 'display-', 'automatic-tau-', 'owned/', 'adversarial/', 'policy-expectations')
$first = $true
foreach ($filter in $filters) {
    $tag = 'default-restored-' + ($filter -replace '[^a-zA-Z0-9]', '_')
    if ($first) {
        dotnet run --project Spike.csproj -c Release -- $tag "--filter=$filter"
        $first = $false
    } else {
        dotnet run --project Spike.csproj -c Release --no-build -- $tag "--filter=$filter"
    }
    if ($LASTEXITCODE -ne 0) { throw "Failed selector: $filter" }
}
```

Every selector exited 0. The `default-restored-*.log` files contain the exact results, including both
candidates' existing policy assertions. These overlapping selectors must not be summed as a distinct-test
count. Independent acceptance, the forty-copy F-14 family, CDT/domain meshing and independent bounds
approval remain unverified. The fresh-input fixed-point counterexample is not altered by this correction.
