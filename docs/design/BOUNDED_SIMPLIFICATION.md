# Simplification with a two-sided Hausdorff bound (study)

**Status:** study, docs only (2026-11-13). No library code. The numbers come from a throwaway probe (a `Reducer`
subclass in a scratch project, not committed; its algorithm is given in section 5.1 so it can be rebuilt). Decisions for
the owner are in section 10.

## 1. Question

A simplification (decimation) whose output `S` is guaranteed to be within a given ε of the input `M` in the
**two-sided** Hausdorff distance, `H(S, M) = max(h(S, M), h(M, S)) <= ε` with `h(A, B) = sup_{a in A} d(a, B)`, and
that stays closed, manifold and free of self-intersections when `M` is. What can be proven with the library's building
blocks, what does it cost per collapse, and what is only measured?

## 2. What the library has (searched before the literature, AGENT_WORKFLOW §3)

| need | building block | status |
|---|---|---|
| quadric edge collapses | `Reducer` (`mesh/Reducer.cs`; Garland-Heckbert QEM, regularized, scale-safe frame, fold-over guard) | done, Reducer / Remesher 2.0 |
| **h(S, M) <= ε** (output near input) | `Reducer.MaxDeviation`: every new triangle of a collapse is certified with `TriangleDeviationBounds.IsWithin` (Sacht-Jacobson cascading upper bounds, subdivision to `MaxDeviationDepth`) against the input's tree | done, **one-sided**; the doc comment says so ("input features thinner than the tolerance (eg a spike) can still be cut off") |
| **h(M, S) <= ε** (input near output) | nothing per collapse | **missing**: the subject of this study |
| a posteriori two-sided check | `MeshHausdorffDistance` (certified interval `[LowerBound, UpperBound]`, symmetric or one-sided, `PatchBound` option); `MeshDeviation` | done; scale defects #731 (overflow above ~1e120, a wrong 0 at 1e-200), fix in PR #735 |
| closed / manifold | `DMesh3.CollapseEdge` (link condition, tetrahedron test), bowtie handling and `PreventFoldOver` in the `Reducer` | done; topology kept on every Reducer / Remesher 2.0 benchmark mesh |
| free of self-intersections | `Reducer.PreventSelfIntersections`: new triangles of a collapse against a grid of all current triangles, `ExactTriangleIntersection.IntersectsBeyondShared` (exact) | done; 2-3x the reduction time |
| exact containment | `PolyhedralEnvelope` (Wang et al. 2020; internal; points, segments, triangles exact), `MeshEnvelope` (prisms, `BigInteger`) | kept as internal tools; the envelope for the `Reducer` was **stopped** (ENVELOPE.md §6: accepts 8-17 % of what `IsWithin` certifies, the shape ε/√3 is the blocker, and 4-6x slower) |
| a carried / reference bound | the D1 reference rule of SafeRounding (`SAFE_ROUNDING_D1_DRIFT.md`, `MeshRoundingRepair`): per triangle the corners at its last certification, the displacement measured from there; Z3-checked lemma | done for rounding repair; the same "measure against the original, do not sum steps" idea as Klein et al. 1996 |
| slab parallelism | `PartitionedReducer` / `PartitionedReduction` (frozen borders, confinement); refuses `MaxDeviation` in `Configure` | done |

Not related despite the brief: issue #748 is about the meshing2d oracle's constraint envelope (`GridUnsplittable`), not
the Hausdorff distance; nothing of it carries over.

So the output side (h(S, M), closed, manifold, no self-intersections) is already proven per collapse; the one missing
piece is h(M, S). Everything below is about that piece and how it composes with the rest.

## 3. Prior art

Citations marked [m] are recalled from memory (not re-read for this study); [s] are known from the library's earlier
notes (SOURCES.md, ENVELOPE.md, SAFE_ROUNDING_D1_DRIFT.md). No code was read for this study.

- M. Garland, P. S. Heckbert, *Surface simplification using quadric error metrics*, SIGGRAPH 1997 [s]. The QEM
  priority; it bounds nothing (the quadric is a sum of squared plane distances, not a distance to the surface).
- J. Cohen, A. Varshney, D. Manocha, G. Turk, H. Weber, P. Agarwal, F. Brooks, W. Wright, *Simplification envelopes*,
  SIGGRAPH 1996 [m]. Inner and outer offset surfaces that do not self-intersect; the simplified surface must stay
  between them, so every point is within ε of the input. Requires an orientable manifold; building the envelopes is the
  hard part (it shrinks the offset locally where the offsets would intersect, which weakens the bound near thin parts).
  The reverse direction follows only from the topological argument that the output separates the envelopes, which
  needs care; the paper's own claim is the output-near-input direction [m].
- R. Klein, G. Liebich, W. Straßer, *Mesh reduction with error control*, IEEE Visualization 1996 [s, from abstract].
  Two-sided Hausdorff control by keeping a correspondence between the original and the reduced mesh and re-checking
  only what an operation changes. **This is the closest prior art to the recommended option.**
- A. Guéziec, *Locally toleranced surface simplification*, IEEE TVCG 5(2), 1999 (and *Surface simplification with
  variable tolerance*, MRCAS 1995) [m]. Error volumes: a tolerance sphere per vertex, interpolated over the
  triangles, grown on every collapse so that the old surface stays inside the union; a conservative two-sided bound with
  growing volumes.
- J. Cohen, D. Manocha, M. Olano, *Simplifying polygonal models using successive mappings*, IEEE Visualization 1997
  [s]. A piecewise linear map from every simplified triangle to the original; bounds the distance along the mapping
  (a bijection, so it bounds both directions, but it is a mapping distance, an upper bound of the Hausdorff distance).
- A. Ciampalini, P. Cignoni, C. Montani, R. Scopigno, *Multiresolution decimation based on global error*, The Visual
  Computer 13, 1997 [m]. Removed vertices are kept attached to the faces that cover them; the global error is
  evaluated against them (a sampled two-sided error, not a certified one).
- L. Kobbelt, S. Campagna, H.-P. Seidel, *A general framework for mesh decimation*, Graphics Interface 1998 [s]; M.
  Botsch, L. Kobbelt, M. Pauly, P. Alliez, B. Lévy, *Polygon Mesh Processing*, AK Peters 2010, chapter 7 [m]: the
  standard survey of error control (one-sided, two-sided, sampled vs bounded); summed deviations are "a very coarse but
  conservative estimate".
- H. Borouchaki, P. J. Frey, *Simplification of surface mesh using Hausdorff envelope*, CMAME 194, 2005 [s, title
  only].
- S. Gumhold, P. Borodin, R. Klein, *Intersection free simplification*, International Journal of Shape Modeling 9(2),
  2003 [m]. Self-intersection-free decimation by testing the changed triangles against a spatial index of the others:
  what `PreventSelfIntersections` does, with exact tests here.
- B. Wang, T. Schneider, Y. Hu, M. Attene, D. Panozzo, *Exact and efficient polyhedral envelope containment check*,
  ACM TOG 39(4), 2020 [s]. Exact one-sided containment (built here as `PolyhedralEnvelope`). Its users (fTetWild,
  Y. Hu et al., *Fast tetrahedral meshing in the wild*, TOG 2020 [m]) keep only the one-sided bound and accept that thin
  input features may vanish.
- L. Sacht, A. Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, SGP 2024 [s]. The
  bounds of `TriangleDeviationBounds` and `MeshHausdorffDistance`.

**How others solve it (summary).** Production decimators (QEM-based tools, fTetWild's envelope) guarantee at most the
one-sided bound. The two-sided bound in the literature comes from a correspondence that is maintained through the
operations (Klein 1996, Ciampalini 1997, Cohen 1997) or from growing tolerance volumes (Guéziec 1999). Certified
versions of the correspondence check are not common; the library already has the certified bound per triangle
(Sacht-Jacobson), which is the missing piece of Klein's approach.

## 4. Options

### O1 (recommended, tier M): reverse coverage by dependency sets, local and sound

Keep for every input triangle `T` a small set `D(T)` of current output triangle ids with the invariant

> **(I)** every point of `T` is within ε of the union of the triangles `D(T)`, with their current geometry.

- **Start:** `D(T) = {T}` (the output is the input, distance 0).
- **A collapse** of edge (a, b) changes exactly the triangles of the one-rings of a and b, `R` (two are removed, the
  others get the moved vertex; `DMesh3.CollapseEdge` keeps the ids of the surviving ones). Every other output triangle
  keeps its geometry. The affected inputs are `A = { T : D(T) ∩ R ≠ ∅ }`, found through the inverse map
  (output triangle -> input triangles that depend on it).
- **Check:** for each `T` in `A`, the candidate set is `C(T) = N ∪ (D(T) \ R)`, where `N` is the new ring (`R` minus the
  removed two, with the new vertex position). Certify that `T` is within ε of the union of `C(T)` (upper bound, as
  below). If one fails, reject the collapse. If all pass, set `D(T)` to the members that the certificate used.
- **Why it is sound:** (I) holds for `T` not in `A`, since none of its triangles changed. For `T` in `A`, the members of
  `C(T)` are current output triangles after the collapse, and the distance to a subset of the output is at least the
  distance to the output. So by induction over the collapses, `h(M, S) <= max_T h(T, ∪ D(T)) <= ε`. The check is local
  (a dozen input triangles against about 10 output triangles) and the bound is global.
- **The per-triangle certificate** is the same family as `IsWithin`: for a single target triangle the distance is
  convex, so the maximum over `T` is at a corner; else the covering bound (largest corner distance plus the longest
  edge); else subdivide `T` into 4 up to a depth limit; reject if a corner is farther than ε from every candidate.
  In the library it would be a variant of `TriangleDeviationBounds.UpperBound` over a candidate list instead of a tree.
- **Composition:** together with `MaxDeviation` (h(S, M) <= ε) it gives `H(S, M) <= ε`. Closed / manifold come from the
  collapse's link condition, freedom from self-intersections from `PreventSelfIntersections` (each proven per collapse
  for the whole mesh by the same locality argument: only the new triangles change).
- **What it must exclude or include:** every operation that moves geometry must go through the check: `Reproject` /
  `ProjectionTarget` (moves vertices after the collapses) must be off or checked in the same way; `FastCollapsePass`
  and the `PartitionedReduction` slabs need the hook too (a slab's rings never leave the slab, so the sets stay inside
  it, but the frozen-border argument must be written down).

### O2 (tier S): a posteriori certification only

Reduce as today (`MaxDeviation`), then `MeshHausdorffDistance` (symmetric, `PatchBound`). This **measures** the bound;
it does not construct it. On failure the only remedies are a rerun with a smaller ε (no guarantee of progress) or a
local repair. Useful as the oracle of O1's tests and as the cheap report, not as the guarantee.

### O3 (tier L): the exact tier of O1

O1's certificate decides `d(p, t) <= δ` in floating point, like `IsWithin` (no explicit rounding term; the
deviation-bounds spike found no accepted triangle within 1e-8 ε of the tolerance, ENVELOPE.md §6, while the rounding is
about 1e-13 ε). An exact version needs (a) an exact predicate "point within δ of a triangle" on doubles (squared
distances by Voronoi region: a degree-4 / degree-6 polynomial sign, a candidate for `tools/PredicateCompiler`), and (b)
covering terms without square roots (compare corners against `ε - r` with `r` rounded up and the difference rounded
down, so only the comparison is exact). Same for `IsWithin` on the forward side. Worth it only if the owner wants
"exact" rather than "certified up to rounding" (decision 3).

### O4 (not recommended, tier L): envelopes

- The polyhedral envelope as the forward check: exact, but stopped for the `Reducer` (8-17 % acceptance, 4-6x the
  time; ENVELOPE.md §6), and it gives only the forward direction.
- Simplification envelopes (Cohen 1996): building non-self-intersecting offsets is a project of its own (it is the
  alpha wrap's problem), weakens the bound at thin parts, and gives the reverse direction only by a topological
  argument for closed orientable inputs.

### O5 (tier M, an alternative to O1): Guéziec-style tolerance volumes

Grow a per-vertex tolerance on every collapse so that the old surface stays inside; reject when it exceeds ε. Simpler
data (one number per vertex), but conservative by construction: the volumes only grow, as the additive drift of
SafeRounding before D1 did (SAFE_ROUNDING_D1_DRIFT.md §1: 0.96 of the budget used at a true drift below 0.001). O1 is
the "reference" variant of the same idea and measures against the original every time.

## 5. Measurements (probe)

### 5.1 The probe

A scratch project (net10.0, not committed) with a subclass of `Reducer` that overrides `CollapseEdge`: it runs O1's
check (section 4, brute force over `C(T)` with `DistPoint3Triangle3`, depth 6, new lists per check, no caching) **before**
`base.CollapseEdge`, and updates the sets when the base reports `Ok_Collapsed`. The base keeps `MaxDeviation = ε`, so B
is the full two-sided reduction. Because the check runs before the base's own checks (flip, fold, deviation), it also
pays for collapses the base rejects afterwards: the time is an **upper bound** of O1's cost. The probe checked that the
base never moved the vertex to another position than the one checked ("pos mismatch" 0 on every run).

Each run reduces to 4 triangles (as far as the checks allow), at ε = 0.001 and 0.003 of the bounding-box diagonal:
- plain: `Reducer` without checks to A's triangle count (today's QEM speed);
- A: `Reducer { MaxDeviation = ε }` (today's one-sided bound);
- B: A plus O1 (two-sided).

`h(S, M)` and `h(M, S)` are `MeshHausdorffDistance` upper bounds (one-sided each, `Tolerance = 0.01 ε`, so values up to
about 1.01 ε are consistent with a true distance <= ε); for A's `h(M, S)` the lower bound is given as well, so a lower
bound above ε is a proven violation. Machine: the cloud container, 4 cores, one run each (timings are indicative).

### 5.2 Procedural meshes

Bumpy sphere (`Sphere3Generator_NormalizedCube`, EdgeVertices 40, radial bumps), spiky sphere (EdgeVertices 60, three
narrow spikes of height 0.3), terrain 80 x 80 (open, hills and ripples), as in `benchmarks/ReducerBenchmark`.

RESULTS_PROCEDURAL

### 5.3 Thingi10K (fetched at run time, not committed)

Six models drawn with seed 731 from the Thingi10K metadata (revision `2d5d3b2f`, as in `benchmarks/UVAtlasBenchmark`)
among the closed, edge- and vertex-manifold, single-component meshes with 5k-20k faces and no self-intersections,
duplicated or degenerate faces: IDs 1036464, 51011, 894833, 472201, 1436278, 60516.

RESULTS_THINGI

### 5.4 Reading

READING

## 6. Guarantees: proven vs measured

| property | how | proven / measured |
|---|---|---|
| h(S, M) <= ε | `MaxDeviation` (`IsWithin` per new triangle) | proven per collapse, global by locality; floating-point certificate (no explicit rounding term) |
| h(M, S) <= ε | O1 (dependency sets) | proven by the invariant (I) and induction (section 4); floating-point certificate like `IsWithin` |
| H(S, M) <= ε, exact | O1 + O3 | provable; needs the exact distance predicates (tier L) |
| closed, manifold, same topology | link condition of `DMesh3.CollapseEdge`, bowtie rules | proven per collapse (existing) |
| no self-intersections | `PreventSelfIntersections` (exact triangle tests against all current triangles) | proven per collapse (existing); its candidate filter skips degenerate new triangles, which the review should confirm cannot intersect anything (a check item, not a known defect) |
| the result is within ε of the input after `Reproject` | not covered | excluded (must be off) |
| triangle count reached, quality (angles) | QEM order, guards | measured only |
| how much the reverse check costs and rejects | probe, section 5 | measured only |

The one-sided output violates h(M, S) <= ε on inputs with features thinner than ε (spikes, the rim of an open mesh):
section 5 shows it on the spiky sphere and the terrain.

## 7. Test plan (repo style)

- **Guarantee tests:** for every accepted reduction, `MeshHausdorffDistance` (both directions, tolerance 0.01 ε) gives
  an upper bound <= 1.01 ε, and its lower bound <= ε; closed and manifold kept (`MeshRobustRepair.NotCleanReason`
  clean when the input is), no self-intersection with `PreventSelfIntersections`. Cases: spiky sphere, terrain (open),
  a thin plate (two sheets closer than ε), a pinched torus, a box with a slot narrower than ε.
- **Tests that fail on the old code:** on the spiky sphere and the terrain the one-sided reduction has `h(M, S)` lower
  bound > ε (section 5); the new option must bring it to <= ε. The `New tests on base` job then reports them failing on
  base.
- **Invariance:** over `Similarity.All()` (`Harness/Invariance.cs`): the 2^k scales give the same reduction bit for bit
  (distances scale exactly by 2^k as long as nothing under- or overflows; the scale-safe frame of the `Reducer` covers
  the quadrics; the check needs the same power-of-two frame as #735 for extreme extents), and the rotations /
  translations keep the guarantee.
- **Reuse:** two reductions on one `Reducer` instance (`ReduceToTriangleCount` twice, and to a count after a
  `ReduceToEdgeLength`): the sets carry over and the bound still holds against the original input (as
  `MaxDeviation`'s reference does).
- **Determinism:** the check is serial per collapse; if its per-input checks run in parallel (as `IsWithin` does per
  triangle), parallel equals serial bit for bit (the decision is an AND of independent answers, and the sets are
  sorted before they are stored), repeated runs equal.
- **Mutation:** mutants that drop `D(T) \ R` from the candidates (unsound: accepts too much? no, rejects more; must be
  killed by an acceptance count test), that skip the update of the inverse map (unsound: a later collapse misses an
  affected input; killed by the guarantee test on the spiky sphere), that check only `N` and not the old sets, that
  use the corners only (no subdivision), and that keep the old `D(T)` after an accepted collapse.
- **Oracle:** O2 (the a posteriori `MeshHausdorffDistance`) is the oracle; a brute-force test on small meshes compares
  the sets against a full recomputation of `h(T, S)` per input triangle after every collapse.
- **Time:** every test under 5 s (meshes of 2k-10k triangles); the probe's cases above 5 s go to the benchmark.

## 8. Upper bound / cost estimate

- From the probe: B costs CHECK_FACTOR the time of A (one-sided), of which CHECK_SHARE is the reverse check; A itself
  costs 15-60x the plain QEM reduction (the `IsWithin` checks, as measured before: 90-98 % of the time).
- The probe's check is naive: it runs before the base's cheaper rejections, allocates per check, recomputes every
  corner distance per subdivision level, and does not share corners between the affected inputs (neighbouring input
  triangles share them). Expected gains of a library version: run it last (after flip, fold, deviation,
  self-intersection) — the probe's own counts say how many checked collapses were rejected by the base anyway —;
  share corner distances per collapse; a carried margin per input triangle (skip the check if the moved vertex moved
  less than `ε - U(T)`, as the D1 reference rule and the carried-bound spike of ENVELOPE.md §6 do). A realistic target
  is the reverse check at or below the cost of the forward check, i.e. B at about 1.5-2x A.
- Number of collapses lost: section 5 (final triangle counts of A and B).

## 9. Risks

- **The cost at fine tolerances**: the affected set grows with the ratio of input to output triangles under a ring;
  the probe measured 14-43 input checks per collapse. On meshes reduced by 100x or more it will grow further.
- **Operations that move geometry outside `CollapseEdge`** (Reproject, the Remesher's smoothing and flips, the alpha
  wrap pipeline's quality pass): each needs the hook or must be excluded; forgetting one breaks the invariant silently.
  A debug check (full recomputation on small meshes) catches it in tests.
- **`PartitionedReduction`** refuses `MaxDeviation` today; the two-sided option there needs the slab argument written
  and a test that the sets never cross a frozen border.
- **Floating-point certificate**: the guarantee is "certified up to rounding", like `MaxDeviation`'s; calling it exact
  needs O3.
- **Scale**: the distance computations under- and overflow at extreme extents, as #731 found in `MeshHausdorffDistance`;
  the check needs the power-of-two frame of #735.
- **Memory**: one small list per input triangle plus the inverse map (about 10 ints per input triangle in the probe,
  `max deps` 6-10).

## 10. Decisions for the owner

1. **Build O1** (two-sided bound by dependency sets, an opt-in `Reducer` option next to `MaxDeviation`, eg
   `MaxDeviationTwoSided` or a `DeviationMode` enum; API additive, compatible with 1.0.324)? Recommended.
2. **Default:** off (opt-in, the one-sided `MaxDeviation` stays as it is), or on whenever `MaxDeviation > 0`
   (changes results and time of existing callers)? Recommended: off.
3. **"Exact" or "certified up to rounding"**: is the floating-point certificate of `IsWithin` enough (as today for
   `MaxDeviation`), or should O3 follow (tier L, exact distance predicates for both directions)? Recommended: enough for
   now; O3 only on a concrete need.
4. **Scope of the first PR:** `Reducer.CollapseEdge` only (Reproject off, `FastCollapsePass` and `PartitionedReduction`
   refuse the option), or the slabs as well? Recommended: `Reducer` only.
