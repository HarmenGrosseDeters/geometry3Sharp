# SafeRounding: guarantee, degenerate input, snap rounding

Status: the bound (section 1) is implemented in `MeshRoundingRepair` (ROADMAP row R, item 1, owner decision D′,
November 2026). Section 2 (degenerate input, item 1b) is the plan for its own PR. Section 3 is the bounded evaluation of
the research item "Practical 3D snap rounding with guarantees" (item 4): a concept. Section 7 holds the full-text notes on
Valque and Lazard, section 8 the experiment (prototypes of options A and B behind internal options, the measurements,
and the verdict per criterion of the owner).

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
- What is guaranteed: triangles the repair does not touch are the exact result, rounded. Every edit, also of
  well-shaped neighbours (flips, contractions, moves), stays within a **proven bound** of the rounded result.
  `MeshRobustRepair` can run a second repair with a fresh budget: there the total is at most 2 `maxLength`.

**Drift.** Every vertex has a drift d(v), 0 at the start of `Apply`. A proposed edit removes triangles R and adds
triangles A. Let δ bound how far the edit moves the surface. Applying the edit sets d(v) = max over the vertices u of R
and A of d(u), plus δ, for every vertex v of R and A. An edit is not applied if any of these drifts would exceed
`maxLength` = `MaxDisplacement` (1e-5) times the diagonal. `MaxDrift` reports the largest drift reached.

**Scale (2026-11-08, Exact Boolean 2.0, the far-part defect).** The diagonal in the degenerate area and in
`maxLength` is that of the largest connected component (vertices joined by triangles), not of the box of all
triangles; the ulp term is per component, from the largest coordinate of its own triangles. Before, a small part far
from the rest (a unit tetrahedron 1e8 away from two unit spheres) made the diagonal 1.7e8: every sphere triangle was
degenerate, the bound 1732, and the union came out empty, reported clean. Edits stay within one component, so the drift
bound is checked with the edit's component and the theorem below holds per component. Tiny components are still
removed relative to the largest one (as decided above); the limit of this: a long thin component still sets the
diagonal for all (a rod of length 1e8 makes a unit part tiny, and it is removed), which D′ accepts. A diagonal that is
not finite (coordinates that are not) makes the size NaN whatever the order of the components: every edit is rejected
and nothing is removed as tiny or thin (an infinite size would remove every closed component as tiny). A closed component that the bound takes as thin but that holds
real volume for its area (`|V| >= 1e-3 A^1.5`; a sphere has 0.094) is never removed: it is kept and counted
(`ScaleDisparity`), and the repair is not clean. `MeshRobustRepair.NotCleanReason` keeps the box of all triangles, so
for components far apart compared to their size it can flag triangles that the rounding keeps.

**The bounds δ per edit** (proofs in the code comments of `MeshRoundingRepair`):

| edit | δ | why |
|---|---|---|
| move, slide | the distance the vertex moves | a point of an incident triangle moves by its barycentric weight times that |
| contraction (b kept at p, a vanishes) | max(\|p − a\|, \|p − b\|) | the triangles of a and b map to the new ones with the corners moved by at most that; the two triangles at the edge collapse onto edges |
| flip of a-b to c-d | the smallest of: (1) 2 dist(segment ab, segment cd); (2) max(min(dist(c, abd), dist(d, abc)), min(dist(a, cdb), dist(b, dca))), each point-triangle distance bounded by the distance to the nearest edge; (3) 0 for an exactly flat convex quad | (1) for any p on ab and q on cd, moving c and d by p − q makes the diagonals meet at p. Both pairs of triangles then cover the same flat convex quad, or the same triangle if p or q is an endpoint (rounded nearest points are fine). (2) Replace one corner by any point of the other side's triangle, and the point lands inside that triangle by convexity. |
| zip of two caps at a-b | 2 max(dist(c, ab), dist(d, ab)) | with the apexes moved onto a-b, the old and the new triangles cover the same set |
| strip zip (strip with ends e0, e1) | 2 max over the strip's vertices of dist(v, line e0 e1) | with the strip's vertices moved onto the line (in the order of their computed parameters), the strip is a segment covered by the merged chain, and each outer triangle and its split cover the same set. Needs: all strip vertices, the inner ones too, between e0 and e1 along the line; both boundary chains strictly monotone; the outer triangles distinct, their third corners not in the strip (they do not move); the distances taken relative to e0 (exact for nearby points), so that w does not carry the rounding error of the coordinates' size. The bounds review measured H ≤ 0.91 · 2w (exact distances, 674 rechecks of 9039 strip zips) and a volume change ≤ 0.18 of δ times the replaced area |

**Theorem.** Let S0 be the mesh at the start (the rounded mesh) and S the repaired mesh, both without the closed
components removed as tiny or thin. Then the Hausdorff distance satisfies H(S, S0) ≤ `MaxDrift` ≤ `maxLength`, up
to the rounding of the distance computations. That rounding error is absolute: a few ulps of the largest coordinate
M per edit, not relative to the distance (the bounds review found a computed segment distance 0 for a true 1.15e-14
at coordinates near 100). It is negligible against `maxLength` = 1e-5 · diag unless M/diag is large. Relative to the
exact boolean result, the rounding of the vertices adds at most √3/2 ulp.

*Proof by induction over the edits.* Invariant:
- every point of a current triangle T lies within max over v in T of d(v) of S0;
- every point of S0 lies within the drift of the vertices of some current triangle.

Each edit moves every point of the triangles it removes to within δ of the triangles it adds, or of a simplex spanned by
the edit's vertices, and back; the bounds in the table are two-sided. The vertices of those triangles get at least the
old maximum plus δ, so drifts never decrease, and untouched triangles that share those vertices stay valid.

Special case, the ear: a contraction of a vertex with a single triangle adds no triangle. That triangle goes to the edge
across, which lies in an untouched triangle with both ends among the edit's vertices. Edits never create, merge or split
components, so the theorem holds per component. "Removed components" are the components of S0 whose descendants were
removed.

**Volume.**
- *Moves and contractions:* the change is a third of the move times the area vector of the link. Proven: |ΔV| ≤ δ ·
  area of the replaced triangles.
- *Flips and zips:* the change is the volume between the old and the new triangles (for a flip, the tetrahedron
  abcd). The bounds review measured at most 0.33 (flip), 0.28 (zip) and 0.18 (strip zip) of δ times the replaced area. That is a
  measurement, not a proof.
- *In total:* about the sum over the edits of δ_e · area(R_e), plus the volume of the removed components, which
  their definition bounds:
  - a thin component is thinner than `maxLength` (volume below `maxLength` times half its area);
  - a tiny component has an area below 1e4 times the degenerate area.

"2 `MaxDrift` times the area" is not proven: the band around degenerate triangles has Steiner terms, edge length times
`MaxDrift`². The tests use it only as a sanity check.

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

**Look before you build (the strip zip and the coarse coordinates).**
- *Library:* `MeshPostRepairCleanup` collapses needles and flips caps, without a displacement bound; `MeshRoundingRepair`
  has contractions, flips, moves and the zip of two caps. Nothing removes a strip of slivers, where every flip gives
  slivers again.
- *Literature:*
  - Milenkovic and Sacks [V] have no strip edit: their modification stage contracts, flips and removes thin
    components, and their expansion stage separates the close features that are left.
  - CGAL's `remove_almost_degenerate_faces` [V, documentation; GPL] collapses needles and flips caps, with a filter
    that can cancel an edit. It reports the faces it could not remove for topological reasons: the same vocabulary as
    ours.
  - Botsch and Kobbelt 2001 [S] remove degenerate faces with a slicing technique and numerically stable predicates
    (their pages were not reachable from this session).
  - Attene 2010 (MeshFix) [S]: removes growing regions of bad elements and patches the gaps; heuristic, with no
    convergence guarantee and no displacement bound.
  - No source found for a bounded edit on a strip. The strip zip is the bounded local counterpart of Attene's region
    removal: refilling the hole of a strip only gives slivers again, so both sides are merged into one chain instead.
- *Coarse coordinates:*
  - The snap-rounding methods work on a grid scaled to the bounding box: Valque and Lazard (CGAL 6.1, 2^23 cells per
    axis) and Devillers et al. (an integer grid). In effect they compute in local coordinates.
  - GIS and 3D-tiles practice is to store coordinates relative to a local centre [from memory, not checked here].
  - Neither makes a result representable at world coordinates whose features lie below the world ulp.

**Result of the 1b PR.** `SafeRoundingDegenerateInputTests`, through `ExactMeshBoolean.Resolve` and Booleans. The poked
and split spheres keep the surface of the sphere they come from (their new vertices lie in its faces), so each result is
compared with the sphere (`SameSolid` within `MaxDrift`); the others with the exact resolve without SafeRounding.
- **Families, 564 cases:** collinear pokes (84), slivers at 2^-30 to 2^-52 (72), short edges (72), coincident
  vertices (84), unwelded duplicates (84), a vertex on a face (84), boxes sharing a face (84). All end clean, within the
  bound and the same solid, before and after this PR. Two expected exceptions:
  - a vertex exactly on a face stays a point contact, as in the exact result (two shells touching in a point,
    "self-intersections"), with drift 0;
  - boxes 2^-30 to 2^-52 apart have nothing to repair; their result is the exact one, bit for bit (`SameSolid` probes
    farther than the gap, so it is not used there).
- **Dense clusters** (a triangle poked 2 to 8 times in a row, each new vertex near an edge of the triangle it pokes):
  strips of slivers along lines, where every flip gives slivers again. They need a new edit, the **strip zip**
  (`MeshRoundingRepair`, section 1): the zip of two caps generalized to a strip of degenerate triangles and slivers, a
  disk whose boundary runs monotone along the segment between its two farthest vertices. Bound 2 w, w the largest
  distance of the strip's vertices to the line through its ends. Clean results, master → this PR:

  | set | cases | clean before | clean after |
  |---|---|---|---|
  | clusters, offset 2^-k of the way inside | 108 | 51 | 85 |
  | folded clusters, offset 2^-k absolute (the surface folds over itself) | 90 | 41 | 80 |
  | Booleans (a box cuts the poked and split spheres, all families, union, intersection, difference) | 198 | 183 | 197 |

  All of them, clean or not, stay within the bound and are the same solid as the sphere, before and after.
- **What is left (stop rule):** 23 + 10 clusters and 1 Boolean (a short edge of 2^-44, union) keep degenerate triangles,
  most with a drift far below the bound (down to 1e-15). Their strips are no disk with a monotone boundary: they branch
  (a Y of strips), or they fold in layers on one line, so that the boundary passes a vertex twice. No local edit within
  the bound repairs them; the zipped result of a folded strip would need two sheets on one edge. This is where the
  expansion stage of the paper (section 3, option A) or a vertex snap that merges the layers would come in, as the
  follow-up. The bound stays as it is.
- **Beyond degenerate input:** the strip zip also repairs most of two known limits of the D′ bound: the rotated
  coplanar grid (`InvarianceTests`, 3 slivers) is now clean, and of the repeated cylinder differences
  (`CylinderToolSteppedDown_RepeatedDifference`, ROBUSTNESS_SWEEP R22: one degenerate triangle per step from step 2)
  steps 3 to 5 are clean. Step 2 keeps its triangle: zipping it with its neighbour, a sliver with an edge of 2e-9,
  leaves a needle that a contraction would remove next, but each edit must improve on its own.
- **Coarse coordinates (known limit):** a cluster at scale 1e-3 near UTM coordinates (up to 5.7e6; one unit in the
  last place, 9.3e-10, is 4.7e-7 of the mesh's size, while its slivers are about 1e-13) is already folded by rounding
  the input; two intersecting pairs are left, within the bound and the same solid all the same. The same input moved
  exactly to the local origin (v − T, exact by Sterbenz) resolves clean with no edit: the arrangement is exact in both
  places, but rounding its new vertices to the world grid gives 33 intersecting pairs instead of 0. So the result is not
  representable at world coordinates with the precision its features need, and no internal local origin helps the
  world output. Practice, as in GIS: work in local coordinates. Since the
  ulp tolerance of section 5, 108 of 114 such cases are clean, within 251 ulps.

- **Scale invariance (bugfix, 2026-11-02, found by the snap-rounding experiment).** The moves of `fix_degenerate` and
  `fix_intersection` took their directions from `Normalized` and `GetTriNormal`, which return 0 below the absolute
  `MathUtil.Epsilon`, and the first step of a vertex at the origin was an absolute 1e-300. So a cap whose apex lies
  1e-17 off its base got no move at scale 1 but one at 2^10: 88 of the 198 dense clusters differed at 2^-10 or 2^10.
  The directions are now threshold-free unit vectors, computed as `Normalize` does (the same bits wherever its threshold
  did not bite), and the first step at the origin is ulp(maxLength). After it no cluster differs, also at 2^±60
  (`SafeRoundingBoundTests.Clusters_PowerOfTwoScales_SameResultScaled`, `CornerAtTheOrigin_*`). Results changed only
  where the threshold bit:
  - dense clusters 85 → 89 of 108 clean (folded 80 of 90), Booleans 197 → 198 of 198;
  - the cylinder step 2 (R22) is clean: all six steps;
  - BooleanComparison `profile --repeat 1 --safe-rounding` (cgal, thingi, cherchi_small, grid:5, spheres:20): 261 of 263
    hashes identical; the other two, `cgal/cheese` intersection and difference, are now clean (5 and 4 degenerate
    triangles before);
  - the strip test's counterfactual (no strip zip) is now clean with 16 moves at 5 times the drift of the strip zips.

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
  2025 [V, HAL preprint `hal-04907149v1`]; Valque's thesis, 2024 [V]. Full-text notes: section 7, paper against
  thesis: section 7.8.
  - Method: iterative snap rounding of the vertices of the triangles that still intersect, to a scaled grid (in CGAL
    6.1 by default 2^23 cells per axis), then autorefinement, repeated.
  - Result: all 4524 self-intersecting Thingi10K models resolved (one with a coarser grid). The thesis' *certified*
    algorithm solves 94 % of the 527 hard models only with a relaxed test that drops the topological guarantee, 64 %
    with full certification (section 7.2).
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
- **No-go:** if A misses that criterion, A is dropped and B alone, bounded by the drift, is the fallback.

## 4. Measurements: master against D′

Both tools are built from the same master (`6ce2ad3`), so the comparison measures only the bound. Measured on 1 November 2026, on 4 cores.

**Clean counts.** BooleanComparison, `compare.py --methods ours -j 1` and `analyze.py`:

| class | master | D′ |
|---|---|---|
| `cgal` (90) | 65 clean, 1 self-int., 24 not closed (open inputs) | the same |
| `cherchi` (13) | 13 clean | 13 clean |
| `synthetic` (24) | 23 clean, 1 self-int. | the same |
| RoundingClean, `cgal` operations | 268 of 270 | 268 of 270 |

**MeshRepairBenchmark, 400 set.**
- Command: `robust`, `BENCH_P=2 TIMEOUT=300`, then `analyze_fidelity.py` and `analyze_paths.py`.
- The meshes that timed out under machine load were rerun alone with the same settings: 1037029, 1505037 and 229959 for D′; 498458 and 55928 for master.
- **Repaired: 274 → 273.** The exact stage gave the result for 176 → 175 meshes.
- The one difference is 1102667. A degenerate triangle stays (the bound), so the exact stage fails, and the fallback takes 121 s instead of 16 s.
- Together with R22 (CylinderTool) this is a single case, not a drop.

**Cost.** A warm A B A B profile on a quiet machine:
- Command: `BooleanComparison profile --repeat 3 --safe-rounding perf/cases-thingi12.txt perf/cases-cherchi_small.txt`, two rounds, master and D′ in turn.
- The rounding phase (`rounding`) takes 3.83 s → 4.05 s in total (×1.059); the median per case and operation is ×1.057.
- The unchanged phases of the same runs vary as much: intersections ×1.03, setup ×1.07, assembly ×1.06. So the cost of the drift bookkeeping is within the noise, below about 5 %.
- On these cases SafeRounding is 31 % of the boolean.
- 48 of 51 result hashes are identical. The 3 that differ are all thingi/471993, with the same number of edits: there the bound picks other flips.

## 5. Coordinates far from the origin: a magnitude-aware tolerance (design, approved)

Owner decision (via the coordinator): give results at world coordinates, eg UTM, a bound like D′. Features below the
tolerance need not be represented faithfully, as long as the error stays tiny and bounded and the result is
topologically clean. A strict mode reports an error instead.

**Owner decision of 2026-11-01:** this design is approved with the proposed defaults: `UlpFactor` 64,
`AbsoluteTolerance` 0 (callers set it, eg 1e-6 m), `StrictRounding` off, and the weld edit (δ = 0, only where the result
stays manifold, otherwise reported). It is built as its own PR after 1b.

**Owner decision of 2026-11-02**, after the data on the 1b code (below): `UlpFactor` 256 instead of 64, and the removal of
a vertex of valence 3 instead of the weld.
- *Why 256:* at 64, 11 of the 114 cases keep intersecting pairs, because their edits need 196 to 251 ulps.
- *Why no weld:* at 256, 9 of the cases left have two vertices of valence 3 on one sheet at one position, not joined by an
  edge. A weld would make a bowtie there.

**As built** (`MeshRoundingRepair`, `ExactMeshBoolean`):
- `UlpFactor` 256 and `AbsoluteTolerance` 0; maxLength is the largest of the three terms, `MaxLength` reports it. On
  `ExactMeshBoolean` they are `RoundingUlpFactor` and `RoundingAbsoluteTolerance`.
- **Coarse coordinates:** where the ulp term exceeds the relative one,
  - `ExactMeshBoolean` does not keep its fixed vertices there (`FixedWhereCoarse` false): they are the rounded input
    vertices, which carry rounding errors of the size of the bound there themselves, and keeping them made the edits on
    the others cascade beyond it (at 256, 8 cases needed 257 to 261 ulps). For a public caller of `MeshRoundingRepair`,
    `FixedVertexF` keeps applying (`FixedWhereCoarse` true by default). It is a preference anyway ("kept where possible",
    tried last as the moved vertex; contractions and strip zips can still remove fixed vertices);
  - after the passes, a vertex of valence 3 at the position of another vertex is removed, its three triangles replaced
    by the one of its neighbours, if the result improves (`VertexRemovals`). The passes then run again.
- **Bound of the removal:** δ = dist(u, (a, b, c)). With u moved to its nearest point q in the closed triangle, the
  three triangles (q, a, b), (q, b, c), (q, c, a) tile it, and the bound is tight. It is computed as |u − q|.
  `DistPoint3Triangle3.Get` would not do: its closed form subtracts terms of the triangle's squared size, so distances
  below about 1e-8 of that size come out as 0 (bounds review; regression test
  `VertexRemoval_BoundIsTheTrueDistance_FarFromTheOrigin`).
- **Ordinary meshes:** none of this applies unless the largest coordinate exceeds about 1.75e8 times the diagonal. So
  their results stay bit for bit the same (`OrdinaryMeshes_UlpToleranceChangesNothing`), and the 2^k scales keep giving
  the same result bit for bit, since ulp(2^k M) = 2^k ulp(M).
- **`StrictRounding`:**
  - the repair runs with the relative bound only;
  - if the result is not clean, Compute fails with `BooleanStatus.RoundingNotClean` and Result null;
  - Error gives the defects left, where (`RemainingDefectAt`), and the drift that the smallest edit rejected by the
    bound needed (`SmallestRejectedDrift`).
- **Effort budget** (`MaxPairTests`; `RoundingMaxPairTests` on `ExactMeshBoolean` and `MeshRobustRepair`,
  `--rounding-max-pair-tests` in g3cli): once the exact triangle pair tests of the evaluations exceed it (the last
  evaluation may pass it by its own tests), the repair applies no more edits (`EvaluationLimitReached`); the result is
  valid, its defects counted. Owner decision of 2026-11-02: 5M instead of 20M. Measured after #327: no clean result lost or gained (generated families 1340 of 1512,
  Thingi10K self-unions 336 of 799, six rotated Thingi10K booleans 0 of 18 at 5M to 80M), and less time where the
  budget is reached (families 241 to 145 s, self-unions 1925 to 1774 s, rounding of the rotated cases 193 to 96 s). The
  hash gate (`profile --repeat 1 --safe-rounding`, 263 runs) reaches it nowhere and stays identical.

**Result on the 114 UTM cases** (`SafeRoundingBoundTests.Utm_CleanWithTheUlpTolerance_WithinIt`):

| | clean |
|---|---|
| relative bound only (before) | 92 |
| `UlpFactor` 256 | **108** |

The largest drift is 251 ulps (0.23 µm), and 10 vertices were removed.

The 6 left (stop rule):
- 2 cases keep intersecting pairs: the edits they need, 360 to 370 ulps, are beyond the bound;
- 2 keep a contact at a coincident position whose vertices both have a higher valence;
- 1 keeps a contact with a tiny separate component;
- 1 keeps an inside-out tiny component.

**The cause** (section 2, ROBUSTNESS_SWEEP R23): at UTM coordinates (up to 5.7e6) one ulp is 9.3e-10. Rounding the
exact result's new vertices to that grid folds features below it. The same input at the origin resolves clean. The
relative bound, 1e-5 of the diagonal, is 3.5e-8 for a mesh of 3.5e-3, only 37 ulps: too tight to repair what the
rounding broke.

**Data for k.** `MeshRoundingRepair` with maxLength = max(1e-5·diag, k·ulp(M)), M the largest coordinate. Input: 114
UTM cases (clusters, folded clusters, collinear pokes and slivers of section 2, at scale 1e-3, rotated, near UTM),
resolved without SafeRounding, then repaired:

| k | rounding clean (`IsClean`) | clean (`NotCleanReasons`) | largest drift |
|---|---|---|---|
| 0 (today) | 94 | 94 | 37 ulps |
| 16 | 94 | 94 | 37 ulps |
| 64 | 103 | 101 | 59 ulps (55 nm) |
| 256 | 112 | 101 | 251 ulps (0.23 µm) |
| 1024 | 113 | 102 | 982 ulps (0.9 µm) |

At the origin, all 114 are clean. The gap between the two clean columns comes from 11 cases that are rounding clean
but count as "self-intersections" for `MeshRobustRepair`: the rounding put distinct vertices of touching sheets onto
one double position. The repair accepts such contacts (`SharedPositionsTouch`); the clean check does not. One more
case is an inside-out tiny component. So a larger k alone does not finish the job. It needs one more bounded edit
(item 2).

**Design.**
1. **Tolerance:** maxLength = max(`MaxDisplacement`·diag, `UlpFactor`·ulp(M), `AbsoluteTolerance`). The defaults
   are the owner's decision; proposed: `UlpFactor` 64 (55 nm at 5.7e6), `AbsoluteTolerance` 0 (GIS callers set eg
   1e-6 m).
   - The D′ proof is unchanged: only the bound grows. Hausdorff ≤ `MaxDrift` ≤ maxLength, plus the absolute
     rounding error of a few ulps per edit.
   - ulp(2^k M) = 2^k ulp(M), so the 2^k scales keep giving the same result bit for bit.
   - On ordinary meshes the new term never triggers: 64·ulp(M) ≈ 1.4e-14·M stays below 1e-5·diag unless M/diag
     exceeds about 7e8. So their results and hashes stay bit for bit the same.
2. **Collapse below the tolerance**, with the existing bounded edits (contraction, flip, zip, strip zip, each with its
   D′ proof). One new edit: **weld** vertices that the rounding put onto one position, when the merge keeps the mesh
   manifold. It has δ = 0, since no vertex moves; where it would make a non-manifold vertex, the contact is reported.
   - Guarantee: closed, manifold, no self-intersections, every point within maxLength of the rounded exact result.
   - Reported: `MaxDrift`, the edits per kind, and the number of welds.
3. **Strict mode** (`StrictRounding`, default off): only the relative bound applies. If the result is not clean, the
   status is a failure with the reason, the location (centroid of a defect) and the smallest drift a rejected edit
   would have needed (recorded per defect at the drift check). No result is returned.
4. **API, additive only** (1.0.324 compatible):
   - `MeshRoundingRepair.UlpFactor` and `MeshRoundingRepair.AbsoluteTolerance`, also on `ExactMeshBoolean`, passed
     through to its repair;
   - `ExactMeshBoolean.StrictRounding`;
   - one new `BooleanStatus` value for a strict failure;
   - `MeshRoundingRepair.Welds`.
5. **Evidence for the PR:**
   - the 114 UTM cases (target: as clean as at the origin), including the k30 cluster at world coordinates;
   - Thingi10K-400 and BooleanComparison clean counts, unchanged or better;
   - the result hashes of the ordinary classes, bit for bit the same.

**Look before you build.**
- *Library:* `MeshRoundingRepair` already scales every bound with the diagonal. Its absolute rounding error is
  documented (class comment, REVIEW_CHECKLIST §5). `ExactMeshBoolean` keeps coordinates as given, with no origin or
  grid.
- *GIS libraries* [V, JTS documentation]:
  - JTS works on a fixed-precision grid (`PrecisionModel` with a scale). Its snap rounding (`SnapRoundingNoder`,
    after Hobby and Guibas-Marimont) assumes that all vertices lie on that grid.
  - `GeometryPrecisionReducer` rounds to the grid and makes the output valid. In OverlayNG, components closer than the
    grid precision may merge.
  - GEOS has the same design.
  - That is the same contract as items 1 and 2: below the tolerance, features collapse, and the output is valid.

## 6. Handoff for the snap-rounding experiment (2026-11-02)

Stage R ends here: D′ (#251), 1b (#279), the ulp tolerance (#287), the pass-through API (#294). The next item is the
research experiment of section 3: option A (the Milenkovic–Sacks expansion and its certificate), then B (a
Valque–Lazard iteration bounded by the drift). It is a measurement and prototypes only; the owner decides on an
implementation from the numbers. This section is the handoff to a fresh session.

### 6.1 What is left, and how to reproduce it

All cases are generated in code, so no mesh files are needed.
- **Strips that branch or fold** (1b, section 2, "What is left"):
  - `SafeRoundingDegenerateInputTests.DenseClusters_WithinTheBound_MostlyClean`: dense clusters 85 of 108 clean,
    folded clusters 80 of 90. The pinned minimums are 85 and 80. The 23 + 10 left are within the bound and the same solid.
  - `SafeRoundingDegenerateInputTests.DegenerateInput_InBooleans_CleanWithinTheBound`: 197 of 198; the one left is
    ShortEdge 2^-44, union, seed 0.
  - Make a case with `SafeRoundingDegenerateInputTests.Make(Family, k, seed, count)` (public), resolve it with
    `new ExactMeshBoolean(mesh, new DMesh3(), Ops.Union)`, and read `Rounding` (RemainingDegenerate,
    RemainingIntersections, MaxDrift, MaxLength, SmallestRejectedDrift).
  - The cases left are not listed one by one: loop over the grid of the test and print those with
    `RoundingClean == false`.
  - Diagnosis so far: the strip's boundary passes a vertex twice (a Y of strips), or the strip is no disk. Folded strips
    lie in layers on one line, where a zipped result would need two sheets on one edge.
- **Six UTM cases** (section 5): `SafeRoundingBoundTests.Utm_CleanWithTheUlpTolerance_WithinIt` (108 of 114). The
  names are those of `SafeRoundingBoundTests.UtmCases()`, ExactMeshBoolean defaults:
  - `Cluster k30 count 4 seed 1` and `FoldedCluster k36 count 4 seed 0`: 2 to 3 intersecting pairs left. The edits
    rejected by the bound would need 360 to 370 ulps (`SmallestRejectedDrift`).
  - `Cluster k30 count 4 seed 0` and `FoldedCluster k30 count 4 seed 0`: rounding clean (`IsClean`), but a contact of
    two vertices of higher valence at one position remains, which `MeshRobustRepair.NotCleanReasons` counts as
    "self-intersections".
  - `FoldedCluster k36 count 4 seed 2`: a contact with a tiny separate component.
  - `Cluster k30 count 4 seed 2`: an inside-out tiny component.
- **The cylinder, step 2** (R22): `ExactBooleanIssueCasesTests.CylinderToolSteppedDown_RepeatedDifference` pins
  exactly 1 degenerate triangle in step 2.
  - The triangle is a cap on the tool's vertical seam (x = 1.47), with a long edge of 1.
  - Its neighbour across the long edge is a sliver with an edge of 2.03e-9 (area 1.01e-9, above the degenerate
    threshold of about 1.7e-10).
  - The strip zip of the two is rejected by `improves`, because it leaves a needle with that edge (newB = oldB). A
    contraction would remove the needle next, but each edit must improve on its own.
  - This is a case for a two-step edit, or for the separation of option A.
- **R23** (`ROBUSTNESS_SWEEP.md`): the UTM row above. Its cause: rounding the exact result's new vertices to the world
  grid. The same input moved exactly to the origin is clean.

### 6.2 The experiment, tightened

**Inputs:**
1. The generated families above: dense and folded clusters, the 114 UTM cases, the cylinder. They are small and fast,
   good for prototypes.
2. Thingi10K self-unions: `benchmarks/MeshRepairBenchmark/fetch_data.py` gives the 400 set in `data/meshes`. Resolve
   each with `ExactMeshBoolean.Resolve` / `new ExactMeshBoolean(m, new DMesh3(), Union)`. The models with
   self-intersections are the input set of Valque–Lazard and CGAL.
3. BooleanComparison: `python3 data/fetch.py cgal data_cgal` and `cherchi`, `dotnet $BOOLCMP_TOOL synth synth`, then
   `compare.py <manifest> <out> --methods ours -j 1`.

**Metrics per model and method:**
- `RoundingClean` and `NotCleanReasons`;
- `MaxDrift` against `MaxLength`, also in ulps of the largest coordinate;
- for A: the certificate passed or not;
- the removed components, the time, and `SameSolid` against the unrounded resolve.
- The baseline is the current SafeRounding; also report plain rounding (`SafeRounding = false`).

**Baseline numbers that already exist:**
- section 3 "Baseline", the 400 set, robust: 273 of 400 repaired, 4 rounding failures;
- section 4 and #251's body: D′ against master (BooleanComparison clean counts, MeshRepair 400, the cost);
- #279's body: 1b against #251, BooleanComparison identical, MeshRepair 87 meshes with SafeRounding edits:
  87 → 87, 18 with other edit counts;
- #287's body: the ulp tolerance, 263 of 263 profile hashes identical, the 114 UTM cases 92 → 108.

**Order:**
1. **A prototype** on the generated families: only the remaining close pairs, small LPs, and an exact check of the
   certificate (2e-separation).
2. **B as the fallback** on what A leaves: snap to a scaled grid and resolve again, with each snap rejected beyond the
   drift bound.
3. Then the Thingi10K set, with the go and no-go criteria of section 3.

### 6.3 Pitfalls met in stage R

- **Bounds:**
  - Compute every distance as |p − q| of a nearest point, relative to a nearby point (the difference of two nearby doubles is exact, or nearly, by Sterbenz).
  - Never use `DistPoint3Triangle3.Get()` for a bound: its closed form subtracts terms of the triangle's squared size,
    so distances below about 1e-8 of that size come out as 0 (#287's bounds review).
  - Never use `Normalized` in a filter that must scale exactly: it has an absolute threshold.
  - Errors are absolute, a few ulps of the largest coordinate per edit, not relative (REVIEW_CHECKLIST §5).
  - A bound that is NaN or infinite must reject the edit (`improves`, `maxLength`).
- **Drift bookkeeping:**
  - `improves()` takes the drift from the vertices of the removed ∪ added triangles; `commit_drift()` follows only
    after the edit is applied.
  - The drift is checked before the evaluation budget is charged.
  - A new edit must be in `begin_edit`'s removed / added / addedFrom lists, or its vertices miss the drift.
  - Every new δ needs the proof in the class comment and in the table of section 1, and a bounds review with exact
    rational fuzzing: the double distance has false ratios.
- **"Each edit must improve on its own"** (`newI + newB < oldI + oldB`, or `<=` for a contact removal): this blocks
  two-step repairs (the cylinder). Relaxing it needs a termination argument.
- **Mutation:**
  - `stryker-mesh-boolean-1/2.json` split `ExactMeshBoolean.cs` at a character offset, at the start of
    `front_winding` (50611 after #294). Any edit before it moves the split. `MutationConfigTests` fails, and the
    config edit makes both shards run in full.
  - Any edit of `.github/workflows/mutation.yml` runs every shard in full (`select_shards.py`, `ALWAYS`).
  - Name new tests so that they fall under the existing filters and globs. `SafeRoundingBoundTests` and
    `MeshRoundingRepair*Tests` are in the `rounding` filter; `SafeRoundingDegenerateInputTests` is not, on purpose:
    it is slow.
- **Do not touch:**
  - `MeshRobustRepair`'s wrap path (stage 7b);
  - `mesh_ops/ExactMeshArrangement.cs` (the performance work);
  - the defaults of SafeRounding, without an owner decision: the results of ordinary meshes must stay bit-identical,
    and the hashes are checked with `BooleanComparison profile --repeat 1 --safe-rounding ...` and
    `perf/hashes.py`.
- **`FixedVertexF`** is a public preference. `FixedWhereCoarse` (default true) keeps it where the coordinates are
  coarse; only ExactMeshBoolean turns it off.
- **The cloud container stops when the session idles.** Background jobs die with it. The full MeshRepair 400 needs
  hours of an active session, so prefer subsets: eg the meshes with SafeRounding edits,
  `robust_exact_rounding_edits` in a previous result file. Make runs resumable, appending one line per result.

### 6.4 Open question: an exact or verified small LP solver

Option A separates the remaining close pairs with small linear programs over the displacements of their vertices.
Dependencies stay out of the core.

**Key point:** correctness does not need an exact LP. The certificate (2e-separation of every disjoint pair) is
checked exactly on the doubles afterwards. So the LP may be solved in floating point, and a wrong or infeasible answer
only means that the certificate fails, after which B takes over. What an exact or verified LP would add is a proof of
infeasibility (no displacement within the bound separates the pairs), which a report could use.

Candidates:
- **Dense simplex in floating point, with Bland's rule.** The LPs are small, with the vertices of the close pairs only.
  It is written from the textbook, plus a final exact feasibility check of the rounded solution with rational
  arithmetic (`BigInteger` / `Dyadic`, as in the tests' oracles).
- **Exact rational simplex:** Applegate, Cook, Dash, Espinoza, *Exact solutions to linear programming problems*,
  Operations Research Letters 35, 2007 (QSopt_ex). Implement from the paper: QSopt_ex itself is GPL / LGPL, not to be
  ported.
- **Iterative refinement:** Gleixner, Steffy, Wolter, *Iterative refinement for linear programming*, INFORMS Journal on
  Computing 28, 2016 (SoPlex). Its code is not under an allowed license; the paper only.
- **Safe bounds:** Neumaier, Shcherbina, *Safe bounds in linear and mixed-integer linear programming*, Mathematical
  Programming 99, 2004. A rigorous bound on the optimum from an approximate dual, cheap. It is enough to certify
  infeasibility.

These references are from memory and were not checked in this session; verify them and their licenses
(docs/SOURCES.md) before use.

## 7. Valque–Lazard: full-text notes (2026-11-02)

Read for option B (sections 3 and 6). Notes in our own words; no code was read, CGAL's (GPL) neither. Sections
7.1–7.7 were written from the thesis; section 7.8 compares the CGF 2025 paper with it.

### 7.1 What was read

- **Read in full text [V]:** L. Valque, *3D Snap rounding*, PhD thesis, Université de Lorraine, 2024, NNT 2024LORR0337,
  HAL `tel-05016163`, version 1, <https://hal.science/tel-05016163> (PDF: `/document`, 183 pages, PDF modified
  2025-03-04). Supervisor S. Lazard (GAMBLE, Inria / LORIA). Read: chapters 4.3, 5.2, 12, 13, 14.2, 14.5, 16.1–16.3,
  16.8, 17 (all sections).
- **Paper, read later the same day [V]:** S. Lazard, L. Valque, *Resolving self-intersections in 3D meshes while
  preserving floating-point coordinates*, Computer Graphics Forum 44(5), 2025, DOI 10.1111/cgf.70197. Read in full
  as the HAL preprint `hal-04907149v1` (the only version on HAL), title *Removing self-intersections in 3D meshes
  while preserving floating-point coordinates*, submitted 2025-01-22, 14 pages (13 + HAL cover), CC BY 4.0,
  <https://inria.hal.science/hal-04907149v1/document>. The published CGF version was not read; it may differ from
  the preprint. Comparison with the thesis: section 7.8. Access history (before `inria.hal.science` was allowed):
  - Eurographics digital library: <https://diglib.eg.org/items/df2f3916-37f1-47a1-aebb-6e798372c42f/full>. The
    session proxy refuses `diglib.eg.org` and `diglib7.eg.org` (connect_rejected).
  - HAL preprint: `hal-04907149`, version 1, title *Removing self-intersections in 3D meshes while preserving
    floating-point coordinates*, 2025, <https://hal.science/hal-04907149>. The record page loads on `hal.science`
    (abstract read [V]); the PDF is served from `inria.hal.science`, which the proxy refuses.
  - HAL's search pages answer with a bot check, and `api.archives-ouvertes.fr` is refused: no other companion report
    could be searched for. Not circumvented.
- **Also not reachable:** the CGAL 6.1 blog post (`www.cgal.org` does not resolve in this session).

**The paper is the thesis' heuristic (chapter 13)**, with a default grid one bit finer and an iteration limit of 5
instead of 20; it adds no automatic coarsening, no other vertex set and no new bound. Its comparison "Valque [2024]"
is the thesis' certified algorithm (part II). Details in section 7.8. (An earlier version of this section, written
before the paper could be read, suspected that the paper was a different method.)

### 7.2 Corrections to section 3 (which was written from abstracts)

- **"Certified on 94 %" is not quite what the thesis reports.** The certified 3D snap rounding (thesis part II, a
  local version of Devillers et al.) was run on the 520 Thingi10K models whose rounding to the integer grid scaled to
  [2^51, 2^52) gives proper intersections during the motion:
  - with full certification: 187 of 520 did not finish in 1 h (1 crash in the arrangement, 26 crashes in the code,
    46 infinite loops, 37 growing intersections, 77 unclear). So 333 of 520 (64 %).
  - with a relaxed intersection test (only features of triangles that properly intersect *at the end* of the motion
    are tested; a triangle that passes through another during the motion is missed, so the topological guarantee is
    lost): 52 of 520 fail in 1 h, 33 (6 %) in 10 h. The 94 % is this figure.
  - the author calls the implementation unstable (16.8).
- **The grid is not coarsened per iteration** in the thesis' heuristic. It is fixed; a coarser grid was chosen by hand
  for one model (section 7.3).
- **"Thingi10K 100 %":** the heuristic resolves 4523 of 4524 self-intersecting models with the default grid; the last
  one (Thingi10K 996816) with a coarser grid. Of the 527 hard models, 1 is left with the default grid, 0 with the
  coarse grid, 6 with the fine grid.

### 7.3 The heuristic (thesis chapter 13), precise enough to implement

Terms: two triangles **properly intersect** if their intersection is non-empty and is not a common vertex, edge or
face of both. Input: a triangle soup with double coordinates (no closedness or winding assumption). The arrangement
step is a **subdivision along the self-intersections** (every triangle kept, interior ones too), not a self-union; the
thesis notes that a self-union, as in Zhou et al., is probably more efficient where it is defined.

0. Resolve the self-intersections of the input exactly; round the new vertices to double (the "naive rounding").
   If no proper intersection remains, stop. (Fails on 527 of the 4524 self-intersecting Thingi10K models, 12 %.)
1. **Grid.** Scale so that the largest absolute coordinate lies in [2^22, 2^23); the grid is the integer lattice of
   the scaled coordinates. Voxels are unit cubes centred on the lattice points, so rounding a coordinate to the
   nearest integer moves the point to its voxel's centre. The thesis says "the largest absolute value of each
   coordinate", and the scaling is applied per coordinate just before rounding and undone right after (footnote 48):
   we read this as a scale per axis, each chosen from that axis' largest absolute value. That the factor is a power of
   two is not stated in the thesis; the paper states both: a power of two, per axis (section 7.8). The thesis calls
   2^22..2^23 the largest scale at which the integer part is a `float`; that is off by one bit (a `float` holds every
   integer up to 2^24), and the paper's default is [2^23, 2^24) with exactly this justification. Chosen to compare
   with Zhou et al.'s rounding to `float`. Grid unit (thesis): 2^-23 to 2^-22 of the axis' largest absolute
   coordinate, "about 100 nm for an object one metre wide"; paper: 2^-24 to 2^-23.
2. **Vertex set V:** all vertices of all pairs of triangles that properly intersect in the current (double) mesh.
3. **Hot voxels:** add to V every vertex of the mesh that lies in a voxel containing a vertex of V.
4. **Snap:** round every coordinate of every vertex in V to the nearest integer (in the scaled coordinates). Other
   vertices are not moved.
5. **Arrangement again:** subdivide the pairs of triangles that properly intersect after the snap, exactly.
6. **Round the new vertices** of step 5 to double (not to the grid).
7. **Repeat** steps 2–6 while proper intersections remain, at most a fixed number of times (20 in the thesis'
   experiments, 5 in the paper, with a time limit; no successful run needed more than 3).
8. Undo the scaling.

Implementation choices reported: exact predicates (so a result reached within the limit is checked intersection-free
exactly) and exact constructions (to control the distance; exact predicates with inexact constructions would also
certify the output but lose control of the distance). Serial, no parallel code.

Why it works, per the author: snapping to a coarse grid collapses needles and other tiny features (most
self-intersections involve triangles with tiny edges), and the new points are built from coordinates of less than
half the input precision, so they lie farther apart. Rounding to `float` (Zhou et al.) fails mostly when coordinates
are near zero, where floats are fine: the uniform grid fixes exactly that.

### 7.4 What is certified and what is heuristic

- **Certified (checked, a posteriori):** when the loop ends before its limit, the output has no proper intersection,
  checked with exact predicates on the double coordinates.
- **Bounded:** the displacement. Each iteration moves a snapped coordinate by at most 1/2 grid unit, and only snapped
  vertices move; new vertices are additionally rounded to double. The thesis states "at most 1/2 times the number of
  iterations" (in grid units, before scaling back) and, in table 17.2, a maximum distance of k · 2^-23 (default grid),
  k · 2^-16 (coarse), k · 2^-30 (fine), relative to the largest coordinate, for k iterations (k · 2^-52 for the
  iterative naive rounding). This is per coordinate (L∞); the Euclidean bound is √3 times that. Our reading: the
  sum over iterations is a triangle inequality, so the bound holds for the original input vertices; a vertex created
  in iteration j moves at most k − j more times. No Hausdorff bound beyond this is stated.
- **Not guaranteed:** an intersection-free output when the iteration limit is reached; the topology (vertices collapse
  into one point, a triangle can pass through another while snapping); no statement about closedness, manifoldness or
  winding numbers is made.

### 7.5 Results (thesis chapter 17, Thingi10K, serial)

| Method on the 527 hard models | unsolved | solved with > 1 iteration | max iterations | output size vs ours | time vs ours | max distance |
|---|---|---|---|---|---|---|
| iterative naive rounding (20 iterations, 1 h) | 287 | 527 | 18 | 1.08 | 2.16 | k 2^-52 |
| Zhou et al. variant (subdivision instead of self-union) | 48 | 368 | 17 | 1.06 | 1.63 | k 2^-23 |
| heuristic, grid [2^22, 2^23) | 1 | 29 | 3 | 1 | 1 | k 2^-23 |
| heuristic, coarse grid [2^15, 2^16) | 0 | 22 | 3 | 0.99 | 0.95 | k 2^-16 |
| heuristic, fine grid [2^30, 2^31) | 6 | 106 | 8 | 1.02 | 1.08 | k 2^-30 |

- Default grid on all 4524: all but one finish in under 30 minutes; 26 need two iterations, 3 need three (plus 16
  trivial models with two). The text of 17.1 gives 284 unsolved for the iterative naive rounding, the table 287.
- Running time is dominated by the arrangement, about 1 s per 10^4 of "input size" n + 2k (n vertices, k
  intersecting pairs); power fit 21 x^1.09 µs. Output size about 0.79 (mean) times n + 2k.
- **Thingi10K 996816** (76k vertices, 171k triangles, 5.0 million intersecting pairs): the arrangement crashes with
  the default grid. Grid [2^15, 2^16): solved, about 5 million vertices, 30 million faces, about 40 min. Grid
  [2^14, 2^15): one iteration, 272k vertices, 3 min, distance at most about 30 µm for a 1 m object.
- **Hot voxels matter:** without step 3, 58 models need two iterations, 10 need three, and 2 fail (996816 and a small
  one, Thingi10K 105867, whose intersections grow with the iterations); one trivial model (225354) fails as well.
- **Rounding everything** to `float` or to the grid before resolving is much worse: about 42 % fail once, 24–25 %
  after 20 iterations. Only the vertices of intersecting pairs (and their hot voxels) must be snapped.
- **Stress test, rotated cubes** (a randomly rotated unit cube added per step, all triangles kept): the heuristic
  needs 1 to 3 iterations up to step 30 (about 10^6 vertices, 1.3·10^5 intersecting pairs), and fails (> 5 h) at
  step 31; there the iterative naive rounding is as good as the coarse grid, because dense meshes make the snapped
  vertices cross many faces. The paper runs a different cube test (boolean intersections, section 7.8) with a much
  better result; the two are not comparable.

### 7.6 Failure modes and parameters

- Failure modes: the arrangement blows up (millions of pairs, triplets of triangles); intersections growing from
  iteration to iteration (seen mainly without hot voxels, and with the fine grid); the iteration limit reached (then
  no guarantee); very dense configurations (rotated cubes). Any input can be solved by a coarse enough grid (in the
  limit one voxel collapses everything), at the cost of distance.
- Parameters: the grid exponent (default 2^22..2^23 of the largest coordinate; the author recommends a scale matching
  the model's significant digits); the iteration limit (20; useful runs need at most 3); a time limit; exact
  predicates and constructions.

### 7.7 Consequences for option B (inference, for the experiment session)

- **B is cheap to prototype** with what we have: our exact resolve is the arrangement; `MeshRoundingRepair` /
  SafeRounding's intersection test (exact predicates) gives the pairs; the snap is one rounding per coordinate.
- **Displacement against D′:** the default grid moves up to 2^-23 (thesis) or 2^-24 (paper) of the largest
  coordinate per iteration, about 2^28 to 2^29 ulps of M, far beyond `maxLength` of ordinary cases. To bound B by the drift, choose the grid unit from the
  drift budget (u ≤ 2 · maxLength / (√3 k_max)) or reject a snap beyond it, as section 3 proposes; the thesis' data
  says coarser grids succeed more often, finer ones fail more (fine: 6 of 527), so a drift-bounded grid will
  succeed less than the published 100 %. That is the number the experiment must measure.
- **Invariance:** a grid anchored at the largest coordinate with a power-of-two scale is invariant under 2^k scales
  bit for bit (`Similarity.All()`); it is not invariant under translation (the grid is anchored at the origin), the
  same issue as R23 / the UTM cases.
- **Topology:** the heuristic collapses vertices and keeps interior triangles. In our pipeline the results must still
  pass `MeshRobustRepair.NotCleanReasons` and `SameSolid`, which the thesis does not test.

### 7.8 Paper (CGF 2025) vs thesis

Read: `hal-04907149v1` (preprint, 22 January 2025), in full; section 7.1 has the reference. Our own words; numbers
as printed. The CGF version (DOI 10.1111/cgf.70197) was not read; the abstract on HAL gives the same headline.

**Algorithm.** The same six steps as the thesis' heuristic (section 7.3): find the properly intersecting pairs
without constructing the intersections; snap their vertices to the centres of their grid cells; also snap every
vertex lying in one of those cells (hot voxels); subdivide exactly; round the new vertices to double; repeat. The
input is a triangle soup, and the arrangement is a subdivision (CGAL's `autorefine`), not a self-union. Nothing is
added: no automatic coarsening, no adaptive vertex set, no post-processing.

**Changes against the thesis.**

| Item | Thesis (ch. 13, 17) | Paper |
|---|---|---|
| Default grid: largest absolute coordinate per axis scaled into | [2^22, 2^23) | [2^23, 2^24) |
| Scale | per axis; power of two not stated | per axis, power of two (stated) |
| Grid unit / max snap per iteration, relative to the axis' largest coordinate | ≤ 2^-22 / ≤ 2^-23 | ≤ 2^-23 / ≤ 2^-24 (≈ 6e-8) |
| Iteration limit | 20 (and a time limit) | 5 (and a time limit) |
| Coarse grid for Thingi10K 996816 | [2^15, 2^16): 40 min, ~5M vertices; [2^14, 2^15): 3 min, 272k vertices | [2^13, 2^14): 38 s, ~100k vertices, 245k faces, 1 iteration |
| Rotated-cubes stress test | soup of rotated cubes, all triangles kept; fails at step 31 | boolean intersections of cubes centred at the origin (CGAL corefinement), 16 seeds; see below |
| Speed metric | power fit in n + 2k (n vertices, k intersecting pairs) | linear fits in the vertex count of the exact arrangement |

- **Grid.** The paper's default is the thesis' default refined by one bit, justified as the finest uniform grid on
  which every snapped coordinate's integer part is a `float`, which is the correct limit (section 7.3). Whether
  the thesis' interval or its justification is the misprint cannot be told from the two texts. The 996816 sequence
  in the paper (2^13: 38 s; 2^14: 2 min, about 280k vertices; 2^15: 35 min, 4.8M vertices; 2^16: over 1 h) matches
  the thesis' runs at the same exponents (272k vertices in 3 min; about 5M in 40 min), so on that model the
  exponents mean the same grid in both texts; the paper does not report the Thingi10K runs on the thesis' default. For B, treat the grid exponent as a parameter to measure, not as fixed by the literature.
- **Iteration limit 5.** It costs nothing on Thingi10K: at most 3 iterations were ever used (26 models need two, 3
  need three: 1368052, 106838, 78227). An implementation can cap low.
- **Bound.** The paper states it as a Hausdorff bound: per axis, at most 2^-24 times the axis' largest absolute
  coordinate times the number of iterations (L∞). It is the same argument as the thesis (section 7.4), with the
  finer grid. For 996816 on the coarse grid: relative error at most 2^-14 (≈ 6e-5); coordinates are at most 100
  in absolute value, so the L∞ Hausdorff distance is at most 6e-3 in model units, after one iteration.
- **Certification.** Unchanged: exact predicates (CGAL EPECK) certify a run that ends within the iteration and time
  limits as intersection-free; exact constructions are used to control the distance
  (EPICK would certify too, with less control of the distance). A run fails if the last rounded mesh still has
  intersections or the time limit is hit. No topological statement; the paper does not discuss closedness,
  manifoldness or winding numbers either.

**Test sets and success rates (paper).** Thingi10K minus three unreadable models (49911, 81313, 77942); 4524 with
self-intersections; 527 "non-trivial" (naive rounding fails, 12 %). All implementations are the authors' own over
the same `autorefine`; one core of a Xeon Gold 5220.

- Naive rounding: fails on 527 of 4524.
- Iterative naive rounding (1 h): 273 of 527 still fail (thesis: 284 in the text, 287 in the table); 237 of those
  273 end with more intersections than they started with.
- Zhou et al. (rounding to `float`, 20 iterations or 10 h): 48 of 527 fail (9 %). Without the iteration cap only
  five of them finish (21 to 53 iterations); in 37 the intersections grow, 6 cycle after more than 100 iterations.
  Many of its failures disappear when the model is translated so that all coordinates exceed 1.
- Valque 2024 (certified, as quoted by the paper): 187 failures, about 35 %, with full certification; 33, about
  6 %, relaxed; about 100 vertices per second. The paper divides by 527, the thesis by 520 (section 7.2).
- The heuristic: all 4524 with the default grid except 996816, which needs the coarse grid.
- Ablations: without hot voxels, 105867 (15k vertices, 75 intersecting pairs, intersections growing) fails besides
  996816, and 46 models need more iterations (the thesis counts 58 + 10 and one more failure, 225354; the paper's
  run uses the finer grid). Rounding all coordinates (to `float` or to the uniform grid) before or after the
  arrangement: about 42 % failures, 24 % after iterating for 1 h. Same as the thesis.

**Timings (paper).** Ten Thingi10K models in a table, for example 1368052 (1.2M input vertices, 2.1M arrangement
vertices): 3 iterations, 21 min, against 40 min for Zhou et al. and over 1 h for iterative naive rounding; 252786
(355k, 2.2M): 1 iteration, 7 min 34 s, the others over 1 h. Average throughput in arrangement vertices per second:

| | Iterative naive | Zhou et al. | Heuristic |
|---|---|---|---|
| 3997 trivial models | 17.5k | 14.7k | 18.9k |
| 527 non-trivial models | 7.0k | 9.5k | 16.5k |

Least-squares slopes, ms per 1000 arrangement vertices: trivial 57 / 68 / 53, non-trivial 144 / 105 / 60. The
arrangement dominates the time; the heuristic's extra cost over a plain exact resolve is small, because it rarely
needs a second iteration. Output size follows the arrangement size and is sometimes much smaller (snapping merges
tiny features), rarely larger.

**Rotated cubes (paper).** Cubes added one at a time by boolean intersection; a trial stops when the scheme leaves
intersections or the boolean's result is not 2-manifold (a non-manifold result follows when rounding has created an
intersection). Mean cubes reached over 16 seeds: iterative naive 150 (SD 96), Zhou et al. 288 (SD 163), heuristic
984 (SD 368) with 9 of 16 trials still running, under 3 min per step.

**Explanation offered (paper section 5, new against the thesis' chapter 13, heuristic in the paper's own words:
"supporting evidence remains elusive").** If input coordinates are either equal or at least δ apart and bounded by
M, an edge-triangle intersection point lies at least c·δ^5/M^4 from the triangle's edges in each axis-parallel
projection. Snapping the intersecting features to a coarse grid makes coordinates collapse or separate; the new
points are then rounded to double, at least 2^29 times finer than the grid, so the subdivided triangles rarely flip
during rounding. This is not a guarantee (the grid gap is not as large as δ^5/M^4 asks). The same argument explains
why rounding to `float` fails near zero, where floats are dense, and why a uniform grid does not.

**Failure modes (paper).** Only one Thingi10K model fails with the default grid (996816: 5M intersecting pairs,
20M triplets, an exact arrangement of 30M vertices and 240M faces); a coarser grid solves it, and finer grids grow
the output fast. The paper gives no other failure, no theoretical termination argument (it notes that termination
of iterated naive rounding is open too), and no rule for choosing the grid automatically.

**What changes for option B (inference).** Use the paper's grid as the default candidate ([2^23, 2^24) per axis,
power of two) and a cap of about 5 iterations. The success data hold for the bounding-box grid only; a grid derived
from our drift budget is finer than the paper's default by many bits for ordinary `maxLength`, and both texts show
finer grids failing more often (thesis fine grid: 6 of 527). Section 7.7 stands otherwise.

## 8. The snap-rounding experiment: results (2026-11-02)

The experiment of section 6: options A and B as prototypes, measured on the leftovers of stage R and on Thingi10K. Both
prototypes are off by default; nothing changes for existing results (BooleanComparison `profile --repeat 1
--safe-rounding`, master against this branch: 263 of 263 result hashes identical). The owner decides on an
implementation from these numbers.

### 8.1 What the papers guarantee (look before you build)

- **Milenkovic and Sacks** (arXiv 1805.03252, read in full):
  - d-separation concerns only *disjoint* features: a vertex and a triangle, or two edges, that share no vertex. A
    skinny triangle is the modification stage's part, not the expansion's.
  - The expansion iterates linear programs: the first-order distance of each close pair along the direction of its
    nearest points, the rotation of that direction as bounded LP variables, a Δ box on the displacements, "maximize
    s, then minimize the displacement", constraints on the pairs closer than 2√3 d, and a conservative intersection
    test per step.
  - Its termination proof assumes an intersection-free start ("the first LP computes s > 0 because no triangles
    intersect"). No displacement bound beyond d times the number of edits; dense clusters are "future work".
  - They name mesh untangling (Freitag and Plassmann 2000) as related and say the approach "readily extends to
    control other aspects of the mesh, such as triangle normals".
- **Valque and Lazard** (CGF 2025, thesis 2024): read in full by separate sessions, section 7. This session's network
  blocked them, so the first version of B (section 8.2) was built from the abstracts: snap the vertices of what is
  still defective, resolve again, iterate. Section 7 corrects two things it assumed: the published grid is fixed
  ([2^23, 2^24) of the largest coordinate per axis, at most 5 iterations, with hot voxels), not coarsened; and the
  "certified 94 %" uses a relaxed test (64 % with full certification).

**What this means for the leftovers.** All 41 leftovers of section 6.1 (33 clusters, the cylinder step 2, the
ShortEdge Boolean, the 6 UTM cases) are degenerate triangles (a corner about 1e-18 of the diagonal from the opposite
edge: collinear in doubles), intersecting pairs after rounding, or contacts at shared positions. Plain expansion
repairs none of them: degenerate triangles have no disjoint features, and the intersecting pairs violate its
precondition. So option A was built **extended**: linear constraints keep every triangle around a defect above the
degenerate area along a reference normal (the untangling idea), next to the slab separation of vertex-disjoint near
pairs.

Upper bounds measured before building (a scratch probe, random moves of the defect vertices within the drift left):
5 of 33 clusters, 5 of 6 UTM cases, both the cylinder and the Boolean become clean. Two thirds of the clusters had used
more than half of the drift bound already (many 0.7 to 0.99).

### 8.2 What was built

- **A, separation** (owner decision 2026-11-02: measured and closed, not merged; the prototype, its LP solver and
  their tests are on the branch `claude/snap-rounding-A-archive`, files `mesh_ops/MeshRoundingRepair.Separation.cs`,
  `math/DenseSimplex.cs`, `tests/.../MeshRoundingRepairSeparationTests.cs`, `DenseSimplexTests.cs`; there
  `MeshRoundingRepair.Separate`, internal): after
  SafeRounding, per cluster of defects (degenerate triangles and intersecting pairs, joined by shared vertices), the
  vertices of the cluster (then with their one-ring) get displacement variables. Rows:
  - per triangle at those vertices, n · ((b - a) × (c - a)) ≥ 8 times the degenerate area, linearized; n is the
    triangle's normal, or for a tiny triangle the sum of the normals of the triangles at its corners;
  - per vertex-disjoint pair within reach, all corners of one on one side of a slab of width 8 ulps, the other's on
    the other side (the direction from their nearest points; for an intersecting pair, the normal of the larger
    triangle);
  - one slack s_k per row (one common s, as in the paper, let a single row that cannot be met block all others).
  Maximize Σ s_k minus a small multiple of Σ|Δ|, in a box of a fraction of the drift left (1/1024 to 1/2),
  re-linearized up to 6 times. The result is proposed as one edit, all moved vertices at once, through the exact check
  `improves()` (no new intersection or degenerate triangle, fewer defects), with the bound δ = the largest vertex move
  (every point of a triangle moves by at most the largest move of its corners). A small dense simplex
  (`math/DenseSimplex.cs`, Chvátal, Bland's rule) solves the programs in floating point: correctness comes from the
  exact check, as section 6.4 expected.
- **B, snap fallback** (`benchmarks/SnapRoundingExperiment/SnapFallback.cs`, not in the library): if SafeRounding's
  result is not clean, resolve it once more (`ExactMeshBoolean`, union with nothing, the drift left as its bound); if
  that is not clean either, snap the vertices of the defect triangles to grids of 2^j ulps of the largest coordinate,
  from fine to coarse, and resolve again, up to three rounds per grid. The drift adds up (first repair, the snap, the
  second repair) and must stay within the first bound.
  - Not proven: the second resolve keeps the boundary of the snapped solid, so its surface lies on the snapped
    surface (one side of the Hausdorff distance), but parts of the snapped surface inside the solid may be removed.
    The measured Hausdorff distance stands in for the other side.
- **The certificate** (`tests/geometry3Sharp.Tests/Harness/SeparationCertificate.cs`, tested and mutation-checked in
  `SeparationCertificateTests`): every pair of disjoint features (vertices at one position count as one) farther apart
  than d = 2e = √3 ulp(M), decided exactly with dyadic rationals (point-segment, point-triangle, segment-segment).
  A certified mesh survives one more rounding of its vertices without an intersection.
- **The harness** (`benchmarks/SnapRoundingExperiment`, README there): each case and method in its own process with a
  time limit, resumable; `summarize.py` gives the tables below.

### 8.3 The generated sets

(The columns A and AB were measured with the tool as it is on the branch `claude/snap-rounding-A-archive`; on master the
tool has no A.)
`run.py <set> --methods plain,safe -j 4 --timeout 300`, then each of `again`, `A`, `B`, `AB` alone with `--left -j 4
--timeout 120` (the methods change nothing where SafeRounding's result is clean); `summarize.py`. 4 cores, four cases at a time.
"Clean": `RoundingClean` and `MeshRobustRepair.NotCleanReasons` empty. "Certified": clean and 2e-separated. "Failed":
the time limit (120 s per method). Drift in units of the bound (`MaxLength`).

The 41 leftovers of SafeRounding:

| set (left of all) | again | A | B | AB |
|---|---|---|---|---|
| clusters (33 of 198), clean / certified / failed | 7 / 7 / 0 | 6 / 5 / 18 | **25 / 19 / 7** | 14 / 12 / 18 |
| cylinder step 2 (1 of 6) | 0 | 1 / 1 | 1 / 0 | 1 / 1 |
| ShortEdge 2^-44 Boolean (1 of 198) | 0 | 1 / 1 | 1 / 1 | 1 / 1 |
| UTM (6 of 114) | 3 / 0 | 1 / 0 | **6** / 0 | 6 / 0 |
| **all 41: clean / certified** | 10 / 7 | 9 / 7 | **33 / 20** | 22 / 14 |

All sets, clean (SafeRounding → B): clusters 165 → 190 of 198, cylinder 5 → 6, Booleans 197 → 198, UTM 108 → 114.

- **Drift.** Every result stays within its bound (largest 0.99 of it, the clusters that had used it up already).
  A's moves are larger than B's on the cases both repair (cylinder: A 4.7e-4 of the bound, B 2.8e-5; Boolean: A 6.1e-4,
  B 7.1e-4), all far below it.
- **Hausdorff to the exact result** (rounded, an upper bound with a tolerance of 1e-2 of the bound): at most 0.21 of the
  bound for SafeRounding, 0.16 for B, 0.39 for A, on the clusters; these include the removed tiny components (the
  theorem excludes them).
- **Certificate.** UTM results are never 2e-separated: their features lie below the ulp of the coordinates (section 5),
  clean or not. Elsewhere B's results are certified in 19 of 25 cluster repairs, A's in 5 of 6; the cylinder repaired by
  B keeps 5 pairs closer than 2e, by A none.
- **Time.** `again` and B cost one or a few resolves (median 1.6 s and 1.3 s on the 33 clusters, against 0.7 s for
  SafeRounding); on 7 clusters B ran into the limit (the arrangement of snapped coincident geometry, `prepare_touched`,
  takes minutes). A ran into the limit on 18 clusters (median 4.6 s where it finished).
- **What A does not repair**, from its diagnostics (`SEP_DEBUG=1`):
  - the dense count-8 clusters give programs of 500 and more rows (slabs to the many near triangles of the folded
    layers). The dense tableau stalls on them (it reaches its pivot limit, also with Bland's rule), so no proposal comes
    out, or the run times out;
  - where a program is solved, the exact check often rejects the proposal (on `Cluster k0 count 8 seed 1`, 18 of 19);
  - two of the six UTM leftovers are contacts at shared positions, which A does not move by design.
  - A first version also gave up whenever a sliver had only slivers around it (no reference normal); falling back to
    the normals of all triangles that move fixed that, and the numbers above are with it.

### 8.4 Thingi10K self-unions

The 400 set of `benchmarks/MeshRepairBenchmark/fetch_data.py` (all of it, open inputs included), each resolved as the
union with nothing: `run.py thingi.jsonl @list --methods plain,safe -j 4 --timeout 300`, then each of `again`, `A`, `B`,
`AB` alone with `--rounding-left -j 4 --timeout 120` (only where SafeRounding's result is not rounding clean: elsewhere
the defects come from the input, eg open meshes, and A does nothing by design).

- **All 400:** plain rounding clean 150, SafeRounding clean 188 and rounding clean 361 (one resolve took more than 300 s).
  Of the 212 not clean, 173 are rounding clean: open inputs ("not closed": 32 alone, 82 with self-intersections, 9 with
  bowties, 1 with an inside-out component), contacts where the exact result touches itself (35 "self-intersections"),
  14 empty unions; 1 timed out.
- **The 38 rounding failures** (32 from open inputs, 6 with closed results, of which 5 keep degenerate triangles and
  intersecting pairs, 1 only degenerate triangles):

  | method | rounding clean | clean | failed (120 s) | of the 6 closed: clean |
  |---|---|---|---|---|
  | SafeRounding | 0 | 0 | 0 | 0 |
  | again | 0 | 0 | 2 | 0 |
  | A | 5 | 0 | 9 | 0 |
  | B | 3 | 3 | 12 | 1 (72609) |
  | AB | 3 | 3 | 15 | 1 |

- **Time:** B resolves the whole mesh once per grid and round; on these meshes (median 2 s for SafeRounding) a B run
  takes a median of 30 s and 12 of 38 exceed 120 s. A's programs stall (section 8.3) or time out on 9.
- The Hausdorff distances to the exact result exceed the bound by far on these models (up to 692 times it for
  SafeRounding): they include the removed tiny components (245 on the 38 models) and the parts of open inputs the
  union discards, so they say nothing about the drift there.

### 8.5 Findings beyond the experiment

- **SafeRounding is not 2^k-invariant on some clusters (master, pre-existing).** `DegenerateFamilies.Make(Cluster, 0,
  5, 4)`, resolved: scale 1 leaves 2 degenerate triangles (2 moves), 2^10 is clean (5 moves), 2^-10 leaves them (1
  move), with different drifts. Cause: `fix_degenerate` moves the apex of a cap along `off.Normalized`, and
  `Vector3d.Normalized` returns zero below the absolute `MathUtil.Epsilon`; the offset of a cap is about 1e-17 at scale
  1 (`fix_intersection`'s `toCtr.Normalized` is the same kind). The REVIEW_CHECKLIST §5 class. A fix changes results
  where the threshold bites, so it is its own bugfix PR, #316 (merged). The invariance test of A (on the archive
  branch) therefore runs A without SafeRounding's edits.

- **Slow resolves of snapped meshes (B's time limit).** Repro, generated:
  `SNAP_TRACE=1 SNAP_CERT=0 dotnet bin/Release/net10.0/SnapRoundingExperiment.dll run cluster:Cluster:0:8:5 B` prints
  every grid round of B with its time.
  - The resolve of the snapped mesh at 512 ulps, round 3 (2970 triangles, 1145 vertices snapped) takes 87 s; other rounds
    of the same case take 6 to 22 s, against a fraction of a second for the unsnapped mesh.
  - Stack samples (`dotnet-stack`) during that resolve: `MeshRoundingRepair` (`intersecting_pairs`, `fix_intersection`,
    `try_strip` / `zip_strip`), `ExactTriangleIntersection` and `ExactMeshBoolean.front_winding`. In the first probe, with
    coarser grids (a fixed fraction of the bound), the same case spent minutes in `ExactMeshArrangement.prepare_touched`.
  - So the cost comes from the many coincident and intersecting features a coarse snap creates, in the arrangement and in
    the repair that follows. Whether either has a worst-case defect beyond that (a cost superlinear in the coincidences)
    is not established; the repro above is where to start.

### 8.6 Against the go / no-go criteria of section 3, and a recommendation

**Go criterion for A** (section 3): at least 94 % certified at a displacement well below 2^-24 of the box, and A + B
at 100 % clean within `maxLength`.
- **A misses it.** On the 38 Thingi10K rounding failures it makes none clean (5 rounding clean, all open inputs), on
  the 41 generated leftovers 9 (section 8.3). Its displacement is small (at most 6.1e-4 of the bound on the repaired
  cases, about 6e-9 of the diagonal, far below 2^-24) and its results are mostly certified where they are clean
  (7 of 9; the UTM one cannot be). What fails is reach and cost: dense strips that branch or fold, contacts at shared positions it may not
  move, and programs of hundreds of rows that the dense floating-point simplex does not finish (the open LP question of
  section 6.4 is real: a bounded-variable or sparse solver would be needed).
- **A + B misses 100 %** as well: 22 of 41 generated leftovers (A's timeouts block B behind it), 3 of 38 Thingi10K.
- **So, by the stop rule of section 3: no-go for A.** The expansion of Milenkovic and Sacks addresses close disjoint
  features before rounding; our leftovers are degenerate triangles and intersections after rounding, which it does not
  model, and the extension to them (area rows) is not enough for the dense clusters.

**B, bounded by the drift**, is the stronger of the two: 33 of 41 generated leftovers clean (all 6 UTM cases, the
cylinder step, the Boolean, 25 of 33 clusters), always within the bound. Its limits:
- it resolves the whole mesh again per grid and round: a median of 30 s on the Thingi10K failures, 12 of 38 over
  120 s; 7 clusters over 120 s (the arrangement of snapped coincident geometry);
- no certificate in general (20 of its 33 repairs certified, the 6 UTM ones cannot be, the cylinder is not), and the Hausdorff side "parts of
  the snapped surface removed by the resolve" is measured, not proven;
- a third of B's gain on the generated sets is the second resolve alone (`again`: 10 of 41).

**Recommendation (the owner decides):**
1. Do not implement A.
2. If the remaining defects matter, implement B behind an option (off by default), but **locally**: snap and resolve
   only the defect clusters with a margin (an open patch with fixed boundary), so that its cost is the size of the
   defect, not of the mesh. The drift bookkeeping stays as measured here; the missing Hausdorff side needs a proof or
   a check of the removed parts against the bound.
3. Independently of A and B: fix the 2^k invariance of SafeRounding (section 8.5), a bugfix PR.
4. The certificate (`SeparationCertificate`) and the harness stay useful for any of these.

### 8.7 The owner's criteria (fixed in advance), verdict per criterion

Measured on master `99745a7` plus this experiment, before the scale-invariance bugfix (#316). With #316 SafeRounding itself
already repairs the cylinder step 2, `cgal/cheese` (both operations) and 4 more dense clusters; it changes none of the
verdicts below. Commands as in sections 8.3 and 8.4; `Bpaper` is B with the paper's grid (section 7: [2^23, 2^24) of
the largest coordinate per axis, hot voxels, at most 5 iterations, no drift bound, `SnapFallback.PaperGrid`); B is the
drift-bounded variant (the product variant). BooleanComparison: the 87 cases of `perf/cases-cgal.txt`,
`cases-thingi.txt` and `cases-cherchi_small.txt`, three operations each (`run.py @bc:list`).

**Criterion 1, the measurement** (plain / safe / again / A / B / Bpaper / AB on the clusters, the Thingi10K self-unions
and BooleanComparison): done.

| set | safe clean | again | A | B | Bpaper (within the bound) | AB |
|---|---|---|---|---|---|---|
| generated leftovers (41) | 0 | 10 | 9 | **33** | 16 (10) | 22 |
| BooleanComparison (261) | 255 | 255 | 257 | 257 | 256 | 257 |
| Thingi10K self-unions (399 finished) | 188 | 188 | 188 | 209 | 194 (194) | 191 |

(Bpaper on Thingi10K, like B on all 211 unclean results (`--left`): 6 new clean results, all within the drift bound
there, 2 also within the Hausdorff bound; 2 timeouts; time ratio median 1.7 and p95 15 over all cases, 6.3 and 18 on the
affected. These numbers and those of criterion 2 below come from `summarize.py`'s "Criterion 2" table. On the generated leftovers its grid is far coarser than the bound at UTM coordinates, 0.3 of a unit there.
BooleanComparison: the two cases A and B repair are `cgal/cheese` intersection and difference, which #316 repairs
already; the other 4 unclean results are empty. Thingi10K: again, A and AB ran on the 38 rounding failures only; B on all
211 unclean results.)

**Criterion 2, B default-on as a bounded fallback:** B must raise the clean share on the Thingi10K self-unions
measurably, never exceed the drift bound, keep the Hausdorff distance to the exact result within the bound, and keep
the time acceptable. **Not met.**
- Clean share: 188 → 209 of 399 (+21) when B runs on every unclean result; +3 when it runs only on the 38 rounding
  failures (the others are open inputs or contacts).
- Drift bound: never exceeded (0 of 211).
- Hausdorff: **fails.** 9 of the 21 new clean results lie far from the exact result (1.0e0 to 9.3e4 times the bound;
  SafeRounding's results there: about 0.01 of it, the tolerance). These are open inputs: the second resolve takes the
  union again and drops what the winding number of the snapped mesh excludes, the side of the Hausdorff distance that
  the drift does not bound (section 8.2). Restricted to the rounding failures, 1 of the 3 new clean results stays within
  the bound.
- Time (B's time over SafeRounding's per case; a timeout counts as 120 s, so these are lower bounds): all 399 cases,
  median 4.7 and p95 66; the 211 affected, median 36 and p95 76. Only on the rounding failures: all cases median 1.0 and
  p95 26.5, the 38 affected median 27 and p95 67.
- **So B stays off by default**: it does not qualify for a default. As a whole-mesh fallback it is too slow and, on open
  inputs, unbounded in the Hausdorff distance. A patch-wise B would fix the time (next point); the Hausdorff side
  needs a check of what the resolve removes.
- An optimistic estimate of a patch-wise B (tool mode `patch`, the 12 Thingi10K runs that hit the limit): the defect
  triangles grown by three rings, 286 to 15088 triangles (median 7 % of the mesh), resolve in a median of 0.17 s (max
  16 s, 1368057) against 3.9 s for the whole SafeRounding, so the resolve of one grid round costs about 1/20 of a full
  one. It is a lower bound of the cost, not an upper one (independent review): it leaves out the work every round still
  does on the whole mesh (finding the defects, cutting out the patch, putting it back), counts one round where B takes
  up to three per grid, and `patch_ok` is only the resolve's status, not a clean patch. It covers only results that are
  not rounding clean; the contacts behind most of B's +21 and most of its time are not measured. Not measured either:
  whether the patch result fits back (fixed boundary).

**Criterion 3, A only for a remainder that only A fixes:** **not met: 0 cases.** In every set the results A makes clean are
also made clean by B. The one thing only A gives is the 2e-certificate of the cylinder step 2, which #316 makes clean (and
the certificate is no criterion).

**Criterion 4:** neither qualifies for a default. Options A and B are **measured and closed** as defaults. The
prototypes stay out of the library: A on the branch `claude/snap-rounding-A-archive`, B in the tool
(`benchmarks/SnapRoundingExperiment`). A patch-wise B behind an
off-by-default option remains possible later, if the owner wants it for open inputs; its preconditions are the
patch-fitting and the Hausdorff check above.

The findings that come out of the experiment regardless: the scale-invariance bugfix (#316), the exact d-separation
certificate and the harness, and the hang of the arrangement on snapped coincident geometry (section 8.5).
