# Patch certificate for the self-intersection checks (design, for approval)

Status: design only, no code. For the owner's approval before a prototype (AGENT_WORKFLOW §2). Scope: the
per-collapse check of `Reducer.PreventSelfIntersections` first; the Remesher's per-operation check afterwards (see
"Remesher").

## 1. Problem and upper bound

After #261 (the `TriangleTriangleFilter` in front of the exact tests) the checks spend their time on the number of
candidate pairs, not on single expensive tests (`benchmarks/SelfIntersectionBenchmark/README.md`, "Where the time
goes with the filter"): the Reducer tests about 10 new triangles against about 260 grid candidates each per collapse
(32 M pairs on `HarnessPlate(50, 0.5)`), the Remesher about 36 per query (15 M on Thingi10K 135363). Per pair the
filter, `IsDegenerate` and the bookkeeping each cost a share; no single item is worth a rewrite.

Upper bound for culling whole patches (instrumented build, counting only, not committed; the same benchmark cases, one
run): for every candidate pair that reaches the pair test, the angle between the unit normals, and whether the
candidate is reached from the collapse's new triangles through edge-adjacent candidates whose normals lie within
45 degrees of the query triangle's ("intra-patch"):

| case | pairs | angle < 10° | intra-patch (45°) |
|---|---:|---:|---:|
| Reducer `HarnessPlate(50, 0.5)` | 29.1 M | 100 % | 100 % |
| Reducer `HarnessTilt(HarnessPlate(50, 0.5))` | 4.6 M | 99.9 % | 97.8 % |
| Reducer `HarnessTilt(HarnessPlate(50))` | 10.1 M | 100 % | 100 % |
| Reducer `HarnessPlate(100)` | 0.93 M | 100 % | 99.8 % |
| Reducer jitter plate tilted | 7.7 M | 100 % | 100 % |
| Reducer box grid | 2.5 M | 84.1 % | 83.6 % |
| Reducer bumpy sphere | 0.45 M | 83.9 % | 100 % |
| Remesher 135363 (unoriented, see §7) | 13.5 M | 88.3 % | not measured |
| Remesher plates (unoriented) | 0.15 M | 100 % | not measured |

So nearly all pairs of the expensive Reducer cases lie inside one connected, nearly flat, consistently oriented patch
around the collapse. A certificate that such a patch cannot intersect itself replaces all its pair tests by one test
of the patch. Still optimistic: it ignores the contour condition (§3), which the dangerous cases fail on purpose.

## 2. Literature and library check ("Look before you build")

Literature (implemented from the papers only; no code read):
- P. Volino, N. Magnenat-Thalmann, *Efficient self-collision detection on smoothly discretized surface animations
  using geometrical shape regularity*, Computer Graphics Forum (Eurographics) 1994: a surface region cannot
  self-intersect if its normals lie in an open half-space of directions (a normal cone below 90 degrees) and the
  projection of its contour onto the plane orthogonal to the cone's axis does not self-intersect.
- S. C. Schvartzman, J. Pérez, M. A. Otaduy, *Star-contours for efficient hierarchical self-collision detection*,
  ACM TOG (SIGGRAPH) 2010: the contour test as a star-shapedness test against a point, linear in the contour length
  and hierarchical; the normal-cone test as before.
- S. Gumhold, P. Borodin, R. Klein, *Intersection free simplification*, IJSM 9(2), 2003: the setting of the Reducer
  (pair contraction with a spatial search, the test done lazily for the collapse about to be applied). It tests pairs;
  it has no patch culling.
- S. Curtis, R. Tamstorf, D. Manocha, *Fast collision detection for deformable models using representative-triangles*,
  I3D 2008, and M. Tang, S.-E. Yoon, D. Manocha, adjacency-based culling for continuous collision detection: avoiding
  the tests of adjacent features (our filter's shared-vertex paths do part of this).
- Not used: PaMO (Oh et al., CGF 2025, a parallel GPU intersection-free simplification), noted for the parallel item.

Library:
- `ExactPredicates.Orient2d` (exact, filtered): the only predicate the certificate needs (§5).
- `MeshNormals`, `DMesh3.GetTriNormal`: floating-point normals; used only to choose the projection axis, never to
  decide.
- `Polygon2d` (`IsClockwise`, `Contains`): floating-point, not usable for a certificate. The exact 2D segment tests of
  `ExactTriangleIntersection` are private; `ExactTriangulation2` is internal and builds triangulations, not contour
  tests. Nothing exact for "simple / star-shaped polygon" exists, so the contour test is new code (small, §5).
- `TriangleTriangleFilter` (#261): stays the pair test for the candidates outside the patch.

## 3. The certificate

Let P be a set of triangles of the mesh after the collapse (vertex ids, positions; §4 says which), and k an axis, π the
projection that drops coordinate k. **P is certified** if all of:

1. **Disk.** P is edge-connected, every edge of P is in one or two triangles of P, every vertex's triangles in P form
   one fan (vertex-manifold), every interior edge appears in opposite directions in its two triangles (consistently
   oriented), and V − E + F = 1 with exactly one boundary loop: P is a consistently oriented triangulated topological
   disk. Integer checks only. (Without the orientation condition two neighbours wound the same way both pass (2) and
   lie on the same side of their edge: a fold.)
2. **Orientation (normal cone around the axis k).** With the winding of the mesh, every triangle of P projects with
   the same strict orientation: `Orient2d(π a, π b, π c)` has the same sign s ≠ 0 for every triangle (a, b, c) of P.
3. **Contour (star-shaped, winding once).** For a point z (the projected position of the collapse's kept vertex, §4)
   and the boundary loop (q_0, …, q_{m-1}) of P in the direction its triangles induce: s · `Orient2d(z, q_i,
   q_{i+1})` > 0 for every i, and the loop crosses the ray from z in direction +x exactly once (counted exactly, §5).

**Claim.** A certified P has no self-intersection: two triangles of P meet exactly in their common vertices and edge
(as vertex ids), nowhere else. In particular every pair (A, C) in P passes `ExactTriangleIntersection.IntersectsBeyondShared`
with "no intersection", and pairs of different ids at the same position cannot occur.

**Proof sketch.** Let f be the piecewise linear map from the abstract disk |P| to the plane, f = π ∘ (the positions).
(2) makes f orientation preserving on each triangle; across an interior edge the two triangles, consistently wound,
lie on opposite sides of the projected edge, so f is a local homeomorphism away from the vertices, and at an interior
vertex it wraps the star w ≥ 1 times (angles positive). (3) makes f(∂P) a simple closed polygon around z (its polar
angle about z increases strictly and turns once in total), bounding a region D. By the degree of f (the number of
preimages of a point y not on f(∂P) and not the image of a vertex equals the winding number of f(∂P) about y), every
such y in D has exactly one preimage and every y outside D none. A vertex with w ≥ 2 would give points near it two
preimages, so w = 1 everywhere and f is injective on |P|. Since f factors through the 3D positions, these are injective
on |P| too: P is embedded. ∎ (Volino and Magnenat-Thalmann state the criterion for smooth regions; Schvartzman et al.
the star test. The disk condition (1) and the exact predicates are what make it a certificate for a triangle mesh: a
region with a hole or a handle can satisfy (2) and (3) and still overlap itself.)

The certificate only answers "no intersection inside P". It never claims an intersection: when any condition fails,
nothing is certified and the pair tests run as today.

## 4. Where, and on which triangles (Reducer)

In `collapse_creates_intersection(a, b, vKeep, newPos)`:

- **N:** the new triangles, the one-ring triangles of a and b with a, b replaced by vKeep at newPos, without the two
  removed by the collapse, with the mesh winding (the code keeps the index order, so the winding is kept). Degenerate
  ones are skipped today and are not in P (a degenerate triangle fails (2) anyway).
- **K:** the grid candidates of the triangles of N (as today: closed boxes, outside the old one-rings).
- **P:** N plus the triangles of K reached from N by edge adjacency in the mesh **after** the collapse, through
  triangles of K that pass (2). The adjacency after the collapse is known without changing the mesh: an edge of a
  triangle of N not incident to vKeep has, across it, the neighbour of the old ring triangle it came from; edges
  incident to vKeep are shared within N.
- **The point z:** π(newPos). It is an interior vertex of P when vKeep is not on the mesh boundary (the Reducer does
  not collapse boundary vertices by default); otherwise no certificate (pair tests).

(a) **The collapsed region overlapping itself after the move.** The certificate runs on the new state: N at the new
position, together with the unchanged triangles of K that it is connected to. If the move drags N over a part of the
same sheet, then one of: a triangle of N flips (fails (2)); or N lies over triangles of P with both oriented the same
way, so f is not injective, which by the claim means (1) or (3) fails; or the overlapped triangles are not in P, then
they are pair-tested (b). The two removed triangles are not in N, the ring triangles as they were before are not in K
(they are replaced by N), as in today's check.

(b) **Candidates outside the patch.** Every candidate C of a triangle A of N that is not in P (not reached, fails (2),
or the whole P fails) is tested against A with `TriangleTriangleFilter.IntersectsBeyondShared` as today. Pairs within
N are covered by the certificate when it succeeds, else tested as today. So the set of pairs the check decides is
unchanged; only the pairs inside a certified P skip their tests, and their exact answer is "no intersection". The
decisions, and so the results, stay bit-identical; the fingerprints of the benchmark check it.

## 5. Exact predicates and degenerate cases

- Orientation (2): `Orient2d` of the projected corners, exact. **Zero** (a triangle degenerate in the projection, eg
  a zero-area triangle, or a triangle containing the axis direction, including slivers whose floating-point normal is
  0): the triangle is not added to P (BFS) or, if it is in N, no certificate. The axis k is the dominant axis of the
  area-weighted floating-point normal of N; only the decision uses exact predicates.
- Contour (3): `Orient2d(z, q_i, q_{i+1})` exact; **zero** (z on the line of a contour edge, collinear contour
  points through z): no certificate. Collinear consecutive contour points not through z are allowed (strictly positive
  around z still holds). The ray count: an edge (p, q) crosses iff p.y < z.y ≤ q.y (half-open, exact comparisons of
  doubles; with all edges strictly counter-clockwise about z, a crossing from below to above lies on the +x side, so no
  further predicate is needed). Winding ≠ 1: no certificate.
- Disk (1): integer bookkeeping over vertex ids (an edge map of P, the boundary loop by following boundary edges).
  Two contour vertices with different ids but the same position make f non-injective; the claim covers it (the
  certificate then fails (2) or (3)).
- Non-finite coordinates: no certificate (as the filter, which returns Undecided).
- No new floating-point error bound: everything that decides is `Orient2d` and exact comparisons.

## 6. Cost, tests, measurement

Cost per collapse: building P is a BFS over at most the candidates already collected (one `Orient2d` per triangle,
hash lookups); the contour is O(boundary length) `Orient2d`. Compared with today's about 2600 pair tests of a
collapse at about 20 `Orient2d` each, the predicate calls drop by one to two orders of magnitude on flat regions. What
stays is the grid query and the candidate collection (about 15 % + 23 % of the Reducer's time on `HarnessPlate(50,
0.5)`), so the expected bound is about 2 to 2.5 times on the flat Reducer cases, less where few pairs are intra-patch
(the box grid: 84 %). To be measured with `SelfIntersectionBenchmark` (master vs prototype, alternating, minimum of 3,
fingerprints identical) before anything is merged.

Tests (beyond the CLAUDE.md set: `Similarity.All()`, reuse, results checked):
- the certificate against the exact pair tests on random patches (flat, tilted, curved, folded, with a hole, with a
  handle, with coincident vertices of different ids): never certified when some pair intersects, certified for the
  plain disks;
- mutations: drop (1), drop the winding count of (3), use ≥ instead of > in (2) or (3): each must fail a test (the hole
  and the handle cases, a doubly wound star, a flat fold);
- the Reducer: the existing `DefectFreeReduceTests` (flat ring with islands, coincident sheets, touching boxes) and
  the fingerprints of the benchmark unchanged.

Review: an independent review of the claim and of §5 (it is a new certificate, though it adds no error bound).

## 7. Remesher (no promise yet)

The Remesher's raw counts showed about 40 % of the pairs with normals more than 90 degrees apart, also on flat plates.
Cause found: `split_creates_intersection` and `flip_creates_intersection` build the hypothetical new triangles from the
edge's vertex order (a, b) from `DMesh3.GetEdge`, not from the winding of t0 / t1, so about half of them are wound the
other way. The pair test does not depend on orientation, so this is no bug. With unoriented normals the share within
10 degrees is 100 % on the plates and 88.3 % on 135363. For a certificate the new triangles must be wound as the mesh
will wind them (from t0, t1). The Remesher's pairs share a vertex more often (90 to 97 % on the plates, 32 % on
135363), and the operations are smaller (about 36 candidates per query), so the gain there is smaller and needs its own
bound; it is a later step, after the Reducer.

## 8. Open questions for the owner

- Approve the prototype for the Reducer (§4 to §6)?
- The star test may fail on patches that are simple but not star-shaped about z (long thin patches around a sliver).
  Then the pair tests run; a general simple-polygon test (O(m log m)) is possible later if the measured share of such
  patches is large.
