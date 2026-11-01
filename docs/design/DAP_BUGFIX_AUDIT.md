# DAP fork: bug-fix audit and selective adoption

Status: audited on 2026-11-01 ([result](#audit-result-2026-11-01)): no DAP change is a bug fix that applies to this code, so nothing was adopted. Checked on 2026-10-30: `8f185f1` and `158507b` exist, and the root license is BSL-1.0.
Owner decision (2026-10-30): DAP is a fork of `gradientspace/geometry3Sharp`, not company code, and the audit may read its source. The
root license is BSL-1.0, the same as this repository and upstream. Files the fork added can have another origin, so provenance per file still applies.
The delta is 215 files (+18371/-694), so this is more than one PR.

## Task brief

- **Goal:** review the DAP fork's non-whitespace changes and adopt each demonstrated bug fix with a regression
  that fails on our implementation before the fix and passes afterwards.
- **Scope:** a per-change audit record, targeted existing library methods, procedural regression tests, and
  `docs/SOURCES.md`; compatibility notes in `docs/UPSTREAM_CHANGES.md` when behavior changes.
- **Non-goals:** wholesale merging, namespace/reformatting churn, new features before provenance clearance,
  project/package migration, mesh assets or unrelated refactoring.
- **Tier:** S for this plan; M per ordinary bug-fix batch. Exact arithmetic, bounds or `ExactMesh*` candidates
  are L and require a separately approved brief, bounds review and the existing mutation gates before changes.
- **Interface:** preserve upstream 1.0.324 compatibility; adapt the smallest semantic fix to current code rather
  than transplanting a whole DAP file or its API changes.
- **Budget:** triage the complete pinned delta once; implement one independently testable fault class per PR.
  Stop an individual candidate at an unresolved provenance, specification or reproduction question and record it.
- **Report:** candidate disposition, immutable source links, before/after test commands and output, own fix/PR,
  and independent review. No automatic merge.

## Verified source snapshot

- Repository: [Digital-Production-Aachen/DAPComputationalGeometry](https://github.com/Digital-Production-Aachen/DAPComputationalGeometry).
- Requested base `8f185f1` resolves to `8f185f19a96966237ef631d97da567f380e10b6b`.
- Source HEAD observed during planning: `158507b2d912cfcb5bff2b1f76eb9cac1171446c`.
- [LICENSE.txt at that HEAD](https://github.com/Digital-Production-Aachen/DAPComputationalGeometry/blob/158507b2d912cfcb5bff2b1f76eb9cac1171446c/LICENSE.txt)
  is Boost Software License 1.0. Preserve applicable copyright and license notices when porting.
- Only commit metadata, root file names and the root license were inspected for this plan. No DAP implementation
  diff was reviewed; no bug count, feature provenance clearance or test result is claimed.

The root license permits compatible adoption but is not proof that every added feature has compatible provenance.
Check individual file headers, originating commits and any referenced third-party implementation first. Unknown
origin means deferred, not implicitly Boost. Never inspect or port Triangle / Triangle.NET, the owner's existing
quadtree or company code; never use GPL/LGPL sources. Audit path names and provenance before opening new modules.

## Execution order

1. Create a separate read-only source checkout outside this repository, pinned to the source HEAD above. Record
   the destination's clean base SHA separately; ignore unrelated uncommitted work. Verify base ancestry in DAP.
   `HEAD` in the requested command means **DAP HEAD**, never this repository's HEAD.
2. Inventory paths/renames and classify metadata-only, formatting-only, candidate fixes, features and unknown
   provenance. Use the non-whitespace delta below as the review source; read it in bounded, approved path groups.
   A renamed or reformatted file is not by itself a bug fix. Also inspect a normal diff for whitespace-sensitive
   strings, generated data and preprocessing where `-w` could hide semantic changes.

   ```powershell
   # Run only from the separate DAP source checkout pinned above.
   git merge-base --is-ancestor 8f185f19a96966237ef631d97da567f380e10b6b HEAD
   git --no-pager diff --name-status -M 8f185f1 HEAD
   git --no-pager diff -w --stat 8f185f1 HEAD
   # For each provenance-approved path from the inventory:
   git --no-pager diff -w 8f185f1 HEAD -- <approved-path>
   ```

3. The delta has only 4 commits, and `a2a4b9f` is the initial commit of the DAP internal fork, so history gives no origin per fix:
   record the file-level commit, and mark the origin of a change as unknown where it is. Locate our corresponding method. Classify it as reproducible here, already fixed/equivalent, intentional behavior/API
   change, feature, not applicable, or blocked. Do not assume that every DAP change corrects a defect.
4. Write the smallest procedural test against the intended contract, not against DAP's output. Demonstrate an
   assertion failure on our pinned pre-fix code; build/tool failures do not count. Already-fixed candidates get
   their existing regression linked or a missing coverage test, but are not called newly adopted fixes.
5. Apply the minimal fix, run the same test green, then temporarily remove only that fix in an isolated validation
   checkout and rerun red. Never reset the user's working tree. Add invariance over `Similarity.All()`, reuse for
   `Compute()` state, serial/parallel repeatability where applicable, and actual success-result checks. Readers
   also join `ReaderFuzzTests` when their input behavior changes.
6. Add per-fix attribution to `docs/SOURCES.md`: source repository, full commit SHA/permalink, original path and
   symbol, license/provenance evidence, adaptation versus direct port, own affected methods and regression test.
   Cite DAP even when the implementation is adapted rather than copied. Include notices where required.
7. Review one coherent batch independently; run the repository's full pre-push CI and API/package checks before
   publication. Continue until every inventoried delta has a disposition, not until every DAP change is copied.

## Audit record and acceptance

Create the implementation audit ledger as part of the audit, not as a list of speculative bugs in this plan:

| Field | Required evidence |
|---|---|
| Candidate ID / source | Source path, hunk or symbol, the full SHA of the commit in the delta and immutable URL |
| Provenance | File-level license / origin evidence; restrictions or unresolved questions |
| Our counterpart | File/symbol and pinned pre-fix SHA, or explanation that it does not exist |
| Disposition | Reproduced, already fixed, intentional behavior, feature, not applicable or blocked, with reason |
| Regression | Test name, procedural input, failing assertion and exact pre-fix command/output |
| Adoption | Minimal patch, passing command/output, removal/mutation evidence, own commit/PR and SOURCES entry |

Acceptance: every path in the pinned delta is accounted for, every adopted fix has a genuine red/green
regression and attribution, and no feature or uncleared source slips into a bug-fix PR. Features remain a separate
backlog with per-feature provenance decisions; audit completion is not feature approval. If our code is already
correct, do not invent a failing expectation merely to satisfy the red-test rule.

No tests, dependencies or production files are changed by this planning task.

## Audit result (2026-11-01)

Delta: `8f185f19a96966237ef631d97da567f380e10b6b` to `a2a4b9f` (initial commit of the DAP fork), compared with
`git diff -w`. The three later commits up to `158507b` change only `2dGeometry/Polygon2dExtensions.cs`, the
`.csproj` and `LICENSE` (`git diff --name-status 158507b a2a4b9f`), all DAP additions. Of the 215 paths, 139 are
modified upstream files and 7 are deleted ones (build files, `AssemblyInfo`, and the moves of `comp_geom/ConvexHull2.cs`
to `2dGeometry/` and `mesh_ops/MeshPlaneCut.cs` to `3dGeometry/`, diffed against their new paths). The rest are new
files (features, below). Our counterpart: master `b1d9dee`.

**Result: no bug fix to adopt.** Every hunk in the modified upstream files is one of:

| Class | Examples |
|---|---|
| Formatting | removed `(double)` / `(float)` casts (checked for int/float promotion changes: none), brace style, `ref` arguments dropped |
| Refactor, same results | struct enumerators in `RefCountVector` / `SmallListSet`, `Duplicate()` instead of copy constructors, `ParallelStream` on `BlockingCollection`, span helpers |
| API change (breaks upstream 1.0.324) | `ref` to `in` and merged overloads in `Vector3d`, `MathUtil`, `AxisAlignedBox2d/3d`, `Line2d`, `Box3`; `ConvexHull2` constructor and `Get1DHullInfo`; `DVector.FastGetBuffer` and `SimpleMesh.Get*Buffer` pointer overloads replaced by `Span<T>`; `PolyLine2d.Vertices` / `GeneralPolygon2d.Holes` types; `PolyLine2d.Length` removed; `SVGWriter.Style` struct to class; namespaces `gs` to `g3`; `PackedSparseMatrix.nonzero` renamed |
| Behavior change, not a fix | `ConvexHull2` rewritten with an absolute epsilon (scale dependent, breaks `Similarity.All()` invariance) and a fake triangle for degenerate input; `ContMinBox2` ignores `queryType`; `Polygon2d.Contains(Vector2d)` returns false on a vertex; `Polygon2d.DistanceSquared` numbers segment `i` as (i-1, i), no longer consistent with `PointAt`; `Polygon2d.Simplify` returns early for 3 vertices and clamps the tolerance to a quarter of the smaller bounds side; bounds and signed area cached on `Timestamp`; `MarchingCubes` default sphere radius 1; `MeshTransforms.FromFrame(Frame3f)` computes in double; SVG output wrapped in `<g id="default">` |
| Feature | see below |

Audited by area, each against our code: `curve/Polygon2d.cs`; `mesh/DMesh3*.cs` and `core/`; the rest of `curve/`, `io/`,
`implicit/`; `math/`, `distance/`, `intersection/`, `solvers/`, `approximation/`, `color/`, `containment/`, `comp_geom/`;
the rest of `mesh/`, `mesh_generators/`, `mesh_ops/`, `queries/`, `spatial/`. The owner-reserved files the delta touches
(`mesh/Reducer.cs`, `containment/ContMinCircle2.cs`, `mesh/RemesherPro.cs`) have only formatting changes, so nothing
goes to the owner.

### Defects the fork has and we do not

Do not take these over when a DAP feature is ported later:

| DAP file / symbol | Defect | Our code |
|---|---|---|
| `math/MathUtil.cs` `AspectRatio(Vector3d, Vector3d, Vector3d)` | calls itself (the by-value overload wins over the new `in` one): stack overflow, also from `Triangle3d.AspectRatio` | calls the `ref` overload |
| `core/DVector.cs` `FastGetBuffer(DVector<T>, Span<T>)` | `AsSpan(iCurBlockUsed)` copies the unused tail of the last block instead of `AsSpan(0, iCurBlockUsed)`: wrong data or an exception | original pointer version |
| `curve/PolyLine3d.cs` `Bounds` / `GetBounds()` | cache stamp starts equal to `Timestamp`, so a new polyline returns an empty box | computes the box |
| `curve/PolyLine2d.cs` `Bounds` | `VertexOffset`, `Trim*`, `Transform` and span writes do not increment `Timestamp`: stale bounds | computes the box |
| `core/gParallel.cs` `ParallelStream` | an exception in the producer never calls `CompleteAdding`: the consumer waits forever | flags, `finally`, exception rethrown |
| `math/Line2.cs` `IntersectionPoint(in Line2d, double)` | ignores its `dotThresh` | no such overload |
| New `ImplicitSphere3d.Contains`, `Circle2d.Contains(Circle2d)`, `AxisAlignedBox2d.ContainsInclusive` | inverted sign; bounding-box test only; `<=` and `<` mixed | not present |

Bugs of upstream that the fork still has and we fixed earlier: `ConvexHull2` step `31337` (our `ModuloIterationStep`,
`ForkFixesTests.ModuloIteration_VisitsEveryIndex`), `GridIndexing` negative exact multiples (our `floor_div`),
`MeshPlaneCut.FillHoles` inverted `bAllOk`, the rounded-rectangle corner parameter in `PlaneGenerators`.

### Features (not adopted; each needs its own provenance decision)

| DAP path | Provenance |
|---|---|
| `2dGeometry/Clipper2Wrapper.cs` (polygon booleans, offsets, aggregation by distance), `mesh_ops/ECCSlicer.cs`, `Protobuf/` | DAP; depend on Clipper2 (Boost-1.0, use the NuGet package, ROADMAP item 3) and OpenVectorFormat. ECCSlicer implements doi:10.1007/s00170-015-7071-9 |
| `2dGeometry/ApproximateConvexDecomposition/` | header: ported from [jmlien/acd2d](https://github.com/jmlien/acd2d) "with a major rework and bug fixes". acd2d is MIT (`LICENSE`, Jyh-Ming Lien 2018); its repository also ships FIST binaries and gl2ps (LGPL, GUI only), which the port does not need. Compatible, attribution required |
| `FortuneVoronoi/` | no source note; origin unknown, blocked |
| `2dGeometry/LargestInscribedCircleFortune.cs` | marked `[Experimental("IS_STILL_BUGGY")]`; builds on FortuneVoronoi; blocked |
| `2dGeometry/ConvexPolygonOps.cs` | DAP; Minkowski sum "adapted from cp-algorithms.com" (CC BY-SA 4.0 text; check before a port) |
| `HierarchicalSpatialGrid/`, `Graphs/`, `Collections/`, `2dGeometry/` (others), `3DShadowSlicing/`, `3dGeometry/`, `BoxExtensions/`, `Utils/`, `io/SVGImporter.cs`, `io/SVGExportExtensions.cs` | DAP, no third-party note |
| `supports/*`, `Quaterniond.InverseMultiply` | "ported from SutroMachine/geometry3Sharp by @gregmeess"; check that fork's license |
| `DMesh3.RemoveEdge`, `CleanupUnusedVertices`, `IsValidEdge` | "ported from pshtif/geometry3Sharp"; check that fork's license. `IsValidEdge` checks only `t0`, not the refcount |
| DAP-specific `DMesh3` members (`MeshType`, `Color`, `PartName`, `ComputeShadow`, `GetOffsetMesh`, ...) | DAP; pull in DAP dependencies, not in upstream's scope |
