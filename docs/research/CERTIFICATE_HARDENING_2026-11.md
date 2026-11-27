# Hardening the certifying checker of the exact Boolean (2026-11-25)

Owner go 2026-11-25. The certifying checker of `ExactMeshBoolean` (`tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs`,
[docs/qa/testing-and-invariance.md](../qa/testing-and-invariance.md) 2.4a) is meant to certify results of the boolean without a
reference in a planned spike. Before that, this note (a) gives hand mutants to the floating-point filters it gained on
2026-11-24 (step A of [CERTIFYING_BOOLEANS_2026-11.md](CERTIFYING_BOOLEANS_2026-11.md)) and (b) runs the checker on real
meshes (Thingi10K). It covers test code and a benchmark mode only; no library code changed.

## 1. Hand mutants of the filters

`tools/certificate_hand_mutants.py` had 50 mutants from 2026-11-22, none for the filters of 2026-11-24: the box filter in front
of the in-plane loops of C2 and C3 (`ApproxBox`, `CoplanarNear` with its linear scan and its tree query), the double filters
of C5 (a reject by the triangle's box, an accept by a point inside the triangle, with the slack `FilterEps` and its floor
2^-480). The script now has 72 mutants and a list of 15 equivalent ones; `--equivalent` runs those, and each must pass.

The new mutants (24 in the first round, 19 of them kept as mutants, and 2 of the equivalent list that the tests kill): each makes a filter decide wrongly or too early.

| Group | Mutants | Before the new tests | After |
|---|---|---|---|
| box filter: widening (both sides off, tightened 2^-49 to 2^-62), NaN guard off | 3 | survived | killed |
| box filter: a pair dropped (scan drops its last member, scan or tree tests a wrong axis, tree visits one child, leaf drops its last item) | 5 | killed | killed |
| C5 reject: no margin, margin inside delta, a wrong axis | 3 | 2 killed, no margin survived | killed |
| C5 accept: no margin, margin outside delta, the double filter decides (no exact fallback) | 3 | killed | killed |
| C5 approximation: both guards off (size and finiteness) | 1 | (new) | killed |
| C5 slack: tightened 2^-40 to 2^-58, floor 1e-300, floor 2^-560, Cover without slack | 4 | 1e-300 killed, the rest survived | killed |

The tests added for them (`BooleanCertificateTests`, each below 1 s except the widened existing one):
- `BoxFilter_KeepsBoxesThatTouch_WhenTheApproximationsAreOutOfOrder`: pairs of triangles whose exact boxes touch or overlap by
  less than an ulp, with corners of 62-bit odd denominators, so that `ApproxVector3d` (three roundings, not monotone) puts
  the corners in the wrong order in about a fifth of them; the filter boxes must still meet (test hook `ApproxBoxesMeet`,
  the closed-box test of the scan). Plus a triangle beyond the double range (its box must be everything).
- `CoverageFilters_AgreeWithTheExactTest_AtDistanceDeltaWithRoundedCoordinates`: a point at distance 1 + s 2^-k (k = 36 to
  70) from a face whose plane is x = c (c up to 2^20), all coordinates rationals of large odd denominators, against the
  exact test; and a point whose numerator is beyond 2^1024 at about 2^1000.
- `CoverageTest_UsesTheSlackOfItsCoordinates`: the coverage test's slack (`CoverageSlack`) is FilterEps of its coordinates.
- `CoverageFilters_AgreeWithTheExactDistanceTest` gained the scale 2^-545 (the floor mutant 2^-560 is sound at 2^-560 itself:
  there 3 x floor is above delta, so no accept happens), with 2000 instead of 3000 iterations per scale (the same total).

Equivalent (each with its reason in the script):
- **Box filter:** the widening of only one side; strict instead of closed comparisons in the scan and the leaves; the
  widening loosened; always or never the tree. Either side's widening of 2^-49 alone covers both
  boxes' errors, boxes that touch exactly after widening are apart before it, and the other changes only change the cost.
- **C5 margins:** loosened by a factor 4; the slack taken without the extent (extent <= 2 maxAbs); the
  floor at 2^-440 or 2^-520. A larger slack only hands more cases to the exact test. A floor of 2^-520 is still sound: the
  only underflow-sensitive step is the final length of p - q, which loses about 2^-1075 / d absolute, and an accept needs
  delta >= 3 floor; the floor has to stay above about 2^-539. This argument depends on the clamp of the weights: below
  about 2^-255 their products are subnormal noise, and only the clamp keeps q in the triangle. So it stands or falls with
  "weights not clamped" below; `..._OnThinTriangles` now also runs at the scales 2^-250 to 2^-280 (review of #1604).
- **C5 approximation:** the finiteness check alone. Behind the 1000-bit guard both parts convert to finite doubles, the
  denominator at least 1, so the quotient is finite. The size guard alone is **not** equivalent (review of #1604, first
  listed here as "each covers what the other would catch", which is wrong): a numerator below 2^1024 over a denominator above
  it converts to finite / infinity = 0, a finite value, and the accept filter then takes a point at x of about 1/4 as lying in
  the plane x = 0. `..._AtDistanceDeltaWithRoundedCoordinates` now has that case, and the mutant moved to the killed list.
- **C5 NaN weights:** a NaN weight gives a NaN distance through `Math.Min` / `Math.Max`, and no accept takes it.

One survivor that is **not proved equivalent**: removing the clamp of the closest-point weights to [0, 1] ("weights not
clamped"). The step-A note calls the clamp a belt. It survives the thin-triangle test whose summary claimed to kill it, and
that summary is corrected. A targeted search (1.2 million near-collinear cases, the double routine against an exact
`Fraction` reference, `ScratchUncoveredTests` not committed) found the weights outside [0, 1] only in 1-ulp roundings of
v + w, never with an unsound accept. It is listed as equivalent in practice. An exact argument (the face region with a wrong
sign of va, vb or vc only near region boundaries) is open.

Run (2026-11-25, 4 cores, about 2 hours for both lists): **all 69 mutants killed**. Of the 18 then listed as equivalent, 16
passed and 2 were killed, so they are not equivalent and moved to the mutants: "the source not added" (a faulty piece outside
its source triangle, as in `InjectedCrossingSheet_AndFoldedCopies_AreCaught`, has no box hit, and the check throws instead of
rejecting) and "the slack loosened 2^-40 to 2^-30" (`CoverageTest_UsesTheSlackOfItsCoordinates` bounds the slack from above).
Both are killed in the mutant list as well (checked alone), so the script had 71 mutants, all killed, and 16 equivalent.
The review of #1604 found a third, "size guard off" (above); with its test it is killed, so the script has **72 mutants, all
killed, and 15 equivalent**.

White-box kills: "no slack in Cover" and "the slack loosened to 2^-30" are killed only by `CoverageTest_UsesTheSlackOfItsCoordinates`,
which reads `CoverageSlack`, not by a wrong verdict. A verdict-level kill needs a result triangle whose distance to the exact
result lies between delta - 3 eps and delta + 3 eps at coordinates where eps is above the rounding; no such input is in the tests. The script now first runs the tests on the unmutated checker and stops if they fail (a failing baseline
would count every mutant as killed; it happened once during this work, when a test's threshold was not adjusted to fewer
iterations).

## 2. The checker on Thingi10K

`BooleanComparison certify thingi` (`CertifyThingi.cs`, driver `data/certify_thingi.py`, [benchmark
README](../../benchmarks/BooleanComparison/README.md)) runs `ExactMeshBoolean` with the certificate and `BooleanCertificate.Check`
per case, all three operations, and writes rows only (ids, sizes, verdict, clause, times). Run in this container (the fetch
works through the proxy), one process per case, timeout 900 s, a cap of **20000 triangles per model**. Sample: self-unions
of 20 clean models (`clean_models.csv`, SplitMix64 order, seed 20261125), the first 20 pairs of `pairs.csv` under the cap,
and the first 3 pairs per variant of `contact_pairs.csv` under the cap (79 models).

| Class | Operations | Certified | Uncovered | Removed parts | Rejected | Not applicable | Other |
|---|---:|---:|---:|---:|---:|---:|---|
| self (A op A) | 60 | 60 | 0 | 0 | 0 | 0 | |
| pairs (thingi-pairs) | 60 | 60 | 0 | 0 | 0 | 0 | |
| contact: face-contact | 3 | 3 | 0 | 0 | 0 | 0 | 2 cases with no exact placement (transform error rows) |
| contact: partial-overlap | 9 | 9 | 0 | 0 | 0 | 0 | |
| contact: ulp-shift | 9 | 5 | 1 | 3 | 0 | 0 | |
| contact: ulp-shift-few | 6 | 3 | 1 | 2 | 0 | 0 | 1 case timed out (900 s, section 2.3) |
| contact: mirror | 9 | 6 | 2 | 1 | 0 | 0 | |
| **all** | **156** | **146** | **4** | **6** | **0** | **0** | |

No rejection, so no false alarm and no library defect found by a rejection. "Not applicable" is 0, because all inputs are
closed (clean by `NotCleanReason`). Three kinds of verdict need a diagnosis.

### 2.1 Removed parts (6): documented

In every one of the six, the rounding removed thin components of the exact result (up to 488 parts), and the checker lists
them ("not clean: ..."). This is the documented decision of the rounding (thin and tiny components, 2.4a). It happens in
mirror and ulp-shift contacts, where the difference of nearly coincident solids is a set of slivers. The procedural
counterpart, a capped cylinder minus its mirror image across a plane 1e-8 from its axis, gives the same: thin components of
area up to 6.9, a few 1e-8 thick.

### 2.2 Uncovered (4): incompleteness of C5, no defect

"Uncovered" means: no corner is farther than Delta (no violation), but some triangle is not proven within Delta of the
other mesh after `SplitDepth` = 3 four-way splits. It is inconclusive, which counts as a false alarm for a spike that
certifies without a reference.
- **Procedural repro:** a sphere (`Sphere3Generator_NormalizedCube`, 13 edge vertices) unioned with its mirror image across
  x = 0. The two nearly coincide (ulps apart), and 872 of the union's 2536 triangles are uncovered (348 of the
  intersection's).
- **Not a library defect:** the rounded result lies within 3.7e-9 of the exact result (24 x 24 samples per triangle, both
  directions) against Delta = 3.46e-5.
- **Cause:** the result has fewer triangles than the exact result (2536 against 4008). A result triangle is tiled by several
  coplanar pieces of the exact result, and C5's sufficient condition needs one exact triangle within Delta of each (sub-)
  triangle. A sub-triangle across the boundary of two pieces satisfies it only once it is about Delta small: 12 or more
  splits for these sizes.
- **Deeper splits do not converge.** On the repro, uncovered falls from 872 (depth 3) to 824 (6), 504 (8) and 308 (10), while
  the coverage tests grow from 0.3 M to 21.7 M and the time from 5 s to 350 s. On Thingi10K at depth 10, 2 of the 4
  uncovered operations certify, and 2 keep 2 uncovered triangles each.

Proposal (a checker change, not made here; for the coordinator): a coplanar union cover. The candidates are grouped by exact
plane. A sub-triangle is covered by a group if its corners are within Delta of the plane (the distance to a plane is
affine, so the corners bound it) and its projection lies in the group's union. The containment is exact by areas: the
exact clipped areas of the projection against the group's triangles add up to the projection's area. The pieces are
interior-disjoint (C1 and C2 have certified them before C5 runs), so the sum cannot overcount. This is exact, needs no
new error bound, and its cost is linear in the group.

**Result of #1631 (2026-11-27).** The union cover is in the checker (`Harness/BooleanCertificate.Coverage.cs`), in a more general form than proposed
above: the mirror contact leaves the exact pieces of the two copies in planes that differ by ulps, so grouping by exact plane would split the union
into two groups. Instead, for a triangle t that no single triangle covers: take the chart that drops the coordinate of the largest component of t's
normal; for each candidate triangle G, the region of the chart where G exists and the gap along that axis between t and G is at most Delta is a convex
polygon (the chart of t clipped by G's three edges and two half-planes, exact rationals); every point of t over the region is within Delta of G (the
distance of two points that differ along one axis is that difference). t is covered if the chart minus the union of the regions is empty, or each left
polygon, lifted onto t's plane, is within Delta of one triangle (the old test: convex distance, corners). The difference needs no disjointness of the
exact pieces (convex polygon minus convex polygon, repeated), no error bound and no plane grouping. The sphere and its mirror: uncovered 872 -> 0
(union, 13 edge vertices), 348 -> 0 (intersection), at the default depth 3; 7 edge vertices: 200 -> 0 and 80 -> 0. C5 costs more where it now decides
(13 edge vertices, union, idle-ish 4 cores: 1.2 s -> 4.6 s: 872 triangles decided by exact rationals instead of left to the splits, which fail
and cost 4^3 sub-triangles each). The 4 Thingi10K operations are not checked here (no network in the PR tier): to run `certify thingi` on them.

**Cost of the union cover (2026-11-27, profile and first part).** Sphere and mirror, union, 13 edge vertices: the union cover was 97 % of the
coverage (3.4 to 4.3 s of 4.4 s; regions 1.8 s: per-candidate normal and chart 0.35 s, the three edge clips 0.87 s, of which 86 % of the 30 candidates per
triangle are empty; the difference of the polygons 1.65 s). Done: the charts of the candidates are cached per (candidate, axis), and a candidate whose chart
box (from the approximated corners, margin `FilterEps`) is apart from the box of the triangle's chart is not clipped (the exact clips would find the
region empty, the list of regions is the same; `FastUnionCover = false` is the old path, and the tests compare the two). Result: clipped candidates 26584 -> 12484
at 13 vertices, coverage 3.7-4.2 s -> 2.9-3.1 s (C5 4.4-4.7 s -> 3.6-4.1 s); at 7 vertices 3930 -> 2742 clipped, no time difference (0.66 s). (Measured locally on one machine, not a benchmark: a Stopwatch around `CheckCoverageOnly()` and `CheckResult()` of `SphereAndMirror(n)` in a throw-away test, `FastUnionCover` on and off, two runs each, the second kept; the counts of clipped candidates are exact and asserted by `FastUnionCover_GivesTheResultAndCountsOfTheExactPath_WithFewerCandidates`.) Not done, and why: a
floating-point filter for the signs of the polygon difference (`Side2`), which would save an estimated 40 to 60 % of its 1.65 s. It needs a static error bound
for a new expression in a certificate (proof, review and a mutation run for the bound), for about 0.8 s on a repro that no CI run exercises. The prefilter keeps
12484 of 26584 candidates, 3.5 times the 3591 that survive the exact edge clips: a tighter test (the exact chart polygons against each other in doubles) is the
next step if the cost matters again.

### 2.3 The timeout: quadratic cost of the coplanar stacks, and an output-quadratic boolean

One operation, an ulp-shift-few contact (union) of a model with 6726 triangles, did not finish in 900 s:
- **Boolean:** 12 s alone (`nocheck`; measured with other jobs on the machine).
- **Checker:** C0 0.3 s and C1 5.6 s, then C2 did not finish in 45 minutes. 78k of its 173k pieces are coplanar copies: a
  shift of 2 ulps along x moves the model's faces that are parallel to x within their own planes.
- **Procedural repro:** a capped cylinder (fan caps) unioned with itself shifted by 2 ulps along x. Times on a loaded
  machine:

| Slices | Input triangles | Pieces (copies) | Boolean | C0 + C1 | C2 |
|---:|---:|---:|---:|---:|---:|
| 30 | 120 | 1298 (910) | 0.8 s | 0.1 s | 2.2 s |
| 60 | 240 | 4358 (3610) | 0.8 s | 0.4 s | 5.5 s |
| 120 | 480 | 15878 (14410) | 1.9 s | 1.4 s | 32 s |
| 240 | 960 | 60518 (57610) | 9.5 s | 7.9 s | 210 s |

Two quadratic terms:
- **Checker (a cost defect of the checker):** `PrepareStacks` tests the centroid of every piece, copies included, exactly
  (BigInteger) against every coplanar input triangle whose box meets the piece's. In a fan, the boxes of the long thin
  triangles overlap almost everything, so the box filter of step A removes little, and the step is O(pieces x triangles of
  the plane). Options: a sound double filter in front of `InsideInPlane` (a new error bound, which needs the review of
  error bounds and the `mutation` label), or an exact point location per plane (the pieces of a source tile it, so a sweep
  over the plane's input triangles would do).
- **Boolean (for the coordinator):** two fans shifted within their plane overlay into Θ(n²) pieces (x 3.4 to 3.8 per doubling
  of the slices), and the boolean grows by about x 5 per doubling: 9.5 s at 960 triangles, so above 20 s at about 1,400.
  First read as the size of the exact overlay. The measurement for #1630 shows otherwise: the time grows faster than the
  overlay (pieces x 3.9 per doubling, `classification.stacks` and `classification.winding` x 8), and the union takes 55 s at
  1920 + 1920 triangles (without the certificate). So it is a defect (#1630, with the diagnosis in its comment), not a
  documented limit; design: `docs/design/COPLANAR_PROPAGATION.md` in draft PR #1635.

**Result of #1632 (2026-11-26).** The profile at 240 slices showed three quadratic terms, not one (4 cores, C2 only, milliseconds): the
stacks (`PrepareStacks`) 5.4 s, the pair test of the classified pieces 8.6 s (931,251 pairs) and the straddle test 13.7 s. A double filter in
front of the exact signs decided only 20 % of them: the 2-ulp overlay puts its Θ(n²) pieces in a blob of about 1e-14 of the fan's size around its
centre, below the enclosure of the approximations (2^-49 relative), and many corners lie exactly on the lines. So the fix keeps every sign exact and makes it
cheaper and parallel: an edge form per edge (5 products instead of 21, the same integer as the direct computation), one table per group of
equal pieces (the coplanar copies of one region: the same three corners), no sign for corners of the other triangle that are ends of the edge, the
sides of a pair found once, and the pieces, the pairs and the straddle tests in parallel (the verdict is picked in order afterwards: the lowest piece or pair). Measured on 4 cores
(before from the table above, after on an idle machine):

| Slices | Classified pieces | Pairs | Boolean (recording) | C2 before | C2 after |
|---:|---:|---:|---:|---:|---:|
| 30 | 1298 | 15,935 | 0.07 s | 2.2 s | 0.20 s |
| 60 | 4358 | 54,911 | 0.4 s | 5.5 s | 0.6 s |
| 120 | 15,878 | 219,491 | 1.7 s | 32-34 s | 2.8 s |
| 240 | 60,518 | 931,251 | 12 s | 210 s | 17.7 s |

The cost is still quadratic per plane (all boxes of a fan overlap); only the constant fell (12x at 240), so C2 is now 1.5 times the boolean at 240 slices
instead of 18 times. A near-linear C2 needs an exact point location per plane. C5 of the same repro grows faster than C2 now (69 s at 120 slices, 5 s at 60): not
part of #1632, noted here for the coordinator.

Attribution of the two slow cases (review of #1604):
- **The C2 run of more than 45 minutes** on the Thingi10K model is the checker's `PrepareStacks` cost above, #1632: the same
  O(pieces x coplanar triangles) shape as the library's coplanar stacks of #1630, in the checker's own code.
- **The boolean above 20 s at about 1,400 triangles** (projected) is #1630 itself. Measured since: 10.2 s at 960 + 960
  triangles and 55 s at 1920 + 1920. The 12 s boolean of the Thingi10K model (a 2-ulp shift that moves faces within their planes)
  has the same mechanism, coplanar copies, but was not profiled separately; no separate cause is known, so no further issue.

### 2.4 Cost against size

Checker time against the input triangles (A + B). Measured on 4 cores with other jobs running (the hand-mutant runs), so
the absolute times are high; the ratios are less affected:

| Input triangles | Operations | Boolean, median | Checker, median | Checker, max | Checker / boolean, median |
|---|---:|---:|---:|---:|---:|
| < 5000 | 39 | 0.27 s | 2.3 s | 7.2 s | 8.4 |
| 5000-10000 | 45 | 0.37 s | 2.1 s | 6.0 s | 6.0 |
| 10000-20000 | 42 | 0.65 s | 5.1 s | 61 s | 12.3 |
| 20000-40000 | 30 | 1.0 s | 5.9 s | 57 s | 6.2 |

By class, the checker / boolean ratio (median, max) is:
- self-unions: 2.7, 5.4 (at most 7.3 s);
- pairs: 22, 65 (at most 23 s);
- contacts: 18, 66 (at most 61 s).

Above 20 s: one pair, a 19388-triangle model with a 1814-triangle one, at 23 s. There C2 takes 8.5 s with 175k coplanar
pairs among 403k, and C5 11.5 s. The rest are the mirror and ulp-shift contacts (30-61 s), where coplanar copies and
coplanar pairs dominate.

## 3. Summary for the coordinator

- **Mutants:** 19 new mutants of the filters; the 8 that survived the old tests are killed by 3 new tests and a wider
  existing one. Full re-run: 71 of 71 killed (2 moved from the equivalent list, which the tests kill); the review of #1604
  moved a third ("size guard off") with a new test: 72 killed, 15 equivalent mutants with reasons, all pass; one belt (the clamp
  of the weights) not proved equivalent.
- **Thingi10K:**
  - 156 operations, 146 certified, 0 rejected (no false alarm), 0 not applicable.
  - 6 with removed parts: documented.
  - 4 uncovered: the C5 incompleteness, with a procedural repro and a proposal.
  - 1 timeout: the checker's quadratic stacks, with a procedural repro.
- **Library:** no defect shown by a rejection. One potential defect by the 20 s rule, now #1630 (repro in 2.3): the boolean
  on coplanar fans shifted by ulps is worse than its output, by two cubic stages (coplanar stacks and winding).
