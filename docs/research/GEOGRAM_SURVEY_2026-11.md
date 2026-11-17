# Geogram survey (2026-11)

Owner request 2026-11-17: go through the geogram rows of the table "Open items where geogram has an implementation" in
[sources-surveys.md](../roadmap/sources-surveys.md#geogram-as-a-source-owner-2026-11-03) and look for ideas that help
our open items. **Read and analysed only.** No library code was written, and geogram was neither built nor run.

- **Source:** [BrunoLevy/geogram](https://github.com/BrunoLevy/geogram), commit `16440c45c6ec5015b50f4a430c5ea1a300c2542f`
  (2026-11-15). Only files under `src/lib/geogram` whose header carries the Inria BSD-3 notice were read; each header
  was checked first (`head -40`). The files read are listed at the end of each section, with line ranges.
- **Not read:** `third_party/` (only named: OpenNL, HLBFGS, xatlas, ARPACK, SuperLU), `delaunay/delaunay_triangle.*`,
  `delaunay/delaunay_tetgen.*`, `mesh/mesh_tetrahedralize.*`, `delaunay/delaunay.cpp` (it registers the TetGen backend;
  only grepped for "tetgen"). The generated predicate files under `numerics/predicates/` carry no Inria notice: their
  headers were checked and nothing past them was read. So the form of geogram's generated filters is **assumed**, not
  verified.
- **2D mesher provenance:** the files of `delaunay/CDT_2d.*` and `delaunay/LFS.*` that were read are logged in
  [MESHING2D_PROVENANCE.md](../design/MESHING2D_PROVENANCE.md) (entry [GG26]). They were read for ideas only, and no
  mesher code was changed.
- **Not redone:** the boolean classification (`MeshSurfaceIntersection`). It was read before
  ([LEVY_CLASSIFICATION_LITERATURE.md](../design/LEVY_CLASSIFICATION_LITERATURE.md) section 2). Here only its rounding and
  output path was read, for RC4 of [BOOLEAN_TRIAGE_2026-11.md](BOOLEAN_TRIAGE_2026-11.md).
- **Method:** for each area, our counterpart and its current numbers were read first (docs/perf, the benchmark READMEs,
  docs/roadmap, docs/design), then the geogram code.
  - Speed ideas get an upper bound from our measured stage shares. The stopping rule asks for at least 10 %.
  - "Not measured" means that no number exists in our docs, and "assumed" marks a claim without a measurement.
  - All numbers on our side are quoted from the docs named in each section; none was measured for this note.

## Result in short

**Nothing to adopt now.** No geogram idea clears the 10 % speed bar on our measured stage shares. Our exact kernel is
already ahead of geogram's public one:
- Our exact points are canonical grid integers in fixed width. Geogram's own comment calls its public expansion points
  "10x to 20x" slower than the closed geogramplus design, which is the design we use.
- We construct points lazily; geogram builds every intersection point exactly, up front.
- Our rounding of output points is below 1 ulp; geogram's is `estimate/estimate`, with a TODO.

**RC4:** geogram does not solve it. Lévy's code does not repair what rounding causes:
- `remove_fins()` is "Not implemented yet".
- `mesh_remove_intersections` re-intersects and repairs without a bound.

So RC4 stays on the triage's own next step.

**Worth a measurement, ranked by expected value for effort.** Each is tied to an open item:

| # | Idea | Area | Expected gain | Evidence | Effort | Risk | Exact code | Recommendation |
|---|---|---|---|---|---|---|---|---|
| 1 | Pin-free spectral conformal map (Mullen et al. 2008; inverse power iteration on our `SparseCholesky`) as an opt-in next to LSCM | parameterization | quality: no pin choice, the P3 asymmetry (~1e-4) should go (assumed); effect on the P1 inner-ring collapse unknown | geogram ships a spectral LSCM mode, but apparently without the boundary mass matrix (not a reference to port); the paper; our P1/P3 fixtures | M | low as an opt-in; a default change would move ARAP/SLIM/atlas results | no | **measure first** (it is open research item #4): spike on the P1 cylinders, the P3 plate, ParameterizationBenchmark |
| 2 | Ruppert lfs of the PSLG as a yardstick (∫ 1/lfs², the size-optimality lower bound) for the 2D grading gap | 2D mesher | none by itself; it tells how far both meshers are from the bound (upper bound on any count gain: the reference gap, −29..−50 % on f-11/f-40, about −7 % on Herne) | our GRADING numbers; Ruppert 1995. Geogram's `LFS` is **3D**, a medial-axis LFS from Delaunay poles of surface samples; in 2D it goes to 0 at every polygon corner, so it cannot serve as a size function | M | low (a measurement) | no | **measure first**, as the yardstick for the coarsening option; also correct [meshing2d.md](../roadmap/meshing2d.md) option (2) ("`delaunay/LFS` exists": it does not serve it), done in this PR |
| 3 | True restricted Voronoi cells (facet clipping by bisectors, security-radius stop, as in geogram's RVD) for the (C') / R2 region check of certified remeshing | remesh (RDR) | quality of the certificate: the spike's nearest-input-vertex proxy "sees no feature smaller than the input's edges"; true cells see them. Cost of clipping not measured | geogram `RVD` / `generic_RVD` clipping; Yan, Lévy et al. 2009 (already T14 in CERTIFIED_REMESHING.md); spike README | M (float spike), L (exact) | the float version is no certificate; the exact one needs the planned bisector predicates and new bounds | yes, if exact | **measure first** in RDR stage 1b: a float RVC check in the spike against the proxy |
| 4 | Co3Ne (interpolating reconstruction: per-point tangent-disk Voronoi cells, triangles voted by all three vertices, then incremental manifold extraction) next to C1 | point clouds | quality: zero vertex error on clean dense scans, no grid resolution limit; speed: likely well below C1's 6-8 s (assumed). Loses C1's closed output | geogram `co3ne.cpp`; POINT_CLOUD_RECONSTRUCTION.md numbers | M | open or non-manifold output on noisy or thin data; our `NotCleanReason` check plus repair would be needed | no | **measure first**, only when the point-cloud item reopens (#887: a scan use case): a benchmark prototype on the five torus scenes against C1 |
| 5 | Constraint crossings from the original input segments, not from the current (already rounded) piece endpoints | 2D mesher | accuracy: no drift build-up along a chain of crossings (today "a few ulps" per piece is possible) | geogram `ExactCDT2d::create_intersection` (comment: "much better ... simpler coordinates"); our `Cross` | S | medium: the O11 tolerance argument must be re-checked; the source is not unique when a piece has several | no | **measure first**: count the drift (max distance of piece vertices from their source segments) on the M2 fuzz and corpus; adopt only if it exceeds a few ulps anywhere |
| 6 | Double filter in front of the BigInteger orient and crossing tests of `SnapRounding2` | 2D mesher (snap speed) | at most about 15 % of an M2 snap run (snap + restore ~0.1 s of 0.65 s in the prototype), less on quality runs | geogram has no snap rounding; the lesson is only "filter first"; SNAP doc 3f | S | low (same signs: the inputs are doubles times a power of two) | no | **measure first**: time `SnapRounding2.Apply` alone on the Herne / ALKIS snap runs; pursue only if ≥ 10 % of a run |
| 7 | Atlas: chart budget from the mesh size (geogram: facets / 30000 + 1 initial segments) and chart typing (cylinder / sock split along the inertia axis, boundary smoothing) | parameterization (atlas) | large set: 16 of 20 meshes hit `MaxCharts`, median 233 s; the gain is not measured (no stage times in the docs) | geogram `mesh_atlas_maker`; UVAtlasBenchmark README | S-M | changes atlases | no | **measure first** on the 20 large meshes; run the existing xatlas comparison for seam length before trying boundary smoothing |
| 8 | Skip the star-growth pass and the swallowed-vertex check of `Delaunay3.Insert` when no predicate of the insertion returned 0 | tet / wrap | at most ~21 % of `Insert`, so ~2.7 % of the alpha-wrap pipeline (L2 probe); only ≥ 10 % for a Delaunay-bound caller | our L2 split (measured); geogram has neither pass (it uses SoS) | S | low if the general-position argument holds; needs a proof note and the degenerate tests | no | **measure first**, only if a Delaunay-bound caller appears |
| 9 | BRIO + Hilbert order for bulk `Delaunay3` insertion | tet meshing | random → coherent order is 5x at 400k (measured); over our grid sort at most the location share (4 % in the wrap) | TetMeshBenchmark README; geogram `compute_BRIO_order` | S (reuse `DelaunayKernel2`'s BRIO/Hilbert) | none | no | **adopt with a batch caller** (e.g. a fresh Delaunay3 of the surface vertices in RDR); nothing to gain in the wrap |
| 10 | Re-measure where hole filling creates folds and degenerate triangles; if still the main source, Liepa 2003 (from the paper) as the start of `MinimalHoleFill` | repair | quality only; speed < 9 % (the repair-kept path is 9 % of the 400-set time) | the per-step numbers are from the 1.0.324-era run; geogram's `loop_split` is a greedy heuristic, not better than Liepa | S (measurement) | none | no | **measure first** (`BENCH_REPAIR=steps`, `analyze_stages.py` on master) |
| 11 | CVT (Lloyd) tangential relocation in the Remesher via `CustomSmoothF` | remesh | quality on smooth parts only; 99 % of q < 0.1 triangles touch feature constraints, which smoothing does not move | RemeshBenchmark; geogram `CVT` | S | low | no | **measure first**, low priority: one A/B in RemeshQualityBenchmark |
| 12 | `mesh_adjust_surface` (normal offset by least squares so facets straddle the input) as an opt-in post-pass | remesh | lower chordal error, at most about half the sagitta on convex inputs (assumed) | geogram code | S | vertices leave the input (conflicts with projection guarantees and certificates) | no | **measure first** only if a caller asks for lower chordal error; else drop |

**Dropped** (details in the sections):
- **Numerics:**
  - FPG semi-static filters: at most ~5 %; a SIMD spike on the same filter gave ~2 %.
  - Upward-rounding intervals: .NET cannot set the rounding mode, and geogram does not use them either.
  - Expansion-based `vec3HE` points, approximate lifts in incircle, a further Orient3d exact-stage kernel (now ~2 % of
    serial time): all dropped.
  - SoS: dropped for the boolean; the 15-term `orient_3d_SOS` table is a note for a future tet mesher.
- **2D mesher:**
  - Faster Sloan recovery: M2 is 10-20 % of a quality run.
  - Exact homogeneous CDT points (conflicts with D1), approximate lifting, the orient cache, `LFS` as a size function.
- **3D Delaunay:**
  - The cavity cache: ≤ ~2 %.
  - The inexact pre-walk.
  - Lock-based parallel insertion: not deterministic. Our deterministic insertion rounds (#774) already cover the wrap.
  - SoS in `Delaunay3`: dropped for now; a prerequisite only.
- **Remesh:**
  - A CVT remesher: topology is not kept, the parallel reductions are not deterministic, there are no sharp features,
    and our smooth-input p01 is already 40°.
  - The 6D anisotropy.
  - Geogram's vertex-clustering decimation: no error bound; we have QEM.
  - Its `mesh_smooth`: harmonic fairing, which `LaplacianMeshSmoother` already does.
- **Repair:** `loop_split` as a port; anti-Moebius orientation, signed-volume orientation and absolute-epsilon
  colocation, where ours are equal or better.
- **Parameterization:**
  - ABF++: BFF already flattens 87 % of the atlas charts near-conformally, and the tail comes from the fallbacks.
  - The raster validator and the Tetris packer: our exact `UVOverlap` and our rotating packer are stronger.
  - The iterative solver.

**Minor geogram defects seen while reading.** Recorded only, nothing posted upstream:
- `mesh_decimate_vertex_clustering` without `KEEP_B` indexes an empty `vertices_flags`.
- The angle variant of `mesh_LSCM.cpp` uses `angle_[c0+1+1]` where `c0+i+1` is meant; it is wrong only for polygon
  facets.
- ABF++ stops on absolute L1 thresholds, which grow with mesh size; geogram's own code marks this with a TODO.
- `PCK::approximate` is not correctly rounded; a TODO in the source says so.

## 1. Exact arithmetic and boolean robustness (PCK, expansion_nt, interval_nt, exact_geometry; RC4)

Geogram commit 16440c45 (2026-11-15). Every file read carries the Inria BSD-3 notice, except the generated predicate
files (see the end). Nothing in geogram was built or run. Numbers below are ours, quoted with their source; "not
measured" where our docs have none.

### (a) Filters, exact numbers, homogeneous points, SOS

#### 1. Our counterpart and its numbers

- Predicates on doubles: `math/ExactPredicates.cs`. Its filter is a per-call Shewchuk bound (constant times the
  permanent, plus an underflow term). Exact stage: `orient3d_int`, which is "kernel B" (64-bit differences in units of
  2^e, the determinant in 2-3 limbs). Then `AdaptivePredicates.cs` (Shewchuk stages B-D) and the generated
  `math/Generated/DoublePredicates.cs` (P2: m*2^e monomials on stack limbs).
- Arrangement: `mesh_ops/ExactMeshArrangement.cs`. Inputs go exactly onto one integer grid (`ScaleExp`, `Center`).
  Constructed points are implicit (a construction, a double approximation and an error bound, after Attene / Cherchi
  et al.). They are built only when a filter fails: a gcd-reduced homogeneous integer `ExactPoint3`, stored in 4 fixed
  limbs (`FixedArith`, docs/design/FIXED_WIDTH_EXACT_POINTS.md) with a `BigInteger` fallback.
- Other exact code: `math/ExactLimbSigns.cs` (the classification's sign stages, and Orient2 / InCircle beyond the fixed
  width, on stack limbs), `math/ExactIntervals.cs` (an RN interval widened by 1 ulp per operation),
  `math/PredicateStats.cs`. Generator: `tools/PredicateCompiler`.
- Numbers:
  - Serial shares of a boolean before #256 (docs/perf/EXACT_BOOLEAN_PERFORMANCE.md, "Where the time is now"):
    - all exact work: 10.4 / 12.9 / 10.9 % (`cgal` / Thingi10K / `cherchi`), n-ary 41 %;
    - exact `Orient3d` stages: 7.0 / 6.0 / 0 %, about 250 ns each, about 90 % of them with result 0.
  - Kernel B was built in #256 (docs/design/CHEAPER_CONSTRUCTIONS.md section 12):
    - one exact stage in place: 260 -> 176 ns;
    - `cgal`: 7.73M stages, now about 2 % of the serial time;
    - class runs −2.4 / −1.4 %;
    - on rotated inputs it admits 99.9-100 % of the exact stages (EXACT_BOOLEAN_PERFORMANCE.md, SafeRounding table).
  - Filter cost (benchmarks/PredicateComparison/README.md):
    - orient3d 7.0-7.6 ns, incircle 9 ns, insphere 28 ns per call, after #753;
    - SIMD filter spike: the filter is about 10 % of samples, total gain about 2 % (EXACT_BOOLEAN_PERFORMANCE.md);
    - "SIMD for the Orient3d filter measured at 2-4 %" (roadmap/exact-boolean.md).
  - Normal stratum (roadmap/exact-boolean.md, exact-numbers study):
    - exact arithmetic about 5-6 % self time;
    - exact point construction 2.4 / 4.9 % of the one-thread time.
  - P2: orient3d_bigint 5077 -> 202 ns.
- Open items this area serves:
  - docs/roadmap/hotspots.md row "Exact stage of the adaptive Orient3d" (#256 kernel B: built);
  - EXACT_BOOLEAN_PERFORMANCE.md candidates 1 (point-vs-plane kernel) and 6 (indirect predicates, not wired in);
  - the L3 candidates of 55928 (fixed-width limbs for the exact stages of implicit points);
  - candidate D (draft #893).

#### 2. Geogram: what it does

- **Predicate structure** (PCK.h, predicates.cpp around 1245 and 1841):
  - each predicate is a filter, then an exact stage, then SOS when the exact value is 0;
  - `PCK_STATS` counts invoke / exact / SOS per predicate with atomics. It is off by default: the comment says it
    almost doubles the run time (6'36 against 3'38 on a 2M-point run).
  - `orient_3d(double*)` = generated `orient_3d_filter`, then `orient_3d_exact`: 9 `expansion_diff` and
    `expansion_det3x3`, all on the stack.
  - multi_precision.cpp starts with `GEO_FP_CONTRACT_OFF` (no FMA), the same rule as ours.
- **Filters:**
  - The generated `*_filter` functions come from `.pck` sources through a code generator (PCK / `pck.sh`). I did not
    read their code: the files lack the Inria notice. That they are Meyer-Pion FPG semi-static filters (an error bound
    from the maximum magnitudes of the inputs) is my assumption from the FPG paper, not checked in the code.
  - Predicates on constructed homogeneous points (exact_geometry.cpp) use `interval_nt` as the filter.
- **interval_nt** (interval_nt.h):
  - `intervalRN`: round to nearest, after Harris (ACCU Overload 103). Each result is widened by a factor of 1 ± eps,
    with `±eps*min` near zero, NaN-safe, and a branch-free Sign2.
  - `intervalRU`: upward rounding via `_MM_SET_ROUNDING_MODE` / `fesetround`, storing −lower. It exists but is not used.
    The comment gives the reasons: not significantly faster, needs `-frounding-math`, and a rounding-mode guard in the
    client code. So geogram ships RN, as we do.
  - The conversion from an expansion to an interval adds components from the largest down and stops at the first one
    below an ulp (widening by `nextafter`).
- **Homogeneous exact points** (exact_geometry.h, vechg):
  - `vec3HE` = (x, y, z, w) as `expansion_nt`. No gcd and no canonical form; equality and order by a lexicographic
    rational compare (`vec3HgLexicoCompare`).
  - Interval differences of homogeneous points always go to a common denominator.
  - `orient_3d(vec3HE)`: interval filter on U = p1−p0 etc. (needs every `w` sign certain), then the exact det3x3 times
    the signs of the w. The comment says this exact det runs on the heap, not the stack: long expansions in RadialSort
    overflowed the stack.
  - Header comment: with the closed `geogramplus` `exact_nt`, the algorithm is "10x to 20x faster" and has "no risk of
    underflow / overflow". The exact points are then canonicalized (`vec3HExLexicoCompareCanonical`, "makes the global
    vertex map much much faster").
- **Expansion engineering** (multi_precision.h/.cpp, expansion_nt.h):
  - `new_expansion_on_stack` uses `alloca`, up to 1024 components (256 on Apple). Larger ones and all `expansion_nt`
    values live on the heap (malloc, reference counted).
  - Under `GEO_HAS_BIG_STACK` (Linux) there are template specializations of det / dot / mix / `triangle_normal` that
    keep the intermediate values on the stack.
  - `optimize()` = Shewchuk compress. It is called after each construction (`init_geometry`) and on the operands of
    incircle.
  - Products use balanced distillation when the shorter operand has at least 16 components, else a linear accumulation.
- **SOS:**
  - The variadic `PCK::SOS(compare, p_i, lambda_i...)` sorts the points with a comparator and returns the first
    non-zero cofactor sign.
  - `orient_3d_SOS_impl` (predicates.h 807) is a static table of the first 15 terms of the eps^(2^k)-perturbed 4x4
    determinant. The terms are orient_2d / orient_1d minors; the term in eps^84 is a constant, so the table stops
    there. The comment dates it to a discussion with Marc Alexa, January 2026.
  - `SOS_LEXICO` mode (perturb by lexicographic coordinate order rather than by address) is switched on during the mesh
    intersection, "to get compatible triangulations on coplanar faces".
- **incircle with cached approximate lifts** (exact_geometry.cpp; mesh_surface_intersection_internal):
  - The l_i = (x² + y²) / w² are given doubles, computed once per vertex: it is a regular triangulation, not an exact
    Delaunay one.
  - So `CDTBase2d::exact_incircle_ = false` and the CDT adds convexity checks.
  - The comment claims 20-25 % for caching the lifts.

#### 3. Ideas that differ from ours

| idea | gain (upper bound) | evidence | effort / risk | exact code? | recommendation |
|---|---|---|---|---|---|
| Semi-static (FPG) filter for `Orient3d` / `InCircle` on input doubles, instead of the per-call permanent bound | The filter is about 10 % of samples where it is hot. At most about 5 % if the cheaper bound halves it; the SIMD spike on the same filter got about 2 % | Our measured 7 ns filter and spike numbers; the FPG form is assumed from the paper, not read in geogram | S / low; the results would not change (the exact stage decides) | yes (`ExactPredicates`) | drop (< 10 %) |
| Interval arithmetic with upward rounding | 0: .NET has no rounding-mode control, and geogram kept RN as not significantly faster | interval_nt.h comment | – | – | drop |
| Expansions on the stack via alloca, plus compress after each construction | We already work without allocation: P2 monomials, `ExactLimbSigns` and `FixedArith` on stack limbs; points in fixed width | Our numbers above (P2 5077 -> 202 ns; 55928 −12 % from limbs) | – | – | we already do this (differently) |
| Expansion-based homogeneous points `vec3HE` | Negative: geogram's own comment says its expansion points are 10-20x slower than a canonical exact type. Ours are grid integers with gcd, canonical and fixed width (the `geogramplus` design) | exact_geometry.h comment; FIXED_WIDTH_EXACT_POINTS.md (−8..−26 %) | – | – | drop; we are on the faster design |
| SOS (lexicographic, the Alexa table for `orient_3d_SOS`) | No speed gain. Quality: none for the boolean (our arrangement treats degeneracies exactly and merges coplanar triangles by construction, so it needs no perturbation). Could serve a future Delaunay-3D / tet mesher | predicates.h 807 table | S / low | if added: `ExactPredicates` | drop for the boolean; note for a tet mesher (the table is a 15-row port, BSD-3) |
| Approximate cached lifts in incircle (regular instead of Delaunay) | `arr.incircle` exact stages are 0.01-0.17 % of calls on real inputs; on n-ary 31 % exact. Not bit-identical (another triangulation) | EXACT_BOOLEAN_PERFORMANCE.md filter-failure table | M / changes results and needs convexity checks | yes | drop |
| Lazy interval filter on constructed points (filter first, construct only on failure) | – | We already do it, and more lazily: geogram builds every intersection point exactly up front (`compute_geometry` -> `plane_line_intersection` in expansions, then `optimize()`); we build only when the filter fails | – | – | we already do this |
| Exact Orient3d stage on input points | After kernel B about 2 % of the serial time on `cgal`, so a further gain is at most about 2 % | CHEAPER_CONSTRUCTIONS.md §12 | – | – | drop; geogram's exact stage is plain expansions with no integer fast path, so it has nothing to add here |

**Upper bound for the whole area:** all exact work is 5-6 % self time on the normal stratum and point construction
2.4-4.9 % (one thread), so nothing from geogram's numerics can reach the 10 % bar on normal booleans. Only the n-ary
grid (41 % exact work, measured before #223 / #256) and the BigInteger tail (55928, #711 class) have more. On those,
geogram has nothing beyond what we have built (limbs, P2) or have open (candidate D, L3).

#### 4. Where we are equal or better

- FMA is banned in the exact code (geogram: `GEO_FP_CONTRACT_OFF`).
- Predicate statistics: our `G3PredicateStats` build flag against geogram's `PCK_STATS` define.
- RN intervals: ours widen by 1 ulp per operation (`ExactInterval`), geogram's by (1 ± eps); the same idea.
- No underflow or overflow: integer grid points against expansions that can under- or overflow (geogram's own
  caveat).
- Canonical points, so a key compares as equality (FixedKey against geogram's lexicographic rational compare in a
  `std::map`).
- The filter underflow fix (#753) and kernel B: geogram's double filter path has no integer fast path, as far as the
  non-generated files show.
- Geogram's `dot_2d`, `aligned_3d` and `on_segment_3d` on `vec3HE` have no filter ("TODO: filter").

### (b) Rounding of near-coincident surfaces (RC4)

**Ours:**
- `ExactPoint3.ToVector3d`: a 64-bit quotient, then a single scaling; "nearly correctly rounded: error below 1 ulp".
- Then `MeshRoundingRepair` (SafeRounding): local edits checked exactly, a proven drift bound, a position search
  (docs/design/SAFE_ROUNDING.md sections 1 and 9).
- RC4 (docs/research/BOOLEAN_TRIAGE_2026-11.md): 9 of the 39 non-clean `ours` results (#1010, #1011, #993) end with
  `RoundingClean == false`. These are 1-ulp shifts or mirrors of CAD parts with large coplanar faces. iud p0005 starts
  with 17788 intersecting pairs and 129688 degenerate triangles. The cause is not diagnosed: budget, bound, or no
  local edit.

**Geogram** (mesh_surface_intersection*.cpp/.h, rounding and output parts only):
- Exact points live only during the run, in `std::map<ExactPoint,index_t>` plus a per-vertex pointer attribute.
- The output vertex is `PCK::approximate(p)` = `x.estimate()/w.estimate()` per coordinate. That is not correctly
  rounded: two estimates and a division. The source comment: "TODO: find a way of computing the round to nearest
  approximation".
- The prologue removes degenerate triangles and colocates exactly equal vertices. The epilogue merges original
  vertices that equal an intersection point exactly (issue #111).
- No repair after the rounding:
  - `remove_fins()`, whose purpose is the degenerate pairs that "may appear due to snap rounding issues", is
    documented as "Not implemented yet";
  - `mesh_boolean_operation` ends with `simplify_coplanar_facets` (an angle tolerance, default 0), which is not a
    rounding repair;
  - `mesh_remove_intersections(M, max_iter)` re-intersects the rounded mesh and runs `mesh_repair` up to max_iter
    times. Its documentation says "Attempts to"; it has no bound and no convergence guarantee.

**Verdict:** geogram has nothing that solves RC4. It does not repair what the rounding of intersection points causes at
all: it outputs `approximate()` coordinates and leaves the defects. Its rounding (estimate / estimate) is no better than
ours (< 1 ulp). Its only remedy, re-intersect and repair (`mesh_remove_intersections`), is a heuristic without a
guarantee. Our SafeRounding plus a fresh second repair in `MeshRobustRepair` already covers that idea, with a bound.
RC4 should follow the triage's own next step (the remaining-defect fields in the rows, then budget, bound or removal of
the slabs). Geogram gives no input to that.

### Geogram files read (path under src/lib/geogram, approx. lines)

- numerics/PCK.h 1-220 (all); PCK.cpp header only (1-40)
- numerics/interval_nt.h 1-891 (all, the intervalDummy part skimmed)
- numerics/exact_geometry.h 1-516 (all); numerics/exact_geometry.cpp 1-765 (all)
- numerics/multi_precision.h 1-40, 230-400, 1085-1235, grep of 1240-1690 (macro list); numerics/multi_precision.cpp
  1-70, 270-310, 690-800, 1097-1103, grep of the function list
- numerics/expansion_nt.h: header and grep only (allocation / optimize lines); expansion_nt.cpp header only
- numerics/predicates.h 1-40, 60-90 (SOSMode), 780-880; numerics/predicates.cpp 1-40, 1245-1290, 1841-1890
- numerics/predicates/orient3d.h 1-20, orient3d.pck 1-30, side3h.h 1-15. These carry no Inria notice ("Automatically
  generated code" / no header): I opened them only to check the header and read no further. Their filter form is not
  verified.
- mesh/mesh_surface_intersection.h 1-40, 95-110, 560-590, 1300-1315 and grep; mesh/mesh_surface_intersection.cpp 1-40,
  224-275, 640-700, 805-845, 2270-2317 and grep
- mesh/mesh_surface_intersection_internal.cpp 1-40, 150-290; mesh/mesh_surface_intersection_internal.h 1-40, 225-260

## 2. 2D mesher (CDT_2d, LFS)

Geogram commit 16440c45. Clean-room note: only the Inria BSD-3 files listed at the end were opened; `delaunay/delaunay_triangle.*` and
`third_party/triangle` were not opened (their names appear only in a directory listing).

### (a) delaunay/CDT_2d.h/.cpp: constrained Delaunay with exact predicates and constraint crossings

#### 1. Our counterpart and its numbers

- Kernel: `comp_geom/DelaunayKernel2.cs` (Bowyer-Watson with ghost triangles, BRIO + Hilbert order, visibility walk, exact adaptive
  predicates on doubles, `CanonicalizeTies` for D18), `DelaunayKernel2.Constraints.cs` (Lawson insertion, `RecoverEdge` = Sloan flips,
  `RestoreDelaunay`), `DelaunayKernel2.Deletion.cs`.
- Noding and domain: `comp_geom/ConstrainedDelaunayMesh2.cs` (`InsertPiece` walk, `Cross`, `Intersection` exact in BigInteger and
  rounded to the nearest double, `NearVertex` / tolerance 2^-41 of the extent, O11/Q14, `RingParity`), `ConstrainedMesh2*.cs`.
- Snap: `comp_geom/SnapRounding2.cs` (Hobby SR + SRR restore, every decision in BigInteger, crossings as exact num/den).
- Numbers (all from docs, not re-measured):
  - Speed target "<= 2x the fork's mesher-only time" **closed by the owner 2026-11-16** at about 2.7-2.8x (cq 373600 q30) and 3.4x
    (q20); the SplitTriangle breakdown (#909) left 6-9 % without output change; BestCandidate is about 35 % of refinement
    (`docs/roadmap/meshing2d.md` lines 97-99, 334).
  - Snap path: once 5-20x slower than the reference (373600 q20-snap 5.5 s vs 0.64 s), half in `BelowFloor`; #328 gave 7.5-8.3 -> 4.4-4.7 s
    (`docs/roadmap/meshing2d.md` line 288). SR + restore prototype: restore at most 6 % of a run, routing is the cost; snapping at a
    data-sized pixel adds about 0.1 s to a 0.65 s mesh of water-huge (`docs/design/MESHING2D_SNAP.md` section 3f).
  - ALKIS sample (`benchmarks/Meshing2Benchmark/README.md`, 2026-11-12, one run each, JIT included): M2 (CDT + snap 0.01 m)
    80-575 ms per tile against 270-6640 ms for the M3 quality runs, i.e. the whole constrained phase is roughly 10-20 % of a
    quality run on these tiles. The share of segment recovery and of noding inside M2 is **not measured**.
- Open items served: `docs/roadmap/sources-surveys.md` line 137 ("2D mesher: snap speed, the exact kernel, constraint intersections");
  the speed item is closed (above), so a speed idea here must stand on its own.

#### 2. What geogram does (read)

- **Structure** (`CDTBase2d`, h 60-760, 1300-1330): flat arrays `T_` (3 vertices per triangle), `Tadj_` (3 neighbours), `v2T_`,
  `Tflags_` (uint8), per triangle-edge a linked list of constraint ids (`Tecnstr_first_`, `ecnstr_val_`, `ecnstr_next_`), so overlapping
  constraints are counted per edge and inside/outside goes by parity (`remove_external_triangles`, cpp 825-905). Three intrusive
  doubly linked triangle lists (S flip stack, Q intersected edges, N new edges) share `Tnext_/Tprev_` and use flag bits for O(1)
  membership; a triangle can be in one list at a time. Edges are always addressed as edge 0 of a triangle that is rotated in place
  (`Trot`), so no edge lookup by vertex pair is ever needed. An enclosing triangle or quad is required (no ghosts, cpp 122-155).
- **Points and predicates are virtual** (h 1035-1072): `orient2d`, `incircle`, `create_intersection` by vertex index. `CDT2d`: doubles,
  `PCK::orient_2d`, `PCK::in_circle_2d_SOS` (symbolic perturbation), crossings computed in doubles and rounded (cpp 1337-1380); the
  header says this path is not robust for crossing constraints (h 1360-1366). `ExactCDT2d`: homogeneous exact points `vec2h` with
  expansion coordinates (or geogramplus multiprecision), cpp 1534-1770.
- **Exact crossings** (`ExactCDT2d::create_intersection`, cpp 1717-1768): the point is computed from the *original* endpoints of the two
  input constraints (`constraints_[E]`), never from sub-pieces that are themselves crossings, so the expression degree does not grow;
  `mix(t, p_i, p_j)` with a rational t, then `optimize_number_representation`. The new vertex is inserted directly into the
  intersected constrained edge (`insert_vertex_in_edge`, which copies its constraint list to both halves), its star is
  re-Delaunayized, and the walk is redone from scratch (cpp 241-300).
- **Incircle with approximate lifting** (cpp 1706-1716, 1993-2017): without multiprecision, incircle uses the exact homogeneous x, y, w
  but a per-vertex squared length rounded to double (sum of the expansion components in `__float128`). This is exactly a regular
  (weighted) triangulation predicate, so flips still terminate, but the result is only approximately Delaunay; `exact_incircle_ =
  false` then adds an `is_convex_quad` guard before every Delaunay flip (cpp 658-742).
- **orient2d cache** (cpp 1663-1704): `std::map<trindex, Sign>` keyed by the sorted triple with permutation parity; during `locate` the
  results go to a buffer that is committed only if the point is not a duplicate. Worth it only because exact points are expensive.
- **Constraint insertion** (cpp 241-345, 400-656): Sloan 1993. `find_intersected_edges` walks from i towards j (`walk_constraint_v` around
  a vertex, `walk_constraint_t` across a triangle); it stops at a vertex exactly on [i,j] (and marks an embedded edge as constrained)
  or at a crossing with a constrained edge; the constraint is then processed piecewise (i = k loop). `constrain_edges` pops Q: if the
  quad is not strictly convex the triangle goes back to the front; else flip, and a 4-case rule from the combinatorics (is the neighbour
  in Q, which vertex is shared) plus **one** `orient2d(i,j,v0)` decides whether the new edge still crosses (re-queued) or is new (to N).
  `Delaunayize_new_edges` sweeps N until no flip (cpp 697-742). Loop guards: 10 nT iterations, then assert.
- **Location** (cpp 744-823): straight walk with a random start edge per triangle (`Numeric::random_int32`); duplicates detected by two
  zero orientations; on-edge points split the edge (4 triangles) or a border edge (2).
- Batch insertion: BRIO order (`compute_BRIO_order`), hint = triangle of the previous vertex (cpp 1382-1450).

#### 3. Ideas that differ from ours

| # | Idea | Expected gain | Evidence | Effort | Risk | Exact code? | Recommendation |
|---|---|---|---|---|---|---|---|
| A1 | Sloan recovery with rotated triangles and intrusive lists: no `FindEdge` per dequeued edge, one orient per flip instead of two for the "still crossing" test, no `Queue<long>`/`List<long>` allocations | Upper bound: segment recovery is a part of M2, and M2 is about 10-20 % of a quality run (ALKIS table, JIT included), so < 10 % on quality runs; on M2-only runs the share of recovery is **not measured** | geogram cpp 571-656 (read); our `RecoverEdge` (FindEdge per dequeue, 4 orients per flip) | S-M | low (recovery is local; golden rows could change through slot order) | no (comp_geom; but D18/golden layout) | **drop** for the quality mesher; **measure first** (a profile of the recovery share on the corpus M2 runs) only if an M2-only use case asks |
| A2 | Crossing computed from the original input segments, not from the current (already rounded) piece endpoints | Accuracy, not speed: today a piece may drift "a few units in the last place" off its input segment (class remarks) and the next crossing on it inherits that; from the sources the crossing is within 0.5 ulp per axis of the true one | geogram cpp 1726-1735 (comment: "much better ... simpler coordinates"); our `Cross` uses `k.Point(u..l)` | S | medium: the rounded point of the true crossing may lie on the other side of the current pieces; our re-insertion as a free point plus `NearVertex` handles any point, but the O11 tolerance argument must be re-checked; with several sources per piece the choice of source is not unique | no | **measure first**: count drift (max distance of piece vertices from their sources) on the M2 fuzz/corpus; adopt only if it is above a few ulps anywhere |
| A3 | Keep crossings as exact homogeneous points (vec2HE) and round only at output | Robustness of an exact CDT, but D1 asks for double output valid on its own coordinates; rounding at the end is the snap-rounding problem we already solve with SnapRounding2 / tolerance | geogram h 1531-1543, cpp 1717-1768 | L | high | would add exact code | **drop** (contradicts D1; ours already noded exactly + rounded + tolerance) |
| A4 | Approximate lifting incircle (exact x,y,w, double squared length) + convexity guard | Only useful for implicit/homogeneous points; our 2D points are doubles and Shewchuk's adaptive incircle is exact and cheap | geogram cpp 1706-1716 | - | - | - | **drop** for the 2D mesher (a note for the 3D exact pipeline's coplanar CDT, outside this area) |
| A5 | orient2d result cache (map by sorted triple) | Only for expensive predicates | cpp 1663-1704 | - | - | - | **drop** |
| A6 | Snap speed: geogram has no snap rounding and no refinement; its lesson for `SnapRounding2` is arithmetic: expansions / a double filter instead of BigInteger for every orient and crossing | Upper bound: SR + restore of the input is about 0.1 s of a 0.65 s mesh (prototype, SNAP 3f) and the snap runs' time is now in refinement on the grid (BestCandidate 35 % of refinement); a filter on the input part is therefore < 15 % of an M2 snap run and smaller on quality runs; library breakdown **not measured** | SNAP doc 3f; roadmap line 288, 334; `SnapRounding2.AddCrossings` (BigInteger `Orient` without a double filter: the double `ExactPredicates.Orient2d` on the input points gives the same sign, since X, Y are the coordinates times a power of two) | S | low | no | **measure first** (time `SnapRounding2.Apply` alone on the Herne / ALKIS snap runs); pursue only if >= 10 % of a run |

#### 4. Where we do equally or better

- Exact predicates on doubles with a deterministic tie rule (D18 fan from the smallest representative) vs geogram's SoS incircle: same
  purpose (an order-independent result); ours also has a deterministic walk (geogram's random start edge is harmless for the result).
- Ghost triangles instead of an enclosing triangle/quad: no invented coordinates (geogram needs `create_enclosing_*`).
- Double-coordinate crossings: geogram's `CDT2d` rounds crossings in plain double arithmetic and warns it is not robust; ours computes
  them exactly, rounds correctly (`RatioToDouble`, ties to even), merges within the O11 tolerance (`NearVertex`) and re-routes the
  pieces, which the M2 fuzz and the reference run (4854 VALID of 4869, `docs/roadmap/meshing2d.md` line 286) back.
- Walk stops at exactly collinear vertices and splits the constraint there: same in both (`InsertPiece` ov == 0 / `Ahead`).
- Overlaps and parity: geogram's per-edge constraint list vs our per-constraint `Sources` and `RingParity` (domain by ring parity with
  nesting roles, D5): ours is richer.
- Snap rounding with restore and grid-aware refinement have no counterpart in geogram.

### (b) delaunay/LFS.h/.cpp: local feature size

#### 1. Our counterpart and its numbers

- `comp_geom/QualityMesh2.cs` (Ruppert / off-centres, grading steps 1-2: worst first, `BestCandidate`, petal candidates of
  Erten-Üngör 2007). No LFS code exists in the repo (grep: no `lfs`/`LocalFeatureSize` in any `.cs` of the mesher).
- `docs/design/MESHING2D_GRADING.md`: before grading f-11/a 256, f-11/b 456, f-40 (33.75°) 276 triangles against the reference's 108 /
  205 / 130 (2-2.5x); after step 2 f-11/a 215, f-11/b 363, f-40 184 (`docs/roadmap/meshing2d.md` line 381), i.e. 1.99x / 1.77x / 1.42x;
  target 1.25x, owner stop 2026-11-04. Diagnosis: about 5 vertices per octave against the reference's 2.7, median smallest angle 31°
  at a 20° bound; "closing it needs a different placement strategy".
- At scale the gap is small: Herne 373600 q20-snap 62 303 triangles after lenses vs reference 57 890 (`docs/roadmap/meshing2d.md`
  line 290, 1.08x).
- Open item: "Further options ... (2) an explicit size function h = c·LFS with a growth parameter (`delaunay/LFS` exists ...)"
  (`docs/roadmap/meshing2d.md` line 393); "Yardstick for 'fewer': an LFS-based lower bound".

#### 2. What geogram does (read, whole files)

- `LocalFeatureSize` is **3D only and for smooth surfaces**: a 3D Delaunay ("PDEL") of surface samples; tetrahedron circumcentres;
  slivers (any dihedral angle within 0.01° of 0 or 180°) skipped; per sample the positive pole = farthest circumcentre of its incident
  finite tets (or, for an unbounded Voronoi cell, the average outer facet normal as a direction); negative pole = the circumcentre with
  the largest projection on the opposite direction; all poles go into a nearest-neighbour Delaunay ("NN"); `squared_lfs(p)` = squared
  distance from p to the nearest pole (Amenta-Bern pole approximation of the medial axis). No 2D variant, no segments, no corners.

#### 3. Ideas and assessment

| # | Idea | Expected gain | Evidence | Effort | Risk | Exact code? | Recommendation |
|---|---|---|---|---|---|---|---|
| B1 | Use geogram's LFS for a size function | None directly: it is 3D, for samples of a smooth surface. Its 2D analogue (poles of a 2D Delaunay of boundary samples) approximates the medial-axis lfs, which tends to 0 at every polygon corner (the medial axis reaches convex vertices), so h = c·lfs would refine without end at corners; PSLG meshing needs Ruppert's lfs (radius of the smallest disc at p meeting two non-incident features) instead | LFS.h 40-125, LFS.cpp 170-342 (read); Ruppert 1995 | - | - | - | **drop**; correct the roadmap's "`delaunay/LFS` exists" (it does not serve option 2) |
| B2 | Ruppert lfs (from the CDT of the input) as a size *upper* bound h = c·lfs | Does not reduce triangles: a size bound only adds refinement; angle refinement already grades with lfs. Our excess is density at a given lfs (5 vs 2.7 vertices per octave), which a size cap does not touch | GRADING.md diagnosis | M | low | no | **drop** as a count lever |
| B3 | Ruppert lfs as a yardstick and as a placement guide (reject or move candidates closer than c·lfs to existing vertices, i.e. a lower bound on spacing in `BestCandidate`) | Upper bound on the gain: the reference gap itself, f-11/a at most −50 % (215 -> 108), f-11/b −44 %, f-40 −29 %; on real data at most about −7 % (Herne 62 303 -> 57 890), and the 1.25x target asks for less. Measured gain: none (not built). Step 3's levers moved small cases ±15 % chaotically (GRADING.md), so the realised gain is likely well below the bound | our docs; the lfs lower bound ∫ 1/lfs² (Ruppert's size-optimality) is the yardstick the roadmap asks for | M (lfs from the input CDT: per input vertex/segment the nearest non-incident feature, interpolated) | medium: interacts with the angle guarantee and termination (a rejected insertion must not stall a bad triangle) | no | **measure first, yardstick only**: compute ∫ 1/lfs² on f-11/a, f-11/b, f-40 and a Herne tile to see how far both meshers are from the bound; a placement change belongs to the "different placement strategy" / coarsening project (option 1), not to geogram |

#### 4. Equal or better

- Our grading already uses literature placement (off-centres, Erten-Üngör petals); geogram has no 2D quality mesher of its own in the
  files read (its 2D quality meshing goes through Triangle, not read).

### Geogram files read (for the provenance log)

All under `src/lib/geogram/delaunay/`, commit 16440c45c6ec5015b50f4a430c5ea1a300c2542f, Inria BSD-3 header checked (lines 1-40) before reading:

- `CDT_2d.h` (1751 lines): read 1-800 (header, `CDTBase2d` public API, flags, `DList`, insertion/constraint declarations), 1000-1100
  (`locate`, `is_convex_quad`, virtual predicates, `create_intersection`, `find_3`), 1300-1380 (naive variants, data members, `CDT2d`
  doc comment), 1520-1751 (`ExactCDT2d`); 800-1000 and 1100-1300, 1380-1520: only declaration lines seen through a grep outline.
- `CDT_2d.cpp` (2019 lines): read 1-1160 (licence, Sloan note, ctor/clear, enclosing triangle/quad, `insert`, `insert_constraint`,
  `Delaunayize_vertex_neighbors`, constraint walker, `constrain_edges`, `Delaunayize_new_edges`, `locate`, `remove_external_triangles`,
  `remove_marked_triangles`, `insert_vertex_in_edge/triangle`, `swap_edge`, `is_convex_quad`, `Tedge_is_Delaunay`, start of
  `check_edge_intersections`), 1309-1452 (`CDT2d` predicates, `create_intersection`, batch `insert`), 1520-1770 (the last lines of `CDT2d::save`, then `ExactCDT2d` ctor, insert, transactions, `odd_order`, cached `orient2d`, `incircle`, `create_intersection`), 1990-2019
  (`squared_length`). Not read: 1160-1309 (debug and naive variants), 1453-1519 (body of `CDT2d::save`), 1770-1990 (`classify_triangles`, 2D CSG,
  `save`); seen only as a grep outline of function signatures.
- `LFS.h` (125 lines): all. `LFS.cpp` (342 lines): all.
- Directory listing of `delaunay/` only (file names); no other geogram file opened. `delaunay_triangle.*` and `third_party/triangle`
  not opened.

## 3. 3D Delaunay / tet meshing (delaunay_3d, parallel_delaunay_3d) and point clouds (Co3Ne)

### (a) 3D Delaunay: geogram `Delaunay3d` / `ParallelDelaunay3d` vs our `Delaunay3`

#### 1. Our counterpart

- `comp_geom/Delaunay3.cs` (913 lines): incremental Bowyer-Watson inside a fixed box (8 corners, no infinite vertex),
  exact `ExactPredicates.Orient3d` / `InSphere` (float filter + exact stage), stochastic visibility walk (`Locate`, random
  start facet, exact filtered Orient3d), cavity by BFS over strict InSphere, then a second pass that grows the cavity until
  it is star-shaped (cospherical inputs, no SoS), a check that no vertex is swallowed, `minSeparation`, new cells linked by a
  `Dictionary<long,int>` edge map. Extras geogram does not have: `RefinementSupport` (labels, incident cells, `IsEdge` /
  `IsFacet`, `UndoLastInsert`), `Clone`, and round insertion `PlanInsert` / `AssignInsertIds` / `ApplyInsert`
  (deterministic parallel carving, #774). Not in the exact-code list (`.editorconfig` lists only `math/Exact*` etc.).
- Users: `MeshAlphaWrap` (carving), `MeshTetrahedralization` (stage 1, #775), `TetMeshImprover` (stage 2, #821),
  `benchmarks/RestrictedDelaunaySpike`. `TetMesh3`, `TetMeshQuality` hold / measure the output.
- Numbers:
  - `benchmarks/TetMeshBenchmark/README.md` (2026-11-09, 1 thread): grid-sorted (32^3 cell order) 12.6-13.1 us/point,
    1.9 us/cell, constant 20k-400k; random order 25.9 (20k) / 35.1 (100k) / 67.7 us/point (400k).
  - `docs/roadmap/alpha-wrap-and-repair.md` (L2 probe): `Insert` = cavity search 45 % (about 38 filtered InSphere per
    insertion, no exact fallback), edge linking 16 %, new cells 13 %, star check 11 %, vertex checks 10 %, location 4 %;
    per-vertex chains instead of the dictionary saved 15 % of the linking, "<= about 1.02x".
  - Share in the pipeline: Delaunay insertion 13 % (12.2 % in another run) of the alpha-wrap pipeline on the 189 meshes
    over budget (`docs/design/PARTITIONED_REDUCTION_HANDOVER.md`, `alpha-wrap-and-repair.md` lines 38/52/73).
  - Tet meshing: 40/40 valid, refinement median 0.13 s, 26 us per tet, median 4.6k tets (`docs/design/TET_MESHING.md`
    14.8). Meshes are small; Delaunay speed is not the gap there (dihedral quality is).
- Open items served: research.md item 3 (tet meshing; stage 1/2 done, dihedral target), Phase 5 #4 (constrained Delaunay
  tetrahedrization: not started, went the wrap route), the alpha-wrap tail (insertion 13 %).

#### 2. Geogram (read)

- **Data structure** (`delaunay_3d.h`): flat `cell_to_v` / `cell_to_cell` arrays (4 per tet), a `cell_next` linked list
  that serves as conflict list, free list and "in list" flag at once; one stamp array for "visited, not in conflict"
  (stamp = vertex index, no clearing). An infinite vertex with virtual tets (convex hull, no bounding box). Batch API only
  (`Delaunay::set_vertices`, `delaunay.h`): no public incremental insert, no removal, no undo; the regular (weighted)
  triangulation is the same code with lifted points (`orient_3dlifted_SOS`).
- **Predicates**: `PCK::in_sphere_3d_SOS` / `orient_3d` with symbolic perturbation, so the conflict region is always
  star-shaped and the triangulation is unique for a given vertex numbering (no star-growing pass, no "vertex swallowed"
  check). Virtual tets: conflict = orient3d with the hull facet, ties resolved by the real neighbour.
- **Order** (`mesh_reorder.cpp`, `compute_BRIO_order`): random shuffle, then recursively the first 1/8 (ratio 0.125,
  threshold 64) is a lower BRIO level; each level Hilbert-sorted by median splits (`std::nth_element`, CGAL-style
  Delage / Devillers spatial sort). The levels are returned for the parallel version. Hint = a new cell of the last insertion.
- **Walk**: `locate_inexact` (plain double orient3d, no random, at most 2500 steps; "structural filtering", Funke et al.;
  the code comment says it gains "a few percent"), then the exact walk from there with a random start facet; the walk
  returns the four orientation signs, used to detect duplicates (3 zeros) and to seed the cavity across zero facets.
- **Cavity cache** (`cavity.h`): the boundary facets are recorded during the conflict BFS (no second pass). A fixed table
  of 1033 slots keyed by oriented edge (v1, v2) -> local facet id (uint8, at most 128 facets), open addressing, memset on
  clear. `stellate_cavity` makes one new tet per boundary facet, then sets the three inner neighbours by looking up the
  reversed edges. On overflow (>128 facets or a full table) it falls back to a recursive (de-recursified)
  traversal along the cavity border (`stellate_conflict_zone_iterative`).
- **Parallel** (`parallel_delaunay_3d.cpp`, `delaunay_sync.h`): no geometric partition. BRIO levels; the first levels up
  to 1000 points serial; each later level's Hilbert-sorted range is cut into one contiguous chunk per thread (spatially
  coherent chunks). Per-tet atomic owner byte (`CellStatusArray`, CAS, up to 126 threads); a thread locks every tet it
  walks through, the cavity and its neighbours; on contention it releases all and rolls back. Interference rule: the
  lower thread id waits for an event of the interfering thread; the higher one reverses direction (inserts from the other
  end of its chunk). Fixed per-thread tet pools (7n / threads); on overflow the rest is inserted serially afterwards.
  Thread-local LCG random numbers. Final pass compacts the cell arrays in index order.
- **Determinism**: the cell ids and in-cell vertex order depend on thread timing (pool, rollback and direction
  decisions); there is no canonical renumbering. With SoS the tet set itself should be order-independent (inferred from
  the SoS predicate; not verified, PCK not read in this section). So parallel != serial as indexed output.
- **Speed**: no number in the files read. Geogram vs ours: not measured, and not comparable without a run on one machine
  (C++ vs C#). Our 13 us/point is about 10x the 1-2 us/point planning figure in TET_MESHING.md section 4 (that figure is
  itself an assumption).

#### 3. Ideas

| Idea | Expected gain (upper bound) | Evidence | Effort | Risk | Exact code | Rec. |
|---|---|---|---|---|---|---|
| A1. Cavity cache (fixed oriented-edge table) instead of `Dictionary` linking | linking is 16 % of `Insert`; Insert is 13 % of the wrap pipeline -> <= ~2 % pipeline; per-vertex chains already got only 15 % of the 16 % | our L2 probe (measured); geogram `cavity.h` | S | low, bit-identical possible | no | drop (below 10 %) |
| A2. Skip the star-growth pass and the swallowed-vertex check when no InSphere / Orient3d returned 0 in this insertion (general position: strict conflict region is star-shaped, no vertex removed) and collect boundary facets in the BFS | star 11 % + vertex checks 10 % of Insert -> <= ~21 % of Insert, <= ~2.7 % of the wrap pipeline; >= 10 % only for a Delaunay-bound caller | L2 split (measured); geogram has neither pass (SoS) | S | low, result unchanged if the argument holds; needs a proof note and the degenerate tests | no | measure first, only if a Delaunay-bound caller appears |
| A3. BRIO + Hilbert batch order for bulk insertion | random -> coherent is 5x at 400k (measured); BRIO over our grid sort: at most the location share (4 % in the wrap; not measured in the benchmark) | TetMeshBenchmark README; geogram `compute_BRIO_order` | S (reuse `DelaunayKernel2`'s BRIO/Hilbert, hash instead of random shuffle) | none | no | adopt only with a batch caller (eg S1-F "fresh Delaunay3 of the surface vertices", RDR spike); the wrap inserts in queue order, so no gain there |
| A4. Inexact pre-walk (structural filtering) | location is 4 % of Insert; geogram's own comment: "a few percent" | L2 split; geogram comment | S | none | no | drop |
| A5. SoS InSphere / Orient3d (unique triangulation, order-independent) | no speed claim; removes the star pass by construction, makes results independent of insertion order (cospherical lattices, box corners) | geogram design | M | changes bits on degenerate inputs; new perturbed predicate needs the exact-code review | yes (`ExactPredicates`) | drop for now; note as the prerequisite of A6 |
| A6. Optimistic lock-based parallel insertion (geogram scheme) | for bulk triangulation only; our parallel need is the wrap's carving, which `PlanInsert` rounds already cover deterministically (main carving 1.53x on 4 cores, #774) | geogram code; alpha-wrap-and-repair.md | L | not deterministic without SoS + canonical renumbering (sort cells by sorted vertex tuple); breaks parallel == serial | no (Delaunay3 not exact-listed), but A5 needed | drop |
| A7. Weighted (regular) triangulation via lifting | quality, not speed: the protecting-ball option of TET_MESHING.md (lines 83, 545) | geogram `RegularWeightedDelaunay3d` | M | new power predicate + bound review | yes | reference only if the weighted option is chosen |

#### 4. Already equal or better

- Exactness: both exact; ours without SoS but with an explicit star repair, exactly positive cells.
- Incremental API, undo, labels, `IsFacet`, `Clone`: needed by refinement and the wrap, absent in geogram's Delaunay3d.
- Deterministic parallel insertion (rounds, same result for every thread count): geogram's parallel version is not.
- Stochastic walk with hints: same method (Devillers, Pion, Teillaud 2002).

### (b) Point clouds: geogram Co3Ne / colocate / kd-tree vs our point-cloud code

#### 1. Our counterpart

- `mesh_ops/PointCloudNormals.cs` (538 lines): PCA normals (k = 16, clamp 8-256), orientation by an alpha wrap (two
  probes per point, exact inside test), confidence report, Alpha escalation, exact-duplicate merge, power-of-two frame,
  parallel == serial. Internal `PointTree`: exact kNN k-d tree, median split on the widest axis, leaves of 16, ties by index.
- `mesh_ops/PointCloudSurface.cs` (296 lines, step C1, #580): fast winding number of the oriented points contoured by
  marching cubes on a band, the wrap as bound and as fallback in holes; `MeshAlphaWrap` on points (step A, #566); XYZ
  reader / writer (#571).
- Numbers (`docs/design/POINT_CLOUD_RECONSTRUCTION.md`): wrap on 5k-20k torus points 19-1634 ms, 100 % closed (section 4);
  C1 on 6000 points: mean distance 0.0073 (clean) .. 0.036 (noise+hole+nonuniform) vs wrap 0.0096 .. 0.146; 6-8 s on 4
  cores for 1-1.5 M grid nodes (section 8).
- Open items: research.md item 6 / "Later": screened Poisson (#887, closed as not planned until a scan use case and a
  measured gap of C1); C1's open points: no enclosure guarantee, features rounded to the cube size, time.

#### 2. Geogram (read)

- **Co3Ne** (`co3ne.cpp`, "Concurrent Co-Cones", interpolating: the output vertices are the input points):
  1. Normals: PCA of the k nearest neighbours (`PrincipalAxes3d`); optional orientation by propagation over the kNN graph
     with a priority queue on |cos| (most parallel neighbours first; Hoppe-like, no MST). Optional smoothing: project each
     point to its neighbours' plane, iterated.
  2. Local restricted Voronoi cell (RVC) per point i: a disk of radius r in i's tangent plane (a polygon from a sin/cos
     table), clipped by the bisector planes of its nearest neighbours (20 first, growing by 1/3 up to 1000), in floating
     point; stop when the next neighbour is farther than 2r or than twice the current cell radius (security radius).
     Each clipped polygon vertex remembers the neighbour whose bisector made it; consecutive vertices made by j and k
     give a candidate triangle (i, j, k), ie a dual Delaunay triangle of the local tangent-plane RVD. Per-thread lists,
     points split into index ranges.
  3. Reconciliation: sort each triple, sort lexicographically; a triangle produced by all three of its vertices ("T3")
     is good, by one or two ("T12") is a candidate.
  4. Manifold extraction (`Co3NeManifoldExtraction`): build the T3 mesh, remove triangles on non-manifold edges and
     Moebius configurations (`mesh_reorient` with a removal list), then optionally add T12 triangles one by one, each
     accepted only if: its edges stay manifold; the normals of the adjacent triangles agree (`co3ne:max_N_angle`); it has
     at least two neighbours (one only in strict mode, and then only if the opposite vertex is isolated); no vertex becomes
     non-manifold; the orientation is consistent (component flip by BFS if needed). Repeated until nothing changes.
  5. `mesh_reorient`, optional `mesh_repair` (MESH_REPAIR_RECONSTRUCT: hole filling etc.). No closedness guarantee, no
     self-intersection check, not exact.
- **colocate** (`colocate.h/.cpp`): tolerance colocation by kNN queries in a `parallel_for`, plus an exact version by
  lexicographic sort.
- **kd-tree** (`kd_tree.h/.cpp`, `nn_search.cpp`): `BalancedKdTree` ("BNN": median by `nth_element` on the coordinate of
  largest spread, leaves of 16, top three levels built in parallel) and `AdaptiveKdTree` ("CNN"), behind a factory.

#### 3. Ideas

| Idea | Expected gain | Evidence | Effort | Risk | Exact code | Rec. |
|---|---|---|---|---|---|---|
| B1. Co3Ne as an interpolating reconstruction (vertices = samples) next to C1 | quality: vertex error 0 on clean dense scans and no grid resolution limit (C1 is bound to the cube size); speed: O(n k) local work with no grid, so likely well below C1's 6-8 s (assumed, not measured). Loses what C1 has: closed output, wrap fallback in holes | geogram code; C1 numbers in POINT_CLOUD_RECONSTRUCTION.md | M (kNN, PCA, orientation exist: our wrap-based orientation is stronger than geogram's propagation; new: disk-RVC clipping, T3 vote, incremental manifold extraction, then our hole filling / `MeshRobustRepair` and the `NotCleanReason` check) | open / non-manifold output on noisy or thin data; self-intersections possible across thin walls; floating-point clipping (2^k invariance holds in a power-of-two frame, test needed) | no | measure first, and only when the point-cloud item is reopened (#887 condition: a scan use case): a benchmark prototype on the five torus scenes against C1 (mean / max distance, closed, clean, time) |
| B2. Geogram's kNN graph orientation | none: our wrap-based orientation gives 97-100 % with a confidence report where propagation has no such check | design note section 4 | - | - | no | drop |
| B3. Balanced kd-tree / tolerance colocate | we already have the same tree (median, widest axis, leaf 16) with exact ties; parallel build of the top levels would only matter if tree build were a hotspot (not measured) | `PointCloudNormals.PointTree` | S | none | no | drop (build parallelism: not measured, no reason seen) |

#### 4. Already equal or better

- Normal orientation with a guarantee check (wrap probes, confidence, escalation) instead of graph propagation.
- A closed, clean result always available (wrap; C1 bounded by the wrap); Co3Ne gives none.
- kNN tree equivalent, with deterministic ties; parallel == serial throughout.

### Geogram files read (commit 16440c45; all headers Inria BSD-3, checked with `head -40`)

- `src/lib/geogram/delaunay/delaunay_3d.h`: 1-140, 930-1140 (+ grep of member signatures)
- `src/lib/geogram/delaunay/delaunay_3d.cpp`: 1-60, 95-125, 141-240, 394-940
- `src/lib/geogram/delaunay/cavity.h`: 1-341 (whole file)
- `src/lib/geogram/delaunay/parallel_delaunay_3d.h`: 1-173
- `src/lib/geogram/delaunay/parallel_delaunay_3d.cpp`: 1-115, 285-360, 605-830, 2635-3004 (+ grep for locks / rollbacks)
- `src/lib/geogram/delaunay/delaunay_sync.h`: 1-40, grep of 45-143
- `src/lib/geogram/delaunay/delaunay.h`: 1-40, grep of the virtual API (190-775)
- `src/lib/geogram/mesh/mesh_reorder.h`: 1-40, 155-190; `mesh_reorder.cpp`: 1-82, 1105-1160, 1287-1320
- `src/lib/geogram/points/co3ne.h`: 1-113; `co3ne.cpp`: 1-40, 100-168, 233-410, 1182-1232, 1505-1570, 1700-1860, 2094-2150, 2193-2350, 2451-2600
- `src/lib/geogram/points/colocate.h`: 1-101; `colocate.cpp`: 1-40 + grep
- `src/lib/geogram/points/kd_tree.h`, `kd_tree.cpp`, `nn_search.h`, `nn_search.cpp`: headers + grep (split, leaf size, factory 120-145)
- Not opened: `delaunay/delaunay.cpp` (registers the TetGen factory under `GEOGRAM_WITH_TETGEN`; only a grep for "tetgen"
  was run on it), `delaunay_tetgen.*`, `mesh/mesh_tetrahedralize.*`, `numerics/PCK` (SoS predicates; behaviour inferred
  from the call sites only), all third_party code.

## 4. Remeshing, decimation, smoothing (mesh_remesh / CVT, mesh_decimate, mesh_smoothing)

Geogram commit 16440c45. Every geogram file read carries the Inria BSD-3 header (checked with `head -40`). No
third-party code opened (HLBFGS and OpenNL are called by the files below and only named here).

### (a) CVT remeshing (`remesh_smooth`) vs Remesher / RemesherPro / RDR

#### 1. Our counterpart and its numbers

- `mesh/Remesher.cs`, `mesh/RemesherPro.cs`: Botsch-Kobbelt incremental remeshing (split, collapse, flip, tangential
  smoothing `SmoothTypes` Uniform / Cotan / MeanValue, projection onto the input via `MeshProjectionTarget`),
  `MeshConstraints` for creases and boundary, `MinTriangleQuality`, `RejectInvertingPasses`, `PreventSelfIntersections`,
  `TargetEdgeLengthF` (`mesh_ops/RemeshSizing.cs`: `FromWallThickness` only). Also `RegionRemesher`, `EdgeLoopRemesher`.
- Restricted Delaunay refinement: `docs/design/CERTIFIED_REMESHING.md` (study, stage 0 proofs done, nothing built),
  spike `benchmarks/RestrictedDelaunaySpike` (on `comp_geom/Delaunay3.cs`).
- Numbers:
  - `docs/design/REDUCER_REMESHER_20.md` section 2: the Remesher takes 23 / 11 µs per input triangle (x1 / x2 mean
    edge), 282 / 121 µs with `PreventSelfIntersections`. p01 min angle: 1.3° / 0° on Thingi10K, 40° / 42° on the
    procedural set. Degenerate ("not clean"): 17 / 18 of 20. Topology changed: 0. Bit-identical repeat / serial.
    Section 11: `MinTriangleQuality = 0.2` gives p01 5.65°, 0 degenerate on 20 of 20, at +40 % time.
  - `benchmarks/RemeshBenchmark/README.md`: Remesher sphere_refine 0.597 s, 24446 tris, q min 0.638, Hausdorff/target
    0.04; bumpy_uniform 2.002 s, 74750 tris, Hausdorff/target 0.043. RemesherPro is faster on smooth procedural
    inputs and slower on Thingi10K (median speedup 0.67x / 0.81x).
  - `benchmarks/RestrictedDelaunaySpike/README.md`: RDR 193-265 µs per output vertex at diag/120, min angle >= 30°,
    0 uncertified on 12 smooth cases, fails on creases sharper than about 45-60°.
- Open roadmap items served (`docs/roadmap/alpha-wrap-and-repair.md`, Phase 2): degenerate triangles (the quality
  option is opt-in), self-intersections (opt-in, 6-10x), crease slivers (Wang et al. 2019 feature-curve operators,
  not scheduled), certified remeshing stage 1b.

#### 2. Geogram: what `remesh_smooth` does

Files: `mesh/mesh_remesh.{h,cpp}`, `voronoi/CVT.{h,cpp}`, plus parts of `voronoi/RVD.cpp`, `voronoi/generic_RVD.h`,
`voronoi/RVD.h`, `mesh/mesh_sampling.h`, `mesh/mesh_geometry.{h,cpp}`, `mesh/mesh_repair.{h,cpp}` (postprocess).

- **Pipeline** (`mesh_remesh.cpp`): random initial sampling of `nb_points` seeds, then 5 Lloyd iterations, then 30
  "Newton" iterations (in fact L-BFGS from HLBFGS, m = 7, all tolerances 0, so exactly 30 iterations), then RDT
  extraction (`compute_surface`), then `mesh_adjust_surface`, then a facet flip if the enclosed volume is
  negative. The header says it does not handle sharp features ("remeshes a 'smooth' shape"). Cited: Yan, Lévy, Liu,
  Sun, Wang SGP 2009; Lévy, Bonneel IMR 2012 (6D anisotropy).
- **Initial sampling** (`mesh_sampling.h`, `mesh_generate_random_samples_on_surface`): reset the RNG (reproducible),
  draw n uniform values, sort them, walk the cumulative facet masses (area times the optional vertex `weight`
  density) and place each sample with random barycentrics in its facet. The mesh is first partitioned for the threads
  (`create_threads`), so the samples inherit data locality.
- **RVD computation** (`generic_RVD.h`, `compute_surfacic_with_seeds_priority`, `clip_by_cell_SR`): a double
  propagation. Facets are visited by a flood over the facet graph; for each facet the seeds whose cell meets it are
  found by a flood over the Delaunay 1-skeleton starting from a seed near the facet (stamps per seed and facet). Each
  (seed, facet) polygon is the facet clipped by the bisectors of the seed's neighbours in increasing distance order
  (`clip_by_plane`, ping-pong polygons). The "Delaunay" is by default a k-nearest-neighbour graph (ANN kd-tree) with
  the **security radius** test: stop when d(pi, pj)^2 > 4.1 R^2, R the farthest polygon vertex from pi; otherwise
  enlarge the neighbourhood by 1/8 and continue. Optional symbolic vertices and exact predicates (off by default
  in this path). Parallel: the mesh is cut into one part per thread and the per-seed sums (mass, mass times
  centroid) are accumulated under per-seed spinlocks (`compute_centroids_on_surface`), so the summation order
  depends on thread timing.
- **Lloyd** (`CVT.cpp`): seed := RVC centroid (sum over clipped triangles of area times centroid / area), skipping
  locked seeds and empty cells, with the security-radius check off (`set_check_SR(false)`) for speed.
- **Newton** (`CVT.cpp`, `funcgrad`): the CVT energy and its gradient (`compute_CVT_func_grad`, or an integration-simplex
  functional for LpCVT) handed to L-BFGS; locked seeds get a zero gradient. Falls back to Lloyd if HLBFGS is missing.
- **Anisotropy** (`mesh_geometry.cpp`, `set_anisotropy`): vertices become 6D (x, y, z, s n), n the unit normal,
  s = anisotropy times the bbox diagonal; the whole CVT runs in R^6 (`dim = 6`). Output vertices are the first three
  coordinates (`is_projection_`). Default `remesh:anisotropy` 0 (isotropic).
- **Gradation**: optional sizing field from a local feature size estimate (`compute_sizing_field`: its own CVT with
  5 Lloyd + 10 Newton iterations, medial axis, then vertex weights). Weights drive sampling and centroids.
- **RDT extraction** (`RVD.cpp`, `GetConnectedComponentsPrimalTriangles`): "multinerve" mode (default on): one
  output vertex per connected component of each restricted Voronoi cell; a triangle for each RVD vertex with two
  bisectors (a Voronoi edge crossing the surface); duplicates filtered later. Vertex position: the component's
  centroid, or the seed itself if the cell has one component and does not touch the border (`RDT_PREFER_SEEDS`).
- **Post-processing for manifoldness** (`mesh_repair.cpp`, `mesh_postprocess_RDT`): detect bad facets, repeatedly
  delete facets that have a vertex with a single incident facet, rebuild facet adjacency, reorient anti-Moebius,
  split non-manifold vertices. Without multinerve: plain `mesh_repair` with colocation at 1e-6 diag. No check of
  topology against the input, no self-intersection test, no quality guard; manifoldness is a repair, not a
  guarantee.
- **`mesh_adjust_surface`**: one linear least-squares solve (OpenNL), one unknown lambda_v per vertex, displacement
  along the vertex normal. Targets: the bidirectional ray hit from each vertex and from each facet centre (direction
  = sum of vertex normals) on the input AABB, cut off at 0.5 x local edge length. Border vertices use an in-surface
  direction and a "ribbon" of quads swept along the input border, weight `border_importance` 2. Pulls vertices
  off the input so that facets straddle it (less chordal error).

#### 3. Ideas that differ from ours

| Idea | Expected gain | Evidence | Effort | Risk | Exact code | Recommendation |
|---|---|---|---|---|---|---|
| A1. A CVT remesher (sampling + Lloyd + L-BFGS + RDT) next to the Remesher | quality: equilateral-looking triangles on smooth inputs; speed: not measured for geogram or us. Our procedural p01 is already 40° (REDUCER_REMESHER_20 section 2), so on smooth inputs the angle headroom is small; on Thingi10K the slivers sit at crease constraints (section 11), which `remesh_smooth` does not handle at all | geogram code (header: no sharp features; repair-based manifoldness); our numbers | L (kNN or Delaunay3 neighbourhood, RVD clipping, L-BFGS from the Nocedal paper, RDT, repair) | topology not kept (RDT is homeomorphic only for dense enough samples; the repair deletes facets); spinlock reductions are not deterministic, so the parallel path would need fixed-order per-part sums; breaks our closed / manifold / topology-kept success checks | no | **drop** as a remesher; the guaranteed route is RDR (CERTIFIED_REMESHING) |
| A2. Restricted Voronoi cells by clipping input triangles with bisectors (Yan 2009 RVD, security-radius stop) as the (C') / R2 region check of RDR | quality of the certificate: the spike's (C') proxy uses the input vertices nearest a sample and "sees no feature smaller than the input's edges" (spike README); true RVCs see them. Speed: the stage-1a proxy passes cost 0.01-0.1 s per case (spike README), the clipping cost is not measured | T14 is already cited in CERTIFIED_REMESHING.md (paper); geogram `clip_by_cell_SR` shows the 4.1 R^2 stop, propagation over facets and seeds | M (float spike); L exact (the bisector predicate family of section 14.4) | the float version is not a certificate; exact needs the planned BTPI predicates and new bounds | yes, if exact | **measure first** in stage 1b: a float RVC check in the spike against the proxy (uncertified share, missed regions) |
| A3. Lloyd / CVT tangential relocation in the Remesher (vertex to the area-weighted centroid of its restricted cell, approximated by the one-ring triangle centroids weighted by area) | quality: q p5 / p01 on smooth parts; not measured. Upper bound on the slivers that count: 99 % of q < 0.1 touch a feature constraint (RemeshBenchmark), which a smoothing type does not move | literature (Alliez 2003, the "CVT barycentre" relocation in RESEARCH_REDUCER_REMESHER.md); `CustomSmoothF` already allows it | S | low; the 2^k and determinism tests must hold | no | **measure first**, low priority: one A/B run in RemeshQualityBenchmark via `CustomSmoothF` before any API |
| A4. `mesh_adjust_surface` post-pass (normal offset solved in least squares against vertex and facet-centre targets) | quality: Hausdorff to the input. Assumed bound: with all vertices on a convex smooth input the error is the sagitta; offsetting outwards can at best halve it. Measured base: Hausdorff/target 0.04 (sphere_refine), 0.043 (bumpy_uniform) | geogram code; no measurement | S (one sparse least-squares solve; `LaplacianMeshSmoother` has a CG path) | vertices leave the input (conflicts with projection-based guarantees, `MeshProjectionTarget` users, Hausdorff certificates of RDR, D within the band); can create self-intersections on thin walls | no | **measure first** as an opt-in post-pass only if a caller asks for lower chordal error; otherwise drop |
| A5. 6D anisotropic embedding (x, y, z, s n) | quality: anisotropic, curvature-aligned triangles at the same vertex count | Lévy-Bonneel 2012 (paper); geogram code | L (needs A1) | as A1; no anisotropic caller on the roadmap ("quad remeshing" is on the later list) | no | **drop** for now |
| A6. LFS-based gradation (sizing from a medial-axis estimate) | quality: fewer vertices at equal fidelity on parts with fine features | geogram code; our `RemeshSizing` only has `FromWallThickness` | M | LFS from samples is noisy at creases (lfs = 0) | no | **measure first** only if a caller needs curvature / feature sizing; `FromWallThickness` already covers the thin-wall case (roadmap) |

#### 4. What we already do better or equally

- Topology kept (0 changes), creases and boundary kept through constraints, opt-in self-intersection prevention,
  bit-identical repeat / serial / 2^k scaling (REDUCER_REMESHER_20 sections 2 and 11); geogram's CVT path has none
  of these (repair-based manifoldness, parallel spinlock sums, no feature handling in `remesh_smooth`).
- The guaranteed route (RDR with exact predicates, proofs of section 14) aims beyond what geogram's RDT offers.

### (b) Decimation and smoothing

#### 1. Our counterparts and numbers

- `mesh/Reducer.cs` (QEM, area-weighted quadrics, boundary and feature planes, Trettner-Kobbelt regularization,
  `PreventFoldOver`, `PreventSelfIntersections`, `MaxDeviation`, `MaxDeviationTwoSided`), `mesh_ops/PartitionedReducer.cs`.
  REDUCER_REMESHER_20 section 2: 3.2 µs per input triangle (0.3 s for 100k), 21 µs with `PreventSelfIntersections`,
  topology changed 0, Hausdorff at 1 % 7 % of the diagonal (thingi median), bit-identical. Octree vertex clustering
  exists only inside `AdaptiveDualContouring` (docs/SOURCES.md).
- Smoothing: `mesh_ops/LaplacianMeshSmoother.cs` (global least squares |Lx|^2 + soft weighted position constraints,
  CG, solved relative to the bounds centre), `mesh_ops/MeshIterativeSmooth.cs` (explicit Jacobi rounds, uniform /
  cotan / mean value), `mesh_ops/LaplacianMeshDeformer.cs`, `mesh_ops/MeshLoopSmooth.cs`, `mesh_ops/SmoothedHoleFill.cs`.
  No benchmark numbers found for these: not measured.

#### 2. Geogram

- `mesh/mesh_decimate.{h,cpp}` (`mesh_decimate_vertex_clustering`): **uniform-grid vertex clustering**
  (Rossignac-Borrel style). Cell size h = bbox diagonal / nb_bins; each non-required vertex snapped to the cell's
  lower corner (`floor((p - min)/h) h`), required vertices (border with `KEEP_B`, flagged vertices) kept; merge by
  `colocate_by_lexico_sort`; new position = plain average of the cluster's original points (not a QEM optimum);
  remap corners, delete merged vertices, `mesh_repair` (duplicate facets with `DUP_F`), and with `DEG_3` repeatedly
  remove degree-3 vertices within 0.001 R. No manifold or topology guarantee, no error bound. (Side note: with
  `vertices_flags` but without `KEEP_B`, `is_required` is empty and indexed; a latent bug.)
- `mesh/mesh_smoothing.{h,cpp}` (`mesh_smooth`): one global OpenNL least-squares solve per coordinate of the uniform
  graph Laplacian = 0 (membrane energy), vertices in the "selection" attribute locked, rows normalized. That is
  harmonic interpolation of the free vertices from the locked ones (fairing / hole patch), not a mild smoothing.

#### 3. Ideas

| Idea | Expected gain | Evidence | Effort | Risk | Exact code | Recommendation |
|---|---|---|---|---|---|---|
| B1. Grid vertex clustering as a fast LOD / pre-decimation for very large meshes | speed: avoids the queue and quadrics; our Reducer is 3.2 µs per triangle, a clustering pass is a sort plus linear passes, but its time is not measured. Upper bound on a pre-pass: it can save at most the Reducer's time on the removed part | geogram code; Rossignac-Borrel 1993 (already in SOURCES.md, from memory) | S | non-manifold output and topology changes (fails our clean-result checks); averaged positions shrink features | no | **drop** (no caller; the Reducer and `PartitionedReducer` cover the sizes on the roadmap) |
| B2. Hard-locked global membrane smoothing (`mesh_smooth`) | none: `LaplacianMeshSmoother` with large weights on the locked set (or `RegionSmooth` with a fixed border) computes the same minimizer | geogram code vs our class doc | - | - | no | **drop**, we already do this |

#### 4. Already equal or better

- Reducer: error-driven (QEM), features, fold-over and self-intersection guards, certified deviation bounds, topology
  kept, deterministic; geogram's decimator is a cruder, guarantee-free tool.
- Smoothing: ours has soft weights, a numerically safer origin shift, explicit and cotan / mean-value variants;
  geogram has only the uniform membrane solve with hard locks.

### Geogram files read (path, approx. lines)

- src/lib/geogram/mesh/mesh_remesh.h (1-133), mesh/mesh_remesh.cpp (1-602, all)
- src/lib/geogram/voronoi/CVT.cpp (1-379, all), voronoi/CVT.h (80-160 via grep, defaults)
- src/lib/geogram/voronoi/RVD.h (430-470, RDTMode)
- src/lib/geogram/voronoi/RVD.cpp (99-210 via grep, 360-430, 1655-1700, 1900-2140)
- src/lib/geogram/voronoi/generic_RVD.h (60-370 via grep, 1300-1470, 2120-2240)
- src/lib/geogram/mesh/mesh_sampling.h (1-30 header, 120-180)
- src/lib/geogram/mesh/mesh_geometry.h (340-365), mesh/mesh_geometry.cpp (237-280)
- src/lib/geogram/mesh/mesh_repair.h (80-105), mesh/mesh_repair.cpp (1093-1175)
- src/lib/geogram/basic/command_line_args.cpp (header; lines 131-302 and 770-781 via grep, remesh / opt defaults)
- src/lib/geogram/mesh/mesh_decimate.h (1-91), mesh/mesh_decimate.cpp (1-169, all)
- src/lib/geogram/mesh/mesh_smoothing.h (1-64), mesh/mesh_smoothing.cpp (1-110, all)
- headers only (first 40 lines, licence check): all other files in src/lib/geogram/voronoi/

## 5. Mesh repair, hole filling (mesh_repair, mesh_fill_holes, mesh_preprocessing) and parameterization (LSCM, ABF++, atlas)

Every geogram file read carries the Inria BSD-3 header (checked with `head -40` first). No OpenNL, xatlas or other
third_party code was opened. Nothing in this area touches exact code (`math/Exact*`, `AdaptivePredicates.cs`,
`mesh_ops/ExactMesh*`).

### (a) Mesh repair and hole filling

#### 1. Our counterpart
- Pipeline: `mesh_ops/MeshRobustRepair.cs` (cascade: repair kept / exact resolve / solidify / alpha wrap; verdict via
  `NotCleanReason` / `FindDefects`), `mesh_ops/MeshAutoRepair.cs` (orient, `MergeCoincidentEdges` crack closing, tiny
  boundary edge collapse, hole fill, bowtie disconnect, cut orientation seams, erosion fallback, global orientation),
  `MeshRepairOrientation.cs` (patches along a maximum spanning tree, `KeepShortSeams`; global orientation by ray casts),
  `MinimalHoleFill.cs` (fan fill, remesh, collapse back to the boundary vertices, flip passes for flatness, Gaussian
  curvature, triangle quality), `AutoHoleFill.cs` (always picks the minimal fill today), `SimpleHoleFiller`,
  `SmoothedHoleFill`, `PlanarHoleFiller`, `MeshStitchLoops.cs`, `MeshPostRepairCleanup.cs` (duplicate pairs, needles,
  caps, folds, bowties, inside-out non-cavity components), STL welding strategies in `io/STLReader.cs`.
- Numbers (benchmarks/MeshRepairBenchmark/README.md): `robust` 274 of 400 repaired, large set 13 of 30 (2026-10-28);
  path time shares repair kept / exact / solidify / wrap = 9 / 35 / 12 / 44 % (248 / 974 / 327 / 1221 s); median repair
  2.8 s. Old per-step finding (1.0.324-era section "Results with this fork"): hole filling adds most degenerate triangles
  and folds; `MergeCoincidentEdges` adds duplicates and folds. Not re-measured per step on current master.
- NotCleanBenchmark/README.md: `NotCleanReason` 144 ms (200k sphere), 240 ms (185k wrap) after three levers.
- Roadmap (docs/roadmap/alpha-wrap-and-repair.md, "Open for the future"): no open item on hole filling or orientation;
  item 6 (procedural coverage of `WindingContouring.FinishHoles`) is adjacent.

#### 2. Geogram
- `mesh_repair()` (mesh/mesh_repair.cpp): colocate (eps = 0: lexicographic sort, exact duplicates; eps > 0: absolute
  radius via `Geom::colocate`, not read) -> triangulate -> remove degenerate facets (repeated vertex) and duplicates
  (vertices rotated to smallest index and direction-normalized, indirect lexicographic sort, runs of equal facets;
  orientation ignored; an `operand_bit` XOR keeps fins right for booleans) -> connect facets by corner lists per
  vertex, accepting both orientations; edges with more than two facets are left unconnected (become border) ->
  "anti-Moebius" reorientation -> split non-manifold vertices (walk fans, duplicate the vertex per fan) -> optional
  Co3Ne post-process (small components, `fill_holes`, small components again, reconnect) -> `orient_normals`.
- Anti-Moebius reorientation: graph distance of each facet to the border, clamped at 5 (bytes, BFS by sweeps);
  propagation starts from the facets farthest from the border, priority queue = array of stacks by distance. A facet
  with conflicting visited neighbours is a Moebius event: it keeps the majority orientation and is disconnected from
  the minority neighbours (a cut).
- `orient_normals` (mesh_preprocessing.cpp): flips every connected component with negative signed volume. A closed
  cavity shell nested in a solid is therefore flipped to the wrong side.
- `remove_small_connected_components` (area or facet count), `remove_small_facets`, `expand_border` (moves border
  vertices outward in the tangent plane so that CVT remeshing closes small holes), `remove_degree2_vertices`
  (disconnects facet pairs adjacent twice).
- `fill_holes` (mesh_fill_holes.cpp): holes = border loops; skipped above `max_edges`; triangulation without new
  vertices, chosen by `algo:hole_filling`:
  - `loop_split` (default): recursive split by the chord (i, j) minimizing |p_i - p_j| / min(arc length along the
    loop); chords between already connected vertices are skipped (avoids non-manifold edges). O(n^2) per split.
  - `Nloop_split`: the ratio times an angle factor from the averaged border normals (in-plane, outward) at i and j,
    so chords along the "closing" direction are preferred; documented as better on a hand scan "but may cause some
    triangles overlap".
  - `ear_cut`: greedy ear by the signed turning angle about the averaged normal; no check of existing edges (TODO).
  - The filled hole is accepted only if its total area is below `max_area`; afterwards `mesh_repair` runs again to
    drop zero-length edges. No refinement, no fairing, no Liepa-style DP.
- `mesh_degree3_vertices.h` (header only): removes degree-3 vertices close to their neighbours' plane; not repair.

#### 3. Ideas that differ from ours
| idea | gain | evidence | effort / risk | recommendation |
|---|---|---|---|---|
| Loop-split hole triangulation (chord ratio, optional normal factor) instead of fan + remesh + collapse | quality only (degenerate triangles, folds from the fill); speed: the repair-kept path is 9 % of the 400-set time and the fill is a part of it, so the bound is < 9 %, below the 10 % rule | geogram code; our per-step defect numbers are from the 1.0.324-era run, not current master | S / changes every hole fill (outputs) | drop as a port: it is a greedy heuristic and our minimal fill optimizes curvature after the fact. If the current per-step run (`BENCH_REPAIR=steps`, `analyze_stages.py`) still shows the fill as the main source of folds or degenerate triangles, measure Liepa 2003 (min max-dihedral, then area, O(n^3) DP; paper) as the initial triangulation of `MinimalHoleFill` against the fan |
| Skip chords between vertices already joined by an edge | robustness (fewer failed fills) | geogram `halfedge_exists_between_vertices` | S | we already fail cleanly: `MinimalHoleFill.Apply()` returns false when the fan would make non-manifold edges and the cascade falls back; measure first how often that fallback is hit (not measured) |
| Anti-Moebius order (start far from the border, cut minority at conflicts) | orientation quality on non-orientable inputs | geogram code | - | we already do equal or better: maximum spanning tree patches with a short seam, `CutOrientationSeams` (README: 101397 went 2 -> 175 inconsistent edges with the depth-first version, fixed) |
| Global orientation by signed volume per component | none | geogram flips nested cavities wrongly | - | drop: ours uses ray casts plus `FindInvertedComponents` with the cavity test |
| Duplicate facets by normalized sort, keep one | none | geogram code | - | drop: `MeshPostRepairCleanup` removes duplicate pairs (two-sided sheets), which suits solids better; the exact boolean has its own fin handling |
| Non-manifold edges left open, then non-manifold vertices split | none | geogram code | - | equal: `DMesh3` cannot hold non-manifold edges, readers duplicate vertices; `DisconnectBowtie` (fixed in this fork) splits fans |
| Small components by area / facet count | none | only in geogram's Co3Ne mode | - | drop: our cleanup removes tiny closed sliver components; `PointCloudSurface.MinComponentAreaFraction` covers reconstruction |
| `expand_border` + remesh to close small holes geometrically | none | geogram code | - | drop: the solidify and alpha wrap fallbacks close holes with guarantees |
| Colocation with eps = 0 (exact) or an absolute eps | none | geogram code | - | drop: our tolerances are relative to the mesh size (README: absolute tolerances lost >10 % triangles on 118/400 in meters) and `MergeCoincidentEdges` also closes cracks, which geogram does not |
| Hole fill gated by `max_area` / `max_edges` | API convenience | geogram code | S | drop for the repair (it wants closed solids); only as an option if a caller asks |

#### 4. Where we are equal or better
Relative tolerances; crack merging; orientation with seams and cavity-aware global orientation; a verified verdict
(`NotCleanReason`: exact self-intersections, inside-out components) and a cascade with volume / Hausdorff checks.
Geogram's repair has none of these checks; it neither detects nor resolves self-intersections.

### (b) Parameterization

#### 1. Our counterpart
- `parameterization/`: `LSCMParameterization` (conformal energy E_D - A, two automatic boundary pins far apart, cotan
  weights with intrinsic mollification, `FlippedTriangles` status), `BoundaryFirstFlattening`, `ConeFlattening`,
  `ARAPParameterization`, `SLIMParameterization`, `FoldoverFreeParameterization`, Tutte, `MeshDiscCut`,
  `MeshChartSegmentation` (VSA L2,1 region growing + D-Charts compactness, farthest-point reseeding, `SplitInTwo`),
  `UVAtlasGenerator` (Auto: cones else charts, split failing charts, SLIM / Tutte fallback, exact `UVOverlap`),
  `UVPacker` (min-area rectangle rotation, skyline bottom-left, several bin widths, exact post-check). Solver
  `solvers/SparseCholesky.cs`. No ABF / ABF++, no spectral conformal, no sparse eigensolver in the library.
- Numbers: ParameterizationBenchmark/README.md, 48k hemisphere: LSCM 0.78 s, BFF 0.68 s, ARAP 1.31 s, Tutte 0.38 s,
  SLIM 4.3 s (default); LSCM / BFF mean s1/s2 1.004-1.014; interleaved u,v Cholesky 1.37 s first, 0.27 s refactor,
  29 ms solve. UVAtlasBenchmark/README.md (250 meshes): 159 Success, 79 DistortionLimitExceeded, 12 Failed (9 disc-cut
  failures on broken meshes, 3 non-injective Tutte); 0 overlaps; charts by method BFF 87 %, SLIM 4 %, Tutte 5 %;
  coverage median 0.46; seam / sqrt(area) median 20.3; mean conformal median 1.013, p90 17.7; time median 2.6 s, max
  148 s. Large set (20): time median 233 s, 16 of 20 at `MaxCharts` = 128, conformal median 4.6. The xatlas comparison
  (`compare.py`, uvatlas-compare.yml) exists, but its results are not in the docs (not measured here).
- Roadmap (docs/roadmap/parameterization-geodesics.md "Open for the future" #4; docs/ISSUE_CASE_FINDINGS.md P1, P3):
  P1 collapse of the free inner ring on a low-poly open cylinder, P3 pin-dependent asymmetry (~1e-4 of UV size); both
  name the spectral conformal parameterization (Mullen et al. 2008) as the research fix.

#### 2. Geogram
- `mesh_LSCM.cpp`: LSCM in the "geometric form" (Z1 - Z0)(U2 - U0) = (Z2 - Z0)(U1 - U0) per triangle in a local frame,
  two rows, no division (stable on degenerate triangles). Pins: project on the plane of the two longest bbox axes, lock
  the min / max vertices along the longest one (any vertex, not only boundary); that projection is also the CG start.
  Solver: SuperLU up to 200k vertices, else AMGCL, else Jacobi-CG; threshold 1e-10, 5n iterations. Angle variant (used
  after ABF++): rows from target angles with the sine-law ratio, scaled by sqrt(triangle area). Spectral mode: ARPACK,
  10 eigenpairs of the least-squares system with no pins, takes the first eigenvalue > 1e3 x the smallest. The code
  passes no boundary mass matrix B, so it appears not to be the generalized problem of Mullen et al. (OpenNL not read,
  so not certain). Minor: the angle path indexes `angle_[c0+1+1]` instead of `c0+i+1` (wrong only for polygons).
- `mesh_ABF.cpp` (ABF++, Sheffer et al. 2005): unknowns: corner angles alpha, multipliers for C1 (triangle sum, one
  per face), C2 (2 pi per interior vertex), C3 (sine-law wheel per interior vertex). Targets beta = 3D angles scaled to
  sum 2 pi at interior vertices, clamped to [3, 175] degrees (input angles clamped >= 1 degree); weights 1 / beta^2.
  Newton: the Hessian of the energy is diagonal (Delta = 2w), so alpha is eliminated; J1 is implicit (one face per
  corner) and Delta* is diagonal per face, so lambda1 is eliminated too; the remaining (2 nint) x (2 nint) system
  M = J* Delta*^-1 J*^T - J2 Delta^-1 J2^T is factorized with SuperLU each iteration; back-substitution gives dlambda1
  and dalpha. At most 10 iterations; stop at L1 gradient norm <= 1 or L1 step <= 1 (absolute, grows with mesh size,
  a TODO in the code); step cut so no angle drops below 1e-4, weights x1.2 for angles hitting 0 or pi. Not converged
  -> beta (= LSCM). UVs from the angles by the angle-LSCM above (two pins). Triangles only.
- `mesh_atlas_maker.cpp`: initial charts by flood fill across edges with dihedral < 45 degrees (and an optional
  "chart" attribute); a single chart over 30000 facets is pre-split by VSA L2. Stack of charts: precheck (has border,
  <= 30000 facets) -> parameterize (default ABF++; one facet: plane projection) -> postcheck by `ParamValidator`
  (1024^2 raster, rejects > 0.5 % overlapped pixels or a scaling ratio > 20; charts of <= 10 facets retried by
  projection). Failing charts are classified by Euler characteristic, border count and hole-area / surface ratio
  (disc-, cylinder-, sock-like, other) and split: cylinder / sock into 2 along a principal inertia axis, others into
  >= 6 (>= 7) by VSA. Then packing.
- `mesh_segmentation.cpp` (parts): VSA L2 / L12 as a CVT on the surface (restricted Voronoi diagram, 30 Lloyd + 10
  Newton), L12 via anisotropic normals x (100 diag x 0.02), spectral variants via 8 / 20 / 100 manifold harmonics as
  the embedding, inertia-axis split; then 10 iterations of segmentation-boundary smoothing and a post-process.
- `mesh_param_packer.cpp` (parts): Tetris packer (Lévy et al. 2002): per chart an upper / lower horizon on a grid,
  placement minimizing lost area under the horizon, bisection on the strip width; 4-pixel margin; or xatlas packing
  (`rotateCharts = false`). Chart rotation: TODO in the code.

#### 3. Ideas that differ from ours
| idea | gain | evidence | effort / risk | recommendation |
|---|---|---|---|---|
| Pin-free spectral conformal (Mullen et al. 2008, generalized E_C u = lambda B u), by inverse power iteration on `SparseCholesky` (one factorization, a few 29 ms solves at 48k) | quality: P3 asymmetry from ~1e-4 toward solver precision (assumed: no pins); P1 inner-ring collapse: unknown, must be measured; no pin choice. Speed about LSCM's (assumed) | papers; geogram ships a spectral mode (with ARPACK, and apparently without B, so not a reference to port); our P1 / P3 fixtures exist | M / opt-in method keeps LSCM outputs; a default change would move ARAP, SLIM and atlas results | measure first: a spike on the P1 cylinders (8x3, 16x4, 24x6) and the P3 plate, plus the ParameterizationBenchmark meshes for distortion and time. This is the open research item #4 |
| ABF++ (angle-space Newton with two Schur reductions, then angle-LSCM) | quality: angle distortion vs BFF not measured; atlas tails (p90 conformal 17.7) come from Tutte / SLIM fallbacks and charts at `MaxCharts`, not from the conformal method (BFF charts are near 1.0) | geogram code, paper; our numbers | M-L (Newton, indefinite reduced system: our solver is Cholesky only, LDL^T or LU needed) / not injective either | drop; reopen only if a measurement shows BFF charts with clearly higher angle distortion than ABF++ on coarse meshes |
| Degenerate-safe "geometric form" of the LSCM rows | none | geogram code | - | equal: our cotan weights use intrinsic mollification |
| Chart typing (cylinder- / sock-like) and inertia-axis split into 2, plus segmentation-boundary smoothing | seam length (median 20.3 sqrt(area)) and chart count; not measured against geogram | geogram code | S-M / changes atlases | measure first: run the existing xatlas comparison and see whether our seams are worse; if so, try boundary smoothing in `MeshChartSegmentation` first (cheap) |
| Initial segments from mesh size (facets / 30000 + 1) instead of a fixed `MaxCharts` = 128 | large set: 16 of 20 at `MaxCharts`, conformal median 4.6, time median 233 s; how much a size rule saves is not measured | geogram code; our README already says "MaxCharts should grow with the mesh" | S / changes large-mesh atlases | measure first, on the 20 large meshes (time per stage is not in the docs, so no upper bound yet) |
| Raster validator (0.5 % overlapped pixels) | none | geogram code | - | drop: our exact `UVOverlap` is strictly stronger |
| Tetris packer / xatlas packing | none | geogram code | - | drop: our skyline packer rotates charts (geogram does not), tries widths and checks exactly; coverage 0.46 median (geogram not measured) |
| Iterative solver (AMG / CG) above 200k vertices | speed on very large charts; not measured | geogram code | M | drop for now: charts are bounded by segmentation; reopen with a large-chart workload |

#### 4. Where we are equal or better
More methods (BFF, cones, SLIM, foldover-free, Tutte fallback), exact overlap checks before and after packing,
status codes instead of silent fallbacks (geogram ABF++ falls back to LSCM silently and accepts up to 0.5 % overlap),
disc cutting of any genus (`MeshDiscCut`), chart rotation in packing.

### Geogram files read (src/lib/geogram, commit 16440c4)
- mesh/mesh_repair.h 1-230; mesh/mesh_repair.cpp 1-1288 (all)
- mesh/mesh_fill_holes.h 1-90; mesh/mesh_fill_holes.cpp 1-750 (all)
- mesh/mesh_preprocessing.h 1-121; mesh/mesh_preprocessing.cpp 1-220 (all)
- mesh/mesh_degree3_vertices.h 40-77 (header only)
- parameterization/mesh_LSCM.h 1-83; parameterization/mesh_LSCM.cpp 1-644 (all)
- parameterization/mesh_ABF.h 1-71; parameterization/mesh_ABF.cpp 1-843 (all)
- parameterization/mesh_atlas_maker.h 1-124; parameterization/mesh_atlas_maker.cpp 1-~730
- parameterization/mesh_segmentation.h 1-96; parameterization/mesh_segmentation.cpp 1-60 (includes), a grep of
  declarations, 522-618
- parameterization/mesh_param_packer.h 1-82; parameterization/mesh_param_packer.cpp 1-60 (includes), a grep of
  declarations, 1092-1140
- parameterization/mesh_param_validator.cpp 1-12 (licence) and a grep of the thresholds (lines ~95-180)
- Licence headers (head -40 or head -12) of all .h files in mesh/ and parameterization/ and of the .cpp files above.
