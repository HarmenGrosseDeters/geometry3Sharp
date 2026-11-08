# Small input angles in the default (conforming) mode (design note, 2026-11-08)

**Owner decision 2026-11-08.** The default (conforming) mode gets the small-angle rule that
[MESHING2D_SMALL_ANGLES.md](MESHING2D_SMALL_ANGLES.md) (#394) has for the constrained-quality mode. O2 stays as it is: conforming Delaunay
everywhere, including the empty circles across holes. Only the angle bound is waived in the documented region around small input angles, and the
oracle judges it that way. This note comes before the code; a second, independent review of the termination argument (section 4) is wanted.

## 1. Why #394's rule cannot be copied bit for bit

#394's rule leaves a subsegment s unsplit although a refinement vertex v of a partner source lies in its diametral circle. In the constrained mode
that is fine: s is a constraint and need not be a Delaunay edge. In the conforming mode O2 asks for empty circumcircles, so s must be a Delaunay
edge: the apex angles of its two triangles must add up to at most 180°. A vertex v in the diametral circle has an angle above 90° at s, and the
other apex can make the sum exceed 180°.

**Probe 1 (throwaway, not in a PR; master 6f8a1b0 plus the change "ApexExempt also in the conforming mode").** Catalogue rows in the default mode,
`Meshing2Benchmark rows`, F-4, F-5, F-22, F-26, F-27, F-38, F-39, F-42:

| | rows ok | rows FAIL |
|---|---|---|
| master | all 19 | 0 |
| the rule unchanged in the conforming mode | 10 | 9: F-4/a, b, c, F-5, F-22/a, F-26, F-27, F-38/d, F-38/e, F-39, F-42 (`NotDelaunayLocal`; F-38/e also `NotDelaunayGlobal`) |

So the rule alone breaks O2 in the default mode, as expected.

## 2. The rule for the conforming mode ("rule C")

In `Encroached(e)`, an apex vertex v that the rule of #394 exempts for the subsegment e still counts as encroaching when e is **not locally
Delaunay**: the in-circle test of the apex of one triangle at e against the circumcircle of the other, exact (`ExactPredicates.InCircle`), over the
two triangles of the kernel's triangulation at e (holes and outside included, as the conforming mode already counts them; a ghost side counts as
Delaunay). Nothing else changes: the pair, the v condition, (i) and (ii), the causes 1 and 2, the shells, the grid rules, the constrained mode.

In words: the conforming mode leaves a subsegment unsplit for a partner vertex only while that subsegment is a Delaunay edge.

## 3. O2 is kept (this part is an argument, not a measurement)

Claim: when refinement stops without the Steiner limit, every constrained edge of the kernel's triangulation is locally Delaunay.
- A subsegment that no vertex encroaches has both apexes outside its diametral circle, so both apex angles are at most 90°, their sum at most
  180°: locally Delaunay. This is what the conforming mode relies on today.
- A subsegment encroached only by exempt vertices is, by rule C, locally Delaunay (else it would still be split).
- The unconstrained edges are locally Delaunay by the kernel's constrained Delaunay flips.

The kernel's triangulation is a triangulation of the convex hull of all vertices with every edge locally Delaunay, so by the Delaunay lemma it is
the Delaunay triangulation of all vertices: every circumcircle is empty, across holes and the outside too, since those triangles are in the kernel.
That is O2, up to the existing clauses (collinear within a few ulps, the merge zones, the grid's unsplittable pieces), which rule C does not touch.

Probe 2 (same rows, same state, rule C added): **all 19 rows ok.** Triangle counts, default mode:

| row | master | rule C |
|---|---|---|
| F-4/a | 5 449 | 3 937 |
| F-4/b | 16 218 | 13 048 |
| F-4/c | 71 412 | 56 595 |
| F-5 | 6 075 | 3 609 |
| F-22/a | 40 006 | 40 006 |
| F-26, F-27 | 1 624 | 432 |
| F-38/d | 22 344 | 21 957 |
| F-42 | 23 934 | 18 874 |

Rows not listed are unchanged. For reference, the constrained mode with #394: F-5 3 269, F-22/a 386, F-38/d 3 165, F-42 18 245. So rule C keeps
much of the gain on F-4, F-5, F-26/27 and F-42 and little on F-22/a and F-38/d (fans and narrow wedges, where the other apex of s often is a
nearly 180° neighbour, so s stops being Delaunay). The corpus (island, cdt, stmlf/1461, Herne) is not measured; that is the first measurement of the
PR, on a runner.

## 4. Termination: measured, not proven

#394 says the same of the constrained mode ("§6.2's termination argument does not carry over", MESHING2D_SMALL_ANGLES.md section 3). For rule C:
- The run is bounded by `SteinerLimit` and the step cap (`RefinementLimit`), so it never hangs; a run that reaches the limit is reported.
- Rule C splits at least the subsegments the constrained rule splits, and at most those the conforming mode splits today: at each state, the set of
  subsegments it treats as encroached lies between the two. That is a statement per state, not per run, so it does not give termination from the
  two ends; I do not have a proof and I do not claim one.
- What carries over from §6.2: the cure is applied only to a refinement vertex on a source incident to s's source (or meeting it), the situation of
  the figure-eight cycle. The forced splits of rule C (s not Delaunay) are splits that conforming Delaunay needs whatever the angles are.
- Open for the reviewer: is there a cycle in which a forced split (s not Delaunay) halves an apex piece, a circumcentre then lands near the partner
  piece, and the roles swap for ever? Probe 2 settles on every probed row; the corpus is the test.

## 5. The angle bound and the oracle

As in #394 the rule excuses no triangle. A triangle left is good, or excused by `SpansSmallAngle` (the mesher's seditious-edge exemption, at least as
strict as the oracle's `SpansSmallInputAngle`), or still bad and split by causes 1 and 2, or left by the existing exemptions. The oracle's
conforming check (local and global) is unchanged; probe 2's verdicts above came from the unchanged oracle. So **no oracle change** is expected, and
no spec change: O3's exception list stays. (The owner's wording "the angle bound is waived in the documented region" is thereby met by the existing
excuse; the rule does not widen it. If a wider region is wanted, that is the Terminator item of the ROADMAP, a spec change.)

## 6. Implementation, tests, measurements (after the review)

- **Code:** `ApexExempt` also runs in the conforming mode (the `options.Conforming` guard goes); `Encroached` adds the exact local test
  (`LocallyDelaunayAt`) only when `ApexExempt` fired, so the cost is zero elsewhere. Probe 2 is about 12 lines.
- **Tests:** the F-4, F-5, F-26 and F-42 rows (procedural) in the default mode with the oracle: VALID, and fewer triangles than with the rule off
  (`SmallAngleExemptionForTests`); a test that fails on the rule without the Delaunay check (probe 1's 9 rows are such inputs: the oracle says
  `NotDelaunayLocal`); invariance over `Similarity.All()` (2^k scales bit for bit); reuse (two `Compute()` on one instance); the constrained mode
  bit for bit unchanged; mutants: the Delaunay check dropped, the guard back, the ghost side counted as non-Delaunay.
- **Measurements:** golden M3 rows (they change; each change explained), the catalogue default-mode rows, the corpus at 20° and 30° in the default
  mode (triangles, time, verdicts, `SteinerLimitReached`) and Herne default mode, on a runner; the oracle at its normal setting.
- **Not in scope:** the Terminator, coarsening, grading, lenses (Rand's 28.6° does not apply to them).

## 7. Questions for the coordinator

1. Is "measured, not proven" acceptable for termination, as in #394? (My leaning: yes, with the limits above and the corpus run as the gate.)
2. Rule C keeps O2 and gives a smaller gain than the constrained mode (F-22/a and F-38/d almost none). Build it anyway? (Leaning: yes.) The
   alternative that reaches the constrained numbers is a spec change (O2 waived near small angles, or the Terminator with a region excuse), which the
   owner did not choose.
3. Should the apex rule also apply to hull edges and holes' sides in the conforming mode as in the constrained one? The probe did: no failures.
