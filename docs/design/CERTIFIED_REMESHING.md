# Certified surface remeshing (study)

Status: **study only, nothing built; waits for the owner's decision** (section 9). Docs only; the probe of section 4
was a throwaway program in the session's scratch directory (not committed; numbers and setup below). Written
2026-11-13.

Question: can the library offer an isotropic surface remesher with stated guarantees: the output closed, manifold and
free of self-intersections, within a Hausdorff band of the input, with the input's topology (isotopic, not only the same
genus), and with triangle quality bounds where they can be proved? Which of these need a reach / local feature size
(lfs) estimate, can one be computed for a triangle mesh, and what does each step cost?

Short answer:

- **Closed, manifold, no self-intersections and a one-sided band can be guaranteed by construction today**, with
  existing pieces: `Remesher.PreventSelfIntersections`, the link condition of the edge operations, and
  `TriangleDeviationBounds.IsWithin` (used by the Reducer and `AlphaWrapQualityPass`, not yet by the Remesher). The
  other direction of the band is a final certified check (`MeshHausdorffDistance`).
- **Topology: the reach / lfs route does not fit a triangle-mesh input.** The reach of a polyhedral surface is 0 at
  every crease (the medial axis touches every non-flat edge), so every guarantee of the Boissonnat-Oudot / Chew family
  ("isotopic for a band below the reach / for a sampling below eps lfs") is void for it as stated. The weak feature
  size is positive but there is no cheap certified bound for it.
- **A reach-free route exists:** prove every edge operation and vertex move an *elementary move across an empty
  tetrahedron* (the PL definition of isotopy). Then the output is ambient isotopic to the input by construction, with
  exact `Orient3d` tests on the mesh's doubles only, no reach, no constructed point. This is stronger than
  `PreventSelfIntersections` (which checks only the end state: a flip can jump over a thin sheet and change the linking
  while both states are intersection-free). Prior art: the CCD-guarded remeshing of Brochu and Bridson (El Topo).
- **A post-hoc grid certificate does not work on a fixed grid** (probe, section 4): the contouring study's grid lemma
  (Theorem 1 of CONTOURING_TOPOLOGY.md) applies to any two PL surfaces, but grid nodes inside the thin band between
  input and output flip the crossing pattern; on 4 procedural shapes, 0 of 8 random grid offsets certified the output in
  40 of 48 settings.
- **Triangle quality has no provable absolute bound for the Botsch-Kobbelt remesher**; what can be proved is
  monotonicity ("no operation lowers the smallest angle below the current minimum or a threshold", Hu et al. 2017,
  already the rule of `AlphaWrapQualityPass`). Provable absolute angle bounds need Delaunay refinement (Chew 1993,
  Boissonnat-Oudot 2005, DelPSC), which on faceted meshes needs protecting balls at every crease: tier L or more.
- **Recommendation: option B** (section 6): an opt-in isotopy guard in the Remesher (empty swept tetrahedra per
  operation) plus a band guard and a `RemeshCertificate` result, in three stages, after a stage-0 proof of the move
  lemmas. Tier M (stages 1-2), stage 3 measured.

## 1. What the library has (searched before the literature)

| Block | Where | What it gives here |
|---|---|---|
| `Remesher` / `RemesherPro` / `RegionRemesher` | `mesh/Remesher.cs` | Botsch-Kobbelt split / collapse / flip / smooth / project; collapses under the link condition (manifold kept); `PreventSelfIntersections` (exact, `ExactTriangleIntersection.IntersectsBeyondShared`, end state of every operation and buffered move pass, deterministic revert rounds); `MinTriangleQuality` guards (normal flips, flat triangles, the rim fold check of #621 / #640); `RejectInvertingPasses` (whole-pass rollback when a closed component turns inside out); 2^k scaling bit-identical since 2.0 T5 |
| Quality and validity measures | `TriangleQualityStatistics`, `MeshFidelityMetrics`, `MeshTopologySummary`, `MeshRobustRepair.NotCleanReason(s)`, `MeshRepairMetrics.SelfIntersections` | closed / manifold / degenerate / self-intersection checks (exact), genus and components, p01 angles |
| Certified distances | `MeshHausdorffDistance` (Sacht-Jacobson 2024 cascading bounds), `TriangleDeviationBounds.IsWithin`, `PolyhedralEnvelope` (Wang et al. 2020, exact) | two-sided Hausdorff as an interval; per-triangle "within eps of the input" (float, certified); exact envelope containment (too slow for per-operation use in the remeshers, research.md) |
| Guarded local operations | `AlphaWrapQualityPass` (Hu et al. 2017 style) | the pattern of option B: worst-first ops, each kept only if it keeps the min angle, stays within tolerance (IsWithin), and (optionally) creates no intersection; serial, deterministic, scale invariant |
| 2D Delaunay refinement with proofs | `QualityMesh2` (Ruppert / Shewchuk / Chew, off-centres, concentric shells, lenses; MESHING2D_*.md) | the proof style and oracle / golden infrastructure for angle bounds; nothing 3D |
| 3D Delaunay | `Delaunay3` (exact Bowyer-Watson, `Orient3d` / `InSphere`) | the kernel a restricted-Delaunay remesher (option C) would use |
| Alpha wrap | `MeshAlphaWrap` | a restricted-Delaunay-like mesher of the *offset* surface with Chew-style quality refinement (`RefineMinAngleDegrees`) and a proven separation, so min angle >= asin(offset / (2 alpha)); closed, manifold, intersection-free by construction. Not a remesher of the input: one-sided band (outside), topology not kept (gaps below 2 alpha are closed) |
| Contouring topology certificate | CONTOURING_TOPOLOGY.md (#749, owner decision 2026-11-13: stages 1-2 of option A), spike on `claude/contouring-certificate-stage1` | grid lemma (two PL surfaces compatible with one grid and with the same pattern are ambient isotopic) and an exact per-cell test by counting (Theorem 3, `Orient2d` / `Orient3d` only). Probed for remeshing in section 4 |
| lfs-like estimates | `MeshWallThickness` (InscribedSphere, shrinking ball with the Dey-Zhao angle condition), `RemeshSizing.FromWallThickness` | sizing below the local thickness; an estimate, not a bound: cannot be the premise of a guarantee |
| Intrinsic triangulations | `IntrinsicTriangulation.DelaunayRefine`, `IntrinsicCommonSubdivision` | provable angle bounds on the *same* surface (Hausdorff 0, topology trivially kept), but the result is intrinsic; the common subdivision is a polygon mesh, not a remesh |

Missing: an exact triangle-tetrahedron / tetrahedron-tetrahedron overlap test (all `Orient3d`, small); a swept-volume
check of the Remesher's operations; the band guard (`IsWithin`) in the Remesher; a certificate result object for a
remesh; any certified reach / weak-feature-size bound; restricted Voronoi computations on a PL surface (option C).

## 2. Prior art

Read status: **R** read (abstract and summary pages in this study), **S** from a restating source (the
search summaries or the library's own notes), **M** from memory, not re-read (check before code is built on it). All
paper only; no code read. CGAL and libigl appear as papers only.

| Source | Guarantee | Needs | Fits a PL input? |
|---|---|---|---|
| L. P. Chew, *Guaranteed-quality mesh generation for curved surfaces*, SoCG 1993 (in SOURCES.md) | min angle (30 deg) via circumcentre insertion on the surface | smooth surface, sizing below lfs | no (lfs = 0 at creases) |
| H. Edelsbrunner, N. Shah, *Triangulating topological spaces*, IJCGA 7, 1997 (M) | restricted Delaunay homeomorphic if the Voronoi diagram has the topological ball property (TBP) | TBP check | in principle (TBP can be tested), at the cost of exact restricted Voronoi faces |
| J.-D. Boissonnat, S. Oudot, *Provably good sampling and meshing of surfaces*, Graphical Models 67, 2005 (in SOURCES.md; R via CONTOURING_TOPOLOGY.md) | restricted Delaunay isotopic, Hausdorff O(eps^2 lfs), angle bounds | C2 surface, eps-sample w.r.t. lfs, an intersection oracle | no |
| S.-W. Cheng, T. K. Dey, E. A. Ramos, T. Ray, *Sampling and meshing a surface with guaranteed topology and geometry*, SoCG 2004 (S) | homeomorphism without lfs, by enforcing TBP | smooth surface, critical-point computations | no |
| S.-W. Cheng, T. K. Dey, E. A. Ramos, *Delaunay refinement for piecewise smooth complexes*, SODA 2007 / DCG 2010 (S); S.-W. Cheng, T. K. Dey, J. A. Levine, *A practical Delaunay meshing algorithm for a large class of domains*, IMR 2007 (DelPSC) (S); T. K. Dey, J. A. Levine, *Delaunay meshing of piecewise smooth complexes without expensive predicates*, 2008 manuscript (S) | homeomorphism (extended TBP), creases protected by weighted points, no input-angle restriction | features given as a complex; on a faceted mesh every crease edge is a feature | yes in principle; cost and the number of protecting balls on faceted meshes unknown; DelPSC software takes a polygonal mesh |
| S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC 2012, ch. 13-15 (in SOURCES.md; M for chapter numbers) | the textbook statements of the rows above | | |
| F. Chazal, D. Cohen-Steiner, *A condition for isotopic approximation*, Symposium on Solid Modeling 2004, pp. 93-99, doi 10.2312/sm.20041380; INRIA RR-4931, 2003 (R, abstract) | two homeomorphic surfaces are isotopic if one lies in a topological thickening of the other and separates its sides | that the band is a thickening (reach / wfs) | needs a wfs bound for the input |
| F. Chazal, A. Lieutier, *The lambda-medial axis*, Graphical Models 2005; F. Chazal, D. Cohen-Steiner, A. Lieutier, *A sampling theory for compact sets in Euclidean space*, DCG 41, 2009 (M) | weak feature size (wfs) and mu-reach: positive for polyhedra, offsets below wfs share the topology | critical points of the distance function | defined, but no cheap certified bound for a mesh |
| M. Botsch, L. Kobbelt, *A remeshing approach to multiresolution modeling*, SGP 2004 (in SOURCES.md) | none (the heuristic baseline the `Remesher` implements) | | |
| V. Surazhsky, C. Gotsman, *Explicit surface remeshing*, SGP 2003 (M) | none (area-equalising relocation, angle improvement) | | |
| D.-M. Yan, B. Lévy, Y. Liu, F. Sun, W. Wang, *Isotropic remeshing with fast and exact computation of restricted Voronoi diagram*, SGP 2009 (M) | CVT quality measured, no topology proof; RVD computed exactly on the input triangles | | building block for option C (the RVD of a PL surface); geogram (BSD) implements it, licence to check per file |
| K. Hu, D.-M. Yan, D. Bommes, P. Alliez, B. Benes, *Error-bounded and feature preserving surface remeshing with minimal angle improvement*, IEEE TVCG 2017 (in SOURCES.md) | hard Hausdorff bound per operation, min angle monotone | | yes; the model of option B's band and quality guards |
| Y. Wang et al., *Isotropic surface remeshing without large and small angles*, TVCG 2019 (in SOURCES.md) | angle ranges measured, not proved | | |
| T. Brochu, R. Bridson, *Robust topological operations for dynamic explicit surfaces*, SIAM J. Sci. Comput. 31(4), 2009 (El Topo; S: UBC TR-2009-03 and search summary) | the mesh stays intersection-free through remeshing and motion; each collapse / flip / split is checked as a motion with continuous collision detection | CCD | yes; **closest prior art of option B** |
| T. Brochu, E. Edwards, R. Bridson, *Efficient geometrically exact continuous collision detection*, SIGGRAPH 2012 (S) | exact CCD for linear vertex motion; used in curvature-driven adaptive cloth remeshing that stays intersection-free | | yes; option B needs only the simpler swept-tetrahedron case (one vertex moves, or a flip) |
| C. Rourke, B. Sanderson, *Introduction to piecewise-linear topology*, Springer 1972 (M) | PL isotopy is generated by elementary moves across simplices whose interiors miss the rest | | the lemma behind option B |

How others solve it (one line): guaranteed topology comes either from sampling theory on smooth surfaces (lfs, reach,
TBP; Boissonnat-Oudot, Cheng-Dey) or, for explicit meshes that move and are remeshed, from checking every local change
as a collision-free motion (Brochu-Bridson); error bounds from per-operation envelope or Hausdorff tests (Hu 2017,
fTetWild's envelope); angle bounds are proved only by Delaunay refinement and are otherwise monotone or measured.

## 3. Which guarantees need a reach / lfs estimate

| Guarantee | Needs reach / lfs? | Without it |
|---|---|---|
| closed, manifold | no | link condition and non-degenerate operations (by construction, today) |
| no self-intersections | no | exact end-state checks (`PreventSelfIntersections`, today) |
| out -> in band (output within delta of input) | no | `IsWithin` per new triangle (certified, float distances with bounds); exact alternative `PolyhedralEnvelope` |
| in -> out band (input within delta of output) | no | final `MeshHausdorffDistance` (certified interval); per operation only approximately (the removed triangles' patch against the new one) |
| same genus, components, nesting (homeomorphism, same inside / outside structure) | no | `MeshTopologySummary` and exact nesting tests at the end; implied by option B |
| **isotopy** | **yes for the sampling theorems** (band below reach, sampling below eps lfs; reach = 0 at every crease of a PL surface, so they say nothing); wfs-based variant (Chazal-Cohen-Steiner) needs a certified wfs bound | **elementary moves** (option B): no premise on the input beyond being clean |
| absolute min-angle bound | yes (Chew, Boissonnat-Oudot: sizing below lfs; DelPSC: protecting balls) | monotone bound only (no worse than the threshold or the current minimum) |

Computing or bounding the reach / wfs of a mesh: the reach of a polyhedral surface is 0 unless it is flat; the local
feature size is 0 on every crease. The wfs is the smallest critical value of the distance function; a certified lower
bound needs a search for critical points over the band (a branch and bound with distance bounds on cells, in the
style of `MeshHausdorffDistance`), with no published cheap method found (terms: "weak feature size computation
polyhedron", "certified reach estimate mesh"). `MeshWallThickness` estimates local thickness well on smooth parts but
is not a bound. Use: sizing (`RemeshSizing.FromWallThickness`) and diagnostics only.

## 4. Probe: the grid isotopy certificate on remesher output (throwaway, not committed)

Idea: Theorem 1 of CONTOURING_TOPOLOGY.md is stated for *any* two PL surfaces compatible with a grid with the same
pattern. Run the spike's exact cell test (`LevelSetCertificate`, commit fe9f3361 of
`claude/contouring-certificate-stage1`, copied, the winding classification removed) on the input M and the output M'
on one uniform grid; the crossings of M define the pattern that M' must match (pairing on 4-crossing faces by the
exact parity inside test of M). Certified = no cell refused for either.

Setup: master d2e02a14, net10.0, 4 cores. Procedural closed inputs, rotated by 23.7 deg about (0.3, 0.7, 0.2): sphere
(18k triangles), bumpy sphere (42k), torus R 1 r 0.3 (15k), thin torus r 0.04 (13k). `Remesher` with
`SetTargetEdgeLength(L)`, `MeshProjectionTarget.Auto(input)`, `MinTriangleQuality = 0.2`, 15 passes;
`PreventSelfIntersections` on for the thin torus. Grid cell diag / N, N in {8, 12, 16, 24}, 8 random offsets.

| case | L / diag | out tris | new SI pairs | Hausdorff / diag | offsets certified at N = 8 / 12 / 16 / 24 | cells refused for the input (best offset) |
|---|---:|---:|---:|---:|---|---|
| sphere | 0.01 | 33360 | 0 | 0.00031 | 5 / 4 / 2 / 1 | 0 |
| sphere | 0.02 | 7264 | 0 | 0.00089 | 5 / 3 / 0 / 0 | 0 |
| sphere | 0.05 | 1136 | 0 | 0.00405 | 0 / 0 / 0 / 0 | 0 |
| bumpy | 0.01 | 30758 | 0 | 0.00108 | 0 / 1 / 1 / 0 | 0-4 |
| bumpy | 0.02, 0.05 | 7264, 1130 | 0 | 0.0029, 0.0118 | 0 everywhere | 0-4 |
| torus | 0.01, 0.02, 0.05 | 24390, 5328, 894 | 0, **6**, 0 | 0.0006-0.017 | 0 everywhere | 4-7 |
| thin torus (si on) | 0.01-0.05 | 4656-552 | 0 | 0.0036-0.024 | 0 everywhere | 28-71 |

Earlier single-offset run, refusal reasons: almost all refusals of M' are **sign mismatches** (a grid edge crossed by
M but not by M', or the reverse: a node lies between the two surfaces), e.g. sphere L 0.05 at / 64: 2047 of 2061
refused cells; the input's own refusals are edges crossed twice near silhouettes (a grid line almost tangent to the
surface) and, for the thin torus, faces and cells (tube thinner than a cell).

Reading: the expected number of grid nodes in the band is about area x mean deviation / h^3, so the certificate needs
cells coarse against the deviation but fine against the features; that window is empty or narrow for most settings,
and refining the grid adds nodes to the band rather than removing them. A post-hoc isotopy certificate on a fixed or
adaptive box grid is **not practical** for remeshing; repairing it (moving nodes off the band, non-box cells) is a
research item, not a building block. Side findings: the plain `Remesher` at `MinTriangleQuality = 0.2` created 6
self-intersecting pairs on a clean torus (L 0.02 diag) and 460 / 123 / 63 on the thin torus without
`PreventSelfIntersections` (not tabulated above); the probe cost 0.1-0.7 s per grid, comparable to the remesh itself.

## 5. Options

| | Option | Guarantees (proved / measured) | Tier | Cost estimate |
|---|---|---|---|---|
| A | **Certify after the fact**: `RemeshCertificate` (closed, manifold, exact no self-intersections, certified two-sided Hausdorff, same genus / components / nesting) on the existing Remesher with `PreventSelfIntersections` | all checks proved when they pass; isotopy not covered; a failed check is reported, not avoided | S | one exact SI check + one `MeshHausdorffDistance` + topology summary per call, about the cost of 1-3 remesh passes (to measure) |
| B | **Guard by construction**: A plus, per operation, (1) the isotopy guard (every flip, collapse, split and vertex move an elementary move across empty tetrahedra, exact), (2) the out -> in band guard (`IsWithin(delta)` on new triangles), (3) the min-angle monotone guard (exists in part as `MinTriangleQuality`); the final certificate of A checks in -> out | closed, manifold, intersection-free, ambient isotopic to the input, out -> in band: proved by construction; in -> out band: certified at the end (reported if not met); quality: monotone, p01 measured | M (stages 1-2), S (stage 3) | upper bound below: about 1-2 x the cost of today's `PreventSelfIntersections` (12-17 x the plain Remesher on Thingi10K before #261's filter, 2-4 x less after it) plus `IsWithin` (the Reducer's `MaxDeviation` pays 12-32 x, cached) |
| C | **Restricted Delaunay refinement** on the PL input (Delaunay3 + exact restricted Voronoi of the triangles, DelPSC-style protecting balls at creases, TBP checks, Chew quality rule) | homeomorphism (TBP), absolute angle bounds away from small input angles, Hausdorff by refinement; isotopy from the TBP results only for smooth parts (M) | L / XL | new high-degree implicit predicates (Voronoi vertex against a triangle plane), protecting balls on every crease edge of a faceted mesh, vertices rounded to doubles (re-check needed, as the rounding of the boolean); no upper bound possible without a prototype |
| D | **Alpha wrap at a small offset** (exists) | closed, manifold, intersection-free, angle bound asin(offset / (2 alpha)), proved; one-sided band (outside, up to about offset + alpha) | 0 | exists; not a remesher of the input: topology not kept, output offset outwards |
| E | **Grid isotopy certificate** after the fact (section 4) | isotopy when it passes | M | probe: rarely passes; not recommended |

Upper bound for B (from existing measurements; a probe is stage 1's first step): the isotopy test uses the same
candidate search as `PreventSelfIntersections` (the triangles near the union box of the old and new triangles; the
swept tetrahedra lie in the convex hull of old and new, so the box is the same) and replaces each triangle-triangle
test by triangle-tetrahedron tests (a tetrahedron is 4 faces plus containment: at most about 4 exact tri-tri-like
decisions, most rejected by the float filter of `TriangleTriangleFilter` style). Bound: 1x (filter rejects as before)
to 4x (no filter) the check cost of `PreventSelfIntersections`, whose check is 92 % of its run. So B costs about
13-50 x the plain Remesher in the worst reading, realistically 1-2 x the `PreventSelfIntersections` time. The
smoothing pass must become a sequence of independent sets (below), losing part of its parallel speed (smoothing +
projection were 26-47 % of the plain run, parallel).

## 6. Recommendation: option B, staged

**Stage 0 (docs, S): the move lemmas, written out like CONTOURING_TOPOLOGY.md section 7.** On a clean closed surface S
(PL, embedded):

1. *Flip* of edge ab with opposite vertices c, d: the old and new triangles bound the tetrahedron abcd. If abcd is
   non-degenerate and no triangle of S other than the two old ones meets it except in the shared edges and vertices of
   its boundary (exact tests), the flip is a move across abcd: an ambient isotopy. Flat case (abcd degenerate): the
   flip is in the plane, an isotopy iff the quad is strictly convex (the old triangles already exclude other surface
   there).
2. *Vertex move* v -> v' along a segment: triangle (v, a, b) sweeps the tetrahedron (v, v', a, b). If these
   tetrahedra have pairwise disjoint interiors, each meets the others only in the faces / edges through vv', and no
   other triangle meets their union except in the shared link, the move is an isotopy (at time t each moving triangle
   lies in its own tetrahedron; neighbours meet only in the segment v(t) b; argument to be written out).
3. *Collapse* of ab (a -> b) under the link condition: the vertex move a -> b of rule 2 restricted to the link of a
   without b; the two triangles at ab degenerate. Claim: isotopy if the swept tetrahedra (a, b, x_i, x_{i+1}) satisfy
   rule 2. The degenerate end needs its own argument (an epsilon-before-the-end surface plus one more move); to prove.
4. *Split*: the new vertex is the rounded midpoint m; if m is not exactly on ab, the split is a move across (a, b, c,
   m) and (a, b, d, m) (rule 1 shape); exactly on ab: no move.
5. *Simultaneous moves* (the buffered smoothing and projection passes): an independent set of vertices (no two in one
   triangle) moving at once is an isotopy if, in addition to rule 2 per vertex, the swept tetrahedra of different
   vertices have disjoint interiors. Deterministic batches: a fixed greedy colouring by vertex id; a rejected move
   stays (as the `PreventSelfIntersections` revert rounds today).
6. Input requirement: the input must be clean (closed, manifold, no self-intersections; `NotCleanReason` empty);
   otherwise the guard reports `InputNotClean` and runs as today.

Proof review by a fresh session that gets the note only (as #749). Prior art for these lemmas: Rourke-Sanderson
(elementary moves), Brochu-Bridson (the remeshing practice).

**Stage 1 (M): `RemeshCertificate` (option A) and the probe of B's cost.** A result object with `Closed`, `Manifold`,
`SelfIntersectionFree` (exact), `Hausdorff` (interval), `TopologyKept` (genus, components, nesting), and the reasons;
the stage-1 probe instruments the swept-tetrahedron test (throwaway) on the RemeshQualityBenchmark set to replace the
upper bound above by a measurement, with a stop rule: if B costs more than 2 x `PreventSelfIntersections` on the median
mesh, report and let the owner decide.

**Stage 2 (M): the guards** in `Remesher` / `RemesherPro`, opt-in (`PreserveIsotopy`, `MaxDeviation`), the exact
triangle-tetrahedron test (new, `Orient3d` only; in an exact file under the `.editorconfig` rules, no parallel loop
inside it), sharing the hash grid of `PreventSelfIntersections` (which it implies: `PreserveIsotopy` switches it on).
Defaults unchanged (upstream compatibility; the owner's rule on the Remesher defaults).

**Stage 3 (S, measured only):** quality: the monotone min-angle rule in front of every operation (as
`AlphaWrapQualityPass`), absolute p01 reported on the benchmark set; no absolute bound claimed.

Not recommended now: C (tier L/XL, no prototype bound; revisit if a user needs absolute angle bounds with topology,
eg FEM, and then first for smooth implicit inputs where Boissonnat-Oudot applies directly); E (section 4).

## 7. Guarantees: proved vs measured (option B)

| Property | Status |
|---|---|
| closed, 2-manifold, consistently oriented | proved (link condition, rules of stage 0) |
| no self-intersections | proved (implied by the isotopy guard; exact) |
| ambient isotopic to the input | proved, after stage 0's review; requires a clean input |
| output within delta of the input | proved per new triangle (`IsWithin`, certified float bounds) |
| input within delta of the output | certified at the end (interval); not guaranteed: reported |
| min angle | monotone (proved); absolute p01 measured |
| target edge length reached | measured (guards block operations; the share blocked is reported) |
| determinism | parallel = serial, repeat = same bits (fixed colouring and order); tested |
| 2^k scale invariance | bit-identical (exact predicates are scale-free, the guards use relative or scaled tolerances); tested |

## 8. Test plan (repo style)

- **Counterexample tests that fail on the old code** (the CI job `New tests on base`): a thin sheet threaded through
  the tetrahedron of a flip (both end states intersection-free, linking changed): `PreventSelfIntersections` accepts
  the flip, `PreserveIsotopy` must reject it; the same for a collapse and for a smoothing move that jumps a sheet; a
  split whose rounded midpoint leaves the edge next to a touching sheet.
- **Result checks on the success path**: `NotCleanReason` empty, `RemeshCertificate` all true, genus and components
  equal, `MeshHausdorffDistance.UpperBound <= delta` for the out -> in direction, on procedural inputs (sphere, torus,
  thin torus, two nested shells, a box with creases, a linked pair of tori: the case whose linking a non-isotopic remesh
  could change).
- **Invariance over `Similarity.All()`** (`tests/geometry3Sharp.Tests/Harness/Invariance.cs`): 2^k scales bit for bit;
  the other similarities give the same certificate booleans and topology, triangle counts within the 5 % of 2.0 T5.
- **Reuse**: `RemeshCertificate.Compute()` twice on one instance; one `Remesher` instance run twice.
- **Determinism**: parallel smoothing batches = serial, 10 repeated runs same bits, any thread count.
- **Mutation**: each rule of stage 0 gets a hand mutant (drop the flat-flip convexity test, drop the
  tetrahedron-tetrahedron test of simultaneous moves, test only the end state) that a test kills; the exact test file
  in its Stryker shard; incremental run on the PR.
- **Fuzz**: random clean inputs from the harness generators, the invariant "certificate true" as the oracle; any
  failure becomes a test.
- Time: procedural inputs of at most a few thousand triangles per test (5 s rule); the corpus run is a benchmark
  (`benchmarks/RemeshQualityBenchmark`, with before / after numbers), not a test.

## 9. Risks

- **The guard blocks too much**: near thin walls and creases most operations may be refused, so the result keeps
  short edges and misses the target length (as `PreventSelfIntersections` today: "thin walls stay intersection-free but
  end far from the input"). Measured in stage 1; reported as the blocked share.
- **Cost** (section 5): the bound is wide (1-4 x the SI check); the stop rule of stage 1 applies.
- **The collapse lemma** (stage 0, item 3) is the one not yet argued; if it fails, collapses must be decomposed
  (vertex move to near b plus a flip-like removal) or checked more conservatively.
- **Parallel smoothing** becomes batched: slower; the determinism argument must cover the batch order.
- **In -> out band** stays a final check, not a construction guarantee: a mesh can pass all guards and still miss it
  (drift); the certificate reports it.
- Option C stays the only route to absolute angle bounds; the owner should not expect them from B.

## 10. Decisions for the owner

1. Option B, staged as in section 6 (stage 0 proof and its review first, stage 1 certificate and cost probe with the
   stop rule "> 2 x `PreventSelfIntersections` on the median mesh: report")? Leaning: yes.
2. Opt-in only (`PreserveIsotopy`, `MaxDeviation`), defaults unchanged? Leaning: yes (upstream compatibility, the
   owner's 2026-11 rule on Remesher defaults).
3. Absolute angle bounds (option C): not now; a separate study only if a user asks for them (eg FEM). Leaning: not now.
4. Order against the running work (Exact Boolean 3.0, Alpha Wrap 3.0, contouring certificate stages 1-2): stage 0
   (docs) can run anytime; stages 1-2 after the contouring certificate stage 1, which builds a similar exact counting
   kernel. Leaning: stage 0 now, the rest queued.
