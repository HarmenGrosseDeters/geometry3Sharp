# Boolean metamorphic and property tests

Status: planned, not implemented. Implements the Boolean part of [QA §4a, items 9 and 10](../QA.md#4a-making-agent-work-verifiable).

## Task brief

- **Goal:** detect Boolean regressions through algebraic and representation-invariance properties, with reproducible,
  automatically shrunk procedural counterexamples.
- **Tier:** S for this documentation; M for test-only implementation and independent review. No production exact
  arithmetic, numerical bounds or Boolean implementation changes are authorized.
- **Scope:** new Boolean tests and narrowly scoped helpers under `tests/geometry3Sharp.Tests`, its project file,
  and the directly related QA, roadmap and source documentation.
- **Non-goals:** changes to `mesh_ops/ExactMesh*`, `math/Exact*`, production APIs, mutation tooling or CI workflows;
  terrain and 2D meshing properties; arbitrary invalid/open triangle soups; imported mesh files.
- **Interface:** existing public Boolean operations and result/status fields. Use fresh input meshes for each
  execution, derived from immutable, printable case descriptions.
- **Sources:** CsCheck's published documentation and package metadata; existing repository test helpers. No GPL,
  Triangle / Triangle.NET, company code or owner's quadtree sources.
- **Operation under test:** `ExactMeshBoolean`, not `MeshBoolean` or `MeshWindingNumberBoolean`. Inputs outside its contract
  are in `docs/OPERATIONS.md` and `ExactBooleanByDesignTests`.
- **Stop rule:** if a property exposes a production defect, preserve the smallest reproduced procedural case and
  report it as separate work. Do not repair exact code, discard the case, relax the property or silently skip it.
  CI must stay green, so the case is pinned as a `KnownLimitation_` test by the repository's idiom. Until the owner
  decides otherwise, that is the recording.
- **Report:** test-only PRs, one per step: analytic oracle and first properties; CsCheck with shrinking and replay (pin a
  version: 5.0.0 is current, Apache-2.0); conforming subdivision and non-axis cases. Each with commands and raw test output, shrink/replay evidence, and an independent review
  against `docs/REVIEW_CHECKLIST.md`. Do not merge automatically.

## Dependency and framework decision

Recommended configuration: run CsCheck generation and shrinking on **net10.0 only**. Keep deterministic regression
cases and dependency-free oracle/representation tests runnable on net48 as well. This retains compatibility
coverage without forcing the property campaign onto an old CsCheck release.

CsCheck is **Apache-2.0, not MIT** according to its [license](https://github.com/AnthonyLloyd/CsCheck/blob/master/LICENSE)
and [NuGet 3.0.0 metadata](https://api.nuget.org/v3/catalog0/data/2023.11.12.18.21.39/cscheck.3.0.0.json).
Apache-2.0 is allowed by this repository. A test-only reference does not change the library's Boost license or
need to become a dependency of the shipped geometry3Sharp package. Preserve applicable license/notice obligations
if distributing CsCheck itself. Record the selected release and its license in `docs/SOURCES.md` when adding it.

Pin a published version compatible with net10.0 after checking that release's metadata and replay API; do not
follow `master` or a floating version. The 3.0.0 release notes identify 2.14.1 as the last netstandard2.0 release;
there is no reason to select it if generation runs only on net10.0. Condition both the `PackageReference`
(`PrivateAssets="all"`) and compilation of the CsCheck-dependent file on net10.0. Do not hide other tests behind
that condition. Verify restore, test discovery and the package dependency list.

## 1. Generators: small valid solids, shrinkable parameters

Start with outward-oriented, closed boxes with bounded integer/dyadic coordinates. Encode a case as primitive
parameters, not a random seed that constructs an opaque mesh. Positive extents, bounded coordinates and valid
orientation must survive every shrink; avoid rejection-heavy filtering and shrinking by deleting arbitrary faces.

Use separate case families for disjoint, identical, strictly nested, overlapping, coplanar-overlapping and
face/edge/vertex-touching solids. Each family has deterministic examples and its own generated campaign, so
rare cases cannot disappear by chance. Contact means regularized solid semantics: an intersection that is only
a face, edge or point is empty. Disconnected manifold sheets are allowed at contact.

Generate representation changes separately: operand selection (A, B or both), vertex permutation, triangle
permutation, cyclic corner rotation and a bounded subdivision level. Preserve the relation family while
shrinking geometry. Reduce transformation complexity as well as coordinates and extents. Include a deliberately
nontrivial transformation in each fixed representation test.

After the analytic box campaign is working, add bounded non-axis-aligned cases (small procedural tetrahedra and
transformed boxes). Check these with the metamorphic checks below, clearly distinguishing their numerical
surface checks from the stronger analytic box oracle. Do not add high-resolution spheres or unbounded soups.

## 2. Oracles and properties

Every Boolean execution checks `Compute()` and its documented status, non-null/finite/valid output, and the
appropriate manifold/closed and self-intersection checks. Empty meshes are a separate valid result: existing
`ResultChecks.AssertClosed` cannot be called on them. A failed computation is a failing property, not a discard.

| Property | Executions and assertions |
|---|---|
| Inclusion-exclusion | `vol(Union(A,B)) + vol(Intersection(A,B)) = vol(A) + vol(B)`; compare each box result with independently computed analytic volume as well. |
| Partition reconstruction | Actually compute `Union(Difference(A,B), Intersection(A,B))` and compare its solid and boundary with A; a volume sum alone is insufficient. |
| Commutativity | Compare `Union(A,B)` with `Union(B,A)` and `Intersection(A,B)` with `Intersection(B,A)`. Difference is not commutative. |
| Representation invariance | For Union, Intersection and Difference, compare baseline with each transformed operand selection. Triangle IDs, vertex IDs, triangle count and diagonal choice need not match. |

Use `Invariance.Volume` for translated inputs. Derive numerical tolerances from coordinate resolution, scale,
area and accumulation error; document their units and test the oracle on deliberately incorrect results.
Do not use a fixed world-unit epsilon or a tolerance chosen merely to make a failure pass.

For boxes, build an independent reference from the sorted input coordinate planes: occupied cells follow simple
interval membership and the operation's truth table. Their exposed, oriented rectangles define the boundary.
Check analytic volume and boundary coverage (projected triangle/rectangle coverage, missing/extra/duplicated
patches and orientation), independent of the Boolean under test. Handle empty output before accessing bounds.
Test this helper independently on hand-built good and bad meshes. Do not use another Boolean as the sole oracle.

For non-axis-aligned cases, combine centered signed volume, surface area, oriented surface probes in both
directions (vertices and triangle-interior samples), and validity checks. This is a bounded numerical check,
not proof of surface equality. `Invariance.GeometricDifference` compares triangle sets and therefore cannot be
the general oracle when triangulations differ; `Invariance.VertexDistance` alone can miss interior differences.

## 3. Representation transformations

- Reuse `Invariance.Permuted` for combined permutations; add test-local separated variants so a failure identifies
  triangle order, vertex IDs or cyclic corner rotation. Never reverse winding by swapping two triangle corners.
- Subdivide each triangle into four with shared edge midpoints, reusing one midpoint per undirected edge. Rebuild
  the entire mesh per level to avoid T-junctions. Dyadic input and bounded levels keep midpoints representable.
- Check each transformation before running a Boolean: same oriented surface and volume, finite coordinates,
  valid closed mesh, and an actual representation change. Check subdivision against an unsplit procedural oracle.
- Run the fixed corpus over `Similarity.All()`. For exactly scaled dyadic cases, mapped-back results must satisfy
  the existing bit-exact power-of-two invariant; other similarities use resolution-aware comparisons.
- Keep existing reuse/determinism tests. Add a small generated-case replay through reuse and serial/parallel
  execution rather than multiplying the whole property campaign by every mode. Keep CsCheck sampling single-worker for deterministic shrinking/replay.

## 4. Shrinking, replay and runtime budget

Use CsCheck `Gen` composition and `Sample`; assertions must run inside the property so failures are shrunk.
Build fresh meshes from each candidate. Emit property name, operation, case family, full invariant-culture
parameters, transform, package version, original seed and reported minimized replay seed. Seeds alone are not
stable across generator/package changes, so preserve the parameter description too.

Use an explicit finite sample count and seed for the normal test suite; do not use a wall-clock campaign as the
CI pass criterion. Choose the count after measuring the targeted suite and record the command/output in the PR.
The targeted suite should add no more than about one minute to a full run on a normal machine.
Document a larger opt-in local campaign and a single-property/single-case replay command using the pinned
version's supported API. Bound mesh size and shrinking work; report an exhausted budget, not a globally minimal
counterexample. CsCheck shrinking does not guarantee a global minimum within a finite run.

Prove this integration with a controlled test-local faulty result (for example a dropped boundary patch or wrong
operation): the property must fail, shrink to a simpler still-valid descriptor and reproduce from that descriptor
and the reported seed. Catch the expected failure in the harness self-test so the committed suite remains green.
Also verify that the oracle rejects equal-volume but geometrically different solids and reversed/missing faces.
Do not modify production exact code or `tests/mutation` to demonstrate sensitivity.

## 5. Implementation order and acceptance

1. Add the conditional dependency and immutable descriptors; verify net10.0 discovery and net48 compilation.
2. Add/test the analytic reference, result checks, permutation and conforming subdivision helpers.
3. Add the algebraic properties, then representation properties, with fixed and generated case families.
4. Add shrinking/replay self-tests, similarities and bounded non-axis-aligned/reuse/determinism coverage.
5. Measure the default campaign, document replay and dependency provenance, and obtain independent review.

Acceptance: every property and family above is exercised; all intermediate results are validated; injected faults
are detected and shrunk; replay is reproducible; no production files change; no mesh assets are added; net48
retains deterministic regressions; package dependencies and upstream 1.0.324 API compatibility remain unchanged.
A test-only addition is not expected to fail on correct old code: controlled faulty results provide the required
non-vacuity evidence instead. A newly discovered real defect requires separate authorization to fix.

Proposed targeted validation (commands are a plan, not executed results):

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~BooleanProperty|FullyQualifiedName~BooleanMetamorphic"
dotnet test tests\geometry3Sharp.Tests -c Release -f net48 --filter "FullyQualifiedName~BooleanMetamorphic"
```

Before any push, run every local CI step in `CLAUDE.md`, including both suites, package/API validation and smoke
test, net48 build and all benchmark builds. Record unrelated baseline failures separately; never label a red
run as passing. No test execution or dependency installation is needed for this planning-only change.
