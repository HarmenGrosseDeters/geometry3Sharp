# Independent, hidden acceptance tests for quadtree and meshing

Status: **independent author sessions started; real-API binding and product execution pending**.
The owner approved starting from pinned master specifications with test-owned contracts on 2026-10-30.
Highest-priority follow-up while implementation is already in progress. This implements [AGENT_WORKFLOW §5](../AGENT_WORKFLOW.md#5-hidden-acceptance-tests-tier-l)
and the hidden-test item in [QA §4a](../QA.md#4a-making-agent-work-verifiable).

## 1. Separation and delivery contract

Tier S for this planning document; the acceptance work belongs to the tier-L quadtree and meshing projects.
It is not permission to modify production code, exact arithmetic or bounds, existing acceptance tests, mutation
infrastructure, or the owner's specifications. Two **fresh Copilot author sessions**, one per module, were
started after the owner's explicit request. They must not inherit implementation conversations or prior test results.

Three roles remain separate:

1. **Coordinator/owner:** freezes the behavioral contract, resolves genuine ambiguities, prepares the author input
   package and records its provenance. May see implementation status but must not feed its failure cases, patches,
   tuning results or existing tests into test generation.
2. **Acceptance author:** sees only the approved normative specification and public interface contract; writes
   procedural inputs, independent oracles, tests and benchmark driver. Does not inspect the implementation,
   implementation tests, implementation PRs, logs, repository history or internal symbols.
3. **Black-box executor/integrator:** builds the implementation separately, provides its binary/public reference
   surface and runs the frozen tests. It may supply a mechanical public-API adapter, but may not alter inputs,
   assertions, tolerances, expected statuses, sample sizes or benchmark thresholds to fit the implementation.

A Git worktree of the implementation repository is **not** adequate isolation: it exposes the source and history.
Use separate spec-only workspaces/test-only repositories, outside implementation checkouts, with an explicit read
allowlist. This is a workflow boundary, not an OS security sandbox; if the execution environment cannot keep the
implementation out of the author's accessible context, use an owner-managed separate environment instead.
Do not put hidden test sources in the shared implementation branch while test authorship is in progress.

### Frozen author input package

- Repository-wide process and license rules, and this brief's scope/stop rules, without implementation paths or
  suggestions for particular fixes.
- Quadtree: only the normative preamble and **§§1-8** of `QUADTREE2D_SPEC.md`. **Exclude §9 and everything after it**:
  the live document appends implementation/verification records that would contaminate independent authorship.
- Meshing: the normative preamble, feature scope, F catalogue, non-functional requirements and **§5 owner decisions**
  of `MESHING2D_SPEC.md`; exclude any future implementation record or owner-only F-number-to-fix mapping.
- Record original specification path, committed base SHA, SHA-256 of the actual source snapshot (including approved
  uncommitted specification edits), extracted sections, and hash of the delivered package. The owner approves the
  exact package before sessions start; a moving working-tree file or branch name is not the contract.
- Public signatures/status definitions and referenced value-type declarations only, independently approved as a
  contract. Quadtree §8 already supplies much of this. The meshing spec defines behavior, not a complete C# API:
  obtain an approved interface-only contract, **not** source files, before binding its tests.
- If meshing signatures are not settled, authors can write test-owned abstract contract fixtures and oracles;
  API binding and executable acceptance remain explicitly blocked, not passed or silently skipped.

After authorship, freeze test sources, fixture generators, project dependencies, seeds, oracle version and
benchmark configuration by commit/hash **before the first run against the implementation**. The author alone
owns substantive test revisions; an owner-approved spec clarification creates a new version with a rationale.
The implementing session receives results and sufficient reproducer information for fixes, but does not edit
acceptance tests. Preserve the original failure and test version when a clarification supersedes a case.

No Triangle / Triangle.NET, owner's existing quadtree, company source or GPL/LGPL implementation may be inspected.
Procedural data only in the test repository; no committed mesh assets. The old quadtree is available only to the
owner for the later black-box side-by-side benchmark, never to the author or implementer as source.

## 2. Quadtree author brief

**Goal:** independent behavioral acceptance of the approved core and adapters, plus a reproducible benchmark for
§4.2. **Scope:** test-only project, procedural data generator, brute-force oracle, caller-side helper for §2.3,
benchmark driver and machine-readable case manifest. No production or existing test edits.

**Contract precedence:** §7 decisions and §8 override historical behavior in §§1-3. In particular, extent grows,
empty insertion works, tolerance is absolute Euclidean XY distance, exact height hits take precedence, proximity
is complete, and domain-specific edge policies belong in the caller's helper layer.

| Case | Acceptance and independent oracle |
|---|---|
| §2.3 worked example | Generate A-F exactly from the spec. Check every documented query/option, identity or endpoint provenance where specified, and height-source/snap behavior. General index APIs supply candidates; a test-owned caller helper implements domain-specific relevant-edge/z policies. Verify that helper against explicit expected results and an independent exhaustive geometric reference. Do not require nonexistent production helper APIs. |
| D-1 | Integer and half-integer grid borders, index boundaries and negative/UTM offsets; exact closed-AABB result sets against a full scan, including border-only contact. |
| D-2 | Test successful growth and insertion into an empty index. For genuinely invalid insertion (non-finite/inverted input under §8), verify unchanged count, extent, contents and next ID. Inserting outside the previous extent must **not** be rejected. |
| D-3 | Segment point/projected queries with a candidate that is not a true hit and a later true hit; stop only on a true hit. Use permutations and oracle result sets, not knowledge of tree traversal order. Ensure the controlled faulty early-stop adapter is detected. |
| D-4 | A traversal callback launches nested queries on the same thread/index with distinct caller buffers. Compare inner and outer outcomes with independent serial scans; also concurrent queries. |
| D-5 | Warmed-up hot queries with preallocated, sufficiently large result buffers and struct callbacks: zero managed bytes on ordinary inputs; index-size sweep must not hide growing allocations. Separate setup/JIT/result-buffer growth and worker-thread allocation accounting. Robust extreme/near-degenerate fallbacks are a separate correctness campaign; §8 explicitly permits allocation there. |
| D-6 | Procedural long thin triangles across center, quarter, integer and power-of-two lines; tiny rectangle queries at both ends agree with closed-AABB brute force. |
| D-7 | Remove large spanning items twice, by ID and value where applicable; second removal changes nothing, other IDs remain stable. Duplicates remain distinct, and value removal removes the first live matching snapshot. |

Complete the acceptance envelope with snapshots independent of later caller edits, duplicate/degenerate data,
invalid query arguments, clearing reused result lists, nearest/k-nearest including distance bounds and ID ties,
exact-first height with affine extrapolation for tolerance hits, and far-away eligible proximity hits. Compare
sets with multiplicity/identity as required, never accidentally deduplicate distinct objects by coordinates.

Use independent interval/segment geometry and analytically known heights; if exact signs are needed, use a
small test-owned rational/BigInteger oracle over finite binary64 inputs rather than the production predicate.
Run applicable 2D similarity transformations (XY rotations/translations and scaled tolerances); do not tilt the
XY plane and assume height/proximity semantics stay unchanged. Check power-of-two cases exactly, non-exact
transforms with an explicit coordinate-resolution bound. Reuse and serial/parallel cases are mandatory.

### §4.2 benchmark, not a noisy unit-test timeout

Reproduce the specified distribution: 708 by 708 cells, spacing 7.07, origin (2539000, 5640000), interior XY
jitter ±0.15 cell, unjittered border, z in [0,100), both prescribed triangles per cell, shuffled; seed 1.
Generate 10000 uniform query origins, anchored 20 by 20 rectangles and segments with XY offsets uniform ±100.
Record PRNG/version/seed; the spec requires the distribution, not the old implementation's exact random stream.

Measure bulk build, height, rectangle and segment queries, index-only memory, and allocated bytes. The spec's
**owner-machine comparison targets**, not measured results of this plan, are build about 100-260 ms, height
about 2.5 microseconds, rectangle about 5.7 microseconds, segment about 21 microseconds, and index-only memory
about 67 MB. Keep input storage and harness memory separate and state how adapter snapshots are accounted for.
Use BenchmarkDotNet as required by §5, with a pinned permissively licensed benchmark-only dependency if needed;
no dependency or tooling is installed during planning.

Run correctness/oracle checks outside the timed loop. Record runtime, CPU, core count, GC settings, warmup,
iteration distribution and raw output with the exact command. Validate allocation on net48 and .NET 8+ using
an appropriate runtime-supported counter/profiler; inability to measure is **unavailable**, never zero. The
repository's net10 runner can be used locally, but the owner's requested net48/net8 comparison remains explicit.
A local brute-force comparison validates correctness and provides context, not a claim to match the old index.
Final side-by-side runs happen on the owner's machine with the restricted implementation as a black box; report
unavailable if it or the hardware is absent. Never turn these historical timings into machine-independent CI gates.

**Stop/deliver:** freeze the complete D/§2.3 test matrix and benchmark before implementation execution. Return
project/artifact hashes, test discovery manifest, blocked contract questions and standalone run instructions.
No push to the implementation branch, merge, production repair or performance tuning by the author.

## 3. Meshing author brief

**Goal:** every F-1 through F-66 has an explicit scope disposition, and every first-version behavioral obligation
has procedural acceptance coverage. **Scope:** a separate test-only project, fixture generators, independent
geometric oracles, process-isolated runner and case manifest. No implementation/predicate changes or existing
regression-test inspection.

**Contract precedence:** §5 wins over historical expectations. Noding failure is a failure **status**, except
invalid arguments/cancellation; ring-based holes remove interior segments; input-index/output mapping follows
Q8; regional labels/areas and size functions are in scope. No algorithm selector, arbitrary refinement callback,
maximum-angle option, finished-mesh smoothing, Voronoi, file formats or bandwidth renumbering in version one.

The following is a planning disposition, not a coverage or pass claim. The author expands each range into
individual F-number records with spec citation, parameters, expected status/geometry, test names and evidence.

| F IDs | First-version treatment |
|---|---|
| F-1–F-8 | Direct procedural crossing, noding, duplicate, spike and refinement cases. |
| F-9 | Adapt the historical exception to Q10 failure-status semantics; never accept invalid geometry as success. |
| F-10–F-19 | Direct procedural snap/noise/wedge/hole cases under the approved tolerance contract. |
| F-20 | Procedural dense-polyline/tiny-angle crossing family; named external issue-211 fixture is separate provenance-gated coverage, not reproduced merely by a similar generator. Clarify any missing exact acceptance parameters. |
| F-21 | Procedural parallel stripes; correctness test plus scaling/performance campaign, not an unconditional one-second CI assertion. |
| F-22–F-26 | Direct snap, area, regional and locked-segment cases; clarify conflicting expectations rather than weakening validity/quality checks. |
| F-27 | Needle correctness and bounded-resource run; historical timing belongs in a separately reported benchmark. |
| F-28–F-29 | Deferred: maximum-angle option excluded by Q3. Do not expose or emulate it just to satisfy old tests. |
| F-30–F-31 | Retain invalid-min-angle, area and splitting-policy obligations; remove only the excluded maximum-angle variants. |
| F-32–F-34 | Extreme lengths and per-triangle/area-only refinement; separate F-34 timing from correctness. |
| F-35 | Convex-hull preservation and configuration reuse; use approved area/size-function settings, not the excluded arbitrary callback. |
| F-36–F-40 | Direct locked-breakline, termination, tiny-angle, high-degree and capped high-min-angle cases. |
| F-41 | Retain thin-rectangle validity/area with supported options; original max-angle clause is deferred. |
| F-42 | Resource-bound requirement is in scope, but "certain inputs" is insufficient as a fixture. Ask for an observable procedural case and memory budget; no invented equivalent or vacuous pass. |
| F-43–F-50 | Direct holes/regions/interior-point cases; F-46 follows ring semantics from Q7, not the old acknowledged limitation. |
| F-51–F-52 | Separate supported extreme magnitudes from required argument rejection above the documented limit and non-finite input. Obtain an exact limit if the public contract still says only "about 1e70". |
| F-53 | Procedural near-degenerate predicate families with an independent exact oracle; the named published fixture corpus needs its own licensed provenance and is not certified by substitutes. |
| F-54–F-58 | Procedural hard predicates, large coordinates, cocircularity, identical points and concurrency. One approved algorithm, no old algorithm-selection matrix. |
| F-59 | Q8 input-index identity, new IDs and snap mapping; preserve caller IDs as metadata only if specified by the approved API. Clarify ambiguities instead of assuming old ID semantics. |
| F-60–F-62 | Independent inputs/results, supported public point equality/hash semantics including signed zero, and point location on shared edges. Bind only to approved public contracts. |
| F-63 | Deferred to TerrainGreedyTIN under Q5; track the transfer, do not silently omit or claim meshing coverage. |
| F-64 | Finished-mesh smoothing/Voronoi cases deferred under Q1/Q4. Retained Steiner-smoothing validity/reuse/cancellation obligations need their own procedural tests; these do not claim full F-64 coverage. |
| F-65 | Direct output-grid tests: positive orientations after rounding, segment recovery and the specified near-segment quality exception. |
| F-66 | Procedural archetypes for tangent holes, spikes, noisy borders and other described shapes; exact named public fixtures remain blocked until licensed inputs or sufficient procedural descriptions are approved. No copied mesh assets, GPL sources or assertion that archetypes reproduce the original corpus. |

### Oracles and execution

Every success path checks the actual mesh: finite coordinates, strictly positive exact orientation, conforming
edge adjacency, no T-junctions/overlaps/gaps, correct domain coverage, and recovered input-segment chains.
Area alone is insufficient: equal missing and overlapping areas can cancel. Use analytic domains/independent
polygon membership and intersection checks; account for approved snapping displacement with a justified bound.
Do not use the unrefined implementation output as the sole reference for refined area or carried z.

Check constrained versus conforming Delaunay appropriately (visibility/constraint exceptions versus global empty
circumcircles); use an independent exact determinant oracle over binary64/rational coordinates. Check holes,
region labels/areas, locked segments (except permitted crossing splits), all guaranteed quality bounds, input
mapping, and z against the fixed input reference surface where supplied. Exceptions for input-forced small angles
or Steiner limits must be justified by the fixture and explicit result flags, not by accepting every bad triangle.
Verify that the Steiner cap counts all created points, not only survivors, when observable through the contract;
if no public evidence exposes this, request a contract clarification rather than private implementation access.

Run applicable similarities with dimensional parameters transformed correctly, bit-exact power-of-two cases,
repeated/reused input and instances, and independent parallel runs. Freeze order-independent fingerprints of
oriented triangles/subsegments; never use IDs alone as the geometry oracle. Use deterministic PRNGs, fixed seeds
and reproducible failure descriptions. No all-success status check without geometry validation.

Crash/hang/resource cases execute in child processes with bounded time/resources and explicit cancellation;
record the command, exit code, timeout and last F-ID. Terminate only the process tree launched by that case.
Keep workload sizes and limits in the frozen manifest. Historical performance numbers are targets with provenance,
not portable CI deadlines. A crash, timeout, missing API or unavailable runtime cannot be counted as passed.

**Stop/deliver:** all F IDs have a disposition and every supported variant has fixtures/oracles or an explicit
owner-visible blocker. Produce the test-only artifact, per-F discovery manifest, hashes and runner instructions;
freeze before integration. No production fixes, silent skips or test relaxation by the implementation session.

## 4. Scheduling, gates and evidence

1. Owner approves the spec-only packages and interface contracts. The quadtree package must stop before §9;
   the meshing package must include the owner decisions. Resolve genuine normative contradictions first.
2. Start fresh quadtree and meshing author sessions separately; neither reads the other's implementation work.
   Quadtree can reach execution sooner because §8 supplies its public API. Meshing may proceed with behavioral
   fixtures while an interface-only binding is being approved.
3. Author validates generator/oracle/harness behavior with hand-computable cases and deliberately faulty
   **test-owned** stand-ins (dropped border hit, premature stop, wrong status, inverted face, missing constraint).
   Record which assertion detects each fault. This proves non-vacuity without exposing production internals.
4. Freeze both acceptance artifacts, then have a separate executor bind and run them on net48 and net10, with
   net8 where required for the owner performance comparison. Preserve raw logs and every failing procedural case.
   Same implementation binary and test-artifact hashes accompany every report.
5. Report states distinctly: passed, failed, blocked by contract, deferred by owner scope, and unavailable.
   Implementation failures go back as result/reproducer reports; fixes require rerunning the unchanged suite.
   The implementing author may not edit frozen assertions to make its implementation pass.
6. Independent review checks spec coverage, oracle independence and performance methodology. Production bounds
   review remains a separate implementation gate; passing black-box tests cannot prove every pruning/error bound.
7. Owner controls eventual publication of the test sources and integration into regular CI. Before any authorized
   push, run the applicable local CI steps from `CLAUDE.md`; never auto-merge or publish hidden artifacts early.

## 5. Authoring kickoff (2026-10-30)

The owner explicitly approved starting now from current master, with test-owned contracts and real API binding
later. Source baseline: `origin/master` at `93880ed97a282f356ae96aea465e539130bea69b`.
The original checkout and its pending changes were left untouched by workspace preparation.

| Author package | Delivered normative content | SHA-256 of SPEC.md |
|---|---|---|
| Quadtree | Master preamble and sections 1-7 | `bb1b9d0c5768a526e1c3fb19d6921dbed9cbf422d71e009bf1aecf35f24f058e` |
| Meshing | Master preamble and sections 1-5 | `34489c1a4d2fc6f58b36d19a33661a36a425eeab982bcad328fd0355e4963f6f` |

**Contract update:** the normative quadtree section 8 seen during planning was not present on the fetched master.
It was not reconstructed from memory or supplied from another implementation workspace. Consequently both
authors now work against the master behavioral decisions and test-owned adapter contracts. Earlier references
in this plan to section 8 describe intended future binding, not content delivered or approved for this kickoff.
Missing public signatures and any unspecified behavior remain visible blockers until separately approved.
The quadtree master snapshot contains no section 9 or implementation record.

Each author has a separate newly initialized, source-free Git workspace containing only SPEC.md, SNAPSHOT.json
and TASK_BRIEF.md at launch. There is no implementation history or remote. The input hashes above and the
approved read boundaries were verified before the author sessions started. Authors must not read the parent
repository, existing tests, other sessions or implementation reports. Only their own workspace/session artifacts
and required SDK/tool/package metadata are allowed. No product source or binary has been delivered.

Both Copilot author sessions were confirmed in progress. They are to generate fixtures/oracles and prove their
test machinery using controlled test-only faults, then freeze the artifacts before black-box integration.
No implementation test or benchmark has run as part of this kickoff; no product-acceptance claim is made.
Next gate: collect authored artifacts and contract questions, approve the interface-only binding, then run the
frozen tests in a separate executor.
