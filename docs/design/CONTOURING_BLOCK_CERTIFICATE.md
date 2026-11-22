# Class S on box-shaped blocks, and the cell shape of the smooth certificate (proof step)

Follow-up of [CONTOURING_TOPOLOGY.md](CONTOURING_TOPOLOGY.md) (its section 7.14 points here). Section, theorem and
lemma numbers below without a file are that note's: Theorem 1 (7.2), Theorem 2 (7.3), Theorem 4 (7.5), Theorem 1b
(7.7), Theorem 5 with Lemmas 5.1 and 5.2 (7.8), Theorem 6 with Lemmas 6.1 to 6.4 (7.10), Theorem 7 (7.13).

Owner decision 2026-11-22: the route to a certificate for a clipped lattice is Theorem 5 on box-shaped blocks, as a
proof step with its own review before any code. This note is that step. It is the class S analogue of Theorem 6:
there the level set is a mesh and a block is any ball of cells, tested by counting; here the level set is that of a
function and a block is a box, tested by one direction. It also settles the cell shape (hypothesis (P3) of Theorem 7),
which the review of #1392 left as "a reading of the premise, not a proved margin". Status: **written and independently
reviewed on 2026-11-22 (section 5); nothing is built.**

Why blocks for class S: Theorem 5 refuses a cell whose edge may be crossed twice or whose face may carry a closed curve
of L, that is every tangency of L with the grid, at every resolution ((S1), (S2)). On the capped TPMS of the spike these
are the cells Theorem 5 leaves (2.5 % at 32 cells per period, all refused by the edge or the face test). Theorem 7 has no
edge or face test, but it never certifies a crease, and a clipped lattice has one at the cut. Blocks keep Theorem 5's
cell-by-cell statement and its creases and move the tangencies inside a cell of a coarser complex.


Short answer:

- **Theorem 8 holds** (section 1): Theorem 5 carries over to a complex in which some cells are boxes made of several
  grid cells. A block is certified by (B0) to (B2) on its boundary (Theorem 5's node, edge and face tests), one partial
  derivative of constant sign on the block and at most one circle of L on its boundary. The proof adds no error bound.
- **Theorems 1, 2, 4, 5, 6 and 8 need boxes, not cubes** (remark 5); a certificate by Theorem 5 or 8 has no cell-shape
  condition. Theorem 7 still needs cubes, through its premise, until Lemma 7.1 is adopted.
- **Lemma 7.1** (section 2): the smooth certificate of #1392 (Theorem 7) keeps its premise for exact cubes and gets a
  proved margin instead of the tolerance (P3): the negative products of the gradient test are multiplied by the square
  of the largest spacing ratio. That is a small code change with a new bound; today's code is short of the lemma by a
  margin of about twice its `CellShapeTolerance` on that test (2^-19 at the default, 2^-9 at the largest setting).
- **Measured** (section 3a): box blocks cover 96.5 % of the refused cells of smooth TPMS at 32 cells per period (five
  of six runs complete) and about a third overall (9 to 56 % per run) for a TPMS clipped by a ball; box blocks grown
  this way do not close a clipped lattice.
- **Open** (sections 3 and 4): the mesh side for marching cubes and for marching tetrahedra on cubes (lemmas of their
  own; a finite check for the tetrahedra is done here), the two levels of a wall together, and the rim of a clipped
  lattice.

## 1. Theorem 8

**Setting.** K is a grid of axis-aligned boxes between consecutive node coordinates, which increase strictly on each
axis (the uniform grid's doubles `origin + h * i` are one; nothing below needs equal spacings). f, G(X), L and the
requirement on the enclosures are those of section 7.8. A *box block* is a box R that is a union of 3-cells of K (a
range of cells per axis). R_1 .. R_k are box blocks with pairwise disjoint interiors, and K' is the complex of section
7.10: the blocks, the 3-cells of K in no block, and the faces, edges and nodes of K inside no block. A box is a PL ball,
so Lemma 6.1 holds: K' is a complex for Theorem 1. The boundary of a block R is the union of the faces of K *on* R; an
edge of K on R lies either in the interior of a side of the box R, where its two faces on R are coplanar, or on an edge
of the box R; a node on R is a node of K.

A block R is *certified* if

- (B0) f has no zero at the nodes on R (the enclosure of f at each excludes 0);
- (B1) every edge of K on R satisfies (S1);
- (B2) every face of K on R satisfies (S2), the separating segment on a face with four crossing points included (f has
  one sign on the segment, the sign at its ends, and the crossing points on either side of it are paired);
- (B3) there is a direction d with d . g > 0 for all g in G(R), and the boundary of R carries at most one circle of L.

Nothing is asked of the nodes, edges and faces inside R. A 3-cell of K in no block is certified as in section 7.8
((S0) to (S3)); a cell or block that is not certified must have no zero of f (its enclosure excludes 0), or be listed.

**The circles on a block are counted from the pattern.** Under (B0) to (B2) alone, L meets every edge on R in at most
one point and every face on R in arcs that end on its edges: on an edge by (S1) (strictly monotone, or no zero); on a
face with direction a, L is a graph over the line orthogonal to a (Lemma 5.1 in the plane), and that graph ends only
where f = 0 on an edge of the face, where (S1) gives a strict sign change along the edge and (S0) excludes the nodes,
so there is no isolated point and no arc that ends inside the face (Lemma 8.1 (i) below). Every edge on R lies in
exactly two faces on R, and in each of them exactly one arc ends at its crossing point. So L on the boundary of R is a
set of disjoint closed curves, each through at least three crossed edges (a face carries no closed curve, and two
faces share one edge at most), and their number m is the number of cycles of the graph whose vertices are the crossed edges
on R and whose links are the arcs: read from the node signs on R and, on a face with four crossing points, from the
side of the separating segment. No geometry, and the same number for every surface with that pattern.

**Lemma 8.1 (the boundary of a block).** Lemma 5.2 holds for a block R in the place of the cell c, in two parts.
(i) Under (B0) to (B2): at every point p of L on the boundary of R there are directions u, u' pointing strictly into R
and a ball B around p with u . g < 0 and u' . g > 0 for all generalized gradients at the points of B, and L on the
boundary of R is near p one arc through p, across which f changes sign there. (ii) Consequently (nothing of (B3) is
needed for it) R with {f <= 0} and R with {f >= 0} are near p the regions on one side of a Lipschitz graph, and L in R
is near p a closed half disk whose edge is L on the boundary of R.

*Proof.* By (B0) p is not a node, so it is in the relative interior of a face of K on R or of an edge of K on R.
In a face: the proof of Lemma 5.2, with the outward normal n of the side of R that contains the face and the direction
a of (B2). On an edge with direction a that lies on an edge of the box R: the proof of Lemma 5.2 for an edge, with the
normals n_1, n_2 of the two sides. The new case is an edge of K, with direction a, in the interior of a side of R with
outward normal n. (B1) gives a sign s of a . g on G(edge), which carries over to a ball B around p by upper
semicontinuity, as in Lemma 5.2. Since a . n = 0, u = -s a - eps n and u' = s a - eps n point strictly into R (R is the
half space n . x <= const near p) and have u . g < 0, u' . g > 0 on B for eps small. In the plane of that side, f is
strictly monotone along a near p (Lemma 5.1 in the plane), so L is there a graph over the line orthogonal to a: one arc
through p, which crosses the edge, and f changes sign across it. That is (i). For (ii), the rest of the proof of
Lemma 5.2 (Lemma 5.1 on the ball B, the graph of max(phi, psi), the arc that cuts it, (J)) uses only (i) and that R is
convex. ∎

**Theorem 8.** Let every cell of K' that L meets be certified (a 3-cell of K by (S0) to (S3), a block by (B0) to (B3)),
and let L not meet the outer layer (f has no zero on the cells of K in it, whether they are in a block or not). Then:

(a) L is a closed surface compatible with K': (E) and (F) hold on the edges and faces of K', L meets every certified
3-cell of K in disks, a block with one circle in one disk and a block with no circle not at all.

(b) L is ambient isotopic to every PL surface that is compatible with K' with the same pattern, by a homeomorphism that
maps every block and every other cell of K' onto itself (Theorem 1 on K'). Theorem 1b holds with K' as well.

(c) Let D be the contour of `DualContouring` on the node signs, with (H1) to (H4) (Theorem 2), let the contour's sign at
every node of K' be the true sign of f (certified by (S0) or (B0), or by the zero-free enclosure of a cell that
contains the node), let its face rule on every face of K' with four crossing points agree with
the side of the separating segment, and let in every block, certified or zero-free, every component of D in R meet the boundary of R
and P - A + Z be their number (condition 4 of Theorem 6, Lemmas 6.2 and 6.4). Then D is ambient isotopic to L.

*Proof.* (a) (E) and (F) on the edges and faces of K': as in Theorem 5; each lies in a certified cell of K' or in a
cell that L does not meet. In a 3-cell of K: Theorem 5. In a block R: by (B3) and Lemma 5.1 on the convex set R, L in R
is the graph of a Lipschitz function along d, a surface near each of its points in the interior of R, and by Lemma 8.1
a half disk near each of its points on the boundary. So L in R is a compact 2-manifold with boundary on the boundary of
R, and every line parallel to d meets it at most once. Theorem 4 (its proof uses of the cell only that it is convex and
that (E), (F) hold on its boundary): no part is closed, every part has a boundary circle, and the parts are all disks
if their number is m. With m = 1 there is one part, a disk. With m = 0 there is no part: L does not meet R. Near every
point L is a surface (Lemmas 5.1, 5.2, 8.1), so it is closed.

(b) Lemma 6.1 makes K' a complex for Theorem 1. Steps 1 to 3 of Theorem 1 need arcs on rectangles only. Step 4 in a
block with one disk D_1: the sphere made of D_1 and either of the two disks its circle cuts from the boundary of R is
locally flat (inside D_1 by Lemma 5.1, on the boundary of R away from L because R is convex, on the circle by Lemma
8.1; the two disks of the boundary of R carry the two signs of f, so the two spheres bound R with {f <= 0} and R with
{f >= 0}), so by Brown's two theorems, as in the proof of Theorem 5, D_1 cuts R into two balls; the cone extension and
the Alexander trick finish. In a 3-cell of K: Theorem 5. If a block contains cells of the outer layer, h is still the
identity on the boundary of the grid's box, which no surface meets (as at the end of Theorem 6's proof). For Theorem 1b
the same, on the certified cells of K' only.

(c) By Theorem 2, D is compatible with K, so (E) and (F) hold for D on the edges and faces of K' and (C) in the cells of
K in no block; in a block, Lemma 6.4 and Lemma 6.2 with the stated count give disks. The patterns agree: L crosses an
edge of K' exactly if f changes sign between its ends ((S1): strictly monotone, or no zero), D exactly if the contour's
signs differ there, and these are the same signs; on a face with four crossing points both pairings are the one the
separating segment allows. A block has no pattern inside; in a block the count makes D a set of disks, as many as
its boundary carries circles, which is the number m of the common pattern (none in a zero-free block: a wrong sign at
an inner node there would give a closed component, which the count refuses). D does not meet the boundary of the
grid's box: by (H1) every polygon of D lies in the interior of the four cells around its edge (Theorem 2). (b) with D
as the PL surface. ∎

Remarks.

1. **Why boxes.** Convexity of the block is used four times: in Lemma 5.1 (the segment of the mean value theorem stays
   in R), in Theorem 4 (the projection along d is injective on L in R because every line parallel to d meets R in one
   segment), in Lemma 8.1 (a direction pointing strictly into a convex set makes its boundary a graph) and for the
   local flatness of the boundary of R. A block that is a general ball of cells, as in Theorem 6, is not covered: in an
   L-shaped block a line along d can leave and enter again, and L in R need not be a graph. Whether a block that is
   convex along d only would do is not worked out.
2. **With box enclosures, (B3)'s direction is an axis.** If G(R) is enclosed by an axis-aligned box of intervals, a
   direction d with d . g > 0 on the box exists exactly if one component interval excludes 0, and that axis, with the
   sign of the interval, is such a d. So (B3) is "one partial derivative keeps its sign on R" (the enclosure over R
   being the hull of the enclosures over sub-boxes that cover R). As a condition on the gradients this is weaker than
   A+ of Theorem 7 (no condition on pairs); a certified block needs (B0) to (B2) and m <= 1 besides, which A+ does not.
3. **More than one circle.** Theorem 4's second form (surfaces without zeros that separate the circles) would admit
   m > 1. For boxes the only cheap such surface is a plane of faces inside R on which f has no zero, and then the block
   can be split there into two blocks instead. So the statement keeps m <= 1.
4. **What a block absorbs, and what it does not.** Inside a block: an edge crossed twice, a closed curve of L in a face,
   a face with alternating corner signs, a node on L, and a wrong contour sign at an inner node as long as the contour
   in the block is still disks. Not absorbed: a part of L that is closed inside the block or two
   sheets through it (then no partial derivative keeps its sign, or m > 1); a tangency on the block's own boundary (the
   block must grow or move); a crease on which no partial derivative keeps its sign for all active pieces (with box
   enclosures that can be a crease well below 180 degrees); a lost handle (the contour side's count fails, or the
   patterns differ on the boundary). A wrong contour is never certified, by (c).
5. **No cell-shape condition.** Theorems 1, 2, 5, 6 and 8 use that cells are boxes (Theorem 2: the four cells around an
   edge form a convex box), never that they are cubes. A certificate by this theorem needs no (P3): the grid of
   millimetre cells at UTM coordinates, whose spacings differ by a rounding unit of the coordinate, is covered as it
   is. The enclosures are taken over the boxes between the grid's own doubles.
6. **Rounding.** The proof adds no bound. Every test is the sign of an outward-rounded enclosure over a box whose
   corners are node coordinates (or over boxes that cover a separating segment, remark 7), an exact comparison of
   doubles, or a count on the pattern.
7. **Faces with alternating corner signs.** With (S2) in its axis form such a face is never certified: f strictly
   monotone along an axis of the face contradicts alternating signs at its corners. So with axis tests only, the
   separating segment and the face-rule hypothesis of (c) are never used, and every such face must lie inside a block
   or is refused. It can pass only by (S2') with an oblique direction a; the separating segment is then oblique too (a
   diagonal of the face in the simplest case), and its test is an enclosure of f over boxes that cover the segment.
8. **Sources.** Nothing new: Theorems 1, 2, 4, 5 and Lemmas 5.1, 5.2, 6.1, 6.2, 6.4 of this note with their sources
   (Brown 1960 and 1962, Clarke 1983).
9. **(B3) on the sub-boxes that can hold a zero** (used by the measurement of section 3a; checked independently on
   2026-11-22 with Lemma 9, and rewritten after that check). It is enough that d . g > 0 for all g in G(S) for every
   box S of a finite set of closed boxes in R that covers every zero of f in R. *Proof.* Let p be a zero in R, in the
   listed box S. G(S) contains the generalized gradients at p, so by upper semicontinuity, as in Lemma 5.2, there is a
   ball B around p with d . g >= delta > 0 at all its points; by Lemma 5.1 on the convex set B, f increases strictly
   along d through p, and L is in B a Lipschitz graph along d. So on a line parallel to d, which meets the convex R
   in a segment, every zero has a neighbourhood without another zero: the zeros on the segment are finitely many.
   Were there two, take two consecutive ones: just after the first f > 0, just before the second f < 0, so there is a
   zero between them, a contradiction. Hence the projection along d is injective on L in R, and L is near each of its
   points in the interior of R a Lipschitz graph; at its points on the boundary of R Lemma 8.1 needs nothing of (B3).
   That is all Theorem 8 uses: in (a), Theorem 4 needs the compact 2-manifold and the injective projection (L in R is
   then locally, not globally, a Lipschitz graph); in (b), the local flatness inside D_1 is local, and the two
   pieces of the boundary of R carry the two signs of f because each piece of R minus D_1 is connected and without a
   zero, and the signs differ across D_1 by Lemma 8.1 (i). With this form an axis is no longer the only candidate
   for d (enclosures [2, 3] x [-1, 1] and [-1, 1] x [2, 3] admit d = (1, 1) and no axis); a sufficient test, for
   instance: one partial derivative
   keeps its sign on every listed sub-box, the same one with the same sign.

## 2. Lemma 7.1: the cell shape in Theorem 7

**Lemma 7.1 (the cell shape in Theorem 7, with a proved margin).** Theorem 7 rests on a statement for cubes. Let the
node coordinates increase strictly on each axis, let m and M be the smallest and the largest spacing over all three
axes, and let rho >= M / m. Call a cell A+(rho) if f is smooth on a neighbourhood of it and an axis-aligned box of
intervals [l_i, u_i] that contains every gradient of f on the cell satisfies

    sum over the components with 0 not in [l_i, u_i] of min(l_i^2, u_i^2)  +  rho^2 * sum over the others of l_i u_i  >  0

(for rho = 1 this is the test A+ of section 7.13; the second sum is not positive). Then Theorem 7 holds with (P1)
"every cell satisfies Z or A+(rho)" and without (P3). (Theorem 2, used in its step (4), needs boxes only: remark 5.)

*Proof.* The inequality is strict and the cells are finitely many, so if every cell that is not Z passes A+(rho), it
passes A+(rho') for some rho' > rho >= M / m (the smallest of the cells' margins); call that rho again, so rho > M / m.
(If M = m, no change of coordinates is needed: the cells are cubes.) After step (2) of Theorem 7's proof f is smooth
everywhere. Choose delta > 0 with M (1 + delta) <= rho m (1 - delta). For each axis there is a smooth increasing function phi_i of the real line with
phi_i(j) = the j-th node coordinate of that axis and m (1 - delta) <= phi_i' <= M (1 + delta): let phi_i' be the
spacing of the cell on the middle part of each unit interval, pass monotonically from one spacing to the next in a
window of width 2 eta around each integer, and add a smooth bump inside each interval that restores its integral to
the spacing; the two half windows of an interval change its integral by at most 2 eta (M - m), so for eta small the
bumps keep phi_i' in the stated range. Outside the grid phi_i' continues with the first and the last spacing. Phi = (phi_1, phi_2, phi_3) is a diffeomorphism of space that maps the grid of unit cubes with integer
nodes onto K, cell onto cell. Let f~ = f o Phi. It is smooth, its zero set is the preimage of L, a cell of K is
zero-free exactly if its unit cube is, and the node signs are the same. Its partial derivatives are
phi_i'(y_i) times those of f at Phi(y). So on the unit cube of an A+(rho) cell the i-th component of the gradient of
f~ lies in [m (1 - delta) l_i, M (1 + delta) u_i] if l_i > 0 (mirrored if u_i < 0) and in
M (1 + delta) [l_i, u_i] otherwise. The interval inner product of that box with itself has the lower bound

    m^2 (1 - delta)^2 * ( sum of min(l_i^2, u_i^2)  +  (M (1 + delta) / (m (1 - delta)))^2 * sum of l_i u_i ),

which is positive, because the factor of the second sum is at most rho^2 and that sum is not positive. So every unit
cube satisfies Z or A+ for f~: the premise PV applies to f~ on exact cubes and gives a marching-cubes-type mesh M~
isotopic to the zero set of f~. Phi is isotopic to the identity through the diffeomorphisms (1 - t) id + t Phi (each
coordinate stays strictly increasing; cut off outside a large ball if a compactly supported isotopy is wanted), so the
zero set of f~ is isotopic to L. Let Psi be the piecewise linear map that
interpolates the node coordinates on each axis; it is isotopic to the identity in the same way, and Psi(M~) is a closed
PL surface compatible with K with the pattern of the node signs and the chosen pairing. Steps (4) to (6) of Theorem 7
with Psi(M~) in the place of M: D ~ Psi(M~) ~ M~ ~ zero set of f~ ~ L. ∎

What this changes. The premise PV is now used for exact cubes only, so the second of the three "read, not quoted"
points of section 7.13 goes away. The code of #1392 tests A+(1) and accepts spacings within 2^-20: between that and the
lemma lie the cells with 0 < (sum) <= (rho^2 - 1) |second sum|, a margin of about twice the tolerance relative to the second
sum: 2^-19 at the default `CellShapeTolerance` of 2^-20, about 2^-9 at `MaxCellShapeTolerance` = 2^-10
(only cells with a gradient component through 0 are affected). Closing
it is a small change with a new bound, so it needs the exact review: compute an upper bound rho^2 of (M / m)^2 from the
node coordinates (outward rounded), multiply the negative products of the gradient test by it, rounded down. Then the
tolerance is no longer a refusal: the millimetre grid at UTM coordinates (M / m about 1 + 2e-6) is tested with
rho^2 about 1 + 4e-6 and certified unless a cell passes the gradient test by less than that. The alternative, exact
cubes only, would refuse every grid on which `origin + h * i` is rounded (h not a power of two, or an origin far
from 0 against h), UTM coordinates among them; it is not needed.

## 3. What is missing for a certificate of `TpmsLattice`

- *The mesh side for marching cubes.* `TpmsLattice` meshes with `MarchingCubes`, and (c) is stated for
  `DualContouring` (Theorem 2). Part (b) needs only a PL surface compatible with K' with L's pattern. For a
  marching-cubes mesh that is a lemma of its own, not proved here: every mesh vertex strictly inside its grid edge (an
  exact comparison; linear interpolation can round onto a node), the table's pairing on a face with alternating signs a
  function of that face's signs only (so both cells agree), per cell one triangulated disk per patch (a finite check of
  the table), no self-intersections (exact), and a push of the triangles and diagonals that lie in a face into their
  cell (a small isotopy, to be written down). In a block the contour side's count (Lemma 6.4) is stated for patches and
  carries over.
- *The choice of blocks* is the implementation's: the clusters of cells refused by (S1) or (S2), covered by boxes, grown
  until (B0) to (B2) hold on the boundary, merged or shrunk until the interiors are disjoint. The theorem holds for any
  choice.
- *Measured (2026-11-22), level-set side only: box blocks close the gap for smooth lattices and not for clipped
  ones.* Section 3a.

## 3a. Measurement: how many refused cells do box blocks cover?

[benchmarks/ContouringIsotopySubdivision](../../benchmarks/ContouringIsotopySubdivision/README.md), mode `blocks`
(`Blocks.cs`, `results/blocks.csv`): the spike's cell test (S0) to (S3), then every refused surface cell as the seed
of a box that grows by one layer on the sides that carry a failing node, edge or face, until (B0) to (B3) pass or the
box exceeds 6 cells per side or reaches the outer layer; overlapping boxes are merged into their bounding box. (B3) in
the form of remark 9. Only the level-set side: the contour's count in a block (Lemma 6.4) is not measured, so
"complete" below means that every surface cell is certified or lies in a passing block, not that the contour is
certified. 42 runs, axis-aligned and rotated, one machine (40 in the table; the two runs of two spheres half a cell
apart are in no row: nothing refused in one, 157 of 178 covered in the other).

| Set | Runs | Refused by the cell test (of the surface cells) | Covered by blocks | Left (of the surface cells) | Runs complete: cell test -> with blocks |
|---|---|---|---|---|---|
| capped TPMS (gyroid, Schwarz P, D), 32 cells per period | 6 | 6.2 % | 96.5 % | 0.21 % | 0 -> 5 |
| capped TPMS, 16 cells per period | 6 | 15.2 % | 84.9 % | 2.3 % | 0 -> 1 |
| smooth controls (sphere, torus, thick shell) | 8 | 2.3 % | 100 % | 0 | 2 -> 8 |
| **TPMS clipped by a ball** (a crease at the cut), 32 cells per period | 6 | 22.0 % | 32.6 % | 14.9 % | 0 -> 0 |
| TPMS clipped by a ball, 16 cells per period | 6 | 39.7 % | 22.4 % | 30.9 % | 0 -> 0 |
| creases (tunnel, rod: 3 cells wide) | 4 | 5.6 % | 74.3 % | 1.4 % | 0 -> 1 |
| thin controls (torus and shell of half a cell) | 4 | 69.0 % | 0.1 % | 69.0 % | 0 -> 0 |

- **Smooth lattices: the blocks work.** At 32 cells per period five of six runs are complete, the sixth (Schwarz D,
  rotated) leaves 220 of 19 876 surface cells, all for a missing direction. The blocks are small: of the 2 169 blocks
  of the twelve smooth TPMS runs, 1 854 are two cells wide, 302 wider and 13 one cell. The block step costs 7 to 27 %
  of the cell test there.
- **Clipped lattices: box blocks grown this way do not close them.** About a third of the refused cells overall
  (32.6 % at 32 cells per period, 22.4 % at 16), between 9 and 56 % per run, and no run complete. Of the seeds left at
  32 cells per period, 13 442 fail by an edge, 1 999 by a face, 1 904 by the direction. Of the 5 196 blocks, 2 956
  are one cell (a cell that passes when each edge and face may use its own tightening). The coverage rises slowly
  with the size limit (limit 3, 6, 10: 36, 39 and 46 % on one clipped gyroid). Only this one greedy growth rule was
  measured.
- *An explanation, not a measurement* (the results do not record where the refused cells lie): at the cut the field
  is a `max` of two pieces, and an edge near the rim is refused whenever the two pieces' derivatives along it differ
  in sign (it may enter the solid through one piece and leave through the other). That would hold along the whole rim
  for a fixed share of the edges, so the refusals would be a band along a curve, as the silhouette bands of meshes
  (section 7.10's estimate), and a box that takes in a stretch of the band meets it again on its own boundary.
- The thin controls stay refused, as they must.
- Not measured: wrong contours (the contour's side is not in the prototype), and the cost against a contouring.

**Decision (owner, 2026-11-22):** Theorem 8 is built for levels of a smooth field only. The route to a lattice clipped
to a domain is not built; it stays an open limit of the contouring certificates (also in
[roadmap/research.md](../roadmap/research.md)).

**What follows.** For a level of a smooth field (a lattice with a smooth cap, a level g = +-w of
`TwoLevelMarchingTetrahedra` without a domain) Theorem 8 is worth building: it turns "2 to 15 % of the cells refused
at every resolution" into complete runs at 32 cells per period. For a lattice clipped to a domain, box blocks grown
this way do not close the gap. If the explanation above is right, the rim needs blocks that follow a curve (general
balls of cells, for which the convexity arguments of remark 1 do not hold) or a statement for two surfaces that cross
(the wall and the cut, each smooth); neither exists here, and another growth rule for boxes was not tried.

## 4. The per-level certificate of `TwoLevelMarchingTetrahedra` (the queued brief): covered and open

The brief: a non-exact certificate that each of the two levels g = +w and g = -w is isotopic to its mesh, cell by cell,
by Theorem 5's tests (S0) to (S3) once per level on the interval interface of #1392; the mesh is made on five
tetrahedra per cube. Against this note:

**Covered.**

- *The level-set side* is Theorem 5 (and Theorem 8 with blocks) applied to g - w and to g + w: it does not depend on
  the mesher. The cells are the cubes; no cell-shape condition (remark 5).
- *The tetrahedra do not need a test of their own, if the mesh side below holds.* The mesh is compared with L on the
  cube grid K. Its vertices on face diagonals lie inside faces of K, where compatibility asks only for arcs.
- *The face diagonal and (S2).* On a face of K with alternating corner signs the two corners of the split's diagonal
  have the same sign, so the mesh has no vertex on the diagonal and pairs the crossings across it: the diagonal is the
  mesh's separating segment. The certificate takes it as the segment of (S2): f has no zero on that diagonal (an
  enclosure over boxes that cover it), with a direction a of (S2') that is not an axis (the natural one is along the
  diagonal: f strictly monotone along it over the whole face). By remark 7 the axis form of (S2) never passes such a
  face, so this oblique test, or a block around the face, is the only way to certify it; where L pairs the other way
  the cell is refused, rightly. Expect few such faces to pass: a face with alternating signs usually holds a point
  where the gradient is normal to the face (a saddle of f in the face), and then no direction of (S2') exists, so
  these faces mostly go into blocks. The split alternates with the parity of the cube, so both cubes at a
  face use the same diagonal.
- *One disk per circle in a cube: a finite check, done.* For the split (0,3,5,6), (1,0,3,5), (2,0,3,6), (4,0,5,6),
  (7,3,5,6) (the other parity is its mirror image) and each of the 256 corner-sign patterns, the marching-tetrahedra
  surface in the cube was enumerated as a cell complex (vertices on sign-changing tetrahedron edges, one segment per
  mixed tetrahedron face, one polygon per mixed tetrahedron): every component has Euler characteristic 1 and the
  components are as many as the circles on the cube's boundary (0, 1, 2, 3 or 4 circles in 2, 168, 76, 8 and 2
  patterns). So the surface meets every cube in disks, (C). This becomes a test of the build. It covers corner values
  with a sign only (+ or -); a value of exactly zero is refused by (S0).

**Open, to be proved or decided before the build.**

1. *Lemma for the mesh side (marching tetrahedra).* Written: Lemma 9, section 4a.
2. *Node values moved by `SliverGuard` and `RimGuard`.* Only signs and "strictly inside the edge" enter the pattern.
   The certificate compares the certified sign of g - w (g + w) at a node with the sign of the value the mesher used;
   a node whose sign a guard changed is refused with its cells. No bound on the size of the change is needed.
3. *The two levels together.* Theorem 1 asks that an edge meets the surface at most once, (E); the union of the two
   levels meets an edge twice where the wall is thinner than a cell, which is the mesher's purpose. So the statement
   is per level: each side is isotopic to its mesh, by two isotopies. That the pair, and so the wall between them, is
   isotopic to the meshed wall needs Theorem 1 for several disjoint surfaces with ordered crossings on an edge. That
   is plausible (g strictly monotone on the edge orders the crossings) and not proved.
4. *The rim at a domain clip.* The boundary of {|g| <= w, d <= 0} is not a level set of one function per level; as one
   function max(|g| - w, d) it has creases along the rim and the mesh crosses grid edges several times. Not covered by
   a per-level statement; it would need point 3 and Theorem 5 on that function. Without a domain, or where the clip is
   far, the per-level statement stands.
5. *Tangencies.* (S1) and (S2) refuse them at every resolution; blocks (Theorem 8) are the answer, and they are not
   measured (section 3).

## 4a. Lemma 9: the marching-tetrahedra mesh of one level is compatible with the cube grid

Owner decision 2026-11-22, after the measurement of section 3a: Theorem 8 is built for levels of a smooth field only,
which includes the per-level certificate of `TwoLevelMarchingTetrahedra` without a domain. This is its mesh side. A
lattice clipped to a domain is not built (section 3a; an open limit).

**Setting.** K is the grid of cubes of `TwoLevelMarchingTetrahedra` (boxes between the node coordinates
`origin + h * i`), each split into five tetrahedra, the split mirrored with the parity of the cube, so that the two
cubes at a face use the same diagonal of it. A *level* is one of the mesher's functions (g - w, or -g - w) without a
`Domain`: a value a_n at every node, as the mesher used it (after `SliverGuard`). A node is *inside* if a_n < 0. A
tetrahedron edge is *crossed* if exactly one of its ends is inside; it is an edge of a cube or a diagonal of a face
of a cube (the split has no diagonal through the interior of a cube). M is the part of the output mesh that belongs
to the level: one vertex v_e per crossed tetrahedron edge e, and per tetrahedron with inside and outside nodes the
polygon through the vertices of its crossed edges (a triangle if one node is alone on its side, else a
quadrilateral cut into two triangles from one of its vertices), in the order in which neighbours lie in one face of
the tetrahedron.

Hypotheses:

- (T0) no tetrahedron edge on the boundary of the grid is crossed (read from the signs; M then stays off the grid's
  boundary);
- (T1) no a_n is 0 (so "inside" is the sign of a_n);
- (T2) every vertex on a cube edge lies strictly between the two nodes of the edge and has their other two
  coordinates; every vertex on a face diagonal has the face's constant coordinate and lies strictly inside the face's
  rectangle (exact comparisons of doubles; the vertex need not be exactly on the diagonal);
- (T3) M is exactly the triangles described above: no polygon is clipped, dropped or reduced. This is recorded by
  the mesher, not argued (remark 6): no polygon vertex was found outside the other level, and no triangle was left
  out;
- (T4) two different triangles of M meet exactly in the hull of the mesh vertices they share: nothing, a vertex or a
  side (the exact self-intersection test, which must report a pair that shares a side and overlaps or folds onto
  itself, and a pair that shares a vertex and meets elsewhere).

**Lemma 9.** Under (T0) to (T4), M is a closed surface in the interior of the grid, compatible with K. Its pattern
is: the crossed cube edges; and on a face with four crossed edges, the pairing that joins the two
edges at each corner that is not on the face's diagonal.

*Proof.* A cube is convex, and so are its faces and edges, which are faces of it as a convex set: a convex
combination of points of the cube lies in a face (an edge) of the cube only if every point with a positive weight
does. Call this (X). A triangle of M that belongs to a tetrahedron of the cube c has its vertices on tetrahedron
edges of c, so in c by (T2), and lies in c.

*Which vertices lie where.* By (T2) a vertex v_e lies in a face f of c exactly if e does (e is an edge of f or its
diagonal), and on a cube edge exactly if e is that edge. A tetrahedron of the split meets the plane of a face of its
cube in one of its own faces, edges or nodes. The crossed edges of a tetrahedron are never all in one of its faces
(three edges at one node are not in a face; the four edges between two pairs of nodes are not), and no three vertices
of a quadrilateral's triangle are (a face of the tetrahedron holds two of the four crossed edges at most). So no
triangle of M has its three vertices in one face of c. Consequently no triangle is degenerate: different vertices
are different points (they lie in the relative interiors of different edges and faces of cubes, by (T2)), and if
three were on a line, the middle one would be a proper convex combination of the other two and lie in a face of c, so
by (X) all three would lie in that face. A triangle of another cube c' lies in c' and meets c only in the common
face, edge or node of the two cubes, where by (X) it has the hull of its vertices there: a vertex v_e of a crossed
edge of c, or a side in a common half face. So below it adds nothing to what the triangles of c give.

*(E).* By (X), a triangle meets a cube edge e' in the hull of its vertices on e', and the only vertex on e' is
v_(e'), for a crossed e'. So M meets e' in v_(e'), an interior point by (T2), or not at all; no node is on M.

*(F).* By (X), a triangle of c meets a face f of c in the hull of its vertices in f: nothing, a vertex, or one of
its sides (not the triangle, by the above). A side of a triangle with both ends in f joins the vertices of two
crossed edges of one face of a tetrahedron (it is not the cut of a quadrilateral, whose ends are on opposite edges
of the tetrahedron, which no face plane holds both), and that face lies in f: it is one of the two triangles the diagonal
cuts f into. In such a half face with inside and outside corners exactly two of its three sides are crossed, and
the polygon of the tetrahedron behind it has those two vertices as neighbours, so M has the segment between them
(the same segment for the tetrahedron of the cube on the other side of f, which has the same half face). So M meets
f in one segment per half face with inside and outside corners. If the diagonal is crossed, both halves have such a
segment, each from the diagonal's vertex to a vertex on a cube edge: one arc with two crossed cube edges. If it is
not, its ends have one sign, and each half has a segment or none: none, one arc, or two arcs, the last exactly if
the two corners off the diagonal have the other sign, which is the face with four crossed edges; each of the two
arcs then joins the two edges at one of these corners, and the two arcs are disjoint (each lies in its closed half
with its ends inside cube edges, so off the diagonal). The arcs end in the interiors of cube edges, and their other
points are in the interior of f by (X) for the rectangle. The two segments of an arc through the diagonal's vertex
meet only there: they run from a point inside the rectangle to two different points of its boundary. No closed
curve, no isolated point.

*(C).* M meets c in the triangles of the tetrahedra of c (a triangle of another cube lies in that cube and meets c
in a common face only, in sides that are sides of triangles of c as well). As a cell complex (vertices; one segment
per tetrahedron face with inside and outside nodes; one polygon per such tetrahedron) this depends on the signs at
the eight corners only, and for each of the 256 sign patterns every component of it has Euler characteristic 1 and
there are as many components as closed curves of segments on the faces of c (the enumeration of section 4, for one
parity; the other is its mirror image). The complex is a surface with boundary: a segment on a tetrahedron face
inside c lies in two polygons, one on a face of c in one, and the polygons around a vertex form a fan (it is the
complex of the zero set of the piecewise linear interpolant of any values with those signs; also checked in the
enumeration). A compact connected surface with Euler characteristic 1 is a disk or a projective plane, and as many
components as circles leaves the disk: each component is a disk with one boundary circle. The triangles are not
degenerate and their vertices are different points (above), and by (T4) they meet only as the complex says, so the
linear map of the triangulated complex is injective: M meets c in disjoint embedded disks. Their boundaries are the arcs on the
faces of c; an open triangle and an open side that is not in a face lie in the interior of c by (X); the vertices
all lie on the arcs. So the interior of each disk is in the interior of c.

Every point of M has a disk neighbourhood in M (inside a cube by (C); on a face or an edge the disks of the two or
four cubes fit along the arcs), so M is a closed surface where it does not reach the grid's boundary. ∎

Remarks.

1. **What the rounding changes, and what it does not.** In exact arithmetic M is the zero set of the piecewise linear
   interpolant, and (T4) holds by construction. The vertices are rounded doubles: on a cube edge the two fixed
   coordinates are exact (the mesher computes a + t (b - a) with b - a = 0 there), on a diagonal only the face's
   coordinate is. The proof therefore uses only (T2) and never that a vertex is on its diagonal or that a polygon is
   planar, and it takes (T4) from the exact test instead of the construction.
2. **Zeros and the guard.** A value of exactly 0 counts as outside for the mesher; (T1) refuses it. `SliverGuard`
   removes zeros (a value below its threshold becomes negative) except when the level changes sign on no grid edge
   or the threshold underflows to 0. The position a + t (b - a) can round onto a node, or one unit past it, also for
   a t inside (0, 1): (T2) tests the position, not t.
3. **In a block** (Theorem 8 (c) for this mesher). The count P - A + Z of Lemma 6.4 becomes: the cell complex above
   over the cubes of the block (polygons, minus the segments on tetrahedron faces inside the block, plus the vertices
   on tetrahedron edges inside the block), its components, and whether each has a segment on the block's boundary;
   Lemma 6.2 as it is. All from the signs.
4. **The pattern against the level set's.** On a face with four crossed edges the mesh pairs across the diagonal that
   the split gives the face. Theorem 8 needs the level set to pair the same way, by a certified sign on that diagonal
   (remark 7 of section 1), or the face inside a block.
5. **Scope.** One level, no `Domain`. With a constant `HalfWidth` the level's field is g - w; a `HalfWidthF` needs an
   interval form of its own. The two levels together stay per level (point 3 below).
6. **What the mesher has to record** (it keeps none of this today): the node values it used, per level, for (T1) and
   for the comparison with the certified signs of the field (for the level -g - w the certified sign of g + w,
   negated); for every vertex its tetrahedron edge, for (T2); the level of every triangle; and for (T3) two counters
   that must be 0, the polygon vertices found outside the other level and the triangles left out. The first counter
   is needed: on an edge that both levels cross, the mesher orders the two crossings by two separately rounded
   quotients, and although the other level is inside at the crossing in exact arithmetic, a reversal by rounding is
   not excluded by proof (none was found in 6e6 random trials of values as the mesher computes them; for values a few
   units apart a reversal exists). A reversal clips a polygon.
7. **Theorem 8 (c) for this mesher**: with Lemma 9 in the place of Theorem 2 and remark 3 in the place of Lemma 6.4;
   (T0) in the place of what (H1) gives for the boundary of the grid.

## 5. Review

Independent review on 2026-11-22 (an agent with the design note only, asked to find errors and gaps): Theorem 8 (a),
(b), (c) and Lemma 7.1 hold; no error found. Its findings are worked in:

- Lemma 8.1 is split into what needs (B0) to (B2) only and the rest, because the circle count of (B3) uses (E) and (F)
  on the block's boundary (the first version cited Theorem 5 there, which would have been circular in form).
- Lemma 7.1 is stated for rho >= M / m (an outward-rounded bound can be exact), with the finiteness argument; the
  integral change of the windows is 2 eta (M - m); the continuation outside the grid; the margin is relative to the
  second sum.
- A second review (the coordinator's referee, with the note and the code, on #1413) found no error either and
  reproduced the enumeration of section 4; its two corrections (which theorems need no cubes; the margin as twice the
  tolerance) and four smaller ones are worked in.
- Theorem 2's setting in the design note said "uniform grid"; its proof was checked step by step for a hidden use of
  equal spacings and has none. The setting sentence is changed.
- Faces with alternating corner signs can never pass the axis form of (S2) (remark 7); remark 6 on rounding and the
  wording of remarks 1, 2 and 4 corrected (a fourth use of convexity; creases with box enclosures; a wrong inner sign
  is absorbed only while the contour in the block is still disks).
- The outer layer inside a block, the true sign at nodes of zero-free cells, and the count in zero-free blocks are
  now said in Theorem 8 and its proof.

Section 4 was written after the first review and read by the second. Remark 9 and Lemma 9 (section 4a) were checked
by a third independent reader on 2026-11-22 (the note, the design note and the mesher's code; the enumeration and the
combinatorial claims about the two splits reproduced by script): Lemma 9 holds, no error; remark 9's statement holds,
but its first argument used more than it stated and is replaced by the one above (upper semicontinuity at each zero).
Worked in: the manifold step before "Euler characteristic 1 means disk", non-degenerate triangles and the exact
reading of (T4), triangles of neighbouring cubes, (T0), the wording on zeros and rounding, and remark 6: one code
path (two crossings on one edge ordered by rounded quotients) can make (T3) false and is to be counted, not argued.

## 6. Next steps (for the coordinator and the owner)

1. Coordinator's review of this proof.
2. Lemma 7.1 in the code of #1392: the weighted gradient test and the spacing ratio as a bound (small, exact review).
3. The measurement before any build of blocks: done, section 3a. Blocks close the gap for smooth levels and not for
   a lattice clipped to a domain.
4. The mesh-side lemma for the mesher that is to be certified (marching tetrahedra: point 1 of section 4; marching
   cubes: section 3), then the build. A public option of `TpmsLattice` is the owner's decision, on an API sketch.
