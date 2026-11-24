# Sheet extension: spike on procedural inputs, 2026-11

Spike of [SHEET_EXTENSION.md](../design/SHEET_EXTENSION.md), section 10 step 2: the construction of section 4 built with public
members only (no library change) and run on the procedural cases of section 8, with the mutations of the construction's rules by hand.
The part "on the owner's inputs" is run by another session locally and is not in this note.

- Update (helper PR): the construction described here is now the library's `gs.MeshSheetExtension`; [benchmarks/SheetExtensionProbe](../../benchmarks/SheetExtensionProbe/README.md) runs the library through an adapter, with the same cases and outcomes (111 rows, 128 invariance runs). The note below is the spike as it was run; the file names `SheetExtension.cs` and the "public members only" statements refer to the spike.
- Library: master `f11323390` (2026-11-24); design branch `design/sheet-extension`.
- Probe, cases and the raw output of every table: [benchmarks/SheetExtensionProbe](../../benchmarks/SheetExtensionProbe/README.md)
  (`results/cases.md`, `cases.jsonl`, `mutants.md`, `cost.md`). The construction is one file, `SheetExtension.cs`.
- Machine: one Windows workstation, .NET 10, single runs for the cases, medians of 5 for the cost; read the ratios, not the milliseconds.

## 1. Result

The construction works on every class of case of section 8, curved sheets included. 111 rows (35 cases in the identity frame, the 19 that
are continued or left alone also in four more frames) end as the design says, after two corrections of the design's own statements: the
test for an inverted quad (item 1 of section 5) and the free pin (item 5). Every continued cut has the analytic volume, to
`6e-16` of the workpiece in the identity frame, `2e-11` in the translated frames (up to `5.7e6`) and `1e-16` in the rotated frame for planar and bent sheets. The three curved sheets (a cylinder segment,
a spherical dome, a shallow tray) are continued and cut; the cut differs from the smooth surface the sheet stands for by 24 % to 57 % of
`U`, so `U` held as an estimate (it is not a bound). Repeated runs, and scalings by `2^-7`, `2^5` and `2^20`, give the same extended sheet bit for bit (128 runs).

What does not work as the design says: a free pin is refused by the curve rule only if it stays clear of the outer edge (section 5.5); several of
the refusal rules are redundant with each other or with the sheet role's own checks (section 4), so most single mutants of them end in
another refusal, not in a wrong cut; and the strip at a curved rim's corner is not unique, it depends on the points of subdivision, by about `1e-7`
of the workpiece in the rotated frame (item 6 of section 5). No class of case needed a workaround with reflection or a library change.

## 2. Method

- **Construction** (section 4 of the design, [SheetExtension.cs](../../benchmarks/SheetExtensionProbe/SheetExtension.cs)): rim edges that meet the interior
  (ray hits along the edge, the winding number at the middle of each stretch), a unit direction per rim vertex, the ray to the first exit,
  the overshoot, one quad per edge as a fan, the subdivision of an outer segment that touches the workpiece, then the verification:
  depth limit, grazing exit, new points strictly outside, no strip triangle turned against the sheet, the curve rule (one `ExactMeshImprint`
  with `LabelCurves` per run, curves counted as connected components of `CurveEdges`), no coplanar contact, no intersection of the strip with
  the sheet, itself or another tool (`MeshRepairMetrics.SelfIntersectionPairs`, exact triangle tests), and the sheet role's cut of the extended sheet.
  Constants and every open choice of the design are options with defaults, listed in the probe's README.
- **Cases:** 35 procedural cases with their expected outcome and, where one exists, the analytic kept volume (a hand formula, an independent closed
  intersection for the sphere, or an independent integral over the extended sheet for the curved ones). Frames: the identity, translations of about
  `1e2`, `6e5` and `5.7e6`, and a rotation about a skew axis. "Wrong" means: an accepted cut whose kept volume differs from the analytic one, or an
  accepted cut where the design says refuse.
- **Mutants:** each refusal rule switched off by an option, the first exit replaced by the last, the overshoot rule by a fixed length, the
  choice by edge by the choice by vertex, the subdivision off, the quads turned, and five combinations. Each is run on all 35 cases (identity frame).

## 3. The cases

Identity frame. `g / D` is the largest depth relative to the sheet's bounding-box diagonal, `U` the direction estimate (volume) and
its ratio to the smaller of the two parts of the cut, `curves` the curve components / the distinct `CurveId` values of the strips.
A blank column: the construction stopped before. Expected outcome and reason are in `results/cases.md`; every row below ended as expected.

| case | expected | actual | g / D | U | U / smaller | kept | analytic | curves |
|---|---|---|---|---|---|---|---|---|
| planar 0.1 % | extended | extended | 0.0010 | 0.0001 | 1.6e-07 | 500.0000 | 500.0000 | 1/1 |
| bent 0.1 % | extended | extended | 0.0010 | 0.0001 | 1.9e-07 | 462.5000 | 462.5000 | 1/1 |
| planar 0.5 % | extended | extended | 0.0050 | 0.0022 | 4.4e-06 | 500.0000 | 500.0000 | 1/1 |
| bent 0.5 % | extended | extended | 0.0052 | 0.0025 | 5.3e-06 | 462.5000 | 462.5000 | 1/1 |
| planar 2 % | extended | extended | 0.0200 | 0.0359 | 7.2e-05 | 500.0000 | 500.0000 | 1/1 |
| bent 2 % | extended | extended | 0.0209 | 0.0400 | 8.6e-05 | 462.5000 | 462.5000 | 1/1 |
| planar 8 % | refused: DepthAboveLimit | refused: DepthAboveLimit |  |  |  |  |  |  |
| bent 8 % | refused: DepthAboveLimit | refused: DepthAboveLimit |  |  |  |  |  |  |
| slit ends in the middle | refused: DepthAboveLimit | refused: DepthAboveLimit |  |  |  |  |  |  |
| slot and fin (kept 506, fin untouched) | extended | extended | 0.0271 | 0.0533 | 1.1e-04 | 506.0000 | 506.0000 | 1/1 |
| free pin in the slot | refused: CurveRule | refused: CurveRule | 0.0271 |  |  |  |  |  |
| thick pin touching the outer edge | extended (routed round) | extended | 0.0271 | 0.0533 | 1.1e-04 | 506.1800 | 506.1800 | 1/1 |
| rib on the slot wall | refused (outer edge, inverted quad or curve rule) | refused: InvertedQuad |  |  |  |  |  |  |
| free pin whose top lies in the strip's plane | refused (coplanar contact or curve rule) | refused: CoplanarContact | 0.0271 |  |  |  |  |  |
| wide pocket (concave, re-entry) | extended | extended | 0.0293 | 0.0461 | 1.2e-04 | 400.0000 | 400.0000 | 1/1 |
| notch narrower than the depth | refused: InvertedQuad | refused: InvertedQuad |  |  |  |  |  |  |
| rim outside, next to another part (U) | unchanged | unchanged |  |  |  | 530.0000 | 530.0000 |  |
| bowtie vertex on the rim | refused: BowtieVertex | refused: BowtieVertex |  |  |  |  |  |  |
| chord across a sphere | extended | extended | 0.0227 | 0.0213 | 9.6e-05 | 221.1559 | 221.1559 | 1/1 |
| rim vertex exactly on the surface | extended | extended | 0.0109 | 0.0092 | 1.8e-05 | 500.0000 | 500.0000 | 2/2 |
| exit at 3 degrees | refused: GrazingExit | refused: GrazingExit |  |  |  |  |  |  |
| exit at 20 degrees | extended | extended | 0.0253 | 0.0416 | 1.2e-02 | 3.4343 | 3.4343 | 1/1 |
| short on four sides (rays through box edges) | extended | extended | 0.0319 | 0.1165 | 2.3e-04 | 500.0000 | 500.0000 | 1/1 |
| octahedron (rays through its vertices and edges) | extended or refused, never wrong | extended | 0.0186 | 0.3618 | 5.4e-04 | 666.6667 | 666.6667 | 1/1 |
| ray grazes a reflex edge | refused: NewPointNotOutside | refused: NewPointNotOutside |  |  |  |  |  |  |
| sheet in the plane of a step face | refused: NewPointNotOutside | refused: NewPointNotOutside |  |  |  |  |  |  |
| cylinder segment 0.5 % | extended | extended | 0.0062 | 0.0037 | 8.2e-06 | 444.1905 | 444.1905 | 1/1 |
| spherical dome (stated sheet) | extended | extended | 0.0337 | 0.1042 | 2.2e-04 | 518.4339 | 518.4339 | 1/1 |
| shallow tray (stated sheet) | extended | extended | 0.0254 | 0.1734 | 6.3e-04 | 724.4160 | 724.4160 | 1/1 |
| several sheets, one short | extended | extended | 0.0053 | 0.0025 | 4.9e-06 | 500.0000 | 500.0000 | 1/1 |
| strip crosses another sheet | refused: MeetsOtherTool | refused: MeetsOtherTool | 0.0053 |  |  |  |  |  |
| folded sheet, strips converge | refused (strip meets itself or the sheet) | refused: MeetsItself | 0.0056 |  |  |  |  |  |
| `UncutSide` Outside / Inside, no uncut part | extended | extended | 0.0053 | 0.0025 | 4.9e-06 | 500.0000 | 500.0000 | 1/1 |
| `UncutSide` Outside, two shells | refused: SheetRoleRefused | refused: SheetRoleRefused | 0.0053 |  |  |  |  |  |

Notes on single rows:

- **Curved sheets and the intent.** The kept volume against the volume of the smooth surface (the sheet's own facets where the sheet is, the smooth
  surface beyond its rim): cylinder `+0.0021` (U `0.0037`), dome `-0.0252` (U `0.1042`), tray `+0.0746` (U `0.1734`). The cylinder, dome and tray are
  continued by the planar strip of each rim triangle; at the corners of the dome and the tray the strip is a wedge between two rays.
- **Frames.** The 76 rows of the other frames end as the identity row. Largest volume error over the workpiece: `1e-15` (shift `1e2`), `6e-12` (`6e5`),
  `2.3e-11` (`5.7e6`), `1.3e-7` (rotated, only the dome and the tray; item 6 of section 5). The `g / D` of the rotated frame is a quarter to a third smaller: the
  diagonal of the bounding box is not rotation invariant (item 2).
- **Plain sheet role.** In every continued case the sheet role alone refuses the unextended sheet with `RimInsideWorkpiece`.
- **Several sheets.** The sheet role names the short sheet (`SheetWitnessMesh`); only that one is continued; the other is an `OtherTools` entry and the strip may not meet it.

## 4. The mutants

Every mutant, all 35 cases, identity frame (`results/mutants.md` has the full lists). "Wrong accepted": a cut that is accepted although the design says
refuse, or has the wrong volume.

| mutant | what the cases show | wrong accepted cut |
|---|---|---|
| depth rule off | planar 8 %, bent 8 % and the slit (depth 35 % of the diagonal) are continued | yes, against the 5 % limit; the volumes are those of the planar continuation (500, 462.5), so the limit is a policy, not a correctness rule |
| grazing rule off | exit at 3 degrees is continued (kept 23.75) | yes, against the rule; the cut of this planar sheet is right, the rule protects the claim about `U` |
| new-point test off | the reflex edge and the step face end in later refusals (`DepthAboveLimit`, `OuterEdgeInside`): the new point inside the material gives a strip whose rim is inside, which the next layers refuse | no: redundant for correctness, it gives the reason |
| inverted-quad test off | the notch ends in `MeetsSheet`, the rib in `MeetsItself` | no: redundant with the self-intersection test |
| self-intersection test off | the folded sheet ends in `SheetRoleRefused`: the sheet role itself refuses a strip that crosses the sheet inside the workpiece | no: redundant with the sheet role for a crossing inside; a crossing outside the workpiece (harmless to the cut) is the only thing the rule adds |
| inverted-quad and self-intersection tests both off | the notch and the rib end in `SheetRoleRefused` | no |
| outer-edge test off | no case changes | no: redundant with the layers behind it (no case of this set needs it) |
| curve rule off | the free pin is accepted, kept 506.015: the strip passes through the pin and cuts its top off | yes (case: free pin in the slot) |
| coplanar test off | the pin top ends in `CurveRule` | no: redundant with the curve rule in this case |
| other-tool test off | the strip crosses the second sheet, accepted (kept 497.5) | yes (case: strip crosses another sheet) |
| last exit instead of first | slot and fin, thick pin, wide pocket, the reflex edge, the step face and the two shells are refused (`CurveRule`, `DepthAboveLimit`) | no: wrong outcome, refused instead of continued (6 cases) |
| last exit and curve rule off | slot and fin accepted, kept 491 against 506: the fin's top is cut off, section 11 of the design reproduced; the thick pin (491.09 against 506.18), the free pin and the rib too | yes (4 cases) |
| fixed overshoot instead of the adaptive rule | slot and fin, both pins and the rib end in `NewPointNotOutside` | no: wrong outcome (5 cases); with the new-point test and the curve rule off they end in `OuterEdgeInside` |
| choice by vertex instead of by edge | the chord (both ends outside, no vertex of the rim inside) is not chosen and the sheet role refuses it | no: wrong outcome (1 case) |
| no subdivision of the outer edge | every case with a rim edge that has an end outside is refused with `OuterEdgeInside` | no: wrong outcome (19 cases: the planar and bent sheets from 0.5 % up, the slot, the chord and others) |
| quads turned against the sheet | all 19 continued cases end in `SheetRoleRefused` | no: the sheet role refuses an inconsistent orientation itself |
| new-point test and curve rule off | the free pin is accepted again (506.015) | yes |

Reading. Single faults of the verification do not give a wrong cut because the rules overlap and the sheet role's own checks are behind them. The rules that carry weight, in
the sense that a case gives a wrong accepted cut without them: the curve rule (the free pin, and the last exit once the strip crosses a second wall), the strip against another tool, and the two policy
limits (depth, exit angle). Without the curve rule the first exit is not enough: the pin sits between the wall and the outer edge, where the first exit and the overshoot cannot see it.
No case needs the outer-edge test, the coplanar test or the self-intersection test alone; they are cheap, and the self-intersection test and the other-tool test also cover crossings
that the sheet role would accept (outside the workpiece, or with another tool). The cases for the three rules without a kill of their own are the next step if they are to stay in the design.

## 5. Where the design was ambiguous or wrong

Each item is a correction or a decision for the design (marked in `SHEET_EXTENSION.md` as "corrected by the spike").

1. **The inverted-quad test (section 4.5) is wrong as written.** `(B' - A') . (B - A) > 0` holds for a plain quad but not for the segments of a subdivided outer
   edge at a corner: the segment from the corner's new point to the first point of its neighbour's fan runs nearly across the rim edge and can go slightly backwards (`-0.05`
   of the rim length in the rotated slot case) although nothing folds, and the verdict depended on the frame through `o_max`. Used instead: every strip triangle's normal has a cosine
   above `1e-9` with the normal of the sheet triangle it continues. This catches the notch (the quad folds over) and nothing that is merely oblique.
2. **The measure of the sheet is not frame invariant.** The depth limit (5 %) and `o_max` are shares of the bounding-box diagonal; a rotation changes the
   diagonal (the `g / D` of the rotated rows is a quarter to a third smaller), so a rim near the limit is continued in one frame and refused in another. Axis permutations and 2^k scalings keep it.
   The design needs a rotation-invariant length (the radius of the smallest enclosing sphere of the sheet, or the largest distance of two rim vertices), or must say that it is the box's.
3. **"Strictly outside by the exact winding number" (section 4.5) cannot see the surface:** the winding number is symbolically perturbed, so a point on a face gets 0 or 1. The
   step-face case (the ray runs in the plane of a face after its exit) gives new points on the surface. Used: the exact winding number is 0 and no triangle of the workpiece contains the point
   (`ExactTriangleIntersection.SegmentIntersectsTriangle` of a degenerate segment).
4. **The subdivision is the main path, not a repair.** Every sheet that is larger than the workpiece at the sides has rim edges with an end outside (`g = 0`: no ray, no new point), so the
   outer segment starts at the vertex itself and passes through the workpiece until it is subdivided; 19 of 35 cases need it (the no-subdivision mutant). The design leaves open where to refine and when to give up.
   Chosen: the middle of the longest stretch of the rim edge inside the workpiece between the two points; the same at `0.414` and `0.586` when the new point is not strictly outside (a point that lands exactly on
   a face of the workpiece, as in the rotated thick-pin case, is no exit); at most 14 levels. The direction at a point of subdivision is the interpolation of the end directions.
5. **The free pin (section 8) is refused by the curve rule only for a thin pin.** A pin that reaches the outer edge touches the outer segment, so the subdivision routes the outer edge between wall and
   pin and the strip does not touch the pin: continued, kept 506.18, the pin untouched, which is right. A pin that stays clear of the outer edge (x 9.51 to 9.515 against an outer edge at 9.525) is pierced by the strip:
   refused by the curve rule (two components), and accepted without it, with its top cut off. A rib on the wall cannot be routed round by a monotone strip and ends in the inverted-quad test. So the statement
   "refused by the curve rule" holds for the thin pin, and the case list needs both pins and the rib.
6. **The strip at a curved rim's corner is not unique.** The quad between a corner's ray and its neighbour's is not planar; its triangulation follows the points of subdivision, which follow the frame
   (rounding of the rotated input). The cut differs by `1e-7` of the workpiece (rotated dome and tray), far below `U`, but "bit for bit" holds for the 2^k scalings and repeated runs only, not for general rotations.
7. **"Counting the distinct curve ids" (section 4.5):** `ExactMeshImprint.CurveId` joins edges only at vertices where exactly two curve edges meet, so a curve that branches at a vertex (a ray through a
   vertex of the workpiece) would count twice. The probe counts connected components of `CurveEdges` and reports the number of ids too; they were equal in every case here (the octahedron, with rays through its vertices, gives 1 and 1),
   so the difference is not shown by any case. The design should say components.
8. **"The edges the refusal names" (section 4.1)** are one witness, not the rim. The choice by edge needs all rim edges classified; composed from `FindAllHitTriangles` along the edge and winding numbers at the middle of each stretch
   (doubles; a vertex within 64 rounding units of the surface counts as on it). A misclassification can only lead to a refusal by the verification or by the sheet role, not to a cut that the exact checks do not see; this is argued, not proven.
9. **`UncutSide` cannot be shown to matter.** Passed through to the cut it changes nothing in any case: a single-shell workpiece cut by a sheet whose rim ends inside has no uncut part that I could build, and with several shells the
   sheet role refuses the second shell whatever `UncutSide` says (the extension's work is lost). If that is the rule in the pipeline, a check before the extension saves it.
10. **A vertex of the rim on the surface in the middle ends two runs** and the curve rule counts per run: 2 components in total (one per run), as the design says; the new point is the vertex itself, there is no degenerate triangle.
11. **The overshoot:** the adaptive rule is needed (fixed overshoot makes the slot cases refuse); it is also what keeps the strip off the coplanar and the next surfaces. A ray that lies in the plane of a face sees no hit on that face (the
    perturbation convention of the ray query), so `o` is `o_max` there and the new point is on the face: caught by item 3.
12. **Order of the checks.** The new point is verified where it is made (inside the ray), so a failing point of subdivision can be retried; the design lists the verification after the construction.

## 6. What the construction costs

`results/cost.md`: a sphere of 2028, 10092 and 41772 triangles, a planar sheet of 288, 1152 and 4608 triangles with its far rim inside the sphere, a strip of 6, 14 and 26 triangles; medians of 5 runs after a warm-up.

| sphere triangles | construct ms | verification ms (without the imprint) | imprint ms | cut ms | imprint / cut |
|---|---|---|---|---|---|
| 2028 | 2.5 | 2.1 | 4.1 | 27.4 | 0.15 |
| 10092 | 9.4 | 1.9 | 13.5 | 98.6 | 0.14 |
| 41772 | 41.9 | 5.0 | 30.0 | 270.4 | 0.11 |

The imprint of the strip by the workpiece (the curve rule) costs 11 % to 15 % of the sheet cut; the construction is dominated by the box tree of the workpiece (a pipeline has one) and by the ray queries.
The whole extension (construct, verify, imprint) is about 30 % of one cut at the largest size; it comes on top of the refused attempt (one cut) and is followed by the accepted cut (one more), so a pipeline
pays up to two cuts and a fraction for a continued sheet. The whole `all` run (cases, mutants, cost) takes about 20 s.

## 7. Go / no-go, as far as procedural inputs answer it (section 10)

- Every procedural through cut within the limit gives the analytic volume: yes (planar, bent, curved, chord, vertex on the surface, rays through edges and vertices, several sheets, translated and rotated frames).
- Every adversarial case of section 8 ends as stated there: yes, with the two corrections of section 5 (the inverted-quad test, the pin).
- No accepted wrong cut: none with the rules on. With one rule off, a wrong accepted cut needs the curve rule, the other-tool test or one of the two policy limits; the strip's other tests are redundant with each other and with the sheet role.
- The construction is deterministic, scale covariant, and costs about a third of a cut.
- Procedural verdict: **go** for the helper (deliverable B) on these inputs. Open before a helper PR: the measure of the sheet's size (item 2 of section 5), the verdict of the redundant rules (keep for cost or reasons), and what the helper does at a non-manifold rim vertex.

## 8. On the owner's inputs

To be filled in by the local run of the probe (`file` mode and the harness that compiles `SheetExtension.cs`): the number of `RimInsideWorkpiece` refusals, how many of them stated sheets and how many auto-routed tools, the distribution of the
rim's depth relative to the diagonal, the share within the limit, and how many of those are continued and cut right against a reference. No numbers here.

## 9. Not verified

- Anything on real inputs; large sheets (the largest is 4608 triangles against a workpiece of 41772); a random or fuzz corpus.
- Rotated or translated frames of the degenerate cases (a vertex on the surface, the octahedron): rounding moves the exact contact, so they were not run.
- Rays through a vertex of the workpiece only in the octahedron case (identity frame); a ray through a vertex that branches the curve is not covered.
- General rotations, mirror images and the axis permutations of `Similarity.All()`: the bit-for-bit claim is for the scalings by `2^k` only; the construction is not rotation invariant (items 2 and 6 of section 5).
- A rim vertex shared by more than two rim edges is only refused (no per-wedge points), non-manifold sheets are the sheet role's business.
- The pipeline integration, the verification option and the diagnosis (deliverable A) are not touched.
- The redundancy of the outer-edge, coplanar and self-intersection tests is shown on these cases, not in general.

Reproduce: `dotnet run -c Release --project benchmarks/SheetExtensionProbe -- all`; `selftest` is the regression check (exit code 1 if a case does not end as expected).
