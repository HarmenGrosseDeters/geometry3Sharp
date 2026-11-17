# Boolean research after 3.0: prior art for guaranteed rounding and a certifying boolean (2026-11-17)

Status: **prior-art check** (AGENT_WORKFLOW section 2) for two research directions after Exact Boolean 3.0, owner
approved 2026-11-17. **Owner decisions 2026-11-17:** direction 1 not now; direction 2, stages 1-2, built as a test-only
checker (section 3.8). The owner uses it to decide whether either one gets a research effort. Papers and documentation
only: no library code was changed, nothing was measured, and no code of CGAL, libigl, fTetWild, TetGen, Trueform or any
other GPL / LGPL / MPL project was opened.

Marks, as in [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md): **[V]** full text read (in this repository's earlier
notes, which are cited where they hold the reading); **[S]** abstract, bibliographic record or search snippet only;
**[M]** from memory, not re-checked this time. An [S] or [M] claim is a lead to check before anyone builds on it.

## 1. Result in short

| | Direction 1: rounding with a guarantee | Direction 2: certifying boolean |
|---|---|---|
| Novelty | **partially known.** Existence is proven: Devillers, Lazard and Lenhart round within 3/2 grid units, and on a power-of-two grid near 2^-19 of the diagonal that fits both our drift bound and our degenerate-area test, when the two diagonals the code uses are close (section 2.3). Valque's thesis has a local version, certified on 64 % of 520 hard models. What is open is the output size (their worst case is O(n^15) by the SoCG 2018 abstract, O(n^5) under assumptions), a local scheme that always ends, and whether the output passes our clean test (collapses can leave coincident faces) | **partially known.** Every building block has prior art (geometric checkers, subdivision and partition verification, winding-number propagation over arrangement cells). We found no certifying 3D mesh boolean, and nothing that certifies the rounding to doubles |
| Closest papers | Devillers et al. 2018 / 2020; Fortune 1999; Milenkovic and Sacks 2019; Valque and Lazard 2025 | Mehlhorn et al. 1999; Devillers et al. 1998; McConnell et al. 2011; Palios (partition verification); Zhou et al. 2016 |
| Research risk | **high** for a theorem with near-linear size; **medium** for a local, guaranteed fallback without a size bound | **low to medium** for the classification and output part; **high** for a worst-case near-linear embedding certificate (section 3.4 shows the convex-cell witnesses are quadratic in the worst case) |
| Effort | a theorem: months, a research paper, uncertain. The first experiment: 2-3 days | stages 1-2 (section 3.5): about 1-2 weeks; an embedding witness: weeks |
| First experiment that can fail cheaply | Section 2.6: Devillers-type rounding of only the defect clusters, on a grid chosen by section 2.3, with a fixed collar, on the RC4b cases and the 19 generated leftovers | Section 3.6: a checker for the classification and the selection only (linear), fed with mutated classifications |
| Breakthrough vs engineering | a breakthrough is an O(m polylog m) size bound (m = size of the exact result) with an isotopy-type statement, or a proof that this is impossible. Anything else is engineering on the current repair | a breakthrough is a certificate of near-linear size, checkable in near-linear time in the worst case, for the embedding of the arrangement. A checker for the classification on top of the existing self-intersection search is engineering |

Recommendation (the owner decides): direction 2 in its stages 1-2 is the cheaper of the two and helps the robustness
round directly (a sound oracle for fuzzing, where today the volume identities are the oracle). Direction 1 is worth only
the experiment of section 2.6, and only if the RC4b leftovers become a user's problem: the triage found five of them (the
RC4b differences of the ABC chained and contact campaigns), and the plateau variant was a no-go for the same reason (gain on real data).

## 2. Direction 1: 3D rounding of the exact result with a guarantee

### 2.1 What we already decided or measured (not presented as new)

- SafeRounding (D′, [SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md) section 1) proves H(S, S0) ≤ `MaxDrift` ≤ `maxLength`
  (1e-5 of the diagonal) for every edit it applies, with the reference rule of D1 (Z3-checked). It does **not**
  guarantee that it ends clean: `RoundingClean == false` stays possible.
- Option A (Milenkovic and Sacks expansion) and option B (Valque and Lazard iteration) were closed as defaults in #313
  (SAFE_ROUNDING.md section 8.6). The certified empty ball, clearance skins and the separating-axis prefilter are closed
  ([SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md) section 5). The single-vertex position search is built (#755); its
  plateau variant is a no-go before the release, and the rule stays a design (owner 2026-11-15, section 10 of that note).
- A local, patch-wise option B (snap and resolve only the defect clusters with a margin) was recommended in
  SAFE_ROUNDING.md 8.6 and its cost estimated in 8.7 (tool mode `patch`: a median of 0.17 s per resolve of a patch, a
  lower bound). It used the doubles' grid and no guarantee.
- RC4b ([BOOLEAN_TRIAGE_2026-11.md](BOOLEAN_TRIAGE_2026-11.md)): five differences where the smallest repairing edit needs
  1.04-1.12 times `maxLength`, or the budget is at 0.985 and the effort limit is reached as well. The owner keeps the
  bound (2026-11-17): a documented limit.
- The literature check of SNAP_ROUNDING_3D.md section 3 found no method since 2018 with a no-new-intersection guarantee at
  practical cost; Diazzi et al. 2023 call the flip-free conversion to floating point "an unsolved problem (though rare in
  practice)" [V there].

### 2.2 Prior art

| Work | What is proven | Size / cost | Relevance to us |
|---|---|---|---|
| J. D. Hobby, *Practical segment intersection with finite precision output*, CGTA 13(4), 1999 [M]; D. H. Greene, F. F. Yao 1986 [M] | 2D snap rounding: segments become polylines through hot-pixel centres, within half a pixel, no new crossings, topology kept up to collapses | output linear in the hot-pixel incidences | the 2D model our `comp_geom/SnapRounding2.cs` implements; the theory that does **not** carry to 3D |
| M. T. Goodrich, L. J. Guibas, J. Hershberger, P. J. Tanenbaum, *Snap rounding line segments efficiently in two and three dimensions*, SoCG 1997, 284-293, doi:10.1145/262839.262985 [S] | output-sensitive 2D algorithms; a framework for segments in 3D | | segments only, not triangles |
| D. Halperin, E. Packer, *Iterated snap rounding*, CGTA 23(2), 209-225, 2002 [S]; E. Packer, *Iterated snap rounding with bounded drift*, CGTA 40(3), 2008 [S] | every vertex at least half a pixel from every non-incident edge (ISR); ISRBD adds a drift bound | ISR's drift can grow with n; ISRBD bounds it | the 2D analogue of "separation after rounding" and of a drift bound on an iterated scheme; no 3D version found |
| J. Hershberger, *Stable snap rounding*, CGTA 46(4), 403-416, 2013 (SoCG 2011) [S] | a variant whose result is stable under re-rounding | | relevant to chained booleans (rounding a rounded result again), 2D only |
| S. Fortune, *Vertex-rounding a three-dimensional polyhedral subdivision*, DCG 22(4), 593-618, 1999, doi:10.1007/PL00009480 [S] | a subdivision on a grid of a small power-of-two spacing that embeds the input's vertices, edges and facets | O(n^4) vertices, worst case and in practice (Milenkovic and Sacks [V]); the grid is finer than the input's by a factor that grows with n | the first 3D guarantee; the finer grid is the problem: our output grid is fixed (doubles) |
| O. Devillers, S. Lazard, W. Lenhart, *3D snap rounding*, SoCG 2018, LIPIcs 99:30; *Rounding meshes in 3D*, DCG 64(1), 37-62, 2020, doi:10.1007/s00454-020-00202-2 [S] | n polygons with disjoint interiors map to a simplicial complex on the integer grid; a continuous motion moves each face within L∞ distance 3/2; points that become equal stay equal; relative positions kept | O(n^15) elements and O(n^19) time worst case by the SoCG 2018 abstract, O(n^5) and O(n^6) under assumptions; a search snippet of the journal version gives O(n^4 √n) for nice discretizations of smooth surfaces. SAFE_ROUNDING.md section 3 and SNAP_ROUNDING_3D.md quote O(n^13) (to O(n^15)) and O(n√n) in practice, also from abstracts: the exponents need the full text | **the only full guarantee on a fixed grid.** No implementation known. No lower bound on the output size found in the abstracts |
| V. Milenkovic, E. Sacks, *Geometric rounding and feature separation in meshes*, CAD 108, 2019, arXiv 1805.03252 [V, SAFE_ROUNDING.md 8.1] | a certificate (2e-separation of disjoint features) when the LP expansion converges; output size = input size | no a-priori displacement bound ("we lack a bound in terms of the input") | our closed option A |
| V. Milenkovic, L. Nackman, *Finding compact coordinate representations for polygons and polyhedra*, SoCG 1990 [S] | rounding that keeps the combinatorial structure is NP-complete (exact statement not verified) | | why a guarantee needs the freedom to refine or to collapse |
| S. Valque, S. Lazard, *Resolving self-intersections in 3D meshes while preserving floating-point coordinates*, CGF 44(5), 2025, doi:10.1111/cgf.70197; Valque's thesis 2024 [V, SAFE_ROUNDING.md section 7] | the heuristic (paper): none a priori, a posteriori intersection-free when the loop ends before its limit. Thesis part II: **a local version of Devillers et al.**, certified when it ends | heuristic: 4523 of the 4524 self-intersecting Thingi10K models with the default grid, the last with a coarser one. Local certified version on the 520 models whose rounding intersects during the motion: 333 (64 %) within 1 h with full certification, 94 % only with a relaxed test that drops the topological guarantee; the author calls the implementation unstable | the heuristic is our closed option B; part II is the closest prior art to a local guarantee (section 2.4) |
| B. Lévy, *Exact predicates, exact constructions and combinatorics for mesh CSG*, arXiv 2405.12949 (v2 2025), ACM TOG, doi:10.1145/3744642 [V, earlier notes; v2 not re-read] | exact co-refinement and Weiler model; names snap rounding "the main missing component" | | confirms the problem is open in the closest BSD system |
| J. Nehring-Wirxel, P. Trettner, L. Kobbelt, *Fast exact booleans for iterated CSG using octree-embedded BSPs*, CAD 2021, arXiv 2103.02486 [S]; P. Trettner, J. Nehring-Wirxel, L. Kobbelt, *EMBER*, ACM TOG 41(4), 2022 [V, earlier notes] | plane-based representation with fixed-width integers: exact and closed under iterated CSG **as long as the output stays plane-based** | | they avoid the rounding by never producing vertex coordinates in doubles; not a rounding result |
| Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh arrangements for solid geometry*, TOG 35(4), 2016 [M; the 2.19 % from a search snippet of the paper, page not checked] | none: round, check, resolve again; 2.19 % of Thingi10K outputs intersect after plain rounding | | the "verify and repeat" baseline |

Terms searched without a newer result: "3D snap rounding" 2023-2026, "rounding meshes in 3D lower bound output size",
"Lévy snap rounding geogram 2025", "iterated snap rounding 3D", "feature separation rounding Thingi10K". The newest
work found is Valque and Lazard 2025 and the CGAL 6.1 iterative snap rounding that implements it (documentation only).

### 2.3 An observation: the known guarantee fits our budgets

Not stated in the papers in this form (inference, checked by hand; worth a Z3 or a written lemma before it is used):

1. **Area lemma.** On a grid of spacing g, a triangle with grid vertices that is not degenerate (corners not collinear)
   has area at least g²/2: its cross product is g² times a non-zero integer vector, whose length is at least 1.
2. **Two diagonals.** The degenerate test is area < `DegenerateArea` · D_a² with `DegenerateArea` = 1e-12 and D_a the
   diagonal of the box of all triangles (`areaSize` in `MeshRoundingRepair`, as in `NotCleanReason`); `maxLength` is
   1e-5 · D_c with D_c the diagonal of the largest component (SAFE_ROUNDING.md section 1, "Scale"). D_a ≥ D_c, up to
   100 D_c before the components count as far apart.
3. **The window for g.** The area test needs g²/2 ≥ 1e-12 D_a², so g ≥ 1.414e-6 D_a. Devillers et al. move each face
   within L∞ 3/2 grid units, so within (3/2)√3 g ≈ 2.6 g in the Euclidean norm, which must stay ≤ 1e-5 D_c, so
   g ≤ 3.85e-6 D_c. The window [1.414e-6 D_a, 3.85e-6 D_c] has the ratio 2.72 D_c / D_a, and a grid of doubles needs g
   to be a power of two. A power of two lies in every interval whose ratio is at least 2, so **a fitting g exists when
   D_a / D_c ≤ 1.36**, and may not exist above that (by the code's own comments, results of pieces side by side reach
   ratios of 1.6 to 16). Near the edges of the window the margin is small: g rounded up to the next power of two can
   give a drift of 9.9e-6 D_c, 1 % under `maxLength`; rounded down, the area can fall to 0.45e-12 D_a², below the test.
4. **Representability.** The grid points are doubles only if g ≥ ulp(max |x|), that is max |x| < 2^53 g, about
   1.2e10 D_a for g near 2^-19 D_a. The coarse UTM cluster of SAFE_ROUNDING.md section 5 (scale 1e-3 at coordinates up
   to 5.7e6, max |x| about 3e9 D_a) is still inside; parts further from the origin relative to their size are not.

Consequence: **for results with D_a / D_c ≤ 1.36 and max |x| below about 1e10 D_a, existence is not the open question for our bound.** An
embedded result within `maxLength` whose non-collapsed triangles pass the degenerate test exists. It need not pass
`NotCleanReason`: the motion lets features thinner than the grid collapse onto each other, so a thin wall attached to a
larger part can end as coincident faces or a non-manifold contact, which D′ avoids by removing only whole thin
components. What no one has is (a) a size bound anywhere near the exact result's size m, (b) an algorithm that rounds
only where the plain rounding fails and always ends (the global scheme moves every vertex by up to 2.6 g, which breaks
D′'s promise that untouched triangles are the exact result, rounded, and would change every T6 result; Valque's local
version does not always end, section 2.2), and (c) a statement about what the collapses do to closedness and manifoldness.

Caveats: the theorem's input is polygons with disjoint interiors; our exact result has shared edges and vertices, which
is what "a polyhedral complex" means in their setting [S, to be checked in the full text]. The L∞ reading of their 3/2
is from the abstract [S].

### 2.4 What is proven, what is open

- **Proven:** existence with bounded drift on a fixed grid (Devillers et al.), polynomial but huge worst-case size; 2D
  snap rounding with near-optimal output-sensitive algorithms; structure-preserving rounding without refinement is
  NP-hard (Milenkovic and Nackman, [S]) and can be infeasible (a Schönhardt configuration inside one grid cell, Diazzi et
  al. 2023 [V there]).
- **Open:**
  - any lower bound on the output size of 3D rounding beyond the trivial Ω(m). We found none. The gap is between Ω(m)
    and O(n^15) (O(n^5) under assumptions); a quadratic or cubic lower-bound construction would itself be a result;
  - a scheme whose output is O(m polylog m), or O(m + k) with k a local density measure, with a continuous-motion or
    isotopy-type statement;
  - a **local** guarantee that always ends: Valque's thesis (part II) has a local version of Devillers et al. on the
    doubles' own grid (scaled to [2^51, 2^52)), certified where it ends, but 187 of 520 hard models did not end within
    1 h. Open is a local scheme with a fixed, already-rounded collar that provably ends, and its behaviour on a coarse
    grid where the area lemma holds;
  - stability under chained booleans (Hershberger's 2D result has no 3D version).

### 2.5 What a new result would have to show

A result is new if it proves, for every closed, embedded exact result E with m triangles and every δ above a stated
multiple of the grid (for us δ = `maxLength`), an output R in doubles with:
1. R closed, oriented, embedded (no proper intersections);
2. a continuous motion from E to R within δ in which points that meet stay together (the Devillers statement), which
   gives the Hausdorff bound and forbids new topology except collapses of features thinner than δ;
3. no triangle below the degenerate area (section 2.3 makes this automatic on a coarse enough grid);
4. |R| = O(m polylog m), or O(m) outside a set of clusters whose size is bounded by a measure of E;
5. the triangles of E far from every defect unchanged up to the nearest-double rounding (the locality our users see).

Items 1-3 are known together; 4 and 5 are the open part. A lower bound showing 4 impossible (a family of E that needs
ω(m polylog m) triangles for 1-3) would be equally publishable and would close the direction.

### 2.6 First experiment (can fail in 2-3 days)

**Local coarse-grid rounding of the defect clusters.** In `benchmarks/SnapRoundingExperiment` (not the library):
1. take the RC4b cases (ucd p0007 and iud p0020 difference, the clean bound cases) and the 19 generated leftovers of
   SNAP_ROUNDING_3D.md section 10;
2. per defect cluster, cut a patch with a collar of k rings; snap the patch's vertices to the power-of-two grid g of
   section 2.3 (only cases with D_a / D_c ≤ 1.36; record the others as outside the window), keep the collar's doubles; re-resolve the patch exactly (the existing arrangement on the
   patch only, as option B's local variant of SAFE_ROUNDING.md 8.6 proposed);
3. record: clean or not, the measured Hausdorff distance (the existing certified upper bound), the triangle count of the
   patch before and after, and whether the collar boundary had to move.

Go / no-go, fixed in advance:
- **No-go** if fewer than half of the RC4b and generated leftovers become clean within `maxLength`, or if the patch size
  grows more than 10 times on any case: then the coarse grid alone is not the missing lever, and the direction stops at
  this note.
- **Go for a theory effort** only if nearly all become clean and the failures are at the collar: then the open question
  is exactly item 5 of section 2.5, and a design note with the local lemma comes next (a Z3 pilot on small patches, as
  for D1).

The experiment changes no library result. What is new against what was done: option B and its patch estimate (SAFE_ROUNDING.md
8.6, 8.7) and Valque's local certified version used the doubles' own fine grid; this uses the coarse grid of section 2.3,
so that the area lemma and our drift bound hold together, and the measure is our clean test, not only intersection-freeness.
If the patch-wise B of 8.7 is rebuilt for it, the cost side is already estimated there.

### 2.7 Risk, effort, and breakthrough vs engineering

- **Engineering:** a local guaranteed fallback for RC4b-type clusters (section 2.6 turned into an option, off by
  default). Medium risk, 1-2 weeks after a go, tier L (rounding shards). Gain on real data: the five RC4b differences of the
  triage and none of the 294 T6 results (270 clean, 24 correctly empty).
- **Breakthrough:** item 4 (or its impossibility) of section 2.5. High risk, months, with a mathematician-level proof
  effort; the payoff is a paper, and for the library the right to say "always clean within the bound".

## 3. Direction 2: a certifying mesh boolean

### 3.1 What we already have (not presented as new)

- The output side is already checked: `MeshRobustRepair.NotCleanReason` (closed, manifold, no degenerate triangles, no
  self-intersections by an exact search), winding-closedness, and the volume identities in the BooleanComparison and
  abc-chained harnesses (BOOLEAN_TRIAGE_2026-11.md).
- Certificates elsewhere in the library: `SeparationCertificate` (Milenkovic-Sacks separation, tests),
  `ContouringTopologyCertificate`, the certified Hausdorff upper bound, the Meshing2D oracle witnesses; the review
  checklist's rules for certificates (REVIEW_CHECKLIST.md: the reference must be the documented one, #935; one mutation
  test per topological condition).
- The certifying-algorithm idea was named before for the Meshing2D oracle (roadmap/research.md, hard problem 7,
  McConnell et al. 2011) and as a transfer in [CROSS_FIELD_GEOMETRY.md](CROSS_FIELD_GEOMETRY.md) (ideas 4 and 6).
- **What is not checked today:** that the output is the boolean of the inputs. The volume identities are a necessary
  condition, measured in floating point; they do not catch a wrong patch whose volume cancels, and they need all four
  operations.

### 3.2 Prior art

| Work | What it gives |
|---|---|
| R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 119-161, 2011 [S] | the paradigm: output plus witness, a checker much simpler than the algorithm; survey |
| K. Mehlhorn, S. Näher, M. Seel, R. Seidel, T. Schilz, S. Schirra, C. Uhrig, *Checking geometric programs or verification of geometric structures*, SoCG 1996, 159-165; CGTA 12, 85-103, 1999, doi:10.1016/S0925-7721(98)00036-4 [S; content M] | checkers for convex polytopes, triangulations, Delaunay triangulations in LEDA; the local-to-global argument (locally convex and one point inside implies convex; local Delaunay implies global) |
| O. Devillers, G. Liotta, F. P. Preparata, R. Tamassia, *Checking the convexity of polytopes and the planarity of subdivisions*, CGTA 11(3-4), 187-208, 1998 (WADS 1997) [S] | checkers for planar subdivisions and convex subdivisions, with the arithmetic degree of the checker as a cost measure |
| L. Palios, *Verification of partitions of 2d and 3d objects*, University of Ioannina [S] | O(n log n) verification that a set of polyhedra partitions a polyhedron or forms a cell complex, by matching facets |
| E. Alkassar, S. Böhme, K. Mehlhorn, C. Rizkallah, *A framework for the verification of certifying computations*, J. Automated Reasoning 52(3), 241-273, 2014 (CAV 2011) [S] | the checker itself formally verified (VCC, Isabelle/HOL) |
| A. Fouilhé, D. Monniaux, M. Périn, *Efficient generation of correctness certificates for the abstract domain of polyhedra*, SAS 2013, arXiv 1304.0864 [S; authors and venue M] | untrusted fast polyhedra library, Coq-verified checker of inclusion certificates: the same architecture, for convex polyhedra |
| Q. Zhou et al. 2016 (above) [S]; A. Jacobson, L. Kavan, O. Sorkine-Hornung, *Robust inside-outside segmentation using generalized winding numbers*, TOG 32(4), 2013 [S] | winding numbers of arrangement cells propagated across patches by ±1 jumps: the local rule a classification certificate checks |
| N. Wetzler, M. Heule, W. Hunt, *DRAT-trim*, SAT 2014 [M]; Y. K. Tan, M. Heule, M. Myreen, *cake_lpr*, TACAS 2021 [M] | the closest working practice outside geometry: every unsatisfiable answer of a competition SAT solver comes with a proof checked by a verified checker |
| B. Chazelle, *Convex partitions of polyhedra: a lower bound and worst-case optimal algorithm*, SIAM J. Comput. 13(3), 488-507, 1984 [S] | polyhedra that need Ω(n²) convex pieces: a lower bound on any witness built from convex cells (section 3.4) |

Terms searched without a result: "certifying mesh boolean", "certificate checker polyhedra arrangement verification",
"winding number certificate jump condition checker", "formally verified CSG / polygon boolean" (the nearest is a 2025
ITP paper verifying a vertical cell decomposition in Rocq [S], and Coq polyhedra libraries). **Nothing found that
certifies a 3D mesh boolean, a 3D mesh arrangement, or the rounding of one to doubles.**

### 3.3 What a certificate could contain for our pipeline

Notation: A, B the closed input meshes (doubles), E the exact result, R the rounded result. Each part names what the
checker verifies, its cost, and what it trusts.

| Part | Certificate | Checker | Cost | Trusts |
|---|---|---|---|---|
| C1 pieces | for every arrangement triangle, its parent input triangle and exact (rational) corners | every piece lies in its parent's plane and inside it (exact orientation tests); the pieces of a parent tile it (signed areas sum exactly to the parent's, plus edge matching as in Palios) | O(k log k) for k pieces | exact predicates on rationals |
| C2 embedding | that no two pieces of different parents cross except along shared edges | **(a)** recheck by a simple exact pair search (a uniform grid of boxes, exact tests); **(b)** a witness: a tetrahedralization of a box around A ∪ B in which every piece is a union of faces; the checker verifies positive orientation of every tet, facet pairing, and that the boundary is the box (the Mehlhorn local-to-global argument in 3D) | (a) output-sensitive in practice, no near-linear worst case known to us; (b) O(T) for T tets, but T can be Ω(n²) | (a) a broad phase, which is simple to argue conservative; (b) nothing beyond orientation tests |
| C3 classification | a winding pair (w_A, w_B) for each side of each patch | the two sides of a patch of A differ by exactly the patch's orientation in w_A (and not in w_B), likewise for B; around each intersection edge, consecutive patches in the exact radial order agree on the wedge between them; one anchor per connected component of A ∪ B: the unbounded side has (0, 0), and a nested component gets its value from one exact ray to a component outside it | linear, plus one ray per nested component (O(n) each; the anchors are the non-local part) | C1, C2, the exact radial sort |
| C4 selection | none beyond C3 | a patch is in E iff its two sides map to different values of the operation's predicate, oriented to the "in" side | linear | C3 |
| C5 rounding | R, and per triangle of R a list of triangles of E, and the reverse | R closed, oriented, not degenerate, embedded (as C2 on doubles, which is cheaper); H(R, E) ≤ δ through coverage: the distance to a convex set is convex, so a triangle of R lies within δ of one triangle of E iff its three corners do (exact squared distances); a triangle that needs several is split by the witness (the coverage idea of CROSS_FIELD_GEOMETRY.md idea 4, Wang et al.) | linear in the witness | exact distance comparisons |

What the certificate does **not** cover: open operands (the generalized winding number is not an integer; #528 classifies
per piece, outside C3's integer jumps) and the D′ removal of tiny and thin components (a removed component would need its
own witness: its volume and thickness bounds, SAFE_ROUNDING.md section 1). The certificate would state the closed-input
case and say so, as the contouring certificate states its class.

### 3.4 Checker cost and a limit

- C1, C3, C4 and the closedness part of C5 are linear (C3 up to the anchors), simple, and need only exact orientation and
  distance tests: a checker of a few hundred lines that could even be formally verified (Alkassar et al.).
- C2 is the hard part, as everywhere in this field: missing an intersection is the classic boolean failure, and checking
  that n triangles do not cross has no near-linear worst-case algorithm that we know of. A witness of convex cells
  (C2b) cannot be near-linear in the worst case: Chazelle's polyhedra need Ω(n²) convex pieces. So "near-linear in the
  worst case" is out of reach with that witness type; the realistic claim is near-linear on the inputs we see, with a
  checker whose correctness is simpler than the arrangement's.
- Producing C2b needs a constrained tetrahedralization of the arrangement (Diazzi et al. 2023 show a robust one in
  practice, from the paper only), which may cost more than the boolean.

### 3.5 Trust gain

- The arrangement and the classification are the parts of the pipeline that no current check relates to the inputs.
  The triage found no defect there ("No case points at the arrangement, the classification or the assembly"), so the
  gain is assurance, not a known bug class.
- **For testing:** C3/C4 is a sound oracle for the robustness round: a fuzzing or CsCheck campaign could check every
  result against its inputs, where today the volume identities (floating point, four operations) are the oracle.
- **For users:** a "certified" mode for users who need certification (medical, CAD; roadmap/research.md, bit-for-bit
  determinism item). Only C1-C5 together, with C2 by a witness or by a simple recheck, would carry that name.
- **What it costs us:** the certificate's data (parents, winding pairs) exist inside `ExactMeshArrangement` and the
  classification already; exporting them is new API (an owner decision) or an internal test hook (no API change).

Stages, if the owner wants this: (1) C3 + C4 as a test-only checker over the internal data (no API); (2) C1 and C5's
closedness and coverage; (3) C2a as a simple exact recheck; (4) C2b, a witness, only as research.

### 3.6 First experiment (can fail in 2-3 days)

A test-only checker for C3 + C4 on the internal classification data of `ExactMeshBoolean`, run on the T6 cases, the
abc-chained cases and the CsCheck generators, then fed with mutations (flip one patch's winding pair, swap two patches
in a radial order, drop a nested component's anchor, swap A and B in a difference).

- **Fails** if the checker needs more than linear work in practice (the anchors of nested components dominate), if it
  must re-run most of the classification (then it is not simpler than the algorithm and adds no trust), or if a mutation
  survives that the volume identities catch.
- **Passes** if it is linear, kills every mutation, and runs in a few percent of the boolean: then stage 1 is a cheap
  addition to the robustness round's oracle.

### 3.7 Breakthrough vs engineering

- **Engineering:** stages 1-3. The parts are known; the contribution is putting them together for a 3D exact boolean
  with rounding, which we found nowhere. Publishable as a systems / tool paper at most.
- **Breakthrough:** a certificate of the embedding (C2) whose size and checking time are near-linear in the worst case,
  by a witness type that is not a convex-cell decomposition (Chazelle's bound rules those out), or a lower bound showing
  that no such certificate exists in a natural model. Also new: a certificate for the rounding (C5) that includes the
  isotopy-type statement of section 2.5 item 2, not only Hausdorff and embedding.

### 3.8 Stages 1-2 built (2026-11-17)

`tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs`, described in [QA.md section 2.4a](../QA.md): C1, C3, C4 and
C5's closedness and coverage, test-only, over an internal recording of the boolean (no API, no change of behaviour).
Against the criteria of section 3.6:
- **Linear in practice:** C1, C3 and C4 are linear in the pieces plus one ray per anchor; the anchors were one per
  connected set of pieces (1-2 on the test solids, more only with a Difference's early-out, which needs one per connected
  set under its comparison rule). On two overlapping spheres (696 pieces, warm) C1 + C3 + C4 took about 30 ms against
  4 ms of the boolean, C5 about 65 ms: **not** a few percent of the boolean, as BigInteger decides every sign without a
  floating-point filter. Fine for a test oracle; a certified mode for users would need filters (and their bounds).
- **Not a re-run of the classification:** no ray per patch; the classification's carried, per-patch and filtered paths
  are not used. The coplanar stacks are recomputed (quadratic per plane, fine for the test meshes).
- **Every mutation killed:** the four of section 3.6 (a patch's winding pair off by one, two patches' pairs swapped, a
  nested component without its anchor, A and B swapped in a difference) and those of the selection, the pieces and the
  result, and a wrongly culled component (counts in QA.md 2.4a). A nested component read as outside is caught only by
  its anchor ray, as expected. The review found that the first version trusted the early-out's culling (a component
  culled by mistake was invisible); C0 now checks every culled triangle against the culling mesh.
- **Limits found:** the early-out of an Intersection culls against both meshes, and a comparison rule for that case
  would be circular (a region that is wrongly read as outside both meshes could pass): such runs are `n/a`, the oracle
  runs Intersection without the early-out. Open inputs are out of the class (section 3.3).

## 4. Sources (search results used for the [S] marks)

- Devillers, Lazard, Lenhart: [SoCG 2018](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.SoCG.2018.30),
  [DCG 2020 (HAL)](https://hal-univ-tlse3.archives-ouvertes.fr/INRIA/hal-02549290).
- Fortune 1999: [Bell Labs record](https://www.nokia.com/bell-labs/publications-and-media/publications/vertex-rounding-a-three-dimensional-polyhedral-subdivision).
- Goodrich et al. 1997: [dblp](https://dblp.uni-trier.de/rec/conf/compgeom/GoodrichGHT97.html).
- Halperin and Packer 2002: [project page](https://www.cgl.cs.tau.ac.il/projects/iterated-snap-rounding/); Hershberger 2013:
  [record](https://portal.mardi4nfdi.de/wiki/Publication:1940702).
- Milenkovic and Sacks: [arXiv 1805.03252](https://arxiv.org/abs/1805.03252).
- Valque and Lazard 2025: [CGF](https://onlinelibrary.wiley.com/doi/10.1111/cgf.70197), [HAL](https://inria.hal.science/hal-04907149).
- Lévy: [arXiv 2405.12949](https://arxiv.org/abs/2405.12949).
- Nehring-Wirxel et al. 2021: [arXiv 2103.02486](https://arxiv.org/abs/2103.02486).
- Mehlhorn et al. 1996/1999: [MPG record](https://pure.mpg.de/pubman/item/item_1331069_1); Devillers et al. 1998:
  [Brown TR CS-97-11](https://cs.brown.edu/research/pubs/techreports/reports/CS-97-11.html).
- McConnell et al. 2011: [record](https://portal.mardi4nfdi.de/wiki/Publication:465678); Alkassar et al.:
  [CAV 2011](https://link.springer.com/doi/10.1007/978-3-642-22110-1_7).
- Palios: [core.ac.uk](https://core.ac.uk/download/132460789.pdf); Fouilhé et al.: [arXiv 1304.0864](https://arxiv.org/abs/1304.0864).
- Chazelle 1984 (cited through a later paper): [Chazelle polyhedra](https://oa.tib.eu/renate/items/0a958281-67de-4318-8a9d-d6aab97765fc).
- Zhou et al. 2016: [project page](https://www.cs.columbia.edu/cg/mesh-arrangements).
