# Small input angles in the constrained-quality mode (design, 2026-11-06)

**Owner decision 2026-11-06, option (c).** A rule against the subsegment cascade at small input angles:
- **Mode:** the constrained-quality mode only (`QualityMesh2` with `Conforming = false` and `MinAngle > 0`). The default mode (Conforming, O2) is
  unchanged.
- **What it exempts:** encroachment by a vertex (split cause 0), and only in the apex region of a small input angle.
- **Research item:** Shewchuk's Terminator with a wider oracle excuse (ROADMAP).

This note comes before the code; the PR follows it. Revised after the review of the first draft (B1 fixed by #396; B2 and B3 below).

## 1. Diagnosis (measured, master 14d744f0)

The large triangle-count outliers of the reference comparison come from a cascade of subsegment splits at small input angles:
- Two subsegments meet at a small angle and split each other in turn, each split triggered by a vertex on the other. This is Shewchuk 2002 §6.2's
  Figure 24 cycle. The concentric shells are meant to stop it ("incident subsegments of equal length do not encroach upon each other", p. 29), but
  off-shell vertices (midpoints, circumcentre-born vertices, off-centres) restart it.
- The splits shrink geometrically towards the apex. They only stop at the snap tolerance, about 2^-40 of the extent.
- Without rings the domain is the convex hull, and its edges become constraints (D13), so hull edges count too. A hull edge that meets a ring
  segment at a small angle is the most common source.

| input | smallest input angles | depth of the cascade | input's closest features |
|---|---|---|---|
| ext/cdt/island | 2.6° (hull edge vs. segment) | 38 octaves | 2^-5.3 of the extent |
| ext/cdt/cdt | 0.0°, 0.14° | 38 octaves | 2^-7.4 of the extent |
| TestOverlay-stmlf/1461 (q20) | 0.0°, 0.013° | 22 octaves | near-contact input as well |

So the depth comes from the cascade, not from the input.

**Splits by cause** (`G3_M2_COUNTS`, q30, constrained mode; 0 = encroached by a vertex, 1 = circumcentre behind the subsegment, 2 = circumcentre
or off-centre would encroach):

| input | rule | cause 0 | cause 1 | cause 2 | triangles |
|---|---|---|---|---|---|
| island | off | 960 | 0 | 1 140 | 8 879 |
| island | on | 67 | 26 | 43 | 786 |
| cdt | off | 3 584 | 0 | 2 088 | 20 699 |
| cdt | on | 551 | 1 012 | 2 208 | 14 273 |

- On island, cause 0 drives the cascade; with the rule the circumcentre splits fall as well.
- On cdt, the cascade partly goes on through causes 1 and 2: cause 1 rises from 0 to 1 012. The rule does not cut those (section 2), so cdt stays
  far above the reference (4 426).

## 2. The rule

**Sources.** Input segments, ring edges, and the hull edges that `ConstrainHull` adds.
- On the snap grid a snapped source is replaced by the input segment it comes from (`OriginOf`, as `SpansSmallAngle` does), so the angles are the
  input's, the ones the oracle judges.
- `SnapRounding2.TryOrigin` returns the first origin when a snapped piece has several (merged chains). The rule then uses that one.
- Two sources with the same origin never pair.

**Small pair.** Sources a and c form a small pair when both conditions hold:
- **Angle:** sin² of the angle between their lines is below sin²θ − 2^-30 − 2^-40 (θ = `MinAngle`), computed in a common power-of-two scale. This
  is the oracle-consistent margin of #396 (B1).
- **Meeting point:** their input segments meet on both of them, at the parameters t1, t2 ∈ [−10^-9, 1 + 10^-9] of the line intersection, in the
  same scale.
  - This covers a shared endpoint, a T-junction and a crossing, as the oracle's `SpansSmallInputAngle` does.
  - The tolerance is wider than in `SpansSmallAngle`'s strict test (t ∈ {0} ∪ [10^-12, 1]). That test must never excuse a triangle the oracle
    would not excuse, and at a shared endpoint it can miss the pair by rounding (t1 = 1 + ε).
  - The rule excuses no triangle (section 4), so it can use the wider test.
  - Measured on island q30: 4 210 → 786 triangles with the wide test, with the same region.
- **Not covered:**
  - Collinear sources (cross = 0) never pair: their overlap is noded into shared subsegments, and there is no apex.
  - An angle between rays is tested through |cross| plus a same-side test (below). At a crossing the supplement (180° − φ) never counts, since its
    rays lie on opposite sides.
  - O11 hull bends (a hull edge bent at a merged point) are not input angles.

The pairs are computed once per ordered pair of sources and cached (`PairOf`). The sources do not change during refinement.

**Exemption.** In the constrained-quality mode, a subsegment s with ends u and w is **not** treated as encroached by a vertex v if, for some source
a of s and some source c at v (`SourcesAt(v)`), with a ≠ c and (a, c) a small pair, one of these holds:
- **(i) Shell subsegment.**
  - An end of s, say u, is a vertex on c too (`SourcesAt(u)` contains c): s starts at the apex, or at a crossing vertex of a and c.
  - v lies on s's side of u (exact sign of (v − u)·(w − u)).
- **(ii) Matched radii.** v and an end of s lie at distances from the apex within a factor of 1.5 of each other (2.24 on squares), on the same side
  of it. This is the test `SpansSmallAngle` uses (`NearSmallAngle`).

A multi-source subsegment is exempt if any of its sources pairs. The cascade can come from any of the sources a subsegment lies on.

**Why the region (B2).** The first draft exempted a vertex on c at any distance. The region keeps the far features with Ruppert's normal treatment.
- **(i) alone** misses the mutual encroachment of shell vertices with off-shell vertices inside an octave. Measured: island q30 4 210, cdt q30
  20 435.
- **(ii) alone** misses the shell subsegment encroached by a partner vertex at half its length: in the shell cascade the ratio is 2 > 1.5. Measured:
  island q30 8 683, cdt q30 16 388 (strict pair test).
- **Both together** gave the same mesh as the unrestricted rule on island, cdt and stmlf/1461 at q30 (island 786, cdt 14 273 with the wide pair
  test; with the strict one 5 194, 16 384 and 389 428 for both). The restriction costs nothing measured, and it rules out the far case by
  construction. A test pins the far case (section 6).

**Threshold θ, not 60°.**
- The oracle consistency (section 4) does not depend on the threshold, since the rule excuses no triangle. So 60° would also be oracle-safe; the real
  limits are B1 for the pair's angle and B2 for the region.
- θ is chosen because the oracle excuses the skinny triangles near an apex below θ only. Above θ, the triangles that an exempted split leaves would
  be split by causes 1 and 2 anyway.
- The first draft measured nearly the same gain at 60° and at θ.

**The rule is pairwise, not transitive.** A fan of 20° + 20° at θ = 30° pairs the neighbours, not the outer two (40°).

**Unchanged:**
- encroachment by a free vertex, or by a vertex on a source that does not pair with s's, or outside the region;
- **cause 1**, a circumcentre behind s;
- **cause 2**, a circumcentre or off-centre that would encroach s (`EncroachedByPoint`, a separate test);
- the seditious-edge triangle exemption (`SpansSmallAngle`);
- the shells, the lenses, the grid rules;
- the Conforming mode, bit for bit (`ApexExempt` returns false there).

**Queued cause-2 entries.** When a circumcentre would encroach several subsegments, the first is split at once and the others are queued. A queued
entry is re-checked with `Encroached()` when it is dequeued. If only exempt vertices encroach it by then, it is dropped silently. The triangle
behind it is tried again after the first split, so it is not left behind (section 4).

## 3. Literature and termination

**Read.**
- Shewchuk 2002, §6.2–6.4 (pp. 29–36), read in full from the paper's text, which the owner supplied in the session on 2026-11-06 (some
  symbols lost in the extraction). The coordinator's earlier summary of §6.2–6.3 agrees with it.
- My own fetch of the paper (people.eecs.berkeley.edu) had failed: the host does not resolve in this environment. No other route was tried.
- Miller, Pav and Walkington 2005: not read (abstract only, unverified).

Logged in MESHING2D_PROVENANCE.md.

**Which cure this is.** It is the conservative cure of §6.2, not the Terminator of §6.3. §6.2 (p. 32) gives it in two forms:
- **The strategy:** "never insert a vertex whose insertion radius is smaller than the insertion radius of its most recently inserted ancestor (its
  parent if the parent was inserted; its grandparent if the parent was rejected), unless its parent is an input vertex or lies on a nonincident
  segment."
- **"The precise rule":** "if a subsegment is encroached upon by a segment vertex, and the new vertex inserted in the encroached subsegment would
  have a smaller insertion radius than the encroaching vertex, do not split the encroached subsegment."

§6.2 calls it "a sure-fire way to guarantee termination". The claim is stated, not proven, in the text.

The exception "unless its parent is an input vertex or lies on a nonincident segment" limits the cure to encroachment by a vertex on an *incident*
segment, one that shares an endpoint. Our pair condition is that limit, with two differences:
- a pair also forms at a T-junction or a crossing (not incident in Shewchuk's sense);
- the angle must be below θ (§6.2 has no angle condition; its cycle needs an angle of at most 60°, p. 29).

**Match or approximation: an approximation.**
- The rule does not compare insertion radii; it decides by geometry: the pair, the shell subsegment, matched radii.
- In the cascade the two agree.
  - The encroaching vertex v on c lies at about the radius of the new split point on a, at a distance of about r·sin φ from it.
  - So the new vertex's insertion radius (at most its distance to v) is about r·sin φ. It is below v's own, which v got as a shell split of c,
    about r/2.
- Neither condition implies the other in general.
  - The rule ignores the insertion radii. It exempts a split even where the new vertex would not shrink the radius, for example after an off-centre
    on c put v unusually close to a.
  - §6.2 refuses splits the rule makes: an incident segment at an angle between θ and 60°, or outside the region, whenever the new vertex would
    have the smaller insertion radius.
- So §6.2's termination argument does not carry over. **Termination is measured, not proven**, as for the whole refiner (MESHING2D_GRADING.md:
  off-centres, petal candidates, the grid, lenses and lens deletion are not covered by any proof).

**§6.2's caveats apply.**
- The rule leaves encroached subsegments, can leave angles near 180° and other skinny triangles at the apex, and the mesh is not "truly Delaunay".
- In the constrained-quality mode this is allowed. The spec promises a constrained Delaunay triangulation, the angle bound up to the oracle's excuses
  (O3) and the area bound; it promises neither unencroached subsegments nor an upper angle bound.
- An encroached subsegment does not violate the constrained Delaunay property.

**§6.4** (p. 36) is about the Terminator with Chew's lenses, not this rule.
- The problem it names: when a split is declined, deleting the free vertices in the diametral circle may never end (vertices eternally created and
  deleted), and keeping them may leave arbitrarily skinny triangles.
- Our lens deletion (MESHING2D_GRADING.md, lenses (A)) runs only before an actual bisection, so a declined split deletes nothing.
- A skinny triangle left that way is still bad and is split by causes 1 and 2 (section 4).
- The constrained-quality mode uses lenses at θ ≤ 25.66°. This combination is measured, not covered by a proof (section 5).

**Guards.**
- `SteinerLimit` and the step cap (`RefinementLimit`, 64·(limit + slots) + 100 000) stop a run that would not settle. A run is never a hang.
- A run at the Steiner limit skips the oracle's angle check. So every measured run reports `steiner_limit_reached` (all 0 so far), and the tests
  assert `SteinerLimitReached == false`.

## 4. The angle guarantee near the apexes, and the oracle

**The rule excuses no triangle.** It only removes subsegment splits caused by a vertex. Every triangle refinement leaves is therefore one of these:
- good (meets θ, and the area bound);
- excused by the mesher's `SpansSmallAngle` (the seditious-edge exemption, unchanged). It is at least as strict as the oracle's
  `SpansSmallInputAngle`:
  - the same factor (1.5);
  - the same side;
  - "small" with the B1 margin;
  - the apex strictly on both segments.

  So the oracle excuses it too.
- still bad and therefore still split. Its circumcentre or off-centre is inserted, or splits the subsegment it would encroach or lies behind
  (causes 1 and 2, not exempted). A queued cause-2 entry that is dropped (section 2) does not change this: the triangle is tried again after the
  first split.
- left by the existing exemptions, which are unchanged: locked or grid-unsplittable subsegments, the grid floor, and the Steiner limit (reported).

So the rule cannot leave an unexcused bad triangle behind. No oracle change is needed.

**Measured: no AngleBelowBound** on island, cdt or stmlf/1461 at q20–q35, nor in the first draft's corpus runs. If the PR's runs (Herne, the
catalogue with the angle check on) show one, the rule gets narrower; the oracle does not get wider. Any oracle change goes to the coordinator first.

**Small item (oracle docs).** The `SpansSmallInputAngle` summary says "a factor of two", but the code uses 1.5 (rp·4 > rq·9). The PR fixes the
comment only.

## 5. Grid, catalogue and performance

- **Hull sources on the grid.**
  - The mesher's hull sources on the grid are the snapped chains of the hull edges (`HullSegmentsFrom`–`HullSegmentsTo`, MESHING2D_HULL_GRID.md
    §7), resolved with `OriginOf`.
  - The oracle's `HullSources()` are the edges of the domain's outer hull (rounded on M3), split at input points on them.
  - The two can differ by the rounding. That changes only which splits are exempt, not the oracle consistency (section 4).
  - A grid test with a hull edge at a small angle to a segment checks pinches 0 and VALID.
- **Merged chains near the apex.** Two pair chains that share pixels near the apex become one chain with both sources. `OriginOf` picks the first
  origin, and two sources with one origin never pair. A grid test of this case checks pinches 0, VALID, and that the run settles.
- **Catalogue.** The F-38 small-angle rows have `AngleGuaranteed = false`, so "golden rows VALID" checks nothing for section 4. The PR runs fan, star
  and the 30° triangle in constrained mode with the angle check forced on.
- **Performance.**
  - The pairs are cached per source pair.
  - `SourcesAt` and the pair lookups run only when `EncroachesOn` is already true.
  - The PR measures Herne cq time before and after.

## 6. What the PR measures and tests

**Numbers.**
- island, cdt and stmlf/1461 at 20°, 25° and 30°;
- the corpus at 20° and 30° in constrained mode;
- Herne cq;
- the cq golden rows: every changed row listed.

For each: verdicts, pinches 0, `SteinerLimitReached`, triangles and time.

**Tests:**
- a procedural ring with a hull edge at a small angle (island-like), whose triangle count must drop well below master's (fails on master);
- the B1 window: a pair at θ − 6·10^-10 rad is not small (the exemption does not act), and the result is VALID;
- **B2 far case:** a vertex of the partner far from the apex encroaching a subsegment of a away from the apex is still split;
- a T-junction and a crossing at a small angle: exempt like an endpoint, VALID;
- an angle at or above θ: unchanged against master, bit for bit;
- the grid case with a hull pair and merged chains: pinches 0, VALID;
- fan, star and the 30° triangle with the angle check on;
- `SteinerLimitReached == false` asserted in every test;
- mutants:
  - the exemption off;
  - (i) only, (ii) only;
  - the threshold at 60°;
  - the same-source check dropped;
  - the strict pair test instead of the wide one;
  - the same-side test dropped;
- invariance over `Similarity.All()` (2^k bit for bit);
- determinism (repeated runs, parallel = serial);
- the Conforming mode unchanged, bit for bit.

**Intentional behaviour change:** the output of `QualityMesh2` with `Conforming = false` and `MinAngle > 0` changes.

**Measured with a build of the rule on master 14d744f0** (constrained mode, all VALID, `steiner_limit_reached` 0):

| input | master | with the rule | reference |
|---|---|---|---|
| island q25 | 694 | 456 | |
| island q30 | 8 879 | 786 | 929 |
| island q35 | 36 971 | 17 561 | |
| cdt q30 | 20 699 | 14 273 | 4 426 |
| cdt q34 | 38 242 | 31 177 | |
| stmlf/1461 q20 | 136 960 | 117 457 | |
| stmlf/1461 q25 | 197 163 | 183 631 | |
| stmlf/1461 q30 | 390 342 | 325 875 | 15 575 |

## 7. Left open

- **`SpansSmallAngle`'s strict apex test misses shared endpoints by rounding** (t1 = 1 + ε). Those triangles are not excused by the mesher and are
  refined further: safe, but more triangles. An exact shared-endpoint test would keep it oracle-consistent. Own follow-up, measured first.
- The cascade through causes 1 and 2 (cdt): the Terminator, with a wider oracle excuse, is the ROADMAP research item.
- The near-contact features of cdt and stmlf (a vertex very close to a segment it does not touch).
- Lenses up to 28.6° (Rand, CCCG 2011), later.
- The O2 question for the default mode (#390 section 6, owner).
