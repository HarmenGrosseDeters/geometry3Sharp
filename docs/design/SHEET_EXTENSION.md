# Sheet extension: an opt-in convention for a cutting sheet that ends just short of the workpiece's surface

Status: design proposal (2026-11-24). Nothing is implemented. Section 9 lists what the owner has decided and what is
open; section 10 is the plan, which starts with a spike. User-facing behaviour today:
[booleans-and-imprint.md](../guides/booleans-and-imprint.md#open-cutting-surface-sheet-role).

## 1. Problem

The sheet role (`ExactMeshBoolean.InputRole.Sheet`, `SolidBooleanPipeline.InputRoles`, and by default every open tool of
a Difference) cuts a closed workpiece along an open surface. It refuses with `SheetRefusal.RimInsideWorkpiece` where a
free edge of the sheet lies inside the workpiece: the sheet then does not divide the workpiece, and "the side behind the
sheet" is not defined.

Observed by the owner on private inputs: a class of such refusals are sheets that are meant as through cuts and whose rim
ends a little short of the workpiece's surface, by about 0.1 % to 1 % of the sheet's size. Two neighbouring classes look
similar and are different problems:

| class | what it is | what answers it |
|---|---|---|
| flush rim | the rim is meant to lie in a face and sits a few rounding units inside it | the grid retry of `AutomaticSteps` ([PIPELINE_GRID_STEP.md](PIPELINE_GRID_STEP.md)); exact, no convention needed |
| short rim | the rim ends visibly short of the surface (far more than a grid cell) | this note |
| shell with a missing face | a closed body with one face removed, used as a tool | not this note (section 7) |

Today a short rim is refused, or, with `InputRepair = AutoRepair`, closed by a hole fill into a solid that the caller did
not describe: the result then contains surface that lies on no input (what the planned `Verification` option calls
invented surface).

## 2. Goal and non-goals

- Goal: an opt-in option that extends such a sheet to the workpiece's surface and cuts, and that accepts the cut only if
  a coarse check shows that it is unambiguous. Without the option nothing changes, bit for bit, and nothing extra is
  computed.
- Non-goals: no change of a default; no new meaning for a sheet that ends well inside the workpiece (a slit, a slot, a
  pocket wall); no closing of open bodies; no change of the exact Boolean or of the sheet role's classification.

## 3. The convention

Two members on `SolidBooleanPipeline`:

```csharp
public bool ExtendSheets = false;          // opt-in
public double MaxSheetExtension = 0.05;    // relative to the sheet's diagonal; the owner's value (2026-11-23)
```

With `ExtendSheets`, a sheet that is still refused with `RimInsideWorkpiece` after the grid retry is tried again as an
extended sheet:

- **The strip.** Every rim vertex `v` gets one new point `v + e * d(v)`. `d(v)` is the outward direction of the rim at
  `v`: for each of the two rim edges at `v`, the unit vector in the plane of the edge's triangle, perpendicular to the
  edge and pointing away from the triangle; `d(v)` is their mitred mean (the mean, scaled by `1 / cos` of half the angle
  between them, the scale capped at 2). Each rim edge gets one quad between its two vertices and their two new points,
  split into two triangles and oriented like the triangle it continues. The original triangles are kept exactly; only
  triangles are added.
- **The length.** `e` is the smallest value on the ladder `L/8, L/4, L/2, L` times the sheet's diagonal, with
  `L = MaxSheetExtension`, for which the sheet role accepts the extended sheet. If none is accepted, the refusal stands.
- **The route.** `Report.Route` gets a step `SheetExtension` with the input, `e` (absolute and relative), the area of
  the strip, and the numbers of the check (section 4), whether it passed or not.

Why one mitred point per rim vertex and not one flat quad per rim edge with a fan at the vertex: at a concave rim vertex
flat quads of neighbouring edges overlap, and the extended sheet would intersect itself; the mitred strip is a manifold
surface for rim angles up to the cap. The price: where the rim is not planar the quads are slightly twisted. The spike
(section 10) measures both variants on bent sheets before one is chosen.

What the strip is not: a continuation of a curved sheet's curvature. It continues each rim triangle's plane. That is why
the check below asks whether the result depends on the extension.

## 4. The coarse check

All four parts must hold, otherwise the run is refused exactly as without the option (the step in the route says which
part failed).

1. **Sheet, not shell.** The tool must have one boundary loop, and a cap over that loop (a fan from the loop's
   centroid, only measured, never used) must have an area of at least 0.45 of the area of the tool closed by it. A sheet
   closed by a cap is a lens: the cap is about as large as the sheet, so its share is near 0.5. A body with a missing
   face has a small cap. This part runs first; it costs one pass over the tool.
2. **The cut is consistent.** The cut with the extended sheet and the cut with the extended sheet reversed both succeed
   through the sheet role, and the two kept volumes add up to the workpiece's volume (relative error at most 1e-9, on
   the volumes before the rounding repair). A wrong side or a partial cut cannot pass this.
3. **The result does not depend on the extension.** The cut is computed again with twice the extension, `2 e`. The kept
   volume changes by less than 1 % of the workpiece's volume. A sheet whose continuation matters (it bends near its rim,
   or it ends far from the surface) fails here.
4. **The extension is small.** The share of the new cut face of the result that lies on added triangles, not on the
   original sheet, is at most 10 %. The share is reported in every case.

Thresholds: 0.45, 1e-9, 1 % and 10 % are proposals. Each becomes a named constant with its reason, and the spike reports
the margin of each on procedural inputs (how far the passing cases and the failing cases are from the threshold).

## 5. Why this cannot produce a gross error silently

- A wrong side or a partial cut breaks part 2: the two sides do not add up.
- A sheet that was not meant as a through cut (a slit that ends well inside) needs an extension above
  `MaxSheetExtension`, or its continuation is arbitrary and part 3 or part 4 fails.
- A body with a missing face fails part 1.
- Every accepted result says in its route that the sheet was extended, by how much, and what share of the cut face is
  new.

What remains possible: a sheet that ends short, is nearly straight at its rim, and was nevertheless meant to end there.
Geometry cannot tell that case from a short through cut. This is why the option is opt-in and why it is not part of
`AutomaticSteps`.

## 6. Order in the pipeline and interplay

- **Before it:** the direct sheet cut, then the grid retry (`AutomaticSteps`), which is exact and cheaper and answers
  the flush rim. The extension is tried only if the refusal is still `RimInsideWorkpiece`.
- **After it:** if the extension is refused or fails its check, the run continues as today (the fallback, the input
  repair or the refusal). The first failure's text stands; the step in the route records the try.
- **Stated roles:** the option applies to auto-routed tools and to sheets stated in `InputRoles` alike: the caller
  asked for it by name.
- **`UncutSide`:** unchanged; it is passed to the cuts of the check as to any sheet cut.
- **Verification (`NoInventedSurface`, planned):** the part of the cut face on the strip lies on no input the caller
  passed in, so the check would count it as invented and, at its limit 0, refuse every extended cut. Proposal: when the
  result comes from an accepted extension, the strip is added to the reference surfaces of the check, and the share
  from part 4 is the reported measure of what was added. The alternative (the caller must raise
  `MaxInventedSurfaceShare`) makes the two options unusable together. Open for the owner (section 9).
- **Rounding:** the cuts of the check run the same rounding as any cut; the volumes of part 2 are those before the
  rounding repair, so a thin result does not spoil the identity.

## 7. What stays refused, with a better diagnosis

A change without effect on results, in the same PR or before it: the refusal says what is missing.

- `RimInsideWorkpiece`: the largest depth of the rim inside the workpiece, absolute and relative to the sheet's
  diagonal, and whether the tool passes part 1 (sheet or shell). With these two numbers a caller can decide between the
  grid, the extension and fixing the input.
- Body with a missing face: stays refused. Observed by the owner on private inputs: the openings of such tools are not
  planar, so a flat cap does not exist and any closing is a guess. No convention is proposed.
- Sheet with an edge used by more than two triangles (`NotSurface`): stays refused. A pre-step that separates the
  pieces at such an edge is a separate question and needs its own procedural study.
- Open workpiece (`WorkpieceNotClosed`): stays refused by default; the input repair stays opt-in.

## 8. Tests (procedural, no fixture data) and cost

- A box cut by a bent sheet (two planes meeting at an angle) whose rim ends 0.1 %, 0.5 % and 2 % of its diagonal short:
  with the option the first two cut and give the analytic volume; the third is refused at `MaxSheetExtension = 0.01`
  and cut at 0.05.
- The same sheet ending in the middle of the box (a slit): refused at every limit (part 3 or 4), with the old status.
- A box without a lid as the tool: refused (part 1).
- A sheet that bends sharply just before its rim, so that the direction of the extension decides the cut: refused
  (part 3).
- A rim with a concave vertex, and a rim that is not planar: the strip is a manifold surface and the cut is accepted or
  refused by the check, never by a self-intersection of the strip.
- Far from the origin and in rotated frames: the same outcomes.
- Without the option: every case as today, bit for bit.
- The project's standard set: invariance over `Similarity.All()` (the 2^k scales bit for bit), reuse (compute twice),
  repeated runs; success paths check the result (closed, manifold, finite, clean) and the volume.
- Mutants: each of the four parts switched off, each threshold moved by a factor, the ladder's order, the strip's
  orientation; each must fail a test.

Cost: nothing without the option, and nothing for a sheet that cuts directly or on the grid. A sheet that reaches the
extension costs up to four sheet cuts for the ladder plus two for the check (the reversed cut and the cut with `2 e`);
part 1 is one pass over the tool.

## 9. Decisions

Decided by the owner: the option is opt-in; the limit's default is 5 % of the sheet's diagonal (2026-11-23); bodies
with a missing face and sheets with non-manifold edges stay refused.

Open:

1. The strip: mitred points at the rim vertices (proposed) or flat quads per rim edge with fans; the spike decides.
2. The four thresholds, after the spike reports their margins.
3. Verification: an accepted strip counts as a reference surface of `NoInventedSurface` (proposed), or the caller
   raises the limit.
4. Whether the better diagnosis of section 7 goes in first as its own small PR (proposed: yes, it helps without the
   option).

## 10. Plan

1. **Spike (no library change):** a benchmark project that builds the strip in both variants on procedural sheets
   (planar, bent, curved; convex and concave rims; short by 0.05 % to 5 %; slits; shells), runs the four parts through
   the public API and reports for each case the outcome, the numbers of each part and the margin to its threshold; the
   owner runs the same probe on private inputs locally. Go: every procedural through cut within the limit is accepted
   with the analytic volume, every slit and shell is refused, and each threshold separates the two groups with a margin
   of at least a factor 2. No-go: a class of inputs where the check accepts a wrong cut, or where the margins overlap.
2. **Diagnosis PR:** the depth of the rim and the sheet-or-shell measure in the refusal's text and report.
3. **Feature PR:** the option, its tests and the guide, after the grid retry default and the verification option are on
   master.

## Prior art

- Autodesk Fusion, Split Body: the option "Extend Splitting Tool(s)" extends the selected tool so that it intersects the
  body to split, automatically where possible (the API member `isSplittingToolExtended` says the same). Users report
  that the extension can fail or be slow on complex faces and ask for it to be off unless the face is planar: an
  argument for opt-in and for a check of the result.
- SolidWorks, Cut With Surface: the surface must fully divide the body; the common advice is to over-build or extend
  the surface past the part by hand.

Both products extend the tool or ask the user to; neither documents a check of the kind in section 4. No code of either
was read; the statements are from their public help pages and user forums (searched 2026-11-24). Nothing is ported.
