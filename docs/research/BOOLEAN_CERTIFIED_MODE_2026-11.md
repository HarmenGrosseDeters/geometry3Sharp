# Certifying Boolean checker stage 3, a certified user mode, exact coplanar clip coordinates (2026-11-22)

A research spike for the parked item of plan #1256 (owner go 2026-11-22). It answers three questions with measurements:
what does a checker that independently certifies a result of `ExactMeshBoolean` cost per run, can it be offered to users
as an opt-in certified mode, and what exactly is needed for exact coplanar clip coordinates. **No library code was
changed.** The prototypes are two modes of `benchmarks/BooleanComparison` (`certify`, `coplanar-clip`); every number below
names its command, and the raw output is in `benchmarks/BooleanComparison/perf/`.

It builds on section 3 of [BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) (the parts C1-C5
of a certificate) and on the stages 1-2 checker (#1061, [QA 2.4a](../qa/testing-and-invariance.md), test-only:
`tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs`).

## Result in short

| Question | Answer |
|---|---|
| Stage 3 (C2a, the embedding) | Feasible with a small checker: a box tree of its own and one predicate (the sign of an orientation determinant of four exact points). The prototype agrees with the library's independent exact test on 445 876 lattice pairs and catches 378 of 378 injected missed intersections; no false alarm on 28 procedural booleans. |
| Its cost | Deciding every sign with BigInteger (as stages 1-2 do): 14-100 times the boolean (3 on the two smallest cases). With a floating-point filter and three shortcuts: 0.4-8.5 times, median about 4, one case 14.6. The floor (floating point only, no certified answer) is 0.4-2.7 times, 3.4-8.4 on inputs with many coplanar input triangles. Near-linear in the pieces up to 395 000. The prototype is serial; the boolean ran on 4 cores. |
| Solids the rounding drops (audit F1) | The checker as built certifies both audit cases: it exempts the components the rounding removed and only counts them. Without the exemption it rejects both. Neither drop is within the rounding bound; what a certificate says about removed components is an owner decision (4.4). |
| The whole checker (C0-C5) as built | 16-570 times the boolean on these inputs (from 84 pieces on). It has one quadratic part (the coplanar stacks of C3) and one undiagnosed outlier (C5 on the linked tori: 19 s); fine at test sizes, not at these. |
| A certified user mode | Possible, not cheap: the checker moves into the library as exact code with filters (review, mutation shard, at least one new error bound), the coplanar stacks get an index and the C5 outlier a diagnosis, and the price per run is a rough estimate of 3-16 times the boolean. The class is narrow: closed inputs, and a result with contacts is certified as embedded "up to its documented contacts", not as clean. |
| What users can have today | `MeshRobustRepair.NotCleanReason(result)`: closed, manifold, no self-intersection, by an exact search on the result's doubles, for 0.1-0.6 times the boolean. It says nothing about the point set. |
| Exact coplanar clip coordinates | Already there where it matters. The boolean computes coplanar crossings as exact implicit points and gives them out (`ComputeExactVertices` / `ResultExactVertices`, public). `IntrTriangle3Triangle3.Find` returns the exact polygon rounded: 0 deviations in 120 000 coplanar pairs (93 344 intersecting, all in the plane z = 0). Open is only the legacy 2D class `IntrTriangle2Triangle2` (floating-point clip: wrong corner counts, corners off by up to 4e9 ulps on nearly parallel edges, contacts without area not reported), and its only caller in the library was dead code (removed 2026-11-22, option D1). |

Leaning (details in "Options"): build stage 3 as a test-only extension of the checker (option B); keep the certified user
mode parked until a caller needs it (option C), with these numbers as its budget; close the coplanar item for the boolean
and leave the 2D class as documented legacy (option D1).

## 1. What a certificate has to say

The owner's wording: the result is closed, manifold, free of self-intersections, and the right point set up to the rounding
bound. In the parts of the prior-art note (A, B the inputs, E the exact result, R the rounded one):

| Claim | Part | Checked today |
|---|---|---|
| the arrangement's inputs are the caller's, nothing was culled wrongly | C0 | yes (stages 1-2) |
| every piece lies in its source triangle, and the pieces of a source tile it | C1 | yes |
| no two pieces meet except in a shared corner or along a shared edge | **C2** | **no: stage 3** |
| the winding numbers on both sides of every piece are right | C3 | yes, **trusting C2** |
| the kept pieces are those the operation selects | C4 | yes |
| E is the kept pieces; R is closed and oriented; R and E are within the rounding bound of each other | C5 | yes |
| R has no self-intersection | C2 on doubles | not in the checker; `NotCleanReason` does it |

C3 is only as good as C2: the winding number is constant on one side of a piece only if no other triangle passes through
the piece, and the radial order around an edge is complete only if every piece that touches the edge has it as an edge.
So without stage 3 the checker certifies "right, if the arrangement is embedded".

## 2. Look before you build

**Library.** `BooleanCertificate` (C0, C1, C3, C4, C5; BigInteger decides every sign); `ExactTriangleIntersection`
(exact, on doubles: usable for input triangles, not for the rational corners of pieces, and it is the arrangement's own
test); `ExactPredicates.Orient3d` / `Orient2d` (adaptive, exact on doubles); `TriangleBoxTree` (the arrangement's broad
phase: not independent); the indirect predicates of the implicit points (the arrangement's own); `MeshRobustRepair
.NotCleanReason` (exact self-intersection search on doubles); `SeparationCertificate` and `ContouringTopologyCertificate`
(the house style for certificates and their mutation tests).

**Literature and practice** (papers and public documentation only):
- R. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011: output
  plus witness, a checker simpler than the algorithm. Our witness for C2 is empty: the checker re-derives the claim (the
  paper's "recheck" case), which is acceptable when the recheck is simpler than the algorithm. It is: a pair search
  against an arrangement.
- O. Devillers, P. Guigue, *Faster triangle-triangle intersection tests*, INRIA RR-4488, 2002 (J. Graphics Tools 8(1),
  2003): a triangle pair decided by orientation signs alone. The prototype follows the same principle (orientation
  signs only, no constructed point), with its own case analysis for "meets only where they share" (section 3).
- O. Devillers, G. Liotta, F. Preparata, R. Tamassia, *Checking the convexity of polytopes and the planarity of
  subdivisions*, CGTA 11, 1998: the arithmetic degree of a checker as its cost measure. C2a on rational piece corners
  has a high degree (the corners are intersections of three planes), which is why a filter is needed at all.
- J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 18, 1997:
  filters with an error bound and an exact fallback.
- How others give users this assurance: Zhou et al. 2016 (*Mesh arrangements for solid geometry*) state the output
  property (no self-intersections, winding number 0 or 1) and do not check it; CGAL's announcement of its
  autorefinement with snap rounding (2025) reports results after naive rounding that self-intersect again, and its users
  check with a separate self-intersection predicate; Manifold (Apache-2.0, documentation) guarantees a manifold result
  by construction and reports a status. **No library found that ships a certificate of a boolean against its inputs**
  (terms searched: "mesh boolean certificate checker", "certified mesh arrangement validation"; and the terms of the
  prior-art note, section 3.2).

## 3. The C2a prototype

`benchmarks/BooleanComparison/Certify.cs`. It takes the arrangement's pieces (three point ids each, the source triangle)
and the exact corners (`ExactPoint3`, homogeneous rationals on the arrangement's grid: the witness) and shares no search
or intersection code with the arrangement (it uses `ExactPoint3` and, for input vertices, `ExactPredicates`):

1. **Broad phase:** boxes of the pieces from approximations of the corners, widened beyond the approximation error; a
   median-split box tree of its own with a self-join. Pairs of pieces of the same source are skipped (C1 certifies the
   tiling of a source).
2. **Pair test** "do p and q meet anywhere beyond the corners they share by id": two triangles in different planes meet
   in a segment whose ends each lie on an edge of one inside the other. So they meet only where they share iff every
   edge of each that is not a shared edge meets the other triangle at most in an end that is a shared corner. An edge
   against a triangle is decided by orientation signs: the ends against the plane, then the line against the three
   edges; an edge inside the plane by the same signs seen from the third corner of its own triangle (off the plane).
   Coplanar pairs use the same rule in the plane, plus the fold (two triangles on the same side of a shared edge); the
   same triangle twice (three shared corners) is counted as a duplicate, not a violation.
3. **One predicate**, the sign of det[b - a, c - a, d - a], in three variants:
   - *BigInteger* on the exact corners (how stages 1-2 decide every sign);
   - *filtered*: (a) equal ids give 0; (b) a corner of a piece of source s lies in s's plane (C1 certifies it), so that
     sign is 0 without arithmetic; (c) four input vertices: the library's adaptive `ExactPredicates.Orient3d` on the
     input doubles (the grid is the input scaled by a power of two and shifted); (d) otherwise the determinant of the
     approximations with an error bound from the widened entries, and BigInteger where the bound does not decide;
   - *float only*: (a)-(c) and the determinant's sign without a bound. This certifies nothing; it is the idealised
     floor of any filtered design.

The filter's bound is a prototype bound (generous, not reviewed); the verdicts of the filtered variant equal those of
the BigInteger variant pair by pair on all 28 cases.

**Self test** (`certify selftest`, `perf/certify_selftest.txt`):
- against `ExactTriangleIntersection.IntersectsBeyondShared` on random triangles of the lattices {0..2}³, {0..4}³,
  {0..8}³ with 0-2 shared corners, a quarter of them coplanar: 445 876 pairs (134 215 meeting beyond what they share),
  **0 mismatches**, in both orders and with the BigInteger, filtered, adaptive and mixed paths;
- the classic failure, a missed intersection: every split source triangle of the 17 cases (up to 40 per case) put back
  unsplit as one more piece: **378 of 378 caught**; 0 violations without the mutation.

## 4. Measured cost

Machine: a cloud container, 4 logical cores, .NET 10.0.15, Release. Medians of 5 warm runs (the BigInteger columns: one
run). The boolean runs with its default threads; every checker part is serial. The container is shared, so times vary
by some 10-30 % between runs; the ratios below are orders of magnitude, not benchmarks to regress against.

Inputs: the 17 procedural cases of `benchmarks/SolidBooleanBenchmark` (welded), and a size series (two spheres, two
tori, two finely meshed boxes face on face).

```
dotnet run -c Release --project benchmarks/BooleanComparison -- certify cost 5       > perf/certify_cost.txt
dotnet run -c Release --project benchmarks/BooleanComparison -- certify scale-c2a 5  > perf/certify_scale.txt
python3 benchmarks/BooleanComparison/certify_summary.py perf/certify_cost.txt perf/certify_scale.txt > perf/certify_summary.txt
```

### 4.1 C2a against the boolean (ms; in brackets: times the boolean)

"Total" is the exact corners, their approximations, the broad phase and the pair tests.

| case | pieces | pairs | boolean | C2a filtered, total | float floor, total | C2a BigInteger, total | `NotCleanReason(R)` |
|---|---|---|---|---|---|---|---|
| box+box overlap U | 84 | 879 | 2.9 | 15 (5.1) | 2.3 (0.8) | 182 (62) | 0.2 (0.1) |
| sphere+sphere overlap U | 14 228 | 97 294 | 69 | 70 (1.0) | 57 (0.8) | 942 (14) | 20 (0.3) |
| sphere&sphere overlap I | 14 228 | 97 294 | 41 | 81 (2.0) | 61 (1.5) | 822 (20) | 5.2 (0.1) |
| sphere-sphere D | 10 098 | 70 462 | 41 | 56 (1.4) | 33 (0.8) | 686 (17) | 11 (0.3) |
| box-cylinder D | 892 | 12 295 | 4.5 | 27 (6.2) | 8.5 (1.9) | 265 (59) | 0.5 (0.1) |
| torus+cylinder U | 4 360 | 37 040 | 18 | 104 (5.9) | 28 (1.6) | 1046 (59) | 8.8 (0.5) |
| torus+torus linked U | 7 984 | 67 842 | 35 | 261 (7.4) | 46 (1.3) | 1035 (30) | 9.0 (0.3) |
| fine box edge-touch U | 5 400 | 46 532 | 21 | 80 (3.8) | 100 (4.7) | 990 (46) | 5.2 (0.2) |
| sphere on box face U | 9 614 | 66 497 | 26 | 64 (2.5) | 65 (2.5) | 749 (29) | 6.5 (0.3) |
| cylinder on box, partial face-touch U | 5 462 | 75 019 | 41 | 595 (14.6) | 108 (2.7) | 3616 (89) | 4.2 (0.1) |
| 3 spheres U | 16 736 | 128 067 | 41 | 140 (3.4) | 67 (1.6) | 1541 (37) | 10 (0.3) |
| box+sphere+cyl U | 7 552 | 72 387 | 25 | 112 (4.5) | 43 (1.7) | 938 (38) | 6.2 (0.2) |
| sphere+sphere U n=32 | 25 108 | 167 427 | 55 | 167 (3.1) | 104 (1.9) | 1483 (27) | 30 (0.5) |
| sphere+sphere U n=64 | 99 428 | 638 704 | 190 | 454 (2.4) | 443 (2.3) | 3997 (21) | 74 (0.4) |
| sphere+sphere U n=128 | 395 508 | 2 489 606 | 718 | 1804 (2.5) | 1626 (2.3) | 12 971 (18) | 220 (0.3) |
| torus-torus D nu=64 | 7 354 | 65 135 | 41 | 335 (8.2) | 39 (1.0) | 1311 (32) | 6.1 (0.1) |
| torus-torus D nu=256 | 85 915 | 620 046 | 298 | 1572 (5.3) | 370 (1.2) | 6107 (20) | 39 (0.1) |
| fine box face-on-face U n=32 | 25 076 | 226 759 | 73 | 610 (8.4) | 609 (8.4) | 6160 (85) | 18 (0.2) |

(All 28 rows: `perf/certify_summary.txt`.)

Reading it:
- **Per pair** (two spheres, 14 228 pieces): about 9 µs with BigInteger, 0.4-0.5 µs filtered, 0.3 µs at the floor.
  Other inputs differ widely: box+box overlap needs about 200 µs per pair with BigInteger (long rationals). There are about
  7 candidate pairs per piece, most of them neighbours in one input mesh.
- **Scaling:** near-linear. Two spheres from 6 380 to 395 508 pieces: filtered total 1.5 to 3.1 times the boolean.
- **Where the filtered variant loses against the floor:** true zeros. A corner on an edge line of another piece, or
  three collinear corners in a plane, give a determinant that is exactly 0; no filter decides a zero, so BigInteger
  does (two tori: 72 459 of 445 395 predicate calls; cylinder on box: 302 628 of 3.3 million, which is the 14.6). The
  shortcut (b) already removes the largest class (232 911 known zeros on two spheres). The rest needs a cheaper exact
  stage (expansions or the fixed-width exact points the arrangement already has, at the price of sharing them) or more
  witness data (which input edge a point lies on).
- **Where the floor itself is high:** pairs of input triangles in one plane (meshed box faces): up to 10 million
  adaptive predicate calls for 25 076 pieces. A per-plane 2D test would be cheaper than the generic pair test there.
- **The parts:** the broad phase is a third to a half of the filtered total on the sphere cases (24 of 70 ms); the
  exact corners cost little here (1-5 ms) because recording a certificate already computes most of them.
- **Recording** (`RecordCertificate`, `ComputeExactVertices`) costs the boolean typically 10-40 %, on single rows nothing measurable or as
  much as the boolean itself (column "recording" of `perf/certify_cost.txt`: a difference of two noisy medians, not
  measured on its own).
- **Plan #1256's stop rule** ("stop if C2a takes more than 3 times the boolean without a clean spatial-index
  speedup"): the BigInteger version fails it everywhere; the filtered prototype meets it on 10 of the 28 cases and
  misses it on the rest, mostly by the true zeros; the floor meets it except on the coplanar grids. As a serial prototype
  against a boolean on 4 cores. The pair tests are independent, so they parallelise with a deterministic verdict (all
  pairs pass, or the smallest failing pair as the witness); in exact code that loop would need its allow-list entry.

### 4.2 The stages 1-2 checker as built, on the same inputs

| case | pieces | boolean | C0 | C1 | C3+C4 | C5 | sum (times the boolean) |
|---|---|---|---|---|---|---|---|
| box+box overlap U | 84 | 2.9 | 18 | 23 | 20 | 39 | 100 (34) |
| sphere+sphere overlap U | 14 228 | 69 | 251 | 777 | 352 | 1851 | 3231 (47) |
| sphere-sphere D | 10 098 | 41 | 867 | 385 | 179 | 1306 | 2737 (66) |
| box-cylinder D | 892 | 4.5 | 3.4 | 14 | 25 | 30 | 72 (16) |
| torus+torus linked U | 7 984 | 35 | 87 | 467 | 214 | **19 246** | 20 014 (572) |
| fine box edge-touch U | 5 400 | 21 | 31 | 174 | **5120** | 1335 | 6660 (311) |
| cylinder on box, partial face-touch U | 5 462 | 41 | 16 | 171 | **4572** | 452 | 5211 (128) |
| 3 spheres U | 16 736 | 41 | 210 | 561 | 257 | 1358 | 2386 (58) |

All 17 certify (no failure, no uncovered triangle). The checker's own comment says it is a test oracle and names the
coplanar stacks of C3 as quadratic per plane; the numbers confirm both (in bold). The 19 s of C5 on the linked tori are
one outlier, not diagnosed here: the comment calls C5 near-linear, and the other rows agree. Neither matters at the sizes the tests use (hundreds of pieces) and
would have to go before any user mode.

### 4.3 The result on its own

`MeshRobustRepair.NotCleanReason(R)` costs 0.1-0.6 times the boolean and is exact on the doubles. On the cases whose
solids touch (edge-touch, vertex-touch, pole touch, the torus differences: `Contacts` 1-16) it reports
"self-intersections": the result's contact is split into manifold pairs that coincide in position, as documented. A
certificate therefore cannot promise "clean" for every input; for a result with contacts it can promise "embedded
except at the contacts the boolean reports", and that statement needs its own check (the coinciding corners and edges
are exactly the reported contacts).

### 4.4 Components the rounding removes (cases of the pipeline audit)

The `SolidBooleanPipeline` audit (PR #1304, its note's finding F1) found that the boolean returns `Success` while
SafeRounding's removal of tiny and thin components drops a solid of the exact result. Its two cases, rebuilt here
(`certify removed`, `perf/certify_removed.txt`; the rotation is this benchmark's own, 40 degrees about (1, 2, 3)):

| case | result / exact result triangles | `RemovedComponents` | checker as built | checker strict |
|---|---|---|---|---|
| box [0,1024]³ ∪ cube of edge 2^-8 at x = 1100 | 24 / 24 | 0 | certified | certified |
| the same, edge 2^-9 | 12 / 24 | 1 | certified, 1 component exempt | **rejects** (C5: a corner of the exact result is farther than the bound 0.0177 from the result) |
| [0,2]³ ∩ [2-h,4]×[0,2]², h = 2^-10, turned | 22 / 24 | 0 | 2 triangles not covered (inconclusive) | the same |
| the same, h = 2^-15 | 0 / 30 | 1 | certified, 1 component exempt | **rejects** (C5: the exact result has triangles, the result none) |

- The checker as built follows the removal's documented rule: where the rounding reports removed components, the
  components of the exact result that are tiny or thin by that rule are left out of the coverage (QA 2.4a). So it
  **certifies both drops**, and the drop shows only in a counter (`ExemptComponents`). It sees the missing solid: with
  the exemption off ("strict": one line in the benchmark) C5 rejects both.
- Neither removal is "within the rounding bound". The bound is a distance (1e-5 of the largest component's diagonal: 0.0177 in
  the first case); the removed cube is 76 away from the rest of the result, and the removed slab leaves an empty result. The
  removal is a second, separate licence of the rounding (by size and thickness), not a consequence of the bound.
- For a certified mode this decides the certificate's wording: either "the point set within the bound" (strict: these
  results are **not certified**, and the caller learns which component is missing), or "within the bound, except
  components removed as tiny or thin", with the removed components listed in the certificate. Silent exemption is
  right for a test oracle of a documented decision and wrong for a user certificate. This is an output-semantics
  decision of the owner, together with F1 of the audit.
- A limit of the checker itself: the kept thin slab (h = 2^-10) ends as "2 triangles not covered", neither certified
  nor rejected: no single triangle of the other mesh covers them within the bound after three levels of splits. The
  campaign oracle counts that as a failure; a user mode needs a coverage test that decides.

### 4.5 Search against predicates ("hints instead of search")

The cross-field note (CROSS_FIELD_TRANSFER_2026-11.md, item 5.5) asks to split a checker's time into search and
predicates, because a producer can hand the checker witnesses for what it would otherwise search. For C2a on the two
spheres (70 ms filtered): the exact corners and approximations 5 ms, the search (broad phase) 24 ms, the predicates
42 ms; at the floor 24 of 57 ms are search.
- The search of C2a cannot become a hint: "no two pieces meet" is a claim about all pairs, and a producer cannot list
  the pairs that do not meet. Only a different witness removes it (C2b, a tetrahedralization in which every piece is a
  face: checked without any search, but of worst-case quadratic size; prior-art note, section 3.4).
- The predicates can use hints, and the largest one is already measured: "this corner lies in that source's plane"
  (shortcut (b), certified by C1) answers 232 911 of about 780 000 sign questions on the two spheres without arithmetic. The
  next hint of this kind is the incidence the arrangement knows for every implicit point (which input edge or which
  planes define it): it would turn most of the remaining true zeros (the gap between the filtered column and the
  floor) into lookups that C1-style checks verify.
- In stages 1-2 the search parts are the coplanar stacks of C3 (which input triangles contain a piece) and the
  coverage of C5 (which triangle covers which): the two slow parts of 4.2. Both are hint-shaped: the
  boolean knows a piece's stack and which exact triangle a rounded one came from. That is the first thing to try
  before an index, for a user mode and for the test oracle alike.

## 5. Can it be an opt-in certified mode?

**What it takes.**
1. The checker (1262 lines, test code) moves into the library, plus C2a (about 250 lines in the prototype). It becomes
   exact code: an entry in `.editorconfig`, the exact review, its own mutation shard (today: 36 hand mutants by script,
   about 25 minutes, because Stryker does not mutate test files).
2. Speed: every sign of C1, C3 and C5 gets the filter that C2a got here, the coplanar stacks get an index (a 2D
   grid per plane) and the C5 outlier a diagnosis. C2a showed a factor of 4-13 from filtering (totals of 4.1); a
   similar factor on the 16-66 of the well-behaved rows of 4.2 gives very roughly 2-8 times the boolean for C0, C1,
   C3-C5, plus 1-8 for C2a: **about 3-16 times the boolean in total** (an extrapolation from one measured part, not a
   measurement).
   The floor of C2a alone is 0.4-2.7 (up to 8.4 with many coplanar input triangles).
3. At least one new error bound (the filter on approximated rational corners), or the reuse of the arrangement's
   filtered predicates. The second is cheaper and weakens the independence that the name "certified" rests on; today
   the only shared code is `ExactTriangleIntersection` in C0.
4. API (additive, compatible with 1.0.324): a switch and a result object, as the plan sketches (`CertifyExactResult`,
   a certificate with one flag per part, the first failing witness, and "not applicable" with its reason). Off must
   stay bit-identical and free.
5. The usual tests: invariance over `Similarity.All()`, reuse, determinism, and one mutation test per condition (the
   test suite of stages 1-2 has them and moves along).

**Constraints.** CPU only: yes. netstandard2.0 and net10.0: yes (BigInteger and doubles; no new dependency).
Bit-for-bit determinism: the verdict is a conjunction and the witness the smallest failing index, so serial and
parallel runs agree; no floating-point value crosses threads.

**The class it can certify** (unchanged from stages 1-2): closed inputs only; an Intersection of several closed meshes
only with the early-out off (`CullOutsideA = false`, a slower boolean); components the rounding removed are exempt;
contacts as in 4.3. A user who asks for "certified" and gets "not applicable" on an open input needs that said in the
API, not in a remark.

**What the user gains.** Assurance, not a fix: the triage found no defect in the arrangement or the classification,
the 17 cases of 4.2 certify, all 28 are embedded, and the campaigns have certified since #1061. The mode pays several
times the boolean to state that.

## 6. Exact coplanar clip coordinates: what exactly is needed

The roadmap line ("the 2D clipping of `IntrTriangle2Triangle2` is unchanged; exact output coordinates stay open") and
the plan (which places the work in `ExactMeshArrangement` / `ExactTriangulation2`) describe two different things. Looked
at one by one:

| Place | State | Needed |
|---|---|---|
| `ExactMeshBoolean` / `ExactMeshArrangement` | A crossing of two coplanar edges is an implicit point of its own kind (`PointKind.Segments`), exact like every other point. `ComputeExactVertices` returns the result's vertices as rationals (`ResultExactVertices`, public); C1 and C5 check them. | **Nothing.** The plan's PR B has no work in the arrangement. |
| `IntrTriangle3Triangle3.Find` with `ReportCoplanarIntersection` | Exact decisions, and every corner is an input corner or a crossing computed from exact determinants and rounded once (#253). Measured: 0 wrong decisions in 120 000 pairs, and on the 93 344 that intersect 0 wrong corner counts and **0 corners different from the rounded exact polygon**. All pairs lie in the plane z = 0; tilted planes were not measured. | Only if someone needs the rationals themselves: an additive output next to `PolygonPoints`. No caller known. |
| `IntrTriangle2Triangle2` (public, from WildMagic5) | Floating-point separating axes and clip. Its only caller in the library, `IntrTriangle3Triangle3.GetCoplanarIntersection`, was private and no longer called (removed 2026-11-22, option D1). | See below. |

Measurement (`coplanar-clip`, `perf/coplanar_clip.txt`; 20 000 pairs per family; reference: an exact rational clip,
Sutherland-Hodgman with exact signs and crossings, a prototype in the benchmark):

```
dotnet run -c Release --project benchmarks/BooleanComparison -- coplanar-clip 20000 > perf/coplanar_clip.txt
```

| family | touching only (point or segment) | 2D class: wrong decisions | wrong corner count | corners off by more than 1 ulp | largest deviation (ulps) |
|---|---|---|---|---|---|
| lattice 0..4 | 3343 | 3343 | 8 | 17 | 3 |
| lattice 0..16 | 476 | 475 | 3 | 489 | 47 |
| uniform in [0, 1) | 0 | 0 | 0 | 3929 | 1.9e4 |
| uniform in [0, 1) + 1e6 | 0 | 0 | 0 | 6620 | 2.0e4 |
| a copy turned by 1e-9 about a corner | 0 | 0 | 5389 | 16 175 | 4.0e9 |
| a corner at the midpoint of the other's edge | 1406 | 1541 | 5007 | 5513 | 1.2e4 |

So the 2D class does not report contacts without area (by its design: 3343 of 3343, 475 of 476; in the last family at
least 135 of its 1541 wrong decisions are something else), miscounts corners where edges are nearly
parallel or a corner lies on an edge, and its corners are off by up to 4e9 ulps (about 1e-6 at coordinates of size 1) in the
nearly parallel family. Time per pair: the 2D class about 0.25 µs, `IntrTriangle3Triangle3.Find` on the same pairs
8-15 µs, the rational prototype 7-40 µs (corners of up to 182 bits).

What "exact coplanar clip coordinates" would mean for the 2D class, as options:
- **D1, leave it:** say in its documentation that it is the unchanged floating-point clip and name
  `IntrTriangle3Triangle3` (with `ReportCoplanarIntersection`) as the exact route; remove the dead
  `GetCoplanarIntersection`. Done 2026-11-22 (owner: option D1).
- **D2, make its `Find` exact:** the decisions and corners of #253 in 2D. It changes the results of a public class
  (contacts without area are reported, corner counts change) and costs 13-64 times per pair in the measurement unless filtered. An output
  change: the owner's decision.
- **D3, an additive exact clip:** a new method that returns the polygon with rational corners. Additive, about the size
  of the prototype (80 lines) plus tests. No caller known.

## 7. Options

How others solve it: section 2 (no library ships a certificate of a boolean against its inputs; users check the result
with a separate self-intersection test; Manifold guarantees by construction).

| # | Option | Gain | Effort | Risk |
|---|---|---|---|---|
| A | Nothing new; document for users that `NotCleanReason` on the result is the available check (closed, manifold, no self-intersection; 0.1-0.6 times the boolean) and what it does not say | users know what they have | S (docs) | none |
| B | **Stage 3 test-only:** C2a in `BooleanCertificate` (the prototype's pair test and tree; BigInteger, or with the shortcuts (a)-(c), which need no new bound), with fault injection (the unsplit triangle, a moved corner, a crossing piece) and hand mutants | the oracle of the campaigns and the Nightly corpus no longer trusts the embedding: C0-C5 complete | M (one session; test code only, no exact file, no API) | low. Cost per case with BigInteger on sphere-like inputs: 9 µs per pair, 7 pairs per piece, so about 45 ms for the 696 pieces of the two test spheres (C5 there: 65 ms). The Nightly corpus and the PR-tier tests grow accordingly (not measured); the 5 s rule decides on which families C2a runs in the PR tier |
| C | **Certified user mode** (opt-in): the checker in the library with filters, section 5 | a certificate for callers who must show one | L (exact review, a mutation shard, a new bound or shared predicates, two indexes, API) | medium to high: 3-16 times the boolean (estimated), a narrow class, and independence erodes with every shared building block. No caller today. |
| D1 | Coplanar: close the item for the boolean and the 3D query (already exact); document the 2D class as legacy and remove the dead caller | the roadmap matches the code | S (later library PR) | none |
| D2 / D3 | An exact `IntrTriangle2Triangle2.Find`, or an additive exact 2D clip | exact 2D clip for direct callers | M | D2 changes public results; neither has a caller |

**Leaning.** B, then stop. It is the cheap part of [plan #1256](../plans/boolean-certifying-checker-stage-3.md), closes the one hole in the oracle's argument, and needs
no decision on API or exact files. C stays parked behind its trigger (a caller who needs a certificate); if that
trigger comes, sections 4 and 5 are its budget and the first step is the filter for C1 / C3 / C5 with a measurement
like 4.1. A is a paragraph in the boolean's guide and can ride with B. For the coplanar item: D1.

**For the neighbouring spikes.** The `SolidBooleanPipeline` audit builds an independent oracle for the pipeline: the pair
test and the box tree of `Certify.cs` check any triangle soup with exact rational or double corners and could serve it.
The boolean-speed spike: nothing here changes the boolean; recording a certificate is the only cost on its side.

## 8. Limits of this spike

- The two audit cases of 4.4 were rebuilt from their description (own rotation), not taken from the audit's harness.
- Procedural inputs only, up to 395 000 pieces; no Thingi10K run (T6 would be the next sample if C is ever built).
- The filter's error bound is not reviewed; its verdicts were compared with BigInteger on every pair of the 28 cases.
  It has no underflow term: the review found wrong signs for coplanar points scaled to about 2^-360 (none at 2^-356
  and above, none in 3 million near-coplanar quadruples at normal ranges). Grid coordinates do not reach that range,
  but a library version needs the term or a range guard.
- Part 1 of the self test gives the filter exact approximations and does not use shortcut (b); the filter with real
  approximation errors and shortcut (b) are exercised by part 2 and by the pair-by-pair comparison on the 28 cases.
- Times from a shared 4-core container; ratios, not benchmarks.
- The estimate for the whole filtered checker (3-16 times) extrapolates from C2a; C1, C3 and C5 were not rewritten.
- The 19 s of C5 on the linked tori and the 4-5 s of C3 on meshed boxes were measured, not diagnosed.

## Sources

Papers and public documentation only; no code was read or ported for this spike.
- R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011.
- O. Devillers, P. Guigue, *Faster triangle-triangle intersection tests*, INRIA RR-4488, 2002; J. Graphics Tools 8(1),
  2003 (the principle; abstract and the library's earlier use, docs/sources).
- O. Devillers, G. Liotta, F. P. Preparata, R. Tamassia, *Checking the convexity of polytopes and the planarity of
  subdivisions*, CGTA 11(3-4), 1998.
- J. R. Shewchuk, *Adaptive precision floating-point arithmetic and fast robust geometric predicates*, DCG 18(3), 1997.
- I. E. Sutherland, G. W. Hodgman, *Reentrant polygon clipping*, CACM 17(1), 1974 (the reference clip of section 6).
- Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh arrangements for solid geometry*, SIGGRAPH 2016.
- CGAL project news, *Fixing self-intersections in triangle soups using snap rounding*, 2025 (the announcement text
  only); Manifold library documentation (Apache-2.0).
