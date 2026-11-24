# N-ary union on dense packings: where one arrangement goes superlinear (#1466)

Diagnosis only, no library change. Question: `ExactMeshBoolean` with all meshes of a dense ball packing in one arrangement
takes 4.3 s at 96 balls, a balanced tree of pairwise unions 1.2 s ([#1455](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1455),
`benchmarks/ProceduralSphereUnionProbe`). Where is the superlinear cost, is it inherent, and is there a cheap rule?

Measured with the probe's new modes `scaling` and `hybrid` (raw lines in `benchmarks/ProceduralSphereUnionProbe/results/`,
aggregated by `aggregate_scaling.py`). Procedural balls only (`Sphere3Generator_NormalizedCube`, 5 vertices per cube edge, 192
triangles per ball), `StrictRounding = true`, master 9fd7dcd7a, net10.0 Release.

## 1. Short answer

The superlinear term is the per-patch winding stage of the classification (`classify`, `classification.winding`;
`patch_winding` -> `front_crossings`, `mesh_ops/ExactMeshBoolean.cs`): it casts one exact ray per (patch, closed body whose
box contains the patch centroid). On a sphere cut by `k` neighbours the arrangement has about `0.5 k^2` patches and each patch
sees about `0.55 k` bodies, so the ray count grows like `N * k^3`, while the overlap degree `k` of a growing dense packing
itself rises with `N`. The rest of the arrangement (intersections, merge, triangulation) grows like the piece count, about
`N * k` (1.7 power of `N` here), and 95 % of those pieces end on source triangles that the union discards.

## 2. Reproduction

Inputs: `dense<seed>` = `MakeRandomPacking(n, seed)` (the probe's multi-radii packing; prefixes of one seed are nested),
`helix` (1D chain), `lattice` (cubic, spacing 0.9 of the diameter; each ball meets 6 neighbours). Per size 5 runs of
multi and tree in alternation, in one process after a warm-up run. Medians, `[min-max]`. Other sessions used the machine:
absolute times carry about +-20 % (a later series ran 1.4x slower), counts and stage shares are the evidence.

| Family, balls | overlap degree mean (max) | multi ms | tree ms | multi / tree | result tris multi / tree |
|---|---|---|---|---|---|
| dense104, 12 | 6.0 (9) | 52 [46-76] | 94 [87-131] | 0.55 | 2562 / 2784 |
| dense104, 24 | 8.3 (14) | 120 [107-132] | 229 [202-242] | 0.52 | 3904 / 4338 |
| dense104, 48 | 14.3 (30) | 263 [245-281] | 245 [230-379] | 1.07 | 6452 / 7640 |
| dense104, 96 | 23.3 (54) | 1124 [1049-1190] | 545 [512-555] | 2.1 | 10212 / 12168 (+19 %) |
| dense104, 144 | 30.1 (71) | 2788 [2656-2893] | 931 [856-951] | 3.0 | 12722 / 15086 |
| dense104, 192 | 34.0 (86) | 4606 [4207-4707] | 1229 [1216-1285] | 3.7 | 15564 / 18780 (+21 %) |
| dense103, 96 (second instance) | 19.3 (39) | 770 [754-980] | 576 [547-682] | 1.3 | 10708 / 12534 |
| helix, 192 | 2.8 (4) | 288 [279-302] | 1088 [1056-1132] | 0.26 | 48084 / 49272 |
| lattice, 216 | 5.0 (6) | 443 [430-579] | 1305 [1270-1370] | 0.34 | 58752 / 58752 |

Growth exponents of the time (`log2` of the ratio per doubling): one arrangement on dense104 1.2 (12 to 24), 1.1 (24 to 48),
2.1 (48 to 96), 2.0 (96 to 192); the tree 1.2 (48 to 96) and 1.2 (96 to 192). Helix: 1.0 (one arrangement), 1.1 (tree).
Lattice (constant degree), 125 to 216 balls: 0.74 as a power of the ball count for the arrangement and 0.86 for the tree. The
issue's 4.3 s at 96 balls was a slower run of the same case (1.1 to 1.6 s here); the shape is the same: the cliff
(1.1 to 2.1 exponent) sits at overlap degree about 14 and above, and one arrangement wins below that (dense104 24 balls, lattice,
helix). The crossover between the routes is at mean degree 12 to 14 (dense103, 48 balls, degree 12.4: 214 vs 245 ms, still the
arrangement).

Note on "dense": the packing grows by attaching balls to random existing ones, so its mean overlap degree rises with the
size (6, 8, 14, 23, 30, 34); it is not a constant-density packing. The lattice and helix are the constant-degree controls
and are linear.

## 3. Stage times and counts (one arrangement, dense104)

Stage times in ms (medians of 5), shares of the wall time in brackets. The sub-steps `classification.*_us` are in
microseconds in `PhaseMilliseconds`.

| Stage | 48 balls | 96 balls | 192 balls | 96 to 192 |
|---|---|---|---|---|
| total | 263 | 1124 | 4606 | 4.1x |
| intersections (pairs, planes, intersect) | 46 (17 %) | 141 (13 %) | 328 (7 %) | 2.3x |
| merge (points, crossings, predict) | 54 (21 %) | 142 (13 %) | 427 (9 %) | 3.0x |
| triangulation | 48 (18 %) | 203 (18 %) | 690 (15 %) | 3.4x |
| classification | 87 (33 %) | 564 (50 %) | 3087 (67 %) | 5.5x |
| of it `winding` (patch rays) | 74 (28 %) | 522 (46 %) | 2942 (64 %) | 5.6x |
| of it stacks, edges, patches, decisions | 11 | 36 | 130 | 3.6x |
| assembly (with simplify) | 9 (3 %) | 34 (3 %) | 32 (1 %) | - |
| rounding repair | 9 (3 %) | 12 (1 %) | 22 (0.5 %) | - |

Counts (multi, medians; 12, 24, 48, 96, 192 balls):

| Count | 12 | 24 | 48 | 96 | 192 |
|---|---|---|---|---|---|
| input triangles | 2304 | 4608 | 9216 | 18432 | 36864 |
| candidate pairs | 2999 | 8751 | 31989 | 100974 | 295652 |
| intersecting pairs | 1493 | 4365 | 15866 | 50061 | 146566 |
| pieces after the arrangement | 8588 | 23424 | 82268 | 272892 | 861508 |
| pieces per source triangle, mean | 3.7 | 5.1 | 8.9 | 14.8 | 23.4 |
| pieces per source triangle, max | 41 | 53 | 89 | 151 | 297 |
| source triangles with 17 to 256 pieces | 52 | 235 | 1510 | 6240 | 17500 |
| patches (first pieces evaluated) | 185 | 729 | 4904 | 27322 | 119598 |
| patches per ball | 15 | 30 | 102 | 285 | 623 |
| (patch, body) candidates (boxes containing the centroid) | 789 | 3862 | 44083 | 359k | 2.21M |
| candidates per patch | 4.3 | 5.3 | 9.0 | 13.1 | 18.4 |
| plane side tests of the rays (`winding_plane_sides`) | 5270 | 25110 | 278072 | 2.27M | 13.7M |
| kept pieces = result triangles before rounding | 2562 | 3904 | 6452 | 10212 | 15564 |
| rounding: edits / intersections | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 1 / 0 |

The pieces-per-triangle distribution (instrumented run, 192 balls): 1 piece 5971 triangles, 2 to 4: 1995, 5 to 16: 11396,
17 to 64: 14570, 65 to 256: 2930, over 256: 2. The triangulation costs 0.7 to 0.9 us per piece at every size, so it is
linear in the pieces; no per-piece loop over all bodies shows up there.

Tree (sums over its 95 or 191 unions): pieces 174752 (96) and 394826 (192), 45 % of the single arrangement's; plane side tests
7428 and 16250 (0.3 % and 0.1 %); classification 19 ms and 58 ms.

## 4. The term, with its code location

1. `classify` (ExactMeshBoolean.cs, `classification.winding`): `gParallel.ForRange(firsts.Count, ...)` calls `patch_winding(i, ...)`
   once per patch (a connected set of pieces of one mesh not separated by an intersection curve; `join_pieces` joins across
   every edge that is not a `ConstraintEdges` member).
2. `patch_winding`: the candidates are the closed bodies whose box contains the centroid (`closedTree.Query`), so the cost per
   patch is the number of bodies near it; for each, `front_winding` -> `front_crossings` casts an exact ray to a far point
   outside that body's box and tests the planes of the triangles the segment's box query returns (`winding_plane_sides`,
   about 6 tests per (patch, body) at 192 balls; the stage's wall time per test is 215 to 230 ns at 96 and 192 balls).
3. Why it grows like `N * k^3`: a ball cut by `k` neighbours is a circle arrangement on a sphere, `O(k^2)` faces; measured
   patches per ball `/ k^2` = 0.42, 0.44, 0.50, 0.52, 0.54 for 12 to 192 balls. Candidates per patch `/ k` = 0.72, 0.64, 0.63,
   0.56, 0.54: bodies whose box holds a point of the sphere are the point's neighbours. So the ray count is
   about `0.28 * N * k^3`. With `k` itself growing from 23 to 34 and `N` doubling, 96 to 192 balls is 6.1x in candidates and 6.0x in
   plane side tests (time 5.6x), 48 to 96 is 8.2x (time 7.1x).
4. The candidates are the real overlap, not box slack: 94 to 95 % (48 to 192 balls) of the candidate bodies share an
   intersection curve with the patch's ball, and about half evaluate to a nonzero winding.
5. The arrangement stages scale with the pieces, `pieces per triangle = 0.62 to 0.69 * k`, so `N * k`: exponent 1.7 per
   doubling here. Not quadratic per triangle (max 297 pieces on one triangle at 192 balls; the triangulation time per piece is flat).

Not found: a per-piece loop over all bodies (the stacks, `coplanar_stack`, are empty, 0 coplanar tests; the open path is
unused, `classification.open` 0), the pair search (candidate pairs per input triangle 1.3 to 8, linear in `k`), assembly and rounding
(under 4 % of the time, no rounding edits).

## 5. Why the tree avoids it

Each pairwise union deletes the interior surface before the next union sees it. In the single arrangement:

| | 48 balls | 96 balls | 192 balls |
|---|---|---|---|
| pieces kept in the result | 7.8 % | 3.7 % | 1.8 % |
| source triangles with at least one kept piece | 36 % | 29 % | 22 % |
| pieces on source triangles that have no kept piece | 80 % | 90 % | 95 % |
| pieces of the tree, relative to the single arrangement | 95 % | 64 % | 46 % |

So 95 % of the triangulated pieces and, via the patches, nearly all of the winding rays of the 192-ball arrangement are spent on
surface inside the union. The tree's unions have only two bodies, `k` at most two, so no patch sees more than two candidates
(1.6 per patch at every size) and the cost per input triangle stays flat, at the price of 191 arrangements
and 18 to 21 % more result triangles (a hybrid, section 6, shows where that price starts). About 40 % of the tree's wall time at 96 balls lies outside the stage timers (not attributed here).

## 6. Upper bounds of the levers (throwaway probes, reverted)

Throwaway edits of `ExactMeshBoolean.cs` (patch `results/instrumentation_1466.patch`: counters, a memo for non-neighbour
bodies, a mode that evaluates only the first patch of each mesh; `.editorconfig` set to not fail on the determinism
analyzers; all reverted, `git diff origin/master -- mesh_ops math` is empty). The instrumented counters cost wall time
(contended `Interlocked`), so the timing runs use a build with the counters off and the modes selected by environment
variables. Alternating A (library as is), B (`skip`), C (`memo`), 6 rounds each, one process per run
(`results/ub_winding_raw.txt`); the machine was 1.4x slower than during section 2, so read the ratios only.

| Lever (ceiling = the work removed, nothing added) | 48 balls | 96 balls | 192 balls | helix 192 | lattice 216 |
|---|---|---|---|---|---|
| winding stage as is, ms | 100 | 679 | 3627 | 5.3 | 15 |
| B: winding evaluated for the first patch of each ball only (propagation of the winding across the curves, stage cost without the propagation), stage ms | 23 | 165 | 656 | 3.6 | 4.8 |
| total time A / B, ms | 433 / 439 (noise) | 1636 / 1151 (-30 %) | 6010 / 3072 (-49 %) | n/a | n/a |
| idealized: stage down to 0 | -23 % | -41 % | -60 % | -1 % | -2 % |
| C: bodies that share no curve with the patch's ball evaluated once per (ball, body) | winding 87 vs 100 | 736 vs 679 (noise) | 3413 vs 3627 (-6 %) | none | none |

Reading:

- B is the ceiling of "classify the pieces by propagating the winding number across patches" (Zhou et al.'s labelling; the
  library's exact radial order, `radial_order`, `ExactMeshCells`, and the test-only `BooleanCertificate` C3 already do the
  jump rule). The classification would give the same windings as today (integers), so the result stays bit for bit the same.
  The measured B result is garbage (its `keep` is wrong, so the rounding and assembly times are not comparable); only the
  stage is. It does not reach the tree: the remaining stages at 192 balls (about 1.5 to 2.4 s in these runs, 1.7 s in the
  quiet run: 4606 - 2942) are still 1.4x the tree's 1.2 s, because the arrangement has 2.2x the tree's pieces.
- C: at most 6 % of the candidate evaluations are non-neighbours, so a memo saves 0 to 6 % (inside the noise). Not worth it.
- Cull before the arrangement (the library's T3a cull, `CullOutsideA`, `ExactMeshArrangement.cull`): it is built for
  Difference and Intersection only (triangles wholly outside the first mesh, or the others), never for Union, and the dual for
  a union (triangles wholly inside another body) finds almost nothing here: the triangles with exactly one piece, which are
  the only ones that meet no other surface, hold 5971 of 861508 pieces (0.7 %) at 192 balls. The discarded pieces sit on triangles
  that are cut by several bodies and covered by their union, so knowing they are interior needs the union (the tree).
- Restricting the per-piece classification to the bodies whose boxes contain the piece: already done (`closedTree.Query`),
  and the box slack is only 4 to 6 % of the candidates.
- Hybrid: groups of `g` consecutive bodies in one arrangement each, then a pairwise tree of the group results, public API only
  (`results/hybrid_raw.txt`, 5 rounds alternating inside one process, machine loaded: spreads of the times are 1.5x; counts
  are exact).

| g (192 balls, input order) | pieces | plane side tests | result tris | ms median [min-max] |
|---|---|---|---|---|
| 1 (pairwise tree) | 394826 | 16250 | 18780 | 1617 [1295-3166] |
| 8 | 319694 | 18956 | 18764 | 1293 [951-2188] |
| 16 | 289160 | 41642 | 18720 | 1222 [862-2335] |
| 32 | 281412 | 167072 | 18488 | 1244 [851-1874] |
| 64 | 348674 | 1134432 | 17874 | 1613 [1148-1876] |
| 192 (one arrangement) | 861508 | 13716408 | 15564 | 7655 [5323-11060] |

  At 96 balls: g = 1: 688 ms, 8: 474, 16: 406, 32: 442, 96: 1132 (same run, loaded). Groups by Morton order of the centres
  are not better than the input order (the packing's input order is already local): 192 balls g = 16: 1568 ms vs 1222.
  So a hybrid with g about 16 has 0.6 to 0.75 of the tree's time on the dense packings (inside the spread, its
  pieces are 73 % of the tree's and 1/3 of the single arrangement's) and the same triangle count as the tree to 1 % (18720 vs
  18780, 20 % above the single arrangement). On the sparse controls the hybrid is slower than one arrangement:
  helix 192 balls: g = 32 743 ms, g = 8 1017 ms, one arrangement 620 ms (same loaded run), the tree 2868 ms; lattice 216: g = 27
  895 ms, one arrangement 454 ms, tree 1912 ms. A fixed `g` is therefore a loss on sparse inputs; it needs the density rule.
- Automatic choice of the route by the overlap degree: the crossover is at mean degree 12 to 14 for these balls (section 2).
  Whatever the rule, the output differs from today's for the inputs it redirects: +19 to +21 % triangles on the dense packings
  (the volume agrees to 1e-9, area likewise; the vertex and triangle hashes differ). So the rule is deterministic and gives the
  same result on every run for the same input, but not the same result as the single arrangement: a change of output, an
  owner decision (the issue says so).

## 7. Prior art

- The classification by winding numbers of patches of one arrangement, propagated across patches, is Zhou, Grinspun, Zorin,
  Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG 2016, and Cherchi, Pellacini, Attene, Livesu, *Interactive and Robust
  Mesh Booleans*, ACM TOG 2022 (both in [docs/sources/exact-arithmetic-and-booleans.md](../sources/exact-arithmetic-and-booleans.md);
  `BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md` section 3.3 lists the jump rule across patches and one anchor ray per connected component
  as the certificate's C3). Our classification instead casts a ray per patch for the closed bodies (the carried rays, L2, are for
  open inputs). From memory, not re-read for this note: both papers label cells by propagation over the patch adjacency graph, and
  neither addresses the density of overlapping bodies or a clustered n-ary union; the 2022 paper builds one arrangement for a CSG
  tree.
- Manifold (Apache-2.0) exposes `BatchBoolean` for many operands; its internal strategy (I recall pairwise merging of the smaller
  results first and composing disjoint parts without a Boolean) is not verified: a web search did not return the source, no code
  was read. A pairwise or batched tree is the common route of the established kernels for many operands.
- Zhou et al., Cherchi et al. and EMBER (Trettner et al. 2022, plane-based local arrangements) are all single-arrangement
  methods; none of them, to our knowledge, reports a density-driven cliff. "Nothing found" for a published density rule for
  choosing between a tree and one arrangement (terms searched: n-ary mesh arrangement dense overlapping bodies, CSG union many
  spheres, Manifold BatchBoolean); the molecular surface literature avoids Booleans for many overlapping balls and uses
  implicit or analytic (alpha shape, power diagram) constructions.

## 8. Options

| Option | Ceiling (96 / 192 balls) | Sparse cost | Output | Cost of work |
|---|---|---|---|---|
| 0. Nothing in the library: document, and recommend the pairwise tree or the grouped union for dense packings in the guide with the degree threshold | none | none | none | one guide section |
| 1. Winding propagation across patches (one exact ray per connected component, jump by the exact radial order or the plane-side sign across each curve) | -41 % / -60 % of the time of the single arrangement (idealized); stays 1.4x the tree at 192 balls | at most 1 to 2 % gain, no loss if the adjacency build is linear | the same bit for bit (same integers) | tier L (exact code, a new mechanism: spec, review, mutation) |
| 2. Automatic route by mean overlap degree (or the predicted piece count) to the tree | to the tree's time (-52 % / -73 %) | none if the rule only fires above degree about 12 | changes (+19 to +21 % triangles on redirected inputs) | owner decision; a few lines plus the box-overlap degree |
| 3. Grouped union (g about 16) chosen by the same rule | about 0.75 of the tree's time | none if the rule only fires on dense input | changes; between the tree's and the arrangement's triangle count | owner decision; a design for the grouping order |
| 4. Memo of non-neighbour windings | -0 to 6 % | none | same | not worth |
| 5. Union cull | under 1 % | none | same | not worth |

Leaning: option 0 now (a short guide paragraph: one arrangement for sparse and chain-like inputs, a grouped union or the tree
once the mean overlap degree is above about 12, with the numbers of section 2 and the +20 % triangle count of the tree), and
option 1 as the fix of the superlinear term if the owner wants one arrangement to stay usable on dense packings (it is
the only lever that keeps the output and removes the `N * k^3` term; with it the single arrangement would be linear in
`N * k`, still above the tree at 192 balls). Options 2 and 3 change the output: owner decision, and they come after a measurement
of the box-overlap degree as a predictor (the sphere degree used here is not available to the library). What a user pays for
option 0: 1.1 to 3.7x time for the single arrangement above degree 14 if they do not follow the advice, or +20 % triangles
(and 95 unions, no single `Compute`) if they do.

## 9. What was too noisy or not verified

- Absolute times (+-20 %, later series 1.4x slower, hybrid spreads up to 1.5x); the 48-ball ratios of the upper-bound table
  and the hybrid medians are inside the noise. Counts are exact and reproducible (the same counts in every repetition).
- The upper-bound mode B produces a wrong classification: only its stage time is meaningful.
- The patch / pieces counters come from the throwaway instrumentation (one run per size, `diag_instrumented_raw.txt`); the same
  `pieces` count as in the uninstrumented run was checked at every size.
- Manifold's `BatchBoolean` strategy and the two papers' statements are from memory.
- The mean sphere degree is a probe quantity; the box-overlap degree as the rule's predictor was not measured.
- No second dense family beyond a second seed (dense103, 24 to 96 balls) and no real molecule.
