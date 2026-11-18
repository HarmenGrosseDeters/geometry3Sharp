# ExactMeshBoolean against Geogram's exact mesh boolean (2026-11)

Owner-approved 2026-11-17: a head-to-head measurement of `ExactMeshBoolean` against Geogram's exact mesh boolean
(`MeshSurfaceIntersection`, B. Lévy, *Exact predicates, exact constructions and combinatorics for mesh CSG*, 2024). Before
this note, the conclusion "better than Geogram" rested on design only
([GEOGRAM_SURVEY_2026-11.md](GEOGRAM_SURVEY_2026-11.md)). **Measurement only:** no library code was changed and nothing of
Geogram was ported.

The sets are those of the CGAL comparison:
- **T6:** the 98 Thingi10K pairs of 1k-100k triangles, each with union, intersection and difference
  (`benchmarks/BooleanComparison/perf/t6.py`, roadmap T6).
- **ABC chains:** 90 chains of three booleans on ABC models with exact contact placements (`data/abc_chained.py`).

Both sets run on GitHub-hosted runners only (ubuntu-latest, 4 cores) and fetch their data at run time. No mesh is
committed.

## Result in short

| | T6, 1 thread | T6, 4 cores | ABC step 1, 1 thread | ABC step 1, 4 cores |
|---|---|---|---|---|
| median time ratio ours / Geogram | **0.36** | **0.46** | **0.24** | **0.32** |
| p90 | 0.56 | 0.74 | 0.48 | 0.70 |
| max | 0.89 | 1.22 | 0.72 | 1.13 |
| summed time ratio | 0.31 (96.7 s / 310.4 s) | 0.40 (64.5 s / 162.0 s) | – | – |
| volumes agree (≤ 1e-6 (vA + vB)) | 294 / 294 | 294 / 294 | 90 / 90 | 90 / 90 |

- **Speed.** Ours is about 2.2-4x faster at the median on single booleans of raw inputs, at 1 thread and at 4 cores.
- **Clean results.** On T6, ours gives 294 of 294 results clean or correctly empty. Geogram leaves degenerate triangles in
  42 of 294: area below 1e-12 times the squared diagonal, the same `NotCleanReason` check that gave CGAL EPECK 49 of 294.
  These come from its conversion of exact points to doubles, `PCK::approximate`, which has no repair after the rounding.
- **Failures.**
  - On the ABC chains, Geogram **fails on 46 (1 thread) and 49 (4 cores) of the about 170 steps after step 1**. 33 and 36
    of those are on inputs that pass our clean check.
  - Every failure is the same internal error: the radial sort of a bundle of triangles around an intersection edge fails,
    and `geo_assert_not_reached` follows. Geogram's own source says such a sort "may return !OK (if not using
    geogram_plus)", "you may need geogramplus, contact TESSAEL".
  - 27 of about 31 steps that place C as an ulp-shifted copy of A fail. Ours has no failure, refusal or timeout on either
    set.
- **Where ours is slower.**
  - On step 3 of the chains (the result against A again, an operand that shares A's surface), ours is slower where
    Geogram finishes. The median ratio is 1.27 at 1 thread and 1.56 at 4 cores, over 42 and 39 steps; by placement of C
    it is 1.12-2.15.
  - The inputs of step 3 differ between the two methods (each works on its own step-2 result).
  - Geogram failed on most of the hard step-3 cases, so those are not in the ratio.
  - This is the one lead for our speed work that this comparison found (see "Leads").
- **Fairness checks.**
  - **Warm-up:** Geogram's first run in a process is 1 % slower than its second (median cold/warm 1.01 on 90 operations).
    The ratios do not move (0.35 / 0.48 cold or warm).
  - **Threads:** at 1 thread Geogram ran with `maximum_concurrent_threads() = 1`, which is what its `parallel_for` uses.
  - **Excluded code:** the public Geogram build, without Triangle, TetGen and the closed geogramplus.

**Verdict:** the design-based claim holds on measurement against the public Geogram (BSD-3, `expansion_nt`):
- it is faster on single booleans;
- its results are clean, where Geogram leaves degenerate triangles in 14 % of T6 results;
- it is robust on chained near-coincident inputs, where Geogram fails on 27 % of the later chain steps.

It holds with one exception: step-3 operands that share a surface, see "Leads". Geogram's closed geogramplus (`exact_nt`;
its source claims the algorithm is "10x to 20x faster" with it) was not measured and is not part of the claim.

## Setup

- **Ours:**
  - `BooleanComparison run` on library master: T6 at 9ebeedab; ABC at ea7e40e2 (4 cores) and 4479707d (1 thread), because
    master moved between the two dispatches.
  - It times `ExactMeshBoolean.Compute` after an untimed warm-up run, as in all comparisons of this benchmark.
- **Geogram:**
  - Version 1.10.2, commit 16440c45 (the commit of the survey), built on the runner by
    `benchmarks/BooleanComparison/external/build_geogram.sh` with Geogram's own CMake, Release flags and platform
    `Linux64-gcc-dynamic`.
  - The driver `external/geogram_bool.cpp` (the repo's own code) calls
    `mesh_boolean_operation(result, A, B, "A+B" | "A*B" | "A-B", MESH_BOOL_OPS_DEFAULT)`. That is the entry point Geogram
    documents for two closed surfaces: exact intersections, radial sort, classification, and the merge and Delaunay
    retriangulation of coplanar facets.
  - Geogram's options are documented in `mesh_surface_intersection.h`:
    - delaunay on;
    - detect intersecting neighbours on;
    - radial sort on;
    - attribute interpolation off;
    - coplanar facets simplified.
  - The exact number type is `expansion_nt`, the only one in the public code.
  - Output: each new vertex gets the double `PCK::approximate` of its exact point, the way Geogram itself stores it, written
    with 17 digits. There is no other conversion and no repair.
  - Time: the boolean alone, without I/O, at microsecond resolution.
- **Threads:**
  - 4 cores: ours with all cores, Geogram with all cores (`Process::max_threads` = 4).
  - 1 thread: the whole job is pinned to core 0 (`taskset -c 0`), ours with `DOTNET_PROCESSOR_COUNT=1`, Geogram with
    `GEOGRAM_THREADS=1`. Every Geogram row records `threads 1` (its `parallel_for` sizes the thread groups by that cap).
- **Evaluation:** `BooleanComparison eval` checks every result the same way for both methods, as for CGAL: weld by exact
  position, split at pinches, `MeshRobustRepair.NotCleanReason` (degenerate triangles, not closed, non-manifold, and so on),
  self-intersections beyond the pinches, and the volume.
- **Volume agreement:** |V_ours − V_Geogram| / (vA + vB) of the operation's inputs, at most 1e-6.
- **Runs:**
  - T6 [44080501217](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/44080501217): both thread counts,
    timeout 300 s.
  - ABC [44080506292](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/44080506292) (4 cores) and
    [44080511801](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/44080511801) (1 thread): chunk 0000,
    30 chains per variant.
  - Warm-up check [44101384975](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/44101384975): the first
    30 T6 pairs, `geogram_warmup`.
  - Raw rows are on the data branches `runs/t6-cgal` and `runs/abc-chained`, folder `run-<id>`.
- **Reproduce:** run `t6-cgal.yml` with methods `ours,geogram`, and `abc-chained.yml` with methods `ours,geogram` and
  threads `all` or `1`. Tables: `perf/t6.py report --other geogram`, `data/abc_chained.py summary` and
  `data/abc_chained.py versus geogram`.

### Licence boundary

Only Geogram's own BSD-3 files are built, plus the permissive libraries its library target compiles in: amgcl, libMeshb,
rply, OpenNL, zlib, Shewchuk's public-domain predicates, PoissonRecon and xatlas. Shewchuk's Triangle and every TetGen path
are excluded three ways:

1. **Sparse, blob-less clone:** the blobs of these paths are never downloaded. A test checks this with a local stand-in
   repository (`test_geogram.py`). The excluded paths:
   - `third_party/triangle/` and `third_party/tetgen/`;
   - `delaunay/delaunay_triangle.*` and `delaunay/delaunay_tetgen.*`;
   - any other `*tetgen*` or `third_party/triangle/` path;
   - Geogram's own tet mesher `mesh/mesh_tetrahedralize.cpp`. It includes `delaunay_tetgen.h` without a condition, and the
     boolean does not use it. Nothing in the library calls it: the driver links with no undefined symbols.
2. **Build switches:** `GEOGRAM_WITH_TRIANGLE=OFF` and `GEOGRAM_WITH_TETGEN=OFF`.
3. **Checks that fail the build:** on the checkout, the build tree and the full symbol table of `libgeogram.so`. They passed
   in every run.

The binary is built and run on the runner only and is not distributed.

## T6: 98 pairs, 294 operations

**1 thread**

| bucket | pairs | ops (both ok) | median ratio | p90 | max | median ratio, both clean (n) | volumes agree | max volume diff | ours clean / empty | Geogram clean / empty | Geogram unclean |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 0.38 | 0.56 | 0.89 | 0.36 (75) | 81/81 | 3.6e-11 | 72 / 9 | 66 / 9 | degenerate triangles 6 |
| 10k-100k | 71 | 213 | 0.36 | 0.56 | 0.76 | 0.34 (177) | 213/213 | 5.1e-10 | 198 / 15 | 162 / 15 | degenerate triangles 36 |

**4 cores**

| bucket | pairs | ops (both ok) | median ratio | p90 | max | median ratio, both clean (n) | volumes agree | max volume diff | ours clean / empty | Geogram clean / empty | Geogram unclean |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 0.47 | 0.74 | 1.22 | 0.49 (75) | 81/81 | 3.6e-11 | 72 / 9 | 66 / 9 | degenerate triangles 6 |
| 10k-100k | 71 | 213 | 0.46 | 0.71 | 1.13 | 0.46 (177) | 213/213 | 5.1e-10 | 198 / 15 | 162 / 15 | degenerate triangles 36 |

- **Failures:** neither method refused, failed or timed out on an operation.
- **Degenerate triangles:** Geogram's 42 unclean results have no triangle of exactly zero area after the weld. They are
  slivers below the area bound, from rounding exact points that lie close together.
- **Worst cases:**
  - At 1 thread, ours is faster on every operation (max 0.89).
  - At 4 cores, 6 operations are above 1, all between 1.01 and 1.22: unions and differences of 5k-21k triangles. Geogram
    takes 46-186 ms there.
- **Context with CGAL:** on the same set (T6 run 43819984226), ours / CGAL EPECK is 4.2x at 1 thread and 3.6x at 4 cores
  (CGAL single-threaded).
  - Multiplying the medians, the public Geogram is roughly 12x slower than CGAL EPECK at 1 thread, and roughly 8x with
    Geogram on 4 cores.
  - This is an inference across runs on different master commits, not a measurement in one job.

## ABC chains: 90 chains of 3 steps

**Steps and failures** (from `abc_chained.py summary`; "failed": Geogram's radial sort error, see below):

| variant | method | steps run, 1 thread (step 1/2/3) | chosen result clean or empty, 1 thread | all steps clean, 1 thread / 4 cores | ended early: failed, 1 thread / 4 cores |
|---|---|---|---|---|---|
| ucd | Geogram | 30/30/25 | 27/15/4 | 3 / 3 | 5 / 5 |
| ucd | ours | 30/30/30 | 28/23/26 | 23 / 23 | 0 / 0 |
| dui | Geogram | 30/30/18 | 30/15/1 | 1 / 1 | 12 / 12 |
| dui | ours | 30/30/30 | 30/30/30 | 30 / 30 | 0 / 0 |
| iud | Geogram | 30/28/23 | 28/15/3 | 3 / 3 | 5 / 6 |
| iud | ours | 30/28/28 | 30/28/23 | 23 / 23 | 0 / 0 |

- **Unclean results of ours, unchanged from the earlier ABC runs:**
  - pinches 14: clean except where the solid touches itself, a class of its own;
  - inside-out components 3 and degenerate triangles 1 (iud): RC4 of the triage.
- **Geogram's unclean results** (1 thread): degenerate triangles 32 and not closed 24.
- **Steps that ended a chain early because the result was empty:** 2 per method (iud), the same for both.

**Time ratio and volumes per step** (`abc_chained.py versus geogram`; only steps where both results are ok):

| step | 1 thread: both ok | median | p90 | max | 4 cores: both ok | median | p90 | max | volumes agree (1 thread / 4 cores) |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 90 | 0.24 | 0.48 | 0.72 | 90 | 0.32 | 0.70 | 1.13 | 90/90 / 90/90 |
| 2 | 66 | 0.44 | 1.26 | 3.98 | 65 | 0.56 | 1.47 | 2.89 | 66/66 / 65/65 |
| 3 | 42 | 1.27 | 4.44 | 5.51 | 39 | 1.56 | 5.42 | 8.22 | 42/42 / 38/38 |
| all | 198 | 0.37 | 1.61 | 5.51 | 194 | 0.45 | 1.69 | 8.22 | 198/198 / 193/193 |

- **Same inputs:** step 1 has the same inputs for both methods. From step 2 on, each method works on its own result. The
  volumes still agree to 4e-7, so both methods reach the same solids.
- **Median ratio by the placement of C** (4 cores; 1 thread in brackets):

  | step | face-contact | partial-overlap | mirror | ulp-shift |
  |---|---|---|---|---|
  | 2 | 0.54 (0.41) | 0.46 (0.38) | 0.83 (0.63) | 1.03 (0.93), only 4-5 steps ok for Geogram |
  | 3 | 2.15 (1.65) | 1.15 (1.18) | 1.52 (1.12) | none ok for Geogram |

- **Absolute times at step 3:** median 0.9-3.4 s for ours against 0.5-2.7 s for Geogram.

**Geogram's failures**

| | 4 cores | 1 thread |
|---|---|---|
| steps failed | 49 | 46 |
| of them on inputs that pass `NotCleanReason` | 36 | 33 |
| at step 2 / 3 | 23 / 26 | 22 / 24 |
| by placement of C (failed / steps): ulp-shift | 27/31 | 27/32 |
| face-contact | 8/46 | 7/46 |
| partial-overlap | 11/54 | 9/54 |
| mirror | 3/22 | 3/22 |

- **Where it fails:** `mesh_surface_intersection.cpp` line 1572, "Did not manage to sort a bundle ... you may need
  geogramplus, contact TESSAEL", then `geo_assert_not_reached`. Some failures log "Both triangles in same reference
  half-plane" (`RadialSort`) first.
- **How it ends:**
  - At 1 thread, the driver catches the exception (status failed).
  - At 4 cores, it is thrown in a worker thread of `parallel_for` and ends the process (abort, status crash).
  - 45 of the failed steps are the same in both runs.
- **Step 1:** there is no failure at step 1 (raw inputs, face-contact or partial-overlap placements of B).

## Leads

- **Step-3 operands that share a surface.** On step 3 of the chains (`R ∩ A`, `R − A`, or the like, where R already holds
  A's surface), ours is 1.1-2.2x slower than Geogram at the median where Geogram finishes. Ours takes 0.9-3.4 s at the
  median. It is not diagnosed here. The first ABC chained run, against CGAL and Manifold (README, "Chained booleans on ABC
  models"), showed the same step-3 medians for ours (1.9-2.4 s at 4 cores), and CGAL EPECK was faster there as well
  (0.9-1.3 s). A profile of the step-3 cases (`BooleanComparison chain-step` keeps the
  inputs) is the next step, if the owner wants it.
- **Nothing to adopt from Geogram for robustness.** Its public radial sort fails on near-coincident surfaces, and it has no
  repair after rounding. That matches the survey's RC4 verdict.

## Files

- `.github/workflows/t6-cgal.yml`: inputs `methods`, `max_pairs`, `geogram_warmup`.
- `.github/workflows/abc-chained.yml`: method `geogram`, input `threads`.
- `benchmarks/BooleanComparison/external/geogram_bool.cpp` and `external/build_geogram.sh`.
- `compare.py`: method `geogram`.
- `perf/t6.py report --other`: plus the both-clean ratio, volume agreement and summed times.
- `data/abc_chained.py versus`.
- `test_geogram.py`: 17 tests, in CI.
