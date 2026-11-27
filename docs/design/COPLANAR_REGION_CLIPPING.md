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
