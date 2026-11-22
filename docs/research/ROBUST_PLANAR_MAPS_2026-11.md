# Robust Planar Maps for 3D Vectorization (Fuchs, Crane 2026) against the exact 2D code (2026-11-22)

Prior-art check and upper-bound measurement. No library change. The harness is the `planarmap` command of
[benchmarks/Meshing2Benchmark](../../benchmarks/Meshing2Benchmark/README.md) (`PlanarMapStress.cs`); the code it looks at is
`ConstrainedDelaunayMesh2` (exact noding, [MESHING2D_SPEC.md](../design/MESHING2D_SPEC.md)) and `SnapRounding2`
([MESHING2D_SNAP.md](../design/MESHING2D_SNAP.md)).

**Paper.** R. Fuchs, K. Crane, *Robust Planar Maps for 3D Vectorization*, ACM Trans. Graph. 45(4), Article 106 (SIGGRAPH 2026, Best
Paper), DOI 10.1145/3811357. Read in full (19 pages, the author's PDF).

**Question.** Does the paper's planar-map construction (its spatial hierarchy, its handling of near-degenerate crossings) offer something
the library's exact 2D arrangements and snap rounding lack, in robustness, speed or guarantees?

**Answer in short.**
- *The method: no-go.* It is a floating-point method with a tolerance, and it says so ("Non-goal: Geometric Exactness"). Its robustness
  is a map that is topologically valid although the geometry is approximate: one "virtual" vertex stands for all crossings in a cell,
  and regions may overlap or leave gaps of area up to eps². The library decides every crossing exactly and, with a snap tolerance, gives
  the guarantees of snap rounding (no new crossings, a separation of half a pixel, bounded movement). Every robustness property the paper
  claims we have in a stronger form. Its speed claims are against CGAL's sweep-line arrangements only.
- *It must not be built anyway.* No code is released, and the project page states "Patent pending. A U.S. provisional patent
  application has been filed for this technology." (section 1.3). Study only.
- *What the paper has and we do not* is outside the library's scope today: arrangements of curves (Bézier, ellipses) without
  tessellation, occlusion by ray tracing, merged layers for transparency.
- *The stress inputs found four things in our code* (section 4), none of which the paper's method would fix:
  1. **A limit:** `ConstrainedDelaunayMesh2` returns `Failed / NodingLimit` on a valid input with heavy collinear overlap (from between 125 and
     187 overlapping intervals per line). It fails cleanly, with no wrong result.
  2. **Speed, automatic tolerance:** a bundle of near-parallel segments is superlinear in its output (0.8 s, 9.2 s, 130 s for 4x the
     output each time); about 80 % of the time is the edge recovery of the triangulation, not the search for crossings.
  3. **Speed, snap rounding:** on long segments with many crossings the snap rounding is nearly all of the time and 30 times or more
     the cost of the exact noding of the same input (16 s against 0.5 s on 500 random segments).
  4. **A disagreement of mesher and oracle** on the marker of some subsegments under snap rounding, when crossing segments carry
     different markers (`SubsegmentMarker`; the cause is not isolated). Every geometric check passes.
- *Options for the owner:* section 5. Leaning: close the paper as "no gap"; items 1 and 4 as issues with the diagnosis below; items 2
  and 3 only when a workload needs them (the GIS inputs have short segments and do not hit them).

## 1. The paper

### 1.1 What it does

Input: closed or open parametric curves in the image plane (projected boundaries of 3D surface patches), a tolerance eps, and two black
boxes: curve against line segment, and the first patch a ray hits. Output: regions bounded by pieces of the input curves, each with the
identity of its visible patch.

1. **Intersection detection (its section 2).** A binary space partition of the viewport is built top down. A cell that meets more than
   one curve is split, alternately in x and y, until it meets at most one curve or reaches the depth d_max = log2(A0 / eps²). A leaf with
   more than one curve is an *intersection node*; it becomes one *virtual intersection point*, which may stand for many geometric
   crossings. Only curve-line intersections are ever computed, so mixed curve types need no pairwise routines.
2. **Where to split (2.1).** A split line does not go through the midpoint of a cell edge, because a curve may cross arbitrarily close
   to it. It goes through the midpoint of the gap between the two curve crossings of that edge that enclose the edge's midpoint. The
   cells are therefore convex polygons, not boxes: this is the only reason for a BSP instead of a quadtree.
3. **Separating lines (2.3, optional).** If the convex hulls of all curves in a cell are pairwise separable, the cell is split along a
   separating line (separating axis test), also beyond d_max. This avoids deep trees along near-parallel curves and the "zipper", where
   two lines meeting at a small angle are merged into one over a stretch. Applied to every separable pair it would undo the
   simplification by depth, so it is applied only when all pairs are separable.
4. **Region identification (3).** Vertices are the virtual points and the curve ends; edges are the curve pieces between them, as a
   halfedge mesh. The cyclic order of the edges at a virtual vertex is not computed from tangents or a bounding circle (both give
   inconsistent orders in floating point, its Figures 12, 13 and 31). It is read off the walk around the boundary of the cell, where
   every cell edge has one canonical order of its crossings that both neighbours share. Two-sided faces inside one cell are removed by
   keeping one edge per cell edge.
5. **Region geometry (3.4).** A region is a closed loop of input curve pieces; inside an intersection node consecutive pieces are joined
   by a straight connector of length about eps. The ends are deliberately not merged ("nudged"), so loops of neighbouring regions do not
   fit exactly: gaps or overlaps of area up to eps².
6. **Gaps in the input (3.5).** A small circle (1/1000 of the image width) is added around every open curve end before the tree is
   built; the later merge of regions with the same patch removes what the circles cut off.
7. **Occlusion and layers (4).** A ray from a point inside each face gives its visible patch; faces with the same patch are merged and
   the edge between them becomes a hidden line. With transparency every face keeps the stack of patches the ray hits, and stacks of
   neighbouring faces are merged greedily while their order stays acyclic.

### 1.2 What it claims, and what it does not

- **Robustness:** a valid partition on inputs where CGAL's arrangements crash, do not end, or lose regions; correct coverage against a
  rasterized reference on 100 arrangements where tangent or loop ordering fail. No proof is given; the guarantee is argued from the
  construction (cells partition the plane, orders are shared).
- **Speed:** median 62x and 24x faster than CGAL's polyline and Bézier sweep-line arrangements on layered icons (500 examples of 3 to 5
  icons, 100 of 10 to 25); its own failure rate there is 3 % (15 of 18 out of memory, 3 timeouts). No absolute time per crossing or per
  output element is given for the 2D sets. In 3D: 14 486 NURBS patches in about 50 s on a laptop, at depth 15 without separating lines;
  by the paper's formula for d_max that depth is eps = 2^-7.5 of the viewport's side, about 1/181. Separating lines gain 1.5 to 2x.
- **Stated limits:** not exact; curves that overlap on an interval make the tree grow exponentially with the depth ("We do not claim to
  handle such cases gracefully"; the likely cause of its 3 % failures); self-intersecting curves must be split beforehand; the gap
  radius is a fixed constant; the construction is serial.
- **Baselines:** CGAL only, and vector editors used against their purpose. Snap rounding is not cited or compared (no Hobby, Guibas and
  Marimont, Halperin and Packer, de Berg et al., Hershberger); Clipper2 and GEOS are named and set aside.

### 1.3 Licence and patent

Checked before anything else, 2026-11-22: the project page (https://www.cs.cmu.edu/~kmcrane/Projects/RobustPlanarMaps/) links the PDF
only. No implementation is released; the paper mentions a supplemental dataset (curves, cameras, meshes) and no code. The page has a
section "IP Status" that reads: "Patent pending. A U.S. provisional patent application has been filed for this technology."

So: paper only, no code was read (there is none), and the core idea (the spatial hierarchy as the planar map) is not to be implemented
without a patent check by the owner. This note takes no construction from the paper into the library or the benchmark; the harness
measures our code with our own yardsticks.

## 2. Paper component against ours

| Paper | Ours | Gap |
|---|---|---|
| BSP of the viewport finds all crossings to a tolerance eps; a leaf with several curves is one virtual vertex (2) | Automatic tolerance: every crossing exactly, in integers, rounded to the nearest double (`ConstrainedDelaunayMesh2`, noding by walking the triangulation, `DelaunayKernel2.Constraints`). Explicit tolerance: hot pixels of snap rounding (`SnapRounding2`, Hobby 1999) | None. A hot pixel is the paper's virtual vertex with a defined position (the pixel centre) and proven properties |
| Split lines placed away from the curve crossings of a cell edge, to avoid near-degenerate cells in floating point (2.1) | Not needed: exact predicates (`ExactPredicates.Orient2d`, in-circle) and half-open pixels decide every degenerate case | None |
| Separating lines keep near-parallel curves apart without deep trees and avoid the zipper (2.3) | Automatic tolerance: near-parallel segments are resolved exactly, however small the angle. Explicit tolerance: what is closer than the pixel merges, by the spec (O11, the band of MESHING2D_SNAP.md section 3, item 4) | None in the result. In speed see finding 2: our cost on such a bundle is superlinear |
| Order of the edges at a vertex from the shared order on cell edges, not from angles (3.3) | Faces are the triangles' connected cells between constraints; adjacency is the triangulation's, built with exact predicates. No angular sort anywhere | None |
| Faces as loops of the original curves, joined by connectors; gaps and overlaps up to eps² (3.4) | A conforming triangulation: the faces fit exactly, on straight segments | None for segments. Curves: see the last row |
| Curve ends that almost meet: gap circles of a fixed radius (3.5) | Automatic tolerance: merge within 2^-41 of the magnitude. Explicit tolerance: an end within the tolerance of a segment becomes its vertex, with the documented band (same item) and the half-pixel separation after SRR's restore (Belussi et al. 2016) | None; ours has a stated bound, the paper's a constant |
| Output-sensitive simplification by lowering the depth (1.1, 2) | `SnapTolerance` | None |
| Curves overlapping on an interval: exponential growth, unhandled (5.3) | Collinear overlaps are noded exactly and the piece carries all its sources (`SubsegmentSources`, D7) | None in kind. But see finding 1: beyond a size our step limit reports `NodingLimit` |
| Region identity by ray tracing, merge of equal neighbours, hidden lines (4.1 to 4.3) | Regions by rings and seeds, even-odd domain (`m2.regions`, D5); `TerrainSlopeOverlay` overlays two triangulations with exact `Orient2d`. No visibility | Out of scope: 3D vectorization is not a roadmap item |
| Layers with transparency, acyclic local order (4.4) | Nothing | Out of scope |
| Mixed curve types (Bézier, ellipse, polyline) as input and output, no tessellation (1, Appendix A, B) | The exact 2D code takes straight segments; `curve/` has parametric curves and `PlanarComplex` nests their sampled loops in doubles (upstream, tolerance-based); `Arrangement2d` is the upstream double-precision arrangement, documented as without guarantee | A difference in scope, not in robustness. An exact arrangement of curves would need algebraic predicates; the paper avoids them by not being exact |
| Serial; a parallel build is suggested (5.2) | Serial noding | None |

## 3. Measurement

### 3.1 Method

`Meshing2Benchmark planarmap` builds procedural segment sets, runs `ConstrainedDelaunayMesh2` on them at the automatic tolerance (0)
and with `SnapTolerance` 2^-20 and 2^-10 (the coordinates are in the unit square), and reports the median of three runs after a warm-up
(one run where a row takes tens of seconds, as stated), the allocation of one run, and the oracle's verdict (`--judge`, M2 default
expectations) on the cases up to 40 000 used output vertices. Every family of a run is warmed up three times at a small size on every
path. Rows below about half a second still vary by tens of percent between runs. Linux container, 4 cores, .NET 10.0.15, nothing else
running.

*Used vertices.* The mesh keeps every input point as a vertex, also the ones merged into another (`InputToVertex`). The tables count
the vertices that a triangle or a subsegment uses.

Families (n segments; every segment has its own two end points, so nothing is connected in the input, as in the paper's "curve soup"):

| family | what | degenerate how |
|---|---|---|
| `soup` | random segments in the unit square | not at all: the reference |
| `bundle` | n segments across the square in a band of height n 2^-30, the right ends a random permutation of the left ones | about n²/4 crossings at angles of 2^-30 to n 2^-30 |
| `pencil-exact`, `pencil-near` | n segments through (1/2, 1/2); `near`: every end moved by up to 2^-48 | all n²/2 pairs cross in one point, or within a few ulps of it |
| `junction-ulp`, `junction-tiny` | a square grid; every grid edge is its own segment whose ends miss the node by up to 2^-48 or 2^-30 | gaps, overshoots and slivers at every node; below and above the automatic tolerance |
| `overlap-exact`, `overlap-near` | 32 lines, each with n/32 random intervals; `near`: ends moved across the line by up to 2^-48 | every stretch is covered many times: the paper's failure mode |
| `tchain` | one slanted segment and n short ones starting on it, the start computed in doubles | a long chain of T-junctions, each up to an ulp off |

Two yardsticks bound what any other method could gain:
- `detect`: only the pairs of segments that meet, from a uniform grid over the boxes (cell: the mean box extent, so on the soup, the
  bundle and the pencils it tests all pairs) and the filtered exact `Orient2d`; no map is built.
  Every arrangement method does at least this much, exact or not, unless it never looks at pairs.
- `delaunay`: `DelaunayMesh2` on the output's used vertices without constraints: the part of our time that buys a triangulated output.

### 3.2 Automatic tolerance

`perf/planarmap-auto-net10.txt` (small and large), `perf/planarmap-scaling-net10.txt` (the series).

| family | segments | crossing pairs | used vertices | time | µs per used vertex | `detect` | `delaunay` | verdict |
|---|---|---|---|---|---|---|---|---|
| soup | 250 | 6 944 | 7 444 | 0.06 s | 8.5 | 1 ms | 8 ms | VALID |
| soup | 500 | 27 238 | 28 238 | 0.48 s | 17.0 | 6 ms | 39 ms | VALID |
| soup | 2 000 | 464 454 | 468 454 | 7.43 s | 15.9 | 69 ms | 717 ms | not judged |
| bundle | 125 | 3 856 | 4 016 | 0.19 s | 48 | 0.3 ms | 11 ms | not judged |
| bundle | 250 | 14 371 | 14 654 | 0.79 s | 54 | 1 ms | 40 ms | VALID |
| bundle | 500 | 62 555 | 62 987 | 9.2 s | 146 | 3 ms | 228 ms | not judged |
| bundle | 1 000 | 249 766 | 250 479 | 130 s | 518 | 18 ms | 1 701 ms | not judged |
| pencil-exact | 1 000 | 499 500 | 2 001 | 0.24 s | 119 | 19 ms | 2 ms | VALID |
| pencil-exact | 4 000 | 7 998 000 | 8 001 | 2.94 s | 368 | 175 ms | 7 ms | not judged |
| pencil-exact | 16 000 | 127 992 000 | 32 001 | 51.9 s | 1 623 | 3.4 s | 30 ms | VALID |
| pencil-near | 4 000 | 7 998 000 | 8 190 | 1.98 s | 242 | 221 ms | 8 ms | not judged |
| pencil-near | 16 000 | 127 992 000 | 32 327 | 27.0 s | 834 | 3.2 s | 44 ms | VALID |
| junction-ulp | 9 940 | 5 004 (+6 contacts) | 5 041 | 0.05 s | 10.3 | 1 ms | 7 ms | VALID |
| junction-ulp | 99 904 | 49 901 (+61) | 50 176 | 0.59 s | 11.8 | 14 ms | 61 ms | not judged |
| junction-tiny | 9 940 | 4 960 | 24 835 | 0.14 s | 5.6 | 2 ms | 41 ms | VALID |
| junction-tiny | 99 904 | 49 702 | 249 455 | 1.42 s | 5.7 | 13 ms | 392 ms | not judged |
| overlap-exact | 1 984 | 114 414 (+41 442) | 4 223 | 0.08 s | 19.8 | 14 ms | 7 ms | VALID |
| overlap-exact | 4 000 | 458 412 (+167 282) | 8 255 | 0.21 s | 26 | 57 ms | 10 ms | VALID |
| overlap-exact | 5 984 | 1 027 536 (+375 352) | none | 0.30 s | | 131 ms | | **Failed: NodingLimit** (195 vertices made in 1 261 409 steps) |
| overlap-exact | 16 000 | 7 091 539 (+2 656 519) | none | 1.06 s | | 975 ms | | **Failed: NodingLimit** (25 vertices, 3 173 601 steps) |
| overlap-near | 1 984 | 128 267 (+12) | 4 223 | 0.09 s | 22 | 12 ms | 5 ms | VALID |
| overlap-near | 16 000 | 7 994 530 (+724) | none | 0.74 s | | 679 ms | | **Failed: NodingLimit** (25 vertices, 3 173 601 steps) |
| tchain | 10 001 | 4 992 | 20 002 | 0.12 s | 6.2 | 19 ms | 47 ms | VALID |
| tchain | 100 001 | 50 055 | 200 002 | 1.36 s | 6.8 | (1.4 s: the yardstick's cell, the mean segment extent, is far wider than the spacing of the short segments) | 488 ms | not judged |

Reading:
- **Robustness.** Every result that was judged is valid, including the families made of the paper's hard cases (crossings at angles of
  2^-30, 128 million pairs through one point, ends an ulp apart). The one failure is a status, not a wrong map (finding 1).
- **Generic cost.** The soup is about linear in its output, 9 to 17 µs per used vertex (16 to 17 at the two larger sizes); `delaunay`
  is 8 to 10 % of it and `detect` about 1 %. So an arrangement that is not triangulated could in principle be several times faster on dense random crossings. That headroom
  is the price of the conforming constrained Delaunay output and of the known allocation of the work list (2.1 GB at 468 000 vertices,
  "not tuned yet" in the benchmark README), not something a tolerance-based hierarchy would give us.
- **Near-degenerate inputs are not free.** Per used vertex `junction-ulp` costs about what the soup costs (10 to 12 µs), `junction-tiny`
  and the T-chain less (6 to 7 µs). The
  bundle and the pencils cost more and grow faster than their output (the bundle: finding 4.2; the pencils: n² pairs through one point,
  option B5). `pencil-exact` takes about twice as long as `pencil-near` at 16 000; not examined.

### 3.3 With snap rounding

`perf/planarmap-snap-net10.txt`; the soup at 125 and 250 and the bundle at 62 and 125 from `planarmap-scaling-net10.txt` (median of
three), the verdicts of the soup at 125 and 250 from `planarmap-markers-net10.txt`. Otherwise one run per row. `snap` is
`SnapRounding2.Apply` alone in a separate single run, so it can exceed the whole run by the noise between runs.

| family | segments | tolerance | used vertices | time | `snap` alone | the same input at tolerance 0 | verdict |
|---|---|---|---|---|---|---|---|
| soup | 125 | 2^-20 | 2 002 | 0.29 s | 0.08 s | 0.06 s | VALID |
| soup | 250 | 2^-20 | 7 444 | 0.69 s | 0.70 s | 0.06 s | SubsegmentMarker x1 |
| soup | 500 | 2^-20 | 28 232 | 16.2 s | 14.5 s | 0.48 s | SubsegmentMarker x4 |
| soup | 500 | 2^-10 | 24 081 | 29.3 s | 25.9 s | 0.48 s | not judged |
| bundle | 62 | 2^-20 | none (Empty) | 0.13 s | 0.14 s | | not judged |
| bundle | 125 | 2^-20 | none (Empty) | 2.25 s | 2.70 s | 0.19 s | not judged |
| bundle | 250 | 2^-20 | none (Empty) | 43.1 s | 45.4 s | 0.79 s | VALID |
| bundle | 250 | 2^-10 | none (Empty) | 1.22 s | 1.21 s | 0.79 s | not judged |
| pencil-exact | 1 000 | 2^-20 | 2 001 | 1.30 s | 0.93 s | 0.24 s | VALID |
| pencil-near | 1 000 | 2^-20 | 2 001 | 1.70 s | 1.68 s | 0.17 s | VALID |
| junction-ulp | 9 940 | 2^-20 | 5 041 | 0.30 s | 0.19 s | 0.05 s | VALID |
| junction-tiny | 9 940 | 2^-20 | 5 041 | 0.42 s | 0.20 s | 0.14 s | VALID |
| overlap-exact | 1 984 | 2^-20 | 4 223 | 2.21 s | 1.95 s | 0.08 s | VALID |
| overlap-near | 1 984 | 2^-20 | 18 618 | 21.1 s | 20.8 s | 0.09 s | VALID |
| overlap-near | 1 984 | 2^-10 | 12 773 | 13.7 s | 14.7 s | 0.09 s | not judged |
| tchain | 10 001 | 2^-20 | 20 002 | 4.13 s | 3.30 s | 0.12 s | VALID |
| tchain | 10 001 | 2^-10 | 5 037 | 3.49 s | 3.15 s | 0.12 s | not judged |

Reading:
- **The snap rounding does what the paper's simplification does, with a defined result.** The bundle (a band 2^-22 high at n = 250)
  collapses onto one line at both tolerances and the result is `Empty` (no area); the row at 2^-20 is judged valid. The junctions close: 5 041 used vertices
  (the 71² nodes of the grid) and 9 800 triangles at both perturbation sizes. `overlap-near` keeps more vertices than at tolerance 0 (18 618 against 4 223): the
  automatic tolerance merges the near-collinear ends, while snap rounding makes every pixel with a crossing hot (a reading of the
  two rules, not checked on the output).
- **Its cost is the finding** (4.3): on the soup, the bundle and the overlaps the snap rounding alone takes about as long as the whole
  run, 30 times or more what the same input takes at tolerance 0.

## 4. Findings in our code

None of these is a gap against the paper. They are what its class of input (long curves, many crossings, near-degenerate bundles) shows
in ours.

### 4.1 `NodingLimit` on heavy collinear overlap (a limit; valid input, status `Failed`)

`overlap-exact` at tolerance 0: 1 984 and 4 000 segments succeed and are judged valid; 5 984 (187 intervals on each of 32 lines), 8 000
and 16 000 fail with `Triangulation2FailureKind.NodingLimit` after 0.3 to 1.1 s ("noding did not settle: 195 vertices made in
1 261 409 steps" at 5 984); `overlap-near` the same at 16 000. The oracle reports the status as
`StatusInconsistent` (a required result is missing).

Diagnosis (read from the code; the failure detail confirms which limit trips, the per-piece count is not probed):
`Builder.InsertConstraints` stops when `steps > 64 (m + n + made) + 100 000`, with m
sources, n points and `made` new vertices. The comment calls both limits runaway guards and argues from crossings: each crossing makes at
most one vertex. Collinear overlap makes almost no vertex (195 at 5 984 segments, where the budget
64 (5 984 + 11 968 + 195) + 100 000 = 1 261 408 is exactly the reported step count): a source is split at every end point of every other interval on its line, so
k intervals on a line take about k² steps while the limit grows with k. With 32 lines the budget is 64 · 3 · 32 k + 100 000, linear in k,
against a number of pieces of the order of 32 k²; the two meet at a k of a few hundred, the order of the measured threshold (between
125 and 187 per line). The exact step count per piece was not probed.

Nothing wrong is returned, and inputs of this kind (hundreds of constraints on one line, each overlapping most others) are not what the
GIS gates contain. But the documented meaning of `NodingLimit` is "did not settle", and this noding would settle.

### 4.2 Near-parallel bundle: superlinear edge recovery (speed, automatic tolerance)

`bundle` at 125 / 250 / 500 / 1 000 segments (see `perf/planarmap-scaling-net10.txt`): the output grows about 4x per step, the time 4x, then
13x and 14x (0.19 s, 0.71 to 0.79 s, 9.2 s, 130 s).
A profile of n = 500 (dotnet-trace, sampled, thread time; one run under the tracer): of the mesher's 9.5 s, `InsertPiece` is 8.3 s and
`DelaunayKernel2.RecoverEdge` inside it 7.7 s (about 80 %); `Cross` (the exact crossing) 0.6 s, `Covering` 0.8 s. `detect` finds all
62 555 crossing pairs in 7 ms.

So the search for crossings is not the cost; making each piece an edge of the triangulation is. Within `RecoverEdge` the Lawson pass
(`RestoreDelaunay`) is 0.2 s and the adaptive `orient2d` 0.3 s, so the time is in the loop of Sloan's flips itself (finding the edge,
the convexity test, the flip, and re-queuing an edge whose quadrilateral is not yet convex).

Hypothesis, not counted: in a band of height 2^-20 every triangle is a sliver across the band, a piece crosses many edges, and the loop
re-queues most of them several times (its own guard allows 64 (k + 4)² steps for k crossed edges). The number of crossed edges per piece
and of flips was not measured; that count comes before any work on it. 130 s on one input is above the 20 s rule of CLAUDE.md, so this
gets an issue.

### 4.3 Snap rounding on long segments with many hot pixels (speed, explicit tolerance)

Soup of 500 segments at 2^-20: 14.5 s in `SnapRounding2` alone (16.2 s the whole run), against 0.48 s for the exact noding of the same input with nearly the same
output (28 232 against 28 238 vertices). A profile of n = 250 (one run): of `SnapRounding2.Compute`, `RouteAll` is 60 %, `Critical`
30 %, `AddCrossings` 6 %, `Build` 4 %.

A second profile, `bundle` 125 at 2^-20 (one run): `Critical` 63 %, `RouteAll` 30 %, `Build` 6 %.

Diagnosis (read from the code, consistent with both profiles; no counts): the spatial buckets have the side of the mean constraint length
(in pixels, at least 4, a power of two). That suits GIS input, where constraints are short. Here a constraint is as long as the input, so
there are a handful of buckets and they index nothing:
- `RouteAll` tests every segment against every hot pixel of its buckets inside its bounding box, with exact `BigInteger` arithmetic:
  on the soup about segments times hot pixels per round.
- `Critical` scans, for every edge of every output chain, all hot pixels of the edge's buckets. On the bundle each of the 250 chains
  runs through thousands of hot pixels that all lie in one or two buckets, so this is about chains times pixels per chain times pixels
  per bucket; that is why the bundle costs more than the soup although its output is a line.

The soup grows from 125 to 250 to 500 segments as given in section 3.3. At the coarser pixel 2^-10 it is not cheaper (three rounds,
16.9 GB allocated).

Estimate of what an index at the density of the hot pixels could gain, with the result bit for bit (only which pixels are tested
changes, not the decisions): on the rows above 10 s the snap rounding alone takes about as long as the whole run (14.5 of 16.2 s on soup 500,
45.4 against 43.1 s on bundle 250, 20.8 of 21.1 s on overlap-near; two separate single runs each, so the share is known only as
"about 90 % or more"). If the routing cost about what the exact noding of the same input costs, the soup at 500 segments would go from 16.2 s to
about 1 s, some 15 to 20x. That floor is an assumption, not a measurement. The Herne runs of the benchmark README are not this regime (their
constraints are short and refinement dominates); that is a reading of their inputs, not measured here.

### 4.4 Subsegment markers under snap rounding (mesher against oracle; cause not isolated)

Soup with markers (segment i has marker i mod 7): at 2^-10 the oracle reports `SubsegmentMarker` on 5 subsegments at 40 segments, 114
at 125 and 200 at 250; at 2^-20 on none at 40 and 125, 1 at 250 (`perf/planarmap-markers-net10.txt`) and 4 at 500
(`planarmap-snap-net10.txt`). The first violation of each row has the form "has marker k, expected 0". No other kind of
violation occurs: coverage, conformity, the Delaunay property, the tube and the domain all pass. At tolerance 0 the same inputs are
valid (rows in the same file).

Hypothesis (from reading the two rules; not verified on a single piece): the oracle expects marker 0 when more than one source with different non-zero markers lies
within the tube (a distance of `SnapTolerance`) of both ends of a subsegment (D7). The mesher gives a piece the sources whose chains run
through both of its hot pixels, and a segment is routed through a pixel only if it meets the pixel's half-open square (half-width
tolerance / 2). If that is the cause, then next to a crossing at a shallow angle a short piece of one segment can lie within the tolerance of the other segment
without the other segment's chain using its pixels, and the two rules differ there. Whether the oracle's rule is too wide or the mesher's source list too narrow is a
question of the spec (R8, D7) and is not settled here. The existing gates do not have crossing segments with different markers at this
density.

## 5. Go / no-go and options

**The paper's method: no-go** (leaning, and the coordinator's condition for any build: none, patent pending). There is no gap in
robustness or guarantees; the gap in scope (curves, visibility, layers) is not on the roadmap.

How others solve the four findings (prior-art check, papers and documentation only):
- *Edge insertion in a triangulation crossing many edges:* J. R. Shewchuk, B. C. Brown, *Fast segment insertion and incremental
  construction of constrained Delaunay triangulations*, CGTA 48(8), 2015: the cavity of the crossed triangles is retriangulated in
  expected linear time instead of flipping. Or the arrangement is noded first, without a triangulation (a sweep, Bentley and Ottmann
  1979; monotone chains with an index, as I recall the JTS documentation of its noders; not re-read for this note), and triangulated once.
- *Snap rounding in time sensitive to the output:* M. Goodrich, L. Guibas, J. Hershberger, P. Tanenbaum, SoCG 1997; M. de Berg,
  D. Halperin, M. Overmars, CGTA 2007; J. Hershberger, CGTA 2013 (all already cited in MESHING2D_SNAP.md, which chose "a simple sweep or
  grid" for the input sizes of the gates). JTS's snap-rounding noder indexes the hot pixels in a k-d tree (its documentation, from memory; not re-read).
- *Collinear overlaps:* overlay libraries merge coincident pieces per supporting line before noding the rest (JTS documentation,
  "dissolving" of segment strings; from memory). Nothing found on step limits; that part is ours.
- *Markers of merged pieces:* nothing found beyond D7's own check ("None found").

| option | what | gain | cost and risk | leaning |
|---|---|---|---|---|
| A | Close the paper: no build, this note as the record | | none | **yes** |
| B1 | Issue for 4.1 (`NodingLimit` on collinear overlap), with the repro command | a valid input class stops failing | a probe to confirm the step count, then the limit counts what collinear splits need; `comp_geom`, not exact code; tests with a procedural overlap | **yes, as an issue**; small |
| B2 | Issue for 4.4 (marker rule of oracle and mesher under snap), with the 40-segment repro | one rule, checked | a spec reading (R8, D7) before any code; the answer may be an oracle change, which is a gate file | **yes, as an issue**; decision on the rule is the owner's |
| B3 | Speed spike for 4.3: hot pixels indexed at their own density, so routing and the critical-pixel scan visit only pixels near the segment; bit-identical output | an estimate of 15 to 20x on dense long-segment inputs with a snap tolerance; nothing expected on GIS tiles | internal to `SnapRounding2`; the determinism proof names its orders (R11), so the review must show the visiting order does not enter the result | only when a workload needs it; the issue of the 20 s rule records it |
| B4 | Speed spike for 4.2: first the count of crossed edges and flips; if the hypothesis holds, edge recovery by cavity retriangulation (Shewchuk and Brown 2015) | if it holds, the bundle towards the soup's rate (130 s to seconds at n = 1 000); an estimate | changes the core of `DelaunayKernel2`; the constrained Delaunay result is unique up to the tie rule, so golden rows should hold, but that has to be shown | not now; the issue of the 20 s rule records it |
| B5 | Pencils (n² pairs through one point, 27 to 52 s at 16 000) | | inherent to any pairwise noding; `detect` alone is 3.2 to 3.4 s | no |

Six rows are above 20 s on one input: `bundle` 1 000 at tolerance 0 (130 s), `pencil-exact` and `pencil-near` 16 000 (52 s, 27 s), and
with snap rounding `bundle` 250 at 2^-20 (43 s), `overlap-near` 1 984 at 2^-20 (21 s) and `soup` 500 at 2^-10 (29 s). By the test-time
rule of CLAUDE.md each is a potential defect and gets an issue with a diagnosis: one for 4.2, one for 4.3, the pencils recorded there as
n² pairs. The issues are not filed yet; they are proposed to the coordinator with this note.

## 6. Commands

```
dotnet build benchmarks/Meshing2Benchmark -c Release
B=benchmarks/Meshing2Benchmark/bin/Release/net10.0/Meshing2Benchmark.dll; P=benchmarks/Meshing2Benchmark/perf; T=9.5367431640625e-07
dotnet $B planarmap --tolerances 0 --repeats 3 --judge                                         > $P/planarmap-auto-net10.txt
dotnet $B planarmap --sizes small --tolerances $T --repeats 1 --judge                          > $P/planarmap-snap-net10.txt
dotnet $B planarmap --kinds soup,bundle,tchain,overlap-near --sizes small --tolerances 0.0009765625 --repeats 1      (appended)
dotnet $B planarmap --kinds soup --sizes 40,125,250 --tolerances 0,0.0009765625,$T --repeats 1 --judge   > $P/planarmap-markers-net10.txt
dotnet $B planarmap --kinds bundle --sizes 125,250,500 --tolerances 0 --repeats 3              > $P/planarmap-scaling-net10.txt
dotnet $B planarmap --kinds overlap-exact --sizes 2000,4000,6000,8000 --tolerances 0 --repeats 3 --judge             (appended)
dotnet $B planarmap --kinds pencil-exact,pencil-near --sizes 1000,4000 --tolerances 0 --repeats 3                    (appended)
dotnet $B planarmap --kinds soup --sizes 125,250 --tolerances 0,$T --repeats 3                                       (appended)
dotnet $B planarmap --kinds bundle --sizes 62,125 --tolerances $T --repeats 3                                        (appended)
```

All four files are from the committed harness. The oracle's time dominates the judged snap rows (minutes for soup 500 and
overlap-near). The profiles (dotnet-trace 10.0, `--format Speedscope`, one run each of `bundle 500` at tolerance 0, `soup 250` and
`bundle 125` at 2^-20) are not committed; their shares are inclusive thread time and are the only numbers of this note without raw
output in `perf/`. The command has no tests: it is a measurement, not a gate, and changes no library code.
