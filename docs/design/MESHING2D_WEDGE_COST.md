# The cost of a shallow wedge in the default (conforming) mode (design note, 2026-11-10, QA 2.0 T5)

**Decision (owner, 2026-11-10): D.** The default mode's cost of about 1/θ at a shallow wedge is documented as today's behaviour (it comes from rule C and the
independent splitting of A and B, not from O2; it is not a lower bound). The constrained quality mode is the answer for such inputs. No code. Option B
(matched shells) is recorded as a reopenable research item (section 5) with the independent exact-rational check.

**Question (owner option B of 2026-11-10):** can shell-matched split points make a small-angle wedge cost O(log 1/θ) in the default mode,
where it costs about 1/θ today? **Answer of this note (after an independent check of its first version): yes in principle, if the A and B split
points stay matched; no for the vertex sets that today's refinement produces, which cost 1/θ because their vertices are not matched.**
The first version of this note claimed 1/θ as a lower bound for any algorithm; the check refuted that (section 2). Nothing is implemented.

## 1. Measurement

Procedural, no mesh files. The unit square, a segment A along y = 1/2 and a segment B that starts on A's interior (a T-junction, so
the input has a vertex there) and ends 2^-k above A at the same x (angle θ ≈ 2^-k·2 radian), `QualityMesh2`, 30°, triangles:

| k | 5 | 10 | 15 | 20 | 25 |
|---|---|---|---|---|---|
| constrained quality mode (cq) | 40 | 38 | 38 | 38 | 38 |
| default (conforming) mode | 255 | 5 878 | 178 918 | 800 004 (Steiner limit) | 800 006 |

k = 12 is the 0.013° of stmlf/1461. Default mode: 1 584 triangles at k = 8, 5 878 at 10, 22 153 at 12: times 4 per 2 octaves, 1/θ.
A near miss (points 2^-k beside a segment they do not touch) and two segment ends 2^-k apart grow linearly in k in both modes (about 250
triangles per octave): fine. The T-junction fan (four such B segments) is linear in k in cq, 1/θ in the default mode.

Where the splits come from (k = 12, `CountsForBenchmarks`): 3 347 subsegment splits "encroached", of which 3 342 are rule C revocations
(slot 16: the exemption of a partner vertex is revoked because the subsegment is not locally Delaunay). The vertices lie along the whole
wedge, not at the apex: the A and B subsegments are about 3 to 4 wedge widths long everywhere (octave histogram of the vertices by distance from the apex:
none within 2^-5, 1 311 and 1 468 in the octaves 2^-3 and 2^-2 at k = 10; the A vertices face a B vertex within 10 % of the local spacing in 40 % of the cases).
The shell rule (a piece next to an input vertex is split at a power of two from it) is active at the apex; it does not change the count.

## 2. When the spacing is forced to be about the wedge width (and when not)

Let a, a' be consecutive vertices of A, spacing ℓ, in a wedge of width w ≪ ℓ at that place (w = θ·x for the distance x from the apex). O2
needs an empty circle through a and a' (every constrained edge is an edge of the Delaunay triangulation of all vertices). Put a, a' at (∓ℓ/2, 0);
the circles through them are centred at (0, c). A point X at horizontal offset t and signed height s (positive: towards B) lies inside the circle iff
t² + s² − 2sc < ℓ²/4, so its offset c(X) = (t² + s² − ℓ²/4)/(2s); X on the B side excludes c > c(X), X on the other side excludes c < c(X). An
empty circle exists iff every c(Q) of the other side is at most every c(P) of the B side.
- **The apex of the triangle below the edge.** Its apex Q is a vertex, so c(Q) is a bound whatever the rest of the mesh is. For the triangle (a, a', Q)
  with apex angle φ, c(Q) = −(ℓ/2)·cot φ; the 30° bound gives φ ∈ [30°, 120°], hence c(Q) ≥ −(√3/2)ℓ ≈ −0.866 ℓ.
- **A B-side vertex opposite the middle of the edge** (t = 0, height w) has c(P) = (w² − ℓ²/4)/(2w), and needs c(P) ≥ −0.866 ℓ, ie
  **ℓ ≤ (2√3 + 2√5)·w ≈ 7.5 w** (about 6.9 w without the w² term). More generally a B-side vertex at distance ε inside the end of the span
  (t = ℓ/2 − ε) has c(P) ≈ (w² − ℓε)/(2w): it forces ℓ ≲ 7.5 w unless ε is below about 0.6 w (an apex angle of 60°; 0.9 w at 30°).
- **So the 1/θ is forced only for a vertex set with a B-side vertex inside the span of an A-subsegment.** That is what the refinement produces
  today (independent splits of A and B: the measured spacing is 3 to 4 w, and only 40 % of the B vertices face an A vertex within 10 % of the
  spacing). Summed over the wedge, N ≥ ∫ dx / (7.5 θ x) = ln(L/x0)/(7.5 θ).
- **Matched split points avoid it.** If B's vertices are at the same radii from the apex as A's (the shells), they lie at t ≈ ±ℓ/2 of A's
  subsegments, c(P) ≈ w/2 > 0 imposes nothing, and the circle may bulge as far as the other side allows; ℓ is then limited only by the grading on the far
  side, not by w. Checked numerically (exact rational arithmetic, independent check, `/tmp` script not kept): vertices on A and B at radii r^i·10^-3
  for r = 1.5, 2, 3 and θ = 2^-6, 2^-12, 2^-18, far-side apexes of isosceles triangles with base angles about 50° below each A-edge and above each B-edge: every
  A-edge and B-edge has an empty circle, with 7 to 18 vertices per side for every θ. With the B vertices at the geometric mid-spans instead, every A-edge
  and nearly every B-edge fails, as above. Not checked: the full triangulation, and the 30° bound of the whole grading from r = 2 to 3.
- **Assumptions:** the wedge is interior to the domain (far-side triangles exist) and the far side is a plain quality region, not another wedge;
  w(x) = θx varies by a factor r across a span when ℓ ~ x (geometric grading), so the thresholds above must be recomputed with the heights actually used.

The same view explains the cq mode: constrained Delaunay ignores the far side, so nothing forces the spacing.

## 3. Consequence for the stmlf/1461 outlier

Its 6.8x (default mode, q30, 325 662 triangles, "even relative to the LFS yardstick") is then the price of unmatched A and B vertices in θ = 2^-12..2^-15
wedges, not of O2 itself. Not verified on the input (it is not in the repository).

## 4. What it does not say

- The matched construction is checked for the O2 condition of the edges and the apex triangles, not as a whole triangulation: the Steiner
  points that encroachment (rule C revokes) and circumcentres add inside the wedge must also stay matched, which is the hard part of an implementation.
  That the shell rule changes nothing today (section 1) shows the current implementation does not preserve the matching, not that it is impossible.
- The wedge needs a quality mesh on both outer sides; where the other side is itself narrow, the bound is the corridor's.
- Rule C itself is needed (its argument B in MESHING2D_SMALL_ANGLES_CONFORMING.md: every forced split is needed by any conforming solution).

## 5. Options (a decision for the owner)

- **B (shell-matched splits in the conforming wedge), research item, reopenable:** after a small-angle pair (the pair of `SpansSmallAngle`) is detected, split the
  subsegments of both sources next to the apex at the same distances from it (powers of two), and keep matched pairs together (a Steiner point on A
  in the wedge gets its partner on B at the same radius). Expected: the default-mode wedge falls from 1/θ towards the cq numbers. Needs a design with
  the termination argument (SMALL_ANGLES_CONFORMING section 4) and the interaction with rule C and the O11 merge zone, and a probe on the
  generators of section 1 and the F rows 4, 5, 26, 27, 38, 42 before any adoption; high effort, uncertain until probed.
- **D (document), chosen:** keep today's refinement, say in the spec and the roadmap that the default mode costs about 1/θ at a wedge of angle θ below the
  bound, with the numbers of section 1; the cq mode is the answer for such inputs. No code.
- **E (O2 in the wedge):** an O2 exception for subsegments that span a small input angle (constrained Delaunay inside the wedge). A spec change
  (O2's exception list, the oracle's conformity check) and mesher change (rule C does not revoke there); conformity across segments is what default-mode
  users rely on, so the exception would be narrow. Cheaper than B, weaker guarantee.
- **C (cap):** stop revoking the exemption below a wedge-width scale: not recommended, it hides a non-conforming output.

Recommendation (taken): D now (true and free); B as a probe if the owner wants the default mode to stay conforming at such inputs, E if conformity inside the wedge is
not needed.

## 6. Where D is stated

This note (section 1 has the numbers) and the roadmap line of the near-contact item in `docs/roadmap/meshing2d.md`. The spec text of O2 is unchanged: the
default mode keeps O2 at a wedge and pays for it; a user with such inputs and no need for conformity across segments uses the constrained quality mode.
