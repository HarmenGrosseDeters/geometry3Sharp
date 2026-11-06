# Small input angles in the constrained-quality mode (design, 2026-11-06)

Owner decision 2026-11-06: a rule against the subsegment cascade at small input angles, **in the constrained-quality mode only**
(`QualityMesh2` with `Conforming = false`). The default mode (Conforming, O2) is unchanged. This note comes before the code; the PR
follows it.

## 1. Diagnosis (measured, master 188ad8c7)

The large triangle-count outliers of the reference comparison come from a cascade of subsegment splits at small input angles. Two
subsegments meet at a small angle and split each other in turn, each split triggered by a vertex on the other subsegment. The splits
shrink geometrically towards the apex, and only stop at the snap tolerance, about 2^-40 of the extent.

Without rings the domain is the convex hull, and its edges become constraints (D13), so hull edges count too. A hull edge that meets a
ring segment at a small angle is the most common source.

Measured with a per-split log at q30, constrained mode:

| input | smallest input angles | depth of the cascade | splits per octave | vertices within 2^-16 of the extent of a small-angle apex |
|---|---|---|---|---|
| ext/cdt/island | 2.6° (hull edge vs. segment) | 38 octaves | about 57 | 57 % |
| ext/cdt/cdt | 0.0°, 0.14° | 38 octaves | about 140 | 41 % |
| TestOverlay-stmlf/1461 (q20) | 0.0°, 0.013° | 22 octaves | about 2800 | (near-contact input as well) |

The input's own closest features are only 2^-5.3 (island) and 2^-7.4 (cdt) of the extent apart, so the depth comes from the cascade, not
from the input.

- The existing seditious-edge exemption (`SpansSmallAngle`, Shewchuk 2002) only stops **triangle** splits.
- Nothing stops subsegment-against-subsegment encroachment at the apex.
- The concentric shells make the splits meet on common circles, but do not end the mutual encroachment.

## 2. The rule

**Cluster.** Two input sources form a small-angle pair when they share an endpoint X and meet at X at an input angle below the
**angle bound** θ (`MinAngle`), measured in exact arithmetic or with a margin that never classifies an angle at or above θ as small.
- Sources are input segments, ring edges, and the hull edges that `ConstrainHull` adds.
- On the snap grid, the input segment the snapped source comes from is used (`OriginOf`), as `SpansSmallAngle` already does, so the
  angles are the input's, the ones the oracle judges.

**Exemption.** In the constrained-quality mode, subsegment s (a piece of source a) is **not** treated as encroached by a vertex v, if:
- v lies on a chain of another source c (`SourcesAt(v)` contains c), and
- a and c form a small-angle pair.

Nothing else changes:
- encroachment by a free vertex, or by a vertex on a source that does not pair with a;
- the would-encroach test of a circumcentre or off-centre (split cause 2);
- the seditious-edge triangle exemption;
- the shells, lenses and grid rules.

**Threshold.** θ, not 60°:
- With θ the exemption only acts where the oracle's existing excuses (section 4) already apply.
- 60° (the probe's first threshold) would leave encroached subsegments next to input angles between θ and 60°, which no excuse covers.
- Measured, the gain is nearly the same:

| input | 60° | θ |
|---|---|---|
| island q30 | 741 | 786 |
| island q25 | 456 | 456 |
| cdt q30 | 14 273 | 14 273 |
| stmlf/1461 q20 | 117 628 | 117 706 |

**Conforming mode** keeps the cascade. O2 asks for an empty circumcircle against all vertices, and an encroached subsegment violates
it. The probe confirms this: NotDelaunayLocal on island and cdt. Whether O2 should be excused near small input angles is an open spec
question (#390 section 6, owner).

## 3. Termination

The literature, papers only. **Read so far: abstracts and published summaries.** The full texts of Shewchuk 2002 §6 and of Miller,
Pav and Walkington are to be read, and logged in MESHING2D_PROVENANCE.md, before the PR. Any statement below that the full text does
not support gets corrected then.
- **Ruppert's argument:** every insertion has an insertion radius bounded below by a constant times the local feature size. It fails
  only where two subsegments meet at an input angle below 60°: a vertex on one encroaches the other, and the radius shrinks by about
  the sine of the angle per step.
- **Shewchuk 2002 (§6, small input angles):** concentric-shell splitting, plus refusing the splits that this small-angle pair would
  otherwise trigger. The skinny triangles that cannot be removed then stay near the apex. Its termination proof covers the modified
  algorithm with these rules, at the price of the angle guarantee near small input angles.
- **Miller, Pav and Walkington** ("When and why Delaunay refinement algorithms work", IJCGA 2005): an adaptive variant terminates on
  any PSLG. Near an input angle θ* the output angle is bounded below by arctan(sin θ* / (2 − cos θ*)); elsewhere the usual bound
  holds.

How the rule fits that argument:
- The exempted encroachments are exactly those whose split would continue the small-angle pair's cascade: the encroaching vertex lies
  on the partner source of a pair below θ.
- Every remaining segment split is caused either by a vertex that is not on a partner source, or by a would-encroach centre. For
  these, Ruppert's lower bound on the insertion radius applies as before.
- Within a pair, the shells keep the remaining splits on common circles.

**Status: measured, not proven.** Our refiner differs from the analysed algorithms (off-centres, petal candidates, the grid, lenses,
lens deletion). Its termination is measured, not proven, as stated in `MESHING2D_GRADING.md`. This rule does not change that status:
it removes the one cascade the literature identifies, but adds no proof for our variant.

Measured so far, all terminating and VALID in constrained mode:
- island at 25° to 35°;
- cdt at 28° to 34°;
- stmlf/1461 at 20°, 25° and 30°;
- the 84-input corpus at 20° and 30°.

The PR adds Herne cq and the catalogue rows.

The noding bugfix (its own PR, first) fixes the one termination failure found on the way. It is independent of this rule.

## 4. The angle guarantee near the apexes, and the oracle

The rule leaves skinny triangles near small-angle apexes, which refinement used to split by going on for ever. The guarantee becomes:
- every triangle meets θ;
- except those whose small angle is forced by a small input angle, which O3 already allows.

The oracle excuses exactly those with **existing** rules. No oracle change is needed:
- **`ForcedByInputAngle`:** the triangle's small corner lies in the wedge of two constraints that meet at an angle below θ.
- **`SpansSmallInputAngle`:** the shortest edge's ends lie in the tubes of two input constraints meeting below θ, hull edges included,
  at distances from the apex within a factor of 1.5, on the same side. The mesher's `SpansSmallAngle` uses the same factor
  (2.24 ≈ 1.5² on squared distances).

A skinny triangle near an apex that neither excuse covers is still bad for the mesher (`BadByAngle`). It is still split: its centre
would encroach, and that split cause is not exempted. So the rule cannot leave an unexcused bad triangle behind on its own.

Measured: no AngleBelowBound on island, cdt, stmlf/1461 or the corpus. If the PR's runs (Herne, catalogue) show one, the rule gets
narrower; the oracle does not get wider. Any oracle change goes to the coordinator first.

## 5. What the PR measures and tests

- **Numbers:** island, cdt, stmlf/1461 at 20°, 25° and 30°; the corpus at 20° and 30° in constrained mode; Herne cq; the cq golden
  rows (every changed row listed, all VALID). For each: verdicts, pinches 0, triangles, time.
- **Tests:**
  - a procedural island-like ring (as in the noding bugfix), whose triangle count must drop well below today's (fails on master);
  - mutants: the exemption off; the threshold at 60°; the own-source and the shared-endpoint checks dropped;
  - invariance over `Similarity.All()`;
  - determinism;
  - Conforming mode unchanged, bit for bit.
- **Intentional behaviour change:** the output of `QualityMesh2` with `Conforming = false` and `MinAngle > 0` changes.

Expected effect at q30, constrained mode:

| input | today | with the rule | reference |
|---|---|---|---|
| island | 8 879 | 786 | 929 |
| cdt | 20 699 | 14 273 | 4 426 |
| stmlf/1461 | 390 342 | about 326 600 | 15 575 (constrained mode) |

At 20°, the corpus in constrained mode drops by 31 % in total, with a median of 0 % per run.

**Left open:**
- the near-contact features of cdt and stmlf (a vertex very close to a segment it does not touch);
- lenses up to 28.6° (Rand, CCCG 2011), later;
- the O2 question for the default mode.
