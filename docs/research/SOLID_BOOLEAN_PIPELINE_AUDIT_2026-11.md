# SolidBooleanPipeline: end-to-end audit, 2026-11

Owner request 2026-11-22: "get the best possible out of `SolidBooleanPipeline`". This is the research spike: an audit of
`mesh_ops/SolidBooleanPipeline.cs` and the stages it chains, measured on a procedural adversarial corpus with an oracle
that shares no code with the pipeline. No library code is changed; the implementation of anything below needs the owner's
go.

- Library: master `e340ef5c` (2026-11-22). Harness, corpus, oracle and the raw output of every table:
  [benchmarks/SolidBooleanAudit](../../benchmarks/SolidBooleanAudit/README.md) (`results/*.txt`; each section names its mode).
- Read, not edited: the open contract PRs #1221 (cavity owner), #1222 (final check: contacts), #1223 (sheet role).
  Their code is not on master, so nothing here runs it; where an item concerns them, the measurement is of the geometry
  the option has to cover.
- Not done: no Thingi10K run (procedural inputs only), no change of any default, no speed work.

## 1. Method

Each case has the operands as given and the *intent*: the clean, closed, outward operands before the corpus damages them.
The truth at a point is the operation applied to "inside intent mesh i" (the signed crossing number of a ray, the oracle's
own arithmetic on doubles in a local frame); the pipeline's result is judged at 6000 points per run by its own crossing
number, by its volume where the case has one in closed form, and by its closedness. 60 cases in 7 classes, each in 10 frames
(identity, unwelded soup, 2^40, 2^-40, UTM offset, 1e12 offset, a generic rotation, rotation + UTM, rotation rounded to
float32, 16 x at UTM rounded to float32): 600 runs with the defaults. Every run that is not right is repeated with each
escalation (`ByNesting`, `AreaMajority`, `AllowInsideOutInputs`, `AutoRepair`, `AutoRepairThenRobust`, `MeshRobustRepair`
of the inputs first) and with the plain `ExactMeshBoolean`. Details and limits of the oracle: the benchmark README.

## 2. Result in one table (`corpus`)

| class | ok | refused, a setting gives the right result | refused, no setting does | wrong (Status Success) |
|---|---|---|---|---|
| base (7 cases) | 70 | 0 | 0 | 0 |
| nested (9) | 80 | 0 | 0 | 10 |
| touch (14) | 136 | 0 | 4 | 0 |
| thin (8) | 72 | 0 | 0 | 8 |
| open (9) | 0 | 80 | 10 | 0 |
| nonmanifold (6) | 60 | 0 | 0 | 0 |
| orientation (7) | 0 | 60 | 0 | 10 |
| all (60) | 418 | 140 | 14 | 28 |

What holds: outside the thin class (finding 1) and one subnormal coordinate (F7), every clean closed input gave the
right solid in every frame, including UTM and 1e12 offsets, 2^40 and 2^-40, exact and rotated contacts (face, partial
face, edge, vertex, identical operands, flush cuts, one ulp apart), hollow solids with islands, pockets, and operands that
are non-manifold or self-overlapping by themselves: 346 of 346 runs of the base, nested (without the outward cavity wall),
touch (without the subnormal case) and nonmanifold classes. No exception, no result that is not closed or that covers
itself, no run above 20 s (the whole corpus takes 100 s). The float32 frames behave like the double ones: the pipeline is
right about the solid it is given.

The 28 wrong and 154 refused runs are four things: thin solids removed by the rounding (finding 1), a cavity wall given
outward (finding 2), open inputs (finding 5) and orientation (finding 4). The sampling does not see solids thinner than
about 1e-4 of the diagonal, so the corpus table undercounts finding 1; the modes `small` and `tiny` measure it directly.

## 3. Findings, by severity

### F1. Small and thin solids vanish silently (high; documented design, undocumented reach, no report)

`SafeRounding` removes closed components that are tiny or thin relative to the largest component
([SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md) section 1; owner 2026-11-16 for the slivers of A - A'). Measured, the rule
reaches valid solids, and the pipeline returns `Status Success` with nothing in `SolidBooleanReport`:

- **A well-shaped input solid in a union** (`tiny`): the box [0,1024]^3 united with a cube of edge a at x = 1100. The cube
  is in the result for a >= 2^-8 and gone for a <= 2^-9, in the identity, rotated and rotated + UTM frames alike: an edge
  below 1.1e-6 of the large solid's diagonal (2 mm beside a 1 km solid, 2 um beside a 1.8 m part). 12 triangles come back,
  `Report.Clean` true, `RoundingClean` true. The cube is untouched input; it goes because its triangles are below the
  degenerate area (1e-12 x diagonal^2 of the whole result), which marks it as defective.
- **A thin solid made by the boolean** (`small`): [0,2]^3 intersected with [2-h,4] x [0,2]^2 is a 2 x 2 x h slab. Axis
  aligned it is returned down to h = 2^-35 and dropped from 2^-40; after one generic rotation it is dropped from
  h = 2^-15 (1.1e-5 of the diagonal; 2^-14 stays), at the origin and at UTM. The wall a Difference leaves goes from
  2^-15 when rotated at the origin and from 2^-16 when rotated at UTM. (With the UTM offset alone the slab is kept to
  2^-30; below that the coordinates there no longer hold it, which is the input's limit, not the rounding's.)
  The result is empty, `NotCleanReason` "empty". The threshold moves by six orders of magnitude with a rotation, because
  the removal applies only to components in which the rounding left something to repair.
- **A thin input plate in a union**: 2 x 2 x h beside a cube is kept down to 2^-30 and dropped from 2^-35 in every frame.

The only trace is `ExactMeshBoolean.Rounding.RemovedComponents`, which a caller reaches by capturing the boolean in
`ConfigureBoolean` (`report` shows it). Two statements in the docs are wrong against this: the class comment says the
result is "exact up to the rounding of the new vertices, within the relative bound of StrictRounding, or the run fails",
and `MinThickness` says the exact boolean keeps thin solids.

Reproducers: `-- tiny`, `-- small`, `-- report` (second entry). Reported to the coordinator on 2026-11-22.

### F2. A cavity wall given outward is filled (high for the caller; documented winding semantics, no signal)

A hollow box whose inner shell points outward (both shells with positive volume) has winding number 2 in the cavity. With
the default `Orientation = Refuse` the pipeline takes it as solid: volume 65 instead of 57, `Success`, `Clean` true, every
report field zero (10 of 10 frames). `Orientation = ByNesting` gives the hollow solid in all 10. If the same mesh also
self-intersects anywhere (a box poking through the wall), no pipeline setting gives it (10 of 10 wrong with `ByNesting`
too, as its doc says: nesting per component is not implemented); only `MeshRobustRepair` of the input first does, in 7
of the 10 frames.

Reproducers: `-- corpus "cavity"` (both cases).

### F3. A triangle target without `MaxDeviation` has no fidelity guard (high for the caller; as specified)

`TargetFraction = 0.25` with no `MaxDeviation` (`reduce`, 55 runs on results with more than 12 triangles): 43 of 55 lose
more than 10 % of their volume. Flat-faced solids are already minimal, and the count target keeps collapsing: box U box
15 -> 3.72, two vertex-touching boxes keep 3.4 %. `Status Success`, `Clean` true, `StopReason TargetReached`; the report
has no measure of the change. With `MaxDeviation` = 1e-3 of the diagonal the volume ratio stays in [0.997, 1.000] in all
55 and the reduction stops where it must (36 -> 36, a face-touch union 20 -> 12). Curved results (spheres, torus) at 25 %
keep 96-99.8 % without the bound.

Reproducer: `-- reduce`.

### F4. Orientation refusals all have a right answer (medium)

Inside-out operands, a component inside-out, flipped triangles: 60 of 60 runs are refused (`InsideOutInput`,
`InconsistentOrientation`), and `ByNesting` gives the right solid in all 60. `AreaMajority` does in 10 (one flipped
triangle) and is refused as inside-out for half-flipped faces, as documented. `AllowInsideOutInputs` returns a wrong solid
with `Success` in all 40 inside-out runs (that is what it is for). The error texts name the input and the option to set;
they are sufficient to act on. `ByNesting` on clean inputs adds 22-62 % to the pipeline time (`perf`: 6.2 -> 8.4 s and
5.1 -> 8.2 s at 1.56 M triangles, 1.5 -> 2.2 s at 387 k), above the 25 % bound the owner set for a default.

### F5. Open inputs: `Refuse` is the right default, and the message could say more (medium)

All 90 runs are refused with `OpenInput`. In 80 `AutoRepair` gives the right solid, so a right answer exists; in the 10 of
the cutting sheet every repair returns a confident wrong one (the cube uncut, volume 64 instead of 32, `Success`, `Clean`
true, `RepairedInputs` 1), and no setting on master gives the half cube (#1211, the sheet role of #1223). A repair cannot
know which of the two an input is, which is the case for `Refuse`. For five of the nine cases the plain `ExactMeshBoolean`
(winding number above 1/2) gives the right closed solid without a repair: a face missing wholly inside the other operand
of a union, a duplicated face and a debris triangle in 10 of 10 frames, a zero-area triangle in 7, an exact T-junction in
the 6 frames where it stays exact (after a rotation it is a real crack). `Error` does not name the open input (it is in `Report.InputClosed`), nor how open
it is.

### F6. `ConfigureBoolean` can switch the guarantees off (low, small fix)

The pipeline forces `AllowOpenMeshes = false` and `StrictRounding = true` after `ConfigureBoolean`, but not the two
settings its promises also rest on (`report`): with `SafeRounding = false` the strict mode has nothing to check and a
result with degenerate triangles is returned as `Success`; with `CountInsideOutCells = false` an inside-out input is taken
as empty, `Success`, `Clean` true (volume 8 instead of 15).

### F7. Report and status warts (low)

- `Report.Clean` is true on a failed run (the reason defaults to ""), eg with `Status OpenInput`.
- An empty result is `Success` with `Clean` false and reason "empty", the same for a legitimate empty intersection and for
  a result the rounding emptied (F1).
- The facts of the rounding (`RemovedComponents`, `MaxDrift`, `ReorientedTriangles` of the boolean) are only on the
  boolean instance.
- Coordinates outside 2^-200 .. 2^200, or one subnormal coordinate, fail with `BooleanFailed` / `RoundingNotClean` and a
  message that starts with "0 intersecting pairs, 0 degenerate triangles" and names the range last; clean boxes are
  refused from 2^199 up and 2^-202 down (`negvol`, last column). It is an input property and could be `InvalidInput`.
- `PairContacts` (bool) and `PairContactsMode.On` are one option twice; `AllowInsideOutInputs` and
  `Orientation = ByNesting` overlap (the first keeps the inside-out reading, the second corrects it).

### Not a pipeline defect: float32 at UTM (see 4.6), and `MeshRobustRepair` on flipped triangles

`MeshRobustRepair` of a cube with one flipped triangle returns a solid that differs from the cube at 7.5 % of the sample
points, and with half of the faces flipped at 58 % (`repair`); `MeshAutoRepair` fixes the first and not the second. The
pipeline never sends such an input to a repair (the boolean refuses it as inconsistently oriented first), so this is a
note for the repair topic, not diagnosed here.

## 4. The open owner items, measured

Each item: what was measured, the options, how others solve it, and a leaning.

### 4.1 #1221 touch tolerance: A (256 eps M of the shell's box), B (relative to the largest input magnitude), C (4096)

Measured (`touch`): a point in a wall triangle, both exact, sent through a history of transforms in doubles; the exact
distance of the point from the wall's plane afterwards, in units of eps x M (M the largest coordinate), 4000 trials each
at M = 1, 1e3, 5.5e6, 1e9.

| history | largest distance / (eps M) | above 256 | above 4096 |
|---|---|---|---|
| 1 rigid step | 0.97 | 0 | 0 |
| 8 rigid steps | 16 | 0 | 0 |
| 64 rigid steps | 30 | 0 | 0 |
| away by 1e8 M and back | 3.9e8 | 99-100 % (M <= 5.5e6) | 99-100 % |
| away by 1e12 M and back | 5.5e12 | 99-100 % (M <= 1e3) | 99-100 % |
| float32 | 4.6e8 | 97-100 % (M <= 5.5e6) | 97-100 % |
| text, 6 decimals | 1.7e9 at M = 1; 6.8e3 at M = 5.5e6; 20 at M = 1e9 | 100 %; 54 %; 0 | 100 %; 0.08 %; 0 |
| text, 3 decimals | 1.6e12 at M = 1; 5e6 at M = 5.5e6; 4.8e4 at M = 1e9 | 100 %; 100 %; 92 % | 100 %; 99 %; 9 % |

(Trials in which the history collapses the wall triangle itself are not counted and are listed in the raw table: all
4000 for the 1e12 excursion at M >= 5.5e6, about 3700 of 4000 for the 1e8 excursion and float32 at M = 1e9; of the few
left there, 1-3 % are above every factor.)

- **A** covers every rigid history measured with a factor of 8 to spare at 64 chained steps.
- **C** gains in two cells, both text coordinates whose quantum happens to lie between the two factors: 6 decimals at UTM
  magnitudes (46 % -> 99.9 % covered) and 3 decimals at 1e9 (8 % -> 91 %). It costs a cavity left unowned behind a wall
  thinner than 5e-6 m at UTM instead of 3e-7 m.
- **B** was not measured directly (no history here has a second, far input). By argument it gains on none of the
  measured ones: an excursion (away and back) leaves no trace in the magnitudes of the inputs, and float32 or text noise
  is not relative to the double magnitude at all. It costs margin for small shells near the origin whenever another input
  is far away.
- The histories no factor covers are quantised inputs. Their error is the quantum, which the caller knows: `InputGrid`.

How others solve it: Manifold keeps a tolerance per mesh, proportional to its bounding box and carried through its
operations (its API documents `SetTolerance` / `GetTolerance`); CGAL's corefinement has no tolerance (exact predicates:
touching is exact or absent); snap rounding ties the tolerance to the grid (Hobby 1999; Devillers, Lazard, Lenhart 2018).

**Leaning: A, unchanged.** If quantised inputs matter, use the larger of A and `InputGrid` when a grid is set (a follow-up,
after #1221), not a larger constant.

### 4.2 Nesting per component for `ByNesting`

Measured: one corpus case (F2, second half): a mesh that is not embedded with a cavity wall given outward is returned
filled, `Success`, in 10 of 10 frames with `ByNesting`. The upper bound of the feature on this corpus is that one case of
60; `MeshRobustRepair` first already answers it.

Options: (a) leave it documented; (b) make it a refusal: under `ByNesting`, a mesh that is not embedded and has a
component nested in another one is refused instead of keeping its orientation (the silent wrong result becomes a status;
small, in the orientation code of `ExactMeshBoolean`, an exact file); (c) nesting after the self-arrangement of the input
(resolve, then the parity of each cell), a tier-L change.

How others solve it: CGAL orients by nesting only for closed meshes without self-intersections
(`orient_to_bound_a_volume`, with `does_bound_a_volume` as the precondition); Zhou et al. 2016 (the mesh arrangements
behind libigl's booleans) resolve first and take the winding number per cell, which is (c).

**Leaning: (b)** now, (c) only on a demand that the repair route does not serve.

### 4.3 `negative_volume`: overflow above ~1e100, underflow below ~1e-100

Measured (`negvol`): a mesh that is not embedded with an inside-out box, scaled by 2^k. `ExactMeshBoolean` with
`ByNesting` refuses it (`InconsistentOrientation`, as documented) for 2^-359 <= scale <= 2^338, and from 2^339
(coordinates 3.4e102) up and 2^-360 (1.3e-108) down returns `Success` with the inside-out box taken as empty (volume 16 of
the unit frame; `RoundingClean` false because the rounding does not run outside 2^-200 .. 2^200). **The pipeline is not
exposed**: its strict rounding refuses every input outside that range (clean boxes from 2^199 and 2^-202), 140 binary
orders before the test breaks.

Options: (a) leave it (documented range); (b) scale the component by a power of two before the test (exact, bit
identical inside the range, a few lines in an exact file: exact review and a mutation run); (c) treat a non-finite or
zero norm product as "cannot tell" and refuse.

How others solve it: the standard remedy for overflow in a filter is exact scaling by a power of two before evaluating
(as `hypot` and the scaled norms of LAPACK do); nothing specific found for orientation tests (searched: "orientation
filter overflow scaling", "signed volume overflow power of two").

**Leaning: (b)**, low priority, bundled with the next exact change to that file; (a) is defensible.

### 4.4 The repair selection rule

Measured (`repair`): 13 damaged solids and one cutting sheet, each repaired by `MeshAutoRepair` and `MeshRobustRepair`,
against the intent.

- The order is right: `MeshAutoRepair` is at least as close to the intent as `MeshRobustRepair` in 14 of 14 (equal in 8)
  and faster in 13 (0-95 ms against 5-2900 ms).
- What is missing is acceptance. Both return a closed mesh for inputs whose intent a repair cannot know, and the pipeline
  then reports `Success`: two adjacent faces missing (10 % of the points wrong), half a sphere (15-17 %), the sheet (55 %).
- A cheap signal separates them among the inputs with missing surface (7 cases and the sheet): the area after the repair
  over the area given is at most 1.20 for the repairs that match the intent (largest: a whole cube face; at most 1.2 % of
  the points differ) and at least 1.44 for those that do not (two faces 1.59 / 1.44, half sphere 1.56 / 1.53, sheet 2.0 /
  2.24). It says nothing about flipped triangles (ratio 0.94 with 58 % of the points wrong), which the pipeline does not
  send to a repair. Eight cases are a small sample.

How others solve it: Manifold and CGAL's volume operations do not repair, they reject (`NotManifold`; the "bounds a
volume" precondition); hole-filling tools (MeshFix, Attene 2010; CGAL hole filling) leave the acceptance to the caller.

**Leaning:** keep the order (`MeshAutoRepair`, then `MeshRobustRepair`); add to `SolidBooleanInputRepair` the area given and after, and the number of boundary edges
of the input (no behaviour change). A refusal threshold on the ratio is a second step after a larger sample (open
Thingi10K meshes at run time).

### 4.5 The small solids `SafeRounding` drops

Measured: F1 (`tiny`, `small`). The mechanism, confirmed independently by the Boolean speed spike
(`research/exact-boolean-speed-levers`, its note section 8b; same threshold 2^-8 / 2^-9 on its own probe): the degenerate
scan of the component removal runs over all result triangles with one threshold, 1e-12 x (diagonal of the box of all
result triangles)^2 (the input plate of `small` is the same path: the union spans [0,5] x [0,2]^2, the threshold is
3.3e-11, and a side triangle of area h = 2^-35 is below it). Unchanged input triangles of a small solid fall below it, the component counts as defective and tiny, and it
is removed; no edit and no new vertex is involved. The removal and the clean verdict are therefore one criterion: with
the cube kept, `NotCleanReason` says "degenerate triangles" (12 of them; `report` shows the same for the rotated slab with
`SafeRounding` off). That spike also reports that none of its speed levers widens the drop (bit identical, including
`RemovedComponents`) and that its 294 T6 booleans removed no component.

Three options, as the coordinator asked to separate them:

- **(a) Reporting only** (no output change): `SolidBooleanReport` gets the components, triangles and volume the rounding
  removed, taken from the boolean; the two doc statements are corrected. A stricter variant, `Clean` / `RoundingClean`
  false whenever input or result volume was dropped, changes what a caller's check returns for results that are otherwise
  the same: with `StrictRounding` it turns today's `Success` into a refusal, so that part is (b).
- **(b) An opt-out or a threshold** (**output / default change, owner**): an option that refuses the run
  (`BooleanFailed` / `RoundingNotClean`) or keeps the component when the rounding would remove one whose thickness its
  coordinates resolve (a few ulps: that separates the solids of F1 from the zero-thickness residue of #1067). Opt-in it
  changes nothing by default; as a default it changes the results of F1 (every `tiny` row from 2^-9 and the `small`
  rows with ratio 0 above the coordinate resolution). A kept component is "not clean" by the public
  check unless the degenerate criterion is also made relative to the component.
- **(c) A rotation-stable criterion** (**output / default change, owner**): today the threshold for a thin solid is 1e-5
  of the diagonal where the rounding had something to repair and about 1e-11 where it had not, so one rotation moves it
  by six orders of magnitude. Two stable rules bracket the choice: remove by size alone (everything thinner than 1e-5 of
  the diagonal goes, also axis aligned: the `small` rows between 2^-15 and 2^-35 that are kept today would go), or never
  remove what the coordinates resolve (every `tiny` row and every `small` row above the resolution stays; the slivers of
  A - A', which the removal was decided for on 2026-11-16, come back as thin solids for `MinThickness` to filter). The
  speed spike's variant, "only components with a new triangle or an edited vertex are candidates", keeps untouched input
  solids (`tiny`, `U plate`) and still removes the rotated slab; it is stable for inputs, not for results.

How others solve it: CGAL and libigl (Zhou et al. 2016) keep the exact result and do no rounding repair (the rounded
output may self-intersect); Manifold merges features below its tolerance without a report; Milenkovic and Sacks remove
components thinner than their bound, as here. No library found that reports the removed volume. Whether a rounding can
keep such solids with a guarantee, and at what cost, is the subject of the 3D snap rounding spike (#1278 / #1259), and a
checker of "the right point set up to the rounding bound" (the certifying checker spike) would flag exactly this drop;
both have the two reproducers.

What the snap rounding spike found so far (its message of 2026-11-22; branch `research/snap-rounding-3d-guarantee`, tool
`benchmarks/SnapRoundingExperiment`, its own numbers, not re-run here): both cases reproduce at `ExactMeshBoolean` level
with the same thresholds. The cause is the removal rule, not the rounding: the same repair with `RemoveTinyComponents`
off ends clean (`IsClean`, the component still there) on all 8 removed cases it ran, down to the rotated slab of 2^-35,
so these solids are representable and clean in doubles. The rule is needed elsewhere: on 74 generated results with
removed slivers the same probe is clean on 12 and not clean on 62. A guaranteed grid rounding (Devillers et al.) would
not fix it: on the grid that fits the present bounds (about 2^-19 of the diagonal) it keeps the slab only from about two
cells of thickness (2^-16 here) and not the cube, at 16 to 258 times the triangles. (The two spikes differ on whether a
kept solid reads as clean: the repair's own `IsClean` says yes, `NotCleanReason` on the plainly rounded result says
"degenerate triangles"; the degenerate criterion relative to the whole result is the difference.) The certifying
checker spike has not reported yet.

**Leaning: (a) at once.** For (b) and (c) the owner decides; the leaning is the opt-in refusal of (b) now and, for (c),
the speed spike's rule for untouched input components (it removes the case nobody intended, a valid input solid deleted
from a union) once the degenerate criterion that goes with it is settled.

### 4.6 float32 STL at UTM coordinates

Measured (`utm32`, and the `x16+utm,f32` column of the corpus): at (500000, 5500000, 100) the float32 grid is 0.031 m in
x and 0.5 m in y. The pipeline is right about what it is given in all 18 runs (model sizes 1 m to 1 km, up to 97 zero-area
triangles in the inputs) and in every corpus case that is right in doubles. The damage is in the file: the float32 solid
differs from the double model at 11-22 % of the sample points for a 1 m model, 4-9 % at 16 m, 0.8-2.4 % at 256 m and
0.3-0.6 % at 1 km.
No orientation or repair setting changes the outcome, and `InputGrid` cannot restore what the file no longer holds (not
measured against the double model: its column is judged against the float32 solid, which a grid of 0.5 m moves further).
Recentring before the conversion to float32 leaves no damage (0 points differ) in all 18.

How others solve it: a local origin with a stored offset (IFC map conversion and local placement; the `RTC_CENTER` of 3D
Tiles); STL has no place for it, so the exporter has to subtract it.

**Leaning:** document it in the guide (recentre before a float32 export; the pipeline cannot undo it), and optionally
report a hint: all coordinates are float32 values and their grid at the model's magnitude is more than 1e-4 of the
diagonal (one pass over the vertices). No pipeline behaviour to change.

### 4.7 The default `Refuse`, with explicit roles

Measured: F5. Of 90 open runs a repair gives the right solid in 80 and a confident wrong one in 10, and the report of a
repaired run does not tell them apart. **Leaning: keep `Refuse`**; the roles of #1223 answer the 10 that no repair can.
After #1223 the `OpenInput` error should name the input, its boundary edges, and the two ways out (`InputRoles` for a
cutting surface, `InputRepair` for a solid with a hole).

## 5. Usability

- Status and reason are sufficient to act on for orientation (`InsideOutInput`, `InconsistentOrientation`: the text names
  the input and the option). They are not for F1 (nothing), F2 (nothing), F3 (nothing), and thin for `OpenInput` (no index)
  and the coordinate range (wrong status, cause last).
- Orthogonality: the two overlaps of F7; `ConfigureBoolean` reaching the settings the pipeline's promises rest on (F6);
  `MinThickness` (an explicit thin-solid filter, 2 V / A) coexisting with the rounding's implicit one (1e-5 of the
  diagonal, only where the rounding repaired) without a word on each other.

## 6. Performance, as an upper bound only (`perf`)

Without a reduction the pipeline takes 1.1 to 1.8 times the plain `ExactMeshBoolean` on two spheres (5 k to 1.56 M input
triangles; best of 3; stratum: clean smooth inputs, wall time including GC; 10-20 % noise between runs). The input check
and the final check together are 18-39 % of the pipeline's time (28 % at 1.56 M: 0.77 s + 0.96 s of 6.2 s). Both repeat
work the boolean does: it checks closedness itself, and with no reduction the final self-intersection check runs on a
result the rounding has verified exactly. The upper bound of sharing both is that 18-39 %, for runs without a reduction only; with a reduction the
final check is needed. Boolean speed work is stopped; this is a bound, not a proposal.

## 7. Improvement PRs, ranked

| # | PR | fixes | effort | risk |
|---|---|---|---|---|
| 1 | Report what the rounding removed (components, triangles, volume) and the volume before / after the reduction; `Clean` false on a failed run; name the open input; correct the class comment and `MinThickness` doc; the float32 note in the guide | F1 (visibility), F3 (visibility), F5, F7, 4.5 (a), 4.6 | S-M, pipeline file and docs, new report fields only | low (API additions; pack validates) |
| 2 | Force `SafeRounding = true` and `CountInsideOutCells = true` after `ConfigureBoolean` | F6 | S | low; a caller who switched them off on purpose gets the documented behaviour back |
| 3 | Opt-in refusal (or keep) when the rounding would remove a resolvable solid | F1, 4.5 (b) | M | low while opt-in; an output change as a default (owner) |
| 4 | `ByNesting`: refuse a mesh that is not embedded and has nested components | F2 (second half), 4.2 (b) | M, exact file: exact review, mutation run | medium (a refusal where a result came back before) |
| 5 | Repair acceptance signal in `SolidBooleanInputRepair` (areas, boundary edges) | F5, 4.4 | S | low |
| 6 | Coordinate range and subnormal inputs as `InvalidInput` with the range first | F7 | S | low for the pipeline; in `ExactMeshBoolean` it changes a status |
| 7 | `negative_volume` scaled by a power of two | 4.3 | S, exact file | low; bit identical inside the range |

Owner decisions these raise (none taken here):

1. F1 / 4.5 (b) and (c): should the rounding's removal be refusable or switched off, and should its criterion be the
   same in every frame? In particular: should an untouched, well-shaped input component be removable as "tiny" by the
   diagonal of another component? Today a 2 mm cube beside a 1 km solid is.
2. F3: a default `MaxDeviation` relative to the diagonal whenever a triangle target is set, or stay with reporting the
   change (PR 1). A default changes results.
3. F4: an orientation fallback (run `ByNesting` only after an `InsideOutInput` / `InconsistentOrientation` refusal: free on
   good inputs) as the default, or keep `Refuse`. `ByNesting` as the plain default costs 22-62 %.
4. F2 (first half): under `Refuse`, should a region that one input covers twice be reported or refused? It needs the
   per-input winding of the cells from the boolean (an exact file).

## 8. Sources

- Zhou, Grinspun, Zorin, Jacobson: Mesh Arrangements for Solid Geometry, SIGGRAPH 2016 (resolve, then winding number per
  cell; exact output, no rounding repair).
- Milenkovic, Sacks: Geometric rounding and feature separation in meshes, CAD 2019 (removal of components thinner than the
  bound).
- Hobby: Practical segment intersection with finite precision output, 1999; Devillers, Lazard, Lenhart: 3D snap rounding,
  SoCG 2018 (tolerance tied to the grid).
- Attene: A lightweight approach to repairing digitized polygon meshes, 2010 (hole filling without acceptance).
- CGAL Polygon Mesh Processing manual (documentation only, no code read): `orient_to_bound_a_volume`,
  `does_bound_a_volume`, corefinement preconditions. Manifold API documentation (tolerance per mesh, `NotManifold`).
- IFC 4 `IfcMapConversion`; 3D Tiles `RTC_CENTER` (local origin with a stored offset).

The prior-art lines are from the papers and public documentation as known to the author on 2026-11-22; no external code
was read or run for this note.
