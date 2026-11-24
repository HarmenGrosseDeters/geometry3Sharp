# Sheet extension: a cutting sheet that ends just short of the workpiece's surface

Status: design proposal, second version (2026-11-24). Nothing is implemented. The first version (a fixed-length strip
with a four-part check of the cut) was withdrawn after an independent review built a counterexample; section 11 keeps
what was learned. Section 9 lists what is decided and what is open; section 10 is the plan. User-facing behaviour today:
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

## 4. The construction: up to the surface, per rim vertex

Input: the sheet `S` (the original triangles are kept exactly; only triangles are added) and the closed workpiece `W`.

1. **Where.** Only the parts of the rim that lie inside `W` are continued: a rim vertex is inside by the exact winding
   number of `W`; a run of inside vertices gets one edge of slack at each end. A rim outside `W` is left alone, so a
   strip cannot reach into an unrelated part of the workpiece from outside.
2. **Direction.** For a rim vertex `v` with rim edges `a` and `b`: `n_a`, `n_b` are the unit vectors in the plane of
   each edge's triangle, perpendicular to the edge and pointing away from the triangle. `d(v) = normalize(n_a + n_b) /
   cos(half the angle between n_a and n_b)`, the factor capped at 2 (a true mitre: the strip keeps its width at a
   corner). A vertex on a non-manifold rim (more than two rim edges) gets one point per boundary wedge, or the helper
   refuses with a named reason; the spike decides which.
3. **Length.** A ray from `v` along `d(v)`. Its first exit from `W` at distance `g(v)` (the depth of the rim there). The
   new point is `v + (g(v) + o) d(v)` with a small overshoot `o` so that the strip ends outside. One quad per rim edge
   between its two vertices and their two new points, split into two triangles, oriented like the triangle it continues.
4. **Refusals** (each with a reason and a witness):
   - a ray has no exit within the limit (`g(v)` above `MaxExtension` times the sheet's diagonal);
   - a ray crosses the surface of `W` again before its end (the overshoot enters other material);
   - a quad is inverted (its outer edge runs against its rim edge: the rays of neighbouring vertices cross, as at a
     notch narrower than the depth);
   - the strip meets `W`'s surface anywhere other than in one curve per continued run, from one end of the run to the
     other (checked with the exact triangle predicates; this is what stops a strip whose corner rays are fine and whose
     interior passes through a fin);
   - the strip intersects the sheet or itself.
5. **Report.** Per continued run: the largest and mean depth `g`, the length of the run, the area of the strip inside
   `W`, and the direction-uncertainty volume `U = sum over rim edges of (1/2) g^2 tan(delta) length`, the volume
   by which the cut could move if the true continuation left the rim at an angle up to `delta` from the strip
   (`delta` a parameter, proposed 15 degrees). `U` is compared with the smaller of the two parts the cut makes, not with
   the workpiece.

Why per vertex and up to the first exit: a strip of one fixed length is accepted by the sheet role as soon as it leaves
the workpiece *somewhere suitable*, which can be after passing through a second wall (section 11). The first exit is the
only length that is local.

## 5. What the rules guarantee, and what they do not

- The extended sheet equals the original outside the band between the old rim and the first surface the rays meet. The
  band's width is `g(v)`, at most the limit.
- The band meets the workpiece's surface in exactly one curve per run. So the cut it completes is the one the sheet
  already made up to the rim; no other part of the workpiece is cut.
- The reported `U` bounds what the direction of the continuation can change.
- Not guaranteed: that the sheet was meant to go through (intent, section 2); that a curved sheet continues with its
  curvature (the strip continues each rim triangle's plane; `U` is the measure of that).

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
  each sheet at most once. Sheets that were fine are not touched.
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
  diagonal, along the direction of section 4, or "no exit within the limit"; and the length of the rim that is inside.
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
- **a concave workpiece** where a longer strip would re-enter: the first exit is used; the re-entry is never reached;
- a dome and a tray as stated sheets: both continued, both reported;
- several sheets, one short; `UncutSide` set; a rectangular sheet short on all four sides (the mitre at 90 degrees);
- far from the origin and in rotated frames; without the option: bit for bit as today;
- the project's standard set: invariance over `Similarity.All()` (2^k scales bit for bit), reuse, repeated runs;
  success paths check the result (closed, manifold, finite, clean) and the volume;
- mutants: each refusal rule switched off, the first exit replaced by the last, the mitre factor, the slack edges, the
  orientation of the quads; each must fail a test.

Cost: one winding query and one ray per rim vertex, the strip-against-surface test, and one sheet cut. Nothing for a
sheet that cuts directly or on the grid, and nothing without the option.

## 9. Decisions

Decided by the owner: opt-in; a limit of 5 % of the sheet's diagonal (2026-11-23); no convention for bodies with a
missing face and for sheets with non-manifold edges.

Open:

1. Is the diagnosis (A) plus the helper (B) enough, or is the pipeline option (C) wanted? Proposed: A now, B after the
   spike, C only if the spike's numbers on the owner's inputs justify it.
2. The extension only for stated sheets (proposed), or also for auto-routed open tools.
3. The limits: the depth (5 % decided), `delta` and the bound on `U` relative to the smaller part; after the spike.
4. A bowtie rim vertex: per-wedge points or a refusal.
5. For C: does a failed extension block the hole-fill fallback for that tool; and the strip as a reference surface of
   the verification.

## 10. Plan

1. **Diagnosis PR (A):** the depth of the rim in the refusal's text and report. No convention, no option.
2. **Spike (no library change):** a benchmark project that builds the strip of section 4 with the public API on the
   procedural cases of section 8 and reports, per case, the outcome, the refusal reason, `g`, `U` and the volume against
   the analytic value. The owner runs the same probe locally on private inputs to get the upper bound: how many
   `RimInsideWorkpiece` refusals there are, the distribution of the rim's depth relative to the diagonal, the share
   within the limit, how many of those are continued, and how many accepted cuts are wrong by the project's independent
   checks (the self-consistency checks, a second Boolean implementation, a reference where one exists). Go: every
   procedural through cut within the limit gives the analytic volume; every adversarial case of section 8 ends as
   stated there; no accepted cut on the owner's inputs is wrong. No-go: any accepted wrong cut, or a share of continued
   cases too small to be worth an option.
3. **Helper PR (B)**, then, if decided, **the pipeline option (C)** after the grid retry default and the verification
   option are on master.

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
- **The mitred strip** inverts at a notch narrower than the extension, and the first version's formula for the mitre
  was not the true mitre.

## Prior art

- Autodesk Fusion, Split Body: the option "Extend Splitting Tool(s)" extends the selected tool so that it intersects the
  body, automatically where possible; users report that it can fail or be slow on complex faces and ask for it to be off
  unless the face is planar.
- SolidWorks, Cut With Surface: the surface must fully divide the body; users over-build or extend the surface by hand.
  Its separate Extend Surface feature has the end conditions Distance, Up to point and **Up to surface**, with the
  extension types "same surface" and "linear" (tangent): "up to surface" with a linear extension is the construction of
  section 4.
- Open CASCADE has `GeomLib::ExtendSurfByLength` for parametric surfaces (LGPL: cited, not read or ported).

No source was found that documents a check of an automatic extension. No code of any of these was read; the statements
are from public help pages and forums (searched 2026-11-24). Nothing is ported.
