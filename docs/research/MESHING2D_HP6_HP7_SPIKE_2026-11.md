# 2D meshing: small input angles (HP 6) and a certifying oracle (HP 7), research spike (2026-11-22)

A research spike on two hard problems of the 2D mesher (plans #1282 and #1283; context #1262, #1263). No library code changed.
The prototypes and the raw numbers are in [benchmarks/Meshing2HardProblems](../../benchmarks/Meshing2HardProblems/README.md).
Every number below names its command there. The implementation of any option needs the owner's go.

## Summary

**HP 6, small input angles.**
- A refinement rule with a proof exists: the exemption of Miller, Pav and Walkington (2005). It terminates on every input for
  bounds up to 26.45 degrees. Next to an input angle φ below 60 degrees it leaves no angle below arctan(sin φ / (2 − cos φ)),
  which is about φ, and no angle above 137.1 degrees anywhere.
- The proof covers circumcentres and diametral circles only. The library's refiner also uses off-centres, lenses, the snap grid and
  rule C, and bounds up to 33 degrees. No paper read covers those together with small angles. So the proof does not carry over.
- Measured on 96 procedural inputs: every triangle the library leaves below the bound is of the two kinds the literature
  excuses (at the apex, or its shortest edge spans the small angle). 3.5 to 4 % of them are below the paper's floor (down to
  0.27 φ in the constrained mode). The prototype of the proven rule has none below the floor, with 1.5 to 1.8 times the triangles.
- "Below the bound only where it is necessary" has no clean answer. Shewchuk (2002, section 5) shows that new small angles
  cannot be avoided in general. A collar prototype (corner lopping) cuts the triangles below the bound by 76 %, at 7 times
  the triangles and with angles up to 177 degrees. That is above the plan's stop rule (3 times).
- **Leaning:** no change of the mesher now. Option 1 below (the oracle counts the excused triangles below the paper's floor, a
  report and no rule) makes the weak spot visible at small cost. The proven rule as a mode (option 2) only with a use case.

**HP 7, a certifying oracle.**
- A checker of a certifying design is feasible for the core of the specification: a planar triangulation that covers the
  domain and is constrained Delaunay. The certificate is what the mesher already holds: the input constraints of every
  subsegment. The checker is linear, and the argument is short (section 2.3).
- Measured on procedural inputs: the oracle takes 15 to 477 times the mesher, the prototype checker 1 to 13 % of the mesher.
  The prototype models no excuse and no snap grid, so this is an upper bound, not a replacement.
- The profile shows where the oracle's time is on these inputs: scans of all ring edges per source in `Segments` and the exact
  area of the domain. Both can be fixed inside today's oracle without a certificate.
- **Leaning:** option 1 first (index the two scans in the oracle, measured share 89 to 94 % on the parcels inputs). The
  certifying core (option 2) only if the oracle is still above its target afterwards. The full certifying oracle with the
  grid and all excuses (option 3): no.

**Side findings** (section 3): the oracle judges the snapped jittered parcels of `perf --quality` INVALID from 2 x 2 rings on,
and takes 195 s on one coastline output.

## 1. HP 6: small input angles

### 1.1 The question and today's state

Can refinement terminate with a proven minimum angle everywhere, except in a neighbourhood of the small input angles that is
provably necessary, without a weaker guarantee elsewhere?

Today ([MESHING2D_SMALL_ANGLES.md](../design/MESHING2D_SMALL_ANGLES.md), owner 2026-11-09, #496):
- The mesher does not split a triangle whose shortest edge spans an input angle below 60 degrees, with its ends within a factor
  1.5 in distance from the apex. The oracle excuses the same triangles (`SpansSmallInputAngle`), with no floor on their angle.
- A triangle at the apex is excused when the input forces its angle.
- Termination is measured, not proven.

### 1.2 The literature, in our words

Papers only; no code was read. What was read and what was not is in section 4.

**Miller, Pav, Walkington 2005** ("When and why Delaunay refinement algorithms work", full text read).
- The algorithm is Ruppert's, with concentric shells: a subsegment with one input end is split at a power-of-two distance
  from that end. One exemption: a triangle with an angle below the bound is not split when the edge opposite that angle joins
  two midpoints on adjoining input segments that meet below 60 degrees. A point of an unbalanced shell split counts as an input
  point, not as a midpoint.
- Proven: termination for every bound up to arcsin(2^(−7/6)), about 26.45 degrees, on any input (theorem 4, corollary 2).
  Every angle is at least the bound, except an angle opposite such an edge, which is at least arctan(sin φ / (2 − cos φ))
  (lemma 7). No angle is above about 137.1 degrees (corollary 3).
- Plain Ruppert with shells terminates when the bound is below both 26.45 degrees and that floor of the smallest input angle
  (claim 10.1). The exemption is not needed above 36.53 degrees.
- Not covered: off-centres, lenses, an area bound. Grading is asserted, with the proof in Pav's thesis (not reached).
- The limit of 26.45 degrees is tight for this algorithm; the case that sets it is a T-junction.

**Shewchuk 2002** ("Delaunay refinement algorithms for triangular mesh generation", sections 5 and 6, full text read).
- The Terminator: a skinny triangle whose circumcentre encroaches a subsegment of a cluster is split only if the new
  vertex would not be closer to others than its grandparent was. It always terminates (theorem 11). At the 20.7 degree setting
  no angle is below arcsin(sin(φ / 2) / √2), about φ / 2.83 (theorem 12). There is no bound on large angles.
- The negative result (section 5): "For any angle bound θ > 0, there exists a PSLG X such that it is not possible to triangulate
  X without creating a new corner (not present in X) whose angle is smaller than θ." The example is three segments at one vertex,
  a tiny angle next to a large one. For a bound of 30 degrees it applies from about φ = 0.6 degrees down.

**Pav, Walkington 2005** ("Delaunay refinement by corner lopping", full text read).
- Every acute corner gets a ball of radius (√2 − 1) times its local feature size. The segments at the corner are cut on the
  ball. The ball shrinks when another vertex enters it. A circumcentre inside a ball is never inserted.
- Proven: termination below 20.7 degrees; the triangles that may be below the bound have all vertices within two radii of a
  lopped corner. No formula for their angles. Large angles occur there; the fix (protected arcs on the ball) is only sketched.

**Rand 2011** ("Where and how Chew's second Delaunay refinement algorithm works", full text read). Chew's second algorithm with
off-centres terminates up to 28.6 degrees only if no input segments are adjacent; with collar protection the result holds "away
from small input angles", without an angle formula.

**What follows for the question.**
- The floor near a small angle is about φ, not φ / 2.83: the paper's exemption gives the stronger bound of the two proven rules,
  and it bounds the large angles too.
- The set of triangles that must be below the bound is not simply "the triangle at the apex". Around the apex, edges of length
  r and of length r·φ meet at one vertex. A triangulation with all angles at least θ changes edge lengths around a vertex by at
  most a factor (2 cos θ)^(180° / θ) (Mitchell's bound, as used by Shewchuk): 292 at 20 degrees, 72 at 25, 27 at 30. Once
  1 / φ (in radians) is well above that factor, more triangles than the apex one must be bad. This is our own rough reading,
  not a threshold from a paper; Shewchuk's worked example, which combines the factor with a wedge bound, gives about 0.6 degrees
  for a bound of 30. So a "provably necessary neighbourhood" depends on φ and on the bound, and no paper read gives it in
  closed form.

### 1.3 Building blocks in the library

- The mesher has the pieces of the paper's rule: shells (`SplitSegment`), the pair cache (`PairOf`, `SmallPairSin2`), the
  spanning test (`SpansSmallAngle`), exact meeting tests (`MeetKind`).
- The oracle already credits the paper's floor for the triangle at the apex
  ([sources](../sources/geodesics-and-parameterization.md)); the spanning excuse has no floor.
- The differences to the paper: the 1.5 factor in place of "midpoints on the same shell", input points allowed as ends of the
  spanning edge, off-centres, lenses up to 25.65 degrees, rule C, the grid.

### 1.4 Measurement

`Meshing2HardProblems hp6` (4.5 minutes; raw rows in `perf/hp6-net10.csv`).

- **Inputs:** 8 procedural families with one parameter, the small angle φ: a triangle domain (`wedge`), two segments in a box
  (`boxwedge`), a fan of 5 segments, a comb of 6 teeth, a T-junction, a crossing, a rhombus, and two apexes 1/64 apart (`twin`).
  φ is 40, 20, 10, 5, 1 and 0.1 degrees; the bounds are 20 and 25 degrees. That is 96 inputs.
- **Meshers:** the library in the default mode (`g3-default`) and in the constrained quality mode (`g3-cq`), each judged by the
  oracle; and a reference refiner written from the papers, in three modes: plain Ruppert with shells (`ruppert`), the paper's
  exemption (`mpw`), and a collar after Pav and Walkington with protected arcs (`collar`).
- **The reference refiner** is a prototype with the expensive part idealised: it rebuilds the constrained Delaunay triangulation
  with the library's `ConstrainedDelaunayMesh2` in every round and works in doubles.
- **Classes of a triangle below the bound:**
  - *forced at the apex*: its small corner is an input vertex and it lies in a wedge narrower than the bound;
  - *spanning*: the edge opposite its small corner joins the two sides of a wedge below 60 degrees, with the ends at the same
    distance from the apex, or within a factor 1.5 (today's excuse);
  - *other*.

**Totals over the 96 inputs.**

| mesher | finished | triangles | below the bound | forced at the apex | spanning | other | below the paper's floor | largest angle |
|---|---|---|---|---|---|---|---|---|
| g3-default | 96 (oracle ok) | 31 682 | 2 429 | 171 | 2 258 | 0 | 85 | 155.4° |
| g3-cq | 96 (oracle ok) | 25 418 | 2 772 | 171 | 2 601 | 0 | 107 | 179.0° |
| mpw | 96 | 46 738 | 3 878 | 171 | 3 707 | 0 | 0 | 136.2° |
| collar | 96 | 227 030 | 583 | 171 | 0 | 412 | 0 | 176.8° |
| ruppert | 24 | (cascade) | | | | | | |

**By the small angle** (both bounds added; triangles / below the bound, of which not forced at the apex).

| φ | g3-default | g3-cq | mpw | collar |
|---|---|---|---|---|
| 40° | 416 / 1 (1) | 358 / 13 (13) | 450 / 0 | 1 048 / 1 (1) |
| 20° | 414 / 55 (36) | 380 / 57 (38) | 658 / 46 (27) | 1 538 / 21 (2) |
| 10° | 571 / 81 (43) | 488 / 89 (51) | 1 359 / 88 (50) | 2 501 / 49 (11) |
| 5° | 889 / 99 (61) | 732 / 115 (77) | 2 174 / 128 (90) | 4 224 / 87 (49) |
| 1° | 3 011 / 279 (241) | 2 511 / 304 (266) | 5 879 / 502 (464) | 18 360 / 160 (122) |
| 0.1° | 26 381 / 1 914 (1 876) | 20 949 / 2 194 (2 156) | 36 218 / 3 114 (3 076) | 199 359 / 265 (227) |

**What the numbers say.**
1. **Plain Ruppert cascades.** Without an exemption 72 of 96 runs do not finish: 65 run down to the triangulator's tolerance
   (2^-41 of the extent) and 7 reach the vertex limit of 20 000. The 24 that finish are the 16 with φ = 40 degrees, where
   claim 10.1 applies (the floor of φ is above the bound), and the 8 with φ = 20 degrees at the bound 20. Those 8 are outside
   the claim (the floor of 20 degrees is 17.9): there the apex triangle sits exactly at the bound, a knife edge of the
   comparison in doubles, and nothing can be read from them.
2. **The proven rule works as stated.** `mpw` finishes on all 96 inputs, including the fan, the T-junction, the crossing and
   the two apexes 1/64 apart. No angle is below the paper's floor. The largest angle is 136.2 degrees (the paper: 137.1).
3. **The library's excused set is of the right kind, and a little looser than the paper's.**
   - All 5 201 triangles below the bound are forced at the apex or spanning. None is "other", so the excuse does not reach
     away from the wedges.
   - 85 (default) and 107 (constrained) are below the paper's floor. The smallest is 0.52 φ in the default mode and 0.27 φ in
     the constrained mode (5.47 degrees at a crossing of 20 degrees). These are spanning edges with an input point as an end,
     seen from a far vertex; the paper would split them.
   - Where φ is at or above the bound, the paper's floor shows what #496 gives up. At φ = 40 degrees the library leaves 1
     (default) and 13 (constrained) triangles below the bound, down to 11.2 degrees; `mpw` and plain Ruppert leave none. The
     exemption is needed only where the floor is below the bound: below φ = 23.2 degrees for a bound of 20, and 32.7 for 25.
4. **The proven rule costs triangles here.** `mpw` has 1.48 times the triangles of `g3-default` and 1.84 times those of
   `g3-cq`, and more triangles below the bound (3 878 against 2 429), because it refines the spanning triangles that the library
   leaves. The comparison is not like for like: the library also places off-centres and uses lenses.
5. **A collar restricts the bad triangles to the apex, at a high price.** Below the bound: 583, of which 412 are not forced,
   all within 0.42 of the apex's shortest segment. That is 76 % fewer than `g3-default`. The price is 7.2 times the triangles
   (2.5 times at 40 degrees, 4.4 at 10, 7.6 at 0.1) and angles up to 176.8 degrees beside the collars, as Pav and Walkington
   describe. The plan's stop rule (more than 3 times the vertices) applies.

### 1.5 Fit and constraints

- **CPU only, netstandard2.0 and net10.0:** nothing in these rules needs more than the mesher has.
- **Determinism, bit for bit:** the rules are local decisions on exact predicates, as the mesher's are. A shell radius must be
  a power of two relative to the frame, which `SplitSegment` already handles.
- **The proof's scope** is the real constraint. A mode that the proof covers would have to switch off off-centres, lenses and
  rule C, and it does not exist on the snap grid. That is a second refinement path to test and to keep.
- **The oracle** would need the paper's exempt set as an exact predicate: both ends refinement vertices on two sources of a
  small pair, at the same shell, and the angle at least the floor. `ExistedBeforeRefinement` and `SubsegmentSources` (#870)
  carry what it needs.

### 1.6 The floor as a lemma, and an interval bisection

The cross-field note ([CROSS_FIELD_TRANSFER_2026-11.md](CROSS_FIELD_TRANSFER_2026-11.md), item 5.3) asks whether the floor can be
written as an inequality over a compact box and checked by interval bisection. It can, and the box has three parameters.

**The lemma** (the geometric core of the paper's lemma 7, in our words). Let two input segments meet at the apex x at an
angle φ of at most 60 degrees. Let a and b be vertices on them at the same distance r from x, and let a⁺ be a vertex on the
first segment at a distance between r and 2r (with shells the next vertex beyond a is such a point). Let abc be a triangle
of a Delaunay triangulation with c on the side of a-b away from x. Then the angle at c is at least
arctan(sin φ / (2 − cos φ)).

**Proof.** The points a⁺ and c are on the same side of a-b. The triangle is Delaunay, so a⁺ is not strictly inside its
circumcircle (in a constrained Delaunay triangulation this needs a⁺ to be visible from the triangle). By the inscribed angle theorem the angle at c over the chord a-b is then at least the angle at a⁺ over it. The
angle at a⁺ falls as a⁺ moves away from a, and at the distance 2r it is arctan(sin φ / (2 − cos φ)): with x at the origin,
a = (1, 0), b = (cos φ, sin φ) and a⁺ = (2, 0), its tangent is sin φ / (2 − cos φ). On the side of x the same argument with
x in place of a⁺ gives at least φ.

**The bisection** (`Meshing2HardProblems floor`, a throwaway check in doubles widened per operation, not a formal proof).
Box: φ, and c in [−4, 6] x [−5, 5] without discs of 0.01 around a and b; a⁺ = (2, 0). A box is closed when c is on the side
of x, when a⁺ is strictly inside the circumcircle, or when the cotangent of the angle at c is at most 1.05 times the floor's.
- One angle (φ = 20 degrees): proved with about 50 000 boxes.
- φ in [20, 21] and in [40, 60] degrees: proved, with 52 and 145 million boxes.
- φ in [0.5, 60] degrees: not finished within the limit of 200 million boxes (the boxes left at the limit are reported as
  undecided). A range of φ costs a thousand times the boxes of one angle and more, so plain bisection is a poor tool for
  the whole range; the proof above does not need it.

Raw output: `perf/floor-net10.txt`.

**What the lemma needs, and what the library gives.** Three hypotheses: the two ends at the same distance from the apex, a
next vertex within twice that distance on the same source, and a Delaunay triangle. The oracle's excuse asks for none of
the three: the ends may differ by a factor 1.5, they may be input points with no vertex beyond them, and in the constrained
mode the triangle need not be Delaunay. Without the second hypothesis there is no floor at all: c can lie as far away as
the mesh allows, and the angle goes to zero. The 85 and 107 triangles below the floor in section 1.4 are counterexamples
to a floor for today's excuse; which of the three hypotheses each one breaks was not recorded. Each is a test input (the smallest: `crossing`, φ = 20 degrees, bound 20, constrained mode, 5.47 degrees). So the answer to
the item is: the floor is a lemma for the paper's rule, with a three-parameter box; for today's excuse region the search
ends in a counterexample, not in a proof.

### 1.7 Options, ranked

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 1 | The oracle counts the excused triangles below the paper's floor (a report like `MergeZoneExcused`, no rule) | the weak spot becomes visible per run; a basis for any later decision | S | low: no verdict changes |
| 2 | The paper's rule as a proven mode (bounds up to 26.45 degrees, off the grid, circumcentres only), with the oracle's excuse for it tightened to the paper's set | a proven guarantee: floor about φ, at most 137.1 degrees; none of the 85 to 107 triangles below the floor | L (tier L: a second refinement path, the oracle's rule, goldens) | medium: about 1.5 to 1.8 times the triangles on these inputs; a default or output change is the owner's |
| 3 | Lower the pair threshold from 60 degrees to the angle where the floor equals the bound (23.2 degrees at 20, 32.7 at 25) | the bound holds again for φ between that angle and 60 (1 to 13 triangles on these inputs) | M | high: #496 came from a non-terminating run at 26.6 degrees; claim 10.1 does not cover off-centres and lenses |
| 4 | A collar mode (corner lopping with protected arcs) | bad triangles only at the apex and beside the collar (−76 %) | L | high: 7 times the triangles, angles up to 177 degrees, proven only below 20.7 degrees; over the plan's stop rule |
| 5 | A proof for the library's own rule (off-centres, lenses, the 1.5 factor) | the guarantee without a change of output | research, not PR-sized | no paper to build on |

**How others solve it.** CGAL's 2D mesher documents 20.7 degrees as the termination limit and states that triangles near
smaller input angles violate the criteria, without a formula (manual, from search snippets only; the host was not reachable).
Shewchuk's paper reports for its own Terminator, on an input with angles from 1.43 to 8.97 degrees at the 20.7 degree setting,
nine new angles below the bound, the smallest 5.83 degrees. Gmsh's manual gives no angle guarantee. So an excuse near small
angles without a tight region is the common practice; the paper's floor of about φ is the strongest proven statement found.

**Leaning.** Option 1 now. Option 2 only when a use case needs a proven bound near small angles; then as an opt-in mode,
not as the default. Options 3 to 5 not now.

**Status of option 1: done** (#1358, 2026-11-22). The oracle counts the triangles excused by `SpansSmallInputAngle` (both kinds)
whose smallest angle is below the floor of the pair that excused them, `OracleReport.BelowMpwFloorExcused`, exact
(`Exact.BelowMpwFloor`, no root), and lists them in `ExcusedSummary`. A report, no rule: no verdict and no output of the mesher
changes (the count is taken after the excuse has decided). Tests (`Meshing2OracleMpwFloorTests`): the counts of the classifier of
section 1.4 on `boxwedge` and `fan5`; since 2026-11-25 also `crossing` against a recount in doubles, including the smallest case of
section 1.4 (φ = 20 degrees, bound 20, constrained mode, 5.47 degrees); a mesh without small angles counts 0. Design:
[MESHING2D_SMALL_ANGLES.md](../design/MESHING2D_SMALL_ANGLES.md).

## 2. HP 7: a certifying oracle

### 2.1 The question and today's state

Can the oracle be replaced or complemented by a certifying algorithm: the mesher emits a certificate and a cheap independent
checker verifies it? What does the checker cost against today's oracle?

Today ([MESHING2D_FAST_ORACLE.md](../design/MESHING2D_FAST_ORACLE.md)): the fast path already follows the idea in one place,
the global Delaunay pass, where the oracle builds and certifies its own witness. It stopped at its stop rule (Herne 3.6 times
the mesher; the target was 2 times and the stop rule 2.5).

### 2.2 The literature, in our words

- **McConnell, Mehlhorn, Näher, Schweitzer 2011** (not reached; the definitions read in Alkassar et al., arXiv 1301.7462): a
  certifying algorithm returns a witness with its output; a checker accepts only if the witness proves the output correct for
  this input. Only the checker has to be trusted, so it must be simple.
- **Devillers, Liotta, Preparata, Tamassia 1998** (full text read): a geometric graph with the edge order at each vertex is a
  planar triangulation if and only if the order is planar (an Euler count), the inner faces are triangles and the outer face is
  a convex polygon (lemma 17, theorem 18: linear time). After that, Delaunay is one in-circle test per edge (corollary 23).
  Convex outer face only: holes and non-convex domains are not treated. Their figure 5 shows that a planar order does not give
  a planar drawing once faces are not convex.
- **Mehlhorn et al. 1996 / 1999** (as restated by Devillers et al.): local convexity does not prove a convex polygon or
  polytope, since a locally convex curve can wind around more than once. One global test (a ray from a point of the core hits
  one facet) closes the gap.
- **The Delaunay lemma and its constrained version** (Shewchuk's lecture notes, theorems 3 and 13): for a triangulation of a
  point set, all edges locally Delaunay gives Delaunay; for a triangulation of a domain with segments, all edges that are no
  segments locally Delaunay gives constrained Delaunay. Being a triangulation that covers exactly the domain is a hypothesis
  and needs its own check. The constrained version says nothing about vertices hidden behind segments, so it does not give
  the conforming property across holes.
- **Sawai, Tanaka, Ozaki, Oishi 2024** (arXiv 2401.08242, partly read): a validator after the fact for triangulations, which
  proves coverage without overlap by growing a polygon; about n^1.6 measured.

No paper was found that certifies a conforming Delaunay mesh of a domain with holes by a triangulation of the complement.
The oracle's own witness (section 6 of the fast-oracle note) is that construction.

### 2.3 The design: the certificate and the checker

**The certificate:** for every subsegment, the input constraints it is a piece of. The mesher holds this already
(`ConstrainedMesh2.SubsegmentSources`, internal, #870).

**The checker** (prototype: `Hp7Checker.cs`; rings define the domain, no snap grid; ε is the tube):
1. Every triangle is positive (exact orientation, with a filter).
2. No directed edge is in two triangles, and every vertex is a corner of a triangle.
3. Every subsegment is an edge and names at least one source. Every vertex of a piece is within ε of each source it names.
   The pieces of a source, sorted along it, are one chain from its first end to its last. Sources on the same two input
   points (a border that two rings share) are one class: a piece names all of a class or none.
4. An edge with one triangle is a subsegment that the rings cross exactly once, with the domain on its left. An edge with two
   triangles is crossed by the rings zero times in total (two outer rings that share a border cancel).
5. Every edge with two triangles that is no subsegment is locally Delaunay (exact in-circle, with a filter).
6. Every triangle below the angle bound is on the mesher's list.

Passes 1, 2, 4, 5 and 6 are linear. Pass 3 sorts the pieces of each source.

**Why this proves a triangulation of the domain** (own write-up; the degree argument is the one of the fast-oracle note,
section 6.2, and of the checkers above).
- Let c(p) be the number of triangles that contain a point p. By 1 and 2, c is constant near every point that is on no edge
  with one triangle, and it changes by one across such an edge. So c(p) is the winding number of the mesh's boundary around p.
- By 3 and 4 the boundary of the mesh is the sum of the rings, with every ring edge replaced by its chain. A chain stays in
  the ε-tube of its edge and has the same ends, so it can be moved onto the edge inside the tube.
- So for every p farther than ε from the rings, c(p) is the winding number of the rings: 1 in the domain and 0 outside.
- Hence, outside the tubes, the triangles cover the domain exactly once. No two triangles overlap there, no vertex lies inside
  another triangle, and no edges cross. The sweep for crossing edges, the point-in-triangle search and the exact area are not
  needed for this conclusion.
- A double cover is the case the local tests miss. The self-check has one: an annulus covered twice, with every triangle
  positive and every inner edge in two triangles. Pass 3 rejects it, since no chain of a ring edge joins its two ends.

**Hypotheses the argument uses and the prototype does not check.**
- The rings are simple and properly nested or side by side (a precondition on the input, which the oracle checks today).
- The sign of a ring's area is taken in doubles.
- No two input points have the same coordinates. The classes are keyed by input points, so a border between duplicated
  points is not recognised as shared (the review's second pass built such a mesh, and the prototype accepts it).
- Every input point lies in the domain. An input point in a hole or outside the rings would make pass 2 reject a valid mesh.
- Sources that overlap only in part are not recognised as one class. Then two chains can run beside each other in the
  tube, and the mesh need not be edge to edge there. The real checker needs the noding of the input for this, which is
  an input-only computation.
- Cover-once outside the tubes does not by itself make the mesh edge to edge on a border that two rings share: a vertex can
  hang on one side. The class rule in pass 3 closes this for borders on the same two input points.

**Why constrained Delaunay follows:** the constrained Delaunay lemma, with pass 5 and the triangulation just proven.

**What a certificate does not replace.**
- **The conforming property across holes.** The lemma needs a triangulation of the convex hull. The oracle already builds and
  certifies its own completion (0.45 s to build and 0.9 s to certify for 122 721 vertices, fast-oracle note 6.7). A completion
  from the mesher would save the build, at the price of independence.
- **The excuses.** Small input angles, merge zones, O11 pieces, the grid rules and Q17 are rules of the specification about
  the input. A certificate can name the rule and the sources for each excused item, so the checker verifies one case instead
  of searching; the exact test of each case stays.
- **Inside the tubes** the cover number is that of the chains. Where the tubes of two sources overlap (small angles, the
  snap grid), the chain rules of the specification decide. That is the Q17 part of the plan, and it is not modelled here.

**Soundness against a wrong certificate.** The checker must test every claim of the certificate against the input, so that a
wrong certificate can only make it reject. The first version did not: the independent review of this spike found three
meshes it accepted wrongly (an edge that is not locally Delaunay, declared a subsegment of no source; a vertex hanging on a
border that two rings share; an input point left out of the mesh). All three are fixed and are cases of the self-check now.
This is the main risk of the design in small: every rule the checker does not enforce is a way past it, and the list of
rules is only as complete as the review of the argument. `Meshing2HardProblems selfcheck` runs 52 cases: a reversed, missing or doubled triangle, a
missing triangle declared a hole in the certificate, a flipped edge, a vertex moved away, a piece named for another source or
for none, a missing subsegment, an unlisted triangle below the angle, the three cases of the review, and the double cover. The 47
corrupted ones are rejected; the mesher's four results and the single cover of the annulus are accepted.

### 2.4 Measurement

`Meshing2HardProblems hp7 --size large` (13 minutes; raw output in `perf/hp7-net10.txt`). Linux container, 4 cores, net10.0.
Mesher: the median of three runs after one untimed run. Oracle: the fast path, the second of two runs, with its laps on.
Checker: the best of five; its certificate is derived from the geometry outside the timing. Verdicts: oracle ok and checker
accepted on the 17 rows without a snap grid; on the 3 rows with one the oracle says INVALID (section 3) and the checker,
which does not model the grid, accepts.

- **Inputs:** `parcels`: side x side jittered square rings that share their borders (the generator of `perf --quality`);
  `coast`: one ring of n points with a random radius; a comb of 40 teeth of 1 degree and a fan of 12 segments 2 degrees apart;
  one input with a corner at (1e-250, 1e-270) (the class of #947). Bound 20 or 30 degrees, both modes.

| case | triangles | mesher ms | oracle ms | oracle / mesher | checker ms | checker / mesher |
|---|---|---|---|---|---|---|
| parcels30 q20 default | 1 802 | 27 | 3 375 | 124 | 1.59 | 0.058 |
| parcels60 q20 default | 7 224 | 48 | 18 451 | 382 | 2.83 | 0.059 |
| parcels60 q30 default | 13 760 | 125 | 20 682 | 165 | 5.29 | 0.042 |
| coast2000 q20 default | 26 144 | 403 | 6 084 | 15 | 9.00 | 0.022 |
| coast8000 q20 default | 401 151 | 6 648 | 195 355 | 29 | 160 | 0.024 |
| parcels60 q20 cq | 7 202 | 37 | 17 616 | 477 | 3.24 | 0.088 |
| coast2000 q20 cq | 7 600 | 153 | 2 376 | 16 | 1.65 | 0.011 |
| coast8000 q20 cq | 87 879 | 2 506 | 47 247 | 19 | 34 | 0.014 |
| fan12 φ = 2° q20 default | 31 | 0.13 | 14 | 110 | 0.02 | 0.131 |
| wide exponents q20 default | 66 | 0.26 | 103 | 392 | 0.01 | 0.034 |

(10 of 20 rows; all are in the raw output. Mesher times of a few milliseconds vary by a factor of two between runs, and
so do the ratios of those rows.)

**The oracle's time by check** (its own laps, `G3_ORACLE_TIMING`; the largest per case).

| case | largest checks |
|---|---|
| parcels60 q20 default | segments 51 %, domain 41 %, marks 2 % |
| parcels30 q20 default | segments 62 %, domain 28 %, marks 4 % |
| coast8000 q20 default | conformity 31 %, coverage 23 %, quality 22 %, segments 17 % |
| coast8000 q20 cq | coverage 30 %, conformity 28 %, segments 25 %, quality 11 % |
| fan12 φ = 2° q20 default | quality 88 % |
| wide exponents q20 default | quality 61 %, segments 24 % |

**Locating against deciding** (item 5.5 of the cross-field note: how much of the oracle's time is search). Sampling
profiles (dotnet-trace), split by `profile_split.py`: a sample counts as *deciding* when its stack is inside the oracle's
exact kernel (`Exact`, the rationals `Q` and `Pt`, `System.Numerics`) or one of its tube and zone tests, and as *locating*
otherwise (scans, grids, list copies, hashing, sorting). Stratum: one input each; the times include the allocation and the
garbage collection the functions cause. Raw output: `perf/profile-split.txt`.

| case | deciding | locating | largest checks: share of the oracle (locating share of the check) |
|---|---|---|---|
| parcels30 q20 default | 33 % | 67 % | segments 57 % (84 %), domain 32 % (36 %) |
| coast2000 q20 default | 67 % | 33 % | conformity 27 % (39 %), segments 27 % (39 %), coverage 19 % (21 %), quality 15 % (10 %) |
| coast2000 q20 cq | 54 % | 46 % | segments 34 % (71 %), conformity 25 % (44 %), coverage 15 % (17 %), quality 14 % (13 %) |
| parcels30 q20 snap 1/64 | 71 % | 29 % | segments 79 % (17 %), coverage 12 % (96 %) |
| wide exponents q20 default | 69 % | 31 % | quality 41 % (4 %), segments 20 % (13 %) |

- On the parcels without a grid, the time is mostly locating. In the profile of `parcels30 q20 default` (inclusive times),
  49 % of `Segments` is in copying lists of ring edges per source (`ToList` under `RetainedPieces`) and 11 % in the greatest
  common divisor of the exact rationals; 90 % of `Domain` is the exact area (`ComputeArea`), 38 % of it the same divisor.
- On the other inputs most of the time is deciding: exact rational arithmetic, without a filter in doubles in front of it.
- The deciding share is an upper estimate of what must stay: a predicate evaluated on a candidate that a search produced
  disappears with the search. So "locating" is a lower bound of what hints remove.
- **The stop rule of item 5.5** asks whether the oracle stays above 2.5 times the mesher with all search removed. On these
  inputs it does: with locating at 29 to 67 %, a ratio of 15 to 477 stays far above 2.5. Hints alone do not reach the target.
  Herne and the corpus were not measured (no tiles and no corpus files in this session); `profile_split.py` runs on any
  trace of the oracle.
- What makes the prototype checker fast is therefore not the hints alone. It is three things together: the certificate
  (no search), predicates behind filters in doubles (on these outputs no orientation test and at most 519
  in-circle tests fell back to exact arithmetic), and the degree argument, which replaces checks instead of speeding them up.

**What the numbers say.**
1. **The upper bound is large.** The checker of the core takes 1 to 13 % of the mesher's time; the oracle 15 to 477 times.
   If the core were all, a certifying design would end the speed item.
2. **The plan's stop rule does not apply.** It asked to stop if exact arithmetic on single predicates were above 80 % of the
   time left. On these inputs it is not: `Segments` is mostly scans, and the checks the degree argument replaces (conformity,
   coverage) are 31 to 58 % of the oracle on the coast inputs.
3. **Much of the gain does not need a certificate.** The two largest items on ring-heavy inputs are a scan of all ring edges
   per source and the exact area. The first needs an index (the oracle has `BoxGrid`), the second a filter or the degree
   argument. Both stay inside the oracle and keep it independent.
4. **Hints are the smaller part** (the split above): locating is 29 to 67 % of the oracle's time; the rest is exact
   arithmetic. How much of it a filter in doubles would decide was not measured on the oracle; the only evidence is the
   checker's own two predicates, which fell back to exact arithmetic at most 519 times on 401 151 triangles.
5. **The oracle's time per source is high.** The ratio to the mesher is worst where an input has many sources and few triangles
   per source (parcels: 2 triangles per ring at 20 degrees). Herne has the opposite shape, which is why its ratio is 3.6.
6. **The strata differ.** With many small input angles (`fan`, `comb`, wide exponents) the time is in `quality`, in the
   excuses, which a certificate shortens only by naming the case. On the three snap-grid rows the checker says nothing: it
   accepts outputs that the oracle judges INVALID, because the grid rules are not modelled (the oracle's verdict there is a
   finding of its own, section 3).

### 2.5 Fit and constraints

- **CPU only, netstandard2.0:** the checker is integer bookkeeping and two filtered exact predicates. The oracle's project
  is netstandard2.0 already.
- **Determinism:** the checker's result is a function of exact predicates; the filters only decide what is certain.
- **Independence** ([ACCEPTANCE_STRATEGY.md](../design/ACCEPTANCE_STRATEGY.md)): the certificate is data, not code, and every
  claim in it is tested. The oracle would still reference nothing of the library. What changes is that a verdict then rests
  on an argument (section 2.3) in place of a direct search, as it does for the Delaunay witness today.
- **The reference path stays** as it is: every test exact, every scan full, and `Meshing2OracleModesTests` compares the paths.

### 2.6 Options, ranked

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 1 | Inside the oracle: index the per-source scans of `Segments` (pieces by source, the existing grid), filter or drop the exact area, and put filters in doubles in front of the exact tests of `Segments`, `Coverage`, `Conformity` and `Quality` | `Segments` and `Domain` are 89 to 94 % of the oracle on the parcels inputs (17 to 49 % on the coast inputs); needs no certificate | M (an equivalence argument per replacement, as in #376; check mode; mutants) | low to medium: the examples an INVALID report names may change order |
| 2 | A certifying core on the fast path: `SubsegmentSources` as the certificate and the checker of section 2.3 for coverage, conformity and the chains, where no excuse and no grid rule is involved | the core's upper bound (conformity and coverage: 31 to 58 % of the oracle on the coast inputs, 1 to 7 % on the parcels) | L (tier L: the argument against the specification's own wording of coverage, the tubes, the reference comparison) | medium: two ways to the same verdict must agree on every input, including inside the tubes |
| 3 | A full certifying oracle: also the grid rules, Q17 and every excuse named in the certificate | the rest | XL | high: the certificate grows with the specification, and the mesher becomes responsible for explaining each excuse |

**How others solve it.** Checkers of triangulations in the literature (Devillers et al. 1998; the LEDA checkers of Mehlhorn et
al.) cover a convex outer face and plain Delaunay; the validator of Sawai et al. 2024 covers constrained triangulations of
polygons after the fact, without a certificate, at about n^1.6. None covers tolerance tubes, snap rounding or excuses. Our
oracle's certified Delaunay witness is already at the level of that literature.

**Leaning.** Option 1 (the scans first, then the filters, each measured), then measure again on the corpus and, where tiles are available, on Herne. Option 2 only if the oracle
is still above its target after that; its argument is written down here and its prototype exists. Option 3: no.

## 3. Side findings

1. **Snapped jittered parcels are judged INVALID.** The input of `Meshing2Benchmark perf --quality` (which only times the
   mesher) with a snap pixel of 1/64, bound 20 degrees: `BoundaryOutsideTube`, "the input boundary near ... has no output
   boundary within 1 pixel", 8 violations, from 2 x 2 rings (8 triangles) on. Reproduce:
   `Meshing2HardProblems dump --case "parcels2 q20 snap 1/64" --output in.txt`, then
   `Meshing2Benchmark mesh --input in.txt --output out.txt --milestone M3`. The named points lie on borders that two rings
   share. Not diagnosed: it may be the oracle's rule for shared borders of outer rings on the grid, or the mesher.
2. **The oracle takes 195 s on one output** (`coast8000 q20 default`, 401 151 triangles, mesher 6.6 s): conformity 62 s,
   coverage 44 s, quality 44 s, segments 34 s.
3. **Angles between the bound and 60 degrees** lose the bound today although plain Ruppert keeps it on these inputs
   (section 1.4, item 3). This is the known trade of #496, now with numbers.

## 4. Provenance

- **Read in full (paper text only):** Miller, Pav, Walkington, IJCGA 15(1), 2005; Shewchuk, Computational Geometry 22, 2002,
  sections 5 and 6, and "Mesh generation for domains with small angles", SoCG 2000; Pav, Walkington, IMR 2005; Rand, CCCG 2011;
  Devillers, Liotta, Preparata, Tamassia, CGTA 11, 1998; Shewchuk, "Lecture notes on Delaunay mesh generation", sections 2.4
  and 2.8; Alkassar, Böhme, Mehlhorn, Rizkallah, arXiv 1301.7462; Dufourd, Bertot, arXiv 1007.3350.
- **Abstract or parts only:** Sawai et al., arXiv 2401.08242.
- **Not reached** (hosts blocked in the session): McConnell et al. 2011 and Mehlhorn et al. 1999 in full text; the IMR 2003
  version of Miller, Pav, Walkington and Pav's thesis; Rand and Walkington, "Collars and intestines"; Cheng, Dey, Shewchuk,
  section 6.6 (the table of contents only); the CGAL and Gmsh manuals (search snippets only).
- **No code was read**: no Triangle, Triangle.NET, CGAL, LEDA, GEOS, TetGen or any other mesher's code. The prototypes were
  written from the papers and from this repository.
- **Reproduce:** `dotnet build benchmarks/Meshing2HardProblems -c Release`, then `hp6`, `hp7 --size large` and `selfcheck`
  ([README](../../benchmarks/Meshing2HardProblems/README.md)).
