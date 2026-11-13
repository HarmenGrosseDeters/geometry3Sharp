# Certified surface remeshing by restricted Delaunay refinement (study)

Status: **study only, nothing built; waits for the owner's decision** (section 12). Docs only. The probe of section 9
was a throwaway program in the session's scratch directory (not committed; numbers and setup there). Written
2026-11-13; revised the same day on the owner's focus note (via the coordinator): restricted Delaunay refinement on
surfaces is the main subject, the guard of the existing `Remesher` a side section (section 10).

Question: an isotropic surface remesher with stated guarantees: closed, manifold, free of self-intersections, within
a Hausdorff band of the input, topology kept (homeomorphic, isotopic where it can be proved), triangle quality
(radius-edge ratio / minimum angle) where it can be proved. Which theorem gives which guarantee under which
hypotheses; how the hypotheses can be met or certified for a triangle-mesh input, whose local feature size is 0 at
every crease; which predicates are needed and which of the library's exact building blocks cover them; termination,
size bounds, determinism, 2^k invariance; a staged plan with tiers and stop rules.

Short answer:

- **The classical theorems do not apply to a triangle mesh as stated.** Boissonnat-Oudot (2005) and the Chew-style
  refinement prove homeomorphism, isotopy, Hausdorff O(eps^2 lfs) and a 30 degree angle bound for epsilon-samples of a
  smooth (C2) surface, with the sizing tied to the local feature size (lfs). A polyhedral surface has lfs = 0 on every
  crease (the medial axis touches every non-flat edge), so the epsilon-sample condition cannot be met there.
- **What replaces lfs for a PL input** (section 4): (a) the **topological ball property (TBP)** of Edelsbrunner and
  Shah, which gives homeomorphism without any feature size and *can be checked exactly* on a PL surface (the counting
  lemma of the contouring certificate, CONTOURING_TOPOLOGY.md Theorem 3, holds for any convex cell, so it carries over
  to Voronoi cells); refinement enforces it, as Cheng, Dey, Ramos and Ray (2004) do on smooth surfaces;
  (b) **feature protection** at creases and corners above a feature angle (sample points on the crease curves first,
  weighted protecting balls as in DelPSC where that is not enough); (c) a **user minimum size h_min**, which makes
  termination and the size bound unconditional (packing) and turns the theorems into a **certificate**: every region is
  proved or listed as uncertified, as in the contouring certificate.
- **Guarantees then** (section 5): closed, 2-manifold and free of self-intersections by construction (the output is a
  subcomplex of the exact `Delaunay3` triangulation; TBP gives the manifold); homeomorphic to the input when no region
  is uncertified; Hausdorff both ways at most r_max, proved by the refinement rules; radius-edge ratio at most rho
  (min angle at least arcsin(1 / (2 rho)), 30 degrees at rho = 1) on every facet except listed ones near h_min or at
  protected features. **Isotopy is not covered** by a published theorem for PL inputs without lfs; it is a stage-0
  proof attempt (section 5.3), not a promise.
- **Predicates** (section 6): one new family covers the restricted-Delaunay test, the surface Delaunay balls and the TBP
  checks: signs of linear forms (bisector planes, the input triangle's planes and edge lines) at the intersection point
  of two bisector planes with a triangle's plane, a three-plane point of degree 5 / 4 whose tests have degree 6, lower
  than the TPI orient3d (degree 9) the predicate compiler already handles. `Delaunay3`, `ExactPredicates`, the
  predicate compiler with `FixedArith`, Simulation of Simplicity (`ExactRayTriangle`) and the alpha wrap's refinement
  skeleton cover the rest. Stage 2's weighted points need a regular triangulation (power test): new.
- **Recommendation**: build it, in stages (section 11): stage 0 the proofs (docs), stage 1 a float-predicate spike as
  the upper bound, then smooth-ish closed inputs without features with the exact predicates and the certificate
  (tier L), stage 2 feature protection (tier L), each with a stop rule. The cheap side path (guard the `Remesher`,
  section 10) stays available and is independent.

## 1. What the library has (searched before the literature)

| Block | Where | Use for restricted Delaunay refinement (RDR) |
|---|---|---|
| `Delaunay3` | `comp_geom/Delaunay3.cs` | exact incremental Bowyer-Watson (`Orient3d`, `InSphere`), box hull with 8 corner vertices (no infinite cells, so every Voronoi edge of an interior facet is a bounded segment between two circumcentres), stochastic walk, `Insert(p, hint, minSeparation, ..., removed, added)` reports the cavity's removed and new cells: exactly the update RDR needs. No deletion, no weights |
| `MeshAlphaWrap` | `mesh_ops/MeshAlphaWrap.cs`, ALPHA_WRAP_PAPER_NOTES.md sections 6-7 | the closest existing mesher: refines a `Delaunay3` by inserting points on a surface (the offset surface), Chew / Boissonnat-Oudot quality rule (`RefineMinAngleDegrees`: the centre of a facet's surface Delaunay ball = its dual Voronoi edge crossing the surface), a proven separation min(offset, alpha - offset) giving termination and asin(offset / (2 alpha)); pinch refinement after Boissonnat-Oudot. Its surface tests are floating point (distance to the input) because the offset surface is not PL |
| 2D Delaunay refinement | `QualityMesh2` (Ruppert, Shewchuk, Chew; off-centres, concentric shells, diametral lenses; MESHING2D_*.md) | the proof pattern: insertion radius, termination by packing, the small-angle exemptions; concentric shells are the 2D analogue of protecting the neighbourhood of a sharp feature |
| Exact predicates and implicit points | `ExactPredicates` (`Orient2d`, `Orient3d`, `InCircle`, `InSphere`), `AdaptivePredicates`, `math/Generated/IndirectPredicates.cs` from `tools/PredicateCompiler` (LPI, TPI; filter with proven bound, expansions, `FixedArith` / `BigInteger` fallback), FIXED_WIDTH_EXACT_POINTS.md | the generator derives the filters and bounds of the new predicate family (section 6) from the formula; the bounds-table style and its tests exist |
| Exact mesh kernels | `ExactMeshArrangement` (`TriangleBoxTree`, internal), `ExactTriangleIntersection`, `ExactRayTriangle` (with Simulation of Simplicity) | candidate search of input triangles against Voronoi edges and cells in exact code; SoS for degenerate positions |
| Contouring certificate | CONTOURING_TOPOLOGY.md (#749; owner 2026-11-13: stages 1-2 of option A), spike `claude/contouring-certificate-stage1` | Theorem 3's counting lemma (Euler characteristic of S in a *convex* cell by counting triangles, edges and vertices that meet it) is the exact TBP test; the per-cell certified / uncertified statement is the model for RDR's certificate |
| Certified distances | `MeshHausdorffDistance`, `TriangleDeviationBounds.IsWithin`, `PolyhedralEnvelope` | the independent final check of the band (section 5.2 proves it; the check guards the implementation) |
| Validity and measures | `MeshRobustRepair.NotCleanReason(s)`, `MeshTopologySummary`, `TriangleQualityStatistics`, `MeshFidelityMetrics` | input precondition (clean), result checks of the tests and benchmarks |
| lfs-like estimates | `MeshWallThickness` (InscribedSphere), `RemeshSizing.FromWallThickness` | an initial sizing field and a default for r_max; an estimate, never a premise |

Missing: the bisector-plane predicate family (section 6); restricted Voronoi bookkeeping (which Delaunay facets,
edges and cells are restricted, per cavity update); the TBP checks on Voronoi cells; feature detection with a feature
angle and crease-curve sampling (stage 2); a weighted (regular) `Delaunay3` with an exact power test (stage 2, only if
unweighted crease sampling is not enough); a certificate result type.

## 2. Prior art: theorems, guarantees, hypotheses

Read status: **R** read in this study (abstract or summary pages), **S** from a restating source (search summaries,
the textbook's statements as restated by CONTOURING_TOPOLOGY.md, or the library's own notes), **M** from memory, not
re-read. Constants and exact hypotheses marked M or S must be checked against the papers before stage 0 relies on
them. All papers only; CGAL (Mesh_3, surface mesher) and the DelPSC software appear as papers only, no code read.

| # | Source | Guarantee | Hypotheses | PL input? |
|---|---|---|---|---|
| T1 | H. Edelsbrunner, N. Shah, *Triangulating topological spaces*, IJCGA 7(4), 1997 (M) | if Vor(P) has the **closed ball property** (TBP) w.r.t. a manifold S, the restricted Delaunay complex Del(P)\|S is homeomorphic to S | TBP: every Voronoi cell, facet, edge meets S in a closed ball of dimension 2, 1, 0 or not at all (with general position); whether P must lie on S: check (M) | **yes**: needs no lfs; checkable exactly on PL S (section 6) |
| T2 | N. Amenta, M. Bern, *Surface reconstruction by Voronoi filtering*, DCG 22, 1999 (M); Boissonnat-Oudot 2005 (below) | an epsilon-sample (every x on S has a sample within eps lfs(x)) of a smooth S with eps small (about 0.1; book: 0.08, S) has the TBP | C2 (or C1,1) surface, lfs > 0 | **no** (lfs = 0 on creases) |
| T3 | J.-D. Boissonnat, S. Oudot, *Provably good sampling and meshing of surfaces*, Graphical Models 67(5), 2005 (in SOURCES.md; S) | Del(P)\|S of a loose eps-sample is **isotopic** to S, Hausdorff O(eps^2 lfs), normals O(eps); the refinement (insert the centre of a bad surface Delaunay ball) terminates with O(integral of 1 / lfs^2) points | smooth S, an oracle for segment-S intersection, a sizing below eps lfs | no as stated; the refinement skeleton is ours already (alpha wrap) |
| T4 | L. P. Chew, *Guaranteed-quality mesh generation for curved surfaces*, SoCG 1993 (in SOURCES.md) | radius-edge ratio at most rho for every restricted facet, so min angle at least arcsin(1 / (2 rho)), 30 degrees for rho = 1; termination for rho >= 1 by the insertion-radius argument | smooth surface, sizing below lfs | the angle part carries over to PL with h_min (section 5.4) |
| T5 | S.-W. Cheng, T. K. Dey, E. A. Ramos, T. Ray, *Sampling and meshing a surface with guaranteed topology and geometry*, SoCG 2004 / SIAM J. Comput. 2007 (S) | homeomorphism **without knowing lfs**: refine until the TBP holds (checked), then T1 | smooth S (termination: an eps-sample is eventually reached); critical-point / TBP computations | the TBP enforcement carries over; termination does not (section 4) |
| T6 | S.-W. Cheng, T. K. Dey, E. A. Ramos, *Delaunay refinement for piecewise smooth complexes*, SODA 2007 / DCG 43, 2010 (S) | for a piecewise smooth complex (PSC): protect 0- and 1-cells by weighted points (balls), refine until an **extended TBP** holds; output homeomorphic, stratum by stratum; no restriction on input angles | the PSC's features given; weighted Delaunay; exact TBP checks | **yes in principle** (a faceted mesh is a PSC); every crease is a 1-cell |
| T7 | S.-W. Cheng, T. K. Dey, J. A. Levine, *A practical Delaunay meshing algorithm for a large class of domains*, IMR 2007 (DelPSC) (S, R abstract) | as T6, a practical version; surfaces and volumes; DelPSC software takes a polygonal mesh of a PSC | as T6 | yes; the closest prior art for stage 2 |
| T8 | T. K. Dey, J. A. Levine, *Delaunay meshing of piecewise smooth complexes without expensive predicates*, 2008 manuscript / IMR 2009 (S) | avoids the expensive TBP checks with a new notion of local feature size and a generalized TBP proof | as T6 | an option if the exact TBP checks are too slow (stage 1 stop rule) |
| T9 | J.-D. Boissonnat, S. Oudot, *Provably good sampling and meshing of Lipschitz surfaces*, SoCG 2006 (M) | isotopy / Hausdorff for Lipschitz surfaces (sharp features allowed, bounded "Lipschitz radius"), no protection | Lipschitz radius > 0; dihedral angles not too sharp | a candidate for moderate creases without protection (M; check) |
| T10 | S.-W. Cheng, T. K. Dey, J. R. Shewchuk, *Delaunay Mesh Generation*, CRC 2012, ch. 12-15 (smooth surfaces, PSCs; chapter numbers M) | the textbook statements of T1-T7, with constants and proofs | | the reference for stage 0 |
| T11 | F. Chazal, D. Cohen-Steiner, *A condition for isotopic approximation*, Symposium on Solid Modeling 2004, pp. 93-99, doi 10.2312/sm.20041380 (R, abstract) | homeomorphic surfaces are isotopic if one lies in a topological thickening of the other and separates its sides | that the band is a thickening (reach / weak feature size) | a route to isotopy if a wfs bound or a certified thickening exists (section 5.3) |
| T12 | F. Chazal, D. Cohen-Steiner, A. Lieutier, *A sampling theory for compact sets in Euclidean space*, DCG 41, 2009 (M) | weak feature size and mu-reach; positive for polyhedra | critical points of the distance function | defines what replaces lfs; no cheap certified bound found |
| T13 | J. Ruppert 1995; J. R. Shewchuk, *Delaunay refinement algorithms for triangular mesh generation*, CGTA 2002 (in SOURCES.md) | 2D termination and angle bounds, small input angles (concentric shells) | | the 2D proofs `QualityMesh2` implements; pattern for crease protection |
| T14 | D.-M. Yan, B. Lévy, Y. Liu, F. Sun, W. Wang, *Isotropic remeshing with fast and exact computation of restricted Voronoi diagram*, SGP 2009 (M) | restricted Voronoi diagram of a PL surface computed exactly by clipping input triangles; CVT quality measured, no topology proof | | the computation of restricted Voronoi cells on a PL surface (used in a TBP check alternative) |

How others solve it: guaranteed surface meshing with topology and quality is done by restricted Delaunay refinement
with a sizing tied to lfs on smooth surfaces (Boissonnat-Oudot; CGAL's surface mesher as a paper), by TBP enforcement
when lfs is unknown (Cheng-Dey-Ramos-Ray), and with protecting balls on sharp features (DelPSC; CGAL Mesh_3's
protection as a paper). Explicit-mesh remeshers (Botsch-Kobbelt, Hu 2017, Brochu-Bridson) give per-operation error
and intersection guarantees but no topology or angle theorems.

## 3. The hypotheses, one by one, for a triangle-mesh input

| Hypothesis of the theorems | On a clean closed triangle mesh S | What replaces it |
|---|---|---|
| S smooth (C2, or C1,1) | false on every edge with a dihedral angle != 180 deg | treat S as a PSC: creases above a feature angle theta_f are 1-cells (protected, stage 2); creases below it are left to the TBP checks (stage 1) |
| lfs(x) > 0, sizing below eps lfs | lfs = 0 on every crease, so no eps-sample exists near one | TBP checked instead (T1, T5); a user minimum size h_min; regions that fail the TBP at h_min are listed, not proved |
| an intersection oracle for segment-surface | exact for PL S: a segment (a Voronoi edge) meets an input triangle, decided by signs (section 6) | none needed: PL is the easy case for the oracle |
| samples on S | an inserted point is the surface Delaunay ball centre, an implicit point on an input triangle, rounded to doubles: off S by up to half an ulp | check T1's hypothesis (M); if P must lie on S, keep the combinatorics on the rounded points and prove T1 for points near S (stage 0), or check the result exactly at the end (as the boolean's rounding) |
| general position (no five points cospherical, no Voronoi vertex on S, no input vertex on a Voronoi facet) | violated by CAD inputs (axis-aligned faces, symmetric samples) | Simulation of Simplicity on the input's doubles (as `ExactRayTriangle`, terrain overlay); or refuse the element and perturb the inserted point's rounding |
| features given (T6, T7) | not given | detected: edges with a dihedral deviation above theta_f, vertices with two crease edges meeting at an angle or with three or more crease edges (corners), crease curves as chains |

Why a feature angle is needed at all (scale argument, to be proved in stage 0): near a crease of dihedral angle phi,
S is a wedge, invariant under scaling about a point of the crease. If the Voronoi cell of a sample on one face meets
the other face in a second piece (the TBP fails) for some configuration, the scaled configuration fails the same way,
so refinement alone cannot make the TBP hold near a sharp enough crease: there is a threshold angle below which
points must be put on the crease itself (as T6 does). For mild creases (phi near 180 deg) the Voronoi cells of nearby
samples meet the wedge in disks and refinement terminates. Measuring the threshold on wedges is stage 1's first probe.

## 4. Algorithm (stage 1: clean closed input, creases below theta_f only)

Input: a clean closed mesh S (`NotCleanReason` empty), r_max (largest surface Delaunay ball radius, the size and
band parameter), rho (radius-edge bound, default 1), h_min (floor, default r_max / 16). Output: the restricted
Delaunay triangulation D = Del(P)\|S, a certificate (section 5) and the list of uncertified elements.

1. `Delaunay3` on a box around S enlarged by a margin (the 8 corners keep every interior Voronoi edge bounded).
2. Initial sample: a deterministic h_min-separated subset of the input vertices (or points from a grid in vertex-id
   order), enough to meet every component (one per component at least, plus the TBP refinement does the rest).
3. Restricted elements: a Delaunay facet is restricted if its dual Voronoi edge (between the circumcentres of its two
   cells) meets S; each meeting point is a surface Delaunay ball centre c, radius |c - a|. Kept incrementally from the
   cavity lists of `Insert`.
4. Refinement queue, worst first with a deterministic key (squared radius, then the sorted vertex ids):
   - R1 (TBP): a Voronoi edge of a restricted facet meets S more than once; a Voronoi facet meets S in something other
     than one arc; a Voronoi cell of a sample meets S in something other than one disk. Insert the farthest
     intersection point of the violating element from its sample (a surface Delaunay ball centre or a restricted
     Voronoi vertex), as T5 does (S: the choice rule to be fixed in stage 0).
   - R2 (size and band): a surface Delaunay ball with radius above r_max; a restricted Voronoi region (Vor(p) meets S)
     with a point farther than r_max from p (the farthest point of a convex cell's intersection with a planar piece is
     at a vertex of that polygon, so the test runs over finitely many implicit points).
   - R3 (quality): a restricted facet with circumradius / shortest edge above rho; insert its surface Delaunay ball
     centre.
   - Separation: an insertion closer than h_min to an existing sample is refused (`Insert`'s `minSeparation`); the
     element stays and is listed as uncertified.
5. Stop when the queue is empty. D is the set of restricted facets.

Termination and size bound (to be written as proofs in stage 0, after the alpha wrap's argument): every inserted point
is at least h_min from every sample (rule 4 separation, up to the rounding of the insertion, a relative slack of a few
ulp), so the balls of radius h_min / 2 around the samples are disjoint and lie in the h_min / 2 offset of S. By
Steiner's formula for the offset of a polyhedral surface, its volume is at most area(S) h_min + O(h_min^2 crease
length) + O(h_min^3 vertices), so |P| <= C (area(S) / h_min^2 + total crease length / h_min + input vertices), finite,
and every step inserts one point or lists one element: the refinement terminates. Where lfs is large compared with
r_max (smooth-ish parts) the classical insertion-radius argument (T3, T4 with rho >= 1) gives the better bound
O(area / r_max^2) and no element reaches h_min; the share that does is measured.

Determinism: insertion is serial in queue order with the deterministic key; the tests of the new elements of a cavity
(restricted-facet tests, TBP checks) are pure functions of the triangulation and may run in parallel, written into
per-element slots and merged in element-id order, so parallel = serial bit for bit (the pattern of the alpha wrap and
the boolean). The stochastic walk of `Delaunay3` must use a fixed seed (to check).

2^k invariance: every decision is an exact sign of a homogeneous polynomial in the coordinates, so it is unchanged by
a 2^k scaling (inside the supported input range); the constructed centres (Cramer's rule with + - * /) and squared
radii scale exactly, so the queue order and the rounded insertions scale exactly; r_max and h_min are lengths and are
scaled with the input in the invariance test. Square roots only in reported values, never in decisions.

## 5. Guarantees: what is proved, what is checked, what is measured

### 5.1 By construction (proved)

- **No self-intersections, consistent embedding**: D is a subcomplex of the exact Delaunay triangulation of the double
  points P, so two output triangles meet only in a common edge or vertex. No rounding after the fact (the vertices are
  the inserted doubles), unlike the boolean's rounding.
- **Closed 2-manifold, homeomorphic to S**: by T1 when no element is uncertified (TBP checked exactly for every
  restricted element). Orientation from S (the side of the dual edge's crossing).
- **Partial statement** when elements are uncertified: the analogue of the contouring note's Theorem 1b (homeomorphic
  outside the listed Voronoi cells); to be proved in stage 0. With uncertified elements D may be non-manifold there;
  the result then reports `TopologyCertified = false` and the cells.

### 5.2 Hausdorff band (proved, both directions)

- D within r_max of S: every point of an output facet lies in one of its surface Delaunay balls (the facet's
  circumdisk is inside every ball through its vertices whose centre is on the dual line), whose centre is on S and
  radius at most r_max (R2).
- S within r_max of D: every x on S lies in a restricted Voronoi region of some sample p, at distance at most r_max
  from p (R2), and p is a vertex of D (a sample whose region is a disk under the TBP is a vertex of a restricted
  facet). So the two-sided Hausdorff distance is at most r_max. `MeshHausdorffDistance` re-checks it in the tests.

### 5.3 Isotopy (open for PL inputs)

T3 proves isotopy for smooth S through the normal projection (needs the reach). For a PL S no published theorem was
found (terms: "restricted Delaunay isotopic polyhedral surface", "isotopy topological ball property"). Routes for the
stage-0 proof attempt, in order: (a) the grid lemma of CONTOURING_TOPOLOGY.md Theorem 1 holds for any complex of
convex cells, so S and D are isotopic if both are compatible with the Voronoi complex with the same pattern; S is
compatible by the TBP, D in general is not (a restricted facet meets its own dual edge only if its circumcentre is inside
it): it needs an intermediate surface or a refined complex; (b) T11 with the union of the surface Delaunay balls as
the thickening; (c) the elementary-move argument of section 10. Until one is proved and reviewed, the stated guarantee
is homeomorphism, and nesting and linking are checked only as far as `MeshTopologySummary` and the exact nesting
tests go.

The post-hoc grid certificate (comparing the crossing patterns of S and D on a box grid) was probed and is not
practical (section 9).

### 5.4 Quality (proved with exemptions)

After R3, every restricted facet has circumradius / shortest edge at most rho, so its smallest angle is at least
arcsin(1 / (2 rho)) (30 deg at rho = 1), except facets whose refinement was refused by h_min or that touch a protected
feature (stage 2; T6 gives no angle bound near small input angles, M). The exempt facets are listed and counted; the
share is measured. This is the alpha wrap's statement with h_min in place of the offset separation.

### 5.5 Checked or measured, not proved

| Item | How |
|---|---|
| implementation of the proofs | every success-path test checks `NotCleanReason` empty, genus and components equal to the input, `MeshHausdorffDistance.UpperBound <= r_max`, the angle bound on the non-exempt facets |
| the TBP premise when samples are rounded off S | stage 0 (T1's hypotheses); until then the final checks above |
| uncertified share, output size against the bound, p01 angle, time | benchmark (section 7) |

## 6. Predicates and building blocks

Notation: a, b, c, d, e samples (doubles), t a triangle of S with vertices t0, t1, t2 (doubles). The bisector plane of
a and b: n = 2 (b - a) (degree 1), offset |b|^2 - |a|^2 (degree 2). The plane of t: normal (degree 2), offset (degree
3).

| Need | Formulation | Degree (homogeneous, estimate) | Covered by |
|---|---|---|---|
| Delaunay3 insertion, location, conflict | `Orient3d`, `InSphere` on doubles | 3, 4 | `Delaunay3` (exists) |
| restricted facet: the dual Voronoi edge of abc meets t | the point x = (bisector(a, b)) ∩ (bisector(a, c)) ∩ plane(t), a three-plane point (TPI-like, numerators degree 5, denominator degree 4); x on the dual edge iff x is closer to a than to d and than to e (d, e the apexes of the two cells): the sign of bisector(a, d) at x, degree 6; x in t: orient2d of x with two vertices of t in t's dominant projection, degree 6 | 6 | **new**: a "bisector TPI" point kind in `tools/PredicateCompiler` (planes given by coefficients instead of three points); filter and bounds from the generator; fallback `FixedArith` / `BigInteger` |
| surface Delaunay ball centre and radius (for insertion and the queue) | x in floating point (Cramer's rule), squared radius | construction | float; the decision whether to insert is exact (the queue key may be approximate without breaking proofs, the rules R1-R3 are decided exactly) |
| TBP (E'): a Voronoi edge meets S at most once | count the triangles t for which the test above holds; a Voronoi edge that hits a mesh edge or vertex is a degeneracy (SoS) | 6 | new family + candidate search |
| TBP (F'): a Voronoi facet (dual of Delaunay edge ab: the polygon on bisector(a, b) bounded by Voronoi edges) meets S in one arc | Theorem 3's counting on a convex polygon: mesh edges crossing the polygon (endpoint signs against bisector(a, b): degree 2 on doubles; the crossing LPI point inside the polygon: bisector signs at an LPI point, degree about 3-4) and triangles crossed by the polygon's edges (row above); per part \|T\| - \|M\| = 1 | 2-6 | new family; the counting code of the contouring certificate |
| TBP (C'): a Voronoi cell meets S in one disk | Theorem 3's counting on a convex cell: mesh vertices in the cell (nearest-sample tests, degree 2), edges and triangles from (E'), (F'); per part \|T\| - \|M\| + \|V\| = 1 | 2-6 | as above; **the lemma holds for any convex X** (CONTOURING_TOPOLOGY.md 7.4), so it carries over unchanged |
| R2 farthest point of a restricted Voronoi region | distances from p to the region's vertices (restricted Voronoi vertices = the TPI points above, mesh vertices, LPI points of mesh edges with Voronoi facets) against r_max | 6-8 | comparison of a squared distance at an implicit point with r_max^2: new, same family |
| candidate search (triangles near a Voronoi edge or cell) | boxes; exact code | - | `TriangleBoxTree` (internal, exact code) or `DMeshAABBTree3` with conservative padding |
| degeneracies | Simulation of Simplicity on the input's doubles | - | the pattern of `ExactRayTriangle` / terrain overlay; per predicate in the generator (new work) |
| stage 2: weighted points (protecting balls) | regular triangulation: power test (lifted orient4d, degree 5) | 5 | **new**: weighted `Delaunay3`; only if unweighted crease sampling fails |
| stage 2: crease curves | 1D refinement along chains of input edges (points on input edges: rounded, on the edge up to an ulp) | - | the 2D segment encroachment pattern of `QualityMesh2` |

The exact files (new predicates, the restricted bookkeeping) fall under the `.editorconfig` rules: no FMA, no `float`,
no parallel loop without review; the parallel tests of section 4 are outside the exact kernels (they call them) or go
into `core/ExactCodeAllowList.cs` with a review.

## 7. Cost and upper bound

No direct measurement yet; the bound is from the alpha wrap, which does the same kind of work (a `Delaunay3`
refinement with surface tests per facet): about 100 us per raw triangle (median raw wrap 3.7 s; alpha-wrap roadmap
WP1), floating-point surface tests. RDR has fewer cells (points only on S, not in a volume band) but exact tests and
the TBP counting per cell; estimate 50-300 us per output vertex (2 output triangles per vertex), so 100k output
triangles in about 2.5-15 s. Against the `Remesher`: 11-23 us per triangle plain, about 280 us with
`PreventSelfIntersections` (REDUCER_REMESHER_20.md section 2). So RDR is expected between the plain and the guarded
Remesher, with stronger guarantees. Stage 1's first step is a float-predicate spike that replaces this estimate
before any exact code is written (upper bound first, AGENT_WORKFLOW section 3).

## 8. Test plan (repo style)

- **Procedural inputs only** (no mesh files): sphere, ellipsoid, torus, thin torus, two nested spheres, a linked pair of
  tori, a bumpy sphere (smooth-ish); stage 2: box, cylinder, wedges of dihedral angle 30-170 deg, a cone tip.
- **Success path checks the result**: `NotCleanReason` empty; genus and components equal; `TopologyCertified` and an
  empty uncertified list on the smooth set; `MeshHausdorffDistance.UpperBound <= r_max`; min angle >= arcsin(1/(2 rho))
  on the non-exempt facets; output size below the packing bound.
- **Predicate tests**: the new family against an exact rational oracle (`BigInteger`) at many scales and in degenerate
  positions (cospherical samples, a Voronoi vertex on a triangle, a mesh edge on a bisector), as the indirect predicates'
  tests; the generator's bound table checked by `GeneratedPredicatesTests`.
- **TBP counterexamples**: hand-built configurations where each of (E'), (F'), (C') fails (a Voronoi edge crossing a
  thin slab twice, a facet meeting S in a closed curve, a cell containing a handle); the check must refuse each; mutants
  that drop one clause must fail a test.
- **Invariance over `Similarity.All()`**: 2^k scales bit for bit (r_max, h_min scaled); rotations and translations give
  the same certificate booleans, genus and the angle bound (counts within a tolerance).
- **Reuse**: `Compute()` twice on one instance, same bits.
- **Determinism**: parallel tests = serial, repeated runs same bits, any thread count.
- **Mutation**: the new exact files in their own Stryker shard (threshold set once locally), incremental runs on PRs;
  hand mutants for R1-R3 and the separation rule.
- **Fuzz**: random clean meshes from the harness generators; oracle "certificate true implies the result checks hold".
- **Time**: tests on inputs of at most a few thousand output triangles (5 s rule); the corpus (the 20 Thingi10K
  meshes of `RemeshBenchmark`, fetched at run time) is a benchmark with before / after numbers against the `Remesher`.

## 9. Probe: a post-hoc grid isotopy certificate on remesher output (throwaway, not committed)

Question: can the contouring certificate's grid lemma certify isotopy of a remesh after the fact (S and the output D
compatible with one box grid with the same pattern)? Setup: the spike's exact cell test (`LevelSetCertificate`,
commit fe9f3361 of `claude/contouring-certificate-stage1`, copied, winding classification removed); the crossings of S
define the pattern D must match. Master d2e02a14, net10.0, 4 cores; procedural closed inputs rotated by 23.7 deg about
(0.3, 0.7, 0.2): sphere (18k triangles), bumpy sphere (42k), torus R 1 r 0.3 (15k), thin torus r 0.04 (13k);
`Remesher` with `MeshProjectionTarget.Auto`, `MinTriangleQuality = 0.2`, 15 passes (`PreventSelfIntersections` on for
the thin torus); grid cell diag / N, N in {8, 12, 16, 24}, 8 random offsets.

| case | L / diag | out tris | new SI pairs | Hausdorff / diag | offsets certified at N = 8 / 12 / 16 / 24 |
|---|---:|---:|---:|---:|---|
| sphere | 0.01 | 33360 | 0 | 0.00031 | 5 / 4 / 2 / 1 |
| sphere | 0.02 | 7264 | 0 | 0.00089 | 5 / 3 / 0 / 0 |
| sphere | 0.05 | 1136 | 0 | 0.00405 | 0 / 0 / 0 / 0 |
| bumpy | 0.01 | 30758 | 0 | 0.00108 | 0 / 1 / 1 / 0 |
| bumpy | 0.02, 0.05 | 7264, 1130 | 0 | 0.0029, 0.0118 | 0 everywhere |
| torus | 0.01, 0.02, 0.05 | 24390, 5328, 894 | 0, **6**, 0 | 0.0006-0.017 | 0 everywhere |
| thin torus | 0.01-0.05 | 4656-552 | 0 | 0.0036-0.024 | 0 everywhere |

0 of 8 offsets certified in 40 of 48 settings. The refusals of D are almost all sign mismatches (a grid node between
S and D); the input's own refusals are grid edges crossed twice near silhouettes and, for the thin torus, cells larger
than the tube. Expected nodes in the band: about area x mean deviation / h^3, and refinement adds nodes to the band, so
the window between "coarse against the deviation" and "fine against the features" is empty for most settings: **not
practical**. Side finding: the plain `Remesher` at `MinTriangleQuality = 0.2` created 6 self-intersecting pairs on a
clean torus (L 0.02 diag) and 63-460 on the thin torus without `PreventSelfIntersections`.

## 10. Side path: guard the existing Remesher (cheap, independent)

For callers who keep the Botsch-Kobbelt remesher: closed, manifold and free of self-intersections already hold with
`PreventSelfIntersections`; add (1) the out -> in band guard `IsWithin(delta)` on new triangles (as the Reducer's
`MaxDeviation` and `AlphaWrapQualityPass`), (2) a `RemeshCertificate` result (exact SI check, certified two-sided
Hausdorff, genus / components / nesting), and optionally (3) an isotopy guard: every flip, collapse, split and vertex
move must be an elementary move across empty tetrahedra (a flip across abcd, a move v -> v' across the tetrahedra
(v, v', a, b) of its star; exact `Orient3d` triangle-tetrahedron tests), which makes the result ambient isotopic to a
clean input by construction (PL elementary moves, Rourke-Sanderson 1972, M; the remeshing practice of T. Brochu,
R. Bridson, *Robust topological operations for dynamic explicit surfaces*, SIAM J. Sci. Comput. 31(4), 2009, S, and
T. Brochu, E. Edwards, R. Bridson, *Efficient geometrically exact continuous collision detection*, SIGGRAPH 2012, S).
(1)-(2) tier S; (3) tier M with a cost bound of 1-4 x today's `PreventSelfIntersections` check (the same candidate
search, triangle-tetrahedron instead of triangle-triangle tests), and the collapse lemma still to prove. No angle
bound: only monotone (the min angle does not drop, Hu et al. 2017). Independent of the RDR work; can run first if a
caller needs it.

## 11. Staged plan

| Stage | Content | Tier | Stop rule / gate |
|---|---|---|---|
| 0 | Proofs written out (docs, as CONTOURING_TOPOLOGY.md section 7): T1's hypotheses checked against the paper (samples off S by rounding), termination and size bound with h_min, the band proofs of 5.2, the angle statement of 5.4, the partial statement for uncertified elements, the wedge threshold argument of section 3, the isotopy attempt of 5.3. Reviewed by a fresh session with the note only | S-M | a hypothesis that fails (eg T1 needs P on S and no repair argument): report before stage 1 |
| 1a | Spike, float predicates, benchmark project only (no library code): RDR with R1-R3 on the smooth procedural set and wedges; measure time per output vertex, uncertified share at h_min = r_max / 16, the TBP threshold angle on wedges | M | stop and report if the median exceeds 300 us per output vertex, or more than 1 % of restricted facets on the smooth set stay uncertified, or the wedge threshold is above 150 deg (stage 1 would then cover almost no real mesh without stage 2) |
| 1b | Exact version in the library, internal first: the bisector-TPI predicate family from the generator (bounds review), the restricted bookkeeping, TBP counting, certificate, tests of section 8; public API (`RestrictedDelaunayRemesh`, options + `Compute()` + result with status) after the review | L | exact-code review of every new bound; incremental mutation run above threshold; time within 2 x the spike |
| 2 | Feature protection: feature detection (theta_f), crease-curve sampling (1D encroachment), corners; weighted `Delaunay3` (power test) only if the unweighted protection fails the wedge tests; extended TBP (T6) | L | stop and report if the unweighted variant fails below the wedge threshold measured in 1a and the weighted kernel's upper bound (a spike) exceeds 2 x stage 1's time |
| 3 | Benchmark against the `Remesher` (plain, with `PreventSelfIntersections`) on the 20 Thingi10K meshes: guarantees, p01 angle, Hausdorff, size, time | S | numbers in the benchmark README; no default changes |

## 12. Risks

- **T1 with rounded samples**: if the theorem needs samples on S, rounding breaks the premise; repair needs a
  perturbation argument (stage 0). Fallback: exact final checks (closed, manifold, no self-intersections, genus) and
  report homeomorphism only as "checked".
- **Faceted real meshes are mostly creases**: on 30 degree creases, 35-94 % of the vertices of the Remesher's worst
  meshes were feature vertices (REDUCER_REMESHER_20.md section 13); stage 1 alone may cover few real inputs, so stage 2
  carries the practical value. The wedge threshold of 1a decides how much.
- **Predicate degree and cost**: degree 6-8 with `BigInteger` fallbacks in degenerate CAD positions; the generator
  bounds them, but the slow tail may be large (as the boolean's rounding was). Stop rules of 1a / 1b.
- **TBP checks are the expensive part** (T8 exists because of it); if too slow, the Dey-Levine variant (paper only).
- **Isotopy stays open**: the owner should expect homeomorphism plus the band, not isotopy, unless stage 0 succeeds.
- **No output-size control by count**: the output size follows r_max, rho and the surface; a triangle-count target is
  not a guarantee of this method.
- **Small input angles at features** (stage 2): no angle bound there in T6 (M); exemptions as in `QualityMesh2`.

## 13. Decisions for the owner

1. RDR as the main route, staged as in section 11 (stage 0 proofs and review first, stage 1a spike with its stop
   rules before any library code)? Leaning: yes.
2. The stated guarantee: homeomorphism + two-sided band r_max + radius-edge bound with listed exemptions, and isotopy
   only if stage 0's proof succeeds? Leaning: yes.
3. Inputs: clean closed meshes only (broken inputs go through `MeshRobustRepair` first)? Leaning: yes.
4. Side path (section 10): build the band guard and `RemeshCertificate` for the existing `Remesher` now (tier S),
   the isotopy guard later or not? Leaning: certificate now only if a caller needs it; otherwise after stage 1a.
5. Order against the running work (Exact Boolean 3.0, Alpha Wrap 3.0, contouring certificate stages 1-2): stage 0 now
   (docs); stage 1 after contouring certificate stage 1, whose counting kernel it reuses. Leaning: yes.
