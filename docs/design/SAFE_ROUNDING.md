# SafeRounding: guarantee, degenerate input, snap rounding

Status: the bound (section 1) is implemented in `MeshRoundingRepair` (ROADMAP row R, item 1, owner decision D′,
November 2026). Section 2 (degenerate input, item 1b) is the plan for its own PR. Section 3 is the bounded evaluation of
the research item "Practical 3D snap rounding with guarantees" (item 4): a concept, nothing implemented.

`ExactMeshBoolean` computes its result exactly and rounds the new vertices to double. `SafeRounding`
(`MeshRoundingRepair`, on by default) then repairs what the rounding leaves: self-intersections and degenerate
triangles (area below `1e-12` of the squared diagonal, as `MeshRobustRepair.NotCleanReason`). It uses local edits,
each checked with exact predicates on the doubles. The edits follow the modification stage of Milenkovic and Sacks
2019: contractions, flips, tiny moves, and removal of tiny or thin closed components.

## 1. The guarantee (decision D′)

The owner decided:
- Below the degenerate threshold, SafeRounding contracts small features, also exact ones. Tiny triangles trouble
  slicers, FEM and remeshers, and a safe result is SafeRounding's purpose.
- Finding 1 of the Boolean property tests (`ExactBooleanPropertyFindings`) is therefore intended behaviour. The finding
  is a well-shaped exact triangle of size 2^-20; its contraction changes the volume by 2^-40 / 6.
- What is guaranteed: exactness above the threshold, and a **proven bound** on the displacement below it.

**Drift.** Every vertex has a drift d(v), 0 at the start of `Apply`. A proposed edit removes triangles R and adds
triangles A. Let δ bound how far the edit moves the surface. Applying the edit sets d(v) = max over the vertices u of R
and A of d(u), plus δ, for every vertex v of R and A. An edit is not applied if any of these drifts would exceed
`maxLength` = `MaxDisplacement` (1e-5) times the diagonal. `MaxDrift` reports the largest drift reached.

**The bounds δ per edit** (proofs in the code comments of `MeshRoundingRepair`):

| edit | δ | why |
|---|---|---|
| move, slide | the distance the vertex moves | a point of an incident triangle moves by its barycentric weight times that |
| contraction (b kept at p, a vanishes) | max(\|p − a\|, \|p − b\|) | the triangles of a and b map to the new ones with the corners moved by at most that; the two triangles at the edge collapse onto edges |
| flip of a-b to c-d | the smallest of: (1) 2 dist(segment ab, segment cd); (2) max(min(dist(c, abd), dist(d, abc)), min(dist(a, cdb), dist(b, dca))), each point-triangle distance bounded by the distance to the nearest edge; (3) 0 for an exactly flat convex quad | (1) moving c and d by the vector between the nearest points makes the diagonals meet: the quad becomes flat and convex, and both pairs of triangles cover it; (2) replace one corner by its nearest point in the other side's triangle, and the point lands inside that triangle |
| zip of two caps at a-b | 2 max(dist(c, ab), dist(d, ab)) | with the apexes moved onto a-b, the old and the new triangles cover the same set |

**Theorem.** Let S0 be the mesh at the start and S the repaired mesh, both without the closed components removed as
tiny or thin. Then the Hausdorff distance satisfies H(S, S0) ≤ `MaxDrift` ≤ `maxLength`, up to the rounding of the
distance computations (relative 1e-15).

*Proof by induction over the edits.* Invariant: every point of a current triangle T lies within max over v in T of d(v)
of S0, and every point of S0 lies within the drift of the vertices of some current triangle. An edit moves the points of
the triangles it changes by at most δ in both directions; the bounds in the table are two-sided. The vertices of those
triangles get at least the old maximum plus δ.

**Volume.** Closed solids differ only within distance `MaxDrift` of the edited triangles. The volume therefore changes
by at most about 2 `MaxDrift` times the area of the edited triangles. On top of that comes the volume of the removed
components, which their definition bounds:
- a thin component is thinner than `maxLength` (volume below `maxLength` times half its area);
- a tiny component has an area below 1e4 times the degenerate area.

**Not guaranteed.** That every case is repaired. `RoundingClean` false (`RemainingDegenerate` > 0) is still a
possible outcome, and the bound makes it more likely: edits that would move the surface further are not applied.
Milenkovic and Sacks give no bound in terms of the input either. Their displacement is "at most d times the number of
edits"; their words are "we lack a bound in terms of the input".

**What the bound rejects.** In `ExactBooleanIssueCasesTests.CylinderToolSteppedDown_RepeatedDifference` (six
repeated differences), the old repair flipped edges across a sharp edge of the tool, next to a tiny edge whose ends
nearly coincide. Such a flip folds two nearly coincident triangles across the edge, and bound (1) is 0.1 to 1 (on a
work piece of size 6). The old repair used these flips as intermediate steps; the bound rejects them. One degenerate
triangle then remains in steps 2 to 5 (`RoundingClean` false), while the drift of the other edits stays at 1e-6 or
below.

**Measured consequences:** see section 4, master against D′.

## 2. Degenerate input (design question 1b): plan

The owner decided that SafeRounding also repairs input that is already degenerate. `ExactMeshBoolean.Resolve` does
that today: the reproduction `ExactBooleanPropertyFindings.Resolve_DegenerateInputTriangle` (a triangle of area
2^-41 in a cube) gets contracted. Its skipped test asserts the opposite and will be rewritten like Finding 1 (clean,
within the bound).

**What the paper guarantees for such input.** These points are verified on the arXiv full text, 1805.03252:
- The sufficient condition: a mesh is d-separated if every pair of disjoint features (vertex-triangle, edge-edge) is
  more than d apart. Rounding moves every vertex by at most e = √3 M ε (M the largest coordinate, ε the unit
  roundoff). So a 2e-separated mesh rounds without intersections and keeps its topology. Degenerate input is not
  separated.
- Their modification stage contracts edges shorter than d, flips thin triangles, and removes components thinner than d.
  Their expansion stage then separates the remaining close features by iterated linear programs.
- Termination is proven in O(τ² log(d/δ)) steps, under the assumption that M/δ does not grow during the separation.
  The displacement bound is only d times the number of edits. The analysis of dense clusters of close features is
  "future work".
- So the paper does not promise "degenerate input ends clean". It promises a certificate when the expansion succeeds.

**Our pipeline against it.** We have the modification stage, plus moves and zips, and since D′ a displacement
bound. We have neither the expansion stage nor the d-separation certificate. The repair is verified by an exact
check of the result: intersections and the area threshold.

**Plan for the 1b PR** (acceptance from the coordinator):
- **Generated families of degenerate input:** zero-area and sliver triangles (collinear corners, a corner within 2^-k
  of the opposite edge), coincident vertices (unwelded and welded duplicates), self-touching sheets (a vertex on
  another face, two faces sharing a sub-region). Each family gets fresh meshes from printable parameters, in the
  style of `BOOLEAN_PROPERTY_TESTS.md`.
- **Expected per family:** `RoundingClean`, `MaxDrift` ≤ `maxLength`, and the same solid within `MaxDrift`
  (`BooleanOracle.SameSolid`). Families that fail are reported with the case and the defect left. They are not hidden.
- **Thingi10K:** the 400 set (`robust`) and BooleanComparison, before and after, with clean counts and cost.
- **Stop rule:** if the families show cases that no edit within the bound repairs, the doc lists them with numbers. The
  expansion stage of the paper (section 3, option A) is then the follow-up. A wider bound is not the answer.

## 3. Snap rounding with guarantees: evaluation (concept)

**Baseline, the current SafeRounding on Thingi10K.** MeshRepairBenchmark 400 set, `robust`, master `6ce2ad3`,
`BENCH_P=2 TIMEOUT=300 ./run_all.sh robust`, `analyze_paths.py` and `analyze_fidelity.py` (1 November 2026, under
load):
- 273 of 400 meshes are repaired; 2 timed out.
- The exact stage (resolve with SafeRounding) was tried on 225 meshes. It gave the result for 175, 103 of them
  faithful.
- It failed for rounding on 4: 3 with "degenerate triangles" left, and 1 more combined with an open input. It never
  failed for self-intersections left: `after_selfint_exact` is 0 on all 398 results.
- Its other failures are not rounding: 31 "volume differs from the input", 14 "not closed (winding number)", 1 resolve
  failure. All of these come from open inputs.

So on this benchmark rounding is not where the pipeline loses most. What is missing is a **guarantee**: today
the result is verified, not certified in advance, and the 4 residual cases are left as they are.

**Approaches.** Each entry: guarantee, cost, license. [V]: read in the primary source; [S]: from abstracts or snippets.

- **Devillers, Lazard, Lenhart**, *3D snap rounding*, SoCG 2018; *Rounding meshes in 3D*, DCG 64, 2020 [S].
  - Guarantee: a simplicial complex on the integer grid, Hausdorff distance at most 3/2 grid units, the relative
    position of features kept, no intersections.
  - Cost: the output has O(n^13) to O(n^15) elements in the worst case; the authors expect O(n√n) in practice. No
    implementation is known.
  - Verdict: the only full guarantee, impractical.
- **Milenkovic, Sacks**, *Geometric rounding and feature separation in meshes*, CAD 108, 2019 [V].
  - Guarantee: a certificate (2e-separation, checkable exactly) when the expansion converges. Displacement: median at
    most d, maximum 8d in their tests (d = 1e-6), but no a-priori bound.
  - Cost: 2e-5 to 0.02 s per triangle; the expansion uses linear programs (CPLEX in the paper).
  - Code: none published.
  - Verdict: we have its first stage. The missing piece is the expansion plus the certificate.
- **Valque, Lazard**, *Resolving self-intersections in 3D meshes while preserving floating-point coordinates*, CGF 44(5),
  2025; Valque's thesis, 2024 [S].
  - Method: iterative snap rounding of the vertices of the triangles that still intersect, to a scaled grid (in CGAL
    6.1 by default 2^23 cells per axis), then autorefinement, repeated.
  - Result: all 4524 self-intersecting Thingi10K models resolved; certified on 94 % in the thesis.
  - CGAL's documentation [V]: of 3870 Thingi10K inputs with intersections, naive rounding leaves 529 broken, the
    iterative snap rounding 0. The function still returns false when intersections remain, and the documentation says
    "no guarantee of its validity".
  - Its displacement is tied to the grid: about the bounding box times 2^-24 per round, far coarser than our
    1e-5 × diagonal on average.
  - **License: CGAL is GPL-3.0+.** Implement only from the paper, never from the code.
- **Zhou, Grinspun, Zorin, Jacobson**, *Mesh arrangements for solid geometry*, TOG 2016.
  - Method: round, check, resolve again. Succeeds on 99.95 % of 10k meshes [V via Milenkovic and Sacks]. libigl
    (MPL2); its CGAL part is GPL.
- **Cherchi et al. 2022** (MIT [V]) and **Lévy 2024/25** (geogram, BSD-3 [V]) do not solve the rounding; Lévy calls
  snap rounding "the main missing component". **Manifold** (Apache-2.0) avoids it with symbolic perturbation in
  floating point. **fTetWild** guarantees only an ε-envelope (1e-3 of the box), not the boolean's topology.

**Assessment (inference).** Three options for a guarantee of no new self-intersections with bounded displacement, at
practical cost:
- **A. Complete Milenkovic and Sacks: expansion and certificate.**
  - Method: after the modification stage, separate the remaining pairs closer than 2e (e = √3 M ε, about 4e-16 M) by
    small linear programs over the vertices of those pairs only, then check the certificate exactly. Every disjoint
    pair must be farther apart than 2e, an exact rational test on the doubles.
  - Guarantee when the certificate passes: rounding is proven safe, and the displacement is bounded by the steps
    taken; with D′, also by `maxLength`.
  - Cost: close pairs are rare, as the baseline above shows, so the linear programs are small. It needs a small exact
    or verified LP solver, written from the literature, since dependencies stay out of the core.
  - Risk: convergence for dense clusters (an open question in the paper).
- **B. Valque-Lazard iteration as the fallback**, for the cases where A fails. It needs no LP. It has no a-priori
  guarantee, but is very effective empirically. Its grid displacement can be bounded by `maxLength` the same way as
  D′ (reject a snap that exceeds the drift), which makes it a bounded, verified fallback.
- **C. Devillers et al.** is not practical.

**Proposed experiment, before any implementation.** The meshes are not committed; they are fetched by the benchmark.
- **Inputs:** the self-unions of all Thingi10K models with self-intersections (the input set of Valque-Lazard and of
  CGAL), and BooleanComparison `thingi10k`.
- **Per model:** exact arrangement; plain rounding (`SafeRounding = false`), which gives the base rate (published
  figures 527 of 4524, 529 of 3870); current SafeRounding; with D′; then A as a prototype, then B.
- **Metrics:**
  - clean and certified shares;
  - `MaxDrift`, and the measured largest displacement, in units of the diagonal and in ulps of M;
  - Hausdorff distance to the exact result;
  - removed components;
  - time.
- **Go criterion for A:** at least 94 % certified (the thesis' certified rate) at a displacement well below 2^-24 of
  the box, and the full pipeline (A + B) at 100 % clean within `maxLength`.

## 4. Measurements: master against D′

To be filled from the runs below (commands and raw files in the PR):
- BooleanComparison `compare.py --methods ours -j 1` on `cgal` (90), `cherchi` (13), `synthetic` (24): clean counts and
  the time of the booleans.
- MeshRepairBenchmark 400 set `robust`: repaired, exact stage outcome, time.
