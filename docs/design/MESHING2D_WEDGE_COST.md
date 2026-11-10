# The cost of a shallow wedge in the default (conforming) mode (design note, 2026-11-10, QA 2.0 T5)

**Question (owner option B of 2026-11-10):** can shell-matched split points make a small-angle wedge cost O(log 1/θ) in the default mode,
where it costs about 1/θ today? **Answer of this note: no, not with O2 and the angle bound kept; the 1/θ is a lower bound for any algorithm.**
What can change it is a spec decision on O2 in the wedge (section 5). Nothing is implemented.

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

## 2. Why it is a lower bound (O2 plus a quality mesh on the other side)

Let a, a' be consecutive vertices of A, spacing ℓ, in a wedge of width w ≪ ℓ at that place (w = θ·x for the distance x from the apex). O2
needs an empty circle through a and a' (every constrained edge is an edge of the Delaunay triangulation of all vertices). The circles
through a and a' form a pencil, parametrised by the signed offset c of the centre from the chord (positive: towards B).
- A vertex P on the B side, at height w and horizontal offset t from the chord's middle, |t| < ℓ/2, lies inside the circle iff
  c > c(P) = (t² + w² − ℓ²/4)/(2w), about −(ℓ²/4 − t²)/(2w): very negative, so P allows only circles bulging towards the other side.
- A vertex Q on the other side of A at depth d, horizontal offset t', lies inside iff c < c(Q) = −(t'² + d² − ℓ²/4)/(2d).
- An empty circle exists iff c(Q) < c(P) for all pairs. With d ≥ ℓ this needs d > (ℓ²/4 − t²)/w, about ℓ²/(4w).
So no vertex may lie on the other side of A within depth ℓ²/(4w) ≈ ℓ·(ℓ/4w) of the edge. But the angle bound (30°) bounds the triangle
below the edge: its apex lies at depth at most about 1.9 ℓ (an isosceles triangle with the smallest allowed apex angle). Hence
ℓ/(4w) ≲ 2, ie **ℓ ≲ 8 w** everywhere along the wedge, and the number of vertices on A is at least ∫ dx / (8 θ x) = ln(L/x0)/(8θ). The
measured spacing of 3 to 4 w is inside that bound. Shell-matched split points (A and B split at the same distances from the apex, which
makes the strip of isosceles trapezoids, cyclic quadrilaterals) make the wedge side empty, but they do not change the circle on the other
side, where the condition above bites. They would help the cq mode's neighbours, which it does not need (38 triangles).

The same argument shows why the cq mode is constant: it asks for constrained Delaunay only (empty circles within the visibility of the edge),
so the vertices on the other side of A do not count.

## 3. Consequence for the stmlf/1461 outlier

Its 6.8x (default mode, q30, 325 662 triangles, "even relative to the LFS yardstick") is then not a defect of the refinement but the price of
O2 plus the angle bound at θ = 2^-12..2^-15 wedges; the reference (triangle.c) either does not keep O2 there or merges the features (it has
no tolerance: its 2^-15 features are real). Not verified: the input is not in the repository.

## 4. What it does not say

- The lower bound is for a wedge with a quality mesh on both sides; where the other side is itself narrow (a corridor), the bound is the corridor's.
- It does not bound the constant: 3 to 4 w measured against 8 w; a smarter choice of split points could save a factor, not the order.
- Rule C itself is needed (its argument B in MESHING2D_SMALL_ANGLES_CONFORMING.md: every forced split is needed by any conforming solution).

## 5. Options (a spec decision, owner)

- **D (document):** keep O2, say in the spec and the roadmap that the default mode costs Θ(1/θ) at a wedge of angle θ below the bound, with the
  numbers of section 1; the cq mode is the answer for such inputs. No code. The generator and the campaign family stay (item 1).
- **E (O2 in the wedge):** an O2 exception for a subsegment that spans a small input angle (the pair of `SpansSmallAngle`): empty circles are
  demanded within the wedge, not against vertices across the other side of the segment, ie constrained Delaunay there. It changes the spec
  (O2's exception list, the oracle's local and global conformity check), the mesher (rule C does not revoke the exemption for such a
  subsegment), and costs a probe; expected effect: the default mode at the wedge falls to the cq numbers (38 instead of 5 878 at k = 10). Risk:
  conformity is what downstream users of the default mode rely on (Delaunay across segments); the exception would be narrow (only at input
  angles below the bound, on subsegments whose both sides are covered by the pair).
- **C (cap):** stop revoking the exemption below a wedge-width scale. Same effect as E but without the spec statement; it would make the
  output not conforming at the wedge without saying so: not recommended.

Recommendation: D now (it is true and costs nothing), and E only if the owner wants the default mode to be usable on such inputs; E needs the spec
text first, and the probe (generators of section 1, the F rows 4, 5, 26, 27, 38, 42, the corpus where available) before any decision to adopt it.
