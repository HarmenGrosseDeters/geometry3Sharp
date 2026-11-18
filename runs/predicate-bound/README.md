# How much is left in the exact predicates? (measurement, 2026-11-19; no library change, no PR)

**Verdict: no predicate lever clears the 10 % bar.** The exact (non-filter) stage of the predicates costs a median 0.5 % of
an ExactMeshBoolean (p95 3.9 %, max 8.4 %; never above 10 %). Making every predicate free is worth at most 7 % (median) to
18 % (p95). Filters 2x faster: 1-3 % (median), 3-8 % (p95). The levers that clear 10 % on about half the inputs are the exact
**constructions** around the predicates (implicit-point, line and plane BigInteger arithmetic: median 7 %, 10.8 % with the
predicate stages, max 36 %), not the predicate evaluations. One input class has a predicate exact path above 10 %: an exactly
cocircular 2D grid in the Delaunay mesher (22 %).

## Method (and its limits)

The first plan (stage timers inside the exact-scope files) was dropped: the determinism analyzers (G3D0001/G3D0009) reject the
edit and the override was denied. This is the coordinator's option C: no library edit (the instrumentation commit on this
branch is reverted; its history stays).

* **Calls and exact-stage counts:** the existing `PredicateStats` (`-p:G3PredicateStats=true`), BooleanComparison `profile`,
  one run per case and operation (`raw/counts_boolean.jsonl`). Plane sides come from `Counts["winding_plane_sides(_exact)"]`.
  Counts exist for the Boolean corpus only (the other pipelines print none; no library edit to add them).
* **Time per stage: sampling profiler.** `dotnet-trace collect --profile dotnet-sampled-thread-time` (EventPipe, managed
  stacks, speedscope export), `DOTNET_PROCESSOR_COUNT=1`, normal Release build. Each CPU sample under a `geometry3Sharp!`
  frame is bucketed by its stack (`tools/classify.py`): *construction* (compute_exact, edge_plane_exact, ...), *exact* (an
  `ExactPredicates.*_exact`, AdaptivePredicates, FixedArith, ExactLimbSigns, PreparedLimbs or BigInteger frame; under a
  predicate entry = predicate exact stage, else = other exact arithmetic), *filter* (the predicate entry frame itself),
  other. The denominator is the CPU_TIME leaf samples under library frames (waits excluded; GC/unmanaged samples are not in it,
  so shares are slightly high = conservative for an upper bound).
* **Resolution and error:** 1.5 ms mean sample interval (EventPipe 1 ms nominal); 1500-4000 samples per Boolean input
  (>= 2.5 s of repeats per input), so a 10 % share has a 95 % interval of about +-1.3 points, and 1 % about +-0.4. A
  sampled share is not corrected for tracing overhead (about 1.7-2.8x slower wall time under the trace; assumed uniform
  across the code). Inlined code has no frame: a filter that the JIT inlined into its caller is invisible, so the sampled
  filter share is a lower bound.
* **Filter time (upper bound): counts x microbenchmark** (`micro/`, plain Stopwatch loops, not BenchmarkDotNet; hot cache,
  random inputs the filter decides). Two numbers per filter: throughput loop (LOW: orient2d 2.7, orient3d 6.5, incircle 9,
  insphere 26, plane side 2 ns) and a delegate loop with call overhead (HIGH: 11.3, 16.6, 18.6, 32, 6.9 ns; the arrangement
  filters have no microbenchmark and are assumed 5-12 / 12-30 / 4-10 / 5-12 ns). HIGH is the upper bound used in (b) and (c);
  the truth lies between. Divided by the un-traced median wall time of the three operations (1 core).
* **Timer overhead:** there is no per-call timer (the profiler replaces it); the cost of the profiler is the tracing overhead
  above.
* **Inputs.** Boolean: 24 BooleanComparison synthetic pairs (coplanar, touching, thin gaps, UTM, scale spread), `grid:5`
  (n-ary one pass), 12 clean Thingi10K models (`perf/cases-thingi12.txt`, each against its rotated copy; fetched from the
  Hugging Face mirror to a scratch directory, never committed; 392 to 124k triangles). Union, intersection and difference
  pooled per input. Also: SelfIntersectionBenchmark (19 cases), Meshing2Benchmark `perf` (DelaunayMesh2, 4 point kinds,
  and the constrained mesher), TetMeshBenchmark (insert, refine proc = MeshTetrahedralization, wrap on procedurals and 6
  Thingi meshes), SolidBooleanBenchmark (one aggregate trace). **Not covered:** the large tail cases (#1047, 42327: classification
  with 3.88e9 plane sides) are not reproduced here; there the plane-side count is the cost (below). The generated
  `IndirectPredicates` (Attene-style LPI/TPI) are not called by the Boolean pipelines (only PolyhedralEnvelope).

## Counts (Boolean corpus, 37 inputs, one run each, summed)

| predicate | calls | exact stage | share |
|---|---:|---:|---:|
| orient3d (double input; intersection tests) | 23.3 M | 1.76 M | 7.6 % |
| orient2d | 4.26 M | 152 k | 3.6 % |
| arr.orient (implicit points) | 4.14 M | 684 k | 16.5 % |
| arr.incircle | 2.14 M | 119 k | 5.5 % |
| arr.compare | 2.31 M | 91 k | 3.9 % |
| arr.along | 1.82 M | 63 k | 3.5 % |
| plane side (classification) | 188 k | 19 k | 10.1 % |
| incircle (double), winding orient3d | 3 k, 210 k | 3 k, 0.8 k | 100 %, 0.4 % |

Stage split of the implicit-point predicates (`PredicateStats` has no per-stage counts below "exact"; the profiler shows the exact
time is mostly FixedArith and BigInteger, e.g. grid:5: FixedArith.Mul and BigInteger multiply/gcd in `exact_ref`/`compute_exact`).

## Upper bounds, % of total time (Boolean, 37 inputs; per input over the 3 operations)

| bound | median | p95 | max |
|---|---:|---:|---:|
| (a) predicate exact stages free | 0.5 | 3.9 | 8.4 |
| (a') all exact arithmetic free (predicate stages + constructions + other BigInteger/limbs) | 10.8 | 21.8 | 36.5 |
| &nbsp;&nbsp;of which exact point / plane constructions | 1.4 | 9.0 | 12.3 |
| &nbsp;&nbsp;of which other exact arithmetic (line directions, edge planes, centroids, far points) | 5.9 | 17.5 | 23.7 |
| filter time, LOW / HIGH microbenchmark | 2.5 / 6.5 | 6.2 / 16.5 | 12.4 / 32.4 |
| filter time, sampled (lower bound) | 0.4 | 2.1 | 2.6 |
| (b) all predicates free = (a) + filter (HIGH) | 7.3 | 17.9 | 33.1 |
| (c) filters 2x faster = filter / 2 (HIGH / LOW) | 3.3 / 1.2 | 8.3 / 3.1 | 16.2 / 6.2 |

Inputs where (a) exceeds 10 %: **none**. Where (a') exceeds 10 % (20 of 37): coplanar_half_box 15.0, coplanar_meshed_half_cell 13.5,
coplanar_rotated_45 19.1, coplanar_shift_x 12.7, far_from_origin_1e8 12.0, nearly_coplanar_1e-6deg 10.8, sphere_identical 24.6,
touch_edge 10.9, utm_buildings_axis_aligned 12.6, utm_buildings_rotated 13.6, Thingi 110205 13.9, 1146188 12.4, 116076 13.2, 1228193 13.0,
191078 21.8, 45612 16.3, 471993 11.9, 472174 10.8, 61583 11.6, grid:5 36.5. By class: the 12 Thingi pairs have (a) median 0.6 / p95 1.5 / max 3.6 and (a')
median 12.2 / p95 16.3 / max 21.8 (constructions 5.3 median); the synthetic pairs (a) 0.4 / 3.9 / 6.5 and (a') 9.0 / 19.1 / 24.6.
HIGH filter share exceeds 10 % on 12 inputs (max 32 % on Thingi 472174); that is the upper bound, the sampled lower bound is 2.6 %.

## Other pipelines (sampling only, no counts; percentages of library CPU time)

| pipeline | inputs | exact path (a) median / p95 / max | sampled filter median / p95 | (c) filters 2x faster median / p95 | inputs with (a) > 10 % |
|---|---:|---|---|---|---|
| self-intersection resolve (Reducer / Remesher PreventSelfIntersections) | 19 | 0.1 / 1.6 / 1.9 | 3.3 / 8.0 | 1.7 / 4.0 (lower bound) | none |
| SolidBooleanPipeline (one aggregate trace, 17 cases) | 1 | 4.1 total (3.4 exact + 0.7 construction) | 3.7 | 1.8 | - |
| 2D triangulation, DelaunayMesh2 `perf` (uniform, grid, utm, clustered) and constrained (coastline, parcels) | 6 | 0.0 / 22.2 / 22.2 | 11.3 / 14.9 | 5.7 / 7.5 | `grid` (exactly cocircular points, exact incircle through AdaptivePredicates) 22.2 |
| tetrahedralization: Delaunay3 insert 100k, MeshTetrahedralization refine (procedural), wrap (procedural) | 3 | 3.8 / 6.7 / 6.7 | 2.3 / 8.5 | 1.2 / 4.3 | none |

Short Thingi wraps (0.2-0.3 s) have too few samples; read as 0-2 %. The mesher's `perf --m2 crossings` row failed to run (not measured).
In the mesher the sampled filter share is real (orient2d / incircle are not inlined): 11-15 % of the time; 2x faster filters
would give 5-7 %, still under the bar.

## What would attack the measured cost (papers only; plausible gain on these numbers)

* **Floating-point filters** (Shewchuk 1997 adaptive stages; Burnikel-Funke-Seel and Meyer-Pion style static/semi-static
  filters; Ozaki-Ogita-Rump-Oishi, "Simple floating-point filters for the two-dimensional orientation problem", BIT 2016,
  tighter error bounds): our filters already settle 85-99 % of calls and filter time is median 2.5-6.5 % (Boolean); a tighter
  bound moves a few percent of the exact-stage calls into the filter: gain far below 1 % of total time. Vectorizing them: the
  roadmap's measured 2-4 %.
* **Lévy 2024 (exact predicates / constructions / combinatorics for mesh CSG, Geogram `expansion_nt`):** lazy exact numbers and
  cached, interval-filtered implicit points; relevant to the *construction* share here (median 1.4 %, max 12 %), and a
  comparison of designs rather than a speedup of the predicate stage. Plausible gain: part of the constructions bucket on
  coplanar / many-implicit-point inputs (a few points), not the 0.5 % predicate stage.
* **Attene 2020 (indirect predicates) and Cherchi et al. 2020 / 2022 (arrangements with floating-point filtered implicit
  points):** evaluate the predicate of an implicit point directly from its defining points (lower degree than building the
  exact point, then the predicate). That is what `FixedArith` + generated `IndirectPredicates` already implement for
  orient3d LPI/TPI; the remaining BigInteger / limb time in "other exact arithmetic" (line directions, edge planes,
  centroids, far points: median 5.9 %, max 23.7 %) is constructions the arrangement still needs as explicit exact values; a
  lower-degree or deferred formulation (compute only when a predicate needs it, as the lazy points do) is the one lever
  with a measured ceiling above 10 %: about 11 % median, 22 % p95 if all exact arithmetic were free, realistically a
  fraction of that.
* **Fewer predicate calls** (the plane-side count in classification, 3.88e9 orient3d in the tail case): algorithmic (patch
  propagation as in Lévy 2024, L1/L2 of #633), not predicate speed. Our HIGH filter bound says 16-32 % of an operation can be
  filter evaluations on a few inputs (472174), which supports reducing the number of evaluations over speeding each one.

## Reproduce

`tools/run_cases.py`, `run_si.py`, `run_generic.py` (dotnet-trace per case; paths are scratch paths of the session),
`classify.py` (bucket rules), `table.py` (counts and bounds), `micro/` (filter microbenchmark). Raw numbers: `raw/`.
