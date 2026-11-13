# The distance-field enclosure for the offset topology certificate (stage A2, proof first)

Status: **written proof, not reviewed.** It is for the fresh-session review the coordinator routes (decision 2 of
[MESH_OFFSET.md](MESH_OFFSET.md), section 9, 2026-11-13). A2 is built only after this review. No code.

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
cells of lower dimension. The grid has cell size h with h <= |d| / 2, as in A1. A cell (box, face or edge) X is called
**clear** if

    inf over X of u  >  0,

i.e. X does not meet M. This is checked by intervals: u(c) - r(X) > 0, with the A1 bounds of u at a point c of X and
r(X) an upper bound of the distance from c to the corners of X.

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
- **(S3)**: a direction d with d · (z_i - p_j) > 0 for all corners and candidates. d is chosen in double (eg the
  normalized sum of n_t(c) over the candidates, or the mean direction from the nearest points). Each sign is that of
  sum over k of d_k (z_ik - p_jk), a dot product of doubles with differences of doubles. It is decided exactly by an
  interval filter plus the exact dyadic stage (ExactDyadic). This is the only place where exact arithmetic beyond
  comparisons is needed, and its code belongs to the exact-code rules (no FMA, no float).
  (S3) also needs either one circle of L on the cell's boundary, or separating surfaces. The circles are those of the
  certified pattern on the faces. With more than one, the cell is refined (A2 does not test separating surfaces).
- **(S0)**: F has no zero at the node. This holds if |d| < uLo(node) or |d| > uHi(node), with the interval bounds of
  A1 and the sign s from the exact parity.

So **every test of the certificate is a comparison of doubles, a sign of an exact dot product, or an interval bound
of A1.** No error constant is derived by hand.

The candidate set C(X) may be larger than needed. That makes the tests harder to pass, never wrong.

## 4. What it proves for `MeshOffset`

**Theorem O.** Let the result D of `MeshOffset` satisfy the mesh-side hypotheses (H1)-(H4) of Theorem 2 of the #749
note (section 7.3: vertices strictly inside their cells, face and edge vertices of option B strictly inside their
faces and edges, no self-intersection, manifold).

Let D's node signs be the true signs of F, which (S0) proves. Let the face rule D used on every face with four
sign-changing edges agree with the pairing that (S2) certifies for L.

Let every cell that L meets be clear and certified ((S0)-(S3) with G of Theorem E), and let the outer layer of cells
have F < 0 proven (the grid margin of A1 is 2h beyond the offset).

Then:

1. L is a closed surface, and D is ambient isotopic to L (Theorem 5 and Theorem 1 of #749).
2. Every point of L is within diam(c) of D, for the cell c it lies in. With the band certificate of A1, the band is
   proven on both sides.

*Proof.* (1): Theorem 5 of #749 needs exactly an enclosure G(X) of the generalized gradients over each such X with
the interface properties, and (S0)-(S3). Theorem E and section 3 provide these. Theorem 2 of #749 makes D compatible
with the pattern its node signs and face rule define. The hypotheses here make that pattern equal to L's: the same
sign-changing edges by (S0) and (E), and the same pairing on 4-crossing faces. Theorem 1 then gives the isotopy.

(2): a point y of L lies in a cell c that L meets. By (C), L ∩ c consists of disks whose boundary circles run over
sign-changing edges of c. So c has a sign-changing edge, and D has a vertex of c's patch inside c (H1). Hence
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
- **Clear fails** only within sqrt(3) h of M. By Lemma 0 no cell that L meets is there.

## 6. Proposed relaxation of (S2) (for the review)

**(S2')**: on a face, either F has no zero on the face, or there is a direction a **in the plane of the face** (not
necessarily an axis) with a · g of one sign on G(face). The four-crossing separation condition is unchanged.

Claim: Theorem 5's proof holds with (S2') in place of (S2).
- Lemma 5.1 of #749 is already stated for any direction u and convex U.
- In Lemma 5.2, inside a face, the proof builds u = -s a - eps n and u' = s a - eps n from the axis a of (S2) and the
  face's outward normal n. It uses only a · n = 0 and the sign of a · g, both of which hold for an in-plane a.
- In (F), L in the face is then a graph over the line of the face orthogonal to a, instead of over "the other axis".
  That is again a 1-manifold without closed curves.
- At an edge point, Lemma 5.2 uses (S1), not (S2), so nothing changes there.

Gain: (S2') fails only where the projections of n1 and n2 into the face plane are antiparallel, a non-generic case,
so creases certify after refinement. With section 3, the test is again a set of exact dot-product signs, a · (z_i - p_j)
with a in double, decided exactly.

**This is the one step that changes a reviewed theorem. The review should accept or refuse it.** If it is refused,
A2 uses (S2) and reports the crease cells of section 5.

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

## 9. Questions for the reviewer

1. Lemma 2: is the inclusion ∂(min u_t) ⊆ co{∇u_t : active} correctly derived from Clarke's max rule, with the
   inactive pieces dropped by continuity?
2. Theorem E covers the generalized gradients at points of X only. As read here, #749's Lemma 5.2 needs no more: it
   takes the gradients at the boundary point p, which lie in G(face) or G(edge), and extends their sign to a ball
   around p by upper semicontinuity at p. Lemma 5.1 needs G(cell) on the cell. Please confirm that no enclosure beyond
   X is needed. If it is, compute C and the corners for X grown by a margin m > 0 (any m works).
3. Section 3: is the reduction to corners exact for the face and edge cases, where X has 4 or 2 corners?
4. Theorem O: are the hypotheses on the face rule and the node signs enough to make D's pattern equal to L's? (S0) and
   (E) give the edges, (S2)'s separation gives the pairing.
5. Section 6: does (S2') keep Theorem 5's proof?
6. The second band side (Theorem O (2)): is diam(c) right for the two-level grid with face vertices, or does the bound
   need the coarse cell?
