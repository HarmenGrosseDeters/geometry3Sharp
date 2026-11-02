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
