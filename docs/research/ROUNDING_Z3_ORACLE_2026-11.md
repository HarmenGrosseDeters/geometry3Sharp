# An exact oracle for the degenerate triangles the rounding repair leaves (#1465, 2026-11-25)

Research spike (owner approved 2026-11-25) on branch `research/1465-z3-rounding-oracle`, on top of PR #1573 (`fix/1465-joint-move`,
bc7aadfff: the joint move of two corners). **No library change.** The question: can the degenerate triangles that
`MeshRoundingRepair` leaves after the joint stage be removed by moving up to k vertices within their drift bounds, or is that
provably impossible? The tool: a dump of the exact state from C# (`BooleanInputOrder oracle dump`), an exact model in Python
(`tools/Z3Pilot/rounding_oracle.py`) decided with Z3, and a check of every witness by the library's own tests
(`BooleanInputOrder oracle verify`).

## Answer in short

- **#1465's remaining cases are not a proven limit: almost all are repairable within the drift bound, and mostly by the edit that
  exists.** Of the 44 degenerate triangles left in the 29 states, 41 have a joint move of two corners that the library itself
  accepts (`try_move_pair` on the state the repair left: fewer degenerate triangles, no intersecting pair, every bound kept, the
  orientation rule). **40 of the 41 move a corner that `FixedVertexF` marks** (an input vertex of the boolean). The joint stage
  never proposes such a pair: it takes only free corners, while `FixedVertexF` is a preference in `ExactMeshBoolean`
  (`FixedHard` false; the single-vertex position search moves fixed vertices in its second pass). The 41st (s3_18, t203) moves two
  free corners by different fractions of their rooms (1 and 1/8), which the stage's grid (the same 2^-j for both) does not contain.
- **Whole states:** 22 of the 29 states become clean with **one** joint move that `try_move_pair` accepts (all 6 of seed 2, 16
  of seed 3; in 7 states with two degenerate triangles one pair removes both). 4 states with three degenerate triangles become
  clean with one simultaneous move of 3 vertices (two fixed corners and a free one; the configuration checked by the library's
  tests, but the repair has no 3-vertex edit). 3 states (`wave,2,2` differences with one fixed and two free corners: s3_10,
  s3_23, s3_27) are **open**: no move found (the library's grid for 2 and 3 corners, ring 1 with free vertices for k up to 3 or
  4) and no proof (the branch and bound timed out at 120 s for k = 2 and 3).
- **Proven (UNSAT):** with the fixed corners held, 16 triangles (two fixed corners each) cannot be repaired by moving any number
  of other vertices within any bound: the one free corner cannot lift the area of t within its drift balls (0.6 to 7 s, one box;
  the proof uses only the area of t, so it holds for every k and every ring). That is the case (b) of the joint-move note, now
  proved; it is not a limit of the repair, since moving the fixed corner works for all 16.
- **Not decided:** for the free corners only (the joint stage's own rule), the 27 other triangles timed out (10 to 106 boxes in 120
  s, one run 282 boxes in 1 500 s); the enumeration of the joint-move note found no pair for them on its grid, but that is not a
  proof. Z3's nonlinear arithmetic did not decide even the smallest of these models (section 3).
- **Recommendation (a decision for the owner, not built here):** let the joint stage pair a free corner with a fixed one (and both
  fixed corners), after the free pairs, as the single search does; and try independent sizes for the two vertices. On these
  samples that alone would leave 7 of the 29 states (3 open, 4 that need three vertices) instead of 29. Results that are clean
  today would not change (the stage runs only where degenerate triangles are left); the cohort acceptance run of #1573 would be
  repeated for the change.

## 1. The states

`search --count 1500 --seed 2` and `search --count 4000 --seed 3 --tiny` on bc7aadfff give 6 and 23 states that end with only
degenerate triangles (the joint stage on), as in the note `ROUNDING_DEGENERATE_LEFT_2026-11.md` section 10.3: 29 states with
44 degenerate triangles (1 to 3 per state). The positive control is the minimal reproducer of that note
(`wave,1,2,0.2,2e-9,-1.1,0,D`) in the state *without* the joint stage (`--nojoint`), where the joint move is known to work.

`oracle dump --case <spec> [--nojoint] --out state.json` (`benchmarks/BooleanInputOrder/Oracle.cs`) reruns the case and writes
the state the repair leaves: every vertex (its coordinates as the bits of its doubles, `is_fixed`, boundary, bowtie,
component, canonical id, `room_at`), every triangle (its corners, the three anchors and the carried bound of the reference rule,
`refAnchor` / `refCarry`, and `is_bad`), every ghost (corners, anchors, carry, lost), and per component the degenerate area,
`sizeArea` and `MaxLength`. Private members by reflection; the meshes are procedural and not committed.

## 2. The model

Per degenerate triangle t, for the movable vertices M (the corners of t, or ring r around them; no boundary vertex, no bowtie;
fixed vertices only when they are allowed to move, below) and a number k: is there a position for each vertex of M, **at most
k of them moved**, such that

- **(D) drift:** every triangle and every ghost with a moved corner keeps its bound, `carry + |p - anchor| <= L` for each moved
  corner (`triangle_bound`, `ghost_bound`; L the component's `MaxLength`). This is the bound itself (an intersection of balls
  about the anchors), not the conservative `room_at` ball about the current point, so it admits every move the reference rule
  admits;
- **(A) area:** t and every triangle of the moved stars that is not degenerate now are not degenerate by `is_bad`'s area test;
- **(I) intersections:** no pair of triangles, one with a moved corner, both not degenerate, intersects beyond what they share
  (`ExactTriangleIntersection.IntersectsBeyondShared`), with every triangle whose box can come within reach (boxes grown by 2 L).

The model is a **relaxation** of the library's acceptance: every configuration that the library would call clean within the
bound satisfies it. So UNSAT is a proof that no such configuration exists, SAT only a candidate (checked below). Each relaxation
is a necessary condition of the library's test:

| part | library | model | why it is necessary |
|---|---|---|---|
| positions | doubles | reals | the doubles are a subset |
| (D) | `fl(carry + fl(Distance)) <= L` | `|p - a|^2 <= (L - carry + 2^-40 L)^2`, exact | the rounding of the distance and the sum is below 6 u L |
| (A) | `0.5 |fl(cross)| >= eps` (and not collinear) | `|cross|^2 >= tau^2`, `tau = 2 eps (1 - 2^-40) - 2^-40 Lam^2`, Lam twice the longest current coordinate difference plus 4 L | the cross product's rounding is below 11 u e1 e2, the length's 3 u |
| (I), no shared vertex | closed triangles meet | weak separation along one of the 11 axes (two normals, nine edge cross products), the axis nonzero | disjoint triangles are separated by a facet normal of T - U, which is one of them (or by T's normal if T - U is flat) |
| (I), one shared vertex s | they meet beyond s | the cones at s weakly separated by a plane through s with normal +-g_i x g_j, nonzero (g the four edge directions at s) | cones that meet only at s are strictly separated (their difference is pointed); the cone of separating normals then has an extreme ray orthogonal to two of the g, or (all four coplanar) the plane's normal |
| (I), shared edge | coplanar fold | left out | dropping a constraint enlarges the set |
| pairs with a triangle that may be degenerate | not tested by the library | left out | as above |
| orientation rule, `SharedPositionsTouch` | acceptance rules of `improves` | not modelled | a SAT witness is checked by `try_move_pair`, which has them |

Weak separation admits pairs that touch (the library counts those), a relaxation on a set of measure zero. A zero axis (two
parallel edges) would separate anything; the first version of the encoding allowed it and the unit test of the tube (section 4)
found it: the walls parallel to the sliver's long edge made every wall constraint true.

## 3. The decision procedure, and why not Z3's nonlinear arithmetic alone

The constraints are polynomials of degree 2 to 4 in the moves, with coefficients that are long dyadic rationals (coordinates
near 1 or 1e4 with 52-bit mantissas, moves of 1e-5, faces 3e-10 apart). **Z3's nonlinear real arithmetic (nlsat) did not
decide them:** on the minimal reproducer (two movable corners, 6 variables), the drift balls and the exact area of t alone
returned *unknown* after 30 s (also with `qfnra-nlsat`), with the 24-bit rounded coefficients as well; with 8-bit coefficients it
was SAT in 0.01 s, so the numerals, not the structure, are the problem. With auxiliary variables for the edges and cross
products the same query took 14 s, with 8 pair constraints more than 60 s (unknown). A first lazy loop with the full nonlinear
model stalled in its second round.

So the decision is a **spatial branch and bound** over boxes of the moves (in units S, a power of two near L), with Z3 deciding
**linear** real arithmetic:

- per box, every active constraint is relaxed to linear form: `p(x) >= 0` implies `p(c) + grad p(c) (x - c) + R >= 0` on the
  box, R the sum of |coefficient| times the half widths over the monomials of degree 2 and more of `p(c + h)`; the drift balls
  by their tangent planes at the box centre (convex, exact). The relaxation is computed in floats with a padding of 2^-36 times
  the absolute sums it comes from (each float result is within a few u of its exact value); on boxes below 2^-14 of the start
  width exactly (Dyadic arithmetic, the coefficients rounded outwards to 40 bits);
- the relaxation with the booleans of "at most k vertices move" goes to Z3 (`QF_LRA`): unsat excludes the box;
- a feasible box gives a candidate (a model of the outer relaxation, and first of an *inner* one, `p(c) + grad (x - c) - R - E >= 0`,
  whose models satisfy the active constraints exactly), checked **exactly** against every constraint (Dyadic): all hold -> SAT;
  a violated constraint joins the active set (counterexample-guided, for every later box); otherwise the box is split in half
  along its widest variable of a nonlinear term;
- every box excluded -> UNSAT: no configuration satisfies the relaxation of the active constraints, so none satisfies the model,
  so none is accepted by the library;
- SAT candidates come first from the library's own grid (each moved vertex by `room / 2^j`, j < 5, along the 26 directions,
  every combination for two vertices, 20 000 random combinations for three or four), screened in floats and checked exactly.

A SAT witness is rounded to doubles and checked exactly again, then by the library: `oracle verify` sets the positions and counts
the degenerate triangles and intersecting pairs of the whole mesh with the repair's `is_bad` and `intersects`, evaluates the
drift bound of every triangle and ghost with `triangle_bound` / `ghost_bound`, and offers the move to `try_move_pair` (the
joint edit with all its rules) on the state the repair left.

**The core.** An UNSAT proof uses only its active constraints. If none of their triangles has a corner v, moving v changes none
of them, so the proof also holds with v moving, by any distance and in any number. An UNSAT whose active set is only the area of
t (core = the corners of t) therefore holds for every k and every ring: no move of any other vertices helps.

## 4. Checks of the encoding

- `tools/Z3Pilot/test_rounding_oracle.py` (about 6 s): a sliver whose apex is 1e-9 off its base line inside a square tube of four
  static walls; the degenerate area needs the apex 5e-4 away, the bound is 1e-3. A tube of half width 1e-2: SAT with one move,
  the witness clean exactly and after the rounding to doubles. Half width 1e-4: UNSAT, with wall constraints in the active set,
  and SAT without the walls. A bound of 1e-4: UNSAT by the area alone. The pair encoding on crossing, apart and touching
  triangles, and with one shared vertex (folded into the other, turned away). The test found two bugs of the first version:
  zero axes, and boxes chased into the boundary of the feasible set (now: inner relaxation, exact small boxes).
- **The model is not stricter than the library** on the library's own accepted move: the best pair of `joint --nojoint` on the
  minimal reproducer (v72 by a quarter, v103 by an eighth of their room, accepted by the library) satisfies every constraint of
  the model exactly.
- **The model's intersections are the library's:** a grid candidate of `wave,2,2,0.2,2.803e-9,0.3,100,D` that the float screen
  passed and the exact model refused (pairs 187-193, 193-200, 201-203, 201-204) has exactly those four intersecting pairs in the
  library (`oracle verify`), and `try_move_pair` refuses it.
- Positive control: the minimal reproducer without the joint stage is SAT (two free corners, 14 s), and the library accepts the
  witness (`try_move_pair` true, 0 degenerate triangles, 0 intersecting pairs, every bound below L).

## 5. Results per triangle

Times on one Windows machine (12 threads; three to four runs in parallel), proof attempts limited to 120 s; "a + b s" is the
time of the grid seeds and of the exact checks. "free" is the joint stage's
rule (the corners not kept by `FixedVertexF`); "fixed too" lets the fixed corners move as well. `*` marks a fixed vertex. A SAT
row lists the moved vertices; every SAT witness was verified by the library (section 6). Ring 1 was searched (seeds, not a
proof) only where ring 0 found nothing.

| state | t: corners (* fixed) | free corners, ring 0 | fixed corners too, ring 0 | ring 1 (seeds, no proof) |
|---|---|---|---|---|
| minimal (no joint stage) | 175: 72 68* 103 | k 2: SAT (72, 103; 33.0 + 13.8 s) | - | - |
| s2_01 | 2421: 1019 985* 1044 | k 2: timeout (12 boxes, 59 constraints active) | k 2: SAT (985*, 1019; 53.7 + 14.2 s) | - |
| s2_02 | 1788: 803 764* 802 | k 2: timeout (21 boxes, 39 constraints active) | k 2: SAT (764*, 803; 52.4 + 11.6 s) | - |
| s2_03 | 453: 169* 266 170* | k 1: **UNSAT** 0.6 s, the area of t alone | k 2: SAT (169*, 266; 42.7 + 10.5 s) | - |
| s2_04 | 1796: 805 766* 804 | k 2: timeout (30 boxes, 21 constraints active) | k 2: SAT (766*, 805; 73.3 + 21.2 s) | - |
| s2_05 | 379: 226 228* 227* | k 1: **UNSAT** 1.0 s, the area of t alone | k 2: SAT (226, 228*; 36.9 + 5.3 s) | - |
| s2_06 | 1787: 775 740* 774 | k 2: timeout (20 boxes, 41 constraints active) | k 2: SAT (740*, 775; 57.3 + 15.1 s) | - |
| s3_07 | 397: 55 86* 118 | k 2: timeout (12 boxes, 47 constraints active) | k 2: SAT (55, 86*; 69.1 + 22.7 s) | - |
| s3_08 | 162: 103 106* 105* | k 1: **UNSAT** 1.5 s, the area of t alone | k 2: SAT (103, 106*; 56.2 + 10.0 s) | - |
| s3_08 | 172: 106* 112 105* | k 1: **UNSAT** 3.1 s, the area of t alone | k 2: SAT (106*, 112; 53.1 + 19.3 s) | - |
| s3_09 | 334: 41 65* 56 | k 2: timeout (10 boxes, 51 constraints active) | k 2: SAT (41, 65*; 49.4 + 13.3 s) | - |
| s3_10 | 193: 121* 128 127 | k 2: timeout (12 boxes, 45 constraints active) | k 2: timeout (13 boxes, 49 constraints active); k 3: timeout (11 boxes, 45 constraints active) | free k 2: none; free k 3: none; free k 4: none |
| s3_20 | 327: 41 62* 53 | k 2: timeout (12 boxes, 56 constraints active) | k 2: SAT (41, 62*; 48.9 + 13.3 s) | - |
| s3_21 | 401: 55 89* 118 | k 2: timeout (12 boxes, 40 constraints active) | k 2: SAT (55, 89*; 55.7 + 17.5 s) | - |
| s3_22 | 375: 225 227* 226* | k 1: **UNSAT** 0.7 s, the area of t alone | k 2: SAT (225, 227*; 39.2 + 7.5 s) | - |
| s3_23 | 149: 101* 108 107 | k 2: timeout (12 boxes, 49 constraints active) | k 2: timeout (12 boxes, 45 constraints active); k 3: timeout (11 boxes, 44 constraints active) | free k 2: none; free k 3: none |
| s3_15 | 186: 122* 133 124* | k 1: **UNSAT** 4.2 s, the area of t alone | k 2: SAT (122*, 133; 49.3 + 10.1 s) | - |
| s3_15 | 195: 124* 131 130 | k 2: timeout (43 boxes, 18 constraints active) | k 2: SAT (124*, 130; 27.9 + 13.3 s) | - |
| s3_15 | 199: 124* 133 131 | k 2: timeout (11 boxes, 44 constraints active) | k 2: SAT (124*, 133; 42.4 + 8.8 s) | - |
| s3_16 | 162: 103 106* 105* | k 1: **UNSAT** 0.7 s, the area of t alone | k 2: SAT (103, 106*; 42.5 + 9.9 s) | - |
| s3_16 | 172: 106* 112 105* | k 1: **UNSAT** 3.1 s, the area of t alone | k 2: SAT (106*, 112; 42.9 + 9.1 s) | - |
| s3_17 | 158: 106* 117 108* | k 1: **UNSAT** 4.4 s, the area of t alone | k 2: SAT (106*, 117; 50.2 + 10.9 s) | - |
| s3_17 | 171: 108* 117 115 | k 2: timeout (28 boxes, 35 constraints active) | k 2: SAT (108*, 117; 49.9 + 8.7 s) | - |
| s3_18 | 190: 119* 130 121* | k 1: **UNSAT** 5.0 s, the area of t alone | k 2: SAT (119*, 130; 67.4 + 17.1 s) | - |
| s3_18 | 199: 121* 128 127 | k 2: timeout (77 boxes, 14 constraints active) | k 2: SAT (121*, 127; 52.2 + 13.7 s) | - |
| s3_18 | 203: 121* 130 128 | k 2: SAT (128, 130; 20.9 + 7.9 s) | - | - |
| s3_19 | 162: 104* 115 106* | k 1: **UNSAT** 4.7 s, the area of t alone | k 2: SAT (104*, 115; 88.2 + 14.8 s) | - |
| s3_19 | 175: 106* 115 113 | k 2: timeout (37 boxes, 30 constraints active) | k 2: SAT (106*, 115; 79.1 + 18.4 s) | - |
| s3_25 | 153: 104* 115 106* | k 1: **UNSAT** 6.7 s, the area of t alone | k 2: SAT (104*, 115; 69.1 + 15.6 s) | - |
| s3_25 | 166: 106* 115 113 | k 2: timeout (26 boxes, 35 constraints active) | k 2: SAT (106*, 115; 54.7 + 13.4 s) | - |
| s3_26 | 215: 126* 137 128* | k 1: **UNSAT** 3.2 s, the area of t alone | k 2: SAT (126*, 137; 42.1 + 11.8 s) | - |
| s3_26 | 228: 128* 137 135 | k 2: timeout (28 boxes, 31 constraints active) | k 2: SAT (128*, 137; 39.4 + 7.5 s) | - |
| s3_27 | 197: 127* 134 133 | k 2: timeout (12 boxes, 47 constraints active) | k 2: timeout (11 boxes, 53 constraints active); k 3: timeout (12 boxes, 44 constraints active) | free k 2: none; free k 3: none |
| s3_11 | 310: 41 68* 87 | k 2: timeout (10 boxes, 43 constraints active) | k 2: SAT (41, 68*; 56.5 + 16.9 s) | - |
| s3_12 | 167: 105* 116 107* | k 1: **UNSAT** 7.3 s, the area of t alone | k 2: SAT (105*, 107*; 70.9 + 14.9 s) | - |
| s3_12 | 180: 107* 116 114 | k 2: timeout (24 boxes, 32 constraints active) | k 2: SAT (107*, 116; 69.3 + 14.9 s) | - |
| s3_13 | 387: 54 83* 114 | k 2: timeout (12 boxes, 48 constraints active) | k 2: SAT (54, 83*; 46.7 + 16.1 s) | - |
| s3_14 | 388: 56 90* 119 | k 2: timeout (11 boxes, 41 constraints active) | k 2: SAT (56, 90*; 61.4 + 18.2 s) | - |
| s3_24 | 209: 49* 89 131 | k 2: timeout (106 boxes, 9 constraints active) | k 2: SAT (49*, 89; 24.1 + 9.1 s) | - |
| s3_28 | 198: 121* 132 123* | k 1: **UNSAT** 4.9 s, the area of t alone | k 2: SAT (121*, 123*; 75.2 + 19.5 s) | - |
| s3_28 | 207: 123* 130 129 | k 2: timeout (40 boxes, 16 constraints active) | k 2: SAT (123*, 129; 65.7 + 15.9 s) | - |
| s3_28 | 211: 123* 132 130 | k 2: timeout (16 boxes, 36 constraints active) | k 2: SAT (123*, 132; 60.4 + 18.6 s) | - |
| s3_29 | 175: 110* 121 112* | k 1: **UNSAT** 5.5 s, the area of t alone | k 2: SAT (110*, 112*; 62.3 + 16.5 s) | - |
| s3_29 | 184: 112* 119 118 | k 2: timeout (33 boxes, 21 constraints active) | k 2: SAT (112*, 118; 43.1 + 13.4 s) | - |
| s3_29 | 188: 112* 121 119 | k 2: timeout (13 boxes, 47 constraints active) | k 2: SAT (112*, 121; 53.5 + 14.5 s) | - |

## 6. The witnesses in the library

Every SAT witness of the table (the minimal reproducer and 41 triangles) was rounded to doubles, checked exactly again (all
constraints hold), then given to the library (`oracle verify`): each one leaves fewer degenerate triangles, 0 intersecting pairs
in the whole mesh (brute force over boxes with the repair's `intersects`), every triangle and ghost within its bound (the largest
bound 1e-11 to 1e-7 below L), and **`try_move_pair` accepts it** on the state the repair left (41 of 41, the minimal one included).

The witnesses of one state, applied one after the other (`oracle apply`), do not compose: in a state with two or three
degenerate triangles the triangles share their fixed corner, and after the first move the second witness (computed for the
original state) is refused. The question per state (all its degenerate triangles at once, `Problem(..., also=...)`) gives:

| state | degenerate triangles | fewest moved vertices found that clean the whole state | library |
|---|---|---|---|
| s3_08 | 2 | 2 (105*, 106*) | try_move_pair accepted |
| s3_12 | 2 | 2 (107*, 116) | try_move_pair accepted |
| s3_15 | 3 | 3 (122*, 124*, 131) | configuration clean, all bounds kept (no library edit of 3 vertices) |
| s3_16 | 2 | 2 (105*, 106*) | try_move_pair accepted |
| s3_17 | 2 | 2 (108*, 117) | try_move_pair accepted |
| s3_18 | 3 | 3 (119*, 121*, 128) | configuration clean, all bounds kept (no library edit of 3 vertices) |
| s3_19 | 2 | 2 (106*, 115) | try_move_pair accepted |
| s3_25 | 2 | 2 (106*, 115) | try_move_pair accepted |
| s3_26 | 2 | 2 (128*, 137) | try_move_pair accepted |
| s3_28 | 3 | 3 (121*, 123*, 130) | configuration clean, all bounds kept (no library edit of 3 vertices) |
| s3_29 | 3 | 3 (110*, 112*, 119) | configuration clean, all bounds kept (no library edit of 3 vertices) |

The states with one degenerate triangle are clean after their one accepted pair (15 of 18; s3_10, s3_23, s3_27 open). With the
7 states above: **22 of 29 states clean after one joint move the library accepts**, 4 more after one move of three vertices.

## 7. Interpretation

- **What the witnesses suggest:** a joint move of two vertices, the free corner of the degenerate triangle and its fixed corner
  (in a few, both fixed corners of t), each mostly by its full room (in three dome states the free corner by 1/16 of its room). Corners only, no neighbours: no witness needed a ring-1 vertex. So the edit that exists (`try_move_pair`) suffices; what
  is missing is the candidate set of `search_pairs`: (1) pairs with a fixed corner, (2) independent sizes for the two vertices
  (s3_18), (3) for the clusters of three degenerate triangles around two fixed corners, a move of three vertices, or the pair
  of the two fixed corners first (it removes one triangle in s3_28, s3_29) followed by a recomputed search.
- **Why the fixed corner** (a reading, not checked per state): in the reproducer of the #1465 note the fixed corner is the
  corner of the coarse plate, 0.5e-9 from the corner of the turned copy, at the end of the triangle's 3.6e-10 edge. Moving only
  the free corners must lift the triangle across the fan of 1e-7 edges (the joint-move note's reason (a)); moving the fixed
  corner with one free corner opens the short edge itself.
- **The guarantee is not weakened** by moving a fixed vertex: the drift bound holds for every vertex (the anchors and ghosts are
  per triangle), and `FixedVertexF` is documented as a preference in `ExactMeshBoolean` (not `FixedHard`); the single position
  search already moves fixed vertices. What changes: an input vertex of the boolean moves by up to L, as with the single search.
- **The three open states** (wave plates, `D`, thickness 0.2 / 0.02, one fixed and two free corners): the library's grid
  candidates that lift t cross the triangles next to t (exact model and library agree, section 4); the B&B could neither find a
  point nor exclude the space in 120 s. They stay #1465's open part.
- **Verdict for #1465:** not closable as a proven limit. The issue can be narrowed: 16 triangles are provably out of reach of
  any move that keeps their two fixed corners, but every one of them, and 41 of 44 triangles in all, has a library-accepted joint
  move with a fixed corner. A follow-up change of the joint stage's candidates (owner decision) would repair 22 of the 29 states
  on these samples; 4 more need a 3-vertex move; 3 are open.

## 8. What was not modelled, and limits

- **Sequences of edits.** The model asks for one configuration reached at once; the library makes one edit at a time, each of
  which must improve. For two moved vertices that is one joint move; for three or four there is no such edit.
- **Other edits.** Contractions, flips, zips and strips change the topology; they are not in the model (the note of #1465 lists
  why each is refused on these states).
- **Trades.** A configuration that removes t but makes another good triangle degenerate (or that removes two degenerate
  triangles and adds one) is not counted; `improves` would accept the latter.
- **The orientation rule and `SharedPositionsTouch`** are not in the model; `try_move_pair` checks them on every witness.
- **Pairs sharing an edge** and pairs with a triangle that may be degenerate are left out (a relaxation: it can only add
  solutions).
- **Proofs for the free corners** (27 triangles, and the fixed corners too for the 3 open ones) timed out: 10 to 106 boxes in
  120 s with 9 to 59 active constraints of 340 to 620; one run of 1 500 s on `dome,6,2,0.02,1.496e-9` ended at the limit as well (282 boxes, 39 active constraints, timeout). The cost per box is the Python relaxation (about 0.4 to
  10 s with 40 constraints of up to 22 alternatives of 9 atoms each), not Z3. These are not claims either way.
- **Ring 1** was searched only for the three open triangles, with free vertices only (k = 2, 3, and 4 for s3_10), a sample of
  15 to 40 vertex sets and 3 000 to 4 000 random grid combinations per set of three or four; with the fixed vertices too it was
  not run (it took about 25 minutes per question in this Python tool). Neither is a proof.
- **Measure-zero solutions.** Weak separation admits touching pairs; a relaxed problem whose only solutions touch cannot be
  refuted by the branch and bound (it ends "box too small"). It did not occur in the states.

## 9. How to repeat

```
dotnet run --project benchmarks/BooleanInputOrder -c Release -- search --count 1500 --seed 2
dotnet run --project benchmarks/BooleanInputOrder -c Release -- search --count 4000 --seed 3 --tiny
dotnet run --project benchmarks/BooleanInputOrder -c Release -- oracle dump --case <spec> --out <dir>/state.json
dotnet run --project benchmarks/BooleanInputOrder -c Release -- oracle dump --case wave,1,2,0.2,2e-9,-1.1,0,D --nojoint --out <dir>/minimal.json
python tools/Z3Pilot/rounding_oracle.py <dir>/*.json --timeout 120 --out <dir>/report.json
dotnet run --project benchmarks/BooleanInputOrder -c Release -- oracle verify --case <spec> --witness <dir>/w.json
python -m unittest discover -s tools/Z3Pilot -p 'test_rounding_oracle.py'
```

The summary of the run is `tools/Z3Pilot/results/rounding_oracle_2026-11-25.json` (answers, times, sizes, the witnesses as
double bits). Z3 5.4.1 (Python bindings), Python 3.14, numpy.
