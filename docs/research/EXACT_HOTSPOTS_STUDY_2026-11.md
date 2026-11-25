# The exact Boolean's hot spots on the slow tail: orient3d's exact stage and the projected tri-tri filter (2026-11-25)

A study of the two hot spots that the profile of `docs/research/HIGHER_MATH_STUDY_2026-11.md` (PR #1563, not yet merged) (section 2.5,
PR #1563) found in `ExactMeshBoolean` on degenerate inputs. The owner approved it on 2026-11-25. **No library code was
changed.** The probes are in [benchmarks/ExactHotspotsStudy](../../benchmarks/ExactHotspotsStudy/README.md), and the raw
outputs are in its `results/` folder.

The measurement counters and the spike's cache were a temporary patch of the library, kept as
`results/probe_counters.patch` and never committed. All inputs are procedural. The numbers are single runs on a Linux
container with 4 cores (Xeon 2.1 GHz), .NET 10.0.115, master 25b5cc8. The owner decides on every option.

## Summary

| Hot spot | What the time is | Upper bound of the proposed lever | Next candidate measured | Leaning |
|---|---|---|---|---|
| 1. The sign-bearing exact stage of `orient3d` (16.6 % of the workload in place, 21 % on the slowest case; 12 % in the profile) | It is called almost only from `MeshRoundingRepair`'s triangle-triangle tests on the rounded doubles. Every exact-stage call takes the fixed-width integer path `orient3d_int` (192-bit, no BigInteger); the expansions run on about 0.02 % of the recorded calls. In the filter's own view, **94 % of the calls that reach the exact stage have |det_fp| below u · permanent** (r_fp < 1), where no floating-point filter can certify a sign | a tighter proven filter (Ozaki et al.-style, even at the invalid ideal θ = 1) has at most **5.7 %** of the exact-stage calls as candidates. The bound is **about 0.9 %** of the workload, and 2.1 % at the high-side estimate | the integer path itself: 32-267 ns per call against 9-28 ns for the filter. Halving it bounds at 6-8.5 % of the workload, and about 10.5 % on the worst case only | stop |
| 2. `TriangleTriangleFilter.by_projection` (11.9 %) | It is a filter on doubles in front of the exact tri-tri test, using exact `Orient2d` and comparisons only. It constructs no point. 98 % of its time comes from `MeshRoundingRepair.improves`, and 73 % is its own arithmetic | indirect predicates (Attene 2020; Cherchi et al. 2020) help where implicit points are constructed. This path constructs none, so their bound here is **0**. The arrangement already uses them (`math/Generated/IndirectPredicates.cs`) | a cache of the repair's tri-tri results. They are tested **2.0 times per distinct pair** (2.2 on the slowest case), and the tests are 47.6 % of the Boolean, so the ideal bound was about 24 %. **The spike is 25 % slower** (identical results, 37 of 37), because a key of 18 doubles costs more than the filtered test it saves | stop. The real lever is fewer tests in the rounding repair (section 3) |

**How others solve it, in one line each.**
1. Semi-static and static filters with proven tight bounds (Ozaki, Bünger, Ogita, Oishi, Rump 2016, for 2D) and interval
   (dynamic) filters (Brönnimann, Burnikel, Pion 2001) raise the share decided in floating point. ESSA (Ratschek and Rokne)
   and fixed-width integer kernels decide the rest exactly. The library has all of these kinds: its filter, the integer path,
   the expansions, and the generated limb predicates after Ratschek-Rokne and Kulisch. What remains are inputs that are
   degenerate or nearly so, where every method needs the exact stage.
2. Indirect predicates avoid constructing intersection points in the arrangement (Attene 2020; Cherchi et al. 2020). The
   library uses them there already. The overlap test of Guigue and Devillers (2003) is a decision tree over `orient3d` signs:
   the same predicates the library's exact test uses, so it would not change the exact-stage calls of degenerate pairs.

## 1. Hot spot 1: the sign-bearing exact stage of `orient3d`

### 1.1 What the library has (building blocks)

`ExactPredicates.Orient3d` has three stages:
1. A dynamic filter, `Orient3dFilter`. Its bound is `Orient3dBound` = (7 + 56u) u times the permanent, plus an underflow term.
2. The exact stage `orient3d_exact`:
   - first the trivial equal-point zero;
   - then `orient3d_int`, a fixed-width 192-bit integer determinant, when the coordinate differences fit in 64 bits at the
     common exponent;
   - otherwise Shewchuk's adaptive expansions (`AdaptivePredicates.Orient3d`), or `DoublePredicates.Orient3d` when the coordinates are out of the expansions' range.
3. The PredicateCompiler-generated predicates (`math/Generated/`): limb kernels after Ratschek-Rokne and Kulisch, and
   indirect predicates for implicit points.

### 1.2 Where the calls come from

The callers come from the dotnet-trace profile of PR #1563's degenerate stratum, folded by call path
(`callpath.py`, `results/callpath_degenerate.txt`).
- `orient3d_exact` takes 1962 ms of 16 167 ms busy time. Its callers:
  - `ExactTriangleIntersection.Intersects` < `MeshRoundingRepair.intersects`: 58.7 %;
  - `SegmentIntersectsTriangle` < `IntersectsBeyondShared`: 25.8 %;
  - `tri_tri`: 7.5 %;
  - the arrangement (`separated`, `one_side_except_shared`): about 4 %.
- So the hot spot is the rounding repair's validation of candidate edits, on the rounded doubles. It is not the arrangement.
- 99.9 % of the time inside `orient3d_exact` is its own code (the inlined integer path); `AdaptivePredicates` takes 0.1 %.
- At the phase level (mode `cases`, `results/cases.txt`, clean master): the `rounding` phase is **83 %** of the workload's
  wall time, 2612 of 3132 ms. The arrangement is 16 % of the busy time in the profile.

### 1.3 The stage histogram

Mode `filter`, `results/filter.txt`, 37 cases, 4.66 million calls that reach the exact stage.
- **Sampling.** The library records them by reservoir sampling: up to 200 000 per case, uniform over each case's calls. The
  shares are scaled to all calls.
  - The two FoldedCluster k = 40 cases have 3.89 of the 4.66 million calls, so they dominate the totals.
  - A first run recorded the first 200 000 calls per case instead. It gave the same picture, with 19.9 % exact zeros and
    73.7 % at 0 < r < 1.
- **Path:** the integer path decides all the recorded calls except 116, which go to the expansions. They are on the Cluster k = 20
  (24, 55) and FoldedCluster cases (8, 13 at k = 20; 5, 11 at k = 40).
- **Two ratios.**
  - r = |det| / (u · permanent), with the exact determinant: how far the call is from degenerate.
  - r_fp = |det_fp| / (u · permanent), with the floating-point determinant the filter computes. This is what a filter sees.
  - The library's filter decides r_fp > 7 + O(u) (`Orient3dBound` = (7 + 56u) u, with an underflow term that is negligible at
    these scales). A filter with a proven bound θ u · permanent could decide at most the calls with θ < r_fp ≤ 7.
- **What the bins do not say.** The bins of the exact r are not what any filter tests. A valid θ = 1 filter is sure to decide
  only calls with r above about 1 + 7, and it may decide some with r < 1 whose det_fp happens to be large. The cross table
  shows that this mixing is small.

| r_fp (the filter's view) | share of the exact-stage calls | candidates of |
|---|---|---|
| 0 (det_fp = 0, including a zero permanent) | 7.4 % | none |
| 0 < r_fp < 1 | 86.8 % | none: below the rounding of the determinant |
| 1 ≤ r_fp < 3 | 3.2 % | only a filter at the ideal θ about 1 |
| 3 ≤ r_fp ≤ 7 | 2.5 % | a filter about twice as tight as today's (θ about 3, the order of the bounds of Ozaki et al. 2016 for 2D) |
| r_fp > 7 | 0 % | (decided by today's filter) |

The cross table of the exact r against r_fp, as shares of all exact-stage calls:

| exact r \ r_fp | 0 | (0, 1) | [1, 3) | [3, 7] | > 7 |
|---|---|---|---|---|---|
| r = 0 | 7.33 % | 3.96 % | 0.02 % | 0 | 0 |
| 0 < r < 1 | 0.11 % | 82.64 % | 0.45 % | 0 | 0 |
| 1 ≤ r < 3 | 0 | 0.19 % | 2.65 % | 0.09 % | 0 |
| 3 ≤ r ≤ 7 | 0 | 0 | 0.10 % | 2.43 % | 0 |
| r > 7 | 0 | 0 | 0 | 0.03 % | 0 |

The 0.02 % with r = 0 and r_fp ≥ 1 show that a filter at θ = 1 would not be valid. A provable θ lies above 1.

**Cost per call.** On the recorded inputs, as plain calls, `Orient3d` with the exact stage takes 32-267 ns and the filter
alone 9-28 ns. The highest values are on the FoldedCluster k = 40 cases (252-267 ns and 25-28 ns), where even the filter is
slow.

**Share of the exact stage in the workload.** The three measurements disagree:
- in-place, with Stopwatch counters around every real call (PR #1563, `orient3d_zero.txt`): **16.6 %** (784 of 4732 ms),
  21 % on FoldedCluster k = 40;
- the dotnet-trace profile: **12 %** of the busy time;
- this microbenchmark, as calls × (exact − filter): **36 %**, and 40-55 % on FoldedCluster k = 40.

The microbenchmark times the sampled inputs out of their context (200 000 scattered records, so the data is cold) and is the
high-side estimate. The in-place measures are used below, and the high side is checked.

### 1.4 The bound and the stop

- A tighter filter saves at most the exact-stage time of its candidates. With θ = 1 these are 5.7 % of the calls (r_fp ≥ 1),
  with θ = 3 they are 2.5 %. The time per call is about the same across the bins.
- The bound is therefore 16.6 % × 5.7 % ≈ **0.9 %** of the workload (θ = 1, not even valid) and 0.4 % (θ = 3). Even at the
  microbenchmark's 36 % it is about 2.1 %. All of these are below the 10 % threshold. **Stop; no new error bound is
  proposed**, so none needs a derivation.
- Why the stop does not hinge on the exact bins: 94 % of the exact-stage calls have r_fp < 1. At or below the rounding of the
  determinant no floating-point filter can certify a sign, whatever its constant.
- The next candidate is the exact path itself (`orient3d_int`). A 2× faster integer kernel bounds at half the exact-stage
  share:
  - 6-8.5 % of the workload with the in-place measures, below the threshold;
  - about 10.5 % on the worst single case (FoldedCluster k = 40, 21 % in place);
  - 18 % at the microbenchmark's high side.
  
  It is borderline on the worst case only. It was not pursued, and it is listed for the coordinator.
- The call count is the bigger lever, and it lives in the rounding repair (section 3).

### 1.5 Options (hot spot 1)

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 1a | a tighter semi-static or static filter for `orient3d` (a new error bound, with a derivation) | ≤ 1 % on the slow tail, less on normal inputs | M (a bound, its proof, a mutation gate) | low, but a new error bound for ≤ 1 % |
| 1b | an interval (dynamic) stage between the filter and `orient3d_int` | ≤ 1 %, for the same reason: 94 % of the calls are at or below the rounding of the determinant | M | low |
| 1c | a faster `orient3d_int` | ≤ 6-8.5 % on the stratum, about 10.5 % on the worst case, 18 % at the high-side estimate (for a 2x kernel) | M | low |
| 1d | nothing | | | |

**Leaning:** 1d.

## 2. Hot spot 2: `TriangleTriangleFilter.by_projection`

### 2.1 What it is

`TriangleTriangleFilter` (intersection/TriangleTriangleFilter.cs) is a filter on doubles in front of
`ExactTriangleIntersection.IntersectsBeyondShared`. It decides the pairs that a projection to an axis plane settles, using exact
`ExactPredicates.Orient2d` and comparisons, and it adds no error bound of its own. Its cases:
- both triangles in one axis plane;
- the plane sides of neighbours;
- the projection along the dominant axis of A's normal, with the shared-corner cases;
- one triangle in an axis plane.

It **constructs no point**. Its inputs are the rounded doubles of the result.

In the profile, `by_projection` takes 2677 ms. Of that:
- 72.6 % is its own arithmetic, 9.6 % `ExactPredicates.Orient2d`, 7.0 % `common_axis_plane`, and 4.2 % `orient2d_exact` plus
  about 6 % of expansions.
- 97.8 % of its time comes from `MeshRoundingRepair.intersects` < `improves`.

### 2.2 Indirect predicates: the bound is 0

- Attene 2020 (*Indirect predicates for geometric constructions*, CAD) and Cherchi, Livesu, Scateni, Attene 2020 (*Fast and
  robust mesh arrangements using floating-point arithmetic*, TOG) evaluate predicates on implicit points, such as an
  edge-plane or three-plane intersection, without constructing them.
- The library uses them in the arrangement already: `math/Generated/IndirectPredicates.cs`, cited in
  `docs/sources/exact-arithmetic-and-booleans.md`.
- The projection path has no implicit point: every corner it sees is a double of the rounded mesh. So the share that indirect
  predicates could avoid here is **0**. The coordinator's question "what does the projection path still construct exactly"
  has the answer: nothing.
- Guigue and Devillers 2003 (JGT 8(1)) is an overlap test as a decision tree over `orient3d` signs; only the paper's metadata
  and opening were seen. It would replace one sequence of the same predicates by another. On the degenerate pairs here (the
  `orient3d` of section 1, 94 % at or below rounding), it would still need the exact stage. No bound above the 10 % threshold
  is visible, so it was not measured.

### 2.3 The next candidate: the repetition of the tests

Mode `pairs`, `results/pairs.txt`.
- `MeshRoundingRepair` runs 3.41 million tri-tri tests on the workload, for 1.69 million distinct pairs (the 18 coordinates):
  **2.0 tests per distinct pair**, and 2.2 on FoldedCluster k = 40 against a shifted copy.
- The tests (`MeshRoundingRepair.intersects`, inclusive) are 47.6 % of `ExactMeshBoolean.Compute` busy time. This share is from
  PR #1563's dotnet-trace profile of the same workload (5 repetitions). The 2.0 tests per distinct pair are from this
  study's mode `pairs` (one run).
- An ideal cache would have bounded at 47.6 % × (1 − 1/2.0) ≈ **24 %**, above the threshold. So a spike followed.

### 2.4 The spike: a cache of pair results

The cache was a temporary patch: a per-thread `Dictionary` in `MeshRoundingRepair.intersects`. The key is the full 18
coordinates, so there are no false hits. The result is a pure function of the coordinates: the canonical ids are equal
exactly at equal positions. Mode `cache`, `results/cache.txt`, minimum of 3 alternating runs per case:

| | without | with the cache |
|---|---|---|
| workload | 2373 ms | 2959 ms (**+24.7 %**) |
| FoldedCluster k = 40, shifted copy | 1140 ms | 1448 ms (+27 %), 1.08 million hits |
| results identical bit for bit | | 37 of 37 |

- **Why the bound did not hold:** it assumed a hit costs nothing and a miss costs no more than before. In fact each lookup
  hashes 18 doubles, and each miss stores a key of about 150 bytes in a table of up to a million entries (cache misses, growth and GC).
- That costs more than the filtered test it saves: most tests are decided by `by_projection` or the `orient3d` filter in a few
  tens of nanoseconds. A cheaper key (a 64-bit hash without the coordinates) would admit false hits, and that is not
  acceptable in front of an exact test.
- **Stop.**

### 2.5 Options (hot spot 2)

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 2a | indirect predicates on the projection path | 0 (no constructions on this path) | | |
| 2b | a Guigue-Devillers style decision tree | not visible: the same predicates, the same exact stage on degenerate pairs | M | low |
| 2c | a cache of pair results | −25 % (measured, slower) | | |
| 2d | nothing | | | |

**Leaning:** 2d.

## 3. What the measurement points to (for the coordinator)

On this stratum the slow tail is the rounding repair: 83 % of the wall time, and 63 % of the busy time in `improves` alone. Its
cost is the number of candidate edits times the tri-tri tests that validate each edit; the predicates are already near their
floor. The next candidates are:
- **Where the edits come from** (`results/callpath_degenerate.txt`): `improves` is called by `try_move` from
  `fix_intersection` (56 %), `search_corners` (22 %) and `fix_degenerate` (10 %), and by `try_flip` (6 %).
- **Fewer tests per candidate edit.** The repair validates an edit against the pairs near the moved vertices. The measured
  repetition (2.0 tests per distinct pair) shows that edits are re-validated against the same geometry. An incremental
  validation that tests only pairs with a moved corner is the lever. Its bound needs the share of tests whose pair contains
  no moved corner, a new counter, not measured here.
- **Fewer candidate edits** on folded clusters at 2^-40: FoldedCluster k = 40 is 52 % of the workload by itself. See
  [ROUNDING_COST_2026-11.md](ROUNDING_COST_2026-11.md) for the repair's cost model.

These are algorithmic changes of `MeshRoundingRepair`, outside this study's two ideas. They are listed, not pursued.

## 4. What was read and what was not

- Papers, as abstracts, metadata and opening pages (web search, 2026-11-25):
  - Ozaki, Bünger, Ogita, Oishi, Rump, *Simple floating-point filters for the two-dimensional orientation problem*, BIT
    56, 2016. Semi-static and static filters for 2D; no 3D version found.
  - Ratschek, Rokne, *Exact computation of the sign of a finite sum*, Appl. Math. Comput. 1999 (ESSA), and Mörig, Schirra,
    *Engineering an exact sign of sum algorithm*, tech. report 2010.
  - Cherchi, Livesu, Scateni, Attene, TOG 2020.
  - Guigue, Devillers, JGT 8(1), 2003.
- Brönnimann, Burnikel, Pion 2001 and Attene 2020 are as already cited in `docs/sources/`.
- The coordinator's reference to Ozaki, Ogita, Rump, Oishi "adaptive and efficient algorithms for the orientation problem"
  (about 2009/2012) matches the group's earlier 2D work on error-free determinant transformations (Ozaki, Ogita, Oishi,
  Information and Computation 2012). It was seen in search results only, not read.
- No code from outside the repository was read. No CGAL, Triangle, libigl, GEOS or GPL / LGPL / MPL code. The search results
  listed a libigl port of the Guigue-Devillers code; it was not opened.
- Library code read: `ExactPredicates` (`Orient3d`, `Orient3dFilter`, `orient3d_exact`, `orient3d_int`),
  `TriangleTriangleFilter`, `MeshRoundingRepair.intersects`, and the sources entries for the generated predicates.
