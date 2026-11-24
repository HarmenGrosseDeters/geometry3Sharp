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
