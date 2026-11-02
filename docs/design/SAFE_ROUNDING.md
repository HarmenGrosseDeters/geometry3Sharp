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
- What is guaranteed: triangles the repair does not touch are the exact result, rounded. Every edit, also of
  well-shaped neighbours (flips, contractions, moves), stays within a **proven bound** of the rounded result.
  `MeshRobustRepair` can run a second repair with a fresh budget: there the total is at most 2 `maxLength`.

**Drift.** Every vertex has a drift d(v), 0 at the start of `Apply`. A proposed edit removes triangles R and adds
triangles A. Let δ bound how far the edit moves the surface. Applying the edit sets d(v) = max over the vertices u of R
and A of d(u), plus δ, for every vertex v of R and A. An edit is not applied if any of these drifts would exceed
`maxLength` = `MaxDisplacement` (1e-5) times the diagonal. `MaxDrift` reports the largest drift reached.

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
  2025 [S, the paper's host was blocked]; Valque's thesis, 2024 [V]. Full-text notes: section 7.
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

Read for option B (sections 3 and 6). Notes in our own words; no code was read, CGAL's (GPL) neither.

### 7.1 What was read

- **Read in full text [V]:** L. Valque, *3D Snap rounding*, PhD thesis, Université de Lorraine, 2024, NNT 2024LORR0337,
  HAL `tel-05016163`, version 1, <https://hal.science/tel-05016163> (PDF: `/document`, 183 pages, PDF modified
  2025-03-04). Supervisor S. Lazard (GAMBLE, Inria / LORIA). Read: chapters 4.3, 5.2, 12, 13, 14.2, 14.5, 16.1–16.3,
  16.8, 17 (all sections).
- **Not read, host blocked:** S. Lazard, L. Valque, *Resolving self-intersections in 3D meshes while preserving
  floating-point coordinates*, Computer Graphics Forum 44(5), 2025, DOI 10.1111/cgf.70197.
  - Eurographics digital library: <https://diglib.eg.org/items/df2f3916-37f1-47a1-aebb-6e798372c42f/full>. The
    session proxy refuses `diglib.eg.org` and `diglib7.eg.org` (connect_rejected).
  - HAL preprint: `hal-04907149`, version 1, title *Removing self-intersections in 3D meshes while preserving
    floating-point coordinates*, 2025, <https://hal.science/hal-04907149>. The record page loads on `hal.science`
    (abstract read [V]); the PDF is served from `inria.hal.science`, which the proxy refuses.
  - HAL's search pages answer with a bot check, and `api.archives-ouvertes.fr` is refused: no other companion report
    could be searched for. Not circumvented.
- **Also not reachable:** the CGAL 6.1 blog post (`www.cgal.org` does not resolve in this session).

**Important: the paper is not the thesis' heuristic, or not only.** The paper's abstract [V] compares itself with
"Valque [2024]" (the thesis' certified algorithm, 94 % of the 527 hard models) and Zhou et al. (91 %), handles all
4524 self-intersecting Thingi10K models, at about 17k vertices per second (serial), 1.7 times faster than Zhou et
al. on the hard models. The thesis' heuristic (section 7.3) gives the same headline. Whether the paper adds anything
(an automatic coarsening, other vertex sets, a bound) could not be checked; a session that can reach
`inria.hal.science` or `diglib.eg.org` should read it before B is implemented.

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
   two is not stated; it is the natural choice (scaling then is exact in double). 2^22..2^23 is the largest scale at
   which the integer part is representable as a `float`, chosen to compare with Zhou et al.'s rounding to `float`.
   Grid unit: 2^-23 to 2^-22 of the axis' largest absolute coordinate, "about 100 nm for an object one metre wide".
2. **Vertex set V:** all vertices of all pairs of triangles that properly intersect in the current (double) mesh.
3. **Hot voxels:** add to V every vertex of the mesh that lies in a voxel containing a vertex of V.
4. **Snap:** round every coordinate of every vertex in V to the nearest integer (in the scaled coordinates). Other
   vertices are not moved.
5. **Arrangement again:** subdivide the pairs of triangles that properly intersect after the snap, exactly.
6. **Round the new vertices** of step 5 to double (not to the grid).
7. **Repeat** steps 2–6 while proper intersections remain, at most a fixed number of times (20 in the experiments,
   with a time limit).
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
  vertices cross many faces.

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
- **Displacement against D′:** the default grid moves up to 2^-23 of the largest coordinate per iteration, which is
  2^29 ulps of M, far beyond `maxLength` of ordinary cases. To bound B by the drift, choose the grid unit from the
  drift budget (u ≤ 2 · maxLength / (√3 k_max)) or reject a snap beyond it, as section 3 proposes; the thesis' data
  says coarser grids succeed more often, finer ones fail more (fine: 6 of 527), so a drift-bounded grid will
  succeed less than the published 100 %. That is the number the experiment must measure.
- **Invariance:** a grid anchored at the largest coordinate with a power-of-two scale is invariant under 2^k scales
  bit for bit (`Similarity.All()`); it is not invariant under translation (the grid is anchored at the origin), the
  same issue as R23 / the UTM cases.
- **Topology:** the heuristic collapses vertices and keeps interior triangles. In our pipeline the results must still
  pass `MeshRobustRepair.NotCleanReasons` and `SameSolid`, which the thesis does not test.
