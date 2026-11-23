# Input order and the exact boolean with rounding: diagnosis and options (2026-11-23)

Diagnosis of a dependence on the order of the input, observed by the owner on private inputs: on the same geometry,
reversing the order of the input triangles turned two Differences of nearly coincident curved plates from `Success` into
`BooleanFailed / RoundingNotClean`, and changed the volume of a thin curved strip (an Intersection) in the fourth significant
digit. **No library change**: tests that pin the behaviour, a measurement harness, and options for the owner.

**Status (2026-11-23):** the owner chose A with A2 and D; they are built in the same PR (#1464), see section 8. Sections 1 to 7
describe the library before that change (master 083ef07b), and `--numbered` is the emulation that was measured before the
build.

- Harness: [benchmarks/BooleanInputOrder](../../benchmarks/BooleanInputOrder/README.md), raw outputs in its `results/`
  (master 083ef07b, 4-core cloud container, .NET 10.0.115).
- Tests: `tests/geometry3Sharp.Tests/ExactBooleanInputOrderTests.cs` (56 cases after #1464, each under 5 s; 11 when the note was written).

## Answer in short

- **The exact result does not depend on the order.** The kept pieces of the arrangement are the same triangles in every
  order (396 of 396 cases, 8 orders). The exact result after the simplification of flat regions is the same in 1019 of
  1020 cases; in one, `ExactPlanarSimplify` triangulates a flat region differently for other vertex ids (the same exact
  surface).
- **The first stage that diverges is the rounding repair** (`MeshRoundingRepair`). It gets the same rounded triangles and
  finds the same defects, but it works through them by ids, and its edits are not confluent: another order of the same
  edits ends in another mesh. The default-mode result differs between the orders in 889 of 1020 cases.
- **Both observations are reproduced.** The volume differs by more than 1e-4 relative in 103 of 396 cases of the first
  family (more than 1e-2 in 44; in 3 the result is empty in some orders only). The pipeline's status differs between the
  orders in 2 of 624 cases of the second family (`Success` in 4 orders, `RoundingNotClean` in 2: one degenerate triangle
  left).
- **The pipeline adds nothing of its own**: without a reduction target and without automatic steps its status is the
  repair's verdict under `StrictRounding`.
- **Recommendation** (section 6): number the exact result by its geometry before the repair (option A, with the small
  fix A2 of the simplification), and document the contract (D). It makes result and status a function of the geometry and
  costs under 1 % of the run time; it does not make more runs succeed. The failing orders point at a gap of the repair
  (section 4.3), which is a task of its own.

## 1. Method

Inputs (generated, no mesh files): a curved plate is the surface z = h(x, y) over the unit square on an n by n grid, with
a thickness t along z and side walls; h is a paraboloid ("bowl"), a product of sines ("wave") or a spherical cap of radius
2 ("dome").

| Family | Cases | What |
|---|---|---|
| `plates` | 396 | Grids 6, 10, 14; t = 0.02, 0.002; the plate minus a copy that is shrunk about its centre, turned, turned and shifted (each by 1e-4, 1e-6, 1e-8, 1e-10) or tessellated on the grid n + 1 or n + 2 (Difference: skins between nearly coincident surfaces), and the plate intersected with a turned copy lifted by nearly t (Intersection: a thin curved strip, 72 cases) |
| `tessellations` | 624 | Grids 5 to 8 against n + 1 and n + 2, t = 0.02, the finer one turned by 1e-5 to 1e-8 in 13 steps, as A − B and B − A |

Orders: as given, the triangles reversed, the vertex ids reversed, both reversed, then seeded shuffles of both (8 orders
for `plates`, 6 for `tessellations`). Both inputs are reordered the same way; the corners of each triangle are kept.

Compared per order:

| Stage | How |
|---|---|
| Arrangement | the result without `SafeRounding` and without `SimplifyFlatRegions`: the kept pieces, as a set of triangles by position (bits), whatever their ids |
| Exact result | the result without `SafeRounding`, the same way (the vertices are the exact points rounded, a function of the point) |
| Repair | the default mode: `Status`, `RoundingClean`, the set of triangles, the volume (summed in an order that does not depend on ids), and the counts of `MeshRoundingRepair` |
| Pipeline | `SolidBooleanPipeline`: `Status`, `BooleanStatus`, `Report.Route` |

Commands (the totals below are the last lines of their outputs, `results/plates_2026-11-23.txt` and
`results/tessellations_2026-11-23.txt`):

```
dotnet run --project benchmarks/BooleanInputOrder -c Release -- plates --orders 8 --stages --numbered
dotnet run --project benchmarks/BooleanInputOrder -c Release -- tessellations --orders 6 --numbered
```

## 2. Measurement

| | `plates` (8 orders) | `tessellations` (6 orders) |
|---|---|---|
| Cases | 396 | 624 |
| Kept pieces differ between orders | 0 | not run |
| Exact result differs between orders | 1 | 0 |
| Default-mode result differs between orders | 274 | 615 |
| Volume differs by more than 1e-4 relative | 103 | 0 |
| Volume differs by more than 1e-2 relative | 44 | 0 |
| Result empty in some orders only | 3 | 0 |
| `RoundingClean` differs between orders | 0 | 2 |
| Pipeline status differs between orders | 0 | 2 |
| Pipeline fails in every order (`RoundingNotClean`) | 9 | 69 (and 3 more succeed with thin components left, `BooleanStatus` `RoundingNotClean`) |

- The cases with one result for all orders are those whose rounded result has no defect (the repair does nothing), or
  whose few defects leave no choice.
- Volume: the spread is large where the result is about as thin as the repair's bound (`RoundingMaxDisplacement` 1e-5 of
  the diagonal), so that each contraction moves a visible part of it. Of the 72 strip cases, 44 differ between orders, with
  a median spread of 1.5e-5 and a largest of 7.5e-2; of the Differences 230 differ, median 1.8e-6. In the `tessellations`
  family the result is a solid with thin skins on it and the spread stays below 1e-4. Every one of these results is clean
  and within the repair's bound of the exact result: the spread is what the bound allows.
- Empty in some orders only: a Difference whose exact result (volume 3.7e-11) is one thin component. In 5 of 8 orders the
  repair ends with it removed as thin, in 3 it stays (`bowl n10 t0.02 rotshift 1E-10 D`). Both count as clean.
- Status: `dome n6 grid+2 rot 1.78E-08 Drev` and the same with n8. The triangle order decides (as given: failure; reversed:
  success); the vertex ids alone change nothing there. The pipeline's opt-in `Grid` step runs in the failing orders and
  fails again.

## 3. Where the outputs diverge

### 3.1 Arrangement and classification: no dependence found

The kept pieces are the same triangles in all 3168 runs of `plates`. The exact surface was known to be independent of
the order (the fingerprints of [EXACT_BOOLEAN_FUZZ_2026-11.md](EXACT_BOOLEAN_FUZZ_2026-11.md), section 2.3); this adds that
the triangulation of the pieces is, on these inputs.

### 3.2 `ExactPlanarSimplify`: rare, vertex ids only

`wave n10 t0.02 rot 1E-06 D`: the same pieces, the same 63 removed points, the same number of triangles, the same exact
surface, but two different triangulations over the 8 orders; reversing the vertex ids switches between them, reversing the
triangles does not. Cause, read from the code:

- The points are removed in a geometric order (`ComparePoints`, exact), which is fine.
- The hole of a removed point is a polygon that starts at a corner of the point's first piece (`fan`, `start =
  at(list[0], v).b`, `ExactPlanarSimplify.cs:250`); `ear_clip` cuts the first ear it finds from there.
- The flips towards Delaunay are made only where the floating-point filter is certain ("nearly cocircular cases are left
  as they are"), so on points on a circle (a regular grid) the ear clipping's choice stays.

It changes the triangulation of a flat region, not the solid. It matters for option A: the repair can only give one
result if it is handed one exact result (the one case with "several results" in section 5).

### 3.3 `MeshRoundingRepair`: the first diverging stage in general

The repair gets the same triangles with other ids: `assemble` takes the pieces in the order of the input triangles
(`ExactMeshBoolean.cs:5832`) and numbers the vertices in the order of their first corner (`:5878`). The repair on **one**
mesh with only its ids reversed gives another mesh (`Repair_OfOneMesh_DependsOnItsNumbering_Today`), so the dependence is
in the repair, not in what the boolean hands it. Where ids enter:

| Choice | Where | Decided by |
|---|---|---|
| Order of the degenerate triangles | `repair_passes`, `bad.Sort` by area (`:921`) | an unstable sort of a list in triangle-id order: equal areas (exactly collinear corners have area 0) in an order of ids |
| Order of the intersecting pairs, of the position search, of the contacts | `intersecting_pairs`, `position_search`, `false_contacts` | triangle ids, smallest vertex id |
| Which edge of a defect is contracted first | `collapse_any`, `edges.Sort` by length (`:2960`) | ties by the triangle's edge order |
| **Which end of a contracted edge is kept** | `collapse_any` (`:2965`): `try_collapse(a, b)` before `(b, a)` | `GetEdgeV`: the vertex ids; only a fixed (input) vertex has priority |
| Which edge is flipped or zipped, which corner moves | `fix_degenerate` (`Array.Sort(len, order)`, `:2992`), `fix_intersection`, `search_corners` | corner order, and the order of the pair |
| Direction of a move towards the neighbours | centroid of `VtxVerticesItr` | a floating-point sum in neighbour order |
| Everything after the first edit | passes over a changing mesh; the effort limits | the sequence so far |

The edits are local and greedy, and they are not confluent: contracting edge e first can make the contraction of a
neighbouring edge illegal (link condition, a new degenerate triangle, the drift bound) or unnecessary. So the order decides
the set of edits, not only their sequence. The counts show it: the same case ends with 53 or 55 contractions depending on
the order.

### 3.4 The pipeline: no dependence of its own

Without a reduction target, `SolidBooleanPipeline` runs the boolean with `StrictRounding`: its status is `Success` exactly
where the repair is clean (or leaves only thin components); the default configuration has no automatic step that could
take another route. With `AutomaticSteps = Grid` the retry runs in exactly the failing orders of the two cases of section 2
(checked by hand on those two; the harness does not compare routes). The reduction (a target triangle count) was not
measured: it has work lists of its own and is expected to depend on ids as well.

## 4. The three effects

### 4.1 Volume in the fourth digit

The kept end of a contracted edge is chosen by vertex id. For a skin or strip whose thickness is near the bound, moving a
vertex by the length of a contracted edge changes the local thickness by a large fraction; over some tens of contractions
the volumes of two orders differ by 1e-5 to 1e-2 relative. Both results are within the stated bound.

### 4.2 Thin components that stay or go

A thin closed component is removed if it keeps a defect or the edits changed its volume by more than a factor 2 (#1216).
Whether its defects are repaired, and by how much its volume changes, follows from 4.1. So a component can stay in one
order and go in another, and where it is resolvable (`RemovedResolvable`) the verdict can differ too. Measured: the result
empty in some orders only in 3 cases; a verdict that differs for this reason was not observed.

### 4.3 `Success` against `RoundingNotClean`

In the failing orders one degenerate triangle is left: edges 2.6e-8, 2.0e-7 and 2.1e-7 at a diagonal of 1.42, far below
the bound of 1.42e-5, two of its corners input vertices. `SmallestRejectedDrift` is infinite: no edit was rejected by the
bound, every edit was refused for another reason (the link condition, a new defect, the fixed ends). In the other orders an
earlier contraction happens to remove the configuration. So the status flip is the visible side of a **gap of the repair**:
a defect of 1e-8 that it cannot remove from some states. An order cannot fix that; it only decides whether the state is
reached. The 81 cases whose repair is not clean in any order show the same thing at a larger scale (72 of 624 in
`tessellations`; in those looked at, a removed component that is resolvable).

## 5. Building blocks and prior art

**In the library.**
- `ExactMeshArrangement.ComparePoints`: an exact total order of the arrangement's points (filtered), already used for the
  removal order of `ExactPlanarSimplify` because point ids depend on the thread schedule.
- `assemble` numbers the vertices as classes of corner slots "in the order of their smallest slots"
  (`ExactMeshEdgeGroups.NumberClasses`): if the kept pieces are sorted by a geometric key first, the vertex ids follow,
  duplicated contact vertices included, with no second mechanism.
- The determinism work ([DETERMINISM_PROOF.md](../design/DETERMINISM_PROOF.md), `ExactBooleanDeterminismTests`) made the
  result independent of the thread schedule the same way: replace an order by ids with one by geometry or by slots.
- [TET_TIE_ORDER_2026-11.md](TET_TIE_ORDER_2026-11.md): the same question for the tet improver (the outcome depends on the
  order of equal candidates; a restart under another order was proposed there).
- [METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md) and both bug hunts list permutation differences of other operations as
  documented limits; none promises permutation identity today.

**How others solve it** (search of 2026-11-23; the abstracts and summaries were read, the full texts of the papers were
not reachable from the session, so statements beyond the abstracts are marked):
- *Snap rounding* (Guibas and Marimont 1998; Hobby 1999; in 3D Devillers, Lazard and Lenhart, SoCG 2018): the rounded
  output is defined by the input geometry (the hot pixels or voxels), not by a sequence of edits, so it cannot depend on
  an order. The price in 3D is the complexity (O(n^5) under the paper's assumptions); see
  [SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](SNAP_ROUNDING_3D_GUARANTEE_2026-11.md).
- *Milenkovic and Sacks 2019* (the method the repair follows): a series of mesh edits, contractions for short edges and
  flips for skinny triangles, then an optimization. An iterative method of this kind has no canonical result; whether the
  paper fixes an order was not verified.
- *Devillers and Teillaud 2011* (Computational Geometry 44): a symbolic perturbation that makes the Delaunay triangulation
  uniquely defined for degenerate (cospherical) input, implemented in CGAL. It is the standard answer to ties of a
  triangulation (section 3.2). From memory, not verified today: it orders by the points' coordinates, where the simulation
  of simplicity of Edelsbrunner and Mücke 1990 orders by index and so depends on the input order.
- *Spatial sorting* (BRIO, Amenta, Choi and Rote 2003; `CGAL::spatial_sort`): incremental algorithms reorder their input by
  a geometric key first, for speed; a side effect is that the input order matters less. Geogram has no canonical
  renumbering ([GEOGRAM_SURVEY_2026-11.md](GEOGRAM_SURVEY_2026-11.md), "Determinism").

So the two established routes are a rounding that is defined by geometry (snap rounding: a different algorithm) and a
canonical order by a geometric key in front of an order-dependent algorithm (option A).

## 6. Options

Upper bound of option A, measured (`--numbered`): the repair, configured as the pipeline runs it, on the exact result
numbered by its geometry (vertices sorted by position, those at one position by their triangles; triangles sorted). The
emulation is the library's repair: without the numbering it gives the library's verdict and mesh in all 6912 runs.

| | `plates` | `tessellations` |
|---|---|---|
| Cases with one result over all orders | 395 of 396 | 624 of 624 |
| The one with several | the case of section 3.2 (two exact results) | |
| Vertices the geometry does not tell apart | 0 | 0 |
| Clean in every order today → clean | 386 | 549 |
| Clean in every order today → not clean | 0 | 1 |
| Clean in some orders today → not clean | 0 | 2 |
| Not clean in any order today → not clean | 9 | 72 |
| A sort of the vertices and triangles by position, share of the boolean's time | 0.80 % | 0.81 % |

| Option | What | Removes the dependence | Cost | Output changes |
|---|---|---|---|---|
| **A** | In `assemble`: sort the kept pieces by a geometric key (points ranked by `ComparePoints`, each piece rotated to its smallest point) before the vertices are numbered | Yes for the boolean and the pipeline, together with A2 (measured: one result in 1019 of 1020 cases, the last one needs A2) | Time: under 1 % (the sort above; `ComparePoints` instead of doubles costs more per comparison, to be measured). Code: small, but in `ExactMeshBoolean` (exact-code review, mutation run) | The ids of every result change once (vertex and triangle order). Results without rounding defects keep their triangles. Results the repair edits become another valid result. Status: 3 of 1020 cases go from clean (in all or some orders) to not clean, none the other way: the numbering is one more order, not a better one |
| **A2** | `ExactPlanarSimplify.fan`: start the hole at its smallest link point (`ComparePoints`) | The dependence of section 3.2 (expected; to be verified by the test of this PR turning green) | At most one comparison per link point of a removed point. Exact code: review and mutation run | The diagonals of some flat regions with points on a circle; the same solid |
| **B** | Replace every choice of the repair by a geometric key with complete ties (the table of 3.3) | Yes, also for `MeshRoundingRepair` used directly | At least 12 places in a 3650-line class with nine mutation shards (a full run takes hours); the floating-point sums over neighbours and the iteration orders of `DMesh3` are easy to miss; a geometric comparison per choice in the inner loops | As A for the boolean |
| **C** | On a result that is not clean, repeat the repair from the exact result under another numbering | No (it uses the dependence) | A second repair on failures only | 2 of 74 failing cases of `tessellations` and 0 of 9 of `plates` succeed in some order: at most 2 % of the failures |
| **D** | Document the contract: for one input the result is deterministic; the exact result does not depend on the order; the default-mode mesh, its volume within the bound, and the status do | No | None | None |

**Recommendation.**
1. **A with A2**, as one PR after the owner's decision: result and status become a function of the geometry, at under 1 %
   of the time. What the owner pays is a one-time change of every baseline that compares ids or edited results, and the
   exact-code review. The implementation PR measures the cost with `ComparePoints` and lists the tests whose pinned
   numbers move; the `_Today` tests of this PR become the invariance tests.
2. **D in any case**, in the guide of the boolean, because A covers relabelings only: another triangulation of the same
   surface, a translation or a rotation is another input, and the mesh after the repair can differ as before.
3. **Not B** (A gives the same for the boolean at a fraction of the cost and risk; `MeshRoundingRepair` used directly
   stays dependent on the ids of the mesh it is given, which is documented with D). **Not C** (2 % of the failures).
4. **Separate from the order**: the degenerate triangle of section 4.3 that no edit removes is a defect of the repair with
   a small reproducer (`TwoTessellations` in the tests, in the order as given). It deserves an issue with a diagnosis of
   which rule refuses each edit; fixing it removes failures, which no order does.

## 7. Limits of this note

- Two procedural families of one kind (plates on grids); the owner's inputs were not available. The mechanisms do not
  depend on the family, the rates do.
- Corner rotations of the input triangles and the reduction stage of the pipeline were not measured.
- Option A was measured as an emulation outside the library (sorting rounded positions, with the repair's internal hooks
  set by reflection). It is checked against the library's repair run by run, but the cost of the exact comparison and the
  effect on the existing tests are for the implementation PR.
- A2 is derived from the code and one case; that it is sufficient is expected, not shown.

## 8. What was built (#1464, owner decision 2026-11-23)

**The change.**
- `ExactMeshBoolean.order_pieces` (called in `assemble` before the simplification): each kept piece is rotated to start at
  its smallest point and the pieces are sorted by their three points; the points are ranked by
  `ExactMeshArrangement.SortPoints`, which gives the order of `ComparePoints` (a sort of the filter's double keys, then an
  insertion sort by the exact comparison). Everything after it follows the pieces: the vertex and triangle ids, the splits
  where the result touches itself, the copies of vertices at edges with more than two triangles, and every choice the
  repair makes by id.
- `ExactPlanarSimplify.fan` starts the hole of a removed point at its smallest link point (A2).
- **A2 was not enough**, and the note's section 3.2 was incomplete: the flips towards Delaunay called `InCircleFiltered`,
  which returns 0 where its filter does not decide. The filter reads the stored double approximation of a point and its
  error, and those depend on how the arrangement computed the point, which follows the input's ids. So the same four
  points could be flipped for one order and left for another. The flips now use `InCircle` (the filter first, the exact
  sign where it does not decide): strictly inside flips, exactly on the circle stays. The determinism proof listed the
  filtered call as "returns the exact sign" (`DeterministicApis.txt`, assumption S2-3); that is true now.

- **The arrangement itself depended on the triangle order for coplanar faces**, which section 3.1 missed because the plates
  have none (found by the independent review of the PR). Two segments with the same ends in one input triangle, eg the common
  diagonal of two coplanar triangles of the other mesh, are reduced to the one of the lower triangle index, and the
  constraint was inserted in the direction of that segment's line; the two lines point opposite ways. With points on a
  common circle the constrained triangulation followed the direction: 3 of 240 random pairs of grid boxes gave other pieces
  for reversed triangles (without `SimplifyFlatRegions`). `triangulate_touched` now inserts every constraint from its
  smaller end (`ComparePoints`); 400 such pairs, with and without the simplification, are the same in all 8 orders.

- **The inside-out check read an input-dependent triangle.** `count_inside_out` shoots its ray at one triangle per
  component, and the triangle was the first by input order; for a self-intersecting input the parity of the crossings
  depends on the triangle. `inside_out_triangle` now picks it by position: the smallest corner (`ComparePoints`) of the
  triangles with an area, then among the triangles at that point the one whose later corners are smallest. A permutation test
  of a self-intersecting input (`InsideOutCheck_OfASelfIntersectingInput_IsTheSame_WhateverTheInputOrder`) kills the old rule.

**Result** (`ExactBooleanInputOrderTests`, 56 cases): the same mesh bit for bit, vertex, triangle and edge ids included, and
the same status over 8 orders (triangle order, vertex ids, corner rotation) for the boolean with and without `SafeRounding`
and for the pipeline; on the plates, on boxes with coplanar gridded faces, on boxes touching along an edge and at a vertex,
on three inputs and on an open input, with and without the simplification; on nine near-coincident and tangent inputs
(`NearCoincidentAndTangentInputs_AreBitIdentical_WhateverTheInputOrder`: the cases of the repair's own tests); under
`Similarity.All()`; the 2^k scales give the scaled mesh bit for bit. Hand mutants killed: no ordering, no corner rotation, the hole's start at the first triangle,
the filter-only flips, the constraint's direction, and in `SortPoints` no insertion and no fallback.

**Time** (`BooleanInputOrder timing 5`: 8 procedural cases of 4k to 58k result triangles, median of 5 runs, three
alternating runs of before and after; `results/timing_*`):

| | before | after |
|---|---|---|
| Sum of the medians, three runs | 3784, 3471, 3467 ms | 3643, 3680, 3529 ms |
| The ordering (`assembly.order_us`) | | 37.8, 38.8, 36.6 ms: 1.04 % of the total, 0.5 to 3.7 % per case |
| The simplification, with the exact flips | 175, 185, 162 ms | 196, 209, 165 ms |

The totals differ by less than the run-to-run spread. The first version of the ordering (comparison sorts with a delegate)
took 2.6 % overall and up to 9.5 % on one case; the key sorts above replaced it.

**Clean results on the degenerate-family corpus** (`BooleanInputOrder corpus`: Cluster, FoldedCluster, Collinear and Sliver
at 7 sizes, 3 densities, 6 seeds, in three frames; 1458 runs; `results/corpus_*`):

| Frame | Default mode clean, before → after | `StrictRounding` succeeds, before → after |
|---|---|---|
| identity | 460 → 460 of 486 | 463 → 461 |
| rotated | 444 → 439 | 448 → 445 |
| rotated, scale 1e-3, UTM | 399 → 388 | 401 → 400 |
| all | 1303 → 1287 | 1312 → 1306 |

- The UTM row counts one configuration about 12 times: with seed 0 and every fourth triangle the pokes are below the
  resolution for every size, so those rows are the same mesh. There the repair ends clean by its own verdict in both
  versions; after the change `NotCleanReasons` reports self-intersections at vertices the rounding put on one position (the
  coarse-coordinate case of the class comment), where before a vertex removal cleared them.
- Two other orders of the pieces, tried with a throw-away patch (the geometric order reversed, and a fixed shuffle of it):
  1290 and 1293 clean, 1300 and 1297 strict. So on this corpus the generators' own order is the best of the four by
  about 1 %, and the three others lie within 0.5 % of each other. The repair's limits were tuned on these generators in
  that order; no order is better in general. A canonical order makes the outcome deterministic, not better.

**Existing tests that moved** (40; all re-pinned, each with a comment that names #1464):

| Kind | Tests | Change |
|---|---|---|
| Hashes of results, or of reductions that start from a boolean's mesh | `ExactMeshBooleanTestsLever4` (9), `ExactBooleanRadialOrderTests` (2, 44 prints; the counts beside the hashes are unchanged), `ReducerPairedContactsTests` (2), `SolidBooleanPairContactsTests` (2) | new hashes |
| Cases picked to show one behaviour of the repair | `MeshRoundingRepairTestsPositionSearch` (8), `SafeRoundingBoundTests.StrictRounding_FailsWithTheReason`, `MeshRoundingRepairIncrementalTests`, `MeshRoundingRepairTests` (the cube pair whose rounding self-intersects), `SafeRoundingLastComponentTests` | a neighbouring seed or size with the same behaviour; the rejected-drift case was checked against its mutant |
| The cube beside the box of 1024 | `MeshRoundingRepairTestsThinSolids` (4), `BooleanCertificateTests` | at 2^-11 it goes as `StillDefective` (before: `VolumeChanged`); 2^-10 where the test is about the changed volume; the factor-2 borderline is the cube of 5/2048 (0.54 times its volume, kept) |
| Worse | `SafeRoundingBoundTests.Utm_CleanWithTheUlpTolerance` 108 → 106 of 114 by default (102 → 104 with the relative bound only); `RoundingDenseTessellationTests` drift 0.5 → 0.77 of the bound, still clean; `TetMeshLocalAlphaTests` deviation factor 1.1 → 1.103; `SolidBooleanPipelineThinSolidTests` one volume within 1e-8 instead of 1e-9 | thresholds follow |
| Better | `ReferencePartsTests`: the screw stays at the ratios 1e-5 and 7e-6 (the limit was between 2e-5 and 1e-5, now between 7e-6 and 5e-6); the nightly census of known "not clean" rows 301 → 285; a flattened sliver is no longer turned inside out; the `tessellations` case of section 2 succeeds in every order | pins follow |
| Other | `ExactBooleanOpenInputTests`: 2 instead of 5 split bowtie vertices in one open-input union (which vertices get copies at an edge with more than two triangles follows the order of the pieces); `ReducerEndpointFallbackTests`: another box size (its inequality was never a rule) | pins follow |

**Open from the review:** the factor-2 test brackets the upper side of the factor only by the sheet at 3.1 times its volume
(no cube that ends just under 2 was found under the new order); an input whose components are reoriented by area is not
covered (the guide says so).

**Not changed:** `MeshRoundingRepair` used directly still depends on the ids of its mesh (pinned by
`Repair_OfOneMesh_DependsOnItsNumbering`); the operand order, the pipeline's input repair and its reduction are not covered
(the guide lists the limits). The degenerate triangle of section 4.3 is #1465: after the change the reproducer succeeds in
every order, which hides the gap there and does not fix it.

