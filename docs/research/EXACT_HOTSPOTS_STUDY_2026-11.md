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
| 1. The sign-bearing exact stage of `orient3d` (16-21 % on the slow cases) | It is called almost only from `MeshRoundingRepair`'s triangle-triangle tests on the rounded doubles. Every exact-stage call takes the fixed-width integer path `orient3d_int` (192-bit, no BigInteger); the expansions run on about 0.02 % of the recorded calls. Of the calls that reach the exact stage, **19.9 % are exactly 0 and 73.7 % lie below one ulp of the permanent** (r < 1). No floating-point filter can decide these | a tighter proven filter (Ozaki et al.-style, down to an ideal bound of 1 u · permanent) decides at most **6.3 %** of the exact-stage calls. The exact stage is 15.2 % of the workload, so the bound is **about 1 %** | the integer path itself: 40-150 ns per call against 9-15 ns for the filter. Even halving it bounds at about 7.6 % of the workload | stop |
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
   - otherwise Shewchuk's adaptive expansions (`AdaptivePredicates.Orient3d`), or the double-double fallback out of range.
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

Mode `filter`, `results/filter.txt`, 37 cases. It records every call that reaches the exact stage (up to 200 000 per case;
4.66 million calls in all).
- **Path:** the integer path decides all the recorded calls except 228, on the Cluster and FoldedCluster cases (47, 81, 24, 55,
  8 and 13; first run of the mode), which go to the expansions.
- **Ratio:** r = |det| / (u · permanent), with u = 2^-53. The filter decides r > 7. A tighter proven bound θ u · permanent
  would decide θ < r ≤ 7.

| r | share of the exact-stage calls | decidable by |
|---|---|---|
| 0 (exactly degenerate) | 19.9 % | the exact stage only |
| 0 < r < 1 | 73.7 % | the exact stage only (below the rounding of the determinant itself) |
| 1 ≤ r < 3 | 3.5 % | an ideal filter (θ about 1) |
| 3 ≤ r ≤ 7 | 2.8 % | a filter about twice as tight (θ about 3, the order of the bounds of Ozaki et al. 2016 for 2D) |
| r > 7 | 0.1 % | the underflow term of the current bound |

- **Cost per call** (the recorded inputs, plain calls): `Orient3d` with the exact stage takes 37-148 ns (mean 87 ns), the
  filter alone 8-19 ns.
- **Share:** the exact stage is about 15 % of the workload (calls × (exact − filter)), and up to 19 % on single cases (FoldedCluster
  k = 20 and 40). This matches the earlier Stopwatch count (16.2 %) and the profile (12 % of the busy time).

### 1.4 The bound and the stop

- A tighter filter saves at most the exact-stage time of its newly decided calls: 15.2 % × 6.3 % ≈ **1.0 %** of the workload
  with an ideal θ = 1, or **0.4 %** with θ = 3. That is below the 10 % threshold. **Stop; no new error bound is proposed**,
  so none needs a derivation.
- The next candidate is the exact path itself (`orient3d_int`, about 80 ns over the filter). A 2× faster integer kernel bounds
  at 15.2 % × 0.5 ≈ 7.6 % of the workload, and at about 9.5 % on the worst case. That is below the threshold; it was not
  pursued.
- The call count is the lever, and it lives in the rounding repair (section 3).

### 1.5 Options (hot spot 1)

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 1a | a tighter semi-static or static filter for `orient3d` (a new error bound, with a derivation) | ≤ 1 % on the slow tail, less on normal inputs | M (a bound, its proof, a mutation gate) | low, but a new error bound for ≤ 1 % |
| 1b | an interval (dynamic) stage between the filter and `orient3d_int` | ≤ 1 %, for the same reason: 94 % of the calls are at or below the rounding of the determinant | M | low |
| 1c | a faster `orient3d_int` | ≤ 7.6 % | M | low |
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
- The tests (`MeshRoundingRepair.intersects`, inclusive) are 47.6 % of `ExactMeshBoolean.Compute` busy time in the profile.
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
