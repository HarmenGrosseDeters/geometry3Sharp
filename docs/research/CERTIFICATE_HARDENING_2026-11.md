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
2^-480). The script now has 69 mutants and a list of 18 equivalent ones; `--equivalent` runs those, and each must pass.

The new mutants (24 in the first round, 19 of them kept as mutants): each makes a filter decide wrongly or too early.

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
  widening loosened; always or never the tree; the source not added. Either side's widening of 2^-49 alone covers both
  boxes' errors, boxes that touch exactly after widening are apart before it, and the other changes only change the cost.
- **C5 margins:** loosened by a factor 4; the slack loosened to 2^-30, or taken without the extent (extent <= 2 maxAbs); the
  floor at 2^-440 or 2^-520. A larger slack only hands more cases to the exact test. A floor of 2^-520 is still sound: the
  only underflow-sensitive step is the final length of p - q, which loses about 2^-1075 / d absolute, and an accept needs
  delta >= 3 floor; the floor has to stay above about 2^-539.
- **C5 approximation:** the size guard alone, or the finiteness check alone. Each covers what the other would catch;
  removing both is a killed mutant.
- **C5 NaN weights:** a NaN weight gives a NaN distance through `Math.Min` / `Math.Max`, and no accept takes it.

One survivor that is **not proved equivalent**: removing the clamp of the closest-point weights to [0, 1] ("weights not
clamped"). The step-A note calls the clamp a belt. It survives the thin-triangle test whose summary claimed to kill it, and
that summary is corrected. A targeted search (1.2 million near-collinear cases, the double routine against an exact
`Fraction` reference, `ScratchUncoveredTests` not committed) found the weights outside [0, 1] only in 1-ulp roundings of
v + w, never with an unsound accept. It is listed as equivalent in practice. An exact argument (the face region with a wrong
sign of va, vb or vc only near region boundaries) is open.

Run (4 cores): the full run of all 69 mutants and the check of the 18 equivalent ones are under way; their result follows in
this section. The script now first runs the tests on the unmutated checker and stops if they fail (a failing baseline
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
  This is the size of the exact overlay, which an exact arrangement has to build. I lean to "outside the supported range,
  a documented limit" ([docs/QA.md](../QA.md) 2.5a: scale disparity in one plane). Still, the 20 s rule makes it a
  potential defect that needs a decision.

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
  existing one (the full re-run of all 69 follows in section 1); 18 equivalent mutants with reasons; one belt (the clamp
  of the weights) not proved equivalent.
- **Thingi10K:**
  - 156 operations, 146 certified, 0 rejected (no false alarm), 0 not applicable.
  - 6 with removed parts: documented.
  - 4 uncovered: the C5 incompleteness, with a procedural repro and a proposal.
  - 1 timeout: the checker's quadratic stacks, with a procedural repro.
- **Library:** no defect shown by a rejection. One potential defect by the 20 s rule: an output-quadratic boolean on
  coplanar fans shifted by ulps (repro in 2.3), which needs a decision.
