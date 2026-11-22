# Contouring spike: sharp features from distance grids, walls thinner than a cell, and a subdivision certificate

Date: 2026-11-22 (Berlin). Research spike (owner go 2026-11-22): a note and a measurement prototype
([benchmarks/ContouringSubgridProbe](../../benchmarks/ContouringSubgridProbe/README.md)). No library code changed.
Covers plan #1297 (two SIGGRAPH 2026 papers), plan #1289 (contouring certificate, thin features and options B / C)
and item 5a of [UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md) (the subdivision variant
of excursion-aware isotopy certificates; the SOS variant 5b is out of scope).

Question: what sharp-feature recovery and thin-feature preservation is possible on procedural distance inputs,
against the analytic surface; and can a subdivision isotopy certificate cover the thin cases cheaply?

## 1. Answer in short

| Track | Upper bound measured | Verdict |
|---|---|---|
| A. Sharp features from a grid of distances (Carrera et al. 2026) | exact Hermite data on the same sign grid, 48 cells: mean feature distance 0.204 → 0.016 cells (13x), edge Chamfer 0.257 → 0.037 (7x), feature maximum 0.63 → 0.39 (1.6x); all runs clean | a large bound, but only for inputs that are a grid and nothing else: the library's own pipelines already have the oracle. A local grid-only estimator reaches about 30 % of it. Not now |
| B. Walls thinner than a cell (Baktash et al. 2026) | restricted prototype, 32 cells: a rotated plate 0.25 cells thick comes out closed, manifold, with the right topology in 3 of 3 runs (marching cubes: fragments; coverage 0.14 → 0.92). For sheet fields the two-level form needs no evaluation beyond the grid nodes and gave the same topology in all 30 shell and gyroid runs | the strongest lever. Build as an opt-in generator, two-level form first |
| C. Subdivision certificate for thin walls (Plantinga and Vegter; Burr and Byrd) | gyroid wall of 0.25 cells: as one function 16.2 million cells, 4x per halving of the thickness; as two levels of one function 19 440 cells at any thickness (mid-surface alone: 18 208) | yes for sheet and shell fields, as the class S certificate (stage 3) applied per level; no for one non-smooth field (a box plate) |

Sections 5 to 7 hold the numbers, section 8 the options with effort and risk, section 9 the leaning.

## 2. The papers (paper text only, in our words)

Licences: both arXiv papers were read as text (2604.00157v2 and 2606.00454v1; the later sections through a summary
of the HTML, so parameter values below are as reported there). No code was opened. The code of paper A builds on
libigl (MPL, on our banned list) and no software licence is stated for either paper, so both stay paper only.

### 2.1 Dual Contouring of Signed Distance Data (Carrera, Wang, Batty, Stein, Sellán; SIGGRAPH 2026)

Input: one exact signed distance per node of a uniform grid, nothing else. Each sample is read as a ball that is
empty and touches the surface. The method starts with interpolated edge crossings and trilinear gradients and then
alternates: build the dual contouring mesh; assign every grid sample to the cell that holds its closest point on that
mesh; per cell, move the vertex so that the local mesh touches the balls of its samples (a least-squares problem,
linearised around the closest point, solved like the QEF of Ju et al. with a proximal term and a small weight on the
Hermite planes); re-estimate the Hermite data of each crossing edge from a plane through the four cell vertices
around it, blended with the previous estimate. Up to 100 outer and 100 inner iterations are reported, and random
batches of samples.

What it needs and gives: true distances (the eikonal property); noise of 0.5 to 1 % makes it worse than the
empty-sphere method of Kohlbrenner and Alexa (2025). Vertices may leave their cells, which the authors name as a
cause of self-intersections and flipped faces; manifold dual contouring is left as future work. No timings of the
method itself are given. The sampling is random, so the paper's procedure is not deterministic as written.

### 2.2 Subgrid Marching Tetrahedra (Baktash, Gillespie, Crane; ACM TOG 2026)

Input: a tetrahedral grid (each cube split into five tetrahedra) and, per edge, *all* its crossings with the
surface, as a count and positions; no signs, no function. The counts on the six edges of a tetrahedron are read as
normal coordinates (as in normal surface theory): on each face the crossings are joined to arcs around the corners;
where a face violates the triangle inequality of its counts, the surplus crossings of one edge are joined to each
other ("scoops"). The arcs of the four faces close to loops, and each loop is filled (triangle, quad, fans for
octagons and for loops that run along an edge). Each tetrahedron is independent, the work is linear in the number
of crossings.

Guarantees stated: manifold; closed and orientable if every face has an even sum and the boundary of the grid is
not crossed; the primal reconstruction is free of self-intersections. Not guaranteed, in the authors' words: the
same topology as the surface; the result only has the same number of crossings on every edge. A feature that no
grid edge crosses stays invisible. The crossings come from a one-dimensional root finder per edge (sphere tracing
for distances, interval roots, or sampling, which can miss crossings). A dual variant with one vertex per loop has
no intersection guarantee.

### 2.3 Certified simultaneous isotopic approximation of curves via subdivision (Burr, Byrd; arXiv 2302.04908)

Planar curves only (polynomials, smooth, transverse intersections). It builds on the two cell tests of Plantinga
and Vegter (2004): C0, the interval of f over the cell excludes 0; C1, the interval of the inner product of two
gradients in the cell excludes 0. A cell that passes neither is split. An *excursion* is a piece of the curve in a
cell with both ends on one edge; it is not refined away: under C1 it lies in the half disk over that edge, and the
isotopy moves it into the neighbour. For several curves the paper adds a test that the gradients of two curves are
nowhere parallel in a cell, so that an excursion cannot hide an intersection. Subdivision does not end at a singular
point or a tangential intersection. The paper has no statement for surfaces in space; the text we read gives no
bound of the depth by the distance of two nearly parallel branches (its examples report many more cells there).

## 3. Fit with the library

Building blocks found (searched before the prototype):

| Need | In the library |
|---|---|
| Dual contouring with caller-supplied Hermite data | `DualContouring.HermiteF` (used by `MeshOffset` to project crossings onto the input); `ExactHermite` bisects on `Implicit` and takes a central-difference normal (#991) |
| Contouring a sampled grid | `DenseGridTrilinearImplicit` / `CachingMeshSDFImplicit` wrap a grid as an `ImplicitFunction3d`; there is no mode for a grid without a function behind it |
| Marching tetrahedra, several crossings per edge | none (`MarchingCubes`, `MarchingCubesPro` only). `DualContouring.HiddenGapF` / `HiddenWallF` only *count* edges with two crossings in the sparse mode |
| Sheet fields | `TpmsLattice`: `F = max((|g| - c) / L, d)` with a proven gradient bound L (so F is 1-Lipschitz), marching cubes on it, and the documented limit "no topology guarantee for walls thinner than about two cubes" |
| Certificates | `ContouringTopologyCertificate` (exact, class P, clean meshes; stages 1 to 3 built); Theorem 5 of [CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md) 7.8 for functions with gradient enclosures (class S) is proven and not built; `MeshOffset.CertifyBand` (a 1-Lipschitz interval bound) |
| Intervals of implicits | `Interval1d` only; no interval evaluation of an `ImplicitFunction3d` |
| Metrics | `MeshFidelityMetrics` (edge Chamfer), `MeshTopologySummary`, `MeshRobustRepair.NotCleanReason`, the six CAD solids of `benchmarks/SharpFeatureProbe` |

Constraints: everything measured here is CPU only, needs no training and no solver beyond a 3 x 3 eigen problem,
and builds for netstandard2.0. Determinism: track B is a fixed per-tetrahedron construction (serial here; parallel
per tetrahedron with ordered output like `MarchingCubes`); track C is a fixed recursion; the paper's procedure of
track A samples at random and would need a fixed sample order. The certificate of track C must use outward-rounded
bounds in a build (the probe uses plain doubles and only counts cells).

## 4. The prototype

[benchmarks/ContouringSubgridProbe](../../benchmarks/ContouringSubgridProbe/README.md), public API only. Inputs are
procedural and rotated per seed, with a random grid shift (nothing is axis-aligned). Raw output is in its
`results/` folder; every number below is from there.

- **sharp**: the six CAD solids as exact distance grids (24 and 48 cells, 3 seeds). All variants contour the same
  sign grid and differ only in the Hermite data: the grid-only state (`interp`, `trilin`), a local grid-only
  estimator (`plane`), and the oracle (exact crossing and exact normal; `oracle-x` / `oracle-n` with only one of
  them exact).
- **thin**: a rotated plate, a spherical shell and a gyroid sheet clipped to a ball (16 cells per period, as
  `TpmsLattice`), wall 0.25 to 1.5 cells, 32 cells, 3 seeds. `MarchingCubes` and `DualContouring` at the grid, marching
  cubes at half and a quarter of the cell, and a restricted Subgrid Marching Tetrahedra with three sources of
  crossings: 8 samples per edge, sphere tracing with the Lipschitz bound, and the two-level form (below).
- **cert**: the number of cells of a Plantinga and Vegter subdivision for the gyroid sheet and the shell.

What the subgrid prototype leaves out of the paper: the dedicated fills (it uses fans; a loop with a scoop gets a
fan from a point half way to the centre of the tetrahedron, which costs position accuracy at rims),
the proof-carrying embedding, and the odd-sum cases (they cannot occur for a closed surface found by sign changes).

## 5. Track A: sharp features from a grid of distances

`dotnet run -c Release --project benchmarks/ContouringSubgridProbe -- sharp 3` (raw: `results/sharp.txt`). Six solids, 3
seeds, 18 runs per row; distances in cells. featMean / featMax: distance of points on the sharp edges of the truth
(dihedral angle above 30 degrees, spacing a quarter cell) to the result; cornerMax: the same for the truth's corners;
edge Chamfer: `MeshFidelityMetrics.EdgeChamfer` (sharp edges of both meshes); Hausdorff: the larger of its two sampled
maxima. All 216 results are clean (`NotCleanReason`, with self-intersections).

| cells | Hermite data | featMean | featMax | cornerMax | edge Chamfer | Hausdorff |
|---|---|---|---|---|---|---|
| 24 | interp (grid only, state before 2026-11) | 0.204 | 0.595 | 0.514 | 0.277 | 0.523 |
| 24 | trilin (grid only, today's defaults) | 0.208 | 0.608 | 0.523 | 0.256 | 0.547 |
| 24 | plane (grid only, local prototype) | 0.151 | 0.629 | 0.602 | 0.204 | 0.583 |
| 24 | oracle-x (crossing exact) | 0.146 | 0.452 | 0.382 | 0.180 | 0.391 |
| 24 | oracle-n (normal exact) | 0.105 | 0.545 | 0.464 | 0.141 | 0.499 |
| 24 | **oracle** (both exact) | **0.017** | 0.372 | 0.256 | **0.038** | 0.337 |
| 48 | interp | 0.201 | 0.605 | 0.511 | 0.287 | 0.501 |
| 48 | trilin | 0.204 | 0.626 | 0.525 | 0.257 | 0.521 |
| 48 | plane | 0.143 | 0.646 | 0.605 | 0.197 | 0.552 |
| 48 | oracle-x | 0.143 | 0.454 | 0.369 | 0.177 | 0.370 |
| 48 | oracle-n | 0.100 | 0.550 | 0.421 | 0.137 | 0.455 |
| 48 | **oracle** | **0.016** | 0.391 | 0.269 | **0.037** | 0.314 |

Reading:

- The bound is large in the mean and small in the maximum. With perfect Hermite data the mean distance of the
  result to the sharp edges falls 12x to 13x and the edge Chamfer 7x; the maximum (at corners and where a crease
  leaves a cell) falls only 1.6x, to 0.37-0.39 cells. That floor is `DualContouring` itself (one vertex per patch
  and cell, the eigenvalue cut-off, the cell margin), not the Hermite data. So the acceptance of plan #1297
  ("Hausdorff along creases and corners >= 3x") cannot be met by any Hermite estimator; a mean or Chamfer gate can.
- Crossing and normal both matter: either alone gives 1.4x to 2x, together 13x.
- Errors in cells do not shrink with the resolution in the grid-only rows (0.20 at 24 and at 48 cells): the blur
  of a trilinear gradient at a crease is a fixed fraction of a cell.
- The local estimator (`plane`: candidate planes from one-sided differences, chosen by the empty-ball rule and the
  eikonal defect, no iteration) recovers 30 % of the mean gap and makes the maximum slightly worse. A first version
  with more candidates was worse (0.19 to 0.20): choosing among planes is the hard part, because the plane of a face
  legitimately enters the ball of a node beyond a convex crease. This is the part the paper solves by its global
  fit, at the cost of up to 100 x 100 iterations with closest-point queries on the changing mesh (no timings in the
  paper; not prototyped here).
- Where the bound applies: only to an input that is a grid of distances and nothing else. `MeshOffset` passes a
  `HermiteF` that projects onto the input mesh, and `Implicit` inputs get `ExactHermite`; both are the oracle of
  this table already. The library has no reader or operation today whose only input is a sampled distance grid.

## 6. Track B: walls thinner than a cell

`... -- thin 3` (raw: `results/thin.txt`). 32 cells across [-1, 1], 3 seeds; k = wall thickness in cells.
Coverage: share of 4000 points on the true surface that have a result triangle within 0.25 cells whose normal is
within 60 degrees of the true one. "right": clean and the truth's components and genera (plate: one sphere;
shell: two spheres). Evaluations of the field include root finding.

Plate (a rotated box, a non-smooth exact distance), means of 3 runs:

| k | variant | clean | right | coverage | triangles | evaluations |
|---|---|---|---|---|---|---|
| 0.25 | `MarchingCubes` | 3 | 0 | 0.141 | 616 | 233 637 |
| 0.25 | `DualContouring` | 0 (empty) | 0 | 0.000 | 0 | 83 341 |
| 0.25 | marching cubes, cell / 2 | 3 | 0 | 0.521 | 4 768 | 1 763 893 |
| 0.25 | marching cubes, cell / 4 | 3 | 0 | 0.885 | 31 221 | 13 614 587 |
| 0.25 | subgrid, 8 samples per edge | 3 | **3** | 0.916 | 5 307 | 2 360 815 |
| 0.25 | subgrid, sphere tracing | 3 | **3** | 0.921 | 5 347 | 183 623 |
| 0.5 | `MarchingCubes` / `DualContouring` | 3 / 3 | 0 / 0 | 0.377 / 0.266 | 1 195 / 1 279 | 238 944 / 101 384 |
| 0.5 | marching cubes, cell / 4 | 3 | 3 | 0.993 | 30 224 | 13 622 880 |
| 0.5 | subgrid, sphere tracing | 3 | **3** | 0.943 | 5 360 | 189 870 |
| 1 | `MarchingCubes` / `DualContouring` | 3 / 3 | 0 / 0 | 0.781 / 0.713 | 2 003 / 1 586 | 244 517 / 200 752 |
| 1 | subgrid, sphere tracing | 3 | **3** | 0.952 | 5 561 | 199 770 |
| 1.5 | `MarchingCubes` / `DualContouring` | 3 / 3 | 3 / 3 | 0.896 / 0.996 | 2 024 / 726 | 245 544 / 123 824 |
| 1.5 | subgrid, sphere tracing | 3 | 3 | 0.958 | 5 773 | 207 640 |

Shell and gyroid sheet (smooth walls), sphere tracing against the two-level form; runs of 3:

| shape | k | variant | clean | topology | coverage | evaluations |
|---|---|---|---|---|---|---|
| shell | 0.25 | `MarchingCubes` / cell / 4 | 3 / 2 | wrong / wrong | 0.136 / 0.856 | 266 704 / 14 833 827 |
| shell | 0.25 | subgrid, sphere tracing | 3 | wrong in 3 of 3 (1 component, genus 2 to 4) | 1.000 | 688 292 |
| shell | 0.25 | subgrid, two-level | 2 | two spheres in 3 of 3 | 1.000 | 54 872 |
| shell | 0.25 to 1.5 | subgrid, sphere tracing (15 runs) | 12 | two spheres in 4 of 15 | 1.000 | 688 292 to 724 462 |
| shell | 0.25 to 1.5 | subgrid, two-level (15 runs) | 11 | two spheres in 15 of 15 | 1.000 | 54 872 |
| gyroid | 0.25 | `MarchingCubes` / cell / 4 | 3 / 2 | fragments | 0.059 / 0.657 | 257 464 / 14 798 987 |
| gyroid | 0.25 | subgrid, sphere tracing | 3 | 1 component, genus 26 to 31 | 0.912 | 904 050 |
| gyroid | 0.25 | subgrid, two-level | 3 | 1 component, genus 16 | 0.908 | 54 872 |
| gyroid | 0.25 to 1.5 | subgrid, two-level (15 runs) | 14 | 1 component, genus 16 in 15 of 15 | 0.908 to 0.962 | 54 872 |
| gyroid | 1.5 | `MarchingCubes` / cell / 4 | 3 / 3 | no agreement with the cell / 8 run | 0.754 / 0.981 | 332 717 / 15 119 580 |

Reading:

- **Thin walls are recovered.** At a quarter of a cell the subgrid result covers 0.90 to 1.00 of the surface where
  `MarchingCubes` covers 0.06 to 0.14 and `DualContouring` 0 to 0.04; a quarter of the cell size with marching cubes
  reaches 0.66 to 0.89 with 6x the triangles and still the wrong topology. The coverage below 1 is the rim of the plate and of the clipped gyroid (the fans of this prototype: vertices up
  to 0.5 cells off at the plate's rim and up to 1 cell at the gyroid's; 0.02 cells for the shell in the two-level
  form).
- **Closed and manifold in all 165 subgrid runs, no self-intersection found.** 148 are clean; the other 17 stop
  at "degenerate triangles" (a crossing next to a grid node makes a sliver, the case `TpmsLattice` handles with its
  sliver guard and grid shifts), which also hides the self-intersection check of `NotCleanReason` in those runs.
- **Topology is not guaranteed with sampled crossings, as the authors say.** The plate is right in 45 of 45 runs.
  In 11 of 15 sphere-tracing runs the two spheres of the shell are joined into one component (genus 0 to 4): where the surface dips
  through an edge and back (an excursion), a face sees 3 or more crossings on one edge, and the counts do not say
  which two of them belong together. Pairing neighbours or nesting gave identical results (`subtrace-n`).
- **The two-level form removes that ambiguity for sheet fields.** If the wall is `|g| <= w` and g is interpolated
  linearly on each edge, each of the two levels crosses an edge at most once, so no face has a scoop from the wall
  and the two sides are parallel planes in every tetrahedron: they cannot meet, at any thickness above zero. It
  uses the node values only (54 872 evaluations, a quarter to a sixth of what `MarchingCubes` with bisection spends), gave two
  spheres in 15 of 15 shell runs and the same topology (one component, genus 16) in all 15 gyroid runs across five
  thicknesses and three rotations. There is no certified reference for the gyroid: the marching-cubes runs at a
  quarter and an eighth of the cell do not agree with each other. What it cannot see is what the grid does not
  resolve of g itself (the period, not the wall).
- **Cost.** Sampling every edge 8 times costs 43x the node count; sphere tracing with the Lipschitz bound 3.3x
  (plate), 13x (shell) and 16x to 19x (gyroid, loose bound sqrt 6), unoptimised (40 bisection steps per crossing, a smallest step of 1 / 64
  cell). A refinement of the band of cells near the surface to a quarter of the cell would cost about 100 000 to
  725 000 evaluations by the same Lipschitz bound (column `bandEvals` of the raw output), so sphere tracing is not
  cheaper in evaluations than a band refinement; it saves triangles (5 300 against 31 000 for the plate) and has no
  lower limit of the thickness. Times of this serial dictionary-based prototype (0.1 to 0.5 s at 37^3 cells) say
  nothing about a build.

## 7. Track C: a subdivision certificate for thin walls

`... -- cert` (raw: `results/cert.txt`). Octree over [-1, 1]^3, split until C0 or C1 holds; depth 5 is the 32^3
grid of section 6. Counts only, no balancing (it adds a constant factor), no outward rounding.

| shape | wall k (cells) | as one function g^2 - w^2: cells / finest cell | as two levels g = +w, g = -w: cells / finest cell |
|---|---|---|---|
| gyroid | mid-surface only | 18 208 / 1 | |
| gyroid | 2 | 289 808 / 1/4 | 21 344 / 1 |
| gyroid | 1 | 1 044 240 / 1/8 | 21 344 / 1 |
| gyroid | 0.5 | 4 080 224 / 1/16 | 21 232 / 1 |
| gyroid | 0.25 | 16 181 936 / 1/32 | 19 440 / 1 |
| shell | mid-surface only | 106 / 4 | |
| shell | 1 | 66 508 / 1/4 | 106 / 4 |
| shell | 0.25 | 1 059 143 / 1/16 | 106 / 4 |

Reading:

- A wall given as **one** function cannot be certified cheaply by any test of this kind: the gradient of the
  function turns around inside the wall, so C1 fails in every cell that holds both sides, and the subdivision must
  go below the wall thickness (cells of about an eighth of the thickness here). The count grows 4x per halving of
  the thickness: 16 million cells for a quarter-cell gyroid wall on a 32^3 grid.
- Given as **two levels of one smooth function**, each level passes C1 wherever the mid-surface would, and the two
  levels cannot meet because they are levels of one function, so the pairwise test of Burr and Byrd is not needed.
  The count is that of the mid-surface (19 440 to 21 344 against 18 208) and does not depend on the thickness. The
  certified cells are the cells of the contouring grid itself (finest cell 1).
- This is the class S certificate of [CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md) (Theorem 5, option B,
  stage 3: proven, not built), applied once per level instead of once to `|g| - w`. Applied to `|g| - w` as one
  locally Lipschitz function, its gradient enclosure holds both `grad g` and `-grad g` in a thin-wall cell and no
  direction passes, the same failure as above.
- Excursions (item 5a): Burr and Byrd do not refine an excursion away; under C1 it stays in a half disk over its edge
  and the isotopy absorbs it. That is a statement about planar curves; the paper has none for surfaces, and our
  class P certificate forbids two crossings on an edge by its edge condition, which is what stopped the stage 2
  refinement (silhouette edges). Subgrid marching tetrahedra is the first contourer here that *represents* two
  crossings on an edge (scoops). Whether the edge condition can be relaxed to "the crossings the contour has" is a
  proof question for plan #1289, not measured here.
- Not covered by any of this: a thin wall that is a single non-smooth field, such as the box plate. At a crease the
  generalized gradient holds two normals 90 degrees apart, so C1 fails at every depth along the crease (an
  argument, not a measurement). For meshes that is what the exact class P certificate is for; for a thin plate as an
  implicit there is no certificate short of resolving it.

## 8. Options

How others solve it: sharp features from distance samples by empty-ball constraints (Kohlbrenner and Alexa 2025;
Sellán et al., *Reach for the Spheres* 2023, on the Later list of [research.md](../roadmap/research.md)) or by
learned priors, which we exclude; thin features by adaptive refinement (octree dual contouring, Ju et al. 2002; our
`SetRefinement`), by several vertices per cell (Schaefer et al. 2007, built) or by more data per edge (Subgrid
Marching Tetrahedra; occupancy-based dual contouring, noted in
[DC_INTERSECTION_FREE.md](../design/DC_INTERSECTION_FREE.md)); sheet lattices in practice by contouring the two
offset levels of the TPMS function; certified topology by interval subdivision (Snyder 1992; Plantinga and Vegter
2004; Burr and Byrd 2023 for curves) or restricted Delaunay refinement (Boissonnat and Oudot 2005). The last four
citations before Burr and Byrd are from memory, not re-read for this note.

| # | Option | Gain (measured here) | Effort | Risk |
|---|---|---|---|---|
| 1 | **Two-level marching tetrahedra for sheet fields**: an opt-in generator for `|g| <= w` clipped by a domain, on the node values of g; `TpmsLattice` Sheet style as the first user (opt-in, default output unchanged) | walls of any thickness closed and manifold, the two sides disjoint by construction; coverage 0.91 to 1.00 at a quarter cell (shell, gyroid) against 0.06 to 0.14; same topology in 30 of 30 runs; no evaluation beyond the nodes | M: a new file in `mesh_generators/`, tests (invariance, reuse, determinism, thin walls), a sliver guard as in `TpmsLattice` | low to medium: slivers at nodes (5 of 30 runs here); the rim at the domain needs the general scoop case or a grid-aligned clip; five tetrahedra per cube give 2.4x to 2.9x the triangles of marching cubes |
| 2 | **Certificate per level for sheet fields** (class S, stage 3 of the contouring certificate, with interval enclosures of g and its gradient) | the cells of the mid-surface, independent of the thickness (19 440 against 16.2 million) | M to L: the interval interface for implicits (new, opt-in), the cell test of Theorem 5; touches no exact file if built beside `ContouringTopologyCertificate`, otherwise the exact review and a mutation run | medium: Theorem 5 is reviewed but unbuilt; outward rounding; it certifies the level sets on the grid cells, and tying it to the marching-tetrahedra mesh needs the compatibility argument of Theorem 2 for that mesh |
| 3 | **General subgrid marching tetrahedra** with sphere tracing for 1-Lipschitz fields (plates, `MeshOffset` thin parts, any distance field) | the plate right in 45 of 45 runs; but handles in 11 of 15 shell runs (excursions), no topology guarantee | L: the paper's full case analysis (scoops, loops along an edge, octagons) and its embedding argument for freedom from intersections | medium to high: wrong topology without a signal unless the scoop count is reported; evaluations of the order of a band refinement |
| 4 | **Grid-only sharp features in `DualContouring`** (the paper's iterative fit, or a better local estimator) | bound 13x in the mean and 7x in edge Chamfer, 1.6x in the maximum; the local prototype reaches 30 % of the mean gap | L for the paper's method (closest-point queries on a changing mesh, up to 10^4 sweeps, a fixed sample order for determinism); M for more work on a local estimator with an unknown ceiling | high: no consumer in the library today, noise sensitivity stated by the authors, vertices leaving their cells against our intersection-free guarantee |
| 5 | **Relax the edge condition of the class P certificate to subgrid crossings** (excursions, plan #1289) | not measured; would address the silhouette double crossings that stopped stage 2 | L, proof first (exact code, exact review, mutation run) | high: no three-dimensional statement in the literature we read |
| 6 | Nothing | - | 0 | walls below two cells stay a documented limit of `TpmsLattice` |

## 9. Leaning

Option 1, then option 2 as its certificate, as two PRs; options 3 to 5 not now.

- Option 1 is the only lever here that is cheap, has a user in the library (`TpmsLattice` documents walls below two
  cubes as its limit) and comes with a structural guarantee instead of a measured one. The paper's general method
  is the route to it, but the measured topology errors of the general form (shell) argue for starting with the
  form that cannot have them.
- Option 2 answers plan #1289 for the class it can be answered for: a subdivision or per-cell certificate covers
  thin walls cheaply exactly when the wall is two levels of one smooth function. For a wall given as a single
  field, smooth or not, the cost is that of resolving it. That is a result for the plan's decision table; it does
  not reopen the stop rule of #944 for meshes.
- Option 4 has the largest number in this note and the weakest case: the gain exists only for callers who have a
  distance grid and no geometry behind it. If such an input appears (an importer of sampled distance volumes), the
  bound of section 5 says the mean gain is there to be had, and the acceptance should be stated on the mean or the
  edge Chamfer, not on the Hausdorff distance.

Open questions for the owner: (a) go for option 1 as an opt-in on `TpmsLattice` and a public generator, or as a
generator only; (b) whether option 2 may be built beside the exact certificate (a new, non-exact file with interval
bounds) or must go into it.
