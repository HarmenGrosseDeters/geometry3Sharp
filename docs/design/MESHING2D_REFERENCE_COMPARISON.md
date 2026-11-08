# Meshing2: comparison with the reference producer (rows only)

Analysis of the coordinator's comparison of our golden rows with rows of a reference producer (the owner's Triangle.NET fork at
504ab65b29f5, run as a black box by a separate session; golden format, `MESHING2D_GOLDEN_FORMAT.md`). The implementing session saw
only the rows the coordinator relayed, never the reference's code or repository (clean room). Items 2 to 5 of the coordinator's
summary of 2026-11-02; item 1 (M1: 28 of 28 rows identical, triangle fingerprints included) needs nothing.

## 2. f-1/c: the reference rounds a crossing one ulp off

Both rows have 12 vertices, 15 triangles, 15 subsegments, the same area and minimum angle, and 2 constructed vertices, but different
vertex, triangle and subsegment fingerprints. The two crossings, exactly:

| Segments | Exact crossing | Correctly rounded (ours) |
|---|---|---|
| (5, 0)-(1, 10) and (7, 7)-(0, 7) | (11/5, 7) | (2.2, 7) = (0x400199999999999a, 0x401c000000000000) |
| (5, 0)-(1, 10) and (3, 3)-(8, 8) | (25/7, 25/7) | (0x400c924924924925, 0x400c924924924925) |

Our output is these correctly rounded values (D1). The vertex fingerprint is a hash of the sorted coordinate bits, so the reference's
value can be found by trying the doubles near the exact crossings: the reference's `vertices_fp` (33699f85...) is reproduced exactly
when the x coordinate of the second crossing is one ulp below the correctly rounded one, 3.571428571428571 (0x400c924924924924), with
y correct. So the reference does not round that crossing correctly (an error of about 0.6 ulp, as from evaluating the crossing along
one segment's parameter in doubles); the different triangle and subsegment fingerprints follow from that vertex. No change on our side.

## 3. M3: more points than the reference on small inputs

| Case | Ours: V / T, min angle | Reference: V / T, min angle |
|---|---|---|
| f-40/angle-33.75 | 156 / 270, 34.2° | 80 / 130, 33.8° |
| f-44 | 28 / 24, 33.7° | 12 / 12, 33.7° |
| f-48/a, b, c | 15 / 16, 33.7° | 11 / 12, 33.7° |
| f-23/angle-0, 20 | 175 / 300, 33.1° | 170 / 313 (0.2°), 173 / 312 (23.8°) |
| f-23/angle-30 | 174 / 298, 31.5° | 175 / 303, 30.7° |
| f-34 (area only) | 974 / 1818, 29.4° | 833 / 1613, 0.2° |
| f-38/a, b | 3 / 1 (one bad triangle) | 15 / 13 and 21 / 19 (9 and 15 bad) |

Measured on our side:

- **Not conforming Delaunay (O2):** the same counts with `Conforming = false`; quality refinement alone produces them.
- **The extra points lie on segments**, not in the interior: on f-44 (the frame between the outer square and the hole) the midpoints
  of all eight sides and the shell points at 2 and 8, sixteen in all; the reference's 12 triangles mean four boundary points.
- **Cause: encroachment by diametral circles.** We split a subsegment when a vertex sees it at more than 90° (inside its diametral
  circle, Ruppert 1995). With diametral lenses instead (more than 120°; L. P. Chew 1989, J. R. Shewchuk, *Delaunay refinement
  algorithms for triangular mesh generation*, CGTA 2002, who reports fewer vertices with lenses), an experiment (not committed) gives
  f-44 12 triangles, f-48 12, f-23 314, f-34 1571: the reference's counts, all accepted by the oracle (conforming included). But
  f-40 at 33.75° then runs into the Steiner limit (200 032 vertices, min angle 10.6°): lenses alone do not terminate at so high a
  bound. The reference reaches 33.75° with half our points, which off-centres (A. Üngör, *Off-centers*, LATIN 2004; size-optimal
  meshes with fewer Steiner points at high angle bounds) would explain; that is an inference from the counts, not knowledge of its
  code.
- **f-38/a, b (a triangle with an input angle of 0.9° and 0.11°):** we leave the single triangle, reported bad (the small input angle
  is forced, O3); the reference refines to 13 and 19 triangles and leaves 9 and 15 bad. Both are valid; ours is the smaller result.
- **f-34 (area only):** our circumcentre insertion also keeps the angles at 29°; the reference leaves 0.2°.

Not a defect. **Proposal** (owner's call): diametral lenses where the bound allows them (with circles kept above about 26°, or
together with off-centres), measured on the catalogue and the corpus; expected up to half the Steiner points on small inputs.

**Measured (2026-11-02, the off-centre PR), with lenses active under conforming Delaunay (the M3 candidate) and master's oracle:**

| | circles (master) | off-centres | lenses | lenses + off-centres |
|---|---|---|---|---|
| catalogue M3 rows passing | 55 / 55 | 55 / 55 | 46 / 55 | 46 / 55 |
| catalogue vertices (sum) | 391 214 | 391 549 | 748 809 | 748 993 |
| catalogue Steiner-limit hits | 0 | 0 | 2 (f-40 at 33.75, 34.5) | 2 |
| corpus passing at 20 / 25 / 30 / 33 degrees | 83 / 81 / 82 / 77 | 82 / 82 / 82 / 78 | | 34 / 32 / 36 / 25 |
| corpus Steiner-limit hits at 20 / 25 / 30 / 33 | 1 / 1 / 1 / 6 | 1 / 1 / 1 / 6 | | 1 / 1 / 1 / 41 |

- **Lenses break O2.** The failing catalogue rows (F-4/a, b, c, F-5, F-18/b, F-26, F-27, F-39, F-42) and almost all failing corpus runs
  violate conforming Delaunay (O2, empty circumcircles). The four small rows above were the exception.
- **Lenses save points, but only where they pass.** Examples: F-26 909 -> 191, F-4/c 37 568 -> 17 899.
- **Only water-huge fails without lenses.** The single violation in the circle and off-centre columns is earcut's water-huge, an oracle
  gap closed separately (O11-unsplittable pieces).

**Owner decision 2026-11-02, confirmed on these numbers: no lenses (option a); off-centres go ahead.** Off-centres save 0.6 to 0.9 %
of the vertices on the corpus at 20 to 30 degrees and are mixed at 33. Lenses for `Conforming = false` remain a possible later item.
The corpus shows the same pattern: poly2tri's deadly_quad, a quadrilateral of 4 points, gets 5088 vertices at 0° (quality without an
angle bound, conforming).

## 4. f-41/a: no golden row because it is on the slow list, which is out of date

`Catalogue.SlowRows` lists F-41/a, so the golden master leaves it out (golden rows are recorded for rows that are not slow). It is
fast now: 16 384 triangles, mesh 0.56 s, with the oracle 1.8 s. Several other "slow" rows are fast too (oracle included):

| Row | Total | Row | Total |
|---|---|---|---|
| F-4/a | 1.1 s | F-26, F-27 | 0.25 s |
| F-4/b | 2.9 s | F-37 | 2.9 s |
| F-4/c | 19.3 s | F-38/d | 3.6 s |
| F-5 | 1.0 s | F-38/e | 0.08 s |
| F-22/a | 8.2 s | F-40/angle-40 | 14.7 s |
| F-41/a | 1.8 s | F-42 | 4.0 s |

**Proposal:** keep only rows above about 5 s on the list (F-4/c, F-22/a, F-40/angle-40 and the F-14 rows of `m2.snap`) and record
golden rows for the others with the reason "no longer slow".

## 5. Set E: our mesher on the 92 public inputs

Set E is the corpus (earcut 61, delaunator 7, poly2tri 16; fetched at run time, `corpus/fetch.py`) plus 8 inputs fetched at run time
from the same pinned commits (nothing committed): delaunator issue13; poly2tri deadly_quad, polygon_test_01 to 03, stalactite,
steiner; CDT (artem-ogre/CDT, MPL-2.0, data only) issue-211, a point set with 98 constraint edges whose indices are 1-based.

Each input runs at M2 (M1 for point sets) and at M3 with minimum angles 0°, 20° and 30°, judged by the oracle (`corpus`; results
above 60 000 vertices are not judged):

| Inputs | Runs | Passed the oracle | Not judged (size) | Failed |
|---|---|---|---|---|
| the corpus, 84 | 336 | 332 | 4 | 0 |
| the 7 further earcut-format inputs | 28 | 28 | 0 | 0 |
| CDT issue-211 | 4 | 3 | 0 | 1 (below) |

The four runs not judged: earcut's water-huge3 at 30° (114 263 vertices) and poly2tri's debug2 at all three angles, where refinement
reaches the Steiner limit (310 000 vertices; 10 000 points on a few lines). Two performance notes: judging debug2 at M2 (a ring of
10 000 vertices) took the oracle about 50 minutes, the mesher 70 ms; and deadly_quad (above).

- **A real defect, CDT issue-211 at M3, 20°:** 73 594 vertices, no Steiner limit, 7 triangles below 20° with no exemption, in a
  refinement cascade towards a Steiner point at about (-0.00127, -0.00157) with vertices 1e-10 apart (coordinates of 1e-3: far from
  the precision limit). M2, M3 at 0° and at 30° pass. To be fixed in its own pull request (`bugfix`), with the input as a generated
  test case if its geometry can be reduced, since the file itself is not committed.
- The reference's 15 outputs the oracle rejects (self-crossing or self-touching rings, a segment inside a hole) are deviations of
  the reference from D5 and the ring semantics, not targets.

## 6. Big run of 2026-11-05: master d9006aa against the reference

The reference session ran 6000 cases on one machine (2026-11-04 20:36 to 2026-11-05 21:49 UTC, 3 cases in parallel), every output
judged by our oracle of the same master:

- **The 4869 cases of the run of 2026-11-03** (master f3d92da), as they are: section 9 of the golden format and every prepared public
  input.
- **1131 M3 variants:** every golden case and a stratified corpus sample, each at minimum angles of 20, 25 and 30 degrees with
  `steiner_limit(200000)`, meshed in default mode and with `mode constrained`.
- **Pairs, by triangulation type** (corrected 2026-11-06: the first version of this section paired by mode name, which crossed the
  types). Our default is conforming Delaunay and `mode constrained` is constrained Delaunay; the reference's default output is
  constrained Delaunay and its second mode conforming. For the angle variants the pairs are now conforming / conforming and
  constrained / constrained; the reference's constrained output is judged against the input with `mode constrained`.
- **The cases as they are stay crossed:** the run has only our conforming default and the reference's constrained default for them,
  no conforming run of the reference, so their rows compare different triangulation types and cannot be matched without a re-run.
- **Limits:** 600 s per mesher. The oracle had 600 s per call until 13:59 UTC and 120 s after (owner decision: the slow outputs are the
  GEOS overlay variants at the Steiner limit, where the oracle took 89 % of the wall time).
- **Restarts:** the container restarted four times; the cases in flight then were run again.

**Cases as they are (4869).**

- **Ours:** 4862 VALID, 6 NO-OUTPUT, 1 INVALID. The NO-OUTPUT cases are GEOS TestValid 37, 38, 39, 40, 44 and 49, whose inputs the
  oracle calls invalid in the reference's output too. The INVALID case is f-17/b, CannotJudge for every producer.
- **Against 2026-11-03,** 8 verdicts changed, all for the better:
  - 7 BoundaryNotConstrained to VALID: TestOverlay-isochrone 49, 53, 54, 70 and TestOverlay-stmlf 1040, 1304, 1579;
  - 1 ORACLE-TIMEOUT to VALID: TestOverlay-shapely-829/1.

  None got worse, and no case has more than 10 % more triangles.

| Pair | Both valid | Only ours valid | Only the reference valid | Neither |
|---|---|---|---|---|
| as they are: ours conforming, reference constrained (crossed) | 4803 | 59 | 0 | 7 |
| the same, 2026-11-03 | 4661 | 193 | 0 | 15 |
| angle variants, conforming / conforming | 575 | 326 | 3 | 83 |
| angle variants, constrained / constrained | 792 | 253 | 9 | 77 |

The reference's output is the same as on 2026-11-03 (same commit). 142 of its verdicts went from INVALID to VALID with the newer
oracle, which accounts for most of the drop in "only ours valid".

Ratios, ours / reference, over cases where both are VALID (5 % / median / 95 %; the time is the mesher alone, cases under 5 ms left
out, and the times are load-dependent):

| Set | Triangles | Time |
|---|---|---|
| as they are (crossed) | 0.88 / 1.00 / 1.00 | 0.90 / 1.38 / 2.14 |
| the same, 2026-11-03 | 0.90 / 1.00 / 1.00 | 0.85 / 1.29 / 2.07 |
| angle variants, conforming / conforming | 0.60 / 1.00 / 2.21 | 1.05 / 1.62 / 4.08 |
| angle variants, constrained / constrained | 0.43 / 1.03 / 2.61 | 0.97 / 1.72 / 5.00 |

**Flags.**

- **m1/subnormal-32 at 20, 25 and 30 degrees, both modes: fixed by #388 (4d784f3a).**
  - In this run the outputs were INVALID with BoundaryNotConstrained (10 to 26), SubsegmentUnsourced, TriangleOutsideDomain (3 to 12)
    and AreaMismatch, while the reference had AngleBelowBound only (14 / 20 / 34).
  - Re-run at 4d784f3a, mesher and oracle: AngleBelowBound only, 6 / 8 / 14 in default mode (73 / 94 / 116 triangles) and 5 / 7 / 14
    constrained.
- **Only the reference valid** (angle variants, matched pairs; 3 conforming, 9 constrained):
  - conforming: TestOverlay-stmlf/1326 at 25° (AngleBelowBound(1)); TestOverlay-stmlf/1044 at 20° and 25° (ours ORACLE-TIMEOUT);
  - constrained: TestOverlay-stmlf/1326 at 20° and 25°, TestOverlay-stmlf/850 at 25° and 30° (AngleBelowBound, 1 or 2 each);
    stmlf-20061020/10 at 20°, TestOverlay-stmlf/473 at 20°, 638 at 20° and 25°, 1044 at 30° (ours ORACLE-TIMEOUT).
- **AngleBelowBound where the reference fails as well:** issue-geos-434/1 and 17 TestOverlay-stmlf variants at 20 and 25 degrees,
  mostly 1 to 8 angles. The worst are TestOverlay-stmlf/1529 and 1875 constrained, with up to 200.
- **84 of our outputs ORACLE-TIMEOUT** (44 default, 40 constrained), all angle variants:
  - f-12/b;
  - GEOS TestBufferMitredJoin/2 and TestBufferExternal-2/92;
  - GEOS overlay cases at the Steiner limit.

  Re-judged with the oracle of e2a0bb99 (1800 s), a sample of 12 of them is VALID in every case, 141 s to 1296 s each, 90 to 98 %
  of it in the conformity phase; the other 72 were not re-judged.
- **Triangle-count outliers:** 257 outputs have more than 1.5 times the paired reference output's triangle count (253 angle
  variants, 4 crossed cases as they are), 185 of them where the reference has at least 100 triangles. The largest:

| Case | Pair | Ours | Reference | Ratio |
|---|---|---|---|---|
| TestOverlay-stmlf/1461, 30° | constrained | 390 342 | 15 575 | 25.1 |
| TestOverlay-stmlf/1461, 20° | constrained | 136 960 | 6 993 | 19.6 |
| TestOverlay-stmlf/1461, 25° | constrained | 197 163 | 10 141 | 19.4 |
| TestOverlay-stmlf/1461, 30° | conforming | 390 342 | 27 818 | 14.0 |
| TestOverlay-stmlf/1461, 25° | conforming | 244 848 | 22 907 | 10.7 |
| ext/cdt/island, 30° | constrained | 8 879 | 929 | 9.6 |
| TestOverlay-stmlf/1461, 20° | conforming | 159 985 | 20 131 | 8.0 |
| ext/cdt/cdt, 30° | constrained | 20 699 | 3 613 | 5.7 |
| TestOverlay-stmlf/1818, 30° | constrained | 2 910 | 582 | 5.0 |
| safe-16595/17, 30° | constrained | 2 016 | 408 | 4.9 |
| ext/cdt/cdt, 30° | conforming | 20 699 | 4 426 | 4.7 |
| issue-geos-838/1, 30° | constrained | 75 920 | 17 857 | 4.3 |

## 7. Targeted QA recheck, 2026-11-08

After #392 and #394–#396, a new run re-meshes the same historical inputs on
`a482d490`: all 79 AngleBelowBound operations and all 84 oracle-timeout operations.
It does not re-judge the old meshes or rerun reference producers. Matched pairs
follow #393; all base hashes and 79 available historical variant hashes match.

| Historical class | Mode | Operations | VALID | INVALID | Oracle timeout | Mesh timeout | Errors | VALID at Steiner limit |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| AngleBelowBound | conforming | 39 | 0 | 39 | 0 | 0 | 0 | 0 |
| AngleBelowBound | constrained | 40 | 2 | 38 | 0 | 0 | 0 | 0 |
| Oracle timeout | conforming | 44 | 24 | 0 | 20 | 0 | 0 | 24 |
| Oracle timeout | constrained | 40 | 31 | 4 | 4 | 1 | 0 | 9 |

The angle group has 77 remaining INVALID outputs and two now VALID; six of the
77 also violate local/global Delaunay. Among the twelve historical reference-only
valid operations, five are now valid (two at the Steiner limit), five still have
angle violations and two time out. These remain explicit findings, not relaxed
oracle rules or new failing golden baselines.

Among the historical timeouts, 4 current outputs are now judged
INVALID; their case IDs and violations are listed in the complete report. An
old timeout has no valid verdict to regress from, so this is a newly resolved
failure finding, not proof that a previously valid output got worse.

One current timeout is measured inside Conformity before Delaunay/quality:
301453 triangles, about 9 billion point/triangle and 22 billion edge-pair
bounding-box candidates after shared-corner exclusions. These are candidate
overlaps, not actual intersections. The finer subphase remains unprofiled.

See the [complete report, per-operation evidence and commands](../../benchmarks/Meshing2Benchmark/rechecks/results/2026-11-08/README.md).
This targeted bounded run is not a whole-corpus no-regression or performance
claim; the changed stage budgets, reached Steiner limits and single-run resource
contention are documented there. The acceptance batch returns 1 for its recorded
unresolved findings. Separate CI coverage adds 52 constrained M3 golden rows
under the unchanged independent oracle; the previous mode baselines are preserved.
