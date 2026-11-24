# Design note: a canonical slope envelope, independent of the construction

Status: **stage 1 design (2026-11-24), for the owner's decision (b) of 2026-11-24.** Branch `terrain/slope-envelope-canonical`.
Context: [roadmap/terrain.md](../roadmap/terrain.md) ("Slope envelope construction"), [TERRAIN_1C_PLAN.md](TERRAIN_1C_PLAN.md)
section 9 (randomized insertion order with a canonical result), the earlier prototype
[`claude/terrain-envelope-dc`](https://github.com/HarmenGrosseDeters/geometry3Sharp/tree/claude/terrain-envelope-dc)
(`TERRAIN_SLOPE_ENVELOPE_DC.md` there, not merged).

## Goal

Make the slope surface a pure function of the input, through the labelled envelope only. Then a faster construction (merging
during insertion, divide and conquer, an envelope per sector) can replace the incremental `SlopeMap.InsertPiece` without
changing any output.

## 1. Where the history could leak, and what the code does today

The output is built in three steps. Each step was checked for dependence on the construction (vertex and line ids,
face ids, the order of splits, cached state):

1. **Labels** (`InsertPiece`, `insert_into`). Each point gets the lexicographic minimum of (height, plane id) over the
   pieces; inside a piece, the maximum with the lowest id among equal planes (TERRAIN_1C_PLAN section 9). The decisions are
   exact (`Side`, `Crossing`). The prefilters (`fBox` and `prefilter_skips`) and the fan sector choice (`add_sector`) use
   approximations, but they are conservative: a correct skip changes nothing. They depend on the face shapes, so on the
   history. **A non-conservative prefilter is the one way the history reaches the labels.** That is what the
   2026-11-10 observation was (below).
2. **Partition** (`Simplify`). The faces become the components of one label (connected across non-barrier edges), cut by
   one ray from each reflex corner up to the component's boundary. Rays do not stop at other rays, so their order does
   not matter. Then every degree-2 vertex between collinear edges of one barrier (or none) is removed. Ids appear only
   as shortcuts that agree with an exact predicate: `on_one_line` (the same kernel line) implies `Orient == 0`, and
   `reflex` and `removable` decide the same either way. The ray list and `merge_part` take faces in id order, but the
   result is a union of full rays and does not depend on it. `OrderedFaces` sorts by the exact `CompareXY` of the
   corners; label, then id, break ties, and two distinct faces never tie. So the partition is a function of the labels,
   the barriers and the box.
3. **Realization** (`Realize`, `merge_close_positions`, `append_faces`, then `CollapseShortEdges`, `BandMerge`,
   `FlipNeedles` on the mesh). The positions are `K.Rounded`, a function of the exact point (not of the lines that define
   it). `labelsAt` is filled in `OrderedFaces` order. The close-position merge sorts all positions in a total order
   (barrier, label count, x, y). The mesh's vertex and triangle ids follow `OrderedFaces`. The mesh repairs work on that
   mesh only.

   **One latent leak:** `triangulate` (the quality DP) and `quality` (the ear fallback) read `K.Approx`.
   `ExactSlopeKernel.Approx` returns the interval midpoint of the vertex's defining crossing, unless the vertex was
   rounded before, and then the rounded position. So it depends on which two lines define the vertex (the construction)
   and on the call history. Today this is masked: `Realize` calls `Rounded` on every corner (for `labelsAt`) before it
   triangulates. Take that call away, or give one point two different defining lines, and the triangulation of a
   near-symmetric face (a regular polygon, where qualities nearly tie) could change.

**The 2026-11-10 observation was not a vertex-history leak.** The prototype found `Merge_RaisesTheToeLowering`
at 80 triangles and 0 merged positions, against 81 and 3 on the incremental path. The reason was the prefilter bug
#805, fixed in #806 (2026-11-13): faces smaller than the error of the approximate corners were skipped where the piece
is lower, at apexes where many planes meet. The incremental map was not the exact envelope there, and its wrong
labels were the "cluster" that the close-position merge saw. #806 folded the test into
`RegularPolygons_NoMergeAfterPrefilterFix_InEveryFrame`. On master, both polygons give 80 triangles and 0 merges with
every construction (below). The roadmap entry predates #806.

## 2. The canonical rule

Define the output as a function of the **labelled envelope**: the map from each point of the domain to its label
(lexicographic minimum of (height, plane id) over the pieces, the cap included), plus the barriers and the box. The
rules:

- **R1 (labels).** Any construction must produce, on every open face, exactly the labels of the exact envelope. A filter
  may skip work only if the skipped work cannot change a label (conservative, with the corners' error bound counted, as
  `prefilter_skips` after #806). This is the contract a new construction must meet. It is also the one place where a bug
  shows up as an "output change".
- **R2 (partition).** `Simplify` as in TERRAIN_1C_PLAN section 9: components, reflex rays to the boundary, removal of
  straight degree-2 vertices. Unchanged.
- **R3 (realization reads exact positions only).** Every geometric decision after `Simplify` uses the exact kernel
  (`Orient`, `CompareXY`) or `K.Rounded`, never `K.Approx`. **New:** `triangulate` and `quality` use `Rounded` (cached in
  the vertex after the first call). This changes no output today, because the corners are already rounded there. It
  makes the property hold by construction, not through the order of calls.

Argument: under R1 the labels are equal as functions. Under R2 the partition is a function of the labels, the barriers
and the box (section 1, step 2). Under R3 the mesh is a function of the partition's exact corners, in canonical order.
The repairs after `Realize` are functions of that mesh. So the output depends on the input only. Construction-specific
data (vertex, line and face ids, `Approx` midpoints, split order, prefilter decisions) does not reach it. Near-degenerate
designs (clusters of positions within 64 ulps) go through the same function. They merge the same way because they see
the same position set.

**Literature.** The labelled envelope is the minimization diagram of the piecewise-linear functions F_k (lower envelopes:
Sharir and Agarwal 1995; Edelsbrunner, Guibas and Sharir 1989). It is unique as a labelling, but its convex
decomposition is not. Making the decomposition canonical is the reflex-ray decomposition of TERRAIN_1C_PLAN section 9
(Keil's survey, cited there). Divide and conquer through the overlay of two envelopes is Edelsbrunner, Guibas and Sharir
1989 (as in the prototype note). No new sources. Library building blocks: `SlopeMap.Simplify` /
`OrderedFaces` (canonical partition), `ExactSlopeKernel.Rounded` / `CompareXY` (history-free positions and order), the
prototype's `MergeMin` / `Compact` (on `claude/terrain-envelope-dc`). No convex hull or envelope code elsewhere in
`mesh_ops` fits (the cones are max-of-planes per piece, not planes).

## 3. Measurements (master + this branch, net10.0 Release, this container)

A construction switch for tests: `TerrainSlopeDesign.CompactDuringInsert` calls the new `SlopeMap.Compact` after each
piece. It merges neighbouring faces of one label across non-barrier edges while the union stays convex, by exact
`Orient`. The prototype used an approximate turn test. A hook `AfterSimplify` gives the test the map, and
`SlopeMap.PartitionText` gives the partition per face (label and rounded corners). Each construction is compared with
the default on the map after `Simplify` (every build) and on the slope surface (`Invariance.Difference`, bit for bit):

| case | merged positions | triangles | merge during insert | reversed order |
|---|---|---|---|---|
| 19 stress cases (`TerrainSlopeOneBuildTests.Cases`) | 0 | 10-236 | identical (map and surface) | (nightly test, identical) |
| `WavyOnWaves` n 40 r 100 (band-merge reference) | **7**, 1 band merge | 420 | identical | identical |
| `WavyHambach(130)` (the benchmark design) | 0 | 476 | identical | identical |
| old pin n 16 (`Merge_RaisesTheToeLowering`) | 0 (was 3 before #806) | 80 (was 81) | identical | identical |
| old pin n 22 | 0 | 66 | identical | identical |
| 600 random regular polygons (n 3-24, a quarter at UTM) | 0 in all | - | - | - |

**Changed pins under R3: none.** With R3, all 574 `TerrainSlope*` tests outside Nightly pass, Slow ones included
(2 min 46 s). R3 only replaces `Approx` by `Rounded` where both are already the rounded value.

**Cost of the rule:** none at runtime (`Rounded` is cached and is computed for these corners anyway).

**Cost of the naive merge during insertion** (`WavyHambach(130)`, phase `insert`): 3.9 s plain against 9.3 s with
`Compact` after every piece. `Compact` scans every face ever made, 48 times, and the larger faces pass the prefilter more
often. The roadmap's finer variant gained 1.05-1.4x. So merging alone is not the lever, as the profile says: the splits
cost, not the faces.

## 4. Stage 2: the faster construction (after the OK)

Under R1 every construction is checked the same way: the **pure-function test** (below) must pass, bit for bit. In order:

1. **Upper-bound check first** (CLAUDE.md): the time of `InsertPiece` for all pieces on a map that already has the final
   size. Re-insert every piece into the simplified map; the labels do not change. That is about the least work any
   incremental construction can do. If it is not well below today's 3-4 s, no incremental variant can pay off.
2. **Merging during insertion, local:** compact only the faces the last piece touched (the stamp list of `InsertPiece`),
   with the exact convexity test. Cheap to build; R1 holds trivially (the labels are untouched).
3. **Divide and conquer** (`E(S) = min(E(left), E(right))`, the prototype's `MergeMin` with an exact interior-disjoint test
   and no approximate skip). It goes forward only if step 1 shows room and step 2 gains less than 2x. A sector-local
   envelope is a variant of it (the leaves are the pieces of one sector).

Benchmark: `TerrainBenchmark slope toe=wavy n=48 r=150 h=130 ratio=1.5 tol=0.05`, before and after, `insert` phase and
total, identical `hash_surface`.

## 5. Tests (stage 1)

- **Pure function** (`TerrainSlopeEnvelopeCanonicalTests`): for each case, the default construction against merge during
  insertion and against two other insertion orders (reversed, a seed). Checked: the map after `Simplify` of every build
  (`PartitionText`), status, builds, toe lowering, `MergedPositions`, and the slope surface bit for bit with its triangle
  groups. The PR tier gets one regular polygon and one wavy case under 5 s. The full set (the 19 stress cases, the 7-merge
  `WavyOnWaves`, the old pins) is Nightly, because the run is the check, as with `InsertionOrder_DoesNotChangeTheResult`.
- **R3 directly:** a map whose face corners are triangulated before any `Rounded` call (a unit test on `SlopeMap` with a
  vertex defined by two different line pairs). The triangulation is the same as after rounding. Mutation: `Approx` back
  in `triangulate` must fail it.
- **Invariance** over `Similarity.All()` of the merge-during-insertion construction (the 2^k frames bit for bit), and
  **reuse** (`Compute()` twice on one instance with `CompactDuringInsert`). There is no parallel path, so no determinism
  test.
- **Mutations by hand:** `Compact` accepting a reflex union (`Orient < 0` check dropped) must fail the pure-function test
  (the map after `Simplify` is then non-convex or differs). So must a prefilter that skips one face too many (for example
  the #806 mutant), and a `Simplify` ray that stops at earlier rays.

## 6. Open points for the owner

- No output changes, so there is nothing to approve on the outputs. The roadmap entry ("not bit-identical in
  near-degenerate designs") should be corrected: that was #805.
- Whether `CompactDuringInsert` and `AfterSimplify` stay as internal test switches (as `InsertionOrder` does) or go once
  stage 2 picks a construction.
