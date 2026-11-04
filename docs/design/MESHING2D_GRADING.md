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

- **A 2D candidate region.** Erten and Üngör choose the Steiner point in a region, the petal of the shortest edge, not only on a
  line. The conference version (SGP 2007) was read on 2026-11-04; see section 4.3.
- **The bad-triangle criterion.** At present a triangle is split when its smallest angle is below the bound. A criterion that also
  weighs the edge-length ratio, so that triangles slightly below the bound next to good ones are left to their neighbours' splits, is
  to be measured.
- **The cq churn.** Lens deletion (A) and grading interact: points chosen here may be deleted again by a later bisection. The score
  could count the free vertices in the diametral circles of nearby subsegments, or deletion could spare points above some insertion
  radius.
- **Snap speed and unsplittable subsegments.** These are separate items on the ROADMAP.

## 4. Step 2: design (paper-independent; draft of 2026-11-04, no code yet)

The two targets pull against each other. Quality should move toward 1.25× the reference (section 3). Speed has a stop rule from the
coordinator (2026-11-04): the mesher back to at most +10 % of pre-grading on Herne, or a stated reason why not.

The agreed order (coordinator 2026-11-04):
1. (A), the union cavity, with its equivalence argument;
2. a measurement;
3. then (B);
4. (C) and (D) only with numbers.

The code for (A) waits for the owner's decision on the order of the fast-oracle steps.

The 2D candidate region (4.3) follows Erten and Üngör, read on 2026-11-04 in its SGP 2007 version (the journal version of 2009 is
paywalled and not read).

### 4.1 Speed: what the choice costs, and four ways to cut it

Profile of step 1 on Herne 373600 q30 (dotnet-trace, final code):

- The choice (`BestCandidate`) is about 24 % of refinement time.
- Of that, `Score` is 45 %. This is the cavity search per candidate: a walk, a breadth-first search with exact InCircle, and the fan.
- The exact pixel of the circumcentre is 12 %. The default path needs it anyway.
- The refusal checks (`EncroachedByPoint`, `ConstraintInPixel`) are 8–13 %.
- The worst-first queue is +5 to 8 % on its own.

**(A) One union cavity per choice instead of one per candidate. Same output.**

The 8 candidates lie on one bisector, so their cavities overlap heavily.

1. Search once from t, over unconstrained edges, for every triangle whose circumcircle holds *any* candidate. That is the union of
   the cavities.
2. For each candidate, mark which union triangles hold it: one exact InCircle per pair, at most 8 × |union|.
3. Take each candidate's connected component through unconstrained edges inside the union, starting from the triangle that holds
   it (a walk inside the union). That component is the candidate's cavity. Its boundary edges give the fan.

This drops 7 of the 8 walks and searches and their memory traffic. The InCircle tests stay.

Expected saving: about half of `Score`, so about 10–12 % of refinement time. On its own that reaches roughly +15 %.

Equivalence:
- The cavity of p is the connected component, through unconstrained edges, of the triangles whose circumcircle holds p, containing
  the triangle that holds p.
- That component lies inside the union.
- So the same set is found, and the same score.

**(B) An upper bound on each candidate's score, to stop early. Same output.**

- destroyed(p) is at most the number of bad triangles in p's cavity, so it is at most the number of bad triangles in the union.
  Together with created(p) ≥ 0, that bounds the score from above.
- Candidates are taken in a fixed order, so a candidate whose bound cannot beat the current best (score, then radius) is skipped
  without its fan.
- The default point is always scored, because it sets the floor.
- The order of evaluation does not change the winner, given the tie-break by radius and then by index. The skipped candidates could
  not have won.

Expected saving: small when many candidates score alike. To be measured.

**(C) Fewer candidates where the choice rarely matters. Changes the output; to be measured.**

Step 1's counters (G3_M2_COUNTS slot 7) give how often a point other than the default wins. If that is rare for triangles just below
the bound, those triangles can take the default point without the search.

- The rule to try: no choice when t's smallest angle is at least θ − δ, for example δ = 2°.
- Measure the triangle counts and the time against step 1 on f-11, f-40, the corpus and Herne.
- Stop rule: drop the rule if it costs more than 2 % of step 1's triangle savings.

**(D) The worst-first queue: a bucket queue instead of a binary heap.** A small output change: the order inside a bucket is FIFO.

- 64 buckets by sin² of the smallest angle, covering [0, sin²θ).
- O(1) per operation instead of O(log n).
- The order within a bucket is the order queued, so the result is still deterministic but not the same as step 1's.
- Measure the triangle counts as well.

Plan: (A) first, the exact one, then measure. (B) next. (C) and (D) only if the target is not met, each measured on its own.

### 4.2 The bad-triangle criterion

At present a triangle is split when its smallest angle is below the bound (`BadByAngle`, with the O3 and small-input-angle
exemptions), in the order of `TriangleQueue`. Two ideas to measure, both changing the output:

- **Postpone, do not skip.** A triangle just below the bound (within δ) whose neighbours across unconstrained edges include a worse
  triangle is put back at the end of the queue once. A neighbour's split often removes it. Termination is not affected: it is taken
  the second time.
- **Count, then choose.** Track how often a triangle that was queued as bad is no longer bad when it is taken out (step 1's queue
  already judges it then). If that is frequent, the order is doing the grading work, and the criterion needs no change.

Measure on f-11 and f-40 first: the triangles per octave against the reference's 5–6 (about 15 before step 1).

### 4.3 A 2D candidate region: the petal (Erten and Üngör, SGP 2007, read 2026-11-04)

**What the paper does** (Algorithm 1):
- **The region.** For a bad triangle pqr with shortest edge pq, the region is the petal of pq: the disk through p and q, on the
  triangle's side, in which pq subtends at least the angle bound α. Its radius is |pq| / (2 sin α).
- **The point.** The point of the petal furthest from all existing vertices, i.e. the centre of the largest empty circle centred in
  the petal. It lies on the Voronoi diagram:
  - a Voronoi vertex inside the petal: the circumcentre of pqr (type IV) or of a nearby triangle (type III);
  - or a Voronoi edge's intersection with the petal's boundary: the off-centre on pq's dual edge (type I), or another edge (type II).
- **The search.** It is local, on the Voronoi graph from pqr. Start at the triangle opposite pqr's largest angle; a triangle with a
  smallest angle below α/2 always gives type I.
- **Results.** About half the Steiner points of off-centres at 30°, and termination in practice up to 42° with relocation
  (Algorithm 2).
- **Analysis.** Lemma 1: inserting the point creates no edge shorter than the existing ones for type I if α ≤ 60°, type II if
  α ≤ 45° / 36° / 30° (by subcase), types III and IV if α ≤ 30°. Theorem 1: termination for α ≤ 30° on periodic point sets.

**What it means for us.**
- **The floor holds by construction.** Our default point (the off-centre, type I, or the circumcentre, type IV) is in the petal (the
  off-centre on its boundary, the circumcentre inside when it is the default). The point furthest from all vertices is therefore at
  least as far from them as the default point, so its insertion radius is at least the default's. This is the floor of step 1, with
  no separate check. On the snap grid both points are rounded to their pixel centres, which can reverse the order; there the rounded
  radii are compared again, as step 1 does.
- **Cheaper than step 1's score.** The criterion is the insertion radius alone, with no count of bad triangles in a cavity per
  candidate:
  - a Voronoi vertex's distance to its nearest vertex is its circumradius;
  - a point on a Voronoi edge is as far from both of the edge's generators.

  The search visits the triangles whose circumcentres lie in the petal, plus the dual edges that cross the petal's boundary. That is
  about the cavity of one point, not of eight. This addresses the speed target (4.1) and the quality target together. It replaces
  step 1's score, so it changes the output; it is measured against step 1.

**What our setting adds**, and the paper's analysis (periodic point sets, no constraints) does not cover:
- **Constraints.** The Voronoi diagram of a constrained Delaunay triangulation is not the plain one; the search must not cross a
  constraint. A point beyond a constraint, or one encroaching a subsegment, is refused as in step 1, and the default point is then
  taken.
- **Lenses** (cq ≤ 25.65°), **the snap grid** (the point rounded to its pixel centre, as in step 1, moving it by up to half a pixel's
  diagonal) and **small input angles**: the same handling as step 1.
- **Termination** stays measured, not proven, as for lenses, off-centres and the grid. The floor keeps the insertion-radius argument
  for every point inserted.
- **The paper's points exactly.** The type II points (a ray's or line's intersection with a circle) need square roots, so they are
  computed in doubles in the triangle's scaled frame (`Local`), as the off-centre is. That keeps the 2^k invariance.

**Plan.**
1. A probe first: the petal point in place of step 1's choice, behind a switch. Triangle counts on f-11/a, f-11/b, f-40, the corpus
   and the catalogue; Herne for counts and time.
2. Then a decision against step 1, on both quality and speed.
3. Algorithm 2 (relocation) only if the angle range above 30° becomes a goal.

(Before the paper this section proposed a probe of a few candidates off the bisector. The petal is the region that idea was looking
for.)

### 4.4 The cq churn

In the constrained-quality mode, lens deletion (A) deletes free vertices in a subsegment's diametral circle before it is bisected.
Points the choice placed near subsegments are deleted again later. On f-11/a cq before step 1, 128 of 265 insertions were deleted.
Options, each to be measured by the deletions per insertion and the triangle counts:

- **Score penalty.** A candidate inside the diametral circle of a nearby subsegment that will be encroached scores lower. These are
  the subsegments on the boundary of its cavity, already visited by `EncroachedByPoint`.
- **Order.** Split encroached subsegments before choosing for nearby triangles. The segment queue already comes first. Check
  whether the churn comes from subsegments that become encroached only after the choice.

### Measurements to make first (no code changes)

- Step 1's G3_M2_COUNTS slot 7 per Herne run: how often a non-default candidate wins, in total and for triangles within 2° of the
  bound.
- The size of the union cavity against the cavity per candidate (an instrumented probe build), to estimate (A).
- The cq churn after step 1: deletions against insertions on f-11/a cq and on the Herne cq runs.

### Measured 2026-11-04 (master after #375, `G3_M2_COUNTS`)

The step-1 code on the 12 Herne runs. "Default" is the circumcentre or off-centre; "chosen" is a point other than the default; the
deletions are lens deletion (A), cq at 20° only.

| run | default | chosen | deleted |
|---|---|---|---|
| base 371800 q20 | 2742 | 1618 | 0 |
| base 371800 q30 | 12775 | 4875 | 0 |
| base 373600 q20 | 10173 | 6801 | 0 |
| base 373600 q30 | 47747 | 20920 | 0 |
| base 381000 q20 | 612 | 342 | 0 |
| base 381000 q30 | 3268 | 980 | 0 |
| cq 371800 q20 | 1877 | 1199 | 281 |
| cq 373600 q20 | 8607 | 5111 | 1272 |
| cq 381000 q20 | 501 | 236 | 54 |

The cq runs at 30° are the default runs: lenses apply only up to 25.65°.

- **The choice wins often.** A point other than the default is inserted for 25–40 % of the triangles split. So (C) saves little
  unless those triangles lie close to the bound; measure how the wins spread over the smallest angle before building (C).
- **The cq churn is small after step 1.** Deletions are 7–9 % of the insertions on Herne, against 48 % on f-11/a cq before step 1.
  Section 4.4 is therefore low priority for Herne. f-11/a cq is to be measured again.
