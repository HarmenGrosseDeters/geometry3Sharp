# Coplanar propagation: stacks and winding numbers in one pass over the coplanar cells

Status: the stack walk (option A of COPLANAR_FAN_DIAGNOSIS.md) is built (2026-11-27, section 9); the winding regions (option B) are not. Design of 2026-11-25. Refs #1630. Measurements: the issue and its upper-bound comment; re-measurement, per-stage counts, upper bound and the choice of options: [COPLANAR_FAN_DIAGNOSIS.md](COPLANAR_FAN_DIAGNOSIS.md) (2026-11-27).

## 1. Problem

`ExactMeshBoolean` spends cubic time on overlays of coplanar fans. The repro is a capped cylinder (`CappedCylinderGenerator`,
radius 0.5, height 1, `Slices = n`) unioned with a copy shifted by 2 ulps of its x extent along x. The two cap fans overlay into
O(n^2) pieces. Almost all of them lie within a few ulps of the cap centre, below the resolution of double coordinates.

| slices | pieces | union ms | `classification.stacks` ms | `classification.winding` ms | `assembly.simplify` ms | patches |
|---:|---:|---:|---:|---:|---:|---:|
| 30 | 2,208 | 137 | 29 | 13 | 16 | 514 |
| 60 | 7,968 | 600 | 231 | 48 | 49 | 1,924 |
| 120 | 30,288 | 2,787 | 815 | 260 | 312 | 7,444 |
| 240 | 118,128 | 10,195 | 3,355 | 1,163 | 1,801 | |
| 480 | 466,608 | 55,357 | 23,311 | 5,423 | 10,476 | |

(Release, net10.0, 4 cores, one run each. At 30 and 60 slices the run follows a warm-up. The patches were counted in a later
run.)

Two stages grow by a factor of 8 per doubling, against 3.9 for the pieces:

- **Stacks** (`coplanar_stack`). Each piece runs the exact test `inside_coplanar` (4 exact orientations) against every triangle of
  `src.Coplanar`. There are about n/3 such triangles per piece, and every one really touches the source: switching the box filter
  off changes the count by less than 2% (159,696 against 162,368 tests at 60 slices). So no index of triangles or boxes can
  shrink the list; it saves at most the box scan, under 10% of the stage.
- **Winding** (`patch_winding`). Cap pieces with different stacks do not join into one patch, so there are about pieces/4
  patches. Each patch shoots an exact ray, which meets O(n) wedges from the cluster: 95, 213 and 453 plane sides per patch at
  30, 60 and 120 slices.

## 2. The idea: one walk per source triangle

The pieces of one input triangle `src` triangulate it. Both the stack and the front winding numbers are functions of the point
in `src`, and both change only across particular edges:

- **Stack.** stack(p) = [src] + [u in src.Coplanar (ascending) : the closed u contains centroid(p)]. Membership of a point in the
  closed u is constant on each connected part of the interior of src minus the boundary of u. So it changes only across an edge
  that lies on an edge of u (section 3).
- **Front winding.** For a mesh m, W_m(c) = lim W_m(c + eps n_src) as eps goes to 0+. This limit is locally constant off the
  segments where triangles that are not coplanar with src meet its plane. Isolated touching points do not matter: a path from
  centroid to centroid through the relative interior of an edge avoids the vertices. So the front winding numbers change only
  across an edge that lies on an intersection curve with a triangle not coplanar with src. They do not change at the boundary of
  a coplanar triangle, because the point in front is off the plane.

One walk over each source's pieces, breadth first over the shared edges inside src, carries both from a seed:

1. **Seed** (the first piece of src in the order of `arr.Output`): today's stack, the full exact test against all of
   src.Coplanar. Its front winding numbers come from today's ray.
2. **Across an edge e from p to q:**
   - D(e) is the set of coplanar triangles with an edge that contains e (section 4).
   - stack(q) is stack(p), with the members of D(e) re-tested exactly at centroid(q). Only the members of D(e) can change.
   - If e lies on no intersection curve with a non-coplanar triangle, W(q) = W(p): no ray. Otherwise q starts a new winding
     region, with today's ray.
3. **Decisions stay per piece.** keep and flip come from (W, stack) as today. Patches keep their meaning for the decision.
   What changes is that the winding is evaluated once per winding region, not once per patch.

The stack lists are assembled in ascending order of src.Coplanar, and the representative flag (no member below src) is
derived from them as today. So the stacks, and therefore every later decision, are the same lists, not only the same sets.

Cost on the repro:
- seeds: the sum of |src.Coplanar| over the sources, O(n^2) = O(pieces);
- re-tests: the sum of |D(e)| over the crossed edges, a few per edge, O(pieces);
- rays: one per winding region, about one per cap source, O(n) rays of O(n) sides, O(n^2).

This is the walk in a triangulation (Devillers, Pion, Teillaud 2002) used as a traversal rather than for point location. It is
also the propagation of labels across arrangement cells (Zhou, Grinspun, Zorin, Jacobson 2016), restricted to the coplanar
cells of one source. L2 (`trace_open_winding`, `l2_carried`) already carries open-mesh crossings from piece to piece within an
input triangle; the walk reuses its traversal order and adjacency where they fit.

## 3. The completeness assumption, and how to prove and check it

**(A) Every edge of every u in src.Coplanar, clipped to src, is a union of edges of pieces of src.**

Where it comes from in the code:
- `ExactMeshArrangement.intersect_coplanar` clips each edge k of U into T (`clip_into(T, U.Corners[k], U.Corners[k+1],
  4 * ui + 1 + k)`) and the other way round. Each clip becomes a constraint segment with its line label.
- Crossing segments are split at their exact crossing points (`prepare_touched`).
- The constrained triangulation of the source keeps every constraint as a chain of edges.
- One exception, the pairs that `PreDecideCoplanar` decides (`meets_only_at_shared`). There U meets T only in shared corners or a
  shared edge, so its clipped edges lie on T's boundary and the interior of T is outside U. No piece of src is in U, and no
  interior edge of src lies on U's boundary. The walk never re-tests U, and the seed's full test gives "not in U" once, which is
  right for every piece.

What the proof must establish:
1. Every segment added by `clip_into` survives into the output triangulation of src as a chain of edges, including the
   collinear overlaps and the shared constructions (`CanonicalPlane`). Write the proof from the code of `prepare_touched` and the
   triangulator before building.
2. Membership. Take two pieces p and q that share an edge e not contained in the boundary of u. By (A), the interiors of p and q
   and the relative interior of e do not meet the boundary of u. Their union is connected, and both centroids lie in it, so the
   centroids are both in the closed u or both outside it.
3. Winding labels (review of #1635). Every triangle v that is not coplanar with src, and whose intersection with src's plane is
   a segment inside src (its relative interior meeting the interior of src), leaves a label with `(line & 3) == 0` on every
   output edge of src along that segment. This must also hold where the pair is decided without clipping, in the touch-only and
   pre-decided paths, because those are exactly where a missing label would let a winding region run across a surface. Two
   cases matter:
   - **fold:** a neighbour of a coplanar triangle u that folds out of the plane at their shared edge, which lies inside src;
   - **fin:** a triangle that stands on src with one edge in src's interior and the rest on one side of the plane.
   Prove it from the pair loop (the non-coplanar branch and its early returns) before building. Where a path adds the segment
   with only a coplanar label, or with none, the walk must not rely on labels for the winding: either the path is changed to
   add the triangle-plane label, or e counts as an intersection edge whenever a `Partners` triangle of src contains it
   (an exact test per such edge).

How it is checked:
- **In tests:** the reference switch (section 6) compares every stack on the whole coplanar test set.
- **At run time, optional:** a debug count of re-tests whose result differs from the seed's propagation where D(e) is empty.
  It must be 0. It costs nothing when off. The same switch for obligation 3: where a winding region continues across e, shoot
  today's ray at q as well and count disagreements, which must be 0.

## 4. D(e): the line labels first, exact line keys as the independent check

**Labels (preferred, no arithmetic).** Every constraint segment carries `line = 4 * ui + 1 + k`. That is the edge plane of edge
k of coplanar triangle ui; `(line & 3) == 0` marks a triangle plane, which is a non-coplanar intersection. If each output edge of
src keeps the labels of all segments it lies on, then:
- D(e) = { ui : a label 4 ui + 1 + k on e };
- "e lies on an intersection curve with a non-coplanar triangle" means a label with `(line & 3) == 0` on e.

Two places can lose labels:
- collinear segments from different lines with the same end points, which are merged (see the comment "same ends from
  different lines" in `ExactMeshArrangement`);
- `CanonicalPlane`, which maps triangle-plane keys of equal planes to one representative.

The merge must keep the union of the labels, and the D(e) lookup must expand a canonical plane to its triangles. Both are small
changes to the arrangement's bookkeeping, not to its geometry.

**Exact line keys (independent check, and the fallback if a label path is not provably complete).** A line through two points
with homogeneous integer coordinates A = (a, a_w) and B = (b, b_w) has the Pluecker coordinates:

- d = a_w b - b_w a (direction);
- m = a x b (moment).

They are integer, defined up to a common factor and sign, and the same for any two distinct points of the line. Canonical key:
- divide (d, m) by the gcd of its six entries (BigInteger.GreatestCommonDivisor, exact);
- make the first nonzero entry of d positive.

Two segments are on one line exactly when their keys are equal, so this needs no tolerance and no filter. Cost:
- the input triangles' edges have integer corners: small keys, one per edge;
- an output edge's end points are rational intersection points: one gcd of numbers of a few hundred bits per edge, O(pieces) in
  all, a few microseconds each.

D(e) is then the triangles of src.Coplanar with an edge whose key equals e's key, looked up in a per-source dictionary from
key to the triangles in ascending order.

**Exact-code rules (`.editorconfig` exact section, `BannedSymbols.txt`).**
- Keys and re-tests are BigInteger and exact orientations; no FMA, no float.
- No filter is added. If a double filter is added for the re-tests later, it needs a written error bound and the mutation gate.
- No new parallel loop: the walk runs inside the existing per-piece `gParallel.ForRange` only if it can be split by source
  without sharing state. Otherwise it is serial per source, inside a loop over sources. A parallel loop over sources needs an
  entry in `core/ExactCodeAllowList.cs` and the review of section 8 of AGENT_WORKFLOW (no floating-point reduction; the walk
  has none).

## 5. Size threshold for the fallback

The walk costs adjacency building, BFS bookkeeping and the per-edge labels or keys. Today's scan costs |src.Coplanar| tests per
piece. Use the walk for a source only if:

- |src.Coplanar| >= C, and
- the number of pieces of src >= P.

Starting values: C = 16 and P = 8, set by the crossover on the cylinder series and on a grid of coplanar boxes. Expected
crossover: near 60 slices, about 20 coplanar triangles per piece. The threshold affects only the cost: both paths give the same
lists, which the identity test checks for C = 0, C = infinity and the chosen value.

## 6. Reference switch and test plan

- **Switch.** `internal bool CoplanarWalk = true` on `ExactMeshBoolean`. False runs today's `coplanar_stack` and per-patch
  winding unchanged. A second internal value forces the walk for every source (C = P = 0), so that small inputs exercise it.
- **Bit identity.** On the cylinder series (slices 30 and 60) and on the existing coplanar boolean tests
  (`ExactCoplanarPreDecisionTests`, `ExactTouchOnlyCoplanarTests`, `ExactArrangementCoplanarOrderTests`,
  `ExactMeshBooleanTestsStackFilter`, `CoplanarTriangleDifferentialTests`, and grids of coplanar boxes), run with the walk off,
  forced and at its threshold. Assert:
  - `StacksPerPiece` (`RecordStacks`) lists equal;
  - the representative flags equal;
  - `CertificateKeep` and `CertificateFlip` (`RecordCertificate`) equal;
  - the result's vertices (doubles, bit for bit) and triangles equal.
- **Fold and fin (obligation 3).** Procedural cases in which a winding region must stop at an edge without crossing triangles:
  - a fold: a box whose top face lies in the plane of a larger plate's top face, inside it, so that the box's side faces fold
    down out of the plane at the edges of its top face (with the box above, below and straddling the plate);
  - a fin: a thin wedge standing on the plate with its bottom edge in the plate's interior, as a separate mesh and as part of
    the plate's mesh;
  - each with union, intersection and difference, the walk forced; the same bit-identity assertions as above, and the winding
    regions must split at the fold and fin edges (a test count of the regions).
- **Growth.** `Counts["coplanar_tests"]` must grow by at most 4.5x per doubling on slices 30, 60 and 120. Today's code fails
  this: 7.2 and 7.6. Add a new count for the winding rays, which must grow by at most 4.5x as well.
- **The rules of CLAUDE.md:**
  - an invariance test over `Similarity.All()`;
  - a reuse test (Compute twice);
  - determinism (parallel against serial, repeated runs) if a parallel path is added;
  - the certifying checker certifies the cylinder series at 30 and 60 slices;
  - each test at most 5 s. Both paths run at 30 and 60 slices only (0.7 s together); the growth test runs only the walk at
    120 slices, which takes 2.8 s today and less with the walk.
- **Mutants.** The walk is exact code, so the incremental mutation run gates the merge. Hand-check that these are killed:
  - D(e) empty (the stacks are not re-tested);
  - a winding region continued across a non-coplanar label;
  - the seed not tested;
  - the lists not sorted.

## 7. Expected gain per stage (from the measurements)

| stage | today at 480 slices | with the walk | how |
|---|---:|---:|---|
| stacks | 23.3 s | about 0.5 to 1 s | O(pieces) re-tests instead of about n/3 per piece (74.7 M tests today) |
| winding | 5.4 s | about 0.1 s | O(n) winding regions instead of about pieces/4 patches |
| simplify | 10.5 s | 10.5 s | not addressed (x5.8 per doubling, in #1630) |
| triangulation, merge, rest | about 16 s | about 16 s | not addressed |
| **union** | **55.4 s** | **about 27 s** | |

- At 30 and 60 slices the gain is about 0 (the threshold keeps today's path).
- At 120 slices: about 1.0 of 2.8 s.
- The union at 480 slices stays above 20 s even with the walk, so #1630 needs a separate look at `assembly.simplify` and the
  triangulation of the cluster before it can be closed.
- The upper bounds above are cost models, not measurements. Before the build, a spike measures the walk's stacks stage alone
  (CLAUDE.md: an upper-bound check before a performance spike).

## 8. Sources

Papers only; no code read (docs/sources/exact-arithmetic-and-booleans.md):

- O. Devillers, S. Pion, M. Teillaud, *Walking in a triangulation*, IJFCS 13(2), 2002: traversal of a triangulation across
  shared edges.
- Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG 35(4), 2016: labels propagated
  across the cells of an arrangement.
- Pluecker line coordinates: a standard construction (for example J. Stolfi, *Oriented Projective Geometry*, Academic Press,
  1991).

## 9. As built: the stack walk (option A, 2026-11-27)

Built: the stacks only; the winding stays per patch. `ExactMeshBoolean.CoplanarWalk` (internal, default true) switches the walk on for the
sources with at least `CoplanarWalkMinCoplanar` = 16 coplanar triangles and `CoplanarWalkMinPieces` = 8 pieces; false runs the scan of
every piece as before (the reference of the tests); 0 and 0 walk every source with a coplanar triangle.

**Edge labels (section 4, the labels variant).** With `RecordEdgeLines` the arrangement keeps, for each input triangle with at least
`EdgeLinesMinCoplanar` coplanar triangles, a dictionary `EdgeLinesOf(ti)`: for the key of an output edge (`EdgeKey` of its point ids), the
coplanar triangles whose edge plane carries a constraint segment through that edge (ascending, without duplicates). Where it is filled:
- `prepare_touched` drops a segment whose ends equal those of a kept one (the common edge of two adjacent fan wedges, for example, comes
  with two lines). The dropped lines are kept per edge key (`Touch.Aliases`), so the union of the lines is not lost (the point
  section 4 asks for).
- `triangulate_touched` splits every segment at the points on it and inserts the parts as constraints. For each part it adds the segment's
  line and its aliases to the part's key. Only edge planes (`line & 3 != 0`) count: a line `4 * u` is the plane of a triangle that is not
  coplanar and is the winding's business (option B).
No new arithmetic: keys are integers, lists are built by insertion in a fixed order (the determinism proof scope allows no enumeration of a
dictionary and no library sort: `DeterministicApis.txt` has one new site line, `Interlocked.Add` of the test counter, and the ledger line
of DETERMINISM_PROOF.md 5.4 is regenerated).

**The walk (`coplanar_walk`).** The pieces of a source are consecutive in `Output`. The first one gets today's scan (`coplanar_stack`, with
the box filter), so its list is the scan's. The others are reached breadth first across the edges the pieces share (two pieces per edge
of one triangulation). From piece p across edge e to q:
- no recorded plane on e: the list of q is a copy of the list of p (and so its representative flag);
- else the exact centroid of q is built and each recorded triangle u (in `src.Coplanar`) is tested again with `inside_coplanar`; u is
  inserted into or removed from the ascending member list as the test says. The list is `[source, members...]` or null, as the scan builds
  it, and the flag is "no member below the source".
The walk runs per source inside a new `gParallel.ForRange` of `classify` (that member is on the allow list of G3E0002: each source writes its
own slice of `stack` and `representative`, the only shared value is the `Interlocked` counter of the exact tests).

**Completeness: why no stack member is missed.** Claim: for pieces p and q that share the edge e, and a coplanar triangle u of the source T that
has no recorded plane on e, the centroids of p and q are both in the closed u or both outside it.
1. A coplanar pair that `meets_only_at_shared` decides (`PreDecideCoplanar`) meets T only in shared corners or a shared edge: no piece
   centroid, which lies in T's interior, is in u, and the first piece's scan says so; u is never tested again and never recorded. The
   same function also returns true for three shared corners, a triangle u identical to T: every centroid is in u, the first piece's scan says
   inside and the copies keep it (nothing is recorded, so nothing changes it).
2. Every other pair is clipped (`intersect_coplanar`): each edge of u is clipped to T (`clip_into`) into a point or a segment of its
   edge plane; so the part of the boundary of u inside the closed T is a union of such points and segments.
3. Every segment is split at all points on it (the points whose boxes meet the segment's, tested exactly for collinearity: a point on the
   segment has its box meeting the segment's box, so none is missed) and each part is inserted as a constraint; constraints stay edges of
   the constrained triangulation; so each part is an edge of a piece, and the walk recorded the plane of u for that edge's key, from the
   segment's line or from an alias (item above) of a segment with the same ends. A recorded point (a tangential touch) is a vertex of the
   triangulation.
4. Suppose a point x in the relative interior of e is on the boundary of u. Then x is on a clipped segment (a point is a vertex, never in
   the relative interior of an edge); that segment is a chain of edges of the triangulation; the edge of the chain through x meets e in a point
   that is not a vertex, so it is e (two edges of one triangulation meet only in a common end). So u is recorded on e: contradiction.
   The interiors of p and q contain no point of an edge, so they do not meet the boundary of u either.
5. The set interior(p), relative interior(e), interior(q) is connected and does not meet the boundary of u; it lies in the interior of u or
   in its exterior as a whole. The centroids are in it: the same answer for both.
The dual graph of a triangulation of a triangle is connected across shared edges, so the breadth first order reaches every piece, and by
induction along the tree every list equals the scan's: the same membership, in the same ascending order, the same flag. The scan's box
filter is conservative (it skips only triangles that cannot contain the centroid), so it changes no scan result either. What can break
the argument is a label lost in 3 (a duplicate segment, or a part of a collinear overlap that is not split at the other segment's ends): the
test `ExactMeshBooleanCoplanarWalkTests` compares pieces, stacks, decisions and result of the walk (forced for every source) with the scan on
fans (a cylinder and its 2-ulp, 0.25 and 1e-9 shifts), on boxes with coplanar tops, under every similarity, for union, intersection and
difference. The optional run-time check of section 3 is not built.

**Measured** (the repro of COPLANAR_FAN_DIAGNOSIS.md, Release, net10.0, 4 cores, one run each; the counts are exact, the times noisy by 10 to 20 %). Case S (the cylinder and its 2-ulp shift), scan -> walk:

| ring | pieces | union ms | stacks ms | exact tests (per piece) | winding ms |
|---:|---:|---:|---:|---:|---:|
| 32 | 2,352 | 485 -> 141 | 59 -> 34 | 25,432 (10.8) -> 18,216 (7.7) | 38 -> 13 |
| 120 | 29,808 | 2,567 -> 1,170 | 879 -> 60 | 1,197,064 (40.2) -> 58,143 (2.0) | 312 -> 125 |
| 240 | 117,168 | 10,478 -> 6,584 | 4,457 -> 501 | 9,392,504 (80.2) -> 184,337 (1.6) | 1,208 -> 1,161 |
| 480 | 464,688 | 50,419 -> 27,531 | 23,060 -> 1,352 | 74,426,584 (160.2) -> 687,064 (1.5) | 4,846 -> 5,057 |

Case R (offset 0.25): union 351 -> 171 ms (ring 120), 1,369 -> 1,146 (240), 5,389 -> 4,835 (480); stacks 166 -> 26, 297 -> 196, 1,793 -> 742;
tests per piece 18.6 -> 1.8, 35.1 -> 1.3, 67.9 -> 1.2. The result is the scan's, bit for bit (the tests compare it at the smaller rings). The tests per
piece fall with the ring (the seed scans, one per source, are about 4 n^2 over the pieces): the growth is bounded, as the growth test asserts. What is left of the union
at ring 480 is the winding (5.1 s: the patches, option B), simplify (8.5 s) and the stages of section 7 that stay; the union is still above the 20 s rule
at 1,920 triangles (27.5 s), as the diagnosis predicted (about 25 s).

**Known limit.** The walk is serial inside a source (the breadth first order is a chain of dependencies), and the sources run in parallel. One huge walked source with few
others runs on one thread, where the scan it replaces parallelised per piece: it does far less work in all, but a source with a million pieces and nothing else could be slower in
wall time than the scan on many cores. At ring 480 the cap sources are many, so there is no loss. The edge planes and the aliases are released after the triangulation and
the stacks.
