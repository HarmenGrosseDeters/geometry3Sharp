# SafeRounding and thin solids: why real sheets vanish, and what rule would keep them (study, 2026-11-22)

Status: **research note, no library code; the rule is decided (owner 2026-11-22, end of section 8), not built.**
A design study of two linked items: issue #1216 (SafeRounding removes real thin solids and returns `Success`) and plan #1259 (a guaranteed 3D rounding after Devillers et al.). The removal
threshold, any output change and any new option are owner decisions; section 8 lists the options with a leaning.
The owner's decision of 2026-11-22 on the pipeline audit ([ROADMAP.md](../ROADMAP.md), "Decided on 2026-11-22": the
removal "gets an opt-out and a threshold, plus a criterion that does not change with rotation; removed parts are listed
and the result is not clean") sets the direction; this note supplies the criterion and its numbers. It does not treat
the opt-out, and where it says "today's verdicts" it means the verdicts before that decision is built: "not clean
whenever a part is removed" comes on top of any rule here (section 8).

The measurement is `benchmarks/SnapRoundingExperiment` (`thin`, `ThinSolids.cs`, `thin.py`); raw output in
`benchmarks/SnapRoundingExperiment/trace/results/thin_2026-11-22/` (one compressed file per set, `tables.txt`,
`cost.jsonl`). Measured on master `bae5df61`, 4 cores, the cloud container of the session, .NET SDK 10.0.115. Every
input is generated in code.

Not repeated here: the guarantee side of a grid rounding
([SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](SNAP_ROUNDING_3D_GUARANTEE_2026-11.md), which found that the lost slabs are a
matter of the removal rule), the audit that found the drop
([SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md](SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md) F1 and 4.5), and the cost of the
rounding (the rounding cost study, PR #1390, [ROUNDING_COST_2026-11.md](ROUNDING_COST_2026-11.md)).

## 1. Result in short

- **The cause is one conjunction: "thin by the bound" and "edited anywhere".** A closed component goes if its
  thickness 2 V / A is below `maxLength` (1e-5 of the largest component's diagonal, or 256 ulps) and the repair edited
  at least one of its vertices. The size of the edit and the state it leaves play no part. Over all sets: 2976 of
  2976 thin components with an edit are removed, 0 of 732 without one (section 4.1).
- **The edit that condemns the sheet of #1216 is 7.6e-11 long.** The issue's 1e-4 degree tilt makes B's bottom
  diagonal miss A's corner edge by 7.6e-11 (on a 100 m model); the exact result holds two sliver triangles there, the
  repair contracts them (two contractions, drift 7.6e-11), and the sheet, 1e-5 to 1e-3 thick and 1.7e9 to 3.5e10
  ulps, is "edited". Far from the origin the coordinate spacing (9.3e-10) is above that gap: the inputs no longer hold the
  near-coincidence, nothing is edited, the same sheet stays (4.2).
- **So the threshold of one solid moves by seven orders of magnitude with the frame.** At the origin the thinnest
  sheet kept is 1.2e-4 of the model size in two frames and 7e-12 in the axis-aligned one (table of 4.3). 463 of the
  1106 thin solids of 4 ulps and more that the inputs define are removed today, and 837 of the 2022 results of those
  sets come back empty with `Success` and `RoundingClean` true.
- **A second, separate mechanism sits below about 1e-11 of the size:** the sheet's own side triangles fall below the
  degenerate area (1e-12 of the squared diagonal), the repair contracts them as decision D′ says, and the solid is
  mangled (volume changed by 2.8 to 12 851 times) whether or not it is then removed. No removal rule repairs that; it
  is the degenerate-area question of the audit's cube (4.4).
- **Candidates.** An ulp threshold alone, (a), keeps the sheets and leaves 16 more results not clean (defective
  slivers stay). "Only where defective", (b), alone keeps 1097 of 1432 slivers below 4 ulps and leaves 132 results
  not clean. **The combination (c), "below K ulps, or still defective", loses none of the 1106 solids at K = 4, keeps
  1 sliver (the one today's rule keeps too) and leaves exactly today's 29 results not clean.** What it adds are 508
  "between" components and 139 solids that the repair mangled and that then stay. **(d1), (c) plus "the repair
  changed its volume by more than a factor 2", removes those too:** 324 of the 463 lost solids come back (287 with
  the exact result's volume to 1 %), the 139 mangled ones still go, nothing kept is deformed or defective, and the thinnest sheet kept is the
  same in every frame (sections 5 and 6).
- **All candidates keep what SafeRounding guarantees:** closed (whole closed components go), `RoundingClean` as
  today under (c) and (d1) on every measured result (with two cases the sets do not hold, 5.4), and the drift bound,
  which the removal was never inside. Cost: one pass over the
  triangles, 2 to 5 % of the rounding as measured standalone (5.5).
- **#1259 does not remove the need for the rule.** A grid rounding merges sheets closer than a cell: of the 463 lost
  solids 439 are thinner than one cell, of the 324 that (d1) returns 300. Its output is at least 16 to 258 times the
  triangles. **No-go**, with the plan's trigger kept and one more precondition (section 7).
- **Leaning: (d1) at 4 ulps as the criterion of the change decided on 2026-11-22**, with the removed parts reported
  (count, volume, thickness). K and the factor are the owner's; (c) and K = 256 are the neighbours, with their
  numbers in section 6.

## 2. The rule today

`MeshRoundingRepair.remove_tiny_components` (`mesh_ops/MeshRoundingRepair.cs`), called at the end of `Apply` if the
repair found or edited anything; `ExactMeshBoolean` runs it with the defaults (`mesh_ops/ExactMeshBoolean.cs`, the
`SafeRounding` block of `Compute`). Per closed connected component, on the mesh after the edits:

| test | definition |
|---|---|
| *tiny* | area below 1e4 x 1e-12 x (diagonal of the largest component)^2 |
| *thin* | 2 abs(V) / area below `maxLength` = max(1e-5 x that diagonal, 256 ulp(largest coordinate of the component), `AbsoluteTolerance`) |
| *edited* | a vertex of the component was touched by an edit, or one of its triangles is still degenerate |
| *zero volume* | 2 abs(V) / area below 1 ulp of its largest coordinate, decided exactly (#1067, #1191) |

- Edited and (tiny or thin): removed. Exception: thin, not tiny, and abs(V) >= 1e-3 area^1.5 (real volume for its
  area): kept and counted as `ScaleDisparity`, not clean.
- Not edited: removed only if of zero volume, or tiny, inside out and enclosed by nothing (#1067, owner 2026-11-19:
  for untouched components thin is measured against the coordinate resolution).
- Nothing is removed if that would empty a result whose input is itself thin or tiny (`KeepWhenEmptied`, #985).
- The count is `Rounding.RemovedComponents`; the result is `RoundingClean`, `Status Success`.

So the untouched components already have the rule that #1216 proposes; the edited ones have the 1e-5 rule.

## 3. Method

**The replay.** The library offers no way to run the boolean's repair without the removal, so the tool rebuilds it
outside: `ExactMeshBoolean` with `SafeRounding` off gives the exact result, plainly rounded; on a copy the tool runs
`MeshRoundingRepair` with the options `Compute` sets (the fixed vertices and point ids are read from the boolean by
reflection) and `RemoveTinyComponents` off. Per component it then has: area and exact volume before and after the
edits, the largest coordinate, whether an edit touched it, and the defects left in it (degenerate triangles,
intersecting pairs, contacts made by the rounding). Each rule is a predicate on those; its outcome (the mesh without
the components it removes) is checked with `MeshRobustRepair.NotCleanReasons`.

**The check of the replay.** Today's rule, written out as such a predicate, must give the triangles of the boolean's
own default result. It does on 3474 of 3474 results with a non-empty exact result.

**The sets** (3529 cases, 55 of them with an empty exact result):

| set | cases | what |
|---|---:|---|
| `issue` | 6 | the three rows of the issue's table, near the origin and at (2527500, 5634200, 100) |
| `sheet` | 1920 | the repro scaled: thickness t = L 2^-k (k = 7, 10, ... 52), L = 1, 100, 1e4, 1e5, five offsets from 0 to 1e7 L, six frames |
| `plate` | 128 | a cube united with a thin plate beside it (an untouched thin input) |
| `drop` | 23 | the audit's cube and slab |
| `copies` | 936 | A minus a nearly coincident copy: shifted, jittered or turned by 2^e ulps, e = 0 to 36; a rotated box and a cube-sphere; two sizes, two places, three seeds |
| `clusters`, `utm`, `booleans`, `cylinder` | 516 | the generated cases of the earlier rounding notes (the slivers the rule was built for) |

The frames of `sheet`: `axis`; `rz` (30 degrees about z); `issue` (the 1e-4 degree tilt of B, then `rz`: the issue's
case); `gen` (one generic rotation); `rza` and `gena` (as `rz` and `gen`, with B's sides shifted so that no edge of B
passes over a corner of A). In `axis`, `rz`, `issue` and `gen`, B's bottom diagonal runs over two corners of A: a
coincidence of the kind faceted CAD models are full of.

**Classes.** The population is every closed component that is thin, tiny or of zero volume by today's tests (4379
components). Its thickness is 2 abs(V) / area of the exact result, rounded (before the edits), in ulps of the
component's largest coordinate.
- *Solids the inputs define*: the components of `issue`, `sheet`, `plate` and `drop` (1609; 1106 of 4 ulps and more).
- *Slivers*: the components of the other sets below 4 ulps (1432).
- *Between*: the components of the other sets of 4 ulps and more (1338): what is left between copies 8 to 2^36 ulps
  apart. Geometry cannot tell these from a thin solid; only their scale differs. This class is the decision.

A kept component is *defective* (a defect is left in it), *deformed* (its volume changed by more than a factor 2) or
*kept* (within a factor 2, no defect).

## 4. Root cause of #1216

### 4.1 Thin by the bound, and edited anywhere

`thin.py tables`, "The cause": closed components thinner than the bound, not tiny, not of zero volume.

| sets | edited: components | of them removed | not edited: components | of them removed |
|---|---:|---:|---:|---:|
| issue, sheet, plate, drop | 692 | 692 | 657 | 0 |
| copies, clusters, utm, booleans, cylinder | 2284 | 2284 | 75 | 0 |

The rule reads "edited" as "this component is a product of the rounding". It is a flag per component, set by any
edit at any of its vertices.

### 4.2 The issue's case: near the origin and not far from it

The six rows reproduce exactly (exact volumes 10, 1 and 0.271; `maxLength` 1.93e-3, and 1.51e-3 for the 1e-5 sheet,
which is a wedge over part of A; removed near, kept far).

- Near the origin the exact result of the 1e-3 sheet has 16 triangles, four of them degenerate: two slivers at each
  of two corners, where B's bottom diagonal crosses A's wall 7.6e-11 beside A's vertical edge. The tilt causes the
  gap: turning B by 1e-4 degrees about x moves its corner by 51 (1 - cos) = 7.8e-11 in y. `thin dump issue:1e-3:near`
  shows the vertex pair. The repair removes the slivers with two contractions (`MaxDrift` 7.6e-11) and nothing else;
  the sheet's volume is 10 before and 9.999999999997 after. The removal then deletes it: thin (1e-3 < 1.93e-3), edited.
- At (2527500, 5634200, 100) the spacing of the coordinates is 9.3e-10, above the gap. The inputs there do not hold
  the near-coincidence, the exact result has 12 triangles and no degenerate one, nothing is edited, the sheet stays.
- Between the two it depends on the placement: with the tilt, the sheet is lost at 6 of the 20 combinations of size
  and offset (all four sizes at the origin, L = 100 also at offsets 10 and 1000) and kept at 14. In the kept ones
  the vertex beside the corner is not in the exact result (12 triangles; the boolean's flat-region simplification
  takes it out, `thin dump sheet:16:1:1e1:issue`). Why it survives at one placement and not at another was not
  followed further: either way, 7.6e-13 of the model size decides over the whole solid.

The edit against the sheet it condemns (`tables`, "The size of the edit"): in the `issue` frame the drift is 5e-8 to
3e-6 of the sheet's thickness, and the sheets are 3.7e5 to 8.4e10 ulps thick. In the `gen` frame the smallest ratio
is 2.3e-13.

### 4.3 Which other inputs it hits

The thinnest sheet kept today, as k (t = L 2^-k), at the origin, the same for all four sizes; and under two candidates:

| rule | axis | rz | rza | issue | gen | gena |
|---|---:|---:|---:|---:|---:|---:|
| today | 37 | 34 | 34 | 13 | 13 | 19 |
| (d1), at 4 or 256 ulps | 37 | 37 | 37 | 52 | 37 | 37 |

((c) keeps the same sheets with the volume within a factor 2 and, below them, the mangled ones of 4.4: down to
k = 49 at 4 ulps and 43 at 256. k = 13 is 1.2e-4 L, 19 is 1.9e-6 L, 34 is 5.8e-11 L, 37 is 7.3e-12 L. In the `issue` frame a sheet thinner than
the tilt's rise is a wedge of the same shape at every k, so it is kept throughout.)

- **Any thin result component with one near-degeneracy.** With a coincidence between the operands (`issue`, `gen`)
  everything below `maxLength` goes: the first removed sheet is k = 16, 1.5e-5 L, 1.5 mm on a 100 m model. The
  audit's rotated slab is the same case (its operands share faces).
- **Also without any coincidence, from about 7e-7 of the size.** In `gena` nothing of B passes over a vertex of A.
  The sheet still has a small triangle at each corner where the diagonal of A's wall crosses the cut (legs 10 t and
  t); below about t = 7e-7 L its area is below the degenerate area, the repair contracts it (drift 10 t, volume changed
  by 0.2 %), and the sheet is edited: kept at k = 19, removed from k = 22.
- **Not axis-aligned boxes** (`axis`, `rz`, `rza`): nothing needs an edit down to k = 34 to 37, where 4.4 takes over.
- **Not untouched inputs**: the plates of `plate` stay down to k = 34 in both frames; the removed ones are all at
  k = 37 and thinner (4.4).
- **Far from the origin**: in the `issue` frame not from an offset of 1e5 L on (4.2); in `gen` and `gena` nearly as
  often as at the origin (46 to 52 and 35 to 44 of 64 sheets per offset; `tables`, "The sheet of #1216").
- **Differences of nearly coincident copies** more than a few ulps apart: at 2^30 and 2^36 ulps (1e-7 to 1e-5 of the
  size) 192 of 204 thin components are removed today; without the removal none of the 204 keeps a defect and none
  changes its volume by a factor 2 (section 6.1). They are the sheet of #1216 by another construction.

Per set today (`tables`, "Today"): `sheet` 828 of 1865 results emptied, `issue` 3 of 6, `drop` 6 of 23, `copies` 779
of 936; the thickest removed component is 9.6e-6 of the largest diagonal and 1.1e11 ulps.

### 4.4 The second mechanism: the solid's own triangles are degenerate

Below 4e-12 to 7e-12 of the size (by the frame's box; between k = 34 and 40 at the origin) the side walls of the
sheet are triangles of area L t / 2 below the degenerate area. The repair contracts them, as decision D′ intends for features below the degenerate
threshold ([SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md) section 1), and what is left is a different solid:

| k (frame rz, L = 1, origin) | thickness, ulps | triangles before, after | volume after / before |
|---:|---:|---|---:|
| 34 | 2.6e5 | 12, 12 | 1 |
| 37 | 3.3e4 | 12, 6 | 0.68 |
| 40 | 4.1e3 | 12, 6 | 3.1 |
| 46 | 64 | 12, 6 | 175 |
| 52 | 1 | 12, 6 | 11 205 |

139 of the 1106 solids are of this kind (volume changed by more than a factor 2), 18 of them plates of `plate` and
the audit's cube of 2^-11. Removing them (today, (d1)) loses the solid; keeping them ((c)) returns a solid that is
not the input's. Keeping such a solid as it is needs a degenerate test relative to the component, which the audit
names (its 4.5) and this study did not measure. The cube of 2^-9 is the borderline: the repair collapses it to a
tetrahedron of 1.85 times its volume, which (c) and (d1) keep.

### 4.5 Two cases from the Boolean fuzz harness

The fuzz harness of PR #1382 reported two differences of a box and a thin slab at its top whose result loses a part
(its CI log of `6f055e0a`, net48). They are the set `fuzz` here, the coordinates taken over as bits
(`thin fuzz:0 fuzz:1`, `trace/results/thin_2026-11-22/fuzz.jsonl.gz`); they are not in the counts of this note.

| case | the removed component | thickness | why it goes today | (c), (d1) | no removal |
|---|---|---|---|---|---|
| `fuzz:0` (slab 2^-30, sheared) | 4 triangles, area 7.6e-9, volume 1.2e-18 | 3.1e-10, 6.9e5 ulps | tiny, and one of its own triangles is degenerate; no edit | removed (still defective) | "degenerate triangles" |
| `fuzz:1` (slab 2^-43, turned by 3.8e-6 rad) | 12 triangles, area 1, volume 4.6e-7 | 9.2e-7, 2.1e9 ulps | thin, and two contractions with a drift of 2.2e-12 | kept, volume unchanged to 1e-10 | clean |

The first is a component whose own triangle is degenerate and that no edit repairs (it goes as "still defective",
the neighbour of 4.4); the second is the mechanism of 4.1. Both components are removed on net10.0 on Linux as
well: the rule's outcome is the same on both runtimes on these inputs, so the net48-only failure of that test is not
a difference of the rule (net48 itself was not run here).

## 5. Candidate rules

### 5.1 Definitions

All candidates change one thing: which *edited* closed components go. The untouched ones (zero volume, inside-out
residues), the `ScaleDisparity` guard and `KeepWhenEmptied` stay as today. Thickness is that of the exact result,
rounded (before the edits); ulp is that of the component's largest coordinate.

| rule | an edited closed component goes if |
|---|---|
| today | tiny or thin (thin: below 1e-5 of the diagonal or 256 ulps) |
| (a) at K | tiny, or thinner than K ulps |
| (b) | (tiny or thin as today) and a defect is left in it after the repair |
| (c) at K | thinner than K ulps, or (b) |
| (d1) at K | (c), or (tiny or thin as today) and the repair changed its volume (absolute value) by more than a factor 2 |
| (d2) at K | (c), or (tiny or thin as today) and thinner than twice the repair's largest drift |

In the tool every rule also removes an edited component whose volume after the edits is numerically zero (as today's
does); it decides nothing here. (d1) and (d2) are what the data suggested: (d1) from 4.4, (d2) from 4.2 ("compare the edit with the thickness").
Also run: (a) on the thickness after the edits, and (a) without the tiny rule.

### 5.2 Scores

`thin.py tables`, "The rules, scored". Solids: of 1106 (4 ulps and more). Slivers: of 1432. Between: of 1338.
Results not clean: `NotCleanReasons` not empty, of 2022 (solids' sets) and 1452 (other sets).

| rule | solids lost | solids deformed | slivers kept | between: removed | between: kept defective | between: kept deformed | between: kept | not clean, solids' sets | not clean, other sets | results emptied | results that differ from today |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| today | 463 | 0 | 1 | 1264 | 0 | 0 | 74 | 0 | 29 | 1616 | 0 |
| no removal | 0 | 139 | 1432 | 0 | 16 | 508 | 814 | 30 | 182 | 0 | 1772 |
| (a) at 4, after the edits | 2 | 138 | 717 | 2 | 16 | 508 | 812 | 8 | 97 | 374 | 1302 |
| (a) at 4 | 2 | 138 | 1 | 0 | 16 | 508 | 814 | 0 | 45 | 695 | 973 |
| (a) at 256 | 138 | 51 | 1 | 557 | 2 | 221 | 558 | 0 | 31 | 1048 | 607 |
| (a) at 2^16 | 280 | 0 | 1 | 921 | 2 | 26 | 389 | 0 | 31 | 1307 | 329 |
| (b) | 0 | 139 | 1097 | 16 | 0 | 508 | 814 | 20 | 112 | 222 | 1517 |
| **(c) at 4** | 0 | 139 | 1 | 16 | 0 | 508 | 814 | 0 | 29 | 707 | 963 |
| (c) at 256 | 136 | 52 | 1 | 559 | 0 | 221 | 558 | 0 | 29 | 1048 | 609 |
| **(d1) at 4** | 139 | 0 | 1 | 524 | 0 | 0 | 814 | 0 | 29 | 1018 | 633 |
| (d1) at 256 | 188 | 0 | 1 | 780 | 0 | 0 | 558 | 0 | 29 | 1163 | 482 |
| (d2) at 4 | 350 | 0 | 1 | 1245 | 0 | 0 | 93 | 0 | 29 | 1494 | 126 |

Reading it, as false removals (a solid of the inputs lost) and false keeps (a sliver kept, a kept component
defective or deformed):

- **Today:** 463 false removals; 1 sliver kept (untouched, 2.1 ulps; every rule keeps it); no kept component is
  defective or deformed.
- **(a) alone** keeps defective slivers above K: 16 more results not clean at K = 4 (45 against 29), 2 more at 256.
  Its two "lost" solids at K = 4 are the audit's cubes (the tiny rule). On the thickness after the edits it fails:
  the edits inflate slivers past the threshold (717 slivers kept).
- **(b) alone** has no false removal and keeps most slivers: 1097 of 1432 stay (the repair "cleans" a sliver by
  inflating it; 947 of the kept are deformed), and 132 results are not clean, most with inside-out components.
- **(c) at 4** has no false removal, the false keeps of today (1), and today's 29 results not clean (the same
  reasons: defects in components that are neither thin nor tiny). It keeps 139 mangled solids and 508 mangled
  "between" components: clean, closed, within the drift bound, and not what the exact result holds.
- **(d1) at 4** removes exactly those: no kept component is defective or deformed. Its 139 false removals are the
  solids of 4.4, which no removal rule can return intact. 324 of today's 463 lost solids come back; 287 of them with
  the volume within 1 % of the exact result's, 211 within 0.1 % (the rest are the corner contractions of 4.3 and the
  band next to 4.4).
- **(d2)** fails: a contraction along the sheet (4.3) has a drift of the sheet's thickness and changes nothing of its
  volume. Drift measures how far the surface moved, also within itself. 350 false removals.
- **K = 256 against 4:** 49 more solids lost under (d1) (sheets of 4 to 256 ulps that the repair leaves intact) and
  256 fewer "between" components kept. 256 is the multiple of the bound's own ulp term (`UlpFactor`, owner
  2026-11-02); 1 is the multiple of the untouched rule (#1067).

By band of thickness (`tables`, "Solids the inputs define" and "What is left between nearly coincident surfaces";
removed / kept defective / kept deformed / kept):

| class, rule | below 1 ulp | 1 to 4 | 4 to 256 | 256 to 2^16 | 2^16 to 2^32 | 2^32 and more |
|---|---|---|---|---|---|---|
| solids, components | 436 | 67 | 261 | 330 | 457 | 58 |
| solids, today | 372 / 0 / 0 / 64 | 51 / 0 / 0 / 16 | 136 / 0 / 0 / 125 | 142 / 0 / 0 / 188 | 165 / 0 / 0 / 292 | 20 / 0 / 0 / 38 |
| solids, (c) at 4 | 372 / 0 / 0 / 64 | 51 / 0 / 0 / 16 | 0 / 0 / 87 / 174 | 0 / 0 / 51 / 279 | 0 / 0 / 1 / 456 | 0 / 0 / 0 / 58 |
| solids, (d1) at 4 | 372 / 0 / 0 / 64 | 51 / 0 / 0 / 16 | 87 / 0 / 0 / 174 | 51 / 0 / 0 / 279 | 1 / 0 / 0 / 456 | 0 / 0 / 0 / 58 |
| others, components | 1103 | 329 | 567 | 391 | 326 | 54 |
| others, today | 1103 / 0 / 0 / 0 | 328 / 0 / 0 / 1 | 557 / 0 / 0 / 10 | 364 / 0 / 0 / 27 | 295 / 0 / 0 / 31 | 48 / 0 / 0 / 6 |
| others, no removal | 0 / 58 / 889 / 156 | 0 / 12 / 186 / 131 | 0 / 14 / 287 / 266 | 0 / 0 / 195 / 196 | 0 / 2 / 26 / 298 | 0 / 0 / 0 / 54 |
| others, (c) at 4 | 1103 / 0 / 0 / 0 | 328 / 0 / 0 / 1 | 14 / 0 / 287 / 266 | 0 / 0 / 195 / 196 | 2 / 0 / 26 / 298 | 0 / 0 / 0 / 54 |
| others, (d1) at 4 | 1103 / 0 / 0 / 0 | 328 / 0 / 0 / 1 | 301 / 0 / 0 / 266 | 195 / 0 / 0 / 196 | 28 / 0 / 0 / 298 | 0 / 0 / 0 / 54 |

(The solids kept below 4 ulps today are untouched ones far from the origin, where a thin sheet is a few ulps.)

### 5.3 The factor of (d1)

`tables`, "Rule (d1): the factor of the volume change" (computed in `thin.py` from the components; at the factor 2 it
gives the tool's own rule on 3474 of 3474 results):

| factor | solids lost (of 1106) | between: removed (of 1338) | between: kept |
|---:|---:|---:|---:|
| 1.001 | 252 | 1085 | 253 |
| 1.01 | 176 | 940 | 398 |
| 1.1 | 154 | 795 | 543 |
| 2 | 139 | 524 | 814 |
| 10 | 95 | 487 | 851 |
| 1000 | 26 | 341 | 997 |

The solids are nearly bimodal (154 lost at 1.1, 139 at 2, and the same 139 for every factor from 1.85 to 2.8): a
solid is either left alone or mangled. The "between" components are not; for them the factor is a second threshold.
It is the owner's knob like K.

### 5.4 The guarantees

| guarantee of SafeRounding today | (a) | (b) | (c) | (d1) |
|---|---|---|---|---|
| Closed, manifold | kept: whole closed components go, as today | kept | kept | kept |
| `RoundingClean` true means no intersection, no degenerate triangle, no contact made by the rounding | kept as a verdict; true on fewer results (16 fewer at K = 4) | kept | **the same verdict as today on every measured result**: a defective component that is thin or tiny goes under both, any other stays under both; two cases outside the sets below | as (c) |
| `NotCleanReasons` empty | 16 more results not clean | 103 more (inside-out slivers) | the same 29 | the same 29 |
| Drift: the kept surface within `maxLength` of the rounded exact result | kept; fewer components are exempt from it | kept | kept | kept |
| Volume: removed components add theirs | below K ulps x area / 2 per removed component | no bound | below K ulps x area / 2, or of a defective component | as (c), or of a component whose volume the repair had changed by more than the factor |
| The 2^k scales give the same result bit for bit | kept if the comparison is made as `numerically_zero_volume` makes it (exact volume against K ulp x the computed area) | kept | kept | kept with a power of two as the factor and exact volumes |

The removal was never inside the drift bound: the bound is a distance between the kept surfaces, and a removed
component is exempt from it (the class comment says so; the certified-mode spike showed that a checker without that
exemption rejects both audit drops, [BOOLEAN_CERTIFIED_MODE_2026-11.md](BOOLEAN_CERTIFIED_MODE_2026-11.md) 4.4). On
every measured result the candidates with K up to 256 remove a subset of what today's rule removes, so they have
fewer exemptions. The thickest removed component of the whole run falls from 9.6e-6 of the size to 1.7e-8 under (c)
and 9.2e-8 under (d1) (the audit's cube of 2^-11), the largest removed volume from 5.7e-6 of size^3 to 5.6e-9.

Two cases the sets do not hold, where a candidate as the tool writes it would differ from today's verdict; a built
rule has to settle both:
- *`ScaleDisparity`.* Today an edited thin component with real volume for its area is kept and counted, and the
  result is not clean. The tool applies that guard after the candidate's predicate, so such a component of 4 ulps or
  more without a defect would stay uncounted, and the result would be clean. No result here has a disparity under any
  rule. To keep today's verdict the guard must be evaluated on today's test (thin by the bound), not on the candidate's.
- *`KeepWhenEmptied`.* Today nothing goes if everything would. A candidate that removes less no longer empties the
  result, so it removes its (smaller) set where today keeps all and reports `ScaleDisparity`. The flag is set only
  in `plate` here, and no result of it is emptied.

Also: a candidate measures the thickness before the edits and today's rule after them, so "a subset of today's" is a
measured statement, not one by construction.

Not checked here: the cohort and hash gates of the library (263 hashes, the ABC and Thingi10K cohorts). None of the
earlier notes' real results removed a component of one cell or more, so the expected change on them is small; that is
a statement to measure when a rule is built, not a result.

### 5.5 Cost

- (b) and the defective part of (c): nothing. The list of degenerate triangles and the intersecting pairs exist when
  the removal runs.
- The thickness before the edits and the volume of (d1): area, volume and largest coordinate per component, taken
  before the first edit. `thin cost` times that pass as the tool makes it (`connected_components` and three vertex
  reads per triangle, the median of 9 passes), against the boolean of the same case, single runs:

  | case | triangles | boolean | of it rounding | the pass | of the rounding | of the boolean |
  |---|---:|---:|---:|---:|---:|---:|
  | scale:41:Union | 27 486 | 220 ms | 108 ms | 2.4 ms | 2.2 % | 1.1 % |
  | scale:81:Union | 107 276 | 851 ms | 438 ms | 21.5 ms | 4.9 % | 2.5 % |
  | scale:161:Union | 423 736 | 1686 ms | 551 ms | 28.0 ms | 5.1 % | 1.7 % |
  | scale:161:Difference | 387 896 | 1915 ms | 709 ms | 24.6 ms | 3.5 % | 1.3 % |

  An upper bound: the repair already walks the components once for their boxes (`component_scales`), where the sums
  can be taken, and they are needed only for closed components that are thin or tiny at the start (none on these
  four results). Against the rounding's share of a boolean (3 to 77 %, median 22 %, on the 33 rotated Thingi10K
  operations of PR #1390) it is noise.
- The exact comparison (volume against K ulp x area) runs only for components near the threshold, as today's
  zero-volume test does.

## 6. What the "between" class is

### 6.1 The copies, by their distance

`tables`, "Nearly coincident copies" (72 cases per distance; removed / kept defective / kept deformed / kept):

| distance, ulps | thin components | thickness, ulps: median, largest | today | (b) | (c) at 4 | (d1) at 4 | results not clean: today, (b), (c), (d1) |
|---:|---:|---|---|---|---|---|---|
| 1 | 204 | 0.28, 1.1 | 204 / 0 / 0 / 0 | 62 / 0 / 142 / 0 | 204 / 0 / 0 / 0 | 204 / 0 / 0 / 0 | 0, 15, 0, 0 |
| 4 | 239 | 0.4, 2.3 | 239 / 0 / 0 / 0 | 66 / 0 / 139 / 34 | 239 / 0 / 0 / 0 | 239 / 0 / 0 / 0 | 0, 6, 0, 0 |
| 16 | 269 | 1.3, 9.1 | 268 / 0 / 0 / 1 | 19 / 0 / 180 / 70 | 197 / 0 / 38 / 34 | 235 / 0 / 0 / 34 | 0, 2, 0, 0 |
| 256 | 174 | 42, 140 | 173 / 0 / 0 / 1 | 2 / 0 / 86 / 86 | 13 / 0 / 81 / 80 | 94 / 0 / 0 / 80 | 0, 0, 0, 0 |
| 2^14 | 228 | 1500, 8700 | 218 / 0 / 0 / 10 | 2 / 0 / 114 / 112 | 2 / 0 / 114 / 112 | 116 / 0 / 0 / 112 | 0, 0, 0, 0 |
| 2^24 | 110 | 1.2e6, 8.9e6 | 100 / 0 / 0 / 10 | 2 / 0 / 12 / 96 | 2 / 0 / 12 / 96 | 14 / 0 / 0 / 96 | 0, 0, 0, 0 |
| 2^30 | 96 | 2e8, 5.7e8 | 92 / 0 / 0 / 4 | 0 / 0 / 0 / 96 | 0 / 0 / 0 / 96 | 0 / 0 / 0 / 96 | 6, 6, 6, 6 |
| 2^36 | 108 | 5.5e9, 3.7e10 | 100 / 0 / 0 / 8 | 0 / 0 / 0 / 108 | 0 / 0 / 0 / 108 | 0 / 0 / 0 / 108 | 0, 0, 0, 0 |

- **Copies 1 to 4 ulps apart** (what the rule was built for, owner 2026-11-16): their slivers are below 4 ulps and
  go under every rule with an ulp part. (b) alone keeps two thirds of them.
- **From about 2^8 ulps** the difference of the copies is a thin shell that doubles hold without a defect: about
  half the components stay recognisable when nothing removes them, the other half is mangled (their own
  triangles are degenerate, 4.4) and goes under (d1).
- **From 2^30 ulps** (1e-7 of the size) none is mangled. Today 192 of 204 go. (The six results not clean there
  have intersections in components that are neither thin nor tiny, under every rule.)
- **"Kept" is not "unchanged" for these shells.** They taper to nothing at their rim, and the repair contracts the
  rim, within its bound. Of the 814 "between" components that (c) and (d1) keep, 146 have the volume of the exact
  result to 1e-6, 399 to 1 %, 543 to 10 %, and 271 differ by 10 to 98 % (median 32 %; most of them the crescents
  of a turned sphere). Of the 967 solids kept, 829 are unchanged to 1e-6 and 930 to 1 %.

### 6.2 The sets the rule was built for

On `clusters`, `utm`, `booleans` and `cylinder` (516 cases, 156 removed components today, all below 0.13 ulp), every
rule with an ulp part removes the same 156 components as today, and the results that are not clean are the same 23.
The candidates change nothing there.

### 6.3 What this means for the decision of 2026-11-16

"The slivers of A minus A′ go" holds under (c) and (d1) for copies a few ulps apart. For copies farther apart the
result is a thin shell, and under (c) and (d1) it comes back where the repair leaves it intact. Whether a shell of
1e-7 of the size is noise is then not the rounding's call; section 9.

## 7. Plan #1259: a guaranteed 3D rounding

The paper was read in full and its bounds measured in
[SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](SNAP_ROUNDING_3D_GUARANTEE_2026-11.md) (2026-11-22, after the plan was
written). This section adds what #1216 asks of it.

**Does it remove the need for the removal rule? No, for three reasons.**

1. *It loses more of these solids, not fewer.* The scheme merges sheets closer than a grid cell and keeps a slab from
   about two cells (that note, 4.2; an inference from the paper's steps). On the grid that fits the drift bound and
   the degenerate test (2^-19 of the diagonal, about), `tables`, "Against a grid rounding":

   | solids of 4 ulps and more | all | 2 cells and thicker | 1 to 2 cells | below 1 cell |
   |---|---:|---:|---:|---:|
   | thin, tiny or of zero volume by today's tests | 1106 | 108 | 6 | 992 |
   | removed today | 463 | 23 | 1 | 439 |
   | removed today, kept by (d1) at 4 ulps (by (c): with the volume within a factor 2) | 324 | 23 | 1 | 300 |

   Of the issue's three sheets (cell 4.9e-4) only the 1e-3 one is two cells thick. A grid as fine as the coordinates
   would keep more, and then every distinct coordinate is its own slab (its 4.3: 0.5 to 0.9 sqrt(n) times the
   triangles).
2. *It needs a removal rule of its own.* Where it merges two sheets the output is a non-manifold complex; making that
   a closed surface again means deciding which thin part goes: the same decision, moved from components to parts of
   components, with no theorem.
3. *The rule is not what a guarantee is missing.* The 324 solids are representable and clean in doubles after the
   repair we have; they are lost to a rule, not to a rounding.

**Cost against today's rounding.**

- Today (PR #1390, master `57623890`, 33 rotated Thingi10K operations): the rounding is 3 to 77 % of a boolean,
  median 22 %, 58.0 of 226.5 s; its levers are bounded by 16 % of the summed booleans.
- The scheme's output, counted and not built (the guarantee note, 4.3): at least 16 to 258 times the triangles of an
  ordinary result of 300 to 420 000 triangles on the grid above, 16 to 370 times on a grid as fine as the
  coordinates; every vertex moves, by up to 2.6 cells.
- In time, an estimate only: at scale:161 union the lower bound is 63.7 million triangles for a result of 424 000
  that took 1.5 s, 0.4 s of it rounding, in the guarantee note's run (1.7 and 0.55 s in the run of 5.5). At the throughput of today's whole boolean (0.29 million result
  triangles per second there) writing that output alone is about 220 s, 600 times the rounding it replaces; the
  exact 2D arrangements of all faces in two projections come on top. The verifying search, which is what the
  rounding costs today, would run on 150 times the triangles if the certificate per result is kept.

**Implementation sketch (from the paper only; no code exists anywhere).**

1. Grid: a power of two g >= ulp(largest coordinate), from the drift bound and the degenerate test (the guarantee
   note, section 3). Exact integer coordinates from there on.
2. General position by four nested symbolic perturbations (a shift in x, one in y, a scaling of y, a rotation). Our
   exact results are full of axis-parallel faces, so the degenerate branches are the common case.
3. Collapse along y: for each ordered pair of faces, cut out what lies within one unit along y and replace it by its
   projection, with connecting faces. New exact code; the step that merges thin solids.
4. Slabs along x: the arrangements of all projected edges in xy and in xz, lifted back onto all faces. Building
   blocks: the exact 2D arrangement of `ExactMeshArrangement` and the 2D mesher.
5. Triangulate the thin slabs by 2D snap rounding (`comp_geom/SnapRounding2.cs` exists) and the thick slabs as
   trapezoids; snap every vertex to its voxel centre.
6. Not in the paper: turn the complex back into a closed manifold surface (drop doubled sheets, re-pair the faces
   around the seams), then `NotCleanReasons`.

Risks: step 6 has no proof and is the removal question again; steps 2 and 3 are new exact code of tier L (bounds
review, mutation shards) with no reference implementation to test against; the output size rules out the global
form, and a local form needs a verified seam that grows on failure, the structure closed as option B (#313); the
result changes every vertex of every boolean, against D′.

**Recommendation: no-go for #1259, phase 1 included.** The plan's phase 1 (2 to 3 days, 5 RC4b cases and 10 rotated
ones) cannot include step 3 in that time, and step 3 is the part in question. Stop rule, replacing the plan's: the
plan stays parked until both hold: (1) its trigger, a corpus or caller where SafeRounding leaves defects on clearly
more than the five RC4b differences and the 1 of 16 239 pairs; (2) a written answer to "what is a clean result where
two sheets are closer than the bound", because the scheme merges exactly there. If it is ever started: stop at the
first RC4b cluster whose patch after step 3 is not a closed manifold surface, or whose seam needs more than three
rounds of growth. #1216 is not a trigger: it is solved, or not, by the rule.

## 8. Options for the owner

| # | Option | What it does on the measured sets | Effort | Risk |
|---|---|---|---|---|
| 0 | Keep the rule; report what it removed (count, volume, thickness per component) and count the result as not clean, as decided on 2026-11-22 (the opt-out of that decision is not treated here) | no solid comes back; 1772 of 3474 results here would report a removal, 975 of them one of 4 ulps and more | S (report fields) | none to outputs; "not clean" on results that are clean today is a visible change |
| 1 | **(d1) at 4 ulps**: an edited closed component goes if it is thinner than 4 ulps, or thin or tiny by today's bound and either still defective or changed in volume by more than a factor 2 | 324 of 463 lost solids back, 139 still lost (4.4); no kept component defective or deformed; the same results not clean as before the decision of 2026-11-22 (with it, every result with a removal is not clean: 1171 here); one threshold in every frame (4.3); 633 results differ from today, 355 still remove a component of 4 ulps and more (all mangled or defective) | S-M in `MeshRoundingRepair` (the sums before the edits, the exact comparison as #1191); tier L process, because outputs change: the 263 hashes, the cohorts, the rounding shards | **an output change (owner).** It narrows the decision of 2026-11-16 to copies a few ulps apart (6.3); thin shells of copies farther apart come back |
| 2 | (c) at 4 ulps: as 1 without the volume part | as 1, and the 139 solids and 508 "between" components the repair mangled stay, clean and within the bound; 963 results differ | S | returns solids that are not the exact result's (a sheet of 175 times its volume); simpler to state |
| 3 | K = 256 instead of 4 in 1 or 2 (the bound's own ulp multiple) | 49 more solids lost under (d1), 256 fewer "between" components kept | as 1 or 2 | a sheet of 5 to 256 ulps is lost when edited and kept when not, as today |
| 4 | (a): an ulp threshold alone | 16 more results not clean at K = 4, 2 more at 256 | S | trades lost solids for results that are not clean |
| 5 | (b) alone | 1097 of 1432 slivers stay, 103 more results not clean | S | not recommended |
| 6 | A degenerate area relative to the component, for the solids of 4.4 and the audit's cube | not measured here | M, and it changes what "degenerate" means in a result | its own study; independent of 1 to 5 |
| 7 | A guaranteed grid rounding (#1259) | loses 439 of the 463 solids again; 16 to 258 times the triangles | L | no-go (section 7) |

How others solve it:
- *Exact or tolerance-free mesh booleans* (CGAL corefinement, libigl after Zhou et al. 2016) keep the exact result
  and remove nothing; small components are a separate, opt-in filter by size (CGAL's
  `remove_connected_components_of_negligible_size`: area and volume thresholds, by default 1 % of the bounding box
  diagonal squared and cubed, each can be switched off; from its documentation).
- *Rounding with separation* (Milenkovic and Sacks 2019) removes components thinner than its bound, as here; Valque
  and Lazard 2025 round, verify and repeat without removing components.
- *Tolerance-based kernels* merge what is below their tolerance and do not report it (Manifold; the audit note, 4.5).
  CAD kernels measure against a fixed absolute model resolution rather than a fraction of the model: Jackson (Solid
  Modeling 1995, by a search snippet) gives 5e-9 m as the fixed global tolerance of one.
- *Sliver removal as an explicit user tool* with a stated width (the defeaturing tools of CAD and FEM
  preprocessors).
- No library found that removes thin solids by default at 1e-5 of the size and reports success. Terms searched on
  2026-11-22: "remove connected components negligible size threshold default", "boolean tolerance merge features
  epsilon", "sliver faces sheets boolean tolerance model resolution".

**Leaning: option 1, with the report of option 0 for what still goes; K = 4 and the factor 2 as the starting values,
both the owner's.** It is the only measured rule with no false removal that a removal rule can avoid, no kept
component that is defective or mangled, no new result that is not clean, and one threshold in every frame, which
is what the decision of 2026-11-22 asks for. That decision's "the result is not clean" is not part of the rule: it
applies to whatever the rule still removes. Option 2 is the fallback if the volume test is unwanted; option 6 is the follow-up
for what both leave. Open for the owner in either: whether a result that removed only components below K ulps is
"not clean" (under option 1, 816 of the 1171 results with a removal removed nothing else).

**Decision (owner 2026-11-22, through the coordinator):** option 1, rule (d1), with K = 4 ulps and the factor 2 as
the starting value. A result that removed only components below K ulps stays clean and reports the removal; any
other removal makes the result not clean, as decided earlier that day; every removed component is reported with
count, volume and thickness (option 0). The build is a separate tier-L PR after #1382, with a plan that settles the
two cases of 5.4 first. Nothing of it is in this note's PR.

## 9. Interplay with `SolidBooleanPipeline.MinThickness`

`MinThickness` (default 0) removes the solids of the result below a caller's length, measured by the same 2 V / A,
and lists them (`Report.RemovedThinSolids`). Its doc says the exact boolean keeps thin solids and the caller decides.
Its default and semantics are an open, parked owner decision; nothing here decides it. What each candidate means:

| rule | what `MinThickness` sees |
|---|---|
| today | Two filters in a row. The rounding has already removed the edited solids below 1e-5 of the diagonal, unlisted, so a `MinThickness` below that (or 0) cannot keep them, and its doc sentence is wrong for them. Above 1e-5 of the diagonal `MinThickness` alone decides. The answer to "are sub-millimetre layers artifacts" is given by the rounding, per frame |
| (a), (c), (d1) at K | The rounding removes only what the coordinates cannot hold (K ulps) or what it could not repair or mangled. Everything else reaches `MinThickness`, which becomes the one place where a thin solid is called an artifact; its doc sentence is true. With the default 0 the thin shells of 6.3 are in the result |
| (d1) | As above, and what it still removes is listed with volume and thickness, the same shape as `RemovedThinSolids` |
| (b) | Slivers of a few ulps reach `MinThickness` too: a caller would need it to get a clean result |
| a grid rounding | A third, coarser filter (one to two cells) in front of both |

Consequences for the parked decision, stated and not decided: under option 1 or 2 the question "should sub-millimetre
layers go by default" becomes a question about the default of `MinThickness` only, in the caller's units, with a
report; under today's rule a non-zero default would mostly duplicate what the rounding already did near the origin
and add to it far from it. If the owner wants thin shells of nearly coincident copies gone by default as on
2026-11-16, that is expressible as a `MinThickness` default (or `RoundingAbsoluteTolerance`, which already widens
`maxLength` and with it the "thin or tiny by today's bound" part of (c) and (d1)), not as a property of the rounding.

## 10. Limits of this study

- Procedural inputs only: boxes, a cube-sphere, the generated degenerate families. No CAD or scanned data was run;
  the real-data statement of #1216 (sheets of 0.1 to 0.18 mm) is the issue's, not measured here.
- The rules are replayed outside the library on the repair without the removal; in the library the removal runs once
  at the end, so the replay is the same computation (checked for today's rule on every result), but a built rule may
  differ at exact thresholds (the tool compares in doubles where the library would compare exactly).
- No measured result has a `ScaleDisparity` component, and none is emptied with `KeepWhenEmptied` set: the two
  cases of 5.4 are argued from the code, not measured. A compact thin part with real volume and a thin input are the
  families to add when a rule is built.
- "Edited" in (d2) used the repair's largest drift over the whole mesh, an upper bound for the component.
- The classes are by construction and by scale; whether a "between" component is wanted is the decision, not a
  measurement.
- Times are single runs on a shared container.

## 11. Reproduce

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release
cd benchmarks/SnapRoundingExperiment
python3 thin.py selftest
python3 thin.py run out/thin -j 4          # all nine sets, about two minutes; the same files bit for bit
python3 thin.py tables out/thin            # every table above (trace/results/thin_2026-11-22/tables.txt)
dotnet bin/Release/net10.0/SnapRoundingExperiment.dll thin issue:1e-3:near issue:1e-3:far    # single cases
dotnet bin/Release/net10.0/SnapRoundingExperiment.dll thin dump issue:1e-3:near              # the sliver pair
dotnet bin/Release/net10.0/SnapRoundingExperiment.dll thin cost scale:161:Union              # section 5.5
```

## 12. Sources

- O. Devillers, S. Lazard, W. Lenhart, *3D snap rounding*, SoCG 2018 (paper only; via the guarantee note).
- V. Milenkovic, E. Sacks, *Geometric rounding and feature separation in meshes*, CAD 2019 (the removal of thin
  components).
- CGAL documentation, Polygon Mesh Processing, `remove_connected_components_of_negligible_size` (documentation only,
  no code read).
- D. J. Jackson, a paper on tolerances in boundary representation modelling, ACM Solid Modeling 1995 (by a search
  snippet only: the fixed global tolerance; the full text was not read).
- Repository: [SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md) sections 1 and 5, issues #985, #1067, #1191, #1216,
  plan #1259, PR #1304 (audit), PR #1390 (cost).
