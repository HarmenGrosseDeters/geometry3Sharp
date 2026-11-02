# Terrain phase 1c: plan

Status: plan of the 1c PR, first part: the literature check for the speed of the slope surface and the upper bound of
each idea, before any of it is built (coordinator, 2026-11-02, "look before you build"; AGENT_WORKFLOW.md 2, 3, 8).
Section numbers refer to [TERRAIN.md](TERRAIN.md). Scope of 1c (section 11): volumes (5.7), failure kinds (5.6),
catch-line accuracy (5.5), km-scale benchmarks (8), and the speed case of 1a (the wavy 64-gon).

## 1. Where the time goes (measured)

The speed case of 1a, `TerrainBenchmark slope toe=wavy n=64 r=100 h=30 coords=utm` (closed wavy 64-gon, toe heights
±4 m, 1:1, `Tolerance` 0.01), 4 cores, 2026-11-02, master with 1b (#285):

| total | builds | insert | simplify | realize / check | predicates (exact) | exact stage | triangles | λ |
|---|---|---|---|---|---|---|---|---|
| 44.0 s | 3 | 17.0 s | 26.3 s | 0.39 / 0.12 s | 79.5M (6.0M = 7.5 %) | 18.4 s | 14577 | 5.9e-6 m |

- The exact stage was timed by a local stopwatch around the exact branches of `ExactSlopeKernel.Side` / `Orient` (not
  committed): 18.4 s for 6.0M calls, about 3 µs each; the filtered 73.5M take the remaining 25 s, about 0.34 µs each.
- Per build: about 14.7 s, almost all in `insert` and `simplify`; realization and the exact output check are about 1 %.
- 5400 predicates per output triangle.

## 2. Literature: lower envelopes of planes and cones

S̃ = min_k F_k, each F_k the maximum of the planes of one toe piece (a convex polyhedral function), so S̃ is the lower
envelope of n convex polyhedral functions, or, with all planes, of N piecewise linear functions; its projection is the
minimization diagram. The exact R1 surface (min of cones) has as its diagram a weighted Voronoi diagram of the toe
segments.

| family | what it gives | sources | fits here? |
|---|---|---|---|
| Lower envelopes of piecewise linear functions / triangles in 3D, divide and conquer | O(n² α(n)) worst-case complexity and time for n triangles; the merge of two envelopes is an overlay | H. Edelsbrunner, L. J. Guibas, M. Sharir, *The upper envelope of piecewise linear functions: algorithms and applications*, DCG 4, 1989; M. Sharir, P. K. Agarwal, *Davenport–Schinzel Sequences and Their Geometric Applications*, Cambridge 1995, ch. 7 | the worst case is for arbitrary triangles; our functions are convex per piece and the diagram is Voronoi-like (each piece owns a connected region near its segment), where the complexity is linear in practice. The merge step is the 1b overlay with a min instead of a difference |
| Randomized incremental construction (RIC) | expected time bounded by the expected size of the structure after each step (backwards analysis); for envelopes of triangles O(n² α(n)) expected, output-sensitive for Voronoi-like diagrams, O(n log n) | K. L. Clarkson, P. W. Shor, *Applications of random sampling in computational geometry II*, DCG 4, 1989; K. Mulmuley, *Computational Geometry: An Introduction through Randomized Algorithms*, 1994; J.-D. Boissonnat, K. Dobrindt, *On-line construction of the upper envelope of triangles and surface patches in three dimensions*, CGTA 5, 1996 | **yes, closest to 1a**: 1a already inserts piece after piece into a planar map; it inserts in toe order and keeps conflict information only through the quadtree. A random order bounds the expected number of faces a new piece changes |
| Sweep (Fortune) | O(n log n) for Voronoi diagrams of points, of segments and of additively weighted points, by a transformation that makes the sweep line meet a site before its region | S. Fortune, *A sweepline algorithm for Voronoi diagrams*, Algorithmica 2, 1987 | for constant slope the R1 diagram is the Voronoi diagram of the toe segments with additive weights (the toe heights, linear along a segment): curved bisectors (conics), exact predicates of high degree; varying slopes give multiplicative weights. Not for the plane-based S̃ of 1a, whose cells are polygons; a sweep over the planar map would need the event structure for every plane crease |
| Multiplicatively weighted diagrams | Θ(n²) complexity in the worst case for points | F. Aurenhammer, H. Edelsbrunner, *An optimal algorithm for constructing the weighted Voronoi diagram in the plane*, Pattern Recognition 17, 1984 | explains why varying slopes have no near-linear guarantee; per toe piece the diagram is still a connected cell in our cases |
| Lower envelope of planes = convex polytope (duality, 3D hull) | O(N log N) | F. P. Preparata, S. J. Hong 1977; Clarkson–Shor 1989 | only for a min of planes (concave) or one F_k (convex, a max): it builds each F_k, which is cheap already; S̃ is a min of convex functions, neither convex nor concave |
| Kinetic / sweep over heights (offset curves level by level) | the level sets of S̃ are offsets of the toe | contour offsetting, eg Held 1991 (pocket machining) | gives the level lines, not a surface with the slope guarantee (TERRAIN.md 5.9 rejected it as the core) |

## 3. What the library has

- `SlopeMap` (1a): an incremental planar map of convex faces with exact predicates, a loose quadtree over faces, insertion
  by clipping a face with a piece and an incremental max; `Simplify` merges same-label faces afterwards.
- `SlopeTerrainOverlay` (1b): a symbolic overlay of two triangulated surfaces with SoS; with min instead of the sign of a
  difference it is the merge step of a divide and conquer.
- `ExactTriangulation2`, `LooseQuadtree2d` / `TriangleQuadtree2d` (public API), `ExactSlopeKernel` (filter, then exact).

## 4. Ideas and their upper bounds

Each bound is the time left if the idea removed its whole share, measured above (an upper bound of the speedup, not
a forecast). Ordered by bound per effort.

| # | idea | removes | time left at best | bound | effort, risk |
|---|---|---|---|---|---|
| A | **One build, λ by re-realization.** Lowering the toe by λ lowers every plane by λ. In exact arithmetic the line of two planes, Nz_a·D_b − Nz_b·D_a, does not change when both D grow by λ·N_z, so the map is the same for every λ; only the lines with the cap plane change. Build the map once from the unlowered planes (cap at H, so the lowered surface ends at H − λ, never above it), and realize with z = exact height − λ, exactly rounded. A support failure then costs a re-realization and a check (about 0.5 s here), not a build | builds 2 and 3 | 14.7 + 2 × 0.5 ≈ 15.7 s | **≤ 2.8×** here, ≤ 2× in the 2-build cases | small: the kernel planes get an exact offset; the attempt loop changes; δ raises still need a build. To check before building: the undercut test and the planes that are dropped by the support check at construction must use the lowered points |
| B | **No `Simplify` pass**: insert a piece into a face as one convex region per label (its cells merged while they are made), so no split lines are left to merge afterwards | 26.3 s | 17.7 s (with A: 6.2 s) | ≤ 2.5× (with A ≤ 7×) | medium: the incremental max must produce merged cells directly; the failed-merge memo of 1a shows how much of `Simplify` is re-trying |
| C | **Faster exact stage**: fixed-width integer arithmetic instead of `BigInteger` dyadics, as the generated predicates of the library do (docs/design/FIXED_WIDTH_EXACT_POINTS.md), or a tighter filter (Shewchuk-style static bounds for the side test) so fewer calls reach it | ≤ 18.4 s | 25.5 s | ≤ 1.7× | medium: the degree of the side test (planes through 3 points, crossing of two plane lines) sets the width; a bounds review for every new filter constant |
| D | **Randomized insertion order** (Clarkson–Shor, Boissonnat–Dobrindt): insert the pieces in a random (seeded, so deterministic) order instead of along the toe | the faces a piece changes and later pieces undo | not measurable before: the bound is the gap between 5400 predicates per output triangle and an output-sensitive O(log n) per face | ≤ 30× in principle (100 predicates per triangle) | small to try; the result must stay independent of the order (ties keep the earlier label today, so the order changes which of two equal labels wins: needs a canonical tie rule first) |
| E | **Divide and conquer**: envelopes of the two halves of the toe, merged by the 1b overlay with a min | the faces far from a piece | as D | as D | large; the merge is the 1b overlay with min, a building block that exists |
| F | Exact curved R1 diagram (Fortune-type sweep over the toe segments, additively weighted) | the planes altogether | not comparable: another surface (curved cells, approximated afterwards) | — | rejected: bisectors are conics with high-degree exact predicates, varying slopes make it multiplicatively weighted (Θ(n²) worst case), and the plane-based guarantee of 1a would have to be rebuilt |

Proposal: A first (the largest bound for the smallest change, also the 1b repeated builds), then measure again and
decide between B, C and D with the new split. D needs a canonical tie rule before any order change, so the result stays
deterministic and independent of the input order.

## 5. The rest of 1c

- Volumes (5.7): on the 1b overlay, D linear on convex faces, integrated exactly as in `TerrainVolumeDifference`; cut,
  fill, areas, per piece; `TerrainVolumeDifference(Terrain, Composite)` as the independent check.
- `StatusKind` (5.6) for every status.
- Catch-line accuracy (5.5): per segment |∇D| and δ_xy = ε_D / |∇D| (local estimate), `IllConditionedCatchLine`,
  `TangentialContact`, `CatchLineWarningThreshold`, `RequiredCatchLineAccuracy`, `TerrainVerticalUncertainty`.
- km-scale benchmarks (section 8): procedural first; the Hambach data via `fetch_data.py` (nothing committed).
- From 1b: the band-only overlay, a local height limit instead of the terrain's global maximum, the per-triangle
  constraint checks of the composite (O(segments × points)).
