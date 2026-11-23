# A Difference with an open cutting surface: semantics, 2026-11

Study for #1211: a Difference whose second operand is an open sheet (a cutting surface) returns the workpiece unchanged
and reports success. This document defines the candidate semantics, measures them on procedural cases and gives a
recommendation. No library code is changed; the semantics, any new option and any changed default are the owner's decision.

- Library: master `e875d747` (2026-11-22). The sheet role of the open PR #1223 (head `2f3f33ec`) is measured from its
  branch and not edited.
- Prototype and the raw output of every table: [benchmarks/OpenCuttingSurface](../../benchmarks/OpenCuttingSurface/README.md)
  (`results/master.txt`, `results/sheet-role-pr1223.txt`).
- Not done: no Thingi10K run, no frames other than the identity (the invariance of B is tested in #1223), no mesh built for
  candidate C (it is sampled).

## 1. Summary

- **No established system defines the plain Boolean difference with an open sheet** (section 3). They refuse it and offer a
  separate split or trim operation, in which either all pieces are returned or the side comes from the sheet's orientation
  under a full-crossing precondition.
- **Recommended cut semantics: B, the oriented sheet with exact cell labelling**, which is what PR #1223 builds: right on all
  8 cases with a defined intent (volume and side), a named refusal on the 3 cases where the sheet ends inside the solid, no
  new predicate. As the PR is, it covers single-shell workpieces (a cut through a hollow body is refused). Two points of its
  contract are open for the owner (section 7): a sheet that only touches the workpiece removes all of it with `Success`,
  and the side convention differs from the wording of #1211.
- **D, the sheet swept to a closed tool, is a cheap complement** for the cases B refuses (a sheet ending inside: a step, a
  slot, a blind pocket). It needs no exact code (`MeshExtrudeMesh` builds the tool, then the closed boolean) and took 0.5 to
  0.8 of B's time. It
  is a different operation (an extruded cut along a direction), so it should be explicit, not a fallback.
- **C, the generalized winding number, is not a cut semantics**: the threshold 1/2 is today's behaviour (wrong on 6 of 8);
  the sign rule ignores where the sheet ends, and it has no mesh where the rim or the level set `w = 0` lies inside the
  workpiece (where neither does, it is B's result).
- **The silent success is a separate decision** (section 8). The pipeline's default (`Refuse`) is no longer silent. Silent
  today: `ExactMeshBoolean` with its default `AllowOpenMeshes = true` (13 of 13 `Success`, 6 of 8 wrong, an open result on a
  curved case) and the pipeline's `AutoRepair` modes (13 of 13 `Success` and `Clean`, 6 of 8 wrong). Leaning: a report field
  now (no behaviour change), and the default `AllowOpenMeshes = false` as an owner decision once the sheet role is merged.

## 2. Today, measured

Workpiece: the cube `[0,4]^3` (volume 64). `intent` is the volume of the cut by the sheet's sides, with the convention of
#1223: the tool is behind the sheet (the side its normals point away from). `-`: the sheet does not define one.
Command: `dotnet run -c Release --project benchmarks/OpenCuttingSurface -- today` (`results/master.txt`).

| case | intent | `ExactMeshBoolean` (open, w > 1/2) | pipeline `Refuse` | pipeline `AutoRepair` (and `ThenRobust`) | `MeshRobustRepair` of the sheet, then closed Difference | `MeshPlaneCut` + `FillHoles` | `ExactMeshImprint` |
|---|---|---|---|---|---|---|---|
| plane z = 2, normals up | 32 | Success 64 | OpenInput | Success, clean, 64 | Success 59.206 | 32 | 8 curve edges, 2 regions |
| plane z = 2, normals down | 32 | Success 64 | OpenInput | Success, clean, 64 | Success 59.206 | 32 | 8, 2 |
| tilted plane z = 1 + x/2 | 32 | Success 64 | OpenInput | Success, clean, 64 | Success 58.002 | 32 | 8, 2 |
| bowl (12 x 12 grid) | 37.25 | Success 64 | OpenInput | Success, clean, 57.573 | Success 57.385 | - | 36, 2 |
| bucket, opening above the cube | 52 | Success 52 | OpenInput | Success, clean, 52 | Success 52 | - | 8, 2 |
| rim on the surface (z = 2 on `[0,4]^2`) | 32 | Success 64 | OpenInput | Success, clean, 64 | Success 60.924 | 32 | 8, 2 |
| half sheet (z = 2, x in [-1, 2]) | - | Success 64 | OpenInput | Success, clean, 64 | Success 61.587 | 32 | 6, 1 |
| sheet with a hole `[1,3]^2` | - | Success 64 | OpenInput | Success, clean, 64 | Success 63.909 | 32 | 16, 2 |
| disc inside (z = 2 on `[1,3]^2`) | - | Success 64 | OpenInput | Success, clean, 64 | Success 63.604 | 32 | 0, 1 |
| on the top face (z = 4), up | 0 | Success 64 | OpenInput | Success, clean, 64 | Success 61.601 | 0 | 5, 3 |
| on the top face (z = 4), down | 64 | Success 64 | OpenInput | Success, clean, 64 | Success 61.603 | 64 | 5, 3 |
| patch on the top face (`[1,3]^2`), up | - | Success 64 | OpenInput | Success, clean, 64 | Success 63.802 | 0 | 5, 3 |
| outside (z = 6), up | - | Success 64 | OpenInput | Success, clean, 64 | Success 64 | 0 | 0, 1 |

- The plain boolean and both repair modes are right on 2 of the 8 cases with an intent (the bucket, whose winding number
  is above 1/2 in the pocket, and the sheet facing away on the top face) and wrong with `Success` on 6. The bowl under
  `AutoRepair` gives 57.573: neither the workpiece nor the cut.
- A repair of the sheet cannot be the cut: `MeshRobustRepair` turns a sheet into a slab or a blob (alpha wrap or solidify,
  volumes 0.2 to 13.7; only the bucket is closed to the box it stands for), and the Difference then removes a part of it (`Success`, 57.4 to 63.9 for the 11 sheets that reach the cube; the outside
  sheet 64). This is the family of cases
  that the wrap cannot rescue.
- The imprint already has the cut lines: 2 regions of the workpiece's surface in every case that crosses fully, 1 region
  (a slit) for the half sheet, no curve for the disc. It has no cap, so it gives the trimmed shell, not the solid.
- `MeshPlaneCut` (floating point) gives the half-space of a planar sheet's plane: right where the intent is that
  half-space, and it ignores where the sheet ends (32 for the half sheet, the hole and the disc; 0 for the patch).
- On a curved case (`cost`, a sphere against a wavy sheet) the plain boolean returns `Success` with a result that is
  **not closed** (volume 4.10 of 4.19, boundary edges) and takes 2 to 5 times the time of the real cut (section 6).

## 3. How others solve it

Short prior-art check (docs, manuals and papers only; no source code of CGAL, libigl or GEOS was opened). Evidence: *read*
(the primary text), *snippet* (an excerpt of it), *unverified* (forums, memory). Several vendor hosts were not reachable
from the container, so those rows are not *read*.

| system | with an open tool | evidence |
|---|---|---|
| CGAL PMP corefinement Booleans, `clip(tm, clipper)` | precondition `does_bound_a_volume` on the tool: not allowed | read (5.1 manual) |
| CGAL PMP `split(tm, splitter)` | the one operation for an open cutter: corefines and duplicates the edges on the intersection; no side, no cap, a partial crossing leaves a slit | read |
| CGAL PMP `clip(tm, plane)`, Manifold `TrimByPlane` / `SplitByPlane`, Blender Bisect, VTK clip filters, trimesh `slice_plane` | plane only; CGAL keeps the side opposite to the normal, Manifold the side the normal points to | read (CGAL), snippet (others) |
| Manifold Boolean, `Split(cutter)` | the cutter is a closed manifold; an open mesh gives status `NotManifold` | snippet |
| OpenCASCADE `BOPAlgo` Cut | "the minimal dimension of S2 should not be less than the maximal dimension of S1": solid minus face is not defined | read (7.0 guide) |
| OpenCASCADE Splitter | the way to cut a solid by a face or shell: all split parts of the objects, no side | snippet |
| NX / Parasolid | a sheet cannot be the tool of a Boolean; Trim Body cuts a solid by a sheet that must be larger than it | unverified (forum) |
| T-FLEX (Parasolid) | subtracting a sheet cuts the solid; "the sheet body must be defined as a semi-space" | snippet |
| SolidWorks, Fusion 360, Onshape split / cut with surface | the surface must cross the body completely, else the feature fails; split keeps all bodies | snippet / unverified |
| MCUT | built for it: open or closed cut mesh; fragments of the source above or below by the cut mesh's winding order, sealed or not; a partial cut is allowed | read (site, FAQ) |
| Houdini Boolean SOP | per input "Treat As" solid or surface; a surface subtracts nothing by volume, it shatters | snippet |
| Blender Boolean modifier, VTK Boolean filter | accepted, no semantics, artefacts documented or reported | snippet / unverified |
| Zhou et al. 2016 (mesh arrangements) | the input must induce a piecewise constant winding number; a lone sheet does not | snippet |
| Jacobson et al. 2013, Barill et al. 2018 (generalized winding number) | never fails; near the border of a sheet the value fades, the result there is an artefact of the threshold | read (libigl tutorial), snippet (papers) |
| regularized set operations (Requicha, Tilove) | `closure(interior(A \ B))` with a sheet B is A: a no-op | own derivation from the definition |

Four semantics exist in practice: (1) refuse or leave undefined (the most common for the Boolean itself); (2) a split
without a side, returning all pieces; (3) the sheet as a half-space with the side by its orientation, under a full-crossing
precondition (the only family in which "solid minus sheet" is a closed solid); (4) a plane-only trim. The winding number
threshold (5) never fails and is not a set operation near the sheet's border. Today's `Success` with the workpiece
unchanged is the regularized no-op, which no system offers as the answer to a cut.

## 4. Building blocks in the library

| part | what it gives for a sheet | state |
|---|---|---|
| `ExactMeshArrangement` | the exact arrangement of workpiece and sheet: all pieces, also the sheet's patches inside the workpiece (the caps) | master |
| `ExactMeshBoolean` open inputs | classification of every output piece by the generalized winding number above 1/2 (Antipodal Method, fast winding numbers), `ConflictEdges` where an open boundary meets other pieces | master; no other threshold (`WindingPredicate` sees integers) |
| `ExactMeshBoolean.radial_order` | the exact order of the pieces around an edge | master (#1218) |
| `ExactMeshCells` + `Roles` / `SheetUncutSide` / `SheetRefusal` | the cells of the workpiece split by the sheet, labelled by the sheet's sides; candidate B complete | PR #1223, open |
| `ExactMeshImprint` | the intersection curves on the workpiece (`CurveEdges`), no caps | master |
| `MeshMeshCut` | floating-point cut of two meshes along their intersection | master, not exact, obsolete |
| `MeshPlaneCut` + `FillHoles` | the half-space cut of a plane, floating point | master |
| `MeshExtrudeMesh` | an open surface and its offset copy stitched along the boundary loops: the swept tool of D | master, floating point construction |
| `MeshAutoRepair`, `MeshRobustRepair` (solidify, alpha wrap) | a closed solid from the sheet: a slab or a blob, never the tool the sheet stands for | master |
| `SolidBooleanPipeline.InputRepair` | `Refuse` (default since #1212), `AutoRepair`, `AutoRepairThenRobust` | master |

`MeshExtrudeMesh` (with `ExtrudedPositionF` as a translation and `IsPositiveOffset = false`) turns an open surface into the
closed shell of candidate D: the review of this study ran it on the plane and on the sheet with a hole and got the
prototype's results (32 and 40). Missing for D are the test that the sweep is valid, the choice of direction and length,
and a refusal status. `MeshExtrudeFaces` and `MeshExtrudeLoop` extrude a face set or one loop in place and do not apply.

## 5. The candidate semantics

W is the workpiece (closed), S the sheet (open, oriented). All candidates leave a Difference with a closed tool as it is.

### A. Refuse

- **Definition:** no cut semantics. A Difference with an open tool fails.
- **Defined when:** never. **Decided:** the closedness in the winding number sense (every edge used equally often in both
  directions), exact, already computed (`ExactWinding`).
- **Status:** `OpenInput`, with the input named (the audit's improvement 1).
- **Cost:** none. It is the pipeline's default today and the answer of CGAL and OpenCASCADE to the Boolean (also of
  Manifold and NX by the evidence of section 3, which is not the primary text).

### B. Oriented sheet, exact cell labelling (PR #1223)

- **Definition:** S is a part of the tool's outward surface. The pieces of S inside W split W into cells; a cell behind a
  piece of S (the side its normals point away from) is in the tool and removed, a cell in front of one stays. The result is
  closed: the caps are the sheet's own pieces.
- **Defined when:** every free edge of S is outside W or exactly on its surface (the sheet crosses fully), and no cell is in
  front of one piece and behind another. A rim exactly on the surface is a cut. A cell no piece touches needs a stated side
  (`SheetUncutSide`).
- **Decided exactly:** the existing arrangement, the exact radial order of the pieces around each edge, a union-find over
  the two sides of each piece. No new predicate, no bound, no threshold.
- **Status when it does not apply:** `SheetRefused` with a `SheetRefusal` and a witness: `RimInsideWorkpiece` (the half
  sheet, the hole, the disc), `SidesInconsistent` (a ring, a spiral), `UncutComponents` (no contact), `NotSurface`,
  `OrientationInconsistent`, and the role refusals (Union, Intersection, first operand).
- **Cost:** measured 1.2 to 2.0 times the closed Difference with the swept tool of D on the same inputs, rising with the
  size (section 6).
- **Limits of the PR as it is:** conservative refusals for hollow workpieces (nested shells) and floating parts; see also
  section 7.

### C. Generalized winding number of the sheet with a side rule

- **Definition:** a point p of W is removed where `w_S(p) > tau`. `tau = 1/2` is today's rule. `tau = 0` (the sign rule)
  makes a planar sheet the half-space of its plane, because the solid angle of a planar polygon has the sign of the side.
- **Defined when:** as a point set, always. As a mesh, only where the boundary `w_S = tau` inside W is the sheet itself. For
  the sign rule the boundary leaves the sheet at its rim and continues as the level set `w_S = 0`, which is not a piece of
  the arrangement: wherever the rim, or that level set, is inside W, the pieces of W are not on one side and there is no cap.
- **Decided:** for a planar sheet the sign is one `orient3d`, exact, and the result is the plane cut (the sheet's extent is
  not used). For a curved sheet the value is a sum of solid angles with an error bound, not exact, and its level set needs a
  contouring.
- **Status when it does not apply:** none by itself: it never fails. That is the silent case of today.
- **Cost:** one winding query per output piece (the open path of today: about 2 to 5 times the cut on the sphere case).
- **Measured:** `tau = 1/2` is wrong on 6 of 8. The sign rule matches the intent on the 8 (0 % of the samples wrong on the 7
  with a closed-form point set; the bowl 37.26 against 37.25, within the sampling error), and gives the full plane cut where
  the sheet ends inside: 32 for the half sheet, the sheet with a hole and the disc, and 0 for the patch on the face.

### D. Sweep the sheet to a closed tool

- **Definition:** the tool is S swept along a direction d, pointing behind the sheet, to beyond the common box: S, its
  translated copy reversed, and walls on the free edges. Then the ordinary closed Difference. With S crossing fully and
  its walls outside W this is B's result; with S ending inside W it removes everything under the sheet along d (the
  "extruded cut" of CAD systems).
- **Defined when:** S is a height field along d: no line along d meets S twice. Then the swept tool is embedded. That
  every triangle faces away from d is necessary and not sufficient: two parts of S stacked along d pass it (the review ran
  the planes z = 1 and z = 3 in one mesh: accepted, `Success`, 16), and the closed boolean then takes the union of the swept
  prisms, which is a defined result but not the one of an embedded tool. Not for a bucket or any sheet that folds back
  along d. The default direction (against the area-weighted mean normal) is a
  heuristic; a stated direction is a contract.
- **Decided exactly:** the facing test is the sign of a 3 x 3 determinant per triangle (`orient3d` with a point at
  `a + d`, or an expansion); everything after it is the closed exact boolean on a concrete mesh. The swept vertices are
  rounded doubles, so the walls are parallel to d only up to rounding; the result is the exact Difference with the tool as
  built.
- **Status when it does not apply:** a refusal naming a triangle that faces the sweep; for stacked parts, a refusal by the
  self-intersections of the tool (`SelfIntersectingPairs` of the boolean, or a check of the tool before it).
- **Cost:** the closed boolean on a tool with about twice the sheet's triangles; measured 0.5 to 0.8 of B's time.
- **Measured:** right on 7 of 8, the bucket refused (triangles facing the sweep). The step (48), the pillar under the hole (40), the
  blind pocket under the disc (56) and a through hole under the patch on the face (48): defined results that B refuses, and
  that a user may or may not mean.

### E. Split without a side

- **Definition:** all cells of W split by S, each as a closed solid; the caller picks (CGAL `split`, OpenCASCADE Splitter,
  MCUT fragments, CAD split body).
- **Defined when:** as B without the orientation: the rim not inside W. A partial crossing would be one cell with a slit,
  which is not a solid.
- **Decided exactly:** B's cells without the marks. **Status:** B's `RimInsideWorkpiece`.
- **Cost:** with #1223 as it is, two runs (S and S reversed); measured, the volumes add up to 64 in every case B accepts
  (32 + 32, 37.25 + 26.75, 52 + 12). One run with a per-cell output would be an API addition on top of B.

## 6. Measurements of the candidates

`dotnet run -c Release --project benchmarks/OpenCuttingSurface -- semantics` on the head of #1223
(`results/sheet-role-pr1223.txt`; on master the columns B and E print `n/a`). Volumes; `ok` / `WRONG` against the intent:
the volume and, where the intent is a point set in closed form (all but the bowl), the side at 216 points, because a
halving plane has the same volume on both sides. The intents 0 and 64 of the two sheets on the top face follow from B's own
rule (a coplanar sheet is not defined by any system of section 3).

| case | intent | A | B sheet role | C, w > 1/2 (sampled) | C, w > 0 (sampled) | D swept tool | E split (front + back) |
|---|---|---|---|---|---|---|---|
| plane z = 2, up | 32 | refused | 32 ok | 64 WRONG | 32 | 32 ok | 32 + 32 |
| plane z = 2, down | 32 | refused | 32 ok | 64 WRONG | 32 | 32 ok | 32 + 32 |
| tilted plane | 32 | refused | 32 ok | 64 WRONG | 32 | 32 ok | 32 + 32 |
| bowl | 37.25 | refused | 37.25 ok | 64 WRONG | 37.263 | 37.25 ok | 37.25 + 26.75 |
| bucket | 52 | refused | 52 ok | 52 | 52 | refused (a triangle faces the sweep) | 52 + 12 |
| rim on the surface | 32 | refused | 32 ok | 64 WRONG | 32 | 32 ok | 32 + 32 |
| half sheet | - | refused | `RimInsideWorkpiece` | 64 | 32 | 48 | refused |
| sheet with a hole | - | refused | `RimInsideWorkpiece` | 64 | 32 | 40 | refused |
| disc inside | - | refused | `RimInsideWorkpiece` | 64 | 32 | 56 | refused |
| on the top face, up | 0 | refused | 0 ok | 64 WRONG | 0 | 0 ok | 0 + 64 |
| on the top face, down | 64 | refused | 64 ok | 64 | 64 | 64 ok | 64 + 0 |
| patch on the top face, up | - | refused | **Success, 0** | 64 | 0 | 48 | 0 + 64 |
| outside, up | - | refused | `UncutComponents` (64 with `Outside`) | 64 | 0 | 0 | refused |

Cost (`cost`, the same run: a sphere of radius 1 against a wavy sheet over it; wall ms, median of 5 after a warm-up, 4
cores, one shared container: read the ratios). The result volume is in brackets; half the sphere is 2.094.

| sphere triangles | sheet triangles | today: open boolean | D swept tool | B sheet role | imprint only |
|---|---|---|---|---|---|
| 11 532 | 2 048 | 121 (4.101, open) | 45 (2.092) | 55 (2.092) | 27 |
| 47 628 | 8 192 | 592 (4.102, open) | 119 (2.094) | 201 (2.094) | 103 |
| 193 548 | 32 768 | 2077 (4.102, open) | 450 (2.094) | 880 (2.094) | 302 |

B and D agree on the volume. B costs 1.2, 1.7 and 2.0 times D (rising with the size; one job, three sizes), although D
has twice the tool triangles: the labelling of #1223 has room (a note for that PR, not measured further here). Today's
open path costs about 2 to 5 times either and returns a wrong, open result. The cases are one convex cube and one sphere:
no hollow or non-convex workpiece, no case with more than two cells.

## 7. Comparison and recommendation

| semantics | definition | defined when | exact | cost | existing parts |
|---|---|---|---|---|---|
| A refuse | no cut | never | yes (closedness) | none | master (pipeline default) |
| B oriented sheet | cells of W behind S are removed | S crosses fully (rim outside W or on its surface), sides consistent; in #1223 as it is, a single-shell workpiece | yes, no new predicate | 1.2 to 2.0 x D, rising with size | PR #1223, complete with tests |
| C winding number | removed where `w_S > tau` | point set: always; mesh: only where the level set is the sheet | planar: yes (it is the plane cut); curved: no | a winding query per piece (today's slow open path) | master for `tau = 1/2`; nothing for a level-set mesh |
| D swept tool | S swept along d to a closed tool, closed Difference | S is a height field along d | yes after the construction (rounded sweep vertices) | 1 x the closed boolean | `MeshExtrudeMesh` and the closed boolean; the validity check of the sweep is new (small, not exact code) |
| E split | all cells, no side | as B without orientation | yes | 2 x B today | B twice; a per-cell output is new |

How others solve it: section 3 (refusal for the Boolean, a split without a side, or the half-space by orientation under a
full crossing; plane-only trims are universal).

**Recommendation.**

1. The plain Difference with an open tool and no stated role stays refused (A) in the pipeline, and is reported or refused
   in `ExactMeshBoolean` (section 8). No system defines it, and every guess measured here is wrong somewhere.
2. The cut is B with an explicit role, as #1223 builds it: it is family (3) of the prior art, it is exact, and it refuses
   what it cannot define. Two contract points go to the owner with that PR:
   - **A sheet that only touches.** The patch on the top face (rim on the surface, no piece inside W) removes the whole
     cube with `Success`: the cube's one cell is behind the patch. By the definition that is consistent, and it is the
     same rule that makes the full top face right (0). But the patch separates nothing, so a small sheet lying on a face
     silently empties the workpiece. Options: keep; or treat a sheet with no piece in the interior of W as cutting nothing
     (`UncutComponents`, so the side must be stated). Leaning: the second, because a refusal there costs one option and
     the other direction is a silent total loss. Its price: the two full sheets on the top face have no piece inside W
     either, so they become refusals too, and B is then right on 6 of the 8 and refuses 2.
   - **The side convention.** #1211 words the intent as "the half on the sheet's back side stays"; #1223 keeps the front
     (the tool is behind the sheet, as for a closed tool's outward surface, and as Manifold's `TrimByPlane`; CGAL's
     `clip(plane)` keeps the other side). Leaning: the PR's, since it is the limit of a closed tool, stated in the guide.
3. D is worth offering only as an explicit operation with a stated direction (a helper around `MeshExtrudeMesh` that
   checks the sweep, outside the exact code), for sheets that end inside the solid. It is not a fallback of B: the hole
   case gives a pillar and the patch a through hole. Cost: small. "Not now" is a valid answer; the recipe can also be a
   documented example.
4. C is not pursued. A planar sheet under the sign rule is the plane cut, which `MeshPlaneCut` has (floating point); an
   exact plane cut would be B with a large planar sheet.
5. E follows B if a caller asks for both sides; until then two runs give it.

## 8. The silent success, as its own decision

Independent of the cut semantics: should a `Success` on an unchanged workpiece become a refusal or a report now?

Measured today (section 2 and `today`, last column):

- `SolidBooleanPipeline`, default `Refuse`: `OpenInput` in 13 of 13. Not silent. The error does not name the input or the
  ways out (the audit's F5).
- `SolidBooleanPipeline`, `AutoRepair` and `AutoRepairThenRobust`: `Success` and `Clean` in 13 of 13, wrong in 6 of the 8
  with an intent, with `RepairedInputs` as the only trace. Documented on the enum since #1212.
- `ExactMeshBoolean`, default `AllowOpenMeshes = true`: `Success` in 13 of 13, wrong in 6 of 8; on the sphere case a
  result that is not closed, still `Success`.
- A proxy of a signal, from what the boolean already reports: the tool is open, its triangles meet the workpiece's
  (`IntersectingPairs > 0`) and the result has the workpiece's volume (compared exactly, which holds for these dyadic
  cases only; S1 below would count the tool's pieces in the result instead). It flags 10 of the 13 (every sheet that
  crosses or touches: the 6 wrong ones, 3 without an intent, and the one on the top face that faces away, where "no
  effect" is the right answer); not the bucket (it
  cuts), the disc and the outside sheet (no contact). It does not see the cases where the open tool changes the result
  wrongly (the sphere: open result; the bowl under `AutoRepair`: 57.573). For those the facts are "an open operand" and
  "the result is not closed", which the boolean has.

Options:

| | change | catches | cost and risk |
|---|---|---|---|
| S0 | none (documented) | nothing | none |
| S1 | report only: `ExactMeshBoolean` counts the open operands of which no piece is in the result although they meet the first mesh, and says whether the result is closed; the pipeline's report carries it; the `OpenInput` error names the input and the ways out (`InputRoles`, `InputRepair`) | the 10 flagged cases as a fact the caller can test; no status changes | small, API additions only (pack validates) |
| S2 | the pipeline's repair modes refuse an open tool of a Difference whose repair is implausible (the audit's area ratio of at least 1.44 after the repair) | the sheet under `AutoRepair`, by a heuristic threshold | a threshold on a small sample; false refusals for solids with large missing faces |
| S3 | `ExactMeshBoolean.AllowOpenMeshes` defaults to `false` | every case: an open operand needs an explicit opt-in (the winding number rule) or a role (the sheet) | a changed default of a public class: the five audit cases where the open boolean is right without a repair then need the opt-in |

How others solve it: refusal by default (CGAL preconditions, OpenCASCADE's dimension rule, Manifold's `NotManifold`);
where open inputs are accepted without semantics (Blender, VTK), the manuals warn of artefacts.

Leaning: S1 now, since it changes no behaviour and makes the case testable; S3 as the owner's decision after #1223 is
merged, when every legitimate open input has an explicit way in (`Roles` for a sheet, `AllowOpenMeshes = true` for the
winding number rule). S2 is not recommended: a heuristic refusal where S3 gives an exact one.

## 9. Open decisions for the owner

1. The cut semantics: B (#1223) as recommended, or another.
2. In B: a sheet that only touches the workpiece (keep `Success` with everything removed, or `UncutComponents`).
3. In B: the side convention (the front stays, as the PR; or the back, as #1211 words it).
4. D as an explicit swept-tool operation: build, document as a recipe, or not now.
5. The silent success: S0, S1, S3 (or S1 then S3).

## 10. Outcome (2026-11-22/23)

Owner decisions on section 9: (1) B; (2) a sheet that only touches is refused; (3) the front side stays; both in #1223, merged.
(4) D as a documented and tested recipe without a new API: [the guide](../guides/booleans-and-imprint.md#extruded-cut-with-an-open-sheet-recipe)
and `docs/examples/SweptCutExample.cs`. (5) S1 now: `ExactMeshBoolean.OpenInputsWithoutEffect` and `OpenInput` errors that name
the inputs and the ways out; S3 (the default of `AllowOpenMeshes`) is still open. The list is defined on the classification's
decisions, not on the volume proxy of section 8: on the 13 cases it names the sheet in the 10 flagged ones and for the disc
inside the cube. It says that the result is the one of an input that encloses nothing (for a Difference tool: the result
without it), not that the result is right: an open input
that changes the result wrongly (the sphere case of section 2) is not listed.

## Sources

G. Barill, N. G. Dickson, R. Schmidt, D. I. W. Levin, A. Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG 2018.
A. Jacobson, L. Kavan, O. Sorkine-Hornung, *Robust Inside-Outside Segmentation using Generalized Winding Numbers*, ACM TOG 2013.
Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG 2016.
A. A. G. Requicha, *Representations for Rigid Solids: Theory, Methods, and Systems*, ACM Computing Surveys 1980 (regularized
set operations). Documentation only: the CGAL Polygon Mesh Processing manual (corefinement, `clip`, `split`), the
OpenCASCADE Boolean Operations user guide, the MCUT site and FAQ, the libigl tutorial, and the vendor pages listed in
section 3. The catalog entry is in [sources/exact-arithmetic-and-booleans.md](../sources/exact-arithmetic-and-booleans.md).
