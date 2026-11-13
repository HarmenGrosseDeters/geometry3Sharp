# Certified surface remeshing by restricted Delaunay refinement (study)

Status: **study; decided (coordinator under the owner's mandate, 2026-11-13): RDR, staged as in section 11; stage 0
(the proofs, section 14) written and reviewed by a fresh session (2026-11-13; one must-fix and six should-fixes worked
in, section 14.7).** Nothing built. Docs only. The probe of section 9
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
  protected features. **Isotopy**: no published theorem for PL inputs without lfs; stage 0 proves (section 14.3) that
  under the checked TBP the input is ambient isotopic to an explicit intermediate surface S2 (the "dual quad" surface on
  the samples and the surface Delaunay ball centres), and reduces isotopy of the output D to the emptiness of explicit
  tetrahedra between S2 and D (a sufficient, checkable condition). Isotopy is claimed only where that check passes and
  only after review.
- **Predicates** (section 6): one new family covers the restricted-Delaunay test, the surface Delaunay balls and the TBP
  checks: signs of linear forms (bisector planes, the input triangle's planes and edge lines) at the intersection point
  of two bisector planes with a triangle's plane, a three-plane point of degree 5 / 4 whose tests have degree 6, lower
  than the TPI orient3d (degree 9) the predicate compiler already handles; every test the certificate needs has
  degree at most 6 (section 14.4). `Delaunay3`, `ExactPredicates`, the
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
- **Closed 2-manifold, homeomorphic to S**: Theorem A (section 14.2, the argument of T1 written out for this setting)
  when no element is uncertified (TBP checked exactly for every restricted element). Orientation from S (the side of the dual edge's crossing).
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

### 5.3 Isotopy (for PL inputs: section 14.3)

T3 proves isotopy for smooth S through the normal projection (needs the reach). For a PL S no published theorem was
found (terms: "restricted Delaunay isotopic polyhedral surface", "isotopy topological ball property"). Stage 0 proves
(Theorem B, 14.3) that under the checked TBP the input S is ambient isotopic to the dual quad surface S2 (vertices: the
samples and the surface Delaunay ball centres), using the grid lemma of CONTOURING_TOPOLOGY.md Theorem 1 on the
Voronoi complex. From S2 to the output D: (a) explicit moves across tetrahedra, a sufficient condition checked per
tetrahedron (recommended; cost measured in 1a); (b) Chazal-Cohen-Steiner with a certified weak-feature-size bound (a
separate study); (c) the Remesher move guard, the same mechanism, not an RDR route. Isotopy of D is claimed only
where (a) passes, and only after review; otherwise the guarantee is homeomorphism (Theorem A).

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
| restricted facet: the dual Voronoi edge of abc meets t | the point x = (bisector(a, b)) ∩ (bisector(a, c)) ∩ plane(t), a three-plane point (TPI-like, numerators degree 5, denominator degree 4); x on the dual edge iff x is closer to a than to d and than to e (d, e the apexes of the two cells): the sign of bisector(a, d) at x, degree 6; x in t: orient2d of x with two vertices of t in t's dominant projection, degree 6 | 6 | **new**: `ipredicate`s in `tools/PredicateCompiler/indirect.spec` (straight-line formulas on the input points, as `Orient3dLPI` / `Orient3dTPI`; no generator extension needed, section 14.4); filter and bound from the generator, checked by `GeneratedPredicatesTests`; fallback `FixedArith` / `BigInteger` |
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

- **T1 with rounded samples**: resolved by stage 0: the homeomorphism proof of section 14.2 does not use samples on S
  (only the TBP pattern and general position), so rounding the inserted points does not break it.
- **Faceted real meshes are mostly creases**: on 30 degree creases, 35-94 % of the vertices of the Remesher's worst
  meshes were feature vertices (REDUCER_REMESHER_20.md section 13); stage 1 alone may cover few real inputs, so stage 2
  carries the practical value. The wedge threshold of 1a decides how much.
- **Predicate degree and cost**: degree 6-8 with `BigInteger` fallbacks in degenerate CAD positions; the generator
  bounds them, but the slow tail may be large (as the boolean's rounding was). Stop rules of 1a / 1b.
- **TBP checks are the expensive part** (T8 exists because of it); if too slow, the Dey-Levine variant (paper only).
- **Isotopy is conditional**: proved for the intermediate surface S2; for the output D only where the explicit move
  check of section 14.3 passes, whose exact tests have high degree (rational fallback); its pass rate and cost are a
  stage-1a measurement.
- **No output-size control by count**: the output size follows r_max, rho and the surface; a triangle-count target is
  not a guarantee of this method.
- **Small input angles at features** (stage 2): no angle bound there in T6 (M); exemptions as in `QualityMesh2`.

## 13. Decisions (coordinator under the owner's mandate, 2026-11-13)

1. RDR staged as in section 11, with the stop rules of stage 1a; stage 1a is a float spike in a benchmark project, not
   library code.
2. The guarantee is homeomorphism + two-sided band + radius-edge bound with listed exemptions; isotopy is claimed only
   once proved and reviewed (section 14.3: where route (a)'s check passes).
3. Clean closed inputs only, for now; other inputs are refused with a status, no repair inside.
4. The Remesher side path (section 10) comes after RDR stage 1a; the torus self-intersection finding is #790.
5. Stage 0 now (section 14); the coordinator routes the fresh-session review. Stage 1 waits for contouring-certificate
   stage 1. New predicates go through the PredicateCompiler with a checked error bound; the exact-code rules apply.
   Papers only (no CGAL).

## 14. Stage 0: the proofs

Everything here is about a clean closed PL surface S (a compact 2-manifold, possibly with several components, embedded,
triangles of positive area) strictly inside an axis-aligned box B, and a finite set P of sites strictly inside B
together with the 8 corners of a larger box B' (the corners of `Delaunay3`'s box), so that every Voronoi cell of a
site of P is bounded. K is the Voronoi complex of P clipped to B': its 3-cells V(p) (convex polytopes), 2-cells
(*facets* F_pq = V(p) ∩ V(q), dual to Delaunay edges pq), 1-cells (*Voronoi edges* e_pqr, dual to Delaunay triangles
pqr) and 0-cells (Voronoi vertices, dual to tetrahedra). Tools from CONTOURING_TOPOLOGY.md section 7: Theorem 1 (the
grid lemma) and its Remark 1 (any complex of convex cells whose boundaries are unions of lower cells), and the
counting lemma of section 7.4 (stated there for any compact convex X). Status of every statement: written in this
study, **not reviewed yet**.

### 14.1 The conditions

**(G) General position**, for the elements that S meets or touches. A Voronoi vertex is *near* S if one of the cells
incident to it meets S; "the triangles near it" are all triangles of S that meet one of those cells.

1. every Voronoi vertex near S is in exactly 4 cells and every Voronoi edge of a cell that meets S in exactly 3 (no 5
   cospherical sites whose empty sphere's centre is near S): a zero of `InSphere` between a cell and a neighbour's
   apex is a violation;
2. S contains no Voronoi vertex (P5 of 14.4 is not 0 for the triangles near it);
3. no Voronoi edge meets S in a mesh edge or vertex, and none lies in the plane of a triangle it meets (the
   three-plane point of P1 / P2 exists, d != 0, and P2 is not 0);
4. no mesh vertex lies on a Voronoi facet (P3 is not 0), and no mesh edge lies in the plane of a facet it meets (P3
   is not 0 at both ends of it).

(G3) and (G4) exclude tangencies: S meets every Voronoi edge only at interior points of triangles, crossing them, and
meets every facet and cell transversally (no mesh vertex on a facet, no mesh edge or triangle in a facet's plane where
they meet). A violated element is uncertified (stage 1b may replace this by Simulation of Simplicity on the input's
doubles; the statements below then hold for the perturbed configuration).

**(TBP) The topological ball property, as checked**:

- (E') every Voronoi edge meets S in at most one point;
- (F') every facet meets S in nothing or in one arc;
- (C') every cell V(p), p a site, meets S in nothing or in one closed disk;
- (R0) S meets no cell of a corner of B' (used only so that no corner becomes a vertex of D).

**Lemma 1 (the checks are exact counts).** Under (G), (E') holds for e exactly if at most one triangle of S meets e
(P1 and P2); (F') holds for F exactly if the triangles meeting F and the mesh edges crossing F form one part with
|T| - |M| = 1; (C') holds for V(p) exactly if the triangles, mesh edges and mesh vertices meeting it form one part
with |T| - |M| + |V| = 1. Every membership in these counts is decided by P1-P4 of 14.4.

*Proof.* Under (G), S meets a facet in a compact 1-manifold with boundary on the facet's edges and a cell in a compact
2-manifold with boundary on its boundary (the argument of CONTOURING_TOPOLOGY.md 7.4, which uses only convexity of
the cell and its faces). The counting lemma there gives the Euler characteristic of each part from the counts. A
connected compact 1-manifold with boundary is an arc (characteristic 1) or a circle (0); a connected compact surface
with boundary inside an orientable surface S has characteristic 2 - 2g - b, which is 1 only for the disk; a part
without boundary would be a closed component of S inside one cell, characteristic 2 or less but even, so not 1.
Requiring one part makes the ball unique. Membership: a mesh vertex v is in V(p) iff no Delaunay neighbour q of p is
closer to v (P3; the cell is the intersection of the half-spaces of p's Delaunay neighbours); a mesh edge crosses F_pq
iff its ends are on opposite sides of bisector(p, q) (P3) and the crossing point is closer to p than to every other
Delaunay neighbour of edge pq (P4); a Voronoi edge e_pqr meets triangle t iff the three-plane point x of bisector(p, q),
bisector(p, r) and plane(t) is closer to p than to the apexes of the two tetrahedra at pqr (P1) and inside t (P2). A
triangle meets a convex X exactly if one of its vertices is in X, one of its edges crosses a facet of X, or an edge of
X crosses it (two convex sets; under (G) the intersection polygon has its vertices at such points). ∎

### 14.2 Theorem A: homeomorphism

**Theorem A.** Under (G) and (TBP), let D be the set of Delaunay triangles pqr whose dual Voronoi edge meets S. Then D
is a closed 2-manifold triangle mesh, homeomorphic to S, and two triangles of D meet only in a common edge or vertex.
**Samples need not lie on S.**

*Proof.* (1) The sets S ∩ e (points), S ∩ F (arcs) and S ∩ V(p) (disks) are the cells of a decomposition Σ of S. The
end points of an arc S ∩ F lie on edges of F (Lemma 1's manifold boundary), so they are points S ∩ e; the boundary
circle of a disk S ∩ V(p) lies on the boundary of V(p), hence is a union of arcs and points of Σ. The cells are closed
balls whose boundaries are unions of cells: Σ is a regular cell decomposition of S.
(2) Every point x = S ∩ e is an end of exactly 3 arcs and lies in exactly 3 disks: e is in 3 facets and 3 cells by
(G1); S crosses e transversally inside one triangle (G3), so near x it meets each of the 3 facets in a curve ending at
x, which is part of that facet's one arc (F'), and each of the 3 cells. Every arc lies in exactly 2 disks (a facet is
in 2 cells).
(3) The dual of Σ: one vertex per disk, one edge per arc joining the two disks it lies in, one triangle per point x
(degree 3 by (2)). For a regular cell decomposition of a closed surface the dual complex is again a cell decomposition
of the same surface (both are refined by the barycentric subdivision of Σ; standard, eg Munkres, *Elements of
Algebraic Topology*, 1984, section 64, M). Here every dual 2-cell is a triangle with three distinct vertices.
(4) The dual is D: the disk of p is dual to the site p, the arc in F_pq to the Delaunay edge pq, the point on e_pqr to
the Delaunay triangle pqr, with the same incidences. The map is injective: (C') gives one disk per cell, (F') one arc
per facet, (E') one point per Voronoi edge. A bigon (two triangles with two common edges) would need two Voronoi edges
both on F_pq and F_pr; these facets meet only on the Voronoi line of p, q, r, which carries one Voronoi edge. So the
dual is a simplicial complex and equals D as an abstract complex.
(5) D is a subcomplex of the Delaunay tetrahedralization of P (exact `Delaunay3`), so its geometric realization is
embedded: two triangles meet in a common face. Hence D is homeomorphic to the dual of Σ, which is homeomorphic to S;
closed and 2-manifold follow. Nowhere is a sample assumed to lie on S. ∎

Remark. This is the argument behind T1 written for this setting; it replaces the open question of section 3 (rounded
samples): rounding the inserted points changes P, and the checks are made on the rounded P.

### 14.3 Theorem B: isotopy to an intermediate surface, and the three routes to the output

For every Voronoi edge e that meets S let x_e = S ∩ e (a point of S, the centre of the surface Delaunay ball of the
dual triangle). For every Delaunay edge pq of D, its facet F_pq meets S in one arc with end points x_e, x_e' (e, e' the
Voronoi edges dual to the two triangles of D at pq). Define the **dual quad surface**
S2 = union over the edges pq of D of the triangles (p, x_e, x_e') and (q, x_e, x_e').

**Theorem B.** Under (G) and (TBP), S2 is a PL surface and S is ambient isotopic to S2, by an isotopy that maps every
cell of K onto itself.

*Setting.* K is the Voronoi complex clipped to B' (every cell intersected with B': convex polytopes; the new faces
on the boundary of B' meet neither S nor S2). S and S2 lie in the box B, strictly inside B' (S by assumption; S2 is
made of segments between samples and points of S, all in the convex B). Every site p is in the interior of V(p).
Write s_pq = [x_e, x_e'] for the segment of D-edge pq, gamma_p for the union of the segments s_pq over the D-edges at
p, and C_p for the cone from p over gamma_p (the union of the triangles (p, x_e, x_e')).

*Proof, part 1: S2 is a closed PL 2-manifold, embedded* (shown directly, before Theorem 1 is used).

1. *gamma_p is a simple closed polygon on the boundary of V(p).* The circle c_p = boundary of the disk S ∩ V(p) lies
   on the boundary of V(p) and is the union of the arcs S ∩ F over the facets F of V(p), consecutive arcs meeting at
   points x_e on the Voronoi edges (Theorem A, steps 1-2). Replace each arc by the segment between its ends: one
   segment per facet (F'). The ends of an arc lie in the interiors of two different edges of its facet (G2, and an
   arc's ends are on different Voronoi edges by (E')), so the open segment lies in the relative interior of the
   convex facet (two distinct edges of a convex polygon are not collinear). Segments of different facets are in
   different facets and meet only at common Voronoi edges, ie only at shared ends x_e; each x_e is an end of exactly two
   segments of gamma_p (the two facets of V(p) at e). So gamma_p is a simple closed polygon, in the same cyclic order as
   c_p.
2. *C_p is an embedded disk with C_p ∩ boundary(V(p)) = gamma_p.* The radial map from the interior point p is injective
   on the boundary of the convex V(p), so the cone over a simple closed curve on it is an embedded disk; a point
   (1 - s) p + s y with y on gamma_p and s < 1 is in the interior of V(p) by convexity.
3. *gamma_p ∩ F_pq = s_pq for a D-edge pq, and the cones meet only along these segments.* An x_e on an edge of F_pq is
   an end of the arc S ∩ F_pq (S crosses e transversally, so near x_e it meets each of the 3 facets at e, by (G3) and
   (E')), so it is an end of s_pq; hence gamma_p ∩ F_pq = s_pq. The cells have disjoint interiors and C_p ∩ boundary
   = gamma_p (2), so C_p ∩ C_q is contained in gamma_p ∩ gamma_q ∩ V(p) ∩ V(q): this is s_pq if V(p) ∩ V(q) is the facet
   F_pq of a D-edge, {x_e} if it is a Voronoi edge e (or a facet whose arc is empty and an edge of it meets S), and empty
   otherwise (Voronoi vertices are not on S2's segments by (G2)).
4. *Points of the open segment (x_e, x_e')* lie in the relative interior of F_pq (1), which is in V(p) and V(q) only;
   so they lie in exactly the two cones C_p and C_q, in the triangles (p, x_e, x_e') and (q, x_e, x_e'), which are on
   the two sides of the bisector plane (p and q are strictly on opposite sides): a neighbourhood is two half-disks glued
   along the segment.
5. *The link of x_e is a 6-cycle.* Let e = e_pqr. x_e is in the interior of e, so in V(p), V(q), V(r) only (G1), and the
   segments of S2 at x_e are s_pq, s_qr, s_rp (the facets at e), with other ends y_pq, y_qr, y_rp. The triangles of S2 at
   x_e are the cone triangles over these segments from both of their cells: (p, x_e, y_pq), (q, x_e, y_pq),
   (q, x_e, y_qr), (r, x_e, y_qr), (r, x_e, y_rp), (p, x_e, y_rp). Their link is the cycle p - y_pq - q - y_qr - r -
   y_rp - p; its six vertices are distinct (p, q, r are distinct sites; y_pq = y_qr would put one point on an edge of
   F_pq and of F_qr other than e, but these facets share only e). By 3, the six triangles meet only along the edges
   of this fan, so the cycle is simple and x_e has a disk neighbourhood.
6. *The link of p is gamma_p*: near p only C_p is present (the other cones avoid the interior of V(p), by 2), and
   C_p is the cone over gamma_p. Points of the open cone triangles and of the open segments p x_e are interior points
   of V(p), in C_p only, inside the disk C_p.

Every point of S2 is one of these kinds, so S2 is a compact 2-manifold without boundary; it is PL and embedded (the
pieces meet only as listed in 3).

*Proof, part 2: isotopy.* S is compatible with K in the sense of CONTOURING_TOPOLOGY.md 7.1: (E), (F), (C) are (E'),
(F'), (C') together with (G). S2 is compatible with the same pattern: it meets e exactly in x_e where S does (3, 5) and
no other Voronoi edge; it meets F_pq exactly in s_pq, with the same two ends as the arc of S (3, 4), and no other facet
except in those ends; it meets V(p) exactly in the disk C_p with boundary gamma_p on the boundary of V(p) (2), and no
other cell. Theorem 1, with Remark 1 (any complex of convex cells whose boundaries are unions of lower cells), applies
to S and S2 on K: neither meets the boundary of B', so the homeomorphism it builds is the identity there, maps every
cell onto itself and S onto S2, and is isotopic to the identity. ∎

Partial statement. With uncertified cells, Theorem 1b of the contouring note gives the same on the union of the
certified cells (their facets and edges), and nothing inside the others.

**From S2 to D, three routes:**

(a) **Explicit moves (recommended).** S2 becomes D by two rounds of moves across tetrahedra: for every edge pq of D,
replace (p, x_e, x_e') and (q, x_e, x_e') by (p, q, x_e) and (p, q, x_e'), a move across the tetrahedron
T_pq = (p, q, x_e, x_e'); afterwards every triangle pqr of D carries a tent (p, q, x), (q, r, x), (r, p, x) with x =
x_e for its dual edge; replace it by pqr, a move across T_pqr = (p, q, r, x). A move across a non-degenerate
tetrahedron that meets the rest of the current surface only in the shared boundary edges is an ambient isotopy (the
elementary move of PL topology, Rourke-Sanderson 1972, M). So **if every T_pq and T_pqr is non-degenerate and empty in
that sense, in the stated order, D is ambient isotopic to S.** This is a sufficient condition, checked per
tetrahedron; it is not implied by the TBP (a T_pq is not inside V(p) ∪ V(q) in general). Its exact tests involve the
implicit points x_e (N degree 5, d degree 4) as tetrahedron and triangle corners: an orient3d with four implicit points
has degree 19 (3 x 5 + 4), with three implicit points and one input point 15. That is far beyond the generator's
fixed-width stages, so the check runs with a float filter and an exact rational fallback (`BigInteger`, every point is
rational in the input doubles). A zero (coplanar or touching outcome) counts as a failed move: the condition is only
sufficient. Its pass rate and cost are measured in stage 1a. Where it fails, D is reported as homeomorphic only; S2 (with exact implicit vertices) remains isotopic but
is not a double-coordinate mesh.

(b) **Chazal-Cohen-Steiner (T11).** D lies in the r_max offset of S (section 5.2), is homeomorphic to S (Theorem A),
and separates the two sides of the offset if the winding number of D at one point of each boundary component of the
offset is right (one exact parity test per component: the winding number is constant on a connected component that D
does not meet). The missing premise is that the r_max offset of S is a topological thickening, which holds below the
weak feature size (T12, M); a certified bound wfs(S) > r_max needs a search for critical points of the distance to S in
the band (points that lie in the convex hull of their nearest points; near a crease they do not occur, so wfs > 0 for
PL S). No published method for a certified wfs bound of a mesh was found; this route is a separate study (tier L).

(c) **The elementary-move guard of section 10** is the same mechanism as (a) applied to the Remesher's operations; for
RDR it is not a separate route, since D is built by insertions, not by moves from S. It is listed for completeness:
its lemmas (flip, vertex move, collapse) are the side path's stage 0, not RDR's.

### 14.4 The predicates and their degrees

All in coordinates relative to a sample a (translation invariant, as the existing `ipredicate`s): b' = b - a, etc.
Bisector(a, b): 2 b'·y = |b'|^2 (y = x - a). Plane of t: n_t·y = h_t with n_t = (t1 - t0) x (t2 - t0) (degree 2) and
h_t = n_t·(t0 - a) (degree 3). Degrees are total degrees in the input coordinates; a homogeneous implicit point is
(N, d) with x = a + N / d, and a sign on it is sign(poly) · sign(d) (the generator's convention).

| Name | Formula | Degree |
|---|---|---|
| BTPI(a, b, c; t) | three-plane point of bisector(a, b), bisector(a, c), plane(t): d = det(2b', 2c', n_t) (1 + 1 + 2 = 4); N = \|b'\|^2 (2c' x n_t) + \|c'\|^2 (n_t x 2b') + h_t (2b' x 2c') (2 + 3, 2 + 3, 3 + 2 = 5) | N 5, d 4 |
| P1 SideOfBisector(BTPI, e) | s = sign(2 e'·N - \|e'\|^2 d) · sign(d); **s < 0: x is closer to a than to e** (since \|x - e\|^2 - \|x - a\|^2 = \|e'\|^2 - 2 e'·y), s = 0 equidistant, s > 0 closer to e | 6 |
| P2 Orient2dBTPI(t_i, t_j) | orient2d of x, t_i, t_j in the projection that drops the largest component of n_t (chosen by exact comparisons of the n_t components, degree 2): (N_u - d t'_iu)(t'_jv - t'_iv) - (N_v - d t'_iv)(t'_ju - t'_iu), times sign(d) | 6 |
| P3 SideOfBisector(v; a, b) | sign of \|v - a\|^2 - \|v - b\|^2 = 2 b'·v' - \|b'\|^2 on doubles (v' = v - a); < 0: v is closer to a | 2 |
| P4 SideOfBisectorLPI(q0, q1; a, b; c) | the crossing y of mesh edge q0 q1 with bisector(a, b): l_k = 2 b'·q'_k - \|b'\|^2 (degree 2), N = q'_0 l_1 - q'_1 l_0 (3), d = l_1 - l_0 (2); sign of 2 c'·N - \|c'\|^2 d, times sign(d); < 0: y is closer to a than to c (P1's convention) | 4 |
| P5 Orient3dCircumcentre(a, b, c, e; t) | circumcentre of tetrahedron abce: N = \|b'\|^2 (c' x e') + \|c'\|^2 (e' x b') + \|e'\|^2 (b' x c') (4), d = 2 det(b', c', e') (3); sign of n_t·N - h_t d, times sign(d) (for (G2)) | 6 |
| sign(d) of BTPI, LPI, circumcentre | the denominators | 4, 2, 3 |

**Claim (degree bound).** Every exact decision of the restricted bookkeeping, the TBP checks of Lemma 1 and (G) is one
of P1-P5, the existing `Orient3d` / `InSphere` of `Delaunay3`, or a comparison of input coordinates, so the maximum
degree is 6. *Proof*: by the expansion above (each entry is a sum of products whose degrees add as listed; the
largest is a degree-1 or degree-2 factor times a degree-5 or degree-4 one). ∎ For comparison, `Orient3dTPI` in
`indirect.spec` has degree 9. The decisions that may be conservative instead of exact (R2's band test, R3's radius-edge
test, the queue key) use floating-point values with a relative error bound and never decide a combinatorial
structure. They are made conservative: refine whenever the float value cannot certify the bound (computed value above
the bound reduced by its error bound), so the band and angle statements hold exactly as stated (r_max, rho);
refining more than needed keeps every proof, and termination is by Theorem C regardless.

All five are straight-line formulas on input points, so they fit `indirect.spec` as `ipredicate`s like `Orient3dLPI`
and `Orient3dTPI`; the generator derives the filter bound, the expansion lengths and the `FixedArith` widths, and
`GeneratedPredicatesTests` checks the bound table. P1's spec, as a draft for stage 1b (not committed to the spec file):

```
# schematic (vector notation); the spec file expands it into scalar lines as Orient3dTPI does
# result < 0: the point x = BTPI(a, b, c; t) is closer to a than to e; 0: equidistant; > 0: closer to e
ipredicate SideOfBisectorBTPI(a, b, c, t0, t1, t2, e) inputs xyz
    b' = b - a; c' = c - a; e' = e - a; u = t1 - t0; w = t2 - t0; t' = t0 - a   (coordinate differences)
    n_t = cross(u, w); h = dot(n_t, t')
    d = det(2 b', 2 c', n_t)
    N = dot(b', b') cross(2 c', n_t) + dot(c', c') cross(n_t, 2 b') + h cross(2 b', 2 c')
    sign 2 dot(e', N) - dot(e', e') d, d
```

### 14.5 Theorem C: termination and size

Rules as in section 4, with: an insertion is refused when the squared distance computed by `Delaunay3.Insert` to the
nearest vertex is at most h_min^2 (floating point; `Insert` compares with the vertices of the conflict region, among
which the nearest vertex of the new point is; the relative error of the squared distance is a few units of 2^-53, so
the true separation of accepted points is at least h = h_min (1 - 2^-50), say); a refused element is marked
uncertified with the stamps of its cells and is not queued again until one of them changes. Inserted points: every
rule inserts an exact point of S (below), either computed exactly (rationals) and rounded once to nearest, so its
distance from S is at most eps_0 = (sqrt 3 / 2) ulp of its coordinates, or constructed in floating point with a
computed error bound delta, and then eps_0 = delta.

**Theorem C.** (i) The refinement terminates. (ii) With the initial sample an h-separated set of input vertices,

|P without the corners| <= sum over triangles t of S of [2 r A_t + (pi / 2) r^2 L_t + (4 pi / 3) r^3] / ((pi / 6) h^3),   r = h / 2 + eps_0,

A_t, L_t the area and perimeter of t. The leading term is 6 area(S) / (pi h^2).

*Proof.* (i) Accepted points are pairwise at least h apart and lie in B', so the balls of radius h / 2 around them are
disjoint and lie in B' enlarged by h / 2: their number is at most the volume ratio, finite. Between two insertions the
bookkeeping visits finitely many elements, each queued once per change of its cells or marked; the number of changes is
bounded with the number of insertions, so the loop ends.
(ii) Every point of P except the corners lies within eps_0 of S, rule by rule: the initial sample is input vertices
(on S); R0 inserts a point of S in the corner's cell; R1 inserts a point of S on a Voronoi edge, facet or cell (a
surface Delaunay ball centre x_e, a point of S ∩ F, a point of S ∩ V(p)); R2 inserts a surface Delaunay ball centre or
the farthest point of S ∩ V(p) from p, a vertex of a polygon t ∩ V(p), so a point of S; R3 inserts a surface Delaunay
ball centre. Each is rounded once (or carries the bound delta). So the disjoint balls of radius h / 2 lie in the
(h / 2 + eps_0)-offset of S, which is contained in the union of the offsets of the triangles; their volumes are given
by Steiner's formula for a planar convex set in R^3, vol = 2 r A + (pi / 2) r^2 L + (4 pi / 3) r^3. Comparing volumes
gives the bound. ∎

Remark (progress of R0): every point of a corner's cell is closer to the corner than to any sample, and at least the
margin from it; with a margin of at least h_min between B and B' an R0 insertion is never refused.

Where S is smooth on the scale r_max (lfs large against it) the insertion radius argument of T3 / T4 bounds the size
by O(area / r_max^2) instead, and no element reaches h_min; how many do on real meshes is stage 1a's measurement.
The wedge threshold of section 3 needs only scale invariance: a wedge is mapped onto itself by scaling about a point of
its crease, and the Voronoi complex scales with the sites, so a configuration that violates the TBP is violated at
every scale; whether refinement reaches one is not decided by this argument (measured in 1a).

### 14.6 Theorems D, E: band and angles (the statements of sections 5.2 and 5.4)

**Theorem D (band).** Assume (TBP) and (G) at the end, and that no R2 insertion was refused (R3 refusals, the
exemptions of Theorem E, do not matter here). Then every point of D is within r_max of S and every point of S within
r_max of D. *Proof*: section 5.2; the farthest point of a restricted region S ∩ V(p) from p is a vertex of one of the convex
polygons t ∩ V(p) (the squared distance is convex), which are mesh vertices in V(p), crossings of mesh edges with
facets (P4's point) or crossings of Voronoi edges with triangles (BTPI points): finitely many, tested by R2
(conservatively: refined whenever the float test cannot certify the distance <= r_max, so the bound is exactly r_max).
Each restricted facet has exactly one surface Delaunay ball by (E'); every sample with a nonempty restricted region is a
vertex of D (Theorem A). ∎

**Theorem E (angles).** Independent of Theorem D. Every facet of D not marked exempt has circumradius / shortest edge <= rho, so its smallest
angle is at least arcsin(1 / (2 rho)). *Proof*: R3 refines a facet whose ratio exceeds rho by inserting its surface
Delaunay ball centre, unless refused by h_min (then marked exempt); at the end no unmarked facet exceeds rho; for a
triangle, R / l_min <= rho gives sin(alpha_min) = l_min / (2 R) >= 1 / (2 rho). ∎

### 14.7 Review (fresh session, 2026-11-13, on 157f478f)

Result: the chain Lemma 1 -> A -> B -> route (a) is sound in substance; C, D and E hold; no counterexample. Worked in:

- M1 (must-fix): Theorem B used the grid lemma, which assumes a closed PL 2-manifold, to conclude that S2 is one.
  Now part 1 proves it directly (embedded cones, the open segments in exactly two cones, the 6-cycle link of x_e, the
  link gamma_p of p, gluing only along the segments), and part 2 applies Theorem 1.
- S1: there was no P5; the circumcentre predicate is now P5. P1's sign convention is stated (< 0: closer to a), also for
  P3 and P4 and in the spec draft.
- S2: route (a)'s degree: 19 with four implicit points, 15 with one input point; a zero counts as a failed move.
- S3: Theorem C: eps_0 needs exact points rounded once (or the construction's error bound); termination needs only
  h-separation and packing in B'; the size bound rests on the rule-by-rule argument that every point is within eps_0
  of S.
- S4: Theorem B uses that S and S2 do not meet the boundary of B'; (R0) is kept only so that no corner is a vertex of D.
- S5: Theorem D's hypotheses are (TBP), (G) and no refused R2 insertion, separate from Theorem E's exemptions; one band
  bound, exactly r_max (conservative refinement).
- S6: (G3) / (G4) exclude tangencies (transversal intersections); "near" in (G1) / (G2) is defined.

Still premises, not proved here: the dual-complex statement for regular cell decompositions of closed surfaces
(Theorem A step 3, standard, M) and the elementary-move lemma (route (a), Rourke-Sanderson, M).
