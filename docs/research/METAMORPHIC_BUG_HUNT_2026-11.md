# Metamorphic bug hunt over chains of operations (2026-11)

Status: research spike (tier S: this note and one benchmark harness, no library change), 2026-11-22, on master `dcc8e5ff`.
Harness: [benchmarks/MetamorphicBugHunt](../../benchmarks/MetamorphicBugHunt/README.md); raw output of the run described here:
`benchmarks/MetamorphicBugHunt/results/master_dcc8e5ff.txt` (Linux x64, net10.0, 4 cores).
Builds on [METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md) (which relations find which fault classes, measured on the closed
issues) and [design/METAMORPHIC_RELATIONS.md](../design/METAMORPHIC_RELATIONS.md) (the catalogue). Those notes test single
operations and ask which relation would have caught a known bug. This one applies the relations on purpose and broadly, to
**chains** of operations, and checks every Success with code that shares nothing with the operation.

**Question.** Does the library report a wrong result as success, or lose geometry without a report, when its operations are
chained (Boolean → repair → remesh → tet, the wrap, contouring, the 2D mesher) on procedural inputs?

## 1. Result in five lines

1. **One defect, filed as #1337**: `MeshRobustRepair` returns success for an input that is already clean and changes its solid.
   The box `[-1, 1]^3` with one edge split 0.002 from a corner (14 triangles, clean) loses that corner (volume 8 →
   7.998666666666668); next to a far clean box the same mesh passes through untouched. Three relations fire on it: "repair of a
   clean mesh is the identity", "repair is idempotent" and "a far part does not change the result of a part" (section 4).
2. **Everything else holds**: 5157 checks, 15 failures, all of them the one defect; no call above 20 s. Per stage in section 3.
3. `ExactMeshBoolean` and the chains built on it did not produce one wrong solid: 24 random chains of up to six Booleans on
   rounded results agree with the composition of the original solids' winding numbers at every step and are bit for bit the
   same at 2^10.
4. Several checks fired first and were **expectations of the harness, not defects** (section 5): each is a documented limit
   (contacts, a reduction without a deviation bound, the remesher without constraints, the reducer without a quality bound, holes
   in the quad-dominant mesh, the wrap closing cavities). They are listed so that the next hunt does not repeat them.
5. The yield dropped after the second hour, as [METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md) predicted for the areas with
   campaigns and oracles (section 7: what is left, and where a further hunt could still pay).

## 2. Method

**Inputs** (`Shapes.cs`): boxes (12 triangles and gridded), spheres (normalized cube), a torus, star and n-gon prisms, cylinders,
tetrahedra, all rotated and placed procedurally; defects made from them (removed, flipped, unwelded, duplicated triangles,
overlapping soups, a cavity, a thin plate, a needle, a single triangle); distance fields for the contouring; polygons with
holes for the 2D mesher. No mesh file, no network.

**Independent checks** (`Checks.cs`; nothing of the operations under test):

| Check | How |
|---|---|
| closed and manifold | every directed edge once, its reverse present, one fan per vertex (from the triangle list alone) |
| finite | every vertex |
| volume, area, Euler characteristic | from the triangles (the volume relative to the first vertex, so a translation does not cancel digits) |
| the solid | the winding number as a sum of solid angles (Van Oosterom and Strackee 1983) at random points, against a Boolean function of the **inputs'** winding numbers; points closer to a surface than a margin are skipped (brute-force distance) |
| deviation | brute-force point-triangle distances of vertices and centroids |
| tet mesh | positive volume of every tet, every facet matched or on the boundary, the boundary equal to `BoundaryTriangles` and a solid, the input inside it |
| polygon mesh | directed edges of the faces, open edges, Euler characteristic |
| 2D mesh | positive triangles, area equal to the domain's, directed edges once, every input point a vertex, the angles outside `BadTriangles` |

`MeshRobustRepair.NotCleanReason` is used next to them as the library's own word on a mesh (it is the documented meaning of
"clean"), never as the only check of a result of the repair.

**A firing check is a finding only if the documentation promises otherwise.** Each first firing was read against the class
comments and guides before it counted (section 5): this is the control step of [METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md),
section 2, with the documentation in the place of the fixing commit.

## 3. Relations and chains tried

| Stage | Operations | Cases | Checks passed | Failed | Time |
|---|---|---|---|---|---|
| `boolean` | `ExactMeshBoolean` | 17 pairs (9 general, 8 with coplanar or touching faces) x 3 operations | 1349 | 0 | 8 s |
| `chain` | Boolean → `MeshRobustRepair` → `Remesher`, `Reducer`, `QuadRemesher`, `MeshTetrahedralization`; `SolidBooleanPipeline` | the 43 non-empty Boolean results | 1567 | 7 (#1337) | 41 s |
| `deep` | chains of up to 6 Booleans → tet; `SolidBooleanPipeline` with `InputRepair` | 24 random chains, 4 broken inputs x 3 operations x 2 partners x 3 modes | 684 | 0 | 7 s |
| `repair` | `MeshRobustRepair` | 33 defects of known solids | 493 | 2 (#1337) | 74 s |
| `wrap` | `MeshAlphaWrap`, `MeshTetrahedralization` | 11 inputs | 451 | 0 | 20 s |
| `contour` | `MarchingCubes`, `DualContouring`, `MeshOffset` | 5 fields x 2 methods, 5 solids x 5 distances | 228 | 0 | 156 s |
| `mesh2d` | `QualityMesh2` | 8 domains x 2 angle bounds x 10 variants | 368 | 0 | 1 s |
| `findings` | the reduced cases of section 4 and 6 | 4 boxes | 2 | 6 (#1337) | 0.1 s |
| total | | | 5142 | 15 | about 5 min |

Relations per stage (the details are in the stage files and the harness README):

| Relation of the brief | Where | Result |
|---|---|---|
| rotation | `boolean`, `repair`, `wrap`, `mesh2d` (31 degrees about z, 17 about x; z only in 2D) | holds: a solid, the same volume up to rounding (exact paths) or the method's band (wrap, solidify) |
| scale 2^k, bit for bit | every stage (2^-10 and 2^10; the whole chain at 2^10 in `deep`) | holds everywhere, including `MeshRobustRepair` on every path, the wrap, the tet mesher, `MeshOffset`, `MarchingCubes`, `DualContouring`, `QualityMesh2` (the same triangle list) |
| other scales (1e-3, 1e3, 3) | `boolean`, `mesh2d` | holds (volume within 1e-7 relative) |
| translation far from the origin | `boolean` (UTM, 2^20), `repair`, `wrap`, `mesh2d` (UTM), `contour` (whole cells) | holds; one observation (O1, section 6) |
| vertex and triangle reordering | `boolean`, `repair`, `wrap`, `contour` (offset), `mesh2d` (start vertex, ring direction) | holds |
| split into parts and join | `boolean` (a half space, axis-aligned and tilted: volumes add up, the union of the halves is the input: volume, winding, Euler characteristic), `mesh2d` (an added segment), `wrap` (two far parts = two wraps), `chain` (a far clean part next to a clean mesh) | holds, except the far part of the repair: **#1337** |
| `a ∪ a = a`, `a ∩ a = a`, `a − a = ∅` | `boolean`, on a separately permuted copy | holds (volume within 1e-12) |
| `(a ∪ b) − b = a − b`, `(a − b) ∪ (a ∩ b) = a`, `(a ∪ b) ∩ a = a` | `boolean`, on rounded results | holds (volume within 1e-6, winding samples); the results of the first have contacts, reported (section 5.1) |
| `a ∩ b = b ∩ a`, `a ∪ b = b ∪ a` | `boolean` | holds (volume within 1e-9) |
| volume and area identities | inclusion-exclusion, `vol(A−B) = vol A − vol(A∩B)` (`boolean`); the volume laws of every step (`deep`); tet volume = boundary volume (`wrap`); analytic volumes of the level sets (`contour`); the area of the domain (`mesh2d`) | holds |
| repair is idempotent | `chain` (on Boolean results), `repair` (on its own results) | **fails: #1337** (4 of 41 clean Boolean results are changed, 2 of 33 repair results are changed by a second `Apply()`) |
| remeshing keeps closed and manifold | `chain`: `Remesher` (closed, manifold, valid, Euler characteristic; clean with `PreventSelfIntersections`), `Reducer` (the same, plus the deviation bound and clean), `QuadRemesher`, `MeshTetrahedralization` | holds within the documented guarantees (section 5) |

Chains: Boolean → repair → remesh | reduce | quad | tet (`chain`, 17 pairs x 3 operations); Boolean → Boolean → ... six deep →
tet (`deep`); broken input → pipeline input repair → Boolean (`deep`); defect → repair → repair (`repair`); wrap → wrap and
input → wrap → tet (`wrap`); offset → inset (`contour`).

**Cost.** The whole run takes about five minutes; half of it is `MeshOffset` (60 calls, about 2.5 s on average). No single
call reached the 20 s limit (the harness prints `SLOW` for one; there is none): `MeshTetrahedralization` takes up to 1.1 s on
these inputs.

## 4. The defect: #1337, `MeshRobustRepair` changes a clean one-part mesh

- **Relations**: repair of a clean mesh is the identity; repair is idempotent; a far clean part does not change the result of
  a part.
- **Operation**: `MeshRobustRepair.Apply()` → `MeshAutoRepair` with `PostRepairCleanup` → `MeshPostRepairCleanup`.
- **Smallest input**: the box `[-1, 1]^3` with one edge split at `t` from a corner (9 vertices, 14 triangles, the same solid;
  `NotCleanReasons` is empty). `FindingsStage.BoxWithSplitEdge`.

| t | result | volume | input up to ... from the result | next to a far box |
|---|---|---|---|---|
| 1e-2 | unchanged | 8 | 2e-8 | passes through, unchanged |
| 1e-3 | 8 vertices, 12 triangles | 7.998666666666668 | 1.0e-3 | passes through, unchanged |
| 1e-4 | 8 vertices, 12 triangles | 7.999866666666667 | 1.0e-4 | passes through, unchanged |
| 1e-6 | 8 vertices, 12 triangles | 7.9999986666666665 | 1.0e-6 | passes through, unchanged |

`Apply()` is true, `ResultReason` is empty and `ResultPath` is `Repair` in every row.

- **Diagnosis** (read, not fixed):
  1. `MeshRobustRepairPassThrough.Split` returns false for fewer than two components, so the stage "a clean closed component
     is passed through untouched" does not apply to a clean input of one part. It runs through `MeshAutoRepair` with the cleanup.
  2. `MeshAutoRepair` without the cleanup leaves the mesh as it is. `MeshPostRepairCleanup` treats a triangle with
     `maxEdge^2 / (2 area) > 1000` like a degenerate one and collapses its shortest edge to the midpoint, with a flip check
     only: no bound on the displacement, no preference for the end that is a corner. The box corner moves by half the short
     edge; the solid loses a tetrahedron of `2t * 2 * 2 / 6`.
  3. The volume check (`MinVolumeIoU` 0.99) cannot see 1.7e-4, and the result is clean.
- **On real chains**: 4 of the 41 clean `ExactMeshBoolean` results of the `chain` stage are changed by the repair after them
  (sphere ∪ box: 303 / 602 → 301 / 598 vertices / triangles, the surface moves by 7.4e-5, 2e-5 of the diagonal), and 2 of the 33
  repair results are changed by a second `Apply()` (the exact resolve of three overlapping spheres: 1730 → 1726 triangles).
- **Size of the effect**: at most 1/2000 of the longest edge of the needle. Small on the meshes here, but the displacement is
  tied to the length of a long thin triangle, not to a defect: a clean CAD-style tessellation gets corners moved by a repair that
  had nothing to repair.
- **Reproducer**: three xUnit tests in the issue; all fail on master. In the harness: stage `findings`, lines `F1`.
- **Fix** (not in this spike; for the bugfix PR): pass a clean whole input through (small, correct on its own), or bound the
  displacement of the needle collapse and collapse onto the end that keeps the volume (general).

## 5. Checks that fired on documented limits (not defects)

Each of these fired in the first run of its stage. The harness now treats it as `INFO` or tests the bounded variant.

1. **Boolean results with contacts fail `NotCleanReason`.** Two boxes that touch along an edge or at a vertex, `(A∪B)−B` on
   curved inputs, and two boxes with a shared face after a rotation (the face is no longer exactly coplanar) give a result
   with `Contacts > 0`; `NotCleanReason` says "self-intersections" for it. Documented (`ExactMeshBoolean.Contacts`: manifold with
   duplicated vertices, not manifold once welded by position). The harness fails a Boolean result only if it is closed and
   manifold by the independent check and not clean while `RoundingClean` is true and `Contacts` is 0; that never happened
   (21 results with contacts in the `boolean` stage).
2. **A far translation of 2^40 changes the input**, not the Boolean (the transformed operands are different solids). The
   relation is run at 2^20 and UTM.
3. **`SolidBooleanPipeline` with `TargetFraction` and no `MaxDeviation`** reduces a 12-triangle box to a tetrahedron (volume 8
   → 1.48), Status Success. As asked: no deviation bound was set. With `MaxDeviation` and `MaxDeviationTwoSided` the volume,
   the two-sided distance and the winding samples hold in all 86 pipeline results.
4. **`Reducer` without `MinCollapseTriangleQuality`** leaves near-degenerate triangles on flat faces (a gridded cube reduced to
   a third: 2 triangles below 1e-12 diag^2, volume kept). Documented on the option. With it (0.02, as the pipeline sets it) every
   reduction is clean.
5. **`Remesher` without constraints** rounds sharp edges: the volume changes by 5 to 22 % at an edge length of 1/25 of the
   diagonal, mostly on the box-like results (23 of 86 runs). It stays closed, manifold and of the same Euler characteristic in all of
   them, and clean with `PreventSelfIntersections`. Documented (`docs/guides/remeshing-and-deformation.md`, guarantees and limits).
6. **`QuadRemesher`** returns `NoFaces` on inputs with a few large triangles (documented status) and a quad-dominant mesh with
   holes where faces were dropped (`DroppedFaces`; `docs/design/QUAD_REMESHING.md`: defect faces are not triangulated): 15 of 15
   successful results on the Boolean results are not closed. Every one is edge-manifold with consistent orientation and within
   one target edge of the input.
7. **The tet mesher meshes the alpha wrap**: a cavity is filled (once) and touching parts are joined (twice; Euler
   characteristic 2 instead of 4), a notch narrower than about 2 alpha is bridged (once). Documented on `MeshAlphaWrap`.
8. **Contouring on a grid that meets the surface.** In a first run with a cell of 0.1 (not a dyadic number) `MarchingCubes`
   had triangles below the degenerate area on the sphere and the touching spheres (upstream behaviour: a crossing next to a
   node), and a shift "by whole cells" changed the counts, because the nodes of the two grids differ by rounding and the box
   faces lie on nodes. With the dyadic cell of the committed run (1/8) none of it occurs. Thin walls below a cell are the subject
   of [CONTOURING_SDF_SUBGRID_2026-11.md](CONTOURING_SDF_SUBGRID_2026-11.md) and were not tested here (the slab case has a node
   plane inside it).
9. **`MeshOffset` under a permutation of the input** differs by up to 4e-8 relative in volume (the nearest triangle of a tie
   gives another Hermite normal); it promises 2^k bit identity, which holds, not permutation identity.
10. **Quality targets that are reported as not met.** The tet mesher misses its radius-edge target of 2 on all 11 inputs of the
    `wrap` stage (4 to 3253 tets above it, the dihedral floor of 10 degrees is met everywhere) and says so in `Targets`; the
    2D mesher lists the triangles below the angle bound in `BadTriangles` (4 of 16 runs, at the sharp input angles of the stars).
    Reported, so not a silent failure; the harness checks the angle bound outside `BadTriangles` only.

## 6. Observation O1: `DualContouring` with `Simplify` on a singular level set

Two spheres that touch in one point (`min` of two distances, a level set with a singular point and a crease plane), cell 1/8,
the field evaluated at `p − shift` with a shift by whole cells (exact in double):

| Simplify | shift | largest vertex difference |
|---|---|---|
| on | (0.125, 0, 0) | 2.5e-15 |
| on | (8, 0, 0) | 1.9e-2 (0.15 cells), volume 1.0460 vs 1.0471 |
| on | (40, −80, 120) | 2.1e-2 (0.17 cells) |
| off | (8, 0, 0) | 2.4e-15 |
| off | (40, −80, 120) | 1.4e-4 (0.001 cells) |

The same counts (112 vertices, 216 triangles), closed and manifold in every row. The sphere, the torus and the box show
rounding only (below 1e-12) for the same shifts. So the simplification (and to a small degree the QEF solve) takes a decision at
a threshold that the rounding of the shifted coordinates flips on this symmetric, singular input; the result stays inside
`SimplifyTolerance` (0.25 cells). No issue: no documented guarantee is broken (the class promises 2^k scale invariance, which
holds), and the input is degenerate by construction. If translation reproducibility of the simplified contour is wanted, this is
the case to start from (`FindingsStage`, lines `O1`).

## 7. What is left

Time-box: about three hours, stopped at the box; the last hour (deep chains, pipeline input repairs, the wrap, the 2D mesher,
the contouring) found no defect.

Not done, in the order of expected yield:

1. **The needle class behind #1337 in the other users of `MeshPostRepairCleanup`** (`MeshAutoRepair.PostRepairCleanup`, the
   pipeline's `InputRepair`): the same collapse runs there on inputs that are not clean, where a moved corner hides behind the
   repair. A relation for it: repair(defect far from a needle) keeps the needle's corner.
2. **Operations without a campaign**: `MeshMeshCut`, `PlaneCut` chains, `ExactMeshImprint`, the hole fillers, `MeshOffset` on
   non-convex thin inputs and insets that split a solid, `ContourLoft`, the terrain tools, UV atlas and geodesics. None was run here.
3. **Symmetry (the 48 axis maps) on chains**: only rotations, scales, translations and permutations were run; reflections on
   chains were not (the single-operation tier exists, `SymmetryInvarianceTests`).
4. **Parallel = serial and repeated runs on chains**: run for the tet mesher only. The thread-order class (#1314) needs
   machines with other core counts or a schedule fuzzer, not more inputs.
5. **Larger inputs**: everything here has at most a few thousand triangles. Cost cliffs (#1317) and budget paths
   (`RoundingMaxPairTests`, `MaxSteinerPoints`, the Steiner limit of the 2D mesher) are not reached; the A1 relation of
   [METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md) is the tool for them.
6. **Quad chains**: `PureQuads` and `SizeAdaptation` were not run (default options only); the quad result was not fed onward.
7. **Thingi10K at run time**: not used. The procedural inputs have no organic defects; the campaigns on the corpus exist
   elsewhere (`benchmarks/MeshRepairBenchmark`, `BooleanComparison`).

**Reading for the next hunt.** The defect was found by a relation that the catalogue ranks low (idempotence: "one issue at
most") applied across an operation boundary (Boolean result → repair), not by a relation inside one operation. Inside the
operations with campaigns (Boolean, repair of defects, wrap, 2D mesher) 5 000 checks found nothing. The remaining yield is at
the boundaries: what one operation calls a valid result against what the next one silently normalizes (needles, contacts,
duplicated vertices, near-degenerate triangles). A cheap standing form of that: for every operation with a "clean" input
class, a test "clean in → the same solid out, and unchanged where the docs say untouched".

## 8. Sources

Metamorphic testing: Chen, Cheung, Yiu 1998 and the surveys cited in [METAMORPHIC_TESTING.md](METAMORPHIC_TESTING.md). The solid
angle of a triangle: A. Van Oosterom, J. Strackee, *The Solid Angle of a Plane Triangle*, IEEE Transactions on Biomedical
Engineering 30(2), 1983 (the formula of `Checks.Winding`, written from the paper). No code of another library was read or
used. Listed in [sources/tools-and-verification.md](../sources/tools-and-verification.md).
