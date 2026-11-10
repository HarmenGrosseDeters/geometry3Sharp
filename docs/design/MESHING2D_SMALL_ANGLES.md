# Small input angles in the constrained-quality mode (design, 2026-11-06)

**Owner decision 2026-11-06, option (c).** A rule against the subsegment cascade at small input angles:
- **Mode:** the constrained-quality mode only (`QualityMesh2` with `Conforming = false` and `MinAngle > 0`). The default mode (Conforming, O2) is
  unchanged.
- **What it exempts:** encroachment by a vertex (split cause 0), and only in the apex region of a small input angle.
- **Research item:** Shewchuk's Terminator with a wider oracle excuse (ROADMAP).

This note comes before the code; the PR follows it. Revised after two reviews: B1 fixed by #396; B2 (region), B3 (literature) and the second
review's points (v restricted as in §6.2, exact pairing, §6.2 differences) below.

## Update 2026-11-09: the pair threshold is 60 degrees

**Owner decision 2026-11-09**, after the CsCheck campaign of `QualityMesh2` against the oracle (QA 2.0 T5) found that a hull domain of four
points and two segments (`(0, 4)-(4, 3)` and `(2, 3)-(2, 0)`, bound 20 degrees, input angle 26.6 degrees at `(2, 0)`) refined towards the apex down to
1e-12 and left a triangle below the bound, which the oracle did not excuse. Input angles between the bound and 60 degrees were the gap named in
the §6.2 comparison below ("an incident segment at an angle between θ and 60°"): Ruppert's and Chew's termination arguments, and Shewchuk's
(2002, section 6.2, Figure 24 cycle at 60 degrees or less), need input angles of at least 60 degrees.

- **The threshold** of a small pair, in the mesher (`Refiner.PairOf`, `SmallPairSin2`) and in the oracle's excuse for a shortest edge that spans an
  input angle (`SpansSmallInputAngle`, `SmallPairSin2`), is the sine squared of 60 degrees less the slack, whatever the angle bound. The other
  excuses (an angle forced by the input, the merge zone, the Shewchuk-bound clause for a triangle at the apex) keep the bound.
- **What follows:** the rule against the cascade (`ApexExempt`, both modes) and the triangle excuse (`SpansSmallAngle` / the oracle) work for input
  angles below 60 degrees; triangles at such corners may lie below the bound, are listed in `BadTriangles`, and the oracle excuses them by the same
  rule. Golden rows of M3 and M3 constrained changed with this reason: fewer vertices (f-4/a: 1994 to 1266 vertices), more bad triangles.
- **Termination is still measured, not proven**, as before (end of section 2).
- Where the text below says "below θ", read "below 60 degrees" for the pair angle.

## 1. Diagnosis (measured, master 14d744f0)

The large triangle-count outliers of the reference comparison come from a cascade of subsegment splits towards the apex of a small input angle. The
splits shrink geometrically and only stop at the snap tolerance, about 2^-40 of the extent.

| input | smallest input angles | depth of the cascade | input's closest features |
|---|---|---|---|
| ext/cdt/island | 2.6° (hull edge vs. segment) | 38 octaves | 2^-5.3 of the extent |
| ext/cdt/cdt | 0.0°, 0.14° | 38 octaves | 2^-7.4 of the extent |
| TestOverlay-stmlf/1461 (q20) | 0.0°, 0.013° | 22 octaves | near-contact input as well |

So the depth comes from the cascade, not from the input.

- **Hull edges count.** Without rings the domain is the convex hull, and its edges become constraints (D13). A hull edge that meets a ring segment
  at a small angle is the most common source. Shewchuk 2002, Appendix A (Fig. 40, the guitar) describes the same effect: when the whole convex hull
  is triangulated, "the boundary edges of the convex hull must be treated as segments, and spurious small angles and features may form".
- **Which cycle.** It is not §6.2's Figure 24 cycle, the plain mutual encroachment of two subsegments.
  - The mesher splits a subsegment with exactly one input end at the concentric shell closest to its middle (`SplitSegment`: a power of two from
    the input vertex, within a third to two thirds of the piece). That is §6.2's rule.
  - So the apex pieces of both sources have power-of-two lengths. After one equalising split they are equal, and "incident subsegments of equal
    length do not encroach upon each other" (p. 29).
  - The depth comes from §6.2's Figure 26 cycle, the figure eight "in which a segment vertex sires another segment vertex (via a small input
    angle), which sires a free vertex (at the circumcenter of a skinny triangle), which sires another segment vertex" (p. 31).
  - In our split causes, a circumcentre or off-centre of a skinny triangle at the apex (cause 1 or 2) halves an apex piece. The partner then follows
    by vertex encroachment (cause 0), and the follow creates the next shorter free edge.

**Splits by cause** (`G3_M2_COUNTS`, q30, constrained mode; 0 = encroached by a vertex, 1 = circumcentre behind the subsegment, 2 = circumcentre or
off-centre would encroach). "Rule" is the rule of section 2.

| input | rule | cause 0 | cause 1 | cause 2 | triangles |
|---|---|---|---|---|---|
| island | off | 960 | 0 | 1 140 | 8 879 |
| island | on | 106 | 150 | 389 | 2 971 |
| cdt | off | 3 584 | 0 | 2 088 | 20 699 |
| cdt | on | 588 | 991 | 2 190 | 14 245 |

- The rule cuts the cause-0 leg, the partner's follow.
- The circumcentre legs stay (causes 1 and 2; cause 1 rises from 0 to about 1 000 on cdt). So the cascade partly goes on, and cdt stays far above
  the reference (4 426). The Terminator cuts that leg instead (§6.3), at the price of a wider oracle excuse (ROADMAP research item).

## 2. The rule

**Sources.** Input segments, ring edges, and the hull edges that `ConstrainHull` adds.
- On the snap grid a snapped source is replaced by the input segment it comes from (`OriginOf`, as `SpansSmallAngle` does), so the angles are the
  input's, the ones the oracle judges.
- `SnapRounding2.TryOrigin` returns the first origin when a snapped piece has several (merged chains). The rule then uses that one.
- Two sources with the same origin never pair.

**Small pair.** Sources a and c form a small pair when both conditions hold:
- **Angle:** sin² of the angle between their lines is below sin²θ − 2^-30 − 2^-40 (θ = `MinAngle`), computed in a common power-of-two scale. This
  is the oracle-consistent margin of #396 (B1).
  - Below about θ = 0.0018°, sin²θ < 2^-30 and nothing pairs.
- **Meeting point:** their closed input segments meet, decided exactly (`MeetKind`: four `Orient2d` signs; equal endpoints for a shared endpoint).
  This is the oracle's exact t ∈ [0, 1]. It covers a shared endpoint, a T-junction (an end on the other's interior) and a proper crossing.
- **Not covered:**
  - Collinear sources (cross = 0 or all four signs 0) never pair: their overlap is noded into shared subsegments, and there is no apex.
  - O11 hull bends (a hull edge bent at a merged point) are not input angles.

**Exact meeting test, measured.** The second draft used a window t ∈ [−10^-9, 1 + 10^-9] in doubles. The window is relative to each segment's
length, so it could pair near T-junctions with a short segment. Measured with the exact test:
- **Near misses** (pairs the window adds): 0 on island, cdt and stmlf/1461 at every angle.
- **Shared endpoints:** almost all exemptions.
- **T-junctions or crossings:** a few, only on stmlf (22–89).

So the window was not the mechanism, and the exact test gives the same meshes. The gain over the second draft's "strict" figures came from the strict
test of `SpansSmallAngle` (t ∈ {0} ∪ [10^-12, 1] in doubles), which misses shared endpoints when t rounds to 1 + ε. That test stays as it is for
`SpansSmallAngle` (follow-up, section 7).

The pairs are computed once per ordered pair of sources and cached (`PairOf`). The sources do not change during refinement.

**Exemption.** In the constrained-quality mode, a subsegment s with ends u and w is **not** treated as encroached by a vertex v if all of these hold:
- **v is a refinement vertex on exactly one source c.** v was made by refinement (`v ≥ firstSteiner`, so it is not an input vertex and not a crossing
  the triangulation made), and `SourcesAt(v)` is {c}, with no third source.
- **The pair:** for some source a of s with a ≠ c, (a, c) is a small pair.
- **(i) or (ii):**
  - **(i) Shell subsegment:** an end of s, say u, is a vertex on c too (`SourcesAt(u)` contains c), so s starts at the apex or at the crossing of a
    and c; and v lies on s's side of u (exact sign of (v − u)·(w − u)).
  - **(ii) Radius matching:** v and an end of s lie at distances from the apex within a factor of 1.5 of each other (2.24 on squares), on the same
    side of it (`NearSmallAngle`, the test `SpansSmallAngle` uses).

A multi-source subsegment is exempt if any of its sources pairs.

**What (ii) is, honestly (second review).** (ii) matches radii; it does not measure closeness to the apex.
- With shells, a piece's end radii differ by at most about a factor of 2.
- An encroaching v projects into the piece, so v is within 1.5 of one end (√2 < 1.5).
- So (ii) admits a partner vertex anywhere along the wedge, as long as the piece's end radii differ by at most 2.25.
- (ii) fails only for a piece whose ends are further apart in radius, for example between two input vertices on a.

The real limit is the condition on v, which mirrors §6.2's exceptions:
- §6.2 does not apply its cure when the parent "is an input vertex or lies on a nonincident segment" (p. 32).
- So an input vertex on c, c's far endpoint, a crossing vertex Y of c with a third segment, and any vertex with a third source still encroach, and
  their subsegments are split.

**Why both (i) and (ii)** (measured with the second draft's region probe):
- (i) alone misses the off-shell partner vertices inside an octave (midpoints, circumcentre-born vertices): island q30 4 210, cdt q30 20 435.
- (ii) alone misses the shell subsegment encroached by a partner vertex at half its length, a ratio of 2 > 1.5: island q30 8 683, cdt q30 16 388.
  Those runs used the strict pair test and allowed input vertices as v.

**The v condition costs island most of its gain** (decision for the coordinator before the code PR). With input vertices and multi-source vertices
allowed as v, island q30 gives 786. With §6.2's exceptions it gives 2 971.
- Most of the difference is c's far endpoint, an input vertex on two ring segments, encroaching a's long hull-edge piece.
- §6.2 splits there: the split refines a to the local feature size |XY|·sin φ at that far endpoint.
- cdt and stmlf barely change (section 6).

**Threshold θ, not 60°.**
- The oracle consistency (section 4) does not depend on the threshold, since the rule excuses no triangle. So 60° would also be oracle-safe; the real
  limits are B1 for the pair's angle and the v condition and region.
- θ is chosen because the oracle excuses the skinny triangles near an apex below θ only. Above θ, the triangles that an exempted split leaves would
  be split by causes 1 and 2 anyway.
- The first draft measured nearly the same gain at 60° and at θ.
- A class of angles below a threshold resembles the Terminator's subsegment cluster (§6.3, defined by 60° and transitive) more than §6.2, which has
  no angle condition. Ours is not transitive: a fan of 20° + 20° at θ = 30° pairs the neighbours, not the outer two (40°).

**Unchanged:**
- encroachment by an input vertex, a crossing vertex, a vertex with several sources, a free vertex, a vertex on a source that does not pair with
  s's, or outside the region;
- **cause 1**, a circumcentre behind s;
- **cause 2**, a circumcentre or off-centre that would encroach s (`EncroachedByPoint`, a separate test);
- the seditious-edge triangle exemption (`SpansSmallAngle`);
- the shells, the lenses, the grid rules;
- the Conforming mode, bit for bit (`ApexExempt` returns false there).

**Queued cause-2 entries (existing behaviour).** When a circumcentre would encroach several subsegments, the first is split at once and the others are
queued. A queued entry is re-checked with `Encroached()` when it is dequeued, and dropped silently if nothing encroaches it by then. This is existing
behaviour; with the rule, an entry encroached only by exempt vertices is dropped the same way. The triangle behind it is tried again after the first
split, so it is not left behind (section 4).

## 3. Literature and termination

**Read.**
- Shewchuk 2002, §6.2–6.5, §7 and Appendix A (pp. 29–51), read in full from the paper's text, which the owner supplied in the session on 2026-11-06 (some
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

**How the rule relates to §6.2.**
- **The v condition mirrors §6.2's exceptions.** The exception "unless its parent is an input vertex or lies on a nonincident segment" keeps the cure
  to vertices that refinement made on a segment incident to the encroached one. Our v condition is the same: a refinement vertex on exactly one
  source c, which pairs with s's source.
- **The differences:**
  - A pair also forms at a T-junction or a proper crossing, which are not incident in Shewchuk's sense. v must still be a refinement vertex on c, so
    the crossing vertex itself never exempts.
  - The pair angle must be below θ. §6.2 has no angle condition; its Figure 24 cycle needs an angle of at most 60° (p. 29).
  - Insertion radii are not compared. The geometry decides: the pair, the v condition, and (i) or (ii).

**Match or approximation: an approximation.** The two agree in the cascade:
- The encroaching v on c lies at about the radius of the new split point on a, at a distance of about r·sin φ from it.
- So the new vertex's insertion radius (at most its distance to v) is about r·sin φ.
- That is below v's own, which v got as a split of c, about r/2.

Neither condition implies the other in general:
- **The rule exempts splits §6.2 makes:** a new vertex that would not shrink the insertion radius, for example after an off-centre on c put v
  unusually close to a.
- **§6.2 refuses splits the rule makes:** an incident segment at an angle between θ and 60°, or a piece that fails (i) and (ii) (ends far apart in
  radius), whenever the new vertex would have the smaller insertion radius. For non-incident segments both split.

So §6.2's termination argument does not carry over. **Termination is measured, not proven**, as for the whole refiner (MESHING2D_GRADING.md:
off-centres, petal candidates, the grid, lenses and lens deletion are not covered by any proof).

**§6.2's caveats apply.**
- The rule leaves encroached subsegments, can leave angles near 180° and other skinny triangles at the apex, and the mesh is not "truly Delaunay".
- In the constrained-quality mode this is allowed. The spec promises a constrained Delaunay triangulation, the angle bound up to the oracle's excuses
  (O3) and the area bound; it promises neither unencroached subsegments nor an upper angle bound.
- An encroached subsegment does not violate the constrained Delaunay property.

**§6.4** (p. 36) is about the Terminator with Chew's lenses, not this rule.
- The problem it names: when a split is declined, deleting the free vertices in the diametral circle may never end (vertices eternally created and
  deleted), and keeping them may leave arbitrarily skinny triangles.
- Our lens deletion (MESHING2D_GRADING.md, lenses (A)) runs only before an actual bisection, so a declined split deletes nothing. §6.4 prescribes
  the same for the Terminator with lenses: the rejected circumcentre is not inserted, "but other vertices inside the diametral circle of s are not
  deleted".
- With lenses, equal-length subsegments of a cluster encroach each other only below a smaller angle than with circles (§6.4; the value is lost in
  the extracted text). So with lenses (θ ≤ 25.66°) part of the cascade the rule targets does not arise in the first place; the measured gains at
  q20 and q25 are smaller (island q25 694 → 456).
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
- left by the existing exemptions, which are unchanged:
  - a subsegment that is locked or `unsplittable`. Off the grid, `unsplittable` comes from a split point near another source (`NearOtherSource`,
    O11; the oracle's merge zone), a piece below `MinRoundedSplitExponent`, or a split point that makes no progress (the oracle's precision limit,
    `AtPrecisionLimit`). On the grid it comes from `GridSplitPoint`.
  - `exempt`, a triangle given up: its circumcentre failed, or the kernel refused the insertion, or the subsegment it would split is locked or
    unsplittable;
  - the grid floor;
  - the Steiner limit (reported).

So the rule cannot leave an unexcused bad triangle behind. No oracle change is needed.

**Reported by the PR:** the exempt and unsplittable counts by reason, and the oracle's `MergeZoneExcused`, before and after.

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

**Tests** (`QualityMesh2SmallAngleRuleTests`, public API, fail on master; `QualityMesh2ApexExemptionTests`, the refiner's decision log):
- the hull wedge (a segment at 2.6° and 1° to a hull edge, at 20° with lenses and 30°): valid, at most 30 triangles (master 59 to 430); on a
  2^-8 grid at most 40 (master 57 and 178);
- the wedge under `Similarity.All()` (2^k bit for bit), at 2^-500 and 2^200 bit for bit, and deterministic (repeated, parallel = serial, one
  mesher reused);
- the catalogue's small-angle rows F-38/a-e in constrained mode with the angle check forced on: valid (master is valid too); F-38/d at most 5 000
  triangles (master 22 384);
- the v condition: an input point on c, c's far endpoint (on two sources), and an input vertex Y where a third segment ends on c each encroach a
  subsegment of a and never exempt it. A proper crossing of c with a third segment cannot be isolated (the third segment's end on a's side
  encroaches a anyway); its vertex is excluded by the same `firstSteiner` check;
- the far case for (ii): a refinement vertex on c encroaching a's piece between two input vertices (radii 2 and 16) is split;
- the shell rule (i): an exempt shell subsegment whose far end is more than 1.5 times the vertex's distance from the apex;
- both sources ending at the apex, where the double test rounds t to 1 + 2^-52: paired by the exact test;
- angles at θ, 31°, 40° and θ − 6·10^-10 rad: no exemption, the mesh of master bit for bit;
- the conforming mode: no exemption, the mesh of master bit for bit;
- `MeetKind`: shared endpoint (both orders), T-junction, small-angle crossing, a near miss 2^-30 off a long segment, just past an end, collinear;
- `SteinerLimitReached == false` asserted in every test.

**Intentional behaviour change:** the output of `QualityMesh2` with `Conforming = false` and `MinAngle > 0` changes.

**Measured with the rule as implemented** (master 14d744f0 plus the rule; constrained mode; all VALID, `steiner_limit_reached` 0; "off" is
`G3_M2_APEX_EXEMPT=0`, the master behaviour; "any v" also allowed input and multi-source vertices, the figure the owner approved (c) on, before the
§6.2 v condition was confirmed):

| input | off | rule | any v | reference |
|---|---|---|---|---|
| island q20 | 511 | 366 | | |
| island q25 | 694 | 544 | 456 | |
| island q30 | 8 879 | 2 971 | 786 | 929 |
| cdt q20 | 7 619 | 5 692 | | |
| cdt q25 | 9 478 | 8 293 | | |
| cdt q30 | 20 699 | 14 245 | 14 273 | 4 426 |
| stmlf/1461 q20 | 136 960 | 117 608 | 117 535 | |
| stmlf/1461 q25 | 197 163 | 184 714 | 184 497 | |
| stmlf/1461 q30 | 390 342 | 325 662 | 326 112 | 15 575 |

Mesh time falls with the triangle count (island q30 392 → 239 ms, stmlf q30 5 887 → 4 407 ms). The oracle's merge-zone excuses stay about the
same (stmlf q30 16 → 18, q25 13 → 8; island and cdt 1–2 either way).

**Herne cq** (the three tiles, snap 0.01 m, all VALID before and after, no Steiner limit):

| tile | q20 off → rule | q30 off → rule | mesh time q30 |
|---|---|---|---|
| 371800 5710600 | 11 386 → 7 506 | 39 606 → 24 433 | 1 493 → 1 600 ms |
| 373600 5709000 | 59 978 → 57 113 | 164 641 → 154 284 | 4 133 → 4 109 ms |
| 381000 5710400 | 3 463 → 3 053 | 10 392 → 8 315 | 824 → 811 ms |

**Corpus** (84 inputs, constrained mode): 20° 426 762 → 299 978 triangles (−29.7 %), 30° 552 429 → 539 537 (−2.3 %); the median run is
unchanged; no verdict worse.

**Catalogue rows in constrained mode** (`rows --mode constrained`): all "ok" before and after; 12 rows change, all with fewer triangles: F-4/a-c,
F-5 (6 075 → 3 269), F-12/b, F-22/a (40 008 → 386), F-26, F-27, F-38/d (22 384 → 3 165), F-39, F-42 (23 934 → 18 245). The golden rows have no
constrained-mode row and are unchanged (M1 28, M2 33, M3 52 rows, 0 differences).

**Exemptions by meeting kind** (q30): island 824 at shared endpoints; cdt 7 506; stmlf/1461 158 007, and 89 at T-junctions or crossings; Herne
q30 has T-junction or crossing exemptions on the grid (1 004 on 371800). No near misses exist with the exact test.

**Mutants** (the two test classes, `QualityMesh2ApexExemptionTests` and `QualityMesh2SmallAngleRuleTests`): killed: the exemption off, (i) only,
(ii) only, the threshold at 60°, the v condition dropped, input vertices allowed, the strict double meeting test, the conforming check dropped,
and three in `MeetKind`. Surviving: the same-source check (equivalent: a pair (a, a) has cross = 0 and is never small), the same-side test in (i)
(equivalent under encroachment: a vertex in the diametral circle or lens of u-w has (v − u)·(w − u) > 0), and the single-source check alone
(it matters only for refinement vertices on two sources, overlapping or merged grid chains; the input-vertex check kills the combined mutant).

## 7. Left open

- **`SpansSmallAngle`** (own follow-up, not in this PR):
  - Its strict apex test in doubles misses shared endpoints when t rounds to 1 + ε. Those triangles are not excused by the mesher and are refined
    further: safe, but more triangles.
  - It also accepts a computed t1 == 1 where the exact value is 1 + δ, and it picks the corner in doubles. Near-ties become likelier with the
    near-180° triangles this rule leaves.
  - An exact test (as `MeetKind`) would fix both and stay oracle-consistent; measured first.
- The cascade through causes 1 and 2 (cdt): the Terminator, with a wider oracle excuse, is the ROADMAP research item.
- The near-contact features of cdt and stmlf (a vertex very close to a segment it does not touch).
- Lenses up to 28.6°: not applicable, Rand's bound is for Chew's second algorithm, not for lenses (meshing2d.md, Open).
- The O2 question for the default mode (#390 section 6, owner).
