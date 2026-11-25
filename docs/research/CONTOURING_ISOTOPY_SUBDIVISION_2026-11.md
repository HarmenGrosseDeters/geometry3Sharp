# Contouring spike: certified isotopic approximation by subdivision as the proof for options B and C

Date: 2026-11-22 (Berlin). Research spike (owner go 2026-11-22): this note and a measurement prototype
([benchmarks/ContouringIsotopySubdivision](../../benchmarks/ContouringIsotopySubdivision/README.md)). No library code
changed. Covers plan #1289 (contouring certificate, thin features and options B / C) and item 5a of
[UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md); it continues track C of
[CONTOURING_SDF_SUBGRID_2026-11.md](CONTOURING_SDF_SUBGRID_2026-11.md), which counted cells and built no certificate.

Question: does certified isotopic approximation by subdivision (Plantinga and Vegter for surfaces; Burr and Byrd,
arXiv 2302.04908, for several curves) give the reviewed proof that options B and C of
[CONTOURING_TOPOLOGY.md](../design/CONTOURING_TOPOLOGY.md) lack, and what does it certify in practice?

## 1. Answer in short

| Option | Does the method give the proof? | What was measured |
|---|---|---|
| **B, smooth fields** (TPMS, analytic shapes, a level of one C1 function, a `min` / `max` where every cell that meets a piece boundary is zero-free) | **Yes, by composition.** The premise was checked against both of Plantinga and Vegter's papers (section 4.3): it holds for the published form of the test and not for the pairwise tightening. If every cell of the uniform grid passes C0 or C1 and the node signs are certified, Plantinga and Vegter's theorem gives level set ~ marching-cubes-type mesh, and our Theorems 1 and 2 give that mesh ~ the `DualContouring` output (section 4). No edge or face test is needed, so the tangencies with the grid that Theorem 5 refuses are covered. The published 3D argument for the regular grid is a geometric sketch of about two pages, in the journal version too | section 5: the whole run certified in 26 of 176 runs with the published form of the test (28 with a pairwise tightening that the paper does not cover), never on a wrong contour (for 5 and 6 of them the truth is the certified run itself); capped TPMS at 32 cells per period 2 of 3 (3 of 3 tightened; Theorem 5's cell test: 0 of 3, 97.5 % of the cells); the test alone costs 0.3 to 0.5 times the contouring there |
| **B, non-smooth level sets** (creases of CSG, offset creases, thin walls given as one field) | **No.** C1 is false at every depth at a crease of 90 degrees or more, and the theorem is stated for smooth functions (its authors: not for surfaces that are not C1). Theorem 5 (a cone below 180 degrees, Clarke gradients) stays the proof there. Burr and Byrd's test for several curves is planar; nothing exists for surfaces | section 5: tunnel and rod (creases): 0 of 36 runs; every leaf left open four levels below the grid holds a non-smooth point |
| **C, winding number of a broken mesh** | **No.** The two things option C misses (enclosures of w and of grad w; any statement at a defect edge) are not supplied by a subdivision method, and in the clean regions the level set is the input's triangles, where C1 fails and the exact class P test is the right tool | not measured (nothing to run) |

Leaning (section 8): have the composed proof reviewed (a docs step, now with both papers read); if it holds, build
class S for smooth fields as the C0 / C1 cell test in its published form with Theorem 5 as the second test per cell, `TpmsLattice` first. Option C stays stopped.

## 2. The method (paper text only, in our words)

Read status: **R** read in full text for this spike, **S** from a paper that restates it. No code was opened.

- **Plantinga and Vegter**, *Isotopic approximation of implicit curves and surfaces*, SGP 2004; *Isotopic meshing of
  implicit surfaces*, The Visual Computer 23, 2007. Both **R** (added after the first version of this note: the 2004 extended abstract from a copy at cims.nyu.edu, the
  2007 paper as text supplied by the owner; section 4.3). Both also restated in Boissonnat, Cohen-Steiner, Mourrain, Rote, Vegter, *Meshing of Surfaces*, HAL hal-00488274, sections
  1.2.3 and 1.2.4 (**R**; Vegter is a co-author), in Burr, Gao, Tsigaridas, arXiv 1801.05864 (**R**), in Lin's and
  Galehouse's theses (NYU 2011, 2009; **R** in parts) and in Burr and Byrd (**R**)).
  - Tests on a box B, with interval functions for f and its gradient: **C0**: 0 is not in the interval of f over B.
    **C1**: 0 is not in the interval inner product of the gradient over B with itself, where each factor is an
    independent copy (so a component interval [a, b] contributes min(a^2, ab, b^2), not 0): no two points of B have
    orthogonal gradients.
  - Algorithm: split boxes into eight until C0 or C1 holds; balance the octree (neighbours differ by one level);
    evaluate the sign of f at the corners (a zero counts as positive); put one vertex on every edge whose ends differ
    in sign; join the vertices on each face by arcs, group the arcs into loops on the box, triangulate each loop.
  - Ambiguous faces (alternating corner signs) can occur in 3D under C1 (the condition does not carry over to a face).
    The chapter states that the two arcs may be chosen arbitrarily on such a face, as long as both boxes of the face
    use the same choice, and that the two choices give isotopic meshes.
  - Theorem: for f with a bounded, non-singular zero set inside the root box, the mesh is ambient isotopic to the zero
    set. The isotopy is **not** cell by cell: the surface may cross an edge twice or dip through a face and come back
    (an *excursion*); under C1 an excursion stays inside the neighbouring box, and the proof first pushes excursions
    back (an isotopy supported in the two boxes), then straightens the surface inside each box. The 3D proof is
    described by the later authors as a case analysis by hand. No erratum was found. An independent 3D proof under a
    weaker test exists (Lin, Yap, Yu, below).
- **Burr and Byrd**, *Certified simultaneous isotopic approximation of curves via subdivision*, arXiv 2302.04908v2
  (**R**; preliminary version ISSAC 2023). Planar curves only. It keeps C0 and C1, defines an excursion (a component
  of the curve in a box with both ends on one edge; under C1 it lies in the half disk over that edge), and adds a
  third test for a pair of curves: no gradient of one is parallel to a gradient of the other over a neighbourhood of
  the box (two steps of neighbours, bounded by the box scaled by 13), so that an excursion cannot hide an intersection.
  The output is one ambient isotopy for all curves. It assumes smooth curves, transversal intersections and no triple
  point; the subdivision does not end at a singular point or a tangential intersection. The paper states nothing for
  surfaces. Its predicates are evaluated exactly on dyadic points (arbitrary precision); rounding is not discussed.
  Its examples: the extra test costs 1.4 to 6 times the boxes of two separate runs (1768 against 1288, 2566 against
  418 for nearly parallel curves, 673 against 367).
- **Lin, Yap, Yu**, *Non-local isotopic approximation of nonsingular surfaces*, CAD 45(2), 2013 (**R**, preprint);
  Lin's thesis. A weaker box test (one partial derivative excludes 0, Snyder's parameterizability) with the same
  non-local argument. Two consequences matter here: under that weaker test an arbitrary choice on ambiguous faces is
  **not** enough (they fix the arcs by direction and split ambiguous boxes), and faces on the boundary of the region
  need a 2D test. So the weaker test does not certify a mesh with `DualContouring`'s face rule; C1 does.
- **Finite precision.** Cucker, Ergür, Tonelli-Cueto, arXiv 2004.06879 (**R**): a floating-point version of the
  subdivision for polynomials (thresholds on a scaled f, a precision that grows as boxes shrink) is correct in the
  sense that every output box satisfies C0 or C1; corner signs and meshing are not analysed there. Xu and Yap, arXiv
  1905.03505 (**R** in parts): correctness must be shown for the rounded ("effective") predicates, with one-sided
  tests. Both say the same thing our rule says: the proof uses only the *facts* C0 and C1, and a rounded test is sound
  if a pass implies the fact.
- **Complexity.** Burr, Gao, Tsigaridas (**R**): the number of boxes is an integral of the inverse local box size,
  which is governed by the distance to the nearest (complex) singular point of f and of (x, y) -> <grad f(x), grad
  f(y)>; the subdivision does not terminate exactly if f has a real singular point in the box. Cucker et al.:
  polynomial on average and smoothed.

## 3. The predicates and what the library can evaluate soundly

Building blocks found (searched before the prototype): `ExactInterval` (`math/ExactIntervals.cs`, internal: round to
nearest and widen by one unit in the last place; +, -, *, /, square root, square, a sign that is a proof), used by
the terrain slope code, `MeshOffset` and the Hausdorff bound; no interval sin / cos and no interval evaluation of an
`ImplicitFunction3d` anywhere. `TpmsLattice` carries a proven gradient bound (`LipschitzBoundUsed`), `MeshOffset` a
1-Lipschitz band bound (`CertifyBand`), and [MESH_OFFSET_ENCLOSURE.md](../design/MESH_OFFSET_ENCLOSURE.md) a reviewed
enclosure of the generalized gradients of an offset field (Theorem E, probe shelved). `DualContouring` exposes its grid, pattern and face
rule for a certificate (`RecordCertificateData`, `ContourGrid()`, internal). The prototype copies the semantics of
`ExactInterval` into the benchmark, as `benchmarks/HausdorffBenchmark` does, because the type is internal.

| Input | Value enclosure over a box | Gradient enclosure over a box | C0 / C1 usable? |
|---|---|---|---|
| TPMS (`TpmsLattice`: gyroid, Schwarz P, diamond, ...; sheet and skeletal styles) | yes: interval sin / cos, built in the prototype from +, -, *, / only (no libm) | yes, the same way. The proven Lipschitz bound alone gives C0 only | yes for a skeletal level (one smooth function). A sheet `abs(g) - c` is a `max` of two levels: per level yes, as one field no (section 6) |
| Analytic shapes and operators of `implicit/` (sphere, box, `min` / `max`, offset, blend) | to be written per class (a new optional interface, as stage 3 of the design note plans) | smooth pieces: yes; `min` / `max`: the hull over the pieces that can be active on the box, decided from enclosures | C1 only in cells where one piece is active and f is C1; at a crease on the level set never |
| Sampled grids (`DenseGridTrilinearImplicit`, `CachingMeshSDFImplicit`) | of the trilinear interpolant: exact from the 8 samples | of the interpolant: exact per cell (hull of edge differences); it jumps across faces | for the interpolant: the classical exact case analysis (Chernyaev, Nielson), not C1. For the sampled function: only with a bound of its second derivatives (below) |
| Mesh offsets (`MeshOffset`, F = d - signed distance) | yes (`CertifyBand`'s bounds) | yes: Theorem E, exact sign tests on corner differences; loose for large triangles (the shelved probe) | yes where the nearest point is unique in the cell (smooth parts); not at concave offset creases |
| Winding number of a clean closed mesh | piecewise constant | none: the level set is the input surface | no. Class P (exact counting) is the tool, built |
| Winding number of a broken mesh | missing (interval atan2 or the antipodal form) | missing; unbounded at defect edges | no at defect edges and on input triangles; in free-space cells only once the enclosures exist |

**Sampled data without an analytic f.** Three levels of statement are possible:

1. *About the interpolant.* The level set of the trilinear interpolant is known exactly per cell from the 8 samples
   (the face and interior ambiguity tests of the marching-cubes literature). That certifies the mesh against the
   interpolant and says nothing about the sampled function (the design note, section 1).
2. *With a bound M of the pure second derivatives of f* (an assumption the caller must supply; it cannot be read off
   the samples). Tensor-product linear interpolation on a cell of size h has value error at most 3 M h^2 / 8 and
   gradient error at most M h per component (the sum of the three one-dimensional errors: h^2 M / 8 each for the value;
   h M / 2 for the differentiated direction and h M / 4 for each of the other two). Adding these to the exact
   enclosures of the interpolant gives enclosures of f and of its gradient, and C0 / C1 apply as for an analytic
   field. The price is resolution: C1 needs the gradient error well below the gradient, so about M h < |grad f| / 2;
   for a gyroid of period P (|grad f| about 2 pi / P at the surface, M about 2 (2 pi / P)^2) that is P / h > 25 cells
   per period before any curvature of the surface itself, against 16 to 32 with the analytic gradient (section 5).
   This is arithmetic, not a measurement.
3. *With a Lipschitz bound only* (a signed distance grid: 1-Lipschitz). The value enclosure f(node) +- distance proves
   cells empty (C0, what `TpmsLattice` and [plan #1276](../plans/dc-coarse-first-construction.md) use) and nothing else: a Lipschitz bound cannot prove a crossing
   pattern (Kalra and Barr 1989, as the design note says). A distance field is not smooth where the nearest point
   changes, so no gradient enclosure follows from the samples.

## 4. Proof sketch for the library's setting

### 4.1 Setting and claim

- Ω is the box of the contouring grid and K the uniform grid on it. Its node coordinates are the doubles
  `origin + h * i` as `DualContouring` computes them; the cells are the closed boxes between consecutive coordinates.
- f is locally Lipschitz on a neighbourhood of Ω: a smooth (C-infinity) function, or `min` / `max` of smooth functions.
  L is its zero set in Ω. D is the raw `DualContouring` mesh on K (no cleanup, no simplification), contoured at iso
  value 0. For pieces that are only C1 (a capsule) the claim needs the premise in its C1 reading (section 4.3, row 4).
- For a cell c, **Z(c)**: f has no zero in c. **A(c)**: f is C1 on a neighbourhood of c (one piece is strictly active on
  all of c, hence near it) and <grad f(p), grad f(q)> > 0 for all p, q in c. **A+(c)**: A(c), and an axis-aligned box
  G that contains every gradient of f on c has a positive interval inner product with itself (the published form of
  the test: besides A it says that one partial derivative keeps its sign on c). These are facts about f; C0 and C1 are the rounded tests that prove
  them.

**Hypotheses** (none is assumed about f; what the prototype establishes of each is said in section 4.4):

- (P0) f is not 0 at any node, and the contouring's inside flag of every node is the true sign.
- (P1) every cell satisfies Z or A+ (the published form). The weaker "Z or A" is not covered (point 5 of section
  4.3).
- (P2) every cell of the outermost layer satisfies Z.
- (P3) the cells are cubes up to a stated tolerance: the largest and the smallest spacing of consecutive node
  coordinates, over all three axes, differ by a factor of at most 1 + 2^-20 (an exact comparison of doubles). The
  published argument is for a "uniform cubic grid" and uses the shape of the cell (a dip of the surface through a
  face cannot reach the opposite face); it tolerates a bounded aspect ratio, not an arbitrary one, and the computed
  coordinates `origin + h * i` are equally spaced only up to rounding.
- (M) D satisfies (H1) to (H4) of Theorem 2 of the design note (vertices strictly in their cells, manifold, no
  self-intersection, no polygon left out).

**Claim (Theorem 7, on the premise PV of section 4.3).** L is a closed C1 surface in the interior of Ω, and D is
isotopic to L, in the sense in which the premise gives an isotopy (ambient, if its deformations are read as ambient;
section 4.3).

No regularity is assumed: A(c) with p = q says grad f is not 0 on every cell that L meets, so 0 is a regular value
near L. A lower bound of |grad f| on L and the absence of singular points are what makes the tests *pass* at a finite
resolution; they are not needed for soundness.

### 4.2 Steps

1. **L is a closed surface.** By (P1), L lies in the A cells, where f is C1 with a gradient that does not vanish:
   L is a C1 surface (implicit function theorem). By (P2) it is compact without boundary inside Ω.
2. **Reduction to a smooth function.** Let S be the closed set of points of Ω where f is not smooth (the piece
   boundaries). S meets no A cell (one piece is strictly active on a neighbourhood of an A cell), so it has a positive
   distance δ from their union, and its δ-neighbourhood lies in Z cells only, where |f| >= ε > 0 (compact cells without
   a zero). Replace f in a smaller neighbourhood of S by a mollified f that differs from it by less than ε / 2, blended
   with a smooth cutoff. The new function is smooth (away from S the old one is a single smooth piece), has the same
   zero set and the same sign at every point, and equals f on a neighbourhood of the A cells. So Z, A+ and the node
   signs carry over, and f can be extended beyond Ω without zeros, because its sign is constant on the connected outer
   layer (P2): its zero set is bounded, as the premise asks. From here f is smooth.
3. **Level set to a marching-cubes-type mesh (premise PV).** Plantinga and Vegter's statement for the regular grid K
   (their section 5): let M be the mesh with one vertex on every edge of K whose end nodes differ in sign,
   arcs on the faces, on a face with alternating signs either of the two pairings (the same for both cells), and one
   disk per loop of arcs on a cell. Then M is isotopic to L.
4. **The contour is compatible.** By Theorem 2 with (M) and (P0), D is compatible with K ((E), (F), (C) of the design
   note, section 7.1); its pattern is the sign-changing edges and the face rule of `cell_patches` (inside corners stay
   apart).
5. **Mesh to mesh.** Take for M the pairing of D's face rule (admissible: it depends on the signs of the face only,
   so both cells agree). The paper's triangulation of a loop may touch the cell's boundary (a triangle edge between
   two edge midpoints of one face lies in that face); push the interior of every disk slightly into its cell, an
   isotopy that fixes the loops. M is then a closed PL surface that meets every edge of K at most once, every face in
   arcs between edge points, and every cell in disjoint disks with their interiors inside: it is compatible with K,
   with D's pattern, and by (P2) it avoids the outer layer, as D does. Theorem 1 (two compatible surfaces with the
   same pattern are ambient isotopic): D ~ M.
6. D ~ M ~ L. ∎ (sketch)

What this uses and does not use, against Theorem 5: no edge test (S1), no face test (S2), no count of circles (S3);
no Clarke calculus and no theorem of Brown, because the only non-PL step is the cited theorem; L need **not** be
compatible with K (it may cross an edge twice, or leave a closed curve on a face). The price: the stronger cell test
(any two gradients less than 90 degrees apart, against a cone below 180 degrees), C1 functions only, and a statement
about the whole grid only (section 4.5).

### 4.3 The premise PV, checked against the 2004 paper

Status: **checked on 2026-11-22 against both papers**: the extended abstract (Plantinga and Vegter, *Isotopic
Approximation of Implicit Curves and Surfaces (Extended Abstract)*, SGP 2004, 10 pages, from the copy at cims.nyu.edu
that the owner pointed to) and the journal version (*Isotopic meshing of implicit surfaces*, The Visual Computer
23(1), 2007, pp. 45-58, the version of record, text supplied by the owner). Both were read in full. Their section 5
(the regular grid) is the same argument, nearly word for word; the journal version adds the octree proof (its
section 6) and a tetrahedral variant (section 7). The note was first written from secondary sources; the points a
reviewer was asked to check, and what the papers say (quotes from the 2004 text; the 2007 text agrees unless noted):

| # | Point | Result | The paper (section 5, "Marching cubes revisited", unless noted) |
|---|---|---|---|
| 1 | The theorem holds for a uniform grid in which every cell passes the test | **holds** | The regular grid is treated first and on its own: "Let S be a regular, bounded, implicit surface, given as the zero set of a smooth function F"; "0 is a regular value of F"; for each cell C "0 ∉ □F(C) ∨ 0 ∉ <□∇F(C), □∇F(C)>"; conclusion: "the triangulation of the regular grid is isotopic to the implicit surface." The octree and its balancing come after (section 6) |
| 2 | Either pairing on a face with alternating signs | **holds** | "There are two ways to connect these vertices pairwise"; "Since both choices lead to the same isotopic approximation, we can choose either of them, as long as the triangulations for both cells fit together." The authors' own choice is by direction (segments parallel to (1,1,0), (1,0,1), (0,1,1)); "cells have at most one ambiguous face" |
| 3 | The mesh of a cell is one disk per loop | **holds on the regular grid** | Figure 7 gives the triangulation of the 9 possible corner-sign configurations (5 of the 14 are excluded by the test), each loop closed by a disk. Section 6: "we close each loop with triangles, constructing a disk for each chain of edges", except two nested loops, which occur only at a 2:1 face of the octree |
| 4 | Function class; the surface inside the box | **holds as stated, with our reading for C1** | "smooth function", 0 a regular value, S bounded, the tree "initialized to a bounding box". The argument uses the mean value theorem and parametrizability, so C1 is what it needs; the 2007 conclusion speaks of "the set of C1-surfaces" as the class the method covers. A zero at a grid node: "considering F to be strictly positive whenever F ≥ 0", which is `DualContouring`'s rule here (inside is f < 0) |
| 5 | Is the weaker fact A (pairwise only) enough? | **not supported** | The proof uses more than the pairwise fact: "for each cell where 0 ∈ □F(C) the surface S is parametrizable in the direction of one of the axes". The excluded configurations 5, 8, 12 and 14, the block of two cells at an ambiguous face ("Both cubes are therefore parametrizable in the vertical direction") and the removal of a closed curve on a face all rest on it. That is A+, which any inclusion function gives when its interval product excludes 0; the pairwise tightening does not give it |

What follows for this note:

- **Step 3 of section 4.2 is the paper's statement for the published form A+** (with any enclosure of the gradient
  over the cell as the inclusion function, so the hull of sub-box enclosures qualifies). The certificate that the
  proof covers is the published form: 26 certified runs, not 28. The pairwise form is not covered by the paper and
  should not be built without its own proof.
- **How much of a proof the papers give.** The regular grid is argued in about two pages (the possible
  configurations; the two-cell block at an ambiguous face; pushing a closed curve on a face and a pair of crossings of
  an edge back by linear interpolation in a parametrizable direction). The journal version does **not** add detail to
  this part: what the extended abstract deferred to "the full paper" is the octree (how to connect vertices on a face
  with subdivided sides, and that further subdivision does not change the isotopy), which Theorem 7 does not use. So
  the premise is a peer-reviewed argument at the level of a geometric sketch, not a line-by-line proof, and that is
  the published state; the later literature (section 2) calls the 3D proof a case analysis by hand and reports no
  erratum. A review of Theorem 7 can check our composition; it cannot make the premise more rigorous than its source
  without redoing the regular-grid case (the block route of option 4 would do that with our own theorems).
- **Creases, in the authors' words** (2007, conclusion): the method of Boissonnat and Oudot for Lipschitz surfaces
  covers surfaces that "are not necessarily C1. Our method does not apply to this larger class of surfaces." They
  think it "feasible" to relax the condition to a bounded variation of normal cones and leave it out of scope. That
  confirms the no-go for creases as published, and marks creases below 90 degrees as conjectured, not proven.
- **Refined grids** (2007, section 6): the octree proof subdivides a balanced octree to a complete one in steps and
  shows that the isotopy class does not depend on the signs at the new nodes; a cell with two nested loops gets an
  annulus. This is what option 5 would have to match with `DualContouring`'s two-level grid; it is not used here.
- **Isotopy.** The paper says "regularly isotopic", glossed as "equivalent to the curve or surface under continuous
  deformation within the embedding space". Its proof deforms the surface (linear interpolation along a graph
  direction, cell by cell or in a block of cells) and does not construct an isotopy of space. Each of its moves is
  supported in a block and extends to one, but that extension is an added argument, not the paper's text. Theorem 7
  therefore claims an isotopy, and an ambient one only with that reading as part of the premise.
- **The thinnest point of the premise.** Configuration 13 is excluded by looking "at the diagonal pairs" with the
  remark that "the inner product condition does not depend on a particular coordinate system". That remark is true of
  the pairwise fact and not of the interval test, and the pairwise fact gives no parametrizability along a diagonal.
  "Cells have at most one ambiguous face", which the either-pairing step uses, rests on these exclusions. The
  independent review (section 4.6) could neither prove nor refute this step; a build should carry a cell-level check
  that a certified cell has one of the nine admitted configurations, and refuse it otherwise.
- The papers also note that the result need not be close in Hausdorff distance and that at a singularity the
  subdivision does not terminate. On balancing the extended abstract says it "is not necessary for an isotopic
  result"; the journal version weakens that to "We believe it is not necessary".

Lin's thesis notes that 9 of the 14 corner-sign types can occur under C1, two of them with an ambiguous face (the
paper's configurations 4 and 6); the prototype counts both (section 5.5).

### 4.4 Where rounding enters

Every decision of the certificate is the sign of one bound of an outward-rounded interval, and a rounded test may
only err towards "fail":

| Where | What is computed | Why it is sound |
|---|---|---|
| Arithmetic | +, -, *, /, square root: round to nearest, then one unit in the last place outward on both ends (the rule of `ExactInterval`) | IEEE 754 rounds these five correctly, so the exact result is within half a unit of the computed one. No directed rounding mode is used, and every operation is rounded and widened on its own, so there is no expression a compiler could fuse into a multiply-add (the .NET JIT does not contract them either). Flush-to-zero and denormals-are-zero modes must be off (the .NET default) |
| sin, cos | argument reduced by an integer multiple of an *interval* that contains pi / 2 (a double and its successor), then a Taylor polynomial with its Lagrange remainder, in the same arithmetic; over an interval, the hull of the two ends plus +-1 wherever a multiple test cannot exclude an extremum | no libm call, so no unspecified error and the same bits on every runtime; any integer in the reduction is sound, a bad one only widens |
| Enclosure of f and grad f over a box | the natural interval extension of the formula | inclusion holds by induction over the operations |
| `min` / `max` | one piece if its enclosure is strictly below (above) the other's over the box; else the hull of both and the box is marked not smooth | requirement (i) of the design note's Theorem 5: never decided from a point value. A cell is A only if no sub-box is marked: the pieces' strict regions are open and disjoint, the cell is connected, so one piece is active on all of it |
| A square root or division at 0 | the whole line, and the box is marked not smooth | requirement (ii) |
| Tightening | a cell is cut into 2^t sub-boxes per axis. Z: every sub-box excludes 0. A+: the hull of the sub-boxes' gradient enclosures, as one box, has a positive interval inner product with itself. A: the lower bound of the inner product is positive for **every pair** of sub-boxes (any two points lie in two of them) | the sub-boxes cover the cell exactly: their coordinates are `origin + (h / 2^t) * j`, and `(h / 2^t) * (i 2^t) = h * i` in doubles, so the breakpoints of level t contain the grid's own |
| Node signs (P0) | the enclosure of f at the node's doubles excludes 0 (by more than 1e-30, because the contouring keeps node values as `float`) | the prototype feeds the midpoint of that enclosure to the contouring, so its sign is the certified one. A build must either take the contouring's node signs from the certified evaluation or compare them (the offset probe did the latter) |
| Cell shape (P3) | the largest and smallest spacing of consecutive node coordinates, compared exactly | a comparison of doubles; fails for a cell size within a few million units in the last place of the coordinates, which no practical grid has |
| Grid | cells are defined by the computed coordinates, not by ideal ones | nothing to round: the complex *is* the doubles |

In one sentence: the node signs (P0) and the two cell tests are certified under outward rounding, with no dependency
on FMA or on a rounding mode. **Range:** soundness holds over the whole double range. A subnormal result is still
widened by one spacing (2^-1074), which encloses the exact value, as `ExactInterval` documents; an overflow gives a
bound that is still a true bound (the largest double or infinity; a test may pass on it, soundly), and a NaN gives
the whole line, which fails every test because the tests are written as "not (lower bound > 0)". What is *not* kept outside the normal range is
the invariance under scaling (section 5.5), and the node values must stay inside the `float` range the contouring
stores them in (the certificate's 1e-30 margin refuses a node below it).

What the prototype does **not** establish, and a build must: (P3) is not checked; the node-sign margin is right for
iso value 0 only (`DualContouring` compares the `float` node value with `IsoValue`, so for another iso value the
contouring's signs must be taken from, or compared with, the certified ones at every node); (P0) is checked at the
nodes of the cells that are not proven zero-free only (a node deep inside a zero-free region whose value is below the `float` range would be seen as
outside by the contouring; not reachable with the fields here); and (M) is read from the public flags of
`DualContouring` only (closed, `SelfIntersectionFree`, no non-manifold triangle), not from its certificate data for
(H1) and (H4). The `pv_certified` column is the level-set side alone; every certified run here also has a clean mesh.

Not covered by this table: the mesh side (M), which the library already checks exactly (`SelfIntersectionFree`, the
certificate data of `DualContouring`); the prototype reads only the public flags.

### 4.5 Where it fails, precisely

- **A singular point of f on or near L** (a cone point, two sheets touching, a pinch): A is false in every cell that
  holds the point, at every size. The subdivision does not terminate; with a depth limit the cells are listed. Near a
  singular member of a family the needed depth grows (by the published analysis with the logarithm of the distance to
  the singular point; the prototype's depth limit of four levels shows the growth, not the law; section 5.3).
- **Thin features.** A cell that holds two sheets (a wall, a gap, a tube thinner than about two cells) has
  gradients more than 90 degrees apart, so A is false, correctly: this is where the contour is wrong. Refinement
  fixes it at cells of a fraction of the feature size; a wall given as one function costs cells below the wall
  thickness (the earlier spike: 16 million cells for a quarter-cell gyroid wall).
- **Curvature against the cell.** A needs the normal to turn by less than 90 degrees over the cell: cells of about
  the radius of curvature or smaller. A tube of two cells diameter fails in half its cells (section 5.2).
- **Creases.** At a crease of the level set (two pieces of a `min` / `max` meeting on L) f is not C1; step 2 does not
  apply, and for an angle of 90 degrees or more between the normals no version of A can hold. Never certified, at any
  depth.
- **A piece boundary inside a cell that L also meets**, even far from L within the cell (two spheres half a cell
  apart; a shell): the cell is not A. This is a limit of the argument, not of the geometry; Theorem 5's test passes
  there (section 5.2).
- **A node on the level set, or too close to decide.** (P0) fails for the cells around it. A grid shift removes it.
- **The surface leaves the box** (a clipped lattice): (P2) fails. The published extensions to a region with boundary
  need extra tests on the boundary faces (Lin, Yap, Yu; Burr, Choi, Galehouse, Yap).
- **Loose enclosures.** A pass is a proof, a fail is not a refutation. A rotated frame widens every box (section 5.2).
- **No partial statement.** The isotopy is not cell by cell, so "certified cells" mean nothing by themselves when
  other cells fail; Theorem 1b's statement for a subset of cells is not available. A mixed certificate (A cells and
  Theorem 5 cells in one grid) is plausible (a Theorem 5 cell has no excursion through its faces) and unproven.
- **Refined grids.** For the two-level grid the published 2:1 rules would have to be matched with `DualContouring`'s
  transitions (face and edge vertices, refine factors above 2). Not done.

### 4.6 Independent review of the composition (2026-11-22)

After the owner's decision to build on the published premise, a fresh agent reviewed sections 4.1 to 4.5 against the
2004 paper and the design note's Theorems 1 and 2, without the author's reasoning. It did not have the 2007 text, so
what section 4.3 attributes to the journal version is the author's reading alone. Verdict: no step of the composition
fails; it holds, conditional on the paper, once four hypotheses are stated. All findings were checked by the author
and are worked in above:

| Finding | Disposition |
|---|---|
| The paper is for a "smooth function"; the note invoked it for C1 | step 2 now produces a smooth function (pieces smooth), so the premise is used as stated; C1-only pieces need the C1 reading |
| "Ambient" is not what the paper states | the claim is an isotopy; ambient only with the stated reading (section 4.3) |
| The cell shape is a hypothesis (cubes, or a bounded aspect ratio) | hypothesis (P3) with an exact check |
| The paper's mesh may touch the cell boundary; compatibility was asserted | step 5 pushes the disks into the cells |
| The exclusion of configuration 13 is argued with the pairwise fact | recorded as the thinnest point of the premise; a build checks the configuration of every certified cell |
| The node-sign margin holds for iso value 0 only; (H1) and (H4) are not read by the prototype | stated in section 4.4 as duties of a build |
| The FMA and overflow sentences were wrong as worded | reworded in section 4.4 |
| "Bounded" needs f without zeros outside the box; the `min` / `max` row of section 1 was loose | step 2 and section 1 |

What remains a premise after the review: the regular-grid argument of the paper itself (a two-page sketch, with the
exclusion of five configurations and the either-pairing claim), its reading as an ambient isotopy, its tolerance of
cells that are cubes only up to rounding, everything attributed to the 2007 text, and Theorems 1 and 2 as reviewed
earlier.

## 5. Measurements

`dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- all` (raw rows:
`benchmarks/ContouringIsotopySubdivision/results/all.csv`; the tables are `analyze.py` on that file). 176 runs: 58 cases
at 16, 32 and 64 cells across the box (two capped gyroids also at 128). One run each, 4 cores.

### 5.1 What is run

- **Fields** with enclosures of value and gradient (`Fields.cs`), inside where f < 0, in a box of side 2.2:
  the feature-width families of the design note's section 2.1 (torus, gap, shell, tunnel, rod; width k = 0.5, 1, 2
  cells, the torus also 4), a sphere, three TPMS (gyroid, Schwarz P, diamond; two periods across the box, so 8, 16
  and 32 cells per period) closed by a smooth cap `g(x) - level + 4 (|x|^2 / 0.81)^3`, the gyroid also at levels 0.6
  to 1.35 (it pinches off near 1.41), Cassini surfaces `|x - a|^2 |x + a|^2 = b^4` towards the singular one (b / a =
  1), and a polynomial sphere through six grid nodes. Seed -1 is the shape with a grid shift, seed 0 a random rotation.
- **PV**: the test of section 4 on every grid cell, with the tightening to t = 4 (4096 sub-boxes), in two forms: the
  **published** form A+ (`-- hull`, `results/hull.csv`) and the **pairwise** form A (the columns of `all.csv`; all tables
  below unless they say otherwise).
- **S**: the cell test (S0) to (S3) of Theorem 5 with the same enclosures (t up to 3), in the segment form of the
  offset probe (direction tests on the sub-pieces that can hold a zero). A cell with several circles is refused (the
  separating-surface test is not built); one cell of the 176 runs failed for that reason.
- **Octree**: the subdivision proper (split until C0 or C1 as one box, no tightening), to four levels below the grid.
- **Contour**: `DualContouring` (`Simplify` off) on the same grid and node values; right = clean (closed,
  `SelfIntersectionFree`, no non-manifold triangle) with the truth's components and genera. Truth: by construction; for
  a capped TPMS the contour of the finest grid whose PV certificate passes (so the certificate supplies the
  reference, which is the gate the design note set for stage 3; for those runs "right" is then true by construction,
  and the coarser grids of the same field are the ones tested against it).
- **Class P**: the built certificate (`MeshWindingNumberSolidify`, blocks and growth) on a marching-cubes mesh of the
  same field at the same cell size, for 16 and 32 cells. It certifies *its own* contour (adaptive, with the hidden-gap
  refinement), not the one above, so only the verdicts and the soundness can be compared.

### 5.2 Certified runs and cells

Cells are the grid cells not proven zero-free, summed over the runs of the row. "Only PV" / "only S": certified by
one test and not the other.

| Group | N | Runs | Contour right | PV: runs certified | S: runs certified | PV cells | S cells | Only PV | Only S | Neither |
|---|---|---|---|---|---|---|---|---|---|---|
| Sphere; torus with a tube of 2 and 4 cells | 16 / 32 / 64 | 6 each | 6 / 6 / 6 | 3 / 3 / 3 | 1 / 1 / 1 | 91.1 / 93.9 / 96.1 % | 92.5 / 95.4 / 97.6 % | 5.1 / 3.0 / 1.6 % | 6.5 / 4.5 / 3.1 % | 2.4 / 1.6 / 0.8 % |
| Capped TPMS, level 0 (8 / 16 / 32 cells per period) | 16 / 32 / 64 | 3 each | 3 / 3 / 3 | 0 / 0 / **3** | 0 / 0 / 0 | 68.9 / 98.3 / 100.0 % | 78.8 / 93.1 / 97.5 % | 10.2 / 6.3 / 2.5 % | 20.0 / 1.1 / 0.0 % | 11.0 / 0.6 / 0.0 % |
| Capped gyroid, levels 0.6 to 1.35 | 16 / 32 / 64 | 4 each | 3 / 3 / 3 (1 unknown) | 0 / 0 / 2 | 0 / 0 / 0 | 83.5 / 98.1 / 99.8 % | 86.1 / 94.8 / 97.7 % | 7.9 / 4.4 / 2.3 % | 10.4 / 1.2 / 0.1 % | 6.1 / 0.7 / 0.1 % |
| Cassini, b / a from 1.2 to 1.01 and 0.99 to 0.95 | 16 / 32 / 64 | 10 each | 10 / 10 / 10 | 1 / 4 / 6 | 0 / 0 / 1 | 87.6 / 99.4 / 99.9 % | 84.9 / 94.2 / 97.7 % | 9.6 / 5.5 / 2.3 % | 6.9 / 0.2 / 0.1 % | 5.4 / 0.3 / 0.0 % |
| Cassini, b / a = 1.001, 1, 0.999 | 16 / 32 / 64 | 6 each | 3 / 3 / 4 (2 singular) | 0 / 0 / 0 | 0 / 0 / 0 | 80.3 / 98.4 / 99.7 % | 88.5 / 93.3 / 97.3 % | 3.4 / 5.3 / 2.4 % | 11.7 / 0.2 / 0.0 % | 8.0 / 1.4 / 0.3 % |
| Torus, tube 0.5 and 1 cells | 16 / 32 / 64 | 4 each | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 | 13.3 / 18.5 / 15.5 % | 41.4 / 36.4 / 38.0 % | 0.2 / 1.6 / 0.7 % | 28.4 / 19.5 / 23.2 % | 58.4 / 62.0 / 61.3 % |
| Gap (two spheres, a `min`) | 16 / 32 / 64 | 6 each | 6 / 6 / 4 | 0 / 1 / 1 | 1 / 3 / 0 | 96.7 / 98.7 / 99.5 % | 96.3 / 98.3 / 99.1 % | 3.5 / 1.5 / 0.9 % | 3.1 / 1.1 / 0.5 % | 0.2 / 0.2 / 0.1 % |
| Shell (two levels, a `max`) | 16 / 32 / 64 | 6 each | 2 / 2 / 2 | 0 / 0 / 0 | 1 / 1 / 0 | 50.1 / 50.8 / 50.9 % | 75.8 / 78.0 / 78.8 % | 1.4 / 0.7 / 0.4 % | 27.1 / 28.0 / 28.3 % | 22.8 / 21.2 / 20.8 % |
| Tunnel (a crease of 90 degrees) | 16 / 32 / 64 | 6 each | 4 / 4 / 3 | 0 / 0 / 0 | 0 / 0 / 0 | 90.9 / 96.7 / 98.6 % | 94.9 / 97.4 / 98.9 % | 2.0 / 1.2 / 0.4 % | 6.1 / 2.0 / 0.7 % | 3.0 / 1.3 / 0.7 % |
| Rod (a concave crease) | 16 / 32 / 64 | 6 each | 4 / 4 / 4 | 0 / 0 / 0 | 0 / 0 / 1 | 83.1 / 94.2 / 97.7 % | 83.7 / 94.4 / 97.8 % | 9.3 / 3.5 / 1.3 % | 9.9 / 3.7 / 1.3 % | 7.0 / 2.1 / 1.0 % |
| Sphere through six nodes | 16 / 32 / 64 | 1 each | 1 / 1 / 1 | 0 / 0 / 0 | 0 / 0 / 0 | 83.8 / 95.9 / 99.0 % | 83.8 / 95.9 / 99.0 % | 0 | 0 | 16.2 / 4.1 / 1.0 % |

Reading:

- **Whole runs.** The PV test certifies 28 of the 176 runs in the pairwise form and **26 in the published form**
  (the same runs without the diamond at 64, which misses by 3 of 13 211 cells, and the unrotated torus with a tube of
  four cells at 64); 13 runs pass with every cell as one box (t = 0). Theorem 5's cell test certifies 11; 4 runs pass
  both. Over all cells the two forms differ little: 74.2 against 77.6 % at 16, 85.1 against 85.6 % at 32, 86.9 against
  86.9 % at 64. A run needs every cell, so a share of 99 % is still "not certified".
- **Smooth fields at an adequate resolution are the PV test's ground.** The three capped TPMS at 32 cells per period
  are certified in full in the pairwise form (100.0 % of 41 875 cells) and 2 of 3 in the published form; the cell test
  of Theorem 5 certifies none of them: it leaves 2.5 % of the cells (1 046 of 41 875), all of them refused by the edge
  test (S1: 1 012) or the face test (S2: 34). That is where a tangency with the grid shows (the design note, section
  4.8, for meshes); the test stops at t = 3, and a fail is not a refutation, so the share that is a true tangency is
  not separated here. The share Theorem 5 leaves falls with the resolution (11 to 29 % per field at 8 cells per
  period, 4 to 10 % at 16, 2 to 3 % at 32) while their number grows (413, 640 and 1 046 cells).
  The (S2) of the prototype tries the two axes with a positive sign only and relies on the in-plane gradient for the
  rest, which can cost it a few cells. At 16 cells per period the PV
  test reaches 98.3 % and no full run; at 8 cells per period 69 %.
- **Why the PV test fails where it fails** (table B of `analyze.py`): on the thin torus 77 % of the cells are
  *refuted* (two points with a certified inner product of the gradients <= 0: C1 is false, not unproven); on the
  torus with a tube of two cells the unrotated runs stay at 62 to 64 % and the rotated ones at 49 to 51 % (the normal
  turns through more than 90 degrees inside a cell);
  with a tube of four cells the unrotated run is certified at every N and the rotated one stops at 96 to 97 % with
  one refuted cell in the three runs, which is the looseness of the enclosure in a rotated frame (the tightening passes 7 % of the smooth
  group's cells at t = 1, 5 % at t = 2, 2 % at t = 3; without it they would count as failures).
- **Pieces of a `min` / `max`.** The shell is the clearest case: half of its cells hold one sheet and the piece
  boundary (the mid-surface between the two spheres), so they are not C1 for the PV argument, while Theorem 5 passes
  78 %. For the gap, the two tests are equal within a percent, and each certifies runs
  the other does not.
- **Creases.** Tunnel and rod: 0 of 36 runs by the PV test, 1 of 36 by Theorem 5 (rod, 2 cells, unrotated, 64). Every leaf
  the octree leaves open four levels below the grid holds a possible non-smooth point (3 313 of 3 313 at 16, and so
  on): the crease is the only obstruction, and it does not go away. Theorem 5's test fails there too in most runs, by
  the edge and face tests next to the crease; its share is up to 4 points higher at 16 and equal within half a point at 64.
- **A node on the surface.** Both tests refuse exactly the cells around the six nodes; the share of refused cells falls
  with N, the verdict does not change. A grid shift is the remedy.

### 5.3 Depth, thin features and near-singular cases

The octree (the subdivision as published: split until a box passes as one enclosure; depth 0 = a grid cell):

| Group | Deepest C1 leaf below the grid (N = 16 / 32 / 64) | Runs with leaves still open at depth 4 | Open leaves (N = 16 / 32 / 64) |
|---|---|---|---|
| Sphere; torus with a tube of 2 and 4 cells | 3 / 3 / 3 | 0 | 0 |
| Capped TPMS, level 0 | 3 / 2 / 1 | 0 | 0 |
| Capped gyroid, levels 0.6 to 1.2 | 4 / 3 / 2 | 0 | 0 |
| Capped gyroid, level 1.35 (near the pinch-off) | 4 / 4 / 4 | 1 of 1 at each N | 342 / 353 / 348 |
| Cassini, b / a = 1.2, 1.05, 0.95 | 3 / 2 / 1 | 0 | 0 |
| Cassini, b / a = 1.01 and 0.99 | 4 / 4 / 3 | 4 of 4 at 16, 0 at 32 and 64 | 532 / 0 / 0 |
| Cassini, b / a = 1.001, 1, 0.999 | 4 / 4 / 4 | 6 of 6 at each N | 873 / 792 / 747 |
| Torus, tube 0.5 and 1 cells | 4 / 4 / 4 | 1 of 4 at each N (tube 0.5 cells, rotated) | 303 / 480 / 809 |
| Shell | 4 / 4 / 4 | 0 | 0 |
| Gap | 3 / 3 / 3 | 0 | 0 |
| Tunnel, rod (creases) | 4 / 4 / 4 | 6 of 6 at each N, both | tunnel 3 313 / 2 909 / 2 774, rod 4 171 / 3 420 / 3 038, every one with a non-smooth point |

- **Smooth and regular: the depth is finite and small.** A capped TPMS needs cells of 1/8 of a 16-cell grid's (depth
  3 at 16, 1 at 64): about 64 cells per period for the octree's plain test, 32 with the tightening of section 5.2.
- **Thin features terminate, at the price of resolving them.** The gap closes at depth 3 and the shell at depth 4 (cells of 1/8
  and 1/16 of a grid cell, for walls and gaps of half a cell to two cells); the thin torus at depth 4, except the
  rotated tube of half a cell, which stays open. This
  is the earlier spike's finding (cells of about an eighth of the thickness) with sound intervals.
- **Near-singular: the depth grows as the singular member is approached, and the certificate says so.** Cassini with
  b / a = 1.05 is certified by the PV test in 1 of 2 runs at 32 and 2 of 2 at 64; at 1.01 in none (6 to 36 cells
  left per run; the octree stays open at 16 and closes at 32 and 64); at 1.001, 1 and 0.999 7 to 39 cells per run
  around the origin stay open at every N and the octree does not close four levels down. Of the 12 runs there with a known truth the contour is right in 10 and
  wrong in 2 (the rotated surface with the 0.001 neck at 16 and 32 cells: two components for one); none is certified. The singular surface
  itself (b = a) is refused like its neighbours, as it must be.
- **The pinch-off of the gyroid** behaves the same way: at level 1.35 the contour has genus 2 at 16 cells and genus 5
  from 32 on, the PV test leaves 0.04 % of the cells at 128 (30 of 74 022), and the
  truth stays "unknown" because no grid was certified.

### 5.4 Time

Wall time of one run, the certificate serial, `DualContouring.Generate` on 4 cores. The stratum is this prototype on
these procedural fields: the field evaluation is an interval evaluation in both, about the same cost per call.

| N | Runs | Contour, median ms | PV test / contour: median (min to max) | S test in addition / contour: median (min to max) | Octree / contour, median |
|---|---|---|---|---|---|
| 32 | 58 | 16 | 2.8 (0.12 to 42) | 2.9 (0 to 12) | 0.25 |
| 64 | 58 | 69 | 0.81 (0.12 to 22) | 1.8 (0.4 to 12) | 0.06 |
| 128 | 2 | 5 146 | 0.32 (0.27 to 0.36) | 2.5 (2.1 to 2.9) | - |

- Where the cells pass as one box, the PV test is a fraction of the contouring: the capped TPMS at 64 take 0.27 to
  0.34 times the contouring in the pairwise form (212 to 315 ms against 797 to 1 022 ms) and 0.32 to 0.49 times in the
  published form (291 to 441 ms, a separate run) for the level-set side of the certificate; Theorem 5's test takes
  2.9 to 4.2 times the contouring on the same runs and certifies none. One enclosure of a cell replaces twelve edges
  and six faces.
- The maxima are the tightening: a cell that is cut down to t = 4 has cost 4 681 enclosures and 131 000 pair
  products. The rotated torus with a tube of four cells passes 23 % of its cells as one box and needs t = 1 to 3 for
  the rest (1.5 s against 77 ms of contouring at 64). A build would bound t
  (the gain of t = 3 and 4 is 2 % of the cells) or use a centred form instead of deeper cuts.
- At 16 and 32 cells the contouring takes a few milliseconds and the ratios say little (median 12 at 16).

### 5.5 Agreement with the other certificates, soundness, invariance

Verdicts of the 98 runs with a class P result (16 and 32 cells; 4 more have no usable mesh input, the singular Cassini):

| PV | S | Class P (its own contour) | Runs | `DualContouring` right | Class P contour right |
|---|---|---|---|---|---|
| yes | yes | yes | 3 | 3 | 3 |
| yes | no | yes | 9 | 9 | 9 |
| no | yes | yes | 4 | 4 | 4 |
| no | yes | no | 1 | 1 | 0 |
| no | no | yes | 56 | 44 | 56 |
| no | no | no | 25 | 11 | 6 |

- **No certificate passed on a wrong contour**: PV 0 of 28 (published form: 0 of 26), S 0 of 11, class P 0 of 72. For
  6 of the 28 (5 of the 26) the truth is the certified run's own contour (the capped TPMS), so the count with an
  independent truth is 0 of 22 (0 of 21) (41 of the 176
  `DualContouring` results are wrong, all on features of half a cell or one cell, or next to the singular Cassini surface).
- Every run the PV test or Theorem 5's test certifies is also certified by class P on the meshed field (16 of 17), with
  one exception that is not a disagreement: the gap of half a cell at 32, where `DualContouring` is right and
  certified by S, and the class P contour (a different, adaptive contour) joins the two spheres and is correctly
  refused.
- Class P certifies 56 runs that neither function test does. Two reasons: its contour is refined at hidden gaps, so
  it is right where the uniform contour is wrong (the shells, 12 of 12); and it certifies by blocks, which absorb
  the tangencies and need no smoothness (the creases of tunnel and rod). The comparison is of verdicts on
  different contours, so it shows the direction, not a ranking.
- Ambiguous faces do occur in PV cells (150 cell sides over all runs, about half as many faces), and 94 PV cells have more than
  one patch: premise 2 of section 4.3 is exercised, not vacuous. All of those runs with a full PV certificate have the
  right topology.
- **Invariance under 2^k** (`-- invariance`): the whole case scaled by 2^10, 2^-10, 2^40 and 2^-40, ten cases at 32
  cells. 39 of the 40 scaled runs give the same row bit for bit (cell counts, the depth t of every pass, the
  contour's topology, the number of field evaluations). The exception is the quartic Cassini field at 2^-40: its
  values are about 2^-160, below the `float` range in which `DualContouring` keeps node values, so the contour is
  empty; the certificate refuses every node there (its 1e-30 margin) and certifies nothing. So the range is: every
  interval operation and the one-unit widening scale exactly while no intermediate value is subnormal or overflows
  (in the subnormal range the widening no longer scales, and the enclosure stays sound but is no longer the scaled
  one); an overflow gives a bound that is still true (so a pass on it is sound); and node values must stay within
  the `float` range the contouring uses, with the margin above.
- **Checks of the prototype** (`-- selftest`): 0 failures of the sin / cos enclosures at 200 000 points and
  intervals; 0 failures of value and gradient enclosures of all fields in 88 000 random boxes; 0 of 1 467 PV cells
  with sampled gradients 90 degrees or more apart. A hand mutant (`G3_CIS_MUTANT=1`: each sub-box tested against
  itself only, the classic mistake of squaring the gradient interval) fails that last check in 222 of 2 033 cells. The
  run-level truth check alone did **not** catch the mutant in ten tries on wrong contours (the shares rose, for
  example from 36 to 73 % on the torus of one cell, but another cell always failed): "never certified on a wrong
  result" has little power by itself, and a build needs cell-level oracles like this one as its mutation targets.

Not measured: a refined (two-level) grid; `TpmsLattice`'s own fields and its sheet style; mesh offsets (the offset
probe's code is on its own branch); sampled grids (section 3 gives the arithmetic); a parallel certificate.

## 6. What this means for options B and C

**B.** The design note's status line "B and C: the proof first" is met for smooth fields in two independent ways:
Theorem 5 (written and reviewed 2026-11-13, cell by cell, Clarke gradients, creases below 180 degrees) and Theorem 7
of this note (non-local, C1 only, resting on Plantinga and Vegter's regular-grid argument, checked in section 4.3). They are complementary, and the measurements say which
one carries: 

- on smooth fields at an adequate resolution Theorem 7 certifies whole runs (the capped TPMS at 32 cells per period:
  2 of 3 in the published form, 3 of 3 in the pairwise form) where Theorem 5's cell test certifies none: it leaves 2
  to 3 % of the cells there, by its edge and face tests; and the PV test is cheaper (0.3 to 0.5 against 3 to 4 times
  the contouring);
- where a cell holds a crease or a piece boundary, only Theorem 5 applies (the shell: 78 % of the cells against
  51 %), and neither certifies a run with a crease of 90 degrees (0 and 1 of 36).

So a class S certificate that is to say "certified" for a TPMS has to contain the C1 test; one built on Theorem 5
alone would report cells on every smooth input, as the offset probe did (0 to 94 % of the cells, shelved).

What neither gives: a thin wall as one field (both need cells below the thickness), a crease of 90 degrees or more
with a tangency at the grid (Theorem 5's (S2') can fail at every size, [MESH_OFFSET_ENCLOSURE.md](../design/MESH_OFFSET_ENCLOSURE.md)
section 5), and a sheet lattice as one field. For a sheet given as two levels of one smooth g, each level is
certified separately by either theorem; that the two meshes are isotopic to the two levels *simultaneously* (one
isotopy for both) does not follow from two separate isotopies (Burr and Byrd's point in the plane), and neither
theorem gives it as stated: Theorem 1 would have to be applied to the union of the two levels, which is compatible
with the grid only where the grid resolves the wall (an edge may meet the union once).

**C.** Nothing changes. Section 7.9 of the design note lists what is missing: (1) a rigorous enclosure of the winding
number at a point, (2) enclosures of its gradient over a box, (3) any statement at a defect edge. A subdivision
method consumes (1) and (2), it does not produce them; at (3) w is not locally Lipschitz and the gradient turns
through all directions, so C1 fails at every depth exactly as (S3) does. On the input's own triangles the level set
is piecewise linear with creases, which is class P's ground. In free-space cells crossed by a harmonic sheet, A could
replace Theorem 5's tests once (1) and (2) exist, with the same gain as for B; that is a detail of a later build, not
a proof for C.

**The excursion question of plan #1289** (can the edge condition of the class P certificate be relaxed to the
crossings the contour has?). For smooth fields Theorem 7 is that relaxation: it never looks at edges. For meshes the
built answer stands (Theorem 6, blocks). Burr and Byrd's excursions are the planar picture of both; their paper adds
nothing for surfaces.

## 7. Options

How others solve it: interval subdivision with the two box tests and a non-local isotopy (Plantinga and Vegter 2004 /
2007; Burr and Byrd 2023 for several curves in the plane); the weaker parameterizability test with fixed rules on
ambiguous faces (Snyder 1992; Lin, Yap, Yu 2013); restricted Delaunay refinement with a feature-size bound (Boissonnat
and Oudot 2005; CGAL's surface mesher, as papers); complex-cell tests on an adaptive grid for polyhedral and CSG inputs
(Varadhan et al. 2004); exact case analysis for the trilinear interpolant of samples (Chernyaev 1995; Nielson 2003);
rounding by outward-rounded intervals or by a precision that grows with the depth (Xu and Yap 2019; Cucker, Ergür,
Tonelli-Cueto 2022). Snyder, Boissonnat and Oudot, Varadhan, Chernyaev and Nielson are from the design note's table,
not re-read here. Nothing found for a certificate of a dual contouring mesh by these tests, for non-smooth implicits
beyond Theorem 5's setting, or for winding-number fields (terms searched: isotopic, certified, subdivision, dual
contouring, interval, Plantinga Vegter, simultaneous, surfaces, finite precision).

| # | Option | Gain (measured here) | Effort | Risk |
|---|---|---|---|---|
| 1 | **Review of Theorem 7** (section 4): a fresh session with this note and the two papers checks section 4.3's reading and the composition with Theorems 1 and 2 | turns "B: the proof first" into a reviewed statement for smooth fields; decides options 2 and 3 | S, docs only; the extended abstract is reachable at cims.nyu.edu and has the regular-grid section in the same words as the journal version | low to medium: the published 3D argument is a sketch. If the review finds a gap in it, the fall-back is Theorem 5 on box-shaped blocks (option 4) |
| 2 | **Class S for smooth fields as the C0 / C1 cell test in its published form**, opt-in, report only, beside `ContouringTopologyCertificate`: an interval interface for implicits (value and gradient over a box), interval sin / cos without libm, the built-in smooth shapes and the TPMS functions; `TpmsLattice` skeletal levels first | whole-run certificates where Theorem 5's test gives none (TPMS at 32 cells per period: 2 of 3 against 0 of 3); the test at 0.3 to 0.5 times the contouring; the certified reference for the gyroid that stage 3's gate asks for | M: a new non-exact file with outward-rounded bounds (the `ExactInterval` rule), tests (invariance under 2^k, reuse, determinism, cell-level oracles as mutation targets, never certified on a wrong result) | medium: all or nothing (one failing cell voids the statement; 16 cells per period reach 98 % and no certificate); loose enclosures in rotated frames need a centred form; the public interval interface is new API |
| 3 | **Both tests per cell** (C1, else Theorem 5), for fields with pieces and creases | the cells only one test certifies: 2 to 10 % each way on smooth fields, 27 % (Theorem 5 only) on the shell | M on top of option 2, **after a proof**: a mixed grid of non-local and cell-by-cell cells is not covered by either theorem | medium to high: the mixed statement is plausible (a Theorem 5 cell admits no excursion through its faces) and unproven; without it the two tests can only be reported side by side |
| 4 | **Theorem 5 on box-shaped blocks** (the class S analogue of Theorem 6): merge the cells that fail (S1) / (S2) into boxes with a graph direction | would cover tangencies *and* creases below 180 degrees, cell by cell, with a partial statement (Theorem 1b) | S for the proof (a box is a convex cell of the design note's grid, so Theorem 5 and Lemma 6.4 should compose), M to L to build | medium: not measured here; the separating-surface test for several circles is unbuilt; the edge and face tests cost 3 to 4 times the contouring in the prototype |
| 5 | **Refinement by the certificate** (the octree, or the two-level grid) where cells fail | thin features and near-singular spots certify at depth 3 to 4 | L: the 2:1 rules against `DualContouring`'s transitions, a proof for the refined complex | high: no bound at creases and singular points (it must stop and report); the stage 2 spike's cost (3 to 15 times) applies |
| 6 | **Option C by this route** | none: the method needs the enclosures C lacks and fails at defect edges at every depth | - | no-go |
| 7 | **Creases of 90 degrees or more by this route** | none: C1 is false at every depth (0 of 36 runs; every open leaf holds the crease) | - | no-go; Theorem 5 or blocks |
| 8 | Nothing | - | 0 | options B and C stay "stopped by rule" with no reviewed route for smooth fields |

## 8. Leaning

Option 1 now, then option 2 if the review holds; option 4's proof as the next docs step; options 3 and 5 not now;
6 and 7 are the no-gos the owner asked to have on record.

- The question of the brief has a split answer. For **smooth fields** the subdivision method does give the missing
  proof, and it is the stronger tool in practice: it is the only one of the two function certificates that certified
  a TPMS in full, at a third to a half of the contouring's time. It rests on Plantinga and Vegter's regular-grid argument, which both
  papers confirm on the four points Theorem 7 needs and give in sketch form; the pairwise
  tightening of the test is not covered by it and is dropped from the proposal. The review is still the gate, and
  it is small.
- For **creases and for option C** it gives nothing, for reasons that no refinement removes (section 4.5, section 6).
  That closes the "excursion-aware" idea of the untapped-science list (item 5a) for those inputs: Burr and Byrd's
  excursions are planar, and their surface counterpart is exactly Plantinga and Vegter's argument, which needs C1.
- The built class P certificate remains the most widely applicable one (72 of 98 runs on meshed fields), because
  blocks need no smoothness. That argues for option 4 (the same idea for functions) over more work on the non-local
  route once smooth fields are served.
- Open questions for the owner: (a) whether a premise that is published only as a geometric sketch is acceptable for a build after the review of
  option 1, or whether the regular-grid case should first be redone with our own theorems (option 4); (b) whether the interval
  interface of option 2 may be public from the start or stays internal with `TpmsLattice` as its only user; (c)
  whether "certified" for a lattice may be offered only above a stated resolution (32 cells per period here), with the
  uncertified cells reported below it.
