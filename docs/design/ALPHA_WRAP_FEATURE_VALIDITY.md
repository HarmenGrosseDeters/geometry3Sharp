# Alpha wrap sharp-feature pass: the validity statement and its proof

Status: **built 2026-11-22** (option 1 of [PROVABLE_SHARP_WRAP_STUDY.md](../research/PROVABLE_SHARP_WRAP_STUDY.md), owner go
2026-11-22). This note states what `AlphaWrapFeaturePass` (pass K, opt-in, `MeshRobustRepair.AlphaWrapSharpFeatures`)
guarantees, proves it from the checks in the code, and names the test of every premise. It covers validity only: closed,
2-manifold, free of self-intersections, the input inside. It does **not** cover the offset band (the pass checks the
distance to the input at vertices only, in floating point) and it says nothing about how much of a crease is recovered;
both are in the study.

## 1. Statement

Notation: the input I, the set of the input mesh's triangles (degenerate ones count as their segment or point; vertices
without a triangle are not part of I, and `MeshRobustRepair` removes them before the wrap), and a triangle mesh R.

**Hypotheses on the mesh handed to the pass.**

- (H1) R is a closed 2-manifold triangle mesh.
- (H2) No triangle of R is degenerate (collinear corners), and two triangles of R meet only in a shared vertex or edge.
- (H3) Every point of I has winding number 1 with respect to R and no triangle of R touches I (I is strictly inside).

The raw result of `MeshAlphaWrap` satisfies them: it is closed, manifold and free of self-intersections with the input
strictly inside (its class guarantees), and its triangles are facets of a 3D Delaunay triangulation, which has no
collinear facet.

**Theorem (pass K).** If R satisfies (H1)-(H3) and the sweep test is on (`CheckSweep`, the default; the switch exists for
tests, and without it (H3) is not preserved), then after `AlphaWrapFeaturePass.Compute()`, completed or cancelled at
any point, and after any sequence of `TryMove` calls, the mesh again satisfies (H1)-(H3). No property of which operations
are tried, in which order, or with which targets is used.

**Corollary (pipeline).** With `AlphaWrapSharpFeatures` on and the other options at their defaults, the mesh that
`MeshRobustRepair` takes from the wrap path (the pass, then `SimplifyAlphaWrap`) is closed, manifold, free of
self-intersections, and has the input strictly inside. (The opt-in steps after it, the triangle target and the quality
pass, are outside this statement.)

## 2. Proof of the theorem

The pass applies three operations, each kept only if its checks pass, and changes the mesh by nothing else:

| operation | replaces | by | swept region S |
|---|---|---|---|
| move of a vertex p to x (`try_move`) | the triangles around p | the same triangles with p at x | the tetrahedra (a, b, p, x) over the link edges (a, b) |
| split of an edge (a, b) with a new vertex at m (`split_pass`) | the two triangles (a, b, c), (b, a, d) | (a, m, c), (m, b, c), (b, m, d), (m, a, d) | the tetrahedra (a, b, c, m) and (b, a, d, m) |
| flip of an edge (a, b) (`flip_pass`) | (a, b, c), (b, a, d) | (c, a, d), (d, b, c) | the tetrahedron (a, b, c, d) |

In each case a triangulated disk D of R is replaced by a triangulated disk D' with the same boundary cycle.

**(H1) stays.** A move does not change the connectivity. A split of an interior edge and a flip are done by
`DMesh3.SplitEdge` / `DMesh3.FlipEdge`, which keep a closed manifold closed and manifold; the flip is tried only if the
edge (c, d) does not exist (`Mesh.FindEdge`), and an operation `DMesh3` refuses changes nothing.

**(H2) stays.** Each new triangle passes `NotDegenerate` (a floating-point normal exists, and the triangle is not
degenerate by `ExactTriangleIntersection.IsDegenerate`), so no degenerate triangle enters the mesh. (`IsDegenerate` decides
collinearity with exact orientation predicates behind a floating-point prefilter, "the squared normal is above 1e-24 of
the squared scale: not degenerate"; that prefilter is existing library code with its own test,
`IsDegenerate_PrefilterThreshold`, and this proof takes `IsDegenerate` as the definition's decision procedure. It adds no
threshold of its own.) `clear` then tests, with exact predicates
(`TriangleTriangleFilter.IntersectsBeyondShared`), every pair of new triangles and every new triangle against every
unchanged triangle whose box meets its box (the grid holds every triangle of the mesh and is updated by every kept
operation): the operation is kept only if no pair meets beyond its shared vertices and edges. That test is defined for
non-degenerate triangles; by (H2) and the first sentence all are, and should the mesh ever hold a degenerate triangle
the operation next to it is refused rather than left untested. Unchanged triangles meet each other as before. So R'
= (R minus D) plus D' satisfies (H2).

**(H3) stays.** No new triangle touches I: `Reducer.TrianglesTouch` (exact; degenerate input triangles as segments and
points) is the first test of `clear`. For the winding number, deform D into D' linearly (the moved or new vertex runs
from its old position, or from a point of the split edge, to its new one; for a flip, sweep the tetrahedron (a, b, c, d)
from one pair of its faces to the other). Every intermediate surface differs from R only inside S, the union of the
closed tetrahedra of the table. The winding number of a point with respect to a closed surface changes only when the
surface passes through the point, so a point outside S keeps its winding number. The pass keeps the operation only if
`Reducer.TetrahedraMeet` finds no input triangle meeting S, so every point of I is outside S and keeps winding number 1.
(The intermediate surfaces need not be embedded; only the winding number of points outside S is used.)

**`TetrahedraMeet` is complete** (the "containment sentence"): it reports every input triangle T that meets the closed
tetrahedra. It first tests T against all four faces of every tetrahedron with exact predicates. If T touches none of
them, T, being connected, lies in the open interior of one tetrahedron or outside all of them; in the first case each of
its corners is strictly inside that tetrahedron, which the exact orientation test of the corners finds. A flat
tetrahedron (four coplanar corners) has no interior, and is covered by its four faces. (The centroid, also tested, is
not needed for this argument.)

Induction over the kept operations gives the theorem. A cancelled `Compute` stops between operations.

## 3. Proof of the corollary

After the pass the mesh satisfies (H1)-(H3). `SimplifyAlphaWrap` (`MeshRobustRepair.SimplifyWithin` with the wrap's input)
then collapses edges with a `Reducer` that has `PreventSelfIntersections` and `KeepClearOf` = the input:

- closed, manifold, no self-intersections: `SimplifyWithin` returns a mesh only if `NotCleanReason` (with the exact
  self-intersection test, `CheckSelfIntersections`, on by default) is empty on the finished mesh; otherwise the pipeline keeps the mesh it had. This part
  is an a-posteriori check of the result, not an invariant.
- the input inside: every collapse is kept only if its new triangles do not touch the input (`Reducer.TrianglesTouch`) and
  the input does not meet the swept tetrahedra (old triangle, new position; `collapse_sweeps_obstacle`, #760). The
  argument is the one of (H3) above with the collapse's swept region.

## 4. The premises and their tests

| premise | check in the code | tests; **bold**: fails if the check is removed (mutation run 2026-11-22, or by its own counter-case) |
|---|---|---|
| new triangles against the unchanged ones, a degenerate neighbour included | `clear`: grid query, exact test; refusal next to a degenerate rest triangle | **`TryMove_NextToADegenerateRestTriangle_IsRefused`** (new; the pass used to skip such a triangle) |
| no degenerate new triangle | `NotDegenerate` | `NotDegenerate_RefusesCollinearCorners_Exactly` (new; pins the contract, no teeth against the old code: see the remark below) |
| no input triangle meets the swept region, containment included | `Reducer.TetrahedraMeet` | **`TetrahedraMeet_AgreesWithTheSeparatingAxisOracle`** (new: an independent separating-axis oracle on integer coordinates, single tetrahedra and pairs; it requires more than 500 meeting cases, more than 500 apart and more than 100 wholly inside; flat tetrahedra by two fixed cases), **`TryMove_RefusesToSweepOverInput`**, **`TryMove_SweepFaceTest_FindsAnInputTriangleInASideFace`** |
| new triangles do not touch the input, degenerate input included | `Reducer.TrianglesTouch` | **`TryMove_RefusesToCutTheInput_OrComeTooClose`**, **`TryMove_RefusesToCutADegenerateInputTriangle`** |
| the pipeline's simplification | `Reducer.KeepClearOf`, `SimplifyWithin`'s final check | **`AlphaWrapNoContactTests`** (without the input the simplification crosses it), **`ReducerSweptClearTests`**; `Pipeline_PassThenSimplification_KeepsTheGuarantees` (new: states the conclusion after the pass by exact tests that are not the pipeline's own; no teeth of its own, because after the pass a simplification without the input does not cross this input) |
| the theorem's conclusion on a real wrap | - | `LShape_RecoversTheConcaveCrease_KeepsTheGuarantees`, `FeaturePass_Cancel_LeavesACleanMesh`, `FeaturePass_Invariance` |

**Remark on `NotDegenerate`.** Before this note the pass decided "not degenerate" by a floating-point normal alone
(length above 1e-12 of the longest squared edge). No input was found on which that test accepts exactly collinear
corners: for exactly parallel edge vectors the two products of each cross-product component are the same real number,
so only the rounding of the subtractions contributes, about 1e-16 of that scale (a search over 10 million exactly
collinear triples, the underflow range included, found none). So the `IsDegenerate` call added here changes no result
and its removal is an equivalent mutant: the new test pins the contract, it cannot fail on the old code. The call is
there so that the pass and its proof use one decision of "degenerate", the library's, for new and rest triangles alike,
instead of a second floating-point threshold with its own rounding argument. The rounding argument that remains is
`IsDegenerate`'s prefilter (existing, tested); this change adds no error bound.

## 5. What is not covered

- **The offset band.** Moved and new vertices keep `MinDistance` (0.9) offsets from the input in floating point; between
  vertices a triangle may come closer (measured 0.003 offsets in the study), and nothing bounds the distance from above.
  A certified band needs a point-triangle distance with a rounding bound: left out by the owner's decision of 2026-11-22.
- **`AlphaWrapQualityPass`** (off by default) has the same structure and still skips degenerate rest triangles
  (`AlphaWrapQualityPass.accept`); this note makes no statement about it.
- **Recovery**: which creases come back, and how completely, has no guarantee (study, section 5).
