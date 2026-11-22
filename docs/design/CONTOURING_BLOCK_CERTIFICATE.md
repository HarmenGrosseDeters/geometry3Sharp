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
- **No theorem of the design note needs cubes** (remark 5); a certificate by Theorem 5 or 8 has no cell-shape condition.
- **Lemma 7.1** (section 2): the smooth certificate of #1392 (Theorem 7) keeps its premise for exact cubes and gets a
  proved margin instead of the tolerance (P3): the negative products of the gradient test are multiplied by the square
  of the largest spacing ratio. That is a small code change with a new bound; today's code is short of the lemma by a
  margin of about 2^-19 on that test.
- **Open** (sections 3 and 4): the mesh side for marching cubes and for marching tetrahedra on cubes (lemmas of their
  own; a finite check for the tetrahedra is done here), the two levels of a wall together, and a measurement of how
  many refused cells box blocks cover, which comes before any build.

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
- (B2) every face of K on R satisfies (S2), the separating segment on a face with four crossing points included;
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
grid's box: its nodes are nodes of K' with the true sign, and f has one sign on the connected outer layer. (b) with D
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
lemma lie the cells with 0 < (sum) <= (rho^2 - 1) |second sum|, a margin of about 2^-19 relative to the second sum
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
- *It is not measured.* The spike measured Theorem 5's cell test without blocks. Whether small boxes with one circle and
  one signed partial derivative cover the refused cells of a clipped TPMS is open; for meshes, box blocks covered much
  less than general blocks (section 7.10's estimate: 31 to 40 %), because silhouette bands run diagonally. For a
  function the refused edges are not a band but single edges near the silhouette curve (an edge is crossed twice only
  if both crossings fall into the same edge), so small boxes around them are plausible. The measurement comes before a
  build: the spike's prototype has the tests (S0) to (S3) and can count the boxes that pass (B0) to (B3).

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
  patterns). So the surface meets every cube in disks, (C). This becomes a test of the build.

**Open, to be proved or decided before the build.**

1. *Lemma for the mesh side (marching tetrahedra).* To write down: with every vertex strictly inside its tetrahedron
   edge (exact comparisons; the mesher's guards exist for this), the level surface of the piecewise linear interpolant
   is a closed PL surface compatible with K, with the sign-changing cube edges and the diagonal's pairing as pattern:
   (E) and (F) from the construction (one planar piece per tetrahedron, pieces of neighbours meeting in the common
   segment), (C) from the finite check. No self-intersection test is needed, by the mesher's construction. With a
   block, the count P - A + Z of Lemma 6.4 is replaced by the Euler characteristic and the components of that cell
   complex in the block (Lemma 6.2 as it is).
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

## 5. Review

Independent review on 2026-11-22 (an agent with the design note only, asked to find errors and gaps): Theorem 8 (a),
(b), (c) and Lemma 7.1 hold; no error found. Its findings are worked in:

- Lemma 8.1 is split into what needs (B0) to (B2) only and the rest, because the circle count of (B3) uses (E) and (F)
  on the block's boundary (the first version cited Theorem 5 there, which would have been circular in form).
- Lemma 7.1 is stated for rho >= M / m (an outward-rounded bound can be exact), with the finiteness argument; the
  integral change of the windows is 2 eta (M - m); the continuation outside the grid; the margin is relative to the
  second sum.
- Theorem 2's setting in the design note said "uniform grid"; its proof was checked step by step for a hidden use of
  equal spacings and has none. The setting sentence is changed.
- Faces with alternating corner signs can never pass the axis form of (S2) (remark 7); remark 6 on rounding and the
  wording of remarks 1, 2 and 4 corrected (a fourth use of convexity; creases with box enclosures; a wrong inner sign
  is absorbed only while the contour in the block is still disks).
- The outer layer inside a block, the true sign at nodes of zero-free cells, and the count in zero-free blocks are
  now said in Theorem 8 and its proof.

Not reviewed by a second reader: section 4 (written after the review; its finite check is a script result, to become
a test).

## 6. Next steps (for the coordinator and the owner)

1. Coordinator's review of this proof.
2. Lemma 7.1 in the code of #1392: the weighted gradient test and the spacing ratio as a bound (small, exact review).
3. A measurement before any build of blocks: on the spike's prototype (`benchmarks/ContouringIsotopySubdivision`,
   which has (S0) to (S3)), the share of refused cells of capped and clipped TPMS that boxes with (B0) to (B3) cover.
4. The mesh-side lemma for the mesher that is to be certified (marching tetrahedra: point 1 of section 4; marching
   cubes: section 3), then the build. A public option of `TpmsLattice` is the owner's decision, on an API sketch.
