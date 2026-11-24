# A degenerate triangle the rounding repair leaves, and why no edit removes it (#1465, 2026-11-24)

Diagnosis of issue #1465 on master f11323390 (after #1464, input order, and #1467, caps first). **No library change.** The
reproducer of the issue no longer fails (the order no longer reaches its state), so this note first finds a state of the same kind
that holds on this master, then lists every edit the repair considers on it and the rule that refuses each.

## Answer in short

- **A reproducer exists on this master**, 72 triangles, procedural (section 2). `ExactMeshBoolean` with `SafeRounding` ends with
  1 degenerate triangle, 0 intersecting pairs, 0 contacts, `SmallestRejectedDrift` infinite, the evaluation limit not reached.
  The state is the same in 5 other input orders (it does not depend on the order, #1464).
- **The two families of the issue have none**: `tessellations` (624 cases) and `plates` (396 cases) leave no degenerate triangle on this
  master; their 74 not-clean results are removed components that are resolvable (a different cause), and in one case 6
  intersecting pairs. A wider seeded sample (13 300 cases) finds 90 states of this kind, about 1 %.
- **Diagnosis**: the left-over triangle is the mouth of a thin double layer. It is a flat wedge of two nearly coincident faces
  (opposite orientation, 3.6e-10 apart at the corner, joined along one edge) on the wall of the plate, part of the main surface.
  At the end of the repair no edit of the repair's set is accepted on any of the 89 states examined (each edit tried on the final
  mesh): every contraction either violates the link condition (`DMesh3.CollapseEdge`) or would add an intersecting pair (a folded
  double face: two coincident triangles of opposite orientation), the flips are impossible (the diagonal exists) or add an
  intersecting pair or do not improve, the zips do not apply, and all 624 proposals of the position search leave the triangle
  degenerate. **It is not the order and not the budget.**
- **No rule can be relaxed soundly**: each refusal protects a real invariant (manifoldness; no new intersection; a net gain), and
  the edit that would be sound here (remove or flatten the wedge) is not an existing edit with a rule loosened.
- **A new edit is needed**, and the one that was probed works: a **joint move of two free corners** (section 5). In the minimal case 11 456
  of 16 900 joint moves leave no defect, 1 294 of them also without a turned-over triangle. It is not a small local change
  (a second moved vertex in `improves`, its drift and ghosts, a candidate set, a termination argument that holds
  for it). Not built; options in section 6.

## 1. What was searched

All with `ExactMeshBoolean` (default `SafeRounding`), the repair's own counts, one input order.

| search | cases | clean | left-over degenerate only | with intersections left | removed resolvable | other |
|---|---:|---:|---:|---:|---:|---:|
| `tessellations --orders 1` | 624 | 557 | **0** | 0 | 64 | 3 (scale disparity or none of the counts) |
| `plates --orders 1` | 396 | 389 | **0** | 1 (6 pairs) | 6 | 0 |
| `search --count 300 --seed 1` | 300 | 265 | 2 | 0 | 26 | 7 |
| `search --count 5000 --seed 2` | 5 000 | 4 347 | **35** | 0 | 542 | 76 |
| `search --count 4000 --seed 3 --tiny` | 4 000 | 3 379 | **53** (+1 with a resolvable removal) | 0 | 521 | 46 |
| `search --count 3000 --seed 7 --n 1` | 3 000 | not tallied | hits listed (smallest 44 and 72 triangles) | | | |

The `tessellations` and `plates` numbers are the issue's 81 of 1020 on master 083ef07b: now 74 of 1020, none of them a left-over
degenerate triangle, so the first class of the issue is gone from these two families and the second (removed resolvable) is not
this issue. The `search` mode (new in `benchmarks/BooleanInputOrder`) draws surface (bowl, wave, dome), grid n (3 to 8, or 1 to 3
with `--tiny`), the second grid larger by 1 to 3, thickness 0.02 / 0.002 / 0.2, rotation 10^-5 to 10^-9, the axis ratio of the second
rotation, a shift of both inputs by 0 / 100 / 10 000, and both ways round. Of the 88 hits of the seeds 2 and 3, 53 have an infinite
`SmallestRejectedDrift`, none stopped at the evaluation limit, 60 have 1, 17 have 2 and 11 have 3 degenerate triangles. In every hit
at least one corner of the triangle is an input vertex (fixed: 1 corner in 65 of 80 triangles of the tiny search, 2 in 15), every edge
is below 1e-6 (all but one of 129 triangles), no triangle is a cap by the rule of #1467 (their edges are too short for it).

## 2. The reproducer

The smallest with an infinite `SmallestRejectedDrift`: 12 + 60 triangles.

```
dotnet run --project benchmarks/BooleanInputOrder -c Release -- search --count 1 --case wave,1,2,0.2,2e-9,-1.1,0,D
```

In words: a plate of thickness 0.2 whose top and bottom follow `z = 0.05 sin(3x + 1) cos(2y)` over the unit square, on a 1 by 1 grid
(`A`, 12 triangles), minus the same plate on a 3 by 3 grid (`B`, 60 triangles) turned about its centre by 2e-9 about x and by -2.2e-9
about y (`Plate(n, t, h, p => Rot(p, e, ratio * e))` of the harness). `new ExactMeshBoolean(a, b, Difference)` ends with

```
defects 0/43/0 left 0/1/0 collapses 23 flips 1 moves 3 zips 0 strips 0 search 1 removed 2 resolvable 0 limit False NOT CLEAN
StrictRounding: RoundingNotClean rounding not clean within the bound 1.42E-05: 0 intersecting pairs, 1 degenerate triangles,
  0 contacts made by the rounding left, eg at 0.9999999998302377 0.9999999655839038 0.11574704638497392; the smallest edit
  rejected by the bound needed Infinity; ...
```

(The 12 + 32 triangle case `wave,1,1,0.2,2.209947632348079E-9,-1.1,0,D` is smaller but has a finite rejected drift of 0.06.)
The triangle (mesh diagonal 1.42, bound 1.42e-5, degenerate area 2.08e-12):

| | |
|---|---|
| area | 1.3e-17 |
| edges | 3.6e-10 (v68-v103), 1.03e-7 (v72-v68), 1.03e-7 (v103-v72) |
| v68 | (1, 1, 0.1157470482156689), valence 11, **fixed** (input vertex, the corner of `A`) |
| v103 | (0.9999999997453565, 0.9999999997453565, 0.11574704822237276), valence 11, free |
| v72 | (0.9999999997453565, 0.9999998970063548, 0.11574704271688012), valence 13, free |
| room (bound minus the bound used) | v72 1.42e-5, v68 and v103 1.41e-5 |

It is not a cap (the shortest and longest edge span 3.7e-17, the cap rule needs 32 times 2.08e-12), so `fix_degenerate` goes straight
to `collapse_any`.

## 3. The configuration

The vertices v68 and v103 are the corner of `A` and the corresponding corner of the turned `B`, 0.5e-9 apart. The faces on the
wall y = 1 that join them with v93 and v90 are two triangles (v90, v93, v68) of `A` and (v90, v93, v103) of `B`, reversed (area
0.0102 each, the wall of the plate between z = 0 and 0.1157): a **flat wedge** of opening 3.6e-10 at the corner and 0 along the shared
edge v90-v93. The same wedge runs along the other wall (x = 1). The corner vertices also carry the big triangles of the top surface
(v103 has neighbours at 0.3 to 0.9, areas 0.07 to 0.08), so this is part of the main surface, not a closed thin component that the
removal of tiny components could take. The left-over triangle (v72, v68, v103) is the mouth of the wedge.

Common neighbours (the link condition needs only the two opposite corners): v68 and v103 share v58, v72, v93, v90 (opposite: v72,
v58): the extra v93 and v90 are the wedge. v72-v68 share v83, v103, v93, v91 (opposite v83, v103). v103-v72 share v68, v93, v53, v55
(opposite v93, v68). All three edges violate the link condition.

## 4. Every edit and the rule that refuses it

The repair was patched temporarily (not committed): each `return false` of `improves`, `try_collapse`, `try_flip`, `try_zip`,
`zip_strip` and `try_move` records its own line, the first one reached wins, and a hook after `RemainingDegenerate = bad.Count` calls
the private edits by reflection on the state the repair leaves, on exactly the proposals `fix_degenerate` makes (`collapse_any`: the
fixed end is kept, both directions and the midpoint only between free ends; flips and zips of the three edges; `try_strip`; the 26
directions by 8 sizes of the position search at every corner). If one had been accepted it would have been reported: none was, in
any of the 89 states. Lines are those of master f11323390 `mesh_ops/MeshRoundingRepair.cs`.

The minimal case (v68 is fixed, so only the contraction toward it is proposed for its edges):

| edit | refused by |
|---|---|
| contract v72 into v68 | L1814 `improves`: a new triangle (v93, v68, v103) meets the new triangle (v51, v68, v53): a new intersecting pair; the link condition would refuse as well |
| contract v103 into v68 | L1810: the new triangle (v90, v93, v68) is the existing triangle (v90, v68, v93) reversed (a folded double face, counted as intersecting); link condition violated (v93, v90) |
| contract v103 into v72, v72 into v103, midpoint | L1810 / L1814: new triangles meet the existing (v55, v53, v72) and (v55, v103, v53) (the second is the reversed double face again); link condition violated |
| flip of v72-v68 | L1796: it does not remove a degenerate triangle and there is no intersection to remove (`newB == oldB && oldI == 0`) |
| flip of v68-v103 | L1810: the new triangle (v72, v58, v103) meets the existing triangle (v55, v74, v103) |
| flip of v103-v72 | L2226: the edge between the opposite corners (v93, v68) exists already |
| zip of v72-v68, v68-v103 | L2292: the apexes are not both strictly between the edge's ends |
| zip of v103-v72 | L2286: the opposite corners are joined by an edge already |
| strip | L2577: the zipped chain duplicates a directed edge the mesh has (not manifold) |
| position search, 624 proposals | L1796 for all: the triangle stays degenerate (see below) |

Over all 89 states (54 of the tiny search, 35 of the seed 2 search; the rule that refused first, per proposal):

- *Contractions* (623 proposals): the link condition is violated in 566 (91 %), in 226 of those by a folded double face (a new
  triangle that exists already, reversed). The refusal that is reported first is mostly an intersecting pair of `improves` (L1810,
  L1814); 133 reach `CollapseEdge` (L2181) because `improves` passed; 3 stop at the drift bound (L1577). The 57 contractions with
  the link condition intact are refused by `improves`: no gain (29, another bad triangle stays) or a new intersection (28).
- *Flips* (384): the diagonal exists, L2226, 39 %; a triangle would turn over (fold rule, L1758) 29 %; no gain (L1796) 18 %; a
  new intersection 14 %.
- *Zips* (384): the apex test (L2292) 61 %, the opposite corners are joined already (L2286) 39 %, 1 other.
- *Strips* (128): manifold check (L2577) 42 %, no strip around the triangle (L2397) 25 %, not a disk or not monotone (L2488,
  L2513, L2530, L2533) 21 %, an outer triangle used twice (L2549) 9 %, the bound 3 %.
- *Position search* (79 872 proposals): 99.0 % no gain (L1796); 418 a new intersection, 119 a new degenerate triangle (L1763), 244 the
  drift bound (L1577; the search resets `SmallestRejectedDrift`, which is why the report stays infinite).

**Why every move of the position search fails.** The shortest edge is 3.6e-10. A triangle with it is above the degenerate area only
if the third corner is at least 2 * 2.08e-12 / 3.6e-10 = 1.2e-2 from the line, far more than the room 1.4e-5. Moving the two free
corners apart, so that both edges are long, would need the single move to produce the area alone (0.5 * 1.03e-7 * d >= 2.08e-12 needs
d = 4e-5, also above the room). Only two corners moving together reach the area (0.5 * d1 * d2 with d1 = d2 = 2e-6).

## 5. The questions

**Is it an order or a budget gap?** No. (a) The state is the same in 5 other input orders for all 88 hits of the two seeds
(`hit in 5 other orders: same result 5`). (b) The evaluation limit was not reached in any hit. (c) On the final mesh no edit of the
repair's set is accepted, so a second pass, another order of the passes or a larger budget changes nothing. (d) The passes' retry
rule (only triangles near changed vertices) is not the cause: the edits are refused on the final mesh itself.

**Can a rule be relaxed soundly?**
- *Link condition* (`DMesh3.CollapseEdge`, L2181): protects a 2-manifold. Here the contraction merges two faces into a coincident
  pair, a non-manifold or folded result. No.
- *New intersection count* (L1810, L1814): the coincident opposite pair is a real defect (it intersects). The edit would trade the
  degenerate triangle for an intersecting pair, no net gain, and the check cannot tell it from a harmful one. No.
- *No gain* (L1796): what the termination argument rests on (every accepted edit lowers the number of defects). No, short of a new
  potential (the plateau variant of `SNAP_ROUNDING_3D.md` section 10, no-go of the owner 2026-11-15).
- *Diagonal exists* (L2226, L2286): a flip would make a double edge. No.

**Is a new edit needed, and which?** Yes. Candidates:
1. **Remove or flatten the wedge** (contract the mouth and cancel the two coincident opposite faces, gluing their other edges).
   Topologically valid when the extra common neighbours are exactly one edge shared by the two faces. But it removes a face pair
   of size 0.1: the surface moves by that, far beyond the drift bound 1.4e-5, which is a bound on the surface (Hausdorff) and not on the
   volume. It is the question of `remove_tiny_components` (by volume, closed components only) extended to flaps: a policy decision
   with a new guarantee. Not an edit within today's contract.
2. **A joint move of two corners** keeps the contract (no topology change, each corner within its room, the same exact `improves`
   check on the union). Probe (temporary code): on the minimal case, the two free corners moved together by room / 2^j (j = 0 to 4)
   along each of the 26 directions: 16 900 joint moves, **11 456 leave no degenerate triangle and no intersecting pair on the whole
   mesh** (exact, from scratch), 1 294 of them also have no triangle above 100 degenerate areas turned over (the rule of
   `improves`); the smallest larger step among those is 3.55e-6 (room / 4), the best is clean as well. Over the 79 left-over triangles of the
   54 states of the tiny search (sizes room to room / 8, 26 by 26 directions; the counts are over 475 probe calls, each state
   run six times, the same result each time): a valid joint move exists in 24 %, a clean mesh only with a turned-over triangle in 16 %, none in 40 %, and 19 % have
   fewer than two free corners (two fixed corners: the move would have to be one of a free corner and a neighbour). So the pair move
   is a partial fix.

**What a new edit has to guarantee.** No new intersection and no new degenerate triangle (the exact `improves` check with two moved
vertices); the drift bound with two moved vertices (the ghosts and `proposed_drift` handle one `movedV`); order independence as
in #1464: candidates enumerated by position (the 26 directions by sizes, corners in the order of their coordinates, not their ids),
decisions by exact checks only; the termination of the passes (an edit lowers the number of defects, as a pair move that removes
the triangle does); no float parallel loop in the exact code. The cost: 26 * 26 * sizes^2 candidates per triangle, each an exact
evaluation; the probe above (16 900 candidates with a from-scratch count) took 8 s, so a real edit needs a pruned set (the
directions that raise the area: perpendicular to the longest edge for the apex, along the short edge for the end).

**Prior art** (short; sources already in `docs/sources`, nothing new read, the full texts of the papers were not reachable):
- Milenkovic and Sacks 2019, the method the repair follows: contractions, flips, then an optimization (separation by linear programs
  over several vertices at once, option A, closed in #313). It moves several vertices at once; the library has single-vertex moves.
- Devillers, Lazard, Lenhart (snap rounding in 3D, 2018/2020): the output is defined by the grid; a collapsed or degenerate triangle is
  a legitimate output there (topology is guaranteed, not a nonzero area), so a leftover degenerate triangle is not an error state.
  From memory, not verified today. The library's clean criterion (area below `DegenerateArea` times the diagonal squared, the
  test of `MeshRobustRepair.NotCleanReason`) is stricter.
- `docs/design/SNAP_ROUNDING_3D.md` section 9 and 10: the position search of #755; its probe on the five D1 leftovers (1 to 3
  degenerate triangles each) found that "a degenerate triangle needs its corner moved by about the bound"; the plateau variant (sideways
  steps) is a design only (owner no-go 2026-11-15, `docs/roadmap/exact-boolean.md`). The joint move here is an improving step, not
  a sideways one, so it needs no termination lemma of its own.

## 6. Options

| option | what | cost | effect |
|---|---|---|---|
| 0. keep | `RoundingNotClean`, the pipeline's `Grid` retry | none | the state is reached by about 1 % of this adversarial sample (near-coincident plates, 1e-9 rotation), 5 of 16 239 operations of the D1 cohort were of this kind |
| 1. pair move | after the single-vertex position search, a search over two corners of a degenerate triangle with an area-raising candidate set; `improves` for two moved vertices | new evaluation path, the drift of two moved vertices, new tests (invariance, reuse, determinism, mutants), a shard baseline for `MeshRoundingRepair` | clean in a quarter of the states (valid joint move) and maybe more with other candidate sets; results that are clean today are not reached |
| 2. flap removal | remove thin double layers by volume (extend `remove_tiny_components` to flaps) | a new guarantee (the surface moves by the flap's size), an owner decision | would clear the wedge states, changes what the pipeline promises |
| 3. count it as clean | accept a nonzero-area triangle that is exactly non-degenerate | changes the contract of `NotCleanReason` | no |

**Leaning**: do not build now. The state is real and deterministic but needs a new edit, not a rule; option 1 is the candidate that keeps the
contract, with a quarter of the states measured, and should be a design with a candidate-set study first (which pairs, which
directions) and a benchmark on the D1 leftovers, not a patch. Options 2 and 3 are the owner's.

## 7. How to repeat

`search` of `benchmarks/BooleanInputOrder` (committed) finds the states and classifies the repair's result. The table of section 4
needs the temporary instrumentation described there (the line of the first `return false` per proposal; the hook is about 150 lines
of reflection and is not committed). Not verified: the joint-move probe does not check the drift bound of the two moves together
(each is within its room) nor the ghosts; the 24 % is over the 79 triangles of the tiny search; `dome` and
bigger grids (non-tiny hits, 35 states) were classified by the refusing rules but not probed for joint moves; the instrumented
counts are per proposal.

## 8. What the pipeline's default grid retry does with these states

Measured on master fc251bad2 (after #1480, the default `AutomaticSteps = GridBeforeFallback | GridAfterRoundingNotClean`, and #1481),
with `search --pipeline` (new): for every case whose repair leaves a degenerate triangle (group "degenerate", 35 + 54 hits; the
one tiny case that also has a resolvable removal is in this group) or only a resolvable removed component (group "removed", 542 + 521 cases),
`SolidBooleanPipeline` on the same inputs and operation with `AutomaticSteps = None` and with the default. Seed 2 (5000 cases),
seed 3 `--tiny` (4000 cases); the minimal reproducer is added as a third column. The retry cells were 2^-40 to 2^-27 (9e-13 at coordinates
up to 1, 7.5e-9 at a shift of 1e4); the size guard (cell above 1/64 of an input's size) never fired and no retry result was empty.

| group | seed | cases | `None`: RoundingNotClean | default: success via Grid, clean | default: success via Grid, only contacts | default: size guard | default: fails again on the grid (RoundingNotClean) |
|---|---|---:|---:|---:|---:|---:|---:|
| degenerate | 2 | 35 | 35 | 9 | 7 | 0 | 19 |
| degenerate | 3 tiny | 54 | 54 | 16 | 9 | 0 | 29 |
| degenerate | minimal | 1 | 1 | 0 | 0 | 0 | 1 |
| **degenerate, total** | | **90** | **90** | **25 (28 %)** | **16 (18 %)** | **0** | **49 (54 %)** |
| removed | 2 | 542 | 542 | 3 | 1 | 0 | 538 |
| removed | 3 tiny | 521 | 521 | 4 | 0 | 0 | 517 |
| **removed, total** | | **1063** | **1063** | **7 (0.7 %)** | **1** | **0** | **1055 (99 %)** |

**Correction (section 9): the volume differences in this paragraph and the next are an artifact of the measure, not a property of the results.** `Volume()` of the harness sums the triple products about the origin; at a shift of 1e4 that sum loses several digits of a volume of 1e-3. Measured about the box centre, all 41 successes agree with an independent estimate to 2.1e-4. Largest relative volume difference of a default success against the non-strict boolean of the unsnapped inputs (about the origin): degenerate group 0.177
(seed 2; seed 3: 0.029), removed group 0.433 (seed 2, the only-contacts case; seed 3: 3.4e-7). Of the 41 successes of the degenerate group, 26 differ by more than 0.1 %, 15 by more than 1 %, 6 of them by more than 1 % of the first input's whole volume (largest 7.5 %, a dome plate of thickness 0.002 at a shift of 1e4); the
others by 1e-10 to 1e-3. All failures are `RoundingNotClean` again on the grid; with `None` every case ends `RoundingNotClean`, as expected, and no run
ended in another status.

Reading: for the left-over-degenerate state the default retry gives a result in 41 of 90 cases (46 %), 25 of them clean, but it is not a cure: a
little over half still end `RoundingNotClean` (the snap moves the corners by up to a cell but the wedge of section 3 is still there), and 26 of the 41
successes differ by more than 0.1 % from the non-strict result (up to 18 % of the result's volume, because the results of these near-coincident plates are slivers of
a volume about 1e-3 of the input and the snap moves their faces by a cell), so a caller sees a state that is half cured and not always in an accurate way
(check `Report.Route` for the Grid step and `Report.Clean`). For the other class (removed resolvable) the retry is almost no help (8 of 1063): the
snapped run ends in the same refusal (the cause of that class was not examined here). The gap of section 6 therefore
matters in practice for the pipeline too: about 54 % of the degenerate states and 99 % of the removed states stay refused by default. The reference volume
is the non-strict boolean's result, which is itself a rounded result of an ill-conditioned input; the differences show the order of magnitude, not an error
against the exact solid. Not measured: other families than these plates; the cells of the retry were not varied.

## 9. Are the retry's successes right?

**Yes. The volume differences of section 8 come from the measure, not from the retry, the repair or the reference.** Question (2026-11-24): the retry
returned `Success` where the volume differed from the non-strict boolean by up to 7.5 % of the first input's volume, which a snap by 1e-12 cannot do.
Settled with `search --pipeline --verify` (new; the same seeds and cases as section 8: seed 2 with 5000 cases, seed 3 `--tiny` with 4000), on master fc251bad2.

### Method

Per case the volumes are measured **about the centre of the box of both inputs** (`VolumeAbout`: the triple products of the vertices minus the centre, sorted, summed).
`V_exact` is `ExactMeshBoolean` with `SafeRounding = false` on the unsnapped inputs; the first run's `Report.RoundingVolumeBefore`
(pipeline with `AutomaticSteps = None`; a failed run keeps it) agrees with it in the 6 digits printed, in all 97 failed-or-successful degenerate and removed cases. `V_nonstrict` is the default `ExactMeshBoolean` (the reference of section 8).
`V_snap_exact` is `RoundingVolumeBefore` of the retry run (and, as a check, `ExactMeshBoolean` with `SafeRounding = false` on the inputs snapped by the pipeline's own `Snapped`: the same).
`V_retry` is the volume of `pipeline.Result`. `V_mc` does not use a boolean: the winding numbers of the INPUTS along vertical lines. Per jittered column (6 replicas of 250 x 250 columns over the
box) the crossings of the line with the triangles of each input give the winding number exactly (+1 entering, -1 leaving), the expected solid is the set where `w(A) > 0 and w(B) <= 0`
(Difference), and its length on the line is summed with the area of a column. The standard error is that of the 6 replicas (1e-5 to 1e-4 of the volume; 1e-3 at most in the control group). The column method is checked against `DMeshAABBTree3.WindingNumber`
(1500 random points between the crossings per case: 0 mismatches in all cases). Because a line is integrated exactly, a sliver costs nothing. The same columns give, per result, the volume where its winding number is
positive, negative, and the symmetric difference with the expectation (the sampled membership error, as a volume).

### The 6 cases (the successes of section 8 that differ by more than 1 % of the first input's volume)

All six have shift 1e4. Volumes about the centre, 6 digits. "section 8" is the relative difference of the origin-based `Volume()` of the retry's result against the non-strict one.

| case (seed, #, `--case`) | `V_in0` | `V_exact` | `V_nonstrict` | `V_snap_exact` | `V_retry` | `V_mc` (SE) | step that differs | section 8 | the retry's result |
|---|---:|---:|---:|---:|---:|---:|---|---:|---|
| 2, 350 `dome,6,3,0.02,1.77e-9,0.7,1e4,Drev` | 0.02 | 0.00138949 | 0.00138949 | 0.00138949 | 0.00138949 | 0.00138942 (6e-8) | none | 0.144 | 34 components, `OnlyContacts` |
| 2, 4821 `dome,3,3,0.002,3.86e-8,0.3,1e4,Drev` | 0.002 | 0.00197463 | 0.00197463 | 0.00197463 | 0.00197463 | 0.00197463 (1e-8) | none | 0.0756 | 1 component, clean |
| 3, 2160 `wave,2,2,0.002,2.44e-9,0.3,1e4,D` | 0.002 | 0.00155248 | 0.00155248 | 0.00155248 | 0.00155248 | 0.00155248 (4e-8) | none | 0.0247 | 4 components, `OnlyContacts` |
| 3, 2466 `wave,3,3,0.002,1.29e-9,-1.1,1e4,Drev` | 0.002 | 0.00130843 | 0.00130843 | 0.00130843 | 0.00130843 | 0.00130847 (3e-8) | none | 0.029 | 5 components, `OnlyContacts` |
| 3, 2897 `wave,2,2,0.002,1.14e-7,0.3,1e4,Drev` | 0.002 | 0.00155248 | 0.00155248 | 0.00155248 | 0.00155248 | 0.00155251 (4e-8) | none | 0.0172 | 2 components, clean |
| 3, 3717 `dome,3,3,0.002,9.98e-9,0.3,1e4,Drev` | 0.002 | 0.00197463 | 0.00197463 | 0.00197463 | 0.00197463 | 0.00197463 (4e-8) | none | 0.0274 | 1 component, clean |

What the results are: the whole solid, not a sliver (the result is 7 % to 99 % of the first input's volume; the "about 1e-3 of the input" of section 8 was wrong for them too). The winding number of the result is never below 0
(volume with winding below 0: 0 in all six), and no part is missing or extra: the symmetric difference with the expectation is 1.3e-6 to 2.0e-6 of `V_mc` (about 2.5e-9 of 1.4e-3), below the area times the cell (the snap's move of the faces by up to 7.45e-9, area 2).
In the retry's rounding (a replica: `ExactMeshBoolean` with `StrictRounding` on the snapped inputs, which gives the pipeline's result volume) the volume before and after the repair is equal in 6 digits,
`MaxDrift` 0 to 3.8e-6, 0 to 59 collapses, at most 1 flip, no resolvable removed component. `NotCleanReason` is "self-intersections" with `OnlyContacts` in three of them (parts that touch), "" in three.
The Grid step's `Change` text: cell 2^-27 (7.45e-9), 68 to 298 vertices moved, the largest relative change of an input's volume -9e-8 to -5e-7. A caller who reads it sees a snap of 1e-7 of a size, which is what happened.

### Which step makes the difference

None. In the six cases `V_exact`, `V_nonstrict`, `V_snap_exact` and `V_retry` agree in the 6 digits printed, and `V_mc` agrees to its standard error (over all 41 successes the largest
`|V - V_mc| / SE` is 3.4). The section 8 reference (the non-strict boolean) is right too. What differed is the measure: `Volume()` in `Program.cs` sums `p . (q x r)` about the **origin**. With coordinates of 1e4 the
cross product of two vectors of 1e4 has a rounding error of 1e-8 per component, times `|p|` = 1e4: 1e-4 per term, against 6 V = 1e-2 for the sum. The same two meshes give, about the origin, 0.00187184 (retry) and 0.00202483 (non-strict), and
0.00197463 for both about their centre (case 4821). The artifact is where it should be: of the 41 successes of the degenerate group, the 26 with a difference above 0.1 % and the 15 above 1 % are all among the 28 at shift 1e4;
the 8 at shift 0 and the 5 at shift 100 have none (largest 1e-6 and 5e-8).

### Totals (all of the successes, about the centre)

| set | cases | largest `|V_retry - V_mc| / V_mc` | largest `|V_nonstrict - V_mc| / V_mc` | largest `|V_exact - V_mc| / V_mc` | `|V - V_mc| / SE` at most |
|---|---:|---:|---:|---:|---:|
| degenerate group, default success via Grid | 41 | 2.1e-4 | 2.1e-4 | 2.1e-4 | 3.4 |
| removed group, default success via Grid | 8 | 1.3e-4 | 1.3e-4 | 1.3e-4 | 3.0 |
| degenerate group, default fails again (no result) | 48 | no result | 9.7e-5 | 9.7e-5 | 4.4 |
| control: clean without a retry, same searches | 40 | 1.7e-3 | 1.7e-3 | 1.7e-3 | 3.4 |

The largest relative errors are the standard error of the estimate (the control group's results are smaller, hence its larger SE); no case is above 4.4 standard errors. The retry's result differs from `V_exact` by at most 1.2e-4 of its volume (the rounding repair's own move in the
retry); the snap alone moves the volume by at most 2.3e-6 of it (`V_snap_exact` against `V_exact`).

### Scope: the rounding repair, and other families

- **Without the retry** the same holds: `V_nonstrict` and `V_exact` are within 2.1e-4 (the successes) and 9.7e-5 (the 48 cases where the retry fails again, so they stay refused) of `V_mc`: the non-strict boolean of these inputs is right too. The repair does not move a volume visibly in these cases.
- **Other families** (new mode `families`): the generated degenerate families (`Cluster`, `FoldedCluster`, `Collinear`, `Sliver`, three frames), and sphere and box pairs that nearly coincide (a copy turned by 1e-9 to 1e-6, another tessellation, `Difference` and `Intersection`, at the origin and at 1e4),
  1 554 cases. 156 end `RoundingNotClean` without the retry; the default retry succeeds in 120 (all `Cluster` or `FoldedCluster`, no sphere or box pair) and fails again in 36. Of the 120 successes none is wrong (largest error 6.7e-5, largest `dV / |Before|` of the retry run 1.2e-8).
  The columns take the expectation as the region of winding number above 0, which is the union with nothing.

### The two rules, by hand

There is no wrong result among the successes, so no rule can refuse one here (0 of 0 refused). What can be said is the false alarms and the margin.

| rule | measure | the 49 successes (41 + 8) | control (40 clean runs) | other families (120) |
|---|---|---|---|---|
| (a) `RoundingVolume`: `dV = abs(After - Before)` of the result's run, fails when `dV > 1e-3 abs(Before)` (and `dV > 1e-6` of the smallest closed input) | `dV / abs(Before)` | 0 alarms; median 7.7e-9, largest 1.2e-4 | 0 alarms; largest 5.2e-5 | 0 alarms; largest 1.2e-8 |
| (b) consistency guard: `abs(V_retry - V_first) / (A (cell + drift))`, A the area of the result, `V_first` the first run's `RoundingVolumeBefore`, drift the retry's `MaxDrift` | ratio | median 1.8e-4, largest 0.084 | | |
| (b') the same with the allowed bound (`RoundingMaxDisplacement` = 1e-5 of the diagonal) in place of the drift | ratio | median 1.3e-5, largest 7.8e-3 | | |
| (b'') the same with the cell alone: `abs(V_retry - V_first) / (A cell)` | ratio | largest 575; above 1 in 17 of 49, above 10 in 9, above 100 in 5 | | |

(a) never fires; its margin to the 1e-3 is a factor 8 on the largest of the successes. (b) and (b') stay below 0.1: a factor of 1 on them would never raise a false alarm, with a margin of 12 (b) or 128 (b'). (b'') is not usable: the cell
(7.5e-9 at 1e4, 9e-13 at 1) is far below the drift of the repair (up to 1.5e-5), and 17 of the 49 correct results would be refused. The drift is not in the `Report` (the pipeline would have to take it from the boolean's `Rounding`).
What neither rule can say is whether it would catch a really wrong retry: none was found.

### Conclusion

**Not a defect of the default retry, of the rounding repair or of the reference.** The retry's results are the right solids (the exact result of the snapped inputs, which is within 2.3e-6 of the volume of the exact result of the given ones; with the repair's own move within 1.2e-4),
the non-strict boolean and the exact result of these inputs are right as well, and the large differences of section 8 are the harness's volume about the origin at a shift of 1e4. Severity: none for the library. Section 8's reading
("not always in an accurate way", "up to 18 % of the result's volume") is withdrawn (corrected there). Recommendation: **no fix, no guard now.** (b) would never fire on these inputs and has nothing to catch; if a guard against an unknown failure is wanted later,
(b) with the drift, or `RoundingVolume` (a), has a margin of 8 or more on everything measured here, but it needs a wrong result to be tuned against. The harness now measures about the centre.

### Smallest reproducer of the observation

Procedural, no mesh files (case 4821 of seed 2: two dome plates of thickness 0.002 on 3 and 6 grid cells, the second turned by 3.9e-8, both at (1e4, 5e3, -2.5e3)):

```
dotnet run --project benchmarks/BooleanInputOrder -c Release -- search --count 1 --verify --case dome,3,3,0.002,3.8598557935951046E-08,0.3,10000,Drev
```

It prints `V_exact`, `V_nonstrict`, `V_snap_exact` and `V_retry` as 0.00197463 each, `V_mc` as 0.00197464 +- 1.8e-8, and a line `VERORIGIN`: about the origin 0.00187184 (retry) against 0.00202483 (non-strict), 7.6 % apart; about the centre they are equal.
The effect of the measure alone: the sum of `p . (q x r) / 6` over the triangles of a closed mesh at an offset of 1e4 (a sphere of radius 0.5 with 40 rings at (1e4, 5e3, -2.5e3), double arithmetic: 0.5220729 about the origin against
0.5222544 about its centre, 3.5e-4 apart; the error is about 1e-4 of absolute size per mesh, so a solid of volume 2e-3 loses several percent). The library's own volumes (`MeshRoundingRepair.VolumeBefore/After`, hence `Report.RoundingVolumeBefore/After`) are computed about the centre of the mesh's box and are not affected.

### How to repeat and what was not verified

`search --count 5000 --seed 2 --verify --controls 20` and `search --count 4000 --seed 3 --tiny --verify --controls 20` (a `VER` line per case; the environment variable `G3_VERIFY_DETAIL=1` adds the components, the retry's rounding replica and the columns); `families`.
Not verified: the estimate's standard error is 1e-5 to 1e-4 of the volume (1e-3 at most), so a wrong volume below about 5e-4 of itself would not be seen (the claim is "within 2e-4 to 4 SE", not "exact"); the replica of the retry's rounding is `ExactMeshBoolean` on the pipeline's snapped
inputs with the pipeline's strict settings, not the pipeline's own object (its volume equals the pipeline's result in the six cases); the section 8 totals for the removed group were re-measured only for its 8 successes; a case with a wrong result, if one exists, is not in these samples
(9 000 cases, 49 successes of the plate searches, 120 of the other families); nothing for coordinates beyond 1e4, nor for other operations than `Difference` here (`Intersection` only in the families run, `Union` with nothing).
