# Simplification with a two-sided Hausdorff bound (study)

**Status:** study, docs only (2026-11-13). No library code. The numbers come from a throwaway probe (a `Reducer`
subclass in a scratch project, not committed; its algorithm is given in section 5.1 so it can be rebuilt). Decisions for
the owner are in section 10.

**Build (2026-11-13, #791):** O1 is `Reducer.MaxDeviationTwoSided` (`mesh/ReducerReverseCoverage.cs`), decided by the
coordinator as recommended (section 10: opt-in, default off, floating-point certificate, `Reducer` only; FastCollapsePass
added in #792). With the check last in `CollapseEdge` it costs 1.41x the one-sided reduction at the median (max 1.65x,
`benchmarks/ReducerBenchmark` README, "twosided"), well under the probe's 2.4-8.2x; the 12 Thingi10K cases run only
in the benchmark, the PR tests use procedural meshes.

Result in short:
- The library already proves, per collapse, the output side: `h(S, M) <= ε` (`Reducer.MaxDeviation`), closed and
  manifold (link condition), no self-intersections (`PreventSelfIntersections`, exact). Missing is `h(M, S) <= ε`.
- Today's one-sided reduction violates `h(M, S) <= ε` in 12 of 18 measured cases (procedural and Thingi10K), by up to
  0.28 ε (proven by the lower bound of `MeshHausdorffDistance`).
- Recommended: **O1**, per input triangle a small set of output triangles it is certified against, re-checked only when
  a collapse touches one of them (Klein et al. 1996's correspondence idea with the library's Sacht-Jacobson bounds). The
  bound is global and proven by induction from local checks. In the probe it fixes all 18 cases, costs 0-7 % more
  triangles (+72 % on the worst violator) and 2.4-8.2x the time of today's one-sided reduction with a naive check; a
  library version should reach about 2x (section 8).

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
  edge, a safe covering radius); else subdivide `T` into 4 up to a depth limit; reject if a corner is farther than ε from every candidate.
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

| mesh | input triangles | ε / diagonal | plain QEM (ms) | A: triangles, ms, h(S,M)/ε, h(M,S)/ε (lower bound) | B: triangles, ms, of which reverse check ms, h(S,M)/ε, h(M,S)/ε | B: collapses rejected by the reverse check / done, input checks per checked collapse, largest D(T) | time B / A |
|---|---|---|---|---|---|---|---|
| bumpy sphere | 18252 | 0.001 | 62 | 1706, 3545, 1.008, 1.011 (**1.001**) | 1706, 14964, 11844, 1.008, 1.009 | 2681 / 8273, 25.2, 9 | 4.2x |
| bumpy sphere | 18252 | 0.003 | 50 | 518, 1802, 1.003, 1.007 (0.997) | 518, 5712, 3605, 1.003, 1.007 | 812 / 8867, 35.9, 8 | 3.2x |
| spiky sphere | 41772 | 0.001 | 104 | 1252, 5782, 1.009, 1.009 (1.000) | 1252, 24960, 18084, 1.009, 1.009 | 1931 / 20260, 37.6, 9 | 4.3x |
| spiky sphere | 41772 | 0.003 | 107 | 432, 3430, 1.009, 1.024 (**1.017**) | 434, 10403, 5967, 1.009, 1.008 | 672 / 20669, 43.1, 6 | 3.0x |
| terrain 80 | 12800 | 0.001 | 28 | 2941, 1339, 1.008, 1.176 (**1.167**) | 2940, 7941, 6367, 1.008, 1.009 | 4798 / 5008, 13.8, 10 | 5.9x |
| terrain 80 | 12800 | 0.003 | 37 | 932, 1020, 1.002, 1.109 (**1.099**) | 936, 2850, 1684, 1.002, 1.009 | 1444 / 6043, 22.5, 7 | 2.8x |

### 5.3 Thingi10K (fetched at run time, not committed)

Six models drawn with seed 731 from the Thingi10K metadata (revision `2d5d3b2f`, as in `benchmarks/UVAtlasBenchmark`)
among the closed, edge- and vertex-manifold, single-component meshes with 5k-20k faces and no self-intersections,
duplicated or degenerate faces: IDs 1036464, 51011, 894833, 472201, 1436278, 60516.

| mesh | input triangles | ε / diagonal | plain QEM (ms) | A: triangles, ms, h(S,M)/ε, h(M,S)/ε (lower bound) | B: triangles, ms, of which reverse check ms, h(S,M)/ε, h(M,S)/ε | B: collapses rejected by the reverse check / done, input checks per checked collapse, largest D(T) | time B / A |
|---|---|---|---|---|---|---|---|
| 1036464 | 7068 | 0.001 | 28 | 940, 1311, 1.009, 1.077 (**1.068**) | 954, 4545, 3520, 1.009, 0.998 | 1543 / 3057, 20.6, 13 | 3.5x |
| 1036464 | 7068 | 0.003 | 17 | 390, 626, 1.004, 1.008 (0.998) | 394, 2138, 1424, 1.004, 1.008 | 584 / 3337, 29.8, 13 | 3.4x |
| 1436278 | 18610 | 0.001 | 60 | 532, 3554, 1.004, 1.288 (**1.279**) | 914, 28643, 25020, 1.001, 1.006 | 1167 / 8848, 86.3, 18 | 8.1x |
| 1436278 | 18610 | 0.003 | 58 | 318, 1862, 0.999, 1.007 (0.998) | 320, 14312, 12089, 0.999, 1.007 | 332 / 9145, 113.0, 11 | 7.7x |
| 472201 | 5572 | 0.001 | 13 | 346, 1179, 1.004, 1.010 (**1.003**) | 370, 2874, 1846, 1.003, 1.010 | 707 / 2601, 28.2, 8 | 2.4x |
| 472201 | 5572 | 0.003 | 13 | 182, 643, 1.009, 1.041 (**1.033**) | 186, 2342, 1674, 1.009, 0.980 | 287 / 2693, 33.0, 5 | 3.6x |
| 51011 | 8656 | 0.001 | 21 | 1764, 1586, 1.004, 1.017 (**1.008**) | 1772, 4049, 2501, 1.004, 1.008 | 2705 / 3442, 16.0, 8 | 2.6x |
| 51011 | 8656 | 0.003 | 22 | 902, 936, 0.998, 1.047 (**1.037**) | 908, 2268, 1249, 0.998, 1.005 | 1359 / 3874, 23.2, 11 | 2.4x |
| 60516 | 14628 | 0.001 | 33 | 1652, 1732, 1.009, 1.126 (**1.116**) | 1676, 14222, 12194, 1.009, 1.010 | 2558 / 6476, 24.5, 23 | 8.2x |
| 60516 | 14628 | 0.003 | 39 | 1000, 1143, 1.007, 1.150 (**1.141**) | 1010, 4676, 3442, 1.007, 0.992 | 1449 / 6809, 32.7, 16 | 4.1x |

### 5.4 Reading

Bold: a lower bound above ε, so a **proven** violation of `h(M, S) <= ε` by today's one-sided reduction.

- **The one-sided bound is not two-sided in practice.** Today's `MaxDeviation` reduction violates `h(M, S) <= ε` in
  12 of 18 cases (4 of 6 procedural, 8 of 12 Thingi10K), by up to 0.28 ε (1436278 at 0.001; terrain 0.17 ε at its open
  rim). The forward direction stays within ε everywhere (upper bounds <= 1.009 ε, the oracle's tolerance is 0.01 ε).
- **O1 closes it.** B's `h(M, S)` upper bound is <= 1.010 ε in all 18 cases (consistent with <= ε at the oracle's
  tolerance), its `h(S, M)` unchanged.
- **Collapses lost:** few. B's final triangle count is within 0-1 % of A on the procedural meshes and 0.6-7 % on
  Thingi10K, except 1436278 at 0.001 (532 -> 914, +72 %), the case where A cut 0.28 ε deep: the extra triangles are
  what the bound costs there. The "rejected" counts are larger (B rejects 10-50 % of its checked collapses) because a
  rejected edge comes back after its neighbours change, and because the probe checks before the base's own guards.
- **Time:** B takes 2.4-8.2x A's time; the reverse check is 55-87 % of B, and B minus the check is about A's time.
  The worst factors are on 1436278 and 60516, where 86-113 and 25-33 input triangles are checked per collapse
  (their inputs are dense under smooth regions). A itself is 15-60x the plain QEM reduction (the known cost of
  `IsWithin`).
- **Sets stay small:** the largest `D(T)` is 5-23 output triangles; the probe's candidates are the new ring plus the
  old set (about 10-30 triangles).

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
- **Mutation:** the unsound mutants must be killed by the guarantee tests: skipping the update of the inverse map (a
  later collapse misses an affected input), keeping the old `D(T)` after an accepted collapse (stale geometry), testing
  the corners only (no subdivision, no covering term), `<` / `<=` swaps against ε. The over-strict ones by a count test
  (triangles reached on a fixed case): dropping `D(T) \ R` from the candidates, a smaller depth limit.
- **Oracle:** O2 (the a posteriori `MeshHausdorffDistance`) is the oracle; a brute-force test on small meshes compares
  the sets against a full recomputation of `h(T, S)` per input triangle after every collapse.
- **Time:** every test under 5 s (meshes of 2k-10k triangles); the probe's cases above 5 s go to the benchmark.

## 8. Upper bound / cost estimate

- From the probe: B costs 2.4-8.2x the time of A (one-sided), of which 55-87 % is the reverse check; A itself
  costs 15-60x the plain QEM reduction (the `IsWithin` checks, as measured before: 90-98 % of the time).
- The probe's check is naive: it runs before the base's cheaper rejections, allocates per check, recomputes every
  corner distance per subdivision level, and does not share corners between the affected inputs (neighbouring input
  triangles share them). Expected gains of a library version: run it last (after flip, fold, deviation,
  self-intersection) — the probe's own counts say how many checked collapses were rejected by the base anyway —;
  share corner distances per collapse; a carried margin per input triangle (skip the check if the moved vertex moved
  less than `ε - U(T)`, as the D1 reference rule and the carried-bound spike of ENVELOPE.md §6 do).
- Upper bound of the gain: the non-check part of B is about A's time, so even a free reverse check gives B = A; a
  reverse check as costly as the forward one (the same kind of bound on a similar number of triangles) gives about
  1.9x A. Target for the library version: B <= 2x A at the median of the 18 cases, <= 3x at the worst (1436278).
  Above that, the stop rule: measure first which of the three savings above carries it.
- Collapses lost: 0-7 % more triangles, +72 % where the one-sided reduction violated the bound most (section 5.4).

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
