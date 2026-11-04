# 2D meshing: grading

The quality mesher (`QualityMesh2`, M3) met its angle and size bounds but used 2 to 2.5 times the triangles of the reference mesher
on cases that grade over many octaves (f-11/a, f-11/b, f-40 at 33.75°). The owner's target (2026-11-03) is at most 1.25 times the
reference on those cases and on a corpus sample at the same angle bound, with no verdict worse. This note records the diagnosis and
the first step. The 1.25× target is **still open** (ROADMAP).

## 1. Diagnosis (master de29dada, 2026-11-04)

| case | default | constrained quality (cq) | reference |
|---|---|---|---|
| f-11/a | 256 | 286 | 108 |
| f-11/b | 456 | 451 | 205 |
| f-40, 33.75° | 276 | 276 | 130 |

- **Uniform, not local.** On f-11 (a cluster 2^-20 wide at the centre of a unit square) we place about 15 triangles in every octave of
  distance from the cluster, the reference about 5 to 6. The insertion radius of each new vertex over the shortest edge of its triangle
  follows the textbook bound: at least 1/(2 sin θ) = 1.46 at 20° for circumcentres, about 2.9 for off-centres. Segment cascades are
  ruled out: f-11/a needs 8 subsegment splits against 120 triangle insertions.
- **Single levers move ±16 %.** These are switching off-centres off, changing the off-centre factor, using off-centres for every
  triangle, and diametral lenses on or off.
- **cq churn.** In the constrained-quality mode, lens deletion (A) deletes free vertices before bisections, and they are then inserted
  again. On f-11/a, 128 of 265 insertions were deleted again. The churn costs +3 to +8 % triangles over the default mode on these cases.

## 2. Step 1: worst first, and the best of a few points on the bisector

Two changes in `QualityMesh2.Refiner`. Both are on by default whenever there is an angle bound (`MinAngle > 0`). Without an angle bound
(size refinement only) the order and the split points stay as before.

**Worst first** (`TriangleQueue`). The queue of bad triangles was FIFO. It is now a binary heap keyed by the sin² of the triangle's
smallest angle at the time it is queued (`MinSin2`: computed in the triangle's frame scaled by a power of two, so it does not depend on
scale). Ties go in the order queued, so the order is deterministic. Only triangles below the angle bound go into the heap; the others
(bad by size only, or not bad: most of the triangles queued) wait in a FIFO behind them, in the order queued as before. A heap of every
triangle queued cost a quarter of the mesher's time on the largest Herne run. An entry may be stale: its slot may have been refilled, or freed by
a deletion. The main loop judges the triangle as it is when taken out, as it did with the FIFO.

**Best candidate** (`BestCandidate`, `Score`). For a triangle bad by its angle, the split point is chosen from these candidates on the
perpendicular bisector of its shortest edge pq:

- the default point (the off-centre if there is one, otherwise the circumcentre);
- the circumcentre;
- 6 points evenly spaced from the nearer of the off-centre and the circumcentre out to 1.3 times the circumcentre's distance from the
  midpoint of pq.

On the snap grid the default point is the pixel centre refinement inserts without the choice (the circumcentre's decided
exactly), and every other candidate is rounded to its pixel centre. Duplicates are dropped. A candidate counts only if it would be
inserted as it is:

- it can be reached from the triangle without crossing a constraint (`Walk`);
- it encroaches on no subsegment (`EncroachedByPoint`);
- the triangle lies in its cavity;
- on the grid, no constraint crosses its pixel (`ConstraintInPixel`).

The score is the number of triangles of the domain bad by their angle in the candidate's cavity, minus the number of new triangles in
its fan that are below the angle bound. Ties go to the larger insertion radius (the distance to the nearest vertex of the fan), then to
the earlier candidate. When the default point wins, nothing is chosen and refinement goes on as before. The fan's count uses the angle
bound only: a new triangle at a small input angle counts as bad although refinement would leave it, which skews the score but not the
mesh. The fan's angles and distances are computed in the triangle's frame scaled by a power of two, around the
candidate. So an input scaled by 2^k gives the same choice bit for bit; the tests check 2^±10, 2^-500 and 2^200, with and without the
grid, in both modes.

**The floor and termination.** A candidate's insertion radius must be at least the default point's. So no chosen point is nearer to the
mesh than the point refinement would insert without the choice, and the insertion-radius argument for termination (Ruppert 1995;
Shewchuk 2002; Üngör 2004 for off-centres) bounds the chosen point as it bounds the default one. When the default point does not count
(it is blocked, encroaches, or its pixel holds a constraint), nothing is chosen. Refinement then goes on as before: it splits the
subsegment, or falls back to the circumcentre. Termination stays **measured, not proven**, as for lenses, off-centres and the grid
(MESHING2D_PROVENANCE.md). The floor keeps the argument's key quantity no worse than before. Without it and without the default candidate (the probe) the counts were lower,
for example 193 instead of 246 on f-11/a cq and 224 instead of 249 on f-40, but there was no bound at all.

The first probe had 12 candidates and no default candidate. It reached about 1.8× the reference, a little better than this step, but
had neither the floor nor the default candidate. The coordinator chose the 6-candidate variant with both for the first PR (2026-11-04).

### Results

Mesh triangle counts, before (master de29dada) → after. Every verdict is unchanged except one, which is better.

| case | default | cq |
|---|---|---|
| f-11/a | 256 → 217 | 286 → 222 |
| f-11/b | 456 → 379 | 451 → 392 |
| f-40, 33.75° | 276 → 245 | 276 → 245 |

Corpus (`corpus --dir`, both modes), triangles summed per milestone:

| run | default | cq |
|---|---|---|
| M3@0 | ±0 | ±0 |
| M3@20 | 532 194 → 524 373 (−1.5 %) | 432 071 → 429 630 (−0.6 %) |
| M3@25 | 612 291 → 596 245 (−2.6 %) | 473 458 → 466 532 (−1.5 %) |
| M3@30 | 901 634 → 820 153 (−9.0 %) | 636 123 → 596 579 (−6.2 %) |

- Some inputs get more triangles: 8 to 13 per set, against 37 to 52 that get fewer.
- One verdict changes: earcut/water-huge3 M3@25 default goes from unjudged to ok.
- Catalogue (`rows`, 55 rows): 761 804 → 750 907 triangles (−1.4 %) default and 705 204 → 696 356 (−1.3 %) cq. No verdict changes.

Herne (12 ALKIS runs, snap 0.01 m: every candidate on the grid). Mesher time is `mesh --no-oracle`, a single run each, on one
machine. All runs are VALID with no violation, so RefinementPinch and RefinementCoincidence are 0:

| run | verdict / violations | triangles | mesher ms | oracle s |
|---|---|---|---|---|
| base_371800_5710600_q20s | VALID/0 → VALID/0 | 21262 → 20912 (-1.6 %) | 1597 → 1926 (+21 %) | 4.6 → 4.8 |
| base_371800_5710600_q30s | VALID/0 → VALID/0 | 59021 → 49835 (-15.6 %) | 1951 → 2159 (+11 %) | 6.3 → 6.1 |
| base_373600_5709000_q20s | VALID/0 → VALID/0 | 93810 → 91827 (-2.1 %) | 3799 → 4613 (+21 %) | 29.2 → 27.4 |
| base_373600_5709000_q30s | VALID/0 → VALID/0 | 245104 → 206700 (-15.7 %) | 5216 → 7707 (+48 %) | 58.3 → 51.7 |
| base_381000_5710400_q20s | VALID/0 → VALID/0 | 5708 → 5384 (-5.7 %) | 1056 → 961 (-9 %) | 3.7 → 2.9 |
| base_381000_5710400_q30s | VALID/0 → VALID/0 | 15042 → 12990 (-13.6 %) | 1159 → 1480 (+28 %) | 3.8 → 3.7 |
| cq_371800_5710600_q20s | VALID/0 → VALID/0 | 12523 → 12017 (-4.0 %) | 1534 → 1573 (+3 %) | 3.8 → 3.8 |
| cq_371800_5710600_q30s | VALID/0 → VALID/0 | 59021 → 49835 (-15.6 %) | 2134 → 2659 (+25 %) | 6.0 → 6.7 |
| cq_373600_5709000_q20s | VALID/0 → VALID/0 | 62590 → 61159 (-2.3 %) | 3217 → 3734 (+16 %) | 14.3 → 13.4 |
| cq_373600_5709000_q30s | VALID/0 → VALID/0 | 245104 → 206700 (-15.7 %) | 5830 → 7802 (+34 %) | 38.4 → 35.3 |
| cq_381000_5710400_q20s | VALID/0 → VALID/0 | 3901 → 3549 (-9.0 %) | 774 → 821 (+6 %) | 2.4 → 2.5 |
| cq_381000_5710400_q30s | VALID/0 → VALID/0 | 15042 → 12990 (-13.6 %) | 1134 → 1495 (+32 %) | 3.4 → 3.9 |
| total | | 838128 → 733898 (-12.4 %) | 29401 → 36930 (+26 %) | 174.2 → 162.2 |

### Cost

The mesher is 26 % slower in total on Herne, from −9 % to +48 % per run; the worst case is the largest run, 373600 q30. On the
smaller corpus runs the difference is within noise. The oracle gets 7 % faster, because it has fewer triangles to check.

- **Worst first** costs +5 to 8 % on its own.
- **The choice** costs the rest: up to 8 walks and cavity searches per split of a triangle bad by its angle. The searches use
  generation-stamped arrays per triangle slot. Sets that were reused had to zero all their buckets on every clear, and that was most of
  the choice's time in the first profile.
- **Lazy checks.** The checks that can only refuse a candidate (encroachment, the pixel) run only for a candidate that would win.
- **Shared work.** Each triangle's angle test is shared by the overlapping cavities of one choice, and the exact pixel of the
  circumcentre is computed once per split.

The remaining cost is the cavity search itself, which the score needs. Cheaper scores (fewer candidates for triangles just below the
bound, or a bound on the score that prunes candidates early) change the result and belong with the next grading step.

## 3. Next steps (open)

- **A 2D candidate region.** Erten and Üngör choose the Steiner point in a region around the circumcentre, not only on a line, by a
  local optimality criterion (H. Erten, A. Üngör, *Quality triangulations with locally optimal Steiner points*, SIAM J. Sci. Comput.
  31(3), 2009; **from memory, not re-read**: its host was blocked in this session). Next after this step, once the paper can be read.
- **The bad-triangle criterion.** At present a triangle is split when its smallest angle is below the bound. A criterion that also
  weighs the edge-length ratio, so that triangles slightly below the bound next to good ones are left to their neighbours' splits, is
  to be measured.
- **The cq churn.** Lens deletion (A) and grading interact: points chosen here may be deleted again by a later bisection. The score
  could count the free vertices in the diametral circles of nearby subsegments, or deletion could spare points above some insertion
  radius.
- **Snap speed and unsplittable subsegments.** These are separate items on the ROADMAP.
