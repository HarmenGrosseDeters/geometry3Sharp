# The distance-field enclosure for the offset topology certificate (stage A2, proof first)

Status: **written proof, reviewed 2026-11-13** (fresh session, routed by the coordinator, on head d659949): Theorem E
and the sign reduction sound, (S2') accepted, Theorem O holds with the hypotheses added below; no must-fix. The review
findings are worked in (premises from A1 in section 1.1, the decidable form of Theorem O, the conditions of (S2'), the
answers in section 9). Decision 2 of [MESH_OFFSET.md](MESH_OFFSET.md), section 9: A2 may now be built. No code.

Context: stage A1 (`gs.MeshOffset`, #799) contours F = d - sd with dual contouring and proves one side of the band.
A2 adds the topology certificate: class S of the contouring certificate, Theorem 5 of `docs/design/CONTOURING_TOPOLOGY.md`
on #749, section 7.8 (reviewed 2026-11-13). That theorem asks, for every cell X the level set meets, for a compact
convex set G(X) that contains every Clarke generalized gradient of the field at every point of X, plus the cell tests
(S0)-(S3) on G. This note:

- proves that a set computable from the input's doubles is such an enclosure for F (section 2);
- reduces (S1)-(S3) on it to finitely many exact sign tests (section 3);
- states what the certificate then proves for `MeshOffset`, including the second side of the band (section 4);
- lists where it cannot pass, and why that is right (section 5);
- proposes one relaxation of (S2) for the review to accept or refuse (section 6);
- gives the A2 test plan and cost (sections 7, 8) and the review questions (section 9).

Notation: M is the input, a closed triangle mesh that is the boundary of a solid S (clean: closed, manifold, oriented
outward, no self-intersections). T is its set of triangles, each a compact convex set in R^3.
- u(x) = dist(x, M) = min over t in T of u_t(x), with u_t(x) = dist(x, t).
- sd = u outside S, sd = -u inside.
- F = d - sd, inside of the offset solid where F > 0, and L = {F = 0}.
- ∂g(x) is Clarke's generalized gradient. Citations of Clarke's book (F. H. Clarke, *Optimization and Nonsmooth
  Analysis*, Wiley 1983) are by statement; the proposition numbers are recalled [m] and must be checked before they go
  into docs/SOURCES.md. The #749 note marks them the same way.

## 1. Setting

Cells are the closed boxes of the grid (uniform, or the two-level grid of #749 section 7.1), and edges and faces are
cells of lower dimension. The grid has cell size h with h <= |d| / 2, as in A1; for a coarse cell of the two-level
grid, h below means its own (coarse) size. A cell (box, face or edge) X is called **clear** if X is disjoint from M,
i.e.

    inf over X of u  >  0   (the whole closed cell, not only its corners or centre).

This matters beyond the interior: the generalized gradient at a point of the boundary of X is a limit of gradients at
nearby points, some outside X, and Lemma 2 needs those points off M too (the open set where the formula holds must
contain X). It is checked by an A1 interval bound: uLo(c) - r(X) > 0, with uLo(c) the lower bound of u at a point c
and r(X) an upper bound of the distance from c to every point of X (to its exact corners; X is convex).

### 1.1 Premises from A1 (proved there, used here)

Every number of the certificate other than the exact signs of section 3 comes from the A1 bounds (`gs.MeshOffset`,
#799; design note [MESH_OFFSET.md](MESH_OFFSET.md) section 10, code `MeshOffset.signed_distance_bounds`):

- (P1) **uLo(c) <= dist(c, M) <= uHi(c)** for a double point c: uHi is the distance to an explicit point of the nearest
  triangle (`triangle_upper`), uLo the minimum of the support bounds over a superset of the triangles that can be that
  close (`triangle_lower`); both in `ExactInterval` arithmetic (one ulp per operation). Tests:
  `DistanceBounds_EncloseTheDistance_Tightly` and the mutants of A1 (a 1.2x lower bound, a 0.8x upper bound).
- (P2) **C(X) is a proven superset of {t : dist(X, t) <= U(X)}**: the tree traversal descends into every box whose
  lower distance bound (`box_lower_sq`: interval differences, Down-rounded squares, minus 8 ulps of the coordinates)
  is at most U(X)^2 rounded up, and the tree's boxes are padded by 2^-40 of the root's magnitude
  (`DMeshAABBTree3.ComputeBoxPadding`) against the rounding of their centre / extent form. For a cell X (not a point)
  A2 runs the same traversal, with the same rounding rules, on the distance between the box of X and the tree's boxes;
  that variant is new code of A2 and gets the tests of P1's kind.
- (P3) **s(X), the side of the input**, from the exact crossing parity (`DMeshAABBTree3.IsInside`) at a point of X that
  is provably off M (P1), valid for clean closed M.

**Interval bounds versus exact signs.** (S0), "clear", the separating segment of (S2) and the proofs that a cell is
zero-free (section 4) are A1 interval bounds, not exact signs: they are proofs, but their precision (how close to the
level set or to M they can decide) is that of A1, a premise of the certificate. A bound that cannot decide makes the
cell uncertified (refined, then listed), never certified. Only the direction tests of section 3 are exact signs.

**Lemma 0.** Every cell that meets L is clear. Every point of such a cell has u > |d| - diam(X) >= |d| - sqrt(3) h > 0.

*Proof.* At a point y of L, sd(y) = d, so u(y) = |d|. u is 1-Lipschitz (a distance function), so every point of a
cell through y has u >= |d| - diam(X). Since h <= |d| / 2, diam(X) <= sqrt(3) h < |d|. ∎

Lemma 0 is only an argument for why the certificate can pass. The certificate itself does not assume it: it tests
"clear" for every cell it certifies.

On a clear connected X, sd has one sign s(X) in {+1, -1}, because sd changes sign only across M. So F = d - s u on X,
and F is 1-Lipschitz (locally Lipschitz, as Theorem 5 requires).

## 2. The enclosure

### 2.1 The generalized gradient of u off M

**Lemma 1.** Let t be a compact convex set and x not in t. Then u_t is continuously differentiable near x, with
gradient n_t(x) = (x - p_t(x)) / u_t(x), where p_t(x) is the unique nearest point of t. This is a unit vector.

*Proof.* Standard (the squared distance to a closed convex set is C1 with gradient 2 (x - p_t(x)); for example
Hiriart-Urruty & Lemaréchal, *Fundamentals of Convex Analysis*, 2001 [m]). u_t = sqrt of it is C1 where it is
positive. ∎

**Lemma 2 (min of C1 functions).** For x not on M, let A(x) = {t in T : u_t(x) = u(x)} be the active triangles. Then

    ∂u(x)  ⊆  co { n_t(x) : t in A(x) }.

*Proof.* u = -max over t of (-u_t). Near x, every u_t is C1 (Lemma 1), hence regular. For a finite max of
locally Lipschitz functions, Clarke gives ∂(max g_i)(x) ⊆ co{∂g_i(x) : i active} (Prop. 2.3.12 [m]), and ∂(-g) = -∂g
(Prop. 2.3.1 [m]). For C1 functions, ∂g_i(x) = {∇g_i(x)}. A triangle with u_t(x) > u(x) is not active near x by
continuity, so the inactive ones do not matter. ∎

Only the inclusion is used. Equality, which holds for regular functions, is not needed.

### 2.2 An enclosure over a cell

Let X be a clear cell, and let U(X) be a proven upper bound of u over X. In A1 terms, U(X) = uHi(c) + r(X): the
interval upper bound at c plus the radius.

**Candidates.** Let C(X) be the triangles t with dist(X, t) <= U(X). Every superset of C(X) can serve instead, for
example the triangles found by the A1 tree traversal with box lower bounds. The traversal is conservative: it pads
by 2^-40 of the root and adds 8 ulps.

**Lemma 3.** For every z in X: A(z) ⊆ C(X).

*Proof.* If t is active at z, then dist(X, t) <= u_t(z) = u(z) <= U(X). ∎

**Directions.** For a candidate t, let Q_t(X) = X - t = {z - p : z in X, p in t}. It is the Minkowski difference of
two convex polytopes, hence a convex polytope. Its vertices are among the differences of vertices: box corners minus
triangle corners, 8 x 3 = 24 points for a box, fewer for a face or an edge. Every point of Q_t(X) has length at least
dist(X, t) >= inf over X of u_t >= inf over X of u > 0, since X is clear. So 0 is not in Q_t(X).

Let D(X) be the union over t in C(X) of the unit vectors q / |q|, for q in Q_t(X).

**Theorem E (the enclosure).** Let X be a clear cell. Then for every z in X:

    ∂F(z)  ⊆  G(X)  :=  -s(X) · co D(X).

G(X) is compact and convex.

*Proof.* On X, F = d - s u, so ∂F(z) = -s ∂u(z): Clarke's sum rule with a constant, and ∂(λ g) = λ ∂g [m].
By Lemma 2, ∂u(z) ⊆ co{n_t(z) : t in A(z)}. By Lemma 3, every such t is in C(X). For each one,
n_t(z) = (z - p_t(z)) / |z - p_t(z)| with z in X and p_t(z) in t, so z - p_t(z) is in Q_t(X) and n_t(z) is in D(X).
Hence co{n_t(z)} ⊆ co D(X).

D(X) is compact: it is the image of the compact set made of the finitely many Q_t(X) under q -> q / |q|, which is
continuous away from 0. The convex hull of a compact set in R^3 is compact (Carathéodory). ∎

Theorem 5 of the #749 note also asks for the three parts of its interface requirement (i)-(iii):
- (i) the active pieces of a min are decided from enclosures over the box, never from a point value. Lemma 3 does
  this: C(X) comes from bounds over all of X.
- (ii) no non-Lipschitz operator. sqrt appears only in the value bounds, which are applied at positive arguments
  (clear cells), not in G.
- (iii) the sign tests are made on outward-rounded or exact bounds. Section 3 makes them exact.

## 3. The cell tests as finitely many exact signs

For a vector a and a cell X:

    a · g > 0 for all g in co D(X)
    ⇔  a · g > 0 for all g in D(X)                              (a linear function on a convex hull)
    ⇔  a · q > 0 for all q in Q_t(X), for all t in C(X)          (q / |q| is a positive multiple of q)
    ⇔  a · (z_i - p_j) > 0 for every corner z_i of X, corner p_j of t, and t in C(X)
                                                                 (a linear function on a polytope; its vertices
                                                                 are among the z_i - p_j)

The same holds with "< 0". With the sign -s(X) of Theorem E, "a · g has one sign on G(X)" is exactly this test.

- **(S1)**, for an edge of direction e_k (an axis). a · (z_i - p_j) = z_ik - p_jk, so the test is min over the edge of
  coordinate k > max over the candidates' vertices of coordinate k, or the same with < and min / max swapped. These
  are comparisons of doubles, exact. Only the edge's end coordinates and the input's vertices enter: no constructed
  value.
- **(S2)**, for a face with axes e_k, e_l: the same comparison, for one of the two axes.
  On a face with four crossings, it also needs a segment in the face that separates the crossings two and two and on
  which F has no zero. This is checked with the value bounds of A1 on the segment: interval bounds of u at points of
  the segment, together with the Lipschitz bound, by subdivision.
- **(S3)**: a direction d with d · (z_i - p_j) > 0 for all corners and candidates. (S3) asks for d · g > 0 for all g in
  G(X) = -s co D(X); if instead -s d passes the test, that is d · g < 0 on G, and -d is a valid direction for (S3)
  as well (Theorem 4 needs only some direction along which L is a graph), so the test is run for d and the sign is
  not a condition. d is chosen in double (eg the
  normalized sum of n_t(c) over the candidates, or the mean direction from the nearest points). Each sign is that of
  sum over k of d_k (z_ik - p_jk), a dot product of doubles with differences of doubles. It is decided exactly by an
  interval filter plus the exact dyadic stage (ExactDyadic). This is the only place where exact arithmetic beyond
  comparisons is needed, and its code belongs to the exact-code rules (no FMA, no float).
  (S3) also needs either one circle of L on the cell's boundary, or separating surfaces. The circles are those of the
  certified pattern on the faces. With more than one, the cell is refined (A2 does not test separating surfaces).
- **(S0)**: F has no zero at the node. This holds if |d| < uLo(node) or |d| > uHi(node), with the interval bounds of
  A1 and the sign s from the exact parity (P1, P3): an interval bound, not an exact sign (section 1.1).

So **every test of the certificate is a comparison of doubles, a sign of an exact dot product, or an interval bound
of A1** (P1-P3). No error constant is derived by hand.

The candidate set C(X) may be larger than needed. That makes the tests harder to pass, never wrong.

## 4. What it proves for `MeshOffset`

**Theorem O.** Let D be the **raw dual contouring output** of `MeshOffset` (any later repair, collapse, flip or
simplification of D voids (1) and (2) below; the band certificate of A1 alone survives such edits, since it is re-run
on the final mesh). Assume:

- (H) the mesh-side hypotheses (H1)-(H4) of Theorem 2 of the #749 note (section 7.3: cell vertices strictly inside
  their cells, face and edge vertices of option B strictly inside their faces and edges, no self-intersection,
  manifold), **and option B's implementation facts (i)-(iii) of #749 section 7.3**, exactly as #749 states them (they are not
  restated here, so that the two texts cannot drift apart);
- (N) D's node signs are the true signs of F at the nodes, proved by (S0);
- (P) the face rule D used on every face with four sign-changing edges agrees with the pairing (S2)'s separating
  segment certifies for L. The face rule is read as data from the A1 contouring (the hook of #749 section 7.6 item 1:
  `DualContouring` exposes its face decisions; A2 adds that accessor, internal, if A1 does not have it);
- (Z) **every cell of the grid** is one of: certified ((S0)-(S3) with G of Theorem E, and clear); or proven zero-free
  by an A1 enclosure that excludes 0 (|d| < uLo(c) - r(X) or |d| > uHi(c) + r(X), with the side s(X) constant on X by
  P3, i.e. F has one sign on the whole cell); or **listed** (uncertified). This is Theorem 5's decidable form; A2
  proves the zero-free cells exactly so, cell by cell (most of the grid, far from L, passes it at once);
- the outer layer of cells is zero-free with F < 0 (the grid margin of A1 is 2h beyond the offset).

Then, if no cell is listed:

1. L is a closed surface, and D is ambient isotopic to L (Theorem 5 and Theorem 1 of #749).
2. Every point y of L is within diam(c) of D, for the cell c, fine or coarse, that contains y. With the band
   certificate of A1, the band is proven on both sides.

If cells are listed, the statement is Theorem 1b's partial one (#749 section 7.7): D and L agree outside the listed
cells in the sense stated there.

*Proof.* (1): the cells that L meets are certified (a zero-free cell does not meet L, and none is listed). Theorem 5
of #749 needs exactly an enclosure G(X) of the generalized gradients over each such X with the interface properties,
and (S0)-(S3). Theorem E and section 3 provide these. Theorem 2 of #749 with (H) makes D compatible with the pattern
its node signs and face rule define. (N) and (P) make that pattern equal to L's: the same sign-changing edges by (N)
and (E), and the same pairing on 4-crossing faces by (P). Theorem 1 then gives the isotopy.

(2): y lies in a cell c that L meets, which is certified. By (C), L ∩ c consists of disks whose boundary circles run
over sign-changing edges of c. So c has a sign-changing edge, and D has a vertex of c's patch inside c (H1). Hence
dist(y, D) <= diam(c). ∎

What Theorem O does not need: anything about the triangle quality (issue #800), or that the input has a feature size.

## 5. Where it cannot pass, and why that is right

- **Sheets that touch.** Where two parts of S are exactly 2|d| apart, two offset sheets touch, and the nearest points
  of a point of L lie in opposite directions. D(X) then contains antipodal vectors for every X there, so no direction
  d passes (S3) at any refinement. L is not a surface there, so a certificate must fail. The cells are reported.
  Near such a spot, the closer the sheets, the deeper the refinement. The refinement limit turns that into a report.
- **Concave offset creases.** Where two sheets meet at an angle, the generalized gradient at the crease is the hull of
  the two sheet normals n1 and n2 (Lemma 2 with two active triangles, or more on a corner).
  - (S3) passes if n1 and n2 are not antipodal (d = n1 + n2).
  - (S1) on an edge passes once the edge is far enough from the crease that only one sheet's triangles are candidates.
    A crease curve generically misses the grid edges (a curve and a line in R^3), so refinement gets there. The cost
    is a refinement zone of width about the finest cell along the crease.
  - (S2) on a face the crease crosses (a curve and a plane meet generically) needs an axis e_k of the face with
    e_k · n1 and e_k · n2 of one sign. **That can fail for every refinement**, for example n1 = (1, -0.1, 0) and
    n2 = (-0.1, 1, 0) on a z-face: the projections of the normals into the face are 101° apart, so neither axis has
    one sign.
  
  This is a limit of the axis form of (S2), not of the offset. Section 6 proposes the relaxation.
- **Concave corners of the offset (three or more sheets).** Where three or more sheets meet (eg the offset of a
  re-entrant corner of a box, the octant), the active directions at the corner point can surround the face's normal
  so that no in-plane direction has one sign on all of their projections: (S2') fails there for every refinement, and
  (F) can be genuinely false in the corner's face (L can meet the face in a set that is not a union of graph arcs
  over one line). These cells are listed, next to the touching sheets. Planned: a measurement of how many corner cells
  remain listed on the procedural CAD solids (octant, slot) at the refinement limit, in the A2 spike.
- **Clear fails** only within sqrt(3) h of M (h the cell's own size). By Lemma 0 no cell that L meets is there.

## 6. The relaxation (S2') of (S2) (accepted)

**(S2')**: on a face, either F has no zero on the face, or there is a direction a **in the plane of the face** (not
necessarily an axis) with a · g of one sign on G(face). The four-crossing separation condition is unchanged.
**a lies exactly in the face plane**: its component along the face's axis normal is set to exactly 0 in double (the
other two components are any doubles), so a · n = 0 holds exactly, as the proof needs.

Claim: Theorem 5's proof holds with (S2') in place of (S2).
- Lemma 5.1 of #749 is already stated for any direction u and convex U.
- In Lemma 5.2, inside a face, the proof builds u = -s a - eps n and u' = s a - eps n from the axis a of (S2) and the
  face's outward normal n. It uses only a · n = 0 and the sign of a · g, both of which hold for an in-plane a.
- In (F), L in the face is then a graph over the line of the face orthogonal to a, instead of over "the other axis".
  That is again a 1-manifold without closed curves.
- At an edge point, Lemma 5.2 uses (S1), not (S2), so nothing changes there.

Gain, stated correctly: (S2') needs the projections into the face plane of **all** candidate directions (all of
D(face)) to lie in an open half-plane. At a crease with two sheets that fails only where the two projections are
antiparallel, a non-generic case, so creases certify after refinement (the candidates of other sheets drop out). At a
concave corner where three or more sheets meet, the projections can surround the origin and (S2') fails for every
refinement; (F) can be false there (section 5). Corner cells are listed. With section 3, the test is again a set of exact dot-product signs, a · (z_i - p_j)
with a in double, decided exactly.

**Accepted by the review (2026-11-13).** It changes the reviewed text of #749 section 7.8 in three sentences; the exact
wording is in section 10, for the #749 note.

## 7. A2 test plan (repo style)

- **Never certified when wrong.** Truth from the exact polytope sandwich (option C of MESH_OFFSET.md, Nightly tier,
  small inputs), and from analytic offsets (boxes, the L-shape, the hollow cube, two boxes at gaps below, at and above
  2|d|: the touching case must be reported, never certified).
- **Mutants.**
  - Drop a candidate from C(X), for example by using U = uHi(c) without r: a crease cell must then be certified wrongly
    on a constructed case, and the test catches it.
  - Use the gradient at the centre only, instead of the box-to-triangle differences.
  - Compare with >= instead of > in (S1).
  - Use the face rule of DC without the pairing check.
  - Skip (S0) at a node placed on L (a grid shift that puts a node on a box's offset plane).
  - In (S2'), an a with a nonzero component along the face normal (a constructed face where that a passes but no
    in-plane one does, so the cell must stay uncertified).
- **Invariance** over `Similarity.All()`. The 2^k scales give the same certified cells bit for bit: the tests are
  comparisons and exact signs on scaled doubles.
- **Reuse; determinism** (parallel equals serial; the refinement order is deterministic).
- **Certified share**: measured on the procedural set and the lever-6 clean meshes; reported, not gated, until stage 2's
  numbers exist.

## 8. Cost

Per cell, one candidate query (already done for the band certificate) and, per candidate, 24 comparisons per axis.
(S3) adds 24 exact dot-product signs per candidate, nearly all decided by the interval filter.

Upper bound: the number of candidates per cell is that of the band certificate's traversal, typically a handful of
triangles. Estimate: the same order as the band certificate, which is about half of MeshOffset's time today. A2 then
costs 1.3-2x of A1 before refinement. Not measured: the A2 spike measures the share of the tests first (AGENT_WORKFLOW
section 3, upper bound before a spike).

## 9. Questions for the reviewer, and the answers (review 2026-11-13)

1. Lemma 2 (the inclusion from Clarke's max rule, inactive pieces dropped by continuity): **yes**.
2. No enclosure beyond X is needed: **confirmed**. Upper semicontinuity of ∂F at the boundary point and the
   1-Lipschitz bound suffice (Lemma 5.2 of #749).
3. The reduction to corners for faces (4 corners) and edges (2): **yes** (the Minkowski difference of a segment or a
   rectangle and a triangle is the hull of the corner differences).
4. The hypotheses on the face rule and the node signs make D's pattern equal to L's: **yes**, with (H) extended by
   option B's facts (i)-(iii) (Theorem O as now stated).
5. (S2') keeps Theorem 5's proof: **accepted**, with a exactly in the face plane (section 6).
6. The second band side: **diam(c)** with c the cell, fine or coarse, that contains y.

## 10. The wording change for #749 section 7.8 (for the contouring-certificate note)

`docs/design/CONTOURING_TOPOLOGY.md` is not on master yet (it is on the #749 branch, where the contouring-certificate
session works). The three sentences to change, so that the reviewed text and A2 match, minimal:

- Lemma 5.2, interior of a face. Now: "If p is in the interior of a face with outward normal n, let a be the axis of
  (S2) and s that sign; take u = -s a - eps n and u' = s a - eps n." New: "If p is in the interior of a face with
  outward normal n, let a be the direction of (S2) (an axis, or any direction with a . n = 0, (S2')) and s that sign;
  take u = -s a - eps n and u' = s a - eps n (a . n = 0)."
- Lemma 5.2, the arc sentence. Now: "In a face: by (S2) a graph over the other axis." and "(along the monotone axis of
  (S2) in a face, ...)". New: "In a face: by (S2) a graph over the line of the face orthogonal to a." and "(along a in
  a face, ...)".
- Theorem 5, (F). Now: "(F): by (S2) and Lemma 5.1 in the plane of the face, L in the face is a graph over an axis".
  New: "(F): by (S2) and Lemma 5.1 in the plane of the face (Lebourg along a), L in the face is a graph over the line of
  the face orthogonal to a".
