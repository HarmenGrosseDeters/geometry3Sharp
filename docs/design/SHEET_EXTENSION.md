# Sheet extension: a cutting sheet that ends just short of the workpiece's surface

Status: design, third version (2026-11-24); the helper (deliverable B) and the diagnosis (deliverable A, as `Diagnose()` of the same class) are built (`gs.MeshSheetExtension`, [guide](../guides/booleans-and-imprint.md#a-sheet-that-ends-short-of-the-surface), PR of branch `feature/sheet-extension-helper`); the pipeline option (C) is next and not built. The first version (a fixed-length strip
with a four-part check of the cut) was withdrawn after an independent review built a counterexample; section 11 keeps
what was learned. In the review the construction of section 4 was run with the public API on five cases of section 8 (the slot and the
fin, the free pin, the chord, the rectangle short on four sides, a vertex on the surface); they end as listed there. Section 9 lists what is decided and what is open; section 10 is the plan. User-facing behaviour today:
[booleans-and-imprint.md](../guides/booleans-and-imprint.md#open-cutting-surface-sheet-role).

## 1. Problem

The sheet role (`ExactMeshBoolean.InputRole.Sheet`, `SolidBooleanPipeline.InputRoles`, and by default every open tool of
a Difference) cuts a closed workpiece along an open surface. It refuses with `SheetRefusal.RimInsideWorkpiece` where a
free edge of the sheet lies inside the workpiece: the sheet then does not divide the workpiece, and "the side behind the
sheet" is not defined.

Observed by the owner on private inputs: a class of such refusals are sheets that are meant as through cuts and whose rim
ends a little short of the workpiece's surface, by about 0.1 % to 1 % of the sheet's size. Neighbouring classes:

| class | what it is | what answers it |
|---|---|---|
| flush rim | the rim is meant to lie in a face and sits a few rounding units inside it | the grid retry of `AutomaticSteps` ([PIPELINE_GRID_STEP.md](PIPELINE_GRID_STEP.md)); exact, no convention needed |
| short rim | the rim ends visibly short of the surface (far more than a grid cell) | this note |
| shell with a missing face | a closed body with one face removed, used as a tool | not decidable from the shape (section 7) |

Today a short rim is refused, or, with `InputRepair = AutoRepair`, closed by a hole fill into a solid that the caller did
not describe.

## 2. Goal and non-goals

- Goal: a way to continue such a sheet to the workpiece's surface so that the sheet role can cut, with rules that make
  the continuation local (it changes nothing but the band between the old rim and the surface) and that refuse where it
  is not. Without it nothing changes, bit for bit, and nothing extra is computed.
- Non-goals: no change of a default; no meaning for a sheet that ends well inside the workpiece (a slit, a slot, a
  pocket wall); no closing of open bodies; no change of the exact Boolean or of the sheet role's classification.
- What no rule can decide: whether a sheet that ends short was meant to end there. That is intent, not geometry; it is
  why every form of this is opt-in.

## 3. Three deliverables, in the order of their value

- **A. Diagnosis (first, on its own).** The refusal says what is missing: the depth of the rim inside the workpiece
  (section 7). With it a caller can decide between the grid, fixing the input, the extruded-cut recipe of the guide, and
  B. This helps without any convention and may be enough.
- **B. An explicit helper.** A public operation that takes a sheet and a workpiece and returns the sheet continued up to
  the workpiece's surface, or a refusal with a reason (section 4). The caller runs it and passes the result as an
  ordinary input. The Boolean stays exact and unchanged, the extended sheet is an input like any other (so a later
  verification of the result has nothing to object to), and what was added is in the helper's report.
- **C. A pipeline option** that calls B for a refused sheet and cuts. Only this needs the interplay rules of section 6.
  Whether C is worth having is decided on the spike's numbers (section 10); B is its building block either way.

## 4. The construction: up to the surface, per rim edge

Input: the sheet `S` (the original triangles are kept exactly; only triangles are added) and the closed workpiece `W`.
The principle: **construct in doubles, verify exactly.** The strip is built with ordinary floating-point geometry; it is
accepted only if exact tests hold. A degenerate situation (a ray through an edge or a vertex of `W`, a grazing ray) can
then only cause a refusal, never a wrong cut.

1. **Where.** A rim edge is continued if it meets the interior of `W` (the segment against `W`, or the edges the
   refusal names). The choice is by edge, not by vertex: a long rim edge can pass through `W` while both its ends are
   outside (a straight rim across a round workpiece). A run is a maximal chain of such edges. A rim that is outside `W`
   is left alone, so a strip cannot reach into an unrelated part of the workpiece from outside.
2. **Direction.** For a rim vertex `v` with rim edges `a` and `b`: `n_a`, `n_b` are the unit vectors in the plane of
   each edge's triangle, perpendicular to the edge and pointing away from the triangle; `d(v) = normalize(n_a + n_b)`,
   a unit vector. There is no mitre factor: every vertex finds its own exit along its own ray, so a corner needs none.
   A vertex on a non-manifold rim (more than two rim edges) gets one point per boundary wedge, or the helper refuses
   with a named reason; the spike decides which.
3. **Length.** A ray from `v` along `d(v)`. `g(v)` is the Euclidean distance to its first exit from `W` (the depth of
   the rim at `v`); a vertex outside `W` or on its surface has `g = 0` and ends a run (a vertex on the surface in the
   middle of the rim ends two runs and its new point is shared by both). The new point is `v + (g(v) + o(v)) d(v)`
   with the overshoot `o(v) = min(o_max, half the distance from the exit to the ray's next hit of W)`, at least a few
   rounding units of the coordinates; `o_max` is a small share of the sheet's diagonal. With this rule the new point
   cannot enter other material along its ray.
4. **The strip.** One quad per continued rim edge between its two vertices and their two new points, split into two
   triangles and oriented like the triangle it continues. Where the outer edge of a quad would pass back through `W`
   (rays that diverge on a curved rim, a convex face between two exits, an edge with both ends outside), the quad is
   subdivided along the rim edge's direction with further rays until every point of the outer edge is outside. This
   adds vertices in the strip only; the original triangles are never changed. (Corrected by the spike: this is the main path, not a repair.
   Every sheet that is larger than `W` at the sides has rim edges with an end outside, `g = 0`, whose outer edge passes through `W` until it is
   subdivided; 19 of the spike's 35 cases need it. Where to refine: the middle of the longest stretch of the rim edge inside `W` between the
   two outer points, retried off-centre if the new point is not strictly outside.)
5. **Exact verification; each failure is a refusal with a reason and a witness.**
   - Depth: every `g(v)` is at most `MaxExtension` times the sheet's diagonal, and every ray has an exit. (The spike: the diagonal of the bounding box is not rotation invariant, a rotated input has a diagonal a quarter to a third smaller; the measure of the sheet's size is open.)
   - Every new point is strictly outside `W` (exact winding number). (Corrected by the spike: the winding number is symbolically perturbed and cannot tell a point on a
     face from one beside it; strictly outside is: the exact winding number is 0 and no triangle of `W` contains the point.)
   - No quad is inverted: for a rim edge `A -> B` with new points `A'`, `B'`, `(B' - A') . (B - A) > 0` (the rays of
     neighbouring vertices do not cross, as they do at a notch narrower than the depth). (Corrected by the spike: this fails for the segments of a subdivided outer edge at a
     corner, which can run slightly backwards without any fold, and depends on the frame. Used: every strip triangle's normal has a positive cosine with the normal of the sheet triangle it continues.)
   - The curve rule: the intersection of the strip with the surface of `W` has exactly one connected component per
     run (an open curve, or a closed loop where the whole rim is inside), and the strip has no coplanar contact with a
     face of `W`. It is checkable today through the public API: `ExactMeshImprint` of the strip by `W` with
     `LabelCurves`, counting the distinct curve ids. This is the rule that catches material between two rim vertices
     whose rays both exit cleanly (a thin free pin in a slot, clear of the outer edge). (Corrected by the spike: count the connected components of `CurveEdges`; `CurveId` joins edges only at
     vertices where exactly two curve edges meet, so a branching curve would count twice. The two numbers were equal in every case of the spike.)
   - The strip meets neither the sheet, nor itself, nor any other tool or sheet inside `W`.
   - The sheet role accepts the extended sheet.
6. **Report.** Per run: the largest and the mean depth `g`, the length of the run, the area of the strip inside `W`,
   and the direction estimate `U` (section 5), absolute and relative to the smaller of the two parts the cut makes.

Why per edge and up to the first exit: a strip of one fixed length is accepted by the sheet role as soon as it leaves
the workpiece somewhere suitable, which can be after passing through a second wall (section 11). The first exit is the
only length that is local.

## 5. What the rules guarantee, and what they do not

- The extended sheet equals the original outside the band between the old rim and the first surface the rays meet. The
  band's width is `g(v)`, at most the limit.
- The band meets the workpiece's surface in exactly one curve per run and touches nothing else. So the extended sheet
  differs from any other continuation that leaves through the same first surface only inside the wedge between the
  two continuations.
- `U` is a first-order ESTIMATE of that wedge, not a bound. For a rim vertex whose ray meets the surface at the angle
  `alpha`, a continuation tilted by `delta` changes the cut by about `(1/2) g^2 sin(delta) sin(alpha) / sin(alpha -
  delta)` per unit length of rim; `U` is the sum over the run. It assumes a straight alternative and a locally flat
  surface. `delta` comes from the sheet itself: `delta(v) = max(delta_min, kappa(v) g(v))`, with `kappa` the sheet's
  turning per unit length over its last rows of triangles at the rim (the dihedral angle between neighbouring
  triangles over the distance of their centroids); a planar sheet gets `delta_min`, a curved one more. Where
  `alpha <= delta` plus a margin the tilted continuation may not exit within the limit at all: that vertex is flagged
  and the helper refuses.
- Not guaranteed: that the sheet was meant to go through (intent, section 2); that a curved sheet continues with its
  curvature (the strip continues each rim triangle's plane; `U` is the estimate of what that costs).

A cheap assertion for the implementation, not a safety check: the cut with the extended sheet and the cut with the
reversed sheet keep volumes that add up to the workpiece's. It holds by construction for any accepted sheet cut (and
changes its form with `UncutSide`), so it can only catch a bug in the implementation.

## 6. Interplay (for the pipeline option C)

- **Which tools.** Proposed: only sheets the caller STATED in `InputRoles`, not tools the pipeline routed by itself. A
  stated sheet is the caller's word that the tool is a cutting surface. An auto-routed open tool may be a body with a
  missing face, which shape cannot tell from a sheet (section 7).
- **Order.** The direct sheet cut, then the grid retry (exact, and it answers the flush rim), then the extension, on the
  inputs as given, not on the snapped copies. With `AutomaticSteps = None` the extension would also absorb flush rims;
  the guide says to keep the grid retry on.
- **Several sheets.** The sheet role names one refused sheet; only that sheet is continued, then the cut is tried again,
  each sheet at most once (so up to n + 1 cuts for n sheets). Sheets that were fine are not touched, and a strip that
  meets another tool or sheet inside the workpiece is refused (section 4).
- **`MinThickness` and thin inputs** are measured on the inputs as given, before the strip is added.
- **After a refusal of the extension** the run continues exactly as without the option. Whether a failed extension
  should also block the hole-fill fallback for that tool is open (section 9).
- **`UncutSide`** is passed through; no rule above depends on it.
- **Verification (`NoInventedSurface`, planned):** with the helper B the extended sheet is an input and nothing is
  invented. Inside the pipeline option C the strip is not an input the caller passed: proposed, the strip is added to
  the check's reference surfaces and the strip's area inside the workpiece is reported as what was added.

## 7. What stays refused, and the diagnosis

The diagnosis (deliverable A), without effect on results:

- `RimInsideWorkpiece`: the largest depth of the rim inside the workpiece, absolute and relative to the sheet's
  diagonal, along the direction of section 4, or "no exit within the limit"; and the length of the rim that is inside,
  measured per rim edge (an edge can be inside while both its ends are outside).
- A sheet with an edge used by more than two triangles (`NotSurface`) and an open workpiece (`WorkpieceNotClosed`) stay
  refused as today.

Sheet or shell: the first version tried to tell a sheet from a body with a missing face by the area of a cap over the
boundary loop. Measured on standard shapes, no such measure separates them: a spherical dome of half angle 60 degrees
(a legitimate sheet) has a cap share of 0.43, a shallow open tray (a shell) 0.42; a hemisphere used as a sheet and a
sphere with a missing face are the same geometry. It is intent. Hence the rule of section 6: the extension is for
stated sheets. A shell whose opening lies just below the surface, stated as a sheet, would be continued into the pocket
its caller probably meant; that is the stated role's consequence and is reported like any extension.

## 8. Tests and cost

Procedural, no fixture data. The list contains the adversarial cases a review found against the first version:

- a box and a bent sheet whose rim ends 0.1 %, 0.5 % and 2 % of its diagonal short: continued and cut with the analytic
  volume within the limit, refused above it;
- a slit that ends in the middle of the box: refused (no exit within the limit);
- **a slot and a fin**: a block, a narrow slot, then a thin wall; the rim ends just short of the slot: the strip stops
  at the slot's wall; the fin is untouched;
- **a notch in the rim narrower than the depth**: refused (inverted quad), never a self-intersecting sheet;
- **a rim outside the workpiece next to another part of it** (a U-shaped workpiece): that rim is not continued;
- **a bowtie vertex on the rim**: per-wedge points or the named refusal;
- **a free pin in a slot, between two rim vertices whose rays both exit cleanly**: refused by the curve rule (the
  sheet role alone accepts that strip and cuts the pin's top off). (Corrected by the spike: only for a thin pin that stays clear of the outer edge. A pin that reaches the outer edge is routed round by
  the subdivision and the strip does not touch it, which is right; a rib on the wall cannot be routed round and is refused by the inverted-quad test. The case list has the thin pin, the thick pin and the rib.)
- **a chord**: a straight rim edge across a round workpiece, both ends outside, the middle inside: continued (chosen
  by edge, the outer edge subdivided);
- **a rim vertex exactly on the surface**: it ends a run with `g = 0`; no degenerate triangle;
- **an exit at a grazing angle** (`alpha` near `delta`): refused; and a ray through an edge or a vertex of the workpiece
  (a rectangular sheet short at a box's corner): accepted only if the exact verification holds;
- **a concave workpiece** where a longer strip would re-enter: the first exit is used; the re-entry is never reached;
- a dome and a tray as stated sheets: both continued, both reported;
- several sheets, one short; `UncutSide` set; a rectangular sheet short on all four sides (each corner ray finds its own exit);
- far from the origin and in rotated frames; without the option: bit for bit as today;
- the project's standard set: invariance over `Similarity.All()` (2^k scales bit for bit), reuse, repeated runs;
  success paths check the result (closed, manifold, finite, clean) and the volume;
- mutants: each refusal rule switched off, the first exit replaced by the last, the overshoot rule, the choice by
  edge replaced by the choice by vertex, the subdivision of the outer edge, the orientation of the quads; each must
  fail a test.

Cost: one segment test per rim edge, one ray and one winding query per new point, one imprint of the strip by the
workpiece for the curve rule, and one sheet cut per continued sheet. Nothing for a sheet that cuts directly or on the
grid, and nothing without the option. The spike reports the cost of the imprint per case.

## 9. Decisions

Decided by the owner: opt-in; a limit of 5 % of the sheet's diagonal (2026-11-23); no convention for bodies with a
missing face and for sheets with non-manifold edges.

Open:

1. Is the diagnosis (A) plus the helper (B) enough, or is the pipeline option (C) wanted? Proposed: A now, B after the
   spike, C only if the spike's numbers on the owner's inputs justify it.
2. The extension only for stated sheets (proposed), or also for auto-routed open tools.
3. The limits: the depth (5 % decided), `delta_min`, the margin on the exit angle, the overshoot cap and the limit on
   `U` relative to the smaller part; after the spike.
4. A bowtie rim vertex: per-wedge points or a refusal.
5. For C: does a failed extension block the hole-fill fallback for that tool; and the strip as a reference surface of
   the verification.

## 10. Plan

1. **Diagnosis (A):** built with the helper as `MeshSheetExtension.Diagnose()` (a caller reports the rim's depth and length inside for a refused cut). Not yet in the refusal's text of the boolean or the pipeline (that is C's business). No convention, no option.
2. **Spike (no library change; done 2026-11-24, [SHEET_EXTENSION_SPIKE_2026-11.md](../research/SHEET_EXTENSION_SPIKE_2026-11.md), [benchmarks/SheetExtensionProbe](../../benchmarks/SheetExtensionProbe/README.md)):** a benchmark project that builds the strip of section 4 with public members only
   (`ExactMeshImprint` for the curve rule, the sheet cut, a winding query for the choice, since the verification is
   exact) on the procedural cases of section 8 and reports, per case, the outcome, the refusal reason, `g`, `U`, the
   cost of the imprint and the volume against the analytic value. The owner runs the same probe locally on private
   inputs to get the upper bound: how many `RimInsideWorkpiece` refusals there are, how many of them come from stated
   sheets and how many from auto-routed tools, the distribution of the rim's depth relative to the diagonal, the share
   within the limit, and how many of those are continued.
   What can judge an extension: only a reference for what the caller meant. A second Boolean implementation run on the
   same extended sheet checks the Boolean, not the extension. "Wrong" therefore means: the result differs from a
   reference result, where one exists, by more than `U` plus the tolerance of the comparison.
   Go: every procedural through cut within the limit gives the analytic volume; every adversarial case of section 8
   ends as stated there; no continued cut on the owner's inputs is wrong against a reference. No-go: any accepted wrong
   cut. Whether the share of continued cases is worth the helper and the option is the owner's decision on the reported
   numbers.
3. **Helper PR (B), built (2026-11-24):** `gs.MeshSheetExtension` (`mesh_ops/MeshSheetExtension.cs`): the spike's construction ported unchanged, with the diagnosis as `Diagnose()` (the rim's depth, length inside, "no exit within the limit" and U per run, without the strip), witnesses with the free edge and the rim vertex, and the refusals of the input (`WorkpieceNotClosed`, `SheetNotSurface`) named. The spike's cases give the same outcomes and volumes through the library (`benchmarks/SheetExtensionProbe` runs the library; 111 rows, 128 invariance runs). Tests: `MeshSheetExtension*Tests`. Bowtie rim vertex: refused (`BowtieVertex`), as in the spike. The measure of the sheet's size stays the box's diagonal (item 2 of the spike's section 5; documented). Open from the build: a rib on the slot wall is refused in the identity frame (`InvertedQuad`) but continued, with the right cut, in a rotated frame (the strip's subdivision finds a way round); no accepted cut was wrong. The mutation shard of the file is a follow-up (`tests/mutation`).
4. **The pipeline option (C)**, if decided, after the grid retry default and the verification option are on master; it calls the helper for a refused sheet. Open points of section 9 (stated sheets only, whether a failed extension blocks the hole-fill fallback, the strip as a reference surface of the verification) are decided there.

## 11. What the first version got wrong

The first version of this note continued the sheet by a strip of one length from a ladder (up to 5 % of the diagonal),
took the smallest length the sheet role accepted, and accepted the cut if four checks held. A review built the strip
with the public API and found:

- **The ladder is not local.** A block, a slot 0.05 wide, then a fin 0.3 thick; the sheet's rim ends 0.1 short of the
  slot. The shorter strips end inside the fin and are refused; the first accepted strip passes through the fin and cuts
  its top off. The kept volume is wrong by 1.5 % of the workpiece (by as much as one likes with a taller fin), and all
  four checks pass.
- **"The cut and the reversed cut add up"** holds by construction for any accepted sheet cut: reversing the sheet swaps
  every mark of the exact labelling. It cannot fail on a wrong side or a partial cut.
- **"The result does not change when the extension is doubled"** cannot fail either: once the strip has left the
  workpiece, a longer strip changes only the part outside.
- **"Sheet, not shell" by the cap's area** does not separate the two (section 7).
- **The mitred strip** inverts at a notch narrower than the extension. With a ray per vertex to its own exit no mitre
  is needed at all.
- **Choosing the continued rim by vertex** (a first revision) misses a rim edge that passes through the workpiece with
  both ends outside; the choice is by edge.

## Prior art

- Autodesk Fusion, Split Body: the option "Extend Splitting Tool(s)" extends the selected tool so that it intersects the
  body, automatically where possible; users report that it can fail or be slow on complex faces and ask for it to be off
  unless the face is planar.
- SolidWorks, Cut With Surface: the surface must fully divide the body; users over-build or extend the surface by hand.
  Its separate Extend Surface feature has the end conditions Distance, Up to point and **Up to surface**, with the
  extension types "same surface" and "linear" (tangent): "up to surface" with a linear extension is the closest manual
  analogue of section 4 (there the user picks the target face; here it is the first exit).
- Open CASCADE has `GeomLib::ExtendSurfByLength` for parametric surfaces (LGPL: cited, not read or ported).

No source was found that documents a check of an automatic extension. No code of any of these was read; the statements
are from public help pages and forums (searched 2026-11-24). Nothing is ported.
