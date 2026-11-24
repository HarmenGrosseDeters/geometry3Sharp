# MeshRobustRepair and the lost area: what the repair drops, why, and what should report it (#1479, 2026-11)

Status: measurement and diagnosis, no library change. Issue [#1479](https://github.com/HarmenGrosseDeters/geometry3Sharp/issues/1479), a side finding of
[ALPHA_WRAP_ACCURACY_2026-11.md](ALPHA_WRAP_ACCURACY_2026-11.md) (section 7, option B2 of section 10; draft PR #1477, this work sits on top of that branch).
Probe: modes `lost` and `repro` of [benchmarks/WrapAccuracyProbe](../../benchmarks/WrapAccuracyProbe/README.md); raw JSONL and the tables
([tables_lost.md](../../benchmarks/WrapAccuracyProbe/results/tables_lost.md)) in its `results/`. The meshes are read from a local copy of Thingi10K and
never copied or committed; this note and the result files name ids and numbers only. Times are wall clock on a machine that other sessions used.

## 1. Answer in short

- The five numbers of the issue are reproduced (section 3). They hardly depend on the tolerance, and the other 16 open meshes lose at most 3.2 % (at
  tau = 4e-4 of the diagonal), 12 of them less than 1 %.
- **The four `ExactResolve` meshes (42331, 37731, 1038438, 66112) lose interior surface: the correct outcome for a solid (judgment a).** 81 to 98 % of
  the lost area has winding number "inside" on both sides (interior walls, contact faces of touching parts, parts inside other parts), 92 to 100 % of it lies
  inside the result's solid, and 0 to 2.9 % of the visible surface is lost. The rule is the definition of `ExactMeshBoolean.Resolve`: the result is the
  boundary of the region where the winding number is positive. The volume check cannot object (the solid is the same, IoU 0.9995 to 1), and it should not. 42331 has a residual that is not explained (section 5: 19 % of its lost area is skin and 8 % lies outside the result's solid; nothing visible is lost).
- **100040 is real damage (judgment b), and the acceptance check is what caused it.** The repair stage's result is right (0.26 % lost, nothing added)
  and its exact resolve is clean, but `MinVolumeIoU` rejects it (IoU 0.0019) because the *reference* solid, the input's winding-number solid, is nearly empty (the `Solidify` contour of that field encloses 0.6 % of the box,
  the repaired solid is 37.6 % of it). The stages then run on the input and `Solidify` contours the wrong field: 72 % of the area lost, 57 % of the result added.
  With `MinVolumeIoU = 0` the same input ends in `ExactResolve` with 0.26 % lost. The same mechanism is reproduced procedurally by a closed box
  with a third of its triangles flipped (section 7): 47 % lost, 56 % added, against 0 / 0 without the check.
- **No check measures the lost area**, and the existing ones cannot: `MinVolumeIoU` and `RepairVolumeIoU` compare solids (they do not see interior surface, thin parts, or
  an open sheet, whose winding number is about 1/2 and so no solid); the lost-part check of F1 asks whether the result reaches a part, within 0.25 of the part's
  diagonal, so a fin that is partly inside a solid counts as present (section 7: a fin of 7 % of the visible surface vanishes with `Apply() == true`).
- A report of the **lost area outside the result's solid** (input surface farther than tau from the result and not inside the result) separates most damaged cases from the
  by-design ones here, but not at zero: 72 % (100040), 35 % and 34 % (flipped box, with hole), 6.8 % (fin) against at most 2.6 % on the 20 other meshes. The by-design cases
  are not 0: 1038438 1.3 %, 42331 1.0 %, 66112 0.65 % (only 37731 is 0; table 3.1), so about 1 % of real outside loss sits on the "by design" side and a threshold has to lie above it
  (here the gap is 1.3 % against 6.8 %). The measure cannot tell that residual from a small real loss, and it does not flag 66640 (2.6 %), whose mechanism is that of 100040 in a weaker
  form (section 5): that one needs the *added* share (16.3 % against at most 1.9 % on the controls) as well. The plain lost-area share does not separate: it is 13 to 50 % on the four correct cases (section 6).
- Not the F2 plan (PR #1257): that is about small separable parts in scenes of very different scales. Nothing here is a scale problem; the lost-area report
  would also serve as F2's acceptance measure (section 8).

## 2. What was measured

- **Lost and added share** at tau (the issue's: 4 offsets = 4e-4 of the diagonal at offset 1e-4; the probe also gives 1e-4, 1e-3, 4e-3): lost = share of the input's area (60000
  area-stratified samples) farther than tau from the result; added = share of the result's area farther than tau from the input.
- **Structure of the input:** components and boundary loops, bowties, pairs of self-intersecting triangles (`MeshRepairMetrics.SelfIntersectionsAtMost`), the number of facets in the STL against the
  triangles of the mesh the library reads (the reader drops non-manifold facets), the exact generalized winding number at 6000 uniform points of the box, and each surface sample classified by the winding number
  at tau on both sides of it: *skin* (one side inside, the other outside; "inside" = above 0.5), *interior wall* (both inside), *outside sheet* (both outside), *ambiguous* (a value within 0.15 of 0.5).
  *Doubled*: another triangle (not a neighbour) on the sample's normal line within 2 tau (20 tau).
- **Lost triangles** (at least 2 of 4 points farther than tau from the result), clustered by edge adjacency in the input; per cluster and in total: whole component or part, class, inside the result's solid
  (winding number of the result at the centroid above 0.5), *visible* (at least 10 % of up to 64 rays from just off the centroid, on either side, leave the input without a hit: reachable from outside; for an open
  input "outside" includes the inside seen through its holes).
- **Per stage on its own:** `MeshAutoRepair` (with the post-repair cleanup, as the repair stage), `ExactMeshBoolean.Resolve` of the input and of the repair result (the library's `Resolve`, orientation by area:
  not identical to the orientation of the repair's own resolve, so `robust_MinVolumeIoU0` is the faithful one), `MeshWindingNumberSolidify` (resolution 128, adaptive) of the input and of the repair result, the wrap at
  the repair's defaults (alpha d/300, offset d/3000) and at d/100 and 1e-4, and `MeshRobustRepair` with `MinVolumeIoU = 0` and with `ExactResolve = false` (public settings, no library change).
- The measurement is on the mesh as `StandardMeshReader` reads it (welded). It dropped 2 (42331), 6 (37731) and 32 (66112) facets of the STL as non-manifold; the other three lost none.

## 3. Reproduction: the 21 open meshes

### 3.1 The 21 open meshes: result of `MeshRobustRepair` (lost / added share at the given tau, in units of the diagonal)

| id | triangles | path | repair IoU | lost 1e-4 | lost 4e-4 | lost 1e-3 | lost 4e-3 | added 4e-4 | lost outside the result's solid / input |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|
| 100040 | 736 | Solidify | 0.0019 | 0.731 | 0.720 | 0.706 | 0.651 | 0.566 | 0.7199 |
| 1038438 | 3448 | ExactResolve | 0.9995 | 0.485 | 0.465 | 0.446 | 0.348 | 0.005 | 0.0133 |
| 66112 | 16860 | ExactResolve | 1.0000 | 0.186 | 0.184 | 0.178 | 0.151 | 0.000 | 0.0065 |
| 37731 | 11042 | ExactResolve | 1.0000 | 0.139 | 0.138 | 0.137 | 0.132 | 0.000 | 0.0000 |
| 42331 | 4712 | ExactResolve | 1.0000 | 0.166 | 0.162 | 0.155 | 0.120 | 0.000 | 0.0102 |
| 66640 | 9800 | Solidify | 0.8749 | 0.089 | 0.029 | 0.018 | 0.009 | 0.163 | 0.0256 |
| 42293 | 310 | Solidify | 0.5269 | 0.032 | 0.021 | 0.015 | 0.010 | 0.007 | 0.0029 |
| 68656 | 639 | ExactResolve | 0.9951 | 0.026 | 0.024 | 0.022 | 0.009 | 0.019 | 0.0053 |
| 73429 | 438 | Solidify | 0.8642 | 0.014 | 0.010 | 0.009 | 0.006 | 0.001 | 0.0000 |
| 38631 | 16705 | ExactResolve | 1.0000 | 0.009 | 0.007 | 0.005 | 0.000 | 0.000 | 0.0000 |
| 47979 | 7241 | ExactResolve | 0.9996 | 0.013 | 0.009 | 0.005 | 0.000 | 0.006 | 0.0028 |
| 86648 | 8604 | ExactResolve | 0.9931 | 0.005 | 0.005 | 0.004 | 0.000 | 0.003 | 0.0000 |
| 37111 | 88034 | ExactResolve | 1.0000 | 0.001 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0004 |
| 109091 | 15197 | ExactResolve | 1.0000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0000 |
| 285604 | 12764 | ExactResolve | 1.0000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0000 |
| 57070 | 5786 | Repair | 1.0000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0000 |
| 50108 | 2276 | Repair | 1.0000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0000 |
| 45341 | 4348 | ExactResolve | 0.9996 | 0.000 | 0.000 | 0.000 | 0.000 | 0.012 | 0.0000 |
| 1146197 | 14580 | ExactResolve | 1.0000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0000 |
| 80177 | 588 | Repair | 1.0000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0000 |
| 96675 | 11236 | ExactResolve | 1.0000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 | 0.0000 |


Reading of the table:

- **Reproduced.** The five numbers match the issue at tau = 4e-4 (42331 0.162, 37731 0.138, 1038438 0.465, 66112 0.184, 100040 0.720; the stages are those of the issue).
  Settings: `MeshRobustRepair` with its defaults (`MinVolumeIoU` 0.99, `ExactResolve` on, wrap alpha d/300 and offset d/3000 as last resort), the meshes as `StandardMeshReader` reads them,
  tau = 4 offsets of spike B (offset 1e-4 of the diagonal). The repair does not depend on the probe's alpha or offset; only the measuring tolerance is the probe's.
- **Dependence on tau.** Weak where the loss is large: 42331 0.166 / 0.162 / 0.155 / 0.120 for tau = 1e-4 / 4e-4 / 1e-3 / 4e-3; the loss falls by a quarter to a third at 4e-3 because lost sheets lie close to
  surface that stays (p99 of the distance of the lost samples: 0.019 to 0.114 of the diagonal, `lost_p99` in the JSONL). It is larger for a `Solidify` result, whose contour moves the surface by up to a cell: 66640 0.089 at 1e-4, 0.029 at 4e-4.
- **Control.** The other 16 meshes lose 0 to 2.9 % at 4e-4 (66640 2.9 %, 68656 2.4 %, 42293 2.1 %, 73429 1.0 %; 12 of the 16 lose under 1 %, 7 lose nothing). The five stand out by a factor of 5 or more.

### 3.2 The five, each stage on its own (lost / added share at tau = 4e-4; `-` = no mesh; a bracket: the verdict of `NotCleanReason` if not clean)

| id | robust | auto | resolve_input | resolve_auto | solidify_input | solidify_auto | wrap_robust_defaults | wrap_d100_1e-4 | robust_MinVolumeIoU0 | robust_NoExactResolve |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 42331 | 0.162 / 0.000 | 0.000 / 0.000 (self-intersections) | 0.162 / 0.000 (not closed) | 0.654 / 0.000 | 0.167 / 0.008 | 0.657 / 0.008 | 0.674 / 0.045 | 0.703 / 0.117 | 0.654 / 0.000 | 0.167 / 0.008 |
| 37731 | 0.138 / 0.000 | 0.001 / 0.004 (degenerate triangles) | 0.135 / 0.000 (not closed) | 0.138 / 0.000 | 0.145 / 0.009 | 0.145 / 0.009 | 0.159 / 0.021 | 0.187 / 0.051 | 0.138 / 0.000 | 0.145 / 0.009 |
| 1038438 | 0.465 / 0.005 | 0.000 / 0.005 (degenerate triangles) | 0.468 / 0.000 (not closed) | 0.465 / 0.005 (degenerate triangles) | 0.534 / 0.046 | 0.532 / 0.047 | 0.558 / 0.062 | 0.615 / 0.157 | 0.465 / 0.005 | 0.532 / 0.047 |
| 66112 | 0.184 / 0.000 | 0.000 / 0.000 (self-intersections) | 0.184 / 0.000 (self-intersections) | 0.184 / 0.000 (self-intersections) | 0.202 / 0.018 | 0.202 / 0.017 | 0.224 / 0.037 | 0.277 / 0.077 | 0.184 / 0.000 | 0.202 / 0.017 |
| 100040 | 0.720 / 0.566 | 0.000 / 0.000 (degenerate triangles) | - | 0.003 / 0.000 | 0.720 / 0.566 | 0.016 / 0.015 | 0.048 / 0.042 | 0.077 / 0.055 | 0.003 / 0.000 | 0.720 / 0.566 |

(`resolve_*` is the public `ExactMeshBoolean.Resolve`, orientation by area; the repair's own resolve orients by the nesting of the components, so `robust_MinVolumeIoU0` is the faithful "repair without the
volume check": for 42331 it is the run that the volume check turns into the 0.162 result. `solidify_*` and `wrap_*` are the stages alone, not what `MeshRobustRepair` would return.)

- **The stage that keeps the area is not clean.** `MeshAutoRepair` alone loses 0 to 0.1 % on four of the five and keeps interior sheets, but its result has self-intersections or degenerate triangles, so the repair goes on.
  Every stage that returns a clean closed solid from an input with doubled or interior surface loses that surface: `Resolve` by its rule, `Solidify` and the wrap because the winding number field has no surface there
  (and, on the four, they add invented surface: 0.8 to 4.6 % for `Solidify`, up to 16 % for the wrap at d/100). Only nested closed shells that are clean as they are keep an interior wall (`box_in_box`, section 7).
- **100040 is the exception that shows a gentler stage:** `resolve_auto` (the repair result, resolved) loses 0.3 % and adds nothing, `solidify_auto` 1.6 % / 1.5 %, the wrap 4.8 % / 4.2 %. The result that `MeshRobustRepair` returns is the `Solidify` of the
  *input* (72 % / 57 %), the worst of the stages.

## 4. Structure of the five

Counts from `results/lost_open5.jsonl` and `results/lost_structure_open21.jsonl` (the share of inconsistently oriented edges is only in the latter, field `edges_inconsistently_oriented_share`); winding number at 6000 uniform points of the box (~0: within 0.1 of 0; between: 0.1 to 0.9; ~1: 0.9 to 1.1).
Surface classes: share of the input's area by the winding number on both sides (skin: one side inside; interior wall: both inside).

| id | components (closed), boundary loops | self-int pairs | winding number of the box: ~0 / between / ~1 / 2 and more | surface: skin / interior wall / outside sheet / ambiguous | doubled sheet within 2 tau (20 tau) | edges inconsistently oriented |
|---|---|---:|---|---|---|---:|
| 42331 | 67 (65), 2 loops of 3 edges; 91 bowties | 81514 | 0.84 / 0 / 0.16 / 0 | 0.84 / 0.16 / 0 / 0 | 0.45 (0.58) | 0 |
| 37731 | 102 (0): one of 10941 triangles (97 % of the area), 101 single triangles; 133 loops (472 boundary edges) | 3175 | 0.60 / 0 / 0.40 / 0 | 0.86 / 0.14 / 0 / 0 | 0.09 (0.09) | 0 |
| 1038438 | 482 (30), 463 loops (1690 boundary edges); 232 bowties; largest part 212 triangles | 14163 | 0.67 / 0 / 0.33 / 0.01 | 0.50 / 0.50 / 0 / 0 | 0.40 (0.58) | 0 |
| 66112 | 44 (23), 21 loops; 14 bowties | 7791 | 0.76 / 0 / 0.22 / 0.03 | 0.81 / 0.19 / 0 / 0 | 0.05 (0.13) | 0 |
| 100040 | 6 (0): two of 366 triangles (each 50 % of the area) and 4 single triangles; 6 loops | 78 | 0.10 / **0.63** / 0.00 / 0.00, **negative 0.27** | 0.41 / 0 / 0.35 / **0.24** | 0 (0) | **0.22** |

- **The four `ExactResolve` meshes are assemblies of closed or nearly closed parts that overlap or touch** (42331: 65 of 67 components closed, 81514 intersecting pairs, 45 % of the surface doubled within 2 tau; 1038438: 482 components, 40 % doubled).
  Their winding number field has a clear inside (between-values at most 0.1 % of the box) and no negative values: the "input is not a solid" hypothesis does not apply to them. 14 to 50 % of their area is interior wall (both sides inside).
- **100040 has no clear inside**: 63 % of the box has a winding number between 0.1 and 0.9, 27 % is negative, 0.1 % is near 1, and 22 % of its interior edges are traversed in the same direction by their two triangles (none in the other four; 0 to 1.1 % in the 16
  controls: 66640 0.08 %, 73429 0.9 %, 42293 1.1 %, 47979 0.09 %, 37111 0.02 %, the rest 0). Each of its two big components is a thin bar (0.96 x 0.15 x 0.09 of the diagonal) with one hole of 6 boundary edges that spans 10 % of the bar's box diagonal, nearly its whole
  cross section (an open end); the bar's own winding number at 400 random points of its box: 29 % negative, 11 % near 0, 61 % between, none near 1. A nearly closed bar with part of its triangles reversed.
- Facets of the STL against triangles of the mesh the reader makes: 42331 4714 / 4712, 37731 11048 / 11042, 66112 16892 / 16860, the others equal (non-manifold facets are dropped by the reader, at most 0.2 %).

## 5. What is lost, where, and by which decision

Lost region = lost input triangles (2 of 4 points farther than tau = 4e-4 from the result) joined by edge adjacency.

| id | lost / input | regions | whole components lost (share of lost) | skin | interior wall | outside sheet | ambiguous | inside the result's solid | visible | lost / visible surface | lost / skin |
|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 42331 | 0.130 | 80 | 34 (0.07) | 0.19 | 0.81 | 0 | 0 | 0.92 | 0 | 0 | 0.029 |
| 37731 | 0.139 | 28 | 26 (0.13) | 0 | 0.98 | 0 | 0.02 | 1.00 | 0 | 0 | 0 |
| 1038438 | 0.501 | 485 | 277 (0.16) | 0.06 | 0.94 | 0 | 0 | 0.97 | 0.02 | 0.029 | 0.059 |
| 66112 | 0.151 | 93 | 10 (0.00) | 0.08 | 0.92 | 0 | 0 | 0.96 | 0.04 | 0.010 | 0.014 |
| 100040 | 0.733 | 17 | 0 | 0.53 | 0 | 0.12 | 0.35 | 0.02 | 0.95 | 0.733 | 1.000 |

(Columns "skin" to "visible" are shares of the lost area; "visible" = at least 10 % of the rays leave the input; "lost / visible surface" = lost area of the visible surface over its area. The area counted by triangles differs from the sampled share of
section 3 where the result cuts through triangles: 0.130 against 0.162 for 42331.)

**The four `ExactResolve` meshes.** The rule is in `resolve_exactly`: the repair result is oriented (inside-out parts flipped by their nesting), checked to be winding-closed, and replaced by `ExactMeshBoolean(m, empty, Union)`,
which `ExactMeshBoolean.Resolve` documents as "the boundary of the solid of a mesh (where its winding number is positive): self-intersections, nested shells and duplicated faces are resolved exactly". Whatever is not on that
boundary is dropped by definition: interior walls (the shared face of two touching parts, a part inside another part, doubled faces). The data agree: 81 to 98 % of the lost area is interior wall, 92 to 100 % lies inside
the result's solid, and the whole components lost are parts inside other parts (42331: 34 components, 7 % of the lost area, all inside the result; 37731: 26 single triangles floating inside the solid; 1038438: 277 small parts, 16 %).
The accepted result has the same solid as the input (IoU 0.9995 to 1): the loss of interior surface is invisible to the volume check and, by the definition of a solid's boundary, correct.
A residual remains in two meshes: visible skin, small but not zero. 1038438 loses 1.2 % of its area as visible skin (123 triangles in 26 components, none a whole component) and 66112 0.65 % (185 triangles in 20 components). That fits triangles
that straddle the intersection curves of parts (the part of the triangle outside the other part stays, the triangle's sample points are farther than tau), but this was not verified. Visible-surface loss: 0, 0, 2.9 %, 1.0 % (42331, 37731, 1038438, 66112).

**42331: a residual not explained here.** 19 % of its lost area is skin (inside on one side only; 2.9 % of its skin area) and 8 % of the lost area lies outside the result's solid (1.0 % of the input's area, table 3.1); visible is 0. Skin that is lost partly outside the result is not an interior wall: the explanation above (interior walls and parts inside parts) covers the other 81 %, not this. What is established: nothing visible is lost. What is not: why this skin, and this part outside the result's solid, is lost (the guess given for 1038438 and 66112, triangles that straddle intersection curves, was not checked for it). Not diagnosed.

**100040.** Not by the rule of the exact resolve but by the order of the stages, which the volume check set:

1. The repair stage (`MeshAutoRepair` with cleanup) fills the two open ends and orients the bars: closed, 748 triangles, nothing lost or added, but with degenerate triangles (`NotCleanReason`), so the stages go on.
2. Its exact resolve is clean (732 triangles, 0.3 % lost, nothing added; solid 37.6 % of the box).
3. The volume check compares it with the input's solid (`SolidReference`: the oriented copy of the input, winding number above 0.5): IoU **0.0019**, rejected ("volume differs from the input"). The solid of that field is tiny: the `Solidify` contour of it
   encloses 0.6 % of the box. The winding number of a surface with a fifth of its edges inconsistently oriented has no clear inside (section 4).
4. The stages restart from the input: its resolve is "not closed (winding number)", the repair is `UsedInput`, and `Solidify` (128 cells, iso 0.5 of the same field) contours the field: **72 % lost, 57 % added**. Its result is clean and
   its volume is not checked (a solidify result never is), so `Apply()` returns true.

With `MinVolumeIoU = 0` the same input ends in the exact resolve of step 2. The volume check, built to catch a repair that *lost* volume, here rejects the right result against a reference that is itself the defect. 66640 (inside-out, 45 % of the box
negative, 72 loops) shows a weaker form: its repair result resolves with 0 lost and 0.000 added, is rejected at IoU 0.875, and `Solidify` returns 2.9 % / 16.3 %. Its lost share outside the result's solid is only 2.6 % (table 3.1), so by that measure alone it falls among the controls (section 6): the added share is what shows this mechanism.

## 6. Judgment

| id | judgment | what is lost | share of the outer, visible surface lost | rule that dropped it |
|---|---|---|---:|---|
| 42331 | **(a)** for 81 % of the lost area (nothing visible is lost); a residual is not explained | interior walls and parts inside parts of a 65-part assembly (45 % of its surface is doubled); the other 19 % of the lost area is skin, 8 % of it outside the result's solid (1.0 % of the input's area), not diagnosed | 0 | `Resolve`: boundary of the winding-positive region (for the residual: not established) |
| 37731 | **(a)** correct | interior walls, 26 floating single triangles inside the solid | 0 | the same |
| 1038438 | **(a)**, with a residual | half the area is interior wall of 482 parts; 1.2 % of the input's area is visible skin along intersection curves (not diagnosed) | 2.9 % | the same |
| 66112 | **(a)**, with a residual | interior walls; 0.65 % of the area is visible skin | 1.0 % | the same |
| 100040 | **(b)** real damage; the check misfired | the visible skin of two bars (100 % of the skin; 95 % of the lost area is visible) | 73 % | `MinVolumeIoU` rejected the right result against a reference that is not a solid, then `Solidify` of the input |

Which check should have caught what:

- **`MinVolumeIoU` / `RepairVolumeIoU`** compare solids. They cannot see interior surface (a), a thin part under the sampling, an open sheet (no solid), or a part lost by `Solidify`, whose volume is not checked (`MeshRobustRepair.cs`: "the solidify and the wrap themselves are not checked for volume"). The final lost-part check (F1, `MeshRobustRepairLostParts.FirstLost`) does run after every stage, so a separable part that `Solidify` loses whole is reported there (the next bullet says what it cannot see).
  For 100040 the check *fired*, in the wrong direction (section 5): its reference is the winding number solid of an input with inconsistent orientation, and it has no notion of "the reference is not a solid".
  A guard on the reference (share of box points with a winding number between 0.1 and 0.9; share of inconsistently oriented edges) separates 100040 (0.63; 0.22) from the 20 other open meshes (at most 0.18 for between-values on 66640, at most 0.011 inconsistent).
  The three other `Solidify` meshes of the control also have inconsistent edges (66640 0.08 %, 73429 0.9 %, 42293 1.1 %); that this is the cause of their IoU was not traced.
- **The lost-part check (F1, `MeshRobustRepairLostParts`)** asks whether the result reaches a part: present if the result has a triangle closer than 0.25 of the part's own diagonal to one of 64 samples of the part. It is silent when a
  part is partly inside the solid (the fin of section 7), and it judges whole components only.
- **No existing check measures the lost area.** What would have caught 100040, the flipped box and the fin: the area of the input that is farther than tau from the result *and not inside the result's solid* (surface inside the solid is dropped
  by design; surface outside it is gone). Over the 31 inputs of this note: 100040 72.0 %, flipped box 35.3 %, with a hole 33.7 %, 20 % flips 9.3 %, fin 6.8 %; at most 2.6 % on the other 20 open meshes, and not 0 on the by-design ones
  (66640 2.6 %, a `Solidify` case, not by design; 1038438 1.3 %, 42331 1.0 %, 66112 0.65 %, 37731 0). So about 1 % of real outside loss sits on the "by design" side, and the
  measure cannot separate such a residual from a small real loss. 66640 is on the wrong side: section 5 calls it a weaker form of the 100040 mechanism (rejected at IoU 0.875; `Solidify` 2.9 % lost, 16.3 % added), yet its
  lost-outside share (2.6 %) counts it among the controls, so that share alone does not flag the mechanism; the added share does (16.3 % against at most 1.9 % on the controls; 100040 56.6 %, the flipped box 56 %). The plain lost-area share would flag the four correct ones (13 to 50 %). The visible-surface share separates equally (0 / 2.9 / 1.0 / 0 against 73 %) but costs
  rays; the "outside the result's solid" test costs one winding number per lost sample, and the result is closed (O(log n) each).

## 7. Procedural reproducers (no meshes; `repro`, `Reproducers.cs`)

Box 16 x 10 x 6 of unit quads (1264 triangles) unless noted. Lost / added at tau = 4e-4; "outside" = lost outside the result's solid over the input's area; "visible" = lost share of the visible surface; the last column is
`MeshRobustRepair` with `MinVolumeIoU = 0`.

| class | input | path | lost / added | outside | visible | `MinVolumeIoU = 0` | what it shows |
|---|---|---|---|---:|---:|---|---|
| a | `touching_boxes`: two closed boxes sharing a 6 x 6 patch of a face | ExactResolve | 0.099 / 0 | 0 | 0 | same | the doubled contact wall is dropped: by design |
| a | `box_fin_solid`, `box_thin_plate`: a closed plate 0.02 / 0.002 thick (9e-4 / 9e-5 of the diagonal) through the box top | ExactResolve | 0.031 / 0 | 0 | 0 | same | only the part inside is lost; the exact resolve keeps a plate far thinner than tau |
| (kept) | `box_in_box`: a closed box inside a closed box | Repair | 0 / 0 | 0 | 0 | same | a nested closed shell is a clean input and stays (`Resolve` alone would drop it: lost 0.103) |
| (stage rule) | `box_thin_plate` with `ExactResolve = false` | Solidify | **0.185** / 0 | | | | `Solidify` loses the 0.002 plate whole; the 0.02 plate survives (0.031, adaptive refinement); the limit lies between 9e-5 and 9e-4 of the diagonal, not located |
| **c** | `box_fin_sheet`: a closed box with an open sheet (zero thickness, 6 x 8 outside the box) through its top | ExactResolve, `Apply()` true | 0.102 / 0 | **0.068** | **0.071** | same | zero thickness has winding number 1/2 on the sheet: no solid; the part of the fin outside the box vanishes silently (F1 counts it present: it touches the box) |
| **b** | `flipped_closed`: the box with 33 % of its triangles reversed (45 % of its edges inconsistent: `edges_inconsistently_oriented_share` 0.4509 in `results/lost_repro.jsonl`) | **Solidify** (`UsedInput`) | **0.466 / 0.562** | **0.353** | **0.483** | Repair, 0 / 0 | the mechanism of 100040: IoU of the repair result 0.089, rejected; the box becomes a contour of a field whose solid is 7 % of the box (the repaired solid is the whole box) |
| **b** | `flipped_hole`: the same with a 1 % hole (0.445 inconsistent) | Solidify | 0.458 / 0.561 | 0.337 | 0.478 | Repair, 0 / 0.010 | the same with an open input |
| b (wrong stage, no area) | `box_flipped_third`: the two x faces reversed (a closed box; 19 % of the triangles, not a third: the name is kept because result files use it; 3.4 % of the edges inconsistent) | **AlphaWrap** | 0 / 0 (1.0 at tau 1e-4: moved by the offset d/3000) | 0 | 0 | Repair, 0 / 0 | a clean closed solid goes to the last-resort wrap: IoU 0 against the reference |
| | `open_tube` (the box without its two x faces) | Solidify | 0.007 / 0.194 | 0 | 0 | ExactResolve, 0 / 0.189 | caps (the added share is the two end faces) |
| | `open_box_3faces` (three faces missing; winding number at the centre 1/2) | Solidify | 0.035 / 0.417 | 0.024 | 0.046 | ExactResolve, 0.059 / 0.407 | caps; `Solidify` cuts back along the open rim (3.5 % lost) |

Sweep of the flipped share (`results/lost_repro_flip_sweep.jsonl`, the inconsistent shares are its field `edges_inconsistently_oriented_share`; share of reversed triangles -> share of inconsistent edges): 0.5 % -> 0.6 %: Repair, IoU 0.9993; 2 % -> 4.2 %: Repair, IoU 0.9955; 5 % -> 8.9 %: **Solidify**, IoU 0.9898 (just under 0.99), 2.4 % lost / 10.6 % added;
10 % -> 17 %: Solidify, 6.0 % / 22 %; 20 % -> 33 %: Solidify, 20.2 % / 51.6 % (outside 9.3 %). `MinVolumeIoU = 0` gives 0 lost at every share. The stage flips between 2 % and 5 % of reversed triangles and the damage grows from there; 100040 has 22 % inconsistent edges.

The rule that drops each: for (b) the volume check against a reference of inconsistent orientation, then the contour of that field by `Solidify` (iso 0.5, cell diagonal / 128); for (c) the winding number definition of the solid, in `ExactResolve`; for the thin plate the
resolution of `Solidify`. No stage is wrong on an input that is a consistently oriented solid. What is wrong is the choice of the stage when the input's winding number is no solid.

## 8. Relation to the F2 plan (PR #1257) and to F1

F1 (`MeshRobustRepairLostParts`, built) reports whole separable parts that the result does not reach; F2 (parked by the owner on 2026-11-17, plan in PR #1257) would *keep* small separable parts in scenes of very different scales (per-part
references in the rounding and the repair, the 29 lost-part shapes of the scale-free study). This note is not that: none of the five meshes has a scale disparity or a lost small separable part (the whole components lost in 42331, 37731 and 1038438 lie inside other parts),
and 100040's loss is a choice of stage. Nothing in the F2 plan covers (b) or (c). Two points of contact: (1) a lost-outside-area report is a stronger acceptance measure for F2 than the part check (it also sees a part that is partly present, the fin), and the F2 acceptance
"small separable parts preserved" could use it; (2) F2's stop rule (no more than 2 % more solidify or wrap fallbacks) should count the open-input fallbacks that the volume check already causes
([INSIDE_OUTSIDE_OPEN_INPUTS.md](../design/INSIDE_OUTSIDE_OPEN_INPUTS.md) section 1: 63 of 66 "volume differs from the input; input: not closed (winding number)" fallbacks are open inputs), the class of 100040 and 66640 here.
That note's spike (resolve open inputs by the generalized winding number) stopped at 3 of 111; it tried to improve the decision, this note questions whether the open input's winding solid should be the reference at all.

## 9. Prior art (short; from memory, nothing was fetched in this session)

- **fTetWild / TetWild** (Hu et al. 2018, 2020): an envelope of tolerance epsilon bounds the output *to* the input (the output surface stays within epsilon of the input: our "added"), not the input *from* the output. Input surface that the winding-number
  based inside / outside labelling removes (interior, doubled, open sheets) is dropped without a report. No lost-area figure.
- **MeshFix** (Attene 2010): keeps the input triangles, removes what is degenerate or intersecting and fills holes; closest to a "what was removed" report, but not a solid-boundary operation.
- **PolyMender** (Ju 2004) and winding-number repair (Jacobson et al. 2013; Barill et al. 2018): the solid is the volume decision, the surface is the contour of the field; area off the contour is dropped by definition and the papers do not report it. The generalized winding number
  is ill-defined (values near 1/2) for open or inconsistently oriented input, the case of 100040.
- So the practice is: bound the output against the input, drop the rest by the volume decision, report nothing about the dropped area. The project's planned `SolidBooleanPipeline.Verification` ("no invented surface") is the same one-sided check. A lost-area
  counterpart is a plain sampled distance, not a new method.

## 10. Options and leaning

| option | what | cost | risk | what it fixes |
|---|---|---|---|---|
| O1 | **Report**: `LostAreaShare` and `LostOutsideShare` (and maybe the visible share) in `MeshRobustRepair`, 60000 stratified samples, deterministic, tau a fraction of the diagonal (1e-3; the shares move little below it), no change of `Apply()` | 1 day with tests (invariance, reuse, determinism) | none for results; new public fields (API compatible) | (b) and (c) become visible: 100040 72 %, fin 6.8 %, against at most 2.6 % on the controls; the by-design meshes show 0.65 to 1.3 % and 66640 (2.6 %) is not separated: report the added share with it |
| O2 | **Acceptance**: `Apply()` false (as the F1 reasons) when `LostOutsideShare` exceeds a threshold (5 %) or the added share does (about 5 %: 66640 16 %, controls at most 1.9 %), result kept in `Mesh` | O1 + 0.5 day + an 800-mesh run to set the thresholds (the 5 % of the lost-outside share rests on one case, the fin at 6.8 %, against 2.6 % of 66640 and 1.3 % of the by-design 1038438) | false alarms on corpus meshes above 5 % that are fine; changes the contract for callers | (c): the fin is reported, not repaired |
| O3 | **Reference guard**: if the input's winding number field has no clear inside (between-values or inconsistent edges above a limit; 0.63 / 0.22 for 100040 against at most 0.18 / 0.011), do not reject the repair result by the volume IoU, or take the IoU against the input with consistent orientation, or arbitrate the stages by the O1 measure (the first result whose lost-outside and added shares are small) | 2 to 4 days with the 800-mesh A/B of the repair benchmark | changes the path of the open-input fallbacks (63 of 66 in the 2026-11-02 count): high; needs the corpus run first | (b): 100040 from 72 % / 57 % to 0.3 % / 0; also 66640 (2.9 % / 16.3 % to 0 / 0.000) |
| O4 | **Solidify's thin parts**: nothing (documented: "features smaller than the grid cell size are lost"); O1 reports it | 0 | | the thin plate through `ExactResolve = false` |

**Leaning.** O1 now: cheap, changes no result, and makes the loss visible in every result; the 31 inputs here already give the separation. Then, before O3, an upper-bound check on the 800-mesh corpus with O1's measure
(of the fallbacks "volume differs from the input; input: not closed", how many have a repair resolve with a lost-outside share below 5 % and an added share below 1 %: that is the possible gain, and the rate of results the guard would change);
O3 only if it is large. O2 only on the owner's decision about the contract, as an opt-in (`MaxLostShare`, default off). The by-design loss of (a) needs no change, only the report: a caller that wants interior walls kept cannot have a clean solid.

## 11. What was not verified

- The residual of 42331 (19 % of its lost area is skin, 8 % outside the result's solid, nothing visible), and the 66640 case (its lost-outside share does not flag it; only the added share does).
- The cause of the visible skin loss of 1038438 (1.2 % of its area) and 66112 (0.65 %): consistent with triangles that straddle intersection curves, not checked triangle by triangle.
- Why 66640, 42293 and 73429 (`Solidify`, inconsistent edges 0.08 to 1.1 %) fail the IoU: only 100040 and the flipped boxes were traced to the reference. A mesh of the control (45341) shows 13056 STL facets against 4348 triangles; not explained, it is a control with no loss.
- The threshold 5 % of O2 and the tau 1e-3 of O1 come from 31 inputs; the 800-mesh corpus was not run. Whether the visible-share criterion adds anything over the "outside the result's solid" criterion was not tested beyond these inputs.
- Visibility is an upper estimate for open inputs (an open input is "visible" from outside through its holes); rays start at tau from the centroid, 64 directions.
- `ExactMeshBoolean.Resolve` stands in for the repair's own resolve in the "stage alone" columns (orientation by area against by nesting); they differ on 42331 (0.654 against 0.162).
- No timings are claimed (shared machine). No test suite was run (the machine was in use); the probe builds, and its changes are measurement code only; no library change.

