# Boolean comparison

Runs `ExactMeshBoolean` and external mesh boolean implementations on the same inputs and evaluates every
result the same way. Each input mesh A is paired with a rotated, shifted copy B; union, intersection and
difference are computed by every method.

The evaluation (`BooleanComparison eval`) welds result vertices by position and reports for each method:
closedness in the winding sense, manifold closedness, exact self-intersections of the output, volumes and the
volume identities `V(A∪B) + V(A∩B) = V(A) + V(B)` and `V(A−B) = V(A) − V(A∩B)` (relative error).

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

python3 compare.py meshes.txt results.jsonl --timeout 300 -j 1   # a list of mesh files, or a directory
python3 analyze.py results.jsonl
```

Use `-j 1` for timings: several methods are multi-threaded. The input meshes can be the Thingi10K selection
of `benchmarks/MeshRepairBenchmark`.

A single run of our method, with phase times, counts and GC statistics:

```sh
dotnet bin/Release/net10.0/BooleanComparison.dll export mesh.stl dir      # writes dir/A.off, dir/B.off
dotnet bin/Release/net10.0/BooleanComparison.dll run dir/A.off dir/B.off union out.off
```

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
