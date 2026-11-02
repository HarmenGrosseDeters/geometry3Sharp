# Alpha wrap: notes on the paper

Reading notes for `MeshAlphaWrap`, written for the investigation of degenerate and near-degenerate output
triangles of the raw wrap. Everything below is a paraphrase of the paper unless it is marked **[derived]** (our own
reasoning from the paper's statements) or **[library]** (a comparison with our code). No CGAL source was opened; no
CGAL manual or blog page was used for these notes.

## Source

C. Portaneri, M. Rouxel-Labbé, M. Hemmer, D. Cohen-Steiner, P. Alliez, *Alpha Wrapping with an Offset*, ACM
Transactions on Graphics 41(4) (Proc. SIGGRAPH 2022), pp. 1-22, doi:10.1145/3528223.3530152.

- Read: the author version on HAL, <https://inria.hal.science/hal-03688637/document> (HAL Id hal-03688637,
  version v1, submitted 15 June 2022; marked "Author version"; 22 pages plus the HAL cover page, read in full,
  including the appendices A to E).
- Not reachable from the session (2026-11-02): <https://www-sop.inria.fr/members/Pierre.Alliez/papers/alphawrap.pdf>
  returns 404 (the host is reachable; the member page redirects to team.inria.fr, which the proxy refuses);
  dl.acm.org is refused by the proxy. No block was circumvented.
- Page numbers below are the paper's printed page numbers (the PDF page is one higher because of the HAL cover).

## 1. The algorithm (Sections 4.1, 4.2, Algorithm 1, Appendix B.1; pp. 4-6, 17-18)

**Parameters** (p. 5). Alpha (α) is the radius of the empty balls used for carving: it sets the smallest strait or
hole that carving can pass through. Offset (δ) is the level of the unsigned distance field whose level set I_δ (the
offset surface) carries the new vertices; it sets how tight the result is. Both are independent, arbitrary and
must be strictly positive.

**Initialization** (p. 5). The corners of a loose axis-aligned box around the offset surface go into an empty 3D
Delaunay triangulation. Infinite cells (with the infinite vertex) are outside, finite cells inside, so every facet
has two cells. The output at every stage is the set of facets between an inside and an outside cell.

**Gates and traversability** (p. 5, Definition 2 on p. 17). A gate is a facet with one outside and one inside
cell. Φ(f) is the minimum radius of the empty (Delaunay) balls through the three vertices of f, ie the minimum over
the dual Voronoi edge of f. A gate is α-traversable if Φ(f) > α. Fig. 23 (p. 18): along the dual edge the empty-ball
radius is smallest at the facet's diametral ball if that ball's center lies on the edge, otherwise at one end of
the edge (a circumcenter of an adjacent cell).

**Queue** (p. 6). A priority queue of α-traversable gates, largest Φ first (as in Delaunay refinement, larger
elements first), initialized with the traversable facets of the box hull. When a gate is popped (Algorithm 1):
skip it if Φ(f) < α or if the inner cell is already outside; gates destroyed by an insertion are removed from the
queue.

**Rule R1, intersection** (p. 6; Appendix B.1, p. 18). The dual Voronoi edge of the gate is the oriented segment from
k_c (the circumcenter of the outside cell, or the facet's circumcenter if the outside cell is infinite) to k_cn
(the circumcenter of the inside cell). If it crosses I_δ, the first crossing from the outside end is inserted. This
is the restricted Delaunay refinement step (Boissonnat and Oudot 2005). R1 is tested even when the inside cell does
not touch the input, "in order to avoid inserting Steiner points on inner geometries" (p. 6).

**Rule R2, projection** (p. 6; p. 18). Only if R1 does not apply and the inside cell c_n intersects the input I:
project k_cn onto I (point π_n), and insert the point of I_δ on the segment [k_cn, π_n] closest to k_cn. Appendix B
writes it as Π_δ(k_cn), the closest point to k_cn on I_δ; the two agree because Lemma 1 shows k_cn lies outside the
offset volume V_δ when R2 fires.

**Carving** (p. 6). If neither rule applies, the inside cell is tagged outside and its α-traversable facets are
queued. After an insertion, new finite cells are tagged inside, new infinite cells outside, and the new traversable
gates are queued. The loop ends when the queue is empty.

**Oracle** (Section 4.4, p. 7). The input is reached only through three queries: tetrahedron-input intersection
(carving allowed?), segment-offset-surface intersection (R1), projection onto the input (R2). The segment-offset
intersection has no closed form and is found by bisection ("dichotomy") with repeated distance queries; Appendix E
(p. 20) prunes the bisection with the 1-Lipschitz property of the distance function.

**Cospherical degeneracy** (Section 4.4, p. 7; Section 5.4, p. 11). Many vertices on or near one Delaunay ball (half
domes in Thingi10K) make each insertion a large combinatorial update. Six Thingi10K meshes timed out (1 h) in the
first benchmark. Remedy used by the authors: when such a large update is detected, insert the center of that
Delaunay ball (Earth Shot - Split: one such vertex, 3,015 s down to 135 s). The paper says nothing about how this
extra rule interacts with the guarantees (the center is in general not on the offset surface). This is the only
place where the paper treats nearly cospherical points, and it treats them as a cost problem, not a quality problem.

## 2. Guarantees and their assumptions (Section 4.3, pp. 6-7; Section 8, p. 15; Appendices B, C)

- **Termination** (Theorem 1, pp. 18-19). Lemma 1: an R2 point lies strictly inside the Delaunay ball of the cell it
  refines, so the insertion breaks that cell. Lemma 2: every vertex has insertion radius r(p) >= min(α, δ). The R1
  point is the center of an empty ball through f of radius >= Φ(f) > α. The R2 point is δ inside the empty ball
  centered at k_cn through π_n. Since I_δ is bounded, a packing argument bounds the number of insertions. The text
  stresses that δ > 0 is what makes this work: inserting on the input itself would give no separation bound.
- **Watertight** (p. 6-7): the output is the inside/outside interface of a partition of space, so it is closed.
- **"Manifoldness" / orientable** (p. 7): each carving step removes a whole tetrahedron, so every output edge has an
  even number of incident facets and the surface can be oriented. **This is not a 2-manifold guarantee.** Section 8,
  "Immersion versus embedding" (p. 15): the output is an immersed combinatorial manifold, not necessarily an embedded
  one. Facets always meet conformally (they all come from one Delaunay triangulation), but carving can pinch the
  surface at vertices or edges. The authors suggest two fixes, without detail or proof: forbid pinching
  configurations during carving ("straightforward, but yields larger volumes"), or locally ε-deflate the inside
  elements ("rigorously proving the correctness ... is still out of reach").
- **No self-intersection**: the paper claims it only in the sense above (conformal Delaunay facets); the pinches are
  the remaining non-embedded contacts.
- **Strict enclosure** (p. 7): starts from a box that encloses the input; a cell that intersects the input is never
  carved (R2 refines it first).
- **Offset band** (Section 5.2, "Approximation Error", p. 9): all vertices lie on I_δ (distance δ). A point on a
  facet has distance in [0, α + δ] to the input: 0 because a facet can pass arbitrarily close to a convex corner;
  α + δ because every final facet has Φ < α and its vertices are at distance δ, with the stated assumption that the
  center of the facet's smallest Delaunay ball lies in the facet. Fig. 7: errors peak around δ, maximum slightly
  above α.
- **Size** (Section 5.2, "Complexity", p. 9): at termination every output facet has Φ < α (else it would still be
  queued). α is a sizing criterion, not a strict sizing field: with small δ, R2 refines convex curved regions well
  below α (Appendix A, Figs. 21, 22, pp. 17-18).
- **Practical correctness** (Appendix C, p. 19; Section 4.4, pp. 7-8). Predicates (in-sphere, α-traversability,
  tetrahedron-input intersection) are filtered exact predicates. Steiner points are constructed in an exact number
  type and rounded to double. Both the R1 bisection stop and the rounding must keep the point inside the Delaunay
  ball it must break, and δ is the margin that allows this (the theoretical point lies at least δ from that ball's
  boundary). If δ is near the double resolution, another number type is needed. (Appendix C says the bisection is
  "used in Rule R2"; Sections 4.4 and E show it is R1. This looks like a typo.)
- **Validation** (Section 5.1, p. 8): Thingi10K, GrabCAD, ABC, Fusion 360; every output checked with exact predicates
  to be watertight, orientable and strictly enclosing. Manifoldness in the embedded sense and triangle quality are
  not part of that check.

## 3. Output triangle quality: everything the paper says

- Section 4.3, "Quality Guarantees" (p. 7): no guarantee on the shape of the output facets. R1 points are
  equidistant from the vertices of the refined facet (like restricted Delaunay refinement), which "favors"
  well-shaped facets.
- Section 5.2, "Quality" (p. 9): no theoretical bound on quality such as minimal angles. R1 and R2 differ: R2
  projects onto the input and lifts back to the offset, so the elements are better shaped when R1 does most
  insertions. A larger δ (offset farther from the input) means more R1 insertions and better elements (Fig. 27,
  rightmost column). A small δ means more tetrahedron-input intersections, more R2, worse elements.
- Section 5.2, "Sharp Features" (p. 9): with small δ, R2 points near a sharp convex input edge tend to land on that
  feature, so convex features are roughly kept (Fig. 8).
- Section 5.4 (p. 10): the R2 projection places Steiner vertices on the tips of local convex bumps, which helps the
  enclosure. R1 does not do that.
- Section 5.4 and Fig. 15 (pp. 11, 14): distributions over all of Thingi10K of runtime, memory, complexity,
  Hausdorff distance and "several common facet quality criteria" (min angle, ratios). The text only says the
  average facet quality (min angle, ratios) is higher than that of the other methods. The figure is a raster image;
  the text gives no minimum angle, no worst case, and no counts of slivers, needles or caps. Fig. 13 (p. 13) mentions
  "many degenerate faces" only for ManifoldPlus's output.
- Appendix A, Fig. 22 (p. 18): in 2D, with tiny δ (0.001) and large α (1), "several skinny triangles keep
  intersecting the curved area" and trigger R2 refinement. These are Delaunay cells (the 2D analogue of
  tetrahedra) during the run, not output facets.
- Section 9 (p. 16): "A large value for δ increases the quality of the output mesh elements."
- Section 8, "Spatially-varying parameters" (p. 15): α and δ are global, so meshes are mostly uniform, especially
  for large δ.
- **Post-processing:** the paper neither recommends nor applies any quality post-processing (no smoothing,
  remeshing, edge collapse or flips). The outputs are used as they are (CFD, swept volumes, Section 6).
- The words sliver, needle and cap do not occur in the paper (the only "caps" are the round faces of alpha shapes,
  Section 3.1).

## 4. Failure modes and parameter advice

- Both parameters are given relative to the bounding box diagonal l_b (Section 5, p. 8). Values used: comparison
  over Thingi10K α = l_b/100, δ = l_b/3000 (p. 11); runtime plot δ = α/30 (Fig. 12); bicycle α = l_b/500 to l_b/5 with
  δ = l_b/100 (Fig. 1); Tire α = 1/150, δ = 1/3000 (Fig. 4); perturbation test α = 1/40, δ = 1/500 (Fig. 5).
- Small α with small δ: dense output, slow (Fig. 26: up to 7.4M facets, 8,000 s). Small α with tight δ on convex
  curvature: refinement well below α (Appendix A).
- Large α closes holes, with collateral defeaturing (Section 8, "Repair", p. 13). Large δ also defeatures (closes
  gaps) and rounds convex sharp edges (Section 5.2).
- Concave sharp features: the wrap is less tight there; protection balls or sharp feature recovery are left to
  future work (Section 8, p. 15).
- α smaller than the gaps: two-sided wraps, 2δ thick around open or zero-volume parts (Fig. 9, p. 10).
- Many vertices on one Delaunay ball: runtime blow-up (Section 1 above).
- Pinched (non-embedded) output: Section 2 above.
- δ near the floating-point resolution: the exact-construction safety margin breaks down (Appendix C).

## 5. What can produce near-degenerate output facets

The paper does not address near-degenerate output facets directly. What it gives:

1. **R2 placement** (p. 9): named as the source of worse-shaped elements; frequent when δ is small relative to α
   and to the input's curvature.
2. **Steiner points close to existing vertices**: excluded in theory by Lemma 2 (all vertex distances
   >= min(α, δ)). In practice this relies on exact construction and rounding with margin δ (Appendix C).
3. **Nearly cospherical points**: treated only as a runtime problem, fixed by inserting the ball's center
   (Sections 4.4, 5.4).
4. **Pinching** (Section 8): not a facet shape problem, but any repair of it (forbid during carving, deflate) is a
   change that the paper does not analyse for quality.

**[derived] A minimum angle bound that follows from the paper's own statements.** Take an output facet with
circumradius R and shortest edge e. All output facets have Φ(f) < α at termination (p. 9), and the circumradius is at
most Φ(f) (every ball through the three vertices is at least as large as their circumcircle), so R < α. Lemma 2 gives
e >= min(α, δ) for every pair of vertices. By the law of sines, the smallest angle θ_min satisfies
sin θ_min = e / (2R) > min(α, δ) / (2α). So in exact arithmetic, every facet produced by carving alone has

| α / δ | θ_min greater than | largest angle less than |
|---|---|---|
| 1 or less | 30° | 120° |
| 10 (our `MeshRobustRepair` fallback: l_b/300, l_b/3000) | 2.87° | 174.3° |
| 30 (our `MeshAlphaWrap` defaults: l_b/20, l_b/600; also the paper's Fig. 12) | 0.955° | 178.1° |
| 100 | 0.286° | 179.4° |

(largest angle < 180° - 2 θ_min). The bound needs only the termination state and Lemma 2, not the quality claims.
It assumes every output facet is a gate that ended with Φ < α, and that Lemma 2 holds for the vertices actually
inserted.

**[library] Where `MeshAlphaWrap` departs from these assumptions** (from `mesh_ops/MeshAlphaWrap.cs` and the
alpha wrap section of `docs/SOURCES.md`; candidates for the raw-wrap investigation, not verified here):

- Traversability uses the facet's circumradius instead of Φ(f). The circumradius is at most Φ(f), so a remaining gate
  still has R <= α, and the R1 point (on the dual edge) is still at least Φ(f) > α from the facet's vertices. The
  bound above is unaffected.
- The removal of enclosed outside pockets and the manifold step move outside cells back inside. Their facets become
  output facets that were never tested as gates, so neither R < α nor the bound applies to them. (The class doc
  already notes the band can grow to about Offset + 2 Alpha there.) The paper has no such step: it leaves pinches in
  place.
- Rejected rule 1 / rule 2 Steiner points (counted as `rejected_rule1` / `rejected_rule2`) leave gates unrefined.
- Steiner points are computed in double arithmetic (rule 1 by a 60-step bisection) and the intersection test has a
  contact margin; the paper's
  Appendix C constructs them exactly and rounds once with margin δ. Lemma 2 holds only up to that error.
- Clamped circumcenters of flat cells (`clamp_center`) move the dual-edge end points along the facet normal; the R1
  point is then still on the dual edge, but R1 can miss a crossing beyond the clamp.

So with α/δ = 30, a facet with a minimum angle of 0.05° cannot come from the paper's idealized loop. With α/δ = 10
(the repair fallback), caps of 178° cannot either. Comparing the bad facets against the list above (manifold step
or pocket removal, rejected points, vertex pairs closer than min(α, δ), R > α) should identify their origin.

## 6. Correction and results (2026-11-02, #311 and the follow-up PR)

**Correction of the [derived] bound for `MeshAlphaWrap`.** The paper's Lemma 2 gives a vertex spacing of at least
min(α, δ). For our implementation the bound is weaker: at least **min(δ, α − δ)**, less the contact margin (1e-6 δ)
and rounding.
- Lemma 1 has k_cn outside the offset volume whenever R2 fires. In our code R2 also fires when the whole dual edge
  lies inside the offset volume (the bisection branch of rule 1 finds no exit). Then dist(k_cn) < δ, and the R2 point
  lies only R − δ + dist(k_cn) ≥ α − δ inside the empty ball.
- The cell "intersects" the input up to the contact margin, so dist(k_cn) < R + contact.
- So the angle bound is sin θ_min ≥ min(δ, α − δ) / (2α). For δ < α/2 this is δ/(2α), the same numbers as the table
  above (2.87° at α/δ = 10, 0.955° at α/δ = 30), but the form min(α, δ) does not hold for our code when α < 2δ.
- The independent review of #311 checked this derivation.

**Where the triangles below the bound came from** (all checked per triangle):
1. **Rule 2 on flat box-corner cells** (fixed in #311). `dual_edge` clamps the circumcenter of flat cells to 4 box
   diagonals. Rule 2 projected the clamped point, which lies outside the cell's circumsphere, so it neither removes the
   cell nor keeps the spacing. Points 1e-6 α apart and needles of 5e-5° followed (Thingi10K 59196, 163738). Rule 2 now
   projects the cell's own circumcenter, guarded by an exact InSphere test.
2. **The manifold step** (`make_manifold`, our addition: the paper has no manifold step and leaves pinches, Section 8).
   It moves outside cells inside, and their facets were never gates.
   - After #311, on an 800 mesh Thingi10K sample (random, ≤ 100k faces, both α/δ = 10 and 30), every triangle below
     0.5° or above 170° has a circumradius above α. Every triangle with a circumradius up to α satisfies the bound.
   - With `MakeManifold` off, no result facet has a circumradius above α.
   - Moving a different (outside) cell inside instead almost never helps: 1 of 328 caps had a neighbouring outside
     cell whose new facets all have a circumradius up to α. The outside cells at the surface were carved through large
     facets.
   - The step also widens the band: the result reached up to 8.8 (δ + α) from the input, against the documented about
     δ + 2α.
3. Rejected Steiner points and `PointLimitReached` would leave large facets too; neither occurred on the sample.

**Refinement at the non-manifold spots** (`MeshAlphaWrap.RefineNonManifold`, default on: owner decision 2026-11-02,
accepting the time cost in the table below).
- Before the manifold step, every outside cell that a trial run of the manifold step would move inside gets a Steiner
  point on the offset surface, and the refinement continues, up to 10 rounds. The point is either:
  - the projection of its circumcenter, at least min(δ, α − δ) deep inside its circumsphere; or
  - the first offset crossing on the dual edge of a facet towards an inside cell, for a facet with a circumradius of at
    least that much.
- This is the separation argument of restricted Delaunay refinement (Boissonnat and Oudot 2005: a dense enough sample
  gives a manifold restricted Delaunay surface). It is not a method of the paper.
- On the 800 mesh sample (798 evaluated), with no guarantee failures:

| | α/δ = 10 (repair fallback) | α/δ = 30 (defaults) |
|---|---|---|
| triangles below 0.5° | 28 → 5 | 0 → 1 |
| triangles above 170° | 327 → 40 | 42 → 6 |
| meshes with a triangle below 0.5° / above 170° | 19 / 166 → 5 / 34 | 0 / 38 → 1 / 6 |
| band max / (δ + α) | 8.76 → 1.11 | 1.33 → 0.99 |
| cells moved by the manifold step | 22987 → 1532 | 2005 → 216 |
| Steiner points added by the refinement (share of all) | 16606 (0.03 %) | 1682 (0.4 %) |
| time per mesh, median / p90 (3 meshes in parallel on 4 cores) | ×1.22 / ×1.44 | ×1.05 / ×1.38 |

