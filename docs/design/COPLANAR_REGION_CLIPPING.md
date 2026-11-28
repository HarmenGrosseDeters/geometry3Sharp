# Coplanar region clipping (option E'): O(n) pieces for overlaid coplanar fans, #1630

Status: approved by the owner on 2026-11-27; stage 0 measured (a scratch spike, not merged) and stage 1 (region identification, `BuildRegions`, no behaviour change) built (section 8). Refs #1630. It changes the output
triangulation of every boolean whose inputs have coplanar clusters (section 5). The stack walk of
[COPLANAR_PROPAGATION.md](COPLANAR_PROPAGATION.md) (option A) is built and is the base of this design.
[COPLANAR_FAN_DIAGNOSIS.md](COPLANAR_FAN_DIAGNOSIS.md) has the earlier measurements.

## 1. Where the time is after the walk

Case S: a capped cylinder (ring points on the rational circle) and its copy shifted by 2 ulps of the x extent, `ExactMeshBoolean`,
`Union`, defaults. Release, net10.0, 4 cores, one run each, milliseconds.

| ring (+ centre) | input tris | pieces | total | intersections | merge | triangulation | classify | of it stacks / winding | assembly | of it simplify | rounding |
|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|
| 121 | 480 | 29,808 | 857 | 73 | 92 | 198 | 201 | 104 / 89 | 269 | 238 | 19 |
| 241 | 960 | 117,168 | 4,604 | 210 | 527 | 1,088 | 1,141 | 281 / 830 | 1,584 | 1,463 | 51 |
| 481 | 1,920 | 464,688 | 27,529 | 989 | 3,203 | 7,298 | 6,575 | 1,375 / 5,100 | 9,348 | 8,785 | 113 |

From ring 241 to 481 the pieces grow x3.97 and the time x6.0 (winding x6.1, simplify x6.0, triangulation x6.7, merge x6.1): the
cost per piece also rises, by about x1.5 per doubling (triangulation 9.3 to 15.7 microseconds per piece, simplify 12.5 to 18.9).

## 2. What the other levers can give (upper bounds at ring 481, 27.5 s)

- **Winding regions (option B).** The winding stage is 5.1 s. With one ray per winding region (about one per cap source) it falls to
  0.1 to 0.3 s: at most -4.8 s, 22.6 s left. Needs the label proof of obligation 3 in COPLANAR_PROPAGATION.md.
- **Simplify.** 8.8 s, fully serial: `ExactPlanarSimplify.Run` sorts the candidate points, removes them one by one (ear clipping,
  exact predicates) and runs one Lawson flip queue; the file has no parallel loop. Switching it off is no bound:
  `SimplifyFlatRegions = false` gives 22.8 s in all, 227,418 output triangles and a rounding stage of 4.0 s instead of 0.1 s (the
  rounding then does the work). Batches of removals whose fans are disjoint, run in parallel with a fixed batch and flip order, would
  take 8.8 s to about 2.5 to 3 s on 4 cores: about -6 s, 21.5 s left.
- **Both:** about 16.7 s, below 20 s, the growth stays about x6 per doubling (3,840 input triangles would be about 100 s again).
  This is the fallback of section 8.

## 3. The probe that changes the growth

The pieces are Theta(n^2) because of the fan topology: every wedge of one cap is crossed by the radial edges of the other. The same
2-ulp union with other cap triangulations (same harness, same shift):

| cap | ring | pieces | union ms |
|---|---:|---:|---:|
| zigzag strip, no apex | 120 / 240 / 480 / 960 | 4,288 / 8,608 / 17,248 / 34,528 | 145 / 285 / 579 / 1,093 |
| none (open cylinders, `AllowOpenMeshes`) | 120 / 240 / 480 / 960 | 504 / 984 / 1,944 / 3,864 | 486 (JIT) / 102 / 246 / 379 |
| fan (today, with the walk; ring + 1 centre) | 120 / 240 / 480 | 29,808 / 117,168 / 464,688 | 857 / 4,604 / 27,529 |

The pieces and the time of the zigzag caps grow x2 per doubling. So if the overlay of the two caps had O(n) pieces, the union at
1,920 input triangles would take about 0.6 s, not 27.5 s, and the growth rate would be linear. That is the only lever that changes
the growth, and it is the content of this note.

## 4. The rule E'

Today a triangle T of mesh A is cut by the clipped edges of every coplanar triangle U of mesh B (`intersect_coplanar` /
`clip_into`): also by the edges that U shares with another triangle of B in the same plane, which are not a boundary of anything.

- **Region.** A maximal set of triangles of one mesh in one plane, connected through *flat edges*: an edge shared by exactly two
  triangles of that mesh that lie in the same exact plane and traverse the edge in opposite directions (consistent orientation; not "opposite normals"). The plane test is the exact
  integer plane that `ExactPlanarSimplify` and `CanonicalPlane` already compare. An edge whose two triangles traverse it in the same direction (a fin), with
  three or more triangles, or with one triangle is a boundary edge.
- **Rule.** Clip an edge of U into T only if the edge is a boundary edge of U's region. The flat edges of the other mesh no longer
  cut T. The edges of the triangles of T itself stay what they are: they are the pieces' units.
- **Effect on the repro.** The disc of mesh B has one boundary edge per ring segment; each fan wedge of A meets O(1) of them. The
  pieces of the cap are O(n), as in the zigzag probe. The pair loop still looks at the O(n^2) coplanar pairs (the intersections
  stage, 1.0 s at ring 481), but adds almost nothing for them.

## 5. What changes in the output and what stays

Stays: the surface as a set (the same points, the same orientation and sidedness); closed, manifold, the exact volume; every input
vertex; the result of every boolean without coplanar clusters, bit for bit (the rule never fires); the flat-region points that
`ExactPlanarSimplify` removes (the removal is by exact plane and keeps its meaning); the winding labels of the non-coplanar
segments.

Changes: for a boolean with a coplanar cluster, the cut lines of the other mesh's flat edges disappear from the pieces, so the
triangulation of that cluster in the result differs (fewer triangles before simplification; after it the retriangulation of the
region by ear clipping and Lawson flips can differ in the diagonals). Where the other mesh's flat edges met T's boundary, T gets no
split point, so fewer vertices on the shared edge; the mesh stays conforming because both neighbours skip the same edges.
Comparisons of results triangle by triangle, or by vertex count, against today's output change; comparisons by surface (exact
difference empty both ways) do not. That is the owner's decision.

## 6. Exactness obligations

1. **Region membership instead of one triangle.** The coplanar stack lists the triangles whose closed set contains the piece. With
   regions the stack member is the region, entered once: a piece can straddle two triangles U1, U2 of a region. Obligations:
   (a) a region has at most one member per piece (the triangles of a region overlap only on shared edges, since they are a surface
   in one plane with one orientation: no two interiors meet); (b) the centroid is in the closed region iff it is in one of its
   triangles. The walk of option A carries it: membership of a region changes only across a boundary edge of the region, and there
   one test against the triangle that carries the edge decides it (the other side of a boundary edge is outside the region
   locally). So the labels are the edge planes of boundary edges only, the re-test is today's `inside_coplanar`, and the list
   removes the region's entry (keyed by region) when its triangle says "outside". The seed scan tests the triangles of the candidate
   regions: O(|region|) per source, O(n^2) over a cap, as today's seeds.
2. **Completeness of the labels.** The proof of COPLANAR_PROPAGATION.md section 9 holds with "boundary of the region" in place of
   "boundary of u": the boundary of the region inside T is a union of clipped boundary edges, each a chain of constraint edges
   with its label; a piece interior or an open flat edge does not meet it.
3. **Which side supplies the output.** Today the piece is output from the lowest-numbered triangle of its stack. With regions the
   rank must be the region's (the lowest triangle index in it), and the pieces of the winning region's triangles then partition the
   overlap exactly once; a piece of a losing region is dropped. Without this a piece of T could be dropped for a triangle U1 whose
   own pieces do not cover T's part (they are cut by A's boundary only).
4. **Orientation and sidedness.** Members of a stack are used with their mesh and the orientation of their normal
   (`BooleanVolumeGradient`, the winding of the other orders of a stack). A region has one orientation by construction, so a region
   member gives the same vector as its triangles did.
5. **Conformity.** The decision "clip this edge of U" depends on U's edge and its region only, not on the receiving triangle:
   all triangles that share an edge of the receiving mesh skip the same cuts, so the pieces meet along shared edges.
6. **Exact proof tools.** No new arithmetic: exact planes, `Orient`, the existing predicates; one exact-code review and the
   incremental mutation run (exact shards), as for option A.

### Proofs to write before stage 2 is built (each goes into this note as a section, then to the review)

- **P1, membership.** For a region R of mesh B and a piece p of a triangle T of mesh A: the centroid of p is in the closed R iff it is in one triangle
  of R; and two pieces that share an edge e are on the same side of the boundary of R unless e carries a label of a boundary edge of R. From the
  clipped boundary edges being constraint chains (as COPLANAR_PROPAGATION.md section 9, steps 2 to 5), with "boundary of R" for "boundary of u".
- **P2, one entry per region.** The triangles of one region have disjoint interiors (a surface in one plane, one orientation), so a stack has at most one
  entry per region; a piece on a flat edge of R is in two triangles of R and one entry.
- **P3, the partition of an overlap.** For regions RA and RB that overlap, exactly one of the two supplies each point of RA intersect RB as output, the
  rank being the region's lowest triangle index; the pieces of the winning region's triangles are a partition of the overlap, because the pieces of
  one triangle are a partition of it and the triangles of a region partition it.
- **P4, equal surface.** The set covered by the output pieces with the rule E' equals the set covered today: both are the same decision per point
  of the plane (the stack is the same set of meshes and orientations at every point, only its members' granularity differs); the exact test is the
  difference of the two results being empty both ways, run on the cases of stage 2.
- **P5, no new bound.** Every decision is an exact orientation of input points or of arrangement points with the existing predicates; the proof lists
  the new call sites and confirms that no filter and no error bound is added; if a filter is added later it needs its own bound.

## 7. What it replaces and what it keeps

Keeps: the stack walk, the edge-plane labels and their aliases (now fewer: only boundary edges), the pair loop, the arrangement,
simplify, the certifying checker. Replaces: the "stack member = triangle" meaning and the "lowest triangle index decides" rule
(region membership and region rank); the clipping of flat edges in `intersect_coplanar`. Adds: region identification per mesh
(a union-find over the flat edges, once per input mesh, O(triangles)).

Library building blocks (searched first): `ExactPlanarSimplify` groups triangles by exact plane and orientation (the same key);
`ExactMeshArrangement.CanonicalPlane`, `Orient`, `clip_into`, `meets_only_at_shared`; the edge neighbour structure of `DMesh3`
(`GetTriNeighbourTris`) or the arrangement's own edge groups (`ExactMeshEdgeGroups`, a union-find with a deterministic root);
`ExactTriangulation2` (the exact CDT of the pieces); the coplanar walk of option A. The Clipper2 addon is a 2D polygon boolean on
integer coordinates: not usable for exact rational points. There is no code to take over.

## 8. Staged plan with a gate per stage

| stage | what | gate to go on |
|---|---|---|
| 0 | spike behind an internal flag: skip the clips of flat edges, nothing else (classification wrong); measure pieces and time on S, R and the box cases | pieces grow at most x2.3 per doubling and S at ring 481 takes at most 3 s (the zigzag probe says 0.6 s); else stop |
| 1 | region identification (exact planes, flat edges, union-find) with unit tests: box faces, fans, a fin, a non-manifold edge, two meshes | no change of any output when no region has more than one triangle: the existing exact suites bit for bit |
| 2 | stack members and rank by region; the walk with region keys; the output rule of obligation 3 | on S, R, plate and boxes, identical boxes: valid result (`MeshRobustRepair.NotCleanReason` empty), exact difference of old and new result empty both ways, the certifying checker certifies, invariance over `Similarity.All()`, reuse, determinism |
| 3 | measurement on the doubling series | S at ring 481 at most 3 s; the growth at most x2.5 per doubling; no regression on the cases without clusters |
| 4 | owner's decision on the default (flag on or off) and the release note: the triangulation of coplanar clusters differs | owner |

Stages 0 and 3 are measurements in a scratch run and in `benchmarks/`; each stage is a PR of its own, no stacked PRs.

**Fallback if the owner keeps the output.** Winding regions (option B) plus the parallel batches of simplify: about 16.7 s at ring
481 (an estimate on 4 cores), growth unchanged at about x6 per doubling, #1630 closed as a bounded constant factor. B needs the
obligation-3 proof of COPLANAR_PROPAGATION.md; the parallel simplify needs a bit-identical batch order and a review for the
allow-list.

### Stage 0 measured (2026-11-27, scratch spike, not merged)

The spike skips the constraint of every clip of a flat edge (two triangles of one mesh in the same exact plane, the edge traversed in opposite directions by the two
(consistent orientation, not an opposite normal), third corners strictly on opposite sides by the exact in-plane `Orient`); the pair is still clipped, so the coplanar relation is recorded. Stacks are not region aware
yet, so only pieces and time are meaningful. Union of a capped cylinder and a copy shifted by dx:

| case | ring | pieces before | pieces with the switch | Compute ms before / with |
| --- | --- | --- | --- | --- |
| S (2 ulps) | 121 | 29,808 | 2,184 | 930 / 771 |
| S | 241 | 117,168 | 4,344 (x1.99) | 6,185 / 555 |
| S | 481 | (27.5 s run) | 8,664 (x1.99) | 27,529 / 2,614 |
| R (0.25) | 61 | 6,064 | 1,128 | 204 / 165 |
| R | 121 | 22,480 | 2,232 (x1.98) | 948 / 319 |
| R | 241 | | 4,448 (x1.99) | / 847 |
| R | 481 | | 8,880 (x2.0) | / 2,236 |
| plate and 9 boxes | | 1,092 | 852 | 18 / 7 |

Gate (pieces at most x2.3 per doubling, ring 481 at most 3 s): met. The flat-edge test adds no predicate (exact `Orient` of input points as in
`meets_only_at_shared`). The spike was an internal switch with a wrong classification, so it is not merged; its patch is kept outside the repository.

### Stage 1 built (2026-11-27): region identification

`ExactMeshArrangement.RegionOf` (internal, in `mesh_ops/ExactMeshArrangementRegions.cs`, a partial of the arrangement; filled in `find_intersections` only
with the internal flag `BuildRegions`, null otherwise): per input triangle the lowest triangle index of its region, by a serial union-find over the flat
edges, a function of the triangles alone. Nothing uses it yet: the output is bit-identical with the flag on and off (tested). Unit tests: a grid in one plane
is one region and a bent grid two, a box has six regions of two triangles, two meshes never share a region, a neighbour that traverses the edge the same
way (a flipped one) or that folds over (same side) is not flat, a cylinder has two caps and one region per side quad, the regions are the same for every
power-of-two scale and when computed twice. (A non-manifold edge cannot occur: `DMesh3` does not hold one.) The file is in the determinism proof
scope and in the shard `stryker-mesh-arrangement-4`.

### Stage 2 built (2026-11-27): the rule behind `RegionClipping`

Internal switch `ExactMeshBoolean.RegionClipping` (off by default; `ExactMeshArrangement.RegionClipping` below it). With it on:

1. **Clips.** `intersect_coplanar` clips every edge of each triangle into the other as before and adds every clip as a segment. In `prepare_touched`, the segments
   that are clips of a flat edge of an *embedded* region become **ghosts**: not constraints (that is the whole saving), but their vertices are kept so that the
   triangles stay conforming: (a) a ghost's **crossings with the constraints** of the triangle (a proper crossing of the ghost and a constraint segment) are points of
   the triangle; (b) a ghost's **end on the interior of a constraint segment** is a point; (c) a ghost's end on a **side of the triangle that is not a flat edge of
   its own region** is a point (exact `Orient`). All three are decided by the constraint segment or the side, not by the triangle: every triangle that carries the same
   constraint segment (a boundary edge of a region of another layer, or the contact segment of a non-coplanar triangle) or the same side gets the same points from its
   own pair with the triangle that carries the flat edge. A first version (points only for (c), at clip time) was not conforming with three or more coplanar layers: a flat
   edge of C crossed a boundary edge f of B, B's triangle got the point on its side f, but the triangle of A that carries f as a constraint did not (review of #1725, 6
   inputs reproduced, now tests). Points on a flat side of the region are not added (both triangles of the region skip them alike; adding every end gave back the old
   29,808 pieces at ring 121). Crossings of two ghosts are not needed (neither is a constraint).
   **Embedded regions only.** A region is embedded, and its flat edges are ghosts, unless (a) two triangles of one mesh in one plane meet and are not neighbours: the pair
   loop marks both regions (`any` is true), and the pairs that share three corners; such a region overlaps itself or another region of its mesh (a self-overlapping prism:
   winding number 2 in the overlap). "Not neighbours" is the pair that `meets_only_at_shared` does not pre-decide, so it assumes `PreDecideCoplanar` (the default): with it
   off every pair of triangles that meet is clipped, and every region is marked not embedded (the old rule, as it should be). (b) The region contains a **culled**
   triangle (`CullOutsideA`, the default of Difference and Intersection): the boundary edges on a culled triangle are never paired, so a piece of another triangle could
   cross the region's real boundary and P1 would fail (found by the second review: certificate C2 on a Difference of three fine boxes). A region that is not embedded
   is clipped as before (no ghosts), counted per triangle in the stacks (`RegionKey` is the triangle), and its triangles keep points on every side (rule (c) above takes
   all sides of such a triangle, since its flat neighbour may be culled or marked alike). P1 and P2 below are for embedded regions; the others keep the old rule, so
   a `WindingPredicate` that reads exact winding numbers gives the same result as before (test).
2. **Stacks.** `coplanar_stack` is unchanged: it lists every triangle of another mesh that contains the piece's centroid, ascending, and the piece is output from
   the first. The members are triangles of other meshes, and all triangles of a mesh come before those of the next, so the region of the lowest rank is the
   lowest mesh's: the old rule and the rank by region are the same (a region is inside one mesh), and the code keeps the old one.
3. **One crossing per region.** A centroid on a flat edge of a region is in two triangles of it. The windings behind the piece add one crossing per region
   (`region_listed_before`); the ray of `front_crossings` skips every triangle in the list, as before.
4. The stack walk is not used (`RecordEdgeLines` and `walked_sources` are off with the switch): the pieces are linear, the scan is not cubic any more.

Proofs (P1 to P5 of section 6; "region" is as in section 4):

- **P1, membership.** (Embedded regions, none of whose triangles is culled; others are clipped as before.) Let R be a region of mesh B and p a piece of a triangle T of mesh A (coplanar). Every boundary edge of R meeting T is clipped into T (the
  pair of T with the triangle that carries the edge is a candidate pair, `any` is true when the clip is not empty, and the segment is added since the edge is
  not flat). So the interior of p, which is a face of the arrangement of T, meets no boundary edge of R; as it is connected, it is inside R or disjoint from the
  interior of R. The centroid is interior to p, hence in the closed R iff p is, and then it is in at least one triangle of R, which `coplanar_stack` finds
  (`inside_coplanar` is the closed containment, exact). A piece that is inside R but whose centroid is on a flat edge of R is in two triangles of R: P2.
- **P2, one entry per region.** The triangles of R are in one plane with consistent orientation and are joined through edges whose third corners are on opposite
  sides, so their interiors are disjoint; a point of the plane is in at most two triangles of R, both only on a flat edge. `region_listed_before` counts the first
  triangle of each region in the list only, so a stack contributes one crossing per region, the sign from the normal of the first triangle, which is the
  region's normal (same plane, same orientation).
- **P3, the partition of an overlap.** Two regions RA, RB of different meshes overlap in a set that both partition into pieces (the pieces of the triangles of RA
  cover RA, and likewise RB). By P1 each piece of RA inside RB has the stack [source, a triangle of RB]; the piece is output iff its source is lower than the
  member, i.e. iff mesh A is below mesh B. So the overlap is covered by the pieces of exactly one region (the lower mesh's), and the pieces of the other region
  inside it are copies that are not output; the pieces of either outside the overlap have no such member and are output. With several meshes the first triangle
  of the stack is the lowest mesh's, as before.
- **P4, equal surface.** The old rule outputs, for each point of the plane, the pieces of the lowest mesh covering it, with the crossings of every triangle that
  contains the piece's centroid (one per mesh and orientation at a point not on an edge); the new rule outputs the same sets (P3) and the same crossings (P1, P2).
  Both therefore classify every point of the plane alike; only the cut of the pieces differs. The test is exact: the difference of the two results is empty both
  ways (`ExactMeshBooleanRegionClippingTests`).
- **P5, no new predicate or bound.** The flat test (`flat_edge`) is exact `Orient` of input points (as `meets_only_at_shared`), plus the exact plane test
  `same_plane`; `on_boundary` is `Orient` of an arrangement point against input corners (the existing predicate, the same call as `clip_into` makes). No filter and no
  error bound is added.

Mutant of the one-crossing rule in `label_sheets` (the second dedupe site): equivalent because the site cannot be reached with two triangles of one region in a stack: a representative's source is the lowest stack entry and the stack holds no other triangle of its region (same-mesh coplanar entries exist only for regions that are not embedded, which are keyed per triangle). The first site is killed by a test with a predicate that reads the exact winding. The ghosts are not sorted (stage 3): `ghost_points` only adds points to a set that `touched_points` sorts, so the order of the ghosts (the pair loop's) changes nothing.

Checker: the certifying checker (`BooleanCertificate`) is extended for `RegionClipping`: its own regions (not embedded ones as singletons, found by an exact overlap test of the triangles of one mesh in a plane) (flat edge = two triangles of one mesh in one exact plane
with their third corners on opposite sides), the stack by region (a centroid on flat edges only is inside, on a boundary edge of a region is an error as before), and
C2's straddle test against the boundary edges of the regions only (an exact segment against the open piece, by the signs of the forms). It does not use the arrangement.

### Stage 3 measured (2026-11-27)

[benchmarks/CoplanarRegionClipping](../../benchmarks/CoplanarRegionClipping/README.md) has the doubling series (S and R, ring 61 to 961, old rule up to 241) and a case
without a coplanar cluster (13 ms against 12 ms, the same pieces and triangles). Against the gate of stage 3:

- **S at ring 481 at most 3 s: met**, 1.4 s warm (27.5 s before; 3.2 s on the first run of a process with the JIT). R: 0.8 s.
- **Growth at most x2.5 per doubling: NOT MET (stated plainly; decided 2026-11-27 to accept the measurement).** Pieces are x2.0, the time x2.6 / x2.9 / x3.8 on S (ring 241 to 961) and x3.0 / x4.4 / x3.2 on R. The cause is the
  pair stage: the intersecting coplanar pairs are x3.9 per doubling because the 2 ulp shift makes every thin wedge of one fan cross about half of the wedges of the
  other at the common centre (genuinely, not a box artefact), and each pair is clipped six times and recorded. Ring 961: 5.3 s (S).
- **No regression without clusters: met** (13 ms against 12 ms).
- **Upper bound for a linear pair stage** (open item, not built; roadmap/exact-boolean.md): a probe that clips only the pairs of neighbouring wedges (the results are invalid, only
  the time is meaningful) takes S ring 481 in 0.47 s instead of 1.40 s and ring 961 in 1.34 s instead of 5.27 s: the coplanar pair stage (pairs, clipping, recording, ghosts)
  is about 0.93 s of 1.40 s (two thirds) at ring 481 and 3.9 s of 5.3 s (three quarters) at ring 961; with it linear the time would grow x1.4 and x2.9 per doubling (the
  rest is the box search and the exact tests of the 351k and 1.39M candidate pairs, still quadratic in count). Option (1) of the PR, stacks by spatial query and no clipping of the
  pairs that cannot cut, is what it would build; decided 2026-11-27 not to build it now.
- One cost found on the way and removed: the sort of the ghosts in `prepare_touched` (the defensive sort of the second review round) was 600 ms of 1.7 s at ring 481 for
  no effect (`ghost_points` only adds points to a set that `touched_points` sorts), so it is gone; the equivalent mutant is the same: no sort of the ghosts at all.

### Stage 4: the owner's decision (2026-11-27) and its first PR, the corpus gate

The owner decided that `RegionClipping` becomes the default after a corpus run. First PR: the comparison, flag on against off, no default change. It is
`benchmarks/BooleanComparison` `region-compare` (rows from `RegionClippingCompare`), the test `RegionClippingCorpusTests` and the workflow
`.github/workflows/region-clipping-corpus.yml`; the gate per case is the same Status, the same `NotCleanReason`, the exact difference empty both ways, and the certificate
with the switch on; the time with and without is reported. Second PR, only if the gate is clean: the default switches to on (the flag stays as an opt-out), the pins and
hashes move with a reason each, and a release note says "the triangulation of coplanar clusters differs; same surface".

### Stage 4 part 2: the default switch (2026-11-28)

`ExactMeshBoolean.RegionClipping` is on by default (`internal`, the opt-out stays: `RegionClipping = false`). What moved with it:

- **Regions are built lean**: only for triangles that share a plane with another needed triangle (`rep` equality instead of the exact `same_plane`), so an input with no
  coplanar cluster pays for no BigInteger plane and no edge dictionary. This is the suspected source of the slower medians on small and mid-size corpus inputs
  (0-5k triangles 41 against 55 ms, 20k-100k 72 against 182 ms in the corpus run before the change); on the procedural inputs below it is gone (two crossing spheres without a
  coplanar face: x0.95-0.99). The corpus meshes are not available in the session, so the median of the corpus is to be read from the next corpus run (workflow
  `region-clipping-corpus.yml`), not from this note.
- **Two consumers of the stacks became region-aware** (found by the suite with the default on): the contact regions of `BooleanVolumeGradient` (a piece with its centroid on a
  flat edge listed two triangles of one region: one contact region of area 0.25 came out as two of 0.125) and the fast generalized winding number in front of a piece
  (`front_generalized_winding` counted the +-1/2 of a region twice: the intersection of two coplanar boxes through `ForceGeneralizedWinding` was open with 15 boundary edges).
  Both are pinned by existing tests that fail on the old code of this branch (`GeneralizedWinding_MatchesExactForClosedMeshes` with the mutation: the dedup off, red).
- **Pins**: tests that compare the old rule's mechanics (coplanar walk, certificate fan and box tree tests, certificate file round trip: the file format has no regions) set
  `RegionClipping = false` explicitly; `GeneralizedWinding_MatchesExactForClosedMeshes` compares triangle counts with the old rule (the exact path culls, the generalized does not,
  and the triangulations can then differ) and the volumes with the default.
- **Correction (review of #1744)**: an earlier text of this PR said the flush-box intersection goes from 12 to 21 triangles. The 21 was the open result of the
  winding bug above; with the fix it is 12 closed, equal to the old rule. Measured counts with regions are equal or lower (fine boxes 5 per edge, offset (0.3, 0.2): union 312
  against 324, intersection 160 against 172; 7 per edge, offset (0.3, 0.1): 596 against 676); `TheDefaultIsOn_AndShowsInTheTriangulationOfOffsetFineBoxes` pins the default with the
  first case. The walk is off with regions, so a larger count in another case is not excluded.
- **Lean regions are incomplete, never unsound** (review of #1744): the plane test is `rep` equality, exact `same_plane` against a common representative, with the bucket of the
  rounded plane picking the candidates. Equal planes split by `round_key` at a rounding boundary, or beyond `MaxPlaneCandidates = 8` planes in a bucket (which planes overflow
  depends on the triangle order), get no join: a triangle outside a candidate pair is a one-triangle region, exactly the old clip. A missed join costs speed (and can make the
  triangulation depend on the operand order), never exactness. A `same_plane` fallback where the reps differ would close the gap; it is a follow-up, not in this PR (the corpus run
  of this head stays valid).
- **Known limit**: a certificate file (`CertificateFile`) of a region-clipped result cannot be checked stand-alone; the in-process certificate checks it.

Cost of the switch on procedural inputs of the corpus's sizes (`dotnet run --project benchmarks/CoplanarRegionClipping -c Release`, 4 cores, median of 3, ms; phases of the last run):

| case | triangles | ms off | ms on | on / off | planes off / on | intersect off / on | merge off / on | classification off / on | assembly off / on |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| two spheres crossing (no coplanar face), 30 per edge | 20184 | 47 | 46 | x0.98 | 1 / 1 | 1 / 1 | 2 / 2 | 3 / 4 | 8 / 8 |
| two spheres crossing (no coplanar face), 60 per edge | 83544 | 148 | 141 | x0.95 | 1 / 2 | 2 / 2 | 8 / 7 | 11 / 9 | 25 / 33 |
| two spheres crossing (no coplanar face), 120 per edge | 339864 | 540 | 535 | x0.99 | 5 / 17 | 5 / 10 | 25 / 22 | 40 / 39 | 120 / 122 |
| two grid boxes, face contact, 30 per edge | 20184 | 302 | 110 | x0.36 | 2 / 4 | 26 / 31 | 17 / 6 | 159 / 20 | 9 / 9 |
| two grid boxes, face contact, 60 per edge | 83544 | 1453 | 540 | x0.37 | 13 / 16 | 184 / 147 | 82 / 27 | 690 / 177 | 39 / 29 |
| two grid boxes, face contact, 120 per edge | 339864 | 5700 | 2467 | x0.43 | 96 / 65 | 649 / 750 | 705 / 237 | 2509 / 611 | 191 / 173 |
| two grid boxes overlapping, faces flush, 30 per edge | 20184 | 461 | 163 | x0.35 | 5 / 12 | 33 / 57 | 20 / 6 | 170 / 19 | 69 / 15 |
| two grid boxes overlapping, faces flush, 60 per edge | 83544 | 1668 | 728 | x0.44 | 17 / 26 | 203 / 172 | 217 / 135 | 632 / 178 | 269 / 40 |
| two grid boxes overlapping, faces flush, 120 per edge | 339864 | 7094 | 3007 | x0.42 | 63 / 91 | 1270 / 1257 | 733 / 235 | 2299 / 561 | 1088 / 218 |
| two capped cylinders, shifted by 0.25 (fans) | 1920 | 1062 | 220 | x0.21 | 1 / 1 | 122 / 104 | 58 / 67 | 486 / 15 | 231 / 7 |

Without a coplanar cluster the switch costs nothing measurable (spheres, perpendicular cylinders: 11 against 10 ms); with face contact it is x0.2-0.45 of the old time, the saving
in the classification and the merge. Where the corpus run still shows slower medians, the planes phase (x1.3-3 in the table: the regions of the clustered inputs) is the first suspect.

## 9. Prior art (papers only; no code read)

- G. Bernstein, D. Fussell, *Fast, Exact, Linear Booleans*, Computer Graphics Forum 28(5), 1269-1278, 2009 (Eurographics SGP): exact BSP
  booleans on planar polygons.
- J. Nehring-Wirxel, P. Trettner, L. Kobbelt, *Fast Exact Booleans for Iterated CSG using Octree-Embedded BSPs*, Computer-Aided
  Design 135, 103015, 2021: plane-based exact geometry with integer arithmetic, a BSP in each octree cell.
- Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG 35(4), 2016 (SIGGRAPH): exact
  arrangement of triangle meshes, the base of this library's arrangement.
- G. Cherchi, M. Livesu, R. Scateni, M. Attene, *Fast and Robust Mesh Arrangements using Floating-Point Arithmetic*, ACM TOG 39(6), 2020
  (SIGGRAPH Asia): arrangement without computing intersection coordinates (indirect predicates).
- F. Martinez, A. J. Rueda, F. R. Feito, *A new algorithm for computing Boolean operations on polygons*, Computers & Geosciences
  35(6), 1177-1185, 2009: output-sensitive 2D polygon booleans in O((n + k) log n), n edges, k intersections: the size that E' gives
  the cap overlay.

The statement that polygon- or plane-based pipelines keep coplanar faces together while triangle-based arrangements (this library,
and the arrangement papers above) split triangle by triangle is the author's reading of the designs, not a claim of these papers
about coplanar fans; the numbers of sections 1 to 3 are this library's.
