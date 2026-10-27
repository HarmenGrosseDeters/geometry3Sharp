# Boolean comparison

Runs `ExactMeshBoolean` and external mesh boolean implementations on the same inputs and evaluates every
result the same way. The inputs come in data classes (see below): pairs of meshes A and B, of which union,
intersection and difference are computed by every method, and sequences of booleans, of which the final
result is evaluated.

The evaluation (`BooleanComparison eval`) welds result vertices by position and reports for each method:
closedness in the winding sense, manifold closedness, exact self-intersections of the output, triangles of zero
area, volumes (measured relative to the centre of A, so that coordinates far from the origin do not cancel) and
the volume identities `V(A∪B) + V(A∩B) = V(A) + V(B)` and `V(A−B) = V(A) − V(A∩B)` (relative error); for
synthetic cases also the error against the analytically known volumes of the results.

The external code is not part of this repository; build it next to it as described below.

**Licenses:** the files in `external/` are small drivers written for this comparison. `cgal_bool.cpp` links
against CGAL's Polygon Mesh Processing, which is GPL: a compiled `cgal_bool` falls under the GPL, so it is not
distributed, and nothing of CGAL is part of the library (which stays under the Boost license). Cherchi et al.
(MIT) and Manifold (Apache-2.0) are only run as external programs as well. See also `docs/SOURCES.md`.

## Methods

| name | implementation | license | threads |
|---|---|---|---|
| `ours` | `ExactMeshBoolean` (this library) | Boost | parallel |
| `cgal_epick` | CGAL Polygon Mesh Processing corefinement, inexact constructions | GPL | 1 |
| `cgal_epeck` | the same with exact constructions | GPL | 1 |
| `cherchi` | Cherchi et al. 2022, *Interactive and Robust Mesh Booleans* | MIT | parallel |
| `manifold` | Manifold (Python `manifold3d`) | Apache-2.0 | parallel |

All tools report the time of the boolean alone (without file I/O); `wall_ms` includes process start and I/O.
CGAL refuses inputs that are open or self-intersecting; Manifold refuses non-manifold inputs.

## Data classes

| class | cases | what it tests | how to get it |
|---|---|---|---|
| `thingi10k` | Thingi10K meshes, each with a rotated, shifted copy | real-world models, many open or self-intersecting | `benchmarks/MeshRepairBenchmark/fetch_data.py`; a list or directory of meshes |
| `cgal` | 89 CGAL test and example meshes, each with a rotated, shifted copy, and the pair of CGAL's corefinement examples (`blobby`, `eight`) | CAD and scanned models, some open or degenerate | `python3 data/fetch.py cgal <dir>` (CGAL v6.0.1) |
| `cherchi` | the test meshes of Cherchi et al.: bunny with cow at 25k–200k triangles, two spheres, a cube with the armadillo, the stencil of the paper (fertility minus 703 spheres given as one mesh), five meshes with a rotated, shifted copy | larger inputs, time | `python3 data/fetch.py cherchi <dir>` (commit 7bd6c26) |
| `synthetic` | 24 pairs of boxes and spheres | coplanar faces, solids touching at a face, an edge or a vertex, overlaps and gaps of 1e-9, nearly coplanar faces, UTM coordinates (3e5, 5.6e6), coordinates of 1e8, scale differences of 1e6 | `BooleanComparison synth <dir>` |
| `sequence` | iterated cube grids: n³ overlapping unit cubes (spacing 0.9, so the faces of a layer are coplanar) united one after the other, then a cube of edge 0.5 subtracted at the centre of each (n = 5: 249 booleans, n = 10: 1999) | repeated booleans on results of booleans, exactly coplanar faces | `BooleanComparison synth <dir>`; one pass over all meshes: `BooleanComparison nary` (below) |

`data/fetch.py` downloads the meshes at a fixed commit and writes a case manifest (`cases.jsonl`); nothing of the
data is part of this repository (sources and licenses in `docs/SOURCES.md`). The `cube_grid` sequences are
modelled after the iterated cube grid of the Open Boolean Benchmark, whose cases are not published.

In a sequence, ExactMeshBoolean and Manifold keep the intermediate results in memory (both run the sequence as
an Open Boolean Benchmark request, see below), CGAL keeps them in memory as well (`cgal_bool seq`, so EPECK stays
exact between the steps), Cherchi et al. run one process per boolean and pass the results as OBJ files (its
program has no other interface).

## Open Boolean Benchmark runner

The [Open Boolean Benchmark](https://github.com/Open-Boolean-Benchmark/boolean-benchmark-runners) compares
mesh booleans of about a dozen libraries through small runner programs with a common protocol (a request JSON
with SSA-style operation lists, a result JSON with per-operation status and times). `BooleanComparison obb`
implements this protocol for ExactMeshBoolean:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll obb --request request.json --result result.json
```

It passes the smoke test of the runners repository (`tests/smoke`, cube minus cube). `obb-runner/` contains a
`runner.yaml` and `build.py` in the layout of that repository; copied there as `exactmeshboolean/<version>/`
(with `upstream.ref` set to a fixed commit), the benchmark's meta-runner can run ExactMeshBoolean next to the
other libraries. The meta-runner and the benchmark cases (Nefertiti, dental scans, the iterated cube grid) are
not published, so this comparison cannot be run here.

## Building the tools

```sh
dotnet build -c Release benchmarks/BooleanComparison

# CGAL (Ubuntu: apt install libcgal-dev libgmp-dev libmpfr-dev)
g++ -O2 -std=c++17 -DNDEBUG -DCGAL_NDEBUG external/cgal_bool.cpp -o cgal_bool -lgmp -lmpfr

# Cherchi et al.: clone https://github.com/gcherchi/InteractiveAndRobustMeshBooleans and build it with CMake
# (Release). To get the time of the boolean alone, wrap booleanPipeline(...) in main.cpp with a timer that
# prints {"ms":<milliseconds>} to stderr.

# Manifold
python3 -m venv env && env/bin/pip install manifold3d numpy
```

## Running

```sh
# copy the built tool, so that rebuilding the library during a run does not mix versions
cp -r bin/Release/net10.0 /some/where/frozen
export BOOLCMP_TOOL=/some/where/frozen/BooleanComparison.dll
export CGAL_BOOL=/path/to/cgal_bool
export CHERCHI_BOOL=/path/to/InteractiveAndRobustMeshBooleans/build/mesh_booleans
export MANIFOLD_PYTHON=/path/to/env/bin/python

python3 compare.py meshes.txt results.jsonl --timeout 300 -j 1        # a list of mesh files or a directory (class thingi10k)
python3 data/fetch.py cgal data_cgal && python3 compare.py data_cgal/cases.jsonl results.jsonl
python3 data/fetch.py cherchi data_cherchi && python3 compare.py data_cherchi/cases.jsonl results.jsonl
dotnet $BOOLCMP_TOOL synth synth && python3 compare.py synth/cases.jsonl results.jsonl --timeout 900
python3 analyze.py results.jsonl                                      # Markdown tables per data class
```

Use `-j 1` for timings: several methods are multi-threaded. `--timeout` is per boolean, or per sequence.
`--only name,...` runs single cases of a manifest.

Each run gets a status: `ok`, `rejected` (the tool refuses the input), `failed` (the tool reports a failure on an
input it accepts), `crash` (signal, exception or exit code without a report) or `timeout`. `analyze.py` combines
these with the evaluation into one outcome per case and method (see the comment at its top), per data class, on
the cases that every method accepts, and gives the time on the cases that every method solves correctly.

A single run of our method, with phase times, counts and GC statistics:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll export mesh.stl dir      # writes dir/A.off, dir/B.off
dotnet bin/Release/net10.0/BooleanComparison.dll run dir/A.off dir/B.off union out.off
```

A profile of our method over many cases (phase times and allocations, GC, counts, a hash of each result for
before/after comparisons; see [Profile and performance work](#profile-and-performance-work-october-2026)):

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll profile --repeat 5 cases.txt grid:10   # cases.txt: one case directory per line
dotnet build -c Release -p:G3PredicateStats=true     # a build that also counts the filter failures of the predicates
```

## Results per data class

Run on 4 cores, `-j 1`, timeout 300 s per boolean (900 s per sequence); ExactMeshBoolean as of this branch
(master with the faster exact booleans). The times of ExactMeshBoolean below are from before the performance work
of October 2026 (see [Profile and performance work](#profile-and-performance-work-october-2026)), which made a
boolean measured the same way (one process per boolean, as `compare.py` runs it) 2.4-2.8 times faster on the
`cgal` and Thingi10K inputs and 1.3-2.3 times faster on the `cherchi` inputs; the other methods were not rerun. Outcomes per case and method over all its operations, as defined at
the top of `analyze.py`: **clean** (correct, closed manifold, no self-intersections), **self-int.** (correct,
but self-intersections after rounding to double or where solids touch), **volume** (closed, but a volume
identity or a known volume off by more than 1e-6), **not closed**, **failed** (the tool reports a failure on an
input it accepts), **timeout**, **crash**, **rejected** (the tool refuses the input). Times are the sum of the
three operations (boolean only, without I/O), on the cases that every method solves correctly.

### Summary

| class | cases | where ExactMeshBoolean stands |
|---|---|---|
| `cgal` | 90 | as good as CGAL and Manifold on the 50 inputs all accept; the only method without rejections (CGAL refuses 40, Manifold 33); 9× slower than CGAL, 11× slower than Manifold (median per case) |
| `cherchi` | 13 | all methods clean; 5× slower than CGAL, 10× slower than Manifold, 3× slower than Cherchi et al. (median per case; on the largest cases 2–3× slower than CGAL and Cherchi et al.) |
| `synthetic` | 24 | clean everywhere except where the correct result touches itself (as Manifold); CGAL refuses the union of boxes touching at an edge; Cherchi et al. leaves coplanar and touching cases open |
| `sequence` | 2 | 249 booleans clean (6.3 s, Manifold 0.2 s); 1999 booleans **time out**: the result fragments (see below) |

### `cgal` (90 cases, 50 with closed inputs without self-intersections)

| method | clean | self-int. | volume | not closed | rejected |
|---|---|---|---|---|---|
| ExactMeshBoolean | 62 | 3 | – | 25¹ | – |
| CGAL EPECK | 47 | 3 | – | – | 40 |
| CGAL EPICK | 47 | 3 | – | – | 40 |
| Manifold | 49 | 4 | 4 | – | 33 |
| Cherchi et al. | 27 | 25 | 3 | 35 | – |

On the 50 cases that every method accepts:

| method | clean | self-int. | not closed |
|---|---|---|---|
| ExactMeshBoolean | 48 | 2² | – |
| CGAL EPECK / EPICK | 47 | 3 | – |
| Manifold | 49 | 1 | – |
| Cherchi et al. | 27 | 22 | 1 |

Time on the 49 cases that every method solves correctly: ExactMeshBoolean median 532 ms (total 49 s), CGAL EPECK
62 ms (9.0 s), EPICK 68 ms (8.8 s), Cherchi et al. 113 ms (19 s), Manifold 41 ms (4.4 s).

¹ open inputs: by design (generalized winding number). ² `cheese` and `u_arch`: the exact methods (ours, CGAL
EPECK, Cherchi et al.) all leave a few self-intersections after rounding the new vertices to double; Manifold's
results are clean.

### `cherchi` (13 cases, all inputs closed without self-intersections)

All five methods give clean results on all 13 cases. Time: ExactMeshBoolean median 3631 ms (total 58 s), CGAL
EPECK 645 ms (16 s), EPICK 682 ms (16 s), Cherchi et al. 1363 ms (23 s), Manifold 304 ms (5.9 s). The gap shrinks
with size: bunny 200k + cow 200k takes us 6.8 s (CGAL EPECK 2.2 s, Cherchi et al. 3.0 s, Manifold 0.9 s), the
stencil (fertility minus 703 spheres) 15 s (CGAL EPECK 6.3 s, Cherchi et al. 6.4 s, Manifold 1.1 s).

### `synthetic` (24 cases)

| method | clean | self-int. | volume | not closed | failed |
|---|---|---|---|---|---|
| ExactMeshBoolean | 23 | 1 | – | – | – |
| CGAL EPECK / EPICK | 23 | – | – | – | 1 |
| Manifold | 23 | 1 | – | – | – |
| Cherchi et al. | 10 | 2 | 2 | 10 | – |

| case | ExactMeshBoolean | CGAL EPECK / EPICK | Manifold | Cherchi et al. |
|---|---|---|---|---|
| coplanar faces (5 cases: shifted, half box, rotated 45°, meshed faces) | clean | clean | clean | **not closed** |
| identical box, identical sphere | clean | clean | clean | **volume** |
| touching at a face (full, partial), nested touching a face | clean | clean | clean | **not closed** |
| touching at an edge | self-int.³ | failed (union: "output not manifold") | self-int.³ | self-int. |
| touching at a vertex | clean | clean | clean | clean |
| UTM buildings (axis-aligned, rotated, same ground plane) | clean | clean | clean | **not closed** |
| sphere and its rotated copy (same centre) | clean | clean | clean | self-int. |
| overlap / gap of 1e-9, nearly coplanar (1e-6°), sliver (0.01°), UTM spheres, coordinates 1e8, scale 1e3 vs 1e-3, scale 1e-6, generic spheres | clean | clean | clean | clean |

³ the union of two boxes touching at an edge is non-manifold there; welded by position, the two boxes touch.

### `sequence` (iterated cube grids)

| case | ExactMeshBoolean | CGAL EPECK | CGAL EPICK | Manifold | Cherchi et al. |
|---|---|---|---|---|---|
| 5×5×5, 249 booleans | clean, 6.3 s | self-int.⁴, 2.1 s | failed after 5 | clean, 0.2 s | crash after 7 |
| 10×10×10, 1999 booleans | **timeout** after 47 | self-int.⁴, 64 s | failed after 10 | clean, 7.5 s | crash after 12 |

EPICK throws "Unauthorized intersections of constraints" on the coplanar faces; Cherchi et al. crash (SIGSEGV)
on intermediate results passed as files. ⁴ EPECK stays exact in memory, but its final result, rounded to double,
has duplicated points, triangles of zero area (432 for n = 10) and self-intersections.

ExactMeshBoolean fragments coplanar regions: where faces of the two inputs overlap in a plane, the result keeps
the subdivision of both (eg points where the diagonals of the overlapping side faces cross), and after rounding
to double these points lie slightly off the lines they were on. The next boolean intersects them again, so the
results grow geometrically (after 29, 33, 37, 41 unions of the 10×10×10 grid: 1364, 2342, 6002, 21936
triangles; 28 s for the 46th boolean). The results stay correct (closed, exact volume). A fix (simplifying flat
regions of the result, or snap rounding) belongs in `ExactMeshBoolean` itself.

### Many meshes in one operation

`ExactMeshBoolean` takes any number of meshes into one arrangement, with an operation or a predicate on their
winding numbers (n-ary union / intersection / difference, "inside at least k of n", ...). `BooleanComparison nary`
compares this with the chain of pairwise booleans:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll nary grid 10                     # chained, onepass, twostep
dotnet bin/Release/net10.0/BooleanComparison.dll nary spheres 100 chained onepass
```

`grid n` is the `sequence` case: n³ cubes united, minus a void in each cube. `onepass` is one ExactMeshBoolean
over all 2n³ meshes with the predicate "inside a cube and in no void", `twostep` the n-ary union of the cubes and
then the n-ary difference of that union and all voids. `spheres k` is the union of k random overlapping spheres
(1536 triangles each). Before timing, the requested variants run untimed on a small case of the same kind for
3 s (warm-up, see below). Run on 4 cores, booleans only (without building the inputs):

| case | inputs | chained (pairwise booleans) | one pass | two steps | result |
|---|---|---|---|---|---|
| grid 3 | 54 | 0.56 s (53) | 0.14 s | 0.21 s | identical: 624 triangles, exact volume, clean |
| grid 5 | 250 | 3.2 s (249) | 1.3 s | 1.5 s | identical: 2472 triangles, exact volume, clean |
| grid 10 | 2000 | 124 s (1999) | 15.3 s | 15.8 s | identical: 16332 triangles, exact volume, clean |
| spheres 20 | 20 | 1.8 s (19) | 0.73 s | – | same volume, clean; 30576 instead of 30864 triangles |
| spheres 100 | 100 | 31 s (99) | 5.7 s | – | same volume, clean; 110934 instead of 113774 triangles |

One pass is 2.5–8× faster: every input triangle is intersected and retriangulated once, instead of the growing
intermediate result again in every step. Only meshes whose boxes overlap are intersected, and a patch gets a
winding number ray only for the meshes whose box contains it, so far-apart inputs cost little. The one-pass
result rounds the new vertices once; the chain keeps rounded vertices of intermediate results (more triangles on
curved inputs). For grid 10 the time goes to the merge of intersection points (6.3 s), the classification of
42048 patches (4.0 s), the triangulation (2.6 s) and the intersections (2.3 s).

**Warm-up.** A variant run alone in a cold process used to take much longer than in the table: grid 5 one pass
2.8–3.6 s alone, 1.6 s after the chained variant. This was not a regression of the library: the PR head of the
n-ary booleans (24fc5cd), its merge (edb069f) and master at 7f0f97c all take 2.8–3.6 s cold and 1.5–2.0 s in the
second or third run in the same process. The difference is mostly JIT compilation (tiered: optimized code only
after enough calls; `DOTNET_TieredCompilation=0`: 2.1 s cold), partly growth of the GC heap (server GC,
`DOTNET_gcServer=1`, which the benchmark does not use: 1.0–1.4 s warm). The earlier table was measured with all
variants in one process, so one pass came after the chained run. Now every run warms up on a small case first
(this covers the JIT, the heap only partly); grid 5 one pass alone: 1.6–1.9 s (before: 2.8–3.6 s).

## Results (80 Thingi10K meshes)

Each mesh A with a rotated, shifted copy B; union, intersection and difference. Outcomes per mesh and method
(all three operations): **rejected** (the tool refuses the input: CGAL needs closed polygon meshes without
self-intersections, Manifold manifold ones), **crash**, **timeout** (300 s per operation), **not closed**,
**volume** (closed, but a volume identity is off by more than 1e-6), **self-int.** (correct, but the output has
self-intersections after rounding to double), **clean** (correct, closed manifold, no self-intersections).

Inputs that all methods accept (38: closed, manifold, no self-intersections):

| method | clean | self-int. | other |
|---|---|---|---|
| ExactMeshBoolean | 38 | 0 | – |
| CGAL EPECK (exact constructions) | 38 | 0 | – |
| CGAL EPICK (inexact constructions) | 38 | 0 | – |
| Manifold | 38 | 0 | – |
| Cherchi et al. | 3 | 35 | – |

On the common inputs ExactMeshBoolean is as good as CGAL and Manifold, not better: every result of all four is
clean (EPECK and EPICK alike; there is no EPECK failure to trace back to the rounding of its output). The
difference is coverage, on the inputs that other tools refuse:

| inputs | method | clean | self-int. | volume | not closed | crash / timeout | rejected |
|---|---|---|---|---|---|---|---|
| closed, self-intersecting or non-manifold (20) | ExactMeshBoolean | 8 | 9 | 3¹ | – | – | – |
| | CGAL EPECK / EPICK | – | – | – | – | – | 20 |
| | Manifold | 4 | 6 | 7 | – | – | 3 |
| | Cherchi et al. | – | 4 | – | 6 | 10 | – |
| open (22) | ExactMeshBoolean | 1 | 2 | – | 19² | – | – |
| | CGAL EPECK / EPICK | – | – | – | – | – | 22 |
| | Manifold | – | – | – | – | – | 22 |
| | Cherchi et al. | – | – | – | 12 | 10 | – |

¹ inputs whose solid has (almost) no volume (flat or sheet-like, volume 0 or 1e-12): the relative volume error
is meaningless there; the results are closed. ² by design: open inputs use the generalized winding number,
whose solid is in general not bounded by a closed surface.

Self-intersections in our results on self-intersecting inputs come mostly from rounding the new vertices to
double and from solids that touch (the touching sheets are kept apart only by duplicated vertices); see
`MeshRobustRepair.SeparateTouchingVertices` for how the repair handles the latter.

Time (union + intersection + difference, boolean only, median on the 38 common inputs): ExactMeshBoolean 994 ms,
Cherchi et al. 264 ms, CGAL EPICK 110 ms, EPECK 110 ms, Manifold 57 ms. ExactMeshBoolean, Cherchi and Manifold
use all cores, CGAL one.

## Profile and performance work (October 2026)

`BooleanComparison profile` (`Profile.cs`) runs ExactMeshBoolean on a list of cases and writes one JSON line per
case and operation: the time of each run, the phase times and the bytes allocated per phase (`PhaseClock`), GC
collections and pause time, the counts of the boolean, and a hash of the result mesh (with `--exact` also of the
exact result vertices). A library built with `-p:G3PredicateStats=true` also counts, per predicate, the calls and
how often its floating-point filter does not decide (`PredicateStats`; in a normal build the counting calls are
removed by the compiler). Inputs:

- `cherchi`: the 13 cases of the class;
- `cgal`: the 50 cases with closed inputs without self-intersections;
- Thingi10K: the 32 of the 40 clean controls of `MeshRepairBenchmark`'s selection (`fetch_data.py`) whose
  exported pair is closed and free of self-intersections (each with a rotated, shifted copy, as `export` writes it);
- n-ary: `grid:5`, `grid:10` (one pass) and `spheres:20` of `nary`.

All on 4 cores, union, intersection and difference. **Warm**: 5 runs in one process after a warm-up, the fastest.
**Cold**: one process per boolean, as `compare.py` runs it (`run`: a short warm-up on two small spheres, then the
boolean). Times per case are the sums of the three operations.

### Where the time went (master, warm)

| class | runs | median / case | setup | trees | pair search | planes | tri-tri | merge | triangulation | classification | assembly | (simplify) | GC pause | alloc / run |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| cherchi | 39 | 1208 ms | 8 % | 7 % | 21 % | 6 % | 3 % | 4 % | 14 % | 10 % | 26 % | 7 % | 34 % | 467 MB |
| cgal | 150 | 67 ms | 4 % | 6 % | 22 % | 3 % | 5 % | 12 % | 11 % | 9 % | 22 % | 5 % | 15 % | 61 MB |
| thingi | 96 | 120 ms | 3 % | 4 % | 21 % | 6 % | 10 % | 16 % | 13 % | 8 % | 15 % | 4 % | 23 % | 91 MB |
| n-ary | 3 | | 0 % | 0 % | 1 % | 0 % | 12 % | 41 % | 20 % | 25 % | 1 % | 0 % | 25 % | 5.3 GB |

Phases as shares of the total time (they include the GC pauses that fall into them; simplify is part of assembly).
Pair search: the box tree queries and the exact tests that discard pairs of triangles that cannot intersect (most
of them neighbours in the same mesh: self-intersections are computed too); tri-tri: the intersection of the
remaining pairs; merge: the merge of coincident points and the crossings of segments in a triangle.

- **No phase dominates on the real-world inputs.** The pair search is the largest single phase (21-22 %), then the
  assembly of the result mesh (15-26 %). The exact arithmetic is not the bottleneck there: most predicates are
  decided by their filters (below).
- **GC pauses take 15-34 % of the time**, most on the large inputs (467 MB allocated per boolean of `cherchi`).
- **The n-ary grid is dominated by exact arithmetic**: coincident points from different constructions (merge 41 %),
  exactly cocircular points in the triangulation (74 % of its incircle tests need the exact stage), and the ray
  tests of the 42048 patches. Grid 10: 13.7 s, of which merge 5.8 s, classification 3.3 s, triangulation 2.6 s.
- **JIT.** A cold boolean took 3-9 times as long as a warm one (`cgal` median 602 ms cold, 67 ms warm). With tiered
  compilation, methods with loops start as unoptimized code and are replaced only after many iterations (on-stack
  replacement), and the optimized versions arrive after a call-counting delay that keeps being extended while new
  methods are compiled. `DOTNET_TieredCompilation=0` made a cold boolean of `cgal/elephant` 3.3 times faster
  (367 -> 114 ms), `DOTNET_TC_QuickJitForLoops=0` almost as much.

Filter failures per predicate (master; calls over all runs of the class, and the share that needed the exact stage):

| predicate | cherchi | cgal | thingi | n-ary |
|---|---|---|---|---|
| `ExactPredicates.Orient3d` (double input: pair search, tri-tri) | 120 M, 0.0 % | 67 M, 11.6 % | 85 M, 8.2 % | 8.4 M, 31 % |
| `ExactPredicates.Orient2d` (degenerate triangles, coplanar tests) | 11 M, 0.3 % | 39 M, 10.6 % | 34 M, 7.2 % | 0.2 M, 27 % |
| arrangement `Orient` (implicit points) | 3.9 M, 11.8 % | 2.1 M, 11.1 % | 2.4 M, 10.0 % | 18.5 M, 20.8 % |
| arrangement `InCircle` | 5.0 M, 0.0 % | 2.6 M, 0.2 % | 2.8 M, 0.0 % | 1.9 M, 74.1 % |
| arrangement `compare_along` | 3.7 M, 0.0 % | 2.5 M, 0.0 % | 4.7 M, 0.0 % | 5.7 M, 13.6 % |
| arrangement `ComparePoints` (order of points, merge) | 1.6 M, 0.4 % | 1.6 M, 29.6 % | 3.0 M, 43.9 % | 25 M, 21.9 % |
| winding ray: plane side | 0.18 M, 0.0 % | 0.04 M, 1.5 % | 0.08 M, 3.2 % | 0.73 M, 22.2 % |
| winding ray: `orient3d` | 0.24 M, 0.0 % | 0.04 M, 0.0 % | 0.09 M, 0.0 % | 0.67 M, 1.5 % |

The exact stages are frequent where the input has exact coplanarity (CAD models in `cgal`, many Thingi10K models:
flat regions, axis-aligned faces, the copies' faces) and where coincident points are compared (`ComparePoints` in
the merge). The 11-12 % of the arrangement's `Orient` are mostly exactly collinear points (points on the edges of
the triangle they split).

Parallel scaling (master, warm, sums over the cases; `taskset` and `DOTNET_PROCESSOR_COUNT`):

| cases | 1 core | 2 cores | 4 cores | 4 / 1 |
|---|---|---|---|---|
| 5 smaller `cherchi` cases | 8.2 s | 5.5 s | 4.7 s | 1.75 |
| 12 Thingi10K cases | 8.9 s | 6.3 s | 4.9 s | 1.82 |
| grid 5 | 2.3 s | 1.7 s | 1.3 s | 1.74 |
| bunny 200k + cow 200k | 7.0 s | 5.3 s | 4.5 s | 1.55 |

The serial parts (classification, assembly, building the points) and the GC pauses limit the scaling.

### What changed

All changes keep the results bit-identical: the same triangles, vertex coordinates and ids, the same exact vertices,
the same status, on every case above (hashes of the result mesh, and with `--exact` of the exact vertices, compared
with master: 0 differences over all warm runs and 263 exact runs), on 1, 2 and 4 cores, and in repeated runs.

- **JIT** (`core/JitHints.cs`): the methods of the boolean pipeline (and the append path of `DMesh3`) are marked
  `AggressiveOptimization`, so they are compiled optimized at their first call. This is the largest gain for single
  booleans; it does not change the generated arithmetic (no contraction to FMA).
- **Merge of coincident points**: the points of a cluster are grouped by their exact (canonical) point instead of
  sorted with `ComparePoints`, so each exact point is computed once, not in every comparison.
- **Exact stages of the arrangement's `Orient` and `InCircle`**: integer points whose coordinates are doubles go
  through the adaptive double predicates (`ExactPredicates`); `InCircle` is a 3x3 integer determinant of differences
  instead of a 4x4 one in a `BigInteger[,]` (the derivation is in the code).
- **Pair search**: the box tree on arrays, queries into a reused list (no closure, stack or delegate per query), the
  trees of the meshes built in parallel, `Parallel.For` over index ranges instead of `Parallel.ForEach` over an
  enumerable (`gParallel.ForRange`).
- **Setup**: input triangles built in parallel; the degenerate test starts with the projection along the largest
  normal component (the others are degenerate for triangles in coordinate planes and needed the exact stage).
- **Classification**: the winding numbers of the patches are computed in parallel, each patch with a random number
  generator of its own (the winding numbers are exact; the rays only change the diagnostic counts
  `winding_plane_sides`); the stacks of coplanar pieces in parallel.
- **Assembly**: arrays per point id instead of dictionaries and hash sets; the incidence lists of
  `ExactPlanarSimplify` as arrays, lists only for the points that change; no checks before `AppendTriangle` that
  cannot fail (no edge with more than two pieces and no conflicting pieces); `DMesh3.FindEdge` without an iterator.
- **Allocations**: planes of the input triangles in an object created only for the triangles that need them,
  directions of segment lines rounded to double once, smaller point index shards; the ids of `DMesh3` (and
  `NTMesh3`, `DGraph*`) enumerated without boxing each id (`RefCountVector` had only a non-generic enumerator);
  presized lists; `ExactTriangulation2` reuses the arrays of removed triangles.
- **Unoptimized runtime code**: the edges are grouped with a radix sort of our own (stable) instead of
  `Array.Sort(long[], int[])`, whose generic code took about 14 % of a cold boolean's main thread; loop bodies of the
  parallel phases are methods with the JIT hint instead of lambdas.
- **Coplanar triangles share their constructions**: triangles in the same plane (exactly; candidates found by a
  rounded key of the plane's approximation) get one plane key, so an edge crossing that plane is one implicit point,
  not one per triangle. In grid 10, 95 % of the points were such duplicates: 656k points -> 143k, merge 2.6 -> 1.0 s.

### After (warm)

| class | median / case before | after | speedup (median of cases) | total before | after | GC pause | alloc / run |
|---|---|---|---|---|---|---|---|
| cherchi | 1208 ms | 685 ms | 1.61 | 32.1 s | 22.0 s | 34 % -> 28 % | 467 -> 322 MB |
| cgal | 67 ms | 56 ms | 1.21 | 12.5 s | 9.3 s | 15 % -> 9 % | 61 -> 41 MB |
| thingi | 120 ms | 106 ms | 1.20 | 12.4 s | 8.9 s | 23 % -> 9 % | 91 -> 62 MB |

| n-ary case | before | after | merge | triangulation | classification |
|---|---|---|---|---|---|
| grid 5 | 1278 ms | 746 ms | 365 -> 139 ms | 464 -> 258 ms | 305 -> 250 ms |
| grid 10 | 13.7 s | 5.7 s | 5.8 -> 1.0 s | 2.6 -> 1.7 s | 3.3 -> 1.9 s |
| spheres 20 | 531 ms | 375 ms | 140 -> 21 ms | 57 -> 161 ms | 167 -> 36 ms |

(The class rows are from the state before the shared constructions of coplanar triangles, which changes nothing
measurable outside the n-ary grid: on 17 cases of the three classes 3.96 / 3.97 s before, 3.97 / 3.82 s after. The
n-ary rows include it. spheres 20: the GC pauses fall into different phases; the total went down.)

### After (cold, one process per boolean)

| class | median / case before | after | speedup (median of cases) | total before | after |
|---|---|---|---|---|---|
| cgal (50 cases) | 602 ms | 199 ms | 2.84 | 56.1 s | 23.1 s |
| Thingi10K (32 cases) | 917 ms | 325 ms | 2.41 | 49.7 s | 22.1 s |
| cherchi, 5 smaller cases | 2166 ms | 846 ms | 2.30 | 14.4 s | 6.1 s |
| bunny 200k + cow 200k | 7.9 s | 4.7 s | 1.69 | | |
| stencil (fertility minus 703 spheres) | 16.7 s | 13.3 s | 1.26 | | |

Parallel scaling after (warm, the cases of the table above): 5 `cherchi` cases 6.1 / 3.9 / 2.9 s on 1 / 2 / 4
cores (2.08), 12 Thingi10K cases 6.9 / 4.6 / 3.3 s (2.09), grid 5 1.47 / 1.01 / 0.73 s (2.01), bunny 200k +
cow 200k 4.8 / 3.6 / 2.8 s (1.68).

**Cold runs and the application's JIT settings.** A cold boolean still takes about 3 times as long as a warm one:
the runtime's generic collections over the library's value types (dictionaries of point keys, lists of segments)
start as unoptimized code. ReadyToRun does not help: the tool published with `PublishReadyToRun` (the library
precompiled) took the same time (`cgal/elephant` 139 against 144 ms, Thingi10K 79241 1099 against 791 ms). The
methods with the JIT hint are not precompiled by it, and the generic code of the runtime over the library's types is
not part of the library's image. What helps is a setting of the application that runs single booleans:
`<TieredCompilation>false</TieredCompilation>` in its project (or `DOTNET_TieredCompilation=0`) made the same cold
booleans 1.4-1.9 times faster (`cgal/elephant` 144 -> 75 ms, Thingi10K 79241 791 -> 552 ms); `<TieredPGO>false</TieredPGO>`
is a milder version. Long-running applications (many booleans in one process) do not need it.

### SIMD spike (not merged)

The floating-point filters are a small part of the time: in a sampling profile of the warm `cgal` and Thingi10K runs,
`ExactPredicates.Orient3d` (whose body is its filter) takes about 10 % of the samples, almost all of it in the
pair search; the box tests and the arrangement's filters are below 1 % each. The spike (branch
`claude/boolean-simd-spike`) filters the three corners of a triangle against the plane of another one in the lanes
of a `Vector256<double>` (`ExactPredicates.Orient3d3`): the same IEEE operations in the same order per lane as
`Orient3d`, no FMA (checked in the disassembly: separate `vmulpd` / `vaddpd`), no reductions across lanes, the exact
stage per point as before. On 17 cases (8 Thingi10K, 8 `cgal`, bunny 50k + cow 50k; union and difference, warm,
two rounds): pair search 842 / 885 ms -> 744 / 782 ms, total 4117 / 4176 ms -> 3979 / 4136 ms, about 2 %, within the
noise of the runs; results bit-identical. The same batching without SIMD (scalar, the plane terms computed once) was
slower than the original (pair search 977 / 1009 ms). Not worth a second code path per platform, so the determinism
rules for SIMD paths (and a switch to force the scalar path) are not needed.

### What is left

- **GC pauses** (9-28 %): the input triangles are objects (with an array of corners each), and the exact stages
  allocate `BigInteger`s.
- **Pair search** (15-26 %): mostly the exact tests of neighbouring triangles of the same mesh (needed to find
  self-intersections), with exact stages where neighbours are coplanar.
- **The n-ary grid**: still dominated by `BigInteger` arithmetic of the remaining coincident points (other kinds of
  constructions of the same point) and of cocircular points in the triangulation.
- **Cold runs** still take about 3 times as long as warm ones (see above; an application setting, not the library).
