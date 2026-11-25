# The certified empty ball as a rounding guarantee: stopped at the stop rule (research note, 2026-11-24)

Status: **research note, no library code; stopped by the stop rule of the brief.** The question: can the empty ball
give the double rounding of an exact Boolean result a proven guarantee (no new self-intersection, bounded
displacement)? Each vertex would get a radius r_v, certified by a lower distance bound to everything its star does not
share. A vertex that moves less than r_v would need no check, and a local fallback would run where the clearance is too
small. The probe is `benchmarks/SnapRoundingExperiment` (`ball`, `EmptyBall.cs`, `ball.py`). Its output is in
`trace/results/ball_leftovers_2026-11-24.jsonl` and `ball_control_2026-11-24.jsonl`. Measured on master `f2783d0`.
Every input is generated in code.

Read first and not repeated here: [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md) (all of it; 5.1 closes the
empty ball as a speed filter, 5.2 the clearance skins, 6.1 the d-separation certificate, 10 the plateau variant),
[SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](SNAP_ROUNDING_3D_GUARANTEE_2026-11.md),
[BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md](BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md) section 2,
[SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md) sections 7-9 and `tools/Z3Pilot`.

## 1. Result in short

- **The angle is not new.** As a guarantee, the empty ball is the d-separation lemma of Milenkovic and Sacks 2019, with
  one radius per vertex instead of one global d. SNAP_ROUNDING_3D.md 6.1 closed it as "adds nothing to a result that
  the exact search verifies anyway". Section 2 states the per-vertex form precisely. Its acceptance set is a subset of
  SafeRounding's: every configuration the balls certify is one the exact search also passes. So it cannot make a single
  result clean that SafeRounding leaves unclean. It can only make results clean that SafeRounding already certifies,
  either faster (closed in 5.1) or in advance (closed in 6.1).
- **The leftovers are exactly the places where the balls are too small, by an argument and by measurement:**
  - *argument:* by the lemma's contrapositive, a pair that rounding makes intersect is a pair whose clearance is below
    the displacement. Degenerate triangles and pairs that share a corner are outside any ball certificate, because a
    triangle's area depends only on its own corners and the distance of a pair sharing a corner is 0;
  - *measurement (exact, with fractions):* on the 15 of the 23 generated leftover results that SafeRounding still does
    not report as rounding clean today, **0 of the 186 leftover vertices are certified** under plain rounding. All 186
    have an exact clearance **below one ulp**: the largest is 0.26 ulp, the median about 0.005, and 47 are 0 (the exact
    result has a contact there). The only double inside any of their balls is the exact point itself, where that point
    happens to be a double (41 vertices). Everywhere else the ball allows no position at all.
  - Of the 151 intersecting pairs left, **135 share a corner position**. All **50** degenerate triangles left are already
    below the area threshold in the exact result: they need a corner moved by a median of 7e6 ulps, against a median
    corner clearance of 0.013 ulp.
- **The fallback would therefore be the whole problem.** It would have to handle every leftover vertex, and it is
  SafeRounding and its position search, which are built and measured.
- **The real-data leftovers are covered by the argument, not by this probe** (no meshes here). Both kinds fall outside
  the ball for the reasons above:
  - 838003 difference: one degenerate triangle, with 97 % of the bound used;
  - RC4b: the smallest repairing edit is 1.04-1.12 times `maxLength`, on near-coincident surfaces.
- **No lemma was checked with Z3.** Lemma L1 is one line of convexity, and its consequence was checked exactly on the
  data instead: 0 violations of the contrapositive on 7 935 intersecting pairs of plain rounding. Nothing survives
  that would need the rest of the brief (a probe of the fallback, a Z3 obligation file).

## 2. The angle, stated precisely

Exact result K: triangles with exact rational corners p_v, intersecting only in shared corners and edges (contacts of
the exact result aside). The rounded positions are q_v, with |q_v − p_v| ≤ δ_v. For a triangle T, δ_T is the largest
δ_v over its corners, and d is the Euclidean distance of closed triangles.

**L1 (pairs that share no corner).** If d(A_p, B_p) > δ_A + δ_B, then A_q and B_q are disjoint.
*Proof:* a point of A_q is Σ λ_i q_i = Σ λ_i p_i + Σ λ_i (q_i − p_i), within δ_A of the point Σ λ_i p_i of A_p. The same
holds for B, and the triangle inequality gives the claim.

**Per-vertex form (the empty ball).** Let c_T be the smallest d(T_p, B_p) over the triangles B that share no corner
with T, and let c_v be the smallest c_T over the star of v. If every vertex moves by δ_v < c_v / 2, then for each such
pair δ_A + δ_B < max(c_A, c_B) ≤ d(A_p, B_p), so L1 applies to every pair that shares no corner.

**What L1 does not cover, so a scheme needs exact local checks for it:**
- pairs that share a corner or an edge: d = 0, so no radius works, and the check is the exact pair test on the rounded
  star;
- degenerate triangles: the area is a function of the triangle's own three corners, and the check is the exact area
  test.

A scheme of the brief's form is therefore "balls for the pairs that share no corner, exact checks of stars and areas".
SafeRounding's verifier is "exact checks of stars and areas, and exact tests of every pair that shares no corner and
whose boxes overlap". The scheme accepts a configuration only if the verifier does: an inclusion of acceptance sets.
What the balls could add:
- (a) an a-priori statement for plain rounding: the d-separation of 6.1. Per vertex it certifies a few more vertices
  than the global d = √3 ulp, but a clean result is already certified exactly afterwards
  (SNAP_ROUNDING_3D_GUARANTEE 4.1);
- (b) speed: the filter of 5.1, below 10 % before the cost of the certificate;
- (c) a region in which a vertex may move without a check (the skins of 5.2). On the leftovers, (c) is what section 3
  measures: the region holds no double.

## 3. The probe

`SnapRoundingExperiment ball <case>` runs the boolean twice:
- once with SafeRounding off and `ComputeExactVertices` on, which gives the exact complex and its exact vertices;
- once with the default, which gives the leftovers, mapped back to the exact vertices by their exact point.

It lists every pair that shares no corner and whose distance in doubles is below 16 ulps. `ball.py` then decides
everything with fractions:
- the exact squared triangle distance, in rationals: 0 if an edge of one triangle meets the other, else the smallest
  of the nine edge pairs and the six corner-triangle distances;
- the exact displacement of nearest rounding;
- L1 for each pair;
- for each leftover vertex, its clearance and the move SafeRounding made.

A pair that is not listed is farther apart than any δ_A + δ_B of plain rounding, which is at most √3 ulp.
`ball.py selfcheck` checks the distance on six hand-computed cases.

**The 23 generated leftovers of the guarantee note**, rerun on `f2783d0`: 8 are now rounding clean (contacts only) and
15 are not. The table below has the 15 (`ball.py tables <file> --unclean`):

| case | triangles (exact) | triangles certified by L1 | vertices certified | leftover vertices | of them certified | their clearance, ulps: median, largest | SafeRounding's move of them, ulps: median, largest |
|---|---:|---:|---:|---:|---:|---|---|
| Cluster:0:8:3 | 676 | 55 % | 43 % | 10 | 0 | 0.00015, 0.0026 | 2.2e8, 6.3e10 |
| Cluster:36:8:1 | 724 | 60 % | 49 % | 23 | 0 | 0.0016, 0.21 | 0.43, 6.8e9 |
| Cluster:0:8:1 | 706 | 55 % | 41 % | 6 | 0 | 0.0069, 0.018 | 4.3e8, 6.0e10 |
| Cluster:30:8:1 | 668 | 68 % | 55 % | 4 | 0 | 0.0040, 0.0040 | 0.69, 3.1e7 |
| Cluster:30:8:2 | 716 | 56 % | 40 % | 35 | 0 | 0, 0.0093 | 219, 5.2e9 |
| Cluster:30:8:3 | 724 | 57 % | 41 % | 6 | 0 | 0.0035, 0.013 | 14.5, 4.9e6 |
| Cluster:30:8:5 | 634 | 69 % | 56 % | 8 | 0 | 0.048, 0.12 | 0.17, 0.25 |
| Cluster:36:8:0 | 684 | 68 % | 53 % | 8 | 0 | 0.0068, 0.0069 | 3.9e8, 3.9e9 |
| Cluster:40:8:2 | 684 | 64 % | 51 % | 13 | 0 | 0.00033, 0.019 | 0.37, 2.4e9 |
| Cluster:52:8:1 | 744 | 50 % | 35 % | 16 | 0 | 0.011, 0.074 | 1.9e8, 6.0e10 |
| Cluster:52:8:5 | 630 | 62 % | 50 % | 7 | 0 | 0.00058, 0.26 | 0.19, 7.9e5 |
| FoldedCluster:36:8:3 | 732 | 57 % | 44 % | 19 | 0 | 0.0034, 0.027 | 3.5e3, 3.7e9 |
| FoldedCluster:44:8:1 | 684 | 65 % | 52 % | 11 | 0 | 0, 0.022 | 0.56, 1.2e7 |
| FoldedCluster:44:8:2 | 684 | 62 % | 45 % | 11 | 0 | 0.0096, 0.016 | 1.9e9, 6.0e10 |
| FoldedCluster:52:8:1 | 714 | 62 % | 49 % | 9 | 0 | 0.0011, 0.0075 | 224, 2.3e8 |
| **all 15** | 10 404 | 61 % | 47 % | **186** | **0** | all below 1 ulp; 47 are 0 | `maxLength` is about 3e11 ulps |

- **The leftovers.** None of the 186 leftover vertices is certified:
  - 145 of them have a δ_v that is larger than half their clearance;
  - the other 41 sit on an exact point that is a double (δ_v = 0), so their ball allows only "do not move", and a
    neighbour in their star is not certified.
- **The defects left:**
  - 50 degenerate triangles, all degenerate in the exact result already (exact area at most 5e-7 of the threshold).
    They need at least 1e6 to 1e8 ulps of motion, and the largest clearance among their corners has a median of
    0.013 ulp;
  - 151 intersecting pairs, 135 of them sharing a corner position. The 16 pairs that share none are 0.0002 to 0.15
    ulps apart in the exact result.
- **The rest of these results.** 39-65 % of the vertices are not certified even away from the leftovers: these are
  dense clusters of nearly coincident sheets, built for this.
- **A control** (10 rounding-clean cases: `scale` 11 and 21, three `bool` families, `cylinder:3`, three clusters of 2
  and 4 parts): 99 % of the triangles and 98 % of the vertices are certified, 100 % on every `scale` result. These are
  results that the exact search certifies anyway.
- **The check of the script.** Every pair that plain rounding makes intersect and that shares no corner fails L1: 7 935
  pairs over the 23 results, 0 violations.

Limits: generated clusters only. The real leftovers (838003, RC4b) are argued, not measured. The clearance is that of
the exact result; the clearance after a partial repair was not computed. It cannot help here, because the vertices'
neighbours are below one ulp in the exact result already.

## 4. Prior art (papers only; [V] read, [S] abstract or snippet, [M] from memory)

| Source | What it gives |
|---|---|
| V. Milenkovic, E. Sacks, *Geometric rounding and feature separation in meshes*, CAD 108, 2019, arXiv 1805.03252 [V in SAFE_ROUNDING.md 8.1; S here] | "moving each vertex of a d-separated mesh by at most d/2 cannot make triangles intersect"; rounding moves a vertex at most √3 M ε, so 2e-separation is the certificate. This is L1 in its global form. Where the mesh is not separated, they *expand* it first by linear programs (our option A, closed in #313) |
| D. Halperin, E. Packer, *Iterated snap rounding*, CGTA 23, 2002 [S] | the 2D analogue: every vertex half a pixel from every non-incident edge after rounding, reached by changing the output (more snapping), not by a certificate on a fixed one |
| D. Halperin, C. Shelton 1998; S. Funke et al., SODA 2005; K. Mehlhorn, R. Osbild, M. Sagraloff, *A general approach to the analysis of controlled perturbation algorithms*, CGTA 44, 2011 [S] | guards: a predicate is safe when its value is farther from 0 than the error. Where a guard fails, the *input* is perturbed again until all guards pass. That needs the freedom to perturb by more than the gap; our bound is fixed, and on the leftovers the gap is below the grid |
| L. Verlet 1967; X. Zhang, M. Lee, Y. J. Kim 2006 (conservative advancement) [S, SNAP_ROUNDING_3D.md 3]; R. Bridson, R. Fedkiw, J. Anderson, *Robust treatment of collisions, contact and friction for cloth animation*, SIGGRAPH 2002 [M] | a distance lower bound over a motion upper bound gives a safe step; where it is too small, simulation falls back to an exact (continuous) collision check. That is the structure SafeRounding has already, with the box overlap as the "too close" test |

Searched on 2026-11-24 ("certified clearance vertex rounding floating point mesh self-intersection", "controlled
perturbation guard distance to degeneracy"): no method that turns a per-vertex clearance into a rounding guarantee
where the clearance is below the grid. The 2025 works found (Valque and Lazard; CGAL's announcement, documentation
only) round and verify.

## 5. Verdict and the cost of a next step

- **Not a new angle.** It is L1, the d-separation of Milenkovic and Sacks per vertex, and it accepts only what the
  exact verifier accepts.
- **No help on the leftovers.** They sit exactly where the clearance is below the displacement. On the measured ones it
  is below one ulp, so the fallback is everything. The defects left are mostly of the kinds no ball covers:
  pairs that share a corner (135 of 151) and degenerate triangles (50, degenerate before any rounding).
- **Lemma:** yes, trivial (L1). It has no use beyond 6.1, so no Z3 file was written.
- **Next step:** none recommended, at no cost.
  - The only way past the sub-ulp clearances is to change the output: expand (option A, closed), merge sheets
    (Devillers et al., the guarantee note 4.4), or collapse (today's removal rule).
  - The theory effort of option 6 of the guarantee note stays the only path to a real guarantee. This probe gives it one
    constraint: a lemma has to cover clearances below the grid and pairs that share a corner, which a ball cannot.
- **Cost if someone wants more data:** running `ball` on the real leftovers needs their meshes on a runner, about 1 h.
  The result is predicted by the argument of section 2.

## 6. Reproduce

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release
cd benchmarks/SnapRoundingExperiment
python3 ball.py selfcheck
python3 ball.py run out/ball.jsonl cluster:Cluster:30:8:1 cluster:FoldedCluster:36:8:3   # the cases: section 3, or "clean": false in trace/results/bound_2026-11-22.jsonl
python3 ball.py tables trace/results/ball_leftovers_2026-11-24.jsonl --unclean
python3 ball.py tables trace/results/ball_control_2026-11-24.jsonl
```

The 23 cases take 2.5 min on one core, almost all of it in the fractions.
