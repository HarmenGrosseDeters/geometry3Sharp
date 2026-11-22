# Integer jumps on the frozen edges of the 3B flow: prototype (2026-11-22)

Research spike (owner go 2026-11-22: option C of [QUAD_SHEAF_OBSTRUCTION_2026-11.md](QUAD_SHEAF_OBSTRUCTION_2026-11.md) section 4 "if the
effort is reasonable"). No library change. The prototype is [benchmarks/QuadIntegerJumps](../../benchmarks/QuadIntegerJumps/README.md); the
pipeline is stage 3B of [QUAD_REMESHING.md](../design/QUAD_REMESHING.md) (`QuadRemesher.PureQuads`, `QuadFlowRepair`).

**Question.** Does it help to let the frozen edges of the 3B flow take other integer values before the flow, and is a solver for the
full problem worth building?

**Answer in short.**
- *It helps.* With the jumps 52 of the 54 gate runs are clean (library 43), 0 undetected bad. 10 of the 11 fallbacks become clean,
  among them 2 of the 3 inputs that no configuration made clean; the third is clean with `CurvatureAlignment` 10 and the jumps.
- *No new solver was needed.* The full problem is a min-cost flow in a bi-directed graph. Its relaxation is an ordinary flow on a
  doubled network, which the library's `MinCostFlow` solves. Rounding its values on the frozen edges and running the library's own
  flow after it is the whole prototype.
- *Quality.* On the 42 runs that are clean both ways the jumps are better: corner deviation p95 median 12.1 against 15.5 degrees, map
  area median 1.02 against 0.94 of the surface. The capped cylinder is good. The capsule is valid and coarse (area 0.27 to 0.77 at
  alignment 0), as the relaxation of the sheaf note predicted.
- *Stop rule.* One clean run is lost (bumpy sphere at ratio 0.7), which is the third stop condition read literally. It does not occur
  if the jumps are the second attempt after a fallback. So: **go for the small step** (section 5, option 1), **no go for an exact
  integer solver now**. The decision between "second attempt" and "always" is the owner's.

## 1. The problem, in the library's terms

`QuadFlowRepair.SolveFlow` rotates the two equations of every triangle ("the three offsets sum to zero") along a breadth-first tree,
so that an edge between two triangles enters the same row of both with opposite signs: a column of a network. An edge whose cycle has a
holonomy cannot be brought to that form. The library keeps it at the rounded value of the position field (clamped to -2..2), changes
only as many of them as the balance of the two network components needs, and solves the flow on the rest. These are the *frozen* edges:
25 to 1010 per run on the gate set, 0 on the aligned torus.

*What a frozen column looks like* (read from the code, derived here). With the rows as nodes, every component of every edge has exactly
two entries, each +1 or -1. For a quarter-turn mismatch of 2 both components have the same sign in both rows; for 1 or 3 one component
is an ordinary arc between an x row and a y row and the other has the same sign in both. So the system with all edges free is a flow in
a **bi-directed graph** (Edmonds and Johnson 1970): the setting of Heistermann, Warnett and Bommes 2023 for T-mesh quantization, here
on the triangle rows directly.

*The relaxation.* A bi-directed flow problem becomes an ordinary one on the doubled network: every node gets a mirror with the negated
equation, every column becomes an arc and its mirror arc, and half the sum of the two is a solution of the linear relaxation of the
original, with values that are multiples of one half (the skew-symmetric construction; Goldberg and Karzanov 1996 describe it for
maximum flows). Convex step costs carry over as parallel arcs, as in the library's flow. So the relaxation needs no new solver:
4 nodes per triangle instead of 2, twice the arcs, `MinCostFlow` as it is.

*The prototype.* (1) Solve the relaxation with the library's step costs (sign-aware cost included) and the library's capacity loop.
(2) Give every frozen edge its relaxed value; a half is rounded to the neighbour nearer to the real offset. (3) Run the library
unchanged: its greedy balancing repairs the component sums the rounding may leave, its flow solves the network columns, then subdivision,
greedy moves, SAT, positions, extraction. The result is always an integer solution of the library's own problem with other frozen
values; it is not the optimum of the integer bi-directed problem where the relaxation has halves.

Checks in the benchmark: the relaxed solution closes every equation (twice the residual plus the signed steps is zero, per row); the
replay of the library's steps through reflection without the jumps reproduces the library's status and mesh on every run (0 mismatches
in 120 runs); the frozen values the jumps wrote are read back after the library's flow (its balancing moves at most 4 frozen edges
per run and never resets them); the results are judged by the gate's independent check only.

## 2. Measurement

Setup: the 54 runs of `QuadFlow3BGate` (9 procedural closed shapes x 6 variations), master 4137234d, net10.0 Release, 4-core cloud
container, single runs. Commands and full tables: the benchmark README and its `results_*.txt`.

### 2.1 Clean counts (the gate's independent check)

| configuration | library clean | jumps clean | gained | lost | clean with either | undetected bad |
|---|---|---|---|---|---|---|
| `SizeAdaptation` Off (the 54 gate runs) | 43 | **52** | 10 | 1 | 53 | 0 |
| `ScaleAndRotation` (54 runs; the size field is applied on 42, not on the capsule) | 48 | **52** | 5 | 1 | 53 | 0 |
| capsule and capped cylinder, `CurvatureAlignment` 10 (12 runs) | 3 | **12** | 9 | 0 | 12 | 0 |

- **The 11 fallbacks** (Off): 10 become clean: all 6 capped cylinder rows and 4 of the 5 capsule rows (7 of the 8 `FlippedFaces` rows
  and all 3 `BudgetExceeded` rows). The capsule at scale 1.5 stays `FlippedFaces`.
- **The 3 always-fallback inputs**: the capsule at scale 1 and the capped cylinder at ratio 0.35 are clean with the jumps in all
  three configurations (for the capsule `ScaleAndRotation` is the same run as Off: its size field is not applied, and `Scale` is not
  applied on either shape, so it is not listed); the capsule at scale 1.5 is clean with alignment 10 (area 0.98, corner p95 26.7 degrees) and a fallback otherwise.
- **The 43 clean runs**: 42 stay clean; the bumpy sphere at ratio 0.7 becomes `OrientationFolds` (one fold after the flow that the SAT
  cannot remove; the library's flow leaves none there). The same row is lost with `ScaleAndRotation` and with the other two rounding
  rules. It has 6 half-integral frozen components.
- The rounding rule does not matter on this set (`round=keep`, `round=down`: the same counts).

### 2.2 What the jumps change in the flow (54 runs, Off)

| | library | jumps |
|---|---|---|
| frozen edges changed by the jumps | - | 9.5 % of them; the largest change is 3, no value leaves -2..2 |
| step cost of the offsets after the flow, sum over the runs | 46.5 M | 20.6 M (relaxation: 18.5 M, the lower bound) |
| runs where the cost equals the relaxation's (the rounded solution is optimal) | - | 21 of 54 (7 of the 10 gained rows) |
| folds right after the flow, sum over the runs | 99 | 11 |
| runs that need a flow capacity above 2 | 4 | 0 |
| map area over surface area: clean rows min / median / max | 0.10 / 0.94 / 1.14 (43 rows) | 0.27 / 1.02 / 1.27 (52 rows) |
| clean rows with an area below 0.75 ("clean but collapsed") | 6 | 4 (all capsule) |

The frozen edges cost more than half of the flow's objective: with them free the cost drops to 40 % and the flow leaves a ninth of the
folds. On one run the rounded solution costs more than the library's (filleted box at ratio 0.35: 254k against 133k, relaxation 44k;
16 half-integral frozen components); it is clean both ways.

### 2.3 Quality

Stage 2 is the quad-dominant mesh the fallback returns. Corner deviation p95 in degrees, Hausdorff distance and edge lengths in units of
the target edge length.

| rows | stage 2 | library | jumps |
|---|---|---|---|
| 42 rows clean both ways: corner p95, median | 13.1 | 15.5 | **12.1** (lower than the library on 35 rows) |
| the same rows: corner p95 at most 15 | 30 | 20 | 33 |
| the same rows: Hausdorff at most 0.5 | - | 40 | 41 |
| the same rows: irregular vertices | 8 to 228 | 0 to 20 | the same on 41 rows, one more on 1 |
| capped cylinder, 6 gained rows: corner p95 | 12.7 to 17.5 | - | 14.9 to 26.4 |
| capped cylinder: irregular vertices; edge length p05 to p95 (max) | 59 to 105; - | - | 8; 0.54 to 1.18 (1.36) |
| capped cylinder: Hausdorff; map area | 0.27 to 0.44; - | - | 0.20 to 0.29; 1.13 to 1.27 |
| capsule, 4 gained rows: corner p95 | 12.4 to 19.4 | - | 27.2 to 35.1 |
| capsule: irregular vertices; edge length p05 to p95 (max) | 66 to 91; - | - | 8 to 10; 0.44 to 2.62 (3.22) |
| capsule: Hausdorff; map area | 0.10 to 0.17; - | - | 0.15 to 0.36; 0.39 to 0.77 |
| capsule at scale 0.8 (clean both ways): corner p95; Hausdorff; area | 13.7; 0.09; - | 65.8; 3.59; 0.10 | 40.6; 0.43; 0.27 |

- **Capped cylinder: good.** 8 irregular vertices (the corners of the two caps) instead of 59 to 105, a smaller Hausdorff distance than
  stage 2, edges within 0.54 to 1.36 of the target. The mesh is denser than asked (area 1.13 to 1.27, 14 to 26 % more quads than
  stage 2) and the corner p95 is up to 10 degrees above stage 2 (at ratio 0.35 it is 2 below).
- **Capsule: valid and coarse.** The meshes pass every check and have 8 to 10 irregular vertices, but the quads at the caps are
  stretched (edges up to 2.1 to 3.2 times the target), the corner p95 is about twice that of stage 2, and a quarter to three quarters of
  the cells are missing (scale 0.8 and 1.25: 0.27 and 0.39, clearly worse than stage 2). This is the part the sheaf note assigned to the
  cross field (its relaxation kept 0.68 to 0.78): the four cones of a cap sit close together and the barrel period follows from their
  distances. With `CurvatureAlignment` 10 the capsule is clean in all 6 variations with areas 0.42 to 0.98 and corner p95 20 to 28.
- **The "1.7 to 3.5 cells" of the sheaf note** were the L2 correction with free cones. In the L1 problem here no frozen edge moves by
  more than 3 and none leaves -2..2; the distortion shows as stretched quads at the caps of the capsule, not as an invalid map.
- **Other shapes.** The collapsed clean rows of the library recover: noisy sphere 0.50 to 0.63 becomes 0.94 to 1.01, bumpy sphere 0.74
  to 0.89 becomes 1.11 to 1.16, with a lower corner p95 on every one of them.

### 2.4 Time

| | library | jumps |
|---|---|---|
| repair time, sum over the 54 runs (Off) | 9.3 s | 19.9 s, of it 14.2 s the relaxation |
| repair time per run, ratio to the library, median | 1 | 2.1 |
| clean rows within the gate max(3 x stage 2, 1 s) | 41 / 43 | 46 / 52 (estimated: the library's `Compute()` with the repair replaced) |
| the same with `ScaleAndRotation` | 48 / 48 | 51 / 52 |

The prototype solves two flows (the relaxation on the doubled network, then the library's). The relaxation alone is 1.5 times the whole
library repair. The library's own flow is cheaper after the jumps on the hard rows (no capacity step above 2, almost no SAT:
the whole attempt on the capsule at scale 1 takes 0.33 s against 0.56 s). A library version can drop the second flow where the relaxation has no half on any
edge; today that is rare (5 of 54) because the solver splits flow between an arc and its mirror even where it need not (the aligned
torus has no frozen edge and still 6 to 106 halves at the same cost). Times are single runs with reflection overhead in both columns.

## 3. Stop rule of the brief

| condition | result |
|---|---|
| fewer than 2 of the 3 always-fallback inputs clean | **not met**: 2 of 3 without any lever, 3 of 3 with alignment 10 on the third |
| clean results clearly worse than the stage 2 fallback | **not met** on the capped cylinder (6 rows) and on the 42 rows clean both ways; **met** on 2 capsule rows (scale 0.8 and 1.25: area below 0.4, corner p95 35 to 41), borderline on the other 3 capsule rows |
| a clean run regresses | **met** for "jumps always": 1 of 43 (bumpy sphere, ratio 0.7). **Not met** for "jumps as a second attempt after a fallback": 53 of 54 by construction |

Read literally the third condition stops the always-on variant. The second-attempt variant passes all three, with the capsule caveat.
The prototype stayed inside tier M; no integer solver was written.

## 4. Options and the effort of a full solver

*How others solve it* (search of 2026-11-22; abstracts and search results only, the project page of the Bi-MDF paper is outside this
session's network policy and was not fetched). Heistermann, Warnett and Bommes 2023 state T-mesh quantization as a minimum-deviation
flow in a bi-directed graph and give a fast approximate solver and an exact one "based on iterative refinement with matching"; the
integers there are chosen on a T-mesh before the map is optimized. The theory is older: bi-directed flows and their integer hull
(Edmonds and Johnson 1970), skew-symmetric networks (Goldberg and Karzanov 1996), the reduction to b-matching. Nothing was found that
frees the non-network columns of a QuadriFlow-style per-triangle repair; terms searched: "bidirected min-cost flow half-integral
skew-symmetric", "Min-Deviation-Flow bi-directed T-mesh quantization".

| # | option | what it gives on the gate set | effort | risk |
|---|---|---|---|---|
| 1 | **The prototype in the library, as a second attempt**: after a fallback of the pure-quad attempt, solve the relaxation, round the frozen edges, run the repair again; keep the first result if it was clean | 53 of 54 clean (Off) instead of 43; no clean run changes; clean runs cost what they cost today, a fallback costs about 3 times its repair | **M**: about 150 lines in `QuadFlowRepair` (the doubled network over the existing arrays, `MinCostFlow` unchanged), reuse, invariance and determinism tests, the gate as the benchmark | low: integer and serial like the flow; the capsule results are valid and coarse (section 2.3), which the area ratio (option A of the sheaf note) would make visible |
| 2 | The same, always on (the jumps replace the frozen values of every run) | 52 of 54 clean, and better meshes on the runs that are clean today (corner p95 median 12.1 against 15.5, no collapsed sphere rows); one clean run lost; about twice the repair time, time gate 46 of 52 | M, the same code | medium: it changes every pure-quad result (an output change: owner), loses one row, and misses the time gate on 6 rows until the second flow is dropped where the relaxation is integral |
| 3 | 2 with a fallback to the library's values when the attempt is not clean ("best of both") | 53 of 54 with the better meshes; the lost row costs two attempts | M plus little | as 2 without the lost row |
| 4 | An exact solver for the integer bi-directed flow (remove the halves optimally, by matching, after Heistermann et al.) | bounded by the gap between the rounded and the relaxed cost: zero on 21 of 54 runs and on 7 of the 10 gained rows; it could matter for the lost row (6 halves) and the filleted box at ratio 0.35 | **L**: a min-cost perfect matching (blossom) solver is new in the library, the paper must be supplied by the owner, a design doc first | high effort for no measured clean row; not needed for the gain above |
| 5 | Move the cones (option E of the sheaf note) | the capsule's missing area: alignment 10 already brings it to 0.42 to 0.98 with the jumps | L, cross-field roadmap | unchanged |

**Leaning.** Build option 1 (tier M), then measure option 3 on the same code, because the quality numbers favour the jumps on the
runs that are clean today; switching the default to 3 is an output change for the owner. Do not build 4. Build the area ratio
(option A of the sheaf note) with or before it, so that the coarse capsule results are reported as such.

**Library API sketch** (nothing built; new public options and defaults are the owner's call):
- *Internal only (leaning)*: `QuadFlowRepair` gets `internal bool FreeFrozenEdges`; `QuadRemesher.Compute()` runs the pure-quad attempt
  and, if it is not `Success`, a second `QuadFlowRepair` with the flag set. No public surface. `PureQuadStatus` keeps its meaning (the
  status of the attempt that is used, or of the first if both fail); `FlowSeconds` and `RepairSeconds` are the sums. Results that are
  `Success` today stay bit for bit.
- *Opt-in*: `public PureQuadJumps PureQuadJumps = Off | OnFallback | Always` on `QuadRemesher` (a new enum, default `Off`: API
  compatible), for users who want the better meshes of option 2 or 3 before the default changes.

## 5. Limits

- 54 runs on 9 procedural closed shapes, single runs; the two shapes that gain are the two that failed, so the gain is in sample.
- The rounding is a heuristic. The prototype is not the optimum of the integer problem on the 33 runs with a half on a frozen edge.
- The frozen set is the one of the library's breadth-first tree; another tree gives other frozen edges and possibly other results.
- The time with the jumps is estimated from a replay through reflection, not from library code.
- The fold of the lost row was not diagnosed (area-obstructed or not); the benchmark reproduces it with
  `only=bumpy var='^1/0.7'`.
- Open meshes are not covered (`PureQuads` rejects them).

## 6. Sources

Papers and abstracts only; no code of QuadriFlow, QuadWild, libSatsuma, CoMISo, Instant Meshes or of any flow, matching or SAT solver was
read or run.

- J. Edmonds, E. L. Johnson, *Matching: a well-solved class of integer linear programs*, in Combinatorial Structures and Their
  Applications, Gordon and Breach 1970 (bi-directed flows; as cited by the papers below, not read).
- A. V. Goldberg, A. V. Karzanov, *Path problems in skew-symmetric graphs*, Combinatorica 16(3), 1996, and *Maximum skew-symmetric
  flows and matchings*, Math. Programming 100, 2004, arXiv:math/0304290 (abstract and search result: the doubled network of a
  bi-directed flow; the min-cost form used here is derived in section 1).
- M. Heistermann, J. Warnett, D. Bommes, *Min-Deviation-Flow in Bi-directed Graphs for T-Mesh Quantization*, ACM TOG 42(4), 2023
  (abstract only; the paper host is blocked for this session).
- R. K. Ahuja, T. L. Magnanti, J. B. Orlin, *Network Flows*, 1993, section 9.7, and J. Huang et al., *QuadriFlow*, CGF 37(5), 2018: as
  used in `MinCostFlow` and `QuadFlowRepair` ([sources/meshing-and-repair.md](../sources/meshing-and-repair.md)).
